# Hava İzi Sensör Simülatörü

ICD-HIS-001 Rev 1.1 mesajlarını UDP unicast veya broadcast olarak gönderen .NET 8 simülatörüdür.

## Çözüm yapısı

```text
HavaIziSimulator.sln
├── HavaIziSimulator.Core
├── HavaIziSimulator
├── HavaIziSimulator.Wpf
└── HavaIziSimulator.McpServer
```

Ortak ICD kütüphanesi solution projesi değildir. `LocalPackages` klasöründeki `HavaIzi.IcdLib` NuGet paketi olarak kullanılır.

## Sadeleştirilmiş Core

```text
HavaIziSimulator.Core
├── SensorSimulatoru.cs
├── TrackModelleri.cs
└── UdpYayinci.cs
```

- `SensorSimulatoru.cs`: İz oluşturur, günceller, teşhis/tasnif değiştirir, iz düşürür ve heartbeat gönderir. Mesajları doğrudan `IcdEncoder` ile kodlar.
- `TrackModelleri.cs`: Yalnızca simülatörün çalışma zamanı iz durumunu tutar.
- `UdpYayinci.cs`: Hazır `byte[]` mesajını UDP üzerinden gönderir.
- `Ai/Chat/AiAssistantService.cs`: sohbeti yönetir; Groq yanıtlarını ve tüm MCP
  araç çağrılarını aynı akışta işler.
- `Llm/Groq/GroqChatClient.cs`: OpenAI uyumlu Groq HTTP isteğini gönderir;
  hem `content` hem de `tool_calls` cevabını okur.
- `LogAnalysis/LogAnalysisContextBuilder.cs`: seçilen JSONL logunu teşhis/tasnif
  zaman aralıklarına dönüştürerek log sorularına bağlam sağlar.
- `Llm/RadarMcpProcessClient.cs`: Ayrı MCP süreciyle stdio/JSON-RPC konuşur.
- `HavaIziSimulator.McpServer`: Çoklu iz, koşullu seçim ve sınıflandırma
  kurallarını modelden bağımsız uygular.

Projede yerel enum, CRC, Big-Endian, ICD sabiti veya ikinci mesaj kodlayıcı bulunmaz. Bunların tamamı NuGet paketindedir.

## Visual Studio'da çalıştırma

1. `HavaIziSimulator.sln` dosyasını açın.
2. Solution'a sağ tıklayıp **Restore NuGet Packages** seçin.
3. `HavaIziSimulator.Wpf` projesini başlangıç projesi yapın.
4. **Build → Rebuild Solution** çalıştırın.
5. F5 ile başlatın.

Groq anahtarının güvenli tanımı, örnek doğal dil komutları ve MCP öz testi için
`LLM_MCP_KURULUMU.md` dosyasını izleyin. MCP sunucusu WPF ile otomatik başlar;
ayrı bir terminal komutu gerekmez.

İlk yerel test için:

```text
Hedef IP: 127.0.0.1
Port: 5000
Broadcast: kapalı
```

## Ekran düzeni

- **İz Yönetimi:** Manuel/otomatik/AI ve log replay dahil bütün aktif izler;
  oluşturma, güncelleme, teşhis, tasnif ve düşürme kontrolleri.
- **Log Replay:** JSONL seçme/çalıştırma ve yalnız o replay'e ait aktif izler.
- **AI Asistan:** Başlangıçta aktif iz soruları ve doğal dil radar komutları
  çalışır. Soru alanının altından log seçilince yalnız o dosya sorgulanır;
  `Aktif İzlere Geri Dön` düğmesi create/update/drop modunu yeniden açar.
- **Gönderim Logları:** Bütün kaynaklardan UDP ile gönderilen mesajlar.
