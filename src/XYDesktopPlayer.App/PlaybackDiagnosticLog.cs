using System.Text.Json;
using System.Text.Json.Serialization;

namespace XYDesktopPlayer.App;

/// <summary>
/// One sampled state of the playback environment. Recorded only when the signature
/// changes, so a quiet desktop does not flood the log but every real transition is kept.
/// </summary>
internal sealed record DiagnosticSample(
    DateTimeOffset Timestamp,
    string Trigger,
    bool IsBlocked,
    string Action,
    ForegroundInfo Foreground,
    int BlockingWindowCount,
    IReadOnlyList<BlockingWindowInfo> BlockingWindows,
    bool WasBlocked,
    bool AutoPauseArmed,
    bool StartupPending,
    string? DetectorError)
{
    public string Signature =>
        $"{IsBlocked}|{Action}|{Foreground.ClassName}|{BlockingWindowCount}|{TopHandle}";

    private nint TopHandle =>
        BlockingWindows.Count > 0 ? BlockingWindows[0].Handle : nint.Zero;
}

/// <summary>
/// Ring-buffer of diagnostic samples. Kept small and deduplicated so it is cheap to keep
/// running all the time and safe to export on demand.
/// </summary>
internal sealed class PlaybackDiagnosticLog
{
    private readonly object _gate = new();
    private readonly List<DiagnosticSample> _samples = new();
    private readonly int _capacity;
    private string? _lastSignature;

    public PlaybackDiagnosticLog(int capacity = 240)
    {
        _capacity = Math.Max(10, capacity);
    }

    public void Record(DiagnosticSample sample)
    {
        lock (_gate)
        {
            if (sample.Signature == _lastSignature)
            {
                return;
            }

            _lastSignature = sample.Signature;
            _samples.Add(sample);
            if (_samples.Count > _capacity)
            {
                _samples.RemoveAt(0);
            }
        }
    }

    public IReadOnlyList<DiagnosticSample> Snapshot()
    {
        lock (_gate)
        {
            return _samples.ToList();
        }
    }

    public string Export(string directory)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(
            directory,
            $"WindowDetection-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        var snapshot = Snapshot();
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(snapshot, options));
        return path;
    }
}
