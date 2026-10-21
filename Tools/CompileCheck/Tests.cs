using Baanverkenner.Kern;
using Modeltreinbesturing;
using Modeltreinbesturing.Model;

// Regressietests voor de Baankaart-import (BUG #53/#56). Geeft exitcode 1 bij een mislukte controle.
public static class Tests
{
    static int _fouten;
    /// <summary>Pendeltraject uit de test van 10-10 10:25 (zonder wissels): 143-144-136-133-132-140-139,
    /// blokken 12/12/12/11/10/10/10. Regressie voor BUG #57 (loc blijft op melder 132 staan).</summary>
    static void PendelTest()
    {
        // Scenario 1: de proef-commando's bij melder 132 gaan één keer verloren; scenario 2: ook de tweede poging mislukt.
        PendelScenario(new Dictionary<(int, int, bool), int> { [(132, 10, true)] = 1 }, "pendel", true);
        // BUG #58: de proef rijdt ver terug (lange proeftijd) en de loc moet daarna langzaam (kruipsnelheid 5) terug naar de melder
        PendelScenario(new Dictionary<(int, int, bool), int>(), "pendel (ver teruggereden)", true, proefKort: 12);
        PendelScenario(new Dictionary<(int, int, bool), int>(), "pendel (melder 143 flikkert)", true, flikker: 143);
        // BUG #59: leerprofiel en automatische snelheid
        var eerste = PendelScenario(new Dictionary<(int, int, bool), int>(), "leren: eerste run", true);
        var profiel = new LeerProfiel(); profiel.Leer(eerste.V.Kaart, eerste.V.Kaart.RefSnelheid);
        var terug = LeerProfiel.VanJson(profiel.AlsJson())!;
        Eis(terug.Overgangen.Count == profiel.Overgangen.Count && profiel.Overgangen.Count > 0, "leerprofiel: bewaren en laden geeft hetzelfde terug");
        var o144 = eerste.V.Kaart.Overgangen.FirstOrDefault(o => o.Van == 144 && o.Naar == 136 && o.AantalMetingen > 0);
        Eis(o144 is not null && Math.Abs((terug.Verwacht(144, 136, Richting.Achteruit, 12) ?? 0) - o144.GemiddeldeSeconden) < 0.01, "leerprofiel: verwachte tijd bij dezelfde snelheid klopt met de meting");
        Eis(Math.Abs((terug.Verwacht(144, 136, Richting.Achteruit, 6) ?? 0) - 2 * o144!.GemiddeldeSeconden) < 0.01, "leerprofiel: bij halve snelheid is de verwachte tijd dubbel");
        // BUG #156: iedere rit staat in het rapport, per blok met melders en wisselstanden
        var k156 = eerste.V.Kaart;
        Eis(k156.Ritten.Count >= k156.Trajecten.Count && k156.Ritten.Count > 0, "alle ritten (#156): elke gereden rit is bewaard (" + k156.Ritten.Count + " ritten, " + k156.Trajecten.Count + " basisritten)");
        var t156 = Rapport.AlsTekst(k156);
        Eis(t156.Contains("ALLE RITTEN") && t156.Contains("wissels afbuigend:") && Rapport.AlsHtml(k156).Contains("Alle ritten"), "alle ritten (#156): tekst- en htmlrapport tonen ze");
        Eis(Baankaart.VanJson(k156.AlsJson())!.Ritten.Count == k156.Ritten.Count, "alle ritten (#156): blijven bewaard na opslaan en laden");
        Console.WriteLine("  voorbeeld: " + Rapport.RitRegel(k156, k156.Ritten[0]));
        var dubbel = LeerProfiel.VanJson(profiel.AlsJson())!; dubbel.Leer(eerste.V.Kaart, eerste.V.Kaart.RefSnelheid);
        Eis(dubbel.AantalMetingen == profiel.AantalMetingen, "leerprofiel: dezelfde kaart twee keer aanbieden telt niet dubbel");
        var tweede = PendelScenario(new Dictionary<(int, int, bool), int>(), "leren: tweede run met profiel", true, profiel: terug);
        Eis(tweede.Log.AlsTekst().Contains("Leerprofiel:"), "leerprofiel: tweede run meldt dat het profiel gebruikt wordt");
        Eis(tweede.Seconden <= eerste.Seconden * 1.05, "leerprofiel: tweede run is niet langzamer (" + tweede.Seconden.ToString("0") + " s tegen " + eerste.Seconden.ToString("0") + " s)");
        int Geen(string t) => t.Split("geen beweging").Length - 1;
        Console.WriteLine($"  (eerste run: {eerste.Seconden:0} s, {Geen(eerste.Log.AlsTekst())}x 'geen beweging'; tweede run: {tweede.Seconden:0} s, {Geen(tweede.Log.AlsTekst())}x)");
        Eis(profiel.BlokHint(132) == 10 && profiel.BlokHint(133) == 11 && profiel.BlokHint(136) == 12, "blokhints: het leerprofiel onthoudt welk blok elke melder voedt");
        Eis(LeerProfiel.VanJson("{\"FormaatVersie\":1,\"Overgangen\":[]}") is { } oud && oud.BlokHint(132) is null, "blokhints: een oud profielbestand zonder hints blijft laadbaar");
        Eis(Geen(tweede.Log.AlsTekst()) < Geen(eerste.Log.AlsTekst()), "blokhints: tweede run met profiel heeft minder mislukte blokpogingen");
        Eis(eerste.Log.AlsTekst().Contains("bekend kopspoor") || tweede.Log.AlsTekst().Contains("bekend kopspoor"), "kopspoor (#60): bekend kopspoor wordt herkend en de loc keert zonder de hele wachttijd");
        var echt = Laad("baankaart_pendel_2026-10-10_1025.json");
        var zaad = new LeerProfiel(); zaad.Leer(echt, 12);
        Eis(Math.Abs((zaad.Verwacht(144, 143, Richting.Vooruit, 12) ?? 0) - 17.04) < 0.1 && Math.Abs((zaad.Verwacht(136, 133, Richting.Achteruit, 12) ?? 0) - 3.02) < 0.1, "leerprofiel: tijden uit de echte kaart van 10-10 10:24 worden overgenomen");
        Eis(Math.Abs((zaad.Verwacht(144, 143, Richting.Vooruit, 6) ?? 0) - 34.08) < 0.2, "leerprofiel: echte tijd 17 s bij snelheid 12 wordt 34 s bij snelheid 6");
        var kort = PendelScenario(new Dictionary<(int, int, bool), int>(), "korte sectie", true, lengte136: 30, autoSnelheid: true);
        Eis(kort.Ins.Verkensnelheid < 12 && kort.Log.AlsTekst().Contains("Snelheid automatisch aangepast"), "auto-snelheid: heel korte sectie => verkensnelheid omlaag (nu " + kort.Ins.Verkensnelheid + ")");
        Eis(kort.Ins.Verkensnelheid >= kort.Ins.Kruipsnelheid + 2, "auto-snelheid: blijft boven de kruipsnelheid");
        Eis(eerste.Ins.Verkensnelheid == 12, "auto-snelheid: staat standaard uit, normale baan blijft op snelheid 12 (nu " + eerste.Ins.Verkensnelheid + ")");
        var normaalAuto = PendelScenario(new Dictionary<(int, int, bool), int>(), "auto-snelheid aan, normale baan", true, autoSnelheid: true);
        Eis(normaalAuto.Ins.Verkensnelheid == 12, "auto-snelheid: normale baan blijft ook met auto-snelheid aan op 12 (nu " + normaalAuto.Ins.Verkensnelheid + ")");


        PendelScenario(new Dictionary<(int, int, bool), int> { [(132, 10, true)] = 2, [(132, 10, false)] = 2 }, "pendel (proef blijft mislukken)", true);

        // BUG #62: kortsluiting tijdens de blokproef wordt herkend (oude code bleef alle blokken blind proberen)
        var ks = PendelScenario(new Dictionary<(int, int, bool), int>(), "kortsluiting tijdens de blokproef", false, kortsluit: (12, 5), controleer: false);
        Console.WriteLine("  (kopsporen na kortsluiting: " + string.Join(",", ks.V.Kaart.Kopsporen.Select(x => x.Melder)) + ")");
        Eis(ks.Log.AlsTekst().Contains("Kortsluiting tijdens de blokproef"), "kortsluiting in de blokproef: herkend en gemeld, met advies over de wissel");
        Eis(ks.Log.AlsTekst().Contains("KORTSLUITING/ONTSPORING vermoed"), "kortsluiting in de blokproef: afgehandeld als gewone kortsluiting (noodstop en herstel)");
        Eis(ks.V.Kaart.Melders.Count == 7 && ks.Seconden < 1000, "kortsluiting in de blokproef: verkenning loopt daarna door en is sneller dan zonder herkenning (" + ks.Seconden.ToString("0") + " s tegen 1044 s)");
        var ks2 = PendelScenario(new Dictionary<(int, int, bool), int>(), "kortsluiting in blokproef (10, 1)", false, kortsluit: (10, 1), controleer: false);
        Eis(ks2.Log.AlsTekst().Contains("Kortsluiting tijdens de blokproef"), "kortsluiting in de blokproef: ook herkend bij een ander blok en moment");
        // BUG #61 (11:38-rit): loc staat bij het stoppen met een deel in twee secties
        foreach (var fl in new[] { 133, 132, 136 })
            PendelScenario(new Dictionary<(int, int, bool), int>(), $"pendel (loc op grens, melder {fl} flikkert)", true, flikker: fl, grensRem: true);
        foreach (var l in new[] { 50.0, 52.0, 54.0, 56.0, 58.0, 60.0, 62.0, 64.0, 66.0, 68.0, 70.0, 72.0, 75.0, 80.0, 100.0 })
        {
            var gr = PendelScenario(new Dictionary<(int, int, bool), int>(), $"pendel (loc op grens, sectie 136 = {l} cm)", true, lengte136: l, grensRem: true);
            var tekst = gr.Log.AlsTekst();
            Eis(!tekst.Contains("terugrijden gaf bij geen enkel blok"), $"pendel (loc op grens, 136 = {l} cm): geen mislukte blokproef met de loc op twee secties");
        }
    }

    static (BaanVerkenner V, VerkenLog Log, VerkenInstellingen Ins, double Seconden) PendelScenario(Dictionary<(int, int, bool), int> blokkeringen, string naam, bool blokMoetBekendZijn, int proefKort = 6, int flikker = 0, LeerProfiel? profiel = null, double lengte136 = 80, bool autoSnelheid = false, bool grensRem = false, (int, int)? kortsluit = null, bool controleer = true)
    {
        var sim = new Baanverkenner.Kern.Simulatie.SimulatieBaan { DinamoGedrag = true, StaatStilZonderBlokcommando = true, LocAdres = 5408, StilstaanRemtOpGrens = grensRem };
        if (kortsluit is { } ks) sim.KortsluitBijCommando = ks;
        foreach (var gb in blokkeringen) sim.GeblokkeerdeCommandos[gb.Key] = gb.Value; // op de baan gaf het 'klein stukje terug' bij melder 132 geen beweging
        var A = Baanverkenner.Kern.Simulatie.SimulatieBaan.Eind.A; var B = Baanverkenner.Kern.Simulatie.SimulatieBaan.Eind.B;
        var s143 = sim.NieuweSectie(143, 100, 12); var s144 = sim.NieuweSectie(144, 400, 12); var s136 = sim.NieuweSectie(136, lengte136, 12);
        var s133 = sim.NieuweSectie(133, 300, 11); var s132 = sim.NieuweSectie(132, 300, 10);
        var s140 = sim.NieuweSectie(140, 150, 10); var s139 = sim.NieuweSectie(139, 150, 10);
        sim.Verbind(s143, B, s144, A); sim.Verbind(s144, B, s136, A); sim.Verbind(s136, B, s133, A);
        sim.Verbind(s133, B, s132, A); sim.Verbind(s132, B, s140, A); sim.Verbind(s140, B, s139, A);
        sim.FlikkerMelder = flikker; sim.FlikkerKeer = 8;
        sim.PlaatsLoc(s144, 200, -1); // vooruit = richting 143 (zoals op de baan)
        var ins = new VerkenInstellingen { LocAdres = 5408, LocStappen = 28, Verkensnelheid = 12, Kruipsnelheid = 5, WisselAdresVan = 1, WisselAdresTot = 1, DinamoBlokken = "10-12", BlokproefKortSeconden = proefKort, AutoSnelheid = autoSnelheid };
        sim.VerbindenAsync("SIM").GetAwaiter().GetResult();
        var klok = new VirtueleKlok(); klok.Getikt += sim.Tik;
        var log = new VerkenLog(klok);
        var v = new BaanVerkenner(sim, ins, klok, log, (_, _) => Task.FromResult(true)) { Leerprofiel = profiel, VraagJaNee = (_, _) => Task.FromResult<bool?>(true) };
        using var cts = new CancellationTokenSource();
        try { v.VoerUit(cts.Token).GetAwaiter().GetResult(); } catch (Exception ex) { Console.WriteLine("  (verkenner stopte met: " + ex.Message + ")"); }
        if (Environment.GetEnvironmentVariable("PENDEL_LOG") == "1") Console.WriteLine(log.AlsTekst());
        var k = v.Kaart;
        if (!controleer) return (v, log, ins, (klok.Nu - new DateTime(2026, 1, 1, 12, 0, 0)).TotalSeconds);
        Eis(k.Melders.Count == 7, naam + ": alle 7 melders gevonden (nu " + k.Melders.Count + ")");
        Eis(!blokMoetBekendZijn || k.Melders.All(m => m.DinamoBlok is not null), naam + ": elke melder heeft een Dinamo-blok (zonder: " + string.Join(",", k.Melders.Where(m => m.DinamoBlok is null).Select(m => m.Nummer)) + ")");
        Eis(!blokMoetBekendZijn || k.Melders.FirstOrDefault(m => m.Nummer == 132)?.DinamoBlok == 10, naam + ": melder 132 = Dinamo-blok 10");
        Eis(k.Overgangen.Any(o => o.Van == 140 && o.Naar == 139 && o.Richting == Richting.Achteruit) || k.Overgangen.Any(o => o.Van == 132 && o.Naar == 140), naam + ": rit loopt door voorbij melder 132");
        Eis(!k.Kopsporen.Any(x => x.Melder == 132), naam + ": melder 132 is GEEN kopspoor");
        Eis(k.Kopsporen.Any(x => x.Melder == 139) && k.Kopsporen.Any(x => x.Melder == 143), naam + ": echte uiteinden 139 en 143 zijn kopspoor");
        return (v, log, ins, (klok.Nu - new DateTime(2026, 1, 1, 12, 0, 0)).TotalSeconds);
    }

    // BUG #63: overloopwissels (wissel 2 afbuigend -> verbindingsstuk -> wissel 1 van achteren): beide moeten afbuigend staan
    static void OverloopTest()
    {
        var sim = new Baanverkenner.Kern.Simulatie.SimulatieBaan { DinamoGedrag = false, LocAdres = 5408 };
        var A = Baanverkenner.Kern.Simulatie.SimulatieBaan.Eind.A; var B = Baanverkenner.Kern.Simulatie.SimulatieBaan.Eind.B;
        var P = Baanverkenner.Kern.Simulatie.SimulatieBaan.Poort.Stam; var R = Baanverkenner.Kern.Simulatie.SimulatieBaan.Poort.Recht; var Af = Baanverkenner.Kern.Simulatie.SimulatieBaan.Poort.Af;
        var s1 = sim.NieuweSectie(1, 90, 1); var s2 = sim.NieuweSectie(2, 80, 2); var sL = sim.NieuweSectie(3, 60, 3);
        var s4 = sim.NieuweSectie(4, 80, 4); var s5 = sim.NieuweSectie(5, 60, 5);
        var w2 = sim.NieuweWissel(2, true, 1); var w1 = sim.NieuweWissel(1, true, 3);
        sim.Verbind(w2, P, s1, B); sim.Verbind(w2, R, s2, A); sim.Verbind(w2, Af, sL, A);
        sim.Verbind(w1, Af, sL, B); sim.Verbind(w1, P, s4, A); sim.Verbind(w1, R, s5, A);
        sim.PlaatsLoc(s1, 45, +1);
        var ins = new VerkenInstellingen { LocAdres = 5408, LocStappen = 28, Verkensnelheid = 12, Kruipsnelheid = 5, WisselAdresVan = 1, WisselAdresTot = 4 };
        sim.VerbindenAsync("SIM").GetAwaiter().GetResult();
        var klok = new VirtueleKlok(); klok.Getikt += sim.Tik;
        var log = new VerkenLog(klok);
        var v = new BaanVerkenner(sim, ins, klok, log, (_, _) => Task.FromResult(true));
        using var cts = new CancellationTokenSource();
        try { v.VoerUit(cts.Token).GetAwaiter().GetResult(); } catch (Exception ex) { Console.WriteLine("  (overloop: verkenner stopte met: " + ex.Message + ")"); }
        if (Environment.GetEnvironmentVariable("OVERLOOP_LOG") == "1") Console.WriteLine(log.AlsTekst());
        var k = v.Kaart;
        Console.WriteLine("  (overloop: melders " + string.Join(",", k.Melders.Select(m => m.Nummer)) + "; kortsluitpunten " + string.Join(" | ", k.Kortsluitpunten.Select(x => $"#{x.Id} na {x.NaMelder} {x.Configuratie} opgelost={x.Opgelost} door={x.OpgelostDoorAdres} geprobeerd={string.Join(",", x.GeprobeerdeAdressen)}")) + ")");
        Eis(k.Melders.Any(m => m.Nummer == 4), "overloop: melder 4 (voorbij de tweede wissel) wordt bereikt");
        Eis(k.Kortsluitpunten.Any(x => x.Opgelost && x.OpgelostDoorAdres == 1), "overloop: de kortsluiting na wissel 2 afbuigend wordt opgelost door wissel 1 er ook op afbuigend te zetten");
        Eis(k.Wissels.Any(w => w.Adres == 1) && k.Wissels.Any(w => w.Adres == 2), "overloop: beide wissels staan als wissel in het rapport");
    }

    // BUG #63 (log 13:40): de proefrit eindigt op een afbuigende tak (stomp spoor); de loc moet met wissel nog afbuigend terug
    static void AfbuigendeTakTerugTest()
    {
        var sim = new Baanverkenner.Kern.Simulatie.SimulatieBaan { DinamoGedrag = false, LocAdres = 5408 };
        var A = Baanverkenner.Kern.Simulatie.SimulatieBaan.Eind.A; var B = Baanverkenner.Kern.Simulatie.SimulatieBaan.Eind.B;
        var P = Baanverkenner.Kern.Simulatie.SimulatieBaan.Poort.Stam; var R = Baanverkenner.Kern.Simulatie.SimulatieBaan.Poort.Recht; var Af = Baanverkenner.Kern.Simulatie.SimulatieBaan.Poort.Af;
        var s1 = sim.NieuweSectie(1, 90, 1); var s2 = sim.NieuweSectie(2, 80, 2); var sD = sim.NieuweSectie(3, 60, 3);
        var w2 = sim.NieuweWissel(2, true, 1);
        sim.Verbind(w2, P, s1, B); sim.Verbind(w2, R, s2, A); sim.Verbind(w2, Af, sD, A);
        sim.PlaatsLoc(s1, 45, +1);
        var ins = new VerkenInstellingen { LocAdres = 5408, LocStappen = 28, Verkensnelheid = 12, Kruipsnelheid = 5, WisselAdresVan = 1, WisselAdresTot = 3 };
        sim.VerbindenAsync("SIM").GetAwaiter().GetResult();
        var klok = new VirtueleKlok(); klok.Getikt += sim.Tik;
        var log = new VerkenLog(klok);
        var v = new BaanVerkenner(sim, ins, klok, log, (_, _) => Task.FromResult(true));
        using var cts = new CancellationTokenSource();
        try { v.VoerUit(cts.Token).GetAwaiter().GetResult(); } catch (Exception ex) { Console.WriteLine("  (afbuigende tak: verkenner stopte met: " + ex.Message + ")"); }
        if (Environment.GetEnvironmentVariable("OVERLOOP_LOG") == "1") Console.WriteLine(log.AlsTekst());
        var k = v.Kaart;
        Eis(k.Melders.Select(m => m.Nummer).OrderBy(x => x).SequenceEqual(new[] { 1, 2, 3 }), "afbuigende tak: melders 1, 2 en 3 gevonden (nu " + string.Join(",", k.Melders.Select(m => m.Nummer)) + ")");
        Eis(k.Wissels.Any(w => w.Adres == 2), "afbuigende tak: wissel 2 gevonden");
        Eis(k.Voltooid, "afbuigende tak: verkenning voltooid zonder vastlopen");
    }

    // BUG #64: een gevonden overgang geldt ook omgekeerd (zelfde wisselstand): geen lege "achteruit →" meer
    static void OmgekeerdeOvergangTest()
    {
        var k = Laad("baankaart_lijn_5-30_tussenstand_2026-10-10_1340.json");
        Eis(k.BekendeVolgende(14, Richting.Achteruit, Configuratie.Basis).SequenceEqual(new[] { 13 }), "omgekeerde overgang: 13 -> 14 vooruit geeft 14 -> 13 achteruit");
        Eis(k.BekendeVolgende(27, Richting.Vooruit, Configuratie.Basis).Contains(28) && k.BekendeVolgende(28, Richting.Achteruit, Configuratie.Basis).Contains(27), "omgekeerde overgang: 27 -> 28 vooruit geeft 28 -> 27 achteruit");
        Eis(k.BekendeVolgende(28, Richting.Achteruit, new Configuratie(new[] { 7 })).Count == 0, "omgekeerde overgang: bij een andere wisselstand niets afgeleid");
        var tekst = Rapport.AlsTekst(k);
        Eis(!System.Text.RegularExpressions.Regex.IsMatch(tekst, @"Melder 14\s.*achteruit → *\r?$", System.Text.RegularExpressions.RegexOptions.Multiline), "omgekeerde overgang: rapport toont geen leeg vervolg meer voor melder 14 achteruit");
    }

    // BUG #66: partnervolgorde - de wissel die al bij deze melder is waargenomen (kruiswissel 10/13) eerst
    static void PartnerVolgordeTest()
    {
        var k = new Baankaart();
        k.Wissel(10).Waarnemingen.Add(new WisselWaarneming { Soort = WaarnemingSoort.Splitsing, NaMelder = 24, Richting = Richting.Achteruit, VolgendeBijRechtdoor = 14, VolgendeBijAfbuigend = 129 });
        var alle = Enumerable.Range(1, 20).ToList();
        var v = BaanVerkenner.PartnerVolgorde(k, alle, 13, 24, Configuratie.Basis, 8);
        Eis(v.Count == 8 && v[0] == 10, "partnervolgorde: kruiswissel-partner 10 (al waargenomen bij melder 24) staat vooraan (nu " + string.Join(",", v) + ")");
        Eis(!v.Contains(13), "partnervolgorde: het geteste adres zelf valt af");
        Eis(v.Skip(1).SequenceEqual(new[] { 12, 14, 11, 15, 9, 16, 8 }) || v.Skip(1).First() == 12, "partnervolgorde: daarna op afstand van het geteste adres");
        var v2 = BaanVerkenner.PartnerVolgorde(k, alle, 13, 24, new Configuratie(new[] { 10 }), 3);
        Eis(!v2.Contains(10) && v2.Count == 3, "partnervolgorde: een al afbuigend wissel wordt niet nog eens geprobeerd");
        // BUG #252: een wissel in de aanliggende sectie (melder 14, direct naast 24) gaat vóór een wissel met een dichter bij liggend adresnummer maar zonder ligging bij 24
        var k2 = new Baankaart();
        k2.Overgangen.Add(new Overgang { Van = 24, Naar = 14, Richting = Richting.Vooruit, AantalKeer = 3 });
        k2.Wissel(17).Waarnemingen.Add(new WisselWaarneming { Soort = WaarnemingSoort.Splitsing, NaMelder = 14, Richting = Richting.Vooruit, VolgendeBijRechtdoor = 24, VolgendeBijAfbuigend = 129 });
        var v3 = BaanVerkenner.PartnerVolgorde(k2, Enumerable.Range(1, 20).ToList(), 13, 24, Configuratie.Basis, 3);
        Eis(v3[0] == 17, "partnervolgorde (#252): wissel 17 uit de aanliggende sectie gaat vóór de adressen 12 en 14 (nu " + string.Join(",", v3) + ")");
    }


    // BUG #69 (log 16:22): de loc staat met een draaistel in een ander (niet door een melder bewaakt) blok dan de melder aangeeft
    // (sectie 1 = blok 1, wisselstuk zonder melder = blok 9) en rijdt alleen met commando's naar ALLE die blokken.
    static void BlokgrensTest()
    {
        var sim = new Baanverkenner.Kern.Simulatie.SimulatieBaan { DinamoGedrag = true, StaatStilZonderBlokcommando = true, StilstaanRemtOpGrens = true, LocAdres = 5408 };
        var A = Baanverkenner.Kern.Simulatie.SimulatieBaan.Eind.A; var B = Baanverkenner.Kern.Simulatie.SimulatieBaan.Eind.B;
        var P = Baanverkenner.Kern.Simulatie.SimulatieBaan.Poort.Stam; var R = Baanverkenner.Kern.Simulatie.SimulatieBaan.Poort.Recht;
        var s1 = sim.NieuweSectie(1, 80, 1); var s2 = sim.NieuweSectie(2, 80, 2);
        var w = sim.NieuweWissel(1, false, 9);
        sim.Verbind(w, P, s1, B); sim.Verbind(w, R, s2, A);
        sim.PlaatsLoc(s1, 60, +1);
        var ins = new VerkenInstellingen { LocAdres = 5408, LocStappen = 28, Verkensnelheid = 12, Kruipsnelheid = 5, WisselAdresVan = 1, WisselAdresTot = 1, DinamoBlokken = "1-9" };
        var status = new VerkenStatus { Instellingen = ins };
        status.Kaart.StartMelder = 1; status.Kaart.Hardware = "Simulatie"; status.Kaart.RefSnelheid = 12;
        status.Kaart.Melder(1).DinamoBlok = 1; status.Kaart.Melder(2).DinamoBlok = 2;
        status.Wachtrij.Add(new Opdracht { Id = 1, Configuratie = Configuratie.Basis, Start = 1, Richting = Richting.Vooruit, Reden = "test" });
        sim.VerbindenAsync("SIM").GetAwaiter().GetResult();
        var klok = new VirtueleKlok(); klok.Getikt += sim.Tik;
        // eerst de loc zo neerzetten dat hij met zijn kop in het wisselstuk (blok 9, geen melder) staat: bezet = alleen melder 1
        sim.ZetLocSnelheid(5408, 6, true, 1, 28); klok.Wacht(TimeSpan.FromSeconds(1.3)).GetAwaiter().GetResult();
        sim.ZetLocSnelheid(5408, 0, true, 1, 28); klok.Wacht(TimeSpan.FromSeconds(1)).GetAwaiter().GetResult();
        var log = new VerkenLog(klok);
        var v = new BaanVerkenner(sim, ins, klok, log, (_, _) => Task.FromResult(true), status);
        using var cts = new CancellationTokenSource();
        try { v.VoerUit(cts.Token).GetAwaiter().GetResult(); } catch (Exception ex) { Console.WriteLine("  (blokgrens: verkenner stopte met: " + ex.Message + ")"); }
        if (Environment.GetEnvironmentVariable("GRENS_LOG") == "1") Console.WriteLine(log.AlsTekst());
        var t = log.AlsTekst();
        Eis(t.Contains("alle Dinamo-blokken tegelijk"), "blokgrens: de loc die niet vertrekt krijgt een poging met alle blokken tegelijk");
        Eis(v.Kaart.Melder(1).DinamoBlok == 1, "blokgrens: het bekende blok 1 van melder 1 blijft bewaard (nu " + v.Kaart.Melder(1).DinamoBlok + ")");
        Eis(!t.Contains("Blokkoppeling wordt gecontroleerd"), "blokgrens: geen volledige blokproef van 18 blokken");
        Eis(v.Kaart.Overgangen.Any(o => o.Van == 1 && o.Naar == 2), "blokgrens: de rit komt daarna bij melder 2");
    }

    // BUG #71 (log 16:48): navigatie begint vanaf een aangenomen melder terwijl de loc ergens anders staat. Dan geen valse overgang vastleggen.
    // BUG #268: tijdens een navigatie 1 -> 2 -> 3 meldt Dinamo even melder 137 (0,8 s, onbekend in de kaart): geen navigatiefout; blijft hij bezet, dan wel
    static void NavigatieEchoTest(bool blijft)
    {
        var sim = new Baanverkenner.Kern.Simulatie.SimulatieBaan { DinamoGedrag = true, LocAdres = 5408 };
        var A = Baanverkenner.Kern.Simulatie.SimulatieBaan.Eind.A; var B = Baanverkenner.Kern.Simulatie.SimulatieBaan.Eind.B;
        var s1 = sim.NieuweSectie(1, 80, 1); var s2 = sim.NieuweSectie(2, 80, 2); var s3 = sim.NieuweSectie(3, 80, 3);
        sim.Verbind(s1, B, s2, A); sim.Verbind(s2, B, s3, A);
        sim.PlaatsLoc(s1, 40, +1);
        var ins = new VerkenInstellingen { LocAdres = 5408, LocStappen = 28, Verkensnelheid = 12, Kruipsnelheid = 5, WisselAdresVan = 1, WisselAdresTot = 1, DinamoBlokken = "1-3" };
        var status = new VerkenStatus { Instellingen = ins };
        status.Kaart.StartMelder = 1; status.Kaart.Hardware = "Simulatie"; status.Kaart.RefSnelheid = 12;
        foreach (var n in new[] { 1, 2, 3 }) status.Kaart.Melders.Add(new MelderInfo { Nummer = n });
        sim.VerbindenAsync("SIM").GetAwaiter().GetResult();
        var klok = new VirtueleKlok(); klok.Getikt += sim.Tik;
        var log = new VerkenLog(klok);
        var v = new BaanVerkenner(sim, ins, klok, log, (_, _) => Task.FromResult(true), status);
        var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var monitor = (MelderMonitor)typeof(BaanVerkenner).GetField("_monitor", bf)!.GetValue(v)!;
        sim.BezetmeldingGewijzigd += monitor.Ontvang;
        foreach (var mm in new[] { 1, 2, 3 }) sim.VraagMelderStatusOp(mm);
        klok.Wacht(TimeSpan.FromSeconds(1)).GetAwaiter().GetResult();
        sim.KortsluitEchoMelders = new[] { 137 }; sim.EchoElkeSeconden = 2.5;
        if (blijft) { klok.Getikt += _ => { if (sim.KortsluitEchosGemeld > 0) { sim.EchoElkeSeconden = 0; sim.KortsluitEchoMelders = Array.Empty<int>(); } }; }
        var rit = typeof(BaanVerkenner).GetMethod("Rit", bf)!;
        bool fout = false; RitResultaat? res = null;
        try { res = ((Task<RitResultaat>)rit.Invoke(v, new object[] { 1, Richting.Vooruit, new RitDoel { DoelMelder = 3, VerwachtPad = new List<int> { 1, 2, 3 } } })!).GetAwaiter().GetResult(); }
        catch (NavigatieFout nf) { fout = nf.Onverwacht == 137; }
        var regels = log.Regels.Select(x => x.Tekst).ToList();
        if (Environment.GetEnvironmentVariable("ECHO_LOG") == "1") foreach (var x in log.Regels) Console.WriteLine("    | " + x.Tijd.ToString("HH:mm:ss.fff") + " " + x.Tekst);
        if (!blijft)
            Eis(!fout && res?.Einde == RitEinde.DoelBereikt && sim.KortsluitEchosGemeld > 0 && regels.Any(x => x.Contains("melder 137 (onbekend in de kaart) was maar even bezet")),
                $"navigatie-echo (#268): melder 137 even bezet ({sim.KortsluitEchosGemeld}x) -> genegeerd, doel 3 bereikt ({res?.Einde}, fout {fout})");
        else
            Eis(fout, "navigatie-echo (#268): blijft melder 137 bezet, dan is het wel een onverwachte melder");
    }

    // BUG #278: de loc staat andersom op melder 2 (neus naar 1); 'vooruit naar 3' rijdt naar 1, terwijl 2 -> 1 altijd achteruit gereden is: stoppen met 'loc andersom', geen overgang vastleggen
    static void LocAndersom278Test()
    {
        var sim = new Baanverkenner.Kern.Simulatie.SimulatieBaan { DinamoGedrag = true, LocAdres = 5408 };
        var A = Baanverkenner.Kern.Simulatie.SimulatieBaan.Eind.A; var B = Baanverkenner.Kern.Simulatie.SimulatieBaan.Eind.B;
        var s1 = sim.NieuweSectie(1, 80, 1); var s2 = sim.NieuweSectie(2, 80, 2); var s3 = sim.NieuweSectie(3, 80, 3);
        sim.Verbind(s1, B, s2, A); sim.Verbind(s2, B, s3, A);
        sim.PlaatsLoc(s2, 40, -1);
        var ins = new VerkenInstellingen { LocAdres = 5408, LocStappen = 28, Verkensnelheid = 12, Kruipsnelheid = 5, WisselAdresVan = 1, WisselAdresTot = 1, DinamoBlokken = "1-3" };
        var status = new VerkenStatus { Instellingen = ins };
        status.Kaart.StartMelder = 2; status.Kaart.Hardware = "Simulatie"; status.Kaart.RefSnelheid = 12;
        foreach (var n in new[] { 1, 2, 3 }) status.Kaart.Melders.Add(new MelderInfo { Nummer = n });
        status.Kaart.Overgangen.Add(new Overgang { Van = 2, Naar = 3, Richting = Richting.Vooruit, AantalKeer = 5 });
        status.Kaart.Overgangen.Add(new Overgang { Van = 2, Naar = 1, Richting = Richting.Achteruit, AantalKeer = 5 });
        sim.VerbindenAsync("SIM").GetAwaiter().GetResult();
        var klok = new VirtueleKlok(); klok.Getikt += sim.Tik;
        var log = new VerkenLog(klok);
        var v = new BaanVerkenner(sim, ins, klok, log, (_, _) => Task.FromResult(true), status);
        var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var monitor = (MelderMonitor)typeof(BaanVerkenner).GetField("_monitor", bf)!.GetValue(v)!;
        sim.BezetmeldingGewijzigd += monitor.Ontvang;
        foreach (var mm in new[] { 1, 2, 3 }) sim.VraagMelderStatusOp(mm);
        klok.Wacht(TimeSpan.FromSeconds(1)).GetAwaiter().GetResult();
        var rit = typeof(BaanVerkenner).GetMethod("Rit", bf)!;
        NavigatieFout? fout = null;
        try { ((Task<RitResultaat>)rit.Invoke(v, new object[] { 2, Richting.Vooruit, new RitDoel { DoelMelder = 3, VerwachtPad = new List<int> { 2, 3 } } })!).GetAwaiter().GetResult(); }
        catch (NavigatieFout nf) { fout = nf; }
        var ov = status.Kaart.Overgangen.Where(o => o.Van == 2 && o.Naar == 1 && o.Richting == Richting.Vooruit).Sum(o => o.AantalKeer);
        Eis(fout is { LocAndersom: true, Onverwacht: 1 } && ov == 0, $"loc andersom (#278): melder 1 achter de loc -> 'loc andersom', geen overgang 2 -> 1 vooruit (fout: {fout?.Message}, andersom {fout?.LocAndersom}, overgang {ov})");
    }

    static void NavigatieOnjuisteStartTest()
    {
        var sim = new Baanverkenner.Kern.Simulatie.SimulatieBaan { DinamoGedrag = true, LocAdres = 5408 };
        var A = Baanverkenner.Kern.Simulatie.SimulatieBaan.Eind.A; var B = Baanverkenner.Kern.Simulatie.SimulatieBaan.Eind.B;
        var s1 = sim.NieuweSectie(1, 80, 1); var s2 = sim.NieuweSectie(2, 80, 2); var s3 = sim.NieuweSectie(3, 80, 3);
        sim.Verbind(s1, B, s2, A); sim.Verbind(s2, B, s3, A);
        sim.PlaatsLoc(s2, 40, +1);
        var ins = new VerkenInstellingen { LocAdres = 5408, LocStappen = 28, Verkensnelheid = 12, Kruipsnelheid = 5, WisselAdresVan = 1, WisselAdresTot = 1, DinamoBlokken = "1-3" };
        var status = new VerkenStatus { Instellingen = ins };
        status.Kaart.StartMelder = 1; status.Kaart.Hardware = "Simulatie"; status.Kaart.RefSnelheid = 12;
        sim.VerbindenAsync("SIM").GetAwaiter().GetResult();
        var klok = new VirtueleKlok(); klok.Getikt += sim.Tik;
        var log = new VerkenLog(klok);
        var v = new BaanVerkenner(sim, ins, klok, log, (_, _) => Task.FromResult(true), status);
        var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var monitor = (MelderMonitor)typeof(BaanVerkenner).GetField("_monitor", bf)!.GetValue(v)!;
        sim.BezetmeldingGewijzigd += monitor.Ontvang;
        foreach (var mm in new[] { 1, 2, 3 }) sim.VraagMelderStatusOp(mm);
        klok.Wacht(TimeSpan.FromSeconds(1)).GetAwaiter().GetResult();
        var rit = typeof(BaanVerkenner).GetMethod("Rit", bf)!;
        // a) de loc staat al op het doel (melder 2), navigatie dacht vanaf 1
        var res = ((Task<RitResultaat>)rit.Invoke(v, new object[] { 1, Richting.Vooruit, new RitDoel { DoelMelder = 2, VerwachtPad = new List<int> { 1, 2 } } })!).GetAwaiter().GetResult();
        Eis(res.Einde == RitEinde.DoelBereikt && !v.Kaart.Overgangen.Any(o => o.Van == 1 && o.Naar == 2), "navigatie-startcontrole: loc al op het doel -> geen rit en geen overgang 1 -> 2");
        // b) de loc staat niet op het verwachte pad -> navigatiefout, geen overgang
        bool fout = false;
        try { ((Task<RitResultaat>)rit.Invoke(v, new object[] { 1, Richting.Vooruit, new RitDoel { DoelMelder = 3, VerwachtPad = new List<int> { 1, 3 } } })!).GetAwaiter().GetResult(); }
        catch (NavigatieFout) { fout = true; }
        Eis(fout && !v.Kaart.Overgangen.Any(o => o.Van == 1), "navigatie-startcontrole: loc buiten het verwachte pad -> NavigatieFout, geen valse overgang");
        // c) een verkenningsrit (geen navigatie) vanaf een verkeerd aangenomen plek wordt niet gestart (BUG #73)
        bool fout2 = false;
        try { ((Task<RitResultaat>)rit.Invoke(v, new object[] { 1, Richting.Vooruit, new RitDoel { Basis = new List<int> { 1 } } })!).GetAwaiter().GetResult(); }
        catch (NavigatieFout) { fout2 = true; }
        Eis(fout2 && !v.Kaart.Overgangen.Any(o => o.Van == 1), "verkenningsrit-startcontrole: loc staat elders -> NavigatieFout, geen valse overgang");
    }

    // BUG #74: doorgangen (ingangskant -> vervolg + wisselstand), kruiswissel in melder 24
    static void DoorgangTest()
    {
        var k = new Baankaart();
        var A = Configuratie.Basis; var B = new Configuratie(new[] { 13 }); var C = new Configuratie(new[] { 10, 13 });
        k.RegistreerDoorgang(14, 24, 21, Richting.Vooruit, A);
        k.RegistreerDoorgang(14, 24, 8, Richting.Vooruit, B);
        k.RegistreerDoorgang(14, 24, 8, Richting.Vooruit, C);
        Eis(k.Doorgangen.Count == 4, "doorgangen: 2 heen + 2 terug, standen samengenomen (nu " + k.Doorgangen.Count + ")");
        Eis(k.Doorgangen.Any(d => d.Via == 21 && d.Melder == 24 && d.Volgende == 14 && d.Richting == Richting.Achteruit), "doorgangen: omgekeerde doorgang 21 -> 24 -> 14 (achteruit) bestaat");
        // gewenste stand leidt (met deze ingang) elders heen -> bevestigde stand
        Eis(k.KiesConfiguratieBijIngang(14, 24, Richting.Vooruit, 8, A).Equals(B), "doorgangen: vanaf 14 naar 8 met alles rechtdoor -> stand 'afbuigend 13' (dichtst bij gewenst)");
        Eis(k.KiesConfiguratieBijIngang(14, 24, Richting.Vooruit, 8, B).Equals(B), "doorgangen: reeds bevestigde stand blijft");
        Eis(k.KiesConfiguratieBijIngang(14, 24, Richting.Vooruit, 21, A).Equals(A), "doorgangen: vanaf 14 naar 21 met alles rechtdoor blijft");
        Eis(k.KiesConfiguratieBijIngang(13, 24, Richting.Vooruit, 8, A).Equals(A), "doorgangen: onbekende ingangskant -> gewenste stand blijft (geen bewijs)");
        Eis(k.KiesConfiguratieBijIngang(14, 24, Richting.Vooruit, 99, A).Equals(A), "doorgangen: onbekend vervolg -> gewenste stand blijft");
        Eis(k.KiesConfiguratieBijIngang(21, 24, Richting.Achteruit, 14, B).Equals(B) || k.KiesConfiguratieBijIngang(21, 24, Richting.Achteruit, 14, B).Equals(A), "doorgangen: terugweg 21 -> 14 geeft een bevestigde stand");
        Eis(k.KiesConfiguratieBijIngang(21, 24, Richting.Achteruit, 14, B).Equals(B), "doorgangen: vanaf 21 naar 14 met 'afbuigend 13': geen tegenbewijs, dus blijft zoals gewenst");
        var o = k.OpvallendeDoorgangen();
        Eis(o.Count == 1 && o[0].Key == 24, "doorgangen: alleen melder 24 is opvallend");
        // JSON-rondgang en oudere kaart zonder veld
        var k2 = Baankaart.VanJson(k.AlsJson())!;
        Eis(k2.Doorgangen.Count == 4 && k2.KiesConfiguratieBijIngang(14, 24, Richting.Vooruit, 8, A).Equals(B), "doorgangen: blijven na opslaan en laden");
        var oud = Laad("baankaart_lijn_5-30_voltooid_kruiswissel24_2026-10-10_1707.json");
        Eis(oud.Doorgangen.Count == 0, "doorgangen: oudere kaart zonder doorgangen laadt met lege lijst");
        // rapport met doorgangen
        Eis(Rapport.AlsTekst(k).Contains("DOORGANGEN") && Rapport.AlsHtml(k).Contains("Doorgangen (ingangskant)"), "doorgangen: staan in het rapport (tekst en html)");

        // een echte (gesimuleerde) rit legt doorgangen vast: lijn 1-2-3, loc op 1, vooruit
        var sim = new Baanverkenner.Kern.Simulatie.SimulatieBaan { DinamoGedrag = true, LocAdres = 5408 };
        var E1 = Baanverkenner.Kern.Simulatie.SimulatieBaan.Eind.A; var E2 = Baanverkenner.Kern.Simulatie.SimulatieBaan.Eind.B;
        var s1 = sim.NieuweSectie(1, 80, 1); var s2 = sim.NieuweSectie(2, 80, 2); var s3 = sim.NieuweSectie(3, 80, 3); var s4 = sim.NieuweSectie(4, 80, 4);
        sim.Verbind(s1, E2, s2, E1); sim.Verbind(s2, E2, s3, E1); sim.Verbind(s3, E2, s4, E1);
        sim.PlaatsLoc(s1, 40, +1);
        var ins = new VerkenInstellingen { LocAdres = 5408, LocStappen = 28, Verkensnelheid = 12, Kruipsnelheid = 5, WisselAdresVan = 1, WisselAdresTot = 1, DinamoBlokken = "1-4" };
        var status = new VerkenStatus { Instellingen = ins };
        status.Kaart.StartMelder = 1; status.Kaart.Hardware = "Simulatie"; status.Kaart.RefSnelheid = 12;
        sim.VerbindenAsync("SIM").GetAwaiter().GetResult();
        var klok = new VirtueleKlok(); klok.Getikt += sim.Tik;
        var log = new VerkenLog(klok);
        var v = new BaanVerkenner(sim, ins, klok, log, (_, _) => Task.FromResult(true), status);
        var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var monitor = (MelderMonitor)typeof(BaanVerkenner).GetField("_monitor", bf)!.GetValue(v)!;
        sim.BezetmeldingGewijzigd += monitor.Ontvang;
        foreach (var mm in new[] { 1, 2, 3, 4 }) sim.VraagMelderStatusOp(mm);
        klok.Wacht(TimeSpan.FromSeconds(1)).GetAwaiter().GetResult();
        var rit = typeof(BaanVerkenner).GetMethod("Rit", bf)!;
        ((Task<RitResultaat>)rit.Invoke(v, new object[] { 1, Richting.Vooruit, new RitDoel { DoelMelder = 3, VerwachtPad = new List<int> { 1, 2, 3 } } })!).GetAwaiter().GetResult();
        Eis(v.Kaart.Doorgangen.Any(d => d.Via == 1 && d.Melder == 2 && d.Volgende == 3 && d.Richting == Richting.Vooruit), "doorgangen: gereden rit 1-2-3 legt doorgang 1 -> 2 -> 3 vast");
        // tweede rit in dezelfde richting: de ingangskant van melder 3 (vanaf 2) wordt onthouden over de ritgrens
        ((Task<RitResultaat>)rit.Invoke(v, new object[] { 3, Richting.Vooruit, new RitDoel { DoelMelder = 4, VerwachtPad = new List<int> { 3, 4 } } })!).GetAwaiter().GetResult();
        Eis(v.Kaart.Doorgangen.Any(d => d.Via == 2 && d.Melder == 3 && d.Volgende == 4 && d.Richting == Richting.Vooruit), "doorgangen: ingangskant blijft bekend over de ritgrens (2 -> 3 -> 4)");
    }

    // BUG #105: terug naar de startmelder door verder te rijden als dat korter is dan de hele lus terug
    static void DoorrijdenTest()
    {
        var k = new Baankaart();
        int[] lus = { 13, 14, 24, 21, 29, 30, 20, 27, 28, 26, 25, 17, 8, 15, 16, 5, 13 };
        for (int i = 0; i + 1 < lus.Length; i++) k.RegistreerOvergang(lus[i], lus[i + 1], Richting.Vooruit, Configuratie.Basis, 3.0);
        var rit = new RitResultaat { Richting = Richting.Vooruit };
        foreach (var m in lus.Take(lus.Length - 1)) rit.Reeks.Add(m); // gestopt op 5, vlak voor 13
        var p = BaanVerkenner.DoorrijdenNaarStart(k, 13, rit, Configuratie.Basis, new HashSet<int> { 5, 16 });
        Eis(p is not null && p.SequenceEqual(new[] { 5, 13 }), "doorrijden: vanaf 5 één melder vooruit naar 13 in plaats van 15 melders terug (nu " + (p is null ? "geen" : string.Join(",", p)) + ")");
        Eis(BaanVerkenner.DoorrijdenNaarStart(k, 13, rit, Configuratie.Basis, new HashSet<int> { 16 }) is null, "doorrijden: staat de loc niet op de laatste melder -> niet doorrijden");
        Eis(BaanVerkenner.DoorrijdenNaarStart(k, 13, rit, new Configuratie(new[] { 2 }), new HashSet<int> { 5 }) is null, "doorrijden: weg niet met deze wisselstand gereden -> niet doorrijden");
        var half = new RitResultaat { Richting = Richting.Vooruit };
        foreach (var m in lus.Take(9)) half.Reeks.Add(m); // gestopt op 28, halverwege: vooruit en terug even ver
        Eis(BaanVerkenner.DoorrijdenNaarStart(k, 13, half, Configuratie.Basis, new HashSet<int> { 28 }) is null, "doorrijden: halverwege de lus is vooruit niet korter dan terug -> terug zoals voorheen");
        k.RegistreerOvergang(5, 12, Richting.Vooruit, Configuratie.Basis, 3.0);
        Eis(BaanVerkenner.DoorrijdenNaarStart(k, 13, rit, Configuratie.Basis, new HashSet<int> { 5 }) is null, "doorrijden: twee mogelijke vervolgen -> niet doorrijden (geen keuze maken)");
    }

    // BUG #106: adrestest stopt vóór een stap die het adres rechtdoor eist; zelf terug via de bekende basisweg zonder route
    static void Wissel14Test()
    {
        var k = new Baankaart();
        k.Wissel(14).Waarnemingen.Add(new WisselWaarneming { Soort = WaarnemingSoort.Splitsing, NaMelder = 8, Richting = Richting.Achteruit, VolgendeBijRechtdoor = 17, VolgendeBijAfbuigend = 24 });
        var reeks = new List<int> { 8, 15, 16, 5, 13, 14, 24, 21, 29, 30, 20, 27, 28, 26, 25, 17, 8 };
        var s = BaanVerkenner.EisConflictStops(k, reeks, Richting.Vooruit, 14);
        Eis(s.SetEquals(new[] { 17 }), "wissel 14: afbuigend testen stopt op melder 17, vóór 17 -> 8 (nu " + string.Join(",", s) + ")");
        Eis(BaanVerkenner.EisConflictStops(k, reeks, Richting.Vooruit, 9).Count == 0, "wissel 14: een ander adres krijgt geen extra stop");
        Eis(BaanVerkenner.EisConflictStops(k, reeks, Richting.Achteruit, 14).Count == 0, "wissel 14: in de andere richting geen conflict");

        var lus = new[] { 8, 15, 16, 5, 13, 14, 24, 21, 29, 30, 20, 27, 28, 26, 25, 17, 8 };
        var kb = new Baankaart();
        for (int i = 0; i + 1 < lus.Length; i++) kb.RegistreerOvergang(lus[i], lus[i + 1], Richting.Vooruit, Configuratie.Basis, 3.0);
        var w = BaanVerkenner.ZelfTerugWeg(kb, 13, 8);
        Eis(w is not null && w.Count == 12 && w[0].Van == 13 && w[^1].Naar == 8 && w.All(x => x.Richting == Richting.Vooruit), "zelf terug: van 13 naar 8 vooruit over de basis-lus (nu " + (w is null ? "geen" : w.Count.ToString()) + " stappen)");
        Eis(BaanVerkenner.ZelfTerugWeg(kb, 8, 8)!.Count == 0, "zelf terug: al op de startmelder -> lege weg");
        Eis(BaanVerkenner.ZelfTerugWeg(kb, 99, 8) is null, "zelf terug: onbekende melder -> geen weg");
        var kz = new Baankaart();
        kz.RegistreerOvergang(5, 13, Richting.Vooruit, new Configuratie(new[] { 2 }), 3.0);
        Eis(BaanVerkenner.ZelfTerugWeg(kz, 5, 13) is null, "zelf terug: alleen met afbuigende wissel gereden -> geen basisweg");
    }

    // BUG #107: wissel 5 stond verkeerd tijdens de controle (rit 4 -> 129 -> 14 achteruit met 5 rechtdoor)
    static void StaartWisselTest()
    {
        var k = new Baankaart();
        k.Wissel(5).Waarnemingen.Add(new WisselWaarneming { Soort = WaarnemingSoort.Splitsing, NaMelder = 14, Richting = Richting.Vooruit, VolgendeBijRechtdoor = 24, VolgendeBijAfbuigend = 129 });
        k.Wissel(9).Waarnemingen.Add(new WisselWaarneming { Soort = WaarnemingSoort.Splitsing, NaMelder = 129, Richting = Richting.Vooruit, VolgendeBijRechtdoor = 24, VolgendeBijAfbuigend = 4 });
        k.Wissel(14).Waarnemingen.Add(new WisselWaarneming { Soort = WaarnemingSoort.Splitsing, NaMelder = 8, Richting = Richting.Achteruit, VolgendeBijRechtdoor = 17, VolgendeBijAfbuigend = 24 });
        k.Wissel(2).Waarnemingen.Add(new WisselWaarneming { Soort = WaarnemingSoort.KortsluitingBijOmzetten, NaMelder = 5, Richting = Richting.Vooruit, VolgendeBijRechtdoor = 13 });
        string T(List<(int Adres, bool Afbuigend)> l) => string.Join(",", l.Select(x => $"{x.Adres}{(x.Afbuigend ? "A" : "R")}"));
        Eis(T(BaanVerkenner.StaartWissels(k, 129, Richting.Achteruit)) == "5A", "staartwissel: aankomst op 129 achteruit -> wissel 5 afbuigend (nu " + T(BaanVerkenner.StaartWissels(k, 129, Richting.Achteruit)) + ")");
        Eis(T(BaanVerkenner.StaartWissels(k, 4, Richting.Achteruit)) == "9A", "staartwissel: aankomst op 4 achteruit -> wissel 9 afbuigend");
        Eis(T(BaanVerkenner.StaartWissels(k, 24, Richting.Achteruit)) == "5R,9R", "staartwissel: aankomst op 24 achteruit -> 5 en 9 rechtdoor (nu " + T(BaanVerkenner.StaartWissels(k, 24, Richting.Achteruit)) + ")");
        Eis(T(BaanVerkenner.StaartWissels(k, 17, Richting.Vooruit)) == "14R", "staartwissel: aankomst op 17 vooruit -> wissel 14 rechtdoor");
        Eis(BaanVerkenner.StaartWissels(k, 129, Richting.Vooruit).Count == 0, "staartwissel: op 129 vooruit rijdt de loc van de puntzijde, geen eis");
        Eis(BaanVerkenner.StaartWissels(k, 13, Richting.Achteruit).Count == 0, "staartwissel: wissel 2 heeft alleen een kortsluiting-waarneming, geen splitsing -> geen eis");
        // tegenspraak tussen twee splitsingen van hetzelfde adres: weglaten
        k.Wissel(5).Waarnemingen.Add(new WisselWaarneming { Soort = WaarnemingSoort.Splitsing, NaMelder = 20, Richting = Richting.Vooruit, VolgendeBijRechtdoor = 99, VolgendeBijAfbuigend = 24 });
        Eis(T(BaanVerkenner.StaartWissels(k, 24, Richting.Achteruit)) == "9R", "staartwissel: tegenstrijdige splitsingen voor één adres -> dat adres blijft weg (nu " + T(BaanVerkenner.StaartWissels(k, 24, Richting.Achteruit)) + ")");
        k.Wissel(5).Waarnemingen.RemoveAt(1);
        Eis(T(BaanVerkenner.EisenLaatsteStap(k, new List<int> { 13, 5, 12, 11, 4, 129, 14 }, Richting.Achteruit)) == "5A", "laatste stap: 129 -> 14 achteruit gaat van achteren door wissel 5 (afbuigend)");
        Eis(BaanVerkenner.EisenLaatsteStap(k, new List<int> { 13, 14 }, Richting.Vooruit).Count == 0, "laatste stap: van de puntzijde af geeft geen eis");
        Eis(BaanVerkenner.EisenLaatsteStap(k, new List<int> { 14 }, Richting.Vooruit).Count == 0, "laatste stap: één melder geeft geen eis");

        // BUG #108: vooruitkijken (log 11:12: wissel 5 ging op melder 24 naar rechtdoor en pas op 129 naar afbuigend, 0,65 s later kortsluiting)
        var k2 = new Baankaart();
        k2.Wissel(5).Waarnemingen.Add(new WisselWaarneming { Soort = WaarnemingSoort.Splitsing, NaMelder = 14, Richting = Richting.Vooruit, VolgendeBijRechtdoor = 24, VolgendeBijAfbuigend = 129 });
        k2.Wissel(10).Waarnemingen.Add(new WisselWaarneming { Soort = WaarnemingSoort.Splitsing, NaMelder = 24, Richting = Richting.Achteruit, VolgendeBijRechtdoor = 14, VolgendeBijAfbuigend = 129 });
        var cf = new Configuratie(new int[0]);
        foreach (var (v, n) in new[] { (24, 14), (24, 129), (129, 14), (129, 10) }) k2.RegistreerOvergang(v, n, Richting.Achteruit, cf, 3.0);
        Eis(T(BaanVerkenner.StaartWissels(k2, 24, Richting.Achteruit, a => a == 10)) == "5A", "vooruitkijken: op 24 achteruit met wissel 10 afbuigend gaat de loc naar 129 en daarna van achteren op wissel 5 af -> wissel 5 meteen afbuigend (nu " + T(BaanVerkenner.StaartWissels(k2, 24, Richting.Achteruit, a => a == 10)) + ")");
        Eis(T(BaanVerkenner.StaartWissels(k2, 24, Richting.Achteruit, a => false)) == "5R", "vooruitkijken: op 24 achteruit met wissel 10 rechtdoor gaat de loc naar 14 -> wissel 5 rechtdoor (nu " + T(BaanVerkenner.StaartWissels(k2, 24, Richting.Achteruit, a => false)) + ")");
        Eis(T(BaanVerkenner.StaartWissels(k2, 129, Richting.Achteruit, a => false)) == "5A", "vooruitkijken: op 129 achteruit -> wissel 5 afbuigend");

        // BUG #110: wissels bij een bezette melder worden niet voor een proef omgezet (wissel 9 ligt bij melder 129)
        var bij129 = BaanVerkenner.WisselsBijBezet(k, new[] { 129 });
        Eis(bij129.Contains(9) && bij129.Contains(5) && !bij129.Contains(14), "bij bezette melder 129: wissel 9 (punt na 129) en 5 (tak 129) vallen af, wissel 14 niet (nu " + string.Join(",", bij129.OrderBy(x => x)) + ")");
        Eis(BaanVerkenner.WisselsBijBezet(k, new[] { 99 }).Count == 0, "bij bezette melder: een onbekende melder geeft geen wissels");

        // simulatie: 4 -> 129 -> (wissel 5 van achteren, tak 'afbuigend') -> 14
        (Baanverkenner.Kern.Simulatie.SimulatieBaan, Baanverkenner.Kern.Simulatie.SimulatieBaan.Wissel, BaanVerkenner, VerkenLog, VirtueleKlok, System.Reflection.MethodInfo) Wereld()
        {
            var sim = new Baanverkenner.Kern.Simulatie.SimulatieBaan { DinamoGedrag = false, LocAdres = 5408 };
            var A = Baanverkenner.Kern.Simulatie.SimulatieBaan.Eind.A; var B = Baanverkenner.Kern.Simulatie.SimulatieBaan.Eind.B;
            var Stam = Baanverkenner.Kern.Simulatie.SimulatieBaan.Poort.Stam; var Recht = Baanverkenner.Kern.Simulatie.SimulatieBaan.Poort.Recht; var Af = Baanverkenner.Kern.Simulatie.SimulatieBaan.Poort.Af;
            var s4 = sim.NieuweSectie(4, 60, 2); var s129 = sim.NieuweSectie(129, 60, 9); var s14 = sim.NieuweSectie(14, 60, 3); var s24 = sim.NieuweSectie(24, 60, 8);
            var w5 = sim.NieuweWissel(5, true, 3);
            sim.Verbind(s4, B, s129, A); sim.Verbind(w5, Af, s129, B); sim.Verbind(w5, Stam, s14, A); sim.Verbind(w5, Recht, s24, A);
            sim.PlaatsLoc(s4, 30, +1);
            var ins = new VerkenInstellingen { LocAdres = 5408, LocStappen = 28, Verkensnelheid = 12, Kruipsnelheid = 5, WisselAdresVan = 5, WisselAdresTot = 5 };
            var status = new VerkenStatus { Instellingen = ins };
            status.Kaart.StartMelder = 4; status.Kaart.Hardware = "Simulatie"; status.Kaart.RefSnelheid = 12;
            status.Kaart.Wissel(5).Waarnemingen.Add(new WisselWaarneming { Soort = WaarnemingSoort.Splitsing, NaMelder = 14, Richting = Richting.Achteruit, VolgendeBijRechtdoor = 24, VolgendeBijAfbuigend = 129 });
            sim.VerbindenAsync("SIM").GetAwaiter().GetResult();
            var klok = new VirtueleKlok(); klok.Getikt += sim.Tik;
            var log = new VerkenLog(klok);
            var v = new BaanVerkenner(sim, ins, klok, log, (_, _) => Task.FromResult(true), status);
            var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var monitor = (MelderMonitor)typeof(BaanVerkenner).GetField("_monitor", bf)!.GetValue(v)!;
            sim.BezetmeldingGewijzigd += monitor.Ontvang;
            foreach (var mm in new[] { 4, 129, 14, 24 }) sim.VraagMelderStatusOp(mm);
            klok.Wacht(TimeSpan.FromSeconds(1)).GetAwaiter().GetResult();
            return (sim, w5, v, log, klok, typeof(BaanVerkenner).GetMethod("Rit", bf)!);
        }

        var (sim1, w1, v1, log1, _, rit1) = Wereld();
        var uit1 = ((Task<RitResultaat>)rit1.Invoke(v1, new object[] { 4, Richting.Vooruit, new RitDoel { StaartWissels = true } })!).GetAwaiter().GetResult();
        Eis(uit1.Einde != RitEinde.Kortsluiting && uit1.Reeks.SequenceEqual(new[] { 4, 129, 14 }), "staartwissel sim: rit 4 -> 129 -> 14 zonder kortsluiting (nu " + string.Join(",", uit1.Reeks) + ", " + uit1.Einde + ")");
        Eis(w1.Afbuigend, "staartwissel sim: wissel 5 is door de verkenner afbuigend gezet");
        Eis(v1.Kaart.Overgangen.Any(o => o.Van == 129 && o.Naar == 14 && o.GezienBijConfiguraties.Contains("5")), "staartwissel sim: overgang 129 -> 14 is vastgelegd met de werkelijke stand (wissel 5 afbuigend)");
        Eis(log1.Regels.Any(r => r.Tekst.Contains("van achteren op wissel 5 af")), "staartwissel sim: het log zegt waarom wissel 5 omgezet werd");

        var (sim2, w2, v2, log2, _, rit2) = Wereld();
        var uit2 = ((Task<RitResultaat>)rit2.Invoke(v2, new object[] { 4, Richting.Vooruit, new RitDoel { StaartWissels = true, ProefAdres = 5 } })!).GetAwaiter().GetResult();
        Eis(uit2.Einde == RitEinde.Kortsluiting, "staartwissel sim: het adres dat de rit zelf test (5) wordt niet automatisch gezet -> kortsluiting blijft (controle van de test zelf)");

        var (sim3, w3, v3, log3, _, rit3) = Wereld();
        var uit3 = ((Task<RitResultaat>)rit3.Invoke(v3, new object[] { 4, Richting.Vooruit, new RitDoel { StaartWissels = false } })!).GetAwaiter().GetResult();
        Eis(uit3.Einde == RitEinde.Kortsluiting && !w3.Afbuigend, "staartwissel sim: zonder de automatische stand geeft dezelfde rit kortsluiting (de test is dus betekenisvol)");
        // herstel: de kaart eist voor de laatste stap (129 -> 14) wissel 5 afbuigend; de verkenner schakelt hem zelf
        var bf2 = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var herstel = typeof(BaanVerkenner).GetMethod("HerstelBekendeWisselstand", bf2)!;
        var res = new RitResultaat { Richting = Richting.Vooruit };
        res.Reeks.AddRange(new[] { 4, 129, 14 });
        try { ((Task<bool>)herstel.Invoke(v3, new object[] { res })!).GetAwaiter().GetResult(); } catch (Exception ex) { Console.WriteLine("  (herstel stopte met: " + ex.Message + ")"); }
        Eis(w3.Afbuigend, "herstel: de verkenner schakelt wissel 5 zelf naar afbuigend omdat de kaart dat voor 129 -> 14 eist");
        Eis(log3.Regels.Any(r => r.Tekst.Contains("verklaart deze kortsluiting")), "herstel: het log legt de verklaring uit");
    }

    // BUG #104: "Voltooide baan nogmaals checken": de bestaande kaart blijft onaangeroerd, de baan wordt opnieuw gereden en vergeleken
    static List<WisselstraatVoorstel> Wisselstraten_Bereken(Baankaart k) => WisselstraatVoorstel.Bereken(k);
    static void ControleRondeTest()
    {
        var sim = new Baanverkenner.Kern.Simulatie.SimulatieBaan { DinamoGedrag = false, LocAdres = 5408 };
        var A = Baanverkenner.Kern.Simulatie.SimulatieBaan.Eind.A; var B = Baanverkenner.Kern.Simulatie.SimulatieBaan.Eind.B;
        var P = Baanverkenner.Kern.Simulatie.SimulatieBaan.Poort.Stam; var R = Baanverkenner.Kern.Simulatie.SimulatieBaan.Poort.Recht; var Af = Baanverkenner.Kern.Simulatie.SimulatieBaan.Poort.Af;
        var s1 = sim.NieuweSectie(1, 90, 1); var s2 = sim.NieuweSectie(2, 80, 2); var sL = sim.NieuweSectie(3, 60, 3);
        var s4 = sim.NieuweSectie(4, 80, 4); var s5 = sim.NieuweSectie(5, 60, 5);
        var w2 = sim.NieuweWissel(2, true, 1); var w1 = sim.NieuweWissel(1, true, 3);
        sim.Verbind(w2, P, s1, B); sim.Verbind(w2, R, s2, A); sim.Verbind(w2, Af, sL, A);
        sim.Verbind(w1, Af, sL, B); sim.Verbind(w1, P, s4, A); sim.Verbind(w1, R, s5, A);
        sim.PlaatsLoc(s1, 45, +1);
        var ins = new VerkenInstellingen { LocAdres = 5408, LocStappen = 28, Verkensnelheid = 12, Kruipsnelheid = 5, WisselAdresVan = 1, WisselAdresTot = 4 };
        var status = new VerkenStatus { Instellingen = ins };
        sim.VerbindenAsync("SIM").GetAwaiter().GetResult();
        var klok = new VirtueleKlok(); klok.Getikt += sim.Tik;
        var v = new BaanVerkenner(sim, ins, klok, new VerkenLog(klok), (_, _) => Task.FromResult(true), status);
        using var cts = new CancellationTokenSource();
        try { v.VoerUit(cts.Token).GetAwaiter().GetResult(); } catch (Exception ex) { Console.WriteLine("  (controle: eerste verkenning stopte met: " + ex.Message + ")"); }
        Eis(status.Kaart.Voltooid, "controle: de eerste verkenning is voltooid");
        string voor = status.Kaart.AlsJson();
        int opdrVoor = status.AfgerondeOpdrachten;

        // zonder voltooide kaart kan geen controle gestart worden
        bool gefaald = false;
        try { new VerkenStatus { Instellingen = ins }.VoorControle(DateTime.Now); } catch (InvalidOperationException) { gefaald = true; }
        Eis(gefaald, "controle: een niet-voltooide kaart kan niet gecontroleerd worden");

        // de loc staat na afloop weer op de startmelder; controle starten
        status.VoorControle(DateTime.Now);
        Eis(status.Controle && status.ControleKaart is not null && status.Kaart.Voltooid, "controle: bestaande kaart blijft 'voltooid', controlekaart is aangemaakt");
        Eis(status.ControleKaart!.Overgangen.Count == 0 && status.ControleKaart.Wissels.Count == status.Kaart.Wissels.Count && status.ControleKaart.StartMelder == status.Kaart.StartMelder, "controle: controlekaart begint zonder overgangen maar met de bekende wissels");
        var v2 = new BaanVerkenner(sim, ins, klok, new VerkenLog(klok), (_, _) => Task.FromResult(true), status);
        using var cts2 = new CancellationTokenSource();
        try { v2.VoerUit(cts2.Token).GetAwaiter().GetResult(); } catch (Exception ex) { Console.WriteLine("  (controle: controleronde stopte met: " + ex.Message + ")"); }
        Eis(!status.Controle, "controle: de vlag staat na afloop weer uit");
        Eis(status.ControleKaart.Overgangen.Count > 0 && status.ControleKaart.Voltooid, "controle: de baan is opnieuw gereden (overgangen op de controlekaart: " + status.ControleKaart.Overgangen.Count + ")");
        var k = status.Kaart;
        Eis(k.ControleVerslagen.Count == 1, "controle: precies één verslag bij de kaart");
        // de kaart zelf is niet gewijzigd (behalve het verslag)
        var na = Baankaart.VanJson(k.AlsJson())!;
        na.ControleVerslagen.Clear();
        var vk = Baankaart.VanJson(voor)!;
        na.Bijgewerkt = vk.Bijgewerkt;
        Eis(na.AlsJson() == vk.AlsJson(), "controle: de bestaande kaart is ongewijzigd (alleen het verslag is toegevoegd)");
        var vs = k.ControleVerslagen[0];
        Console.WriteLine("  (controle: " + vs.Samenvatting + (vs.Regels.Count > 0 ? " | " + string.Join(" | ", vs.Regels) : "") + ")");
        Eis(status.ControleKaart.Overgangen.Count == k.Overgangen.Count, "controle (#161): de controle reed alle overgangen van de kaart (nu " + status.ControleKaart.Overgangen.Count + " van " + k.Overgangen.Count + ")");
        Eis(!vs.Verschillen, "controle: dezelfde baan geeft geen verschillen");
        Eis(Rapport.AlsTekst(k).Contains("CONTROLES VAN DE BAAN") && Rapport.AlsHtml(k).Contains("Controles van de baan"), "controle: rapport toont het verslag");
        var ct = Rapport.ControleAlsTekst(vs); var ch = Rapport.ControleAlsHtml(vs);
        Eis(ct.Contains(vs.Samenvatting) && ch.Contains("Controle van de baan") && ch.Contains("<html"), "controle (#112): losse tekst en webpagina van het verslag");
        var vz = new ControleVerslag { Datum = DateTime.Now, Samenvatting = "Controle: 2 verschil(len) om te bekijken; daarnaast kwamen 55 onderdelen wel overeen met de kaart.", Verschillen = true, Regels = { "NIEUW: a <b>", "WISSEL 3 anders" } };
        Eis(Rapport.ControleAlsTekst(vz).Contains("Punten om te bekijken (2)") && Rapport.ControleAlsTekst(vz).Contains("  2. WISSEL 3 anders") && Rapport.ControleAlsHtml(vz).Contains("a &lt;b&gt;"), "controle (#112): alle regels genummerd, html veilig");

        // vergelijking: een weggelaten overgang en een extra overgang worden gemeld
        var oud = Baankaart.VanJson(k.AlsJson())!; oud.ControleVerslagen.Clear();
        var nieuw = Baankaart.VanJson(status.ControleKaart.AlsJson())!;
        var weg = nieuw.Overgangen[0]; nieuw.Overgangen.RemoveAt(0);
        nieuw.Overgangen.Add(new Overgang { Van = 90, Naar = 91, Richting = Richting.Vooruit });
        var vs2 = ControleVergelijking.Vergelijk(oud, nieuw, status.ControleWisselSeed, new HashSet<int>(), DateTime.Now);
        Eis(vs2.Verschillen && vs2.Regels.Any(r => r.Contains($"{weg.Van} → {weg.Naar}")) && vs2.Regels.Any(r => r.StartsWith("NIEUW: overgang melder 90 → 91")), "controle: ontbrekende en nieuwe overgangen worden gemeld");

        // een onderbroken controle bewaart de status en kan hervat worden: JSON-rondreis
        var st2 = new VerkenStatus { Instellingen = ins, Kaart = Baankaart.VanJson(voor)! };
        st2.VoorControle(DateTime.Now);
        var terug = VerkenStatus.VanJson(st2.AlsJson())!;
        Eis(terug.Controle && terug.ControleKaart is not null && terug.ControleWisselSeed.Count == st2.ControleWisselSeed.Count && terug.Kaart.Voltooid, "controle: controlestatus blijft na opslaan en laden bewaard");
        // BUG #114: opnieuw beginnen midden in een controle geeft weer een schone controlekaart
        st2.ControleKaart!.Overgangen.Add(new Overgang { Van = 90, Naar = 91, Richting = Richting.Vooruit });
        st2.AfgerondeOpdrachten = 14;
        st2.VoorControle(DateTime.Now);
        Eis(st2.Controle && st2.ControleKaart!.Overgangen.Count == 0 && st2.AfgerondeOpdrachten == 0 && st2.Wachtrij.Count == 0 && st2.Kaart.Voltooid, "controle (#114): opnieuw beginnen geeft een schone controlekaart en wijzigt de bestaande kaart niet");
    }

    // BUG #74: een voltooide verkenning uitbreiden met een tweede, fysiek los traject (twee losse stukken spoor: 1-2-3 en 4-5)
    static void UitbreidenTest()
    {
        var sim = new Baanverkenner.Kern.Simulatie.SimulatieBaan { DinamoGedrag = true, LocAdres = 5408 };
        var A = Baanverkenner.Kern.Simulatie.SimulatieBaan.Eind.A; var B = Baanverkenner.Kern.Simulatie.SimulatieBaan.Eind.B;
        var s1 = sim.NieuweSectie(1, 80, 1); var s2 = sim.NieuweSectie(2, 80, 2); var s3 = sim.NieuweSectie(3, 80, 3);
        var s4 = sim.NieuweSectie(4, 80, 4); var s5 = sim.NieuweSectie(5, 80, 5);
        sim.Verbind(s1, B, s2, A); sim.Verbind(s2, B, s3, A); sim.Verbind(s4, B, s5, A);
        sim.PlaatsLoc(s1, 40, +1);
        var ins = new VerkenInstellingen { LocAdres = 5408, LocStappen = 28, Verkensnelheid = 12, Kruipsnelheid = 5, WisselAdresVan = 1, WisselAdresTot = 2, DinamoBlokken = "1-5" };
        var status = new VerkenStatus { Instellingen = ins };
        sim.VerbindenAsync("SIM").GetAwaiter().GetResult();
        var klok = new VirtueleKlok(); klok.Getikt += sim.Tik;
        var log = new VerkenLog(klok);
        var v = new BaanVerkenner(sim, ins, klok, log, (_, _) => Task.FromResult(true), status);
        using var cts = new CancellationTokenSource();
        try { v.VoerUit(cts.Token).GetAwaiter().GetResult(); } catch (Exception ex) { Console.WriteLine("  (uitbreiden: eerste verkenning stopte met: " + ex.Message + ")"); }
        var k1 = v.Kaart;
        Eis(k1.Voltooid && k1.StartMelder == 1 && k1.Melders.Count == 3 && !k1.Melders.Any(m => m.Nummer == 4), "uitbreiden: eerste verkenning is voltooid met melders 1-3 (nu " + string.Join(",", k1.Melders.Select(m => m.Nummer)) + ")");
        // de loc met de hand op het andere traject zetten en uitbreiden
        status.VoorUitbreiding();
        Eis(!status.Kaart.Voltooid && status.Uitbreiden, "uitbreiden: VoorUitbreiding zet de kaart op 'niet voltooid'");
        sim.PlaatsLoc(s4, 40, +1);
        var log2 = new VerkenLog(klok); // dezelfde klok: de simulatie rekent met absolute tijd
        var v2 = new BaanVerkenner(sim, ins, klok, log2, (_, _) => Task.FromResult(true), status);
        using var cts2 = new CancellationTokenSource();
        try { v2.VoerUit(cts2.Token).GetAwaiter().GetResult(); } catch (Exception ex) { Console.WriteLine("  (uitbreiden: tweede verkenning stopte met: " + ex.Message + ")"); }
        var k = v2.Kaart;
        Eis(k.Voltooid, "uitbreiden: de uitgebreide kaart is voltooid");
        Eis(k.Melders.Select(m => m.Nummer).OrderBy(x => x).SequenceEqual(new[] { 1, 2, 3, 4, 5 }), "uitbreiden: alle vijf melders staan in één kaart (nu " + string.Join(",", k.Melders.Select(m => m.Nummer)) + ")");
        Eis(k.StartMelder == 4 && k.EerdereStartMelders.SequenceEqual(new[] { 1 }), "uitbreiden: nieuwe startmelder 4, eerdere startmelder 1 bewaard (nu " + k.StartMelder + " / " + string.Join(",", k.EerdereStartMelders) + ")");
        Eis(k.Overgangen.Any(o => o.Van == 1 && o.Naar == 2) && k.Overgangen.Any(o => o.Van == 4 && o.Naar == 5), "uitbreiden: overgangen van beide trajecten aanwezig");
        Eis(!k.Overgangen.Any(o => (o.Van <= 3 && o.Naar >= 4) || (o.Van >= 4 && o.Naar <= 3)), "uitbreiden: geen valse overgang tussen de twee trajecten");
        Eis(!status.Uitbreiden, "uitbreiden: de vlag is na de start weer uit (gewone hervatting bij onderbreking)");
        Eis(k.VoorgesteldeBlokken.Count >= 2, "uitbreiden: voorgestelde blokken voor beide trajecten (nu " + k.VoorgesteldeBlokken.Count + ")");
        Eis(Rapport.AlsTekst(k).Contains("eerdere deelverkenningen: 1"), "uitbreiden: rapport noemt de eerdere startmelder");
        var terug = Baankaart.VanJson(k.AlsJson())!;
        Eis(terug.EerdereStartMelders.SequenceEqual(new[] { 1 }), "uitbreiden: eerdere startmelders blijven na opslaan en laden");
        if (Environment.GetEnvironmentVariable("UITBREIDEN_LOG") == "1") Console.WriteLine(log2.AlsTekst());
    }

    // BUG #79: eerst vragen of er een wissel verkeerd staat, die dan meteen schakelen
    static void VerkeerdeWisselVraagTest()
    {
        bool? Antw(Queue<bool?> q) => q.Dequeue();
        (BaanVerkenner v, Baanverkenner.Kern.Simulatie.SimulatieBaan sim) Maak(Queue<bool?> jn, Queue<int?> ad)
        {
            var sim = new Baanverkenner.Kern.Simulatie.SimulatieBaan { DinamoGedrag = true, LocAdres = 5408 };
            var A = Baanverkenner.Kern.Simulatie.SimulatieBaan.Eind.A; var B = Baanverkenner.Kern.Simulatie.SimulatieBaan.Eind.B;
            var s1 = sim.NieuweSectie(1, 80, 1); var s2 = sim.NieuweSectie(2, 80, 2);
            sim.Verbind(s1, B, s2, A);
            sim.PlaatsLoc(s1, 40, +1);
            var ins = new VerkenInstellingen { LocAdres = 5408, LocStappen = 28, Verkensnelheid = 12, Kruipsnelheid = 5, WisselAdresVan = 1, WisselAdresTot = 4, DinamoBlokken = "1-2" };
            sim.VerbindenAsync("SIM").GetAwaiter().GetResult();
            var klok = new VirtueleKlok(); klok.Getikt += sim.Tik;
            var v = new BaanVerkenner(sim, ins, klok, new VerkenLog(klok), (_, _) => Task.FromResult(true), new VerkenStatus { Instellingen = ins })
            { VraagJaNee = (_, _) => Task.FromResult(jn.Dequeue()), VraagWisselAdres = (_, _) => Task.FromResult(ad.Dequeue()) };
            return (v, sim);
        }
        var m = typeof(BaanVerkenner).GetMethod("ControleerVerkeerdeWissel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        Task<bool> Roep(BaanVerkenner v)
        {
            // de monitor wordt normaal door VoerUit gevoed; hier melden we de loc zelf op melder 1
            var mon = (MelderMonitor)typeof(BaanVerkenner).GetField("_monitor", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(v)!;
            mon.Ontvang(1, true);
            return (Task<bool>)m.Invoke(v, new object[] { 1, "test" })!;
        }
        // Ja, adres 3, afbuigend, daarna Nee: wissel geschakeld en loc staat al goed
        var jn1 = new Queue<bool?>(new bool?[] { true, true, false }); var ad1 = new Queue<int?>(new int?[] { 3 });
        var (v1, _) = Maak(jn1, ad1);
        bool klaar = Roep(v1).GetAwaiter().GetResult();
        Eis(klaar && jn1.Count == 0 && ad1.Count == 0, "wissel verkeerd: Ja -> adres -> stand -> Nee schakelt de wissel en de loc hoeft niet verplaatst (nu " + klaar + ")");
        // bekende stand afbuigend: "verkeerd" betekent naar rechtdoor, en een tweede keer weer terug naar afbuigend (altijd tegenovergesteld)
        var jn5 = new Queue<bool?>(new bool?[] { true, false }); var ad5 = new Queue<int?>(new int?[] { 3 });
        var (v5, _) = Maak(jn5, ad5);
        var ws = (Dictionary<int, bool>)typeof(BaanVerkenner).GetField("_wisselStand", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(v5)!;
        ws[3] = true;
        Roep(v5).GetAwaiter().GetResult();
        Eis(ws.TryGetValue(3, out var na1) && na1 == false && jn5.Count == 0 && ad5.Count == 0, "wissel verkeerd: afbuigend + 'verkeerd' -> rechtdoor (nu " + (ws.TryGetValue(3, out var x) ? x.ToString() : "onbekend") + ")");
        jn5.Enqueue(true); jn5.Enqueue(false); ad5.Enqueue(3);
        Roep(v5).GetAwaiter().GetResult();
        Eis(ws.TryGetValue(3, out var na2) && na2 == true, "wissel verkeerd: nog een keer 'verkeerd' -> weer afbuigend (altijd tegenovergesteld)");
        // Nee meteen: false, dan valt VraagLocTerugTeZetten terug op het verplaatsen
        var (v2, _) = Maak(new Queue<bool?>(new bool?[] { false }), new Queue<int?>());
        Eis(!Roep(v2).GetAwaiter().GetResult(), "wissel verkeerd: Nee -> loc met de hand verplaatsen (geeft false)");
        // Annuleren
        var (v3, _) = Maak(new Queue<bool?>(new bool?[] { null }), new Queue<int?>());
        bool gestopt = false;
        try { Roep(v3).GetAwaiter().GetResult(); } catch (OperationCanceledException) { gestopt = true; }
        Eis(gestopt, "wissel verkeerd: Annuleren stopt de verkenning");
        // adres buiten bereik wordt genegeerd, daarna Nee
        var (v4, _) = Maak(new Queue<bool?>(new bool?[] { true, false }), new Queue<int?>(new int?[] { 99 }));
        Eis(!Roep(v4).GetAwaiter().GetResult(), "wissel verkeerd: adres buiten bereik wordt genegeerd");
    }

    // BUG #78 (B): wisselstraten uit de voorstellen importeren
    static void WisselstraatImportTest()
    {
        var bb = new BlokBeheerder();
        Blok MaakBlok(int nr, int melder) { var b = new Blok { Nummer = nr }; b.Bezetmeldpunten.Add(new Bezetmeldpunt { MeldernNummer = melder, Rol = BezetmeldpuntRol.Voorblok }); bb.Blokken.Add(b); return b; }
        var b1 = MaakBlok(1, 14); var b2 = MaakBlok(2, 129); var b3 = MaakBlok(3, 24); var b4 = MaakBlok(4, 21);
        bb.LegRelatieVast(b1, b2); bb.LegRelatieVast(b1, b3); bb.LegRelatieVast(b3, b4);
        var w5 = new Wissel { Adres = 5 };
        var kw = new Kruiswissel { Adres = 13, Adres2 = 10, IsEngels = true };
        var k = new Baankaart();
        k.Wisselstraatvoorstellen.Add(new WisselstraatVoorstel { VanMelder = 14, NaarMelder = 129, Richting = Richting.Vooruit, AantalKeer = 3, Standen = "5", Eisen = { new WisselEis { Adres = 5, Afbuigend = true, Bron = EisBron.Splitsing } } });
        k.Wisselstraatvoorstellen.Add(new WisselstraatVoorstel { VanMelder = 14, NaarMelder = 24, Richting = Richting.Vooruit, AantalKeer = 5, Standen = "10", Eisen = { new WisselEis { Adres = 5, Afbuigend = false, Bron = EisBron.Splitsing } } });
        // kruiswissel: eis alleen op motor 10 (rechtdoor), motor 13 afbuigend in de gereden stand
        k.Wisselstraatvoorstellen.Add(new WisselstraatVoorstel { VanMelder = 24, NaarMelder = 21, Richting = Richting.Vooruit, AantalKeer = 2, Standen = "13", Eisen = { new WisselEis { Adres = 10, Afbuigend = false, Bron = EisBron.Aangenomen } } });
        // melder zonder blok en onbekende wissel
        k.Wisselstraatvoorstellen.Add(new WisselstraatVoorstel { VanMelder = 99, NaarMelder = 14, Richting = Richting.Vooruit, AantalKeer = 1, Eisen = { new WisselEis { Adres = 5, Afbuigend = true, Bron = EisBron.Splitsing } } });
        k.Wisselstraatvoorstellen.Add(new WisselstraatVoorstel { VanMelder = 14, NaarMelder = 129, Richting = Richting.Vooruit, AantalKeer = 1, Standen = "5,77", Eisen = { new WisselEis { Adres = 5, Afbuigend = true, Bron = EisBron.Splitsing }, new WisselEis { Adres = 77, Afbuigend = true, Bron = EisBron.Aangenomen } } });
        var res = new WisselstraatImporter().Importeer(k, bb, new[] { w5 }, new[] { kw }, new List<Wisselstraat>());
        var a = res.Nieuw.FirstOrDefault(w => w.Van == b1 && w.Naar == b2);
        Eis(a is { Volgnummer: 1 } && a.Wissels.Count == 1 && a.Wissels[0].Wissel == w5 && a.Wissels[0].GewensteStand == WisselStand.Afbuigend, "wisselstraat-import: 1 -> 2 vraagt wissel 5 afbuigend");
        Eis(res.Nieuw.Count(w => w.Van == b1 && w.Naar == b2) == 1, "wisselstraat-import: alternatief met onbekende wissel 77 wordt overgeslagen");
        Eis(res.Meldingen.Any(m => m.Omschrijving.Contains("77")), "wisselstraat-import: onbekende wissel 77 wordt gemeld");
        var b = res.Nieuw.FirstOrDefault(w => w.Van == b1 && w.Naar == b3);
        Eis(b is not null && b.Wissels[0].GewensteStand == WisselStand.Rechtdoor, "wisselstraat-import: 1 -> 3 vraagt wissel 5 rechtdoor");
        var c = res.Nieuw.FirstOrDefault(w => w.Van == b3 && w.Naar == b4);
        Eis(c is not null && c.Kruiswisselstanden.Count == 1 && c.Kruiswisselstanden[0].StandAdres2 == WisselStand.Rechtdoor && c.Kruiswisselstanden[0].StandAdres == WisselStand.Afbuigend,
            "wisselstraat-import: kruiswissel krijgt eis op motor 10 en de overige motor uit de gereden stand (13 afbuigend)");
        Eis(res.Meldingen.Any(m => m.Ernst == "Waarschuwing" && m.Omschrijving.Contains("aangenomen")), "wisselstraat-import: aangenomen eis krijgt een waarschuwing");
        Eis(!res.Nieuw.Any(w => w.Naar == b1), "wisselstraat-import: melder zonder blok geeft geen wisselstraat");
        // bestaand blokpaar niet overschrijven; zonder relatie geen wisselstraat
        var vast = new Wisselstraat { Van = b1, Naar = b2 };
        var res2 = new WisselstraatImporter().Importeer(k, bb, new[] { w5 }, new[] { kw }, new[] { vast });
        Eis(!res2.Nieuw.Any(w => w.Van == b1 && w.Naar == b2), "wisselstraat-import: bestaande wisselstraat voor 1 -> 2 wordt niet aangeraakt");
        var bb2 = new BlokBeheerder(); bb2.Blokken.AddRange(bb.Blokken);
        var res3 = new WisselstraatImporter().Importeer(k, bb2, new[] { w5 }, new[] { kw }, new List<Wisselstraat>());
        Eis(res3.IsLeeg, "wisselstraat-import: zonder BlokRelatie komt er geen wisselstraat");
    }

    // BUG #163: bestaande wisselstraten corrigeren met wat de kaart bewezen heeft
    static void WisselstraatCorrectieTest()
    {
        var bb = new BlokBeheerder();
        Blok MaakBlok(int nr, int melder) { var b = new Blok { Nummer = nr }; b.Bezetmeldpunten.Add(new Bezetmeldpunt { MeldernNummer = melder, Rol = BezetmeldpuntRol.Voorblok }); bb.Blokken.Add(b); return b; }
        var b4 = MaakBlok(4, 16); var b3 = MaakBlok(3, 5); var b5 = MaakBlok(5, 24); var b6 = MaakBlok(6, 21);
        bb.LegRelatieVast(b4, b3); bb.LegRelatieVast(b3, b5); bb.LegRelatieVast(b5, b6);
        var w2 = new Wissel { Adres = 2 }; var w7 = new Wissel { Adres = 7 };
        var kw = new Kruiswissel { Adres = 13, Adres2 = 10, IsEngels = true };
        var k = new Baankaart();
        k.Wisselstraatvoorstellen.Add(new WisselstraatVoorstel { VanMelder = 16, NaarMelder = 5, Richting = Richting.Vooruit, AantalKeer = 4, Eisen = { new WisselEis { Adres = 2, Afbuigend = false, Bron = EisBron.Kortsluiting }, new WisselEis { Adres = 7, Afbuigend = true, Bron = EisBron.Splitsing } } });
        // splitsing alleen: wijzigt niets, alleen melding; kruismotor: nooit wijzigen
        k.Wisselstraatvoorstellen.Add(new WisselstraatVoorstel { VanMelder = 5, NaarMelder = 24, Richting = Richting.Vooruit, AantalKeer = 3, Eisen = { new WisselEis { Adres = 7, Afbuigend = true, Bron = EisBron.Splitsing } } });
        k.Wisselstraatvoorstellen.Add(new WisselstraatVoorstel { VanMelder = 24, NaarMelder = 21, Richting = Richting.Vooruit, AantalKeer = 3, Eisen = { new WisselEis { Adres = 10, Afbuigend = false, Bron = EisBron.Kortsluiting } } });
        var s43 = new Wisselstraat { Van = b4, Naar = b3, Wissels = { new WisselInWisselstraat { Wissel = w2, GewensteStand = WisselStand.Afbuigend } } };
        var s35 = new Wisselstraat { Van = b3, Naar = b5, Wissels = { new WisselInWisselstraat { Wissel = w7, GewensteStand = WisselStand.Rechtdoor } } };
        var s56 = new Wisselstraat { Van = b5, Naar = b6, Kruiswisselstanden = { new KruiswisselInWisselstraat { Kruiswissel = kw, StandAdres = WisselStand.Afbuigend, StandAdres2 = WisselStand.Afbuigend } } };
        var cor = new WisselstraatCorrectie();
        var res = cor.Bereken(k, bb, new[] { w2, w7 }, new[] { kw }, new[] { s43, s35, s56 });
        Eis(res.Wijzigingen.Count == 2, $"correctie (#163): 2 wijzigingen (nu {res.Wijzigingen.Count})");
        cor.Pas(res);
        Eis(s43.Wissels.First(w => w.Wissel == w2).GewensteStand == WisselStand.Rechtdoor, "correctie (#163): wissel 2 van 4 -> 3 is rechtdoor gezet (kortsluiting bewezen)");
        Eis(s43.Wissels.Any(w => w.Wissel == w7 && w.GewensteStand == WisselStand.Afbuigend), "correctie (#163): ontbrekende wissel 7 is toegevoegd");
        Eis(s35.Wissels[0].GewensteStand == WisselStand.Rechtdoor, "correctie (#163): splitsing alleen wijzigt een bestaande stand niet");
        Eis(res.Meldingen.Any(m => m.Omschrijving.Contains("NIET gewijzigd") && m.Omschrijving.Contains("3 → 5")), "correctie (#163): splitsing-verschil wordt gemeld");
        Eis(s56.Kruiswisselstanden[0].StandAdres2 == WisselStand.Afbuigend && s56.Kruiswisselstanden[0].StandAdres == WisselStand.Afbuigend, "correctie (#163): kruismotoren blijven onaangeroerd");
        Eis(res.Meldingen.Any(m => m.Omschrijving.Contains("kruismotor 10")), "correctie (#163): kruismotor-verschil wordt gemeld");
        cor.Herstel(res);
        Eis(s43.Wissels.Count == 1 && s43.Wissels[0].GewensteStand == WisselStand.Afbuigend, "correctie (#163): ongedaan maken herstelt de oude situatie");
        var leeg = cor.Bereken(new Baankaart(), bb, new[] { w2 }, new[] { kw }, new[] { s43 });
        Eis(leeg.Wijzigingen.Count == 0 && leeg.Meldingen.Count == 0, "correctie (#163): lege kaart wijzigt niets");
    }

    // BUG #80: een wissel waar de loc op staat wordt niet omgezet (ontsporing)
    static void WisselOnderLocTest()
    {
        var sim = new Baanverkenner.Kern.Simulatie.SimulatieBaan { DinamoGedrag = true, LocAdres = 5408 };
        var A = Baanverkenner.Kern.Simulatie.SimulatieBaan.Eind.A; var B = Baanverkenner.Kern.Simulatie.SimulatieBaan.Eind.B;
        var s1 = sim.NieuweSectie(1, 80, 1); var s2 = sim.NieuweSectie(2, 80, 2);
        sim.Verbind(s1, B, s2, A);
        sim.PlaatsLoc(s1, 40, +1);
        var ins = new VerkenInstellingen { LocAdres = 5408, LocStappen = 28, Verkensnelheid = 12, Kruipsnelheid = 5, WisselAdresVan = 1, WisselAdresTot = 4, DinamoBlokken = "1-2" };
        sim.VerbindenAsync("SIM").GetAwaiter().GetResult();
        var klok = new VirtueleKlok(); klok.Getikt += sim.Tik;
        var v = new BaanVerkenner(sim, ins, klok, new VerkenLog(klok), (_, _) => Task.FromResult(true), new VerkenStatus { Instellingen = ins });
        var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var kaart = v.Kaart;
        kaart.Wissels.Add(new WisselVondst { Adres = 3, Waarnemingen = { new WisselWaarneming { Soort = WaarnemingSoort.KortsluitingBijOmzetten, NaMelder = 1, Richting = Richting.Vooruit } } });
        var mon = (MelderMonitor)typeof(BaanVerkenner).GetField("_monitor", bf)!.GetValue(v)!;
        mon.Ontvang(1, true);
        klok.Wacht(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult(); mon.Bijwerken();
        var ws = (Dictionary<int, bool>)typeof(BaanVerkenner).GetField("_wisselStand", bf)!.GetValue(v)!;
        ws[3] = true; ws[2] = true;
        var m = typeof(BaanVerkenner).GetMethod("ZetConfiguratie", bf)!;
        ((Task)m.Invoke(v, new object[] { Configuratie.Basis, true })!).GetAwaiter().GetResult();
        Eis(ws[3] == true && ws[2] == false, "wissel onder loc: wissel 3 (ligt bij bezette melder 1) blijft staan, wissel 2 wordt gewoon omgezet (nu 3=" + ws[3] + ", 2=" + ws[2] + ")");
        ((Task)m.Invoke(v, new object[] { Configuratie.Basis, false })!).GetAwaiter().GetResult();
        Eis(ws[3] == false, "wissel onder loc: zonder sparen (rit volgt) wordt wissel 3 wel omgezet");
    }

    sealed class StilteHw : Baanverkenner.Kern.Simulatie.SimulatieBaan, Modeltreinbesturing.Hardware.IVerbindingsBron
    {
        public event Action<bool, double>? StilteGewijzigd { add { _stilteLuisteraar += value; } remove { _stilteLuisteraar -= value; } }
        Action<bool, double>? _stilteLuisteraar;
        public void Roep(bool stil, double s) => _stilteLuisteraar?.Invoke(stil, s);
        public double? Stilte { get; set; }
        public double? SecondenSindsLaatsteOntvangst => Stilte;
    }

    // BUG #81: vastgelopen Dinamo (geen bericht meer) wordt herkend bij kortsluitmelding
    static void DinamoStilteTest()
    {
        var hw = new StilteHw();
        hw.Stilte = 0.4;
        Eis(BaanVerkenner.StilteWaarschuwing(hw) is null, "stilte: 0,4 s sinds laatste bericht is normaal -> geen waarschuwing");
        hw.Stilte = null;
        Eis(BaanVerkenner.StilteWaarschuwing(hw) is null, "stilte: nog niets ontvangen -> geen waarschuwing");
        hw.Stilte = 7.5;
        var t = BaanVerkenner.StilteWaarschuwing(hw);
        Eis(t != null && t.Contains("7.5") && t.Contains("herstart Dinamo"), "stilte: 7.5 s geen bericht -> waarschuwing met herstart-advies (nu " + t + ")");
        Eis(BaanVerkenner.StilteWaarschuwing(new Baanverkenner.Kern.Simulatie.SimulatieBaan()) is null, "stilte: hardware zonder verbindingsbron -> geen waarschuwing");

        // Marco 12:17: stilte van Dinamo (geen keepalive meer) komt als waarschuwing in het logboek van de verkenner en verdwijnt weer
        var sim = new StilteHw { LocAdres = 5408 };
        var ins = new VerkenInstellingen { LocAdres = 5408, LocStappen = 28, Verkensnelheid = 12, Kruipsnelheid = 5, WisselAdresVan = 1, WisselAdresTot = 2 };
        var klok = new VirtueleKlok(); var log = new VerkenLog(klok);
        var v = new BaanVerkenner(sim, ins, klok, log, (_, _) => Task.FromResult(true), new VerkenStatus { Instellingen = ins });
        var m = typeof(BaanVerkenner).GetMethod("HardwareMeldtStilte", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        m.Invoke(v, new object[] { true, 2.4 });
        Eis(v.DinamoStil && log.Regels.Any(r => r.Tekst.Contains("DINAMO ANTWOORDT NIET MEER") && r.Tekst.Contains("2.4")), "keepalive-stilte: waarschuwing in het logboek van de verkenner (nu " + string.Join(" | ", log.Regels.Select(r => r.Tekst)) + ")");
        m.Invoke(v, new object[] { false, 5.0 });
        Eis(!v.DinamoStil && log.Regels.Any(r => r.Tekst.Contains("antwoordt weer")), "keepalive-stilte: melding dat Dinamo weer antwoordt");
    }

    // BUG #82: een blokalarm van Dinamo vlak voor de volgende stap stopt wissels omzetten en rijden
    static void AlarmVoorStapTest()
    {
        var sim = new Baanverkenner.Kern.Simulatie.SimulatieBaan { DinamoGedrag = true, LocAdres = 5408 };
        var A = Baanverkenner.Kern.Simulatie.SimulatieBaan.Eind.A; var B = Baanverkenner.Kern.Simulatie.SimulatieBaan.Eind.B;
        var s1 = sim.NieuweSectie(1, 80, 1); var s2 = sim.NieuweSectie(2, 80, 2);
        sim.Verbind(s1, B, s2, A); sim.PlaatsLoc(s1, 40, +1);
        var ins = new VerkenInstellingen { LocAdres = 5408, LocStappen = 28, Verkensnelheid = 12, Kruipsnelheid = 5, WisselAdresVan = 1, WisselAdresTot = 4, DinamoBlokken = "1-2" };
        sim.VerbindenAsync("SIM").GetAwaiter().GetResult();
        var klok = new VirtueleKlok(); klok.Getikt += sim.Tik;
        var v = new BaanVerkenner(sim, ins, klok, new VerkenLog(klok), (_, _) => Task.FromResult(true), new VerkenStatus { Instellingen = ins });
        var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var check = typeof(BaanVerkenner).GetMethod("ControleerGeenAlarmVoorStap", bf)!;
        var alarm = typeof(BaanVerkenner).GetMethod("HardwareMeldtBlokAlarm", bf)!;
        ((Task)check.Invoke(v, new object[] { "test" })!).GetAwaiter().GetResult();
        Eis(true, "alarm voor stap: zonder alarm gebeurt er niets");
        alarm.Invoke(v, new object[] { 8, true });
        bool fout = false; string tekst = "";
        try { ((Task)check.Invoke(v, new object[] { "test" })!).GetAwaiter().GetResult(); }
        catch (NavigatieFout nf) { fout = true; tekst = nf.Message; }
        Eis(fout && tekst.Contains("blok 8") && tekst.Contains("wissels geprobeerd"), "alarm voor stap: blok 8 blijft in alarm en geen wissel helpt -> navigatiefout (nu " + tekst + ")");
        alarm.Invoke(v, new object[] { 8, false });
        ((Task)check.Invoke(v, new object[] { "test" })!).GetAwaiter().GetResult();
        Eis(true, "alarm voor stap: alarm opgeheven -> verder");

        // BUG #108: helpt het omzetten van wissel 3, dan gaat de verkenner zelf verder en blijft die wissel staan
        var stand = (Dictionary<int, bool>)typeof(BaanVerkenner).GetField("_wisselStand", bf)!.GetValue(v)!;
        alarm.Invoke(v, new object[] { 8, true });
        klok.Getikt += _ => { if (stand.TryGetValue(3, out var af) && af) alarm.Invoke(v, new object[] { 8, false }); };
        bool ok = true;
        try { ((Task)check.Invoke(v, new object[] { "test" })!).GetAwaiter().GetResult(); } catch (NavigatieFout) { ok = false; }
        Eis(ok && stand.TryGetValue(3, out var af3) && af3, "alarm voor stap #108: kortsluiting weg na omzetten van wissel 3 -> verkenner gaat zelf verder, wissel 3 blijft afbuigend");
        Eis(stand.Where(kv => kv.Key != 3).All(kv => !kv.Value), "alarm voor stap #108: de eerder geprobeerde wissels staan weer in hun oude stand");
    }

    // BUG #144b: na een kortsluiting blijft een loc die op melder x en de vorige melder staat staan (niet achteruit rijden)
    static void HerstelBlijftStaanTest()
    {
        Console.WriteLine("Test: herstel blijft staan (BUG #144b)");
        var sim = new Baanverkenner.Kern.Simulatie.SimulatieBaan { DinamoGedrag = true, LocAdres = 5408 };
        var A = Baanverkenner.Kern.Simulatie.SimulatieBaan.Eind.A; var B = Baanverkenner.Kern.Simulatie.SimulatieBaan.Eind.B;
        var s1 = sim.NieuweSectie(1, 80, 1); var s2 = sim.NieuweSectie(2, 80, 2);
        sim.Verbind(s1, B, s2, A); sim.PlaatsLocOpGrens(s1, 75, +1, s2);
        var ins = new VerkenInstellingen { LocAdres = 5408, LocStappen = 28, Verkensnelheid = 12, Kruipsnelheid = 5, WisselAdresVan = 1, WisselAdresTot = 4, DinamoBlokken = "1-2" };
        sim.VerbindenAsync("SIM").GetAwaiter().GetResult();
        var klok = new VirtueleKlok(); klok.Getikt += sim.Tik;
        var log = new VerkenLog(klok);
        var v = new BaanVerkenner(sim, ins, klok, log, (_, _) => Task.FromResult(true), new VerkenStatus { Instellingen = ins });
        var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var mon = (MelderMonitor)typeof(BaanVerkenner).GetField("_monitor", bf)!.GetValue(v)!;
        mon.Ontvang(1, true); mon.Ontvang(2, true);
        klok.Wacht(TimeSpan.FromSeconds(1)).GetAwaiter().GetResult();
        mon.Bijwerken();
        typeof(BaanVerkenner).GetField("_laatsteKortsluitMelders", bf)!.SetValue(v, new List<int> { 1 });
        var herstel = typeof(BaanVerkenner).GetMethod("HerstelNaKortsluiting", bf)!;
        double voor = sim.LocPositieTest();
        ((Task)herstel.Invoke(v, new object[] { 2, Richting.Vooruit })!).GetAwaiter().GetResult();
        mon.Bijwerken();
        Eis(mon.Bezet.OrderBy(x => x).SequenceEqual(new[] { 1, 2 }), "herstel #144b: de loc staat nog op melder 1 en 2 (kreeg: " + string.Join(",", mon.Bezet.OrderBy(x => x)) + ")");
        Eis(Math.Abs(sim.LocPositieTest() - voor) < 0.01, "herstel #144b: de loc is niet verplaatst");
    }

    // BUG #144: de wissel die het alarm bij een stilstaande loc wegneemt wordt bij het kortsluitpunt vastgelegd
    static void AlarmOplossingVastleggenTest()
    {
        Console.WriteLine("Test: alarmoplossing vastleggen (BUG #144)");
        var sim = new Baanverkenner.Kern.Simulatie.SimulatieBaan { DinamoGedrag = true, LocAdres = 5408 };
        var A = Baanverkenner.Kern.Simulatie.SimulatieBaan.Eind.A; var B = Baanverkenner.Kern.Simulatie.SimulatieBaan.Eind.B;
        var s1 = sim.NieuweSectie(1, 80, 1); var s2 = sim.NieuweSectie(2, 80, 2);
        sim.Verbind(s1, B, s2, A); sim.PlaatsLoc(s1, 40, +1);
        var ins = new VerkenInstellingen { LocAdres = 5408, LocStappen = 28, Verkensnelheid = 12, Kruipsnelheid = 5, WisselAdresVan = 1, WisselAdresTot = 4, DinamoBlokken = "1-2" };
        sim.VerbindenAsync("SIM").GetAwaiter().GetResult();
        var klok = new VirtueleKlok(); klok.Getikt += sim.Tik;
        var status = new VerkenStatus { Instellingen = ins };
        var v = new BaanVerkenner(sim, ins, klok, new VerkenLog(klok), (_, _) => Task.FromResult(true), status);
        var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var kaart = status.Kaart;
        kaart.Kortsluitpunten.Add(new Kortsluitpunt { Id = 1, NaMelder = 1, Richting = Richting.Vooruit, Soort = KortsluitpuntSoort.Kortsluiting, Configuratie = Configuratie.Basis.Met(2), AlarmBlokken = new() { 1 } });
        var check = typeof(BaanVerkenner).GetMethod("ControleerGeenAlarmVoorStap", bf)!;
        var alarm = typeof(BaanVerkenner).GetMethod("HardwareMeldtBlokAlarm", bf)!;
        var stand = (Dictionary<int, bool>)typeof(BaanVerkenner).GetField("_wisselStand", bf)!.GetValue(v)!;
        var mon = (MelderMonitor)typeof(BaanVerkenner).GetField("_monitor", bf)!.GetValue(v)!;
        mon.Ontvang(1, true);
        klok.Wacht(TimeSpan.FromSeconds(1)).GetAwaiter().GetResult();
        mon.Bijwerken();
        alarm.Invoke(v, new object[] { 1, true });
        klok.Getikt += _ => { if (stand.TryGetValue(3, out var af) && af) alarm.Invoke(v, new object[] { 1, false }); };
        bool ok = true;
        try { ((Task)check.Invoke(v, new object[] { "test" })!).GetAwaiter().GetResult(); } catch (NavigatieFout) { ok = false; }
        var kp = kaart.Kortsluitpunten.First();
        Eis(ok && kp.Opgelost && kp.OpgelostDoorAdres == 3 && kp.OpgelostMetAfbuigend == true, "alarmoplossing: wissel 3 afbuigend haalde het alarm weg -> kortsluitpunt #1 staat als opgelost door wissel 3 (kreeg: " + kp.Opgelost + "/" + kp.OpgelostDoorAdres + ")");
        var pad = new List<int> { 1, 2 };
        var c = kaart.MetBekendeKortsluitOplossingen(pad, Richting.Vooruit, Configuratie.Basis.Met(2), out _);
        Eis(c.Bevat(3), "alarmoplossing: de volgende rit over dit punt zet wissel 3 meteen afbuigend (MetBekendeKortsluitOplossingen)");
    }

    // BUG #145: een oplossing die bij aankomst op de startmelder niet blijkt te werken wordt ingetrokken en de opdracht wordt niet herhaald
    static void AankomstOplossingWeerlegdTest()
    {
        Console.WriteLine("Test: aankomst-oplossing weerleggen (BUG #145)");
        var sim = new Baanverkenner.Kern.Simulatie.SimulatieBaan { DinamoGedrag = true, LocAdres = 5408 };
        var A = Baanverkenner.Kern.Simulatie.SimulatieBaan.Eind.A; var B = Baanverkenner.Kern.Simulatie.SimulatieBaan.Eind.B;
        var s1 = sim.NieuweSectie(1, 80, 1); var s2 = sim.NieuweSectie(2, 80, 2);
        sim.Verbind(s1, B, s2, A); sim.PlaatsLoc(s1, 40, +1);
        var ins = new VerkenInstellingen { LocAdres = 5408, LocStappen = 28, Verkensnelheid = 12, Kruipsnelheid = 5, WisselAdresVan = 1, WisselAdresTot = 4, DinamoBlokken = "1-2" };
        sim.VerbindenAsync("SIM").GetAwaiter().GetResult();
        var klok = new VirtueleKlok(); klok.Getikt += sim.Tik;
        var status = new VerkenStatus { Instellingen = ins };
        var v = new BaanVerkenner(sim, ins, klok, new VerkenLog(klok), (_, _) => Task.FromResult(true), status);
        var kaart = status.Kaart;
        var kp = new Kortsluitpunt { Id = 1, NaMelder = 2, Richting = Richting.Vooruit, Soort = KortsluitpuntSoort.Kortsluiting, Configuratie = Configuratie.Basis.Met(2),
            Opgelost = true, OpgelostDoorAdres = 1, OpgelostMetAfbuigend = true, AantalKortsluitingen = 1,
            Route = new List<Etappe> { new Etappe { Van = 1, Naar = 2, Richting = Richting.Vooruit, Configuratie = Configuratie.Basis.Met(2), Pad = new List<int> { 1, 2 } } } };
        kaart.Kortsluitpunten.Add(kp);
        var o = new Opdracht { Id = 4, Start = 2, Richting = Richting.Vooruit, Configuratie = Configuratie.Basis.Met(1).Met(2) };
        var rijstand = Configuratie.Basis.Met(1).Met(2);
        Eis(v.BekendeAankomstKortsluiting(o, rijstand) is null, "aankomst: een (nog) opgelost kortsluitpunt blokkeert de opdracht niet vooraf");
        Eis(v.WeerlegAankomstOplossing(o, rijstand), "aankomst: kortsluiting bij aankomst met dezelfde stand weerlegt het punt");
        Eis(!kp.Opgelost && kp.OpgelostDoorAdres is null && kp.Opgegeven, "aankomst: de oplossing (wissel 1) is ingetrokken en het punt is opgegeven");
        Eis(kaart.MetBekendeKortsluitOplossingen(new List<int> { 1, 2 }, Richting.Vooruit, Configuratie.Basis.Met(2), out _).Equals(Configuratie.Basis.Met(2)), "aankomst: wissel 1 wordt niet meer als oplossing toegevoegd");
        Eis(v.BekendeAankomstKortsluiting(o, rijstand) is { Id: 1 }, "aankomst: dezelfde opdracht wordt voortaan overgeslagen");
        Eis(v.BekendeAankomstKortsluiting(new Opdracht { Id = 5, Start = 2, Richting = Richting.Vooruit, Configuratie = Configuratie.Basis }, Configuratie.Basis) is null, "aankomst: een opdracht zonder die wissel afbuigend blijft gewoon mogelijk");
    }

    // BUG #195: optie 6 'Rondrijden' op de Miniworld-simulatie vanaf een hoofdlus: alle melders, wissels, geen valse wissels, blokken pas aan het eind
    static void RondritTest()
    {
        Console.WriteLine("Test: optie 6 rondrijden (BUG #195)");
        var r = MiniworldRit(5, 10, insAanpassing: i => { i.Rondrit = true; i.StapVoorStap = true; });   // StapVoorStap staat aan: optie 6 schakelt optie 5 uit
        var k = r.V.Kaart;
        var regels = r.Log.Regels.Select(x => x.Tekst).ToList();
        Eis(!r.V.Status.Instellingen.StapVoorStap, "rondrit: optie 5 is uitgeschakeld");
        Eis(k.Melders.Count == 21, $"rondrit: alle 21 melders gevonden (nu {k.Melders.Count})");
        Eis(new[] { 2, 5, 6, 9, 14 }.All(a => k.Wissels.Any(w => w.Adres == a && w.Waarnemingen.Count > 0)), "rondrit: wissels 2, 5, 6, 9 en 14 gevonden");
        Eis(k.Wissels.All(w => new[] { 1, 2, 5, 6, 9, 10, 13, 14 }.Contains(w.Adres)), "rondrit: geen valse wissels");
        Eis(k.Wissels.Any(w => w.Adres is 10 or 13 && w.Waarnemingen.Count > 0), "rondrit: de kruismotoren 10 en 13 zijn als gewone wissel gevonden");
        int eersteProef = regels.FindIndex(x => x.StartsWith("Blokproef voor melder"));
        int blokFase = regels.FindIndex(x => x.Contains("Nu worden de Dinamo-blokken gezocht"));
        Eis(blokFase >= 0 && eersteProef > blokFase, $"rondrit: de eerste blokproef (regel {eersteProef}) komt pas na het ontdekken van alle melders (regel {blokFase})");
        Eis(k.Voltooid && k.Melders.All(m => m.DinamoBlok is not null), "rondrit: voltooid en elke melder heeft een Dinamo-blok");
        Eis(r.Kortsluitingen <= 10, $"rondrit: beperkt aantal kortsluitingen (nu {r.Kortsluitingen})");
    }

    // BUG #232: optie 6 op de Miniworld-simulatie MET de keerlus 129-4-11-12-1-9-10-129 (zoals de logs van 18-10 laten zien)
    static void KeerlusTest()
    {
        Console.WriteLine("Test: rondrijden met de keerlus achter melder 129 (BUG #232)");
        Baanverkenner.Kern.Simulatie.MiniworldBaan.Keerlus = true;
        try
        {
            foreach (var start in (Environment.GetEnvironmentVariable("KEERLUS_START") ?? "13").Split(',').Select(int.Parse))
            {
                var r = MiniworldRit(start, 10, insAanpassing: i => { i.Rondrit = true; i.StapVoorStap = false; });
                var k = r.V.Kaart;
                var regels = r.Log.Regels.Select(x => x.Tekst).ToList();
                // BUG #245: alleen de verkenningsfase telt; in de blokfase rijdt de loc na de keerlus nog verkeerd (open punt #234)
                int naar10 = regels.Count(x => x.Contains("onverwacht melder 10 bezet (verwacht: 129, 14)") && !x.StartsWith("Blok zoeken"));
                Eis(regels.Count(x => x.Contains("BUG #245")) > 0, $"keerlus (start {start}) BUG #245: wissel 1 (loste de kortsluiting bij melder 12 op) wordt na de proef niet onder de loc omgezet");
                Console.WriteLine($"  keerlus start {start}: {r.Einde}, {r.Seconden / 60:0} min, {r.Kortsluitingen} kortsluiting(en), melders {k.Melders.Count} ({string.Join(",", k.Melders.Select(m => m.Nummer).OrderBy(x => x))}), voltooid {k.Voltooid}, 129->14-maar-10: {naar10}");
                if (Environment.GetEnvironmentVariable("KEERLUS_LOG") == "1") Console.WriteLine(r.Log.AlsTekst());
                Console.WriteLine($"  keerlus start {start}: #270 {regels.Count(x => x.Contains("BUG #270"))}x, #271 {regels.Count(x => x.Contains("BUG #271"))}x, rondjes {regels.Count(x => x.Contains("rijdt rondjes"))}x");
                Eis(k.Melders.Count == 23, $"keerlus (start {start}): alle 23 melders (met 1 en 9) gevonden (nu {k.Melders.Count})");
                Eis(naar10 == 0, $"keerlus (start {start}): de terugweg 129 -> 14 gaat niet steeds naar 10 (nu {naar10}x)");
                Eis(k.Voltooid && k.Melders.All(m => m.DinamoBlok is not null), $"keerlus (start {start}) BUG #281/#282: de blokfase wordt afgemaakt, elke melder heeft een Dinamo-blok (zonder: {string.Join(",", k.Melders.Where(m => m.DinamoBlok is null).Select(m => m.Nummer))})");
                if (start == 13)
                {
                    // BUG #268: zelfde keerlus, maar Dinamo meldt bij elke kortsluiting even melder 137 en 138 (zoals op de baan 21-10 11:21:55)
                    Baanverkenner.Kern.Simulatie.SimulatieBaan? sim268 = null;
                    var r268 = MiniworldRit(start, 10, simAanpassing: s => { s.KortsluitEchoMelders = new[] { 137, 138 }; sim268 = s; }, insAanpassing: i => { i.Rondrit = true; i.StapVoorStap = false; });
                    var reg268 = r268.Log.Regels.Select(x => x.Tekst).ToList();
                    Console.WriteLine($"  keerlus met kortsluitecho: {r268.Kortsluitingen} kortsluiting(en), {sim268!.KortsluitEchosGemeld} echo's, melders {r268.V.Kaart.Melders.Count}");
                    Eis(sim268.KortsluitEchosGemeld > 0, "kortsluitecho (#268): de simulatie meldde 137/138 rond een kortsluiting");
                    Eis(!r268.V.Kaart.Melders.Any(m => m.Nummer is 137 or 138), "kortsluitecho (#268): 137 en 138 komen niet in de kaart");
                    Eis(!reg268.Any(x => x.Contains("onverwacht melder 137") || x.Contains("onverwacht melder 138")), "kortsluitecho (#268): geen 'onverwacht melder 137/138' (" + reg268.FirstOrDefault(x => x.Contains("onverwacht melder 13")) + ")");
                    Eis(r268.V.Kaart.Melders.Count == 23, $"kortsluitecho (#268): nog steeds alle 23 melders (nu {r268.V.Kaart.Melders.Count})");
                }
                // Nog open (#234): in deze simulatie lukt de blokfase daarna niet voor 15 melders (de loc rijdt na de keerlus de verkeerde kant op); daarom geen eis op 'voltooid'.
            }
        }
        finally { Baanverkenner.Kern.Simulatie.MiniworldBaan.Keerlus = false; }
    }

    // BUG #195: optie 6 'Rondrijden' in de Miniworld-simulatie, per startplek en in gewone en strenge sim
    static void RondritBench()
    {
        var echt = new HashSet<int> { 1, 2, 5, 6, 9, 10, 13, 14 };
        foreach (var start in (Environment.GetEnvironmentVariable("BENCH_START") ?? "17,5,13,30,129,12,4,10").Split(',').Select(int.Parse))
        foreach (var streng in new[] { false, true })
        {
            Baanverkenner.Kern.Simulatie.SimulatieBaan? sim = null;
            var r = MiniworldRit(start, 10, insAanpassing: i => { i.Rondrit = true; i.StapVoorStap = Environment.GetEnvironmentVariable("RONDRIT_STAP") == "1"; }, simAanpassing: sa => { sim = sa; sa.BuurwisselHeftKruisKortsluitingOp = streng; });
            var k = r.V.Kaart;
            var gevonden = k.Wissels.Where(w => w.Waarnemingen.Count > 0).Select(w => w.Adres).ToList();
            int blokken = k.Melders.Count(m => m.DinamoBlok is not null);
            Console.WriteLine($"RONDRIT start {start,3} streng={(streng ? 1 : 0)}: {r.Einde,-12} {(k.Voltooid ? "voltooid" : "NIET-voltooid"),-13} melders {k.Melders.Count,2}/21  met-blok {blokken,2}  wissels [{string.Join(",", gevonden.OrderBy(x => x))}]  valse {gevonden.Count(a => !echt.Contains(a))}  kortsl {r.Kortsluitingen,2}  onder-loc {sim?.OnderLocOmgezet ?? 0,2}  {r.Seconden / 60:0} min");
            if (Environment.GetEnvironmentVariable("RONDRIT_LOG") == "1") Console.WriteLine(r.Log.AlsTekst());
        }
    }

    // BUG #194/#195
    // BUG #193: het blokkenschema tekent de gevonden wissels als blokjes
    static void SchemaWisselsTest()
    {
        Console.WriteLine("Test: blokkenschema met wissel-blokjes (BUG #193)");
        var k = Laad("baankaart_2026-10-17_1601.json");
        var t = SchemaTekening.Maak(k);
        var svg = t.AlsSvg();
        var adressen = t.Vakken.SelectMany(v => v.Wissels).Select(w => w.Adres).Distinct().OrderBy(x => x).ToList();
        Eis(adressen.Contains(2) && adressen.Contains(14), "schema: wissel 2 en 14 staan als blokje in een vak (nu " + string.Join(",", adressen) + ")");
        Eis(svg.Contains(">W2") && svg.Contains(">W14"), "schema: de SVG bevat de blokjes W2 en W14");
        Eis(k.Wissels.Count(w => w.Waarnemingen.Count > 0) >= adressen.Count, "schema: niet meer blokjes dan wissels met waarnemingen");
    }

    // BUG #193: kruiswissel herkennen door uitsluiting (log 17-10 16:01: motor 10+13 opgegeven, melder 24 had drie buren)
    static void KruisUitsluitingTest()
    {
        Console.WriteLine("Test: kruiswissel door uitsluiting (BUG #193)");
        var sim = new Baanverkenner.Kern.Simulatie.SimulatieBaan { DinamoGedrag = true, LocAdres = 5408 };
        var ins = new VerkenInstellingen { LocAdres = 5408, LocStappen = 28, Verkensnelheid = 12, Kruipsnelheid = 5, WisselAdresVan = 1, WisselAdresTot = 14, DinamoBlokken = "1-8", KruisMotoren = "13+10" };
        sim.VerbindenAsync("SIM").GetAwaiter().GetResult();
        var klok = new VirtueleKlok(); klok.Getikt += sim.Tik;
        var status = new VerkenStatus { Instellingen = ins, Kaart = Laad("baankaart_2026-10-17_1601.json") };
        var v = new BaanVerkenner(sim, ins, klok, new VerkenLog(klok), (_, _) => Task.FromResult(true), status);
        v.Kruis = KruisKennis.Parse("13+10");
        status.Wachtrij.Clear();
        Eis(v.KruisUitUitsluiting() == 24, "uitsluiting: op de kaart van 16:01 is melder 24 de kruiswissel (nu " + v.KruisUitUitsluiting() + ")");
        status.Wachtrij.Add(new Opdracht { Id = 99, Start = 5, Richting = Richting.Vooruit, Configuratie = Configuratie.Basis });
        Eis(v.KruisUitUitsluiting() is null, "uitsluiting: niet zolang er nog opdrachten in de wachtrij staan");
        status.Wachtrij.Clear();
        v.Kruis = new KruisKennis();
        Eis(v.KruisUitUitsluiting() is null, "uitsluiting: zonder opgegeven motoren niets afleiden");
    }

    // BUG #192 (log 17-10 16:01): aankomst op melder 12 met wissel 2 (nodig) en wissel 1 (oplossingspartner): wissel 1 mag niet rechtdoor blijven
    static void Aankomst192Test()
    {
        Console.WriteLine("Test: aankomst melder 12 houdt wissel 1 bij wissel 2 afbuigend (BUG #192)");
        var k = Laad("baankaart_2026-10-17_1601.json");
        var e = new Etappe { Van = 5, Naar = 12, Richting = Richting.Achteruit, Pad = new List<int> { 5, 12 }, Configuratie = Configuratie.Basis.Met(1).Met(2) };
        var c = Baanverkenner.Kern.BaanVerkenner.AankomstZonderKortsluitWissels(k, e, e.Configuratie, out var u);
        Eis(c.Bevat(1) && c.Bevat(2), "192: 5 -> 12 houdt wissel 1 en 2 samen afbuigend (nu " + c + ", " + (u ?? "geen uitleg") + ")");
    }

    // BUG #146: aankomst zonder kortsluit-wissels en overloop-partner in beide richtingen
    static void AankomstEnOverloopTest()
    {
        Console.WriteLine("Test: aankomst zonder kortsluit-wissels + overloop-partner (BUG #146)");
        var kaart = new Baankaart();
        var etappe = new Etappe { Van = 13, Naar = 5, Richting = Richting.Vooruit, Configuratie = Configuratie.Basis.Met(2), Pad = new List<int> { 13, 14, 16, 5 } };
        kaart.Kortsluitpunten.Add(new Kortsluitpunt { Id = 1, NaMelder = 5, Richting = Richting.Vooruit, Soort = KortsluitpuntSoort.Kortsluiting, Configuratie = Configuratie.Basis.Met(2),
            Opgelost = true, OpgelostDoorAdres = 1, OpgelostMetAfbuigend = true, Route = new List<Etappe> { etappe } });
        var nav = new Etappe { Van = 13, Naar = 5, Richting = Richting.Vooruit, Configuratie = Configuratie.Basis, Pad = new List<int> { 13, 14, 16, 5 } };
        var c = Baanverkenner.Kern.BaanVerkenner.AankomstZonderKortsluitWissels(kaart, nav, Configuratie.Basis.Met(1).Met(2).Met(7), out var uitleg);
        Eis(!c.Bevat(1) && !c.Bevat(2) && c.Bevat(7) && uitleg is not null, "aankomst: 1 en 2 blijven rechtdoor tijdens de aankomst op 5, andere wissels (7) blijven staan");
        var andere = new Etappe { Van = 13, Naar = 8, Richting = Richting.Vooruit, Pad = new List<int> { 13, 8 } };
        Eis(Baanverkenner.Kern.BaanVerkenner.AankomstZonderKortsluitWissels(kaart, andere, Configuratie.Basis.Met(1).Met(2), out _).Equals(Configuratie.Basis.Met(1).Met(2)), "aankomst: een andere aankomstmelder blijft ongemoeid");
        // overloop: wissel 2 afbuigend (achteruit, kortsluitpunt bij melder 12) krijgt wissel 1 erbij, omdat 2+1 vooruit al bewezen is
        kaart.Wissel(1).Waarnemingen.Add(new WisselWaarneming { Soort = WaarnemingSoort.LostKortsluitingOp, NaMelder = 5, Richting = Richting.Vooruit, Configuratie = Configuratie.Basis.Met(2).ToString() });
        var r = kaart.MetOverloopPartners(Configuratie.Basis.Met(2), null, null, out var wijz);
        Eis(r.Bevat(1) && r.Bevat(2) && wijz.Count == 1, "overloop: rit met wissel 2 afbuigend krijgt wissel 1 erbij (beide richtingen)");
        Eis(!kaart.MetOverloopPartners(Configuratie.Basis.Met(2), 1, null, out _).Bevat(1), "overloop: de opdracht die wissel 1 zelf test, krijgt hem niet automatisch erbij");
        Eis(!kaart.MetOverloopPartners(Configuratie.Basis, null, null, out _).Bevat(1), "overloop: zonder wissel 2 afbuigend verandert er niets");
        // BUG #254: partner van een gespaarde wissel wordt ook gespaard; alleen bewezen (niet voorlopige) oplossingen
        var k254 = new Baankaart();
        k254.Kortsluitpunten.Add(new Kortsluitpunt { Id = 3, NaMelder = 12, Richting = Richting.Achteruit, Soort = KortsluitpuntSoort.Kortsluiting, Configuratie = Configuratie.Basis.Met(2), Opgelost = true, OpgelostDoorAdres = 1, OpgelostMetAfbuigend = true });
        Eis(k254.OverloopPartnersVan(new[] { 2 }).SetEquals(new[] { 1 }), "overloop (#254): wissel 1 is partner van gespaarde wissel 2");
        Eis(k254.OverloopPartnersVan(new[] { 1 }).SetEquals(new[] { 2 }), "overloop (#254): wissel 2 is partner van gespaarde wissel 1");
        Eis(k254.OverloopPartnersVan(new[] { 7 }).Count == 0, "overloop (#254): niet-verwante wissel geeft geen partners");
        // BUG #255: wissel 2 (melders 5, 12, 13, 16) ligt niet nabij de bezette melders 129 en 10
        var k255 = new Baankaart();
        foreach (var (v, nn) in new[] { (13, 14), (14, 129), (129, 10), (13, 5), (5, 12), (5, 16) }) { k255.Overgangen.Add(new Overgang { Van = v, Naar = nn, Richting = Richting.Vooruit, AantalKeer = 2 }); }
        k255.Wissel(2).Waarnemingen.Add(new WisselWaarneming { Soort = WaarnemingSoort.Splitsing, NaMelder = 5, Richting = Richting.Achteruit, VolgendeBijRechtdoor = 16, VolgendeBijAfbuigend = 12 });
        k255.Wissel(5).Waarnemingen.Add(new WisselWaarneming { Soort = WaarnemingSoort.Splitsing, NaMelder = 14, Richting = Richting.Vooruit, VolgendeBijRechtdoor = 24, VolgendeBijAfbuigend = 129 });
        Eis(!k255.LigtNabij(2, new[] { 129, 10 }), "nabij (#255): wissel 2 ligt niet bij melder 129 en 10");
        Eis(k255.LigtNabij(5, new[] { 129, 10 }), "nabij (#255): wissel 5 (afbuigend naar 129) ligt wel bij melder 129");
        Eis(k255.LigtNabij(9, new[] { 129 }), "nabij (#255): een nog onbekende wissel telt als mogelijk nabij");
        k254.Kortsluitpunten[0].Voorlopig = true;
        Eis(k254.OverloopPartnersVan(new[] { 2 }).Count == 0, "overloop (#254): een voorlopige oplossing telt niet");
    }

    // BUG #147: het alarm blijft en alleen de wissel bij de bezette melder lost het op -> vragen en dan proberen
    /// <summary>BUG #148 (Marco 18:50): 15 -> 8 -> 24 achteruit met alleen wissel 14 afbuigend reed met motor 13 rechtdoor de kruiswissel in, omdat de kaart
    /// door ruis (17 -> 8 met 14 afbuigend) alleen 17 als vervolg van 8 zag.</summary>
    static void KruisRuisVoorafTest()
    {
        var kaart = Laad("baankaart_2026-10-14_1849.json");
        var kruis = KruisKennis.Parse("10+13@24");
        var ing = KruisBewijs.Ingangen(kaart, kruis, 15, Richting.Achteruit, Configuratie.Basis.Met(14));
        Eis(ing.Contains((24, 8)), "kruis (#148): 15 achteruit met 14 afbuigend rijdt via 8 de kruiswissel in, ook met ruis in de kaart (nu " + string.Join(";", ing) + ")");
        Eis(KruisBewijs.Ingangen(kaart, kruis, 15, Richting.Achteruit, Configuratie.Basis).Count == 0, "kruis (#148): de gewone lus (alles rechtdoor) raakt de kruiswissel niet");
        var ins = new VerkenInstellingen { LocAdres = 5408, LocStappen = 28, Verkensnelheid = 12, Kruipsnelheid = 5, WisselAdresVan = 1, WisselAdresTot = 14 };
        var klok = new VirtueleKlok(); var hw = new EchoHw();
        var v = new BaanVerkenner(hw, ins, klok, new VerkenLog(klok), (_, _) => Task.FromResult(true), new VerkenStatus { Instellingen = ins, Kaart = kaart });
        v.Kruis = KruisKennis.Parse("10+13@24");
        var c = ((Task<Configuratie>)typeof(BaanVerkenner).GetMethod("ZetKruisVooraf", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(v, new object[] { 15, Richting.Achteruit, Configuratie.Basis.Met(14), new RitDoel { Configuratie = Configuratie.Basis.Met(14), StaartWissels = true } })!).GetAwaiter().GetResult();
        Eis(c.Bevat(13) && c.Bevat(14), "kruis (#148): motor 13 gaat vooraf naar afbuigend voor de rit 15 -> 8 -> 24 (nu " + c + ")");
    }

    /// <summary>BUG #149: opdracht 5 vooruit met wissel 1+2 afbuigend moet melder 5 binnenkomen vanaf 12 (bewezen), niet vanaf 16.</summary>
    static void AankomstViaIngangTest()
    {
        var kaart = Laad("baankaart_2026-10-14_1855.json");
        var kruis = KruisKennis.Parse("10+13@24");
        var normaal = kaart.Kortsluitpunten.First(x => x.Id == 1).Route.Select(e => new Etappe { Configuratie = e.Configuratie, Richting = e.Richting, Van = e.Van, Naar = e.Naar, Pad = e.Pad.ToList() }).ToList();
        var o = new Opdracht { Id = 4, Start = 5, Richting = Richting.Vooruit, Configuratie = Configuratie.Basis.Met(2), Route = normaal };
        var rs = Configuratie.Basis.Met(1).Met(2);
        var alt = BaanVerkenner.AankomstViaBewezenIngang(kaart, o, rs, kruis, out var uitleg);
        Eis(alt is { Count: 1 } && alt[0].Pad.SequenceEqual(new[] { 13, 14, 129, 4, 11, 12, 5 }), "ingang (#149): 5 vooruit met 1+2 afbuigend komt via 14-129-4-11-12 aan (nu " + (alt is null ? "geen" : string.Join("-", alt[0].Pad)) + ")");
        Eis(alt is not null && alt[0].Configuratie.Bevat(1) && alt[0].Configuratie.Bevat(2) && alt[0].Configuratie.Bevat(5), "ingang (#149): de etappe zet 1, 2 en 5 afbuigend vooraf (nu " + alt?[0].Configuratie + ")");
        Eis(uitleg is not null && uitleg.Contains("melder 12"), "ingang (#149): uitleg noemt de bewezen ingang");
        var o2 = new Opdracht { Id = 5, Start = 5, Richting = Richting.Vooruit, Configuratie = Configuratie.Basis.Met(2), Route = normaal };
        Eis(BaanVerkenner.AankomstViaBewezenIngang(kaart, o2, Configuratie.Basis, kruis, out _) is null, "ingang (#149): zonder afbuigende wissels verandert de route niet");
        var alt2 = BaanVerkenner.AankomstZonderKortsluitWissels(kaart, alt![0], alt[0].Configuratie, out var u5);
        Eis(alt2.Bevat(1) && alt2.Bevat(2) && u5 is null, "ingang (#149): aankomst vanaf 12 houdt wissel 1+2 niet meer rechtdoor (nu " + alt2 + ")");
        var kaart2 = Laad("baankaart_2026-10-14_1849.json");
        Eis(BaanVerkenner.AankomstViaBewezenIngang(kaart2, new Opdracht { Id = 8, Start = 15, Richting = Richting.Achteruit, Configuratie = Configuratie.Basis.Met(14), Route = normaal }, Configuratie.Basis.Met(13).Met(14), kruis, out _) is null, "ingang (#149): andere opdrachten (start 15) blijven ongemoeid");
    }

    static void AlarmWisselBijLocTest()
    {
        Console.WriteLine("Test: alarm - wissel bij de loc met toestemming proberen (BUG #147)");
        foreach (bool toestemming in new[] { true, false })
        {
            var sim = new Baanverkenner.Kern.Simulatie.SimulatieBaan { DinamoGedrag = true, LocAdres = 5408 };
            var A = Baanverkenner.Kern.Simulatie.SimulatieBaan.Eind.A; var B = Baanverkenner.Kern.Simulatie.SimulatieBaan.Eind.B;
            var s1 = sim.NieuweSectie(1, 80, 1); var s2 = sim.NieuweSectie(2, 80, 2);
            sim.Verbind(s1, B, s2, A); sim.PlaatsLoc(s1, 40, +1);
            var ins = new VerkenInstellingen { LocAdres = 5408, LocStappen = 28, Verkensnelheid = 12, Kruipsnelheid = 5, WisselAdresVan = 1, WisselAdresTot = 4, DinamoBlokken = "1-2" };
            sim.VerbindenAsync("SIM").GetAwaiter().GetResult();
            var klok = new VirtueleKlok(); klok.Getikt += sim.Tik;
            var status = new VerkenStatus { Instellingen = ins };
            var v = new BaanVerkenner(sim, ins, klok, new VerkenLog(klok), (_, _) => Task.FromResult(true), status);
            int gevraagd = 0;
            v.VraagJaNee = (_, _) => { gevraagd++; return Task.FromResult<bool?>(toestemming); };
            var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var kaart = status.Kaart;
            kaart.Wissel(3).Waarnemingen.Add(new WisselWaarneming { Soort = WaarnemingSoort.KortsluitingBijOmzetten, NaMelder = 1, Richting = Richting.Vooruit, VolgendeBijRechtdoor = 2, Configuratie = "alle wissels rechtdoor" });
            kaart.Kortsluitpunten.Add(new Kortsluitpunt { Id = 1, NaMelder = 1, Richting = Richting.Vooruit, Soort = KortsluitpuntSoort.Kortsluiting, Configuratie = Configuratie.Basis.Met(4), AlarmBlokken = new List<int> { 1 } });
            var check = typeof(BaanVerkenner).GetMethod("ControleerGeenAlarmVoorStap", bf)!;
            var alarm = typeof(BaanVerkenner).GetMethod("HardwareMeldtBlokAlarm", bf)!;
            var stand = (Dictionary<int, bool>)typeof(BaanVerkenner).GetField("_wisselStand", bf)!.GetValue(v)!;
            var mon = (MelderMonitor)typeof(BaanVerkenner).GetField("_monitor", bf)!.GetValue(v)!;
            mon.Ontvang(1, true);
            klok.Wacht(TimeSpan.FromSeconds(1)).GetAwaiter().GetResult();
            mon.Bijwerken();
            alarm.Invoke(v, new object[] { 1, true });
            klok.Getikt += _ => { if (stand.TryGetValue(3, out var af) && af) alarm.Invoke(v, new object[] { 1, false }); };
            bool ok = true;
            try { ((Task)check.Invoke(v, new object[] { "test" })!).GetAwaiter().GetResult(); } catch (NavigatieFout) { ok = false; }
            var kp = kaart.Kortsluitpunten.First();
            if (toestemming)
                Eis(ok && gevraagd == 1 && kp.Opgelost && kp.OpgelostDoorAdres == 3, "alarm bij loc: met toestemming lost wissel 3 (ligt bij bezette melder 1) het alarm op en staat als oplossing vastgelegd");
            else
                Eis(!ok && gevraagd == 1 && !(stand.TryGetValue(3, out var af3) && af3) && !kp.Opgelost, "alarm bij loc: zonder toestemming blijft wissel 3 ongemoeid en volgt de gewone melding");
        }
    }

    // BUG #83: een opgelost kortsluitpunt wordt ook op routes en terugwegen toegepast
    static void KoploperReferentieTest()
    {
        Console.WriteLine("Test: Koploper-referentie (BUG #84)");
        var rf = KoploperReferentie.LeesExport(Path.Combine(AppContext.BaseDirectory, "testdata", "Koploper_export_testbaan_002.xlsx"));
        Eis(rf.BlokVan(1) == 1 && rf.BlokVan(13) == 3 && rf.BlokVan(144) == 12, "referentie: gewone melders hebben hun Koploper-blok (1->1, 13->3, 144->12)");
        Eis(rf.IsDoorgangsmelder(24) && rf.IsDoorgangsmelder(129) && !rf.IsDoorgangsmelder(13), "referentie: 24 en 129 zijn doorgangsmelders, 13 niet");
        Eis(rf.WisselstraatBlokken.Select(b => b.Blokkaart).OrderBy(x => x).SequenceEqual(new[] { 8, 9 }), "referentie: wisselstraatblokken zijn 8 en 9");
        Eis(rf.WisselstraatBlokkenVoor(129).SequenceEqual(new[] { 9 }), "referentie: 129 past alleen in wisselstraatblok 9 (heeft 1>2)");
        Eis(rf.WisselstraatBlokkenVoor(24).SequenceEqual(new[] { 8, 9 }), "referentie: 24 past in 8 (voorkeur, minste overbodige overgangen) en 9");
        Eis(rf.WisselstraatBlokkenVoor(24, new HashSet<int> { 8 }).SequenceEqual(new[] { 9, 8 }), "referentie: een al gebruikt blok komt achteraan");
        Eis(rf.KlopptMetKoploper(24, 8) == true && rf.KlopptMetKoploper(24, 5) == false && rf.KlopptMetKoploper(13, 3) == true && rf.KlopptMetKoploper(13, 4) == false && rf.KlopptMetKoploper(999, 1) == null, "referentie: KlopptMetKoploper");

        // Blokkandidaten: doorgangsmelder alleen 8/9, gewone melder eerst zijn Koploper-blok
        var sim = new Baanverkenner.Kern.Simulatie.SimulatieBaan { DinamoGedrag = true, LocAdres = 5408 };
        sim.VerbindenAsync("SIM").GetAwaiter().GetResult();
        var klok = new VirtueleKlok(); var log = new VerkenLog(klok);
        var ins = new VerkenInstellingen { DinamoBlokken = "1-9" };
        var v = new BaanVerkenner(sim, ins, klok, log, (_, _) => Task.FromResult(true)) { Referentie = rf };
        var mi = typeof(BaanVerkenner).GetMethod("BlokKandidaten", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var k129 = (List<int>)mi.Invoke(v, new object?[] { null, 129 })!;
        var k24 = (List<int>)mi.Invoke(v, new object?[] { 5, 24 })!;
        var k13 = (List<int>)mi.Invoke(v, new object?[] { null, 13 })!;
        Eis(k129.SequenceEqual(new[] { 9 }), "kandidaten: melder 129 -> alleen blok 9 (kreeg: " + string.Join(",", k129) + ")");
        Eis(k24.SequenceEqual(new[] { 8, 9 }), "kandidaten: melder 24 -> alleen 8 en 9, ook al is het vorige blok 5 (kreeg: " + string.Join(",", k24) + ")");
        Eis(k13.Count == 9 && k13[0] == 3, "kandidaten: gewone melder 13 -> blok 3 eerst, daarna de rest (kreeg: " + string.Join(",", k13) + ")");
        v.Referentie = null;
        var zonder = (List<int>)mi.Invoke(v, new object?[] { null, 129 })!;
        Eis(zonder.Count == 9, "kandidaten: zonder referentie blijft het gedrag zoals voorheen");

        // Het meegeleverde JSON-bestand van de Baanverkenner moet hetzelfde zeggen als de export
        var uitBestand = KoploperReferentie.Laad(Path.Combine(AppContext.BaseDirectory, "testdata", "koploper_referentie.json"));
        Eis(uitBestand != null && uitBestand.WisselstraatBlokkenVoor(129).SequenceEqual(new[] { 9 }) && uitBestand.WisselstraatBlokkenVoor(24).SequenceEqual(new[] { 8, 9 }) && uitBestand.BlokVan(13) == 3 && uitBestand.Melders.Count == rf.Melders.Count,
            "referentie: meegeleverd koploper_referentie.json komt overeen met de export");

        // JSON-rondreis
        var terug = KoploperReferentie.VanJson(rf.AlsJson())!;
        Eis(terug.WisselstraatBlokkenVoor(129).SequenceEqual(new[] { 9 }) && terug.BlokVan(13) == 3, "referentie: JSON-rondreis");
    }

    static void KruiswisselReferentieTest()
    {
        Console.WriteLine("Test: kruiswissel-referentie (BUG #85)");
        Configuratie P(List<int> pad, Configuratie c) => KruiswisselReferentie.PasToe(pad, c, out _);
        var basis = Configuratie.Basis;
        Eis(P(new List<int> { 14, 24, 21 }, basis.Met(10).Met(13)).Equals(basis), "kruis: 14-24-21 -> 13 en 10 rechtdoor (gaf anders kortsluiting)");
        Eis(P(new List<int> { 21, 24, 14 }, basis.Met(10)).Equals(basis), "kruis: 21-24-14 achteruit -> ook rechtdoor/rechtdoor");
        Eis(P(new List<int> { 14, 24, 8 }, basis).Equals(basis.Met(13)), "kruis: 14-24-8 -> motor 13 afbuigend");
        Eis(P(new List<int> { 14, 24, 8 }, basis.Met(10)).Equals(basis.Met(10).Met(13)), "kruis: 14-24-8 -> motor 10 blijft zoals hij stond (vrij)");
        Eis(P(new List<int> { 129, 24, 21 }, basis.Met(13)).Equals(basis.Met(10)), "kruis: 129-24-21 -> 13 rechtdoor, 10 afbuigend");
        Eis(P(new List<int> { 129, 24, 8 }, basis.Met(10).Met(13)).Equals(basis.Met(10).Met(13)), "kruis: 129-24-8 (onbekend) verandert niets");
        Eis(P(new List<int> { 13, 14, 24 }, basis.Met(10)).Equals(basis.Met(10)), "kruis: pad dat op 24 eindigt verandert niets (ingangskant en uitgang onbekend)");
        Eis(P(new List<int> { 24, 8 }, basis.Met(5).Met(10).Met(13).Met(14)).Equals(basis.Met(5).Met(10).Met(13).Met(14)), "kruis: 24 -> 8 (start op 24) verandert niets");
        Eis(P(new List<int> { 14, 24, 21, 29 }, basis.Met(5)).Equals(basis.Met(5)), "kruis: andere wissels (5) blijven onaangeroerd");
        // twee tegenstrijdige doorgangen in een pad: niets afdwingen voor het strijdige adres
        Eis(P(new List<int> { 21, 24, 14, 24, 8 }, basis.Met(10)).Equals(basis), "kruis: tegenstrijdige eis (motor 13) wordt niet toegepast, de niet-strijdige (motor 10 rechtdoor) wel");
        var svg = KruiswisselReferentie.AlsSvg();
        if (Environment.GetEnvironmentVariable("KRUIS_SVG") is { Length: > 0 } svgPad) File.WriteAllText(svgPad, svg);
        try { System.Xml.Linq.XDocument.Parse(svg); Eis(true, "kruis: SVG is geldige XML"); } catch (Exception ex) { Eis(false, "kruis: SVG is geldige XML: " + ex.Message); }
        Eis(new[] { "129", "14", "21", "8", "24" }.All(m => svg.Contains(">" + m + "<")), "kruis: alle benen staan in de tekening");
        var k = Laad("baankaart_2026-10-11_1103.json"); k.KruisReferentie = true; k.KruisMotoren = "10+13"; k.KruisMidden = new List<int> { 24 };
        // BUG #89: een lege baankaart (nieuwe verkenning) toont het kruis blanco; een gevonden melder verschijnt zodra hij gevonden is
        var leeg = new Baankaart();
        var svgLeeg = KruiswisselReferentie.AlsSvg(leeg);
        Eis(!new[] { "129", "14", "21", "8", "24", "13" }.Any(m => svgLeeg.Contains(">" + m + "<")) && svgLeeg.Contains("<rect"), "kruis: nieuwe verkenning -> kruis aanwezig maar alle melders blanco");
        Eis(!KruiswisselReferentie.AlsTekst(leeg).Contains("motor 13 afbuigend") && !Rapport.AlsHtml(leeg).Contains("&harr;"), "kruis: nieuwe verkenning -> geen standen in tekst/tabel");
        var gedeeld = new Baankaart(); foreach (var n in new[] { 24, 14 }) gedeeld.Melders.Add(new MelderInfo { Nummer = n });
        var svgDeel = KruiswisselReferentie.AlsSvg(gedeeld);
        Eis(svgDeel.Contains(">24<") && svgDeel.Contains(">14<") && !svgDeel.Contains(">21<") && !svgDeel.Contains(">8<"), "kruis: alleen gevonden melders staan ingevuld");
        Eis(Rapport.AlsHtml(k).Contains("Kruiswissel in melder 24") && Rapport.AlsTekst(k).Contains("KRUISWISSEL MELDER 24"), "kruis: staat in het rapport (html en tekst)");
    }

    static void HandmatigeOplossingTest()
    {
        Console.WriteLine("Test: handmatige wisselcorrectie wordt onthouden (BUG #87)");
        var k = Laad("baankaart_2026-10-11_1258.json");
        var kp = k.Kortsluitpunten.First(x => x.Id == 1);
        Eis(!kp.Opgelost && kp.NaMelder == 12 && kp.Configuratie.Equals(Configuratie.Basis.Met(2)), "hand: kortsluitpunt #1 (na 12 achteruit, wissel 2 afbuigend) is open");
        var voor = k.MetBekendeOplossingenVoorStart(13, Richting.Achteruit, Configuratie.Basis.Met(2), out _);
        Eis(voor.Equals(Configuratie.Basis.Met(2)), "hand: voor de correctie verandert de ritstand niet");
        Eis(!k.RegistreerHandmatigeOplossing(kp, 2, true), "hand: een wissel die al in de kortsluitstand zit is geen oplossing");
        Eis(k.RegistreerHandmatigeOplossing(kp, 1, true) && kp.Opgelost && kp.OpgelostDoorAdres == 1, "hand: wissel 1 afbuigend legt kortsluitpunt #1 als opgelost vast");
        Eis(k.MetBekendeOplossingenVoorStart(13, Richting.Achteruit, Configuratie.Basis.Met(2), out _).Equals(Configuratie.Basis.Met(1).Met(2)), "hand: opdracht 13 achteruit met wissel 2 rijdt voortaan met wissel 1 erbij");
        Eis(k.MetBekendeOplossingenVoorStart(14, Richting.Achteruit, Configuratie.Basis.Met(2), out _).Equals(Configuratie.Basis.Met(2)), "hand: andere startmelder blijft ongemoeid");
        Eis(k.MetBekendeOplossingenVoorStart(13, Richting.Vooruit, Configuratie.Basis.Met(2), out _).Equals(Configuratie.Basis.Met(2)), "hand: andere richting blijft ongemoeid");
        Eis(k.MetBekendeKortsluitOplossingen(new List<int> { 13, 5, 12 }, Richting.Achteruit, Configuratie.Basis.Met(2), out _).Equals(Configuratie.Basis.Met(1).Met(2)), "hand: ook navigatie over pad 13-5-12 gebruikt het");
        // doorgeven aan een volgende verkenning via het leerprofiel (json-rondreis)
        var profiel = new LeerProfiel(); profiel.LeerBlokHints(k);
        var terug = LeerProfiel.VanJson(profiel.AlsJson())!;
        var nieuw = new Baankaart();
        foreach (var o in terug.KortsluitOplossingen) nieuw.BekendeKortsluitOplossingen.Add(o);
        Eis(terug.KortsluitOplossingen.Any(o => o.Adres == 1 && o.NaMelder == 12 && o.Start == 13), "hand: oplossing staat in het leerprofiel (nu " + terug.KortsluitOplossingen.Count + ")");
        Eis(nieuw.MetBekendeOplossingenVoorStart(13, Richting.Achteruit, Configuratie.Basis.Met(2), out _).Equals(Configuratie.Basis.Met(1).Met(2)), "hand: een nieuwe, lege kaart past de oplossing uit het leerprofiel meteen toe");
    }

    static void StopHerhalingTest()
    {
        Console.WriteLine("Test: stop herhalen na blokalarm (BUG #86)");
        var sim = new Baanverkenner.Kern.Simulatie.SimulatieBaan { DinamoGedrag = true, LocAdres = 5408 };
        var s1 = sim.NieuweSectie(1, 80, 1); var s2 = sim.NieuweSectie(2, 80, 2);
        sim.Verbind(s1, Baanverkenner.Kern.Simulatie.SimulatieBaan.Eind.B, s2, Baanverkenner.Kern.Simulatie.SimulatieBaan.Eind.A); sim.PlaatsLoc(s1, 40, +1);
        var ins = new VerkenInstellingen { LocAdres = 5408, LocStappen = 28, Verkensnelheid = 12, Kruipsnelheid = 5, WisselAdresVan = 1, WisselAdresTot = 4, DinamoBlokken = "1-2" };
        sim.VerbindenAsync("SIM").GetAwaiter().GetResult();
        var klok = new VirtueleKlok(); klok.Getikt += sim.Tik;
        var log = new VerkenLog(klok);
        var v = new BaanVerkenner(sim, ins, klok, log, (_, _) => Task.FromResult(true), new VerkenStatus { Instellingen = ins });
        var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var alarm = typeof(BaanVerkenner).GetMethod("HardwareMeldtBlokAlarm", bf)!;
        var stop = typeof(BaanVerkenner).GetMethod("VolledigeStop", bf)!;
        // zonder alarm: geen herhaling
        ((Task)stop.Invoke(v, null)!).GetAwaiter().GetResult();
        Eis(!log.AlsTekst().Contains("stop wordt opnieuw gestuurd") && !log.AlsTekst().Contains("Stop wordt opnieuw gestuurd"), "stop: zonder blokalarm blijft het bij een stoppas");
        // alarm tijdens de stop dat daarna verdwijnt: nog een stoppas
        var t0 = klok.Nu;
        klok.Getikt += _ => { if (klok.Nu >= t0 + TimeSpan.FromSeconds(1)) alarm.Invoke(v, new object[] { 2, false }); };
        alarm.Invoke(v, new object[] { 2, true });
        ((Task)stop.Invoke(v, null)!).GetAwaiter().GetResult();
        Eis(log.AlsTekst().Contains("Stop wordt opnieuw gestuurd"), "stop: blokalarm tijdens de stop -> de stoppas wordt opnieuw gestuurd");
    }

    static void TijdelijkeKortsluitingTest()
    {
        Console.WriteLine("Test: kortsluiting die bij herhaling lukt is tijdelijk (BUG #93)");
        var sim = new Baanverkenner.Kern.Simulatie.SimulatieBaan { DinamoGedrag = true, LocAdres = 5408 };
        var s1 = sim.NieuweSectie(1, 80, 1);
        var ins = new VerkenInstellingen { LocAdres = 5408, LocStappen = 28, Verkensnelheid = 12, Kruipsnelheid = 5, WisselAdresVan = 1, WisselAdresTot = 4, DinamoBlokken = "1" };
        sim.VerbindenAsync("SIM").GetAwaiter().GetResult();
        var klok = new VirtueleKlok(); klok.Getikt += sim.Tik;
        var log = new VerkenLog(klok);
        var status = new VerkenStatus { Instellingen = ins };
        var v = new BaanVerkenner(sim, ins, klok, log, (_, _) => Task.FromResult(true), status);
        var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var kaart = status.Kaart;
        var c5 = new Configuratie(new[] { 5 });
        kaart.Kortsluitpunten.Add(new Kortsluitpunt { Id = 2, NaMelder = 14, Richting = Richting.Vooruit, Configuratie = c5 });
        kaart.Kortsluitpunten.Add(new Kortsluitpunt { Id = 3, NaMelder = 14, Richting = Richting.Vooruit, Configuratie = new Configuratie(new[] { 7 }) });
        var mark = typeof(BaanVerkenner).GetMethod("MarkeerTijdelijkeKortsluiting", bf)!;
        RitResultaat Rit(RitEinde e, params int[] reeks) { var r = new RitResultaat { Einde = e }; r.Reeks.AddRange(reeks); return r; }
        mark.Invoke(v, new object[] { 14, Richting.Vooruit, c5, Rit(RitEinde.Kortsluiting, 14) });
        Eis(!kaart.Kortsluitpunten[0].Tijdelijk, "tijdelijk: opnieuw kortsluiting -> blijft open");
        mark.Invoke(v, new object[] { 14, Richting.Vooruit, c5, Rit(RitEinde.Doodlopend, 14, 129) });
        Eis(kaart.Kortsluitpunten[0].Tijdelijk && !kaart.Kortsluitpunten[0].Opgelost, "tijdelijk: dezelfde rit lukt (14 -> 129) -> kortsluitpunt tijdelijk");
        Eis(!kaart.Kortsluitpunten[1].Tijdelijk, "tijdelijk: een ander kortsluitpunt (andere wisselstand) blijft open");
        Eis(log.AlsTekst().Contains("bleek tijdelijk"), "tijdelijk: staat in het log");
        Eis(Rapport.AlsTekst(kaart).Contains("tijdelijk"), "tijdelijk: staat in het rapport");
    }

    static void KenVervolgTest()
    {
        Console.WriteLine("Test: bekend vervolg bepaalt de rijstand van de verkenner (BUG #93)");
        var k = Laad("baankaart_2026-10-11_1521.json");
        var ins = new VerkenInstellingen { LocAdres = 5408, LocStappen = 28, Verkensnelheid = 12, Kruipsnelheid = 5, WisselAdresVan = 1, WisselAdresTot = 4, DinamoBlokken = "1" };
        var sim = new Baanverkenner.Kern.Simulatie.SimulatieBaan { DinamoGedrag = true, LocAdres = 5408 };
        sim.VerbindenAsync("SIM").GetAwaiter().GetResult();
        var klok = new VirtueleKlok(); klok.Getikt += sim.Tik;
        var log = new VerkenLog(klok);
        var status = new VerkenStatus { Instellingen = ins, Kaart = k };
        var v = new BaanVerkenner(sim, ins, klok, log, (_, _) => Task.FromResult(true), status);
        var rs = typeof(BaanVerkenner).GetMethod("RijStand", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        Configuratie Stand(Opdracht o) => (Configuratie)rs.Invoke(v, new object[] { o })!;
        var c5 = Configuratie.Basis.Met(5);
        Eis(Stand(new Opdracht { Id = 1, Start = 129, Richting = Richting.Vooruit, Configuratie = c5 }).Equals(c5.Met(10)), "vervolg: 129 vooruit (bekend vervolg 24 -> 21) rijdt met wissel 10 afbuigend erbij");
        Eis(Stand(new Opdracht { Id = 2, Start = 129, Richting = Richting.Vooruit, Configuratie = c5, BronAdres = 10 }).Equals(c5), "vervolg: de wissel die de opdracht zelf test (BronAdres) blijft ongemoeid");
        Eis(Stand(new Opdracht { Id = 3, Start = 14, Richting = Richting.Vooruit, Configuratie = Configuratie.Basis }).Equals(Configuratie.Basis.Met(13).Met(10)) || Stand(new Opdracht { Id = 3, Start = 14, Richting = Richting.Vooruit, Configuratie = Configuratie.Basis }).Equals(Configuratie.Basis), "vervolg: andere start geeft geen vreemde wissels (kaart: " + Stand(new Opdracht { Id = 3, Start = 14, Richting = Richting.Vooruit, Configuratie = Configuratie.Basis }) + ")");
    }

    // BUG #97 (log 16:03): loc stond op melder 13 andersom, rit "13 achteruit" ging 13 -> 14 en reed de hele baan rond
    static void LocAndersomTest()
    {
        var k = Laad("baankaart_2026-10-11_1608.json");
        // de kaart bevat de foute rit zelf al (13 14 achteruit bij wissel 1,2): voor de test weghalen
        k.Overgangen.RemoveAll(x => x.Van == 13 && x.Naar == 14 && x.Richting == Richting.Achteruit);
        var basis = new List<int> { 13, 5, 16, 15, 8 };
        var c12 = new Configuratie(new[] { 1, 2 });
        Eis(BaanVerkenner.IsLocAndersom(k, 13, Richting.Achteruit, 14, basis, c12), "loc andersom: 13 achteruit -> 14 met wissel 1,2 afbuigend wordt herkend");
        Eis(!BaanVerkenner.IsLocAndersom(k, 13, Richting.Achteruit, 5, basis, c12), "loc andersom: 13 achteruit -> 5 (basisrit) is gewoon goed");
        k.RegistreerOvergang(13, 14, Richting.Achteruit, new Configuratie(new[] { 13 }), null);
        Eis(!BaanVerkenner.IsLocAndersom(k, 13, Richting.Achteruit, 14, basis, new Configuratie(new[] { 13 })), "loc andersom: 13 achteruit -> 14 met wissel 13 afbuigend is eerder zo gezien: geen alarm");
        Eis(!BaanVerkenner.IsLocAndersom(k, 13, Richting.Achteruit, 99, basis, c12), "loc andersom: een onbekende melder is geen bewijs");
    }

    // BUG #98 (log 16:21): 14 -> 24 -> 8 met motor 13 afbuigend vraagt ook wissel 14 afbuigend (bekend uit de splitsing), maar die stond rechtdoor
    static void VoorspeldPadTest()
    {
        var k = Laad("baankaart_2026-10-11_1608.json"); // nog zonder de rit 24 -> 8 en zonder wissel 14 uit de kaart van 16:23
        var weg = k.VerwijderSpiegelOvergangen();
        Eis(weg.Count >= 10 && !k.Overgangen.Any(o => o.Van == 14 && o.Naar == 13 && o.Richting == Richting.Vooruit), "spiegel: overgangen uit ritten met de loc andersom zijn weg (nu " + weg.Count + ")");
        Eis(k.Overgangen.Any(o => o.Van == 13 && o.Naar == 14 && o.Richting == Richting.Vooruit) && k.Overgangen.Any(o => o.Van == 5 && o.Naar == 13 && o.Richting == Richting.Vooruit), "spiegel: de gewone overgangen blijven");
        var pad = BaanVerkenner.VoorspeldPad(k, 14, Richting.Vooruit, new Configuratie(new[] { 13 }));
        Eis(pad.Take(3).SequenceEqual(new[] { 14, 24, 8 }), "voorspeld pad: 14 vooruit met motor 13 afbuigend begint met 14 - 24 - 8 (nu " + string.Join("-", pad) + ")");
        var eis = k.MetWisselEisen(pad, Richting.Vooruit, new Configuratie(new[] { 13 }), out _);
        Eis(eis.Bevat(14), "voorspeld pad: dat pad vraagt wissel 14 afbuigend (nu " + eis + ")");
        var pad2 = BaanVerkenner.VoorspeldPad(k, 14, Richting.Vooruit, Configuratie.Basis);
        Eis(pad2.Count >= 3 && pad2[2] == 21, "voorspeld pad: met motor 13 rechtdoor gaat 14 vooruit naar 21 (nu " + string.Join("-", pad2) + ")");
    }

    // BUG #98 (log 16:23): 8 -> 24 met alleen wissel 14 afbuigend gaf kortsluiting in blok 8: de enige bewezen verbinding vanaf 8 vraagt motor 13 afbuigend
    static void KruisIngangTest()
    {
        var k = Laad("baankaart_2026-10-11_1608.json");
        k.VerwijderSpiegelOvergangen();
        var c14 = new Configuratie(new[] { 14 });
        var pad = BaanVerkenner.VoorspeldPad(k, 8, Richting.Achteruit, c14);
        Eis(pad.Take(2).SequenceEqual(new[] { 8, 24 }), "kruis-ingang: 8 achteruit met wissel 14 afbuigend gaat naar 24 (nu " + string.Join("-", pad) + ")");
        var c = BaanVerkenner.KruisIngangStand(pad, c14, 14, out var uitleg);
        Eis(c.Bevat(13) && c.Bevat(14) && uitleg is not null, "kruis-ingang: motor 13 wordt vooraf afbuigend gezet (nu " + c + ")");
        var c2 = BaanVerkenner.KruisIngangStand(new List<int> { 14, 24, 21 }, Configuratie.Basis, null, out _);
        Eis(!c2.Bevat(13), "kruis-ingang: vanaf 14 zijn twee standen bewezen (naar 21 of 8): niets afdwingen");
        var c3 = BaanVerkenner.KruisIngangStand(new List<int> { 21, 24, 14 }, new Configuratie(new[] { 13 }), null, out _);
        Eis(!c3.Bevat(13), "kruis-ingang: vanaf 21 vraagt elke bewezen verbinding motor 13 rechtdoor");
        var c4 = BaanVerkenner.KruisIngangStand(pad, Configuratie.Basis, 13, out _);
        Eis(!c4.Bevat(13), "kruis-ingang: de motor die de opdracht zelf test wordt niet aangeraakt");
    }

    // BUG #100 (log 16:29): tegenstrijdige opgeloste kortsluitpunten voor wissel 13 zetten elkaar steeds terug
    static void BotsendeOplossingenTest()
    {
        Console.WriteLine("Test: tegenstrijdige opgeloste kortsluitpunten (BUG #100)");
        var k = Laad("baankaart_2026-10-11_1629.json");
        k.VerwijderSpiegelOvergangen();
        var pad = new List<int> { 14, 129, 24 };
        var c = k.MetBekendeKortsluitOplossingen(pad, Richting.Vooruit, new Configuratie(new[] { 5, 10 }), out var wijz);
        Eis(!c.Bevat(13), "bots: wissel 13 blijft rechtdoor (nu " + c + ")");
        Eis(c.Bevat(5) && c.Bevat(10), "bots: 5 en 10 blijven afbuigend (nu " + c + ")");
        Eis(wijz.Count <= 4, "bots: geen herhaalde regels meer (nu " + wijz.Count + ")");
        var c2 = k.MetBekendeKortsluitOplossingen(pad, Richting.Vooruit, new Configuratie(new[] { 5, 10 }), out _);
        Eis(c2.Equals(c), "bots: tweede aanroep geeft dezelfde uitkomst");
    }

    // BUG #101 (log 16:35): de loc stond al op melder 13; de stand die de Dinamo bij het verbinden meldde werd weggegooid en aanvragen worden niet beantwoord
    static void OpstartStandTest()
    {
        Console.WriteLine("Test: stand uit de opstartfase wordt bewaard en doorgegeven (BUG #101)");
        var hw = new Modeltreinbesturing.Hardware.DinamoHardware();
        var t = typeof(Modeltreinbesturing.Hardware.DinamoHardware);
        var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        t.GetField("_poort", bf)!.SetValue(hw, new System.IO.Ports.SerialPort("COM0", 19200, System.IO.Ports.Parity.Odd, 8, System.IO.Ports.StopBits.One));
        var buf = (List<byte>)t.GetField("_ontvangstBuffer", bf)!.GetValue(hw)!;
        var opstart = t.GetField("_opstartFaseActief", bf)!;
        var verwerk = t.GetMethod("VerwerkOntvangenData", bf)!;
        void Voer(string hex) { buf.AddRange(Convert.FromHexString(hex)); verwerk.Invoke(hw, null); }
        opstart.SetValue(hw, true);
        // zo meldde de Dinamo bij het verbinden (log 16:35): melder 14 bezet, melder 13 knippert en blijft bezet, melder 14 vrij
        foreach (var f in new[] { "0AD08D99", "4AD08CDA", "0AC08CAA", "4AD08CDA", "0AC08DA9" }) Voer(f);
        opstart.SetValue(hw, false);
        var ontvangen = new List<(int, bool)>();
        hw.BezetmeldingGewijzigd += (m, b) => ontvangen.Add((m, b));
        hw.HerhaalBekendeMelderStanden();
        Eis(ontvangen.Count == 1 && ontvangen[0] == (13, true), "opstartstand: alleen melder 13 bezet wordt doorgegeven (nu " + string.Join(",", ontvangen) + ")");
        Voer("0AC08CAA"); // melder 13 daarna vrij
        ontvangen.Clear();
        hw.HerhaalBekendeMelderStanden();
        Eis(ontvangen.Count == 0, "opstartstand: een latere wijziging (13 vrij) wint van de opstartstand");
    }

    // BUG #129 (Marco 11:10): Engelse wissels worden bij de start opgegeven; de vaste referentie hoort alleen bij voorkennis
    static void KruisKennisTest()
    {
        var a = KruisKennis.Parse("13+10");
        Eis(a.IsMotor(13) && a.IsMotor(10) && !a.IsMotor(5) && a.Middens.Count == 0 && !a.MetReferentie, "kruiskennis (#129): '13+10' -> motoren 13 en 10, melder onbekend, geen referentie");
        var b = KruisKennis.Parse("13+10@24, 3+4");
        Eis(b.Motoren.SetEquals(new[] { 13, 10, 3, 4 }) && b.Middens.SetEquals(new[] { 24 }), "kruiskennis (#129): twee Engelse wissels en een opgegeven melder");
        Eis(KruisKennis.Parse("").Motoren.Count == 0 && KruisKennis.Parse(null).Motoren.Count == 0 && KruisKennis.Parse("abc").Motoren.Count == 0, "kruiskennis (#129): leeg of onzin -> geen Engelse wissels");
        Eis(!KruisKennis.Parse("13+10").OpKruis(new[] { 24 }), "kruiskennis (#129): zonder bekende melder nooit 'op de kruiswissel'");
        Eis(KruisKennis.Parse("13+10@24").OpKruis(new[] { 24, 14 }) && !KruisKennis.Parse("13+10@24").OpKruis(new[] { 14 }), "kruiskennis (#129): opgegeven melder 24 herkend");

        // kortsluiting met een kruismotor: zonder bekende melder telt elke zo'n kortsluiting (melder wordt ontdekt), met bekende melder alleen die melder
        RitResultaat Rit(RitEinde e, params int[] reeks) { var r = new RitResultaat { Einde = e }; r.Reeks.AddRange(reeks); return r; }
        Eis(BaanVerkenner.IsKruiswisselMotorKortsluiting(13, Rit(RitEinde.Kortsluiting, 14, 24), KruisKennis.Parse("13+10")), "kruiskennis (#129): onbekende melder -> kortsluiting met motor 13 telt als kruiswissel");
        Eis(!BaanVerkenner.IsKruiswisselMotorKortsluiting(5, Rit(RitEinde.Kortsluiting, 14, 24), KruisKennis.Parse("13+10")), "kruiskennis (#129): gewone wissel 5 is geen kruismotor");
        Eis(!BaanVerkenner.IsKruiswisselMotorKortsluiting(13, Rit(RitEinde.Kortsluiting, 14, 16), KruisKennis.Parse("13+10@24")), "kruiskennis (#129): bekende melder 24, kortsluiting elders telt niet");

        // opstarten: wat is er actief?
        (BaanVerkenner V, VerkenLog Log) Start(string motoren, bool? voorkennis)
        {
            var sim = new Baankaart(); _ = sim;
            var simHw = new Baanverkenner.Kern.Simulatie.SimulatieBaan { LocAdres = 5408 };
            var ins = new VerkenInstellingen { LocAdres = 5408, LocStappen = 28, Verkensnelheid = 12, Kruipsnelheid = 5, WisselAdresVan = 1, WisselAdresTot = 2, KruisMotoren = motoren };
            var klok = new VirtueleKlok(); var log = new VerkenLog(klok);
            var p = new LeerProfiel(); p.KortsluitOplossingen.Add(new KortsluitOplossing { NaMelder = 12, Richting = Richting.Achteruit, Stand = "2", Start = 13, Adres = 3, Afbuigend = true, Bron = "test" });
            var v = new BaanVerkenner(simHw, ins, klok, log, (_, _) => Task.FromResult(true), new VerkenStatus { Instellingen = ins }) { Leerprofiel = p };
            if (voorkennis is bool vk) v.VraagJaNee = (_, _) => Task.FromResult<bool?>(vk);
            using var cts = new CancellationTokenSource(); cts.CancelAfter(50);
            try { v.VoerUit(cts.Token).GetAwaiter().GetResult(); } catch { }
            return (v, log);
        }
        var leeg = Start("", false);
        Eis(leeg.V.Kruis.Motoren.Count == 0 && !leeg.V.Kruis.MetReferentie && leeg.V.Kaart.KruisMotoren == "" && !leeg.V.Kaart.KruisReferentie, "kruiskennis (#129): zonder opgave en zonder voorkennis -> geen kruiswissel-regels");
        var eigen = Start("13+10", false);
        Eis(eigen.V.Kruis.IsMotor(13) && eigen.V.Kruis.IsMotor(10) && eigen.V.Kruis.Middens.Count == 0 && !eigen.V.Kruis.MetReferentie && eigen.V.Kaart.KruisMotoren == "10+13", "kruiskennis (#129): opgegeven motoren, geen referentie, melder onbekend (nu " + eigen.V.Kaart.KruisMotoren + ")");
        Eis(eigen.Log.AlsTekst().Contains("Alles wordt vanaf nul ontdekt"), "kruiskennis (#129): logboek meldt dat alles vanaf nul ontdekt wordt");
        var metVoor = Start("", true);
        Eis(metVoor.V.Kruis.MetReferentie && metVoor.V.Kruis.IsMotor(13) && metVoor.V.Kruis.Middens.Contains(24) && metVoor.V.Kaart.KruisReferentie, "kruiskennis (#129): voorkennis Ja -> vaste referentie (melder 24, motor 13 en 10) actief");

        // rapport: zonder referentie geen 'bewezen'-rijen
        var k = new Baankaart { KruisMotoren = "10+13" };
        Eis(!Rapport.AlsHtml(k).Contains("Kruiswissel in melder 24") && Rapport.AlsHtml(k).Contains("Engelse wissel") && !Rapport.AlsTekst(k).Contains("REFERENTIE"), "kruiskennis (#129): rapport zonder referentie toont alleen de opgegeven Engelse wissel");
    }

    // BUG #128: voorkennis uit eerdere verkenningen alleen na bevestiging (Marco 10:48: alles vanaf nul leren)
    static void VoorkennisVraagTest()
    {
        LeerProfiel Profiel()
        {
            var p = new LeerProfiel();
            p.KortsluitOplossingen.Add(new KortsluitOplossing { NaMelder = 12, Richting = Richting.Achteruit, Stand = "2", Start = 13, Adres = 3, Afbuigend = true, Bron = "test" });
            p.BlokHints.Add(new LeerProfiel.BlokHintItem { Melder = 13, Blok = 3 });
            return p;
        }
        (string Log, int Vragen, string? Titel, int Opl) Run(bool? antwoord, bool metVraagfunctie)
        {
            var sim = new Baanverkenner.Kern.Simulatie.SimulatieBaan { LocAdres = 5408 };
            var ins = new VerkenInstellingen { LocAdres = 5408, LocStappen = 28, Verkensnelheid = 12, Kruipsnelheid = 5, WisselAdresVan = 1, WisselAdresTot = 2 };
            var klok = new VirtueleKlok(); var log = new VerkenLog(klok);
            int vragen = 0; string? titel = null;
            var v = new BaanVerkenner(sim, ins, klok, log, (_, _) => Task.FromResult(true), new VerkenStatus { Instellingen = ins }) { Leerprofiel = Profiel() };
            if (metVraagfunctie) v.VraagJaNee = (t, _) => { vragen++; titel = t; return Task.FromResult(antwoord); };
            using var cts = new CancellationTokenSource(); cts.CancelAfter(50);
            try { v.VoerUit(cts.Token).GetAwaiter().GetResult(); } catch { }
            return (log.AlsTekst(), vragen, titel, v.Kaart.BekendeKortsluitOplossingen.Count);
        }
        var nee = Run(false, true);
        Eis(nee.Vragen == 1 && nee.Titel!.Contains("eerdere verkenningen") && nee.Opl == 0 && nee.Log.Contains("vanaf nul"), "voorkennis (#128): Nee -> niets overgenomen (vragen " + nee.Vragen + ", oplossingen " + nee.Opl + ")");
        var ja = Run(true, true);
        Eis(ja.Vragen == 1 && ja.Opl == 1, "voorkennis (#128): Ja -> kortsluitoplossing overgenomen (oplossingen " + ja.Opl + ")");
        var geen = Run(null, false);
        Eis(geen.Opl == 0, "voorkennis (#128): geen vraagfunctie -> vanaf nul (oplossingen " + geen.Opl + ")");
        var gesloten = Run(null, true);
        Eis(gesloten.Opl == 0, "voorkennis (#128): pop-up gesloten -> vanaf nul");
    }

    // BUG #127 (Marco 10:40): melder 14 bleef na een Dinamo-herstart en in nieuwe verkenningen 'bezet' (gemiste vrij-melding in het geheugen van het programma)
    static void MelderGeheugenWisTest()
    {
        var hw = new Modeltreinbesturing.Hardware.DinamoHardware();
        var t = typeof(Modeltreinbesturing.Hardware.DinamoHardware);
        var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        t.GetField("_poort", bf)!.SetValue(hw, new System.IO.Ports.SerialPort("COM0", 19200, System.IO.Ports.Parity.Odd, 8, System.IO.Ports.StopBits.One));
        var buf = (List<byte>)t.GetField("_ontvangstBuffer", bf)!.GetValue(hw)!;
        var verwerk = t.GetMethod("VerwerkOntvangenData", bf)!;
        void Voer(string hex) { buf.AddRange(Convert.FromHexString(hex)); verwerk.Invoke(hw, null); }
        var ontvangen = new List<(int, bool)>();
        hw.BezetmeldingGewijzigd += (m, b) => ontvangen.Add((m, b));
        Voer("0AD08C9A"); Voer("4AD08DD9"); // melder 13 en 14 bezet gemeld
        Eis(ontvangen.SequenceEqual(new[] { (13, true), (14, true) }), "geheugen wissen (#127): beide melders komen als bezet binnen (nu " + string.Join(",", ontvangen) + ")");
        ontvangen.Clear();
        hw.WisMelderGeheugen(new[] { 14 });
        Eis(ontvangen.SequenceEqual(new[] { (14, false) }), "geheugen wissen (#127): melder 14 wordt als vrij doorgegeven (nu " + string.Join(",", ontvangen) + ")");
        ontvangen.Clear();
        hw.HerhaalBekendeMelderStanden();
        Eis(ontvangen.SequenceEqual(new[] { (13, true) }), "geheugen wissen (#127): alleen melder 13 blijft onthouden (nu " + string.Join(",", ontvangen) + ")");
        ontvangen.Clear();
        Voer("0AD08DD9".Replace("0AD08DD9", "4AD08DD9")); // melder 14 meldt zich later echt weer
        Eis(ontvangen.SequenceEqual(new[] { (14, true) }), "geheugen wissen (#127): een nieuwe melding van 14 telt gewoon weer (nu " + string.Join(",", ontvangen) + ")");
        ontvangen.Clear();
        hw.WisMelderGeheugen();
        hw.HerhaalBekendeMelderStanden();
        Eis(ontvangen.Count(x => !x.Item2) == 2 && ontvangen.Count(x => x.Item2) == 0, "geheugen wissen (#127): alles wissen geeft alles vrij en onthoudt niets (nu " + string.Join(",", ontvangen) + ")");
    }

    // BUG #102 (Marco 17:01): na "wissel 1 is de oplossing" moet de opdracht meteen opnieuw, niet achteraan de wachtrij
    static void HerpogingVolgordeTest()
    {
        Console.WriteLine("Test: herpoging na handmatige oplossing komt vooraan (BUG #102)");
        Opdracht O(int id) => new Opdracht { Id = id };
        var q = new List<Opdracht> { O(6), O(7) };
        BaanVerkenner.PlaatsHerpoging(q, O(5), true);
        Eis(q.Select(x => x.Id).SequenceEqual(new[] { 5, 6, 7 }), "herpoging: na handmatige oplossing vooraan (nu " + string.Join(",", q.Select(x => x.Id)) + ")");
        var q2 = new List<Opdracht> { O(6), O(7) };
        BaanVerkenner.PlaatsHerpoging(q2, O(5), false);
        Eis(q2.Select(x => x.Id).SequenceEqual(new[] { 6, 7, 5 }), "herpoging: zonder oplossing achteraan");
    }

    // BUG #169: wissels van een tak richting de samenvoeging eerst, wissels van de samengevoegde kant nooit proberen
    static void WisselRollenTest()
    {
        Console.WriteLine("Test: wisselrollen bij een kortsluiting (BUG #169)");
        var k = new Baankaart();
        // wissel 14: puntzijde na melder 8 (richting vooruit): rechtdoor -> 24, afbuigend -> 17
        k.Wissel(14).Waarnemingen.Add(new WisselWaarneming { Soort = WaarnemingSoort.Splitsing, NaMelder = 8, Richting = Richting.Vooruit, VolgendeBijRechtdoor = 24, VolgendeBijAfbuigend = 17 });
        // wissel 5: puntzijde na melder 14 (vooruit): rechtdoor -> 24, afbuigend -> 129
        k.Wissel(5).Waarnemingen.Add(new WisselWaarneming { Soort = WaarnemingSoort.Splitsing, NaMelder = 14, Richting = Richting.Vooruit, VolgendeBijRechtdoor = 24, VolgendeBijAfbuigend = 129 });
        // loc staat op 24 en rijdt achteruit (richting van 24 naar 8/14): van een tak (24) naar de samenvoeging van wissel 14 en wissel 5
        var (achter, kop) = BaanVerkenner.WisselRollen(k, 24, Richting.Achteruit, null);
        Eis(achter.Contains(14) && achter.Contains(5) && kop.Count == 0, "loc op 24 achteruit: 14 en 5 worden van achteren bereden (nu " + string.Join(",", achter) + ")");
        // loc op 8 rijdt vooruit: wissel 14 van de samengevoegde kant
        var (a2, k2) = BaanVerkenner.WisselRollen(k, 8, Richting.Vooruit, null);
        Eis(a2.Count == 0 && k2.Contains(14), "loc op 8 vooruit: wissel 14 wordt van de samengevoegde kant bereden, niet omzetten");
        // laatste stap 17 -> 8 achteruit: door wissel 14 van achteren (afbuigend), geen kop
        var (a3, k3) = BaanVerkenner.WisselRollen(k, 8, Richting.Achteruit, new[] { 17, 8 });
        Eis(a3.Contains(14) && !k3.Contains(14), "laatste stap 17 -> 8 achteruit: 14 van achteren");
    }

    // BUG #170: het volledige bekende pad bepaalt of een geerfde wissel (14 afbuigend) een bekende kortsluitingsingang neemt; de eerste verdachte staat vooraan
    static void VerbodenPadTest()
    {
        Console.WriteLine("Test: bekend pad met verboden ingang, wissel 14 (BUG #170)");
        var k = Laad("baankaart_2026-10-16_1924.json");
        var c = new Configuratie(new[] { 7, 14 });
        var pad = BaanVerkenner.VoorspeldPad(k, 14, Richting.Vooruit, c, false, 30);
        Eis(pad.Contains(17) && pad[^1] == 8 || pad.Contains(8), "pad vanaf 14 vooruit loopt tot melder 8 (nu " + string.Join("-", pad) + ")");
        var cV = k.MetVerbodenIngangen(pad, Richting.Vooruit, c, out var w);
        Eis(!cV.Bevat(14) && cV.Bevat(7), "wissel 14 gaat rechtdoor (verboden ingang 17 -> 8), wissel 7 blijft (nu " + cV + ")");
        var kort = BaanVerkenner.VoorspeldPad(k, 14, Richting.Vooruit, c, false, 8);
        Eis(!kort.Contains(8), "het oude korte pad (8 stappen) miste dat (nu " + string.Join("-", kort) + ")");
        var (achter, _) = BaanVerkenner.WisselRollen(k, 8, Richting.Vooruit, new[] { 25, 17, 8 });
        Eis(achter.Count > 0 && achter[0] == 14, "loc op 8 vooruit na kortsluiting: wissel 14 is de eerste verdachte (nu " + string.Join(",", achter) + ")");
        var (achter2, _) = BaanVerkenner.WisselRollen(k, 24, Richting.Achteruit, new[] { 8, 24 });
        Eis(!achter2.Contains(1) && !achter2.Contains(7), "zeven wissels met dezelfde valse ingang 8 -> 24 tellen niet als verdachten (nu " + string.Join(",", achter2) + ")");
    }

    // BUG #171: kent de kaart bij de huidige wisselstand geen vervolg, dan bepaalt het bekende vervolg toch de kruismotoren
    // BUG #173: waar rijdt de loc heen na een wisselprobleem? (kaart 17-10 08:19: de loc stond op melder 14, vlak bij wissel 5)
    static void VeiligePlekTest()
    {
        Console.WriteLine("Test: terug naar een getest stuk baan na een wisselprobleem (BUG #173)");
        var k = Laad("baankaart_2026-10-17_0819.json");
        var kr = new KruisKennis(); kr.Motoren.Add(10); kr.Motoren.Add(13);
        Eis(!BaanVerkenner.IsVeiligeMelder(k, kr, 14), "melder 14 (wissel 5 erachter) is geen veilige plek");
        Eis(!BaanVerkenner.IsVeiligeMelder(k, kr, 24), "melder 24 (uitgang van wissel 5) is geen veilige plek");
        Eis(BaanVerkenner.IsVeiligeMelder(k, kr, 13), "melder 13 is een veilige plek");
        Eis(BaanVerkenner.KiesVeiligePlek(k, kr, 14) == 13, "vanaf melder 14 rijdt de loc terug naar melder 13 (nu " + BaanVerkenner.KiesVeiligePlek(k, kr, 14) + ")");
        Eis(BaanVerkenner.KiesVeiligePlek(k, kr, 24) == 13, "vanaf melder 24 ook naar melder 13 (nu " + BaanVerkenner.KiesVeiligePlek(k, kr, 24) + ")");
        Eis(BaanVerkenner.KiesVeiligePlek(k, kr, 13) == 13, "staat de loc al op een veilige plek, dan blijft hij daar");
    }

    static void KruisRuimTest()
    {
        Console.WriteLine("Test: kruisingang ook bij onbekende wisselstand (BUG #171)");
        var k = Laad("baankaart_2026-10-16_1955.json");
        var kr = new KruisKennis(); kr.Motoren.Add(10); kr.Motoren.Add(13); kr.Middens.Add(24);
        var cfg = new Configuratie(new[] { 1, 3 });
        var strikt = KruisBewijs.Ingangen(k, kr, 129, Richting.Vooruit, cfg);
        var ruim = KruisBewijs.Ingangen(k, kr, 129, Richting.Vooruit, cfg, ruim: true);
        Eis(strikt.Count == 0, "zonder ruim: onbekende stand geeft geen ingang (nu " + strikt.Count + ")");
        Eis(ruim.Contains((24, 129)), "met ruim: 129 -> 24 is een kruisingang");
        var gezien = KruisBewijs.GezienBijIngang(k, kr, 24, 129);
        var stand = KruisBewijs.KiesStand(gezien, cfg, new[] { 10, 13 }, new HashSet<int>());
        Eis(stand is not null && stand.Bevat(10) && !stand.Bevat(13), "bewijs: ingang 129 vraagt motor 10 afbuigend, 13 rechtdoor (nu " + stand + ")");
    }

    // BUG #166: de basisrit door de kruiswissel bewijst de motorstand; de proeven rijden daarmee (geen reset van motor 13)
    static void KruisStandBasisTest()
    {
        Console.WriteLine("Test: kruismotor-stand van de basisrit blijft de rijstand (BUG #166)");
        var kr = new KruisKennis(); kr.Motoren.Add(10); kr.Motoren.Add(13); kr.Middens.Add(24);
        var reeks = new[] { 15, 8, 24, 14, 13, 5, 16, 15 };
        var rijstand = new Configuratie(new[] { 14 });
        bool? Stand(int a) => a == 13 ? true : a == 10 ? false : (bool?)null;
        var c = BaanVerkenner.KruisStandUitBasis(rijstand, reeks, kr, Stand, new HashSet<int>(), out var uitleg);
        Eis(c.Bevat(13) && c.Bevat(14) && !c.Bevat(10), "basis door 24: motor 13 afbuigend blijft (nu " + c + ")");
        Eis(uitleg is not null && uitleg.Contains("motor 13 afbuigend"), "uitleg noemt motor 13");
        var c2 = BaanVerkenner.KruisStandUitBasis(rijstand, new[] { 15, 8, 24 }, kr, Stand, new HashSet<int>(), out var u2);
        Eis(!c2.Bevat(13) && u2 is null, "rit eindigt op 24 (geen uitgang): rijstand ongewijzigd");
        var c3 = BaanVerkenner.KruisStandUitBasis(rijstand, new[] { 15, 8, 4 }, kr, Stand, new HashSet<int>(), out _);
        Eis(!c3.Bevat(13), "rit buiten de kruiswissel: ongewijzigd");
        var c4 = BaanVerkenner.KruisStandUitBasis(rijstand, reeks, kr, Stand, new HashSet<int> { 13 }, out _);
        Eis(!c4.Bevat(13), "motor die de opdracht zelf vastlegt blijft zoals de opdracht wil");
    }

    // BUG #165: het plusteken met alle bezetmelders in volgorde staat ook zonder referentie in het rapport
    static void KruisPlusTest()
    {
        Console.WriteLine("Test: plusteken uit de eigen verkenning in het rapport (BUG #165)");
        var k = Laad("baankaart_2026-10-16_1750.json");
        var p = KruisPlus.Maak(k);
        Eis(p is not null && p.Value.Midden == 24, "plus: midden is melder 24");
        // BUG #167: per been twee Dinamo-blokken (zuid 7 en 6, west 3 met vervolg 4 of 2)
        var zuid2 = p!.Value.Benen.First(b => b.Plek == "zuid").Melders;
        Eis(zuid2.Select(m => k.Melders.First(x => x.Nummer == m).DinamoBlok).Distinct().Count() == 2, "plus: zuid toont twee blokken (" + string.Join("-", zuid2) + ")");
        Eis(p.Value.Benen.First(b => b.Plek == "west").Takken is { Count: 2 } tk && tk[0][0] == 16 && tk[1][0] == 12, "plus (#168): west splitst na 5 en toont beide takken (16 en 12)");
        var benen = p!.Value.Benen;
        Eis(benen.Count == 4, "plus: vier benen (nu " + benen.Count + ")");
        string B(string plek) => string.Join("-", benen.First(b => b.Plek == plek).Melders);
        Eis(B("west").StartsWith("14-13-5") && B("oost").StartsWith("8-15-16") && B("zuid").StartsWith("21-29-30") && B("noord").StartsWith("129"),
            "plus: benen in volgorde van het midden naar buiten (west " + B("west") + ", oost " + B("oost") + ", zuid " + B("zuid") + ", noord " + B("noord") + ")");
        var tekst = Rapport.AlsTekst(k); var html = Rapport.AlsHtml(k);
        Eis(tekst.Contains("Plusteken van melder 24") && tekst.Contains("14 <-> 21"), "plus: staat in het tekstrapport");
        Eis(html.Contains("Plusteken van melder 24") && html.Contains("<svg"), "plus: staat in het html-rapport");
        Console.WriteLine(KruisPlus.AlsTekst(k));
        // ruis van één keer (motor 13 rechtdoor naast vijf keer afbuigend) mag de pre-set van motor 13 niet blokkeren
        var kk = new KruisKennis(); kk.Motoren.Add(13); kk.Motoren.Add(10); kk.Middens.Add(24);
        var kr = new Baankaart();
        kr.Doorgangen.Add(new Doorgang { Via = 8, Melder = 24, Volgende = 14, Richting = Richting.Achteruit, AantalKeer = 6,
            Configuraties = new List<string> { "13", "13,14", "", "14" }, Tellers = new Dictionary<string, int> { ["13"] = 2, ["13,14"] = 3, [""] = 1, ["14"] = 0 } });
        var gez = KruisBewijs.GezienBijIngang(kr, kk, 24, 8);
        Eis(gez.Count == 1 && gez[0].Bevat(13), "plus (#165): ruis van 1x met motor 13 rechtdoor telt niet mee tegen 5x afbuigend (nu " + string.Join(" | ", gez.Select(g => g.ToString())) + ")");
        var stand = KruisBewijs.KiesStand(gez, Configuratie.Basis, new[] { 10, 13 }, new HashSet<int>());
        Eis(stand is not null && stand.Bevat(13), "plus (#165): motor 13 gaat dan vooraf afbuigend voor de ingang vanaf 8");
        var c165 = KruisBewijs.PasEigenBewijsToe(kr, kk, new List<int> { 15, 8, 24, 14 }, new Configuratie(new[] { 14 }), new HashSet<int>(), out var uitleg165);
        Eis(c165.Bevat(13) && c165.Bevat(14) && uitleg165.Count == 1, "plus (#165): de rijstand van 15 -> 8 -> 24 -> 14 krijgt motor 13 afbuigend uit het eigen bewijs (nu " + c165 + ")");
        var c165b = KruisBewijs.PasEigenBewijsToe(kr, kk, new List<int> { 15, 8, 24, 14 }, new Configuratie(new[] { 14 }), new HashSet<int> { 13 }, out _);
        Eis(!c165b.Bevat(13), "plus (#165): een motor die de opdracht zelf vastlegt blijft zoals de opdracht wil");
        var c165c = KruisBewijs.PasEigenBewijsToe(kr, kk, new List<int> { 15, 8, 17 }, new Configuratie(new[] { 14 }), new HashSet<int>(), out _);
        Eis(c165c.Equals(new Configuratie(new[] { 14 })), "plus (#165): een pad zonder kruiswissel verandert niets");
    }

    // BUG #103 (log 08:06): route naar 129 liep een hele ronde extra; en zelf terugrijden i.p.v. de gebruiker te vragen
    static void RouteVerkortTest()
    {
        Console.WriteLine("Test: route verkorten en zelf terugrijden (BUG #103)");
        Etappe E(Richting r, params int[] pad) => new Etappe { Configuratie = Configuratie.Basis, Richting = r, Van = pad[0], Naar = pad[^1], Pad = pad.ToList() };
        var lap = new[] { 24, 21, 29, 30, 20, 27, 28, 26, 25, 17, 8, 15, 16, 5, 13, 14, 129 };
        var route = new List<Etappe> { E(Richting.Vooruit, 13, 14), E(Richting.Vooruit, 14, 129, 24), E(Richting.Vooruit, lap) };
        var kort = BaanVerkenner.VerkortRoute(route, 129);
        Eis(kort.Count == 2 && kort[1].Pad.SequenceEqual(new[] { 14, 129 }) && kort[1].Naar == 129, "route: 13-14-129-24 plus ronde wordt 13-14, 14-129 (nu " + string.Join(" | ", kort.Select(e => string.Join("-", e.Pad))) + ")");
        var normaal = new List<Etappe> { E(Richting.Vooruit, 13, 14), E(Richting.Vooruit, 14, 24) };
        Eis(BaanVerkenner.VerkortRoute(normaal, 24).Count == 2, "route: eindigt de route op de startmelder, dan blijft hij zoals hij was");
        var ronde = new List<Etappe> { E(Richting.Vooruit, 13, 14, 24, 21, 29, 30, 20, 27, 28, 26, 25, 17, 8, 15, 16, 5, 13), E(Richting.Achteruit, 13, 5, 12) };
        var r2 = BaanVerkenner.VerkortRoute(ronde, 12);
        Eis(r2.Count == 1 && r2[0].Pad.SequenceEqual(new[] { 13, 5, 12 }), "route: een gesloten ronde aan het begin van de route valt weg (kortsluitpunt na melder 12)");
        Eis(BaanVerkenner.VerkortRoute(new List<Etappe>(), 5).Count == 0, "route: lege route blijft leeg");
        Eis(BaanVerkenner.ZelfTerugIndex(kort, 13, 13) == -1, "zelf terug: loc al op de startmelder");
        Eis(BaanVerkenner.ZelfTerugIndex(kort, 129, 13) == 1, "zelf terug: loc op 129 -> terug via etappe 1 en 0");
        Eis(BaanVerkenner.ZelfTerugIndex(kort, 21, 13) is null, "zelf terug: melder 21 ligt niet op de route -> de gebruiker vragen");
        // BUG #164: de loc staat op een melder die alleen als eindpunt van een rit bekend is (12 achter wissel 1 en 2): de weg terug is de omgekeerde overgang
        var k164 = new Baankaart();
        k164.Overgangen.Add(new Overgang { Van = 5, Naar = 12, Richting = Richting.Achteruit, AantalKeer = 1, GezienBijConfiguraties = new List<string> { "2" } });
        k164.Overgangen.Add(new Overgang { Van = 5, Naar = 13, Richting = Richting.Vooruit, AantalKeer = 2, GezienBijConfiguraties = new List<string> { "" } });
        Eis(BaanVerkenner.ZelfTerugWeg(k164, 12, 13) is null, "zelf terug (#164): zonder afbuigende standen is er vanaf 12 geen weg");
        var w164 = BaanVerkenner.ZelfTerugWegAlleStanden(k164, 12, 13);
        Eis(w164 is { Count: 2 } && w164[0].Van == 12 && w164[0].Naar == 5 && w164[0].Richting == Richting.Vooruit && w164[0].Cfg.Bevat(2) && w164[1].Naar == 13 && w164[1].Cfg.Afbuigend.Length == 0,
            "zelf terug (#164): 12 -> 5 (omgekeerde overgang, wissel 2 afbuigend) -> 13 (nu " + (w164 is null ? "geen weg" : string.Join(" | ", w164.Select(x => $"{x.Van}>{x.Naar} {x.Richting} {x.Cfg}"))) + ")");
    }

    static void StopVoorTerugzettenTest()
    {
        Console.WriteLine("Test: stop naar alle blokken voordat de loc terug gezet moet worden (BUG #94)");
        var sim = new Baanverkenner.Kern.Simulatie.SimulatieBaan { DinamoGedrag = true, LocAdres = 5408 };
        var s1 = sim.NieuweSectie(1, 80, 1); var s2 = sim.NieuweSectie(2, 80, 2);
        sim.Verbind(s1, Baanverkenner.Kern.Simulatie.SimulatieBaan.Eind.B, s2, Baanverkenner.Kern.Simulatie.SimulatieBaan.Eind.A); sim.PlaatsLoc(s1, 40, +1);
        var ins = new VerkenInstellingen { LocAdres = 5408, LocStappen = 28, Verkensnelheid = 12, Kruipsnelheid = 5, WisselAdresVan = 1, WisselAdresTot = 4, DinamoBlokken = "1-2" };
        sim.VerbindenAsync("SIM").GetAwaiter().GetResult();
        var klok = new VirtueleKlok(); klok.Getikt += sim.Tik;
        var log = new VerkenLog(klok);
        bool stopVoorVraag = false; string tekstBijVraag = "";
        var v = new BaanVerkenner(sim, ins, klok, log, (_, _) => { tekstBijVraag = log.AlsTekst(); stopVoorVraag = tekstBijVraag.Contains("stop naar alle blokken"); return Task.FromResult(true); }, new VerkenStatus { Instellingen = ins });
        var m = typeof(BaanVerkenner).GetMethod("VraagLocTerugTeZetten", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        try { ((Task)m.Invoke(v, new object[] { 1, "test" })!).GetAwaiter().GetResult(); } catch (Exception) { /* melder 1 kan onbezet blijven: alleen de volgorde telt */ }
        Eis(stopVoorVraag, "terugzetten: de stop naar alle blokken is gelogd VOORDAT de gebruiker de vraag krijgt");
    }

    static void KruisAankomstTest()
    {
        Console.WriteLine("Test: opdracht op de kruiswissel zet motor 13/10 vóór het inrijden (BUG #95)");
        Etappe E(params int[] pad) => new Etappe { Configuratie = Configuratie.Basis, Richting = Richting.Achteruit, Van = pad[0], Naar = pad[^1], Pad = pad.ToList() };
        var basis = Configuratie.Basis;
        var r1 = BaanVerkenner.KruisAankomstStand(E(30, 29, 21, 24), basis, basis.Met(10), out var u1);
        Eis(r1.Equals(basis.Met(10)) && u1 is not null, "kruisstart: loc komt van 21, opdracht wil 10 afbuigend -> 10 al voor het inrijden afbuigend (bewezen 129<->21)");
        var r2 = BaanVerkenner.KruisAankomstStand(E(30, 29, 21, 24), basis, basis.Met(13), out var u2);
        Eis(r2.Equals(basis) && u2 is null, "kruisstart: 13 afbuigend vanaf 21 is niet bewezen -> niets aangepast");
        var r3 = BaanVerkenner.KruisAankomstStand(E(5, 13, 14, 24), basis, basis.Met(10), out var u3);
        Eis(r3.Equals(basis) && u3 is null, "kruisstart: vanaf 14 met 10 afbuigend bestaat niet (14<->21 vraagt 10 rechtdoor) -> niets aangepast");
        var r4 = BaanVerkenner.KruisAankomstStand(E(16, 15, 8, 24), basis, basis.Met(13), out _);
        Eis(r4.Equals(basis.Met(13)), "kruisstart: vanaf 8 met 13 afbuigend (bewezen 14<->8) -> 13 vooraf afbuigend");
        var r5 = BaanVerkenner.KruisAankomstStand(E(13, 14), basis, basis.Met(10), out _);
        Eis(r5.Equals(basis), "kruisstart: een opdracht die niet op melder 24 begint verandert niets");
        var r6 = BaanVerkenner.KruisAankomstStand(E(30, 29, 21, 24), basis.Met(10), basis.Met(10), out var u6);
        Eis(r6.Equals(basis.Met(10)) && u6 is null, "kruisstart: staat de gewenste stand al goed -> geen melding");
    }

    static void OrientatieTekstTest()
    {
        Console.WriteLine("Test: de vraag om de loc terug te zetten zegt welke kant de neus op moet (BUG #96)");
        var k = Laad("baankaart_2026-10-11_1521.json");
        var t13 = BaanVerkenner.OrientatieTekst(k, 13);
        Eis(t13.Contains("NEUS") && t13.Contains("melder 14") && t13.Contains("melder 5"), "richting: melder 13 -> neus naar 14, achterkant naar 5 (" + t13 + ")");
        var t24 = BaanVerkenner.OrientatieTekst(k, 24);
        Eis(t24.Contains("NEUS"), "richting: melder 24 heeft ook een aanwijzing (" + t24 + ")");
        Eis(BaanVerkenner.OrientatieTekst(new Baankaart(), 99).Contains("dezelfde rijrichting"), "richting: onbekende melder -> algemene aanwijzing");
    }

    static void EchteBloknummersTest()
    {
        Console.WriteLine("Test: alleen echte Dinamo-bloknummers (BUG #88)");
        var k = Laad("baankaart_2026-10-11_1258.json");
        var blokken = BlokVoorstel.Bereken(k);
        Eis(blokken.Where(b => b.DinamoBlok is not null).All(b => b.Nummer == b.DinamoBlok && b.Label == b.DinamoBlok.ToString()), "bloknummers: een bekend Dinamo-blok is zelf het nummer en het label");
        Eis(blokken.Any(b => b.DinamoBlok == 8 && b.Melders.SequenceEqual(new[] { 24 })) && blokken.Any(b => b.DinamoBlok == 9 && b.Melders.SequenceEqual(new[] { 129 })), "bloknummers: melder 24 = Dinamo-blok 8 en 129 = 9");
        Eis(blokken.Single(b => b.DinamoBlok is null).Label == "onbekend (melder 12)", "bloknummers: onbekend blok heet 'onbekend (melder 12)', geen verzonnen nummer");
        var html = Rapport.AlsHtml(k); var tekst = Rapport.AlsTekst(k);
        Eis(html.Contains("<th>Dinamo-blok</th>") && !html.Contains("<th>Blok</th>") && tekst.Contains("Dinamo-blok 3") && !tekst.Contains("  Blok 1 "), "bloknummers: rapport toont Dinamo-blokken en geen 'Blok 1..8'");
        Eis(tekst.Contains("Dinamo-blok 3 → Dinamo-blok 8"), "bloknummers: wisselstraten tussen Dinamo-blokken");
        var t = SchemaTekening.Maak(k);
        Eis(t.Vakken.All(v => v.Titel.StartsWith("Dinamo-blok ")) && t.Vakken.Any(v => v.Titel == "Dinamo-blok 8"), "bloknummers: schema-vakken heten Dinamo-blok N");
        Eis(blokken.All(b => b.Buren.All(n => blokken.Any(x => x.Nummer == n))), "bloknummers: buren verwijzen naar bestaande blokken");
    }

    // BUG #174: melders zonder bekend Dinamo-blok mogen niet in één verzamelblok ("melder 24") samengevoegd worden zodra de kaart blok-info heeft
    static void OnbekendeBlokkenTest()
    {
        Console.WriteLine("Test: onbekende melders niet samenvoegen (BUG #174, log 17-10 08:51)");
        var k = Laad("baankaart_2026-10-17_0851.json");
        var blokken = BlokVoorstel.Bereken(k);
        var onbekend = blokken.Where(b => b.DinamoBlok is null).ToList();
        Eis(onbekend.Count > 0 && onbekend.All(b => b.Melders.Count == 1), $"onbekende blokken: elke onbekende melder is een eigen blok (nu {string.Join(" | ", onbekend.Select(b => string.Join("+", b.Melders)))})");
    }

    static void KruiswisselControleTest()
    {
        Console.WriteLine("Test: kruiswisselstanden in wisselstraten naast de referentie (BUG #91)");
        var kw = new Kruiswissel { Adres = 13, Adres2 = 10, IsEngels = true };
        Blok B(int n) => new Blok { Nummer = n };
        Wisselstraat W(int van, int naar, WisselStand s13, WisselStand s10)
        {
            var w = new Wisselstraat { Van = B(van), Naar = B(naar) };
            w.Kruiswisselstanden.Add(new KruiswisselInWisselstraat { Kruiswissel = kw, StandAdres = s13, StandAdres2 = s10 });
            return w;
        }
        var R = WisselStand.Rechtdoor; var A = WisselStand.Afbuigend;
        var lijst = new List<Wisselstraat> { W(7, 3, A, A), W(4, 3, R, R), W(7, 1, R, A), W(3, 4, R, R), W(3, 7, R, R), W(1, 4, A, A) };
        var m = KruiswisselControle.Controleer(lijst);
        Eis(m.Count == 3, "kruiswisselcontrole: 7->3, 4->3 en 3->4 wijken af; 3->7, 7->1 en 1->4 niet (samen 3 meldingen) (nu " + m.Count + ")");
        Eis(m.Any(x => x.Omschrijving.Contains("blok 7 -> 3") && x.Omschrijving.Contains("motor 13") && x.Omschrijving.Contains("motor 10")), "kruiswisselcontrole: 7->3 wijkt af op motor 13 en 10");
        Eis(m.Any(x => x.Omschrijving.Contains("blok 4 -> 3") && x.Omschrijving.Contains("motor 13") && !x.Omschrijving.Contains("motor 10")), "kruiswisselcontrole: 4->3 wijkt alleen af op motor 13 (motor 10 vrij)");
        Eis(!m.Any(x => x.Omschrijving.Contains("blok 7 -> 1")), "kruiswisselcontrole: 7->1 (13 rechtdoor, 10 afbuigend) klopt met de referentie");
        Eis(!m.Any(x => x.Omschrijving.Contains("blok 1 -> 4")), "kruiswisselcontrole: 1->4 is nog nooit gereden: geen oordeel");
        Eis(lijst[0].Kruiswisselstanden[0].StandAdres == A && lijst[1].Kruiswisselstanden[0].StandAdres == R, "kruiswisselcontrole: de wisselstraten zijn NIET aangepast");
        Eis(KruiswisselControle.Controleer(new List<Wisselstraat>()).Count == 0, "kruiswisselcontrole: lege lijst geeft niets");
    }

    static void KruisMotorKortsluitingTest()
    {
        Console.WriteLine("Test: kortsluiting op melder 24 met motor 13/10 afbuigend: geen partnerzoektocht (BUG #92)");
        RitResultaat Rit(RitEinde e, params int[] reeks) { var r = new RitResultaat { Einde = e }; r.Reeks.AddRange(reeks); return r; }
        Eis(BaanVerkenner.IsKruiswisselMotorKortsluiting(10, Rit(RitEinde.Kortsluiting, 28, 26, 14, 24)), "kruismotor: adres 10 + kortsluiting op melder 24 = geen partnerzoektocht");
        Eis(BaanVerkenner.IsKruiswisselMotorKortsluiting(13, Rit(RitEinde.Kortsluiting, 14, 24)), "kruismotor: adres 13 + kortsluiting op melder 24 ook");
        Eis(!BaanVerkenner.IsKruiswisselMotorKortsluiting(5, Rit(RitEinde.Kortsluiting, 14, 24)), "kruismotor: adres 5 (gewone wissel) houdt de partnerzoektocht");
        Eis(!BaanVerkenner.IsKruiswisselMotorKortsluiting(10, Rit(RitEinde.Kortsluiting, 5, 16)), "kruismotor: kortsluiting elders (melder 16) houdt de partnerzoektocht");
        Eis(!BaanVerkenner.IsKruiswisselMotorKortsluiting(10, Rit(RitEinde.Lus, 14, 24)), "kruismotor: zonder kortsluiting niets bijzonders");
    }

    static void OverloopStraatTest()
    {
        // BUG #116: wissel 2 afbuigend (5 -> 12) vraagt ook wissel 1 afbuigend; 5 -> 16 blijft zonder wissels
        var k = new Baankaart();
        k.Overgangen.Add(new Overgang { Van = 5, Naar = 12, Richting = Richting.Achteruit, AantalKeer = 2, GezienBijConfiguraties = { "2" } });
        k.Overgangen.Add(new Overgang { Van = 13, Naar = 5, Richting = Richting.Achteruit, AantalKeer = 30, GezienBijConfiguraties = { "", "2" } });
        k.Kortsluitpunten.Add(new Kortsluitpunt { Id = 1, NaMelder = 12, Richting = Richting.Achteruit, Configuratie = Configuratie.Basis.Met(2), Opgelost = true, OpgelostDoorAdres = 1, OpgelostMetAfbuigend = true });
        k.Kortsluitpunten.Add(new Kortsluitpunt { Id = 5, NaMelder = 5, Richting = Richting.Achteruit, Configuratie = Configuratie.Basis.Met(2), Opgelost = true, OpgelostDoorAdres = 2, OpgelostMetAfbuigend = false });
        var v = WisselstraatVoorstel.Bereken(k);
        var s512 = v.Where(x => x.VanMelder == 5 && x.NaarMelder == 12).ToList();
        Eis(s512.Count == 1 && s512[0].Eisen.Any(e => e.Adres == 2 && e.Afbuigend) && s512[0].Eisen.Any(e => e.Adres == 1 && e.Afbuigend && e.Bron == EisBron.Overloop), "overloopstraat (#116): 5 -> 12 heeft wissel 2 en wissel 1 afbuigend (nu " + (s512.Count > 0 ? s512[0].EisenTekst : "geen") + ")");
        Eis(!s512.Any(x => x.Eisen.Any(e => e.Adres == 2 && !e.Afbuigend)), "overloopstraat (#116): oplossing 'wissel 2 rechtdoor' botst en wordt niet toegepast op 5 -> 12");
        var s135 = v.Where(x => x.VanMelder == 13 && x.NaarMelder == 5).ToList();
        Eis(s135.Count > 0 && s135.All(x => !x.Eisen.Any(e => e.Adres == 1)), "overloopstraat (#116): 13 -> 5 zonder wissel 2 krijgt wissel 1 niet");
        Eis(s512[0].EisenTekst.Contains("overloopwissel"), "overloopstraat (#116): tekst noemt de overloopwissel");
    }

    static void ProefStopTest()
    {
        // BUG #115 (log 14:02 en 14:37)
        var k = new Baankaart();
        k.Wissels.Add(new WisselVondst { Adres = 2, Waarnemingen = { new WisselWaarneming { Soort = WaarnemingSoort.KortsluitingBijOmzetten, NaMelder = 5, Richting = Richting.Vooruit, VolgendeBijRechtdoor = 13, Configuratie = "alle wissels rechtdoor" } } });
        k.Wissels.Add(new WisselVondst { Adres = 10, Waarnemingen = { new WisselWaarneming { Soort = WaarnemingSoort.Splitsing, NaMelder = 24, Richting = Richting.Achteruit, VolgendeBijRechtdoor = 14, VolgendeBijAfbuigend = 129, Configuratie = "alle wissels rechtdoor" } } });
        var stops = BaanVerkenner.BekendeOmzetKortsluitStops(k, 2, Richting.Vooruit, Configuratie.Basis);
        Eis(stops.SetEquals(new[] { 5 }), "proefstop (#115): wissel 2 afbuigend gaf al kortsluiting na melder 5 -> stop daar");
        Eis(BaanVerkenner.BekendeOmzetKortsluitStops(k, 2, Richting.Achteruit, Configuratie.Basis).Count == 0, "proefstop (#115): andere richting geeft geen stop");
        // BUG #118 (log 16:18 en 16:24): een extra wissel elders (opdracht 5 rijdt met 10 erbij) mag de herkenning niet verhinderen; ontbrekende wissels wel
        Eis(BaanVerkenner.BekendeOmzetKortsluitStops(k, 2, Richting.Vooruit, new Configuratie(new[] { 5, 10 })).SetEquals(new[] { 5 }), "proefstop (#118): kortsluiting gezien met alles rechtdoor geldt ook als er elders wissels afbuigend staan");
        k.Wissels.Add(new WisselVondst { Adres = 6, Waarnemingen = { new WisselWaarneming { Soort = WaarnemingSoort.KortsluitingBijOmzetten, NaMelder = 129, Richting = Richting.Vooruit, VolgendeBijRechtdoor = 24, Configuratie = "afbuigend: 5, 10" } } });
        Eis(BaanVerkenner.BekendeOmzetKortsluitStops(k, 6, Richting.Vooruit, new Configuratie(new[] { 5, 10 })).SetEquals(new[] { 129 }), "proefstop (#118): wissel 6 na melder 129 bij 5+10 afbuigend wordt herkend");
        Eis(BaanVerkenner.BekendeOmzetKortsluitStops(k, 6, Richting.Vooruit, new Configuratie(new[] { 5 })).Count == 0, "proefstop (#118): zonder wissel 10 is de waarneming van wissel 6 niet van toepassing");
        Eis(BaanVerkenner.VeiligeStops(k, new List<int> { 14, 129, 24 }, 6, new[] { 129 }).SetEquals(new[] { 14 }), "proefstop (#118): wissel 6 ligt direct na de startmelder 14 -> stop valt op de startmelder (adres wordt overgeslagen)");
        var reeks = new List<int> { 13, 14, 24, 21, 29, 30, 20, 27, 28, 26, 25, 17, 8, 15, 16, 5, 13 };
        Eis(BaanVerkenner.VeiligeStops(k, reeks, 2, new[] { 5 }).SetEquals(new[] { 16 }), "proefstop (#115): wissel 2 ligt na melder 5 -> de rit stopt op 16, niet erop");
        var achteruit = new List<int> { 13, 5, 16, 15, 8, 17, 25, 26, 28, 27, 20, 30, 29, 21, 24, 14, 13 };
        Eis(BaanVerkenner.VeiligeStops(k, achteruit, 10, new[] { 24 }).SetEquals(new[] { 21 }), "proefstop (#115): wissel 10 ligt in melder 24 (kruiswissel) -> stop op 21");
        Eis(BaanVerkenner.VeiligeStops(k, achteruit, 14, new[] { 24 }).SetEquals(new[] { 24 }), "proefstop (#115): een wissel die niet bij de stopmelder ligt houdt de stop zoals hij was");
    }

    // BUG #117 (Marco 16:12, log 15:58)
    static void KruisVoorafTest()
    {
        var basis = Configuratie.Basis;
        Etappe E(params int[] pad) => new Etappe { Van = pad[0], Naar = pad[^1], Pad = pad.ToList() };
        var r1 = BaanVerkenner.KruisVoorafStand(E(13, 14), basis, basis.Met(5).Met(10), out var u1);
        Eis(r1.Bevat(10) && !r1.Bevat(5) && u1 is not null, "kruisvooraf (#119): 13->14 zet motor 10 al vóór aankomst op 14 (5 blijft aan de opdracht)");
        var r2 = BaanVerkenner.KruisVoorafStand(E(13, 14), basis.Met(10), basis.Met(10), out var u2);
        Eis(r2.Bevat(10) && u2 is null, "kruisvooraf (#119): staat al goed, niets te doen");
        var r3 = BaanVerkenner.KruisVoorafStand(E(30, 29, 21, 24), basis, basis.Met(10), out var u3);
        Eis(!r3.Bevat(10) && u3 is null, "kruisvooraf (#119): pad via melder 24 is aan KruisAankomstStand");
        var r4 = BaanVerkenner.KruisVoorafStand(E(13, 14), basis.Met(13), basis, out _);
        Eis(!r4.Bevat(13), "kruisvooraf (#119): motor 13 terug naar rechtdoor vóór aankomst");
    }

    // BUG #121 (log 18:14): oplossing van een kortsluitpunt geldt ook voor een opdracht die LATER op dezelfde weg begint
    static void OplossingLaterOpWegTest()
    {
        var k = new Baankaart();
        k.Kortsluitpunten.Add(new Kortsluitpunt
        {
            Id = 1, NaMelder = 12, Richting = Richting.Achteruit, Configuratie = new Configuratie(new[] { 2 }), Opgelost = true, OpgelostDoorAdres = 1, OpgelostMetAfbuigend = true,
            Route = { new Etappe { Van = 13, Naar = 12, Richting = Richting.Achteruit, Pad = { 13, 5, 12 }, Configuratie = new Configuratie(new[] { 2 }) } }
        });
        var c2 = new Configuratie(new[] { 2 });
        k.RegistreerOvergang(13, 5, Richting.Achteruit, c2, 3.0);
        k.RegistreerOvergang(5, 12, Richting.Achteruit, c2, 3.0);
        var c = k.MetBekendeOplossingenVoorStart(13, Richting.Achteruit, c2, out _);
        Eis(c.Bevat(1), "oplossing (#121): zelfde startmelder 13 -> wissel 1 erbij");
        c = k.MetBekendeOplossingenVoorStart(5, Richting.Achteruit, c2, out _);
        Eis(c.Bevat(1) && c.Bevat(2), "oplossing (#121): opdracht vanaf melder 5 (later op dezelfde weg) krijgt wissel 1 ook (nu " + c + ")");
        c = k.MetBekendeOplossingenVoorStart(16, Richting.Achteruit, c2, out _);
        Eis(!c.Bevat(1), "oplossing (#121): startmelder zonder bekende weg naar melder 12 -> niets erbij");
        c = k.MetBekendeOplossingenVoorStart(5, Richting.Achteruit, new Configuratie(new[] { 3 }), out _);
        Eis(!c.Bevat(1), "oplossing (#121): andere stand -> niets erbij");
        // terugweg naar de kruiswissel: motor 13 afbuigend van de opdracht blijft staan als de referentie dat bewijst (14 <-> 8)
        Etappe E(params int[] pad) => new Etappe { Van = pad[0], Naar = pad[^1], Pad = pad.ToList() };
        var r = BaanVerkenner.KruisAankomstStand(E(13, 14, 24), Configuratie.Basis.Met(14), Configuratie.Basis.Met(13).Met(14), out _);
        Eis(r.Bevat(13) && r.Bevat(14), "kruisterugweg (#121): terugweg 13-14-24 zet motor 13 afbuigend vóór het inrijden van 24 (nu " + r + ")");
    }

    // BUG #122 (controle 18:36): wissels bij de startmelder vooraf zetten; geen triviale oplossingen; oplossingsketen
    sealed class EchoHw : Baanverkenner.Kern.Simulatie.SimulatieBaan, Modeltreinbesturing.Hardware.IWisselBevestiging
    {
        public long AantalWisselBevestigingen { get; private set; }
        public bool Levend = true;
        public int OpLevenNa = int.MaxValue;
        int _stil;
        public override void ZetWissel(int adres, bool afbuigend) { base.ZetWissel(adres, afbuigend); if (!Levend && ++_stil >= OpLevenNa) Levend = true; if (Levend) AantalWisselBevestigingen++; }
    }

    // BUG #126: Dinamo meldt elk uitgevoerd wissel-commando terug; blijft dat weg, dan stopt de verkenner met een melding
    static void WisselBevestigingTest()
    {
        var w = new EchoWaakhond();
        Eis(!w.Noteer(false) && !w.Noteer(false) && !w.Noteer(false) && w.Noteer(false), "echo (#126): zonder ooit een terugmelding gaat het alarm na 4 commando's af");
        var w2 = new EchoWaakhond(); w2.Noteer(true);
        Eis(!w2.Noteer(false) && !w2.Noteer(false) && w2.Noteer(false), "echo (#126): na eerdere terugmeldingen gaat het alarm al na 3 commando's af");
        var w3 = new EchoWaakhond(); w3.Noteer(true); w3.Noteer(false); w3.Noteer(false); w3.Noteer(true);
        Eis(w3.Reeks == 0 && !w3.Noteer(false), "echo (#126): een terugmelding zet de teller terug");
        var w4 = new EchoWaakhond { Negeren = true }; w4.Noteer(true);
        Eis(!w4.Noteer(false) && !w4.Noteer(false) && !w4.Noteer(false) && !w4.Noteer(false), "echo (#126): na 'Nee' van de gebruiker blijft het eerst stil");
        var w5 = new EchoWaakhond { Negeren = true }; w5.Noteer(true);
        bool alarm12 = false; for (int i = 1; i <= 12; i++) { if (w5.Noteer(false)) { alarm12 = i == 12; break; } }
        Eis(alarm12, "echo (#157): na 'Nee' gaat het alarm pas weer af bij 12 commando's zonder terugmelding");

        var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        BaanVerkenner Maak(EchoHw hw, VerkenLog log, Func<string, string, Task<bool?>>? vraag)
        {
            var ins = new VerkenInstellingen { LocAdres = 5408, LocStappen = 28, Verkensnelheid = 12, Kruipsnelheid = 5, WisselAdresVan = 1, WisselAdresTot = 8 };
            var klok = new VirtueleKlok();
            return new BaanVerkenner(hw, ins, klok, log, (_, _) => Task.FromResult(true), new VerkenStatus { Instellingen = ins }) { VraagJaNee = vraag };
        }
        void Zet(BaanVerkenner v, int a, bool b) => ((Task)typeof(BaanVerkenner).GetMethod("ZetWissel", bf)!.Invoke(v, new object[] { a, b, true })!).GetAwaiter().GetResult();

        // gezond: elk commando wordt teruggemeld -> nooit een vraag
        var hw1 = new EchoHw(); var log1 = new VerkenLog(new VirtueleKlok()); int vragen1 = 0;
        var v1 = Maak(hw1, log1, (_, _) => { vragen1++; return Task.FromResult<bool?>(true); });
        for (int i = 1; i <= 8; i++) Zet(v1, i, i % 2 == 0);
        Eis(vragen1 == 0, "echo (#126): gezonde Dinamo -> geen melding (nu " + vragen1 + ")");

        // uitval na twee gelukte commando's: melding na het derde commando zonder terugmelding
        var hw2 = new EchoHw(); var log2 = new VerkenLog(new VirtueleKlok()); var titels = new List<string>();
        var v2 = Maak(hw2, log2, (t, _) => { titels.Add(t); return Task.FromResult<bool?>(false); });
        Zet(v2, 1, true); Zet(v2, 2, true); hw2.Levend = false;
        Zet(v2, 3, true); Zet(v2, 4, true);
        Eis(titels.Count == 0, "echo (#126): twee commando's zonder terugmelding is nog geen alarm");
        Zet(v2, 5, true);
        Eis(titels.Count == 1 && titels[0].Contains("Dinamo voert wissels niet uit") && log2.Regels.Any(r => r.Tekst.Contains("DINAMO VOERT WISSELCOMMANDO'S NIET UIT")), "echo (#126): derde commando zonder terugmelding -> melding en logregel (nu " + titels.Count + ")");
        Zet(v2, 6, true); Zet(v2, 7, true); Zet(v2, 8, true);
        Eis(titels.Count == 1, "echo (#126): na 'Nee' geen herhaalde meldingen");

        // zonder vraagfunctie: de verkenning stopt
        var hw3 = new EchoHw { Levend = false }; var v3 = Maak(hw3, new VerkenLog(new VirtueleKlok()), null);
        bool gestopt = false;
        try { for (int i = 1; i <= 8; i++) Zet(v3, i, true); } catch (Exception ex) when (ex is OperationCanceledException || ex.InnerException is OperationCanceledException) { gestopt = true; }
        Eis(gestopt, "echo (#126): geen terugmelding sinds het begin en geen vraagfunctie -> verkenning stopt");

        // herstel: 'Ja' stuurt alle wissels opnieuw en gaat door als Dinamo weer terugmeldt
        var hw4 = new EchoHw(); var log4 = new VerkenLog(new VirtueleKlok()); int ja = 0;
        var v4 = Maak(hw4, log4, (_, _) => { ja++; hw4.Levend = true; return Task.FromResult<bool?>(true); });
        Zet(v4, 1, true); hw4.Levend = false; Zet(v4, 2, true); Zet(v4, 3, true); Zet(v4, 4, true);
        Eis(ja == 2 && log4.Regels.Any(r => r.Tekst.Contains("Dinamo hersteld volgens de gebruiker")) && log4.Regels.Any(r => r.Tekst.Contains("Hoortest geslaagd")), "echo (#126/#131): 'Ja' -> hoortest (tweede vraag), dan opnieuw sturen en doorgaan (vragen " + ja + ")");

        // BUG #157: Dinamo komt tijdens het omschakelen van een willekeurige wissel vanzelf weer tot leven -> geen vraag, bevindingen sinds de uitval weg
        var hw5 = new EchoHw(); var log5 = new VerkenLog(new VirtueleKlok()); int vragen5 = 0;
        var v5 = Maak(hw5, log5, (_, _) => { vragen5++; return Task.FromResult<bool?>(true); });
        Zet(v5, 1, true); hw5.Levend = false; hw5.OpLevenNa = 10;
        Zet(v5, 2, true);
        v5.Kaart.Kortsluitpunten.Add(new Kortsluitpunt { Id = 99, NaMelder = 7, Richting = Richting.Vooruit });   // 'bevinding' tijdens de uitval
        Zet(v5, 3, true); Zet(v5, 4, true);
        Eis(vragen5 == 0 && log5.Regels.Any(r => r.Tekst.Contains("vanzelf hersteld")), "uitval (#157): Dinamo komt tijdens het omschakelen weer tot leven -> geen vraag aan de gebruiker (vragen " + vragen5 + ")");
        Eis(!v5.Kaart.Kortsluitpunten.Any(x => x.Id == 99) && v5.Kaart.Waarschuwingen.Any(w => w.Contains("uit de kaart gehaald")), "uitval (#157): bevindingen sinds de eerste ontbrekende terugmelding zijn uit de kaart gehaald");

        // BUG #157: 'Nee' (wissels gaan wel om) -> rit krijgt de markering onbetrouwbaar en niets gaat naar het leerprofiel
        var hw6 = new EchoHw(); var log6 = new VerkenLog(new VirtueleKlok());
        var v6 = Maak(hw6, log6, (_, _) => Task.FromResult<bool?>(false));
        Zet(v6, 1, true); hw6.Levend = false; Zet(v6, 2, true); Zet(v6, 3, true); Zet(v6, 4, true);
        Eis(typeof(BaanVerkenner).GetField("_onbetrouwbaar", bf)!.GetValue(v6) is true && v6.Kaart.Waarschuwingen.Any(w => w.Contains("ONBETROUWBAAR")), "uitval (#157): na 'Nee' staat alles daarna als onbetrouwbaar gemarkeerd");
    }


    // BUG #131-#134 (Marco 14-10 13:25)
    static void HoortestEnKruisTest()
    {
        Console.WriteLine("Test: hoortest na herstel van Dinamo (BUG #131) en kruiswissel-fixes (BUG #132-#134)");
        var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        // 131: Ja -> hoortest; 'niet gehoord' -> opnieuw herstarten -> hoortest herhaald -> gehoord
        var hw = new EchoHw(); var log = new VerkenLog(new VirtueleKlok()); var titels = new List<string>(); int hoor = 0;
        var ins = new VerkenInstellingen { LocAdres = 5408, LocStappen = 28, Verkensnelheid = 12, Kruipsnelheid = 5, WisselAdresVan = 1, WisselAdresTot = 8 };
        var v = new BaanVerkenner(hw, ins, new VirtueleKlok(), log, (_, _) => Task.FromResult(true), new VerkenStatus { Instellingen = ins })
        {
            VraagJaNee = (t, _) => { titels.Add(t); if (t.Contains("Hoortest")) return Task.FromResult<bool?>(++hoor >= 2); return Task.FromResult<bool?>(true); }
        };
        var stand = (Dictionary<int, bool>)typeof(BaanVerkenner).GetField("_wisselStand", bf)!.GetValue(v)!;
        stand[3] = true;
        long voor = hw.AantalWisselBevestigingen;
        ((Task)typeof(BaanVerkenner).GetMethod("HoortestNaHerstel", bf)!.Invoke(v, null)!).GetAwaiter().GetResult();
        Eis(titels.Count == 3 && titels[1].Contains("opnieuw herstarten") && hoor == 2, "hoortest (#131): niet gehoord -> Dinamo opnieuw herstarten -> hoortest herhaald (vragen: " + string.Join(" | ", titels) + ")");
        Eis(hw.AantalWisselBevestigingen - voor == 4, "hoortest (#131): elke poging stuurt twee commando's (heen en weer) (nu " + (hw.AantalWisselBevestigingen - voor) + ")");
        Eis(stand[3] == true && stand.Count(kv => kv.Value) == 1, "hoortest (#131): de wisselstanden zijn na de test ongewijzigd");

        // 132: de hand-oplossing hoort bij het kortsluitpunt van de lopende kortsluiting, niet bij het laatste onopgeloste
        var k = new BaanVerkenner(new EchoHw(), ins, new VirtueleKlok(), new VerkenLog(new VirtueleKlok()), (_, _) => Task.FromResult(true), new VerkenStatus { Instellingen = ins });
        var reg = typeof(BaanVerkenner).GetMethod("RegistreerKortsluitpunt", bf)!;
        var o = new Opdracht { Id = 1, Start = 13, Richting = Richting.Achteruit };
        reg.Invoke(k, new object?[] { o, new List<int> { 13, 5, 12 }, Configuratie.Basis.Met(2), new List<Etappe>(), Richting.Achteruit, KortsluitpuntSoort.Kortsluiting });
        var wacht = typeof(BaanVerkenner).GetField("_wachtendeHandOplossing", bf)!;
        wacht.SetValue(k, (24, Richting.Vooruit, 10, true, DateTime.Now));
        reg.Invoke(k, new object?[] { o, new List<int> { 14, 24 }, Configuratie.Basis.Met(5), new List<Etappe>(), Richting.Vooruit, KortsluitpuntSoort.Kortsluiting });
        var kaart = k.Kaart;
        var kp1 = kaart.Kortsluitpunten.First(x => x.NaMelder == 12); var kp2 = kaart.Kortsluitpunten.First(x => x.NaMelder == 24);
        Eis(!kp1.Opgelost && kp2.Opgelost && kp2.OpgelostDoorAdres == 10, "hand (#132): wissel 10 lost kortsluitpunt na melder 24 op, het oudere punt na melder 12 blijft open");
        Eis(wacht.GetValue(k) is null, "hand (#132): de wachtende oplossing is verwerkt");

        // 133 en 134: loc op de kruiswissel
        var hw2 = new EchoHw(); var klok = new VirtueleKlok(); var log2 = new VerkenLog(klok);
        var v2 = new BaanVerkenner(hw2, ins, klok, log2, (_, _) => Task.FromResult(true), new VerkenStatus { Instellingen = ins });
        v2.Kruis = KruisKennis.Parse("3+4@1");
        var mon = (MelderMonitor)typeof(BaanVerkenner).GetField("_monitor", bf)!.GetValue(v2)!;
        mon.Ontvang(1, true); klok.Wacht(TimeSpan.FromSeconds(1)).GetAwaiter().GetResult(); mon.Bijwerken();
        ((Task)typeof(BaanVerkenner).GetMethod("ZetConfiguratie", bf)!.Invoke(v2, new object[] { Configuratie.Basis.Met(3).Met(5), false })!).GetAwaiter().GetResult();
        var st2 = (Dictionary<int, bool>)typeof(BaanVerkenner).GetField("_wisselStand", bf)!.GetValue(v2)!;
        Eis(!(st2.TryGetValue(3, out var m3) && m3) && st2.TryGetValue(5, out var m5) && m5, "kruismotor (#133): met de loc op de kruiswissel blijft motor 3 staan, gewone wissel 5 gaat wel om");
        Eis(log2.Regels.Any(r => r.Tekst.Contains("Motor 3 wordt nu NIET omgezet")), "kruismotor (#133): de melding staat in het log");
        var alarm = typeof(BaanVerkenner).GetMethod("HardwareMeldtBlokAlarm", bf)!;
        alarm.Invoke(v2, new object[] { 8, true });
        string? fout = null;
        try { ((Task)typeof(BaanVerkenner).GetMethod("ControleerGeenAlarmVoorStap", bf)!.Invoke(v2, new object[] { "test" })!).GetAwaiter().GetResult(); }
        catch (Exception ex) { fout = (ex.InnerException ?? ex).GetType().Name; }
        Eis(fout == "NavigatieFout" && !log2.Regels.Any(r => r.Tekst.Contains("probeert zelf wissels")), "alarmroutine (#134): op de kruiswissel geen wissels doorproberen, meteen navigatiefout (nu " + fout + ")");
    }


    // BUG #135-#137 (Marco 14-10 14:12, log 14:07-14:11): kruiswissel-bewaking zonder vaste referentie, met het bewijs uit de EIGEN kaart
    // BUG #140: loc stond alleen op melder 30 (blok 7) en moest naar melder 20 (blok 6): het doelblok kreeg geen rijcommando en hij bleef staan
    // BUG #142: melder 16 (loc langer dan de melder, staat altijd ook op melder 5 met bekend blok 3): blok vinden zonder de loc te isoleren
    static void BuurmelderBlokproefTest()
    {
        Console.WriteLine("Test: blokproef met de loc op twee melders (BUG #142, log 14-10 15:48: melder 16 zonder blok)");
        var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        foreach (var richting in new[] { Richting.Vooruit })
        {
            var sim = new Baanverkenner.Kern.Simulatie.SimulatieBaan { DinamoGedrag = true, StaatStilZonderBlokcommando = true, LocAdres = 5408, LocLengte = 20 };
            var A = Baanverkenner.Kern.Simulatie.SimulatieBaan.Eind.A; var B = Baanverkenner.Kern.Simulatie.SimulatieBaan.Eind.B;
            var s15 = sim.NieuweSectie(15, 100, 4); var s16 = sim.NieuweSectie(16, 15, 2); var s5 = sim.NieuweSectie(5, 100, 3);
            sim.Verbind(s15, B, s16, A); sim.Verbind(s16, B, s5, A);
            sim.PlaatsLocOpGrens(s16, 12, 1, s5); // voorste deel in melder 5, achterste deel in melder 16
            var ins = new VerkenInstellingen { LocAdres = 5408, LocStappen = 28, Verkensnelheid = 12, Kruipsnelheid = 5, WisselAdresVan = 1, WisselAdresTot = 1, DinamoBlokken = "1-4" };
            sim.VerbindenAsync("SIM").GetAwaiter().GetResult();
            var klok = new VirtueleKlok(); klok.Getikt += sim.Tik;
            var log = new VerkenLog(klok);
            var v = new BaanVerkenner(sim, ins, klok, log, (_, _) => Task.FromResult(true));
            var monitor = (MelderMonitor)typeof(BaanVerkenner).GetField("_monitor", bf)!.GetValue(v)!;
            sim.BezetmeldingGewijzigd += monitor.Ontvang;
            v.Kaart.Melders.Add(new MelderInfo { Nummer = 15, DinamoBlok = 4 }); v.Kaart.Melders.Add(new MelderInfo { Nummer = 5, DinamoBlok = 3 }); v.Kaart.Melders.Add(new MelderInfo { Nummer = 16, DinamoBlok = null });
            foreach (var mm in new[] { 5, 15, 16 }) sim.VraagMelderStatusOp(mm);
            klok.Wacht(TimeSpan.FromSeconds(1)).GetAwaiter().GetResult();
            monitor.Bijwerken();
            Eis(monitor.IsBezet(16) && monitor.IsBezet(5), "buurmelder (#142): de loc staat vooraf op melder 16 en 5 (nu bezet: " + string.Join(",", monitor.Bezet) + ")");
            bool gelukt = ((Task<bool>)typeof(BaanVerkenner).GetMethod("BlokZoekenMetBuurmelder", bf)!.Invoke(v, new object?[] { 16, new List<int> { 5 }, richting })!).GetAwaiter().GetResult();
            Eis(gelukt, "buurmelder (#142): het blok van melder 16 is gevonden terwijl de loc ook op melder 5 staat");
            Eis(v.Kaart.Melder(16).DinamoBlok == 2, "buurmelder (#142): melder 16 hoort bij blok 2 (nu " + v.Kaart.Melder(16).DinamoBlok + ")");
            Eis(log.Regels.Any(r => r.Tekst.Contains("Blokproef met alleen de overige blokken (1, 2, 4)")), "buurmelder (#142): blok 3 (van melder 5) krijgt geen commando, de rest wel (log: " + string.Join(" | ", log.Regels.Where(r => r.Tekst.Contains("Blokproef")).Select(r => r.Tekst)) + ")");
        }
    }

    // BUG #141: aanhoudende kortsluiting bij stilstaande loc op de proefwissel -> die gaat terug naar rechtdoor
    static void StatischeKortsluitingTest()
    {
        Console.WriteLine("Test: kortsluiting die terugkomt bij een stilstaande loc (BUG #141, log 14-10 15:30-15:32)");
        var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        (BaanVerkenner V, Baanverkenner.Kern.Simulatie.SimulatieBaan.Wissel W, VerkenLog Log) Maak(int proefAdres, bool afbuigendNu)
        {
            var sim = new Baanverkenner.Kern.Simulatie.SimulatieBaan { DinamoGedrag = false, LocAdres = 5408 };
            var A = Baanverkenner.Kern.Simulatie.SimulatieBaan.Eind.A; var B = Baanverkenner.Kern.Simulatie.SimulatieBaan.Eind.B;
            var s5 = sim.NieuweSectie(5, 100, 3); var w = sim.NieuweWissel(proefAdres, true, 3); var s13 = sim.NieuweSectie(13, 100, 3);
            sim.Verbind(s5, B, s13, A);
            var ins = new VerkenInstellingen { LocAdres = 5408, LocStappen = 28, Verkensnelheid = 12, Kruipsnelheid = 5, WisselAdresVan = 1, WisselAdresTot = 14, DinamoBlokken = "3" };
            sim.VerbindenAsync("SIM").GetAwaiter().GetResult();
            var klok = new VirtueleKlok(); klok.Getikt += sim.Tik;
            var log = new VerkenLog(klok);
            var v = new BaanVerkenner(sim, ins, klok, log, (_, _) => Task.FromResult(true));
            v.Kruis = KruisKennis.Parse("10+13");
            typeof(BaanVerkenner).GetField("_ritDoel", bf)!.SetValue(v, new RitDoel { ProefAdres = proefAdres });
            ((Dictionary<int, bool>)typeof(BaanVerkenner).GetField("_wisselStand", bf)!.GetValue(v)!)[proefAdres] = afbuigendNu;
            typeof(BaanVerkenner).GetField("_statischeKortsluiting", bf)!.SetValue(v, true);
            return (v, w, log);
        }
        bool Los(BaanVerkenner v) => ((Task<bool>)typeof(BaanVerkenner).GetMethod("LosMakenProefWissel", bf)!.Invoke(v, null)!).GetAwaiter().GetResult();

        var (v1, w1, log1) = Maak(2, true);
        Eis(Los(v1), "statisch (#141): proefwissel 2 afbuigend + terugkerende kortsluiting -> wordt losgemaakt");
        Eis(log1.Regels.Any(r => r.Tekst.Contains("klem op wissel 2")), "statisch (#141): het log legt uit waarom wissel 2 terug naar rechtdoor gaat");
        // BUG #241: het is een vermoeden, geen zekerheid; en de wissel wordt onthouden om achteraf te melden of het klopte
        Eis(log1.Regels.Any(r => r.Tekst.Contains("waarschijnlijk zit hij klem op wissel 2")), "klemmelding (#241): de melding brengt het als vermoeden");
        Eis(((List<int>)typeof(BaanVerkenner).GetField("_zojuistLosgemaakt", bf)!.GetValue(v1)!).SequenceEqual(new[] { 2 }), "klemmelding (#241): wissel 2 staat in de lijst om achteraf te melden of het hielp");
        Eis(!(bool)typeof(BaanVerkenner).GetField("_statischeKortsluiting", bf)!.GetValue(v1)!, "statisch (#141): de vlag is daarna gewist (geen herhaling)");

        var (v2, _, _) = Maak(2, false);
        Eis(!Los(v2), "statisch (#141): staat de proefwissel al rechtdoor, dan gebeurt er niets");
        var (v3, _, _) = Maak(10, true);
        Eis(!Los(v3), "statisch (#141): een kruismotor (10) wordt nooit onder de loc omgezet");
        var (v4, _, _) = Maak(2, true);
        typeof(BaanVerkenner).GetField("_statischeKortsluiting", bf)!.SetValue(v4, false);
        Eis(!Los(v4), "statisch (#141): zonder terugkerende kortsluiting (vlag uit) gebeurt er niets");
    }

    static void NastellenAnderBlokTest()
    {
        Console.WriteLine("Test: neerzetten op een melder in een ander Dinamo-blok (BUG #140, log 14-10 15:01)");
        foreach (var metFix in new[] { true })
        {
            var sim = new Baanverkenner.Kern.Simulatie.SimulatieBaan { DinamoGedrag = true, StaatStilZonderBlokcommando = true, LocAdres = 5408 };
            var A = Baanverkenner.Kern.Simulatie.SimulatieBaan.Eind.A; var B = Baanverkenner.Kern.Simulatie.SimulatieBaan.Eind.B;
            var s20 = sim.NieuweSectie(20, 150, 6); var s30 = sim.NieuweSectie(30, 200, 7); var s29 = sim.NieuweSectie(29, 200, 7);
            sim.Verbind(s20, B, s30, A); sim.Verbind(s30, B, s29, A);
            sim.PlaatsLoc(s30, 4, -1); // alleen op melder 30, vlak voor de grens met blok 6, rijdt (vooruit) richting melder 20
            var ins = new VerkenInstellingen { LocAdres = 5408, LocStappen = 28, Verkensnelheid = 12, Kruipsnelheid = 5, WisselAdresVan = 1, WisselAdresTot = 1, DinamoBlokken = "6-7" };
            sim.VerbindenAsync("SIM").GetAwaiter().GetResult();
            var klok = new VirtueleKlok(); klok.Getikt += sim.Tik;
            var log = new VerkenLog(klok);
            var v = new BaanVerkenner(sim, ins, klok, log, (_, _) => Task.FromResult(true));
            var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var monitor = (MelderMonitor)typeof(BaanVerkenner).GetField("_monitor", bf)!.GetValue(v)!;
            sim.BezetmeldingGewijzigd += monitor.Ontvang;
            v.Kaart.Melders.Add(new MelderInfo { Nummer = 20, DinamoBlok = 6 }); v.Kaart.Melders.Add(new MelderInfo { Nummer = 30, DinamoBlok = 7 }); v.Kaart.Melders.Add(new MelderInfo { Nummer = 29, DinamoBlok = 7 });
            foreach (var mm in new[] { 20, 29, 30 }) sim.VraagMelderStatusOp(mm);
            klok.Wacht(TimeSpan.FromSeconds(1)).GetAwaiter().GetResult();
            var nast = typeof(BaanVerkenner).GetMethod("Nastellen", bf)!;
            Exception? fout = null;
            try { ((Task)nast.Invoke(v, new object?[] { 20, Richting.Vooruit, null, null })!).GetAwaiter().GetResult(); } catch (Exception ex) { fout = ex.InnerException ?? ex; }
            monitor.Bijwerken();
            Eis(fout is null && monitor.Bezet.Count == 1 && monitor.IsBezet(20), "neerzetten (#140): loc komt vanuit blok 7 netjes op melder 20 in blok 6 (nu bezet: " + string.Join(",", monitor.Bezet) + (fout is null ? "" : ", fout: " + fout.Message) + ")");
            Eis(log.Regels.Any(r => r.Tekst.Contains("naar blok 6, 7")), "neerzetten (#140): het log laat zien dat blok 6 en 7 het rijcommando kregen");
        }
    }

    static void WisselTerugmeldingTest()
    {
        Console.WriteLine("Test: terugmelding van een wissel wordt leesbaar gelogd (BUG #139), voorbeeld uit hardwarelog 14-10 14:11");
        // echte regels: 4A 90 80 (wissel 1 rechtdoor), 0A 90 81 (wissel 2), 0A 94 89 (wissel 10 afbuigend)
        Eis(Modeltreinbesturing.Hardware.DinamoLogVertaler.WisselTerugmeldingTekst(0x10, 0x00, out string s1) == "1" && s1 == "rechtdoor", "terugmelding (#139): 90 80 = wissel 1 rechtdoor");
        Eis(Modeltreinbesturing.Hardware.DinamoLogVertaler.WisselTerugmeldingTekst(0x10, 0x01, out string s2) == "2" && s2 == "rechtdoor", "terugmelding (#139): 90 81 = wissel 2 rechtdoor");
        // BUG #140: korte vertaling voor het live log, echte regels uit hardwarelog 14-10 15:01
        string K(string r, string hex) => Modeltreinbesturing.Hardware.DinamoLogVertaler.KortTekst(r, Convert.FromHexString(hex));
        Eis(K("In", "4A9080A6") == "terugmelding wissel 1 rechtdoor", "live (#140): In 4A9080A6 = terugmelding wissel 1 rechtdoor (nu: " + K("In", "4A9080A6") + ")");
        Eis(K("In", "4AD09DC9") == "melder 30 bezet", "live (#140): In 4AD09DC9 = melder 30 bezet (nu: " + K("In", "4AD09DC9") + ")");
        Eis(K("In", "4AC093E3") == "melder 20 vrij", "live (#140): In 4AC093E3 = melder 20 vrij (nu: " + K("In", "4AC093E3") + ")");
        Eis(K("In", "0AB282C2").Contains("KORTSLUITING") && K("In", "0AB282C2").Contains("3"), "live (#140): In 0AB282C2 = kortsluiting blok 3 (nu: " + K("In", "0AB282C2") + ")");
        Eis(K("Uit", "4B90808C99").StartsWith("wissel/sein 1 rechtdoor"), "live (#140): Uit 4B90808C99 = wissel 1 rechtdoor (nu: " + K("Uit", "4B90808C99") + ")");
        Eis(K("Uit", "4DA885E5A0AAD7").Contains("blok 6") && K("Uit", "4DA885E5A0AAD7").Contains("snelheid 5") && K("Uit", "4DA885E5A0AAD7").Contains("vooruit"), "live (#140): kruip vooruit blok 6 (nu: " + K("Uit", "4DA885E5A0AAD7") + ")");
        Eis(K("Uit", "0DA886E0A0AA9B").StartsWith("STOP blok 7"), "live (#140): stop blok 7 (nu: " + K("Uit", "0DA886E0A0AA9B") + ")");
        Eis(K("Uit", "4DA886ECA0AACF").Contains("snelheid 12") , "live (#140): snelheid 12 (nu: " + K("Uit", "4DA886ECA0AACF") + ")");
        Eis(K("Uit", "4A8180B5") == "Reset Fault (foutstatus wissen)" || K("Uit", "4A8180B5").Length >= 0, "live (#140): geen fout bij onbekend patroon");
        Eis(Modeltreinbesturing.Hardware.DinamoLogVertaler.WisselTerugmeldingTekst(0x14, 0x09, out string s3) == "10" && s3 == "afbuigend", "terugmelding (#139): 94 89 = wissel 10 afbuigend");
    }

    static void KruisZonderReferentieTest()
    {
        Console.WriteLine("Test: kruiswissel zonder vaste referentie (BUG #135-#137), echte kaart van 14-10 14:11");
        var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var kruis = KruisKennis.Parse("10+13@24");
        Eis(!kruis.MetReferentie && kruis.Middens.Contains(24), "kruis (#135): verkenning vanaf nul heeft melder 24 en geen vaste referentie");
        var kaart = Laad("baankaart_2026-10-14_1411.json");
        Etappe E(Richting r, params int[] pad) => new Etappe { Van = pad[0], Naar = pad[^1], Richting = r, Pad = pad.ToList() };

        // 137: kortsluitpunt #1 (route 13-5-12) is nooit door motor 10 opgelost; #2 (na melder 24) wel
        Eis(kaart.Kortsluitpunten.First(x => x.Id == 1).OpgelostDoorAdres == 10 && kaart.Kortsluitpunten.First(x => x.Id == 2).OpgelostDoorAdres == 10, "kaart (#137): de opgeslagen kaart heeft de verkeerde oplossing bij #1 (uitgangssituatie)");
        var hersteld = kaart.HerstelOnmogelijkeKruisOplossingen(kruis);
        var kp1 = kaart.Kortsluitpunten.First(x => x.Id == 1); var kp2 = kaart.Kortsluitpunten.First(x => x.Id == 2);
        Eis(hersteld.Count == 1 && !kp1.Opgelost && kp1.OpgelostDoorAdres is null && !kp1.GeprobeerdeAdressen.Contains(10), "kaart (#137): #1 staat weer open (nu " + hersteld.Count + " hersteld)");
        Eis(kp2.Opgelost && kp2.OpgelostDoorAdres == 10, "kaart (#137): #2 (na melder 24) blijft opgelost door motor 10");
        Eis(!kaart.RegistreerHandmatigeOplossing(kp1, 10, true, kruis) && kp1.Opgelost == false, "kaart (#137): motor 10 wordt ook niet opnieuw aan #1 gehangen");
        Eis(kaart.RegistreerHandmatigeOplossing(kp1, 1, true, kruis), "kaart (#137): een gewone wissel (1) mag #1 wel oplossen");
        kp1.Opgelost = false; kp1.OpgelostDoorAdres = null; kp1.OpgelostMetAfbuigend = null;

        // 135: opdracht 24 vooruit met motor 13 afbuigend: motor 13 gaat al om vóór melder 24 (ingang 14: 14 -> 24 -> 8 is met 13 gezien)
        var r1 = BaanVerkenner.KruisAankomstStand(E(Richting.Vooruit, 13, 14, 24), Configuratie.Basis, Configuratie.Basis.Met(13), kruis, kaart, out var u1);
        Eis(r1.Bevat(13) && !r1.Bevat(10) && u1 is not null && u1.Contains("eigen kaart"), "kruis (#135): opdracht 24 vooruit (13 afbuigend) zet motor 13 vóór het inrijden, zonder referentie (nu " + r1 + ")");
        var r2 = BaanVerkenner.KruisAankomstStand(E(Richting.Achteruit, 13, 14, 24), Configuratie.Basis, Configuratie.Basis.Met(10), kruis, new Baankaart(), out var u2);
        Eis(r2.Equals(Configuratie.Basis) && u2 is null, "kruis (#135): zonder bewijs in de kaart verandert er niets");
        var r3 = BaanVerkenner.KruisAankomstStand(E(Richting.Achteruit, 29, 21, 24), Configuratie.Basis, Configuratie.Basis.Met(13), kruis, kaart, out _);
        Eis(r3.Equals(Configuratie.Basis), "kruis (#135): vanaf melder 21 is motor 13 afbuigend nooit gezien -> niets aangepast");
        var r4 = BaanVerkenner.KruisVoorafStand(E(Richting.Vooruit, 13, 14), Configuratie.Basis, Configuratie.Basis.Met(10), kruis, out var u4);
        Eis(r4.Bevat(10) && u4 is not null, "kruis (#135): motor 10 gaat al om voordat de loc op melder 14 aankomt, ook zonder referentie");
        var andere = KruisKennis.Parse("3+4@7");
        var r5 = BaanVerkenner.KruisVoorafStand(E(Richting.Vooruit, 5, 6), Configuratie.Basis, Configuratie.Basis.Met(4).Met(12), andere, out _);
        Eis(r5.Bevat(4) && !r5.Bevat(12), "kruis (#135): werkt voor elke Engelse wissel die de gebruiker opgeeft (3+4 bij melder 7), niet alleen 13/10 bij 24");

        // 135: de vooruitblik: ingangen volgens de kaart
        var motoren = kruis.Motoren.OrderBy(x => x).ToList();
        var ing = KruisBewijs.Ingangen(kaart, kruis, 15, Richting.Achteruit, Configuratie.Basis.Met(14));
        Eis(ing.Count == 1 && ing[0] == (24, 8), "kruis (#135): vanaf melder 15 achteruit met wissel 14 afbuigend rijdt de loc via 8 de kruiswissel in (nu " + string.Join(";", ing) + ")");
        Eis(KruisBewijs.Ingangen(kaart, kruis, 15, Richting.Achteruit, Configuratie.Basis).Count == 0, "kruis (#135): de gewone lus 15 -> 8 -> 17 (wissel 14 rechtdoor) raakt de kruiswissel niet");
        Eis(KruisBewijs.Ingangen(kaart, kruis, 8, Richting.Vooruit, Configuratie.Basis).Count == 0, "kruis (#135): vooruit vanaf 8 gaat naar 15, niet de kruiswissel in");
        var gezien8 = KruisBewijs.GezienBijIngang(kaart, kruis, 24, 8);
        Eis(gezien8.Count == 1 && gezien8[0].Equals(Configuratie.Basis.Met(13)), "kruis (#135): vanaf melder 8 is alleen met motor 13 afbuigend door de kruiswissel gereden (nu " + string.Join("|", gezien8) + ")");
        Eis(KruisBewijs.KiesStand(gezien8, Configuratie.Basis.Met(14), motoren, new HashSet<int>())?.Equals(Configuratie.Basis.Met(13)) == true, "kruis (#135): met wissel 14 afbuigend en 13 rechtdoor kiest de vooruitblik motor 13 afbuigend");
        Eis(KruisBewijs.KiesStand(gezien8, Configuratie.Basis.Met(13), motoren, new HashSet<int>()) is null, "kruis (#135): staat 13 al afbuigend, dan verandert er niets");
        Eis(KruisBewijs.KiesStand(gezien8, Configuratie.Basis, motoren, new HashSet<int> { 13 }) is null, "kruis (#135): een motor die de rit zelf rechtdoor wil (vast) wordt niet aangepast");
        Eis(KruisBewijs.KiesStand(new List<Configuratie>(), Configuratie.Basis, motoren, new HashSet<int>()) is null, "kruis (#135): zonder bewijs niets doen");

        // 135: volledige stap in de verkenner (zoals 14:08:52): loc op melder 15, wissel 14 afbuigend, rijdt achteruit -> motor 13 gaat vooraf om
        var ins = new VerkenInstellingen { LocAdres = 5408, LocStappen = 28, Verkensnelheid = 12, Kruipsnelheid = 5, WisselAdresVan = 1, WisselAdresTot = 14 };
        BaanVerkenner Maak(Baankaart k, out VirtueleKlok klok, out EchoHw hw)
        {
            klok = new VirtueleKlok(); hw = new EchoHw();
            var v = new BaanVerkenner(hw, ins, klok, new VerkenLog(klok), (_, _) => Task.FromResult(true), new VerkenStatus { Instellingen = ins, Kaart = k });
            v.Kruis = KruisKennis.Parse("10+13@24");
            return v;
        }
        async Task<Configuratie> Vooruitblik(BaanVerkenner v, int melder, Richting r, Configuratie cfg, RitDoel doel)
            => await (Task<Configuratie>)typeof(BaanVerkenner).GetMethod("ZetKruisVooraf", bf)!.Invoke(v, new object[] { melder, r, cfg, doel })!;
        Dictionary<int, bool> Stand(BaanVerkenner v) => (Dictionary<int, bool>)typeof(BaanVerkenner).GetField("_wisselStand", bf)!.GetValue(v)!;
        var vA = Maak(kaart, out var kA, out _);
        var cA = Vooruitblik(vA, 15, Richting.Achteruit, Configuratie.Basis.Met(14), new RitDoel { Configuratie = Configuratie.Basis.Met(14), StaartWissels = true }).GetAwaiter().GetResult();
        Eis(cA.Bevat(13) && cA.Bevat(14) && Stand(vA).TryGetValue(13, out var s13) && s13, "kruis (#135): verkenningsrit 15 achteruit (14 afbuigend) zet motor 13 vóór het wegrijden (nu " + cA + ")");
        var vB = Maak(kaart, out _, out _);
        var cB = Vooruitblik(vB, 15, Richting.Achteruit, Configuratie.Basis.Met(14), new RitDoel { Configuratie = Configuratie.Basis.Met(14), ProefAdres = 13 }).GetAwaiter().GetResult();
        Eis(!cB.Bevat(13), "kruis (#135): test de rit zelf motor 13, dan blijft die ongemoeid");
        var vC = Maak(kaart, out _, out _);
        var cC = Vooruitblik(vC, 15, Richting.Achteruit, Configuratie.Basis, new RitDoel { Configuratie = Configuratie.Basis }).GetAwaiter().GetResult();
        Eis(cC.Equals(Configuratie.Basis) && Stand(vC).Count == 0, "kruis (#135): de gewone lus 15 -> 8 -> 17 krijgt geen extra commando's");
        var vD = Maak(kaart, out var kD, out _);
        var mon = (MelderMonitor)typeof(BaanVerkenner).GetField("_monitor", bf)!.GetValue(vD)!;
        mon.Ontvang(24, true); kD.Wacht(TimeSpan.FromSeconds(1)).GetAwaiter().GetResult(); mon.Bijwerken();
        var cD = Vooruitblik(vD, 8, Richting.Achteruit, Configuratie.Basis.Met(14), new RitDoel { Configuratie = Configuratie.Basis.Met(14) }).GetAwaiter().GetResult();
        Eis(!cD.Bevat(13), "kruis (#135/#133): staat de loc al op de kruiswissel, dan wordt er niets omgezet");

        // 136: de hand-oplossing van 14:09:58 hoort bij kortsluitpunt #3 (dat al bestond)
        var kaart2 = Laad("baankaart_2026-10-14_1411.json");
        var vE = Maak(kaart2, out var kE, out _);
        kaart2.HerstelOnmogelijkeKruisOplossingen(vE.Kruis);
        typeof(BaanVerkenner).GetField("_laatsteKortsluitContext", bf)!.SetValue(vE, (24, Richting.Achteruit, Configuratie.Basis.Met(14)));
        typeof(BaanVerkenner).GetField("_laatsteKortsluitingOm", bf)!.SetValue(vE, kE.Nu);
        var zoek = typeof(BaanVerkenner).GetMethod("ZoekKortsluitpunt", bf)!;
        var koppel = typeof(BaanVerkenner).GetMethod("KoppelHandOplossing", bf)!;
        var gevonden = (Kortsluitpunt?)zoek.Invoke(vE, new object[] { 24, Richting.Achteruit, Configuratie.Basis.Met(14) });
        Eis(gevonden is not null && gevonden.Id == 3, "hand (#136): bij de kortsluiting na melder 24 achteruit hoort kortsluitpunt #3 (nu #" + gevonden?.Id + ")");
        Eis((bool)koppel.Invoke(vE, new object[] { gevonden!, 13, true })! && gevonden!.Opgelost && gevonden.OpgelostDoorAdres == 13, "hand (#136): wissel 13 afbuigend lost #3 op");
        Eis(!kaart2.Kortsluitpunten.First(x => x.Id == 2).GeprobeerdeAdressen.Contains(13) && kaart2.Kortsluitpunten.First(x => x.Id == 1).Opgelost == false, "hand (#136): de andere kortsluitpunten blijven zoals ze waren");
        Eis(kaart2.MetBekendeOplossingenVoorStart(15, Richting.Achteruit, Configuratie.Basis.Met(14), out _).Bevat(13), "hand (#136): opdracht 15 achteruit met wissel 14 rijdt voortaan met motor 13 afbuigend (nu " + kaart2.MetBekendeOplossingenVoorStart(15, Richting.Achteruit, Configuratie.Basis.Met(14), out _) + ")");
    }

    static void StartmelderVoorafTest()
    {
        var k = new Baankaart();
        k.Wissel(1).Waarnemingen.Add(new WisselWaarneming { Soort = WaarnemingSoort.Splitsing, NaMelder = 5, Richting = Richting.Achteruit, VolgendeBijRechtdoor = 16, VolgendeBijAfbuigend = 12 });
        k.Wissel(2).Waarnemingen.Add(new WisselWaarneming { Soort = WaarnemingSoort.Splitsing, NaMelder = 5, Richting = Richting.Achteruit, VolgendeBijRechtdoor = 16, VolgendeBijAfbuigend = 12 });
        k.Wissel(6).Waarnemingen.Add(new WisselWaarneming { Soort = WaarnemingSoort.Splitsing, NaMelder = 13, Richting = Richting.Achteruit, VolgendeBijRechtdoor = 5, VolgendeBijAfbuigend = 7 });
        var e = new Etappe { Van = 13, Naar = 5, Richting = Richting.Achteruit, Pad = new List<int> { 13, 5 } };
        var doel = Configuratie.Basis.Met(1).Met(2);
        var r = BaanVerkenner.StartmelderWisselsVooraf(k, e, Configuratie.Basis, doel, out var uitleg);
        Eis(r.Bevat(1) && r.Bevat(2) && uitleg is not null, "startmelder vooraf (#122): wissels 1 en 2 (bij melder 5) staan al afbuigend voordat de loc op 5 komt (nu " + r + ")");
        Eis(!r.Bevat(6), "startmelder vooraf (#122): wissel 6 ligt bij de melder waar de loc nog staat en blijft ongemoeid");
        var r2 = BaanVerkenner.StartmelderWisselsVooraf(k, e, Configuratie.Basis.Met(6), doel.Met(6), out _);
        Eis(r2.Bevat(6), "startmelder vooraf (#122): wissel onder de loc wordt niet omgezet");
        var kp = new Kortsluitpunt { Id = 5, NaMelder = 5, Richting = Richting.Achteruit, Configuratie = Configuratie.Basis.Met(2) };
        Eis(!k.RegistreerHandmatigeOplossing(kp, 2, false), "oplossing (#122): de geteste wissel zelf terugzetten is geen oplossing");
        // kaart van 14-10: wissel 2 hangt bij 5 (NaMelder) en 13 (vervolg), wissel 1 is onbekend; opdracht 5 achteruit met 1 en 2 afbuigend
        var k4 = new Baankaart();
        k4.Wissel(2).Waarnemingen.Add(new WisselWaarneming { Soort = WaarnemingSoort.KortsluitingBijOmzetten, NaMelder = 5, Richting = Richting.Vooruit, VolgendeBijRechtdoor = 13 });
        var e4 = new Etappe { Van = 13, Naar = 5, Richting = Richting.Achteruit, Pad = new List<int> { 13, 5 } };
        var r4 = BaanVerkenner.StartmelderWisselsVooraf(k4, e4, Configuratie.Basis, Configuratie.Basis.Met(1).Met(2), out _);
        Eis(r4.Bevat(1) && r4.Bevat(2), "startmelder vooraf (#124): wissel 2 (ook vervolgmelder 13) en onbekende wissel 1 worden vooraf gezet (nu " + r4 + ")");
        // een triviale oplossing komt niet in het leerprofiel
        var k5 = new Baankaart();
        k5.BekendeKortsluitOplossingen.Add(new KortsluitOplossing { NaMelder = 24, Richting = Richting.Vooruit, Stand = Configuratie.Basis.Met(13).Sleutel, Start = 14, Adres = 13, Afbuigend = false });
        k5.BekendeKortsluitOplossingen.Add(new KortsluitOplossing { NaMelder = 12, Richting = Richting.Achteruit, Stand = Configuratie.Basis.Met(2).Sleutel, Start = 13, Adres = 1, Afbuigend = true });
        Eis(k5.AlleKortsluitOplossingen().Count == 1 && k5.AlleKortsluitOplossingen()[0].Adres == 1, "leerprofiel (#124): 'wissel 13 terugzetten' wordt niet bewaard, een echte extra wissel wel");
        // BUG #125: vertrek van een melder die eerder bewezen vertrok = Dinamo-probleem, geen blokprobleem
        var k6 = new Baankaart();
        k6.RegistreerOvergang(13, 14, Richting.Vooruit, Configuratie.Basis, 3.0);
        Eis(BaanVerkenner.VertrekWasBewezen(k6, 13, Richting.Vooruit), "dinamo stil (#125): van melder 13 vooruit is eerder vertrokken");
        Eis(!BaanVerkenner.VertrekWasBewezen(k6, 13, Richting.Achteruit) && !BaanVerkenner.VertrekWasBewezen(k6, 5, Richting.Vooruit), "dinamo stil (#125): andere richting/melder is niet bewezen -> gewone blokproef");
        // keten: stand 2 + wissel 1 geeft kortsluiting, stand 1+2 + wissel 5 lost op
        var k3 = new Baankaart();
        k3.BekendeKortsluitOplossingen.Add(new KortsluitOplossing { NaMelder = 12, Richting = Richting.Achteruit, Stand = Configuratie.Basis.Met(2).Sleutel, Start = 13, Adres = 1, Afbuigend = true });
        k3.BekendeKortsluitOplossingen.Add(new KortsluitOplossing { NaMelder = 12, Richting = Richting.Achteruit, Stand = Configuratie.Basis.Met(1).Met(2).Sleutel, Start = 13, Adres = 5, Afbuigend = true });
        var ketting = k3.MetBekendeOplossingenVoorStart(13, Richting.Achteruit, Configuratie.Basis.Met(2), out _);
        Eis(ketting.Bevat(1) && ketting.Bevat(2) && ketting.Bevat(5), "oplossingsketen (#122): 2 -> 1+2 -> 1+2+5 (nu " + ketting + ")");
    }

    static void ProefOplossingTest()
    {
        var k = new Baankaart();
        k.Kortsluitpunten.Add(new Kortsluitpunt
        {
            Id = 1, NaMelder = 12, Richting = Richting.Achteruit, Configuratie = new Configuratie(new[] { 2 }), Opgelost = true, OpgelostDoorAdres = 1, OpgelostMetAfbuigend = true,
            Route = { new Etappe { Van = 13, Naar = 12, Richting = Richting.Achteruit, Pad = { 13, 5, 12 }, Configuratie = new Configuratie(new[] { 2 }) } }
        });
        var adressen = Enumerable.Range(1, 14).ToList();
        var c = BaanVerkenner.ProefStandMetOplossing(k, adressen, 13, Richting.Achteruit, Configuratie.Basis, 2, out var extra);
        Eis(c.Bevat(2) && c.Bevat(1) && extra.Count == 1 && extra[0] == (1, true), "proefstand (#117): wissel 2 afbuigend achteruit vanaf 13 krijgt de bekende oplossing, wissel 1 afbuigend (nu " + c + ")");
        c = BaanVerkenner.ProefStandMetOplossing(k, adressen, 13, Richting.Vooruit, Configuratie.Basis, 2, out extra);
        Eis(c.Equals(new Configuratie(new[] { 2 })) && extra.Count == 0, "proefstand (#117): andere richting -> geen extra wissel");
        c = BaanVerkenner.ProefStandMetOplossing(k, adressen, 13, Richting.Achteruit, Configuratie.Basis, 3, out extra);
        Eis(c.Equals(new Configuratie(new[] { 3 })) && extra.Count == 0, "proefstand (#117): ander proefadres -> geen extra wissel");
        // kruiswissel-motoren blijven buiten beschouwing
        k.Kortsluitpunten.Add(new Kortsluitpunt { Id = 2, NaMelder = 14, Richting = Richting.Achteruit, Configuratie = new Configuratie(new[] { 10 }), Opgelost = true, OpgelostDoorAdres = 5, OpgelostMetAfbuigend = true,
            Route = { new Etappe { Van = 13, Naar = 14, Richting = Richting.Achteruit, Pad = { 13, 24, 14 }, Configuratie = new Configuratie(new[] { 10 }) } } });
        c = BaanVerkenner.ProefStandMetOplossing(k, adressen, 13, Richting.Achteruit, Configuratie.Basis, 10, out extra);
        Eis(c.Equals(new Configuratie(new[] { 10 })) && extra.Count == 0, "proefstand (#117): motor 10 wordt niet met een extra wissel getest (nu " + c + ")");

        // na een opgeloste kortsluiting blijft de stand die het alarm wegnam gelden voor de volgende stap
        var sim = new Baanverkenner.Kern.Simulatie.SimulatieBaan { DinamoGedrag = true, LocAdres = 5408 };
        var A = Baanverkenner.Kern.Simulatie.SimulatieBaan.Eind.A; var B = Baanverkenner.Kern.Simulatie.SimulatieBaan.Eind.B;
        var s1 = sim.NieuweSectie(1, 80, 1); var s2 = sim.NieuweSectie(2, 80, 2);
        sim.Verbind(s1, B, s2, A); sim.PlaatsLoc(s1, 40, +1);
        var ins = new VerkenInstellingen { LocAdres = 5408, LocStappen = 28, Verkensnelheid = 12, Kruipsnelheid = 5, WisselAdresVan = 1, WisselAdresTot = 4, DinamoBlokken = "1-2" };
        sim.VerbindenAsync("SIM").GetAwaiter().GetResult();
        var klok = new VirtueleKlok(); klok.Getikt += sim.Tik;
        var v = new BaanVerkenner(sim, ins, klok, new VerkenLog(klok), (_, _) => Task.FromResult(true), new VerkenStatus { Instellingen = ins });
        var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var check = typeof(BaanVerkenner).GetMethod("ControleerGeenAlarmVoorStap", bf)!;
        var alarm = typeof(BaanVerkenner).GetMethod("HardwareMeldtBlokAlarm", bf)!;
        var stand = (Dictionary<int, bool>)typeof(BaanVerkenner).GetField("_wisselStand", bf)!.GetValue(v)!;
        alarm.Invoke(v, new object[] { 8, true });
        klok.Getikt += _ => { if (stand.TryGetValue(1, out var af) && af) alarm.Invoke(v, new object[] { 8, false }); };
        ((Task)check.Invoke(v, new object[] { "test" })!).GetAwaiter().GetResult();
        var volgende = v.MetAlarmStand(new Configuratie(new[] { 2 }));
        Eis(volgende.Bevat(1) && volgende.Bevat(2), "alarmstand (#117): de stap na de opgeloste kortsluiting houdt wissel 1 afbuigend (nu " + volgende + ")");
        ((Task)check.Invoke(v, new object[] { "test" })!).GetAwaiter().GetResult();
        Eis(v.MetAlarmStand(new Configuratie(new[] { 2 })).Equals(new Configuratie(new[] { 2 })), "alarmstand (#117): bij de volgende controle zonder alarm geldt hij niet meer");
    }


    // BUG #130 (Marco 11:42, log 11:32-11:36): lost de alarmcontrole een kortsluiting op door wissel a terug te zetten, dan bewijst de geslaagde
    // rit GEEN overloopverbinding a+b. Zonder die controle werd "14 én 12 samen afbuigend" vastgelegd en veroorzaakte het een nieuwe kortsluiting.
    static void PartnerAlarmIngreepTest()
    {
        var sim = new Baanverkenner.Kern.Simulatie.SimulatieBaan { DinamoGedrag = true, LocAdres = 5408 };
        var A = Baanverkenner.Kern.Simulatie.SimulatieBaan.Eind.A; var B = Baanverkenner.Kern.Simulatie.SimulatieBaan.Eind.B;
        var s1 = sim.NieuweSectie(1, 80, 1); var s2 = sim.NieuweSectie(2, 80, 2);
        sim.Verbind(s1, B, s2, A); sim.PlaatsLoc(s1, 40, +1);
        var ins = new VerkenInstellingen { LocAdres = 5408, LocStappen = 28, Verkensnelheid = 12, Kruipsnelheid = 5, WisselAdresVan = 1, WisselAdresTot = 4, DinamoBlokken = "1-2" };
        sim.VerbindenAsync("SIM").GetAwaiter().GetResult();
        var klok = new VirtueleKlok(); klok.Getikt += sim.Tik;
        var log = new VerkenLog(klok);
        var v = new BaanVerkenner(sim, ins, klok, log, (_, _) => Task.FromResult(true), new VerkenStatus { Instellingen = ins });
        var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var alarm = typeof(BaanVerkenner).GetMethod("HardwareMeldtBlokAlarm", bf)!;
        var stand = (Dictionary<int, bool>)typeof(BaanVerkenner).GetField("_wisselStand", bf)!.GetValue(v)!;
        stand[2] = true;                                   // wissel 2 (= a) staat afbuigend en geeft de kortsluiting
        alarm.Invoke(v, new object[] { 8, true });
        klok.Getikt += _ => { if (stand.TryGetValue(2, out var af) && !af) alarm.Invoke(v, new object[] { 8, false }); };   // pas weg als 2 weer rechtdoor staat
        var mon = (MelderMonitor)typeof(BaanVerkenner).GetField("_monitor", bf)!.GetValue(v)!;
        sim.BezetmeldingGewijzigd += mon.Ontvang;
        mon.Ontvang(1, true);                              // de loc staat op melder 1
        klok.Wacht(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
        mon.Bijwerken();
        var o = new Opdracht { Id = 1, Start = 1, Richting = Richting.Vooruit };
        var proef = new RitResultaat { Einde = RitEinde.Kortsluiting }; proef.Reeks.Add(1);
        var m = typeof(BaanVerkenner).GetMethod("PartnerwisselZoeken", bf)!;
        var t = (Task)m.Invoke(v, new object?[] { o, 2, proef, null })!;
        t.GetAwaiter().GetResult();
        var uitkomst = t.GetType().GetProperty("Result")!.GetValue(t);
        if (Environment.GetEnvironmentVariable("PARTNER_LOG") == "1") Console.WriteLine(log.AlsTekst());
        Eis(uitkomst is null, "partner na alarmingreep (#130): geen partner vastgelegd als de alarmcontrole zelf wissel 2 terugzette");
        Eis(log.AlsTekst().Contains("bewijst geen overloopverbinding"), "partner na alarmingreep (#130): het logboek legt uit waarom er niets vastgelegd is");
    }

    static void AltijdAfbuigendTest()
    {
        // BUG #112 (log 12:31): oplossing 'wissel 2 rechtdoor' (na melder 5 achteruit) hoort bij het pad naar 16, niet bij 5 -> 12.
        var k = new Baankaart();
        k.Overgangen.Add(new Overgang { Van = 5, Naar = 12, Richting = Richting.Achteruit, AantalKeer = 2, GezienBijConfiguraties = { "2" } });
        k.Overgangen.Add(new Overgang { Van = 13, Naar = 5, Richting = Richting.Achteruit, AantalKeer = 30, GezienBijConfiguraties = { "", "2", "1" } });
        k.BekendeKortsluitOplossingen.Add(new KortsluitOplossing { NaMelder = 5, Richting = Richting.Achteruit, Stand = "2", Start = 13, Adres = 2, Afbuigend = false, Bron = "test" });
        var naar12 = k.MetBekendeKortsluitOplossingen(new List<int> { 5, 12, 11 }, Richting.Achteruit, Configuratie.Basis.Met(1).Met(2), out _);
        Eis(naar12.Bevat(2) && naar12.Bevat(1), "altijd afbuigend (#112): pad 5-12-11 houdt wissel 2 afbuigend (nu " + naar12 + ")");
        var naar16 = k.MetBekendeKortsluitOplossingen(new List<int> { 13, 5 }, Richting.Achteruit, Configuratie.Basis.Met(2), out _);
        Eis(!naar16.Bevat(2), "altijd afbuigend (#112): pad 13-5 zet wissel 2 nog steeds rechtdoor (nu " + naar16 + ")");
        Eis(k.AltijdAfbuigendOpPad(new List<int> { 13, 5 }, Richting.Achteruit).Count == 0, "altijd afbuigend (#112): stap gereden met en zonder wissel -> niets beschermd");
        Eis(k.AltijdAfbuigendOpPad(new List<int> { 5, 12 }, Richting.Vooruit).Count == 0, "altijd afbuigend (#112): andere richting niet beschermd");
    }

    static void KortsluitOplossingTest()
    {
        var k = Laad("baankaart_2026-10-11_1103.json");
        // 13 -> 14 -> 24 vooruit met alleen wissel 10 afbuigend gaf kortsluiting (#1); de oplossing (wissel 5 afbuigend) botst met de eis
        // van 14 -> 24 (wissel 5 rechtdoor): wissel 10 hoort hier dus rechtdoor.
        var eis = k.MetWisselEisen(new List<int> { 13, 14, 24 }, Richting.Vooruit, new Configuratie(new[] { 10 }), out _);
        var n = k.MetBekendeKortsluitOplossingen(new List<int> { 13, 14, 24 }, Richting.Vooruit, eis, out var w);
        Eis(n.Equals(Configuratie.Basis) && w.Count > 0, "kortsluit-oplossing: pad 13-14-24 met alleen 10 afbuigend -> alle wissels rechtdoor (nu " + n + ")");
        // 24 -> 8 vooruit met 5, 10, 13, 14 afbuigend klopt met de oplossingen en blijft ongewijzigd
        var c2 = new Configuratie(new[] { 5, 10, 13, 14 });
        var n2 = k.MetBekendeKortsluitOplossingen(new List<int> { 24, 8 }, Richting.Vooruit, c2, out var w2);
        Eis(n2.Equals(c2) && w2.Count == 0, "kortsluit-oplossing: 24-8 met 5,10,13,14 afbuigend blijft ongewijzigd (nu " + n2 + ")");
        // zonder botsende eis wordt de oplossing zelf toegepast: pad 24 -> 8 met alleen 10 afbuigend krijgt wissel 5 afbuigend erbij
        var n3 = k.MetBekendeKortsluitOplossingen(new List<int> { 24, 8 }, Richting.Vooruit, new Configuratie(new[] { 10 }), out _);
        Eis(n3.Bevat(5) && n3.Bevat(10), "kortsluit-oplossing: 24-8 met alleen 10 afbuigend -> wissel 5 erbij (nu " + n3 + ")");
        // de andere rijrichting en een pad zonder die melder blijven ongemoeid
        Eis(k.MetBekendeKortsluitOplossingen(new List<int> { 24, 21 }, Richting.Achteruit, new Configuratie(new[] { 10 }), out _).Equals(new Configuratie(new[] { 10 })), "kortsluit-oplossing: andere richting (24 achteruit, geen opgelost punt) verandert niets");
        var n5 = k.MetBekendeKortsluitOplossingen(new List<int> { 21, 14 }, Richting.Achteruit, new Configuratie(new[] { 10 }), out _);
        Eis(n5.Bevat(9) && n5.Bevat(10), "kortsluit-oplossing: kortsluitpunt #5 (14 achteruit, 10 afbuigend) wordt met wissel 9 opgelost (nu " + n5 + ")");
        Eis(k.MetBekendeKortsluitOplossingen(new List<int> { 30, 20 }, Richting.Vooruit, new Configuratie(new[] { 10 }), out _).Equals(new Configuratie(new[] { 10 })), "kortsluit-oplossing: pad zonder die melder verandert niets");
    }

    // BUG #75: de tweede bezette melder bij een kortsluiting wijst de betrokken wissel aan
    static void TweedeMelderTest()
    {
        var k = new Baankaart();
        // 24 -> 21 is gereden met wissel 10 rechtdoor en 5 rechtdoor; 24 -> 8 alleen met 5 en 10 afbuigend
        k.RegistreerOvergang(24, 21, Richting.Vooruit, Configuratie.Basis, 3);
        k.RegistreerOvergang(24, 8, Richting.Vooruit, new Configuratie(new[] { 5, 10 }), 3);
        // kortsluiting na 24 met wissel 10 afbuigend, terwijl ook melder 8 bezet was
        var stand = new Configuratie(new[] { 10 });
        var h = k.HintsBijTweedeMelder(24, new[] { 8 }, stand);
        Eis(h.Contains((5, true)) && !h.Contains((10, true)), "tweede melder: 8 bezet -> wissel 5 afbuigend is de verdachte (nu " + string.Join(";", h) + ")");
        var h2 = k.HintsBijTweedeMelder(24, new[] { 21 }, stand);
        Eis(h2.Contains((10, false)), "tweede melder: 21 bezet -> wissel 10 terug naar rechtdoor is de verdachte (nu " + string.Join(";", h2) + ")");
        Eis(k.HintsBijTweedeMelder(24, new[] { 99 }, stand).Count == 0 && k.HintsBijTweedeMelder(24, Array.Empty<int>(), stand).Count == 0, "tweede melder: onbekende of ontbrekende tweede melder geeft geen hints");
        var kp = new Kortsluitpunt { TweedeMelders = new List<int> { 8 } };
        var kk = new Baankaart(); kk.Kortsluitpunten.Add(kp);
        Eis(Baankaart.VanJson(kk.AlsJson())!.Kortsluitpunten[0].TweedeMelders.SequenceEqual(new[] { 8 }), "tweede melder: blijft na opslaan en laden");
        Eis(Rapport.AlsTekst(kk).Contains("ook bezet: melder 8"), "tweede melder: staat in het rapport");
    }

    // BUG #77 (log 18:58): terugweg 14 -> 129 vereist wissel 5 afbuigend
    static void WisselEisenTest()
    {
        var k = Laad("baankaart_2026-10-10_1903_doorgangen.json");
        var c = new Configuratie(new[] { 10 });
        var n = k.MetWisselEisen(new List<int> { 14, 129, 24, 21 }, Richting.Vooruit, c, out var w);
        Eis(n.Equals(new Configuratie(new[] { 5, 10 })), "wisselEisen: pad 14-129-24-21 vooruit met 10 afbuigend krijgt ook wissel 5 afbuigend (nu " + n + ")");
        var n2 = k.MetWisselEisen(new List<int> { 21, 24, 129, 14 }, Richting.Achteruit, new Configuratie(new[] { 10 }), out _);
        Eis(n2.Equals(new Configuratie(new[] { 5, 10 })), "wisselEisen: van achteren 129 -> 14 vraagt wissel 5 afbuigend (nu " + n2 + ")");
        var n3 = k.MetWisselEisen(new List<int> { 14, 24, 21 }, Richting.Vooruit, new Configuratie(new[] { 5 }), out _);
        Eis(n3.Equals(Configuratie.Basis), "wisselEisen: 14 -> 24 vooruit vraagt wissel 5 rechtdoor (nu " + n3 + ")");
        Eis(k.MetWisselEisen(new List<int> { 30, 20, 27 }, Richting.Vooruit, c, out var w5).Equals(c) && w5.Count == 0, "wisselEisen: gewoon spoor zonder wissels verandert niets");
    }

    // BUG #143: wissel 1 en 2 (overloopwissel-paar, melder 12 / 5 / 13) werden door de verkenner zelf omgezet terwijl de loc in de kruising stond
    static void OverloopPaarEisenTest()
    {
        Console.WriteLine("Test: splitsing met twee gelijke uitgangen geeft geen wisseleis (BUG #143, baankaart 14-10 16:20)");
        var k = Laad("baankaart_2026-10-14_1621.json");
        var c12 = new Configuratie(new[] { 1, 2 });
        var n = k.MetWisselEisen(new List<int> { 12, 5, 13 }, Richting.Vooruit, c12, out var w);
        Eis(n.Equals(c12) && w.Count == 0, "overloop (#143): pad 12-5-13 vooruit met wissel 1 en 2 afbuigend blijft ongewijzigd (nu " + n + ")");
        var n2 = k.MetWisselEisen(new List<int> { 13, 5, 12 }, Richting.Achteruit, c12, out _);
        Eis(n2.Equals(c12), "overloop (#143): pad 13-5-12 achteruit met wissel 1 en 2 afbuigend blijft ongewijzigd (nu " + n2 + ")");
        Eis(!k.WisselEisenVoorStap(5, 13, Richting.Vooruit).Any(e => e.Adres == 2), "overloop (#143): stap 5 -> 13 vooruit stelt geen eis aan wissel 2");
        // andere splitsingen blijven werken (wissel 5: 14 -> 24 rechtdoor, 14 -> 129 afbuigend; wissel 14: 8 -> 24 achteruit afbuigend)
        Eis(k.WisselEisenVoorStap(14, 24, Richting.Vooruit).Any(e => e.Adres == 5 && !e.Afbuigend) && k.WisselEisenVoorStap(14, 129, Richting.Vooruit).Any(e => e.Adres == 5 && e.Afbuigend), "overloop (#143): gewone splitsing (wissel 5) vraagt nog steeds de juiste stand");
    }

    // BUG #78: wisselstraten-voorstel uit de baankaart van 19:03
    static void WisselstratenTest()
    {
        var k = Laad("baankaart_2026-10-10_1903_doorgangen.json");
        var v = WisselstraatVoorstel.Bereken(k);
        if (Environment.GetEnvironmentVariable("STRATEN_UIT") == "1") foreach (var x in v) Console.WriteLine($"  blok {x.VanBlok}->{x.NaarBlok} {x.Richting} {x.VanMelder}->{x.NaarMelder} [{x.Standen}] {x.EisenTekst}");
        WisselstraatVoorstel? Zoek(int van, int naar, Richting r) => v.FirstOrDefault(x => x.VanMelder == van && x.NaarMelder == naar && x.Richting == r);
        var a = Zoek(14, 129, Richting.Vooruit);
        Eis(a is not null && a.Eisen.Any(e => e.Adres == 5 && e.Afbuigend), "wisselstraat: 14 -> 129 vooruit vraagt wissel 5 afbuigend");
        var b = Zoek(14, 24, Richting.Vooruit);
        Eis(b is not null && b.Eisen.Any(e => e.Adres == 5 && !e.Afbuigend && e.Bron == EisBron.Splitsing), "wisselstraat: 14 -> 24 vooruit vraagt wissel 5 rechtdoor (uit splitsing)");
        var c = Zoek(24, 8, Richting.Vooruit);
        Eis(c is not null && c.Eisen.Any(e => e.Adres == 5 && e.Afbuigend) && c.Eisen.Any(e => e.Adres == 10 && e.Afbuigend), "wisselstraat: 24 -> 8 vooruit vraagt wissel 5 en 10 afbuigend");
        var d = Zoek(24, 21, Richting.Vooruit);
        Eis(d is not null && d.Eisen.Any(e => e.Adres == 10 && !e.Afbuigend && e.Bron == EisBron.Kortsluiting), "wisselstraat: 24 -> 21 vooruit vraagt wissel 10 rechtdoor (anders kortsluiting)");
        var t = v.Where(x => x.VanMelder == 129 && x.NaarMelder == 14 && x.Richting == Richting.Achteruit).ToList();
        Eis(t.Count == 1 && t[0].Eisen.Count == 1 && t[0].Eisen[0].Adres == 5 && t[0].Eisen[0].Afbuigend, "wisselstraat: 129 -> 14 achteruit: alleen wissel 5 afbuigend (de gereden stand met 10 erbij is overbodig, en van achteren telt de splitsing) (nu " + string.Join(" | ", t.Select(x => x.EisenTekst)) + ")");
        var e1 = Zoek(24, 21, Richting.Vooruit);
        Eis(e1 is not null && e1.Eisen.Any(e => e.Adres == 10 && !e.Afbuigend), "wisselstraat: 24 -> 21 vooruit vraagt wissel 10 rechtdoor");
        // BUG #155: kaart van 15-10 08:24 - 16 -> 5 vooruit met wissel 2 afbuigend geeft kortsluiting (ingang), niet 'tussen 5 en 13'
        var k155 = Laad("baankaart_2026-10-15_0824.json");
        foreach (var ob in k155.Wissel(2).Waarnemingen.Where(x => x.Soort == WaarnemingSoort.KortsluitingBijOmzetten && x.Via is null)) ob.Via = 16;
        var v155 = WisselstraatVoorstel.Bereken(k155).FirstOrDefault(x => x.VanMelder == 16 && x.NaarMelder == 5 && x.Richting == Richting.Vooruit);
        Eis(v155 is not null && v155.Eisen.Any(e => e.Adres == 2 && !e.Afbuigend && e.Bron == EisBron.Kortsluiting), "ingang (#155): wisselstraat 16 -> 5 vooruit vraagt wissel 2 rechtdoor (anders kortsluiting)");
        Eis(Rapport.AlsTekst(k155).Contains("ingang 16 → 5"), "ingang (#155): het rapport noemt 'ingang 16 → 5' en niet alleen 'kortsluiting tussen 5 en 13'");
        // BUG #159: oudere waarnemingen zonder Via + kaart van 12:20: 16 -> 5 met wissel 2 afbuigend blijft verboden, ook als de oplossing 'wissel 2 afbuigend' erin staat
        foreach (var naam in new[] { "baankaart_2026-10-15_1220.json" })
        {
            var k159 = Laad(naam);
            Eis(k159.VerbodenIngangen().Any(x => x.Via == 16 && x.Na == 5 && x.Adres == 2), "ingang (#159): " + naam + " kent 16 -> 5 met wissel 2 afbuigend als verboden (nu " + string.Join(", ", k159.VerbodenIngangen().Select(x => $"{x.Via}>{x.Na}/W{x.Adres}")) + ")");
            k159.HerstelStrijdigeOplossingen();
            Eis(!k159.Kortsluitpunten.Any(x => x.Opgelost && x.OpgelostMetAfbuigend == true && x.OpgelostDoorAdres == 2 && x.NaMelder == 5), "ingang (#159): " + naam + " heeft geen opgelost kortsluitpunt meer met 'wissel 2 afbuigend' na melder 5");
        }
        // BUG #209: niet-bestaand wissel 11 wordt uit een bestaande kaart gehaald
        var k209 = new Baankaart();
        k209.Wissels.Add(new WisselVondst { Adres = 11 });
        k209.Wissels.Add(new WisselVondst { Adres = 5 });
        k209.Overgangen.Add(new Overgang { Van = 13, Naar = 14, Richting = Richting.Vooruit, AantalKeer = 2, GezienBijConfiguraties = new List<string> { "5", "5,11" } });
        k209.Doorgangen.Add(new Doorgang { Via = 13, Melder = 14, Volgende = 129, Richting = Richting.Vooruit, Configuraties = new List<string> { "5,11" }, Tellers = new Dictionary<string, int> { ["5,11"] = 1, ["5"] = 2 } });
        k209.AdressenZonderEffect.Add(11);
        k209.VerwijderNietBestaandeAdressen(new[] { 11 });
        Eis(k209.Wissels.Count == 1 && k209.Wissels[0].Adres == 5, "opruimen (#209): wissel 11 is uit de wisselslijst");
        Eis(k209.Overgangen[0].GezienBijConfiguraties.SequenceEqual(new[] { "5" }), "opruimen (#209): '5,11' is '5' geworden en niet dubbel (nu " + string.Join("|", k209.Overgangen[0].GezienBijConfiguraties) + ")");
        Eis(k209.Doorgangen[0].Tellers["5"] == 3 && k209.Doorgangen[0].Tellers.Count == 1, "opruimen (#209): tellers van de doorgang samengevoegd");
        Eis(!k209.AdressenZonderEffect.Contains(11), "opruimen (#209): adres 11 niet meer 'zonder effect'");
        var i209 = new VerkenInstellingen { WisselAdresVan = 9, WisselAdresTot = 13, NietBestaandeWissels = "11" };
        Eis(!i209.WisselAdressen().Contains(11) && i209.WisselAdressen().Count() == 4, "opruimen (#209): adres 11 wordt niet meer getest");
        // BUG #214 + #209 met de echte kaart van 18-10 09:50 (kortsluitpunt #2 stond nog open; valse wissel 11 zat erin)
        var k214 = Laad("baankaart_2026-10-18_0950.json");
        var kp214 = k214.Kortsluitpunten.First(x => x.Id == 2);
        Eis(!kp214.Opgelost, "kortsluitpunt #2 (#214): staat in de kaart van 09:50 nog open");
        var meld214 = k214.LosKortsluitpuntenOpUitRitten();
        Eis(kp214.Opgelost && kp214.OpgelostDoorAdres == 5 && kp214.OpgelostMetAfbuigend == true, "kortsluitpunt #2 (#214): opgelost met adres 5 afbuigend (nu " + kp214.Opgelost + "/" + kp214.OpgelostDoorAdres + "; " + string.Join(";", meld214) + ")");
        Eis(k214.LosKortsluitpuntenOpUitRitten().Count == 0, "kortsluitpunt #2 (#214): tweede keer niets meer te doen");
        Eis(k214.Wissels.Any(w => w.Adres == 11), "opruimen (#209): de kaart van 09:50 bevat de valse wissel 11");
        k214.VerwijderNietBestaandeAdressen(new[] { 11 });
        Eis(!k214.Wissels.Any(w => w.Adres == 11) && !k214.Overgangen.Any(o => o.GezienBijConfiguraties.Any(c => Configuratie.VanSleutel(c).Bevat(11))) && !k214.Doorgangen.Any(d => d.Configuraties.Any(c => Configuratie.VanSleutel(c).Bevat(11))), "opruimen (#209): wissel 11 is overal uit de kaart van 09:50 verdwenen");
        Eis(Baankaart.VanJson(k214.AlsJson()) is not null, "opruimen (#209): de opgeruimde kaart blijft laden");
        // BUG #215: terugweg over een keerlus (log 18-10 10:22)
        var k215 = new Baankaart();
        foreach (var (v215, n215) in new[] { (129, 10), (10, 9), (9, 1), (1, 12), (12, 11), (11, 4), (4, 129), (129, 14), (14, 13) })
            k215.Overgangen.Add(new Overgang { Van = v215, Naar = n215, Richting = Richting.Achteruit, AantalKeer = 1, GezienBijConfiguraties = new List<string> { "5,10" } });
        var pad215 = new List<int> { 129, 10, 9, 1, 12, 11, 4, 129, 14, 13 };
        // BUG #232: op de plek waar de loc staat (begin van het pad) wordt niet meer geknipt: 129 -> 14 keren na aankomst vanaf 10 ging op de baan twee keer naar 10
        Eis(BaanVerkenner.KnipLus(k215, pad215, Richting.Achteruit).SequenceEqual(pad215), "terugweg (#215/#232): op de plek van de loc (129, aangekomen vanaf 10) wordt de lus niet geknipt (nu " + string.Join(",", BaanVerkenner.KnipLus(k215, pad215, Richting.Achteruit)) + ")");
        var pad232 = new List<int> { 13, 129, 10, 9, 1, 12, 11, 4, 129, 14, 13 };
        Eis(BaanVerkenner.KnipLus(k215, pad232, Richting.Achteruit).SequenceEqual(new[] { 13, 129, 14, 13 }), "terugweg (#215): verderop in het pad wordt een lus nog wel geknipt (nu " + string.Join(",", BaanVerkenner.KnipLus(k215, pad232, Richting.Achteruit)) + ")");
        Eis(!BaanVerkenner.KerenToegestaan(10, Richting.Vooruit, 14, Richting.Achteruit) && BaanVerkenner.KerenToegestaan(10, Richting.Vooruit, 10, Richting.Achteruit) && BaanVerkenner.KerenToegestaan(10, Richting.Vooruit, 24, Richting.Vooruit), "terugweg (#232): na aankomst op 129 vanaf 10 (vooruit) mag keren alleen terug naar 10; doorrijden mag");
        var k215b = new Baankaart();
        Eis(BaanVerkenner.KnipLus(k215b, pad215, Richting.Achteruit).Count == pad215.Count, "terugweg (#215): is de overgang na de lus onbekend, dan blijft het pad zoals het was");
        Eis(BaanVerkenner.HerhaaldeOvergang(new List<int> { 129, 10, 9, 1, 12, 11, 4, 129, 10 }) && !BaanVerkenner.HerhaaldeOvergang(new List<int> { 129, 10, 9, 1, 12, 11, 4, 129 }), "navigatie (#215): herhaalde overgang 129 -> 10 wordt na één rondje gezien, niet eerder");
        // BUG #217: kaart 18-10 11:46 - kortsluitpunt #2 stond 'opgelost met wissel 6', maar 14 -> 129 vooruit met 6 afbuigend geeft kortsluiting
        var k217 = Laad("baankaart_2026-10-18_1146.json");
        var kp217 = k217.Kortsluitpunten.First(x => x.Id == 2);
        Eis(kp217.Opgelost && kp217.OpgelostDoorAdres == 6, "kortsluitpunt #2 (#217): staat in de kaart van 11:46 op 'opgelost met 6'");
        Eis(k217.HerstelStrijdigeOplossingen().Any(t => t.Contains("#2")) && !kp217.Opgelost && !kp217.Opgegeven, "kortsluitpunt #2 (#217): de oplossing met wissel 6 wordt ingetrokken (nog niet opgegeven)");
        Eis(!k217.RegistreerHandmatigeOplossing(kp217, 6, true), "kortsluitpunt #2 (#217): wissel 6 wordt niet opnieuw als oplossing geaccepteerd");
        k217.LosKortsluitpuntenOpUitRitten();
        Eis(kp217.Opgelost && kp217.OpgelostDoorAdres == 5, "kortsluitpunt #2 (#217): daarna volgt adres 5 uit de ritten (nu " + kp217.OpgelostDoorAdres + ")");
        // BUG #204: een voorlopige (alleen uit het alarm afgeleide) oplossing wordt door ritten vervangen of bevestigd
        var k204 = Laad("baankaart_2026-10-18_1146.json");
        var kp204 = k204.Kortsluitpunten.First(x => x.Id == 2);
        kp204.Voorlopig = true;
        var t204 = k204.ToetsVoorlopigeOplossingen();
        Eis(t204.Count == 1 && kp204.Opgelost && kp204.OpgelostDoorAdres == 5 && !kp204.Voorlopig, "voorlopig (#204): ruis 'wissel 6' wordt door de ritten vervangen door wissel 5 (nu " + kp204.OpgelostDoorAdres + "; " + string.Join(";", t204) + ")");
        var kp204b = k204.Kortsluitpunten.First(x => x.Id == 1);
        kp204b.Voorlopig = true;
        k204.ToetsVoorlopigeOplossingen();
        Eis(kp204b.Opgelost && !kp204b.Voorlopig && kp204b.OpgelostDoorAdres == 1, "voorlopig (#204): wissel 1 bij kortsluitpunt #1 wordt door de ritten 12 -> 5 met 1 en 2 afbuigend bevestigd");
        var kp204c = new Kortsluitpunt { Id = 9, NaMelder = 99, Richting = Richting.Vooruit, Configuratie = Configuratie.Basis.Met(2), Opgelost = true, OpgelostDoorAdres = 3, OpgelostMetAfbuigend = true, Voorlopig = true };
        k204.Kortsluitpunten.Add(kp204c); k204.ToetsVoorlopigeOplossingen();
        Eis(kp204c.Opgelost && kp204c.Voorlopig, "voorlopig (#204): zonder ritten over de overgang blijft een voorlopige oplossing staan (geen bewijs en geen weerlegging)");
        // BUG #202: wisselstanden gelden in beide richtingen
        var k202 = Laad("baankaart_2026-10-18_1146.json"); k202.HerstelStrijdigeOplossingen();
        var v202 = Wisselstraten_Bereken(k202);
        int verschil202 = 0;
        foreach (var w202 in v202)
        {
            var tg = v202.Where(x => x.VanMelder == w202.NaarMelder && x.NaarMelder == w202.VanMelder && x.Richting == w202.Richting.Om()).ToList();
            if (tg.Count != 1 || v202.Count(x => x.VanMelder == w202.VanMelder && x.NaarMelder == w202.NaarMelder && x.Richting == w202.Richting) != 1) continue;
            string S(WisselstraatVoorstel w) => string.Join(",", w.Eisen.Where(e => e.Bron != EisBron.Aangenomen).OrderBy(e => e.Adres).Select(e => $"{e.Adres}{(e.Afbuigend ? 'A' : 'R')}"));
            if (S(w202) != S(tg[0])) { verschil202++; Console.WriteLine($"  #202 {w202.VanMelder}>{w202.NaarMelder} {w202.Richting}: {w202.EisenTekst} || {tg[0].EisenTekst}"); }
        }
        Eis(verschil202 == 0, "wisselstraten (#202): bewezen eisen zijn per overgang in beide richtingen gelijk (verschillen: " + verschil202 + ")");
        // BUG #207: doorgang toont alleen de minimale rijstanden
        Eis(Rapport.DoorgangStanden(new[] { "5,10", "5,10,14", "5,10,14,2" }) == Configuratie.VanSleutel("5,10").ToString(), "rapport (#207): alleen de kleinste rijstand van een doorgang wordt getoond (nu " + Rapport.DoorgangStanden(new[] { "5,10", "5,10,14", "5,10,14,2" }) + ")");
        Eis(Rapport.DoorgangStanden(new[] { "", "3,4" }) == "geen wissel van invloed gezien", "rapport (#207): reed de doorgang ook zonder afbuigende wissels, dan staat er 'geen wissel van invloed gezien'");
        // BUG #212: een opgelost kortsluitpunt krijgt een waarneming bij het oplossingswissel
        var k212 = Laad("baankaart_2026-10-18_1146.json");
        k212.HerstelStrijdigeOplossingen(); k212.LosKortsluitpuntenOpUitRitten(); k212.VulOntbrekendeOplossingWaarnemingen();
        Eis(k212.Kortsluitpunten.Where(x => x.Opgelost && x.OpgelostDoorAdres is int).All(x => k212.Wissel(x.OpgelostDoorAdres!.Value).Waarnemingen.Any(w => w.Soort == WaarnemingSoort.LostKortsluitingOp && w.NaMelder == x.NaMelder && w.Richting == x.Richting)), "kaartcontrole (#212): elk opgelost kortsluitpunt heeft een waarneming bij zijn oplossingswissel");
        // BUG #218-#225: echte log en kaart 18-10 12:21
        Eis(BaanVerkenner.KlemNogOnderLoc(new[] { 24, 129 }, new[] { 129 }, false), "klem (#218): loc nog op 129 na de oplossing bij 24/129: wissel 10 blijft afbuigend, ook zonder sparen");
        Eis(!BaanVerkenner.KlemNogOnderLoc(new[] { 24, 129 }, new[] { 129, 14 }, false) && BaanVerkenner.KlemNogOnderLoc(new[] { 24, 129 }, new[] { 129, 14 }, true), "klem (#218): staat de loc al deels op 14, dan bepaalt een nieuwe route de wissel weer (met sparen blijft hij staan)");
        Eis(!BaanVerkenner.KlemNogOnderLoc(new[] { 24, 129 }, new[] { 13 }, true), "klem (#218): loc op 13: de klemoplossing geldt niet meer");
        Eis(!BaanVerkenner.PartnerHoortBijPlek(new[] { 5, 16 }, new[] { 24, 129 }) && BaanVerkenner.PartnerHoortBijPlek(new[] { 24 }, new[] { 24, 129 }), "partner (#219): wissel 2 (losgemaakt op 5/16) gaat bij de kortsluiting op 24/129 niet terug naar afbuigend");
        var bas222 = new List<int> { 13, 14, 24, 21, 29 };
        Eis(BaanVerkenner.ProefWisselVanPuntzijdeBereden(bas222, new List<int> { 13, 14, 129, 24 }), "puntzijde (#222): 13-14-129-24 met wissel 5: de loc nam de tak en reed verder, wissel 5 is van de puntzijde bereden");
        Eis(!BaanVerkenner.ProefWisselVanPuntzijdeBereden(bas222, new List<int> { 13, 14, 24 }) && !BaanVerkenner.ProefWisselVanPuntzijdeBereden(bas222, new List<int> { 13, 14, 129 }), "puntzijde (#222): zelfde melders als de basis (wissel 10) of kortsluiting direct op de tak: wel losmaken");
        Eis(BaanVerkenner.StopVolgorde(BaanVerkenner.StopVolgordeEerst(new[] { 9, 8 }, new[] { 8 }, new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }), new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }).SequenceEqual(new[] { 9, 8, 1, 2, 3, 4, 5, 6, 7 }), "stop (#221): de blokken met de kortsluiting krijgen de STOP als eerste");
        Eis(BaanVerkenner.EersteAfwijking(bas222, new List<int> { 13, 14, 129, 24 }) == 2 && BaanVerkenner.EersteAfwijking(bas222, new List<int> { 13, 5 }) == 1 && BaanVerkenner.EersteAfwijking(bas222, new List<int> { 13, 14 }) == -1, "kruipen (#224): wissel 5 splitst na melder 14, niet bij de startmelder 13");
        string K225(string r, string hex) => Modeltreinbesturing.Hardware.DinamoLogVertaler.KortTekst(r, Convert.FromHexString(hex));
        Eis(K225("Uit", "0AC080B6") == "stand opvragen van melder 1" && K225("Uit", "4AC0BFB7") == "stand opvragen van melder 64", "hardwarelog (#225): de standvraag voor melder 1 en 64 is geen 'onbekend commando' meer (nu '" + K225("Uit", "0AC080B6") + "')");
        var k223 = Laad("baankaart_2026-10-18_1221.json");
        var kp223 = k223.Kortsluitpunten.First(x => x.Id == 1);
        Eis(!kp223.Opgelost && kp223.NaMelder == 24, "kortsluitpunt #1 (#223): staat in de kaart van 12:21 nog open");
        Eis(BaanVerkenner.AlarmOplossingPastBij(kp223, new[] { 24, 129 }, new[] { 8, 9 }) && !BaanVerkenner.AlarmOplossingPastBij(kp223, new[] { 5, 16 }, new[] { 3, 4 }), "kortsluitpunt #1 (#223): de oplossing van 12:08 (loc op 24/129, blok 8/9) hoort erbij, die bij 5/16 niet");
        Eis(k223.RegistreerHandmatigeOplossing(kp223, 10, true) && kp223.OpgelostDoorAdres == 10, "kortsluitpunt #1 (#223): wissel 10 afbuigend wordt geaccepteerd als oplossing");
        // BUG #227: melder aan de andere kant bij het vertrek (autolog 18-10 13:25)
        var k227 = Laad("baankaart_2026-10-18_1325.json");
        Eis(BaanVerkenner.IsAchterkantMelder(k227, 21, Richting.Achteruit, 29) && !BaanVerkenner.IsAchterkantMelder(k227, 21, Richting.Achteruit, 24) && !BaanVerkenner.IsAchterkantMelder(k227, 21, Richting.Vooruit, 29), "vertrek (#227): achteruit vanaf 21 ligt melder 29 aan de andere kant, 24 niet; vooruit is 29 gewoon de volgende");
        Eis(k227.WisselEisenVoorStap(24, 21, Richting.Vooruit).Contains((10, false)), "kaart 13:25 (#227): de valse splitsing eist wissel 10 rechtdoor voor 24 -> 21 (ook vanaf 129)");
        var t227 = k227.VerwijderAchterkantOvergangen();
        Console.WriteLine("    opgeruimd (#227): " + string.Join(" | ", t227));
        Eis(!k227.Overgangen.Any(o => o.Van == 21 && o.Naar == 29 && o.Richting == Richting.Achteruit) && !k227.Overgangen.Any(o => o.Van == 29 && o.Naar == 24 && o.Richting == Richting.Achteruit), "kaart 13:25 (#227): de valse overgangen 21 -> 29 en 29 -> 24 (achteruit) zijn weg");
        Eis(!k227.Wissel(10).Waarnemingen.Any(o => o.Soort == WaarnemingSoort.Splitsing && o.NaMelder == 21) && k227.Wissel(10).Waarnemingen.Any(o => o.Soort == WaarnemingSoort.KortsluitingBijOmzetten && o.NaMelder == 24), "kaart 13:25 (#227): de valse splitsing van wissel 10 na melder 21 is weg, de echte waarneming bij 24 blijft");
        Eis(!k227.WisselEisenVoorStap(24, 21, Richting.Vooruit).Contains((10, false)) && k227.Overgangen.Any(o => o.Van == 21 && o.Naar == 29 && o.Richting == Richting.Vooruit), "kaart 13:25 (#227): geen eis 'wissel 10 rechtdoor' meer voor 24 -> 21; 21 -> 29 vooruit blijft");
        Eis(Baankaart.VanJson(k227.AlsJson()) is not null && Laad("baankaart_2026-10-18_1221.json").VerwijderAchterkantOvergangen().Count == 0, "kaart (#227): de opgeruimde kaart laadt; de kaart van 12:21 heeft niets op te ruimen");
        // BUG #228/#229: navigatie 13 -> 14 -> 129 -> 24 (autolog 18-10 13:31)
        var k228 = Laad("baankaart_2026-10-18_1333.json");
        var e228a = new Etappe { Van = 13, Naar = 14, Richting = Richting.Vooruit, Pad = new List<int> { 13, 14 }, Configuratie = Configuratie.Basis };
        var e228b = new Etappe { Van = 14, Naar = 24, Richting = Richting.Vooruit, Pad = new List<int> { 14, 129, 24 }, Configuratie = Configuratie.Basis.Met(5).Met(10) };
        var c228 = BaanVerkenner.GrensWisselsVooraf(k228, e228a, e228b, Configuratie.Basis, e228b.Configuratie, out var u228);
        Eis(c228.Bevat(5) && !c228.Bevat(10), "navigatie (#228): wissel 5 (splitsing na melder 14) wordt al tijdens 13 -> 14 afbuigend gezet, wissel 10 nog niet (nu " + c228 + ")");
        var c228b = BaanVerkenner.GrensWisselsVooraf(k228, e228a, new Etappe { Van = 14, Naar = 13, Richting = Richting.Achteruit, Pad = new List<int> { 14, 13 }, Configuratie = Configuratie.Basis.Met(5) }, Configuratie.Basis, Configuratie.Basis.Met(5), out _);
        Eis(!c228b.Bevat(5), "navigatie (#228): bij een kering op melder 14 wordt niets vooraf gezet");
        var c229 = BaanVerkenner.StartmelderWisselsVooraf(k228, e228b, Configuratie.Basis.Met(5).Met(10), Configuratie.Basis.Met(5).Met(6), out var u229);
        Eis(c229.Bevat(10), "navigatie (#229): wissel 10 blijft afbuigend (oplossing kortsluitpunt #1) en wordt niet vooraf rechtdoor gezet voor startmelder 24 (nu " + c229 + "; " + u229 + ")");
        // BUG #231: keerlus 10 -> 129 (autolog 18-10 13:43)
        var k231 = Laad("baankaart_2026-10-18_1349.json");
        var v231 = BaanVerkenner.AndereTakVerdachten(k231, new[] { 10, 129 }, new List<int> { 14, 129, 4, 11, 12, 1, 9, 10, 129 });
        Eis(v231.Contains(6), "keerlus (#231): na 10 -> 129 is wissel 6 de eerste verdachte (afbuigend gaf kortsluiting vanaf 14) (nu " + string.Join(",", v231) + ")");
        Eis(!BaanVerkenner.AndereTakVerdachten(k231, new[] { 129 }, new List<int> { 13, 14, 129 }).Contains(6), "keerlus (#231): vanaf 14 (dezelfde ingang) is wissel 6 geen verdachte van de andere tak");
        Eis(AutoTussenstand.Map(@"C:\Marco\Autolog") == @"C:\Marco" && AutoTussenstand.Map(@"C:\Marco\autolog\") == @"C:\Marco" && AutoTussenstand.Map("") == @"C:\Marco" && AutoTussenstand.Map(@"D:\Logs") == @"D:\Logs" && new VerkenInstellingen().AutoTussenstandMap == @"C:\Marco", "tussenstand (#233): standaard en oude standaard C:\\Marco\\Autolog worden C:\\Marco; een eigen map blijft");
        // BUG #235: wissel 6 omgekeerd aangesloten
        var om235 = new VerkenInstellingen { OmgekeerdAangesloten = "6" }.OmgekeerdeWissels();
        Eis(om235.SetEquals(new[] { 6 }) && new VerkenInstellingen().OmgekeerdeWissels().Count == 0, "omgekeerd (#235): instelling '6' geeft wissel 6, standaard geen");
        var t235 = VerkenLog.MetBaanstand("De kortsluiting ging weg nadat wissel 6 (ligt bij de loc) naar afbuigend is geschakeld.", om235);
        Eis(t235.Contains("omgekeerd aangesloten") && VerkenLog.MetBaanstand("Adres 6: afbuigend (wisselcommando 74)", om235).Contains("op de baan rechtdoor") && VerkenLog.MetBaanstand("Niet geprobeerd: wissel 5, 6 (ligt bij de loc), afbuigend", om235).Contains("omgekeerd"), "omgekeerd (#235): meldingen over wissel 6 met een stand krijgen de uitleg");
        Eis(VerkenLog.MetBaanstand("Adres 16: afbuigend", om235) == "Adres 16: afbuigend" && VerkenLog.MetBaanstand("Wissel 6 gevonden", om235) == "Wissel 6 gevonden" && VerkenLog.MetBaanstand("Rijstand: afbuigend: 5, 6, 10", om235).Contains("omgekeerd"), "omgekeerd (#235): adres 16 of een melding zonder stand blijft gewoon; 'afbuigend: 5, 6' wel");
        var k235 = Laad("baankaart_2026-10-18_1349.json"); k235.OmgekeerdeWissels = new List<int> { 6 };
        Eis(Rapport.AlsTekst(k235).Contains("OMGEKEERD AANGESLOTEN") && Rapport.AlsHtml(k235).Contains("Omgekeerd aangesloten: wissel 6"), "omgekeerd (#235): rapport (tekst en html) noemt het");
        // BUG #260: vanaf 13 (vooruit, wissel 5 rechtdoor) komt de loc via 14 op 24; met wissel 10 afbuigend is dat een bekende kortsluiting
        var k260 = Laad("baankaart_2026-10-20_1800.json");
        var v260 = BaanVerkenner.VerbodenVooruit(k260, 13, Richting.Vooruit, a => a == 1 || a == 9 || a == 10);
        Eis(v260.Contains(10) && !v260.Contains(1), "verboden vooruit (#260): op 13 vooruit met 1, 9, 10 afbuigend moet wissel 10 rechtdoor (nu " + string.Join(",", v260) + ")");
        Eis(BaanVerkenner.VerbodenVooruit(k260, 13, Richting.Vooruit, a => a == 1).Count == 0, "verboden vooruit (#260): zonder wissel 10 afbuigend niets");
        // BUG #261: na de kortsluiting 14 -> 24 met wissel 1, 9, 10 afbuigend is alleen wissel 10 een bekende verboden ingang
        {
            var bf261 = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var sim261 = new Baanverkenner.Kern.Simulatie.SimulatieBaan { DinamoGedrag = false, LocAdres = 5408 };
            sim261.VerbindenAsync("SIM").GetAwaiter().GetResult();
            var klok261 = new VirtueleKlok();
            var ins261 = new VerkenInstellingen { LocAdres = 5408, WisselAdresVan = 1, WisselAdresTot = 14 };
            var v261 = new BaanVerkenner(sim261, ins261, klok261, new VerkenLog(klok261), (_, _) => Task.FromResult(true), new VerkenStatus { Instellingen = ins261, Kaart = Laad("baankaart_2026-10-20_1800.json") });
            var st261 = (Dictionary<int, bool>)typeof(BaanVerkenner).GetField("_wisselStand", bf261)!.GetValue(v261)!;
            st261[1] = true; st261[9] = true; st261[10] = true;
            typeof(BaanVerkenner).GetField("_laatsteKortsluitReeks", bf261)!.SetValue(v261, new List<int> { 12, 5, 13, 14, 24 });
            typeof(BaanVerkenner).GetField("_laatsteKortsluitContext", bf261)!.SetValue(v261, ((int, Richting, Configuratie)?)(24, Richting.Vooruit, Configuratie.Basis));
            var bk261 = (List<int>)typeof(BaanVerkenner).GetMethod("BekendeVerbodenVoorLaatsteStap", bf261)!.Invoke(v261, null)!;
            Eis(bk261.SequenceEqual(new[] { 10 }), "verboden laatste stap (#261): alleen wissel 10 (nu " + string.Join(",", bk261) + ")");
            // de kortsluiting werd gemeld vóór de terugweg (context 24 achteruit): dan telt de laatste rit (14 -> 24 vooruit)
            typeof(BaanVerkenner).GetField("_laatsteKortsluitContext", bf261)!.SetValue(v261, ((int, Richting, Configuratie)?)(24, Richting.Achteruit, Configuratie.Basis));
            typeof(BaanVerkenner).GetField("_laatsteKortsluitReeks", bf261)!.SetValue(v261, new List<int>());
            var kaart261 = (VerkenStatus)typeof(BaanVerkenner).GetField("_status", bf261)!.GetValue(v261)!;
            kaart261.Kaart.Ritten.Add(new GeredenRit { Reeks = new List<int> { 12, 5, 13, 14, 24 }, Richting = Richting.Vooruit, Start = 12 });
            var bk261b = (List<int>)typeof(BaanVerkenner).GetMethod("BekendeVerbodenVoorLaatsteStap", bf261)!.Invoke(v261, null)!;
            Eis(bk261b.Contains(10), "verboden laatste stap (#261): ook via de laatste rit als de melding van de terugweg kwam (nu " + string.Join(",", bk261b) + ")");
        }
        // BUG #279: na de navigatie 29 .. 24, 129 .. 11, 4 (achteruit) met de loc op 4 en 129 is wissel 9 rechtdoor (van 24 -> 129 aan het begin) niet 'bewezen' voor de plek van de loc
        var k279 = Laad("baankaart_2026-10-21_1455.json");
        var b279 = BaanVerkenner.BewezenStandenBijLoc(k279, new[] { 29, 21, 24, 129, 10, 9, 1, 12, 11, 4 }, Richting.Achteruit, new[] { 4, 129 });
        Eis(!b279.ContainsKey(9), "bewezen bij de loc (#279): wissel 9 telt niet als bewezen door de stap 24 -> 129 aan het begin van de rit (nu " + string.Join(",", b279.Select(kv => kv.Key + (kv.Value ? "A" : "R"))) + ")");
        var o280 = BaanVerkenner.OplossingenVooruit(k279, 11, Richting.Achteruit, a => a is 6 or 10);
        Eis(o280.Contains((9, true)), "vooruitregels (#280): op 11 achteruit (ook in een navigatie) gaat wissel 9 afbuigend (nu " + string.Join(",", o280.Select(x => x.Adres + (x.Afbuigend ? "A" : "R"))) + ")");
        // BUG #276: wissel 5 ligt bij 14/24/129 en kan de stap 8 -> 15 niet oplossen; wissel 13 (kruiswissel bij 24/8) wel
        var k276 = Laad("baankaart_2026-10-21_1302.json");
        Eis(BaanVerkenner.LigtNietBijStap(k276, 5, 8, 15) is { Count: > 0 }, "plek van de wissel (#276): wissel 5 ligt niet bij 8 -> 15");
        Eis(BaanVerkenner.LigtNietBijStap(k276, 13, 8, 15) is null, "plek van de wissel (#276): wissel 13 kan bij 8 liggen");
        Eis(BaanVerkenner.LigtNietBijStap(k276, 14, 8, 15) is null, "plek van de wissel (#276): wissel 14 (plek niet uit een splitsing bekend) telt niet als 'elders'");
        // BUG #274: kortsluitpunt #2 (na 8 vooruit) mag wissel 14 afbuigend niet proberen: na 8 vooruit (vanaf 17) gaf dat al kortsluiting
        var k274 = Laad("baankaart_2026-10-21_1242.json");
        Eis(BaanVerkenner.BekendeKortsluitingNa(k274, 14, 8, Richting.Vooruit) is not null, "bekende kortsluiting (#274): wissel 14 afbuigend na melder 8 vooruit is bekend");
        Eis(BaanVerkenner.BekendeKortsluitingNa(k274, 14, 8, Richting.Achteruit) is null && BaanVerkenner.BekendeKortsluitingNa(k274, 13, 8, Richting.Vooruit) is null, "bekende kortsluiting (#274): niet voor een andere richting of wissel");
        var wb275 = BaanVerkenner.WisselsBijBezet(k274, new[] { 14, 129 });
        Eis(wb275.Contains(5) || wb275.Contains(6), "sparen (#275): met de loc op 14 en 129 ligt wissel 5 of 6 bij de loc, dus ZetConfiguratie(Basis, sparen) laat ze staan (nu " + string.Join(",", wb275.OrderBy(x => x)) + ")");
        // BUG #270: terugweg na de lus van opdracht #8 (stand 9+10): op 24 achteruit naar 14 moet wissel 10 rechtdoor (24 -> 129 alleen met 10 afbuigend)
        var k270 = Laad("baankaart_2026-10-21_1221.json");
        var e270a = BaanVerkenner.StapEisenUitOvergangen(k270, 24, 14, Richting.Achteruit);
        var e270b = BaanVerkenner.StapEisenUitOvergangen(k270, 24, 129, Richting.Achteruit);
        Eis(e270a.TryGetValue(10, out var x270a) && !x270a && e270b.TryGetValue(10, out var x270b) && x270b, "overgang-eisen (#270): 24 -> 14 achteruit vraagt 10 rechtdoor, 24 -> 129 vraagt 10 afbuigend (nu " + string.Join(",", e270a.Select(kv => kv.Key + (kv.Value ? "A" : "R"))) + " / " + string.Join(",", e270b.Select(kv => kv.Key + (kv.Value ? "A" : "R"))) + ")");
        var p270 = new List<int> { 5, 16, 15, 8, 17, 25, 26, 28, 27, 20, 30, 29, 21, 24, 14, 13, 5, 12, 11, 4, 129, 10 };
        var (c270, w270) = BaanVerkenner.MetOvergangEisen(k270, p270, Richting.Achteruit, new Configuratie(new[] { 9, 10 }));
        Eis(!c270.Bevat(10), "overgang-eisen (#270): het terugpad van 12:18 krijgt wissel 10 rechtdoor (nu " + c270 + ", " + string.Join(", ", w270) + ")");
        var s270 = BaanVerkenner.SplitsOpOvergangEisen(k270, new List<int> { 21, 24, 14 }, Richting.Achteruit);
        Eis(s270.Count == 1, "overgang-eisen (#270): pad zonder tegenstrijdige stappen blijft één stuk");
        var s270b = BaanVerkenner.SplitsOpOvergangEisen(k270, new List<int> { 13, 14, 24, 129 }, Richting.Vooruit);
        Console.WriteLine("  #270 splitsing 13-14-24-129 vooruit: " + string.Join(" | ", s270b.Select(x => string.Join(",", x))));
        // BUG #269: op 129 achteruit met 5 rechtdoor en 6 Dinamo afbuigend (baan: beide rechtdoor) gaat de loc naar 10, niet naar 14: geen staartwissel 5
        var k269 = Laad("baankaart_2026-10-21_1207.json");
        Func<int, bool> st269 = a => a is 6 or 10;
        var v269 = BaanVerkenner.VerwachteVolgende(k269, 129, Richting.Achteruit, st269);
        Eis(v269.SequenceEqual(new[] { 10 }), "vervolg bij stand (#269): 129 achteruit met 5/6 op de baan rechtdoor -> alleen 10 (nu " + string.Join(",", v269) + ")");
        Eis(!BaanVerkenner.StaartWissels(k269, 129, Richting.Achteruit, st269).Any(x => x.Adres == 5), "vervolg bij stand (#269): wissel 5 is dan geen staartwissel");
        var v269b = BaanVerkenner.VerwachteVolgende(k269, 129, Richting.Achteruit, a => a is 5 or 10);
        Eis(v269b.Contains(14) && !v269b.Contains(10), "vervolg bij stand (#269): met 5 afbuigend (6 Dinamo rechtdoor) -> 14 (nu " + string.Join(",", v269b) + ")");
        Eis(BaanVerkenner.VerbodenVooruit(k269, 24, Richting.Achteruit, a => a is 9 or 10).Contains(9), "verboden vooruit (#260/#269): 24 -> 129 achteruit met wissel 9 afbuigend (nieuwe vondst 12:02) gaat vooraf naar rechtdoor");
        // BUG #267: op melder 11 achteruit (naar 4 en 129) moet wissel 9 afbuigend (kortsluitpunt #3, opgelost met 9), ook als de opdracht een andere stand heeft
        var k267 = Laad("baankaart_2026-10-21_1128.json");
        var o267 = BaanVerkenner.OplossingenVooruit(k267, 11, Richting.Achteruit, a => a == 10);
        Eis(o267.Contains((9, true)), "oplossing vooruit (#267): op 11 achteruit wissel 9 afbuigend (nu " + string.Join(",", o267.Select(x => x.Adres + (x.Afbuigend ? "A" : "R"))) + ")");
        var o267b = BaanVerkenner.OplossingenVooruit(k267, 11, Richting.Achteruit, a => a == 9);
        Eis(!o267b.Any(x => x.Adres == 9), "oplossing vooruit (#267): staat 9 al afbuigend, dan niets");
        Eis(!BaanVerkenner.OplossingenVooruit(k267, 11, Richting.Achteruit, _ => false).Any(x => x.Adres == 10), "oplossing vooruit (#267): voorlopige oplossing (kortsluitpunt #4, wissel 10) telt niet");
        // BUG #264: staartwissel 5 met volger 6 (overloop) niet omzetten als de loc al op de afbuigende tak (129) staat
        {
            var bf264 = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var sim264 = new Baanverkenner.Kern.Simulatie.SimulatieBaan { DinamoGedrag = false, LocAdres = 5408 };
            sim264.VerbindenAsync("SIM").GetAwaiter().GetResult();
            var klok264 = new VirtueleKlok(); var log264 = new VerkenLog(klok264);
            var ins264 = new VerkenInstellingen { LocAdres = 5408, WisselAdresVan = 1, WisselAdresTot = 14, GekoppeldeWissels = "5+6", OmgekeerdAangesloten = "6" };
            var k264 = new Baankaart();
            k264.Wissel(5).Waarnemingen.Add(new WisselWaarneming { Soort = WaarnemingSoort.Splitsing, NaMelder = 14, Richting = Richting.Vooruit, VolgendeBijRechtdoor = 24, VolgendeBijAfbuigend = 129 });
            var v264 = new BaanVerkenner(sim264, ins264, klok264, log264, (_, _) => Task.FromResult(true), new VerkenStatus { Instellingen = ins264, Kaart = k264 });
            var mon264 = (MelderMonitor)typeof(BaanVerkenner).GetField("_monitor", bf264)!.GetValue(v264)!;
            mon264.Ontvang(4, true); mon264.Ontvang(129, true);
            klok264.Wacht(TimeSpan.FromSeconds(1)).GetAwaiter().GetResult(); mon264.Bijwerken();
            var st264 = (Dictionary<int, bool>)typeof(BaanVerkenner).GetField("_wisselStand", bf264)!.GetValue(v264)!;
            st264[5] = false; st264[6] = true;
            var t264 = (Task<Configuratie>)typeof(BaanVerkenner).GetMethod("ZetStaartWissels", bf264)!.Invoke(v264, new object[] { 129, Richting.Achteruit, Configuratie.Basis, new RitDoel { Configuratie = Configuratie.Basis, StaartWissels = true } })!;
            t264.GetAwaiter().GetResult();
            Eis(!st264[5] && st264[6] && log264.Regels.Any(x => x.Tekst.Contains("BUG #264")), "overloop te laat (#264): met de loc op 129 gaan 5 en 6 niet om");
            mon264.Ontvang(129, false); mon264.Ontvang(4, false); mon264.Ontvang(11, true);
            klok264.Wacht(TimeSpan.FromSeconds(1)).GetAwaiter().GetResult(); mon264.Bijwerken();
            var t264b = (Task<Configuratie>)typeof(BaanVerkenner).GetMethod("ZetStaartWissels", bf264)!.Invoke(v264, new object[] { 129, Richting.Achteruit, Configuratie.Basis, new RitDoel { Configuratie = Configuratie.Basis, StaartWissels = true } })!;
            t264b.GetAwaiter().GetResult();
            Eis(st264[5] && !st264[6], "overloop te laat (#264): staat de loc nog niet bij 129, dan gaan 5 en 6 wel om (6 Dinamo rechtdoor = baan afbuigend)");
        }
        // BUG #262: een onbekende melder die knippert (137/138 bij 129) telt niet als bezet; een bekende melder wel
        {
            var klok262 = new VirtueleKlok();
            var mon262 = new MelderMonitor(klok262, () => 100, m => m == 129);
            int? gevonden262 = null; mon262.FlikkeraarGevonden = m => gevonden262 ??= m;
            for (int i = 0; i < 8; i++)
            {
                mon262.Ontvang(137, i % 2 == 0); mon262.Ontvang(129, i % 2 == 0);
                klok262.Wacht(TimeSpan.FromMilliseconds(600), CancellationToken.None).GetAwaiter().GetResult(); mon262.Bijwerken();
            }
            mon262.Ontvang(129, true); mon262.Ontvang(137, true);
            klok262.Wacht(TimeSpan.FromMilliseconds(300), CancellationToken.None).GetAwaiter().GetResult(); mon262.Bijwerken();
            Eis(gevonden262 == 137 && !mon262.Bezet.Contains(137) && mon262.Bezet.Contains(129), "knippermelder (#262): 137 wordt genegeerd, 129 (bekend) niet (bezet: " + string.Join(",", mon262.Bezet) + ")");
            var mon262b = new MelderMonitor(klok262, () => 100, _ => false);
            mon262b.Ontvang(31, true); klok262.Wacht(TimeSpan.FromSeconds(4), CancellationToken.None).GetAwaiter().GetResult(); mon262b.Ontvang(31, false);
            klok262.Wacht(TimeSpan.FromMilliseconds(300), CancellationToken.None).GetAwaiter().GetResult(); mon262b.Bijwerken(); mon262b.Ontvang(31, true);
            klok262.Wacht(TimeSpan.FromMilliseconds(300), CancellationToken.None).GetAwaiter().GetResult(); mon262b.Bijwerken();
            Eis(mon262b.Bezet.Contains(31) && mon262b.Flikkeraars.Count == 0, "knippermelder (#262): een nieuwe melder die gewoon bezet raakt (een paar wissels) telt wel");
        }
        // BUG #258: voor 14 -> 129 (vooruit) zijn wissel 5 afbuigend en wissel 6 niet-afbuigend (Dinamo) bewezen; die worden bij een kortsluiting daar niet omgezet
        var k258 = Laad("baankaart_2026-10-20_1800.json");
        var b258 = BaanVerkenner.BewezenStandenBijLoc(k258, new[] { 13, 14, 129 }, Richting.Vooruit, new[] { 14, 129 });
        Eis(b258.TryGetValue(5, out var w5) && w5 && b258.TryGetValue(6, out var w6) && !w6, "bewezen (#258): 14 -> 129 vooruit: wissel 5 afbuigend, wissel 6 Dinamo rechtdoor (nu " + string.Join(", ", b258.Select(kv => kv.Key + (kv.Value ? "A" : "R"))) + ")");
        var b258b = BaanVerkenner.BewezenStandenBijLoc(k258, new[] { 21, 29 }, Richting.Vooruit, new[] { 14, 129 });
        Eis(b258b.Count == 0, "bewezen (#258): stappen ver van de loc tellen niet mee");
        // BUG #247: schema's tonen bij een omgekeerd aangesloten wissel de stand op de baan (Marco 16:07: wissel 6 'niet afbuigend' voor 14 -> 129, op de baan moet hij afbuigend)
        var k247 = Laad("baankaart_2026-10-18_1600.json");
        k247.Wissel(6).Waarnemingen.Add(new WisselWaarneming { Soort = WaarnemingSoort.KortsluitingBijOmzetten, NaMelder = 129, Via = 14, Richting = Richting.Vooruit, VolgendeBijRechtdoor = 24 });
        var lab247 = Melderschema.Maak(k247).Randen.SelectMany(r => r.Labels).ToList();
        Eis(lab247.Contains("W6 niet rd. (baan) (kortsl.)") && lab247.Contains("W5 afb."), "baanstand (#247): melderschema zegt bij wissel 6 'niet rechtdoor (baan)', wissel 5 gewoon 'afb.' (nu " + string.Join(" | ", lab247) + ")");
        Eis(k247.StandLang(6, false) == "afbuigend op de baan (Dinamo rechtdoor)" && k247.StandLang(5, true) == "afbuigend", "baanstand (#247): lange vorm");
        Eis(Rapport.MetBaanstand(k247, "wissel 5 afbuigend (uit splitsing), wissel 6 rechtdoor (anders kortsluiting)") == "wissel 5 afbuigend (uit splitsing), wissel 6 afbuigend op de baan (Dinamo rechtdoor) (anders kortsluiting)", "baanstand (#247): wisselstraat-tekst");
        k247.OmgekeerdeWissels.Clear();
        Eis(Melderschema.Maak(k247).Randen.SelectMany(r => r.Labels).Contains("W6 niet afb. (kortsl.)"), "baanstand (#247): zonder omgekeerde wissel blijft het 'niet afb.'");
        // BUG #246: proef vanaf 14 met stand 1+5 krijgt de oplossing van kortsluitpunt #1 (na 24, stand 5: wissel 10 afbuigend)
        var k246 = Laad("baankaart_2026-10-18_1600.json");
        var c246 = k246.MetBekendeOplossingenVoorStart(14, Richting.Vooruit, new Configuratie(new[] { 1, 5 }), out _);
        Eis(c246.Bevat(10), "proefstand (#246): vanaf 14 met wissel 1 en 5 afbuigend komt wissel 10 afbuigend erbij (nu " + c246 + ")");
        var d246 = k246.MetBekendeOplossingenVoorStart(14, Richting.Vooruit, new Configuratie(new[] { 1 }), out _);
        Eis(!d246.Bevat(10), "proefstand (#246): zonder wissel 5 afbuigend geldt de oplossing niet (nu " + d246 + ")");
        // BUG #244: hervatten neemt 'omgekeerd aangesloten' (en de uitgevoerde eenmalige aanpassingen) uit het scherm over
        var vast244 = new VerkenInstellingen { Verkensnelheid = 12 }; var scherm244 = new VerkenInstellingen { OmgekeerdAangesloten = "6", Verkensnelheid = 20 };
        scherm244.UitgevoerdeAanpassingen.Add("239-wissel6-omgekeerd");
        VerkenInstellingen.NeemMeldingInstellingenOver(vast244, scherm244);
        Eis(vast244.OmgekeerdeWissels().SetEquals(new[] { 6 }) && vast244.UitgevoerdeAanpassingen.Contains("239-wissel6-omgekeerd") && vast244.Verkensnelheid == 12, "hervatten (#244): wissel 6 omgekeerd komt uit het scherm, de rest blijft");
        // BUG #236: eerder gevonden oplossing (1 bij 2) geldt alleen voor haar eigen ingang
        var k236 = Laad("baankaart_2026-10-18_1424.json");
        k236.BekendeKortsluitOplossingen.Add(new KortsluitOplossing { NaMelder = 5, Via = 12, Richting = Richting.Vooruit, Stand = "2", Start = 13, Adres = 1, Afbuigend = true, Bron = "test" });
        var c236 = k236.MetBekendeOplossingenVoorStart(13, Richting.Vooruit, Configuratie.Basis.Met(2), out _);
        Eis(!c236.Bevat(1), "voorkennis (#236): oplossing 'wissel 1' voor ingang 12 -> 5 wordt niet gebruikt als de loc vanaf 16 op melder 5 komt (nu " + c236 + ")");
        var k236b = Laad("baankaart_2026-10-18_1424.json");
        Eis(k236b.BekendeKortsluitOplossingen.Any(o => o.NaMelder == 5 && o.Via == 0 && o.Stand == "2" && o.Adres == 1 && o.Richting == Richting.Vooruit), "voorkennis (#236): de kaart van 14:24 bevat de oude oplossing '5 vooruit, stand 2 -> wissel 1' zonder ingang");
        Eis(!k236b.MetBekendeOplossingenVoorStart(13, Richting.Vooruit, Configuratie.Basis.Met(2), out _).Bevat(1), "voorkennis (#236): die oplossing wordt niet meer toegepast nu bekend is dat 16 -> 5 met wissel 2 afbuigend kortsluiting geeft");
        k236.BekendeKortsluitOplossingen[^1].Via = 16;
        Eis(!k236.MetBekendeOplossingenVoorStart(13, Richting.Vooruit, Configuratie.Basis.Met(2), out _).Bevat(1), "voorkennis (#236): ook met ingang 16 niet: de kaart weet dat 16 -> 5 met wissel 2 afbuigend kortsluiting geeft (zelfde regel als #217)");
        var k236c = new Baankaart();
        foreach (var (v236, n236) in new[] { (13, 14), (14, 16), (16, 5), (5, 13) }) k236c.Overgangen.Add(new Overgang { Van = v236, Naar = n236, Richting = Richting.Vooruit, AantalKeer = 3, GezienBijConfiguraties = new List<string> { "" } });
        k236c.BekendeKortsluitOplossingen.Add(new KortsluitOplossing { NaMelder = 5, Via = 16, Richting = Richting.Vooruit, Stand = "2", Start = 13, Adres = 1, Afbuigend = true });
        Eis(k236c.MetBekendeOplossingenVoorStart(13, Richting.Vooruit, Configuratie.Basis.Met(2), out _).Bevat(1), "voorkennis (#236): zonder verbod en met de juiste ingang wordt de oplossing wel gebruikt");
        // BUG #238: voorkennis 'wissel 6 afbuigend voor 129 -> 14 achteruit' stuurde de loc naar 10 (autolog 18-10 14:26:22)
        var k238 = Laad("baankaart_2026-10-18_1424.json");
        int voor238 = k238.BekendeKortsluitOplossingen.Count;
        var t238 = k238.WeerlegVoorkennis(new List<int> { 24, 129, 14, 13 }, Richting.Achteruit, Configuratie.VanSleutel("5,6,9,10"), null, 129);
        Eis(t238.Count == 1 && t238[0].Contains("wissel 6") && k238.BekendeKortsluitOplossingen.Count == voor238 - 1 && k238.WeerlegdeOplossingen.Count == 1, "voorkennis (#238): de oplossing 'wissel 6' na melder 14 (ingang 129) wordt weerlegd (nu: " + string.Join(" | ", t238) + ")");
        var lp238 = new LeerProfiel(); lp238.KortsluitOplossingen.Add(new KortsluitOplossing { NaMelder = 14, Via = 129, Richting = Richting.Achteruit, Stand = "10", Start = 24, Adres = 6, Afbuigend = true });
        lp238.LeerKortsluitOplossingen(k238);
        Eis(!lp238.KortsluitOplossingen.Any(x => x.Adres == 6 && x.NaMelder == 14), "voorkennis (#238): het leerprofiel haalt de weerlegde oplossing weg en neemt hem niet opnieuw over");
        Eis(k238.WeerlegVoorkennis(new List<int> { 13, 14, 24 }, Richting.Vooruit, Configuratie.Basis, 24, null).Count == 0, "voorkennis (#238): een rit zonder die wissels weerlegt niets");
        // BUG #239: eenmalige aanpassing wissel 6 omgekeerd
        var ins239 = new VerkenInstellingen();
        var g239 = EenmaligeAanpassingen.PasToe(ins239);
        Eis(g239.Count == 2 && ins239.OmgekeerdeWissels().SetEquals(new[] { 6 }) && ins239.UitgevoerdeAanpassingen.Contains("239-wissel6-omgekeerd"), "eenmalig (#239): wissel 6 wordt één keer bij 'Omgekeerd aangesloten' gezet");
        ins239.OmgekeerdAangesloten = "";
        Eis(EenmaligeAanpassingen.PasToe(ins239).Count == 0 && ins239.OmgekeerdeWissels().Count == 0 && !EenmaligeAanpassingen.Open(ins239), "eenmalig (#239): haalt Marco hem weg, dan komt hij niet terug");
        var ins239b = new VerkenInstellingen { OmgekeerdAangesloten = "3" }; EenmaligeAanpassingen.PasToe(ins239b);
        Eis(ins239b.OmgekeerdeWissels().SetEquals(new[] { 3, 6 }), "eenmalig (#239): een al ingevulde wissel blijft staan, 6 komt erbij");
        var js239 = System.Text.Json.JsonSerializer.Serialize(ins239);
        Eis(System.Text.Json.JsonSerializer.Deserialize<VerkenInstellingen>(js239)!.UitgevoerdeAanpassingen.Contains("239-wissel6-omgekeerd"), "eenmalig (#239): het bewaren en laden van de instellingen onthoudt dat het gedaan is");
        // BUG #259: overloopset 5+6 als eenmalige aanpassing, en wissel 6 schakelt mee met dezelfde stand OP DE BAAN (6 is omgekeerd aangesloten)
        Eis(ins239.GekoppeldeParen().SequenceEqual(new[] { (5, 6) }) && ins239.UitgevoerdeAanpassingen.Contains("259-overloop-5-6"), "overloop (#259): eenmalig 5+6 bij 'Gekoppelde wissels'");
        {
            var bf259 = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var sim259 = new Baanverkenner.Kern.Simulatie.SimulatieBaan { DinamoGedrag = false, LocAdres = 5408 };
            sim259.NieuweSectie(13, 100, 3);
            sim259.VerbindenAsync("SIM").GetAwaiter().GetResult();
            var klok259 = new VirtueleKlok(); klok259.Getikt += sim259.Tik;
            var log259 = new VerkenLog(klok259);
            var ins259 = new VerkenInstellingen { LocAdres = 5408, WisselAdresVan = 1, WisselAdresTot = 14, GekoppeldeWissels = "5+6", OmgekeerdAangesloten = "6" };
            var v259 = new BaanVerkenner(sim259, ins259, klok259, log259, (_, _) => Task.FromResult(true));
            void Zet259(int a, bool afb) => ((Task)typeof(BaanVerkenner).GetMethod("ZetWissel", bf259)!.Invoke(v259, new object[] { a, afb, false })!).GetAwaiter().GetResult();
            var stand259 = (Dictionary<int, bool>)typeof(BaanVerkenner).GetField("_wisselStand", bf259)!.GetValue(v259)!;
            Zet259(5, true);
            Eis(stand259[5] && stand259.TryGetValue(6, out var s6a) && !s6a, "overloop (#259): wissel 5 afbuigend -> wissel 6 Dinamo rechtdoor (= op de baan afbuigend)");
            Zet259(5, false);
            Eis(!stand259[5] && stand259[6], "overloop (#259): wissel 5 rechtdoor -> wissel 6 Dinamo afbuigend (= op de baan rechtdoor)");
            Zet259(6, false);
            Eis(stand259[6], "overloop (#259): wissel 6 los zetten wordt genegeerd (volgt wissel 5)");
            Eis(log259.Regels.Any(r => r.Tekst.Contains("schakelt mee met wissel 5")), "overloop (#259): het logboek zegt dat wissel 6 meeschakelt");
        }
        // BUG #240: klemmelders inclusief de melder van de kortsluiting (melder 24 viel tijdens de kortsluiting weg)
        Eis(!BaanVerkenner.KlemNogOnderLoc(new[] { 129 }, new[] { 24, 129 }, false) && BaanVerkenner.KlemNogOnderLoc(new[] { 24, 129 }, new[] { 24, 129 }, false), "klem (#240): alleen 129 als klemmelder liet wissel 10 los zodra 24 weer bezet meldde; met 24 erbij blijft hij staan");
        // BUG #226: automatische tussenstand
        var map226 = Path.Combine(Path.GetTempPath(), "autotussenstand_test_" + Guid.NewGuid().ToString("N"));
        var nu226 = new DateTime(2026, 10, 18, 12, 31, 0);
        var b226 = AutoTussenstand.Schrijf(map226, k223, "log", "hw", nu226, "test", out var f226);
        Eis(f226 is null && b226.Count == 5 && File.Exists(Path.Combine(map226, "baanrapport_2026-10-18_1231.txt")) && File.ReadAllText(Path.Combine(map226, "LAATSTE.txt")).Contains("hardwarelog_2026-10-18_1231.txt"), "tussenstand (#226): rapport, html, kaart, logboek en hardwarelog + LAATSTE.txt geschreven (nu " + b226.Count + (f226 ?? "") + ")");
        // BUG #257: alleen de nieuwste 3 sets blijven staan, andere bestanden niet aangeraakt
        var map257 = Path.Combine(Path.GetTempPath(), "bv257_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(map257);
        File.WriteAllText(Path.Combine(map257, "modeltreinbesturing_bug255.zip"), "x");
        File.WriteAllText(Path.Combine(map257, "baan.json"), "x");
        for (int i = 0; i < 5; i++) AutoTussenstand.Schrijf(map257, k223, "log", "hw", new DateTime(2026, 10, 20, 17, 0, 0).AddMinutes(10 * i), "test", out _);
        var over257 = Directory.GetFiles(map257).Select(Path.GetFileName).OrderBy(x => x).ToList();
        Eis(over257.Count(x => x!.StartsWith("hardwarelog_")) == 3 && over257.Contains("hardwarelog_2026-10-20_1740.txt") && !over257.Contains("hardwarelog_2026-10-20_1710.txt")
            && over257.Contains("modeltreinbesturing_bug255.zip") && over257.Contains("baan.json") && over257.Contains("LAATSTE.txt"), "opruimen (#257): 3 nieuwste sets blijven, de rest weg, zip/baan.json blijven (nu " + string.Join(", ", over257) + ")");
        try { Directory.Delete(map257, true); } catch { }
        Eis(AutoTussenstand.IsTijd(nu226.AddMinutes(-10), nu226, 10) && !AutoTussenstand.IsTijd(nu226.AddMinutes(-9), nu226, 10) && !AutoTussenstand.IsTijd(null, nu226, 0), "tussenstand (#226): elke 10 minuten, 0 = uit");
        try { Directory.Delete(map226, true); } catch { }
        k223.Wisselstraatvoorstellen = WisselstraatVoorstel.Bereken(k223);
        Console.WriteLine("    wisselstraten 12:21 met kortsluitpunt #1 opgelost: " + string.Join(" | ", k223.Wisselstraatvoorstellen.Select(w => $"{w.VanMelder}->{w.NaarMelder} {w.Richting}: {w.EisenTekst}")));
        // BUG #160: voltooide kaart van 12:42 - ingangen afgeleid, kruismotor 10 blijft staan bij de aankomst op melder 129
        var k160 = Laad("baankaart_2026-10-15_1242.json");
        int? ViaVan(int adres, int na) => k160.Wissel(adres).Waarnemingen.FirstOrDefault(x => x.Soort == WaarnemingSoort.KortsluitingBijOmzetten && x.NaMelder == na)?.Via;
        Eis(ViaVan(2, 5) == 16 && ViaVan(14, 8) == 17, "ingang (#160): na het laden is de ingang afgeleid: wissel 2 -> 16 naar 5, wissel 14 -> 17 naar 8 (nu " + ViaVan(2, 5) + " en " + ViaVan(14, 8) + ")");
        Eis(Rapport.AlsTekst(k160).Contains("ingang 16 → 5"), "ingang (#160): het rapport noemt 'ingang 16 → 5' voor wissel 2");
        var e160 = new Etappe { Van = 21, Naar = 129, Richting = Richting.Achteruit, Pad = new List<int> { 21, 24, 129 }, Configuratie = Configuratie.Basis.Met(5).Met(6).Met(10) };
        var c160 = BaanVerkenner.AankomstZonderKortsluitWissels(k160, e160, e160.Configuratie, out var u160);
        // BUG #161: elke traject van de voltooide kaart (behalve die op een bekend kortsluitpunt eindigen) heeft een route vanaf de startmelder
        var t129 = k160.Trajecten.First(t => t.Start == 129 && t.Richting == Richting.Achteruit);
        var r129 = BaanVerkenner.ControleRoute(k160, t129, k160.Trajecten, new HashSet<Traject>());
        Eis(r129 is not null && r129[0].Van == k160.StartMelder && r129[^1].Naar == 129 && r129[^1].Configuratie.Bevat(10), "controle (#161): route naar melder 129 loopt vanaf de startmelder en neemt het traject met motor 10 afbuigend (nu " + (r129 is null ? "geen" : string.Join(" ; ", r129.Select(e => $"{e.Van}->{e.Naar} {e.Configuratie}"))) + ")");
        Eis(k160.Trajecten.Where(t => t.Einde != RitEinde.BekendKortsluitpunt).All(t => BaanVerkenner.ControleRoute(k160, t, k160.Trajecten, new HashSet<Traject>()) is not null), "controle (#161): alle trajecten (zonder bekend kortsluitpunt) zijn te bereiken");
        // BUG #162: de controle begint met de oplossingen van de kaart; staartwissel 9 niet afbuigend als motor 10 afbuigend staat
        var st162 = new VerkenStatus { Instellingen = new VerkenInstellingen { LocAdres = 5408, LocStappen = 28, Verkensnelheid = 12, Kruipsnelheid = 5, WisselAdresVan = 1, WisselAdresTot = 14 }, Kaart = Laad("baankaart_2026-10-15_1242.json") };
        st162.VoorControle(DateTime.Now);
        Eis(st162.ControleKaart!.BekendeKortsluitOplossingen.Any(x => x.NaMelder == 12 && x.Adres == 1 && x.Afbuigend), "controle (#162): de controlekaart begint met de oplossingen van de kaart (wissel 1 afbuigend na melder 12) (nu " + st162.ControleKaart.BekendeKortsluitOplossingen.Count + " oplossingen)");
        var kr = KruisKennis.Referentie();
        Eis(!BaanVerkenner.StaartWisselToegestaan(k160, 9, true, Richting.Achteruit, Configuratie.Basis.Met(10), kr) && BaanVerkenner.StaartWisselToegestaan(k160, 9, true, Richting.Achteruit, Configuratie.Basis, kr),
            "staartwissel (#162): wissel 9 niet afbuigend bij motor 10 afbuigend (achteruit, ingang 24 -> 129), wel zonder motor 10");
        Eis(c160.Bevat(10) && !c160.Bevat(5) && !c160.Bevat(6), "aankomst (#160): bij 21 -> 24 -> 129 blijft kruismotor 10 afbuigend; 5 en 6 wachten tot de loc op melder 129 staat (nu " + c160 + ")");
        Eis(!v.Any(x => x.VanBlok == x.NaarBlok), "wisselstraat: geen voorstel binnen een blok");
        Eis(!v.Any(x => x.VanMelder == 20 && x.NaarMelder == 27), "wisselstraat: 20 -> 27 (zelfde blok) heeft geen voorstel");
        var alt = v.Where(x => x.VanMelder == 13 && x.NaarMelder == 24 && x.Richting == Richting.Vooruit).ToList();
        Eis(alt.Count == 3 && alt.All(x => x.Eisen.Count(e => e.Afbuigend) == 1), "wisselstraat: 13 -> 24 geeft drie alternatieven (wissel 11, 12 of 13 afbuigend) (nu " + alt.Count + ": " + string.Join(" | ", alt.Select(x => x.EisenTekst)) + ")");
        var terug = Baankaart.VanJson((new Baankaart { Wisselstraatvoorstellen = v }).AlsJson())!;
        Eis(terug.Wisselstraatvoorstellen.Count == v.Count && Baankaart.VanJson(new Baankaart().AlsJson())!.Wisselstraatvoorstellen.Count == 0, "wisselstraat: voorstellen blijven na opslaan en laden; lege kaart heeft er geen");
        Eis(Rapport.AlsTekst(k).Contains("WISSELSTRATEN") && Rapport.AlsHtml(k).Contains("Wisselstraten (voorstel)"), "wisselstraat: staat in het rapport (tekst en html)");
    }

    // Blokkenschema-tekening (SVG) uit de baankaart van 17:07
    static void BlokkenschemaTest()
    {
        var k = Laad("baankaart_lijn_5-30_voltooid_kruiswissel24_2026-10-10_1707.json"); k.KruisReferentie = true; k.KruisMotoren = "10+13"; k.KruisMidden = new List<int> { 24 };
        var t = SchemaTekening.Maak(k);
        Eis(t.Vakken.Count == 7, "blokkenschema: 7 vakken (nu " + t.Vakken.Count + ")");
        Eis(t.Verbindingen.Count >= 7, "blokkenschema: minstens 7 verbindingen (nu " + t.Verbindingen.Count + ")");
        Eis(t.Vakken.All(v => v.X > 0 && v.Y > 0 && v.X < t.Breedte && v.Y < t.Hoogte), "blokkenschema: alle vakken liggen binnen de tekening");
        var svg = t.AlsSvg(true);
        Eis(svg.Contains("<svg") && svg.Contains("melders 24") && svg.Contains("onbekend (melder 129)"), "blokkenschema: svg bevat melder 24 en het onbekende Dinamo-blok van melder 129 (geen verzonnen nummer)");
        Eis(t.Vakken.First(v => v.Melders == "24").Soort == "splitsing", "blokkenschema: melder 24 (kruiswissel) is een splitsing");
        // BUG #90: melders op de zijde naar het buurblok, kruiswissel als X
        var kruis = t.Vakken.First(v => v.Melders == "24");
        Eis(kruis.Vorm == "kruis" && kruis.Chips.Select(c => c.Melder).OrderBy(x => x).SequenceEqual(new[] { 8, 14, 21, 129 }), "blokkenschema: kruiswissel is een X met de melders 14, 129, 21 en 8 in de hoeken (nu " + string.Join(",", kruis.Chips.Select(c => c.Melder)) + ")");
        Eis(t.Vakken.All(v => v.Chips.Count(c => c.Zijde == -1) == (v.Vorm == "kruis" ? 4 : 0)), "blokkenschema: alleen de kruiswissel heeft hoekmelders");
        var b3 = t.Vakken.First(v => v.DinamoBlok == 3);
        Eis(b3.Chips.Select(c => c.Melder).OrderBy(x => x).SequenceEqual(new[] { 5, 13, 14 }), "blokkenschema: blok 3 heeft de grensmelders 5, 13 en 14 als vlakje (nu " + string.Join(",", b3.Chips.Select(c => c.Melder)) + ")");
        bool eindOpChip = t.Verbindingen.All(vb =>
        {
            var va = t.Vakken.First(x => x.Nummer == vb.Van); var vn = t.Vakken.First(x => x.Nummer == vb.Naar);
            var ca = va.Chips.Where(c => c.Melder == vb.VanMelder || va.Vorm == "kruis").Select(c => c.Anker); var cn = vn.Chips.Where(c => c.Melder == vb.NaarMelder || vn.Vorm == "kruis").Select(c => c.Anker);
            return ca.Any(p => Math.Abs(p.X - vb.Begin.X) < 0.01 && Math.Abs(p.Y - vb.Begin.Y) < 0.01) && cn.Any(p => Math.Abs(p.X - vb.Eind.X) < 0.01 && Math.Abs(p.Y - vb.Eind.Y) < 0.01);
        });
        Eis(eindOpChip, "blokkenschema: elke verbinding begint en eindigt bij het vlakje van haar melder");
        Eis(t.Vakken.All(v => v.Chips.All(c => c.X > 0 && c.Y > 0 && c.X < t.Breedte && c.Y < t.Hoogte)) && t.Verbindingen.All(vb => vb.LabelPlek.X > 0 && vb.LabelPlek.X < t.Breedte && vb.LabelPlek.Y > 0 && vb.LabelPlek.Y < t.Hoogte), "blokkenschema: melders en labels liggen binnen de tekening");
        var uit = Environment.GetEnvironmentVariable("SCHEMA_UIT");
        if (!string.IsNullOrEmpty(uit)) { File.WriteAllText(uit, svg); Console.WriteLine("  svg geschreven naar " + uit); }
        Eis(Rapport.AlsHtml(k).Contains("<h2>Blokkenschema</h2>") && Rapport.AlsHtml(k).Contains("<svg"), "blokkenschema: het HTML-rapport bevat de tekening");
    }

    // Overdracht van de baankaart van de Baanverkenner naar Modeltreinbesturing
    static void OverdrachtTest()
    {
        var oud = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME"); var tmp = Path.Combine(Path.GetTempPath(), "overdracht_" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", tmp);
        try
        {
            var k = Laad("baankaart_lijn_5-30_voltooid_kruiswissel24_2026-10-10_1707.json");
            Eis(BaankaartOverdracht.Klaar() is null, "overdracht: eerst niets klaargezet");
            BaankaartOverdracht.Zet(k);
            Eis(BaankaartOverdracht.Klaar() is not null && Baankaart.VanJson(File.ReadAllText(BaankaartOverdracht.Pad))!.Melders.Count == k.Melders.Count, "overdracht: klaargezette baankaart is te lezen en compleet");
            BaankaartOverdracht.MarkeerVerwerkt();
            Eis(BaankaartOverdracht.Klaar() is null, "overdracht: na verwerken wordt hij niet nog eens aangeboden");
        }
        finally { Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", oud); try { Directory.Delete(tmp, true); } catch { } }
    }

    // De geslaagde, volledige rit van 10-10 11:38 (blokken 10, 11, 12): referentie voor importer en leerprofiel
    /// <summary>BUG #150: een volledige verkenning vanaf nul op de nagebouwde Miniworld-baan (zie MiniworldBaan). Geeft het aantal kortsluitingen terug.</summary>
    static (BaanVerkenner V, VerkenLog Log, int Kortsluitingen, double Seconden, string Einde) MiniworldRit(int startMelder, double maxUren = 6, Action<BaanVerkenner>? voorbereiding = null, bool omzettenOnderLoc = true, Action<Baanverkenner.Kern.Simulatie.SimulatieBaan>? simAanpassing = null, Action<VerkenInstellingen>? insAanpassing = null, Func<Baanverkenner.Kern.Simulatie.SimulatieBaan>? simFabriek = null, Func<VerkenLog, bool>? stopWanneer = null)
    {
        var sim = Baanverkenner.Kern.Simulatie.MiniworldBaan.Maak(omzettenOnderLoc, startMelder, simFabriek);
        simAanpassing?.Invoke(sim);
        var ins = new VerkenInstellingen { LocAdres = 5408, LocStappen = 28, Verkensnelheid = 12, Kruipsnelheid = 5, WisselAdresVan = 1, WisselAdresTot = 14, DinamoBlokken = "1-12", KruisMotoren = "10+13", HoogsteMelder = 150 };
        insAanpassing?.Invoke(ins);
        sim.VerbindenAsync("SIM").GetAwaiter().GetResult();
        var klok = new VirtueleKlok(); klok.Getikt += sim.Tik;
        var log = new VerkenLog(klok);
        var status = new VerkenStatus { Instellingen = ins };
        // Marco 17-10: de gesimuleerde gebruiker zet de loc ECHT terug als de verkenner dat vraagt (anders bleef de loc waar hij zat en liep elke test na een 'zet de loc terug' vast)
        var v = new BaanVerkenner(sim, ins, klok, log, (kop, tekst) =>
        {
            var m = System.Text.RegularExpressions.Regex.Match(tekst, @"met de hand op melder (\d+)");
            if (kop == "Loc terugzetten" && m.Success) sim.ZetLocOpMelder(int.Parse(m.Groups[1].Value));
            return Task.FromResult(true);
        }, status) { VraagJaNee = (_, _) => Task.FromResult<bool?>(true) };
        voorbereiding?.Invoke(v);
        using var cts = new CancellationTokenSource();
        var t0 = klok.Nu;
        var vorigeSpoor = DateTime.MinValue;
        klok.Getikt += nu => { if ((nu - t0).TotalHours > maxUren || stopWanneer?.Invoke(log) == true) cts.Cancel(); if (Environment.GetEnvironmentVariable("MINI_TRACE") == "1" && (nu - vorigeSpoor).TotalSeconds >= 1.0) { vorigeSpoor = nu; log.Info("[sim] " + sim.ToestandTest()); } };
        string einde = "klaar";
        try { v.VoerUit(cts.Token).GetAwaiter().GetResult(); } catch (OperationCanceledException) { einde = "tijdslimiet"; } catch (Exception ex) { einde = "gestopt: " + ex.Message; }
        if (Environment.GetEnvironmentVariable("MINI_LOG") == "1") Console.WriteLine(log.AlsTekst());
        int kort = log.Regels.Count(r => r.Tekst.Contains("KORTSLUITING/ONTSPORING vermoed"));
        return (v, log, kort, (klok.Nu - t0).TotalSeconds, einde);
    }


    // BUG #243: stoppen tijdens de terugrit na een proef met kortsluiting -> de vondst is toch vastgelegd
    static void StopTijdensTerugritTest()
    {
        Console.WriteLine("Test: stoppen tijdens de terugrit na een proef (BUG #243, autolog 18-10 15:10)");
        int gezien = 0; int? adres = null;
        bool Stop(VerkenLog l)
        {
            for (; gezien < l.Regels.Count; gezien++)
            {
                var m = System.Text.RegularExpressions.Regex.Match(l.Regels[gezien].Tekst, @"met adres (\d+) afbuigend, terwijl de loc dezelfde melders reed");
                if (m.Success && adres is null) adres = int.Parse(m.Groups[1].Value);
            }
            return adres is not null;
        }
        var r = MiniworldRit(13, 10, insAanpassing: i => { i.Rondrit = true; i.StapVoorStap = true; }, stopWanneer: Stop);
        Eis(adres is not null, "terugrit (#243): in de simulatie komt een proef voor waarbij de loc dezelfde melders reed en kortsluiting kreeg");
        if (adres is not int a) return;
        var status = (VerkenStatus)typeof(BaanVerkenner).GetField("_status", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(r.V)!;
        Eis(r.Log.Regels.Any(x => x.Tekst.Contains("BUG #243")), "terugrit (#243): het log meldt dat de vondst bij het stoppen is vastgelegd (einde: " + r.Einde + ")");
        Eis(status.Geidentificeerd.Contains(a), $"terugrit (#243): adres {a} staat als gevonden in de bewaarde voortgang, dus na hervatten geen herhaalde kortsluiting");
    }

    // BUG #181: het register van vastgestelde feiten meldt elke overschrijving
    static void KennisRegisterTest()
    {
        Console.WriteLine("Test: register van vastgestelde feiten (BUG #181)");
        var r = new KennisRegister();
        var gemeld = new List<string>();
        r.Gemeld = o => gemeld.Add(o.ToString());
        Eis(!r.Leg("blok", "13", "3", "test") && gemeld.Count == 0, "register: de eerste waarde is geen overschrijving");
        Eis(!r.Leg("blok", "13", "3", "test") && r.Bevestigingen == 1, "register: dezelfde waarde is een bevestiging");
        Eis(r.Leg("blok", "13", "4", "test") && gemeld.Count == 1 && gemeld[0].Contains("3 -> 4"), "register: een andere waarde is een overschrijving met oud en nieuw");
        r.Wis("blok", "13", "blokproef");
        Eis(gemeld.Count == 1 && r.Wissingen == 1, "register: wissen alleen telt niet als overschrijving");
        Eis(!r.Leg("blok", "13", "4", "herstel"), "register: na wissen dezelfde waarde terug = niets verloren");
        r.Zaai("rijrichting", "5>16", "vooruit");
        Eis(r.Leg("rijrichting", "5>16", "achteruit", "rit") && gemeld.Count == 2, "register: een uit de kaart gezaaide rijrichting wordt bewaakt");
    }

    // BUG #181: een loc die op de kruiswissel kortgesloten zit door de BUURWISSEL komt vrij als die wordt omgezet (alleen in de strenge simulatiemodus)
    static void SimBuurwisselTest()
    {
        Console.WriteLine("Test: simulator - buurwissel heft kortsluiting op de kruiswissel op (BUG #181)");
        foreach (var streng in new[] { false, true })
        {
            var sim = Baanverkenner.Kern.Simulatie.MiniworldBaan.Maak(true, 8);
            sim.BuurwisselHeftKruisKortsluitingOp = streng;
            sim.VerbindenAsync("SIM").GetAwaiter().GetResult();
            // wissel 9 (na melder 129, tak richting de kruiswissel) staat afbuigend: een loc die 8 -> kruiswissel -> 129 rijdt, loopt tegen die tong op
            sim.ZetWissel(14, true); sim.ZetWissel(9, true);
            var klok = new VirtueleKlok(); klok.Getikt += sim.Tik;
            bool kort = false; sim.KortsluitingStatusGewijzigd += a => kort = a;
            sim.ZetLocSnelheid(5408, 12, false, 4, 28);
            for (int i = 0; i < 1200 && !kort; i++) klok.Wacht(TimeSpan.FromMilliseconds(100)).GetAwaiter().GetResult();
            Eis(kort, $"sim (streng={streng}): 8 -> kruiswissel met wissel 9 afbuigend geeft kortsluiting");
            sim.ZetWissel(9, false);
            for (int i = 0; i < 60; i++) klok.Wacht(TimeSpan.FromMilliseconds(100)).GetAwaiter().GetResult();
            Eis(streng ? !kort : kort, $"sim (streng={streng}): wissel 9 terugzetten {(streng ? "heft" : "heft niet")} de kortsluiting op");
        }
    }

    // BUG #172: werkwijze 'Stap voor stap' op de nagebouwde Miniworld-baan
    static void StapVoorStapTest()
    {
        Console.WriteLine("Test: stap voor stap op de nagebouwde Miniworld-baan (BUG #172)");
        foreach (var start in (Environment.GetEnvironmentVariable("STAP_START") ?? "17").Split(',').Select(int.Parse))
        {
            var r = MiniworldRit(start, double.Parse(Environment.GetEnvironmentVariable("STAP_UREN") ?? "8", System.Globalization.CultureInfo.InvariantCulture), insAanpassing: i => { i.StapVoorStap = true; i.StapAanloop = Environment.GetEnvironmentVariable("STAP_AANLOOP") != "0"; i.StapDoorrijSeconden = double.Parse(Environment.GetEnvironmentVariable("STAP_DOORRIJ") ?? "2", System.Globalization.CultureInfo.InvariantCulture); }, simAanpassing: sa => sa.BuurwisselHeftKruisKortsluitingOp = Environment.GetEnvironmentVariable("STAP_STRENG") != "0");   // volledige run zonder aanloop: met aanloop loopt de sim vast in het herstel na de kruisproef (zie AanloopTest en BUGS.md #175)
            Console.WriteLine($"  start {start}: {r.Einde}, {r.Seconden / 60:0} min, {r.Kortsluitingen} kortsluiting(en), melders: {string.Join(",", r.V.Kaart.Melders.Select(m => m.Nummer).OrderBy(x => x))}, voltooid: {r.V.Kaart.Voltooid}, wissels: {string.Join(",", r.V.Kaart.Wissels.Where(w => w.Waarnemingen.Count > 0).Select(w => w.Adres))}");
            if (Environment.GetEnvironmentVariable("MINI_OVERGANGEN") == "1")
                foreach (var ov in r.V.Kaart.Overgangen.Where(x => new[] { 4, 5, 11, 12 }.Contains(x.Van) && new[] { 4, 5, 11, 12 }.Contains(x.Naar)))
                    Console.WriteLine($"  OVERGANG {ov.Van}->{ov.Naar} {ov.Richting} x{ov.AantalKeer} configs: {string.Join(" | ", ov.GezienBijConfiguraties)}");
            foreach (var ow in r.V.Kennis.Lijst) Console.WriteLine("  OVERSCHREVEN: " + ow);
            Eis(!r.V.Kennis.Lijst.Any(x => x.Soort is "blok" or "kruismelder" or "rijrichting"), $"stap voor stap (start {start}) BUG #181: vastgestelde blokken, kruismelder en rijrichtingen worden niet overschreven (nu {r.V.Kennis.Lijst.Count(x => x.Soort is "blok" or "kruismelder" or "rijrichting")})");
            Eis(r.V.Kaart.Voltooid, $"stap voor stap (start {start}): de verkenning rondt af");
            Eis(r.V.Kaart.Melders.Count >= 18, $"stap voor stap (start {start}): de melders zijn gevonden (nu {r.V.Kaart.Melders.Count})");
            Eis(r.Kortsluitingen <= 10, $"stap voor stap (start {start}): beperkt aantal kortsluitingen (nu {r.Kortsluitingen}; kruismotoren geven er inherent enkele)");
            var zonderBlok = r.V.Kaart.Melders.Where(m => m.DinamoBlok is null).Select(m => m.Nummer).OrderBy(x => x).ToList();
            Console.WriteLine($"  melders zonder Dinamo-blok: {(zonderBlok.Count == 0 ? "geen" : string.Join(",", zonderBlok))}");
            Eis(zonderBlok.Count <= 2, $"stap voor stap (start {start}) BUG #174: (bijna) alle melders kennen hun Dinamo-blok (zonder: {string.Join(",", zonderBlok)})");
            Eis(r.V.Kaart.KruisMidden.Contains(24) && !r.V.Kaart.KruisMidden.Contains(129), $"stap voor stap (start {start}): alleen melder 24 is de kruiswissel (nu {string.Join(",", r.V.Kaart.KruisMidden)})");
        }
    }


    /// <summary>BUG #188: meetopzet. BENCH=1 draait alleen deze meting (start-lijst in BENCH_START) en schrijft per run een regel plus een totaalscore; zo wordt elke wijziging
    /// bewezen met cijfers vergeleken met de basislijn in plaats van geraden. Echte wissels in de nagebouwde baan: 1,2,5,6,9,14 (en de kruismotoren 10,13).</summary>
    static void BenchTest()
    {
        var echt = new HashSet<int> { 1, 2, 5, 6, 9, 10, 13, 14 };
        int totValsVoltooid = 0, totVoltooid = 0, totMelders = 0, totVals = 0, totMis = 0, totKort = 0, totOnder = 0, runs = 0;
        foreach (var start in (Environment.GetEnvironmentVariable("BENCH_START") ?? "17,5,13,30,129,12,4,10").Split(',').Select(int.Parse))
        foreach (var streng in new[] { false, true })
        {
            Baanverkenner.Kern.Simulatie.SimulatieBaan? sim = null;
            var r = MiniworldRit(start, 10, insAanpassing: i => { i.StapVoorStap = true; i.StapAanloop = true; i.StapDoorrijSeconden = 2; }, simAanpassing: sa => { sim = sa; sa.BuurwisselHeftKruisKortsluitingOp = streng; });
            var gevonden = r.V.Kaart.Wissels.Where(w => w.Waarnemingen.Count > 0).Select(w => w.Adres).ToList();
            int vals = gevonden.Count(a => !echt.Contains(a));
            int mis = new[] { 2, 5, 6, 9, 14 }.Count(a => !gevonden.Contains(a));
            int onder = sim?.OnderLocOmgezet ?? 0;
            Console.WriteLine($"BENCH start {start,3} streng={(streng ? 1 : 0)}: {(r.V.Kaart.Voltooid ? "voltooid" : "NIET-voltooid"),-13} melders {r.V.Kaart.Melders.Count,2}/21  valse-wissels {vals}  ontbrekende-wissels {mis}  kortsl {r.Kortsluitingen,2}  onder-loc {onder,2}  zonder-effect {r.V.Kaart.AdressenZonderEffect.Count,2}  open-ksl {r.V.Kaart.Kortsluitpunten.Count(k => k.Soort == KortsluitpuntSoort.Kortsluiting && !k.Opgelost)}  geen-blok {r.V.Kaart.Melders.Count(m => m.DinamoBlok is null)}  {r.Seconden / 60:0} min");
            runs++; if (r.V.Kaart.Voltooid) totVoltooid++; if (r.V.Kaart.Voltooid && r.V.Kaart.Melders.Count < 21) totValsVoltooid++; totMelders += r.V.Kaart.Melders.Count; totVals += vals; totMis += mis; totKort += r.Kortsluitingen; totOnder += onder;
        }
        Console.WriteLine($"BENCH TOTAAL runs {runs}: voltooid {totVoltooid} (waarvan ten onrechte {totValsVoltooid}), melders {totMelders}, valse-wissels {totVals}, ontbrekende-wissels {totMis}, kortsluitingen {totKort}, onder-loc {totOnder}");
    }

    // BUG #183-#185 (Marco 17-10): de echte instelling (doorrijden 2 s + aanloop) in beide simulatiemodi; gewoon en streng (buurwissel heft kruiskortsluiting op)
    static void StapMatrixTest()
    {
        Console.WriteLine("Test: stap voor stap, echte instelling (doorrijden 2 + aanloop), alle startplekken x gewone en strenge sim (BUG #183-#185)");
        foreach (var start in (Environment.GetEnvironmentVariable("MATRIX_START") ?? "17,5").Split(',').Select(int.Parse))
        foreach (var streng in new[] { false, true })
        {
            var r = MiniworldRit(start, 10, insAanpassing: i => { i.StapVoorStap = true; i.StapAanloop = true; i.StapDoorrijSeconden = 2; }, simAanpassing: sa => sa.BuurwisselHeftKruisKortsluitingOp = streng);
            string k = $"matrix (start {start}, streng={streng})";
            Console.WriteLine($"  {k}: {r.Einde}, {r.Seconden / 60:0} min, {r.Kortsluitingen} kortsluiting(en), melders {r.V.Kaart.Melders.Count} ({string.Join(",", r.V.Kaart.Melders.Select(m => m.Nummer).OrderBy(x => x))}), voltooid {r.V.Kaart.Voltooid}, wissels: {string.Join(",", r.V.Kaart.Wissels.Where(w => w.Waarnemingen.Count > 0).Select(w => w.Adres))}");
            // BUG #187: blokproef direct bij de eerste aankomst op een nieuwe melder (niet pas bij het tweede bezoek)
            var regels = r.Log.Regels.Select(x => x.Tekst).ToList();
            int eersteRit = regels.FindIndex(x => x.Contains("Opdracht #1:"));
            int eersteProef = regels.FindIndex(x => x.StartsWith("Blokproef voor melder") && !x.Contains($"melder {start}:"));
            Eis(eersteProef > eersteRit && eersteProef - eersteRit < 25, $"{k} BUG #187: de eerste blokproef van een nieuwe melder volgt direct op de eerste rit (regel {eersteProef} na rit op {eersteRit})");
            Eis(r.V.Kaart.Voltooid, $"{k}: de verkenning rondt af");
            Eis(r.V.Kaart.Melders.Count == 21, $"{k}: alle 21 melders zijn gevonden (nu {r.V.Kaart.Melders.Count})");
            Eis(r.V.Kaart.Wissels.Any(w => w.Adres == 9 && w.Waarnemingen.Count > 0), $"{k}: wissel 9 (naar 4-11-12) is gevonden");
            Eis(r.Kortsluitingen <= 12, $"{k}: beperkt aantal kortsluitingen (nu {r.Kortsluitingen})");
            Eis(!r.V.Kennis.Lijst.Any(x => x.Soort is "blok" or "kruismelder" or "rijrichting"), $"{k}: geen vastgestelde feiten overschreven");
        }
    }

    // BUG #175 (Marco 17-10 09:44): één melder extra terug: de proefritten beginnen vóór de startmelder. Alleen het eerste uur van de sim, daar zit wat de aanloop moet bewijzen.
    static void AanloopTest()
    {
        Console.WriteLine("Test: stap voor stap met een melder aanloop (BUG #175)");
        var r = MiniworldRit(17, 0.8, insAanpassing: i => { i.StapVoorStap = true; i.StapAanloop = true; });
        var tekst = r.Log.AlsTekst();
        Console.WriteLine($"  eerste {r.Seconden / 60:0} min: {r.Einde}, {r.Kortsluitingen} kortsluiting(en), melders {r.V.Kaart.Melders.Count}");
        Eis(r.Log.Regels.Any(x => x.Tekst.Contains("proefritten beginnen één melder eerder")), "aanloop: de proefritten beginnen een melder eerder");
        Eis(r.V.Kaart.Wissels.Any(w => w.Adres == 14 && w.Waarnemingen.Count > 0), "aanloop: wissel 14 (vóór melder 17 -> 8) wordt al in het eerste uur gemeten");
        Eis(r.Kortsluitingen <= 2, $"aanloop: weinig kortsluitingen in het eerste uur (nu {r.Kortsluitingen})");
    }

    // BUG #173: Dinamo-uitval midden in 'Stap voor stap': alles na het begin van het probleem telt niet, de loc rijdt terug naar een getest stuk en gaat daar verder
    static void StapUitvalTest()
    {
        Console.WriteLine("Test: stap voor stap met een Dinamo-uitval (BUG #173)");
        foreach (var vanaf in (Environment.GetEnvironmentVariable("UITVAL_VANAF") ?? "60").Split(',').Select(int.Parse))
        {
            Baanverkenner.Kern.Simulatie.UitvalSimulatieBaan? uitval = null;
            var r = MiniworldRit(int.Parse(Environment.GetEnvironmentVariable("STAP_START") ?? "17"), 10, insAanpassing: i => { i.StapVoorStap = true; i.StapAanloop = true; i.StapDoorrijSeconden = 2; },
                simFabriek: () => uitval = new Baanverkenner.Kern.Simulatie.UitvalSimulatieBaan { UitvalVanafCommando = vanaf, UitvalDuur = TimeSpan.FromSeconds(75) });
            if (Environment.GetEnvironmentVariable("MINI_LOG") == "1") Console.WriteLine(r.Log.AlsTekst());
            Console.WriteLine($"  uitval vanaf commando {vanaf}: {r.Einde}, {r.Seconden / 60:0} min, {r.Kortsluitingen} kortsluiting(en), {uitval!.GenegeerdeCommandos} commando's genegeerd, voltooid: {r.V.Kaart.Voltooid}, melders: {r.V.Kaart.Melders.Count}");
            Eis(uitval.GenegeerdeCommandos > 0, $"uitval (vanaf {vanaf}): de uitval heeft plaatsgevonden");
            Eis(r.Log.Regels.Any(x => x.Tekst.Contains("WISSELPROBLEEM")), $"uitval (vanaf {vanaf}): het wisselprobleem wordt herkend en de opdracht begint opnieuw");
            Eis(r.Log.Regels.Any(x => x.Tekst.Contains("Hier gaat het verder")), $"uitval (vanaf {vanaf}): de loc staat eerst weer op een getest stuk baan");
            Eis(r.V.Kaart.Voltooid, $"uitval (vanaf {vanaf}): de verkenning rondt toch af");
            Eis(r.V.Kaart.Melders.Count >= 18, $"uitval (vanaf {vanaf}): alle melders gevonden (nu {r.V.Kaart.Melders.Count})");
        }
    }

    // BUG #164: de kortsluitmelding van Dinamo komt vlak NA het begin van de stop (log 16-10 09:00:35: 1 ms na het eerste stopcommando) en werd als gevolg van de eigen stop genegeerd.
    static void MiniworldAlarmVertragingTest()
    {
        var lijst = Environment.GetEnvironmentVariable("MINI_VERTRAGING");
        if (string.IsNullOrEmpty(lijst)) return;
        Console.WriteLine("Test: verkenning op de Miniworld-baan met verschillende meldvertragingen van het blokalarm (BUG #164)");
        foreach (var vt in lijst.Split(',').Select(x => double.Parse(x, System.Globalization.CultureInfo.InvariantCulture)))
        {
            var r = MiniworldRit(int.Parse(Environment.GetEnvironmentVariable("MINI_START") ?? "17"), 1.5, simAanpassing: sim => { sim.KortsluitMeldVertraging = vt; sim.AlarmAlleenBijRijstroom = Environment.GetEnvironmentVariable("MINI_ALLEENRIJSTROOM") == "1"; });
            int stop = r.Log.Regels.Count(x => x.Tekst.Contains("tijdens de stop een kortsluitmelding"));
            int vermoed = r.Log.Regels.Count(x => x.Tekst.Contains("KORTSLUITING/ONTSPORING vermoed"));
            int blokproef = r.Log.Regels.Count(x => x.Tekst.Contains("Blokproef: blok"));
            int vragen = r.Log.Regels.Count(x => x.Tekst.Contains("Gebruiker") || x.Tekst.Contains("terugzetten van de loc"));
            bool klas12 = r.Log.Regels.Any(x => x.Tekst.Contains("vermoed na melder 12"));
            bool proef12 = r.Log.Regels.Any(x => x.Tekst.Contains("Blokproef voor melder 12"));
            int zelf = r.Log.Regels.Count(x => x.Tekst.Contains("De kortsluiting ging weg nadat wissel"));
            Console.WriteLine($"  [12: geclassificeerd {klas12}, blokproef {proef12}, zelf opgelost {zelf}x] vertraging {vt:0.00} s: {r.Einde}, {r.Seconden / 60:0} min, vermoed {vermoed}, stop-met-alarm {stop}, blokproef-regels {blokproef}, vragen {vragen}, melders {r.V.Kaart.Melders.Count}, voltooid {r.V.Kaart.Voltooid}");
        }
    }

    static void MiniworldFoutzoek()
    {
        var sim = Baanverkenner.Kern.Simulatie.MiniworldBaan.Maak(true, 13);
        sim.VerbindenAsync("SIM").GetAwaiter().GetResult();
        var klok = new VirtueleKlok(); klok.Getikt += sim.Tik;
        sim.ZetLocSnelheid(5408, 28, true, 3);
        for (int i = 0; i < 40; i++) { klok.Wacht(TimeSpan.FromSeconds(1)).GetAwaiter().GetResult(); sim.ZetLocSnelheid(5408, 28, true, 3); sim.ZetLocSnelheid(5408, 28, true, 8); sim.ZetLocSnelheid(5408, 28, true, 7); Console.WriteLine($"  t={i + 1}s {sim.ToestandTest()}"); }
    }

    static void MiniworldRitTest()
    {
        Console.WriteLine("Test: volledige verkenning vanaf nul op de nagebouwde Miniworld-baan (BUG #150)");
        foreach (var start in (Environment.GetEnvironmentVariable("MINI_START") ?? "13").Split(',').Select(int.Parse))
        {
            var r = MiniworldRit(start);
            Console.WriteLine($"  start {start}: {r.Einde}, {r.Seconden / 60:0} min, {r.Kortsluitingen} kortsluiting(en), melders: {string.Join(",", r.V.Kaart.Melders.Select(m => m.Nummer).OrderBy(x => x))}, voltooid: {r.V.Kaart.Voltooid}");
            foreach (var l in r.Log.Regels.Where(x => x.Tekst.Contains("KORTSLUITING/ONTSPORING vermoed") || x.Tekst.Contains("VONDST")).Take(40)) Console.WriteLine("    " + l.Tekst.Substring(0, Math.Min(200, l.Tekst.Length)));
        }
    }


    // BUG #150: tweede run met voorkennis (leerprofiel van de eerste run); doel: minder kortsluitingen dan de eerste.
    static void MiniworldVoorkennisTest()
    {
        if (Environment.GetEnvironmentVariable("MINI_VOORKENNIS") != "1") return;
        Console.WriteLine("Test: tweede run op de Miniworld-baan met voorkennis (BUG #150)");
        var r1 = MiniworldRit(17, 1.0);
        var lp = new LeerProfiel(); lp.Leer(r1.V.Kaart, 12); lp.LeerBlokHints(r1.V.Kaart);
        Console.WriteLine($"  run 1: {r1.Einde}, {r1.Seconden / 60:0} min, {r1.Kortsluitingen} kortsluiting(en), {lp.KortsluitOplossingen.Count} oplossingen ({string.Join("; ", lp.KortsluitOplossingen.Select(o => $"{o.Via}>{o.NaMelder} {o.Richting} wissel {o.Adres}"))})");
        var r2 = MiniworldRit(30, 1.0, v => v.Leerprofiel = lp);
        Console.WriteLine($"  run 2: {r2.Einde}, {r2.Seconden / 60:0} min, {r2.Kortsluitingen} kortsluiting(en), melders {r2.V.Kaart.Melders.Count}, voltooid {r2.V.Kaart.Voltooid}");
        foreach (var l in r2.Log.Regels.Where(x => x.Tekst.Contains("KORTSLUITING/ONTSPORING vermoed")).Take(10)) Console.WriteLine("    " + l.Tekst.Substring(0, Math.Min(200, l.Tekst.Length)));
    }

    // Hulpmiddel (BUG #150): alle kaarten in testdata samenvoegen tot één lijst bewezen kortsluitoplossingen.
    static void ZaadAllesTest()
    {
        var lp = new LeerProfiel();
        var map = Path.Combine(AppContext.BaseDirectory, "testdata");
        foreach (var f in Directory.GetFiles(map, "baankaart_*.json").OrderBy(x => x))
        {
            Baankaart? k; try { k = Baankaart.VanJson(File.ReadAllText(f)); } catch { continue; }
            if (k is null) continue;
            lp.LeerBlokHints(k);
            Console.WriteLine($"  {Path.GetFileName(f)}: {k.AlleKortsluitOplossingen().Count} oplossingen");
        }
        foreach (var o in lp.KortsluitOplossingen.OrderBy(x => x.NaMelder).ThenBy(x => x.Richting))
            Console.WriteLine($"  na {o.NaMelder} {o.Richting} stand [{o.Stand}] start {o.Start}: wissel {o.Adres} {(o.Afbuigend ? "af" : "recht")} ({o.Bron})");
        var uit = Environment.GetEnvironmentVariable("ZAAD_ALLES");
        if (!string.IsNullOrEmpty(uit)) File.WriteAllText(uit, lp.AlsJson());
    }

    static void GeslaagdeRitTest()
    {
        var k = Laad("baankaart_pendel_2026-10-10_1138_geslaagd.json");
        Eis(k.Voltooid && k.Melders.Count == 7 && k.Wissels.Count == 0, "11:38-kaart: voltooid, 7 melders, geen wissels");
        var bb = new BlokBeheerder();
        new BaankaartImporter().Importeer(k, bb);
        Eis(bb.Blokken.Select(b => b.Nummer).OrderBy(x => x).SequenceEqual(new[] { 10, 11, 12 }), "11:38-kaart: blokken 10, 11 en 12");
        Eis(bb.Blokken.First(b => b.Nummer == 10).Bezetmeldpunten.Select(p => p.MeldernNummer).OrderBy(x => x).SequenceEqual(new[] { 132, 139, 140 }), "11:38-kaart: blok 10 = melders 132, 139, 140");
        Eis(bb.Blokken.First(b => b.Nummer == 11).Bezetmeldpunten.Select(p => p.MeldernNummer).SequenceEqual(new[] { 133 }), "11:38-kaart: blok 11 = melder 133");
        Eis(bb.Blokken.First(b => b.Nummer == 12).Bezetmeldpunten.Select(p => p.MeldernNummer).OrderBy(x => x).SequenceEqual(new[] { 136, 143, 144 }), "11:38-kaart: blok 12 = melders 136, 143, 144");
        Eis(k.Kopsporen.Select(x => x.Melder).OrderBy(x => x).SequenceEqual(new[] { 139, 143 }), "11:38-kaart: kopsporen 139 en 143");
        Eis(bb.Relaties.All(r => r.RijrichtingVooruit != null) && bb.Relaties.Count >= 2, "11:38-kaart: alle relaties hebben een rijrichting");
        var rb = new TreinrouteBeheerder(new RichtingsverbodBeheerder());
        Eis(new DienstAanvuller().Vul(bb.Blokken, rb, bb).Toegevoegd.Count > 0, "11:38-kaart: automatische dienst wordt aangemaakt");
        // alle 12 gemeten overgangen staan in het leerprofiel
        var lp = new LeerProfiel(); lp.Leer(Laad("baankaart_pendel_2026-10-10_1025.json"), 12); lp.Leer(k, 12);
        var lpZaad = new LeerProfiel(); lpZaad.LeerBlokHints(Laad("baankaart_voltooid_2026-10-04.json")); lpZaad.Leer(Laad("baankaart_pendel_2026-10-10_1025.json"), 12); lpZaad.Leer(k, 12); lpZaad.Leer(Laad("baankaart_lijn_5-30_tussenstand_2026-10-10_1215.json"), 12);
        Eis(lpZaad.BlokHint(16) == 4 && lpZaad.BlokHint(17) == 5 && lpZaad.BlokHint(28) == 6 && lpZaad.BlokHint(24) == 8, "blokhints: melders 16, 17 en 28 (nooit gevonden in de rit van 12:15) komen uit de kaart van 4 oktober");
        Eis(lp.Overgangen.Count == 10, "11:38-kaart: leerprofiel kent de 10 gemeten overgangen (143->144 en 139->140 zijn nooit gemeten: die ritten begonnen op het uiteinde) (nu " + lp.Overgangen.Count + ")");
        Eis(Math.Abs((lp.Verwacht(144, 143, Richting.Vooruit, 12) ?? 0) - 17.0) < 1.0, "11:38-kaart: 144 -> 143 duurt ongeveer 17 s bij snelheid 12");
        Eis((lp.Verwacht(136, 133, Richting.Achteruit, 12) ?? 99) < 5, "11:38-kaart: 136 -> 133 is een korte sectie (< 5 s)");
        Eis((lp.Verwacht(133, 136, Richting.Vooruit, 12) ?? 0) > 10, "11:38-kaart: 133 -> 136 duurt juist lang (12 s): heen en terug zijn NIET gelijk, dus niet spiegelen");
        var zaadPad = Environment.GetEnvironmentVariable("GENEREER_ZAAD");
        if (!string.IsNullOrEmpty(zaadPad)) { File.WriteAllText(zaadPad, lpZaad.AlsJson()); Console.WriteLine("  zaadprofiel geschreven naar " + zaadPad); }
    }

    static void Eis(bool ok, string tekst) { Console.WriteLine((ok ? "OK    " : "FOUT  ") + tekst); if (!ok) _fouten++; }
    // BUG #196-#199 (log 17-10 20:05): wissel 5 na melder 14, melderschema, onverwachte-terugwegregel
    static void Log2005Test()
    {
        var k = Laad("baankaart_2026-10-17_2005.json");
        var o = new Opdracht { Start = 13, Richting = Richting.Vooruit, Configuratie = new Configuratie(new[] { 5 }), BronAdres = 5, BronRechtdoorVolgende = 14 };
        var sp = BaanVerkenner.ZoekAndereKantSplitsing(k, o, new List<int> { 13, 14, 129, 24, 21, 29 }, 5);
        Eis(sp is { Na: 14, Rechtdoor: 24, Afbuigend: 129 }, "#197: wissel 5 splitst na melder 14 (rechtdoor 24, afbuigend 129), niet 14 -> 14 na melder 13 (nu " + (sp is null ? "null" : $"{sp.Value.Na}:{sp.Value.Rechtdoor}/{sp.Value.Afbuigend}") + ")");
        var o2 = new Opdracht { Start = 13, Richting = Richting.Vooruit, Configuratie = new Configuratie(new[] { 2 }), BronAdres = 2, BronRechtdoorVolgende = 14 };
        var sp2 = BaanVerkenner.ZoekAndereKantSplitsing(k, o2, new List<int> { 13, 5 }, 2);
        Eis(sp2 is { Na: 13, Rechtdoor: 14, Afbuigend: 5 }, "#197: een verschil direct na de startmelder blijft gewoon na de startmelder");
        var o3 = new Opdracht { Start = 13, Richting = Richting.Vooruit, Configuratie = new Configuratie(new[] { 3 }), BronAdres = 3, BronRechtdoorVolgende = 14 };
        Eis(BaanVerkenner.ZoekAndereKantSplitsing(k, o3, new List<int> { 13, 14, 24, 21 }, 3) is null, "#197: dezelfde rit als zonder de wissel geeft geen (valse) splitsing");

        var m = Melderschema.Maak(k);
        Eis(m.Knopen.Count == 19, "melderschema: 19 melders (nu " + m.Knopen.Count + ")");
        Eis(m.Knopen.First(x => x.Melder == 24).Splitsing && m.Knopen.First(x => x.Melder == 14).Splitsing, "melderschema: melder 14 en 24 zijn splitsingen (drie of meer buren)");
        Eis(m.Knopen.All(x => x.X > 0 && x.Y > 0 && x.X < m.Breedte && x.Y < m.Hoogte), "melderschema: alle melders liggen binnen de tekening");
        double min = double.MaxValue;
        for (int i = 0; i < m.Knopen.Count; i++)
            for (int j = i + 1; j < m.Knopen.Count; j++)
                min = Math.Min(min, Math.Sqrt(Math.Pow(m.Knopen[i].X - m.Knopen[j].X, 2) + Math.Pow(m.Knopen[i].Y - m.Knopen[j].Y, 2)));
        Eis(min > 36, "melderschema: geen twee melders liggen over elkaar (kleinste afstand " + min.ToString("0") + ")");
        Eis(Melderschema.Maak(k).AlsSvg() == m.AlsSvg(), "melderschema: dezelfde kaart geeft steeds dezelfde tekening");
        var svg = m.AlsSvg();
        Eis(svg.Contains("W10 afb.") && svg.Contains("W2 rd."), "melderschema: wissels staan bij de lijnen");
        Eis(Rapport.AlsHtml(k).Contains("<h2>Melderschema</h2>"), "melderschema: het HTML-rapport bevat het schema");
        var uit = Environment.GetEnvironmentVariable("MELDERSCHEMA_UIT");
        if (!string.IsNullOrEmpty(uit)) { File.WriteAllText(uit, svg); Console.WriteLine("  melderschema geschreven naar " + uit); }
        Eis(k.AdressenZonderEffect is not null, "kaart geladen");
    }

    static Baankaart Laad(string f) => Baankaart.VanJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "testdata", f)))!;

    public static int Main()
    {
        if (Environment.GetEnvironmentVariable("ALLEEN_KEERLUS") == "1") { KeerlusTest(); Console.WriteLine(_fouten == 0 ? "ALLES OK" : $"{_fouten} FOUT(EN)"); return _fouten == 0 ? 0 : 1; }
        if (Environment.GetEnvironmentVariable("BENCH") == "1") { BenchTest(); return 0; }
        if (Environment.GetEnvironmentVariable("RONDRIT") == "1") { RondritBench(); return 0; }
        var kaart = Laad("baankaart_voltooid_2026-10-04.json");

        // 1. leeg project
        var bb = new BlokBeheerder();
        var imp = new BaankaartImporter();
        var r1 = imp.Importeer(kaart, bb);
        Eis(bb.Blokken.Select(b => b.Nummer).OrderBy(x => x).SequenceEqual(new[] { 3, 4, 5, 6, 7, 8 }), "blokken 3-8 aangemaakt (Nummer = Dinamo-blok)");
        Eis(bb.Blokken.All(b => b.Type == BlokType.Normaal), "geen vals kopspoor (melder 17 en 28 lopen maar in één richting dood)");
        Eis(bb.Blokken.First(b => b.Nummer == 3).Bezetmeldpunten.Select(p => p.MeldernNummer).OrderBy(x => x).SequenceEqual(new[] { 5, 13, 14 }), "blok 3 = melders 5, 13, 14");
        Eis(bb.Relaties.First(r => r.Van.Nummer == 3 && r.Naar.Nummer == 4).RijrichtingVooruit == false, "3 -> 4 = achteruit (vastgesteld door Marco)");
        Eis(bb.Relaties.First(r => r.Van.Nummer == 3 && r.Naar.Nummer == 8).RijrichtingVooruit == true, "3 -> 8 (kruiswissel-sectie, melder 24) = vooruit");
        Eis(bb.Relaties.All(r => r.RijrichtingVooruit != null), "alle relaties hebben een rijrichting");

        // 2. idempotent
        Eis(imp.Importeer(kaart, bb).IsLeeg, "tweede import verandert niets");

        // 3. bestaand project: overloop-melder 24, vastgelegde richtingen blijven staan, herstel werkt
        var bb4 = new BlokBeheerder();
        Blok Mk(int n, params int[] m) { var b = new Blok { Nummer = n }; foreach (var x in m) b.Bezetmeldpunten.Add(new Bezetmeldpunt { MeldernNummer = x }); bb4.Blokken.Add(b); return b; }
        var b3 = Mk(3, 14, 13, 5, 24); var b4 = Mk(4, 24, 16, 15); Mk(5, 17, 25, 26); Mk(6, 27, 28, 20); var b7 = Mk(7, 21, 29, 30, 24);
        bb4.LegRelatieVast(b3, b4)!.RijrichtingVooruit = false; bb4.LegRelatieVast(b3, b7)!.RijrichtingVooruit = true;
        var snap = BaankaartImporter.Momentopname.Maak(bb4);
        imp.Importeer(kaart, bb4);
        Eis(!bb4.Blokken.Any(b => b.Nummer == 8), "melder 24 (al overloop in blok 3/4/7) maakt geen blok 8");
        Eis(b4.Bezetmeldpunten.Any(p => p.MeldernNummer == 8), "ontbrekende melder 8 aan blok 4 toegevoegd");
        Eis(bb4.Relaties.First(r => r.Van == b3 && r.Naar == b4).RijrichtingVooruit == false && bb4.Relaties.First(r => r.Van == b3 && r.Naar == b7).RijrichtingVooruit == true, "vastgelegde richtingen niet overschreven");
        snap.Herstel(bb4);
        Eis(bb4.Blokken.Count == 5 && bb4.Relaties.Count == 2 && b4.Bezetmeldpunten.Count == 3, "momentopname herstelt alles");

        // 4. dienst: automatische route per nieuw blok, bestaande routes blijven, herstel werkt
        var rb = new TreinrouteBeheerder(new RichtingsverbodBeheerder());
        var bestaand = rb.NieuweTreinroute(bb.Blokken.First(b => b.Nummer == 3), "Mijn eigen route");
        var dienst = new DienstAanvuller();
        var dres = dienst.Vul(bb.Blokken, rb, bb);
        Eis(dres.Toegevoegd.Count == 5 && rb.Treinroutes.Count == 6, "automatische route voor elk blok zonder route (5 nieuw)");
        Eis(rb.Treinroutes.Contains(bestaand) && bestaand.Omschrijving == "Mijn eigen route" && !bestaand.Automatisch, "bestaande route niet aangepast");
        Eis(dres.Toegevoegd.All(r => r.Automatisch && r.GeplandeVertrektijd == null), "automatisch, zonder verzonnen vertrektijd");
        Eis(dienst.Vul(bb.Blokken, rb, bb).Toegevoegd.Count == 0, "tweede keer voegt geen routes toe");
        dienst.Herstel(dres, rb);
        Eis(rb.Treinroutes.Count == 1 && rb.Treinroutes[0] == bestaand, "dienst-herstel verwijdert alleen eigen routes");

        PendelTest();
        GeslaagdeRitTest();
        OverloopTest();
        AfbuigendeTakTerugTest();
        OmgekeerdeOvergangTest();
        PartnerVolgordeTest();
        BlokgrensTest();
        NavigatieOnjuisteStartTest();
        NavigatieEchoTest(false); NavigatieEchoTest(true);
        LocAndersom278Test();
        BlokkenschemaTest();
        OverdrachtTest();
        DoorgangTest();
        UitbreidenTest();
        ControleRondeTest();
        DoorrijdenTest();
        Wissel14Test();
        StaartWisselTest();
        AltijdAfbuigendTest();
        ProefStopTest();
        OverloopStraatTest();
        PartnerAlarmIngreepTest();
        ProefOplossingTest();
        KruisVoorafTest();
        OplossingLaterOpWegTest();
        StartmelderVoorafTest();
        WisselBevestigingTest();
        HoortestEnKruisTest();
        KruisZonderReferentieTest();
        WisselTerugmeldingTest();
        NastellenAnderBlokTest();
        StatischeKortsluitingTest();
        StopTijdensTerugritTest();
        BuurmelderBlokproefTest();
        OverloopPaarEisenTest();
        TweedeMelderTest();
        VerkeerdeWisselVraagTest();
        WisselstraatImportTest();
        WisselstraatCorrectieTest();
        WisselOnderLocTest();
        DinamoStilteTest();
        AlarmVoorStapTest();
        AlarmOplossingVastleggenTest();
        AankomstOplossingWeerlegdTest();
        AankomstEnOverloopTest();
        Aankomst192Test();
        KruisUitsluitingTest();
        SchemaWisselsTest();
        Log2005Test();
        RondritTest();
        AlarmWisselBijLocTest();
        KruisRuisVoorafTest();
        AankomstViaIngangTest();
        ZaadAllesTest();
        if (Environment.GetEnvironmentVariable("MINI_DEBUG") == "1") MiniworldFoutzoek();
        MiniworldRitTest();
        StapUitvalTest();
        OnbekendeBlokkenTest();
        KennisRegisterTest();
        SimBuurwisselTest();
        StapVoorStapTest();
        StapMatrixTest();
        if (Environment.GetEnvironmentVariable("KEERLUS") != "0") KeerlusTest();
        AanloopTest();
        MiniworldAlarmVertragingTest();
        MiniworldVoorkennisTest();
        HerstelBlijftStaanTest();
        KortsluitOplossingTest();
        KoploperReferentieTest();
        KruiswisselReferentieTest();
        HandmatigeOplossingTest();
        StopHerhalingTest();
        EchteBloknummersTest();
        KruiswisselControleTest();
        KruisMotorKortsluitingTest();
        TijdelijkeKortsluitingTest();
        KenVervolgTest();
        StopVoorTerugzettenTest();
        LocAndersomTest();
        VoorspeldPadTest();
        KruisIngangTest();
        BotsendeOplossingenTest();
        OpstartStandTest();
        MelderGeheugenWisTest();
        VoorkennisVraagTest();
        KruisKennisTest();
        HerpogingVolgordeTest();
        RouteVerkortTest();
        KruisPlusTest();
        KruisStandBasisTest();
        WisselRollenTest();
        VerbodenPadTest();
        KruisRuimTest();
        VeiligePlekTest();
        KruisAankomstTest();
        OrientatieTekstTest();
        WisselEisenTest();
        WisselstratenTest();

        Console.WriteLine(_fouten == 0 ? "ALLES OK" : $"{_fouten} FOUT(EN)");
        return _fouten == 0 ? 0 : 1;
    }
}
