namespace Baanverkenner.Kern.Simulatie;

/// <summary>
/// Kant-en-klare proefbaan om de Baanverkenner zonder echte baan te proberen:
///
///   Ovaal 1-2-3-4-5-6 met:
///   - inhaalspoor 7 tussen wissel 1 (splitsend na melder 2) en wissel 2 (samenvoegend vóór 4)
///   - kopspoor-tak na melder 5 via wissel 5 naar 8, daarin wissel 14 (van achteren) naar
///     9 (stootjuk) en zijspoor 11 (stootjuk) - 11 is alleen bereikbaar door terug te zetten
///   - zijspoor 10 via wissel 9 (ongepolariseerd puntstuk) tussen 6 en 1, bereikbaar
///     vanuit melder 1 achteruit
///   - adres 7 is een sein: geen effect op de rijweg
///
/// De testloc (adres 3) staat op melder 1, vooruit = richting melder 2.
/// </summary>
public static class DemoBaan
{
    public static SimulatieBaan Maak(bool dinamoGedrag)
    {
        var b = new SimulatieBaan { DinamoGedrag = dinamoGedrag, LocAdres = 3 };
        var E = SimulatieBaan.Eind.A;
        var F = SimulatieBaan.Eind.B;

        var s1 = b.NieuweSectie(1, 90, 1);
        var s2 = b.NieuweSectie(2, 70, 1);
        var s3 = b.NieuweSectie(3, 110, 2);
        var s7 = b.NieuweSectie(7, 110, 3);
        var s4 = b.NieuweSectie(4, 80, 4);
        var s5 = b.NieuweSectie(5, 60, 4);
        var s6 = b.NieuweSectie(6, 120, 5);
        var s8 = b.NieuweSectie(8, 70, 6);
        var s9 = b.NieuweSectie(9, 60, 6);
        var s11 = b.NieuweSectie(11, 50, 7);
        var s10 = b.NieuweSectie(10, 60, 8);

        b.Verbind(s1, F, s2, E);

        var w1 = b.NieuweWissel(1, gepolariseerd: true, blok: 1);
        b.Verbind(w1, SimulatieBaan.Poort.Stam, s2, F);
        b.Verbind(w1, SimulatieBaan.Poort.Recht, s3, E);
        b.Verbind(w1, SimulatieBaan.Poort.Af, s7, E);

        var w2 = b.NieuweWissel(2, gepolariseerd: true, blok: 4);
        b.Verbind(w2, SimulatieBaan.Poort.Recht, s3, F);
        b.Verbind(w2, SimulatieBaan.Poort.Af, s7, F);
        b.Verbind(w2, SimulatieBaan.Poort.Stam, s4, E);

        b.Verbind(s4, F, s5, E);

        var w5 = b.NieuweWissel(5, gepolariseerd: true, blok: 4);
        b.Verbind(w5, SimulatieBaan.Poort.Stam, s5, F);
        b.Verbind(w5, SimulatieBaan.Poort.Recht, s6, E);
        b.Verbind(w5, SimulatieBaan.Poort.Af, s8, E);

        var w14 = b.NieuweWissel(14, gepolariseerd: true, blok: 6);
        b.Verbind(w14, SimulatieBaan.Poort.Recht, s8, F);
        b.Verbind(w14, SimulatieBaan.Poort.Af, s11, E);
        b.Verbind(w14, SimulatieBaan.Poort.Stam, s9, E);

        var w9 = b.NieuweWissel(9, gepolariseerd: false, blok: 1);
        b.Verbind(w9, SimulatieBaan.Poort.Recht, s6, F);
        b.Verbind(w9, SimulatieBaan.Poort.Af, s10, E);
        b.Verbind(w9, SimulatieBaan.Poort.Stam, s1, E);

        b.PlaatsLoc(s1, 45, +1);
        return b;
    }
}
