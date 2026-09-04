using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace XYDesktopPlayer.App;

internal sealed record StartupPlaybackResult(
    bool Started,
    string? Source,
    string? Error);

internal static class StartupPlaybackService
{
    /// <summary>
    /// Waits for the theme's audio to become prepared, then starts playback.
    /// </summary>
    /// <param name="webView">The player WebView2 instance.</param>
    /// <param name="isBlocked">
    /// Optional host-side check for whether playback should be blocked right now. When this
    /// returns true at the moment audio is ready, playback is deliberately NOT started and a
    /// <c>deferred-blocked</c> result is returned so the caller can retry later. This closes
    /// the race where the old code waited up to three seconds for audio and then started
    /// playing regardless of whatever window the user had opened in the meantime.
    /// </param>
    public static async Task<StartupPlaybackResult> TryStartAsync(
        CoreWebView2 webView,
        Func<bool>? isBlocked = null)
    {
        ArgumentNullException.ThrowIfNull(webView);

        try
        {
            var ready = await WaitForAudioReadyAsync(webView);
            if (!ready)
            {
                return new StartupPlaybackResult(
                    Started: false,
                    Source: null,
                    Error: "theme audio is not ready");
            }

            if (isBlocked is not null && isBlocked())
            {
                return new StartupPlaybackResult(
                    Started: false,
                    Source: "deferred-blocked",
                    Error: null);
            }

            var started = await StartPlaybackAsync(webView);
            return started
                ? new StartupPlaybackResult(Started: true, Source: "theme-ready", Error: null)
                : new StartupPlaybackResult(
                    Started: false,
                    Source: null,
                    Error: "start playback returned false");
        }
        catch (Exception exception)
        {
            return new StartupPlaybackResult(
                Started: false,
                Source: null,
                Error: exception.Message);
        }
    }

    private static async Task<bool> WaitForAudioReadyAsync(CoreWebView2 webView)
    {
        var request = JsonSerializer.Serialize(new
        {
            expression = """
                (async () => {
                  const deadline = Date.now() + 3000;
                  while (Date.now() < deadline) {
                    const state = window.__xyDesktopGetPlayerState?.();
                    if (typeof window.__xyDesktopStartPlayback === 'function' && state?.audioPrepared) {
                      return true;
                    }
                    await new Promise(resolve => setTimeout(resolve, 50));
                  }
                  return false;
                })()
                """,
            awaitPromise = true,
            returnByValue = true
        });
        var rawResult = await webView.CallDevToolsProtocolMethodAsync(
            "Runtime.evaluate",
            request);
        return ParseBooleanResult(rawResult);
    }

    private static async Task<bool> StartPlaybackAsync(CoreWebView2 webView)
    {
        var request = JsonSerializer.Serialize(new
        {
            expression = """
                (async () => await window.__xyDesktopStartPlayback())()
                """,
            awaitPromise = true,
            userGesture = true,
            returnByValue = true
        });
        var rawResult = await webView.CallDevToolsProtocolMethodAsync(
            "Runtime.evaluate",
            request);
        return ParseBooleanResult(rawResult);
    }

    private static bool ParseBooleanResult(string rawResult)
    {
        try
        {
            using var document = JsonDocument.Parse(rawResult);
            var root = document.RootElement;
            if (!root.TryGetProperty("result", out var runtimeResult) ||
                !runtimeResult.TryGetProperty("value", out var value))
            {
                return false;
            }

            if (value.ValueKind == JsonValueKind.True)
            {
                return true;
            }

            if (value.ValueKind == JsonValueKind.False)
            {
                return false;
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString();
                if (text is "true" or "True")
                {
                    return true;
                }

                // Older page shims returned the object as a JSON string.
                if (!string.IsNullOrEmpty(text) &&
                    text.StartsWith("{", StringComparison.Ordinal))
                {
                    using var inner = JsonDocument.Parse(text);
                    return inner.RootElement.TryGetProperty("started", out var started) &&
                           started.ValueKind == JsonValueKind.True;
                }

                return false;
            }

            // Runtime.evaluate with returnByValue returns objects as structured JSON:
            // { started: boolean, source: string, error: string }.
            if (value.ValueKind == JsonValueKind.Object)
            {
                return value.TryGetProperty("started", out var started) &&
                       started.ValueKind == JsonValueKind.True;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<bool> GetManualPausedAsync(CoreWebView2 webView)
    {
        ArgumentNullException.ThrowIfNull(webView);
        try
        {
            var result = await webView.ExecuteScriptAsync(
                "Boolean(window.__xyDesktopGetPlayerState?.().manualPaused)");
            return JsonSerializer.Deserialize<bool>(result);
        }
        catch
        {
            return false;
        }
    }

    public static async Task ApplyManualPausedAsync(CoreWebView2 webView, bool paused)
    {
        ArgumentNullException.ThrowIfNull(webView);
        var value = paused ? "true" : "false";
        await webView.ExecuteScriptAsync(
            $"window.__xyDesktopApplyManualPaused?.({value})");
    }

    public static async Task ReleaseAsync(CoreWebView2 webView)
    {
        ArgumentNullException.ThrowIfNull(webView);
        try
        {
            await webView.ExecuteScriptAsync("window.__xyDesktopReleasePlayer?.()");
        }
        catch
        {
            // Navigation and shutdown may invalidate the page before release completes.
        }
    }
}
