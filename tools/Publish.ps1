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
    $Output = Join-Path $publishRoot 'XY桌面播放器-theme-skeleton-win-x64'
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

$requiredAssetEntries = @(
    'assets\covers',
    'assets\songs',
    'assets\lyrics\original',
    'assets\lyrics\romanized'
)
foreach ($relativePath in $requiredAssetEntries) {
    if (-not (Test-Path -LiteralPath (Join-Path $resolvedContent $relativePath))) {
        throw "播放器内容不完整，缺少：$relativePath"
    }
}

$playerSource = Join-Path $repoRoot 'web\player'
$themeDefinitionSource = Join-Path $repoRoot 'themes\bocchi'
foreach ($requiredPath in @(
    (Join-Path $playerSource 'index.html'),
    (Join-Path $playerSource 'static'),
    (Join-Path $themeDefinitionSource 'pack.json'),
    (Join-Path $themeDefinitionSource 'songs.json')
)) {
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        throw "项目骨架不完整，缺少：$requiredPath"
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

    $packagedPlayerRoot = Join-Path $stagingRoot 'content\player'
    $packagedThemeRoot = Join-Path $stagingRoot 'content\themes\孤独摇滚'
    New-Item -ItemType Directory -Force -Path $packagedPlayerRoot, $packagedThemeRoot | Out-Null
    foreach ($entry in Get-ChildItem -LiteralPath $playerSource -Force) {
        Copy-Item -LiteralPath $entry.FullName -Destination $packagedPlayerRoot -Recurse -Force
    }
    Copy-Item -LiteralPath (Join-Path $themeDefinitionSource 'pack.json') -Destination $packagedThemeRoot
    Copy-Item -LiteralPath (Join-Path $themeDefinitionSource 'songs.json') -Destination $packagedThemeRoot

    foreach ($assetDirectory in @('covers', 'songs', 'lyrics')) {
        Copy-Item `
            -LiteralPath (Join-Path $resolvedContent "assets\$assetDirectory") `
            -Destination $packagedThemeRoot `
            -Recurse `
            -Force
    }

    $sourceAssetRoot = Join-Path $resolvedContent 'assets'
    $sourceAssetFiles = @(
        foreach ($assetDirectory in @('covers', 'songs', 'lyrics')) {
            Get-ChildItem -LiteralPath (Join-Path $sourceAssetRoot $assetDirectory) -File -Recurse -Force
        }
    )
    $packagedAssetFiles = @(
        foreach ($assetDirectory in @('covers', 'songs', 'lyrics')) {
            Get-ChildItem -LiteralPath (Join-Path $packagedThemeRoot $assetDirectory) -File -Recurse -Force
        }
    )
    if ($sourceAssetFiles.Count -ne $packagedAssetFiles.Count) {
        throw "主题素材复制文件数不一致：来源 $($sourceAssetFiles.Count)，成品 $($packagedAssetFiles.Count)。"
    }
    $sourceBytes = ($sourceAssetFiles | Measure-Object -Property Length -Sum).Sum
    $packagedBytes = ($packagedAssetFiles | Measure-Object -Property Length -Sum).Sum
    if ($sourceBytes -ne $packagedBytes) {
        throw "主题素材复制字节数不一致：来源 $sourceBytes，成品 $packagedBytes。"
    }
    $packagedByPath = @{}
    foreach ($file in $packagedAssetFiles) {
        $relative = [IO.Path]::GetRelativePath($packagedThemeRoot, $file.FullName).Replace('\', '/')
        $packagedByPath[$relative] = $file
    }
    foreach ($sourceFile in $sourceAssetFiles) {
        $relative = [IO.Path]::GetRelativePath($sourceAssetRoot, $sourceFile.FullName).Replace('\', '/')
        $packagedFile = $packagedByPath[$relative]
        if (-not $packagedFile -or $packagedFile.Length -ne $sourceFile.Length) {
            throw "主题素材复制清单不一致：$relative"
        }
        $sourceHash = (Get-FileHash -LiteralPath $sourceFile.FullName -Algorithm SHA256).Hash
        $packagedHash = (Get-FileHash -LiteralPath $packagedFile.FullName -Algorithm SHA256).Hash
        if ($sourceHash -ne $packagedHash) {
            throw "主题素材复制校验失败：$relative"
        }
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
