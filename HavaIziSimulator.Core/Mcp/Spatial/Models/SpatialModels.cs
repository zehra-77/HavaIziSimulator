using System.Text.Json;

namespace HavaIziSimulator.Mcp.Spatial.Models;

public sealed record GeocodingFeature(
    string DisplayName,
    double Latitude,
    double Longitude,
    double South,
    double North,
    double West,
    double East,
    JsonElement Geometry);

public sealed record ResolvedSpatialScope(
    string Type,
    string DisplayName,
    GeocodingFeature Primary,
    double? RadiusKm = null);

public sealed record GeoPoint(double Latitude, double Longitude);
