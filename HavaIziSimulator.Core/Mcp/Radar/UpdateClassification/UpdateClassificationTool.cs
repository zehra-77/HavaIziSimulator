using System.Text.Json;
using HavaIziSimulator.Llm;
using HavaIziSimulator.Mcp.Abstractions;
using HavaIziSimulator.Mcp.Radar.Shared;

namespace HavaIziSimulator.Mcp.Radar.UpdateClassification;

public sealed class UpdateClassificationTool : McpToolBase
{
    public override string Name => "radar_update_classification";
    public override string Description => "Koşulu sağlayan aktif izlerin tasnif değerini değiştirir.";
    protected override string SchemaJson => RadarSchemas.Filter("yeniTasnif", "[\"BILINMIYOR\",\"UCAK\",\"DONERKANAT\",\"FUZE\",\"IHA\"]");
    public override Task<McpCallResult> ExecuteAsync(
        JsonElement arguments, McpToolContext context, CancellationToken cancellationToken = default) =>
        RadarResult(RadarToolOperations.UpdateClassification(arguments, context.ActiveTracks));
}
