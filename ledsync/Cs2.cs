using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace LedSync;

// Counter-Strike 2: Valve'in resmi Game State Integration'i (GSI). Oyun, cfg klasorundeki
// gamestate_integration_*.cfg dosyasinda yazan adrese durum degistikce JSON POST eder. Biz yalnizca dinleriz;
// oyunun surecine, bellegine ya da dosyalarina (cfg disinda) dokunulmaz. SteelSeries ve Logitech de ayni yolu kullanir.
static class Cs2Setup
{
    const string FileName = "gamestate_integration_rlc.cfg";

    public enum Result { Ready, Installed, NotFound, Failed }

    // ...\steamapps\common\Counter-Strike Global Offensive; Steam kutuphaneleri libraryfolders.vdf'ten okunur.
    public static string? GameDir()
    {
        try
        {
            if (Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) is not string steam) return null;
            var libs = new List<string> { steam.Replace('/', '\\') };
            string vdf = Path.Combine(libs[0], "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf))
                foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                    libs.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
            foreach (var lib in libs.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string dir = Path.Combine(lib, "steamapps", "common", "Counter-Strike Global Offensive");
                if (Directory.Exists(Path.Combine(dir, "game", "csgo", "cfg"))) return dir;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        return null;
    }

    static string? CfgPath() => GameDir() is { } dir ? Path.Combine(dir, "game", "csgo", "cfg", FileName) : null;

    // Dosya yoksa ya da farkliysa yazar. Installed: CS2 aciksa yeniden baslatilmali (cfg yalnizca acilista okunur).
    public static Result Install(string token)
    {
        if (CfgPath() is not { } path) return Result.NotFound;
        try
        {
            string content = Cfg(token);
            if (File.Exists(path) && File.ReadAllText(path) == content) return Result.Ready;
            // Valve: dosya BOM'suz UTF-8 olmali.
            File.WriteAllText(path, content, new UTF8Encoding(false));
            return Result.Installed;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Write($"cs2: cfg yazilamadi: {ex.Message}");
            return Result.Failed;
        }
    }

    // CS2 kurali kapatilinca dosya kaldirilir; oyun bos yere istek gondermesin.
    public static void Remove()
    {
        try { if (CfgPath() is { } path && File.Exists(path)) File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Write($"cs2: cfg silinemedi: {ex.Message}"); }
    }

    // throttle: iki gonderim arasi en az 50 ms; heartbeat: degisiklik yokken 10 sn'de bir (oyunun kapandigini anlamak icin).
    static string Cfg(string token) => $$"""
        "RHZ LED Controller"
        {
          "uri"       "http://127.0.0.1:{{Cs2Monitor.Port}}/"
          "timeout"   "1.0"
          "buffer"    "0.0"
          "throttle"  "0.05"
          "heartbeat" "10.0"
          "auth"
          {
            "token" "{{token}}"
          }
          "data"
          {
            "provider"           "1"
            "map"                "1"
            "round"              "1"
            "player_id"          "1"
            "player_state"       "1"
            "player_match_stats" "1"
          }
        }

        """;
}

// CS2'nin GSI gonderimlerini yerel bir portta dinler ve can/olum/flash/bomba durumu ile oldurme ve mac sonucu
// olaylarina cevirir. Yalnizca 127.0.0.1; cfg'deki anahtari tasimayan istekler yok sayilir.
sealed class Cs2Monitor : IGameMonitor
{
    public const int Port = 3271;
    // Flash degeri 0-255; hafif bir yansima uyari sayilmaz.
    const int FlashThreshold = 50;
    // Heartbeat 10 sn; bu sure boyunca hic istek gelmezse oyun kapanmis sayilir.
    static readonly TimeSpan Stale = TimeSpan.FromSeconds(30);

    readonly string _token;
    readonly TcpListener _listener = new(IPAddress.Loopback, Port);
    readonly CancellationTokenSource _cts = new();
    readonly Timer _staleTimer;
    readonly object _gate = new();
    DateTime _lastPost = DateTime.MinValue;
    GameState? _last;
    bool _published;
    // Mac ici sayaclar; -1/null: henuz taban yok (izleme ortasinda baslarsa eski degerler olay sayilmaz).
    int _kills = -1;
    string? _myTeam, _mapPhase;

    public event Action<GameState?>? StateChanged;
    public event Action<string>? Fired;

    // Port acilamadiysa (baska program kullaniyor) false; ayrinti hata kaydinda.
    public bool Listening { get; }
    // Bu oturumda CS2'den en az bir gecerli istek geldi mi.
    public bool Connected { get; private set; }

    public Cs2Monitor(string token)
    {
        _token = token;
        try
        {
            _listener.Start();
            Listening = true;
            _ = Accept();
        }
        catch (SocketException ex) { Log.Write($"cs2: {Port} portu acilamadi: {ex.Message}"); }
        _staleTimer = new Timer(_ => CheckStale(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    async Task Accept()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(_cts.Token); }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException) { return; }
            _ = Handle(client);
        }
    }

    // Kucuk bir HTTP sunucusu: tek POST okur, 200 doner, baglantiyi kapatir. HttpListener yerine TCP:
    // URL ACL kaydi gerekmez ve yalnizca loopback'e baglanir.
    async Task Handle(TcpClient client)
    {
        using (client)
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                var stream = client.GetStream();
                var data = new MemoryStream();
                var buf = new byte[8192];
                int headerEnd = -1, length = 0;
                while (true)
                {
                    int n = await stream.ReadAsync(buf, timeout.Token);
                    if (n == 0) break;
                    data.Write(buf, 0, n);
                    if (data.Length > 1 << 20) return;
                    var bytes = data.GetBuffer();
                    if (headerEnd < 0 && (headerEnd = IndexOfHeaderEnd(bytes, (int)data.Length)) >= 0)
                    {
                        var m = Regex.Match(Encoding.ASCII.GetString(bytes, 0, headerEnd), @"Content-Length:\s*(\d+)", RegexOptions.IgnoreCase);
                        length = m.Success ? int.Parse(m.Groups[1].Value) : 0;
                    }
                    if (headerEnd >= 0 && data.Length >= headerEnd + 4 + length) break;
                }
                var ok = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(ok, timeout.Token);
                if (headerEnd >= 0 && length > 0) Process(Encoding.UTF8.GetString(data.GetBuffer(), headerEnd + 4, length));
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException or SocketException) { }
        }
    }

    static int IndexOfHeaderEnd(byte[] b, int len)
    {
        for (int i = 0; i + 3 < len; i++)
            if (b[i] == '\r' && b[i + 1] == '\n' && b[i + 2] == '\r' && b[i + 3] == '\n') return i;
        return -1;
    }

    static string? Str(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    static int? Int(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;
    static JsonElement Obj(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v : default;

    void Process(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (Str(Obj(root, "auth"), "token") != _token) return;
            lock (_gate)
            {
                _lastPost = DateTime.UtcNow;
                Connected = true;
                var map = Obj(root, "map");
                // Ana menu: harita yok.
                if (map.ValueKind != JsonValueKind.Object) { ResetMatch(); Publish(null); return; }

                string? me = Str(Obj(root, "provider"), "steamid");
                var player = Obj(root, "player");
                // Olunce GSI izlenen takim arkadasini gonderir; onun cani bizim canimiz sanilmamali.
                bool self = me is not null && Str(player, "steamid") == me;
                if (self && Str(player, "team") is { } team) _myTeam = team;
                var state = Obj(player, "state");
                int health = Int(state, "health") ?? 0;
                string? phase = Str(map, "phase");
                var round = Obj(root, "round");

                if (self && Int(Obj(player, "match_stats"), "kills") is int kills)
                {
                    if (_kills >= 0 && kills > _kills) Fired?.Invoke("kill");
                    _kills = kills;
                }

                // Mac sonu: "gameover"a gecis aninda bir kez; skor takimimiza gore.
                if (phase == "gameover" && _mapPhase is not null && _mapPhase != "gameover" && _myTeam is not null)
                {
                    int ct = Int(Obj(map, "team_ct"), "score") ?? 0, t = Int(Obj(map, "team_t"), "score") ?? 0;
                    int mine = _myTeam == "CT" ? ct : t, theirs = _myTeam == "CT" ? t : ct;
                    if (mine != theirs) Fired?.Invoke(mine > theirs ? "win" : "lose");
                }
                _mapPhase = phase;

                var flags = new List<string>();
                if (self && (Int(state, "flashed") ?? 0) > FlashThreshold) flags.Add("flash");
                if (Str(round, "bomb") == "planted") flags.Add("bomb");
                bool dead = !self || health <= 0;
                Publish(new GameState(self ? Math.Clamp(health / 100.0, 0, 1) : 0, dead, string.Join(",", flags)));
            }
        }
        catch (JsonException) { }
    }

    void Publish(GameState? st)
    {
        if (_published && st == _last) return;
        _published = true;
        _last = st;
        StateChanged?.Invoke(st);
    }

    void ResetMatch()
    {
        _kills = -1;
        _myTeam = _mapPhase = null;
    }

    void CheckStale()
    {
        lock (_gate)
        {
            if (_last is null || DateTime.UtcNow - _lastPost < Stale) return;
            ResetMatch();
            Publish(null);
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _staleTimer.Dispose();
        _listener.Stop();
    }
}
