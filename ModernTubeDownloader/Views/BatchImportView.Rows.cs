using ModernFormsNext;
using ModernTubeDownloader.Batch;
using ModernTubeDownloader.Theming;

namespace ModernTubeDownloader.Views;

internal sealed partial class BatchImportView
{
    private readonly Dictionary<Guid, BatchRowPresenter> rowPresenters = [];
    private string rowShape = "";
    private sealed class BatchRowPresenter(BatchImportSession session, Guid id, Panel panel, CheckBox selection,
        Label title, Label url, Label status, Button[] actions, PictureBox? thumbnail)
    {
        public readonly BatchImportSession Session = session;
        public readonly Guid Id = id;
        public readonly Panel Panel = panel;
        public readonly CheckBox Selection = selection;
        public readonly Label Title = title, Url = url, Status = status;
        public readonly Button[] Actions = actions;
        public readonly PictureBox? Thumbnail = thumbnail;
        public long Revision = -1;
        public string Language = "";
        public BatchEntryStatus? LastStatus;
        public bool ThumbnailRequested;
    }

    private void RenderRows(BatchImportSession current, BatchPage data)
    {
        var appearance = AppearancePresentation.Current.BatchImport;
        var shape = $"{current.Id}/{dashboard}/{appearance.Thumbnail}/{appearance.ShowTitle}/{appearance.ShowSource}/{appearance.ShowUrl}/{appearance.ShowQuality}/{appearance.ShowContainer}/{appearance.StatusDetails}";
        var ids = data.Entries.Select(entry => entry.Id).ToHashSet();
        var geometryChanged = false;
        foreach (var (id, row) in rowPresenters.ToArray())
            if (rowShape != shape || !ids.Contains(id))
            {
                TraceBatchRow(row.Panel, false); rows.Controls.Remove(row.Panel); row.Panel.Dispose();
                rowPresenters.Remove(id); PresentationRowDisposedCount++; geometryChanged = true;
            }
        rowShape = shape;
        for (var index = 0; index < data.Entries.Count; index++)
        {
            var entry = data.Entries[index];
            if (!rowPresenters.TryGetValue(entry.Id, out var row))
            {
                row = CreateRow(current, entry); rowPresenters.Add(entry.Id, row);
                rows.Controls.Add(row.Panel);
                // Typography and button recipes resolve the actual presentation owner.
                // Attaching only the row's parent does not rebind its descendants.
                if (dashboard) PrepareDashboardRow(row.Panel, entry);
                TraceBatchRow(row.Panel, true);
                PresentationRowCreatedCount++; geometryChanged = true;
            }
            if (rows.Controls.IndexOf(row.Panel) != index) { rows.Controls.SetChildIndex(row.Panel, index); geometryChanged = true; }
            geometryChanged |= RefreshRow(row, entry);
        }
        if (geometryChanged) LayoutRows();
    }

    private BatchRowPresenter CreateRow(BatchImportSession current, BatchImportEntry entry)
    {
        var panel = new Panel { Height = AppTypography.TextHeight(106), Margin = new Padding(0, 0, 0, 6) }; AppUi.Card(panel);
        var checkbox = new CheckBox { Text = entry.Ordinal.ToString(), Checked = selection.Contains(entry.Id), AccessibleAutomationId = "BatchSelect" + entry.Ordinal };
        AppUi.BindForeground(checkbox);
        checkbox.CheckedChanged += (_, _) => { if (checkbox.Checked) selection.Add(entry.Id); else selection.Remove(entry.Id); };
        var title = AppUi.Heading("", 12); title.AccessibleAutomationId = "BatchEntryTitle" + entry.Ordinal;
        var url = AppUi.Muted(entry.Url); var state = AppUi.Muted(); state.AccessibleAutomationId = "BatchEntryStatus" + entry.Ordinal;
        var actions = Enumerable.Range(0, 4).Select(_ => (Button)new DashboardButton()).ToArray();
        foreach (var button in actions) AppUi.Secondary(button);
        panel.Controls.AddRange([checkbox, title, url, state, .. actions]);
        PictureBox? thumbnail = null;
        if (AppearancePresentation.Current.BatchImport.Thumbnail)
        { thumbnail = new PictureBox { SizeMode = PictureBoxSizeMode.Zoom, Tag = "BatchThumbnail" }; panel.Controls.Add(thumbnail); }
        var row = new BatchRowPresenter(current, entry.Id, panel, checkbox, title, url, state, actions, thumbnail);
        // Resolve actions by durable identity; changing status never replaces controls
        // or captures an obsolete entry snapshot in a newly subscribed callback.
        actions[0].Click += (_, _) => _ = RunAsync(async () =>
        {
            var ids = new HashSet<Guid> { row.Id };
            await services.Batch.RetryFailedAsync(row.Session, ids, OperationToken);
            _ = ObserveAnalysis(services.Batch.StartAsync(row.Session, ids));
        });
        actions[1].Click += (_, _) => _ = RunAsync(async () => await services.Batch.AddReadyAsync(row.Session, new HashSet<Guid> { row.Id }, OperationToken));
        actions[2].Click += (_, _) => _ = RunAsync(async () =>
        {
            if (await services.Batch.MetadataAsync(row.Session, row.Id, lifetime.Token) is not { } metadata) return;
            using var window = new VideoDetailsForm(metadata, services.Localization, services.Thumbnails);
            using var registration = RegisterDialog?.Invoke(window); await window.ShowDialog(FindForm()!);
        });
        actions[3].Click += (_, _) => _ = RunAsync(() => services.Batch.EditAsync(row.Session, new HashSet<Guid> { row.Id }, remove: true));
        return row;
    }

    private bool RefreshRow(BatchRowPresenter row, BatchImportEntry entry)
    {
        var t = services.Localization; var appearance = AppearancePresentation.Current.BatchImport;
        row.Selection.Checked = selection.Contains(entry.Id);
        var retry = entry.QueueItemId is not null && entry.Status == BatchEntryStatus.Failed;
        row.Actions[0].Enabled = !busy && !services.Batch.IsOperating && (entry.QueueItemId is null || retry);
        row.Actions[1].Enabled = !busy && !services.Batch.IsOperating && entry.Status == BatchEntryStatus.Ready;
        row.Actions[2].Enabled = !busy && entry.HasMetadata;
        row.Actions[3].Enabled = !busy && entry.QueueItemId is null && entry.Status is not (BatchEntryStatus.Analyzing or BatchEntryStatus.Validating);
        if (row.Revision == entry.Revision && row.Language == t.Language) return false;
        var beforeTitle = row.Title.Text; var languageChanged = row.Language != t.Language;
        var beforeQuality = (row.Panel.Tag as DashboardBatchRow)?.Quality.Text;
        row.Title.Visible = appearance.ShowTitle || appearance.ShowSource; row.Url.Visible = appearance.ShowUrl;
        row.Title.Text = dashboard
            ? appearance.ShowTitle && !string.IsNullOrWhiteSpace(entry.Title) ? entry.Title : appearance.ShowSource ? entry.Source : ""
            : (appearance.ShowTitle ? entry.Title : "") + (appearance.ShowSource ? " • " + entry.Source : "");
        var labels = new[] { t[retry ? "Common.Retry" : "Batch.Analyze"], t["Batch.Add"], t["Common.Details"], t["Batch.Remove"] };
        for (var index = 0; index < labels.Length; index++)
        {
            row.Actions[index].AccessibleName = labels[index];
            if (dashboard) dashboardTips.SetToolTip(row.Actions[index], labels[index]);
            else row.Actions[index].Text = labels[index];
        }
        if (row.Panel.Tag is DashboardBatchRow modern)
        {
            modern.Status.Text = t["Batch.Status." + entry.Status];
            modern.Quality.Text = string.Join(" · ", new[] { appearance.ShowQuality ? t["Quality." + entry.Quality] : null,
                appearance.ShowContainer ? t["Container." + entry.Container] : null }.Where(value => !string.IsNullOrEmpty(value)));
            var detail = modern.Status.Text + " · " + modern.Quality.Text + " " + entry.Range.DisplayText;
            if (appearance.StatusDetails && entry.ErrorKey is { } error) detail += " · " + t[error];
            modern.Status.AccessibleName = detail; dashboardTips.SetToolTip(modern.Status, detail);
            dashboardTips.SetToolTip(modern.Quality, modern.Quality.Text); dashboardTips.SetToolTip(modern.Title, modern.Title.Text);
            if (row.LastStatus != entry.Status) AppUi.BindForeground(modern.Status, BatchStatusToken(entry.Status));
        }
        else row.Status.Text = t["Batch.Status." + entry.Status] + (appearance.ShowQuality ? " • " + entry.Quality : "") +
            (appearance.ShowContainer ? " • " + entry.Container : "") + " " + entry.Range.DisplayText +
            (appearance.StatusDetails && entry.ErrorKey is { } key ? " • " + t[key] : "");
        if (row.Thumbnail is { } image && entry.HasMetadata && !row.ThumbnailRequested)
        { row.ThumbnailRequested = true; _ = LoadThumbnailAsync(row.Session, row.Id, image); }
        row.Revision = entry.Revision; row.Language = t.Language; row.LastStatus = entry.Status;
        // Only text capable of wrapping changes row geometry. Status/progress does not.
        return languageChanged || beforeTitle != row.Title.Text || beforeQuality != (row.Panel.Tag as DashboardBatchRow)?.Quality.Text;
    }
}
