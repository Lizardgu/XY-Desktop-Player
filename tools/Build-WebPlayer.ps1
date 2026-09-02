[CmdletBinding()]
param(
    [switch] $Force
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$webRoot = Join-Path $repoRoot 'web'
$sourceRoot = Join-Path $webRoot 'player-src'
$buildRoot = Join-Path $sourceRoot 'build'
$outputRoot = Join-Path $webRoot 'player'
$storeRoot = Join-Path $repoRoot '.pnpm-store'
$webBoundary = [System.IO.Path]::GetFullPath($webRoot).TrimEnd('\') + '\'
$resolvedOutput = [System.IO.Path]::GetFullPath($outputRoot)

if (-not $resolvedOutput.StartsWith($webBoundary, [StringComparison]::OrdinalIgnoreCase)) {
    throw "播放器输出目录必须位于 web 目录内：$webRoot"
}
if (Test-Path -LiteralPath $resolvedOutput) {
    if (-not $Force) {
        throw "播放器输出目录已存在；确认需要重建时使用 -Force：$resolvedOutput"
    }
    Remove-Item -LiteralPath $resolvedOutput -Recurse -Force
}

$pnpm = Get-Command pnpm -ErrorAction SilentlyContinue
if (-not $pnpm) {
    throw '未找到 pnpm。请安装 Node.js/pnpm 后重试。'
}

& $pnpm.Source install --dir $sourceRoot --frozen-lockfile --store-dir $storeRoot
if ($LASTEXITCODE -ne 0) { throw "pnpm install 失败：$LASTEXITCODE" }

& $pnpm.Source --dir $sourceRoot run test:theme
if ($LASTEXITCODE -ne 0) { throw "前端主题测试失败：$LASTEXITCODE" }

& $pnpm.Source --dir $sourceRoot run build
if ($LASTEXITCODE -ne 0) { throw "前端构建失败：$LASTEXITCODE" }

$stagingRoot = Join-Path $webRoot ('.player-building-' + [Guid]::NewGuid().ToString('N'))
try {
    New-Item -ItemType Directory -Path $stagingRoot | Out-Null
    foreach ($file in Get-ChildItem -LiteralPath $buildRoot -File -Recurse) {
        if ($file.Extension -ieq '.map') { continue }
        $relative = [System.IO.Path]::GetRelativePath($buildRoot, $file.FullName)
        $destination = Join-Path $stagingRoot $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $destination
    }
    Move-Item -LiteralPath $stagingRoot -Destination $resolvedOutput
}
finally {
    if (Test-Path -LiteralPath $stagingRoot) {
        $resolvedStaging = [System.IO.Path]::GetFullPath($stagingRoot)
        if ($resolvedStaging.StartsWith($webBoundary, [StringComparison]::OrdinalIgnoreCase)) {
            Remove-Item -LiteralPath $resolvedStaging -Recurse -Force
        }
    }
}

Write-Host "共用播放器已生成：$resolvedOutput"
