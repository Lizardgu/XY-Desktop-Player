[CmdletBinding()]
param(
    [string] $Content = (Join-Path (Split-Path -Parent $PSScriptRoot) 'content\reference-player'),

    [string] $Output = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\smoke\reference-player.png')
)

$repoRoot = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'Invoke-DotNet.ps1') run `
    --no-restore `
    --project (Join-Path $repoRoot 'src\NikkiDesktop.App\NikkiDesktop.App.csproj') `
    -- `
    --mode window `
    --content $Content `
    --capture $Output
exit $LASTEXITCODE
