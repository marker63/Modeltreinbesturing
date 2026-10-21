using System.Globalization;
using System.Text;

namespace Baanverkenner.Kern;

/// <summary>BUG #199 (Marco 17-10 20:14: "kan je net als het blokkenschema ook een melderschema maken?"): de baan als graaf van melders. Elke melder is een knoop, elke
/// gereden overgang een lijn; bij een lijn staat welke wissel (en welke stand) daar hoort. Een melder met drie of meer buren (kruiswissel, splitsing in of bij de melder)
/// is oranje. Is het Dinamo-blok van een melder bekend, dan staat dat eronder; zo is het ook tijdens een rondrit (zonder bloknummers) te gebruiken.
/// De plaatsing is deterministisch (zelfde kaart = zelfde tekening): de langste bekende rit wordt als ring gelegd, de rest erbij gezet en daarna met een korte
/// kracht-simulatie uit elkaar getrokken. Nog niet getest op de baan.</summary>
public class Melderschema
{
    public const double KnoopB = 34, KnoopH = 22;

    public class Knoop
    {
        public int Melder { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public int? DinamoBlok { get; set; }
        public int AantalBuren { get; set; }
        public bool Splitsing => AantalBuren >= 3;
    }

    public class Rand
    {
        public int A { get; set; }
        public int B { get; set; }
        public bool GereedenAB { get; set; }
        public bool GereedenBA { get; set; }
        public List<string> Labels { get; } = new();
        public (double X, double Y) LabelPlek { get; set; }
    }

    public List<Knoop> Knopen { get; } = new();
    public List<Rand> Randen { get; } = new();
    public double Breedte { get; private set; }
    public double Hoogte { get; private set; }

    public static Melderschema Maak(Baankaart k)
    {
        var s = new Melderschema();
        var nummers = k.Melders.Select(m => m.Nummer).Union(k.Overgangen.SelectMany(o => new[] { o.Van, o.Naar })).Distinct().OrderBy(x => x).ToList();
        var randen = new Dictionary<(int, int), Rand>();
        Rand Zoek(int a, int b)
        {
            var sl = a < b ? (a, b) : (b, a);
            if (!randen.TryGetValue(sl, out var r)) randen[sl] = r = new Rand { A = sl.Item1, B = sl.Item2 };
            return r;
        }
        foreach (var o in k.Overgangen.Where(o => o.AantalKeer > 0 || o.GezienBijConfiguraties.Count > 0))
        {
            var r = Zoek(o.Van, o.Naar);
            if (o.Van == r.A) r.GereedenAB = true; else r.GereedenBA = true;
        }
        void Label(int a, int b, string tekst)
        {
            var sl = a < b ? (a, b) : (b, a);
            if (!randen.TryGetValue(sl, out var r)) return;   // alleen op gereden verbindingen
            if (!r.Labels.Contains(tekst)) r.Labels.Add(tekst);
        }
        foreach (var w in k.Wissels.OrderBy(w => w.Adres))
            foreach (var o in w.Waarnemingen)
            {
                switch (o.Soort)
                {
                    case WaarnemingSoort.Splitsing:
                        if (o.VolgendeBijRechtdoor is int rd && o.VolgendeBijRechtdoor != o.VolgendeBijAfbuigend) Label(o.NaMelder, rd, $"W{w.Adres} {k.StandKort(w.Adres, false)}");   // BUG #247: stand op de baan
                        if (o.VolgendeBijAfbuigend is int af && o.VolgendeBijRechtdoor != o.VolgendeBijAfbuigend) Label(o.NaMelder, af, $"W{w.Adres} {k.StandKort(w.Adres, true)}");
                        break;
                    case WaarnemingSoort.KortsluitingBijOmzetten:
                        if (o.Via is int via) Label(via, o.NaMelder, $"W{w.Adres} niet {k.StandKort(w.Adres, true)} (kortsl.)");
                        break;
                    case WaarnemingSoort.LostKortsluitingOp:
                        if (o.VolgendeBijAfbuigend is int af2) Label(o.NaMelder, af2, $"W{w.Adres} {k.StandKort(w.Adres, true)}");
                        break;
                }
            }
        s.Randen.AddRange(randen.Values.OrderBy(r => r.A).ThenBy(r => r.B));

        var buren = nummers.ToDictionary(n => n, n => new HashSet<int>());
        foreach (var r in s.Randen) { buren[r.A].Add(r.B); buren[r.B].Add(r.A); }
        foreach (var n in nummers)
            s.Knopen.Add(new Knoop { Melder = n, AantalBuren = buren[n].Count, DinamoBlok = k.Melders.FirstOrDefault(m => m.Nummer == n)?.DinamoBlok });

        s.Plaats(k, buren);
        foreach (var r in s.Randen)
        {
            var a = s.Knopen.First(x => x.Melder == r.A); var b = s.Knopen.First(x => x.Melder == r.B);
            double bw = LabelB(r), bh = LabelH(r);
            r.LabelPlek = (Math.Clamp((a.X + b.X) / 2, bw / 2 + 4, s.Breedte - bw / 2 - 4), Math.Clamp((a.Y + b.Y) / 2, bh / 2 + 4, s.Hoogte - bh / 2 - 4));
        }
        return s;
    }

    private void Plaats(Baankaart k, Dictionary<int, HashSet<int>> buren)
    {
        int n = Knopen.Count;
        Breedte = Math.Max(1000, 64.0 * n); Hoogte = Math.Max(660, 40.0 * n);
        if (n == 0) return;
        double cx = Breedte / 2, cy = Hoogte / 2, rx = Breedte / 2 - 150, ry = Hoogte / 2 - 110;
        var pos = new Dictionary<int, (double X, double Y)>();

        // de ring: de langste bekende rit (zonder herhaling); anders alle melders op volgorde van nummer
        var ring = new List<int>();
        // liefst de lus zonder omgezette wissels (de hoofdlus); zijtakken en lussen via een omgezette wissel komen er als uitsteeksel naast
        var langste = k.Trajecten.Where(t => t.Einde == RitEinde.Lus && t.Reeks.Distinct().Count() >= 3)
                          .OrderBy(t => t.Configuratie.Afbuigend.Length).ThenByDescending(t => t.Reeks.Distinct().Count()).FirstOrDefault()
                      ?? k.Trajecten.OrderByDescending(t => t.Reeks.Distinct().Count()).FirstOrDefault();
        if (langste is not null) foreach (var m in langste.Reeks) if (!ring.Contains(m) && buren.ContainsKey(m)) ring.Add(m);
        if (ring.Count < 3) { ring = Knopen.Select(x => x.Melder).ToList(); }
        for (int i = 0; i < ring.Count; i++)
        {
            double hoek = 2 * Math.PI * i / ring.Count - Math.PI / 2;
            pos[ring[i]] = (cx + rx * Math.Cos(hoek), cy + ry * Math.Sin(hoek));
        }
        // de rest: bij de gemiddelde plek van de al geplaatste buren, naar buiten geduwd
        var over = Knopen.Select(x => x.Melder).Where(m => !pos.ContainsKey(m)).OrderBy(m => m).ToList();
        for (int ronde = 0; ronde < over.Count + 2 && over.Count > 0; ronde++)
            foreach (var m in over.ToList())
            {
                var geplaatst = buren[m].Where(pos.ContainsKey).ToList();
                if (geplaatst.Count == 0) continue;
                double mx = geplaatst.Average(p => pos[p].X), my = geplaatst.Average(p => pos[p].Y);
                double dx = mx - cx, dy = my - cy, len = Math.Sqrt(dx * dx + dy * dy);
                if (len < 1e-6) { dx = 1; dy = 0; len = 1; }
                int zelfde = pos.Values.Count(p => Math.Abs(p.X - (mx + dx / len * 150)) < 60 && Math.Abs(p.Y - (my + dy / len * 150)) < 60);
                double hoekDraai = zelfde * 0.7;
                double ux = dx / len * Math.Cos(hoekDraai) - dy / len * Math.Sin(hoekDraai), uy = dx / len * Math.Sin(hoekDraai) + dy / len * Math.Cos(hoekDraai);
                pos[m] = (mx + ux * 150, my + uy * 150);
                over.Remove(m);
            }
        // BUG #286 (Marco 21-10 16:04/16:12: "de volgorde zou moeten zijn 139, 140, 132, 133, 136, 144, 143; in het schema staan ze in oplopende nummering"): een los traject (niet verbonden met de
        // ring) stond als één rij op nummer onderaan. Nu krijgt elk los traject een eigen rij, in de volgorde van het spoor: vanaf een uiteinde langs de buren.
        int rij = 0;
        while (over.Count > 0)
        {
            var deel = new HashSet<int> { over[0] }; var rijQ = new Queue<int>(); rijQ.Enqueue(over[0]);
            while (rijQ.Count > 0) foreach (var b in buren[rijQ.Dequeue()].Where(over.Contains)) if (deel.Add(b)) rijQ.Enqueue(b);
            int begin = deel.OrderBy(m => buren[m].Count(deel.Contains)).ThenBy(m => m).First();
            var volgorde = new List<int>(); var bezocht = new HashSet<int>();
            void Loop(int m) { if (!bezocht.Add(m)) return; volgorde.Add(m); foreach (var b in buren[m].Where(deel.Contains).OrderBy(b => buren[b].Count(deel.Contains)).ThenBy(b => b)) Loop(b); }
            Loop(begin);
            double stap = Math.Min(90, (Breedte - 120) / Math.Max(1, volgorde.Count - 1));
            for (int i = 0; i < volgorde.Count; i++) pos[volgorde[i]] = (60 + i * stap, Hoogte - 40 - rij * 70);
            foreach (var m in volgorde) over.Remove(m);
            rij++;
        }

        // alleen uit elkaar duwen wat te dicht op elkaar ligt (de ring blijft zo een ring); vast aantal rondes, geen toeval
        var lijst = Knopen.Select(x => x.Melder).ToList();
        for (int it = 0; it < 80; it++)
        {
            bool geduwd = false;
            for (int i = 0; i < lijst.Count; i++)
                for (int j = i + 1; j < lijst.Count; j++)
                {
                    double dx = pos[lijst[j]].X - pos[lijst[i]].X, dy = pos[lijst[j]].Y - pos[lijst[i]].Y;
                    double dist = Math.Sqrt(dx * dx + dy * dy);
                    if (dist >= 80) continue;
                    if (dist < 1e-6) { dx = 1; dy = 0.5; dist = Math.Sqrt(1.25); }
                    double duw = (80 - dist) / 2 + 0.5;
                    pos[lijst[i]] = (pos[lijst[i]].X - dx / dist * duw, pos[lijst[i]].Y - dy / dist * duw);
                    pos[lijst[j]] = (pos[lijst[j]].X + dx / dist * duw, pos[lijst[j]].Y + dy / dist * duw);
                    geduwd = true;
                }
            foreach (var m in lijst) pos[m] = (Math.Clamp(pos[m].X, 70, Breedte - 70), Math.Clamp(pos[m].Y, 50, Hoogte - 50));
            if (!geduwd) break;
        }
        foreach (var kn in Knopen) { kn.X = Math.Round(pos[kn.Melder].X, 1); kn.Y = Math.Round(pos[kn.Melder].Y, 1); }
    }

    private static double LabelB(Rand r) => r.Labels.Count == 0 ? 0 : 12 + r.Labels.Max(l => l.Length) * 5.6;
    private static double LabelH(Rand r) => r.Labels.Count == 0 ? 0 : 4 + r.Labels.Count * 14;

    private static string F(double d) => d.ToString("0.#", CultureInfo.InvariantCulture);
    private static string E(string s) => System.Net.WebUtility.HtmlEncode(s);

    /// <summary>De tekening als SVG (in een HTML-pagina te plakken of los op te slaan).</summary>
    public string AlsSvg(bool metXmlKop = false)
    {
        var sb = new StringBuilder();
        if (metXmlKop) sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {F(Breedte)} {F(Hoogte)}\" width=\"{F(Breedte)}\" height=\"{F(Hoogte)}\" font-family=\"Segoe UI, Arial, sans-serif\" style=\"max-width:100%;height:auto;background:#fff\">");
        foreach (var r in Randen)
        {
            var a = Knopen.First(x => x.Melder == r.A); var b = Knopen.First(x => x.Melder == r.B);
            sb.Append($"<line x1=\"{F(a.X)}\" y1=\"{F(a.Y)}\" x2=\"{F(b.X)}\" y2=\"{F(b.Y)}\" stroke=\"#44506a\" stroke-width=\"2\"/>");
        }
        foreach (var r in Randen.Where(r => r.Labels.Count > 0))
        {
            double bh = LabelH(r), bw = LabelB(r);
            sb.Append($"<rect x=\"{F(r.LabelPlek.X - bw / 2)}\" y=\"{F(r.LabelPlek.Y - bh / 2)}\" width=\"{F(bw)}\" height=\"{F(bh)}\" rx=\"4\" fill=\"#fff7d6\" fill-opacity=\"0.95\" stroke=\"#8a6d00\"/>");
            for (int i = 0; i < r.Labels.Count; i++)
                sb.Append($"<text x=\"{F(r.LabelPlek.X)}\" y=\"{F(r.LabelPlek.Y - bh / 2 + 13 + i * 14)}\" font-size=\"10.5\" text-anchor=\"middle\" fill=\"#5c4700\">{E(r.Labels[i])}</text>");
        }
        foreach (var kn in Knopen)
        {
            string rand = kn.Splitsing ? "#c27a1a" : "#1f6f5c", vul = kn.Splitsing ? "#fff1dc" : "#e6f2ee";
            sb.Append($"<g><title>melder {kn.Melder}: {kn.AantalBuren} buren{(kn.DinamoBlok is int bl ? $", Dinamo-blok {bl}" : ", Dinamo-blok onbekend")}</title><rect x=\"{F(kn.X - KnoopB / 2)}\" y=\"{F(kn.Y - KnoopH / 2)}\" width=\"{F(KnoopB)}\" height=\"{F(KnoopH)}\" rx=\"6\" fill=\"{vul}\" stroke=\"{rand}\" stroke-width=\"2\"/>");
            sb.Append($"<text x=\"{F(kn.X)}\" y=\"{F(kn.Y + 4.5)}\" font-size=\"12\" font-weight=\"700\" text-anchor=\"middle\" fill=\"#1d2330\">{kn.Melder}</text>");
            if (kn.DinamoBlok is int blok) sb.Append($"<text x=\"{F(kn.X)}\" y=\"{F(kn.Y + KnoopH / 2 + 12)}\" font-size=\"10\" text-anchor=\"middle\" fill=\"#555\">blok {blok}</text>");
            sb.Append("</g>");
        }
        sb.Append("</svg>");
        return sb.ToString();
    }
}
