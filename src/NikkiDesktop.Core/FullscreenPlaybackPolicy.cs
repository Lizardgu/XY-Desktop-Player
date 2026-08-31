namespace NikkiDesktop.Core;

public readonly record struct FullscreenWindowContext(
    bool HasForegroundWindow,
    bool IsOwnProcess,
    bool IsDesktopSurface,
    bool IsVisible,
    bool IsMinimized,
    bool IsCloaked,
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
            context.IsCloaked ||
            !IsValid(context.WindowBounds) ||
            !IsValid(context.MonitorBounds))
        {
            return false;
        }

        return context.WindowBounds.Left <= context.MonitorBounds.Left + EdgeTolerance &&
               context.WindowBounds.Top <= context.MonitorBounds.Top + EdgeTolerance &&
               context.WindowBounds.Right >= context.MonitorBounds.Right - EdgeTolerance &&
               context.WindowBounds.Bottom >= context.MonitorBounds.Bottom - EdgeTolerance;
    }

    private static bool IsValid(DesktopRectangle rectangle) =>
        rectangle.Right > rectangle.Left && rectangle.Bottom > rectangle.Top;
}

public readonly record struct FullscreenPlaybackState(
    bool WasFullscreen,
    bool IsFullscreen,
    bool AutoPauseArmed);

public enum FullscreenPlaybackAction
{
    None,
    Pause,
    Resume
}

public static class FullscreenPlaybackPolicy
{
    public static FullscreenPlaybackAction Decide(FullscreenPlaybackState state)
    {
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
