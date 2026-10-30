using ModernFormsNext;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Theming;

namespace ModernTubeDownloader.Views;

internal sealed class ThumbnailToolForm : Form
{
    private readonly AppServices services;
    private readonly AppScrollPanel body = new() { Dock = DockStyle.Fill, AutoScroll = true };
    private readonly Panel previewCard = new();
    private readonly Label heading = AppUi.Heading("", 20), description = AppUi.Muted(), status = AppUi.Muted(), imageTitle = AppUi.Heading("", 12), dimensions = AppUi.Muted();
    private readonly TextBox source = new() { AccessibleAutomationId = "ThumbnailSource" };
    private readonly PictureBox image = new() { SizeMode = PictureBoxSizeMode.Zoom, AccessibleAutomationId = "ThumbnailPreviewImage" };
    private readonly Button load = new DashboardButton { AccessibleAutomationId = "ThumbnailLoad" }, cancel = new DashboardButton { AccessibleAutomationId = "ThumbnailCancel" },
        png = new DashboardButton { AccessibleAutomationId = "ThumbnailSavePng" }, jpeg = new DashboardButton { AccessibleAutomationId = "ThumbnailSaveJpeg" }, close = new DashboardButton { AccessibleAutomationId = "ThumbnailClose" };
    private CancellationTokenSource? operation;
    private ThumbnailPreview? preview;
    private bool closeRequested, disposed;
    private string statusKey = "ThumbnailTool.Idle";

    public ThumbnailToolForm(AppServices services, bool modern)
    {
        this.services = services;
        Size = new(960, 760); MinimumSize = new(700, 580);
        AccessibilityObject.AutomationId = "ThumbnailToolWindow"; AppBranding.Apply(this);
        description.Multiline = status.Multiline = imageTitle.Multiline = true;
        status.AccessibleAutomationId = "ThumbnailStatus"; imageTitle.AccessibleAutomationId = "ThumbnailTitle";
        AppUi.Input(source); AppUi.BindBackground(body); DashboardStyle.Card(previewCard);
        AppUi.BindBackground(image, AppThemeTokens.Background);
        foreach (var button in new[] { load, cancel, png, jpeg, close }) AppUi.Secondary(button);
        AppUi.Primary(load); png.Enabled = jpeg.Enabled = cancel.Enabled = false;
        previewCard.Controls.AddRange([image, imageTitle, dimensions]);
        body.Controls.AddRange([heading, description, source, load, cancel, status, previewCard, png, jpeg, close]); Controls.Add(body);
        AppTypography.SetDashboardPresentation(body, modern);
        if (modern) foreach (var button in new[] { load, cancel, png, jpeg, close }) DashboardVisual.Button(button, button == load);
        load.Click += async (_, _) => await RunAsync(async token =>
        {
            var next = await services.ThumbnailTool.PreviewAsync(source.Text, token);
            if (disposed) { next.Dispose(); return; }
            image.Image = null; preview?.Dispose(); preview = next; image.Image = next.Bitmap;
            imageTitle.Text = next.Title; dimensions.Text = $"{next.Bitmap.Width} × {next.Bitmap.Height} px";
            SetStatus("ThumbnailTool.Ready");
        });
        cancel.Click += (_, _) => operation?.Cancel();
        png.Click += async (_, _) => await SaveAsync(ThumbnailExportFormat.Png);
        jpeg.Click += async (_, _) => await SaveAsync(ThumbnailExportFormat.Jpeg);
        close.Click += (_, _) => Close();
        Closing += (_, args) => { if (operation is not null) { args.Cancel = true; closeRequested = true; operation.Cancel(); } };
        SizeChanged += (_, _) => Arrange();
        services.Localization.LanguageChanged += LanguageChanged;
        AppTypography.RegisterLayout(body, Arrange); Localize(); Arrange();
    }

    private async Task SaveAsync(ThumbnailExportFormat format)
    {
        if (preview is null || operation is not null) return;
        var extension = format == ThumbnailExportFormat.Png ? "png" : "jpg";
        var dialog = new SaveFileDialog { FileName = preview.SuggestedFileName + "." + extension, DefaultExtension = extension };
        dialog.AddFilter(extension.ToUpperInvariant(), "*." + extension);
        if (await dialog.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.FileName)) return;
        await RunAsync(async token =>
        {
            var result = await services.ThumbnailTool.ExportAsync(preview, dialog.FileName, format, token);
            SetStatus(result.HistorySaved ? "ThumbnailTool.Saved" : "ThumbnailTool.SavedNoHistory");
        }, "ThumbnailTool.Saving");
    }

    private async Task RunAsync(Func<CancellationToken, Task> action, string working = "ThumbnailTool.Loading")
    {
        if (operation is not null || disposed) return;
        using var cancellation = new CancellationTokenSource(); operation = cancellation;
        load.Enabled = source.Enabled = png.Enabled = jpeg.Enabled = false; cancel.Enabled = true; SetStatus(working);
        try { await action(cancellation.Token); }
        catch (OperationCanceledException) { SetStatus("ThumbnailTool.Cancelled"); }
        catch (Exception error)
        {
            services.Logger.Error("Thumbnail tool operation failed.", error);
            SetStatus(error.Message.StartsWith("ThumbnailTool.", StringComparison.Ordinal) ? error.Message : "ThumbnailTool.Failed");
        }
        finally
        {
            operation = null;
            if (!disposed)
            {
                load.Enabled = source.Enabled = true; cancel.Enabled = false; png.Enabled = jpeg.Enabled = preview is not null;
                Arrange(); if (closeRequested) Close();
            }
        }
    }

    private void SetStatus(string key) { statusKey = key; if (!disposed) { status.Text = services.Localization[key]; Arrange(); } }
    private void Localize()
    {
        var t = services.Localization; Text = heading.Text = t["Other.Thumbnails.Title"]; description.Text = t["ThumbnailTool.Description"];
        source.Placeholder = t["Downloads.UrlPlaceholder"]; source.AccessibleName = t["ThumbnailTool.Source"];
        load.Text = t["ThumbnailTool.Load"]; cancel.Text = t["Common.Cancel"]; close.Text = t["Common.Close"];
        png.Text = t["ThumbnailTool.SavePng"]; jpeg.Text = t["ThumbnailTool.SaveJpeg"]; status.Text = t[statusKey];
    }
    private void Arrange()
    {
        var origin = body.DisplayRectangle.Location; var width = Math.Max(1, body.DisplayRectangle.Width - 40);
        heading.SetBounds(origin.X + 20, origin.Y + 18, width, AppTypography.TextHeight(40));
        description.SetBounds(heading.Left, heading.Bottom + 8, width, DashboardVisual.LabelHeight(description, width, 4));
        source.SetBounds(heading.Left, description.Bottom + 16, width, AppTypography.TextHeight(42));
        var line = AppTypography.TextHeight(36);
        load.SetBounds(heading.Left, source.Bottom + 10, Math.Min(width / 2 - 6, DashboardVisual.ActionWidth(load)), line);
        cancel.SetBounds(load.Right + 12, load.Top, Math.Min(width / 2 - 6, DashboardVisual.ActionWidth(cancel)), line);
        status.SetBounds(heading.Left, load.Bottom + 12, width, DashboardVisual.LabelHeight(status, width, 4));
        previewCard.SetBounds(heading.Left, status.Bottom + 12, width, 1);
        image.SetBounds(14, 14, width - 28, Math.Min(300, (width - 28) * 9 / 16));
        imageTitle.SetBounds(14, image.Bottom + 12, width - 28, DashboardVisual.LabelHeight(imageTitle, width - 28, 3));
        dimensions.SetBounds(14, imageTitle.Bottom + 6, width - 28, AppTypography.TextHeight(24)); previewCard.Height = dimensions.Bottom + 14;
        png.SetBounds(heading.Left, previewCard.Bottom + 14, Math.Min(width / 3 - 8, DashboardVisual.ActionWidth(png)), line);
        jpeg.SetBounds(png.Right + 12, png.Top, Math.Min(width / 3 - 8, DashboardVisual.ActionWidth(jpeg)), line);
        close.SetBounds(heading.Left + width - Math.Min(width / 3 - 8, DashboardVisual.ActionWidth(close)), png.Top, Math.Min(width / 3 - 8, DashboardVisual.ActionWidth(close)), line);
    }
    private void LanguageChanged(object? sender, EventArgs args) => Application.RunOnUIThread(() => { if (!disposed) { Localize(); Arrange(); } });
    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposed) { disposed = true; operation?.Cancel(); services.Localization.LanguageChanged -= LanguageChanged; image.Image = null; preview?.Dispose(); }
        base.Dispose(disposing);
    }
}
