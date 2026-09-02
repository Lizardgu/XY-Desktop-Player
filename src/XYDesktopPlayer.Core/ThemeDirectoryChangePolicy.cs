namespace XYDesktopPlayer.Core;

public static class ThemeDirectoryChangePolicy
{
    public static bool ShouldSchedule(WatcherChangeTypes changeType) =>
        (changeType & (WatcherChangeTypes.Created |
                       WatcherChangeTypes.Deleted |
                       WatcherChangeTypes.Renamed)) != 0;
}
