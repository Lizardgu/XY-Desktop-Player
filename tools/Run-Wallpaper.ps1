[CmdletBinding()]
param(
    [string] $Content = (Join-Path (Split-Path -Parent $PSScriptRoot) 'content\reference-player')
)

$repoRoot = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'Invoke-DotNet.ps1') run `
    --no-restore `
    --project (Join-Path $repoRoot 'src\XYDesktopPlayer.App\XYDesktopPlayer.App.csproj') `
    -- `
    --mode wallpaper `
    --content $Content
exit $LASTEXITCODE
