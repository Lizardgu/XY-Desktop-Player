using System.Runtime.InteropServices;
using System.Windows.Automation;
using NikkiDesktop.Core;

namespace NikkiDesktop.App;

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

            var listElement = AutomationElement.FromHandle(listView);
            var nativeItemCount = DesktopListViewLocator.GetItemCount(listView);
            var itemCondition = new PropertyCondition(
                AutomationElement.ControlTypeProperty,
                ControlType.ListItem);
            var items = listElement.FindAll(TreeScope.Descendants, itemCondition);
            var rectangles = new List<DesktopRectangle>(items.Count);

            for (var index = 0; index < items.Count; index++)
            {
                try
                {
                    var item = items[index];
                    if (item.Current.IsOffscreen)
                    {
                        continue;
                    }

                    var bounds = item.Current.BoundingRectangle;
                    if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0)
                    {
                        continue;
                    }

                    rectangles.Add(new DesktopRectangle(
                        (int)Math.Floor(bounds.Left),
                        (int)Math.Floor(bounds.Top),
                        (int)Math.Ceiling(bounds.Right),
                        (int)Math.Ceiling(bounds.Bottom)));
                }
                catch (ElementNotAvailableException)
                {
                    // Explorer can replace an icon element while the snapshot is being read.
                }
            }

            if (!DesktopIconSnapshotPolicy.IsUsable(nativeItemCount, rectangles.Count))
            {
                PublishInvalid("Explorer 报告存在桌面图标，但未能读取其遮罩范围。");
                return;
            }

            Publish(new DesktopIconMask(true, rectangles));
        }
        catch (Exception exception) when (
            exception is ElementNotAvailableException or InvalidOperationException or COMException)
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
