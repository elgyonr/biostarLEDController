using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LedSync;

// Oyun simgeleri kullanicinin kendi kurulumundaki programdan okunur; uygulamaya logo gomulmez.
static class GameIcons
{
    // Riot istemcisi kurulum yolunu bu dosyaya yazar; yoksa varsayilan klasore bakilir.
    const string LolSettings = @"C:\ProgramData\Riot Games\Metadata\league_of_legends.live\league_of_legends.live.product_settings.yaml";

    public static ImageSource? League()
    {
        try
        {
            string dir = @"C:\Riot Games\League of Legends";
            if (File.Exists(LolSettings))
                foreach (var line in File.ReadLines(LolSettings))
                    if (line.TrimStart().StartsWith("product_install_full_path:"))
                        dir = line[(line.IndexOf(':') + 1)..].Trim().Trim('"');
            string exe = Path.Combine(dir, "LeagueClient.exe");
            if (!File.Exists(exe)) return null;
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(exe);
            if (icon is null) return null;
            var src = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
    }
}

// League of Legends: Riot'un oyun sirasinda actigi resmi yerel API (Live Client Data, 127.0.0.1:2999).
// Yalnizca okunur; oyunun surecine, bellegine ya da dosyalarina dokunulmaz. Oyun disinda baglanti reddedilir.
sealed class LolMonitor : IDisposable
{
    const string Url = "https://127.0.0.1:2999/liveclientdata/activeplayer";
    readonly HttpClient _http;
    readonly CancellationTokenSource _cts = new();

    // null: oyunda degil (ya da izleyici/yukleme ekrani); aksi halde 0-1 can orani.
    public event Action<double?>? HealthChanged;

    public LolMonitor()
    {
        var handler = new HttpClientHandler
        {
            // Yerel sunucu Riot'un kendi kok sertifikasiyla imzali; yalnizca bu yerel adres icin kabul edilir.
            ServerCertificateCustomValidationCallback = (req, _, _, _) => req.RequestUri is { Host: "127.0.0.1", Port: 2999 },
        };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(800) };
        _ = Run();
    }

    async Task Run()
    {
        double? last = null;
        bool first = true;
        while (!_cts.IsCancellationRequested)
        {
            double? hp = await Read();
            if (first || hp != last) { first = false; last = hp; HealthChanged?.Invoke(hp); }
            // Oyundayken saniyede 4 kez; oyun disinda 3 saniyede bir yoklar.
            try { await Task.Delay(hp is null ? 3000 : 250, _cts.Token); }
            catch (TaskCanceledException) { return; }
        }
    }

    async Task<double?> Read()
    {
        try
        {
            using var doc = JsonDocument.Parse(await _http.GetStringAsync(Url, _cts.Token));
            var st = doc.RootElement.GetProperty("championStats");
            double cur = st.GetProperty("currentHealth").GetDouble(), max = st.GetProperty("maxHealth").GetDouble();
            return max > 0 ? Math.Clamp(cur / max, 0, 1) : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _http.Dispose();
    }
}
