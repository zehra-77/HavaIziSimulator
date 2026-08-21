using System.Diagnostics;
using System.Text.Json;
using IcdLib.Models;

namespace HavaIziSimulator.Llm;

/// <summary>
/// Ayrı HavaIziSimulator.McpServer süreciyle stdio üzerinden MCP JSON-RPC konuşur.
/// Sunucu WPF tarafından otomatik başlatılır; kullanıcı ek bir terminal açmaz.
/// </summary>
public sealed class RadarMcpProcessClient : IRadarToolClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private Process? _process;
    private StreamWriter? _input;
    private StreamReader? _output;
    private long _requestId;
    private bool _initialized;
    private bool _disposed;

    public async Task<IReadOnlyList<McpToolDefinition>> ListToolsAsync(
        CancellationToken cancellationToken = default)
    {
        using JsonDocument response = await SendRequestAsync("tools/list", new { }, cancellationToken);
        return response.RootElement.GetProperty("result").GetProperty("tools")
            .Deserialize<List<McpToolDefinition>>(JsonOptions) ?? [];
    }

    public async Task<McpCallResult> CallToolAsync(
        string toolName,
        JsonElement arguments,
        IReadOnlyList<TrackData> activeTracks,
        CancellationToken cancellationToken = default)
    {
        using JsonDocument response = await SendRequestAsync("tools/call", new
        {
            name = toolName,
            arguments,
            _meta = new { activeTracks }
        }, cancellationToken);

        JsonElement result = response.RootElement.GetProperty("result");
        if (result.TryGetProperty("isError", out JsonElement isError) && isError.ValueKind == JsonValueKind.True)
            throw new InvalidOperationException(result.GetProperty("content")[0].GetProperty("text").GetString());

        JsonElement structured;
        if (result.TryGetProperty("structuredContent", out JsonElement structuredElement))
        {
            structured = structuredElement.Clone();
        }
        else
        {
            string json = result.GetProperty("content")[0].GetProperty("text").GetString() ?? "{}";
            using JsonDocument contentDocument = JsonDocument.Parse(json);
            structured = contentDocument.RootElement.Clone();
        }

        List<RadarScenarioDto> actions = structured.ValueKind == JsonValueKind.Object &&
                                                structured.TryGetProperty("actions", out JsonElement actionElement)
            ? actionElement.Deserialize<List<RadarScenarioDto>>(JsonOptions) ?? []
            : [];

        return new McpCallResult
        {
            StructuredContent = structured,
            RadarActions = actions
        };
    }

    private async Task<JsonDocument> SendRequestAsync(
        string method,
        object parameters,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureStartedAndInitializedAsync(cancellationToken);
            long id = Interlocked.Increment(ref _requestId);
            string request = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id,
                method,
                @params = parameters
            });
            await _input!.WriteLineAsync(request.AsMemory(), cancellationToken);
            await _input.FlushAsync(cancellationToken);

            JsonDocument response = await ReadJsonResponseAsync(cancellationToken);
            if (response.RootElement.TryGetProperty("error", out JsonElement error))
            {
                string message = error.TryGetProperty("message", out JsonElement text)
                    ? text.GetString() ?? "Bilinmeyen MCP hatası"
                    : "Bilinmeyen MCP hatası";
                response.Dispose();
                throw new InvalidOperationException($"MCP: {message}");
            }
            return response;
        }
        catch (OperationCanceledException)
        {
            // İstek gönderildikten sonra iptal olduysa daha sonra gelecek cevap
            // bir sonraki JSON-RPC isteğiyle karışmasın; süreci temiz başlat.
            ResetProcess();
            throw;
        }
        catch when (_process is { HasExited: true })
        {
            ResetProcess();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureStartedAndInitializedAsync(CancellationToken cancellationToken)
    {
        if (_process is null || _process.HasExited) StartProcess();
        if (_initialized) return;

        long id = Interlocked.Increment(ref _requestId);
        string request = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id,
            method = "initialize",
            @params = new
            {
                protocolVersion = "2025-03-26",
                capabilities = new { },
                clientInfo = new { name = "hava-izi-simulator-wpf", version = "1.0.0" }
            }
        });
        await _input!.WriteLineAsync(request.AsMemory(), cancellationToken);
        await _input.FlushAsync(cancellationToken);
        using JsonDocument response = await ReadJsonResponseAsync(cancellationToken);
        if (response.RootElement.TryGetProperty("error", out JsonElement error))
            throw new InvalidOperationException($"MCP initialize: {error.GetProperty("message").GetString()}");

        string notification = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            method = "notifications/initialized"
        });
        await _input.WriteLineAsync(notification.AsMemory(), cancellationToken);
        await _input.FlushAsync(cancellationToken);
        _initialized = true;
    }

    /// <summary>
    /// MCP stdout'tan ilk geçerli JSON-RPC nesnesini okur. İlk process açılışında
    /// oluşabilen BOM, boş satır veya eski executable'ın yazdığı kısa bir ön ek
    /// JSON başlangıcından önce temizlenir.
    /// </summary>
    private async Task<JsonDocument> ReadJsonResponseAsync(
        CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            string? line = await _output!.ReadLineAsync(cancellationToken);
            if (line is null)
                throw new InvalidOperationException(
                    "MCP sunucusu JSON cevabı göndermeden kapandı.");

            string cleanLine = line
                .TrimStart('\uFEFF', '\u200B', ' ', '\t', '\r', '\n');

            // Bazı hatalı encoding zincirlerinde BOM üç görünür karaktere
            // dönüşebilir. Bu olasılığı da temizle.
            if (cleanLine.StartsWith("ï»¿", StringComparison.Ordinal))
                cleanLine = cleanLine[3..];

            int jsonStart = cleanLine.IndexOf('{');
            if (jsonStart < 0)
            {
                if (!string.IsNullOrWhiteSpace(cleanLine))
                    Debug.WriteLine($"[MCP STDOUT ATLANDI] {cleanLine}");
                continue;
            }

            return JsonDocument.Parse(cleanLine[jsonStart..]);
        }

        throw new InvalidOperationException(
            "MCP sunucusundan geçerli JSON-RPC cevabı alınamadı.");
    }

    private void StartProcess()
    {
        string? configuredPath = Environment.GetEnvironmentVariable("RADAR_MCP_SERVER_PATH");
        string exePath = configuredPath ?? Path.Combine(AppContext.BaseDirectory, "HavaIziSimulator.McpServer.exe");
        string dllPath = configuredPath ?? Path.Combine(AppContext.BaseDirectory, "HavaIziSimulator.McpServer.dll");

        ProcessStartInfo startInfo;
        if (File.Exists(exePath) && exePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            startInfo = new ProcessStartInfo(exePath);
        }
        else if (File.Exists(dllPath))
        {
            startInfo = new ProcessStartInfo("dotnet", $"\"{dllPath}\"");
        }
        else
        {
            throw new FileNotFoundException(
                "MCP sunucusu bulunamadı. HavaIziSimulator.Wpf projesini yeniden derleyin veya " +
                "RADAR_MCP_SERVER_PATH ortam değişkenini tanımlayın.", exePath);
        }

        startInfo.UseShellExecute = false;
        startInfo.RedirectStandardInput = true;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.CreateNoWindow = true;
        startInfo.StandardInputEncoding =
            new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        startInfo.StandardOutputEncoding =
            new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        _process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("MCP sunucusu başlatılamadı.");
        _input = _process.StandardInput;
        _output = _process.StandardOutput;
        _ = DrainErrorAsync(_process.StandardError);
        _initialized = false;
    }

    private static async Task DrainErrorAsync(StreamReader error)
    {
        while (await error.ReadLineAsync() is { } line)
            Debug.WriteLine($"[MCP STDERR] {line}");
    }

    private void ResetProcess()
    {
        try
        {
            if (_process is { HasExited: false })
                _process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Süreç başka bir yoldan kapanmış olabilir.
        }
        _input?.Dispose();
        _output?.Dispose();
        _process?.Dispose();
        _input = null;
        _output = null;
        _process = null;
        _initialized = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ResetProcess();
        _gate.Dispose();
    }
}
