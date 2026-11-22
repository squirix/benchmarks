using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

internal static class Certificates
{
    public static void Create(string directory)
    {
        _ = Directory.CreateDirectory(directory);
        using var rootKey = RSA.Create(2048);
        var rootRequest = new CertificateRequest("CN=Squirix local benchmark root", rootKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        rootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        rootRequest.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(rootRequest.PublicKey, false));
        var now = DateTimeOffset.UtcNow.AddMinutes(-10);
        using var root = rootRequest.CreateSelfSigned(now, now.AddDays(2));
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost");
        names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        using var publicCert = request.Create(root, now, now.AddDays(1), RandomNumberGenerator.GetBytes(16));
        using var cert = publicCert.CopyWithPrivateKey(key);
        File.WriteAllText(Path.Combine(directory, "ca.crt"), root.ExportCertificatePem());
        File.WriteAllText(Path.Combine(directory, "server.crt"), cert.ExportCertificatePem());
        File.WriteAllText(Path.Combine(directory, "server.key"), key.ExportPkcs8PrivateKeyPem());
        File.WriteAllBytes(Path.Combine(directory, "server.pfx"), cert.Export(X509ContentType.Pkcs12, "benchmark-cert"));
    }
}
