using System.ComponentModel.DataAnnotations;

namespace Avallo.Connector.MercadoLivre;

public sealed class MercadoLivreOptions
{
    public const string SectionName = "Connectors:MercadoLivre";
    [Required] public string ClientId { get; init; } = string.Empty;
    [Required] public string ClientSecret { get; init; } = string.Empty;
    [Required, Url] public string RedirectUri { get; init; } = string.Empty;
}
