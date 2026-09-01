[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$shimPath = Join-Path $repoRoot 'src\NikkiDesktop.App\WallpaperEngineShim.cs'
$shim = Get-Content -Raw -LiteralPath $shimPath

if ($shim -notmatch 'FadeDurationMilliseconds\s*=\s*500') {
    throw 'Automatic pause fade duration is not fixed at 500 milliseconds.'
}
if ($shim -notmatch 'createGain\(\)') {
    throw 'The Web Audio graph has no master gain node for a global fade.'
}
if ($shim -notmatch 'linearRampToValueAtTime\(0') {
    throw 'Automatic pause does not schedule a smooth gain ramp to silence.'
}
if ($shim -notmatch '__bocchiPauseForFullscreen\s*=\s*async function') {
    throw 'Automatic pause must await the fade before pausing media elements.'
}

$pauseMatch = [regex]::Match(
    $shim,
    '__bocchiPauseForFullscreen\s*=\s*async function[\s\S]*?__bocchiResumeAfterFullscreen')
if (-not $pauseMatch.Success) {
    throw 'Could not locate the automatic pause implementation.'
}
$pauseBody = $pauseMatch.Value
$delayIndex = $pauseBody.IndexOf('await waitForFade', [StringComparison]::Ordinal)
$pauseIndex = $pauseBody.IndexOf('audio.pause()', [StringComparison]::Ordinal)
if ($delayIndex -lt 0 -or $pauseIndex -lt 0 -or $pauseIndex -lt $delayIndex) {
    throw 'Media is paused before the 500 millisecond fade completes.'
}

$resumeMatch = [regex]::Match(
    $shim,
    '__bocchiResumeAfterFullscreen\s*=\s*async function[\s\S]*?__bocchiClearFullscreenPause')
if (-not $resumeMatch.Success) {
    throw 'Could not locate the automatic resume implementation.'
}
$resumeBody = $resumeMatch.Value
$gainResetIndex = $resumeBody.IndexOf('resetMasterGain()', [StringComparison]::Ordinal)
$playIndex = $resumeBody.IndexOf('audio.play()', [StringComparison]::Ordinal)
if ($gainResetIndex -lt 0 -or $playIndex -lt 0 -or $gainResetIndex -gt $playIndex) {
    throw 'Automatic resume does not restore full gain before playback starts.'
}

Write-Host 'PASS automatic pause fades for 500ms and automatic resume restores full gain immediately'
