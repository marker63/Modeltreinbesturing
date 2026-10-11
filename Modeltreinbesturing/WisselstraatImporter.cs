using Baanverkenner.Kern;
using Modeltreinbesturing.Model;

namespace Modeltreinbesturing;

/// <summary>
/// BUG #78 (deel B): maakt uit de wisselstraat-voorstellen van de Baanverkenner (<see cref="Baankaart.Wisselstraatvoorstellen"/>) echte
/// wisselstraten in het project. Er wordt nooit iets overschreven: een blokpaar dat al een wisselstraat heeft, wordt overgeslagen, en er
/// komt alleen een wisselstraat waar al een BlokRelatie bestaat. Wissels die niet op het baanontwerp staan worden gemeld, niet verzonnen.
/// De nieuwe wisselstraten zijn NIET getest op de baan; "aangenomen" eisen zijn niet bewezen nodig.
/// </summary>
public class WisselstraatImporter
{
    public class Resultaat
    {
        public List<Wisselstraat> Nieuw { get; } = new();
        public List<BaanControleMelding> Meldingen { get; } = new();
        public bool IsLeeg => Nieuw.Count == 0;
    }

    public Resultaat Importeer(Baankaart kaart, BlokBeheerder bb, IReadOnlyList<Wissel> wissels, IReadOnlyList<Kruiswissel> kruiswissels,
        IReadOnlyList<Wisselstraat> bestaand)
    {
        var res = new Resultaat();
        if (kaart.Wisselstraatvoorstellen.Count == 0)
        {
            res.Meldingen.Add(new BaanControleMelding("Info", "Deze baankaart bevat geen wisselstraat-voorstellen (te oude verkenning of nog geen overgangen)."));
            return res;
        }

        var blokVanMelder = new Dictionary<int, List<Blok>>();
        foreach (var b in bb.Blokken)
            foreach (var p in b.Bezetmeldpunten)
            {
                if (p.MeldernNummer <= 0) continue;
                if (!blokVanMelder.TryGetValue(p.MeldernNummer, out var l)) blokVanMelder[p.MeldernNummer] = l = new List<Blok>();
                if (!l.Contains(b)) l.Add(b);
            }
        Blok? Uniek(int melder) => blokVanMelder.TryGetValue(melder, out var l) && l.Count == 1 ? l[0] : null;

        var wisselVanAdres = wissels.Where(w => w.Adres > 0).GroupBy(w => w.Adres).ToDictionary(g => g.Key, g => g.First());
        Kruiswissel? KruisVan(int adres) => kruiswissels.FirstOrDefault(k => k.Adres == adres || (k.Adres2 > 0 && k.Adres2 == adres));

        // per blokpaar de alternatieven, beste (meest gezien) eerst
        var perPaar = new Dictionary<(Blok, Blok), List<WisselstraatVoorstel>>();
        var overgeslagenOnbekend = new HashSet<string>();
        foreach (var v in kaart.Wisselstraatvoorstellen.OrderByDescending(v => v.AantalKeer).ThenBy(v => v.VanMelder).ThenBy(v => v.NaarMelder))
        {
            var van = Uniek(v.VanMelder); var naar = Uniek(v.NaarMelder);
            if (van is null || naar is null)
            {
                if (overgeslagenOnbekend.Add($"{v.VanMelder}>{v.NaarMelder}"))
                    res.Meldingen.Add(new BaanControleMelding("Info", $"Melder {v.VanMelder} -> {v.NaarMelder}: een van de melders zit niet (of in meerdere blokken) in het project - geen wisselstraat."));
                continue;
            }
            if (van == naar) continue;
            if (!perPaar.TryGetValue((van, naar), out var lijst)) perPaar[(van, naar)] = lijst = new List<WisselstraatVoorstel>();
            lijst.Add(v);
        }

        foreach (var ((van, naar), voorstellen) in perPaar.OrderBy(p => p.Key.Item1.Nummer).ThenBy(p => p.Key.Item2.Nummer))
        {
            if (!bb.Relaties.Any(r => r.Van == van && r.Naar == naar))
            {
                res.Meldingen.Add(new BaanControleMelding("Info", $"Blok {van.Nummer} -> {naar.Nummer}: nog geen relatie in het project - geen wisselstraat (leg eerst de relatie vast)."));
                continue;
            }
            if (bestaand.Any(w => w.Van == van && w.Naar == naar) || res.Nieuw.Any(w => w.Van == van && w.Naar == naar))
            {
                res.Meldingen.Add(new BaanControleMelding("Info", $"Blok {van.Nummer} -> {naar.Nummer}: heeft al een wisselstraat - ongemoeid gelaten."));
                continue;
            }

            var gezien = new HashSet<string>();
            int volg = 1;
            foreach (var v in voorstellen)
            {
                var straat = Bouw(v, van, naar, wisselVanAdres, KruisVan, res);
                if (straat is null) continue;
                string inhoud = string.Join("|", straat.Wissels.OrderBy(w => w.Wissel.Adres).Select(w => $"{w.Wissel.Adres}{w.GewensteStand}"))
                    + "#" + string.Join("|", straat.Kruiswisselstanden.OrderBy(k => k.Kruiswissel.Adres).Select(k => $"{k.Kruiswissel.Adres}{k.StandAdres}{k.StandAdres2}"));
                if (!gezien.Add(inhoud)) continue;
                straat.Volgnummer = volg++;
                res.Nieuw.Add(straat);
                var onbewezen = v.Eisen.Where(e => e.Bron == EisBron.Aangenomen).ToList();
                res.Meldingen.Add(new BaanControleMelding(onbewezen.Count > 0 ? "Waarschuwing" : "Info",
                    $"Wisselstraat {van.Nummer} -> {naar.Nummer} (#{straat.Volgnummer}, {v.AantalKeer}x gezien): {v.EisenTekst}." +
                    (onbewezen.Count > 0 ? $" Let op: {string.Join(", ", onbewezen.Select(e => "wissel " + e.Adres))} is aangenomen, niet bewezen nodig." : "")));
            }
        }

        if (res.Nieuw.Count > 0)
            res.Meldingen.Add(new BaanControleMelding("Info", "Deze wisselstraten komen uit wat de verkenning gezien heeft en zijn NIET getest op de baan. Je eigen vastgestelde standen zijn niet aangeraakt."));
        return res;
    }

    private static Wisselstraat? Bouw(WisselstraatVoorstel v, Blok van, Blok naar, Dictionary<int, Wissel> wisselVanAdres,
        Func<int, Kruiswissel?> kruisVan, Resultaat res)
    {
        var volledig = string.IsNullOrWhiteSpace(v.Standen) ? new HashSet<int>() : Configuratie.VanSleutel(v.Standen).Afbuigend.ToHashSet();
        var straat = new Wisselstraat { Van = van, Naar = naar };
        var kruisEisen = new Dictionary<Kruiswissel, Dictionary<int, bool>>();
        foreach (var e in v.Eisen)
        {
            var stand = e.Afbuigend ? WisselStand.Afbuigend : WisselStand.Rechtdoor;
            if (wisselVanAdres.TryGetValue(e.Adres, out var w))
            {
                if (!straat.Wissels.Any(x => x.Wissel == w)) straat.Wissels.Add(new WisselInWisselstraat { Wissel = w, GewensteStand = stand });
                continue;
            }
            var k = kruisVan(e.Adres);
            if (k is null)
            {
                res.Meldingen.Add(new BaanControleMelding("Waarschuwing", $"Wisselstraat {van.Nummer} -> {naar.Nummer}: wisseladres {e.Adres} staat niet op het baanontwerp - overgeslagen (zet de wissel en importeer opnieuw)."));
                return null;
            }
            if (!kruisEisen.TryGetValue(k, out var d)) kruisEisen[k] = d = new Dictionary<int, bool>();
            d[e.Adres] = e.Afbuigend;
        }
        foreach (var (k, d) in kruisEisen)
        {
            WisselStand Van(int adres) => adres <= 0 ? WisselStand.Rechtdoor
                : (d.TryGetValue(adres, out var afb) ? afb : volledig.Contains(adres)) ? WisselStand.Afbuigend : WisselStand.Rechtdoor;
            straat.Kruiswisselstanden.Add(new KruiswisselInWisselstraat { Kruiswissel = k, StandAdres = Van(k.Adres), StandAdres2 = Van(k.Adres2) });
        }
        return straat;
    }
}
