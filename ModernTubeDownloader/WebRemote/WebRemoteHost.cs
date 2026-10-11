using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Localization;
using ModernTubeDownloader.Services;
using SkiaSharp;

namespace ModernTubeDownloader.WebRemote;

/// <summary>In-process, opt-in HTTP adapter over the desktop application's existing services.</summary>
public sealed class WebRemoteHost : IDisposable
{
    private readonly AppServices services;
    private readonly SemaphoreSlim lifecycle = new(1, 1);
    private readonly SemaphoreSlim addGate = new(2, 2);
    private WebApplication? application;
    private string token = NewToken();
    private string? runningAddress;
    private int runningPort;
    private bool runningHttps;
    private bool runningAuthentication;
    private X509Certificate2? serverCertificate;
    private readonly LocalizationService polish = new("pl");
    private readonly LocalizationService english = new("en");
    public const string SessionCookie = "mtd-remote-session";
    private bool disposed;
    private bool stopping;

    public WebRemoteHost(AppServices services)
    {
        this.services = services;
        Certificates = new WebRemoteCertificateStore(services.Paths);
        services.Settings.Changed += SettingsChanged;
    }

    public event EventHandler? Changed;
    public bool IsRunning => application is not null;
    public string? Address => runningAddress is null ? null : $"{(runningHttps ? "https" : "http")}://{runningAddress}:{runningPort}/";
    public WebRemotePairing Pairing { get; } = new();
    public WebRemoteCertificateStore Certificates { get; }
    public string Token => token;
    public string? LastError { get; private set; }

    public static IReadOnlyList<string> AvailableAddresses()
    {
        try
        {
            var addresses = NetworkInterface.GetAllNetworkInterfaces()
                .Where(nic => nic.OperationalStatus == OperationalStatus.Up)
                .SelectMany(nic => nic.GetIPProperties().UnicastAddresses)
                .Select(unicast => unicast.Address)
                .Where(IsPrivateLanAddress)
                .Select(address => address.ToString())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(address => address, StringComparer.Ordinal)
                .ToList();
            addresses.Insert(0, IPAddress.Loopback.ToString());
            return addresses;
        }
        catch (NetworkInformationException) { return [IPAddress.Loopback.ToString()]; }
    }

    public void RegenerateToken()
    {
        token = NewToken();
        Pairing.Stop();
        if (IsRunning && services.Settings.Current.WebRemoteRequireAuthentication) Pairing.Start();
        services.Logger.Info("Web remote token regenerated; existing browser sessions invalidated.");
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void RegeneratePairingCode()
    {
        Pairing.Regenerate();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task GenerateCertificateAsync()
    {
        await lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            var address = IPAddress.Parse(services.Settings.Current.WebRemoteBindAddress);
            await Task.Run(() => Certificates.Generate(address)).ConfigureAwait(false);
            if (runningHttps) await StopCoreAsync().ConfigureAwait(false);
        }
        finally { lifecycle.Release(); }
        await ReconcileAsync().ConfigureAwait(false);
    }

    public async Task ReconcileAsync()
    {
        await lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            if (stopping) return;
            var config = services.Settings.Current;
            if (!config.EnableWebRemote)
            {
                await StopCoreAsync().ConfigureAwait(false);
                LastError = null;
                return;
            }
            if (application is not null && runningAddress == config.WebRemoteBindAddress && runningPort == config.WebRemotePort &&
                runningHttps == config.WebRemoteUseHttps)
            {
                if (runningAuthentication != config.WebRemoteRequireAuthentication)
                {
                    token = NewToken();
                    Pairing.Stop();
                    runningAuthentication = config.WebRemoteRequireAuthentication;
                    if (runningAuthentication) Pairing.Start();
                }
                if (runningAuthentication) Pairing.EnsureCode();
                return;
            }
            await StopCoreAsync().ConfigureAwait(false);
            if (!IPAddress.TryParse(config.WebRemoteBindAddress, out var bindIp) ||
                !AvailableAddresses().Contains(bindIp.ToString(), StringComparer.Ordinal))
                throw new WebRemoteCertificateException("Settings.RemoteInvalidBind");
            var certificate = config.WebRemoteUseHttps ? Certificates.Load(bindIp) : null;
            WebApplication app;
            try { app = Build(bindIp, config.WebRemotePort, certificate); }
            catch { certificate?.Dispose(); throw; }
            try { await app.StartAsync().ConfigureAwait(false); }
            catch { await app.DisposeAsync().ConfigureAwait(false); certificate?.Dispose(); throw; }
            application = app;
            serverCertificate = certificate;
            runningAddress = bindIp.ToString();
            runningPort = config.WebRemotePort;
            runningHttps = config.WebRemoteUseHttps;
            runningAuthentication = config.WebRemoteRequireAuthentication;
            if (runningAuthentication) Pairing.Start();
            LastError = null;
            services.Logger.Info($"Web remote started on {runningAddress}:{runningPort}; LAN binding only.");
        }
        catch (Exception ex)
        {
            LastError = ex is WebRemoteCertificateException known ? services.Localization[known.MessageKey] : services.Localization["Settings.RemoteStartFailed"];
            services.Logger.Error("Web remote could not start.", ex);
        }
        finally
        {
            lifecycle.Release();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task StopAsync()
    {
        stopping = true;
        services.Settings.Changed -= SettingsChanged;
        await lifecycle.WaitAsync().ConfigureAwait(false);
        try { await StopCoreAsync().ConfigureAwait(false); }
        finally { lifecycle.Release(); Changed?.Invoke(this, EventArgs.Empty); }
    }

    private async Task StopCoreAsync()
    {
        Pairing.Stop();
        token = NewToken();
        if (application is null) return;
        var app = application;
        application = null;
        runningAddress = null;
        runningPort = 0;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try { await app.StopAsync(timeout.Token).ConfigureAwait(false); }
        finally
        {
            await app.DisposeAsync().ConfigureAwait(false);
            serverCertificate?.Dispose();
            serverCertificate = null;
        }
        services.Logger.Info("Web remote stopped.");
    }

    private WebApplication Build(IPAddress bindIp, int port, X509Certificate2? certificate)
    {
        // Never inherit a developer exception page from the desktop process environment.
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [], EnvironmentName = Environments.Production });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.AddServerHeader = false;
            kestrel.Limits.MaxRequestBodySize = 8192;
            kestrel.Listen(bindIp, port, endpoint => { if (certificate is not null) endpoint.UseHttps(certificate); });
        });
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0,
                        AutoReplenishment = true
                    }));
            options.AddFixedWindowLimiter("mutation", limiter =>
            {
                limiter.PermitLimit = 20;
                limiter.Window = TimeSpan.FromMinutes(1);
                limiter.QueueLimit = 0;
            });
        });
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            try { await next(context); }
            catch (Exception ex)
            {
                services.Logger.Error("Web remote request failed.", ex);
                if (!context.Response.HasStarted)
                {
                    context.Response.Clear();
                    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                    await context.Response.WriteAsJsonAsync(new { error = "server-error" });
                }
            }
        });
        app.UseRouting();
        app.UseRateLimiter();
        app.Use(async (context, next) =>
        {
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["X-Frame-Options"] = "DENY";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers["Cache-Control"] = "no-store";
            context.Response.Headers["Content-Security-Policy"] = "default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self' blob:; connect-src 'self'; base-uri 'none'; form-action 'self'; frame-ancestors 'none'";
            var expectedOrigin = $"{(certificate is null ? "http" : "https")}://{bindIp}:{port}";
            if (!string.Equals(context.Request.Host.Host, bindIp.ToString(), StringComparison.Ordinal) ||
                context.Request.Host.Port != port ||
                (context.Request.Headers.Origin.Count > 0 &&
                 !string.Equals(context.Request.Headers.Origin.ToString(), expectedOrigin, StringComparison.OrdinalIgnoreCase)))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
            var hasBearer = Authenticate(context.Request.Headers.Authorization.ToString());
            var hasSession = Pairing.Authenticate(context.Request.Cookies[SessionCookie]);
            if (context.Request.Path.StartsWithSegments("/api") &&
                !string.Equals(context.Request.Path.Value, "/api/auth-mode", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(context.Request.Path.Value, "/api/pair", StringComparison.OrdinalIgnoreCase) &&
                services.Settings.Current.WebRemoteRequireAuthentication &&
                !hasBearer && !hasSession)
            {
                services.Logger.Warning($"Web remote auth failure ClientIp={context.Connection.RemoteIpAddress}");
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }
            // Cookie-authorized mutations and pairing require an explicit same-origin browser request.
            // Diagnostic bearer clients retain the existing API; no bearer is exposed in the browser UI.
            if (context.Request.Path.StartsWithSegments("/api") && !HttpMethods.IsGet(context.Request.Method) &&
                !hasBearer && (!string.Equals(context.Request.Headers.Origin.ToString(), expectedOrigin, StringComparison.OrdinalIgnoreCase) ||
                context.Request.Headers["X-MTD-Request"] != "1"))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
            await next(context);
        });
        app.MapGet("/", () => Asset("index.html", "text/html; charset=utf-8"));
        app.MapGet("/app.css", () => Asset("app.css", "text/css; charset=utf-8"));
        app.MapGet("/remote-queue.css", () => Asset("remote-queue.css", "text/css; charset=utf-8"));
        app.MapGet("/time-input.css", () => Asset("time-input.css", "text/css; charset=utf-8"));
        app.MapGet("/time-input.js", () => Asset("time-input.js", "text/javascript; charset=utf-8"));
        app.MapGet("/app.js", () => Asset("app.js", "text/javascript; charset=utf-8"));
        app.MapGet("/i18n.js", () => Asset("i18n.js", "text/javascript; charset=utf-8"));
        app.MapPost("/api/pair", (PairRequest request, HttpContext context) =>
        {
            if (!services.Settings.Current.WebRemoteRequireAuthentication) return Results.Conflict(new { error = "pairing-disabled" });
            if (request.Code is not { Length: > 0 and <= 16 }) return Results.BadRequest(new { error = "pairing-invalid" });
            var result = Pairing.Pair(request.Code, context.Connection.RemoteIpAddress?.ToString() ?? "unknown");
            if (result.Session is null) return Results.Json(new { error = result.Error }, statusCode: result.Error == "pairing-rate-limit" ? 429 : 400);
            context.Response.Cookies.Append(SessionCookie, result.Session, CookieOptions(context.Request.IsHttps));
            return Results.Ok();
        });
        app.MapPost("/api/logout", (HttpContext context) =>
        {
            Pairing.Logout(context.Request.Cookies[SessionCookie]);
            context.Response.Cookies.Delete(SessionCookie, CookieOptions(context.Request.IsHttps));
            return Results.Ok();
        });
        app.MapGet("/api/auth-mode", () => Results.Json(new
        {
            authenticationRequired = services.Settings.Current.WebRemoteRequireAuthentication,
            desktopLanguage = services.Localization.Language == "pl" ? "pl" : "en"
        }));
        app.MapGet("/api/status", () => Results.Json(new
        {
            paused = services.Queue.IsPaused,
            count = services.Queue.Snapshot().Count,
            language = services.Settings.Current.Language,
            authenticationRequired = services.Settings.Current.WebRemoteRequireAuthentication,
            remainingAdmissionSeconds = services.Processor.RemainingAdmissionDelay?.TotalSeconds
        }));
        app.MapGet("/api/queue", (HttpContext context) => Results.Json(services.Queue.Snapshot().Select((item, index) => new
        {
            id = item.Id, position = index + 1, title = item.Title, status = item.Status.ToString(),
            statusText = Locale(context)[$"Status.{item.Status}"],
            progress = item.ProgressPercent, speed = item.SpeedBytesPerSecond,
            etaSeconds = item.Eta?.TotalSeconds,
            quality = Locale(context)[$"Quality.{QualityPreset.Find(item.QualityPresetId).Id}"],
            container = Locale(context)[$"Container.{item.PreferredVideoContainer}"],
            range = item.RequestedRange?.DisplayText,
            retryAttempt = item.AttemptCount, maximumAttempts = item.MaximumAttempts,
            error = item.FailureMessageKey is { } key ? Locale(context)[key] : null,
            hasThumbnail = !string.IsNullOrWhiteSpace(item.ThumbnailUrl)
        }).ToArray()));
        app.MapGet("/api/queue/{id:guid}/thumbnail", async (Guid id, CancellationToken cancellationToken) =>
        {
            var item = services.Queue.Find(id);
            if (item?.ThumbnailUrl is not { Length: > 0 } thumbnailUrl) return Results.NotFound();
            var bitmap = await services.Thumbnails.GetAsync(thumbnailUrl, cancellationToken);
            if (bitmap is null) return Results.NotFound();
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 80);
            return Results.File(data.ToArray(), "image/png");
        });
        app.MapPost("/api/queue/add", AddAsync).RequireRateLimiting("mutation");
        app.MapPost("/api/queue/{id:guid}/cancel", (Guid id, HttpContext context) => ActionResult("cancel", id,
            services.Processor.CancelActive(id), context)).RequireRateLimiting("mutation");
        app.MapPost("/api/queue/{id:guid}/retry", (Guid id, HttpContext context) => ActionResult("retry", id,
            services.Queue.Retry(id), context)).RequireRateLimiting("mutation");
        app.MapDelete("/api/queue/{id:guid}", (Guid id, HttpContext context) => ActionResult("remove", id,
            services.Queue.Remove(id), context)).RequireRateLimiting("mutation");
        app.MapPost("/api/queue/{id:guid}/up", (Guid id, HttpContext context) => ActionResult("move-up", id,
            services.Queue.MoveBy(id, -1), context)).RequireRateLimiting("mutation");
        app.MapPost("/api/queue/{id:guid}/down", (Guid id, HttpContext context) => ActionResult("move-down", id,
            services.Queue.MoveBy(id, 1), context)).RequireRateLimiting("mutation");
        app.MapPost("/api/queue/pause", (HttpContext context) => PauseResult(true, context)).RequireRateLimiting("mutation");
        app.MapPost("/api/queue/resume", (HttpContext context) => PauseResult(false, context)).RequireRateLimiting("mutation");
        app.MapGet("/api/live", (HttpContext context) => Results.Json(services.Live.Snapshot().Select(session => new
        {
            id = session.Id, title = session.Title, state = session.State.ToString(),
            stateText = Locale(context)[$"Live.State.{session.State}"],
            recordedSeconds = session.RecordedDuration.TotalSeconds, bytes = session.BytesWritten,
            speed = session.CurrentSpeed, nextCheckAt = session.NextCheckAt, retryAt = session.RetryAt,
            scheduledStartAt = session.ScheduledStartAt, partsCount = session.Parts.Count,
            startPolicy = session.StartPolicy.ToString(), canResume = session.CanResume,
            isActive = session.IsActive, retryCount = session.ResumeCount,
            quality = Locale(context)[$"Quality.{session.QualityPresetId}"], container = "MKV",
            error = session.FailureMessageKey is { } key ? Locale(context)[key] : null
        }).ToArray()));
        app.MapPost("/api/live/{id:guid}/stop", (Guid id, HttpContext context) => ActionResult("live-stop", id,
            services.Live.StopAndSave(id), context)).RequireRateLimiting("mutation");
        app.MapPost("/api/live/{id:guid}/cancel", (Guid id, HttpContext context) => ActionResult("live-cancel", id,
            services.Live.Cancel(id), context)).RequireRateLimiting("mutation");
        app.MapPost("/api/live/{id:guid}/resume", (Guid id, HttpContext context) => ActionResult("live-resume", id,
            services.Live.Resume(id), context)).RequireRateLimiting("mutation");
        app.MapPost("/api/live/analyze", async (LiveAnalyzeRequest request, CancellationToken token) =>
        {
            if (!YtDlpMetadataService.TryValidateSourceUrl(request.Url ?? string.Empty, out var url, out _))
                return Results.BadRequest(new { error = "invalid-url" });
            await addGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                var metadata = await services.Metadata.AnalyzeAsync(url, token).ConfigureAwait(false);
                return Results.Json(new { title = metadata.Title, kind = MediaAvailabilityPolicy.Classify(metadata).ToString(),
                    scheduledStart = metadata.ReleaseTimestamp, openInApp = true });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var failure = DownloadFailureClassifier.Classify(ex);
                return failure.Category == DownloadFailureCategory.Upcoming
                    ? Results.Json(new { kind = "Upcoming", openInApp = true })
                    : Results.BadRequest(new { error = "analysis-failed" });
            }
            finally { addGate.Release(); }
        }).RequireRateLimiting("mutation");
        return app;
    }

    private IResult PauseResult(bool paused, HttpContext context)
    {
        services.Queue.SetPaused(paused);
        services.Logger.Info($"Web remote action={(paused ? "pause" : "resume")} ClientIp={context.Connection.RemoteIpAddress} Result=ok");
        return Results.Ok();
    }

    private LocalizationService Locale(HttpContext context) => context.Request.Headers["X-MTD-Language"].ToString() switch
    {
        "pl" => polish, "en" => english, _ => services.Localization
    };

    private static CookieOptions CookieOptions(bool secure) => new()
    {
        HttpOnly = true, SameSite = SameSiteMode.Strict, Secure = secure, IsEssential = true,
        Path = "/", MaxAge = WebRemotePairing.SessionLifetime
    };

    private IResult ActionResult(string action, Guid id, bool success, HttpContext context)
    {
        services.Logger.Info($"Web remote action={action} ItemId={id} ClientIp={context.Connection.RemoteIpAddress} Result={(success ? "ok" : "rejected")}");
        return success ? Results.Ok() : Results.Conflict();
    }

    private async Task<IResult> AddAsync(AddRequest request, HttpContext context, CancellationToken cancellationToken)
    {
        if (request.Url is not { Length: > 0 and <= 2048 } ||
            !YtDlpMetadataService.TryValidateSourceUrl(request.Url, out var url, out _) ||
            new Uri(url).Scheme != Uri.UriSchemeHttps ||
            !IsAllowedMediaHost(new Uri(url).Host) ||
            !QualityPreset.All.Any(preset => preset.Id == request.Quality) ||
            !Enum.TryParse<PreferredVideoContainer>(request.Container, true, out var container) ||
            !Enum.IsDefined(container) || !TryRange(request, out var range))
            return Results.BadRequest(new { error = "invalid-input" });
        if (!await addGate.WaitAsync(0, cancellationToken)) return Results.StatusCode(StatusCodes.Status429TooManyRequests);
        try
        {
            QueueBatchAddResult result;
            var preset = QualityPreset.Find(request.Quality);
            var subtitles = request.UseSubtitleDefaults == true
                ? services.Settings.Current.SubtitleDefaults.Copy() : new SubtitleOptions();
            var sponsorBlock = request.UseSponsorBlockDefaults == true
                ? services.Settings.Current.SponsorBlockDefaults.Copy() : new SponsorBlockOptions();
            var compatibility = DownloadOptionCompatibilityValidator.Check(range, subtitles, sponsorBlock, container);
            if (compatibility != DownloadOptionIssue.None)
                return Results.BadRequest(new { error = DownloadOptionCompatibilityValidator.WebRemoteCode(compatibility) });
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(90));
            if (YtDlpMetadataService.IsPlaylistUrl(url))
            {
                var playlist = await services.Metadata.AnalyzePlaylistAsync(url, timeout.Token);
                var selection = new PlaylistSelection(playlist);
                result = services.Queue.AddRangeSkippingDuplicates(selection.CreateQueueItems(preset, container, range,
                    subtitles, sponsorBlock));
            }
            else
            {
                var metadata = await services.Metadata.AnalyzeAsync(url, timeout.Token);
                if (MediaAvailabilityPolicy.Classify(metadata) is MediaAvailabilityKind.ActiveLive or MediaAvailabilityKind.Upcoming)
                    return Results.BadRequest(new { error = "live-open-in-app" });
                if (range.Validate(metadata.DurationSeconds, MediaAvailabilityPolicy.Classify(metadata) == MediaAvailabilityKind.ActiveLive) is not null)
                    return Results.BadRequest(new { error = "invalid-range" });
                var formats = FormatSelector.Select(metadata, preset, container);
                result = services.Queue.AddRangeSkippingDuplicates(
                    [DownloadQueueItem.FromMetadata(metadata, preset, formats, container, range, subtitles, sponsorBlock)]);
            }
            services.Logger.Info($"Web remote action=add ClientIp={context.Connection.RemoteIpAddress} Added={result.Added} Duplicates={result.DuplicatesSkipped}");
            return Results.Json(new { result.Added, result.DuplicatesSkipped,
                warning = DownloadOptionCompatibilityValidator.WarningKey(sponsorBlock) is not null
                    ? "sponsorblock-remove-experimental" : null });
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Results.StatusCode(StatusCodes.Status504GatewayTimeout);
        }
        catch (Exception ex)
        {
            services.Logger.Error("Web remote add failed.", ex);
            return Results.BadRequest(new { error = AddErrorCode(ex) });
        }
        finally { addGate.Release(); }
    }

    internal static string AddErrorCode(Exception error) => DownloadFailureClassifier.Classify(error).Category switch
    {
        DownloadFailureCategory.CookieFileMissing => "cookie-file-missing",
        DownloadFailureCategory.CookieFileUnreadable => "cookie-file-unreadable",
        DownloadFailureCategory.CookieFileUnsupported => "cookie-file-unsupported",
        DownloadFailureCategory.CookieFileRejected => "cookie-file-rejected",
        DownloadFailureCategory.CookieDatabaseLocked => "cookie-database-locked",
        DownloadFailureCategory.CookieProfileUnavailable => "cookie-profile-unavailable",
        DownloadFailureCategory.CookieUnavailable => "cookie-unavailable",
        _ => "analysis-failed"
    };

    private static bool TryRange(AddRequest request, out MediaTimeRange range)
    {
        range = MediaTimeRange.Full;
        if (string.Equals(request.RangeMode, "full", StringComparison.OrdinalIgnoreCase))
            return string.IsNullOrWhiteSpace(request.From) && string.IsNullOrWhiteSpace(request.To);
        if (!string.Equals(request.RangeMode, "custom", StringComparison.OrdinalIgnoreCase) ||
            !MediaTimeRange.TryParseTime(request.From, out var start) ||
            !MediaTimeRange.TryParseTime(request.To, out var end)) return false;
        range = new MediaTimeRange(MediaRangeMode.Custom, start, end);
        return range.Validate(null) is null;
    }

    private static bool IsAllowedMediaHost(string host) =>
        host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase) ||
        host.Equals("youtube.com", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".youtube.com", StringComparison.OrdinalIgnoreCase) ||
        host.Equals("youtube-nocookie.com", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".youtube-nocookie.com", StringComparison.OrdinalIgnoreCase);

    private bool Authenticate(string header)
    {
        if (header.Length is < 10 or > 80 || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            var submitted = DecodeToken(header[7..]);
            var expected = DecodeToken(token);
            return submitted.Length == expected.Length && CryptographicOperations.FixedTimeEquals(submitted, expected);
        }
        catch (FormatException) { return false; }
    }

    private static byte[] DecodeToken(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(base64.PadRight((base64.Length + 3) / 4 * 4, '='));
    }

    private static string NewToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static bool IsPrivateLanAddress(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork) return false;
        var bytes = address.GetAddressBytes();
        return bytes[0] == 10 || bytes[0] == 192 && bytes[1] == 168 ||
               bytes[0] == 172 && bytes[1] is >= 16 and <= 31;
    }

    private static IResult Asset(string name, string contentType)
    {
        var assembly = typeof(WebRemoteHost).Assembly;
        var resourceName = $"ModernTubeDownloader.WebRemote.wwwroot.{name}";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Missing web remote resource: {name}");
        using var reader = new StreamReader(stream);
        return Results.Text(reader.ReadToEnd(), contentType);
    }

    private void SettingsChanged(object? sender, EventArgs e) => _ = ReconcileAsync();

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        stopping = true;
        services.Settings.Changed -= SettingsChanged;
    }

    public sealed record AddRequest(string? Url, string? Quality, string? Container, string? RangeMode, string? From, string? To,
        bool? UseSubtitleDefaults = null, bool? UseSponsorBlockDefaults = null);
    public sealed record LiveAnalyzeRequest(string? Url);
    public sealed record PairRequest(string? Code);
}
