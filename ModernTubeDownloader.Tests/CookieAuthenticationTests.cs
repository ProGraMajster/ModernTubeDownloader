using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using ModernTubeDownloader.Authentication;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Services;

namespace ModernTubeDownloader.Tests;

[Trait("Category","CookieSecurity")]
public sealed class CookieAuthenticationTests : IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"mtd-auth-tests",Guid.NewGuid().ToString("N"));
    private const string Origin="chrome-extension://aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/";
    private static CookieMessage Snapshot(long version=1,string? id=null) => new()
    {
        Type="cookieSnapshot",Browser="chrome",ProfileId=id??Guid.NewGuid().ToString("D"),SnapshotVersion=version,
        GeneratedAtUtc=DateTimeOffset.UtcNow,Scopes=["example.test"],
        Cookies=[new() { Domain=".example.test",Path="/",Name="synthetic_session",Value=Guid.NewGuid().ToString("N"),
            Session=true,Secure=true,HttpOnly=true }]
    };
    [Fact]
    public void SyntheticNativeToDpapiToNetscapeHasNoPlaintextMetadataAndCleansJar()
    {
        var store=new CookieStore(root);var message=Snapshot();var secret=message.Cookies[0].Value;
        var ack=NativeCookieProtocol.Handle(store,Origin,"chrome",message);
        Assert.True(ack.Ok); Assert.Equal(1,ack.CookieCount);
        Assert.DoesNotContain(secret,JsonSerializer.Serialize(store.GetProfiles(),CookieProtocol.Json));
        Assert.DoesNotContain(secret,Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(root,"profiles.dpapi"))));
        using(var jar=store.CreateJar("https://www.example.test/video")!) {
            Assert.NotNull(jar);var text=File.ReadAllText(jar.Path);
            Assert.StartsWith("# Netscape HTTP Cookie File\n",text);
            Assert.Contains("#HttpOnly_.example.test\tTRUE\t/\tTRUE\t0\tsynthetic_session\t",text);
            var rules=new FileInfo(jar.Path).GetAccessControl().GetAccessRules(true,true,typeof(SecurityIdentifier));
            Assert.All(rules.Cast<FileSystemAccessRule>(),r=>Assert.Equal(WindowsIdentity.GetCurrent().User,r.IdentityReference));
        }
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(root,"tmp")));
        Assert.Single(Directory.EnumerateFiles(root,"*",SearchOption.TopDirectoryOnly));
    }
    [Theory]
    [InlineData("evil-example.test",false)] [InlineData("example.test",true)] [InlineData("a.example.test",true)]
    public void MatchingUsesLabelBoundaries(string host,bool expected) => Assert.Equal(expected,CookieProtocol.Matches(host,"example.test"));
    [Fact]
    public void HostOnlyAndExpiredCookiesAndPartitionsAreNotFlattened()
    {
        var message=Snapshot();message.Cookies=[
            new() { Domain="example.test",Name="host",Value="synthetic",Session=true,HostOnly=true },
            new() { Domain=".example.test",Name="expired",Value="synthetic",ExpirationDate=1 },
            new() { Domain=".example.test",Name="partition",Value="synthetic",Session=true,PartitionKey=JsonSerializer.SerializeToElement(new {topLevelSite="https://other.test"}) }];
        var store=new CookieStore(root);var ack=store.Apply(Origin,message);
        Assert.Equal(1,ack.CookieCount);Assert.Equal(1,ack.ExcludedPartitioned);
        Assert.Null(store.CreateJar("https://www.example.test/video"));
        using var jar=store.CreateJar("https://example.test/video");Assert.NotNull(jar);
        Assert.Contains("example.test\tFALSE\t/\tFALSE\t0\thost\t",File.ReadAllText(jar.Path));
    }
    [Theory]
    [InlineData("value-crlf")] [InlineData("value-tab")] [InlineData("scope")] [InlineData("duplicate")]
    [InlineData("timestamp")] [InlineData("version")] [InlineData("protocol")] [InlineData("name-limit")]
    [InlineData("value-limit")] [InlineData("expiry")] [InlineData("null-fields")] [InlineData("count")]
    public void MalformedRecordsAreRejectedWithoutCredentialDiagnostics(string kind)
    {
        var m=Snapshot();var sentinel=m.Cookies[0].Value;
        switch(kind){
            case "value-crlf":m.Cookies[0].Value+="\r\n";break;case "value-tab":m.Cookies[0].Value+="\t";break;
            case "scope":m.Cookies[0].Domain="other.test";break;case "duplicate":m.Cookies=[m.Cookies[0],m.Cookies[0]];break;
            case "timestamp":m.GeneratedAtUtc=DateTimeOffset.UtcNow.AddDays(-1);break;case "version":m.SnapshotVersion=0;break;
            case "protocol":m.ProtocolVersion=2;break;case "name-limit":m.Cookies[0].Name=new string('a',257);break;
            case "value-limit":m.Cookies[0].Value=new string('a',8193);break;
            case "expiry":m.Cookies[0].Session=false;m.Cookies[0].ExpirationDate=double.NaN;break;
            case "null-fields":m.Scopes=null!;break;case "count":m.Cookies=Enumerable.Repeat(m.Cookies[0],1001).ToArray();break;
        }
        var exception=Assert.Throws<CookieSecurityException>(()=>CookieProtocol.Validate(m));
        Assert.DoesNotContain(sentinel,exception.ToString());
    }
    [Fact]
    public void RevocationRejectsStaleSnapshotAndDesktopDeletionRequiresRenewedConsent()
    {
        var store=new CookieStore(root);var message=Snapshot();store.Apply(Origin,message);
        using var active=store.CreateJar("https://example.test/video")!;
        store.Apply(Origin,new() { Type="revoke",Browser="chrome",ProfileId=message.ProfileId,SnapshotVersion=2 });
        Assert.Null(store.CreateJar("https://example.test/video")); Assert.True(File.Exists(active.Path));
        Assert.Throws<CookieSecurityException>(()=>store.Apply(Origin,message));
        store.DeleteAll(); Assert.Throws<CookieSecurityException>(()=>store.Apply(Origin,Snapshot(3)));
        store.AllowSync();store.Apply(Origin,Snapshot(3)); Assert.Single(store.GetProfiles(),p=>!p.Revoked);
    }
    [Fact]
    public void ActiveJarsSurviveCleanupAndNewBoundaryUsesLatestSnapshotWithoutOverwriting()
    {
        var store=new CookieStore(root);var message=Snapshot();store.Apply(Origin,message);
        using var active=store.CreateJar("https://example.test/video")!;var old=File.ReadAllText(active.Path);
        CookieJarLease.CleanupAbandoned(Path.Combine(root,"tmp"));Assert.True(File.Exists(active.Path));
        message.SnapshotVersion=2;message.Cookies[0].Value=Guid.NewGuid().ToString("N");store.Apply(Origin,message);
        using var fresh=store.CreateJar("https://example.test/video")!;
        Assert.Equal(old,File.ReadAllText(active.Path));Assert.Equal(2,fresh.SnapshotVersion);Assert.NotEqual(active.Path,fresh.Path);
    }
    [Fact]
    public async Task ConcurrentProfilesSerializeAndNeverMergeCookiesAcrossProfiles()
    {
        var messages=Enumerable.Range(0,8).Select(_=>Snapshot()).ToArray();
        await Task.WhenAll(messages.Select(m=>Task.Run(()=>new CookieStore(root).Apply(Origin,m))));
        var store=new CookieStore(root);Assert.Equal(8,store.GetProfiles().Count);
        using var jar=store.CreateJar("https://example.test/video")!;
        Assert.Equal(2,File.ReadAllLines(jar.Path).Length);
    }
    [Fact]
    public void CorruptEncryptedStoreFailsClosedWithoutOverwriting()
    {
        var store=new CookieStore(root);store.Apply(Origin,Snapshot());var file=Path.Combine(root,"profiles.dpapi");
        File.WriteAllBytes(file,[1,2,3]);Assert.Throws<CookieSecurityException>(()=>store.GetProfiles());Assert.Equal(3,new FileInfo(file).Length);
    }
    [Theory]
    [InlineData(@"C:\Example\Auth", @"\\?\C:\Example\Auth", true)]
    [InlineData(@"C:\Example\Auth\", @"\\?\c:\example\auth", true)]
    [InlineData(@"C:\Example\Auth", @"\\?\C:\Example\Packages\Test\LocalCache\Local\Auth", false)]
    [InlineData(@"C:\Example\Auth\manifest.json", @"\\?\C:\Example\Packages\Test\LocalCache\Local\Auth\manifest.json", false)]
    public void BridgeRegistrationCannotMistakeVirtualizedAppDataForBrowserVisibleDirectory(string logical, string physical, bool expected) =>
        Assert.Equal(expected, AuthenticationFiles.SameDirectoryPath(logical, physical));
    [Fact]
    public void PhysicalBridgeWriteProbeLeavesNoArtifactAndPreservesExistingFiles()
    {
        AuthenticationFiles.EnsurePrivateDirectory(root);
        var existing = Path.Combine(root, "existing.txt");
        File.WriteAllText(existing, "existing non-secret test data");
        _ = AuthenticationFiles.IsUnredirectedDirectory(root);
        Assert.Equal("existing non-secret test data", File.ReadAllText(existing));
        Assert.Equal(existing, Assert.Single(Directory.EnumerateFiles(root)));
    }
    [Fact]
    public void ExplicitDeletionClearsCorruptStoreAndBlocksBackgroundResurrection()
    {
        var store = new CookieStore(root); store.Apply(Origin, Snapshot());
        File.WriteAllBytes(Path.Combine(root, "profiles.dpapi"), [1, 2, 3]);
        store.DeleteAll();
        Assert.Empty(store.GetProfiles());
        Assert.Throws<CookieSecurityException>(() => store.Apply(Origin, Snapshot()));
        store.AllowSync();
        Assert.True(store.Apply(Origin, Snapshot()).Ok);
    }
    [Fact]
    public async Task LogicalSessionSharesOneJarAndProtectsOutputsLogsAndCrashDiagnostics()
    {
        var paths=AppPaths.Create(root);var settings=new SettingsService(paths,new NullAppLogger());await settings.LoadAsync();
        settings.Current.CookieSource=AuthenticationSource.SyncedBrowserProfile;
        var provider=new YtDlpAuthenticationProvider(paths,settings);var snapshot=Snapshot();var secret=snapshot.Cookies[0].Value;
        provider.Store.Apply(Origin,snapshot);
        string path;
        using(provider.BeginSession("https://example.test/video")) {
            using var metadata=provider.Prepare("https://example.test/video");path=metadata.CookiePath!;
            using var subtitles=provider.Prepare("https://example.test/video");Assert.Equal(path,subtitles.CookiePath);
            Assert.DoesNotContain(secret,metadata.Redact("diagnostic "+secret));
            var json=metadata.SanitizeOutput(JsonSerializer.Serialize(new {id="test",tags=new[]{secret},http_headers=new {Cookie=secret},formats=new[]{new{url="https://example.test/media",http_headers=new{Cookie=secret}}}}));
            Assert.DoesNotContain(secret,json);Assert.DoesNotContain("http_headers",json);
            FatalErrorReporter.Log(paths,null,"Authentication test",new InvalidOperationException(metadata.Redact(secret)));
            Assert.All(Directory.EnumerateFiles(paths.LogDirectory),file=>Assert.DoesNotContain(secret,File.ReadAllText(file)));
            Assert.All(Directory.EnumerateFiles(Path.Combine(root,"CrashReports")),file=>Assert.DoesNotContain(secret,File.ReadAllText(file)));
        }
        Assert.False(File.Exists(path));
    }
    [Fact]
    public async Task LegacyManualSourceIsMutuallyExclusiveWithSyncedSource()
    {
        var paths=AppPaths.Create(root);Directory.CreateDirectory(root);var manual=Path.Combine(root,"manual.txt");
        File.WriteAllText(manual,"# Netscape HTTP Cookie File\n");var settings=new SettingsService(paths,new NullAppLogger());await settings.LoadAsync();
        settings.Current.UseCookieFile=true;settings.Current.CookieFilePath=manual;
        var auth=new YtDlpAuthenticationProvider(paths,settings);using(var invocation=auth.Prepare("https://example.test/video"))Assert.Equal(manual,invocation.CookiePath);
        settings.Current.CookieSource=AuthenticationSource.SyncedBrowserProfile;await settings.SaveAsync();
        using(var invocation=auth.Prepare("https://example.test/video"))Assert.Null(invocation.CookiePath);
        Assert.False(settings.Current.UseCookieFile);Assert.True(File.Exists(manual));
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task RealChildProcessReceivesOneJarAndCredentialOutputIsSanitizedBeforeFailureOrMetadata(bool failure)
    {
        var paths=AppPaths.Create(root);paths.EnsureCreated();var settings=new SettingsService(paths,new NullAppLogger());await settings.LoadAsync();
        settings.Current.UseCustomYtDlp=settings.Current.UseCustomFfmpeg=settings.Current.UseCustomDeno=true;
        settings.Current.CustomYtDlpPath=settings.Current.CustomFfmpegPath=settings.Current.CustomFfprobePath=CoreTests.FakeToolPath();
        var directory=Path.Combine(root,"deno");Directory.CreateDirectory(directory);
        foreach(var file in Directory.EnumerateFiles(AppContext.BaseDirectory,"ModernTubeDownloader.FakeTool*"))File.Copy(file,Path.Combine(directory,Path.GetFileName(file)));
        settings.Current.CustomDenoPath=Path.Combine(directory,"deno.exe");File.Copy(CoreTests.FakeToolPath(),settings.Current.CustomDenoPath);
        settings.Current.CookieSource=AuthenticationSource.SyncedBrowserProfile;settings.Current.QueuePaused=true;await settings.SaveAsync();
        using var services=AppServices.Create(paths);var snapshot=Snapshot();var secret=snapshot.Cookies[0].Value;
        services.Authentication.Store.Apply(Origin,snapshot);
        try {
            if(failure){var error=await Assert.ThrowsAsync<ExternalProcessException>(()=>services.Metadata.AnalyzeAsync("https://example.test/auth-probe-error"));
                Assert.DoesNotContain(secret,error.ToString());Assert.DoesNotContain(secret,error.Result!.StandardErrorText);Assert.DoesNotContain(secret,error.Result.StandardOutputText);}
            else {var result=await services.Metadata.AnalyzeAsync("https://example.test/auth-probe");
                using var json=JsonDocument.Parse(result.RawJson!);Assert.Equal(1,json.RootElement.GetProperty("auth_argument_count").GetInt32());
                Assert.True(json.RootElement.GetProperty("jar_exists").GetBoolean());Assert.DoesNotContain(secret,result.RawJson!);}
            Assert.Empty(Directory.EnumerateFiles(Path.Combine(paths.AuthenticationDirectory,"tmp")));
        }finally{await services.ShutdownAsync();}
        services.Dispose();
        Assert.All(Directory.EnumerateFiles(paths.LogDirectory),file=>Assert.DoesNotContain(secret,File.ReadAllText(file)));
    }
    [Fact, Trait("Category","NativeHost")]
    public async Task NativeHostFramingIsBoundedAndRepliesContainOnlyAcknowledgementMetadata()
    {
        using var output=new MemoryStream();await NativeCookieProtocol.WriteFrameAsync(output,new(true,"synced",CookieCount:2));
        output.Position=0;var bytes=await NativeCookieProtocol.ReadFrameAsync(output);Assert.NotNull(bytes);
        Assert.DoesNotContain("value",Encoding.UTF8.GetString(bytes));
        var header=new byte[4];BinaryPrimitives.WriteUInt32LittleEndian(header,CookieProtocol.MaximumMessageBytes+1);
        await Assert.ThrowsAsync<CookieSecurityException>(()=>NativeCookieProtocol.ReadFrameAsync(new MemoryStream(header)));
        await Assert.ThrowsAsync<EndOfStreamException>(()=>NativeCookieProtocol.ReadFrameAsync(new MemoryStream([1,0])));
    }
    [Fact, Trait("Category","NativeHost")]
    public void WrongOriginAndBrowserAreRejected()
    {
        Assert.False(NativeBridgeRegistration.IsAllowed(root,Origin,"chrome"));
        Assert.Throws<CookieSecurityException>(()=>NativeCookieProtocol.Handle(new CookieStore(root),Origin,"edge",Snapshot()));
        Assert.False(CookieProtocol.IsExtensionId("*"));
    }
    [Theory]
    [InlineData("--cookies-fr chrome")] [InlineData("--cookies=secret.txt")] [InlineData("--no-cookies")]
    [InlineData("--alias c '--cookies file'")] [InlineData("--print-traffic")] [InlineData("--netrc-cmd echo")]
    [InlineData("--downloader arbitrary")] [InlineData("--ppa args")] [InlineData("--add-headers 'Cookie: synthetic'")]
    [InlineData("--add-header=Cookie:synthetic")] [InlineData("--add-h 'Authorization: synthetic'")]
    [InlineData("--add-headers='Proxy-Authorization: synthetic'")]
    [InlineData("--mark-watched")] [InlineData("--enable-file-urls")] [InlineData("--remote-components arbitrary")]
    public void ExpertArgumentsCannotBypassAuthOwnership(string input) =>
        Assert.Throws<ModernTubeDownloader.Utilities.CustomArgumentException>(()=>ModernTubeDownloader.Utilities.CommandLineArgumentTokenizer.ParseSafeYtDlpArguments(input));
    [Fact, Trait("Category","NativeHost")]
    public async Task NativeExecutableRejectsArbitraryShellParentWithoutStdoutOrMainForm()
    {
        var start=new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory,"ModernTubeDownloader.NativeHost.exe")) {
            UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true };
        start.ArgumentList.Add(Origin);
        using var process=Process.Start(start)!;await process.WaitForExitAsync();
        Assert.Equal(2,process.ExitCode);Assert.Empty(await process.StandardOutput.ReadToEndAsync());Assert.Empty(await process.StandardError.ReadToEndAsync());
    }
    public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}
}
