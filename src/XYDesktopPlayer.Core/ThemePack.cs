namespace XYDesktopPlayer.Core;

public sealed record ThemeLyrics(
    string? Original,
    string? Romanized,
    string? Translation);

public sealed record ThemeSong(
    string Title,
    string? Artist,
    string Audio,
    string Cover,
    ThemeLyrics Lyrics,
    string BackgroundColor,
    string? TextColor,
    string? AccentColor,
    string? BackgroundImage);

public sealed record ThemeAppearance(
    string? Logo,
    string? Font,
    string? AccentColor,
    string? TextColor,
    IReadOnlyDictionary<string, string> Icons,
    IReadOnlyDictionary<string, string> Effects);

public sealed record ThemePack(
    int Format,
    string Id,
    string Name,
    string? Author,
    ThemeAppearance Appearance,
    IReadOnlyList<ThemeSong> Songs,
    string DefinitionRoot,
    string AssetRoot,
    bool IsBuiltIn);

public sealed record ThemePackLoadResult(
    bool IsValid,
    ThemePack? Theme,
    IReadOnlyList<string> Warnings,
    string? Error);

public sealed record ThemeCatalogDiagnostic(
    string ThemeFolder,
    string Message);

public sealed record ThemeCatalogResult(
    IReadOnlyList<ThemePack> Themes,
    IReadOnlyList<ThemeCatalogDiagnostic> Diagnostics);
