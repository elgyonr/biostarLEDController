using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LedSync;

[JsonConverter(typeof(JsonStringEnumConverter<LightEffect>))]
// Ilk bes efekt yazilimla (kare kare) calisir. Hw* efektleri on fan denetleyicisinin kendi efektleridir:
// bir kez mod kodu + renk + hiz yazilir, gerisini denetleyici yapar (TEST_PLANI.md, test 9).
enum LightEffect { Static, Breath, Cycle, Blink, Wave, Music, HwWave, HwStarry, HwLightning, HwRainbow, HwShine, HwBreathe, HwMeteor }

// Bir isik kaynaginin ayari: renk, parlaklik, efekt, hiz. Senkronda uc cihaz tek ayari paylasir.
sealed class DeviceLight
{
    public string Color { get; set; } = "#FF0000";
    // Nefes iki renk arasinda gecer; diger efektler yalnizca Color'u kullanir.
    public string Color2 { get; set; } = "#0000FF";
    public int Brightness { get; set; } = 80;
    public LightEffect Effect { get; set; } = LightEffect.Static;
    public int Speed { get; set; } = 5; // 1 (yavas) - 10 (hizli)

    [JsonIgnore]
    public (byte r, byte g, byte b) Rgb => Parse(Color);
    [JsonIgnore]
    public (byte r, byte g, byte b) Rgb2 => Parse(Color2);
    static (byte, byte, byte) Parse(string c) => (Convert.ToByte(c[1..3], 16), Convert.ToByte(c[3..5], 16), Convert.ToByte(c[5..7], 16));
    public DeviceLight Clone() => new() { Color = Color, Color2 = Color2, Brightness = Brightness, Effect = Effect, Speed = Speed };

    public bool Valid()
    {
        Speed = Math.Clamp(Speed, 1, 10);
        // %0 artik secilemez (kapatmak guc dugmesinin isi); eski kayitlardaki 0 varsayilana doner.
        Brightness = Brightness <= 0 ? 80 : Math.Min(Brightness, 100);
        if (!LightState.IsHex(Color)) return false;
        // Eski surumlerden kalan koyu renkler ayni tonda tam parlakliga cekilir.
        Color = LightState.Bright(Color) ?? "#FFFFFF";
        // Ikinci renk sonradan eklendi; eski kayitlarda yoksa ya da bozuksa varsayilana doner.
        Color2 = LightState.Bright(Color2 ?? "") ?? "#0000FF";
        return true;
    }
}

// Kullanicinin istedigi isik. Kaydedilir; acilista geri yuklenir.
sealed class LightState
{
    public bool On { get; set; } = true;
    public bool Sync { get; set; } = true;
    public DeviceLight Shared { get; set; } = new();
    // Ayri modda cihaz basina ayar; ilk kez ayri moda gecildiginde ortak ayardan kopyalanir.
    public Dictionary<string, DeviceLight> Devices { get; set; } = [];
    public Dictionary<string, bool> Enabled { get; set; } = new() { ["fans"] = true, ["cpu"] = true, ["gpu"] = true };
    // "System" (Windows uygulama temasi), "Light" ya da "Dark".
    public string Theme { get; set; } = "System";
    // Oyun basina isik kurallari ("lol", "cs2"); tanimlar GameRules.cs'te.
    public Dictionary<string, GameSettings> Games { get; set; } = GameDefs.Defaults();

    // Eski surumlerin LoL alanlari: yalnizca okunur (Games["lol"]'a tasinir), yazilmaz.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? LolAlert { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? LolThreshold { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, GameRule>? LolRules { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DeviceLight? LolLight { get; set; }

    public bool IsEnabled(string id) => Enabled.GetValueOrDefault(id, true);

    // Bu cihazin su anki ayari: senkronda ortak, ayri modda kendi.
    public DeviceLight For(string device)
    {
        if (Sync) return Shared;
        if (!Devices.TryGetValue(device, out var d)) Devices[device] = d = Shared.Clone();
        return d;
    }

    // Isiklar acik ve bir cihaz yazilim efektindeyse kare dongusu gerekir. Onizleme donanim efektlerini de canlandirir.
    public bool Moving(IEnumerable<string> devices, bool preview = false)
        => On && devices.Any(id => IsEnabled(id) && For(id).Effect is var e && e != LightEffect.Static && (preview || !Effects.IsHardware(e)));

    public LightState Clone() => new()
    {
        On = On, Sync = Sync, Shared = Shared.Clone(), Theme = Theme,
        Games = Games.ToDictionary(kv => kv.Key, kv => kv.Value.Clone()),
        Devices = Devices.ToDictionary(kv => kv.Key, kv => kv.Value.Clone()), Enabled = new(Enabled),
    };

    static readonly string Path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LedSync", "settings.json");

    public static LightState Load()
    {
        try
        {
            if (File.Exists(Path) && JsonSerializer.Deserialize<LightState>(File.ReadAllText(Path)) is { Shared: not null, Devices: not null, Enabled: not null } s
                && s.Shared.Valid() && s.Devices.Values.All(d => d is not null && d.Valid()))
            {
                // Eski LoL alanlari (tek kural seti ya da tek uyari isigi) oyun ayarlarina tasinir.
                // (Games varsayilanla dolu gelir; eski kayit eski alanlarin varligindan anlasilir.)
                if (s.LolAlert is not null || s.LolThreshold is not null || s.LolRules is not null || s.LolLight is not null)
                {
                    var lol = new GameSettings { On = s.LolAlert ?? true, Threshold = s.LolThreshold ?? 30, Rules = s.LolRules ?? [] };
                    if (s.LolLight is not null) lol.Rules["lowhp"] = new GameRule { Light = s.LolLight };
                    s.Games ??= [];
                    s.Games["lol"] = lol;
                }
                s.Games = GameDefs.Normalize(s.Games);
                s.LolAlert = null; s.LolThreshold = null; s.LolRules = null; s.LolLight = null;
                // Cihaz acma/kapama arayuzden kalkti; eski surumde kapatilmis bir cihaz gorunmeden kapali kalmasin.
                s.Enabled = new LightState().Enabled;
                // Donanim efektleri yalnizca on fanlarda var; baska cihaza yazilmissa sabite don.
                foreach (var (id, d) in s.Devices)
                    if (id != LightController.Fans && Effects.IsHardware(d.Effect)) d.Effect = LightEffect.Static;
                // Ortak ayarda donanim efekti ve muzik yok (muzik yalnizca Sync kapaliyken).
                if (Effects.IsHardware(s.Shared.Effect) || s.Shared.Effect == LightEffect.Music) s.Shared.Effect = LightEffect.Static;
                return s;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        return new LightState();
    }

    public void Save()
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        File.WriteAllText(Path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static bool IsHex(string s) => s.Length == 7 && s[0] == '#' && s.Skip(1).All(Uri.IsHexDigit);

    // Rengi ayni tonda tam parlakliga ceker (en guclu kanal 255). LED'de koyu renk sonuk isik demek;
    // parlakligi kaydirici belirler. Siyahin tonu yok: null.
    public static string? Bright(string hex)
    {
        if (!IsHex(hex)) return null;
        int r = Convert.ToByte(hex[1..3], 16), g = Convert.ToByte(hex[3..5], 16), b = Convert.ToByte(hex[5..7], 16);
        int max = Math.Max(r, Math.Max(g, b));
        if (max == 0) return null;
        byte S(int c) => (byte)Math.Round(c * 255.0 / max);
        return $"#{S(r):X2}{S(g):X2}{S(b):X2}";
    }
}

// Bir cihazin bir andaki isigi: ekran rengi (sRGB) ve 0-1 seviye (kullanici parlakligi dahil).
readonly record struct Light(byte R, byte G, byte B, double Level);

// Efektler zamanin saf fonksiyonu: onizleme ve donanim ayni hesabi kullanir.
static class Effects
{
    public static readonly (LightEffect id, string name, string desc)[] All =
    [
        (LightEffect.Static, "Sabit", "Seçtiğin renk sürekli yanar."),
        (LightEffect.Breath, "Nefes", "Seçtiğin iki renk arasında yavaşça gidip gelir."),
        (LightEffect.Cycle, "Renk döngüsü", "Tüm renkler sırayla döner; seçtiğin renk kullanılmaz."),
        (LightEffect.Blink, "Yanıp sönme", "Seçtiğin renk yanar ve söner."),
        (LightEffect.Wave, "Dalga", "Işık ön fanlardan CPU soğutucuya, oradan ekran kartına akar."),
        (LightEffect.Music, "Müzik", "Bilgisayardan çıkan sesin seviyesine göre parlar. Ses kaydedilmez."),
    ];

    // On fan denetleyicisinin kendi efektleri. Kod: io mod bayti; hiz 0xA9 (kucuk = hizli).
    public static readonly (LightEffect id, string name, string desc, byte code)[] Hardware =
    [
        (LightEffect.HwWave, "ARGB dalga", "Işık fanların içinde döner (denetleyici efekti).", 0x03),
        (LightEffect.HwStarry, "Yıldızlı gece", "LED'ler rastgele parlayıp söner.", 0x04),
        (LightEffect.HwLightning, "Yıldırım", "Ani parlamalar.", 0x02),
        (LightEffect.HwRainbow, "Gökkuşağı", "Fanlarda dönen gökkuşağı; seçtiğin renk kullanılmaz.", 0x05),
        (LightEffect.HwShine, "Parlama", "Seçtiğin renk parlayıp söner.", 0x08),
        (LightEffect.HwBreathe, "Donanım nefesi", "Nefes efektini denetleyici kendisi çalıştırır.", 0x07),
        // Kod 01 kullanici gozlemiyle SILVER'daki Meteor'a esledi (21 Ekim 2026).
        (LightEffect.HwMeteor, "Meteor", "Kuyruklu bir ışık fanların içinde akar.", 0x01),
    ];

    public static bool IsHardware(LightEffect e) => e >= LightEffect.HwWave;
    public static byte HardwareCode(LightEffect e) => Hardware.First(x => x.id == e).code;

    // Hiz 1-10 -> 0xA9: 1 = 0xFF (en yavas), 10 = 0x01 (en hizli); SILVER kaydiricisi da dogrusal (orta 0x87).
    public static byte HardwareSpeed(int speed) => (byte)Math.Round(255 - (Math.Clamp(speed, 1, 10) - 1) * 254 / 9.0);

    // Onizleme donanim efektlerini en yakin yazilim efektiyle taklit eder.
    static LightEffect PreviewOf(LightEffect e) => e switch
    {
        LightEffect.HwWave or LightEffect.HwBreathe or LightEffect.HwMeteor => LightEffect.Breath,
        LightEffect.HwRainbow => LightEffect.Cycle,
        _ => LightEffect.Blink,
    };

    public static string Name(LightEffect e) => All.Concat(Hardware.Select(h => (h.id, h.name, h.desc))).First(x => x.id == e).name;

    // Tam bir turun suresi (saniye). Hiz 5 temel sure; her 3 adim iki kat hizli ya da yavas.
    public static double Period(LightEffect e, int speed)
    {
        double basis = e switch { LightEffect.Breath => 4, LightEffect.Cycle => 12, LightEffect.Blink => 1, LightEffect.Wave => 3, _ => 2 };
        return basis * Math.Pow(2, (5 - speed) / 3.0);
    }

    static double Offset(string device) => device switch { LightController.Fans => 0, LightController.Cpu => 1 / 3.0, _ => 2 / 3.0 };

    static double Frac(double x) => x - Math.Floor(x);

    // Yumusak nabiz 0-1-0. Kare alinarak karanlik ucta daha uzun kalir; LED dogrusal oldugu icin goz bunu esit hizda gorur.
    static double Pulse(double phase)
    {
        double k = 0.5 - 0.5 * Math.Cos(2 * Math.PI * phase);
        return k * k;
    }

    // still: efekti yok say, sabit rengi ver (cikista son kare icin). overlay: oyun kurali isigi, her seyin ustune biner.
    // Donanim efektinde donanima sabit renk gider (efekti denetleyici yapar); onizleme ise taklidini cizer.
    public static Light At(LightState s, string device, double t, bool still = false, DeviceLight? overlay = null, bool preview = false)
    {
        // Oyun kurali isigi her seyin ustune biner; rengi, efekti ve parlakligi kuralin kendisi.
        if (overlay is not null && s.On) return Shade(overlay, device, t, overlay.Brightness / 100.0, overlay.Effect, false);
        var d = s.For(device);
        var effect = still ? LightEffect.Static : d.Effect;
        bool hwPreview = IsHardware(effect) && preview;
        if (IsHardware(effect)) effect = preview ? PreviewOf(effect) : LightEffect.Static;
        return Shade(d, device, t, s.On ? d.Brightness / 100.0 : 0, effect, hwPreview);
    }

    static Light Shade(DeviceLight d, string device, double t, double level, LightEffect effect, bool hwPreview)
    {
        var (r, g, b) = d.Rgb;
        if (effect == LightEffect.Static || level == 0) return new(r, g, b, level);
        double phase = Frac(t / Period(effect, d.Speed));
        switch (effect)
        {
            case LightEffect.Breath:
                // Tek renk (donanim onizlemesi ya da iki renk ayni): kararip aydinlanan klasik nabiz.
                if (hwPreview || d.Color2 == d.Color) return new(r, g, b, level * Pulse(phase));
                // Renk 1 -> Renk 2 -> Renk 1; isik sonmez, yalnizca renk kayar.
                var (mr, mg, mb) = Mix(d.Rgb, d.Rgb2, 0.5 - 0.5 * Math.Cos(2 * Math.PI * phase));
                return new(mr, mg, mb, level);
            case LightEffect.Cycle:
                var (hr, hg, hb) = Hue(phase);
                return new(hr, hg, hb, level);
            case LightEffect.Blink:
                return new(r, g, b, phase < 0.5 ? level : 0);
            case LightEffect.Wave:
                return new(r, g, b, level * Pulse(Frac(phase - Offset(device))));
            case LightEffect.Music:
                // Sessizde hafif bir taban isik; kok egrisi kisik sesleri de gorunur kilar.
                return new(r, g, b, level * (0.06 + 0.94 * Math.Sqrt(AudioMeter.Level)));
            default:
                return new(r, g, b, level);
        }
    }

    // Iki rengi isik olarak (dogrusal) karistirir ve en guclu kanali tama ceker. sRGB baytlarini dogrudan
    // karistirmak ara tonu karartir (kirmizi+yesil ortasi koyu zeytin olur); boylece sari ve hep tam parlak.
    static (byte, byte, byte) Mix((byte r, byte g, byte b) a, (byte r, byte g, byte b) c, double k)
    {
        static double Lin(byte v) { double x = v / 255.0; return x <= 0.04045 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4); }
        static byte Enc(double l) { double x = l <= 0.0031308 ? l * 12.92 : 1.055 * Math.Pow(l, 1 / 2.4) - 0.055; return (byte)Math.Round(Math.Clamp(x, 0, 1) * 255); }
        double r = Lin(a.r) + (Lin(c.r) - Lin(a.r)) * k, g = Lin(a.g) + (Lin(c.g) - Lin(a.g)) * k, b = Lin(a.b) + (Lin(c.b) - Lin(a.b)) * k;
        double max = Math.Max(r, Math.Max(g, b));
        if (max > 0) { r /= max; g /= max; b /= max; }
        return (Enc(r), Enc(g), Enc(b));
    }

    // Tam doygun renk tekerlegi (HSV, S = V = 1).
    static (byte, byte, byte) Hue(double phase)
    {
        double h = phase * 6;
        int i = (int)h % 6;
        byte up = (byte)Math.Round((h - Math.Floor(h)) * 255), down = (byte)(255 - up);
        return i switch
        {
            0 => (255, up, 0), 1 => (down, 255, 0), 2 => (0, 255, up),
            3 => (0, down, 255), 4 => (up, 0, 255), _ => (255, 0, down),
        };
    }
}

enum DeviceHealth { Unknown, Ok, Off, Missing, Error }

sealed record DeviceStatus(DeviceHealth Health, string Text);

// Tek isik, uc cihaz. Donanim yazmalari tek bir arka plan isinde. Sabit renkte yalnizca degisince yazar;
// efektte ~30 kare/sn yazar. Ust uste gelen istekler birlesir (son durum kazanir).
sealed class LightController : IDisposable
{
    public const string Fans = "fans", Cpu = "cpu", Gpu = "gpu";
    static readonly string[] Devices = [Fans, Cpu, Gpu];
    const int FrameMs = 33, RetryMs = 2000;

    readonly Motherboard _board = new();
    readonly GraphicsCard _gpu = new();
    readonly object _gate = new();
    readonly AutoResetEvent _wake = new(false);
    readonly Dictionary<string, DeviceStatus> _reported = [];
    LightState _state = new();
    int _version;
    bool _stop;
    DeviceLight? _overlay;
    Thread? _thread;

    public event Action<string, DeviceStatus>? StatusChanged;

    // --no-hardware: arayuz denemeleri icin; hicbir cihaza yazilmaz.
    public static bool NoHardware { get; set; }

    public void Apply(LightState state)
    {
        if (NoHardware)
        {
            foreach (var id in Devices) StatusChanged?.Invoke(id, new(DeviceHealth.Unknown, "Donanım kapalı (--no-hardware)"));
            return;
        }
        lock (_gate)
        {
            if (_stop) return;
            _state = state.Clone();
            _version++;
            _thread ??= StartThread();
        }
        _wake.Set();
    }

    // Oyun kurali isigi baslayip bitince (null); ayarlari degistirmez, yalnizca kareleri ezer.
    public void SetOverlay(DeviceLight? light)
    {
        if (NoHardware) return;
        lock (_gate)
        {
            if (_stop || Same(_overlay, light)) return;
            _overlay = light?.Clone();
            _version++;
            _thread ??= StartThread();
        }
        _wake.Set();
    }

    static bool Same(DeviceLight? a, DeviceLight? b) => a is null || b is null ? a == b
        : a.Color == b.Color && a.Effect == b.Effect && a.Speed == b.Speed && a.Brightness == b.Brightness;

    Thread StartThread()
    {
        var t = new Thread(Run) { IsBackground = true, Name = "LedSync ışık" };
        t.Start();
        return t;
    }

    void Run()
    {
        var clock = Stopwatch.StartNew();
        int written = -1;
        LightState s;
        while (true)
        {
            int v;
            DeviceLight? overlay;
            lock (_gate)
            {
                s = _state; v = _version; overlay = _overlay;
                if (_stop) break;
            }
            if (overlay is null && !s.Moving(Devices))
            {
                bool failed = false;
                if (v != written) { failed = Frame(s, 0, still: true); written = v; }
                // Hata varsa (or. kilit baska programdaydi) birazdan yeniden dener; yoksa degisiklik bekler.
                if (failed) written = -1;
                _wake.WaitOne(failed ? RetryMs : Timeout.Infinite);
                continue;
            }
            long start = clock.ElapsedMilliseconds;
            Frame(s, start / 1000.0, overlay: overlay);
            written = v;
            int rest = FrameMs - (int)(clock.ElapsedMilliseconds - start);
            _wake.WaitOne(Math.Max(1, rest));
        }
        // Cikista efekt yarim bir karede (or. sonuk) donmasin: sabit renkte birak.
        if (written >= 0 && (s.Moving(Devices) || _overlay is not null)) Frame(s, 0, still: true);
        _board.Dispose();
    }

    // Bir kare yazar; bir cihaza yazilamadiysa true doner.
    bool Frame(LightState s, double t, bool still = false, DeviceLight? overlay = null)
    {
        var zones = new List<BoardZone>();
        foreach (var (id, zone) in new[] { (Fans, "io"), (Cpu, "h1") })
        {
            if (!s.IsEnabled(id)) { Report(id, new(DeviceHealth.Off, "Devre dışı")); continue; }
            // Anakartta parlaklik rengi olcekler.
            var l = Effects.At(s, id, t, still, overlay);
            byte Scale(byte c) => (byte)Math.Round(ToLinear(c) * l.Level);
            // Donanim efekti (yalnizca on fanlar, uyari yokken): mod kodu ve hiz denetleyiciye gider.
            var fx = s.For(id).Effect;
            // Cikista da korunur: denetleyici efekti kendi surdurur, donmus kare sorunu yok.
            bool hw = overlay is null && Effects.IsHardware(fx);
            zones.Add(new(id, zone, hw ? Effects.HardwareCode(fx) : null, hw ? Effects.HardwareSpeed(s.For(id).Speed) : null,
                Scale(l.R), Scale(l.G), Scale(l.B)));
        }
        foreach (var (id, status) in _board.Write(zones)) Report(id, status);

        if (s.IsEnabled(Gpu))
        {
            // Ekran kartinda parlaklik donanimdan (brightnessPct) uygulanir.
            var l = Effects.At(s, Gpu, t, still, overlay);
            Report(Gpu, _gpu.Write(ToLinear(l.R), ToLinear(l.G), ToLinear(l.B), (byte)Math.Round(l.Level * 100), force: still));
        }
        else Report(Gpu, new(DeviceHealth.Off, "Devre dışı"));

        // Ekran kartindaki tek seferlik hata arayuze yansimaz ama sabit renk yeniden denenmeli.
        return _gpu.Pending || _reported.Values.Any(v => v.Health == DeviceHealth.Error);
    }

    // Efektte her kare ayni durumu uretir; arayuze yalnizca degisiklik gider.
    void Report(string id, DeviceStatus status)
    {
        if (_reported.GetValueOrDefault(id) == status) return;
        _reported[id] = status;
        // Hatalar arayuzde gosterilmez; hata kaydina gider (durum degistiginde bir kez).
        if (status.Health is DeviceHealth.Error or DeviceHealth.Missing) Log.Write($"{id}: {status.Text}");
        StatusChanged?.Invoke(id, status);
    }

    // Secilen renk ekran icin (sRGB, gamma kodlu); LED PWM'i dogrusal. Donusturmeden yazilirsa
    // kucuk kanallar fazla parlak yanar ve renk solar (kirmizi pembeye kayar). IEC 61966-2-1 egrisi.
    static byte ToLinear(byte v)
    {
        double c = v / 255.0;
        double lin = c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        return (byte)Math.Round(lin * 255);
    }

    public void Dispose()
    {
        Thread? t;
        lock (_gate) { _stop = true; t = _thread; }
        _wake.Set();
        // Son sabit kareyi yazmasina izin ver; takilirsa uygulamanin kapanmasini bekletme.
        t?.Join(TimeSpan.FromSeconds(2));
    }
}

// Anakarta bir bolge yazmasi. Mode null: sabit renk; aksi halde denetleyici efekt kodu (ve varsa hiz).
readonly record struct BoardZone(string Id, string Zone, byte? Mode, byte? Speed, byte R, byte G, byte B);

// Anakart LED MCU'su: PawnIO + SmbusPIIX4, kimligi 0x14 olan Sinif A (port 0, 0x58). Kurallar ledtest/Core.cs'ten.
// Oturum acik kalir (modul bir kez yuklenir); SMBus kilidi yalnizca her yazma suresince tutulur.
sealed class Motherboard : IDisposable
{
    // Sabit renk ("Permanent") donanim kodlari: io sirali numara 0x09, h1 bit alani 0x01 (TEST_PLANI.md, test 9).
    static readonly Dictionary<string, byte> StaticMode = new() { ["io"] = 0x09, ["h1"] = 0x01 };
    // RetryMs denetleyicinin yeniden deneme araligindan (2 sn) kisa: yeniden denendiginde bekleme bitmis olur.
    const int VerifyMs = 2000, RetryMs = 1500;

    readonly string _module = Path.Combine(AppContext.BaseDirectory, "modules", "SmbusPIIX4.bin");
    LedSession? _s;
    (int port, byte addr, ControllerClass cls)? _target;
    // Bu oturumda bolgeye yazilan son mod ve hiz; degismedikce yeniden yazilmaz.
    readonly Dictionary<string, byte> _mode = [], _speed = [];
    readonly Dictionary<string, (byte, byte, byte)> _lastColor = [];
    long _verifiedAt = long.MinValue, _retryAt;
    DeviceStatus? _failure;

    public IEnumerable<(string id, DeviceStatus status)> Write(List<BoardZone> zones)
    {
        if (zones.Count == 0) return [];
        long now = Environment.TickCount64;
        // Hatadan sonra her karede yeniden denemek kilidi ve sureci zorlar; kisa bir sure ayni hatayi bildir.
        if (_failure is { } f && now < _retryAt) return Fail(zones, f.Health, f.Text);
        try
        {
            if (!File.Exists(_module)) return Failed(zones, DeviceHealth.Missing, "SmbusPIIX4.bin bulunamadı", now);
            _s ??= LedSession.OpenShared(_module);
            _s.Acquire(TimeSpan.FromSeconds(1));
            try
            {
                _target ??= Find(_s);
                if (_target is not { } t) return Failed(zones, DeviceHealth.Missing, "LED kontrolcüsü bulunamadı", now);
                if (!_s.TrySelectPort(t.port, out string? err)) throw new LedTestException(err!);
                if (now - _verifiedAt > VerifyMs)
                {
                    var (hr, id) = _s.ReadByteData(t.addr, Known.RegId);
                    if (hr != 0 || id != t.cls.ExpectedId) { Reset(); return Failed(zones, DeviceHealth.Error, "Kimlik doğrulanamadı", now); }
                    _verifiedAt = now;
                }

                foreach (var (_, zoneName, fx, speed, r, g, b) in zones)
                {
                    var zone = Known.ZonesFor(t.cls).First(z => z.Name == zoneName);
                    byte mode = (byte)((fx ?? StaticMode[zoneName]) | 0x80);
                    if (_mode.GetValueOrDefault(zoneName) != mode)
                    {
                        Known.EnsureModeAllowed(zone, mode);
                        Check(_s.WriteByteData(t.addr, zone.Mode, mode), zone.Mode);
                        _mode[zoneName] = mode;
                        _lastColor.Remove(zoneName);
                    }
                    // Hiz yalnizca io'da var (0xA9); sabit renkte dokunulmaz.
                    if (speed is byte sp && zone.Speed is byte spReg && _speed.GetValueOrDefault(zoneName) != sp)
                    {
                        Check(_s.WriteByteData(t.addr, spReg, sp), spReg);
                        _speed[zoneName] = sp;
                    }
                    // Degismeyen rengi yeniden yazma (yanip sonmede karelerin cogu ayni).
                    if (_lastColor.TryGetValue(zoneName, out var last) && last == (r, g, b)) continue;
                    Check(_s.WriteByteData(t.addr, zone.Color, r), zone.Color);
                    Check(_s.WriteByteData(t.addr, (byte)(zone.Color + 1), g), (byte)(zone.Color + 1));
                    Check(_s.WriteByteData(t.addr, (byte)(zone.Color + 2), b), (byte)(zone.Color + 2));
                    _lastColor[zoneName] = (r, g, b);
                }
            }
            finally
            {
                _s?.Release();
            }
            _failure = null;
            return zones.Select(z => (z.Id, new DeviceStatus(DeviceHealth.Ok, "Bağlı")));
        }
        catch (Exception ex) when (ex is LedTestException or IOException or UnauthorizedAccessException or DllNotFoundException)
        {
            Reset();
            return Failed(zones, DeviceHealth.Error, ex is UnauthorizedAccessException ? "Yönetici yetkisi gerekiyor" : ex.Message, now);
        }
    }

    IEnumerable<(string, DeviceStatus)> Failed(List<BoardZone> zones, DeviceHealth h, string text, long now)
    {
        _failure = new(h, text);
        _retryAt = now + RetryMs;
        return Fail(zones, h, text);
    }

    static IEnumerable<(string, DeviceStatus)> Fail(List<BoardZone> zones, DeviceHealth h, string text)
        => zones.Select(z => (z.Id, new DeviceStatus(h, text)));

    // Oturumu kapatir; sonraki yazma kontrolcuyu yeniden arar ve modlari yeniden yazar.
    void Reset()
    {
        _mode.Clear();
        _speed.Clear();
        _lastColor.Clear();
        _target = null;
        _verifiedAt = long.MinValue;
        try { _s?.Dispose(); } catch (LedTestException) { }
        _s = null;
    }

    static void Check(int hr, byte reg)
    {
        if (hr != 0) throw new LedTestException($"Yazma {Fmt.Hex(reg)} başarısız: {Status.Describe(hr)}");
    }

    // Yalnizca statik analizde bulunan iki port ve 0x58; yalnizca kimligi 0x14 olan kontrolcu kabul edilir.
    static (int, byte, ControllerClass)? Find(LedSession s)
    {
        var cls = Known.Classes.First(c => c.ExpectedId == 0x14);
        foreach (int port in Known.Ports)
        {
            if (!s.TrySelectPort(port, out _)) continue;
            var (hr, id) = s.ReadByteData(cls.Address, Known.RegId);
            if (hr == 0 && id == cls.ExpectedId) return (port, cls.Address, cls);
        }
        return null;
    }

    public void Dispose() => Reset();
}

// Ekran karti: NVIDIA NvAPI Illumination, yalnizca dogrulanmis kart (NvGpu.VerifiedGpus), yalnizca on LED (bolge 0).
sealed class GraphicsCard
{
    // Kartin LED denetleyicisi surucu uzerinden yavas bir hatla beslenir; efektte daha sik yazmak ara sira
    // NVAPI_NOT_SUPPORTED dondurur. Arka arkaya bu kadar hata olmadikca arayuze hata bildirilmez.
    const int MinIntervalMs = 50, FailuresToReport = 3;

    NvGpu? _nv;
    GpuInfo? _gpu;
    byte[]? _buf;
    (byte, byte, byte, byte)? _last;
    long _lastWrite;
    int _failures;

    // Son yazma basarisiz: deger kartta degil, yeniden denenmeli.
    public bool Pending => _failures > 0;

    // force: sabit renk ve cikis yazmalari; hiz sinirina takilmaz (yoksa son deger kaybolur).
    public DeviceStatus Write(byte r, byte g, byte b, byte brightness, bool force)
    {
        long now = Environment.TickCount64;
        try
        {
            _nv ??= NvGpu.Open();
            _gpu ??= _nv.Gpus().FirstOrDefault(NvGpu.IsVerified);
            if (_gpu is null) return new(DeviceHealth.Missing, "Doğrulanmış kart bulunamadı");
            if (_last == (r, g, b, brightness)) return new(DeviceHealth.Ok, "Bağlı");
            if (!force && now - _lastWrite < MinIntervalMs) return new(DeviceHealth.Ok, "Bağlı");
            // Tampon bir kez okunur; her karede yalnizca bolge 0'in rengi degisir.
            _buf ??= _nv.ReadControlBuffer(_gpu);
            _nv.WriteControlBuffer(_gpu, NvGpu.WithManual(_buf, 0, r, g, b, 0, brightness));
            _last = (r, g, b, brightness);
            _lastWrite = now;
            _failures = 0;
            return new(DeviceHealth.Ok, "Bağlı");
        }
        catch (LedTestException ex)
        {
            Log.Write($"gpu hata #{_failures + 1}: {ex.Message} | renk {r},{g},{b} parlaklik {brightness} | onceki yazmadan {now - _lastWrite} ms | force {force}");
            _gpu = null; _buf = null; _last = null;
            return ++_failures >= FailuresToReport ? new(DeviceHealth.Error, ex.Message) : new(DeviceHealth.Ok, "Bağlı");
        }
    }
}

// Hata kaydi: %AppData%\LedSync\rlc-hata.log. Arayuzde hata gosterilmez, hepsi buraya yazilir; 256 KB'yi asinca bastan baslar.
static class Log
{
    static readonly string FilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LedSync", "rlc-hata.log");
    static readonly object Gate = new();

    public static void Write(string line)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 256 * 1024) File.Delete(FilePath);
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {line}{Environment.NewLine}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}

// Ayni cihazlara yazan diger RGB programlari. Calisiyorlarsa ayarlarimizi ezebilirler (TEST_PLANI.md, test 10).
static class Conflicts
{
    static readonly (string process, string name)[] Known =
    [
        ("OpenRGB", "OpenRGB"), ("ThPanel", "ThunderMaster"), ("SVLT", "BIOSTAR SILVER"), ("VividLEDDJ", "VIVID LED DJ"),
    ];

    public static List<string> Running()
        => Known.Where(k => System.Diagnostics.Process.GetProcessesByName(k.process).Length > 0).Select(k => k.name).ToList();
}
