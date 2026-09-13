using ImmichUploader;
using Xunit;

namespace ImmichUploader.Tests;

public class DebounceQueueTests
{
    [Fact]
    public void BatchesWithinFixedWindow()
    {
        var batches = new List<IReadOnlyList<string>>();
        var signal = new ManualResetEventSlim(false);
        using var queue = new DebounceFileQueue(1, files =>
        {
            lock (batches) batches.Add(files);
            signal.Set();
        });

        queue.Add("a.jpg");
        Thread.Sleep(150);      // still inside the fixed window
        queue.Add("b.jpg");

        Assert.True(signal.Wait(TimeSpan.FromSeconds(5)), "debounce callback did not fire");
        lock (batches)
        {
            Assert.Single(batches);
            Assert.Equal(2, batches[0].Count);
        }
    }

    [Fact]
    public void DuplicateAddsAreSuppressed_CaseInsensitively()
    {
        using var queue = new DebounceFileQueue(60, _ => { });

        queue.Add("a.jpg");
        queue.Add("a.jpg");
        queue.Add("A.JPG");

        Assert.Equal(1, queue.Count);
    }

    [Fact]
    public void FlushDrainsImmediately()
    {
        using var queue = new DebounceFileQueue(60, _ => { });

        queue.Add("a.jpg");
        queue.Add("b.jpg");
        var files = queue.Flush();

        Assert.Equal(2, files.Count);
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public void ShutdownClearsAndRejectsFurtherAdds()
    {
        using var queue = new DebounceFileQueue(60, _ => { });
        queue.Add("a.jpg");

        queue.Shutdown();

        Assert.Equal(0, queue.Count);
        Assert.True(queue.IsShutdown);
        queue.Add("b.jpg");
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public void ResetReArmsQueue()
    {
        using var queue = new DebounceFileQueue(60, _ => { });
        queue.Shutdown();

        queue.Reset(1);

        Assert.False(queue.IsShutdown);
        queue.Add("a.jpg");
        Assert.Equal(1, queue.Count);
    }

    [Fact]
    public void StaleTimerAfterResetDoesNotFire()
    {
        var fired = 0;
        using var queue = new DebounceFileQueue(1, _ => Interlocked.Increment(ref fired));

        queue.Add("a.jpg");
        queue.Reset(60);   // invalidate the pending 1s timer
        Thread.Sleep(1500);

        Assert.Equal(0, Volatile.Read(ref fired));
    }
}
