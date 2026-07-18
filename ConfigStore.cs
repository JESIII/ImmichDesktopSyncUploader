using System.Text.Json;

namespace ImmichUploader;

public static class ConfigStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public static AppConfig Load()
    {
        var path = new AppConfig().ResolveConfigPath();
        if (!File.Exists(path))
        {
            var defaults = new AppConfig();
            Save(defaults);
            return defaults;
        }
        try
        {
            var json = File.ReadAllText(path);
            var cfg = JsonSerializer.Deserialize<AppConfig>(json, Options) ?? new AppConfig();
            if (cfg.Folders == null) cfg.Folders = new List<string>();
            return cfg;
        }
        catch
        {
            var backup = path + ".corrupt-" + DateTime.Now.ToString("yyyyMMddHHmmss");
            try { File.Move(path, backup); } catch { }
            var defaults = new AppConfig();
            Save(defaults);
            return defaults;
        }
    }

    public static void Save(AppConfig cfg)
    {
        var path = cfg.ResolveConfigPath();
        var json = JsonSerializer.Serialize(cfg, Options);
        File.WriteAllText(path, json);
    }
}
