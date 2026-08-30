[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$testId = [Guid]::NewGuid().ToString('N')
$outputRoot = Join-Path $repoRoot "artifacts\test-publish-$testId"

try {
    & (Join-Path $repoRoot 'tools\Publish.ps1') -Output $outputRoot

    $requiredFiles = @(
        'NikkiDesktop.App.exe',
        '启动-A-普通窗口.cmd',
        '启动-B-桌面模式.cmd',
        '运行说明.txt'
    )
    foreach ($relativePath in $requiredFiles) {
        if (-not (Test-Path -LiteralPath (Join-Path $outputRoot $relativePath) -PathType Leaf)) {
            throw "Published package is missing $relativePath."
        }
    }

    $media = @(Get-ChildItem -LiteralPath $outputRoot -File -Recurse | Where-Object Extension -In @('.mp3', '.flac', '.jpg', '.lrc'))
    if ($media.Count -ne 0) {
        throw "Source-only publish unexpectedly contains $($media.Count) media files."
    }

    Write-Host 'PASS publish creates a self-contained host without copyrighted media'
}
finally {
    $artifactBoundary = [System.IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts')) + [System.IO.Path]::DirectorySeparatorChar
    $resolvedOutput = [System.IO.Path]::GetFullPath($outputRoot)
    if ($resolvedOutput.StartsWith($artifactBoundary, [StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $resolvedOutput)) {
        Remove-Item -LiteralPath $resolvedOutput -Recurse -Force
    }
}
