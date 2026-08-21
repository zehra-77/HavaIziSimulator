using HavaIziSimulator;
using HavaIziSimulator.Ai.Models;
using HavaIziSimulator.Ai.Chat;
using HavaIziSimulator.Llm;
using HavaIziSimulator.LogEkleme;
using HavaIziSimulator.LogAnalysis;
using HavaIziSimulator.Mcp.Time.Models;
using HavaIziSimulator.Mcp.Time.Scheduling;
using IcdLib;
using IcdLib.Enums;
using IcdLib.Models;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Windows.Threading;

namespace HavaIziSimulator.Wpf;

/// <summary>
/// WPF önyüzünün ana ViewModel'i. Bağlantı ayarlarını, aktif iz listesini,
/// gönderim log'unu ve tüm komutları yönetir.
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private UdpYayinci? _yayinci;
    private SensorSimulatoru? _simulator;
    //log jsonlarını okuma
    private readonly LogEklemeService _logEklemeService = new();
    private CancellationTokenSource? _logEklemeIptalKaynagi;

    private readonly DispatcherTimer _tickTimer;
    private readonly DispatcherTimer _otomatikModTimer;
    private readonly Random _rnd = new();

    private readonly Dictionary<ushort, TrackRowViewModel> _satirlar = new();
    private readonly Dictionary<ushort, TrackRowViewModel> _logReplaySatirlar = new();

    private readonly AiAssistantService _llmClient = new();
    private CancellationTokenSource? _senaryoIptalKaynagi;
    private bool _senaryoCalisiyor;

    private readonly RadarScenarioValidator _scenarioValidator = new();
    private readonly ScheduledActionRunner _scheduledActionRunner = new();
    private readonly LogAnalysisContextBuilder _logAnalysisContextBuilder = new();

    private string _aiLogDosyasiYolu = string.Empty;
    private bool _aiLogModuAktif;

    public bool AiLogModuAktif
    {
        get => _aiLogModuAktif;
        private set
        {
            if (!Set(ref _aiLogModuAktif, value)) return;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AiAktifIzModu)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AiBaglamBasligi)));
            KomutlariYenile();
        }
    }

    public bool AiAktifIzModu => !AiLogModuAktif;

    public string AiSeciliLogDosyasiMetni =>
        string.IsNullOrWhiteSpace(_aiLogDosyasiYolu)
            ? "Log dosyası seçilmedi"
            : Path.GetFileName(_aiLogDosyasiYolu);

    public string AiBaglamBasligi => AiLogModuAktif
        ? $"Log analizi: {AiSeciliLogDosyasiMetni}"
        : "Aktif iz modu: create / update / drop komutu verebilir; şehir, bölge, merkez noktası veya yarıçap içindeki izler hakkında soru sorabilirsiniz.";

    private string _senaryoPrompt = string.Empty;

    public string SenaryoPrompt
    {
        get => _senaryoPrompt;
        set
        {
            if (Set(ref _senaryoPrompt, value))
            {
                SenaryoOlusturVeGonderKomutu.RaiseCanExecuteChanged();
            }
        }
    }

    private string _senaryoDurumu = string.Empty;

    public string SenaryoDurumu
    {
        get => _senaryoDurumu;
        set => Set(ref _senaryoDurumu, value);
    }

    public bool SenaryoCalisiyor
    {
        get => _senaryoCalisiyor;
        private set
        {
            if (Set(ref _senaryoCalisiyor, value))
            {
                KomutlariYenile();
            }
        }
    }
    public ObservableCollection<TrackRowViewModel> AktifIzler { get; } = new();
    public ObservableCollection<TrackRowViewModel> LogReplayIzler { get; } = new();
    public ObservableCollection<string> Loglar { get; } = new();

    // ------------------------------------------------------------
    // Bağlantı ayarları
    // ------------------------------------------------------------

    private string _hedefIp = "127.0.0.1";

    public string HedefIp
    {
        get => _hedefIp;
        set => Set(ref _hedefIp, value);
    }

    private int _port = 5000;

    public int Port
    {
        get => _port;
        set => Set(ref _port, value);
    }

    private bool _broadcastModu;

    public bool BroadcastModu
    {
        get => _broadcastModu;
        set => Set(ref _broadcastModu, value);
    }

    private bool _baglandi;

    public bool Baglandi
    {
        get => _baglandi;
        set
        {
            if (Set(ref _baglandi, value))
            {
                KomutlariYenile();
            }
        }
    }

    private string _baglantiDurumu = "Bağlı değil";

    public string BaglantiDurumu
    {
        get => _baglantiDurumu;
        set => Set(ref _baglantiDurumu, value);
    }

    // ------------------------------------------------------------
    // Yeni iz oluşturma alanları
    // ------------------------------------------------------------

    private string _yeniTrackIdMetni = "";

    public string YeniTrackIdMetni
    {
        get => _yeniTrackIdMetni;
        set => Set(ref _yeniTrackIdMetni, value);
    }

    // ------------------------------------------------------------
    // Seçili iz
    // ------------------------------------------------------------

    private TrackRowViewModel? _seciliIz;

    public TrackRowViewModel? SeciliIz
    {
        get => _seciliIz;
        set
        {
            if (ReferenceEquals(_seciliIz, value))
            {
                return;
            }

            if (Set(ref _seciliIz, value))
            {
                SeciliIzAlanlariniDoldur();
                KomutlariYenile();
            }
        }
    }

    // ------------------------------------------------------------
    // Enum seçenekleri
    // ------------------------------------------------------------

    public Teshis[] TeshisSecenekleri { get; } =
        Enum.GetValues<Teshis>();

    public Tasnif[] TasnifSecenekleri { get; } =
        Enum.GetValues<Tasnif>();

    public DropReason[] DusmeNedeniSecenekleri { get; } =
        Enum.GetValues<DropReason>();

    public Yonelim[] YonelimSecenekleri { get; } =
        Enum.GetValues<Yonelim>();

    // ------------------------------------------------------------
    // Teşhis, tasnif ve düşürme alanları
    // ------------------------------------------------------------

    private Teshis _seciliYeniTeshis = Teshis.Dost;

    public Teshis SeciliYeniTeshis
    {
        get => _seciliYeniTeshis;
        set => Set(ref _seciliYeniTeshis, value);
    }

    private Tasnif _seciliYeniTasnif = Tasnif.Ucak;

    public Tasnif SeciliYeniTasnif
    {
        get => _seciliYeniTasnif;
        set => Set(ref _seciliYeniTasnif, value);
    }

    private DropReason _seciliDusmeNedeni = DropReason.SinyalKaybi;

    public DropReason SeciliDusmeNedeni
    {
        get => _seciliDusmeNedeni;
        set => Set(ref _seciliDusmeNedeni, value);
    }

    // ------------------------------------------------------------
    // TRACK_UPDATED form alanları
    // ------------------------------------------------------------

    private string _yeniHizMetni = "";

    public string YeniHizMetni
    {
        get => _yeniHizMetni;
        set => Set(ref _yeniHizMetni, value);
    }

    private string _yeniYukseklikMetni = "";

    public string YeniYukseklikMetni
    {
        get => _yeniYukseklikMetni;
        set => Set(ref _yeniYukseklikMetni, value);
    }

    private string _yeniEnlemMetni = "";

    public string YeniEnlemMetni
    {
        get => _yeniEnlemMetni;
        set => Set(ref _yeniEnlemMetni, value);
    }

    private string _yeniBoylamMetni = "";

    public string YeniBoylamMetni
    {
        get => _yeniBoylamMetni;
        set => Set(ref _yeniBoylamMetni, value);
    }

    private Yonelim _seciliYonelim;

    public Yonelim SeciliYonelim
    {
        get => _seciliYonelim;
        set => Set(ref _seciliYonelim, value);
    }

    // ------------------------------------------------------------
    // Otomatik mod ve sayaçlar
    // ------------------------------------------------------------

    private bool _otomatikModAcik;

    public bool OtomatikModAcik
    {
        get => _otomatikModAcik;
        set => Set(ref _otomatikModAcik, value);
    }

    private int _gonderilenMesajSayisi;

    public int GonderilenMesajSayisi
    {
        get => _gonderilenMesajSayisi;
        set => Set(ref _gonderilenMesajSayisi, value);
    }

    // ------------------------------------------------------------
    // Log dosyası ekleme
    // ------------------------------------------------------------

    private string _logDosyasiYolu = "";

    public string LogDosyasiYolu
    {
        get => _logDosyasiYolu;
        set
        {
            if (Set(ref _logDosyasiYolu, value))
            {
                KomutlariYenile();
            }
        }
    }

    private string _logEklemeDurumu = "Log dosyası seçilmedi.";

    public string LogEklemeDurumu
    {
        get => _logEklemeDurumu;
        set => Set(ref _logEklemeDurumu, value);
    }

    private bool _logEklemeCalisiyor;

    public bool LogEklemeCalisiyor
    {
        get => _logEklemeCalisiyor;
        set
        {
            if (Set(ref _logEklemeCalisiyor, value))
            {
                KomutlariYenile();
            }
        }
    }

    // ------------------------------------------------------------
    // Komutlar
    // ------------------------------------------------------------

    public RelayCommand BaglanKomutu { get; }
    public RelayCommand BaglantiyiKesKomutu { get; }
    public RelayCommand YeniIzOlusturKomutu { get; }
    public RelayCommand TeshisDegistirKomutu { get; }
    public RelayCommand TasnifDegistirKomutu { get; }
    public RelayCommand IziDusurKomutu { get; }
    public RelayCommand IziGuncelleKomutu { get; }
    public RelayCommand HeartbeatGonderKomutu { get; }
    public RelayCommand LoglariTemizleKomutu { get; }
    public RelayCommand LogDosyasiSecKomutu { get; }
    public RelayCommand LogEklemeBaslatKomutu { get; }
    public RelayCommand LogEklemeDurdurKomutu { get; }
    public RelayCommand SenaryoOlusturVeGonderKomutu { get; }
    public RelayCommand AiLogDosyasiSecKomutu { get; }
    public RelayCommand AiAktifIzlereDonKomutu { get; }

    public MainViewModel()
    {
        BaglanKomutu = new RelayCommand(
            Baglan,
            _ => !Baglandi);

        BaglantiyiKesKomutu = new RelayCommand(
            BaglantiyiKes,
            _ => Baglandi);

        YeniIzOlusturKomutu = new RelayCommand(
            YeniIzOlustur,
            _ => Baglandi && !LogEklemeCalisiyor);

        TeshisDegistirKomutu = new RelayCommand(
            TeshisDegistir,
            _ => Baglandi && SeciliIz is not null && !LogEklemeCalisiyor);

        TasnifDegistirKomutu = new RelayCommand(
            TasnifDegistir,
            _ => Baglandi && SeciliIz is not null && !LogEklemeCalisiyor);

        IziDusurKomutu = new RelayCommand(
            IziDusur,
            _ => Baglandi && SeciliIz is not null && !LogEklemeCalisiyor);

        IziGuncelleKomutu = new RelayCommand(
            IziGuncelle,
            _ => Baglandi && SeciliIz is not null && !LogEklemeCalisiyor);

        HeartbeatGonderKomutu = new RelayCommand(
            () => _simulator?.HeartbeatGonder(),
            () => Baglandi && !LogEklemeCalisiyor);

        LoglariTemizleKomutu = new RelayCommand(
            () => Loglar.Clear());

        LogDosyasiSecKomutu = new RelayCommand(
    LogDosyasiSec,
    _ => !LogEklemeCalisiyor);

        LogEklemeBaslatKomutu = new RelayCommand(
            LogEklemeBaslat,
            _ => Baglandi
                 && !LogEklemeCalisiyor
                 && !string.IsNullOrWhiteSpace(LogDosyasiYolu));

        LogEklemeDurdurKomutu = new RelayCommand(
            LogEklemeDurdur,
            _ => LogEklemeCalisiyor);

        AiLogDosyasiSecKomutu = new RelayCommand(
            AiLogDosyasiSec,
            _ => !SenaryoCalisiyor);

        AiAktifIzlereDonKomutu = new RelayCommand(
            AiAktifIzlereDon,
            _ => AiLogModuAktif && !SenaryoCalisiyor);

        //llm için ekledik bu methodu
        SenaryoOlusturVeGonderKomutu =
            new RelayCommand(
                async _ => await SenaryoOlusturVeGonder(),
                _ => !LogEklemeCalisiyor &&
                     !SenaryoCalisiyor &&
                     !string.IsNullOrWhiteSpace(SenaryoPrompt));



        /*
         * Bu timer TRACK_UPDATED periyodu değildir.
         * Yalnızca SensorSimulatoru.Tick() metodunu sık aralıkla kontrol eder.
         *
         * Gerçek TRACK_UPDATED periyodu IcdLib içindeki
         * IcdConstants.TrackUpdatePeriod değeridir.
         */
        _tickTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(150),
        };

        _tickTimer.Tick += (_, _) => _simulator?.Tick();

        /*
         * Bu timer sadece otomatik senaryo işlemleri içindir:
         * iz oluşturma, teşhis/tasnif değiştirme ve iz düşürme.
         */
        _otomatikModTimer =
            new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(1),
            };

        _otomatikModTimer.Tick += (_, _) => OtomatikModAdimi();
    }

    private async Task SenaryoOlusturVeGonder()
    {
        if (SenaryoCalisiyor)
        {
            return;
        }

        SensorSimulatoru? simulator = _simulator;

        _senaryoIptalKaynagi?.Dispose();
        _senaryoIptalKaynagi = new CancellationTokenSource();
        SenaryoCalisiyor = true;

        try
        {
            SenaryoDurumu = AiLogModuAktif
                ? "Groq seçili log dosyasını analiz ediyor..."
                : "Groq modeli radar MCP aracını seçiyor...";
            SenaryoJson = string.Empty;
            AiCevabi = string.Empty;
            LogEkle($"[LLM] Prompt {_llmClient.ProviderDescription} akışına gönderiliyor.");

            bool logModu = AiLogModuAktif;
            IReadOnlyList<TrackData> aktifIzler = logModu
                ? []
                : simulator?.AktifIzleriGetir() ?? [];
            string? logContext = logModu
                ? await _logAnalysisContextBuilder.BuildAsync(
                    _aiLogDosyasiYolu,
                    _senaryoIptalKaynagi.Token)
                : null;

            AiAssistantResponse assistantResponse = await _llmClient.ChatAsync(
                SenaryoPrompt,
                aktifIzler,
                logContext,
                _senaryoIptalKaynagi.Token,
                logModu || simulator is null
                    ? null
                    : new Func<IReadOnlyList<TrackData>>(simulator.AktifIzleriGetir),
                logOnlyMode: logModu);
            List<RadarScenarioDto> senaryolar = assistantResponse.RadarActions;
            AiCevabi = assistantResponse.Answer;
            foreach (AiToolCallInfo call in assistantResponse.ToolCalls)
                LogEkle($"[GROQ][TOOL] {call.Name} {call.ArgumentsJson}");

            // Ham cevap, hata olsa bile ekranda görünsün diye üretilir üretilmez yazılıyor.
            SenaryoJson = JsonSerializer.Serialize(
                senaryolar,
                new JsonSerializerOptions { WriteIndented = true });

            LogEkle($"[MCP] {senaryolar.Count} doğrulanabilir radar işlemi üretildi.");

            List<ScheduledActionPayloadDto> scheduledActions = senaryolar
                .Where(x => x.MessageType == "SCHEDULED_ACTION")
                .Select(x => x.Payload.Deserialize<ScheduledActionPayloadDto>()
                    ?? throw new ArgumentException("Zamanlanmış işlem payload'u boş."))
                .ToList();
            List<RadarScenarioDto> immediateActions = senaryolar
                .Where(x => x.MessageType != "SCHEDULED_ACTION")
                .ToList();

            if (immediateActions.Count > 0 && simulator is null)
                throw new InvalidOperationException("Radar işlemi için önce bağlantı kurulmalıdır.");

            IReadOnlyList<LlmSenaryoSonucu> sonuclar = simulator is null
                ? []
                : SenaryolariUygula(simulator, immediateActions);
            foreach (ScheduledActionPayloadDto action in scheduledActions)
            {
                _scheduledActionRunner.Schedule(action, ZamanlanmisIslemiCalistirAsync);
                LogEkle($"[TIME MCP][PLANLANDI] {action.DelaySeconds} sn sonra {action.ToolName}");
            }

            SenaryoDurumu = scheduledActions.Count > 0
                ? $"{scheduledActions.Count} işlem planlandı, {sonuclar.Count} mesaj gönderildi."
                : sonuclar.Count > 0
                    ? $"{sonuclar.Count} mesaj gönderildi."
                    : "Groq yanıtı alındı.";
        }
        catch (OperationCanceledException)
        {
            SenaryoDurumu = "Senaryo işlemi iptal edildi.";
            LogEkle("[LLM] Senaryo işlemi iptal edildi.");
        }
        catch (Exception ex)
        {
            SenaryoDurumu = $"Hata: {ex.Message}";
            LogEkle($"[LLM][HATA] {ex.Message}");
        }
        finally
        {
            _senaryoIptalKaynagi?.Dispose();
            _senaryoIptalKaynagi = null;
            SenaryoCalisiyor = false;
        }
    }

    private IReadOnlyList<LlmSenaryoSonucu> SenaryolariUygula(
        SensorSimulatoru simulator,
        IReadOnlyList<RadarScenarioDto> senaryolar)
    {
        IReadOnlyList<LlmSenaryoSonucu> sonuclar =
            _scenarioValidator.DogrulaVeDonusturListe(senaryolar);
        foreach (LlmSenaryoSonucu sonuc in sonuclar)
        {
            switch (sonuc.MessageType)
            {
                case MessageType.TrackCreated:
                    simulator.SenaryodanIzOlustur(sonuc.TrackData!, true);
                    break;
                case MessageType.TrackUpdated:
                    simulator.SenaryodanIzGuncelle(sonuc.TrackData!, true);
                    break;
                case MessageType.TrackDropped:
                    simulator.IziDusur(sonuc.TrackDroppedData!.TrackId, sonuc.TrackDroppedData.Neden);
                    break;
                case MessageType.TeshisUpdated:
                    simulator.TeshisDegistir(sonuc.TeshisUpdatedData!.TrackId, sonuc.TeshisUpdatedData.YeniTeshis);
                    break;
                case MessageType.TasnifUpdated:
                    simulator.TasnifDegistir(sonuc.TasnifUpdatedData!.TrackId, sonuc.TasnifUpdatedData.YeniTasnif);
                    break;
                case MessageType.Heartbeat:
                    simulator.HeartbeatGonder();
                    break;
            }
            LogEkle($"[LLM/MCP][GÖNDERİLDİ] {sonuc.MessageType}");
        }
        return sonuclar;
    }

    private async Task ZamanlanmisIslemiCalistirAsync(
        ScheduledActionPayloadDto action,
        CancellationToken cancellationToken)
    {
        Task uiTask = await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            if (_simulator is null)
            {
                LogEkle($"[TIME MCP][ATLANDI] Bağlantı kapalı: {action.ToolName}");
                return;
            }
            try
            {
                SensorSimulatoru simulator = _simulator;
                List<RadarScenarioDto> scenarios = await _llmClient.CallToolDirectAsync(
                    action.ToolName,
                    action.Arguments,
                    simulator.AktifIzleriGetir(),
                    cancellationToken);
                SenaryolariUygula(simulator, scenarios);
                LogEkle($"[TIME MCP][ÇALIŞTI] {action.ToolName}");
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                LogEkle($"[TIME MCP][HATA] {ex.Message}");
            }
        });
        await uiTask;
    }

    private string _senaryoJson = string.Empty;
    private string _aiCevabi = string.Empty;

    public string AiCevabi
    {
        get => _aiCevabi;
        set => Set(ref _aiCevabi, value);
    }

    /// <summary>
    /// MCP radar araçlarının ürettiği JSON'un okunabilir (indented) hâli.
    /// Ekrandaki salt-okunur JSON kutusuna bind edilir; hem başarı hem
    /// hata durumunda doldurulur ki kullanıcı modelin ne ürettiğini görebilsin.
    /// </summary>
    public string SenaryoJson
    {
        get => _senaryoJson;
        set => Set(ref _senaryoJson, value);
    }

    // ------------------------------------------------------------
    // Komut yenileme
    // ------------------------------------------------------------

    private void KomutlariYenile()
    {
        BaglanKomutu.RaiseCanExecuteChanged();
        BaglantiyiKesKomutu.RaiseCanExecuteChanged();
        YeniIzOlusturKomutu.RaiseCanExecuteChanged();
        TeshisDegistirKomutu.RaiseCanExecuteChanged();
        TasnifDegistirKomutu.RaiseCanExecuteChanged();
        IziDusurKomutu.RaiseCanExecuteChanged();
        IziGuncelleKomutu.RaiseCanExecuteChanged();
        HeartbeatGonderKomutu.RaiseCanExecuteChanged();

        LogDosyasiSecKomutu.RaiseCanExecuteChanged();
        LogEklemeBaslatKomutu.RaiseCanExecuteChanged();
        LogEklemeDurdurKomutu.RaiseCanExecuteChanged();
        SenaryoOlusturVeGonderKomutu.RaiseCanExecuteChanged();
        AiLogDosyasiSecKomutu.RaiseCanExecuteChanged();
        AiAktifIzlereDonKomutu.RaiseCanExecuteChanged();
    }

    // ------------------------------------------------------------
    // Bağlantı
    // ------------------------------------------------------------

    private void Baglan(object? _ = null)
    {
        try
        {
            _yayinci = new UdpYayinci(
                HedefIp,
                Port,
                BroadcastModu);

            _simulator = new SensorSimulatoru(
                _yayinci,
                LogEkle);

            _simulator.MesajGonderildi += (_, _) =>
            {
                GonderilenMesajSayisi++;
            };

            _simulator.IzlerDegisti += (_, _) =>
            {
                IzListesiniTazele();
            };

            Baglandi = true;

            BaglantiDurumu = BroadcastModu
                ? $"Bağlandı → Broadcast 255.255.255.255:{Port}"
                : $"Bağlandı → Unicast {HedefIp}:{Port}";

            _tickTimer.Start();

            LogEkle($"[SISTEM] {BaglantiDurumu}");
        }
        catch (Exception ex)
        {
            BaglantiDurumu = $"Hata: {ex.Message}";
            LogEkle($"[HATA] Bağlantı kurulamadı: {ex.Message}");
        }
    }

    private void BaglantiyiKes(object? _ = null)
    {
        _senaryoIptalKaynagi?.Cancel();

        // Çalışan log ekleme işlemi varsa önce iptal isteği gönder.
        _logEklemeIptalKaynagi?.Cancel();

        _tickTimer.Stop();
        _otomatikModTimer.Stop();

        OtomatikModAcik = false;

        _yayinci?.Dispose();
        _yayinci = null;
        _simulator = null;

        SeciliIz = null;

        AktifIzler.Clear();
        _satirlar.Clear();
        LogReplayIzler.Clear();
        _logReplaySatirlar.Clear();

        Baglandi = false;
        BaglantiDurumu = "Bağlı değil";

        LogEkle("[SISTEM] Bağlantı kesildi.");
    }

    // ------------------------------------------------------------
    // Log dosyası seçme ve çalıştırma
    // ------------------------------------------------------------

    private void LogDosyasiSec(object? _ = null)
    {
        var dosyaSecici = new OpenFileDialog
        {
            Title = "ICD log dosyasını seç",
            Filter =
                "JSONL log dosyası (*.jsonl)|*.jsonl|" +
                "Tüm dosyalar (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        bool? sonuc = dosyaSecici.ShowDialog();

        if (sonuc != true)
        {
            return;
        }

        LogDosyasiYolu = dosyaSecici.FileName;

        string dosyaAdi =
            Path.GetFileName(LogDosyasiYolu);

        LogEklemeDurumu =
            $"Seçilen dosya: {dosyaAdi}";

        LogEkle(
            $"[LOG EKLEME] Dosya seçildi: {LogDosyasiYolu}");
    }

    private async void LogEklemeBaslat(object? _ = null)
    {
        if (_simulator is null || !Baglandi)
        {
            LogEklemeDurumu =
                "Önce UDP bağlantısını kurmalısınız.";

            return;
        }

        if (string.IsNullOrWhiteSpace(LogDosyasiYolu))
        {
            LogEklemeDurumu =
                "Önce bir log dosyası seçmelisiniz.";

            return;
        }

        if (!File.Exists(LogDosyasiYolu))
        {
            LogEklemeDurumu =
                "Seçilen log dosyası bulunamadı.";

            LogEkle(
                $"[HATA][LOG EKLEME] Dosya bulunamadı: " +
                $"{LogDosyasiYolu}");

            return;
        }

        /*
         * Log çalışırken otomatik mod kapatılır.
         * Böylece log olaylarıyla rastgele olaylar birbirine karışmaz.
         */
        _otomatikModTimer.Stop();
        OtomatikModAcik = false;

        _logEklemeIptalKaynagi?.Dispose();
        _logEklemeIptalKaynagi =
            new CancellationTokenSource();

        CancellationToken cancellationToken =
            _logEklemeIptalKaynagi.Token;

        /*
         * Async işlem devam ederken bağlantı kesilirse bile
         * bu işlem başladığı andaki simülatörü kullanır.
         */
        SensorSimulatoru simulator = _simulator;

        LogEklemeCalisiyor = true;
        LogEklemeDurumu = "Log dosyası okunuyor...";

        try
        {
            IReadOnlyList<IcdLogRecord> olaylar =
                await _logEklemeService.DosyayiOkuAsync(
                    LogDosyasiYolu,
                    cancellationToken);

            LogEkle(
                $"[LOG EKLEME] {olaylar.Count} olay okundu.");

            LogEklemeDurumu =
                $"{olaylar.Count} olay çalıştırılıyor...";

            await _logEklemeService.OlaylariCalistirAsync(
                olaylar,
                simulator,
                durum =>
                {
                    LogEklemeDurumu = durum;
                    LogEkle(durum);
                },
                cancellationToken);

            LogEklemeDurumu =
                $"Log ekleme tamamlandı. " +
                $"{olaylar.Count} olay gönderildi.";

            LogEkle(
                $"[LOG EKLEME] Tamamlandı. " +
                $"{olaylar.Count} olay gönderildi.");
        }
        catch (OperationCanceledException)
        {
            LogEklemeDurumu =
                "Log ekleme durduruldu.";

            LogEkle(
                "[LOG EKLEME] İşlem durduruldu.");
        }
        catch (Exception ex)
        {
            LogEklemeDurumu =
                $"Log ekleme hatası: {ex.Message}";

            LogEkle(
                $"[HATA][LOG EKLEME] {ex.Message}");
        }
        finally
        {
            LogEklemeCalisiyor = false;

            _logEklemeIptalKaynagi?.Dispose();
            _logEklemeIptalKaynagi = null;
        }
    }

    private void LogEklemeDurdur(object? _ = null)
    {
        if (!LogEklemeCalisiyor)
        {
            return;
        }

        LogEklemeDurumu =
            "Log ekleme durduruluyor...";

        LogEkle(
            "[LOG EKLEME] Durdurma isteği gönderildi.");

        _logEklemeIptalKaynagi?.Cancel();
    }

    // ------------------------------------------------------------
    // Yeni iz oluşturma
    // ------------------------------------------------------------

    private void YeniIzOlustur(object? _ = null)
    {
        if (_simulator is null)
        {
            return;
        }

        try
        {
            ushort? istenenTrackId = null;

            if (!string.IsNullOrWhiteSpace(YeniTrackIdMetni))
            {
                /*
                 * Önce int olarak okunur.
                 *
                 * Doğrudan ushort.TryParse kullanılırsa 70000 gibi
                 * sayısal fakat ushort sınırının üzerindeki bir değer için
                 * yanıltıcı bir hata mesajı oluşabilir.
                 */
                if (!int.TryParse(
                        YeniTrackIdMetni,
                        NumberStyles.Integer,
                        CultureInfo.CurrentCulture,
                        out int trackIdDegeri))
                {
                    LogEkle("[HATA] Track ID tam sayı olmalıdır.");
                    return;
                }

                /*
                 * Track ID sınırları doğrudan IcdLib NuGet paketinden alınır.
                 */
                if (trackIdDegeri < IcdConstants.MinTrackId ||
                    trackIdDegeri > IcdConstants.MaxTrackId)
                {
                    LogEkle(
                        $"[HATA] Track ID " +
                        $"{IcdConstants.MinTrackId}-" +
                        $"{IcdConstants.MaxTrackId} " +
                        "aralığında olmalıdır.");

                    return;
                }

                istenenTrackId = (ushort)trackIdDegeri;
            }

            _simulator.YeniIzOlustur(istenenTrackId);

            YeniTrackIdMetni = "";
        }
        catch (Exception ex)
        {
            LogEkle($"[HATA] {ex.Message}");
        }
    }

    // ------------------------------------------------------------
    // Teşhis, tasnif ve iz düşürme
    // ------------------------------------------------------------

    private void TeshisDegistir(object? _ = null)
    {
        if (_simulator is null || SeciliIz is null)
        {
            return;
        }

        _simulator.TeshisDegistir(
            SeciliIz.TrackId,
            SeciliYeniTeshis);
    }

    private void TasnifDegistir(object? _ = null)
    {
        if (_simulator is null || SeciliIz is null)
        {
            return;
        }

        _simulator.TasnifDegistir(
            SeciliIz.TrackId,
            SeciliYeniTasnif);
    }

    private void IziDusur(object? _ = null)
    {
        if (_simulator is null || SeciliIz is null)
        {
            return;
        }

        ushort trackId = SeciliIz.TrackId;

        _simulator.IziDusur(
            trackId,
            SeciliDusmeNedeni);

        SeciliIz = null;
    }

    // ------------------------------------------------------------
    // İz bilgilerini güncelleme
    // ------------------------------------------------------------

    private void IziGuncelle(object? _ = null)
    {
        if (_simulator is null || SeciliIz is null)
        {
            return;
        }

        /*
         * Hız önce int olarak okunur.
         * Ardından IcdLib içindeki gerçek ICD sınırları kontrol edilir.
         */
        if (!int.TryParse(
                YeniHizMetni,
                NumberStyles.Integer,
                CultureInfo.CurrentCulture,
                out int hizDegeri))
        {
            LogEkle("[HATA] Hız tam sayı olmalıdır.");
            return;
        }

        if (hizDegeri < IcdConstants.MinHiz ||
            hizDegeri > IcdConstants.MaxHiz)
        {
            LogEkle(
                $"[HATA] Hız " +
                $"{IcdConstants.MinHiz}-" +
                $"{IcdConstants.MaxHiz} kt " +
                "aralığında olmalıdır.");

            return;
        }

        /*
         * Yükseklik sınırları da IcdLib üzerinden alınır.
         */
        if (!int.TryParse(
                YeniYukseklikMetni,
                NumberStyles.Integer,
                CultureInfo.CurrentCulture,
                out int yukseklikDegeri))
        {
            LogEkle("[HATA] Yükseklik tam sayı olmalıdır.");
            return;
        }

        if (yukseklikDegeri < IcdConstants.MinYukseklik ||
            yukseklikDegeri > IcdConstants.MaxYukseklik)
        {
            LogEkle(
                $"[HATA] Yükseklik " +
                $"{IcdConstants.MinYukseklik}-" +
                $"{IcdConstants.MaxYukseklik} m " +
                "aralığında olmalıdır.");

            return;
        }

        /*
         * DoubleOku, hem Türkçe ondalık virgülü hem de
         * invariant nokta biçimini destekler.
         */
        if (!DoubleOku(
                YeniEnlemMetni,
                out double yeniEnlem))
        {
            LogEkle("[HATA] Enlem sayısal olmalıdır.");
            return;
        }

        if (yeniEnlem < IcdConstants.MinEnlem ||
            yeniEnlem > IcdConstants.MaxEnlem)
        {
            LogEkle(
                $"[HATA] Enlem " +
                $"{IcdConstants.MinEnlem}-" +
                $"{IcdConstants.MaxEnlem} " +
                "aralığında olmalıdır.");

            return;
        }

        if (!DoubleOku(
                YeniBoylamMetni,
                out double yeniBoylam))
        {
            LogEkle("[HATA] Boylam sayısal olmalıdır.");
            return;
        }

        if (yeniBoylam < IcdConstants.MinBoylam ||
            yeniBoylam > IcdConstants.MaxBoylam)
        {
            LogEkle(
                $"[HATA] Boylam " +
                $"{IcdConstants.MinBoylam}-" +
                $"{IcdConstants.MaxBoylam} " +
                "aralığında olmalıdır.");

            return;
        }

        /*
         * ICD sınır kontrolleri tamamlandığı için ushort dönüşümü güvenlidir.
         */
        ushort yeniHiz = (ushort)hizDegeri;
        ushort yeniYukseklik = (ushort)yukseklikDegeri;

        try
        {
            ushort trackId = SeciliIz.TrackId;

            _simulator.IziGuncelle(
                trackId,
                yeniHiz,
                yeniYukseklik,
                SeciliYonelim,
                yeniEnlem,
                yeniBoylam);

            /*
             * Bu log bir kullanıcı/sistem işlem logudur.
             * TRACK_UPDATED mesajının gerçekten gönderilip gönderilmediği
             * SensorSimulatoru.IziGuncelle metodunun davranışına bağlıdır.
             */
            LogEkle(
    $"[BEKLIYOR] İz verileri bellekte değiştirildi: " +
    $"ID={trackId}, " +
    $"Hız={yeniHiz}kt, " +
    $"Yükseklik={yeniYukseklik}m, " +
    $"Yönelim={SeciliYonelim}, " +
    $"Enlem={yeniEnlem:F6}, " +
    $"Boylam={yeniBoylam:F6}. " +
    $"Yeni değerler ilgili izin bir sonraki periyodik " +
    $"TRACK_UPDATED mesajında gönderilecek.");

            IzListesiniTazele();
        }
        catch (Exception ex)
        {
            LogEkle(
                $"[HATA] İz güncellenemedi: {ex.Message}");
        }
    }

    // ------------------------------------------------------------
    // Seçili iz değerlerini forma doldurma
    // ------------------------------------------------------------

    private void SeciliIzAlanlariniDoldur()
    {
        if (SeciliIz is null)
        {
            YeniHizMetni = "";
            YeniYukseklikMetni = "";
            YeniEnlemMetni = "";
            YeniBoylamMetni = "";

            return;
        }

        YeniHizMetni =
            SeciliIz.SpeedKnots.ToString(
                CultureInfo.CurrentCulture);

        YeniYukseklikMetni =
            SeciliIz.AltitudeMeters.ToString(
                CultureInfo.CurrentCulture);

        SeciliYonelim = SeciliIz.Yonelim;

        YeniEnlemMetni =
            SeciliIz.Latitude.ToString(
                "F6",
                CultureInfo.CurrentCulture);

        YeniBoylamMetni =
            SeciliIz.Longitude.ToString(
                "F6",
                CultureInfo.CurrentCulture);
    }

    private static bool DoubleOku(
        string metin,
        out double deger)
    {
        return double.TryParse(
                   metin,
                   NumberStyles.Float,
                   CultureInfo.CurrentCulture,
                   out deger)
               ||
               double.TryParse(
                   metin,
                   NumberStyles.Float,
                   CultureInfo.InvariantCulture,
                   out deger);
    }

    // ------------------------------------------------------------
    // Otomatik mod
    // ------------------------------------------------------------

    /// <summary>
    /// Otomatik modda rastgele aralıklarla yeni iz oluşturur,
    /// düşük olasılıkla teşhis/tasnif değiştirir veya bir izi düşürür.
    /// </summary>
    private void OtomatikModAdimi()
    {
        if (_simulator is null || !OtomatikModAcik)
        {
            return;
        }

        if (_simulator.AktifTrackIdleri.Count < 6 &&
            _rnd.NextDouble() < 0.3)
        {
            _simulator.YeniIzOlustur();
        }

        foreach (ushort trackId in
                 _simulator.AktifTrackIdleri.ToList())
        {
            if (_rnd.NextDouble() < 0.03)
            {
                Teshis[] degerler = TeshisSecenekleri;

                _simulator.TeshisDegistir(
                    trackId,
                    degerler[_rnd.Next(degerler.Length)]);
            }

            if (_rnd.NextDouble() < 0.03)
            {
                Tasnif[] degerler = TasnifSecenekleri;

                _simulator.TasnifDegistir(
                    trackId,
                    degerler[_rnd.Next(degerler.Length)]);
            }

            if (_rnd.NextDouble() < 0.01)
            {
                DropReason[] nedenler =
                    DusmeNedeniSecenekleri;

                _simulator.IziDusur(
                    trackId,
                    nedenler[_rnd.Next(nedenler.Length)]);
            }
        }
    }

    public void OtomatikModDegisti(bool acik)
    {
        OtomatikModAcik = acik;

        if (acik)
        {
            _otomatikModTimer.Start();
        }
        else
        {
            _otomatikModTimer.Stop();
        }
    }

    // ------------------------------------------------------------
    // Aktif iz listesini yenileme
    // ------------------------------------------------------------

    /// <summary>
    /// Simülatördeki güncel iz anlık görüntülerini DataGrid'e yansıtır.
    /// Yeni izler eklenir, düşürülenler kaldırılır ve mevcutlar yenilenir.
    /// </summary>
    private void IzListesiniTazele()
    {
        if (_simulator is null)
        {
            return;
        }

        KoleksiyonuTazele(
            _simulator.TumAktifVeriler(),
            _satirlar,
            AktifIzler);

        KoleksiyonuTazele(
            _simulator.LogReplayAktifIzleriGetir(),
            _logReplaySatirlar,
            LogReplayIzler);
    }

    private void AiLogDosyasiSec(object? _ = null)
    {
        var dosyaSecici = new OpenFileDialog
        {
            Title = "AI ile analiz edilecek ICD log dosyasını seç",
            Filter = "JSONL log dosyası (*.jsonl)|*.jsonl|Tüm dosyalar (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dosyaSecici.ShowDialog() != true) return;

        _aiLogDosyasiYolu = dosyaSecici.FileName;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AiSeciliLogDosyasiMetni)));
        _llmClient.ClearHistory();
        AiLogModuAktif = true;
        AiCevabi = string.Empty;
        SenaryoJson = string.Empty;
        SenaryoDurumu = $"Log analiz modu açıldı: {AiSeciliLogDosyasiMetni}";
        LogEkle($"[AI][LOG MODU] Dosya seçildi: {_aiLogDosyasiYolu}");
    }

    private void AiAktifIzlereDon(object? _ = null)
    {
        _aiLogDosyasiYolu = string.Empty;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AiSeciliLogDosyasiMetni)));
        _llmClient.ClearHistory();
        AiLogModuAktif = false;
        AiCevabi = string.Empty;
        SenaryoJson = string.Empty;
        SenaryoDurumu = "Aktif iz moduna dönüldü.";
        LogEkle("[AI] Aktif iz moduna dönüldü.");
    }

    private static void KoleksiyonuTazele(
        IReadOnlyList<TrackData> guncelVeriler,
        Dictionary<ushort, TrackRowViewModel> satirlar,
        ObservableCollection<TrackRowViewModel> koleksiyon)
    {
        HashSet<ushort> guncelIdler = guncelVeriler
            .Select(x => x.TrackId)
            .ToHashSet();

        foreach (ushort eskiId in satirlar.Keys
                     .Where(id => !guncelIdler.Contains(id))
                     .ToList())
        {
            if (satirlar.Remove(eskiId, out TrackRowViewModel? satir))
                koleksiyon.Remove(satir);
        }

        foreach (TrackData veri in guncelVeriler)
        {
            if (!satirlar.TryGetValue(veri.TrackId, out TrackRowViewModel? satir))
            {
                satir = new TrackRowViewModel();
                satirlar[veri.TrackId] = satir;
                koleksiyon.Add(satir);
            }

            satir.Guncelle(veri);
        }
    }

    // ------------------------------------------------------------
    // Log
    // ------------------------------------------------------------

    private void LogEkle(string mesaj)
    {
        string zaman =
            DateTime.Now.ToString("HH:mm:ss.fff");

        Loglar.Add($"{zaman}  {mesaj}");

        while (Loglar.Count > 500)
        {
            Loglar.RemoveAt(0);
        }
    }

    // ------------------------------------------------------------
    // IDisposable
    // ------------------------------------------------------------

    public void Dispose()
    {
        _senaryoIptalKaynagi?.Cancel();
        _senaryoIptalKaynagi?.Dispose();
        _senaryoIptalKaynagi = null;

        _logEklemeIptalKaynagi?.Cancel();
        _logEklemeIptalKaynagi?.Dispose();
        _logEklemeIptalKaynagi = null;

        _tickTimer.Stop();
        _otomatikModTimer.Stop();

        _yayinci?.Dispose();
        _scheduledActionRunner.Dispose();
        _llmClient.Dispose();
        _yayinci = null;
        _simulator = null;
    }

    // ------------------------------------------------------------
    // INotifyPropertyChanged
    // ------------------------------------------------------------

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(
        ref T alan,
        T deger,
        [CallerMemberName] string? adi = null)
    {
        if (EqualityComparer<T>.Default.Equals(
                alan,
                deger))
        {
            return false;
        }

        alan = deger;

        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(adi));

        return true;
    }
}
