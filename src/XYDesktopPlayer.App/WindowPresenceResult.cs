using XYDesktopPlayer.Core;

namespace XYDesktopPlayer.App;

/// <summary>
/// Result of a single window-presence scan.
/// </summary>
internal sealed class WindowPresenceResult(
    bool IsBlocked,
    ForegroundWindowContext Context,
    ForegroundInfo Foreground,
    IReadOnlyList<BlockingWindowInfo> BlockingWindows,
    string? Error)
{
    public bool IsBlocked { get; } = IsBlocked;
    public ForegroundWindowContext Context { get; } = Context;
    public ForegroundInfo Foreground { get; } = Foreground;
    public IReadOnlyList<BlockingWindowInfo> BlockingWindows { get; } = BlockingWindows;
    public string? Error { get; } = Error;
}

/// <summary>
/// Details of the current foreground window, used for diagnostics.
/// </summary>
internal sealed record ForegroundInfo(
    nint Handle,
    nint RootHandle,
    uint ProcessId,
    string? ProcessName,
    string? ClassName,
    string? Title,
    bool Visible,
    bool Minimized,
    bool Cloaked,
    bool IsOwnProcess,
    bool IsDesktopSurface,
    bool IsShellOverlay);

/// <summary>
/// A visible, on-screen, foreign top-level window that should block playback.
/// </summary>
internal sealed record BlockingWindowInfo(
    nint Handle,
    uint ProcessId,
    string? ProcessName,
    string? ClassName,
    string? Title,
    int Left,
    int Top,
    int Right,
    int Bottom,
    bool Visible,
    bool Minimized,
    bool Cloaked);
