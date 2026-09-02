using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using XYDesktopPlayer.Core;

namespace XYDesktopPlayer.App;

/// <summary>
/// Detects whether any program window is visible above the desktop, by enumerating
/// every top-level window instead of trusting a single foreground window.
///
/// The old <see cref="ForegroundWindowDetector"/> only inspected
/// <c>GetForegroundWindow()</c>. That missed any visible window that was not currently
/// focused (multi-monitor, no-activate windows, windows that opened while focus was
/// still on the taskbar or desktop), which is exactly why "open a program → sometimes
/// does not pause" happened. It also returned a non-blocking result on any Win32
/// exception, so one odd window class silently disabled pausing entirely.
///
/// This detector enumerates all top-level windows and reports a block when at least one
/// window is visible, not minimized, not cloaked, not our own process, not a desktop or
/// shell surface, and actually intersects a monitor.
/// </summary>
internal sealed class WindowPresenceDetector
{
    private const uint GaRoot = 2;
    private const uint DwmwaCloaked = 14;
    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;
    private const uint GwlOwner = 4;
    private const long WsVisible = 0x10000000L;
    private const long WsExToolWindow = 0x00000080L;
    private const uint MonitorDefaultToNull = 0x0000;

    private static readonly HashSet<string> DesktopOrShellClasses = new(StringComparer.Ordinal)
    {
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "#32768"
    };

    private readonly uint _currentProcessId = (uint)Environment.ProcessId;

    public WindowPresenceResult Detect()
    {
        var candidates = new List<BlockingWindowInfo>();
        var foregroundHandle = nint.Zero;
        ForegroundInfo foreground;
        try
        {
            foregroundHandle = NativeMethods.GetForegroundWindow();
            foreground = foregroundHandle == nint.Zero
                ? EmptyForeground()
                : Describe(foregroundHandle, isForeground: true);
            _ = NativeMethods.EnumWindows((window, _) =>
            {
                try
                {
                    var info = Classify(window);
                    if (info is not null)
                    {
                        candidates.Add(info);
                    }
                }
                catch
                {
                    // A single misbehaving window must never abort the whole scan.
                }

                return true;
            }, nint.Zero);
        }
        catch (Exception exception)
        {
            // If enumeration itself fails, do not claim a block (which would freeze
            // playback); surface the failure through the diagnostic instead.
            return new WindowPresenceResult(
                IsBlocked: false,
                Context: DesktopContext(),
                Foreground: EmptyForeground(),
                BlockingWindows: Array.Empty<BlockingWindowInfo>(),
                Error: exception.Message);
        }

        // A normal desktop always has a few helper windows (launchers, GPU/overlay panels,
        // tray apps) that stay WS_VISIBLE but are not what the user is actually working in.
        // Blocking on *any* visible window therefore froze playback permanently. We only
        // block when:
        //   1. the foreground window is a real program (the user is actively in another app), OR
        //   2. a visible foreign window covers most of a monitor (a big app opened behind
        //      focus, e.g. while focus stayed on the taskbar/desktop).
        // Tiny tray helpers and 1x1 off-screen windows satisfy neither, so the wallpaper
        // keeps playing whenever the user is on the desktop.
        var foregroundRoot = foregroundHandle == nint.Zero
            ? nint.Zero
            : NativeMethods.GetAncestor(foregroundHandle, GaRoot);
        var foregroundIsReal =
            foregroundRoot != nint.Zero && candidates.Any(c => c.Handle == foregroundRoot);
        var covering = candidates.FirstOrDefault(c =>
            c.Handle != foregroundRoot && CoversMajorityOfMonitor(c));

        var isBlocked = foregroundIsReal || covering is not null;
        return new WindowPresenceResult(
            IsBlocked: isBlocked,
            Context: isBlocked ? BlockingContext() : DesktopContext(),
            Foreground: foreground,
            BlockingWindows: candidates,
            Error: null);
    }

    private static ForegroundInfo EmptyForeground() =>
        new(
            Handle: nint.Zero, RootHandle: nint.Zero, ProcessId: 0, ProcessName: null,
            ClassName: null, Title: null, Visible: false, Minimized: false, Cloaked: false,
            IsOwnProcess: false, IsDesktopSurface: false, IsShellOverlay: false);

    private BlockingWindowInfo? Classify(nint window)
    {
        var root = NativeMethods.GetAncestor(window, GaRoot);
        if (root == nint.Zero)
        {
            root = window;
        }

        _ = NativeMethods.GetWindowThreadProcessId(root, out var processId);
        if (processId == _currentProcessId)
        {
            return null;
        }

        var className = ReadClassName(root);
        if (string.IsNullOrEmpty(className) || DesktopOrShellClasses.Contains(className))
        {
            return null;
        }

        if (!NativeMethods.IsWindowVisible(root))
        {
            return null;
        }

        if (NativeMethods.IsIconic(root))
        {
            return null;
        }

        if (IsCloaked(root))
        {
            return null;
        }

        var exStyle = (long)NativeMethods.GetWindowLongPtr(root, GwlExStyle);
        if ((exStyle & WsExToolWindow) != 0)
        {
            // Tool windows (floating palettes, helper overlays) are not the program the
            // user is actively working in, so they should not force a pause.
            return null;
        }

        if (!NativeMethods.GetWindowRect(root, out var rect))
        {
            return null;
        }

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        if (NativeMethods.MonitorFromWindow(root, MonitorDefaultToNull) == nint.Zero)
        {
            // Window exists but is not on any monitor: hidden, off-screen, or a 0-size helper.
            return null;
        }

        var info = Describe(root, isForeground: false);
        return new BlockingWindowInfo(
            Handle: root,
            ProcessId: processId,
            ProcessName: info.ProcessName,
            ClassName: className,
            Title: info.Title,
            Left: rect.Left,
            Top: rect.Top,
            Right: rect.Right,
            Bottom: rect.Bottom,
            Visible: true,
            Minimized: false,
            Cloaked: info.Cloaked);
    }

    private ForegroundInfo Describe(nint window, bool isForeground)
    {
        _ = NativeMethods.GetWindowThreadProcessId(window, out var processId);
        var className = ReadClassName(window);
        string? processName = null;
        try
        {
            using var process = Process.GetProcessById((int)processId);
            processName = process.ProcessName;
        }
        catch
        {
            // Cross-session or already-exited processes are fine to leave unnamed.
        }

        var title = ReadWindowText(window);
        return new ForegroundInfo(
            Handle: isForeground ? window : nint.Zero,
            RootHandle: window,
            ProcessId: processId,
            ProcessName: processName,
            ClassName: className,
            Title: title,
            Visible: NativeMethods.IsWindowVisible(window),
            Minimized: NativeMethods.IsIconic(window),
            Cloaked: IsCloaked(window),
            IsOwnProcess: processId == _currentProcessId,
            IsDesktopSurface: className is "Progman" or "WorkerW",
            IsShellOverlay: className is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "#32768");
    }

    private static bool CoversMajorityOfMonitor(BlockingWindowInfo window)
    {
        var monitor = NativeMethods.MonitorFromWindow(window.Handle, MonitorDefaultToNull);
        if (monitor == nint.Zero)
        {
            return false;
        }

        var info = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
        if (!NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            return false;
        }

        var monitorArea = (long)(info.Right - info.Left) * (info.Bottom - info.Top);
        if (monitorArea <= 0)
        {
            return false;
        }

        var windowArea = (long)(window.Right - window.Left) * (window.Bottom - window.Top);
        return windowArea >= monitorArea / 2;
    }

    private static bool IsCloaked(nint window)
    {
        var cloaked = 0;
        var result = NativeMethods.DwmGetWindowAttribute(
            window,
            DwmwaCloaked,
            ref cloaked,
            Marshal.SizeOf<int>());
        return result == 0 && cloaked != 0;
    }

    private static string ReadClassName(nint window)
    {
        var buffer = new StringBuilder(256);
        var length = NativeMethods.GetClassName(window, buffer, buffer.Capacity);
        return length > 0 ? buffer.ToString() : string.Empty;
    }

    private static string ReadWindowText(nint window)
    {
        try
        {
            // GetWindowText can hang for seconds if the target window's thread is busy or
            // crashed. SendMessageTimeout with SMTO_ABORTIFHUNG bounds that to 100 ms so a
            // single misbehaving window can never stall the playback UI thread.
            const uint WmGetTextLength = 0x000E;
            const uint WmGetText = 0x000D;
            const uint SmtoAbortIfHung = 0x0002;
            var lengthResult = NativeMethods.SendMessageTimeout(
                window,
                WmGetTextLength,
                nint.Zero,
                null,
                SmtoAbortIfHung,
                100,
                out var length);
            if (lengthResult == nint.Zero || length <= 0)
            {
                return string.Empty;
            }

            var buffer = new StringBuilder((int)length + 1);
            NativeMethods.SendMessageTimeout(
                window,
                WmGetText,
                (nint)(length + 1),
                buffer,
                SmtoAbortIfHung,
                100,
                out _);
            return buffer.ToString();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static ForegroundWindowContext BlockingContext() =>
        new(
            HasForegroundWindow: true,
            IsOwnProcess: false,
            IsDesktopSurface: false,
            IsShellOverlay: false,
            IsVisible: true,
            IsMinimized: false,
            IsCloaked: false);

    private static ForegroundWindowContext DesktopContext() =>
        new(
            HasForegroundWindow: true,
            IsOwnProcess: false,
            IsDesktopSurface: true,
            IsShellOverlay: false,
            IsVisible: true,
            IsMinimized: false,
            IsCloaked: false);

    private static class NativeMethods
    {
        internal delegate bool EnumWindowsProc(nint window, nint parameter);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern nint GetForegroundWindow();

        [DllImport("user32.dll")]
        internal static extern nint GetAncestor(nint window, uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern uint GetWindowThreadProcessId(nint window, out uint processId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern int GetClassName(nint window, StringBuilder className, int maxCount);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindowVisible(nint window);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsIconic(nint window);

        [DllImport("user32.dll")]
        internal static extern nint GetWindowLongPtr(nint window, int index);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetWindowRect(nint window, out NativeRectangle rect);

        [DllImport("user32.dll")]
        internal static extern nint MonitorFromWindow(nint window, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetMonitorInfo(nint hMonitor, ref MonitorInfo info);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern nint SendMessageTimeout(
            nint hWnd,
            uint Msg,
            nint wParam,
            StringBuilder? lParam,
            uint fuFlags,
            uint uTimeout,
            out nint lpdwResult);

        [DllImport("dwmapi.dll")]
        internal static extern int DwmGetWindowAttribute(
            nint window,
            uint attribute,
            ref int value,
            int valueSize);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EnumWindows(EnumWindowsProc callback, nint parameter);
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NativeRectangle(int Left, int Top, int Right, int Bottom);

    [StructLayout(LayoutKind.Sequential)]
    private sealed class MonitorInfo
    {
        public int cbSize;
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
        public int WorkLeft;
        public int WorkTop;
        public int WorkRight;
        public int WorkBottom;
        public uint Flags;
    }
}
