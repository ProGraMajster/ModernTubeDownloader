using System.Diagnostics;
using ModernFormsNext;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Theming;

namespace ModernTubeDownloader.Views;

internal sealed class InformationView : UserControl
{
    private readonly AppServices services;
    private readonly bool about;
    private readonly Label title = AppUi.Heading("", 24), body = AppUi.Muted();
    private readonly PictureBox icon = new() { Image = AppBranding.Icon, SizeMode = PictureBoxSizeMode.Zoom };
    private readonly Button repository = new(), diagnostics = new();
    public InformationView(AppServices services, bool about)
    {
        this.services = services; this.about = about; Dock = DockStyle.Fill;
        AppUi.BindBackground(this); body.Multiline = true;
        AppUi.Secondary(repository); AppUi.Secondary(diagnostics);
        repository.Click += (_, _) => Process.Start(new ProcessStartInfo("https://github.com/ProGraMajster/ModernTubeDownloader") { UseShellExecute = true });
        diagnostics.Click += (_, _) => Utilities.ShellService.OpenFolderForFile(services.Paths.LogDirectory);
        Controls.AddRange([icon, title, body, repository, diagnostics]);
        SizeChanged += (_, _) => LayoutPage(); services.Localization.LanguageChanged += Changed;
        AppTypography.RegisterLayout(this, LayoutPage); RefreshText();
    }
    private void Changed(object? sender, EventArgs e) => RefreshText();
    private void RefreshText()
    {
        title.Text = services.Localization[about ? "Nav.About" : "Nav.Help"];
        body.Text = about ? "ModernTubeDownloader\nv" + AppBranding.Version + "\n\n" + services.Localization["Dashboard.SourceCommit"] + ": " + AppBranding.SourceCommit + "\n\nMIT · ProGraMajster\n\nModernFormsNext " +
            typeof(MainForm).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false).OfType<System.Reflection.AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "ModernFormsNextCommit")?.Value
            : services.Localization["Dashboard.HelpBody"];
        repository.Text = services.Localization["Dashboard.Repository"]; diagnostics.Text = services.Localization["Settings.OpenLogs"];
        LayoutPage();
    }
    private void LayoutPage()
    {
        AutoScroll = true; var width = Math.Max(280, ClientSize.Width - 48);
        icon.SetBounds(24, 24, 64, 64); title.SetBounds(108, 24, Math.Max(160, width - 84), AppTypography.TextHeight(42));
        body.SetBounds(24, 112, width, AppTypography.TextHeight(210));
        repository.SetBounds(24, body.Bottom + 20, Math.Min(width, AppTypography.TextHeight(250)), AppTypography.TextHeight(40));
        diagnostics.SetBounds(24, repository.Bottom + 12, repository.Width, repository.Height);
    }
    protected override void Dispose(bool disposing) { if (disposing) services.Localization.LanguageChanged -= Changed; base.Dispose(disposing); }
}
