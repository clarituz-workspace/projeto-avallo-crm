using System.Security.Cryptography;
using System.Text;

namespace Avallo.Connector.Shopee;

public static class ShopeeSignature
{
    public static string CreatePublic(long partnerId, string path, long timestamp, string partnerKey) =>
        Sign($"{partnerId}{path}{timestamp}", partnerKey);

    public static string CreateShop(long partnerId, string path, long timestamp, string accessToken, long shopId, string partnerKey) =>
        Sign($"{partnerId}{path}{timestamp}{accessToken}{shopId}", partnerKey);

    private static string Sign(string value, string partnerKey)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(partnerKey));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }
}
