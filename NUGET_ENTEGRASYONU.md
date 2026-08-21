# HavaIzi.IcdLib NuGet Entegrasyonu

Ortak ICD kütüphanesi çözüm içinde ayrı bir proje değildir.

Visual Studio'da şu konumda görünür:

```text
HavaIziSimulator.Core
└── Dependencies
    └── Packages
        └── HavaIzi.IcdLib (1.1.0)
```

Paket dosyası:

```text
LocalPackages/HavaIzi.IcdLib.1.1.0.nupkg
```

Yerel paket kaynağı `NuGet.config` içinde tanımlıdır. Çözüm açıldığında paket otomatik geri yüklenir.

Core projesinde yalnızca şu referans vardır:

```xml
<PackageReference Include="HavaIzi.IcdLib"
                  Version="1.1.0"
                  PrivateAssets="all" />
```

## Tek doğruluk kaynağı

Aşağıdaki protokol bileşenleri yalnızca NuGet paketinden kullanılır:

- `IcdConstants`
- `MessageType`
- `Yonelim`
- `Teshis`
- `Tasnif`
- `DropReason`
- `IcdEncoder`
- `IcdDecoder`
- `IcdValidator`
- `TrackData` ve diğer ICD modelleri
- Big-Endian, CRC ve epoch zaman işlemleri

Simülatör projesinde bunların tekrar tanımları bulunmaz.
