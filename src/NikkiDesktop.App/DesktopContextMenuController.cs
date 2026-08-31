using System.Runtime.InteropServices;

namespace NikkiDesktop.App;

internal sealed class DesktopContextMenuController
{
    private const uint GuiPopupMenuMode = 0x00000010;
    private const uint WmCancelMode = 0x001F;

    public bool IsDesktopMenuOpen() => TryGetDesktopMenuOwner(out _);

    public bool TryDismissDesktopMenu()
    {
        return TryGetDesktopMenuOwner(out var owner) &&
               NativeMethods.PostMessage(owner, WmCancelMode, nint.Zero, nint.Zero);
    }

    private static bool TryGetDesktopMenuOwner(out nint owner)
    {
        var info = new GuiThreadInfo
        {
            Size = checked((uint)Marshal.SizeOf<GuiThreadInfo>())
        };
        if (!NativeMethods.GetGUIThreadInfo(0, ref info) ||
            (info.Flags & GuiPopupMenuMode) == 0 ||
            info.MenuOwner == nint.Zero ||
            !IsDesktopWindow(info.MenuOwner))
        {
            owner = nint.Zero;
            return false;
        }

        owner = info.MenuOwner;
        return true;
    }

    private static bool IsDesktopWindow(nint window)
    {
        var current = window;
        while (current != nint.Zero)
        {
            var className = NativeMethods.ReadClassName(current);
            if (className is "Progman" or "WorkerW" or "SHELLDLL_DefView" or "SysListView32")
            {
                return true;
            }

            current = NativeMethods.GetParent(current);
        }

        return false;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        internal uint Size;
        internal uint Flags;
        internal nint ActiveWindow;
        internal nint FocusWindow;
        internal nint CaptureWindow;
        internal nint MenuOwner;
        internal nint MoveSizeWindow;
        internal nint CaretWindow;
        internal NativeRectangle CaretRectangle;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NativeRectangle(int Left, int Top, int Right, int Bottom);

    private static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetGUIThreadInfo(uint threadId, ref GuiThreadInfo info);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);

        [DllImport("user32.dll")]
        internal static extern nint GetParent(nint window);

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
