using System.IO;

namespace LedSync;

// "Son canli" damgasi: zorla oldurulen surec kayda "kapaniyor" yazamaz. Uygulama dakikada bir
// %AppData%\LedSync\rlc-son.txt dosyasina zaman yazar; duzgun kapanista durumu "kapandi" yapar.
// Acilista onceki oturum hala "calisiyor" gorunuyorsa habersiz kapanmistir: hata kaydina olum araligi not edilir.
// deploy.ps1 zorla kapatmadan once durumu "deploy" yapar (o kapanis zaten kayitli).
static class Heartbeat
{
    static readonly string FilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LedSync", "rlc-son.txt");
    static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);
    static Timer? _timer;
    static DateTime _started;

    public static void Start()
    {
        CheckPrevious();
        _started = DateTime.Now;
        Write("calisiyor");
        _timer = new Timer(_ => Write("calisiyor"), null, Interval, Interval);
    }

    public static void Stop()
    {
        _timer?.Dispose();
        _timer = null;
        Write("kapandi");
    }

    static void CheckPrevious()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            var f = File.ReadAllLines(FilePath).Select(l => l.Split('=', 2)).Where(p => p.Length == 2).ToDictionary(p => p[0], p => p[1]);
            if (f.GetValueOrDefault("durum") != "calisiyor") return;
            Log.Write($"önceki oturum habersiz kapandı (PID {f.GetValueOrDefault("pid")}, başladı {f.GetValueOrDefault("basladi")}): " +
                      $"son canlı {f.GetValueOrDefault("son")}; kapanış bundan sonraki 1 dakika içinde. Çökme ya da kapanış satırı yoksa dışarıdan sonlandırıldı.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    static void Write(string state)
    {
        try
        {
            File.WriteAllText(FilePath,
                $"pid={Environment.ProcessId}\nbasladi={_started:yyyy-MM-dd HH:mm:ss}\nson={DateTime.Now:yyyy-MM-dd HH:mm:ss}\ndurum={state}\n");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
