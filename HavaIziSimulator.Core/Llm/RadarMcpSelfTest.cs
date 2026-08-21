using System.Text.Json;
using IcdLib.Enums;
using IcdLib.Models;

namespace HavaIziSimulator.Llm;

public static class RadarMcpSelfTest
{
    public static async Task RunAsync()
    {
        using IRadarToolClient client = new RadarMcpProcessClient();

        IReadOnlyList<McpToolDefinition> tools = await client.ListToolsAsync();
        if (!tools.Any(x => x.Name == "radar_create_tracks") ||
            !tools.Any(x => x.Name == "radar_drop_tracks"))
            throw new InvalidOperationException("MCP tools/list testi başarısız.");

        List<RadarScenarioDto> created = (await client.CallToolAsync(
            "radar_create_tracks",
            JsonSerializer.SerializeToElement(new { count = 5, tasnif = "UCAK" }),
            [])).RadarActions;
        if (created.Count != 5)
            throw new InvalidOperationException($"Çoklu iz testi başarısız: 5 yerine {created.Count} üretildi.");
        int uniqueIds = created.Select(x => x.Payload.GetProperty("trackId").GetInt32()).Distinct().Count();
        if (uniqueIds != 5)
            throw new InvalidOperationException("Çoklu iz testi başarısız: Track ID'ler benzersiz değil.");

        TrackData[] activeTracks =
        [
            Track(101, 300),
            Track(102, 450),
            Track(103, 399)
        ];
        List<RadarScenarioDto> dropped = (await client.CallToolAsync(
            "radar_drop_tracks",
            JsonSerializer.SerializeToElement(new
            {
                field = "hiz",
                @operator = "lt",
                value = 400,
                neden = "MANUEL_SONLANDIRMA",
                limit = (int?)null,
                random = (bool?)null
            }),
            activeTracks)).RadarActions;
        int[] droppedIds = dropped
            .Select(x => x.Payload.GetProperty("trackId").GetInt32())
            .OrderBy(x => x)
            .ToArray();
        if (!droppedIds.SequenceEqual(new[] { 101, 103 }))
            throw new InvalidOperationException(
                $"Koşullu seçim testi başarısız: [{string.Join(", ", droppedIds)}]");

        List<RadarScenarioDto> classified = (await client.CallToolAsync(
            "radar_update_classification",
            JsonSerializer.SerializeToElement(new
            {
                field = "trackId",
                @operator = "eq",
                value = 102,
                yeniTasnif = "IHA"
            }),
            activeTracks)).RadarActions;
        if (classified.Count != 1 ||
            classified[0].Payload.GetProperty("trackId").GetInt32() != 102 ||
            classified[0].Payload.GetProperty("yeniTasnif").GetString() != "IHA")
            throw new InvalidOperationException("Sınıflandırma MCP testi başarısız.");

        List<RadarScenarioDto> heartbeat = (await client.CallToolAsync(
            "radar_send_heartbeat",
            JsonSerializer.SerializeToElement(new { }),
            activeTracks)).RadarActions;
        if (heartbeat.Count != 1 || heartbeat[0].MessageType != "HEARTBEAT")
            throw new InvalidOperationException("Heartbeat MCP testi başarısız.");

        bool rejectedUnsafeFilter = false;
        try
        {
            await client.CallToolAsync(
                "radar_drop_tracks",
                JsonSerializer.SerializeToElement(new { neden = "MANUEL_SONLANDIRMA" }),
                activeTracks);
        }
        catch (InvalidOperationException)
        {
            rejectedUnsafeFilter = true;
        }
        if (!rejectedUnsafeFilter)
            throw new InvalidOperationException("Eksik filtre güvenlik testi başarısız.");

        Console.WriteLine("MCP TEST OK: 5 benzersiz iz üretildi.");
        Console.WriteLine("MCP TEST OK: hiz < 400 koşulu null opsiyonlarla yalnızca 101 ve 103 izlerini seçti.");
        Console.WriteLine("MCP TEST OK: 102 numaralı iz IHA olarak sınıflandırıldı.");
        Console.WriteLine("MCP TEST OK: heartbeat ve eksik filtre reddi doğrulandı.");
    }

    private static TrackData Track(ushort id, ushort speed) => new(
        id,
        speed,
        1000,
        Yonelim.Kuzey,
        Teshis.Bilinmeyen,
        Tasnif.Ucak,
        39.9,
        32.8,
        0);
}
