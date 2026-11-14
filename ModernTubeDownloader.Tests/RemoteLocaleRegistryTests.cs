using ModernTubeDownloader.Localization;
using ModernTubeDownloader.WebRemote;

namespace ModernTubeDownloader.Tests;

public class RemoteLocaleRegistryTests
{
    [Fact]
    public void RemoteCatalogUsesExactlyTheDesktopRegistryAndContainsOnlyPublicResourceStrings()
    {
        var catalog = RemoteLocalizationCatalog.Current.Value;
        Assert.Equal(LocaleRegistry.Supported.Select(locale => locale.Code), catalog.Locales.Select(locale => locale.Code));
        var text = new LocalizationService("en");
        var keys = catalog.Resources["en"].Keys.Order().ToArray();
        Assert.Contains("footerHttp", keys); Assert.Contains("followApp", keys);
        foreach (var locale in catalog.Locales)
        {
            Assert.Equal(keys, catalog.Resources[locale.Code].Keys.Order());
            foreach (var (key, value) in catalog.Resources[locale.Code])
                Assert.Equal(text.GetResources(locale.Code)["RemoteWeb." + key], value);
        }
    }
}
