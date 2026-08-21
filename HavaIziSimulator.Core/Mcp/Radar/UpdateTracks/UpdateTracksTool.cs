using System.Text.Json;
using HavaIziSimulator.Llm;
using HavaIziSimulator.Mcp.Abstractions;
using HavaIziSimulator.Mcp.Radar.Shared;

namespace HavaIziSimulator.Mcp.Radar.UpdateTracks;

public sealed class UpdateTracksTool : McpToolBase
{
    public override string Name => "radar_update_tracks";
    public override string Description => "Koşulu sağlayan aktif izlerin hareket veya konum alanlarını günceller; belirtilmeyen alanları korur.";
    protected override string SchemaJson => RadarSchemas.Update;
    public override Task<McpCallResult> ExecuteAsync(
        JsonElement arguments, McpToolContext context, CancellationToken cancellationToken = default) =>
        RadarResult(RadarToolOperations.UpdateTracks(arguments, context.ActiveTracks));
}
