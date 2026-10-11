using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ModernTubeDownloader.Authentication;

public enum AuthenticationSource { None, ManualFile, SyncedBrowserProfile }

// Never use these credential-bearing DTOs in UI, logging or exception messages.
public sealed class BrowserCookie
{
    public string Domain { get; set; } = "";
    public string Path { get; set; } = "/";
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
    public bool Secure { get; set; }
    public bool HttpOnly { get; set; }
    public bool Session { get; set; }
    public double? ExpirationDate { get; set; }
    public bool HostOnly { get; set; }
    public string SameSite { get; set; } = "unspecified";
    public string StoreId { get; set; } = "0";
    public JsonElement? PartitionKey { get; set; }
}

public sealed class CookieMessage
{
    [JsonRequired] public int ProtocolVersion { get; set; } = 1;
    public string Type { get; set; } = "";
    public string ProfileId { get; set; } = "";
    public string Browser { get; set; } = "";
    public string ScopeMode { get; set; } = "domains";
    public long SnapshotVersion { get; set; }
    public DateTimeOffset GeneratedAtUtc { get; set; }
    public string[] Scopes { get; set; } = [];
    public BrowserCookie[] Cookies { get; set; } = [];
    public bool RefreshBeforeUse { get; set; }
    public string? RequestId { get; set; }
    public string? RefreshStatus { get; set; }
}

public sealed record CookieProfileInfo(string ProfileId, string Browser, string ScopeMode, string[] Domains,
    DateTimeOffset LastSyncUtc, long SnapshotVersion, int CookieCount, int ExcludedPartitioned, bool Revoked);
public sealed record CookieAcknowledgement(bool Ok, string Status, int ProtocolVersion = 1,
    long SnapshotVersion = 0, int CookieCount = 0, int ExcludedPartitioned = 0);
public sealed record CookieRefreshTarget(string Origin, string Browser, string ProfileId, long SnapshotVersion, bool Consented);
public sealed record CookieRefreshRequest(string RequestId, string Origin, string Browser, string ProfileId,
    long OldVersion, DateTimeOffset Deadline, string Status = "pending", long NewVersion = 0);
public sealed record CookieBridgePresence(string Origin, string Browser, string ConnectionId, DateTimeOffset Expires);
public sealed record CookieRefreshResult(string Status, long SnapshotVersion = 0)
{
    public bool Refreshed => Status == "refreshed";
}
public sealed class CookieSecurityException(string code) : Exception(code);

public static partial class CookieProtocol
{
    public const int MaximumMessageBytes = 512 * 1024;
    public const int MaximumCookies = 1000;
    public const int MaximumProfiles = 16;
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 12, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    [GeneratedRegex("^[a-p]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex ExtensionIdPattern();
    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?(?:\\.[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?)+$", RegexOptions.CultureInvariant)]
    private static partial Regex DomainPattern();

    public static bool IsExtensionId(string? id) => id is not null && ExtensionIdPattern().IsMatch(id);
    public static bool IsDomain(string? domain) => domain is not null && domain.Length <= 253 && DomainPattern().IsMatch(domain)
        && !System.Net.IPAddress.TryParse(domain, out _) && domain is not ("co.uk" or "com.au" or "co.jp" or "com.br" or "co.in");
    public static bool Matches(string host, string domain, bool hostOnly = false) =>
        host.Equals(domain.TrimStart('.'), StringComparison.OrdinalIgnoreCase) || !hostOnly &&
        host.EndsWith("." + domain.TrimStart('.'), StringComparison.OrdinalIgnoreCase);

    public static void Validate(CookieMessage message)
    {
        if (message.Scopes is null || message.Cookies is null) throw new CookieSecurityException("message-fields");
        if (message.ProtocolVersion != 1) throw new CookieSecurityException("protocol-version");
        if (message.Type is not ("cookieSnapshot" or "revoke" or "delete" or "status" or "watch" or "refreshResult")) throw new CookieSecurityException("message-type");
        if (message.Browser is not ("chrome" or "edge" or "brave" or "chromium")) throw new CookieSecurityException("browser");
        if (message.Type is "status" or "watch") { if (message.Cookies.Length != 0) throw new CookieSecurityException("unexpected-cookies"); return; }
        if (message.RequestId is not null && !Guid.TryParseExact(message.RequestId, "D", out _)) throw new CookieSecurityException("request-id");
        if (message.Type == "refreshResult")
        {
            if (message.Cookies.Length != 0 || message.RequestId is null || message.RefreshStatus is not
                ("permission-revoked" or "disabled" or "no-matching-profile" or "rejected" or "unavailable"))
                throw new CookieSecurityException("refresh-result");
            return;
        }
        if (!Guid.TryParseExact(message.ProfileId, "D", out _)) throw new CookieSecurityException("profile-id");
        if (message.SnapshotVersion <= 0) throw new CookieSecurityException("snapshot-version");
        if (message.Type != "cookieSnapshot") { if (message.Cookies.Length != 0) throw new CookieSecurityException("unexpected-cookies"); return; }
        if (message.ScopeMode is not ("current" or "services" or "domains" or "custom")) throw new CookieSecurityException("scope-mode");
        if (message.GeneratedAtUtc == default || (DateTimeOffset.UtcNow - message.GeneratedAtUtc).Duration() > TimeSpan.FromMinutes(10))
            throw new CookieSecurityException("snapshot-time");
        if (message.Scopes.Length is < 1 or > 16 || message.Scopes.Distinct(StringComparer.Ordinal).Count() != message.Scopes.Length || message.Scopes.Any(d => !IsDomain(d)))
            throw new CookieSecurityException("scope");
        if (message.Cookies.Length > MaximumCookies) throw new CookieSecurityException("cookie-count");
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var cookie in message.Cookies)
        {
            if (cookie is null || cookie.Domain is null || !IsDomain(cookie.Domain.TrimStart('.')) || !message.Scopes.Any(s => Matches(cookie.Domain.TrimStart('.'), s)))
                throw new CookieSecurityException("cookie-scope");
            if (cookie.Domain.StartsWith("..",StringComparison.Ordinal) || cookie.HostOnly && cookie.Domain.StartsWith('.')) throw new CookieSecurityException("host-only");
            if (!Safe(cookie.Name, 256, false) || !Safe(cookie.Value, 8192, true) || !Safe(cookie.Path, 2048, false) || !cookie.Path.StartsWith('/') ||
                cookie.StoreId != "0" || cookie.SameSite is not ("unspecified" or "no_restriction" or "lax" or "strict"))
                throw new CookieSecurityException("cookie-fields");
            if (!cookie.Session && (cookie.ExpirationDate is not { } expiry || !double.IsFinite(expiry) || expiry < 0 || expiry > 253402300799))
                throw new CookieSecurityException("cookie-expiry");
            // Netscape has neither cookie-store nor partition identity: never silently flatten them.
            if (cookie.PartitionKey is not null) continue;
            if (!identities.Add(cookie.Domain + "\t" + cookie.Path + "\t" + cookie.Name)) throw new CookieSecurityException("duplicate-cookie");
        }
    }
    private static bool Safe(string? value, int limit, bool empty) => value is not null && value.Length <= limit &&
        (empty || value.Length > 0) && !value.Any(c => c < ' ' || c == '\u007f');
}
