using ModernFormsNext;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Theming;
using ModernTubeDownloader.Utilities;

namespace ModernTubeDownloader.Views;

/// <summary>A read-only presentation of one canonical recording, with commands resolved at click time.</summary>
internal sealed class LiveSessionDetailsForm : Form
{
    private readonly AppServices services;
    private readonly Guid sessionId;
    private LiveSession snapshot;
    private readonly Panel surface = new() { Dock = DockStyle.Fill };
    private readonly AppScrollPanel content = new() { AutoScroll = true, AccessibleAutomationId = "LiveDetailsContent" };
    private readonly Panel header = new(), recording = new(), diagnostics = new(), files = new(), footer = new();
    private readonly Label title = AppUi.Heading("", 18), channel = AppUi.Muted(), state = AppUi.Muted();
    private readonly Label recordingHeading = AppUi.Heading("", 12), diagnosticsHeading = AppUi.Heading("", 12), filesHeading = AppUi.Heading("", 12);
    private readonly DashboardFields recordingFields = new(), diagnosticsFields = new();
    private readonly TextBox source = new() { ReadOnly = true, AccessibleAutomationId = "LiveDetailsSource" };
    private readonly TextBox output = new() { ReadOnly = true, MultiLine = true, ScrollBars = ScrollBars.Vertical, AccessibleAutomationId = "LiveDetailsParts" };
    private readonly Label warning = AppUi.Muted();
    private readonly DownloadsPictureBox image = new() { SizeMode = PictureBoxSizeMode.Zoom, RoundedPreview = true };
    private readonly AppVectorIcon placeholder = new(AppIconKind.Live, 38, AppThemeTokens.Info);
    private readonly Button close = new DashboardButton { AccessibleAutomationId = "LiveDetailsClose" };
    private readonly Button action = new DashboardButton { AccessibleAutomationId = "LiveDetailsAction" };
    private readonly Button openFile = new DashboardButton { AccessibleAutomationId = "LiveDetailsOpenFile" };
    private readonly Button openFolder = new DashboardButton { AccessibleAutomationId = "LiveDetailsOpenFolder" };
    private readonly Button copy = new DashboardButton { AccessibleAutomationId = "LiveDetailsCopyUrl" };
    private readonly CancellationTokenSource lifetime = new();
    private readonly System.Threading.Timer clock;
    private bool disposed, arranging;
    private int refreshPending;
    private string? thumbnailUrl;

    public LiveSessionDetailsForm(AppServices services, LiveSession session)
    {
        this.services = services; snapshot = session.Copy(); sessionId = session.Id;
        Text = services.Localization["Live.Details"];
        Size = new System.Drawing.Size(900, 720); MinimumSize = new System.Drawing.Size(540, 420);
        AccessibilityObject.AutomationId = "LiveDetailsWindow";
        AppBranding.Apply(this); AppUi.BindBackground(surface); AppUi.BindBackground(content);
        AppTypography.SetDashboardPresentation(surface, true); Controls.Add(surface);
        foreach (var card in new[] { header, recording, diagnostics, files }) DownloadsVisual.Card(card);
        title.Multiline = true; title.AutoEllipsis = true; channel.AutoEllipsis = true;
        state.AutoEllipsis = true; warning.Multiline = true;
        AppUi.BindForeground(warning, AppThemeTokens.Warning);
        foreach (var input in new[] { source, output }) AppUi.Input(input);
        header.Controls.AddRange([image, placeholder, title, channel, state, source]);
        recording.Controls.AddRange([recordingHeading, recordingFields]); diagnostics.Controls.AddRange([diagnosticsHeading, diagnosticsFields]);
        files.Controls.AddRange([filesHeading, output, warning]);
        content.Controls.AddRange([header, recording, diagnostics, files]);
        footer.Controls.AddRange([action, openFile, openFolder, copy, close]); surface.Controls.AddRange([content, footer]);
        AppUi.BindBackground(footer, AppThemeTokens.Surface);
        AppTypography.CaptureRecipes(surface).Invoke();
        close.Click += (_, _) => Close();
        action.Click += (_, _) =>
        {
            if (services.Live.Find(sessionId) is not { } current) return;
            if (current.IsActive && current.State != LiveSessionState.Finalizing) services.Live.StopAndSave(sessionId);
            else if (current.CanResume) services.Live.Resume(sessionId);
            RefreshSession();
        };
        openFile.Click += (_, _) => ShellService.OpenFile((Current() ?? snapshot).Parts.LastOrDefault());
        openFolder.Click += (_, _) =>
        {
            var current = Current() ?? snapshot;
            ShellService.OpenFolderForFile(current.Parts.LastOrDefault() is { } part ? System.IO.Path.GetDirectoryName(part) : current.WorkspacePath);
        };
        copy.Click += async (_, _) =>
        {
            try { await Clipboard.SetTextAsync(snapshot.SourceUrl); }
            catch (Exception error) { services.Logger.Error("LIVE details URL copy failed.", error); }
        };
        services.Live.Changed += SessionChanged; services.Localization.LanguageChanged += SessionChanged;
        clock = new System.Threading.Timer(_ => QueueRefresh(), null, 1000, 1000);
        SizeChanged += (_, _) => LayoutContent(); content.SizeChanged += (_, _) => LayoutContent();
        AppTypography.RegisterLayout(surface, () => { RefreshSession(); LayoutContent(); });
        RefreshSession();
    }

    private LiveSession? Current() => services.Live.Find(sessionId);
    private void SessionChanged(object? sender, EventArgs args) => QueueRefresh();
    private void QueueRefresh()
    {
        if (disposed || Interlocked.Exchange(ref refreshPending, 1) != 0) return;
        Application.RunOnUIThread(() => { Interlocked.Exchange(ref refreshPending, 0); if (!disposed && Visible) RefreshSession(); });
    }
    private void RefreshSession()
    {
        var current = Current(); if (current is not null) snapshot = current;
        var t = services.Localization; Text = t["Live.Details"];
        title.Text = snapshot.Title; channel.Text = snapshot.Channel; source.Text = snapshot.SourceUrl;
        state.Text = t["Live.State." + snapshot.State];
        AppUi.BindForeground(state, snapshot.State is LiveSessionState.Completed or LiveSessionState.Recording ? AppThemeTokens.Success :
            snapshot.State == LiveSessionState.Failed ? AppThemeTokens.Error : AppThemeTokens.Info);
        recordingHeading.Text = t["Live.RecordingDetails"]; diagnosticsHeading.Text = t["Live.ConnectionDetails"]; filesHeading.Text = t["Live.SavedFiles"];
        recordingFields.SetRows((t["Dashboard.Format"], snapshot.Container.ToString().ToUpperInvariant()),
            (t["Downloads.Quality"], t["Quality." + snapshot.QualityPresetId]),
            (t["Live.StartMode"], t[snapshot.StartPolicy == LiveStartPolicy.FromStart ? "Live.FromStart" : "Live.FromNow"]),
            (t["Live.ScheduledLabel"], snapshot.ScheduledStartAt?.ToLocalTime().ToString("g", t.Culture)),
            (t["Live.StartedLabel"], snapshot.RecordingStartedAt?.ToLocalTime().ToString("g", t.Culture)),
            (t["Dashboard.Duration"], snapshot.RecordedDuration.ToString(@"hh\:mm\:ss")),
            (t["Dashboard.Size"], DashboardStyle.Bytes(snapshot.BytesWritten)));
        diagnosticsFields.SetRows((t["Live.SourceState"], snapshot.SourceLiveStatus ?? "—"),
            (t["Live.Parts"], snapshot.Parts.Count.ToString(t.Culture)), (t["Live.CurrentPart"], snapshot.PartIndex.ToString(t.Culture)),
            (t["Live.ReconnectCount"], snapshot.ResumeCount.ToString(t.Culture)),
            (t["Live.NextCheckAt"], (snapshot.NextCheckAt ?? snapshot.RetryAt)?.ToLocalTime().ToString("g", t.Culture)));
        var paths = snapshot.Parts.Count == 0 ? snapshot.WorkspacePath ?? t["Live.NoSavedFiles"] : string.Join(Environment.NewLine, snapshot.Parts);
        if (output.Text != paths) output.Text = paths;
        warning.Text = string.Join("\n", new[] { snapshot.FailureMessageKey, snapshot.RecoveryWarningKey }.Where(key => key is not null).Select(key => t[key!]));
        close.Text = t["Common.Close"]; copy.Text = t["Dashboard.CopyUrl"]; openFile.Text = t["Common.OpenFile"]; openFolder.Text = t["Common.OpenFolder"];
        action.Text = t[snapshot.IsActive ? "Live.StopAndSave" : "Live.Resume"];
        action.Visible = snapshot.IsActive || snapshot.CanResume; action.Enabled = current is not null && snapshot.State != LiveSessionState.Finalizing;
        openFile.Enabled = File.Exists(snapshot.Parts.LastOrDefault());
        var folder = snapshot.Parts.LastOrDefault() is { } part ? System.IO.Path.GetDirectoryName(part) : snapshot.WorkspacePath;
        openFolder.Enabled = Directory.Exists(folder); copy.Enabled = snapshot.SourceUrl.Length > 0;
        if (thumbnailUrl != snapshot.ThumbnailUrl)
        {
            thumbnailUrl = snapshot.ThumbnailUrl; var expected = thumbnailUrl;
            _ = ThumbnailImageBinding.For(image).LoadAsync(services.Thumbnails, expected, lifetime.Token,
                LayoutContent, () => !disposed && thumbnailUrl == expected);
        }
        LayoutContent();
    }

    private void LayoutContent()
    {
        if (arranging || disposed) return; arranging = true;
        try
        {
            for (var pass = 0; pass < 3; pass++)
            {
                var viewportWidth = content.DisplayRectangle.Width;
                ArrangeContent();
                if (content.DisplayRectangle.Width == viewportWidth) break;
            }
        }
        finally { arranging = false; }
    }

    private void ArrangeContent()
    {
            var available = Math.Max(1, ClientSize.Width - 32); var line = AppTypography.TextHeight(36); var x = 16; var y = 12;
            foreach (var button in new[] { action, openFile, openFolder, copy, close })
            {
                if (!button.Visible) { button.SetBounds(0, 0, 0, 0); continue; }
                DownloadsVisual.Button(button, button == close || button == action && snapshot.CanResume);
                var w = Math.Min(available, DashboardVisual.ActionWidth(button));
                if (x > 16 && x + w > ClientSize.Width - 16) { x = 16; y += line + 8; }
                button.SetBounds(x, y, w, line); x += w + 8;
            }
            footer.SetBounds(0, Math.Max(0, ClientSize.Height - y - line - 12), ClientSize.Width, y + line + 12);
            content.SetBounds(0, 0, ClientSize.Width, Math.Max(1, footer.Top));
            var o = content.DisplayRectangle.Location; var width = Math.Max(1, content.DisplayRectangle.Width - 32);
            var inset = Math.Max(1, width - 28); var imageWidth = width >= AppTypography.TextHeight(560) ? 184 : 108;
            header.SetBounds(16 + o.X, 16 + o.Y, width, 1);
            image.SetBounds(14, 14, imageWidth, imageWidth * 9 / 16); placeholder.Visible = image.Image is null;
            placeholder.SetBounds(image.Left + (image.Width - 38) / 2, image.Top + (image.Height - 38) / 2, 38, 38);
            var left = image.Right + 16; var textWidth = Math.Max(1, width - left - 14);
            title.SetBounds(left, 14, textWidth, DashboardVisual.LabelHeight(title, textWidth, 3));
            channel.SetBounds(left, title.Bottom + 5, textWidth, AppTypography.TextHeight(24));
            state.SetBounds(left, channel.Bottom + 4, textWidth, AppTypography.TextHeight(24));
            source.SetBounds(14, Math.Max(image.Bottom, state.Bottom) + 12, inset, line); header.Height = source.Bottom + 14;
            var paired = width >= AppTypography.TextHeight(680); var cardWidth = paired ? (width - 12) / 2 : width;
            recording.SetBounds(header.Left, header.Bottom + 12, cardWidth, 1);
            recordingHeading.SetBounds(14, 12, cardWidth - 28, AppTypography.TextHeight(28));
            recordingFields.SetBounds(14, recordingHeading.Bottom + 8, cardWidth - 28, recordingFields.Height); recording.Height = recordingFields.Bottom + 14;
            diagnostics.SetBounds(paired ? recording.Right + 12 : header.Left, paired ? recording.Top : recording.Bottom + 12, paired ? width - cardWidth - 12 : width, 1);
            diagnosticsHeading.SetBounds(14, 12, diagnostics.Width - 28, AppTypography.TextHeight(28));
            diagnosticsFields.SetBounds(14, diagnosticsHeading.Bottom + 8, diagnostics.Width - 28, diagnosticsFields.Height); diagnostics.Height = diagnosticsFields.Bottom + 14;
            files.SetBounds(header.Left, Math.Max(recording.Bottom, diagnostics.Bottom) + 12, width, 1);
            filesHeading.SetBounds(14, 12, inset, AppTypography.TextHeight(28));
            output.SetBounds(14, filesHeading.Bottom + 8, inset, AppTypography.TextHeight(snapshot.Parts.Count > 2 ? 120 : 68));
            warning.Visible = warning.Text.Length > 0;
            warning.SetBounds(14, output.Bottom + 8, inset, warning.Visible ? DashboardVisual.LabelHeight(warning, inset, 6) : 0);
            files.Height = (warning.Visible ? warning.Bottom : output.Bottom) + 14;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    { base.OnKeyDown(e); if (e.KeyCode == Keys.Escape) { e.Handled = e.SuppressKeyPress = true; Close(); } }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposed)
        {
            disposed = true; clock.Dispose(); lifetime.Cancel(); ThumbnailImageBinding.For(image).Dispose(); lifetime.Dispose();
            services.Live.Changed -= SessionChanged; services.Localization.LanguageChanged -= SessionChanged;
        }
        base.Dispose(disposing);
    }
}
