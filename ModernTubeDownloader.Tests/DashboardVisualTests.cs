using ModernFormsNext;
using ModernFormsNext.Accessibility;
using ModernFormsNext.Testing;
using ModernTubeDownloader.Theming;

namespace ModernTubeDownloader.Tests;

[Collection("ModernFormsNext TestHost")]
public sealed class DashboardVisualTests
{
    [Theory]
    [InlineData(100)] [InlineData(150)] [InlineData(200)]
    public void FittingUnicodeTextHasIdenticalPixelsWithAndWithoutEllipsis(int scale)
    {
        using var host = ModernFormsTestHost.Create();
        AppTypography.SetScale(scale);
        try
        {
            var panel = new Panel(); AppTypography.SetDashboardPresentation(panel, true);
            var label = new DashboardLabel { Text = "Integration Sample — Łódź 日本語", Width = 1500, Height = 90, Font = new Font("Segoe UI", 20 * scale / 100f) };
            panel.Controls.Add(label); var window = host.Show(panel, 1520, 140);
            using var without = window.CaptureRenderedSnapshot();
            label.AutoEllipsis = true;
            using var with = window.CaptureRenderedSnapshot();
            Assert.True(without.CopyPixels().SequenceEqual(with.CopyPixels()), "Fitting Unicode must not acquire a premature ellipsis.");
            label.Text = new string('W', 400); label.Width = 180;
            using var truncated = window.CaptureRenderedSnapshot(); label.AutoEllipsis = false;
            using var clipped = window.CaptureRenderedSnapshot();
            Assert.False(truncated.CopyPixels().SequenceEqual(clipped.CopyPixels()), "Actual overflow must visibly expose the ellipsis.");
        }
        finally { AppTypography.SetScale(100); }
    }

    [Fact]
    public void DynamicallyAttachedTextScalesAndRestoresItsClassicStateRecipe()
    {
        using var host = ModernFormsTestHost.Create();
        var classic = new Panel(); var modern = new Panel(); AppTypography.SetDashboardPresentation(modern, true);
        var label = AppUi.Muted("Dynamic facts"); var original = label.Font.Size; var hoverSize = label.StyleHover.FontSize;
        modern.Controls.Add(label); Assert.True(label.Font.Size > original); Assert.Equal(label.Font.Size, label.StyleHover.GetFontSize());
        modern.Controls.Remove(label); classic.Controls.Add(label);
        Assert.Equal(original, label.Font.Size); Assert.Equal(hoverSize, label.StyleHover.FontSize);
        classic.Dispose(); modern.Dispose();
    }

    [Fact]
    public void ModernSwitchRetainsTheCanonicalToggleActionAndCheckedState()
    {
        using var host = ModernFormsTestHost.Create();
        var root = new Panel(); AppTypography.SetDashboardPresentation(root, true);
        var toggle = new DashboardToggle { Text = "Notifications", Width = 300, Height = 36 }; root.Controls.Add(toggle); host.Show(root, 400, 140);
        var provider = toggle.AccessibilityObject;
        Assert.Equal(AccessibleControlType.CheckBox, provider.ControlType);
        Assert.True(provider.PerformAction(AccessibleActions.Toggle)); Assert.True(toggle.Checked);
        Assert.True(provider.PerformAction(AccessibleActions.Toggle)); Assert.False(toggle.Checked);
    }

    [Fact]
    public void RemovingModernPresentationRestoresBorderRecipes()
    {
        var button = new Button(); var width = button.Style.Border.Width; var radius = button.Style.Border.Radius; var color = button.Style.Border.Color;
        DashboardVisual.Flat(button, true); DashboardVisual.RestoreTree(button);
        Assert.Equal(width, button.Style.Border.Width); Assert.Equal(radius, button.Style.Border.Radius); Assert.Equal(color, button.Style.Border.Color);
        button.Dispose();
    }
}
