using System.Runtime.InteropServices;
using Accessibility;
using XYDesktopPlayer.Core;

namespace XYDesktopPlayer.App;

internal sealed class DesktopIconMaskProvider : IDisposable
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(750);
    private DesktopIconMask _snapshot = DesktopIconMask.Invalid;
    private System.Threading.Timer? _timer;
    private int _refreshing;
    private bool _disposed;

    public DesktopIconMask Snapshot => Volatile.Read(ref _snapshot);

    public string? LastError { get; private set; }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _timer ??= new System.Threading.Timer(
            _ => RefreshNow(),
            null,
            TimeSpan.Zero,
            RefreshInterval);
    }

    public void RefreshNow()
    {
        if (_disposed || Interlocked.Exchange(ref _refreshing, 1) != 0)
        {
            return;
        }

        try
        {
            var listView = DesktopListViewLocator.Find();
            if (listView == nint.Zero)
            {
                PublishInvalid("未找到 Explorer 桌面图标视图。");
                return;
            }

            if (!DesktopListViewLocator.IsHierarchyVisible(listView))
            {
                Publish(DesktopIconMask.Empty);
                return;
            }

            var nativeItemCount = DesktopListViewLocator.GetItemCount(listView);
            var rectangles = ReadAccessibleIconRectangles(listView);

            if (!DesktopIconSnapshotPolicy.IsUsable(nativeItemCount, rectangles.Count))
            {
                PublishInvalid("Explorer 报告存在桌面图标，但辅助功能接口未能读取其遮罩范围。");
                return;
            }

            Publish(new DesktopIconMask(true, rectangles));
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or InvalidCastException or ArgumentException or COMException)
        {
            PublishInvalid($"读取桌面图标范围失败：{exception.Message}");
        }
        finally
        {
            Volatile.Write(ref _refreshing, 0);
        }
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
        Volatile.Write(ref _snapshot, DesktopIconMask.Invalid);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
    }

    private void Publish(DesktopIconMask snapshot)
    {
        LastError = null;
        Volatile.Write(ref _snapshot, snapshot);
    }

    private void PublishInvalid(string error)
    {
        LastError = error;
        Volatile.Write(ref _snapshot, DesktopIconMask.Invalid);
    }

    private static List<DesktopRectangle> ReadAccessibleIconRectangles(nint listView)
    {
        var accessibleId = typeof(IAccessible).GUID;
        var result = NativeAccessibility.AccessibleObjectFromWindow(
            listView,
            NativeAccessibility.ObjIdClient,
            ref accessibleId,
            out var accessible);
        if (result < 0)
        {
            Marshal.ThrowExceptionForHR(result);
        }

        if (accessible is null)
        {
            throw new InvalidOperationException("Explorer 桌面图标视图没有提供辅助功能对象。");
        }

        try
        {
            var rectangles = new List<DesktopRectangle>(accessible.accChildCount);
            for (var childId = 1; childId <= accessible.accChildCount; childId++)
            {
                try
                {
                    var state = Convert.ToInt32(accessible.get_accState(childId));
                    if ((state & (NativeAccessibility.StateInvisible | NativeAccessibility.StateOffscreen)) != 0)
                    {
                        continue;
                    }

                    accessible.accLocation(
                        out var left,
                        out var top,
                        out var width,
                        out var height,
                        childId);
                    if (width <= 0 || height <= 0)
                    {
                        continue;
                    }

                    rectangles.Add(new DesktopRectangle(
                        left,
                        top,
                        checked(left + width),
                        checked(top + height)));
                }
                catch (Exception exception) when (
                    exception is InvalidCastException or ArgumentException or COMException)
                {
                    // Explorer can replace one icon while the snapshot is being read.
                }
            }

            return rectangles;
        }
        finally
        {
            if (Marshal.IsComObject(accessible))
            {
                _ = Marshal.ReleaseComObject(accessible);
            }
        }
    }

    private static class NativeAccessibility
    {
        internal const uint ObjIdClient = unchecked((uint)-4);
        internal const int StateInvisible = 0x00008000;
        internal const int StateOffscreen = 0x00010000;

        [DllImport("oleacc.dll")]
        internal static extern int AccessibleObjectFromWindow(
            nint window,
            uint objectId,
            ref Guid interfaceId,
            [MarshalAs(UnmanagedType.Interface)] out IAccessible? accessible);
    }

    private static class DesktopListViewLocator
    {
        private static nint _foundListView;
        private const uint LvmGetItemCount = 0x1004;

        public static nint Find()
        {
            _foundListView = nint.Zero;
            _ = NativeMethods.EnumWindows(FindOnTopLevelWindow, nint.Zero);
            return _foundListView;
        }

        public static bool IsHierarchyVisible(nint window)
        {
            var current = window;
            while (current != nint.Zero)
            {
                if (!NativeMethods.IsWindowVisible(current))
                {
                    return false;
                }

                var className = NativeMethods.GetClassName(current);
                if (className is "WorkerW" or "Progman")
                {
                    break;
                }

                current = NativeMethods.GetParent(current);
            }

            return true;
        }

        public static int GetItemCount(nint listView) =>
            checked((int)NativeMethods.SendMessage(
                listView,
                LvmGetItemCount,
                nint.Zero,
                nint.Zero));

        private static bool FindOnTopLevelWindow(nint window, nint parameter)
        {
            var shellView = NativeMethods.FindWindowEx(
                window,
                nint.Zero,
                "SHELLDLL_DefView",
                null);
            if (shellView == nint.Zero)
            {
                return true;
            }

            var listView = NativeMethods.FindWindowEx(
                shellView,
                nint.Zero,
                "SysListView32",
                "FolderView");
            if (listView == nint.Zero)
            {
                listView = NativeMethods.FindWindowEx(
                    shellView,
                    nint.Zero,
                    "SysListView32",
                    null);
            }

            if (listView == nint.Zero)
            {
                return true;
            }

            _foundListView = listView;
            return false;
        }

        private static class NativeMethods
        {
            internal delegate bool EnumWindowsProc(nint window, nint parameter);

            [DllImport("user32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool EnumWindows(EnumWindowsProc callback, nint parameter);

            [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            internal static extern nint FindWindowEx(
                nint parent,
                nint childAfter,
                string? className,
                string? windowName);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool IsWindowVisible(nint window);

            [DllImport("user32.dll")]
            internal static extern nint GetParent(nint window);

            [DllImport("user32.dll")]
            internal static extern nint SendMessage(
                nint window,
                uint message,
                nint wParam,
                nint lParam);

            [DllImport("user32.dll", CharSet = CharSet.Unicode)]
            private static extern int GetClassName(nint window, char[] className, int maxCount);

            internal static string GetClassName(nint window)
            {
                var buffer = new char[256];
                var length = GetClassName(window, buffer, buffer.Length);
                return length > 0 ? new string(buffer, 0, length) : string.Empty;
            }
        }
    }
}
