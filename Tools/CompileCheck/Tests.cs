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

    // Blokkenschema-tekening (SVG) uit de baankaart van 17:07
    static void BlokkenschemaTest()
    {
        var k = Laad("baankaart_lijn_5-30_voltooid_kruiswissel24_2026-10-10_1707.json");
        var t = SchemaTekening.Maak(k);
        Eis(t.Vakken.Count == 7, "blokkenschema: 7 vakken (nu " + t.Vakken.Count + ")");
        Eis(t.Verbindingen.Count >= 7, "blokkenschema: minstens 7 verbindingen (nu " + t.Verbindingen.Count + ")");
        Eis(t.Vakken.All(v => v.X > 0 && v.Y > 0 && v.X < t.Breedte && v.Y < t.Hoogte), "blokkenschema: alle vakken liggen binnen de tekening");
        var svg = t.AlsSvg(true);
        Eis(svg.Contains("<svg") && svg.Contains("melders 24") && svg.Contains("Dinamo ?"), "blokkenschema: svg bevat melder 24 en het onbekende Dinamo-blok van melder 129");
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

        Console.WriteLine(_fouten == 0 ? "ALLES OK" : $"{_fouten} FOUT(EN)");
        return _fouten == 0 ? 0 : 1;
    }
}
