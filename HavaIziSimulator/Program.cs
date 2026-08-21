using HavaIziSimulator;
using HavaIziSimulator.Llm;
using IcdLib.Enums;

Console.OutputEncoding = System.Text.Encoding.UTF8;

if (args.Contains("--mcp-self-test", StringComparer.OrdinalIgnoreCase))
{
    await RadarMcpSelfTest.RunAsync();
    return;
}

Console.WriteLine("==========================================================");
Console.WriteLine(" Hava İzi Sensör Simülatörü — ICD-HIS-001 Rev 1.1");
Console.WriteLine(" UDP Soket Tabanlı Haberleşme Arayüzü");
Console.WriteLine("==========================================================");

// ---- Yapılandırma ----
string hedefIp = "127.0.0.1";
int port = 5000;
bool broadcastModu = false;

Console.Write($"Hedef IP [{hedefIp}] (broadcast için 'b' yazın): ");
string? girisIp = Console.ReadLine();
if (!string.IsNullOrWhiteSpace(girisIp))
{
    if (girisIp.Trim().Equals("b", StringComparison.OrdinalIgnoreCase))
        broadcastModu = true;
    else
        hedefIp = girisIp.Trim();
}

Console.Write($"Hedef Port [{port}]: ");
string? girisPort = Console.ReadLine();
if (!string.IsNullOrWhiteSpace(girisPort) && int.TryParse(girisPort, out int parsedPort))
    port = parsedPort;

using var yayinci = new UdpYayinci(hedefIp, port, broadcastModu);
var simulator = new SensorSimulatoru(yayinci);

Console.WriteLine();
Console.WriteLine(broadcastModu
    ? $"Gönderim modu: Broadcast → 255.255.255.255:{port}"
    : $"Gönderim modu: Unicast → {hedefIp}:{port}");
Console.WriteLine();
Console.WriteLine("Menü:");
Console.WriteLine("  1) Otomatik simülasyon başlat (rastgele izler, periyodik yayın + heartbeat)");
Console.WriteLine("  2) Manuel senaryo modu (izleri elle oluştur/güncelle/düşür)");
Console.Write("Seçim: ");
string? secim = Console.ReadLine();

switch (secim?.Trim())
{
    case "2":
        ManuelSenaryoModu(simulator);
        break;
    default:
        OtomatikSimulasyonModu(simulator);
        break;
}

return;

// =====================================================================

static void OtomatikSimulasyonModu(SensorSimulatoru simulator)
{
    Console.WriteLine();
    Console.WriteLine("Otomatik simülasyon başlatıldı. Çıkmak için Ctrl+C.");
    Console.WriteLine("Rastgele aralıklarla yeni izler oluşturulacak, teşhis/tasnif");
    Console.WriteLine("değişiklikleri anlık bildirilecek ve izler zaman zaman düşürülecektir.");
    Console.WriteLine();

    var rnd = new Random();
    var sonrakiYeniIzZamani = DateTime.UtcNow;
    var calisiyor = true;
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; calisiyor = false; };

    while (calisiyor)
    {
        var simdi = DateTime.UtcNow;

        // Zaman zaman yeni bir iz oluştur (aktif iz sayısı 6'dan azsa).
        if (simdi >= sonrakiYeniIzZamani && simulator.AktifTrackIdleri.Count < 6)
        {
            simulator.YeniIzOlustur();
            sonrakiYeniIzZamani = simdi + TimeSpan.FromSeconds(rnd.Next(3, 8));
        }

        simulator.Tick();

        // Rastgele: bazı izlerin teşhis/tasnif değerini değiştir veya izi düşür.
        foreach (var trackId in simulator.AktifTrackIdleri.ToList())
        {
            // %2 ihtimalle teşhis değiştir
            if (rnd.NextDouble() < 0.01)
            {
                var degerler = Enum.GetValues<Teshis>();
                simulator.TeshisDegistir(trackId, degerler[rnd.Next(degerler.Length)]);
            }

            // %1 ihtimalle tasnif değiştir
            if (rnd.NextDouble() < 0.01)
            {
                var degerler = Enum.GetValues<Tasnif>();
                simulator.TasnifDegistir(trackId, degerler[rnd.Next(degerler.Length)]);
            }

            // %0.5 ihtimalle izi düşür
            if (rnd.NextDouble() < 0.005)
            {
                var nedenler = Enum.GetValues<DropReason>();
                simulator.IziDusur(trackId, nedenler[rnd.Next(nedenler.Length)]);
            }
        }

        Thread.Sleep(150);
    }

    Console.WriteLine("Simülasyon durduruldu.");
}

static void ManuelSenaryoModu(SensorSimulatoru simulator)
{
    Console.WriteLine();
    Console.WriteLine("Manuel senaryo modu. Aktif izlerin periyodik TRACK_UPDATED");
    Console.WriteLine("yayını arka planda otomatik devam eder.");
    Console.WriteLine();
    Console.WriteLine("Komutlar:");
    Console.WriteLine("  create                → yeni rastgele iz oluştur (TRACK_CREATED)");
    Console.WriteLine("  teshis <id> <0-3>      → Teşhis değiştir (TESHIS_UPDATED)");
    Console.WriteLine("                           0=Bilinmeyen 1=Dost 2=Düşman 3=Tarafsız");
    Console.WriteLine("  tasnif <id> <0-4>      → Tasnif değiştir (TASNIF_UPDATED)");
    Console.WriteLine("                           0=Bilinmiyor 1=Uçak 2=DönerKanat 3=Füze 4=İHA");
    Console.WriteLine("  drop <id> <0-3>        → İzi düşür (TRACK_DROPPED)");
    Console.WriteLine("                           0=SinyalKaybı 1=KapsamaDışı 2=Manuel 3=Diğer");
    Console.WriteLine("  heartbeat              → HEARTBEAT gönder");
    Console.WriteLine("  list                   → aktif izleri listele");
    Console.WriteLine("  exit                   → çıkış");
    Console.WriteLine();

    var calisiyor = true;
    var backgroundThread = new Thread(() =>
    {
        while (calisiyor)
        {
            simulator.Tick();
            Thread.Sleep(150);
        }
    });
    backgroundThread.IsBackground = true;
    backgroundThread.Start();

    while (calisiyor)
    {
        Console.Write("> ");
        string? satir = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(satir)) continue;

        var parcalar = satir.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var komut = parcalar[0].ToLowerInvariant();

        try
        {
            switch (komut)
            {
                case "create":
                    simulator.YeniIzOlustur();
                    break;

                case "teshis":
                    ushort tId = ushort.Parse(parcalar[1]);
                    var teshisDeger = (Teshis)byte.Parse(parcalar[2]);
                    simulator.TeshisDegistir(tId, teshisDeger);
                    break;

                case "tasnif":
                    ushort tasId = ushort.Parse(parcalar[1]);
                    var tasnifDeger = (Tasnif)byte.Parse(parcalar[2]);
                    simulator.TasnifDegistir(tasId, tasnifDeger);
                    break;

                case "drop":
                    ushort dId = ushort.Parse(parcalar[1]);
                    var neden = (DropReason)byte.Parse(parcalar[2]);
                    simulator.IziDusur(dId, neden);
                    break;

                case "heartbeat":
                    simulator.HeartbeatGonder();
                    break;

                case "list":
                    Console.WriteLine("Aktif izler: " + string.Join(", ", simulator.AktifTrackIdleri));
                    break;

                case "exit":
                    calisiyor = false;
                    break;

                default:
                    Console.WriteLine("Bilinmeyen komut.");
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Hata: {ex.Message}");
        }
    }
}
