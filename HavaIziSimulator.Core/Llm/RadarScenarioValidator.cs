using System.Text.Json;
using IcdLib;
using IcdLib.Enums;
using IcdLib.Models;

namespace HavaIziSimulator.Llm;

public sealed class RadarScenarioValidator
{
    private static readonly JsonSerializerOptions JsonSecenekleri = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// MCP aracının ürettiği senaryo dizisindeki her elemanı doğrular ve
    /// tipe çevirir. Herhangi bir eleman geçersizse tüm liste reddedilir
    /// (kısmi/tutarsız bir senaryo gönderilmesin diye) — hata mesajı
    /// hangi elemanın (kaçıncı sırada) sorunlu olduğunu belirtir.
    /// </summary>
    public IReadOnlyList<LlmSenaryoSonucu> DogrulaVeDonusturListe(
        IReadOnlyList<RadarScenarioDto> senaryolar)
    {
        if (senaryolar is null || senaryolar.Count == 0)
            return [];

        var sonuclar = new List<LlmSenaryoSonucu>(senaryolar.Count);

        for (int i = 0; i < senaryolar.Count; i++)
        {
            try
            {
                sonuclar.Add(DogrulaVeDonustur(senaryolar[i]));
            }
            catch (Exception ex)
            {
                throw new ArgumentException(
                    $"{i + 1}. mesaj geçersiz: {ex.Message}", ex);
            }
        }

        return sonuclar;
    }

    public LlmSenaryoSonucu DogrulaVeDonustur(RadarScenarioDto senaryo)
    {
        if (senaryo is null)
            throw new ArgumentException("Senaryo boş olamaz.");

        if (!Enum.TryParse(
                Normalize(senaryo.MessageType),
                true,
                out MessageType tip))
        {
            throw new ArgumentException(
                $"Bilinmeyen messageType: {senaryo.MessageType}");
        }

        return tip switch
        {
            MessageType.TrackCreated or MessageType.TrackUpdated =>
                new LlmSenaryoSonucu(tip, TrackData: DogrulaTrackData(senaryo.Payload)),

            MessageType.TrackDropped =>
                new LlmSenaryoSonucu(tip, TrackDroppedData: DogrulaTrackDropped(senaryo.Payload)),

            MessageType.TeshisUpdated =>
                new LlmSenaryoSonucu(tip, TeshisUpdatedData: DogrulaTeshisUpdated(senaryo.Payload)),

            MessageType.TasnifUpdated =>
                new LlmSenaryoSonucu(tip, TasnifUpdatedData: DogrulaTasnifUpdated(senaryo.Payload)),

            MessageType.Heartbeat =>
                new LlmSenaryoSonucu(tip),

            _ => throw new ArgumentException($"Desteklenmeyen messageType: {tip}")
        };
    }

    private TrackData DogrulaTrackData(JsonElement payloadJson)
    {
        TrackPayloadDto payload =
            payloadJson.Deserialize<TrackPayloadDto>(JsonSecenekleri)
            ?? throw new ArgumentException("Payload boş olamaz.");

        if (payload.TrackId < IcdConstants.MinTrackId || payload.TrackId > IcdConstants.MaxTrackId)
            throw new ArgumentException($"Track ID geçersiz: {payload.TrackId}");

        if (payload.Hiz < IcdConstants.MinHiz || payload.Hiz > IcdConstants.MaxHiz)
            throw new ArgumentException($"Hız geçersiz: {payload.Hiz}");

        if (payload.Yukseklik < IcdConstants.MinYukseklik || payload.Yukseklik > IcdConstants.MaxYukseklik)
            throw new ArgumentException($"Yükseklik geçersiz: {payload.Yukseklik}");

        if (payload.Enlem < IcdConstants.MinEnlem || payload.Enlem > IcdConstants.MaxEnlem)
            throw new ArgumentException($"Enlem geçersiz: {payload.Enlem}");

        if (payload.Boylam < IcdConstants.MinBoylam || payload.Boylam > IcdConstants.MaxBoylam)
            throw new ArgumentException($"Boylam geçersiz: {payload.Boylam}");

        Yonelim yonelim = ParseEnum<Yonelim>(payload.Yonelim, "Yönelim");
        Teshis teshis = ParseEnum<Teshis>(payload.Teshis, "Teşhis");
        Tasnif tasnif = ParseEnum<Tasnif>(payload.Tasnif, "Tasnif");

        return new TrackData(
            checked((ushort)payload.TrackId),
            checked((ushort)payload.Hiz),
            checked((ushort)payload.Yukseklik),
            yonelim,
            teshis,
            tasnif,
            payload.Enlem,
            payload.Boylam,
            EpochTime.NowMillis());
    }

    private TrackDroppedData DogrulaTrackDropped(JsonElement payloadJson)
    {
        TrackDroppedPayloadDto payload =
            payloadJson.Deserialize<TrackDroppedPayloadDto>(JsonSecenekleri)
            ?? throw new ArgumentException("Payload boş olamaz.");

        if (payload.TrackId < IcdConstants.MinTrackId || payload.TrackId > IcdConstants.MaxTrackId)
            throw new ArgumentException($"Track ID geçersiz: {payload.TrackId}");

        DropReason neden = ParseEnum<DropReason>(Normalize(payload.Neden), "Düşme nedeni");

        return new TrackDroppedData(
            checked((ushort)payload.TrackId),
            EpochTime.NowMillis(),
            neden);
    }

    private TeshisUpdatedData DogrulaTeshisUpdated(JsonElement payloadJson)
    {
        TeshisUpdatedPayloadDto payload =
            payloadJson.Deserialize<TeshisUpdatedPayloadDto>(JsonSecenekleri)
            ?? throw new ArgumentException("Payload boş olamaz.");

        if (payload.TrackId < IcdConstants.MinTrackId || payload.TrackId > IcdConstants.MaxTrackId)
            throw new ArgumentException($"Track ID geçersiz: {payload.TrackId}");

        Teshis teshis = ParseEnum<Teshis>(payload.YeniTeshis, "Teşhis");

        return new TeshisUpdatedData(
            checked((ushort)payload.TrackId),
            teshis,
            EpochTime.NowMillis());
    }

    private TasnifUpdatedData DogrulaTasnifUpdated(JsonElement payloadJson)
    {
        TasnifUpdatedPayloadDto payload =
            payloadJson.Deserialize<TasnifUpdatedPayloadDto>(JsonSecenekleri)
            ?? throw new ArgumentException("Payload boş olamaz.");

        if (payload.TrackId < IcdConstants.MinTrackId || payload.TrackId > IcdConstants.MaxTrackId)
            throw new ArgumentException($"Track ID geçersiz: {payload.TrackId}");

        Tasnif tasnif = ParseEnum<Tasnif>(payload.YeniTasnif, "Tasnif");

        return new TasnifUpdatedData(
            checked((ushort)payload.TrackId),
            tasnif,
            EpochTime.NowMillis());
    }

    private static TEnum ParseEnum<TEnum>(string deger, string alanAdi) where TEnum : struct, Enum
    {
        if (!Enum.TryParse(Normalize(deger), true, out TEnum sonuc))
            throw new ArgumentException($"{alanAdi} geçersiz: {deger}");

        return sonuc;
    }

    /// <summary>
    /// LLM çıktısındaki "SINYAL_KAYBI" gibi alt çizgili değerleri
    /// IcdLib enum adlarına ("SinyalKaybi") çevirmeden Enum.TryParse'ın
    /// case-insensitive eşleşebilmesi için alt çizgileri kaldırır.
    /// </summary>
    private static string Normalize(string deger) =>
        deger.Replace("_", string.Empty);
}
