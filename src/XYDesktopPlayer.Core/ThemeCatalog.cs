namespace XYDesktopPlayer.Core;

public static class ThemeCatalog
{
    public static ThemeCatalogResult Scan(ThemePack builtInTheme, string userThemesRoot)
    {
        ArgumentNullException.ThrowIfNull(builtInTheme);
        if (!builtInTheme.IsBuiltIn)
        {
            throw new ArgumentException("内置主题必须标记为 IsBuiltIn。", nameof(builtInTheme));
        }

        var diagnostics = new List<ThemeCatalogDiagnostic>();
        var candidates = new List<(ThemePack Theme, string Folder)>();
        if (Directory.Exists(userThemesRoot))
        {
            string[] directories;
            try
            {
                directories = Directory.GetDirectories(userThemesRoot);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                diagnostics.Add(new ThemeCatalogDiagnostic(
                    userThemesRoot,
                    $"无法扫描主题目录：{exception.Message}"));
                return new ThemeCatalogResult([builtInTheme], diagnostics);
            }

            foreach (var directory in directories.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                var result = ThemePackLoader.Load(directory, directory, isBuiltIn: false);
                if (!result.IsValid || result.Theme is null)
                {
                    diagnostics.Add(new ThemeCatalogDiagnostic(directory, result.Error ?? "主题无效"));
                    continue;
                }

                if (string.Equals(result.Theme.Id, builtInTheme.Id, StringComparison.Ordinal))
                {
                    diagnostics.Add(new ThemeCatalogDiagnostic(directory, "主题 id 与内置主题保留 id 冲突"));
                    continue;
                }

                candidates.Add((result.Theme, directory));
                diagnostics.AddRange(result.Warnings.Select(warning =>
                    new ThemeCatalogDiagnostic(directory, warning)));
            }
        }

        var accepted = new List<ThemePack> { builtInTheme };
        foreach (var group in candidates.GroupBy(candidate => candidate.Theme.Id, StringComparer.Ordinal))
        {
            var duplicateGroup = group.ToArray();
            if (duplicateGroup.Length > 1)
            {
                diagnostics.AddRange(duplicateGroup.Select(candidate =>
                    new ThemeCatalogDiagnostic(candidate.Folder, $"主题 id 重复：{candidate.Theme.Id}")));
                continue;
            }

            accepted.Add(duplicateGroup[0].Theme);
        }

        accepted = accepted
            .Take(1)
            .Concat(accepted.Skip(1).OrderBy(theme => theme.Name, StringComparer.CurrentCultureIgnoreCase))
            .ToList();
        return new ThemeCatalogResult(accepted, diagnostics);
    }
}
