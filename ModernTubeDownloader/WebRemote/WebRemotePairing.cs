using System.Security.Cryptography;
using System.Text;

namespace ModernTubeDownloader.WebRemote;

/// <summary>Volatile, single-use pairing codes and bounded opaque sessions; no credentials are persisted.</summary>
public sealed class WebRemotePairing(TimeProvider? timeProvider = null)
{
    public const string Alphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";
    public const int CodeLength = 8;
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(8);
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private readonly object sync = new();
    private readonly Dictionary<string, DateTimeOffset> sessions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AttemptWindow> clients = new(StringComparer.Ordinal);
    private string code = "";
    private byte[]? codeHash;
    private DateTimeOffset expiresAt;
    private int attempts;
    private bool active;
    private PairingCodeState state = PairingCodeState.Inactive;

    public PairingCodeSnapshot Snapshot
    {
        get
        {
            lock (sync)
            {
                var current = state == PairingCodeState.Available && clock.GetUtcNow() >= expiresAt
                    ? PairingCodeState.Expired : state;
                return new(current == PairingCodeState.Available ? code.Insert(4, "-") : null, expiresAt, current);
            }
        }
    }

    public void Start()
    {
        lock (sync)
        {
            active = true;
            sessions.Clear();
            clients.Clear();
            RegenerateCore();
        }
    }

    /// <summary>Creates a new code without logging it or disconnecting already paired devices.</summary>
    public void Regenerate()
    {
        lock (sync) if (active) RegenerateCore();
    }

    /// <summary>Ensures one usable code on enable/save, never on a repaint or countdown tick.</summary>
    public void EnsureCode()
    {
        lock (sync)
            if (active && (codeHash is null || clock.GetUtcNow() >= expiresAt)) RegenerateCore();
    }

    public void Stop()
    {
        lock (sync)
        {
            active = false;
            state = PairingCodeState.Inactive;
            code = "";
            codeHash = null;
            sessions.Clear();
            clients.Clear();
        }
    }

    public PairingResult Pair(string? submitted, string client)
    {
        lock (sync)
        {
            var now = clock.GetUtcNow();
            if (!active) return new("pairing-inactive");
            foreach (var stale in clients.Where(pair => now - pair.Value.Start >= TimeSpan.FromMinutes(1)).Select(pair => pair.Key).ToArray())
                clients.Remove(stale);
            if (!clients.TryGetValue(client, out var window))
            {
                if (clients.Count >= 256) return new("pairing-rate-limit");
                clients[client] = window = new(now);
            }
            if (++window.Count > 5) return new("pairing-rate-limit");
            if (codeHash is null || now >= expiresAt) return new("pairing-expired");
            // Eight attempts total per code, across all clients. Regeneration does not reset
            // per-client windows, so neither client churn nor repeated new codes permit a brute force burst.
            if (++attempts > 8) { codeHash = null; code = ""; state = PairingCodeState.Blocked; return new("pairing-rate-limit"); }
            var normalized = (submitted ?? "").Replace("-", "").Replace(" ", "").ToUpperInvariant();
            var candidate = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
            if (normalized.Length != CodeLength || !CryptographicOperations.FixedTimeEquals(candidate, codeHash))
                return new("pairing-invalid");
            foreach (var stale in sessions.Where(pair => now >= pair.Value).Select(pair => pair.Key).ToArray()) sessions.Remove(stale);
            if (sessions.Count >= 64) return new("pairing-rate-limit");
            var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            sessions[Hash(secret)] = now + SessionLifetime;
            codeHash = null;
            state = PairingCodeState.Used;
            code = ""; // A successful exchange consumes the code. New device = explicit New code.
            return new(null, secret);
        }
    }

    public bool Authenticate(string? secret)
    {
        lock (sync)
        {
            if (!active || secret is not { Length: 64 } || !secret.All(Uri.IsHexDigit)) return false;
            var key = Hash(secret);
            if (!sessions.TryGetValue(key, out var expiry)) return false;
            if (clock.GetUtcNow() < expiry) return true;
            sessions.Remove(key);
            return false;
        }
    }

    public void Logout(string? secret)
    {
        lock (sync) if (secret is { Length: 64 }) sessions.Remove(Hash(secret));
    }

    private void RegenerateCore()
    {
        code = new string(Enumerable.Range(0, CodeLength).Select(_ => Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)]).ToArray());
        codeHash = SHA256.HashData(Encoding.UTF8.GetBytes(code));
        expiresAt = clock.GetUtcNow() + CodeLifetime;
        attempts = 0;
        state = PairingCodeState.Available;
    }

    private static string Hash(string secret) => Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(secret)));
    private sealed class AttemptWindow(DateTimeOffset start) { public DateTimeOffset Start { get; } = start; public int Count; }
}

public enum PairingCodeState { Inactive, Available, Used, Expired, Blocked }
public sealed record PairingCodeSnapshot(string? Code, DateTimeOffset ExpiresAt, PairingCodeState State);
public sealed record PairingResult(string? Error, string? Session = null);
