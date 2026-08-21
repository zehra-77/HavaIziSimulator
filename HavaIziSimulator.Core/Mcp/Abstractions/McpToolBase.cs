using System.Text.Json;
using HavaIziSimulator.Llm;

namespace HavaIziSimulator.Mcp.Abstractions;

public abstract class McpToolBase : IMcpTool
{
    public abstract string Name { get; }
    public abstract string Description { get; }
    protected abstract string SchemaJson { get; }
    public JsonElement InputSchema => JsonSerializer.Deserialize<JsonElement>(SchemaJson);
    public abstract Task<McpCallResult> ExecuteAsync(
        JsonElement arguments,
        McpToolContext context,
        CancellationToken cancellationToken = default);

    protected static Task<McpCallResult> RadarResult(List<RadarScenarioDto> actions) =>
        Task.FromResult(McpCallResult.FromActions(actions));
}
