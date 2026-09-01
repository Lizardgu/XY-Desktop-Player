using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace XYDesktopPlayer.App;

internal sealed record StartupPlaybackResult(
    bool Started,
    string? Source,
    string? Error);

internal static class StartupPlaybackService
{
    public static async Task<StartupPlaybackResult> TryStartAsync(CoreWebView2 webView)
    {
        ArgumentNullException.ThrowIfNull(webView);

        try
        {
            var request = JsonSerializer.Serialize(new
            {
                expression = """
                    (async () => {
                      const deadline = Date.now() + 3000;
                      let playable = null;
                      while (Date.now() < deadline) {
                        const tracked = window.__xyDesktopTrackedAudio ?? [];
                        const candidates = tracked.filter(audio =>
                          audio?.src && !audio.src.includes('/keypress.mp3'));
                        playable = candidates.find(audio => !audio.paused && !audio.ended) ??
                          candidates.find(audio => !audio.ended) ??
                          candidates[0] ?? null;
                        if (playable) break;
                        await new Promise(resolve => setTimeout(resolve, 50));
                      }
                      if (!playable) {
                        return { started: false, source: null, error: 'no playable audio' };
                      }
                      try {
                        window.__xyDesktopResumeAudioContext?.();
                        await playable.play();
                        return {
                          started: !playable.paused,
                          source: playable.src,
                          error: playable.paused ? 'play returned while audio remained paused' : null
                        };
                      } catch (error) {
                        return {
                          started: false,
                          source: playable.src,
                          error: String(error?.message ?? error)
                        };
                      }
                    })()
                    """,
                awaitPromise = true,
                userGesture = true,
                returnByValue = true
            });
            var rawResult = await webView.CallDevToolsProtocolMethodAsync(
                "Runtime.evaluate",
                request);
            using var document = JsonDocument.Parse(rawResult);
            var root = document.RootElement;
            if (!root.TryGetProperty("result", out var runtimeResult) ||
                !runtimeResult.TryGetProperty("value", out var value))
            {
                return new StartupPlaybackResult(
                    Started: false,
                    Source: null,
                    Error: "WebView 未返回启动播放结果。");
            }

            var started = value.TryGetProperty("started", out var startedProperty) &&
                          startedProperty.GetBoolean();
            var source = value.TryGetProperty("source", out var sourceProperty) &&
                         sourceProperty.ValueKind == JsonValueKind.String
                ? sourceProperty.GetString()
                : null;
            var error = value.TryGetProperty("error", out var errorProperty) &&
                        errorProperty.ValueKind == JsonValueKind.String
                ? errorProperty.GetString()
                : null;
            return new StartupPlaybackResult(started, source, error);
        }
        catch (Exception exception)
        {
            return new StartupPlaybackResult(
                Started: false,
                Source: null,
                Error: exception.Message);
        }
    }
}
