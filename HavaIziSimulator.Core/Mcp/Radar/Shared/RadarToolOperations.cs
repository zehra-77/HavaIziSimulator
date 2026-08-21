using System.Globalization;
using System.Text.Json;
using IcdLib;
using IcdLib.Enums;
using IcdLib.Models;

namespace HavaIziSimulator.Llm;

/// <summary>
/// MCP sunucusunun çağırdığı radar iş kuralları. Model yalnızca araç ve
/// parametre seçer; adet, ID, filtre ve ICD doğruluğu burada uygulanır.
/// </summary>
public static class RadarToolOperations
{
    internal static List<RadarScenarioDto> CreateTracks(JsonElement args, IReadOnlyList<TrackData> activeTracks)
    {
        var occupied = activeTracks.Select(x => x.TrackId).ToHashSet();
        if (args.TryGetProperty("groups", out JsonElement groups))
        {
            EnsureAllowedProperties(args, "groups");
            if (groups.ValueKind != JsonValueKind.Array || groups.GetArrayLength() == 0)
                throw new ArgumentException("groups boş olmayan bir dizi olmalıdır.");

            var groupedResults = new List<RadarScenarioDto>();
            foreach (JsonElement group in groups.EnumerateArray())
                groupedResults.AddRange(CreateTrackGroup(group, occupied));
            return groupedResults;
        }

        // Eski count tabanlı doğrudan MCP çağrılarıyla uyumluluk korunur.
        return CreateTrackGroup(args, occupied);
    }

    private static List<RadarScenarioDto> CreateTrackGroup(
        JsonElement args,
        HashSet<ushort> occupied)
    {
        EnsureAllowedProperties(args,
            "count", "trackIds", "hiz", "yukseklik", "yonelim",
            "teshis", "tasnif", "enlem", "boylam");

        int count = ReadRequiredInt(args, "count");
        if (count is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(count), "Adet 1-100 arasında olmalıdır.");

        int[] requestedIds = args.TryGetProperty("trackIds", out JsonElement ids) &&
                             ids.ValueKind != JsonValueKind.Null
            ? ReadIntArray(ids, "trackIds")
            : [];
        if (requestedIds.Length > 0 && requestedIds.Length != count)
            throw new ArgumentException("trackIds sayısı count ile aynı olmalıdır.");
        if (requestedIds.Distinct().Count() != requestedIds.Length)
            throw new ArgumentException("trackIds içinde tekrar eden ID olamaz.");

        var results = new List<RadarScenarioDto>(count);
        for (int i = 0; i < count; i++)
        {
            ushort trackId = requestedIds.Length > 0
                ? ValidateNewTrackId(requestedIds[i], occupied)
                : RandomUniqueTrackId(occupied);
            occupied.Add(trackId);

            results.Add(Scenario("TRACK_CREATED", new TrackPayloadDto
            {
                TrackId = trackId,
                Hiz = ReadOptionalInt(args, "hiz") ?? Random.Shared.Next(IcdConstants.MinHiz, IcdConstants.MaxHiz + 1),
                Yukseklik = ReadOptionalInt(args, "yukseklik") ?? Random.Shared.Next(IcdConstants.MinYukseklik, IcdConstants.MaxYukseklik + 1),
                Yonelim = ReadOptionalString(args, "yonelim") ?? RandomEnum<Yonelim>(),
                Teshis = ReadOptionalString(args, "teshis") ?? RandomEnum<Teshis>(),
                Tasnif = ReadOptionalString(args, "tasnif") ?? RandomEnum<Tasnif>(),
                Enlem = ReadOptionalDouble(args, "enlem") ?? RandomRange(IcdConstants.MinEnlem, IcdConstants.MaxEnlem),
                Boylam = ReadOptionalDouble(args, "boylam") ?? RandomRange(IcdConstants.MinBoylam, IcdConstants.MaxBoylam)
            }));
        }
        return results;
    }

    internal static List<RadarScenarioDto> DropTracks(JsonElement args, IReadOnlyList<TrackData> activeTracks)
    {
        List<TrackSnapshot> active = activeTracks.Select(ToSnapshot).ToList();
        EnsureAllowedProperties(args, "field", "operator", "value", "limit", "random", "neden");
        string reason = ReadRequiredString(args, "neden");
        EnsureOneOf(reason, "neden", "SINYAL_KAYBI", "KAPSAMA_ALANI_DISI", "MANUEL_SONLANDIRMA", "DIGER");
        return MatchingTracks(args, active)
            .Select(x => Scenario("TRACK_DROPPED", new { trackId = x.TrackId, neden = reason }))
            .ToList();
    }

    internal static List<RadarScenarioDto> UpdateDiagnosis(JsonElement args, IReadOnlyList<TrackData> activeTracks)
    {
        List<TrackSnapshot> active = activeTracks.Select(ToSnapshot).ToList();
        EnsureAllowedProperties(args, "field", "operator", "value", "limit", "random", "yeniTeshis");
        string diagnosis = ReadRequiredString(args, "yeniTeshis");
        EnsureOneOf(diagnosis, "yeniTeshis", "BILINMEYEN", "DOST", "DUSMAN", "TARAFSIZ");
        return MatchingTracks(args, active)
            .Select(x => Scenario("TESHIS_UPDATED", new { trackId = x.TrackId, yeniTeshis = diagnosis }))
            .ToList();
    }

    internal static List<RadarScenarioDto> UpdateClassification(JsonElement args, IReadOnlyList<TrackData> activeTracks)
    {
        List<TrackSnapshot> active = activeTracks.Select(ToSnapshot).ToList();
        EnsureAllowedProperties(args, "field", "operator", "value", "limit", "random", "yeniTasnif");
        string classification = ReadRequiredString(args, "yeniTasnif");
        EnsureOneOf(classification, "yeniTasnif", "BILINMIYOR", "UCAK", "DONERKANAT", "FUZE", "IHA");
        return MatchingTracks(args, active)
            .Select(x => Scenario("TASNIF_UPDATED", new { trackId = x.TrackId, yeniTasnif = classification }))
            .ToList();
    }

    internal static List<RadarScenarioDto> UpdateTracks(JsonElement args, IReadOnlyList<TrackData> activeTracks)
    {
        List<TrackSnapshot> active = activeTracks.Select(ToSnapshot).ToList();
        EnsureAllowedProperties(args,
            "field", "operator", "value", "limit", "random",
            "hiz", "yukseklik", "yonelim", "enlem", "boylam");
        if (!new[] { "hiz", "yukseklik", "yonelim", "enlem", "boylam" }
            .Any(name => args.TryGetProperty(name, out JsonElement value) &&
                         value.ValueKind != JsonValueKind.Null))
            throw new ArgumentException("En az bir güncelleme alanı verilmelidir.");

        int? hiz = ReadOptionalInt(args, "hiz");
        int? yukseklik = ReadOptionalInt(args, "yukseklik");
        string? yonelim = ReadOptionalString(args, "yonelim");
        double? enlem = ReadOptionalDouble(args, "enlem");
        double? boylam = ReadOptionalDouble(args, "boylam");
        EnsureRange(hiz, "hiz", IcdConstants.MinHiz, IcdConstants.MaxHiz);
        EnsureRange(yukseklik, "yukseklik", IcdConstants.MinYukseklik, IcdConstants.MaxYukseklik);
        EnsureRange(enlem, "enlem", IcdConstants.MinEnlem, IcdConstants.MaxEnlem);
        EnsureRange(boylam, "boylam", IcdConstants.MinBoylam, IcdConstants.MaxBoylam);
        if (yonelim is not null) EnsureOneOf(yonelim, "yonelim", "KUZEY", "GUNEY", "DOGU", "BATI");

        return MatchingTracks(args, active)
            .Select(x => Scenario("TRACK_UPDATED", new TrackPayloadDto
            {
                TrackId = x.TrackId,
                Hiz = hiz ?? x.Hiz,
                Yukseklik = yukseklik ?? x.Yukseklik,
                Yonelim = yonelim ?? x.Yonelim,
                Teshis = x.Teshis,
                Tasnif = x.Tasnif,
                Enlem = enlem ?? x.Enlem,
                Boylam = boylam ?? x.Boylam
            })).ToList();
    }

    internal static List<RadarScenarioDto> SendHeartbeat(JsonElement args)
    {
        EnsureAllowedProperties(args);
        return [Scenario("HEARTBEAT", new { })];
    }

    private static IReadOnlyList<TrackSnapshot> MatchingTracks(JsonElement args, List<TrackSnapshot> active)
    {
        string field = ReadRequiredString(args, "field");
        string op = ReadRequiredString(args, "operator");
        if (!new[] { "all", "trackId", "hiz", "yukseklik", "yonelim", "teshis", "tasnif" }
            .Contains(field, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException($"Geçersiz filtre alanı: {field}");
        if (!new[] { "eq", "lt", "lte", "gt", "gte" }.Contains(op, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException($"Geçersiz karşılaştırma operatörü: {op}");
        if (!args.TryGetProperty("value", out JsonElement value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            throw new ArgumentException("value zorunludur.");

        IEnumerable<TrackSnapshot> matches;
        if (field.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            if (!op.Equals("eq", StringComparison.OrdinalIgnoreCase) ||
                !value.ToString().Equals("all", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("field=all için operator=eq ve value=all kullanılmalıdır.");
            matches = active;
        }
        else
        {
            if (field.Equals("trackId", StringComparison.OrdinalIgnoreCase) ||
                field.Equals("hiz", StringComparison.OrdinalIgnoreCase) ||
                field.Equals("yukseklik", StringComparison.OrdinalIgnoreCase))
                _ = ReadFilterNumber(value, "value");
            else if (value.ValueKind != JsonValueKind.String)
                throw new ArgumentException($"{field} filtresinin value değeri metin olmalıdır.");
            matches = active.Where(track => Matches(track, field, op, value));
        }

        if (args.TryGetProperty("random", out JsonElement random) &&
            random.ValueKind != JsonValueKind.Null)
        {
            if (random.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
                throw new ArgumentException("random boolean olmalıdır.");
            if (random.GetBoolean()) matches = matches.OrderBy(_ => Random.Shared.Next());
        }
        int? limit = ReadOptionalInt(args, "limit");
        if (limit is <= 0) throw new ArgumentOutOfRangeException(nameof(limit), "limit en az 1 olmalıdır.");
        return (limit.HasValue ? matches.Take(limit.Value) : matches).ToList();
    }

    private static bool Matches(TrackSnapshot track, string field, string op, JsonElement value)
    {
        if (field is "trackId" or "hiz" or "yukseklik")
        {
            double numericLeft = field switch { "trackId" => track.TrackId, "hiz" => track.Hiz, _ => track.Yukseklik };
            double numericRight = ReadFilterNumber(value, "value");
            return op.ToLowerInvariant() switch
            {
                "lt" => numericLeft < numericRight,
                "lte" => numericLeft <= numericRight,
                "gt" => numericLeft > numericRight,
                "gte" => numericLeft >= numericRight,
                "eq" => Math.Abs(numericLeft - numericRight) < 0.000001,
                _ => false
            };
        }

        if (field is not ("yonelim" or "teshis" or "tasnif"))
            throw new ArgumentException($"Geçersiz filtre alanı: {field}");
        if (!op.Equals("eq", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"{field} alanı yalnızca eq operatörünü destekler.");
        string textLeft = field switch { "yonelim" => track.Yonelim, "teshis" => track.Teshis, _ => track.Tasnif };
        return Normalize(textLeft) == Normalize(value.ToString());
    }

    private static TrackSnapshot ToSnapshot(TrackData track) => new(
        track.TrackId, track.Hiz, track.Yukseklik,
        track.Yonelim.ToString().ToUpperInvariant(),
        track.Teshis.ToString().ToUpperInvariant(),
        track.Tasnif.ToString().ToUpperInvariant(), track.Enlem, track.Boylam);

    private static RadarScenarioDto Scenario(string type, object payload) => new()
    {
        MessageType = type,
        Payload = JsonSerializer.SerializeToElement(payload)
    };

    private static ushort ValidateNewTrackId(int value, HashSet<ushort> occupied)
    {
        if (value < IcdConstants.MinTrackId || value > IcdConstants.MaxTrackId)
            throw new ArgumentOutOfRangeException(nameof(value), $"Track ID geçersiz: {value}");
        ushort id = checked((ushort)value);
        if (occupied.Contains(id)) throw new InvalidOperationException($"Track ID {id} zaten aktif.");
        return id;
    }

    private static ushort RandomUniqueTrackId(HashSet<ushort> occupied)
    {
        for (int i = 0; i < 20_000; i++)
        {
            ushort id = (ushort)Random.Shared.Next(IcdConstants.MinTrackId, IcdConstants.MaxTrackId + 1);
            if (!occupied.Contains(id)) return id;
        }
        throw new InvalidOperationException("Kullanılabilir benzersiz Track ID bulunamadı.");
    }

    private static int ReadRequiredInt(JsonElement args, string name) =>
        args.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int result)
            ? result : throw new ArgumentException($"{name} zorunlu bir tam sayıdır.");

    private static int? ReadOptionalInt(JsonElement args, string name)
    {
        if (!args.TryGetProperty(name, out JsonElement value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int result))
            throw new ArgumentException($"{name} tam sayı olmalıdır.");
        return result;
    }

    private static double? ReadOptionalDouble(JsonElement args, string name)
    {
        if (!args.TryGetProperty(name, out JsonElement value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out double result))
            throw new ArgumentException($"{name} sayısal olmalıdır.");
        return result;
    }

    private static string ReadRequiredString(JsonElement args, string name) =>
        ReadOptionalString(args, name) is { Length: > 0 } value
            ? value : throw new ArgumentException($"{name} zorunludur.");

    private static string? ReadOptionalString(JsonElement args, string name)
    {
        if (!args.TryGetProperty(name, out JsonElement value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.String) throw new ArgumentException($"{name} metin olmalıdır.");
        return value.GetString();
    }

    private static int[] ReadIntArray(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.Array) throw new ArgumentException($"{name} dizi olmalıdır.");
        return value.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.Number && x.TryGetInt32(out int id)
            ? id : throw new ArgumentException($"{name} yalnızca tam sayı içermelidir.")).ToArray();
    }

    private static void EnsureAllowedProperties(JsonElement args, params string[] allowed)
    {
        HashSet<string> names = allowed.ToHashSet(StringComparer.Ordinal);
        string[] unexpected = args.EnumerateObject()
            .Select(x => x.Name)
            .Where(x => !names.Contains(x))
            .ToArray();
        if (unexpected.Length > 0)
            throw new ArgumentException($"Beklenmeyen araç parametresi: {string.Join(", ", unexpected)}");
    }

    private static void EnsureOneOf(string value, string name, params string[] allowed)
    {
        if (!allowed.Contains(value, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException($"{name} geçersiz: {value}");
    }

    private static void EnsureRange(double? value, string name, double min, double max)
    {
        if (value.HasValue && (value.Value < min || value.Value > max))
            throw new ArgumentOutOfRangeException(name, $"{name} {min}-{max} aralığında olmalıdır.");
    }

    private static double ReadFilterNumber(JsonElement value, string name)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double number))
            return number;
        if (value.ValueKind == JsonValueKind.String &&
            double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number))
            return number;
        throw new ArgumentException($"{name} sayısal olmalıdır.");
    }

    private static string RandomEnum<T>() where T : struct, Enum
    {
        T[] values = Enum.GetValues<T>();
        return values[Random.Shared.Next(values.Length)].ToString()!.ToUpperInvariant();
    }

    private static double RandomRange(double min, double max) => min + Random.Shared.NextDouble() * (max - min);
    private static string Normalize(string value) => value.Replace("_", string.Empty).ToUpperInvariant();

    private sealed record TrackSnapshot(
        ushort TrackId, int Hiz, int Yukseklik, string Yonelim,
        string Teshis, string Tasnif, double Enlem, double Boylam);
}
