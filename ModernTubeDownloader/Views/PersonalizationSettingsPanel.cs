using System.Reflection;
using System.Text.Json;
using ModernFormsNext;
using ModernFormsNext.WindowKit.Platform.Services;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Theming;

namespace ModernTubeDownloader.Views;

/// <summary>Only the closed appearance schema is editable; no paths, credentials or executable configuration.</summary>
internal sealed class PersonalizationSettingsPanel : Panel
{
    private static readonly string[] Sections = ["Global", "Navigation", "Downloads", "Live", "History", "Other", "BatchImport", "Settings", "Details"];
    private readonly AppServices services;
    private readonly ComboBox section = new() { AccessibleAutomationId = "AppearanceSection" };
    private readonly FlowLayoutPanel commands = new() { WrapContents = true };
    private readonly Panel fields = new();
    private readonly Label title = AppUi.Heading("", 15), warning = AppUi.Muted();
    private readonly Panel preview = new();
    private readonly Label previewText = AppUi.Heading("", 13);
    private readonly Button previewButton = new();
    private readonly ProgressBar previewProgress = new() { Minimum = 0, Maximum = 100, Value = 55 };
    private readonly Dictionary<Button, string> labels = [];
    private readonly ModernFormsNext.Timer debounce = new() { Interval = 80 };
    private readonly SemaphoreSlim operations = new(1, 1);
    private AppearanceProfile? pending;
    private bool editing, disposed;
    public PersonalizationSettingsPanel(AppServices services)
    {
        this.services = services; AppUi.Card(this); AppUi.Surface(fields); AppUi.Surface(commands); AppUi.Card(preview, true);
        AppUi.Input(section); AppUi.Primary(previewButton); warning.Multiline = true;
        preview.Controls.AddRange([previewText, previewButton, previewProgress]);
        Controls.AddRange([title, commands, section, fields, preview, warning]);
        foreach (var name in new[] { "Default", "Compact", "Large", "Minimal" }) Add("Preset." + name, () => ApplyAsync(AppearanceProfile.Preset(name)));
        Add("Import", ImportAsync); Add("Export", ExportAsync);
        Add("ResetSection", () => ConfirmResetAsync(false)); Add("ResetAll", () => ConfirmResetAsync(true));
        section.SelectedIndexChanged += (_, _) => { if (!editing) BuildFields(); };
        debounce.Tick += async (_, _) => { debounce.Stop(); if (pending is { } profile) { pending = null; await SafeAsync(() => ApplyAsync(profile, false)); } };
        services.Localization.LanguageChanged += LanguageChanged; services.Appearance.ModeChanged += ProfileChanged;
        Localize();
    }
    private void Add(string key, Func<Task> action)
    {
        var button = new Button { Width = 190, Height = 38, Margin = new Padding(0, 0, 8, 8), AccessibleAutomationId = "Appearance" + key.Replace(".", "") };
        AppUi.Secondary(button); labels[button] = "Appearance." + key; commands.Controls.Add(button); button.Click += async (_, _) => await SafeAsync(action);
    }
    private async Task SafeAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) { services.Logger.Error("Appearance action failed.", ex); if (!disposed) warning.Text = services.Localization["Appearance.Error.Profile"]; }
    }
    private async Task ApplyAsync(AppearanceProfile value, bool rebuild = true)
    {
        await operations.WaitAsync();
        try { editing = !rebuild; await services.Appearance.ApplyProfileAsync(value); }
        finally { editing = false; operations.Release(); }
        if (!disposed && rebuild) BuildFields();
    }
    private void QueueEdit(Action<AppearanceProfile> edit)
    {
        if (editing || disposed) return;
        pending ??= services.Appearance.Profile.Copy(); edit(pending); debounce.Stop(); debounce.Start();
    }
    private void BuildFields()
    {
        editing = true;
        try
        {
            foreach (var child in fields.Controls.ToArray()) { fields.Controls.Remove(child); child.Dispose(); }
            var name = Sections[Math.Max(0, section.SelectedIndex)];
            var sectionProperty = typeof(AppearanceProfile).GetProperty(name)!;
            var settings = sectionProperty.GetValue(services.Appearance.Profile)!;
            foreach (var property in settings.GetType().GetProperties().Where(p => p.PropertyType == typeof(bool) || p.PropertyType == typeof(int) || p.PropertyType.IsEnum))
            {
                var row = new Panel { Height = AppTypography.TextHeight(46), Tag = property.Name }; AppUi.Surface(row);
                var label = AppUi.Muted(services.Localization["Appearance.Field." + property.Name]);
                Control input;
                if (property.PropertyType == typeof(bool))
                {
                    var check = new CheckBox { Checked = (bool)property.GetValue(settings)! }; AppUi.BindForeground(check);
                    check.CheckedChanged += (_, _) => QueueEdit(p => property.SetValue(sectionProperty.GetValue(p), check.Checked)); input = check;
                }
                else if (property.PropertyType.IsEnum)
                {
                    var combo = new ComboBox(); var values = Enum.GetValues(property.PropertyType); foreach (var value in values) combo.Items.Add(services.Localization["Appearance.Value." + value]);
                    combo.SelectedIndex = Array.IndexOf(values, property.GetValue(settings)); AppUi.Input(combo);
                    combo.SelectedIndexChanged += (_, _) => { if (combo.SelectedIndex >= 0) QueueEdit(p => property.SetValue(sectionProperty.GetValue(p), values.GetValue(combo.SelectedIndex))); }; input = combo;
                }
                else
                {
                    var limits = property.Name switch { "CornerRadius" => (0, 32), "SpacingPercent" => (75, 150), "SectionSpacing" => (4, 40), _ => (200, 1200) };
                    var number = new NumericUpDown { Minimum = limits.Item1, Maximum = limits.Item2, Value = (int)property.GetValue(settings)! }; AppUi.Input(number);
                    number.ValueChanged += (_, _) => QueueEdit(p => property.SetValue(sectionProperty.GetValue(p), (int)number.Value)); input = number;
                }
                input.AccessibleAutomationId = "Appearance" + name + property.Name; row.Controls.AddRange([label, input]); fields.Controls.Add(row);
            }
            if (name == "Global") foreach (var token in new[] { "Accent", "Background", "Surface", "SurfaceSecondary", "Border", "TextPrimary", "TextSecondary" }) AddColor(token);
        }
        finally { editing = false; }
        LayoutForWidth(Width); CheckContrast();
    }
    private void AddColor(string token)
    {
        var row = new Panel { Height = AppTypography.TextHeight(46) }; AppUi.Surface(row);
        var label = AppUi.Muted(services.Localization["Appearance.Color." + token]);
        var color = new TextBox { Text = services.Appearance.Profile.Global.Colors.GetValueOrDefault(token, Hex(AppUi.GetColor("App." + token))), AccessibleAutomationId = "AppearanceColor" + token };
        AppUi.Input(color); var pick = new Button { Text = "…" }; var reset = new Button { Text = services.Localization["Appearance.ResetToken"] }; AppUi.Secondary(pick); AppUi.Secondary(reset);
        color.TextChanged += (_, _) => { var value = color.Text; if (value.Length == 7 && value[0] == '#' && value[1..].All(Uri.IsHexDigit)) QueueEdit(p => { p.Global.Theme = AppearanceTheme.Custom; p.Global.Colors[token] = value; }); };
        pick.Click += async (_, _) => await SafeAsync(async () => { if (FindForm() is not { } owner) return; var dialog = new ColorDialog { Color = AppUi.GetColor("App." + token) }; if (await dialog.ShowDialog(owner) == DialogResult.OK) color.Text = Hex(dialog.Color); });
        reset.Click += async (_, _) => await SafeAsync(async () => { debounce.Stop(); var profile = pending ?? services.Appearance.Profile.Copy(); pending = null; profile.Global.Colors.Remove(token); await ApplyAsync(profile); }); row.Controls.AddRange([label, color, pick, reset]); fields.Controls.Add(row);
    }
    private static string Hex(SkiaSharp.SKColor color) => $"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}";
    private void CheckContrast()
    {
        var foreground = AppUi.GetColor(AppThemeTokens.TextPrimary); var background = AppUi.GetColor(AppThemeTokens.Surface);
        warning.Text = AppAppearanceService.ColorContrast(System.Drawing.Color.FromArgb(foreground.Red, foreground.Green, foreground.Blue), System.Drawing.Color.FromArgb(background.Red, background.Green, background.Blue)) < 4.5 ? services.Localization["Appearance.ContrastWarning"] : "";
    }
    private async Task ConfirmResetAsync(bool all)
    {
        if (await SystemMessageBox.ShowAsync(FindForm(), services.Localization["Appearance.ResetConfirm"], services.Localization["Appearance.Title"], MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        var profile = services.Appearance.Profile.Copy(); if (all) profile = new(); else profile.ResetSection(Sections[Math.Max(0, section.SelectedIndex)]); await ApplyAsync(profile);
    }
    private async Task ImportAsync()
    {
        if (FindForm() is not { } owner) return;
        var dialog = new OpenFileDialog(); dialog.AddFilter("JSON", "*.json"); if (await dialog.ShowDialog(owner) != DialogResult.OK || dialog.FileName is not { } file) return;
        if (new FileInfo(file).Length > 65536) throw new InvalidDataException(); await ApplyAsync(AppearanceProfileStore.Validate(await File.ReadAllTextAsync(file)));
    }
    private async Task ExportAsync()
    {
        if (FindForm() is not { } owner) return;
        var dialog = new SaveFileDialog { FileName = "appearance.json", DefaultExtension = "json" }; dialog.AddFilter("JSON", "*.json");
        if (await dialog.ShowDialog(owner) == DialogResult.OK && dialog.FileName is { } file) await File.WriteAllTextAsync(file, JsonSerializer.Serialize(services.Appearance.Profile, AppearanceProfileStore.Options));
    }
    public void LayoutForWidth(int width)
    {
        Width = width; var line = AppTypography.TextHeight(46); var commandHeight = AppTypography.TextHeight(46) * (int)Math.Ceiling(8d / Math.Max(1, (width - 48) / 198));
        title.SetBounds(24, 18, width - 48, AppTypography.TextHeight(32));
        foreach (var button in labels.Keys) { button.Height = AppTypography.TextHeight(38); button.Width = Math.Min(width - 48, AppTypography.TextHeight(190)); }
        // Width-dependent wrap count includes the text-scaled command buttons.
        commandHeight = (int)Math.Ceiling(8d / Math.Max(1, (width - 48) / (Math.Min(width - 48, AppTypography.TextHeight(190)) + 8))) * line;
        commands.SetBounds(24, title.Bottom + 10, width - 48, commandHeight); section.SetBounds(24, commands.Bottom + 8, width - 48, AppTypography.TextHeight(38));
        fields.SetBounds(24, section.Bottom + 12, width - 48, fields.Controls.Count * line);
        var y = 0; foreach (var row in fields.Controls) { row.SetBounds(0, y, fields.Width, line); var c = row.Controls.ToArray(); var left = Math.Max(200, fields.Width / 2); c[0].SetBounds(0, 0, left - 10, line - 4); c[1].SetBounds(left, 0, Math.Max(100, fields.Width - left), line - 6); if (c.Length > 2) { c[1].Width = Math.Max(100, fields.Width - left - 156); c[2].SetBounds(c[1].Right + 4, 0, 42, line - 6); c[3].SetBounds(c[2].Right + 4, 0, 106, line - 6); } y += line; }
        preview.SetBounds(24, fields.Bottom + 12, width - 48, AppTypography.TextHeight(116)); previewText.SetBounds(16, 12, preview.Width - 32, AppTypography.TextHeight(28)); previewButton.SetBounds(16, previewText.Bottom + 6, Math.Min(340, preview.Width - 32), AppTypography.TextHeight(38)); previewProgress.SetBounds(16, previewButton.Bottom + 12, preview.Width - 32, 8);
        warning.SetBounds(24, preview.Bottom + 12, width - 48, AppTypography.TextHeight(46)); Height = warning.Bottom + 20;
    }
    private void Localize()
    {
        editing = true; var index = section.SelectedIndex; section.Items.Clear(); foreach (var name in Sections) section.Items.Add(services.Localization["Appearance.Section." + name]); section.SelectedIndex = Math.Max(0, index); editing = false;
        title.Text = services.Localization["Appearance.Title"]; previewText.Text = services.Localization["Appearance.Preview"]; previewButton.Text = services.Localization["Appearance.PreviewButton"]; foreach (var (button, key) in labels) button.Text = services.Localization[key]; BuildFields();
    }
    private void LanguageChanged(object? sender, EventArgs e) => Application.RunOnUIThread(Localize);
    private void ProfileChanged(object? sender, EventArgs e) { if (!disposed) { CheckContrast(); if (!editing) BuildFields(); } }
    protected override void Dispose(bool disposing) { if (disposing) { disposed = true; debounce.Stop(); debounce.Dispose(); services.Localization.LanguageChanged -= LanguageChanged; services.Appearance.ModeChanged -= ProfileChanged; } base.Dispose(disposing); }
}
