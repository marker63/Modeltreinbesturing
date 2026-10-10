using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Baanverkenner.Kern;

namespace Baanverkenner;

/// <summary>Toont het automatisch gemaakte blokkenschema (zie <see cref="SchemaTekening"/>) van de huidige baankaart.</summary>
public partial class BlokkenschemaWindow : Window
{
    private readonly Func<Baankaart?> _kaartBron;
    private SchemaTekening? _tekening;

    public BlokkenschemaWindow(Func<Baankaart?> kaartBron)
    {
        InitializeComponent();
        _kaartBron = kaartBron;
        Teken();
    }

    private static Brush Kleur(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;

    private void Teken()
    {
        Tekenvlak.Children.Clear();
        var kaart = _kaartBron();
        if (kaart is null)
        {
            Tekenvlak.Width = 400; Tekenvlak.Height = 100;
            var leeg = new TextBlock { Text = "Er is nog geen baankaart. Start eerst een verkenning.", Margin = new Thickness(20) };
            Tekenvlak.Children.Add(leeg);
            return;
        }
        _tekening = SchemaTekening.Maak(kaart);
        Tekenvlak.Width = _tekening.Breedte;
        Tekenvlak.Height = _tekening.Hoogte;
        var lijnKleur = Kleur("#44506a");

        foreach (var vb in _tekening.Verbindingen)
        {
            Tekenvlak.Children.Add(new Line
            {
                X1 = vb.Begin.X, Y1 = vb.Begin.Y, X2 = vb.Eind.X, Y2 = vb.Eind.Y,
                Stroke = lijnKleur, StrokeThickness = 2
            });
            if (vb.PijlNaarNaar) Pijlpunt(vb.Begin.X, vb.Begin.Y, vb.Eind.X, vb.Eind.Y, lijnKleur);
            if (vb.PijlNaarVan) Pijlpunt(vb.Eind.X, vb.Eind.Y, vb.Begin.X, vb.Begin.Y, lijnKleur);
        }
        foreach (var vb in _tekening.Verbindingen)
        {
            if (vb.Labels.Count == 0) continue;
            var tekst = new TextBlock { Text = string.Join("\n", vb.Labels), FontSize = 11, TextAlignment = TextAlignment.Center, Foreground = Kleur("#333333") };
            var rand = new Border
            {
                Child = tekst, Background = Kleur("#EBFFFFFF"), BorderBrush = Kleur("#C9CFDB"), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4), Padding = new Thickness(5, 2, 5, 2), Width = vb.LabelBreedte, Height = vb.LabelHoogte
            };
            Canvas.SetLeft(rand, vb.LabelPlek.X - vb.LabelBreedte / 2);
            Canvas.SetTop(rand, vb.LabelPlek.Y - vb.LabelHoogte / 2);
            Tekenvlak.Children.Add(rand);
        }
        foreach (var v in _tekening.Vakken)
        {
            string vul = v.Soort == "splitsing" ? "#FFF1DC" : v.Soort == "onbekend" ? "#F4F4F4" : "#E6F2EE";
            string rand = v.Soort == "splitsing" ? "#C27A1A" : v.Soort == "onbekend" ? "#999999" : "#1F6F5C";
            var paneel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            paneel.Children.Add(new TextBlock { Text = v.Titel, FontWeight = FontWeights.SemiBold, FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center });
            paneel.Children.Add(new TextBlock { Text = "melders " + v.Melders, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center });
            if (v.Markeringen.Count > 0)
                paneel.Children.Add(new TextBlock
                {
                    Text = v.Markeringen[0] + (v.Markeringen.Count > 1 ? $" (+{v.Markeringen.Count - 1})" : ""),
                    FontSize = 10.5, Foreground = Kleur("#A12A2A"), HorizontalAlignment = HorizontalAlignment.Center
                });
            var kader = new Border
            {
                Child = paneel, Width = v.B, Height = v.H, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(2),
                Background = Kleur(vul), BorderBrush = Kleur(rand),
                ToolTip = v.Markeringen.Count == 0 ? null : string.Join("\n", v.Markeringen)
            };
            Canvas.SetLeft(kader, v.X - v.B / 2);
            Canvas.SetTop(kader, v.Y - v.H / 2);
            Tekenvlak.Children.Add(kader);
        }
    }

    private void Pijlpunt(double vanX, double vanY, double naarX, double naarY, Brush kleur)
    {
        double dx = naarX - vanX, dy = naarY - vanY;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1e-6) return;
        double ux = dx / len, uy = dy / len;
        const double l = 12, b = 5;
        var punt = new Point(naarX, naarY);
        var a = new Point(naarX - ux * l - uy * b, naarY - uy * l + ux * b);
        var c = new Point(naarX - ux * l + uy * b, naarY - uy * l - ux * b);
        Tekenvlak.Children.Add(new Polygon { Points = new PointCollection { punt, a, c }, Fill = kleur });
    }

    private void Ververs_Click(object sender, RoutedEventArgs e) => Teken();

    private void OpslaanPng_Click(object sender, RoutedEventArgs e)
    {
        if (_tekening is null || Tekenvlak.Width <= 0) return;
        var dlg = new Microsoft.Win32.SaveFileDialog { FileName = $"blokkenschema_{DateTime.Now:yyyy-MM-dd_HHmm}.png", Filter = "Afbeelding (*.png)|*.png", AddExtension = true };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            Tekenvlak.Measure(new Size(Tekenvlak.Width, Tekenvlak.Height));
            Tekenvlak.Arrange(new Rect(0, 0, Tekenvlak.Width, Tekenvlak.Height));
            var bmp = new RenderTargetBitmap((int)Tekenvlak.Width, (int)Tekenvlak.Height, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(Tekenvlak);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using var fs = File.Create(dlg.FileName);
            enc.Save(fs);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Opslaan mislukt", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpslaanSvg_Click(object sender, RoutedEventArgs e)
    {
        if (_tekening is null) return;
        var dlg = new Microsoft.Win32.SaveFileDialog { FileName = $"blokkenschema_{DateTime.Now:yyyy-MM-dd_HHmm}.svg", Filter = "SVG (*.svg)|*.svg", AddExtension = true };
        if (dlg.ShowDialog(this) != true) return;
        try { File.WriteAllText(dlg.FileName, _tekening.AlsSvg(true), new System.Text.UTF8Encoding(false)); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Opslaan mislukt", MessageBoxButton.OK, MessageBoxImage.Error); }
    }
}
