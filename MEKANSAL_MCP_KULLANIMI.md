# Mekânsal MCP kullanımı

Proje toplam 11 MCP aracı sunar. Yeni mekânsal araçlar şunlardır:

- `radar_query_active_tracks`: Aktif izleri filtreler, sayar ve özetler.
- `radar_query_tracks_spatial`: Yer adı, bölge, merkez noktası veya yarıçap içindeki aktif izleri sorgular.
- `radar_create_tracks_spatial`: Dinamik çözülen konum içinde iz üretir.

Yer adları uygulama koduna gömülmez. `NominatimGeocodingClient.ResolveAsync()`
OpenStreetMap Nominatim üzerinden adı çözer; `SpatialGeometryService.ResolveAsync()`
sonucu `REGION`, `POINT` veya `RADIUS` kapsamına dönüştürür.

## Örnekler

- `Ankara üzerinde bir İHA izi üret.`
- `Marmara Bölgesi'nde iki düşman uçak oluştur.`
- `Marmara Bölgesi üzerinde kaç iz var?`
- `Ankara'nın 100 km çevresindeki füzeleri listele.`

## Akış

1. `AiAssistantService.ChatAsync()` kullanıcı niyetini Groq'ya gönderir.
2. Groq uygun MCP aracını ve yalnızca parametrelerini seçer.
3. `RadarMcpProcessClient.CallToolAsync()` çağrıyı MCP sunucusuna iletir.
4. `McpToolRegistry.CallToolAsync()` ilgili sınıfın `ExecuteAsync()` metodunu çalıştırır.
5. Sorgu tool'u sayım ve filtreyi deterministik yapar; Groq yalnız sonucu okunabilir Türkçeye çevirir.
6. Create tool'u doğrulanmış `RadarActions` döndürür; WPF bunları mevcut ICD gönderim akışına verir.

Aktif izlerin tam listesi Groq prompt'una eklenmez. Veri, MCP context'i olarak
yerel süreçte tutulur; bu hem token kullanımını düşürür hem de modelin yanlış
sayım yapmasını engeller.
