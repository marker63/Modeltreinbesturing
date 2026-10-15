using Baanverkenner.Kern;
using Modeltreinbesturing.Model;

namespace Modeltreinbesturing;

/// <summary>
/// BUG #163 (Marco 15-10 16:22: "gebruik ook al deze kennis om de gehele Modeltreinbesturing zo goed mogelijk te maken", gekozen: automatisch overnemen):
/// legt de BESTAANDE wisselstraten van het project naast wat de Baanverkenner bewezen heeft en past alleen aan wat aantoonbaar nodig is.
///
/// Wat wél automatisch gebeurt (alleen eisen die voor ALLE voorstellen van het blokpaar gelden, zodat nooit een verkeerd alternatief gekozen wordt):
///  - een wissel die de kaart voor deze rit eist (uit een splitsing of omdat de andere stand kortsluiting geeft) maar die in de wisselstraat ontbreekt, wordt toegevoegd;
///  - een wissel die in de wisselstraat staat in de stand waarvan de kaart BEWEZEN heeft dat die kortsluiting geeft (bijv. 16 → 5 met wissel 2 afbuigend), wordt omgezet.
/// Wat NOOIT gebeurt: kruiswissel-motoren (13/10) aanraken (KRUISWISSEL.md: jouw vastgestelde standen zijn leidend), "aangenomen" eisen toepassen,
/// een stand wijzigen op grond van een splitsing alleen (dat wordt gemeld), of een wisselstraat toevoegen of verwijderen.
/// Alles is ongedaan te maken met <see cref="Herstel"/>. NIET getest op de baan.
/// </summary>
public class WisselstraatCorrectie
{
    public class Wijziging
    {
        public required Wisselstraat Straat { get; init; }
        public required Wissel Wissel { get; init; }
        /// <summary>Null = de wissel stond nog niet in de wisselstraat (toegevoegd).</summary>
        public WisselStand? Oud { get; init; }
        public WisselStand Nieuw { get; init; }
        public required string Reden { get; init; }
        public WisselInWisselstraat? Regel { get; set; }
    }

    public class Resultaat
    {
        public List<Wijziging> Wijzigingen { get; } = new();
        public List<BaanControleMelding> Meldingen { get; } = new();
    }

    public Resultaat Bereken(Baankaart kaart, BlokBeheerder bb, IReadOnlyList<Wissel> wissels, IReadOnlyList<Kruiswissel> kruiswissels, IReadOnlyList<Wisselstraat> bestaand)
    {
        var res = new Resultaat();
        // Vers berekend uit de kaart zelf (ingangen/Via en verboden standen zijn dan meegenomen), anders de bewaarde voorstellen.
        var voorstellen = WisselstraatVoorstel.Bereken(kaart);
        if (voorstellen.Count == 0) voorstellen = kaart.Wisselstraatvoorstellen;
        if (voorstellen.Count == 0 || bestaand.Count == 0) return res;

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
        bool IsKruisMotor(int adres) => kruiswissels.Any(k => k.Adres == adres || (k.Adres2 > 0 && k.Adres2 == adres));

        foreach (var straat in bestaand.OrderBy(s => s.Van.Nummer).ThenBy(s => s.Naar.Nummer).ThenBy(s => s.Volgnummer))
        {
            var voor = voorstellen.Where(v => Uniek(v.VanMelder) == straat.Van && Uniek(v.NaarMelder) == straat.Naar).ToList();
            if (voor.Count == 0) continue;
            // eisen die in ELK voorstel van dit blokpaar staan, met dezelfde stand en een bewezen bron
            var gezamenlijk = new List<(int Adres, bool Afb, EisBron Bron)>();
            foreach (var e in voor[0].Eisen.Where(e => e.Bron != EisBron.Aangenomen))
            {
                bool overal = voor.All(v => v.Eisen.Any(x => x.Adres == e.Adres && x.Afbuigend == e.Afbuigend && x.Bron != EisBron.Aangenomen));
                if (overal && !gezamenlijk.Any(g => g.Adres == e.Adres)) gezamenlijk.Add((e.Adres, e.Afbuigend, e.Bron));
            }
            string naam = $"{straat.Van.Nummer} → {straat.Naar.Nummer} (#{straat.Volgnummer})";
            foreach (var (adres, afb, bron) in gezamenlijk)
            {
                var gewenst = afb ? WisselStand.Afbuigend : WisselStand.Rechtdoor;
                if (IsKruisMotor(adres))
                {
                    var kruisRegel = straat.Kruiswisselstanden.FirstOrDefault(k => k.Kruiswissel.Adres == adres || k.Kruiswissel.Adres2 == adres);
                    var huidig = kruisRegel is null ? (WisselStand?)null : kruisRegel.Kruiswissel.Adres == adres ? kruisRegel.StandAdres : kruisRegel.StandAdres2;
                    if (huidig is not null && huidig != gewenst)
                        res.Meldingen.Add(new BaanControleMelding("Waarschuwing", $"Wisselstraat {naam}: de kaart vraagt kruismotor {adres} {(afb ? "afbuigend" : "rechtdoor")}, de wisselstraat heeft {huidig.ToString()!.ToLower()}. NIET gewijzigd (jouw vastgestelde kruiswisselstanden blijven leidend) - controleer dit zelf."));
                    continue;
                }
                if (!wisselVanAdres.TryGetValue(adres, out var wissel)) continue;
                var regel = straat.Wissels.FirstOrDefault(w => w.Wissel == wissel);
                if (regel is null)
                {
                    res.Wijzigingen.Add(new Wijziging { Straat = straat, Wissel = wissel, Oud = null, Nieuw = gewenst,
                        Reden = $"wisselstraat {naam}: wissel {adres} {(afb ? "afbuigend" : "rechtdoor")} ontbrak; de kaart eist hem ({(bron == EisBron.Kortsluiting ? "anders kortsluiting" : "uit een splitsing")})." });
                }
                else if (regel.GewensteStand != gewenst)
                {
                    if (bron == EisBron.Kortsluiting)
                        res.Wijzigingen.Add(new Wijziging { Straat = straat, Wissel = wissel, Oud = regel.GewensteStand, Nieuw = gewenst, Regel = regel,
                            Reden = $"wisselstraat {naam}: wissel {adres} stond {regel.GewensteStand.ToString().ToLower()}, maar de kaart heeft bewezen dat dat kortsluiting geeft; nu {gewenst.ToString().ToLower()}." });
                    else
                        res.Meldingen.Add(new BaanControleMelding("Waarschuwing", $"Wisselstraat {naam}: wissel {adres} staat {regel.GewensteStand.ToString().ToLower()}, de kaart (splitsing) wijst op {gewenst.ToString().ToLower()}. NIET gewijzigd - kan een bewuste keuze zijn (bijv. een andere aankomstkant); controleer het."));
                }
            }
        }
        foreach (var w in res.Wijzigingen) res.Meldingen.Add(new BaanControleMelding("Waarschuwing", w.Reden + " NIET getest op de baan."));
        return res;
    }

    public void Pas(Resultaat res)
    {
        foreach (var w in res.Wijzigingen)
        {
            if (w.Regel is not null) w.Regel.GewensteStand = w.Nieuw;
            else
            {
                w.Regel = new WisselInWisselstraat { Wissel = w.Wissel, GewensteStand = w.Nieuw };
                w.Straat.Wissels.Add(w.Regel);
            }
        }
    }

    public void Herstel(Resultaat res)
    {
        foreach (var w in res.Wijzigingen.AsEnumerable().Reverse())
        {
            if (w.Regel is null) continue;
            if (w.Oud is WisselStand oud) w.Regel.GewensteStand = oud;
            else { w.Straat.Wissels.Remove(w.Regel); w.Regel = null; }
        }
    }
}
