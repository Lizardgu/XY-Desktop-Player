[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$testId = [Guid]::NewGuid().ToString('N')
$sourceRoot = Join-Path ([System.IO.Path]::GetTempPath()) "NikkiDesktop-ImportSource-$testId"
$destinationRoot = Join-Path $repoRoot "content\test-import-$testId"
$manifestPath = Join-Path $repoRoot "manifests\test-import-$testId.json"

try {
    New-Item -ItemType Directory -Force -Path @(
        (Join-Path $sourceRoot 'static'),
        (Join-Path $sourceRoot 'assets\covers'),
        (Join-Path $sourceRoot 'assets\audios'),
        (Join-Path $sourceRoot 'assets\lyrics')
    ) | Out-Null
    Set-Content -LiteralPath (Join-Path $sourceRoot 'index.html') -Value '<!doctype html>' -NoNewline
    Set-Content -LiteralPath (Join-Path $sourceRoot 'static\app.js') -Value 'console.log("test")' -NoNewline
    Set-Content -LiteralPath (Join-Path $sourceRoot 'assets\covers\cover.jpg') -Value 'cover' -NoNewline
    Set-Content -LiteralPath (Join-Path $sourceRoot 'assets\audios\song.mp3') -Value 'audio' -NoNewline
    Set-Content -LiteralPath (Join-Path $sourceRoot 'assets\lyrics\song.lrc') -Value '[00:00.00]test' -NoNewline

    & (Join-Path $repoRoot 'tools\Import-ReferencePlayer.ps1') `
        -Source $sourceRoot `
        -Destination $destinationRoot `
        -ManifestPath $manifestPath

    $sourceFiles = @(Get-ChildItem -LiteralPath $sourceRoot -File -Recurse)
    $destinationFiles = @(Get-ChildItem -LiteralPath $destinationRoot -File -Recurse)
    if ($sourceFiles.Count -ne $destinationFiles.Count) {
        throw "Expected $($sourceFiles.Count) copied files, got $($destinationFiles.Count)."
    }

    $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
    if ($manifest.fileCount -ne $sourceFiles.Count) {
        throw "Manifest file count is $($manifest.fileCount), expected $($sourceFiles.Count)."
    }
    if (@($manifest.files).Count -ne $sourceFiles.Count) {
        throw 'Manifest does not contain one hash entry per file.'
    }
    if ($manifest.PSObject.Properties.Name -contains 'sourcePath') {
        throw 'Manifest must not expose a machine-specific source path.'
    }

    & (Join-Path $repoRoot 'tools\Verify-ContentManifest.ps1') `
        -ContentRoot $destinationRoot `
        -ManifestPath $manifestPath

    Write-Host 'PASS reference player import copies and hashes every file'
}
finally {
    $contentBoundary = [System.IO.Path]::GetFullPath((Join-Path $repoRoot 'content')) + [System.IO.Path]::DirectorySeparatorChar
    $resolvedDestination = [System.IO.Path]::GetFullPath($destinationRoot)
    if ($resolvedDestination.StartsWith($contentBoundary, [StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $resolvedDestination)) {
        Remove-Item -LiteralPath $resolvedDestination -Recurse -Force
    }

    $manifestBoundary = [System.IO.Path]::GetFullPath((Join-Path $repoRoot 'manifests')) + [System.IO.Path]::DirectorySeparatorChar
    $resolvedManifest = [System.IO.Path]::GetFullPath($manifestPath)
    if ($resolvedManifest.StartsWith($manifestBoundary, [StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $resolvedManifest)) {
        Remove-Item -LiteralPath $resolvedManifest -Force
    }

    $tempBoundary = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
    $resolvedSource = [System.IO.Path]::GetFullPath($sourceRoot)
    if ($resolvedSource.StartsWith($tempBoundary, [StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $resolvedSource)) {
        Remove-Item -LiteralPath $resolvedSource -Recurse -Force
    }
}
