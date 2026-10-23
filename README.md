# RHZ LED Controller

> **Sürüm 0.9.0 beta.** Bu bir beta sürümdür; hatalar olabilir. Sorunları [issue](https://github.com/elgyonr/biostarLEDController/issues) olarak bildirirsen `%AppData%\LedSync\rlc-hata.log` dosyasını eklemen çok yardımcı olur.

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
- **Oyun ışık kuralları:** League of Legends ve Counter-Strike 2 için düşük can, ölünce, öldürünce, kazanınca ve kaybedince; CS2'de ayrıca kör olunca (flash) ve bomba kurulunca. Her kuralın rengi ve efekti seçilebilir. Veri yalnızca oyunların resmi yollarından gelir (LoL Live Client Data, CS2 Game State Integration); bellek okuma veya enjeksiyon yoktur.
- **Pencere ve tepsi:**
  - Canlı kasa önizlemesi.
  - Açık ve koyu tema; varsayılan olarak Windows temasını izler.
  - Sistem tepsisinde çalışır.
- **Hata kaydı:** Hatalar arayüzde gösterilmez, `%AppData%\LedSync\rlc-hata.log` dosyasına yazılır.

## Kurulum

`RHZ-LED-Controller-0.9.0-beta-setup.exe` dosyasını çalıştır:
- Uygulama `Program Files\Rhizzu\RHZ LED Controller` klasörüne kurulur, Başlat menüsüne kısayol eklenir.
- İstersen "Windows açılınca tepside başlat" seçeneğiyle otomatik başlar (yönetici yetkisiyle zamanlanmış görev).
- Kurulum dosyası .NET'i kendi içinde taşır; ayrıca .NET kurman gerekmez.
- PawnIO kurulu değilse kurulum seni uyarır.
- Kaldırmak için: Ayarlar → Uygulamalar → RHZ LED Controller. Ayarların (`%AppData%\LedSync`) silinmez.

## Gereksinimler

- Windows 10/11 x64.
- Kaynaktan derlemek için [.NET 10](https://dotnet.microsoft.com/download) SDK. (Kurulum dosyası .NET'i kendi içinde taşır.)
- [PawnIO](https://pawnio.eu) kurulu olmalı.
- Yönetici yetkisi. Uygulama başlarken yetki ister.
- Işıklara yazan diğer programlar (OpenRGB, SignalRGB, VIVID LED DJ vb.) kapalı olmalı. Açıksa uygulama uyarır.

> Yalnızca B650M-SILVER üzerinde denendi. Başka anakartlarda SMBus adresi ve register'lar farklı olabilir. Doğrulanmamış donanıma yazmak için kodu değiştirmeyin.

## Derleme ve çalıştırma

```powershell
dotnet build .\ledsync\LedSync.csproj -c Release -o .\bin\ledsync
.\bin\ledsync\RLEDC.exe            # --tray: pencereyi açmadan tepside başlar
```

Kurulum dosyasını üretmek için (Inno Setup 6 gerekir; `winget install JRSoftware.InnoSetup`):

```powershell
powershell -ExecutionPolicy Bypass -File .\installeruild-setup.ps1   # dist\RHZ-LED-Controller-<sürüm>-setup.exe
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
