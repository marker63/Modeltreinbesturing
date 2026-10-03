namespace Modeltreinbesturing.Model;

/// <summary>
/// Eén regel in Koploper's "Onderhouden gegevens per treintype/bloktype": voor DIT
/// treintype (eigenaar is Treintype.BloktypeGedrag), hoe gedraagt een trein zich bij DIT
/// bloktype - stopkans (0-100%) en de minimale/maximale wachttijd bij zo'n stop. Precies
/// het echte Koploper-voorbeeld: "Stoptrein"+"Stationsblok" op stopkans 100%/10-30 sec,
/// maar een goederendieseltrein op stopkans 0% bij Stationsblok zodat hij daar gewoon
/// doorrijdt zonder te stoppen.
/// </summary>
public class TreintypeBloktypeGedrag
{
    public BlokType BlokType { get; set; }
    public double StopkansPercent { get; set; }
    public double MinWachttijdSeconden { get; set; } = 5;
    public double MaxWachttijdSeconden { get; set; } = 20;
}
