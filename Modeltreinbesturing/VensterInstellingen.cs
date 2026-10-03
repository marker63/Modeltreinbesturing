using System.IO;
using System.Text.Json;
using System.Windows;

namespace Modeltreinbesturing;

/// <summary>Onthoudt de laatst gebruikte positie/grootte per venster (MainWindow/
/// BaanontwerpWindow/TreinrouteWindow) tussen sessies, zodat je niet elke keer opnieuw
/// hoeft te slepen/schuiven nadat je een keer een prettige indeling hebt ingesteld. Puur
/// venstergeometrie - GEEN projectgegevens (die blijven in het eigen .json-bestand via
/// "Opslaan"/"Laden").</summary>
public static class VensterInstellingen
{
    private static readonly string Pad = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Modeltreinbesturing", "vensterinstellingen.json");

    private class Geometrie
    {
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public bool Maximized { get; set; }
    }

    private static Dictionary<string, Geometrie>? _cache;

    private static Dictionary<string, Geometrie> Laad()
    {
        if (_cache != null) return _cache;
        try
        {
            if (File.Exists(Pad))
            {
                var json = File.ReadAllText(Pad);
                _cache = JsonSerializer.Deserialize<Dictionary<string, Geometrie>>(json) ?? new();
                return _cache;
            }
        }
        catch
        {
            // Een beschadigd/onleesbaar instellingenbestand mag het opstarten van de app
            // nooit blokkeren - gewoon met standaardwaarden verdergaan.
        }
        _cache = new();
        return _cache;
    }

    private static void BewaarNaarSchijf()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Pad)!);
            File.WriteAllText(Pad, JsonSerializer.Serialize(_cache));
        }
        catch
        {
            // Opslaan van vensterpositie is puur comfort - een schrijffout hier mag de
            // rest van de applicatie niet verstoren.
        }
    }

    /// <summary>Past de laatst bewaarde positie/grootte toe op dit venster, indien
    /// aanwezig. Roep dit aan in de constructor, na InitializeComponent().</summary>
    public static void Toepassen(Window venster, string sleutel)
    {
        if (!Laad().TryGetValue(sleutel, out var g)) return;

        // Alleen toepassen als de bewaarde afmetingen redelijk zijn (bijv. niet van een
        // scherm dat niet meer bestaat) - anders gewoon de standaardwaarden uit XAML laten staan.
        if (g.Width < 200 || g.Height < 150) return;

        venster.Left = g.Left;
        venster.Top = g.Top;
        venster.Width = g.Width;
        venster.Height = g.Height;
        if (g.Maximized) venster.WindowState = WindowState.Maximized;

        // Zorg dat het venster niet (deels) buiten elk beschikbaar scherm valt, bijv. na
        // het loskoppelen van een tweede monitor.
        venster.Loaded += (_, _) =>
        {
            if (venster.Left < SystemParameters.VirtualScreenLeft - venster.Width ||
                venster.Left > SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth ||
                venster.Top < SystemParameters.VirtualScreenTop - venster.Height ||
                venster.Top > SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight)
            {
                venster.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
        };
    }

    /// <summary>Bewaart de huidige positie/grootte van dit venster. Roep dit aan in de
    /// Closing-handler.</summary>
    public static void Bewaren(Window venster, string sleutel)
    {
        var g = new Geometrie
        {
            Left = venster.WindowState == WindowState.Normal ? venster.Left : venster.RestoreBounds.Left,
            Top = venster.WindowState == WindowState.Normal ? venster.Top : venster.RestoreBounds.Top,
            Width = venster.WindowState == WindowState.Normal ? venster.Width : venster.RestoreBounds.Width,
            Height = venster.WindowState == WindowState.Normal ? venster.Height : venster.RestoreBounds.Height,
            Maximized = venster.WindowState == WindowState.Maximized
        };
        Laad()[sleutel] = g;
        BewaarNaarSchijf();
    }
}
