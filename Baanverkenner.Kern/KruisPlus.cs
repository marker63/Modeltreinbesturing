using System.Text;

namespace Baanverkenner.Kern;

/// <summary>BUG #165 (Marco 16-10 17:55: "graag het plusteken ook weer in het rapport"): de plusfiguur van de kruiswissel uit wat DEZE verkenning gevonden heeft
/// (zonder referentie). Per been staan alle bezetmelders in volgorde van het midden (melder 24) naar buiten, met hun Dinamo-blok; per rit door het midden staat
/// de stand van beide motoren zoals die in de gereden ritten gezien is. Niets uit eerdere verkenningen.</summary>
public static class KruisPlus
{
    public record PlusBeen(string Plek, int[] Melders);
    public record PlusVerbinding(int A, int B, int Keer, string Stand13, string Stand10);

    private const int MaxDiepte = 4;

    public static (int Midden, int[] Motoren, List<PlusBeen> Benen, List<PlusVerbinding> Verbindingen)? Maak(Baankaart k)
    {
        if (k.KruisMotoren == "" || k.KruisMidden.Count == 0) return null;
        int midden = k.KruisMidden[0];
        var motoren = k.KruisMotoren.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(int.Parse).ToArray();
        // buren van het midden: elke melder die direct voor of na het midden gereden is
        var buren = k.Overgangen.Where(o => o.Van == midden).Select(o => o.Naar)
            .Concat(k.Overgangen.Where(o => o.Naar == midden).Select(o => o.Van))
            .Where(n => n != midden).Distinct().OrderBy(n => n).ToList();
        var benen = new List<(int Buur, List<int> Melders)>();
        foreach (var b in buren)
        {
            var rij = new List<int> { b };
            int vorige = midden, nu = b;
            while (rij.Count < MaxDiepte)
            {
                // verder naar buiten: via de doorgangen (ingangskant) als die er zijn, anders de enige andere overgang
                var volg = k.Doorgangen.Where(d => d.Via == vorige && d.Melder == nu).Select(d => d.Volgende).Distinct().ToList();
                if (volg.Count == 0)
                    volg = k.Overgangen.Where(o => o.Van == nu && o.Naar != vorige).Select(o => o.Naar)
                        .Concat(k.Overgangen.Where(o => o.Naar == nu && o.Van != vorige).Select(o => o.Van)).Distinct().ToList();
                // een been loopt alleen door zolang het ondubbelzinnig is, buiten het midden blijft en niet in een lus terugkomt
                if (volg.Count != 1 || volg[0] == midden || rij.Contains(volg[0]) || buren.Contains(volg[0])) break;
                // een been eindigt waar het Dinamo-blok verandert (de rest hoort bij een ander blok/been)
                var bl0 = k.Melders.FirstOrDefault(x => x.Nummer == b)?.DinamoBlok; var bl1 = k.Melders.FirstOrDefault(x => x.Nummer == volg[0])?.DinamoBlok;
                if (bl0 is not null && bl1 is not null && bl0 != bl1) break;
                rij.Add(volg[0]); vorige = nu; nu = volg[0];
            }
            benen.Add((b, rij));
        }
        // zelfde windrichtingen als de referentie waar de buurmelder overeenkomt, de rest vult de vrije plekken
        var plekken = new[] { "noord", "west", "oost", "zuid" };
        var gebruikt = new HashSet<string>();
        var uit = new List<PlusBeen>();
        foreach (var (buur, melders) in benen)
        {
            var ref1 = KruiswisselReferentie.Benen.FirstOrDefault(r => r.Melders[0] == buur);
            string? plek = ref1 is not null && gebruikt.Add(ref1.Plek) ? ref1.Plek : null;
            if (plek is null) { plek = plekken.FirstOrDefault(p => !gebruikt.Contains(p) && !benen.Any(x => KruiswisselReferentie.Benen.Any(r => r.Plek == p && r.Melders[0] == x.Buur))); if (plek is null) plek = plekken.FirstOrDefault(p => !gebruikt.Contains(p)); if (plek is null) continue; gebruikt.Add(plek); }
            uit.Add(new PlusBeen(plek, melders.ToArray()));
        }
        var verb = new List<PlusVerbinding>();
        foreach (var g in k.Doorgangen.Where(d => d.Melder == midden).GroupBy(d => (Math.Min(d.Via, d.Volgende), Math.Max(d.Via, d.Volgende))))
        {
            var cfgs = g.SelectMany(d => d.Configuraties).Distinct().Select(Configuratie.VanSleutel).ToList();
            string St(int motor)
            {
                if (cfgs.Count == 0) return "?";
                bool alleAf = cfgs.All(c => c.Bevat(motor)), alleRecht = cfgs.All(c => !c.Bevat(motor));
                return alleAf ? "afbuigend" : alleRecht ? "rechtdoor" : "vrij (beide gezien)";
            }
            verb.Add(new PlusVerbinding(g.Key.Item1, g.Key.Item2, g.Select(d => d.AantalKeer).DefaultIfEmpty(0).Max(), motoren.Length > 0 ? St(motoren[0]) : "?", motoren.Length > 1 ? St(motoren[1]) : "?"));
        }
        return (midden, motoren, uit, verb.OrderBy(v => v.A).ThenBy(v => v.B).ToList());
    }

    private static string BlokTekst(Baankaart k, int[] melders)
    {
        var blokken = melders.Select(m => k.Melders.FirstOrDefault(x => x.Nummer == m)?.DinamoBlok).Where(b => b is not null).Select(b => b!.Value).Distinct().ToList();
        return blokken.Count == 0 ? "" : "blok " + string.Join("/", blokken);
    }

    public static string AlsTekst(Baankaart k)
    {
        var p = Maak(k);
        if (p is null) return "";
        var (midden, motoren, benen, verb) = p.Value;
        var sb = new StringBuilder();
        sb.AppendLine($"  Plusteken van melder {midden} (motor {string.Join(" + ", motoren)}) - alleen wat deze verkenning zelf gevonden heeft; bezetmelders in volgorde van {midden} naar buiten:");
        foreach (var b in benen.OrderBy(b => b.Plek))
            sb.AppendLine($"    been {b.Plek,-5}: {midden} - {string.Join(" - ", b.Melders)}   ({BlokTekst(k, b.Melders)})");
        if (verb.Count == 0) sb.AppendLine("    Nog geen rit door het midden gereden.");
        foreach (var v in verb)
            sb.AppendLine($"    {v.A} <-> {v.B}: motor {(motoren.Length > 0 ? motoren[0] : 0)} {v.Stand13}, motor {(motoren.Length > 1 ? motoren[1] : 0)} {v.Stand10}  ({v.Keer}x gereden)");
        sb.AppendLine("    Nog niet getest op de baan; 'vrij' = beide standen zijn bij die rit gezien.");
        return sb.ToString();
    }

    public static string AlsHtml(Baankaart k)
    {
        var p = Maak(k);
        if (p is null) return "";
        var (midden, motoren, benen, verb) = p.Value;
        var sb = new StringBuilder();
        string E(string s) => System.Net.WebUtility.HtmlEncode(s);
        sb.Append($"<h2>Plusteken van melder {midden} (motor {E(string.Join(" + ", motoren))})</h2><p class=\"uitleg\">Alleen wat deze verkenning zelf gevonden heeft: per been alle bezetmelders in volgorde van het midden naar buiten, met het Dinamo-blok. De richting op het papier is schematisch. <b>Nog niet getest op de baan</b>.</p>");
        const int B = 760, H = 520, cx = B / 2, cy = H / 2, bw = 54, bh = 28, stap = 64;
        sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {B} {H}\" width=\"{B}\" height=\"{H}\" font-family=\"Segoe UI, Arial, sans-serif\" style=\"max-width:100%;height:auto;background:#fff\">");
        var richting = new Dictionary<string, (int X, int Y)> { ["noord"] = (0, -1), ["west"] = (-1, 0), ["oost"] = (1, 0), ["zuid"] = (0, 1) };
        foreach (var b in benen)
        {
            var (dx, dy) = richting[b.Plek];
            int n = b.Melders.Length + 1, sy = stap - 12;
            sb.Append($"<line x1=\"{cx}\" y1=\"{cy}\" x2=\"{cx + dx * stap * n}\" y2=\"{cy + dy * sy * n}\" stroke=\"#44506a\" stroke-width=\"3\"/>");
            for (int i = 0; i < b.Melders.Length; i++)
            {
                int mx = cx + dx * stap * (i + 1), my = cy + dy * sy * (i + 1);
                sb.Append($"<rect x=\"{mx - bw / 2}\" y=\"{my - bh / 2}\" width=\"{bw}\" height=\"{bh}\" rx=\"5\" fill=\"#eef2ff\" stroke=\"#44506a\"/>");
                sb.Append($"<text x=\"{mx}\" y=\"{my + 5}\" text-anchor=\"middle\" font-size=\"14\" fill=\"#1c2333\">{b.Melders[i]}</text>");
            }
            int lx = dx != 0 ? cx + dx * stap * b.Melders.Length : cx + 36;
            int ly = dx != 0 ? cy - 24 : cy + dy * sy * b.Melders.Length + 5;
            sb.Append($"<text x=\"{lx}\" y=\"{ly}\" text-anchor=\"{(dx != 0 ? "middle" : "start")}\" font-size=\"12\" fill=\"#44506a\">{E(BlokTekst(k, b.Melders))}</text>");
        }
        sb.Append($"<rect x=\"{cx - 34}\" y=\"{cy - 20}\" width=\"68\" height=\"40\" rx=\"8\" fill=\"#fff4e0\" stroke=\"#c77700\" stroke-width=\"2\"/>");
        sb.Append($"<text x=\"{cx}\" y=\"{cy + 2}\" text-anchor=\"middle\" font-size=\"16\" font-weight=\"bold\" fill=\"#1c2333\">{midden}</text>");
        sb.Append($"<text x=\"{cx}\" y=\"{cy + 15}\" text-anchor=\"middle\" font-size=\"9\" fill=\"#c77700\">motor {E(string.Join(" + ", motoren))}</text></svg>");
        sb.Append($"<table><tr><th>Rit door melder {midden}</th><th>Motor {(motoren.Length > 0 ? motoren[0] : 0)}</th><th>Motor {(motoren.Length > 1 ? motoren[1] : 0)}</th><th>Keer gereden</th></tr>");
        foreach (var v in verb) sb.Append($"<tr><td>{v.A} &harr; {v.B}</td><td>{E(v.Stand13)}</td><td>{E(v.Stand10)}</td><td>{v.Keer}</td></tr>");
        if (verb.Count == 0) sb.Append("<tr><td colspan=\"4\">Nog geen rit door het midden gereden.</td></tr>");
        sb.Append("</table>");
        return sb.ToString();
    }
}
