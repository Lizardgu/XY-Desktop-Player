using XYDesktopPlayer.Core;

var failures = 0;

Run("empty arguments default to window mode", () =>
{
    var result = LaunchOptionsParser.Parse([]);

    Expect.True(result.IsSuccess, result.Error ?? "parse failed");
    Expect.Equal(HostMode.Window, result.Options!.Mode);
    Expect.Equal(null, result.Options.ContentRoot);
    Expect.False(result.Options.SelfTest, "self-test should be disabled by default");
    Expect.Equal(null, result.Options.CapturePath);
});

Run("wallpaper content and self-test flags are parsed together", () =>
{
    var result = LaunchOptionsParser.Parse(
        [
            "--mode", "wallpaper",
            "--content", @"D:\player-pack",
            "--self-test",
            "--capture", @"D:\captures\player.png"
        ]);

    Expect.True(result.IsSuccess, result.Error ?? "parse failed");
    Expect.Equal(HostMode.Wallpaper, result.Options!.Mode);
    Expect.Equal(@"D:\player-pack", result.Options.ContentRoot);
    Expect.True(result.Options.SelfTest, "self-test flag was ignored");
    Expect.Equal(@"D:\captures\player.png", result.Options.CapturePath);
});

Run("unknown mode is rejected with an actionable error", () =>
{
    var result = LaunchOptionsParser.Parse(["--mode", "floating"]);

    Expect.False(result.IsSuccess, "unknown mode should fail");
    Expect.Contains("window", result.Error);
    Expect.Contains("wallpaper", result.Error);
});

Run("missing content value is rejected", () =>
{
    var result = LaunchOptionsParser.Parse(["--content"]);

    Expect.False(result.IsSuccess, "missing content value should fail");
    Expect.Contains("--content", result.Error);
});

Run("a missing content directory is rejected", () =>
{
    var missingPath = Path.Combine(Path.GetTempPath(), "XYDesktopPlayerTests", Guid.NewGuid().ToString("N"));

    var result = ContentRootValidator.Validate(missingPath);

    Expect.False(result.IsValid, "missing directory should fail validation");
    Expect.Contains("不存在", result.Error);
});

Run("an incomplete content directory lists the missing player entries", () =>
{
    using var directory = new TemporaryDirectory();
    File.WriteAllText(Path.Combine(directory.Path, "index.html"), "<!doctype html>");

    var result = ContentRootValidator.Validate(directory.Path);

    Expect.False(result.IsValid, "incomplete player should fail validation");
    Expect.SequenceEqual(
        ["static", "assets/covers", "assets/audios", "assets/lyrics"],
        result.MissingEntries);
});

Run("a complete content directory is normalized and accepted", () =>
{
    using var directory = new TemporaryDirectory();
    File.WriteAllText(Path.Combine(directory.Path, "index.html"), "<!doctype html>");
    Directory.CreateDirectory(Path.Combine(directory.Path, "static"));
    Directory.CreateDirectory(Path.Combine(directory.Path, "assets", "covers"));
    Directory.CreateDirectory(Path.Combine(directory.Path, "assets", "audios"));
    Directory.CreateDirectory(Path.Combine(directory.Path, "assets", "lyrics"));

    var result = ContentRootValidator.Validate(Path.Combine(directory.Path, "."));

    Expect.True(result.IsValid, result.Error ?? "complete player should be valid");
    Expect.Equal(Path.GetFullPath(directory.Path), result.ResolvedContentRoot);
    Expect.Equal(0, result.MissingEntries.Count);
});

Run("web content uses a virtual HTTPS origin instead of a file URI", () =>
{
    using var directory = new TemporaryDirectory("播放器 content with spaces");
    directory.CreateCompletePlayerFixture();

    var mapping = WebContentMapping.Create(directory.Path);

    Expect.Equal(Path.GetFullPath(directory.Path), mapping.ResolvedContentRoot);
    Expect.Equal("https", mapping.StartUri.Scheme);
    Expect.Equal("xydesktop.local", mapping.StartUri.Host);
    Expect.Equal("/index.html", mapping.StartUri.AbsolutePath);
    Expect.False(mapping.StartUri.IsFile, "player must not be launched through file://");
});

Run("desktop host selects the WorkerW after the shell icon view", () =>
{
    var windows = new[]
    {
        new DesktopTopLevelWindow((nint)100, "WorkerW", false),
        new DesktopTopLevelWindow((nint)200, "Progman", false),
        new DesktopTopLevelWindow((nint)300, "WorkerW", true),
        new DesktopTopLevelWindow((nint)400, "WorkerW", false),
        new DesktopTopLevelWindow((nint)500, "ApplicationFrameWindow", false)
    };

    var selected = DesktopWindowSelector.SelectWorkerW(windows);

    Expect.Equal((nint?)400, selected);
});

Run("desktop host reports no target when the shell icon view is absent", () =>
{
    var windows = new[]
    {
        new DesktopTopLevelWindow((nint)100, "Progman", false),
        new DesktopTopLevelWindow((nint)200, "WorkerW", false)
    };

    var selected = DesktopWindowSelector.SelectWorkerW(windows);

    Expect.Equal<nint?>(null, selected);
});

Run("desktop cleanup hides only an empty Explorer-owned target WorkerW", () =>
{
    Expect.True(
        DesktopWorkerCleanupPolicy.ShouldHide(
            new DesktopWorkerCleanupContext(
                IsTargetWorkerW: true,
                IsExplorerOwned: true,
                ChildWindowCount: 0)),
        "an empty Explorer-owned wallpaper WorkerW should be hidden after detach");
});

Run("desktop cleanup preserves unrelated or occupied WorkerW windows", () =>
{
    Expect.False(
        DesktopWorkerCleanupPolicy.ShouldHide(
            new DesktopWorkerCleanupContext(false, true, 0)),
        "the shell icon WorkerW must not be hidden");
    Expect.False(
        DesktopWorkerCleanupPolicy.ShouldHide(
            new DesktopWorkerCleanupContext(true, false, 0)),
        "a WorkerW owned by another process must not be hidden");
    Expect.False(
        DesktopWorkerCleanupPolicy.ShouldHide(
            new DesktopWorkerCleanupContext(true, true, 1)),
        "a WorkerW still hosting another child must not be hidden");
});

Run("desktop icon mask contains physical pixels inside an icon rectangle", () =>
{
    var mask = new DesktopIconMask(
        isValid: true,
        [new DesktopRectangle(10, 20, 30, 40)]);

    Expect.True(mask.Contains(new DesktopPoint(10, 20)), "top-left icon pixel was not masked");
    Expect.True(mask.Contains(new DesktopPoint(29, 39)), "bottom-right icon pixel was not masked");
    Expect.False(mask.Contains(new DesktopPoint(30, 40)), "exclusive rectangle edge was masked");
});

Run("invalid desktop icon mask never consumes input", () =>
{
    var context = WallpaperPointer(
        DesktopPointerEventKind.LeftDown,
        new DesktopPoint(500, 500),
        DesktopIconMask.Invalid);

    Expect.Equal(DesktopPointerAction.PassThrough, DesktopPointerRoutePolicy.Decide(context));
});

Run("desktop right click always remains with Explorer", () =>
{
    var context = WallpaperPointer(
        DesktopPointerEventKind.RightDown,
        new DesktopPoint(500, 500),
        DesktopIconMask.Empty);

    Expect.Equal(DesktopPointerAction.PassThrough, DesktopPointerRoutePolicy.Decide(context));
});

Run("desktop icon masks the player beneath it", () =>
{
    var context = WallpaperPointer(
        DesktopPointerEventKind.LeftDown,
        new DesktopPoint(25, 25),
        new DesktopIconMask(true, [new DesktopRectangle(0, 0, 80, 100)]));

    Expect.Equal(DesktopPointerAction.PassThrough, DesktopPointerRoutePolicy.Decide(context));
});

Run("blank desktop left click is forwarded and consumed", () =>
{
    var context = WallpaperPointer(
        DesktopPointerEventKind.LeftDown,
        new DesktopPoint(500, 500),
        new DesktopIconMask(true, [new DesktopRectangle(0, 0, 80, 100)]));

    Expect.Equal(DesktopPointerAction.ForwardAndConsume, DesktopPointerRoutePolicy.Decide(context));
});

Run("blank desktop left click dismisses an open Explorer menu before forwarding", () =>
{
    var context = WallpaperPointer(
        DesktopPointerEventKind.LeftDown,
        new DesktopPoint(500, 500),
        DesktopIconMask.Empty) with { IsDesktopMenuOpen = true };

    Expect.Equal(
        DesktopPointerAction.DismissMenuForwardAndConsume,
        DesktopPointerRoutePolicy.Decide(context));
});

Run("blank desktop wheel input is forwarded and consumed", () =>
{
    var context = WallpaperPointer(
        DesktopPointerEventKind.Wheel,
        new DesktopPoint(500, 500),
        DesktopIconMask.Empty);

    Expect.Equal(DesktopPointerAction.ForwardAndConsume, DesktopPointerRoutePolicy.Decide(context));
});

Run("blank desktop mouse move is forwarded without blocking Explorer", () =>
{
    var context = WallpaperPointer(
        DesktopPointerEventKind.Move,
        new DesktopPoint(500, 500),
        DesktopIconMask.Empty);

    Expect.Equal(DesktopPointerAction.Forward, DesktopPointerRoutePolicy.Decide(context));
});

Run("window mode never uses desktop pointer forwarding", () =>
{
    var context = WallpaperPointer(
        DesktopPointerEventKind.LeftDown,
        new DesktopPoint(500, 500),
        DesktopIconMask.Empty) with { Mode = HostMode.Window };

    Expect.Equal(DesktopPointerAction.PassThrough, DesktopPointerRoutePolicy.Decide(context));
});

Run("desktop input passes through while WebView is not ready", () =>
{
    var context = WallpaperPointer(
        DesktopPointerEventKind.LeftDown,
        new DesktopPoint(500, 500),
        DesktopIconMask.Empty) with { WebViewReady = false };

    Expect.Equal(DesktopPointerAction.PassThrough, DesktopPointerRoutePolicy.Decide(context));
});

Run("desktop input passes through while wallpaper is detached", () =>
{
    var context = WallpaperPointer(
        DesktopPointerEventKind.LeftDown,
        new DesktopPoint(500, 500),
        DesktopIconMask.Empty) with { DesktopAttached = false };

    Expect.Equal(DesktopPointerAction.PassThrough, DesktopPointerRoutePolicy.Decide(context));
});

Run("input over another application is never forwarded", () =>
{
    var context = WallpaperPointer(
        DesktopPointerEventKind.LeftDown,
        new DesktopPoint(500, 500),
        DesktopIconMask.Empty) with { IsDesktopSurface = false };

    Expect.Equal(DesktopPointerAction.PassThrough, DesktopPointerRoutePolicy.Decide(context));
});

Run("physical desktop coordinates convert to WebView CSS coordinates", () =>
{
    var result = WebPointerCoordinateMapper.ToCssPoint(
        new DesktopPoint(360, 240),
        new DesktopPoint(120, 60),
        dpiScale: 1.5);

    Expect.Equal(new WebPointerPoint(160, 120), result);
});

Run("invalid DPI scale is rejected before dispatch", () =>
{
    Expect.Throws<ArgumentOutOfRangeException>(() =>
        WebPointerCoordinateMapper.ToCssPoint(
            new DesktopPoint(100, 100),
            new DesktopPoint(0, 0),
            dpiScale: 0));
});

Run("desktop interaction runs only when every lifecycle gate is ready", () =>
{
    var ready = new DesktopInteractionState(
        HostMode.Wallpaper,
        UserEnabled: true,
        WebViewReady: true,
        DesktopAttached: true);

    Expect.True(DesktopInteractionStatePolicy.ShouldRun(ready), "ready wallpaper interaction did not start");
    Expect.False(
        DesktopInteractionStatePolicy.ShouldRun(ready with { Mode = HostMode.Window }),
        "window mode started desktop interaction");
    Expect.False(
        DesktopInteractionStatePolicy.ShouldRun(ready with { UserEnabled = false }),
        "user-disabled interaction restarted");
    Expect.False(
        DesktopInteractionStatePolicy.ShouldRun(ready with { WebViewReady = false }),
        "interaction started before navigation");
    Expect.False(
        DesktopInteractionStatePolicy.ShouldRun(ready with { DesktopAttached = false }),
        "interaction started before WorkerW attachment");
});

Run("visible native icons require at least one discovered mask rectangle", () =>
{
    Expect.False(
        DesktopIconSnapshotPolicy.IsUsable(nativeItemCount: 3, rectangleCount: 0),
        "missing UI Automation rectangles exposed visible desktop icons");
    Expect.True(
        DesktopIconSnapshotPolicy.IsUsable(nativeItemCount: 0, rectangleCount: 0),
        "an empty desktop was treated as an icon-discovery failure");
    Expect.True(
        DesktopIconSnapshotPolicy.IsUsable(nativeItemCount: 3, rectangleCount: 3),
        "complete icon rectangles were rejected");
});

Run("a visible other window covering its monitor is full screen", () =>
{
    var context = FullscreenWindow(
        new DesktopRectangle(0, 0, 2560, 1440),
        new DesktopRectangle(0, 0, 2560, 1440));

    Expect.True(
        FullscreenWindowPolicy.IsOtherFullscreen(context),
        "an exact monitor-sized foreground window was rejected");
});

Run("a visible normal application window blocks desktop playback", () =>
{
    var context = FullscreenWindow(
        new DesktopRectangle(0, 0, 2560, 1400),
        new DesktopRectangle(0, 0, 2560, 1440));

    Expect.True(
        FullscreenWindowPolicy.IsOtherFullscreen(context),
        "a normal foreground application was ignored");
});

Run("a standard maximized window triggers automatic pause with the taskbar visible", () =>
{
    var context = FullscreenWindow(
        new DesktopRectangle(0, 0, 2560, 1400),
        new DesktopRectangle(0, 0, 2560, 1440)) with
    {
        IsMaximized = true
    };

    Expect.True(
        FullscreenWindowPolicy.IsOtherFullscreen(context),
        "a standard maximized foreground window was ignored");
});

Run("full-screen detection supports negative monitor coordinates", () =>
{
    var context = FullscreenWindow(
        new DesktopRectangle(-1920, 0, 0, 1080),
        new DesktopRectangle(-1920, 0, 0, 1080));

    Expect.True(
        FullscreenWindowPolicy.IsOtherFullscreen(context),
        "a full-screen window on the left monitor was rejected");
});

Run("full-screen detection tolerates two physical pixels on every edge", () =>
{
    var context = FullscreenWindow(
        new DesktopRectangle(2, 2, 2558, 1438),
        new DesktopRectangle(0, 0, 2560, 1440));

    Expect.True(
        FullscreenWindowPolicy.IsOtherFullscreen(context),
        "the documented two-pixel tolerance was not inclusive");
});

Run("full-screen detection excludes non-player foreground surfaces", () =>
{
    var context = FullscreenWindow(
        new DesktopRectangle(0, 0, 2560, 1440),
        new DesktopRectangle(0, 0, 2560, 1440));

    Expect.False(FullscreenWindowPolicy.IsOtherFullscreen(context with { IsOwnProcess = true }), "own process was accepted");
    Expect.False(FullscreenWindowPolicy.IsOtherFullscreen(context with { IsDesktopSurface = true }), "desktop surface was accepted");
    Expect.False(FullscreenWindowPolicy.IsOtherFullscreen(context with { IsVisible = false }), "hidden window was accepted");
    Expect.False(FullscreenWindowPolicy.IsOtherFullscreen(context with { IsMinimized = true }), "minimized window was accepted");
    Expect.False(FullscreenWindowPolicy.IsOtherFullscreen(context with { IsCloaked = true }), "cloaked window was accepted");
    Expect.False(FullscreenWindowPolicy.IsOtherFullscreen(context with { HasForegroundWindow = false }), "missing foreground window was accepted");
});

Run("desktop shell surfaces never block playback", () =>
{
    foreach (var className in new[]
    {
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "#32768",
        "Windows.UI.Core.CoreWindow",
        "XamlExplorerHostIslandWindow"
    })
    {
        Expect.True(
            ForegroundShellSurfacePolicy.IsShellSurface(className),
            $"shell surface was treated as an application: {className}");
    }

    Expect.False(
        ForegroundShellSurfacePolicy.IsShellSurface("CabinetWClass"),
        "an Explorer folder window was treated as desktop shell UI");
    Expect.False(
        ForegroundShellSurfacePolicy.IsShellSurface("Chrome_WidgetWin_1"),
        "a normal application window was treated as desktop shell UI");
});

Run("entering full screen requests one automatic pause", () =>
{
    var action = FullscreenPlaybackPolicy.Decide(
        new FullscreenPlaybackState(
            WasFullscreen: false,
            IsFullscreen: true,
            AutoPauseArmed: false));

    Expect.Equal(FullscreenPlaybackAction.Pause, action);
});

Run("remaining full screen does not request repeated pauses", () =>
{
    var action = FullscreenPlaybackPolicy.Decide(
        new FullscreenPlaybackState(
            WasFullscreen: true,
            IsFullscreen: true,
            AutoPauseArmed: true));

    Expect.Equal(FullscreenPlaybackAction.None, action);
});

Run("leaving full screen resumes only an automatically paused session", () =>
{
    var armedAction = FullscreenPlaybackPolicy.Decide(
        new FullscreenPlaybackState(
            WasFullscreen: true,
            IsFullscreen: false,
            AutoPauseArmed: true));
    var manualPauseAction = FullscreenPlaybackPolicy.Decide(
        new FullscreenPlaybackState(
            WasFullscreen: true,
            IsFullscreen: false,
            AutoPauseArmed: false));

    Expect.Equal(FullscreenPlaybackAction.Resume, armedAction);
    Expect.Equal(FullscreenPlaybackAction.None, manualPauseAction);
});

Run("startup stays silent while another application is in front", () =>
{
    var action = FullscreenPlaybackPolicy.Decide(
        new FullscreenPlaybackState(
            WasFullscreen: true,
            IsFullscreen: true,
            AutoPauseArmed: false,
            StartupPending: true));

    Expect.Equal(FullscreenPlaybackAction.None, action);
});

Run("returning to the desktop starts pending autoplay", () =>
{
    var action = FullscreenPlaybackPolicy.Decide(
        new FullscreenPlaybackState(
            WasFullscreen: true,
            IsFullscreen: false,
            AutoPauseArmed: false,
            StartupPending: true));

    Expect.Equal(FullscreenPlaybackAction.Start, action);
});

Run("window mode checks only the window tray item", () =>
{
    var state = HostModeMenuState.From(HostMode.Window);

    Expect.True(state.WindowModeChecked, "window mode item was not checked");
    Expect.False(state.WallpaperModeChecked, "desktop mode item remained checked");
});

Run("wallpaper mode checks only the desktop tray item", () =>
{
    var state = HostModeMenuState.From(HostMode.Wallpaper);

    Expect.False(state.WindowModeChecked, "window mode item remained checked");
    Expect.True(state.WallpaperModeChecked, "desktop mode item was not checked");
});

Console.WriteLine(failures == 0
    ? "All core tests passed."
    : $"{failures} core test(s) failed.");

return failures == 0 ? 0 : 1;

static DesktopPointerRouteContext WallpaperPointer(
    DesktopPointerEventKind eventKind,
    DesktopPoint point,
    DesktopIconMask iconMask) =>
    new(
        HostMode.Wallpaper,
        WebViewReady: true,
        DesktopAttached: true,
        IsDesktopSurface: true,
        point,
        eventKind,
        iconMask);

static FullscreenWindowContext FullscreenWindow(
    DesktopRectangle windowBounds,
    DesktopRectangle monitorBounds) =>
    new(
        HasForegroundWindow: true,
        IsOwnProcess: false,
        IsDesktopSurface: false,
        IsVisible: true,
        IsMinimized: false,
        IsCloaked: false,
        IsMaximized: false,
        windowBounds,
        monitorBounds);

void Run(string name, Action test)
{
    try
    {
        test();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {name}: {exception.Message}");
    }
}

internal static class Expect
{
    public static void True(bool value, string message)
    {
        if (!value)
        {
            throw new InvalidOperationException(message);
        }
    }

    public static void False(bool value, string message) => True(!value, message);

    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected <{expected}>, got <{actual}>.");
        }
    }

    public static void Contains(string expectedPart, string? actual)
    {
        if (actual is null || !actual.Contains(expectedPart, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Expected <{actual}> to contain <{expectedPart}>.");
        }
    }

    public static void Throws<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name} to be thrown.");
    }

    public static void SequenceEqual<T>(IReadOnlyList<T> expected, IReadOnlyList<T> actual)
    {
        if (expected.Count != actual.Count || !expected.SequenceEqual(actual))
        {
            throw new InvalidOperationException(
                $"Expected <{string.Join(", ", expected)}>, got <{string.Join(", ", actual)}>.");
        }
    }
}

internal sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory(string? leafName = null)
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "XYDesktopPlayerTests",
            $"{Guid.NewGuid():N}-{leafName ?? "fixture"}");
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void CreateCompletePlayerFixture()
    {
        File.WriteAllText(System.IO.Path.Combine(Path, "index.html"), "<!doctype html>");
        Directory.CreateDirectory(System.IO.Path.Combine(Path, "static"));
        Directory.CreateDirectory(System.IO.Path.Combine(Path, "assets", "covers"));
        Directory.CreateDirectory(System.IO.Path.Combine(Path, "assets", "audios"));
        Directory.CreateDirectory(System.IO.Path.Combine(Path, "assets", "lyrics"));
    }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
