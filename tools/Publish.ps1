[CmdletBinding()]
param(
    [string] $Output,

    [string] $Content
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$artifactRoot = Join-Path $repoRoot 'artifacts'
$publishRoot = Join-Path $artifactRoot 'publish'
if (-not $Output) {
    $Output = Join-Path $publishRoot 'XY桌面播放器-v1.0.0-win-x64'
}
if (-not $Content) {
    $Content = Join-Path $repoRoot 'content\reference-player'
}

$resolvedOutput = [System.IO.Path]::GetFullPath($Output)
$resolvedContent = [System.IO.Path]::GetFullPath($Content)
$artifactBoundary = [System.IO.Path]::GetFullPath($artifactRoot) + [System.IO.Path]::DirectorySeparatorChar
if (-not $resolvedOutput.StartsWith($artifactBoundary, [StringComparison]::OrdinalIgnoreCase)) {
    throw "发布目录必须位于项目 artifacts 目录内：$artifactRoot"
}
if (Test-Path -LiteralPath $resolvedOutput) {
    throw "发布目录已存在，不会自动覆盖：$resolvedOutput"
}
if (-not (Test-Path -LiteralPath $resolvedContent -PathType Container)) {
    throw "播放器内容目录不存在：$resolvedContent"
}

$requiredContentEntries = @(
    'index.html',
    'static',
    'assets\covers',
    'assets\audios',
    'assets\lyrics'
)
foreach ($relativePath in $requiredContentEntries) {
    if (-not (Test-Path -LiteralPath (Join-Path $resolvedContent $relativePath))) {
        throw "播放器内容不完整，缺少：$relativePath"
    }
}

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $resolvedOutput) | Out-Null
$stagingRoot = Join-Path $artifactRoot ('.publishing-' + [Guid]::NewGuid().ToString('N'))
$appProject = Join-Path $repoRoot 'src\XYDesktopPlayer.App\XYDesktopPlayer.App.csproj'
$dotnet = Join-Path $PSScriptRoot 'Invoke-DotNet.ps1'

try {
    $appRoot = Join-Path $stagingRoot 'app'
    & $dotnet restore $appProject `
        --runtime win-x64 `
        --configfile (Join-Path $repoRoot 'NuGet.Config') `
        --ignore-failed-sources `
        '-p:NuGetAudit=false'

    & $dotnet publish $appProject `
        --configuration Release `
        --runtime win-x64 `
        --self-contained true `
        --no-restore `
        --output $appRoot `
        '-p:PublishSingleFile=false' `
        '-p:DebugType=None' `
        '-p:DebugSymbols=false'

    $packagedContentRoot = Join-Path $stagingRoot 'content\reference-player'
    New-Item -ItemType Directory -Force -Path $packagedContentRoot | Out-Null
    foreach ($entry in Get-ChildItem -LiteralPath $resolvedContent -Force) {
        Copy-Item -LiteralPath $entry.FullName -Destination $packagedContentRoot -Recurse -Force
    }

    $launcherNames = @(
        '双击这里-启动桌面壁纸.cmd',
        '普通窗口（备用）.cmd'
    )
    $ascii = [System.Text.ASCIIEncoding]::new()
    foreach ($launcherName in $launcherNames) {
        $sourcePath = Join-Path $repoRoot "packaging\$launcherName"
        $destinationPath = Join-Path $stagingRoot $launcherName
        $launcherText = [System.IO.File]::ReadAllText($sourcePath)
        $normalizedText = $launcherText.Replace("`r`n", "`n").Replace("`r", "`n").Replace("`n", "`r`n")
        [System.IO.File]::WriteAllText($destinationPath, $normalizedText, $ascii)
    }

    Copy-Item -LiteralPath (Join-Path $repoRoot 'packaging\使用说明.txt') -Destination $stagingRoot
    Move-Item -LiteralPath $stagingRoot -Destination $resolvedOutput

    Write-Host "发布完成：$resolvedOutput"
}
catch {
    $resolvedStaging = [System.IO.Path]::GetFullPath($stagingRoot)
    if ($resolvedStaging.StartsWith($artifactBoundary, [StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $resolvedStaging)) {
        Remove-Item -LiteralPath $resolvedStaging -Recurse -Force
    }
    throw
}
