using Modeltreinbesturing.Hardware;

namespace Baanverkenner.Kern;

/// <summary>
/// BAANVERKENNER MODULE 2 (Marco 27-10: "een compleet nieuwe tweede versie ... zodat ik kan kiezen welke ik gebruik").
///
/// WAT: verkent de baan opnieuw van de grond af, volgens het stappenplan van 27-10:
///   1. Basis: Dinamo-wachtrij leeg, één puls per wisselcommando, wachten op de terugmelding, nooit een wissel omzetten bij de loc.
///   2. Geraamte: met alle wissels in de basisstand rijden tot de lus rond is (melders, volgorde, richting).
///   3. Wissels zoeken: alle nog niet gevonden adressen tegelijk omzetten en een bekend stuk rijden; gebeurt er iets anders, dan wordt
///      met halveren (de helft van de adressen terug) uitgezocht welk adres het was. Daarna heen en weer om de andere stand te rijden.
///   4. Standen per overgang: elke stap wordt met de standen van de wissels in die knoop vastgelegd, in beide richtingen.
///   5. Doodlopende einden en uitwegen: geen nieuwe melder = doodlopend; een stap die alleen kortsluiting geeft wordt met andere
///      wisselstanden opnieuw geprobeerd.
///   6. Blokken: bij elke parkeerplek een kort rijcommando per Dinamo-blok (buurblokken eerst).
///   7. Volledig: alle melders, beide kanten, alle adressen getest, alle blokken; daarna de baankaart (zelfde formaat als module 1).
///
/// WAAROM zo: module 1 groeide in 357 bugfixes uit tot een verzameling uitzonderingen. Module 2 heeft één regel voor alles:
/// "rij alleen wat al eens gelukt is; iets nieuws is altijd een bewuste proef, en na een proef rijdt de loc langs dezelfde weg terug
/// voordat er een wissel terug gaat". Kennis staat op één plek (<see cref="Kennis2"/>), de baankaart wordt daaruit gemaakt.
///
/// Nog niet getest op de baan; wel volledig in de simulatie met Marco's echte aansluiting (overloopsets 5+6 en 2+1, wissel 6 omgekeerd, keerlus).
/// </summary>
public partial class BaanVerkenner2 : IBaanVerkenner
{
    private readonly IHardwareInterface _hw;
    private readonly VerkenInstellingen _ins;
    private readonly IKlok _klok;
    private readonly VerkenLog _log;
    private readonly Func<string, string, Task<bool>> _vraag;
    private readonly VerkenStatus _status;
    private Kennis2 _k;
    private readonly MelderMonitor _monitor;
    private readonly bool _hervat;
    private CancellationToken _token;
    private volatile bool _gepauzeerd;
    private string _fase = "Voorbereiden";
    private string _bezig = "";

    /// <summary>Overloopsets uit de instellingen (meester, volger): de volger schakelt altijd mee naar dezelfde stand op de baan.</summary>
    private readonly List<(int Meester, int Volger)> _paren;
    private readonly HashSet<int> _omgekeerd;
    /// <summary>De adressen die module 2 zelf omzet en test: alle wisseladressen behalve de volgers van een overloopset.</summary>
    private readonly List<int> _adressen;

    public event Action<VerkenVoortgang>? VoortgangGewijzigd;
    public Action<VerkenStatus>? Bewaren { get; set; }
    public KoploperReferentie? Referentie { get; set; }
    public VerkenLog Log => _log;
    public Baankaart Kaart => _status.Kaart;
    public VerkenStatus Status => _status;
    public Kennis2 Kennis => _k;
    public bool Gepauzeerd => _gepauzeerd;
    public IReadOnlyCollection<int> BezetteMelders
    {
        get { try { return _monitor.Bezet; } catch { return Array.Empty<int>(); } }
    }

    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan DinamoCyclus = TimeSpan.FromMilliseconds(200);
    /// <summary>Marco 25-10 (BUG #305): eerst 10 minuten zelf proberen, pas daarna iets aan Marco vragen.</summary>
    private static readonly TimeSpan ZelfZoekenMaximaal = TimeSpan.FromMinutes(10);
    private const int MaxTaken = 900;

    public BaanVerkenner2(IHardwareInterface hw, VerkenInstellingen ins, IKlok klok, VerkenLog log,
        Func<string, string, Task<bool>> vraag, VerkenStatus? hervatVan = null)
    {
        _hw = hw; _ins = ins; _klok = klok; _log = log; _vraag = vraag;
        _status = hervatVan ?? new VerkenStatus { Instellingen = ins };
        _status.Instellingen = ins;
        _hervat = hervatVan?.Module2 is not null;
        _k = hervatVan?.Module2 ?? new Kennis2();
        _status.Module = 2;
        _status.Module2 = _k;
        _paren = ins.GekoppeldeParen();
        _omgekeerd = ins.OmgekeerdeWissels();
        _log.OmgekeerdeWissels = _omgekeerd;
        _adressen = ins.WisselAdressen().Where(a => !_paren.Any(p => p.Volger == a)).ToList();
        // Zelfde melderbewaking als module 1: ontdenderen, flikkerende en vastzittende melders herkennen (BUG #262/#306).
        _monitor = new MelderMonitor(klok, () => _ins.OntdenderMs, m => _k.Melders.Contains(m));
    }

    // ================================================================ bediening vanuit het venster

    public void Pauzeer() { _gepauzeerd = true; _log.Info("Pauze gevraagd - de loc stopt."); MeldVoortgang(); }
    public void Hervat() { _gepauzeerd = false; _log.Info("Verkenning hervat."); MeldVoortgang(); }

    /// <summary>WAT: noodstop vanuit het venster: Dinamo-noodstop en snelheid 0 naar alle blokken. WAAROM: direct, zonder op de verkenner te wachten.</summary>
    public void Noodstop()
    {
        try
        {
            _hw.Noodstop();
            foreach (var b in AlleBlokken()) _hw.ZetLocSnelheidUrgent(_ins.LocAdres, 0, true, b, _ins.LocStappen);
        }
        catch { /* noodstop mag nooit zelf een fout geven */ }
    }

    public void PasSnelhedenAan(int verkensnelheid, int kruipsnelheid)
    {
        _ins.Verkensnelheid = Math.Max(1, verkensnelheid);
        _ins.Kruipsnelheid = Math.Max(1, kruipsnelheid);
        _log.Info($"Snelheden aangepast: verkensnelheid {_ins.Verkensnelheid}, kruipsnelheid {_ins.Kruipsnelheid}.");
    }

    // ================================================================ hoofdlus

    /// <summary>WAT: de hele verkenning. WAAROM: steeds de nuttigste taak die het dichtst bij de loc ligt, tot er niets meer te leren is.</summary>
    public async Task VoerUit(CancellationToken token)
    {
        _token = token;
        _hw.BezetmeldingGewijzigd += _monitor.Ontvang;
        _hw.KortsluitingStatusGewijzigd += KortsluitStatus;
        try
        {
            await Voorbereiden();
            int taken = 0;
            int verplaatst = 0;
            while (taken++ < MaxTaken)
            {
                _token.ThrowIfCancellationRequested();
                await PauzeMoment();
                _onbereikbaar = 0;
                var taak = KiesTaak();
                if (taak is null)
                {
                    // Er zijn nog taken, maar vanaf hier lukt geen plan (bijv. geen veilige plek om een wissel om te zetten): een stukje bekende baan
                    // rijden en opnieuw proberen (hoogstens 6 keer achter elkaar).
                    if (_onbereikbaar > 0 && verplaatst++ < 6 && await RijdStukjeVerder()) continue;
                    break;
                }
                verplaatst = 0;
                _bezig = taak.Omschrijving;
                MeldVoortgang();
                _log.Stap($"Taak: {taak.Omschrijving}.");
                bool gelukt;
                try { gelukt = await VoerTaakUit(taak); }
                catch (PlanFout pf) { _log.Waarschuwing($"Taak '{taak.Omschrijving}' lukt nu niet: {pf.Message}"); gelukt = false; }
                if (!gelukt)
                {
                    int n = _k.Mislukt[taak.Sleutel] = _k.Mislukt.GetValueOrDefault(taak.Sleutel) + 1;
                    if (n >= 2 && !_k.Overgeslagen.Contains(taak.Sleutel))
                    {
                        _k.Overgeslagen.Add(taak.Sleutel);
                        _log.Waarschuwing($"Taak '{taak.Omschrijving}' is twee keer niet gelukt en wordt overgeslagen.");
                    }
                }
                BewaarNu();
            }
            Afronden();
            BewaarNu();
        }
        catch (OperationCanceledException)
        {
            try { NoodstopZonderWachten(); } catch { }
            _fase = "Gestopt";
            BewaarNu();
            throw;
        }
        finally
        {
            _hw.BezetmeldingGewijzigd -= _monitor.Ontvang;
            _hw.KortsluitingStatusGewijzigd -= KortsluitStatus;
        }
    }

    private async Task PauzeMoment()
    {
        if (!_gepauzeerd) return;
        await Stop();
        MeldVoortgang();
        while (_gepauzeerd) { _token.ThrowIfCancellationRequested(); await Wacht(TimeSpan.FromMilliseconds(200)); }
    }

    private Task Wacht(TimeSpan t) => _klok.Wacht(t, _token);

    // ================================================================ voorbereiden (fase 1 van het stappenplan)

    /// <summary>WAT: verbinding klaarzetten, Dinamo-wachtrij laten leeglopen, de plek van de loc bepalen en (bij een nieuwe verkenning) alle
    /// wissels in de basisstand zetten. WAAROM: BUG #352/#355: een oude wachtrij in Dinamo voerde nog minuten later wisselcommando's uit.</summary>
    private async Task Voorbereiden()
    {
        _fase = "Voorbereiden";
        var k = _status.Kaart;
        if (!_hervat)
        {
            _k.Gestart = _klok.Nu;
            k.Gestart = _klok.Nu;
            k.Programma = "Baanverkenner module 2";
        }
        k.Hardware = _hw.Naam; k.LocAdres = _ins.LocAdres; k.LocStappen = _ins.LocStappen; k.RefSnelheid = _ins.Verkensnelheid;
        k.WisselAdresVan = _ins.WisselAdresVan; k.WisselAdresTot = _ins.WisselAdresTot;
        k.OmgekeerdeWissels = _omgekeerd.OrderBy(a => a).ToList(); k.KruisMotoren = _ins.KruisMotoren;
        _log.Info($"Baanverkenner module 2 {(_hervat ? "hervat" : "gestart")}. Te testen adressen: {string.Join(", ", _adressen)}" +
                  (_paren.Count > 0 ? $"; overloopsets (schakelen mee): {string.Join(", ", _paren.Select(p => $"{p.Meester}+{p.Volger}"))}" : "") +
                  (_omgekeerd.Count > 0 ? $"; omgekeerd aangesloten: {string.Join(", ", _omgekeerd)}" : "") + ".");
        if (_hw is IWisselPuls puls)
        {
            // BUG #352: elk datagram is een aparte puls; één keer sturen is genoeg (Dinamo meldt terug als de puls gegeven is).
            puls.WisselHerhalingen = Math.Max(1, _ins.WisselHerhalingen);
            foreach (var m in KruisMotoren()) puls.ZetPulsTijd(m, _ins.KruisPulsTijd);
        }
        await WachtTotDinamoRustig();
        await BepaalPlek();
        if (!_hervat)
        {
            _log.Stap("Alle wissels in de basisstand zetten (op de baan rechtdoor; overloopsets samen).");
            foreach (var a in _adressen) await ZetAdres(a, BasisStand(a), forceer: true);
        }
        else
        {
            // Hervatten: dezelfde standen nog eens sturen. Een wissel die al zo staat beweegt daardoor niet (dus ook niet onder de loc).
            _log.Stap("Hervatten: de bewaarde wisselstanden opnieuw sturen (een wissel die al zo staat beweegt niet).");
            foreach (var a in _adressen) await ZetAdres(a, _k.Standen.TryGetValue(a, out var s) ? s : BasisStand(a), forceer: true);
            // Gestopt midden in een proef: de nog niet gevonden adressen gaan terug naar de basisstand (als dat hier veilig kan; anders bij de eerste geschikte plek).
            var proefStanden = _adressen.Where(a => !_k.IsGevonden(a) && Fysiek(a) != BasisStand(a)).ToDictionary(a => a, a => BasisStand(a));
            if (proefStanden.Count > 0 && ProefPlek(_k.LocMelder)) await ZetStand(proefStanden, "na hervatten");
        }
        _fase = "Verkennen";
        BewaarNu();
    }

    private List<int> KruisMotoren()
        => (_ins.KruisMotoren ?? "").Split(new[] { '+', ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(x => int.TryParse(x.Trim(), out var a) ? a : 0).Where(a => a > 0).ToList();

    /// <summary>De basisstand van een adres: rechtdoor (Dinamo). Voor een volger van een overloopset volgt de stand uit de meester.</summary>
    private bool BasisStand(int a) => false;

    /// <summary>WAT: wacht tot Dinamo 3 s lang niets meer terugmeldt (hoogstens 3 minuten). WAAROM: BUG #355: anders voert Dinamo nog oude commando's uit tijdens de eerste rit.</summary>
    private async Task WachtTotDinamoRustig()
    {
        if (_hw is not IWisselBevestiging b) return;
        var eind = _klok.Nu + TimeSpan.FromMinutes(3);
        long vorige = b.AantalWisselBevestigingen;
        var stilSinds = _klok.Nu;
        while (_klok.Nu < eind)
        {
            await Wacht(TimeSpan.FromMilliseconds(250));
            if (b.AantalWisselBevestigingen != vorige) { vorige = b.AantalWisselBevestigingen; stilSinds = _klok.Nu; }
            else if ((_klok.Nu - stilSinds).TotalSeconds >= 3) return;
        }
        _log.Waarschuwing("Dinamo bleef 3 minuten lang oude wisselcommando's uitvoeren. Zet Dinamo eventueel even uit en aan.");
    }

    /// <summary>WAT: waar staat de loc? Bij Dinamo alle melders opvragen; staat de loc op twee melders, dan een stukje door tot hij op één staat.</summary>
    private async Task BepaalPlek()
    {
        if (_hw.KanMelderStatusOpvragen)
        {
            int hoogste = Math.Max(1, _ins.HoogsteMelder);
            for (int m = 1; m <= hoogste; m++) _hw.VraagMelderStatusOp(m);
            await Wacht(TimeSpan.FromMilliseconds(Math.Min(60000, hoogste * DinamoCyclus.TotalMilliseconds + 1500)));
        }
        else await Wacht(TimeSpan.FromSeconds(1));
        _monitor.Bijwerken();
        var bezet = _monitor.Bezet.ToList();
        if (bezet.Count == 0)
        {
            _log.Waarschuwing("Geen enkele melder is bezet: de loc kruipt vooruit tot er een melder bezet wordt.");
            await Herparkeer(Richting.Vooruit, alleBlokken: true);
            bezet = _monitor.Bezet.ToList();
            if (bezet.Count == 0) { await Herparkeer(Richting.Achteruit, alleBlokken: true); bezet = _monitor.Bezet.ToList(); }
            if (bezet.Count == 0) throw new InvalidOperationException("De loc is op geen enkele melder te vinden. Zet de loc op een melder en start opnieuw.");
        }
        if (_hervat && bezet.Count == 1 && bezet[0] == _k.LocMelder)
        {
            _log.Info($"De loc staat nog op melder {_k.LocMelder} (rijrichting {_k.LocRichting.Tekst()}{(_k.LocVia > 0 ? $", kwam van {_k.LocVia}" : "")}).");
            return;
        }
        if (bezet.Count > 1)
        {
            // bij hervatten terug zoals hij kwam (de weg terug is altijd berijdbaar), anders vooruit
            var r0 = _hervat ? _k.LocRichting.Om() : Richting.Vooruit;
            _log.Info($"De loc staat op meerdere melders ({string.Join(", ", bezet.OrderBy(x => x))}); hij kruipt {r0.Tekst()} tot hij op één melder staat.");
            await Herparkeer(r0, alleBlokken: true);
            bezet = _monitor.Bezet.ToList();
        }
        if (bezet.Count >= 1)
        {
            int oud = _k.LocMelder;
            // Gestopt tijdens een rit: staat de loc nu op de melder die (in de rijrichting van toen) na de bewaarde melder komt, dan kwam hij daarvandaan.
            bool verder = _hervat && oud > 0 && _k.Stappen.Any(s => s.Melder == oud && s.Richting == _k.LocRichting && s.Uitkomst == bezet[0]);
            _k.LocMelder = bezet[0]; _k.LocVia = verder ? oud : 0;
            if (!_hervat) _k.LocRichting = Richting.Vooruit;
        }
        if (_status.Kaart.StartMelder == 0) _status.Kaart.StartMelder = _k.LocMelder;
        _log.Info($"De loc staat op melder {_k.LocMelder}.");
    }

    // ================================================================ taken kiezen (wat is nu het nuttigst?)

    private enum Soort { Tegenspraak, Verken, Uitweg, Bevestig, Dekking, Blok }

    private sealed class Taak
    {
        public Soort Soort;
        public string Sleutel = "";
        public string Omschrijving = "";
        public int Via, Melder; public Richting Richting;
        public StapWaarneming? A, B;
        public WisselVondst2? Vondst;
        public Plan? Plan;
        public DekkingRit? Dekking;
    }

    /// <summary>WAT: kiest de volgende taak. Volgorde: eerst tegenspraak oplossen (welk adres heeft invloed?), dan onbekende kanten van melders
    /// verkennen, dan uitwegen bij een stap die alleen kortsluiting gaf, dan de tweede stand van gevonden wissels, dan alle nog niet gevonden adressen
    /// over de bekende baan testen, en als laatste melders waarvan het blok nog onbekend is. Binnen een soort: de dichtstbijzijnde.
    /// WAAROM: zo leert de verkenner eerst het geraamte en rijdt hij zo min mogelijk heen en weer (Marco 26-10: "dat scheelt heel veel tijd").</summary>
    private Taak? KiesTaak()
    {
        var kaart = Dijkstra();
        var onbereikbaar = new List<string>();
        foreach (var soort in new[] { Soort.Tegenspraak, Soort.Verken, Soort.Uitweg, Soort.Bevestig, Soort.Dekking, Soort.Blok })
        {
            Taak? beste = null; int besteKosten = int.MaxValue;
            foreach (var t in Kandidaten(soort))
            {
                if (_k.Overgeslagen.Contains(t.Sleutel)) continue;
                var (plan, kosten) = PlanVoor(t, kaart);
                if (plan is null) { onbereikbaar.Add(t.Omschrijving); continue; }
                if (kosten >= besteKosten) continue;
                t.Plan = plan; beste = t; besteKosten = kosten;
                if (soort == Soort.Dekking) break;   // de dekkingsritten komen al op volgorde van nut
            }
            if (beste is not null) return beste;
        }
        if (onbereikbaar.Count > 0)
        {
            _log.Info($"Nog {onbereikbaar.Count} taak/taken die vanaf melder {_k.LocMelder} nu niet te plannen zijn: {string.Join("; ", onbereikbaar.Take(4))}{(onbereikbaar.Count > 4 ? " …" : "")}. Laatste reden: {_planFout}.");
            _onbereikbaar = onbereikbaar.Count;
        }
        return null;
    }
    private int _onbereikbaar;
    /// <summary>Tegenspraken die met alle verschillende adressen (ook al gevonden wissels) uitgezocht worden.</summary>
    private readonly HashSet<string> _tegenspraakBreed = new();

    private IEnumerable<Taak> Kandidaten(Soort soort)
    {
        switch (soort)
        {
            case Soort.Tegenspraak:
                foreach (var g in _k.Stappen.Where(s => !s.Afgeleid && !s.Terug && s.Via > 0).GroupBy(s => s.IngangSleutel))
                {
                    var s0 = g.First();
                    if (_k.Tegenstrijdig(s0.Via, s0.Melder, s0.Richting) is { } t)
                        yield return new Taak { Soort = soort, Sleutel = "T|" + g.Key + "|" + string.Join(",", t.A.Stand) + "|" + string.Join(",", t.B.Stand), Via = s0.Via, Melder = s0.Melder, Richting = s0.Richting, A = t.A, B = t.B,
                            Omschrijving = $"uitzoeken welk adres de stap {s0.Via} -> {s0.Melder} ({s0.Richting.Tekst()}) verandert ({StapUitkomst.Tekst(t.A.Uitkomst)} of {StapUitkomst.Tekst(t.B.Uitkomst)})" };
                }
                break;
            case Soort.Verken:
                foreach (var m in _k.Melders.Append(_k.LocMelder).Where(x => x > 0).Distinct().OrderBy(x => x))
                    foreach (var r in new[] { Richting.Vooruit, Richting.Achteruit })
                        if (!UitgangBekend(m, r))
                            yield return new Taak { Soort = soort, Sleutel = $"V|{m}|{r}", Melder = m, Richting = r, Omschrijving = $"verkennen wat er na melder {m} komt ({r.Tekst()})" };
                break;
            case Soort.Uitweg:
                foreach (var g in _k.Stappen.Where(s => !s.Afgeleid && !s.Terug && s.Via > 0 && s.Uitkomst == StapUitkomst.Kortsluiting).GroupBy(s => s.IngangSleutel))
                {
                    var s0 = g.First();
                    // alleen als er vanuit deze melder in deze richting nog géén enkele doorgang bekend is
                    if (_k.Stappen.Any(s => s.Melder == s0.Melder && s.Richting == s0.Richting && s.Uitkomst != StapUitkomst.Kortsluiting)) continue;
                    yield return new Taak { Soort = soort, Sleutel = "U|" + g.Key, Via = s0.Via, Melder = s0.Melder, Richting = s0.Richting, A = s0,
                        Omschrijving = $"een uitweg zoeken na melder {s0.Melder} ({s0.Richting.Tekst()}): daar gaf elke stand tot nu toe kortsluiting" };
                }
                break;
            case Soort.Bevestig:
                foreach (var v in _k.Vondsten.Where(v => v.Rechtdoor is null || v.Afbuigend is null))
                    yield return new Taak { Soort = soort, Sleutel = $"B|{v.Adres}|{Kennis2.Sleutel(v.Via, v.Melder, v.Richting)}", Via = v.Via, Melder = v.Melder, Richting = v.Richting, Vondst = v,
                        Omschrijving = $"wissel {v.Adres} na melder {v.Melder} ({v.Richting.Tekst()}) ook {(v.Rechtdoor is null ? "rechtdoor" : "afbuigend")} rijden" };
                break;
            case Soort.Dekking:
                foreach (var d in DekkingRitten()) yield return new Taak { Soort = soort, Sleutel = d.Sleutel, Dekking = d, Omschrijving = d.Omschrijving };
                break;
            case Soort.Blok:
                if (_hw.LocCommandoVereistBlok)
                    foreach (var m in _k.Melders.Where(m => !_k.Blokken.ContainsKey(m)).OrderBy(x => x))
                        yield return new Taak { Soort = soort, Sleutel = $"K|{m}", Melder = m, Omschrijving = $"het Dinamo-blok van melder {m} bepalen" };
                break;
        }
    }

    /// <summary>Is bekend waar de loc komt als hij melder m in richting r verlaat? Ja als er een stap vanuit m in r is, of een stap die m in de
    /// andere richting binnenkwam (die kwam dan door het r-einde van m binnen).</summary>
    internal bool UitgangBekend(int m, Richting r)
        => _k.Stappen.Any(s => s.Melder == m && s.Richting == r) || _k.Stappen.Any(s => s.Uitkomst == m && s.Richting == r.Om());

    private async Task<bool> VoerTaakUit(Taak t)
    {
        switch (t.Soort)
        {
            case Soort.Verken: return await DoeVerken(t);
            case Soort.Tegenspraak: return await DoeTegenspraak(t);
            case Soort.Uitweg: return await DoeUitweg(t);
            case Soort.Bevestig: return await DoeBevestig(t);
            case Soort.Dekking: return await DoeDekking(t);
            case Soort.Blok: return await DoeBlok(t);
        }
        return false;
    }

    /// <summary>WAT: rijdt de loc een paar bekende melders verder (of terug) zonder wissels om te zetten. WAAROM: vanaf sommige plekken (bijv. in een
    /// wisselgebied) kan geen wissel veilig om; een paar melders verder wel.</summary>
    private async Task<bool> RijdStukjeVerder()
    {
        var kaart = Dijkstra();
        var kandidaten = kaart.Kosten.Where(kv => kv.Value >= 1 && kv.Value <= 12).OrderBy(kv => (kv.Key.M * 7919 + _k.Ritten) % 97).Select(kv => kv.Key).ToList();
        foreach (var d in kandidaten)
        {
            var p = MaakPlan(kaart.Start, Pad(kaart, d), d, kaart.Kosten[d]);
            if (p is null) continue;
            _log.Info($"Een stukje verder rijden (naar melder {d.M}) om van daaruit verder te kunnen.");
            return await VoerPlanUit(p);
        }
        // Geen bekende route zonder wissels om te zetten (bijv. na hervatten midden in een wisselgebied): één melder verder met de wissels zoals ze staan.
        _log.Info("Geen bekende route zonder wissels om te zetten; de loc rijdt één melder verder met de wissels zoals ze nu staan.");
        var r = _k.LocVia > 0 ? _k.LocRichting.Om() : _k.LocRichting;
        // niet de kant op waar volgens de kennis een kortsluiting zit
        if (_k.Voorspel(_k.LocVia, _k.LocMelder, r, Fysiek) is { Uitkomst: <= 0 } || _k.Stappen.Any(x => x.Melder == _k.LocMelder && x.Richting == r && x.Uitkomst == StapUitkomst.Kortsluiting && x.Stand.SequenceEqual(HuidigeStand())))
            r = r.Om();
        await Rijd(new RitOpdracht2 { Richting = r, ExtraStappen = 1, Reden = "loskomen" });
        return true;
    }

    // ================================================================ taak: verkennen (fase 2: het geraamte)

    /// <summary>WAT: rijdt naar melder m (in richting r binnenkomend) en rijdt dan door zolang wat volgt nog onbekend is.
    /// WAAROM: zo komt het geraamte (melders, volgorde, richting) er in zo weinig mogelijk ritten in.</summary>
    private async Task<bool> DoeVerken(Taak t)
    {
        if (!await VoerPlanUit(t.Plan!)) return false;
        if (UitgangBekend(t.Melder, t.Richting)) return true;   // onderweg al gezien
        if (_k.LocMelder != t.Melder) return false;
        await Rijd(new RitOpdracht2 { Richting = t.Richting, Verkennen = true, StopBijAfwijking = false, Reden = "verkennen" });
        return true;
    }

    // ================================================================ taak: tegenspraak (fase 3: welk adres?)

    /// <summary>WAT: bij dezelfde ingang en dezelfde stand van de bekende wissels kwam de loc twee keer ergens anders uit. Een van de adressen die
    /// toen anders stonden heeft dus invloed. Door steeds de helft van die adressen om te zetten (en daarna heen en terug) wordt binnen een paar
    /// proeven gevonden welk adres het is. WAAROM: zo kost het vinden van één wissel tussen bijv. 12 adressen hooguit 4 korte proeven.</summary>
    private async Task<bool> DoeTegenspraak(Taak t)
    {
        var a = t.A!; var b = t.B!;
        // A = de 'schoonste' stand (het minst afwijkend van de basisstand): de proeven zetten dan adressen ERBIJ, zodat een effect dat alleen door een
        // combinatie ontstaat niet ten onrechte aan één adres wordt toegeschreven.
        int Vuil(StapWaarneming x) => _adressen.Count(q => x.Stand.Contains(q) != BasisStand(q));
        if (Vuil(b) < Vuil(a)) (a, b) = (b, a);
        var rel = _k.RelevantInKnoop(t.Melder, t.Richting);
        // Een wissel ligt op één plek: een adres dat al bij een andere knoop gevonden is, kan het hier niet zijn (behalve een motor van de Engelse
        // wissel, die bij beide kanten van de kruising hoort).
        var kruis = KruisMotoren();
        var alleVerschil = _adressen.Where(x => a.Stand.Contains(x) != b.Stand.Contains(x) && !rel.Contains(x)).ToList();
        var verschil = alleVerschil.Where(x => !_k.IsGevonden(x) || kruis.Contains(x)).ToList();
        if (verschil.Count == 0 || _tegenspraakBreed.Contains(t.Sleutel)) verschil = alleVerschil;
        if (verschil.Count == 0)
        {
            // Alleen een volger of een adres buiten het bereik verschilt: dan is het geen wissel maar ruis (bijv. een kortsluiting die vanzelf ontstond).
            _log.Waarschuwing($"Stap {t.Via} -> {t.Melder} ({t.Richting.Tekst()}) gaf bij dezelfde stand twee uitkomsten ({StapUitkomst.Tekst(a.Uitkomst)} en {StapUitkomst.Tekst(b.Uitkomst)}); geen adres verschilt. De zeldzaamste uitkomst wordt vergeten.");
            var weg = a.Aantal <= b.Aantal ? a : b;
            _k.Stappen.Remove(weg);
            return true;
        }
        // basis = de volledige stand A (uitkomst a.Uitkomst); proef = een deel van 'verschil' naar de stand van B. Alle andere adressen staan zoals bij A,
        // zodat de proef precies één verschil heeft met A.
        var basis = new Dictionary<int, bool>();
        foreach (var x in _adressen) basis[x] = a.Stand.Contains(x);
        var kandidaten = verschil.ToList();
        int? uitkomstLos = null;   // uitkomst met alleen de gevonden kandidaat omgezet (als die proef gedaan is)
        int proeven = 0;
        while (kandidaten.Count > 1 && proeven < 8)
        {
            var helft = kandidaten.Take(kandidaten.Count / 2).ToList();
            var stand = new Dictionary<int, bool>(basis);
            foreach (var x in helft) stand[x] = b.Stand.Contains(x);
            proeven++;
            _log.Stap($"Proef {proeven}: adres {string.Join(", ", helft)} {(helft.Count == 1 ? "staat" : "staan")} zoals bij de tweede uitkomst, de rest zoals bij de eerste.");
            // bij kortsluiting: terug naar stand A als die bewezen goed ging; anders langzaam terugrijden
            var u = await Proef(t.Via, t.Melder, t.Richting, stand, herstel: a.Uitkomst > 0 ? new Dictionary<int, bool>(basis) : new Dictionary<int, bool>());
            if (u is null) return false;
            if (u.Value != a.Uitkomst) { kandidaten = helft; uitkomstLos = helft.Count == 1 ? u : null; }
            else { kandidaten = kandidaten.Except(helft).ToList(); uitkomstLos = null; }
        }
        if (kandidaten.Count != 1) return false;
        int adres = kandidaten[0];
        if (uitkomstLos is null)
        {
            // Nooit 'door wegstrepen' concluderen: het laatste adres eerst alléén omgezet rijden. Verandert er dan niets, dan kwam het verschil door een
            // combinatie van adressen (bijv. wissel 5 en motor 13 samen bij de Engelse wissel); die waarneming wordt dan vergeten.
            var stand1 = new Dictionary<int, bool>(basis) { [adres] = b.Stand.Contains(adres) };
            _log.Stap($"Controle: alleen adres {adres} zoals bij de tweede uitkomst.");
            var u1 = await Proef(t.Via, t.Melder, t.Richting, stand1, herstel: a.Uitkomst > 0 ? new Dictionary<int, bool>(basis) : new Dictionary<int, bool>());
            if (u1 is null) return false;
            if (u1.Value == a.Uitkomst && verschil.Count < alleVerschil.Count && !_tegenspraakBreed.Contains(t.Sleutel))
            {
                // Eerst nog eens met ALLE verschillende adressen, ook die al elders gevonden zijn: via een overloopset kan een wissel ook bij een andere
                // knoop invloed hebben (wissel 5 bepaalt via volger 6 de stap 10 -> 129, simulatie 28-10).
                _tegenspraakBreed.Add(t.Sleutel);
                _log.Info($"Geen enkel nog niet gevonden adres verklaart het verschil bij {t.Via} -> {t.Melder}; nu ook met de al gevonden wissels.");
                return false;
            }
            if (u1.Value == a.Uitkomst)
            {
                _k.Mislukt["U|" + Kennis2.Sleutel(t.Via, t.Melder, t.Richting)] = _k.Mislukt.GetValueOrDefault("U|" + Kennis2.Sleutel(t.Via, t.Melder, t.Richting)) + 1;
                if (_k.Mislukt["U|" + Kennis2.Sleutel(t.Via, t.Melder, t.Richting)] >= 2 && !_k.Overgeslagen.Contains("U|" + Kennis2.Sleutel(t.Via, t.Melder, t.Richting)))
                    _k.Overgeslagen.Add("U|" + Kennis2.Sleutel(t.Via, t.Melder, t.Richting));
                _log.Waarschuwing($"Het verschil bij {t.Via} -> {t.Melder} ({t.Richting.Tekst()}) komt niet door één adres maar door een combinatie; de waarneming met {StapUitkomst.Tekst(b.Uitkomst)} (adressen {string.Join(",", b.Stand)} afbuigend) wordt vergeten.");
                _k.Stappen.Remove(b);
                return true;
            }
            uitkomstLos = u1;
        }
        _k.MaakRelevant(t.Via, t.Melder, t.Richting, adres);
        bool aAfb = a.Stand.Contains(adres);
        int? metB = uitkomstLos ?? (verschil.Count == 1 ? b.Uitkomst : null);
        var v = new WisselVondst2
        {
            Adres = adres, Via = t.Via, Melder = t.Melder, Richting = t.Richting, Tijd = _klok.Nu,
            Rechtdoor = aAfb ? metB : a.Uitkomst, Afbuigend = aAfb ? a.Uitkomst : metB,
            Stand = rel.Where(x => a.Stand.Contains(x)).OrderBy(x => x).ToList()
        };
        _k.Vondsten.RemoveAll(x => x.Adres == adres && x.Via == v.Via && x.Melder == v.Melder && x.Richting == v.Richting);
        _k.Vondsten.Add(v);
        _log.Vondst($"WISSEL GEVONDEN: adres {adres} na melder {t.Melder} ({t.Richting.Tekst()}, vanaf {t.Via}): rechtdoor -> {(v.Rechtdoor is int rd ? StapUitkomst.Tekst(rd) : "nog niet gereden")}, afbuigend -> {(v.Afbuigend is int af ? StapUitkomst.Tekst(af) : "nog niet gereden")}.");
        foreach (var p in _paren.Where(p => p.Meester == adres)) _log.Info($"Wissel {p.Volger} schakelt mee met wissel {adres} (overloopset); hij krijgt dezelfde vondst.");
        return true;
    }

    // ================================================================ taak: bevestigen (de tweede stand van een gevonden wissel)

    private async Task<bool> DoeBevestig(Taak t)
    {
        var v = t.Vondst!;
        var rel = _k.RelevantInKnoop(v.Melder, v.Richting);
        var stand = rel.ToDictionary(x => x, x => v.Stand.Contains(x));
        bool afb = v.Afbuigend is null;
        var herstel = new Dictionary<int, bool>(stand) { [v.Adres] = !afb };
        bool bekendGoed = (afb ? v.Rechtdoor : v.Afbuigend) > 0;
        stand[v.Adres] = afb;
        var u = await Proef(v.Via, v.Melder, v.Richting, stand, herstel: bekendGoed ? herstel : new Dictionary<int, bool>());
        if (u is null) return false;
        if (afb) v.Afbuigend = u; else v.Rechtdoor = u;
        _log.Vondst($"Wissel {v.Adres} na melder {v.Melder} ({v.Richting.Tekst()}): {(afb ? "afbuigend" : "rechtdoor")} -> {StapUitkomst.Tekst(u.Value)}.");
        return true;
    }

    // ================================================================ taak: uitweg zoeken (fase 5)

    /// <summary>WAT: na melder m gaf elke stand tot nu toe kortsluiting. Alle andere adressen tegelijk omzetten en het nog eens proberen; lukt het dan,
    /// dan zoekt de tegenspraak-taak uit welk adres de uitweg geeft. WAAROM: één proef laat zien of er überhaupt een uitweg met een ander adres is.</summary>
    private async Task<bool> DoeUitweg(Taak t)
    {
        var a = t.A!;
        var rel = _k.RelevantInKnoop(t.Melder, t.Richting);
        var stand = rel.ToDictionary(x => x, x => a.Stand.Contains(x));
        var flips = _adressen.Where(x => !rel.Contains(x)).ToHashSet();
        foreach (var x in flips) stand[x] = !a.Stand.Contains(x);
        _log.Stap($"Uitweg na melder {t.Melder}: alle adressen buiten de bekende wissels van die knoop omgezet (behalve wat nodig is om er te komen).");
        // 'zacht': wat de weg ernaartoe nodig heeft (bijv. wissel 5 afbuigend om bij 129 te komen) gaat voor
        var u = await Proef(t.Via, t.Melder, t.Richting, stand, uitweg: true, herstel: new Dictionary<int, bool>(), zacht: flips);
        if (u is null) return false;
        if (u == StapUitkomst.Kortsluiting)
        {
            _log.Vondst($"Na melder {t.Melder} ({t.Richting.Tekst()}, vanaf {t.Via}) is met geen enkel ander adres een uitweg: die kant is (in deze richting) niet berijdbaar.");
            if (!_k.Overgeslagen.Contains(t.Sleutel)) _k.Overgeslagen.Add(t.Sleutel);
        }
        return true;
    }

    // ================================================================ taak: blok bepalen

    private async Task<bool> DoeBlok(Taak t)
    {
        if (!await VoerPlanUit(t.Plan!)) return false;
        await BlokProefAlsNodig();
        return _k.Blokken.ContainsKey(t.Melder);
    }

    // ================================================================ afronden (fase 7)

    /// <summary>WAT: de baankaart opbouwen en controleren of alles af is. WAAROM: zelfde kaart als module 1, dus rapport en schema's werken direct.</summary>
    private void Afronden()
    {
        _fase = "Afronden";
        ProjecteerKaart();
        var k = _status.Kaart;
        var zonderBlok = _hw.LocCommandoVereistBlok ? k.Melders.Where(m => m.DinamoBlok is null).Select(m => m.Nummer).OrderBy(x => x).ToList() : new List<int>();
        k.Voltooid = zonderBlok.Count == 0;
        if (zonderBlok.Count > 0)
        {
            var w = $"De verkenning is nog NIET af: van melder {string.Join(", ", zonderBlok)} is het Dinamo-blok niet bepaald.";
            if (!k.Waarschuwingen.Contains(w)) k.Waarschuwingen.Add(w);
            _log.Waarschuwing(w);
        }
        foreach (var a in _adressen.Where(a => !_k.IsGevonden(a)))
        {
            var open = _k.GerichteOvergangen.Where(p => !_k.IsGetest(a, p.Van, p.Naar)).Select(p => $"{p.Van}>{p.Naar}").ToList();
            if (open.Count > 0) _log.Info($"Adres {a}: nog niet getest op {string.Join(", ", open)}.");
        }
        foreach (var s in _k.Overgeslagen)
        {
            var w = $"Module 2: taak overgeslagen ({s}).";
            if (!k.Waarschuwingen.Contains(w)) k.Waarschuwingen.Add(w);
        }
        _fase = k.Voltooid ? "Voltooid" : "Gestopt (tussenstand)";
        _bezig = "";
        _log.Vondst($"Resultaat module 2: {k.Melders.Count} melders, {k.Wissels.Count(w => w.Waarnemingen.Count > 0)} wissels, {k.Kopsporen.Count} doodlopende einden, " +
                    $"{k.AdressenZonderEffect.Count} adressen zonder effect, {_k.Ritten} ritten, {_k.Kortsluitingen} kortsluitingen.");
    }

    private void BewaarNu()
    {
        try { ProjecteerKaart(); } catch (Exception ex) { _log.Waarschuwing($"Kaart bijwerken mislukte: {ex.Message}"); }
        _status.Kaart.Bijgewerkt = _klok.Nu;
        _status.Module2 = _k;
        _status.Module = 2;
        try { Bewaren?.Invoke(_status); }
        catch (Exception ex) { _log.Waarschuwing($"Voortgang bewaren mislukte: {ex.Message}"); }
        MeldVoortgang();
    }

    private void MeldVoortgang()
    {
        int teTesten = _adressen.Count(a => !_k.IsGevonden(a));
        VoortgangGewijzigd?.Invoke(new VerkenVoortgang(_gepauzeerd ? "Gepauzeerd" : _fase, _bezig, _k.Ritten, 0,
            _k.Melders.Count(), _adressen.Count(a => _k.IsGevonden(a)), teTesten, _k.Kortsluitingen));
    }
}

/// <summary>Een route kon niet gepland worden (geen veilige plek om een wissel om te zetten, of geen bekende weg).</summary>
public class PlanFout : Exception { public PlanFout(string m) : base(m) { } }
