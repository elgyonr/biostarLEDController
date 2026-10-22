# LED Sync — tasarım sistemi

> **Güncel yön (21 Ekim 2026, kullanıcı onayladı):** taste-skill ile çekilen **achromatic-ledger** stili (seed `1521687689`) ve kasa önizlemesi. Referans: tuvaldeki örnek (`https://claude.ai/artifact/3htYyYVEiyo12MaKNYoX2M`) ve yerel kopya `design/ledsync/index.html`.
> - Açık zemin `#f8f8f8`, mürekkep `#0a0a0a`, gri `#6b6b6b`, çizgi `#e6e6e6`, panel `#fafafa` (radius 16), birincil düğme yakın siyah `#141414`. Arayüzde başka renk yok; tek renk kullanıcının LED rengi ve yalnızca veride (önizleme, şerit, nokta).
> - Kabuk: 256 px kenar çubuğu (ürün işareti, kapsam "B650M-SILVER", gezinme), ortalanmış 1100 px sütun; her bölüm 340 px etiket + 720 px panel.
> - **Kasa önizlemesi** (koyu `#161616` plaka): eğik izdüşüm; yan yüz düz, ön ve üst yüz (+100, −40) ile geri çekilir. Ön mesh arkasında 3 fan (io), üst mesh altında 2 fan (h1), pompada ışıklı altıgen (h1), pompadan radyatöre iki boru, ekran kartında yukarı kıvrılan ışık çizgisi (NvAPI). Ortam ışığı yok.
> - İmza öğesi: "Önümüzdeki 10 saniye" şeridi (efekt aşamasında).
> - Yazı tipi: WPF'te Segoe UI Variable (sistem); tuvaldeki örnek Geist kullanıyor.
>
> Aşağıdaki ilk plan, bu yönün öncülü olarak duruyor.

Tarih: 21 Ekim 2026. Hedef platform: WPF (.NET 10), Windows 11.
Kaynaklar: `frontend-design` (konuya dayalı yön, iki aşamalı plan ve eleştiri), `ui-ux-pro-max` (WPF kuralları ve erişilebilirlik; `search.py --stack wpf`), `taste-skill` ve `impeccable` referans notları. impeccable'ın motoru çalıştırılmadı.

## Konu, kitle, birincil iş

- **Konu:** Kasanın içindeki ışık. Ön fanlar, CPU soğutucusu ve ekran kartı aynı rengi taşıyan üç ışık kaynağı.
- **Kitle:** Bilgisayarının başında oturan tek bir kişi. Uygulamayı ara sıra açar: renk değiştirmek, bir profile geçmek ya da ışıkları kapatmak için.
- **Birincil iş:** "Kasamdaki ışık şu an böyle görünsün" kararını birkaç saniyede vermek ve sonucu ekranda, kasaya bakmadan görmek.

## İlkeler

1. **Renk yalnızca ışığa ait.** Arayüzün geri kalanı nötr. Tek doygun renk, kullanıcının seçtiği LED rengi; o renk her yerde aynı anlama gelir: "ışığın şu an bu."
2. **Yerel, temalı değil.** Bu bir Windows sistem aracı, saatin yanında yaşıyor. Windows 11 Fluent teması (`ThemeMode="System"`), sistemin açık/koyu tercihi, Mica arka planı ve Segoe UI Variable kullanılır. Kendi "oyun" teması yok.
3. **Hareket ışıktan gelir.** Arayüzde dekoratif animasyon yok. Tek hareketli şey, seçilen efektin canlı önizlemesi. Windows'ta "animasyonları göster" kapalıysa önizleme durağan çizilir.
4. **Gerçek parçaların adı.** "Ön fanlar", "CPU soğutucu", "Ekran kartı"; "bölge io/h1" gibi iç adlar arayüze çıkmaz.
5. **Cesaret tek yerde.** Akılda kalan öğe büyük ışık önizlemesi. Çevresindeki her şey sakin ve disiplinli.

## Renk

Fluent sistem renkleri sistem temasından gelir; elle yazılan değerler yalnızca aşağıdakiler. Tümü `Themes/Colors.xaml` içinde tanımlanır; elemanlarda satır içi renk kullanılmaz.

| Ad | Açık | Koyu | Kullanım |
| --- | --- | --- | --- |
| `Light` | kullanıcı rengi | kullanıcı rengi | Önizleme, seçili efekt göstergesi, ana düğmenin rengi. Kodda ayarlanır (`DynamicResource`). |
| `Ink` | `#1B1B1B` | `#F3F3F3` | Birincil metin (Fluent varsayılanı) |
| `InkSoft` | `#5D5D5D` | `#C8C8C8` | İkincil metin, birim ve açıklamalar |
| `Plate` | `#FBFBFB` | `#2C2C2C` | Önizleme plakası, kontrol grupları |
| `Rule` | `#E5E5E5` | `#3A3A3A` | Grup ayırıcıları |
| `Off` | `#8A8A8A` | `#6E6E6E` | Işık kapalıyken önizleme ve devre dışı cihaz |

**Kontrast koruması:** `Light` rengi önizleme dışında metin ya da sınır olarak kullanılacaksa, plakaya karşı kontrastı 3:1'in altındaysa (ör. koyu temada lacivert, açık temada açık sarı) yanına `Ink` renginde 1 px dış çizgi eklenir. Renk asla metnin tek taşıyıcısı olmaz.

**Yüksek kontrast:** Windows yüksek kontrast modunda `Colors.xaml` atlanır; `SystemColors` kullanılır, önizleme ışığı yalnızca dış çizgi olarak çizilir.

## Tipografi

- **Yazı tipi:** Segoe UI Variable (Windows 11'in sistem yazı tipi). Tek aile; sistemin kendisi gibi görünmek için bilinçli tercih. ui-ux-pro-max'in önerdiği Inter, web için iyi bir varsayılan ama Windows aracında yabancı durur.
- **Ölçek** (Fluent tip rampası): Başlık 20 / Semibold · Alt başlık 14 / Semibold · Gövde 14 / Regular · Açıklama 12 / Regular.
- Büyük harfli etiket, harf aralıklı üst etiket ve tek kelimeyi vurgulama kullanılmaz. Bütün metinler cümle düzeninde.

## Yerleşim

Sabit boyutlu küçük pencere (760 × 520), sola hizalı. Sol tarafta önizleme, sağda kontroller.

```
┌───────────────────────────────────────────────────────────────┐
│ LED Sync                                  Işıklar  [● açık]  │
├──────────────────────────┬────────────────────────────────────┤
│                          │ Efekt                              │
│      ╭───────────╮       │ (•) Sabit  ( ) Nefes  ( ) Döngü    │
│     │  ön fanlar  │      │ ( ) Yanıp sönme  ( ) Dalga         │
│     │   ◯ ◯ ◯     │      │                                    │
│      ╰───────────╯       │ Renk                               │
│   ┌──────┐   ┌────────┐  │ ■ ■ ■ ■ ■ ■  [Renk seç…]           │
│   │ CPU  │   │ ekran  │  │                                    │
│   └──────┘   │ kartı  │  │ Parlaklık   ────────●──  80        │
│              └────────┘  │ Hız         ─────●─────  orta      │
│                          │                                    │
│  ☑ Ön fanlar             │ Profil  [Akşam kırmızı   ▾] Kaydet │
│  ☑ CPU soğutucu          │                                    │
│  ☑ Ekran kartı           │ ☑ Windows ile başlat               │
└──────────────────────────┴────────────────────────────────────┘
```

- **Önizleme (sol):** Kasanın içinin sade bir şeması. Üç ışık kaynağı gerçek konumlarına yakın: üstte üç fan halkası, altta CPU bloğu ve ekran kartının ön çizgisi. Her biri o anki rengiyle ve efektin kendi temposuyla çizilir. "Dalga" efektinde gecikme önizlemede de görünür. Kapalı bir cihaz `Off` renginde ve kesik çizgiyle çizilir.
- **Cihaz listesi önizlemenin altında,** aynı sırayla. Ayrı kartlar değil, sade onay kutuları.
- **Kontroller (sağ):** Gruplar arasında başlık ve 24 px boşluk; kutu yok. Efekt seçimi radyo düğmeleri.
- **Hız** kaydırıcısı Sabit efektte de yerinde kalır (yerleşim zıplamasın diye) ve etkin kalır; altında "Sabit efektte hız kullanılmaz" açıklaması görünür. Değeri, hareketli bir efekte geçildiğinde uygulanır.

## Tepsi

- Simge, o anki LED rengiyle doldurulmuş küçük bir halka; ışıklar kapalıyken içi boş.
- Menü: Işıkları kapat/aç · ayırıcı · profiller (seçili olan işaretli) · ayırıcı · LED Sync'i aç · Çıkış.
- Pencere kapatma düğmesi uygulamayı kapatmaz, tepsiye gizler. Çıkış yalnızca tepsi menüsünden.

## Metin

- Düğmeler ne yaptığını söyler: "Kaydet", "Işıkları kapat", "Renk seç".
- Hatalar ne olduğunu ve ne yapılacağını söyler, özür dilemez: "Ekran kartına ulaşılamadı. NVIDIA sürücüsünü kontrol edin; diğer ışıklar çalışmaya devam ediyor."
- Boş durum: profil yoksa "Şu anki ışığı bir profil olarak kaydedin."

## Erişilebilirlik ve kalite tabanı

- Bütün kontroller klavyeyle ulaşılabilir; odak görünür (Fluent varsayılanı korunur).
- Kısayollar `InputBindings` ile: Ctrl+S profil kaydet, Ctrl+L ışıkları aç/kapat.
- Metin kontrastı en az 4.5:1; Windows metin ölçeklendirmesi (%100–%225) yerleşimi bozmaz.
- Windows "animasyonları göster" kapalıysa önizleme durağan.

## Plan eleştirisi ve yapılan değişiklikler

İlk taslak (sohbetteki mockup) gözden geçirildi; "her benzer uygulamada çıkacak varsayılan" olan kısımlar değiştirildi:

| İlk taslak | Neden varsayılan | Değişiklik |
| --- | --- | --- |
| Üç cihaz aynı görünümlü kartlarda | frontend-design'ın "SaaS kart kiti" uyarısı: içeriği özdeş kartlara bölmek | Cihazlar önizlemede gerçek konumlarında; altında sade onay kutuları |
| Efektler hap biçimli düğmeler | Her araçta görülen etiket kümesi | Radyo düğmeleri: aynı anda yalnızca bir efekt seçildiğini doğrudan anlatır |
| ui-ux-pro-max'in mavi/yeşil paleti ve Inter | Takvim uygulaması için üretilmiş palet, web varsayılanı yazı tipi | Nötr Fluent yüzeyleri; tek renk kullanıcının LED rengi; Segoe UI Variable |
| Koyu arka plan ve tek parlak vurgu düşüncesi | frontend-design'ın "neredeyse siyah ve tek asit vurgu" klişesi | Sistem temasını izle; vurgu sabit bir marka rengi değil, ışığın kendisi |
| Önizleme yoktu | Kasaya bakmadan sonucu görme ihtiyacı karşılanmıyordu | Önizleme uygulamanın merkezi ve tek "cesur" öğesi |
