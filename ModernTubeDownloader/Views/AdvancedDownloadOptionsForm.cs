using ModernFormsNext;
using ModernTubeDownloader.Localization;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Settings;
using ModernTubeDownloader.Theming;
using ModernTubeDownloader.Utilities;

namespace ModernTubeDownloader.Views;

internal sealed class AdvancedDownloadOptionsForm : Form
{
    private readonly LocalizationService text;
    private readonly AppSettings settings;
    private readonly PreferredVideoContainer selectedContainer;
    private readonly Panel root;
    private readonly Panel scroll;
    private readonly Panel content;
    private readonly CheckBox subtitleEnabled;
    private readonly ComboBox subtitleSource;
    private readonly CheckedListBox subtitleLanguages;
    private readonly TextBox extraLanguages;
    private readonly ComboBox subtitleFormat;
    private readonly CheckBox subtitleEmbed;
    private readonly CheckBox subtitleKeep;
    private readonly ComboBox sponsorMode;
    private readonly TextBox sponsorCategories;
    private readonly Label sponsorWarning;
    private readonly Label feedback;
    private readonly Button accept;
    private readonly Button cancel;

    public SubtitleOptions Subtitles { get; private set; }
    public SponsorBlockOptions SponsorBlock { get; private set; }

    public AdvancedDownloadOptionsForm(LocalizationService text, AppSettings settings, VideoMetadata? metadata,
        SubtitleOptions initialSubtitles, SponsorBlockOptions initialSponsorBlock,
        PreferredVideoContainer selectedContainer)
    {
        this.text = text;
        this.settings = settings;
        this.selectedContainer = selectedContainer;
        Subtitles = initialSubtitles.Copy();
        SponsorBlock = initialSponsorBlock.Copy();
        Text = text["Advanced.Title"];
        AppBranding.Apply(this);
        Size = new System.Drawing.Size(690, 690);
        MinimumSize = new System.Drawing.Size(580, 520);
        AccessibilityObject.AutomationId = "AdvancedDownloadOptionsWindow";
        root = new Panel { Dock = DockStyle.Fill };
        AppUi.BindBackground(root);

        scroll = new AppScrollPanel { AutoScroll = true, AccessibleAutomationId = "AdvancedOptionsScroll" };
        AppUi.BindBackground(scroll);
        content = new Panel { Height = 770 };
        AppUi.Card(content);

        var subtitleHeading = AppUi.Heading(text["Settings.Subtitles"], 17f);
        subtitleHeading.SetBounds(20, 16, 590, 30);
        subtitleEnabled = Check(text["Advanced.DownloadSubtitles"], 54, "AdvancedSubtitlesEnabled");
        subtitleSource = Combo(116, "AdvancedSubtitleSource");
        var sourceLabel = AppUi.Muted();
        sourceLabel.Text = text["Advanced.Source"];
        sourceLabel.SetBounds(20, 94, 170, 22);
        foreach (var source in Enum.GetValues<SubtitleSource>())
            subtitleSource.Items.Add(new LocalizedOption<SubtitleSource>(source, text[$"SubtitleSource.{source}"]));
        Select(subtitleSource, item => item is LocalizedOption<SubtitleSource> option && option.Value == initialSubtitles.Source);

        var languagesLabel = AppUi.Muted();
        languagesLabel.Text = text["Advanced.Languages"];
        languagesLabel.SetBounds(20, 166, 590, 22);
        subtitleLanguages = new CheckedListBox { AccessibleAutomationId = "AdvancedSubtitleLanguages" };
        AppUi.Input(subtitleLanguages);
        subtitleLanguages.SetBounds(20, 190, 590, 122);
        IReadOnlyList<SubtitleTrack> tracks = metadata is null ? [] : SubtitleTrackReader.Read(metadata);
        var available = tracks.Select(track => track.LanguageCode).Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase).ToArray();
        for (var index = 0; index < available.Length; index++)
        {
            var code = available[index];
            var displayName = tracks.First(track => track.LanguageCode.Equals(code, StringComparison.OrdinalIgnoreCase)).DisplayName;
            subtitleLanguages.Items.Add(new LocalizedOption<string>(code, $"{displayName} ({code})"));
            subtitleLanguages.SetItemChecked(index, initialSubtitles.Languages.Contains(available[index], StringComparer.OrdinalIgnoreCase));
        }
        extraLanguages = new TextBox { AccessibleAutomationId = "AdvancedSubtitleLanguageCodes" };
        AppUi.Input(extraLanguages);
        extraLanguages.Placeholder = text["Advanced.LanguageCodesHint"];
        extraLanguages.Text = string.Join(", ", initialSubtitles.Languages.Where(code =>
            !available.Contains(code, StringComparer.OrdinalIgnoreCase)));
        if (available.Length == 0 && string.IsNullOrWhiteSpace(extraLanguages.Text))
            extraLanguages.Text = settings.Language;
        extraLanguages.SetBounds(20, 320, 590, 36);

        var formatLabel = AppUi.Muted();
        formatLabel.Text = text["Advanced.Format"];
        formatLabel.SetBounds(20, 370, 170, 22);
        subtitleFormat = Combo(392, "AdvancedSubtitleFormat");
        foreach (var format in Enum.GetValues<SubtitleFormat>())
            subtitleFormat.Items.Add(new LocalizedOption<SubtitleFormat>(format, text[$"SubtitleFormat.{format}"]));
        Select(subtitleFormat, item => item is LocalizedOption<SubtitleFormat> option && option.Value == initialSubtitles.Format);
        subtitleEmbed = Check(text["Settings.SubtitlesEmbed"], 436, "AdvancedSubtitleEmbed");
        subtitleEmbed.Checked = initialSubtitles.Embed;
        subtitleKeep = Check(text["Settings.SubtitlesKeepFiles"], 470, "AdvancedSubtitleKeepFiles");
        subtitleKeep.Checked = initialSubtitles.KeepFiles;
        subtitleEnabled.Checked = initialSubtitles.Enabled;

        var sponsorHeading = AppUi.Heading(text["Settings.SponsorBlock"], 17f);
        sponsorHeading.SetBounds(20, 520, 590, 30);
        var sponsorLabel = AppUi.Muted();
        sponsorLabel.Text = text["Advanced.SponsorMode"];
        sponsorLabel.SetBounds(20, 556, 170, 22);
        sponsorMode = Combo(578, "AdvancedSponsorBlockMode");
        sponsorMode.Items.Add(new LocalizedOption<SponsorBlockMode?>(null, text["Advanced.UseDefaults"]));
        foreach (var mode in Enum.GetValues<SponsorBlockMode>())
            sponsorMode.Items.Add(new LocalizedOption<SponsorBlockMode?>((SponsorBlockMode?)mode, text[$"SponsorBlockMode.{mode}"]));
        var sponsorMatchesDefaults = initialSponsorBlock.Mode == settings.SponsorBlockDefaults.Mode &&
            initialSponsorBlock.Categories.SequenceEqual(settings.SponsorBlockDefaults.Categories, StringComparer.OrdinalIgnoreCase);
        Select(sponsorMode, item => item is LocalizedOption<SponsorBlockMode?> option &&
            option.Value == (sponsorMatchesDefaults ? null : initialSponsorBlock.Mode));
        sponsorCategories = new TextBox { AccessibleAutomationId = "AdvancedSponsorBlockCategories" };
        AppUi.Input(sponsorCategories);
        sponsorCategories.Text = string.Join(", ", initialSponsorBlock.Categories);
        sponsorCategories.SetBounds(20, 624, 590, 36);
        sponsorCategories.Enabled = !sponsorMatchesDefaults;
        sponsorMode.SelectedIndexChanged += (_, _) =>
        {
            var useDefaults = (sponsorMode.SelectedItem as LocalizedOption<SponsorBlockMode?>)?.Value is null;
            sponsorCategories.Enabled = !useDefaults;
            if (useDefaults)
                sponsorCategories.Text = string.Join(", ", settings.SponsorBlockDefaults.Categories);
            UpdateSponsorWarning();
        };
        sponsorWarning = AppUi.Muted();
        sponsorWarning.Multiline = true;
        sponsorWarning.SetBounds(20, 664, 590, 42);
        UpdateSponsorWarning();
        var authInfo = AppUi.Muted();
        authInfo.Multiline = true;
        authInfo.Text = settings.UseCookieFile
            ? text["Advanced.CookiesOn"]
            : text["Advanced.CookiesOff"];
        authInfo.SetBounds(20, 712, 590, 44);
        if (metadata is not null && available.Length == 0)
        {
            subtitleEnabled.Checked = false;
            subtitleEnabled.Enabled = false;
            subtitleSource.Enabled = false;
            subtitleLanguages.Enabled = false;
            extraLanguages.Enabled = false;
            subtitleFormat.Enabled = false;
            subtitleEmbed.Enabled = false;
            subtitleKeep.Enabled = false;
        }
        content.Controls.AddRange([subtitleHeading, subtitleEnabled, sourceLabel, subtitleSource, languagesLabel,
            subtitleLanguages, extraLanguages, formatLabel, subtitleFormat, subtitleEmbed, subtitleKeep,
            sponsorHeading, sponsorLabel, sponsorMode, sponsorCategories, sponsorWarning, authInfo]);
        scroll.Controls.Add(content);

        feedback = AppUi.Muted();
        accept = new Button { Text = text["Advanced.Apply"], AccessibleAutomationId = "AdvancedOptionsApply" };
        cancel = new Button { Text = text["Common.Cancel"], AccessibleAutomationId = "AdvancedOptionsCancel" };
        AppUi.Primary(accept);
        AppUi.Secondary(cancel);
        accept.Click += (_, _) => Apply();
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        root.Controls.AddRange([scroll, feedback, accept, cancel]);
        Controls.Add(root);
        root.SizeChanged += (_, _) => LayoutControls();
        LayoutControls();
    }

    private CheckBox Check(string label, int top, string id)
    {
        var control = new CheckBox { Text = label, AccessibleAutomationId = id };
        AppUi.BindForeground(control);
        control.SetBounds(20, top, 590, 30);
        return control;
    }

    private ComboBox Combo(int top, string id)
    {
        var control = new ComboBox { AccessibleAutomationId = id };
        AppUi.Input(control);
        control.SetBounds(20, top, 290, 36);
        return control;
    }

    private static void Select(ComboBox combo, Func<object, bool> predicate)
    {
        for (var index = 0; index < combo.Items.Count; index++)
            if (predicate(combo.Items[index])) { combo.SelectedIndex = index; return; }
        combo.SelectedIndex = 0;
    }

    private void LayoutControls()
    {
        var width = Math.Max(540, root.ClientSize.Width);
        scroll.SetBounds(0, 0, width, Math.Max(300, root.ClientSize.Height - 74));
        content.SetBounds(12 + scroll.DisplayRectangle.X, scroll.DisplayRectangle.Y + 12,
            Math.Max(520, scroll.ClientSize.Width - 36), 770);
        var inner = content.Width - 40;
        foreach (var control in content.Controls)
            if (control.Left == 20 && control.Width >= 290)
                control.Width = control is ComboBox ? Math.Min(290, inner) : inner;
        feedback.SetBounds(16, root.ClientSize.Height - 59, Math.Max(120, width - 270), 36);
        cancel.SetBounds(width - 244, root.ClientSize.Height - 60, 104, 40);
        accept.SetBounds(width - 132, root.ClientSize.Height - 60, 116, 40);
    }

    private void Apply()
    {
        try
        {
            var languages = Enumerable.Range(0, subtitleLanguages.Items.Count)
                .Where(subtitleLanguages.GetItemChecked)
                .Select(index => (subtitleLanguages.Items[index] as LocalizedOption<string>)?.Value ?? string.Empty)
                .Concat(extraLanguages.Text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var subtitles = new SubtitleOptions
            {
                Enabled = subtitleEnabled.Checked,
                Source = (subtitleSource.SelectedItem as LocalizedOption<SubtitleSource>)?.Value ?? SubtitleSource.Manual,
                Languages = languages,
                Format = (subtitleFormat.SelectedItem as LocalizedOption<SubtitleFormat>)?.Value ?? SubtitleFormat.Auto,
                Embed = subtitleEmbed.Checked,
                KeepFiles = subtitleKeep.Checked
            };
            subtitles.Validate();
            var selectedMode = (sponsorMode.SelectedItem as LocalizedOption<SponsorBlockMode?>)?.Value;
            var sponsorBlock = selectedMode is null ? settings.SponsorBlockDefaults.Copy() : new SponsorBlockOptions
            {
                Mode = selectedMode.Value,
                Categories = sponsorCategories.Text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList()
            };
            sponsorBlock.ToYtDlpCategories();
            var issue = DownloadOptionCompatibilityValidator.Check(MediaTimeRange.Full, subtitles,
                sponsorBlock, selectedContainer);
            if (issue != DownloadOptionIssue.None)
            {
                feedback.Text = text[DownloadOptionCompatibilityValidator.MessageKey(issue)];
                return;
            }
            Subtitles = subtitles;
            SponsorBlock = sponsorBlock;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (ArgumentException)
        {
            feedback.Text = text["Settings.InvalidValue"];
        }
    }

    private void UpdateSponsorWarning()
    {
        if (sponsorWarning is null) return;
        var mode = (sponsorMode.SelectedItem as LocalizedOption<SponsorBlockMode?>)?.Value
            ?? settings.SponsorBlockDefaults.Mode;
        sponsorWarning.Text = mode switch
        {
            SponsorBlockMode.Remove => text["SponsorBlock.RemoveExperimentalWarning"],
            SponsorBlockMode.Mark when selectedContainer != PreferredVideoContainer.Mkv => text["Advanced.MarkRequiresMkv"],
            SponsorBlockMode.Mark => text["Settings.SponsorBlockHint"],
            _ => string.Empty
        };
    }
}
