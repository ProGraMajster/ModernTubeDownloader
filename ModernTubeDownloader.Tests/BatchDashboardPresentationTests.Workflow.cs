using ModernFormsNext;
using Path = System.IO.Path;
using ModernFormsNext.Testing;
using ModernTubeDownloader.Batch;
using ModernTubeDownloader.Theming;
using ModernTubeDownloader.Views;

namespace ModernTubeDownloader.Tests;

public sealed partial class BatchDashboardPresentationTests
{
    [Theory]
    [InlineData(1320, 100)]
    [InlineData(1060, 150)]
    [InlineData(1700, 200)]
    public async Task WorkflowDestinationAndStagedProgressRemainBoundedAcrossShells(int width, int scale)
    {
        var directory = Path.Combine(Path.GetTempPath(), "mtd-batch-workflow-view-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var services = await CreateServicesAsync(directory);
            var session = await services.Batch.ImportTextAsync("https://example.test/a\nhttps://example.test/b");
            using (var host = ModernFormsTestHost.Create())
            {
                services.Appearance.Initialize(); AppTypography.SetScale(scale);
                var view = new BatchImportView(services, session); view.SetDashboard(true); host.Show(view, width, 1024); host.ProcessPendingWork();
                Assert.Same(session, view.CurrentSession);
                Assert.True(Find<Button>(view, "BatchAutomatic").Visible);
                Assert.True(Find<Button>(view, "BatchOrganize").Visible);
                Assert.Equal(0, Find<ComboBox>(view, "BatchWorkflowMode").SelectedIndex);
                Assert.True(Find<CheckBox>(view, "BatchListSubfolder").Checked);
                Assert.Equal(session.OutputRoot, Find<TextBox>(view, "BatchDestinationRoot").Text);
                Assert.Equal(session.ListName, Find<TextBox>(view, "BatchListName").Text);
                Assert.Equal(services.Localization["Batch.Workflow.Prepared"], Find<Label>(view, "BatchWorkflowStage").Text);
                foreach (var id in new[] { "BatchDestinationCard", "BatchWorkflowProgress" })
                {
                    var panel = Find<Panel>(view, id);
                    Assert.All(panel.Controls.Where(control => control.Visible && control.Height > 0), control =>
                    {
                        Assert.True(control.Left >= 0 && control.Top >= 0 && control.Width > 0);
                        Assert.True(control.Right <= panel.Width && control.Bottom <= panel.Height,
                            $"{control.AccessibleAutomationId} escapes {id} at {scale}%.");
                    });
                }
                var organizers = 0; view.OrganizeRequested += (_, _) => organizers++;
                Find<Button>(view, "BatchOrganize").PerformClick(); Assert.Equal(1, organizers);
                view.SetDashboard(false); host.ProcessPendingWork();
                Assert.True(Find<Button>(view, "BatchAutomatic").Visible);
                Assert.Equal(session.OutputRoot, Find<TextBox>(view, "BatchDestinationRoot").Text);
                view.SetDashboard(true); host.ProcessPendingWork();
                Assert.Equal(2, Find<FlowLayoutPanel>(view, "BatchRows").Controls.Count);
                Assert.Empty(services.Queue.Snapshot());
            }
            await services.ShutdownAsync();
        }
        finally { AppTypography.SetScale(100); AppearancePresentation.Current = new(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
