[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$playerFormPath = Join-Path $repoRoot 'src\NikkiDesktop.App\PlayerForm.cs'
$desktopHostPath = Join-Path $repoRoot 'src\NikkiDesktop.App\DesktopHostService.cs'
$playerForm = Get-Content -Raw -LiteralPath $playerFormPath
$desktopHost = Get-Content -Raw -LiteralPath $desktopHostPath

$closingMatch = [regex]::Match(
    $playerForm,
    'protected override void OnFormClosing\(FormClosingEventArgs eventArgs\)(?<body>[\s\S]*?)protected override void OnFormClosed')
if (-not $closingMatch.Success) {
    throw 'PlayerForm does not detach the desktop host from OnFormClosing.'
}

$closingBody = $closingMatch.Groups['body'].Value
$detachIndex = $closingBody.IndexOf('_desktopHost.Detach(hideHostWindow: true);', [StringComparison]::Ordinal)
$baseIndex = $closingBody.IndexOf('base.OnFormClosing(eventArgs);', [StringComparison]::Ordinal)
if ($detachIndex -lt 0 -or $baseIndex -lt 0 -or $detachIndex -gt $baseIndex) {
    throw 'Desktop host must detach before the base FormClosing path can destroy the WinForms handle.'
}

if ($desktopHost -notmatch 'RedrawWindow\(') {
    throw 'DesktopHostService does not request an Explorer desktop redraw after detaching.'
}
if ($desktopHost -notmatch 'RdwInvalidate\s*\|\s*RdwErase\s*\|\s*RdwAllChildren\s*\|\s*RdwUpdateNow') {
    throw 'DesktopHostService does not use the complete immediate-redraw flag set.'
}
if ($desktopHost -notmatch 'DesktopWorkerCleanupPolicy\.ShouldHide') {
    throw 'DesktopHostService does not guard cleanup with the WorkerW ownership/child policy.'
}
if ($desktopHost -notmatch 'ShowWindow\(previousAttachedParent,\s*SwHide\)') {
    throw 'DesktopHostService does not hide the empty wallpaper WorkerW after detaching.'
}
if ($desktopHost -notmatch 'ShowWindow\(workerW\.Value,\s*SwShow\)') {
    throw 'DesktopHostService does not show the reusable wallpaper WorkerW before attaching.'
}

Write-Host 'PASS desktop host detaches before destruction, hides only the empty wallpaper WorkerW, and redraws Explorer'
