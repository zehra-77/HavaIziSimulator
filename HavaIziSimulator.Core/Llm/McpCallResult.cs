using System.Text.Json;

namespace HavaIziSimulator.Llm;

/// <summary>
/// Her MCP aracının ortak dönüş zarfı. Sorgu araçları yalnız StructuredContent,
/// radar araçları ise StructuredContent yanında uygulanabilir RadarActions döndürür.
/// </summary>
public sealed class McpCallResult
{
    public JsonElement StructuredContent { get; init; }
    public List<RadarScenarioDto> RadarActions { get; init; } = [];

    public static McpCallResult FromActions(List<RadarScenarioDto> actions) => new()
    {
        RadarActions = actions,
        StructuredContent = JsonSerializer.SerializeToElement(new { actions })
    };
}
