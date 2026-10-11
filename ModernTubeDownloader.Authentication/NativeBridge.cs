using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;

namespace ModernTubeDownloader.Authentication;

public sealed record BridgeBinding(string Browser, string Origin);
public static class NativeBridgeRegistration
{
    public const string HostName = "com.programajster.moderntubedownloader.cookies";
    private static string RegistryPath(string browser) => browser switch
    {
        "chrome" => @"Software\Google\Chrome\NativeMessagingHosts\" + HostName,
        "edge" => @"Software\Microsoft\Edge\NativeMessagingHosts\" + HostName,
        "brave" => @"Software\BraveSoftware\Brave-Browser\NativeMessagingHosts\" + HostName,
        "chromium" => @"Software\Chromium\NativeMessagingHosts\" + HostName,
        _ => throw new CookieSecurityException("browser")
    };
    public static IReadOnlyList<BridgeBinding> ReadBindings(string root)
    {
        var path = Path.Combine(root, "bridge.json");
        if (!File.Exists(path)) return [];
        if (new FileInfo(path).Length > 16 * 1024) throw new CookieSecurityException("bridge-config");
        return JsonSerializer.Deserialize<BridgeBinding[]>(File.ReadAllBytes(path), CookieProtocol.Json) ?? [];
    }
    // Call only after explicit desktop user confirmation. Public IDs are not credentials.
    public static void Register(string root, string browser, string extensionId, string executable)
    {
        if (!CookieProtocol.IsExtensionId(extensionId) || !File.Exists(executable) || !Path.IsPathFullyQualified(executable))
            throw new CookieSecurityException("bridge-input");
        var registry = RegistryPath(browser);
        AuthenticationFiles.EnsurePrivateDirectory(root);
        // A packaged parent can redirect AppData writes while Chrome launches an
        // unpackaged host. Never claim success for a manifest/store Chrome cannot see.
        if (!AuthenticationFiles.IsUnredirectedDirectory(root)) throw new CookieSecurityException("bridge-redirected");
        var origin = "chrome-extension://" + extensionId + "/";
        var bindings = ReadBindings(root).Where(b => b.Browser != browser || b.Origin != origin).Append(new(browser, origin)).ToArray();
        if (bindings.Length > 8) throw new CookieSecurityException("bridge-limit");
        AuthenticationFiles.WriteEncryptedAtomic(Path.Combine(root, "bridge.json"), JsonSerializer.SerializeToUtf8Bytes(bindings, CookieProtocol.Json));
        var manifest = Path.Combine(root, "native-" + browser + ".json");
        var content = new { name = HostName, description = "ModernTubeDownloader cookie synchronization", path = executable,
            type = "stdio", allowed_origins = bindings.Where(b => b.Browser == browser).Select(b => b.Origin).ToArray() };
        AuthenticationFiles.WriteEncryptedAtomic(manifest, JsonSerializer.SerializeToUtf8Bytes(content, CookieProtocol.Json));
        using var key = Registry.CurrentUser.CreateSubKey(registry);
        key.SetValue("", manifest, RegistryValueKind.String);
        new CookieStore(root).AllowSync(); // Explicit registration is renewed desktop consent after DeleteAll.
    }
    public static bool IsAllowed(string root, string origin, string browser) => ReadBindings(root).Any(b =>
        b.Origin == origin && b.Browser == browser && origin.Length == 52 && origin.StartsWith("chrome-extension://", StringComparison.Ordinal)
        && CookieProtocol.IsExtensionId(origin[19..51]) && origin.EndsWith('/'));
}

public static class NativeCookieProtocol
{
    public static CookieAcknowledgement Handle(CookieStore store, string origin, string browser, CookieMessage message)
    {
        if (message.Browser != browser) throw new CookieSecurityException("browser-mismatch");
        return store.Apply(origin, message);
    }
    public static async Task<byte[]?> ReadFrameAsync(Stream input, CancellationToken token = default)
    {
        var header = new byte[4];
        var count = await input.ReadAsync(header.AsMemory(0, 1), token);
        if (count == 0) return null;
        using var frameTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        frameTimeout.CancelAfter(TimeSpan.FromSeconds(20));
        await input.ReadExactlyAsync(header.AsMemory(1), frameTimeout.Token);
        var size = BinaryPrimitives.ReadUInt32LittleEndian(header);
        if (size is 0 or > CookieProtocol.MaximumMessageBytes) throw new CookieSecurityException("message-size");
        var body = new byte[(int)size];
        await input.ReadExactlyAsync(body, frameTimeout.Token);
        return body;
    }
    public static Task WriteFrameAsync(Stream output, CookieAcknowledgement response, CancellationToken token = default) =>
        WriteFrameAsync<CookieAcknowledgement>(output, response, token);
    public static async Task WriteFrameAsync<T>(Stream output, T response, CancellationToken token = default)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(response, CookieProtocol.Json);
        var header = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(header, (uint)body.Length);
        await output.WriteAsync(header, token); await output.WriteAsync(body, token); await output.FlushAsync(token);
    }
}

public static class NativeRefreshConnection
{
    public static async Task RunAsync(CookieStore store, string origin, string browser, Stream input, Stream output,
        Func<bool> stillAllowed, CancellationToken token = default)
    {
        var connectionId = Guid.NewGuid().ToString("D");
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        using var writes = new SemaphoreSlim(1);
        async Task Send<T>(T message)
        {
            await writes.WaitAsync(lifetime.Token).ConfigureAwait(false);
            try { await NativeCookieProtocol.WriteFrameAsync(output, message, lifetime.Token).ConfigureAwait(false); }
            finally { writes.Release(); }
        }
        async Task Pump()
        {
            var delivered = new HashSet<string>(StringComparer.Ordinal);
            var heartbeat = DateTimeOffset.MinValue;
            while (!lifetime.IsCancellationRequested)
            {
                if (DateTimeOffset.UtcNow >= heartbeat)
                {
                    if (!stillAllowed()) throw new CookieSecurityException("bridge-revoked");
                    store.BridgeHeartbeat(origin, browser, connectionId);
                    heartbeat = DateTimeOffset.UtcNow.AddSeconds(5);
                }
                var pending = store.PendingRefreshes(origin, browser);
                delivered.IntersectWith(pending.Select(r => r.RequestId));
                foreach (var request in pending)
                    if (delivered.Add(request.RequestId))
                        await Send(new { protocolVersion = 1, type = "refresh", request.RequestId, request.ProfileId,
                            browser, request.OldVersion, request.Deadline });
                await Task.Delay(250, lifetime.Token).ConfigureAwait(false);
            }
        }
        async Task Read()
        {
            while (await NativeCookieProtocol.ReadFrameAsync(input, lifetime.Token).ConfigureAwait(false) is { } bytes)
            {
                try
                {
                    var message = JsonSerializer.Deserialize<CookieMessage>(bytes, CookieProtocol.Json) ?? throw new CookieSecurityException("message");
                    if (message.Type != "refreshResult") throw new CookieSecurityException("watch-message-type");
                    await Send(NativeCookieProtocol.Handle(store, origin, browser, message));
                }
                finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes); }
            }
        }
        var pump = Pump();
        var reader = Read();
        try { await await Task.WhenAny(pump, reader).ConfigureAwait(false); }
        finally
        {
            lifetime.Cancel();
            try { await Task.WhenAll(pump, reader).ConfigureAwait(false); } catch (OperationCanceledException) { }
            store.BridgeHeartbeat(origin, browser, connectionId, connected: false);
        }
    }
}

public static class NativeBrowserAuthority
{
    [StructLayout(LayoutKind.Sequential)]
    private struct BasicInformation { public nint Reserved; public nint Peb; public nint Reserved2; public nint Reserved3; public nint Pid; public nint ParentPid; }
    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(nint handle, int kind, ref BasicInformation info, int size, out int length);
    private static Process Parent(Process process)
    {
        var data = new BasicInformation();
        if (NtQueryInformationProcess(process.Handle, 0, ref data, Marshal.SizeOf<BasicInformation>(), out _) != 0)
            throw new CookieSecurityException("browser-parent");
        return Process.GetProcessById((int)data.ParentPid);
    }
    public static string GetBrowser()
    {
        using var current = Process.GetCurrentProcess();
        using var parent = Parent(current);
        // Chromium versions may use cmd.exe as their native-process launcher, at most one hop.
        using var owner = parent.ProcessName.Equals("cmd", StringComparison.OrdinalIgnoreCase) ? Parent(parent) : Process.GetProcessById(parent.Id);
        var path = owner.MainModule?.FileName ?? throw new CookieSecurityException("browser-parent");
        var name = Path.GetFileName(path).ToLowerInvariant();
        var company = FileVersionInfo.GetVersionInfo(path).CompanyName ?? "";
        return name switch
        {
            "chrome.exe" when company.Contains("Google", StringComparison.OrdinalIgnoreCase) => "chrome",
            "msedge.exe" when company.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) => "edge",
            "brave.exe" when company.Contains("Brave", StringComparison.OrdinalIgnoreCase) => "brave",
            "chrome.exe" when path.Contains("Chromium", StringComparison.OrdinalIgnoreCase) => "chromium",
            _ => throw new CookieSecurityException("browser-parent")
        };
    }
}
