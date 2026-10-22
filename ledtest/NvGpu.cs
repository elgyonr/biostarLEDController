using System.Runtime.InteropServices;
using System.Text;

// NVIDIA NvAPI Illumination: ekran karti LED'leri icin NVIDIA'nin resmi API'si.
// nvapi64.dll NVIDIA surucusuyle gelir; ham I2C'ye dokunulmaz, donanimla surucu konusur.
// Fonksiyon kimlikleri ve yapilar NVIDIA/nvapi (MIT) nvapi_interface.h / nvapi.h ile birebir.
// nvapi.h #pragma pack(8); buradaki alanlar en fazla 4 bayt hizalama ister -> dogal hizalama.

record GpuInfo(int Index, IntPtr Handle, string Name, uint DeviceId, uint SubSystemId);
// Data: cihaz turune gore birlesim; MCU'da [0]=i2cDevIdx, GPIO PWM RGBW'de [0..3]=R/G/B/W pinleri, tek renkte [0]=pin.
record IllumDevice(int Index, uint Type, uint CtrlModeMask, byte[] Data);
record IllumZone(int Index, uint Type, byte DeviceIdx, byte ProvIdx, uint Location);
record IllumZoneControl(int Index, uint Type, uint Mode, byte[] Data);
// Buffer: ClientIllumZonesGetControl'un dondurdugu yapinin tamami (hex); geri yukleme bunu aynen yazar.
record GpuBackup(uint DeviceId, uint SubSystemId, int Zones, string Buffer);

sealed class NvGpu
{
    const uint IdInitialize = 0x0150e828;
    const uint IdEnumPhysicalGpus = 0xe5ac921f;
    const uint IdGetFullName = 0xceee8e9f;
    const uint IdGetPciIdentifiers = 0x2ddfb66e;
    const uint IdIllumDevicesGetInfo = 0xd4100e58;
    const uint IdIllumZonesGetInfo = 0x4b81241b;
    const uint IdIllumZonesGetControl = 0x3dbf5764;
    const uint IdIllumZonesSetControl = 0x197d065e;

    // Yazma yalnizca okunarak dogrulanmis kartlarda yapilir (TEST_PLANI.md, test 10).
    public static readonly (uint Device, uint SubSystem, string Name)[] VerifiedGpus =
    [
        (0x288210DE, 0xF3111569, "Palit GeForce RTX 4060 Dual"),
    ];

    public static bool IsVerified(GpuInfo gpu) => VerifiedGpus.Any(v => v.Device == gpu.DeviceId && v.SubSystem == gpu.SubSystemId);

    // version(4) + sayi(4) + rsvd[64] = 72 bayt baslik; ardindan 32 eleman.
    const int DeviceInfoSize = 136;   // type(4) + ctrlModeMask(4) + data[64] + rsvd[64]
    const int ZoneInfoSize = 140;     // type(4) + devIdx(1) + provIdx(1) + pad(2) + location(4) + data[64] + rsvd[64]
    const int ZoneControlSize = 200;  // type(4) + ctrlMode(4) + data[128] + rsvd[64]
    const int MaxItems = 32;
    const int InfoHeader = 72;
    const int ControlHeader = 76;     // version(4) + bDefault bit alani(4) + sayi(4) + rsvd[64]

    public const uint ZoneTypeRgb = 1, ZoneTypeColorFixed = 2, ZoneTypeRgbw = 3, ZoneTypeSingleColor = 4;
    public const uint ModeManual = 0, ModePiecewiseLinear = 1;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int InitializeFn();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int EnumFn([Out] IntPtr[] handles, out uint count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FullNameFn(IntPtr gpu, [Out] byte[] name);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int PciFn(IntPtr gpu, out uint deviceId, out uint subSystemId, out uint revision, out uint extDeviceId);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int ParamsFn(IntPtr gpu, [In, Out] byte[] parameters);

    [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
    static extern IntPtr QueryInterface(uint id);

    static T Fn<T>(uint id) where T : Delegate
    {
        IntPtr p = QueryInterface(id);
        if (p == IntPtr.Zero) throw new LedTestException($"NvAPI fonksiyonu bulunamadi: 0x{id:X8} (surucu bu ozelligi sunmuyor olabilir)");
        return Marshal.GetDelegateForFunctionPointer<T>(p);
    }

    static uint Version(int size) => (uint)size | (1u << 16); // MAKE_NVAPI_VERSION(struct, 1)

    static void Check(int status, string what)
    {
        if (status != 0) throw new LedTestException($"{what} basarisiz: {Describe(status)}");
    }

    public static string Describe(int status) => status switch
    {
        0 => "NVAPI_OK",
        -1 => "-1 NVAPI_ERROR",
        -5 => "-5 NVAPI_INVALID_ARGUMENT",
        -6 => "-6 NVAPI_NVIDIA_DEVICE_NOT_FOUND",
        -9 => "-9 NVAPI_INCOMPATIBLE_STRUCT_VERSION (yapi boyutu/surumu uyusmuyor)",
        -104 => "-104 NVAPI_NOT_SUPPORTED",
        _ => $"{status}"
    };

    public static NvGpu Open()
    {
        try
        {
            Check(Fn<InitializeFn>(IdInitialize)(), "NvAPI_Initialize");
        }
        catch (DllNotFoundException)
        {
            throw new LedTestException("nvapi64.dll yuklenemedi; NVIDIA surucusu kurulu degil mi?");
        }
        return new NvGpu();
    }

    public List<GpuInfo> Gpus()
    {
        var handles = new IntPtr[64];
        Check(Fn<EnumFn>(IdEnumPhysicalGpus)(handles, out uint count), "NvAPI_EnumPhysicalGPUs");
        var fullName = Fn<FullNameFn>(IdGetFullName);
        var pci = Fn<PciFn>(IdGetPciIdentifiers);
        var list = new List<GpuInfo>();
        for (int i = 0; i < count; i++)
        {
            var name = new byte[64];
            fullName(handles[i], name);
            pci(handles[i], out uint dev, out uint sub, out _, out _);
            list.Add(new GpuInfo(i, handles[i], Encoding.ASCII.GetString(name).TrimEnd('\0'), dev, sub));
        }
        return list;
    }

    public List<IllumDevice> Devices(GpuInfo gpu)
    {
        var buf = NewParams(InfoHeader + MaxItems * DeviceInfoSize);
        Check(Fn<ParamsFn>(IdIllumDevicesGetInfo)(gpu.Handle, buf), "ClientIllumDevicesGetInfo");
        int n = Math.Min((int)BitConverter.ToUInt32(buf, 4), MaxItems);
        var list = new List<IllumDevice>();
        for (int i = 0; i < n; i++)
        {
            int o = InfoHeader + i * DeviceInfoSize;
            list.Add(new IllumDevice(i, BitConverter.ToUInt32(buf, o), BitConverter.ToUInt32(buf, o + 4), buf.AsSpan(o + 8, 4).ToArray()));
        }
        return list;
    }

    public List<IllumZone> Zones(GpuInfo gpu)
    {
        var buf = NewParams(InfoHeader + MaxItems * ZoneInfoSize);
        Check(Fn<ParamsFn>(IdIllumZonesGetInfo)(gpu.Handle, buf), "ClientIllumZonesGetInfo");
        int n = Math.Min((int)BitConverter.ToUInt32(buf, 4), MaxItems);
        var list = new List<IllumZone>();
        for (int i = 0; i < n; i++)
        {
            int o = InfoHeader + i * ZoneInfoSize;
            list.Add(new IllumZone(i, BitConverter.ToUInt32(buf, o), buf[o + 4], buf[o + 5], BitConverter.ToUInt32(buf, o + 8)));
        }
        return list;
    }

    public List<IllumZoneControl> ZoneControls(GpuInfo gpu) => ParseControls(ReadControlBuffer(gpu));

    // Kontrol yapisinin tamami; yedek ve yazma bu tampon uzerinden yapilir.
    // bDefault = 0: varsayilan degil, su anki ayar okunur.
    public byte[] ReadControlBuffer(GpuInfo gpu)
    {
        var buf = NewParams(ControlHeader + MaxItems * ZoneControlSize);
        Check(Fn<ParamsFn>(IdIllumZonesGetControl)(gpu.Handle, buf), "ClientIllumZonesGetControl");
        return buf;
    }

    public void WriteControlBuffer(GpuInfo gpu, byte[] buf)
    {
        if (!IsVerified(gpu))
            throw new LedTestException($"Bu kart (0x{gpu.DeviceId:X8} / 0x{gpu.SubSystemId:X8}) dogrulanmis listede degil; yazma reddedildi.");
        if (buf.Length != ControlHeader + MaxItems * ZoneControlSize || BitConverter.ToUInt32(buf, 0) != Version(buf.Length))
            throw new LedTestException("Kontrol tamponunun boyutu ya da surumu beklenen degil; yazma reddedildi.");
        Check(Fn<ParamsFn>(IdIllumZonesSetControl)(gpu.Handle, buf), "ClientIllumZonesSetControl");
    }

    public static int ZoneCount(byte[] controlBuffer) => Math.Min((int)BitConverter.ToUInt32(controlBuffer, 8), MaxItems);

    public static List<IllumZoneControl> ParseControls(byte[] buf)
    {
        var list = new List<IllumZoneControl>();
        for (int i = 0; i < ZoneCount(buf); i++)
        {
            int o = ControlHeader + i * ZoneControlSize;
            list.Add(new IllumZoneControl(i, BitConverter.ToUInt32(buf, o), BitConverter.ToUInt32(buf, o + 4), buf.AsSpan(o + 8, 40).ToArray()));
        }
        return list;
    }

    // Tamponun kopyasinda tek bolgeyi MANUAL moda ve verilen renk/parlakliga ayarlar; diger her bayt surucunun degeri kalir.
    // Renk yalnizca RGB/RGBW bolgelerde kullanilir; tek renk/sabit renk bolgelerde yalnizca parlaklik yazilir.
    public static byte[] WithManual(byte[] buf, int zone, byte r, byte g, byte b, byte white, byte brightnessPct)
    {
        if (zone < 0 || zone >= ZoneCount(buf)) throw new LedTestException($"Bolge {zone} yok (kartta {ZoneCount(buf)} bolge var).");
        if (brightnessPct > 100) throw new LedTestException("Parlaklik 0-100 arasinda olmali.");
        var copy = (byte[])buf.Clone();
        int o = ControlHeader + zone * ZoneControlSize;
        uint type = BitConverter.ToUInt32(copy, o);
        BitConverter.TryWriteBytes(copy.AsSpan(o + 4, 4), ModeManual);
        int d = o + 8;
        switch (type)
        {
            case ZoneTypeRgb:
                copy[d] = r; copy[d + 1] = g; copy[d + 2] = b; copy[d + 3] = brightnessPct;
                break;
            case ZoneTypeRgbw:
                copy[d] = r; copy[d + 1] = g; copy[d + 2] = b; copy[d + 3] = white; copy[d + 4] = brightnessPct;
                break;
            case ZoneTypeColorFixed or ZoneTypeSingleColor:
                copy[d] = brightnessPct;
                break;
            default:
                throw new LedTestException($"Bolge {zone} turu desteklenmiyor: {ZoneTypeName(type)}");
        }
        return copy;
    }

    static byte[] NewParams(int size)
    {
        var buf = new byte[size];
        BitConverter.TryWriteBytes(buf.AsSpan(0, 4), Version(size));
        return buf;
    }

    public static string ZoneTypeName(uint t) => t switch
    {
        ZoneTypeRgb => "RGB",
        ZoneTypeColorFixed => "sabit renk (yalnizca parlaklik)",
        ZoneTypeRgbw => "RGBW",
        ZoneTypeSingleColor => "tek renk (yalnizca parlaklik)",
        _ => $"gecersiz ({t})"
    };

    public static string LocationName(uint l) => l switch
    {
        0x00 => "GPU ust",
        0x08 => "GPU on",
        0x0C => "GPU arka",
        0x20 => "SLI ust",
        _ => $"0x{l:X}"
    };

    public static string DescribeDevice(IllumDevice d) => d.Type switch
    {
        1 => $"i2c cihaz {d.Data[0]}",
        2 => $"GPIO pinleri R{d.Data[0]} G{d.Data[1]} B{d.Data[2]} W{d.Data[3]}",
        3 => $"GPIO pini {d.Data[0]}",
        _ => Convert.ToHexString(d.Data)
    };

    // ctrlModeMask: bit n = NV_GPU_CLIENT_ILLUM_CTRL_MODE n destekleniyor.
    public static string DescribeModeMask(uint mask)
        => string.Join(", ", new[] { (ModeManual, "MANUAL"), (ModePiecewiseLinear, "PIECEWISE_LINEAR") }
            .Where(m => (mask & (1u << (int)m.Item1)) != 0).Select(m => m.Item2));

    public static string DeviceTypeName(uint t) => t switch
    {
        1 => "MCU v1.0",
        2 => "GPIO PWM RGBW",
        3 => "GPIO PWM tek renk",
        _ => $"gecersiz ({t})"
    };

    // Kontrol verisini bolge turu ve moda gore okunur metne cevirir (data, yapinin +8 ofsetinden baslar).
    public static string DescribeControl(IllumZoneControl c)
    {
        var d = c.Data;
        string mode = c.Mode switch { ModeManual => "MANUAL", ModePiecewiseLinear => "PIECEWISE_LINEAR", _ => $"mod {c.Mode}" };
        if (c.Mode == ModeManual)
        {
            return c.Type switch
            {
                ZoneTypeRgb => $"{mode} renk {d[0]:X2}{d[1]:X2}{d[2]:X2} parlaklik %{d[3]}",
                ZoneTypeRgbw => $"{mode} renk {d[0]:X2}{d[1]:X2}{d[2]:X2} beyaz {d[3]:X2} parlaklik %{d[4]}",
                ZoneTypeColorFixed or ZoneTypeSingleColor => $"{mode} parlaklik %{d[0]}",
                _ => mode
            };
        }
        if (c.Mode == ModePiecewiseLinear && c.Type == ZoneTypeRgb)
        {
            // 2 uc nokta x 4 bayt, ardindan piecewise verisi (+8): cycleType(4) grpCount(1) pad(1) 6 x u16
            int p = 8;
            return $"{mode} A {d[0]:X2}{d[1]:X2}{d[2]:X2} %{d[3]}  B {d[4]:X2}{d[5]:X2}{d[6]:X2} %{d[7]}  "
                 + $"dongu {BitConverter.ToUInt32(d, p)} grup {d[p + 4]} yukselis {BitConverter.ToUInt16(d, p + 6)}ms "
                 + $"dusus {BitConverter.ToUInt16(d, p + 8)}ms A {BitConverter.ToUInt16(d, p + 10)}ms B {BitConverter.ToUInt16(d, p + 12)}ms "
                 + $"bekleme {BitConverter.ToUInt16(d, p + 14)}ms faz {BitConverter.ToUInt16(d, p + 16)}ms";
        }
        return $"{mode} ham {Convert.ToHexString(d, 0, 16)}";
    }
}
