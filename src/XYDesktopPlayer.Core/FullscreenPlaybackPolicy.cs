namespace XYDesktopPlayer.Core;

public static class ForegroundShellSurfacePolicy
{
    private static readonly HashSet<string> ShellSurfaceClasses = new(StringComparer.Ordinal)
    {
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "#32768",
        "Windows.UI.Core.CoreWindow",
        "XamlExplorerHostIslandWindow"
    };

    public static bool IsShellSurface(string className) =>
        !string.IsNullOrEmpty(className) && ShellSurfaceClasses.Contains(className);
}

public readonly record struct FullscreenWindowContext(
    bool HasForegroundWindow,
    bool IsOwnProcess,
    bool IsDesktopSurface,
    bool IsVisible,
    bool IsMinimized,
    bool IsCloaked,
    bool IsMaximized,
    DesktopRectangle WindowBounds,
    DesktopRectangle MonitorBounds);

public static class FullscreenWindowPolicy
{
    public const int EdgeTolerance = 2;

    public static bool IsOtherFullscreen(FullscreenWindowContext context)
    {
        if (!context.HasForegroundWindow ||
            context.IsOwnProcess ||
            context.IsDesktopSurface ||
            !context.IsVisible ||
            context.IsMinimized ||
            context.IsCloaked)
        {
            return false;
        }

        return true;
    }
}

public readonly record struct FullscreenPlaybackState(
    bool WasFullscreen,
    bool IsFullscreen,
    bool AutoPauseArmed,
    bool StartupPending = false);

public enum FullscreenPlaybackAction
{
    None,
    Pause,
    Resume,
    Start
}

public static class FullscreenPlaybackPolicy
{
    public static FullscreenPlaybackAction Decide(FullscreenPlaybackState state)
    {
        if (state.StartupPending)
        {
            return state.IsFullscreen
                ? FullscreenPlaybackAction.None
                : FullscreenPlaybackAction.Start;
        }

        if (!state.WasFullscreen && state.IsFullscreen)
        {
            return FullscreenPlaybackAction.Pause;
        }

        if (state.WasFullscreen && !state.IsFullscreen && state.AutoPauseArmed)
        {
            return FullscreenPlaybackAction.Resume;
        }

        return FullscreenPlaybackAction.None;
    }
}
