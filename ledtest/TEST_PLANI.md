# Anakart LED testi — yöntem ve adımlar

Tarih: 21 Ekim 2026. Program: `ledtest\Program.cs` → derlenmiş hali `bin\ledtest\LedTest.exe`.
Durum: **derlendi, donanımda henüz çalıştırılmadı.** Hiçbir LED rengi değiştirilmedi.

## Yöntem

BIOSTAR'ın `BS_LED` sürücüsü kullanılmıyor. Yol:

```
LedTest.exe → PawnIOLib.dll → PawnIO.sys (imzalı, çalışıyor)
           → SmbusPIIX4.bin (PawnIO.Modules resmi imzalı modülü)
           → AMD FCH SMBus (00:14.0, 1022:790B rev 71)
           → LED MCU (SMBus Byte Data okuma/yazma)
```

Bu, VIVID'in `0x226104` (read byte) ve `0x226108` (write byte) isteklerinin yaptığı işin aynısıdır; tek fark, ham port erişimi yerine imzalı ve doğrulamalı bir modül kullanılmasıdır. Modül `Global\Access_SMBUS.HTP.Method` kilidini alarak diğer SMBus yazılımlarıyla çakışmayı önler.

## VIVID'den çıkan kontrolcü sınıfları (EXE statik analizi)

Yönetici fonksiyonu `0x429B24` üç sınıfı sırayla dener; kimliği tutanları kullanır.

| Sınıf | Kurucu / vtable | Config anahtarı | 8 bit adres → **7 bit** | Beklenen kimlik (reg `0x03`) |
| --- | --- | --- | --- | --- |
| A | `0x42FB28` / `0x684944` | `LEDMCU_SMB` (vars. 2), `LED_IIC` | `0xB0` → **`0x58`** | `0x14` |
| B | `0x4333DC` / `0x68482C` | `LEDMCU_SMB` (vars. 2), `LED_IIC` | `0xB0` → **`0x58`** | `0x13` |
| C | `0x436A50` / `0x684714` | `LEDMCU1_SMB` (vars. 1), `LED_IIC1` | `0x60` → **`0x30`** | `0x12` |

> Düzeltme: Önceki notlarda `0x60` 7 bit adres gibi yazılmıştı. BS_LED, `SMBHSTADD`'a değeri olduğu gibi yazıyor ve okumada `+1` ekliyor; yani `0x60` ve `0xB0` 8 bit (R/W bitli) biçimdir.

`LEDMCU_SMB`/`LEDMCU1_SMB` değeri iki SMBus tabanından hangisinin kullanılacağını seçiyor (1 → yöneticinin `+4` alanı, 2 → `+8` alanı). Bu tabanların sayısal değeri EXE'de kesinleştirilmedi. AMD FCH'de iki aday var: birincil `0xB00` (port 0) ve aux `0xB20` (port 1). Test ikisini de yokluyor.

### Register haritası

| Register | Anlam | Kanıt |
| --- | --- | --- |
| `0x03` | Kimlik | Üç sınıfın tanıma metodu (`vtable+4`) |
| `0x97` | bit0 = açık/kapalı | A `0x42FCA0`, C `0x436BFC`: oku, bit0'ı ayarla, yaz |
| `0x98`, `0x99`, `0x9A` | Renk baytları 0, 1, 2 | Üç sınıfın renk metodu (`vtable+0x14`) |
| `0x9B` | Efekt kodu `| 0x80`; bit4 ayrı bir bayrak | A `0x42FD3C`, `0x42FFA0`, init `0x430138` |

Sınıf A efekt eşlemesi (VIVID efekt no → donanım kodu): 9→`0x01` (varsayılan), 8→`0x02`, 7→`0x04`, 10→`0x08` (yazılımla sürülen mod), 11→`0x20`, 23→`0x14`, 24→`0x12`, 25→`0x11`. `0x01`'in sabit renk ("Permanent") olduğu **tahmin**; init başka kod okursa varsayılan olarak bunu seçiyor.

Renk baytlarının fiziksel R/G/B sırası gözlenmedi. Test bunu kullanıcı gözlemiyle çözecek.

## Güvenlik önlemleri (kodda zorunlu)

- Yalnızca statik analizde bulunan adresler (`0x30`, `0x58`) ve portlar (0, 1). Port 2–4 seçilmiyor; seçmek birincil portu kullanan yazılımları bozabilir.
- Tarama yalnızca **okuma** yapar (register `0x03` ve durum register'ları). Quick-write probe kullanılmaz.
- `0x30–0x37`, DDR4 SPD EEPROM'larında yazma koruması komutlarıdır. `0x30`'a dokunmadan önce SMBIOS'tan tüm belleğin DDR5 olduğu doğrulanır. Bu makinede RAM DDR5 (SMBIOS tip 34).
- Yazmadan hemen önce kimlik yeniden okunur; beklenen değerle eşleşmezse yazma reddedilir.
- Güç ve mod yazımı yalnızca bunların doğrulandığı sınıflarda izinli.
- `--yes` olmadan `set`/`restore` kuru çalışma yapar.
- Her yazmadan önce önceki register değerleri `led-backup-*.json` dosyasına kaydedilir; `restore` ile geri alınır.
- Program sonunda SMBus portunu başlangıç değerine döndürür.

Bu makinedeki RAM, ADATA XPG Lancer Blade DDR5 (`AX5U6000C3016G-DTLABWH`); kullanıcı doğruladı, **RGB yok** (RGB sürümün kodu `…DTLABRWH`). DDR5 SPD hub'ları yine de SMBus üzerinde (`0x50–0x57`); rastgele adrese yazmamak için ayrı bir neden.

## Çalıştırma adımları

Önkoşul: `bin\ledtest\modules\SmbusPIIX4.bin` (PawnIO.Modules `0.2.11` sürüm arşivinden). Yerleştirildi (21 Ekim 2026):
- Arşiv `release_0_2_11.zip`, 69.582 bayt, SHA-256 `43608cb89bc84247fef1368a139013f7d043e17db6d6c8dfc9b46bf0905a81f4` (GitHub sürüm özetiyle eşleşti).
- `SmbusPIIX4.bin`, 42.404 bayt, SHA-256 `91f9b4b1c39e3d399ce48477a89d8f6bd3e58a2241064a4daec3a1513dff56e5`.
- İçerdiği ioctl'ler: `ioctl_identity`, `ioctl_piix4_port_sel`, `ioctl_smbus_xfer`, `ioctl_clock_freq`, `ioctl_set_sleep_mode`. Programın kullandığı üçü mevcut.
- İmza, `pawnio_load` sırasında PawnIO sürücüsü tarafından doğrulanır; çevrimdışı ayrıca doğrulanmadı. PowerShell **yönetici olarak** açılmalı. VIVID LED DJ, OpenRGB, SignalRGB, iCUE gibi RGB yazılımları kapalı olmalı.

1. **Tarama (salt okuma):**
   ```powershell
   cd .\bin\ledtest   # proje kok klasorunden
   .\LedTest.exe scan
   ```
   Çıktıda `==> Sinif ...` görünen satır, gerçek LED kontrolcüsüdür. Sonuç `led-scan.json` dosyasına yazılır.

2. **Kuru çalışma (yazma yok):** taramada eşleşen port ve adresle:
   ```powershell
   .\LedTest.exe set --port <P> --addr <A> --rgb FF 00 00
   ```

3. **Tek renk yazma:**
   ```powershell
   .\LedTest.exe set --port <P> --addr <A> --rgb FF 00 00 --yes
   ```
   Fanlarda ya da anakartta hangi rengin göründüğü not edilir. Ardından `00 FF 00` ve `00 00 FF` denenir; bu üç gözlem bayt sırasını belirler.

4. Renk değişmezse (yalnızca sınıf A): mod ve güç ile birlikte:
   ```powershell
   .\LedTest.exe set --port <P> --addr <A> --rgb FF 00 00 --mode 01 --power on --yes
   ```

5. **Geri alma:**
   ```powershell
   .\LedTest.exe restore --backup <led-backup-...json> --yes
   ```

## Gerçek sonuçlar

### 1. Tarama — 21 Ekim 2026 (yönetici, salt okuma)

| Port | Adres | reg `0x03` | Sonuç |
| --- | --- | --- | --- |
| 0 (`0xB00`) | `0x30` | `0x800701B1` | Aygıt yok |
| 0 (`0xB00`) | `0x58` | **`0x14`** | **Sınıf A eşleşti** |
| 1 (`0xB20`) | `0x30` | `0x800701B1` | Aygıt yok |
| 1 (`0xB20`) | `0x58` | `0x800701B1` | Aygıt yok |

Sınıf A'nın mevcut durumu: `0x97=0xC7` (bit0=1, açık), `0x98..0x9A = 00 00 00`, `0x9B=0x01`.

`0x800701B1` = `HRESULT_FROM_WIN32(ERROR_NO_SUCH_DEVICE)`. PawnIOLib modülün `STATUS_NO_SUCH_DEVICE` dönüşünü Win32 hatasına çeviriyor. Program artık bu kodu tanıyor.

Sonuç: B650M-SILVER'daki LED MCU, VIVID'in Sınıf A protokolünü konuşuyor ve birincil SMBus portunda (0) `0x58` adresinde. Bu, B650M-SILVER'ın VIVID config listesinde olmamasına rağmen aynı MCU ailesini kullandığını gösteriyor. Tarama donanıma hiçbir şey yazmadı.

### 2. Yalnızca renk yazma — fanlarda değişiklik yok

Kullanıcı gözlemi: "fanlarda hiçbir değişiklik olmadı."

EXE'nin ek incelemesi buna olası bir açıklama getiriyor. VIVID, Sınıf A'yı tanıdıktan sonra init (`0x430138`) içinde `0x9B`'yi okuyup **bit7 açık olarak geri yazıyor** (`0x4305CB`/`0x4305F3`). "Varsayılan" metodu `0x4300F4` ise şu sırayı izliyor:
1. Güç aç (`vtable+8`, `0x97` bit0 = 1).
2. `0x9B` bit4'ü temizle (`vtable+0x18`).
3. Mod 9 → `0x9B = 0x01 | 0x80 = 0x81` (`vtable+0xC`).
4. Renk yaz `0x98..0x9A` (`vtable+0x10` → `0x42FE04`).

Taramada `0x9B = 0x01`, yani bit7 kapalıydı. Hipotez: bit7 kapalıyken MCU yazılım renk register'larını uygulamıyor (BIOS/donanım modu). `--mode 01` seçeneği tam olarak `0x81` yazar ve VIVID'in sırasını tekrarlar. Bu, hipotezin sıradaki testidir.

### 3. `--mode 01` ile tekrar — yine değişiklik yok

Çıktı: `0x9B=0x81`, `0x98..0x9A = FF 00 00` olarak yazıldı ve geri okundu. MCU yazmaları kabul ediyor ve saklıyor, ama görünür etki yok.

**Açıklama (EXE'de yeni bulgu):** `0x98..0x9B` yalnızca **`LED_SYS`** bölgesine ait. `LED_SYS` yöneticisi (`0x429B24`) config değeri 0 ise hiç kontrolcü oluşturmuyor (`0x429B80`) ve `Config.ini`'deki bütün AMD profillerinde `LED_SYS=0`. Yani yazdığımız bölge bu anakart ailesinde muhtemelen bağlı değil.

Kimliği `0x14` olan aynı MCU'da her bölgenin ayrı bir register bloğu var. Her bölge yöneticisi kendi sınıfını oluşturuyor; hepsi `0x58` adresinde, kimlik `0x14`:

| Bölge | Yönetici / vtable | AMD config | Mod | Renk |
| --- | --- | --- | --- | --- |
| sys | `LED_SYS` / `0x684944` | 0 | `0x9B` | `0x98–0x9A` |
| h1 | `LED_H1` / `0x68490C` | **2** | `0x9F` | `0x9C–0x9E` |
| h2 | `LED_H2` / `0x6848D4` | 0 | `0xA3` | `0xA0–0xA2` |
| io | `LED_IO` / `0x68489C` | **4** | `0xA4` | `0xA5–0xA7` |
| sb | `LED_SB` / `0x684864` | 0 | `0xAA` | `0xAB–0xAD` |

`0x97` bit0 güç, tüm bölgeler için ortak görünüyor. Tüm mod fonksiyonları `kod | 0x80` yazıyor. H1'de mod `0x0B` iken renk yerine `0x90`'a renk indeksi yazılıyor.

Program `--zone` seçeneğiyle güncellendi; tarama artık beş bölgenin durumunu okuyor. Sıradaki test: aktif bölgeler `io` ve `h1`.

**Fabrika değerleri:** ilk yedek `led-backup-p0-58-20261021-033215.json` (`0x98..0x9A = 00`) ve `...-033226.json` (`0x9B = 0x01`). SYS'i geri almak için önce `033226`, sonra `033215` geri yüklenir.

### 4. Bölge taraması (salt okuma)

| Bölge | Mod | Renk | Yorum |
| --- | --- | --- | --- |
| sys | `0x81` | `FF 00 00` | Test 2–3'te bizim yazdığımız |
| h1 | `0x01` | `FF 00 0C` | Sıfırdan farklı: BIOS/donanım değeri |
| h2 | `0x01` | `00 00 00` | Boş |
| io | `0x09` | `FF 20 00` | Sıfırdan farklı: BIOS/donanım değeri |
| sb | `0x09` | `00 00 00` | Boş |

Mod kodlaması bölgeye göre değişiyor. SYS/H1/H2 mod fonksiyonları VIVID efekt numarasını çeviriyor (9 → `0x01`), IO/SB fonksiyonları çevirmeden yazıyor (9 → `0x09`). Taramadaki varsayılanlar bununla tutarlı. IO için doğru mod `09`, H1 için `01`.

### 5. İlk başarılı renk değişimi — 21 Ekim 2026

`set --zone io --rgb FF 00 00 --mode 01 --yes` → **ön kasadaki 3 fan kırmızı oldu.** Kullanıcı gözlemi, ilk kez kendi kodumuzla gerçek bir LED değişimi.

Kullanıcının bildirdiği bölge eşlemesi ve modlar:

| Bölge | Bağlı olan | Mod | Gözlem |
| --- | --- | --- | --- |
| io (`0xA4–0xA7`) | Ön kasa fanları (3 adet) | `01` | Kırmızı bir daire çiziyor |
| io | | `05` | Gökkuşağı |
| h1 (`0x9C–0x9F`) | CPU soğutucu LED'leri | `02` | Yanıp sönme |

Bayt 0 = kırmızı olarak gözlendi (`FF 00 00` → kırmızı). Efekt hızı henüz bulunmadı: mod/renk bloklarında yok, ayrı bir register'da ya da yazılım zamanlamasında olabilir.

### 6. Mod taraması (LedGui, `gozlemler.csv`)

**h1 (CPU soğutucu) — mod baytı bit alanı.** VIVID'in çeviri tablosu (`01, 02, 04, 08, 11, 12, 14, 20`) ve gözlemler bununla tutarlı:

| Değer | Gözlem | Yorum |
| --- | --- | --- |
| `01` | sabit | bit0 = sabit |
| `02` | yanıp sönme | bit1 = yanıp sönme |
| `04` | nefes alma | bit2 = nefes alma |
| `08` | sabit | bit3 = VIVID'de yazılımla sürülen mod (`0x430138`, renk iş parçacığı) |
| `03`, `06`, `07`, `09` | sabit / yanıp sönme / sabit / sabit | Geçersiz kombinasyonlar; kullanıcının fark ettiği "döngü" |
| `12` | iki renk yanıp sönerek geçiş | bit4 = renk döngüsü + yanıp sönme |
| `13` | çok hızlı renk geçişi | bit4 + kombinasyon |
| `14` | nefes alma + renk değiştirme | bit4 + nefes |
| `20` | mavi/beyaz nefes, renk değiştirme | bit5 |
| `31` | renk geçişi, daha yavaş | bit5 + bit4 + sabit |
| `32` | sönükten artarak yanıp sönme | |
| `33` | hızlı renk geçişi | |
| `34` | nefes + renk değiştirme | |

Hipotez: bit5 (`0x20`) ve denenmemiş bit6 (`0x40`) hız bitleri. Test: aynı renkte `04`, `24`, `44`, `64` nefes hızları karşılaştırılacak.

**io (ön fanlar) — sıralı efekt numarası** (VIVID IO mod fonksiyonu çeviri yapmıyor, `0x432288`):

| Mod | Gözlem |
| --- | --- |
| `01` | fade circle |
| `02` | nefes alma circle |
| `03` | circle |
| `04` | yıldırım |
| `05` | gökkuşağı |
| `06` | sabit |
| `07` | nefes alma |
| `08` | yanıp sönme |
| `09`, `0A`, `0B` | sabit |
| `0D` | tüm fanlar söndü |

`0C`, `0E`, `0F`: sabit.

**Güvenlik — io `xD` engellendi.** io'da alt yarım baytı `D` olan her değer (`0D`, `1D`, `2D`, …) tüm fanları beyaz parlatıp "kısa devre gibi" söndürüyor. Olası açıklama: tüm adreslenebilir LED'ler tam beyaz yanınca ARGB başlığının akım koruması devreye giriyor. Doğrulanmadı. `Core.cs` içindeki `Known.IsModeBlocked` bu değerleri hem CLI'da hem arayüzde reddediyor.

Bu gözlem io'nun efekti **alt 4 bitten** okuduğunu gösteriyor; üst bitler (`0x10–0x70`) ayrı bir parametre olabilir (hız/parlaklık hipotezi, denenmedi). h1'de renk döngüsüyle (`11`) bit5–6 değiştirildiğinde hız değişmedi; h1 hız hipotezi zayıfladı.

### 7. Hız — mod baytında değil, io'da `0xA9`

`ledgui.log`: io'da `04/24/44/74` ve `07/27/77`, h1'de `52–5F` denendi; yazmalar geri okundu, kullanıcı gözlemi "bitler hızı etkilemiyor". Mod baytının üst bitleri hız değil.

EXE: hız slider'ı `Img_P4_S_Speed_BMouseUp` (`0x415B1C`), config anahtarları `3_Speed` / `6_Speed`, bayrak `speed_supported`. Bölge sınıflarının `vtable+0x28` metodu hızı yazar:
- IO `0x4329DC`: değer `0x5050` değilse baytı doğrudan **`0xA9`**'a yazar.
- H1 `0x431474`: boş; CPU soğutucu bölgesinde hız yok.

Değer aralığı ve yönü (büyük = hızlı mı yavaş mı) doğrulanmadı. Arayüze io için "Hız (`0xA9`)" kontrolü eklendi; oturumda ilk okunan değer fabrika değeri olarak saklanır ve "ilk değerine döndür" ile geri yazılır.

### 8. Resmi SILVER yazılımı bulundu — 21 Ekim 2026

Kullanıcı BIOSTAR SILVER (SILVER LIGHTNING) paketini kurdu: `C:\Program Files (x86)\BIOSTAR\SILVERLIGHTNING`.
- `SVLT.exe` 1.5.2304.1400 (imzasız), çalışıyor.
- Sürücü `BS_RVSIO64.sys` 10.0.2209.2700, Microsoft WHCP imzalı, servis `BS_RVSIO` çalışıyor. VIVID'in `BS_LED` sürücüsünden farklı.
- `ARM\AMDRyzenMasterDriver.sys` de çalışıyor (SILVER'ın OC/OV bölümü).

`Config.ini` `[SupportMB]` içinde **`B650M-SILVER=1`**. Kartın profili `LED_SYS=0, LED_H1=2, LED_H2=0, LED_IO=4, LED_SB=0`: tersine mühendislikle çıkan bölge haritası resmi profille birebir aynı. Kayıtlı LED durumu `1_*` (h1) ve `3_*` (io) anahtarlarında: `ColorSparkle=9`, `Color=16711680`, `ColorLight=255` (parlaklık), `ColorIndex=1024`.

SILVER arayüzündeki efektler: Permanent, Shine, Meteor, Wave, Starry Sky, Breathe, Shine & Music, Lightning, Rainbow, Aurora. LED tipi seçimi: "5V LED".

Aynı profilde SMBus üzerindeki voltaj regülatörleri listeleniyor (`OV*_ADDRESS=0xC4/0xC2` RAA229004, `0x26` uP1816, `SMBINDEX=1`). Rastgele SMBus taraması yapılmamasının bir nedeni daha.

Sonraki yöntem: SILVER'da efekt/hız seçilip `LedTest.exe scan` ile **yalnızca okunarak** SILVER'ın yazdığı değerler kaydedilecek. Tarama çıktısı artık io için hız register'ını (`0xA9`) da gösteriyor.

### 9. SILVER gözlemiyle kesin eşleme

Yöntem: SILVER'da efekt/hız seçildi, ardından `LedTest.exe scan --report silver-*.json` ile **yalnızca okundu**. Dosyalar `bin\ledtest\silver-*.json`.

**io (ön fanlar, 5V ARGB):** mod = sıralı efekt numarası (`0x80` hariç).

| SILVER efekti | Kod | SILVER varsayılan hızı (`0xA9`) |
| --- | --- | --- |
| Permanent | `09` (başlangıç durumu; ayrı tarama yok) | `FF` |
| Shine | `08` | `32` |
| Wave | `03` | `50` |
| Starry Sky | `04` | `32` |
| Breathe | `07` | `80` |
| Shine & Music | `0A` (renk `1B0000`: yazılım ses seviyesine göre ölçekliyor) | `32` |
| Lightning | `02` | `32` |
| Rainbow | `05` | `50` |
| Meteor | `01` (21 Ekim 2026: `set --zone io --mode 01` ile yazıldı, kullanıcı SILVER'daki Meteor'a benzediğini doğruladı) | — |
| Aurora | taranmadı; `06` ("sabit") adayı | — |

**Hız `0xA9`:** slider en yavaş `FF`, orta `87`, en hızlı `01`. Değer gecikme gibi davranıyor: küçük = hızlı. Mod baytının üst bitleri hız değil (test 7).

**h1 (CPU soğutucu):** mod = bit alanı.

| SILVER efekti | Kod |
| --- | --- |
| Permanent | `01` |
| Shine | `02` |
| Breathe | `04` |
| Starry Night | `20` |
| Colorful Permanent | `11` |
| Colorful Shine | `12` |
| Colorful Breathe | `14` |
| Shine & Music | `08` (renk `140000`: yazılım sürüyor) |

`0x10` = Colorful (renk döngüsü), `0x20` = Starry Night. Test 6'daki "bit5–6 hız" hipotezi yanlıştı; `0x20` ayrı bir efekt.

**Renk sırası R, G, B kesin:** h1 Permanent kırmızı → `FF 00 00`. Başlangıçtaki h1 `00 00 FF`, `Config.ini`'deki `1_Color=16711680` ile tutarlı (Windows TColor BGR: `0xFF0000` = mavi).

**Parlaklık:** `ColorLight=255` config'de; ayrı bir register görülmedi. Music modlarındaki ölçeklenmiş renkler (`1B0000`, `140000`), parlaklığın renk baytları ölçeklenerek uygulandığını destekliyor (VIVID `0x42F594`).

Diğer: `0x97` tüm taramalarda `0xC7`. sys bölgesi `01 / 00 00 00` okundu; test 2–3'te yazdığımız değerler fabrika haline dönmüş.

Arayüz bu adlarla güncellendi: h1 ve io için SILVER efekt listeleri; io hız düğmeleri `< Yavaş` (+0x10) / `Hızlı >` (−0x10, en az `01`).

### 10. Ekran kartı — NVIDIA NvAPI Illumination (salt okuma)

OpenRGB bu kartı ("Palit GeForce RTX 4060 Dual") Palit I²C protokolüyle değil, **NvAPI Illumination** ile tanıyor; Palit dedektör listesinde RTX 4060 yok. NvAPI, NVIDIA sürücüsüyle gelen `nvapi64.dll` içinde; fonksiyon kimlikleri ve yapılar NVIDIA'nın MIT lisanslı `NVIDIA/nvapi` başlıklarından alındı. Ham I²C'ye dokunulmuyor.

Kod: `ledtest\NvGpu.cs`; komut `LedTest.exe gpu-scan` (yönetici gerekmez). Yapı boyutları: cihaz bilgisi 4424, bölge bilgisi 4552, bölge kontrolü 6476 bayt; sürücü sürümleri kabul etti.

| | Değer |
| --- | --- |
| GPU | NVIDIA GeForce RTX 4060, aygıt `0x288210DE`, alt sistem `0xF3111569` (Palit) |
| Cihaz 0 | GPIO PWM RGBW, pinler R5 G11 B19 W31, mod: yalnızca MANUAL |
| Cihaz 1 | GPIO PWM tek renk, pin 35, mod: yalnızca MANUAL |
| Bölge 0 | RGBW, kartın önü; şu an `FFFFFF`, beyaz 0, %100 |
| Bölge 1 | Tek renk (yalnızca parlaklık), kartın üstü; %100 |

Donanım efekti (PIECEWISE_LINEAR) yok; OpenRGB'de "mod değişmiyor" gözleminin sebebi bu. Efektler senkron için yazılımda hesaplanacak.

**Yazma testi (`gpu-set` / `gpu-restore`):**
- `gpu-set --zone <n> [--rgb r g b] [--white w] [--brightness 0-100] [--yes]`: sürücüden okunan kontrol yapısının kopyasında yalnızca hedef bölgenin modunu MANUAL'e, renk/parlaklık alanlarını yeni değere çevirir; diğer baytlar sürücünün değeri kalır.
- Yazmadan önce 6476 baytlık yapının tamamı `gpu-backup-z<n>-<zaman>.json` dosyasına kaydedilir. `gpu-restore --backup <dosya> [--yes]` yapıyı aynen geri yazar; kart kimliği ve bölge sayısı eşleşmezse reddeder.
- Yazma yalnızca doğrulanmış kart kimliğinde (`0x288210DE` / `0xF3111569`) yapılır (`NvGpu.VerifiedGpus`).
- Kuru çalışmalar (`--zone 0 --rgb FF 00 00`, `--zone 1 --brightness 30`) ve hata yolları (renksiz bölge 0, olmayan bölge 5) denendi; sonrasında okunan durum değişmedi.

**Gerçek yazma sonuçları (kullanıcı gözlemi):**
- Bölge 0 `--rgb FF 00 00`: **kartın ön ışığı kırmızı oldu.** Ekran kartı kendi kodumuzla kontrol ediliyor.
- Bölge 0 `--rgb FF 00 00 --brightness 20`: **parlaklık düştü.** `brightnessPct` donanımda uygulanıyor; renkleri yazılımda ölçeklemek gerekmiyor.
- Bölge 1 `--brightness 30`: görünür değişiklik yok. Sürücü değeri kabul edip en az 1,5 sn tuttu, ama kullanıcının üç denemesinin her birinde sonraki okuma %100'dü. Arka planda **OpenRGB servisi** çalışıyordu (ayarları yeniden uyguluyor olabilir); ThunderMaster da oturum açılışında başlayan bir görev olarak kurulu. Kullanıcı doğruladı: **kartta tek bir LED var (ön, bölge 0); bölge 1'e bağlı fiziksel LED yok.** Sürücü pin 35'i listeliyor ama Palit Dual'da kullanılmıyor. Uygulamada yalnızca bölge 0 kullanılacak.

## Test arayüzü (LedGui)

`bin\ledtest\LedGui.exe` — çift tıkla açılır, UAC ile yönetici yetkisi ister. Kaynak: `ledgui\MainForm.cs`. Donanım erişimi ve güvenlik kuralları CLI ile ortak `ledtest\Core.cs` dosyasından gelir.

- Açılışta otomatik tarar; yalnızca kimliği eşleşen kontrolcüde düğmeler etkinleşir.
- Bölge, renk (hazır düğmeler ya da renk seçici) ve mod (◀ ▶ ile tek tek) seçilir; "hemen uygula" açıkken her değişiklik anında yazılır.
- Mod oluşturucu bölgeye göre değişir. sys/h1/h2'de temel efekt (`01` sabit, `02` yanıp sönme, `04` nefes, `08` yazılım), "renk döngüsü (`0x10`)" ve deneysel hız (bit5–6: `00/20/40/60`) seçilir. io/sb'de efekt adlarıyla liste gösterilir. Ham değer ile oluşturucu iki yönlü senkron.
- Her yazmadan önce kimlik yeniden doğrulanır; yazmadan sonra register'lar geri okunup günlüğe yazılır.
- "Gözlemi kaydet" → `bin\ledtest\gozlemler.csv` (zaman, bölge, mod, renk, not). Kaydedilen not, o bölge/mod için arayüzde "bilinen" olarak gösterilir.
- "Bu bölgeyi ilk değerine döndür" → taramalarda gözlenen fabrika değerleri.
- "Register dökümü 0x90–0xAF" → salt okuma; hız register'ını aramak için.
- SMBus kilidi yalnızca her işlem süresince tutulur.

Arayüzün düzeni yönetici yetkisi olmadan açılarak kontrol edildi; donanım işlemleri arayüz üzerinden henüz denenmedi.

## Başarı ölçütü

API dönüş kodu değil, kullanıcının fanlarda ve anakartta gördüğü renk. Her denemenin çıktısı ve gözlem birlikte kaydedilmeli.

## Açık sorular

- B650M-SILVER'daki MCU'nun bu üç sınıftan biri olup olmadığı. Taramada eşleşme çıkmazsa SILVER LIGHTNING'in farklı bir protokol kullandığı anlaşılır; tahmin yürütülerek yazma yapılmayacak.
- Kasa fanlarının ve AIO'nun anakartın ARGB başlıklarına mı, yoksa ayrı bir hub'a mı bağlı olduğu.
