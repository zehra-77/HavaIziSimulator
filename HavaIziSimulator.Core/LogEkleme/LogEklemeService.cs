using IcdLib;
using IcdLib.Enums;
using IcdLib.Models;
using System.Text.Json;

namespace HavaIziSimulator.LogEkleme;

/// <summary>
/// Java backend'in oluşturduğu JSONL log dosyasını okur
/// ve olayları zaman aralıklarını koruyarak simülatörde çalıştırır.
/// </summary>
public sealed class LogEklemeService
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// JSONL dosyasını okur ve olayları zaman sırasına dizer.
    /// </summary>
    public async Task<IReadOnlyList<IcdLogRecord>> DosyayiOkuAsync(
        string dosyaYolu,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(dosyaYolu))
        {
            throw new ArgumentException(
                "Log dosyasının yolu boş olamaz.",
                nameof(dosyaYolu));
        }

        if (!File.Exists(dosyaYolu))
        {
            throw new FileNotFoundException(
                "Log dosyası bulunamadı.",
                dosyaYolu);
        }

        var olaylar = new List<IcdLogRecord>();

        await using FileStream stream = File.OpenRead(dosyaYolu);
        using var reader = new StreamReader(stream);

        int satirNumarasi = 0;

        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string? satir = await reader.ReadLineAsync();
            satirNumarasi++;

            if (string.IsNullOrWhiteSpace(satir))
            {
                continue;
            }

            try
            {
                IcdLogRecord? olay =
                    JsonSerializer.Deserialize<IcdLogRecord>(
                        satir,
                        _jsonOptions);

                if (olay is null)
                {
                    throw new JsonException(
                        "Satır boş bir nesne üretti.");
                }

                LogKaydiniDogrula(olay, satirNumarasi);
                olaylar.Add(olay);
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException(
                    $"Log dosyasının {satirNumarasi}. satırı okunamadı.",
                    ex);
            }
        }

        return olaylar
            .OrderBy(x => x.Header.TimestampEpochMillis)
            .ToList();
    }

    /// <summary>
    /// Log olaylarını zaman aralıklarını koruyarak çalıştırır.
    /// </summary>
    public async Task OlaylariCalistirAsync(
        IReadOnlyList<IcdLogRecord> olaylar,
        SensorSimulatoru simulator,
        Action<string>? durumBildir = null,
        CancellationToken cancellationToken = default)
    {
        if (olaylar.Count == 0)
        {
            throw new InvalidOperationException(
                "Log dosyasında çalıştırılacak olay bulunamadı.");
        }

        simulator.LogEklemeModunuBaslat();

        try
        {
            ulong oncekiOlayZamani =
                olaylar[0].Header.TimestampEpochMillis;

            for (int i = 0; i < olaylar.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                IcdLogRecord olay = olaylar[i];

                if (i > 0)
                {
                    ulong olayZamani =
                        olay.Header.TimestampEpochMillis;

                    ulong beklemeSuresi =
                        olayZamani >= oncekiOlayZamani
                            ? olayZamani - oncekiOlayZamani
                            : 0;

                    if (beklemeSuresi > 0)
                    {
                        await Task.Delay(
                            TimeSpan.FromMilliseconds(beklemeSuresi),
                            cancellationToken);
                    }

                    oncekiOlayZamani = olayZamani;
                }

                OlayiUygula(olay, simulator);

                durumBildir?.Invoke(
                    $"[LOG EKLEME] {i + 1}/{olaylar.Count} " +
                    $"{olay.MessageType}");
            }
        }
        finally
        {
            simulator.LogEklemeModunuBitir();
        }
    }

    /// <summary>
    /// Tek bir log olayını uygun simülatör metoduna gönderir.
    /// </summary>
    private static void OlayiUygula(
        IcdLogRecord olay,
        SensorSimulatoru simulator)
    {
        string mesajTipi =
            olay.MessageType.Trim().ToUpperInvariant();

        switch (mesajTipi)
        {
            case "TRACK_CREATED":
                {
                    TrackData veri = TrackVerisiniOlustur(olay.Payload);

                    // otomatikPeriyotBaslat: false (varsayılan) —
                    // bu iz log replay'e ait; kendi 2 sn'lik otomatik
                    // döngüsüne girmez. Sıradaki TRACK_UPDATED, log
                    // dosyasındaki bir sonraki satır geldiğinde
                    // OlaylariCalistirAsync tarafından tetiklenir.
                    simulator.LogdanIzOlustur(veri);
                    break;
                }

            case "TRACK_UPDATED":
                {
                    TrackData veri = TrackVerisiniOlustur(olay.Payload);

                    // otomatikPeriyotBaslat: false (varsayılan) — aynı
                    // sebeple bu güncelleme de otomatik periyot başlatmaz.
                    simulator.LogdanIzGuncelle(veri);
                    break;
                }

            case "TESHIS_UPDATED":
                {
                    ushort trackId =
                        TrackIdOku(olay.Payload);

                    string yeniTeshisMetni =
                        MetinOku(olay.Payload, "yeniTeshis");

                    Teshis yeniTeshis =
                        EnumDegeriniOku<Teshis>(yeniTeshisMetni);

                    simulator.TeshisDegistir(trackId, yeniTeshis);
                    break;
                }

            case "TASNIF_UPDATED":
                {
                    ushort trackId =
                        TrackIdOku(olay.Payload);

                    string yeniTasnifMetni =
                        MetinOku(olay.Payload, "yeniTasnif");

                    Tasnif yeniTasnif =
                        EnumDegeriniOku<Tasnif>(yeniTasnifMetni);

                    simulator.TasnifDegistir(trackId, yeniTasnif);
                    break;
                }

            case "TRACK_DROPPED":
                {
                    ushort trackId =
                        TrackIdOku(olay.Payload);

                    string nedenMetni =
                        MetinOku(olay.Payload, "neden");

                    DropReason neden =
                        EnumDegeriniOku<DropReason>(nedenMetni);

                    simulator.IziDusur(trackId, neden);
                    break;
                }

            case "HEARTBEAT":
                {
                    simulator.HeartbeatGonder();
                    break;
                }

            default:
                throw new InvalidDataException(
                    $"Desteklenmeyen mesaj tipi: {olay.MessageType}");
        }
    }

    /// <summary>
    /// TRACK_CREATED ve TRACK_UPDATED payload'unu TrackData'ya çevirir.
    /// </summary>
    private static TrackData TrackVerisiniOlustur(
        JsonElement payload)
    {
        ushort trackId = TrackIdOku(payload);

        ushort hiz = checked(
            (ushort)SayiOku(payload, "hiz"));

        ushort yukseklik = checked(
            (ushort)SayiOku(payload, "yukseklik"));

        string yonelimMetni =
            MetinOku(payload, "yonelim");

        string teshisMetni =
            MetinOku(payload, "teshis");

        string tasnifMetni =
            MetinOku(payload, "tasnif");

        double enlem =
            OndalikliSayiOku(payload, "enlem");

        double boylam =
            OndalikliSayiOku(payload, "boylam");

        Yonelim yonelim =
            EnumDegeriniOku<Yonelim>(yonelimMetni);

        Teshis teshis =
            EnumDegeriniOku<Teshis>(teshisMetni);

        Tasnif tasnif =
            EnumDegeriniOku<Tasnif>(tasnifMetni);

        return new TrackData(
            trackId,
            hiz,
            yukseklik,
            yonelim,
            teshis,
            tasnif,
            enlem,
            boylam,
            EpochTime.NowMillis());
    }

    private static ushort TrackIdOku(JsonElement payload)
    {
        return checked(
            (ushort)SayiOku(payload, "trackId"));
    }

    private static int SayiOku(
        JsonElement payload,
        string alanAdi)
    {
        if (!payload.TryGetProperty(
                alanAdi,
                out JsonElement alan))
        {
            throw new InvalidDataException(
                $"Payload içinde '{alanAdi}' bulunamadı.");
        }

        return alan.GetInt32();
    }

    private static double OndalikliSayiOku(
        JsonElement payload,
        string alanAdi)
    {
        if (!payload.TryGetProperty(
                alanAdi,
                out JsonElement alan))
        {
            throw new InvalidDataException(
                $"Payload içinde '{alanAdi}' bulunamadı.");
        }

        return alan.GetDouble();
    }

    private static string MetinOku(
        JsonElement payload,
        string alanAdi)
    {
        if (!payload.TryGetProperty(
                alanAdi,
                out JsonElement alan))
        {
            throw new InvalidDataException(
                $"Payload içinde '{alanAdi}' bulunamadı.");
        }

        return alan.GetString()
            ?? throw new InvalidDataException(
                $"'{alanAdi}' değeri boş olamaz.");
    }

    /// <summary>
    /// Örneğin KAPSAMA_ALANI_DISI değerini
    /// KapsamaAlaniDisi enum değeriyle eşleştirir.
    /// </summary>
    private static T EnumDegeriniOku<T>(
        string logDegeri)
        where T : struct, Enum
    {
        string arananDeger = EnumMetniniNormallestir(logDegeri);

        foreach (T enumDegeri in Enum.GetValues<T>())
        {
            string mevcutDeger =
                EnumMetniniNormallestir(enumDegeri.ToString());

            if (mevcutDeger == arananDeger)
            {
                return enumDegeri;
            }
        }

        throw new InvalidDataException(
            $"'{logDegeri}' değeri " +
            $"{typeof(T).Name} enum'una çevrilemedi.");
    }

    private static string EnumMetniniNormallestir(
        string deger)
    {
        return deger
            .Replace("_", "")
            .Replace("-", "")
            .Replace(" ", "")
            .ToUpperInvariant();
    }

    private static void LogKaydiniDogrula(
        IcdLogRecord olay,
        int satirNumarasi)
    {
        if (string.IsNullOrWhiteSpace(olay.MessageType))
        {
            throw new JsonException(
                $"{satirNumarasi}. satırda messageType bulunamadı.");
        }

        if (olay.Header.TimestampEpochMillis == 0)
        {
            throw new JsonException(
                $"{satirNumarasi}. satırda " +
                "timestampEpochMillis bulunamadı.");
        }

        if (olay.Payload.ValueKind is
            JsonValueKind.Undefined or JsonValueKind.Null)
        {
            throw new JsonException(
                $"{satirNumarasi}. satırda payload bulunamadı.");
        }
    }
}