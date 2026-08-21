using System.Text.Json;
using HavaIziSimulator.LogEkleme;

namespace HavaIziSimulator.LogAnalysis;

/// <summary>
/// JSONL logunu Groq'ya ham satırlar halinde göndermek yerine kategorik durum
/// aralıklarına ve sayısal özetlere indirger. Replay davranışına dokunmaz.
/// </summary>
public sealed class LogAnalysisContextBuilder
{
    private readonly LogEklemeService _reader = new();
    private string? _cachedPath;
    private DateTime _cachedWriteTimeUtc;
    private string? _cachedContext;

    public async Task<string?> BuildAsync(
        string? filePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return null;
        DateTime writeTime = File.GetLastWriteTimeUtc(filePath);
        if (_cachedPath == filePath && _cachedWriteTimeUtc == writeTime) return _cachedContext;

        IReadOnlyList<IcdLogRecord> records = await _reader.DosyayiOkuAsync(filePath, cancellationToken);
        var states = new Dictionary<ushort, MutableState>();
        var intervals = new List<object>();
        var droppedTracks = new List<object>();
        var histories = new Dictionary<ushort, TrackHistory>();

        foreach (IcdLogRecord record in records.OrderBy(x => x.Header.TimestampEpochMillis))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string type = record.MessageType.Trim().ToUpperInvariant();
            if (!TryReadTrackId(record.Payload, out ushort trackId)) continue;
            long time = checked((long)record.Header.TimestampEpochMillis);
            if (!histories.TryGetValue(trackId, out TrackHistory? history))
            {
                history = new TrackHistory(trackId);
                histories.Add(trackId, history);
            }

            if (type is "TRACK_CREATED" or "TRACK_UPDATED")
            {
                string teshis = ReadString(record.Payload, "teshis") ?? "BILINMEYEN";
                string tasnif = ReadString(record.Payload, "tasnif") ?? "BILINMIYOR";
                string yonelim = ReadString(record.Payload, "yonelim") ?? "BILINMIYOR";
                int? hiz = ReadInt(record.Payload, "hiz");
                int? yukseklik = ReadInt(record.Payload, "yukseklik");
                double? enlem = ReadDouble(record.Payload, "enlem");
                double? boylam = ReadDouble(record.Payload, "boylam");
                history.AddState(teshis, tasnif, yonelim);
                if (!states.TryGetValue(trackId, out MutableState? currentState))
                    states[trackId] = MutableState.Create(
                        trackId, time, teshis, tasnif, yonelim,
                        hiz, yukseklik, enlem, boylam);
                else if (currentState.Teshis != teshis ||
                         currentState.Tasnif != tasnif ||
                         currentState.Yonelim != yonelim)
                {
                    intervals.Add(ToInterval(currentState, time));
                    states[trackId] = MutableState.Create(
                        trackId, time, teshis, tasnif, yonelim,
                        hiz, yukseklik, enlem, boylam);
                }
                else
                    states[trackId] = currentState.WithMeasurements(
                        hiz, yukseklik, enlem, boylam);
            }
            else if (type == "TESHIS_UPDATED" && states.TryGetValue(trackId, out MutableState? diagnosisState))
            {
                string yeniTeshis = ReadString(record.Payload, "yeniTeshis") ?? diagnosisState.Teshis;
                history.AddDiagnosis(yeniTeshis);
                intervals.Add(ToInterval(diagnosisState, time));
                states[trackId] = diagnosisState.Restart(
                    time,
                    teshis: yeniTeshis);
            }
            else if (type == "TASNIF_UPDATED" && states.TryGetValue(trackId, out MutableState? classificationState))
            {
                string yeniTasnif = ReadString(record.Payload, "yeniTasnif") ?? classificationState.Tasnif;
                history.AddClassification(yeniTasnif);
                intervals.Add(ToInterval(classificationState, time));
                states[trackId] = classificationState.Restart(
                    time,
                    tasnif: yeniTasnif);
            }
            else if (type == "TRACK_DROPPED")
            {
                if (states.Remove(trackId, out MutableState? droppedState))
                    intervals.Add(ToInterval(droppedState, time));
                string neden = ReadString(record.Payload, "neden") ?? "BILINMIYOR";
                history.MarkDropped(time, neden);
                droppedTracks.Add(new
                {
                    trackId,
                    timestampEpochMillis = time,
                    localTime = ToLocalTime(time),
                    neden
                });
            }
        }

        long end = records.Count == 0 ? 0 : checked((long)records.Max(x => x.Header.TimestampEpochMillis));
        intervals.AddRange(states.Values.Select(x => ToInterval(x, end)));
        TrackHistory[] orderedHistories = histories.Values.OrderBy(x => x.TrackId).ToArray();
        _cachedContext = JsonSerializer.Serialize(new
        {
            fileName = Path.GetFileName(filePath),
            recordCount = records.Count,
            startEpochMillis = records.Count == 0 ? 0L : checked((long)records.Min(x => x.Header.TimestampEpochMillis)),
            endEpochMillis = end,
            intervals,
            droppedTracks,
            trackHistory = orderedHistories.Select(x => x.ToSummary()),
            historicalIndexes = new
            {
                teshis = BuildIndex(orderedHistories, x => x.Diagnoses),
                tasnif = BuildIndex(orderedHistories, x => x.Classifications),
                yonelim = BuildIndex(orderedHistories, x => x.Directions)
            }
        });
        _cachedPath = filePath;
        _cachedWriteTimeUtc = writeTime;
        return _cachedContext;
    }

    private static object ToInterval(MutableState state, long to) => new
    {
        trackId = state.TrackId,
        fromEpochMillis = state.From,
        toEpochMillis = to,
        fromLocal = ToLocalTime(state.From),
        toLocal = ToLocalTime(to),
        teshis = state.Teshis,
        tasnif = state.Tasnif,
        yonelim = state.Yonelim,
        sonHiz = state.Hiz,
        minHiz = state.MinHiz,
        maxHiz = state.MaxHiz,
        sonYukseklik = state.Yukseklik,
        minYukseklik = state.MinYukseklik,
        maxYukseklik = state.MaxYukseklik,
        sonEnlem = state.Enlem,
        sonBoylam = state.Boylam
    };

    private static string ToLocalTime(long epochMillis) =>
        DateTimeOffset.FromUnixTimeMilliseconds(epochMillis)
            .ToLocalTime()
            .ToString("yyyy-MM-dd HH:mm:ss.fff");

    private static bool TryReadTrackId(JsonElement payload, out ushort trackId)
    {
        trackId = 0;
        return payload.ValueKind == JsonValueKind.Object &&
               payload.TryGetProperty("trackId", out JsonElement id) &&
               id.TryGetUInt16(out trackId);
    }

    private static string? ReadString(JsonElement payload, string name) =>
        payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty(name, out JsonElement value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString()?.ToUpperInvariant() : null;

    private static int? ReadInt(JsonElement payload, string name) =>
        payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty(name, out JsonElement value) &&
        value.TryGetInt32(out int result) ? result : null;

    private static double? ReadDouble(JsonElement payload, string name) =>
        payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty(name, out JsonElement value) &&
        value.TryGetDouble(out double result) ? result : null;

    private static object[] BuildIndex(
        IEnumerable<TrackHistory> histories,
        Func<TrackHistory, IReadOnlyList<string>> values) => histories
        .SelectMany(history => values(history).Select(value => new { value, history.TrackId }))
        .GroupBy(x => x.value, StringComparer.Ordinal)
        .OrderBy(group => group.Key, StringComparer.Ordinal)
        .Select(group => (object)new
        {
            value = group.Key,
            trackIds = group.Select(x => x.TrackId).Distinct().OrderBy(x => x).ToArray()
        })
        .ToArray();

    private sealed class TrackHistory(ushort trackId)
    {
        private readonly List<string> _diagnoses = [];
        private readonly List<string> _classifications = [];
        private readonly List<string> _directions = [];

        public ushort TrackId { get; } = trackId;
        public IReadOnlyList<string> Diagnoses => _diagnoses;
        public IReadOnlyList<string> Classifications => _classifications;
        public IReadOnlyList<string> Directions => _directions;
        public bool Dropped { get; private set; }
        public long? DropTime { get; private set; }
        public string? DropReason { get; private set; }

        public void AddState(string diagnosis, string classification, string direction)
        {
            AddDistinct(_diagnoses, diagnosis);
            AddDistinct(_classifications, classification);
            AddDistinct(_directions, direction);
        }

        public void AddDiagnosis(string value) => AddDistinct(_diagnoses, value);
        public void AddClassification(string value) => AddDistinct(_classifications, value);

        public void MarkDropped(long time, string reason)
        {
            Dropped = true;
            DropTime = time;
            DropReason = reason;
        }

        public object ToSummary() => new
        {
            trackId = TrackId,
            herhangiBirZamandakiTeshisler = _diagnoses,
            herhangiBirZamandakiTasniflar = _classifications,
            herhangiBirZamandakiYonelimler = _directions,
            dropped = Dropped,
            dropTimeEpochMillis = DropTime,
            dropLocalTime = DropTime.HasValue ? ToLocalTime(DropTime.Value) : null,
            dropReason = DropReason
        };

        private static void AddDistinct(List<string> values, string value)
        {
            if (!values.Contains(value, StringComparer.Ordinal)) values.Add(value);
        }
    }

    private sealed record MutableState(
        ushort TrackId,
        long From,
        string Teshis,
        string Tasnif,
        string Yonelim,
        int? Hiz,
        int? MinHiz,
        int? MaxHiz,
        int? Yukseklik,
        int? MinYukseklik,
        int? MaxYukseklik,
        double? Enlem,
        double? Boylam)
    {
        public static MutableState Create(
            ushort trackId, long from, string teshis, string tasnif, string yonelim,
            int? hiz, int? yukseklik, double? enlem, double? boylam) =>
            new(trackId, from, teshis, tasnif, yonelim,
                hiz, hiz, hiz, yukseklik, yukseklik, yukseklik, enlem, boylam);

        public MutableState WithMeasurements(
            int? hiz, int? yukseklik, double? enlem, double? boylam) => this with
        {
            Hiz = hiz ?? Hiz,
            MinHiz = Min(MinHiz, hiz),
            MaxHiz = Max(MaxHiz, hiz),
            Yukseklik = yukseklik ?? Yukseklik,
            MinYukseklik = Min(MinYukseklik, yukseklik),
            MaxYukseklik = Max(MaxYukseklik, yukseklik),
            Enlem = enlem ?? Enlem,
            Boylam = boylam ?? Boylam
        };

        public MutableState Restart(long from, string? teshis = null, string? tasnif = null) =>
            Create(TrackId, from, teshis ?? Teshis, tasnif ?? Tasnif, Yonelim,
                Hiz, Yukseklik, Enlem, Boylam);

        private static int? Min(int? left, int? right) =>
            left.HasValue && right.HasValue ? Math.Min(left.Value, right.Value) : left ?? right;

        private static int? Max(int? left, int? right) =>
            left.HasValue && right.HasValue ? Math.Max(left.Value, right.Value) : left ?? right;
    }
}
