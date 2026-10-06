using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Net.Http.Json;
using System.Text.Json;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.WebRemote;
using QRCoder;
using SkiaSharp;

namespace ModernTubeDownloader.Tests;

public sealed class WebRemoteIntegrationTests
{
    [Theory]
    [InlineData("Could not copy Chrome cookie database", "cookie-database-locked")]
    [InlineData("Failed to decrypt with DPAPI", "cookie-unavailable")]
    [InlineData("Could not find browser profile", "cookie-profile-unavailable")]
    [InlineData("Generic extraction failure", "analysis-failed")]
    public void AddErrorCode_ExposesOnlyStableSafeCookieDiagnostics(string diagnostic, string expected)
    {
        var result = new ProcessRunResult(1, [], [diagnostic], TimeSpan.Zero);
        Assert.Equal(expected, WebRemoteHost.AddErrorCode(new ExternalProcessException("yt-dlp failed", result)));
    }

    [Fact]
    public void AddressOnlyQr_IsGeneratedLocallyWithoutToken()
    {
        const string address = "http://192.168.1.50:18765/";
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(address, QRCodeGenerator.ECCLevel.Q);
        using var qr = new PngByteQRCode(data);
        using var bitmap = SKBitmap.Decode(qr.GetGraphic(5));
        Assert.NotNull(bitmap);
        Assert.True(bitmap.Width > 100);
    }

    [Fact]
    public async Task LiveApi_IsSeparateSafeAndUsesCentralSessionActions()
    {
        var paths = AppPaths.Create(Path.Combine(Path.GetTempPath(), "MTD.LiveRemote.Tests", Guid.NewGuid().ToString("N")));
        await ConfigureFakeToolsAsync(paths);
        using var services = AppServices.Create(paths);
        try
        {
            services.Queue.SetPaused(true);
            services.Settings.Current.EnableWebRemote = true;
            services.Settings.Current.WebRemoteBindAddress = "127.0.0.1";
            services.Settings.Current.WebRemotePort = FreeLoopbackPort();
            await services.Settings.SaveAsync();
            await WaitUntilAsync(() => services.WebRemote.IsRunning, TimeSpan.FromSeconds(10));
            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{services.Settings.Current.WebRemotePort}/") };
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/live")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/live/analyze", new { url = "https://www.youtube.com/live/upcoming" })).StatusCode);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", services.WebRemote.Token);
            using (var foreign = new HttpRequestMessage(HttpMethod.Get, "/api/live"))
            {
                foreign.Headers.TryAddWithoutValidation("Origin", "https://attacker.example");
                Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(foreign)).StatusCode);
            }
            var waitingLive = services.Live.Add("https://www.youtube.com/live/upcoming", null,
                "720", LiveStartPolicy.FromNow, waiting: true);
            using (var liveQueue = JsonDocument.Parse(await client.GetStringAsync("/api/queue")))
                Assert.DoesNotContain(liveQueue.RootElement.EnumerateArray(),
                    row => row.GetProperty("id").GetGuid() == waitingLive.Id);
            using (var liveSessions = JsonDocument.Parse(await client.GetStringAsync("/api/live")))
            {
                var row = Assert.Single(liveSessions.RootElement.EnumerateArray());
                Assert.Equal("WaitingForLive", row.GetProperty("state").GetString());
                Assert.Equal(waitingLive.Id, row.GetProperty("id").GetGuid());
                foreach (var forbidden in new[] { "workspacePath", "sourceUrl", "rawMetadataJson", "manifestUrl", "cookieFilePath", "rawArguments" })
                    Assert.False(row.TryGetProperty(forbidden, out _));
            }
            Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/queue/{waitingLive.Id}/cancel", null)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/live/{waitingLive.Id}/cancel", null)).StatusCode);
            Assert.Equal(LiveSessionState.Cancelled, services.Live.Find(waitingLive.Id)!.State);
            Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/live/{Guid.NewGuid()}/stop", null)).StatusCode);
            using (var analysis = await client.PostAsJsonAsync("/api/live/analyze", new { url = "https://www.youtube.com/live/upcoming" }))
            {
                Assert.Equal(HttpStatusCode.OK, analysis.StatusCode);
                Assert.DoesNotContain("formats", await analysis.Content.ReadAsStringAsync());
            }

            Assert.Empty(services.Queue.Snapshot());
        }
        finally { await services.ShutdownAsync(); }
    }

    [Fact]
    public async Task OptInAuthQueueActionsValidationAndShutdown_WorkOverHttp()
    {
        var root = Path.Combine(Path.GetTempPath(), "ModernTubeDownloader.Tests", Guid.NewGuid().ToString("N"));
        var paths = AppPaths.Create(root);
        AppServices? services = null;
        try
        {
            await ConfigureFakeToolsAsync(paths);
            services = AppServices.Create(paths);
            Assert.False(services.WebRemote.IsRunning);
            var port = int.TryParse(Environment.GetEnvironmentVariable("MTD_WEB_REMOTE_VISUAL_PORT"), out var visualPort)
                ? visualPort : FreeLoopbackPort();
            services.Queue.SetPaused(true);
            services.Settings.Current.WebRemoteBindAddress = "127.0.0.1";
            services.Settings.Current.WebRemotePort = port;
            services.Settings.Current.EnableWebRemote = true;
            await services.Settings.SaveAsync();
            await WaitUntilAsync(() => services.WebRemote.IsRunning, TimeSpan.FromSeconds(10));
            if (int.TryParse(Environment.GetEnvironmentVariable("MTD_WEB_REMOTE_VISUAL_HOLD_MS"), out var holdMilliseconds))
                await Task.Delay(Math.Clamp(holdMilliseconds, 0, 120000));

            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}/"), Timeout = TimeSpan.FromSeconds(15) };
            var shell = await client.GetAsync("/");
            Assert.Equal(HttpStatusCode.OK, shell.StatusCode);
            var html = await shell.Content.ReadAsStringAsync();
            Assert.DoesNotContain(services.WebRemote.Token, html);
            Assert.Contains("<details id=\"completedSection\"", html, StringComparison.Ordinal);
            Assert.DoesNotContain("<details id=\"completedSection\" open", html, StringComparison.Ordinal);
            Assert.Contains("<label class=\"optionToggle\"><input id=\"useSubtitleDefaults\" type=\"checkbox\">", html, StringComparison.Ordinal);
            Assert.Contains("<label class=\"optionToggle\"><input id=\"useSponsorBlockDefaults\" type=\"checkbox\">", html, StringComparison.Ordinal);
            Assert.DoesNotContain("id=\"from\"", html, StringComparison.Ordinal);
            Assert.DoesNotContain("id=\"to\"", html, StringComparison.Ordinal);
            foreach (var bound in new[] { "from", "to" })
                foreach (var unit in new[] { "Hours", "Minutes", "Seconds" })
                    Assert.Contains($"id=\"{bound}{unit}\" type=\"text\" inputmode=\"numeric\"", html, StringComparison.Ordinal);
            Assert.True(html.IndexOf("/time-input.js", StringComparison.Ordinal) < html.IndexOf("/app.js", StringComparison.Ordinal));
            var timeScript = await client.GetAsync("/time-input.js");
            Assert.Equal(HttpStatusCode.OK, timeScript.StatusCode);
            Assert.Equal("text/javascript", timeScript.Content.Headers.ContentType?.MediaType);
            Assert.Contains("MTDTimeInput", await timeScript.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            var timeCss = await client.GetStringAsync("/time-input.css");
            Assert.Contains("grid-template-columns:repeat(3,minmax(0,1fr))", timeCss, StringComparison.Ordinal);
            Assert.Contains("font-size:16px;min-height:48px", timeCss, StringComparison.Ordinal);
            var queueCss = await client.GetStringAsync("/remote-queue.css");
            Assert.Contains(".optionToggle input[type=checkbox]{width:18px;height:18px;min-height:18px;flex:0 0 18px", queueCss, StringComparison.Ordinal);
            using (var mode = JsonDocument.Parse(await client.GetStringAsync("/api/auth-mode")))
                Assert.True(mode.RootElement.GetProperty("authenticationRequired").GetBoolean());
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/status")).StatusCode);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid");
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/status")).StatusCode);
            var oldToken = services.WebRemote.Token;
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", oldToken);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/status")).StatusCode);
            using (var foreignOrigin = new HttpRequestMessage(HttpMethod.Get, "/api/status"))
            {
                foreignOrigin.Headers.TryAddWithoutValidation("Origin", "http://attacker.example");
                Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(foreignOrigin)).StatusCode);
            }
            using (var foreignHost = new HttpRequestMessage(HttpMethod.Get, "/api/status"))
            {
                foreignHost.Headers.Host = "attacker.example";
                Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(foreignHost)).StatusCode);
            }
            services.WebRemote.RegenerateToken();
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/status")).StatusCode);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", services.WebRemote.Token);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/queue")).StatusCode);

            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/queue/add", Add("file:///secret"))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/queue/add", Add("http://127.0.0.1/secret"))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/queue/add", Add("https://www.youtube.com/watch?v=test", quality: "raw-yt-dlp"))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/queue/add", Add("https://www.youtube.com/watch?v=test", container: "Exe"))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/queue/add", Add("https://www.youtube.com/watch?v=test", rangeMode: "custom", from: "00:10:00", to: "00:05:00"))).StatusCode);
            using (var oversized = new StringContent(new string('x', 10000), System.Text.Encoding.UTF8, "application/json"))
                Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await client.PostAsync("/api/queue/add", oversized)).StatusCode);

            await services.Tools.Initialization;
            var remoteLive = await client.PostAsJsonAsync("/api/queue/add",
                Add("https://www.youtube.com/live/active"));
            Assert.Equal(HttpStatusCode.BadRequest, remoteLive.StatusCode);
            Assert.Contains("live-open-in-app", await remoteLive.Content.ReadAsStringAsync());
            Assert.Empty(services.Queue.Snapshot());
            var added = await client.PostAsJsonAsync("/api/queue/add", Add("https://www.youtube.com/watch?v=remote-test", rangeMode: "custom", from: "00:00:02", to: "00:00:10"));
            Assert.Equal(HttpStatusCode.OK, added.StatusCode);
            var item = Assert.Single(services.Queue.Snapshot());
            Assert.Equal(new MediaTimeRange(MediaRangeMode.Custom, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10)), item.RequestedRange);
            services.Settings.Current.SubtitleDefaults = new SubtitleOptions { Enabled = true, Languages = ["en"] };
            services.Settings.Current.SponsorBlockDefaults = new SponsorBlockOptions { Mode = SponsorBlockMode.Mark, Categories = ["intro"] };
            var cookieFile = Path.Combine(paths.RootDirectory, "test cookies.txt");
            await File.WriteAllTextAsync(cookieFile, "# Netscape HTTP Cookie File\n");
            services.Settings.Current.UseCookieFile = true;
            services.Settings.Current.CookieFilePath = cookieFile;
            await services.Settings.SaveAsync();
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/queue/add", new
            {
                url = "https://www.youtube.com/watch?v=remote-test&id=range-conflict",
                quality = "best", container = "Auto", rangeMode = "custom", from = "00:00:02", to = "00:00:10",
                useSubtitleDefaults = true, useSponsorBlockDefaults = true
            })).StatusCode);
            var unsupportedMark = await client.PostAsJsonAsync("/api/queue/add", new
            {
                url = "https://www.youtube.com/watch?v=remote-test&id=enhancement-defaults",
                quality = "best", container = "Auto", rangeMode = "full",
                useSubtitleDefaults = true, useSponsorBlockDefaults = true
            });
            Assert.Equal(HttpStatusCode.BadRequest, unsupportedMark.StatusCode);
            Assert.Contains("sponsorblock-mark-requires-mkv", await unsupportedMark.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/queue/add", new
            {
                url = "https://www.youtube.com/watch?v=remote-test&id=enhancement-defaults",
                quality = "best", container = "Mkv", rangeMode = "full",
                useSubtitleDefaults = true, useSponsorBlockDefaults = true
            })).StatusCode);
            var enhanced = Assert.Single(services.Queue.Snapshot(), value => value.VideoId == "enhancement-defaults");
            Assert.True(enhanced.SubtitleOptions.Enabled);
            Assert.Equal(["en"], enhanced.SubtitleOptions.Languages);
            Assert.Equal(SponsorBlockMode.Mark, enhanced.SponsorBlockOptions.Mode);
            using (var queueResponse = JsonDocument.Parse(await client.GetStringAsync("/api/queue")))
            {
                var json = queueResponse.RootElement.GetRawText();
                Assert.DoesNotContain(cookieFile, json);
                Assert.DoesNotContain("cookieFilePath", json, StringComparison.OrdinalIgnoreCase);
                var row = queueResponse.RootElement.EnumerateArray().Single(value => value.GetProperty("id").GetGuid() == item.Id);
                Assert.Equal(services.Localization[$"Status.{item.Status}"], row.GetProperty("statusText").GetString());
                Assert.Equal(services.Localization["Quality.best"], row.GetProperty("quality").GetString());
            }
            Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync($"/api/queue/{enhanced.Id}")).StatusCode);
            services.Settings.Current.SubtitleDefaults = new SubtitleOptions();
            services.Settings.Current.SponsorBlockDefaults = new SponsorBlockOptions { Mode = SponsorBlockMode.Remove, Categories = ["intro"] };
            var removeResult = await client.PostAsJsonAsync("/api/queue/add", new
            {
                url = "https://www.youtube.com/watch?v=remote-test&id=remove-warning",
                quality = "best", container = "Mp4", rangeMode = "full",
                useSubtitleDefaults = false, useSponsorBlockDefaults = true
            });
            Assert.Equal(HttpStatusCode.OK, removeResult.StatusCode);
            Assert.Contains("sponsorblock-remove-experimental", await removeResult.Content.ReadAsStringAsync());
            var removeItem = Assert.Single(services.Queue.Snapshot(), value => value.VideoId == "remove-warning");
            Assert.Equal(SponsorBlockMode.Remove, removeItem.SponsorBlockOptions.Mode);
            Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync($"/api/queue/{removeItem.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/queue/resume", null)).StatusCode);
            Assert.False(services.Queue.IsPaused);
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/queue/pause", null)).StatusCode);
            Assert.True(services.Queue.IsPaused);

            var failed = new DownloadQueueItem { VideoId = "failed", Title = "Failed", SourceUrl = "https://www.youtube.com/watch?v=failed", Status = DownloadStatus.Failed, FailureMessageKey = "Error.Download.Network" };
            services.Queue.Add(failed);
            using (var queueResponse = JsonDocument.Parse(await client.GetStringAsync("/api/queue")))
            {
                var row = queueResponse.RootElement.EnumerateArray().Single(value => value.GetProperty("id").GetGuid() == failed.Id);
                Assert.Equal(services.Localization["Error.Download.Network"], row.GetProperty("error").GetString());
            }
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/queue/{failed.Id}/retry", null)).StatusCode);
            Assert.Equal(DownloadStatus.Queued, services.Queue.Find(failed.Id)?.Status);
            Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync($"/api/queue/{failed.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/queue/{Guid.NewGuid()}/cancel", null)).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/queue/{item.Id}/down", null)).StatusCode); // A single item cannot move.

            services.Settings.Current.WebRemoteRequireAuthentication = false;
            await services.Settings.SaveAsync();
            client.DefaultRequestHeaders.Authorization = null;
            using (var mode = JsonDocument.Parse(await client.GetStringAsync("/api/auth-mode")))
                Assert.False(mode.RootElement.GetProperty("authenticationRequired").GetBoolean());
            using (var status = JsonDocument.Parse(await client.GetStringAsync("/api/status")))
            {
                Assert.False(status.RootElement.GetProperty("authenticationRequired").GetBoolean());
                Assert.True(status.RootElement.TryGetProperty("remainingAdmissionSeconds", out _));
            }
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/queue")).StatusCode);
            using (var foreignOriginWithoutAuth = new HttpRequestMessage(HttpMethod.Get, "/api/status"))
            {
                foreignOriginWithoutAuth.Headers.TryAddWithoutValidation("Origin", "http://attacker.example");
                Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(foreignOriginWithoutAuth)).StatusCode);
            }
            using (var foreignHostWithoutAuth = new HttpRequestMessage(HttpMethod.Get, "/api/status"))
            {
                foreignHostWithoutAuth.Headers.Host = "attacker.example";
                Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(foreignHostWithoutAuth)).StatusCode);
            }
            if (int.TryParse(Environment.GetEnvironmentVariable("MTD_WEB_REMOTE_NO_AUTH_VISUAL_HOLD_MS"), out var noAuthHoldMilliseconds))
            {
                services.Queue.Add(new DownloadQueueItem { VideoId = "completed-visual", Title = "Completed sample", Status = DownloadStatus.Completed });
                await Task.Delay(Math.Clamp(noAuthHoldMilliseconds, 0, 120000));
            }
            services.Settings.Current.WebRemoteRequireAuthentication = true;
            await services.Settings.SaveAsync();
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/status")).StatusCode);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", services.WebRemote.Token);

            HttpStatusCode limited = HttpStatusCode.OK;
            for (var attempt = 0; attempt < 25; attempt++)
                limited = (await client.PostAsync("/api/queue/pause", null)).StatusCode;
            Assert.Equal(HttpStatusCode.TooManyRequests, limited);

            services.Settings.Current.EnableWebRemote = false;
            await services.Settings.SaveAsync();
            await WaitUntilAsync(() => !services.WebRemote.IsRunning, TimeSpan.FromSeconds(10));
            services.Settings.Current.EnableWebRemote = true;
            await services.Settings.SaveAsync();
            await WaitUntilAsync(() => services.WebRemote.IsRunning, TimeSpan.FromSeconds(10));
            await services.ShutdownAsync();
            Assert.False(services.WebRemote.IsRunning);
        }
        finally
        {
            if (services is not null)
            {
                await services.ShutdownAsync();
                services.Dispose();
            }
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static object Add(string url, string quality = "best", string container = "Auto", string rangeMode = "full", string? from = null, string? to = null)
        => new { url, quality, container, rangeMode, from, to };

    private static int FreeLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var end = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < end) await Task.Delay(50);
        Assert.True(condition());
    }

    private static async Task ConfigureFakeToolsAsync(AppPaths paths)
    {
        paths.EnsureCreated();
        var settings = new SettingsService(paths, new NullAppLogger());
        await settings.LoadAsync();
        settings.Current.UseCustomYtDlp = true;
        settings.Current.CustomYtDlpPath = CoreTests.FakeToolPath();
        settings.Current.UseCustomFfmpeg = true;
        settings.Current.CustomFfmpegPath = CoreTests.FakeToolPath();
        settings.Current.CustomFfprobePath = CoreTests.FakeToolPath();
        var denoDirectory = Path.Combine(paths.RootDirectory, "custom-deno");
        Directory.CreateDirectory(denoDirectory);
        foreach (var source in Directory.EnumerateFiles(AppContext.BaseDirectory, "ModernTubeDownloader.FakeTool*"))
            File.Copy(source, Path.Combine(denoDirectory, Path.GetFileName(source)), true);
        var denoPath = Path.Combine(denoDirectory, "deno.exe");
        File.Copy(CoreTests.FakeToolPath(), denoPath, true);
        settings.Current.UseCustomDeno = true;
        settings.Current.CustomDenoPath = denoPath;
        await settings.SaveAsync();
    }
}
