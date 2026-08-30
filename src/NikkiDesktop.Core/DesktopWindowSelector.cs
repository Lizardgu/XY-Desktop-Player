namespace NikkiDesktop.Core;

public sealed record DesktopTopLevelWindow(
    nint Handle,
    string ClassName,
    bool ContainsShellDefView);

public static class DesktopWindowSelector
{
    public static nint? SelectWorkerW(IReadOnlyList<DesktopTopLevelWindow> windows)
    {
        for (var index = 0; index < windows.Count; index++)
        {
            if (!windows[index].ContainsShellDefView)
            {
                continue;
            }

            for (var candidateIndex = index + 1; candidateIndex < windows.Count; candidateIndex++)
            {
                var candidate = windows[candidateIndex];
                if (candidate.Handle != nint.Zero &&
                    candidate.ClassName.Equals("WorkerW", StringComparison.Ordinal))
                {
                    return candidate.Handle;
                }
            }
        }

        return null;
    }
}
