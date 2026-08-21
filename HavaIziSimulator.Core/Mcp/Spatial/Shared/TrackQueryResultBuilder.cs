using System.Text.Json;
using HavaIziSimulator.Llm;
using IcdLib.Models;

namespace HavaIziSimulator.Mcp.Spatial.Shared;

public static class TrackQueryResultBuilder
{
    public static McpCallResult Build(
        JsonElement arguments,
        IReadOnlyList<TrackData> matches,
        object? scope = null)
    {
        string operation = arguments.GetProperty("operation").GetString() ?? "list";
        if (operation is not ("count" or "list" or "summary"))
            throw new ArgumentException("operation count, list veya summary olmalıdır.");
        IEnumerable<TrackData> ordered = Sort(matches, arguments);
        int? limit = ReadNullableInt(arguments, "limit");
        TrackData[] returned = (limit.HasValue ? ordered.Take(limit.Value) : ordered).ToArray();

        object structured = new
        {
            operation,
            scope,
            count = matches.Count,
            returnedCount = returned.Length,
            trackIds = matches.Select(x => x.TrackId).OrderBy(x => x).ToArray(),
            tracks = operation == "count"
                ? Array.Empty<object>()
                : Services.TrackQueryService.ToResultRows(returned),
            summary = new
            {
                byDiagnosis = Group(matches, x => x.Teshis.ToString()),
                byClassification = Group(matches, x => x.Tasnif.ToString()),
                byDirection = Group(matches, x => x.Yonelim.ToString())
            }
        };
        return new McpCallResult { StructuredContent = JsonSerializer.SerializeToElement(structured) };
    }

    private static IEnumerable<TrackData> Sort(IEnumerable<TrackData> tracks, JsonElement arguments)
    {
        string sortBy = ReadNullableString(arguments, "sortBy") ?? "trackId";
        bool descending = arguments.TryGetProperty("descending", out JsonElement desc) &&
                          desc.ValueKind is JsonValueKind.True or JsonValueKind.False && desc.GetBoolean();
        Func<TrackData, double> key = sortBy switch
        {
            "hiz" => x => x.Hiz,
            "yukseklik" => x => x.Yukseklik,
            _ => x => x.TrackId
        };
        return descending ? tracks.OrderByDescending(key) : tracks.OrderBy(key);
    }

    private static Dictionary<string, int> Group<T>(IEnumerable<T> values, Func<T, string> selector) =>
        values.GroupBy(x => selector(x).ToUpperInvariant())
            .ToDictionary(x => x.Key, x => x.Count(), StringComparer.Ordinal);

    private static string? ReadNullableString(JsonElement value, string name) =>
        value.TryGetProperty(name, out JsonElement property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() : null;

    private static int? ReadNullableInt(JsonElement value, string name) =>
        value.TryGetProperty(name, out JsonElement property) && property.TryGetInt32(out int number)
            ? number : null;
}
