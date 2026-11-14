using ModernFormsNext;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Views;

namespace ModernTubeDownloader;

public sealed partial class MainForm
{
    private bool suppressExperience, experienceRuntimeStarted, automaticExperienceAttempted, experienceOpen;
    private Form? activeExperience;
    private Task experienceTask = Task.CompletedTask;
    private int experienceGeneration;

    private void WireExperienceActions()
    {
        settingsView.SetupRequested += (_, _) => OpenExperience(manualSetup: true);
        settingsView.WhatsNewRequested += (_, _) => OpenExperience(manualNews: true);
        foreach (var view in new[] { helpView, aboutView })
        {
            view.SetupRequested += (_, _) => OpenExperience(manualSetup: true);
            view.WhatsNewRequested += (_, _) => OpenExperience(manualNews: true);
        }
    }

    private void TryStartExperience()
    {
        if (!experienceRuntimeStarted || automaticExperienceAttempted || shutdownStarted || preparationOverlay.Visible) return;
        automaticExperienceAttempted = true;
        // A startup journey must never interrupt restored downloads, LIVE or batch work.
        if (suppressExperience || fallbackNotice || tray.HasActiveWork || services.Batch.IsAnalyzing) return;
        OpenExperience();
    }

    private void OpenExperience(bool manualSetup = false, bool manualNews = false)
    {
        if (experienceOpen || shutdownStarted) return;
        experienceTask = ShowExperienceAsync(manualSetup, manualNews);
    }

    private async Task ShowExperienceAsync(bool manualSetup, bool manualNews)
    {
        experienceOpen = true;
        var generation = experienceGeneration;
        try
        {
            var action = manualSetup ? StartupExperience.Welcome : manualNews ? StartupExperience.WhatsNew :
                services.Experience.Next(false, suppressExperience, false, services.Appearance.Profile.InterfaceMode, services.Settings.UpdatePolicyNeedsConsent);
            if (action is StartupExperience.Welcome or StartupExperience.UpgradeChoice)
            {
                var draft = services.Experience.State.Draft ?? SetupDraft.From(services,
                    !services.Experience.State.ExistingProfile && services.Experience.State.Setup != SetupProgress.Completed);
                await services.Experience.BeginAsync(draft);
                if (shutdownStarted || generation != experienceGeneration || suppressExperience && !manualSetup) return;
                using var wizard = new SetupWizardForm(services, action == StartupExperience.UpgradeChoice, draft);
                activeExperience = wizard;
#if DEBUG
                if (automation is not null) wizard.RegisterDialog = automation.RegisterWindow;
                using var registration = automation?.RegisterWindow(wizard);
#endif
                await wizard.ShowDialog(this);
                settingsView.ReloadAfterSetup();
                if (!wizard.Accepted || generation != experienceGeneration || suppressExperience && !manualSetup) return;
                action = StartupExperience.WhatsNew;
            }
            if (action == StartupExperience.WhatsNew)
            {
                if (shutdownStarted || generation != experienceGeneration || suppressExperience && !manualNews && !manualSetup) return;
                using var news = new WhatsNewForm(services);
                activeExperience = news;
#if DEBUG
                using var registration = automation?.RegisterWindow(news);
#endif
                await news.ShowDialog(this);
                if (news.Destination is { } category)
                { Navigate(Shell.AppPageId.Settings); settingsView.ShowCategory(category); }
            }
        }
        catch (Exception error)
        {
            services.Logger.Error("The startup journey could not be opened or saved; existing profile data remains intact.", error);
            if (shutdownStarted || generation != experienceGeneration) return;
            using var dialog = new MessageBoxForm(text["Experience.WelcomeTitle"], text["Experience.SaveError"]);
            activeExperience = dialog;
#if DEBUG
            using var registration = automation?.RegisterWindow(dialog);
#endif
            await dialog.ShowDialog(this);
        }
        finally { activeExperience = null; experienceOpen = false; }
    }

    private async Task SuspendExperienceAsync()
    {
        suppressExperience = true;
        experienceGeneration++;
        activeExperience?.Close(); // Wizard persists the draft before allowing close.
        await experienceTask;
    }
}
