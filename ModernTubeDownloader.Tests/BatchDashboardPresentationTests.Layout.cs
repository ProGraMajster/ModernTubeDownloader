using ModernFormsNext;
using ModernFormsNext.Testing;
using ModernTubeDownloader.Batch;
using ModernTubeDownloader.Theming;
using ModernTubeDownloader.Views;
using Path = System.IO.Path;

namespace ModernTubeDownloader.Tests;

public sealed partial class BatchDashboardPresentationTests
{
    [Theory]
    [InlineData(100)]
    [InlineData(200)]
    public async Task RetainedRowActionIconsFitAndPaintAfterFirstLayoutAndResize(int scale)
    {
        var directory = Path.Combine(Path.GetTempPath(), "mtd-batch-row-icons-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var services = await CreateServicesAsync(directory);
            var session = await services.Batch.ImportTextAsync("https://example.test/watch/visible-icons");
            using (var host = ModernFormsTestHost.Create())
            {
                services.Appearance.Initialize(); AppTypography.SetScale(scale);
                var view = new BatchImportView(services, session); view.SetDashboard(true);
                var window = host.Show(view, 1600, scale == 100 ? 1200 : 2400); host.ProcessPendingWork();
                var actions = new[] { "Analyze", "Add", "Details", "Remove" }.Select(action => Find<Button>(view, "BatchEntry" + action + "1")).ToArray();
                var icons = actions.Select(button => Assert.Single(button.Controls.OfType<AppVectorIcon>())).ToArray();
                AssertIconBounds();
                if (scale == 100)
                {
                    using var visible = window.CaptureRenderedSnapshot();
                    foreach (var icon in icons) icon.Visible = false;
                    using var hidden = window.CaptureRenderedSnapshot();
                    Assert.False(visible.CopyPixels().SequenceEqual(hidden.CopyPixels()),
                        "The first frame must paint the action icons, not merely create offscreen children.");
                    foreach (var icon in icons) icon.Visible = true;
                }
                foreach (var width in new[] { 900, 1800, 1600 })
                {
                    host.Resize(width, scale == 100 ? 1200 : 2400); host.ProcessPendingWork();
                    AssertIconBounds();
                }
                host.Dispatcher.ThrowUnhandledExceptions();

                void AssertIconBounds()
                {
                    for (var index = 0; index < actions.Length; index++)
                    {
                        var action = actions[index]; var icon = icons[index];
                        Assert.Same(icon, Assert.Single(action.Controls.OfType<AppVectorIcon>()));
                        Assert.True(icon.Visible && icon.Width > 0 && icon.Height > 0);
                        Assert.Equal((action.Width - icon.Width) / 2, icon.Left);
                        Assert.Equal((action.Height - icon.Height) / 2, icon.Top);
                        Assert.True(icon.Left >= 0 && icon.Top >= 0 && icon.Right <= action.Width && icon.Bottom <= action.Height);
                        Assert.False(string.IsNullOrWhiteSpace(action.AccessibleName));
                    }
                }
            }
            await services.ShutdownAsync();
        }
        finally { AppTypography.SetScale(100); AppearancePresentation.Current = new(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData("pl", 100)]
    [InlineData("en", 100)]
    [InlineData("de-DE", 100)]
    [InlineData("ja-JP", 100)]
    [InlineData("pl", 150)]
    [InlineData("en", 150)]
    [InlineData("de-DE", 150)]
    [InlineData("ja-JP", 150)]
    [InlineData("pl", 200)]
    [InlineData("en", 200)]
    [InlineData("de-DE", 200)]
    [InlineData("ja-JP", 200)]
    public async Task ParsedListKeepsLabeledFieldsAndEveryWorkflowMessageReadable(string language, int scale)
    {
        var directory = Path.Combine(Path.GetTempPath(), "mtd-batch-labelled-layout-" + Guid.NewGuid().ToString("N"));
        var previousCulture = System.Globalization.CultureInfo.CurrentUICulture;
        try
        {
            using var services = await CreateServicesAsync(directory);
            services.Localization.SetLanguage(language);
            var urls = Enumerable.Range(1, 118).Select(index => "https://example.test/watch/item" + index).ToArray();
            var session = await services.Batch.ImportTextAsync(string.Join('\n', urls.Concat(urls.Take(4))));
            session.FileName = "youtube-wiadomosci-prywatne-2026-10-16T15-23-59-449Z-INCOMPLETE.txt";
            var identities = session.Snapshot().Select(entry => entry.Id).ToArray();
            Assert.Equal(118, session.Snapshot().Count(entry => entry.Status == BatchEntryStatus.Pending));
            Assert.Equal(4, session.Snapshot().Count(entry => entry.Status == BatchEntryStatus.Duplicate));
            using (var host = ModernFormsTestHost.Create())
            {
                services.Appearance.Initialize(); AppTypography.SetScale(scale);
                var view = new BatchImportView(services, session); view.SetDashboard(true);
                // The real 1650px shell leaves a 212px or 66px sidebar. Test its
                // available content width, not an unrealistically full-width view.
                host.Show(view, scale == 100 ? 1438 : 1584, 830); host.ProcessPendingWork();
                var destination = Find<Panel>(view, "BatchDestinationCard");
                var workflow = Find<Panel>(view, "BatchWorkflowProgress");
                var fields = new Control[] { Find<ComboBox>(view, "BatchWorkflowMode"), Find<ComboBox>(view, "BatchSessionChoice"),
                    Find<TextBox>(view, "BatchListName"), Find<TextBox>(view, "BatchDestinationRoot") };
                for (var index = 0; index < fields.Length; index++)
                {
                    var caption = Find<Label>(view, "BatchDestinationLabel" + index);
                    Assert.True(caption.Visible && caption.Text.Length > 0);
                    Assert.Equal(fields[index].Left, caption.Left);
                    Assert.True(caption.Bottom <= fields[index].Top);
                    AssertFullyMeasured(caption);
                }
                Assert.All(fields.GroupBy(field => field.Top), row => Assert.InRange(row.Count(), 1, 2));
                foreach (var card in new[] { destination, workflow })
                    Assert.All(card.Controls.Where(control => control.Visible && control.Height > 0), control =>
                    {
                        Assert.True(control.Left >= 0 && control.Top >= 0 && control.Width > 0);
                        Assert.True(control.Right <= card.Width && control.Bottom <= card.Height,
                            $"{control.AccessibleAutomationId} escapes its card at {language}/{scale}%.");
                    });
                for (var index = 1; index <= 5; index++)
                {
                    var caption = Find<Label>(view, "BatchWorkflowCaption" + index);
                    Assert.Equal(services.Localization["Batch.Workflow.Step" + index], caption.AccessibleName);
                    Assert.Contains(services.Localization["Batch.Workflow.Caption" + index], caption.Text);
                    AssertFullyMeasured(caption);
                }
                var fileName = Find<Label>(view, "BatchSourceFileName");
                Assert.Equal(session.FileName, fileName.AccessibleName);
                Assert.EndsWith("INCOMPLETE.txt", fileName.Text);
                Assert.True(DashboardVisual.TextWidth(fileName, fileName.Text) <= fileName.Width);
                var stage = Find<Label>(view, "BatchWorkflowStage");
                var facts = Find<Label>(view, "BatchWorkflowFacts");
                var current = Find<Label>(view, "BatchWorkflowCurrent");
                var bounds = new[] { workflow.Bounds, stage.Bounds, facts.Bounds, current.Bounds };
                foreach (var phase in Enum.GetValues<BatchWorkflowPhase>())
                {
                    stage.Text = services.Localization["Batch.Workflow." + phase];
                    host.ProcessPendingWork();
                    Assert.False(stage.AutoEllipsis); AssertFullyMeasured(stage);
                    Assert.Equal(bounds, new[] { workflow.Bounds, stage.Bounds, facts.Bounds, current.Bounds });
                }
                facts.Text = services.Localization.Get("Batch.Workflow.Facts", int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue);
                AssertFullyMeasured(facts);
                var body = Assert.IsType<AppScrollPanel>(destination.Parent);
                Assert.All(body.Controls.Where(control => control.Visible && control.Width > 0), control =>
                    Assert.True(control.Right - body.DisplayRectangle.X <= body.ClientSize.Width,
                        $"{control.AccessibleAutomationId} causes horizontal page overflow at {language}/{scale}%"));
                Assert.Equal(identities, session.Snapshot().Select(entry => entry.Id));
                Assert.Empty(services.Queue.Snapshot());
                host.Dispatcher.ThrowUnhandledExceptions();
            }
            await services.ShutdownAsync();
        }
        finally { System.Globalization.CultureInfo.CurrentUICulture = previousCulture; AppTypography.SetScale(100); AppearancePresentation.Current = new(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private static void AssertFullyMeasured(Label label)
    {
        var style = label.CurrentStyle;
        var measured = TextMeasurer.MeasureText(label.Text, style.GetFont(), style.GetFontSize(),
            new System.Drawing.Size(Math.Max(1, label.Width), int.MaxValue), AppTypography.TextStyle(label));
        Assert.True(label.Height >= Math.Ceiling(measured.Height), $"{label.AccessibleAutomationId}: {label.Text} requires {measured.Height}px, has {label.Height}px.");
    }
}
