using ModernFormsNext;
using ModernTubeDownloader.Localization;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Settings;
using ModernTubeDownloader.Theming;
using ModernTubeDownloader.Utilities;
using ModernTubeDownloader.WebRemote;
using QRCoder;
using SkiaSharp;

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
    private readonly Panel resilienceCard;
    private readonly Panel liveCard;
    private readonly NumericUpDown liveConcurrency;
    private readonly CheckBox preserveLive;
    private readonly CheckBox resumeLive;
    private readonly CheckBox autoResumeLive;
    private readonly CheckBox preserveLiveCancel;
    private readonly Panel subtitlesCard;
    private readonly Panel sponsorCard;
    private readonly Panel cookiesCard;
    private readonly Panel appearanceCard;
    private readonly Panel browserCard;
    private readonly Panel remoteCard;
    private readonly CheckBox remoteEnabled;
    private readonly CheckBox remoteRequireAuth;
    private readonly Label remoteAuthWarning;
    private readonly NumericUpDown remotePort;
    private readonly ComboBox remoteAddress;
    private readonly Label remoteStatus;
    private readonly TextBox remoteToken;
    private readonly Button remoteCopy;
    private readonly Button remoteRegenerate;
    private readonly Button remoteOpen;
    private readonly Button remoteQrButton;
    private readonly Button remoteShowToken;
    private readonly PictureBox remoteQr;
    private SKBitmap? remoteQrImage;
    private readonly Panel advancedCard;
    private readonly Panel actionsCard;
    private readonly TextBox temporaryDirectory;
    private readonly Button browseTemporary;
    private readonly TextBox outputDirectory;
    private readonly Button browseOutput;
    private readonly ComboBox defaultQuality;
    private readonly ComboBox defaultContainer;
    private readonly ComboBox concurrency;
    private readonly CheckBox keepTemporary;
    private readonly Label concurrencyHint;
    private readonly Panel ytDlpToolCard;
    private readonly Panel ffmpegToolCard;
    private readonly Panel denoToolCard;
    private readonly Label ytDlpDescription;
    private readonly Label ffmpegDescription;
    private readonly Label denoDescription;
    private readonly Label ytDlpStatus;
    private readonly Label ffmpegStatus;
    private readonly Label denoStatus;
    private readonly ProgressBar ytDlpProgress;
    private readonly ProgressBar ffmpegProgress;
    private readonly ProgressBar denoProgress;
    private readonly Button checkUpdates;
    private readonly Button retrySetup;
    public event EventHandler? SupportedSourcesRequested;
    private readonly Button supportedSources;
    private readonly TextBox filenameTemplate;
    private readonly CheckBox overwrite;
    private readonly ComboBox conflict;
    private readonly CheckBox metadataJson;
    private readonly CheckBox thumbnails;
    private readonly CheckBox setFileCreationTime;
    private readonly CheckBox saveMetadataSidecar;
    private readonly CheckBox showQueuePositionNumbers;
    private readonly CheckBox retryFailedDownloads;
    private readonly NumericUpDown additionalRetryAttempts;
    private readonly CheckBox enableInterDownloadDelay;
    private readonly NumericUpDown minimumInterDownloadDelay;
    private readonly NumericUpDown maximumInterDownloadDelay;
    private readonly CheckBox resumeInterruptedDownloads;
    private readonly CheckBox preservePartialDownloads;
    private readonly CheckBox autoResumeAfterRestart;
    private readonly NumericUpDown minimumLiveCheck;
    private readonly NumericUpDown maximumLiveCheck;
    private readonly CheckBox subtitlesEnabled;
    private readonly CheckBox subtitlesAutomatic;
    private readonly TextBox subtitleLanguages;
    private readonly ComboBox subtitleFormat;
    private readonly CheckBox subtitleEmbed;
    private readonly CheckBox subtitleKeepFiles;
    private readonly CheckBox sponsorEnabled;
    private readonly ComboBox sponsorMode;
    private readonly TextBox sponsorCategories;
    private readonly Label sponsorHint;
    private readonly CheckBox cookiesEnabled;
    private readonly Label cookiesFileName;
    private readonly Button cookiesBrowse;
    private readonly Label cookiesHelp;
    private readonly Label cookiesWarning;
    private string? selectedCookieFilePath;
    private readonly ComboBox theme;
    private readonly ComboBox language;
    private readonly CheckBox browserEnabled;
    private readonly Label browserStatus;
    private readonly Label browserExtensionInfo;
    private readonly Button repairBrowser;
    private readonly TextBox customArguments;
    private readonly Label customArgumentsHint;
    private readonly CheckBox useCustomYtDlp;
    private readonly TextBox ytDlpPath;
    private readonly Button browseYtDlp;
    private readonly CheckBox useCustomFfmpeg;
    private readonly TextBox ffmpegPath;
    private readonly Button browseFfmpeg;
    private readonly CheckBox useCustomDeno;
    private readonly TextBox denoPath;
    private readonly Button browseDeno;
    private readonly Button save;
    private readonly Button logs;
    private readonly Label feedback;
    private bool applyingValues;
    private bool operationInProgress;

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
        scroll = new AppScrollPanel { AutoScroll = true, AccessibleAutomationId = "SettingsScroll" };
        AppUi.BindBackground(scroll);

        generalCard = CreateSection(432, "Settings.General", "Settings.GeneralDescription");
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
        AddRowLabel(generalCard, "Settings.DefaultContainer", 250);
        defaultContainer = CreateCombo(generalCard);
        defaultContainer.AccessibleAutomationId = "DefaultContainerSelector";
        AddRowLabel(generalCard, "Settings.Concurrency", 302);
        concurrency = CreateCombo(generalCard);
        concurrency.AccessibleAutomationId = "ConcurrencySelector";
        foreach (var count in new[] { 1, 2, 3 })
            concurrency.Items.Add(count);
        keepTemporary = CreateCheck(generalCard, "Settings.KeepTemporary");
        concurrencyHint = AppUi.Muted();
        BindText(concurrencyHint, "Settings.ConcurrencyHint");
        concurrencyHint.Multiline = true;
        generalCard.Controls.Add(concurrencyHint);

        toolsCard = CreateSection(414, "Settings.Tools", "Settings.ToolsDescription");
        ytDlpToolCard = CreateToolCard(toolsCard, "yt-dlp", "Settings.YtDlpDescription", out ytDlpDescription, out ytDlpStatus, out ytDlpProgress);
        ffmpegToolCard = CreateToolCard(toolsCard, "FFmpeg + ffprobe", "Settings.FfmpegDescription", out ffmpegDescription, out ffmpegStatus, out ffmpegProgress);
        denoToolCard = CreateToolCard(toolsCard, "Deno", "Settings.DenoDescription", out denoDescription, out denoStatus, out denoProgress);
        checkUpdates = CreateButton(toolsCard, "Settings.CheckUpdates", primary: true);
        retrySetup = CreateButton(toolsCard, "Settings.RetrySetup", primary: false);
        supportedSources = CreateButton(toolsCard, "Sources.Title", primary: false);
        supportedSources.AccessibleAutomationId = "SettingsSupportedSourcesButton";
        supportedSources.Click += (_, _) => SupportedSourcesRequested?.Invoke(this, EventArgs.Empty);
        checkUpdates.Click += CheckForUpdates;
        retrySetup.Click += RetrySetup;

        behaviorCard = CreateSection(454, "Settings.Behavior", "Settings.BehaviorDescription");
        AddRowLabel(behaviorCard, "Settings.FilenameTemplate", 94);
        filenameTemplate = CreateTextBox(behaviorCard);
        overwrite = CreateCheck(behaviorCard, "Settings.Overwrite");
        AddRowLabel(behaviorCard, "Settings.Conflict", 198);
        conflict = CreateCombo(behaviorCard);
        metadataJson = CreateCheck(behaviorCard, "Settings.MetadataJson");
        thumbnails = CreateCheck(behaviorCard, "Settings.Thumbnails");
        setFileCreationTime = CreateCheck(behaviorCard, "Settings.SetFileCreationTime");
        saveMetadataSidecar = CreateCheck(behaviorCard, "Settings.SaveMetadataSidecar");
        showQueuePositionNumbers = CreateCheck(behaviorCard, "Settings.ShowQueuePositionNumbers");
        showQueuePositionNumbers.AccessibleAutomationId = "ShowQueueNumbersToggle";

        resilienceCard = CreateSection(428, "Settings.Resilience", "Settings.ResilienceDescription");
        retryFailedDownloads = CreateCheck(resilienceCard, "Settings.RetryFailedDownloads");
        retryFailedDownloads.AccessibleAutomationId = "RetryFailedDownloadsToggle";
        AddRowLabel(resilienceCard, "Settings.AdditionalRetryAttempts", 128);
        additionalRetryAttempts = CreateNumber(resilienceCard, 0, 5, "AdditionalRetryAttempts");
        enableInterDownloadDelay = CreateCheck(resilienceCard, "Settings.EnableInterDownloadDelay");
        enableInterDownloadDelay.AccessibleAutomationId = "InterDownloadDelayToggle";
        AddRowLabel(resilienceCard, "Settings.MinimumInterDownloadDelay", 214);
        minimumInterDownloadDelay = CreateNumber(resilienceCard, 0, 300, "MinimumInterDownloadDelay");
        AddRowLabel(resilienceCard, "Settings.MaximumInterDownloadDelay", 258);
        maximumInterDownloadDelay = CreateNumber(resilienceCard, 0, 300, "MaximumInterDownloadDelay");
        resumeInterruptedDownloads = CreateCheck(resilienceCard, "Settings.ResumeInterruptedDownloads");
        preservePartialDownloads = CreateCheck(resilienceCard, "Settings.PreservePartialDownloads");
        autoResumeAfterRestart = CreateCheck(resilienceCard, "Settings.AutoResumeAfterRestart");
        liveCard = CreateSection(434, "Settings.Live", "Settings.LiveDescription");
        AddRowLabel(liveCard, "Settings.LiveConcurrency", 88, 360);
        liveConcurrency = CreateNumber(liveCard, 1, 3, "LiveConcurrency");
        preserveLive = CreateCheck(liveCard, "Settings.PreserveLive");
        resumeLive = CreateCheck(liveCard, "Settings.ResumeLive");
        autoResumeLive = CreateCheck(liveCard, "Settings.AutoResumeLive");
        preserveLiveCancel = CreateCheck(liveCard, "Settings.PreserveLiveCancel");
        AddRowLabel(liveCard, "Settings.MinimumLiveCheck", 310);
        minimumLiveCheck = CreateNumber(liveCard, 30, 3600, "MinimumLiveCheck");
        AddRowLabel(liveCard, "Settings.MaximumLiveCheck", 362);
        maximumLiveCheck = CreateNumber(liveCard, 30, 3600, "MaximumLiveCheck");

        subtitlesCard = CreateSection(348, "Settings.Subtitles", "Settings.SubtitlesDescription");
        subtitlesEnabled = CreateCheck(subtitlesCard, "Settings.SubtitlesEnabled");
        subtitlesEnabled.AccessibleAutomationId = "SubtitleDefaultsEnabled";
        subtitlesAutomatic = CreateCheck(subtitlesCard, "Settings.SubtitlesAutomatic");
        AddRowLabel(subtitlesCard, "Settings.SubtitleLanguages", 170);
        subtitleLanguages = CreateTextBox(subtitlesCard);
        subtitleLanguages.AccessibleAutomationId = "SubtitleDefaultLanguages";
        AddRowLabel(subtitlesCard, "Settings.SubtitleFormat", 216);
        subtitleFormat = CreateCombo(subtitlesCard);
        subtitleEmbed = CreateCheck(subtitlesCard, "Settings.SubtitlesEmbed");
        subtitleKeepFiles = CreateCheck(subtitlesCard, "Settings.SubtitlesKeepFiles");

        sponsorCard = CreateSection(270, "Settings.SponsorBlock", "Settings.SponsorBlockDescription");
        sponsorEnabled = CreateCheck(sponsorCard, "Settings.SponsorBlockEnabled");
        sponsorEnabled.AccessibleAutomationId = "SponsorBlockDefaultEnabled";
        AddRowLabel(sponsorCard, "Settings.SponsorBlockMode", 136);
        sponsorMode = CreateCombo(sponsorCard);
        AddRowLabel(sponsorCard, "Settings.SponsorBlockCategories", 188);
        sponsorCategories = CreateTextBox(sponsorCard);
        sponsorHint = AppUi.Muted();
        BindText(sponsorHint, "Settings.SponsorBlockHint");
        sponsorHint.Multiline = true;
        sponsorCard.Controls.Add(sponsorHint);

        cookiesCard = CreateSection(306, "Settings.Cookies", "Settings.CookiesDescription");
        cookiesEnabled = CreateCheck(cookiesCard, "Settings.CookiesEnabled");
        cookiesEnabled.AccessibleAutomationId = "CookieFileEnabled";
        AddRowLabel(cookiesCard, "Settings.CookiesFile", 136);
        cookiesFileName = AppUi.Muted();
        cookiesFileName.AccessibleAutomationId = "CookieFileName";
        cookiesBrowse = CreateButton(cookiesCard, "Settings.Browse", primary: false);
        cookiesBrowse.Click += BrowseCookies;
        cookiesHelp = AppUi.Muted();
        BindText(cookiesHelp, "Settings.CookiesHelp");
        cookiesHelp.Multiline = true;
        cookiesWarning = AppUi.Muted();
        AppUi.BindForeground(cookiesWarning, AppThemeTokens.Warning);
        BindText(cookiesWarning, "Settings.CookiesWarning");
        cookiesWarning.Multiline = true;
        cookiesCard.Controls.AddRange([cookiesFileName, cookiesHelp, cookiesWarning]);

        appearanceCard = CreateSection(210, "Settings.Appearance", "Settings.AppearanceDescription");
        AddRowLabel(appearanceCard, "Settings.Theme", 96);
        theme = CreateCombo(appearanceCard);
        theme.AccessibleAutomationId = "ThemeSelector";
        AddRowLabel(appearanceCard, "Settings.Language", 148);
        language = CreateCombo(appearanceCard);
        language.AccessibleAutomationId = "LanguageSelector";
        theme.SelectedIndexChanged += ThemeSelected;
        language.SelectedIndexChanged += LanguageSelected;

        browserCard = CreateSection(190, "Settings.BrowserIntegration", "Settings.BrowserIntegrationDescription");
        browserEnabled = CreateCheck(browserCard, "Settings.BrowserEnabled");
        browserEnabled.AccessibleAutomationId = "BrowserIntegrationToggle";
        browserStatus = AppUi.Muted();
        browserStatus.AccessibleAutomationId = "BrowserIntegrationStatus";
        browserExtensionInfo = AppUi.Muted();
        BindText(browserExtensionInfo, "Settings.BrowserExtensionInfo");
        browserCard.Controls.AddRange([browserStatus, browserExtensionInfo]);
        repairBrowser = CreateButton(browserCard, "Settings.BrowserRepair", primary: false);
        repairBrowser.AccessibleAutomationId = "BrowserIntegrationRepairButton";

        remoteCard = CreateSection(432, "Settings.RemoteTitle", "Settings.RemoteDescription");
        remoteEnabled = CreateCheck(remoteCard, "Settings.RemoteEnable");
        remoteEnabled.AccessibleAutomationId = "WebRemoteEnable";
        remoteRequireAuth = CreateCheck(remoteCard, "Settings.RemoteRequireAuth");
        remoteRequireAuth.AccessibleAutomationId = "WebRemoteRequireAuthentication";
        remoteRequireAuth.CheckedChanged += (_, _) => UpdateRemoteAuthPresentation();
        remoteAuthWarning = AppUi.Muted();
        remoteAuthWarning.AccessibleAutomationId = "WebRemoteAuthWarning";
        AppUi.BindForeground(remoteAuthWarning, AppThemeTokens.Warning);
        remoteAuthWarning.Multiline = true;
        BindText(remoteAuthWarning, "Settings.RemoteAuthWarning");
        remoteCard.Controls.Add(remoteAuthWarning);
        AddRowLabel(remoteCard, "Settings.RemotePort", 208);
        remotePort = CreateNumber(remoteCard, 1024, 65535, "WebRemotePort");
        AddRowLabel(remoteCard, "Settings.RemoteAddress", 252);
        remoteAddress = CreateCombo(remoteCard);
        remoteAddress.AccessibleAutomationId = "WebRemoteAddress";
        remoteStatus = AppUi.Muted();
        remoteStatus.Multiline = true;
        remoteCard.Controls.Add(remoteStatus);
        remoteToken = CreateTextBox(remoteCard);
        remoteToken.ReadOnly = true;
        remoteToken.PasswordCharacter = '●';
        remoteToken.AccessibleAutomationId = "WebRemoteToken";
        remoteCopy = CreateButton(remoteCard, "Settings.RemoteCopyToken", false);
        remoteCopy.AccessibleAutomationId = "WebRemoteCopyToken";
        remoteCopy.Click += async (_, _) => await Clipboard.SetTextAsync(services.WebRemote.Token);
        remoteRegenerate = CreateButton(remoteCard, "Settings.RemoteRegenerate", false);
        remoteRegenerate.AccessibleAutomationId = "WebRemoteRegenerateToken";
        remoteRegenerate.Click += (_, _) => { services.WebRemote.RegenerateToken(); remoteToken.Text = services.WebRemote.Token; };
        remoteOpen = CreateButton(remoteCard, "Settings.RemoteOpen", false);
        remoteOpen.AccessibleAutomationId = "WebRemoteOpen";
        remoteOpen.Click += (_, _) => OpenRemote();
        remoteQrButton = CreateButton(remoteCard, "Settings.RemoteQr", false);
        remoteQrButton.AccessibleAutomationId = "WebRemoteQr";
        remoteQrButton.Click += (_, _) => ToggleRemoteQr();
        remoteShowToken = CreateButton(remoteCard, "Settings.RemoteShowToken", false);
        remoteShowToken.AccessibleAutomationId = "WebRemoteShowToken";
        remoteShowToken.Click += (_, _) => remoteToken.PasswordCharacter = remoteToken.PasswordCharacter is null ? '●' : null;
        remoteQr = new PictureBox { SizeMode = PictureBoxSizeMode.Zoom, Visible = false };
        remoteCard.Controls.Add(remoteQr);
        repairBrowser.Click += RepairBrowserClicked;

        advancedCard = CreateSection(502, "Settings.Advanced", "Settings.AdvancedDescription");
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
        useCustomDeno = CreateCheck(advancedCard, "Settings.UseCustomDeno");
        AddRowLabel(advancedCard, "Settings.CustomDeno", 442);
        denoPath = CreateTextBox(advancedCard);
        browseDeno = CreateButton(advancedCard, "Settings.Browse", primary: false);
        browseDeno.Click += BrowseDeno;
        useCustomDeno.CheckedChanged += (_, _) => denoPath.Enabled = useCustomDeno.Checked;

        actionsCard = new Panel { Height = 84 };
        AppUi.Card(actionsCard);
        save = CreateButton(actionsCard, "Settings.Save", primary: true);
        save.AccessibleAutomationId = "SettingsSaveButton";
        logs = CreateButton(actionsCard, "Settings.OpenLogs", primary: false);
        feedback = AppUi.Muted();
        actionsCard.Controls.Add(feedback);
        save.Click += SaveClicked;
        logs.Click += (_, _) => ShellService.OpenFolderForFile(services.Paths.LogDirectory);

        scroll.Controls.AddRange([generalCard, toolsCard, behaviorCard, resilienceCard, liveCard, subtitlesCard, sponsorCard,
            cookiesCard, appearanceCard, browserCard, remoteCard, advancedCard, actionsCard]);
        Controls.AddRange([header, scroll]);
        SizeChanged += ViewSizeChanged;
        scroll.SizeChanged += ScrollSizeChanged;
        services.Tools.Changed += ToolsChanged;
        services.WebRemote.Changed += RemoteChanged;
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

    private void AddRowLabel(Panel panel, string key, int top, int width = 200)
    {
        var label = AppUi.Muted();
        BindText(label, key);
        label.Multiline = width > 200;
        label.SetBounds(24, top + 7, width, width > 200 ? 36 : 24);
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

    private static NumericUpDown CreateNumber(Panel panel, int minimum, int maximum, string automationId)
    {
        var control = new NumericUpDown { Minimum = minimum, Maximum = maximum, AccessibleAutomationId = automationId };
        AppUi.Input(control);
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
        // ScrollableControl physically offsets child bounds while scrolling. Rebuild the
        // explicit card layout from its translated display origin, not from unscrolled Y=0.
        var displayOrigin = scroll.DisplayRectangle.Location;
        var top = displayOrigin.Y;
        foreach (var card in new[] { generalCard, toolsCard, behaviorCard, resilienceCard, liveCard, subtitlesCard, sponsorCard,
                     cookiesCard, appearanceCard, browserCard, remoteCard, advancedCard, actionsCard })
        {
            card.SetBounds(page + displayOrigin.X, top, cardWidth, card.Height);
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
        defaultContainer.SetBounds(fieldLeft, 250, Math.Min(250, fieldRight - fieldLeft), 36);
        concurrency.SetBounds(fieldLeft, 302, Math.Min(160, fieldRight - fieldLeft), 36);
        keepTemporary.SetBounds(fieldLeft, 352, Math.Max(280, fieldRight - fieldLeft), 30);
        concurrencyHint.SetBounds(fieldLeft, 384, Math.Max(260, fieldRight - fieldLeft), 38);

        LayoutToolCard(ytDlpToolCard, ytDlpDescription, ytDlpStatus, ytDlpProgress, toolsCard, 92);
        LayoutToolCard(ffmpegToolCard, ffmpegDescription, ffmpegStatus, ffmpegProgress, toolsCard, 174);
        LayoutToolCard(denoToolCard, denoDescription, denoStatus, denoProgress, toolsCard, 256);
        checkUpdates.SetBounds(24, 352, 174, 38);
        retrySetup.SetBounds(208, 352, 158, 38);
        supportedSources.SetBounds(376, 352, Math.Max(150, Math.Min(240, cardWidth - 400)), 38);

        filenameTemplate.SetBounds(fieldLeft, 94, Math.Max(280, fieldRight - fieldLeft), 36);
        overwrite.SetBounds(fieldLeft, 146, Math.Max(260, fieldRight - fieldLeft), 30);
        conflict.SetBounds(fieldLeft, 198, Math.Min(280, fieldRight - fieldLeft), 36);
        metadataJson.SetBounds(fieldLeft, 248, Math.Max(260, fieldRight - fieldLeft), 30);
        thumbnails.SetBounds(fieldLeft, 286, Math.Max(260, fieldRight - fieldLeft), 30);
        setFileCreationTime.SetBounds(24, 324, cardWidth - 48, 30);
        saveMetadataSidecar.SetBounds(24, 362, cardWidth - 48, 30);
        showQueuePositionNumbers.SetBounds(24, 400, cardWidth - 48, 30);

        retryFailedDownloads.SetBounds(24, 88, cardWidth - 48, 30);
        additionalRetryAttempts.SetBounds(Math.Max(fieldLeft, 250), 128, 104, 36);
        enableInterDownloadDelay.SetBounds(24, 174, cardWidth - 48, 30);
        minimumInterDownloadDelay.SetBounds(Math.Max(fieldLeft, 250), 214, 104, 36);
        maximumInterDownloadDelay.SetBounds(Math.Max(fieldLeft, 250), 258, 104, 36);
        resumeInterruptedDownloads.SetBounds(24, 302, cardWidth - 48, 30);
        preservePartialDownloads.SetBounds(24, 340, cardWidth - 48, 30);
        autoResumeAfterRestart.SetBounds(24, 378, cardWidth - 48, 30);
        liveConcurrency.SetBounds(Math.Max(fieldLeft, 400), 88, 104, 36);
        preserveLive.SetBounds(24, 140, cardWidth - 48, 30);
        resumeLive.SetBounds(24, 178, cardWidth - 48, 30);
        autoResumeLive.SetBounds(24, 216, cardWidth - 48, 30);
        preserveLiveCancel.SetBounds(24, 254, cardWidth - 48, 30);
        minimumLiveCheck.SetBounds(Math.Max(fieldLeft, 250), 310, 104, 36);
        maximumLiveCheck.SetBounds(Math.Max(fieldLeft, 250), 362, 104, 36);

        subtitlesEnabled.SetBounds(24, 88, cardWidth - 48, 30);
        subtitlesAutomatic.SetBounds(24, 126, cardWidth - 48, 30);
        subtitleLanguages.SetBounds(fieldLeft, 170, Math.Min(300, fieldRight - fieldLeft), 36);
        subtitleFormat.SetBounds(fieldLeft, 216, Math.Min(220, fieldRight - fieldLeft), 36);
        subtitleEmbed.SetBounds(24, 260, cardWidth - 48, 30);
        subtitleKeepFiles.SetBounds(24, 298, cardWidth - 48, 30);

        sponsorEnabled.SetBounds(24, 88, cardWidth - 48, 30);
        sponsorMode.SetBounds(fieldLeft, 136, Math.Min(220, fieldRight - fieldLeft), 36);
        sponsorCategories.SetBounds(fieldLeft, 188, Math.Min(330, fieldRight - fieldLeft), 36);
        sponsorHint.SetBounds(24, 232, cardWidth - 48, 32);

        cookiesEnabled.SetBounds(24, 88, cardWidth - 48, 30);
        cookiesBrowse.SetBounds(fieldRight - browseWidth, 136, browseWidth, 36);
        cookiesFileName.SetBounds(fieldLeft, 142, Math.Max(130, cookiesBrowse.Left - fieldLeft - 10), 28);
        cookiesHelp.SetBounds(24, 190, cardWidth - 48, 48);
        cookiesWarning.SetBounds(24, 250, cardWidth - 48, 42);

        theme.SetBounds(fieldLeft, 96, Math.Min(240, fieldRight - fieldLeft), 36);
        language.SetBounds(fieldLeft, 148, Math.Min(240, fieldRight - fieldLeft), 36);

        browserEnabled.SetBounds(24, 88, cardWidth - 48, 30);
        browserStatus.SetBounds(24, 123, Math.Max(180, cardWidth - 210), 24);
        repairBrowser.SetBounds(cardWidth - 174, 119, 150, 34);
        browserExtensionInfo.SetBounds(24, 159, cardWidth - 48, 23);

        remoteEnabled.SetBounds(24, 88, cardWidth - 48, 30);
        remoteRequireAuth.SetBounds(24, 124, cardWidth - 48, 30);
        remoteAuthWarning.SetBounds(24, 158, cardWidth - 48, 42);
        remotePort.SetBounds(fieldLeft, 208, 108, 36);
        remoteAddress.SetBounds(fieldLeft, 252, Math.Min(270, fieldRight - fieldLeft), 36);
        remoteStatus.SetBounds(24, 296, cardWidth - 48, 44);
        remoteToken.SetBounds(24, 348, Math.Max(190, cardWidth - 390), 36);
        remoteCopy.SetBounds(remoteToken.Right + 8, 348, 96, 36);
        remoteRegenerate.SetBounds(remoteCopy.Right + 8, 348, 128, 36);
        var remoteActionsTop = remoteRequireAuth.Checked ? 392 : 348;
        remoteOpen.SetBounds(24, remoteActionsTop, 145, 34);
        remoteQrButton.SetBounds(177, remoteActionsTop, 145, 34);
        remoteShowToken.SetBounds(330, remoteActionsTop, 145, 34);
        remoteQr.SetBounds(24, remoteActionsTop + 46, 128, 128);

        customArguments.SetBounds(fieldLeft, 96, Math.Max(280, fieldRight - fieldLeft), 72);
        customArgumentsHint.SetBounds(fieldLeft, 174, Math.Max(280, fieldRight - fieldLeft), 42);
        useCustomYtDlp.SetBounds(fieldLeft, 220, Math.Max(280, fieldRight - fieldLeft), 30);
        LayoutPathRow(ytDlpPath, browseYtDlp, advancedCard, 266, fieldLeft, fieldRight, browseWidth);
        useCustomFfmpeg.SetBounds(fieldLeft, 310, Math.Max(280, fieldRight - fieldLeft), 30);
        LayoutPathRow(ffmpegPath, browseFfmpeg, advancedCard, 354, fieldLeft, fieldRight, browseWidth);
        useCustomDeno.SetBounds(fieldLeft, 398, Math.Max(280, fieldRight - fieldLeft), 30);
        LayoutPathRow(denoPath, browseDeno, advancedCard, 442, fieldLeft, fieldRight, browseWidth);

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
            PopulateContainer(value.PreferredVideoContainer);
            SelectCombo(concurrency, item => item is int count && count == value.MaxSimultaneousDownloads);
            keepTemporary.Checked = value.KeepTemporaryFilesAfterSuccessfulMerge;
            useCustomYtDlp.Checked = value.UseCustomYtDlp;
            useCustomFfmpeg.Checked = value.UseCustomFfmpeg;
            useCustomDeno.Checked = value.UseCustomDeno;
            ytDlpPath.Text = value.CustomYtDlpPath ?? string.Empty;
            ffmpegPath.Text = value.CustomFfmpegPath ?? string.Empty;
            denoPath.Text = value.CustomDenoPath ?? string.Empty;
            ytDlpPath.Enabled = useCustomYtDlp.Checked;
            ffmpegPath.Enabled = useCustomFfmpeg.Checked;
            denoPath.Enabled = useCustomDeno.Checked;
            filenameTemplate.Text = value.FilenameTemplate;
            overwrite.Checked = value.OverwriteExistingFiles;
            PopulateConflict(value.ConflictBehavior);
            metadataJson.Checked = value.StoreMetadataJson;
            thumbnails.Checked = value.DownloadThumbnail;
            setFileCreationTime.Checked = value.SetFileCreationTimeFromMediaPublishDate;
            saveMetadataSidecar.Checked = value.SaveMetadataJsonSidecar;
            showQueuePositionNumbers.Checked = value.ShowQueuePositionNumbers;
            retryFailedDownloads.Checked = value.RetryFailedDownloads;
            additionalRetryAttempts.Value = value.AdditionalRetryAttempts;
            enableInterDownloadDelay.Checked = value.EnableInterDownloadDelay;
            minimumInterDownloadDelay.Value = value.MinimumInterDownloadDelaySeconds;
            maximumInterDownloadDelay.Value = value.MaximumInterDownloadDelaySeconds;
            resumeInterruptedDownloads.Checked = value.ResumeInterruptedDownloads;
            preservePartialDownloads.Checked = value.PreservePartialDownloadsOnFailure;
            autoResumeAfterRestart.Checked = value.AutoResumeAfterRestart;
            minimumLiveCheck.Value = value.MinimumLiveCheckSeconds;
            maximumLiveCheck.Value = value.MaximumLiveCheckSeconds;
            liveConcurrency.Value = value.MaxSimultaneousLiveRecordings;
            preserveLive.Checked = value.PreservePartialLiveOnFailure;
            resumeLive.Checked = value.ResumeLiveOnFailure;
            autoResumeLive.Checked = value.AutoResumeLiveAfterRestart;
            preserveLiveCancel.Checked = value.PreservePartialLiveOnCancel;
            subtitlesEnabled.Checked = value.SubtitleDefaults.Enabled;
            subtitlesAutomatic.Checked = value.SubtitleDefaults.Source is SubtitleSource.Both or SubtitleSource.Automatic;
            subtitleLanguages.Text = value.SubtitleDefaults.Languages.Count == 0
                ? value.Language : string.Join(", ", value.SubtitleDefaults.Languages);
            PopulateSubtitleFormat(value.SubtitleDefaults.Format);
            subtitleEmbed.Checked = value.SubtitleDefaults.Embed;
            subtitleKeepFiles.Checked = value.SubtitleDefaults.KeepFiles;
            sponsorEnabled.Checked = value.SponsorBlockDefaults.Mode != SponsorBlockMode.Off;
            PopulateSponsorMode(value.SponsorBlockDefaults.Mode == SponsorBlockMode.Remove ? SponsorBlockMode.Remove : SponsorBlockMode.Mark);
            sponsorCategories.Text = string.Join(", ", value.SponsorBlockDefaults.Categories);
            cookiesEnabled.Checked = value.UseCookieFile;
            selectedCookieFilePath = value.CookieFilePath;
            UpdateCookieFileName();
            browserEnabled.Checked = value.BrowserIntegrationEnabled;
            remoteEnabled.Checked = value.EnableWebRemote;
            remoteRequireAuth.Checked = value.WebRemoteRequireAuthentication;
            remotePort.Value = value.WebRemotePort;
            remoteAddress.Items.Clear();
            foreach (var address in WebRemoteHost.AvailableAddresses().Append(value.WebRemoteBindAddress).Distinct(StringComparer.Ordinal))
                remoteAddress.Items.Add(address);
            for (var index = 0; index < remoteAddress.Items.Count; index++)
                if (string.Equals(remoteAddress.Items[index]?.ToString(), value.WebRemoteBindAddress, StringComparison.Ordinal))
                    remoteAddress.SelectedIndex = index;
            remoteToken.Text = services.WebRemote.Token;
            customArguments.Text = value.CustomYtDlpArguments;
            PopulateTheme(value.ThemeMode);
            PopulateLanguage(value.Language);
        }
        finally
        {
            applyingValues = false;
        }
        UpdateToolStatus();
        UpdateBrowserStatus();
        UpdateRemoteStatus();
        UpdateRemoteAuthPresentation();
    }

    private void PopulateQuality(string selectedId)
    {
        defaultQuality.SelectedIndex = -1;
        defaultQuality.Items.Clear();
        foreach (var preset in QualityPreset.All)
            defaultQuality.Items.Add(new LocalizedOption<QualityPreset>(preset, text[$"Quality.{preset.Id}"]));
        SelectCombo(defaultQuality, item => item is LocalizedOption<QualityPreset> option && option.Value.Id == selectedId);
    }

    private void PopulateSubtitleFormat(SubtitleFormat selected)
    {
        subtitleFormat.Items.Clear();
        foreach (var value in Enum.GetValues<SubtitleFormat>())
            subtitleFormat.Items.Add(new LocalizedOption<SubtitleFormat>(value, text[$"SubtitleFormat.{value}"]));
        SelectCombo(subtitleFormat, item => item is LocalizedOption<SubtitleFormat> option && option.Value == selected);
    }

    private void PopulateSponsorMode(SponsorBlockMode selected)
    {
        sponsorMode.Items.Clear();
        foreach (var value in new[] { SponsorBlockMode.Mark, SponsorBlockMode.Remove })
            sponsorMode.Items.Add(new LocalizedOption<SponsorBlockMode>(value, text[$"SponsorBlockMode.{value}"]));
        SelectCombo(sponsorMode, item => item is LocalizedOption<SponsorBlockMode> option && option.Value == selected);
    }

    private void UpdateCookieFileName() => cookiesFileName.Text = string.IsNullOrWhiteSpace(selectedCookieFilePath)
        ? text["Settings.CookiesNoFile"] : System.IO.Path.GetFileName(selectedCookieFilePath);

    private void PopulateConflict(FileConflictBehavior selected)
    {
        conflict.SelectedIndex = -1;
        conflict.Items.Clear();
        foreach (var value in Enum.GetValues<FileConflictBehavior>())
            conflict.Items.Add(new LocalizedOption<FileConflictBehavior>(value, text[$"Conflict.{value}"]));
        SelectCombo(conflict, item => item is LocalizedOption<FileConflictBehavior> option && option.Value == selected);
    }

    private void PopulateContainer(PreferredVideoContainer selected)
    {
        defaultContainer.SelectedIndex = -1;
        defaultContainer.Items.Clear();
        foreach (var value in Enum.GetValues<PreferredVideoContainer>())
            defaultContainer.Items.Add(new LocalizedOption<PreferredVideoContainer>(value, text[$"Container.{value}"]));
        SelectCombo(defaultContainer, item => item is LocalizedOption<PreferredVideoContainer> option && option.Value == selected);
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

    private async void BrowseDeno(object? sender, EventArgs e)
        => await BrowseToolAsync(denoPath, text["Settings.SelectDeno"]);

    private async void BrowseCookies(object? sender, EventArgs e)
    {
        if (FindForm() is not { } owner) return;
        var dialog = new OpenFileDialog
        {
            Title = text["Settings.CookiesChooseFile"],
            AllowMultiple = false,
            InitialDirectory = string.IsNullOrWhiteSpace(selectedCookieFilePath)
                ? null : System.IO.Path.GetDirectoryName(selectedCookieFilePath)
        };
        dialog.AddFilter(text["Settings.CookiesTextFiles"], "*.txt");
        dialog.AddFilter(text["Settings.AllFiles"], "*.*");
        if (await dialog.ShowDialog(owner) != DialogResult.OK || dialog.FileNames.FirstOrDefault() is not { } path) return;
        selectedCookieFilePath = path;
        LogSanitizer.RegisterCookieFilePath(path);
        UpdateCookieFileName();
    }

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
        if (operationInProgress)
            return;
        SetOperationBusy(true);
        feedback.Text = text["Settings.CheckingUpdates"];
        try
        {
            await services.Tools.CheckForUpdatesAsync();
            feedback.Text = services.Tools.YtDlp.Status == ToolStatus.Failed || services.Tools.Ffmpeg.Status == ToolStatus.Failed || services.Tools.Deno.Status == ToolStatus.Failed
                ? text["Settings.SetupAttention"]
                : text["Settings.UpdateCheckComplete"];
        }
        catch (Exception ex)
        {
            services.Logger.Error("Checking for tool updates failed.", ex);
            feedback.Text = text["Error.Generic"];
        }
        finally
        {
            SetOperationBusy(false);
        }
    }

    private async void RetrySetup(object? sender, EventArgs e)
    {
        if (operationInProgress)
            return;
        SetOperationBusy(true);
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
        finally
        {
            SetOperationBusy(false);
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
        UiCrashDiagnostics.VerifyUiThread("settings.save");
        UiCrashDiagnostics.Record("settings.save.begin");
        if (operationInProgress)
            return;
        SetOperationBusy(true);
        try
        {
            _ = CommandLineArgumentTokenizer.ParseSafeYtDlpArguments(customArguments.Text);
            if (minimumInterDownloadDelay.Value > maximumInterDownloadDelay.Value)
                throw new ArgumentException("Minimum admission delay must not exceed maximum admission delay.");
            if (minimumLiveCheck.Value > maximumLiveCheck.Value)
                throw new ArgumentException("Minimum LIVE check interval must not exceed maximum interval.");
            var subtitleOptions = new SubtitleOptions
            {
                Enabled = subtitlesEnabled.Checked,
                Source = subtitlesAutomatic.Checked ? SubtitleSource.Both : SubtitleSource.Manual,
                Languages = subtitleLanguages.Text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList(),
                Format = (subtitleFormat.SelectedItem as LocalizedOption<SubtitleFormat>)?.Value ?? SubtitleFormat.Auto,
                Embed = subtitleEmbed.Checked,
                KeepFiles = subtitleKeepFiles.Checked
            };
            subtitleOptions.Validate();
            var sponsorOptions = new SponsorBlockOptions
            {
                Mode = sponsorEnabled.Checked
                    ? (sponsorMode.SelectedItem as LocalizedOption<SponsorBlockMode>)?.Value ?? SponsorBlockMode.Mark
                    : SponsorBlockMode.Off,
                Categories = sponsorCategories.Text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList()
            };
            sponsorOptions.ToYtDlpCategories();
            var selectedDefaultContainer = (defaultContainer.SelectedItem as LocalizedOption<PreferredVideoContainer>)?.Value
                ?? PreferredVideoContainer.Auto;
            var compatibility = DownloadOptionCompatibilityValidator.Check(MediaTimeRange.Full,
                subtitleOptions, sponsorOptions, selectedDefaultContainer);
            if (compatibility != DownloadOptionIssue.None)
            {
                feedback.Text = text[DownloadOptionCompatibilityValidator.MessageKey(compatibility)];
                return;
            }
            if (cookiesEnabled.Checked) CookieFileService.Validate(selectedCookieFilePath);
            var value = services.Settings.Current;
            value.TemporaryDirectory = System.IO.Path.GetFullPath(temporaryDirectory.Text.Trim());
            value.FinalOutputDirectory = System.IO.Path.GetFullPath(outputDirectory.Text.Trim());
            value.DefaultQualityPresetId = (defaultQuality.SelectedItem as LocalizedOption<QualityPreset>)?.Value.Id ?? "best";
            value.PreferredVideoContainer = selectedDefaultContainer;
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
            value.ShowQueuePositionNumbers = showQueuePositionNumbers.Checked;
            value.RetryFailedDownloads = retryFailedDownloads.Checked;
            value.AdditionalRetryAttempts = (int)additionalRetryAttempts.Value;
            value.EnableInterDownloadDelay = enableInterDownloadDelay.Checked;
            value.MinimumInterDownloadDelaySeconds = (int)minimumInterDownloadDelay.Value;
            value.MaximumInterDownloadDelaySeconds = (int)maximumInterDownloadDelay.Value;
            value.ResumeInterruptedDownloads = resumeInterruptedDownloads.Checked;
            value.PreservePartialDownloadsOnFailure = preservePartialDownloads.Checked;
            value.AutoResumeAfterRestart = autoResumeAfterRestart.Checked;
            value.MinimumLiveCheckSeconds = (int)minimumLiveCheck.Value;
            value.MaximumLiveCheckSeconds = (int)maximumLiveCheck.Value;
            value.MaxSimultaneousLiveRecordings = (int)liveConcurrency.Value;
            value.PreservePartialLiveOnFailure = preserveLive.Checked;
            value.ResumeLiveOnFailure = resumeLive.Checked;
            value.AutoResumeLiveAfterRestart = autoResumeLive.Checked;
            value.PreservePartialLiveOnCancel = preserveLiveCancel.Checked;
            value.SubtitleDefaults = subtitleOptions;
            value.SponsorBlockDefaults = sponsorOptions;
            value.UseCookieFile = cookiesEnabled.Checked;
            value.CookieFilePath = selectedCookieFilePath;
            value.BrowserIntegrationEnabled = browserEnabled.Checked;
            value.EnableWebRemote = remoteEnabled.Checked;
            value.WebRemoteRequireAuthentication = remoteRequireAuth.Checked;
            value.WebRemotePort = (int)remotePort.Value;
            value.WebRemoteBindAddress = remoteAddress.SelectedItem?.ToString() ?? "127.0.0.1";
            value.CustomYtDlpArguments = customArguments.Text.Trim();
            await services.Settings.SaveAsync();
            ConfigureBrowserIntegration();
            await services.Tools.RefreshAsync();
            feedback.Text = sponsorOptions.Mode == SponsorBlockMode.Remove
                ? text["SponsorBlock.RemoveExperimentalWarning"] : text["Settings.Saved"];
            UiCrashDiagnostics.Record("settings.save.end");
        }
        catch (CookieFileException ex)
        {
            feedback.Text = text[$"Settings.CookieFile.{ex.Reason}"];
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
        finally
        {
            SetOperationBusy(false);
        }
    }

    private void SetOperationBusy(bool busy)
    {
        operationInProgress = busy;
        checkUpdates.Enabled = !busy;
        retrySetup.Enabled = !busy;
        save.Enabled = !busy;
    }

    private void ApplyToolPaths()
    {
        services.Settings.Current.UseCustomYtDlp = useCustomYtDlp.Checked;
        services.Settings.Current.UseCustomFfmpeg = useCustomFfmpeg.Checked;
        services.Settings.Current.UseCustomDeno = useCustomDeno.Checked;
        services.Settings.Current.CustomYtDlpPath = string.IsNullOrWhiteSpace(ytDlpPath.Text) ? null : ytDlpPath.Text.Trim();
        services.Settings.Current.CustomFfmpegPath = string.IsNullOrWhiteSpace(ffmpegPath.Text) ? null : ffmpegPath.Text.Trim();
        services.Settings.Current.CustomFfprobePath = null;
        services.Settings.Current.CustomDenoPath = string.IsNullOrWhiteSpace(denoPath.Text) ? null : denoPath.Text.Trim();
    }

    private void ToolsChanged(object? sender, EventArgs e) => Application.RunOnUIThread(UpdateToolStatus);

    private void UpdateToolStatus()
    {
        ytDlpStatus.Text = FormatTool(services.Tools.YtDlp);
        ffmpegStatus.Text = FormatTool(services.Tools.Ffmpeg);
        denoStatus.Text = FormatTool(services.Tools.Deno);
        UpdateProgress(ytDlpProgress, services.Tools.YtDlp);
        UpdateProgress(ffmpegProgress, services.Tools.Ffmpeg);
        UpdateProgress(denoProgress, services.Tools.Deno);
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
        if (!string.IsNullOrWhiteSpace(tool.ErrorMessage))
            parts.Add(tool.ErrorMessage);
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
            PopulateContainer((defaultContainer.SelectedItem as LocalizedOption<PreferredVideoContainer>)?.Value
                ?? services.Settings.Current.PreferredVideoContainer);
            PopulateConflict((conflict.SelectedItem as LocalizedOption<FileConflictBehavior>)?.Value ?? services.Settings.Current.ConflictBehavior);
            PopulateTheme((theme.SelectedItem as LocalizedOption<AppThemeMode>)?.Value ?? services.Settings.Current.ThemeMode);
            PopulateLanguage(services.Settings.Current.Language);
            PopulateSubtitleFormat((subtitleFormat.SelectedItem as LocalizedOption<SubtitleFormat>)?.Value
                ?? services.Settings.Current.SubtitleDefaults.Format);
            PopulateSponsorMode((sponsorMode.SelectedItem as LocalizedOption<SponsorBlockMode>)?.Value
                ?? SponsorBlockMode.Mark);
        }
        finally
        {
            applyingValues = false;
        }
        UpdateToolStatus();
        UpdateBrowserStatus();
        UpdateRemoteStatus();
        UpdateCookieFileName();
    }

    private async void RepairBrowserClicked(object? sender, EventArgs e)
    {
        browserEnabled.Checked = true;
        services.Settings.Current.BrowserIntegrationEnabled = true;
        try
        {
            await services.Settings.SaveAsync();
            ConfigureBrowserIntegration();
        }
        catch (Exception error)
        {
            services.Logger.Error("Repairing browser integration failed.", error);
            browserStatus.Text = text["Settings.BrowserNeedsRepair"];
        }
    }

    private void ConfigureBrowserIntegration()
    {
        try
        {
            if (services.Settings.Current.BrowserIntegrationEnabled)
                ExternalProtocolRegistration.RegisterCurrentExecutable();
            else
                ExternalProtocolRegistration.UnregisterCurrentUser();
        }
        catch (Exception error)
        {
            services.Logger.Error("Changing browser integration failed.", error);
        }
        UpdateBrowserStatus();
    }

    private void UpdateBrowserStatus()
    {
        if (!OperatingSystem.IsWindows() || !browserEnabled.Checked)
        {
            browserStatus.Text = text["Settings.BrowserDisabled"];
            repairBrowser.Enabled = OperatingSystem.IsWindows();
            return;
        }
        try
        {
            browserStatus.Text = ExternalProtocolRegistration.GetStatus(ExternalProtocolRegistration.GetCurrentExecutable())
                == ExternalProtocolRegistration.RegistrationStatus.Registered
                ? text["Settings.BrowserRegistered"] : text["Settings.BrowserNeedsRepair"];
        }
        catch (Exception error)
        {
            services.Logger.Warning($"Browser integration status unavailable: {error.GetType().Name}.");
            browserStatus.Text = text["Settings.BrowserNeedsRepair"];
        }
        repairBrowser.Enabled = true;
    }

    private void UpdateRemoteStatus()
    {
        var host = services.WebRemote;
        remoteStatus.Text = host.IsRunning ? text.Get("Settings.RemoteRunning", host.Address ?? "") :
            host.LastError is { Length: > 0 } error ? text.Get("Settings.RemoteFailed", error) :
            text["Settings.RemoteStopped"];
        remoteOpen.Enabled = host.IsRunning;
        remoteQrButton.Enabled = host.IsRunning;
    }

    private void UpdateRemoteAuthPresentation()
    {
        var required = remoteRequireAuth.Checked;
        remoteAuthWarning.Visible = !required;
        remoteToken.Visible = required;
        remoteCopy.Visible = required;
        remoteRegenerate.Visible = required;
        remoteShowToken.Visible = required;
        remoteCard.Height = (required ? 432 : 388) + (remoteQr.Visible ? 150 : 0);
        UpdateLayout();
    }

    private void RemoteChanged(object? sender, EventArgs e) => Application.RunOnUIThread(UpdateRemoteStatus);

    private void OpenRemote()
    {
        if (services.WebRemote.Address is not { } address) return;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(address) { UseShellExecute = true });
    }

    private void ToggleRemoteQr()
    {
        UiCrashDiagnostics.VerifyUiThread("settings.qr-toggle");
        if (remoteQr.Visible)
        {
            remoteQr.Visible = false;
            remoteQr.Image = null;
            remoteQrImage?.Dispose();
            remoteQrImage = null;
            remoteCard.Height = remoteRequireAuth.Checked ? 432 : 388;
            UiCrashDiagnostics.Record("settings.qr.hide", "remoteCard", remoteCard.Controls.Count);
            UpdateLayout();
            return;
        }
        if (services.WebRemote.Address is not { } address) return;
        using var generator = new QRCodeGenerator();
        using var code = generator.CreateQrCode(address, QRCodeGenerator.ECCLevel.Q);
        using var png = new PngByteQRCode(code);
        remoteQrImage = SKBitmap.Decode(png.GetGraphic(5));
        remoteQr.Image = remoteQrImage;
        remoteQr.Visible = true;
        remoteCard.Height = (remoteRequireAuth.Checked ? 432 : 388) + 150;
        UiCrashDiagnostics.Record("settings.qr.show", "remoteCard", remoteCard.Controls.Count);
        UpdateLayout();
    }

    private void ViewSizeChanged(object? sender, EventArgs e) => UpdateLayout();
    private void ScrollSizeChanged(object? sender, EventArgs e) => UpdateLayout();
    private void LanguageChanged(object? sender, EventArgs e) => Application.RunOnUIThread(ApplyLocalization);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            services.Tools.Changed -= ToolsChanged;
            services.WebRemote.Changed -= RemoteChanged;
            remoteQr.Image = null;
            remoteQrImage?.Dispose();
            text.LanguageChanged -= LanguageChanged;
            SizeChanged -= ViewSizeChanged;
            scroll.SizeChanged -= ScrollSizeChanged;
        }
        base.Dispose(disposing);
    }
}
