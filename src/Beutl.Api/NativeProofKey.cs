using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Beutl.Api;

internal static class NativeProofKey
{
    public static string Create()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return Convert.ToBase64String(key.ExportPkcs8PrivateKey());
    }

    public static string Thumbprint(string privateKey)
    {
        using ECDsa key = Import(privateKey);
        return Hash(JsonSerializer.Serialize(PublicKey(key)));
    }

    public static string Sign(string privateKey, HttpMethod method, Uri uri, string? accessToken = null)
    {
        using ECDsa key = Import(privateKey);
        string header = Encode(JsonSerializer.SerializeToUtf8Bytes(new { typ = "dpop+jwt", alg = "ES256", jwk = PublicKey(key) }));
        var claims = new Dictionary<string, object>
        {
            ["htm"] = method.Method,
            ["htu"] = new UriBuilder(uri) { Query = "", Fragment = "" }.Uri.AbsoluteUri,
            ["iat"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ["jti"] = Guid.NewGuid().ToString("N"),
        };
        if (accessToken is not null) claims["ath"] = Hash(accessToken);
        string payload = Encode(JsonSerializer.SerializeToUtf8Bytes(claims));
        string input = $"{header}.{payload}";
        byte[] signature = key.SignData(Encoding.ASCII.GetBytes(input), HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return $"{input}.{Encode(signature)}";
    }

    public static string Hash(string value) => Encode(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public static string Encode(ReadOnlySpan<byte> value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static ECDsa Import(string privateKey)
    {
        var key = ECDsa.Create();
        try
        {
            byte[] bytes = Convert.FromBase64String(privateKey);
            try
            {
                key.ImportPkcs8PrivateKey(bytes, out int read);
                if (read != bytes.Length || key.KeySize != 256) throw new CryptographicException("Invalid device key.");
            }
            finally { CryptographicOperations.ZeroMemory(bytes); }
            return key;
        }
        catch { key.Dispose(); throw; }
    }

    private static object PublicKey(ECDsa key)
    {
        ECParameters parameters = key.ExportParameters(false);
        // RFC 7638 requires lexicographically ordered public JWK members.
        return new { crv = "P-256", kty = "EC", x = Encode(parameters.Q.X!), y = Encode(parameters.Q.Y!) };
    }
}
