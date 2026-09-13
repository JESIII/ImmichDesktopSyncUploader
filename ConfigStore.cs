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
            defaults.Normalize();
            Save(defaults);
            return defaults;
        }
        try
        {
            var json = File.ReadAllText(path);
            var cfg = JsonSerializer.Deserialize<AppConfig>(json, Options) ?? new AppConfig();
            cfg.Normalize();
            return cfg;
        }
        catch
        {
            var backup = path + ".corrupt-" + DateTime.Now.ToString("yyyyMMddHHmmss");
            try { File.Move(path, backup); } catch { }
            var defaults = new AppConfig();
            defaults.Normalize();
            Save(defaults);
            return defaults;
        }
    }

    public static void Save(AppConfig cfg)
    {
        cfg.Normalize();
        var path = cfg.ResolveConfigPath();
        var json = JsonSerializer.Serialize(cfg, Options);
        File.WriteAllText(path, json);
    }
}
