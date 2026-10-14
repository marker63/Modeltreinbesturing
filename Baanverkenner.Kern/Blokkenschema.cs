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
    public string Label { get; set; } = "";
    /// <summary>BUG #90: "rechthoek" of "kruis" (de kruiswissel, getekend als een X met de vier benen in de hoeken).</summary>
    public string Vorm { get; set; } = "rechthoek";
    /// <summary>BUG #90: de bezetmelders die aan dit vak grenzen, op de zijde die naar het aangrenzende blok wijst.</summary>
    public List<SchemaMelder> Chips { get; } = new();
    /// <summary>Alleen bij de kruiswissel: waar de naam staat ("onder", "boven", "links" of "rechts"), aan de kant waar geen been naartoe loopt.</summary>
    public string TitelPlek { get; set; } = "onder";
    /// <summary>BUG #88: alleen het echte Dinamo-bloknummer, geen verzonnen nummer.</summary>
    public string Titel => DinamoBlok is int ? $"Dinamo-blok {Label}" : $"Dinamo-blok {Label}";
}

/// <summary>BUG #90: een bezetmelder (klein vlakje) op de rand van een vak, of in de hoek van de kruiswissel.</summary>
public class SchemaMelder
{
    public const double Breedte = 28, Hoogte = 18;
    public int Melder { get; set; }
    public double X { get; set; }       // middelpunt
    public double Y { get; set; }
    public double UitX { get; set; }    // richting van het vak af (eenheidsvector)
    public double UitY { get; set; }
    public int Zijde { get; set; } = -1;   // 0 boven, 1 rechts, 2 onder, 3 links, -1 hoek van het kruis
    internal List<(double Coord, int PaarIndex)> Buren { get; } = new();
    /// <summary>Het punt net buiten het vlakje waar de verbindingslijn begint of eindigt.</summary>
    public (double X, double Y) Anker
    {
        get
        {
            double d = Zijde is 0 or 2 ? Hoogte / 2 : Breedte / 2;
            if (Zijde == -1) d = 16;
            return (X + UitX * d, Y + UitY * d);
        }
    }
}

/// <summary>Een verbinding tussen twee blokken, met de rijrichting per overgang.</summary>
public class SchemaVerbinding
{
    public int Van { get; set; }     // bloknummer (de kleinste van het paar)
    public int Naar { get; set; }
    /// <summary>BUG #90: de bezetmelder in het vak Van / Naar waar deze verbinding aan vastzit.</summary>
    public int VanMelder { get; set; }
    public int NaarMelder { get; set; }
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
    private List<int> _kruisMidden = new();
    public double Breedte { get; private set; }
    public double Hoogte { get; private set; }

    public static SchemaTekening Maak(Baankaart k)
    {
        var blokken = k.VoorgesteldeBlokken.Count > 0 ? k.VoorgesteldeBlokken : BlokVoorstel.Bereken(k);
        var t = new SchemaTekening();
        t._kruisMidden = k.KruisMidden.ToList();   // BUG #129: alleen de kruiswissel van DEZE verkenning (opgegeven of ontdekt)
        if (blokken.Count == 0) { t.Breedte = 400; t.Hoogte = 120; return t; }

        var blokVanMelder = new Dictionary<int, int>();
        foreach (var b in blokken) foreach (var m in b.Melders) blokVanMelder[m] = b.Nummer;

        // Soort + markeringen per blok
        foreach (var b in blokken)
        {
            var v = new SchemaVak { Nummer = b.Nummer, Label = b.Label, DinamoBlok = b.DinamoBlok, Melders = string.Join(" – ", b.Melders) };
            bool splitsing = b.Melders.Any(m =>
                k.Overgangen.Where(o => o.Van == m).GroupBy(o => o.Richting).Any(g => g.Select(o => o.Naar).Distinct().Count() > 1));
            v.Soort = b.DinamoBlok is null ? "onbekend" : splitsing ? "splitsing" : "normaal";
            foreach (var kp in k.Kortsluitpunten.Where(x => b.Melders.Contains(x.NaMelder)))
                v.Markeringen.Add($"{(kp.Opgelost ? "opgelost" : kp.Tijdelijk ? "tijdelijk" : kp.Opgegeven ? "opgegeven" : "open")} kortsluitpunt na {kp.NaMelder} ({kp.Richting.Tekst()})");
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

        // BUG #90: één verbinding per paar bezetmelders (niet meer per paar blokken), zodat elke melder aan de goede zijde komt
        var paren = new Dictionary<(int, int), SchemaVerbinding>();
        foreach (var o in k.Overgangen)
        {
            if (!blokVanMelder.TryGetValue(o.Van, out var a) || !blokVanMelder.TryGetValue(o.Naar, out var bb) || a == bb) continue;
            var sleutel = (Math.Min(o.Van, o.Naar), Math.Max(o.Van, o.Naar));
            if (!paren.TryGetValue(sleutel, out var vb))
                paren[sleutel] = vb = a < bb
                    ? new SchemaVerbinding { Van = a, Naar = bb, VanMelder = o.Van, NaarMelder = o.Naar }
                    : new SchemaVerbinding { Van = bb, Naar = a, VanMelder = o.Naar, NaarMelder = o.Van };
            if (a == vb.Van) vb.PijlNaarNaar = true; else vb.PijlNaarVan = true;
            string label = $"{o.Van}→{o.Naar} {o.Richting.Tekst()}";
            string wissels = string.Join(", ", WisselsBij(k, o.Van, o.Naar));
            if (wissels.Length > 0) label += $" via {wissels}";
            if (!vb.Labels.Contains(label)) vb.Labels.Add(label);
        }
        t.Verbindingen.AddRange(paren.Values.OrderBy(v => v.Van).ThenBy(v => v.Naar).ThenBy(v => v.VanMelder).ThenBy(v => v.NaarMelder));

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

        t.PlaatsMelders(blokken.ToDictionary(x => x.Nummer, x => x.Melders));

        t.PasAfmetingenAan();
        t.PlaatsLabels();
        return t;
    }

    /// <summary>BUG #90: zet elke bezetmelder die aan een ander blok grenst op de zijde van het vak waar de pijl naar dat blok vertrekt,
    /// in de volgorde van de aangrenzende melders; de kruiswissel (melder 24 met 3 of 4 buren) wordt een X met de benen in de hoeken.</summary>
    private void PlaatsMelders(Dictionary<int, List<int>> melderLijsten)
    {
        var vak = Vakken.ToDictionary(v => v.Nummer);
        // Kruiswissel: welk vak, en welke buurmelders
        var kruisBuren = new Dictionary<int, List<(int Melder, SchemaVak Buur)>>();
        foreach (var v in Vakken)
        {
            int midden = melderLijsten[v.Nummer].FirstOrDefault(m => _kruisMidden.Contains(m));
            if (midden == 0) continue;
            var buren = new List<(int, SchemaVak)>();
            foreach (var vb in Verbindingen)
            {
                if (vb.Van == v.Nummer && vb.VanMelder == midden) buren.Add((vb.NaarMelder, vak[vb.Naar]));
                else if (vb.Naar == v.Nummer && vb.NaarMelder == midden) buren.Add((vb.VanMelder, vak[vb.Van]));
            }
            buren = buren.GroupBy(x => x.Item1).Select(g => g.First()).ToList();
            if (buren.Count is >= 3 and <= 4) kruisBuren[v.Nummer] = buren;
        }
        foreach (var v in Vakken) v.B = Math.Max(v.B, v.Titel.Length * 8.6 + 24);
        var hoekChips = new Dictionary<(int Vak, int Melder), SchemaMelder>();
        foreach (var (nr, buren) in kruisBuren)
        {
            var v = vak[nr];
            v.Vorm = "kruis"; v.B = 120; v.H = 120;
            (int sx, int sy)[] hoeken = { (-1, -1), (1, -1), (1, 1), (-1, 1) };
            var toewijzing = BesteHoeken(v, buren, hoeken);
            // de naam komt aan de kant waar het minst een been naartoe loopt
            (string Plek, double Dx, double Dy)[] plekken = { ("onder", 0, 1), ("boven", 0, -1), ("rechts", 1, 0), ("links", -1, 0) };
            double Dichtbij((string Plek, double Dx, double Dy) pl) => buren.Max(bn =>
            {
                double ddx = bn.Buur.X - v.X, ddy = bn.Buur.Y - v.Y, l = Math.Sqrt(ddx * ddx + ddy * ddy);
                return l < 1e-9 ? 0 : (ddx * pl.Dx + ddy * pl.Dy) / l;
            });
            v.TitelPlek = plekken.OrderBy(Dichtbij).First().Plek;
            for (int i = 0; i < buren.Count; i++)
            {
                var (sx, sy) = hoeken[toewijzing[i]];
                var chip = new SchemaMelder { Melder = buren[i].Melder, X = v.X + sx * v.B / 2, Y = v.Y + sy * v.H / 2, UitX = sx / Math.Sqrt(2), UitY = sy / Math.Sqrt(2), Zijde = -1 };
                v.Chips.Add(chip);
                hoekChips[(nr, buren[i].Melder)] = chip;
            }
        }

        // Gewone vakken: chip per (vak, melder, zijde)
        int paarIndex = 0;
        var randChips = new Dictionary<(int Vak, int Melder, int Zijde), SchemaMelder>();
        var einden = new List<(SchemaVerbinding Vb, bool IsVan, SchemaMelder Chip)>();
        foreach (var vb in Verbindingen)
        {
            for (int kant = 0; kant < 2; kant++)
            {
                bool isVan = kant == 0;
                var eigen = vak[isVan ? vb.Van : vb.Naar];
                var buur = vak[isVan ? vb.Naar : vb.Van];
                int melder = isVan ? vb.VanMelder : vb.NaarMelder;
                int buurMelder = isVan ? vb.NaarMelder : vb.VanMelder;
                SchemaMelder chip;
                if (eigen.Vorm == "kruis" && hoekChips.TryGetValue((eigen.Nummer, buurMelder), out var hc) && _kruisMidden.Contains(melder)) chip = hc;
                else
                {
                    int zijde = Zijde(eigen, buur.X, buur.Y);
                    if (!randChips.TryGetValue((eigen.Nummer, melder, zijde), out chip!))
                    {
                        randChips[(eigen.Nummer, melder, zijde)] = chip = new SchemaMelder { Melder = melder, Zijde = zijde };
                        eigen.Chips.Add(chip);
                    }
                    chip.Buren.Add((zijde is 0 or 2 ? buur.X : buur.Y, paarIndex));
                }
                einden.Add((vb, isVan, chip));
            }
            paarIndex++;
        }
        // Per vak en zijde sorteren op de plaats van de buren, het vak zo nodig breder/hoger maken, en de vlakjes verdelen
        foreach (var v in Vakken)
        {
            foreach (var groep in v.Chips.Where(c => c.Zijde >= 0).GroupBy(c => c.Zijde))
            {
                var lijst = groep.OrderBy(c => Math.Round(c.Buren.Average(b => b.Coord))).ThenBy(c => c.Buren.Min(b => b.PaarIndex)).ToList();
                int n = lijst.Count;
                if (groep.Key is 0 or 2) v.B = Math.Max(v.B, n * (SchemaMelder.Breedte + 8) + 8);
                else v.H = Math.Max(v.H, n * (SchemaMelder.Hoogte + 6) + 8);
                for (int i = 0; i < n; i++)
                {
                    double f = (i + 1.0) / (n + 1);
                    var c = lijst[i];
                    switch (groep.Key)
                    {
                        case 0: c.X = v.X - v.B / 2 + v.B * f; c.Y = v.Y - v.H / 2; c.UitX = 0; c.UitY = -1; break;
                        case 2: c.X = v.X - v.B / 2 + v.B * f; c.Y = v.Y + v.H / 2; c.UitX = 0; c.UitY = 1; break;
                        case 1: c.X = v.X + v.B / 2; c.Y = v.Y - v.H / 2 + v.H * f; c.UitX = 1; c.UitY = 0; break;
                        default: c.X = v.X - v.B / 2; c.Y = v.Y - v.H / 2 + v.H * f; c.UitX = -1; c.UitY = 0; break;
                    }
                }
            }
        }
        foreach (var (vb, isVan, chip) in einden)
        {
            if (isVan) vb.Begin = chip.Anker; else vb.Eind = chip.Anker;
        }
    }

    /// <summary>Schuift de hele tekening zo dat de vakken (met melders en de naam van de kruiswissel) netjes binnen het beeld liggen
    /// en maakt het beeld niet groter dan nodig; de ruimte voor de labels komt erbij.</summary>
    private void PasAfmetingenAan()
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var v in Vakken)
        {
            var r = VakRechthoek(v);
            minX = Math.Min(minX, r.X); minY = Math.Min(minY, r.Y);
            maxX = Math.Max(maxX, r.X + r.B); maxY = Math.Max(maxY, r.Y + r.H);
            foreach (var c in v.Chips)
            {
                minX = Math.Min(minX, c.X - 20); minY = Math.Min(minY, c.Y - 14);
                maxX = Math.Max(maxX, c.X + 20); maxY = Math.Max(maxY, c.Y + 14);
            }
        }
        const double marge = 50;
        double dx = marge - minX, dy = marge - minY;
        foreach (var v in Vakken)
        {
            v.X += dx; v.Y += dy;
            foreach (var c in v.Chips) { c.X += dx; c.Y += dy; }
        }
        foreach (var vb in Verbindingen)
        {
            vb.Begin = (vb.Begin.X + dx, vb.Begin.Y + dy);
            vb.Eind = (vb.Eind.X + dx, vb.Eind.Y + dy);
        }
        Breedte = maxX - minX + 2 * marge;
        Hoogte = maxY - minY + 2 * marge;
    }

    /// <summary>Rechthoek die een vak (bij de kruiswissel inclusief de naam ernaast) in beslag neemt.</summary>
    private static (double X, double Y, double B, double H) VakRechthoek(SchemaVak v)
    {
        double x = v.X - v.B / 2, y = v.Y - v.H / 2, b = v.B, h = v.H;
        if (v.Vorm != "kruis") return (x - 16, y - 12, b + 32, h + 24);
        const double tb = 240, th = 60;
        return v.TitelPlek switch
        {
            "boven" => (v.X - tb / 2, y - 30 - th, tb, th + 30),
            "links" => (x - 30 - tb, v.Y - th / 2, tb + 30, th),
            "rechts" => (x + b, v.Y - th / 2, tb + 30 + 30, th),
            _ => (v.X - tb / 2, y + h, tb, th + 30),
        };
    }

    private static bool Raakt((double X, double Y, double B, double H) a, (double X, double Y, double B, double H) b) =>
        a.X < b.X + b.B && b.X < a.X + a.B && a.Y < b.Y + b.H && b.Y < a.Y + a.H;

    /// <summary>Zet elk label op de lijn, maar niet over een vak, een melder of een ander label heen: zo nodig verderop of opzij.</summary>
    private void PlaatsLabels()
    {
        var obstakels = new List<(double X, double Y, double B, double H)>();
        foreach (var v in Vakken)
        {
            var r = VakRechthoek(v);
            obstakels.Add(r);
            if (v.Vorm == "kruis") obstakels.Add((v.X - v.B / 2 - 20, v.Y - v.H / 2 - 20, v.B + 40, v.H + 40));
        }
        foreach (var vb in Verbindingen)
        {
            if (vb.Labels.Count == 0) { vb.LabelPlek = ((vb.Begin.X + vb.Eind.X) / 2, (vb.Begin.Y + vb.Eind.Y) / 2); continue; }
            double dx = vb.Eind.X - vb.Begin.X, dy = vb.Eind.Y - vb.Begin.Y, len = Math.Sqrt(dx * dx + dy * dy);
            double nx = len < 1e-9 ? 0 : -dy / len, ny = len < 1e-9 ? 1 : dx / len;
            double lb = vb.LabelBreedte, lh = vb.LabelHoogte;
            (double X, double Y)? gekozen = null; (double X, double Y) eerste = default; bool heeftEerste = false;
            foreach (double opzij in new[] { 0, lh / 2 + 14, -(lh / 2 + 14), lh + 30, -(lh + 30) })
                foreach (double f in new[] { 0.5, 0.35, 0.65, 0.22, 0.78 })
                {
                    double mx = vb.Begin.X + dx * f + nx * opzij, my = vb.Begin.Y + dy * f + ny * opzij;
                    mx = Math.Clamp(mx, lb / 2 + 4, Breedte - lb / 2 - 4);
                    my = Math.Clamp(my, lh / 2 + 4, Hoogte - lh / 2 - 4);
                    if (!heeftEerste) { eerste = (mx, my); heeftEerste = true; }
                    var rect = (mx - lb / 2, my - lh / 2, lb, lh);
                    if (obstakels.Any(o => Raakt(rect, o))) continue;
                    gekozen = (mx, my); goto klaar;
                }
            klaar:
            vb.LabelPlek = gekozen ?? eerste;
            obstakels.Add((vb.LabelPlek.X - lb / 2, vb.LabelPlek.Y - lh / 2, lb, lh));
        }
    }

    /// <summary>0 boven, 1 rechts, 2 onder, 3 links: aan welke zijde van het vak ligt het punt (x, y)?</summary>
    private static int Zijde(SchemaVak v, double x, double y)
    {
        double dx = x - v.X, dy = y - v.Y;
        if (Math.Abs(dx) / (v.B / 2) > Math.Abs(dy) / (v.H / 2)) return dx > 0 ? 1 : 3;
        return dy > 0 ? 2 : 0;
    }

    /// <summary>Wijst elke buurmelder van de kruiswissel een hoek toe, zo dat de hoek zo goed mogelijk in de richting van het
    /// aangrenzende blok wijst (de X draait dus mee met de indeling van de tekening).</summary>
    private static int[] BesteHoeken(SchemaVak v, List<(int Melder, SchemaVak Buur)> buren, (int sx, int sy)[] hoeken)
    {
        var beste = new int[buren.Count]; double besteScore = double.MaxValue;
        var huidig = new int[buren.Count]; var gebruikt = new bool[hoeken.Length];
        double Afstand(int i, int h)
        {
            double a1 = Math.Atan2(buren[i].Buur.Y - v.Y, buren[i].Buur.X - v.X), a2 = Math.Atan2(hoeken[h].sy, hoeken[h].sx);
            double d = Math.Abs(a1 - a2) % (2 * Math.PI);
            return d > Math.PI ? 2 * Math.PI - d : d;
        }
        void Zoek(int i, double score)
        {
            if (score >= besteScore) return;
            if (i == buren.Count) { besteScore = score; Array.Copy(huidig, beste, beste.Length); return; }
            for (int h = 0; h < hoeken.Length; h++)
            {
                if (gebruikt[h]) continue;
                gebruikt[h] = true; huidig[i] = h;
                Zoek(i + 1, score + Afstand(i, h));
                gebruikt[h] = false;
            }
        }
        Zoek(0, 0);
        return beste;
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
            if (v.Vorm == "kruis")
            {
                // BUG #90: de kruiswissel als X; de vier benen eindigen in de hoeken, waar hun bezetmelder staat
                sb.Append($"<rect x=\"{F(x)}\" y=\"{F(y)}\" width=\"{F(v.B)}\" height=\"{F(v.H)}\" rx=\"8\" fill=\"{vul}\" fill-opacity=\"0.5\" stroke=\"{rand}\" stroke-width=\"1.5\" stroke-dasharray=\"3 3\"/>");
                sb.Append($"<line x1=\"{F(x)}\" y1=\"{F(y)}\" x2=\"{F(x + v.B)}\" y2=\"{F(y + v.H)}\" stroke=\"{rand}\" stroke-width=\"4\" stroke-linecap=\"round\"/>");
                sb.Append($"<line x1=\"{F(x + v.B)}\" y1=\"{F(y)}\" x2=\"{F(x)}\" y2=\"{F(y + v.H)}\" stroke=\"{rand}\" stroke-width=\"4\" stroke-linecap=\"round\"/>");
                sb.Append($"<circle cx=\"{F(v.X)}\" cy=\"{F(v.Y)}\" r=\"11\" fill=\"#fff\" stroke=\"{rand}\" stroke-width=\"2\"/>");
                sb.Append($"<text x=\"{F(v.X)}\" y=\"{F(v.Y + 4)}\" font-size=\"11\" font-weight=\"700\" text-anchor=\"middle\" fill=\"#1d2330\">24</text>");
                var (tx, ty, anker) = v.TitelPlek switch
                {
                    "boven" => (v.X, y - 52, "middle"),
                    "links" => (x - 84, v.Y - 8, "end"),
                    "rechts" => (x + v.B + 84, v.Y - 8, "start"),
                    _ => (v.X, y + v.H + 28, "middle"),
                };
                sb.Append($"<text x=\"{F(tx)}\" y=\"{F(ty)}\" font-size=\"14\" font-weight=\"600\" text-anchor=\"{anker}\" fill=\"#1d2330\">{E(v.Titel)}</text>");
                sb.Append($"<text x=\"{F(tx)}\" y=\"{F(ty + 18)}\" font-size=\"12\" text-anchor=\"{anker}\" fill=\"#333\">melders {E(v.Melders)} (kruiswissel)</text>");
                if (v.Markeringen.Count > 0)
                    sb.Append($"<text x=\"{F(tx)}\" y=\"{F(ty + 34)}\" font-size=\"10.5\" text-anchor=\"{anker}\" fill=\"#a12a2a\">{E(v.Markeringen[0])}{(v.Markeringen.Count > 1 ? $" (+{v.Markeringen.Count - 1})" : "")}</text>");
                continue;
            }
            sb.Append($"<rect x=\"{F(x)}\" y=\"{F(y)}\" width=\"{F(v.B)}\" height=\"{F(v.H)}\" rx=\"8\" fill=\"{vul}\" stroke=\"{rand}\" stroke-width=\"2\"{streep}/>");
            sb.Append($"<text x=\"{F(v.X)}\" y=\"{F(y + 27)}\" font-size=\"14\" font-weight=\"600\" text-anchor=\"middle\" fill=\"#1d2330\">{E(v.Titel)}</text>");
            sb.Append($"<text x=\"{F(v.X)}\" y=\"{F(y + 46)}\" font-size=\"12\" text-anchor=\"middle\" fill=\"#333\">melders {E(v.Melders)}</text>");
            if (v.Markeringen.Count > 0)
                sb.Append($"<text x=\"{F(v.X)}\" y=\"{F(y + 63)}\" font-size=\"10.5\" text-anchor=\"middle\" fill=\"#a12a2a\">{E(v.Markeringen[0])}{(v.Markeringen.Count > 1 ? $" (+{v.Markeringen.Count - 1})" : "")}</text>");
        }
        foreach (var v in Vakken)
            foreach (var c in v.Chips)
            {
                string rand = v.Soort == "splitsing" ? "#c27a1a" : v.Soort == "onbekend" ? "#999" : "#1f6f5c";
                sb.Append($"<rect x=\"{F(c.X - SchemaMelder.Breedte / 2)}\" y=\"{F(c.Y - SchemaMelder.Hoogte / 2)}\" width=\"{F(SchemaMelder.Breedte)}\" height=\"{F(SchemaMelder.Hoogte)}\" rx=\"4\" fill=\"#fff\" stroke=\"{rand}\" stroke-width=\"1.5\"/>");
                sb.Append($"<text x=\"{F(c.X)}\" y=\"{F(c.Y + 4)}\" font-size=\"11\" font-weight=\"600\" text-anchor=\"middle\" fill=\"#1d2330\">{c.Melder}</text>");
            }
        sb.Append("</svg>");
        return sb.ToString();
    }
}
