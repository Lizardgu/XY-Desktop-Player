[CmdletBinding()]
param(
    [string] $Content
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$outputPath = Join-Path $repoRoot 'artifacts\smoke\foreground-app-auto-pause.png'
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $outputPath) | Out-Null
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
    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -AssemblyName System.Drawing
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class ForegroundBootstrapNative
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindow(string className, string windowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

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

    [DllImport("user32.dll")]
    public static extern void SwitchToThisWindow(IntPtr window, bool altTab);

    [DllImport("user32.dll")]
    public static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShowWindow(IntPtr window, int command);
}
'@
    $testWindow = [System.Windows.Forms.Form]::new()
    $testWindow.Text = 'Normal foreground acceptance window'
    $testWindow.FormBorderStyle = [System.Windows.Forms.FormBorderStyle]::Sizable
    $testWindow.StartPosition = [System.Windows.Forms.FormStartPosition]::CenterScreen
    $testWindow.BackColor = [System.Drawing.Color]::FromArgb(24, 30, 42)
    $testWindow.TopMost = $true
    $testWindow.ShowInTaskbar = $true
    $testWindow.ClientSize = [System.Drawing.Size]::new(760, 460)
    $testWindow.Show()
    $testWindow.Activate()

    $captureScript = Join-Path $repoRoot 'tools\Run-CaptureTest.ps1'
    $contentRoot = if ($Content) { $Content } else { Join-Path $repoRoot 'content\reference-player' }
    $hostExecutable = (Get-Process -Id $PID).Path
    $testStart = Get-Date
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
            throw "播放器在前台应用验收开始前退出，退出代码：$($captureProcess.ExitCode)`n$stdout`n$stderr"
        }
        if ((Test-Path -LiteralPath $tracePath) -and
            (Get-Content -Raw -LiteralPath $tracePath) -match 'startup-playback-completed: started=True') {
            break
        }
        $playerWindow = @(
            Get-Process -Name XYDesktopPlayer -ErrorAction SilentlyContinue |
                Where-Object { $_.StartTime -ge $testStart -and $_.MainWindowHandle -ne 0 } |
                Sort-Object StartTime -Descending |
                Select-Object -First 1 -ExpandProperty MainWindowHandle
        )
        $playerWindow = if ($playerWindow.Count -gt 0) { [IntPtr]$playerWindow[0] } else { [IntPtr]::Zero }
        if ($playerWindow -ne [IntPtr]::Zero) {
            $testWindow.TopMost = $true
            $testWindow.Activate()
            $testWindow.BringToFront()
            [System.Windows.Forms.Application]::DoEvents()
            $testWindow.TopMost = $false
            $currentForeground = [ForegroundBootstrapNative]::GetForegroundWindow()
            $foregroundThread = [ForegroundBootstrapNative]::GetWindowThreadProcessId($currentForeground, [IntPtr]::Zero)
            $currentThread = [ForegroundBootstrapNative]::GetCurrentThreadId()
            $inputAttached = $foregroundThread -ne 0 -and $foregroundThread -ne $currentThread -and
                [ForegroundBootstrapNative]::AttachThreadInput($currentThread, $foregroundThread, $true)
            try {
                [ForegroundBootstrapNative]::keybd_event(0x12, 0, 0, [UIntPtr]::Zero)
                [ForegroundBootstrapNative]::keybd_event(0x12, 0, 2, [UIntPtr]::Zero)
                [ForegroundBootstrapNative]::SwitchToThisWindow($playerWindow, $true)
                [ForegroundBootstrapNative]::BringWindowToTop($playerWindow) | Out-Null
                [ForegroundBootstrapNative]::SetForegroundWindow($playerWindow) | Out-Null
            }
            finally {
                if ($inputAttached) {
                    [ForegroundBootstrapNative]::AttachThreadInput($currentThread, $foregroundThread, $false) | Out-Null
                }
            }
        }
        [System.Windows.Forms.Application]::DoEvents()
        Start-Sleep -Milliseconds 100
    }
    if (-not (Test-Path -LiteralPath $tracePath) -or
        (Get-Content -Raw -LiteralPath $tracePath) -notmatch 'startup-playback-completed: started=True') {
        throw '等待播放器开始播放超时。'
    }

    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class ForegroundAcceptanceNative
{
    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr window);

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

    [DllImport("user32.dll")]
    public static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShowWindow(IntPtr window, int command);
}
'@
    $testWindow.TopMost = $true
    $testWindow.Show()
    $testWindow.Activate()
    $testWindow.BringToFront()
    $currentForeground = [ForegroundAcceptanceNative]::GetForegroundWindow()
    $foregroundThread = [ForegroundAcceptanceNative]::GetWindowThreadProcessId($currentForeground, [IntPtr]::Zero)
    $currentThread = [ForegroundAcceptanceNative]::GetCurrentThreadId()
    $inputAttached = $foregroundThread -ne 0 -and $foregroundThread -ne $currentThread -and
        [ForegroundAcceptanceNative]::AttachThreadInput($currentThread, $foregroundThread, $true)
    try {
        [ForegroundAcceptanceNative]::keybd_event(0x12, 0, 0, [UIntPtr]::Zero)
        [ForegroundAcceptanceNative]::keybd_event(0x12, 0, 2, [UIntPtr]::Zero)
        [ForegroundAcceptanceNative]::BringWindowToTop($testWindow.Handle) | Out-Null
        [ForegroundAcceptanceNative]::SetForegroundWindow($testWindow.Handle) | Out-Null
    }
    finally {
        if ($inputAttached) {
            [ForegroundAcceptanceNative]::AttachThreadInput($currentThread, $foregroundThread, $false) | Out-Null
        }
    }

    $activationDeadline = [DateTime]::UtcNow.AddSeconds(2)
    while ([DateTime]::UtcNow -lt $activationDeadline -and
        [ForegroundAcceptanceNative]::GetForegroundWindow() -ne $testWindow.Handle) {
        [System.Windows.Forms.Application]::DoEvents()
        Start-Sleep -Milliseconds 20
    }
    if ([ForegroundAcceptanceNative]::GetForegroundWindow() -ne $testWindow.Handle) {
        $foregroundHandle = [ForegroundAcceptanceNative]::GetForegroundWindow()
        throw "普通大小的测试窗口未能进入 Windows 前台。test=$($testWindow.Handle), foreground=$foregroundHandle"
    }

    Start-Sleep -Milliseconds 1300
    $currentForeground = [ForegroundAcceptanceNative]::GetForegroundWindow()
    $foregroundThread = [ForegroundAcceptanceNative]::GetWindowThreadProcessId($currentForeground, [IntPtr]::Zero)
    $currentThread = [ForegroundAcceptanceNative]::GetCurrentThreadId()
    $inputAttached = $foregroundThread -ne 0 -and $foregroundThread -ne $currentThread -and
        [ForegroundAcceptanceNative]::AttachThreadInput($currentThread, $foregroundThread, $true)
    try {
        [ForegroundAcceptanceNative]::keybd_event(0x12, 0, 0, [UIntPtr]::Zero)
        [ForegroundAcceptanceNative]::keybd_event(0x12, 0, 2, [UIntPtr]::Zero)
        [ForegroundAcceptanceNative]::BringWindowToTop($playerWindow) | Out-Null
        [ForegroundAcceptanceNative]::SetForegroundWindow($playerWindow) | Out-Null
    }
    finally {
        if ($inputAttached) {
            [ForegroundAcceptanceNative]::AttachThreadInput($currentThread, $foregroundThread, $false) | Out-Null
        }
    }
    [ForegroundAcceptanceNative]::ShowWindow($testWindow.Handle, 0) | Out-Null
    $returnDeadline = [DateTime]::UtcNow.AddSeconds(2)
    while ([DateTime]::UtcNow -lt $returnDeadline -and
        [ForegroundAcceptanceNative]::GetForegroundWindow() -ne $playerWindow) {
        Start-Sleep -Milliseconds 20
    }
    if ([ForegroundAcceptanceNative]::GetForegroundWindow() -ne $playerWindow) {
        throw "未能把前台焦点交还给播放器。player=$playerWindow, foreground=$([ForegroundAcceptanceNative]::GetForegroundWindow())"
    }

    if (-not $captureProcess.WaitForExit(15000)) {
        throw '播放器前台应用验收没有在预期时间内结束。'
    }
    if ($captureProcess.ExitCode -ne 0) {
        throw "播放器前台应用验收退出代码：$($captureProcess.ExitCode)"
    }

    $hostResult = Get-Content -Raw -LiteralPath $hostPath | ConvertFrom-Json
    $domResult = Get-Content -Raw -LiteralPath $domPath | ConvertFrom-Json
    if ($hostResult.foregroundPauseCount -lt 1) {
        throw '真实普通大小窗口进入前台后没有触发自动暂停。'
    }
    if ($hostResult.foregroundResumeCount -lt 1) {
        throw '焦点交回播放器后没有触发自动恢复。'
    }
    if (-not $hostResult.manualPausePreserved) {
        throw '手动暂停保护验证失败。'
    }
    if ($hostResult.foregroundMonitorError) {
        throw "前台应用监控报告错误：$($hostResult.foregroundMonitorError)"
    }
    if ($domResult.maxAudioTime -le 0.25) {
        throw '音频没有实际开始播放。'
    }
    if ($domResult.fadeDurationMs -ne 500) {
        throw "自动暂停淡出时长不是 500 毫秒：$($domResult.fadeDurationMs)"
    }
    if ($domResult.lastFadeElapsedMs -lt 450 -or $domResult.lastFadeElapsedMs -gt 1200) {
        throw "真实自动暂停没有在合理的 500 毫秒淡出窗口完成：$($domResult.lastFadeElapsedMs) ms"
    }

    Write-Host "PASS real foreground app fade=$([Math]::Round($domResult.lastFadeElapsedMs))ms, pause=$($hostResult.foregroundPauseCount), resume=$($hostResult.foregroundResumeCount), manual pause preserved"
}
finally {
    if ($testWindow) {
        $testWindow.Hide()
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
