[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$outputPath = Join-Path $repoRoot 'artifacts\smoke\startup-autoplay.png'
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
    if (-not ('StartupDeferralAcceptanceNative' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class StartupDeferralAcceptanceNative
{
    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern IntPtr GetShellWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindow(string className, string windowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr window, IntPtr processId);

    [DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId")]
    public static extern uint GetWindowThreadProcessIdWithProcess(IntPtr window, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(IntPtr window, StringBuilder className, int maxCount);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AttachThreadInput(uint attach, uint attachTo, bool attachInput);

    [DllImport("user32.dll")]
    public static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool BringWindowToTop(IntPtr window);

    public static void ForceForeground(IntPtr window)
    {
        BringWindowToTop(window);
        SetForegroundWindow(window);
    }

    public static string DescribeWindow(IntPtr window)
    {
        var className = new StringBuilder(64);
        GetClassName(window, className, className.Capacity);
        GetWindowThreadProcessIdWithProcess(window, out var processId);
        return window + ":" + className + ":pid=" + processId;
    }
}
'@
    }

    $testWindow = [System.Windows.Forms.Form]::new()
    $testWindow.Text = 'Startup deferral foreground window'
    $testWindow.ClientSize = [System.Drawing.Size]::new(760, 460)
    $testWindow.StartPosition = [System.Windows.Forms.FormStartPosition]::CenterScreen
    $testWindow.TopMost = $true
    $testWindow.ShowInTaskbar = $true
    $testWindow.Show()
    $testWindow.Activate()
    [StartupDeferralAcceptanceNative]::ForceForeground($testWindow.Handle)

    $captureScript = Join-Path $repoRoot 'tools\Run-CaptureTest.ps1'
    $contentRoot = Join-Path $repoRoot 'content\reference-player'
    $hostExecutable = (Get-Process -Id $PID).Path
    $captureProcess = Start-Process `
        -FilePath $hostExecutable `
        -ArgumentList @(
            '-NoProfile',
            '-ExecutionPolicy', 'Bypass',
            '-File', $captureScript,
            '-Mode', 'wallpaper',
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
            throw "播放器在延迟首播验收开始前退出，退出代码：$($captureProcess.ExitCode)`n$stdout`n$stderr"
        }
        $testWindow.Activate()
        [StartupDeferralAcceptanceNative]::ForceForeground($testWindow.Handle)
        [System.Windows.Forms.Application]::DoEvents()
        if ((Test-Path -LiteralPath $tracePath) -and
            (Get-Content -Raw -LiteralPath $tracePath) -match 'startup-playback-deferred') {
            break
        }
        Start-Sleep -Milliseconds 50
    }

    if (-not (Test-Path -LiteralPath $tracePath) -or
        (Get-Content -Raw -LiteralPath $tracePath) -notmatch 'startup-playback-deferred') {
        throw '普通应用位于前台时，播放器没有进入延迟首播状态。'
    }

    Start-Sleep -Milliseconds 650
    if ((Get-Content -Raw -LiteralPath $tracePath) -match 'startup-playback-completed') {
        throw '普通应用仍在前台时播放器已经尝试首播，可能再次出现短促声音。'
    }

    $desktopWindow = [StartupDeferralAcceptanceNative]::GetShellWindow()
    if ($desktopWindow -eq [IntPtr]::Zero) {
        throw '延迟首播验收找不到 Windows 桌面 Shell 窗口。'
    }
    $currentForeground = [StartupDeferralAcceptanceNative]::GetForegroundWindow()
    $foregroundThread = [StartupDeferralAcceptanceNative]::GetWindowThreadProcessId($currentForeground, [IntPtr]::Zero)
    $currentThread = [StartupDeferralAcceptanceNative]::GetCurrentThreadId()
    $inputAttached = $foregroundThread -ne 0 -and $foregroundThread -ne $currentThread -and
        [StartupDeferralAcceptanceNative]::AttachThreadInput($currentThread, $foregroundThread, $true)
    try {
        [StartupDeferralAcceptanceNative]::keybd_event(0x12, 0, 0, [UIntPtr]::Zero)
        [StartupDeferralAcceptanceNative]::keybd_event(0x12, 0, 2, [UIntPtr]::Zero)
        [StartupDeferralAcceptanceNative]::SetForegroundWindow($desktopWindow) | Out-Null
    }
    finally {
        if ($inputAttached) {
            [StartupDeferralAcceptanceNative]::AttachThreadInput($currentThread, $foregroundThread, $false) | Out-Null
        }
    }
    [StartupDeferralAcceptanceNative]::ShowWindow($testWindow.Handle, 0) | Out-Null
    $desktopDeadline = [DateTime]::UtcNow.AddSeconds(5)
    while ([DateTime]::UtcNow -lt $desktopDeadline -and
        (Get-Content -Raw -LiteralPath $tracePath) -notmatch 'startup-playback-completed') {
        [StartupDeferralAcceptanceNative]::ForceForeground($desktopWindow)
        [System.Windows.Forms.Application]::DoEvents()
        Start-Sleep -Milliseconds 50
    }
    if ((Get-Content -Raw -LiteralPath $tracePath) -notmatch 'startup-playback-completed') {
        $foregroundDescription = [StartupDeferralAcceptanceNative]::DescribeWindow(
            [StartupDeferralAcceptanceNative]::GetForegroundWindow())
        throw "回到桌面后没有在 5 秒内完成延迟首播。desktop=$desktopWindow, foreground=$foregroundDescription"
    }

    $audioAdvanceDeadline = [DateTime]::UtcNow.AddMilliseconds(900)
    while ([DateTime]::UtcNow -lt $audioAdvanceDeadline) {
        [StartupDeferralAcceptanceNative]::ForceForeground($desktopWindow)
        [System.Windows.Forms.Application]::DoEvents()
        Start-Sleep -Milliseconds 50
    }

    if (-not $captureProcess.WaitForExit(15000)) {
        throw '播放器延迟首播验收没有在预期时间内结束。'
    }
    if ($captureProcess.ExitCode -ne 0) {
        $stdout = if (Test-Path -LiteralPath $stdoutPath) { Get-Content -Raw -LiteralPath $stdoutPath } else { '' }
        $stderr = if (Test-Path -LiteralPath $stderrPath) { Get-Content -Raw -LiteralPath $stderrPath } else { '' }
        throw "播放器延迟首播验收退出代码：$($captureProcess.ExitCode)`n$stdout`n$stderr"
    }

    $hostResult = Get-Content -Raw -LiteralPath $hostPath | ConvertFrom-Json
    $domResult = Get-Content -Raw -LiteralPath $domPath | ConvertFrom-Json
    $trace = Get-Content -Raw -LiteralPath $tracePath
    if ($hostResult.startupPlaybackStarted -ne $true) {
        throw "回到桌面后播放器没有报告自动播放成功：$($hostResult.startupPlaybackError)"
    }
    if ($domResult.maxAudioTime -le 0.25) {
        throw '回到桌面后音频没有实际推进时间。'
    }
    if ($trace.IndexOf('startup-playback-deferred', [StringComparison]::Ordinal) -gt
        $trace.IndexOf('startup-playback-completed', [StringComparison]::Ordinal)) {
        throw '首播完成记录出现在延迟记录之前。'
    }

    Write-Host "PASS startup remained silent behind another app, then autoplayed on desktop; maxAudioTime=$($domResult.maxAudioTime)"
}
finally {
    if ($testWindow) {
        [StartupDeferralAcceptanceNative]::ShowWindow($testWindow.Handle, 0) | Out-Null
    }
    if ($captureProcess -and -not $captureProcess.HasExited) {
        $captureProcess.Kill()
        $captureProcess.WaitForExit()
    }
    if ($captureProcess) {
        $captureProcess.Dispose()
    }
}
