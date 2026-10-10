using Modeltreinbesturing.Model;

namespace Modeltreinbesturing;

/// <summary>
/// BUG #56 (vervolg): na een baankaart-import krijgt elk NIEUW blok een eigen automatische route
/// (Startblok = dat blok, Automatisch = true: de trein kiest zelf het eerste vrije vervolgblok).
/// Zo kan een nieuwe baan meteen rijden zonder dat iemand routes hoeft te maken.
/// Bestaande routes worden nooit aangepast of vervangen. Er wordt GEEN vertrektijd verzonnen
/// (GeplandeVertrektijd blijft leeg): een dienstregeling met tijden is een keuze van de gebruiker.
/// </summary>
public class DienstAanvuller
{
    public class Resultaat
    {
        public List<Treinroute> Toegevoegd { get; } = new();
        public List<string> Meldingen { get; } = new();
    }

    public Resultaat Vul(IEnumerable<Blok> nieuweBlokken, TreinrouteBeheerder routes, BlokBeheerder blokken)
    {
        var res = new Resultaat();
        foreach (var blok in nieuweBlokken.OrderBy(b => b.Nummer))
        {
            if (blok.Type == BlokType.Kopspoor && !blokken.Relaties.Any(r => r.Van == blok))
            {
                // Een kopspoor zonder uitgaande relatie kan niet vertrekken: geen route, wel melding.
                res.Meldingen.Add($"Blok {blok.Nummer}: geen route aangemaakt (geen uitgaande relatie).");
                continue;
            }
            if (routes.Treinroutes.Any(r => r.Startblok == blok))
            {
                res.Meldingen.Add($"Blok {blok.Nummer}: heeft al een route, niet aangepast.");
                continue;
            }
            var route = routes.NieuweTreinroute(blok, $"Automatisch vanaf blok {blok.Nummer} (Baanverkenner)");
            route.Automatisch = true;
            route.Info = "Automatisch aangemaakt door de baankaart-import. Geen vertrektijd ingesteld.";
            res.Toegevoegd.Add(route);
        }
        if (res.Toegevoegd.Count > 0)
            res.Meldingen.Add($"{res.Toegevoegd.Count} automatische route(s) aangemaakt zonder vertrektijd; stel zelf tijden in als je een dienstregeling wilt.");
        return res;
    }

    public void Herstel(Resultaat res, TreinrouteBeheerder routes)
    {
        foreach (var r in res.Toegevoegd) routes.VerwijderTreinroute(r);
    }
}
