using System.Text;
using System.Text.Json;

namespace XYDesktopPlayer.Core;

public static class PlayerSettingsStore
{
    private const long MaximumSettingsBytes = 64 * 1024;
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static PlayerSettingsLoadResult Load(string path)
    {
        try
        {
            var resolvedPath = Path.GetFullPath(path);
            if (!File.Exists(resolvedPath))
            {
                return new PlayerSettingsLoadResult(PlayerSettings.Default, null);
            }

            if (new FileInfo(resolvedPath).Length > MaximumSettingsBytes)
            {
                return Fallback("设置文件过大，已使用默认设置。");
            }

            var settings = JsonSerializer.Deserialize<PlayerSettings>(
                File.ReadAllText(resolvedPath),
                ReadOptions);
            if (settings is null)
            {
                return Fallback("设置文件为空，已使用默认设置。");
            }

            var selectedThemeId = string.IsNullOrWhiteSpace(settings.SelectedThemeId)
                ? null
                : settings.SelectedThemeId.Trim();
            return new PlayerSettingsLoadResult(new PlayerSettings(selectedThemeId), null);
        }
        catch (Exception exception) when (
            exception is JsonException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            return Fallback($"设置文件无法读取，已使用默认设置：{exception.Message}");
        }
    }

    public static PlayerSettingsSaveResult Save(string path, PlayerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string? temporaryPath = null;
        try
        {
            var resolvedPath = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(resolvedPath);
            if (string.IsNullOrEmpty(directory))
            {
                return new PlayerSettingsSaveResult(false, "设置文件缺少有效目录。");
            }

            Directory.CreateDirectory(directory);
            temporaryPath = Path.Combine(
                directory,
                $".{Path.GetFileName(resolvedPath)}.{Guid.NewGuid():N}.tmp");
            var normalized = new PlayerSettings(
                string.IsNullOrWhiteSpace(settings.SelectedThemeId)
                    ? null
                    : settings.SelectedThemeId.Trim());
            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(normalized, WriteOptions),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            if (File.Exists(resolvedPath))
            {
                File.Replace(temporaryPath, resolvedPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporaryPath, resolvedPath);
            }

            temporaryPath = null;
            return new PlayerSettingsSaveResult(true, null);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return new PlayerSettingsSaveResult(false, $"设置保存失败：{exception.Message}");
        }
        finally
        {
            if (temporaryPath is not null && File.Exists(temporaryPath))
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch
                {
                    // A failed cleanup must not hide the original save result.
                }
            }
        }
    }

    private static PlayerSettingsLoadResult Fallback(string warning) =>
        new(PlayerSettings.Default, warning);
}
