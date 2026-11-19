using System.Drawing;
using System.Globalization;
using System.Text;
using ModernFormsNext;
using ModernTubeDownloader.Theming;
using SkiaSharp;
using ContentAlignment = ModernFormsNext.ContentAlignment;

namespace ModernTubeDownloader.Views;

internal sealed record SettingsSearchEntry(string Key, string Caption, string Category, string CategoryCaption,
    string CardCaption, string Description, Control Card, Control CaptionControl, Control Target,
    PersonalizationSearchOption? Personalization = null);

/// <summary>Caption-only projection over canonical controls. It never reads settings/input values.</summary>
internal static class SettingsSearchIndex
{
    internal static IReadOnlyList<SettingsSearchEntry> Find(IEnumerable<SettingsSearchEntry> entries, string query, CultureInfo culture)
    {
        // Accent ordering is culture dependent (Polish accents can be primary
        // weights). Normalize explicitly, retaining culture-aware case/width rules.
        string Fold(string value)
        {
            string normalized;
            try { normalized = value.Normalize(NormalizationForm.FormD); }
            catch (ArgumentException) { normalized = value; } // incomplete pasted surrogate, never crash typing
            var folded = new StringBuilder(normalized.Length);
            foreach (var character in normalized)
                if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                    folded.Append(culture.TwoLetterISOLanguageName == "pl" ? character switch { 'ł' => 'l', 'Ł' => 'L', _ => character } : character);
            return folded.ToString();
        }
        query = Fold(query.Trim());
        if (query.Length == 0) return [];
        var compare = culture.CompareInfo;
        const CompareOptions options = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace | CompareOptions.IgnoreWidth;
        int Rank(SettingsSearchEntry entry)
        {
            if (compare.Compare(Fold(entry.Caption), query, options) == 0) return 0;
            var position = compare.IndexOf(Fold(entry.Caption), query, options);
            if (position >= 0) return position == 0 ? 1 : 2;
            return compare.IndexOf(Fold(entry.Description), query, options) >= 0 ? 3 :
                compare.IndexOf(Fold(entry.CardCaption), query, options) >= 0 || compare.IndexOf(Fold(entry.CategoryCaption), query, options) >= 0 ? 4 : -1;
        }
        return entries.Select(entry => (Entry: entry, Rank: Rank(entry))).Where(match => match.Rank >= 0)
            .OrderBy(match => match.Rank).Select(match => match.Entry)
            .DistinctBy(entry => (entry.Key, entry.Target, entry.Personalization?.AutomationId)).ToArray();
    }

    internal static bool IsWithin(Control control, Control ancestor)
    {
        for (Control? current = control; current is not null; current = current.Parent) if (current == ancestor) return true;
        return false;
    }
    internal static IEnumerable<Control> Descendants(Control control)
    {
        foreach (var child in control.Controls) { yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
    internal static Rectangle BoundsIn(Control control, Control ancestor)
    {
        var rectangle = control.Bounds;
        for (var parent = control.Parent; parent is not null && parent != ancestor; parent = parent.Parent) rectangle.Offset(parent.Left, parent.Top);
        return rectangle;
    }

    internal static SKColor HighlightColor(SKColor background)
    {
        static Color Drawing(SKColor color) => Color.FromArgb(color.Alpha, color.Red, color.Green, color.Blue);
        var accent = AppUi.GetColor(AppThemeTokens.Accent);
        if (AppAppearanceService.ColorContrast(Drawing(accent), Drawing(background)) >= 3) return accent;
        var primary = AppUi.GetColor(AppThemeTokens.TextPrimary);
        if (AppAppearanceService.ColorContrast(Drawing(primary), Drawing(background)) >= 3) return primary;
        return AppAppearanceService.ColorContrast(Color.White, Drawing(background)) >=
            AppAppearanceService.ColorContrast(Color.Black, Drawing(background)) ? SKColors.White : SKColors.Black;
    }

    // Dynamic status, address, token, profile summary, cookie filename and selected
    // option values are deliberately absent. These keys describe existing controls.
    internal static readonly (string Id, string Key)[] PanelOptions =
    [
        ("TrayEnabled", "Tray.ShowIcon"), ("TrayMinimizeBehavior", "Tray.OnMinimize"), ("TrayCloseBehavior", "Tray.OnClose"),
        ("WindowsAutostartEnabled", "Startup.Enable"), ("WindowsAutostartHidden", "Startup.Hidden"), ("StartupApply", "Startup.Apply"),
        ("ApplicationUpdateAutomatic", "Updates.Automatic"), ("ApplicationUpdateFrequency", "Updates.Frequency"),
        ("ApplicationUpdateMode", "Updates.Mode"), ("ApplicationUpdateOnExit", "Updates.OnExit"),
        ("ApplicationUpdateAtStartup", "Updates.StartupInstall"), ("ApplicationUpdateSave", "Updates.Save"),
        ("ApplicationUpdateCheck", "Updates.Check"), ("ApplicationUpdateNotes", "Updates.Notes"),
        ("AppearanceTextScale", "Appearance.TextTitle"), ("AppearanceTextReset", "Appearance.ResetText"),
        ("InterfaceClassic", "Appearance.Value.Classic"), ("InterfaceModernDashboard", "Appearance.Value.ModernDashboard"),
        ("AppearancePresetDefault", "Appearance.Preset.Default"), ("AppearancePresetCompact", "Appearance.Preset.Compact"),
        ("AppearancePresetLarge", "Appearance.Preset.Large"), ("AppearancePresetMinimal", "Appearance.Preset.Minimal"),
        ("AppearancePresetModernDashboard", "Appearance.Preset.ModernDashboard"), ("AppearanceImport", "Appearance.Import"),
        ("AppearanceExport", "Appearance.Export"), ("AppearanceResetSection", "Appearance.ResetSection"), ("AppearanceResetAll", "Appearance.ResetAll"),
        ("CookieBridgeConsent", "CookieSync.Consent"), ("CookieSyncRefresh", "CookieSync.Refresh"),
        ("CookieSyncDelete", "CookieSync.Delete"), ("CookieSyncRepair", "CookieSync.Repair"), ("CookieSyncOptions", "CookieSync.Options"),
        ("NotificationsTest", "Notification.TestButton"), ("NotificationsReset", "Notification.ResetButton"),
        ("NotificationSound", "Settings.SearchSound"), ("NotificationPresentation", "Settings.SearchNotificationPresentation"),
        ("NotificationDiagnostics", "Settings.Advanced"), ("AuthenticationSource", "Settings.SearchAuthenticationSource"),
        ("CookieBridgeBrowser", "Settings.SearchBrowser"), ("CookieBridgeExtensionId", "Settings.SearchExtensionId")
    ];
}

/// <summary>Two-line caption rendering; Button owns focus, keyboard and Invoke accessibility.</summary>
internal sealed class SettingsSearchResultButton : Button
{
    internal SettingsSearchEntry? Entry { get; set; }
    internal string LocationCaption { get; set; } = string.Empty;
    protected override void OnPaint(PaintEventArgs e)
    {
        var padding = LogicalToDeviceUnits(10); var top = LogicalToDeviceUnits(5);
        var width = Math.Max(1, LogicalToDeviceUnits(Width) - padding * 2);
        var line = Math.Max(1, (LogicalToDeviceUnits(Height) - top * 2) / 2);
        e.Canvas.DrawText(Text, new Rectangle(padding, top, width, line), this, ContentAlignment.MiddleLeft, maxLines: 1, ellipsis: true);
        e.Canvas.DrawText(LocationCaption, CurrentStyle.GetFont(), LogicalToDeviceUnits(CurrentStyle.GetFontSize()),
            new Rectangle(padding, top + line, width, line), AppUi.GetColor(AppThemeTokens.TextSecondary),
            ContentAlignment.MiddleLeft, maxLines: 1, ellipsis: true);
    }
}
