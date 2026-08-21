using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Collections.Concurrent;
using HavaIziSimulator.Mcp.Spatial.Models;

namespace HavaIziSimulator.Mcp.Spatial.Services;

public interface IGeocodingClient
{
    Task<GeocodingFeature> ResolveAsync(
        string query,
        bool requireArea,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// OpenStreetMap Nominatim Search API üzerinden serbest yer adını dinamik çözer.
/// Şehir/bölge koordinatları uygulama kodunda tutulmaz.
/// </summary>
public sealed class NominatimGeocodingClient : IGeocodingClient, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;
    private readonly ConcurrentDictionary<string, GeocodingFeature> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private DateTimeOffset _lastRequest = DateTimeOffset.MinValue;

    public NominatimGeocodingClient(HttpClient? httpClient = null)
    {
        _ownsClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.BaseAddress ??= new Uri("https://nominatim.openstreetmap.org/");
        _httpClient.Timeout = TimeSpan.FromSeconds(20);
        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
            _httpClient.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("HavaIziSimulator", "1.0"));
        _httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    }

    public async Task<GeocodingFeature> ResolveAsync(
        string query,
        bool requireArea,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("Yer adı boş olamaz.", nameof(query));
        string cacheKey = $"{requireArea}:{query.Trim()}";
        if (_cache.TryGetValue(cacheKey, out GeocodingFeature? cached))
            return cached;

        string uri = "search?format=jsonv2&polygon_geojson=1&addressdetails=1&limit=5" +
                     $"&accept-language=tr&q={Uri.EscapeDataString(query.Trim())}";
        await _requestGate.WaitAsync(cancellationToken);
        string body;
        HttpStatusCode statusCode;
        try
        {
            TimeSpan wait = TimeSpan.FromSeconds(1) - (DateTimeOffset.UtcNow - _lastRequest);
            if (wait > TimeSpan.Zero) await Task.Delay(wait, cancellationToken);
            using HttpResponseMessage response = await _httpClient.GetAsync(uri, cancellationToken);
            _lastRequest = DateTimeOffset.UtcNow;
            statusCode = response.StatusCode;
            body = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        finally
        {
            _requestGate.Release();
        }
        if ((int)statusCode is < 200 or >= 300)
            throw new InvalidOperationException(
                $"Konum servisi HTTP {(int)statusCode}: {Shorten(body)}");

        using JsonDocument document = JsonDocument.Parse(body);
        if (document.RootElement.ValueKind != JsonValueKind.Array ||
            document.RootElement.GetArrayLength() == 0)
            throw new InvalidOperationException($"Konum bulunamadı: {query}");

        JsonElement? selected = null;
        JsonElement? first = null;
        foreach (JsonElement candidate in document.RootElement.EnumerateArray())
        {
            first ??= candidate;
            if (!requireArea || IsAreaGeometry(candidate))
            {
                selected = candidate;
                break;
            }
        }
        selected ??= first;

        JsonElement item = selected.Value;
        double latitude = ReadDoubleString(item, "lat");
        double longitude = ReadDoubleString(item, "lon");
        double[] box = item.GetProperty("boundingbox")
            .EnumerateArray()
            .Select(x => double.Parse(x.GetString()!, CultureInfo.InvariantCulture))
            .ToArray();

        JsonElement geometry = requireArea && !IsAreaGeometry(item)
            ? BoundingBoxPolygon(box)
            : item.TryGetProperty("geojson", out JsonElement geoJson)
            ? geoJson.Clone()
            : JsonSerializer.SerializeToElement(new
            {
                type = "Point",
                coordinates = new[] { longitude, latitude }
            });

        var feature = new GeocodingFeature(
            item.GetProperty("display_name").GetString() ?? query,
            latitude,
            longitude,
            box[0], box[1], box[2], box[3], geometry);
        _cache[cacheKey] = feature;
        return feature;
    }

    private static JsonElement BoundingBoxPolygon(double[] box) =>
        JsonSerializer.SerializeToElement(new
        {
            type = "Polygon",
            coordinates = new[]
            {
                new[]
                {
                    new[] { box[2], box[0] }, new[] { box[3], box[0] },
                    new[] { box[3], box[1] }, new[] { box[2], box[1] },
                    new[] { box[2], box[0] }
                }
            }
        });

    private static bool IsAreaGeometry(JsonElement item)
    {
        if (!item.TryGetProperty("geojson", out JsonElement geometry) ||
            !geometry.TryGetProperty("type", out JsonElement type))
            return false;
        return type.GetString() is "Polygon" or "MultiPolygon";
    }

    private static double ReadDoubleString(JsonElement item, string name) =>
        double.Parse(item.GetProperty(name).GetString()!, CultureInfo.InvariantCulture);

    private static string Shorten(string text) => text.Length <= 300 ? text : text[..300];

    public void Dispose()
    {
        if (_ownsClient) _httpClient.Dispose();
        _requestGate.Dispose();
    }
}
