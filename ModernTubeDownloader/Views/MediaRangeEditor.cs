using ModernFormsNext;
using ModernTubeDownloader.Localization;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Theming;

namespace ModernTubeDownloader.Views;

internal sealed class MediaRangeEditor : Panel
{
    private readonly LocalizationService text;
    private readonly Label label;
    private readonly ComboBox mode;
    private readonly Label fromLabel;
    private readonly TextBox from;
    private readonly Label toLabel;
    private readonly TextBox to;
    private bool responsiveLayout, arranging;
    public event EventHandler? ModeChanged;
    public bool IsCustom => mode.SelectedItem is LocalizedOption<MediaRangeMode> { Value: MediaRangeMode.Custom };

    public MediaRangeEditor(LocalizationService text, string automationPrefix)
    {
        this.text = text;
        AppUi.BindBackground(this);
        label = AppUi.Muted();
        mode = new ComboBox { AccessibleAutomationId = automationPrefix + "RangeMode" };
        AppUi.Input(mode);
        fromLabel = AppUi.Muted();
        from = new AppThemeTextBox { AccessibleAutomationId = automationPrefix + "RangeStart", Placeholder = "hh:mm:ss" };
        AppUi.Input(from);
        toLabel = AppUi.Muted();
        to = new AppThemeTextBox { AccessibleAutomationId = automationPrefix + "RangeEnd", Placeholder = "hh:mm:ss" };
        AppUi.Input(to);
        Controls.AddRange([label, mode, fromLabel, from, toLabel, to]);
        mode.SelectedIndexChanged += (_, _) => UpdateVisibility();
        text.LanguageChanged += LanguageChanged;
        SizeChanged += (_, _) => LayoutControls();
        ApplyLocalization();
        LayoutControls();
        AppTypography.RegisterLayout(this, LayoutControls);
    }

    public bool TryGetRange(double? durationSeconds, bool activeLive, out MediaTimeRange range, out string? error)
    {
        range = MediaTimeRange.Full;
        error = null;
        if (mode.SelectedItem is not LocalizedOption<MediaRangeMode> { Value: MediaRangeMode.Custom }) return true;
        if (!MediaTimeRange.TryParseTime(from.Text, out var start) || !MediaTimeRange.TryParseTime(to.Text, out var end))
        {
            error = "Range.Error.Invalid";
            return false;
        }
        range = new MediaTimeRange(MediaRangeMode.Custom, start, end);
        error = range.Validate(durationSeconds, activeLive);
        return error is null;
    }

    public bool CompactLayout { get; set; }
    // Opt-in presentation: Classic callers keep their original row recipe.
    public bool ResponsiveLayout { get => responsiveLayout; set { responsiveLayout = value; if (!value && !CompactLayout) AppUi.BindBackground(this); LayoutControls(); } }
    public void SetRange(MediaTimeRange value)
    {
        mode.SelectedIndex = value.Mode == MediaRangeMode.Custom ? 1 : 0;
        from.Text = value.Start?.ToString(@"hh\:mm\:ss") ?? ""; to.Text = value.End?.ToString(@"hh\:mm\:ss") ?? "";
    }

    public void Reset()
    {
        mode.SelectedIndex = 0;
        from.Text = string.Empty;
        to.Text = string.Empty;
    }

    private void ApplyLocalization()
    {
        var selected = mode.SelectedItem is LocalizedOption<MediaRangeMode> option ? option.Value : MediaRangeMode.Full;
        label.Text = text["Range.Label"];
        fromLabel.Text = text["Range.From"];
        toLabel.Text = text["Range.To"];
        mode.AccessibleName = label.Text; from.AccessibleName = fromLabel.Text; to.AccessibleName = toLabel.Text;
        mode.Items.Clear();
        mode.Items.Add(new LocalizedOption<MediaRangeMode>(MediaRangeMode.Full, text["Range.Full"]));
        mode.Items.Add(new LocalizedOption<MediaRangeMode>(MediaRangeMode.Custom, text["Range.Custom"]));
        mode.SelectedIndex = selected == MediaRangeMode.Custom ? 1 : 0;
        UpdateVisibility();
    }

    private void UpdateVisibility()
    {
        var custom = IsCustom;
        fromLabel.Visible = from.Visible = toLabel.Visible = to.Visible = custom;
        if (CompactLayout || ResponsiveLayout) LayoutControls();
        ModeChanged?.Invoke(this, EventArgs.Empty);
    }

    private void LayoutControls()
    {
        if (arranging) return;
        if (CompactLayout || ResponsiveLayout)
        {
            arranging = true;
            try
            {
                var width = Math.Max(0, ClientSize.Width);
                if (width == 0) { foreach (var child in Controls) child.SetBounds(0, 0, 0, 0); Height = 0; return; }
                var line = AppTypography.TextHeight(24); var field = AppTypography.TextHeight(34);
                var labelWidth = DashboardVisual.TextWidth(label, label.Text) + 4;
                var fromWidth = DashboardVisual.TextWidth(fromLabel, fromLabel.Text) + 4;
                var toWidth = DashboardVisual.TextWidth(toLabel, toLabel.Text) + 4;
                var modeWidth = AppTypography.TextHeight(180); var timeWidth = AppTypography.TextHeight(100);
                var inlineWidth = labelWidth + 8 + modeWidth + (IsCustom ? 16 + fromWidth + 4 + timeWidth + 12 + toWidth + 4 + timeWidth : 0);
                var inline = !CompactLayout && width >= inlineWidth;
                if (inline)
                {
                    label.SetBounds(0, (field - line) / 2, labelWidth, line); mode.SetBounds(label.Right + 8, 0, modeWidth, field);
                    fromLabel.SetBounds(mode.Right + 16, label.Top, fromWidth, line); from.SetBounds(fromLabel.Right + 4, 0, timeWidth, field);
                    toLabel.SetBounds(from.Right + 12, label.Top, toWidth, line); to.SetBounds(toLabel.Right + 4, 0, timeWidth, field);
                }
                else
                {
                    label.SetBounds(0, 0, width, line); mode.SetBounds(0, label.Bottom + 2, width, field);
                    var paired = width >= AppTypography.TextHeight(240); var half = (width - 10) / 2;
                    fromLabel.SetBounds(0, mode.Bottom + 8, paired ? half : width, line); from.SetBounds(0, fromLabel.Bottom + 2, fromLabel.Width, field);
                    toLabel.SetBounds(paired ? half + 10 : 0, paired ? fromLabel.Top : from.Bottom + 8, fromLabel.Width, line); to.SetBounds(toLabel.Left, toLabel.Bottom + 2, toLabel.Width, field);
                }
                if (!IsCustom) foreach (var child in new Control[] { fromLabel, from, toLabel, to }) child.SetBounds(0, 0, 0, 0);
                Height = IsCustom ? Math.Max(mode.Bottom, to.Bottom) : mode.Bottom;
                AppUi.BindBackground(this, AppThemeTokens.Surface);
            }
            finally { arranging = false; }
            return;
        }
        var available = Math.Max(450, Width);
        label.SetBounds(0, 7, 88, 24);
        mode.SetBounds(90, 0, 142, 34);
        fromLabel.SetBounds(242, 7, 30, 24);
        from.SetBounds(274, 0, Math.Max(66, Math.Min(96, (available - 338) / 2)), 34);
        toLabel.SetBounds(from.Right + 8, 7, 28, 24);
        to.SetBounds(toLabel.Right + 4, 0, Math.Max(66, Math.Min(96, available - toLabel.Right - 4)), 34);
        AppTypography.ReflowRows(this);
        if (AppTypography.Scale > 1.25) {
            label.Width = AppTypography.TextHeight(88); mode.Left = label.Right + 4; mode.Width = AppTypography.TextHeight(142);
            if (IsCustom) { fromLabel.SetBounds(0, mode.Bottom + 6, AppTypography.TextHeight(36), AppTypography.TextHeight(24)); from.SetBounds(fromLabel.Right + 4, mode.Bottom + 2, AppTypography.TextHeight(96), AppTypography.TextHeight(34)); toLabel.SetBounds(from.Right + 8, fromLabel.Top, AppTypography.TextHeight(32), AppTypography.TextHeight(24)); to.SetBounds(toLabel.Right + 4, from.Top, AppTypography.TextHeight(96), AppTypography.TextHeight(34)); }
        }
    }

    private void LanguageChanged(object? sender, EventArgs e) => Application.RunOnUIThread(ApplyLocalization);

    protected override void Dispose(bool disposing)
    {
        if (disposing) text.LanguageChanged -= LanguageChanged;
        base.Dispose(disposing);
    }
}
