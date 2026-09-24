using System.Security.Cryptography;
using System.Text;

namespace Avallo.Connector.Amazon;

internal static class AmazonSignature
{
    public static string Sign(string method, Uri uri, IReadOnlyDictionary<string, string> headers,
        string payloadHash, string accessKey, string secretKey, string region, string service, DateTimeOffset now)
    {
        var amzDate = now.UtcDateTime.ToString("yyyyMMddTHHmmssZ");
        var date = now.UtcDateTime.ToString("yyyyMMdd");
        var canonicalHeaders = headers.OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => $"{x.Key.ToLowerInvariant()}:{x.Value.Trim()}\n");
        var signedHeaders = string.Join(';', headers.Keys.OrderBy(x => x, StringComparer.Ordinal).Select(x => x.ToLowerInvariant()));
        var canonical = string.Join("\n", method, uri.AbsolutePath, uri.Query.TrimStart('?'),
            string.Concat(canonicalHeaders), signedHeaders, payloadHash);
        var scope = $"{date}/{region}/{service}/aws4_request";
        var stringToSign = $"AWS4-HMAC-SHA256\n{amzDate}\n{scope}\n{Hash(canonical)}";
        var signingKey = Hmac(Hmac(Hmac(Hmac(Encoding.UTF8.GetBytes("AWS4" + secretKey), date), region), service), "aws4_request");
        return $"AWS4-HMAC-SHA256 Credential={accessKey}/{scope}, SignedHeaders={signedHeaders}, Signature={Convert.ToHexString(Hmac(signingKey, stringToSign)).ToLowerInvariant()}";
    }

    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static byte[] Hmac(byte[] key, string value) => Hmac(key, Encoding.UTF8.GetBytes(value));
    private static byte[] Hmac(byte[] key, byte[] value) => HMACSHA256.HashData(key, value);
}
