using System.Text.Json;
using HavaIziSimulator.Llm;
using HavaIziSimulator.Mcp.Abstractions;
using IcdLib.Models;

namespace HavaIziSimulator.Mcp.Registry;

public sealed class McpToolRegistry : IRadarToolService
{
    private readonly IReadOnlyDictionary<string, IMcpTool> _tools;
    private readonly RadarScenarioValidator _validator = new();

    public McpToolRegistry(IEnumerable<IMcpTool> tools)
    {
        _tools = tools.ToDictionary(x => x.Name, StringComparer.Ordinal);
    }

    public IReadOnlyList<McpToolDefinition> ListTools() => _tools.Values
        .Select(x => new McpToolDefinition
        {
            Name = x.Name,
            Description = x.Description,
            InputSchema = x.InputSchema
        }).ToList();

    public async Task<McpCallResult> CallToolAsync(
        string toolName,
        JsonElement arguments,
        IReadOnlyList<TrackData> activeTracks,
        CancellationToken cancellationToken = default)
    {
        if (arguments.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Araç parametreleri JSON nesnesi olmalıdır.");
        if (!_tools.TryGetValue(toolName, out IMcpTool? tool))
            throw new ArgumentException($"Bilinmeyen MCP aracı: {toolName}");

        McpCallResult result = await tool.ExecuteAsync(
            arguments,
            new McpToolContext(activeTracks, DateTimeOffset.Now),
            cancellationToken);

        foreach (RadarScenarioDto action in result.RadarActions.Where(x => x.MessageType != "SCHEDULED_ACTION"))
            _validator.DogrulaVeDonustur(action);
        return result;
    }
}
