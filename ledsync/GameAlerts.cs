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
            return FromExe(Path.Combine(dir, "LeagueClient.exe"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
    }

    // CS2: Steam kutuphanesindeki cs2.exe.
    public static ImageSource? Cs2() => Cs2Setup.GameDir() is { } dir ? FromExe(Path.Combine(dir, "game", "bin", "win64", "cs2.exe")) : null;

    static ImageSource? FromExe(string exe)
    {
        try
        {
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

// Oyun izleyicisi: durum (can, olu, bayraklar) degisince ve bir kez olan olaylarda ("kill", "win", "lose") haber verir.
interface IGameMonitor : IDisposable
{
    // null: oyunda degil.
    event Action<GameState?>? StateChanged;
    event Action<string>? Fired;
}

// League of Legends: Riot'un oyun sirasinda actigi resmi yerel API (Live Client Data, 127.0.0.1:2999).
// Yalnizca okunur; oyunun surecine, bellegine ya da dosyalarina dokunulmaz. Oyun disinda baglanti reddedilir.
// Tek istek (allgamedata) can, olum, oldurme sayisi ve olay listesini birlikte verir.
sealed class LolMonitor : IGameMonitor
{
    const string Url = "https://127.0.0.1:2999/liveclientdata/allgamedata";
    readonly HttpClient _http;
    readonly CancellationTokenSource _cts = new();
    // Oldurme sayaci ve son gorulen olay; -1/false: henuz taban okunmadi (izleme ortasinda baslarsa eski olaylar yanmasin).
    int _kills = -1, _lastEvent = -1;
    bool _baseline;

    // null: oyunda degil (ya da izleyici/yukleme ekrani/TFT).
    public event Action<GameState?>? StateChanged;
    // Bir kez olan olaylar: "kill", "win", "lose".
    public event Action<string>? Fired;

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
        GameState? last = null;
        bool first = true;
        while (!_cts.IsCancellationRequested)
        {
            GameState? st = await Read();
            if (first || st != last) { first = false; last = st; StateChanged?.Invoke(st); }
            // Oyundayken saniyede 4 kez; oyun disinda 3 saniyede bir yoklar.
            try { await Task.Delay(st is null ? 3000 : 250, _cts.Token); }
            catch (TaskCanceledException) { return; }
        }
    }

    // Oyuncu kimligi: yeni istemcide riotId ("Ad#TAG"), eskisinde summonerName.
    static string? Name(JsonElement p)
        => p.TryGetProperty("riotId", out var r) && r.GetString() is { Length: > 0 } id ? id
         : p.TryGetProperty("summonerName", out var n) ? n.GetString() : null;

    async Task<GameState?> Read()
    {
        try
        {
            using var doc = JsonDocument.Parse(await _http.GetStringAsync(Url, _cts.Token));
            var root = doc.RootElement;
            // TFT ayni yerel API'yi acar ama alanlarin anlami (taktikci cani) dogrulanmadi; kurallar yalnizca normal macta.
            if (root.TryGetProperty("gameData", out var gd) && gd.TryGetProperty("gameMode", out var gm) && gm.GetString() == "TFT")
            {
                Reset();
                return null;
            }
            var ap = root.GetProperty("activePlayer");
            var stats = ap.GetProperty("championStats");
            double cur = stats.GetProperty("currentHealth").GetDouble(), max = stats.GetProperty("maxHealth").GetDouble();
            if (max <= 0) return null;

            string? me = Name(ap);
            JsonElement mine = default;
            if (me is not null && root.TryGetProperty("allPlayers", out var players))
                foreach (var p in players.EnumerateArray())
                    if (Name(p) == me) { mine = p; break; }
            bool found = mine.ValueKind == JsonValueKind.Object;
            bool dead = found && mine.TryGetProperty("isDead", out var d) ? d.GetBoolean() : cur <= 0;

            int kills = found && mine.TryGetProperty("scores", out var sc) && sc.TryGetProperty("kills", out var k) ? k.GetInt32() : -1;
            if (kills >= 0)
            {
                if (_kills >= 0 && kills > _kills) Fired?.Invoke("kill");
                _kills = kills;
            }

            if (root.TryGetProperty("events", out var ev) && ev.TryGetProperty("Events", out var list))
            {
                int newest = _lastEvent;
                foreach (var e in list.EnumerateArray())
                {
                    int id = e.GetProperty("EventID").GetInt32();
                    if (id <= _lastEvent) continue;
                    newest = Math.Max(newest, id);
                    // Ilk okumada yalnizca taban alinir: uygulama mac bittikten sonra acilirsa eski sonuc yanmaz.
                    if (_baseline && e.GetProperty("EventName").GetString() == "GameEnd" && e.TryGetProperty("Result", out var r))
                        Fired?.Invoke(r.GetString() == "Win" ? "win" : "lose");
                }
                _lastEvent = newest;
                _baseline = true;
            }
            return new GameState(Math.Clamp(cur / max, 0, 1), dead);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            // Mac disi: sonraki mac sayaclari sifirdan, taban yeniden alinarak baslar.
            Reset();
            return null;
        }
    }

    void Reset()
    {
        _kills = _lastEvent = -1;
        _baseline = false;
    }

    public void Dispose()
    {
        _cts.Cancel();
        _http.Dispose();
    }
}
