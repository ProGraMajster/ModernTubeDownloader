using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using ModernFormsNext;
using ModernFormsNext.Testing;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Settings;
using ModernTubeDownloader.Theming;
using Path = System.IO.Path;

namespace ModernTubeDownloader.Tests;

[Collection("ModernFormsNext TestHost")]
public sealed class MainFormShutdownTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    [Trait("Scenario", "SHUTDOWN-UI-C01")]
    public async Task ExperienceFailureStillCheckpointsTheSharedRuntimeAndPreventsExitInstaller(bool failExperience)
    {
        var root = Path.Combine(Path.GetTempPath(), "mtd-shutdown-ui-" + Guid.NewGuid().ToString("N"));
        var paths = AppPaths.Create(root); paths.EnsureCreated();
        AppServices? services = null;
        try
        {
            var settings = new SettingsService(paths, new NullAppLogger()); await settings.LoadAsync();
            settings.Current.QueuePaused = true; settings.Current.EnableWebRemote = false;
            settings.Current.Startup.ShowTrayIcon = false; settings.Current.Startup.CloseBehavior = TrayCloseBehavior.Exit;
            settings.Current.Updates.AutomaticChecks = false;
            settings.Current.FinalOutputDirectory = Path.Combine(root, "output"); settings.Current.TemporaryDirectory = Path.Combine(root, "temp");
            settings.Current.UseCustomYtDlp = settings.Current.UseCustomFfmpeg = settings.Current.UseCustomDeno = true;
            settings.Current.CustomYtDlpPath = settings.Current.CustomFfmpegPath = settings.Current.CustomFfprobePath = CoreTests.FakeToolPath();
            var tools = Path.Combine(root, "fake"); Directory.CreateDirectory(tools);
            foreach (var file in Directory.EnumerateFiles(AppContext.BaseDirectory, "ModernTubeDownloader.FakeTool.*"))
                File.Copy(file, Path.Combine(tools, Path.GetFileName(file)));
            settings.Current.CustomDenoPath = Path.Combine(tools, "deno.exe"); File.Copy(CoreTests.FakeToolPath(), settings.Current.CustomDenoPath);
            await settings.SaveAsync();

            // Restore a genuinely verified isolated package, so the failure case
            // cannot pass merely because the updater was ineligible to launch.
            var updates = Path.Combine(root, "Updates"); Directory.CreateDirectory(updates);
            const string version = "99.0.0";
            var filename = $"ModernTubeDownloader-{version}-win-x64.zip";
            var archivePath = Path.Combine(updates, filename);
            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            using (var writer = new StreamWriter(archive.CreateEntry("ModernTubeDownloader.exe").Open()))
                writer.Write("isolated shutdown test package");
            var repository = GitHubApplicationReleaseSource.Repository;
            await AtomicJsonFile.WriteAsync(Path.Combine(updates, "state.json"), new ApplicationUpdateState
            {
                Available = new(version, "Synthetic", new(repository + "/releases/tag/v" + version),
                    new(repository + "/releases/download/v" + version + "/" + filename),
                    new(repository + "/releases/download/v" + version + "/" + filename + ".sha256")),
                StagedArchive = archivePath, StagedHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(archivePath))),
                InstallOnExit = true
            });
            services = AppServices.Create(paths);
            await services.Tools.Initialization; await services.Updates.InitializeAsync();
            Assert.True(services.Updates.Ready); Assert.True(services.Updates.ShouldInstallOnExit);
            var installerCalls = 0;
            var installerSource = Path.Combine(root, "installer-source"); Directory.CreateDirectory(installerSource);
            services.Updates.InstallerSourceDirectory = installerSource;
            services.Updates.InstallerProcessFactory = _ =>
            {
                installerCalls++;
                throw new IOException("Synthetic installer boundary: no process may be created by this test.");
            };

            // This unsaved preference is a witness that the real AppServices
            // shutdown reached its checkpoint owners after experience suspension.
            var before = settings.Current.FinalOutputDirectory;
            var after = Path.Combine(root, "retained-after-shutdown");
            using (var host = ModernFormsTestHost.Create())
            using (var form = new MainForm(services, backgroundActivation: true))
            {
                TestWindowHost? window = null;
                Ui(host, () => window = host.Show(form, 1280, 900));
                if (failExperience)
                    typeof(MainForm).GetField("experienceTask", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .SetValue(form, Task.FromException(new IOException("SHUTDOWN-UI-C01 original experience fault")));
                services.Settings.Current.FinalOutputDirectory = after;
                Ui(host, form.Close);
                Pump(host, () => window!.IsClosed);
                host.Dispatcher.ThrowUnhandledExceptions();
            }
            var restored = new SettingsService(paths, new NullAppLogger()); await restored.LoadAsync();
            Assert.NotEqual(before, after); Assert.Equal(after, restored.Current.FinalOutputDirectory);
            Assert.Empty(services.Processor.ActiveItemIds); Assert.Empty(services.LiveScheduler.ActiveIds);
            Assert.Equal(failExperience ? 0 : 1, installerCalls);
            if (failExperience)
            {
                Assert.Null(services.Updates.State.LastInstallerRequest);
                Assert.Empty(Directory.EnumerateDirectories(updates, "runner-*"));
                var logs = ReadLogs(paths.LogDirectory);
                Assert.DoesNotContain("SHUTDOWN-UI-C01 original experience fault", logs);
                Assert.Contains("MainForm.ShutdownAndCloseAsync", logs);
            }
            Assert.True(File.Exists(archivePath));
        }
        finally
        {
            if (services is not null) { await services.ShutdownAsync(); services.Dispose(); }
            AppTypography.SetScale(100); AppearancePresentation.Current = new();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static void Ui(ModernFormsTestHost host, Action action)
    { host.Dispatcher.Post(action); host.ProcessPendingWork(); host.Dispatcher.ThrowUnhandledExceptions(); }

    private static void Pump(ModernFormsTestHost host, Func<bool> completed)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!completed() && DateTime.UtcNow < deadline) { host.ProcessPendingWork(); Thread.Sleep(5); }
        Assert.True(completed(), "The isolated shutdown did not finish."); host.ProcessPendingWork();
    }

    private static string ReadLogs(string directory) => string.Join(Environment.NewLine, Directory.EnumerateFiles(directory, "*.log").Select(path =>
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream); return reader.ReadToEnd();
    }));
}
