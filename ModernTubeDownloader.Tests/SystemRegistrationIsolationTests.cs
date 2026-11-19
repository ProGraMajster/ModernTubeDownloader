using ModernTubeDownloader.Authentication;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Services;

namespace ModernTubeDownloader.Tests;

public sealed class SystemRegistrationIsolationTests
{
    private const string Temp = @"C:\MtdQaFixture\Temp";
    private const string Profile = Temp + @"\mtd-automation-smoke-fixture";

    private static bool IsQa(bool requested) => IsolatedQaProfile.IsEligible(Profile, requested ? "1" : null, Temp, _ => FileAttributes.Directory);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CurrentUserProtocolRegistrationStopsBeforeItsWriteOperation(bool isolatedQa)
    {
        var writes = 0;
        void Register() => ExternalProtocolRegistration.RegisterCurrentExecutable(() => writes++, () => IsQa(isolatedQa));
        if (isolatedQa) Assert.Throws<InvalidOperationException>(Register);
        else Register();
        Assert.Equal(isolatedQa ? 0 : 1, writes);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CurrentUserProtocolRemovalStopsBeforeDeleteAndPreservesNormalResult(bool isolatedQa, bool deleted)
    {
        var attempts = 0;
        bool Remove() => ExternalProtocolRegistration.UnregisterCurrentUser(() => { attempts++; return deleted; }, () => IsQa(isolatedQa));
        if (isolatedQa) Assert.Throws<InvalidOperationException>(() => Remove());
        else Assert.Equal(deleted, Remove());
        Assert.Equal(isolatedQa ? 0 : 1, attempts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CurrentUserStartupWritesAndDeletesCannotEscapeQa(bool isolatedQa)
    {
        const string original = "synthetic pre-existing startup command";
        string? stored = original;
        var writes = 0;
        var deletes = 0;
        void Write() => CurrentUserStartupRegistration.Write("new command", value => { stored = value; writes++; }, () => IsQa(isolatedQa));
        void Delete() => CurrentUserStartupRegistration.Delete(() => { stored = null; deletes++; }, () => IsQa(isolatedQa));
        if (isolatedQa)
        {
            Assert.Throws<InvalidOperationException>(Write);
            Assert.Throws<InvalidOperationException>(Delete);
            Assert.Equal(original, stored);
        }
        else
        {
            Write(); Assert.Equal("new command", stored);
            Delete(); Assert.Null(stored);
        }
        Assert.Equal(isolatedQa ? 0 : 1, writes);
        Assert.Equal(isolatedQa ? 0 : 1, deletes);
    }

    [Fact]
    public void NormalRegistrationFailuresAreNotTurnedIntoSuccess()
    {
        var failure = new IOException("synthetic unavailable storage");
        Assert.Same(failure, Assert.Throws<IOException>(() => ExternalProtocolRegistration.RegisterCurrentExecutable(() => throw failure, () => false)));
        Assert.Same(failure, Assert.Throws<IOException>(() => ExternalProtocolRegistration.UnregisterCurrentUser(() => throw failure, () => false)));
        Assert.Same(failure, Assert.Throws<IOException>(() => CurrentUserStartupRegistration.Write("command", _ => throw failure, () => false)));
        Assert.Same(failure, Assert.Throws<IOException>(() => CurrentUserStartupRegistration.Delete(() => throw failure, () => false)));
    }

    [Theory]
    [InlineData(Profile + @"\Auth")]
    [InlineData(@"C:\MtdQaFixture\OtherAuth")]
    [InlineData(@"relative\Auth")]
    public void NativeBridgeRejectsNoncanonicalProfilesBeforeAnyWrite(string root)
    {
        var writes = 0;
        var error = Assert.Throws<CookieSecurityException>(() => NativeBridgeRegistration.RegisterFromCanonicalProfile(root, () => writes++));
        Assert.Equal("bridge-profile", error.Message);
        Assert.Equal(0, writes);
    }

    [Fact]
    public void NativeBridgeDefaultProfileKeepsNormalRegistrationPath()
    {
        // Reading the default path is pure; the injected operation touches neither
        // real Auth data nor HKCU. No production registration is used in this suite.
        var calls = 0;
        NativeBridgeRegistration.RegisterFromCanonicalProfile(AuthenticationFiles.DefaultRoot, () => calls++);
        NativeBridgeRegistration.RegisterFromCanonicalProfile(AuthenticationFiles.DefaultRoot + Path.DirectorySeparatorChar, () => calls++);
        Assert.Equal(2, calls);
        var failure = new CookieSecurityException("synthetic-registration-failure");
        Assert.Same(failure, Assert.Throws<CookieSecurityException>(() =>
            NativeBridgeRegistration.RegisterFromCanonicalProfile(AuthenticationFiles.DefaultRoot, () => throw failure)));
    }
}
