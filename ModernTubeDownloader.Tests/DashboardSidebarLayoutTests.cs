using ModernFormsNext;
using ModernFormsNext.Accessibility;
using ModernFormsNext.Testing;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Localization;
using ModernTubeDownloader.Shell;
using ModernTubeDownloader.Theming;

namespace ModernTubeDownloader.Tests;

[Collection("ModernFormsNext TestHost")]
public sealed class DashboardSidebarLayoutTests
{
    [Theory]
    [InlineData("pl", 100)] [InlineData("pl", 150)] [InlineData("pl", 200)]
    [InlineData("en", 100)] [InlineData("en", 150)] [InlineData("en", 200)]
    [InlineData("de-DE", 100)] [InlineData("de-DE", 150)] [InlineData("de-DE", 200)]
    [InlineData("ja-JP", 100)] [InlineData("ja-JP", 150)] [InlineData("ja-JP", 200)]
    public void NavigationFitsTheRealViewportAndRetainsEveryActionAcrossScrollAndResize(string language, int scale)
    {
        using var host = ModernFormsTestHost.Create();
        var previousProfile = AppearancePresentation.Current;
        var previousScale = AppTypography.TextScale;
        try
        {
            var profile = AppearanceProfile.Preset("ModernDashboard");
            profile.Global.Animations = false;
            AppearancePresentation.Current = profile; AppTypography.SetScale(scale);
            var text = new LocalizationService(language);
            using var pages = new Panel();
            using var shell = new AppShellHost(pages, text, new NullAppLogger());
            var invoked = new List<AppPageId>();
            shell.Navigate += (_, page) => { invoked.Add(page); shell.Select(page); };
            shell.Apply(AppInterfaceMode.ModernDashboard);
            host.Show(shell, 1650, 927); host.ProcessPendingWork();
            var sidebar = Descendants(shell).OfType<Panel>().Single(control => control.AccessibleAutomationId == "NavigationSidebar");
            var buttons = sidebar.Controls.OfType<Button>().ToDictionary(button =>
                Enum.Parse<AppPageId>(button.AccessibleAutomationId![3..]));
            Assert.Equal(Enum.GetValues<AppPageId>().Order(), buttons.Keys.Order());

            foreach (var mode in new[] { DashboardSidebarMode.Auto, DashboardSidebarMode.Expanded, DashboardSidebarMode.Compact })
            {
                profile.ModernDashboardShell.Sidebar = mode; shell.RefreshChrome();
                foreach (var (width, height) in new[] { (1650, 927), (1100, 720), (900, 420), (1650, 927) })
                {
                    host.Resize(width, height); host.ProcessPendingWork();
                    var scroll = Assert.IsType<AccessibleScrollInfo>(sidebar.AccessibilityObject.ScrollInfo);
                    Assert.False(scroll.Horizontal.IsScrollable,
                        $"{language}/{scale}/{mode}/{width}x{height}: horizontal overflow {scroll.Horizontal}.");
                    Assert.True(sidebar.AutoScroll); // Keep the actual vertical scrolling contract.
                    if (height == 420) Assert.True(scroll.Vertical.IsScrollable);
                    var compact = mode == DashboardSidebarMode.Compact || mode == DashboardSidebarMode.Auto && (width < 1280 || scale > 125);
                    foreach (var (page, button) in buttons)
                    {
                        Assert.Same(button, sidebar.Controls.OfType<Button>().Single(candidate => candidate.AccessibleAutomationId == "Nav" + page));
                        Assert.True(button.Visible && button.Enabled && button.TabStop);
                        Assert.Equal(text[page == AppPageId.Batch ? "Dashboard.Batch" : "Nav." + page], button.AccessibleName);
                        Assert.Equal(compact ? "" : button.AccessibleName, button.Text);
                        Assert.True(button.Width >= 40);
                        if (!compact)
                            Assert.True(button.Width >= DashboardVisual.TextWidth(button, button.Text) + button.Padding.Horizontal + 2,
                                $"{language}/{scale}/{mode}: {button.AccessibleAutomationId} clips its measured label or focus border.");
                        var icon = Assert.Single(button.Controls.OfType<AppVectorIcon>());
                        Assert.True(icon.Visible && icon.Left > 0 && icon.Top > 0 && icon.Right < button.Width && icon.Bottom < button.Height);
                        Assert.True(button.AccessibilityObject.PerformAction(AccessibleActions.ScrollIntoView));
                        host.ProcessPendingWork();
                        scroll = Assert.IsType<AccessibleScrollInfo>(sidebar.AccessibilityObject.ScrollInfo);
                        Assert.False(scroll.Horizontal.IsScrollable);
                        Assert.True(scroll.ViewportBounds.Contains(button.AccessibilityObject.Bounds),
                            $"{button.AccessibleAutomationId}: {button.AccessibilityObject.Bounds} is clipped by {scroll.ViewportBounds}.");
                        Assert.True(host.Input.Focus(button)); Assert.Same(button, host.FocusedControl);
                        Assert.Equal(1, button.StyleFocused.Border.GetWidth());
                        var calls = invoked.Count; host.Input.PressKey(Keys.Enter);
                        Assert.Equal(calls + 1, invoked.Count); Assert.Equal(page, invoked[^1]);
                    }
                    var offset = sidebar.VerticalScrollProperties.Value;
                    var selected = shell.CurrentPage;
                    shell.RefreshChrome(); host.ProcessPendingWork();
                    Assert.Equal(offset, sidebar.VerticalScrollProperties.Value);
                    Assert.Equal(selected, shell.CurrentPage);
                    Assert.False(Assert.IsType<AccessibleScrollInfo>(sidebar.AccessibilityObject.ScrollInfo).Horizontal.IsScrollable);
                    Assert.Equal(scale, AppTypography.TextScale);
                }
            }
            host.Dispatcher.ThrowUnhandledExceptions();
        }
        finally { AppearancePresentation.Current = previousProfile; AppTypography.SetScale(previousScale); }
    }

    private static IEnumerable<Control> Descendants(Control parent)
        => parent.Controls.SelectMany(child => new[] { child }.Concat(Descendants(child)));
}
