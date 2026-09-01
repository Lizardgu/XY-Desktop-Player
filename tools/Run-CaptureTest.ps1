[CmdletBinding()]
param(
    [ValidateSet('window', 'wallpaper')]
    [string] $Mode = 'window',

    [string] $Content = (Join-Path (Split-Path -Parent $PSScriptRoot) 'content\reference-player'),

    [string] $Output = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\smoke\reference-player.png')
)

$repoRoot = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'Invoke-DotNet.ps1') run `
    --no-restore `
    --project (Join-Path $repoRoot 'src\XYDesktopPlayer.App\XYDesktopPlayer.App.csproj') `
    -- `
    --mode $Mode `
    --content $Content `
    --capture $Output
exit $LASTEXITCODE
