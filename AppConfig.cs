using System.Text.Json.Serialization;

namespace ImmichUploader;

public sealed class AppConfig
{
    public string Server { get; set; } = "http://192.168.1.119:2283";
    public string ApiKey { get; set; } = "";
    public List<string> Folders { get; set; } = new();
    public int Concurrency { get; set; } = 4;
    public int DaysBack { get; set; } = 7;
    public string LogDir { get; set; } = "";
    public string ImmichGoPath { get; set; } = "";
    public bool LaunchOnWindowsStartup { get; set; } = false;
    public bool StartMinimizedToTray { get; set; } = true;

    public DayOfWeek WeeklyDay { get; set; } = DayOfWeek.Sunday;
    public int WeeklyHour { get; set; } = 3;
    public int WeeklyMinute { get; set; } = 0;

    public int MonthlyRescanDay { get; set; } = 1;
    public int MonthlyRescanHour { get; set; } = 4;
    public int MonthlyRescanMinute { get; set; } = 0;

    [JsonIgnore]
    public string DefaultImmichGoPath
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(ImmichGoPath)) return ImmichGoPath;
            var exeDir = AppContext.BaseDirectory;
            var candidate = Path.Combine(exeDir, "immich-go.exe");
            return File.Exists(candidate) ? candidate : "immich-go";
        }
    }

    [JsonIgnore]
    public string DefaultLogDir
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(LogDir)) return LogDir;
            return Path.Combine(AppContext.BaseDirectory, "Logs");
        }
    }

    public string ResolveConfigPath()
    {
        return Path.Combine(AppContext.BaseDirectory, "config.json");
    }
}
