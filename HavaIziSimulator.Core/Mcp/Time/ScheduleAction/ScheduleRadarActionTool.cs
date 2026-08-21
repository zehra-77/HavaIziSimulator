using System.Text.Json;
using HavaIziSimulator.Llm;
using HavaIziSimulator.Mcp.Abstractions;
using HavaIziSimulator.Mcp.Time.Models;

namespace HavaIziSimulator.Mcp.Time.ScheduleAction;

public sealed class ScheduleRadarActionTool : McpToolBase
{
    public override string Name => "time_schedule_radar_action";
    public override string Description => "Bir radar aracını belirtilen saniye sonra çalıştırılmak üzere planlar. Gecikmeli kullanıcı isteklerinde bu aracı seç.";
    protected override string SchemaJson => """
        {"type":"object","properties":{
          "delaySeconds":{"type":"integer","minimum":1,"maximum":86400},
          "toolName":{"type":"string","enum":["radar_create_tracks","radar_create_tracks_spatial","radar_drop_tracks","radar_update_diagnosis","radar_update_classification","radar_update_tracks","radar_send_heartbeat"]},
          "arguments":{"type":"object"}
        },"required":["delaySeconds","toolName","arguments"],"additionalProperties":false}
        """;

    public override Task<McpCallResult> ExecuteAsync(
        JsonElement arguments,
        McpToolContext context,
        CancellationToken cancellationToken = default)
    {
        if (!arguments.TryGetProperty("delaySeconds", out JsonElement delayElement) ||
            !delayElement.TryGetInt32(out int delaySeconds) || delaySeconds is < 1 or > 86400)
            throw new ArgumentException("delaySeconds 1-86400 arasında bir tam sayı olmalıdır.");

        string toolName = arguments.TryGetProperty("toolName", out JsonElement nameElement)
            ? nameElement.GetString() ?? string.Empty : string.Empty;
        string[] allowed = ["radar_create_tracks", "radar_create_tracks_spatial", "radar_drop_tracks", "radar_update_diagnosis",
            "radar_update_classification", "radar_update_tracks", "radar_send_heartbeat"];
        if (!allowed.Contains(toolName, StringComparer.Ordinal))
            throw new ArgumentException($"Zamanlanamayan radar aracı: {toolName}");
        if (!arguments.TryGetProperty("arguments", out JsonElement nested) || nested.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("arguments bir JSON nesnesi olmalıdır.");

        List<RadarScenarioDto> actions =
        [
            new RadarScenarioDto
            {
                MessageType = "SCHEDULED_ACTION",
                Payload = JsonSerializer.SerializeToElement(new ScheduledActionPayloadDto
                {
                    DelaySeconds = delaySeconds,
                    ToolName = toolName,
                    Arguments = nested.Clone()
                })
            }
        ];
        return RadarResult(actions);
    }
}
