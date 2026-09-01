using System.ComponentModel;
using System.Runtime.InteropServices;
using XYDesktopPlayer.Core;

namespace XYDesktopPlayer.App;

internal readonly record struct ObservedPointerEvent(
    DesktopPointerEventKind Kind,
    DesktopPoint Point,
    int WheelDelta,
    uint Timestamp);

internal sealed class LowLevelMouseObserver : IDisposable
{
    private const int WhMouseLl = 14;
    private const int HcAction = 0;
    private const int WmMouseMove = 0x0200;
    private const int WmLButtonDown = 0x0201;
    private const int WmLButtonUp = 0x0202;
    private const int WmRButtonDown = 0x0204;
    private const int WmRButtonUp = 0x0205;
    private const int WmMouseWheel = 0x020A;

    private readonly Func<ObservedPointerEvent, DesktopPointerAction> _route;
    private readonly NativeMethods.HookProc _callback;
    private nint _hook;
    private bool _disposed;

    public LowLevelMouseObserver(Func<ObservedPointerEvent, DesktopPointerAction> route)
    {
        _route = route;
        _callback = HookCallback;
    }

    public bool IsRunning => _hook != nint.Zero;

    public string? LastError { get; private set; }

    public bool Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsRunning)
        {
            return true;
        }

        _hook = NativeMethods.SetWindowsHookEx(
            WhMouseLl,
            _callback,
            NativeMethods.GetModuleHandle(null),
            0);
        if (_hook != nint.Zero)
        {
            LastError = null;
            return true;
        }

        LastError = new Win32Exception(Marshal.GetLastWin32Error()).Message;
        return false;
    }

    public void Stop()
    {
        if (_hook == nint.Zero)
        {
            return;
        }

        _ = NativeMethods.UnhookWindowsHookEx(_hook);
        _hook = nint.Zero;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Stop();
        _disposed = true;
    }

    private nint HookCallback(int code, nint message, nint data)
    {
        if (code != HcAction || !TryMapMessage((int)message, out var kind))
        {
            return NativeMethods.CallNextHookEx(_hook, code, message, data);
        }

        var hookData = Marshal.PtrToStructure<MouseHookData>(data);
        var wheelDelta = kind == DesktopPointerEventKind.Wheel
            ? unchecked((short)((hookData.MouseData >> 16) & 0xffff))
            : 0;
        var pointerEvent = new ObservedPointerEvent(
            kind,
            new DesktopPoint(hookData.Point.X, hookData.Point.Y),
            wheelDelta,
            hookData.Time);

        try
        {
            var action = _route(pointerEvent);
            if (action is DesktopPointerAction.ForwardAndConsume or
                DesktopPointerAction.DismissMenuForwardAndConsume)
            {
                return (nint)1;
            }
        }
        catch
        {
            // A hook callback must never escape into the Windows input chain.
        }

        return NativeMethods.CallNextHookEx(_hook, code, message, data);
    }

    private static bool TryMapMessage(int message, out DesktopPointerEventKind kind)
    {
        kind = message switch
        {
            WmMouseMove => DesktopPointerEventKind.Move,
            WmLButtonDown => DesktopPointerEventKind.LeftDown,
            WmLButtonUp => DesktopPointerEventKind.LeftUp,
            WmRButtonDown => DesktopPointerEventKind.RightDown,
            WmRButtonUp => DesktopPointerEventKind.RightUp,
            WmMouseWheel => DesktopPointerEventKind.Wheel,
            _ => default
        };
        return message is WmMouseMove or WmLButtonDown or WmLButtonUp or
            WmRButtonDown or WmRButtonUp or WmMouseWheel;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NativePoint(int X, int Y);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct MouseHookData(
        NativePoint Point,
        uint MouseData,
        uint Flags,
        uint Time,
        nuint ExtraInfo);

    private static class NativeMethods
    {
        internal delegate nint HookProc(int code, nint message, nint data);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern nint SetWindowsHookEx(
            int hookType,
            HookProc callback,
            nint module,
            uint threadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UnhookWindowsHookEx(nint hook);

        [DllImport("user32.dll")]
        internal static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern nint GetModuleHandle(string? moduleName);
    }
}
