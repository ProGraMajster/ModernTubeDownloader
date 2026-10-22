using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;

namespace ModernTubeDownloader.Tests;

public sealed class DownloadProgressDispatchTests
{
    [Theory]
    [InlineData(DownloadStatus.DownloadingVideo)]
    [InlineData(DownloadStatus.DownloadingAudio)]
    public void DownloadProgressIsAppliedBeforePhaseCompletionNotPostedAfterInterruption(DownloadStatus phase)
    {
        var context = new DeferredContext();
        var item = new DownloadQueueItem { Status = phase };
        var updates = 0;
        var progress = WithContext(context, () => DownloadJobExecutor.CreateDownloadProgress((change, _) =>
        {
            change(item);
            updates++;
        }, phase, "Downloading"));

        progress.Report(new(75, 75, 100, 25, TimeSpan.FromSeconds(1), "downloading"));
        var appliedBeforeCompletion = updates;
        item.Status = DownloadStatus.Interrupted;
        item.StatusMessageKey = "Queue.Detail.StoragePaused";
        item.SpeedBytesPerSecond = null;
        item.Eta = null;
        context.Drain(); // Deterministically deliver anything posted by the old Progress<T> implementation.

        Assert.Equal(DownloadStatus.Interrupted, item.Status);
        Assert.Equal("Queue.Detail.StoragePaused", item.StatusMessageKey);
        Assert.Null(item.SpeedBytesPerSecond);
        Assert.Null(item.Eta);
        Assert.Equal(1, appliedBeforeCompletion);
        Assert.Equal(0, context.Posts);
        Assert.Equal(75, item.ProgressPercent);
    }

    [Theory]
    [InlineData(DownloadStatus.Completed)]
    [InlineData(DownloadStatus.Interrupted)]
    public void MergeProgressCannotArriveAfterTerminalState(DownloadStatus terminal)
    {
        var context = new DeferredContext();
        var item = new DownloadQueueItem { Status = DownloadStatus.Merging };
        var progress = WithContext(context, () => DownloadJobExecutor.CreateMergeProgress((change, _) => change(item)));
        progress.Report(75);
        var appliedBeforeCompletion = item.ProgressPercent;
        item.Status = terminal;
        item.ProgressPercent = terminal == DownloadStatus.Completed ? 100 : 0;
        context.Drain();

        Assert.Equal(terminal, item.Status);
        Assert.Equal(terminal == DownloadStatus.Completed ? 100 : 0, item.ProgressPercent);
        Assert.Equal(75, appliedBeforeCompletion);
        Assert.Equal(0, context.Posts);
    }

    private static T WithContext<T>(SynchronizationContext context, Func<T> create)
    {
        var previous = SynchronizationContext.Current;
        try { SynchronizationContext.SetSynchronizationContext(context); return create(); }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    private sealed class DeferredContext : SynchronizationContext
    {
        private readonly Queue<Action> callbacks = new();
        public int Posts { get; private set; }
        public override void Post(SendOrPostCallback callback, object? state)
        {
            Posts++;
            callbacks.Enqueue(() => callback(state));
        }
        public void Drain()
        {
            while (callbacks.TryDequeue(out var callback)) callback();
        }
    }
}
