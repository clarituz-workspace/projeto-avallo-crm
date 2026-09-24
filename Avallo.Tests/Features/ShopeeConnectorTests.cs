using System.Security.Cryptography;
using System.Text;
using Avallo.Connector.Shopee;
using Xunit;

namespace Avallo.Tests.Features;

public sealed class ShopeeConnectorTests
{
    [Fact]
    public void Public_signature_uses_partner_path_and_timestamp()
    {
        const long partnerId = 123456;
        const long timestamp = 1700000000;
        const string path = "/api/v2/shop/auth_partner";
        const string key = "sandbox-partner-key";

        var expected = Hmac($"{partnerId}{path}{timestamp}", key);

        Assert.Equal(expected, ShopeeSignature.CreatePublic(partnerId, path, timestamp, key));
    }

    [Fact]
    public void Shop_signature_appends_access_token_and_shop_id()
    {
        const long partnerId = 123456;
        const long timestamp = 1700000000;
        const long shopId = 987654;
        const string path = "/api/v2/order/get_order_list";
        const string token = "sandbox-access-token";
        const string key = "sandbox-partner-key";

        var expected = Hmac($"{partnerId}{path}{timestamp}{token}{shopId}", key);

        Assert.Equal(expected, ShopeeSignature.CreateShop(partnerId, path, timestamp, token, shopId, key));
    }

    private static string Hmac(string value, string key)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }
}
