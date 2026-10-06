using ModernTubeDownloader.Infrastructure;
using ModernFormsNext;
using ModernFormsNext.Testing;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Views;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Localization;
using Path = System.IO.Path;
using Microsoft.Win32;

namespace ModernTubeDownloader.Tests;

public sealed class ExternalLinkTests
{
    [Fact]
    public void ProtocolRegistrationQuotesTheExecutableAndTheSingleUriArgument()
    {
        var executable = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "App Folder", "ModernTubeDownloader.exe");
        Assert.Equal($"\"{executable}\" \"%1\"", ExternalProtocolRegistration.BuildOpenCommand(executable));
        Assert.Throws<ArgumentException>(() => ExternalProtocolRegistration.BuildOpenCommand("ModernTubeDownloader.exe"));
    }

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=abc_DEF-12", false)]
    [InlineData("https://youtu.be/abc_DEF-12", false)]
    [InlineData("https://www.youtube.com/shorts/abc_DEF-12", false)]
    [InlineData("https://www.youtube.com/live/abc_DEF-12", false)]
    [InlineData("https://www.youtube.com/playlist?list=PLabc_DEF-12", true)]
    [InlineData("https://www.youtube.com/watch?v=abc_DEF-12&list=PLabc_DEF-12", false)]
    public void SourceValidationAcceptsSupportedMedia(string url, bool playlist)
    {
        Assert.True(ExternalMediaRequest.TryValidateSource(url, out var normalized));
        Assert.Equal(playlist, Services.YtDlpMetadataService.IsPlaylistUrl(normalized));
        var link = $"moderntubedownloader://open?url={Uri.EscapeDataString(url)}&analyze=0";
        Assert.True(ExternalMediaRequest.TryParseProtocol(link, out var request));
        Assert.Equal(normalized, request!.SourceUrl);
        Assert.False(request.AutoAnalyze);
        Assert.False(request.AutoQueue);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/private.txt")]
    [InlineData("https://localhost/watch?v=abc")]
    [InlineData("https://youtube.com.evil.test/watch?v=abc")]
    [InlineData("https://www.youtube.com/redirect?url=https://example.com")]
    [InlineData("https://www.youtube.com/watch?list=PL123")]
    [InlineData("https://www.youtube.com/playlist?list=")]
    [InlineData("https://www.youtube.com/live/")]
    [InlineData("https://www.youtube.com/live/abc/extra")]
    [InlineData("https://user:pass@www.youtube.com/watch?v=abc")]
    public void SourceValidationRejectsUnsafeOrUnsupportedUrls(string url) =>
        Assert.False(ExternalMediaRequest.TryValidateSource(url, out _));

    [Theory]
    [InlineData("moderntubedownloader://open")]
    [InlineData("moderntubedownloader://evil?url=https%3A%2F%2Fyoutu.be%2Fabc")]
    [InlineData("moderntubedownloader://open?url=javascript%3Aalert(1)")]
    [InlineData("moderntubedownloader://open?url=https%3A%2F%2Fyoutu.be%2Fabc&url=https%3A%2F%2Fyoutu.be%2Fdef")]
    [InlineData("moderntubedownloader://open?url=https%3A%2F%2Fyoutu.be%2Fabc&analyze=maybe")]
    [InlineData("moderntubedownloader://open?url=https%3A%2F%2Fyoutu.be%2Fabc%ZZ")]
    [InlineData("moderntubedownloader://open?url=moderntubedownloader%3A%2F%2Fopen")]
    public void ProtocolRejectsMalformedPayloads(string link) =>
        Assert.False(ExternalMediaRequest.TryParseProtocol(link, out _));

    [Fact]
    public void VersionedDownloadRequestParsesQualityAndPlaylistMode()
    {
        var id = Guid.NewGuid();
        var link = $"moderntubedownloader://open?v=1&url={Uri.EscapeDataString("https://www.youtube.com/playlist?list=PL123")}&quality=720&container=mkv&analyze=1&queue=1&open=0&playlist=1&source=browser-extension&id={id:D}";
        Assert.True(ExternalMediaRequest.TryParseProtocol(link, out var request));
        Assert.Equal("720", request!.RequestedQualityPresetId);
        Assert.True(request.AutoQueue);
        Assert.True(request.AutoQueuePlaylist);
        Assert.Equal(PreferredVideoContainer.Mkv, request.RequestedContainer);
        Assert.False(request.OpenInApp);
        Assert.Equal(id, request.RequestId);
    }

    [Fact]
    public void OpenInAppRequestParsesWithoutQueueing()
    {
        var link = $"moderntubedownloader://open?v=1&url={Uri.EscapeDataString("https://youtu.be/abc")}&quality=1080&container=mp4&analyze=1&queue=0&open=1&playlist=0&source=browser-extension";

        Assert.True(ExternalMediaRequest.TryParseProtocol(link, out var request));
        Assert.NotNull(request);
        Assert.True(request.OpenInApp);
        Assert.False(request.AutoQueue);
        Assert.Equal("1080", request.RequestedQualityPresetId);
        Assert.Equal(PreferredVideoContainer.Mp4, request.RequestedContainer);
    }

    [Theory]
    [InlineData("quality=ultra")]
    [InlineData("quality=1080p")]
    [InlineData("container=avi")]
    [InlineData("queue=1&open=1")]
    [InlineData("queue=0&open=0")]
    [InlineData("queue=1&analyze=0")]
    [InlineData("queue=1&playlist=1")]
    [InlineData("source=unknown")]
    [InlineData("id=not-a-guid")]
    [InlineData("v=2")]
    public void InvalidVersionedRequestIsRejected(string fields)
    {
        var link = "moderntubedownloader://open?url=https%3A%2F%2Fyoutu.be%2Fabc&" + fields;
        Assert.False(ExternalMediaRequest.TryParseProtocol(link, out _));
    }

    [Fact]
    public void RegistryRegistrationRepairsStalePathAndCanBeDisabled()
    {
        var keyPath = @"Software\ModernTubeDownloader.Tests\" + Guid.NewGuid().ToString("N");
        using var classes = Registry.CurrentUser.CreateSubKey(keyPath, true)!;
        try
        {
            var first = Path.Combine(Path.GetTempPath(), "Old", "ModernTubeDownloader.exe");
            var moved = Path.Combine(Path.GetTempPath(), "Moved", "ModernTubeDownloader.exe");
            Assert.True(ExternalProtocolRegistration.EnsureRegistered(classes, first));
            Assert.False(ExternalProtocolRegistration.EnsureRegistered(classes, first));
            Assert.Equal(ExternalProtocolRegistration.RegistrationStatus.NeedsRepair,
                ExternalProtocolRegistration.GetStatus(classes, moved));
            Assert.True(ExternalProtocolRegistration.EnsureRegistered(classes, moved));
            Assert.Equal(ExternalProtocolRegistration.RegistrationStatus.Registered,
                ExternalProtocolRegistration.GetStatus(classes, moved));
            Assert.True(ExternalProtocolRegistration.Unregister(classes));
            Assert.False(ExternalProtocolRegistration.Unregister(classes));
            using (var foreign = classes.CreateSubKey(ExternalMediaRequest.Scheme, true)!)
            {
                foreign.SetValue(null, "Other application");
                foreign.SetValue("MTD Owner", "AnotherInstaller");
            }
            Assert.Throws<IOException>(() => ExternalProtocolRegistration.EnsureRegistered(classes, moved));
            Assert.False(ExternalProtocolRegistration.Unregister(classes));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(keyPath, throwOnMissingSubKey: false);
        }
    }

    [Fact]
    public async Task SecondaryInstanceForwardsToPrimaryOnlyOnce()
    {
        var name = "MTDTest-" + Guid.NewGuid().ToString("N");
        using var primary = new ExternalLinkInstance(name);
        using var secondary = new ExternalLinkInstance(name);
        Assert.True(primary.IsPrimary);
        Assert.False(secondary.IsPrimary);
        var received = new TaskCompletionSource<ExternalMediaRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
        primary.Start(request => { received.TrySetResult(request); return Task.CompletedTask; });
        const string link = "moderntubedownloader://open?url=https%3A%2F%2Fyoutu.be%2Fabc&analyze=1";
        Assert.True(await secondary.ForwardAsync(link));
        var request = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("https://youtu.be/abc", request.SourceUrl);
        Assert.True(request.AutoAnalyze);
    }
}

[Collection("ModernFormsNext TestHost")]
public sealed class ExternalLinkUiTests
{
    [Fact]
    public async Task ExternalUrlEntersDownloadsAndOptionallyStartsAnalysis()
    {
        var root = Path.Combine(Path.GetTempPath(), "ModernTubeDownloader.ExternalLinkTests", Guid.NewGuid().ToString("N"));
        var paths = AppPaths.Create(root);
        paths.EnsureCreated();
        var settings = new SettingsService(paths, new NullAppLogger());
        await settings.LoadAsync();
        settings.Current.UseCustomYtDlp = true;
        settings.Current.CustomYtDlpPath = CoreTests.FakeToolPath();
        settings.Current.UseCustomFfmpeg = true;
        settings.Current.CustomFfmpegPath = CoreTests.FakeToolPath();
        settings.Current.CustomFfprobePath = CoreTests.FakeToolPath();
        var denoDirectory = Path.Combine(root, "deno");
        Directory.CreateDirectory(denoDirectory);
        foreach (var source in Directory.EnumerateFiles(AppContext.BaseDirectory, "ModernTubeDownloader.FakeTool*"))
            File.Copy(source, Path.Combine(denoDirectory, Path.GetFileName(source)));
        var denoPath = Path.Combine(denoDirectory, "deno.exe");
        File.Copy(CoreTests.FakeToolPath(), denoPath);
        settings.Current.UseCustomDeno = true;
        settings.Current.CustomDenoPath = denoPath;
        await settings.SaveAsync();

        using (var services = AppServices.Create(paths))
        {
            await services.Tools.Initialization;
            using (var host = ModernFormsTestHost.Create())
            {
                var view = new DownloadsView(services); // The TestHost owns the hosted control.
                host.Show(view, 1200, 800);
#pragma warning disable xUnit1031 // TestHost requires creation, operations and disposal on one thread.
                view.AcceptExternalUrlAsync("https://youtu.be/abc", false).GetAwaiter().GetResult();
                host.ProcessPendingWork();
                var url = Find<TextBox>(view, "DownloadUrlInput");
                var details = Find<Button>(view, "DetailsButton");
                Assert.Equal("https://youtu.be/abc", url.Text);
                Assert.False(details.Enabled);

                view.AcceptExternalUrlAsync("https://youtu.be/abc", true).GetAwaiter().GetResult();
#pragma warning restore xUnit1031
                host.ProcessPendingWork();
                Assert.True(details.Enabled);

#pragma warning disable xUnit1031 // TestHost controls require the same thread for their lifetime.
                view.AcceptExternalRequestAsync(new ExternalMediaRequest(
                        "https://youtu.be/advanced", true, RequestedQualityPresetId: "1080",
                        RequestedContainer: PreferredVideoContainer.Mp4, OpenInApp: true))
                    .GetAwaiter().GetResult();
#pragma warning restore xUnit1031
                host.ProcessPendingWork();
                Assert.Equal("1080", ((LocalizedOption<QualityPreset>)Find<ComboBox>(view, "QualitySelector").SelectedItem!).Value.Id);
                Assert.Equal(PreferredVideoContainer.Mp4,
                    ((LocalizedOption<PreferredVideoContainer>)Find<ComboBox>(view, "ContainerSelector").SelectedItem!).Value);

#pragma warning disable xUnit1031 // TestHost controls require the same thread for their lifetime.
                view.AcceptExternalRequestAsync(new ExternalMediaRequest("https://youtu.be/other", true, true, "720"))
                    .GetAwaiter().GetResult();
                host.ProcessPendingWork();
                Assert.Contains(services.Queue.Snapshot(), item => item.QualityPresetId == "720");

                view.AcceptExternalRequestAsync(new ExternalMediaRequest(
                    "https://www.youtube.com/playlist?list=PL123", true, true, "1080", AutoQueuePlaylist: true))
                    .GetAwaiter().GetResult();
#pragma warning restore xUnit1031
                host.ProcessPendingWork();
                Assert.Equal(11, services.Queue.Snapshot().Count(item => item.PlaylistId == "synthetic-playlist"));
            }
            await services.ShutdownAsync();
        }
        Directory.Delete(root, recursive: true);
    }

    private static T Find<T>(Control root, string id) where T : Control
    {
        if (root is T typed && root.AccessibleAutomationId == id) return typed;
        foreach (Control child in root.Controls)
        {
            try { return Find<T>(child, id); }
            catch (InvalidOperationException) { }
        }
        throw new InvalidOperationException($"Control {id} was not found.");
    }
}
