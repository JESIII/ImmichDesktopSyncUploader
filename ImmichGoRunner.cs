using System.Diagnostics;
using System.Text;

namespace ImmichUploader;

public sealed class UploadResult
{
    public bool Success { get; init; }
    public int ExitCode { get; init; }
    public string LogFile { get; init; } = "";
    public string Message { get; init; } = "";
}

public static class ImmichGoRunner
{
    public static UploadResult RunUpload(
        AppConfig cfg,
        string sourceFolder,
        DateTime sinceUtc,
        string logFile,
        string runMode,
        CancellationToken ct,
        Action<string>? log = null)
    {
        log?.Invoke($"[{runMode}] Starting upload of '{sourceFolder}' (since {sinceUtc:yyyy-MM-dd HH:mm})");

        var dateRange = $"{sinceUtc:yyyy-MM-dd},{DateTime.UtcNow:yyyy-MM-dd}";

        var args = new List<string>
        {
            "upload", "from-folder",
            "--no-ui",
            $"--server={cfg.Server}",
            $"--api-key={cfg.ApiKey}",
            "--recursive",
            "--manage-raw-jpeg=StackCoverRaw",
            $"--date-range={dateRange}",
            $"--concurrent-tasks={Math.Max(1, cfg.Concurrency)}",
            "--on-errors", "continue",
            "--pause-immich-jobs=true",
            "--manage-burst", "Stack",
            "--client-timeout", "60m",
            "--session-tag",
            $"\"{sourceFolder}\""
        };

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
        foreach (var a in args) psi.ArgumentList.Add(a);

        try
        {
            using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
            var sb = new StringBuilder();
            var logLock = new object();

            void HandleLine(string line, bool isErr)
            {
                if (string.IsNullOrEmpty(line)) return;
                lock (logLock)
                {
                    sb.AppendLine(line);
                    log?.Invoke((isErr ? "[stderr] " : "") + line);
                }
            }

            p.OutputDataReceived += (_, e) => HandleLine(e.Data ?? "", false);
            p.ErrorDataReceived += (_, e) => HandleLine(e.Data ?? "", true);

            if (!p.Start())
            {
                return new UploadResult { Success = false, ExitCode = -1, LogFile = logFile, Message = "Failed to start immich-go process" };
            }
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();

            while (!p.HasExited)
            {
                if (ct.IsCancellationRequested)
                {
                    try { p.Kill(true); } catch { }
                    return new UploadResult { Success = false, ExitCode = -2, LogFile = logFile, Message = "Cancelled" };
                }
                Thread.Sleep(200);
            }
            p.WaitForExit();

            try { File.AppendAllText(logFile, sb.ToString()); } catch { }

            var ok = p.ExitCode == 0;
            return new UploadResult
            {
                Success = ok,
                ExitCode = p.ExitCode,
                LogFile = logFile,
                Message = ok ? "Completed" : $"Exited with code {p.ExitCode}"
            };
        }
        catch (Exception ex)
        {
            log?.Invoke($"[error] {ex.Message}");
            return new UploadResult { Success = false, ExitCode = -1, LogFile = logFile, Message = ex.Message };
        }
    }

    public static List<string> EnumerateChangedFiles(string folder, DateTime sinceUtc, CancellationToken ct)
    {
        var results = new List<string>();
        if (!Directory.Exists(folder)) return results;

        var stack = new Stack<DirectoryInfo>();
        stack.Push(new DirectoryInfo(folder));
        var excludeDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "@eaDir", "@__thumb", ".Spotlight-V100", ".photostructure", "thumbnails",
            "Lightroom Catalog", "Recently Deleted", "$RECYCLE.BIN", "System Volume Information"
        };

        while (stack.Count > 0)
        {
            if (ct.IsCancellationRequested) break;
            var dir = stack.Pop();
            try
            {
                foreach (var sub in dir.EnumerateDirectories())
                {
                    if (excludeDirs.Contains(sub.Name)) continue;
                    if ((sub.Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint) continue;
                    stack.Push(sub);
                }
            }
            catch { }
            try
            {
                foreach (var f in dir.EnumerateFiles())
                {
                    try
                    {
                        if (f.LastWriteTimeUtc >= sinceUtc)
                            results.Add(f.FullName);
                    }
                    catch { }
                }
            }
            catch { }
        }
        return results;
    }
}
