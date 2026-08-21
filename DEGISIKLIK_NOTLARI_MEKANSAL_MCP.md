# Değişiklik notları

## Eklenenler

- Toplam MCP araç sayısı 11'e çıkarıldı.
- `radar_query_active_tracks` aktif izlerde deterministik filtre/sayım yapar.
- `radar_query_tracks_spatial` dinamik çözülen bölge, merkez noktası ve yarıçap içinde sorgu yapar.
- `radar_create_tracks_spatial` dinamik çözülen konum içinde iz oluşturur.
- Şehir koordinatları kaynak koda gömülmedi; yer adları çalışma anında
  `NominatimGeocodingClient` ile çözülür ve sonuçlar süreç içinde önbelleğe alınır.
- Aktif izlerin tam listesi Groq prompt'undan çıkarıldı. Liste yalnızca yerel MCP
  context'i olarak taşınır.
- Sorgu sonucunu cümleye çeviren ikinci Groq turunda tool şemaları tekrar
  gönderilmez; TPM tüketimi azaltılır.
- MCP dönüş modeli `McpCallResult` ile hem radar aksiyonu hem sorgu sonucu
  taşıyabilecek hale getirildi.

## Çalıştırma

Visual Studio'da `HavaIziSimulator.sln` açılıp çözüm yeniden derlenmelidir.
Mekânsal işlemler sırasında internet erişimi gerekir; MCP sunucusu OpenStreetMap
Nominatim servisine HTTPS isteği yapar. Groq anahtarı projeye gömülmemiştir;
mevcut yapılandırma yöntemi kullanılmaya devam eder.
