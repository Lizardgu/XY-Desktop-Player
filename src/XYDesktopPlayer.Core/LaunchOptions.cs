namespace XYDesktopPlayer.Core;

public enum HostMode
{
    Window,
    Wallpaper
}

public sealed record LaunchOptions(
    HostMode Mode,
    string? ContentRoot,
    bool SelfTest,
    string? CapturePath);

public sealed record LaunchOptionsParseResult(
    bool IsSuccess,
    LaunchOptions? Options,
    string? Error);
