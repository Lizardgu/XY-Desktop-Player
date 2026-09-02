namespace XYDesktopPlayer.Core;

public static class ThemeAssetPath
{
    public static readonly IReadOnlySet<string> AudioExtensions =
        new HashSet<string>([".mp3", ".m4a", ".flac", ".wav", ".ogg"], StringComparer.OrdinalIgnoreCase);

    public static readonly IReadOnlySet<string> ImageExtensions =
        new HashSet<string>([".png", ".jpg", ".jpeg", ".webp", ".gif"], StringComparer.OrdinalIgnoreCase);

    public static readonly IReadOnlySet<string> LyricExtensions =
        new HashSet<string>([".lrc"], StringComparer.OrdinalIgnoreCase);

    public static readonly IReadOnlySet<string> FontExtensions =
        new HashSet<string>([".woff2", ".ttf", ".otf"], StringComparer.OrdinalIgnoreCase);

    public static bool TryResolveExistingFile(
        string assetRoot,
        string? candidate,
        IReadOnlySet<string> allowedExtensions,
        out string normalizedRelativePath,
        out string resolvedPath,
        out string? error)
    {
        normalizedRelativePath = string.Empty;
        resolvedPath = string.Empty;
        error = null;

        if (string.IsNullOrWhiteSpace(candidate))
        {
            error = "素材路径为空";
            return false;
        }

        var trimmed = candidate.Trim();
        if (trimmed.Contains("://", StringComparison.Ordinal) ||
            Path.IsPathRooted(trimmed))
        {
            error = $"素材路径必须位于主题文件夹内：{candidate}";
            return false;
        }

        var extension = Path.GetExtension(trimmed);
        if (string.IsNullOrEmpty(extension) || !allowedExtensions.Contains(extension))
        {
            error = $"不支持的素材类型：{candidate}";
            return false;
        }

        try
        {
            var root = Path.GetFullPath(assetRoot);
            var boundary = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                           Path.DirectorySeparatorChar;
            var platformRelative = trimmed
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);
            var resolved = Path.GetFullPath(Path.Combine(root, platformRelative));
            if (!resolved.StartsWith(boundary, StringComparison.OrdinalIgnoreCase))
            {
                error = $"素材路径越过主题文件夹：{candidate}";
                return false;
            }

            if (!File.Exists(resolved))
            {
                error = $"素材文件不存在：{candidate}";
                return false;
            }

            if (ContainsReparsePoint(root, resolved))
            {
                error = $"素材路径不能经过目录联接或符号链接：{candidate}";
                return false;
            }

            normalizedRelativePath = Path.GetRelativePath(root, resolved).Replace('\\', '/');
            resolvedPath = resolved;
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = $"素材路径无效：{candidate}";
            return false;
        }
    }

    private static bool ContainsReparsePoint(string root, string resolvedFile)
    {
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
        {
            return true;
        }

        var relative = Path.GetRelativePath(root, resolvedFile);
        var current = root;
        foreach (var segment in relative.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                return true;
            }
        }

        return false;
    }
}
