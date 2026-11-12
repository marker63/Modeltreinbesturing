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
            var (doelO, doel) = bereikbaar[0];
            int kortNu = _k.Kortsluitingen;
            bool ok;
            // De wissels voorbij de doelmelder alvast goed zetten voor een bekende vervolgstap: na aankomst kruipt de loc nog een stukje door (netjes op één
            // melder), en met een verkeerd staande wissel daarachter gaf dat in de simulatie kortsluiting (10 - 129 - 24 vooruit, wissel 14 van achteren).
            var verder = _k.Mogelijkheden(doel.Via, doel.M, doel.H).Where(m => m.Uitkomst > 0 && !m.Terugval).OrderByDescending(m => m.Aantal).Select(m => m.Eis).FirstOrDefault();
            if (Environment.GetEnvironmentVariable("MARCO2_DEBUG") == "1") _log.Info($"DEBUG controle verder na {doel.M} (via {doel.Via}, {doel.H}): {(verder is null ? "geen" : string.Join(" ", verder.Select(e => $"{e.Key}{(e.Value ? "A" : "R")}")))}; alle: {string.Join(" | ", _k.Mogelijkheden(doel.Via, doel.M, doel.H).Select(m => $"{m.Uitkomst}:{string.Join("", m.Eis.Select(e => $"{e.Key}{(e.Value ? "A" : "R")}"))}{(m.Terugval ? "t" : "")}x{m.Aantal}"))}");
            var plan = PlanNaar(kaart, doel, verder);
            if (plan is null && verder is not null) plan = PlanNaar(kaart, doel);
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
        int gereden = alle.Count(o => _gereden412.Contains(o));
        var nietGereden = alle.Where(o => !_gereden412.Contains(o)).Select(o => $"{o.Van}->{o.Naar} ({o.R.Tekst()}){(reden.TryGetValue(o, out var rd) ? $" [{rd}]" : "")}").ToList();
        int kortsluitingen = _k.Kortsluitingen - kortVoor;
        bool goed = nietGereden.Count == 0 && kortsluitingen == 0 && fout.Count == 0;
        string samenvatting = $"{(tweede ? "Tweede controle" : "Controle")} (module 2): {gereden} van {alle.Count} overgangen gereden, {kortsluitingen} kortsluiting(en), {_k.Ritten - rittenVoor} ritten.";
        var verslag = new ControleVerslag { Datum = _klok.Nu, Samenvatting = samenvatting, Verschillen = !goed };
        verslag.Regels.AddRange(fout);
        if (nietGereden.Count > 0) verslag.Regels.Add($"Niet gereden: {string.Join(", ", nietGereden)}.");
        if (goed) verslag.Regels.Add("Alles gereden zonder kortsluiting: de kaart klopt.");
        _status.Kaart.ControleVerslagen.Add(verslag);
        _log.Vondst(samenvatting);
        foreach (var r in verslag.Regels) (goed ? (Action<string>)_log.Info : _log.Waarschuwing)(r);
        _status.Controle = false;
        _gereden412 = null;
        _fase = "Klaar";
        return goed;
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
