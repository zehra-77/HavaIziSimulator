using System.Text.Json;
using HavaIziSimulator.Llm;
using HavaIziSimulator.Mcp.Abstractions;
using HavaIziSimulator.Mcp.Spatial.Services;
using HavaIziSimulator.Mcp.Spatial.Shared;

namespace HavaIziSimulator.Mcp.Radar.QueryActiveTracks;

public sealed class QueryActiveTracksTool : McpToolBase
{
    public override string Name => "radar_query_active_tracks";
    public override string Description => "Aktif izleri filtreler, sayar, listeler veya özetler. Bilgi sorularında değişiklik yapan araçlar yerine bunu kullan.";
    protected override string SchemaJson => SpatialSchemas.Query(false);

    public override Task<McpCallResult> ExecuteAsync(
        JsonElement arguments, McpToolContext context, CancellationToken cancellationToken = default)
    {
        JsonElement? filter = arguments.TryGetProperty("filter", out JsonElement value) ? value : null;
        var matches = TrackQueryService.ApplyFilter(context.ActiveTracks, filter);
        return Task.FromResult(TrackQueryResultBuilder.Build(arguments, matches));
    }
}
