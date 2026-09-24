using System.ComponentModel.DataAnnotations;

namespace Avallo.Connector.Amazon;

public sealed class AmazonOptions
{
    public const string SectionName = "Connectors:Amazon";
    [Required] public string ClientId { get; init; } = string.Empty;
    [Required] public string ClientSecret { get; init; } = string.Empty;
    [Required] public string ApplicationId { get; init; } = string.Empty;
    [Required, Url] public string RedirectUri { get; init; } = string.Empty;
    [Required] public string AwsAccessKeyId { get; init; } = string.Empty;
    [Required] public string AwsSecretAccessKey { get; init; } = string.Empty;
    public string? AwsSessionToken { get; init; }
    public string Region { get; init; } = "us-east-1";
    public string ApiBaseUrl { get; init; } = "https://sellingpartnerapi-na.amazon.com";
    public string AuthorizationBaseUrl { get; init; } = "https://sellercentral.amazon.com.br";
    public string MarketplaceId { get; init; } = "A2Q3Y263D00KWC";
}
