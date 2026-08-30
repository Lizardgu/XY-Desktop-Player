[CmdletBinding()]
param(
    [string] $Output
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$artifactRoot = Join-Path $repoRoot 'artifacts'
$publishRoot = Join-Path $artifactRoot 'publish'
if (-not $Output) {
    $Output = Join-Path $publishRoot 'NikkiDesktop-win-x64'
}

$resolvedOutput = [System.IO.Path]::GetFullPath($Output)
$artifactBoundary = [System.IO.Path]::GetFullPath($artifactRoot) + [System.IO.Path]::DirectorySeparatorChar
if (-not $resolvedOutput.StartsWith($artifactBoundary, [StringComparison]::OrdinalIgnoreCase)) {
    throw "发布目录必须位于项目 artifacts 目录内：$artifactRoot"
}
if (Test-Path -LiteralPath $resolvedOutput) {
    throw "发布目录已存在，不会自动覆盖：$resolvedOutput"
}

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $resolvedOutput) | Out-Null
$stagingRoot = Join-Path $artifactRoot ('.publishing-' + [Guid]::NewGuid().ToString('N'))
$appProject = Join-Path $repoRoot 'src\NikkiDesktop.App\NikkiDesktop.App.csproj'
$dotnet = Join-Path $PSScriptRoot 'Invoke-DotNet.ps1'

try {
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
        --output $stagingRoot `
        '-p:PublishSingleFile=false' `
        '-p:DebugType=None' `
        '-p:DebugSymbols=false'

    Copy-Item -LiteralPath (Join-Path $repoRoot 'packaging\启动-A-普通窗口.cmd') -Destination $stagingRoot
    Copy-Item -LiteralPath (Join-Path $repoRoot 'packaging\启动-B-桌面模式.cmd') -Destination $stagingRoot
    Copy-Item -LiteralPath (Join-Path $repoRoot 'packaging\运行说明.txt') -Destination $stagingRoot
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
