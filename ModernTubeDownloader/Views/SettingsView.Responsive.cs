using ModernFormsNext;
using ModernTubeDownloader.Theming;

namespace ModernTubeDownloader.Views;

internal sealed partial class SettingsView
{
    // The Classic coordinates are rebuilt first. Modern forms use measured rows,
    // so a card can shrink or grow without retaining a fixed field indentation.
    private void LayoutResponsiveSections()
    {
        Label Caption(string key) => (Label)localizedText.First(pair => pair.Value == key).Key;
        void Section(Panel card, Action<SettingsFormRows> compose)
        {
            if (!card.Visible) return;
            var rows = new SettingsFormRows(card, compactTips); compose(rows); rows.Complete();
        }
        Section(generalCard, rows =>
        {
            rows.Field(Caption("Settings.TemporaryDirectory"), temporaryDirectory, browseTemporary);
            rows.Field(Caption("Settings.OutputDirectory"), outputDirectory, browseOutput);
            rows.Field(Caption("Settings.DefaultQuality"), defaultQuality);
            rows.Field(Caption("Settings.DefaultContainer"), defaultContainer);
            rows.Field(Caption("Settings.Concurrency"), concurrency);
            rows.Check(keepTemporary); rows.Text(concurrencyHint);
        });
        Section(behaviorCard, rows =>
        {
            rows.Field(Caption("Settings.FilenameTemplate"), filenameTemplate); rows.Check(overwrite);
            rows.Field(Caption("Settings.Conflict"), conflict);
            foreach (var control in new[] { metadataJson, thumbnails, setFileCreationTime, saveMetadataSidecar, showQueuePositionNumbers }) rows.Check(control);
        });
        Section(resilienceCard, rows =>
        {
            rows.Check(retryFailedDownloads); rows.Field(Caption("Settings.AdditionalRetryAttempts"), additionalRetryAttempts);
            rows.Check(enableInterDownloadDelay); rows.Field(Caption("Settings.MinimumInterDownloadDelay"), minimumInterDownloadDelay);
            rows.Field(Caption("Settings.MaximumInterDownloadDelay"), maximumInterDownloadDelay);
            rows.Check(resumeInterruptedDownloads); rows.Check(preservePartialDownloads); rows.Check(autoResumeAfterRestart);
        });
        Section(liveCard, rows =>
        {
            rows.Field(Caption("Settings.LiveConcurrency"), liveConcurrency);
            foreach (var control in new[] { preserveLive, resumeLive, autoResumeLive, preserveLiveCancel }) rows.Check(control);
            rows.Field(Caption("Settings.MinimumLiveCheck"), minimumLiveCheck);
            rows.Field(Caption("Settings.MaximumLiveCheck"), maximumLiveCheck);
        });
        Section(subtitlesCard, rows =>
        {
            rows.Check(subtitlesEnabled); rows.Check(subtitlesAutomatic);
            rows.Field(Caption("Settings.SubtitleLanguages"), subtitleLanguages);
            rows.Field(Caption("Settings.SubtitleFormat"), subtitleFormat);
            rows.Check(subtitleEmbed); rows.Check(subtitleKeepFiles);
        });
        Section(sponsorCard, rows =>
        {
            rows.Check(sponsorEnabled); rows.Field(Caption("Settings.SponsorBlockMode"), sponsorMode);
            rows.Field(Caption("Settings.SponsorBlockCategories"), sponsorCategories); rows.Text(sponsorHint);
        });
        Section(cookiesCard, rows =>
        {
            rows.Check(cookiesEnabled); rows.Field(Caption("Settings.CookiesFile"), cookiesFileName, cookiesBrowse);
            rows.Text(cookiesHelp); rows.Text(cookiesWarning); rows.CookieSync(cookieSync);
        });
        if (currentCategory != "General") Section(appearanceCard, rows =>
        {
            rows.Field(Caption("Settings.Theme"), theme); rows.Field(Caption("Settings.Language"), language);
        });
        Section(browserCard, rows =>
        {
            rows.Check(browserEnabled); rows.Text(browserStatus); rows.Commands(repairBrowser); rows.Text(browserExtensionInfo);
        });
        Section(remoteCard, rows =>
        {
            rows.Check(remoteEnabled); rows.Check(remoteRequireAuth); rows.Text(remoteAuthWarning);
            rows.Field(Caption("Settings.RemotePort"), remotePort); rows.Field(Caption("Settings.RemoteAddress"), remoteAddress);
            rows.Text(remoteStatus); rows.Field(null, remoteToken); rows.Commands(remoteCopy, remoteRegenerate);
            rows.Text(remoteCodeExpiry); rows.Commands(remoteOpen, remoteQrButton, remoteCopyAddress);
            rows.Check(remoteHttps); rows.Commands(remoteCertificateGenerate, remoteCertificateExport);
            rows.Text(remoteCertificateInfo); rows.Text(remoteTransportHint); rows.Embed(remoteQr, 128, 128);
        });
        Section(advancedCard, rows =>
        {
            rows.Field(Caption("Settings.CustomArguments"), customArguments, height: AppTypography.TextHeight(72)); rows.Text(customArgumentsHint);
            rows.Check(useCustomYtDlp); rows.Field(Caption("Settings.CustomYtDlp"), ytDlpPath, browseYtDlp);
            rows.Check(useCustomFfmpeg); rows.Field(Caption("Settings.CustomFfmpeg"), ffmpegPath, browseFfmpeg);
            rows.Check(useCustomDeno); rows.Field(Caption("Settings.CustomDeno"), denoPath, browseDeno);
        });
    }

    private sealed class SettingsFormRows
    {
        private readonly Panel owner;
        private readonly ToolTip tips;
        private readonly int inner, line;
        private int top = 16;
        public SettingsFormRows(Panel owner, ToolTip tips)
        {
            this.owner = owner; this.tips = tips; inner = Math.Max(1, owner.Width - 32); line = AppTypography.TextHeight(36);
            foreach (var label in owner.Controls.Take(2).OfType<Label>()) Text(label);
            top += 4;
        }
        public void Text(Label label)
        {
            if (!label.Visible) { label.Bounds = System.Drawing.Rectangle.Empty; return; }
            label.Multiline = true; label.AutoEllipsis = false;
            label.SetBounds(16, top, inner, DashboardVisual.LabelHeight(label, inner, 20)); top = label.Bottom + 10;
        }
        public void Check(CheckBox control)
        {
            if (!control.Visible) { control.Bounds = System.Drawing.Rectangle.Empty; return; }
            var style = control.CurrentStyle;
            var measured = TextMeasurer.MeasureText(control.Text, style.GetFont(), style.GetFontSize(),
                new System.Drawing.Size(Math.Max(1, inner - 52), int.MaxValue), AppTypography.TextStyle(control));
            control.SetBounds(16, top, inner, Math.Max(AppTypography.TextHeight(30), (int)Math.Ceiling(measured.Height) + 8)); top = control.Bottom + 10;
        }
        public void Field(Label? label, Control control, Button? browse = null, int? height = null)
        {
            if (!control.Visible) { control.Bounds = System.Drawing.Rectangle.Empty; if (label is not null) label.Bounds = System.Drawing.Rectangle.Empty; return; }
            var left = 16; var available = inner; var fieldHeight = height ?? line;
            if (label is not null)
            {
                label.Multiline = true;
                if (inner >= AppTypography.TextHeight(620))
                {
                    var captionWidth = Math.Min(inner / 3, AppTypography.TextHeight(250));
                    var captionHeight = DashboardVisual.LabelHeight(label, captionWidth, 8);
                    label.SetBounds(16, top, captionWidth, Math.Max(line, captionHeight));
                    fieldHeight = Math.Max(fieldHeight, label.Height); left = label.Right + 16; available = inner - captionWidth - 16;
                }
                else
                {
                    label.SetBounds(16, top, inner, DashboardVisual.LabelHeight(label, inner, 8)); top = label.Bottom + 4;
                }
            }
            if (browse is not null)
            {
                var buttonWidth = Math.Min(available, DashboardVisual.TextWidth(browse, browse.Text) + browse.Padding.Horizontal + 8);
                // Keep the full command label at large text scales. The field moves
                // above the command when their useful widths no longer fit together.
                if (available - buttonWidth - 8 < AppTypography.TextHeight(150))
                {
                    control.SetBounds(left, top, Math.Max(1, available), fieldHeight);
                    top = control.Bottom + 8;
                    browse.SetBounds(left + available - buttonWidth, top, buttonWidth, line);
                    top = browse.Bottom + 12;
                    tips.SetToolTip(control, control.Text); tips.SetToolTip(browse, browse.Text);
                    return;
                }
                browse.SetBounds(left + available - buttonWidth, top, buttonWidth, fieldHeight); available -= buttonWidth + 8;
                tips.SetToolTip(browse, browse.Text);
            }
            control.SetBounds(left, top, Math.Max(1, available), fieldHeight); top = control.Bottom + 12;
            if (control is TextBox) tips.SetToolTip(control, control.Text);
        }
        public void Commands(params Button[] buttons)
        {
            var left = 16; var any = false;
            foreach (var button in buttons)
            {
                if (!button.Visible) { button.Bounds = System.Drawing.Rectangle.Empty; continue; }
                var width = Math.Min(inner, DashboardVisual.ActionWidth(button));
                if (left > 16 && left + width > inner + 16) { left = 16; top += line + 8; }
                button.SetBounds(left, top, width, line); left = button.Right + 8; any = true;
            }
            if (any) top += line + 12;
        }
        public void Embed(Control control, int height, int? width = null)
        {
            if (!control.Visible) { control.Bounds = System.Drawing.Rectangle.Empty; return; }
            control.SetBounds(16, top, Math.Min(inner, width ?? inner), height); top = control.Bottom + 12;
        }
        public void CookieSync(CookieSyncSettingsPanel control)
        {
            control.LayoutForWidth(inner);
            control.SetBounds(16, top, inner, control.Height); top = control.Bottom + 12;
        }
        public void Complete() => owner.Height = top + 6;
    }

    private sealed class SettingsScrollPanel : AppScrollPanel
    {
        private int viewportWidth = -1;
        internal event EventHandler? ViewportWidthChanged;
        protected override void OnScrollViewportChanged(EventArgs e)
        {
            base.OnScrollViewportChanged(e);
            var width = DisplayRectangle.Width;
            if (width == viewportWidth) return;
            viewportWidth = width;
            ViewportWidthChanged?.Invoke(this, e);
        }
    }
}
