using ModernTubeDownloader.Authentication;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Services;
using System.IO.Pipelines;
using System.Text.Json;

namespace ModernTubeDownloader.Tests;

[Trait("Category", "CookieSecurity")]
public sealed class CookieRefreshTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "mtd-refresh-tests", Guid.NewGuid().ToString("N"));
    private const string Origin = "chrome-extension://aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/";
    private const string Url = "https://example.test/auth-refresh";
    private readonly RefreshTestClock clock = new();
    private sealed class RefreshTestClock : TimeProvider
    {
        private readonly DateTimeOffset start = DateTimeOffset.UtcNow;
        private long elapsedTicks;
        public override DateTimeOffset GetUtcNow() => start.AddTicks(Interlocked.Read(ref elapsedTicks));
        public void AdvanceTo(DateTimeOffset instant) => Interlocked.Exchange(ref elapsedTicks, (instant - start).Ticks);
    }
    private static CookieMessage Snapshot(string profile, long version, string? request = null) => new()
    {
        Type = "cookieSnapshot", Browser = "chrome", ProfileId = profile, SnapshotVersion = version,
        GeneratedAtUtc = DateTimeOffset.UtcNow, Scopes = ["example.test"], RefreshBeforeUse = true, RequestId = request,
        Cookies = [new() { Domain = ".example.test", Name = version == 1 ? "mtd_refresh_stale" : "mtd_refresh_fresh",
            Value = Guid.NewGuid().ToString("N"), Session = true }]
    };
    private async Task<YtDlpAuthenticationProvider> Provider()
    {
        var paths = AppPaths.Create(root); paths.EnsureCreated();
        var settings = new SettingsService(paths, new NullAppLogger()); await settings.LoadAsync();
        settings.Current.CookieSource = AuthenticationSource.SyncedBrowserProfile;
        // Expiry belongs to the scenario, not DPAPI/disk/thread-pool speed on a CI worker.
        // The production provider still uses the system clock and its unchanged 20 s budget.
        return new(paths, settings, clock) { RefreshTimeout = TimeSpan.FromMilliseconds(600) };
    }
    private static ExternalProcessException AuthFailure() => new("Authentication required",
        new(1, [], ["ERROR: Sign in; authentication required"], TimeSpan.Zero));
    private static async Task Respond(CookieStore store, string mode, RefreshTestClock? clock = null)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        CookieRefreshRequest? request;
        while ((request = store.PendingRefreshes(Origin, "chrome").SingleOrDefault()) is null)
            await Task.Delay(10, deadline.Token);
        if (mode == "timeout")
        {
            Assert.NotNull(clock);
            clock.AdvanceTo(request.Deadline);
            return;
        }
        if (mode == "same-version")
        {
            Assert.Throws<CookieSecurityException>(() => NativeCookieProtocol.Handle(store, Origin, "chrome", Snapshot(request.ProfileId, 1, request.RequestId)));
            mode = "rejected";
        }
        if (mode is "refreshed" or "second-auth-failure")
        {
            var ack = NativeCookieProtocol.Handle(store, Origin, "chrome", Snapshot(request.ProfileId, 2, request.RequestId));
            Assert.True(ack.Ok); Assert.Equal(2, ack.SnapshotVersion);
        }
        else NativeCookieProtocol.Handle(store, Origin, "chrome", new()
        { Type = "refreshResult", Browser = "chrome", RequestId = request.RequestId, RefreshStatus = mode });
    }

    [Theory]
    [InlineData("refreshed", 2)] [InlineData("unavailable", 1)] [InlineData("permission-revoked", 1)]
    [InlineData("timeout", 1)] [InlineData("same-version", 1)] [InlineData("rejected", 1)]
    [InlineData("disabled", 1)] [InlineData("second-auth-failure", 2)]
    public async Task AuthenticationFailureHasOneBudgetAndOnlyAcceptedNewSnapshotGetsNewJar(string mode, int expectedCalls)
    {
        var provider = await Provider(); var profile = Guid.NewGuid().ToString("D");
        provider.Store.Apply(Origin, Snapshot(profile, 1));
        if (mode != "unavailable") provider.Store.BridgeHeartbeat(Origin, "chrome", Guid.NewGuid().ToString("D"));
        using var watchdog = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var bridge = mode == "unavailable" ? Task.CompletedTask : Respond(provider.Store, mode, clock);
        var calls = 0; var paths = new List<string>(); var versions = new List<long>();
        Task<int> Invoke()
        {
            calls++;
            using var invocation = provider.Prepare(Url);
            paths.Add(invocation.CookiePath!);
            var fresh = File.ReadAllText(invocation.CookiePath!).Contains("mtd_refresh_fresh", StringComparison.Ordinal);
            versions.Add(fresh ? 2 : 1);
            return fresh && mode != "second-auth-failure" ? Task.FromResult(42) : Task.FromException<int>(AuthFailure());
        }
        if (mode == "refreshed") Assert.Equal(42, await provider.ExecuteAsync(Url, Invoke, watchdog.Token));
        else
        {
            var error = await Assert.ThrowsAsync<CookieRefreshException>(() => provider.ExecuteAsync(Url, Invoke, watchdog.Token));
            Assert.Equal(mode == "same-version" ? "rejected" : mode, error.Status);
            Assert.False(DownloadFailureClassifier.Classify(error).Retryable);
        }
        await bridge.WaitAsync(watchdog.Token);
        Assert.Equal(expectedCalls, calls);
        Assert.Equal(expectedCalls, paths.Distinct().Count());
        Assert.Equal(expectedCalls == 2 ? new long[] { 1, 2 } : [1], versions);
        Assert.Empty(provider.Store.PendingRefreshes(Origin, "chrome"));
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(root, "Auth", "tmp")));
        Assert.Equal(expectedCalls == 2 ? 2 : 1, provider.Store.GetProfiles().Single().SnapshotVersion);
    }

    [Fact]
    public async Task SnapshotAcknowledgedAtExpiredDeadlineCannotProveRefresh()
    {
        var provider = await Provider();
        var profile = Guid.NewGuid().ToString("D");
        provider.Store.Apply(Origin, Snapshot(profile, 1));
        provider.Store.BridgeHeartbeat(Origin, "chrome", Guid.NewGuid().ToString("D"));
        var outcome = provider.Store.RequestRefreshAsync(provider.Store.GetRefreshTarget(Url), TimeSpan.FromMilliseconds(600));
        var pending = Assert.Single(provider.Store.PendingRefreshes(Origin, "chrome"));
        clock.AdvanceTo(pending.Deadline);
        var ack = NativeCookieProtocol.Handle(provider.Store, Origin, "chrome", Snapshot(profile, 2, pending.RequestId));
        Assert.True(ack.Ok); // Receipt alone is not timely refresh confirmation.
        Assert.Equal("timeout", (await outcome.WaitAsync(TimeSpan.FromSeconds(5))).Status);
        Assert.Equal(2, Assert.Single(provider.Store.GetProfiles()).SnapshotVersion);
        Assert.Empty(provider.Store.PendingRefreshes(Origin, "chrome"));
    }

    [Fact]
    public async Task LogicalAttemptDoesNotResetRetryBudgetAtNextStageOrMutateActiveJar()
    {
        var provider = await Provider(); var profile = Guid.NewGuid().ToString("D");
        provider.Store.Apply(Origin, Snapshot(profile, 1)); provider.Store.BridgeHeartbeat(Origin, "chrome", Guid.NewGuid().ToString("D"));
        using var active = provider.Store.CreateJar(Url)!; var bytes = File.ReadAllText(active.Path);
        using var logical = provider.BeginSession(Url);
        var bridge = Respond(provider.Store, "refreshed"); var calls = 0;
        Assert.Equal(7, await provider.ExecuteAsync(Url, () => ++calls == 1 ? Task.FromException<int>(AuthFailure()) : Task.FromResult(7), CancellationToken.None));
        await bridge;
        var second = await Assert.ThrowsAsync<CookieRefreshException>(() => provider.ExecuteAsync(Url, () => Task.FromException<int>(AuthFailure()), CancellationToken.None));
        Assert.Equal("second-auth-failure", second.Status); Assert.Equal(2, calls);
        Assert.Equal(bytes, File.ReadAllText(active.Path));
        Assert.Empty(provider.Store.PendingRefreshes(Origin, "chrome"));
    }

    [Fact]
    public async Task MissingConsentAndPublicRequestsNeverDependOnBrowserPresence()
    {
        var provider = await Provider();
        Assert.Equal(1, await provider.ExecuteAsync(Url, () => Task.FromResult(1), CancellationToken.None));
        Assert.Equal("no-matching-profile", (await provider.Store.RequestRefreshAsync(null, TimeSpan.FromSeconds(1))).Status);
        var snapshot = Snapshot(Guid.NewGuid().ToString("D"), 1); snapshot.RefreshBeforeUse = false;
        provider.Store.Apply(Origin, snapshot);
        var error = await Assert.ThrowsAsync<ExternalProcessException>(() => provider.ExecuteAsync(Url, () => Task.FromException<int>(AuthFailure()), CancellationToken.None));
        Assert.IsNotType<CookieRefreshException>(error);
        Assert.Empty(provider.Store.PendingRefreshes(Origin, "chrome"));
    }

    [Fact]
    public async Task LiveBoundaryFinalizesFailedPartBeforeFreshJarRetry()
    {
        var provider = await Provider();
        provider.Store.Apply(Origin, Snapshot(Guid.NewGuid().ToString("D"), 1));
        provider.Store.BridgeHeartbeat(Origin, "chrome", Guid.NewGuid().ToString("D"));
        var bridge = Respond(provider.Store, "refreshed");
        var events = new List<string>();
        var part = 0;
        async Task<int> CapturePart()
        {
            var current = ++part;
            try
            {
                return await provider.ExecuteAsync(Url, () =>
                {
                    using var invocation = provider.Prepare(Url);
                    var fresh = File.ReadAllText(invocation.CookiePath!).Contains("mtd_refresh_fresh", StringComparison.Ordinal);
                    events.Add($"part-{current}:v{(fresh ? 2 : 1)}");
                    return fresh ? Task.FromResult(9) : Task.FromException<int>(AuthFailure());
                }, CancellationToken.None);
            }
            finally { events.Add($"finalized-{current}"); }
        }
        Assert.Equal(9, await provider.ExecuteAsync(Url, CapturePart, CancellationToken.None, retryAtAttemptBoundary: true));
        await bridge;
        Assert.Equal(["part-1:v1", "finalized-1", "part-2:v2", "finalized-2"], events);
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(root, "Auth", "tmp")));
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    [Trait("Category", "NativeHost")]
    public async Task NativeWatchFramesCorrelateRefreshAndRemovePresenceOnClose(bool refreshed)
    {
        var provider = await Provider();
        var profile = Guid.NewGuid().ToString("D");
        provider.Store.Apply(Origin, Snapshot(profile, 1));
        var incoming = new Pipe(); var outgoing = new Pipe();
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var host = NativeRefreshConnection.RunAsync(provider.Store, Origin, "chrome", incoming.Reader.AsStream(),
            outgoing.Writer.AsStream(), () => true, lifetime.Token);
        // Pump establishes presence synchronously before its first delay.
        var result = provider.Store.RequestRefreshAsync(provider.Store.GetRefreshTarget(Url), TimeSpan.FromSeconds(5), lifetime.Token);
        var frame = await NativeCookieProtocol.ReadFrameAsync(outgoing.Reader.AsStream(), lifetime.Token);
        Assert.NotNull(frame);
        using var json = JsonDocument.Parse(frame);
        var message = json.RootElement;
        Assert.Equal("refresh", message.GetProperty("type").GetString());
        Assert.Equal(profile, message.GetProperty("profileId").GetString());
        Assert.Equal("chrome", message.GetProperty("browser").GetString());
        Assert.Equal(1, message.GetProperty("oldVersion").GetInt64());
        Assert.False(message.TryGetProperty("cookies", out _));
        var nonce = message.GetProperty("requestId").GetString()!;
        if (refreshed)
        {
            var ack = NativeCookieProtocol.Handle(provider.Store, Origin, "chrome", Snapshot(profile, 2, nonce));
            Assert.True(ack.Ok); Assert.Equal(2, ack.SnapshotVersion);
        }
        else
        {
            await NativeCookieProtocol.WriteFrameAsync(incoming.Writer.AsStream(), new CookieMessage
            { Type = "refreshResult", Browser = "chrome", RequestId = nonce, RefreshStatus = "permission-revoked" }, lifetime.Token);
            using var ack = JsonDocument.Parse((await NativeCookieProtocol.ReadFrameAsync(outgoing.Reader.AsStream(), lifetime.Token))!);
            Assert.True(ack.RootElement.GetProperty("ok").GetBoolean());
        }
        var outcome = await result;
        Assert.Equal(refreshed ? "refreshed" : "permission-revoked", outcome.Status);
        Assert.Equal(refreshed ? 2 : 1, provider.Store.GetProfiles().Single().SnapshotVersion);
        await incoming.Writer.CompleteAsync();
        await host;
        Assert.Equal("unavailable", (await provider.Store.RequestRefreshAsync(provider.Store.GetRefreshTarget(Url), TimeSpan.FromSeconds(1))).Status);
        await outgoing.Writer.CompleteAsync();
        await incoming.Reader.CompleteAsync(); await outgoing.Reader.CompleteAsync();
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task RealChildProcessAuthFailureRefreshAckAndRetryUseFreshAttempt(bool secondFailure)
    {
        var paths = AppPaths.Create(root); paths.EnsureCreated(); var settings = new SettingsService(paths, new NullAppLogger()); await settings.LoadAsync();
        settings.Current.UseCustomYtDlp = settings.Current.UseCustomFfmpeg = settings.Current.UseCustomDeno = true;
        settings.Current.CustomYtDlpPath = settings.Current.CustomFfmpegPath = settings.Current.CustomFfprobePath = CoreTests.FakeToolPath();
        var directory = Path.Combine(root, "deno"); Directory.CreateDirectory(directory);
        foreach (var file in Directory.EnumerateFiles(AppContext.BaseDirectory, "ModernTubeDownloader.FakeTool*")) File.Copy(file, Path.Combine(directory, Path.GetFileName(file)));
        settings.Current.CustomDenoPath = Path.Combine(directory, "deno.exe"); File.Copy(CoreTests.FakeToolPath(), settings.Current.CustomDenoPath);
        settings.Current.CookieSource = AuthenticationSource.SyncedBrowserProfile; settings.Current.QueuePaused = true; await settings.SaveAsync();
        using var services = AppServices.Create(paths);
        services.Authentication.RefreshTimeout = TimeSpan.FromSeconds(5);
        services.Authentication.Store.Apply(Origin, Snapshot(Guid.NewGuid().ToString("D"), 1));
        services.Authentication.Store.BridgeHeartbeat(Origin, "chrome", Guid.NewGuid().ToString("D"));
        var bridge = Respond(services.Authentication.Store, secondFailure ? "second-auth-failure" : "refreshed");
        try
        {
            if (secondFailure) Assert.Equal("second-auth-failure", (await Assert.ThrowsAsync<CookieRefreshException>(() => services.Metadata.AnalyzeAsync(Url + "-second-failure"))).Status);
            else Assert.Equal("auth-refresh", (await services.Metadata.AnalyzeAsync(Url)).Id);
            await bridge;
            Assert.Empty(Directory.EnumerateFiles(Path.Combine(paths.AuthenticationDirectory, "tmp")));
        }
        finally { await services.ShutdownAsync(); }
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
