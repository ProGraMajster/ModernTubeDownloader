using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace ModernTubeDownloader.Authentication;

public static class AuthenticationFiles
{
    public static string DefaultRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ModernTubeDownloader", "Auth");
    public static void EnsurePrivateDirectory(string path)
    {
        var sid = WindowsIdentity.GetCurrent().User ?? throw new CookieSecurityException("user-identity");
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        var directory = new DirectoryInfo(path);
        if (!directory.Exists) directory.Create(security); else directory.SetAccessControl(security);
    }
    public static void WriteEncryptedAtomic(string path, byte[] bytes)
    {
        var staging = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(staging, bytes); File.Move(staging, path, true); }
        finally { if (File.Exists(staging)) File.Delete(staging); }
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, nint security,
        uint creation, uint flags, nint template);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle handle, StringBuilder path, uint size, uint flags);

    public static bool IsUnredirectedDirectory(string path)
    {
        using var handle = CreateFile(Path.GetFullPath(path), 0, 7, 0, 3, 0x02000000, 0);
        if (handle.IsInvalid) throw new CookieSecurityException("bridge-directory");
        if (!IsUnredirectedHandle(path, handle)) return false;
        // An overlay can expose the real directory but redirect newly created files.
        // A zero-byte, delete-on-close probe checks the actual manifest write route.
        var probePath = Path.Combine(Path.GetFullPath(path), ".bridge-route-" + Guid.NewGuid().ToString("N") + ".tmp");
        using var probe = new FileStream(probePath, FileMode.CreateNew, FileAccess.Write,
            FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.DeleteOnClose);
        return IsUnredirectedHandle(probePath, probe.SafeFileHandle);
    }
    private static bool IsUnredirectedHandle(string path, SafeFileHandle handle)
    {
        var physical = new StringBuilder(4096);
        var size = GetFinalPathNameByHandle(handle, physical, (uint)physical.Capacity, 0);
        if (size is 0 or >= 4096) throw new CookieSecurityException("bridge-directory");
        return SameDirectoryPath(path, physical.ToString());
    }
    internal static bool SameDirectoryPath(string logical, string physical)
    {
        if (physical.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) physical = @"\\" + physical[8..];
        else if (physical.StartsWith(@"\\?\", StringComparison.Ordinal)) physical = physical[4..];
        return Path.GetFullPath(logical).TrimEnd(Path.DirectorySeparatorChar)
            .Equals(Path.GetFullPath(physical).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
    }
}

// The entire index is encrypted, including tombstones. No plaintext snapshot or backup.
public sealed class CookieStore(string root)
{
    private sealed record Entry(string Origin, CookieMessage Snapshot, bool Revoked, int Excluded);
    private sealed record StoreDocument(int Version, Entry[] Entries, bool SyncBlocked = false,
        CookieRefreshRequest[]? Requests = null, CookieBridgePresence[]? Bridges = null);
    private readonly string fullRoot = Path.GetFullPath(root);
    private readonly string file = Path.Combine(Path.GetFullPath(root), "profiles.dpapi");
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("ModernTubeDownloader.Auth.v1");
    private T Locked<T>(Func<T> action)
    {
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fullRoot.ToUpperInvariant())));
        using var gate = new Mutex(false, "Local\\MTD.Auth." + id);
        var entered = false;
        try
        {
            try { entered = gate.WaitOne(TimeSpan.FromSeconds(10)); } catch (AbandonedMutexException) { entered = true; }
            if (!entered) throw new CookieSecurityException("store-busy");
            AuthenticationFiles.EnsurePrivateDirectory(fullRoot);
            return action();
        }
        finally { if (entered) gate.ReleaseMutex(); }
    }
    private StoreDocument Read()
    {
        if (!File.Exists(file)) return new(1, []);
        byte[]? plaintext = null;
        try
        {
            if (new FileInfo(file).Length > 9 * 1024 * 1024) throw new CookieSecurityException("store-unavailable");
            plaintext = ProtectedData.Unprotect(File.ReadAllBytes(file), Entropy, DataProtectionScope.CurrentUser);
            var data = JsonSerializer.Deserialize<StoreDocument>(plaintext, CookieProtocol.Json);
            if (data is null || data.Version != 1 || data.Entries.Length > CookieProtocol.MaximumProfiles ||
                data.Requests?.Length > CookieProtocol.MaximumProfiles || data.Bridges?.Length > 8) throw new CookieSecurityException("store-version");
            return data;
        }
        catch (Exception error) when (error is CryptographicException or JsonException or IOException or UnauthorizedAccessException)
        { throw new CookieSecurityException("store-unavailable"); }
        finally { if (plaintext is not null) CryptographicOperations.ZeroMemory(plaintext); }
    }
    private void Write(StoreDocument document)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document, CookieProtocol.Json);
        try { AuthenticationFiles.WriteEncryptedAtomic(file, ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser)); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    public CookieAcknowledgement Apply(string origin, CookieMessage message) => Locked(() =>
    {
        CookieProtocol.Validate(message);
        var document = Read();
        if (message.Type == "status") return new CookieAcknowledgement(true, "connected");
        if (message.Type == "refreshResult")
        {
            var requests = (document.Requests ?? []).Select(r => r.RequestId == message.RequestId && r.Origin == origin &&
                r.Browser == message.Browser && r.Status == "pending" ? r with { Status = message.RefreshStatus! } : r).ToArray();
            Write(document with { Requests = requests });
            return new CookieAcknowledgement(true, "refresh-result");
        }
        if (message.Type == "watch") throw new CookieSecurityException("watch-lifecycle");
        if (message.Type == "cookieSnapshot" && document.SyncBlocked) throw new CookieSecurityException("store-revoked");
        var old = document.Entries.FirstOrDefault(e => e.Origin == origin && e.Snapshot.ProfileId == message.ProfileId);
        if (old is not null && message.SnapshotVersion <= old.Snapshot.SnapshotVersion) throw new CookieSecurityException("stale-snapshot");
        var entries = document.Entries.Where(e => e != old).ToList();
        if (old is null && entries.Count >= CookieProtocol.MaximumProfiles) throw new CookieSecurityException("profile-limit");
        var excluded = message.Cookies.Count(c => c.PartitionKey is not null);
        var revoked = message.Type is "delete" or "revoke";
        message.Cookies = revoked ? [] : message.Cookies.Where(c => c.PartitionKey is null &&
            (c.Session || c.ExpirationDate > DateTimeOffset.UtcNow.ToUnixTimeSeconds())).ToArray();
        entries.Add(new(origin, message, revoked, excluded));
        var pending = (document.Requests ?? []).Select(r => r.Origin == origin && r.ProfileId == message.ProfileId && r.Status == "pending" &&
            r.Deadline > DateTimeOffset.UtcNow && (revoked || r.RequestId == message.RequestId)
            ? r with { Status = revoked ? "disabled" : message.RefreshBeforeUse && message.SnapshotVersion > r.OldVersion ? "refreshed" : "rejected",
                NewVersion = revoked ? 0 : message.SnapshotVersion } : r).ToArray();
        Write(document with { Entries = entries.ToArray(), Requests = pending });
        return new CookieAcknowledgement(true, revoked ? "revoked" : "synced", SnapshotVersion: message.SnapshotVersion,
            CookieCount: message.Cookies.Length, ExcludedPartitioned: excluded);
    });
    public IReadOnlyList<CookieProfileInfo> GetProfiles() => Locked(() => Read().Entries.Select(e => new CookieProfileInfo(
        e.Snapshot.ProfileId, e.Snapshot.Browser, e.Snapshot.ScopeMode, e.Snapshot.Scopes, e.Snapshot.GeneratedAtUtc,
        e.Snapshot.SnapshotVersion, e.Snapshot.Cookies.Length, e.Excluded, e.Revoked)).ToArray());
    public void DeleteAll() => Locked(() =>
    {
        StoreDocument document;
        // Explicit deletion is also the recovery path for an unreadable store. Ordinary
        // reads and sync still fail closed; only this user action replaces corrupt data.
        try { document = Read(); }
        catch (CookieSecurityException) { document = new(1, []); }
        Write(new(1, document.Entries.Select(e => e with { Revoked = true, Snapshot = new CookieMessage
        {
            ProfileId = e.Snapshot.ProfileId, Browser = e.Snapshot.Browser, Type = "revoke",
            SnapshotVersion = Math.Max(e.Snapshot.SnapshotVersion + 1, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
        } }).ToArray(), true));
        return true;
    });
    public void AllowSync() => Locked(() => { var document=Read(); Write(document with { SyncBlocked=false }); return true; });
    private static Entry? Choose(StoreDocument document, string sourceUrl, string? profileId = null, string? origin = null)
    {
        if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return null;
        return document.Entries.Where(e => !e.Revoked && (profileId is null || e.Snapshot.ProfileId == profileId) && (origin is null || e.Origin == origin) &&
            e.Snapshot.Scopes.Any(d => CookieProtocol.Matches(uri.IdnHost, d)))
            .OrderByDescending(e => e.Snapshot.Scopes.Where(d => CookieProtocol.Matches(uri.IdnHost, d)).Max(d => d.Length))
            .ThenByDescending(e => e.Snapshot.GeneratedAtUtc).ThenBy(e => e.Snapshot.ProfileId, StringComparer.Ordinal).FirstOrDefault();
    }
    public CookieRefreshTarget? GetRefreshTarget(string sourceUrl) => Locked(() =>
    {
        var chosen = Choose(Read(), sourceUrl);
        return chosen is null ? null : new CookieRefreshTarget(chosen.Origin, chosen.Snapshot.Browser,
            chosen.Snapshot.ProfileId, chosen.Snapshot.SnapshotVersion, chosen.Snapshot.RefreshBeforeUse);
    });
    public CookieJarLease? CreateJar(string sourceUrl, string? profileId = null, string? origin = null) => Locked(() =>
    {
        var chosen = Choose(Read(), sourceUrl, profileId, origin);
        if (chosen is null) return null;
        var uri = new Uri(sourceUrl);
        var cookies = chosen.Snapshot.Cookies.Where(c => CookieProtocol.Matches(uri.IdnHost, c.Domain, c.HostOnly) &&
            (c.Session || c.ExpirationDate > DateTimeOffset.UtcNow.ToUnixTimeSeconds())).ToArray();
        if (cookies.Length == 0) return null;
        return CookieJarLease.Create(Path.Combine(fullRoot, "tmp"), cookies, chosen.Snapshot.SnapshotVersion);
    });

    // Metadata-only rendezvous lives inside the existing ACL-protected DPAPI document.
    // Credentials still cross the browser boundary exclusively through Native Messaging.
    public void BridgeHeartbeat(string origin, string browser, string connectionId, bool connected = true) => Locked(() =>
    {
        var document = Read();
        var bridges = (document.Bridges ?? []).Where(b => b.Expires > DateTimeOffset.UtcNow &&
            !(b.Origin == origin && b.Browser == browser && (connected || b.ConnectionId == connectionId))).ToList();
        if (connected) bridges.Add(new(origin, browser, connectionId, DateTimeOffset.UtcNow.AddSeconds(15)));
        if (bridges.Count > 8) throw new CookieSecurityException("bridge-limit");
        Write(document with { Bridges = bridges.ToArray() }); return true;
    });
    public IReadOnlyList<CookieRefreshRequest> PendingRefreshes(string origin, string browser) => Locked(() =>
        (Read().Requests ?? []).Where(r => r.Origin == origin && r.Browser == browser && r.Status == "pending" && r.Deadline > DateTimeOffset.UtcNow).ToArray());

    public async Task<CookieRefreshResult> RequestRefreshAsync(CookieRefreshTarget? target, TimeSpan timeout, CancellationToken token = default)
    {
        if (target is null) return new("no-matching-profile");
        if (!target.Consented) return new("disabled");
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromSeconds(30)) throw new ArgumentOutOfRangeException(nameof(timeout));
        var request = new CookieRefreshRequest(Guid.NewGuid().ToString("D"), target.Origin, target.Browser, target.ProfileId,
            target.SnapshotVersion, DateTimeOffset.UtcNow + timeout);
        var initial = Locked(() =>
        {
            var document = Read();
            var profile = document.Entries.FirstOrDefault(e => e.Origin == target.Origin && e.Snapshot.ProfileId == target.ProfileId);
            if (document.SyncBlocked || profile is null || profile.Revoked || !profile.Snapshot.RefreshBeforeUse) return "disabled";
            if (!(document.Bridges ?? []).Any(b => b.Origin == target.Origin && b.Browser == target.Browser && b.Expires > DateTimeOffset.UtcNow)) return "unavailable";
            var requests = (document.Requests ?? []).Where(r => r.Deadline > DateTimeOffset.UtcNow).ToList();
            if (requests.Any(r => r.Origin == target.Origin && r.ProfileId == target.ProfileId && r.Status == "pending") || requests.Count >= CookieProtocol.MaximumProfiles) return "unavailable";
            requests.Add(request); Write(document with { Requests = requests.ToArray() }); return "pending";
        });
        if (initial != "pending") return new(initial);
        try
        {
            while (DateTimeOffset.UtcNow < request.Deadline)
            {
                token.ThrowIfCancellationRequested();
                var result = Locked(() =>
                {
                    var document = Read();
                    var current = (document.Requests ?? []).FirstOrDefault(r => r.RequestId == request.RequestId);
                    if (current is null) return new CookieRefreshResult("disabled");
                    if (current.Status != "pending") return new CookieRefreshResult(current.Status, current.NewVersion);
                    return !(document.Bridges ?? []).Any(b => b.Origin == target.Origin && b.Browser == target.Browser && b.Expires > DateTimeOffset.UtcNow)
                        ? new CookieRefreshResult("unavailable") : null;
                });
                if (result is not null) return result.Refreshed && result.SnapshotVersion <= target.SnapshotVersion ? new("rejected") : result;
                await Task.Delay(TimeSpan.FromMilliseconds(100), token).ConfigureAwait(false);
            }
            return new("timeout");
        }
        finally
        {
            Locked(() => { var document = Read(); Write(document with { Requests = (document.Requests ?? []).Where(r => r.RequestId != request.RequestId).ToArray() }); return true; });
        }
    }
}

public sealed class CookieJarLease : IDisposable
{
    private FileStream? ownership;
    public string Path { get; }
    public long SnapshotVersion { get; }
    private CookieJarLease(string path, long version, FileStream owner) { Path = path; SnapshotVersion = version; ownership = owner; }
    internal static CookieJarLease Create(string root, IEnumerable<BrowserCookie> cookies, long version)
    {
        AuthenticationFiles.EnsurePrivateDirectory(root);
        var path = System.IO.Path.Combine(root, Guid.NewGuid().ToString("N") + ".txt");
        var owner = new FileStream(path + ".lock", FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        try
        {
            var text = new StringBuilder("# Netscape HTTP Cookie File\n");
            foreach (var cookie in cookies)
            {
                var domain = cookie.HostOnly ? cookie.Domain.TrimStart('.') : "." + cookie.Domain.TrimStart('.');
                text.Append(cookie.HttpOnly ? "#HttpOnly_" : "").Append(domain).Append('\t')
                    .Append(cookie.HostOnly ? "FALSE" : "TRUE").Append('\t').Append(cookie.Path).Append('\t')
                    .Append(cookie.Secure ? "TRUE" : "FALSE").Append('\t')
                    .Append(cookie.Session ? 0 : (long)cookie.ExpirationDate!.Value).Append('\t')
                    .Append(cookie.Name).Append('\t').Append(cookie.Value).Append('\n');
            }
            File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
            return new(path, version, owner);
        }
        catch { owner.Dispose(); File.Delete(path + ".lock"); if (File.Exists(path)) File.Delete(path); throw; }
    }
    public void Dispose()
    {
        if (ownership is null) return;
        try { File.Delete(Path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        ownership.Dispose(); ownership = null;
        try { File.Delete(Path + ".lock"); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
    public static void CleanupAbandoned(string root)
    {
        if (!Directory.Exists(root)) return;
        foreach (var path in Directory.EnumerateFiles(root, "*.txt"))
        {
            if (!Guid.TryParseExact(System.IO.Path.GetFileNameWithoutExtension(path), "N", out _)) continue;
            try
            {
                using (var claim = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)) File.Delete(path);
                File.Delete(path + ".lock");
            }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
