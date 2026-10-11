using System.Text;

namespace Baanverkenner.Kern;

/// <summary>
/// BUG #85. Vaste referentie voor de kruiswissel (Engelse wissel) in melder 24: de vier benen met al hun bezetmelders in volgorde
/// en per verbinding tussen twee benen de stand van de twee motoren 13 en 10. Ontstaan omdat de standen van 13 en 10 steeds door
/// kortsluitoplossingen en onthouden routes werden overschreven. Deze referentie wordt als LAATSTE toegepast op elk pad dat door
/// melder 24 gaat (<see cref="PasToe"/>) en gaat dus voor alles wat uit kortsluitmeldingen is afgeleid.
///
/// Alleen wat BEWEZEN is staat erin (uit de baankaart van 11-10-2026 11:03: Doorgangen via melder 24, en de logs van Marco van
/// 10-10-2026 16:40-17:01). Stand: true = afbuigend, false = rechtdoor, null = maakt niet uit / niet bepaald (dan blijft de stand
/// zoals de rest van de verkenner hem kiest). De verbinding 129 &lt;-&gt; 8 is nog nooit gereden en staat daarom als onbekend erin:
/// daar wordt niets afgedwongen. NIET veldgetest door Claude; de oriëntatie van de plusfiguur is schematisch.
/// </summary>
public static class KruiswisselReferentie
{
    public const int Midden = 24;
    public const int Motor13 = 13;
    public const int Motor10 = 10;

    /// <summary>Een been van de kruiswissel: de melders buitenwaarts vanaf melder 24 (de eerste is de buurmelder van 24).</summary>
    public record Been(string Plek, int KoploperBlok, int[] Melders, string Opmerking);

    /// <summary>De standen van motor 13 en 10 voor een rit tussen twee benen (in beide richtingen).</summary>
    public record Verbinding(int MelderA, int MelderB, bool? Afbuigend13, bool? Afbuigend10, bool Bewezen, string Bron);

    public static readonly Been[] Benen =
    {
        new("noord", 1, new[] { 129, 10, 9, 1 }, "Koploper-blok 1 (melder 129 is doorgangsmelder; vanaf 129 ook naar blok 2 via 4, 11, 12)."),
        new("west", 3, new[] { 14, 13, 5 }, "Koploper-blok 3 (melders 5, 13, 14 delen Dinamo-blok 3)."),
        new("oost", 4, new[] { 8, 15, 16 }, "Koploper-blok 4; volgorde 8, 15, 16 zoals gemeten (Koploper noemt 8, 16, 15)."),
        new("zuid", 7, new[] { 21, 29, 30 }, "Koploper-blok 7."),
    };

    public static readonly Verbinding[] Verbindingen =
    {
        new(14, 21, false, false, true, "Marco 10-10 17:00: 21<->24<->14 lukt met motor 13 en 10 beide rechtdoor; ook de baankaart (46 doorgangen). Motor 13 afbuigend is daar ook eens gezien, maar niet nodig."),
        new(14, 8, true, null, true, "Marco 10-10 16:48 en 16:59: 8<->24<->14 lukt alleen met motor 13 afbuigend (13 rechtdoor = kortsluiting). Motor 10 is in de baankaart zowel rechtdoor als afbuigend gezien (31 doorgangen): maakt niet uit."),
        new(129, 21, false, true, true, "Baankaart 11-10 11:03: 21<->24<->129 (9 doorgangen) alleen met motor 10 afbuigend en 13 rechtdoor; Marco 10-10: 21 -> 24 -> 129 lukte met motor 10 afbuigend."),
        new(129, 8, null, null, false, "Nog nooit gereden: stand onbekend, er wordt niets afgedwongen. Te meten met een proefrit 8 -> 24 -> 129."),
    };

    public static Verbinding? Zoek(int a, int b)
        => Verbindingen.FirstOrDefault(v => (v.MelderA == a && v.MelderB == b) || (v.MelderA == b && v.MelderB == a));

    /// <summary>Past de bewezen kruiswisselstanden toe op <paramref name="c"/> voor elke doorgang X -> 24 -> Y in <paramref name="pad"/>.
    /// Een pad dat op of bij 24 begint of eindigt (geen ingang- of uitgangskant bekend) wordt niet aangeraakt; ook niet als twee doorgangen
    /// in hetzelfde pad elkaar tegenspreken.</summary>
    public static Configuratie PasToe(List<int> pad, Configuratie c, out List<string> wijzigingen)
    {
        wijzigingen = new List<string>();
        var wens = new Dictionary<int, bool>(); var strijdig = new HashSet<int>();
        var bronTekst = new Dictionary<int, string>();
        for (int i = 1; i + 1 < pad.Count; i++)
        {
            if (pad[i] != Midden) continue;
            var v = Zoek(pad[i - 1], pad[i + 1]);
            if (v is not { Bewezen: true }) continue;
            void Eis(int motor, bool? s)
            {
                if (s is not bool b) return;
                if (wens.TryGetValue(motor, out var oud) && oud != b) strijdig.Add(motor);
                wens[motor] = b;
                bronTekst[motor] = $"kruiswissel-referentie {pad[i - 1]} -> 24 -> {pad[i + 1]}";
            }
            Eis(Motor13, v.Afbuigend13); Eis(Motor10, v.Afbuigend10);
        }
        foreach (var (motor, afb) in wens.OrderBy(w => w.Key))
        {
            if (strijdig.Contains(motor) || c.Bevat(motor) == afb) continue;
            c = c.MetStand(motor, afb);
            wijzigingen.Add($"{motor} {(afb ? "afbuigend" : "rechtdoor")} ({bronTekst[motor]})");
        }
        return c;
    }

    // ------------------------------------------------------------------ weergave

    private static string St(bool? s) => s is null ? "vrij" : s.Value ? "afbuigend" : "rechtdoor";

    /// <summary>BUG #89 (Marco 13:44: bij een geheel nieuwe verkenning stond het pluskruis al helemaal gevuld): met een baankaart toont de
    /// weergave alleen wat DEZE verkenning al gevonden heeft; de rest blijft blanco. Zonder baankaart (null) staat alles erin.</summary>
    private static bool Gevonden(Baankaart? k, int melder) => k is null || k.Melders.Any(m => m.Nummer == melder);

    private static bool VerbindingGevonden(Baankaart? k, Verbinding v) => Gevonden(k, Midden) && Gevonden(k, v.MelderA) && Gevonden(k, v.MelderB);

    private static string BlokLabel(Baankaart? k, Been b)
    {
        if (k is null) return $"Dinamo-blok {b.KoploperBlok}";
        var blok = b.Melders.Select(m => k.Melders.FirstOrDefault(x => x.Nummer == m)?.DinamoBlok).FirstOrDefault(x => x is not null);
        return blok is int d ? $"Dinamo-blok {d}" : "";
    }

    public static string AlsTekst(Baankaart? k = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Kruiswissel in melder 24 - referentie (motoren 13 en 10); wat nog niet gevonden is, staat blanco");
        foreach (var b in Benen) sb.AppendLine($"  Been {b.Plek}: {(Gevonden(k, Midden) ? "24" : "__")} - {string.Join(" - ", b.Melders.Select(m => Gevonden(k, m) ? m.ToString() : "__"))}");
        sb.AppendLine("  Verbindingen (in beide richtingen):");
        foreach (var v in Verbindingen)
            sb.AppendLine(VerbindingGevonden(k, v)
                ? $"    {v.MelderA} <-> {v.MelderB}: motor 13 {St(v.Afbuigend13)}, motor 10 {St(v.Afbuigend10)} ({(v.Bewezen ? "bewezen" : "onbekend")})"
                : "    __ <-> __: nog niet gevonden door deze verkenning");
        sb.AppendLine("  Nog niet getest op de baan; wordt door de verkenner als laatste op elk pad door melder 24 toegepast.");
        return sb.ToString();
    }

    /// <summary>De plusfiguur (SVG) met de benen en alle melders in volgorde, plus de tabel met de standen van motor 13 en 10.</summary>
    public static string AlsHtml(Baankaart? k = null)
    {
        var sb = new StringBuilder();
        sb.Append("<h2>Kruiswissel in melder 24 (referentie motor 13 en 10)</h2><p class=\"uitleg\">De vier benen van de kruiswissel met al hun bezetmelders in volgorde (van 24 af naar buiten). Een leeg vakje is nog niet gevonden door deze verkenning. De tabel zegt hoe de motoren moeten staan voor een rit door melder 24. Alleen bewezen standen worden door de verkenner afgedwongen; <b>nog niet getest op de baan</b>. De richting van de benen op het papier is schematisch.</p>");
        sb.Append(AlsSvg(k));
        sb.Append("<table><tr><th>Rit door melder 24</th><th>Motor 13</th><th>Motor 10</th><th>Status</th><th>Bron</th></tr>");
        foreach (var v in Verbindingen)
            sb.Append(VerbindingGevonden(k, v)
                ? $"<tr><td>{v.MelderA} &harr; {v.MelderB}</td><td>{St(v.Afbuigend13)}</td><td>{St(v.Afbuigend10)}</td><td>{(v.Bewezen ? "bewezen" : "onbekend")}</td><td>{System.Net.WebUtility.HtmlEncode(v.Bron)}</td></tr>"
                : "<tr><td>&nbsp;</td><td></td><td></td><td>nog niet gevonden door deze verkenning</td><td></td></tr>");
        sb.Append("</table>");
        return sb.ToString();
    }

    public static string AlsSvg(Baankaart? k = null)
    {
        const int B = 700, H = 500, cx = B / 2, cy = H / 2, bw = 54, bh = 28, stap = 64;
        var sb = new StringBuilder();
        sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {B} {H}\" width=\"{B}\" height=\"{H}\" font-family=\"Segoe UI, Arial, sans-serif\" style=\"max-width:100%;height:auto;background:#fff\">");
        // richtingen per been: (dx, dy)
        var richting = new Dictionary<string, (int X, int Y)> { ["noord"] = (0, -1), ["west"] = (-1, 0), ["oost"] = (1, 0), ["zuid"] = (0, 1) };
        foreach (var b in Benen)
        {
            var (dx, dy) = richting[b.Plek];
            int n = b.Melders.Length + 1;
            int ex = cx + dx * stap * n, ey = cy + dy * (stap - 12) * n;
            sb.Append($"<line x1=\"{cx}\" y1=\"{cy}\" x2=\"{ex}\" y2=\"{ey}\" stroke=\"#44506a\" stroke-width=\"3\"/>");
            for (int i = 0; i < b.Melders.Length; i++)
            {
                int mx = dx != 0 ? cx + dx * stap * (i + 1) : cx;
                int my = dy != 0 ? cy + dy * (stap - 12) * (i + 1) : cy;
                sb.Append($"<rect x=\"{mx - bw / 2}\" y=\"{my - bh / 2}\" width=\"{bw}\" height=\"{bh}\" rx=\"5\" fill=\"#eef2ff\" stroke=\"#44506a\"/>");
                sb.Append($"<text x=\"{mx}\" y=\"{my + 5}\" text-anchor=\"middle\" font-size=\"14\" fill=\"#1c2333\">{(Gevonden(k, b.Melders[i]) ? b.Melders[i].ToString() : "")}</text>");
            }
            int lx = dx != 0 ? cx + dx * stap * b.Melders.Length : cx + 36;
            int ly = dx != 0 ? cy - 24 : cy + dy * (stap - 12) * b.Melders.Length + 5;
            sb.Append($"<text x=\"{lx}\" y=\"{ly}\" text-anchor=\"{(dx != 0 ? "middle" : "start")}\" font-size=\"12\" fill=\"#44506a\">{BlokLabel(k, b)}</text>");
        }
        sb.Append($"<rect x=\"{cx - 34}\" y=\"{cy - 20}\" width=\"68\" height=\"40\" rx=\"8\" fill=\"#fff4e0\" stroke=\"#c77700\" stroke-width=\"2\"/>");
        sb.Append($"<text x=\"{cx}\" y=\"{cy + 2}\" text-anchor=\"middle\" font-size=\"16\" font-weight=\"bold\" fill=\"#1c2333\">{(Gevonden(k, Midden) ? "24" : "")}</text>");
        sb.Append($"<text x=\"{cx}\" y=\"{cy + 15}\" text-anchor=\"middle\" font-size=\"9\" fill=\"#c77700\">motor 13 + 10</text>");
        sb.Append("</svg>");
        return sb.ToString();
    }
}
