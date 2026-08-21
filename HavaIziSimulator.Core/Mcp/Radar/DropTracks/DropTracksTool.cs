using System.Text.Json;
using HavaIziSimulator.Llm;
using HavaIziSimulator.Mcp.Abstractions;
using HavaIziSimulator.Mcp.Radar.Shared;

namespace HavaIziSimulator.Mcp.Radar.DropTracks;

public sealed class DropTracksTool : McpToolBase
{
    public override string Name => "radar_drop_tracks";
    public override string Description => "Koşulu sağlayan aktif radar izlerini düşürür.";
    protected override string SchemaJson => RadarSchemas.Filter("neden", "[\"SINYAL_KAYBI\",\"KAPSAMA_ALANI_DISI\",\"MANUEL_SONLANDIRMA\",\"DIGER\"]");
    public override Task<McpCallResult> ExecuteAsync(
        JsonElement arguments, McpToolContext context, CancellationToken cancellationToken = default) =>
        RadarResult(RadarToolOperations.DropTracks(arguments, context.ActiveTracks));
}
