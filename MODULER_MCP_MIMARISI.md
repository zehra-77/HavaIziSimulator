# Modüler MCP mimarisi

## Ana akış

`MainViewModel.SenaryoOlusturVeGonder` → `AiAssistantService.ChatAsync`
→ `GroqChatClient.CompleteAsync` → gerekirse `RadarMcpProcessClient`
→ `HavaIziSimulator.McpServer` → `McpToolRegistry` → ilgili `IMcpTool.Execute`
→ `RadarScenarioValidator` → `SensorSimulatoru` → UDP.

Groq radar mesajını üretmez. `McpToolRegistry.ListTools` ile sunulan araçlardan
birini ve parametrelerini seçer. Mesaj oluşturma, filtreleme, rastgele değerler
ve ICD kontrolleri uygulama tarafında kalır.

## Klasörler

- `Mcp/Abstractions`: `IMcpTool`, `McpToolBase`, `McpToolContext`.
- `Mcp/Registry`: araç kaydı, listeleme ve ada göre çalıştırma.
- `Mcp/Radar`: her radar özelliği için ayrı tool sınıfı.
- `Mcp/Radar/ExecuteActions`: tek mesajdaki farklı mevcut araçları sıralı
  çalıştıran genel batch aracı; senaryoya özel sınıf üretmez.
- `Mcp/Radar/Shared`: ortak şemalar ve mevcut radar iş kuralları.
- `Mcp/Time`: gecikmeli radar işlemi modeli, aracı ve çalışma zamanı yöneticisi.
- `Ai/Chat`: sohbet geçmişi, Groq araç döngüsü ve doğal dil cevabı.
- `Llm/Groq`: yalnızca Groq HTTP taşıması ve cevap ayrıştırması.
- `LogAnalysis`: seçilen JSONL logunun kompakt, doğrulanmış AI bağlamı.

## Yeni tool ekleme

1. Uygun özellik klasöründe `McpToolBase` türevi bir sınıf oluştur.
2. `Name`, `Description`, `SchemaJson` ve `Execute` üyelerini tanımla.
3. Sınıfı `McpToolCatalog.CreateDefault` listesine ekle.

`McpToolRegistry`, `Program.cs` ve `AiAssistantService` içinde yeni bir `switch`
eklenmesine gerek yoktur.

## Zamanlı işlem

`time_schedule_radar_action`, `SCHEDULED_ACTION` zarfı üretir. WPF tarafındaki
`ScheduledActionRunner` arayüzü bloklamadan bekler. Süre dolduğunda
`AiAssistantService.CallToolDirectAsync` ilgili radar aracını Groq'yu ikinci kez
çağırmadan, o andaki `SensorSimulatoru.AktifIzleriGetir()` sonucu ile çalıştırır.

Örnekler:

- `20 saniye sonra bir uçak izi oluştur`
- `10 saniye sonra 3 İHA oluştur`
- `30 saniye sonra hızı 400 altındaki izleri düşür`
- `5 saniye sonra Track ID 45'in teşhisini düşman yap`

Zamanlı filtreler süre dolduğu anda güncel aktif izler üzerinde değerlendirilir.
Uygulama kapatıldığında `ScheduledActionRunner.Dispose` bekleyen görevleri iptal eder.
