using System.Text.Json;
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
        ["static"],
        result.MissingEntries);
});

Run("a complete content directory is normalized and accepted", () =>
{
    using var directory = new TemporaryDirectory();
    File.WriteAllText(Path.Combine(directory.Path, "index.html"), "<!doctype html>");
    Directory.CreateDirectory(Path.Combine(directory.Path, "static"));

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

Run("player and selected theme use separate virtual HTTPS hosts", () =>
{
    using var directory = new TemporaryDirectory("dual-web-hosts");
    var playerRoot = Path.Combine(directory.Path, "player");
    Directory.CreateDirectory(playerRoot);
    File.WriteAllText(Path.Combine(playerRoot, "index.html"), "<!doctype html>");
    Directory.CreateDirectory(Path.Combine(playerRoot, "static"));
    var themeRoot = directory.CreateThemeFixture("theme", "warm-nikki", "无限暖暖");
    var theme = ThemePackLoader.Load(themeRoot, themeRoot, isBuiltIn: false).Theme!;

    var playerMapping = WebContentMapping.Create(playerRoot);
    var themeMapping = ThemeWebContentMapping.Create(theme);

    Expect.Equal("xydesktop.local", playerMapping.VirtualHostName);
    Expect.Equal("theme.xydesktop.local", themeMapping.VirtualHostName);
    Expect.Equal(Path.GetFullPath(themeRoot), themeMapping.ResolvedAssetRoot);
});

Run("theme menu checks only the selected theme and marks built in", () =>
{
    using var directory = new TemporaryDirectory("theme-menu");
    var builtInRoot = directory.CreateThemeFixture("builtin", "bocchi", "孤独摇滚");
    var userRoot = directory.CreateThemeFixture("user", "warm-nikki", "无限暖暖");
    var builtIn = ThemePackLoader.Load(builtInRoot, builtInRoot, isBuiltIn: true).Theme!;
    var user = ThemePackLoader.Load(userRoot, userRoot, isBuiltIn: false).Theme!;

    var entries = ThemeMenuModel.Create([builtIn, user], "warm-nikki");

    Expect.SequenceEqual(["bocchi", "warm-nikki"], entries.Select(entry => entry.Id).ToArray());
    Expect.False(entries[0].IsChecked, "built-in theme should not be selected");
    Expect.True(entries[0].IsBuiltIn, "built-in theme marker was lost");
    Expect.True(entries[1].IsChecked, "selected user theme should be checked");
});

Run("a valid theme loads per-song media lyrics and fixed colors", () =>
{
    using var directory = new TemporaryDirectory("valid-theme");
    var themeRoot = directory.CreateThemeFixture(
        "warm-nikki",
        "warm-nikki",
        "无限暖暖",
        backgroundColor: "#D7A0B7",
        textColor: "#FFFFFF",
        accentColor: "#F2B8D5");

    var result = ThemePackLoader.Load(themeRoot, themeRoot, isBuiltIn: false);

    Expect.True(result.IsValid, result.Error ?? "valid theme was rejected");
    Expect.Equal("warm-nikki", result.Theme!.Id);
    Expect.Equal("无限暖暖", result.Theme.Name);
    Expect.False(result.Theme.IsBuiltIn, "user theme was marked as built in");
    Expect.Equal(1, result.Theme.Songs.Count);
    var song = result.Theme.Songs[0];
    Expect.Equal("测试歌曲", song.Title);
    Expect.Equal("测试歌手", song.Artist);
    Expect.Equal("audio/01.mp3", song.Audio);
    Expect.Equal("images/covers/01.png", song.Cover);
    Expect.Equal("lyrics/01.lrc", song.Lyrics.Original);
    Expect.Equal("#D7A0B7", song.BackgroundColor);
    Expect.Equal("#FFFFFF", song.TextColor);
    Expect.Equal("#F2B8D5", song.AccentColor);
});

Run("theme asset paths cannot escape their theme or use a network URL", () =>
{
    using var directory = new TemporaryDirectory("unsafe-theme-paths");
    var themeRoot = directory.CreateThemeFixture("unsafe", "unsafe-theme", "不安全主题");
    directory.ReplaceThemeSongs(
        themeRoot,
        [
            directory.ThemeSong(audio: "../outside.mp3"),
            directory.ThemeSong(audio: "https://example.com/song.mp3")
        ]);

    var result = ThemePackLoader.Load(themeRoot, themeRoot, isBuiltIn: false);

    Expect.False(result.IsValid, "theme with only unsafe songs should be rejected");
    Expect.Contains("没有可播放歌曲", result.Error);
    Expect.Equal(2, result.Warnings.Count);
});

Run("an invalid song is skipped while another complete song remains", () =>
{
    using var directory = new TemporaryDirectory("partly-valid-theme");
    var themeRoot = directory.CreateThemeFixture("partial", "partial-theme", "部分有效");
    directory.ReplaceThemeSongs(
        themeRoot,
        [
            directory.ThemeSong(title: "损坏歌曲", cover: "images/covers/missing.png"),
            directory.ThemeSong(title: "可用歌曲")
        ]);

    var result = ThemePackLoader.Load(themeRoot, themeRoot, isBuiltIn: false);

    Expect.True(result.IsValid, result.Error ?? "complete song should keep the theme valid");
    Expect.Equal(1, result.Theme!.Songs.Count);
    Expect.Equal("可用歌曲", result.Theme.Songs[0].Title);
    Expect.Equal(1, result.Warnings.Count);
    Expect.Contains("损坏歌曲", result.Warnings[0]);
});

Run("a null song entry is skipped instead of crashing the theme loader", () =>
{
    using var directory = new TemporaryDirectory("null-song");
    var themeRoot = directory.CreateThemeFixture("null-song", "null-song", "空项主题");
    File.WriteAllText(
        Path.Combine(themeRoot, "songs.json"),
        $"[null,{JsonSerializer.Serialize(directory.ThemeSong(title: "可用歌曲"))}]");

    var result = ThemePackLoader.Load(themeRoot, themeRoot, isBuiltIn: false);

    Expect.True(result.IsValid, result.Error ?? "null entry should not reject the complete song");
    Expect.Equal(1, result.Theme!.Songs.Count);
    Expect.Equal("可用歌曲", result.Theme.Songs[0].Title);
    Expect.Equal(1, result.Warnings.Count);
});

Run("a song requires an audio cover lyric and hexadecimal background color", () =>
{
    using var directory = new TemporaryDirectory("incomplete-song");
    var themeRoot = directory.CreateThemeFixture("incomplete", "incomplete-theme", "不完整主题");
    directory.ReplaceThemeSongs(
        themeRoot,
        [directory.ThemeSong(lyrics: new { }, backgroundColor: "pink")]);

    var result = ThemePackLoader.Load(themeRoot, themeRoot, isBuiltIn: false);

    Expect.False(result.IsValid, "song without lyrics and a valid color should be rejected");
    Expect.Contains("没有可播放歌曲", result.Error);
    Expect.Equal(1, result.Warnings.Count);
});

Run("optional author appearance artist and color overrides may be omitted", () =>
{
    using var directory = new TemporaryDirectory("minimal-theme");
    var themeRoot = directory.CreateThemeFixture("minimal", "minimal-theme", "最小主题");
    File.WriteAllText(
        Path.Combine(themeRoot, "pack.json"),
        JsonSerializer.Serialize(new { format = 1, id = "minimal-theme", name = "最小主题" }));
    directory.ReplaceThemeSongs(
        themeRoot,
        [new
        {
            title = "最小歌曲",
            audio = "audio/01.mp3",
            cover = "images/covers/01.png",
            backgroundColor = "#112233",
            lyrics = new { original = "lyrics/01.lrc" }
        }]);

    var result = ThemePackLoader.Load(themeRoot, themeRoot, isBuiltIn: false);

    Expect.True(result.IsValid, result.Error ?? "minimal theme was rejected");
    Expect.Equal(null, result.Theme!.Author);
    Expect.Equal(null, result.Theme.Songs[0].Artist);
    Expect.Equal(null, result.Theme.Songs[0].TextColor);
    Expect.Equal(null, result.Theme.Songs[0].AccentColor);
});

Run("malformed JSON and an empty song list produce actionable errors", () =>
{
    using var directory = new TemporaryDirectory("bad-json");
    var malformedRoot = directory.CreateThemeFixture("malformed", "malformed", "损坏配置");
    File.WriteAllText(Path.Combine(malformedRoot, "pack.json"), "{");
    var malformed = ThemePackLoader.Load(malformedRoot, malformedRoot, isBuiltIn: false);
    Expect.False(malformed.IsValid, "malformed pack.json should fail");
    Expect.Contains("读取失败", malformed.Error);

    var emptyRoot = directory.CreateThemeFixture("empty", "empty-theme", "空主题");
    directory.ReplaceThemeSongs(emptyRoot, []);
    var empty = ThemePackLoader.Load(emptyRoot, emptyRoot, isBuiltIn: false);
    Expect.False(empty.IsValid, "theme without songs should fail");
    Expect.Contains("没有可播放歌曲", empty.Error);
});

Run("unsupported executable-like media extensions are rejected", () =>
{
    using var directory = new TemporaryDirectory("bad-extension");
    var themeRoot = directory.CreateThemeFixture("extension", "bad-extension", "错误扩展名");
    File.WriteAllText(Path.Combine(themeRoot, "audio", "01.exe"), "not executable test data");
    directory.ReplaceThemeSongs(themeRoot, [directory.ThemeSong(audio: "audio/01.exe")]);

    var result = ThemePackLoader.Load(themeRoot, themeRoot, isBuiltIn: false);

    Expect.False(result.IsValid, "executable-like audio should be rejected");
    Expect.Contains("没有可播放歌曲", result.Error);
    Expect.Contains("不支持的素材类型", result.Warnings.Single());
});

Run("catalog excludes duplicate user ids and the built-in reserved id", () =>
{
    using var directory = new TemporaryDirectory("catalog");
    var builtInRoot = directory.CreateThemeFixture("builtin", "bocchi", "孤独摇滚");
    var userRoot = Path.Combine(directory.Path, "users");
    Directory.CreateDirectory(userRoot);
    directory.CreateThemeFixture("users/one", "duplicate", "重复一");
    directory.CreateThemeFixture("users/two", "duplicate", "重复二");
    directory.CreateThemeFixture("users/reserved", "bocchi", "伪装内置");
    directory.CreateThemeFixture("users/valid", "warm-nikki", "无限暖暖");
    var builtIn = ThemePackLoader.Load(builtInRoot, builtInRoot, isBuiltIn: true).Theme!;

    var catalog = ThemeCatalog.Scan(builtIn, userRoot);

    Expect.SequenceEqual(
        ["bocchi", "warm-nikki"],
        catalog.Themes.Select(theme => theme.Id).ToArray());
    Expect.Equal(3, catalog.Diagnostics.Count);
});

Run("theme selection prefers the user choice then release default then built in", () =>
{
    using var directory = new TemporaryDirectory("selection");
    var builtInRoot = directory.CreateThemeFixture("builtin", "bocchi", "孤独摇滚");
    var nikkiRoot = directory.CreateThemeFixture("nikki", "warm-nikki", "无限暖暖");
    var zzzRoot = directory.CreateThemeFixture("zzz", "zenless-zone-zero", "绝区零");
    var themes = new[]
    {
        ThemePackLoader.Load(builtInRoot, builtInRoot, isBuiltIn: true).Theme!,
        ThemePackLoader.Load(nikkiRoot, nikkiRoot, isBuiltIn: false).Theme!,
        ThemePackLoader.Load(zzzRoot, zzzRoot, isBuiltIn: false).Theme!
    };

    Expect.Equal(
        "zenless-zone-zero",
        ThemeSelectionPolicy.Select(themes, "zenless-zone-zero", "warm-nikki", "bocchi").Id);
    Expect.Equal(
        "warm-nikki",
        ThemeSelectionPolicy.Select(themes, "missing", "warm-nikki", "bocchi").Id);
    Expect.Equal(
        "bocchi",
        ThemeSelectionPolicy.Select(themes, "missing", "also-missing", "bocchi").Id);
});

Run("missing and damaged settings fall back without blocking startup", () =>
{
    using var directory = new TemporaryDirectory("settings-fallback");
    var settingsPath = Path.Combine(directory.Path, "settings.json");
    var missing = PlayerSettingsStore.Load(settingsPath);
    Expect.Equal(null, missing.Settings.SelectedThemeId);
    Expect.Equal(null, missing.Warning);

    File.WriteAllText(settingsPath, "{");
    var damaged = PlayerSettingsStore.Load(settingsPath);
    Expect.Equal(null, damaged.Settings.SelectedThemeId);
    Expect.Contains("设置文件", damaged.Warning);
});

Run("settings save atomically and remember only the selected theme", () =>
{
    using var directory = new TemporaryDirectory("settings-save");
    var settingsPath = Path.Combine(directory.Path, "nested", "settings.json");

    var save = PlayerSettingsStore.Save(
        settingsPath,
        new PlayerSettings("warm-nikki"));
    var loaded = PlayerSettingsStore.Load(settingsPath);

    Expect.True(save.IsSuccess, save.Error ?? "settings save failed");
    Expect.Equal("warm-nikki", loaded.Settings.SelectedThemeId);
    Expect.Equal(0, Directory.GetFiles(Path.GetDirectoryName(settingsPath)!, "*.tmp").Length);
    var json = File.ReadAllText(settingsPath);
    Expect.False(json.Contains("pause", StringComparison.OrdinalIgnoreCase), "pause state leaked into persisted settings");
});

Run("theme runtime data contains virtual media URLs lyrics and no local paths", () =>
{
    using var directory = new TemporaryDirectory("runtime-payload");
    var themeRoot = directory.CreateThemeFixture("runtime", "runtime-theme", "运行主题");
    File.Move(
        Path.Combine(themeRoot, "audio", "01.mp3"),
        Path.Combine(themeRoot, "audio", "主题 歌.mp3"));
    File.Move(
        Path.Combine(themeRoot, "images", "covers", "01.png"),
        Path.Combine(themeRoot, "images", "covers", "封 面.png"));
    File.WriteAllText(Path.Combine(themeRoot, "lyrics", "01.lrc"), "[00:00.00]主题歌词");
    directory.ReplaceThemeSongs(
        themeRoot,
        [directory.ThemeSong(
            audio: "audio/主题 歌.mp3",
            cover: "images/covers/封 面.png",
            backgroundColor: "#ABCDEF",
            textColor: "#102030",
            accentColor: "#405060")]);
    var theme = ThemePackLoader.Load(themeRoot, themeRoot, isBuiltIn: false).Theme!;

    var payload = ThemeRuntimePayloadFactory.Create(theme, "theme.xydesktop.local");

    Expect.Equal("runtime-theme", payload.Id);
    Expect.Equal(1, payload.Songs.Count);
    Expect.Contains("https://theme.xydesktop.local/audio/", payload.Songs[0].Audio);
    Expect.Contains("%20", payload.Songs[0].Audio);
    Expect.Contains("%E4%B8%BB%E9%A2%98", payload.Songs[0].Audio);
    Expect.Equal("[00:00.00]主题歌词", payload.Songs[0].Lyrics.Original);
    Expect.Equal("#ABCDEF", payload.Songs[0].BackgroundColor);
    var serialized = JsonSerializer.Serialize(payload);
    Expect.False(
        serialized.Contains(directory.Path, StringComparison.OrdinalIgnoreCase),
        "runtime payload exposed a local file path");
});

Run("theme directory refresh ignores ordinary file changes but reacts to set changes", () =>
{
    Expect.False(
        ThemeDirectoryChangePolicy.ShouldSchedule(WatcherChangeTypes.Changed),
        "overwriting an active theme should not auto-reload it");
    Expect.True(
        ThemeDirectoryChangePolicy.ShouldSchedule(WatcherChangeTypes.Created),
        "new theme content should schedule a catalog refresh");
    Expect.True(
        ThemeDirectoryChangePolicy.ShouldSchedule(WatcherChangeTypes.Deleted),
        "deleted theme content should schedule a catalog refresh");
    Expect.True(
        ThemeDirectoryChangePolicy.ShouldSchedule(WatcherChangeTypes.Renamed),
        "renamed theme content should schedule a catalog refresh");
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

Run("a visible normal application window blocks desktop playback", () =>
{
    var context = ForegroundWindow();

    Expect.True(
        ForegroundWindowPolicy.ShouldBlockPlayback(context),
        "a normal foreground application was ignored");
});

Run("foreground detection excludes non-player and unavailable surfaces", () =>
{
    var context = ForegroundWindow();

    Expect.False(ForegroundWindowPolicy.ShouldBlockPlayback(context with { IsOwnProcess = true }), "own process was accepted");
    Expect.False(ForegroundWindowPolicy.ShouldBlockPlayback(context with { IsDesktopSurface = true }), "desktop surface was accepted");
    Expect.False(ForegroundWindowPolicy.ShouldBlockPlayback(context with { IsShellOverlay = true }), "shell overlay was accepted");
    Expect.False(ForegroundWindowPolicy.ShouldBlockPlayback(context with { IsVisible = false }), "hidden window was accepted");
    Expect.False(ForegroundWindowPolicy.ShouldBlockPlayback(context with { IsMinimized = true }), "minimized window was accepted");
    Expect.False(ForegroundWindowPolicy.ShouldBlockPlayback(context with { IsCloaked = true }), "cloaked window was accepted");
    Expect.False(ForegroundWindowPolicy.ShouldBlockPlayback(context with { HasForegroundWindow = false }), "missing foreground window was accepted");
});

Run("only the desktop or player window can start and resume playback", () =>
{
    var context = ForegroundWindow();

    Expect.False(
        ForegroundWindowPolicy.CanStartOrResumePlayback(context),
        "another application was allowed to start playback");
    Expect.True(
        ForegroundWindowPolicy.CanStartOrResumePlayback(context with { IsOwnProcess = true }),
        "the player window could not start playback");
    Expect.True(
        ForegroundWindowPolicy.CanStartOrResumePlayback(context with { IsDesktopSurface = true }),
        "the desktop could not start playback");
    Expect.False(
        ForegroundWindowPolicy.CanStartOrResumePlayback(context with { IsShellOverlay = true }),
        "a taskbar or shell overlay was mistaken for the desktop");
    Expect.False(
        ForegroundWindowPolicy.CanStartOrResumePlayback(context with { HasForegroundWindow = false }),
        "an unknown foreground state was allowed to start playback");
});

Run("desktop shell surfaces never block playback", () =>
{
    foreach (var className in new[]
    {
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "#32768"
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
    Expect.False(
        ForegroundShellSurfacePolicy.IsShellSurface("Windows.UI.Core.CoreWindow"),
        "a generic UWP window class was globally treated as shell UI");
    Expect.False(
        ForegroundShellSurfacePolicy.IsShellSurface("XamlExplorerHostIslandWindow"),
        "a generic XAML host window class was globally treated as shell UI");
    Expect.True(ForegroundShellSurfacePolicy.IsDesktopSurface("Progman"), "Progman was not recognized as desktop");
    Expect.True(ForegroundShellSurfacePolicy.IsDesktopSurface("WorkerW"), "WorkerW was not recognized as desktop");
    Expect.False(
        ForegroundShellSurfacePolicy.IsDesktopSurface("Shell_TrayWnd"),
        "the taskbar was mistaken for the desktop");
    Expect.False(
        ForegroundShellSurfacePolicy.IsDesktopSurface("#32768"),
        "a popup menu was mistaken for the desktop");
});

Run("opening another application requests one automatic pause", () =>
{
    var action = ForegroundPlaybackPolicy.Decide(
        new ForegroundPlaybackState(
            WasBlocked: false,
            IsBlocked: true,
            AutoPauseArmed: false));

    Expect.Equal(ForegroundPlaybackAction.Pause, action);
});

Run("remaining behind another application does not request repeated pauses", () =>
{
    var action = ForegroundPlaybackPolicy.Decide(
        new ForegroundPlaybackState(
            WasBlocked: true,
            IsBlocked: true,
            AutoPauseArmed: true));

    Expect.Equal(ForegroundPlaybackAction.None, action);
});

Run("returning to desktop resumes only an automatically paused session", () =>
{
    var armedAction = ForegroundPlaybackPolicy.Decide(
        new ForegroundPlaybackState(
            WasBlocked: true,
            IsBlocked: false,
            AutoPauseArmed: true,
            CanStartOrResume: true));
    var manualPauseAction = ForegroundPlaybackPolicy.Decide(
        new ForegroundPlaybackState(
            WasBlocked: true,
            IsBlocked: false,
            AutoPauseArmed: false,
            CanStartOrResume: true));

    Expect.Equal(ForegroundPlaybackAction.Resume, armedAction);
    Expect.Equal(ForegroundPlaybackAction.None, manualPauseAction);
});

Run("taskbar focus does not resume an automatically paused session", () =>
{
    var action = ForegroundPlaybackPolicy.Decide(
        new ForegroundPlaybackState(
            WasBlocked: true,
            IsBlocked: false,
            AutoPauseArmed: true,
            CanStartOrResume: false));

    Expect.Equal(ForegroundPlaybackAction.None, action);
});

Run("startup stays silent while another application is in front", () =>
{
    var action = ForegroundPlaybackPolicy.Decide(
        new ForegroundPlaybackState(
            WasBlocked: true,
            IsBlocked: true,
            AutoPauseArmed: false,
            CanStartOrResume: false,
            StartupPending: true));

    Expect.Equal(ForegroundPlaybackAction.None, action);
});

Run("returning to the desktop starts pending autoplay", () =>
{
    var action = ForegroundPlaybackPolicy.Decide(
        new ForegroundPlaybackState(
            WasBlocked: true,
            IsBlocked: false,
            AutoPauseArmed: false,
            CanStartOrResume: true,
            StartupPending: true));

    Expect.Equal(ForegroundPlaybackAction.Start, action);
});

Run("taskbar focus does not release pending autoplay", () =>
{
    var action = ForegroundPlaybackPolicy.Decide(
        new ForegroundPlaybackState(
            WasBlocked: true,
            IsBlocked: false,
            AutoPauseArmed: false,
            CanStartOrResume: false,
            StartupPending: true));

    Expect.Equal(ForegroundPlaybackAction.None, action);
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

static ForegroundWindowContext ForegroundWindow() =>
    new(
        HasForegroundWindow: true,
        IsOwnProcess: false,
        IsDesktopSurface: false,
        IsShellOverlay: false,
        IsVisible: true,
        IsMinimized: false,
        IsCloaked: false);

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

    public string CreateThemeFixture(
        string relativeFolder,
        string id,
        string name,
        string backgroundColor = "#112233",
        string textColor = "#FFFFFF",
        string accentColor = "#88AACC")
    {
        var root = System.IO.Path.Combine(
            Path,
            relativeFolder.Replace('/', System.IO.Path.DirectorySeparatorChar));
        Directory.CreateDirectory(System.IO.Path.Combine(root, "audio"));
        Directory.CreateDirectory(System.IO.Path.Combine(root, "images", "covers"));
        Directory.CreateDirectory(System.IO.Path.Combine(root, "lyrics"));
        File.WriteAllText(System.IO.Path.Combine(root, "audio", "01.mp3"), "audio");
        File.WriteAllText(System.IO.Path.Combine(root, "images", "covers", "01.png"), "image");
        File.WriteAllText(System.IO.Path.Combine(root, "lyrics", "01.lrc"), "[00:00.00]test");
        File.WriteAllText(
            System.IO.Path.Combine(root, "pack.json"),
            JsonSerializer.Serialize(new
            {
                format = 1,
                id,
                name,
                author = "测试作者",
                appearance = new
                {
                    accentColor,
                    textColor
                }
            }));
        ReplaceThemeSongs(
            root,
            [ThemeSong(backgroundColor: backgroundColor, textColor: textColor, accentColor: accentColor)]);
        return root;
    }

    public object ThemeSong(
        string title = "测试歌曲",
        string audio = "audio/01.mp3",
        string cover = "images/covers/01.png",
        object? lyrics = null,
        string backgroundColor = "#112233",
        string textColor = "#FFFFFF",
        string accentColor = "#88AACC") =>
        new
        {
            title,
            artist = "测试歌手",
            audio,
            cover,
            backgroundColor,
            textColor,
            accentColor,
            lyrics = lyrics ?? new { original = "lyrics/01.lrc" }
        };

    public void ReplaceThemeSongs(string themeRoot, IReadOnlyList<object> songs)
    {
        File.WriteAllText(
            System.IO.Path.Combine(themeRoot, "songs.json"),
            JsonSerializer.Serialize(songs));
    }

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
