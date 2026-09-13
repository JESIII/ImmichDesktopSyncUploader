using System.IO;

namespace ImmichUploader;

/// <summary>
/// Best-effort file readiness probe: a file is "ready" once it is readable and
/// its size has stopped changing (copy in progress).
/// </summary>
public static class FileReadiness
{
    public static bool IsReady(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
            var first = new FileInfo(path).Length;
            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                // Throws if the writer holds an exclusive lock.
            }
            Thread.Sleep(50);
            var second = new FileInfo(path).Length;
            return first == second;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        catch { return false; }
    }
}

/// <summary>
/// Recursive real-time watcher over the configured source folders.
///
/// Created, modified, and renamed media files are routed through a
/// <see cref="DebounceFileQueue"/> so a burst of events collapses into a single
/// upload batch. Files that are not yet fully written are re-queued for the
/// next window instead of being handed to immich-go mid-copy.
/// </summary>
public sealed class FolderWatcher : IDisposable
{
    private readonly object _lock = new();
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly DebounceFileQueue _queue;
    private readonly Func<string, bool> _isReady;
    private AppConfig _cfg;
    private bool _running;

    /// <summary>Raised (on a worker thread) with the ready files for one batch.</summary>
    public event Action<IReadOnlyList<string>>? BatchReady;

    public event Action<string>? Log;

    public FolderWatcher(AppConfig cfg, Func<string, bool>? isReady = null)
    {
        _cfg = cfg;
        _isReady = isReady ?? FileReadiness.IsReady;
        _queue = new DebounceFileQueue(cfg.WatcherDebounceSeconds, OnBatch);
    }

    public bool IsRunning
    {
        get { lock (_lock) return _running; }
    }

    public int WatchedFolderCount
    {
        get { lock (_lock) return _watchers.Count; }
    }

    public int PendingCount => _queue.Count;

    /// <summary>Start watching. Returns true when at least one folder is watched.</summary>
    public bool Start()
    {
        lock (_lock)
        {
            if (_running) return true;

            _queue.Reset(_cfg.WatcherDebounceSeconds);

            foreach (var folder in _cfg.Folders)
            {
                if (!Directory.Exists(folder))
                {
                    Log?.Invoke($"[watch] Skipping missing folder: {folder}");
                    continue;
                }
                try
                {
                    var watcher = new FileSystemWatcher(folder)
                    {
                        IncludeSubdirectories = true,
                        NotifyFilter = NotifyFilters.FileName
                                     | NotifyFilters.LastWrite
                                     | NotifyFilters.Size
                                     | NotifyFilters.DirectoryName,
                        InternalBufferSize = 64 * 1024
                    };
                    watcher.Created += (_, e) => Handle(e.FullPath);
                    watcher.Changed += (_, e) => Handle(e.FullPath);
                    watcher.Renamed += (_, e) => Handle(e.FullPath);
                    watcher.Error += (_, e) =>
                        Log?.Invoke($"[watch] Watcher error in {folder}: {e.GetException().Message}");
                    watcher.EnableRaisingEvents = true;
                    _watchers.Add(watcher);
                    Log?.Invoke($"[watch] Watching: {folder}");
                }
                catch (Exception ex)
                {
                    Log?.Invoke($"[watch] Failed to watch {folder}: {ex.Message}");
                }
            }

            _running = _watchers.Count > 0;
            return _running;
        }
    }

    /// <summary>Stop watching and clear queued events.</summary>
    public void Stop()
    {
        List<FileSystemWatcher> toDispose;
        lock (_lock)
        {
            toDispose = _watchers.ToList();
            _watchers.Clear();
            _running = false;
        }
        foreach (var watcher in toDispose)
        {
            try
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }
            catch { }
        }
        _queue.Shutdown();
    }

    /// <summary>
    /// Apply a changed configuration: restart watching against the new folders
    /// and debounce window, or stop entirely when disabled.
    /// </summary>
    public void Refresh(AppConfig cfg)
    {
        Stop();
        _cfg = cfg;
        if (cfg.WatcherEnabled) Start();
    }

    public IReadOnlyList<string> FlushPending() => _queue.Flush();

    /// <summary>Config-only acceptance test (does not require the watcher to be running).</summary>
    public bool ShouldAccept(string path)
    {
        AppConfig cfg;
        lock (_lock) cfg = _cfg;
        return PathFilters.ShouldWatchPath(cfg.Folders, path);
    }

    /// <summary>
    /// Route a filesystem event path into the debounce queue. Returns false when
    /// not running or the path is filtered out. Exposed for testing.
    /// </summary>
    public bool Handle(string path)
    {
        bool running;
        lock (_lock) running = _running;
        if (!running) return false;
        if (!ShouldAccept(path)) return false;
        _queue.Add(path);
        return true;
    }

    private void OnBatch(IReadOnlyList<string> files)
    {
        var ready = new List<string>();
        var notReady = new List<string>();
        foreach (var file in files)
        {
            if (_isReady(file)) ready.Add(file);
            else notReady.Add(file);
        }

        // Re-queue unready files: the next window retries once they settle.
        if (notReady.Count > 0) _queue.AddRange(notReady);
        if (ready.Count > 0) BatchReady?.Invoke(ready);
    }

    public void Dispose() => Stop();
}
