using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;

// B650M-SILVER anakart LED testi.
// Yol: PawnIO (imzali surucu) + resmi imzali SmbusPIIX4 modulu -> AMD FCH SMBus -> LED MCU.
// BIOSTAR'in BS_LED surucusu kullanilmaz.
//
// Komutlar:
//   scan                        Salt okuma: kontrolcu kimligi ve mevcut register degerleri.
//   set  ... [--yes]            Tek kontrolcuye renk yazar. --yes olmadan kuru calisma yapar.
//   restore --backup <dosya> [--yes]   Yedekten eski degerleri geri yazar.

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("Bu arac yalnizca Windows icindir.");
    return 2;
}

var opts = Args.Parse(args);
if (opts.Command is null || opts.Command == "help")
{
    Console.WriteLine("""
        Kullanim:
          LedTest.exe scan [--module <SmbusPIIX4.bin>] [--report <dosya.json>]
          LedTest.exe set --port <0|1> --addr <0x30|0x58> --zone <sys|h1|h2|io|sb>
                          --rgb <b0> <b1> <b2> [--power on|off] [--mode <hex>] [--yes]
          LedTest.exe restore --backup <dosya.json> [--yes]
          LedTest.exe gpu-scan [--report <dosya.json>]   (ekran karti, salt okuma, yonetici gerekmez)
          LedTest.exe gpu-set --zone <n> [--rgb <r> <g> <b>] [--white <w>] [--brightness <0-100>] [--yes]
          LedTest.exe gpu-restore --backup <dosya.json> [--yes]

        Ornek (once kuru calisma, sonra gercek yazma):
          LedTest.exe set --port 0 --addr 0x58 --zone io --rgb 00 00 FF --mode 09
          LedTest.exe set --port 0 --addr 0x58 --zone io --rgb 00 00 FF --mode 09 --yes

        Mod kodlari bolgeye gore farklidir: sys/h1/h2 cevrilmis kod (varsayilan 01),
        io/sb VIVID efekt numarasi (varsayilan 09). Taramadaki mevcut degeri kullanin.
        """);
    return 0;
}

// NvAPI SMBus'a dokunmaz ve yonetici yetkisi gerektirmez; PawnIO oturumundan once ayri calisir.
if (opts.Command is "gpu-scan" or "gpu-set" or "gpu-restore")
{
    try
    {
        return opts.Command switch
        {
            "gpu-set" => Commands.GpuSet(opts),
            "gpu-restore" => Commands.GpuRestore(opts),
            _ => Commands.GpuScan(opts.Get("--report") ?? "gpu-scan.json"),
        };
    }
    catch (LedTestException ex)
    {
        Console.Error.WriteLine($"HATA: {ex.Message}");
        return 1;
    }
}

using (var identity = WindowsIdentity.GetCurrent())
{
    if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
    {
        Console.Error.WriteLine("Yonetici yetkisi gerekiyor (PawnIO erisimi). PowerShell'i 'Yonetici olarak calistir' ile acin.");
        return 3;
    }
}

string modulePath = opts.Get("--module") ?? Path.Combine(AppContext.BaseDirectory, "modules", "SmbusPIIX4.bin");
if (!File.Exists(modulePath))
{
    Console.Error.WriteLine($"PawnIO modulu bulunamadi: {modulePath}");
    Console.Error.WriteLine("PawnIO.Modules resmi surumundeki SmbusPIIX4.bin dosyasini bu konuma koyun.");
    return 4;
}

try
{
    using var session = LedSession.Open(modulePath);
    return opts.Command switch
    {
        "scan" => Commands.Scan(session, opts.Get("--report") ?? "led-scan.json"),
        "set" => Commands.Set(session, opts),
        "restore" => Commands.Restore(session, opts),
        _ => Fail($"Bilinmeyen komut: {opts.Command}")
    };
}
catch (LedTestException ex)
{
    Console.Error.WriteLine($"HATA: {ex.Message}");
    return 1;
}

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 2;
}

static class Commands
{
    public static int Scan(LedSession s, string reportPath)
    {
        var report = new Dictionary<string, object?>
        {
            ["capturedAt"] = DateTimeOffset.Now,
            ["controller"] = s.Identity(),
            ["memoryTypes"] = s.MemoryTypes,
        };
        var findings = new List<object>();
        foreach (int port in Known.Ports)
        {
            if (!s.TrySelectPort(port, out string? portError))
            {
                findings.Add(new { port, error = portError });
                continue;
            }
            foreach (byte addr in Known.Addresses)
            {
                if (addr == 0x30 && !s.IsDdr5)
                {
                    findings.Add(new { port, address = Hex(addr), skipped = "DDR5 dogrulanmadi; DDR4 SPD yazma korumasi riski (0x30-0x37)." });
                    continue;
                }
                var (hr, id) = s.ReadByteData(addr, Known.RegId);
                var match = hr == 0 ? Known.Classes.FirstOrDefault(c => c.Address == addr && c.ExpectedId == id) : null;
                Console.WriteLine($"port {port}  adres {Hex(addr)}  reg 0x03 -> {(hr == 0 ? Hex(id) : Status.Describe(hr))}{(match is null ? "" : $"   ==> {match.Name}")}");
                object? state = null;
                if (match is not null)
                {
                    var raw = s.ReadStateRaw(addr, Known.StateRegisters(match));
                    Console.WriteLine($"         guc {Hex(Known.RegPower)}={Hex(raw[Known.RegPower])}");
                    foreach (var z in Known.ZonesFor(match))
                        Console.WriteLine($"         {z.Name,-3} ({z.ConfigKey,-7}) mod {Hex(z.Mode)}={Hex(raw[z.Mode])}  renk {Hex(raw[z.Color])} {Hex(raw[(byte)(z.Color + 1)])} {Hex(raw[(byte)(z.Color + 2)])}"
                            + (z.Speed is byte sp ? $"  hiz {Hex(sp)}={Hex(raw[sp])}" : ""));
                    state = raw.ToDictionary(kv => Hex(kv.Key), kv => Hex(kv.Value));
                }
                findings.Add(new { port, address = Hex(addr), hresult = Hex32(hr), id = hr == 0 ? Hex(id) : null, match = match?.Name, state });
            }
        }
        report["findings"] = findings;
        File.WriteAllText(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Rapor: {Path.GetFullPath(reportPath)}");
        return 0;
    }

    // Ekran karti aydinlatmasini NvAPI ile yalnizca okur: GPU'lar, aydinlatma cihazlari, bolgeler, su anki ayar.
    public static int GpuScan(string reportPath)
    {
        var nv = NvGpu.Open();
        var report = new List<object>();
        foreach (var gpu in nv.Gpus())
        {
            Console.WriteLine($"GPU {gpu.Index}: {gpu.Name}  aygit 0x{gpu.DeviceId:X8}  alt sistem 0x{gpu.SubSystemId:X8}");
            object? devices = null, zones = null, controls = null;
            string? error = null;
            try
            {
                var d = nv.Devices(gpu);
                foreach (var x in d) Console.WriteLine($"   cihaz {x.Index}: {NvGpu.DeviceTypeName(x.Type)}  {NvGpu.DescribeDevice(x)}  modlar: {NvGpu.DescribeModeMask(x.CtrlModeMask)}");
                var z = nv.Zones(gpu);
                foreach (var x in z) Console.WriteLine($"   bolge {x.Index}: {NvGpu.ZoneTypeName(x.Type)}  konum {NvGpu.LocationName(x.Location)}  cihaz {x.DeviceIdx}");
                var c = nv.ZoneControls(gpu);
                foreach (var x in c) Console.WriteLine($"   bolge {x.Index} su an: {NvGpu.DescribeControl(x)}");
                devices = d.Select(x => new { x.Index, type = NvGpu.DeviceTypeName(x.Type), modeMask = $"0x{x.CtrlModeMask:X}", modes = NvGpu.DescribeModeMask(x.CtrlModeMask), detail = NvGpu.DescribeDevice(x) });
                zones = z.Select(x => new { x.Index, type = NvGpu.ZoneTypeName(x.Type), location = NvGpu.LocationName(x.Location), x.DeviceIdx, x.ProvIdx });
                controls = c.Select(x => new { x.Index, x.Type, x.Mode, text = NvGpu.DescribeControl(x), data = Convert.ToHexString(x.Data) });
            }
            catch (LedTestException ex)
            {
                error = ex.Message;
                Console.WriteLine($"   {ex.Message}");
            }
            report.Add(new { gpu.Index, gpu.Name, deviceId = $"0x{gpu.DeviceId:X8}", subSystemId = $"0x{gpu.SubSystemId:X8}", devices, zones, controls, error });
        }
        File.WriteAllText(reportPath, JsonSerializer.Serialize(new { capturedAt = DateTimeOffset.Now, gpus = report }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Rapor: {Path.GetFullPath(reportPath)}");
        return 0;
    }

    public static int GpuSet(Args o)
    {
        int zone = int.Parse(o.Get("--zone") ?? throw new LedTestException("--zone gerekli (gpu-scan'deki bolge numarasi)."));
        var rgb = o.GetMany("--rgb", 3)?.Select(ParseByte).ToArray();
        byte white = o.Get("--white") is string w ? ParseByte(w) : (byte)0;
        byte brightness = o.Get("--brightness") is string br ? byte.Parse(br) : (byte)100;

        var nv = NvGpu.Open();
        var gpu = nv.Gpus().FirstOrDefault(NvGpu.IsVerified)
            ?? throw new LedTestException("Dogrulanmis bir ekran karti bulunamadi; yazma reddedildi.");
        byte[] before = nv.ReadControlBuffer(gpu);
        var current = NvGpu.ParseControls(before);
        if (zone < 0 || zone >= current.Count) throw new LedTestException($"Bolge {zone} yok (kartta {current.Count} bolge var).");
        if (current[zone].Type is NvGpu.ZoneTypeRgb or NvGpu.ZoneTypeRgbw && rgb is null)
            throw new LedTestException("Bu bolge renkli; --rgb gerekli.");
        rgb ??= [0, 0, 0];
        byte[] after = NvGpu.WithManual(before, zone, rgb[0], rgb[1], rgb[2], white, brightness);

        Console.WriteLine($"Hedef: {gpu.Name}, bolge {zone} ({NvGpu.ZoneTypeName(current[zone].Type)})");
        Console.WriteLine($"Onceki: {NvGpu.DescribeControl(current[zone])}");
        Console.WriteLine($"Yeni:   {NvGpu.DescribeControl(NvGpu.ParseControls(after)[zone])}");
        if (!o.Has("--yes"))
        {
            Console.WriteLine("Kuru calisma: hicbir sey yazilmadi. Gercek yazma icin --yes ekleyin.");
            return 0;
        }

        string backup = $"gpu-backup-z{zone}-{DateTime.Now:yyyyMMdd-HHmmss}.json";
        File.WriteAllText(backup, JsonSerializer.Serialize(new GpuBackup(gpu.DeviceId, gpu.SubSystemId, NvGpu.ZoneCount(before), Convert.ToHexString(before))));
        Console.WriteLine($"Yedek: {Path.GetFullPath(backup)}");

        nv.WriteControlBuffer(gpu, after);
        Console.WriteLine($"Sonraki: {NvGpu.DescribeControl(nv.ZoneControls(gpu)[zone])}");
        Console.WriteLine("Ekran kartina bakin ve gordugunuz rengi not edin.");
        return 0;
    }

    // Yedekteki kontrol yapisinin tamamini oldugu gibi geri yazar.
    public static int GpuRestore(Args o)
    {
        string path = o.Get("--backup") ?? throw new LedTestException("--backup gerekli.");
        var b = JsonSerializer.Deserialize<GpuBackup>(File.ReadAllText(path)) ?? throw new LedTestException("Yedek okunamadi.");
        var nv = NvGpu.Open();
        var gpu = nv.Gpus().FirstOrDefault(g => g.DeviceId == b.DeviceId && g.SubSystemId == b.SubSystemId)
            ?? throw new LedTestException("Yedekteki kart bu bilgisayarda bulunamadi; geri yukleme reddedildi.");
        byte[] saved = Convert.FromHexString(b.Buffer);
        if (NvGpu.ZoneCount(saved) != b.Zones || NvGpu.ZoneCount(nv.ReadControlBuffer(gpu)) != b.Zones)
            throw new LedTestException("Bolge sayisi yedektekiyle eslesmiyor; geri yukleme reddedildi.");
        foreach (var c in NvGpu.ParseControls(saved)) Console.WriteLine($"  bolge {c.Index} <- {NvGpu.DescribeControl(c)}");
        if (!o.Has("--yes"))
        {
            Console.WriteLine("Kuru calisma: hicbir sey yazilmadi. Gercek geri yukleme icin --yes ekleyin.");
            return 0;
        }
        nv.WriteControlBuffer(gpu, saved);
        foreach (var c in nv.ZoneControls(gpu)) Console.WriteLine($"  bolge {c.Index} simdi: {NvGpu.DescribeControl(c)}");
        return 0;
    }

    public static int Set(LedSession s, Args o)
    {
        int port = int.Parse(o.Get("--port") ?? throw new LedTestException("--port gerekli (0 veya 1)."));
        byte addr = ParseByte(o.Get("--addr") ?? throw new LedTestException("--addr gerekli."));
        var rgb = o.GetMany("--rgb", 3)?.Select(ParseByte).ToArray() ?? throw new LedTestException("--rgb uc bayt ister, ornek: --rgb FF 00 00");
        string? power = o.Get("--power");
        string? modeText = o.Get("--mode");
        string zoneName = o.Get("--zone") ?? throw new LedTestException("--zone gerekli: sys, h1, h2, io veya sb.");
        bool commit = o.Has("--yes");

        if (!Known.Ports.Contains(port)) throw new LedTestException("Yalnizca port 0 ve 1 destekleniyor.");
        if (!Known.Addresses.Contains(addr)) throw new LedTestException("Yalnizca statik analizde bulunan adresler (0x30, 0x58) kullanilabilir.");
        if (addr == 0x30 && !s.IsDdr5) throw new LedTestException("0x30 icin DDR5 dogrulanmadi; yazma reddedildi.");
        if (!s.TrySelectPort(port, out string? portError)) throw new LedTestException(portError!);

        var (hr, id) = s.ReadByteData(addr, Known.RegId);
        if (hr != 0) throw new LedTestException($"Kimlik okunamadi: {Status.Describe(hr)}");
        var cls = Known.Classes.FirstOrDefault(c => c.Address == addr && c.ExpectedId == id)
            ?? throw new LedTestException($"Port {port} adres {Hex(addr)} kimligi {Hex(id)}; bilinen bir LED kontrolcusuyle eslesmiyor. Yazma reddedildi.");
        if (power is not null && !cls.PowerVerified) throw new LedTestException($"{cls.Name} icin guc register'i dogrulanmadi.");
        if (modeText is not null && !cls.ModeVerified) throw new LedTestException($"{cls.Name} icin mod register'i dogrulanmadi.");
        var zone = Known.ZonesFor(cls).FirstOrDefault(z => z.Name == zoneName)
            ?? throw new LedTestException($"{cls.Name} icin '{zoneName}' bolgesi dogrulanmadi.");

        if (modeText is not null) Known.EnsureModeAllowed(zone, ParseByte(modeText));

        var regs = Known.StateRegisters(cls);
        var before = s.ReadStateRaw(addr, regs);
        var plan = new List<(byte reg, byte value)>();
        if (power is not null) plan.Add((Known.RegPower, (byte)(power == "on" ? before[Known.RegPower] | 1 : before[Known.RegPower] & ~1)));
        if (modeText is not null) plan.Add((zone.Mode, (byte)(ParseByte(modeText) | 0x80)));
        plan.Add((zone.Color, rgb[0]));
        plan.Add(((byte)(zone.Color + 1), rgb[1]));
        plan.Add(((byte)(zone.Color + 2), rgb[2]));

        Console.WriteLine($"Hedef: port {port}, adres {Hex(addr)}, {cls.Name} (kimlik {Hex(id)}), bolge {zone.Name} ({zone.ConfigKey})");
        Console.WriteLine($"Onceki: {Format(before)}");
        foreach (var (reg, value) in plan) Console.WriteLine($"  yaz {Hex(reg)} <- {Hex(value)}");
        if (!commit)
        {
            Console.WriteLine("Kuru calisma: hicbir sey yazilmadi. Gercek yazma icin --yes ekleyin.");
            return 0;
        }

        string backup = $"led-backup-p{port}-{addr:X2}-{DateTime.Now:yyyyMMdd-HHmmss}.json";
        File.WriteAllText(backup, JsonSerializer.Serialize(new Backup(port, addr, id, before.ToDictionary(kv => Hex(kv.Key), kv => kv.Value), plan.Select(p => Hex(p.reg)).ToArray())));
        Console.WriteLine($"Yedek: {Path.GetFullPath(backup)}");

        foreach (var (reg, value) in plan)
        {
            int w = s.WriteByteData(addr, reg, value);
            if (w != 0) throw new LedTestException($"Yazma {Hex(reg)} basarisiz: {Status.Describe(w)}. Geri almak icin: restore --backup {backup} --yes");
        }
        Console.WriteLine($"Sonraki: {Format(s.ReadStateRaw(addr, regs))}");
        Console.WriteLine("Fanlara/anakarta bakin ve gordugunuz rengi not edin.");
        return 0;
    }

    public static int Restore(LedSession s, Args o)
    {
        string path = o.Get("--backup") ?? throw new LedTestException("--backup gerekli.");
        var b = JsonSerializer.Deserialize<Backup>(File.ReadAllText(path)) ?? throw new LedTestException("Yedek okunamadi.");
        if (!s.TrySelectPort(b.Port, out string? portError)) throw new LedTestException(portError!);
        var (hr, id) = s.ReadByteData(b.Address, Known.RegId);
        if (hr != 0 || id != b.Id) throw new LedTestException("Kimlik yedektekiyle eslesmiyor; geri yukleme reddedildi.");
        // Yazilan register'lari ters sirayla eski degerlerine dondur.
        var regs = Enumerable.Reverse(b.Written).Select(ParseByte).ToArray();
        foreach (byte reg in regs) Console.WriteLine($"  yaz {Hex(reg)} <- {Hex(b.Registers[Hex(reg)])}");
        if (!o.Has("--yes"))
        {
            Console.WriteLine("Kuru calisma: hicbir sey yazilmadi. Gercek geri yukleme icin --yes ekleyin.");
            return 0;
        }
        foreach (byte reg in regs)
        {
            int w = s.WriteByteData(b.Address, reg, b.Registers[Hex(reg)]);
            if (w != 0) throw new LedTestException($"Yazma {Hex(reg)} basarisiz: {Status.Describe(w)}");
        }
        Console.WriteLine($"Geri yuklendi: {Format(s.ReadStateRaw(b.Address, regs))}");
        return 0;
    }

    static string Format(Dictionary<byte, byte> regs) => string.Join(" ", regs.Select(kv => $"{Hex(kv.Key)}={Hex(kv.Value)}"));
    public static string Hex(byte b) => $"0x{b:X2}";
    public static string Hex32(int v) => $"0x{v:X8}";
    static byte ParseByte(string s) => Convert.ToByte(s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? s[2..] : s, 16);
}

sealed class Args
{
    readonly List<string> _items;
    public string? Command { get; }

    Args(string? command, List<string> items) => (Command, _items) = (command, items);

    public static Args Parse(string[] args) => new(args.FirstOrDefault(), args.Skip(1).ToList());

    public bool Has(string flag) => _items.Contains(flag);

    public string? Get(string flag)
    {
        int i = _items.IndexOf(flag);
        return i >= 0 && i + 1 < _items.Count ? _items[i + 1] : null;
    }

    public string[]? GetMany(string flag, int count)
    {
        int i = _items.IndexOf(flag);
        return i >= 0 && i + count < _items.Count ? _items.GetRange(i + 1, count).ToArray() : null;
    }
}
