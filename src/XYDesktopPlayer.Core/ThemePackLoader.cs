using System.Text.Json;
using System.Text.RegularExpressions;

namespace XYDesktopPlayer.Core;

public static partial class ThemePackLoader
{
    private const long MaximumDefinitionBytes = 4 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public static ThemePackLoadResult Load(
        string definitionRoot,
        string assetRoot,
        bool isBuiltIn)
    {
        var warnings = new List<string>();
        try
        {
            var resolvedDefinitionRoot = Path.GetFullPath(definitionRoot);
            var resolvedAssetRoot = Path.GetFullPath(assetRoot);
            if (!Directory.Exists(resolvedDefinitionRoot))
            {
                return Invalid($"主题定义目录不存在：{resolvedDefinitionRoot}", warnings);
            }

            if (!Directory.Exists(resolvedAssetRoot))
            {
                return Invalid($"主题素材目录不存在：{resolvedAssetRoot}", warnings);
            }

            var packPath = Path.Combine(resolvedDefinitionRoot, "pack.json");
            var songsPath = Path.Combine(resolvedDefinitionRoot, "songs.json");
            if (!File.Exists(packPath))
            {
                return Invalid("主题缺少 pack.json", warnings);
            }

            if (!File.Exists(songsPath))
            {
                return Invalid("主题缺少 songs.json", warnings);
            }

            if (new FileInfo(packPath).Length > MaximumDefinitionBytes ||
                new FileInfo(songsPath).Length > MaximumDefinitionBytes)
            {
                return Invalid("主题配置文件过大", warnings);
            }

            var definition = JsonSerializer.Deserialize<ThemePackDefinition>(
                File.ReadAllText(packPath),
                JsonOptions);
            var songDefinitions = JsonSerializer.Deserialize<List<ThemeSongDefinition?>>(
                File.ReadAllText(songsPath),
                JsonOptions);
            if (definition is null)
            {
                return Invalid("pack.json 内容为空", warnings);
            }

            if (definition.Format != 1)
            {
                return Invalid($"不支持的主题格式：{definition.Format}", warnings);
            }

            var id = definition.Id?.Trim();
            if (string.IsNullOrEmpty(id) || !ThemeIdPattern().IsMatch(id))
            {
                return Invalid("主题 id 只能使用小写字母、数字、点、横线和下划线", warnings);
            }

            var name = definition.Name?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                return Invalid("主题名称不能为空", warnings);
            }

            var appearanceResult = LoadAppearance(definition.Appearance, resolvedAssetRoot);
            if (!appearanceResult.IsValid)
            {
                return Invalid(appearanceResult.Error!, warnings);
            }

            var songs = new List<ThemeSong>();
            foreach (var songDefinition in songDefinitions ?? [])
            {
                if (songDefinition is null)
                {
                    warnings.Add("歌曲“未命名”已跳过：歌曲配置不能为空");
                    continue;
                }

                var songResult = LoadSong(songDefinition, resolvedAssetRoot);
                if (songResult.Song is null)
                {
                    warnings.Add($"歌曲“{SongLabel(songDefinition)}”已跳过：{songResult.Error}");
                    continue;
                }

                songs.Add(songResult.Song);
            }

            if (songs.Count == 0)
            {
                return Invalid("主题没有可播放歌曲。", warnings);
            }

            return new ThemePackLoadResult(
                true,
                new ThemePack(
                    definition.Format,
                    id,
                    name,
                    NullIfWhiteSpace(definition.Author),
                    appearanceResult.Appearance!,
                    songs,
                    resolvedDefinitionRoot,
                    resolvedAssetRoot,
                    isBuiltIn),
                warnings,
                null);
        }
        catch (Exception exception) when (
            exception is JsonException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            return Invalid($"主题配置读取失败：{exception.Message}", warnings);
        }
    }

    private static (bool IsValid, ThemeAppearance? Appearance, string? Error) LoadAppearance(
        ThemeAppearanceDefinition? definition,
        string assetRoot)
    {
        definition ??= new ThemeAppearanceDefinition();
        if (!TryOptionalColor(definition.AccentColor, out var accentColor) ||
            !TryOptionalColor(definition.TextColor, out var textColor))
        {
            return (false, null, "主题颜色必须使用 #RRGGBB 或 #RRGGBBAA");
        }

        if (!TryOptionalAsset(
                assetRoot,
                definition.Logo,
                ThemeAssetPath.ImageExtensions,
                out var logo,
                out var logoError))
        {
            return (false, null, logoError);
        }

        if (!TryOptionalAsset(
                assetRoot,
                definition.Font,
                ThemeAssetPath.FontExtensions,
                out var font,
                out var fontError))
        {
            return (false, null, fontError);
        }

        if (!TryAssetMap(assetRoot, definition.Icons, ThemeAssetPath.ImageExtensions, out var icons, out var iconsError))
        {
            return (false, null, iconsError);
        }

        if (!TryAssetMap(assetRoot, definition.Effects, ThemeAssetPath.AudioExtensions, out var effects, out var effectsError))
        {
            return (false, null, effectsError);
        }

        return (
            true,
            new ThemeAppearance(logo, font, accentColor, textColor, icons, effects),
            null);
    }

    private static (ThemeSong? Song, string? Error) LoadSong(
        ThemeSongDefinition definition,
        string assetRoot)
    {
        var title = definition.Title?.Trim();
        if (string.IsNullOrEmpty(title))
        {
            return (null, "歌名不能为空");
        }

        if (!TryRequiredColor(definition.BackgroundColor, out var backgroundColor) ||
            !TryOptionalColor(definition.TextColor, out var textColor) ||
            !TryOptionalColor(definition.AccentColor, out var accentColor))
        {
            return (null, "颜色必须使用 #RRGGBB 或 #RRGGBBAA");
        }

        if (!TryRequiredAsset(assetRoot, definition.Audio, ThemeAssetPath.AudioExtensions, out var audio, out var audioError))
        {
            return (null, audioError);
        }

        if (!TryRequiredAsset(assetRoot, definition.Cover, ThemeAssetPath.ImageExtensions, out var cover, out var coverError))
        {
            return (null, coverError);
        }

        if (!TryOptionalAsset(
                assetRoot,
                definition.BackgroundImage,
                ThemeAssetPath.ImageExtensions,
                out var backgroundImage,
                out var backgroundImageError))
        {
            return (null, backgroundImageError);
        }

        var lyricsDefinition = definition.Lyrics ?? new ThemeLyricsDefinition();
        if (!TryOptionalAsset(assetRoot, lyricsDefinition.Original, ThemeAssetPath.LyricExtensions, out var original, out var lyricError))
        {
            return (null, lyricError);
        }

        if (!TryOptionalAsset(assetRoot, lyricsDefinition.Romanized, ThemeAssetPath.LyricExtensions, out var romanized, out lyricError))
        {
            return (null, lyricError);
        }

        if (!TryOptionalAsset(assetRoot, lyricsDefinition.Translation, ThemeAssetPath.LyricExtensions, out var translation, out lyricError))
        {
            return (null, lyricError);
        }

        if (original is null && romanized is null && translation is null)
        {
            return (null, "至少需要一个 LRC 歌词文件");
        }

        return (
            new ThemeSong(
                title,
                NullIfWhiteSpace(definition.Artist),
                audio!,
                cover!,
                new ThemeLyrics(original, romanized, translation),
                backgroundColor!,
                textColor,
                accentColor,
                backgroundImage),
            null);
    }

    private static bool TryRequiredAsset(
        string assetRoot,
        string? candidate,
        IReadOnlySet<string> allowedExtensions,
        out string? normalized,
        out string? error)
    {
        var success = ThemeAssetPath.TryResolveExistingFile(
            assetRoot,
            candidate,
            allowedExtensions,
            out var normalizedValue,
            out _,
            out error);
        normalized = success ? normalizedValue : null;
        return success;
    }

    private static bool TryOptionalAsset(
        string assetRoot,
        string? candidate,
        IReadOnlySet<string> allowedExtensions,
        out string? normalized,
        out string? error)
    {
        normalized = null;
        error = null;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return true;
        }

        return TryRequiredAsset(assetRoot, candidate, allowedExtensions, out normalized, out error);
    }

    private static bool TryAssetMap(
        string assetRoot,
        Dictionary<string, string>? candidates,
        IReadOnlySet<string> allowedExtensions,
        out IReadOnlyDictionary<string, string> normalized,
        out string? error)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        error = null;
        foreach (var pair in candidates ?? [])
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                normalized = values;
                error = "主题素材键不能为空";
                return false;
            }

            if (!TryRequiredAsset(assetRoot, pair.Value, allowedExtensions, out var path, out error))
            {
                normalized = values;
                return false;
            }

            values[pair.Key.Trim()] = path!;
        }

        normalized = values;
        error = null;
        return true;
    }

    private static bool TryRequiredColor(string? candidate, out string? color)
    {
        color = candidate?.Trim();
        return color is not null && HexColorPattern().IsMatch(color);
    }

    private static bool TryOptionalColor(string? candidate, out string? color)
    {
        color = NullIfWhiteSpace(candidate);
        return color is null || HexColorPattern().IsMatch(color);
    }

    private static string SongLabel(ThemeSongDefinition definition) =>
        string.IsNullOrWhiteSpace(definition.Title) ? "未命名" : definition.Title.Trim();

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static ThemePackLoadResult Invalid(string error, IReadOnlyList<string> warnings) =>
        new(false, null, warnings, error);

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex ThemeIdPattern();

    [GeneratedRegex("^#[0-9a-fA-F]{6}([0-9a-fA-F]{2})?$", RegexOptions.CultureInvariant)]
    private static partial Regex HexColorPattern();

    private sealed class ThemePackDefinition
    {
        public int Format { get; set; }
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Author { get; set; }
        public ThemeAppearanceDefinition? Appearance { get; set; }
    }

    private sealed class ThemeAppearanceDefinition
    {
        public string? Logo { get; set; }
        public string? Font { get; set; }
        public string? AccentColor { get; set; }
        public string? TextColor { get; set; }
        public Dictionary<string, string>? Icons { get; set; }
        public Dictionary<string, string>? Effects { get; set; }
    }

    private sealed class ThemeSongDefinition
    {
        public string? Title { get; set; }
        public string? Artist { get; set; }
        public string? Audio { get; set; }
        public string? Cover { get; set; }
        public ThemeLyricsDefinition? Lyrics { get; set; }
        public string? BackgroundColor { get; set; }
        public string? TextColor { get; set; }
        public string? AccentColor { get; set; }
        public string? BackgroundImage { get; set; }
    }

    private sealed class ThemeLyricsDefinition
    {
        public string? Original { get; set; }
        public string? Romanized { get; set; }
        public string? Translation { get; set; }
    }
}
