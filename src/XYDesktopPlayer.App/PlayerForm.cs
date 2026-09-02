using System.Text.Json;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using XYDesktopPlayer.Core;

namespace XYDesktopPlayer.App;

internal sealed class PlayerForm : Form
{
    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);
    private readonly WebContentMapping _mapping;
    private readonly ThemePack _builtInTheme;
    private readonly ApplicationPaths _paths;
    private readonly string _userDataRoot;
    private readonly HostMode _requestedMode;
    private readonly string? _capturePath;
    private readonly DesktopHostService _desktopHost = new();
    private readonly int _taskbarCreatedMessage;
    private readonly WebView2 _webView;
    private readonly Label _statusLabel;
    private NotifyIcon? _trayIcon;
    private ContextMenuStrip? _trayMenu;
    private ToolStripMenuItem? _windowModeItem;
    private ToolStripMenuItem? _wallpaperModeItem;
    private ToolStripMenuItem? _desktopInteractionItem;
    private ToolStripMenuItem? _themesItem;
    private DesktopInteractionController? _desktopInteraction;
    private ForegroundPlaybackController? _foregroundPlayback;
    private ThemeDirectoryMonitor? _themeMonitor;
    private ThemeCatalogResult _themeCatalog;
    private ThemePack _currentTheme;
    private HostMode _currentMode;
    private bool _initialized;
    private bool _captureCompleted;
    private bool _webViewReady;
    private bool _themePayloadSent;
    private bool _themeReady;
    private bool _startupPlaybackStarted;
    private string? _startupPlaybackError;
    private bool _desktopInteractionEnabled = true;
    private bool _interactionFailureReported;
    private bool _pendingManualPause;
    private string? _settingsWarning;

    public PlayerForm(
        WebContentMapping mapping,
        ThemePack builtInTheme,
        ApplicationPaths paths,
        string userDataRoot,
        HostMode requestedMode,
        string? capturePath)
    {
        _mapping = mapping;
        _builtInTheme = builtInTheme;
        _paths = paths;
        _userDataRoot = userDataRoot;
        _requestedMode = requestedMode;
        _currentMode = requestedMode;
        _capturePath = capturePath is null ? null : Path.GetFullPath(capturePath);
        var settings = PlayerSettingsStore.Load(_paths.Settings);
        _settingsWarning = settings.Warning;
        _themeCatalog = ThemeCatalog.Scan(_builtInTheme, _paths.Themes);
        _currentTheme = ThemeSelectionPolicy.Select(
            _themeCatalog.Themes,
            settings.Settings.SelectedThemeId,
            defaultThemeId: null,
            _builtInTheme.Id);
        TraceCapture("form-created");
        _taskbarCreatedMessage = NativeMethods.RegisterWindowMessage("TaskbarCreated");

        Text = requestedMode == HostMode.Wallpaper
            ? "XY桌面播放器 - 桌面模式准备中"
            : "XY桌面播放器 - 独立窗口";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = capturePath is null ? new Size(1280, 720) : new Size(1920, 1080);
        MinimumSize = new Size(960, 540);
        BackColor = Color.Black;

        _webView = new WebView2
        {
            Dock = DockStyle.Fill,
            Visible = false
        };
        _statusLabel = new Label
        {
            AutoSize = true,
            BackColor = Color.Black,
            ForeColor = Color.White,
            Font = new Font("Microsoft YaHei UI", 12F),
            Text = requestedMode == HostMode.Wallpaper
                ? "正在嵌入桌面播放器…"
                : "正在启动独立播放器…",
            Anchor = AnchorStyles.None
        };

        Controls.Add(_webView);
        Controls.Add(_statusLabel);
        Resize += (_, _) => CenterStatusLabel();
        CenterStatusLabel();

        if (requestedMode == HostMode.Wallpaper)
        {
            ConfigureWallpaperSurface();
        }

        if (_capturePath is null)
        {
            CreateTrayIcon();
        }
    }

    public int ExitCode { get; private set; }

    protected override async void OnShown(EventArgs eventArgs)
    {
        base.OnShown(eventArgs);
        TraceCapture("form-shown");
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        if (_requestedMode == HostMode.Wallpaper)
        {
            TryAttachToDesktop(showError: _capturePath is null);
        }

        try
        {
            Directory.CreateDirectory(_userDataRoot);
            TraceCapture("creating-webview-environment");
            var environment = await CoreWebView2Environment.CreateAsync(
                userDataFolder: _userDataRoot);
            TraceCapture("webview-environment-created");
            await _webView.EnsureCoreWebView2Async(environment);
            TraceCapture("webview-initialized");

            _webView.CoreWebView2.Settings.AreDevToolsEnabled = true;
            _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            TraceCapture("webview-settings-configured");
            _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                _mapping.VirtualHostName,
                _mapping.ResolvedContentRoot,
                CoreWebView2HostResourceAccessKind.Allow);
            ApplyThemeHostMapping();
            TraceCapture("virtual-host-mapped");
            TraceCapture("adding-document-script");
            await _webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                WallpaperEngineShim.Script);
            TraceCapture("document-script-added");

            _webView.CoreWebView2.NavigationStarting += (_, _) =>
            {
                TraceCapture("navigation-starting");
                _webViewReady = false;
                _themePayloadSent = false;
                _themeReady = false;
                _startupPlaybackStarted = false;
                _startupPlaybackError = null;
                _desktopInteraction?.Stop();
                _foregroundPlayback?.Stop();
                UpdateDesktopInteractionMenu();
            };
            _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
            _webView.NavigationCompleted += OnNavigationCompleted;
            _webView.Source = _mapping.StartUri;
            TraceCapture("navigation-requested");
            _themeMonitor = new ThemeDirectoryMonitor(_paths.Themes);
            _themeMonitor.RefreshRequested += OnThemeRefreshRequested;
            _themeMonitor.Start();
        }
        catch (Exception exception)
        {
            TraceCapture($"startup-error: {exception}");
            _statusLabel.Text = "启动失败";
            CenterStatusLabel();
            MessageBox.Show(
                this,
                exception.Message,
                "XY桌面播放器 - WebView2 启动失败",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs eventArgs)
    {
        TraceCapture($"navigation-completed: success={eventArgs.IsSuccess}; status={eventArgs.WebErrorStatus}");
        if (!eventArgs.IsSuccess)
        {
            _webViewReady = false;
            _desktopInteraction?.Stop();
            _foregroundPlayback?.Stop();
            _statusLabel.Text = $"页面加载失败：{eventArgs.WebErrorStatus}";
            CenterStatusLabel();
            if (_capturePath is not null)
            {
                ExitCode = 5;
                Close();
            }
            return;
        }

        _statusLabel.Visible = false;
        _webView.Visible = true;
        _webViewReady = true;
        Text = _currentMode == HostMode.Wallpaper
            ? "XY桌面播放器 - 桌面模式"
            : "XY桌面播放器 - 独立窗口";
        UpdateDesktopInteraction(showError: false);
    }

    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs eventArgs)
    {
        try
        {
            if (!eventArgs.Source.StartsWith(
                    $"https://{_mapping.VirtualHostName}/",
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            using var document = JsonDocument.Parse(eventArgs.WebMessageAsJson);
            var root = document.RootElement;
            if (!root.TryGetProperty("type", out var type))
            {
                return;
            }

            if (type.GetString() == "player-ready")
            {
                if (_themePayloadSent)
                {
                    return;
                }

                TraceCapture("player-ready");
                await SendCurrentThemeAsync();
                _themePayloadSent = true;
                return;
            }

            if (type.GetString() != "theme-ready" ||
                !root.TryGetProperty("themeId", out var themeId) ||
                themeId.GetString() != _currentTheme.Id)
            {
                return;
            }

            if (_themeReady)
            {
                return;
            }

            _themeReady = true;
            var songCount = root.TryGetProperty("songCount", out var count) ? count.GetInt32() : 0;
            TraceCapture($"theme-ready: id={_currentTheme.Id}; songs={songCount}");
            _foregroundPlayback ??= new ForegroundPlaybackController(_webView, logDirectory: _paths.Logs);
            if (_pendingManualPause)
            {
                await StartupPlaybackService.ApplyManualPausedAsync(_webView.CoreWebView2, paused: true);
                _foregroundPlayback.Start();
                TraceCapture("startup-playback-skipped: manual pause preserved");
            }
            else
            {
                var started = await StartInitialPlaybackAsync();
                _foregroundPlayback.Start();
                if (!started)
                {
                    // Audio became ready while a window was open, or is not ready yet: arm the
                    // deferred-start path so the controller retries once the desktop is clear.
                    _foregroundPlayback.BeginPendingStart(StartInitialPlaybackAsync);
                    TraceCapture("startup-playback-deferred: will retry when the desktop is clear");
                }
            }

            if (_capturePath is not null && !_captureCompleted)
            {
                _captureCompleted = true;
                await CaptureLoadedPlayerAsync(_capturePath);
            }
        }
        catch (Exception exception)
        {
            TraceCapture($"theme-message-error: {exception}");
            _statusLabel.Text = "主题加载失败";
            _statusLabel.Visible = true;
            CenterStatusLabel();
        }
    }

    private Task SendCurrentThemeAsync()
    {
        ApplyThemeHostMapping();
        var payload = ThemeRuntimePayloadFactory.Create(_currentTheme);
        _webView.CoreWebView2.PostWebMessageAsJson(
            JsonSerializer.Serialize(payload, WebJsonOptions));
        TraceCapture($"theme-sent: id={_currentTheme.Id}; songs={payload.Songs.Count}");
        return Task.CompletedTask;
    }

    private void ApplyThemeHostMapping()
    {
        var themeMapping = ThemeWebContentMapping.Create(_currentTheme);
        _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
            themeMapping.VirtualHostName,
            themeMapping.ResolvedAssetRoot,
            CoreWebView2HostResourceAccessKind.Allow);
    }

    private async Task<bool> StartInitialPlaybackAsync()
    {
        var startupPlayback = await StartupPlaybackService.TryStartAsync(
            _webView.CoreWebView2,
            isBlocked: () => _foregroundPlayback?.IsPlaybackBlocked() ?? false);
        _startupPlaybackStarted = startupPlayback.Started;
        _startupPlaybackError = startupPlayback.Error;
        TraceCapture(
            $"startup-playback-completed: started={startupPlayback.Started}; " +
            $"source={startupPlayback.Source}; error={startupPlayback.Error}");
        return startupPlayback.Started;
    }

    private async Task CaptureLoadedPlayerAsync(string capturePath)
    {
        try
        {
            TraceCapture("capture-started");
            await Task.Delay(7000);
            var captureDirectory = Path.GetDirectoryName(capturePath);
            if (!string.IsNullOrEmpty(captureDirectory))
            {
                Directory.CreateDirectory(captureDirectory);
            }

            await using (var stream = File.Create(capturePath))
            {
                await _webView.CoreWebView2.CapturePreviewAsync(
                    CoreWebView2CapturePreviewImageFormat.Png,
                    stream);
            }
            TraceCapture("capture-image-written");

            var domResult = await _webView.CoreWebView2.ExecuteScriptAsync(
                """
                (() => ({
                  title: document.title,
                  readyState: document.readyState,
                  rootChildCount: document.getElementById('root')?.childElementCount ?? 0,
                  imageCount: document.images.length,
                  loadedImageCount: Array.from(document.images).filter(image => image.complete && image.naturalWidth > 0).length,
                  bodyTextLength: document.body?.innerText?.length ?? 0,
                  location: window.location.href,
                  shimInstalled: window.__xyDesktopShimInstalled === true,
                  wallpaperAudioListenerAvailable: typeof window.wallpaperRegisterAudioListener === 'function',
                  trackedAudioCount: window.__xyDesktopTrackedAudio?.length ?? 0,
                  playingAudioCount: window.__xyDesktopTrackedAudio?.filter(audio => !audio.paused).length ?? 0,
                  maxAudioTime: Math.max(0, ...(window.__xyDesktopTrackedAudio?.map(audio => audio.currentTime) ?? [])),
                  fadeDurationMs: window.__xyDesktopFadeDurationMs ?? 0,
                  lastFadeElapsedMs: window.__xyDesktopLastFadeElapsedMs ?? 0
                }))()
                """);
            var domPath = Path.ChangeExtension(capturePath, ".json");
            File.WriteAllText(domPath, domResult);

            var manualPauseRequest = JsonSerializer.Serialize(new
            {
                expression = """
                    (async () => {
                      const tracked = window.__xyDesktopTrackedAudio ?? [];
                      const playable = tracked.find(audio => audio.src && !audio.src.endsWith('/keypress.mp3')) ?? tracked[0];
                      if (!playable) return { tested: false, reason: 'no tracked audio' };
                      window.__xyDesktopClearFullscreenPause?.();
                      playable.pause();
                      const stored = await (window.__xyDesktopPauseForFullscreen?.() ?? -1);
                      const resumed = await (window.__xyDesktopResumeAfterFullscreen?.() ?? 0);
                      return {
                        tested: true,
                        stored,
                        resumed,
                        remainedPaused: playable.paused
                      };
                    })()
                    """,
                awaitPromise = true,
                userGesture = true,
                returnByValue = true
            });
            var manualPauseRaw = await _webView.CoreWebView2.CallDevToolsProtocolMethodAsync(
                "Runtime.evaluate",
                manualPauseRequest);
            using var manualPauseDocument = JsonDocument.Parse(manualPauseRaw);
            var manualPauseValue = manualPauseDocument.RootElement
                .GetProperty("result")
                .GetProperty("value");
            var manualPausePreserved =
                manualPauseValue.GetProperty("tested").GetBoolean() &&
                manualPauseValue.GetProperty("stored").GetInt32() == 0 &&
                manualPauseValue.GetProperty("resumed").GetInt32() == 0 &&
                manualPauseValue.GetProperty("remainedPaused").GetBoolean();

            var hostPath = Path.ChangeExtension(capturePath, ".host.json");
            File.WriteAllText(
                hostPath,
                JsonSerializer.Serialize(
                    new
                    {
                        requestedMode = _requestedMode.ToString().ToLowerInvariant(),
                        currentMode = _currentMode.ToString().ToLowerInvariant(),
                        desktopAttached = _desktopHost.IsAttached,
                        parentClassName = _desktopHost.AttachedParentClassName,
                        desktopInteractionRunning = _desktopInteraction?.IsRunning == true,
                        desktopIconMaskValid = _desktopInteraction?.IconMaskValid == true,
                        desktopIconRectangleCount = _desktopInteraction?.IconRectangleCount ?? 0,
                        desktopInteractionError = _desktopInteraction?.LastError,
                        foregroundMonitorRunning = _foregroundPlayback?.IsRunning == true,
                        foregroundPauseCount = _foregroundPlayback?.PauseCount ?? 0,
                        foregroundResumeCount = _foregroundPlayback?.ResumeCount ?? 0,
                        foregroundMonitorError = _foregroundPlayback?.LastError,
                        startupPlaybackStarted = _startupPlaybackStarted,
                        startupPlaybackError = _startupPlaybackError,
                        selectedThemeId = _currentTheme.Id,
                        themeSongCount = _currentTheme.Songs.Count,
                        themeReady = _themeReady,
                        manualPausePreserved
                    },
                    new JsonSerializerOptions { WriteIndented = true }));

            using var document = JsonDocument.Parse(domResult);
            var root = document.RootElement;
            var ready = root.GetProperty("readyState").GetString() == "complete";
            var hasApp = root.GetProperty("rootChildCount").GetInt32() > 0;
            var hasLoadedImage = root.GetProperty("loadedImageCount").GetInt32() > 0;
            var shimInstalled = root.GetProperty("shimInstalled").GetBoolean();
            var hasTrackedAudio = root.GetProperty("trackedAudioCount").GetInt32() > 0;
            var audioAdvanced = root.GetProperty("maxAudioTime").GetDouble() > 0.25;
            var captureWritten = new FileInfo(capturePath).Length > 0;
            var desktopModeVerified = _requestedMode != HostMode.Wallpaper ||
                                      (_desktopHost.IsAttached &&
                                       _desktopHost.AttachedParentClassName == "WorkerW");
            var themeVerified = _themeReady && _currentTheme.Songs.Count > 0;

            ExitCode = ready && hasApp && hasLoadedImage && shimInstalled &&
                       hasTrackedAudio && audioAdvanced && captureWritten && desktopModeVerified &&
                       themeVerified && manualPausePreserved && _startupPlaybackStarted
                ? 0
                : 6;
            TraceCapture($"capture-finished: exit={ExitCode}");
        }
        catch (Exception exception)
        {
            TraceCapture($"capture-error: {exception}");
            ExitCode = 7;
            var errorPath = Path.ChangeExtension(capturePath, ".error.txt");
            File.WriteAllText(errorPath, exception.ToString());
        }
        finally
        {
            Close();
        }
    }

    private void CenterStatusLabel()
    {
        _statusLabel.Left = Math.Max(0, (ClientSize.Width - _statusLabel.Width) / 2);
        _statusLabel.Top = Math.Max(0, (ClientSize.Height - _statusLabel.Height) / 2);
    }

    private void TraceCapture(string message)
    {
        if (_capturePath is null)
        {
            return;
        }

        try
        {
            var tracePath = Path.ChangeExtension(_capturePath, ".trace.log");
            var directory = Path.GetDirectoryName(tracePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.AppendAllText(
                tracePath,
                $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch
        {
            // Capture diagnostics must never change application behavior.
        }
    }

    protected override void WndProc(ref Message message)
    {
        base.WndProc(ref message);
        if (message.Msg == _taskbarCreatedMessage && _currentMode == HostMode.Wallpaper)
        {
            BeginInvoke(() =>
            {
                _desktopInteraction?.Stop();
                TryAttachToDesktop(showError: false);
            });
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs eventArgs)
    {
        _themeMonitor?.Stop();
        _desktopInteraction?.Stop();
        _foregroundPlayback?.Stop();
        if (_webView.CoreWebView2 is not null)
        {
            _ = StartupPlaybackService.ReleaseAsync(_webView.CoreWebView2);
        }
        _desktopHost.Detach(hideHostWindow: true);
        base.OnFormClosing(eventArgs);
    }

    protected override void OnFormClosed(FormClosedEventArgs eventArgs)
    {
        if (_webView.CoreWebView2 is not null)
        {
            _webView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
        }
        if (_themeMonitor is not null)
        {
            _themeMonitor.RefreshRequested -= OnThemeRefreshRequested;
            _themeMonitor.Dispose();
        }
        _desktopInteraction?.Dispose();
        _foregroundPlayback?.Dispose();
        _trayIcon?.Dispose();
        _trayMenu?.Dispose();
        _desktopHost.Dispose();
        base.OnFormClosed(eventArgs);
    }

    private void CreateTrayIcon()
    {
        _trayMenu = new ContextMenuStrip();
        _windowModeItem = new ToolStripMenuItem(
            "切换-窗口模式",
            image: null,
            (_, _) => SwitchToWindowMode());
        _wallpaperModeItem = new ToolStripMenuItem(
            "切换-桌面模式",
            image: null,
            (_, _) => TryAttachToDesktop(showError: true));
        _trayMenu.Items.Add(_windowModeItem);
        _trayMenu.Items.Add(_wallpaperModeItem);
        _desktopInteractionItem = new ToolStripMenuItem("桌面交互：已开启")
        {
            Checked = true
        };
        _desktopInteractionItem.Click += (_, _) => ToggleDesktopInteraction();
        _trayMenu.Items.Add(_desktopInteractionItem);
        _themesItem = new ToolStripMenuItem("主题");
        _trayMenu.Items.Add(_themesItem);
        RebuildThemeMenu();
        _trayMenu.Items.Add("重新加载播放器", null, async (_, _) => await ReloadCurrentThemeAsync());
        _trayMenu.Items.Add("导出窗口检测诊断", null, (_, _) => ExportWindowDiagnostics());
        _trayMenu.Items.Add(new ToolStripSeparator());
        _trayMenu.Items.Add("退出XY桌面播放器", null, (_, _) => Close());

        _trayIcon = new NotifyIcon
        {
            ContextMenuStrip = _trayMenu,
            Icon = SystemIcons.Application,
            Text = "XY桌面播放器",
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => SwitchToWindowMode();
        UpdateHostModeMenu();
        if (!string.IsNullOrWhiteSpace(_settingsWarning))
        {
            _trayIcon.ShowBalloonTip(
                5000,
                "XY桌面播放器",
                _settingsWarning,
                ToolTipIcon.Warning);
        }
    }

    private void RebuildThemeMenu()
    {
        if (_themesItem is null)
        {
            return;
        }

        _themesItem.DropDownItems.Clear();
        foreach (var entry in ThemeMenuModel.Create(_themeCatalog.Themes, _currentTheme.Id))
        {
            var label = entry.IsBuiltIn ? $"{entry.Name}（内置）" : entry.Name;
            var item = new ToolStripMenuItem(label)
            {
                Checked = entry.IsChecked,
                CheckOnClick = false,
                Tag = entry.Id
            };
            item.Click += async (_, _) => await SwitchThemeAsync(entry.Id);
            _themesItem.DropDownItems.Add(item);
        }

        _themesItem.DropDownItems.Add(new ToolStripSeparator());
        _themesItem.DropDownItems.Add(
            "打开主题安装目录",
            null,
            (_, _) => OpenThemesDirectory());
        _themesItem.DropDownItems.Add(
            "重新扫描主题",
            null,
            (_, _) => RefreshThemeCatalog(showDiagnostics: true));
    }

    private async Task SwitchThemeAsync(string themeId)
    {
        var target = _themeCatalog.Themes.FirstOrDefault(theme =>
            string.Equals(theme.Id, themeId, StringComparison.Ordinal));
        if (target is null || string.Equals(target.Id, _currentTheme.Id, StringComparison.Ordinal))
        {
            return;
        }

        if (_webView.CoreWebView2 is not null)
        {
            _pendingManualPause = _webViewReady && _themeReady &&
                                  await StartupPlaybackService.GetManualPausedAsync(_webView.CoreWebView2);
            await StartupPlaybackService.ReleaseAsync(_webView.CoreWebView2);
        }
        _currentTheme = target;
        var save = PlayerSettingsStore.Save(
            _paths.Settings,
            new PlayerSettings(_currentTheme.Id));
        if (!save.IsSuccess)
        {
            _trayIcon?.ShowBalloonTip(
                5000,
                "XY桌面播放器",
                save.Error ?? "主题选择未能保存。",
                ToolTipIcon.Warning);
        }

        RebuildThemeMenu();
        _webView.CoreWebView2?.Reload();
    }

    private async Task ReloadCurrentThemeAsync()
    {
        if (_webView.CoreWebView2 is null)
        {
            return;
        }

        _pendingManualPause = _webViewReady && _themeReady &&
                              await StartupPlaybackService.GetManualPausedAsync(_webView.CoreWebView2);
        await StartupPlaybackService.ReleaseAsync(_webView.CoreWebView2);
        var refreshed = ThemePackLoader.Load(
            _currentTheme.DefinitionRoot,
            _currentTheme.AssetRoot,
            _currentTheme.IsBuiltIn);
        if (refreshed.IsValid && refreshed.Theme is not null)
        {
            _currentTheme = refreshed.Theme;
            _themeCatalog = new ThemeCatalogResult(
                _themeCatalog.Themes.Select(theme =>
                        string.Equals(theme.Id, _currentTheme.Id, StringComparison.Ordinal)
                            ? _currentTheme
                            : theme)
                    .ToArray(),
                _themeCatalog.Diagnostics);
        }
        else
        {
            _trayIcon?.ShowBalloonTip(
                5000,
                "XY桌面播放器",
                refreshed.Error ?? "当前主题重新加载失败。",
                ToolTipIcon.Warning);
            return;
        }

        RebuildThemeMenu();
        _webView.CoreWebView2.Reload();
    }

    private void OnThemeRefreshRequested(object? sender, EventArgs eventArgs)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        BeginInvoke(() => RefreshThemeCatalog(showDiagnostics: false));
    }

    private void RefreshThemeCatalog(bool showDiagnostics)
    {
        var refreshedCatalog = ThemeCatalog.Scan(_builtInTheme, _paths.Themes);
        var currentStillExists = refreshedCatalog.Themes.Any(theme =>
            string.Equals(theme.Id, _currentTheme.Id, StringComparison.Ordinal));
        _themeCatalog = refreshedCatalog;
        if (!currentStillExists)
        {
            _ = SwitchThemeAsync(_builtInTheme.Id);
        }
        else
        {
            RebuildThemeMenu();
        }

        if (showDiagnostics && refreshedCatalog.Diagnostics.Count > 0)
        {
            var message = string.Join(
                Environment.NewLine,
                refreshedCatalog.Diagnostics.Take(4).Select(diagnostic => diagnostic.Message));
            _trayIcon?.ShowBalloonTip(
                6000,
                "部分主题未能读取",
                message,
                ToolTipIcon.Warning);
        }
    }

    private void OpenThemesDirectory()
    {
        Directory.CreateDirectory(_paths.Themes);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = _paths.Themes,
            UseShellExecute = true
        });
    }

    private void ExportWindowDiagnostics()
    {
        var path = _foregroundPlayback?.ExportDiagnostics();
        if (path is null)
        {
            _trayIcon?.ShowBalloonTip(
                4000,
                "XY桌面播放器",
                "窗口检测诊断当前不可用。",
                ToolTipIcon.Warning);
            return;
        }

        _trayIcon?.ShowBalloonTip(
            6000,
            "XY桌面播放器",
            $"窗口检测诊断已导出：\n{path}",
            ToolTipIcon.Info);
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch
        {
            // Opening the file is best-effort; the path is already shown in the balloon.
        }
    }

    private void TryAttachToDesktop(bool showError)
    {
        if (IsDisposed)
        {
            return;
        }

        _desktopInteraction?.Stop();
        ConfigureWallpaperSurface();
        var screen = Screen.PrimaryScreen ?? Screen.AllScreens.FirstOrDefault();
        if (screen is null)
        {
            SwitchToWindowMode();
            if (showError)
            {
                MessageBox.Show(this, "未检测到可用显示器。", "XY桌面播放器", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            return;
        }

        if (_desktopHost.TryAttach(Handle, screen.Bounds, out var error))
        {
            _currentMode = HostMode.Wallpaper;
            Text = "XY桌面播放器 - 桌面模式";
            if (_trayIcon is not null)
            {
                _trayIcon.Text = "XY桌面播放器 - 桌面模式";
            }
            UpdateHostModeMenu();
            UpdateDesktopInteraction(showError);
            return;
        }

        SwitchToWindowMode();
        if (showError)
        {
            MessageBox.Show(
                this,
                $"桌面嵌入失败，已回退到普通窗口。\n\n{error}",
                "XY桌面播放器",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        else
        {
            _trayIcon?.ShowBalloonTip(
                5000,
                "XY桌面播放器",
                $"Explorer 重启后重新嵌入失败：{error}",
                ToolTipIcon.Warning);
        }
    }

    private void ConfigureWallpaperSurface()
    {
        WindowState = FormWindowState.Normal;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = false;
        var screen = Screen.PrimaryScreen ?? Screen.AllScreens.FirstOrDefault();
        if (screen is not null)
        {
            Bounds = screen.Bounds;
        }
    }

    private void SwitchToWindowMode()
    {
        _desktopInteraction?.Stop();
        _desktopHost.Detach();
        _currentMode = HostMode.Window;
        FormBorderStyle = FormBorderStyle.Sizable;
        ShowInTaskbar = true;
        WindowState = FormWindowState.Normal;

        var screen = Screen.PrimaryScreen ?? Screen.AllScreens.FirstOrDefault();
        if (screen is not null)
        {
            var width = Math.Min(1280, screen.WorkingArea.Width);
            var height = Math.Min(720, screen.WorkingArea.Height);
            Left = screen.WorkingArea.Left + (screen.WorkingArea.Width - width) / 2;
            Top = screen.WorkingArea.Top + (screen.WorkingArea.Height - height) / 2;
            ClientSize = new Size(width, height);
        }

        Text = "XY桌面播放器 - 独立窗口";
        if (_trayIcon is not null)
        {
            _trayIcon.Text = "XY桌面播放器 - 普通窗口";
        }
        Show();
        Activate();
        UpdateHostModeMenu();
        UpdateDesktopInteractionMenu();
    }

    private void UpdateHostModeMenu()
    {
        if (_windowModeItem is null || _wallpaperModeItem is null)
        {
            return;
        }

        var state = HostModeMenuState.From(_currentMode);
        _windowModeItem.Checked = state.WindowModeChecked;
        _wallpaperModeItem.Checked = state.WallpaperModeChecked;
    }

    private void ToggleDesktopInteraction()
    {
        _desktopInteractionEnabled = !_desktopInteractionEnabled;
        _interactionFailureReported = false;
        UpdateDesktopInteraction(showError: _desktopInteractionEnabled);
    }

    private void UpdateDesktopInteraction(bool showError)
    {
        var state = new DesktopInteractionState(
            _currentMode,
            _desktopInteractionEnabled,
            _webViewReady,
            _desktopHost.IsAttached);
        if (!DesktopInteractionStatePolicy.ShouldRun(state))
        {
            _desktopInteraction?.Stop();
            UpdateDesktopInteractionMenu();
            return;
        }

        _desktopInteraction ??= new DesktopInteractionController(
            Handle,
            _webView,
            _desktopHost);
        if (_desktopInteraction.Start())
        {
            _interactionFailureReported = false;
            UpdateDesktopInteractionMenu();
            return;
        }

        UpdateDesktopInteractionMenu();
        if (!showError || _interactionFailureReported)
        {
            return;
        }

        _interactionFailureReported = true;
        _trayIcon?.ShowBalloonTip(
            5000,
            "XY桌面播放器",
            $"桌面交互启动失败，鼠标已保留给 Windows。{Environment.NewLine}{_desktopInteraction.LastError}",
            ToolTipIcon.Warning);
    }

    private void UpdateDesktopInteractionMenu()
    {
        if (_desktopInteractionItem is null)
        {
            return;
        }

        var running = _desktopInteraction?.IsRunning == true;
        _desktopInteractionItem.Checked = running;
        _desktopInteractionItem.Text = _desktopInteractionEnabled
            ? running
                ? "桌面交互：已开启"
                : "桌面交互：等待桌面模式"
            : "桌面交互：已关闭";
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int RegisterWindowMessage(string message);
    }
}
