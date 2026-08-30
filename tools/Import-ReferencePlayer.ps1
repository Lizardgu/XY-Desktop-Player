[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Source,

    [string] $Destination,

    [string] $ManifestPath
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$contentRoot = Join-Path $repoRoot 'content'
$manifestRoot = Join-Path $repoRoot 'manifests'

if (-not $Destination) {
    $Destination = Join-Path $contentRoot 'reference-player'
}
if (-not $ManifestPath) {
    $ManifestPath = Join-Path $manifestRoot 'reference-player.manifest.json'
}

$resolvedSource = [System.IO.Path]::GetFullPath($Source)
$resolvedDestination = [System.IO.Path]::GetFullPath($Destination)
$resolvedManifest = [System.IO.Path]::GetFullPath($ManifestPath)
$contentBoundary = [System.IO.Path]::GetFullPath($contentRoot) + [System.IO.Path]::DirectorySeparatorChar
$manifestBoundary = [System.IO.Path]::GetFullPath($manifestRoot) + [System.IO.Path]::DirectorySeparatorChar

if (-not (Test-Path -LiteralPath $resolvedSource -PathType Container)) {
    throw "来源目录不存在：$resolvedSource"
}
if (-not $resolvedDestination.StartsWith($contentBoundary, [StringComparison]::OrdinalIgnoreCase)) {
    throw "目标目录必须位于项目 content 目录内：$contentRoot"
}
if (-not $resolvedManifest.StartsWith($manifestBoundary, [StringComparison]::OrdinalIgnoreCase)) {
    throw "清单必须位于项目 manifests 目录内：$manifestRoot"
}
if (Test-Path -LiteralPath $resolvedDestination) {
    throw "目标目录已存在。为避免覆盖，请先保留或明确移走它：$resolvedDestination"
}

$sourceBoundary = $resolvedSource.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
$destinationBoundary = $resolvedDestination.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
if ($destinationBoundary.StartsWith($sourceBoundary, [StringComparison]::OrdinalIgnoreCase) -or
    $sourceBoundary.StartsWith($destinationBoundary, [StringComparison]::OrdinalIgnoreCase)) {
    throw '来源目录与目标目录不能互相包含。'
}

$requiredEntries = @(
    @{ Path = 'index.html'; Type = 'Leaf' },
    @{ Path = 'static'; Type = 'Container' },
    @{ Path = 'assets\covers'; Type = 'Container' },
    @{ Path = 'assets\audios'; Type = 'Container' },
    @{ Path = 'assets\lyrics'; Type = 'Container' }
)
$missingEntries = @(
    foreach ($entry in $requiredEntries) {
        if (-not (Test-Path -LiteralPath (Join-Path $resolvedSource $entry.Path) -PathType $entry.Type)) {
            $entry.Path.Replace('\', '/')
        }
    }
)
if ($missingEntries.Count -gt 0) {
    throw "来源不是完整播放器，缺少：$($missingEntries -join ', ')"
}

New-Item -ItemType Directory -Force -Path $contentRoot, $manifestRoot | Out-Null
$stagingRoot = Join-Path $contentRoot ('.importing-' + [Guid]::NewGuid().ToString('N'))

try {
    New-Item -ItemType Directory -Path $stagingRoot | Out-Null
    Get-ChildItem -LiteralPath $resolvedSource -Force |
        Copy-Item -Destination $stagingRoot -Recurse -Force

    $sourceFiles = @(
        Get-ChildItem -LiteralPath $resolvedSource -File -Recurse -Force |
            Sort-Object FullName
    )
    $copiedFiles = @(
        Get-ChildItem -LiteralPath $stagingRoot -File -Recurse -Force |
            Sort-Object FullName
    )

    if ($sourceFiles.Count -ne $copiedFiles.Count) {
        throw "复制文件数不一致：来源 $($sourceFiles.Count)，目标 $($copiedFiles.Count)。"
    }

    $sourceBytes = ($sourceFiles | Measure-Object -Property Length -Sum).Sum
    $copiedBytes = ($copiedFiles | Measure-Object -Property Length -Sum).Sum
    if ($sourceBytes -ne $copiedBytes) {
        throw "复制字节数不一致：来源 $sourceBytes，目标 $copiedBytes。"
    }

    $manifestFiles = [System.Collections.Generic.List[object]]::new()
    for ($index = 0; $index -lt $sourceFiles.Count; $index++) {
        $sourceFile = $sourceFiles[$index]
        $copiedFile = $copiedFiles[$index]
        $sourceRelative = [System.IO.Path]::GetRelativePath($resolvedSource, $sourceFile.FullName).Replace('\', '/')
        $copiedRelative = [System.IO.Path]::GetRelativePath($stagingRoot, $copiedFile.FullName).Replace('\', '/')
        if ($sourceRelative -ne $copiedRelative -or $sourceFile.Length -ne $copiedFile.Length) {
            throw "复制清单不一致：$sourceRelative / $copiedRelative"
        }

        $sourceHash = (Get-FileHash -LiteralPath $sourceFile.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        $copiedHash = (Get-FileHash -LiteralPath $copiedFile.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($sourceHash -ne $copiedHash) {
            throw "复制校验失败：$sourceRelative"
        }

        $manifestFiles.Add([ordered]@{
            path = $sourceRelative
            bytes = $sourceFile.Length
            sha256 = $sourceHash
        })
    }

    Move-Item -LiteralPath $stagingRoot -Destination $resolvedDestination

    $manifest = [ordered]@{
        schemaVersion = 1
        createdUtc = [DateTime]::UtcNow.ToString('o')
        sourceName = Split-Path -Leaf $resolvedSource
        fileCount = $sourceFiles.Count
        totalBytes = $sourceBytes
        files = $manifestFiles
    }
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $resolvedManifest -Encoding utf8

    Write-Host "导入完成：$resolvedDestination"
    Write-Host "文件：$($sourceFiles.Count)，字节：$sourceBytes"
    Write-Host "SHA-256 清单：$resolvedManifest"
}
catch {
    $resolvedStaging = [System.IO.Path]::GetFullPath($stagingRoot)
    if ($resolvedStaging.StartsWith($contentBoundary, [StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $resolvedStaging)) {
        Remove-Item -LiteralPath $resolvedStaging -Recurse -Force
    }
    throw
}
