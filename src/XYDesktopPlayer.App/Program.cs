using XYDesktopPlayer.Core;

namespace XYDesktopPlayer.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] arguments)
    {
        var parseResult = LaunchOptionsParser.Parse(arguments);
        if (!parseResult.IsSuccess || parseResult.Options is null)
        {
            MessageBox.Show(
                parseResult.Error ?? "启动参数无效。",
                "XY桌面播放器",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 2;
        }

        var options = parseResult.Options;
        using var instanceGuard = SingleInstanceGuard.Acquire(
            isolatedCapture: options.CapturePath is not null);
        if (!instanceGuard.IsPrimaryInstance)
        {
            MessageBox.Show(
                "XY桌面播放器已经在运行，请使用右下角托盘图标操作。",
                "XY桌面播放器",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return 4;
        }

        var paths = ApplicationPaths.Create();
        Directory.CreateDirectory(paths.Root);
        Directory.CreateDirectory(paths.Themes);
        ApplicationLog.Initialize(paths.Logs);
        var legacyContentRoot = options.ContentRoot ?? Path.Combine(
            Environment.CurrentDirectory,
            "content",
            "reference-player");

        if (options.SelfTest)
        {
            return SelfTestRunner.Run(options, legacyContentRoot);
        }

        WebContentMapping mapping;
        ThemePack builtInTheme;
        ApplicationContentLayout layout;
        try
        {
            layout = ApplicationContentLayout.Resolve(
                options.ContentRoot,
                Environment.CurrentDirectory,
                AppContext.BaseDirectory);
            mapping = WebContentMapping.Create(layout.PlayerRoot);
            var builtInResult = ThemePackLoader.Load(
                layout.BuiltInDefinitionRoot,
                layout.BuiltInAssetRoot,
                isBuiltIn: true);
            if (!builtInResult.IsValid || builtInResult.Theme is null)
            {
                throw new InvalidDataException(
                    builtInResult.Error ?? "内置“孤独摇滚”主题无效。");
            }

            builtInTheme = builtInResult.Theme;
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            ApplicationLog.Write("播放器内容或主题无法加载", exception);
            MessageBox.Show(
                exception.Message,
                "XY桌面播放器 - 内容不可用",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 3;
        }

        ApplicationConfiguration.Initialize();
        Application.ThreadException += (_, eventArgs) =>
            ApplicationLog.Write("UI 线程发生未处理异常", eventArgs.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
            ApplicationLog.Write(
                "后台线程发生未处理异常",
                eventArgs.ExceptionObject as Exception);
        var userDataRoot = options.CapturePath is null
            ? paths.WebView2
            : Path.Combine(Environment.CurrentDirectory, "data", "webview2-capture");
        using var form = new PlayerForm(
            mapping,
            builtInTheme,
            paths,
            userDataRoot,
            options.Mode,
            options.CapturePath);
        Application.Run(form);
        ApplicationLog.Write($"应用退出，代码 {form.ExitCode}");
        return form.ExitCode;
    }
}
