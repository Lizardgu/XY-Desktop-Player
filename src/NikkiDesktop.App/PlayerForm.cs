using System.Text.Json;
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
    private readonly WebView2 _webView;
    private readonly Label _statusLabel;
    private bool _initialized;
    private bool _captureCompleted;

    public PlayerForm(
        WebContentMapping mapping,
        string userDataRoot,
        HostMode requestedMode,
        string? capturePath)
    {
        _mapping = mapping;
        _userDataRoot = userDataRoot;
        _requestedMode = requestedMode;
        _capturePath = capturePath is null ? null : Path.GetFullPath(capturePath);

        Text = requestedMode == HostMode.Wallpaper
            ? "Nikki Desktop - 桌面模式准备中"
            : "Nikki Desktop - 独立窗口";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1280, 720);
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
            Text = "正在启动独立播放器…",
            Anchor = AnchorStyles.None
        };

        Controls.Add(_webView);
        Controls.Add(_statusLabel);
        Resize += (_, _) => CenterStatusLabel();
        CenterStatusLabel();
    }

    public int ExitCode { get; private set; }

    protected override async void OnShown(EventArgs eventArgs)
    {
        base.OnShown(eventArgs);
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        try
        {
            Directory.CreateDirectory(_userDataRoot);
            var environment = await CoreWebView2Environment.CreateAsync(
                userDataFolder: _userDataRoot);
            await _webView.EnsureCoreWebView2Async(environment);

            _webView.CoreWebView2.Settings.AreDevToolsEnabled = true;
            _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                _mapping.VirtualHostName,
                _mapping.ResolvedContentRoot,
                CoreWebView2HostResourceAccessKind.Allow);
            await _webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                WallpaperEngineShim.Script);

            _webView.NavigationCompleted += OnNavigationCompleted;
            _webView.Source = _mapping.StartUri;
        }
        catch (Exception exception)
        {
            _statusLabel.Text = "启动失败";
            CenterStatusLabel();
            MessageBox.Show(
                this,
                exception.Message,
                "Nikki Desktop - WebView2 启动失败",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private async void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs eventArgs)
    {
        if (!eventArgs.IsSuccess)
        {
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
        Text = _requestedMode == HostMode.Wallpaper
            ? "Nikki Desktop - 桌面模式"
            : "Nikki Desktop - 独立窗口";

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

            using var document = JsonDocument.Parse(domResult);
            var root = document.RootElement;
            var ready = root.GetProperty("readyState").GetString() == "complete";
            var hasApp = root.GetProperty("rootChildCount").GetInt32() > 0;
            var hasLoadedImage = root.GetProperty("loadedImageCount").GetInt32() > 0;
            var shimInstalled = root.GetProperty("shimInstalled").GetBoolean();
            var hasTrackedAudio = root.GetProperty("trackedAudioCount").GetInt32() > 0;
            var audioAdvanced = root.GetProperty("maxAudioTime").GetDouble() > 0.25;
            var captureWritten = new FileInfo(capturePath).Length > 0;

            ExitCode = ready && hasApp && hasLoadedImage && shimInstalled &&
                       hasTrackedAudio && audioAdvanced && captureWritten
                ? 0
                : 6;
        }
        catch (Exception exception)
        {
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
}
