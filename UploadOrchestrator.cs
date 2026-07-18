namespace ImmichUploader;

public sealed class UploadOrchestrator : IDisposable
{
    private readonly object _stateLock = new();
    private CancellationTokenSource? _runCts;
    private Task? _runningTask;
    private volatile bool _paused;
    private readonly ManualResetEventSlim _pauseGate = new(true);

    public event Action<string>? Log;
    public event Action<UploadResult>? FolderCompleted;
    public event Action<RunSummary>? RunCompleted;

    public bool IsRunning
    {
        get { lock (_stateLock) return _runningTask != null && !_runningTask.IsCompleted; }
    }

    public bool IsPaused => _paused;

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
        }
    }

    public sealed class RunOptions
    {
        public bool ForceFullRescan { get; init; }
        public string Trigger { get; init; } = "manual";
    }

    public sealed class RunSummary
    {
        public int TotalFolders { get; init; }
        public int SuccessCount { get; init; }
        public int FailCount { get; init; }
        public bool WasFullRescan { get; init; }
        public string Trigger { get; init; } = "";
        public string LogFile { get; init; } = "";
    }

    public void Start(AppConfig cfg, RunOptions options)
    {
        lock (_stateLock)
        {
            if (_runningTask != null && !_runningTask.IsCompleted) return;
            _runCts = new CancellationTokenSource();
            var ct = _runCts.Token;
            _runningTask = Task.Run(() => RunInternal(cfg, options, ct), ct);
        }
    }

    private void RunInternal(AppConfig cfg, RunOptions options, CancellationToken ct)
    {
        var state = StateStore.Load();
        var logDir = cfg.DefaultLogDir;
        Directory.CreateDirectory(logDir);
        var logFile = Path.Combine(logDir, $"upload-{DateTime.Now:yyyy-MM-dd-HHmmss}-{options.Trigger}.log");

        var nowUtc = DateTime.UtcNow;
        var sinceDefault = nowUtc.AddDays(-Math.Max(1, cfg.DaysBack));

        var today = DateTime.UtcNow.Date;
        var daysSinceFull = state.LastFullRescan.HasValue
            ? (int)(today - state.LastFullRescan.Value.Date).TotalDays
            : int.MaxValue;
        var doFull = options.ForceFullRescan || daysSinceFull >= 30;

        if (doFull) state.LastFullRescan = nowUtc;
        state.LastRunStarted = nowUtc;
        StateStore.Save(state);

        Log?.Invoke($"[start] Run trigger={options.Trigger} fullRescan={doFull} folders={cfg.Folders.Count} concurrency={cfg.Concurrency}");
        try { File.WriteAllText(logFile, $"Run started: {nowUtc:o} trigger={options.Trigger} fullRescan={doFull}\n"); } catch { }

        int success = 0, fail = 0;
        var folderParallelism = Math.Max(1, Math.Min(3, Environment.ProcessorCount / 2));
        using var folderGate = new SemaphoreSlim(folderParallelism, folderParallelism);

        var tasks = cfg.Folders.Select(folder =>
            Task.Run(() =>
            {
                if (ct.IsCancellationRequested) return;
                folderGate.Wait(ct);
                try
                {
                    _pauseGate.Wait(ct);

                    if (!Directory.Exists(folder))
                    {
                        Log?.Invoke($"[warn] Source path does not exist: {folder}");
                        Interlocked.Increment(ref fail);
                        return;
                    }

                    DateTime since;
                    if (doFull)
                    {
                        since = DateTime.MinValue;
                    }
                    else if (state.LastSuccessByFolder.TryGetValue(folder, out var last))
                    {
                        since = last.ToUniversalTime();
                    }
                    else
                    {
                        since = sinceDefault;
                    }

                    var result = ImmichGoRunner.RunUpload(
                        cfg, folder, since, logFile,
                        options.Trigger, ct,
                        msg => Log?.Invoke($"[{Path.GetFileName(folder)}] {msg}"));

                    if (result.Success)
                    {
                        lock (state)
                        {
                            state.LastSuccessByFolder[folder] = DateTime.UtcNow;
                        }
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
                    folderGate.Release();
                }
            }, ct)).ToArray();

        try { Task.WaitAll(tasks, ct); }
        catch (OperationCanceledException) { }
        catch (AggregateException) { }

        var summary = new RunSummary
        {
            TotalFolders = cfg.Folders.Count,
            SuccessCount = success,
            FailCount = fail,
            WasFullRescan = doFull,
            Trigger = options.Trigger,
            LogFile = logFile
        };

        state.LastRunFinished = DateTime.UtcNow;
        state.LastRunResult = fail == 0 ? "success" : (success == 0 ? "failure" : "partial");
        StateStore.Save(state);

        try { File.AppendAllText(logFile, $"\nRun finished: success={success} fail={fail} log={logFile}\n"); } catch { }
        Log?.Invoke($"[done] {summary.Trigger} success={success} fail={fail} log={logFile}");
        RunCompleted?.Invoke(summary);
    }

    public void Dispose()
    {
        try { _runCts?.Cancel(); } catch { }
        try { _runningTask?.Wait(2000); } catch { }
        _runCts?.Dispose();
        _pauseGate.Dispose();
    }
}
