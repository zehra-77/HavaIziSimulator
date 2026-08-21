using HavaIziSimulator.Llm;

namespace HavaIziSimulator.Ai.Models;

public sealed class AiAssistantResponse
{
    public string Answer { get; init; } = string.Empty;
    public List<RadarScenarioDto> RadarActions { get; init; } = [];
    public List<AiToolCallInfo> ToolCalls { get; init; } = [];
}

public sealed record AiToolCallInfo(string Name, string ArgumentsJson);
