namespace Avallo.Client.Models;

public sealed class GeoLocationModel
{
    public string Ip { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string CountryName { get; set; } = string.Empty;
    public string Timezone { get; set; } = string.Empty;
    public string Continent { get; set; } = string.Empty;
}
