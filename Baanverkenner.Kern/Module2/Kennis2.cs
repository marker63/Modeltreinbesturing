using System.Text.Json.Serialization;

namespace Baanverkenner.Kern;

// =====================================================================================================================
//  BAANVERKENNER MODULE 2 - wat de verkenner weet
//
//  WAT: alle kennis van module 2 over de baan, in één bestand dat ook in de voortgang (VerkenStatus.Module2) bewaard wordt.
//  WAAROM: module 1 hield zijn kennis op veel plekken tegelijk bij (overgangen, doorgangen, kortsluitpunten, oplossingen, eisen),
//  waardoor die elkaar konden tegenspreken. Module 2 heeft één bron van waarheid: de gereden STAPPEN. De baankaart (rapport,
//  melderschema, blokkenschema, wisselstraten) wordt daar telkens opnieuw uit opgebouwd (zie BaanVerkenner2.Kaart.cs).
//
//  Begrippen:
//   - Stap: de loc kwam vanaf melder Via melder Melder binnen, reed in Richting verder en kwam bij Uitkomst
//     (een melder, of 0 = doodlopend, of -1 = kortsluiting), met de wissels in Stand (Dinamo-commando's die afbuigend stonden).
//   - Ingang: (Via, Melder, Richting). Bij een gewone melder hangt de uitkomst niet van Via af; bij een Engelse wissel in één
//     melder (zoals melder 24) wel. Daarom wordt Via altijd bewaard.
//   - Knoop: het stuk spoor tussen twee melders waar de wissels liggen. Twee melder-einden horen bij dezelfde knoop als de loc
//     van het ene naar het andere is gereden. Een wissel die bij een knoop hoort, telt mee bij elke stap door die knoop, in
//     beide richtingen (een wissel van achteren bereden in de verkeerde stand geeft kortsluiting).
//   - Relevant: de wissels waarvan bewezen is dat ze de uitkomst van een ingang veranderen (gevonden door één adres om te zetten
//     en verder alles gelijk te houden).
// =====================================================================================================================

/// <summary>Uitkomst van een stap: een meldernummer (&gt; 0), of een van deze twee bijzondere waarden.</summary>
public static class StapUitkomst
{
    /// <summary>Er kwam geen nieuwe melder: einde spoor (stootjuk) of de loc kon niet verder.</summary>
    public const int Doodlopend = 0;
    /// <summary>Dinamo meldde kortsluiting.</summary>
    public const int Kortsluiting = -1;
    public static string Tekst(int u) => u > 0 ? $"melder {u}" : u == Doodlopend ? "doodlopend" : "kortsluiting";
}

/// <summary>Eén gereden (of afgeleide) stap. Zie de uitleg bovenaan dit bestand.</summary>
public class StapWaarneming
{
    /// <summary>Melder waar de loc vandaan kwam (0 = onbekend, bijv. bij de start).</summary>
    public int Via { get; set; }
    public int Melder { get; set; }
    public Richting Richting { get; set; }
    /// <summary>Alle wisseladressen die afbuigend stonden (Dinamo-commando), gesorteerd.</summary>
    public List<int> Stand { get; set; } = new();
    /// <summary>Melder (&gt; 0), 0 = doodlopend, -1 = kortsluiting.</summary>
    public int Uitkomst { get; set; }
    public int Aantal { get; set; }
    /// <summary>Niet zelf gereden maar afgeleid: de omgekeerde van een gereden doorgang (dezelfde wisselstanden, andere richting).</summary>
    public bool Afgeleid { get; set; }
    /// <summary>De loc keerde in <see cref="Melder"/> om en reed terug naar waar hij vandaan kwam (dan is Via niet bekend).</summary>
    public bool Terug { get; set; }
    public DateTime Laatst { get; set; }
    /// <summary>Gemeten rijtijd (s) van het binnenkomen van Melder tot het binnenkomen van Uitkomst, omgerekend naar verkensnelheid (0 = niet gemeten).</summary>
    public double Seconden { get; set; }
    public int Metingen { get; set; }

    [JsonIgnore] public string IngangSleutel => Kennis2.Sleutel(Via, Melder, Richting);
    public override string ToString() => $"{(Via > 0 ? Via + " -> " : "")}{Melder} ({Richting.Tekst()}) -> {StapUitkomst.Tekst(Uitkomst)} [{(Stand.Count == 0 ? "alle rechtdoor" : "afbuigend " + string.Join(",", Stand))}]";
}

/// <summary>Een gevonden wissel: bij ingang (Via, Melder, Richting) gaf adres <see cref="Adres"/> rechtdoor <see cref="Rechtdoor"/> en afbuigend
/// <see cref="Afbuigend"/> (melder, 0 = doodlopend, -1 = kortsluiting, null = nog niet los gereden), met de andere relevante wissels in <see cref="Stand"/>.</summary>
public class WisselVondst2
{
    public int Adres { get; set; }
    public int Via { get; set; }
    public int Melder { get; set; }
    public Richting Richting { get; set; }
    public int? Rechtdoor { get; set; }
    public int? Afbuigend { get; set; }
    public List<int> Stand { get; set; } = new();
    public DateTime Tijd { get; set; }
}

/// <summary>Alles wat module 2 weet; wordt in de voortgang bewaard zodat stoppen en hervatten niets kost.</summary>
public class Kennis2
{
    public List<StapWaarneming> Stappen { get; set; } = new();
    /// <summary>Per ingang (sleutel <see cref="Sleutel"/>) de wisseladressen die daar bewezen invloed hebben.</summary>
    public Dictionary<string, List<int>> Relevant { get; set; } = new();
    public List<WisselVondst2> Vondsten { get; set; } = new();
    /// <summary>Per adres de overgangen ("a-b", kleinste melder eerst) waarover het adres al omgezet is zonder dat er iets veranderde.</summary>
    public Dictionary<int, List<string>> Getest { get; set; } = new();
    /// <summary>Dinamo-blok per melder.</summary>
    public Dictionary<int, int> Blokken { get; set; } = new();
    /// <summary>Hoe lang (s, op kruipsnelheid) de loc alléén op deze melder staat als hij er doorheen rijdt; voor netjes in het midden parkeren.</summary>
    public Dictionary<int, double> SoloTijd { get; set; } = new();
    /// <summary>Laatst gestuurde (en teruggemelde) Dinamo-stand per adres (true = afbuigend).</summary>
    public Dictionary<int, bool> Standen { get; set; } = new();
    /// <summary>Waar de loc staat: melder, waar hij vandaan kwam (0 = onbekend) en in welke kaartrichting hij reed.</summary>
    public int LocMelder { get; set; }
    public int LocVia { get; set; }
    public Richting LocRichting { get; set; }
    /// <summary>De loc is door een keerlus gereden: decoder 'vooruit' is nu kaartrichting 'achteruit'.</summary>
    public bool Omgedraaid { get; set; }
    /// <summary>Taken die na herhaalde pogingen zijn overgeslagen (sleutel), zodat de verkenning niet blijft hangen.</summary>
    public List<string> Overgeslagen { get; set; } = new();
    public Dictionary<string, int> Mislukt { get; set; } = new();
    public int Ritten { get; set; }
    public int Kortsluitingen { get; set; }
    public int Experimenten { get; set; }
    public DateTime Gestart { get; set; }

    public static string Sleutel(int via, int melder, Richting r) => $"{via}>{melder}:{(r == Richting.Vooruit ? "v" : "a")}";
    public static string OvergangSleutel(int a, int b) => a < b ? $"{a}-{b}" : $"{b}-{a}";

    // ---------------------------------------------------------------- vastleggen

    /// <summary>WAT: legt een gereden stap vast (of telt hem op als hij al bekend is). WAAROM: elke rit is bewijs; een stap die vaker
    /// gezien is, is betrouwbaarder. Bij een doorgang (Via &gt; 0, uitkomst een melder) wordt ook de omgekeerde stap afgeleid: met
    /// dezelfde wisselstanden kan de loc dezelfde weg terug (de wissels waar hij overheen reed liggen dan goed).</summary>
    public StapWaarneming Leg(int via, int melder, Richting r, IEnumerable<int> stand, int uitkomst, DateTime nu, bool terug = false, double seconden = 0)
    {
        var st = stand.Distinct().OrderBy(a => a).ToList();
        var w = Stappen.FirstOrDefault(s => s.Via == via && s.Melder == melder && s.Richting == r && s.Terug == terug && !s.Afgeleid
                                            && s.Uitkomst == uitkomst && s.Stand.SequenceEqual(st));
        if (w is null)
        {
            w = new StapWaarneming { Via = via, Melder = melder, Richting = r, Stand = st, Uitkomst = uitkomst, Terug = terug };
            Stappen.Add(w);
        }
        w.Aantal++;
        w.Laatst = nu;
        if (seconden > 0) { w.Seconden = (w.Seconden * w.Metingen + seconden) / (w.Metingen + 1); w.Metingen++; }
        if (via > 0 && uitkomst > 0 && !terug)
        {
            // de omgekeerde doorgang: van Uitkomst via Melder naar Via, andere richting
            var o = Stappen.FirstOrDefault(s => s.Afgeleid && s.Via == uitkomst && s.Melder == melder && s.Richting == r.Om() && s.Uitkomst == via && s.Stand.SequenceEqual(st));
            if (o is null) Stappen.Add(o = new StapWaarneming { Via = uitkomst, Melder = melder, Richting = r.Om(), Stand = st, Uitkomst = via, Afgeleid = true });
            o.Aantal++;
            o.Laatst = nu;
        }
        return w;
    }

    /// <summary>WAT: adres <paramref name="adres"/> heeft bewezen invloed bij ingang (via, melder, r). Geeft true als dat nieuw is.</summary>
    public bool MaakRelevant(int via, int melder, Richting r, int adres)
    {
        var k = Sleutel(via, melder, r);
        if (!Relevant.TryGetValue(k, out var l)) Relevant[k] = l = new List<int>();
        if (l.Contains(adres)) return false;
        l.Add(adres); l.Sort();
        _knopen = null;
        return true;
    }

    /// <summary>WAT: adres is omgezet terwijl de loc van melder a naar melder b reed, zonder verschil. Per RIJRICHTING: een motor van een Engelse wissel
    /// doet alleen iets als de loc van de ene kant komt (21 -> 24 wel, 24 -> 21 niet). WAAROM: zo weet de verkenner welke stukken baan nog niet met dit adres zijn getest.</summary>
    public void MarkeerGetest(int adres, int a, int b)
    {
        if (!Getest.TryGetValue(adres, out var l)) Getest[adres] = l = new List<string>();
        var s = $"{a}>{b}";
        if (!l.Contains(s)) l.Add(s);
    }
    public bool IsGetest(int adres, int a, int b) => Getest.TryGetValue(adres, out var l) && l.Contains($"{a}>{b}");

    /// <summary>Alle gereden overgangen in beide rijrichtingen (van, naar).</summary>
    [JsonIgnore] public IEnumerable<(int Van, int Naar)> GerichteOvergangen => Overgangen.SelectMany(p => new[] { (p.A, p.B), (p.B, p.A) });

    /// <summary>De buren van melder m aan de kant waar de loc in richting r uitrijdt.</summary>
    public HashSet<int> KantBuren(int m, Richting r)
        => Stappen.Where(s => s.Melder == m && s.Richting == r && s.Uitkomst > 0).Select(s => s.Uitkomst)
                  .Concat(Stappen.Where(s => s.Uitkomst == m && s.Richting == r.Om()).Select(s => s.Melder)).ToHashSet();

    // ---------------------------------------------------------------- knopen (wisselgebieden)

    [JsonIgnore] private Dictionary<string, string>? _knopen;
    [JsonIgnore] private int _knopenVoor = -1;

    /// <summary>Een melder-einde: het eind van <paramref name="m"/> waar de loc uitrijdt als hij in richting <paramref name="r"/> rijdt.</summary>
    public static string Einde(int m, Richting r) => $"{m}{(r == Richting.Vooruit ? "v" : "a")}";

    /// <summary>WAT: deelt alle melder-einden in knopen in (union-find over de gereden stappen). WAAROM: een wissel ligt in één knoop
    /// en telt daar in alle richtingen mee; zo wordt een wissel die vanaf de puntzijde gevonden is, ook van achteren gerespecteerd.</summary>
    private Dictionary<string, string> Knopen()
    {
        if (_knopen is not null && _knopenVoor == Stappen.Count) return _knopen;
        var ouder = new Dictionary<string, string>();
        string Vind(string x) { if (!ouder.TryGetValue(x, out var p)) { ouder[x] = x; return x; } if (p == x) return x; var w = Vind(p); ouder[x] = w; return w; }
        foreach (var s in Stappen)
        {
            var e = Einde(s.Melder, s.Richting);
            Vind(e);
            if (s.Uitkomst > 0)
            {
                // binnenkomen in Uitkomst in richting r = door het einde van Uitkomst aan de kant r.Om()
                var f = Einde(s.Uitkomst, s.Richting.Om());
                var a = Vind(e); var b = Vind(f);
                if (a != b) ouder[a] = b;
            }
        }
        var res = new Dictionary<string, string>();
        foreach (var k in ouder.Keys.ToList()) res[k] = Vind(k);
        _knopen = res; _knopenVoor = Stappen.Count;
        return res;
    }

    public string Knoop(int m, Richting r)
    {
        var e = Einde(m, r);
        return Knopen().TryGetValue(e, out var k) ? k : e;
    }

    /// <summary>De melders die aan knoop <paramref name="knoop"/> liggen.</summary>
    public HashSet<int> MeldersVanKnoop(string knoop)
        => Knopen().Where(kv => kv.Value == knoop).Select(kv => int.Parse(kv.Key[..^1])).ToHashSet();

    /// <summary>WAT: de wissels die bewezen invloed hebben in de knoop waar de loc uit melder m in richting r doorheen rijdt (alle ingangen van die knoop samen).</summary>
    /// <remarks>Niet de hele knoop maar het einde zelf en de einden die er direct mee verbonden zijn (de melders waar de loc vanaf dit einde uitkwam,
    /// aan hun binnenkomstkant). Bij een wisselgebied met een Engelse wissel (14 - 24 - 129) ligt anders melder 129 met beide kanten in één knoop, en
    /// eist de route 14 -> 129 -> 4 tegenstrijdige standen van wissel 9 terwijl de loc op 129 staat (simulatie 28-10).</remarks>
    public HashSet<int> RelevantInKnoop(int m, Richting r)
    {
        var einden = new HashSet<string> { Einde(m, r) };
        foreach (var s in Stappen) if (s.Melder == m && s.Richting == r && s.Uitkomst > 0) einden.Add(Einde(s.Uitkomst, s.Richting.Om()));
        foreach (var s in Stappen) if (s.Uitkomst == m && s.Richting == r.Om()) einden.Add(Einde(s.Melder, s.Richting));
        var res = new HashSet<int>();
        foreach (var (sleutel, adressen) in Relevant)
        {
            var (_, melder, richting) = Ontleed(sleutel);
            if (einden.Contains(Einde(melder, richting))) res.UnionWith(adressen);
        }
        return res;
    }

    /// <summary>WAT: de melders waar adres <paramref name="adres"/> (of een wissel die ermee meeschakelt) kan liggen: alle melders van de
    /// knopen waar het relevant is. WAAROM: zo'n wissel wordt nooit omgezet terwijl de loc op een van die melders staat (ontsporing).</summary>
    public HashSet<int> Zone(int adres)
    {
        var res = new HashSet<int>();
        if (ExtraZone.TryGetValue(adres, out var extra)) res.UnionWith(extra);   // BUG #395
        foreach (var (sleutel, adressen) in Relevant)
        {
            if (!adressen.Contains(adres)) continue;
            var (_, melder, richting) = Ontleed(sleutel);
            // de melder zelf, waar de loc vanuit dat einde uitkwam, en wat vanaf de andere kant door hetzelfde einde binnenkwam (en die kant weer op kan)
            res.Add(melder);
            foreach (var s in Stappen.Where(s => s.Melder == melder && s.Richting == richting && s.Uitkomst > 0))
            {
                res.Add(s.Uitkomst);
                foreach (var t in Stappen.Where(t => t.Melder == s.Uitkomst && t.Richting == richting.Om() && t.Uitkomst > 0)) res.Add(t.Uitkomst);
            }
            foreach (var s in Stappen.Where(s => s.Uitkomst == melder && s.Richting == richting.Om())) res.Add(s.Melder);
        }
        return res;
    }

    public bool IsGevonden(int adres) => Relevant.Values.Any(l => l.Contains(adres));

    public static (int Via, int Melder, Richting R) Ontleed(string sleutel)
    {
        var p1 = sleutel.Split('>'); var p2 = p1[1].Split(':');
        return (int.Parse(p1[0]), int.Parse(p2[0]), p2[1] == "v" ? Richting.Vooruit : Richting.Achteruit);
    }

    // ---------------------------------------------------------------- voorspellen

    /// <summary>Kandidaten voor ingang (via, m, r): eerst de stappen met precies deze ingang; zijn die er niet, dan (als de melder niet
    /// van de ingang afhangt) de stappen vanaf een andere ingang aan dezelfde kant. <paramref name="terugval"/> = de tweede soort is gebruikt.</summary>
    private List<StapWaarneming> Kandidaten(int via, int m, Richting r, out bool terugval)
    {
        terugval = false;
        var eigen = Stappen.Where(s => s.Melder == m && s.Richting == r && s.Via == via && !s.Terug && via > 0).ToList();
        if (eigen.Count > 0) return eigen;
        terugval = true;
        if (ViaAfhankelijk(m, r)) return new List<StapWaarneming>();
        return Stappen.Where(s => s.Melder == m && s.Richting == r).ToList();
    }

    /// <summary>WAT: hangt de uitkomst vanuit melder m (richting r) af van de kant waar de loc binnenkwam? WAAROM: zo ja (Engelse wissel in
    /// één melder), dan mag een stap vanaf de ene ingang niet als voorspelling voor een andere ingang gebruikt worden.</summary>
    public bool ViaAfhankelijk(int m, Richting r)
    {
        var rel = RelevantInKnoop(m, r);
        var l = Stappen.Where(s => s.Melder == m && s.Richting == r && s.Via > 0 && !s.Terug).ToList();
        foreach (var a in l)
            foreach (var b in l)
                if (a.Via != b.Via && a.Uitkomst != b.Uitkomst && rel.All(x => a.Stand.Contains(x) == b.Stand.Contains(x))) return true;
        return false;
    }

    /// <summary>WAT: wat gebeurt er als de loc bij ingang (via, m, r) verder rijdt met de wissels in <paramref name="stand"/>? Alleen een antwoord als
    /// het eerder precies zo gereden is voor alle relevante wissels van de knoop (anders null = onbekend). WAAROM: module 2 rijdt bij het
    /// navigeren alleen wat al eens gelukt is; iets nieuws is altijd een bewuste proef.</summary>
    public (int Uitkomst, StapWaarneming Getuige, bool Terugval)? Voorspel(int via, int m, Richting r, Func<int, bool> stand)
    {
        var rel = RelevantInKnoop(m, r);
        var kand = Kandidaten(via, m, r, out bool terugval);
        var passend = kand.Where(s => rel.All(a => s.Stand.Contains(a) == stand(a))).ToList();
        var uit = passend.Select(s => s.Uitkomst).Distinct().ToList();
        if (uit.Count != 1) return null;
        var getuige = passend.OrderBy(s => s.Afgeleid).ThenBy(s => s.Terug).ThenByDescending(s => s.Aantal).First();
        return (uit[0], getuige, terugval);
    }

    /// <summary>WAT: alle uitkomsten die vanaf ingang (via, m, r) bekend zijn, elk met de wisselstand (alleen de relevante wissels) die erbij hoort.
    /// WAAROM: de routeplanner kiest daaruit de stap die hij nodig heeft.</summary>
    public List<(int Uitkomst, Dictionary<int, bool> Eis, bool Terugval, int Aantal)> Mogelijkheden(int via, int m, Richting r)
    {
        var rel = RelevantInKnoop(m, r);
        var kand = Kandidaten(via, m, r, out bool terugval);
        var res = new List<(int, Dictionary<int, bool>, bool, int)>();
        foreach (var g in kand.GroupBy(s => string.Join(",", rel.OrderBy(a => a).Select(a => s.Stand.Contains(a) ? "A" : "R"))))
        {
            var uit = g.Select(s => s.Uitkomst).Distinct().ToList();
            if (uit.Count != 1)
            {
                // tegenstrijdig: er is nog een onbekende wissel van invloed. WAT: toch een weg aanbieden, met de hele stand van een stap die wél doorreed (alle adressen
                // waarin hij verschilt van de stappen met een andere uitkomst horen bij de eis). WAAROM (BUG #387, Marco 08-11 11:26: "ik heb al vele malen de verkenning moeten
                // uitbreiden terwijl je gewoon zelf verder had kunnen gaan"; log 10:42-10:44): 14 -> 24 reed met de basisstand naar 21, maar met een stand waarin 4, 7, 8, 10, 11,
                // 13 en 14 om stonden gaf het kortsluiting. Die twee vielen in dezelfde groep, dus kende de planning geen enkele weg voorbij 24 meer; de loc pendelde tussen 13, 5 en 14
                // en de verkenning stopte als "niet af".
                foreach (var goed in g.Where(x => x.Uitkomst > 0).OrderByDescending(x => x.Aantal))
                {
                    var anders = g.Where(x => x.Uitkomst != goed.Uitkomst).ToList();
                    var verschil = anders.SelectMany(x => x.Stand.Union(goed.Stand).Except(x.Stand.Intersect(goed.Stand))).Distinct().ToList();
                    var eis = rel.Union(verschil).Distinct().ToDictionary(a => a, a => goed.Stand.Contains(a));
                    if (verschil.Count == 0 || anders.Any(x => eis.All(e => x.Stand.Contains(e.Key) == e.Value))) continue;
                    if (res.Any(x => x.Item1 == goed.Uitkomst)) continue;
                    res.Add((goed.Uitkomst, eis, true, goed.Aantal));
                }
                continue;
            }
            var s0 = g.First();
            res.Add((uit[0], rel.ToDictionary(a => a, a => s0.Stand.Contains(a)), terugval, g.Sum(x => x.Aantal)));
        }
        return res;
    }

    /// <summary>WAT: zijn er bij ingang (via, m, r) stappen met dezelfde stand van de relevante wissels maar een andere uitkomst? Dan geeft dit
    /// de twee standen terug. WAAROM: dan heeft een nog onbekend adres invloed (bijvoorbeeld wissel 14 achter de Engelse wissel), en moet uitgezocht worden welk.</summary>
    public (StapWaarneming A, StapWaarneming B)? Tegenstrijdig(int via, int m, Richting r)
    {
        var rel = RelevantInKnoop(m, r);
        var l = Stappen.Where(s => s.Melder == m && s.Richting == r && s.Via == via && !s.Terug).ToList();
        foreach (var a in l)
            foreach (var b in l)
                if (a.Uitkomst != b.Uitkomst && (!a.Afgeleid || !b.Afgeleid) && a.Aantal >= b.Aantal && rel.All(x => a.Stand.Contains(x) == b.Stand.Contains(x))) return (a, b);
        return null;
    }

    /// <summary>Alle melders die ooit gezien zijn.</summary>
    /// <summary>BUG #382: melders die als spook herkend zijn (geen echte sectie); bewaard zodat een hervatting ze ook negeert.</summary>
    public List<int> Spookmelders { get; set; } = new();
    /// <summary>BUG #393: melders waar de loc stil en als enige bezette melder op stond: die bestaan echt en zijn nooit een spookmelder.</summary>
    public HashSet<int> Echt { get; set; } = new();
    /// <summary>BUG #395: per adres de melders waarop de loc stond toen het omzetten van dat adres een kortsluiting ophief: daar ligt de wissel (onder de loc).
    /// Hoort bij de zone van die wissel, zodat hij daar nooit wordt omgezet en de planning er rekening mee houdt.</summary>
    public Dictionary<int, HashSet<int>> ExtraZone { get; set; } = new();
    /// <summary>BUG #397: "a,b,c|van>naar": van deze adressen hoogstens één omzetten in een dekkingsrit die over dit stuk komt (samen gaven ze daar kortsluiting).</summary>
    public HashSet<string> NietSamen { get; set; } = new();
    /// <summary>BUG #410: per melder de rijstroom-truc die de loc daar weer liet rijden ("blok,richting" of "blok,richting,tweede blok"),
    /// geleerd tijdens het zoeken per blok (#408). Wordt de volgende keer als eerste geprobeerd.</summary>
    public Dictionary<int, string> BlokTruc { get; set; } = new();

    /// <summary>WAT: zoekt een melder X die tussen a en b ligt (a -> X -> b), terwijl de loc met precies dezelfde wisselstand ook rechtstreeks van a naar b reed.
    /// Een echte melder kan de loc niet overslaan; dan is X een spook (Dinamo-ingang die zonder sectie bezet raakt).
    /// WAAROM (BUG #382, Marco 05-11 10:12/10:52): melder 268 bestaat niet, maar Dinamo meldde hem tussen 25 en 17. Module 2 legde hem als melder vast; de taak "blok van 268
    /// bepalen" kon nooit lukken, en de verkenning stopte steeds met "nog NIET af".</summary>
    public int? ZoekSpook()
    {
        foreach (var x in Stappen.Where(s => !s.Afgeleid && s.Uitkomst > 0 && s.Via > 0))
        {
            int a = x.Via, m = x.Melder, b = x.Uitkomst;
            if (Spookmelders.Contains(m)) continue;
            // BUG #391 (Marco 09-11, ronde 13:58-14:57): melder 129 (de kruiswissel) werd als spookmelder weggegooid na één verkeerd vastgelegde stap (10 -> 24 bij een rit
            // die op de grens van 129 en 10 begon). Een melder waar de loc minstens een seconde alleen op stond, bestaat echt (268 was maar 0,2 s bezet).
            if (SoloTijd.TryGetValue(m, out var solo) && solo >= 1.0) continue;
            // BUG #393 (Marco 09-11 17:23, verkenning 15:56-17:13 met versie 391): ook melder 24 en 14 (bij de kruiswissel) werden weggegooid; de loc reed er zo snel overheen
            // dat de tijd alleen op de melder korter was dan 1 s. Een melder met een gevonden Dinamo-blok, of waar de loc stil en alleen op stond, bestaat altijd echt.
            if (Blokken.ContainsKey(m) || Echt.Contains(m)) continue;
            var naarM = Stappen.Where(s => !s.Afgeleid && s.Melder == a && s.Richting == x.Richting && s.Uitkomst == m).ToList();
            var naarB = Stappen.Where(s => !s.Afgeleid && s.Melder == a && s.Richting == x.Richting && s.Uitkomst == b).ToList();
            if (naarM.Any(s1 => naarB.Any(s2 => s1.Stand.SequenceEqual(s2.Stand)))) return m;
        }
        return null;
    }

    /// <summary>BUG #382: haalt een spookmelder uit alle waarnemingen; a -> X -> b wordt a -> b.</summary>
    public void VerwijderSpook(int m)
    {
        if (!Spookmelders.Contains(m)) Spookmelders.Add(m);
        foreach (var s in Stappen.Where(s => s.Uitkomst == m).ToList())
        {
            var verder = Stappen.FirstOrDefault(t => t.Melder == m && t.Via == s.Melder && t.Richting == s.Richting && t.Uitkomst > 0 && t.Uitkomst != m);
            if (verder is not null && !Stappen.Any(t => t != s && t.Melder == s.Melder && t.Via == s.Via && t.Richting == s.Richting && t.Uitkomst == verder.Uitkomst && t.Stand.SequenceEqual(s.Stand)))
                s.Uitkomst = verder.Uitkomst;
            else Stappen.Remove(s);
        }
        Stappen.RemoveAll(s => s.Melder == m);
        foreach (var s in Stappen.Where(s => s.Via == m)) s.Via = 0;
        SoloTijd.Remove(m);
        // BUG #391: een wisselvondst die op de spookmelder steunde, opnieuw bekijken (log 14:13: "adres 14 na melder 29: rechtdoor -> 30, afbuigend -> 268"; zonder 268
        // gaan beide standen naar 30, dus is adres 14 daar helemaal geen wissel). Geeft de vondst daarna in beide standen hetzelfde, dan vervalt hij.
        foreach (var v in Vondsten.ToList())
        {
            var sleutel = Sleutel(v.Via, v.Melder, v.Richting);
            bool weg = v.Melder == m || v.Via == m;
            if (!weg && (v.Rechtdoor == m || v.Afbuigend == m))
            {
                var st = Stappen.Where(x => x.Melder == v.Melder && x.Richting == v.Richting && (x.Via == v.Via || x.Via == 0) && x.Uitkomst != m).ToList();
                int? Uit(bool afb) { var u = st.Where(x => x.Stand.Contains(v.Adres) == afb).Select(x => x.Uitkomst).Distinct().ToList(); return u.Count == 1 ? u[0] : null; }
                if (v.Rechtdoor == m) v.Rechtdoor = Uit(false);
                if (v.Afbuigend == m) v.Afbuigend = Uit(true);
                weg = v.Rechtdoor is null && v.Afbuigend is null || v.Rechtdoor is int x1 && v.Afbuigend == x1;
            }
            if (!weg) continue;
            Vondsten.Remove(v);
            if (Relevant.TryGetValue(sleutel, out var l)) { l.Remove(v.Adres); if (l.Count == 0) Relevant.Remove(sleutel); }
        }
        foreach (var k in Relevant.Keys.ToList()) { var (via, mm, _) = Ontleed(k); if (via == m || mm == m) Relevant.Remove(k); }
    }

    [JsonIgnore] public IEnumerable<int> Melders => Stappen.Select(s => s.Melder).Concat(Stappen.Where(s => s.Uitkomst > 0).Select(s => s.Uitkomst)).Concat(Stappen.Where(s => s.Via > 0).Select(s => s.Via)).Distinct();

    /// <summary>Alle overgangen (melderparen) die ooit gereden zijn.</summary>
    [JsonIgnore] public IEnumerable<(int A, int B)> Overgangen => Stappen.Where(s => s.Uitkomst > 0).Select(s => s.Melder < s.Uitkomst ? (s.Melder, s.Uitkomst) : (s.Uitkomst, s.Melder)).Distinct();
}
