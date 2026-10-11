using ModernFormsNext;
using ModernFormsNext.Notifications;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Settings;
using ModernTubeDownloader.Theming;
using SkiaSharp;

namespace ModernTubeDownloader.Views;

/// <summary>A single settings section; preferences do not imply OS permission or backend support.</summary>
internal sealed class NotificationSettingsPanel : Panel
{
    private readonly AppServices services;
    private readonly List<(Control Control, string Key)> labels = [];
    private readonly List<(CheckBox Control, System.Reflection.PropertyInfo Property)> checks = [];
    private readonly ComboBox sound = new();
    private readonly ComboBox presentation = new();
    private readonly Label status = AppUi.Muted();
    private readonly Label diagnostic = AppUi.Muted();
    private readonly CheckBox advanced = new() { AccessibleAutomationId = "NotificationDiagnostics" };
    private readonly Label heading = AppUi.Heading(string.Empty, 15);
    private readonly Label description = AppUi.Muted();
    private readonly Button test = new() { AccessibleAutomationId = "NotificationsTest" };
    private readonly Button reset = new() { AccessibleAutomationId = "NotificationsReset" };
    private bool loading;
    private bool disposed;
    private SystemNotificationAvailability availability = SystemNotificationAvailability.Unavailable;

    public NotificationSettingsPanel(AppServices services)
    {
        this.services = services;
        AccessibleAutomationId = "NotificationsSection";
        AppUi.Card(this);
        Bind(heading, "Notification.SettingsTitle"); Bind(description, "Notification.SettingsDescription");
        Bind(advanced, "Settings.Advanced"); AppUi.BindForeground(advanced);
        diagnostic.Visible = false;
        advanced.CheckedChanged += (_, _) => diagnostic.Visible = advanced.Checked;
        Controls.AddRange([heading, description, status, advanced, diagnostic]);
        foreach (var property in typeof(NotificationSettings).GetProperties().Where(property => property.PropertyType == typeof(bool)))
        {
            var check = new CheckBox { AccessibleAutomationId = "Notification" + property.Name };
            AppUi.BindForeground(check); check.Style.BackgroundColor = SKColors.Transparent;
            Bind(check, "Notification.Preference." + property.Name);
            checks.Add((check, property)); Controls.Add(check);
            check.CheckedChanged += async (_, _) =>
            {
                if (loading || disposed) return;
                property.SetValue(services.Settings.Current.Notifications, check.Checked);
                try { await services.Settings.SaveAsync(); await RefreshStatusAsync(); }
                catch (Exception error) { services.Logger.Error("Saving notification preferences failed.", error); }
            };
        }
        AppUi.Input(sound); AppUi.Input(presentation); AppUi.Secondary(test); AppUi.Secondary(reset);
        Bind(test, "Notification.TestButton"); Bind(reset, "Notification.ResetButton");
        sound.AccessibleAutomationId = "NotificationSound"; presentation.AccessibleAutomationId = "NotificationPresentation";
        Controls.AddRange([sound, presentation, test, reset]);
        sound.SelectedIndexChanged += async (_, _) =>
        {
            if (loading || disposed || sound.SelectedIndex < 0) return;
            services.Settings.Current.Notifications.Sound = (NotificationSound)sound.SelectedIndex;
            await SaveAsync();
        };
        presentation.SelectedIndexChanged += async (_, _) =>
        {
            if (loading || disposed || presentation.SelectedIndex < 0) return;
            services.Settings.Current.Notifications.Presentation = (NotificationPresentation)presentation.SelectedIndex;
            await SaveAsync();
        };
        test.Click += async (_, _) => { if (services.Notifications is { } notifications) await notifications.SendTestAsync(); await RefreshStatusAsync(); };
        reset.Click += async (_, _) => { services.Settings.Current.Notifications = new(); LoadValues(); await SaveAsync(); };
        services.Localization.LanguageChanged += LanguageChanged;
        LoadValues(); LayoutForWidth(600);
        _ = RefreshStatusAsync();
    }
    private void Bind(Control control, string key) { labels.Add((control, key)); control.Text = services.Localization[key]; }
    private async Task SaveAsync()
    {
        try { await services.Settings.SaveAsync(); await RefreshStatusAsync(); }
        catch (Exception error) { services.Logger.Error("Saving notification preferences failed.", error); }
    }
    private void LoadValues()
    {
        loading = true;
        try
        {
            foreach (var (control, property) in checks) control.Checked = (bool)property.GetValue(services.Settings.Current.Notifications)!;
            foreach (var (control, key) in labels) control.Text = services.Localization[key];
            sound.Items.Clear(); presentation.Items.Clear();
            sound.Items.Add(services.Localization["Notification.Sound.Default"]); sound.Items.Add(services.Localization["Notification.Sound.Silent"]);
            presentation.Items.Add(services.Localization["Notification.Presentation.Normal"]); presentation.Items.Add(services.Localization["Notification.Presentation.CenterOnly"]);
            sound.SelectedIndex = (int)services.Settings.Current.Notifications.Sound;
            presentation.SelectedIndex = (int)services.Settings.Current.Notifications.Presentation;
            RenderStatus();
        }
        finally { loading = false; }
    }
    private async Task RefreshStatusAsync()
    {
        if (!services.Settings.Current.Notifications.Enabled)
        {
            Application.RunOnUIThread(() => { if (!disposed) RenderStatus(); });
            return;
        }
        try { availability = services.Notifications is { } notifications ? (await notifications.GetStatusAsync()).Availability : SystemNotificationAvailability.Unavailable; }
        catch (Exception error) { availability = SystemNotificationAvailability.Unavailable; services.Logger.Error("Notification availability query failed.", error); }
        if (!disposed) Application.RunOnUIThread(() => { if (!disposed) RenderStatus(); });
    }
    private void RenderStatus()
    {
        var enabled = services.Settings.Current.Notifications.Enabled;
        var ready = availability is SystemNotificationAvailability.Ready or SystemNotificationAvailability.Unknown && services.Notifications?.Capabilities.IsSupported == true;
        status.Text = services.Localization[!enabled ? "Notification.Disabled" : ready ? "Notification.Ready" : "Notification.Unavailable"];
        diagnostic.Text = services.Localization.Get("Notification.Diagnostics", services.Notifications?.Capabilities.Backend ?? "Unavailable", availability);
        test.Enabled = enabled && ready;
        presentation.Enabled = services.Notifications?.CenterOnlySupported == true;
        foreach (var (control, property) in checks) if (property.Name != nameof(NotificationSettings.Enabled)) control.Enabled = enabled;
        sound.Enabled = enabled;
        presentation.Enabled &= enabled;
    }
    public void LayoutForWidth(int width)
    {
        heading.SetBounds(24, 20, width - 48, 28); description.SetBounds(24, 52, width - 48, 28);
        status.SetBounds(24, 86, width - 48, 26);
        advanced.SetBounds(24, 114, 130, 26); diagnostic.SetBounds(166, 114, width - 190, 26);
        var columns = width >= 900 ? 2 : 1;
        var rows = (checks.Count + columns - 1) / columns;
        var cellWidth = (width - 48) / columns;
        for (var i = 0; i < checks.Count; i++) checks[i].Control.SetBounds(24 + i / rows * cellWidth, 154 + i % rows * 34, cellWidth - 12, 30);
        var top = 164 + rows * 34;
        sound.SetBounds(24, top, Math.Min(250, (width - 60) / 2), 36);
        presentation.SetBounds(width / 2, top, Math.Min(280, (width - 60) / 2), 36);
        test.SetBounds(24, top + 52, 230, 36); reset.SetBounds(270, top + 52, Math.Min(230, width - 294), 36);
        Height = top + 112;
    }
    private void LanguageChanged(object? sender, EventArgs e) => Application.RunOnUIThread(() => { if (!disposed) LoadValues(); });
    protected override void Dispose(bool disposing)
    {
        if (disposing) { disposed = true; services.Localization.LanguageChanged -= LanguageChanged; }
        base.Dispose(disposing);
    }
}
