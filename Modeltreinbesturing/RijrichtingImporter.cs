using Baanverkenner.Kern;
using Modeltreinbesturing.Model;

namespace Modeltreinbesturing;

/// <summary>
/// BUG #53: haalt uit een Baanverkenner-baankaart de rijrichting (vooruit/achteruit) per
/// blokrelatie, zodat BlokRelatie.RijrichtingVooruit (BUG #49) niet met de hand ingevuld
/// hoeft te worden. Regels (afspraken Marco): NOOIT een al vastgelegde richting overschrijven
/// (alleen leeg invullen, afwijkingen alleen melden); alleen ondubbelzinnige overgangen worden
/// voorgesteld (melders die in meerdere blokken staan, zoals melder 24, geven een melding
/// "onzeker" en worden niet toegepast).
/// </summary>
public class RijrichtingImporter
{
    public record Voorstel(BlokRelatie Relatie, bool Vooruit, int Aantal);

    public class Resultaat
    {
        public List<Voorstel> Invullen { get; } = new();
        public List<BaanControleMelding> Meldingen { get; } = new();
        /// <summary>true = de verkenner-richting is omgedraaid (test-loc met omgekeerde polariteit) op grond van al vastgelegde relaties.</summary>
        public bool Omgedraaid { get; set; }
    }

    public Resultaat Bepaal(Baanverkenner.Kern.Baankaart kaart, BlokBeheerder blokBeheerder)
    {
        var res = new Resultaat();
        var blokkenPerMelder = new Dictionary<int, List<Blok>>();
        foreach (var blok in blokBeheerder.Blokken)
            foreach (var m in blok.Bezetmeldpunten)
            {
                if (m.MeldernNummer <= 0) continue;
                if (!blokkenPerMelder.TryGetValue(m.MeldernNummer, out var l)) blokkenPerMelder[m.MeldernNummer] = l = new List<Blok>();
                if (!l.Contains(blok)) l.Add(blok);
            }

        // relatie -> (aantal vooruit, aantal achteruit) zoals de verkenner reed (ongecorrigeerd)
        var tellers = new Dictionary<BlokRelatie, (int vooruit, int achteruit)>();
        foreach (var o in kaart.Overgangen)
        {
            if (!blokkenPerMelder.TryGetValue(o.Van, out var van) || !blokkenPerMelder.TryGetValue(o.Naar, out var naar)) continue;
            if (van.Intersect(naar).Any()) continue; // binnen één blok
            var kandidaten = new List<BlokRelatie>();
            foreach (var a in van) foreach (var b in naar)
            {
                if (a == b) continue;
                var r = blokBeheerder.Relaties.FirstOrDefault(x => x.Van == a && x.Naar == b);
                if (r != null) kandidaten.Add(r);
            }
            if (kandidaten.Count == 0) continue;
            if (kandidaten.Count > 1 || van.Count > 1 || naar.Count > 1)
            {
                res.Meldingen.Add(new BaanControleMelding("Info",
                    $"Overgang melder {o.Van} -> {o.Naar} ({o.Richting.Tekst()}, {o.AantalKeer}x) is NIET ondubbelzinnig te koppelen aan één blokrelatie ({string.Join(", ", kandidaten.Select(k => $"{k.Van.Nummer}->{k.Naar.Nummer}"))}; gedeelde melder) - niet automatisch ingevuld. Vul de rijrichting van deze relatie(s) handmatig in."));
                continue;
            }
            var rel = kandidaten[0];
            tellers.TryGetValue(rel, out var t);
            if (o.Richting == Richting.Vooruit) t.vooruit += Math.Max(1, o.AantalKeer); else t.achteruit += Math.Max(1, o.AantalKeer);
            tellers[rel] = t;
        }

        // Is de test-loc van de verkenner omgekeerd t.o.v. het blokkenframe? Alleen vast te stellen
        // aan relaties waarvan Marco de richting al heeft vastgelegd.
        int zelfde = 0, tegen = 0;
        foreach (var (rel, t) in tellers)
        {
            if (rel.RijrichtingVooruit is not bool vast) continue;
            if (t.vooruit == t.achteruit) continue;
            bool verkenner = t.vooruit > t.achteruit;
            if (verkenner == vast) zelfde++; else tegen++;
        }
        bool omdraaien = tegen > 0 && zelfde == 0;
        res.Omgedraaid = omdraaien;
        if (tegen > 0 && zelfde > 0)
        {
            res.Meldingen.Add(new BaanControleMelding("Waarschuwing", $"De verkenner is het niet eens met jouw vastgelegde richtingen ({tegen} tegen, {zelfde} gelijk) - dat past niet bij één omgekeerde test-loc. Er wordt NIETS automatisch ingevuld; controleer de richtingen (en de rijrichting-instelling van de test-loc)."));
            return res;
        }
        if (omdraaien)
            res.Meldingen.Add(new BaanControleMelding("Info", $"De verkenner-richtingen zijn tegengesteld aan al jouw vastgelegde richtingen ({tegen}x) - de test-loc van de verkenner reed blijkbaar met omgekeerde polariteit. Alle verkenner-richtingen zijn daarom omgedraaid."));
        else if (zelfde == 0)
            res.Meldingen.Add(new BaanControleMelding("Waarschuwing", "Er is geen enkele relatie met een al vastgelegde richting om de verkenner-richting (en de polariteit van de test-loc) mee te controleren. De voorstellen gaan uit van een test-loc met normale polariteit - controleer ze."));

        foreach (var (rel, t) in tellers.OrderBy(k => k.Key.Van.Nummer).ThenBy(k => k.Key.Naar.Nummer))
        {
            string naam = $"{rel.Van.Nummer} -> {rel.Naar.Nummer}";
            if (t.vooruit > 0 && t.achteruit > 0)
            {
                res.Meldingen.Add(new BaanControleMelding("Waarschuwing", $"Relatie {naam}: de verkenner reed deze zowel vooruit ({t.vooruit}x) als achteruit ({t.achteruit}x) - tegenstrijdig, niet ingevuld."));
                continue;
            }
            bool vooruit = (t.vooruit > 0) != omdraaien;
            int aantal = t.vooruit + t.achteruit;
            string tekst = vooruit ? "vooruit" : "achteruit";
            if (rel.RijrichtingVooruit is bool vast)
            {
                if (vast != vooruit)
                    res.Meldingen.Add(new BaanControleMelding("Waarschuwing", $"Relatie {naam}: jij hebt {(vast ? "vooruit" : "achteruit")} vastgelegd, de verkenner meet {tekst}. Jouw waarde blijft staan - controleer."));
                continue;
            }
            res.Invullen.Add(new Voorstel(rel, vooruit, aantal));
            res.Meldingen.Add(new BaanControleMelding("Info", $"Relatie {naam}: rijrichting nog niet vastgelegd; verkenner ({aantal}x) = {tekst}."));
        }
        return res;
    }

    public static void Toepassen(Resultaat res)
    {
        foreach (var v in res.Invullen)
            if (v.Relatie.RijrichtingVooruit is null) v.Relatie.RijrichtingVooruit = v.Vooruit;
    }
}
