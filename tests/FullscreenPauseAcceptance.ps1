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
            (Get-Content -Raw -LiteralPath $tracePath) -match 'startup-playback-completed: started=True') {
            break
        }
        Start-Sleep -Milliseconds 100
    }
    if (-not (Test-Path -LiteralPath $tracePath) -or
        (Get-Content -Raw -LiteralPath $tracePath) -notmatch 'startup-playback-completed: started=True') {
        throw '等待播放器开始播放超时。'
    }

    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -AssemblyName System.Drawing
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class MaximizedAcceptanceNative
{
    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsZoomed(IntPtr window);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr window, IntPtr processId);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AttachThreadInput(uint attach, uint attachTo, bool attachInput);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool BringWindowToTop(IntPtr window);
}
'@
    $testWindow = [System.Windows.Forms.Form]::new()
    $testWindow.Text = 'Maximized acceptance window'
    $testWindow.FormBorderStyle = [System.Windows.Forms.FormBorderStyle]::Sizable
    $testWindow.StartPosition = [System.Windows.Forms.FormStartPosition]::CenterScreen
    $testWindow.BackColor = [System.Drawing.Color]::FromArgb(24, 30, 42)
    $testWindow.TopMost = $true
    $testWindow.ShowInTaskbar = $true
    $testWindow.Show()
    $testWindow.WindowState = [System.Windows.Forms.FormWindowState]::Maximized
    $testWindow.Activate()
    $testWindow.BringToFront()
    $currentForeground = [MaximizedAcceptanceNative]::GetForegroundWindow()
    $foregroundThread = [MaximizedAcceptanceNative]::GetWindowThreadProcessId($currentForeground, [IntPtr]::Zero)
    $currentThread = [MaximizedAcceptanceNative]::GetCurrentThreadId()
    $inputAttached = $foregroundThread -ne 0 -and $foregroundThread -ne $currentThread -and
        [MaximizedAcceptanceNative]::AttachThreadInput($currentThread, $foregroundThread, $true)
    try {
        [MaximizedAcceptanceNative]::BringWindowToTop($testWindow.Handle) | Out-Null
        [MaximizedAcceptanceNative]::SetForegroundWindow($testWindow.Handle) | Out-Null
    }
    finally {
        if ($inputAttached) {
            [MaximizedAcceptanceNative]::AttachThreadInput($currentThread, $foregroundThread, $false) | Out-Null
        }
    }

    $activationDeadline = [DateTime]::UtcNow.AddSeconds(2)
    while ([DateTime]::UtcNow -lt $activationDeadline -and
        ([MaximizedAcceptanceNative]::GetForegroundWindow() -ne $testWindow.Handle -or
         -not [MaximizedAcceptanceNative]::IsZoomed($testWindow.Handle))) {
        [System.Windows.Forms.Application]::DoEvents()
        Start-Sleep -Milliseconds 20
    }
    if ([MaximizedAcceptanceNative]::GetForegroundWindow() -ne $testWindow.Handle -or
        -not [MaximizedAcceptanceNative]::IsZoomed($testWindow.Handle)) {
        $foregroundHandle = [MaximizedAcceptanceNative]::GetForegroundWindow()
        $zoomed = [MaximizedAcceptanceNative]::IsZoomed($testWindow.Handle)
        throw "测试窗口未能进入 Windows 前台最大化状态。test=$($testWindow.Handle), foreground=$foregroundHandle, isZoomed=$zoomed"
    }

    $fullscreenDeadline = [DateTime]::UtcNow.AddMilliseconds(1300)
    while ([DateTime]::UtcNow -lt $fullscreenDeadline) {
        [System.Windows.Forms.Application]::DoEvents()
        Start-Sleep -Milliseconds 20
    }
    $testWindow.Close()
    $testWindow.Dispose()
    $testWindow = $null

    if (-not $captureProcess.WaitForExit(15000)) {
        throw '播放器最大化窗口验收没有在预期时间内结束。'
    }
    if ($captureProcess.ExitCode -ne 0) {
        throw "播放器最大化窗口验收退出代码：$($captureProcess.ExitCode)"
    }

    $hostResult = Get-Content -Raw -LiteralPath $hostPath | ConvertFrom-Json
    $domResult = Get-Content -Raw -LiteralPath $domPath | ConvertFrom-Json
    if ($hostResult.fullscreenPauseCount -lt 1) {
        throw '真实最大化窗口出现后没有触发自动暂停。'
    }
    if ($hostResult.fullscreenResumeCount -lt 1) {
        throw '真实最大化窗口关闭后没有触发自动恢复。'
    }
    if ($hostResult.maximizedWindowObservationCount -lt 1) {
        throw '验收期间没有明确观察到 Windows 标准最大化状态。'
    }
    if (-not $hostResult.manualPausePreserved) {
        throw '手动暂停保护验证失败。'
    }
    if ($hostResult.fullscreenMonitorError) {
        throw "最大化窗口监控报告错误：$($hostResult.fullscreenMonitorError)"
    }
    if ($domResult.maxAudioTime -le 0.25) {
        throw '音频没有实际开始播放。'
    }

    Write-Host "PASS real maximized-window observed=$($hostResult.maximizedWindowObservationCount), pause=$($hostResult.fullscreenPauseCount), resume=$($hostResult.fullscreenResumeCount), manual pause preserved"
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
