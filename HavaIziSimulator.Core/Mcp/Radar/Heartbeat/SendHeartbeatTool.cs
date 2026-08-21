using System.Text.Json;
using HavaIziSimulator.Llm;
using HavaIziSimulator.Mcp.Abstractions;

namespace HavaIziSimulator.Mcp.Radar.Heartbeat;

public sealed class SendHeartbeatTool : McpToolBase
{
    public override string Name => "radar_send_heartbeat";
    public override string Description => "Sensör canlılık mesajı HEARTBEAT gönderir.";
    protected override string SchemaJson => """{"type":"object","properties":{},"additionalProperties":false}""";
    public override Task<McpCallResult> ExecuteAsync(
        JsonElement arguments, McpToolContext context, CancellationToken cancellationToken = default) =>
        RadarResult(RadarToolOperations.SendHeartbeat(arguments));
}
