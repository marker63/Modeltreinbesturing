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
        var v = new BaanVerkenner(sim, ins, klok, log, (_, _) => Task.FromResult(true)) { Leerprofiel = profiel };
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
        Eis(fout && tekst.Contains("blok 8") && tekst.Contains("zet geen wissels om"), "alarm voor stap: blok 8 blijft in alarm -> navigatiefout, geen wissels/rit (nu " + tekst + ")");
        alarm.Invoke(v, new object[] { 8, false });
        ((Task)check.Invoke(v, new object[] { "test" })!).GetAwaiter().GetResult();
        Eis(true, "alarm voor stap: alarm opgeheven -> verder");
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
        var k = Laad("baankaart_2026-10-11_1103.json");
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
        var k = Laad("baankaart_lijn_5-30_voltooid_kruiswissel24_2026-10-10_1707.json");
        var t = SchemaTekening.Maak(k);
        Eis(t.Vakken.Count == 7, "blokkenschema: 7 vakken (nu " + t.Vakken.Count + ")");
        Eis(t.Verbindingen.Count >= 7, "blokkenschema: minstens 7 verbindingen (nu " + t.Verbindingen.Count + ")");
        Eis(t.Vakken.All(v => v.X > 0 && v.Y > 0 && v.X < t.Breedte && v.Y < t.Hoogte), "blokkenschema: alle vakken liggen binnen de tekening");
        var svg = t.AlsSvg(true);
        Eis(svg.Contains("<svg") && svg.Contains("melders 24") && svg.Contains("onbekend (melder 129)"), "blokkenschema: svg bevat melder 24 en het onbekende Dinamo-blok van melder 129 (geen verzonnen nummer)");
        Eis(t.Vakken.First(v => v.Melders == "24").Soort == "splitsing", "blokkenschema: melder 24 (kruiswissel) is een splitsing");
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
    static Baankaart Laad(string f) => Baankaart.VanJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "testdata", f)))!;

    public static int Main()
    {
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
        BlokkenschemaTest();
        OverdrachtTest();
        DoorgangTest();
        UitbreidenTest();
        TweedeMelderTest();
        VerkeerdeWisselVraagTest();
        WisselstraatImportTest();
        WisselOnderLocTest();
        DinamoStilteTest();
        AlarmVoorStapTest();
        KortsluitOplossingTest();
        KoploperReferentieTest();
        KruiswisselReferentieTest();
        HandmatigeOplossingTest();
        StopHerhalingTest();
        EchteBloknummersTest();
        WisselEisenTest();
        WisselstratenTest();

        Console.WriteLine(_fouten == 0 ? "ALLES OK" : $"{_fouten} FOUT(EN)");
        return _fouten == 0 ? 0 : 1;
    }
}
