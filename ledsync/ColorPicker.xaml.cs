using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace LedSync;

// Ton x doygunluk secici; renk her zaman tam parlak (HSV'de V = 1). Surukledikce ColorChanged ("#RRGGBB") yayinlar;
// denetleyici ust uste gelen yazmalari birlestirir.
public partial class ColorPicker : UserControl
{
    double _h, _s = 1;
    bool _dragging;

    public event Action<string>? ColorChanged;

    public ColorPicker()
    {
        InitializeComponent();
        SizeChanged += (_, _) => Place();
    }

    // Disaridan gelen renk (hazir kutu, hex). Surukleme sirasinda yok sayilir; yoksa yuvarlama imleci geri iter.
    public void SetColor(string hex)
    {
        if (_dragging || !LightState.IsHex(hex)) return;
        double r = Convert.ToByte(hex[1..3], 16) / 255.0, g = Convert.ToByte(hex[3..5], 16) / 255.0, b = Convert.ToByte(hex[5..7], 16) / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        _s = max == 0 ? 0 : d / max;
        // Beyazda ton tanimsiz; son tonu koru ki imlec yatayda ziplamasin.
        if (d > 0)
        {
            double h = max == r ? (g - b) / d : max == g ? 2 + (b - r) / d : 4 + (r - g) / d;
            _h = (h * 60 + 360) % 360;
        }
        Place();
    }

    public static string Hex(double h, double s)
    {
        double x = s * (1 - Math.Abs(h / 60 % 2 - 1)), m = 1 - s;
        var (r, g, b) = (int)(h / 60) switch
        {
            0 => (s, x, 0.0), 1 => (x, s, 0.0), 2 => (0.0, s, x),
            3 => (0.0, x, s), 4 => (x, 0.0, s), _ => (s, 0.0, x),
        };
        static byte B(double f) => (byte)Math.Round(Math.Clamp(f, 0, 1) * 255);
        return $"#{B(r + m):X2}{B(g + m):X2}{B(b + m):X2}";
    }

    void Place()
    {
        Canvas.SetLeft(Thumb, _h / 360 * Area.ActualWidth - Thumb.Width / 2);
        Canvas.SetTop(Thumb, (1 - _s) * Area.ActualHeight - Thumb.Height / 2);
    }

    void OnDown(object sender, MouseButtonEventArgs e) { _dragging = Area.CaptureMouse(); Pick(e.GetPosition(Area)); }
    void OnMove(object sender, MouseEventArgs e) { if (_dragging && Area.IsMouseCaptured) Pick(e.GetPosition(Area)); }

    void OnUp(object sender, MouseButtonEventArgs e)
    {
        Area.ReleaseMouseCapture();
        _dragging = false;
    }

    void Pick(Point p)
    {
        // 360 yerine 359.9: tam sagda ton 0'a (kirmiziya) sarip imleci sola atmasin.
        _h = Math.Clamp(p.X / Area.ActualWidth, 0, 1) * 359.9;
        _s = Math.Clamp(1 - p.Y / Area.ActualHeight, 0, 1);
        Place();
        ColorChanged?.Invoke(Hex(_h, _s));
    }
}
