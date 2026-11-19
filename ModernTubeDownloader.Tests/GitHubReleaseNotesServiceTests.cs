using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Services;

namespace ModernTubeDownloader.Tests;

public sealed class GitHubReleaseNotesServiceTests
{
    private const string Body = "# ModernTubeDownloader 1.3.0\n\n## New\n### Dashboard\n- A shared runtime.\n\n## Improvements\n- Bounded history.\n\n## Fixes\n- Stable popups.\n\n## Known Issues\n- Framework #534.\n";
    private const string OfficialPage = GitHubApplicationReleaseSource.Repository + "/releases/tag/v1.3.0";

    private sealed class Clock : TimeProvider
    {
        private readonly DeadlineTestClock deadlines = new();
        private DateTimeOffset now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
            => deadlines.CreateTimer(callback, state, dueTime, period);
        public void Advance(TimeSpan amount) { now += amount; deadlines.Advance(amount); }
    }

    private sealed class Handler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Respond { get; set; }
            = (_, _) => Task.FromResult(Release());
        public List<(string Uri, string? ETag)> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requests.Add((request.RequestUri!.AbsoluteUri, request.Headers.IfNoneMatch.SingleOrDefault()?.ToString()));
            Assert.Contains(request.Headers.Accept, value => value.MediaType == "application/vnd.github+json");
            Assert.Equal("2022-11-28", request.Headers.GetValues("X-GitHub-Api-Version").Single());
            return Respond(request, token);
        }
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "mtd-release-notes-" + Guid.NewGuid().ToString("N"));
        public Handler Handler { get; } = new();
        public Clock Clock { get; } = new();
        public HttpClient Http { get; }
        public string CacheDirectory => Path.Combine(Root, "release-notes-cache");
        public string CacheFile => Path.Combine(CacheDirectory, "v1.3.0.json");
        public Fixture() { Directory.CreateDirectory(Root); Http = new HttpClient(Handler); }
        public GitHubReleaseNotesService Service() => new(AppPaths.Create(Root), Http, new NullAppLogger(), Clock);
        public void Dispose() { Http.Dispose(); Directory.Delete(Root, true); }
    }

    private static HttpResponseMessage Release(string version = "1.3.0", string? body = Body,
        bool draft = false, bool prerelease = false, string? page = null, string? etag = "\"notes-1\"")
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                tag_name = "v" + version, draft, prerelease, body,
                html_url = page ?? GitHubApplicationReleaseSource.Repository + "/releases/tag/v" + version
            }), Encoding.UTF8, "application/json")
        };
        if (etag is not null) response.Headers.ETag = EntityTagHeaderValue.Parse(etag);
        return response;
    }

    [Fact]
    public async Task ExactInstalledTagIsFetchedOnceAndReopenReadsDurableCacheWithoutTouchingUserStores()
    {
        using var f = new Fixture();
        foreach (var name in new[] { "settings.json", "history.json", "queue.json", "experience.json", "live-sessions.json" })
            await File.WriteAllTextAsync(Path.Combine(f.Root, name), "preserved:" + name);
        var notes = Assert.IsType<RemoteReleaseNotes>(await f.Service().FetchAsync("1.3.0+build.42", false));
        Assert.Equal(ReleaseNotesOrigin.GitHub, notes.Origin);
        Assert.Equal("1.3.0", notes.Version); Assert.Equal(Body, notes.Body);
        Assert.Equal(OfficialPage, notes.OfficialUrl.AbsoluteUri);
        Assert.Equal(new[] { "new", "improved", "fixed", "limitations" }, notes.Sections.Select(section => section.Category));
        Assert.Contains("Dashboard", notes.Sections[0].Items[0]);
        Assert.Contains("A shared runtime.", notes.Sections[0].Items);
        Assert.Equal("https://api.github.com/repos/ProGraMajster/ModernTubeDownloader/releases/tags/v1.3.0", Assert.Single(f.Handler.Requests).Uri);
        Assert.True(File.Exists(f.CacheFile));
        f.Handler.Respond = (_, _) => throw new HttpRequestException("offline");
        var reopened = Assert.IsType<RemoteReleaseNotes>(await f.Service().FetchAsync("1.3.0", false));
        Assert.Equal(ReleaseNotesOrigin.Cache, reopened.Origin); Assert.Equal(Body, reopened.Body);
        Assert.Single(f.Handler.Requests);
        foreach (var name in new[] { "settings.json", "history.json", "queue.json", "experience.json", "live-sessions.json" })
            Assert.Equal("preserved:" + name, await File.ReadAllTextAsync(Path.Combine(f.Root, name)));
    }

    [Fact]
    public async Task ExpiredCacheUsesETagAnd304RenewsTtlDurably()
    {
        using var f = new Fixture(); var service = f.Service();
        await service.FetchAsync("1.3.0", false);
        f.Clock.Advance(GitHubReleaseNotesService.CacheLifetime + TimeSpan.FromSeconds(1));
        f.Handler.Respond = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));
        var notes = Assert.IsType<RemoteReleaseNotes>(await service.FetchAsync("1.3.0", false));
        Assert.Equal(ReleaseNotesOrigin.Cache, notes.Origin);
        Assert.Equal("\"notes-1\"", f.Handler.Requests.Last().ETag);
        Assert.Equal(2, f.Handler.Requests.Count);
        f.Clock.Advance(TimeSpan.FromHours(1));
        Assert.NotNull(await f.Service().FetchAsync("1.3.0", false));
        Assert.Equal(2, f.Handler.Requests.Count);
    }

    [Theory]
    [InlineData(404)] [InlineData(403)] [InlineData(429)] [InlineData(500)] [InlineData(304)]
    public async Task OrdinaryUnavailableResponsesUseLocalFallbackWithoutCreatingCache(int status)
    {
        using var f = new Fixture();
        f.Handler.Respond = (_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status));
        Assert.Null(await f.Service().FetchAsync("1.3.0", false));
        Assert.False(Directory.Exists(f.CacheDirectory));
    }

    [Theory]
    [InlineData(404)] [InlineData(403)] [InlineData(429)] [InlineData(500)]
    public async Task UnavailableRefreshPreservesLastValidatedCache(int status)
    {
        using var f = new Fixture(); var service = f.Service();
        await service.FetchAsync("1.3.0", false);
        var original = await File.ReadAllBytesAsync(f.CacheFile);
        f.Clock.Advance(GitHubReleaseNotesService.CacheLifetime + TimeSpan.FromMinutes(1));
        f.Handler.Respond = (_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status));
        var notes = Assert.IsType<RemoteReleaseNotes>(await service.FetchAsync("1.3.0", true));
        Assert.Equal(ReleaseNotesOrigin.Cache, notes.Origin);
        Assert.Equal(original, await File.ReadAllBytesAsync(f.CacheFile));
    }

    [Fact]
    public async Task OfflineExpiredCacheSurvivesServiceRecreation()
    {
        using var f = new Fixture(); await f.Service().FetchAsync("1.3.0", false);
        f.Clock.Advance(TimeSpan.FromDays(20));
        f.Handler.Respond = (_, _) => throw new HttpRequestException("offline");
        var notes = Assert.IsType<RemoteReleaseNotes>(await f.Service().FetchAsync("1.3.0", false));
        Assert.Equal(ReleaseNotesOrigin.Cache, notes.Origin);
        Assert.Equal(2, f.Handler.Requests.Count);
    }

    [Fact]
    public async Task TimeoutIsDrivenByControlledClockAndDoesNotCancelTheSharedClient()
    {
        using var f = new Fixture(); var service = f.Service();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Handler.Respond = async (_, token) => { entered.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, token); return Release(); };
        var pending = service.FetchAsync("1.3.0", false);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        f.Clock.Advance(TimeSpan.FromSeconds(7)); Assert.False(pending.IsCompleted);
        f.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Null(await pending.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.False(File.Exists(f.CacheFile));
        f.Clock.Advance(TimeSpan.FromMinutes(1)); f.Handler.Respond = (_, _) => Task.FromResult(Release());
        Assert.Equal(ReleaseNotesOrigin.GitHub, (await service.FetchAsync("1.3.0", false))!.Origin);
    }

    [Fact]
    public async Task ClosingCancelsFetchWithoutCacheWriteAndImmediateReopenCanFetchAgain()
    {
        using var f = new Fixture(); var service = f.Service(); using var closing = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Handler.Respond = async (_, token) => { entered.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, token); return Release(); };
        var pending = service.FetchAsync("1.3.0", false, closing.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); closing.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.False(File.Exists(f.CacheFile));
        f.Handler.Respond = (_, _) => Task.FromResult(Release());
        Assert.NotNull(await service.FetchAsync("1.3.0", false));
        Assert.Equal(2, f.Handler.Requests.Count);
    }

    [Fact]
    public async Task ConcurrentOpenIsSerializedAndQueuedCallerCanCancelIndependently()
    {
        using var f = new Fixture(); var service = f.Service();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Handler.Respond = (_, _) => { entered.TrySetResult(); return release.Task; };
        var first = service.FetchAsync("1.3.0", false); await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        using var closing = new CancellationTokenSource();
        var canceled = service.FetchAsync("1.3.0", false, closing.Token); closing.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled);
        var second = service.FetchAsync("1.3.0", false);
        release.SetResult(Release());
        Assert.Equal(ReleaseNotesOrigin.GitHub, (await first.WaitAsync(TimeSpan.FromSeconds(10)))!.Origin);
        Assert.Equal(ReleaseNotesOrigin.Cache, (await second.WaitAsync(TimeSpan.FromSeconds(10)))!.Origin);
        Assert.Single(f.Handler.Requests);
    }

    [Fact]
    public async Task ForceRefreshBypassesTtlButNotRateLimitAndHonorsRetryAfter()
    {
        using var f = new Fixture(); var service = f.Service();
        await service.FetchAsync("1.3.0", false); await service.FetchAsync("1.3.0", true);
        Assert.Single(f.Handler.Requests);
        f.Clock.Advance(GitHubReleaseNotesService.MinimumRequestInterval);
        f.Handler.Respond = (_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(10));
            return Task.FromResult(response);
        };
        await service.FetchAsync("1.3.0", true); Assert.Equal(2, f.Handler.Requests.Count);
        f.Clock.Advance(TimeSpan.FromMinutes(9)); await service.FetchAsync("1.3.0", true);
        Assert.Equal(2, f.Handler.Requests.Count);
        f.Clock.Advance(TimeSpan.FromMinutes(1)); f.Handler.Respond = (_, _) => Task.FromResult(Release(body: Body.Replace("Stable popups.", "Updated popup notes.")));
        Assert.Contains("Updated popup notes.", (await service.FetchAsync("1.3.0", true))!.Body);
        Assert.Equal(3, f.Handler.Requests.Count);
    }

    [Theory]
    [InlineData("1.2.1", false, false)] [InlineData("1.4.0", false, false)]
    [InlineData("1.3.0-candidate.1", false, true)] [InlineData("1.3.0", true, false)]
    [InlineData("1.3.0", false, true)]
    public async Task AnotherTagDraftOrPrereleaseCannotReplaceInstalledStableNotes(string version, bool draft, bool prerelease)
    {
        using var f = new Fixture(); f.Handler.Respond = (_, _) => Task.FromResult(Release(version, draft: draft, prerelease: prerelease));
        Assert.Null(await f.Service().FetchAsync("1.3.0", false));
        Assert.False(File.Exists(f.CacheFile));
    }

    [Fact]
    public async Task CandidateUsesOnlyItsOwnExactTagAndOwnCache()
    {
        using var f = new Fixture();
        f.Handler.Respond = (_, _) => Task.FromResult(Release("1.3.0-candidate.1", prerelease: true));
        var candidate = Assert.IsType<RemoteReleaseNotes>(await f.Service().FetchAsync("1.3.0-candidate.1", false));
        Assert.Equal("1.3.0-candidate.1", candidate.Version);
        Assert.Null(await f.Service().FetchAsync("1.3.0", false));
        Assert.False(File.Exists(f.CacheFile));
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData(" ")] [InlineData("not four categories")]
    public async Task MissingOrUnexpectedBodyIsRejected(string? body)
    {
        using var f = new Fixture(); f.Handler.Respond = (_, _) => Task.FromResult(Release(body: body));
        Assert.Null(await f.Service().FetchAsync("1.3.0", false));
        Assert.False(File.Exists(f.CacheFile));
    }

    [Fact]
    public async Task MissingBodyPropertyIsRejected()
    {
        using var f = new Fixture();
        f.Handler.Respond = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent(JsonSerializer.Serialize(new { tag_name = "v1.3.0", draft = false, prerelease = false, html_url = OfficialPage })) });
        Assert.Null(await f.Service().FetchAsync("1.3.0", false));
    }

    [Theory]
    [InlineData("https://evil.example/releases/tag/v1.3.0")]
    [InlineData("http://github.com/ProGraMajster/ModernTubeDownloader/releases/tag/v1.3.0")]
    [InlineData(OfficialPage + "?next=other")]
    [InlineData("javascript:alert(1)")]
    public async Task UnapprovedReleasePageIsRejected(string page)
    {
        using var f = new Fixture(); f.Handler.Respond = (_, _) => Task.FromResult(Release(page: page));
        Assert.Null(await f.Service().FetchAsync("1.3.0", false));
        Assert.False(File.Exists(f.CacheFile));
    }

    [Fact]
    public async Task RedirectedResponseIsNotConsumedOrCached()
    {
        using var f = new Fixture();
        f.Handler.Respond = (_, _) => { var response = Release(); response.RequestMessage = new(HttpMethod.Get, "https://evil.example/notes"); return Task.FromResult(response); };
        Assert.Null(await f.Service().FetchAsync("1.3.0", false)); Assert.False(File.Exists(f.CacheFile));
    }

    [Fact]
    public async Task HtmlScriptsImagesAndArbitraryLinksRemainLiteralTextWithoutAdditionalRequests()
    {
        using var f = new Fixture();
        const string markup = "<script>alert(1)</script> ![tracking](https://evil.example/image) [execute](javascript:alert(2)) <img src='https://evil.example/a'>";
        var body = Body.Replace("A shared runtime.", markup);
        f.Handler.Respond = (_, _) => Task.FromResult(Release(body: body));
        var notes = Assert.IsType<RemoteReleaseNotes>(await f.Service().FetchAsync("1.3.0", false));
        Assert.Contains(markup, notes.Sections[0].Items);
        Assert.Single(f.Handler.Requests); Assert.Equal(OfficialPage, notes.OfficialUrl.AbsoluteUri);
    }

    [Theory]
    [InlineData("1.3.0/../other")] [InlineData("1.3.0?evil")] [InlineData("1.3.0#evil")]
    [InlineData("https://evil.example")] [InlineData("1.3.0\n")] [InlineData("1.3.0\\cache")]
    public async Task InvalidInstalledVersionCannotSelectNetworkOrCachePath(string version)
    {
        using var f = new Fixture(); Assert.Null(await f.Service().FetchAsync(version, false));
        Assert.Empty(f.Handler.Requests); Assert.Empty(Directory.EnumerateFileSystemEntries(f.Root));
    }

    [Fact]
    public async Task OversizedBodyAndPayloadCannotPopulateCache()
    {
        using var f = new Fixture();
        f.Handler.Respond = (_, _) => Task.FromResult(Release(body: new string('x', GitHubReleaseNotesService.MaximumBodyBytes + 1)));
        Assert.Null(await f.Service().FetchAsync("1.3.0", false));
        f.Handler.Respond = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new ByteArrayContent(new byte[GitHubReleaseNotesService.MaximumPayloadBytes + 1]) });
        Assert.Null(await f.Service().FetchAsync("1.3.0", false)); Assert.False(File.Exists(f.CacheFile));
    }

    [Fact]
    public async Task CorruptWrongVersionAndOversizedCacheFallBackLocallyWithoutChangingUserData()
    {
        using var f = new Fixture(); Directory.CreateDirectory(f.CacheDirectory);
        f.Handler.Respond = (_, _) => throw new HttpRequestException("offline");
        await File.WriteAllTextAsync(f.CacheFile, "{broken"); Assert.Null(await f.Service().FetchAsync("1.3.0", false));
        await File.WriteAllTextAsync(f.CacheFile, JsonSerializer.Serialize(new { Schema = 1, Version = "1.4.0", Body, OfficialUrl = OfficialPage, ValidatedAt = f.Clock.GetUtcNow(), ETag = "\"one\"" }));
        Assert.Null(await f.Service().FetchAsync("1.3.0", false));
        await File.WriteAllBytesAsync(f.CacheFile, new byte[GitHubReleaseNotesService.MaximumCacheBytes + 1]);
        Assert.Null(await f.Service().FetchAsync("1.3.0", false));
        Assert.Equal(GitHubReleaseNotesService.MaximumCacheBytes + 1, new FileInfo(f.CacheFile).Length);
    }

    [Fact]
    public async Task ValidatedNewResponseReplacesCorruptCacheAtomicallyAndRetainsOnlyFourTags()
    {
        using var f = new Fixture(); Directory.CreateDirectory(f.CacheDirectory);
        await File.WriteAllTextAsync(f.CacheFile, "corrupt old cache");
        for (var patch = 0; patch < 6; patch++)
        {
            var version = "1.3." + patch;
            f.Handler.Respond = (_, _) => Task.FromResult(Release(version));
            Assert.NotNull(await f.Service().FetchAsync(version, false));
        }
        Assert.Equal(GitHubReleaseNotesService.MaximumCacheEntries, Directory.GetFiles(f.CacheDirectory, "v*.json").Length);
        Assert.Empty(Directory.GetFiles(f.CacheDirectory, "*.tmp"));
        Assert.True(File.Exists(Path.Combine(f.CacheDirectory, "v1.3.5.json")));
        Assert.All(Directory.GetFiles(f.CacheDirectory), path => Assert.InRange(new FileInfo(path).Length, 1, GitHubReleaseNotesService.MaximumCacheBytes));
    }

    [Fact]
    public async Task CacheDirectoryJunctionCannotReadOrWriteOutsideProfile()
    {
        using var f = new Fixture();
        var outside = Path.Combine(f.Root, "outside-cache"); Directory.CreateDirectory(outside);
        var sentinel = Path.Combine(outside, "v1.3.0.json");
        var externalCache = JsonSerializer.Serialize(new { Schema = 1, Version = "1.3.0", Body, OfficialUrl = OfficialPage, ValidatedAt = f.Clock.GetUtcNow(), ETag = "\"outside\"" });
        await File.WriteAllTextAsync(sentinel, externalCache);
        using var process = Process.Start(new ProcessStartInfo("cmd.exe")
        {
            Arguments = $"/d /c mklink /J \"{f.CacheDirectory}\" \"{outside}\"", CreateNoWindow = true,
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
        })!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(); Assert.True(process.ExitCode == 0, await stdout + await stderr);
        try
        {
            Assert.True((File.GetAttributes(f.CacheDirectory) & FileAttributes.ReparsePoint) != 0);
            f.Handler.Respond = (_, _) => throw new HttpRequestException("offline");
            Assert.Null(await f.Service().FetchAsync("1.3.0", false));
            f.Handler.Respond = (_, _) => Task.FromResult(Release());
            Assert.Equal(ReleaseNotesOrigin.GitHub, (await f.Service().FetchAsync("1.3.0", false))!.Origin);
            Assert.Equal(externalCache, await File.ReadAllTextAsync(sentinel)); Assert.Single(Directory.GetFiles(outside));
            f.Handler.Respond = (_, _) => throw new HttpRequestException("offline");
            Assert.Null(await f.Service().FetchAsync("1.3.0", false));
        }
        finally { Directory.Delete(f.CacheDirectory); }
    }

    [Fact]
    public async Task UnwritableCacheDoesNotLoseSuccessfulNetworkNotes()
    {
        using var f = new Fixture(); await File.WriteAllTextAsync(f.CacheDirectory, "directory blocked by regular file");
        Assert.Equal(ReleaseNotesOrigin.GitHub, (await f.Service().FetchAsync("1.3.0", false))!.Origin);
        Assert.Equal("directory blocked by regular file", await File.ReadAllTextAsync(f.CacheDirectory));
    }

    [Fact]
    public void ParserPreservesPreambleSubheadingsParagraphsAndCodeAndRejectsDuplicateOrHugeSections()
    {
        var notes = ReleaseNotesMarkdown.Parse(Body.Replace("## New", "Introduction.\n\n## New")
            .Replace("A shared runtime.", "A shared runtime.\n\n```text\n## not a category inside code\n```"));
        Assert.Contains("Introduction.", notes[0].Items);
        Assert.Contains(notes[0].Items, item => item.Contains("## not a category inside code", StringComparison.Ordinal));
        Assert.Throws<InvalidDataException>(() => ReleaseNotesMarkdown.Parse(Body + "\n## New\n- Duplicate."));
        Assert.Throws<InvalidDataException>(() => ReleaseNotesMarkdown.Parse(Body.Replace("A shared runtime.", new string('x', 16 * 1024 + 1))));
        Assert.Throws<InvalidDataException>(() => ReleaseNotesMarkdown.Parse(Body.Replace("A shared runtime.", string.Join('\n', Enumerable.Repeat("- many blocks", 193)))));
    }
}
