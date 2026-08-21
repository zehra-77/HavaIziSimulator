using System.Text.Json;

namespace HavaIziSimulator.Llm.Groq;

public sealed class GroqCompletionResult
{
    public string Content { get; init; } = string.Empty;
    public JsonElement AssistantMessage { get; init; }
    public List<GroqToolCall> ToolCalls { get; init; } = [];
}

public sealed record GroqToolCall(string Id, string Name, string ArgumentsJson);
