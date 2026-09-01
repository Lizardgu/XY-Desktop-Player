[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

$requiredPaths = @(
    'XYDesktopPlayer.sln',
    'src\XYDesktopPlayer.App\XYDesktopPlayer.App.csproj',
    'src\XYDesktopPlayer.Core\XYDesktopPlayer.Core.csproj',
    'tests\XYDesktopPlayer.Core.Tests\XYDesktopPlayer.Core.Tests.csproj'
)
foreach ($relativePath in $requiredPaths) {
    if (-not (Test-Path -LiteralPath (Join-Path $repoRoot $relativePath))) {
        throw "XY project structure is missing: $relativePath"
    }
}

$appProject = Get-Content -Raw -LiteralPath (Join-Path $repoRoot 'src\XYDesktopPlayer.App\XYDesktopPlayer.App.csproj')
$coreProject = Get-Content -Raw -LiteralPath (Join-Path $repoRoot 'src\XYDesktopPlayer.Core\XYDesktopPlayer.Core.csproj')
$solution = Get-Content -Raw -LiteralPath (Join-Path $repoRoot 'XYDesktopPlayer.sln')
$mapping = Get-Content -Raw -LiteralPath (Join-Path $repoRoot 'src\XYDesktopPlayer.Core\WebContentMapping.cs')
$publish = Get-Content -Raw -LiteralPath (Join-Path $repoRoot 'tools\Publish.ps1')
$primaryLauncher = Get-Content -Raw -LiteralPath (Join-Path $repoRoot 'packaging\双击这里-启动桌面壁纸.cmd')

if ($appProject -notmatch '<AssemblyName>XYDesktopPlayer</AssemblyName>' -or
    $appProject -notmatch '<Product>XY桌面播放器</Product>') {
    throw 'Application assembly or Windows product metadata is not branded as XY桌面播放器.'
}
if ($coreProject -notmatch '<AssemblyName>XYDesktopPlayer\.Core</AssemblyName>') {
    throw 'Core assembly is not named XYDesktopPlayer.Core.'
}
if ($solution -notmatch 'src\\XYDesktopPlayer\.App\\XYDesktopPlayer\.App\.csproj' -or
    $solution -notmatch 'src\\XYDesktopPlayer\.Core\\XYDesktopPlayer\.Core\.csproj' -or
    $solution -notmatch 'tests\\XYDesktopPlayer\.Core\.Tests\\XYDesktopPlayer\.Core\.Tests\.csproj') {
    throw 'Solution references do not use the XY project paths.'
}
if ($mapping -notmatch 'xydesktop\.local') {
    throw 'WebView virtual host is not xydesktop.local.'
}
if ($publish -notmatch "XY桌面播放器-v1\.0\.0-win-x64") {
    throw 'Publish.ps1 does not default to the versioned XY package directory.'
}
if ($primaryLauncher -notmatch 'app\\XYDesktopPlayer\.exe') {
    throw 'Primary launcher does not start XYDesktopPlayer.exe.'
}

$operationalRoots = @('src', 'tools', 'packaging')
$operationalFiles = foreach ($root in $operationalRoots) {
    Get-ChildItem -LiteralPath (Join-Path $repoRoot $root) -File -Recurse -Force |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }
}
$legacyMatches = @(
    $operationalFiles |
        Select-String -Pattern 'NikkiDesktop|BocchiWallpaperPort|孤独摇滚壁纸移植|nikkidesktop\.local'
)
if ($legacyMatches.Count -ne 0) {
    $locations = $legacyMatches | ForEach-Object { "$($_.Path):$($_.LineNumber)" }
    throw "Operational source still contains legacy host branding:`n$($locations -join "`n")"
}

Write-Host 'PASS project structure, executable, product metadata, virtual host, launcher and package use XY桌面播放器 branding'
