using System.Globalization;
using ModernFormsNext;
using ModernTubeDownloader.Theming;

namespace ModernTubeDownloader.Views;

internal sealed partial class SettingsView
{
    private readonly Label categoryEmpty = AppUi.Muted();

    private void FilterSettingsCategories()
    {
        var query = categorySearch.Text.Trim();
        foreach (var (category, button) in categoryButtons)
        {
            button.Visible = !dashboard || query.Length == 0 || Matches(button.Text) ||
                cardCategories.Where(pair => pair.Value == category).Any(pair => Captions(pair.Key).Any(Matches));
        }
        bool Matches(string caption) => text.Culture.CompareInfo.IndexOf(caption, query,
            CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;
    }

    // Index option captions only. Never inspect user-entered paths, cookies, tokens or passwords.
    private static IEnumerable<string> Captions(Control root)
    {
        foreach (var child in root.Controls)
        {
            if (child is Label or CheckBox) yield return child.Text;
            foreach (var caption in Captions(child)) yield return caption;
        }
    }
}
