# RHZ LED Controller

BIOSTAR **B650M-SILVER** anakartlı bir bilgisayarda kasa ışıklarını tek yerden yöneten Windows uygulaması. Üreticinin SILVER LIGHTNING / VIVID LED DJ yazılımlarının yerine geçer.

*A Windows app that controls the RGB lighting of a BIOSTAR B650M-SILVER system (front fans, CPU cooler and NVIDIA GPU) without the vendor software. The UI is in Turkish.*

## Neyi kontrol eder

| Parça | Yol |
| --- | --- |
| Ön kasa fanları | Anakart LED kontrolcüsü, SMBus 0x58, `io` bölgesi |
| CPU sıvı soğutucu | Aynı kontrolcü, `h1` bölgesi |
| Ekran kartı ışığı | NVIDIA NvAPI (Illumination Zones) |

Donanım erişimi imzalı [PawnIO](https://pawnio.eu) sürücüsü ve onun `SmbusPIIX4` modülü üzerinden yapılır. Uygulama kendi sürücüsünü kurmaz.

## Özellikler

- **Renk ve parlaklık:**
  - Ton × doygunluk renk seçici ve hex kodu girişi.
  - Gama düzeltmesi, böylece `#FF0000` LED'de gerçekten kırmızı yanar.
- **Sync:**
  - Açıkken üç parça aynı ayarı paylaşır.
  - Kapalıyken kasa çiziminde bir parçaya tıklanır ve o parça ayrı ayarlanır.
- **Yazılım efektleri:** Sabit, Nefes, Renk döngüsü, Yanıp sönme, Dalga ve Müzik. Müzik efekti ses çıkışının seviyesine göre yanar.
- **Ön fan donanım efektleri:** ARGB dalga, Yıldızlı gece, Yıldırım, Gökkuşağı, Parlama, Nefes ve Meteor. Bu efektleri kontrolcü kendisi çalıştırır, işlemci yükü yoktur.
- **Oyun uyarısı:** League of Legends'ta can belirlenen eşiğin altına düşünce bütün ışıklar kırmızı yanıp söner. Veri yalnızca oyunun resmi yerel API'sinden (Live Client Data) alınır; bellek okuma veya enjeksiyon yoktur.
- **Pencere ve tepsi:**
  - Canlı kasa önizlemesi.
  - Açık ve koyu tema; varsayılan olarak Windows temasını izler.
  - Sistem tepsisinde çalışır.
- **Hata kaydı:** Hatalar arayüzde gösterilmez, `%AppData%\LedSync\rlc-hata.log` dosyasına yazılır.

## Gereksinimler

- Windows 10/11 x64.
- [.NET 10](https://dotnet.microsoft.com/download) masaüstü çalışma zamanı. Derlemek için .NET 10 SDK gerekir.
- [PawnIO](https://pawnio.eu) kurulu olmalı.
- Yönetici yetkisi. Uygulama başlarken yetki ister.
- Işıklara yazan diğer programlar (OpenRGB, SignalRGB, VIVID LED DJ vb.) kapalı olmalı. Açıksa uygulama uyarır.

> Yalnızca B650M-SILVER üzerinde denendi. Başka anakartlarda SMBus adresi ve register'lar farklı olabilir. Doğrulanmamış donanıma yazmak için kodu değiştirmeyin.

## Derleme ve çalıştırma

```powershell
dotnet build .\ledsync\LedSync.csproj -c Release -o .\bin\ledsync
.\bin\ledsync\RLEDC.exe            # --tray: pencereyi açmadan tepside başlar
```

Donanıma dokunmadan arayüzün ekran görüntüsünü almak için:

```powershell
$env:__COMPAT_LAYER = 'RunAsInvoker'
.\bin\ledsync\RLEDC.exe --no-hardware --snapshot .\shot.png [--theme Light|Dark] [--part fans|cpu|gpu] [--game] [--picker]
```

## Klasörler

- `ledsync/`: uygulama (WPF).
- `ledtest/`: donanım katmanı (`Core.cs`, `NvGpu.cs`), konsoldan tarama ve test aracı, doğrulanmış register'ların listesi (`TEST_PLANI.md`).
- `AGENTS.md`: mimari, kurallar ve yapılacaklar. Geliştiriciler ve kodlama ajanları için.
- `DESIGN.md`: tasarım kararları.

## Lisans

Uygulama: [GPL-3.0](LICENSE).

`ledsync/modules/SmbusPIIX4.bin`, [namazso/PawnIO.Modules](https://github.com/namazso/PawnIO.Modules) projesinin imzalı derlemesidir ve LGPL-2.1 lisanslıdır. Ayrıntı: [ledsync/modules/README.md](ledsync/modules/README.md).

BIOSTAR, NVIDIA ve League of Legends ilgili sahiplerinin markalarıdır. Bu proje onlarla bağlantılı değildir.
