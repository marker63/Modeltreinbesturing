namespace Baanverkenner.Kern;

/// <summary>BUG #104: vergelijkt de kaart van een controleronde ("Voltooide baan nogmaals checken") met de bestaande, voltooide kaart.
/// De bestaande kaart wordt hier nooit gewijzigd. Bij de controle begint de nieuwe kaart met alleen de kennis die kortsluitingen voorkomt
/// (wissels, doorgangen, kortsluitoplossingen, Dinamo-blok per melder); de overgangen, kopsporen en kortsluitpunten zijn dus echt opnieuw gezien.
/// Van de wissels telt alleen wat er tijdens de controle bij is gekomen (<paramref name="wisselSeed"/> = aantal waarnemingen bij de start).</summary>
public static class ControleVergelijking
{
    public static ControleVerslag Vergelijk(Baankaart oud, Baankaart nieuw, IReadOnlyDictionary<int, int> wisselSeed, ISet<int> opnieuwGevonden, DateTime nu)
    {
        var regels = new List<string>();
        int bevestigd = 0;

        // ---- Overgangen (melder -> melder per richting) ----
        var oudO = oud.Overgangen.Select(o => (o.Van, o.Naar, o.Richting)).ToHashSet();
        var nieuwO = nieuw.Overgangen.Select(o => (o.Van, o.Naar, o.Richting)).ToHashSet();
        foreach (var o in oudO.Except(nieuwO).OrderBy(x => x.Van).ThenBy(x => x.Naar))
            regels.Add($"Overgang melder {o.Van} → {o.Naar} ({o.Richting.Tekst()}) stond in de kaart maar is nu niet gereden.");
        foreach (var o in nieuwO.Except(oudO).OrderBy(x => x.Van).ThenBy(x => x.Naar))
            regels.Add($"NIEUW: overgang melder {o.Van} → {o.Naar} ({o.Richting.Tekst()}) is gereden maar staat niet in de kaart.");
        bevestigd += oudO.Intersect(nieuwO).Count();

        // ---- Wissels: alleen wat tijdens de controle is bijgekomen ----
        var oudW = oud.Wissels.ToDictionary(w => w.Adres);
        foreach (var w in nieuw.Wissels.OrderBy(w => w.Adres))
        {
            int seed = wisselSeed.GetValueOrDefault(w.Adres);
            var bij = w.Waarnemingen.Skip(seed).ToList();
            if (!oudW.TryGetValue(w.Adres, out var ow))
            {
                if (bij.Count > 0) regels.Add($"NIEUW: wissel {w.Adres} gevonden die niet in de kaart staat: {bij[0].Toelichting}");
                continue;
            }
            foreach (var b in bij)
            {
                var zelfde = ow.Waarnemingen.Where(x => x.Soort == b.Soort && x.NaMelder == b.NaMelder && x.Richting == b.Richting).ToList();
                if (zelfde.Count == 0)
                    regels.Add($"Wissel {w.Adres}: nu anders vastgelegd: {b.Toelichting} (in de kaart staat dit niet zo).");
                else if (b.Soort == WaarnemingSoort.Splitsing && !zelfde.Any(x => x.VolgendeBijRechtdoor == b.VolgendeBijRechtdoor && x.VolgendeBijAfbuigend == b.VolgendeBijAfbuigend))
                    regels.Add($"WISSEL {w.Adres} VERSCHILT: na melder {b.NaMelder} ({b.Richting.Tekst()}) nu rechtdoor → {Mld(b.VolgendeBijRechtdoor)}, afbuigend → {Mld(b.VolgendeBijAfbuigend)}; in de kaart: rechtdoor → {Mld(zelfde[0].VolgendeBijRechtdoor)}, afbuigend → {Mld(zelfde[0].VolgendeBijAfbuigend)}.");
                else bevestigd++;
            }
        }
        foreach (var w in oud.Wissels.OrderBy(w => w.Adres))
            if (!opnieuwGevonden.Contains(w.Adres) && !(nieuw.Wissels.FirstOrDefault(x => x.Adres == w.Adres) is { } nw && nw.Waarnemingen.Count > wisselSeed.GetValueOrDefault(w.Adres)))
                regels.Add($"Wissel {w.Adres} is tijdens deze controle niet opnieuw vastgesteld (dat hoeft geen fout te zijn: soms is een wissel maar vanaf één kant te vinden).");

        // ---- Kopsporen ----
        var oudK = oud.Kopsporen.Select(k => (k.Melder, k.Richting)).ToHashSet();
        var nieuwK = nieuw.Kopsporen.Select(k => (k.Melder, k.Richting)).ToHashSet();
        foreach (var k in oudK.Except(nieuwK)) regels.Add($"Doodlopend einde bij melder {k.Melder} ({k.Richting.Tekst()}) is nu niet teruggezien.");
        foreach (var k in nieuwK.Except(oudK)) regels.Add($"NIEUW: doodlopend einde bij melder {k.Melder} ({k.Richting.Tekst()}).");
        bevestigd += oudK.Intersect(nieuwK).Count();

        // ---- Kortsluitpunten ----
        var oudS = oud.Kortsluitpunten.Select(k => (k.NaMelder, k.Richting, k.Soort)).ToHashSet();
        var nieuwS = nieuw.Kortsluitpunten.Select(k => (k.NaMelder, k.Richting, k.Soort)).ToHashSet();
        foreach (var k in nieuwS.Except(oudS)) regels.Add($"NIEUW: {(k.Soort == KortsluitpuntSoort.Kortsluiting ? "kortsluiting" : "onverwachte terugweg")} na melder {k.NaMelder} ({k.Richting.Tekst()}); die stond niet in de kaart.");
        foreach (var k in oudS.Except(nieuwS)) regels.Add($"Het kortsluitpunt na melder {k.NaMelder} ({k.Richting.Tekst()}) kwam nu niet voor (opgelost door de bekende oplossingen, of niet bereikt).");
        bevestigd += oudS.Intersect(nieuwS).Count();

        // ---- Adressen zonder effect ----
        // BUG #161: de controle doet geen adrestest (de wissels zijn al bekend), dus 'nu zonder effect' was altijd alle adressen: een schijnverschil. Alleen
        // een adres dat de kaart wel als wissel kent maar dat tijdens de controle ontbreekt is al gemeld bij de wissels.

        bool verschillen = regels.Any(r => r.StartsWith("NIEUW") || r.StartsWith("WISSEL") || r.Contains("niet gereden") || r.Contains("niet teruggezien"));
        string sam = verschillen
            ? $"Controle: {regels.Count} verschil(len) om te bekijken; daarnaast kwamen {bevestigd} onderdelen (overgangen, wissels, einden, kortsluitpunten) wel overeen met de kaart."
            : $"Controle: geen verschillen met de kaart; {bevestigd} onderdelen (overgangen, wissels, einden, kortsluitpunten) kwamen overeen.";
        return new ControleVerslag { Datum = nu, Samenvatting = sam, Verschillen = verschillen, Regels = regels };
    }

    private static string Mld(int? m) => m is int x ? $"melder {x}" : "onbekend";
}
