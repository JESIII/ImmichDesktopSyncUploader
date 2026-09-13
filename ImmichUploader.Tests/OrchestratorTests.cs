using ImmichUploader;
using Xunit;

namespace ImmichUploader.Tests;

public class OrchestratorTests
{
    private static UploadResult Ok(UploadRequest r) => new()
    {
        Success = true,
        SourceFolder = r.SourceFolder,
        RunMode = r.RunMode,
        LogFile = r.LogFile,
        Message = "ok"
    };

    [Fact]
    public void RespectsProcessConcurrencyLimit()
    {
        using var temp = new TempFolder();
        var folders = new[] { temp.CreateDir("a"), temp.CreateDir("b"), temp.CreateDir("c"), temp.CreateDir("d") };
        var cfg = TestConfig.Create(folders);
        cfg.MaxParallelProcesses = 2;

        var runner = new FakeRunner { Handler = r => { Thread.Sleep(120); return Ok(r); } };
        var state = new UploadState();
        using var orchestrator = new UploadOrchestrator(runner, () => state, _ => { });

        var done = new ManualResetEventSlim(false);
        orchestrator.RunCompleted += _ => done.Set();

        orchestrator.RequestRun(cfg, new UploadOrchestrator.RunOptions { Trigger = "manual" });

        Assert.True(done.Wait(TimeSpan.FromSeconds(25)), "run did not complete");
        Assert.Equal(4, runner.CallCount);
        Assert.True(runner.MaxObservedConcurrency <= 2, $"observed {runner.MaxObservedConcurrency} concurrent processes");
    }

    [Fact]
    public void TargetedRun_OnlyScansRequestedFolders()
    {
        using var temp = new TempFolder();
        var a = temp.CreateDir("a");
        var b = temp.CreateDir("b");
        var cfg = TestConfig.Create(a, b);

        var runner = new FakeRunner();
        var state = new UploadState();
        using var orchestrator = new UploadOrchestrator(runner, () => state, _ => { });

        UploadOrchestrator.RunSummary? summary = null;
        var done = new ManualResetEventSlim(false);
        orchestrator.RunCompleted += s => { summary = s; done.Set(); };

        orchestrator.RequestRun(cfg, new UploadOrchestrator.RunOptions
        {
            Trigger = "watcher",
            TargetFolders = new[] { b }
        });

        Assert.True(done.Wait(TimeSpan.FromSeconds(10)));
        Assert.Single(runner.Requests);
        Assert.Equal(b, runner.Requests[0].SourceFolder);
        Assert.NotNull(summary);
        Assert.True(summary!.WasTargeted);
        Assert.Equal(1, summary.TotalFolders);
    }

    [Fact]
    public void FullRun_PassesMinValueSince()
    {
        using var temp = new TempFolder();
        var a = temp.CreateDir("a");
        var cfg = TestConfig.Create(a);

        var runner = new FakeRunner();
        var state = new UploadState();
        using var orchestrator = new UploadOrchestrator(runner, () => state, _ => { });

        UploadOrchestrator.RunSummary? summary = null;
        var done = new ManualResetEventSlim(false);
        orchestrator.RunCompleted += s => { summary = s; done.Set(); };

        orchestrator.RequestRun(cfg, new UploadOrchestrator.RunOptions { FullRescan = true, Trigger = "manual-full" });

        Assert.True(done.Wait(TimeSpan.FromSeconds(10)));
        Assert.Equal(DateTime.MinValue, runner.Requests[0].SinceUtc);
        Assert.True(summary!.WasFullRescan);
    }

    [Fact]
    public void IncrementalRun_UsesPerFolderLastSuccess()
    {
        using var temp = new TempFolder();
        var a = temp.CreateDir("a");
        var cfg = TestConfig.Create(a);

        var runner = new FakeRunner();
        var lastSuccess = DateTime.UtcNow.AddDays(-3);
        var state = new UploadState();
        state.LastSuccessByFolder[a] = lastSuccess;
        using var orchestrator = new UploadOrchestrator(runner, () => state, _ => { });

        var done = new ManualResetEventSlim(false);
        orchestrator.RunCompleted += _ => done.Set();

        orchestrator.RequestRun(cfg, new UploadOrchestrator.RunOptions { Trigger = "manual" });

        Assert.True(done.Wait(TimeSpan.FromSeconds(10)));
        Assert.Equal(lastSuccess.ToUniversalTime(), runner.Requests[0].SinceUtc);
    }

    [Fact]
    public void SuccessfulFolder_RecordsLastSuccessAndResult()
    {
        using var temp = new TempFolder();
        var a = temp.CreateDir("a");
        var cfg = TestConfig.Create(a);

        var runner = new FakeRunner();
        var state = new UploadState();
        using var orchestrator = new UploadOrchestrator(runner, () => state, _ => { });

        var done = new ManualResetEventSlim(false);
        orchestrator.RunCompleted += _ => done.Set();

        orchestrator.RequestRun(cfg, new UploadOrchestrator.RunOptions { Trigger = "manual" });

        Assert.True(done.Wait(TimeSpan.FromSeconds(10)));
        Assert.True(state.LastSuccessByFolder.ContainsKey(a));
        Assert.Equal("success", state.LastRunResult);
        Assert.Equal("manual", state.LastRunTrigger);
    }

    [Fact]
    public void RequestDuringRun_IsCoalescedNotDropped()
    {
        using var temp = new TempFolder();
        var a = temp.CreateDir("a");
        var b = temp.CreateDir("b");
        var cfg = TestConfig.Create(a, b);
        cfg.MaxParallelProcesses = 1;

        var runner = new FakeRunner();
        var state = new UploadState();
        using var orchestrator = new UploadOrchestrator(runner, () => state, _ => { });

        var firstFolderStarted = new ManualResetEventSlim(false);
        var runs = 0;
        var twoRunsDone = new ManualResetEventSlim(false);

        runner.Handler = r =>
        {
            if (r.SourceFolder == a)
            {
                firstFolderStarted.Set();
                Thread.Sleep(400);
            }
            return Ok(r);
        };
        orchestrator.RunCompleted += _ =>
        {
            if (Interlocked.Increment(ref runs) >= 2) twoRunsDone.Set();
        };

        orchestrator.RequestRun(cfg, new UploadOrchestrator.RunOptions { Trigger = "manual" });
        Assert.True(firstFolderStarted.Wait(TimeSpan.FromSeconds(5)));

        orchestrator.RequestRun(cfg, new UploadOrchestrator.RunOptions
        {
            Trigger = "watcher",
            TargetFolders = new[] { b }
        });

        Assert.True(twoRunsDone.Wait(TimeSpan.FromSeconds(20)), "coalesced run never executed");
        Assert.Equal(2, runs);
    }

    [Fact]
    public void Cancel_StopsRunAndReportsCancelled()
    {
        using var temp = new TempFolder();
        var a = temp.CreateDir("a");
        var b = temp.CreateDir("b");
        var cfg = TestConfig.Create(a, b);
        cfg.MaxParallelProcesses = 2;

        var runner = new FakeRunner();
        var state = new UploadState();
        using var orchestrator = new UploadOrchestrator(runner, () => state, _ => { });

        var started = new ManualResetEventSlim(false);
        UploadOrchestrator.RunSummary? summary = null;
        var done = new ManualResetEventSlim(false);

        runner.Handler = r =>
        {
            started.Set();
            r.CancellationToken.WaitHandle.WaitOne(TimeSpan.FromSeconds(5));
            return new UploadResult
            {
                Success = false,
                ExitCode = -2,
                Message = "Cancelled",
                SourceFolder = r.SourceFolder,
                RunMode = r.RunMode
            };
        };
        orchestrator.RunCompleted += s => { summary = s; done.Set(); };

        orchestrator.RequestRun(cfg, new UploadOrchestrator.RunOptions { Trigger = "manual" });
        Assert.True(started.Wait(TimeSpan.FromSeconds(5)));

        orchestrator.Cancel();

        Assert.True(done.Wait(TimeSpan.FromSeconds(10)));
        Assert.True(summary!.Cancelled);
        Assert.Equal("cancelled", state.LastRunResult);
    }

    [Fact]
    public void Pause_BlocksNewFolderProcessesUntilResumed()
    {
        using var temp = new TempFolder();
        var a = temp.CreateDir("a");
        var cfg = TestConfig.Create(a);

        var runner = new FakeRunner();
        var state = new UploadState();
        using var orchestrator = new UploadOrchestrator(runner, () => state, _ => { });

        var done = new ManualResetEventSlim(false);
        orchestrator.RunCompleted += _ => done.Set();

        orchestrator.TogglePause();
        Assert.True(orchestrator.IsPaused);

        orchestrator.RequestRun(cfg, new UploadOrchestrator.RunOptions { Trigger = "manual" });
        Thread.Sleep(400);
        Assert.Equal(0, runner.CallCount);

        orchestrator.TogglePause();
        Assert.True(done.Wait(TimeSpan.FromSeconds(10)));
        Assert.Equal(1, runner.CallCount);
    }

    [Fact]
    public void MissingFolderIsCountedAsFailure()
    {
        var missing = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "definitely-missing-" + Guid.NewGuid().ToString("N"));
        var cfg = TestConfig.Create(missing);

        var runner = new FakeRunner();
        var state = new UploadState();
        using var orchestrator = new UploadOrchestrator(runner, () => state, _ => { });

        UploadOrchestrator.RunSummary? summary = null;
        var done = new ManualResetEventSlim(false);
        orchestrator.RunCompleted += s => { summary = s; done.Set(); };

        orchestrator.RequestRun(cfg, new UploadOrchestrator.RunOptions { Trigger = "manual" });

        Assert.True(done.Wait(TimeSpan.FromSeconds(10)));
        Assert.Equal(0, runner.CallCount);
        Assert.Equal(1, summary!.FailCount);
    }
}
