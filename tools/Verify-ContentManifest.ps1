[CmdletBinding()]
param(
    [string] $ContentRoot,

    [string] $ManifestPath
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $ContentRoot) {
    $ContentRoot = Join-Path $repoRoot 'content\reference-player'
}
if (-not $ManifestPath) {
    $ManifestPath = Join-Path $repoRoot 'manifests\reference-player.manifest.json'
}

$resolvedContent = [System.IO.Path]::GetFullPath($ContentRoot)
$resolvedManifest = [System.IO.Path]::GetFullPath($ManifestPath)
if (-not (Test-Path -LiteralPath $resolvedContent -PathType Container)) {
    throw "内容目录不存在：$resolvedContent"
}
if (-not (Test-Path -LiteralPath $resolvedManifest -PathType Leaf)) {
    throw "清单不存在：$resolvedManifest"
}

$manifest = Get-Content -Raw -LiteralPath $resolvedManifest | ConvertFrom-Json
if ($manifest.schemaVersion -ne 1) {
    throw "不支持的清单版本：$($manifest.schemaVersion)"
}

$files = @(Get-ChildItem -LiteralPath $resolvedContent -File -Recurse -Force)
$totalBytes = ($files | Measure-Object -Property Length -Sum).Sum
if ($files.Count -ne $manifest.fileCount) {
    throw "文件数不一致：当前 $($files.Count)，清单 $($manifest.fileCount)。"
}
if ($totalBytes -ne $manifest.totalBytes) {
    throw "总字节数不一致：当前 $totalBytes，清单 $($manifest.totalBytes)。"
}

$contentBoundary = $resolvedContent.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
$manifestEntries = @($manifest.files)
if ($manifestEntries.Count -ne $files.Count) {
    throw "清单条目数不一致：条目 $($manifestEntries.Count)，文件 $($files.Count)。"
}

$seen = @{}
foreach ($entry in $manifestEntries) {
    $relativePath = [string]$entry.path
    if ([string]::IsNullOrWhiteSpace($relativePath) -or [System.IO.Path]::IsPathRooted($relativePath)) {
        throw "清单包含无效相对路径：$relativePath"
    }

    $resolvedFile = [System.IO.Path]::GetFullPath((Join-Path $resolvedContent $relativePath))
    if (-not $resolvedFile.StartsWith($contentBoundary, [StringComparison]::OrdinalIgnoreCase)) {
        throw "清单路径越过内容目录边界：$relativePath"
    }
    if ($seen.ContainsKey($relativePath)) {
        throw "清单包含重复路径：$relativePath"
    }
    $seen[$relativePath] = $true

    if (-not (Test-Path -LiteralPath $resolvedFile -PathType Leaf)) {
        throw "清单文件缺失：$relativePath"
    }

    $file = Get-Item -LiteralPath $resolvedFile
    if ($file.Length -ne $entry.bytes) {
        throw "文件大小不一致：$relativePath"
    }

    $hash = (Get-FileHash -LiteralPath $resolvedFile -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($hash -ne ([string]$entry.sha256).ToLowerInvariant()) {
        throw "SHA-256 不一致：$relativePath"
    }
}

Write-Host "PASS 内容清单：$($files.Count) 个文件，$totalBytes 字节，SHA-256 全部一致"
