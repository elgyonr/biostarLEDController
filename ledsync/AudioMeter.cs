using System.Runtime.InteropServices;

namespace LedSync;

// Varsayilan hoparlorun anlik tepe seviyesi (Windows ses ayarlarindaki seviye cubugunun degeri).
// Ses kaydedilmez; yalnizca IAudioMeterInformation okunur. Kullanilmayinca kendi isi durur.
static class AudioMeter
{
    static readonly object Gate = new();
    static Thread? _thread;
    static long _lastRead;
    static double _level;

    // 0-1, hizli yukselir yavas iner (yoksa isik her vurusta titrer).
    public static double Level
    {
        get
        {
            Interlocked.Exchange(ref _lastRead, Environment.TickCount64);
            lock (Gate)
            {
                if (_thread is null)
                {
                    _thread = new Thread(Run) { IsBackground = true, Name = "LedSync ses" };
                    _thread.SetApartmentState(ApartmentState.MTA);
                    _thread.Start();
                }
            }
            return Volatile.Read(ref _level);
        }
    }

    static void Run()
    {
        IAudioMeterInformation? meter = null;
        long acquiredAt = 0;
        double level = 0;
        // 3 saniye okunmazsa (muzik efekti kapandi) is biter; bir sonraki okuma yeniden baslatir.
        while (Environment.TickCount64 - Interlocked.Read(ref _lastRead) < 3000)
        {
            long now = Environment.TickCount64;
            // Kulaklik/hoparlor degisebilir: varsayilan cihaz 5 saniyede bir yeniden alinir.
            if (meter is null || now - acquiredAt > 5000)
            {
                meter = Acquire();
                acquiredAt = now;
            }
            float peak = 0;
            if (meter is null || meter.GetPeakValue(out peak) != 0) meter = null;
            double target = Math.Clamp(peak, 0, 1);
            level = target > level ? target : level * 0.88 + target * 0.12;
            Volatile.Write(ref _level, level);
            Thread.Sleep(16);
        }
        Volatile.Write(ref _level, 0);
        lock (Gate) _thread = null;
    }

    static IAudioMeterInformation? Acquire()
    {
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            if (enumerator.GetDefaultAudioEndpoint(0 /* eRender */, 1 /* eMultimedia */, out var device) != 0) return null;
            var iid = typeof(IAudioMeterInformation).GUID;
            return device.Activate(ref iid, 1 /* CLSCTX_INPROC_SERVER */, IntPtr.Zero, out var o) == 0 ? (IAudioMeterInformation)o : null;
        }
        catch (COMException) { return null; }
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumerator;

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
    }

    [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioMeterInformation
    {
        [PreserveSig] int GetPeakValue(out float peak);
    }
}
