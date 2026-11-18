using ModernTubeDownloader.Services;

namespace ModernTubeDownloader.Tests;

public sealed class ReleaseNotesCatalogTests
{
    [Theory]
    [InlineData("pl", "pl", false)]
    [InlineData("en", "en", false)]
    [InlineData("de-DE", "en", true)]
    [InlineData("ja-JP", "en", true)]
    public void InstalledCandidateHasAllFourReviewedOfflineCategories(string requested, string expected, bool fallback)
    {
        var notes = Assert.IsType<InstalledReleaseNotes>(ReleaseNotesCatalog.ForInstalled("1.3.0-candidate.1+build", requested));
        Assert.Equal("1.3.0-candidate.1", notes.Version); Assert.True(notes.IsPrerelease);
        Assert.Equal(expected, notes.Language); Assert.Equal(fallback, notes.IsLanguageFallback);
        Assert.Equal(new[] { "new", "improved", "fixed", "limitations" }, notes.Sections.Select(section => section.Category));
        Assert.All(notes.Sections, section => { Assert.False(string.IsNullOrWhiteSpace(section.Title)); Assert.NotEmpty(section.Items); });
        Assert.Equal("https://github.com/ProGraMajster/ModernTubeDownloader/releases/tag/v1.3.0-candidate.1", notes.OfficialUrl.AbsoluteUri);
    }

    [Theory]
    [InlineData("1.3.0-candidate.2")]
    [InlineData("1.3.0")]
    [InlineData("1.2.1")]
    [InlineData("1.4.0")]
    public void AnotherTagOrAvailableUpdateCannotBorrowInstalledCandidateContent(string otherVersion)
    {
        Assert.False(ReleaseNotesCatalog.Supports(otherVersion));
        Assert.Null(ReleaseNotesCatalog.ForInstalled(otherVersion, "en"));
        Assert.Equal("https://github.com/ProGraMajster/ModernTubeDownloader/releases/tag/v" + otherVersion,
            ReleaseNotesCatalog.OfficialReleaseUrl(otherVersion)!.AbsoluteUri);
        Assert.Equal(new[] { "1.3.0-candidate.1" }, ReleaseNotesCatalog.AvailableVersions);
    }

    [Theory]
    [InlineData("https://evil.example/1.3.0")]
    [InlineData("1.3.0/../../other")]
    [InlineData("1.3.0?next=evil")]
    [InlineData("1.3.0#fragment")]
    [InlineData("1.3.0\n")]
    public void OnlyVersionBoundOfficialLinksCanBeConstructed(string version)
    {
        Assert.Null(ReleaseNotesCatalog.OfficialReleaseUrl(version));
        Assert.False(ReleaseNotesCatalog.Supports(version));
    }

    [Theory]
    [InlineData("pl", false)]
    [InlineData("en", false)]
    [InlineData("de-DE", true)]
    [InlineData("ja-JP", true)]
    public void HelpHasSixRealTopicsAndExplicitContentFallback(string language, bool fallback)
    {
        var help = HelpCatalog.ForLanguage(language);
        Assert.Equal(fallback, help.IsLanguageFallback);
        Assert.Equal(new[] { "start", "downloads", "live", "batch", "settings", "diagnostics" }, help.Topics.Select(topic => topic.Id));
        Assert.All(help.Topics, topic => { Assert.False(string.IsNullOrWhiteSpace(topic.Title)); Assert.False(string.IsNullOrWhiteSpace(topic.Body)); });
    }
}
