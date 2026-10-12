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
        ControleRondeTest();
        DoorrijdenTest();
        Wissel14Test();
        StaartWisselTest();
        AltijdAfbuigendTest();
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
        HerpogingVolgordeTest();
        RouteVerkortTest();
        KruisAankomstTest();
        OrientatieTekstTest();
        WisselEisenTest();
        WisselstratenTest();

        Console.WriteLine(_fouten == 0 ? "ALLES OK" : $"{_fouten} FOUT(EN)");
        return _fouten == 0 ? 0 : 1;
    }
}
