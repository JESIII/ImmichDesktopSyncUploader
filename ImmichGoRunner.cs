using System.Diagnostics;
using System.Text;

namespace ImmichUploader;

/// <summary>
/// Fully-resolved immich-go invocation: the argv, the environment additions,
/// and any safety warnings. Produced by <see cref="ImmichGoRunner.BuildInvocation"/>.
/// </summary>
public sealed class ImmichGoInvocation
{
    public IReadOnlyList<string> Arguments { get; init; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    /// <summary>Argv rendered for logs. Never contains secrets (they live in <see cref="Environment"/>).</summary>
    public string DisplayCommand { get; init; } = "";
}

/// <summary>
/// Centralized immich-go process runner.
///
/// All argument construction lives in <see cref="BuildInvocation"/> so flags are
/// emitted in exactly one place. Curated typed settings (never raw command text)
/// drive the argv. Secrets (API keys) are passed through environment variables
/// and are never added to argv or the logged command line.
/// </summary>
public sealed class ImmichGoRunner : IUploadRunner
{
    /// <summary>immich-go env var honoured for the upload API key.</summary>
    public const string ApiKeyEnvVar = "IMMICH_GO_UPLOAD_API_KEY";

    /// <summary>immich-go env var honoured for the admin API key (job pausing).</summary>
    public const string AdminApiKeyEnvVar = "IMMICH_GO_UPLOAD_ADMIN_API_KEY";

    /// <summary>
    /// Build the immich-go invocation for one folder.
    ///
    /// Pausing Immich background jobs is opt-in and only emitted when an admin
    /// API key is present; otherwise <c>--pause-immich-jobs=false</c> is forced
    /// and a warning is returned (mirrors the GUI's pause-safety behavior).
    /// </summary>
    public static ImmichGoInvocation BuildInvocation(
        AppConfig cfg,
        string sourceFolder,
        DateTime sinceUtc,
        DateTime? nowUtc = null)
    {
        var warnings = new List<string>();
        var args = new List<string> { "upload", "from-folder", "--no-ui" };

        if (!string.IsNullOrWhiteSpace(cfg.Server)) args.Add($"--server={cfg.Server.Trim()}");

        // --recursive defaults to true upstream; emit an explicit false when off.
        args.Add(cfg.Recursive ? "--recursive" : "--recursive=false");

        var to = nowUtc ?? DateTime.UtcNow;
        // Full scans pass DateTime.MinValue; immich-go accepts its zero date but
        // a far-past date is more portable across immich-go versions.
        var from = sinceUtc == DateTime.MinValue ? to.AddYears(-50) : sinceUtc;
        args.Add($"--date-range={from:yyyy-MM-dd},{to:yyyy-MM-dd}");

        // immich-go documents --concurrent-tasks as 1-20.
        args.Add($"--concurrent-tasks={Math.Clamp(cfg.Concurrency, 1, 20)}");
        args.Add($"--on-errors={AppConfig.NormalizeOnErrors(cfg.OnErrors)}");

        if (!string.IsNullOrWhiteSpace(cfg.ManageBurst))
            args.Add($"--manage-burst={cfg.ManageBurst.Trim()}");
        if (!string.IsNullOrWhiteSpace(cfg.ManageRawJpeg))
            args.Add($"--manage-raw-jpeg={cfg.ManageRawJpeg.Trim()}");

        args.Add($"--client-timeout={Math.Clamp(cfg.ClientTimeoutMinutes, 1, 24 * 60)}m");
        if (!string.IsNullOrWhiteSpace(cfg.LogLevel)) args.Add($"--log-level={cfg.LogLevel}");

        // ── Simple options (immich-go defaults apply when unset) ──
        if (!IsDefault(cfg.FolderAsAlbum, "NONE")) args.Add($"--folder-as-album={cfg.FolderAsAlbum}");
        if (!string.IsNullOrWhiteSpace(cfg.IntoAlbum)) args.Add($"--into-album={cfg.IntoAlbum}");
        if (!string.IsNullOrWhiteSpace(cfg.ManageBurst)) args.Add($"--manage-burst={cfg.ManageBurst}");
        if (!string.IsNullOrWhiteSpace(cfg.ManageRawJpeg)) args.Add($"--manage-raw-jpeg={cfg.ManageRawJpeg}");
        if (!IsDefault(cfg.ManageHeicJpeg, "NoStack")) args.Add($"--manage-heic-jpeg={cfg.ManageHeicJpeg}");

        // ── Advanced options (emit only when enabled / non-default) ──
        if (!cfg.DateFromName) args.Add("--date-from-name=false");
        if (cfg.IgnoreSidecarFiles) args.Add("--ignore-sidecar-files");
        if (cfg.ManageEpsonFastFoto) args.Add("--manage-epson-fastfoto");
        if (cfg.FolderAsTags) args.Add("--folder-as-tags");
        if (cfg.SessionTag) args.Add("--session-tag");
        if (cfg.ApiTrace) args.Add("--api-trace");
        if (cfg.SkipSslVerify) args.Add("--skip-verify-ssl");
        if (!string.IsNullOrWhiteSpace(cfg.DeviceUuid)) args.Add($"--device-uuid={cfg.DeviceUuid}");
        if (!string.IsNullOrWhiteSpace(cfg.TimeZone)) args.Add($"--time-zone={cfg.TimeZone}");
        if (!string.IsNullOrWhiteSpace(cfg.AlbumPathJoiner)) args.Add($"--album-path-joiner={cfg.AlbumPathJoiner}");
        if (!IsDefault(cfg.IncludeType, "all")) args.Add($"--include-type={cfg.IncludeType}");
        if (!string.IsNullOrWhiteSpace(cfg.IncludeExtensions)) args.Add($"--include-extensions={cfg.IncludeExtensions}");
        if (!string.IsNullOrWhiteSpace(cfg.ExcludeExtensions)) args.Add($"--exclude-extensions={cfg.ExcludeExtensions}");
        foreach (var pattern in SplitLines(cfg.BanFiles)) args.Add($"--ban-file={pattern}");
        foreach (var tag in SplitCsv(cfg.Tags)) args.Add($"--tag={tag}");

        if (cfg.Overwrite)
        {
            args.Add("--overwrite");
            warnings.Add("Overwrite mode replaces existing files on the server.");
        }

        if (cfg.DryRun)
        {
            args.Add("--dry-run");
            warnings.Add("Dry-run mode is enabled: nothing will be uploaded.");
        }

        // ── Secrets: environment only, never argv ──────────────
        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(cfg.ApiKey))
            env[ApiKeyEnvVar] = cfg.ApiKey.Trim();

        var adminKey = (cfg.AdminApiKey ?? "").Trim();
        var hasAdminKey = adminKey.Length > 0;
        if (hasAdminKey)
            env[AdminApiKeyEnvVar] = adminKey;

        var pause = cfg.PauseImmichJobs && hasAdminKey;
        if (cfg.PauseImmichJobs && !hasAdminKey)
        {
            warnings.Add(
                "Pause Immich jobs is enabled but no Admin API key is configured. " +
                "Pausing is disabled for this run; set an Admin API key to enable it.");
        }
        args.Add($"--pause-immich-jobs={(pause ? "true" : "false")}");

        // Positional source folder last.
        args.Add(sourceFolder);

        return new ImmichGoInvocation
        {
            Arguments = args,
            Environment = env,
            Warnings = warnings,
            DisplayCommand = BuildDisplayCommand(cfg.DefaultImmichGoPath, args)
        };
    }

    private static bool IsDefault(string? value, string defaultValue)
        => string.IsNullOrWhiteSpace(value) || value.Equals(defaultValue, StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> SplitLines(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : value.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                   .Select(v => v.Trim())
                   .Where(v => v.Length > 0);

    private static IEnumerable<string> SplitCsv(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : value.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                   .Select(v => v.Trim())
                   .Where(v => v.Length > 0);

    public static string BuildDisplayCommand(string executable, IReadOnlyList<string> args)
    {
        var sb = new StringBuilder(Quote(executable));
        foreach (var arg in args)
        {
            sb.Append(' ');
            sb.Append(Quote(arg));
        }
        return sb.ToString();
    }

    private static string Quote(string value)
    {
        if (string.IsNullOrEmpty(value)) return "\"\"";
        return value.Any(char.IsWhiteSpace) ? $"\"{value}\"" : value;
    }

    public UploadResult Run(UploadRequest request)
    {
        var cfg = request.Config;
        var invocation = BuildInvocation(cfg, request.SourceFolder, request.SinceUtc);

        request.Log?.Invoke($"[{request.RunMode}] {invocation.DisplayCommand}");
        foreach (var warning in invocation.Warnings) request.Log?.Invoke($"[warn] {warning}");

        var psi = new ProcessStartInfo
        {
            FileName = cfg.DefaultImmichGoPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var arg in invocation.Arguments) psi.ArgumentList.Add(arg);
        foreach (var pair in invocation.Environment) psi.Environment[pair.Key] = pair.Value;

        try
        {
            using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            var buffer = new StringBuilder();
            var logLock = new object();

            void HandleLine(string line, bool isError)
            {
                if (string.IsNullOrEmpty(line)) return;
                lock (logLock)
                {
                    buffer.AppendLine(line);
                    request.Log?.Invoke((isError ? "[stderr] " : "") + line);
                }
            }

            process.OutputDataReceived += (_, e) => HandleLine(e.Data ?? "", false);
            process.ErrorDataReceived += (_, e) => HandleLine(e.Data ?? "", true);

            if (!process.Start())
            {
                return new UploadResult
                {
                    Success = false,
                    ExitCode = -1,
                    LogFile = request.LogFile,
                    Message = "Failed to start immich-go process",
                    SourceFolder = request.SourceFolder,
                    RunMode = request.RunMode
                };
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            while (!process.HasExited)
            {
                if (request.CancellationToken.IsCancellationRequested)
                {
                    try { process.Kill(entireProcessTree: true); } catch { }
                    return new UploadResult
                    {
                        Success = false,
                        ExitCode = -2,
                        LogFile = request.LogFile,
                        Message = "Cancelled",
                        SourceFolder = request.SourceFolder,
                        RunMode = request.RunMode
                    };
                }
                Thread.Sleep(200);
            }
            process.WaitForExit();

            try { File.AppendAllText(request.LogFile, buffer.ToString()); } catch { }

            var ok = process.ExitCode == 0;
            return new UploadResult
            {
                Success = ok,
                ExitCode = process.ExitCode,
                LogFile = request.LogFile,
                Message = ok ? "Completed" : $"Exited with code {process.ExitCode}",
                SourceFolder = request.SourceFolder,
                RunMode = request.RunMode
            };
        }
        catch (Exception ex)
        {
            request.Log?.Invoke($"[error] {ex.Message}");
            return new UploadResult
            {
                Success = false,
                ExitCode = -1,
                LogFile = request.LogFile,
                Message = ex.Message,
                SourceFolder = request.SourceFolder,
                RunMode = request.RunMode
            };
        }
    }

    /// <summary>Recursively enumerate media files modified since <paramref name="sinceUtc"/>.</summary>
    public static List<string> EnumerateChangedFiles(string folder, DateTime sinceUtc, CancellationToken ct)
    {
        var results = new List<string>();
        if (!Directory.Exists(folder)) return results;

        var stack = new Stack<DirectoryInfo>();
        stack.Push(new DirectoryInfo(folder));

        while (stack.Count > 0)
        {
            if (ct.IsCancellationRequested) break;
            var dir = stack.Pop();
            try
            {
                foreach (var sub in dir.EnumerateDirectories())
                {
                    if (PathFilters.IsExcludedDirectoryName(sub.Name)) continue;
                    if ((sub.Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint) continue;
                    stack.Push(sub);
                }
            }
            catch { }

            try
            {
                foreach (var file in dir.EnumerateFiles())
                {
                    try
                    {
                        if (!MediaFileClassifier.IsMediaFile(file.Name)) continue;
                        if (file.LastWriteTimeUtc >= sinceUtc) results.Add(file.FullName);
                    }
                    catch { }
                }
            }
            catch { }
        }

        return results;
    }
}
