using System.Reflection;
using System.Text.Json;
using HavaIziSimulator.Ai.Models;
using HavaIziSimulator.Llm;
using HavaIziSimulator.Llm.Groq;
using IcdLib.Enums;
using IcdLib.Models;

namespace HavaIziSimulator.Ai.Chat;

public sealed class AiAssistantService : IDisposable
{
    private static readonly string SystemPrompt = LoadPrompt();
    private readonly GroqChatClient _groqClient;
    private readonly IRadarToolClient _toolClient;
    private readonly RadarScenarioValidator _validator = new();
    private readonly List<ChatTurn> _history = [];

    public AiAssistantService(
        HttpClient? httpClient = null,
        HostedLlmOptions? options = null,
        IRadarToolClient? toolClient = null)
    {
        _groqClient = new GroqChatClient(httpClient, options);
        _toolClient = toolClient ?? new RadarMcpProcessClient();
    }

    public string ProviderDescription => $"{_groqClient.ProviderDescription} / MCP / Chat";

    public async Task<List<RadarScenarioDto>> CallToolDirectAsync(
        string toolName, JsonElement arguments, IReadOnlyList<TrackData> activeTracks,
        CancellationToken cancellationToken = default)
    {
        McpCallResult result = await _toolClient.CallToolAsync(
            toolName, arguments, activeTracks, cancellationToken);
        return result.RadarActions;
    }

    public async Task<AiAssistantResponse> ChatAsync(
        string prompt,
        IReadOnlyList<TrackData> activeTracks,
        string? logContext = null,
        CancellationToken cancellationToken = default,
        Func<IReadOnlyList<TrackData>>? latestTracksProvider = null,
        bool logOnlyMode = false)
    {
        if (string.IsNullOrWhiteSpace(prompt)) throw new ArgumentException("Mesaj boş olamaz.", nameof(prompt));
        if (logOnlyMode && string.IsNullOrWhiteSpace(logContext))
            throw new InvalidOperationException("Log analiz modu için geçerli bir log dosyası seçilmelidir.");

        IReadOnlyList<McpToolDefinition> tools = logOnlyMode
            ? []
            : await _toolClient.ListToolsAsync(cancellationToken);
        var messages = new List<object> { new { role = "system", content = SystemPrompt } };
        foreach (ChatTurn turn in _history)
        {
            messages.Add(new { role = "user", content = turn.User });
            messages.Add(new { role = "assistant", content = turn.Assistant });
        }
        messages.Add(new
            {
                role = "user",
                content = logOnlyMode
                    ? "Çalışma modu: YALNIZ LOG ANALİZİ. Aktif radar izlerini kullanma ve radar aracı çağırma.\n\n" +
                      $"Seçili logun doğrulanmış geçmişi: {logContext}\n\n" +
                      $"Kullanıcı mesajı: {prompt}"
                    : "Çalışma modu: AKTİF RADAR. Yüklü log bağlamı yoktur. " +
                      "Aktif iz verisini tahmin etme; bilgi için query MCP araçlarını kullan.\n\n" +
                      $"Aktif iz sayısı: {activeTracks.Count}\n\n" +
                          $"Kullanıcı mesajı: {prompt}"
            });
        var workingTracks = (latestTracksProvider?.Invoke() ?? activeTracks).ToList();
        var actions = new List<RadarScenarioDto>();
        var calls = new List<AiToolCallInfo>();
        IReadOnlyList<McpToolDefinition> availableTools = tools;

        for (int turn = 0; turn < 4; turn++)
        {
            GroqCompletionResult completion = await _groqClient.CompleteAsync(
                messages, availableTools, cancellationToken);
            if (completion.ToolCalls.Count == 0)
            {
                string answer = completion.Content.Trim();
                if (string.IsNullOrEmpty(answer) && actions.Count > 0) answer = $"{actions.Count} radar işlemi hazırlandı.";
                Remember(prompt, answer);
                return new AiAssistantResponse { Answer = answer, RadarActions = actions, ToolCalls = calls };
            }

            messages.Add(new
            {
                role = "assistant",
                content = string.IsNullOrWhiteSpace(completion.Content) ? null : completion.Content,
                tool_calls = completion.ToolCalls.Select(call => new
                {
                    id = call.Id,
                    type = "function",
                    function = new { name = call.Name, arguments = call.ArgumentsJson }
                }).ToArray()
            });
            bool toolErrorOccurred = false;
            bool modelMustExplainToolResult = false;
            foreach (GroqToolCall call in completion.ToolCalls)
            {
                calls.Add(new AiToolCallInfo(call.Name, call.ArgumentsJson));
                try
                {
                    using JsonDocument document = JsonDocument.Parse(call.ArgumentsJson);
                    if (document.RootElement.ValueKind != JsonValueKind.Object)
                        throw new InvalidOperationException($"{call.Name} için geçerli parametre üretilmedi.");

                    JsonElement arguments = document.RootElement.Clone();
                    McpCallResult toolResult = await _toolClient.CallToolAsync(
                        call.Name, arguments, workingTracks, cancellationToken);
                    actions.AddRange(toolResult.RadarActions);
                    ApplyToWorkingContext(workingTracks, toolResult.RadarActions);
                    if (toolResult.RadarActions.Count == 0)
                        modelMustExplainToolResult = true;
                    messages.Add(new
                    {
                        role = "tool",
                        tool_call_id = call.Id,
                        name = call.Name,
                        content = JsonSerializer.Serialize(toolResult.StructuredContent)
                    });
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    toolErrorOccurred = true;
                    // Parametre/şema hatasını regex ile düzeltmek yerine modele geri
                    // ver; Groq bir sonraki turda aynı MCP'yi doğru argümanla seçsin.
                    messages.Add(new
                    {
                        role = "tool",
                        tool_call_id = call.Id,
                        name = call.Name,
                        content = JsonSerializer.Serialize(new { isError = true, error = ex.Message })
                    });
                }
            }

            // Başarılı radar işleminde ikinci LLM çağrısı yalnızca kısa bir
            // özet için gereksiz token harcıyordu. Araç seçimini Groq yapar;
            // uygulama doğrulanmış MCP sonucunu burada özetler. Yalnızca araç
            // hatası varsa modele kendini düzeltmesi için yeni tur verilir.
            if (!toolErrorOccurred && !modelMustExplainToolResult)
            {
                string answer = BuildActionSummary(actions);
                Remember(prompt, answer);
                return new AiAssistantResponse { Answer = answer, RadarActions = actions, ToolCalls = calls };
            }

            // Sorgu aracı gerçek sayım/liste sonucunu üretti. Sonraki turda
            // model yalnız bu küçük sonucu açıklar; tüm tool şemalarını yeniden
            // göndermek TPM tüketimini gereksiz yere ikiye katlamasın.
            if (!toolErrorOccurred && modelMustExplainToolResult)
                availableTools = [];
        }
        throw new InvalidOperationException("Groq araç çağrılarını dört tur içinde tamamlayamadı.");
    }

    private void ApplyToWorkingContext(List<TrackData> tracks, IEnumerable<RadarScenarioDto> scenarios)
    {
        foreach (RadarScenarioDto scenario in scenarios)
        {
            if (scenario.MessageType == "SCHEDULED_ACTION") continue;
            LlmSenaryoSonucu result = _validator.DogrulaVeDonustur(scenario);
            switch (result.MessageType)
            {
                case MessageType.TrackCreated: tracks.Add(result.TrackData!); break;
                case MessageType.TrackUpdated: Replace(tracks, result.TrackData!); break;
                case MessageType.TrackDropped: tracks.RemoveAll(x => x.TrackId == result.TrackDroppedData!.TrackId); break;
                case MessageType.TeshisUpdated:
                    {
                        int i = tracks.FindIndex(x => x.TrackId == result.TeshisUpdatedData!.TrackId);
                        if (i >= 0) tracks[i] = tracks[i] with { Teshis = result.TeshisUpdatedData.YeniTeshis };
                        break;
                    }
                case MessageType.TasnifUpdated:
                    {
                        int i = tracks.FindIndex(x => x.TrackId == result.TasnifUpdatedData!.TrackId);
                        if (i >= 0) tracks[i] = tracks[i] with { Tasnif = result.TasnifUpdatedData.YeniTasnif };
                        break;
                    }
            }
        }
    }

    private static void Replace(List<TrackData> tracks, TrackData updated)
    {
        int index = tracks.FindIndex(x => x.TrackId == updated.TrackId);
        if (index >= 0) tracks[index] = updated;
    }

    private void Remember(string user, string assistant)
    {
        _history.Add(new ChatTurn(user, assistant));
        if (_history.Count > 2) _history.RemoveAt(0);
    }

    public void ClearHistory() => _history.Clear();

    private static string BuildActionSummary(IReadOnlyList<RadarScenarioDto> actions)
    {
        if (actions.Count == 0) return "Koşula uyan aktif iz bulunamadı; radar işlemi üretilmedi.";

        string[] parts = actions
            .GroupBy(x => x.MessageType)
            .Select(group => group.Key switch
            {
                "TRACK_CREATED" => $"{group.Count()} iz oluşturuldu",
                "TRACK_UPDATED" => $"{group.Count()} iz güncellendi",
                "TRACK_DROPPED" => $"{group.Count()} iz düşürüldü",
                "TESHIS_UPDATED" => $"{group.Count()} izin teşhisi güncellendi",
                "TASNIF_UPDATED" => $"{group.Count()} izin tasnifi güncellendi",
                "HEARTBEAT" => "heartbeat gönderildi",
                "SCHEDULED_ACTION" => $"{group.Count()} işlem zamanlandı",
                _ => $"{group.Count()} {group.Key} işlemi hazırlandı"
            })
            .ToArray();

        return string.Join(", ", parts) + ".";
    }

    private static string LoadPrompt()
    {
        Assembly assembly = typeof(AiAssistantService).Assembly;
        string name = assembly.GetManifestResourceNames()
            .Single(x => x.EndsWith("system-prompt.md", StringComparison.OrdinalIgnoreCase));
        using Stream stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Trim();
    }

    public void Dispose()
    {
        _toolClient.Dispose();
        _groqClient.Dispose();
    }

    private sealed record ChatTurn(string User, string Assistant);
}
