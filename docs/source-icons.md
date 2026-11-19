# Downloads source identity

`SourceBrandLookup` is presentation only, not a supported-sites list. It uses the canonical `DownloadQueueItem.SourceUrl`; extractor labels never override a conflicting host. The existing admission path chooses metadata `webpage_url`, falling back to `original_url`, when creating that record.

Lookup accepts absolute HTTP(S) URLs, converts IDN hosts to ASCII, removes one terminal DNS root dot, and matches only exact domains or their dot-delimited subdomains. Unknown valid hosts retain their ASCII host caption; missing/invalid URLs use a neutral dash. No credentials, path or query enter the provider caption. A provider match is not evidence that downloading is supported, nor a guarantee about content on a subdomain.

YouTube, Twitch, Facebook, Kick, Vimeo, TikTok, Instagram, X/Twitter and VK have local vector marks. Odysee currently uses the neutral globe. Playlist, channel and Shorts are separate content contexts: their action chips may show context marks, while rows and inspectors retain the provider identity. Icons have no URL cache or external asset/network dependency.

The simplified vector paths in `SourceVectorIcon.cs` were authored in this repository; no third-party favicon, icon package or downloaded brand asset is embedded. Provider names and marks identify their respective services; they are not the application's branding or an endorsement. The official MTD application icon remains unchanged. This provenance note does not claim ownership of third-party trademarks.
