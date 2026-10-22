using System.Security.Cryptography;

namespace LedSync;

// Oyun olayina bagli isik kurali.
sealed class GameRule
{
    public bool On { get; set; } = true;
    public DeviceLight Light { get; set; } = new();
    public GameRule Clone() => new() { On = On, Light = Light.Clone() };
}

// Bir oyunun ayarlari: ana anahtar, dusuk can esigi, kurallar.
sealed class GameSettings
{
    public bool On { get; set; } = true;
    public int Threshold { get; set; } = 30;
    public Dictionary<string, GameRule> Rules { get; set; } = [];
    // CS2: GSI cfg dosyasindaki paylasilan anahtar; baska programlar sahte veri gonderemesin.
    public string? Token { get; set; }

    public GameSettings Clone() => new()
    {
        On = On, Threshold = Threshold, Token = Token, Rules = Rules.ToDictionary(kv => kv.Key, kv => kv.Value.Clone()),
    };
}

// Izleyicinin bildirdigi anlik durum. Flags: oyuna ozgu durum kurallari ("flash", "bomb"), virgulle.
readonly record struct GameState(double Hp, bool Dead, string Flags = "")
{
    public bool Has(string flag) => Flags.Split(',').Contains(flag);
}

// Seconds > 0: olay kurali (o kadar yanar, sonra biter). 0: durum kurali (durum surdukce yanar).
readonly record struct RuleDef(string Id, string Name, double Seconds, DeviceLight Default, string Note);

sealed record GameDef(string Id, string Name, RuleDef[] Rules);

static class GameDefs
{
    // Kural isiklarinda secilebilen efektler.
    public static readonly LightEffect[] Effects = [LightEffect.Blink, LightEffect.Breath, LightEffect.Static];

    // Ayni anda birden cok durum kurali gecerliyse oncelik bu siradadir. Olay kurallari her zaman en ustte.
    public static readonly string[] StateOrder = ["death", "flash", "lowhp", "bomb"];

    static DeviceLight L(string color, LightEffect effect, int speed, int brightness = 100)
        => new() { Color = color, Color2 = color, Brightness = brightness, Effect = effect, Speed = speed };

    const string LowHpNote = "Can eşiğin altına düşünce yanar; toparlanınca ışıklar eski haline döner.";
    const string DeathNote = "Ölü kaldığın sürece yanar; yeniden doğunca biter.";
    static string EventNote(double s) => $"Olay olunca {s:0} saniye yanar, sonra ışıklar eski haline döner.";

    static RuleDef LowHp => new("lowhp", "Düşük can", 0, L("#FF0000", LightEffect.Blink, 7), LowHpNote);
    static RuleDef Death => new("death", "Ölünce", 0, L("#FFFFFF", LightEffect.Breath, 3, 40), DeathNote);
    static RuleDef Kill => new("kill", "Öldürünce", 2, L("#FF9000", LightEffect.Blink, 9), EventNote(2));
    static RuleDef Win => new("win", "Kazanınca", 8, L("#00FF00", LightEffect.Breath, 7), EventNote(8));
    static RuleDef Lose => new("lose", "Kaybedince", 8, L("#FF0000", LightEffect.Static, 5), EventNote(8));

    public static readonly GameDef[] All =
    [
        new("lol", "League of Legends", [LowHp, Death, Kill, Win, Lose]),
        new("cs2", "Counter-Strike 2",
        [
            LowHp, Death,
            new("flash", "Kör olunca", 0, L("#FFFFFF", LightEffect.Static, 5), "Flash ile kör kaldığın sürece yanar."),
            Kill,
            new("bomb", "Bomba kurulunca", 0, L("#FF4000", LightEffect.Breath, 8), "Bomba kuruluyken yanar; patlayınca, çözülünce ya da tur bitince biter."),
            Win, Lose,
        ]),
    ];

    public static GameDef Get(string id) => All.First(g => g.Id == id);

    public static Dictionary<string, GameSettings> Defaults() => All.ToDictionary(g => g.Id, g => Fresh(g));

    static GameSettings Fresh(GameDef g) => new()
    {
        Rules = g.Rules.ToDictionary(r => r.Id, r => new GameRule { Light = r.Default.Clone() }),
        Token = g.Id == "cs2" ? NewToken() : null,
    };

    static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

    // Yuklenen ayarlari tamamlar: eksik oyun/kural varsayilana doner, bozuk isik ya da izinsiz efekt duzeltilir.
    public static Dictionary<string, GameSettings> Normalize(Dictionary<string, GameSettings>? saved)
    {
        var result = new Dictionary<string, GameSettings>();
        foreach (var def in All)
        {
            var fresh = Fresh(def);
            var g = saved?.GetValueOrDefault(def.Id) ?? fresh;
            g.Threshold = Math.Clamp(g.Threshold, 10, 60);
            var rules = new Dictionary<string, GameRule>();
            foreach (var r in def.Rules)
                rules[r.Id] = g.Rules?.GetValueOrDefault(r.Id) is { Light: { } l } kept && l.Valid() && Effects.Contains(l.Effect) ? kept : fresh.Rules[r.Id];
            g.Rules = rules;
            if (def.Id == "cs2" && string.IsNullOrEmpty(g.Token)) g.Token = NewToken();
            result[def.Id] = g;
        }
        return result;
    }
}
