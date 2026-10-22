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

    // Kasadaki parcalar. Alt baslik sade; donanim yolu (adres, bolge) yalnizca ipucunda.
    static readonly (string id, string name, string detail, string tech)[] Parts =
    [
        (LightController.Fans, "Ön fanlar", "3 fan, ön mesh", "SMBus 0x58 · io bölgesi"),
        (LightController.Cpu, "CPU soğutucu", "Üst 2 fan ve pompa", "SMBus 0x58 · h1 bölgesi"),
        (LightController.Gpu, "Ekran kartı", "Palit RTX 4060 Dual, ön LED", "NVIDIA NvAPI"),
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
    LolState? _lolState; // null: oyunda degil
    // Su an yanan olay kurali ("kill", "win", "lose") ve bitis zamani.
    string? _event;
    double _eventUntil;
    readonly DispatcherTimer _eventTimer = new();

    // Oyun kurallari yalnizca Sync acikken (uyari hicbir zaman tek bir parcada yanmasin).
    bool GameOn => _s.Sync && _s.LolAlert;

    GameRule? Rule(string id) => _s.LolRules.TryGetValue(id, out var r) && r.On ? r : null;

    // LED'lere binen oyun isigi. Oncelik: olay (oldurme, mac sonucu) > olu > dusuk can.
    DeviceLight? GameLight()
    {
        if (!GameOn) return null;
        if (_event is not null && _clock.Elapsed.TotalSeconds < _eventUntil && Rule(_event) is { } ev) return ev.Light;
        if (_lolState is not { } st) return null;
        if (st.Dead) return Rule("death")?.Light;
        // Olu iken can 0; esik yalnizca yasarken.
        if (st.Hp > 0 && st.Hp * 100 < _s.LolThreshold) return Rule("lowhp")?.Light;
        return null;
    }

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
        // Son kutu: ozel renk paleti. Yazili; gokkusagi nokta "istedigin renk" demek.
        _customSwatch = new Button { Style = (Style)FindResource("CustomSwatch"), Content = "Özel", ToolTip = "Özel renk ve hex kodu", Background = Rainbow() };
        AutomationProperties_SetName(_customSwatch, "Özel renk");
        _customSwatch.Click += (_, _) => { PickerPopup.PlacementTarget = _customSwatch; PickerPopup.IsOpen = true; };
        Swatches.Children.Add(_customSwatch);
        Picker.ColorChanged += SetColor;
        AddChoices(EffectChoices, Effects.All);
        AddChoices(HardwareChoices, Effects.Hardware.Select(h => (h.id, h.name, h.desc)));
        BuildRuleControls();
        _eventTimer.Tick += (_, _) => { _eventTimer.Stop(); _event = null; ApplyGame(); };
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

    // "Onizle": kural isigi yalnizca ekrandaki kasada birkac saniye oynar; denetleyiciye (LED'lere) gitmez.
    double _previewUntil;
    DeviceLight? _previewLight;
    bool Previewing => _previewLight is not null && _clock.Elapsed.TotalSeconds < _previewUntil;
    DeviceLight? ScreenLight => Previewing ? _previewLight : GameLight();

    bool Animating => (_s.Moving(Parts.Select(p => p.id), preview: true) || ScreenLight is not null) && IsVisible;
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
        Preview.Show(_s, t, ScreenLight);
        // Onizleme bitince sabit isikta ciziciyi durdur.
        if (_previewLight is not null && !Previewing) { _previewLight = null; UpdateAnimation(); Preview.Show(_s, t, ScreenLight); }
    }

    void OnAlertPreview(object sender, RoutedEventArgs e) => PreviewRule();

    void PreviewRule()
    {
        _previewLight = _s.LolRules[_editRule].Light;
        _previewUntil = _clock.Elapsed.TotalSeconds + 3;
        UpdateAnimation();
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
    internal void ShowForSnapshot(bool game, string? part, LightEffect? effect = null)
    {
        _gameTab = game;
        if (part is not null) { _s.Sync = false; _selected = part; }
        if (effect is { } fx) T.Effect = fx;
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

    // Nefes'te ikinci renk duzenleniyor mu; kutular, palet ve hex o rengi gosterir.
    bool _second;
    bool TwoColors => T.Effect == LightEffect.Breath;
    string ActiveColor => _second && TwoColors ? T.Color2 : T.Color;

    public void SetColor(string hex)
    {
        if (LightState.Bright(hex) is not { } bright) return;
        if (_second && TwoColors) T.Color2 = bright; else T.Color = bright;
        Changed();
    }

    void OnSlot1(object sender, RoutedEventArgs e) { _second = false; Render(); }
    void OnSlot2(object sender, RoutedEventArgs e) { _second = true; Render(); }

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
        if (_s.Sync || _gameTab) return;
        _selected = id;
        Render();
    }

    void Render()
    {
        Preview.Show(_s, _clock.Elapsed.TotalSeconds, ScreenLight);
        // Parca secimi yalnizca Isik sekmesinde anlamli; oyun kurali her zaman uc parcaya birden uygulanir.
        bool picking = !_s.Sync && !_gameTab;
        Preview.Select(picking ? _selected : null);
        PickHint.Visibility = picking ? Visibility.Visible : Visibility.Collapsed;
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
        TargetSub.ToolTip = _s.Sync ? null : part.tech;
        // Iki renk yalnizca Nefes'te; baska efekte gecince duzenleme birinci renge doner.
        if (!TwoColors) _second = false;
        ColorSlots.Visibility = TwoColors ? Visibility.Visible : Visibility.Collapsed;
        Slot1.Background = Brush(t.Color);
        Slot2.Background = Brush(t.Color2);
        Slot1.Tag = _second ? null : "selected";
        Slot2.Tag = _second ? "selected" : null;
        string active = ActiveColor;
        foreach (var (b, hex) in _swatchHex) b.Tag = hex == active ? "selected" : null;
        _customSwatch.Tag = _swatchHex.ContainsValue(active) ? null : "selected";
        if (!HexBox.IsKeyboardFocusWithin) HexBox.Text = active;
        PickedDot.Background = Brush(active);
        Picker.SetColor(active);
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
        LolRulesBlock.Visibility = _s.LolAlert ? Visibility.Visible : Visibility.Collapsed;
        RuleEditor.Visibility = _s.Sync ? Visibility.Visible : Visibility.Collapsed;
        foreach (var (id, row) in _ruleRows)
        {
            var r = _s.LolRules[id];
            row.Button.Tag = id == _editRule ? "selected" : null;
            row.Switch.IsChecked = r.On;
            row.Dot.Fill = Brush(r.Light.Color);
            row.Summary.Text = Summary(id);
            row.Summary.Opacity = r.On ? 1 : 0.5;
        }
        var (_, name, seconds) = LightState.LolRuleInfo.First(x => x.id == _editRule);
        var al = _s.LolRules[_editRule].Light;
        RuleTitle.Text = $"{name} · ışık";
        foreach (var (b, e) in _alertEffects) b.Tag = e == al.Effect ? "selected" : null;
        foreach (var (b, hex) in _alertSwatchHex) b.Tag = hex == al.Color ? "selected" : null;
        _alertCustom.Tag = _alertSwatchHex.ContainsValue(al.Color) ? null : "selected";
        if (!AlertHex.IsKeyboardFocusWithin) AlertHex.Text = al.Color;
        AlertDot.Background = Brush(al.Color);
        AlertPicker.SetColor(al.Color);
        LolThresholdRow.Visibility = _editRule == "lowhp" ? Visibility.Visible : Visibility.Collapsed;
        LolThresholdValue.Text = $"%{_s.LolThreshold}";
        RuleNote.Text = _editRule switch
        {
            "lowhp" => "Can eşiğin altına düşünce yanar; toparlanınca ışıklar eski haline döner.",
            "death" => "Ölü kaldığın sürece yanar; yeniden doğunca biter.",
            _ => $"Olay olunca {seconds:0} saniye yanar, sonra ışıklar eski haline döner.",
        };
        // Sync kapaliyken onkosulu ustteki bant anlatir; burada yalnizca tercihin kendisi.
        string? lit = GameLight() is { } l ? LightState.LolRuleInfo.First(x => _s.LolRules[x.id].Light == l).name.ToLower(Tr) : null;
        LolStatus.Text = !_s.LolAlert ? "Kapalı."
            : !_s.Sync ? "Açık · Sync bekleniyor."
            : _lolState is not { } st ? "Açık · maç bekleniyor."
            : $"Oyunda · {(st.Dead ? "ölü" : $"can %{st.Hp * 100:0}")}{(lit is null ? "" : $" · {lit} ışığı yanıyor")}";
    }

    string Summary(string id)
    {
        var l = _s.LolRules[id].Light;
        string what = $"{ColorName(l.Color)} {AlertEffectName(l.Effect).ToLower(Tr)}";
        return id == "lowhp" ? $"%{_s.LolThreshold} altı · {what}" : what;
    }

    // Monitor yalnizca kurallar acik ve Sync acikken calisir; oyun disinda 3 saniyede bir yerel adresi yoklar.
    void UpdateLol()
    {
        if (GameOn && _lol is null)
        {
            _lol = new LolMonitor();
            _lol.StateChanged += st => Dispatcher.BeginInvoke(() => { _lolState = st; ApplyGame(); });
            _lol.Fired += kind => Dispatcher.BeginInvoke(() => Fire(kind));
        }
        else if (!GameOn && _lol is not null)
        {
            _lol.Dispose();
            _lol = null;
            _lolState = null;
            _event = null;
        }
        ApplyGame();
    }

    // Olay kurali: suresi kadar yanar. Yeni olay eskisini ezer (or. oldurmenin ustune mac sonu).
    void Fire(string kind)
    {
        var info = LightState.LolRuleInfo.FirstOrDefault(x => x.id == kind);
        if (!GameOn || info.id is null || Rule(kind) is null) return;
        _event = kind;
        _eventUntil = _clock.Elapsed.TotalSeconds + info.seconds;
        _eventTimer.Stop();
        _eventTimer.Interval = TimeSpan.FromSeconds(info.seconds);
        _eventTimer.Start();
        ApplyGame();
    }

    void ApplyGame()
    {
        _ctl.SetOverlay(GameLight());
        RenderLol();
        UpdateAnimation();
        if (!_rendering) Preview.Show(_s, _clock.Elapsed.TotalSeconds, ScreenLight);
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

    // --- LoL kurallari (Oyun sekmesi): satirlar + secili kuralin efekti ve rengi ---
    static readonly System.Globalization.CultureInfo Tr = new("tr-TR");
    readonly Dictionary<Button, LightEffect> _alertEffects = [];
    readonly Dictionary<Button, string> _alertSwatchHex = [];
    readonly Dictionary<string, (Button Button, CheckBox Switch, System.Windows.Shapes.Ellipse Dot, TextBlock Summary)> _ruleRows = [];
    Button _alertCustom = null!;
    string _editRule = "lowhp";

    static string AlertEffectName(LightEffect e) => e switch
    {
        LightEffect.Blink => "Yanıp sönme", LightEffect.Breath => "Nefes", _ => "Sabit",
    };

    static string ColorName(string hex) => SwatchColors.FirstOrDefault(c => c.hex == hex).name?.ToLower(Tr) ?? hex;

    DeviceLight EditLight => _s.LolRules[_editRule].Light;

    void BuildRuleControls()
    {
        foreach (var (id, name, _) in LightState.LolRuleInfo)
        {
            var sw = new CheckBox { Style = (Style)FindResource("Switch"), VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties_SetName(sw, $"{name} kuralı");
            sw.Click += (_, _) => { _s.LolRules[id].On = sw.IsChecked == true; RuleChanged(preview: false); };
            var dot = new System.Windows.Shapes.Ellipse { Width = 14, Height = 14, Margin = new(0, 0, 10, 0), Stroke = Brush("#33808080"), VerticalAlignment = VerticalAlignment.Center };
            var title = new TextBlock { Text = name, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Width = 92 };
            var summary = new TextBlock { Style = (Style)FindResource("Small"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var dock = new DockPanel();
            DockPanel.SetDock(sw, Dock.Right);
            dock.Children.Add(sw);
            dock.Children.Add(dot);
            dock.Children.Add(title);
            dock.Children.Add(summary);
            var row = new Button { Style = (Style)FindResource("RuleRow"), Content = dock };
            AutomationProperties_SetName(row, $"{name} ışığını düzenle");
            row.Click += (_, _) => { _editRule = id; RenderLol(); };
            _ruleRows[id] = (row, sw, dot, summary);
            RuleRows.Children.Add(row);
        }
        foreach (var e in LightState.LolEffects)
        {
            var b = new Button { Style = (Style)FindResource("Choice"), Content = AlertEffectName(e) };
            b.Click += (_, _) => { EditLight.Effect = e; RuleChanged(); };
            _alertEffects[b] = e;
            AlertEffects.Children.Add(b);
        }
        // Kart ici dar (408 px): kutular 32 px, ozel dugme ayni satira sigar.
        foreach (var (name, hex) in SwatchColors)
        {
            var b = new Button { Style = (Style)FindResource("Swatch"), Background = Brush(hex), ToolTip = name, Width = 32, Height = 32 };
            AutomationProperties_SetName(b, name);
            b.Click += (_, _) => SetAlertColor(hex);
            _alertSwatchHex[b] = hex;
            AlertSwatches.Children.Add(b);
        }
        _alertCustom = new Button { Style = (Style)FindResource("CustomSwatch"), Content = "Özel", Height = 32, ToolTip = "Özel renk ve hex kodu", Background = Rainbow() };
        _alertCustom.Click += (_, _) => { AlertPickerPopup.PlacementTarget = _alertCustom; AlertPickerPopup.IsOpen = true; };
        AlertSwatches.Children.Add(_alertCustom);
        AlertPicker.ColorChanged += SetAlertColor;
    }

    void SetAlertColor(string hex)
    {
        if (LightState.Bright(hex) is not { } bright) return;
        // Kural Nefes'i tek renkli nabizdir: ikinci renk birinciye esitlenir.
        EditLight.Color = EditLight.Color2 = bright;
        RuleChanged();
    }

    // Degisiklik kaydedilir, denetleyiciye gider; isik degistiyse kasada 3 sn onizlenir (LED'ler yalnizca gercek olayda).
    void RuleChanged(bool preview = true)
    {
        _ctl.Apply(_s);
        _save.Stop(); _save.Start();
        if (preview) PreviewRule();
        ApplyGame();
    }

    void OnAlertHexKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) CommitAlertHex();
    }

    void OnAlertHexCommit(object sender, KeyboardFocusChangedEventArgs e) => CommitAlertHex();

    void CommitAlertHex()
    {
        string v = AlertHex.Text.Trim();
        if (!v.StartsWith('#')) v = "#" + v;
        if (LightState.Bright(v) is not null) SetAlertColor(v);
        AlertHex.Text = EditLight.Color;
    }

    void OnLolThreshold(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // Minimum="10" XAML yuklenirken degeri zorlar; olay diger kontroller olusmadan tetiklenebilir.
        if (_loading || !IsInitialized) return;
        _s.LolThreshold = (int)Math.Round(e.NewValue);
        ApplyGame();
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

    static readonly (string mode, string name, string glyph)[] ThemeModes =
        [("System", "Sistem", "\uE770"), ("Light", "Açık", "\uE706"), ("Dark", "Koyu", "\uE708")];

    // Tema dugmesi menu acar: Sistem (Windows'u izler), Acik, Koyu. Secim kaydedilir.
    void OnThemeClick(object sender, RoutedEventArgs e)
    {
        // Menu yazi tipini PlacementTarget'tan miras alir; dugme simge fontu (Segoe Fluent Icons) kullandigi icin
        // metin kutucuk gorunurdu. Pencerenin yazi tipi acikca verilir.
        var menu = new ContextMenu
        {
            PlacementTarget = ThemeButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
            FontFamily = FontFamily, FontSize = FontSize,
        };
        foreach (var (mode, name, _) in ThemeModes)
        {
            var item = new MenuItem { Header = name, IsCheckable = true, IsChecked = _s.Theme == mode };
            item.Click += (_, _) => SetTheme(mode);
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    void SetTheme(string mode)
    {
        _s.Theme = mode;
        Theme.Apply(mode);
        RenderTheme();
        _save.Stop(); _save.Start();
    }

    void OnSystemPreference(object? sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        if (e.Category != Microsoft.Win32.UserPreferenceCategory.General || _s.Theme != "System") return;
        Dispatcher.BeginInvoke(() => { Theme.Apply(_s.Theme); RenderTheme(); });
    }

    // Dugme secili tema modunu gosterir: Sistem ekran, Acik gunes, Koyu ay.
    internal void RenderTheme()
    {
        var (_, name, glyph) = ThemeModes.FirstOrDefault(m => m.mode == _s.Theme, ThemeModes[0]);
        ThemeButton.Content = glyph;
        ThemeButton.ToolTip = $"Tema: {name}";
    }
    void OnTogglePower(object sender, ExecutedRoutedEventArgs e) => TogglePower();

    void OnBrightness(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // Minimum="1" XAML yuklenirken degeri 0'dan 1'e zorlar; o an diger kontroller henuz yok.
        if (_loading || !IsInitialized) return;
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
        HexBox.Text = ActiveColor;
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
