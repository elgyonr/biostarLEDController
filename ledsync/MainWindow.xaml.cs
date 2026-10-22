using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace LedSync;

public partial class MainWindow : Window
{
    public static readonly RoutedUICommand TogglePowerCommand = new("Işıkları aç/kapat", "TogglePower", typeof(MainWindow));

    // LED'in verebildigi saf tonlar: kanallar 0 ya da 255'e yakin. Ekran icin secilmis yumusak tonlar LED'de solar.
    static readonly (string name, string hex)[] SwatchColors =
    [
        ("Kırmızı", "#FF0000"), ("Turuncu", "#FF4000"), ("Kehribar", "#FF9000"), ("Yeşil", "#00FF00"),
        ("Camgöbeği", "#00FFFF"), ("Mavi", "#0000FF"), ("Mor", "#8000FF"), ("Beyaz", "#FFFFFF"),
    ];

    // Kasadaki parcalar; teknik ayrintilar yalnizca alt basliktaki ipucunda.
    static readonly (string id, string name, string detail)[] Parts =
    [
        (LightController.Fans, "Ön fanlar", "3 fan, ön mesh · SMBus 0x58, io bölgesi"),
        (LightController.Cpu, "CPU soğutucu", "Üst 2 fan ve pompa · SMBus 0x58, h1 bölgesi"),
        (LightController.Gpu, "Ekran kartı", "Palit RTX 4060 Dual, ön LED · NVIDIA NvAPI"),
    ];

    readonly LightState _s = LightState.Load();
    readonly LightController _ctl = new();
    readonly Dictionary<string, DeviceStatus> _status = [];
    readonly Dictionary<Button, string> _swatchHex = [];
    readonly Dictionary<Button, LightEffect> _effectButtons = [];
    Button _customSwatch = null!;
    // Sync kapaliyken kontrollerin duzenledigi parca (kasadan secilir).
    string _selected = LightController.Fans;
    bool _gameTab;
    DeviceLight T => _s.Sync ? _s.Shared : _s.For(_selected);
    readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    readonly DispatcherTimer _save = new() { Interval = TimeSpan.FromMilliseconds(600) };
    readonly DispatcherTimer _conflicts = new() { Interval = TimeSpan.FromSeconds(10) };
    List<string> _running = [];
    bool _loading;
    LolMonitor? _lol;
    double? _hp; // null: oyunda degil

    // Oyun ozellikleri yalnizca Sync acikken. Can esik altinda ve sifirdan buyuk (olu iken yanip sonmesin).
    bool AlertActive => _s.Sync && _s.LolAlert && _hp is double h && h > 0 && h * 100 < _s.LolThreshold;

    internal event Action<LightState>? LightChanged;
    public bool ExitRequested { get; set; }
    // Anlik goruntu modu: kullanicinin ayar dosyasina yazilmaz.
    public bool SkipSave { get; set; }

    public MainWindow()
    {
        // Tema kaynaklari pencere yuklenmeden yazilir; DynamicResource'lar ilk cizimde dogru rengi alir.
        Theme.Apply(_s.Theme);
        InitializeComponent();
        // "Sistem" temasindayken Windows'ta tema degisince uygulama da doner.
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnSystemPreference;
        foreach (var (name, hex) in SwatchColors)
        {
            var b = new Button { Style = (Style)FindResource("Swatch"), Background = Brush(hex), ToolTip = name };
            AutomationProperties_SetName(b, name);
            b.Click += (_, _) => SetColor(hex);
            _swatchHex[b] = hex;
            Swatches.Children.Add(b);
        }
        // Son kutu: ozel renk paleti. Gokkusagi dolgu "istedigin renk" demek.
        _customSwatch = new Button { Style = (Style)FindResource("Swatch"), ToolTip = "Özel renk", Background = Rainbow() };
        AutomationProperties_SetName(_customSwatch, "Özel renk");
        _customSwatch.Click += (_, _) => { PickerPopup.PlacementTarget = _customSwatch; PickerPopup.IsOpen = true; };
        Swatches.Children.Add(_customSwatch);
        Picker.ColorChanged += SetColor;
        AddChoices(EffectChoices, Effects.All);
        AddChoices(HardwareChoices, Effects.Hardware.Select(h => (h.id, h.name, h.desc)));
        Preview.PartClicked += OnPart;
        if (GameIcons.League() is { } icon) { LolIcon.Source = icon; LolIconFallback.Visibility = Visibility.Collapsed; }

        _ctl.StatusChanged += (id, st) => Dispatcher.BeginInvoke(() => _status[id] = st);
        _save.Tick += (_, _) => { _save.Stop(); _s.Save(); };
        _conflicts.Tick += (_, _) => { _running = Conflicts.Running(); RenderNotice(); };
        _conflicts.Start();
        _running = Conflicts.Running();

        Render();
        _ctl.Apply(_s);
        UpdateLol();
        // Onizleme efekti ekran yenilemesiyle oynatir; pencere gizliyken durur.
        IsVisibleChanged += (_, _) => UpdateAnimation();
        UpdateAnimation();
    }

    void AddChoices(Panel panel, IEnumerable<(LightEffect id, string name, string desc)> items)
    {
        foreach (var (id, name, desc) in items)
        {
            var b = new Button { Style = (Style)FindResource("Choice"), Content = name, ToolTip = desc };
            b.Click += (_, _) => SetEffect(id);
            _effectButtons[b] = id;
            panel.Children.Add(b);
        }
    }

    bool Animating => (_s.Moving(Parts.Select(p => p.id), preview: true) || AlertActive) && IsVisible;
    bool _rendering;

    void UpdateAnimation()
    {
        if (Animating == _rendering) return;
        _rendering = Animating;
        if (_rendering) CompositionTarget.Rendering += OnFrame;
        else CompositionTarget.Rendering -= OnFrame;
    }

    // Ekran 60-144 Hz yeniler; donanim ~30 kare/sn yazdigi icin onizlemeyi de 30'da tutmak yeterli.
    double _lastFrame;

    void OnFrame(object? sender, EventArgs e)
    {
        double t = _clock.Elapsed.TotalSeconds;
        if (t - _lastFrame < 1 / 30.0) return;
        _lastFrame = t;
        Preview.Show(_s, t, AlertActive);
    }

    internal LightState State => _s;

    // Ozel renk paletini acar; anlik goruntu icin icerigini dondurur.
    internal FrameworkElement OpenPicker()
    {
        PickerPopup.PlacementTarget = _customSwatch;
        PickerPopup.IsOpen = true;
        return (FrameworkElement)PickerPopup.Child;
    }

    // Anlik goruntu icin: oyun sekmesini ya da Sync kapali bir parca secimini gosterir.
    internal void ShowForSnapshot(bool game, string? part)
    {
        _gameTab = game;
        if (part is not null) { _s.Sync = false; _selected = part; }
        Render();
    }

    static void AutomationProperties_SetName(DependencyObject o, string name) => System.Windows.Automation.AutomationProperties.SetName(o, name);

    static SolidColorBrush Brush(string hex) => new((Color)ColorConverter.ConvertFromString(hex));

    static LinearGradientBrush Rainbow()
    {
        var g = new LinearGradientBrush { StartPoint = new(0, 0), EndPoint = new(1, 1) };
        string[] stops = ["#FF0000", "#FFFF00", "#00FF00", "#00FFFF", "#0000FF", "#FF00FF"];
        for (int i = 0; i < stops.Length; i++) g.GradientStops.Add(new((Color)ColorConverter.ConvertFromString(stops[i]), i / (stops.Length - 1.0)));
        return g;
    }

    void Changed()
    {
        Render();
        _ctl.Apply(_s);
        _save.Stop(); _save.Start();
        LightChanged?.Invoke(_s);
    }

    public void SetColor(string hex)
    {
        if (LightState.Bright(hex) is not { } bright) return;
        T.Color = bright;
        Changed();
    }

    void SetEffect(LightEffect e)
    {
        T.Effect = e;
        Changed();
    }

    public void TogglePower()
    {
        _s.On = !_s.On;
        Changed();
    }

    // Sync kapaliyken kasadaki parcaya tiklamak onu duzenlemeye alir; Sync acikken tiklama bir sey yapmaz.
    void OnPart(string id)
    {
        if (_s.Sync) return;
        _selected = id;
        Render();
    }

    void Render()
    {
        Preview.Show(_s, _clock.Elapsed.TotalSeconds, AlertActive);
        Preview.Select(_s.Sync ? null : _selected);
        PickHint.Visibility = _s.Sync ? Visibility.Collapsed : Visibility.Visible;
        UpdateAnimation();

        LightTab.Tag = _gameTab ? null : "selected";
        GameTab.Tag = _gameTab ? "selected" : null;
        LightPanel.Visibility = _gameTab ? Visibility.Collapsed : Visibility.Visible;
        GamePanel.Visibility = _gameTab ? Visibility.Visible : Visibility.Collapsed;

        var t = T;
        SyncSwitch.IsChecked = _s.Sync;
        var part = Parts.First(p => p.id == _selected);
        TargetTitle.Text = _s.Sync ? "Tüm parçalar" : part.name;
        TargetSub.Text = _s.Sync ? "Ön fanlar, CPU soğutucu ve ekran kartı aynı ayarda" : part.detail;
        foreach (var (b, hex) in _swatchHex) b.Tag = hex == t.Color ? "selected" : null;
        _customSwatch.Tag = _swatchHex.ContainsValue(t.Color) ? null : "selected";
        if (!HexBox.IsKeyboardFocusWithin) HexBox.Text = t.Color;
        PickedDot.Background = Brush(t.Color);
        Picker.SetColor(t.Color);
        // Hedef parca degisince kaydiricilar onun degerine gecer; bu bir kullanici degisikligi degil.
        _loading = true;
        Bright.Value = t.Brightness;
        SpeedSlider.Value = t.Speed;
        LolThreshold.Value = _s.LolThreshold;
        _loading = false;
        BrightValue.Text = $"%{t.Brightness}";
        foreach (var (b, e) in _effectButtons)
        {
            b.Tag = e == t.Effect ? "selected" : null;
            // Muzik yalnizca parca parca ayarlarken (Sync kapali) secilebilir.
            if (e == LightEffect.Music) b.Visibility = _s.Sync ? Visibility.Collapsed : Visibility.Visible;
        }
        // On fanlarin kendi efektleri yalnizca Sync kapali ve on fanlar seciliyken.
        HardwareBlock.Visibility = !_s.Sync && _selected == LightController.Fans ? Visibility.Visible : Visibility.Collapsed;
        // Hiz yalnizca zamanla ilerleyen efektte anlamli; sabit ve muzikte satir gizlenir.
        SpeedRow.Visibility = t.Effect is LightEffect.Static or LightEffect.Music ? Visibility.Collapsed : Visibility.Visible;
        SpeedValue.Text = Effects.IsHardware(t.Effect) ? $"{t.Speed} / 10" : $"{t.Speed} / 10 · tur {Effects.Period(t.Effect, t.Speed):0.#} sn";
        PowerButton.Content = _s.On ? "Işıkları kapat" : "Işıkları aç";
        RenderLol();
        RenderNotice();
        RenderTheme();
    }

    // Uyari satiri yalnizca ayni isiklara yazan programlar icin. Cihaz hatalari arayuzde gosterilmez;
    // denetleyici onlari hata kaydina yazar (Log).
    void RenderNotice()
    {
        Notice.Visibility = _running.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        NoticeText.Text = _running.Count == 0 ? ""
            : $"{string.Join(", ", _running)} çalışıyor. Aynı ışıklara yazdığı için seçtiğin rengi geri değiştirebilir; kapatman önerilir.";
    }

    void RenderLol()
    {
        GameSyncNote.Visibility = _s.Sync ? Visibility.Collapsed : Visibility.Visible;
        LolToggle.IsEnabled = _s.Sync;
        LolToggle.IsChecked = _s.LolAlert;
        LolThresholdRow.Visibility = _s.LolAlert ? Visibility.Visible : Visibility.Collapsed;
        LolThresholdValue.Text = $"%{_s.LolThreshold}";
        LolStatus.Text = !_s.Sync ? "Sync kapalıyken çalışmaz."
            : !_s.LolAlert ? "Kapalı."
            : _hp is not double h ? "Açık · maç bekleniyor."
            : AlertActive ? $"Oyunda · can %{h * 100:0} · uyarı" : $"Oyunda · can %{h * 100:0}";
    }

    // Monitor yalnizca uyari acik ve Sync acikken calisir; oyun disinda 3 saniyede bir yerel adresi yoklar.
    void UpdateLol()
    {
        bool want = _s.LolAlert && _s.Sync;
        if (want && _lol is null)
        {
            _lol = new LolMonitor();
            _lol.HealthChanged += hp => Dispatcher.BeginInvoke(() => { _hp = hp; ApplyAlert(); });
        }
        else if (!want && _lol is not null)
        {
            _lol.Dispose();
            _lol = null;
            _hp = null;
        }
        ApplyAlert();
    }

    void ApplyAlert()
    {
        _ctl.SetAlert(AlertActive);
        RenderLol();
        UpdateAnimation();
        if (!_rendering) Preview.Show(_s, _clock.Elapsed.TotalSeconds, AlertActive);
    }

    void OnSyncSwitch(object sender, RoutedEventArgs e) => SetSync(SyncSwitch.IsChecked == true);

    void OnEnableSync(object sender, RoutedEventArgs e) => SetSync(true);

    void SetSync(bool sync)
    {
        _s.Sync = sync;
        // Muzik Sync'te yok; eski bir ortak ayarda kalmissa sabite doner.
        if (sync && _s.Shared.Effect == LightEffect.Music) _s.Shared.Effect = LightEffect.Static;
        Changed();
        UpdateLol();
    }

    void OnLightTab(object sender, RoutedEventArgs e) { _gameTab = false; Render(); }
    void OnGameTab(object sender, RoutedEventArgs e) { _gameTab = true; Render(); }

    void OnLolToggle(object sender, RoutedEventArgs e)
    {
        _s.LolAlert = LolToggle.IsChecked == true;
        UpdateLol();
        _save.Stop(); _save.Start();
    }

    void OnLolThreshold(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // Minimum="10" XAML yuklenirken degeri zorlar; olay diger kontroller olusmadan tetiklenebilir.
        if (_loading || !IsInitialized) return;
        _s.LolThreshold = (int)Math.Round(e.NewValue);
        ApplyAlert();
        _save.Stop(); _save.Start();
    }

    // Baslik cubugu: kapat uygulamayi kapatmaz, tepsiye gizler (OnClosing); cikis tepsi menusunden.
    void OnCloseClick(object sender, RoutedEventArgs e) => Close();
    void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    // Cercevesiz pencerede Windows 11 koseleri kendiliginden yuvarlamaz; DWM'den yuvarlak kose istenir
    // (DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWCP_ROUND = 2). Windows 10'da cagri etkisizdir.
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        int round = 2;
        DwmSetWindowAttribute(new System.Windows.Interop.WindowInteropHelper(this).Handle, 33, ref round, sizeof(int));
    }

    void OnPowerClick(object sender, RoutedEventArgs e) => TogglePower();

    // Tema dugmesi acik ve koyu arasinda gecer; secim kaydedilir ve artik Windows temasini izlemez.
    void OnThemeClick(object sender, RoutedEventArgs e)
    {
        _s.Theme = Theme.IsDark ? "Light" : "Dark";
        Theme.Apply(_s.Theme);
        RenderTheme();
        _save.Stop(); _save.Start();
    }

    void OnSystemPreference(object? sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        if (e.Category != Microsoft.Win32.UserPreferenceCategory.General || _s.Theme != "System") return;
        Dispatcher.BeginInvoke(() => { Theme.Apply(_s.Theme); RenderTheme(); });
    }

    // Dugme gecilecek temayi gosterir: koyudayken gunes, aciktayken ay.
    internal void RenderTheme()
    {
        ThemeButton.Content = Theme.IsDark ? "\uE706" : "\uE708";
        ThemeButton.ToolTip = Theme.IsDark ? "Açık tema" : "Koyu tema";
    }
    void OnTogglePower(object sender, ExecutedRoutedEventArgs e) => TogglePower();

    void OnBrightness(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        T.Brightness = (int)Math.Round(e.NewValue);
        Changed();
    }

    void OnSpeed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // Minimum="1" XAML yuklenirken degeri 0'dan 1'e zorlar ve olay, diger kontroller olusmadan tetiklenir.
        if (_loading || !IsInitialized) return;
        T.Speed = (int)Math.Round(e.NewValue);
        Changed();
    }

    void OnHexKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) CommitHex();
    }

    void OnHexCommit(object sender, KeyboardFocusChangedEventArgs e) => CommitHex();

    void CommitHex()
    {
        string v = HexBox.Text.Trim();
        if (!v.StartsWith('#')) v = "#" + v;
        // Gecersiz kod (ya da siyah) uyari gostermeden gecerli renge geri doner.
        if (LightState.Bright(v) is not null) SetColor(v);
        // Koyu kod ayni tonun tam parlak haline cekilmis olabilir; kutu odaktayken Render yazmaz.
        HexBox.Text = T.Color;
    }

    // Kapatma dugmesi uygulamayi kapatmaz, tepsiye gizler. Cikis tepsi menusunden.
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!ExitRequested)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnSystemPreference;
        if (!SkipSave) _s.Save();
        _lol?.Dispose();
        _ctl.Dispose();
        base.OnClosing(e);
    }
}
