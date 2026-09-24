using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Avallo.Client.Models;

namespace Avallo.Client.Services;

public sealed class GeoLocationService
{
    private static readonly HttpClient SharedHttp = new();

    public async Task<GeoLocationModel?> GetLocationAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await SharedHttp.GetFromJsonAsync<IpApiResult>(
                "https://ipapi.co/json/", cancellationToken);

            if (response is null)
                return null;

            return new GeoLocationModel
            {
                Ip = response.Ip ?? string.Empty,
                City = response.City ?? string.Empty,
                Region = response.Region ?? string.Empty,
                CountryName = response.CountryName ?? string.Empty,
                Timezone = response.Timezone ?? string.Empty,
                Continent = response.Continent ?? string.Empty
            };
        }
        catch
        {
            return null;
        }
    }

    private sealed class IpApiResult
    {
        [JsonPropertyName("ip")]
        public string? Ip { get; set; }

        [JsonPropertyName("city")]
        public string? City { get; set; }

        [JsonPropertyName("region")]
        public string? Region { get; set; }

        [JsonPropertyName("country_name")]
        public string? CountryName { get; set; }

        [JsonPropertyName("continent")]
        public string? Continent { get; set; }

        [JsonPropertyName("timezone")]
        public string? Timezone { get; set; }
    }
}
