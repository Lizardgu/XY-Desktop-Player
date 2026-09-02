namespace XYDesktopPlayer.App;

internal sealed record ApplicationContentLayout(
    string PlayerRoot,
    string BuiltInDefinitionRoot,
    string BuiltInAssetRoot,
    string LegacyContentRoot)
{
    public static ApplicationContentLayout Resolve(
        string? contentRoot,
        string currentDirectory,
        string applicationBaseDirectory)
    {
        var requestedRoot = Path.GetFullPath(contentRoot ?? Path.Combine(
            currentDirectory,
            "content",
            "reference-player"));

        if (File.Exists(Path.Combine(requestedRoot, "player", "index.html")))
        {
            var builtInRoot = Path.Combine(requestedRoot, "themes", "孤独摇滚");
            return new ApplicationContentLayout(
                Path.Combine(requestedRoot, "player"),
                builtInRoot,
                builtInRoot,
                requestedRoot);
        }

        var repositoryRoot = FindRepositoryRoot(currentDirectory) ??
                             FindRepositoryRoot(applicationBaseDirectory) ??
                             throw new DirectoryNotFoundException(
                                 "找不到共用播放器 web/player 与内置主题 themes/bocchi。");
        var assetRoot = Directory.Exists(Path.Combine(requestedRoot, "assets"))
            ? Path.Combine(requestedRoot, "assets")
            : requestedRoot;
        return new ApplicationContentLayout(
            Path.Combine(repositoryRoot, "web", "player"),
            Path.Combine(repositoryRoot, "themes", "bocchi"),
            assetRoot,
            requestedRoot);
    }

    private static string? FindRepositoryRoot(string startPath)
    {
        var current = new DirectoryInfo(Path.GetFullPath(startPath));
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "web", "player", "index.html")) &&
                File.Exists(Path.Combine(current.FullName, "themes", "bocchi", "pack.json")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }
}
