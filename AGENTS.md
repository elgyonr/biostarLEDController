# RHZ LED Controller (RLC) — ajanlar için proje rehberi

Güncelleme: 22 Ekim 2026. Bu dosya projenin **güncel** giriş noktasıdır.

**Public repo:** [elgyonr/biostarLEDController](https://github.com/elgyonr/biostarLEDController). Lisansı GPL-3.0. Repoda yalnızca `ledsync/`, `ledtest/` ve belgeler bulunur.

**Yalnızca yerelde kalanlar** (`.gitignore`'da):
- Araştırma: `arastirma/`, `indirilenler/`.
- Eski notlar: `HANDOFF.md`, `PROJE_NOTLARI.md`, `README_PROTOTIP.md`. Uygulama yazılmadan önceki dönemi anlatırlar; tarihçe olarak okunur, güncel durum için değil.
- Prototipler: `src/`, `ledgui/`, `tools/`, `design/`.
- Tanılama dosyaları: `diagnostics*.json`.

Repodan klonlanan bir kopyada bunlar yoktur; kopyada çalışan bir ajan bu klasörleri aramamalı.

## 1. Ne bu?

BIOSTAR **B650M-SILVER** anakartlı tek bir Windows 11 bilgisayarın RGB ışıklarını kendi uygulamamızla kontrol eden WPF masaüstü uygulaması. Üreticinin yazılımı (SILVER LIGHTNING / VIVID LED DJ) yerine geçer.

| Parça | Arayüz adı | Donanım yolu | Bölge |
| --- | --- | --- | --- |
| Ön kasa fanları | Ön fanlar (`fans`) | SMBus 0x58, PawnIO + imzalı `SmbusPIIX4.bin` | `io` |
| CPU sıvı soğutucu (üst fanlar + pompa) | CPU soğutucu (`cpu`) | SMBus 0x58 | `h1` |
| Ekran kartı ışığı | Ekran kartı (`gpu`) | NVIDIA NvAPI (Illumination Zones) | zone 0 |

- Ürün adı **RHZ LED Controller**, kısaca RLC. Exe `RLEDC.exe`. Marka Rhizzu.
- Kod ad alanı ve ayar klasörü tarihsel olarak `LedSync` adını taşır.
- Kullanıcı Türkçe konuşur. Arayüz metinleri Türkçedir.

## 2. Kesin kurallar (kullanıcının koyduğu, pazarlıksız)

1. **Yeni deneysel donanım yazmaları için önce kullanıcıdan onay al.** Bu kural yeni SMBus register'ı, yeni mod kodu ve bilinmeyen bölge için geçerli. Bilinen ve doğrulanmış yazmalar `Motherboard` / `GraphicsCard` içinde.
   - `Known.EnsureModeAllowed` bazı kombinasyonları kasıtlı olarak engeller (ör. `io` bölgesinde mod xD). Bu korumayı gevşetme.
2. **Oyun verisi yalnızca resmi kaynaklardan alınır.** Bellek okuma, DLL enjeksiyonu, paket yakalama ve anti-cheat atlatma yasak. Resmi kaynak örnekleri: LoL Live Client Data, CS2 GSI, UDP telemetri. Araştırma: `arastirma/OYUN_VERI_KAYNAKLARI.md`.
3. Siber güvenlik korumalarını ve API filtrelerini atlatmaya yönelik hiçbir yöntem önerme.
4. **Kasa görseli fotoğraf değildir.** Kullanıcının gerçek kasa fotoğrafı görsel olarak kullanılmaz. `CasePreview.xaml` vektör çizimdir.
5. **Rhizzu R sembolünün şekli değişmez.** Üzerine RGB işlem yapılabilir. Seçilen tasarım "A varyantı": beyaz R ve RGB şeritler.
6. **Hata mesajları arayüzde görünmez.** Hepsi `%AppData%\LedSync\rlc-hata.log` dosyasına yazılır. Tek istisna çakışma uyarısı (ör. OpenRGB çalışıyor).
7. **Senkron kuralı:**
   - Oyun uyarıları (LoL düşük can) yalnızca Sync açıkken çalışır. Böylece uyarı hiçbir zaman tek bir cihazda yanıp sönmez.
   - Müzik efekti ve ön fan donanım efektleri yalnızca Sync kapalıyken sunulur.

## 3. Klasörler

```text
led projesi/
  AGENTS.md                 bu dosya
  README.md                 public repo tanıtımı
  ledsync/                  ASIL UYGULAMA (WPF, .NET 10, x64, AssemblyName=RLEDC)
    modules/SmbusPIIX4.bin  imzalı PawnIO modülü (LGPL-2.1, değiştirme)
  ledtest/                  donanım katmanı + konsol test aracı
    Core.cs                 PawnIO/SMBus oturumu (LedSession.OpenShared/Acquire/Release); ledsync link'ler
    NvGpu.cs                NvAPI sarmalayıcı; ledsync link'ler
    Program.cs              konsol testleri
    TEST_PLANI.md           doğrulanmış register/mod kodları (Meteor = 01 vb.)
  --- aşağıdakiler yalnızca yerel klasörde, repoda yok ---
  ledgui/                   eski WinForms test arayüzü (Core.cs kullanır) — artık geliştirilmiyor
  src/                      ilk tanılama prototipi BiostarLedProbe — tarihsel
  arastirma/                tersine mühendislik notları, .asm dökümleri, oyun veri araştırması
  design/ledsync/           ilk HTML tasarım örneği (DESIGN.md'nin referansı)
  tools/                    SILVER/FACEIT tanı scriptleri — tarihsel
  bin/ledsync/              KURULU SÜRÜM: RLEDC.exe + modules\SmbusPIIX4.bin
  bin/snap/                 ekran görüntüsü (snapshot) derlemeleri
  indirilenler/             üretici paketleri (dokunma)
```

### ledsync/ içi

| Dosya | Görev |
| --- | --- |
| `Lighting.cs` | Çekirdek. Ayrıntılar aşağıda. |
| `MainWindow.xaml(.cs)` | Tek pencere, 1000×700, boyutlandırılamaz, kendi başlık çubuğu. Ayrıntılar aşağıda. |
| `CasePreview.xaml(.cs)` | Eğik izdüşümlü vektör kasa. `Tag`'li parçalar (`fans`/`cpu`/`gpu`), `PartClicked` olayı, `Select()`, `Show(s, t, alert)`. |
| `ColorPicker.xaml(.cs)` | Ton (yatay) × doygunluk (dikey) alanı. Parlaklık yok, renk hep tam parlak. |
| `GameRules.cs` | Oyun kuralı modeli: `GameRule`, `GameSettings`, `GameState`, `GameDefs` (LoL ve CS2 kural tanımları, öncelik, varsayılanlar, yükleme düzeltmesi). |
| `Cs2.cs` | CS2 GSI: `Cs2Setup` (cfg kurulumu) ve `Cs2Monitor` (yerel dinleyici). |
| `GameAlerts.cs` | `GameIcons.League()`: simge LeagueClient.exe'den alınır. `LolMonitor`: `https://127.0.0.1:2999/liveclientdata/allgamedata` adresini oyundayken 250 ms, oyun dışında 3 s aralıkla sorgular. Can, ölü olma, öldürme sayısı (artış = olay) ve `GameEnd` sonucunu çıkarır. İlk okumada taban alınır; eski olaylar tetiklenmez. Sertifika yalnızca 127.0.0.1:2999 için kabul edilir. |
| `AudioMeter.cs` | COM `IAudioMeterInformation` ile tepe ses seviyesi. Kendi MTA thread'inde çalışır, 3 s okunmazsa kendini durdurur. |
| `Theme.cs` | Açık/koyu token tablosu ve vurgu rengi geçersiz kılma. `Apply("System"/"Light"/"Dark")`. |
| `App.xaml(.cs)` | Stiller, tek kopya mutex'i (`Local\LedSync.App`), tepsi simgesi (NotifyIcon), snapshot modu, beklenmeyen hata kaydı. |
| `Assets/` | `RlcIcon.ps1` simgeleri üretir: `rlc.ico`, `rlc-off.ico` (gri şeritler), `rlc-256.png`. |

**`Lighting.cs` içeriği:**
- `LightEffect` enum'u.
- `DeviceLight` ve `LightState`. Ayarlar `%AppData%\LedSync\settings.json` dosyasında.
- `Effects`: saf `At(...)` fonksiyonu ve donanım efekt kodları.
- `LightController`: motor thread'i.
- `Motherboard` (SMBus yazma) ve `GraphicsCard` (NvAPI yazma).
- `Log` ve `Conflicts`.

**`MainWindow.xaml(.cs)` içeriği:**
- Sekmeler: Işık / Oyun.
- Sync anahtarı, renk örnekleri, efekt düğmeleri ve güç düğmesi.
- Tema düğmesi: koyudayken güneş, açıktayken ay gösterir.

## 4. Mimari — bilinmesi gerekenler

**Tek gerçek kaynak `LightState`.** Arayüz state'i değiştirir, `LightController` onu donanıma yansıtır. Önizleme ve donanım aynı saf fonksiyonu kullanır: `Effects.At(state, device, t, still, alert, preview)`. Efekt mantığını tek yerde değiştir, iki tarafa birden yansır.

**Motor thread'i (`LightController.Run`):**
- Işık durağansa bekler. Yazılım efekti varsa yaklaşık 30 fps çalışır.
- Global SMBus mutex'i (`Global\Access_SMBUS.HTP.Method`) **yalnızca kare başına** tutulur (`Acquire`/`Release`). Böylece aynı veri yolunu kullanan diğer programlar (HWiNFO vb.) kilitlenmez.
- Mod, hız ve renk yazmaları tekrarlanmaz (dedupe). Aynı değer ikinci kez yazılmaz.

**Renk:**
- sRGB değeri doğrusal ışığa çevrilir (`ToLinear`, gama). Bu yapılmazsa kırmızı pembe görünür.
- `LightState.Bright()` rengi, en büyük kanalı 255 olacak şekilde normalleştirir. Parlaklık yalnızca kaydırıcıdan gelir.
- Hazır renkler saf LED tonlarıdır (ör. kırmızı = `#FF0000`).

**Anakart (`Motherboard`):**
- Kayıt yapısı: `BoardZone(Id, Zone, Mode?, Speed?, R, G, B)`.
- Mod bayt'ı `| 0x80` ile yazılır. Statik mod kodları: `io`=0x09, `h1`=0x01.
- Donanım efektleri (yalnızca ön fanlar) bir kez yazılır, sonrasında kontrolcü kendisi çalıştırır. Hız register'ı **0xA9**.
- Kodlar: Dalga 03, Yıldızlı gece 04, Yıldırım 02, Gökkuşağı 05, Parlama 08, Nefes 07, Meteor 01.

**Ekran kartı (`GraphicsCard`):**
- Yazmalar arasında en az 50 ms bekler. `force` (statik ve çıkış) bu beklemeyi atlar.
- Başarısız yazma `Pending` ile tekrar denenir.
- Hata ancak 3 ardışık başarısızlıktan sonra durum olarak bildirilir. Her başarısızlık log'a yazılır.
- Bilinen durum: `NVAPI_NOT_SUPPORTED (-104)` ara sıra gelir ve tekrar denemede düzelir.

**Oyun ışık kuralları (LoL ve CS2):**
- Tanımlar `GameRules.cs` → `GameDefs.All`'da, ayarlar `LightState.Games["lol" | "cs2"]` altında (`GameSettings { On, Threshold, Rules, Token }`).
- Kural türleri:
  - **Durum kuralları** (`Seconds = 0`), durum sürdükçe yanar: düşük can, ölünce, CS2'de "kör olunca" (flash) ve "bomba kurulunca".
  - **Olay kuralları**, `Seconds` kadar yanar: öldürünce 2 s, kazanınca ve kaybedince 8 s.
- **Öncelik:** olay > `GameDefs.StateOrder` (ölü > flash > düşük can > bomba). Kural ışığı `LightController.SetOverlay` ile her şeyin üstüne biner.
- **Çalışma koşulu:** Kurallar yalnızca `Sync` ve oyunun `On` anahtarı açıkken çalışır. Her oyunun izleyicisi `IGameMonitor`'dür.
- **LoL** (`LolMonitor`): `allgamedata` adresini sorgular. TFT'de kurallar devre dışıdır.
- **CS2** (`Cs2.cs`):
  - `Cs2Setup`, Steam kütüphanesinden CS2'yi bulur ve `game\csgo\cfg\gamestate_integration_rlc.cfg` dosyasını yazar. Kural kapatılınca dosyayı siler.
  - `Cs2Monitor`, 127.0.0.1:3271'de küçük bir TCP/HTTP sunucusudur. Cfg'deki `auth.token` (`Games["cs2"].Token`) ile gelmeyen istekleri yok sayar.
  - Oyuncu öldüğünde GSI izlenen takım arkadaşını gönderir. `player.steamid != provider.steamid` ise oyuncu ölü sayılır ve arkadaşın canı ya da öldürmeleri kullanılmaz.
  - Maç sonucu, `map.phase` `gameover`'a geçerken oyuncunun takımının skoruna göre belirlenir.
  - 30 s istek gelmezse oyun kapanmış sayılır.
- **Arayüz:** Oyun sekmesinde üstte oyun seçimi var. Kural satırına tıklayınca düzenleyici açılır, tekrar tıklayınca kapanır. Kural Nefes'i tek renkli nabızdır (`Color2 = Color`).
- **Eski ayarlar:** `LolAlert`, `LolThreshold`, `LolRules` ve `LolLight` yüklemede `Games["lol"]`'a taşınır, bir daha yazılmaz.
- **Genel kural:** `Color2 == Color` olan her Nefes klasik tek renkli nabız olarak çalışır.

**Tema:**
- Uygulama varsayılan olarak Windows temasını izler.
- Tüm renkler `DynamicResource`. `Theme.Apply` Application kaynaklarındaki fırçaları değiştirir.
- Fluent'in mor vurgusu `SystemColors.AccentColor*Key` anahtarları geçersiz kılınarak bastırılır.

## 5. Derleme, çalıştırma, doğrulama

Gereken: Windows x64, .NET 10 SDK, kurulu PawnIO sürücüsü. Uygulama `requireAdministrator` manifest'iyle çalışır.

```powershell
# Kurulu sürümü güncelle: çalışan RLEDC'yi kapatır, derler, yönetici olarak başlatır
powershell -ExecutionPolicy Bypass -File .\deploy.ps1

# Donanıma dokunmadan arayüz görüntüsü al (yönetici gerektirmez)
dotnet build .\ledsync\LedSync.csproj -c Release -o .\bin\snap
$env:__COMPAT_LAYER = 'RunAsInvoker'
.\bin\snap\RLEDC.exe --no-hardware --snapshot .\shot.png [--picker] [--game] [--part fans|cpu|gpu] [--theme Light|Dark] [--effect Breath] [--game lol|cs2] [--rule lowhp]
```

- **Snapshot modu:** 500 ms bekler (anahtar animasyonları bitsin diye) ve ayarları kaydetmez (`SkipSave`).
- **`--tray`:** pencereyi açmadan yalnızca tepsiden başlar.
- **Doğrulama:** Gerçek LED davranışını yalnızca kullanıcı gözle doğrulayabilir. "Çalışıyor" demeden önce kullanıcıdan teyit al.
- **Kurulum (kullanıcı kararı, 22 Ekim 2026):** Her derlemeden sonra `deploy.ps1` çalıştırılır; kullanıcıya sorulmaz, onay beklenmez.
  - Betik çalışan RLEDC'yi zorla kapatır, gerekirse yönetici yetkisiyle `taskkill` kullanır.
  - Ardından derler ve yeni sürümü başlatır.
  - Derleme başarısız olursa betik durur; eski sürüm kapalı kalır.

## 6. Kod üslubu

- Kod içi yorumlar Türkçe ve **ASCII**dir (ı/ş/ğ yok: "isiklar", "donanim"). Bu, PowerShell kodlama sorunlarına karşı alınmış bir önlemdir. Arayüz metinleri ise tam Türkçe karakter kullanır.
- Dosyaları UTF-8 olarak düzenle. PowerShell `Get-Content`/`Set-Content` ile düzenleme Türkçe karakterleri bozdu (cp1254 sorunu). Edit aracını ya da Python `io.open(..., encoding='utf-8')` kullan.
- Kısa, açıklayıcı tek satır yorumlar yaz. Yorum "ne"yi değil "neden"i anlatsın.
- Renk ve fırçaları satır içi yazma. `App.xaml` ve `Theme.cs` token'larını kullan.

## 7. Doğrulanmış durum (gerçek donanımda)

- **Statik renkler:** üç cihazda doğru. Kırmızı net.
- **Yazılım efektleri:** akıcı çalışıyor.
- **Donanım efektleri:** Meteor kodu kullanıcı tarafından doğrulandı.
- **LoL kuralları:** sahte `allgamedata` sunucusuyla, donanımsız test kopyasında denendi. Öldürme (2 s), düşük can, ölü, kazanma (8 s) ve TFT'de devre dışı kalma doğru sırayla çalıştı. Gerçek maçta henüz denenmedi.
- **GPU yazma sınırlaması:** 30 s Renk döngüsü testinde hata yok.
- **RLEDC.exe:** 22 Ekim 2026'da kuruldu ve açıldı.
- **Henüz elle doğrulanmayanlar:**
  - tema düğmesi ve Windows temasını izleme
  - RGB anahtar animasyonu
  - tepsi simgesinin açık/kapalı hali
  - pencereyi sürükleme ve küçültme

## 8. Bilinen açıklar ve geliştirme listesi

Önem sırasına göre. Detaylı gerekçe sohbet özetinde; burada kısa hali var.

**Altyapı / risk**
1. ~~Git deposu yok~~ — **yapıldı (22 Ekim 2026).** Kökte `main` dalıyla depo var; uzak depo `origin` = `elgyonr/biostarLEDController` (public). Push, `core.sshCommand` ayarındaki deploy key ile yapılır. `.gitattributes` `.bin`/`.ico`/`.png` dosyalarını ikili (binary) işaretler.
2. ~~`SmbusPIIX4.bin` bir çıktı klasöründe~~ — **yapıldı.** Kaynak kopya artık `ledsync/modules/SmbusPIIX4.bin` içinde (SHA-256 `91f9b4b1…56e5`) ve derleme onu `modules\` altına kopyalıyor. `ledtest` ve `ledgui` modülü csproj'dan kopyalamıyor; bu projelerin çıktılarına elle konmuştu.
3. **`bin/` ve `obj/` OneDrive ile eşitleniyor.** Bu, derlemeyi yavaşlatabilir ve dosya kilitlenmesine yol açabilir.
4. ~~Eski belgeler yanıltıcı~~ — **kısmen yapıldı.** README uygulamaya göre yeniden yazıldı. Eski README `README_PROTOTIP.md` adıyla yerelde kaldı. HANDOFF ve PROJE_NOTLARI repoda yok ama yerelde hâlâ eski durumu anlatıyor.

**Davranış**
5. **Windows ile otomatik başlama yok.** `--tray` bayrağı var ama başlatan bir mekanizma yok. Uygulama yönetici yetkisi istediği için Run kayıt anahtarı işe yaramaz; "en yüksek ayrıcalıkla" çalışan bir Görev Zamanlayıcı görevi gerekir.
6. **Uyku/uyanma sonrası yeniden yazma yok.** `SystemEvents.PowerModeChanged` dinlenmiyor. Uyandıktan sonra kontrolcü kendi varsayılanına dönerse, uygulama durağan durumda olduğu ve değerleri tekrar yazmadığı (dedupe) için ışıklar yanlış kalır. Resume olayında dedupe önbelleği temizlenip zorla yazılmalı.
7. **Profil yok.** Tek bir ayar seti var. Adlandırılmış profiller ve tepsiden profil geçişi eklenebilir.
8. **GPU `force` yazmaları beklemeyi atlıyor.** Log'da "onceki yazmadan 15 ms | force True" ile `NVAPI_NOT_SUPPORTED` kaydı görüldü. Zorla yazmalar da en az 50 ms aralığa uymalı, ya da bir kez gecikmeli tekrar denenmeli.
9. **CPU kullanımı** yazılım efektinde, pencere açıkken bir çekirdeğin yaklaşık %24'ü. Pencere gizliyken hiç ölçülmedi. Gizliyken önizleme çizimi tamamen durmalı.
10. **Tepsi menüsünde hızlı renk ve efekt seçimi yok.** Şu an yalnızca ışık aç/kapat, aç ve çıkış var.

**Özellik**
11. ~~CS2 Game State Integration~~ — **yapıldı (22 Ekim 2026)**, sahte GSI istekleriyle test edildi; gerçek maçta denenmedi. Ayrıntılar §4. Önceki not: En güçlü resmi yol: yerel HTTP üzerinden can, flash ve bomba durumu gönderir. LoL'deki `LolMonitor` deseni izlenebilir; yapılandırma dosyası CS2'nin `cfg` klasörüne yazılır. Bu makinede SteelSeries ve Logitech de CS2'den GSI ile veri alıyor (`gamestate_integration_steelseries.cfg`, `..._logitech.cfg`); üçüncü bir cfg dosyası onlarla çakışmaz. Diğer adaylar: Rocket League Stats API, Forza/F1 UDP telemetrisi.
    - **Valorant (22 Ekim 2026 araştırması):**
      - Tek canlı kaynak Overwolf GEP. Bu kaynakta canlı `me.health` ile `kill`, `headshot`, `death`, `match_outcome` ve `spike_*` olayları var; verilen hasar ise yalnızca tur sonunda (`round_report`) geliyor.
      - Overwolf'un Riot uyum sayfasına göre özel (kişisel) Valorant uygulamaları artık kabul edilmiyor. Herkese açık uygulamalar için Riot **ve** Overwolf onayı gerekiyor.
      - Riot'un genel kuralına göre oyunculara hizmet eden ürünler, API kullanmasa bile Riot'a kaydedilmeli.
      - Overwolf verinin nasıl elde edildiğini açıklamıyor.
      - Sonuç: onay alınmadan Valorant köprüsü yazılmaz.
12. **Oyun uyarılarını genelleştirmek.** Ortak bir `IGameMonitor` arayüzü ve olay → efekt eşlemesi tanımlanabilir. Örnekler: düşük can → kırmızı yanıp sönme, flash → beyaz parlama, tur kazanma → yeşil.
13. **Çakışma uyarısı.** Kullanıcıya kaldırılması önerildi, henüz yanıt yok.

**Kalite**
14. **Test yok.** `Effects.At`, `LightState.Bright`/`IsHex` ve ayar yükleme/sıfırlama saf fonksiyonlardır ve kolayca birim testi yazılabilir.
15. **`MainWindow.xaml.cs` (yaklaşık 400 satır) ve `Lighting.cs` (yaklaşık 590 satır) büyüyor.** Sınıflar ayrı dosyalara bölünebilir: `Motherboard`, `GraphicsCard`, `Log` ve `Conflicts` bir `Hardware/` klasörüne taşınabilir.
16. **`Local\LedSync.App` mutex'i yalnızca aynı oturumu kapsar.** Hızlı kullanıcı değiştirmede iki kopya aynı SMBus'a yazabilir. `Global\` önekine geçmek düşünülebilir.

**Arayüz — değerlendirme önerileri (22 Ekim 2026)**

Kaynak: Codex'in altı ekran görüntüsüne dayalı incelemesi, [Arayüz değerlendirmesi](arastirma/ARAYUZ_DEGERLENDIRMESI_2026-10-22.md). Rapor yerel `arastirma/` klasöründe, Git'te yok. Kullanıcı 22 Ekim'de "hepsini uygula" dedi. Kullanıcının daha önce açıkça verdiği kararlarla çelişen öneriler bilerek uygulanmadı.

17. **Metinli parça seçimi** — **uygulanmadı (kullanıcı kararı).** Kullanıcı cihaz listesini kaldırtıp kasadan seçmeyi istedi. Önerinin klavye amacı başka yoldan karşılandı: Sync kapalıyken kasaya Tab ile gelinir, ok tuşlarıyla parça değişir.
18. **Genel kontrollerin sabit konumu / "Birlikte / Ayrı"** — **uygulanmadı (kullanıcı kararı).** Güç düğmesinin sağ altta durması ve "Sync" adı kullanıcının seçimi. Sync anahtarının ipucu iki modu zaten anlatıyor.
19. **Ön fan ayarlarını sadeleştirme** — **kısmen yapıldı.** Alt başlıktaki SMBus adresi ve bölge bilgisi ipucuna taşındı. Donanım efektlerini açılır bölüme toplamak uygulanmadı: bu efektler yalnızca Sync kapalıyken ve ön fanlar seçiliyken görünüyor, açılır bölüm her kullanımda bir tıklama ekler.
20. **Koyu temada kasa ayrımı** — **yapıldı.** Kasa yüzleri `CaseTop`/`CaseSide`/`CaseFront` token'larıyla koyu temada bir ton açıldı, `CaseEdge` kenar çizgisi eklendi (açık temada saydam).
21. **Renk ve oyun kontrolleri** — **yapıldı.**
    - Özel renk kutusu yazılı "Özel" düğmesi oldu (`CustomSwatch` stili).
    - LoL kartında kural özeti var ("Can %N altı → kırmızı yanıp sönme").
    - "Önizle" düğmesi uyarıyı 3 saniye yalnızca ekrandaki kasada oynatır; LED'lere yazmaz.
    - Oyun sekmesinde parça seçimi, seçim çerçevesi ve "parçaya tıkla" ipucu gizlendi.
    - Sync önkoşulu artık yalnızca sarı bantta yazıyor.
22. **Tema seçimi ve erişilebilirlik** — **kısmen yapıldı.**
    - Tema düğmesi Sistem / Açık / Koyu menüsü açıyor ve seçili modun simgesini gösteriyor.
    - Renk alanı ok tuşlarıyla kullanılabiliyor (Shift ile ince adım).
    - Ekran okuyucu ve %125–%150 DPI denenmedi.

Ayrıca: Parlaklık kaydırıcısının alt sınırı %1 oldu. Kayıtlı %0 değerler açılışta %80'e döner, çünkü %0 parlaklık "açık görünüp yanmayan parça" demekti; tamamen kapatmak güç düğmesinin işi.

**Efekt akıcılığı — inceleme (22 Ekim 2026)**

23. **Hareketli efektlerde CPU/GPU titremesi** — **henüz düzeltilmedi.** Kullanıcı CPU'da daha belirgin titreme bildirdi. Kodda: anakart rengi değişince sabit kanallar dahil üç kanal yeniden yazılıyor; 33 ms motor döngüsü + 50 ms GPU sınırı ideal örnekte GPU'yu 66 ms aralığa düşürüyor; iki renkli Nefes hesabı doğrusal → sRGB baytı → doğrusal baytı ile gereksiz yuvarlanıyor. Bunlar fiziksel kök nedenin kesin kanıtı değil. Önce gönderim süreleri ölçülmeli; sonra değişmeyen kanal yazmaları azaltılmalı, doğrusal hassasiyet sona kadar korunmalı ve GPU kendi 50 ms hedefine göre zamanlanmalı. Bilinen register/mod sınırları korunmalı, FPS körlemesine artırılmamalı. Ayrıntı ve sayısal hesap: [Renk geçişleri incelemesi](arastirma/RENK_GECISLERI_INCELEMESI_2026-10-22.md) (yerel, Git dışında). Fiziksel sonuç kullanıcıyla doğrulanmalı.

## 9. Yeni bir ajan nereden başlamalı?

1. Bu dosyayı oku. Ardından `ledsync/Lighting.cs` dosyasını oku: her şeyin merkezi orası.
2. Donanım kodları ve register'lar için `ledtest/TEST_PLANI.md` dosyasına bak.
3. Arayüz değişikliğinden sonra snapshot al ve görüntüye bak. Donanım değişikliğinden sonra kullanıcıdan gözle teyit iste.
4. Tasarım kararları için `DESIGN.md` dosyasına bak. Başındaki kutu güncel yönü anlatır; ancak tema artık açık ve koyu iki moddur ve marka renkleri #1E2732 / #AEB7C2'dir.
