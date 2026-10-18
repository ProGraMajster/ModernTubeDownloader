using ModernFormsNext;
using ModernTubeDownloader.Batch;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Theming;

namespace ModernTubeDownloader.Views;

/// <summary>A bounded page of controls, irrespective of session size. Closing the window does not stop preparation.</summary>
internal sealed class BatchImportForm : Form
{
    private readonly AppServices services;
    private readonly AppScrollPanel body = new() { Dock = DockStyle.Fill, AutoScroll = true };
    private readonly FlowLayoutPanel toolbar = new() { WrapContents = true };
    private readonly AppScrollFlowLayoutPanel rows = new() { AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, AccessibleAutomationId = "BatchRows" };
    private readonly Label heading = AppUi.Heading("", 22), status = AppUi.Muted(), pageText = AppUi.Muted();
    private readonly TextBox path = new() { AccessibleAutomationId = "BatchFilePath" }, search = new() { AccessibleAutomationId = "BatchSearch" };
    private readonly ComboBox quality = new() { AccessibleAutomationId = "BatchQuality" }, container = new() { AccessibleAutomationId = "BatchContainer" }, filter = new() { AccessibleAutomationId = "BatchFilter" };
    private readonly NumericUpDown workers = new() { Minimum = 1, Maximum = 8, Value = 1, AccessibleAutomationId = "BatchWorkers" };
    private readonly ProgressBar progress = new() { Minimum = 0, Maximum = 100, AccessibleAutomationId = "BatchAnalysisProgress" };
    private readonly ProgressBar downloadProgress = new() { Minimum = 0, Maximum = 100, AccessibleAutomationId = "BatchDownloadProgress" };
    private readonly Label overall = AppUi.Muted();
    private readonly ToolTip overallTip = new() { InitialDelay = 350, AutoPopDelay = 15000 };
    private readonly MediaRangeEditor range;
    private readonly Dictionary<string, Button> buttons = [];
    private readonly HashSet<Guid> selection = [];
    private readonly ModernFormsNext.Timer timer = new() { Interval = 1000 };
    private readonly CancellationTokenSource lifetime = new();
    private BatchImportSession? session;
    private int page;
    private bool busy, disposed;
    private string signature = "";
    private string? lastErrorKey;
    private volatile BatchOperationProgress? operation;
    private int operationRevision;
    private CancellationTokenSource? operationCancellation;
    private CancellationToken OperationToken => operationCancellation?.Token ?? lifetime.Token;
    public Func<Form, IDisposable?>? RegisterDialog { get; set; }
    public BatchImportForm(AppServices services, BatchImportSession? session = null)
    {
        this.services = services; this.session = session;
        workers.Value = session?.AnalysisWorkers ?? 1;
        AccessibilityObject.AutomationId = "BatchWindow"; AppBranding.Apply(this);
        Size = new System.Drawing.Size(1420, 950); MinimumSize = new System.Drawing.Size(1050, 740);
        heading.AccessibleAutomationId = "BatchTitle"; status.AccessibleAutomationId = "BatchStatus";
        range = new(services.Localization, "Batch") { Width = 500 };
        foreach (var control in new Control[] { path, search, quality, container, filter, workers }) AppUi.Input(control);
        AppUi.BindBackground(body); AppUi.BindBackground(toolbar); AppUi.BindBackground(rows);
        foreach (var preset in QualityPreset.All) quality.Items.Add(services.Localization["Quality." + preset.Id]);
        quality.SelectedIndex = 0;
        foreach (var value in Enum.GetValues<PreferredVideoContainer>()) container.Items.Add(value.ToString()); container.SelectedIndex = 0;
        foreach (var value in Enum.GetValues<BatchFilter>()) filter.Items.Add(services.Localization["Batch.Filter." + value]); filter.SelectedIndex = 0;
        void Add(string key, Action action) { var button = new Button { Width = 160, Height = 38, Margin = new Padding(0, 0, 8, 8), AccessibleAutomationId = "Batch" + key }; AppUi.Secondary(button); button.Click += (_, _) => action(); toolbar.Controls.Add(button); buttons.Add(key, button); }
        Add("Browse", () => _ = RunAsync(BrowseAsync)); Add("Import", () => _ = RunAsync(ImportAsync));
        Add("Examples", () => _ = RunAsync(ShowExamplesAsync));
        Add("Analyze", () => _ = RunAsync(AnalyzeAsync)); Add("Pause", () => _ = RunAsync(async () => { if (this.session is { } s) await services.Batch.SetPausedAsync(s, !s.Paused); }));
        Add("Cancel", () => { if (busy) operationCancellation?.Cancel(); else _ = RunAsync(services.Batch.CancelAsync); });
        Add("Add", () => _ = RunAsync(async () => { if (this.session is { } s) await services.Batch.AddReadyAsync(s, ActionSelection(s), OperationToken, OperationProgress()); }));
        Add("Export", () => _ = RunAsync(ExportAsync)); Add("SelectPage", () => { if (this.session is { } s) foreach (var entry in CurrentPage(s).Entries) selection.Add(entry.Id); signature = ""; Render(); });
        Add("ClearSelection", () => { selection.Clear(); signature = ""; Render(); });
        Add("Apply", () => _ = RunAsync(async () => { if (this.session is { } s) { if (!range.TryGetRange(null, false, out var value, out var error)) throw new InvalidDataException(error); await services.Batch.EditAsync(s, ActionSelection(s).Intersect(selection).ToHashSet(), QualityPreset.All[quality.SelectedIndex].Id, (PreferredVideoContainer)container.SelectedIndex, value); } }));
        Add("Remove", () => _ = RunAsync(async () => { if (this.session is { } s) await services.Batch.EditAsync(s, ActionSelection(s).Intersect(selection).ToHashSet(), remove: true); selection.Clear(); }));
        Add("Previous", () => { page--; signature = ""; Render(); }); Add("Next", () => { page++; signature = ""; Render(); });
        Add("Close", Close);
        overall.Multiline = true; overall.AccessibleAutomationId = "BatchOverallSummary";
        body.Controls.AddRange([heading, path, quality, container, workers, toolbar, filter, search, range, status, progress, downloadProgress, overall, rows, pageText]); Controls.Add(body);
        search.TextChanged += (_, _) => { page = 0; signature = ""; Render(); }; filter.SelectedIndexChanged += (_, _) => { page = 0; signature = ""; Render(); };
        SizeChanged += (_, _) => LayoutPage(); services.Localization.LanguageChanged += LanguageChanged;
        timer.Tick += async (_, _) => { if (!busy && this.session is { } s) await RunAsync(async () => await services.Batch.RefreshQueueAsync(s), false); else Render(); };
        Shown += (_, _) => timer.Start(); ApplyText(); LayoutPage(); Render();
        AppTypography.RegisterLayout(body, () => { signature = ""; LayoutPage(); Render(); });
    }
    private BatchPage CurrentPage(BatchImportSession value) => services.Batch.Page(value, (BatchFilter)Math.Max(0, filter.SelectedIndex), search.Text, page);
    private IReadOnlySet<Guid> ActionSelection(BatchImportSession value)
        => services.Batch.ActionSelection(value, (BatchFilter)Math.Max(0, filter.SelectedIndex), search.Text, selection);
    private IProgress<BatchOperationProgress> OperationProgress()
    {
        var revision = Volatile.Read(ref operationRevision);
        // The existing one-second UI timer presents the newest immutable value;
        // importing 250k rows must not enqueue thousands of dispatcher callbacks.
        return new OperationProgressSink(value => { if (revision == Volatile.Read(ref operationRevision)) operation = value; });
    }
    private sealed class OperationProgressSink(Action<BatchOperationProgress> report) : IProgress<BatchOperationProgress>
    { public void Report(BatchOperationProgress value) => report(value); }
    private void ApplyText()
    {
        Text = heading.Text = services.Localization["Other.Batch.Title"]; path.Placeholder = services.Localization["Batch.Path"];
        overallTip.SetToolTip(overall, services.Localization["Batch.Estimates.Help"]);
        search.Placeholder = services.Localization["Batch.Search"];
        foreach (var (key, button) in buttons) button.Text = services.Localization["Batch." + key];
        var qualityIndex = quality.SelectedIndex; quality.Items.Clear(); foreach (var preset in QualityPreset.All) quality.Items.Add(services.Localization["Quality." + preset.Id]); quality.SelectedIndex = Math.Max(0, qualityIndex);
        var containerIndex = container.SelectedIndex; container.Items.Clear(); foreach (var value in Enum.GetValues<PreferredVideoContainer>()) container.Items.Add(services.Localization["Container." + value]); container.SelectedIndex = Math.Max(0, containerIndex);
        var index = filter.SelectedIndex; filter.Items.Clear(); foreach (var value in Enum.GetValues<BatchFilter>()) filter.Items.Add(services.Localization["Batch.Filter." + value]); filter.SelectedIndex = Math.Max(0, index);
    }
    private async Task RunAsync(Func<Task> action, bool showError = true)
    {
        if (disposed || busy) return; busy = true; Interlocked.Increment(ref operationRevision); operation = null;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        operationCancellation = cancellation; Render();
        try { await action(); if (showError) { signature = ""; lastErrorKey = null; } }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            services.Logger.Error("Batch UI action failed.", ex);
            if (!disposed && showError)
            {
                lastErrorKey = ex is BatchJsonFormatException ? "Batch.Error.JsonFormat" :
                    ex is InvalidDataException && ex.Message.StartsWith("Batch.") ? ex.Message : "Batch.Error.Analysis";
                signature = "";
            }
        }
        finally { Interlocked.Increment(ref operationRevision); busy = false; operation = null; operationCancellation = null; if (!disposed) { Render(); LayoutPage(); } }
    }
    private async Task BrowseAsync()
    {
        var dialog = new OpenFileDialog(); dialog.AddFilter("TXT / CSV / JSON", "*.txt", "*.csv", "*.json");
        if (await dialog.ShowDialog(this) == DialogResult.OK && dialog.FileName is { } file) path.Text = file;
    }
    private async Task ShowExamplesAsync()
    {
        using var dialog = new Form { Text = services.Localization["Batch.Examples"], Size = new System.Drawing.Size(700, 570), MinimumSize = new System.Drawing.Size(520, 400) };
        dialog.AccessibilityObject.AutomationId = "BatchExamplesWindow";
        AppBranding.Apply(dialog);
        var scroll = new ScrollableControl { Dock = DockStyle.Fill, AutoScroll = true };
        AppUi.BindBackground(scroll);
        var help = AppUi.Muted(services.Localization["Batch.Examples.Help"] + "\n\n" + BatchJsonExamples.Text);
        help.Multiline = true; help.Dock = DockStyle.Top; help.Height = AppTypography.TextHeight(740); help.Padding = new Padding(16);
        help.AccessibleAutomationId = "BatchExamplesText";
        var close = new Button { Text = services.Localization["Common.Close"], Dock = DockStyle.Bottom,
            Height = AppTypography.TextHeight(40), AccessibleAutomationId = "BatchExamplesClose" };
        AppUi.Secondary(close);
        close.Click += (_, _) => dialog.Close();
        scroll.Controls.Add(help); dialog.Controls.AddRange([scroll, close]);
        using var registration = RegisterDialog?.Invoke(dialog);
        await dialog.ShowDialog(this);
    }
    private async Task ImportAsync()
    {
        session = await services.Batch.ImportAsync(path.Text, QualityPreset.All[quality.SelectedIndex].Id,
            (PreferredVideoContainer)container.SelectedIndex, (int)workers.Value, OperationToken, OperationProgress());
        selection.Clear(); page = 0;
    }
    private Task AnalyzeAsync()
    {
        if (session is not { } s) return Task.CompletedTask;
        s.AnalysisWorkers = (int)workers.Value;
        // Service owns this task after the window closes; observe every failure.
        _ = ObserveAnalysis(services.Batch.StartAsync(s, ActionSelection(s)));
        return Task.CompletedTask;
    }
    private async Task ObserveAnalysis(Task task)
    { try { await task; } catch (Exception ex) { services.Logger.Error("Batch preparation stopped.", ex); } }
    private async Task ExportAsync()
    {
        if (session is not { } s) return;
        var dialog = new SaveFileDialog { DefaultExtension = "json", FileName = "batch-export.json" }; dialog.AddFilter("JSON", "*.json"); dialog.AddFilter("CSV", "*.csv");
        if (await dialog.ShowDialog(this) == DialogResult.OK) await services.Batch.ExportAsync(s, dialog.FileName, lifetime.Token);
    }
    private void Render()
    {
        if (disposed) return;
        foreach (var (key, button) in buttons) button.Enabled = !busy && (session is not null || key is "Browse" or "Import" or "Examples" or "Close");
        buttons["Cancel"].Enabled = busy || services.Batch.IsAnalyzing;
        buttons["Close"].Enabled = true;
        buttons["Analyze"].Enabled &= !services.Batch.IsAnalyzing; buttons["Pause"].Enabled &= services.Batch.IsAnalyzing;
        if (session is not { } s)
        {
            status.Text = operation is { } active ? services.Localization.Get("Batch.Operation." + active.Phase, active.Completed, active.Total?.ToString() ?? "—") :
                services.Localization[lastErrorKey ?? (busy ? "Batch.Busy" : "Batch.Empty")];
            overall.Text = services.Localization[busy ? "Batch.Busy" : "Batch.Empty"];
            return;
        }
        var data = CurrentPage(s); page = data.Page; pageText.Text = services.Localization.Get("Sources.Page", page + 1, data.Pages, data.Total);
        progress.Visible = AppearancePresentation.Current.BatchImport.Progress != ProgressPresentation.Hidden;
        lock (s.Sync) progress.Value = s.ImportedCount == 0 ? 0 : Math.Clamp((int)(100L * s.Entries.Count(e => e.Status is not (BatchEntryStatus.Pending or BatchEntryStatus.Analyzing or BatchEntryStatus.Validating)) / s.ImportedCount), 0, 100);
        if (operation is { } op) progress.Value = op.Total is > 0 ? (int)(100L * op.Completed / op.Total.Value) : 0;
        buttons["Previous"].Enabled &= page > 0; buttons["Next"].Enabled &= page < data.Pages - 1;
        status.Text = lastErrorKey is { } importErrorKey ? services.Localization[importErrorKey] : services.Localization.Get("Batch.Summary", s.FileName, s.ImportedCount, selection.Count,
            services.Localization[s.Paused ? "Batch.Paused" : "Batch.Running"]);
        if (operation is { } current) status.Text = services.Localization.Get("Batch.Operation." + current.Phase, current.Completed, current.Total?.ToString() ?? "—");
        var snapshots = services.Queue.ProgressSnapshot().ToDictionary(item => item.Id);
        QueueProgressItem[] linked; long knownBytes = 0; int unknown = 0;
        lock (s.Sync)
        {
            linked = s.Entries.Where(entry => !entry.Removed && entry.QueueItemId is not null && snapshots.ContainsKey(entry.QueueItemId.Value))
                .Select(entry => snapshots[entry.QueueItemId!.Value]).ToArray();
            foreach (var entry in s.Entries.Where(entry => !entry.Removed))
                if (entry.EstimatedSizeBytes is > 0 && knownBytes <= long.MaxValue - entry.EstimatedSizeBytes.Value) knownBytes += entry.EstimatedSizeBytes.Value; else unknown++;
        }
        var summary = QueueProgressSummary.Build(linked, services.Queue.IsPaused);
        downloadProgress.Value = (int)summary.Percent;
        overall.Text = services.Localization.Get("Batch.Overall", summary.Settled, summary.Total, summary.Completed, summary.Failed,
            (knownBytes / 1073741824d).ToString("N2", services.Localization.Culture), unknown,
            summary.TransferRemaining is { } eta ? eta.ToString(@"d\.hh\:mm\:ss") : "—");
        buttons["Pause"].Text = services.Localization[s.Paused ? "Batch.Resume" : "Batch.Pause"];
        var nextSignature = string.Join(';', data.Entries.Select(entry => $"{entry.Id}/{entry.Status}/{entry.Quality}/{entry.Container}/{entry.Title}/{entry.Range}"));
        if (signature == nextSignature) return; signature = nextSignature;
        foreach (var child in rows.Controls.ToArray()) { rows.Controls.Remove(child); child.Dispose(); }
        foreach (var entry in data.Entries)
        {
            var appearance = AppearancePresentation.Current.BatchImport;
            var panel = new Panel { Height = AppTypography.TextHeight(106), Margin = new Padding(0, 0, 0, 6) }; AppUi.Card(panel);
            var checkbox = new CheckBox { Text = entry.Ordinal.ToString(), Checked = selection.Contains(entry.Id), AccessibleAutomationId = "BatchSelect" + entry.Ordinal };
            AppUi.BindForeground(checkbox); checkbox.CheckedChanged += (_, _) => { if (checkbox.Checked) selection.Add(entry.Id); else selection.Remove(entry.Id); };
            var title = AppUi.Heading((appearance.ShowTitle ? entry.Title : "") + (appearance.ShowSource ? " • " + entry.Source : ""), 12); title.Visible = appearance.ShowTitle || appearance.ShowSource; title.AccessibleAutomationId = "BatchEntryTitle" + entry.Ordinal;
            var url = AppUi.Muted(entry.Url); url.Visible = appearance.ShowUrl;
            var state = AppUi.Muted(services.Localization["Batch.Status." + entry.Status] + (appearance.ShowQuality ? " • " + entry.Quality : "") + (appearance.ShowContainer ? " • " + entry.Container : "") + " " + entry.Range.DisplayText + (appearance.StatusDetails && entry.ErrorKey is { } errorKey ? " • " + services.Localization[errorKey] : ""));
            state.AccessibleAutomationId = "BatchEntryStatus" + entry.Ordinal;
            var analyze = new Button { Text = services.Localization["Batch.Analyze"], Enabled = !services.Batch.IsAnalyzing && entry.QueueItemId is null };
            var add = new Button { Text = services.Localization["Batch.Add"], Enabled = entry.Status == BatchEntryStatus.Ready };
            var details = new Button { Text = services.Localization["Downloads.Details"], Enabled = entry.HasMetadata };
            var remove = new Button { Text = services.Localization["Batch.Remove"], Enabled = entry.QueueItemId is null && entry.Status != BatchEntryStatus.Analyzing };
            foreach (var button in new[] { analyze, add, details, remove }) AppUi.Secondary(button);
            analyze.Click += (_, _) => _ = RunAsync(() => { _ = ObserveAnalysis(services.Batch.StartAsync(s, new HashSet<Guid> { entry.Id })); return Task.CompletedTask; });
            add.Click += (_, _) => _ = RunAsync(async () => await services.Batch.AddReadyAsync(s, new HashSet<Guid> { entry.Id }));
            remove.Click += (_, _) => _ = RunAsync(() => services.Batch.EditAsync(s, new HashSet<Guid> { entry.Id }, remove: true));
            details.Click += (_, _) => _ = RunAsync(async () => { if (await services.Batch.MetadataAsync(s, entry.Id, lifetime.Token) is { } metadata) { using var window = new VideoDetailsForm(metadata, services.Localization, services.Thumbnails); using var registration = RegisterDialog?.Invoke(window); await window.ShowDialog(this); } });
            panel.Controls.AddRange([checkbox, title, url, state, analyze, add, details, remove]); rows.Controls.Add(panel);
            if (appearance.Thumbnail && entry.HasMetadata) { var image = new PictureBox { SizeMode = PictureBoxSizeMode.Zoom, Tag = "BatchThumbnail" }; panel.Controls.Add(image); _ = LoadThumbnailAsync(s, entry.Id, image); }
        }
        LayoutRows();
    }
    private void LayoutRows()
    {
        foreach (var panel in rows.Controls)
        {
            panel.Width = Math.Max(930, rows.DisplayRectangle.Width - 4); var c = panel.Controls.ToArray();
            var density = AppearancePresentation.Current.BatchImport.RowDensity;
            var gap = density == AppearanceDensity.Compact ? 2 : density == AppearanceDensity.Comfortable ? 10 : 6;
            panel.Height = AppTypography.TextHeight(106);
            c[0].SetBounds(12, 12, 70, 30); c[1].SetBounds(88, 8, panel.Width - 100, 28); c[2].SetBounds(88, 36, Math.Min(AppearancePresentation.Current.BatchImport.UrlColumnWidth, panel.Width - 100), 22);
            c[3].SetBounds(88, 68, panel.Width - 650, 30);
            for (var index = 4; index < 8; index++) c[index].SetBounds(panel.Width - 550 + (index - 4) * 134, 66, 126, 32);
            if (c.Length > 8) { c[8].SetBounds(panel.Width - 100, 8, 80, 45); c[1].Width -= 90; c[2].Width = Math.Min(c[2].Width, panel.Width - 190); }
            AppTypography.ReflowRows(panel);
            if (AppTypography.Scale > 1.25 || density != AppearanceDensity.Normal)
            {
                c[3].SetBounds(88, c[2].Visible ? c[2].Bottom + gap : c[1].Bottom + gap, panel.Width - 100, AppTypography.TextHeight(30));
                var columnWidth = Math.Max(140, (panel.Width - 36) / 2); var top = c[3].Bottom + gap;
                for (var index = 4; index < 8; index++) c[index].SetBounds(12 + ((index - 4) % 2) * (columnWidth + 6), top + ((index - 4) / 2) * (AppTypography.TextHeight(32) + gap), columnWidth, AppTypography.TextHeight(32));
                panel.Height = c[7].Bottom + 10;
            }
        }
    }
    private async Task LoadThumbnailAsync(BatchImportSession value, Guid id, PictureBox image)
    {
        try { if (await services.Batch.MetadataAsync(value, id, lifetime.Token) is { } metadata) { var bitmap = await services.Thumbnails.GetAsync(metadata.ThumbnailUrl, lifetime.Token); if (!disposed && image.Parent is not null) image.Image = bitmap; } }
        catch (OperationCanceledException) { }
        catch (Exception ex) { services.Logger.Error("Batch thumbnail unavailable.", ex); }
    }
    private void LayoutPage()
    {
        var width = Math.Max(970, body.ClientSize.Width - 40); var origin = body.DisplayRectangle.Location;
        heading.SetBounds(20, 12, width, 38); path.SetBounds(20, 58, width - 426, 36);
        quality.SetBounds(path.Right + 10, 58, 160, 36); container.SetBounds(quality.Right + 10, 58, 150, 36); workers.SetBounds(container.Right + 10, 58, 86, 36);
        toolbar.SetBounds(20, 104, width, 142); filter.SetBounds(20, 252, 170, 36); search.SetBounds(200, 252, width - 710, 36); range.SetBounds(width - 500, 250, 520, 100);
        var statusHeight = lastErrorKey is null ? 44 : 100;
        status.SetBounds(20, 300, width - 540, statusHeight); status.Multiline = true;
        overall.SetBounds(20, Math.Max(350, status.Bottom + 8), width, AppTypography.TextHeight(68));
        downloadProgress.SetBounds(20, overall.Bottom + 4, width, 6);
        var rowsTop = downloadProgress.Bottom + 16;
        rows.SetBounds(20, rowsTop, width, Math.Max(160, body.ClientSize.Height - rowsTop - 52)); pageText.SetBounds(20, rows.Bottom + 6, width, 32); LayoutRows();
        if (AppTypography.Scale > 1) {
            heading.Height = AppTypography.TextHeight(38); path.Top = quality.Top = container.Top = workers.Top = heading.Bottom + 8;
            foreach (var input in new Control[] { path, quality, container, workers }) input.Height = AppTypography.TextHeight(36);
            foreach (var button in buttons.Values) { button.Height = AppTypography.TextHeight(38); button.Width = Math.Min(width, AppTypography.TextHeight(160)); }
            toolbar.Top = path.Bottom + 10; toolbar.Height = (int)Math.Ceiling(buttons.Count / (double)Math.Max(1, width / (Math.Min(width, AppTypography.TextHeight(160)) + 8))) * (AppTypography.TextHeight(38) + 8);
            filter.Top = search.Top = toolbar.Bottom + 8; filter.Height = search.Height = AppTypography.TextHeight(36);
            range.SetBounds(20, filter.Bottom + 8, width, AppTypography.TextHeight(100)); status.SetBounds(20, range.Bottom + 8, width, AppTypography.TextHeight(statusHeight));
            overall.SetBounds(20, status.Bottom + 8, width, AppTypography.TextHeight(68));
            downloadProgress.SetBounds(20, overall.Bottom + 4, width, 6);
            rows.Top = downloadProgress.Bottom + 16; rows.Height = Math.Max(180, body.ClientSize.Height - rows.Top - 54); pageText.Top = rows.Bottom + 8; pageText.Height = AppTypography.TextHeight(32);
        }
        progress.SetBounds(20, overall.Top - 6, width, AppearancePresentation.Current.BatchImport.Progress == ProgressPresentation.Compact ? 3 : 5);
        foreach (var child in body.Controls) { child.Left += origin.X; child.Top += origin.Y; }
    }
    private void LanguageChanged(object? sender, EventArgs e) => Application.RunOnUIThread(() => { if (!disposed) { ApplyText(); signature = ""; Render(); LayoutPage(); } });
    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposed) { disposed = true; lifetime.Cancel(); timer.Stop(); timer.Dispose(); overallTip.Dispose(); services.Localization.LanguageChanged -= LanguageChanged; lifetime.Dispose(); }
        base.Dispose(disposing);
    }
}
