namespace XYDesktopPlayer.App;

internal sealed record ApplicationPaths(
    string Root,
    string Themes,
    string Settings,
    string Logs,
    string WebView2)
{
    public static ApplicationPaths Create(string? localApplicationData = null)
    {
        var localRoot = localApplicationData ?? Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        var root = Path.Combine(localRoot, "XYDesktopPlayer");
        return new ApplicationPaths(
            root,
            Path.Combine(root, "Themes"),
            Path.Combine(root, "settings.json"),
            Path.Combine(root, "Logs"),
            Path.Combine(root, "WebView2"));
    }
}
