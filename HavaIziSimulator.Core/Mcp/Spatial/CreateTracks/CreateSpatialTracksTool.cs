using System.Text.Json;
using HavaIziSimulator.Llm;
using HavaIziSimulator.Mcp.Abstractions;
using HavaIziSimulator.Mcp.Radar.Shared;
using HavaIziSimulator.Mcp.Spatial.Services;
using HavaIziSimulator.Mcp.Spatial.Shared;

namespace HavaIziSimulator.Mcp.Spatial.CreateTracks;

public sealed class CreateSpatialTracksTool : McpToolBase
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly SpatialGeometryService _geometry;

    public CreateSpatialTracksTool(SpatialGeometryService geometry) => _geometry = geometry;

    public override string Name => "radar_create_tracks_spatial";
    public override string Description => "Yer adıyla dinamik çözülen şehir/bölge, merkez noktası veya merkez-yarıçap içinde bir veya birden fazla iz grubu oluşturur. Koordinat uydurma.";
    protected override string SchemaJson => SpatialSchemas.Create;

    public override async Task<McpCallResult> ExecuteAsync(
        JsonElement arguments, McpToolContext context, CancellationToken cancellationToken = default)
    {
        EnsureOnly(arguments, "scope", "groups");
        var scope = await _geometry.ResolveAsync(arguments.GetProperty("scope"), cancellationToken);
        JsonElement createArguments = JsonSerializer.SerializeToElement(new
        {
            groups = arguments.GetProperty("groups").Clone()
        });
        List<RadarScenarioDto> actions = RadarToolOperations.CreateTracks(createArguments, context.ActiveTracks);
        foreach (RadarScenarioDto action in actions)
        {
            TrackPayloadDto payload = action.Payload.Deserialize<TrackPayloadDto>(JsonOptions)
                ?? throw new InvalidOperationException("Oluşturulan iz payload'ı okunamadı.");
            var point = _geometry.RandomPoint(scope);
            payload.Enlem = point.Latitude;
            payload.Boylam = point.Longitude;
            action.Payload = JsonSerializer.SerializeToElement(payload);
        }

        return new McpCallResult
        {
            RadarActions = actions,
            StructuredContent = JsonSerializer.SerializeToElement(new
            {
                scope = _geometry.Describe(scope),
                createdCount = actions.Count,
                actions
            })
        };
    }

    private static void EnsureOnly(JsonElement value, params string[] allowed)
    {
        HashSet<string> names = allowed.ToHashSet(StringComparer.Ordinal);
        string[] unexpected = value.EnumerateObject().Select(x => x.Name)
            .Where(x => !names.Contains(x)).ToArray();
        if (unexpected.Length > 0)
            throw new ArgumentException($"Beklenmeyen alan: {string.Join(", ", unexpected)}");
    }
}
