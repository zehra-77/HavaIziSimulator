# AI asistan değişiklikleri

- `GroqChatClient.CompleteAsync`: `tool_choice=auto` ile hem sohbet metnini hem
  birden çok MCP çağrısını okuyabilir.
- `AiAssistantService.ChatAsync`: Groq'nun seçtiği mevcut MCP araçlarını doğrudan
  çalıştırır. Başarılı radar komutunda ikinci Groq çağrısı yapmaz; kısa işlem
  özetini yerel olarak üretir. MCP hatasını ise Groq'ya geri verip düzelttirir.
- `NormalizeKnownArguments` ve promptu regex ile yeniden yazan katman kaldırıldı.
  Şema hatası olursa hata Groq'ya araç sonucu olarak dönerek modelin kendisini
  düzeltmesine izin verilir.
- `LogAnalysisContextBuilder.BuildAsync`: seçili JSONL dosyasından her track için
  teşhis, tasnif ve yönelim zaman aralıkları; hız/yükseklik min-max değerleri ve
  son konum bilgisi çıkarır. AI log cevaplarında yalnızca bu veriyi kullanır.
- `TRACK_DROPPED` kayıtları ayrıca `droppedTracks` listesine track ID, milisaniyeli
  yerel zaman ve `neden` değeriyle eklenir; log sorusu MCP tool geçmişiyle
  karıştırılmaz.
- `trackHistory` ve `historicalIndexes`, düşmüş veya sonradan durumu değişmiş
  izlerin geçmişte sahip olduğu bütün teşhis/tasnif/yönelim değerlerini
  deterministik olarak indeksler. “Herhangi bir zamanda düşman” hesabı LLM'e
  bırakılmaz.
- AI alanı ana sayfadan kaldırılıp `İz Yönetimi`, `Log Replay` ve `Gönderim
  Logları` yanındaki bağımsız `AI Asistan` sekmesine taşındı. Teknik MCP JSON'u
  kapalı bir `Expander` içine alındı; soru ve cevap alanları genişletildi.
- `AktifIzler` manuel, otomatik, AI/MCP ve replay kaynaklarının tamamını gösterir.
  `LogReplayIzler` yalnızca geçerli JSONL replay senaryosunun aktif izlerini
  gösterir. Ayrım `AktifIz.LogReplayIzi` kaynağıyla yapılır.
- AI sekmesi başlangıçta **aktif iz modu** ile açılır. Soru alanının altındaki
  `Log Dosyası Ekle` düğmesi **yalnız log analizi** modunu açar. Bu modda aktif
  izler ve MCP radar araçları Groq'ya gönderilmez. `Aktif İzlere Geri Dön`
  düğmesi log bağlamını kapatıp create/update/drop kullanımına geri döner.
- `MainViewModel.SenaryoOlusturVeGonder`: bağlantı varsa radar işlemlerini uygular;
  bağlantı olmasa da sohbet ve seçili log hakkında soru-cevap çalışır.
- `system-prompt.md` kısaltıldı, konuşma geçmişi son iki turla sınırlandı ve
  `MaxCompletionTokens=350` yapıldı. Böylece Groq TPM tüketimi azaltıldı.
- Mevcut `radar_create_tracks` aracına `groups` eklendi. “Bir uçak ve bir füze”
  artık tekrarlı paralel tool çağrılarına bağlı değildir; tek çağrıda iki grup
  gönderilir ve `RadarToolOperations.CreateTrackGroup` iki izi de üretir.
- `ExecuteRadarActionsTool` eklendi. “9580'in yönünü doğu yap ve 4251'i düşür”
  gibi farklı işlemler tek `radar_execute_actions` çağrısındaki `actions`
  dizisinde sıralı olarak mevcut MCP araçlarına yönlendirilir.

Hızlı kontrol cümleleri:

1. `Selam, ne yapabilirsin?`
2. `Bir uçak ve bir füze oluştur.`
3. `Rastgele bir aktif izi düşür.`
4. Bir JSONL log seçip `Füzeler hangi saatlerde aktifti?` diye sorun.
