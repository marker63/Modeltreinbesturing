using System.Windows;
using Modeltreinbesturing.Model;

namespace Modeltreinbesturing;

public record BaanControleMelding(string Ernst, string Omschrijving);

/// <summary>
/// "Controleer mijn baan" - scant op veelvoorkomende configuratiefouten, vooral waardevol
/// voor nieuwe (open-source-)gebruikers die zelf een baan opbouwen en niet meteen doorhebben
/// waarom een route niet start of een blok nooit bereikt wordt. Puur SIGNALEREND - lost
/// niets automatisch op, laat de gebruiker zelf beoordelen en corrigeren.
/// </summary>
public class BaanControleBeheerder
{
    public List<BaanControleMelding> VoerControleUit(BlokBeheerder blokBeheerder, BaanOntwerpBeheerder baanBeheerder, TreinrouteBeheerder routeBeheerder)
    {
        var meldingen = new List<BaanControleMelding>();

        foreach (var blok in blokBeheerder.Blokken)
        {
            if (blok.Bezetmeldpunten.Count == 0)
                meldingen.Add(new BaanControleMelding("Waarschuwing", $"Blok {blok.Nummer} heeft geen enkel bezetmeldpunt - de simulatie kan dan niet weten wanneer een trein hier aankomt/vertrekt."));

            bool heeftUitgang = blokBeheerder.VolgendeBlokken(blok).Any();
            bool heeftIngang = blokBeheerder.Relaties.Any(r => r.Naar == blok);
            if (!heeftUitgang && !heeftIngang && blok.Type != BlokType.Opstelspoor)
                meldingen.Add(new BaanControleMelding("Waarschuwing", $"Blok {blok.Nummer} heeft geen enkele relatie (geen in- of uitgaande verbinding) - dit blok is voor de simulatie volledig geïsoleerd."));
        }

        foreach (var wissel in baanBeheerder.Symbolen.OfType<Wissel>())
        {
            bool verankerd = baanBeheerder.Symbolen.OfType<Lijn>().Any(l => l.PuntWisselAnkers.Contains(wissel));
            if (!verankerd)
            {
                meldingen.Add(new BaanControleMelding("Waarschuwing", $"Wissel {wissel.Adres} is niet aan enige lijn verankerd - staat los op de tekening, rijdt de simulatie er niet automatisch overheen."));
                continue;
            }

            // Subtielere variant: een wissel met SOMMIGE (maar niet alle 3) poten
            // verankerd - vaak het gevolg van een lijnpunt dat bij het verslepen NET
            // niet binnen de (bewust kleine) ankersnap-afstand viel: ziet er op de
            // tekening verbonden uit, maar heeft in werkelijkheid geen echte
            // ankerverbinding. Precies dit soort "bijna maar net niet"-fout maakt dat
            // een wissel niet meekleurt bij een reservering en/of niet automatisch
            // omgezet wordt - lastig zelf te zien, wél goed te detecteren door te
            // kijken of er een NIET-verankerd lijnpunt vlak bij een niet-verankerde
            // wisselpoot ligt.
            var wisselpunten = BaanOntwerpBeheerder.WisselAnkerpunten(wissel).ToList();
            string[] pootNamen = { "instroom", "rechtdoor", "afbuigend" };
            for (int i = 0; i < wisselpunten.Count; i++)
            {
                bool dezePootVerankerd = baanBeheerder.Symbolen.OfType<Lijn>()
                    .Any(l => l.PuntWisselAnkers.Select((w, j) => (w, j)).Any(t => t.w == wissel && t.j < l.Punten.Count && (l.Punten[t.j] - wisselpunten[i]).Length < 1));
                if (dezePootVerankerd) continue;

                var nabijLosPunt = baanBeheerder.Symbolen.OfType<Lijn>()
                    .SelectMany(l => l.Punten.Select((p, j) => (Lijn: l, Punt: p, Index: j)))
                    .Where(t => t.Index < t.Lijn.PuntWisselAnkers.Count && t.Index < t.Lijn.PuntBlokAnkers.Count && t.Index < t.Lijn.PuntLijnAnkers.Count)
                    .Where(t => t.Lijn.PuntWisselAnkers[t.Index] is null && t.Lijn.PuntBlokAnkers[t.Index] is null && t.Lijn.PuntLijnAnkers[t.Index] is null)
                    .Where(t => (t.Punt - wisselpunten[i]).Length < 30)
                    .FirstOrDefault();
                if (nabijLosPunt.Lijn != null)
                    meldingen.Add(new BaanControleMelding("Waarschuwing", $"Wissel {wissel.Adres}: de {pootNamen[i]}-poot lijkt NIET echt verankerd - er ligt een los lijnpunt vlak in de buurt (lijn {nabijLosPunt.Lijn.Volgnummer}) dat er visueel wel bij lijkt te horen, maar niet daadwerkelijk vastzit. Sleep dat eindpunt (in Verplaatsen) nog even iets dichterbij tot het echt vastklikt."));
            }
        }

        foreach (var route in routeBeheerder.Treinroutes)
        {
            var pad = routeBeheerder.BerekenVolledigPad(route, blokBeheerder);
            if (pad.Count == 1 && blokBeheerder.VolgendeBlokken(route.Startblok).Any())
                meldingen.Add(new BaanControleMelding("Info", $"Route '{route.Omschrijving}' (start blok {route.Startblok.Nummer}) breekt meteen af - controleer de vastgelegde Keuzeblokken of eventuele Richtingsverboden."));
            if (route.Bestemmingsblok != null && pad[^1] != route.Bestemmingsblok)
                meldingen.Add(new BaanControleMelding("Waarschuwing", $"Route '{route.Omschrijving}' komt uit bij blok {pad[^1].Nummer}, niet bij het vastgelegde bestemmingsblok {route.Bestemmingsblok.Nummer} - de route zal daardoor nooit daadwerkelijk kunnen rijden."));
        }

        // Dienstregelingsconflict: twee routes met dezelfde geplande vertrektijd vanaf
        // hetzelfde startblok kunnen NOOIT allebei daadwerkelijk starten (het blok is dan
        // al bezet door de eerste) - dat merk je anders pas als het misgaat.
        var routesMetTijd = routeBeheerder.Treinroutes.Where(r => r.GeplandeVertrektijd != null).ToList();
        foreach (var groep in routesMetTijd.GroupBy(r => (r.GeplandeVertrektijd, r.Startblok)))
        {
            if (groep.Count() < 2) continue;
            var namen = string.Join("', '", groep.Select(r => r.Omschrijving));
            meldingen.Add(new BaanControleMelding("Waarschuwing", $"Dienstregelingsconflict: '{namen}' hebben allemaal dezelfde geplande vertrektijd ({groep.Key.GeplandeVertrektijd:hh\\:mm}) vanaf hetzelfde startblok {groep.Key.Startblok.Nummer} - deze kunnen nooit allemaal daadwerkelijk starten."));
        }

        return meldingen;
    }
}
