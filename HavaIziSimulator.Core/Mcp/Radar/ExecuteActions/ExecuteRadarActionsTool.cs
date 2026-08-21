using System.Text.Json;
using HavaIziSimulator.Llm;
using HavaIziSimulator.Mcp.Abstractions;
using IcdLib.Enums;
using IcdLib.Models;

namespace HavaIziSimulator.Mcp.Radar.ExecuteActions;

/// <summary>
/// Tek kullanıcı mesajındaki farklı radar işlemlerini, mevcut araçları yeniden
/// yazmadan sıralı biçimde çalıştıran genel bileşik araçtır.
/// </summary>
public sealed class ExecuteRadarActionsTool : McpToolBase
{
    private readonly IReadOnlyDictionary<string, IMcpTool> _tools;
    private readonly RadarScenarioValidator _validator = new();

    public ExecuteRadarActionsTool(IEnumerable<IMcpTool> tools)
    {
        _tools = tools.ToDictionary(x => x.Name, StringComparer.Ordinal);
    }

    public override string Name => "radar_execute_actions";

    public override string Description =>
        "Aynı kullanıcı mesajındaki iki veya daha fazla farklı radar işlemini sırayla çalıştırır. " +
        "Her actions elemanında mevcut aracın adını ve normal arguments nesnesini gönder.";

    protected override string SchemaJson => """
        {"type":"object","properties":{
          "actions":{"type":"array","minItems":2,"maxItems":20,"items":{
            "type":"object","properties":{
              "toolName":{"type":"string","enum":[
                "radar_create_tracks","radar_drop_tracks","radar_update_diagnosis",
                "radar_update_classification","radar_update_tracks","radar_send_heartbeat",
                "radar_create_tracks_spatial","time_schedule_radar_action"
              ]},
              "arguments":{"type":"object"}
            },"required":["toolName","arguments"],"additionalProperties":false
          }}
        },"required":["actions"],"additionalProperties":false}
        """;

    public override async Task<McpCallResult> ExecuteAsync(
        JsonElement arguments,
        McpToolContext context,
        CancellationToken cancellationToken = default)
    {
        if (!arguments.TryGetProperty("actions", out JsonElement actions) ||
            actions.ValueKind != JsonValueKind.Array || actions.GetArrayLength() < 2)
            throw new ArgumentException("actions en az iki işlem içeren bir dizi olmalıdır.");

        var workingTracks = context.ActiveTracks.ToList();
        var results = new List<RadarScenarioDto>();

        foreach (JsonElement action in actions.EnumerateArray())
        {
            string toolName = action.TryGetProperty("toolName", out JsonElement nameElement)
                ? nameElement.GetString() ?? string.Empty
                : string.Empty;
            if (!_tools.TryGetValue(toolName, out IMcpTool? tool))
                throw new ArgumentException($"Bileşik işlemde bilinmeyen araç: {toolName}");
            if (!action.TryGetProperty("arguments", out JsonElement toolArguments) ||
                toolArguments.ValueKind != JsonValueKind.Object)
                throw new ArgumentException($"{toolName} arguments alanı JSON nesnesi olmalıdır.");

            McpCallResult toolResult = await tool.ExecuteAsync(
                toolArguments,
                new McpToolContext(workingTracks, context.Now),
                cancellationToken);
            List<RadarScenarioDto> toolResults = toolResult.RadarActions;
            results.AddRange(toolResults);
            ApplyToWorkingTracks(workingTracks, toolResults);
        }

        return McpCallResult.FromActions(results);
    }

    private void ApplyToWorkingTracks(
        List<TrackData> tracks,
        IEnumerable<RadarScenarioDto> scenarios)
    {
        foreach (RadarScenarioDto scenario in scenarios)
        {
            if (scenario.MessageType == "SCHEDULED_ACTION") continue;
            LlmSenaryoSonucu result = _validator.DogrulaVeDonustur(scenario);
            switch (result.MessageType)
            {
                case MessageType.TrackCreated:
                    tracks.Add(result.TrackData!);
                    break;
                case MessageType.TrackUpdated:
                    Replace(tracks, result.TrackData!);
                    break;
                case MessageType.TrackDropped:
                    tracks.RemoveAll(x => x.TrackId == result.TrackDroppedData!.TrackId);
                    break;
                case MessageType.TeshisUpdated:
                    {
                        int index = tracks.FindIndex(x => x.TrackId == result.TeshisUpdatedData!.TrackId);
                        if (index >= 0)
                            tracks[index] = tracks[index] with { Teshis = result.TeshisUpdatedData.YeniTeshis };
                        break;
                    }
                case MessageType.TasnifUpdated:
                    {
                        int index = tracks.FindIndex(x => x.TrackId == result.TasnifUpdatedData!.TrackId);
                        if (index >= 0)
                            tracks[index] = tracks[index] with { Tasnif = result.TasnifUpdatedData.YeniTasnif };
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
}
