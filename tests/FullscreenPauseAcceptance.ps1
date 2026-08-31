[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$outputPath = Join-Path $repoRoot 'artifacts\smoke\fullscreen-auto-pause.png'
$tracePath = [System.IO.Path]::ChangeExtension($outputPath, '.trace.log')
$hostPath = [System.IO.Path]::ChangeExtension($outputPath, '.host.json')
$domPath = [System.IO.Path]::ChangeExtension($outputPath, '.json')
$stdoutPath = [System.IO.Path]::ChangeExtension($outputPath, '.stdout.log')
$stderrPath = [System.IO.Path]::ChangeExtension($outputPath, '.stderr.log')
$captureProcess = $null
$testWindow = $null

foreach ($path in @(
    $outputPath,
    $tracePath,
    $hostPath,
    $domPath,
    $stdoutPath,
    $stderrPath,
    [System.IO.Path]::ChangeExtension($outputPath, '.error.txt')
)) {
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Force
    }
}

try {
    $captureScript = Join-Path $repoRoot 'tools\Run-CaptureTest.ps1'
    $contentRoot = Join-Path $repoRoot 'content\reference-player'
    $hostExecutable = (Get-Process -Id $PID).Path
    $captureProcess = Start-Process `
        -FilePath $hostExecutable `
        -ArgumentList @(
            '-NoProfile',
            '-ExecutionPolicy', 'Bypass',
            '-File', $captureScript,
            '-Mode', 'window',
            '-Content', $contentRoot,
            '-Output', $outputPath
        ) `
        -WorkingDirectory $repoRoot `
        -WindowStyle Hidden `
        -RedirectStandardOutput $stdoutPath `
        -RedirectStandardError $stderrPath `
        -PassThru

    $startupDeadline = [DateTime]::UtcNow.AddSeconds(25)
    while ([DateTime]::UtcNow -lt $startupDeadline) {
        if ($captureProcess.HasExited) {
            $stdout = if (Test-Path -LiteralPath $stdoutPath) { Get-Content -Raw -LiteralPath $stdoutPath } else { '' }
            $stderr = if (Test-Path -LiteralPath $stderrPath) { Get-Content -Raw -LiteralPath $stderrPath } else { '' }
            throw "播放器在全屏验收开始前退出，退出代码：$($captureProcess.ExitCode)`n$stdout`n$stderr"
        }
        if ((Test-Path -LiteralPath $tracePath) -and
            (Get-Content -Raw -LiteralPath $tracePath) -match 'capture-play-request-completed') {
            break
        }
        Start-Sleep -Milliseconds 100
    }
    if (-not (Test-Path -LiteralPath $tracePath) -or
        (Get-Content -Raw -LiteralPath $tracePath) -notmatch 'capture-play-request-completed') {
        throw '等待播放器开始播放超时。'
    }

    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -AssemblyName System.Drawing
    $testWindow = [System.Windows.Forms.Form]::new()
    $testWindow.FormBorderStyle = [System.Windows.Forms.FormBorderStyle]::None
    $testWindow.StartPosition = [System.Windows.Forms.FormStartPosition]::Manual
    $testWindow.Bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $testWindow.BackColor = [System.Drawing.Color]::FromArgb(24, 30, 42)
    $testWindow.TopMost = $true
    $testWindow.ShowInTaskbar = $false
    $testWindow.Show()
    $testWindow.Activate()
    $testWindow.BringToFront()

    $fullscreenDeadline = [DateTime]::UtcNow.AddMilliseconds(1300)
    while ([DateTime]::UtcNow -lt $fullscreenDeadline) {
        [System.Windows.Forms.Application]::DoEvents()
        Start-Sleep -Milliseconds 20
    }
    $testWindow.Close()
    $testWindow.Dispose()
    $testWindow = $null

    if (-not $captureProcess.WaitForExit(15000)) {
        throw '播放器全屏验收没有在预期时间内结束。'
    }
    if ($captureProcess.ExitCode -ne 0) {
        throw "播放器全屏验收退出代码：$($captureProcess.ExitCode)"
    }

    $hostResult = Get-Content -Raw -LiteralPath $hostPath | ConvertFrom-Json
    $domResult = Get-Content -Raw -LiteralPath $domPath | ConvertFrom-Json
    if ($hostResult.fullscreenPauseCount -lt 1) {
        throw '真实全屏窗口出现后没有触发自动暂停。'
    }
    if ($hostResult.fullscreenResumeCount -lt 1) {
        throw '真实全屏窗口关闭后没有触发自动恢复。'
    }
    if (-not $hostResult.manualPausePreserved) {
        throw '手动暂停保护验证失败。'
    }
    if ($hostResult.fullscreenMonitorError) {
        throw "全屏监控报告错误：$($hostResult.fullscreenMonitorError)"
    }
    if ($domResult.maxAudioTime -le 0.25) {
        throw '音频没有实际开始播放。'
    }

    Write-Host "PASS real full-screen pause=$($hostResult.fullscreenPauseCount), resume=$($hostResult.fullscreenResumeCount), manual pause preserved"
}
finally {
    if ($testWindow) {
        $testWindow.Close()
        $testWindow.Dispose()
    }
    if ($captureProcess -and -not $captureProcess.HasExited) {
        $captureProcess.Kill()
        $captureProcess.WaitForExit()
    }
    if ($captureProcess) {
        $captureProcess.Dispose()
    }
}
