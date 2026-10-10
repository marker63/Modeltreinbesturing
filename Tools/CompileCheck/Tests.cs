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
        PendelScenario(new Dictionary<(int, int, bool), int> { [(132, 10, true)] = 2, [(132, 10, false)] = 2 }, "pendel (proef blijft mislukken)", true);
    }

    static void PendelScenario(Dictionary<(int, int, bool), int> blokkeringen, string naam, bool blokMoetBekendZijn)
    {
        var sim = new Baanverkenner.Kern.Simulatie.SimulatieBaan { DinamoGedrag = true, StaatStilZonderBlokcommando = true, LocAdres = 5408 };
        foreach (var gb in blokkeringen) sim.GeblokkeerdeCommandos[gb.Key] = gb.Value; // op de baan gaf het 'klein stukje terug' bij melder 132 geen beweging
        var A = Baanverkenner.Kern.Simulatie.SimulatieBaan.Eind.A; var B = Baanverkenner.Kern.Simulatie.SimulatieBaan.Eind.B;
        var s143 = sim.NieuweSectie(143, 100, 12); var s144 = sim.NieuweSectie(144, 400, 12); var s136 = sim.NieuweSectie(136, 80, 12);
        var s133 = sim.NieuweSectie(133, 300, 11); var s132 = sim.NieuweSectie(132, 300, 10);
        var s140 = sim.NieuweSectie(140, 150, 10); var s139 = sim.NieuweSectie(139, 150, 10);
        sim.Verbind(s143, B, s144, A); sim.Verbind(s144, B, s136, A); sim.Verbind(s136, B, s133, A);
        sim.Verbind(s133, B, s132, A); sim.Verbind(s132, B, s140, A); sim.Verbind(s140, B, s139, A);
        sim.PlaatsLoc(s144, 200, -1); // vooruit = richting 143 (zoals op de baan)
        var ins = new VerkenInstellingen { LocAdres = 5408, LocStappen = 28, Verkensnelheid = 12, Kruipsnelheid = 5, WisselAdresVan = 1, WisselAdresTot = 1, DinamoBlokken = "10-12" };
        sim.VerbindenAsync("SIM").GetAwaiter().GetResult();
        var klok = new VirtueleKlok(); klok.Getikt += sim.Tik;
        var log = new VerkenLog(klok);
        var v = new BaanVerkenner(sim, ins, klok, log, (_, _) => Task.FromResult(true));
        using var cts = new CancellationTokenSource();
        try { v.VoerUit(cts.Token).GetAwaiter().GetResult(); } catch (Exception ex) { Console.WriteLine("  (verkenner stopte met: " + ex.Message + ")"); }
        if (Environment.GetEnvironmentVariable("PENDEL_LOG") == "1") Console.WriteLine(log.AlsTekst());
        var k = v.Kaart;
        Eis(k.Melders.Count == 7, naam + ": alle 7 melders gevonden (nu " + k.Melders.Count + ")");
        Eis(!blokMoetBekendZijn || k.Melders.All(m => m.DinamoBlok is not null), naam + ": elke melder heeft een Dinamo-blok (zonder: " + string.Join(",", k.Melders.Where(m => m.DinamoBlok is null).Select(m => m.Nummer)) + ")");
        Eis(!blokMoetBekendZijn || k.Melders.FirstOrDefault(m => m.Nummer == 132)?.DinamoBlok == 10, naam + ": melder 132 = Dinamo-blok 10");
        Eis(k.Overgangen.Any(o => o.Van == 140 && o.Naar == 139 && o.Richting == Richting.Achteruit) || k.Overgangen.Any(o => o.Van == 132 && o.Naar == 140), naam + ": rit loopt door voorbij melder 132");
        Eis(!k.Kopsporen.Any(x => x.Melder == 132), naam + ": melder 132 is GEEN kopspoor");
        Eis(k.Kopsporen.Any(x => x.Melder == 139) && k.Kopsporen.Any(x => x.Melder == 143), naam + ": echte uiteinden 139 en 143 zijn kopspoor");
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

        Console.WriteLine(_fouten == 0 ? "ALLES OK" : $"{_fouten} FOUT(EN)");
        return _fouten == 0 ? 0 : 1;
    }
}
