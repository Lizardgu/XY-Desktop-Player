using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Web.WebView2.WinForms;
using XYDesktopPlayer.Core;

namespace XYDesktopPlayer.App;

internal sealed class ForegroundPlaybackController : IDisposable
{
    private const int PollIntervalMilliseconds = 500;
    private readonly WebView2 _webView;
    private readonly WindowPresenceDetector _presence;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly PlaybackDiagnosticLog? _diagnostics;
    private readonly string? _logDirectory;
    private bool _checking;
    private bool _wasBlocked;
    private bool _autoPauseArmed;
    private bool _startupPending;
    private Func<Task<bool>>? _startPendingPlayback;
    private bool _disposed;
    private WindowPresenceResult? _lastResult;

    private readonly List<nint> _winEventHooks = new();
    private WinEventDelegate? _winEventDelegate;

    public ForegroundPlaybackController(
        WebView2 webView,
        WindowPresenceDetector? presence = null,
        string? logDirectory = null)
    {
        _webView = webView ?? throw new ArgumentNullException(nameof(webView));
        _presence = presence ?? new WindowPresenceDetector();
        _logDirectory = logDirectory;
        _diagnostics = logDirectory is null ? null : new PlaybackDiagnosticLog();
        _timer = new System.Windows.Forms.Timer
        {
            Interval = PollIntervalMilliseconds
        };
        _timer.Tick += OnTimerTick;
    }

    public bool IsRunning => _timer.Enabled;

    public bool IsBlocked => _wasBlocked;

    public bool AutoPauseArmed => _autoPauseArmed;

    public bool StartupPending => _startupPending;

    public int PauseCount { get; private set; }

    public int ResumeCount { get; private set; }

    public string? LastError { get; private set; }

    public bool IsPlaybackBlocked()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return ForegroundWindowPolicy.ShouldBlockPlayback(_presence.Detect().Context);
    }

    public bool CanStartOrResumePlayback()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return ForegroundWindowPolicy.CanStartOrResumePlayback(_presence.Detect().Context);
    }

    public void Start(
        bool startupPending = false,
        Func<Task<bool>>? startPendingPlayback = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _startupPending = startupPending;
        _startPendingPlayback = startupPending ? startPendingPlayback : null;
        _wasBlocked = startupPending;
        _autoPauseArmed = false;
        if (_timer.Enabled)
        {
            return;
        }

        LastError = null;
        InstallWindowHooks();
        _timer.Start();
    }

    /// <summary>
    /// Re-arms the deferred-start path so the next tick will attempt to start playback and
    /// keep retrying until it actually succeeds. Used when a start was deferred because a
    /// window was open at audio-ready time.
    /// </summary>
    public void BeginPendingStart(Func<Task<bool>> startPendingPlayback)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _startPendingPlayback = startPendingPlayback ?? throw new ArgumentNullException(nameof(startPendingPlayback));
        _startupPending = true;
        if (!_timer.Enabled)
        {
            LastError = null;
            InstallWindowHooks();
            _timer.Start();
        }
    }

    public void Stop()
    {
        _timer.Stop();
        RemoveWindowHooks();
        _wasBlocked = false;
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

    public string? ExportDiagnostics()
    {
        if (_diagnostics is null || _logDirectory is null)
        {
            return null;
        }

        try
        {
            return _diagnostics.Export(_logDirectory);
        }
        catch
        {
            return null;
        }
    }

    private void OnTimerTick(object? sender, EventArgs eventArgs)
    {
        if (_checking || _disposed)
        {
            return;
        }

        _checking = true;
        try
        {
            _ = EvaluateState("timer");
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

    private async Task EvaluateState(string trigger)
    {
        if (_disposed)
        {
            return;
        }

        var result = _presence.Detect();
        _lastResult = result;
        var windowContext = result.Context;
        var isBlocked = ForegroundWindowPolicy.ShouldBlockPlayback(windowContext);
        var canStartOrResume = ForegroundWindowPolicy.CanStartOrResumePlayback(windowContext);
        var action = ForegroundPlaybackPolicy.Decide(
            new ForegroundPlaybackState(
                _wasBlocked,
                isBlocked,
                _autoPauseArmed,
                canStartOrResume,
                _startupPending));

        switch (action)
        {
            case ForegroundPlaybackAction.Pause:
                await EvaluateAsync("window.__xyDesktopPauseForFullscreen?.() ?? 0");
                _autoPauseArmed = true;
                _wasBlocked = true;
                PauseCount++;
                break;

            case ForegroundPlaybackAction.Resume:
                await EvaluateAsync("window.__xyDesktopResumeAfterFullscreen?.() ?? 0");
                _autoPauseArmed = false;
                _wasBlocked = false;
                ResumeCount++;
                break;

            case ForegroundPlaybackAction.Start:
                var startPendingPlayback = _startPendingPlayback;
                if (startPendingPlayback is null)
                {
                    _startupPending = false;
                    _autoPauseArmed = false;
                    _wasBlocked = false;
                    break;
                }

                // Stay armed until the start actually succeeds. If it returns false (e.g. a
                // window opened during the wait), keep _startupPending so the next tick retries.
                _startupPending = true;
                _autoPauseArmed = false;
                _wasBlocked = false;
                var started = await startPendingPlayback();
                if (started)
                {
                    _startupPending = false;
                    _autoPauseArmed = false;
                    _wasBlocked = false;
                }
                break;

            default:
                if (isBlocked || canStartOrResume)
                {
                    _wasBlocked = isBlocked;
                }

                break;
        }

        LastError = null;
        RecordDiagnostic(trigger, action, result);
    }

    private void RecordDiagnostic(
        string trigger,
        ForegroundPlaybackAction action,
        WindowPresenceResult result)
    {
        if (_diagnostics is null)
        {
            return;
        }

        _diagnostics.Record(new DiagnosticSample(
            Timestamp: DateTimeOffset.Now,
            Trigger: trigger,
            IsBlocked: result.IsBlocked,
            Action: action.ToString(),
            Foreground: result.Foreground,
            BlockingWindowCount: result.BlockingWindows.Count,
            BlockingWindows: result.BlockingWindows,
            WasBlocked: _wasBlocked,
            AutoPauseArmed: _autoPauseArmed,
            StartupPending: _startupPending,
            DetectorError: result.Error));
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
        return _webView.CoreWebView2!.CallDevToolsProtocolMethodAsync(
            "Runtime.evaluate",
            request);
    }

    private async Task ClearStoredAudioAsync()
    {
        try
        {
            await _webView.CoreWebView2!.ExecuteScriptAsync(
                "window.__xyDesktopClearFullscreenPause?.()");
        }
        catch
        {
            // Navigation and shutdown are allowed to invalidate the page first.
        }
    }

    private void InstallWindowHooks()
    {
        if (_winEventDelegate is not null)
        {
            return;
        }

        _winEventDelegate = OnWinEvent;
        _winEventHooks.Add(SetWinEventHook(0x0003, 0x0003, nint.Zero, _winEventDelegate, 0, 0, 0)); // EVENT_SYSTEM_FOREGROUND
        _winEventHooks.Add(SetWinEventHook(0x0017, 0x0017, nint.Zero, _winEventDelegate, 0, 0, 0)); // EVENT_SYSTEM_MINIMIZEEND
        _winEventHooks.Add(SetWinEventHook(0x000B, 0x000B, nint.Zero, _winEventDelegate, 0, 0, 0)); // EVENT_SYSTEM_MOVESIZEEND
    }

    private void RemoveWindowHooks()
    {
        foreach (var handle in _winEventHooks)
        {
            if (handle != nint.Zero)
            {
                _ = UnhookWinEvent(handle);
            }
        }

        _winEventHooks.Clear();
    }

    private void OnWinEvent(
        nint hWinEventHook,
        uint eventType,
        nint hwnd,
        int idObject,
        int idChild,
        uint dwEventTime)
    {
        if (idObject != 0 || idChild != 0)
        {
            return;
        }

        if (_disposed || _checking)
        {
            return;
        }

        if (_webView.IsHandleCreated && _webView.InvokeRequired)
        {
            _webView.BeginInvoke(() => _ = EvaluateState("winevent"));
        }
        else
        {
            _ = EvaluateState("winevent");
        }
    }

    private delegate void WinEventDelegate(
        nint hWinEventHook,
        uint eventType,
        nint hwnd,
        int idObject,
        int idChild,
        uint dwEventTime);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWinEventHook(
        uint eventMin,
        uint eventMax,
        nint hmodWinEventProc,
        WinEventDelegate lpfnWinEventProc,
        uint idProcess,
        uint idThread,
        uint dwFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(nint hWinEventHook);
}
