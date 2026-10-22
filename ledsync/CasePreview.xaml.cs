using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace LedSync;

public partial class CasePreview : UserControl
{
    static readonly Color OffColor = Color.FromRgb(0x3a, 0x3a, 0x3a);
    static readonly Brush SelectedStroke = new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF2));

    // Parca -> secilince beyaz cercevesi yanan sekiller ve normal halleri.
    readonly Dictionary<string, (Shape shape, Brush stroke, double thickness)[]> _frames;

    // Sync kapaliyken parcaya tiklaninca: "fans", "cpu", "gpu".
    public event Action<string>? PartClicked;

    public CasePreview()
    {
        InitializeComponent();
        _frames = new()
        {
            [LightController.Fans] = [Frame(FrontFrame)],
            [LightController.Cpu] = [Frame(TopFrame), Frame(PumpFrame)],
            [LightController.Gpu] = [Frame(GpuFrame)],
        };
    }

    static (Shape, Brush, double) Frame(Shape s) => (s, s.Stroke, s.StrokeThickness);

    // selected null: secim yok (Sync acik); parcalar tiklanamaz.
    internal void Select(string? selected)
    {
        Cursor = selected is null ? null : Cursors.Hand;
        foreach (var (id, shapes) in _frames)
            foreach (var (shape, stroke, thickness) in shapes)
            {
                bool on = id == selected;
                shape.Stroke = on ? SelectedStroke : stroke;
                shape.StrokeThickness = on ? thickness + 1.5 : thickness;
            }
    }

    void OnPart(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string id })
        {
            PartClicked?.Invoke(id);
            e.Handled = true;
        }
    }

    // t: saniye; efektler donanimdakiyle ayni hesapla cizilir.
    internal void Show(LightState s, double t, bool alert = false)
    {
        Set("Io", s, LightController.Fans, t, alert);
        Set("H1", s, LightController.Cpu, t, alert);
        Set("Gpu", s, LightController.Gpu, t, alert);
    }

    void Set(string key, LightState s, string device, double t, bool alert)
    {
        var l = Effects.At(s, device, t, alert: alert, preview: true);
        var color = Color.FromRgb(l.R, l.G, l.B);
        // Seviye dogrusal isik; ekranda opaklik algisi icin gamma ile acilir (yoksa nefesin karanlik ucu kaybolur).
        double op = Math.Pow(l.Level, 1 / 2.2);
        bool lit = s.On;
        var ring = (SolidColorBrush)Resources[key + "Ring"];
        var soft = (SolidColorBrush)Resources[key + "Soft"];
        ring.Color = lit ? color : OffColor;
        ring.Opacity = lit ? Math.Min(1, 0.15 + op) : 0.6;
        soft.Color = color;
        soft.Opacity = lit ? op * 0.35 : 0;
    }
}
