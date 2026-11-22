using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace CamBridge.Infrastructure;

public sealed class CertificateProvider : IDisposable
{
    private const string SubjectName = "CN=CamBridge";
    private const string ServerAuthenticationOid = "1.3.6.1.5.5.7.3.1";
    private static readonly TimeSpan Validity = TimeSpan.FromDays(730);
    private static readonly TimeSpan RenewalMargin = TimeSpan.FromDays(7);
    private const X509KeyStorageFlags ServingKeyFlags = X509KeyStorageFlags.UserKeySet;

    private readonly object disposeLock = new();
    private X509Certificate2? certificate;

    private CertificateProvider(X509Certificate2 certificate)
    {
        this.certificate = certificate;
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
    }

    public X509Certificate2 Certificate => certificate ?? throw new ObjectDisposedException(nameof(CertificateProvider));

    public static CertificateProvider Create(NetworkSelection network)
    {
        var pfx = TryLoadReusablePfx(network) ?? GenerateAndStorePfx(network);
        return new CertificateProvider(X509CertificateLoader.LoadPkcs12(pfx, null, ServingKeyFlags));
    }

    public void Dispose()
    {
        lock (disposeLock)
        {
            AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
            certificate?.Dispose();
            certificate = null;
        }
    }

    private void OnProcessExit(object? sender, EventArgs e) => Dispose();

    private static byte[]? TryLoadReusablePfx(NetworkSelection network)
    {
        var path = PortablePaths.CertificateFile;
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var pfx = File.ReadAllBytes(path);
            using var inspected = X509CertificateLoader.LoadPkcs12(pfx, null, X509KeyStorageFlags.EphemeralKeySet);
            return IsReusable(inspected, network) ? pfx : null;
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool IsReusable(X509Certificate2 candidate, NetworkSelection network)
    {
        var now = DateTime.Now;
        if (!candidate.HasPrivateKey || candidate.NotBefore > now || candidate.NotAfter < now.Add(RenewalMargin))
        {
            return false;
        }

        var sanExtension = candidate.Extensions.OfType<X509SubjectAlternativeNameExtension>().FirstOrDefault();
        if (sanExtension is null)
        {
            return false;
        }

        var addresses = sanExtension.EnumerateIPAddresses().ToHashSet();
        return addresses.Contains(network.PrimaryAddress) && addresses.Contains(IPAddress.Loopback);
    }

    private static byte[] GenerateAndStorePfx(NetworkSelection network)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(SubjectName, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(BuildSubjectAlternativeNames(network));
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new Oid(ServerAuthenticationOid) }, false));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        var notBefore = DateTimeOffset.UtcNow.AddDays(-1);
        using var generated = request.CreateSelfSigned(notBefore, notBefore.Add(Validity));
        var pfx = generated.Export(X509ContentType.Pfx);
        PortableFileWriter.TryWrite(PortablePaths.CertificateFile, pfx);
        return pfx;
    }

    private static X509Extension BuildSubjectAlternativeNames(NetworkSelection network)
    {
        var builder = new SubjectAlternativeNameBuilder();
        builder.AddDnsName("localhost");
        builder.AddIpAddress(IPAddress.Loopback);
        foreach (var address in network.AllAddresses.Append(network.PrimaryAddress).Distinct())
        {
            if (!address.Equals(IPAddress.Loopback))
            {
                builder.AddIpAddress(address);
            }
        }

        return builder.Build();
    }
}
