using System.Runtime.InteropServices;
using System.Text;
using NikkiDesktop.Core;

namespace NikkiDesktop.App;

internal sealed class FullscreenWindowDetector
{
    private const uint GaRoot = 2;
    private const uint MonitorDefaultToNearest = 2;
    private const uint DwmwaExtendedFrameBounds = 9;
    private const uint DwmwaCloaked = 14;
    private readonly uint _currentProcessId = (uint)Environment.ProcessId;

    public FullscreenWindowContext CaptureContext()
    {
        try
        {
            var foreground = NativeMethods.GetForegroundWindow();
            if (foreground == nint.Zero)
            {
                return default;
            }

            var root = NativeMethods.GetAncestor(foreground, GaRoot);
            if (root == nint.Zero)
            {
                root = foreground;
            }

            _ = NativeMethods.GetWindowThreadProcessId(root, out var processId);
            var className = new StringBuilder(256);
            _ = NativeMethods.GetClassName(root, className, className.Capacity);

            var visible = NativeMethods.IsWindowVisible(root);
            var minimized = NativeMethods.IsIconic(root);
            var maximized = NativeMethods.IsZoomed(root);
            var cloaked = IsCloaked(root);
            if (!TryGetWindowBounds(root, out var windowBounds))
            {
                return default;
            }

            var monitor = NativeMethods.MonitorFromWindow(root, MonitorDefaultToNearest);
            if (monitor == nint.Zero)
            {
                return default;
            }

            var monitorInfo = new MonitorInfo
            {
                Size = (uint)Marshal.SizeOf<MonitorInfo>()
            };
            if (!NativeMethods.GetMonitorInfo(monitor, ref monitorInfo))
            {
                return default;
            }

            return new FullscreenWindowContext(
                HasForegroundWindow: true,
                IsOwnProcess: processId == _currentProcessId,
                IsDesktopSurface: ForegroundShellSurfacePolicy.IsShellSurface(className.ToString()),
                IsVisible: visible,
                IsMinimized: minimized,
                IsCloaked: cloaked,
                IsMaximized: maximized,
                ToDesktopRectangle(windowBounds),
                ToDesktopRectangle(monitorInfo.Monitor));
        }
        catch
        {
            return default;
        }
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

    private static bool TryGetWindowBounds(nint window, out NativeRectangle bounds)
    {
        var result = NativeMethods.DwmGetWindowAttribute(
            window,
            DwmwaExtendedFrameBounds,
            out bounds,
            Marshal.SizeOf<NativeRectangle>());
        return result == 0 || NativeMethods.GetWindowRect(window, out bounds);
    }

    private static DesktopRectangle ToDesktopRectangle(NativeRectangle rectangle) =>
        new(rectangle.Left, rectangle.Top, rectangle.Right, rectangle.Bottom);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint Size;
        public NativeRectangle Monitor;
        public NativeRectangle WorkArea;
        public uint Flags;
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        internal static extern nint GetForegroundWindow();

        [DllImport("user32.dll")]
        internal static extern nint GetAncestor(nint window, uint flags);

        [DllImport("user32.dll")]
        internal static extern uint GetWindowThreadProcessId(nint window, out uint processId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int GetClassName(nint window, StringBuilder className, int maxCount);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindowVisible(nint window);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsIconic(nint window);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsZoomed(nint window);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetWindowRect(nint window, out NativeRectangle rectangle);

        [DllImport("user32.dll")]
        internal static extern nint MonitorFromWindow(nint window, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo monitorInfo);

        [DllImport("dwmapi.dll")]
        internal static extern int DwmGetWindowAttribute(
            nint window,
            uint attribute,
            ref int value,
            int valueSize);

        [DllImport("dwmapi.dll")]
        internal static extern int DwmGetWindowAttribute(
            nint window,
            uint attribute,
            out NativeRectangle value,
            int valueSize);
    }
}
