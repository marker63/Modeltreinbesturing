using System.Reflection;
using System.Windows;
using System.Windows.Media;

namespace Modeltreinbesturing;

/// <summary>Eén selecteerbare kleur in het palet - alleen Naam/Kleur, gebruikt in de
/// ComboBox-itemtemplate.</summary>
public class PaletKleur
{
    public required string Naam { get; init; }
    public required Brush Kleur { get; init; }
    public override bool Equals(object? obj) => obj is PaletKleur ander && ander.Naam == Naam;
    public override int GetHashCode() => Naam.GetHashCode();
}

/// <summary>Eén regel in de kleurenschema-lijst: een categorie (bijv. "Bezet") met een
/// palet om uit te kiezen. HuidigeKleur past de wijziging DIRECT toe zodra 'm gezet wordt
/// (via de gegeven Toepassen-actie) en meldt het aan alle vensters.</summary>
public class KleurCategorie
{
    public required string Naam { get; init; }
    public required List<PaletKleur> Palet { get; init; }
    public required Action<Brush> Toepassen { get; init; }

    private PaletKleur? _huidigeKleur;
    public PaletKleur? HuidigeKleur
    {
        get => _huidigeKleur;
        set
        {
            if (value is null || ReferenceEquals(value, _huidigeKleur)) { _huidigeKleur = value; return; }
            _huidigeKleur = value;
            Toepassen(value.Kleur);
            KoploperKleuren.MeldWijziging();
        }
    }
}

public partial class KleurenschemaDialog : Window
{
    private static List<PaletKleur>? _gedeeldPalet;

    public KleurenschemaDialog()
    {
        InitializeComponent();
        CategorieenLijst.ItemsSource = BouwCategorieen();
    }

    /// <summary>Bouwt het palet één keer op uit alle standaard WPF-kleuren (Colors-klasse via
    /// reflectie), zodat elke categorie uit dezelfde ruime, herkenbare kleurenset kan kiezen.</summary>
    private static List<PaletKleur> Palet()
    {
        if (_gedeeldPalet != null) return _gedeeldPalet;
        _gedeeldPalet = typeof(Colors).GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(p => p.PropertyType == typeof(Color))
            .OrderBy(p => p.Name)
            .Select(p => new PaletKleur { Naam = p.Name, Kleur = new SolidColorBrush((Color)p.GetValue(null)!) })
            .ToList();
        return _gedeeldPalet;
    }

    private static PaletKleur VindOfMaak(Brush huidige)
    {
        var palet = Palet();
        string? kleurNaam = (huidige as SolidColorBrush)?.Color.ToString();
        var gevonden = palet.FirstOrDefault(p => ((SolidColorBrush)p.Kleur).Color.ToString() == kleurNaam);
        return gevonden ?? new PaletKleur { Naam = "(aangepast)", Kleur = huidige };
    }

    private List<KleurCategorie> BouwCategorieen()
    {
        var lijst = new List<KleurCategorie>
        {
            Categorie("Vrij", () => KoploperKleuren.Vrij, b => KoploperKleuren.Vrij = b),
            Categorie("Bezet", () => KoploperKleuren.Bezet, b => KoploperKleuren.Bezet = b),
            Categorie("Gereserveerd", () => KoploperKleuren.Gereserveerd, b => KoploperKleuren.Gereserveerd = b),
            Categorie("Net vrijgegeven", () => KoploperKleuren.NetVrijgegeven, b => KoploperKleuren.NetVrijgegeven = b),
            Categorie("Geselecteerd", () => KoploperKleuren.Geselecteerd, b => KoploperKleuren.Geselecteerd = b),
            Categorie("Handmatig bezet", () => KoploperKleuren.HandmatigBezet, b => KoploperKleuren.HandmatigBezet = b),
            Categorie("Staartindicatie", () => KoploperKleuren.Staartindicatie, b => KoploperKleuren.Staartindicatie = b),
            Categorie("Foutmelding", () => KoploperKleuren.Foutmelding, b => KoploperKleuren.Foutmelding = b),
            Categorie("Spoorlijn", () => KoploperKleuren.SpoorLijn, b => KoploperKleuren.SpoorLijn = b),
            Categorie("Perron", () => KoploperKleuren.Perron, b => KoploperKleuren.Perron = b),
            Categorie("Wissel actieve poot", () => KoploperKleuren.WisselActievePoot, b => KoploperKleuren.WisselActievePoot = b),
            Categorie("Wissel afbuigende poot", () => KoploperKleuren.WisselAfbuigendePoot, b => KoploperKleuren.WisselAfbuigendePoot = b),
            Categorie("Sein rood", () => KoploperKleuren.SeinRood, b => KoploperKleuren.SeinRood = b),
            Categorie("Sein geel", () => KoploperKleuren.SeinGeel, b => KoploperKleuren.SeinGeel = b),
            Categorie("Sein groen", () => KoploperKleuren.SeinGroen, b => KoploperKleuren.SeinGroen = b),
        };
        return lijst;
    }

    private static KleurCategorie Categorie(string naam, Func<Brush> lees, Action<Brush> schrijf)
    {
        var categorie = new KleurCategorie { Naam = naam, Palet = Palet(), Toepassen = schrijf };
        categorie.HuidigeKleur = VindOfMaak(lees());
        return categorie;
    }

    private void Standaard_Click(object sender, RoutedEventArgs e)
    {
        KoploperKleuren.Vrij = Brushes.Gainsboro;
        KoploperKleuren.VrijBlokMarkering = Brushes.White;
        KoploperKleuren.SpoorLijn = Brushes.Navy;
        KoploperKleuren.Perron = Brushes.Yellow;
        KoploperKleuren.Bezet = Brushes.Firebrick;
        KoploperKleuren.HandmatigBezet = Brushes.Sienna;
        KoploperKleuren.Staartindicatie = Brushes.LightCoral;
        KoploperKleuren.Foutmelding = Brushes.Purple;
        KoploperKleuren.Gereserveerd = Brushes.DarkGoldenrod;
        KoploperKleuren.Geselecteerd = Brushes.SteelBlue;
        KoploperKleuren.WisselActievePoot = Brushes.Navy;
        KoploperKleuren.WisselAfbuigendePoot = Brushes.Cyan;
        KoploperKleuren.WisselNietActievePoot = Brushes.LightGray;
        KoploperKleuren.SeinRood = Brushes.Firebrick;
        KoploperKleuren.SeinGeel = Brushes.DarkGoldenrod;
        KoploperKleuren.SeinGroen = Brushes.ForestGreen;
        KoploperKleuren.MeldWijziging();
        CategorieenLijst.ItemsSource = null;
        CategorieenLijst.ItemsSource = BouwCategorieen();
    }

    /// <summary>Het Okabe-Ito-palet - een wetenschappelijk onderbouwde, wijdverspreid
    /// aanbevolen kleurenset die ook voor de meest voorkomende vormen van kleurenblindheid
    /// (rood-groen) goed te onderscheiden blijft. Vervangt vooral het Bezet/NetVrijgegeven-
    /// paar (voorheen rood/groen, de klassieke lastig-te-onderscheiden combinatie) door een
    /// oranjerood/blauwgroen-paar. Foutmelding/SpoorLijn/WisselAfbuigendePoot blijven
    /// ongewijzigd - die zitten al niet op de rood-groen-as en zijn dus al prima te zien.</summary>
    private void Kleurenblind_Click(object sender, RoutedEventArgs e)
    {
        KoploperKleuren.Bezet = new SolidColorBrush(Color.FromRgb(0xD5, 0x5E, 0x00));           // vermiljoen
        KoploperKleuren.NetVrijgegeven = new SolidColorBrush(Color.FromRgb(0x00, 0x9E, 0x73));  // blauwgroen
        KoploperKleuren.Gereserveerd = new SolidColorBrush(Color.FromRgb(0xF0, 0xE4, 0x42));    // geel
        KoploperKleuren.Geselecteerd = new SolidColorBrush(Color.FromRgb(0x56, 0xB4, 0xE9));    // hemelsblauw
        KoploperKleuren.HandmatigBezet = new SolidColorBrush(Color.FromRgb(0xCC, 0x79, 0xA7));  // roodpaars
        KoploperKleuren.Staartindicatie = new SolidColorBrush(Color.FromRgb(0xE6, 0x9F, 0x00)); // oranje
        KoploperKleuren.SeinRood = new SolidColorBrush(Color.FromRgb(0xD5, 0x5E, 0x00));
        KoploperKleuren.SeinGeel = new SolidColorBrush(Color.FromRgb(0xF0, 0xE4, 0x42));
        KoploperKleuren.SeinGroen = new SolidColorBrush(Color.FromRgb(0x00, 0x9E, 0x73));
        KoploperKleuren.MeldWijziging();
        CategorieenLijst.ItemsSource = null;
        CategorieenLijst.ItemsSource = BouwCategorieen();
    }

    private void Sluiten_Click(object sender, RoutedEventArgs e) => Close();
}
