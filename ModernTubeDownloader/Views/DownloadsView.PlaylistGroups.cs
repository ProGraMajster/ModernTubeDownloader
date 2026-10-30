using ModernFormsNext;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Theming;

namespace ModernTubeDownloader.Views;

internal sealed partial class DownloadsView
{
    private readonly HashSet<PlaylistGroupKey> expandedPlaylistGroups = [];
    private readonly Dictionary<PlaylistGroupKey, PlaylistGroupCard> groupCards = [];
    private PlaylistQueueProjection? playlistProjection;
    private bool projectedGrouping = true;
    private readonly ContextMenu groupMenu = new();
    private int DashboardPageCount => playlistProjection?.Pages.Count ?? 1;

    private void BuildGroupedQueueCards(Guid? reveal, bool rebuild)
    {
        var measurement = System.Diagnostics.Stopwatch.StartNew();
        var items = services.Queue.Snapshot();
        var grouping = AppearancePresentation.Current.ModernDashboardShell.GroupPlaylists;
        projectedGrouping = grouping;
        if (reveal is { } id && items.FirstOrDefault(item => item.Id == id) is { } target && PlaylistGroupKey.From(target) is { } key)
            expandedPlaylistGroups.Add(key);
        playlistProjection = PlaylistQueueProjection.Build(items, grouping, expandedPlaylistGroups);
        // Session state is bounded by the groups that still exist, not past imports.
        if (grouping) expandedPlaylistGroups.IntersectWith(playlistProjection.Groups.Keys);
        dashboardPage = Math.Clamp(reveal is { } job ? playlistProjection.PageOf(job) : dashboardPage, 0, DashboardPageCount - 1);
        var rows = playlistProjection.Pages[dashboardPage];
        var visibleIds = rows.Where(row => row.Item is not null).Select(row => row.Item!.Id).ToHashSet();
        var visibleGroups = rows.Where(row => row.Item is null).Select(row => row.Group!.Key).ToHashSet();
        if (selectedItemId is { } selected && items.All(item => item.Id != selected)) selectedItemId = null;
        var parent = queueHost.Parent;
        var position = parent?.Controls.GetChildIndex(queueHost) ?? 0;
        var projectionMs = measurement.Elapsed.TotalMilliseconds;
        double preparedMs = 0, attachedMs = 0;
        parent?.SuspendLayout(); queueHost.SuspendLayout();
        try
        {
            // A single completed subtree reaches native accessibility, rather than one
            // synchronous native structure scan per removed/inserted child.
            parent?.Controls.Remove(queueHost);
            foreach (var oldId in cards.Keys.Where(id => rebuild || !visibleIds.Contains(id)).ToArray())
            { var old = cards[oldId]; queueHost.Controls.Remove(old); old.Dispose(); cards.Remove(oldId); }
            foreach (var oldKey in groupCards.Keys.Where(key => rebuild || !visibleGroups.Contains(key)).ToArray())
            { var old = groupCards[oldKey]; queueHost.Controls.Remove(old); old.Dispose(); groupCards.Remove(oldKey); }
            foreach (var row in rows)
            {
                if (row.Item is { } item)
                {
                    if (!cards.TryGetValue(item.Id, out var card))
                    {
                        card = new QueueItemCard(services.Thumbnails, text); card.SetDashboard(true);
                        card.KeyDown += DashboardKeys; card.ActionRequested += CardActionRequested;
                        cards.Add(item.Id, card); queueHost.Controls.Add(card);
                    }
                    card.BringToFront(); card.UpdateItem(item, item.Id == selectedItemId);
                }
                else if (row.Group is { } group)
                {
                    if (!groupCards.TryGetValue(group.Key, out var card))
                    {
                        card = new PlaylistGroupCard(group, services.Thumbnails, text);
                        card.ToggleRequested += (_, _) => TogglePlaylistGroup(group.Key);
                        card.MenuRequested += (_, _) => ShowPlaylistGroupMenu(group.Key);
                        groupCards.Add(group.Key, card); queueHost.Controls.Add(card);
                    }
                    card.BringToFront(); card.Update(group, expandedPlaylistGroups.Contains(group.Key), selectedItemId);
                }
            }
            // Size and settle the new rows while the host is detached. Otherwise
            // each of forty width changes republishes the attached native subtree.
            ResizeCards();
        }
        finally
        {
            queueHost.ResumeLayout(true);
            queueHost.SuspendLayout();
            try { ResizeCards(); }
            finally { queueHost.ResumeLayout(true); }
            preparedMs = measurement.Elapsed.TotalMilliseconds;
            if (parent is not null) { parent.Controls.Add(queueHost); parent.Controls.SetChildIndex(queueHost, Math.Min(position, parent.Controls.Count - 1)); parent.ResumeLayout(true); }
            attachedMs = measurement.Elapsed.TotalMilliseconds;
        }
        queueBuilt = true; queueEmpty.Visible = items.Count == 0; queueHost.Visible = items.Count != 0;
        if (queueEmpty.Visible) queueEmpty.BringToFront(); else queueHost.BringToFront();
        ResizeCards(); queueHost.PerformLayout();
        if (reveal is { } revealed) ScrollQueueCardIntoView(revealed);
        BuildQueueMenu(); UpdateQueueSummary(); RefreshDashboardSelection(); UpdateLayout();
        UiCrashDiagnostics.Record("queue.groups.rebuild", "queueHost", queueHost.Controls.Count);
        services.Logger.Info($"Playlist queue projection: jobs={items.Count}, cards={cards.Count}, headers={groupCards.Count}, elapsedMs={measurement.Elapsed.TotalMilliseconds:F1}, projectionMs={projectionMs:F1}, prepareMs={preparedMs - projectionMs:F1}, attachMs={attachedMs - preparedMs:F1}, finishMs={measurement.Elapsed.TotalMilliseconds - attachedMs:F1}.");
    }
    private void TogglePlaylistGroup(PlaylistGroupKey key)
    {
        if (!expandedPlaylistGroups.Remove(key)) expandedPlaylistGroups.Add(key);
        BuildQueueCards();
    }
    private void RefreshPlaylistGroups()
    {
        if (!dashboard || groupCards.Count == 0) return;
        // One snapshot / linear membership pass per telemetry sample, no per-byte tree rebuild.
        var current = new Dictionary<PlaylistGroupKey, List<DownloadQueueItem>>();
        foreach (var item in services.Queue.Snapshot())
            if (PlaylistGroupKey.From(item) is { } key && groupCards.ContainsKey(key))
            {
                if (!current.TryGetValue(key, out var list)) current.Add(key, list = []);
                list.Add(item);
            }
        foreach (var (key, children) in current)
            groupCards[key].Update(new(key, children), expandedPlaylistGroups.Contains(key), selectedItemId);
    }
    private async Task TogglePlaylistGroupingAsync()
    {
        try
        {
            var profile = services.Appearance.Profile.Copy();
            profile.ModernDashboardShell.GroupPlaylists = !profile.ModernDashboardShell.GroupPlaylists;
            await services.Appearance.ApplyProfileAsync(profile);
            if (!disposed && projectedGrouping != profile.ModernDashboardShell.GroupPlaylists) BuildQueueCards();
        }
        catch (Exception error) { services.Logger.Error("Playlist grouping preference could not be saved.", error); }
    }
    private void ShowPlaylistGroupMenu(PlaylistGroupKey key)
    {
        if (!groupCards.TryGetValue(key, out var card)) return;
        var current = services.Queue.Snapshot().Where(item => PlaylistGroupKey.From(item) == key).ToArray();
        groupMenu.Items.Clear();
        groupMenu.Items.Add(text[expandedPlaylistGroups.Contains(key) ? "PlaylistGroup.Collapse" : "PlaylistGroup.Expand"], null, (_, _) => TogglePlaylistGroup(key));
        groupMenu.Items.Add(text["PlaylistGroup.GoToChild"], null, (_, _) =>
        {
            var target = current.FirstOrDefault(item => item.Id == selectedItemId) ?? current.FirstOrDefault();
            if (target is not null) RevealQueueItem(target.Id);
        }).Enabled = current.Length > 0;
        groupMenu.Items.Add(text["PlaylistGroup.RetryFailed"], null, async (_, _) => await RunPlaylistGroupAction(key, false))
            .Enabled = current.Any(item => item.Status is DownloadStatus.Failed or DownloadStatus.Interrupted or DownloadStatus.Partial);
        groupMenu.Items.Add(text["PlaylistGroup.RemoveCompleted"], null, async (_, _) => await RunPlaylistGroupAction(key, true))
            .Enabled = current.Any(item => item.Status == DownloadStatus.Completed);
        card.ShowMenu(groupMenu);
    }
    private async Task RunPlaylistGroupAction(PlaylistGroupKey key, bool remove)
    {
        if (remove && !await ConfirmPlaylistGroupRemoval()) return;
        // Freeze only the current eligible GUIDs. Revalidate membership and status again
        // before each canonical per-job operation; never broaden scope after an await.
        bool Eligible(DownloadQueueItem item) => remove ? item.Status == DownloadStatus.Completed : item.Status is DownloadStatus.Failed or DownloadStatus.Interrupted or DownloadStatus.Partial;
        var ids = services.Queue.Snapshot().Where(item => PlaylistGroupKey.From(item) == key && Eligible(item)).Select(item => item.Id).ToArray();
        var succeeded = 0;
        foreach (var id in ids)
            if (services.Queue.Find(id) is { } item && PlaylistGroupKey.From(item) == key && Eligible(item) &&
                (remove ? services.Queue.Remove(id) : services.Queue.Retry(id))) succeeded++;
        BuildQueueCards();
        using var result = new MessageBoxForm(text["PlaylistGroup.Actions"], text.Get("PlaylistGroup.ActionResult", succeeded, ids.Length - succeeded));
        if (FindForm() is { } owner) await result.ShowDialog(owner);
    }

    private async Task<bool> ConfirmPlaylistGroupRemoval()
    {
        if (FindForm() is not { } owner) return false;
        using var dialog = new MessageBoxForm(text["PlaylistGroup.Actions"], text["PlaylistGroup.RemoveConfirm"]);
        var buttons = dialog.Controls.OfType<Panel>().Single();
        foreach (var existing in buttons.Controls) { existing.Visible = false; existing.Enabled = false; }
        var cancel = new DashboardButton { Text = text["Common.Cancel"], AccessibleAutomationId = "PlaylistGroupRemoveCancel" };
        var confirm = new DashboardButton { Text = text["PlaylistGroup.RemoveCompleted"], AccessibleAutomationId = "PlaylistGroupRemoveConfirm" };
        AppUi.Secondary(cancel); AppUi.Primary(confirm);
        cancel.Click += (_, _) => dialog.DialogResult = DialogResult.Cancel;
        confirm.Click += (_, _) => dialog.DialogResult = DialogResult.Yes;
        buttons.Controls.AddRange([cancel, confirm]);
        AppTypography.SetDashboardPresentation(buttons, true);
        buttons.Height = AppTypography.TextHeight(48);
        var cancelWidth = DashboardVisual.ActionWidth(cancel);
        var confirmWidth = DashboardVisual.ActionWidth(confirm);
        dialog.Size = new System.Drawing.Size(Math.Max(500, cancelWidth + confirmWidth + 56), AppTypography.TextHeight(220));
        void ArrangeButtons()
        {
            var height = AppTypography.TextHeight(34);
            confirm.SetBounds(buttons.Width - confirmWidth - 12, 6, confirmWidth, height);
            cancel.SetBounds(confirm.Left - cancelWidth - 10, 6, cancelWidth, height);
        }
        buttons.SizeChanged += (_, _) => ArrangeButtons(); ArrangeButtons();
        dialog.Shown += (_, _) => cancel.Select();
        return await dialog.ShowDialog(owner) == DialogResult.Yes;
    }
}
