namespace Baanverkenner.Kern;

/// <summary>
/// BUG #172 (Marco 16-10 20:23, na 200 testversies): werkwijze "Stap voor stap". In plaats van elke opdracht vanaf de startmelder te beginnen
/// (en na afloop weer helemaal terug te rijden, soms een hele ronde met kortsluitingen op de kruiswissel), blijft de loc waar hij is en
/// werkt de verkenner de open plekken van dichtbij naar ver weg af:
///   1. Staat de loc op melder X, rijrichting D, dan rijdt hij één melder verder (twee als daarna de kruiswissel volgt) en weer terug.
///   2. Daarna worden de wissels één voor één omgezet en wordt dezelfde korte rit herhaald. Komt er een andere melder, dan ligt die wissel
///      daar en de nieuwe tak is een open plek; geeft het kortsluiting, dan wordt dat vastgelegd (en later opgelost).
///   3. De volgende open plek is de dichtstbijzijnde (gemeten in melders over wat al gereden is).
///   4. De kruiswissel krijgt een eigen proef: vanaf elke ingang met elk van de standen van de kruismotoren.
/// Alles wat de gewone werkwijze kan (kortsluitingen herstellen, partnerwissels, Dinamo-uitval, voortgang bewaren) werkt hier ook.
/// </summary>
public partial class BaanVerkenner
{
    private bool Stap => _ins.StapVoorStap;

    /// <summary>De melder waar de loc volgens de laatste meting staat (de enige bezette melder, anders de laatst bekende die nog bezet is).</summary>
    private int? _stapPlek;
    /// <summary>Voorkomt dat NaarStartVan zichzelf opnieuw als stap-navigatie aanroept.</summary>
    private bool _stapNavigeert;

    internal int? HuidigePlek()
    {
        _monitor.Bijwerken();
        var b = _monitor.Bezet;
        if (b.Count == 1) return _stapPlek = b.First();
        if (b.Count > 1)
        {
            if (_stapPlek is int p && b.Contains(p)) return p;
            return _stapPlek = b.OrderBy(x => x).First();
        }
        return _stapPlek;
    }

    // ------------------------------------------------------------------
    // De kaart als graaf
    // ------------------------------------------------------------------

    /// <summary>De buren van elke melder over alle overgangen die ooit gereden zijn (een overgang geldt ook de andere kant op).</summary>
    internal static Dictionary<int, HashSet<int>> Buren(Baankaart k)
    {
        var res = new Dictionary<int, HashSet<int>>();
        void Voeg(int a, int b)
        {
            if (!res.TryGetValue(a, out var s)) res[a] = s = new HashSet<int>();
            s.Add(b);
        }
        foreach (var o in k.Overgangen.Where(o => o.AantalKeer > 0 || o.GezienBijConfiguraties.Count > 0))
        {
            Voeg(o.Van, o.Naar);
            Voeg(o.Naar, o.Van);
        }
        return res;
    }

    /// <summary>Aantal stappen van <paramref name="van"/> naar elke bereikbare melder.</summary>
    internal static Dictionary<int, int> Afstanden(Baankaart k, int van)
    {
        var buren = Buren(k);
        var res = new Dictionary<int, int> { [van] = 0 };
        var rij = new Queue<int>(); rij.Enqueue(van);
        while (rij.Count > 0)
        {
            int m = rij.Dequeue();
            if (!buren.TryGetValue(m, out var bm)) continue;
            foreach (var n in bm.OrderBy(x => x))
                if (!res.ContainsKey(n)) { res[n] = res[m] + 1; rij.Enqueue(n); }
        }
        return res;
    }

    /// <summary>De volgende opdracht: de dichtstbijzijnde (in stappen vanaf de plek van de loc); bij gelijke afstand de oudste. Is de plek van de loc of de weg
    /// onbekend, dan gewoon de oudste. Een opdracht met meer pogingen komt later.</summary>
    internal static Opdracht KiesVolgendeOpdracht(Baankaart k, int? plek, IReadOnlyList<Opdracht> wachtrij)
    {
        if (plek is not int p) return wachtrij[0];
        var afstand = Afstanden(k, p);
        Opdracht beste = wachtrij[0];
        long besteScore = long.MaxValue;
        for (int i = 0; i < wachtrij.Count; i++)
        {
            var o = wachtrij[i];
            long d = afstand.TryGetValue(o.Start, out var dd) ? dd : 1000;
            long score = d * 1000 + o.Pogingen * 100000 + i;
            if (score < besteScore) { beste = o; besteScore = score; }
        }
        return beste;
    }

    // ------------------------------------------------------------------
    // Navigatie zonder omweg langs de startmelder
    // ------------------------------------------------------------------

    /// <summary>De melder waar een route eindigt (leeg = de startmelder van de verkenning).</summary>
    private int RouteDoel(List<Etappe> route) => Stap && _stapDoel is int d ? d : route.Count > 0 ? route[^1].Naar : _kaart.StartMelder;

    /// <summary>De melder waar de lopende opdracht (of het kortsluitpunt) de loc wil hebben.</summary>
    private int? _stapDoel;

    internal static List<Etappe> WegNaarEtappes(List<(int Van, int Naar, Richting Richting, Configuratie Cfg)> weg)
    {
        var res = new List<Etappe>();
        foreach (var s in weg)
        {
            if (res.Count > 0 && res[^1].Richting == s.Richting && res[^1].Configuratie.Equals(s.Cfg) && res[^1].Pad[^1] == s.Van)
            {
                res[^1].Pad.Add(s.Naar); res[^1].Naar = s.Naar;
            }
            else res.Add(new Etappe { Configuratie = s.Cfg, Richting = s.Richting, Van = s.Van, Naar = s.Naar, Pad = new List<int> { s.Van, s.Naar } });
        }
        return res;
    }

    /// <summary>Kortste weg (in melders) van <paramref name="van"/> naar <paramref name="naar"/> over eerder gereden overgangen, elke stap met de kleinste wisselstand
    /// waarmee hij gereden is. Door een kruiswissel kan de loc alleen van een ingang naar een uitgang waarvan die doorgang (ingang → kruiswissel → uitgang) gereden is,
    /// met de motorstand die daarbij hoort (14 → 24 → 21 met 13 rechtdoor, 8 → 24 → 14 met 13 afbuigend). Null als er geen weg is.</summary>
    internal static List<(int Van, int Naar, Richting Richting, Configuratie Cfg)>? StapWeg(Baankaart k, KruisKennis kr, int van, int naar)
    {
        if (van == naar) return new();
        var rand = new Dictionary<(int, int), (Richting R, Configuratie C)>();
        void Voeg(int a, int b, Richting r, Configuratie c)
        {
            if (!rand.TryGetValue((a, b), out var oud) || c.Afbuigend.Length < oud.C.Afbuigend.Length) rand[(a, b)] = (r, c);
        }
        foreach (var o in k.Overgangen.Where(o => o.GezienBijConfiguraties.Count > 0))
        {
            var c = o.GezienBijConfiguraties.Select(Configuratie.VanSleutel).OrderBy(x => x.Afbuigend.Length).ThenBy(x => x.Sleutel, StringComparer.Ordinal).First();
            Voeg(o.Van, o.Naar, o.Richting, c);
            Voeg(o.Naar, o.Van, o.Richting.Om(), c);
        }
        var uit = rand.Keys.GroupBy(x => x.Item1).ToDictionary(g => g.Key, g => g.Select(x => x.Item2).OrderBy(x => x).ToList());
        var vorige = new Dictionary<(int Cur, int Prev), (int Cur, int Prev)>();
        var start = (van, 0);
        var rij = new Queue<(int Cur, int Prev)>(); rij.Enqueue(start);
        var gezien = new HashSet<(int, int)> { start };
        (int Cur, int Prev)? einde = null;
        while (rij.Count > 0 && einde is null)
        {
            var (cur, prev) = rij.Dequeue();
            if (!uit.TryGetValue(cur, out var buren)) continue;
            foreach (var n in buren)
            {
                if (n == prev) continue;
                if (prev != 0 && kr.Middens.Contains(cur) && !k.Doorgangen.Any(d => d.Via == prev && d.Melder == cur && d.Volgende == n)) continue;
                var st = (n, cur);
                if (!gezien.Add(st)) continue;
                vorige[st] = (cur, prev);
                if (n == naar) { einde = st; break; }
                rij.Enqueue(st);
            }
        }
        if (einde is null) return null;
        var knopen = new List<int>();
        var s = einde.Value;
        while (true)
        {
            knopen.Add(s.Cur);
            if (s.Cur == van && s.Prev == 0) break;
            s = vorige[s];
        }
        knopen.Reverse();
        var weg = new List<(int Van, int Naar, Richting Richting, Configuratie Cfg)>();
        for (int i = 0; i + 1 < knopen.Count; i++)
        {
            var (r, c) = rand[(knopen[i], knopen[i + 1])];
            weg.Add((knopen[i], knopen[i + 1], r, c));
        }
        // De motorstand van elke doorgang door een kruiswissel: zowel op de stap erheen als op de stap eruit.
        for (int i = 1; i + 1 < knopen.Count; i++)
        {
            if (!kr.Middens.Contains(knopen[i])) continue;
            var gezien2 = KruisBewijs.GezienBijIngang(k, kr, knopen[i], knopen[i - 1], knopen[i + 1]);
            if (gezien2.Count == 0) continue;
            var stand = gezien2.OrderBy(g => g.Afbuigend.Length).ThenBy(g => g.Sleutel, StringComparer.Ordinal).First();
            foreach (int j in new[] { i - 1, i })
            {
                var w = weg[j];
                var c = KruisBewijs.ZonderMotoren(w.Cfg, kr.Motoren);
                foreach (var m in kr.Motoren.Where(stand.Bevat)) c = c.Met(m);
                weg[j] = (w.Van, w.Naar, w.Richting, c);
            }
        }
        return weg;
    }

    /// <summary>De loc rijdt van zijn huidige plek naar <paramref name="doel"/> over wat al gereden is (kortste weg in melders).</summary>
    private async Task NaarPlekMelder(int doel, Configuratie? rijstand)
    {
        var van = HuidigePlek();
        if (van is null || van == doel) return;
        if (Kruis.OpKruis(_monitor.Bezet)) { await VerlaatKruiswissel(doel); van = HuidigePlek(); if (van is null || van == doel) return; }
        var weg = StapWeg(_kaart, Kruis, van.Value, doel);
        if (weg is null)
        {
            _log.Waarschuwing($"Er is geen bekende weg van melder {van} naar melder {doel}: de gebruiker wordt gevraagd de loc op melder {doel} te zetten.");
            await VraagLocTerugTeZetten(doel, $"De verkenner weet niet hoe hij van melder {van} naar melder {doel} komt.");
            _stapPlek = doel;
            return;
        }
        _log.Info($"Naar melder {doel}: {string.Join(" → ", new[] { van.Value }.Concat(weg.Select(s => s.Naar)))}.");
        _stapNavigeert = true;
        try { await NaarStartVan(WegNaarEtappes(weg), rijstand); }
        finally { _stapNavigeert = false; }
        _stapPlek = doel;
    }

    // ------------------------------------------------------------------
    // Wisselprobleem: info daarna negeren, terug naar een getest stuk baan (BUG #173)
    // ------------------------------------------------------------------

    /// <summary>Een melder waar de loc veilig kan staan: geen kruiswissel, geen bekende wissel erbij, geen kortsluitpunt, en eerder gereden met alle wissels rechtdoor.</summary>
    internal static bool IsVeiligeMelder(Baankaart k, KruisKennis kr, int m)
    {
        if (kr.Middens.Contains(m)) return false;
        if (WisselsBijBezet(k, new[] { m }).Count > 0) return false;
        if (k.Kortsluitpunten.Any(p => p.NaMelder == m || p.TweedeMelders.Contains(m))) return false;
        return k.Overgangen.Any(o => o.AantalKeer > 0 && (o.Van == m || o.Naar == m) && o.GezienBijConfiguraties.Contains(""));
    }

    /// <summary>De dichtstbijzijnde veilige melder, bereikbaar over geteste overgangen zonder afbuigende wissels (anders null). Staat de loc er al, dan die melder.</summary>
    internal static int? KiesVeiligePlek(Baankaart k, KruisKennis kr, int nu)
    {
        if (IsVeiligeMelder(k, kr, nu)) return nu;
        int? best = null; int bestLen = int.MaxValue;
        foreach (var m in k.Melders.Select(x => x.Nummer).OrderBy(x => x))
        {
            if (!IsVeiligeMelder(k, kr, m)) continue;
            var weg = StapWeg(k, kr, nu, m);
            if (weg is null || weg.Any(s => s.Cfg.Afbuigend.Except(kr.Motoren).Any())) continue;
            if (weg.Count < bestLen) { best = m; bestLen = weg.Count; }
        }
        return best;
    }

    /// <summary>Marco (17-10): "wissel probleem start, alle info hierna negeren en direct terug rijden naar een getest stuk baan, en vanuit daar weer verder gaan."
    /// De opdracht die liep gaat terug in de wachtrij; wat sinds het begin van het probleem gepland of als getest genoteerd is, vervalt (de kaart is al teruggezet);
    /// de loc rijdt naar de dichtstbijzijnde veilige melder en de opdracht begint daar opnieuw. Nog niet getest op de baan.</summary>
    private async Task HerstelNaWisselProbleem(Opdracht o, string reden)
    {
        _log.Waarschuwing($"WISSELPROBLEEM ({reden}). Alles wat sinds het begin van het probleem gezien is, wordt genegeerd. De loc rijdt eerst terug naar een getest stuk baan; opdracht #{o.Id} begint daarna opnieuw.");
        int grens = _uitvalIdGrens, getest = _uitvalGetest;
        _uitvalIdGrens = int.MaxValue; _uitvalGetest = -1;
        int weg = _status.Wachtrij.RemoveAll(x => x.Id >= grens && x.Id != o.Id);
        if (weg > 0) _log.Info($"{weg} opdracht(en) die na het begin van het probleem gepland waren, vervallen.");
        if (getest >= 0 && _status.GetesteOvergangen.Count > getest) _status.GetesteOvergangen.RemoveRange(getest, _status.GetesteOvergangen.Count - getest);
        o.UitvalHersteld++;
        if (o.UitvalHersteld > 3)
        {
            var t = $"Opdracht #{o.Id} is {o.UitvalHersteld} keer afgebroken door een wisselprobleem en wordt overgeslagen.";
            _kaart.Waarschuwingen.Add(t); _log.Waarschuwing(t);
            _status.AfgerondeOpdrachten++;
        }
        else _status.Wachtrij.Insert(0, o);
        _status.Huidig = null;
        BewaarNu();
        // Terug naar het geteste stuk: eerst alle wissels rechtdoor (behalve die onder de loc), dan over geteste overgangen naar de veilige melder.
        try
        {
            await VolledigeStop();
            await ZetConfiguratie(Configuratie.Basis, spaarOnderLoc: true);
            var nu = HuidigePlek();
            int? veilig = nu is int n ? KiesVeiligePlek(_kaart, Kruis, n) : null;
            if (veilig is int v)
            {
                if (nu != v || _monitor.Bezet.Count != 1)
                {
                    _log.Info($"Terug naar het geteste stuk: melder {v} (de loc staat nu op melder {nu}).");
                    if (nu != v) await NaarPlekMelder(v, null);
                    await Nastellen(v, o.Richting);
                }
                _stapPlek = v;
                _log.Info($"De loc staat op melder {v}: een getest stuk baan. Hier gaat het verder.");
            }
            else
            {
                await VraagLocTerugTeZetten(_kaart.StartMelder, "Na het wisselprobleem is er geen geteste plek bereikbaar vanaf waar de loc staat. Zet de loc op de startmelder.");
                _stapPlek = _kaart.StartMelder;
            }
        }
        catch (NavigatieFout nf)
        {
            _log.Waarschuwing($"Terugrijden naar het geteste stuk lukte niet: {nf.Message}");
            await VraagLocTerugTeZetten(_kaart.StartMelder, "Na het wisselprobleem kon de loc niet zelf terug naar een getest stuk baan. Zet de loc op de startmelder.");
            _stapPlek = _kaart.StartMelder;
        }
    }

    /// <summary>De loc staat (deels) op de kruiswissel, bijvoorbeeld na een proef met een stand zonder doorgaand spoor. De motoren kunnen dan niet omgezet worden
    /// (kortsluiting). Hij rijdt eerst de kruiswissel uit met de motorstand die nu staat: een eerder gereden overgang vanaf deze plek waarvan de motorstand overeenkomt.
    /// Nog niet getest op de baan.</summary>
    private async Task VerlaatKruiswissel(int doel)
    {
        var van = HuidigePlek();
        if (van is null) return;
        var nu = WerkelijkeStand(Configuratie.Basis);
        var motorNu = Kruis.Motoren.Where(nu.Bevat).OrderBy(x => x).ToList();
        foreach (var og in _kaart.Overgangen.Where(x => x.Van == van.Value && x.AantalKeer > 0 && !Kruis.Middens.Contains(x.Naar)).OrderBy(x => x.Naar))
        {
            foreach (var sleutel in og.GezienBijConfiguraties)
            {
                var c = Configuratie.VanSleutel(sleutel);
                if (!Kruis.Motoren.Where(c.Bevat).OrderBy(x => x).SequenceEqual(motorNu)) continue;
                _log.Info($"De loc staat op de kruiswissel (melder {van}) en de motoren kunnen niet om: hij rijdt eerst uit naar melder {og.Naar} met de motorstand die nu staat.");
                var rit = await Rit(van.Value, og.Richting, new RitDoel { Configuratie = c, MaxStappen = 1, GeenKruisVooraf = true });
                if (rit.Einde == RitEinde.Kortsluiting) return;
                await Nastellen(og.Naar, og.Richting, achter: van);
                _stapPlek = og.Naar;
                return;
            }
        }
    }

    /// <summary>Na een mislukte navigatie of rit: rijdt de loc zelf naar <paramref name="doel"/> als hij ergens op een bekende melder staat en er geen alarm is.</summary>
    private async Task<bool> ProbeerZelfNaarMelder(int doel)
    {
        var nu = HuidigePlek();
        if (nu is null) return false;
        if (nu == doel && _monitor.Bezet.Count == 1) return true;
        if (HuidigeAlarmBlokken().Count > 0) return false;
        if (StapWeg(_kaart, Kruis, nu.Value, doel) is null) return false;
        try
        {
            _log.Info($"De loc staat op melder {nu}; de verkenner rijdt zelf naar melder {doel}.");
            await NaarPlekMelder(doel, null);
        }
        catch (NavigatieFout nf)
        {
            _log.Waarschuwing($"Zelf terugrijden naar melder {doel} lukte niet: {nf.Message}");
            return false;
        }
        var b = _monitor.Bezet;
        return b.Count == 1 && b.Contains(doel);
    }

    // ------------------------------------------------------------------
    // Het vervolg van een rit
    // ------------------------------------------------------------------

    /// <summary>Na een korte rit begint op de laatste melder de volgende open plek (zelfde richting, zelfde wissels).</summary>
    private void PlanVervolg(Opdracht o, RitResultaat basis)
    {
        if (basis.Einde != RitEinde.StapKlaar || basis.Reeks.Count < 2) return;
        var laatste = basis.Reeks[^1];
        var cfg = o.KruisProef ? KruisBewijs.ZonderMotoren(o.Configuratie, Kruis.Motoren) : o.Configuratie;
        Plan(new Opdracht
        {
            Configuratie = cfg,
            Start = laatste,
            Richting = o.Richting,
            Route = RouteMet(o.Route, o.Configuratie, o.Richting, basis.Reeks),
            Reden = $"verder na melder {basis.Reeks[^2]}"
        });
    }

    /// <summary>Na elke opdracht: is er een kruiswissel bij gekomen, dan krijgt elke ingang daarvan zijn proeven.</summary>
    private void NaOpdrachtStap()
    {
        DetecteerKruis();
        PlanKruisProeven();
    }

    // ------------------------------------------------------------------
    // Kruiswissel
    // ------------------------------------------------------------------

    /// <summary>Een melder met vier verschillende buren is een kruiswissel (als de gebruiker Engelse wissels opgaf).</summary>
    private void DetecteerKruis()
    {
        if (Kruis.Motoren.Count == 0 || Kruis.Middens.Count > 0) return;   // één motorpaar = één kruiswissel; een melder met twee gewone wissels erachter (129) heeft ook vier buren
        var buren = Buren(_kaart);
        foreach (var (m, bm) in buren)
            if (bm.Count >= 4 && !Kruis.Middens.Contains(m))
            {
                _log.Info($"Melder {m} heeft {bm.Count} buren ({string.Join(", ", bm.OrderBy(x => x))}): dit is de kruiswissel van motor {Kruis.MotorenTekst}.");
                LeerKruisMidden(m);
            }
    }

    /// <summary>Voor elke bekende kruiswissel, elke ingang (buur) en elke stand van één kruismotor die nog niet gereden is: een eigen proef van twee melders
    /// (ingang → kruiswissel → uitgang). Zo komt er per ingang uit wat elke motor doet, zonder motoren onder de loc om te zetten.</summary>
    private void PlanKruisProeven()
    {
        if (Kruis.Middens.Count == 0 || Kruis.Motoren.Count == 0) return;
        var buren = Buren(_kaart);
        var motoren = Kruis.Motoren.OrderBy(x => x).ToList();
        var standen = new List<Configuratie> { Configuratie.Basis };
        foreach (var m in motoren) standen.Add(new Configuratie(new[] { m }));
        foreach (var k in Kruis.Middens.OrderBy(x => x))
        {
            if (!buren.TryGetValue(k, out var bk)) continue;
            foreach (var ingang in bk.OrderBy(x => x))
            {
                var r = _kaart.Overgangen.Where(o => o.Van == ingang && o.Naar == k).Select(o => (Richting?)o.Richting).FirstOrDefault()
                        ?? _kaart.Overgangen.Where(o => o.Van == k && o.Naar == ingang).Select(o => (Richting?)o.Richting.Om()).FirstOrDefault();
                if (r is null) continue;
                var gezien = KruisBewijs.GezienBijIngang(_kaart, Kruis, k, ingang);
                foreach (var s in standen)
                {
                    if (gezien.Any(g => g.Equals(s))) continue;
                    Plan(new Opdracht
                    {
                        Configuratie = s,
                        Start = ingang,
                        Richting = r.Value,
                        KruisProef = true,
                        Reden = $"kruiswissel (melder {k}): ingang {ingang} met {KruisBewijs.StandTekst(motoren, s)}"
                    });
                }
            }
        }
    }

    /// <summary>De proef van een kruiswissel-ingang: twee melders rijden met precies de gevraagde motorstanden, vastleggen, terug.</summary>
    private async Task VoerKruisProefUit(Opdracht o)
    {
        _bezig = $"kruiswissel-proef vanaf melder {o.Start}";
        MeldVoortgang();
        await NaarPlekMelder(o.Start, o.Configuratie);
        await Nastellen(o.Start, o.Richting);   // helemaal op de ingang: een loc die nog half in de kruiswissel staat, krijgt kortsluiting van de motoren
        await ZetConfiguratie(o.Configuratie);
        var rit = await Rit(o.Start, o.Richting, new RitDoel { Configuratie = o.Configuratie, MaxStappen = 2, StaartWissels = true, GeenKruisVooraf = true });
        _log.Info($"Kruiswissel-proef: {Lijst(rit.Reeks)} - einde: {EindeTekst(rit.Einde)}.");
        VerwerkEinde(o, rit);
        PlanVervolg(o, rit);
        if (rit.Einde != RitEinde.Kortsluiting)
        {
            _status.GetesteOvergangen.AddRange(Overgangen(rit.Reeks, o.Richting).Where(s => !_status.GetesteOvergangen.Contains(s)));
            // De terugweg door de kruiswissel heeft zijn eigen motorstand: de stand van de proef kan de loc bij een andere uitgang brengen
            // (motor 10 afbuigend stuurt 21 naar 129 in plaats van naar 14). Daarom de kleinste bewezen stand voor dezelfde doorgang.
            // De loc staat na de rit nog half in de kruiswissel-melder; de motoren omzetten kan pas als hij helemaal op de uitgang staat.
            if (rit.Reeks.Count >= 2) await Nastellen(rit.Reeks[^1], rit.Richting, achter: rit.Reeks[^2]);
            var terugCfg = o.Configuratie;
            if (rit.Reeks.Count == 3 && Kruis.Middens.Contains(rit.Reeks[1]))
            {
                var bewezen = KruisBewijs.GezienBijIngang(_kaart, Kruis, rit.Reeks[1], rit.Reeks[0], rit.Reeks[2]);
                if (bewezen.Count > 0)
                {
                    var klein = bewezen.OrderBy(g => g.Afbuigend.Length).ThenBy(g => g.Sleutel, StringComparer.Ordinal).First();
                    terugCfg = KruisBewijs.ZonderMotoren(o.Configuratie, Kruis.Motoren);
                    foreach (var m in Kruis.Motoren.Where(klein.Bevat)) terugCfg = terugCfg.Met(m);
                }
            }
            await TerugNaarMetControle(o, rit, terugCfg);
        }
    }

    // ------------------------------------------------------------------
    // Afronden: lange trajecten voor het rapport
    // ------------------------------------------------------------------

    /// <summary>In deze werkwijze zijn de ritten één melder lang; het rapport en het blokkenschema willen trajecten. Ze worden hier uit de overgangen gebouwd:
    /// vanaf de startmelder in beide richtingen, en vanaf elke gevonden splitsing (afbuigende tak) tot een lus of een doodlopend einde.</summary>
    private void BouwTrajecten()
    {
        _kaart.Trajecten.Clear();
        var starts = new List<(int Melder, Richting R, Configuratie C)>
        {
            (_kaart.StartMelder, Richting.Vooruit, Configuratie.Basis),
            (_kaart.StartMelder, Richting.Achteruit, Configuratie.Basis)
        };
        foreach (var w in _kaart.Wissels)
            foreach (var s in w.Waarnemingen.Where(x => x.Soort == WaarnemingSoort.Splitsing && x.VolgendeBijAfbuigend is not null))
                starts.Add((s.NaMelder, s.Richting, Configuratie.Basis.Met(w.Adres)));
        foreach (var (m0, r, c) in starts)
        {
            var reeks = new List<int> { m0 };
            var einde = RitEinde.Doodlopend;
            int huidig = m0;
            while (true)
            {
                var volg = _kaart.BekendeVolgende(huidig, r, c).Where(x => x != huidig).ToList();
                if (volg.Count == 0 && c.Afbuigend.Length > 0) volg = _kaart.BekendeVolgende(huidig, r).Where(x => x != huidig).ToList();
                if (volg.Count == 0) break;
                int n = volg.FirstOrDefault(x => !reeks.Contains(x));
                if (n == 0) { einde = RitEinde.Lus; break; }
                reeks.Add(n); huidig = n;
                if (reeks.Count > _ins.MaxMeldersPerRit) { einde = RitEinde.Maximum; break; }
            }
            if (reeks.Count >= 2) _kaart.Trajecten.Add(new Traject { Configuratie = c, Start = m0, Richting = r, Reeks = reeks, Einde = einde });
        }
    }
}
