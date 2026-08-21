using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace HavaIziSimulator.Llm.Groq;

public sealed class GroqChatClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly HostedLlmOptions _options;

    public GroqChatClient(HttpClient? httpClient = null, HostedLlmOptions? options = null)
    {
        _options = options ?? new HostedLlmOptions();
        _ownsHttpClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        _httpClient.Timeout = _options.Timeout;
    }

    public string ProviderDescription => $"Groq / {_options.Model}";

    public async Task<GroqCompletionResult> CompleteAsync(
        IReadOnlyList<object> messages,
        IReadOnlyList<McpToolDefinition> tools,
        CancellationToken cancellationToken = default)
    {
        object[] toolSchemas = tools.Select(tool => (object)new
        {
            type = "function",
            function = new { name = tool.Name, description = tool.Description, parameters = tool.InputSchema }
        }).ToArray();

        var request = new Dictionary<string, object?>
        {
            ["model"] = _options.Model,
            ["temperature"] = _options.Temperature,
            ["max_completion_tokens"] = _options.MaxCompletionTokens,
            ["parallel_tool_calls"] = true,
            ["tool_choice"] = "auto",
            ["messages"] = messages
        };
        if (toolSchemas.Length > 0) request["tools"] = toolSchemas;

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = JsonContent.Create(request)
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        using HttpResponseMessage response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        string responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"LLM HTTP {(int)response.StatusCode}: {ExtractApiError(responseText)}");

        using JsonDocument document = JsonDocument.Parse(responseText);
        JsonElement message = document.RootElement.GetProperty("choices")[0].GetProperty("message");
        string content = message.TryGetProperty("content", out JsonElement contentElement) &&
                         contentElement.ValueKind == JsonValueKind.String
            ? contentElement.GetString() ?? string.Empty
            : string.Empty;

        var calls = new List<GroqToolCall>();
        if (message.TryGetProperty("tool_calls", out JsonElement toolCalls) &&
            toolCalls.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement call in toolCalls.EnumerateArray())
            {
                JsonElement function = call.GetProperty("function");
                calls.Add(new GroqToolCall(
                    call.GetProperty("id").GetString() ?? Guid.NewGuid().ToString("N"),
                    function.GetProperty("name").GetString() ?? throw new InvalidOperationException("Groq boş araç adı döndürdü."),
                    function.GetProperty("arguments").GetString() ?? "{}"));
            }
        }

        return new GroqCompletionResult
        {
            Content = content,
            AssistantMessage = message.Clone(),
            ToolCalls = calls
        };
    }

    private static string ExtractApiError(string responseText)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(responseText);
            return document.RootElement.GetProperty("error").GetProperty("message").GetString() ?? responseText;
        }
        catch
        {
            return responseText.Length <= 500 ? responseText : responseText[..500];
        }
    }

    public void Dispose()
    {
        if (_ownsHttpClient) _httpClient.Dispose();
    }
}
