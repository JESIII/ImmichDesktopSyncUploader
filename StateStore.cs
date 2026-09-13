using System.Text.Json;

namespace ImmichUploader;

public sealed class UploadState
{
    public Dictionary<string, DateTime> LastSuccessByFolder { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTime? LastFullRescan { get; set; }
    public DateTime? LastRunStarted { get; set; }
    public DateTime? LastRunFinished { get; set; }
    public string? LastRunResult { get; set; }

    /// <summary>Most recent weekly schedule occurrence already handled (local time).</summary>
    public DateTime? LastWeeklyHandled { get; set; }

    /// <summary>Most recent monthly schedule occurrence already handled (local time).</summary>
    public DateTime? LastMonthlyHandled { get; set; }

    /// <summary>Most recent run trigger (manual, watcher, weekly, monthly-rescan).</summary>
    public string? LastRunTrigger { get; set; }
}

public static class StateStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true
    };

    public static string ResolvePath()
    {
        return Path.Combine(AppContext.BaseDirectory, "state.json");
    }

    public static UploadState Load()
    {
        var path = ResolvePath();
        if (!File.Exists(path)) return new UploadState();
        try
        {
            var json = File.ReadAllText(path);
            var state = JsonSerializer.Deserialize<UploadState>(json, Options) ?? new UploadState();
            if (state.LastSuccessByFolder is null)
            {
                state.LastSuccessByFolder = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            }
            return state;
        }
        catch
        {
            return new UploadState();
        }
    }

    public static void Save(UploadState state)
    {
        var path = ResolvePath();
        var json = JsonSerializer.Serialize(state, Options);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, json);
        // Atomic-ish replace so a crash mid-write cannot corrupt state.json.
        try { File.Move(tmp, path, overwrite: true); }
        catch { if (File.Exists(path)) File.Delete(path); File.Move(tmp, path); }
    }
}
