namespace Modeltreinbesturing.Model;

/// <summary>
/// Laag 1: een rij-relatie tussen twee blokken. Betekent: er kan van
/// "Van" naar "Naar" gereden worden. Een relatie is eenrichtingsverkeer;
/// voor heen-en-weer pendelen leg je twee relaties vast.
/// </summary>
public class BlokRelatie
{
    public required Blok Van { get; set; }
    public required Blok Naar { get; set; }

    /// <summary>Koploper's "uit-via-naar"-principe (letterlijk van de ontwikkelaar: "de
    /// basis van het gehele systeem"): elke overgang heeft een eigen "keer loc"-vinkje dat
    /// bepaalt of de loc bij DEZE SPECIFIEKE overgang fysiek van rijrichting moet wisselen
    /// (bijv. terugrijden naar hetzelfde blok waar hij vandaan kwam, i.p.v. rechtdoor
    /// verder rijden). Standaard false (geen wijziging aan bestaand gedrag) - alleen op
    /// relaties waar dit écht zo is (bijv. een normaal blok waar de trein weer terug moet)
    /// zou je dit aanzetten. Krijgt dezelfde wachttijd-behandeling als een Kopspoor
    /// (WachttijdBijKeren), aangezien onze simulator geen aparte, persistente rijrichting
    /// per loc bijhoudt - dit is dus een bewust vereenvoudigde, additieve aanpak i.p.v. een
    /// volledige uit-via-naar-herbouw van het hele relatiemodel.</summary>
    public bool Keer { get; set; }

    /// <summary>Koploper's "Kans"-kolom (Richtingen-tabblad): relatief gewicht voor de
    /// willekeurige routekeuze bij vrij automatisch rijden (KiesOptimaleKandidaat) wanneer
    /// er geen andere, dwingende voorkeur is (bijv. een passende stopsectie-lengte) - een
    /// relatie met Kans=2 wordt twee keer zo vaak gekozen als een relatie met Kans=1 tussen
    /// dezelfde twee alternatieven. Standaard 1 (alle richtingen even waarschijnlijk, het
    /// oude gedrag vóór deze uitbreiding). Een relatie die niet expliciet is vastgelegd
    /// (LegRelatieVast nooit aangeroepen voor dat Van/Naar-paar) telt ook als gewicht 1.</summary>
    public int Kans { get; set; } = 1;

    /// <summary>BUG #49: de VASTGELEGDE rijrichting bij rijden van Van naar Naar, in het
    /// blokkenframe van de baan (true = vooruit, false = achteruit, null = niet vastgelegd).
    /// Gebruiker: "reserveren van blok 3 naar blok 7 is rijrichting vooruit, 3 naar 4 is
    /// achteruit - dat is echt essentieel, zeker bij de eerste rit na het opstarten". Zonder
    /// dit veld kende de software alleen een standaardrichting + Keer-vinkjes en kon een rit
    /// vanaf een blok met twee uitgangen de verkeerde kant op vertrekken. Bij de eerste stap
    /// van een rit bepaalt dit veld de richting; later wordt een afwijking met het lopende
    /// Keer-mechanisme gelogd.</summary>
    public bool? RijrichtingVooruit { get; set; }

    public override string ToString()
    {
        string basis = $"{Van.Nummer} -> {Naar.Nummer}";
        if (Keer) basis += " (keer)";
        if (RijrichtingVooruit is bool rr) basis += rr ? " [vooruit]" : " [achteruit]";
        if (Kans != 1) basis += $" [kans {Kans}]";
        return basis;
    }
}
