namespace XYDesktopPlayer.Core;

public static class ForegroundShellSurfacePolicy
{
    private static readonly HashSet<string> DesktopSurfaceClasses = new(StringComparer.Ordinal)
    {
        "Progman",
        "WorkerW"
    };

    private static readonly HashSet<string> ShellSurfaceClasses = new(StringComparer.Ordinal)
    {
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "#32768"
    };

    public static bool IsShellSurface(string className) =>
        !string.IsNullOrEmpty(className) && ShellSurfaceClasses.Contains(className);

    public static bool IsDesktopSurface(string className) =>
        !string.IsNullOrEmpty(className) && DesktopSurfaceClasses.Contains(className);
}

public readonly record struct ForegroundWindowContext(
    bool HasForegroundWindow,
    bool IsOwnProcess,
    bool IsDesktopSurface,
    bool IsShellOverlay,
    bool IsVisible,
    bool IsMinimized,
    bool IsCloaked);

public static class ForegroundWindowPolicy
{
    public static bool ShouldBlockPlayback(ForegroundWindowContext context)
    {
        if (!context.HasForegroundWindow ||
            context.IsOwnProcess ||
            context.IsDesktopSurface ||
            context.IsShellOverlay ||
            !context.IsVisible ||
            context.IsMinimized ||
            context.IsCloaked)
        {
            return false;
        }

        return true;
    }

    public static bool CanStartOrResumePlayback(ForegroundWindowContext context)
    {
        if (!context.HasForegroundWindow ||
            !context.IsVisible ||
            context.IsMinimized ||
            context.IsCloaked)
        {
            return false;
        }

        return context.IsOwnProcess || context.IsDesktopSurface;
    }
}

public readonly record struct ForegroundPlaybackState(
    bool WasBlocked,
    bool IsBlocked,
    bool AutoPauseArmed,
    bool CanStartOrResume = false,
    bool StartupPending = false);

public enum ForegroundPlaybackAction
{
    None,
    Pause,
    Resume,
    Start
}

public static class ForegroundPlaybackPolicy
{
    public static ForegroundPlaybackAction Decide(ForegroundPlaybackState state)
    {
        if (state.StartupPending)
        {
            return !state.IsBlocked && state.CanStartOrResume
                ? ForegroundPlaybackAction.Start
                : ForegroundPlaybackAction.None;
        }

        if (!state.WasBlocked && state.IsBlocked)
        {
            return ForegroundPlaybackAction.Pause;
        }

        if (state.WasBlocked &&
            !state.IsBlocked &&
            state.AutoPauseArmed &&
            state.CanStartOrResume)
        {
            return ForegroundPlaybackAction.Resume;
        }

        return ForegroundPlaybackAction.None;
    }
}
