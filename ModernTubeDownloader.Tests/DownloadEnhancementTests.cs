using System.Text.Json;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Utilities;

namespace ModernTubeDownloader.Tests;

public sealed class DownloadEnhancementTests
{
    [Fact]
    public void EmbeddedSubtitleArguments_CopyAudioVideoAndTagEachKnownLanguage()
    {
        var arguments = FfmpegService.BuildSubtitleEmbedArguments("movie.mp4",
            ["subtitle.en.srt", "subtitle.pl.vtt", "subtitle.invalid-code.srt"], "result.mp4").ToArray();
        Assert.Equal("copy", arguments[Array.IndexOf(arguments, "-c:v") + 1]);
        Assert.Equal("copy", arguments[Array.IndexOf(arguments, "-c:a") + 1]);
        Assert.Equal("mov_text", arguments[Array.IndexOf(arguments, "-c:s") + 1]);
        Assert.Equal("language=eng", arguments[Array.IndexOf(arguments, "-metadata:s:s:0") + 1]);
        Assert.Equal("language=pol", arguments[Array.IndexOf(arguments, "-metadata:s:s:1") + 1]);
        Assert.DoesNotContain("-metadata:s:s:2", arguments);
    }

    [Fact]
    public void FinalPathRename_ReservesTheSameBasenameForSubtitleSidecars()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ModernTubeDownloader.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "movie.en.srt"), "existing sidecar");
            Assert.Equal(Path.Combine(directory, "movie (2).mkv"),
                FileSystemUtilities.ResolveFinalPath(directory, "movie", "mkv",
                    ModernTubeDownloader.Settings.FileConflictBehavior.Rename, false, ["en.srt"]));
            Assert.Throws<IOException>(() => FileSystemUtilities.ResolveFinalPath(directory, "movie", "webm",
                ModernTubeDownloader.Settings.FileConflictBehavior.Fail, false, ["en.srt"]));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void SubtitleTracks_ReadManualAndAutomaticWithoutCopyingRawJson()
    {
        var metadata = JsonSerializer.Deserialize<VideoMetadata>("""
            {"subtitles":{"pl":[{"ext":"vtt"},{"ext":"srt"}]},
             "automatic_captions":{"en":[{"ext":"vtt"}]}}
            """)!;
        var tracks = SubtitleTrackReader.Read(metadata);
        Assert.Equal(2, tracks.Count);
        Assert.Contains(tracks, track => track.LanguageCode == "pl" && !track.IsAutomatic &&
            track.AvailableFormats.SequenceEqual(["vtt", "srt"]));
        Assert.Contains(tracks, track => track.LanguageCode == "en" && track.IsAutomatic &&
            track.SourceType == "automatic_captions");
    }

    [Fact]
    public void Cookies_DisabledDoesNotValidateOrAddArgument()
    {
        var arguments = YtDlpMetadataService.BuildArguments("https://example.test/watch/sample", null,
            false, "C:\\nonexistent cookies.txt");
        Assert.DoesNotContain("--cookies", arguments);
        Assert.DoesNotContain("--cookies-from-browser", arguments);
    }

    [Fact]
    public void Cookies_ValidUnicodePathIsUsedForVideoAndPlaylistAnalysisAndRedacted()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mtd cookies test", Guid.NewGuid().ToString("N"), "Zażółć");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "my cookies.txt");
        try
        {
            File.WriteAllText(path, "# Netscape HTTP Cookie File\n");
            foreach (var arguments in new[]
            {
                YtDlpMetadataService.BuildArguments("https://example.test/watch/sample", null, true, path),
                YtDlpMetadataService.BuildPlaylistArguments("https://example.test/playlist/sample", null, true, path)
            })
            {
                var index = Array.IndexOf(arguments.ToArray(), "--cookies");
                Assert.True(index >= 0);
                Assert.Equal(path, arguments[index + 1]);
                Assert.Equal("--", arguments[^2]);
                Assert.Contains("--cookies <cookie-file>", AsyncProcessRunner.RedactArguments(arguments));
                Assert.DoesNotContain(path, AsyncProcessRunner.RedactArguments(arguments));
                Assert.DoesNotContain(path, LogSanitizer.Redact($"yt-dlp rejected {path}"));
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void Cookies_MissingUnreadableAndMalformedFilesAreRejectedWithoutPathInException()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mtd-cookie-validation", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var missing = Path.Combine(directory, "missing.txt");
        var malformed = Path.Combine(directory, "malformed.txt");
        var locked = Path.Combine(directory, "locked.txt");
        try
        {
            Assert.Equal(CookieFileError.Missing, Assert.Throws<CookieFileException>(() => CookieFileService.Validate(missing)).Reason);
            File.WriteAllText(malformed, "not a cookie file");
            Assert.Equal(CookieFileError.UnsupportedFormat,
                Assert.Throws<CookieFileException>(() => CookieFileService.Validate(malformed)).Reason);
            var withBom = Path.Combine(directory, "with bom.txt");
            File.WriteAllText(withBom, "\uFEFF# Netscape HTTP Cookie File\n");
            Assert.Equal(withBom, CookieFileService.Validate(withBom));
            File.WriteAllText(locked, "# Netscape HTTP Cookie File\n");
            using (var exclusive = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var error = Assert.Throws<CookieFileException>(() => CookieFileService.Validate(locked));
                Assert.Equal(CookieFileError.Unreadable, error.Reason);
                Assert.DoesNotContain(locked, error.ToString());
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData("Could not find browser profile", DownloadFailureCategory.CookieProfileUnavailable)]
    [InlineData("Cookie database is locked", DownloadFailureCategory.CookieDatabaseLocked)]
    [InlineData("Could not copy Chrome cookie database", DownloadFailureCategory.CookieDatabaseLocked)]
    [InlineData("Could not decrypt cookies", DownloadFailureCategory.CookieUnavailable)]
    [InlineData("Failed to decrypt with DPAPI", DownloadFailureCategory.CookieUnavailable)]
    [InlineData("Sign in to confirm your age", DownloadFailureCategory.Authentication)]
    [InlineData("This is a private playlist", DownloadFailureCategory.Private)]
    public void AuthenticationErrors_ArePermanentAndSpecific(string message, DownloadFailureCategory expected)
    {
        var failure = DownloadFailureClassifier.Classify(new InvalidOperationException(message));
        Assert.Equal(expected, failure.Category);
        Assert.False(failure.Retryable);
    }

    [Theory]
    [InlineData(CookieFileError.Missing, DownloadFailureCategory.CookieFileMissing)]
    [InlineData(CookieFileError.Unreadable, DownloadFailureCategory.CookieFileUnreadable)]
    [InlineData(CookieFileError.UnsupportedFormat, DownloadFailureCategory.CookieFileUnsupported)]
    public void CookieFileErrors_AreSpecificAndPermanent(CookieFileError reason, DownloadFailureCategory expected)
    {
        var failure = DownloadFailureClassifier.Classify(new CookieFileException(reason));
        Assert.Equal(expected, failure.Category);
        Assert.False(failure.Retryable);
    }

    [Theory]
    [InlineData("Cookies file must be in Netscape format", DownloadFailureCategory.CookieFileRejected)]
    [InlineData("does not look like a Netscape format cookies file", DownloadFailureCategory.CookieFileRejected)]
    public void YtDlpCookieRejection_IsPermanentAndSpecific(string diagnostic, DownloadFailureCategory expected)
    {
        var result = new ProcessRunResult(1, [], [diagnostic], TimeSpan.Zero);
        var failure = DownloadFailureClassifier.Classify(new ExternalProcessException("yt-dlp failed", result));
        Assert.Equal(expected, failure.Category);
        Assert.False(failure.Retryable);
    }

    [Fact]
    public void SensitiveCookieDiagnostics_AreRemovedFromLogs()
    {
        var source = "ERROR: Could not copy Chrome cookie database from C:\\Users\\Someone\\Secret\\Cookies";
        Assert.DoesNotContain("Someone", LogSanitizer.Redact(source));
        Assert.DoesNotContain("Secret", LogSanitizer.Redact(source));
        Assert.DoesNotContain("Someone", LogSanitizer.Redact(
            "ERROR: Could not decrypt cookies from C:\\Users\\Someone\\Browser\\Default"));
        Assert.Equal("<browser-cookie-diagnostic>", LogSanitizer.Redact(
            "ERROR: Failed to decrypt with DPAPI from C:\\Users\\Someone\\Browser\\Default"));
    }

    [Theory]
    [InlineData(SponsorBlockMode.Mark, "sponsor,intro")]
    [InlineData(SponsorBlockMode.Remove, "sponsor,intro")]
    public void SponsorBlock_UsesVerifiedCategories(SponsorBlockMode mode, string expected)
    {
        var options = new SponsorBlockOptions { Mode = mode, Categories = ["sponsor", "intro"] };
        Assert.Equal(expected, options.ToYtDlpCategories());
    }

    [Fact]
    public void SponsorBlock_RemoveRejectsPointOfInterest()
    {
        var options = new SponsorBlockOptions { Mode = SponsorBlockMode.Remove, Categories = ["poi_highlight"] };
        Assert.Throws<ArgumentException>(options.ToYtDlpCategories);
    }

    [Theory]
    [InlineData(PreferredVideoContainer.Mkv, DownloadOptionIssue.None)]
    [InlineData(PreferredVideoContainer.Mp4, DownloadOptionIssue.MarkRequiresMkv)]
    [InlineData(PreferredVideoContainer.WebM, DownloadOptionIssue.MarkRequiresMkv)]
    [InlineData(PreferredVideoContainer.Auto, DownloadOptionIssue.MarkRequiresMkv)]
    public void SponsorBlockMark_OnlyExplicitMkvIsSupported(PreferredVideoContainer container, DownloadOptionIssue expected)
    {
        var sponsor = new SponsorBlockOptions { Mode = SponsorBlockMode.Mark };
        Assert.Equal(expected, DownloadOptionCompatibilityValidator.Check(MediaTimeRange.Full, new SubtitleOptions(), sponsor, container));
    }

    [Fact]
    public void SponsorBlockRemove_IsExperimentalButAllowedWithoutSubtitles()
    {
        var sponsor = new SponsorBlockOptions { Mode = SponsorBlockMode.Remove };
        Assert.Equal(DownloadOptionIssue.None,
            DownloadOptionCompatibilityValidator.Check(MediaTimeRange.Full, new SubtitleOptions(), sponsor, PreferredVideoContainer.Mp4));
        Assert.Equal("SponsorBlock.RemoveExperimentalWarning", DownloadOptionCompatibilityValidator.WarningKey(sponsor));
        Assert.Equal(DownloadOptionIssue.RemoveSubtitleConflict,
            DownloadOptionCompatibilityValidator.Check(MediaTimeRange.Full,
                new SubtitleOptions { Enabled = true }, sponsor, PreferredVideoContainer.Mp4));
    }

    [Fact]
    public void PlaylistBatchRejectsUnsupportedMarkAtomically()
    {
        var root = Path.Combine(Path.GetTempPath(), "mtd-enhancement-tests", Guid.NewGuid().ToString("N"));
        var queue = new DownloadQueueService(new QueuePersistenceService(AppPaths.Create(root), new NullAppLogger()),
            new NullAppLogger());
        var supported = new DownloadQueueItem { VideoId = "first", PreferredVideoContainer = PreferredVideoContainer.Mkv,
            SponsorBlockOptions = new SponsorBlockOptions { Mode = SponsorBlockMode.Mark, Categories = ["intro"] } };
        var unsupported = new DownloadQueueItem { VideoId = "second", PreferredVideoContainer = PreferredVideoContainer.Mp4,
            SponsorBlockOptions = new SponsorBlockOptions { Mode = SponsorBlockMode.Mark, Categories = ["intro"] } };
        var error = Assert.Throws<DownloadOptionException>(() => queue.AddRangeSkippingDuplicates([supported, unsupported]));
        Assert.Equal(DownloadOptionIssue.MarkRequiresMkv, error.Issue);
        Assert.Empty(queue.Snapshot());
        Assert.Equal(2, queue.AddRangeSkippingDuplicates([supported, new DownloadQueueItem
        {
            VideoId = "second", PreferredVideoContainer = PreferredVideoContainer.Mkv,
            SponsorBlockOptions = new SponsorBlockOptions { Mode = SponsorBlockMode.Mark, Categories = ["intro"] }
        }]).Added);
    }

    [Fact]
    public async Task CookieSettingsSaveRejectsMissingFileAndNeverPersistsContents()
    {
        var root = Path.Combine(Path.GetTempPath(), "mtd-enhancement-tests", Guid.NewGuid().ToString("N"));
        var paths = AppPaths.Create(root);
        paths.EnsureCreated();
        var settings = new SettingsService(paths, new NullAppLogger());
        settings.Current.UseCookieFile = true;
        settings.Current.CookieFilePath = Path.Combine(root, "missing cookies.txt");
        Assert.Equal(CookieFileError.Missing,
            (await Assert.ThrowsAsync<CookieFileException>(() => settings.SaveAsync())).Reason);
        Assert.False(File.Exists(paths.SettingsFile));
        var cookies = Path.Combine(root, "valid cookies.txt");
        await File.WriteAllTextAsync(cookies, "# Netscape HTTP Cookie File\n.example.test\tTRUE\t/\tFALSE\t0\tsession\tprivate-value\n");
        settings.Current.CookieFilePath = cookies;
        await settings.SaveAsync();
        var json = await File.ReadAllTextAsync(paths.SettingsFile);
        Assert.Contains("useCookieFile", json);
        Assert.Contains("cookieFilePath", json);
        Assert.DoesNotContain("private-value", json);
        Assert.DoesNotContain("browserCookies", json, StringComparison.OrdinalIgnoreCase);
        File.Delete(cookies);
        var runtimeError = Assert.Throws<CookieFileException>(() => YtDlpMetadataService.BuildArguments(
            "https://example.test/watch/sample", null, settings.Current.UseCookieFile, settings.Current.CookieFilePath));
        Assert.Equal(CookieFileError.Missing, runtimeError.Reason);
    }

    [Theory]
    [InlineData("pl")]
    [InlineData("en.*")]
    [InlineData("ja")]
    public void SubtitleLanguages_AcceptStableCodesAndWildcards(string code) =>
        new SubtitleOptions { Enabled = true, Languages = [code] }.Validate();

    [Theory]
    [InlineData("../../secret")]
    [InlineData("en,ru")]
    [InlineData("*")]
    public void SubtitleLanguages_RejectUntrustedPatterns(string code) =>
        Assert.Throws<ArgumentException>(() => new SubtitleOptions { Enabled = true, Languages = [code] }.Validate());

    [Fact]
    public void SubtitleSidecarCannotBeDisabledWithoutEmbedding() =>
        Assert.Throws<ArgumentException>(() => new SubtitleOptions
            { Enabled = true, Languages = ["en"], Embed = false, KeepFiles = false }.Validate());

    [Fact]
    public async Task QueueReloadAndRetry_PreserveSubtitleAndSponsorBlockOptions()
    {
        var root = Path.Combine(Path.GetTempPath(), "mtd-enhancement-tests", Guid.NewGuid().ToString("N"));
        var paths = AppPaths.Create(root);
        paths.EnsureCreated();
        var queue = new DownloadQueueService(new QueuePersistenceService(paths, new NullAppLogger()), new NullAppLogger());
        var item = new DownloadQueueItem
        {
            SourceUrl = "https://example.test/watch/sample", VideoId = "sample", Status = DownloadStatus.Failed,
            PreferredVideoContainer = PreferredVideoContainer.Mkv,
            SubtitleOptions = new SubtitleOptions { Enabled = true, Source = SubtitleSource.Both,
                Languages = ["pl", "en.*"], Format = SubtitleFormat.Srt, Embed = true },
            SponsorBlockOptions = new SponsorBlockOptions { Mode = SponsorBlockMode.Mark, Categories = ["intro"] }
        };
        queue.Add(item);
        Assert.True(queue.Retry(item.Id));
        await queue.SaveAsync();
        var loaded = Assert.Single(await new QueuePersistenceService(paths, new NullAppLogger()).LoadAsync());
        Assert.Equal(["pl", "en.*"], loaded.SubtitleOptions.Languages);
        Assert.True(loaded.SubtitleOptions.Embed);
        Assert.Equal(SponsorBlockMode.Mark, loaded.SponsorBlockOptions.Mode);
        Assert.Equal(["intro"], loaded.SponsorBlockOptions.Categories);
    }

    [Fact]
    public void QueueDeduplication_DistinguishesPerJobOptions()
    {
        var root = Path.Combine(Path.GetTempPath(), "mtd-enhancement-tests", Guid.NewGuid().ToString("N"));
        var queue = new DownloadQueueService(new QueuePersistenceService(AppPaths.Create(root), new NullAppLogger()),
            new NullAppLogger());
        var plain = new DownloadQueueItem { VideoId = "same", QualityPresetId = "720" };
        var withSubtitles = new DownloadQueueItem { VideoId = "same", QualityPresetId = "720",
            SubtitleOptions = new SubtitleOptions { Enabled = true, Languages = ["en"] } };
        var result = queue.AddRangeSkippingDuplicates([plain, withSubtitles, new DownloadQueueItem
            { VideoId = "same", QualityPresetId = "720" }]);
        Assert.Equal(2, result.Added);
        Assert.Equal(1, result.DuplicatesSkipped);
    }
}
