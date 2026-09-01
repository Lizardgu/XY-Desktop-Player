using System.Text.Json;
using Microsoft.Web.WebView2.WinForms;
using XYDesktopPlayer.Core;

namespace XYDesktopPlayer.App;

internal sealed class FullscreenPlaybackController : IDisposable
{
    private const int PollIntervalMilliseconds = 500;
    private readonly WebView2 _webView;
    private readonly FullscreenWindowDetector _detector;
    private readonly System.Windows.Forms.Timer _timer;
    private bool _checking;
    private bool _wasFullscreen;
    private bool _autoPauseArmed;
    private bool _startupPending;
    private Func<Task>? _startPendingPlayback;
    private bool _disposed;

    public FullscreenPlaybackController(
        WebView2 webView,
        FullscreenWindowDetector? detector = null)
    {
        _webView = webView ?? throw new ArgumentNullException(nameof(webView));
        _detector = detector ?? new FullscreenWindowDetector();
        _timer = new System.Windows.Forms.Timer
        {
            Interval = PollIntervalMilliseconds
        };
        _timer.Tick += OnTimerTick;
    }

    public bool IsRunning => _timer.Enabled;

    public bool IsFullscreen => _wasFullscreen;

    public bool AutoPauseArmed => _autoPauseArmed;

    public bool StartupPending => _startupPending;

    public int PauseCount { get; private set; }

    public int ResumeCount { get; private set; }

    public int MaximizedWindowObservationCount { get; private set; }

    public string? LastError { get; private set; }

    public bool IsPlaybackBlocked()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return FullscreenWindowPolicy.IsOtherFullscreen(_detector.CaptureContext());
    }

    public void Start(
        bool startupPending = false,
        Func<Task>? startPendingPlayback = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _startupPending = startupPending;
        _startPendingPlayback = startupPending ? startPendingPlayback : null;
        _wasFullscreen = startupPending;
        _autoPauseArmed = false;
        if (_timer.Enabled)
        {
            return;
        }

        LastError = null;
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
        _wasFullscreen = false;
        _autoPauseArmed = false;
        _startupPending = false;
        _startPendingPlayback = null;
        if (!_disposed && _webView.CoreWebView2 is not null)
        {
            _ = ClearStoredAudioAsync();
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
        _timer.Tick -= OnTimerTick;
        _timer.Dispose();
    }

    private async void OnTimerTick(object? sender, EventArgs eventArgs)
    {
        if (_checking || _disposed)
        {
            return;
        }

        _checking = true;
        try
        {
            var windowContext = _detector.CaptureContext();
            if (windowContext.IsMaximized)
            {
                MaximizedWindowObservationCount++;
            }

            var isFullscreen = FullscreenWindowPolicy.IsOtherFullscreen(windowContext);
            var action = FullscreenPlaybackPolicy.Decide(
                new FullscreenPlaybackState(
                    _wasFullscreen,
                    isFullscreen,
                    _autoPauseArmed,
                    _startupPending));

            switch (action)
            {
                case FullscreenPlaybackAction.Pause:
                    await EvaluateAsync("window.__xyDesktopPauseForFullscreen?.() ?? 0");
                    _autoPauseArmed = true;
                    _wasFullscreen = true;
                    PauseCount++;
                    break;

                case FullscreenPlaybackAction.Resume:
                    await EvaluateAsync("window.__xyDesktopResumeAfterFullscreen?.() ?? 0");
                    _autoPauseArmed = false;
                    _wasFullscreen = false;
                    ResumeCount++;
                    break;

                case FullscreenPlaybackAction.Start:
                    var startPendingPlayback = _startPendingPlayback;
                    _startupPending = false;
                    _startPendingPlayback = null;
                    _autoPauseArmed = false;
                    _wasFullscreen = false;
                    if (startPendingPlayback is not null)
                    {
                        await startPendingPlayback();
                    }
                    break;

                default:
                    _wasFullscreen = isFullscreen;
                    break;
            }

            LastError = null;
        }
        catch (Exception exception)
        {
            LastError = exception.Message;
        }
        finally
        {
            _checking = false;
        }
    }

    private Task<string> EvaluateAsync(string expression)
    {
        var request = JsonSerializer.Serialize(new
        {
            expression = $"(async () => await ({expression}))()",
            awaitPromise = true,
            userGesture = true,
            returnByValue = true
        });
        return _webView.CoreWebView2.CallDevToolsProtocolMethodAsync(
            "Runtime.evaluate",
            request);
    }

    private async Task ClearStoredAudioAsync()
    {
        try
        {
            await _webView.CoreWebView2.ExecuteScriptAsync(
                "window.__xyDesktopClearFullscreenPause?.()");
        }
        catch
        {
            // Navigation and shutdown are allowed to invalidate the page first.
        }
    }
}
