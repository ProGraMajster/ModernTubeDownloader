using ModernFormsNext;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Shell;
using ModernTubeDownloader.Theming;
using ModernTubeDownloader.Views;

namespace ModernTubeDownloader;

public sealed partial class MainForm
{
    private HistoryView CreateHistoryPage()
    {
        var view = MountPage(AppPageId.History, modern => new HistoryView(services, modern));
        view.RetryRequested += async (_, url) =>
        {
            ShowView(downloadsView);
            try { await downloadsView.AcceptExternalRequestAsync(new(url, true, true, Source: "history", Intent: ExternalMediaIntent.QuickQueue)); }
            catch (Exception error) { downloadsView.ShowExternalRequestError(error); }
        };
        return view;
    }

    private SettingsView CreateSettingsPage()
    {
        var view = MountPage(AppPageId.Settings, _ => new SettingsView(services),
            (page, modern) => page.SetDashboard(modern));
#if DEBUG
        if (automation is not null) view.RegisterDialog = automation.RegisterWindow;
#endif
        view.InstallUpdateRequested += (_, _) => _ = RequestUpdateInstallationAsync();
        view.SupportedSourcesRequested += OpenSupportedSources;
        view.SetupRequested += (_, _) => OpenExperience(manualSetup: true);
        view.WhatsNewRequested += (_, _) => OpenExperience(manualNews: true);
        return view;
    }

    private BatchImportView CreateBatchPage()
    {
        var view = MountPage(AppPageId.Batch, _ => new BatchImportView(services),
            (page, modern) => page.SetDashboard(modern));
#if DEBUG
        if (automation is not null) view.RegisterDialog = automation.RegisterWindow;
#endif
        view.OrganizeRequested += (_, _) => _ = OpenOrganizerAsync(view.CurrentSession);
        return view;
    }

    private InformationView CreateInformationPage(bool about)
    {
        var view = MountPage(about ? AppPageId.About : AppPageId.Help, _ => new InformationView(services, about),
            (page, modern) => { if (!about) page.SetDashboard(modern); });
        view.SetupRequested += (_, _) => OpenExperience(manualSetup: true);
        view.WhatsNewRequested += (_, _) => OpenExperience(manualNews: true);
        if (about) return view;
        view.NavigateRequested += (_, page) => Navigate(page);
        view.SettingsRequested += (_, category) => { Navigate(AppPageId.Settings); settingsPage.Value.ShowCategory(category); };
        view.BatchRequested += (_, _) => _ = OpenBatchAsync();
        view.OrganizerRequested += (_, _) => _ = OpenOrganizerAsync();
        return view;
    }

    private T MountPage<T>(AppPageId id, Func<bool, T> factory, Action<T, bool>? apply = null) where T : Control
    {
        using var timing = PerformanceDiagnostics.Measure("page.create." + id);
        var view = CreatePage(modern =>
        {
            var page = factory(modern);
            try
            {
                page.Visible = false;
                apply?.Invoke(page, modern);
                AppTypography.SetDashboardPresentation(page, modern);
                return page;
            }
            catch { page.Dispose(); throw; }
        });
        pages.Add(view, id);
        content.Controls.Add(view);
        if (shellHost.Mode != services.Appearance.InterfaceMode) ApplyInterface();
        return view;
    }
}
