namespace XYDesktopPlayer.Core;

public sealed record ContentValidationResult(
    bool IsValid,
    string? ResolvedContentRoot,
    IReadOnlyList<string> MissingEntries,
    string? Error);

public static class ContentRootValidator
{
    public static ContentValidationResult Validate(string contentRoot)
    {
        var resolvedRoot = Path.GetFullPath(contentRoot);
        if (!Directory.Exists(resolvedRoot))
        {
            return new ContentValidationResult(
                false,
                null,
                [],
                $"播放器内容目录不存在：{resolvedRoot}");
        }

        var missingEntries = new List<string>();
        if (!File.Exists(Path.Combine(resolvedRoot, "index.html")))
        {
            missingEntries.Add("index.html");
        }

        foreach (var relativeDirectory in new[]
                 {
                     "static",
                     "assets/covers",
                     "assets/audios",
                     "assets/lyrics"
                 })
        {
            if (!Directory.Exists(Path.Combine(resolvedRoot, relativeDirectory)))
            {
                missingEntries.Add(relativeDirectory);
            }
        }

        if (missingEntries.Count > 0)
        {
            return new ContentValidationResult(
                false,
                resolvedRoot,
                missingEntries,
                $"播放器内容不完整，缺少：{string.Join(", ", missingEntries)}");
        }

        return new ContentValidationResult(true, resolvedRoot, [], null);
    }
}
