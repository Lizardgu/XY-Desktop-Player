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
    private bool _deniedLogged;
    private bool _desktopClickPending;
    private bool _startupPending;
    private Func<Task<bool>>? _startPendingPlayback;
    private bool _disposed;
    private WindowPresenceResult? _lastResult;

    private readonly List<nint> _winEventHooks = new();
    private readonly Func<bool>? _isWallpaperMode;
    private readonly Func<nint>? _getPlayerWindowHandle;
    private WinEventDelegate? _winEventDelegate;

    public ForegroundPlaybackController(
        WebView2 webView,
        WindowPresenceDetector? presence = null,
        string? logDirectory = null,
        Func<bool>? isWallpaperMode = null,
        Func<nint>? getPlayerWindowHandle = null)
    {
        _webView = webView ?? throw new ArgumentNullException(nameof(webView));
        _presence = presence ?? new WindowPresenceDetector();
        _logDirectory = logDirectory;
        _isWallpaperMode = isWallpaperMode;
        _getPlayerWindowHandle = getPlayerWindowHandle;
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

    /// <summary>
    /// 用户在空白桌面点了左键(已被转发给播放器):这是明确的“回到桌面”信号,
    /// 立即尝试恢复被自动暂停的音乐(手动暂停不受影响)。
    /// </summary>
    public void NotifyDesktopInteraction()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _desktopClickPending = true;
        if (!_timer.Enabled)
        {
            return;
        }

        if (_webView.IsHandleCreated && _webView.InvokeRequired)
        {
            _webView.BeginInvoke(() => _ = EvaluateState("desktopclick"));
        }
        else
        {
            _ = EvaluateState("desktopclick");
        }
    }

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
        ApplicationLog.Write($"xy-diag fgp-start startupPending={startupPending}");
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

        ApplicationLog.Write("xy-diag fgp-begin-pending-start");
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

        ApplicationLog.Write("xy-diag fgp-stop");
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
            ApplicationLog.Write($"xy-diag fgp-timer-error: {exception}");
        }
        finally
        {
            _checking = false;
        }
    }

    private async Task EvaluateState(string trigger)
    {
        try
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
            var wallpaperMode = _isWallpaperMode?.Invoke() ?? false;
            var foreground = result.Foreground;
            // 恢复只认两条路(按用户要求):
            //   1. Windows+D 等把焦点交给桌面本体(Progman/WorkerW);
            //   2. 鼠标在空白桌面点了左键(desktopclick,桌面交互转发时置位)。
            // 其余情况一律保持暂停。
            var desktopClick = _desktopClickPending;
            _desktopClickPending = false;
            var foregroundAllowsWallpaperResume =
                wallpaperMode && (foreground.IsDesktopSurface || desktopClick);
            var action = ForegroundPlaybackPolicy.Decide(
                new ForegroundPlaybackState(
                    _wasBlocked,
                    isBlocked,
                    _autoPauseArmed,
                    canStartOrResume,
                    _startupPending,
                    WallpaperMode: wallpaperMode,
                    ForegroundAllowsWallpaperResume: foregroundAllowsWallpaperResume));

            var foregroundDetail =
                $"fgClass={foreground.ClassName} fgOwn={foreground.IsOwnProcess} " +
                $"fgDesk={foreground.IsDesktopSurface} fgShell={foreground.IsShellOverlay} " +
                $"fgEmpty={foreground.Handle == nint.Zero} click={desktopClick}";

            // 点击桌面时按用户意图恢复一次:不要求窗口已关闭(焦点可能仍在别的程序上)。
            // 手动暂停由恢复脚本内的 manualPaused 检查兜底,不会被误恢复。
            if (desktopClick &&
                wallpaperMode &&
                _autoPauseArmed &&
                action == ForegroundPlaybackAction.None)
            {
                action = ForegroundPlaybackAction.Resume;
                ApplicationLog.Write($"xy-diag fgp=resume force-desktopclick {foregroundDetail}");
            }

            switch (action)
            {
                case ForegroundPlaybackAction.Pause:
                    _deniedLogged = false;
                    ApplicationLog.Write(
                        $"xy-diag fgp=pause trigger={trigger} blockedWindows={result.BlockingWindows.Count} {foregroundDetail}");
                    // Mark the pause request immediately and run the fade asynchronously so a
                    // quick return to the desktop can cancel it before the audio is paused.
                    _autoPauseArmed = true;
                    _wasBlocked = true;
                    PauseCount++;
                    await RunImmediateAsync(
                        "window.__xyDesktopSetAutoPauseRequested?.(true); " +
                        "window.__xyDesktopSetAutoPaused?.(true);");
                    _ = EvaluateAsync("window.__xyDesktopPauseForFullscreen?.() ?? 0")
                        .ContinueWith(task =>
                        {
                            if (task.IsFaulted && !_disposed)
                            {
                                LastError = task.Exception?.GetBaseException().Message;
                                ApplicationLog.Write(
                                    $"xy-diag fgp-pause-evaluate-error: {task.Exception?.GetBaseException()}");
                            }
                        }, TaskScheduler.Default);
                    break;

                case ForegroundPlaybackAction.Resume:
                    _deniedLogged = false;
                    ApplicationLog.Write(
                        $"xy-diag fgp=resume trigger={trigger} {foregroundDetail}");
                    // Immediate channel (not the CDP queue): this can cancel a fade that is
                    // still in flight and restart audio without waiting for it to finish.
                    // A manual pause (player button) must never be auto-resumed.
                    await RunImmediateAsync(
                        "window.__xyDesktopSetAutoPauseRequested?.(false); " +
                        "if (window.__xyDesktopGetPlayerState?.().manualPaused) { " +
                        "window.__xyDesktopClearFullscreenPause?.(); } else { " +
                        "window.__xyDesktopResumeAfterFullscreen?.(); } " +
                        "window.__xyDesktopSetAutoPaused?.(false);");
                    _autoPauseArmed = false;
                    _wasBlocked = false;
                    ResumeCount++;
                    break;

                case ForegroundPlaybackAction.Start:
                    _deniedLogged = false;
                    ApplicationLog.Write(
                        $"xy-diag fgp=start trigger={trigger} {foregroundDetail}");
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
                    else
                    {
                        ApplicationLog.Write("xy-diag fgp=start-deferred");
                    }
                    break;

                default:
                    if (isBlocked || canStartOrResume)
                    {
                        _wasBlocked = isBlocked;
                    }

                    if (wallpaperMode &&
                        _wasBlocked &&
                        !isBlocked &&
                        _autoPauseArmed &&
                        !foregroundAllowsWallpaperResume &&
                        !_deniedLogged)
                    {
                        _deniedLogged = true;
                        ApplicationLog.Write(
                            $"xy-diag fgp=resume-denied {foregroundDetail}");
                    }

                    break;
            }

            LastError = null;
            RecordDiagnostic(trigger, action, result);
        }
        catch (Exception exception)
        {
            ApplicationLog.Write($"xy-diag fgp-evaluate-error trigger={trigger}: {exception}");
            throw;
        }
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

    /// <summary>
    /// Runs a script through the direct WebView2 channel instead of the CDP queue, so it can
    /// reach the page even while a long CDP evaluation (like a fade) is still in flight.
    /// </summary>
    private async Task RunImmediateAsync(string expression)
    {
        try
        {
            await _webView.CoreWebView2!.ExecuteScriptAsync(expression);
        }
        catch (Exception exception)
        {
            ApplicationLog.Write($"xy-diag fgp-immediate-error: {exception.Message}");
        }
    }

    private async Task ClearStoredAudioAsync()
    {
        try
        {
            await _webView.CoreWebView2!.ExecuteScriptAsync(
                "window.__xyDesktopSetAutoPaused?.(false); " +
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
