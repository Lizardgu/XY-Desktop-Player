using System.Runtime.InteropServices;
using XYDesktopPlayer.Core;

namespace XYDesktopPlayer.App;

internal sealed class DesktopSurfaceClassifier(nint playerWindow)
{
    private const uint GaRoot = 2;

    public bool IsDesktopSurface(DesktopPoint point)
    {
        var hitWindow = NativeMethods.WindowFromPoint(new NativePoint(point.X, point.Y));
        if (hitWindow == nint.Zero)
        {
            return false;
        }

        if (hitWindow == playerWindow || NativeMethods.IsChild(playerWindow, hitWindow))
        {
            return true;
        }

        var rootWindow = NativeMethods.GetAncestor(hitWindow, GaRoot);
        if (rootWindow == nint.Zero)
        {
            rootWindow = hitWindow;
        }

        var rootClass = NativeMethods.ReadClassName(rootWindow);
        return rootClass is "WorkerW" or "Progman";
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NativePoint(int X, int Y);

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        internal static extern nint WindowFromPoint(NativePoint point);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsChild(nint parent, nint child);

        [DllImport("user32.dll")]
        internal static extern nint GetAncestor(nint window, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(nint window, char[] className, int maxCount);

        internal static string ReadClassName(nint window)
        {
            var buffer = new char[256];
            var length = GetClassName(window, buffer, buffer.Length);
            return length > 0 ? new string(buffer, 0, length) : string.Empty;
        }
    }
}
