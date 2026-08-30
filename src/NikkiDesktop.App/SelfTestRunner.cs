using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using NikkiDesktop.Core;

namespace NikkiDesktop.App;

internal static class SelfTestRunner
{
    public static int Run(LaunchOptions options, string contentRoot)
    {
        var checks = new List<SelfTestCheck>();
        var validation = ContentRootValidator.Validate(contentRoot);
        checks.Add(new SelfTestCheck(
            "content",
            validation.IsValid,
            validation.IsValid
                ? $"内容目录有效：{validation.ResolvedContentRoot}"
                : validation.Error ?? "内容目录无效。"));

        string? webView2Version = null;
        try
        {
            webView2Version = CoreWebView2Environment.GetAvailableBrowserVersionString();
            checks.Add(new SelfTestCheck(
                "webview2-runtime",
                !string.IsNullOrWhiteSpace(webView2Version),
                $"WebView2 Runtime：{webView2Version}"));
        }
        catch (Exception exception)
        {
            checks.Add(new SelfTestCheck(
                "webview2-runtime",
                false,
                $"未检测到 WebView2 Runtime：{exception.Message}"));
        }

        var success = checks.All(check => check.Passed);
        var report = new SelfTestReport(
            DateTimeOffset.UtcNow,
            success,
            options.Mode.ToString().ToLowerInvariant(),
            validation.ResolvedContentRoot ?? Path.GetFullPath(contentRoot),
            webView2Version,
            checks);

        var logDirectory = Path.Combine(Environment.CurrentDirectory, "logs");
        Directory.CreateDirectory(logDirectory);
        var reportPath = Path.Combine(logDirectory, "self-test.json");
        File.WriteAllText(
            reportPath,
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));

        foreach (var check in checks)
        {
            Console.WriteLine($"{(check.Passed ? "PASS" : "FAIL")} {check.Name}: {check.Message}");
        }
        Console.WriteLine($"Self-test report: {reportPath}");
        return success ? 0 : 1;
    }

    private sealed record SelfTestReport(
        DateTimeOffset CreatedUtc,
        bool Success,
        string Mode,
        string ContentRoot,
        string? WebView2Version,
        IReadOnlyList<SelfTestCheck> Checks);

    private sealed record SelfTestCheck(string Name, bool Passed, string Message);
}

