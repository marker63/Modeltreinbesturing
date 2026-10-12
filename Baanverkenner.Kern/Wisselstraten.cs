namespace Baanverkenner.Kern;

/// <summary>Waar een wisseleis vandaan komt: <see cref="Splitsing"/> = uit een gevonden splitsing (rijden vanaf de puntzijde of van achteren
/// door dezelfde wissel), <see cref="Kortsluiting"/> = de andere stand gaf kortsluiting bij het omzetten, <see cref="Aangenomen"/> = deze wissel
/// stond afbuigend bij de (minimale) wisselstand waarmee de overgang gereden is - niet bewezen dat hij ook echt nodig is,
/// <see cref="Overloop"/> = BUG #116: partnerwissel van een overloopverbinding (twee wissels samen afbuigend), bewezen door een opgelost kortsluitpunt.</summary>
public enum EisBron { Splitsing, Kortsluiting, Aangenomen, Overloop }

public class WisselEis
{
    public int Adres { get; set; }
    public bool Afbuigend { get; set; }
    public EisBron Bron { get; set; }
    public string Tekst => $"wissel {Adres} {(Afbuigend ? "afbuigend" : "rechtdoor")}";
}

/// <summary>BUG #78: voorstel voor een wisselstraat (Modeltreinbesturing): welke wissels in welke stand om van melder <see cref="VanMelder"/>
/// naar melder <see cref="NaarMelder"/> te rijden, afgeleid uit wat de verkenning al weet. Er wordt niets extra gereden. Wissels die niet
/// genoemd worden hebben (voor zover bekend) geen invloed op deze stap.</summary>
public class WisselstraatVoorstel
{
    public int VanMelder { get; set; }
    public int NaarMelder { get; set; }
    public Richting Richting { get; set; }
    public int AantalKeer { get; set; }
    public int? VanBlok { get; set; }
    public int? NaarBlok { get; set; }
    /// <summary>BUG #88: echte Dinamo-bloknummers (of "onbekend (melder X)") om te tonen.</summary>
    public string VanLabel { get; set; } = "";
    public string NaarLabel { get; set; } = "";
    public List<WisselEis> Eisen { get; set; } = new();
    /// <summary>De volledige wisselstand (sleutel van de configuratie: de adressen die afbuigend stonden) waarbij deze overgang gereden is.</summary>
    public string Standen { get; set; } = "";

    public string EisenTekst => Eisen.Count == 0 ? "geen wissel nodig" : string.Join(", ", Eisen.OrderBy(e => e.Adres).Select(e => e.Tekst + (e.Bron switch
    {
        EisBron.Splitsing => " (uit splitsing)",
        EisBron.Kortsluiting => " (anders kortsluiting)",
        EisBron.Overloop => " (overloopwissel, bewezen)",
        _ => " (aangenomen)"
    })));

    public string Sleutel => $"{VanMelder}>{NaarMelder}:{Richting}|{string.Join(",", Eisen.OrderBy(e => e.Adres).Select(e => $"{e.Adres}{(e.Afbuigend ? 'A' : 'R')}"))}";

    public static List<WisselstraatVoorstel> Bereken(Baankaart k)
    {
        var res = new List<WisselstraatVoorstel>();
        var blokken = k.VoorgesteldeBlokken.Count > 0 ? k.VoorgesteldeBlokken : BlokVoorstel.Bereken(k);
        var blokVan = new Dictionary<int, int>();
        var labelVan = new Dictionary<int, string>();
        foreach (var b in blokken) foreach (var m in b.Melders) { blokVan[m] = b.Nummer; labelVan[m] = b.Label; }
        var gezien = new HashSet<string>();
        var oplossingen = k.AlleKortsluitOplossingen();

        foreach (var o in k.Overgangen.OrderBy(o => o.Van).ThenBy(o => o.Naar).ThenBy(o => o.Richting))
        {
            int? vb = blokVan.TryGetValue(o.Van, out var x1) ? x1 : null;
            int? nb = blokVan.TryGetValue(o.Naar, out var x2) ? x2 : null;
            if (vb is not null && vb == nb) continue; // binnen een voorgesteld blok ligt per definitie geen wissel

            // Eisen die altijd gelden voor deze stap
            var basis = new List<WisselEis>();
            var achterAdressen = new HashSet<int>(); // eisen voor een stap van achteren door de wissel
            foreach (var (adres, afb, achter) in k.WisselEisenVoorStapDetail(o.Van, o.Naar, o.Richting))
            {
                basis.Add(new WisselEis { Adres = adres, Afbuigend = afb, Bron = EisBron.Splitsing });
                if (achter) achterAdressen.Add(adres);
            }
            foreach (var w in k.Wissels)
                foreach (var ob in w.Waarnemingen.Where(x => x.Soort == WaarnemingSoort.KortsluitingBijOmzetten))
                    if (ob.NaMelder == o.Van && ob.Richting == o.Richting && ob.VolgendeBijRechtdoor == o.Naar && ob.VolgendeBijAfbuigend is null
                        && !basis.Any(e => e.Adres == w.Adres))
                        basis.Add(new WisselEis { Adres = w.Adres, Afbuigend = false, Bron = EisBron.Kortsluiting });

            // Minimale wisselstanden waarmee deze overgang gereden is (stand A die een echte deelverzameling van stand B is, maakt B overbodig)
            var configs = o.GezienBijConfiguraties.Select(Configuratie.VanSleutel).Distinct().ToList();
            if (configs.Count == 0) configs.Add(Configuratie.Basis);
            var minimaal = configs
                .Where(c => !configs.Any(d => !d.Equals(c) && d.Afbuigend.All(c.Bevat)))
                .OrderBy(c => c.Afbuigend.Length).ThenBy(c => c.Sleutel, StringComparer.Ordinal).Take(4).ToList();

            var opties = new List<(List<WisselEis> Eisen, string Standen)>();
            foreach (var m in minimaal)
            {
                // Strijdt een afbuigend adres uit deze stand met een eis uit de splitsingen, dan is deze stand waarschijnlijk van achteren
                // doorgereden (tongen open gereden) en bewijst niets: overslaan.
                if (m.Afbuigend.Any(a => basis.Any(e => e.Adres == a && !e.Afbuigend))) continue;
                // Een eis voor een stap vanaf de puntzijde die in deze (echt gereden) stand niet gold, is door de gereden stap tegengesproken
                // (bijv. meerdere adressen die dezelfde motor aansturen): weglaten. Een eis voor een stap van achteren blijft altijd staan, want
                // van achteren door een verkeerd staande wissel rijden kan lukken doordat de tongen open gereden worden.
                var eisen = basis.Where(e => achterAdressen.Contains(e.Adres) || m.Bevat(e.Adres) == e.Afbuigend)
                    .Select(e => new WisselEis { Adres = e.Adres, Afbuigend = e.Afbuigend, Bron = e.Bron }).ToList();
                foreach (var a in m.Afbuigend)
                    if (!eisen.Any(e => e.Adres == a)) eisen.Add(new WisselEis { Adres = a, Afbuigend = true, Bron = EisBron.Aangenomen });
                VoegOverloopToe(eisen, o, oplossingen);
                opties.Add((eisen, m.Sleutel));
            }
            if (opties.Count == 0) { VoegOverloopToe(basis, o, oplossingen); opties.Add((basis, "")); }
            // Een optie met alle eisen van een andere optie en nog meer is overbodig
            string[] Set((List<WisselEis> Eisen, string Standen) opt) => opt.Eisen.Select(e => $"{e.Adres}{(e.Afbuigend ? 'A' : 'R')}").ToArray();
            opties = opties.Where(a => !opties.Any(b => !ReferenceEquals(a.Eisen, b.Eisen) && Set(b).Length < Set(a).Length && Set(b).All(Set(a).Contains))).ToList();

            foreach (var (eisen, standen) in opties)
            {
                var v = new WisselstraatVoorstel
                {
                    VanMelder = o.Van, NaarMelder = o.Naar, Richting = o.Richting, AantalKeer = o.AantalKeer,
                    VanBlok = vb, NaarBlok = nb, VanLabel = labelVan.GetValueOrDefault(o.Van, "?"), NaarLabel = labelVan.GetValueOrDefault(o.Naar, "?"), Eisen = eisen.OrderBy(e => e.Adres).ToList(), Standen = standen
                };
                if (gezien.Add(v.Sleutel)) res.Add(v);
            }
        }
        return res;
    }

    /// <summary>BUG #116: staan in <paramref name="eisen"/> alle wissels van een bewezen kortsluitstand afbuigend en gaat de stap langs de melder van dat
    /// kortsluitpunt, dan hoort de bewezen oplossing (de partnerwissel van de overloopverbinding) erbij. Zonder dat rijdt de wisselstraat in de
    /// kortsluiting (bijv. 5 -> 12 met wissel 2 afbuigend vraagt ook wissel 1 afbuigend). Botst de oplossing met een bestaande eis voor dat adres,
    /// dan geldt de bestaande eis (de oplossing hoort dan bij een andere stap, bijv. wissel 2 rechtdoor voor 5 -> 16).</summary>
    internal static void VoegOverloopToe(List<WisselEis> eisen, Overgang o, List<KortsluitOplossing> oplossingen)
    {
        foreach (var sol in oplossingen)
        {
            if (sol.Richting != o.Richting || (sol.NaMelder != o.Van && sol.NaMelder != o.Naar)) continue;
            var kStand = Configuratie.VanSleutel(sol.Stand);
            if (kStand.Afbuigend.Length == 0 || !kStand.Afbuigend.All(a => eisen.Any(e => e.Adres == a && e.Afbuigend))) continue;
            if (eisen.Any(e => e.Adres == sol.Adres)) continue;
            eisen.Add(new WisselEis { Adres = sol.Adres, Afbuigend = sol.Afbuigend, Bron = EisBron.Overloop });
        }
    }
}
