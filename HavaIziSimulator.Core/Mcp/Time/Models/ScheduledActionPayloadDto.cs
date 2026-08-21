using System.Text.Json;

namespace HavaIziSimulator.Mcp.Time.Models;

public sealed class ScheduledActionPayloadDto
{
    public int DelaySeconds { get; set; }
    public string ToolName { get; set; } = string.Empty;
    public JsonElement Arguments { get; set; }
}
