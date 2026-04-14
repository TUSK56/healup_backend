using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;

namespace HealUp.Api.Services;

public class GoogleMapsService
{
    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;

    public GoogleMapsService(IConfiguration configuration)
    {
        _httpClient = new HttpClient();
        _apiKey = configuration["GoogleMaps:ApiKey"];
    }

    public async Task<double?> GetDistanceKmAsync(double? lat1, double? lon1, double? lat2, double? lon2, CancellationToken cancellationToken = default)
    {
        if (lat1 is null || lon1 is null || lat2 is null || lon2 is null)
            return null;

        if (!string.IsNullOrWhiteSpace(_apiKey))
        {
            var url =
                $"https://maps.googleapis.com/maps/api/distancematrix/json?origins={lat1},{lon1}&destinations={lat2},{lon2}&key={_apiKey}";
            var response = await _httpClient.GetFromJsonAsync<DistanceMatrixResponse>(url, cancellationToken);
            var meters = response?.Rows?.FirstOrDefault()?.Elements?.FirstOrDefault()?.Distance?.Value;
            if (meters.HasValue)
            {
                return meters.Value / 1000d;
            }
        }

        return HaversineKm(lat1.Value, lon1.Value, lat2.Value, lon2.Value);
    }

    private static double HaversineKm(double lat1, double lon1, double lat2, double lon2)
    {
        const double r = 6371;
        var dLat = DegreesToRadians(lat2 - lat1);
        var dLon = DegreesToRadians(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(DegreesToRadians(lat1)) * Math.Cos(DegreesToRadians(lat2)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return r * c;
    }

    private static double DegreesToRadians(double deg) => deg * (Math.PI / 180);

    private sealed class DistanceMatrixResponse
    {
        public Row[]? Rows { get; set; }
    }

    private sealed class Row
    {
        public Element[]? Elements { get; set; }
    }

    private sealed class Element
    {
        public Distance? Distance { get; set; }
    }

    private sealed class Distance
    {
        public int Value { get; set; }
    }
}

