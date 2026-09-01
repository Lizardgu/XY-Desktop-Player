[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$appPath = Join-Path $repoRoot 'src\NikkiDesktop.App\bin\Debug\net8.0-windows\BocchiWallpaperPort.exe'
$contentRoot = Join-Path $repoRoot 'content\reference-player'
$capturePath = Join-Path $repoRoot 'artifacts\smoke\desktop-restoration.png'
$process = $null

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class DesktopRestorationNative
{
    public delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(IntPtr window, StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(IntPtr window);

    public static IntPtr FindWorkerHostingProcess(uint processId)
    {
        IntPtr result = IntPtr.Zero;
        EnumWindows((window, _) =>
        {
            var className = new StringBuilder(64);
            GetClassName(window, className, className.Capacity);
            if (!className.ToString().Equals("WorkerW", StringComparison.Ordinal) || !IsWindowVisible(window))
                return true;

            EnumChildWindows(window, (child, __) =>
            {
                GetWindowThreadProcessId(child, out var childProcessId);
                if (childProcessId == processId)
                {
                    result = window;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            return result == IntPtr.Zero;
        }, IntPtr.Zero);
        return result;
    }
}
'@

if (-not (Test-Path -LiteralPath $appPath -PathType Leaf)) {
    throw "请先构建 Debug 程序：$appPath"
}
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $capturePath) | Out-Null

try {
    $process = Start-Process `
        -FilePath $appPath `
        -ArgumentList @('--mode', 'wallpaper', '--content', $contentRoot, '--capture', $capturePath) `
        -WorkingDirectory $repoRoot `
        -WindowStyle Hidden `
        -PassThru

    $worker = [IntPtr]::Zero
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    while ([DateTime]::UtcNow -lt $deadline -and -not $process.HasExited) {
        $worker = [DesktopRestorationNative]::FindWorkerHostingProcess([uint32]$process.Id)
        if ($worker -ne [IntPtr]::Zero) { break }
        Start-Sleep -Milliseconds 100
    }
    if ($worker -eq [IntPtr]::Zero) {
        throw '没有观察到播放器真实嵌入可见的 WorkerW。'
    }
    if (-not $process.WaitForExit(20000)) {
        throw '桌面恢复验收中的播放器没有按时退出。'
    }
    Start-Sleep -Milliseconds 300
    if ([DesktopRestorationNative]::IsWindowVisible($worker)) {
        throw "播放器退出后，其空 WorkerW 仍然可见：$worker"
    }

    Write-Host "PASS wallpaper WorkerW $worker is hidden after real application exit"
}
finally {
    if ($process -and -not $process.HasExited) {
        $process.Kill()
        $process.WaitForExit()
    }
    if ($process) { $process.Dispose() }
}
