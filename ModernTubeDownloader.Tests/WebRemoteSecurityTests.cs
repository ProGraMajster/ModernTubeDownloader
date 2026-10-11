using System.Net;
using System.Net.Http.Json;
using System.Net.Security;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text.Json;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.WebRemote;

namespace ModernTubeDownloader.Tests;

public sealed class WebRemoteSecurityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealListenerPairsWithHttpOnlyCookieChecksCsrfLogoutRestartAndLocalization(bool https)
    {
        var paths = AppPaths.Create(Path.Combine(Path.GetTempPath(), "mtd-pairing-tests", Guid.NewGuid().ToString("N")));
        await WebRemoteIntegrationTests.ConfigureFakeToolsAsync(paths);
        using var app = AppServices.Create(paths);
        try
        {
            app.Queue.SetPaused(true);
            app.Settings.Current.WebRemoteBindAddress = "127.0.0.1";
            app.Settings.Current.WebRemotePort = WebRemoteIntegrationTests.FreeLoopbackPort();
            app.Settings.Current.EnableWebRemote = true;
            app.Settings.Current.WebRemoteUseHttps = https;
            if (https) app.WebRemote.Certificates.Generate(IPAddress.Loopback);
            await app.Settings.SaveAsync();
            await app.WebRemote.ReconcileAsync();
            Assert.True(app.WebRemote.IsRunning, app.WebRemote.LastError);
            Assert.StartsWith(https ? "https://" : "http://", app.WebRemote.Address);
            using var certificate = https ? app.WebRemote.Certificates.Load(IPAddress.Loopback) : null;
            var fingerprint = certificate?.GetCertHashString(HashAlgorithmName.SHA256);
            using var handler = new HttpClientHandler { CookieContainer = new CookieContainer() };
            // Trust only this explicit test certificate, not any self-signed certificate or a name mismatch.
            if (https) handler.ServerCertificateCustomValidationCallback = (_, cert, _, errors) =>
                errors is SslPolicyErrors.None or SslPolicyErrors.RemoteCertificateChainErrors &&
                cert?.GetCertHashString(HashAlgorithmName.SHA256) == fingerprint;
            using var client = new HttpClient(handler) { BaseAddress = new Uri(app.WebRemote.Address!), Timeout = TimeSpan.FromSeconds(15) };
            if (https)
            {
                using var untrusted = new HttpClient { BaseAddress = client.BaseAddress };
                await Assert.ThrowsAsync<HttpRequestException>(() => untrusted.GetAsync("/"));
            }
            foreach (var endpoint in new[] { "/api/status", "/api/queue", "/api/live" })
                Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(endpoint)).StatusCode);
            foreach (var language in new[] { "pl", "en" })
            {
                app.Localization.SetLanguage(language);
                using var bootstrap = JsonDocument.Parse(await client.GetStringAsync("/api/auth-mode"));
                Assert.True(bootstrap.RootElement.GetProperty("authenticationRequired").GetBoolean());
                Assert.Equal(language, bootstrap.RootElement.GetProperty("desktopLanguage").GetString());
                Assert.Equal(new[] { "authenticationRequired", "desktopLanguage" },
                    bootstrap.RootElement.EnumerateObject().Select(property => property.Name).Order().ToArray());
            }
            var code = app.WebRemote.Pairing.Snapshot.Code!;
            var html = await client.GetStringAsync("/");
            Assert.DoesNotContain(code, html);
            Assert.DoesNotContain(app.WebRemote.Token, html);
            Assert.Contains("id=\"pairingCode\"", html);
            var javascript = await client.GetStringAsync("/app.js");
            Assert.DoesNotContain("Authorization", javascript);
            Assert.DoesNotContain("sessionStorage.setItem", javascript);
            Assert.DoesNotContain("localStorage.setItem", javascript);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/pair", new { code })).StatusCode);
            client.DefaultRequestHeaders.Add("Origin", client.BaseAddress.GetLeftPart(UriPartial.Authority));
            client.DefaultRequestHeaders.Add("X-MTD-Request", "1");
            var paired = await client.PostAsJsonAsync("/api/pair", new { code });
            Assert.Equal(HttpStatusCode.OK, paired.StatusCode);
            var setCookie = Assert.Single(paired.Headers.GetValues("Set-Cookie"));
            Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("samesite=strict", setCookie, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(https, setCookie.Contains("secure", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(code, setCookie);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/status")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/pair", new { code })).StatusCode);
            Assert.Equal(PairingCodeState.Used, app.WebRemote.Pairing.Snapshot.State);
            // This reproduces the reported dashboard + unavailable code: a consumed code
            // and an already authenticated browser, not an anonymous auth bypass.
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/status")).StatusCode);
            app.WebRemote.RegeneratePairingCode();
            Assert.Equal(PairingCodeState.Available, app.WebRemote.Pairing.Snapshot.State);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/status")).StatusCode);
            app.Queue.Add(new DownloadQueueItem { VideoId = "localized", Title = "Unicode żółć 日本語", FailureMessageKey = "Error.Download.Network" });
            foreach (var language in new[] { "pl", "en" })
            {
                client.DefaultRequestHeaders.Remove("X-MTD-Language");
                client.DefaultRequestHeaders.Add("X-MTD-Language", language);
                using var response = JsonDocument.Parse(await client.GetStringAsync("/api/queue"));
                var row = Assert.Single(response.RootElement.EnumerateArray());
                var text = new ModernTubeDownloader.Localization.LocalizationService(language);
                Assert.Equal(text["Status.Queued"], row.GetProperty("statusText").GetString());
                Assert.Equal(text["Error.Download.Network"], row.GetProperty("error").GetString());
            }
            client.DefaultRequestHeaders.Remove("X-MTD-Request");
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/api/queue/pause", null)).StatusCode);
            client.DefaultRequestHeaders.Add("X-MTD-Request", "1");
            client.DefaultRequestHeaders.Remove("Origin");
            client.DefaultRequestHeaders.Add("Origin", "https://foreign.example");
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/api/queue/pause", null)).StatusCode);
            client.DefaultRequestHeaders.Remove("Origin");
            client.DefaultRequestHeaders.Add("Origin", client.BaseAddress.GetLeftPart(UriPartial.Authority));
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/logout", null)).StatusCode);
            foreach (var endpoint in new[] { "/api/status", "/api/queue", "/api/live" })
                Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(endpoint)).StatusCode);
            app.WebRemote.RegeneratePairingCode();
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/pair", new { code = app.WebRemote.Pairing.Snapshot.Code })).StatusCode);
            app.Settings.Current.WebRemoteRequireAuthentication = false;
            await app.Settings.SaveAsync(); await app.WebRemote.ReconcileAsync();
            Assert.Null(app.WebRemote.Pairing.Snapshot.Code);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/status")).StatusCode);
            app.Settings.Current.WebRemoteRequireAuthentication = true;
            await app.Settings.SaveAsync(); await app.WebRemote.ReconcileAsync();
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/status")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/pair", new { code = app.WebRemote.Pairing.Snapshot.Code })).StatusCode);
            app.Settings.Current.EnableWebRemote = false;
            await app.Settings.SaveAsync(); await app.WebRemote.ReconcileAsync();
            Assert.False(app.WebRemote.IsRunning); Assert.Null(app.WebRemote.Pairing.Snapshot.Code);
            app.Settings.Current.EnableWebRemote = true;
            await app.Settings.SaveAsync(); await app.WebRemote.ReconcileAsync();
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/status")).StatusCode);
            foreach (var file in Directory.EnumerateFiles(paths.LogDirectory, "*.log"))
            {
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream);
                Assert.DoesNotContain(code, await reader.ReadToEndAsync());
            }
        }
        finally { await app.ShutdownAsync(); app.Dispose(); Directory.Delete(paths.RootDirectory, true); }
    }

    [Fact]
    public void CertificateRequiresExplicitGenerationProtectsPrivateKeyAndExportsOnlyPublicPart()
    {
        var paths = AppPaths.Create(Path.Combine(Path.GetTempPath(), "mtd-cert-tests", Guid.NewGuid().ToString("N")));
        var store = new WebRemoteCertificateStore(paths);
        Assert.Equal("Settings.RemoteCertificateMissing", Assert.Throws<WebRemoteCertificateException>(() => store.Load(IPAddress.Loopback)).MessageKey);
        store.Generate(IPAddress.Loopback);
        using var certificate = store.Load(IPAddress.Loopback);
        var fingerprint = store.Fingerprint(IPAddress.Loopback);
        Assert.True(certificate.HasPrivateKey);
        Assert.Equal(64, fingerprint.Length);
        using var publicCertificate = X509CertificateLoader.LoadCertificate(store.ExportPublic(IPAddress.Loopback));
        Assert.False(publicCertificate.HasPrivateKey);
        Assert.Equal(fingerprint, publicCertificate.GetCertHashString(HashAlgorithmName.SHA256));
        Assert.Throws<CryptographicException>(() => X509CertificateLoader.LoadPkcs12FromFile(store.CertificatePath, null));
        Assert.Equal("Settings.RemoteCertificateMismatch", Assert.Throws<WebRemoteCertificateException>(() => store.Load(IPAddress.Parse("192.168.254.254"))).MessageKey);
        var acl = new DirectoryInfo(store.DirectoryPath).GetAccessControl();
        Assert.True(acl.AreAccessRulesProtected);
        var user = WindowsIdentity.GetCurrent().User!;
        foreach (FileSystemAccessRule rule in acl.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            Assert.True(rule.IdentityReference.Equals(user) || rule.IdentityReference.Equals(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null)));
        store.Generate(IPAddress.Loopback);
        Assert.NotEqual(fingerprint, store.Fingerprint(IPAddress.Loopback));
        Directory.Delete(paths.RootDirectory, true);
    }
}
