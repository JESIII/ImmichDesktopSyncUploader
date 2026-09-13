using ImmichUploader;

namespace ImmichUploader.Tests;

/// <summary>Throwaway directory under the OS temp path.</summary>
internal sealed class TempFolder : IDisposable
{
    public string Path { get; }

    public TempFolder()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "immichuploader-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string CreateDir(string name)
    {
        var full = System.IO.Path.Combine(Path, name);
        Directory.CreateDirectory(full);
        return full;
    }

    public string CreateFile(string name, string content = "x")
    {
        var full = System.IO.Path.Combine(Path, name);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch { }
    }
}

/// <summary>Deterministic in-memory runner that also tracks peak concurrency.</summary>
internal sealed class FakeRunner : IUploadRunner
{
    private readonly object _lock = new();
    private int _current;

    public int MaxObservedConcurrency { get; private set; }
    public int CallCount { get; private set; }
    public List<UploadRequest> Requests { get; } = new();

    public Func<UploadRequest, UploadResult> Handler { get; set; } = request => new UploadResult
    {
        Success = true,
        SourceFolder = request.SourceFolder,
        RunMode = request.RunMode,
        LogFile = request.LogFile,
        Message = "ok"
    };

    public UploadResult Run(UploadRequest request)
    {
        lock (_lock)
        {
            _current++;
            CallCount++;
            if (_current > MaxObservedConcurrency) MaxObservedConcurrency = _current;
            Requests.Add(request);
        }

        try
        {
            return Handler(request);
        }
        finally
        {
            lock (_lock) _current--;
        }
    }
}

internal static class TestConfig
{
    public static AppConfig Create(params string[] folders) => new()
    {
        Server = "http://immich.local:2283",
        ApiKey = "SECRET-KEY",
        Folders = folders.ToList(),
        Concurrency = 2,
        MaxParallelProcesses = 2,
        ClientTimeoutMinutes = 60,
        DaysBack = 7,
        // Keep incremental runs from auto-upgrading to a full scan during tests.
        MonthlyEnabled = false
    };
}
