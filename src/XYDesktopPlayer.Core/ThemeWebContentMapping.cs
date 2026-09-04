namespace XYDesktopPlayer.Core;

using System.Security.Cryptography;
using System.Text;

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
            $"{CreateHostLabel(theme.Id)}.xydesktop.local");
    }

    private static string CreateHostLabel(string themeId)
    {
        if (themeId.Length <= 57 &&
            themeId[^1] != '-' &&
            themeId.All(character =>
                character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-'))
        {
            return $"theme-{themeId}";
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(themeId)))
            .ToLowerInvariant();
        return $"theme-{hash[..20]}";
    }
}
