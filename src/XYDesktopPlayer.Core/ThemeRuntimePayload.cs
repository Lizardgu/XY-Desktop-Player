using System.Text;

namespace XYDesktopPlayer.Core;

public sealed record ThemeRuntimeLyrics(
    string? Original,
    string? Romanized,
    string? Translation);

public sealed record ThemeRuntimeSong(
    string Title,
    string? Artist,
    string Audio,
    string Cover,
    ThemeRuntimeLyrics Lyrics,
    string BackgroundColor,
    string TextColor,
    string AccentColor,
    string? BackgroundImage);

public sealed record ThemeRuntimeAppearance(
    string? Logo,
    string? Font,
    string AccentColor,
    string TextColor,
    IReadOnlyDictionary<string, string> Icons,
    IReadOnlyDictionary<string, string> Effects);

public sealed record ThemeRuntimePayload(
    int Format,
    string Id,
    string Name,
    string? Author,
    ThemeRuntimeAppearance Appearance,
    IReadOnlyList<ThemeRuntimeSong> Songs);

public static class ThemeRuntimePayloadFactory
{
    private const int MaximumLyricCharacters = 2 * 1024 * 1024;
    private const string DefaultTextColor = "#FFFFFF";
    private const string DefaultAccentColor = "#FFFFFF";

    public static ThemeRuntimePayload Create(
        ThemePack theme,
        string themeHostName = "theme.xydesktop.local")
    {
        ArgumentNullException.ThrowIfNull(theme);
        ValidateHostName(themeHostName);

        var appearanceTextColor = theme.Appearance.TextColor ?? DefaultTextColor;
        var appearanceAccentColor = theme.Appearance.AccentColor ?? DefaultAccentColor;
        var appearance = new ThemeRuntimeAppearance(
            OptionalAssetUrl(theme, theme.Appearance.Logo, ThemeAssetPath.ImageExtensions, themeHostName),
            OptionalAssetUrl(theme, theme.Appearance.Font, ThemeAssetPath.FontExtensions, themeHostName),
            appearanceAccentColor,
            appearanceTextColor,
            AssetMapUrls(theme, theme.Appearance.Icons, ThemeAssetPath.ImageExtensions, themeHostName),
            AssetMapUrls(theme, theme.Appearance.Effects, ThemeAssetPath.AudioExtensions, themeHostName));

        var songs = theme.Songs.Select(song => new ThemeRuntimeSong(
                song.Title,
                song.Artist,
                RequiredAssetUrl(theme, song.Audio, ThemeAssetPath.AudioExtensions, themeHostName),
                RequiredAssetUrl(theme, song.Cover, ThemeAssetPath.ImageExtensions, themeHostName),
                new ThemeRuntimeLyrics(
                    ReadOptionalLyric(theme, song.Lyrics.Original),
                    ReadOptionalLyric(theme, song.Lyrics.Romanized),
                    ReadOptionalLyric(theme, song.Lyrics.Translation)),
                song.BackgroundColor,
                song.TextColor ?? appearanceTextColor,
                song.AccentColor ?? appearanceAccentColor,
                OptionalAssetUrl(theme, song.BackgroundImage, ThemeAssetPath.ImageExtensions, themeHostName)))
            .ToArray();

        return new ThemeRuntimePayload(
            theme.Format,
            theme.Id,
            theme.Name,
            theme.Author,
            appearance,
            songs);
    }

    private static IReadOnlyDictionary<string, string> AssetMapUrls(
        ThemePack theme,
        IReadOnlyDictionary<string, string> assets,
        IReadOnlySet<string> extensions,
        string hostName) =>
        assets.ToDictionary(
            pair => pair.Key,
            pair => RequiredAssetUrl(theme, pair.Value, extensions, hostName),
            StringComparer.OrdinalIgnoreCase);

    private static string? OptionalAssetUrl(
        ThemePack theme,
        string? relativePath,
        IReadOnlySet<string> extensions,
        string hostName) =>
        relativePath is null
            ? null
            : RequiredAssetUrl(theme, relativePath, extensions, hostName);

    private static string RequiredAssetUrl(
        ThemePack theme,
        string relativePath,
        IReadOnlySet<string> extensions,
        string hostName)
    {
        if (!ThemeAssetPath.TryResolveExistingFile(
                theme.AssetRoot,
                relativePath,
                extensions,
                out var normalized,
                out _,
                out var error))
        {
            throw new InvalidOperationException(error);
        }

        var escapedPath = string.Join(
            '/',
            normalized.Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(Uri.EscapeDataString));
        return $"https://{hostName}/{escapedPath}";
    }

    private static string? ReadOptionalLyric(ThemePack theme, string? relativePath)
    {
        if (relativePath is null)
        {
            return null;
        }

        if (!ThemeAssetPath.TryResolveExistingFile(
                theme.AssetRoot,
                relativePath,
                ThemeAssetPath.LyricExtensions,
                out _,
                out var resolved,
                out var error))
        {
            throw new InvalidOperationException(error);
        }

        var file = new FileInfo(resolved);
        if (file.Length > MaximumLyricCharacters * 4L)
        {
            throw new InvalidOperationException($"歌词文件过大：{relativePath}");
        }

        var text = File.ReadAllText(resolved, Encoding.UTF8);
        if (text.Length > MaximumLyricCharacters)
        {
            throw new InvalidOperationException($"歌词内容过大：{relativePath}");
        }

        return text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
    }

    private static void ValidateHostName(string hostName)
    {
        if (string.IsNullOrWhiteSpace(hostName) ||
            Uri.CheckHostName(hostName) == UriHostNameType.Unknown ||
            hostName.Contains('/', StringComparison.Ordinal) ||
            hostName.Contains('\\', StringComparison.Ordinal))
        {
            throw new ArgumentException("主题虚拟主机名无效。", nameof(hostName));
        }
    }
}
