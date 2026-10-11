using System.Security.Cryptography;
using System.Text.Json;
using ModernTubeDownloader.Authentication;

// Binary stdin/stdout only. No UI, app single-instance path, logger or provisioning.
var stage="parent";
try
{
    var root = AuthenticationFiles.DefaultRoot;
    var origin = args.Length > 0 ? args[0] : "";
    var browser = NativeBrowserAuthority.GetBrowser();
    stage="origin";
    if (!NativeBridgeRegistration.IsAllowed(root, origin, browser)) return 2;
    stage="frame";
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
    var input = await NativeCookieProtocol.ReadFrameAsync(Console.OpenStandardInput(), timeout.Token);
    if (input is null) return 0;
    stage="request";
    CookieAcknowledgement response;
    try
    {
        var message = JsonSerializer.Deserialize<CookieMessage>(input, CookieProtocol.Json) ?? throw new CookieSecurityException("message");
        CookieProtocol.Validate(message);
        if (message.Type == "watch")
        {
            if (message.Browser != browser) throw new CookieSecurityException("browser-mismatch");
            CryptographicOperations.ZeroMemory(input);
            // Only a registered, browser-owned connectNative port enters this metadata-only lifecycle.
            await NativeRefreshConnection.RunAsync(new CookieStore(root), origin, browser, Console.OpenStandardInput(),
                Console.OpenStandardOutput(), () => NativeBridgeRegistration.IsAllowed(root, origin, browser));
            return 0;
        }
        response = NativeCookieProtocol.Handle(new CookieStore(root), origin, browser, message);
    }
    catch (CookieSecurityException error) { response = new(false, error.Message); }
    catch { response = new(false, "rejected"); }
    finally { CryptographicOperations.ZeroMemory(input); }
    await NativeCookieProtocol.WriteFrameAsync(Console.OpenStandardOutput(), response, timeout.Token);
    SaveHealth(response.Ok?"ack":"request-rejected","none");
    return response.Ok ? 0 : 1;
}
catch (Exception error) { SaveHealth(stage,error.GetType().Name);return 2; } // No exception messages, payloads or stderr.

void SaveHealth(string result,string errorType)
{
    try {
        var root=AuthenticationFiles.DefaultRoot;
        // Only an already configured public origin can produce this bounded non-secret diagnostic.
        if(args.Length==0||!NativeBridgeRegistration.ReadBindings(root).Any(b=>b.Origin==args[0]))return;
        AuthenticationFiles.WriteEncryptedAtomic(Path.Combine(root,"bridge-health.json"),
            JsonSerializer.SerializeToUtf8Bytes(new {result,errorType,utc=DateTimeOffset.UtcNow},CookieProtocol.Json));
    }catch { } // Diagnostics must not affect framing or store ownership.
}
