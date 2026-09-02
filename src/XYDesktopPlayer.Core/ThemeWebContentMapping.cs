namespace XYDesktopPlayer.Core;

public sealed record ThemeWebContentMapping(
    string ResolvedAssetRoot,
    string VirtualHostName)
{
    public static ThemeWebContentMapping Create(ThemePack theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        var resolvedAssetRoot = Path.GetFullPath(theme.AssetRoot);
        if (!Directory.Exists(resolvedAssetRoot))
        {
            throw new DirectoryNotFoundException($"主题素材目录不存在：{resolvedAssetRoot}");
        }

        return new ThemeWebContentMapping(
            resolvedAssetRoot,
            "theme.xydesktop.local");
    }
}
