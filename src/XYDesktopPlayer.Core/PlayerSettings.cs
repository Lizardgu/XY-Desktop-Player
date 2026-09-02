namespace XYDesktopPlayer.Core;

public sealed record PlayerSettings(string? SelectedThemeId)
{
    public static PlayerSettings Default { get; } = new((string?)null);
}

public sealed record PlayerSettingsLoadResult(
    PlayerSettings Settings,
    string? Warning);

public sealed record PlayerSettingsSaveResult(
    bool IsSuccess,
    string? Error);
