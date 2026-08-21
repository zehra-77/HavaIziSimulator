using System.Text.Json;
using HavaIziSimulator.Llm;
using HavaIziSimulator.Mcp.Abstractions;
using HavaIziSimulator.Mcp.Radar.Shared;

namespace HavaIziSimulator.Mcp.Radar.UpdateDiagnosis;

public sealed class UpdateDiagnosisTool : McpToolBase
{
    public override string Name => "radar_update_diagnosis";
    public override string Description => "Koşulu sağlayan aktif izlerin teşhisini değiştirir.";
    protected override string SchemaJson => RadarSchemas.Filter("yeniTeshis", "[\"BILINMEYEN\",\"DOST\",\"DUSMAN\",\"TARAFSIZ\"]");
    public override Task<McpCallResult> ExecuteAsync(
        JsonElement arguments, McpToolContext context, CancellationToken cancellationToken = default) =>
        RadarResult(RadarToolOperations.UpdateDiagnosis(arguments, context.ActiveTracks));
}
