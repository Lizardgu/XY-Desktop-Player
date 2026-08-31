[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$outputPath = Join-Path $repoRoot 'artifacts\smoke\startup-autoplay.png'
$generatedPaths = @(
    $outputPath,
    [System.IO.Path]::ChangeExtension($outputPath, '.json'),
    [System.IO.Path]::ChangeExtension($outputPath, '.host.json'),
    [System.IO.Path]::ChangeExtension($outputPath, '.trace.log'),
    [System.IO.Path]::ChangeExtension($outputPath, '.error.txt')
)

foreach ($path in $generatedPaths) {
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Force
    }
}

& (Join-Path $repoRoot 'tools\Run-CaptureTest.ps1') `
    -Mode window `
    -Content (Join-Path $repoRoot 'content\reference-player') `
    -Output $outputPath
if ($LASTEXITCODE -ne 0) {
    throw "启动播放验收退出代码：$LASTEXITCODE"
}

$hostResult = Get-Content -Raw -LiteralPath ([System.IO.Path]::ChangeExtension($outputPath, '.host.json')) | ConvertFrom-Json
$domResult = Get-Content -Raw -LiteralPath ([System.IO.Path]::ChangeExtension($outputPath, '.json')) | ConvertFrom-Json
if ($hostResult.startupPlaybackStarted -ne $true) {
    throw "播放器没有报告启动自动播放成功：$($hostResult.startupPlaybackError)"
}
if ($domResult.maxAudioTime -le 0.25) {
    throw '启动后音频没有实际推进时间。'
}
if ($domResult.playingAudioCount -lt 1 -and $hostResult.fullscreenPauseCount -lt 1) {
    throw '启动后的音频既没有继续播放，也没有被最大化或全屏窗口自动暂停。'
}

$autoPaused = $hostResult.fullscreenPauseCount -ge 1

Write-Host "PASS startup autoplay playing=$($domResult.playingAudioCount), autoPaused=$autoPaused, maxAudioTime=$($domResult.maxAudioTime)"
