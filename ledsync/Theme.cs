using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace LedSync;

// Acik/koyu tema. Renk tokenlari Application kaynaklarinda yer degistirir; stiller DynamicResource ile okudugu icin
// aninda guncellenir. Fluent kontroller (kaydirici, metin kutusu) ThemeMode ile birlikte doner.
// Notr tonlar Rhizzu markasindan: Blackened Blue #1E2732 ve Cool Gray #AEB7C2 ailesi.
static class Theme
{
    // "System": Windows uygulama temasini izler; "Light" / "Dark": kullanici secimi.
    public static bool IsDark { get; private set; }

    static readonly (string key, string light, string dark)[] Tokens =
    [
        ("Ground", "#F4F6F8", "#11161C"),
        ("Surface", "#FFFFFF", "#1A2129"),
        ("PanelFill", "#F9FAFB", "#161C23"),
        ("Active", "#E6EAEE", "#263039"),
        ("Line", "#E1E5EA", "#29323D"),
        ("Rule", "#EEF1F4", "#202831"),
        ("Ink", "#1E2732", "#E6EBF0"),
        ("Muted", "#5E6A78", "#8E9AA8"),
        ("Primary", "#1E2732", "#E6EBF0"),
        ("OnPrimary", "#FFFFFF", "#1E2732"),
        ("Secondary", "#E4E8EC", "#232B35"),
        ("Hover", "#DDE2E8", "#2C3642"),
        ("Pressed", "#D0D7DE", "#35414E"),
        ("SwitchOff", "#C9D1DA", "#3A4552"),
        ("Stage", "#1A212A", "#1A212A"),
        ("PosBg", "#DCFAE5", "#12301C"), ("PosInk", "#04250E", "#8BE3A6"),
        ("WarnBg", "#FFFBEB", "#2A2312"), ("WarnLine", "#FDE68A", "#5A4614"), ("WarnInk", "#92400E", "#F2C66D"),
        ("NegBg", "#FFEDEA", "#3A1D1D"), ("NegInk", "#831C1E", "#FF8F86"),
        ("NeuBg", "#EEF1F4", "#232B35"), ("NeuInk", "#3F4C5B", "#C3CCD6"),
    ];

    // Fluent kaydirici ve onay kutusu vurgu ailesinden boyar: acikta marka lacivertine, koyuda acik griye esitlenir.
    static readonly (string suffix, string light, string dark)[] Accent =
    [
        ("", "#1E2732", "#C9D1DA"),
        ("Light1", "#2E3A48", "#D5DCE3"), ("Light2", "#3F4C5B", "#E1E6EB"), ("Light3", "#5E6A78", "#EDF0F3"),
        ("Dark1", "#1E2732", "#AEB7C2"), ("Dark2", "#161D25", "#8E9AA8"), ("Dark3", "#0F141A", "#6F7C8A"),
    ];

    public static void Apply(string mode)
    {
        bool dark = mode switch { "Dark" => true, "Light" => false, _ => SystemDark() };
        IsDark = dark;
        var res = Application.Current.Resources;
        foreach (var (key, light, darkHex) in Tokens) res[key] = Brush(dark ? darkHex : light);

        foreach (var (suffix, light, darkHex) in Accent)
        {
            var c = Parse(dark ? darkHex : light);
            res["SystemAccentColor" + suffix] = c;
            // .NET 9+ SystemColors anahtarlari (AccentColorKey, AccentColorLight1BrushKey ...).
            if (typeof(SystemColors).GetProperty($"AccentColor{suffix}Key")?.GetValue(null) is ResourceKey ck) res[ck] = c;
            if (typeof(SystemColors).GetProperty($"AccentColor{suffix}BrushKey")?.GetValue(null) is ResourceKey bk) res[bk] = Brush(c);
        }
        res["AccentFillColorDefaultBrush"] = Brush(dark ? "#E6EBF0" : "#1E2732");
        res["AccentFillColorSecondaryBrush"] = Brush(dark ? "#D5DCE3" : "#2E3A48");
        res["AccentFillColorTertiaryBrush"] = Brush(dark ? "#C3CCD6" : "#3F4C5B");
        res["AccentFillColorDisabledBrush"] = Brush(dark ? "#4E5966" : "#AEB7C2");

        Application.Current.ThemeMode = dark ? ThemeMode.Dark : ThemeMode.Light;
    }

    // Windows Ayarlar > Kisisellestirme > Renkler > "Varsayilan uygulama modu".
    public static bool SystemDark()
        => Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) is int v && v == 0;

    static Color Parse(string hex) => (Color)ColorConverter.ConvertFromString(hex);
    static SolidColorBrush Brush(string hex) => Brush(Parse(hex));

    static SolidColorBrush Brush(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
