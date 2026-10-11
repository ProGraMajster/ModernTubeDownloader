using System.Net;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using Microsoft.AspNetCore.DataProtection;
using ModernTubeDownloader.Infrastructure;

namespace ModernTubeDownloader.WebRemote;

/// <summary>Explicitly generated LAN server certificate, protected by per-user DPAPI and a restricted directory ACL.</summary>
public sealed class WebRemoteCertificateStore(AppPaths paths)
{
    private readonly object sync = new();
    public string DirectoryPath => Path.Combine(paths.RootDirectory, "WebRemote");
    public string CertificatePath => Path.Combine(DirectoryPath, "certificate.protected");

    public X509Certificate2 Load(IPAddress address)
    {
        lock (sync)
        {
            if (!File.Exists(CertificatePath)) throw new WebRemoteCertificateException("Settings.RemoteCertificateMissing");
            try
            {
                ProtectDirectory();
                var raw = Protector().Unprotect(File.ReadAllBytes(CertificatePath));
                X509Certificate2 certificate;
                // Windows Schannel needs a per-user key container during the TLS listener lifetime.
                // Do not use PersistKeySet: disposing the certificate removes this temporary imported key.
                // The stored certificate remains DPAPI-protected; no plaintext PFX is written to disk.
                try { certificate = X509CertificateLoader.LoadPkcs12(raw, null, X509KeyStorageFlags.UserKeySet); }
                finally { CryptographicOperations.ZeroMemory(raw); }
                var san = certificate.Extensions["2.5.29.17"];
                var matches = san is not null && new X509SubjectAlternativeNameExtension(san.RawData, san.Critical)
                    .EnumerateIPAddresses().Contains(address);
                if (!certificate.HasPrivateKey || !matches || certificate.NotAfter.ToUniversalTime() <= DateTime.UtcNow ||
                    certificate.NotBefore.ToUniversalTime() > DateTime.UtcNow)
                {
                    certificate.Dispose();
                    throw new WebRemoteCertificateException("Settings.RemoteCertificateMismatch");
                }
                return certificate;
            }
            catch (WebRemoteCertificateException) { throw; }
            catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
            { throw new WebRemoteCertificateException("Settings.RemoteCertificateCorrupt", ex); }
        }
    }

    /// <summary>Replaces the certificate only on an explicit user action. Never changes trust stores or firewall rules.</summary>
    public void Generate(IPAddress address)
    {
        lock (sync)
        {
            if (!WebRemoteHost.AvailableAddresses().Contains(address.ToString(), StringComparer.Ordinal))
                throw new WebRemoteCertificateException("Settings.RemoteInvalidBind");
            ProtectDirectory();
            using var key = RSA.Create(2048);
            var request = new CertificateRequest("CN=ModernTubeDownloader Web Remote", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var san = new SubjectAlternativeNameBuilder();
            san.AddIpAddress(address);
            if (IPAddress.IsLoopback(address)) san.AddDnsName("localhost");
            request.CertificateExtensions.Add(san.Build());
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, true));
            using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(365));
            var raw = certificate.Export(X509ContentType.Pkcs12);
            try
            {
                var temporary = CertificatePath + ".tmp";
                File.WriteAllBytes(temporary, Protector().Protect(raw));
                File.Move(temporary, CertificatePath, true);
            }
            finally { CryptographicOperations.ZeroMemory(raw); }
        }
    }

    public byte[] ExportPublic(IPAddress address)
    {
        using var certificate = Load(address);
        return certificate.Export(X509ContentType.Cert); // Public certificate only; never exports the private key.
    }

    public string Fingerprint(IPAddress address)
    {
        using var certificate = Load(address);
        return certificate.GetCertHashString(HashAlgorithmName.SHA256);
    }

    private IDataProtector Protector()
    {
        var keys = new DirectoryInfo(Path.Combine(DirectoryPath, "keys"));
        return DataProtectionProvider.Create(keys, options => options.SetApplicationName("ModernTubeDownloader.WebRemote")
            .ProtectKeysWithDpapi()).CreateProtector("certificate.v1");
    }

    private void ProtectDirectory()
    {
        Directory.CreateDirectory(DirectoryPath);
        var user = WindowsIdentity.GetCurrent().User ?? throw new UnauthorizedAccessException("Missing Windows identity.");
        var acl = new DirectorySecurity();
        acl.SetOwner(user);
        acl.SetAccessRuleProtection(true, false);
        foreach (var sid in new[] { user, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null) })
            acl.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(DirectoryPath).SetAccessControl(acl);
    }
}

public sealed class WebRemoteCertificateException(string messageKey, Exception? inner = null) : Exception(messageKey, inner)
{
    public string MessageKey { get; } = messageKey;
}
