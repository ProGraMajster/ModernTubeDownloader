using ModernFormsNext;
using ModernFormsNext.Testing;
using ModernTubeDownloader.Theming;
using ModernTubeDownloader.Views;
using Path = System.IO.Path;

namespace ModernTubeDownloader.Tests;

[Collection("ModernFormsNext TestHost")]
public sealed class TypographyPresentationMutationTests
{
    [Fact]
    public void PresentationRefreshSurvivesReentrantSiblingReplacementAndSkipsDetachedSubtrees()
    {
        using var host = ModernFormsTestHost.Create();
        using var root = new Panel();
        using var elsewhere = new Panel();
        var trigger = new Label(); var removed = new Label(); var moved = new Panel();
        var movedChild = new Label(); var survivor = new Label(); var replacement = new Label();
        foreach (var control in new Control[] { trigger, removed, movedChild, survivor, replacement }) AppTypography.Bind(control, 10);
        moved.Controls.Add(movedChild); root.Controls.AddRange([trigger, removed, moved, survivor]);
        AppTypography.SetDashboardPresentation(root, true);
        // The classic root owns detached subtrees independently of the root being refreshed.
        elsewhere.Controls.Add(new Label());
        var mutated = false; var staleChanges = 0; var removedDisposed = false;
        removed.Disposed += (_, _) => removedDisposed = true;
        trigger.FontChanged += (_, _) =>
        {
            if (mutated) return;
            mutated = true;
            root.Controls.Remove(removed); removed.Dispose();
            elsewhere.Controls.Add(moved);
            // ParentChanged on the panel does not recursively bind its existing descendants.
            // A traversal of the old root must not subsequently update this child.
            movedChild.FontChanged += (_, _) => staleChanges++;
            root.Controls.Add(replacement);
        };

        AppTypography.SetDashboardPresentation(root, false);

        Assert.True(mutated); Assert.True(removedDisposed);
        Assert.Same(elsewhere, moved.Parent); Assert.Equal(0, staleChanges);
        Assert.Equal(10f, survivor.Font.SizeInPoints);
        Assert.Equal(10f, replacement.Font.SizeInPoints);
        Assert.Equal(14f, movedChild.Font.SizeInPoints);
    }
}

public sealed partial class BatchDashboardPresentationTests
{
    [Fact]
    public async Task RecentToolRefreshPublishesOnlyOnUiDispatcher()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mtd-recent-dispatch-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var services = await CreateServicesAsync(directory);
            var session = await services.Batch.ImportTextAsync("https://example.test/one");
            using (var host = ModernFormsTestHost.Create())
            {
                services.Appearance.Initialize();
                var view = new OtherView(services); view.RegisterTool("Batch", () => { });
                AppTypography.SetDashboardPresentation(view, true); view.SetDashboard(true); host.Show(view, 1320, 1024);
                var initial = view.RefreshToolsAsync();
                PumpUntil(host, () => initial.IsCompleted && Descendants(view).OfType<Button>().Any(button => button.AccessibleAutomationId == "BatchRecent" + session.Id.ToString("N")));
                await initial;
                var recent = Find<Button>(view, "BatchRecent" + session.Id.ToString("N")).Parent!;
                var uiThread = Environment.CurrentManagedThreadId;
                var mutationThreads = new List<int>();
                recent.ControlAdded += (_, _) => mutationThreads.Add(Environment.CurrentManagedThreadId);
                recent.ControlRemoved += (_, _) => mutationThreads.Add(Environment.CurrentManagedThreadId);
                // The I/O completion can arrive without a UI SynchronizationContext.
                var refresh = Task.Run(view.RefreshToolsAsync);
                PumpUntil(host, () => refresh.IsCompleted); await refresh;
                Assert.NotEmpty(mutationThreads);
                Assert.All(mutationThreads, thread => Assert.Equal(uiThread, thread));
                Assert.Contains(session.FileName, Find<Button>(view, "BatchRecent" + session.Id.ToString("N")).AccessibleName);
                Assert.Empty(services.Queue.Snapshot());
            }
            await services.ShutdownAsync();
        }
        finally { AppearancePresentation.Current = new(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
