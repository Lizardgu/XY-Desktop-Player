namespace XYDesktopPlayer.App;

internal sealed class SingleInstanceGuard : IDisposable
{
    private readonly Mutex _mutex;
    private readonly bool _ownsMutex;
    private bool _disposed;

    private SingleInstanceGuard(Mutex mutex, bool ownsMutex)
    {
        _mutex = mutex;
        _ownsMutex = ownsMutex;
    }

    public bool IsPrimaryInstance => _ownsMutex;

    public static SingleInstanceGuard Acquire(bool isolatedCapture = false)
    {
        var name = isolatedCapture
            ? $@"Local\XYDesktopPlayer.Capture.{Environment.ProcessId}"
            : @"Local\XYDesktopPlayer.Singleton";
        var mutex = new Mutex(initiallyOwned: true, name, out var createdNew);
        return new SingleInstanceGuard(mutex, createdNew);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_ownsMutex)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // The process is already shutting down and no longer owns the mutex.
            }
        }

        _mutex.Dispose();
    }
}
