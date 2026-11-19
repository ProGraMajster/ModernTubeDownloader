using ModernFormsNext;
using ModernFormsNext.Testing;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Theming;
using Path = System.IO.Path;

namespace ModernTubeDownloader.Tests;

[Collection("ModernFormsNext TestHost")]
public sealed class TypographyTests
{
    [Theory]
    [InlineData(80)] [InlineData(100)] [InlineData(125)] [InlineData(150)] [InlineData(175)] [InlineData(200)]
    public void AllTokensAreAbsoluteAndDoNotAccumulate(int scale)
    {
        try
        {
            foreach (var token in Enum.GetValues<AppTextToken>())
            {
                AppTypography.SetScale(scale); var once = AppTypography.Font(token).SizeInPoints;
                AppTypography.SetScale(scale); Assert.Equal(once, AppTypography.Font(token).SizeInPoints);
                Assert.Equal(AppTypography.LogicalSize(token) * scale / 100f, once, 3);
            }
        }
        finally { AppTypography.SetScale(100); }
    }
    [Fact]
    public void LiveControlStyleFontsFollowScaleInEveryStateAndResetExactly()
    {
        using var host = ModernFormsTestHost.Create(); using var button = new Button(); AppUi.Secondary(button);
        try
        {
            foreach (var scale in new[] { 100, 150, 200, 150, 100 })
            {
                AppTypography.SetScale(scale);
                Assert.InRange(button.Font.SizeInPoints, 10.5f * scale / 100 - 1, 10.5f * scale / 100 + 1);
                foreach (var style in new[] { button.StyleHover, button.StyleFocused, button.StylePressed, button.StyleDisabled }) Assert.Equal(button.Font, style.TextFont);
            }
        }
        finally { AppTypography.SetScale(100); }
    }
    [Fact]
    public void ReflowExpandsLogicalRowsButNotImageSizeAndRestoresOriginalBounds()
    {
        using var host = ModernFormsTestHost.Create(); using var panel = new Panel();
        var label = AppUi.Muted("Long localized text"); label.SetBounds(12, 24, 400, 30);
        var image = new PictureBox(); image.SetBounds(12, 60, 160, 90); panel.Controls.AddRange([label, image]);
        try
        {
            AppTypography.SetScale(200); AppTypography.ReflowRows(panel); Assert.Equal(60, label.Height); Assert.Equal(160, image.Width); Assert.Equal(90, image.Height);
            AppTypography.UnflowRows(panel); Assert.Equal(24, label.Top); Assert.Equal(30, label.Height);
            AppTypography.SetScale(100); AppTypography.ReflowRows(panel); Assert.Equal(30, label.Height);
        }
        finally { AppTypography.SetScale(100); }
    }
    [Theory]
    [InlineData("{\"schemaVersion\":3}")]
    [InlineData("{\"typography\":{\"appTextScale\":201}}")]
    [InlineData("{\"script\":\"evil\"}")]
    [InlineData("{\"typography\":null}")]
    public void UnsafeOrUnsupportedProfilesAreRejected(string json) => Assert.ThrowsAny<Exception>(() => AppearanceProfileStore.Validate(json));
    [Fact]
    public async Task TextProfilePersistsSeparatelyAndCorruptFallbackPreservesEvidence()
    {
        var root = Path.Combine(Path.GetTempPath(), "mtd-typography-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var file = Path.Combine(root, "appearance.json"); var store = new AppearanceProfileStore(file);
            await store.SaveAsync(new() { Typography = new() { AppTextScale = 150 } }); Assert.Equal(150, (await store.LoadAsync(new NullAppLogger())).Typography.AppTextScale);
            await File.WriteAllTextAsync(file, "broken"); Assert.Equal(100, (await store.LoadAsync(new NullAppLogger())).Typography.AppTextScale); Assert.Equal("broken", await File.ReadAllTextAsync(file));
        }
        finally { Directory.Delete(root, true); }
    }
}
