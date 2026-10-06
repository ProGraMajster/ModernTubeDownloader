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
        from = new TextBox { AccessibleAutomationId = automationPrefix + "RangeStart", Placeholder = "hh:mm:ss" };
        AppUi.Input(from);
        toLabel = AppUi.Muted();
        to = new TextBox { AccessibleAutomationId = automationPrefix + "RangeEnd", Placeholder = "hh:mm:ss" };
        AppUi.Input(to);
        Controls.AddRange([label, mode, fromLabel, from, toLabel, to]);
        mode.SelectedIndexChanged += (_, _) => UpdateVisibility();
        text.LanguageChanged += LanguageChanged;
        SizeChanged += (_, _) => LayoutControls();
        ApplyLocalization();
        LayoutControls();
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
        ModeChanged?.Invoke(this, EventArgs.Empty);
    }

    private void LayoutControls()
    {
        var available = Math.Max(450, Width);
        label.SetBounds(0, 7, 88, 24);
        mode.SetBounds(90, 0, 142, 34);
        fromLabel.SetBounds(242, 7, 30, 24);
        from.SetBounds(274, 0, Math.Max(66, Math.Min(96, (available - 338) / 2)), 34);
        toLabel.SetBounds(from.Right + 8, 7, 28, 24);
        to.SetBounds(toLabel.Right + 4, 0, Math.Max(66, Math.Min(96, available - toLabel.Right - 4)), 34);
    }

    private void LanguageChanged(object? sender, EventArgs e) => Application.RunOnUIThread(ApplyLocalization);

    protected override void Dispose(bool disposing)
    {
        if (disposing) text.LanguageChanged -= LanguageChanged;
        base.Dispose(disposing);
    }
}
