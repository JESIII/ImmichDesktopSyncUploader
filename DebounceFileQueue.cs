using Timer = System.Threading.Timer;

namespace ImmichUploader;

/// <summary>
/// Thread-safe file queue that batches changes within a fixed window.
///
/// The timer starts on the empty -> non-empty transition and is deliberately
/// NOT reset by subsequent files, so a folder that keeps receiving changes
/// still flushes at least once per window. A sliding window could starve
/// uploads forever under a continuous stream of events.
///
/// Duplicate suppression: a file already queued keeps its original
/// first-seen timestamp and is only scheduled once, so repeated
/// created/modified events for the same file do not multiply work.
/// </summary>
public sealed class DebounceFileQueue : IDisposable
{
    private readonly object _lock = new();
    private readonly Dictionary<string, DateTime> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly Action<IReadOnlyList<string>> _callback;
    private Timer? _timer;
    private int _generation;
    private bool _shutdown;
    private int _debounceSeconds;

    public DebounceFileQueue(int debounceSeconds, Action<IReadOnlyList<string>> callback)
    {
        _debounceSeconds = Math.Max(1, debounceSeconds);
        _callback = callback ?? throw new ArgumentNullException(nameof(callback));
    }

    public int Count
    {
        get { lock (_lock) return _files.Count; }
    }

    public bool IsShutdown
    {
        get { lock (_lock) return _shutdown; }
    }

    /// <summary>Queue a single file path.</summary>
    public void Add(string filePath) => AddRange(new[] { filePath });

    /// <summary>Queue multiple paths, starting the window on the first entry.</summary>
    public void AddRange(IEnumerable<string> paths)
    {
        lock (_lock)
        {
            if (_shutdown) return;
            var wasEmpty = _files.Count == 0;
            var now = DateTime.UtcNow;
            foreach (var path in paths)
            {
                if (string.IsNullOrWhiteSpace(path)) continue;
                if (!_files.ContainsKey(path)) _files[path] = now;
            }
            if (wasEmpty && _files.Count > 0) StartTimerLocked();
        }
    }

    /// <summary>Immediately drain and return all queued files, cancelling the window.</summary>
    public IReadOnlyList<string> Flush()
    {
        lock (_lock) return DrainLocked();
    }

    /// <summary>
    /// Clear the queue and re-arm watching (used after a settings change or a
    /// manual flush). Optionally updates the debounce window.
    /// </summary>
    public void Reset(int? debounceSeconds = null)
    {
        lock (_lock)
        {
            _shutdown = false;
            _generation++;
            if (debounceSeconds is int d) _debounceSeconds = Math.Max(1, d);
            _timer?.Dispose();
            _timer = null;
            _files.Clear();
        }
    }

    /// <summary>Cancel the timer, clear the queue, and reject further additions.</summary>
    public void Shutdown()
    {
        lock (_lock)
        {
            _shutdown = true;
            _timer?.Dispose();
            _timer = null;
            _files.Clear();
        }
    }

    private IReadOnlyList<string> DrainLocked()
    {
        var result = _files.Keys.ToList();
        _files.Clear();
        _timer?.Dispose();
        _timer = null;
        return result;
    }

    private void StartTimerLocked()
    {
        if (_shutdown) return;
        _timer?.Dispose();
        var generation = _generation;
        _timer = new Timer(
            _ => OnTimeout(generation),
            state: null,
            dueTime: TimeSpan.FromSeconds(_debounceSeconds),
            period: Timeout.InfiniteTimeSpan);
    }

    private void OnTimeout(int generation)
    {
        IReadOnlyList<string> files;
        lock (_lock)
        {
            // Stale callback from before a Reset: do not drain or cancel.
            if (_shutdown || generation != _generation) return;
            _timer?.Dispose();
            _timer = null;
            files = DrainLocked();
        }
        if (files.Count > 0) _callback(files);
    }

    public void Dispose() => Shutdown();
}
