namespace XYDesktopPlayer.Core;

public readonly record struct HostModeMenuState(
    bool WindowModeChecked,
    bool WallpaperModeChecked)
{
    public static HostModeMenuState From(HostMode mode) => mode switch
    {
        HostMode.Window => new HostModeMenuState(
            WindowModeChecked: true,
            WallpaperModeChecked: false),
        HostMode.Wallpaper => new HostModeMenuState(
            WindowModeChecked: false,
            WallpaperModeChecked: true),
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
    };
}
