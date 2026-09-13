namespace ImmichUploader;

/// <summary>
/// Coordinates immich-go runs across source folders.
///
/// Supports:
///  - targeted incremental scans (only the folders the watcher touched),
///  - full scans (manual or scheduled),
///  - trigger coalescing (a request during a run is merged, not dropped),
///  - a separate process-concurrency limit from per-process task concurrency,
///  - cancellation and pause, with durable per-folder state updates.
/// </summary>
public sealed class UploadOrchestrator : IDisposable
{
    private readonly object _stateLock = new();
    private readonly IUploadRunner _runner;
    private readonly Func<UploadState> _loadState;
    private readonly Action<UploadState> _saveState;

    private CancellationTokenSource? _runCts;
    private Task? _runningTask;
    private volatile bool _paused;
    private readonly ManualResetEventSlim _pauseGate = new(true);

    private AppConfig? _pendingCfg;
    private RunOptions? _pendingOptions;
    private string _currentTrigger = "";
    private bool _disposed;

    public event Action<string>? Log;
    public event Action<UploadResult>? FolderCompleted;
    public event Action<RunSummary>? RunCompleted;

    public UploadOrchestrator(
        IUploadRunner? runner = null,
        Func<UploadState>? loadState = null,
        Action<UploadState>? saveState = null)
    {
        _runner = runner ?? new ImmichGoRunner();
        _loadState = loadState ?? StateStore.Load;
        _saveState = saveState ?? StateStore.Save;
    }

    public bool IsRunning
    {
        get { lock (_stateLock) return _runningTask is { IsCompleted: false }; }
    }

    public bool IsPaused => _paused;

    public string CurrentTrigger
    {
        get { lock (_stateLock) return _currentTrigger; }
    }

    public bool HasPending
    {
        get { lock (_stateLock) return _pendingOptions is not null; }
    }

    public void TogglePause()
    {
        _paused = !_paused;
        if (_paused) _pauseGate.Reset();
        else _pauseGate.Set();
        Log?.Invoke(_paused ? "[pause] Paused" : "[pause] Resumed");
    }

    public void Cancel()
    {
        lock (_stateLock)
        {
            _runCts?.Cancel();
            // A cancel also drops any coalesced request so the app truly stops.
            _pendingOptions = null;
            _pendingCfg = null;
        }
    }

    public sealed class RunOptions
    {
        /// <summary>Ignore per-folder last-success timestamps and rescan everything.</summary>
        public bool FullRescan { get; init; }

        public string Trigger { get; init; } = "manual";

        /// <summary>
        /// Restrict the run to these folders (watcher-triggered partial scans).
        /// Null or empty means "all configured folders".
        /// </summary>
        public IReadOnlyList<string>? TargetFolders { get; init; }

        /// <summary>Force a specific "since" timestamp for targeted runs.</summary>
        public DateTime? SinceOverrideUtc { get; init; }

        /// <summary>When false, a request during an active run is dropped instead of queued.</summary>
        public bool Coalesce { get; init; } = true;
    }

    public sealed class RunSummary
    {
        public int TotalFolders { get; init; }
        public int SuccessCount { get; init; }
        public int FailCount { get; init; }
        public bool WasFullRescan { get; init; }
        public bool WasTargeted { get; init; }
        public bool Cancelled { get; init; }
        public string Trigger { get; init; } = "";
        public string LogFile { get; init; } = "";
    }

    /// <summary>
    /// Entry point for every trigger. Starts a run, or coalesces the request
    /// into the next run when one is already in flight.
    /// </summary>
    public void RequestRun(AppConfig cfg, RunOptions options)
    {
        lock (_stateLock)
        {
            if (_disposed) return;

            if (_runningTask is { IsCompleted: false })
            {
                if (!options.Coalesce)
                {
                    Log?.Invoke($"[queue] {options.Trigger} ignored; a run is already in progress.");
                    return;
                }
                _pendingCfg = Clone(cfg);
                _pendingOptions = Merge(_pendingOptions, options);
                Log?.Invoke($"[queue] Coalesced '{options.Trigger}' into the next run.");
                return;
            }

            StartLocked(cfg, options);
        }
    }

    /// <summary>Backwards-compatible alias for <see cref="RequestRun"/>.</summary>
    public void Start(AppConfig cfg, RunOptions options) => RequestRun(cfg, options);

    private void StartLocked(AppConfig cfg, RunOptions options)
    {
        _runCts = new CancellationTokenSource();
        _currentTrigger = options.Trigger;
        var snapshot = Clone(cfg);
        var token = _runCts.Token;
        _runningTask = Task.Run(() => RunInternal(snapshot, options, token));
    }

    private void RunInternal(AppConfig cfg, RunOptions options, CancellationToken ct)
    {
        try
        {
            var state = _loadState();
            var logDir = cfg.DefaultLogDir;
            Directory.CreateDirectory(logDir);
            var logFile = Path.Combine(logDir, $"upload-{DateTime.Now:yyyy-MM-dd-HHmmss}-{options.Trigger}.log");

            var targeted = options.TargetFolders is { Count: > 0 };
            var targets = targeted
                ? options.TargetFolders!
                    .Where(f => !string.IsNullOrWhiteSpace(f))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
                : cfg.Folders.ToList();

            var nowUtc = DateTime.UtcNow;
            var sinceDefault = nowUtc.AddDays(-Math.Max(1, cfg.DaysBack));

            var today = nowUtc.Date;
            var daysSinceFull = state.LastFullRescan.HasValue
                ? (int)(today - state.LastFullRescan.Value.Date).TotalDays
                : int.MaxValue;
            // Only auto-upgrade to a full scan when the monthly schedule is enabled.
            var doFull = options.FullRescan
                || (!targeted && cfg.MonthlyEnabled && daysSinceFull >= 30);

            if (doFull) state.LastFullRescan = nowUtc;
            state.LastRunStarted = nowUtc;
            state.LastRunTrigger = options.Trigger;
            _saveState(state);

            Log?.Invoke(
                $"[start] trigger={options.Trigger} fullRescan={doFull} targeted={targeted} " +
                $"folders={targets.Count} processes={cfg.MaxParallelProcesses} tasks={cfg.Concurrency}");

            try
            {
                File.WriteAllText(logFile,
                    $"Run started: {nowUtc:o} trigger={options.Trigger} fullRescan={doFull} targeted={targeted}\n");
            }
            catch { }

            int success = 0, fail = 0;
            var maxProcesses = Math.Max(1, Math.Min(cfg.MaxParallelProcesses, Math.Max(1, targets.Count)));
            using var processGate = new SemaphoreSlim(maxProcesses, maxProcesses);

            var tasks = targets.Select(folder => Task.Run(() =>
            {
                if (ct.IsCancellationRequested) return;
                var gateEntered = false;
                try
                {
                    processGate.Wait(ct);
                    gateEntered = true;

                    // Pause blocks *new* folder processes; in-flight ones finish.
                    _pauseGate.Wait(ct);

                    if (!Directory.Exists(folder))
                    {
                        Log?.Invoke($"[warn] Source path does not exist: {folder}");
                        Interlocked.Increment(ref fail);
                        return;
                    }

                    DateTime since;
                    if (doFull) since = DateTime.MinValue;
                    else if (options.SinceOverrideUtc is DateTime ov) since = ov;
                    else if (state.LastSuccessByFolder.TryGetValue(folder, out var last)) since = last.ToUniversalTime();
                    else since = sinceDefault;

                    var result = _runner.Run(new UploadRequest
                    {
                        Config = cfg,
                        SourceFolder = folder,
                        SinceUtc = since,
                        LogFile = logFile,
                        RunMode = options.Trigger,
                        CancellationToken = ct,
                        Log = msg => Log?.Invoke($"[{Path.GetFileName(folder)}] {msg}")
                    });

                    if (result.Success)
                    {
                        lock (state) state.LastSuccessByFolder[folder] = DateTime.UtcNow;
                        Interlocked.Increment(ref success);
                    }
                    else
                    {
                        Interlocked.Increment(ref fail);
                    }

                    FolderCompleted?.Invoke(result);
                }
                catch (OperationCanceledException)
                {
                    Interlocked.Increment(ref fail);
                    Log?.Invoke($"[cancel] Folder cancelled: {folder}");
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref fail);
                    Log?.Invoke($"[error] {folder}: {ex.Message}");
                }
                finally
                {
                    if (gateEntered) processGate.Release();
                }
            }, ct)).ToArray();

            try { Task.WaitAll(tasks, ct); }
            catch (OperationCanceledException) { }
            catch (AggregateException) { }

            var cancelled = ct.IsCancellationRequested;
            state.LastRunFinished = DateTime.UtcNow;
            state.LastRunResult = cancelled
                ? "cancelled"
                : fail == 0 ? "success" : success == 0 ? "failure" : "partial";
            _saveState(state);

            var summary = new RunSummary
            {
                TotalFolders = targets.Count,
                SuccessCount = success,
                FailCount = fail,
                WasFullRescan = doFull,
                WasTargeted = targeted,
                Cancelled = cancelled,
                Trigger = options.Trigger,
                LogFile = logFile
            };

            try { File.AppendAllText(logFile, $"\nRun finished: success={success} fail={fail} cancelled={cancelled}\n"); } catch { }
            Log?.Invoke($"[done] {options.Trigger} success={success} fail={fail} log={logFile}");
            RunCompleted?.Invoke(summary);
        }
        finally
        {
            RunOptions? pending;
            AppConfig? pendingCfg;
            lock (_stateLock)
            {
                pending = _pendingOptions;
                pendingCfg = _pendingCfg;
                _pendingOptions = null;
                _pendingCfg = null;
            }

            if (pending is not null && pendingCfg is not null && !_disposed)
            {
                Log?.Invoke($"[queue] Starting coalesced run '{pending.Trigger}'.");
                lock (_stateLock) StartLocked(pendingCfg, pending);
            }
        }
    }

    private static RunOptions Merge(RunOptions? existing, RunOptions next)
    {
        if (existing is null) return next;

        // If either run is unbounded (all folders), the merged run is unbounded.
        var target = existing.TargetFolders is null || next.TargetFolders is null
            ? null
            : existing.TargetFolders
                .Concat(next.TargetFolders)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        return new RunOptions
        {
            FullRescan = existing.FullRescan || next.FullRescan,
            Trigger = next.Trigger,
            TargetFolders = target,
            SinceOverrideUtc = existing.SinceOverrideUtc ?? next.SinceOverrideUtc,
            Coalesce = true
        };
    }

    private static AppConfig Clone(AppConfig c) => new()
    {
        ConfigVersion = c.ConfigVersion,
        Server = c.Server,
        ApiKey = c.ApiKey,
        AdminApiKey = c.AdminApiKey,
        Folders = new List<string>(c.Folders),
        Concurrency = c.Concurrency,
        MaxParallelProcesses = c.MaxParallelProcesses,
        DaysBack = c.DaysBack,
        OnErrors = c.OnErrors,
        ManageBurst = c.ManageBurst,
        ManageRawJpeg = c.ManageRawJpeg,
        ClientTimeoutMinutes = c.ClientTimeoutMinutes,
        SessionTag = c.SessionTag,
        Recursive = c.Recursive,
        SkipSslVerify = c.SkipSslVerify,
        PauseImmichJobs = c.PauseImmichJobs,
        LogDir = c.LogDir,
        ImmichGoPath = c.ImmichGoPath,
        LaunchOnWindowsStartup = c.LaunchOnWindowsStartup,
        StartMinimizedToTray = c.StartMinimizedToTray,
        WeeklyEnabled = c.WeeklyEnabled,
        WeeklyDay = c.WeeklyDay,
        WeeklyHour = c.WeeklyHour,
        WeeklyMinute = c.WeeklyMinute,
        MonthlyEnabled = c.MonthlyEnabled,
        MonthlyRescanDay = c.MonthlyRescanDay,
        MonthlyRescanHour = c.MonthlyRescanHour,
        MonthlyRescanMinute = c.MonthlyRescanMinute,
        WatcherEnabled = c.WatcherEnabled,
        WatcherDebounceSeconds = c.WatcherDebounceSeconds
    };

    public void Dispose()
    {
        _disposed = true;
        lock (_stateLock)
        {
            try { _runCts?.Cancel(); } catch { }
        }
        try { _runningTask?.Wait(2000); } catch { }
        _runCts?.Dispose();
        _pauseGate.Dispose();
    }
}
