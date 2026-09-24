using System.ComponentModel.DataAnnotations;

namespace Avallo.Connector.Shopee;

public sealed class ShopeeOptions
{
    public const string SectionName = "Connectors:Shopee";
    [Range(1, long.MaxValue)] public long PartnerId { get; init; }
    [Required] public string PartnerKey { get; init; } = string.Empty;
    [Required, Url] public string RedirectUri { get; init; } = string.Empty;
    [Required, Url] public string BaseUrl { get; init; } = "https://partner.shopeemobile.com";
}
