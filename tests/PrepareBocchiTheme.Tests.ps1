[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("XYDesktopPlayer-BocchiTheme-" + [Guid]::NewGuid().ToString('N'))
$assetsRoot = Join-Path $testRoot 'assets'
$outputRoot = Join-Path $testRoot 'theme'
$songDataPath = Join-Path $testRoot 'SongData.json'

try {
    New-Item -ItemType Directory -Force -Path @(
        (Join-Path $assetsRoot 'songs'),
        (Join-Path $assetsRoot 'covers'),
        (Join-Path $assetsRoot 'lyrics\original'),
        (Join-Path $assetsRoot 'lyrics\romanized')
    ) | Out-Null
    Set-Content -LiteralPath (Join-Path $assetsRoot 'songs\Example Song.flac') -Value 'audio' -NoNewline
    Set-Content -LiteralPath (Join-Path $assetsRoot 'covers\Example Album.jpg') -Value 'cover' -NoNewline
    Set-Content -LiteralPath (Join-Path $assetsRoot 'lyrics\original\Example Song.lrc') -Value '[00:00.00]original' -NoNewline
    Set-Content -LiteralPath (Join-Path $assetsRoot 'lyrics\romanized\Example Song.lrc') -Value '[00:00.00]romanized' -NoNewline
    @(
        [ordered]@{
            id = 1
            name = 'Example Song'
            nameOriginal = '示例歌曲'
            album = 'Example Album'
            backgroundColor = '#112233'
            lineColor = 'rgba(10, 20, 30, .9)'
        }
    ) | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $songDataPath -Encoding utf8

    & (Join-Path $repoRoot 'tools\Prepare-BocchiTheme.ps1') `
        -AssetsRoot $assetsRoot `
        -SongDataPath $songDataPath `
        -OutputDirectory $outputRoot

    $songs = @(Get-Content -Raw -LiteralPath (Join-Path $outputRoot 'songs.json') | ConvertFrom-Json)
    if ($songs.Count -ne 1) {
        throw "Expected one generated song, got $($songs.Count)."
    }
    if ($songs[0].audio -ne 'songs/Example Song.flac') {
        throw "Generator did not use assets/songs: $($songs[0].audio)"
    }
    if ($songs[0].cover -ne 'covers/Example Album.jpg') {
        throw "Unexpected cover path: $($songs[0].cover)"
    }
    if ($songs[0].accentColor -ne '#0A141EE6') {
        throw "RGBA color was not converted to #RRGGBBAA: $($songs[0].accentColor)"
    }

    Remove-Item -LiteralPath (Join-Path $assetsRoot 'covers\Example Album.jpg') -Force
    $failedAsExpected = $false
    try {
        & (Join-Path $repoRoot 'tools\Prepare-BocchiTheme.ps1') `
            -AssetsRoot $assetsRoot `
            -SongDataPath $songDataPath `
            -OutputDirectory $outputRoot
    }
    catch {
        $failedAsExpected = $_.Exception.Message -like '*covers/Example Album.jpg*'
    }
    if (-not $failedAsExpected) {
        throw 'Missing cover did not produce an actionable failure.'
    }

    Write-Host 'PASS built-in theme generator validates assets/songs, covers and lyrics'
}
finally {
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
