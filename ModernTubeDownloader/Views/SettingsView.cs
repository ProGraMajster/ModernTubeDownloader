using ModernFormsNext;
using ModernTubeDownloader.Localization;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Settings;
using ModernTubeDownloader.Theming;
using ModernTubeDownloader.Utilities;

namespace ModernTubeDownloader.Views;

internal sealed class SettingsView : UserControl
{
    private readonly AppServices services;
    private readonly LocalizationService text;
    private readonly Panel header;
    private readonly Label pageTitle;
    private readonly Label pageSubtitle;
    private readonly Panel scroll;
    private readonly Dictionary<Control, string> localizedText = [];
    private readonly Panel generalCard;
    private readonly Panel toolsCard;
    private readonly Panel behaviorCard;
    private readonly Panel appearanceCard;
    private readonly Panel advancedCard;
    private readonly Panel actionsCard;
    private readonly TextBox temporaryDirectory;
    private readonly Button browseTemporary;
    private readonly TextBox outputDirectory;
    private readonly Button browseOutput;
    private readonly ComboBox defaultQuality;
    private readonly ComboBox concurrency;
    private readonly CheckBox keepTemporary;
    private readonly Label concurrencyHint;
    private readonly Panel ytDlpToolCard;
    private readonly Panel ffmpegToolCard;
    private readonly Label ytDlpDescription;
    private readonly Label ffmpegDescription;
    private readonly Label ytDlpStatus;
    private readonly Label ffmpegStatus;
    private readonly ProgressBar ytDlpProgress;
    private readonly ProgressBar ffmpegProgress;
    private readonly Button checkUpdates;
    private readonly Button retrySetup;
    private readonly TextBox filenameTemplate;
    private readonly CheckBox overwrite;
    private readonly ComboBox conflict;
    private readonly CheckBox metadataJson;
    private readonly CheckBox thumbnails;
    private readonly CheckBox setFileCreationTime;
    private readonly CheckBox saveMetadataSidecar;
    private readonly ComboBox theme;
    private readonly ComboBox language;
    private readonly TextBox customArguments;
    private readonly Label customArgumentsHint;
    private readonly CheckBox useCustomYtDlp;
    private readonly TextBox ytDlpPath;
    private readonly Button browseYtDlp;
    private readonly CheckBox useCustomFfmpeg;
    private readonly TextBox ffmpegPath;
    private readonly Button browseFfmpeg;
    private readonly Button save;
    private readonly Button logs;
    private readonly Label feedback;
    private bool applyingValues;

    public SettingsView(AppServices services)
    {
        this.services = services;
        text = services.Localization;
        Dock = DockStyle.Fill;
        AppUi.BindBackground(this);

        header = new Panel();
        AppUi.BindBackground(header);
        pageTitle = AppUi.Heading(string.Empty, 22f);
        pageSubtitle = AppUi.Muted();
        header.Controls.AddRange([pageTitle, pageSubtitle]);
        scroll = new Panel { AutoScroll = true };
        AppUi.BindBackground(scroll);

        generalCard = CreateSection(380, "Settings.General", "Settings.GeneralDescription");
        AddRowLabel(generalCard, "Settings.TemporaryDirectory", 94);
        temporaryDirectory = CreateTextBox(generalCard);
        browseTemporary = CreateButton(generalCard, "Settings.Browse", primary: false);
        browseTemporary.Click += BrowseTemporary;
        AddRowLabel(generalCard, "Settings.OutputDirectory", 146);
        outputDirectory = CreateTextBox(generalCard);
        browseOutput = CreateButton(generalCard, "Settings.Browse", primary: false);
        browseOutput.Click += BrowseOutput;
        AddRowLabel(generalCard, "Settings.DefaultQuality", 198);
        defaultQuality = CreateCombo(generalCard);
        AddRowLabel(generalCard, "Settings.Concurrency", 250);
        concurrency = CreateCombo(generalCard);
        foreach (var count in new[] { 1, 2, 3 })
            concurrency.Items.Add(count);
        keepTemporary = CreateCheck(generalCard, "Settings.KeepTemporary");
        concurrencyHint = AppUi.Muted();
        BindText(concurrencyHint, "Settings.ConcurrencyHint");
        concurrencyHint.Multiline = true;
        generalCard.Controls.Add(concurrencyHint);

        toolsCard = CreateSection(332, "Settings.Tools", "Settings.ToolsDescription");
        ytDlpToolCard = CreateToolCard(toolsCard, "yt-dlp", "Settings.YtDlpDescription", out ytDlpDescription, out ytDlpStatus, out ytDlpProgress);
        ffmpegToolCard = CreateToolCard(toolsCard, "FFmpeg + ffprobe", "Settings.FfmpegDescription", out ffmpegDescription, out ffmpegStatus, out ffmpegProgress);
        checkUpdates = CreateButton(toolsCard, "Settings.CheckUpdates", primary: true);
        retrySetup = CreateButton(toolsCard, "Settings.RetrySetup", primary: false);
        checkUpdates.Click += CheckForUpdates;
        retrySetup.Click += RetrySetup;

        behaviorCard = CreateSection(414, "Settings.Behavior", "Settings.BehaviorDescription");
        AddRowLabel(behaviorCard, "Settings.FilenameTemplate", 94);
        filenameTemplate = CreateTextBox(behaviorCard);
        overwrite = CreateCheck(behaviorCard, "Settings.Overwrite");
        AddRowLabel(behaviorCard, "Settings.Conflict", 198);
        conflict = CreateCombo(behaviorCard);
        metadataJson = CreateCheck(behaviorCard, "Settings.MetadataJson");
        thumbnails = CreateCheck(behaviorCard, "Settings.Thumbnails");
        setFileCreationTime = CreateCheck(behaviorCard, "Settings.SetFileCreationTime");
        saveMetadataSidecar = CreateCheck(behaviorCard, "Settings.SaveMetadataSidecar");

        appearanceCard = CreateSection(210, "Settings.Appearance", "Settings.AppearanceDescription");
        AddRowLabel(appearanceCard, "Settings.Theme", 96);
        theme = CreateCombo(appearanceCard);
        AddRowLabel(appearanceCard, "Settings.Language", 148);
        language = CreateCombo(appearanceCard);
        theme.SelectedIndexChanged += ThemeSelected;
        language.SelectedIndexChanged += LanguageSelected;

        advancedCard = CreateSection(414, "Settings.Advanced", "Settings.AdvancedDescription");
        AddRowLabel(advancedCard, "Settings.CustomArguments", 96);
        customArguments = new TextBox { MultiLine = true };
        AppUi.Input(customArguments);
        advancedCard.Controls.Add(customArguments);
        customArgumentsHint = AppUi.Muted();
        BindText(customArgumentsHint, "Settings.CustomArgumentsHint");
        customArgumentsHint.Multiline = true;
        advancedCard.Controls.Add(customArgumentsHint);
        useCustomYtDlp = CreateCheck(advancedCard, "Settings.UseCustomYtDlp");
        AddRowLabel(advancedCard, "Settings.CustomYtDlp", 266);
        ytDlpPath = CreateTextBox(advancedCard);
        browseYtDlp = CreateButton(advancedCard, "Settings.Browse", primary: false);
        browseYtDlp.Click += BrowseYtDlp;
        useCustomFfmpeg = CreateCheck(advancedCard, "Settings.UseCustomFfmpeg");
        AddRowLabel(advancedCard, "Settings.CustomFfmpeg", 354);
        ffmpegPath = CreateTextBox(advancedCard);
        browseFfmpeg = CreateButton(advancedCard, "Settings.Browse", primary: false);
        browseFfmpeg.Click += BrowseFfmpeg;
        useCustomYtDlp.CheckedChanged += (_, _) => ytDlpPath.Enabled = useCustomYtDlp.Checked;
        useCustomFfmpeg.CheckedChanged += (_, _) => ffmpegPath.Enabled = useCustomFfmpeg.Checked;

        actionsCard = new Panel { Height = 84 };
        AppUi.Card(actionsCard);
        save = CreateButton(actionsCard, "Settings.Save", primary: true);
        logs = CreateButton(actionsCard, "Settings.OpenLogs", primary: false);
        feedback = AppUi.Muted();
        actionsCard.Controls.Add(feedback);
        save.Click += SaveClicked;
        logs.Click += (_, _) => ShellService.OpenFolderForFile(services.Paths.LogDirectory);

        scroll.Controls.AddRange([generalCard, toolsCard, behaviorCard, appearanceCard, advancedCard, actionsCard]);
        Controls.AddRange([header, scroll]);
        SizeChanged += ViewSizeChanged;
        scroll.SizeChanged += ScrollSizeChanged;
        services.Tools.Changed += ToolsChanged;
        text.LanguageChanged += LanguageChanged;
        ApplyLocalization();
        LoadValues();
        UpdateLayout();
    }

    private Panel CreateSection(int height, string titleKey, string descriptionKey)
    {
        var card = new Panel { Height = height };
        AppUi.Card(card);
        var title = AppUi.Heading(string.Empty, 15f);
        BindText(title, titleKey);
        title.SetBounds(24, 20, 600, 28);
        var description = AppUi.Muted();
        BindText(description, descriptionKey);
        description.SetBounds(24, 50, 700, 28);
        card.Controls.AddRange([title, description]);
        return card;
    }

    private void AddRowLabel(Panel panel, string key, int top)
    {
        var label = AppUi.Muted();
        BindText(label, key);
        label.SetBounds(24, top + 7, 200, 24);
        panel.Controls.Add(label);
    }

    private TextBox CreateTextBox(Panel panel)
    {
        var control = new TextBox { Height = 36 };
        AppUi.Input(control);
        panel.Controls.Add(control);
        return control;
    }

    private ComboBox CreateCombo(Panel panel)
    {
        var control = new ComboBox { Height = 36 };
        AppUi.Input(control);
        panel.Controls.Add(control);
        return control;
    }

    private CheckBox CreateCheck(Panel panel, string key)
    {
        var control = new CheckBox { Height = 30 };
        BindText(control, key);
        AppUi.BindForeground(control);
        control.Style.BackgroundColor = SkiaSharp.SKColors.Transparent;
        panel.Controls.Add(control);
        return control;
    }

    private Button CreateButton(Panel panel, string key, bool primary)
    {
        var control = new Button { Height = 36 };
        BindText(control, key);
        if (primary)
            AppUi.Primary(control);
        else
            AppUi.Secondary(control);
        panel.Controls.Add(control);
        return control;
    }

    private Panel CreateToolCard(
        Panel owner,
        string title,
        string descriptionKey,
        out Label description,
        out Label status,
        out ProgressBar progress)
    {
        var card = new Panel { Height = 74 };
        AppUi.Card(card, secondary: true);
        var titleLabel = AppUi.Heading(title, 11.5f);
        titleLabel.SetBounds(16, 10, 190, 24);
        description = AppUi.Muted();
        BindText(description, descriptionKey);
        description.TextAlign = ContentAlignment.TopRight;
        status = AppUi.Muted();
        status.SetBounds(16, 40, 600, 22);
        progress = new ProgressBar { Minimum = 0, Maximum = 100, Visible = false };
        card.Controls.AddRange([titleLabel, description, status, progress]);
        owner.Controls.Add(card);
        return card;
    }

    private void BindText(Control control, string key)
    {
        localizedText[control] = key;
        control.Text = text[key];
    }

    private void UpdateLayout()
    {
        const int page = 24;
        var width = Math.Max(560, ClientSize.Width - (page * 2));
        header.SetBounds(page, 14, width, 60);
        pageTitle.SetBounds(0, 0, width, 32);
        pageSubtitle.SetBounds(0, 34, width, 24);
        scroll.SetBounds(0, 82, ClientSize.Width, Math.Max(120, ClientSize.Height - 82));

        var cardWidth = Math.Max(540, scroll.ClientSize.Width - 60);
        var top = 0;
        foreach (var card in new[] { generalCard, toolsCard, behaviorCard, appearanceCard, advancedCard, actionsCard })
        {
            card.SetBounds(page, top, cardWidth, card.Height);
            top = card.Bottom + 14;
            if (!ReferenceEquals(card, actionsCard))
                ResizeSectionHeader(card);
        }

        var fieldLeft = cardWidth < 720 ? 218 : 248;
        var browseWidth = 106;
        var fieldRight = cardWidth - 24;
        LayoutPathRow(temporaryDirectory, browseTemporary, generalCard, 94, fieldLeft, fieldRight, browseWidth);
        LayoutPathRow(outputDirectory, browseOutput, generalCard, 146, fieldLeft, fieldRight, browseWidth);
        defaultQuality.SetBounds(fieldLeft, 198, Math.Min(250, fieldRight - fieldLeft), 36);
        concurrency.SetBounds(fieldLeft, 250, Math.Min(160, fieldRight - fieldLeft), 36);
        keepTemporary.SetBounds(fieldLeft, 300, Math.Max(280, fieldRight - fieldLeft), 30);
        concurrencyHint.SetBounds(fieldLeft, 332, Math.Max(260, fieldRight - fieldLeft), 38);

        LayoutToolCard(ytDlpToolCard, ytDlpDescription, ytDlpStatus, ytDlpProgress, toolsCard, 92);
        LayoutToolCard(ffmpegToolCard, ffmpegDescription, ffmpegStatus, ffmpegProgress, toolsCard, 174);
        checkUpdates.SetBounds(24, 270, 174, 38);
        retrySetup.SetBounds(208, 270, 158, 38);

        filenameTemplate.SetBounds(fieldLeft, 94, Math.Max(280, fieldRight - fieldLeft), 36);
        overwrite.SetBounds(fieldLeft, 146, Math.Max(260, fieldRight - fieldLeft), 30);
        conflict.SetBounds(fieldLeft, 198, Math.Min(280, fieldRight - fieldLeft), 36);
        metadataJson.SetBounds(fieldLeft, 248, Math.Max(260, fieldRight - fieldLeft), 30);
        thumbnails.SetBounds(fieldLeft, 286, Math.Max(260, fieldRight - fieldLeft), 30);
        setFileCreationTime.SetBounds(24, 324, cardWidth - 48, 30);
        saveMetadataSidecar.SetBounds(24, 362, cardWidth - 48, 30);

        theme.SetBounds(fieldLeft, 96, Math.Min(240, fieldRight - fieldLeft), 36);
        language.SetBounds(fieldLeft, 148, Math.Min(240, fieldRight - fieldLeft), 36);

        customArguments.SetBounds(fieldLeft, 96, Math.Max(280, fieldRight - fieldLeft), 72);
        customArgumentsHint.SetBounds(fieldLeft, 174, Math.Max(280, fieldRight - fieldLeft), 42);
        useCustomYtDlp.SetBounds(fieldLeft, 220, Math.Max(280, fieldRight - fieldLeft), 30);
        LayoutPathRow(ytDlpPath, browseYtDlp, advancedCard, 266, fieldLeft, fieldRight, browseWidth);
        useCustomFfmpeg.SetBounds(fieldLeft, 310, Math.Max(280, fieldRight - fieldLeft), 30);
        LayoutPathRow(ffmpegPath, browseFfmpeg, advancedCard, 354, fieldLeft, fieldRight, browseWidth);

        save.SetBounds(24, 22, 166, 40);
        logs.SetBounds(200, 22, 178, 40);
        feedback.SetBounds(394, 30, Math.Max(120, cardWidth - 418), 28);
    }

    private static void ResizeSectionHeader(Panel card)
    {
        if (card.Controls.Count < 2)
            return;
        card.Controls[0].Width = card.Width - 48;
        card.Controls[1].Width = card.Width - 48;
    }

    private static void LayoutPathRow(TextBox input, Button browse, Panel card, int top, int fieldLeft, int fieldRight, int browseWidth)
    {
        browse.SetBounds(fieldRight - browseWidth, top, browseWidth, 36);
        input.SetBounds(fieldLeft, top, Math.Max(150, browse.Left - fieldLeft - 10), 36);
    }

    private static void LayoutToolCard(Panel card, Label description, Label status, ProgressBar progress, Panel owner, int top)
    {
        card.SetBounds(24, top, owner.Width - 48, 74);
        description.SetBounds(208, 11, Math.Max(150, card.Width - 224), 24);
        status.Width = card.Width - 32;
        progress.SetBounds(16, 65, card.Width - 32, 5);
    }

    private void LoadValues()
    {
        applyingValues = true;
        try
        {
            var value = services.Settings.Current;
            temporaryDirectory.Text = value.TemporaryDirectory;
            outputDirectory.Text = value.FinalOutputDirectory;
            PopulateQuality(value.DefaultQualityPresetId);
            SelectCombo(concurrency, item => item is int count && count == value.MaxSimultaneousDownloads);
            keepTemporary.Checked = value.KeepTemporaryFilesAfterSuccessfulMerge;
            useCustomYtDlp.Checked = value.UseCustomYtDlp;
            useCustomFfmpeg.Checked = value.UseCustomFfmpeg;
            ytDlpPath.Text = value.CustomYtDlpPath ?? string.Empty;
            ffmpegPath.Text = value.CustomFfmpegPath ?? string.Empty;
            ytDlpPath.Enabled = useCustomYtDlp.Checked;
            ffmpegPath.Enabled = useCustomFfmpeg.Checked;
            filenameTemplate.Text = value.FilenameTemplate;
            overwrite.Checked = value.OverwriteExistingFiles;
            PopulateConflict(value.ConflictBehavior);
            metadataJson.Checked = value.StoreMetadataJson;
            thumbnails.Checked = value.DownloadThumbnail;
            setFileCreationTime.Checked = value.SetFileCreationTimeFromMediaPublishDate;
            saveMetadataSidecar.Checked = value.SaveMetadataJsonSidecar;
            customArguments.Text = value.CustomYtDlpArguments;
            PopulateTheme(value.ThemeMode);
            PopulateLanguage(value.Language);
        }
        finally
        {
            applyingValues = false;
        }
        UpdateToolStatus();
    }

    private void PopulateQuality(string selectedId)
    {
        defaultQuality.SelectedIndex = -1;
        defaultQuality.Items.Clear();
        foreach (var preset in QualityPreset.All)
            defaultQuality.Items.Add(new LocalizedOption<QualityPreset>(preset, text[$"Quality.{preset.Id}"]));
        SelectCombo(defaultQuality, item => item is LocalizedOption<QualityPreset> option && option.Value.Id == selectedId);
    }

    private void PopulateConflict(FileConflictBehavior selected)
    {
        conflict.SelectedIndex = -1;
        conflict.Items.Clear();
        foreach (var value in Enum.GetValues<FileConflictBehavior>())
            conflict.Items.Add(new LocalizedOption<FileConflictBehavior>(value, text[$"Conflict.{value}"]));
        SelectCombo(conflict, item => item is LocalizedOption<FileConflictBehavior> option && option.Value == selected);
    }

    private void PopulateTheme(AppThemeMode selected)
    {
        theme.SelectedIndex = -1;
        theme.Items.Clear();
        foreach (var value in Enum.GetValues<AppThemeMode>())
            theme.Items.Add(new LocalizedOption<AppThemeMode>(value, text[$"Theme.{value}"]));
        SelectCombo(theme, item => item is LocalizedOption<AppThemeMode> option && option.Value == selected);
    }

    private void PopulateLanguage(string selected)
    {
        language.SelectedIndex = -1;
        language.Items.Clear();
        language.Items.Add(new LocalizedOption<string>("pl", text["Language.Polish"]));
        language.Items.Add(new LocalizedOption<string>("en", text["Language.English"]));
        SelectCombo(language, item => item is LocalizedOption<string> option && option.Value == selected);
    }

    private static void SelectCombo(ComboBox combo, Func<object, bool> predicate)
    {
        for (var index = 0; index < combo.Items.Count; index++)
        {
            if (predicate(combo.Items[index]))
            {
                combo.SelectedIndex = index;
                return;
            }
        }
        if (combo.Items.Count > 0)
            combo.SelectedIndex = 0;
    }

    private async void BrowseTemporary(object? sender, EventArgs e)
        => await BrowseFolderAsync(temporaryDirectory, text["Settings.ChooseTemporary"]);

    private async void BrowseOutput(object? sender, EventArgs e)
        => await BrowseFolderAsync(outputDirectory, text["Settings.ChooseOutput"]);

    private async Task BrowseFolderAsync(TextBox target, string title)
    {
        if (FindForm() is not { } owner)
            return;
        var dialog = new FolderBrowserDialog { Title = title, InitialDirectory = target.Text };
        if (await dialog.ShowDialog(owner) == DialogResult.OK && dialog.SelectedPath is { } path)
            target.Text = path;
    }

    private async void BrowseYtDlp(object? sender, EventArgs e)
        => await BrowseToolAsync(ytDlpPath, text["Settings.SelectYtDlp"]);

    private async void BrowseFfmpeg(object? sender, EventArgs e)
        => await BrowseToolAsync(ffmpegPath, text["Settings.SelectFfmpeg"]);

    private async Task BrowseToolAsync(TextBox target, string title)
    {
        if (FindForm() is not { } owner)
            return;
        var dialog = new OpenFileDialog
        {
            Title = title,
            AllowMultiple = false,
            InitialDirectory = System.IO.Path.GetDirectoryName(target.Text)
        };
        dialog.AddFilter(text["Settings.ExecutableFiles"], "*.exe");
        dialog.AddFilter(text["Settings.AllFiles"], "*.*");
        if (await dialog.ShowDialog(owner) == DialogResult.OK && dialog.FileNames.FirstOrDefault() is { } path)
            target.Text = path;
    }

    private async void CheckForUpdates(object? sender, EventArgs e)
    {
        feedback.Text = text["Settings.CheckingUpdates"];
        try
        {
            await services.Tools.CheckForUpdatesAsync();
            feedback.Text = text["Settings.UpdateCheckComplete"];
        }
        catch (Exception ex)
        {
            services.Logger.Error("Checking for tool updates failed.", ex);
            feedback.Text = text["Error.Generic"];
        }
    }

    private async void RetrySetup(object? sender, EventArgs e)
    {
        feedback.Text = text["Settings.RetryingSetup"];
        try
        {
            await services.Tools.RetryAsync();
            feedback.Text = services.Tools.EngineStatus == DownloadEngineStatus.Ready
                ? text["Settings.SetupReady"]
                : text["Settings.SetupAttention"];
        }
        catch (Exception ex)
        {
            services.Logger.Error("Retrying tool setup failed.", ex);
            feedback.Text = text["Error.Generic"];
        }
    }

    private async void ThemeSelected(object? sender, EventArgs e)
    {
        if (applyingValues || theme.SelectedItem is not LocalizedOption<AppThemeMode> option)
            return;
        try
        {
            await services.Appearance.SetModeAsync(option.Value);
        }
        catch (Exception ex)
        {
            services.Logger.Error("Changing the application theme failed.", ex);
            feedback.Text = text["Error.Generic"];
        }
    }

    private async void LanguageSelected(object? sender, EventArgs e)
    {
        if (applyingValues || language.SelectedItem is not LocalizedOption<string> option)
            return;
        services.Settings.Current.Language = option.Value;
        text.SetLanguage(option.Value);
        await services.Settings.SaveAsync();
    }

    private async void SaveClicked(object? sender, EventArgs e)
    {
        try
        {
            _ = CommandLineArgumentTokenizer.ParseSafeYtDlpArguments(customArguments.Text);
            var value = services.Settings.Current;
            value.TemporaryDirectory = System.IO.Path.GetFullPath(temporaryDirectory.Text.Trim());
            value.FinalOutputDirectory = System.IO.Path.GetFullPath(outputDirectory.Text.Trim());
            value.DefaultQualityPresetId = (defaultQuality.SelectedItem as LocalizedOption<QualityPreset>)?.Value.Id ?? "best";
            value.MaxSimultaneousDownloads = concurrency.SelectedItem is int count ? count : 1;
            value.KeepTemporaryFilesAfterSuccessfulMerge = keepTemporary.Checked;
            ApplyToolPaths();
            value.FilenameTemplate = filenameTemplate.Text.Trim();
            value.OverwriteExistingFiles = overwrite.Checked;
            value.ConflictBehavior = (conflict.SelectedItem as LocalizedOption<FileConflictBehavior>)?.Value ?? FileConflictBehavior.Rename;
            value.StoreMetadataJson = metadataJson.Checked;
            value.DownloadThumbnail = thumbnails.Checked;
            value.SetFileCreationTimeFromMediaPublishDate = setFileCreationTime.Checked;
            value.SaveMetadataJsonSidecar = saveMetadataSidecar.Checked;
            value.CustomYtDlpArguments = customArguments.Text.Trim();
            await services.Settings.SaveAsync();
            await services.Tools.RefreshAsync();
            feedback.Text = text["Settings.Saved"];
        }
        catch (ArgumentException ex)
        {
            services.Logger.Warning($"Settings validation failed: {ex.Message}");
            feedback.Text = text["Settings.InvalidValue"];
        }
        catch (Exception ex)
        {
            services.Logger.Error("Saving settings failed.", ex);
            feedback.Text = text["Error.Generic"];
        }
    }

    private void ApplyToolPaths()
    {
        services.Settings.Current.UseCustomYtDlp = useCustomYtDlp.Checked;
        services.Settings.Current.UseCustomFfmpeg = useCustomFfmpeg.Checked;
        services.Settings.Current.CustomYtDlpPath = string.IsNullOrWhiteSpace(ytDlpPath.Text) ? null : ytDlpPath.Text.Trim();
        services.Settings.Current.CustomFfmpegPath = string.IsNullOrWhiteSpace(ffmpegPath.Text) ? null : ffmpegPath.Text.Trim();
        services.Settings.Current.CustomFfprobePath = null;
    }

    private void ToolsChanged(object? sender, EventArgs e) => Application.RunOnUIThread(UpdateToolStatus);

    private void UpdateToolStatus()
    {
        ytDlpStatus.Text = FormatTool(services.Tools.YtDlp);
        ffmpegStatus.Text = FormatTool(services.Tools.Ffmpeg);
        UpdateProgress(ytDlpProgress, services.Tools.YtDlp);
        UpdateProgress(ffmpegProgress, services.Tools.Ffmpeg);
    }

    private static void UpdateProgress(ProgressBar progress, ToolInfo tool)
    {
        progress.Visible = tool.Status is ToolStatus.Downloading or ToolStatus.Updating;
        progress.Value = (int)Math.Clamp(tool.ProgressPercent ?? 0, 0, 100);
    }

    private string FormatTool(ToolInfo tool)
    {
        var parts = new List<string> { text[$"ToolStatus.{tool.Status}"] };
        parts.Add(tool.ManagedByApplication ? text["Tools.Managed"] : text["Tools.Custom"]);
        if (tool.Installed)
            parts.Add(text.Get("Tools.InstalledVersion", tool.InstalledVersion ?? text["Common.Unknown"]));
        else
            parts.Add(text["Tools.NotInstalled"]);
        if (!string.IsNullOrWhiteSpace(tool.LatestVersion))
            parts.Add(text.Get("Tools.LatestVersion", tool.LatestVersion));
        if (tool.ProgressPercent is { } percent)
            parts.Add(text.Get("Tools.Progress", percent.ToString("F0", text.Culture)));
        return string.Join("  •  ", parts);
    }

    private void ApplyLocalization()
    {
        pageTitle.Text = text["Settings.Title"];
        pageSubtitle.Text = text["Settings.Subtitle"];
        foreach (var pair in localizedText)
            pair.Key.Text = text[pair.Value];
        applyingValues = true;
        try
        {
            PopulateQuality((defaultQuality.SelectedItem as LocalizedOption<QualityPreset>)?.Value.Id ?? services.Settings.Current.DefaultQualityPresetId);
            PopulateConflict((conflict.SelectedItem as LocalizedOption<FileConflictBehavior>)?.Value ?? services.Settings.Current.ConflictBehavior);
            PopulateTheme((theme.SelectedItem as LocalizedOption<AppThemeMode>)?.Value ?? services.Settings.Current.ThemeMode);
            PopulateLanguage(services.Settings.Current.Language);
        }
        finally
        {
            applyingValues = false;
        }
        UpdateToolStatus();
    }

    private void ViewSizeChanged(object? sender, EventArgs e) => UpdateLayout();
    private void ScrollSizeChanged(object? sender, EventArgs e) => UpdateLayout();
    private void LanguageChanged(object? sender, EventArgs e) => Application.RunOnUIThread(ApplyLocalization);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            services.Tools.Changed -= ToolsChanged;
            text.LanguageChanged -= LanguageChanged;
            SizeChanged -= ViewSizeChanged;
            scroll.SizeChanged -= ScrollSizeChanged;
        }
        base.Dispose(disposing);
    }
}
