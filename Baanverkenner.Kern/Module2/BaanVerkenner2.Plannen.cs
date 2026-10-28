namespace Baanverkenner.Kern;

// =====================================================================================================================
//  MODULE 2 - routes plannen, proeven en het testen van nog niet gevonden adressen
//
//  De planner werkt alleen met wat al eens gereden is (Kennis2.Mogelijkheden): elke stap van een route heeft een "eis" (de stand
//  van de wissels in die knoop zoals het toen goed ging). Een route wordt opgedeeld in stukken (benen): tussen twee benen staat de loc
//  stil op een melder waar het veilig is om de wissels voor het volgende been om te zetten (niet in het wisselgebied van die wissels).
// =====================================================================================================================

public partial class BaanVerkenner2
{
    /// <summary>Waar de loc is: melder M, binnengekomen vanaf Via (0 = onbekend), rijrichting H (kaartrichting).</summary>
    internal readonly record struct Toestand(int M, int Via, Richting H);

    /// <summary>Eén stap van een route. <see cref="Omkeer"/> = de loc keert om en rijdt terug naar waar hij vandaan kwam.</summary>
    internal sealed class PlanStap
    {
        public int Van, Naar;
        public Richting H;
        public Dictionary<int, bool> Eis = new();
        public bool Omkeer;
        public override string ToString() => $"{Van}->{Naar} ({H.Tekst()}{(Omkeer ? ", omkeren" : "")}{(Eis.Count > 0 ? ", " + string.Join(" ", Eis.Select(e => $"{e.Key}{(e.Value ? "A" : "R")}")) : "")})";
    }

    /// <summary>Een stuk route in één richting, met de wissels die vóór het vertrek omgezet worden.</summary>
    internal sealed class Been
    {
        public Richting H;
        public List<int> Melders = new();
        public Dictionary<int, bool> Zetten = new();
        public bool Omkeer;
    }

    internal sealed class Plan
    {
        public Toestand Start;
        public Toestand Doel;
        public List<PlanStap> Stappen = new();
        public List<Been> Benen = new();
        public int Kosten;
        public override string ToString() => Stappen.Count == 0 ? "de loc staat al goed" : string.Join(", ", Stappen);
    }

    private sealed class DijkstraKaart
    {
        public Toestand Start;
        public Dictionary<Toestand, int> Kosten = new();
        public Dictionary<Toestand, (Toestand Vorige, PlanStap Stap)> Vorige = new();
    }

    private Toestand Hier => new(_k.LocMelder, _k.LocVia, _k.LocRichting);

    /// <summary>WAT: de stap(pen) die vanuit toestand t mogelijk zijn: verder rijden (alle bekende uitkomsten met hun eis) of omkeren.</summary>
    private IEnumerable<(Toestand Naar, PlanStap Stap, int Kosten)> Buren(Toestand t)
    {
        // Per uitkomst één stand: die het minst afwijkt van wat er nu op de baan staat (meestal de basisstand). Zo kiezen twee stappen door dezelfde
        // knoop (bijv. 4 -> 129 -> 14) geen tegenstrijdige standen voor dezelfde wissel.
        foreach (var g in _k.Mogelijkheden(t.Via, t.M, t.H).Where(x => x.Uitkomst > 0).GroupBy(x => x.Uitkomst))
        {
            var (uit, eis, terugval, _) = g.OrderBy(x => x.Eis.Count(e => Fysiek(e.Key) != e.Value)).ThenByDescending(x => x.Aantal).First();
            yield return (new Toestand(uit, t.M, t.H), new PlanStap { Van = t.M, Naar = uit, H = t.H, Eis = eis }, terugval ? 3 : 1);
        }
        if (t.Via > 0)
        {
            // omkeren: terug naar Via, door dezelfde knoop; eis = de stand waarmee eerder van Via naar M gereden is
            var eis = EisVoorOvergang(t.Via, t.M, t.H);
            yield return (new Toestand(t.Via, t.M, t.H.Om()), new PlanStap { Van = t.M, Naar = t.Via, H = t.H.Om(), Eis = eis ?? new(), Omkeer = true }, 4);
        }
    }

    /// <summary>WAT: de stand van de wissels in de knoop tussen <paramref name="van"/> en <paramref name="naar"/> waarmee de loc eerder in richting
    /// <paramref name="h"/> van van naar naar reed (de stand die het minst van de huidige afwijkt). Null als die overgang nooit gereden is.</summary>
    private Dictionary<int, bool>? EisVoorOvergang(int van, int naar, Richting h)
    {
        var rel = _k.RelevantInKnoop(van, h);
        var kand = _k.Stappen.Where(s => s.Melder == van && s.Richting == h && s.Uitkomst == naar).ToList();
        if (kand.Count == 0) return null;
        var beste = kand.OrderBy(s => rel.Count(a => s.Stand.Contains(a) != Fysiek(a))).First();
        return rel.ToDictionary(a => a, a => beste.Stand.Contains(a));
    }

    /// <summary>WAT: kortste routes (in stappen, omkeren telt zwaarder) vanaf de huidige plek (of <paramref name="vanaf"/>) naar alle bereikbare toestanden.</summary>
    private DijkstraKaart Dijkstra(Toestand? vanaf = null)
    {
        var kaart = new DijkstraKaart { Start = vanaf ?? Hier };
        var rij = new PriorityQueue<Toestand, int>();
        kaart.Kosten[kaart.Start] = 0;
        rij.Enqueue(kaart.Start, 0);
        while (rij.TryDequeue(out var t, out var k))
        {
            if (k > kaart.Kosten[t]) continue;
            if (k > 400) break;
            foreach (var (n, stap, c) in Buren(t))
            {
                int nk = k + c;
                if (kaart.Kosten.TryGetValue(n, out var oud) && oud <= nk) continue;
                kaart.Kosten[n] = nk;
                kaart.Vorige[n] = (t, stap);
                rij.Enqueue(n, nk);
            }
        }
        return kaart;
    }

    private static List<PlanStap> Pad(DijkstraKaart kaart, Toestand doel)
    {
        var res = new List<PlanStap>();
        var t = doel;
        while (!t.Equals(kaart.Start) && kaart.Vorige.TryGetValue(t, out var v)) { res.Add(v.Stap); t = v.Vorige; }
        res.Reverse();
        return res;
    }

    /// <summary>WAT: verdeelt een route in benen en bepaalt waar welke wissel omgezet wordt. Elke wissel gaat om op de eerste veilige parkeerplek nadat zijn oude
    /// stand voor het laatst nodig was. Onderweg niet gebruikte, gevonden wissels gaan aan het begin terug naar de basisstand (als dat veilig kan), en nog niet
    /// gevonden adressen altijd. Null = niet uit te voeren (geen veilige plek om een wissel om te zetten).</summary>
    private Plan? MaakPlan(Toestand start, List<PlanStap> stappen, Toestand doel, int kosten, IReadOnlyDictionary<int, bool>? extraAanEind = null)
    {
        var plan = new Plan { Start = start, Doel = doel, Stappen = stappen, Kosten = kosten };
        var cur = _adressen.ToDictionary(a => a, a => Fysiek(a));
        var laatst = new Dictionary<int, int>();
        var zetten = new Dictionary<int, Dictionary<int, bool>>();
        int Plek(int p) => p == 0 ? start.M : stappen[p - 1].Naar;
        bool VeiligOp(int p, int a)
        {
            var zone = _k.Zone(a);
            foreach (var (v, _) in Volgers(a, false)) zone.UnionWith(_k.Zone(v));
            if (zone.Count == 0) return ProefPlek(Plek(p));   // nog niet gevonden: alleen op een geschikte plek (netjes op één melder, geen kruising)
            return !zone.Contains(Plek(p)) && !(p == 0 && _monitor.Bezet.Any(zone.Contains));
        }
        bool Plaats(int a, bool v, int lo, int hi)
        {
            var breekpunten = new HashSet<int>(zetten.Keys) { 0 };
            for (int i = 0; i < stappen.Count; i++) if (stappen[i].Omkeer) breekpunten.Add(i);
            int? keuze = null;
            for (int p = lo; p <= hi; p++) if (breekpunten.Contains(p) && VeiligOp(p, a)) { keuze = p; break; }
            if (keuze is null) for (int p = lo; p <= hi; p++) if (VeiligOp(p, a)) { keuze = p; break; }
            if (keuze is not int q) { _planFout = $"wissel {a} kan tussen stap {lo} en {hi} ({string.Join(",", Enumerable.Range(lo, hi - lo + 1).Select(Plek))}) nergens veilig om (route vanaf {start.M}: {string.Join(", ", stappen)})"; return false; }
            if (!zetten.TryGetValue(q, out var z)) zetten[q] = z = new Dictionary<int, bool>();
            z[a] = v; cur[a] = v;
            return true;
        }
        for (int i = 0; i < stappen.Count; i++)
            foreach (var (a, v) in stappen[i].Eis)
            {
                if (!cur.ContainsKey(a)) continue;
                if (cur[a] != v && !Plaats(a, v, laatst.TryGetValue(a, out var l) ? l + 1 : 0, i)) return null;
                laatst[a] = i;
            }
        if (extraAanEind is not null)
            foreach (var (a, v) in extraAanEind)
            {
                if (!cur.ContainsKey(a) || cur[a] == v) continue;
                if (!Plaats(a, v, laatst.TryGetValue(a, out var l) ? l + 1 : 0, stappen.Count)) return null;
            }
        // opruimen aan het begin: gevonden wissels die de route niet nodig heeft terug naar de basisstand, niet gevonden adressen altijd
        var gebruikt = stappen.SelectMany(s => s.Eis.Keys).Concat(extraAanEind?.Keys ?? Enumerable.Empty<int>()).ToHashSet();
        foreach (var a in _adressen)
        {
            if (gebruikt.Contains(a) || Fysiek(a) == BasisStand(a)) continue;
            if (!VeiligOp(0, a)) continue;
            if (!zetten.TryGetValue(0, out var z0)) zetten[0] = z0 = new Dictionary<int, bool>();
            z0.TryAdd(a, BasisStand(a));
        }
        // benen maken
        var grenzen = new SortedSet<int>(zetten.Keys) { 0 };
        for (int i = 0; i < stappen.Count; i++) if (stappen[i].Omkeer) grenzen.Add(i);
        var lijst = grenzen.ToList();
        for (int g = 0; g < lijst.Count; g++)
        {
            int van = lijst[g], tot = g + 1 < lijst.Count ? lijst[g + 1] : stappen.Count;
            var been = new Been { Zetten = zetten.TryGetValue(van, out var z) ? z : new Dictionary<int, bool>() };
            if (van < stappen.Count) { been.H = stappen[van].H; been.Omkeer = stappen[van].Omkeer; }
            for (int i = van; i < tot; i++) been.Melders.Add(stappen[i].Naar);
            plan.Benen.Add(been);
        }
        if (zetten.TryGetValue(stappen.Count, out var zEind)) plan.Benen.Add(new Been { Zetten = zEind, H = stappen.Count > 0 ? stappen[^1].H : start.H });
        return plan;
    }

    private string _planFout = "";
    private readonly Dictionary<Toestand, DijkstraKaart> _omwegCache = new();
    private int _omwegCacheVoor = -1;

    /// <summary>WAT: een uitvoerbaar plan naar toestand <paramref name="doel"/>. Lukt de kortste route niet (een wissel die om moet kan nergens veilig om,
    /// bijv. wissel 9 terwijl de loc op melder 4 staat), dan een omweg via een plek in de buurt waar het wel kan (eerst een stukje weg, omzetten, terug).</summary>
    private Plan? PlanNaar(DijkstraKaart kaart, Toestand doel, IReadOnlyDictionary<int, bool>? extraAanEind = null)
    {
        if (!kaart.Kosten.TryGetValue(doel, out var kost)) return null;
        var p = MaakPlan(kaart.Start, Pad(kaart, doel), doel, kost, extraAanEind);
        if (p is not null) return p;
        if (_omwegCacheVoor != _k.Stappen.Count + _k.Relevant.Count * 1000) { _omwegCache.Clear(); _omwegCacheVoor = _k.Stappen.Count + _k.Relevant.Count * 1000; }
        foreach (var (s, ks) in kaart.Kosten.Where(kv => kv.Value is >= 1 and <= 16).OrderBy(kv => kv.Value).Take(14))
        {
            if (!_omwegCache.TryGetValue(s, out var k2)) _omwegCache[s] = k2 = Dijkstra(s);
            if (!k2.Kosten.TryGetValue(doel, out var kd)) continue;
            var stappen = Pad(kaart, s).Concat(Pad(k2, doel)).ToList();
            p = MaakPlan(kaart.Start, stappen, doel, ks + kd + 2, extraAanEind);
            if (p is not null) return p;
        }
        return null;
    }

    /// <summary>WAT: het beste plan voor een taak, met de kosten. Null = (nu) niet bereikbaar.</summary>
    private (Plan? Plan, int Kosten) PlanVoor(Taak t, DijkstraKaart kaart)
    {
        IEnumerable<Toestand> doelen;
        switch (t.Soort)
        {
            case Soort.Verken:
                doelen = kaart.Kosten.Keys.Where(x => x.M == t.Melder && x.H == t.Richting);
                break;
            case Soort.Blok:
                doelen = kaart.Kosten.Keys.Where(x => x.M == t.Melder && x.Via > 0);
                break;
            case Soort.Dekking:
                doelen = new[] { t.Dekking!.Start };
                break;
            default:
                // proeven: de kosten om bij de ingang te komen (de proef plant zichzelf bij het uitvoeren opnieuw)
                var keuze = KiesProefStart(kaart, t.Via, t.Melder, t.Richting, new Dictionary<int, bool>());
                if (keuze is null) return (null, 0);
                return (keuze.Value.Plan, keuze.Value.Plan.Kosten + 2 * keuze.Value.Keten.Count);
        }
        foreach (var d in doelen.OrderBy(d => kaart.Kosten[d]).Take(6))
        {
            var p = PlanNaar(kaart, d, t.Soort == Soort.Dekking ? t.Dekking!.Eis : null);
            if (p is not null) return (p, p.Kosten);
        }
        return (null, 0);
    }

    /// <summary>WAT: voert een plan uit: per been de wissels zetten en rijden. False als de loc ergens anders uitkwam dan gepland (dat is dan wel
    /// vastgelegd; de volgende taak plant opnieuw).</summary>
    private async Task<bool> VoerPlanUit(Plan plan)
    {
        if (!Hier.Equals(plan.Start)) { _log.Info("De loc staat niet meer waar het plan begon; opnieuw plannen."); return false; }
        if (plan.Stappen.Count > 0) _log.Stap($"Route: {plan}.");
        foreach (var been in plan.Benen)
        {
            if (been.Zetten.Count > 0 && !await ZetStand(been.Zetten, "route")) return false;
            if (been.Melders.Count == 0) continue;
            var uit = await Rijd(new RitOpdracht2 { Richting = been.H, Verwacht = been.Melders, StopBijAfwijking = true, Reden = "naar " + been.Melders[^1] });
            if (uit.Einde != RitEinde2.Geparkeerd || uit.Reeks[^1] != been.Melders[^1]) return false;
        }
        return true;
    }

    // ================================================================ proeven

    /// <summary>WAT: kies waar de loc een proef op ingang (via, m, r) begint: een parkeerplek Q van waaruit een bekende keten van hoogstens 3 stappen
    /// (eerste stap mag omkeren) via 'via' naar m leidt, en waar het veilig is om de wissels van de proef om te zetten.</summary>
    private (Plan Plan, List<PlanStap> Keten)? KiesProefStart(DijkstraKaart kaart, int via, int m, Richting r, IReadOnlyDictionary<int, bool> stand, IReadOnlySet<int>? zacht = null)
    {
        (Plan, List<PlanStap>)? beste = null; int besteKosten = int.MaxValue;
        foreach (var (q, kost) in kaart.Kosten.OrderBy(kv => kv.Value).Take(80))
        {
            if (kost >= besteKosten) break;
            var keten = ZoekKeten(q, via, m, r, 3);
            if (keten is null) continue;
            // eisen van de keten mogen niet botsen met de proefstand
            var zet = new Dictionary<int, bool>();
            bool botst = false;
            foreach (var s in keten) foreach (var (a, v) in s.Eis) { if (stand.TryGetValue(a, out var w) && w != v && zacht?.Contains(a) != true) botst = true; zet[a] = v; }
            if (botst) continue;
            foreach (var (a, v) in stand) if (!(zacht?.Contains(a) == true && zet.ContainsKey(a))) zet[a] = v;
            // nog niet gevonden adressen gaan op Q om: Q moet een geschikte plek zijn
            if (zet.Any(kv => !_k.IsGevonden(kv.Key) && Fysiek(kv.Key) != kv.Value) && !ProefPlek(q.M)) continue;
            // veilig op Q?
            bool veilig = true;
            foreach (var (a, v) in zet)
            {
                if (Fysiek(a) == v) continue;
                var zone = _k.Zone(a);
                foreach (var (vv, _) in Volgers(a, false)) zone.UnionWith(_k.Zone(vv));
                if (zone.Contains(q.M)) { veilig = false; break; }
            }
            if (!veilig) continue;
            var plan = PlanNaar(kaart, q);
            if (plan is null) continue;
            int totaal = plan.Kosten + 2 * keten.Count;
            if (totaal < besteKosten) { besteKosten = totaal; beste = (plan, keten); }
        }
        return beste;
    }

    /// <summary>Een keten van bekende stappen vanuit toestand q (eerste stap eventueel omkeren) die eindigt met de stap via -> m in richting r.</summary>
    private List<PlanStap>? ZoekKeten(Toestand q, int via, int m, Richting r, int max)
    {
        var res = new List<PlanStap>();
        bool Zoek(Toestand t, int diepte)
        {
            if (t.M == via && t.H == r)
            {
                var mog = _k.Mogelijkheden(t.Via, via, r).Where(x => x.Uitkomst == m).ToList();
                if (mog.Count > 0) { res.Add(new PlanStap { Van = via, Naar = m, H = r, Eis = mog[0].Eis }); return true; }
                // het eerste stuk van de proef mag ook de ingang zelf zijn zonder bekende stap (bijv. alleen kortsluiting bekend): via -> m moet dan
                // eerder gereden zijn als stap die m binnenkwam
                if (_k.Stappen.Any(s => s.Melder == via && s.Richting == r && s.Uitkomst == m)) { res.Add(new PlanStap { Van = via, Naar = m, H = r, Eis = EisVoorOvergang(via, m, r) ?? new() }); return true; }
            }
            if (t.M == via && t.H == r.Om() && t.Via == m && diepte == 0)
            {
                res.Add(new PlanStap { Van = via, Naar = m, H = r, Eis = EisVoorOvergang(m, via, r.Om()) ?? new(), Omkeer = true });
                return true;
            }
            if (diepte >= max - 1) return false;
            foreach (var (n, stap, _) in Buren(t))
            {
                if (stap.Omkeer && diepte > 0) continue;
                if (stap.H != r) continue;
                res.Add(stap);
                if (Zoek(n, diepte + 1)) return true;
                res.RemoveAt(res.Count - 1);
            }
            return false;
        }
        return Zoek(q, 0) ? res.ToList() : null;
    }

    /// <summary>
    /// WAT: één proef: rijd naar een veilige plek vóór de ingang (via -> m, richting r), zet de wissels in <paramref name="stand"/> (de rest van de nog niet gevonden
    /// adressen in de basisstand), rijd via 'via' door m en kijk waar de loc uitkomt. Daarna langs DEZELFDE weg terug naar 'via' (met dezelfde wisselstanden),
    /// pas daar gaan de proefstanden terug. Geeft de uitkomst (melder, 0 = doodlopend, -1 = kortsluiting) of null als de proef niet uit te voeren was.
    /// WAAROM: Marco 26-10 08:40 ("wissel 2 rechtdoor, naar 13 doorrijden, dan wissel 2 afbuigend en weer naar 5 en kijken wat er volgt"): heen en terug langs
    /// dezelfde weg is altijd veilig, en de wissel gaat pas om als de loc er een hele melder vandaan staat.
    /// </summary>
    private async Task<int?> Proef(int via, int m, Richting r, Dictionary<int, bool> stand, bool uitweg = false, Dictionary<int, bool>? herstel = null, IReadOnlySet<int>? zacht = null)
    {
        for (int poging = 0; poging < 3; poging++)
        {
            var keuze = KiesProefStart(Dijkstra(), via, m, r, stand, zacht);
            if (keuze is null) { _log.Waarschuwing($"Geen veilige plek gevonden om de proef op {via} -> {m} ({r.Tekst()}) te beginnen."); return null; }
            var (plan, keten) = keuze.Value;
            if (!await VoerPlanUit(plan)) continue;
            var zet = new Dictionary<int, bool>();
            foreach (var s in keten) foreach (var (a, v) in s.Eis) zet[a] = v;
            foreach (var a in _adressen.Where(a => !_k.IsGevonden(a))) zet[a] = BasisStand(a);
            foreach (var (a, v) in stand) if (!(zacht?.Contains(a) == true && keten.Any(s => s.Eis.ContainsKey(a)))) zet[a] = v;
            if (!await ZetStand(zet, "proef")) return null;
            _k.Experimenten++;
            var melders = keten.Select(s => s.Naar).ToList();
            var richting = r;
            // bij kortsluiting: de proefwissels terug naar de stand waarmee deze stap eerder goed ging (als die er is), anders langzaam terug
            var herstelStand = herstel ?? HerstelStand(via, m, r, stand);
            var uit = await Rijd(new RitOpdracht2 { Richting = richting, Verwacht = melders, ExtraStappen = 1, StopBijAfwijking = true, Reden = "proef", Herstel = herstelStand });
            var reeks = uit.Reeks;
            int idxM = reeks.IndexOf(m);
            // kwam de loc niet (zoals verwacht) tot m, dan is het geen geldige proef: opnieuw plannen
            bool tot = idxM >= 1 && reeks.Take(idxM + 1).SequenceEqual(new[] { reeks[0] }.Concat(melders));
            int? uitkomst = null;
            if (tot)
            {
                if (reeks.Count > idxM + 1) uitkomst = reeks[idxM + 1];
                else if (uit.Einde == RitEinde2.Kortsluiting) uitkomst = StapUitkomst.Kortsluiting;
                else if (uit.Einde == RitEinde2.Doodlopend) uitkomst = StapUitkomst.Doodlopend;
            }
            // langs dezelfde weg terug tot 'via' (de proefstanden blijven staan tot de loc daar is)
            int viaIndex = idxM >= 1 ? idxM - 1 : Math.Max(0, reeks.LastIndexOf(via));
            await TerugNaarIndex(richting, reeks, viaIndex);
            if (uitkomst is null) { _log.Info("De proef ging niet zoals gepland; opnieuw."); continue; }
            _log.Vondst($"Proef {via} -> {m} ({r.Tekst()}): {StapUitkomst.Tekst(uitkomst.Value)}.");
            // proefstanden van nog niet gevonden adressen terug (de loc staat nu een melder voor de knoop)
            var basis = _adressen.Where(a => !_k.IsGevonden(a) && Fysiek(a) != BasisStand(a)).ToDictionary(a => a, a => BasisStand(a));
            if (basis.Count > 0 && ProefPlek(_k.LocMelder)) await ZetStand(basis, "proef terug");
            return uitkomst;
        }
        return null;
    }

    /// <summary>WAT: de stand van de proefwissels waarmee de stap via -> m (richting r) eerder wel goed ging (geen kortsluiting); leeg als die niet bekend is.</summary>
    private Dictionary<int, bool> HerstelStand(int via, int m, Richting r, IReadOnlyDictionary<int, bool> stand)
    {
        var goed = _k.Stappen.Where(s => s.Melder == m && s.Richting == r && s.Uitkomst > 0 && (s.Via == via || via == 0))
                             .OrderBy(s => s.Afgeleid).ThenByDescending(s => s.Aantal).FirstOrDefault();
        return goed is null ? new Dictionary<int, bool>() : stand.Keys.ToDictionary(a => a, a => goed.Stand.Contains(a));
    }

    /// <summary>WAT: rijdt de loc na een proef langs de gereden weg terug tot melder reeks[<paramref name="doel"/>] (en daarna eventueel nog naar
    /// <paramref name="verder"/>) en parkeert daar. WAAROM: met dezelfde wisselstanden is de weg terug altijd berijdbaar; pas daarna gaan de proefstanden terug.</summary>
    private async Task TerugNaarIndex(Richting h, List<int> reeks, int doel, int verder = 0)
    {
        doel = Math.Clamp(doel, 0, reeks.Count - 1);
        // Waar staat de loc nu? Normaal op het eind van de rit; na een blokproef op een korte melder kan hij een melder verder staan.
        int eind = reeks.LastIndexOf(_k.LocMelder);
        var terug = new List<int>();
        if (eind < 0 && _k.LocRichting == h && _k.LocVia == reeks[^1]) { terug.Add(reeks[^1]); eind = reeks.Count - 1; }
        else if (eind < 0) { eind = reeks.Count - 1; _k.LocMelder = reeks[^1]; _k.LocVia = reeks.Count >= 2 ? reeks[^2] : 0; _k.LocRichting = h; }
        else if (_k.LocRichting == h) { _k.LocVia = eind >= 1 ? reeks[eind - 1] : _k.LocVia; }
        for (int j = eind - 1; j >= doel; j--) terug.Add(reeks[j]);
        if (verder > 0) terug.Add(verder);
        if (terug.Count == 0) return;
        _monitor.Bijwerken();
        await Rijd(new RitOpdracht2 { Richting = h.Om(), Verwacht = terug, StopBijAfwijking = true, Reden = "terug langs dezelfde weg" });
    }

    /// <summary>WAT: mag hier een nog niet gevonden adres omgezet worden? Alleen op een melder die geen kruising is (niet aan beide kanten twee of meer buren,
    /// niet afhankelijk van de ingang) en die niet heel kort is. WAAROM: een nog niet gevonden wissel kan overal liggen; in het midden van een gewone
    /// melder staat de loc er niet op, maar op een Engelse wissel (zoals melder 24) ligt de tong overal onder de loc (simulatie: wissel 10 bij melder 24).</summary>
    private bool ProefPlek(int m)
    {
        if (m <= 0) return false;
        if (_k.KantBuren(m, Richting.Vooruit).Count >= 2 && _k.KantBuren(m, Richting.Achteruit).Count >= 2) return false;
        if (_k.ViaAfhankelijk(m, Richting.Vooruit) || _k.ViaAfhankelijk(m, Richting.Achteruit)) return false;
        if (_k.SoloTijd.TryGetValue(m, out var t) && t > 0 && t < 1.0) return false;
        return true;
    }

    // ================================================================ dekking: nog niet gevonden adressen over de bekende baan testen

    /// <summary>Een rit waarin alle nog niet gevonden adressen die op dat stuk nog niet getest zijn tegelijk omgezet worden.</summary>
    internal sealed class DekkingRit
    {
        public Toestand Start;
        public List<int> Pad = new();
        public Dictionary<int, bool> Eis = new();
        public List<int> Adressen = new();
        public int Winst;
        public string Sleutel = "";
        public string Omschrijving = "";
    }

    /// <summary>WAT: zoekt vanaf de bereikbare plekken het stuk bekende baan (hoogstens 25 stappen in één richting, zonder wissels om te zetten onderweg) waarop
    /// het meeste nog te testen is. WAAROM: alle nog niet gevonden adressen tegelijk omzetten en een lang stuk rijden test veel in één rit; adressen die niet
    /// bestaan (bijv. 3, 4, 7, 8, 11, 12) vallen zo snel af.</summary>
    private IEnumerable<DekkingRit> DekkingRitten()
    {
        var open = _adressen.Where(a => !_k.IsGevonden(a)).ToList();
        if (open.Count == 0) yield break;
        var kaart = Dijkstra();
        var ritten = new List<(DekkingRit Rit, double Score)>();
        foreach (var (s, kost) in kaart.Kosten.OrderBy(kv => kv.Value).Take(60))
        {
            var rit = SimuleerDekking(s, open);
            if (rit is null || rit.Winst == 0) continue;
            ritten.Add((rit, rit.Winst / (double)(kost + rit.Pad.Count + 6)));
        }
        // de beste paar (de taakkeuze neemt de goedkoopste die ook echt te plannen is)
        foreach (var (rit, _) in ritten.OrderByDescending(x => x.Score).Take(5)) yield return rit;
    }

    private DekkingRit? SimuleerDekking(Toestand s, List<int> open)
    {
        var rit = new DekkingRit { Start = s, Pad = { s.M } };
        var t = s;
        var gebruikt = new HashSet<string>();
        for (int stap = 0; stap < 25; stap++)
        {
            var opties = _k.Mogelijkheden(t.Via, t.M, t.H).Where(o => o.Uitkomst > 0 && o.Eis.All(e => !rit.Eis.TryGetValue(e.Key, out var v) || v == e.Value)).ToList();
            if (opties.Count == 0) break;
            var keuze = opties.OrderByDescending(o => open.Count(a => !_k.IsGetest(a, t.M, o.Uitkomst))).ThenByDescending(o => o.Aantal).First();
            var sleutel = $"{t.M}>{keuze.Uitkomst}";
            foreach (var (a, v) in keuze.Eis) rit.Eis[a] = v;
            rit.Pad.Add(keuze.Uitkomst);
            if (!gebruikt.Add(sleutel)) break;
            t = new Toestand(keuze.Uitkomst, t.M, t.H);
        }
        if (rit.Pad.Count < 2) return null;
        var adressen = new HashSet<int>();
        for (int i = 1; i < rit.Pad.Count; i++)
            foreach (var a in open) if (!_k.IsGetest(a, rit.Pad[i - 1], rit.Pad[i])) { adressen.Add(a); rit.Winst++; }
        // de nog niet gevonden adressen gaan op de startplek om: dat moet een geschikte plek zijn (geen kruising, niet te kort)
        if (!ProefPlek(s.M)) return null;
        // de wissels die de rit nodig heeft, moeten op de startplek veilig om te zetten zijn
        foreach (var (a, v) in rit.Eis)
        {
            if (Fysiek(a) == v) continue;
            var zone = _k.Zone(a);
            if (zone.Contains(s.M)) return null;
        }
        rit.Adressen = adressen.OrderBy(a => a).ToList();
        rit.Sleutel = $"D|{s.M}|{s.Via}|{s.H}|{string.Join(",", rit.Pad)}|{string.Join(",", rit.Adressen)}";
        rit.Omschrijving = $"adres {string.Join(", ", rit.Adressen)} tegelijk omgezet testen over {string.Join(" - ", rit.Pad)} ({s.H.Tekst()})";
        return rit;
    }

    /// <summary>WAT: de dekkingsrit uitvoeren. Komt de loc ergens anders uit (of kortsluiting), dan rijdt hij langs dezelfde weg terug tot een melder vóór die knoop
    /// en gaan de adressen daar terug; de tegenspraak-taak zoekt daarna uit welk adres het was.</summary>
    private async Task<bool> DoeDekking(Taak t)
    {
        var d = t.Dekking!;
        if (!await VoerPlanUit(t.Plan!)) return false;
        if (!Hier.Equals(d.Start)) return false;
        var zet = new Dictionary<int, bool>(d.Eis);
        foreach (var a in d.Adressen) zet[a] = !BasisStand(a);
        if (!await ZetStand(zet, "dekking")) return false;
        var basis = d.Adressen.ToDictionary(a => a, a => BasisStand(a));
        var uit = await Rijd(new RitOpdracht2 { Richting = d.Start.H, Verwacht = d.Pad.Skip(1).ToList(), StopBijAfwijking = true, Reden = "adressen testen", Herstel = basis });
        var reeks = uit.Reeks;
        int goed = 0;
        for (int i = 1; i < reeks.Count && i < d.Pad.Count && reeks[i] == d.Pad[i]; i++)
        {
            foreach (var a in d.Adressen) _k.MarkeerGetest(a, d.Pad[i - 1], d.Pad[i]);
            goed = i;
        }
        bool afwijking = uit.Einde is RitEinde2.Kortsluiting or RitEinde2.Doodlopend || uit.AfwijkingIndex is not null;
        if (!afwijking)
        {
            _log.Info($"Adres {string.Join(", ", d.Adressen)}: geen effect op {string.Join(" - ", d.Pad)}.");
            // terugzetten mag alleen op een geschikte plek (niet op een kruising); anders eerst langs dezelfde weg terug tot zo'n plek
            if (!ProefPlek(reeks[^1]))
            {
                int j = reeks.Count - 2;
                while (j > 0 && !ProefPlek(reeks[j])) j--;
                await TerugNaarIndex(d.Start.H, reeks, j);
            }
            await ZetStand(basis, "dekking terug");
            return true;
        }
        _log.Vondst($"Met adres {string.Join(", ", d.Adressen)} omgezet ging de loc na melder {reeks[Math.Min(goed, reeks.Count - 1)]} ergens anders heen ({(uit.Einde == RitEinde2.Kortsluiting ? "kortsluiting" : uit.Einde == RitEinde2.Doodlopend ? "doodlopend" : "melder " + reeks[^1])}); dat wordt uitgezocht.");
        // terug tot een melder vóór de knoop waar het anders ging (een hele melder ervandaan), dan pas de adressen terug
        await TerugNaarIndex(d.Start.H, reeks, Math.Max(0, goed - 1), goed == 0 ? d.Start.Via : 0);
        await ZetStand(basis, "dekking terug");
        return true;
    }
}
