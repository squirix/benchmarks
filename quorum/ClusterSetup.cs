using System;
using System.Globalization;
using System.IO;
using System.IO.Hashing;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

internal static class ClusterSetup
{
    private const string Issuer = "https://quorum-benchmark.invalid";
    private const string Audience = "squirix-benchmark";
    internal const string CertificatePassword = "benchmark-cert";

    internal static string[] SelectKeys()
    {
        // This is the route encoding and consistent-hash ring of Squirix v0.2.0-preview.1.
        const string cache = "local-benchmark";
        var ring = "ABC".SelectMany(static node => Enumerable.Range(0, 128)
                .Select(index => (Hash: Hash(FormattableString.Invariant($"{node}#{index}")), Node: node)))
            .OrderBy(static point => point.Hash).ToArray();
        var keys = new string[1_024];
        for (int candidate = 0, count = 0; count < keys.Length; candidate++)
        {
            var key = "key:" + candidate.ToString("D12", CultureInfo.InvariantCulture);
            var hash = Hash(FormattableString.Invariant($"{cache.Length}:{cache}\u001f{key}"));
            var position = Array.FindIndex(ring, point => point.Hash >= hash);
            if (ring[position < 0 ? 0 : position].Node == 'A')
                keys[count++] = key;
        }
        return keys;
    }

    internal static void Create(string directory)
    {
        var certs = Path.Combine(directory, "certs");
        _ = Directory.CreateDirectory(certs);
        using var rootKey = RSA.Create(2048);
        var rootRequest = new CertificateRequest("CN=Local quorum benchmark CA", rootKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 0, true));
        rootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        rootRequest.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(rootRequest.PublicKey, false));
        var now = DateTimeOffset.UtcNow;
        using var root = rootRequest.CreateSelfSigned(now.AddMinutes(-10), now.AddDays(2));
        File.WriteAllText(Path.Combine(certs, "ca.crt"), root.ExportCertificatePem());
        foreach (var node in "ABC")
            CreateNode(certs, node, root, now);

        var key = RandomNumberGenerator.GetBytes(32);
        File.WriteAllLines(Path.Combine(directory, "squirix.env"),
            [$"SQUIRIX_JWT_SIGNING_KEY={Convert.ToBase64String(key)}", $"SQUIRIX_JWT_ISSUER={Issuer}", $"SQUIRIX_JWT_AUDIENCE={Audience}"]);
        var header = Base64Url("""{"alg":"HS256","typ":"JWT"}"""u8);
        var claims = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new
        {
            iss = Issuer, aud = Audience, nbf = now.AddMinutes(-5).ToUnixTimeSeconds(), exp = now.AddHours(6).ToUnixTimeSeconds(),
        }));
        var signedContent = header + "." + claims;
        var signature = Base64Url(HMACSHA256.HashData(key, Encoding.ASCII.GetBytes(signedContent)));
        File.WriteAllText(Path.Combine(directory, "token.txt"), signedContent + "." + signature);
    }

    private static void CreateNode(string output, char node, X509Certificate2 root, DateTimeOffset now)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest($"CN={node}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName($"node-{char.ToLowerInvariant(node)}");
        names.AddDnsName("localhost");
        names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection
        {
            new("1.3.6.1.5.5.7.3.1"), new("1.3.6.1.5.5.7.3.2"),
        }, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        using var publicCert = request.Create(root, now.AddMinutes(-5), now.AddDays(1), RandomNumberGenerator.GetBytes(16));
        using var cert = publicCert.CopyWithPrivateKey(key);
        File.WriteAllText(Path.Combine(output, $"{node}.crt"), cert.ExportCertificatePem());
        File.WriteAllText(Path.Combine(output, $"{node}.key"), key.ExportPkcs8PrivateKeyPem());
        File.WriteAllBytes(Path.Combine(output, $"{node}.pfx"), cert.Export(X509ContentType.Pkcs12, CertificatePassword));
    }

    private static ulong Hash(string input) => XxHash64.HashToUInt64(Encoding.UTF8.GetBytes(input));
    private static string Base64Url(ReadOnlySpan<byte> value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
