using ModernFormsNext;
using ModernTubeDownloader.Theming;

namespace ModernTubeDownloader.Views;

internal sealed partial class SettingsView
{
    private readonly Button openSetup = new(), openWhatsNew = new();
    public event EventHandler? SetupRequested;
    public event EventHandler? WhatsNewRequested;
    private void InitializeJourneyActions()
    {
        foreach (var (button, key, id) in new[] { (openSetup, "Experience.Reopen", "SettingsOpenSetup"), (openWhatsNew, "Experience.WhatsNew", "SettingsWhatsNew") })
        { localizedText.Add(button, key); button.AccessibleAutomationId = id; AppUi.Secondary(button); actionsCard.Controls.Add(button); }
        openSetup.Click += (_, _) => SetupRequested?.Invoke(this, EventArgs.Empty);
        openWhatsNew.Click += (_, _) => WhatsNewRequested?.Invoke(this, EventArgs.Empty);
    }
    internal void ReloadAfterSetup() { LoadValues(); ApplyLocalization(); UpdateLayout(); }
    private void LayoutJourneyActions()
    {
        var width = Math.Max(1, actionsCard.Width - 32); var y = 16; var x = 16; var line = AppTypography.TextHeight(40);
        foreach (var button in new[] { save, logs, openSetup, openWhatsNew })
        {
            var desired = Math.Min(width, DashboardVisual.ActionWidth(button));
            if (x > 16 && x + desired > width + 16) { x = 16; y += line + 8; }
            button.SetBounds(x, y, desired, line); x = button.Right + 8;
        }
        feedback.SetBounds(16, y + line + 8, width, AppTypography.TextHeight(28)); actionsCard.Height = feedback.Bottom + 16;
    }
}
