using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

// LedTest ve LedGui'nin ortak cekirdegi: PawnIO + SmbusPIIX4 uzerinden LED MCU erisimi ve guvenlik kurallari.

// VIVID LED DJ 3.5.2311.2300 statik analizinden cikan kontrolcu siniflari.
// Adresler 7 bit; VIVID 8 bit bicim kullanir (0x60 -> 0x30, 0xB0 -> 0x58).
// Kimlik: register 0x03.
record ControllerClass(string Name, byte Address, byte ExpectedId, bool PowerVerified, bool ModeVerified, string Source);

static class Known
{
    public static readonly ControllerClass[] Classes =
    [
        new("Sinif A (LEDMCU_SMB / LED_IIC)", 0x58, 0x14, PowerVerified: true, ModeVerified: true, "ctor 0x42FB28, vtable 0x684944"),
        new("Sinif B (LEDMCU_SMB / LED_IIC)", 0x58, 0x13, PowerVerified: false, ModeVerified: false, "ctor 0x4333DC, vtable 0x68482C"),
        new("Sinif C (LEDMCU1_SMB / LED_IIC1)", 0x30, 0x12, PowerVerified: true, ModeVerified: false, "ctor 0x436A50, vtable 0x684714"),
    ];

    public static readonly byte[] Addresses = [0x30, 0x58];
    public static readonly int[] Ports = [0, 1]; // 0 = birincil 0xB00, 1 = aux 0xB20. 2-4 baska yazilimlari bozabilir; kullanilmiyor.

    public const byte RegId = 0x03;
    public const byte RegPower = 0x97;  // bit0 = acik/kapali (tum bolgeler icin ortak)

    // Kimligi 0x14 olan MCU'da her LED bolgesinin kendi blogu var. Renk: Color, Color+1, Color+2
    // (fiziksel R/G/B sirasi henuz gozlenmedi). Mod: efekt kodu | 0x80.
    // Kaynak: LED_SYS/H1/H2/IO/SB yoneticilerinin olusturdugu siniflar (kimlik 0x14).
    public static readonly Zone[] Zones =
    [
        new("sys", "LED_SYS", Color: 0x98, Mode: 0x9B, "vtable 0x684944; AMD profillerinde LED_SYS=0"),
        new("h1", "LED_H1", Color: 0x9C, Mode: 0x9F, "vtable 0x68490C; AMD profillerinde LED_H1=2"),
        new("h2", "LED_H2", Color: 0xA0, Mode: 0xA3, "vtable 0x6848D4; AMD profillerinde LED_H2=0"),
        // Hiz: vtable+0x28 (0x4329DC) degeri dogrudan 0xA9'a yazar; 0x5050 "degistirme" isareti.
        new("io", "LED_IO", Color: 0xA5, Mode: 0xA4, "vtable 0x68489C; AMD profillerinde LED_IO=4", Speed: 0xA9),
        new("sb", "LED_SB", Color: 0xAB, Mode: 0xAA, "vtable 0x684864; AMD profillerinde LED_SB=0"),
    ];

    // Bolge bloklari yalnizca Sinif A ailesinde (kimlik 0x14) dogrulandi; B ve C icin sadece sys.
    public static IEnumerable<Zone> ZonesFor(ControllerClass c) => c.ExpectedId == 0x14 ? Zones : Zones.Take(1);

    // Kullanici gozlemi (21 Ekim 2026): io'da alt yarim bayti 0xD olan her mod (0D, 1D, 2D, ...)
    // tum fanlari beyaz parlatip kisa devre gibi sonduruyor. Asiri akim korumasi olabilir;
    // sebep anlasilana kadar yazilmaz. io efekti alt 4 bitten okuyor gibi gorunuyor.
    public static bool IsModeBlocked(Zone zone, byte mode) => zone.Name == "io" && (mode & 0x0F) == 0x0D;

    public static void EnsureModeAllowed(Zone zone, byte mode)
    {
        if (IsModeBlocked(zone, mode))
            throw new LedTestException($"{zone.Name} bolgesinde mod {Fmt.Hex((byte)(mode & 0x7F))} engelli: alt yarim bayt 0xD fanlari beyaz parlatip sonduruyor (asiri akim korumasi olabilir).");
    }

    public static byte[] StateRegisters(ControllerClass c)
        => [RegPower, .. ZonesFor(c).SelectMany(z => new[] { z.Mode, z.Color, (byte)(z.Color + 1), (byte)(z.Color + 2) }.Concat(z.Speed is byte sp ? [sp] : Array.Empty<byte>()))];
}

// Speed: VIVID'in o bolge icin hiz yazdigi register; h1'in hiz metodu (0x431474) bos oldugu icin yalnizca io'da var.
record Zone(string Name, string ConfigKey, byte Color, byte Mode, string Source, byte? Speed = null);

record Backup(int Port, byte Address, byte Id, Dictionary<string, byte> Registers, string[] Written);

sealed class LedSession : IDisposable
{
    const int I2cSmbusWrite = 0, I2cSmbusRead = 1, I2cSmbusByteData = 2;

    readonly PawnIo _pawn;
    readonly Mutex _smbusMutex;
    int _originalPort;
    bool _locked = true;

    public int[] MemoryTypes { get; }
    public bool IsDdr5 => MemoryTypes.Length > 0 && MemoryTypes.All(t => t is 0x22 or 0x23); // SMBIOS: DDR5 / LPDDR5

    LedSession(PawnIo pawn, Mutex mutex, int originalPort, int[] memoryTypes)
    {
        _pawn = pawn;
        _smbusMutex = mutex;
        _originalPort = originalPort;
        MemoryTypes = memoryTypes;
    }

    public static LedSession Open(string modulePath)
    {
        // SMBus'u kullanan diger yazilimlarla (LibreHardwareMonitor, OpenRGB vb.) ortak kilit.
        var mutex = new Mutex(false, @"Global\Access_SMBUS.HTP.Method");
        try
        {
            if (!mutex.WaitOne(TimeSpan.FromSeconds(5))) throw new LedTestException("SMBus kilidi 5 saniyede alinamadi; baska bir RGB/izleme yazilimini kapatin.");
        }
        catch (AbandonedMutexException) { }

        PawnIo pawn;
        try
        {
            pawn = PawnIo.Open();
        }
        catch
        {
            mutex.ReleaseMutex();
            mutex.Dispose();
            throw;
        }
        int hr = pawn.Load(File.ReadAllBytes(modulePath));
        if (hr != 0)
        {
            pawn.Dispose();
            mutex.ReleaseMutex();
            throw new LedTestException($"SmbusPIIX4 modulu yuklenemedi: {Status.Describe(hr)}");
        }
        var (phr, outp) = pawn.Execute("ioctl_piix4_port_sel", [unchecked((ulong)-1L)], 1);
        if (phr != 0)
        {
            pawn.Dispose();
            mutex.ReleaseMutex();
            throw new LedTestException($"Mevcut SMBus portu okunamadi: {Status.Describe(phr)}");
        }
        return new LedSession(pawn, mutex, (int)outp[0], Smbios.MemoryTypes());
    }

    // Uzun omurlu oturum (efektler icin): modul bir kez yuklenir, SMBus kilidi yalnizca Acquire/Release arasinda tutulur.
    public static LedSession OpenShared(string modulePath)
    {
        var s = Open(modulePath);
        s.Release();
        return s;
    }

    public void Acquire(TimeSpan timeout)
    {
        if (_locked) return;
        try
        {
            if (!_smbusMutex.WaitOne(timeout)) throw new LedTestException("SMBus kilidi alinamadi; baska bir RGB/izleme yazilimi hatti tutuyor.");
        }
        catch (AbandonedMutexException) { }
        _locked = true;
        var (hr, outp) = _pawn.Execute("ioctl_piix4_port_sel", [unchecked((ulong)-1L)], 1);
        if (hr != 0)
        {
            Release();
            throw new LedTestException($"Mevcut SMBus portu okunamadi: {Status.Describe(hr)}");
        }
        _originalPort = (int)outp[0];
    }

    public void Release()
    {
        if (!_locked) return;
        // Portu baslangictaki haline dondur; baska yazilimlar ilk porta guvenir.
        _pawn.Execute("ioctl_piix4_port_sel", [(ulong)_originalPort], 1);
        _locked = false;
        _smbusMutex.ReleaseMutex();
    }

    public object Identity()
    {
        var (hr, o) = _pawn.Execute("ioctl_identity", [], 3);
        if (hr != 0) return new { error = Status.Describe(hr) };
        string type = new string(BitConverter.GetBytes(o[0]).TakeWhile(b => b != 0).Select(b => (char)b).ToArray());
        return new { type, ioBase = $"0x{o[1]:X}", pciIds = $"0x{o[2]:X16}", originalPort = _originalPort };
    }

    public bool TrySelectPort(int port, out string? error)
    {
        var (hr, _) = _pawn.Execute("ioctl_piix4_port_sel", [(ulong)port], 1);
        error = hr == 0 ? null : $"Port {port} secilemedi: {Status.Describe(hr)}";
        return hr == 0;
    }

    public (int hr, byte value) ReadByteData(byte addr, byte reg)
    {
        var (hr, o) = _pawn.Execute("ioctl_smbus_xfer", [addr, I2cSmbusRead, reg, I2cSmbusByteData], 1);
        return (hr, hr == 0 ? (byte)o[0] : (byte)0);
    }

    public int WriteByteData(byte addr, byte reg, byte value)
        => _pawn.Execute("ioctl_smbus_xfer", [addr, I2cSmbusWrite, reg, I2cSmbusByteData, value], 0).hr;

    public Dictionary<byte, byte> ReadStateRaw(byte addr, IEnumerable<byte> registers)
    {
        var regs = new Dictionary<byte, byte>();
        foreach (byte reg in registers)
        {
            var (hr, v) = ReadByteData(addr, reg);
            if (hr != 0) throw new LedTestException($"Register {Fmt.Hex(reg)} okunamadi: {Status.Describe(hr)}");
            regs[reg] = v;
        }
        return regs;
    }

    public void Dispose()
    {
        Release();
        _pawn.Dispose();
        _smbusMutex.Dispose();
    }
}

sealed class PawnIo : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int OpenFn(out IntPtr handle);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int LoadFn(IntPtr handle, byte[] blob, UIntPtr size);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int ExecuteFn(IntPtr handle, [MarshalAs(UnmanagedType.LPStr)] string name,
        ulong[] input, UIntPtr inSize, ulong[] output, UIntPtr outSize, out UIntPtr returnSize);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int CloseFn(IntPtr handle);

    readonly IntPtr _library, _handle;
    readonly LoadFn _load;
    readonly ExecuteFn _execute;
    readonly CloseFn _close;

    PawnIo(IntPtr library, IntPtr handle, LoadFn load, ExecuteFn execute, CloseFn close)
        => (_library, _handle, _load, _execute, _close) = (library, handle, load, execute, close);

    public static PawnIo Open()
    {
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PawnIO", "PawnIOLib.dll");
        if (!File.Exists(path)) throw new LedTestException($"PawnIO kurulu degil: {path}");
        IntPtr lib = NativeLibrary.Load(path);
        T Fn<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(lib, name));
        var open = Fn<OpenFn>("pawnio_open");
        int hr = open(out IntPtr handle);
        if (hr != 0)
        {
            NativeLibrary.Free(lib);
            throw new LedTestException($"pawnio_open basarisiz: {Status.Describe(hr)}");
        }
        return new PawnIo(lib, handle, Fn<LoadFn>("pawnio_load"), Fn<ExecuteFn>("pawnio_execute"), Fn<CloseFn>("pawnio_close"));
    }

    public int Load(byte[] blob) => _load(_handle, blob, (UIntPtr)blob.Length);

    // PawnIO boyutlari bayt degil, 64 bit eleman sayisidir.
    public (int hr, ulong[] output) Execute(string name, ulong[] input, int outCount)
    {
        var output = new ulong[Math.Max(outCount, 1)];
        int hr = _execute(_handle, name, input, (UIntPtr)input.Length, output, (UIntPtr)outCount, out _);
        return (hr, output);
    }

    public void Dispose()
    {
        _close(_handle);
        NativeLibrary.Free(_library);
    }
}

static class Smbios
{
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern uint GetSystemFirmwareTable(uint provider, uint id, byte[]? buffer, uint size);

    // SMBIOS tip 17 (Memory Device) kayitlarindan dolu yuvalarin bellek tipi.
    public static int[] MemoryTypes()
    {
        const uint Rsmb = 0x52534D42;
        uint size = GetSystemFirmwareTable(Rsmb, 0, null, 0);
        if (size == 0) return [];
        var buf = new byte[size];
        GetSystemFirmwareTable(Rsmb, 0, buf, size);
        int length = BitConverter.ToInt32(buf, 4);
        var types = new List<int>();
        int p = 8, end = Math.Min(buf.Length, 8 + length);
        while (p + 4 <= end)
        {
            byte type = buf[p], len = buf[p + 1];
            if (type == 127 || len < 4) break;
            if (type == 17 && len > 0x12 && BitConverter.ToUInt16(buf, p + 0x0C) is not (0 or 0xFFFF))
                types.Add(buf[p + 0x12]);
            int q = p + len;
            while (q + 1 < end && !(buf[q] == 0 && buf[q + 1] == 0)) q++;
            p = q + 2;
        }
        return types.ToArray();
    }
}

static class Status
{
    // PawnIOLib NTSTATUS'u HRESULT_FROM_NT (0x10000000 biti) ile sarabilir; iki bicim de taninir.
    public static string Describe(int hr)
    {
        uint u = unchecked((uint)hr);
        uint nt = (u & 0x10000000) != 0 && (u >> 28) is 0x9 or 0xD ? u & ~0x10000000u : u;
        string text = nt switch
        {
            0x00000000 => "basarili",
            0x80070005 => "erisim engellendi",
            0xC000000E => "aygit cevap vermedi (adreste aygit yok)",
            0xC00000B5 => "SMBus zaman asimi",
            0xC0000185 => "SMBus aygit hatasi / carpisma",
            0xC00000BB => "desteklenmiyor (denetleyici uyumsuz)",
            0xC0000002 => "SMBus tabani varsayilan 0xB00 degil",
            0x80000011 => "SMBus mesgul",
            0xC000000D => "gecersiz parametre",
            // PawnIOLib NTSTATUS'u Win32 hatasina cevirip HRESULT olarak da dondurebiliyor.
            0x800701B1 => "aygit cevap vermedi (ERROR_NO_SUCH_DEVICE, adreste aygit yok)",
            0x800705B4 => "SMBus zaman asimi (ERROR_TIMEOUT)",
            0x8007045D => "SMBus aygit hatasi (ERROR_IO_DEVICE)",
            0x800700AA => "SMBus mesgul (ERROR_BUSY)",
            0x80070032 => "desteklenmiyor (ERROR_NOT_SUPPORTED)",
            _ => "bilinmeyen durum"
        };
        return $"0x{u:X8} {text}";
    }
}

sealed class LedTestException(string message) : Exception(message);

static class Fmt
{
    public static string Hex(byte b) => $"0x{b:X2}";
    public static string Hex32(int v) => $"0x{v:X8}";
}
