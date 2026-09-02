[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $AssetsRoot,

    [Parameter(Mandatory = $true)]
    [string] $SongDataPath,

    [string] $OutputDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'themes\bocchi')
)

$ErrorActionPreference = 'Stop'

function Convert-ToSafeFileName([string] $Value) {
    return $Value -replace '[\\/:*?"<>|]', '_'
}

function Convert-ToHexColor([string] $Value) {
    if ($Value -match '^#[0-9a-fA-F]{6}([0-9a-fA-F]{2})?$') {
        return $Value.ToUpperInvariant()
    }

    if ($Value -notmatch '^rgba\(\s*(\d{1,3})\s*,\s*(\d{1,3})\s*,\s*(\d{1,3})\s*,\s*((?:0?\.\d+)|0|1(?:\.0+)?)\s*\)$') {
        throw "不支持的颜色格式：$Value"
    }

    $red = [int]$Matches[1]
    $green = [int]$Matches[2]
    $blue = [int]$Matches[3]
    $alpha = [double]::Parse($Matches[4], [Globalization.CultureInfo]::InvariantCulture)
    if ($red -gt 255 -or $green -gt 255 -or $blue -gt 255) {
        throw "颜色分量超出范围：$Value"
    }

    $alphaByte = [int][Math]::Round($alpha * 255, [MidpointRounding]::AwayFromZero)
    return '#{0:X2}{1:X2}{2:X2}{3:X2}' -f $red, $green, $blue, $alphaByte
}

function Assert-Asset([string] $RelativePath) {
    $resolved = Join-Path $resolvedAssets ($RelativePath.Replace('/', '\'))
    if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) {
        throw "缺少主题素材：$RelativePath"
    }
}

$resolvedAssets = [IO.Path]::GetFullPath($AssetsRoot)
$resolvedSongData = [IO.Path]::GetFullPath($SongDataPath)
$resolvedOutput = [IO.Path]::GetFullPath($OutputDirectory)
if (-not (Test-Path -LiteralPath $resolvedAssets -PathType Container)) {
    throw "素材目录不存在：$resolvedAssets"
}
if (-not (Test-Path -LiteralPath (Join-Path $resolvedAssets 'songs') -PathType Container)) {
    throw "素材目录缺少 assets/songs：$resolvedAssets"
}
if (-not (Test-Path -LiteralPath $resolvedSongData -PathType Leaf)) {
    throw "SongData.json 不存在：$resolvedSongData"
}

$sourceSongs = @(Get-Content -Raw -LiteralPath $resolvedSongData | ConvertFrom-Json)
if ($sourceSongs.Count -eq 0) {
    throw 'SongData.json 没有歌曲。'
}

$generatedSongs = @(
    foreach ($song in $sourceSongs) {
        if ([string]::IsNullOrWhiteSpace($song.name)) {
            throw 'SongData.json 中存在缺少 name 的歌曲。'
        }

        $safeName = Convert-ToSafeFileName ([string]$song.name)
        $audioExtension = if ($song.audioType) { [string]$song.audioType } else { '.flac' }
        $coverName = if ($song.single) {
            [string]$song.single
        }
        elseif ($song.album) {
            [string]$song.album
        }
        else {
            [string]$song.name
        }
        $safeCoverName = Convert-ToSafeFileName $coverName

        $audio = "songs/$safeName$audioExtension"
        $cover = "covers/$safeCoverName.jpg"
        $original = "lyrics/original/$safeName.lrc"
        $romanized = "lyrics/romanized/$safeName.lrc"
        Assert-Asset $audio
        Assert-Asset $cover
        Assert-Asset $original
        Assert-Asset $romanized

        [ordered]@{
            title = if ($song.nameOriginal) { [string]$song.nameOriginal } else { [string]$song.name }
            artist = '結束バンド'
            audio = $audio
            cover = $cover
            lyrics = [ordered]@{
                original = $original
                romanized = $romanized
            }
            backgroundColor = ([string]$song.backgroundColor).ToUpperInvariant()
            textColor = '#FFFFFF'
            accentColor = Convert-ToHexColor ([string]$song.lineColor)
        }
    }
)

New-Item -ItemType Directory -Force -Path $resolvedOutput | Out-Null
$pack = [ordered]@{
    format = 1
    id = 'bocchi'
    name = '孤独摇滚'
    author = 'OriginalCube / XY桌面播放器移植'
    appearance = [ordered]@{
        textColor = '#FFFFFF'
        accentColor = '#ED709AE6'
    }
}
$pack | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $resolvedOutput 'pack.json') -Encoding utf8
$generatedSongs | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $resolvedOutput 'songs.json') -Encoding utf8

Write-Host "内置主题定义已生成：$resolvedOutput"
Write-Host "歌曲：$($generatedSongs.Count)"
