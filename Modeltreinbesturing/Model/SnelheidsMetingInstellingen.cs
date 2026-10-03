namespace Modeltreinbesturing.Model;

/// <summary>Koploper: "heen-en-weer rijden op het meettraject" of "een rondje rijden waarbij
/// de meting maar in 1 rijrichting wordt gedaan".</summary>
public enum ManierVanMeten { HeenEnWeer, EenRondje }

/// <summary>
/// Koploper's "Algemeen -> Instellingen per database -> Snelheid/Bestemming ->
/// Snelheidsmeting": de configuratie van het meettraject voor het ijken van
/// locomotiefsnelheden - welke twee bezetmelders de grenzen van het meettraject vormen, de
/// exacte afstand ertussen (inclusief de lengte van alle secties ertussen), en de
/// modelschaal om de gemeten tijd om te rekenen naar een echte schaalsnelheid.
/// </summary>
public class SnelheidsMetingInstellingen
{
    public int MeldernNummer1 { get; set; }
    public int MeldernNummer2 { get; set; }

    /// <summary>Exacte afstand tussen de twee meldpunten (mm), inclusief de lengte van
    /// eventuele tussenliggende secties.</summary>
    public double LengteTrajectMm { get; set; }

    /// <summary>Modelschaal als noemer van de verhouding, bijv. 87 voor 1:87 (H0) of 160
    /// voor 1:160 (N) - wordt gebruikt om de gemeten modelsnelheid om te rekenen naar een
    /// echte schaalsnelheid in km/u.</summary>
    public double Modelschaal { get; set; } = 87;

    public ManierVanMeten ManierVanMeten { get; set; } = ManierVanMeten.HeenEnWeer;
}
