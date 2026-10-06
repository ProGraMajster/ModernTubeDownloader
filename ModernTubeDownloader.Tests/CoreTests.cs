using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Utilities;

namespace ModernTubeDownloader.Tests;

public sealed class CoreTests
{
    [Fact]
    public void FormatSelector_ChoosesSeparateCompatibleStreamsFor1080p()
    {
        var metadata = TestMetadata();
        var selection = FormatSelector.Select(metadata, QualityPreset.Find("1080"));

        Assert.Equal("137", selection.VideoFormatId);
        Assert.Equal("140", selection.AudioFormatId);
        Assert.True(selection.RequiresMerge);
        Assert.Equal("mp4", FfmpegService.ChooseContainer(selection));
    }

    [Fact]
    public void FormatSelector_UsesCombinedStreamWhenItIsBestWithin720p()
    {
        var selection = FormatSelector.Select(TestMetadata(), QualityPreset.Find("720"));
        Assert.Equal("22", selection.VideoFormatId);
        Assert.Null(selection.AudioFormatId);
        Assert.False(selection.RequiresMerge);
    }

    [Fact]
    public void AvailableQualityPresets_HidePresetsAboveTheActualMaximum()
    {
        var metadata = TestMetadata();
        var fullPresets = FormatSelector.GetAvailablePresets(metadata);
        Assert.DoesNotContain(fullPresets, preset => preset.Id is "1440" or "2160");
        Assert.Contains(fullPresets, preset => preset.Id == "1080");
        metadata.Formats.RemoveAll(format => format.Height == 1080);

        var presets = FormatSelector.GetAvailablePresets(metadata);

        Assert.DoesNotContain(presets, preset => preset.Id == "1080");
        Assert.Contains(presets, preset => preset.Id == "720");
        Assert.Equal("22", FormatSelector.Select(metadata, QualityPreset.Find("1080")).VideoFormatId);
    }

    [Fact]
    public void AvailableQualityPresets_Expose4KAndSparseUpToStepsOnlyWhenMaximumSupportsThem()
    {
        var metadata = TestMetadata();
        metadata.Formats.Add(new MediaFormat
        {
            Id = "401", Extension = "webm", Height = 2160, Fps = 60,
            VideoCodec = "vp9", AudioCodec = "none", VideoBitrateKbps = 12000
        });
        metadata.Formats.Add(new MediaFormat
        {
            Id = "251", Extension = "webm", VideoCodec = "none", AudioCodec = "opus", AudioBitrateKbps = 160
        });

        var presets = FormatSelector.GetAvailablePresets(metadata);

        Assert.Contains(presets, preset => preset.Id == "2160");
        Assert.Contains(presets, preset => preset.Id == "1440");
        Assert.Contains(presets, preset => preset.Id == "1080");
        Assert.Equal("137", FormatSelector.Select(metadata, QualityPreset.Find("1440")).VideoFormatId);
    }

    [Fact]
    public void AudioOnlySelection_IgnoresVideoContainerPreference()
    {
        var selection = FormatSelector.Select(TestMetadata(), QualityPreset.Find("audio"), PreferredVideoContainer.WebM);

        Assert.True(selection.AudioOnly);
        Assert.Equal("140", selection.AudioFormatId);
        Assert.Equal("m4a", FfmpegService.ChooseContainer(selection, PreferredVideoContainer.WebM));
    }

    [Fact]
    public void Mp4Selection_UsesOnlyCopyCompatibleStreams()
    {
        var selection = FormatSelector.Select(TestMetadata(), QualityPreset.Find("1080"), PreferredVideoContainer.Mp4);

        Assert.Equal("137", selection.VideoFormatId);
        Assert.Equal("140", selection.AudioFormatId);
        Assert.Equal("mp4", FfmpegService.ChooseContainer(selection, PreferredVideoContainer.Mp4));
    }

    [Fact]
    public void Mp4Selection_FallsBackToCompatibleCombinedStreamWithoutTranscoding()
    {
        var metadata = TestMetadata();
        metadata.Formats.RemoveAll(format => format.Id == "140");
        metadata.Formats.Add(new MediaFormat
        {
            Id = "251", Extension = "webm", VideoCodec = "none", AudioCodec = "opus", AudioBitrateKbps = 160
        });

        var selection = FormatSelector.Select(metadata, QualityPreset.Find("1080"), PreferredVideoContainer.Mp4);

        Assert.Equal("22", selection.VideoFormatId);
        Assert.False(selection.RequiresMerge);
    }

    [Fact]
    public void Mp4Selection_RejectsWebMOnlyMediaInsteadOfTranscoding()
    {
        var metadata = WebMMetadata();

        Assert.Throws<InvalidOperationException>(() =>
            FormatSelector.Select(metadata, QualityPreset.Find("1080"), PreferredVideoContainer.Mp4));
    }

    [Fact]
    public void MkvSelection_AllowsStreamCopyAndRequestsRemuxForCombinedInput()
    {
        var selection = FormatSelector.Select(TestMetadata(), QualityPreset.Find("720"), PreferredVideoContainer.Mkv);

        Assert.False(selection.RequiresMerge);
        Assert.Equal("mkv", FfmpegService.ChooseContainer(selection, PreferredVideoContainer.Mkv));
        Assert.True(FfmpegService.RequiresRemux(selection, PreferredVideoContainer.Mkv));
    }

    [Fact]
    public void WebMSelection_AcceptsCompatibleStreamsAndRejectsMp4OnlyMedia()
    {
        var selection = FormatSelector.Select(WebMMetadata(), QualityPreset.Find("1080"), PreferredVideoContainer.WebM);

        Assert.Equal("248", selection.VideoFormatId);
        Assert.Equal("251", selection.AudioFormatId);
        Assert.Equal("webm", FfmpegService.ChooseContainer(selection, PreferredVideoContainer.WebM));
        Assert.Throws<InvalidOperationException>(() =>
            FormatSelector.Select(TestMetadata(), QualityPreset.Find("1080"), PreferredVideoContainer.WebM));
    }

    [Fact]
    public void ProgressParser_ReadsStableTemplateOutput()
    {
        Assert.True(YtDlpProgressParser.TryParse("MTD_PROGRESS|downloading|524288|1048576|NA|262144|2| 50.0%", out var value));
        Assert.Equal(50, value.Percent);
        Assert.Equal(524288, value.DownloadedBytes);
        Assert.Equal(1048576, value.TotalBytes);
        Assert.Equal(262144, value.SpeedBytesPerSecond);
        Assert.Equal(TimeSpan.FromSeconds(2), value.Eta);
    }

    [Theory]
    [InlineData("--output stolen.mp4")]
    [InlineData("-f 22")]
    [InlineData("--exec calc.exe")]
    [InlineData("https://unexpected.example/video")]
    public void CustomArguments_RejectApplicationOwnedOrAdditionalUrl(string value) =>
        Assert.Throws<ArgumentException>(() => CommandLineArgumentTokenizer.ParseSafeYtDlpArguments(value));

    [Fact]
    public void CustomArguments_PreserveQuotedValuesWithoutUsingAShell()
    {
        var result = CommandLineArgumentTokenizer.ParseSafeYtDlpArguments("--user-agent \"Modern Tube Downloader\" --retries 3");
        Assert.Equal(["--user-agent", "Modern Tube Downloader", "--retries", "3"], result);
    }

    [Fact]
    public void MetadataArguments_ApplySafeCustomOptionsBeforeTheMediaUrl()
    {
        var arguments = YtDlpMetadataService.BuildArguments(
            "https://example.test/watch/sample123",
            "--user-agent \"Modern Tube Downloader\" --retries 3");

        Assert.Contains("Modern Tube Downloader", arguments);
        Assert.Contains("--retries", arguments);
        Assert.Equal("--", arguments[^2]);
        Assert.Equal("https://example.test/watch/sample123", arguments[^1]);
    }

    [Fact]
    public void WatchUrlWithPlaylistParameter_IsAnalyzedAsOneVideo()
    {
        const string url = "https://www.youtube.com/watch?v=sample123&list=playlist456";

        Assert.True(YtDlpMetadataService.TryValidateSourceUrl(url, out var normalized, out _));
        var arguments = YtDlpMetadataService.BuildArguments(normalized, null);

        Assert.Contains("--no-playlist", arguments);
        Assert.Equal(url, arguments[^1]);
    }

    [Fact]
    public void YtDlpArguments_UseTheValidatedDenoPathExplicitly()
    {
        var arguments = YtDlpProcessRunner.BuildEffectiveArguments(@"C:\Managed Tools\deno.exe", ["--dump-single-json"]);

        Assert.Equal("--js-runtimes", arguments[0]);
        Assert.Equal(@"deno:C:\Managed Tools\deno.exe", arguments[1]);
        Assert.Equal("--dump-single-json", arguments[2]);
    }

    [Theory]
    [InlineData("deno 2.2.9")]
    [InlineData("unknown")]
    public void DenoVersionValidation_RejectsUnsupportedRuntime(string version) =>
        Assert.Throws<InvalidDataException>(() => ProcessToolBinaryValidator.ValidateDenoVersion(version));

    [Fact]
    public void DenoVersionValidation_AcceptsOfficialMinimum() =>
        ProcessToolBinaryValidator.ValidateDenoVersion("deno 2.3.0");

    [Theory]
    [InlineData("--username=private-user")]
    [InlineData("--password=private-password")]
    [InlineData("--add-header=Authorization: Bearer secret")]
    public void ProcessLogging_RedactsInlineCredentials(string value)
    {
        var rendered = AsyncProcessRunner.RedactArguments([value]);
        Assert.DoesNotContain("private", rendered, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", rendered, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("<redacted>", rendered);
    }

    [Fact]
    public async Task ProcessRunner_CancellationTerminatesChildProcess()
    {
        var runner = new AsyncProcessRunner(new NullAppLogger());
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(
            new ProcessRunRequest(FakeToolPath(), ["--sleep"]), cancellation.Token));

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ProcessRunner_ClosesDescendantHoldingRedirectedOutputAfterToolExit()
    {
        var runner = new AsyncProcessRunner(new NullAppLogger());
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = await runner.RunAsync(new ProcessRunRequest(FakeToolPath(),
            ["--spawn-child-with-inherited-output"]), cancellation.Token);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(result.StandardOutput, line => line.StartsWith("CHILD_PID|", StringComparison.Ordinal));
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(3),
            "A tool's descendant must not keep its redirected output pipe open after the tool exits.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("file:///c:/secret.txt")]
    public void MetadataUrlValidation_RejectsInvalidOrNonHttpInput(string value)
    {
        Assert.False(YtDlpMetadataService.TryValidateSourceUrl(value, out _, out var error));
        Assert.NotEmpty(error);
    }

    private static VideoMetadata TestMetadata() => new()
    {
        Id = "sample123",
        Title = "Sample",
        WebpageUrl = "https://example.test/watch/sample123",
        Formats =
        [
            new MediaFormat { Id = "137", Extension = "mp4", Height = 1080, Fps = 30, VideoCodec = "avc1", AudioCodec = "none", VideoBitrateKbps = 4500 },
            new MediaFormat { Id = "22", Extension = "mp4", Height = 720, Fps = 30, VideoCodec = "avc1", AudioCodec = "mp4a", TotalBitrateKbps = 1800 },
            new MediaFormat { Id = "140", Extension = "m4a", VideoCodec = "none", AudioCodec = "mp4a", AudioBitrateKbps = 129 }
        ]
    };

    private static VideoMetadata WebMMetadata() => new()
    {
        Id = "webm-sample",
        Title = "WebM sample",
        WebpageUrl = "https://example.test/watch/webm",
        Formats =
        [
            new MediaFormat { Id = "248", Extension = "webm", Height = 1080, VideoCodec = "vp9", AudioCodec = "none", VideoBitrateKbps = 3200 },
            new MediaFormat { Id = "251", Extension = "webm", VideoCodec = "none", AudioCodec = "opus", AudioBitrateKbps = 160 }
        ]
    };

    internal static string FakeToolPath()
    {
        var path = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "ModernTubeDownloader.FakeTool.exe" : "ModernTubeDownloader.FakeTool");
        Assert.True(File.Exists(path), $"Fake tool was not copied to {path}");
        return path;
    }
}
