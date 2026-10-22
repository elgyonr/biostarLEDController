# RLC simgesi: Rhizzu R sembolu (marka vektor yollari, sekil degismez) + RGB isik yorumu.
# Kullanim:
#   RlcIcon.ps1 -Preview <png>          uc cesidi (A/B/C) yan yana, 256/48/32/16 px ve iki zeminde gosterir
#   RlcIcon.ps1 -Variant A -OutDir ..   secilen cesitten rlc.ico (16-256) ve rlc-256.png uretir (secilen: A)
#   RlcIcon.ps1 -Variant Off -OutDir .. tepsi icin isiklar kapali hali: rlc-off.ico (seritler soluk gri)
param(
    [string]$Preview,
    [ValidateSet("A", "B", "C", "Off")][string]$Variant = "A",
    [string]$OutDir = $PSScriptRoot
)

Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase
Add-Type -ReferencedAssemblies PresentationCore, PresentationFramework, WindowsBase, System.Xaml -TypeDefinition @"
using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

public static class Rlc
{
    // Rhizzu sembol yollari (rhizzu-app-dark.svg, 263.67 birimlik kare). Dokunulmaz.
    const string Ink0 = "M 132.96,179.46 L 132.96,180.46 L 145.46,192.96 L 150.71,198.71 L 164.46,211.96 L 230.71,211.96 L 231.71,210.96 L 231.71,209.96 L 209.46,187.46 L 191.46,168.71 L 177.46,154.71 L 175.21,153.21 L 159.46,153.46 L 153.71,158.71 Z";
    const string Ink8 = "M 32.21,52.71 L 32.46,54.96 L 64.71,87.96 L 66.71,88.96 L 148.96,88.96 L 152.46,90.96 L 153.96,93.71 L 153.96,96.46 L 152.71,98.96 L 110.46,138.71 L 110.71,139.71 L 182.46,139.71 L 186.71,139.21 L 194.46,136.71 L 198.46,134.71 L 203.21,131.46 L 207.71,127.21 L 211.46,122.46 L 213.96,117.96 L 215.46,114.21 L 216.96,107.46 L 216.96,98.96 L 215.96,94.21 L 212.96,86.71 L 209.71,82.21 L 185.46,57.46 L 181.46,54.71 L 177.21,52.96 L 172.21,51.71 L 166.71,51.21 L 34.21,51.21 L 32.96,51.71 Z";
    const string Acc9 = "M 141.21,153.46 L 140.71,152.96 L 134.46,152.71 L 95.21,152.71 L 93.46,153.46 L 44.21,199.96 L 32.96,210.96 L 32.96,211.96 L 33.96,212.46 L 81.46,211.96 L 137.21,158.21 L 141.21,153.96 Z";
    const string Acc10 = "M 122.71,105.46 L 121.96,104.71 L 120.46,104.21 L 69.46,104.46 L 68.46,104.96 L 31.96,141.21 L 31.96,192.71 L 32.71,192.96 L 40.46,185.71 L 45.21,180.71 L 48.46,177.96 L 75.71,151.46 L 103.46,125.46 L 117.21,111.96 L 122.46,107.46 L 122.96,106.46 Z";
    const double Box = 263.67;
    static readonly Color Navy = Color.FromRgb(0x1E, 0x27, 0x32), Gray = Color.FromRgb(0xAE, 0xB7, 0xC2);

    static Geometry G(string d) { var g = Geometry.Parse(d); g.Freeze(); return g; }

    // Isik yonu: sol alttan sag uste kirmizi -> sari -> yesil -> camgobegi -> mavi -> mor.
    static LinearGradientBrush Rgb(Point from, Point to)
    {
        var b = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = from, EndPoint = to };
        string[] c = { "#FF2D55", "#FF9500", "#FFE600", "#34E36B", "#00D5FF", "#2F6BFF", "#B44CFF" };
        for (int i = 0; i < c.Length; i++) b.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(c[i]), i / (c.Length - 1.0)));
        return b;
    }

    static DrawingVisual Layer(Action<DrawingContext> draw, double blur)
    {
        var v = new DrawingVisual();
        using (var dc = v.RenderOpen()) draw(dc);
        if (blur > 0) v.Effect = new BlurEffect { Radius = blur, KernelType = KernelType.Gaussian };
        return v;
    }

    public static Visual Build(string variant, bool small)
    {
        var root = new ContainerVisual();
        var tile = new RectangleGeometry(new Rect(0, 0, Box, Box), 54, 54);
        var ink = new GeometryGroup(); ink.Children.Add(G(Ink0)); ink.Children.Add(G(Ink8));
        var acc = new GeometryGroup(); acc.Children.Add(G(Acc9)); acc.Children.Add(G(Acc10));
        var diag = Rgb(new Point(32, 212), new Point(232, 51));

        root.Children.Add(Layer(dc => dc.DrawGeometry(new SolidColorBrush(Navy), null, tile), 0));
        if (variant == "A")
        {
            // Gri seritler LED serit gibi yanar; altinda yumusak isik.
            var bars = Rgb(new Point(32, 212), new Point(141, 104));
            if (!small) root.Children.Add(Layer(dc => dc.DrawGeometry(bars, null, acc), 14));
            root.Children.Add(Layer(dc => dc.DrawGeometry(new SolidColorBrush(Colors.White), null, ink), 0));
            root.Children.Add(Layer(dc => dc.DrawGeometry(bars, null, acc), 0));
        }
        else if (variant == "B")
        {
            // Tum sembol tek RGB gecis; serit parcalari biraz soluk ki iki ton ayrimi korunsun.
            if (!small) root.Children.Add(Layer(dc => { dc.PushOpacity(0.55); dc.DrawGeometry(diag, null, ink); dc.DrawGeometry(diag, null, acc); dc.Pop(); }, 16));
            root.Children.Add(Layer(dc => dc.DrawGeometry(diag, null, ink), 0));
            root.Children.Add(Layer(dc => { dc.PushOpacity(0.6); dc.DrawGeometry(diag, null, acc); dc.Pop(); }, 0));
        }
        else if (variant == "Off")
        {
            // Isiklar kapali: A ile ayni yerlesim, seritler sonuk gri ve parlamasiz.
            root.Children.Add(Layer(dc => dc.DrawGeometry(new SolidColorBrush(Colors.White), null, ink), 0));
            root.Children.Add(Layer(dc => dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(0x4E, 0x59, 0x66)), null, acc), 0));
        }
        else
        {
            // Sembol markadaki gibi; karenin kenarinda RGB isik cercevesi.
            var edge = new RectangleGeometry(new Rect(7, 7, Box - 14, Box - 14), 48, 48);
            var pen = new Pen(diag, small ? 22 : 12);
            if (!small) root.Children.Add(Layer(dc => dc.DrawGeometry(null, new Pen(diag, 18), edge), 12));
            root.Children.Add(Layer(dc => { dc.PushClip(tile); dc.DrawGeometry(null, pen, edge); dc.Pop(); }, 0));
            root.Children.Add(Layer(dc => dc.DrawGeometry(new SolidColorBrush(Colors.White), null, ink), 0));
            root.Children.Add(Layer(dc => dc.DrawGeometry(new SolidColorBrush(Gray), null, acc), 0));
        }
        return root;
    }

    public static BitmapSource Render(string variant, int size)
    {
        var host = new ContainerVisual();
        var v = Build(variant, size <= 32);
        host.Children.Add(v);
        double k = size / Box;
        host.Transform = new ScaleTransform(k, k);
        var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(host);
        bmp.Freeze();
        return bmp;
    }

    static byte[] Png(BitmapSource b)
    {
        var e = new PngBitmapEncoder(); e.Frames.Add(BitmapFrame.Create(b));
        using (var ms = new MemoryStream()) { e.Save(ms); return ms.ToArray(); }
    }

    public static void Ico(string variant, string dir)
    {
        int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
        var data = new byte[sizes.Length][];
        for (int i = 0; i < sizes.Length; i++) data[i] = Png(Render(variant, sizes[i]));
        string name = variant == "Off" ? "rlc-off" : "rlc";
        if (variant != "Off") File.WriteAllBytes(Path.Combine(dir, "rlc-256.png"), data[sizes.Length - 1]);
        using (var f = File.Create(Path.Combine(dir, name + ".ico")))
        using (var w = new BinaryWriter(f))
        {
            w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                byte d = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);
                w.Write(d); w.Write(d); w.Write((byte)0); w.Write((byte)0); w.Write((short)1); w.Write((short)32);
                w.Write(data[i].Length); w.Write(offset);
                offset += data[i].Length;
            }
            foreach (var p in data) w.Write(p);
        }
    }

    // Uc cesit: her satir bir cesit; acik ve koyu zeminde 256, 48, 32, 16 px (kucukler 1:1).
    public static void Sheet(string path)
    {
        string[] vs = { "A", "B", "C" };
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xF3, 0xF5, 0xF7)), null, new Rect(0, 0, 900, 3 * 300));
            for (int r = 0; r < 3; r++)
            {
                double y = r * 300 + 22;
                var label = new FormattedText(vs[r], System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI Semibold"), 28, Brushes.Black, 1.0);
                dc.DrawText(label, new Point(20, y));
                dc.DrawImage(Render(vs[r], 256), new Rect(70, y, 256, 256));
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20)), null, new Rect(620, y, 260, 256));
                int[] sm = { 48, 32, 16 };
                double x = 380;
                foreach (int s in sm) { dc.DrawImage(Render(vs[r], s), new Rect(x, y + 104, s, s)); x += s + 30; }
                x = 650;
                foreach (int s in sm) { dc.DrawImage(Render(vs[r], s), new Rect(x, y + 104, s, s)); x += s + 30; }
            }
        }
        var bmp = new RenderTargetBitmap(900, 900, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(dv);
        File.WriteAllBytes(path, Png(bmp));
    }
}
"@

if ($Preview) { [Rlc]::Sheet($Preview); "onizleme: $Preview" }
else { [Rlc]::Ico($Variant, $OutDir); "$Variant cesidi yazildi: $OutDir" }
