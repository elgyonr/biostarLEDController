using System.Threading;
using System.Windows;
using Forms = System.Windows.Forms;
using Gdi = System.Drawing;

namespace LedSync;

public partial class App : Application
{
    Mutex? _single;
    Forms.NotifyIcon? _tray;
    Forms.ToolStripMenuItem? _powerItem;
    MainWindow? _window;
    Gdi.Icon? _iconOn, _iconOff;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Beklenmeyen hatalar da hata kaydina yazilir (uygulama yine kapanir; kayit nedenini gosterir).
        DispatcherUnhandledException += (_, a) => Log.Write($"arayüz hatası: {a.Exception}");
        AppDomain.CurrentDomain.UnhandledException += (_, a) =>
            Log.Write($"{(a.IsTerminating ? "kapanıyor: çökme · " : "")}beklenmeyen hata: {a.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, a) => Log.Write($"görev hatası: {a.Exception}");
        // Gelistirme: --no-hardware --snapshot <yol> pencereyi PNG'ye cizip kapanir. Donanima yazmaz, tek kopya kilidine dokunmaz.
        int snap = Array.IndexOf(e.Args, "--snapshot");
        if (snap >= 0 && snap + 1 < e.Args.Length && e.Args.Contains("--no-hardware"))
        {
            LightController.NoHardware = true;
            // --game: oyun sekmesi; --part fans|cpu|gpu: Sync kapali, o parca secili.
            var w = new MainWindow { SkipSave = true };
            int part = Array.IndexOf(e.Args, "--part");
            // --effect Breath vb.: kayitli efekt yerine bu efekt (kaydedilmez).
            int fx = Array.IndexOf(e.Args, "--effect");
            LightEffect? effect = fx >= 0 && fx + 1 < e.Args.Length && Enum.TryParse<LightEffect>(e.Args[fx + 1], out var parsed) ? parsed : null;
            w.ShowForSnapshot(e.Args.Contains("--game"), part >= 0 && part + 1 < e.Args.Length ? e.Args[part + 1] : null, effect);
            // --game cs2: o oyunun karti; --rule lowhp: o kuralin duzenleyicisi acik.
            int game = Array.IndexOf(e.Args, "--game");
            if (game >= 0 && game + 1 < e.Args.Length && GameDefs.All.Any(g => g.Id == e.Args[game + 1])) w.SelectGame(e.Args[game + 1]);
            int rule = Array.IndexOf(e.Args, "--rule");
            if (rule >= 0 && rule + 1 < e.Args.Length) w.EditRule(e.Args[rule + 1]);
            // --theme Light|Dark: kayitli tema yerine bu tema (kaydedilmez).
            int theme = Array.IndexOf(e.Args, "--theme");
            if (theme >= 0 && theme + 1 < e.Args.Length) { Theme.Apply(e.Args[theme + 1]); w.RenderTheme(); }
            Snapshot(w, e.Args[snap + 1], e.Args.Contains("--picker"));
            return;
        }
        // Ayni anda tek kopya: iki kopya ayni SMBus ve karta yazarsa birbirini ezer.
        _single = new Mutex(true, @"Local\LedSync.App", out bool first);
        if (!first)
        {
            Log.Write("ikinci kopya açılmak istendi; zaten çalıştığı için kapatıldı");
            MessageBox.Show("RHZ LED Controller zaten çalışıyor. Sistem tepsisindeki simgeden açabilirsin.", "RHZ LED Controller");
            Shutdown();
            return;
        }

        LightController.NoHardware = e.Args.Contains("--no-hardware");
        // Acilis ve kapanis da kayda yazilir: uygulama kapali bulundugunda nedeni (cikis, Windows, cokme) anlasilsin.
        var version = typeof(App).Assembly.GetName().Version;
        // Onceki oturum habersiz kapandiysa once onu yazar. Donanimsiz deneme kopyalari damgaya dokunmaz
        // (testte zorla kapatilirlar; gercek oturumu "habersiz kapandi" diye yanlis isaretlemesinler).
        if (!LightController.NoHardware) Heartbeat.Start();
        Log.Write($"başladı: sürüm {version}, PID {Environment.ProcessId}{(e.Args.Length > 0 ? $", argümanlar: {string.Join(' ', e.Args)}" : "")}");
        // Uyku/uyanma: isiklarin uyanmadan sonra yanlis kalmasi (AGENTS.md madde 6) incelenirken zaman cizelgesi icin.
        Microsoft.Win32.SystemEvents.PowerModeChanged += (_, a) =>
        {
            if (a.Mode is Microsoft.Win32.PowerModes.Suspend or Microsoft.Win32.PowerModes.Resume)
                Log.Write(a.Mode == Microsoft.Win32.PowerModes.Suspend ? "bilgisayar uykuya geçiyor" : "bilgisayar uyandı");
        };
        _window = new MainWindow();
        _window.LightChanged += UpdateTray;

        _powerItem = new Forms.ToolStripMenuItem("Işıkları kapat", null, (_, _) => _window.TogglePower());
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(_powerItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("RLC'yi aç", null, (_, _) => ShowWindow());
        menu.Items.Add("Çıkış", null, (_, _) => ExitApp());
        _iconOn = TrayIcon("rlc.ico");
        _iconOff = TrayIcon("rlc-off.ico");
        _tray = new Forms.NotifyIcon { Text = "RLC", ContextMenuStrip = menu, Visible = true };
        _tray.MouseClick += (_, a) => { if (a.Button == Forms.MouseButtons.Left) ShowWindow(); };
        UpdateTray(_window.State);

        if (!e.Args.Contains("--tray")) ShowWindow();
    }

    // --picker: ozel renk paletini acip onu cizer (acilir pencere ayri bir pencere oldugu icin ana goruntude yok).
    void Snapshot(MainWindow w, string path, bool picker)
    {
        w.ExitRequested = true;
        // Anahtar animasyonlari (0.18 sn) bitsin diye cizim yarim saniye bekler.
        w.ContentRendered += (_, _) => Delay(TimeSpan.FromMilliseconds(500), () =>
        {
            if (!picker)
            {
                // Pencerenin kendisi cizilir (istemci alani, kenar bosluklari dahil); ic ogeyi cizmek ofseti kaydirir.
                var root = (FrameworkElement)w.Content;
                Save(w, root.ActualWidth + root.Margin.Left + root.Margin.Right, root.ActualHeight + root.Margin.Top + root.Margin.Bottom, path);
                Finish(w);
                return;
            }
            var panel = w.OpenPicker();
            Dispatcher.BeginInvoke(() => { Save(panel, panel.ActualWidth, panel.ActualHeight, path); Finish(w); },
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        });
        w.Show();
    }

    static void Delay(TimeSpan after, Action action)
    {
        var t = new System.Windows.Threading.DispatcherTimer { Interval = after };
        t.Tick += (_, _) => { t.Stop(); action(); };
        t.Start();
    }

    static void Save(System.Windows.Media.Visual v, double width, double height, string path)
    {
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(v);
        var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(
            (int)Math.Ceiling(width * dpi.DpiScaleX), (int)Math.Ceiling(height * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, System.Windows.Media.PixelFormats.Pbgra32);
        bmp.Render(v);
        var png = new System.Windows.Media.Imaging.PngBitmapEncoder();
        png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
        using var f = System.IO.File.Create(path);
        png.Save(f);
    }

    void Finish(MainWindow w)
    {
        w.Close();
        Shutdown();
    }

    void ShowWindow()
    {
        if (_window is null) return;
        _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    // Tepsi simgesi: marka simgesi (RGB seritli R); isiklar kapaliyken seritler gri. Renk ipucunda yazar.
    // Simgeler Assets\RlcIcon.ps1 ile uretilir; sistemin kucuk simge boyutu (DPI'ya gore 16-32 px) secilir.
    static Gdi.Icon TrayIcon(string name)
    {
        using var stream = GetResourceStream(new Uri($"pack://application:,,,/Assets/{name}")).Stream;
        return new Gdi.Icon(stream, Forms.SystemInformation.SmallIconSize);
    }

    void UpdateTray(LightState s)
    {
        if (_tray is null) return;
        // Ayri modda ipucu on fanlarin rengini gosterir.
        var light = s.For(LightController.Fans);
        _tray.Icon = s.On ? _iconOn : _iconOff;
        _tray.Text = !s.On ? "RLC · ışıklar kapalı" : s.Sync ? $"RLC · {light.Color} · %{light.Brightness}" : "RLC · ayrı ayarlar";
        if (_powerItem is not null) _powerItem.Text = s.On ? "Işıkları kapat" : "Işıkları aç";
    }

    // Windows kapanirken ya da oturum kapanirken: pencere tepsiye gizlenmeye calismasin, ayarlari kaydedip kapansin.
    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        Log.Write($"kapanıyor: Windows {(e.ReasonSessionEnding == ReasonSessionEnding.Shutdown ? "kapanıyor ya da yeniden başlıyor" : "oturumu kapanıyor")}");
        if (_window is not null) _window.ExitRequested = true;
        base.OnSessionEnding(e);
    }

    void ExitApp()
    {
        Log.Write("kapanıyor: tepsiden Çıkış");
        if (_window is not null)
        {
            _window.ExitRequested = true;
            _window.Close();
        }
        if (_tray is not null) { _tray.Visible = false; _tray.Dispose(); }
        _iconOn?.Dispose();
        _iconOff?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Anlik goruntu modu ve ikinci kopya kayda "kapandi" yazmaz (bu kopyalar hic baslamadi).
        if (_window is not null)
        {
            Log.Write($"kapandı (çıkış kodu {e.ApplicationExitCode})");
            if (!LightController.NoHardware) Heartbeat.Stop();
        }
        _single?.Dispose();
        base.OnExit(e);
    }
}
