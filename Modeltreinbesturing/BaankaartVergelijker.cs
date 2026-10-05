using Modeltreinbesturing.Model;

namespace Modeltreinbesturing;

/// <summary>
/// Legt een door de Baanverkenner gemaakte baankaart naast het huidige project en meldt
/// verschillen. Puur SIGNALEREND (zoals "Controleer mijn baan"): niets wordt automatisch
/// aangepast, want de baankaart weet alleen wat er fysiek gereden is - de keuzes in het
/// project (bijv. een melder die bewust in meerdere blokken zit als overloop-melder, of een
/// wissel die buiten het geteste stuk ligt) kent hij niet.
///
/// Uitgangspunt dat uit de praktijk volgt (BLOKKENSCHEMA.md, BUG #38): het Dinamo-blok van
/// een melder is de feitelijke elektrische sectie en hoort in dit project gelijk te zijn aan
/// Blok.Nummer (DinamoHardware.SplitsBlokAdres leidt het Dinamo-adres daar uit af).
/// </summary>
public class BaankaartVergelijker
{
    public List<BaanControleMelding> Vergelijk(Baanverkenner.Kern.Baankaart kaart, BlokBeheerder blokBeheerder, BaanOntwerpBeheerder baanBeheerder)
    {
        var meldingen = new List<BaanControleMelding>();

        if (!kaart.Voltooid)
            meldingen.Add(new BaanControleMelding("Info", "Deze baankaart komt van een NIET voltooide verkenning: 'melder staat niet in de kaart' en 'adres had geen effect' zijn dan onder voorbehoud (het stuk baan is misschien nog niet bereden)."));

        // Welke melders hoort bij welk project-blok (alle drie de plekken waar een melder kan staan).
        var blokkenPerMelder = new Dictionary<int, List<Blok>>();
        void Koppel(int melder, Blok blok)
        {
            if (melder <= 0) return;
            if (!blokkenPerMelder.TryGetValue(melder, out var lijst)) blokkenPerMelder[melder] = lijst = new List<Blok>();
            if (!lijst.Contains(blok)) lijst.Add(blok);
        }
        foreach (var blok in blokBeheerder.Blokken)
        {
            foreach (var m in blok.Bezetmeldpunten) Koppel(m.MeldernNummer, blok);
            foreach (var r in blok.RichtingsBezetmeldingen)
                foreach (var nr in r.MeldernNummers) Koppel(nr, blok);
        }

        var kaartMelders = kaart.Melders.ToDictionary(m => m.Nummer);

        // 1. Melders die de verkenner zag maar die in het project nergens in zitten.
        foreach (var m in kaart.Melders.OrderBy(m => m.Nummer))
        {
            if (blokkenPerMelder.ContainsKey(m.Nummer)) continue;
            int keer = kaart.Overgangen.Where(o => o.Van == m.Nummer || o.Naar == m.Nummer).Sum(o => o.AantalKeer);
            string blokTekst = m.DinamoBlok is int d ? $" (Dinamo-blok {d})" : " (Dinamo-blok onbekend)";
            string voorstel = m.DinamoBlok is int d2 && blokBeheerder.Blokken.Any(b => b.Nummer == d2)
                ? $" Waarschijnlijk hoort hij bij blok {d2}."
                : "";
            string ernst = keer >= 3 ? "Waarschuwing" : "Info";
            meldingen.Add(new BaanControleMelding(ernst, $"Melder {m.Nummer}{blokTekst} is {keer}x gezien tijdens de verkenning, maar zit in geen enkel blok van dit project.{voorstel}" + (keer < 3 ? " Maar weinig gezien - controleer eerst of dit een echte melder is." : "")));
        }

        // 2. Melders in het project die de verkenner nooit zag (alleen betrouwbaar bij een voltooide kaart).
        foreach (var kv in blokkenPerMelder.OrderBy(k => k.Key))
        {
            if (kaartMelders.ContainsKey(kv.Key)) continue;
            string blokken = string.Join(", ", kv.Value.Select(b => b.Nummer));
            meldingen.Add(new BaanControleMelding(kaart.Voltooid ? "Waarschuwing" : "Info",
                $"Melder {kv.Key} (in blok {blokken}) is door de verkenner niet gezien" + (kaart.Voltooid ? " - klopt het meldernummer, of ligt dit stuk buiten het bereden deel?" : " - mogelijk nog niet bereden.")));
        }

        // 3. Dinamo-blok van de melder versus het blok waarin hij staat.
        foreach (var kv in blokkenPerMelder.OrderBy(k => k.Key))
        {
            if (!kaartMelders.TryGetValue(kv.Key, out var info) || info.DinamoBlok is not int dinamo) continue;
            if (kv.Value.Any(b => b.Nummer == dinamo)) continue;
            string blokken = string.Join(", ", kv.Value.Select(b => b.Nummer));
            meldingen.Add(new BaanControleMelding("Waarschuwing",
                $"Melder {kv.Key} is volgens de verkenner elektrisch onderdeel van Dinamo-blok {dinamo}, maar staat in dit project alleen in blok {blokken}. Een overloop-melder die bewust in meerdere blokken staat is prima, maar controleer of hij niet in het verkeerde blok zit."));
        }

        // 4. Melders die in meer dan één project-blok staan (informatief).
        foreach (var kv in blokkenPerMelder.Where(k => k.Value.Count > 1).OrderBy(k => k.Key))
            meldingen.Add(new BaanControleMelding("Info", $"Melder {kv.Key} staat in {kv.Value.Count} blokken ({string.Join(", ", kv.Value.Select(b => b.Nummer))}) - als dit een bewuste overloop-melder is, is dat prima."));

        // 5. Overgangen die fysiek bereden zijn maar waar het project geen relatie voor heeft.
        var gemeld = new HashSet<(int, int)>();
        foreach (var o in kaart.Overgangen.OrderByDescending(o => o.AantalKeer))
        {
            if (!blokkenPerMelder.TryGetValue(o.Van, out var vanBlokken) || !blokkenPerMelder.TryGetValue(o.Naar, out var naarBlokken)) continue;
            if (vanBlokken.Intersect(naarBlokken).Any()) continue; // binnen hetzelfde blok
            bool heeftRelatie = vanBlokken.Any(a => naarBlokken.Any(b =>
                blokBeheerder.Relaties.Any(r => (r.Van == a && r.Naar == b) || (r.Van == b && r.Naar == a))));
            if (heeftRelatie) continue;
            var sleutel = (Math.Min(o.Van, o.Naar), Math.Max(o.Van, o.Naar));
            if (!gemeld.Add(sleutel)) continue;
            // Eén of twee keer gezien is vaak een meetstoring (zie BUG #40, onverwachte terugweg) - dan alleen Info.
            meldingen.Add(new BaanControleMelding(o.AantalKeer >= 3 ? "Waarschuwing" : "Info",
                $"De verkenner reed {o.AantalKeer}x van melder {o.Van} naar melder {o.Naar} (blok {string.Join("/", vanBlokken.Select(b => b.Nummer))} naar blok {string.Join("/", naarBlokken.Select(b => b.Nummer))}), maar in dit project bestaat daar geen relatie voor. Mist er een verbinding?" + (o.AantalKeer < 3 ? " (Maar weinig gezien - kan een meetstoring zijn.)" : "")));
        }

        // 6. Kopsporen.
        foreach (var k in kaart.Kopsporen)
        {
            if (!blokkenPerMelder.TryGetValue(k.Melder, out var blokken)) continue;
            if (blokken.Any(b => b.Type == BlokType.Kopspoor)) continue;
            meldingen.Add(new BaanControleMelding("Info", $"Melder {k.Melder} is bij de verkenning een kopspoor (doodlopend einde) gebleken, maar blok {string.Join("/", blokken.Select(b => b.Nummer))} is in dit project geen Kopspoor."));
        }

        // 7. Wisseladressen.
        var projectAdressen = new HashSet<int>();
        foreach (var w in baanBeheerder.Symbolen.OfType<Wissel>()) if (w.Adres > 0) projectAdressen.Add(w.Adres);
        foreach (var w in baanBeheerder.Symbolen.OfType<Driewegwissel>()) if (w.Adres > 0) projectAdressen.Add(w.Adres);
        foreach (var w in baanBeheerder.Symbolen.OfType<Kruiswissel>())
        {
            if (w.Adres > 0) projectAdressen.Add(w.Adres);
            if (w.Adres2 > 0) projectAdressen.Add(w.Adres2);
        }
        var effectAdressen = kaart.Wissels.Select(w => w.Adres).ToHashSet();
        var zonderEffect = kaart.AdressenZonderEffect.ToHashSet();

        foreach (int a in projectAdressen.OrderBy(a => a))
        {
            if (effectAdressen.Contains(a)) continue;
            if (zonderEffect.Contains(a))
                meldingen.Add(new BaanControleMelding("Info", $"Wisseladres {a} staat in dit project, maar had tijdens de verkenning geen zichtbaar effect op de route van de testloc. Dat kan kloppen (de wissel ligt buiten het bereden deel, of is een rangeerwissel) - klopt het adres zelf?"));
        }
        foreach (int a in effectAdressen.OrderBy(a => a))
        {
            if (projectAdressen.Contains(a)) continue;
            meldingen.Add(new BaanControleMelding("Info", $"Wisseladres {a} heeft bij de verkenning écht een andere route gegeven, maar er staat geen wissel met dit adres getekend in het project."));
        }

        // 8. Kortsluitpunten / onverwachte terugwegen.
        foreach (var k in kaart.Kortsluitpunten)
        {
            string soort = k.Soort == Baanverkenner.Kern.KortsluitpuntSoort.Kortsluiting ? "kortsluiting" : "onverwachte terugweg (geen kortsluiting)";
            meldingen.Add(new BaanControleMelding("Info", $"Verkenner noteerde bij melder {k.NaMelder} een {soort} ({k.AantalKortsluitingen}x)."));
        }

        return meldingen;
    }
}
