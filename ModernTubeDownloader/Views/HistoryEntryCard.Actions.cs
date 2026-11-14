using ModernFormsNext;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Theming;
using ModernTubeDownloader.Utilities;

namespace ModernTubeDownloader.Views;

internal sealed partial class HistoryEntryCard
{
    private readonly ContextMenu historyMenu = new();
    private Button? more;
    public event EventHandler<string>? RetryRequested;
    public event EventHandler<string>? CopySourceRequested;

    private void PrepareMoreAction()
    {
        if (more is not null) return;
        more = new DashboardButton { Text = "⋮", AccessibleName = text["Dashboard.MoreActions"] };
        more.Click += (_, _) => ShowHistoryMenu(); Controls.Add(more);
    }

    private void ShowHistoryMenu()
    {
        if (!dashboard || more is null) return;
        var target = entry;
        SelectionRequested?.Invoke(this, target);
        historyMenu.Items.Clear();
        void Add(string label, Action<DownloadHistoryEntry> run, bool enabled = true)
        {
            var item = historyMenu.Items.Add(text[label], null, (_, _) =>
            {
                // The pager reuses this row. A previously opened menu must never act on its next item.
                if (entry.QueueItemId == target.QueueItemId && entry.FinalPath == target.FinalPath) run(target);
            });
            item.Enabled = enabled;
        }
        Add("Common.OpenFile", value => ShellService.OpenFile(value.FinalPath), File.Exists(target.FinalPath));
        Add("Common.OpenFolder", value => ShellService.OpenFolderForFile(System.IO.Path.GetDirectoryName(value.FinalPath)), Directory.Exists(System.IO.Path.GetDirectoryName(target.FinalPath)));
        historyMenu.Items.Add(new MenuSeparatorItem());
        Add("Dashboard.CopyUrl", value => CopySourceRequested?.Invoke(this, value.SourceUrl), !string.IsNullOrWhiteSpace(target.SourceUrl));
        Add("Common.Retry", value => RetryRequested?.Invoke(this, value.SourceUrl), !string.IsNullOrWhiteSpace(target.SourceUrl));
        Application.RunOnUIThread(() => { if (!disposed && dashboard) historyMenu.Show(more, more.PointToScreen(new(0, more.Height + 4))); });
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!dashboard || e.Handled) return;
        if (e.KeyCode is Keys.Enter or Keys.Space)
        { e.Handled = e.SuppressKeyPress = true; SelectionRequested?.Invoke(this, entry); }
        else if (e.KeyCode == Keys.Apps || e.KeyCode == Keys.F10 && e.Shift)
        { e.Handled = e.SuppressKeyPress = true; ShowHistoryMenu(); }
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (dashboard && e.Button == MouseButtons.Right) ShowHistoryMenu();
    }
}
