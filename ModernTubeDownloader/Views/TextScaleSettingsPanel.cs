using ModernFormsNext;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Theming;

namespace ModernTubeDownloader.Views;

internal sealed class TextScaleSettingsPanel : Panel
{
    private readonly AppServices services;
    private readonly Label title = AppUi.Heading("", 15), hint = AppUi.Muted();
    private readonly ComboBox presets = new() { AccessibleAutomationId = "AppearanceTextPreset" };
    private readonly NumericUpDown scale = new() { Minimum = 80, Maximum = 200, AccessibleAutomationId = "AppearanceTextScale" };
    private readonly Button reset = new() { AccessibleAutomationId = "AppearanceTextReset" };
    private bool applying;
    public TextScaleSettingsPanel(AppServices services)
    {
        this.services = services; Height = 220; AppUi.Card(this); AccessibleAutomationId = "TextScaleSection";
        AppUi.Input(presets); AppUi.Input(scale); AppUi.Secondary(reset); hint.Multiline = true;
        Controls.AddRange([title, presets, scale, reset, hint]); scale.Value = services.Appearance.Profile.Typography.AppTextScale;
        foreach (var percent in new[] { 80, 90, 100, 110, 125, 150, 175, 200 }) presets.Items.Add(percent + "%");
        presets.SelectedIndexChanged += (_, _) => { if (!applying && presets.SelectedIndex >= 0) scale.Value = new[] { 80, 90, 100, 110, 125, 150, 175, 200 }[presets.SelectedIndex]; };
        scale.ValueChanged += async (_, _) => { if (!applying) { applying = true; try { await services.Appearance.SetTextScaleAsync((int)scale.Value); } catch (Exception ex) { services.Logger.Error("Saving text scale failed.", ex); } finally { applying = false; } } };
        reset.Click += (_, _) => scale.Value = 100;
        services.Localization.LanguageChanged += LanguageChanged; services.Appearance.ModeChanged += ProfileChanged; ApplyText();
    }
    public void LayoutForWidth(int width)
    {
        Width = width; Height = AppTypography.TextHeight(220);
        title.SetBounds(24, 18, width - 48, AppTypography.TextHeight(32));
        presets.SetBounds(24, AppTypography.TextHeight(60), 150, AppTypography.TextHeight(38)); scale.SetBounds(190, AppTypography.TextHeight(60), 110, AppTypography.TextHeight(38));
        reset.SetBounds(24, AppTypography.TextHeight(112), Math.Min(500, width - 48), AppTypography.TextHeight(38));
        hint.SetBounds(24, AppTypography.TextHeight(158), width - 48, AppTypography.TextHeight(46));
    }
    private void ApplyText() { title.Text = services.Localization["Appearance.TextTitle"]; hint.Text = services.Localization["Appearance.TextHint"]; reset.Text = services.Localization["Appearance.ResetText"]; applying = true; try { presets.Items.Clear(); foreach (var percent in new[] { 80, 90, 100, 110, 125, 150, 175, 200 }) presets.Items.Add(percent + "%"); presets.SelectedIndex = Array.IndexOf(new[] { 80, 90, 100, 110, 125, 150, 175, 200 }, (int)scale.Value); } finally { applying = false; } }
    private void LanguageChanged(object? sender, EventArgs e) => Application.RunOnUIThread(ApplyText);
    private void ProfileChanged(object? sender, EventArgs e) { applying = true; try { scale.Value = services.Appearance.Profile.Typography.AppTextScale; presets.SelectedIndex = Array.IndexOf(new[] { 80, 90, 100, 110, 125, 150, 175, 200 }, (int)scale.Value); } finally { applying = false; } }
    protected override void Dispose(bool disposing) { if (disposing) { services.Localization.LanguageChanged -= LanguageChanged; services.Appearance.ModeChanged -= ProfileChanged; } base.Dispose(disposing); }
}
