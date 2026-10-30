using ModernFormsNext;
using ModernTubeDownloader.Theming;

namespace ModernTubeDownloader.Views;

internal static class AppMessageBox
{
    // MessageBoxForm owns the modal window; add explicit confirmation/cancellation
    // actions through its public control collection rather than an OS message box.
    public static MessageBoxForm Confirmation(string title, string message, string confirmText, string cancelText, string automationPrefix)
    {
        var dialog = new MessageBoxForm(title, message);
        var buttons = dialog.Controls.OfType<Panel>().Single();
        foreach (var existing in buttons.Controls) { existing.Visible = false; existing.Enabled = false; }
        var cancel = new DashboardButton { Text = cancelText, AccessibleAutomationId = automationPrefix + "Cancel" };
        var confirm = new DashboardButton { Text = confirmText, AccessibleAutomationId = automationPrefix + "Confirm" };
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
        return dialog;
    }
}
