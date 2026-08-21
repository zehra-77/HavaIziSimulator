# Groq + GPT-OSS + MCP kurulumu

Bu sürümde Ollama artık uygulama akışında kullanılmaz. Varsayılan LLM,
Groq üzerinde barındırılan `openai/gpt-oss-20b` modelidir. Bu nedenle model
bilgisayarınızda yaklaşık 5 GB RAM ayırmaz.

Ollama şu anda ayrıca çalışıyorsa belleği hemen bırakması için PowerShell'de
`ollama stop qwen2.5:7b-instruct` çalıştırın ve Ollama'nın tepsi uygulamasından
çıkın. Yeni WPF akışı Ollama'yı oluşturmaz veya çağırmaz.

## 1. Ücretsiz API anahtarını ekleyin

1. `https://console.groq.com/keys` adresinden bir Groq API anahtarı oluşturun.
2. PowerShell'i açıp aşağıdaki komutu çalıştırın:

```powershell
[Environment]::SetEnvironmentVariable("GROQ_API_KEY", "BURAYA_ANAHTAR", "User")
```

3. Visual Studio'yu tamamen kapatıp yeniden açın. Ortam değişkeni yalnızca yeni
   açılan süreçlerde görünür.

API anahtarını `cs` veya `xaml` dosyasına yazmayın ve Git'e eklemeyin.

## 2. Çalıştırın

1. `HavaIziSimulator.sln` dosyasını Visual Studio ile açın.
2. Başlangıç projesi olarak `HavaIziSimulator.Wpf` seçin.
3. Uygulamayı çalıştırın ve önce UDP bağlantısını kurun.
4. **AI Asistan** sekmesinde doğal dil komutunu veya sorunuzu gönderin.

Örnekler:

- `5 tane rastgele uçak oluştur.`
- `Bir uçak ve bir füze oluştur.`
- `Hızı 400 knot altında olan tüm izleri düşür.`
- `Yüksekliği 3000 metreden fazla olan izleri düşman yap.`
- `Track ID 101 olan izi İHA olarak sınıflandır.`
- `Tüm aktif izlerin hızını 450 yap.`

## 3. Yeni çalışma sırası

1. `AiAssistantService.ChatAsync` konuşma geçmişini, aktif iz özetini ve varsa
   `LogAnalysisContextBuilder` tarafından hazırlanan log bağlamını Groq'a gönderir.
2. `GroqChatClient.CompleteAsync` cevaptaki doğal metni ve bütün `tool_calls`
   öğelerini okur. `tool_choice=auto` kullanıldığı için sohbet sorusunda araç
   zorunlu değildir; radar komutunda model uygun MCP araçlarını seçer.
3. `RadarMcpProcessClient`, ayrı `HavaIziSimulator.McpServer` sürecini otomatik
   başlatır ve stdio üzerinden MCP `initialize`, `tools/list` ve `tools/call`
   mesajlarını yürütür. Kullanıcının ayrıca terminal açması gerekmez.
4. Ayrı MCP sunucusundaki `RadarToolService` gerçek aktif iz listesi üzerinde
   filtreleme ve doğrulama yapar.
5. MCP aracı tam sayıda `RadarScenarioDto` üretir.
6. `RadarScenarioValidator` ICD sınırlarını ve enum değerlerini doğrular.
7. `SensorSimulatoru` mesajları IcdLib ile kodlayıp UDP üzerinden gönderir.

Başarılı radar komutunda yalnızca bir Groq HTTP çağrısı yapılır. MCP sonucu
ikinci kez Groq'ya özetletilmez; `AiAssistantService.BuildActionSummary` kısa
sonucu üretir. İkinci çağrı yalnızca MCP argüman hatasını modele düzelttirmek
gerektiğinde yapılır.

LLM nihai radar JSON'unu serbestçe yazmaz. Hangi mevcut aracın çağrılacağını
doğal dilden çıkarır. Örneğin uçak+füze isteğinde tek `radar_create_tracks`
çağrısında iki ayrı `groups` elemanı gönderir. `NormalizeKnownArguments` veya kullanıcı
cümlesini yeniden yorumlayan regex katmanı yoktur. Adet, benzersiz ID, rastgele
alanlar ve `hiz < 400` gibi filtrelerin uygulanması MCP tarafındaki C# kodundadır.

## 4. Log dosyasına soru sorma

**Log Ekleme** bölümünden mevcut JSONL dosyasını seçin; logu oynatmanız gerekmez.
Ardından **AI Asistan** sekmesinde örneğin şunları sorun:

- `Hangi track ID'leri düşmandı?`
- `Füzeler hangi saat aralıklarında aktifti?`
- `Track 45'in teşhis ve tasnif geçmişini açıkla.`

`LogAnalysisContextBuilder` kayıtları deterministik olarak okur ve durum
aralıklarını çıkarır. Groq yalnızca bu doğrulanmış bağlama dayanarak okunabilir
Türkçe cevap oluşturur.

## 5. Ayarları değiştirme

Varsayılan değerler:

```text
RADAR_LLM_BASE_URL=https://api.groq.com/openai/v1
RADAR_LLM_MODEL=openai/gpt-oss-20b
GROQ_API_KEY=...
```

Başka bir OpenAI uyumlu sağlayıcı kullanmak isterseniz ilk iki değeri Windows
ortam değişkeni olarak değiştirebilirsiniz. Farklı sağlayıcı kullanıldığında
`HostedLlmOptions.ApiKeyEnvironmentVariable` değeri de kodda o sağlayıcının
anahtar değişkenine göre ayarlanmalıdır.

## 6. Sorun giderme

- `GROQ_API_KEY ortam değişkeni bulunamadı`: Anahtarı ekledikten sonra Visual
  Studio'yu yeniden başlatın.
- `HTTP 401`: Anahtar yanlış, silinmiş veya başında/sonunda boşluk vardır.
- `HTTP 429`: Ücretsiz kullanım limitine ulaşılmıştır; bir süre sonra tekrar
  deneyin veya Groq hesabınızdaki limitleri kontrol edin.
- Koşulu sağlayan iz yoksa boş liste normaldir; hiçbir UDP işlem mesajı gönderilmez.

LLM API anahtarı kullanmadan yalnızca MCP iş kurallarını test etmek için:

```powershell
dotnet run --project .\HavaIziSimulator -- --mcp-self-test
```

Başarılı sonuçta beş benzersiz izin üretildiğini ve `hiz < 400` koşulunun
yalnızca 101 ile 103 numaralı test izlerini seçtiğini, sınıflandırmayı; heartbeat
ve eksik filtre reddini görürsünüz. Test gerçek ayrı MCP sürecini kullanır.

Derleme sırasında MCP sunucusunun EXE/DLL ve bağımlılıkları WPF çıktı klasörüne
otomatik kopyalanır. `MCP sunucusu bulunamadı` hatası görülürse çözümü tümüyle
yeniden derleyin. Özel konum kullanmak için `RADAR_MCP_SERVER_PATH` değişkenine
sunucu EXE veya DLL yolunu verebilirsiniz.

Groq ücretsiz planı oran limitlidir ve sağlayıcı bu limitleri ileride
değiştirebilir. Uygulamanın yerel RAM tüketimi model çalıştırılmadığı için
Ollama kullanımından belirgin ölçüde düşüktür.
