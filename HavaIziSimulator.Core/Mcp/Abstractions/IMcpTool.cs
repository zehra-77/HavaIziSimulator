using System.Text.Json;
using HavaIziSimulator.Llm;
using IcdLib.Models;

namespace HavaIziSimulator.Mcp.Abstractions;

public interface IMcpTool
{
    string Name { get; }
    string Description { get; }
    JsonElement InputSchema { get; }
    Task<McpCallResult> ExecuteAsync(
        JsonElement arguments,
        McpToolContext context,
        CancellationToken cancellationToken = default);
}

public sealed record McpToolContext(IReadOnlyList<TrackData> ActiveTracks, DateTimeOffset Now);
