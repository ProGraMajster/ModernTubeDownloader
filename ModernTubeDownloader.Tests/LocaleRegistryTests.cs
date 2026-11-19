using System.Text;
using ModernFormsNext;
using ModernFormsNext.Testing;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Localization;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Theming;
using ModernTubeDownloader.Views;
using Path = System.IO.Path;

namespace ModernTubeDownloader.Tests;

public sealed class LocaleResourceTests
{
    [Fact]
    public void EveryPublishedLocaleIsCompleteAndFormatsWithItsDeclaredCulture()
    {
        var service = new LocalizationService("en"); var baseline = service.GetResources("en");
        Assert.Equal(LocaleRegistry.Supported.Count, LocaleRegistry.Supported.Select(locale => locale.Code).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        foreach (var locale in LocaleRegistry.Supported)
        {
            var resources = service.GetResources(locale.Code);
            Assert.Empty(LocalizationResourceValidator.Validate(resources, baseline));
            Assert.Contains("url,quality,container,start,end", resources["Batch.Examples.Help"], StringComparison.Ordinal);
            Assert.Contains("urls/items", resources["Batch.Error.JsonFormat"], StringComparison.Ordinal);
            Assert.Contains("--classic-ui", resources["Dashboard.HelpBody"], StringComparison.Ordinal);
            service.SetLanguage(locale.Code); Assert.Equal(locale.CultureName, service.Culture.Name);
            foreach (var (key, value) in resources)
            {
                var format = CompositeFormat.Parse(value);
                var arguments = Enumerable.Repeat<object>(1234.5m, format.MinimumArgumentCount).ToArray();
                Assert.Equal(string.Format(locale.Culture, value, arguments), service.Get(key, arguments));
            }
        }
    }

    [Theory]
    [InlineData("PL-pl", "pl")]
    [InlineData("EN-us", "en")]
    [InlineData("zz", "en")]
    public void LegacyAndRegionalAliasesRemainStable(string input, string expected)
        => Assert.Equal(expected, LocaleRegistry.Normalize(input));

    [Fact]
    public void DuplicateJsonKeysCannotSilentlyReplaceAnEarlierTranslation()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("{\"Nav.Downloads\":\"One\",\"Nav.Downloads\":\"Two\"}"));
        Assert.Throws<InvalidDataException>(() => LocalizationResourceValidator.Read(stream, "synthetic"));
    }

    [Fact]
    public void ValidatorReportsMissingUnknownEmptyUnicodeAndIncorrectFormatIndices()
    {
        var original = new Dictionary<string, string> { ["count"] = "{0} / {1}", ["missing"] = "Required", ["braces"] = "{{literal}} {0}" };
        var invalid = new Dictionary<string, string> { ["count"] = "{0} / {2}", ["unknown"] = "", ["unicode"] = "\uFFFD", ["broken"] = "{", ["braces"] = "{{literal}} {1}", ["encoded"] = "Import &amp; download", ["tokenized"] = "\u2581Import" };
        var issues = LocalizationResourceValidator.Validate(invalid, original);
        Assert.Contains(new("missing-key", "missing"), issues);
        Assert.Contains(new("unknown-key", "unknown"), issues);
        Assert.Contains(new("empty", "unknown"), issues);
        Assert.Contains(new("unicode", "unicode"), issues);
        Assert.Contains(new("format", "broken"), issues);
        Assert.Contains(new("placeholders", "count"), issues);
        Assert.Contains(new("placeholders", "braces"), issues);
        Assert.Contains(new("encoding-artifact", "encoded"), issues);
        Assert.Contains(new("encoding-artifact", "tokenized"), issues);
    }

    [Fact]
    public void SearchFindsNativeNameAndRegionalCodeWithoutChangingTheSelection()
    {
        var text = new LocalizationService("pl");
        Assert.Contains(LocaleRegistry.Search("Polski", text), locale => locale.Code == "pl");
        Assert.Contains(LocaleRegistry.Search("en-US", text), locale => locale.Code == "en");
        Assert.Empty(LocaleRegistry.Search("no-such-language", text)); Assert.Equal("pl", text.Language);
    }

    [Fact]
    public void SingleLineActionsRejectParagraphsAndUnboundedLabels()
    {
        var issues = LocalizationResourceValidator.Validate(new Dictionary<string, string>
        {
            ["Common.Close"] = "Close\nthis window",
            ["Nav.Downloads"] = new string('x', 257),
            ["Organizer.ConfirmMove"] = "First paragraph.\nSecond paragraph."
        });
        Assert.Contains(new("control-text", "Common.Close"), issues);
        Assert.Contains(new("control-text", "Nav.Downloads"), issues);
        Assert.DoesNotContain(issues, issue => issue.Key == "Organizer.ConfirmMove");
    }

    [Fact]
    public async Task EveryPublishedLocaleSurvivesSettingsPersistence()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mtd-locale-persistence-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = AppPaths.Create(directory); paths.EnsureCreated(); var settings = new SettingsService(paths, new NullAppLogger());
            await settings.LoadAsync();
            foreach (var locale in LocaleRegistry.Supported)
            {
                settings.Current.Language = locale.Code; await settings.SaveAsync();
                var reloaded = new SettingsService(paths, new NullAppLogger()); await reloaded.LoadAsync();
                Assert.Equal(locale.Code, reloaded.Current.Language);
            }
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}

[Collection("ModernFormsNext TestHost")]
public sealed class LocalePickerPresentationTests
{
    [Theory]
    [InlineData(100)]
    [InlineData(150)]
    [InlineData(200)]
    public void NativePickerIsBoundedSearchableAndReturnsTheChosenLocale(int scale)
    {
        using var host = ModernFormsTestHost.Create();
        var text = new LocalizationService("pl");
        try
        {
            AppTypography.SetScale(scale);
            using var picker = new LocalePickerForm(text, "pl"); host.Show(picker, 720, 760); host.ProcessPendingWork();
            var descendants = picker.Controls.SelectMany(control => Descendants(control).Prepend(control)).ToArray();
            var search = Assert.Single(descendants.OfType<TextBox>(), control => control.AccessibleAutomationId == "LocaleSearch");
            var rows = descendants.OfType<Button>().Where(control => control.AccessibleAutomationId?.StartsWith("LocaleChoice-", StringComparison.Ordinal) == true).ToArray();
            Assert.Equal(LocaleRegistry.Supported.Count, rows.Length);
            Assert.All(rows.Where(row => row.Visible), row => Assert.True(row.Left >= 0 && row.Right <= row.Parent!.ClientSize.Width));
            search.Text = "en-US"; host.ProcessPendingWork();
            var choice = Assert.Single(rows, row => row.Visible); Assert.Equal("LocaleChoice-en", choice.AccessibleAutomationId);
            choice.PerformClick(); Assert.Equal("en", picker.Selection); Assert.Equal(DialogResult.OK, picker.DialogResult);
        }
        finally { AppTypography.SetScale(100); }
    }

    private static IEnumerable<Control> Descendants(Control root)
    { foreach (var child in root.Controls) { yield return child; foreach (var nested in Descendants(child)) yield return nested; } }
}
