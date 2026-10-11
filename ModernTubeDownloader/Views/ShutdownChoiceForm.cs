using ModernFormsNext;
using ModernTubeDownloader.Localization;
using ModernTubeDownloader.Theming;

namespace ModernTubeDownloader.Views;

internal sealed class ShutdownChoiceForm : Form
{
    public ShutdownChoiceForm(LocalizationService text, bool activeWork, bool canHide)
    {
        Text = text["Tray.CloseTitle"];
        AccessibilityObject.AutomationId = "ShutdownChoiceWindow";
        AppBranding.Apply(this);
        Size = new System.Drawing.Size(650, 250);
        MinimumSize = Size;
        var body = new Panel { Dock = DockStyle.Fill };
        AppUi.BindBackground(body);
        Controls.Add(body);
        var message = AppUi.Muted(text[activeWork ? "Tray.ActiveWork" : "Tray.CloseQuestion"]);
        message.Multiline = true;
        message.SetBounds(24, 24, 598, 80);
        body.Controls.Add(message);
        AddAction(text["Common.Cancel"], "ShutdownCancel", 24, DialogResult.Cancel, true);
        AddAction(text["Tray.Hide"], "ShutdownHide", 224, DialogResult.Ignore, canHide);
        AddAction(text["Tray.ExitAnyway"], "ShutdownExit", 424, DialogResult.OK, true);
    }

    private void AddAction(string text, string id, int left, DialogResult result, bool enabled)
    {
        var button = new Button { Text = text, AccessibleAutomationId = id, DialogResult = result, Enabled = enabled };
        button.SetBounds(left, 132, 190, 44);
        AppUi.Secondary(button);
        Controls[0].Controls.Add(button);
    }
}
