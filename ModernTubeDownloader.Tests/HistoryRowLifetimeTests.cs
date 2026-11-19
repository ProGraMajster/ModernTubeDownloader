using System.Security.Cryptography;
using System.Text;
using ModernFormsNext;
using ModernFormsNext.Testing;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Localization;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Views;
using SkiaSharp;
using Path = System.IO.Path;

namespace ModernTubeDownloader.Tests;

[Collection("ModernFormsNext TestHost")]
public sealed class HistoryRowLifetimeTests
{
    [Fact]
    public void ApplicationWindowDisposesNestedControlsOnceIncludingRepeatedBrandingAndDispose()
    {
        using var host = ModernFormsTestHost.Create();
        using var form = new Form();
        ModernTubeDownloader.Theming.AppBranding.Apply(form);
        ModernTubeDownloader.Theming.AppBranding.Apply(form);
        var panel = new Panel(); var picture = new PictureBox();
        var released = 0; var panelsReleased = 0;
        picture.Disposed += (_, _) => released++; panel.Disposed += (_, _) => panelsReleased++;
        panel.Controls.Add(picture); form.Controls.Add(panel);
        form.Dispose(); form.Dispose();
        Assert.Equal(1, panelsReleased);
        Assert.Equal(1, released);
    }

    [Fact]
    public void RebindingKeepsControlsAndRetargetsIdentityTextAndFileAvailability()
    {
        using var host = ModernFormsTestHost.Create();
        var root = Path.Combine(Path.GetTempPath(), "MTD.History.Row.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = AppPaths.Create(root); paths.EnsureCreated();
            using var cache = new ThumbnailCacheService(paths, new NullAppLogger(), () => false);
            var first = new DownloadHistoryEntry(Guid.NewGuid(), "First", "https://example.test/", "", "", "", Path.Combine(root, "missing"), DateTimeOffset.UtcNow, "Completed");
            using var card = new HistoryEntryCard(first, new LocalizationService("en"), cache);
            var original = card.Controls.ToArray();
            var present = Path.Combine(root, "present.txt"); File.WriteAllText(present, "SYNTHETIC");
            var second = first with { QueueItemId = Guid.NewGuid(), Title = "Second 日本語", FinalPath = present, LivePartIndex = 3 };
            card.UpdateEntry(second);
            Assert.Equal(original, card.Controls.ToArray());
            Assert.Contains(second.QueueItemId.ToString("N"), card.AccessibleAutomationId);
            Assert.Contains(card.Controls.OfType<Label>(), label => label.Text == second.Title);
            Assert.True(card.Controls.OfType<Button>().Single(button => button.AccessibleAutomationId == "HistoryOpenFile-" + second.QueueItemId.ToString("N")).Enabled);
            card.SuspendThumbnail(); card.UpdateEntry(first);
            Assert.False(card.Controls.OfType<Button>().Single(button => button.AccessibleAutomationId == "HistoryOpenFile-" + first.QueueItemId.ToString("N")).Enabled);
            card.Dispose(); card.Dispose();
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task RecycleAndDisposeReleaseQueuedAndDisplayedCopiesBeforeLateUiCallbacks()
    {
        var root = Path.Combine(Path.GetTempPath(), "MTD.History.Bitmap.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = AppPaths.Create(root); paths.EnsureCreated();
            const string url = "https://fixture.invalid/image";
            using (var source = new SKBitmap(32, 32))
            {
                source.Erase(SKColors.Blue);
                using var image = SKImage.FromBitmap(source); using var bytes = image.Encode(SKEncodedImageFormat.Png, 100);
                File.WriteAllBytes(Path.Combine(paths.ThumbnailCacheDirectory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))) + ".img"), bytes.ToArray());
            }
            using var cache = new ThumbnailCacheService(paths, new NullAppLogger());
            using var warmed = await cache.GetAsync(url);
            using var host = ModernFormsTestHost.Create();
            using var target = new PictureBox();
            var initial = ThumbnailImageBinding.OwnedBytes;
            var binding = ThumbnailImageBinding.For(target);
            await binding.LoadAsync(cache, url, default).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(initial + 32 * 32 * 4, ThumbnailImageBinding.OwnedBytes);
            binding.Clear(); host.ProcessPendingWork();
            Assert.Null(target.Image); Assert.Equal(initial, ThumbnailImageBinding.OwnedBytes);
            await binding.LoadAsync(cache, url, default); host.ProcessPendingWork();
            Assert.Equal(SKColors.Blue, target.Image!.GetPixel(1, 1));
            cache.Dispose(); Assert.Equal(SKColors.Blue, target.Image.GetPixel(1, 1));
            target.Dispose(); host.ProcessPendingWork();
            Assert.Equal(initial, ThumbnailImageBinding.OwnedBytes);
            binding.Dispose();
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
