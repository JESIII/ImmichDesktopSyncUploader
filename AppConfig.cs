using System.Text.Json.Serialization;

namespace ImmichUploader;

/// <summary>
/// Persisted application configuration.
///
/// Schema version history:
///   1 - original schema (Server, ApiKey, Folders, Concurrency, DaysBack, ...)
///   2 - added AdminApiKey, curated advanced upload settings, separate
///       process-vs-task concurrency, schedule toggles, and watcher settings.
///   3 - added the full immich-go upload-from-folder flag surface (album,
///       HEIC/JPEG, sidecars, time zone, include/exclude filters, log level, ...).
///
/// Older files are upgraded in place by <see cref="Normalize"/> (called from
/// <see cref="ConfigStore.Load"/>), which fills missing values with defaults
/// and clamps out-of-range values so an upgraded config can never crash a run.
/// </summary>
public sealed class AppConfig
{
    /// <summary>Current on-disk schema version. Bumped when fields are added.</summary>
    public const int CurrentConfigVersion = 3;

    public int ConfigVersion { get; set; } = CurrentConfigVersion;

    // ── Connection ─────────────────────────────────────────────
    public string Server { get; set; } = "http://192.168.1.119:2283";

    /// <summary>Standard Immich API key used for uploading assets.</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>
    /// Optional Immich admin API key. Required only to pause/resume Immich
    /// background jobs during an upload. Never written to logs or argv; it is
    /// passed to immich-go through an environment variable.
    /// </summary>
    public string AdminApiKey { get; set; } = "";

    // ── Sources ────────────────────────────────────────────────
    public List<string> Folders { get; set; } = new();

    // ── Concurrency (two independent knobs) ────────────────────
    /// <summary>immich-go <c>--concurrent-tasks</c> for a single process (1-20).</summary>
    public int Concurrency { get; set; } = 4;

    /// <summary>How many immich-go processes may run at the same time.</summary>
    public int MaxParallelProcesses { get; set; } = 3;

    // ── Incremental scan window ────────────────────────────────
    public int DaysBack { get; set; } = 7;

    // ── immich-go: simple options ──────────────────────────────
    /// <summary><c>--folder-as-album</c>: NONE, FOLDER, PATH.</summary>
    public string FolderAsAlbum { get; set; } = "NONE";

    /// <summary><c>--into-album</c>: put every uploaded asset into this album.</summary>
    public string IntoAlbum { get; set; } = "";

    /// <summary><c>--manage-burst</c>: NoStack, Stack, StackKeepRaw, StackKeepJPEG.</summary>
    public string ManageBurst { get; set; } = "Stack";

    /// <summary><c>--manage-raw-jpeg</c>: NoStack, KeepRaw, KeepJPG, StackCoverRaw, StackCoverJPG.</summary>
    public string ManageRawJpeg { get; set; } = "StackCoverRaw";

    /// <summary><c>--manage-heic-jpeg</c>: NoStack, KeepHeic, KeepJPG, StackCoverHeic, StackCoverJPG.</summary>
    public string ManageHeicJpeg { get; set; } = "NoStack";

    // ── immich-go: advanced options ────────────────────────────
    /// <summary><c>--recursive</c> (immich-go default true). Emits <c>--recursive=false</c> when off.</summary>
    public bool Recursive { get; set; } = true;

    /// <summary><c>--date-from-name</c> (immich-go default true). Emits false when off.</summary>
    public bool DateFromName { get; set; } = true;

    /// <summary><c>--ignore-sidecar-files</c>.</summary>
    public bool IgnoreSidecarFiles { get; set; } = false;

    /// <summary><c>--manage-epson-fastfoto</c>.</summary>
    public bool ManageEpsonFastFoto { get; set; } = false;

    /// <summary><c>--overwrite</c> (replaces existing server assets).</summary>
    public bool Overwrite { get; set; } = false;

    /// <summary><c>--folder-as-tags</c>: derive tags from folder names.</summary>
    public bool FolderAsTags { get; set; } = false;

    /// <summary><c>--session-tag</c>: tag uploaded assets per session.</summary>
    public bool SessionTag { get; set; } = true;

    /// <summary><c>--api-trace</c>: verbose API call tracing.</summary>
    public bool ApiTrace { get; set; } = false;

    /// <summary>
    /// <c>--dry-run</c>: simulate the upload without writing to the server.
    /// Useful for verifying configuration; no assets are uploaded.
    /// </summary>
    public bool DryRun { get; set; } = false;

    /// <summary>Emit <c>--skip-verify-ssl</c> (only for trusted/self-hosted servers).</summary>
    public bool SkipSslVerify { get; set; } = false;

    /// <summary>
    /// Opt-in: pause Immich background jobs during upload. Requires
    /// <see cref="AdminApiKey"/>; the argument builder forces this off (and
    /// emits a warning) when no admin key is configured.
    /// </summary>
    public bool PauseImmichJobs { get; set; } = false;

    /// <summary><c>--device-uuid</c>.</summary>
    public string DeviceUuid { get; set; } = "";

    /// <summary><c>--time-zone</c> override (e.g. UTC or America/New_York).</summary>
    public string TimeZone { get; set; } = "";

    /// <summary><c>--album-path-joiner</c> (e.g. " / ").</summary>
    public string AlbumPathJoiner { get; set; } = "";

    /// <summary><c>--on-errors</c>: "stop", "continue", or a non-negative integer.</summary>
    public string OnErrors { get; set; } = "continue";

    /// <summary><c>--client-timeout</c> in minutes.</summary>
    public int ClientTimeoutMinutes { get; set; } = 60;

    /// <summary><c>--include-extensions</c> (csv, e.g. .jpg,.heic,.mp4).</summary>
    public string IncludeExtensions { get; set; } = "";

    /// <summary><c>--exclude-extensions</c> (csv, e.g. .thm,.xmp).</summary>
    public string ExcludeExtensions { get; set; } = "";

    /// <summary><c>--ban-file</c> patterns, one per line.</summary>
    public string BanFiles { get; set; } = "";

    /// <summary><c>--tag</c> values (csv, repeated per entry).</summary>
    public string Tags { get; set; } = "";

    /// <summary><c>--include-type</c>: all, IMAGE, VIDEO.</summary>
    public string IncludeType { get; set; } = "all";

    /// <summary><c>--log-level</c>: INFO, DEBUG, WARN, ERROR. Blank = immich-go default.</summary>
    public string LogLevel { get; set; } = "";

    // ── Startup / logging ──────────────────────────────────────
    public string LogDir { get; set; } = "";
    public string ImmichGoPath { get; set; } = "";
    public bool LaunchOnWindowsStartup { get; set; } = false;
    public bool StartMinimizedToTray { get; set; } = true;

    // ── Scheduling ─────────────────────────────────────────────
    public bool WeeklyEnabled { get; set; } = true;
    public DayOfWeek WeeklyDay { get; set; } = DayOfWeek.Sunday;
    public int WeeklyHour { get; set; } = 3;
    public int WeeklyMinute { get; set; } = 0;

    public bool MonthlyEnabled { get; set; } = true;
    public int MonthlyRescanDay { get; set; } = 1;
    public int MonthlyRescanHour { get; set; } = 4;
    public int MonthlyRescanMinute { get; set; } = 0;

    // ── Real-time file watcher ─────────────────────────────────
    /// <summary>Own real-time watching of <see cref="Folders"/> in the tray app.</summary>
    public bool WatcherEnabled { get; set; } = false;

    /// <summary>Fixed debounce window (seconds) that batches filesystem events.</summary>
    public int WatcherDebounceSeconds { get; set; } = 30;

    // ── Choice lists (mirror immich-go's accepted values) ──────
    public static readonly string[] AlbumChoices = { "NONE", "FOLDER", "PATH" };
    public static readonly string[] BurstChoices = { "NoStack", "Stack", "StackKeepRaw", "StackKeepJPEG" };
    public static readonly string[] RawJpegChoices = { "NoStack", "KeepRaw", "KeepJPG", "StackCoverRaw", "StackCoverJPG" };
    public static readonly string[] HeicJpegChoices = { "NoStack", "KeepHeic", "KeepJPG", "StackCoverHeic", "StackCoverJPG" };
    public static readonly string[] IncludeTypeChoices = { "all", "IMAGE", "VIDEO" };
    public static readonly string[] LogLevelChoices = { "INFO", "DEBUG", "WARN", "ERROR" };

    // ── Derived helpers ────────────────────────────────────────

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

    /// <summary>True when a usable admin key is available for job pausing.</summary>
    [JsonIgnore]
    public bool HasAdminApiKey => !string.IsNullOrWhiteSpace(AdminApiKey);

    public string ResolveConfigPath()
    {
        return Path.Combine(AppContext.BaseDirectory, "config.json");
    }

    /// <summary>Deep copy (JSON round-trip) for edit-then-apply UI flows.</summary>
    public AppConfig Clone()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(this);
        return System.Text.Json.JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
    }

    /// <summary>
    /// Clamp and default all values so a migrated or hand-edited config is
    /// always internally consistent. Safe to call repeatedly.
    /// </summary>
    public void Normalize()
    {
        Folders ??= new List<string>();
        Folders = Folders
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => f.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // immich-go documents --concurrent-tasks as 1-20.
        Concurrency = Math.Clamp(Concurrency, 1, 20);
        MaxParallelProcesses = Math.Clamp(MaxParallelProcesses, 1, 32);
        DaysBack = Math.Clamp(DaysBack, 1, 3650);
        ClientTimeoutMinutes = Math.Clamp(ClientTimeoutMinutes, 1, 24 * 60);
        WatcherDebounceSeconds = Math.Clamp(WatcherDebounceSeconds, 1, 3600);

        WeeklyHour = Math.Clamp(WeeklyHour, 0, 23);
        WeeklyMinute = Math.Clamp(WeeklyMinute, 0, 59);
        MonthlyRescanDay = Math.Clamp(MonthlyRescanDay, 1, 28);
        MonthlyRescanHour = Math.Clamp(MonthlyRescanHour, 0, 23);
        MonthlyRescanMinute = Math.Clamp(MonthlyRescanMinute, 0, 59);

        OnErrors = NormalizeOnErrors(OnErrors);
        FolderAsAlbum = NormalizeChoice(FolderAsAlbum, AlbumChoices, "NONE");
        ManageBurst = NormalizeChoice(ManageBurst, BurstChoices, "Stack");
        ManageRawJpeg = NormalizeChoice(ManageRawJpeg, RawJpegChoices, "StackCoverRaw");
        ManageHeicJpeg = NormalizeChoice(ManageHeicJpeg, HeicJpegChoices, "NoStack");
        IncludeType = NormalizeChoice(IncludeType, IncludeTypeChoices, "all");
        LogLevel = NormalizeLogLevel(LogLevel);

        IntoAlbum = Trim(IntoAlbum);
        DeviceUuid = Trim(DeviceUuid);
        TimeZone = Trim(TimeZone);
        AlbumPathJoiner = Trim(AlbumPathJoiner);
        IncludeExtensions = NormalizeCsv(IncludeExtensions);
        ExcludeExtensions = NormalizeCsv(ExcludeExtensions);
        Tags = NormalizeCsv(Tags);
        BanFiles = NormalizeLines(BanFiles);

        if (ConfigVersion < CurrentConfigVersion) ConfigVersion = CurrentConfigVersion;
    }

    private static string Trim(string? value) => (value ?? "").Trim();

    /// <summary>Validate/normalize an <c>--on-errors</c> value; falls back to "continue".</summary>
    public static string NormalizeOnErrors(string? value)
    {
        var v = (value ?? "").Trim();
        if (v.Equals("stop", StringComparison.OrdinalIgnoreCase)) return "stop";
        if (v.Equals("continue", StringComparison.OrdinalIgnoreCase)) return "continue";
        if (int.TryParse(v, out var n) && n >= 0) return n.ToString();
        return "continue";
    }

    /// <summary>Blank stays blank (immich-go default); otherwise an upper-case choice.</summary>
    public static string NormalizeLogLevel(string? value)
    {
        var v = (value ?? "").Trim();
        if (v.Length == 0) return "";
        var match = LogLevelChoices.FirstOrDefault(c => c.Equals(v, StringComparison.OrdinalIgnoreCase));
        return match ?? "";
    }

    /// <summary>Trim entries, drop blanks, and rejoin as a comma-separated list.</summary>
    public static string NormalizeCsv(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        return string.Join(",", value
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => x.Length > 0));
    }

    /// <summary>Trim lines, drop blanks, and rejoin.</summary>
    public static string NormalizeLines(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        return string.Join("\n", value
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => x.Length > 0));
    }

    private static string NormalizeChoice(string? value, string[] allowed, string fallback)
    {
        var v = (value ?? "").Trim();
        var match = allowed.FirstOrDefault(a => a.Equals(v, StringComparison.OrdinalIgnoreCase));
        return match ?? fallback;
    }
}
