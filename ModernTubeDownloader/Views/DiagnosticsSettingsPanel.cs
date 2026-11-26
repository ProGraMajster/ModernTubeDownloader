using ModernFormsNext;
using ModernFormsNext.WindowKit.Threading;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Theming;
using ModernTubeDownloader.Utilities;

namespace ModernTubeDownloader.Views;

internal sealed class DiagnosticsSettingsPanel : Panel
{
    private readonly AppServices services;
    private readonly Label heading = AppUi.Heading("", 15), privacy = AppUi.Muted(), state = AppUi.Muted(), size = AppUi.Muted(), detailLabel = AppUi.Muted(), feedback = AppUi.Muted();
    private readonly CheckBox advanced = new() { AccessibleAutomationId = "DiagnosticsAdvanced" },
        exportAll = new() { AccessibleAutomationId = "DiagnosticsExportAll" };
    private readonly ComboBox detail = new() { AccessibleAutomationId = "DiagnosticsLevel" };
    private readonly Button open = Button("Open"), export = Button("Export"), copy = Button("Copy"), clean = Button("Clean"), cancel = Button("Cancel");
    private CancellationTokenSource? action;
    private bool binding, disposed;

    private static Button Button(string name) => new DashboardButton { AccessibleAutomationId = "Diagnostics" + name };

    internal DiagnosticsSettingsPanel(AppServices services)
    {
        this.services = services;
        AccessibleAutomationId = "DiagnosticsSettings";
        Height = 405;
        AppUi.Card(this);
        AppUi.Input(detail);
        foreach (var label in new[] { privacy, state, size, detailLabel, feedback }) label.Multiline = true;
        state.AccessibleAutomationId = "DiagnosticsState";
        size.AccessibleAutomationId = "DiagnosticsSize";
        feedback.AccessibleAutomationId = "DiagnosticsFeedback";
        foreach (var button in new[] { open, export, copy, clean, cancel }) AppUi.Secondary(button);
        Controls.AddRange([heading, advanced, detailLabel, detail, state, size, privacy, exportAll, open, export, copy, clean, cancel, feedback]);
        advanced.CheckedChanged += async (_, _) => { if (!binding) await SaveOptionsAsync(); };
        detail.SelectedIndexChanged += async (_, _) => { if (!binding) await SaveOptionsAsync(); };
        open.Click += async (_, _) => await RunAsync(_ =>
        {
            ShellService.OpenFolderForFile(services.Paths.LogDirectory);
            return Task.CompletedTask;
        });
        copy.Click += async (_, _) => await RunAsync(async _ =>
        {
            await Clipboard.SetTextAsync(services.Diagnostics.Summary());
            feedback.Text = services.Localization["Diagnostics.Copied"];
        });
        export.Click += async (_, _) => await RunAsync(ExportAsync);
        clean.Click += async (_, _) => await RunAsync(CleanAsync);
        cancel.Click += (_, _) => action?.Cancel();
        services.Localization.LanguageChanged += LanguageChanged;
        VisibleChanged += async (_, _) => { if (Visible) await RefreshSizeAsync(); };
        Localize();
    }

    private void Localize()
    {
        var t = services.Localization;
        heading.Text = t["Settings.Category.Diagnostics"];
        privacy.Text = t["Diagnostics.Privacy"];
        advanced.Text = t["Diagnostics.Advanced"];
        detailLabel.Text = t["Diagnostics.Detail"];
        detail.AccessibleName = detailLabel.Text;
        exportAll.Text = t["Diagnostics.All"];
        open.Text = t["Diagnostics.Open"]; export.Text = t["Diagnostics.Export"]; copy.Text = t["Diagnostics.Copy"];
        clean.Text = t["Diagnostics.Clean"]; cancel.Text = t["Common.Cancel"];
        var settings = services.Settings.Current.Diagnostics;
        binding = true;
        try
        {
            detail.Items.Clear(); detail.Items.Add(t["Diagnostics.Debug"]); detail.Items.Add(t["Diagnostics.Trace"]);
            detail.SelectedIndex = settings.IncludeTrace ? 1 : 0;
            advanced.Checked = settings.Advanced;
        }
        finally { binding = false; }
        RefreshState();
        LayoutForWidth(Width);
    }

    private void RefreshState()
    {
        var t = services.Localization;
        state.Text = t[services.Diagnostics.WriteFailed ? "Diagnostics.WriteFailed" :
            services.Settings.Current.Diagnostics.Advanced ? "Diagnostics.Active" : "Diagnostics.Standard"];
        var idle = action is null;
        advanced.Enabled = idle && !services.Settings.LoadFailed;
        detail.Enabled = advanced.Enabled && advanced.Checked;
        open.Enabled = export.Enabled = copy.Enabled = clean.Enabled = exportAll.Enabled = idle;
        cancel.Enabled = !idle;
    }

    private async Task RefreshSizeAsync()
    {
        var bytes = await services.Diagnostics.SizeAsync();
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (disposed) return;
            size.Text = services.Localization.Get("Diagnostics.Size", (bytes / 1048576d).ToString("N1", services.Localization.Culture));
            RefreshState();
        });
    }

    private Task SaveOptionsAsync() => RunAsync(async token =>
    {
        var options = services.Settings.Current.Diagnostics;
        var wasAdvanced = options.Advanced;
        var wasTrace = options.IncludeTrace;
        options.Advanced = advanced.Checked;
        options.IncludeTrace = detail.SelectedIndex == 1;
        try { await services.Settings.SaveAsync(token); }
        catch
        {
            options.Advanced = wasAdvanced; options.IncludeTrace = wasTrace;
            await Dispatcher.UIThread.InvokeAsync(() => { if (!disposed) Localize(); });
            throw;
        }
    });

    private async Task ExportAsync(CancellationToken token)
    {
        var dialog = new SaveFileDialog { DefaultExtension = "zip", FileName = $"MTD-Diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip" };
        dialog.AddFilter("ZIP", "*.zip");
        if (await dialog.ShowDialog(FindForm()!) != DialogResult.OK) return;
        feedback.Text = services.Localization["Diagnostics.Working"];
        var counts = await services.Diagnostics.ExportAsync(dialog.FileName, exportAll.Checked, token);
        feedback.Text = services.Localization.Get("Diagnostics.Exported", counts.Included, counts.Excluded);
    }

    private async Task CleanAsync(CancellationToken token)
    {
        var t = services.Localization;
        using var dialog = AppMessageBox.Confirmation(t["Settings.Category.Diagnostics"], t["Diagnostics.ConfirmClean"],
            t["Diagnostics.Clean"], t["Common.Cancel"], "DiagnosticsConfirmClean");
        if (await dialog.ShowDialog(FindForm()!) != DialogResult.Yes) return;
        token.ThrowIfCancellationRequested();
        var removed = await services.Diagnostics.RemoveOldAsync();
        feedback.Text = t.Get("Diagnostics.Cleaned", removed);
    }

    private async Task RunAsync(Func<CancellationToken, Task> operation)
    {
        if (action is not null || disposed) return;
        using var source = new CancellationTokenSource();
        action = source;
        feedback.Text = "";
        RefreshState();
        try { await operation(source.Token); }
        catch (OperationCanceledException)
        { await Dispatcher.UIThread.InvokeAsync(() => { if (!disposed) feedback.Text = services.Localization["Status.Cancelled"]; }); }
        catch (Exception error)
        {
            services.Logger.Error("Diagnostics settings operation failed.", error);
            await Dispatcher.UIThread.InvokeAsync(() => { if (!disposed) feedback.Text = services.Localization["Diagnostics.Failed"]; });
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                action = null;
                if (!disposed) RefreshState();
            });
            if (!disposed) await RefreshSizeAsync();
        }
    }

    internal void LayoutForWidth(int width)
    {
        if (width <= 40) return;
        var inner = width - 32;
        var line = AppTypography.TextHeight(34);
        var top = 16;
        void Row(Control control, int height) { control.SetBounds(16, top, inner, height); top = control.Bottom + 6; }
        Row(heading, line);
        Row(advanced, line);
        Row(detailLabel, AppTypography.TextHeight(23));
        Row(detail, line);
        Row(state, DashboardVisual.LabelHeight(state, inner, 3));
        Row(size, AppTypography.TextHeight(25));
        Row(privacy, DashboardVisual.LabelHeight(privacy, inner, 6));
        Row(exportAll, line);
        var left = 16;
        foreach (var button in new[] { open, export, copy, clean, cancel })
        {
            var buttonWidth = Math.Min(inner, DashboardVisual.ActionWidth(button));
            if (left > 16 && left + buttonWidth > width - 16) { left = 16; top += line + 6; }
            button.SetBounds(left, top, buttonWidth, line);
            left = button.Right + 8;
        }
        top += line + 8;
        Row(feedback, AppTypography.TextHeight(56));
        Height = top + 10;
    }

    private void LanguageChanged(object? sender, EventArgs args) => Application.RunOnUIThread(() => { if (!disposed) Localize(); });

    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposed)
        {
            disposed = true;
            action?.Cancel();
            services.Localization.LanguageChanged -= LanguageChanged;
        }
        base.Dispose(disposing);
    }
}
