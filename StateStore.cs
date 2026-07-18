using System.Text.Json;

namespace ImmichUploader;

public sealed class UploadState
{
    public Dictionary<string, DateTime> LastSuccessByFolder { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTime? LastFullRescan { get; set; }
    public DateTime? LastRunStarted { get; set; }
    public DateTime? LastRunFinished { get; set; }
    public string? LastRunResult { get; set; }
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
            return JsonSerializer.Deserialize<UploadState>(json, Options) ?? new UploadState();
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
        File.WriteAllText(path, json);
    }
}
