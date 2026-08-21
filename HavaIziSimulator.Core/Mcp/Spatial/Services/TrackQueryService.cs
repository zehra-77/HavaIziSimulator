using System.Text.Json;
using IcdLib.Models;

namespace HavaIziSimulator.Mcp.Spatial.Services;

public static class TrackQueryService
{
    public static IReadOnlyList<TrackData> ApplyFilter(
        IReadOnlyList<TrackData> tracks,
        JsonElement? filter)
    {
        if (filter is null || filter.Value.ValueKind == JsonValueKind.Null)
            return tracks.ToList();
        JsonElement value = filter.Value;
        if (value.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("filter bir JSON nesnesi olmalıdır.");

        EnsureAllowed(value,
            "trackIds", "teshis", "tasnif", "yonelim",
            "minHiz", "maxHiz", "minYukseklik", "maxYukseklik");
        HashSet<ushort>? ids = ReadIds(value);
        string? teshis = ReadString(value, "teshis");
        string? tasnif = ReadString(value, "tasnif");
        string? yonelim = ReadString(value, "yonelim");
        int? minHiz = ReadInt(value, "minHiz");
        int? maxHiz = ReadInt(value, "maxHiz");
        int? minYukseklik = ReadInt(value, "minYukseklik");
        int? maxYukseklik = ReadInt(value, "maxYukseklik");

        return tracks.Where(track =>
                (ids is null || ids.Contains(track.TrackId)) &&
                (teshis is null || Equal(track.Teshis.ToString(), teshis)) &&
                (tasnif is null || Equal(track.Tasnif.ToString(), tasnif)) &&
                (yonelim is null || Equal(track.Yonelim.ToString(), yonelim)) &&
                (!minHiz.HasValue || track.Hiz >= minHiz) &&
                (!maxHiz.HasValue || track.Hiz <= maxHiz) &&
                (!minYukseklik.HasValue || track.Yukseklik >= minYukseklik) &&
                (!maxYukseklik.HasValue || track.Yukseklik <= maxYukseklik))
            .ToList();
    }

    public static object[] ToResultRows(IEnumerable<TrackData> tracks) => tracks
        .Select(track => (object)new
        {
            trackId = track.TrackId,
            hiz = track.Hiz,
            yukseklik = track.Yukseklik,
            yonelim = track.Yonelim.ToString().ToUpperInvariant(),
            teshis = track.Teshis.ToString().ToUpperInvariant(),
            tasnif = track.Tasnif.ToString().ToUpperInvariant(),
            enlem = track.Enlem,
            boylam = track.Boylam
        }).ToArray();

    private static bool Equal(string left, string right) =>
        Normalize(left) == Normalize(right);

    private static string Normalize(string value) =>
        value.Replace("_", string.Empty).ToUpperInvariant();

    private static string? ReadString(JsonElement value, string name)
    {
        if (!value.TryGetProperty(name, out JsonElement property) ||
            property.ValueKind == JsonValueKind.Null) return null;
        if (property.ValueKind != JsonValueKind.String)
            throw new ArgumentException($"filter.{name} metin olmalıdır.");
        return property.GetString();
    }

    private static int? ReadInt(JsonElement value, string name)
    {
        if (!value.TryGetProperty(name, out JsonElement property) ||
            property.ValueKind == JsonValueKind.Null) return null;
        if (!property.TryGetInt32(out int number))
            throw new ArgumentException($"filter.{name} tam sayı olmalıdır.");
        return number;
    }

    private static HashSet<ushort>? ReadIds(JsonElement value)
    {
        if (!value.TryGetProperty("trackIds", out JsonElement ids) || ids.ValueKind == JsonValueKind.Null)
            return null;
        if (ids.ValueKind != JsonValueKind.Array)
            throw new ArgumentException("filter.trackIds dizi olmalıdır.");
        return ids.EnumerateArray().Select(x => x.TryGetUInt16(out ushort id)
            ? id
            : throw new ArgumentException("filter.trackIds yalnız geçerli ID içermelidir.")).ToHashSet();
    }

    private static void EnsureAllowed(JsonElement value, params string[] allowed)
    {
        HashSet<string> names = allowed.ToHashSet(StringComparer.Ordinal);
        string[] unexpected = value.EnumerateObject()
            .Select(x => x.Name)
            .Where(x => !names.Contains(x))
            .ToArray();
        if (unexpected.Length > 0)
            throw new ArgumentException($"Beklenmeyen filter alanı: {string.Join(", ", unexpected)}");
    }
}
