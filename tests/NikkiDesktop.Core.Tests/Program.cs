using NikkiDesktop.Core;

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
    var missingPath = Path.Combine(Path.GetTempPath(), "NikkiDesktopTests", Guid.NewGuid().ToString("N"));

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
    Expect.Equal("nikkidesktop.local", mapping.StartUri.Host);
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

Console.WriteLine(failures == 0
    ? "All core tests passed."
    : $"{failures} core test(s) failed.");

return failures == 0 ? 0 : 1;

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
            "NikkiDesktopTests",
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
