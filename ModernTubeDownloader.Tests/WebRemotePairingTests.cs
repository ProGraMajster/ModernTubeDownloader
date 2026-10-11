using ModernTubeDownloader.WebRemote;

namespace ModernTubeDownloader.Tests;

public sealed class WebRemotePairingTests
{
    [Fact]
    public void SnapshotSeparatesUsedExpiredBlockedAndInactiveAndSaveEnsuresOnlyOneCode()
    {
        var clock = new TestClock();
        var pairing = new WebRemotePairing(clock);
        Assert.Equal(PairingCodeState.Inactive, pairing.Snapshot.State);
        pairing.Start();
        Assert.Equal(PairingCodeState.Available, pairing.Snapshot.State);
        var code = pairing.Snapshot.Code;
        for (var i = 0; i < 20; i++) pairing.EnsureCode();
        Assert.Equal(code, pairing.Snapshot.Code);
        var session = pairing.Pair(code, "device").Session;
        Assert.Equal(PairingCodeState.Used, pairing.Snapshot.State);
        // Reading/redrawing never creates a code or revokes a session.
        for (var i = 0; i < 20; i++) Assert.Null(pairing.Snapshot.Code);
        Assert.True(pairing.Authenticate(session));
        pairing.EnsureCode();
        Assert.Equal(PairingCodeState.Available, pairing.Snapshot.State);
        Assert.True(pairing.Authenticate(session));
        clock.Advance(WebRemotePairing.CodeLifetime);
        Assert.Equal(PairingCodeState.Expired, pairing.Snapshot.State);
        pairing.EnsureCode();
        Assert.Equal(PairingCodeState.Available, pairing.Snapshot.State);
        for (var i = 0; i < 9; i++) pairing.Pair("wrong", "attempt-" + i);
        Assert.Equal(PairingCodeState.Blocked, pairing.Snapshot.State);
        pairing.Stop();
        Assert.Equal(PairingCodeState.Inactive, pairing.Snapshot.State);
    }

    [Fact]
    public void CodeIsRandomSingleUseAndSessionIsIndependent()
    {
        var clock = new TestClock();
        var pairing = new WebRemotePairing(clock);
        Assert.Null(pairing.Snapshot.Code);
        Assert.Equal("pairing-inactive", pairing.Pair("anything", "client").Error);
        pairing.Start();
        var code = pairing.Snapshot.Code!;
        Assert.Equal(9, code.Length);
        Assert.Equal('-', code[4]);
        Assert.All(code.Replace("-", ""), c => Assert.Contains(c, WebRemotePairing.Alphabet));
        var session = pairing.Pair(code.ToLowerInvariant(), "client").Session;
        Assert.NotNull(session);
        Assert.Equal(64, session.Length);
        Assert.NotEqual(code, session);
        Assert.True(pairing.Authenticate(session));
        Assert.Null(pairing.Snapshot.Code);
        Assert.Equal("pairing-expired", pairing.Pair(code, "second").Error);
        pairing.Regenerate();
        Assert.NotEqual(code, pairing.Snapshot.Code);
        Assert.True(pairing.Authenticate(session));
        clock.Advance(WebRemotePairing.SessionLifetime);
        Assert.False(pairing.Authenticate(session));
    }

    [Fact]
    public void ExpiryRegenerationLogoutAndStopRevokeAsSpecified()
    {
        var clock = new TestClock();
        var pairing = new WebRemotePairing(clock);
        pairing.Start();
        var original = pairing.Snapshot.Code!;
        clock.Advance(WebRemotePairing.CodeLifetime);
        Assert.Null(pairing.Snapshot.Code);
        Assert.Equal("pairing-expired", pairing.Pair(original, "client").Error);
        pairing.Regenerate();
        Assert.Equal("pairing-invalid", pairing.Pair(original, "client").Error);
        var session = pairing.Pair(pairing.Snapshot.Code, "client").Session;
        Assert.True(pairing.Authenticate(session));
        pairing.Logout(session);
        Assert.False(pairing.Authenticate(session));
        pairing.Regenerate();
        var nextSession = pairing.Pair(pairing.Snapshot.Code, "second").Session;
        pairing.Stop();
        Assert.False(pairing.Authenticate(nextSession));
        Assert.Null(pairing.Snapshot.Code);
        pairing.Start();
        Assert.False(pairing.Authenticate(nextSession));
    }

    [Fact]
    public void RateLimitSurvivesRegenerationAndCodeLimitsCoverClientChurn()
    {
        var clock = new TestClock();
        var pairing = new WebRemotePairing(clock);
        pairing.Start();
        for (var i = 0; i < 5; i++) Assert.Equal("pairing-invalid", pairing.Pair("wrong", "same-client").Error);
        pairing.Regenerate();
        Assert.Equal("pairing-rate-limit", pairing.Pair(pairing.Snapshot.Code, "same-client").Error);
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.NotNull(pairing.Pair(pairing.Snapshot.Code, "same-client").Session);
        pairing.Regenerate();
        for (var i = 0; i < 8; i++) Assert.Equal("pairing-invalid", pairing.Pair("wrong", "client-" + i).Error);
        Assert.Equal("pairing-rate-limit", pairing.Pair(pairing.Snapshot.Code, "new-client").Error);
        Assert.Null(pairing.Snapshot.Code);
    }

    [Fact]
    public void ConcurrentExchangesConsumeOnlyOneCode()
    {
        var pairing = new WebRemotePairing();
        pairing.Start();
        var code = pairing.Snapshot.Code!;
        var results = new System.Collections.Concurrent.ConcurrentBag<PairingResult>();
        Parallel.For(0, 20, i => results.Add(pairing.Pair(code, "client-" + i)));
        Assert.Single(results, result => result.Session is not null);
        Assert.False(pairing.Authenticate("short"));
        Assert.False(pairing.Authenticate(new string('x', 64)));
    }

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan value) => now += value;
    }
}
