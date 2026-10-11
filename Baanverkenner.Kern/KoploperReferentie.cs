using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Baanverkenner.Kern;

/// <summary>
/// BUG #84. Referentie uit een Koploper-export ("Koploper_export_..._002.xlsx"): welk Koploper-blok (= Dinamo-blokkaart)
/// bij welke melder hoort, en welke blokkaarten alleen via een WISSELSTRAAT worden aangestuurd.
/// In de export van Marco staan op de blokkaart ook de nummers 8 en 9; die hebben geen gewoon blok maar een lijst
/// wisselstraat-overgangen (8: 1->4, 1->7, 3->7, 4->1, 4->3, 7->1, 7->3; 9: 1->2, 1->4, 1->7, 2->3, 3->7, 4->1, 4->3, 7->1, 7->3).
/// De melders 24 en 129 horen bij geen blok maar hebben alleen overgangen: dat zijn doorgangsmelders (het wisselgebied),
/// en hun rijstroom komt dus van zo'n wisselstraatblok. De referentie is alleen een hulpmiddel voor de blokproef (de volgorde
/// van proeven en een waarschuwing bij afwijking); de blokproef zelf blijft altijd meten.
/// </summary>
public class KoploperReferentie
{
    public class MelderItem
    {
        public int Melder { get; set; }
        /// <summary>Koploper-blok waarin de melder "bezet blok" meldt; null = doorgangsmelder (alleen overgangen).</summary>
        public int? Blok { get; set; }
        /// <summary>Overgangen "van naar" (tussen Koploper-blokken) waarbij deze melder wordt gepasseerd, bv. "4>1".</summary>
        public List<string> Overgangen { get; set; } = new();
    }

    public class WisselstraatBlokItem
    {
        public int Blokkaart { get; set; }
        public List<string> Overgangen { get; set; } = new();
    }

    public int FormaatVersie { get; set; } = 1;
    public List<MelderItem> Melders { get; set; } = new();
    public List<WisselstraatBlokItem> WisselstraatBlokken { get; set; } = new();

    public MelderItem? Melder(int nummer) => Melders.FirstOrDefault(m => m.Melder == nummer);

    /// <summary>Koploper-blok (= blokkaart) van een melder, of null als de melder onbekend of een doorgangsmelder is.</summary>
    public int? BlokVan(int melder) => Melder(melder)?.Blok;

    public bool IsDoorgangsmelder(int melder) => Melder(melder) is { Blok: null } m && m.Overgangen.Count > 0;

    /// <summary>De wisselstraatblokken die de overgangen van een doorgangsmelder allemaal bevatten, de meest passende
    /// (minste overbodige overgangen) eerst. <paramref name="gebruikt"/> = blokken die al aan een andere melder zijn gekoppeld;
    /// die komen achteraan. Leeg als de melder geen doorgangsmelder is.</summary>
    public List<int> WisselstraatBlokkenVoor(int melder, ISet<int>? gebruikt = null)
    {
        var m = Melder(melder);
        if (m is null || m.Blok is not null || m.Overgangen.Count == 0) return new List<int>();
        var eigen = m.Overgangen.ToHashSet();
        return WisselstraatBlokken
            .Where(b => eigen.IsSubsetOf(b.Overgangen))
            .OrderBy(b => gebruikt != null && gebruikt.Contains(b.Blokkaart) ? 1 : 0)
            .ThenBy(b => b.Overgangen.Count)
            .ThenBy(b => b.Blokkaart)
            .Select(b => b.Blokkaart)
            .ToList();
    }

    /// <summary>Is <paramref name="blok"/> volgens Koploper een mogelijke rijstroombron voor deze melder? Null = Koploper zegt er niets over.</summary>
    public bool? KlopptMetKoploper(int melder, int blok)
    {
        var m = Melder(melder);
        if (m is null) return null;
        if (m.Blok is int b) return b == blok;
        var kand = WisselstraatBlokkenVoor(melder);
        return kand.Count == 0 ? null : kand.Contains(blok);
    }

    public string Omschrijving(int melder)
    {
        var m = Melder(melder);
        if (m is null) return "onbekend in Koploper";
        if (m.Blok is int b) return $"Koploper-blok {b}";
        var kand = WisselstraatBlokkenVoor(melder);
        return kand.Count == 0 ? "doorgangsmelder (geen blok)" : $"doorgangsmelder in wisselstraatblok {string.Join(" of ", kand)}";
    }

    // ---------------------------------------------------------------- opslag

    public string AlsJson() => JsonSerializer.Serialize(this, Baankaart.JsonOpties);
    public static KoploperReferentie? VanJson(string json) => JsonSerializer.Deserialize<KoploperReferentie>(json, Baankaart.JsonOpties);

    /// <summary>Laadt een referentie uit .json of rechtstreeks uit een Koploper-export (.xlsx). Een ontbrekend of kapot bestand geeft null.</summary>
    public static KoploperReferentie? Laad(string pad)
    {
        try
        {
            if (!File.Exists(pad)) return null;
            return pad.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) ? LeesExport(pad) : VanJson(File.ReadAllText(pad));
        }
        catch { return null; }
    }

    public void Bewaar(string pad) => File.WriteAllText(pad, AlsJson());

    // ---------------------------------------------------------------- export (.xlsx) lezen, zonder externe pakketten

    private static readonly Regex Cel = new(@"^([A-Z]+)(\d+)$", RegexOptions.Compiled);

    /// <summary>Leest de melderlijst (kolom E = melder, kolom F = "Van x naar y" / "Bezet blok: n") en de blokkaart (kolom N = blokkaart,
    /// O = gewoon blok, P = wisselstraat-overgang "a -> b") uit de Koploper-export van de functiedecoders.</summary>
    public static KoploperReferentie LeesExport(string xlsxPad)
    {
        using var zip = ZipFile.OpenRead(xlsxPad);
        var gedeeld = new List<string>();
        var ss = zip.GetEntry("xl/sharedStrings.xml");
        if (ss != null)
        {
            using var s = ss.Open();
            foreach (var si in XDocument.Load(s).Descendants().Where(e => e.Name.LocalName == "si"))
                gedeeld.Add(string.Concat(si.Descendants().Where(e => e.Name.LocalName == "t").Select(e => e.Value)));
        }
        var blad = zip.Entries.Where(e => e.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase) && e.FullName.EndsWith(".xml"))
                              .OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase).First();

        var cellen = new List<(int Rij, string Kol, string Waarde)>();
        using (var s = blad.Open())
            foreach (var c in XDocument.Load(s).Descendants().Where(e => e.Name.LocalName == "c"))
            {
                var m = Cel.Match((string?)c.Attribute("r") ?? "");
                if (!m.Success) continue;
                string t = (string?)c.Attribute("t") ?? "";
                string? v = c.Elements().FirstOrDefault(e => e.Name.LocalName == "v")?.Value;
                string waarde;
                if (t == "s" && int.TryParse(v, out int idx) && idx >= 0 && idx < gedeeld.Count) waarde = gedeeld[idx];
                else if (t == "inlineStr") waarde = string.Concat(c.Descendants().Where(e => e.Name.LocalName == "t").Select(e => e.Value));
                else waarde = v ?? "";
                if (waarde.Length > 0) cellen.Add((int.Parse(m.Groups[2].Value), m.Groups[1].Value, waarde.Trim()));
            }

        var res = new KoploperReferentie();
        MelderItem? huidig = null;
        foreach (var (rij, kol, w) in cellen.OrderBy(c => c.Rij))
        {
            if (kol == "E" && int.TryParse(w, out int mn))
            {
                huidig = new MelderItem { Melder = mn };
                res.Melders.Add(huidig);
            }
            else if (kol == "F" && huidig != null)
            {
                var van = Regex.Match(w, @"^Van\s+(\d+)\s+naar\s+(\d+)$", RegexOptions.IgnoreCase);
                var bezet = Regex.Match(w, @"^Bezet blok:\s*(\d+)$", RegexOptions.IgnoreCase);
                if (van.Success) huidig.Overgangen.Add($"{van.Groups[1].Value}>{van.Groups[2].Value}");
                else if (bezet.Success) huidig.Blok = int.Parse(bezet.Groups[1].Value);
            }
        }

        // Blokkaart: per rij N (blokkaart) met in O het gewone blok óf in P een wisselstraat-overgang.
        var perRij = cellen.GroupBy(c => c.Rij).OrderBy(g => g.Key);
        foreach (var g in perRij)
        {
            string? n = g.FirstOrDefault(c => c.Kol == "N").Waarde;
            string? p = g.FirstOrDefault(c => c.Kol == "P").Waarde;
            if (n is null || p is null || !int.TryParse(n, out int kaart)) continue;
            var pm = Regex.Match(p, @"^(\d+)\s*->\s*(\d+)$");
            if (!pm.Success) continue;
            var blok = res.WisselstraatBlokken.FirstOrDefault(b => b.Blokkaart == kaart);
            if (blok is null) res.WisselstraatBlokken.Add(blok = new WisselstraatBlokItem { Blokkaart = kaart });
            blok.Overgangen.Add($"{pm.Groups[1].Value}>{pm.Groups[2].Value}");
        }
        return res;
    }
}
