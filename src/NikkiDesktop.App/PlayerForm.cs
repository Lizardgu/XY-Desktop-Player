using System.Text.Json;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using NikkiDesktop.Core;

namespace NikkiDesktop.App;

internal sealed class PlayerForm : Form
{
    private readonly WebContentMapping _mapping;
    private readonly string _userDataRoot;
    private readonly HostMode _requestedMode;
    private readonly string? _capturePath;
    private readonly DesktopHostService _desktopHost = new();
    private readonly int _taskbarCreatedMessage;
    private readonly WebView2 _webView;
    private readonly Label _statusLabel;
    private NotifyIcon? _trayIcon;
    private ContextMenuStrip? _trayMenu;
    private ToolStripMenuItem? _desktopInteractionItem;
    private DesktopInteractionController? _desktopInteraction;
    private HostMode _currentMode;
    private bool _initialized;
    private bool _captureCompleted;
    private bool _webViewReady;
    private bool _desktopInteractionEnabled = true;
    private bool _interactionFailureReported;

    public PlayerForm(
        WebContentMapping mapping,
        string userDataRoot,
        HostMode requestedMode,
        string? capturePath)
    {
        _mapping = mapping;
        _userDataRoot = userDataRoot;
        _requestedMode = requestedMode;
        _currentMode = requestedMode;
        _capturePath = capturePath is null ? null : Path.GetFullPath(capturePath);
        TraceCapture("form-created");
        _taskbarCreatedMessage = NativeMethods.RegisterWindowMessage("TaskbarCreated");

        Text = requestedMode == HostMode.Wallpaper
            ? "孤独摇滚壁纸移植 - 桌面模式准备中"
            : "孤独摇滚壁纸移植 - 独立窗口";
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
            if (_capturePath is null)
            {
                CreateTrayIcon();
            }
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
            TraceCapture("virtual-host-mapped");
            TraceCapture("adding-document-script");
            await _webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                WallpaperEngineShim.Script);
            TraceCapture("document-script-added");

            _webView.CoreWebView2.NavigationStarting += (_, _) =>
            {
                TraceCapture("navigation-starting");
                _webViewReady = false;
                _desktopInteraction?.Stop();
                UpdateDesktopInteractionMenu();
            };
            _webView.NavigationCompleted += OnNavigationCompleted;
            _webView.Source = _mapping.StartUri;
            TraceCapture("navigation-requested");
        }
        catch (Exception exception)
        {
            TraceCapture($"startup-error: {exception}");
            _statusLabel.Text = "启动失败";
            CenterStatusLabel();
            MessageBox.Show(
                this,
                exception.Message,
                "孤独摇滚壁纸移植 - WebView2 启动失败",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private async void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs eventArgs)
    {
        TraceCapture($"navigation-completed: success={eventArgs.IsSuccess}; status={eventArgs.WebErrorStatus}");
        if (!eventArgs.IsSuccess)
        {
            _webViewReady = false;
            _desktopInteraction?.Stop();
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
            ? "孤独摇滚壁纸移植 - 桌面模式"
            : "孤独摇滚壁纸移植 - 独立窗口";
        UpdateDesktopInteraction(showError: false);

        if (_capturePath is not null && !_captureCompleted)
        {
            _captureCompleted = true;
            await CaptureLoadedPlayerAsync(_capturePath);
        }
    }

    private async Task CaptureLoadedPlayerAsync(string capturePath)
    {
        try
        {
            TraceCapture("capture-started");
            var playRequest = JsonSerializer.Serialize(new
            {
                expression = """
                    (async () => {
                      const deadline = Date.now() + 3000;
                      let playable = null;
                      while (Date.now() < deadline) {
                        const tracked = window.__nikkiDesktopTrackedAudio ?? [];
                        playable = tracked.find(audio => audio.src && !audio.src.endsWith('/keypress.mp3')) ?? tracked[0];
                        if (playable) break;
                        await new Promise(resolve => setTimeout(resolve, 50));
                      }
                      if (!playable) return { played: false, reason: 'no tracked audio' };
                      window.__nikkiDesktopResumeAudioContext?.();
                      await playable.play();
                      return { played: true, src: playable.src };
                    })()
                    """,
                awaitPromise = true,
                userGesture = true,
                returnByValue = true
            });
            await _webView.CoreWebView2.CallDevToolsProtocolMethodAsync(
                "Runtime.evaluate",
                playRequest);
            TraceCapture("capture-play-request-completed");
            await Task.Delay(2000);
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
                  shimInstalled: window.__nikkiDesktopShimInstalled === true,
                  wallpaperAudioListenerAvailable: typeof window.wallpaperRegisterAudioListener === 'function',
                  trackedAudioCount: window.__nikkiDesktopTrackedAudio?.length ?? 0,
                  playingAudioCount: window.__nikkiDesktopTrackedAudio?.filter(audio => !audio.paused).length ?? 0,
                  maxAudioTime: Math.max(0, ...(window.__nikkiDesktopTrackedAudio?.map(audio => audio.currentTime) ?? []))
                }))()
                """);
            var domPath = Path.ChangeExtension(capturePath, ".json");
            File.WriteAllText(domPath, domResult);

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
                        desktopInteractionError = _desktopInteraction?.LastError
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

            ExitCode = ready && hasApp && hasLoadedImage && shimInstalled &&
                       hasTrackedAudio && audioAdvanced && captureWritten && desktopModeVerified
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

    protected override void OnFormClosed(FormClosedEventArgs eventArgs)
    {
        _desktopInteraction?.Dispose();
        _trayIcon?.Dispose();
        _trayMenu?.Dispose();
        _desktopHost.Dispose();
        base.OnFormClosed(eventArgs);
    }

    private void CreateTrayIcon()
    {
        _trayMenu = new ContextMenuStrip();
        _trayMenu.Items.Add("切换到普通窗口", null, (_, _) => SwitchToWindowMode());
        _trayMenu.Items.Add("嵌入桌面图标后方", null, (_, _) => TryAttachToDesktop(showError: true));
        _desktopInteractionItem = new ToolStripMenuItem("桌面交互：已开启")
        {
            Checked = true
        };
        _desktopInteractionItem.Click += (_, _) => ToggleDesktopInteraction();
        _trayMenu.Items.Add(_desktopInteractionItem);
        _trayMenu.Items.Add("重新加载播放器", null, (_, _) => _webView.CoreWebView2?.Reload());
        _trayMenu.Items.Add(new ToolStripSeparator());
        _trayMenu.Items.Add("退出孤独摇滚壁纸移植", null, (_, _) => Close());

        _trayIcon = new NotifyIcon
        {
            ContextMenuStrip = _trayMenu,
            Icon = SystemIcons.Application,
            Text = "孤独摇滚壁纸移植",
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => SwitchToWindowMode();
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
                MessageBox.Show(this, "未检测到可用显示器。", "孤独摇滚壁纸移植", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            return;
        }

        if (_desktopHost.TryAttach(Handle, screen.Bounds, out var error))
        {
            _currentMode = HostMode.Wallpaper;
            Text = "孤独摇滚壁纸移植 - 桌面模式";
            if (_trayIcon is not null)
            {
                _trayIcon.Text = "孤独摇滚壁纸移植 - 桌面模式";
            }
            UpdateDesktopInteraction(showError);
            return;
        }

        SwitchToWindowMode();
        if (showError)
        {
            MessageBox.Show(
                this,
                $"桌面嵌入失败，已回退到普通窗口。\n\n{error}",
                "孤独摇滚壁纸移植",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        else
        {
            _trayIcon?.ShowBalloonTip(
                5000,
                "孤独摇滚壁纸移植",
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

        Text = "孤独摇滚壁纸移植 - 独立窗口";
        if (_trayIcon is not null)
        {
            _trayIcon.Text = "孤独摇滚壁纸移植 - 普通窗口";
        }
        Show();
        Activate();
        UpdateDesktopInteractionMenu();
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
            "孤独摇滚壁纸移植",
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
