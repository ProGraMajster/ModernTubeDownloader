namespace ModernTubeDownloader.Services;

/// <summary>Presentation identity, never a supported-site or capability assertion.</summary>
internal enum SourceBrand { Other, YouTube, Playlist, Channel, Shorts, Twitch, Facebook, Vimeo, Kick, TikTok, Instagram, Twitter, Vk, Odysee }

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
    internal static (SourceBrand Brand, string Name) Resolve(string? source)
    {
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return (SourceBrand.Other, "—");
        foreach (var item in Hosts)
        {
            if (!uri.IdnHost.Equals(item.Host, StringComparison.OrdinalIgnoreCase) && !uri.IdnHost.EndsWith("." + item.Host, StringComparison.OrdinalIgnoreCase)) continue;
            var brand = item.Brand;
            if (brand == SourceBrand.YouTube)
            {
                if (uri.AbsolutePath.StartsWith("/shorts/", StringComparison.OrdinalIgnoreCase)) brand = SourceBrand.Shorts;
                else if (uri.AbsolutePath.Equals("/playlist", StringComparison.OrdinalIgnoreCase)) brand = SourceBrand.Playlist;
                else if (uri.AbsolutePath.StartsWith("/@", StringComparison.Ordinal) || new[] { "/channel/", "/c/", "/user/" }.Any(p => uri.AbsolutePath.StartsWith(p, StringComparison.OrdinalIgnoreCase))) brand = SourceBrand.Channel;
            }
            return (brand, item.Name);
        }
        return (SourceBrand.Other, uri.IdnHost);
    }
}
