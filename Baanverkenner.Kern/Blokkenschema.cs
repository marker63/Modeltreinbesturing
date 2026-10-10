using System.Globalization;
using System.Text;

namespace Baanverkenner.Kern;

/// <summary>Eén vak (blok) in de tekening.</summary>
public class SchemaVak
{
    public int Nummer { get; set; }
    public int? DinamoBlok { get; set; }
    public string Melders { get; set; } = "";
    public List<string> Markeringen { get; set; } = new();
    /// <summary>"normaal", "splitsing" (een melder met twee mogelijke vervolgen, bijv. de kruiswissel) of "onbekend" (geen Dinamo-blok gevonden).</summary>
    public string Soort { get; set; } = "normaal";
    public double X { get; set; }   // middelpunt
    public double Y { get; set; }
    public double B { get; set; } = SchemaTekening.VakBreedte;
    public double H { get; set; } = SchemaTekening.VakHoogte;
    public string Titel => DinamoBlok is int d ? $"Blok {Nummer}  (Dinamo {d})" : $"Blok {Nummer}  (Dinamo ?)";
}

/// <summary>Een verbinding tussen twee blokken, met de rijrichting per overgang.</summary>
public class SchemaVerbinding
{
    public int Van { get; set; }     // bloknummer (de kleinste van het paar)
    public int Naar { get; set; }
    public bool PijlNaarNaar { get; set; }   // er is gereden Van -> Naar
    public bool PijlNaarVan { get; set; }    // er is gereden Naar -> Van
    public List<string> Labels { get; set; } = new();
    public (double X, double Y) Begin { get; set; }   // op de rand van het vak Van
    public (double X, double Y) Eind { get; set; }    // op de rand van het vak Naar
    public (double X, double Y) LabelPlek { get; set; }   // middelpunt van het label
    public double LabelBreedte => 14 + (Labels.Count == 0 ? 0 : Labels.Max(l => l.Length)) * 6.2;
    public double LabelHoogte => 6 + Labels.Count * 15;
}

/// <summary>Automatisch gemaakt blokkenschema van een baankaart: blokken in een kring in de volgorde waarin de testloc ze bereed,
/// met verbindingen (pijl = er is in die richting gereden). Gebruikt voor het venster in de Baanverkenner en voor de SVG in het rapport.</summary>
public class SchemaTekening
{
    public const double VakBreedte = 190;
    public const double VakHoogte = 74;

    public List<SchemaVak> Vakken { get; } = new();
    public List<SchemaVerbinding> Verbindingen { get; } = new();
    public double Breedte { get; private set; }
    public double Hoogte { get; private set; }

    public static SchemaTekening Maak(Baankaart k)
    {
        var blokken = k.VoorgesteldeBlokken.Count > 0 ? k.VoorgesteldeBlokken : BlokVoorstel.Bereken(k);
        var t = new SchemaTekening();
        if (blokken.Count == 0) { t.Breedte = 400; t.Hoogte = 120; return t; }

        var blokVanMelder = new Dictionary<int, int>();
        foreach (var b in blokken) foreach (var m in b.Melders) blokVanMelder[m] = b.Nummer;

        // Soort + markeringen per blok
        foreach (var b in blokken)
        {
            var v = new SchemaVak { Nummer = b.Nummer, DinamoBlok = b.DinamoBlok, Melders = string.Join(" – ", b.Melders) };
            bool splitsing = b.Melders.Any(m =>
                k.Overgangen.Where(o => o.Van == m).GroupBy(o => o.Richting).Any(g => g.Select(o => o.Naar).Distinct().Count() > 1));
            v.Soort = b.DinamoBlok is null ? "onbekend" : splitsing ? "splitsing" : "normaal";
            foreach (var kp in k.Kortsluitpunten.Where(x => b.Melders.Contains(x.NaMelder)))
                v.Markeringen.Add($"{(kp.Opgelost ? "opgelost" : kp.Opgegeven ? "opgegeven" : "open")} kortsluitpunt na {kp.NaMelder} ({kp.Richting.Tekst()})");
            foreach (var ks in k.Kopsporen.Where(x => b.Melders.Contains(x.Melder)))
                v.Markeringen.Add($"doodlopend na {ks.Melder} ({ks.Richting.Tekst()})");
            t.Vakken.Add(v);
        }

        // Volgorde in de kring: eerste keer dat de testloc het blok bereed; de rest op nummer
        var volgorde = new List<int>();
        void Voeg(int bloknr) { if (!volgorde.Contains(bloknr)) volgorde.Add(bloknr); }
        foreach (var tr in k.Trajecten)
            foreach (var m in tr.Reeks)
                if (blokVanMelder.TryGetValue(m, out var bn)) Voeg(bn);
        foreach (var b in blokken.OrderBy(b => b.Nummer)) Voeg(b.Nummer);

        // Verbindingen per paar blokken
        var paren = new Dictionary<(int, int), SchemaVerbinding>();
        foreach (var o in k.Overgangen)
        {
            if (!blokVanMelder.TryGetValue(o.Van, out var a) || !blokVanMelder.TryGetValue(o.Naar, out var bb) || a == bb) continue;
            var sleutel = (Math.Min(a, bb), Math.Max(a, bb));
            if (!paren.TryGetValue(sleutel, out var vb))
                paren[sleutel] = vb = new SchemaVerbinding { Van = sleutel.Item1, Naar = sleutel.Item2 };
            if (a == vb.Van) vb.PijlNaarNaar = true; else vb.PijlNaarVan = true;
            string label = $"{o.Van}→{o.Naar} {o.Richting.Tekst()}";
            string wissels = string.Join(", ", WisselsBij(k, o.Van, o.Naar));
            if (wissels.Length > 0) label += $" via {wissels}";
            if (!vb.Labels.Contains(label)) vb.Labels.Add(label);
        }
        t.Verbindingen.AddRange(paren.Values.OrderBy(v => v.Van).ThenBy(v => v.Naar));

        // Plaatsing op een ellips
        int n = volgorde.Count;
        double r = Math.Max(230, n * (VakBreedte + 40) / (2 * Math.PI));
        double rx = r * 1.6, ry = r * 1.1;
        double marge = 120;
        t.Breedte = 2 * rx + VakBreedte + 2 * marge;
        t.Hoogte = 2 * ry + VakHoogte + 2 * marge;
        for (int i = 0; i < n; i++)
        {
            double hoek = -Math.PI / 2 + 2 * Math.PI * i / n;
            var v = t.Vakken.First(x => x.Nummer == volgorde[i]);
            v.X = t.Breedte / 2 + rx * Math.Cos(hoek);
            v.Y = t.Hoogte / 2 + ry * Math.Sin(hoek);
        }

        // Verbindingslijnen van rand tot rand
        foreach (var vb in t.Verbindingen)
        {
            var a = t.Vakken.First(x => x.Nummer == vb.Van);
            var b = t.Vakken.First(x => x.Nummer == vb.Naar);
            vb.Begin = RandPunt(a, b.X, b.Y);
            vb.Eind = RandPunt(b, a.X, a.Y);
            // Label: in het midden; bij een kort stuk zijwaarts (naar buiten) verschoven zodat de pijlpunten zichtbaar blijven
            double mx = (vb.Begin.X + vb.Eind.X) / 2, my = (vb.Begin.Y + vb.Eind.Y) / 2;
            double dx = vb.Eind.X - vb.Begin.X, dy = vb.Eind.Y - vb.Begin.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < vb.LabelBreedte * 1.1 + 60 && len > 1e-9)
            {
                double nx = -dy / len, ny = dx / len;
                if ((mx - t.Breedte / 2) * nx + (my - t.Hoogte / 2) * ny < 0) { nx = -nx; ny = -ny; }
                double af = Math.Abs(nx) * (vb.LabelBreedte / 2) + Math.Abs(ny) * (vb.LabelHoogte / 2) + 12;
                mx += nx * af; my += ny * af;
            }
            mx = Math.Clamp(mx, vb.LabelBreedte / 2 + 4, t.Breedte - vb.LabelBreedte / 2 - 4);
            my = Math.Clamp(my, vb.LabelHoogte / 2 + 4, t.Hoogte - vb.LabelHoogte / 2 - 4);
            vb.LabelPlek = (mx, my);
        }
        return t;
    }

    /// <summary>Wissels (adres + stand) die bij de overgang van melder a naar melder b zijn waargenomen.</summary>
    private static IEnumerable<string> WisselsBij(Baankaart k, int a, int b)
    {
        var res = new List<string>();
        foreach (var w in k.Wissels)
            foreach (var obs in w.Waarnemingen)
            {
                if (obs.NaMelder != a) continue;
                if (obs.VolgendeBijAfbuigend == b) res.Add($"wissel {w.Adres} afbuigend");
                else if (obs.VolgendeBijRechtdoor == b) res.Add($"wissel {w.Adres} rechtdoor");
            }
        return res.Distinct();
    }

    /// <summary>Het punt waar de lijn van het middelpunt van het vak naar (x, y) de rand van het vak snijdt.</summary>
    private static (double, double) RandPunt(SchemaVak v, double x, double y)
    {
        double dx = x - v.X, dy = y - v.Y;
        if (Math.Abs(dx) < 1e-9 && Math.Abs(dy) < 1e-9) return (v.X, v.Y);
        double sx = Math.Abs(dx) < 1e-9 ? double.MaxValue : (v.B / 2) / Math.Abs(dx);
        double sy = Math.Abs(dy) < 1e-9 ? double.MaxValue : (v.H / 2) / Math.Abs(dy);
        double s = Math.Min(sx, sy);
        return (v.X + dx * s, v.Y + dy * s);
    }

    private static string F(double d) => d.ToString("0.#", CultureInfo.InvariantCulture);
    private static string E(string s) => System.Net.WebUtility.HtmlEncode(s);

    /// <summary>De tekening als SVG (in een HTML-pagina te plakken of los op te slaan).</summary>
    public string AlsSvg(bool metXmlKop = false)
    {
        var sb = new StringBuilder();
        if (metXmlKop) sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {F(Breedte)} {F(Hoogte)}\" width=\"{F(Breedte)}\" height=\"{F(Hoogte)}\" font-family=\"Segoe UI, Arial, sans-serif\" style=\"max-width:100%;height:auto;background:#fff\">");
        sb.Append("<defs><marker id=\"pijl\" viewBox=\"0 0 10 10\" refX=\"9\" refY=\"5\" markerWidth=\"9\" markerHeight=\"9\" orient=\"auto-start-reverse\"><path d=\"M0,0 L10,5 L0,10 z\" fill=\"#44506a\"/></marker></defs>");

        foreach (var vb in Verbindingen)
        {
            string markers = (vb.PijlNaarNaar ? " marker-end=\"url(#pijl)\"" : "") + (vb.PijlNaarVan ? " marker-start=\"url(#pijl)\"" : "");
            sb.Append($"<line x1=\"{F(vb.Begin.X)}\" y1=\"{F(vb.Begin.Y)}\" x2=\"{F(vb.Eind.X)}\" y2=\"{F(vb.Eind.Y)}\" stroke=\"#44506a\" stroke-width=\"2\"{markers}/>");
        }
        foreach (var vb in Verbindingen)
        {
            if (vb.Labels.Count == 0) continue;
            double mx = vb.LabelPlek.X, my = vb.LabelPlek.Y;
            double bh = vb.LabelHoogte;
            double bw = vb.LabelBreedte;
            sb.Append($"<rect x=\"{F(mx - bw / 2)}\" y=\"{F(my - bh / 2)}\" width=\"{F(bw)}\" height=\"{F(bh)}\" rx=\"4\" fill=\"#fff\" fill-opacity=\"0.92\" stroke=\"#c9cfdb\"/>");
            for (int i = 0; i < vb.Labels.Count; i++)
                sb.Append($"<text x=\"{F(mx)}\" y=\"{F(my - bh / 2 + 15 + i * 15 - 2)}\" font-size=\"11\" text-anchor=\"middle\" fill=\"#333\">{E(vb.Labels[i])}</text>");
        }
        foreach (var v in Vakken)
        {
            string vul = v.Soort == "splitsing" ? "#fff1dc" : v.Soort == "onbekend" ? "#f4f4f4" : "#e6f2ee";
            string rand = v.Soort == "splitsing" ? "#c27a1a" : v.Soort == "onbekend" ? "#999" : "#1f6f5c";
            string streep = v.Soort == "onbekend" ? " stroke-dasharray=\"6 4\"" : "";
            double x = v.X - v.B / 2, y = v.Y - v.H / 2;
            sb.Append($"<rect x=\"{F(x)}\" y=\"{F(y)}\" width=\"{F(v.B)}\" height=\"{F(v.H)}\" rx=\"8\" fill=\"{vul}\" stroke=\"{rand}\" stroke-width=\"2\"{streep}/>");
            sb.Append($"<text x=\"{F(v.X)}\" y=\"{F(y + 20)}\" font-size=\"14\" font-weight=\"600\" text-anchor=\"middle\" fill=\"#1d2330\">{E(v.Titel)}</text>");
            sb.Append($"<text x=\"{F(v.X)}\" y=\"{F(y + 40)}\" font-size=\"12\" text-anchor=\"middle\" fill=\"#333\">melders {E(v.Melders)}</text>");
            if (v.Markeringen.Count > 0)
                sb.Append($"<text x=\"{F(v.X)}\" y=\"{F(y + 58)}\" font-size=\"10.5\" text-anchor=\"middle\" fill=\"#a12a2a\">{E(v.Markeringen[0])}{(v.Markeringen.Count > 1 ? $" (+{v.Markeringen.Count - 1})" : "")}</text>");
        }
        sb.Append("</svg>");
        return sb.ToString();
    }
}
