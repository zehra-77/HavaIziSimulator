using System.Text.Json;
using IcdLib.Models;

namespace HavaIziSimulator.Llm;

/// <summary>
/// LLM orkestratörünün kullandığı MCP istemci sözleşmesi.
/// Gerçek transport (stdio/HTTP) bu arayüzün arkasında kalır.
/// </summary>
public interface IRadarToolClient : IDisposable
{
    Task<IReadOnlyList<McpToolDefinition>> ListToolsAsync(
        CancellationToken cancellationToken = default);

    Task<McpCallResult> CallToolAsync(
        string toolName,
        JsonElement arguments,
        IReadOnlyList<TrackData> activeTracks,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Ayrı MCP sürecinde çalışan radar iş kurallarının sözleşmesi.
/// </summary>
public interface IRadarToolService
{
    IReadOnlyList<McpToolDefinition> ListTools();

    Task<McpCallResult> CallToolAsync(
        string toolName,
        JsonElement arguments,
        IReadOnlyList<TrackData> activeTracks,
        CancellationToken cancellationToken = default);
}

public sealed class McpToolDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public JsonElement InputSchema { get; set; }
}
