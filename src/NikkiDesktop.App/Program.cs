using NikkiDesktop.Core;

namespace NikkiDesktop.App;

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
                "Nikki Desktop",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 2;
        }

        var options = parseResult.Options;
        var contentRoot = options.ContentRoot ?? Path.Combine(
            Environment.CurrentDirectory,
            "content",
            "reference-player");

        if (options.SelfTest)
        {
            return SelfTestRunner.Run(options, contentRoot);
        }

        WebContentMapping mapping;
        try
        {
            mapping = WebContentMapping.Create(contentRoot);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(
                exception.Message,
                "Nikki Desktop - 内容不可用",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 3;
        }

        ApplicationConfiguration.Initialize();
        var userDataFolderName = options.CapturePath is null ? "webview2" : "webview2-capture";
        var userDataRoot = Path.Combine(Environment.CurrentDirectory, "data", userDataFolderName);
        using var form = new PlayerForm(
            mapping,
            userDataRoot,
            options.Mode,
            options.CapturePath);
        Application.Run(form);
        return form.ExitCode;
    }
}
