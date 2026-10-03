using System.Windows.Media;

namespace Modeltreinbesturing;

/// <summary>
/// Eén gedeeld kleurenschema voor alle vensters, gebaseerd op Koploper's eigen
/// kleurcategorieën uit de handleiding (Instellingen per database, tabblad 'kleuren',
/// p. 279): Vrije lijn, Gereserveerd, Geselecteerd, Blok bezet, Wisselpoot, etc.
/// Bewust sober gehouden (geen kleur-per-trein-confetti) - dat is niet hoe Koploper
/// zelf treinen/blokken laat zien.
///
/// Niet meer readonly: de echte Koploper laat dit kleurenschema per database aanpassen
/// (zie KleurenschemaDialog), dus deze velden kunnen tijdens het draaien wijzigen. Elk
/// venster luistert naar SchemaGewijzigd om zichzelf dan opnieuw te tekenen.
/// </summary>
public static class KoploperKleuren
{
    public static event Action? SchemaGewijzigd;
    public static void MeldWijziging() => SchemaGewijzigd?.Invoke();

    public static Brush Vrij = Brushes.Gainsboro;
    public static Brush VrijBlokMarkering = Brushes.White;
    public static Brush SpoorLijn = Brushes.Navy;
    public static Brush Perron = Brushes.Yellow;
    public static Brush Bezet = Brushes.Firebrick;
    public static Brush HandmatigBezet = Brushes.Sienna;
    public static Brush Staartindicatie = Brushes.LightCoral;
    public static Brush Foutmelding = Brushes.Purple;
    public static Brush Vergrendeld = Brushes.DimGray;
    public static Brush Gereserveerd = Brushes.DarkGoldenrod;
    public static Brush NetVrijgegeven = Brushes.ForestGreen;
    public static Brush Geselecteerd = Brushes.SteelBlue;
    public static Brush WisselActievePoot = Brushes.Navy;
    public static Brush WisselAfbuigendePoot = Brushes.Cyan;
    public static Brush WisselNietActievePoot = Brushes.LightGray;

    /// <summary>Kleur van de ACTIEVE stand (welke van de twee poten dat ook is) als de
    /// wissel NIET gereserveerd/bezet is - "in ruste", net als het echte Koploper: je ziet
    /// in één oogopslag hoe de wissel staat, ook zonder dat er een trein onderweg is.</summary>
    public static Brush WisselInRuste = Brushes.LightSkyBlue;
    public static Brush SeinRood = Brushes.Firebrick;
    public static Brush SeinGeel = Brushes.DarkGoldenrod;
    public static Brush SeinGroen = Brushes.ForestGreen;
    public static Brush TekstOpDonkereAchtergrond = Brushes.White;
    public static Brush TekstOpLichteAchtergrond = Brushes.Black;
}
