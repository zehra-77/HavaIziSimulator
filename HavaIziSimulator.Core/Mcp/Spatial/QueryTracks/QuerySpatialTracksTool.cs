using System.Text.Json;
using HavaIziSimulator.Llm;
using HavaIziSimulator.Mcp.Abstractions;
using HavaIziSimulator.Mcp.Spatial.Services;
using HavaIziSimulator.Mcp.Spatial.Shared;

namespace HavaIziSimulator.Mcp.Spatial.QueryTracks;

public sealed class QuerySpatialTracksTool : McpToolBase
{
    private readonly SpatialGeometryService _geometry;

    public QuerySpatialTracksTool(SpatialGeometryService geometry) => _geometry = geometry;

    public override string Name => "radar_query_tracks_spatial";
    public override string Description => "Yer adıyla çözülen şehir/bölge, merkez noktası veya merkez-yarıçap içindeki aktif izleri sayar, listeler ve özetler. Koordinat uydurma.";
    protected override string SchemaJson => SpatialSchemas.Query(true);

    public override async Task<McpCallResult> ExecuteAsync(
        JsonElement arguments, McpToolContext context, CancellationToken cancellationToken = default)
    {
        var scope = await _geometry.ResolveAsync(arguments.GetProperty("scope"), cancellationToken);
        JsonElement? filter = arguments.TryGetProperty("filter", out JsonElement value) ? value : null;
        var filtered = TrackQueryService.ApplyFilter(context.ActiveTracks, filter);
        var matches = filtered.Where(x => _geometry.Contains(scope, x.Enlem, x.Boylam)).ToList();
        return TrackQueryResultBuilder.Build(arguments, matches, _geometry.Describe(scope));
    }
}
