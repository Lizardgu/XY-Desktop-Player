namespace XYDesktopPlayer.Core;

public sealed record ThemeMenuEntry(
    string Id,
    string Name,
    bool IsChecked,
    bool IsBuiltIn);

public static class ThemeMenuModel
{
    public static IReadOnlyList<ThemeMenuEntry> Create(
        IReadOnlyList<ThemePack> themes,
        string selectedThemeId)
    {
        ArgumentNullException.ThrowIfNull(themes);
        ArgumentException.ThrowIfNullOrWhiteSpace(selectedThemeId);
        return themes.Select(theme => new ThemeMenuEntry(
                theme.Id,
                theme.Name,
                string.Equals(theme.Id, selectedThemeId, StringComparison.Ordinal),
                theme.IsBuiltIn))
            .ToArray();
    }
}
