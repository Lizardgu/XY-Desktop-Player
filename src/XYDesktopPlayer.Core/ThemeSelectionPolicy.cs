namespace XYDesktopPlayer.Core;

public static class ThemeSelectionPolicy
{
    public static ThemePack Select(
        IReadOnlyList<ThemePack> themes,
        string? selectedThemeId,
        string? defaultThemeId,
        string builtInThemeId)
    {
        ArgumentNullException.ThrowIfNull(themes);
        var byId = themes.ToDictionary(theme => theme.Id, StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(selectedThemeId) &&
            byId.TryGetValue(selectedThemeId, out var selected))
        {
            return selected;
        }

        if (!string.IsNullOrWhiteSpace(defaultThemeId) &&
            byId.TryGetValue(defaultThemeId, out var releaseDefault))
        {
            return releaseDefault;
        }

        if (byId.TryGetValue(builtInThemeId, out var builtIn))
        {
            return builtIn;
        }

        throw new InvalidOperationException($"未找到内置主题：{builtInThemeId}");
    }
}
