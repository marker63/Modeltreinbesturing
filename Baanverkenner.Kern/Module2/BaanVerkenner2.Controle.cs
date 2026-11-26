namespace Baanverkenner.Kern;

/// <summary>'Voltooide baan nogmaals checken' voor een baan die met module 2 verkend is (BUG #412).</summary>
public partial class BaanVerkenner2
{
    /// <summary>BUG #412: de overgangen die tijdens deze controle gereden zijn (van, naar, kaartrichting). Null buiten de controle.</summary>
    private HashSet<(int Van, int Naar, Richting R)>? _gereden412;

    /// <summary>WAT: rijdt elke bekende overgang van de baan (melder naar melder, in elke richting die bekend is) één keer, met de wisselstanden die de kennis
    /// daarvoor kent. Elke kortsluiting of afwijking is een fout in de kaart en wordt gemeld; aan het eind volgt een verslag met wat wel en niet gereden is.
    /// WAAROM (Marco 12-11 08:29): "bij een voltooide baan nogmaals checken ... alles moet gereden zijn en er mogen geen kortsluitmeldingen meer gezien worden,
    /// je weet dan al hoe een wissel ligt dus hoef je die fout niet te maken." De oude controle (module 1) werkt met trajecten die module 2 niet maakt; daardoor
    /// gebeurde er bij Marco niets (09-11 15:54). Deze controle gebruikt dezelfde routeplanner als de verkenning: alleen wat al eens gelukt is wordt gereden.</summary>
    private async Task<bool> Controle412(bool tweede = false)
    {
        _fase = "Controle";
        _k.ProefInEis429 = true;
        try { return await Controle412Intern(tweede); }
        finally { _k.ProefInEis429 = false; }
    }

    private async Task<bool> Controle412Intern(bool tweede)
    {
        _gereden412 = new();
        // alle bekende overgangen (geen kortsluiting, geen doodlopend), zonder afgeleide waarnemingen
        var alle = _k.Stappen.Where(s => s.Uitkomst > 0 && !s.Afgeleid).Select(s => (Van: s.Melder, Naar: s.Uitkomst, R: s.Richting)).Distinct().ToList();
        var fout = new List<string>();
        var nietBereikt = new HashSet<(int, int, Richting)>();
        var pogingen = new Dictionary<(int, int, Richting), int>();
        var reden = new Dictionary<(int, int, Richting), string>();
        int kortVoor = _k.Kortsluitingen, rittenVoor = _k.Ritten;
        _log.Stap($"{(tweede ? "Tweede controle" : "Controle")} van de voltooide baan (module 2): {alle.Count} bekende overgangen rijden met de bekende wisselstanden (BUG #412, nog niet getest op de baan).");
        for (int ronde = 0; ronde < 4 * alle.Count + 20; ronde++)
        {
            _token.ThrowIfCancellationRequested();
            await PauzeMoment();
            if (!await LocNetjes412()) { fout.Add($"De loc is kwijt na melder {_k.LocMelder}; de controle is gestopt."); break; }
            var open = alle.Where(o => !_gereden412.Contains(o) && !nietBereikt.Contains(o)).ToList();
            if (open.Count == 0) break;
            var kaart = Dijkstra();
            var bereikbaar = open.Select(o => (O: o, Doel: new Toestand(o.Naar, o.Van, o.R)))
                                 .Where(x => kaart.Kosten.ContainsKey(x.Doel)).OrderBy(x => kaart.Kosten[x.Doel]).ToList();
            if (bereikbaar.Count == 0) { foreach (var o in open) nietBereikt.Add(o); break; }
            int kortNu = _k.Kortsluitingen;
            bool ok;
            // De wissels voorbij de doelmelder alvast goed zetten voor een bekende vervolgstap: na aankomst kruipt de loc nog een stukje door (netjes op één
            // melder), en met een verkeerd staande wissel daarachter gaf dat in de simulatie kortsluiting (10 - 129 - 24 vooruit, wissel 14 van achteren).
            // WAT (BUG #429): van de dichtstbijzijnde open overgangen (hoogstens 6) de eerste nemen waarvoor een plan bestaat waarin geen wissel 'blijft staan'.
            // WAAROM (controle Marco 26-11 09:56): de loc stond op de kruiswissel 24 en de dichtstbijzijnde overgang vroeg een andere stand van wissel 5 dan er
            // stond; die kon op 24 niet om, bleef staan en de loc reed een kortsluiting in. Een andere open overgang vanaf 24 kon wél met de staande wissels.
            var openSet429 = open.ToHashSet();
            var doelO = bereikbaar[0].O;
            Toestand doel = bereikbaar[0].Doel;
            Dictionary<int, bool>? verder = null;
            Plan? plan = null;
            bool eerste429 = true;
            foreach (var kand in bereikbaar.Take(6))
            {
                var vd = VervolgEis429(kand.Doel, uit => openSet429.Contains((kand.Doel.M, uit, kand.Doel.H)));
                var pl = PlanNaar(kaart, kand.Doel, vd);
                if (pl is null && vd is not null) pl = PlanNaar(kaart, kand.Doel);
                if (eerste429) { doelO = kand.O; doel = kand.Doel; verder = vd; plan = pl; eerste429 = false; }
                if (pl is not null && pl.Zacht is null) { doelO = kand.O; doel = kand.Doel; verder = vd; plan = pl; break; }
            }
            if (Environment.GetEnvironmentVariable("MARCO2_DEBUG") == "1") _log.Info($"DEBUG controle verder na {doel.M} (via {doel.Via}, {doel.H}): {(verder is null ? "geen" : string.Join(" ", verder.Select(e => $"{e.Key}{(e.Value ? "A" : "R")}")))}");
            if (plan is null) ok = false;
            else
            {
                _log.Stap($"Controle: overgang {doelO.Van} -> {doelO.Naar} ({doelO.R.Tekst()}), nog {open.Count} te gaan.");
                try { ok = await VoerPlanUit(plan); }
                catch (PlanFout pf) { _log.Waarschuwing($"Controle: {pf.Message}"); ok = false; }
            }
            if (_k.Kortsluitingen > kortNu)
                fout.Add($"Kortsluiting op weg naar {doelO.Van} -> {doelO.Naar} ({doelO.R.Tekst()}), na melder {_k.LocMelder}: de kaart klopt daar niet of een wissel stond niet goed.");
            if (!_gereden412.Contains(doelO))
            {
                int n = pogingen[doelO] = pogingen.GetValueOrDefault(doelO) + 1;
                if (plan is null) reden[doelO] = $"geen uitvoerbare route: {_planFout}";
                if (n >= 3) nietBereikt.Add(doelO);
            }
            BewaarNu();
        }
        _gereden412 ??= new();
        // WAT: een overgang die de controle niet kon rijden en die maar één keer gezien is, is bijna zeker een foute waarneming (een gemiste of te vroeg gemelde
        // melder op de grens van twee secties). Die wordt uit de kennis gehaald en telt niet meer mee.
        // WAAROM (BUG #427, controle 19-11 16:49 en 17:09: "Niet gereden: 24->129 (vooruit), 129->21 (vooruit)"): rit #99 om 09:08 meldde 24, 129, 21 vooruit, terwijl
        // de loc op de grens van 24 en 129 stond. Zo'n overgang bestaat op de baan niet en de controle kan hem nooit rijden.
        var onbetrouwbaar427 = alle.Where(o => !_gereden412.Contains(o)
            && _k.Stappen.Where(x => x.Melder == o.Van && x.Uitkomst == o.Naar && x.Richting == o.R && !x.Afgeleid).Sum(x => x.Aantal) <= 1).ToList();
        foreach (var o in onbetrouwbaar427)
        {
            _k.Stappen.RemoveAll(x => x.Melder == o.Van && x.Uitkomst == o.Naar && x.Richting == o.R);
            _log.Waarschuwing($"Overgang {o.Van} -> {o.Naar} ({o.R.Tekst()}) is maar één keer gezien en was in de controle niet te rijden: waarschijnlijk een foute waarneming, weggehaald (BUG #427, nog niet getest op de baan).");
        }
        if (onbetrouwbaar427.Count > 0) { alle = alle.Except(onbetrouwbaar427).ToList(); BewaarNu(); }
        int gereden = alle.Count(o => _gereden412.Contains(o));
        var nietGereden = alle.Where(o => !_gereden412.Contains(o)).Select(o => $"{o.Van}->{o.Naar} ({o.R.Tekst()}){(reden.TryGetValue(o, out var rd) ? $" [{rd}]" : "")}").ToList();
        int kortsluitingen = _k.Kortsluitingen - kortVoor;
        bool goed = nietGereden.Count == 0 && kortsluitingen == 0 && fout.Count == 0;
        string samenvatting = $"{(tweede ? "Tweede controle" : "Controle")} (module 2): {gereden} van {alle.Count} overgangen gereden, {kortsluitingen} kortsluiting(en), {_k.Ritten - rittenVoor} ritten.";
        var verslag = new ControleVerslag { Datum = _klok.Nu, Samenvatting = samenvatting, Verschillen = !goed };
        verslag.Regels.AddRange(fout);
        if (nietGereden.Count > 0) verslag.Regels.Add($"Niet gereden: {string.Join(", ", nietGereden)}.");
        if (onbetrouwbaar427.Count > 0) verslag.Regels.Add($"Weggehaald als foute waarneming (één keer gezien, niet te rijden): {string.Join(", ", onbetrouwbaar427.Select(o => $"{o.Van}->{o.Naar} ({o.R.Tekst()})"))}.");
        if (goed) verslag.Regels.Add("Alles gereden zonder kortsluiting: de kaart klopt.");
        _status.Kaart.ControleVerslagen.Add(verslag);
        _log.Vondst(samenvatting);
        foreach (var r in verslag.Regels) (goed ? (Action<string>)_log.Info : _log.Waarschuwing)(r);
        _status.Controle = false;
        _gereden412 = null;
        _fase = "Klaar";
        return goed;
    }

    /// <summary>BUG #425 (alleen voor de tests): plant vanaf de plek van de loc elke bekende overgang zonder te rijden en geeft de overgangen terug waarvoor
    /// geen uitvoerbare route is, met de reden. <paramref name="herstel"/> = eerst de correctie van #425 toepassen (zoals bij hervatten).</summary>
    public List<string> PlanTest425(bool herstel, Dictionary<int, bool>? standen = null)
    {
        _fase = "Controle"; _k.ProefInEis429 = true;   // BUG #429: zoals in de echte controle
        if (herstel) NormaliseerAchteren426();
        if (Environment.GetEnvironmentVariable("MARCO2_ECHT_DEBUG") == "1")
        {
            foreach (var v in _k.Vondsten) Console.WriteLine($"   vondst {v.Adres} {v.Via}>{v.Melder}:{v.Richting} R={v.Rechtdoor} A={v.Afbuigend}  relevant-ingangknoop={string.Join(",", _k.RelevantInKnoop(v.Via, v.Richting))}  relevant-hier={string.Join(",", _k.RelevantInKnoop(v.Melder, v.Richting))}");
            foreach (var (k2, l2) in _k.Relevant) Console.WriteLine($"   relevant {k2}: {string.Join(",", l2)}");
        }
        // WAT: alleen voor het testprogramma: de mogelijkheden (uitkomst + eis) bij één ingang tonen, bijv. MARCO2_ECHT_MOG=8,24,A.
        if (Environment.GetEnvironmentVariable("MARCO2_ECHT_MOG") is { Length: > 0 } mog)
        {
            var d = mog.Split(',');
            var rm = d[2] == "A" ? Richting.Achteruit : Richting.Vooruit;
            Console.WriteLine($"   relevant-in-knoop {d[1]}:{rm} = {string.Join(",", _k.RelevantInKnoop(int.Parse(d[1]), rm))}");
            foreach (var x in _k.Mogelijkheden(int.Parse(d[0]), int.Parse(d[1]), rm))
                Console.WriteLine($"   mogelijk -> {x.Uitkomst} [{string.Join(" ", x.Eis.OrderBy(e => e.Key).Select(e => $"{e.Key}{(e.Value ? "A" : "R")}"))}] terugval={x.Terugval} aantal={x.Aantal}");
        }
        if (standen is not null) foreach (var (a, v) in standen) _k.Standen[a] = v;
        // WAT: alleen voor het testprogramma: het controleplan naar één doel tonen, met de vervolgstand die alvast gezet wordt (MARCO2_ECHT_DOEL=8,24,A).
        if (Environment.GetEnvironmentVariable("MARCO2_ECHT_DOEL") is { Length: > 0 } dl)
        {
            var d = dl.Split(',');
            var dt = new Toestand(int.Parse(d[1]), int.Parse(d[0]), d[2] == "A" ? Richting.Achteruit : Richting.Vooruit);
            var vd = VervolgEis429(dt);
            var pl = PlanNaar(Dijkstra(), dt, vd) ?? PlanNaar(Dijkstra(), dt);
            return new List<string> { "doel", $"DOEL {dl} standen [{string.Join(" ", _k.Standen.OrderBy(x => x.Key).Select(x => $"{x.Key}{(x.Value ? "A" : "R")}"))}] vervolg [{(vd is null ? "-" : string.Join(" ", vd.OrderBy(x => x.Key).Select(x => $"{x.Key}{(x.Value ? "A" : "R")}")))}] plan: {(pl is null ? _planFout : pl.ToString() + " | " + string.Join(" ; ", pl.Benen.Select(b => string.Join(",", b.Zetten.Select(z => $"{z.Key}{(z.Value ? "A" : "R")}")))))}{(pl?.Zacht is null ? "" : " ZACHT " + pl.Zacht)}" };
        }
        var res = new List<string>();
        var alle = _k.Stappen.Where(s => s.Uitkomst > 0 && !s.Afgeleid).Select(s => (Van: s.Melder, Naar: s.Uitkomst, R: s.Richting)).Distinct().ToList();
        var kaart = Dijkstra();
        foreach (var o in alle)
        {
            var doel = new Toestand(o.Naar, o.Van, o.R);
            if (!kaart.Kosten.ContainsKey(doel)) { res.Add($"{o.Van}->{o.Naar} ({o.R.Tekst()}): niet bereikbaar"); continue; }
            var verder = VervolgEis429(doel);
            var plan = PlanNaar(kaart, doel, verder) ?? PlanNaar(kaart, doel);
            if (plan is null) res.Add($"{o.Van}->{o.Naar} ({o.R.Tekst()}): {_planFout}");
            else if (plan.Zacht is not null) res.Add($"{o.Van}->{o.Naar} ({o.R.Tekst()}) zacht: {plan.Zacht}");   // BUG #429: wissel blijft staan = kans op kortsluiting
        }
        res.Insert(0, $"{alle.Count - res.Count} van {alle.Count} overgangen te plannen vanaf melder {_k.LocMelder}");
        return res;
    }

    /// <summary>WAT: de wisselstand voor een bekende stap NA de doelmelder, die alvast gezet wordt voordat de loc daar aankomt.
    /// WAAROM: staat de loc eenmaal op het doel (bijv. op de kruiswissel 24), dan kunnen de wissels daar niet meer veilig om. Eerst een stap zonder twijfel,
    /// anders (BUG #429) de vaakst gereden stap met een 'terugval'-eis (stand van een stap die wél doorreed).</summary>
    private Dictionary<int, bool>? VervolgEis429(Toestand doel, Func<int, bool>? nogOpen = null)
    {
        var opl = _k.ProefOplossingenBij(doel.M, doel.H);
        return _k.Mogelijkheden(doel.Via, doel.M, doel.H).Where(m => m.Uitkomst > 0)
            .OrderBy(m => nogOpen is not null && !nogOpen(m.Uitkomst))   // eerst een vervolgstap die de controle nog moet rijden
            .ThenBy(m => m.Terugval && NietPassend429(m.Eis, opl) > 0).ThenBy(m => NietPassend429(m.Eis, opl)).ThenByDescending(m => m.Aantal).Select(m => m.Eis).FirstOrDefault();
    }

    /// <summary>WAT: zorgt dat de loc netjes op één melder staat (kortsluiting opheffen, parkeren, zo nodig zoeken), zoals aan het begin van de verkenningslus.
    /// Geeft false als de loc kwijt is.</summary>
    private async Task<bool> LocNetjes412()
    {
        _monitor.Bijwerken();
        if (Kortsluiting()) { await HerstelKortsluiting(new Dictionary<int, bool>(), _k.LocRichting); _monitor.Bijwerken(); }
        if (_monitor.Bezet.Count > 1)
        {
            try { await Herparkeer(_k.LocRichting.Om(), alleBlokken: true); } catch (PlanFout) { }
            _monitor.Bijwerken();
        }
        if (_monitor.Bezet.Count == 0)
        {
            var gevonden = await ZoekLoc396();
            if (gevonden.Count == 0) return false;
            _monitor.Bijwerken();
        }
        if (_monitor.Bezet.Count == 1 && _monitor.Bezet.First() != _k.LocMelder) { _k.LocMelder = _monitor.Bezet.First(); _k.LocVia = 0; }
        return _monitor.Bezet.Count >= 1;
    }
}
