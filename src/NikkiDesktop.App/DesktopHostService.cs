using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using NikkiDesktop.Core;

namespace NikkiDesktop.App;

internal sealed class DesktopHostService : IDisposable
{
    private const int GwlStyle = -16;
    private const long WsChild = 0x40000000L;
    private const long WsPopup = unchecked((long)0x80000000L);
    private const uint SpawnWorkerWMessage = 0x052C;
    private const uint SmtoNormal = 0x0000;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpFrameChanged = 0x0020;
    private const int SwHide = 0;
    private const uint RdwInvalidate = 0x0001;
    private const uint RdwErase = 0x0004;
    private const uint RdwAllChildren = 0x0080;
    private const uint RdwUpdateNow = 0x0100;

    private nint _hostWindow;
    private nint _attachedParent;
    private nint _originalParent;
    private nint _originalStyle;
    private bool _attached;

    public bool IsAttached =>
        _attached &&
        _hostWindow != nint.Zero &&
        _attachedParent != nint.Zero &&
        NativeMethods.IsWindow(_hostWindow) &&
        NativeMethods.IsWindow(_attachedParent) &&
        NativeMethods.GetParent(_hostWindow) == _attachedParent;

    public string AttachedParentClassName
    {
        get
        {
            if (!IsAttached)
            {
                return string.Empty;
            }

            var className = new StringBuilder(256);
            _ = NativeMethods.GetClassName(_attachedParent, className, className.Capacity);
            return className.ToString();
        }
    }

    public bool TryAttach(nint hostWindow, Rectangle desktopBounds, out string error)
    {
        error = string.Empty;
        if (hostWindow == nint.Zero || !NativeMethods.IsWindow(hostWindow))
        {
            error = "播放器窗口句柄无效。";
            return false;
        }

        if (_attached)
        {
            Detach();
        }

        var progman = NativeMethods.FindWindow("Progman", null);
        if (progman == nint.Zero)
        {
            error = "找不到 Windows 桌面 Progman 窗口。";
            return false;
        }

        _ = NativeMethods.SendMessageTimeout(
            progman,
            SpawnWorkerWMessage,
            (nint)0xD,
            (nint)0x1,
            SmtoNormal,
            1000,
            out _);
        _ = NativeMethods.SendMessageTimeout(
            progman,
            SpawnWorkerWMessage,
            (nint)0xD,
            nint.Zero,
            SmtoNormal,
            1000,
            out _);
        _ = NativeMethods.SendMessageTimeout(
            progman,
            SpawnWorkerWMessage,
            nint.Zero,
            nint.Zero,
            SmtoNormal,
            1000,
            out _);

        var windows = EnumerateDesktopWindows();
        var workerW = DesktopWindowSelector.SelectWorkerW(windows);
        if (workerW is null || workerW.Value == nint.Zero)
        {
            error = "未找到桌面图标后方的 WorkerW 窗口。";
            return false;
        }

        _hostWindow = hostWindow;
        _originalParent = NativeMethods.GetParent(hostWindow);
        _originalStyle = NativeMethods.GetWindowLongPtr(hostWindow, GwlStyle);

        var childStyle = new nint((_originalStyle.ToInt64() | WsChild) & ~WsPopup);
        _ = NativeMethods.SetWindowLongPtr(hostWindow, GwlStyle, childStyle);

        Marshal.SetLastPInvokeError(0);
        var previousParent = NativeMethods.SetParent(hostWindow, workerW.Value);
        var parentError = Marshal.GetLastPInvokeError();
        if (previousParent == nint.Zero && parentError != 0)
        {
            _ = NativeMethods.SetWindowLongPtr(hostWindow, GwlStyle, _originalStyle);
            error = $"无法把播放器放入桌面层：{new Win32Exception(parentError).Message}";
            return false;
        }

        if (!NativeMethods.SetWindowPos(
                hostWindow,
                nint.Zero,
                desktopBounds.X,
                desktopBounds.Y,
                desktopBounds.Width,
                desktopBounds.Height,
                SwpNoActivate | SwpShowWindow | SwpNoZOrder | SwpFrameChanged))
        {
            var positionError = Marshal.GetLastWin32Error();
            _ = NativeMethods.SetParent(hostWindow, _originalParent);
            _ = NativeMethods.SetWindowLongPtr(hostWindow, GwlStyle, _originalStyle);
            error = $"无法铺满桌面：{new Win32Exception(positionError).Message}";
            return false;
        }

        _attached = true;
        _attachedParent = workerW.Value;
        return true;
    }

    public void Detach(bool hideHostWindow = false)
    {
        if (_hostWindow == nint.Zero)
        {
            return;
        }

        var previousAttachedParent = _attachedParent;
        var progman = NativeMethods.FindWindow("Progman", null);
        if (NativeMethods.IsWindow(_hostWindow))
        {
            if (hideHostWindow)
            {
                _ = NativeMethods.ShowWindow(_hostWindow, SwHide);
            }
            _ = NativeMethods.SetParent(_hostWindow, _originalParent);
            _ = NativeMethods.SetWindowLongPtr(_hostWindow, GwlStyle, _originalStyle);
            _ = NativeMethods.SetWindowPos(
                _hostWindow,
                nint.Zero,
                0,
                0,
                0,
                0,
                SwpNoActivate | SwpNoZOrder | SwpFrameChanged);
        }

        _attached = false;
        _attachedParent = nint.Zero;

        var redrawFlags = RdwInvalidate | RdwErase | RdwAllChildren | RdwUpdateNow;
        if (previousAttachedParent != nint.Zero && NativeMethods.IsWindow(previousAttachedParent))
        {
            _ = NativeMethods.RedrawWindow(previousAttachedParent, nint.Zero, nint.Zero, redrawFlags);
        }
        if (progman != nint.Zero && NativeMethods.IsWindow(progman))
        {
            _ = NativeMethods.RedrawWindow(progman, nint.Zero, nint.Zero, redrawFlags);
        }
    }

    public void Dispose() => Detach();

    private static IReadOnlyList<DesktopTopLevelWindow> EnumerateDesktopWindows()
    {
        var windows = new List<DesktopTopLevelWindow>();
        _ = NativeMethods.EnumWindows((window, _) =>
        {
            var className = new StringBuilder(256);
            _ = NativeMethods.GetClassName(window, className, className.Capacity);
            var shellDefView = NativeMethods.FindWindowEx(
                window,
                nint.Zero,
                "SHELLDLL_DefView",
                null);
            windows.Add(new DesktopTopLevelWindow(
                window,
                className.ToString(),
                shellDefView != nint.Zero));
            return true;
        }, nint.Zero);
        return windows;
    }

    private static class NativeMethods
    {
        internal delegate bool EnumWindowsProc(nint window, nint parameter);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern nint FindWindow(string? className, string? windowName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern nint FindWindowEx(
            nint parent,
            nint childAfter,
            string? className,
            string? windowName);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EnumWindows(EnumWindowsProc callback, nint parameter);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern int GetClassName(nint window, StringBuilder className, int maxCount);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern nint SendMessageTimeout(
            nint window,
            uint message,
            nint wParam,
            nint lParam,
            uint flags,
            uint timeout,
            out nint result);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern nint SetParent(nint child, nint newParent);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern nint GetParent(nint window);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        internal static extern nint GetWindowLongPtr(nint window, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        internal static extern nint SetWindowLongPtr(nint window, int index, nint newValue);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetWindowPos(
            nint window,
            nint insertAfter,
            int x,
            int y,
            int width,
            int height,
            uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindow(nint window);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ShowWindow(nint window, int command);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool RedrawWindow(
            nint window,
            nint updateRectangle,
            nint updateRegion,
            uint flags);
    }
}
