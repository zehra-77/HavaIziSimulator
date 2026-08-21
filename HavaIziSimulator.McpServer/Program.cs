using System.Text.Json;
using HavaIziSimulator.Llm;
using HavaIziSimulator.Mcp.Registry;
using IcdLib.Models;

// Console.Out yerine standart akışları kendimiz açıyoruz. Böylece Windows'ta
// ilk stdout yazımının başına EF BB BF (UTF-8 BOM) eklenmesi kesin olarak
// engellenir. MCP stdout yalnızca JSON-RPC satırları içermelidir.
using var standardInput = new StreamReader(
    Console.OpenStandardInput(),
    new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
    detectEncodingFromByteOrderMarks: true);

using var standardOutput = new StreamWriter(
    Console.OpenStandardOutput(),
    new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
{
    AutoFlush = true
};

IRadarToolService tools = new McpToolRegistry(McpToolCatalog.CreateDefault());
var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

while (await standardInput.ReadLineAsync() is { } line)
{
    if (string.IsNullOrWhiteSpace(line)) continue;

    JsonDocument? requestDocument = null;
    try
    {
        requestDocument = JsonDocument.Parse(line);
        JsonElement root = requestDocument.RootElement;
        string method = root.GetProperty("method").GetString() ?? string.Empty;

        if (method == "notifications/initialized") continue;

        JsonElement id = root.TryGetProperty("id", out JsonElement requestId)
            ? requestId.Clone()
            : JsonSerializer.SerializeToElement<object?>(null);

        object result = method switch
        {
            "initialize" => new
            {
                protocolVersion = "2025-03-26",
                capabilities = new { tools = new { } },
                serverInfo = new { name = "hava-izi-radar-mcp", version = "1.0.0" }
            },
            "tools/list" => new { tools = tools.ListTools() },
            "tools/call" => await CallToolAsync(
                root.GetProperty("params"), tools, jsonOptions, CancellationToken.None),
            _ => throw new InvalidOperationException($"Desteklenmeyen MCP metodu: {method}")
        };

        await WriteAsync(new { jsonrpc = "2.0", id, result });
    }
    catch (Exception ex)
    {
        JsonElement id = requestDocument is not null &&
                         requestDocument.RootElement.TryGetProperty("id", out JsonElement requestId)
            ? requestId.Clone()
            : JsonSerializer.SerializeToElement<object?>(null);
        await WriteAsync(new
        {
            jsonrpc = "2.0",
            id,
            error = new { code = -32602, message = ex.Message }
        });
    }
    finally
    {
        requestDocument?.Dispose();
    }
}

return;

static async Task<object> CallToolAsync(
    JsonElement parameters,
    IRadarToolService tools,
    JsonSerializerOptions jsonOptions,
    CancellationToken cancellationToken)
{
    string name = parameters.GetProperty("name").GetString()
        ?? throw new ArgumentException("MCP araç adı boş.");
    JsonElement arguments = parameters.GetProperty("arguments");

    IReadOnlyList<TrackData> activeTracks = [];
    if (parameters.TryGetProperty("_meta", out JsonElement meta) &&
        meta.TryGetProperty("activeTracks", out JsonElement tracks))
        activeTracks = tracks.Deserialize<List<TrackData>>(jsonOptions) ?? [];

    McpCallResult toolResult = await tools.CallToolAsync(
        name, arguments, activeTracks, cancellationToken);
    string text = JsonSerializer.Serialize(toolResult.StructuredContent);
    return new
    {
        content = new[] { new { type = "text", text } },
        structuredContent = toolResult.StructuredContent,
        isError = false
    };
}

async Task WriteAsync(object response)
{
    await standardOutput.WriteLineAsync(JsonSerializer.Serialize(response));
}
