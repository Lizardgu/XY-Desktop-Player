namespace XYDesktopPlayer.Core;

public readonly record struct DesktopWorkerCleanupContext(
    bool IsTargetWorkerW,
    bool IsExplorerOwned,
    int ChildWindowCount);

public static class DesktopWorkerCleanupPolicy
{
    public static bool ShouldHide(DesktopWorkerCleanupContext context) =>
        context.IsTargetWorkerW &&
        context.IsExplorerOwned &&
        context.ChildWindowCount == 0;
}
