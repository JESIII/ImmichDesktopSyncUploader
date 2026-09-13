namespace ImmichUploader;

/// <summary>Result of a single immich-go invocation.</summary>
public sealed class UploadResult
{
    public bool Success { get; init; }
    public int ExitCode { get; init; }
    public string LogFile { get; init; } = "";
    public string Message { get; init; } = "";
    public string SourceFolder { get; init; } = "";
    public string RunMode { get; init; } = "";
}

/// <summary>Everything needed to run one immich-go upload for one folder.</summary>
public sealed class UploadRequest
{
    public required AppConfig Config { get; init; }
    public required string SourceFolder { get; init; }
    public DateTime SinceUtc { get; init; }
    public string LogFile { get; init; } = "";
    public string RunMode { get; init; } = "";
    public CancellationToken CancellationToken { get; init; }
    public Action<string>? Log { get; init; }
}

/// <summary>
/// Seam over the immich-go process invocation so the orchestrator can be tested
/// with a deterministic fake runner.
/// </summary>
public interface IUploadRunner
{
    UploadResult Run(UploadRequest request);
}
