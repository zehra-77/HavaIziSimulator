using System.Text.Json;
using HavaIziSimulator.Mcp.Spatial.Models;

namespace HavaIziSimulator.Mcp.Spatial.Services;

public sealed class SpatialGeometryService
{
    private readonly IGeocodingClient _geocodingClient;

    public SpatialGeometryService(IGeocodingClient geocodingClient)
    {
        _geocodingClient = geocodingClient;
    }

    public async Task<ResolvedSpatialScope> ResolveAsync(
        JsonElement scope,
        CancellationToken cancellationToken = default)
    {
        if (scope.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("scope bir JSON nesnesi olmalıdır.");

        EnsureAllowed(scope, "type", "placeName", "radiusKm");
        string type = RequiredString(scope, "type").ToUpperInvariant();

        return type switch
        {
            "REGION" => await ResolveRegionAsync(scope, cancellationToken),
            "POINT" => await ResolvePointAsync(scope, cancellationToken),
            "RADIUS" => await ResolveRadiusAsync(scope, cancellationToken),
            _ => throw new ArgumentException(
                "scope.type REGION, POINT veya RADIUS olmalıdır.")
        };
    }

    public bool Contains(ResolvedSpatialScope scope, double latitude, double longitude) =>
        scope.Type switch
        {
            "REGION" => ContainsGeoJson(scope.Primary.Geometry, latitude, longitude),
            "POINT" => HaversineKm(latitude, longitude,
                scope.Primary.Latitude, scope.Primary.Longitude) <= 1.0,
            "RADIUS" => HaversineKm(latitude, longitude,
                scope.Primary.Latitude, scope.Primary.Longitude) <= scope.RadiusKm!.Value,
            _ => false
        };

    public GeoPoint RandomPoint(ResolvedSpatialScope scope) => scope.Type switch
    {
        "REGION" => RandomPointInRegion(scope.Primary),
        "POINT" => new GeoPoint(scope.Primary.Latitude, scope.Primary.Longitude),
        "RADIUS" => RandomPointInRadius(scope.Primary, scope.RadiusKm!.Value),
        _ => throw new InvalidOperationException($"Desteklenmeyen scope: {scope.Type}")
    };

    public object Describe(ResolvedSpatialScope scope) => new
    {
        type = scope.Type,
        displayName = scope.DisplayName,
        center = new { latitude = scope.Primary.Latitude, longitude = scope.Primary.Longitude },
        radiusKm = scope.RadiusKm,
        geocoder = "OpenStreetMap Nominatim"
    };

    private async Task<ResolvedSpatialScope> ResolveRegionAsync(
        JsonElement scope, CancellationToken cancellationToken)
    {
        string name = RequiredString(scope, "placeName");
        GeocodingFeature feature = await _geocodingClient.ResolveAsync(name, true, cancellationToken);
        return new ResolvedSpatialScope("REGION", feature.DisplayName, feature);
    }

    private async Task<ResolvedSpatialScope> ResolvePointAsync(
        JsonElement scope, CancellationToken cancellationToken)
    {
        string name = RequiredString(scope, "placeName");
        GeocodingFeature feature = await _geocodingClient.ResolveAsync(name, false, cancellationToken);
        return new ResolvedSpatialScope("POINT", feature.DisplayName, feature);
    }

    private async Task<ResolvedSpatialScope> ResolveRadiusAsync(
        JsonElement scope, CancellationToken cancellationToken)
    {
        string name = RequiredString(scope, "placeName");
        double radius = RequiredPositive(scope, "radiusKm", 20_000);
        GeocodingFeature feature = await _geocodingClient.ResolveAsync(name, false, cancellationToken);
        return new ResolvedSpatialScope("RADIUS", feature.DisplayName, feature, RadiusKm: radius);
    }

    private static GeoPoint RandomPointInRegion(GeocodingFeature feature)
    {
        for (int attempt = 0; attempt < 20_000; attempt++)
        {
            double latitude = feature.South + Random.Shared.NextDouble() * (feature.North - feature.South);
            double longitude = feature.West + Random.Shared.NextDouble() * (feature.East - feature.West);
            if (ContainsGeoJson(feature.Geometry, latitude, longitude))
                return new GeoPoint(latitude, longitude);
        }
        throw new InvalidOperationException(
            $"{feature.DisplayName} polygonu içinde rastgele nokta üretilemedi.");
    }

    private static GeoPoint RandomPointInRadius(GeocodingFeature center, double radiusKm)
    {
        double distance = radiusKm * Math.Sqrt(Random.Shared.NextDouble());
        double angle = Random.Shared.NextDouble() * Math.PI * 2;
        double latitude = center.Latitude + Math.Sin(angle) * distance / 110.574;
        double lonScale = 111.320 * Math.Max(0.01, Math.Cos(center.Latitude * Math.PI / 180));
        double longitude = center.Longitude + Math.Cos(angle) * distance / lonScale;
        return new GeoPoint(Math.Clamp(latitude, -90, 90), NormalizeLongitude(longitude));
    }

    private static bool ContainsGeoJson(JsonElement geometry, double latitude, double longitude)
    {
        string type = geometry.GetProperty("type").GetString() ?? string.Empty;
        JsonElement coordinates = geometry.GetProperty("coordinates");
        return type switch
        {
            "Polygon" => ContainsPolygon(coordinates, latitude, longitude),
            "MultiPolygon" => coordinates.EnumerateArray()
                .Any(polygon => ContainsPolygon(polygon, latitude, longitude)),
            "Point" => HaversineKm(
                latitude, longitude,
                coordinates[1].GetDouble(), coordinates[0].GetDouble()) <= 1.0,
            _ => throw new InvalidOperationException($"Desteklenmeyen GeoJSON tipi: {type}")
        };
    }

    private static bool ContainsPolygon(JsonElement rings, double latitude, double longitude)
    {
        JsonElement.ArrayEnumerator enumerator = rings.EnumerateArray();
        if (!enumerator.MoveNext() || !PointInRing(enumerator.Current, latitude, longitude))
            return false;
        while (enumerator.MoveNext())
            if (PointInRing(enumerator.Current, latitude, longitude)) return false;
        return true;
    }

    private static bool PointInRing(JsonElement ring, double latitude, double longitude)
    {
        (double X, double Y)[] points = ring.EnumerateArray()
            .Select(p => (p[0].GetDouble(), p[1].GetDouble()))
            .ToArray();
        bool inside = false;
        for (int i = 0, j = points.Length - 1; i < points.Length; j = i++)
        {
            (double xi, double yi) = points[i];
            (double xj, double yj) = points[j];
            bool crosses = (yi > latitude) != (yj > latitude) &&
                           longitude < (xj - xi) * (latitude - yi) /
                           ((yj - yi) == 0 ? double.Epsilon : (yj - yi)) + xi;
            if (crosses) inside = !inside;
        }
        return inside;
    }

    private static double HaversineKm(double lat1, double lon1, double lat2, double lon2)
    {
        const double earthRadius = 6371.0088;
        double dLat = (lat2 - lat1) * Math.PI / 180;
        double dLon = (lon2 - lon1) * Math.PI / 180;
        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                   Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) *
                   Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * earthRadius * Math.Asin(Math.Sqrt(a));
    }

    private static double NormalizeLongitude(double longitude)
    {
        while (longitude > 180) longitude -= 360;
        while (longitude < -180) longitude += 360;
        return longitude;
    }

    private static string RequiredString(JsonElement value, string name) =>
        value.TryGetProperty(name, out JsonElement property) &&
        property.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(property.GetString())
            ? property.GetString()!
            : throw new ArgumentException($"scope.{name} zorunlu bir metindir.");

    private static double RequiredPositive(JsonElement value, string name, double max) =>
        value.TryGetProperty(name, out JsonElement property) &&
        property.ValueKind == JsonValueKind.Number &&
        property.TryGetDouble(out double number) && number > 0 && number <= max
            ? number
            : throw new ArgumentException($"scope.{name} 0-{max} arasında olmalıdır.");

    private static void EnsureAllowed(JsonElement value, params string[] allowed)
    {
        HashSet<string> names = allowed.ToHashSet(StringComparer.Ordinal);
        string[] unexpected = value.EnumerateObject()
            .Select(x => x.Name)
            .Where(x => !names.Contains(x))
            .ToArray();
        if (unexpected.Length > 0)
            throw new ArgumentException($"Beklenmeyen scope alanı: {string.Join(", ", unexpected)}");
    }
}
