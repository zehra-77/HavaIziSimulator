using IcdLib;
using IcdLib.Enums;
using IcdLib.Models;

namespace HavaIziSimulator;

/// <summary>
/// Gönderilen mesajın türünü, ham baytlarını ve özetini UI'a taşır.
/// </summary>
public sealed class MesajGonderildiEventArgs : EventArgs
{
    public required MessageType MesajTipi { get; init; }
    public required byte[] Bayt { get; init; }
    public required string Ozet { get; init; }
    public required DateTime ZamanUtc { get; init; }
}

/// <summary>
/// Hava izlerini üretir, yaşam döngülerini yönetir ve mesajları
/// HavaIzi.IcdLib NuGet paketiyle kodlayarak UDP üzerinden gönderir.
/// </summary>
public sealed class SensorSimulatoru
{
    private readonly UdpYayinci _yayinci;
    private readonly Action<string> _log;
    private readonly Random _rnd = new();
    private readonly Dictionary<ushort, AktifIz> _aktifIzler = new();

    private uint _sequenceNumber;
    private DateTime _sonrakiHeartbeat;

    //log eklemek için
    public bool LogEklemeModuAktif { get; private set; }

    public event EventHandler<MesajGonderildiEventArgs>? MesajGonderildi;
    public event EventHandler? IzlerDegisti;

    public SensorSimulatoru(UdpYayinci yayinci, Action<string>? log = null)
    {
        _yayinci = yayinci;
        _log = log ?? Console.WriteLine;
        _sonrakiHeartbeat = DateTime.UtcNow;
    }

    public IReadOnlyCollection<ushort> AktifTrackIdleri => _aktifIzler.Keys.ToList();

    public TrackData? IzVerisiAl(ushort trackId) =>
        _aktifIzler.TryGetValue(trackId, out var iz) ? iz.Data : null;

    public IReadOnlyList<TrackData> TumAktifVeriler() =>
        _aktifIzler.Values.Select(iz => iz.Data).ToList();

    public bool IzAktifMi(ushort trackId) => _aktifIzler.ContainsKey(trackId);


    /// <summary>
    /// Log dosyasındaki olayların sisteme eklenmesini başlatır.
    /// Önceki replay izlerini temizler; diğer kaynaklardaki aktif izleri korur.
    /// </summary>
    public void LogEklemeModunuBaslat()
    {
        LogEklemeModuAktif = true;

        // Önceki replay'e ait izleri temizle; manuel, otomatik ve AI/MCP
        // izleri genel Aktif İzler tablosunda yaşamaya devam eder.
        foreach (ushort trackId in _aktifIzler
                     .Where(x => x.Value.LogReplayIzi)
                     .Select(x => x.Key)
                     .ToList())
            _aktifIzler.Remove(trackId);

        IzDegisimBildir();

        _log("[LOG EKLEME] Log ekleme modu başlatıldı.");
    }

    /// <summary>
    /// Log ekleme modunu kapatır.
    /// </summary>
    public void LogEklemeModunuBitir()
    {
        LogEklemeModuAktif = false;

        _log("[LOG EKLEME] Log ekleme modu tamamlandı.");
    }

    public ushort YeniIzOlustur(ushort? istenenTrackId = null)
    {
        ushort trackId = istenenTrackId ?? RastgeleBenzersizTrackId();

        if (trackId < IcdConstants.MinTrackId || trackId > IcdConstants.MaxTrackId)
        {
            throw new ArgumentOutOfRangeException(
                nameof(istenenTrackId),
                $"Track ID {IcdConstants.MinTrackId}-{IcdConstants.MaxTrackId} aralığında olmalıdır.");
        }

        if (_aktifIzler.ContainsKey(trackId))
        {
            throw new InvalidOperationException($"Track ID {trackId} zaten aktif bir izde kullanılıyor.");
        }

        var veri = new TrackData(
            trackId,
            (ushort)_rnd.Next(IcdConstants.MinHiz, IcdConstants.MaxHiz + 1),
            (ushort)_rnd.Next(IcdConstants.MinYukseklik, IcdConstants.MaxYukseklik + 1),
            RastgeleYonelim(),
            RastgeleTeshis(),
            RastgeleTasnif(),
            RastgeleEnlem(),
            RastgeleBoylam(),
            EpochTime.NowMillis());

        _aktifIzler[trackId] = new AktifIz
        {
            Data = veri,
            LogReplayIzi = false,
            SonrakiGuncellemeZamani = DateTime.UtcNow + IcdConstants.TrackUpdatePeriod,
        };

        ulong simdi = EpochTime.NowMillis();
        byte[] mesaj = IcdEncoder.EncodeTrackCreated(SonrakiSequence(), simdi, veri);

        Yayinla(
            MessageType.TrackCreated,
            mesaj,
            $"[TRACK_CREATED] ID={trackId} Hiz={veri.Hiz}kt " +
            $"Yukseklik={veri.Yukseklik}m Yonelim={veri.Yonelim} " +
            $"Enlem={veri.Enlem:F6} Boylam={veri.Boylam:F6}");

        IzDegisimBildir();
        return trackId;
    }

    /// <summary>
    /// Log dosyasındaki veya LLM'in ürettiği değerlerle yeni bir iz oluşturur.
    /// Rastgele değer üretmez.
    /// </summary>
    /// <param name="logVerisi">Kullanılacak sabit track verisi.</param>
    /// <param name="otomatikPeriyotBaslat">
    /// true ise (LLM senaryosu): bu iz, oluşturulduktan sonra normal
    /// simülasyon izleri gibi kendi 2 saniyelik otomatik TRACK_UPDATED
    /// döngüsüne girer (Tick() tarafından yönetilir).
    ///
    /// false ise (log replay): bu izin otomatik periyodu BAŞLATILMAZ
    /// (SonrakiGuncellemeZamani = DateTime.MaxValue). Çünkü log replay
    /// sırasında TRACK_UPDATED mesajları zaten LogEklemeService tarafından,
    /// log dosyasındaki gerçek zaman damgalarına göre ayrı ayrı
    /// LogdanIzGuncelle çağrılarıyla tetiklenir. Simülatörün kendi 2 sn'lik
    /// timer'ı da devreye girerse, log'da hiç olmayan fazladan
    /// TRACK_UPDATED mesajları araya sıkışır ve replay artık log'un
    /// birebir tekrarı olmaktan çıkar.
    /// </param>
    public void LogdanIzOlustur(TrackData logVerisi, bool otomatikPeriyotBaslat = false)
        => HariciIzOlustur(logVerisi, otomatikPeriyotBaslat, "LOG EKLEME", true);

    /// <summary>LLM/MCP senaryosunun ürettiği sabit değerlerle iz oluşturur.</summary>
    public void SenaryodanIzOlustur(TrackData veri, bool otomatikPeriyotBaslat = true)
        => HariciIzOlustur(veri, otomatikPeriyotBaslat, "LLM/MCP", false);

    private void HariciIzOlustur(
        TrackData logVerisi,
        bool otomatikPeriyotBaslat,
        string kaynak,
        bool logReplayIzi)
    {
        ushort trackId = logVerisi.TrackId;

        if (trackId < IcdConstants.MinTrackId ||
            trackId > IcdConstants.MaxTrackId)
        {
            throw new ArgumentOutOfRangeException(
                nameof(logVerisi),
                $"Track ID {IcdConstants.MinTrackId}-" +
                $"{IcdConstants.MaxTrackId} aralığında olmalıdır.");
        }

        if (_aktifIzler.ContainsKey(trackId))
        {
            throw new InvalidOperationException(
                $"Track ID {trackId} zaten aktif.");
        }

        ulong simdi = EpochTime.NowMillis();

        // Log dosyasındaki tarih eski olabilir.
        // UDP ile tekrar gönderirken güncel zamanı kullanıyoruz.
        TrackData gonderilecekVeri = logVerisi with
        {
            IzZamaniEpochMillis = simdi
        };

        _aktifIzler[trackId] = new AktifIz
        {
            Data = gonderilecekVeri,
            LogReplayIzi = logReplayIzi,

            SonrakiGuncellemeZamani = otomatikPeriyotBaslat
                ? DateTime.UtcNow + IcdConstants.TrackUpdatePeriod
                : DateTime.MaxValue,

            ManuelGuncellemeBekliyor = otomatikPeriyotBaslat
        };

        byte[] mesaj = IcdEncoder.EncodeTrackCreated(
            SonrakiSequence(),
            simdi,
            gonderilecekVeri);

        Yayinla(
            MessageType.TrackCreated,
            mesaj,
            $"[{kaynak}][TRACK_CREATED] " +
            $"ID={trackId} " +
            $"Hiz={gonderilecekVeri.Hiz}kt " +
            $"Yukseklik={gonderilecekVeri.Yukseklik}m " +
            $"Yonelim={gonderilecekVeri.Yonelim} " +
            $"Enlem={gonderilecekVeri.Enlem:F6} " +
            $"Boylam={gonderilecekVeri.Boylam:F6}");

        IzDegisimBildir();
    }

    /// <summary>
    /// Aktif bir izi log dosyasındaki veya LLM'in ürettiği değerlerle günceller.
    /// Rastgele değer üretmez.
    /// </summary>
    /// <param name="logVerisi">Kullanılacak sabit track verisi.</param>
    /// <param name="otomatikPeriyotBaslat">
    /// true ise (LLM senaryosu): bu güncellemeden sonra iz normal
    /// simülasyon izleri gibi kendi 2 saniyelik otomatik TRACK_UPDATED
    /// döngüsüne devam eder/girer.
    ///
    /// false ise (log replay): otomatik periyot tekrar BAŞLATILMAZ; bir
    /// sonraki TRACK_UPDATED yine LogEklemeService tarafından, log
    /// dosyasındaki bir sonraki olayın zaman damgasına göre tetiklenir.
    /// </param>
    public void LogdanIzGuncelle(TrackData logVerisi, bool otomatikPeriyotBaslat = false)
        => HariciIzGuncelle(logVerisi, otomatikPeriyotBaslat, "LOG EKLEME");

    /// <summary>LLM/MCP senaryosunun ürettiği sabit değerlerle izi günceller.</summary>
    public void SenaryodanIzGuncelle(TrackData veri, bool otomatikPeriyotBaslat = true)
        => HariciIzGuncelle(veri, otomatikPeriyotBaslat, "LLM/MCP");

    private void HariciIzGuncelle(
        TrackData logVerisi,
        bool otomatikPeriyotBaslat,
        string kaynak)
    {
        ushort trackId = logVerisi.TrackId;

        if (!_aktifIzler.TryGetValue(trackId, out var aktifIz))
        {
            throw new InvalidOperationException(
                $"Track ID {trackId} bulunamadığı için güncellenemedi.");
        }

        ulong simdi = EpochTime.NowMillis();

        TrackData gonderilecekVeri = logVerisi with
        {
            IzZamaniEpochMillis = simdi
        };

        aktifIz.Data = gonderilecekVeri;

        aktifIz.SonrakiGuncellemeZamani = otomatikPeriyotBaslat
            ? DateTime.UtcNow + IcdConstants.TrackUpdatePeriod
            : DateTime.MaxValue;

        aktifIz.ManuelGuncellemeBekliyor = otomatikPeriyotBaslat;

        byte[] mesaj = IcdEncoder.EncodeTrackUpdated(
            SonrakiSequence(),
            simdi,
            gonderilecekVeri);

        Yayinla(
            MessageType.TrackUpdated,
            mesaj,
            $"[{kaynak}][TRACK_UPDATED] " +
            $"ID={trackId} " +
            $"Hiz={gonderilecekVeri.Hiz}kt " +
            $"Yukseklik={gonderilecekVeri.Yukseklik}m " +
            $"Yonelim={gonderilecekVeri.Yonelim} " +
            $"Enlem={gonderilecekVeri.Enlem:F6} " +
            $"Boylam={gonderilecekVeri.Boylam:F6}");

        IzDegisimBildir();
    }
    public void PeriyodikGuncelleGonder(ushort trackId)
    {
        if (!_aktifIzler.TryGetValue(trackId, out var iz))
        {
            return;
        }

        ulong simdi = EpochTime.NowMillis();
        TrackData yeni;

        if (iz.ManuelGuncellemeBekliyor)
        {
            /*
             * Kullanıcının elle girdiği değerler ilk periyodik
             * TRACK_UPDATED mesajında aynen gönderilir.
             *
             * Burada hız, yükseklik, enlem ve boylama
             * rastgele yürüyüş uygulanmaz.
             */
            yeni = iz.Data with
            {
                IzZamaniEpochMillis = simdi
            };

            iz.ManuelGuncellemeBekliyor = false;
        }
        else
        {
            /*
             * Normal simülasyon akışı:
             * Track değerleri ICD sınırları içinde rastgele değiştirilir.
             */
            TrackData eski = iz.Data;

            yeni = eski with
            {
                Hiz = SinirliRastgeleYuru(
                    eski.Hiz,
                    30,
                    IcdConstants.MinHiz,
                    IcdConstants.MaxHiz),

                Yukseklik = SinirliRastgeleYuru(
                    eski.Yukseklik,
                    50,
                    IcdConstants.MinYukseklik,
                    IcdConstants.MaxYukseklik),

                Enlem = SinirliRastgeleYuruDouble(
                    eski.Enlem,
                    0.01,
                    IcdConstants.MinEnlem,
                    IcdConstants.MaxEnlem),

                Boylam = SinirliRastgeleYuruDouble(
                    eski.Boylam,
                    0.01,
                    IcdConstants.MinBoylam,
                    IcdConstants.MaxBoylam),

                IzZamaniEpochMillis = simdi
            };
        }

        iz.Data = yeni;

        /*
         * TRACK_UPDATED periyodu IcdLib NuGet paketinden alınır.
         * Her track için sonraki yayın zamanı ayrı tutulur.
         */
        iz.SonrakiGuncellemeZamani =
            DateTime.UtcNow + IcdConstants.TrackUpdatePeriod;

        byte[] mesaj = IcdEncoder.EncodeTrackUpdated(
            SonrakiSequence(),
            simdi,
            yeni);

        Yayinla(
            MessageType.TrackUpdated,
            mesaj,
            $"[TRACK_UPDATED] ID={trackId} " +
            $"Hiz={yeni.Hiz}kt " +
            $"Yukseklik={yeni.Yukseklik}m " +
            $"Yonelim={yeni.Yonelim} " +
            $"Enlem={yeni.Enlem:F6} " +
            $"Boylam={yeni.Boylam:F6} " +
            $"Teshis={yeni.Teshis} " +
            $"Tasnif={yeni.Tasnif}");

        IzDegisimBildir();
    }

    public void IziGuncelle(
     ushort trackId,
     ushort yeniHiz,
     ushort yeniYukseklik,
     Yonelim yeniYonelim,
     double yeniEnlem,
     double yeniBoylam)
    {
        if (!_aktifIzler.TryGetValue(trackId, out var iz))
        {
            throw new InvalidOperationException(
                $"Track ID {trackId} aktif izler arasında bulunamadı.");
        }

        /*
         * Bütün sınırlar IcdLib NuGet paketinden alınır.
         */

        if (yeniHiz < IcdConstants.MinHiz ||
            yeniHiz > IcdConstants.MaxHiz)
        {
            throw new ArgumentOutOfRangeException(
                nameof(yeniHiz),
                $"Hız {IcdConstants.MinHiz}-" +
                $"{IcdConstants.MaxHiz} kt aralığında olmalıdır.");
        }

        if (yeniYukseklik < IcdConstants.MinYukseklik ||
            yeniYukseklik > IcdConstants.MaxYukseklik)
        {
            throw new ArgumentOutOfRangeException(
                nameof(yeniYukseklik),
                $"Yükseklik {IcdConstants.MinYukseklik}-" +
                $"{IcdConstants.MaxYukseklik} m aralığında olmalıdır.");
        }

        if (yeniEnlem < IcdConstants.MinEnlem ||
            yeniEnlem > IcdConstants.MaxEnlem)
        {
            throw new ArgumentOutOfRangeException(
                nameof(yeniEnlem),
                $"Enlem {IcdConstants.MinEnlem}-" +
                $"{IcdConstants.MaxEnlem} aralığında olmalıdır.");
        }

        if (yeniBoylam < IcdConstants.MinBoylam ||
            yeniBoylam > IcdConstants.MaxBoylam)
        {
            throw new ArgumentOutOfRangeException(
                nameof(yeniBoylam),
                $"Boylam {IcdConstants.MinBoylam}-" +
                $"{IcdConstants.MaxBoylam} aralığında olmalıdır.");
        }

        /*
         * Bellekteki TrackData hemen güncellenir.
         *
         * IzZamaniEpochMillis burada değiştirilmez.
         * Gerçek snapshot zamanı periyodik TRACK_UPDATED
         * oluşturulurken atanır.
         */
        iz.Data = iz.Data with
        {
            Hiz = yeniHiz,
            Yukseklik = yeniYukseklik,
            Yonelim = yeniYonelim,
            Enlem = yeniEnlem,
            Boylam = yeniBoylam
        };

        /*
         * Bir sonraki periyodik yayında kullanıcının verdiği
         * değerlerin aynen gönderilmesini sağlar.
         */
        iz.ManuelGuncellemeBekliyor = true;

        /*
         * SonrakiGuncellemeZamani değiştirilmez.
         * Böylece mevcut sabit 2 saniyelik periyodik takvim korunur.
         *
         * Burada EncodeTrackUpdated çağrılmaz.
         * Burada Yayinla çağrılmaz.
         */

        IzDegisimBildir();
    }

    public void TeshisDegistir(ushort trackId, Teshis yeniTeshis)
    {
        if (!_aktifIzler.TryGetValue(trackId, out var iz))
        {
            return;
        }

        iz.Data = iz.Data with { Teshis = yeniTeshis };
        ulong simdi = EpochTime.NowMillis();

        byte[] mesaj = IcdEncoder.EncodeTeshisUpdated(
            SonrakiSequence(),
            simdi,
            new TeshisUpdatedData(trackId, yeniTeshis, simdi));

        Yayinla(
            MessageType.TeshisUpdated,
            mesaj,
            $"[TESHIS_UPDATED] ID={trackId} YeniTeshis={yeniTeshis}");

        IzDegisimBildir();
    }

    public void TasnifDegistir(ushort trackId, Tasnif yeniTasnif)
    {
        if (!_aktifIzler.TryGetValue(trackId, out var iz))
        {
            return;
        }

        iz.Data = iz.Data with { Tasnif = yeniTasnif };
        ulong simdi = EpochTime.NowMillis();

        byte[] mesaj = IcdEncoder.EncodeTasnifUpdated(
            SonrakiSequence(),
            simdi,
            new TasnifUpdatedData(trackId, yeniTasnif, simdi));

        Yayinla(
            MessageType.TasnifUpdated,
            mesaj,
            $"[TASNIF_UPDATED] ID={trackId} YeniTasnif={yeniTasnif}");

        IzDegisimBildir();
    }

    public void IziDusur(ushort trackId, DropReason neden)
    {
        if (!_aktifIzler.ContainsKey(trackId))
        {
            return;
        }

        ulong simdi = EpochTime.NowMillis();
        byte[] mesaj = IcdEncoder.EncodeTrackDropped(
            SonrakiSequence(),
            simdi,
            new TrackDroppedData(trackId, simdi, neden));

        Yayinla(
            MessageType.TrackDropped,
            mesaj,
            $"[TRACK_DROPPED] ID={trackId} Neden={neden}");

        _aktifIzler.Remove(trackId);
        IzDegisimBildir();
    }

    public void HeartbeatGonder()
    {
        ulong simdi = EpochTime.NowMillis();
        byte[] mesaj = IcdEncoder.EncodeHeartbeat(SonrakiSequence(), simdi);
        Yayinla(MessageType.Heartbeat, mesaj, "[HEARTBEAT]");
    }

    /// <summary>
    /// Şu an aktif olan tüm izlerin anlık görüntüsünü döndürür.
    /// LLM'e "mevcut durum nedir" bağlamını vermek için kullanılır —
    /// dışarıdan _aktifIzler'e doğrudan erişim yoktur, bu yüzden salt-okunur
    /// bir kopya döndürülür (çağıran taraf _aktifIzler'i değiştiremez).
    /// </summary>
    public IReadOnlyList<TrackData> AktifIzleriGetir()
    {
        return _aktifIzler.Values.Select(x => x.Data).ToList();
    }

    /// <summary>Yalnızca o anda aktif olan JSONL replay izlerini döndürür.</summary>
    public IReadOnlyList<TrackData> LogReplayAktifIzleriGetir()
    {
        return _aktifIzler.Values
            .Where(x => x.LogReplayIzi)
            .Select(x => x.Data)
            .ToList();
    }

    public void Tick()
    {
        DateTime simdi = DateTime.UtcNow;

        foreach (ushort trackId in _aktifIzler
                     .Where(x => !x.Value.LogReplayIzi)
                     .Select(x => x.Key)
                     .ToList())
        {
            if (simdi >= _aktifIzler[trackId].SonrakiGuncellemeZamani)
            {
                PeriyodikGuncelleGonder(trackId);
            }
        }

        if (simdi >= _sonrakiHeartbeat)
        {
            HeartbeatGonder();
            _sonrakiHeartbeat = simdi + IcdConstants.HeartbeatPeriod;
        }
    }

    private void Yayinla(MessageType tip, byte[] mesaj, string ozet)
    {
        _yayinci.Gonder(mesaj);
        _log(ozet);

        MesajGonderildi?.Invoke(this, new MesajGonderildiEventArgs
        {
            MesajTipi = tip,
            Bayt = mesaj,
            Ozet = ozet,
            ZamanUtc = DateTime.UtcNow,
        });
    }

    private void IzDegisimBildir() => IzlerDegisti?.Invoke(this, EventArgs.Empty);

    private uint SonrakiSequence()
    {
        uint sonuc = _sequenceNumber;
        _sequenceNumber = unchecked(_sequenceNumber + 1);
        return sonuc;
    }

    private ushort RastgeleBenzersizTrackId()
    {
        for (int deneme = 0; deneme < 20_000; deneme++)
        {
            ushort id = (ushort)_rnd.Next(IcdConstants.MinTrackId, IcdConstants.MaxTrackId + 1);
            if (!_aktifIzler.ContainsKey(id))
            {
                return id;
            }
        }

        throw new InvalidOperationException("Kullanılabilir benzersiz Track ID bulunamadı.");
    }

    private Yonelim RastgeleYonelim()
    {
        Yonelim[] degerler = Enum.GetValues<Yonelim>();
        return degerler[_rnd.Next(degerler.Length)];
    }

    private Teshis RastgeleTeshis()
    {
        Teshis[] degerler = Enum.GetValues<Teshis>();

        return degerler[
            _rnd.Next(degerler.Length)];
    }

    private Tasnif RastgeleTasnif()
    {
        Tasnif[] degerler = Enum.GetValues<Tasnif>();

        return degerler[
            _rnd.Next(degerler.Length)];
    }

    private double RastgeleEnlem() =>
        IcdConstants.MinEnlem +
        _rnd.NextDouble() * (IcdConstants.MaxEnlem - IcdConstants.MinEnlem);

    private double RastgeleBoylam() =>
        IcdConstants.MinBoylam +
        _rnd.NextDouble() * (IcdConstants.MaxBoylam - IcdConstants.MinBoylam);

    private ushort SinirliRastgeleYuru(ushort mevcut, int maxAdim, int min, int max)
    {
        int yeni = mevcut + _rnd.Next(-maxAdim, maxAdim + 1);
        return (ushort)Math.Clamp(yeni, min, max);
    }

    private double SinirliRastgeleYuruDouble(
        double mevcut,
        double maxAdim,
        double min,
        double max)
    {
        double yeni = mevcut + ((_rnd.NextDouble() * 2 - 1) * maxAdim);
        return Math.Clamp(yeni, min, max);
    }
}
