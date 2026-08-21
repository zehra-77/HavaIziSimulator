using System.Text.Json;
using HavaIziSimulator.Llm;
using HavaIziSimulator.Mcp.Abstractions;
using HavaIziSimulator.Mcp.Radar.Shared;

namespace HavaIziSimulator.Mcp.Radar.CreateTracks;

public sealed class CreateTracksTool : McpToolBase
{
    public override string Name => "radar_create_tracks";
    public override string Description => "Bir veya birden fazla iz grubu oluşturur. Farklı tür/teşhisler aynı istekteyse groups dizisine ayrı grup olarak koy; belirtilmeyen alanlar rastgele doldurulur.";
    protected override string SchemaJson => RadarSchemas.Create;
    public override Task<McpCallResult> ExecuteAsync(
        JsonElement arguments, McpToolContext context, CancellationToken cancellationToken = default) =>
        RadarResult(RadarToolOperations.CreateTracks(arguments, context.ActiveTracks));
}
