using System.Net;
using System.Text.Json;
using ModernFormsNext;
using ModernFormsNext.Testing;
using ModernTubeDownloader.Settings;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Theming;
using ModernTubeDownloader.Views;
using Path = System.IO.Path;

namespace ModernTubeDownloader.Tests;

public sealed partial class ExperiencePresentationTests
{
    private static readonly string NotesVersion = ReleaseNotesCatalog.NormalizeVersion(AppBranding.Version)!;

    [Theory]
    [InlineData("en", 100, false)] [InlineData("pl", 150, false)] [InlineData("ja-JP", 200, false)]
    [InlineData("pl", 200, true)]
    public async Task ExactOnlineNotesUpdateRetainedCardsWithoutLosingScrollFocusOrAcknowledging(string language, int scale, bool focusEditor)
    {
        using var fixture = new Fixture(); using var handler = new ControlledNotesHandler(); using var client = new HttpClient(handler);
        using var services = await fixture.Services(AppThemeMode.Dark, client);
        using (var host = ModernFormsTestHost.Create())
        {
            services.Appearance.Initialize(); services.Localization.SetLanguage(language); AppTypography.SetScale(scale);
            using var form = new WhatsNewForm(services); var window = host.Show(form, 720, 560); var root = Root(form);
            var controls = Descendants(root).ToArray();
            var local = Find<Label>(root, "WhatsNewProvenance");
            Assert.Equal(services.Localization["Experience.LocalNotes"], local.Text);
            Pump(host, () => handler.Requests == 1);
            var scroll = Descendants(root).OfType<ExperienceScrollPanel>().Single();
            var done = Find<Button>(root, "WhatsNewDone");
            var focusedEditor = Find<TextBox>(root, "WhatsNewText-new");
            Ui(host, () =>
            {
                if (focusEditor) { focusedEditor.Select(); focusedEditor.SelectionStart = 5; focusedEditor.SelectionEnd = 17; }
                else done.Select();
                scroll.VerticalScrollProperties.Value = 48;
            });
            var offset = scroll.VerticalScrollProperties.Value;
            Assert.Equal(48, offset); Assert.True(focusEditor ? focusedEditor.Focused : done.Focused);
            handler.Complete();
            Pump(host, () => local.Text == services.Localization["Experience.GitHubNotes"]);
            Assert.Equal(controls, Descendants(root).ToArray());
            Assert.Equal(offset, scroll.VerticalScrollProperties.Value); Assert.True(focusEditor ? focusedEditor.Focused : done.Focused);
            if (focusEditor) { Assert.Equal(5, focusedEditor.SelectionStart); Assert.Equal(17, focusedEditor.SelectionEnd); }
            Assert.Equal(language != "en", Find<Label>(root, "WhatsNewLanguageFallback").Text.Length > 0);
            Assert.Null(services.Experience.State.LastWhatsNewAcknowledgedVersion);
            AssertGeometry(root, "Exact online notes " + language);
            var editors = Descendants(root).OfType<TextBox>().ToArray();
            Assert.Equal(4, editors.Length);
            Assert.All(editors, editor => { Assert.True(editor.ReadOnly && editor.MultiLine); Assert.InRange(editor.Height, 1, 360); Assert.Contains("Verified remote change", editor.Text); });
            var firstEditor = editors[0];
            Ui(host, () => { firstEditor.Select(); firstEditor.SelectAll(); });
            Assert.Equal(firstEditor.Text.Length, Math.Abs(firstEditor.SelectionEnd - firstEditor.SelectionStart));
            Click(host, root, "WhatsNewLater"); Pump(host, () => window.IsClosed);
            Assert.Null(services.Experience.State.LastWhatsNewAcknowledgedVersion);
            using var reopened = new WhatsNewForm(services); var reopenedWindow = host.Show(reopened, 720, 560);
            var reopenedRoot = Root(reopened);
            Pump(host, () => Find<Label>(reopenedRoot, "WhatsNewProvenance").Text == services.Localization["Experience.CachedNotes"]);
            Assert.Equal(1, handler.Requests);
            Click(host, reopenedRoot, "WhatsNewDone"); Pump(host, () => reopenedWindow.IsClosed);
            Assert.Equal(NotesVersion, services.Experience.State.LastWhatsNewAcknowledgedVersion);
        }
        await services.ShutdownAsync();
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task OfficialAndMaximumNotesKeepCompleteTextInBoundedReadOnlySurfaces(bool nearLimit)
    {
        var body = nearLimit
            ? "## New\n" + string.Join("\n\n", Enumerable.Repeat(new string('N', 300), 185))
                + "\n\n## Improvements\nFull content\n\n## Fixes\nFull content\n\n## Known Issues\nFull content"
            : await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "OfficialReleaseNotes.md"));
        Assert.InRange(System.Text.Encoding.UTF8.GetByteCount(body), 1, GitHubReleaseNotesService.MaximumBodyBytes);
        var sections = ReleaseNotesMarkdown.Parse(body);
        using var fixture = new Fixture(); using var handler = new ControlledNotesHandler { Body = body }; using var client = new HttpClient(handler);
        using var services = await fixture.Services(AppThemeMode.Dark, client);
        using (var host = ModernFormsTestHost.Create())
        {
            services.Appearance.Initialize(); AppTypography.SetScale(200);
            using var form = new WhatsNewForm(services); var window = host.Show(form, 720, 560);
            Pump(host, () => handler.Requests == 1); handler.Complete();
            var root = Root(form);
            Pump(host, () => Find<Label>(root, "WhatsNewProvenance").Text == services.Localization["Experience.GitHubNotes"]);
            foreach (var section in sections)
            {
                var editor = Find<TextBox>(root, "WhatsNewText-" + section.Category);
                Assert.Equal(string.Join("\n\n", section.Items), editor.Text);
                Assert.True(editor.ReadOnly); Assert.InRange(editor.Height, 1, 360);
                Assert.InRange(editor.Parent!.Height, 1, 560);
                Ui(host, () => { editor.Select(); editor.SelectAll(); });
                Assert.Equal(editor.Text.Length, Math.Abs(editor.SelectionEnd - editor.SelectionStart));
            }
            AssertGeometry(root, "Full release notes at 200% text");
            Click(host, root, "WhatsNewLater"); Pump(host, () => window.IsClosed);
            Assert.Null(services.Experience.State.LastWhatsNewAcknowledgedVersion);
        }
        await services.ShutdownAsync();
    }

    [Fact]
    public async Task ClosingDuringFetchCancelsItAndReopenCanLoadWithoutLateUiWrites()
    {
        using var fixture = new Fixture(); using var handler = new ControlledNotesHandler(); using var client = new HttpClient(handler);
        using var services = await fixture.Services(AppThemeMode.Dark, client);
        using (var host = ModernFormsTestHost.Create())
        {
            services.Appearance.Initialize();
            var form = new WhatsNewForm(services); var window = host.Show(form, 720, 560); Pump(host, () => handler.Requests == 1);
            Click(host, Root(form), "WhatsNewLater"); Pump(host, () => window.IsClosed && handler.Cancellations == 1); form.Dispose();
            Assert.Null(services.Experience.State.LastWhatsNewAcknowledgedVersion);
            using var next = new WhatsNewForm(services); var nextWindow = host.Show(next, 720, 560);
            Pump(host, () => handler.Requests == 2); handler.Complete();
            Pump(host, () => Find<Label>(Root(next), "WhatsNewProvenance").Text == services.Localization["Experience.GitHubNotes"]);
            host.Dispatcher.ThrowUnhandledExceptions();
            Click(host, Root(next), "WhatsNewLater"); Pump(host, () => nextWindow.IsClosed);
        }
        await services.ShutdownAsync();
    }

    private sealed class ControlledNotesHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int requests, cancellations;
        public int Requests => Volatile.Read(ref requests);
        public int Cancellations => Volatile.Read(ref cancellations);
        public string? Body { get; init; }
        public void Complete() => ready.TrySetResult();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.RequestUri?.AbsolutePath != $"/repos/ProGraMajster/ModernTubeDownloader/releases/tags/v{NotesVersion}")
                return new(HttpStatusCode.NotFound);
            Interlocked.Increment(ref requests);
            try { await ready.Task.WaitAsync(token); }
            catch (OperationCanceledException) { Interlocked.Increment(ref cancellations); throw; }
            var paragraph = string.Join("\n", Enumerable.Repeat("- Verified remote change for exactly this installed release; data is preserved.", 24));
            var body = Body ?? string.Join("\n\n", new[] { "New", "Improvements", "Fixes", "Known Issues" }.Select(title => "## " + title + "\n" + paragraph));
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new {
                tag_name = $"v{NotesVersion}", draft = false, prerelease = false,
                html_url = $"{GitHubApplicationReleaseSource.Repository}/releases/tag/v{NotesVersion}", body
            })) };
        }
    }
}
