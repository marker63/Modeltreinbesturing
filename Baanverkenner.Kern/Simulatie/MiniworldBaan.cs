namespace Baanverkenner.Kern.Simulatie;

/// <summary>
/// BUG #150: nagebouwde testbaan van Marco (Miniworld Rotterdam) voor de volledige test van de verkenning, afgeleid uit ALLE baankaarten van 10 t/m 14-10
/// en de Koploper-tekening. De opbouw is een HYPOTHESE (nog niet door Marco bevestigd), maar sluit aan op alle gezien kortsluitingen en doorgangen:
///
///   grote ring (alles rechtdoor):  13 - 14 - [wissel 5] - kruiswissel 24 - 21 - 29 - 30 - 20 - 27 - 28 - 26 - 25 - 17 - [wissel 14] - 8 - 15 - 16 - [wissel 2] - 5 - 13
///   kruiswissel 24 (motoren 13 en 10): benen 14 en 129 (kant A), 21 en 8 (kant B).
///       motor 13 rechtdoor: 14 &lt;-&gt; 21, afbuigend: 14 &lt;-&gt; 8;  motor 10 afbuigend: 129 &lt;-&gt; 21, rechtdoor: 129 &lt;-&gt; 8.
///   wissel 14 voor melder 8: been 17 = rechtdoor, been 24 (via de kruiswissel) = afbuigend (van achteren bereden: moet kloppen).
///   wissel 5 na melder 14: rechtdoor naar de kruiswissel, afbuigend naar 129 (via wissel 6, rechtdoor); wissel 6: afbuigend naar zijspoor 10.
///   wissel 9 na melder 129: rechtdoor naar de kruiswissel (been 129), afbuigend naar de binnenring 4 - 11 - 12;
///       12 komt via wissel 1 (afbuigend) en een verbindingsstuk in wissel 2 (afbuigend) uit op melder 5 (overloop 1+2).
///
/// De loc (5408) staat standaard op melder 13, vooruit = richting 14.
/// </summary>
public static class MiniworldBaan
{
    public static SimulatieBaan Maak(bool omzettenOnderLocGeeftKortsluiting = true, int startMelder = 13, Func<SimulatieBaan>? fabriek = null)
    {
        var b = fabriek?.Invoke() ?? new SimulatieBaan();
        b.DinamoGedrag = true; b.StaatStilZonderBlokcommando = true; b.StilstaanRemtOpGrens = true; b.LocAdres = 5408;
        b.OmzettenOnderLocGeeftKortsluiting = omzettenOnderLocGeeftKortsluiting; b.MeldersBevrorenBijKortsluiting = true; b.MeldBlokAlarm = true;
        var A = SimulatieBaan.Eind.A; var B = SimulatieBaan.Eind.B;
        var P = SimulatieBaan.Poort.Stam; var R = SimulatieBaan.Poort.Recht; var Af = SimulatieBaan.Poort.Af;

        var s13 = b.NieuweSectie(13, 127, 3); var s14 = b.NieuweSectie(14, 156, 3);
        var s21 = b.NieuweSectie(21, 89, 7); var s29 = b.NieuweSectie(29, 90, 7); var s30 = b.NieuweSectie(30, 300, 7);
        var s20 = b.NieuweSectie(20, 84, 6); var s27 = b.NieuweSectie(27, 67, 6); var s28 = b.NieuweSectie(28, 340, 6);
        var s26 = b.NieuweSectie(26, 72, 5); var s25 = b.NieuweSectie(25, 77, 5); var s17 = b.NieuweSectie(17, 338, 5);
        var s8 = b.NieuweSectie(8, 77, 4); var s15 = b.NieuweSectie(15, 168, 4); var s16 = b.NieuweSectie(16, 86, 4);
        var s5 = b.NieuweSectie(5, 120, 3);
        var s129 = b.NieuweSectie(129, 65, 9); var s10 = b.NieuweSectie(10, 60, 9);
        var s4 = b.NieuweSectie(4, 30, 2); var s11 = b.NieuweSectie(11, 245, 2); var s12 = b.NieuweSectie(12, 120, 2);
        var verbinding = b.NieuweSectie(5, 25, 3);   // verbindingsstuk van de overloop: ligt in het gebied van melder 5 (geen losse melder, anders valt de loc zonder voeding stil)

        var k = b.NieuweKruis(13, 10, melder: 24, blok: 8, lengte: 50);
        var w5 = b.NieuweWissel(5, true, 3); var w6 = b.NieuweWissel(6, true, 9); var w9 = b.NieuweWissel(9, true, 9);
        var w14 = b.NieuweWissel(14, true, 4); var w2 = b.NieuweWissel(2, true, 3); var w1 = b.NieuweWissel(1, true, 2);

        foreach (var w in new[] { w5, w6, w9, w14, w2, w1 }) w.Lengte = 8;
        // De tongen van een wissel liggen binnen het detectiegebied van een melder (gezien in de logs: 16 -> 5 geeft de kortsluiting "na melder 5", 24 -> 8 "na melder 8",
        // 4 -> 129 "na melder 129", 13 -> 5 -> 12 "na melder 12"): de loc op de wissel meldt dus die melder bezet.
        w5.Melder = 14; w6.Melder = 129; w9.Melder = 129; w14.Melder = 8; w2.Melder = 5; w1.Melder = 12;   // korter dan de halve loc: anders valt de loc even buiten elk gevoed blok (sim-artefact)

        b.Verbind(s13, B, s14, A);
        b.Verbind(w5, P, s14, B);
        b.Verbind(w5, R, k, SimulatieBaan.Poort.KA1);
        b.Verbind(w5, Af, w6, R);
        b.Verbind(w6, P, s129, A);
        b.Verbind(w6, Af, s10, A);
        b.Verbind(w9, P, s129, B);
        b.Verbind(w9, R, k, SimulatieBaan.Poort.KA2);
        b.Verbind(w9, Af, s4, A);
        b.Verbind(s4, B, s11, A); b.Verbind(s11, B, s12, A);
        b.Verbind(w1, P, s12, B);
        b.Verbind(w1, Af, verbinding, A);
        b.Verbind(w2, Af, verbinding, B);
        b.Verbind(w2, P, s5, A);
        b.Verbind(w2, R, s16, B);
        b.Verbind(s5, B, s13, A);
        b.Verbind(k, SimulatieBaan.Poort.KB1, s21, A);
        b.Verbind(s21, B, s29, A); b.Verbind(s29, B, s30, A); b.Verbind(s30, B, s20, A); b.Verbind(s20, B, s27, A);
        b.Verbind(s27, B, s28, A); b.Verbind(s28, B, s26, A); b.Verbind(s26, B, s25, A); b.Verbind(s25, B, s17, A);
        b.Verbind(w14, R, s17, B);
        b.Verbind(w14, Af, k, SimulatieBaan.Poort.KB2);
        b.Verbind(w14, P, s8, A);
        b.Verbind(s8, B, s15, A); b.Verbind(s15, B, s16, A);

        var start = new[] { s13, s14, s21, s29, s30, s20, s27, s28, s26, s25, s17, s8, s15, s16, s5, s129, s10, s4, s11, s12 }.First(x => x.Melder == startMelder);
        b.PlaatsLoc(start, start.Lengte / 2, +1);
        return b;
    }
}
