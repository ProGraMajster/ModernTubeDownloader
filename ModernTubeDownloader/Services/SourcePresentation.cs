using ModernTubeDownloader.Localization;
using ModernTubeDownloader.Models;

namespace ModernTubeDownloader.Services;

public static class SourcePresentation
{
    public static string Summary(VideoMetadata metadata, LocalizationService text)
    {
        var facts = ExtractorIdentityService.Facts(metadata);
        return $"{text["Sources.Source"]}: {Value(facts.Identity.DisplayName, text)} • {text["Sources.Type"]}: {text[$"Sources.Type.{facts.Kind}"]}";
    }
    public static string Details(VideoMetadata metadata, LocalizationService text)
    {
        var facts = ExtractorIdentityService.Facts(metadata);
        string Row(string key, object? value) => $"{text[key]}: {Value(value?.ToString(), text)}";
        string Yes(bool value) => text[value ? "Sources.Yes" : "Sources.No"];
        return string.Join(Environment.NewLine,
        [
            Row("Sources.Source", facts.Identity.DisplayName), Row("Details.Extractor", facts.Identity.RuntimeName),
            Row("Sources.ExtractorKey", facts.Identity.ExtractorKey), Row("Sources.Type", text[$"Sources.Type.{facts.Kind}"]),
            Row("Details.Availability", facts.Availability), Row("Details.LiveStatus", facts.LiveStatus),
            Row("Details.OriginalUrl", SafeOriginalUrl(metadata.OriginalUrl ?? metadata.WebpageUrl)), Row("Details.VideoId", metadata.Id),
            Row("Sources.SubtitlesAvailable", Yes(facts.Subtitles > 0)), Row("Sources.AutomaticAvailable", Yes(facts.AutomaticSubtitles > 0)),
            Row("Sources.ChaptersAvailable", Yes(facts.Chapters > 0)), Row("Details.FormatCount", facts.Formats),
            Row("Sources.PlaylistContext", Yes(facts.PlaylistContext)),
            metadata.Formats.Count > 0 && metadata.Formats.All(format =>
                string.IsNullOrWhiteSpace(format.VideoCodec) && string.IsNullOrWhiteSpace(format.AudioCodec))
                ? text["Sources.CodecsUnknown"] : string.Empty,
            facts.Identity.IsGeneric ? text["Sources.GenericRecognized"] : string.Empty,
            VerificationText(facts.Identity.RuntimeName, text)
        ]);
    }
    public static string VerificationText(string runtimeName, LocalizationService text)
    {
        var verified = VerifiedSourcesRegistry.Get(runtimeName);
        return verified.Count == 0 ? text["Sources.YtDlpHint"] : text["Sources.VerificationScoped"] + Environment.NewLine +
            string.Join(Environment.NewLine, verified.Select(item => $"• {text[$"Sources.Capability.{item.Capability}"]} — {text[item.Scope]} ({item.Evidence})"));
    }
    public static string Status(SupportedSource source, LocalizationService text) =>
        text[$"Sources.Status.{source.VerificationStatus}"] + (source.IsBroken ? " • " + text["Sources.Broken"] : "");
    public static string? SafeOriginalUrl(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return null;
        // Source details never expose credentials, fragments or signed/token query values.
        var safe = new UriBuilder(uri) { UserName = "", Password = "", Query = "", Fragment = "" };
        return safe.Uri.AbsoluteUri + (uri.Query.Length > 0 ? "?…" : string.Empty);
    }
    private static string Value(string? value, LocalizationService text) =>
        string.IsNullOrWhiteSpace(value) ? text["Common.NotAvailable"] : value;
}
