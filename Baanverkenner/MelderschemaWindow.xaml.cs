using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Baanverkenner.Kern;

namespace Baanverkenner;

/// <summary>BUG #199: toont het melderschema (zie <see cref="Melderschema"/>) van de huidige baankaart. Werkt ook tijdens een rondrit, zonder bloknummers.</summary>
public partial class MelderschemaWindow : Window
{
    private readonly Func<Baankaart?> _kaartBron;
    private readonly Func<IReadOnlyCollection<int>> _bezetBron;
    private Melderschema? _schema;
    // Wat: ververst het schema elke 2 s en kleurt de bezette melders rood. Waarom: Marco 27-10 15:56, hij wil vanaf zijn werkplek zien waar de loc rijdt.
    private readonly DispatcherTimer _verversTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private string _vorigeStand = "";

    public MelderschemaWindow(Func<Baankaart?> kaartBron, Func<IReadOnlyCollection<int>>? bezetBron = null)
    {
        InitializeComponent();
        _kaartBron = kaartBron;
        _bezetBron = bezetBron ?? (() => Array.Empty<int>());
        Teken();
        _verversTimer.Tick += (_, _) => { if (AutoVerversen.IsChecked == true) VerversAlsNodig(); };
        _verversTimer.Start();
        Closed += (_, _) => _verversTimer.Stop();
    }

    /// <summary>Tekent alleen opnieuw als de kaart of de bezette melders veranderd zijn (anders knippert het scherm niet onnodig).</summary>
    private void VerversAlsNodig()
    {
        try
        {
            var kaart = _kaartBron();
            var bezet = _bezetBron();
            string stand = $"{kaart?.Bijgewerkt:O}|{kaart?.Melders.Count}|{kaart?.Overgangen.Count}|{string.Join(",", bezet.OrderBy(x => x))}";
            if (stand == _vorigeStand) return;
            Teken();
        }
        catch { /* de kaart wordt net bewaard: volgende keer opnieuw */ }
    }

    private static Brush Kleur(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;

    private void Teken()
    {
        Tekenvlak.Children.Clear();
        var kaart = _kaartBron();
        if (kaart is null)
        {
            Tekenvlak.Width = 400; Tekenvlak.Height = 100;
            Tekenvlak.Children.Add(new TextBlock { Text = "Er is nog geen baankaart. Start eerst een verkenning.", Margin = new Thickness(20) });
            return;
        }
        var bezet = _bezetBron().ToHashSet();
        _vorigeStand = $"{kaart.Bijgewerkt:O}|{kaart.Melders.Count}|{kaart.Overgangen.Count}|{string.Join(",", bezet.OrderBy(x => x))}";
        BezetTekst.Text = bezet.Count == 0 ? "Bezet nu: geen" : $"Bezet nu: melder {string.Join(", ", bezet.OrderBy(x => x))}";
        _schema = Melderschema.Maak(kaart);
        Tekenvlak.Width = _schema.Breedte;
        Tekenvlak.Height = _schema.Hoogte;
        var lijnKleur = Kleur("#44506a");
        foreach (var r in _schema.Randen)
        {
            var a = _schema.Knopen.First(x => x.Melder == r.A); var b = _schema.Knopen.First(x => x.Melder == r.B);
            Tekenvlak.Children.Add(new Line { X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y, Stroke = lijnKleur, StrokeThickness = 2 });
        }
        foreach (var r in _schema.Randen.Where(r => r.Labels.Count > 0))
        {
            var tekst = new TextBlock { Text = string.Join("\n", r.Labels), FontSize = 10.5, TextAlignment = TextAlignment.Center, Foreground = Kleur("#5C4700") };
            var kader = new Border
            {
                Child = tekst, Background = Kleur("#F2FFF7D6"), BorderBrush = Kleur("#8A6D00"), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4), Padding = new Thickness(4, 1, 4, 1)
            };
            kader.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(kader, r.LabelPlek.X - kader.DesiredSize.Width / 2);
            Canvas.SetTop(kader, r.LabelPlek.Y - kader.DesiredSize.Height / 2);
            Tekenvlak.Children.Add(kader);
        }
        foreach (var kn in _schema.Knopen)
        {
            string rand = kn.Splitsing ? "#C27A1A" : "#1F6F5C", vul = kn.Splitsing ? "#FFF1DC" : "#E6F2EE";
            bool isBezet = bezet.Contains(kn.Melder);
            if (isBezet) { rand = "#C62828"; vul = "#FFCDD2"; }   // bezet: rood
            var paneel = new Border
            {
                Width = Melderschema.KnoopB, Height = Melderschema.KnoopH, CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(isBezet ? 4 : 2),
                Background = Kleur(vul), BorderBrush = Kleur(rand),
                ToolTip = $"melder {kn.Melder}: {kn.AantalBuren} buren" + (kn.DinamoBlok is int bl ? $", Dinamo-blok {bl}" : ", Dinamo-blok onbekend"),
                Child = new TextBlock { Text = kn.Melder.ToString(), FontSize = 12, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
            Canvas.SetLeft(paneel, kn.X - Melderschema.KnoopB / 2);
            Canvas.SetTop(paneel, kn.Y - Melderschema.KnoopH / 2);
            Tekenvlak.Children.Add(paneel);
            if (kn.DinamoBlok is int blok)
            {
                var t = new TextBlock { Text = $"blok {blok}", FontSize = 10, Foreground = Kleur("#555555") };
                Canvas.SetLeft(t, kn.X - 14);
                Canvas.SetTop(t, kn.Y + Melderschema.KnoopH / 2 + 1);
                Tekenvlak.Children.Add(t);
            }
        }
    }

    private void Ververs_Click(object sender, RoutedEventArgs e) => Teken();

    private void OpslaanPng_Click(object sender, RoutedEventArgs e)
    {
        if (_schema is null || Tekenvlak.Width <= 0) return;
        var dlg = new Microsoft.Win32.SaveFileDialog { FileName = $"melderschema_{DateTime.Now:yyyy-MM-dd_HHmm}.png", Filter = "Afbeelding (*.png)|*.png", AddExtension = true };
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
        if (_schema is null) return;
        var dlg = new Microsoft.Win32.SaveFileDialog { FileName = $"melderschema_{DateTime.Now:yyyy-MM-dd_HHmm}.svg", Filter = "SVG (*.svg)|*.svg", AddExtension = true };
        if (dlg.ShowDialog(this) != true) return;
        try { File.WriteAllText(dlg.FileName, _schema.AlsSvg(true), new System.Text.UTF8Encoding(false)); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Opslaan mislukt", MessageBoxButton.OK, MessageBoxImage.Error); }
    }
}
