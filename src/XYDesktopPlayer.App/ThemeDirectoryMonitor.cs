using XYDesktopPlayer.Core;

namespace XYDesktopPlayer.App;

internal sealed class ThemeDirectoryMonitor : IDisposable
{
    private static readonly TimeSpan DefaultSettleDelay = TimeSpan.FromMilliseconds(900);
    private readonly string _themesRoot;
    private readonly TimeSpan _settleDelay;
    private readonly object _gate = new();
    private FileSystemWatcher? _watcher;
    private System.Threading.Timer? _timer;
    private bool _running;
    private bool _disposed;

    public ThemeDirectoryMonitor(string themesRoot, TimeSpan? settleDelay = null)
    {
        _themesRoot = Path.GetFullPath(themesRoot);
        _settleDelay = settleDelay ?? DefaultSettleDelay;
        if (_settleDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(settleDelay));
        }
    }

    public event EventHandler? RefreshRequested;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_gate)
        {
            if (_watcher is not null)
            {
                return;
            }

            Directory.CreateDirectory(_themesRoot);
            _timer = new System.Threading.Timer(OnSettled);
            _watcher = new FileSystemWatcher(_themesRoot)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.DirectoryName | NotifyFilters.FileName,
                EnableRaisingEvents = false
            };
            _watcher.Created += OnChanged;
            _watcher.Deleted += OnChanged;
            _watcher.Renamed += OnRenamed;
            _watcher.Error += OnError;
            _running = true;
            _watcher.EnableRaisingEvents = true;
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _running = false;
            if (_watcher is not null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Created -= OnChanged;
                _watcher.Deleted -= OnChanged;
                _watcher.Renamed -= OnRenamed;
                _watcher.Error -= OnError;
                _watcher.Dispose();
                _watcher = null;
            }

            _timer?.Dispose();
            _timer = null;
        }
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

    private void OnChanged(object sender, FileSystemEventArgs eventArgs)
    {
        if (ThemeDirectoryChangePolicy.ShouldSchedule(eventArgs.ChangeType))
        {
            Schedule();
        }
    }

    private void OnRenamed(object sender, RenamedEventArgs eventArgs) => Schedule();

    private void OnError(object sender, ErrorEventArgs eventArgs) => Schedule();

    private void Schedule()
    {
        lock (_gate)
        {
            if (!_disposed && _running)
            {
                _timer?.Change(_settleDelay, Timeout.InfiniteTimeSpan);
            }
        }
    }

    private void OnSettled(object? state)
    {
        lock (_gate)
        {
            if (_disposed || !_running)
            {
                return;
            }
        }

        RefreshRequested?.Invoke(this, EventArgs.Empty);
    }
}
