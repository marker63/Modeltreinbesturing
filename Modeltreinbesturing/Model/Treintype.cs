namespace Modeltreinbesturing.Model;

/// <summary>
/// Een treintype, zoals in Koploper's "Onderhouden treintypes" - bepaalt de snelheden
/// waarmee een trein van dit type rijdt. Dit is het fundament waarop later de
/// "Onderhouden variabele treinroute"-regels (Stamgegevens: "Geldt voor: Treintype")
/// gebouwd kunnen worden.
/// </summary>
public class Treintype
{
    public string Omschrijving { get; set; } = "";

    public double MaxSnelheid { get; set; } = 100;
    public double GemiddeldeSnelheid { get; set; } = 70;
    public double MinimumSnelheid { get; set; } = 20;

    /// <summary>Massa simulatie: hoeveel seconden dit treintype nodig heeft om van
    /// stilstand naar 50 km/u te versnellen.</summary>
    public double AccelaratieVan0Naar50Seconden { get; set; } = 8;

    /// <summary>Massa simulatie: hoeveel seconden dit treintype nodig heeft om van
    /// 50 km/u tot stilstand te remmen.</summary>
    public double RemVan50Naar0Seconden { get; set; } = 4;

    /// <summary>Wachttijd bij keren van de trein, in seconden.</summary>
    public double WachttijdBijKerenSeconden { get; set; } = 10;

    /// <summary>Vertrekvertraging nadat het sein op groen springt, in seconden.</summary>
    public double VertrekvertragingSeconden { get; set; } = 2;

    /// <summary>Stopkans (%) op een gewoon (Normaal-type) blok, los van een Station -
    /// zoals in Koploper's "Onderhouden treintype/bloktype": per treintype én per bloktype
    /// een stopkans en een minimale/maximale wachttijd instelbaar, bijv. een goederentrein
    /// die soms willekeurig even stilstaat op een doorgaand spoor. 0 = nooit (standaard,
    /// matcht Koploper's eigen "bij Normaal blok, het standaardblok, zijn geen wachttijden
    /// voorzien"). Bij een treffer wordt een willekeurige duur tussen Min/MaxWachttijd
    /// aangehouden.
    /// LET OP: deze 3 velden zijn de OORSPRONKELIJKE, Normaal-blok-specifieke instelling
    /// (bewaard voor bestaande projecten/backward compatibility) - de volledige, per-
    /// bloktype matrix zit in BloktypeGedrag hieronder, wat het echte Koploper-scherm
    /// "Onderhouden gegevens per treintype/bloktype" completer nabootst (ook Station/
    /// Kopspoor/Opstelspoor, niet alleen Normaal). Zie BloktypeGedragVoor().</summary>
    public double StopkansPercentBijNormaalBlok { get; set; }
    public double MinWachttijdBijStopSeconden { get; set; } = 5;
    public double MaxWachttijdBijStopSeconden { get; set; } = 20;

    /// <summary>De volledige "treintype per bloktype"-matrix - per bloktype een eigen
    /// stopkans/wachttijd. Ontbreekt een bloktype hierin (bijv. bij een ouder, nog niet
    /// aangepast treintype), dan geldt de terugvalwaarde uit BloktypeGedragVoor().</summary>
    public List<TreintypeBloktypeGedrag> BloktypeGedrag { get; set; } = new();

    /// <summary>Effectieve stopkans/wachttijd voor dit bloktype: gebruikt een expliciete
    /// BloktypeGedrag-regel als die bestaat, anders een zinnige terugvalwaarde - Station
    /// stopt standaard altijd (matcht het oude, vaste "elke trein stopt op elk station"-
    /// gedrag van vóór deze functie), Normaal valt terug op de oude StopkansPercentBij-
    /// NormaalBlok-velden hierboven (voor bestaande projecten), Kopspoor/Opstelspoor
    /// hebben standaard geen extra willekeurige stop (Kopspoor heeft al zijn eigen,
    /// altijd-afgedwongen WachttijdBijKeren, een apart concept).</summary>
    public (double StopkansPercent, double MinWachttijd, double MaxWachttijd) BloktypeGedragVoor(BlokType blokType)
    {
        var expliciet = BloktypeGedrag.FirstOrDefault(g => g.BlokType == blokType);
        if (expliciet != null) return (expliciet.StopkansPercent, expliciet.MinWachttijdSeconden, expliciet.MaxWachttijdSeconden);

        return blokType switch
        {
            BlokType.Station => (100, 2, 5),
            BlokType.Normaal => (StopkansPercentBijNormaalBlok, MinWachttijdBijStopSeconden, MaxWachttijdBijStopSeconden),
            _ => (0, 0, 0)
        };
    }

    public override string ToString() => Omschrijving;
}
