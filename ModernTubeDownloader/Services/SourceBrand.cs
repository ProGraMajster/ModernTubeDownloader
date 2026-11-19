namespace ModernTubeDownloader.Services;

/// <summary>Presentation identity, never a supported-site or capability assertion.</summary>
internal enum SourceBrand { Other, YouTube, Twitch, Facebook, Vimeo, Kick, TikTok, Instagram, Twitter, Vk, Odysee }
internal enum SourceContentKind { Video, Playlist, Channel, Shorts }
internal readonly record struct SourceBrandIdentity(SourceBrand Brand, string Name, SourceContentKind Kind = SourceContentKind.Video);

internal static class SourceBrandLookup
{
    // Bounded immutable lookup; no network favicons, downloaded assets or per-URL cache.
    private static readonly (string Host, SourceBrand Brand, string Name)[] Hosts =
    [
        ("youtube.com", SourceBrand.YouTube, "YouTube"), ("youtu.be", SourceBrand.YouTube, "YouTube"),
        ("twitch.tv", SourceBrand.Twitch, "Twitch"), ("facebook.com", SourceBrand.Facebook, "Facebook"),
        ("fb.watch", SourceBrand.Facebook, "Facebook"), ("vimeo.com", SourceBrand.Vimeo, "Vimeo"),
        ("kick.com", SourceBrand.Kick, "Kick"), ("tiktok.com", SourceBrand.TikTok, "TikTok"),
        ("instagram.com", SourceBrand.Instagram, "Instagram"), ("twitter.com", SourceBrand.Twitter, "X / Twitter"),
        ("x.com", SourceBrand.Twitter, "X / Twitter"), ("vk.com", SourceBrand.Vk, "VK"), ("odysee.com", SourceBrand.Odysee, "Odysee")
    ];
    internal static SourceBrandIdentity Resolve(string? source)
    {
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return new(SourceBrand.Other, "—");
        // Use the stored canonical URL, never an untrusted extractor display label.
        // URI performs IDN conversion; compare ASCII labels including the DNS boundary.
        // A single terminal DNS root dot is equivalent, multiple empty labels are not.
        string host;
        try { host = uri.IdnHost.ToLowerInvariant(); }
        catch (UriFormatException) { return new(SourceBrand.Other, "—"); }
        if (host.EndsWith('.')) host = host[..^1];
        if (host.Length == 0 || host.Split('.').Any(label => label.Length == 0)) return new(SourceBrand.Other, "—");
        foreach (var item in Hosts)
        {
            if (!host.Equals(item.Host, StringComparison.Ordinal) && !host.EndsWith("." + item.Host, StringComparison.Ordinal)) continue;
            var kind = SourceContentKind.Video;
            if (item.Brand == SourceBrand.YouTube && (host == "youtube.com" || host.EndsWith(".youtube.com", StringComparison.Ordinal)))
            {
                if (uri.AbsolutePath.StartsWith("/shorts/", StringComparison.OrdinalIgnoreCase)) kind = SourceContentKind.Shorts;
                else if (uri.AbsolutePath.TrimEnd('/').Equals("/playlist", StringComparison.OrdinalIgnoreCase)) kind = SourceContentKind.Playlist;
                else if (uri.AbsolutePath.StartsWith("/@", StringComparison.Ordinal) || new[] { "/channel/", "/c/", "/user/" }.Any(p => uri.AbsolutePath.StartsWith(p, StringComparison.OrdinalIgnoreCase))) kind = SourceContentKind.Channel;
            }
            return new(item.Brand, item.Name, kind);
        }
        return new(SourceBrand.Other, host);
    }
}
