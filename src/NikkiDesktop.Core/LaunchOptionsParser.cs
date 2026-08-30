namespace NikkiDesktop.Core;

public static class LaunchOptionsParser
{
    public static LaunchOptionsParseResult Parse(IReadOnlyList<string> arguments)
    {
        var mode = HostMode.Window;
        string? contentRoot = null;
        string? capturePath = null;
        var selfTest = false;

        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            switch (argument)
            {
                case "--self-test":
                    selfTest = true;
                    break;

                case "--mode":
                    if (!TryReadValue(arguments, ref index, out var modeValue))
                    {
                        return Failure("--mode 后必须填写 window 或 wallpaper。");
                    }

                    if (modeValue.Equals("window", StringComparison.OrdinalIgnoreCase))
                    {
                        mode = HostMode.Window;
                    }
                    else if (modeValue.Equals("wallpaper", StringComparison.OrdinalIgnoreCase))
                    {
                        mode = HostMode.Wallpaper;
                    }
                    else
                    {
                        return Failure("--mode 只接受 window 或 wallpaper。");
                    }

                    break;

                case "--content":
                    if (!TryReadValue(arguments, ref index, out var contentValue))
                    {
                        return Failure("--content 后必须填写播放器内容目录。");
                    }

                    contentRoot = contentValue;
                    break;

                case "--capture":
                    if (!TryReadValue(arguments, ref index, out var captureValue))
                    {
                        return Failure("--capture 后必须填写 PNG 输出路径。");
                    }

                    capturePath = captureValue;
                    break;

                default:
                    return Failure($"未知参数：{argument}");
            }
        }

        return new LaunchOptionsParseResult(
            true,
            new LaunchOptions(mode, contentRoot, selfTest, capturePath),
            null);
    }

    private static bool TryReadValue(
        IReadOnlyList<string> arguments,
        ref int index,
        out string value)
    {
        if (index + 1 >= arguments.Count || arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            value = string.Empty;
            return false;
        }

        value = arguments[++index];
        return true;
    }

    private static LaunchOptionsParseResult Failure(string error) =>
        new(false, null, error);
}
