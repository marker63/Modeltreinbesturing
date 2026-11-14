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
        foreach (var sp in _k.Spookmelders) _monitor.MarkeerSpook(sp);   // BUG #382
    }

    /// <summary>BUG #382: spookmelders opsporen en uit de kennis en de kaart halen.</summary>
    private void RuimSpookmeldersOp()
    {
        while (_k.ZoekSpook() is int sp)
        {
            _log.Vondst($"Melder {sp} is geen echte melder: de loc reed met dezelfde wisselstand ook zonder melder {sp} van de melder ervoor naar de melder erna. Melder {sp} wordt voortaan genegeerd (BUG #382, nog niet getest op de baan).");
            _k.VerwijderSpook(sp);
            _monitor.MarkeerSpook(sp);
            _status.Kaart.Melders.RemoveAll(x => x.Nummer == sp);
            if (_k.LocMelder == sp) _k.LocMelder = 0;
        }
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
            // BUG #412: 'Voltooide baan nogmaals checken' met module 2
            // Klopt alles, dan is de controle klaar. Zijn er fouten (kortsluiting, afwijking, niet te bereiken), dan lost de gewone verkenning ze meteen op
            // (de nieuwe waarnemingen geven tegenspraak-taken) en daarna volgt een tweede controle.
            bool naControle412 = false;
            if (_status.Controle)
            {
                if (await Controle412()) { BewaarNu(); return; }
                _log.Stap("Controle: er zijn fouten gevonden; de verkenning lost ze nu zelf op en controleert daarna opnieuw (BUG #412, nog niet getest op de baan).");
                naControle412 = true;
            }
            int taken = 0;
            int verplaatst = 0;
            while (taken++ < MaxTaken)
            {
                _token.ThrowIfCancellationRequested();
                await PauzeMoment();
                _onbereikbaar = 0;
                RuimSpookmeldersOp();   // BUG #382
                // WAT: staat de loc niet netjes op één melder (op de grens van twee, of op geen enkele), dan eerst netjes parkeren.
                // WAAROM (BUG #378, Marco 04-11 10:22, eerste rit met module 2): na het zelf oplossen bij 4/129 bleef de loc op de grens staan. Elke taak faalde
                // daarna meteen ("de loc staat niet netjes op één melder"), werd twee keer geprobeerd en overgeslagen: binnen 0,1 s stopte de hele verkenning.
                _monitor.Bijwerken();
                // BUG #387: staat er nog een kortsluiting (bijv. na een afwijkende rit die op de grens van 4 en 129 eindigde), dan eerst die opheffen; anders faalde
                // elke taak meteen en stopte de verkenning als niet af (simulatie met Marco's voortgang van 08-11).
                if (Kortsluiting())
                {
                    _log.Waarschuwing("Er staat nog een kortsluiting: eerst opheffen (BUG #387, nog niet getest op de baan).");
                    await HerstelKortsluiting(new Dictionary<int, bool>(), _k.LocRichting);
                    _monitor.Bijwerken();
                }
                if (_monitor.Bezet.Count != 1 && !Kortsluiting())
                {
                    _log.Info($"De loc staat niet netjes op één melder ({(_monitor.Bezet.Count == 0 ? "geen melder bezet" : string.Join(" en ", _monitor.Bezet.OrderBy(x => x)))}): eerst netjes parkeren (BUG #378, nog niet getest op de baan).");
                    try { await Herparkeer(_k.LocRichting.Om(), alleBlokken: true); } catch (PlanFout) { }
                    _monitor.Bijwerken();
                    if (_monitor.Bezet.Count != 1) { try { await Herparkeer(_k.LocRichting, alleBlokken: true); } catch (PlanFout) { } }
                    _monitor.Bijwerken();
                    if (_monitor.Bezet.Count == 1) _k.LocMelder = _monitor.Bezet.First();
                    // BUG #402 (Marco 12-11 08:42: "de voltooide verkenning klopt totaal niet, 17 minuten"): na een rit met alle adressen omgezet verdween de loc voorbij
                    // melder 129 (129 en 14 vrij, daarna geen enkele melder meer). Het programma noemde dat "doodlopend" in beide richtingen en rondde af als VOLTOOID,
                    // met 17 melders en 0 wissels. Nu: is er geen enkele melder bezet, dan de loc zoeken (#396, hoogstens 10 minuten); lukt dat niet, dan stoppen als
                    // NIET af met de melding dat de loc kwijt is.
                    if (_monitor.Bezet.Count == 0 && !Kortsluiting())
                    {
                        _log.Waarschuwing($"De loc is na melder {_k.LocMelder} op geen enkele melder meer te zien: zoeken (BUG #402, nog niet getest op de baan).");
                        var gevonden = await ZoekLoc396();
                        if (gevonden.Count == 0)
                        {
                            _locKwijt402 = $"De loc is na melder {_k.LocMelder} kwijtgeraakt (geen enkele melder bezet, ook niet na 10 minuten zoeken). Kijk of de loc ontspoord is of op een stuk zonder stroom staat, zet hem op een melder en hervat.";
                            _log.Waarschuwing(_locKwijt402);
                            break;
                        }
                        if (gevonden.Count == 1) _k.LocMelder = gevonden[0];
                        continue;
                    }
                }
                // WAT: staat de loc netjes op één melder, maar is dat niet de melder waar de verkenner hem denkt, dan geldt de melder die bezet is.
                // WAAROM (BUG #407, Marco 12-11 14:55): na "26, 25, 268" dacht de verkenner dat de loc op 268 stond, maar alleen melder 25 was bezet. Elke taak
                // wilde vanaf 268 plannen, het omzetten werd bij 25 geweigerd ("kruising of te kort"), en binnen een seconde waren 10 taken overgeslagen: NIET af.
                // BUG #410: kwam de loc niet weg (geen doodlopend spoor), dan eerst de rijstroom per blok proberen
                if (_nietWeg410 > 0)
                {
                    _nietWeg410 = 0;
                    await ZoekPerBlok408(alleenBijVerandering: true);
                    _monitor.Bijwerken();
                }
                if (_monitor.Bezet.Count == 1 && !Kortsluiting() && _monitor.Bezet.First() != _k.LocMelder && _syncAchterElkaar407++ < 3)
                {
                    int echt = _monitor.Bezet.First();
                    _log.Info($"De loc staat op melder {echt}, niet op {_k.LocMelder}: vanaf nu wordt vanaf melder {echt} gepland (BUG #407, nog niet getest op de baan).");
                    _k.LocVia = 0;   // van welke kant hij kwam is niet zeker
                    _k.LocMelder = echt;
                }
                else if (_monitor.Bezet.Count == 1 && _monitor.Bezet.First() == _k.LocMelder) _syncAchterElkaar407 = 0;
                if (_monitor.Bezet.Count == 1 && !Kortsluiting()) _k.Echt.Add(_monitor.Bezet.First());   // BUG #393: de loc staat stil en alleen op deze melder
                // BUG #394 (werkwijze van Marco): staat de loc netjes op een melder waarvan het blok nog onbekend is, dan daar meteen de blokproef.
                if (W3a && _monitor.Bezet.Count == 1 && !Kortsluiting() && _k.LocMelder == _monitor.Bezet.First()) await BlokProefAlsNodig();
                var taak = KiesTaak();
                if (taak is null)
                {
                    // Er zijn nog taken, maar vanaf hier lukt geen plan (bijv. geen veilige plek om een wissel om te zetten): een stukje bekende baan
                    // rijden en opnieuw proberen (hoogstens 6 keer achter elkaar).
                    if (_onbereikbaar > 0 && verplaatst++ < 6 && await RijdStukjeVerder()) continue;
                    // WAT: is de loc bij dat stukje verder rijden uit beeld geraakt, dan eerst zoeken (begin van de lus) in plaats van stoppen.
                    // WAAROM (BUG #409, Marco 12-11 16:25): de rit 129 -> 4 liep vast, geen melder meer bezet, en de verkenning stopte meteen als NIET af
                    // ("4 taken konden vanaf melder 129 niet gepland worden") zonder de loc te zoeken.
                    _monitor.Bijwerken();
                    if (_monitor.Bezet.Count == 0 && !Kortsluiting() && verplaatst <= 6) continue;
                    // BUG #404: zijn er taken overgeslagen, dan krijgen die aan het eind nog één keer een kans (met alles wat inmiddels bekend is)
                    if (_k.Overgeslagen.Count > 0 && !_herkansing404)
                    {
                        _herkansing404 = true;
                        _log.Info($"Nog {_k.Overgeslagen.Count} overgeslagen taak/taken: die krijgen nog één kans (BUG #404).");
                        _k.Overgeslagen.Clear(); _k.Mislukt.Clear();
                        continue;
                    }
                    break;
                }
                verplaatst = 0;
                _bezig = taak.Omschrijving;
                MeldVoortgang();
                _log.Stap($"Taak: {taak.Omschrijving}.");
                bool gelukt;
                _nietNetjes387 = false;
                _nietHier404 = false;
                int meldersVoor394 = _k.Melders.Count(), stappenVoor394 = _k.Stappen.Count(x => !x.Afgeleid);
                try { gelukt = await VoerTaakUit(taak); }
                catch (PlanFout pf) { _log.Waarschuwing($"Taak '{taak.Omschrijving}' lukt nu niet: {pf.Message}"); gelukt = false; }
                // BUG #387: mislukte de taak alleen omdat de loc op de grens van twee melders stond, dan eerst netjes parkeren (begin van de lus) en niet als poging tellen
                // (hoogstens 5 keer achter elkaar, zodat het geen eindeloze lus wordt).
                if (!gelukt && _nietNetjes387 && _nietNetjesAchterElkaar++ < 5) { _log.Info("De taak lukte niet omdat de loc op de grens van twee melders staat: eerst netjes parkeren, dan opnieuw (BUG #387)."); continue; }
                if (gelukt) { _nietNetjesAchterElkaar = 0; _andereAannameGeprobeerd = false; }
                // BUG #394: mislukte de taak maar is er onderweg iets nieuws gevonden (een nieuwe melder of overgang), dan telt het niet als mislukte poging, en mogen
                // eerder overgeslagen taken weer: met de nieuwe kennis is er vaak wel een weg (simulatie: "verkennen na melder 5" werd overgeslagen omdat de route twee keer
                // ergens anders uitkwam, daarna bleef de hele buitenring onverkend).
                bool nieuws394 = _k.Melders.Count() > meldersVoor394 || _k.Stappen.Count(x => !x.Afgeleid) > stappenVoor394 + 1;
                if (nieuws394 && _k.Overgeslagen.Count > 0) { _k.Overgeslagen.Clear(); _k.Mislukt.Clear(); }
                if (!gelukt && !nieuws394)
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
            if (naControle412)
            {
                _status.Controle = true;
                await Controle412(tweede: true);
                BewaarNu();
            }
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
        // BUG #404: bij hervatten krijgen eerder overgeslagen taken een nieuwe kans (Marco's voortgang van 09:20 had er 8, allemaal binnen een halve seconde mislukt)
        if (_hervat && (_k.Overgeslagen.Count > 0 || _k.Mislukt.Count > 0))
        {
            _log.Info($"Hervat: {_k.Overgeslagen.Count} eerder overgeslagen taak/taken krijgen een nieuwe kans (BUG #404).");
            _k.Overgeslagen.Clear(); _k.Mislukt.Clear();
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
    /// <summary>WAT: de beginstand van een adres (Dinamo-commando 'rechtdoor').
    /// BUG #418: geprobeerd om bij een omgekeerd aangesloten wissel (9) met Dinamo 'afbuigend' te beginnen (op de baan rechtdoor, stappenplan stap 1). In de simulatie
    /// werd dat duidelijk slechter (start 13: 66 -> 90 min, start 17 niet meer voltooid), dus niet gedaan: de verkenner begint met alle Dinamo-commando's rechtdoor.</summary>
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
            if (bezet.Count == 0) bezet = await ZoekLoc396();
            if (bezet.Count == 0) throw new InvalidOperationException("De loc is op geen enkele melder te vinden. Zet de loc op een melder en start opnieuw.");
            // BUG #383 (log 05-11 10:51): de kruiprit begon "vanaf" de bewaarde melder (30), maar de loc stond daar niet meer; daardoor gold 30 als 'kwam van' en plande hij
            // 14 -> 30 (omkeren), wat meteen misging. Waar de loc vandaan kwam is na zo'n kruiprit onbekend.
            if (bezet.Count == 1) { _k.LocMelder = bezet[0]; _k.LocVia = 0; }
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

    /// <summary>WAT: de loc zoeken als na twee korte kruipritten nog geen melder bezet is: om beurten vooruit en achteruit, elke keer langer en (elke twee pogingen) iets
    /// sneller, tot er een melder verandert; hoogstens 10 minuten (zelf oplossen, BUG #305). WAAROM (BUG #396, Marco 09-11 17:50: "deze versie vindt de hele loc niet"):
    /// bij een nieuwe verkenning stond de loc nog op melder 25, maar Dinamo meldt alleen wijzigingen, dus het programma wist dat niet. Twee keer 22 s kruipen bleef binnen
    /// melder 25 (een lange melder), er veranderde niets en de verkenning stopte met "de loc is op geen enkele melder te vinden".</summary>
    private async Task<List<int>> ZoekLoc396()
    {
        var eind = _klok.Nu + TimeSpan.FromMinutes(10);
        // BUG #408: eerst per blok proberen (de loc staat waarschijnlijk op een sectiescheiding)
        var perBlok = await ZoekPerBlok408();
        if (perBlok.Count > 0) return perBlok;
        for (int n = 1; _klok.Nu < eind; n++)
        {
            _token.ThrowIfCancellationRequested();
            var r = n % 2 == 1 ? Richting.Vooruit : Richting.Achteruit;
            int v = BaanVerkenner.ZoekKruipSnelheid(Math.Max(1, _ins.Kruipsnelheid), _ins.Verkensnelheid, n + 2);
            var duur = TimeSpan.FromSeconds(Math.Min(90, 20 + 15 * n));
            _log.Info($"Loc zoeken, poging {n}: {r.Tekst()} met snelheid {v}, hoogstens {duur.TotalSeconds:0} s, tot er een melder verandert (BUG #396, nog niet getest op de baan).");
            _monitor.Bijwerken();
            var t0 = _klok.Nu;
            StuurSnelheid(v, r, AlleBlokken());
            bool veranderd = false;
            while (_klok.Nu - t0 < duur)
            {
                await _klok.Wacht(Poll);
                if (Kortsluiting()) break;
                if (_monitor.Bijwerken().Any(x => x.Tijd >= t0) || _monitor.Bezet.Count > 0) { veranderd = true; break; }
            }
            await Stop();
            _monitor.Bijwerken();
            if (Kortsluiting()) { await HerstelKortsluiting(new Dictionary<int, bool>(), r); _monitor.Bijwerken(); }
            // BUG #403 (Marco 12-11 08:47: de loc stond op melder 129, terwijl Dinamo 129 als vrij meldde): een melder ziet de loc alleen als er stroom loopt. Met
            // de proefstand (alle adressen omgezet) lag het stuk van de kruiswissel waar de loc stond zonder stroom: de loc kon niet weg en was voor Dinamo
            // onzichtbaar. Na twee vergeefse pogingen daarom alle wissels terug in de basisstand (de loc staat stil en er loopt geen stroom), daarna verder zoeken.
            // BUG #405 (Marco 12-11, 08:35 bij 129, 09:33 bij 129 en 10:35 bij 10, telkens ook in de basisstand): de loc stond zonder stroom op een stuk zonder melder
            // (het puntstuk van wissel 5/6 bij de overloop en de keerlus). Zoeken met rijstroom helpt dan niet: alleen een wissel daar geeft het stuk weer stroom.
            // Daarom na de basisstand de wissels die bij de laatste melder van de loc horen, één voor één omzetten; na elke omzetting kort heen en weer kruipen.
            // Geeft dat een melder, dan blijft die stand staan. De loc staat stil en er loopt geen stroom (een kleine kans dat een tong onder een stilstaande loc
            // verschuift blijft, gemeld aan Marco 12-11 10:09).
            if (!veranderd && _monitor.Bezet.Count == 0 && n == 3)
            {
                var kandidaten = KandidatenStroom405(_k.LocMelder);
                _log.Waarschuwing($"De loc staat waarschijnlijk zonder stroom bij melder {_k.LocMelder}: wissel {string.Join(", ", kandidaten)} één voor één omzetten tot hij weer stroom krijgt (BUG #405, nog niet getest op de baan).");
                foreach (var a in kandidaten)
                {
                    bool oud = Fysiek(a);
                    await ZetAdres(a, !oud);
                    foreach (var r2 in new[] { Richting.Achteruit, Richting.Vooruit })
                    {
                        _monitor.Bijwerken();
                        var t1 = _klok.Nu;
                        StuurSnelheid(BaanVerkenner.ZoekKruipSnelheid(Math.Max(1, _ins.Kruipsnelheid), _ins.Verkensnelheid, 3), r2, AlleBlokken());
                        bool gezien = false;
                        while (_klok.Nu - t1 < TimeSpan.FromSeconds(8))
                        {
                            await _klok.Wacht(Poll);
                            if (Kortsluiting()) break;
                            if (_monitor.Bijwerken().Any(x => x.Tijd >= t1) || _monitor.Bezet.Count > 0) { gezien = true; break; }
                        }
                        await Stop();
                        _monitor.Bijwerken();
                        if (Kortsluiting()) { await HerstelKortsluiting(new Dictionary<int, bool>(), r2); _monitor.Bijwerken(); }
                        if (gezien || _monitor.Bezet.Count > 0)
                        {
                            // BUG #406 (12-11 12:58): bij "wissel 3" verscheen melder 10 in plaats van 129, maar wissel 3 bestaat op de baan niet. De loc was dus
                            // al gaan rijden (hij had stroom, alleen op een stuk zonder melder). Alleen als dezelfde melder terugkomt wijst het op de wissel.
                            var gezienM = _monitor.Bezet.ToList();
                            if (gezienM.Contains(_k.LocMelder))
                                _log.Vondst($"Met wissel {a} omgezet kreeg de loc weer stroom op melder {_k.LocMelder} (BUG #405).");
                            else
                                _log.Info($"Loc weer gezien op melder {string.Join(", ", gezienM)} (was {_k.LocMelder}) tijdens het proberen van wissel {a}: de loc was al aan het rijden op een stuk zonder melder, dus niet door deze wissel (BUG #406).");
                            if (_monitor.Bezet.Count != 1) { try { await Herparkeer(r2, alleBlokken: true); } catch (PlanFout) { } }
                            var b2 = _monitor.Bezet.ToList();
                            if (b2.Count > 0) return b2;
                        }
                    }
                    await ZetAdres(a, oud);
                }
            }
            if (!veranderd && _monitor.Bezet.Count == 0 && n == 2)
            {
                var om = _adressen.Where(Fysiek).ToList();
                if (om.Count > 0)
                {
                    _log.Waarschuwing($"De loc staat waarschijnlijk zonder stroom (nog steeds op melder {_k.LocMelder}?): wissel {string.Join(", ", om)} terug in de basisstand, daarna verder zoeken (BUG #403, nog niet getest op de baan).");
                    foreach (var a in om) await ZetAdres(a, false);
                }
            }
            if (veranderd || _monitor.Bezet.Count > 0)
            {
                if (_monitor.Bezet.Count != 1) { try { await Herparkeer(r, alleBlokken: true); } catch (PlanFout) { } }
                var b = _monitor.Bezet.ToList();
                if (b.Count > 0) { _log.Vondst($"Loc gevonden: melder {string.Join(", ", b)} (BUG #396)."); return b; }
            }
        }
        return new List<int>();
    }

    /// <summary>WAT: de loc zoeken door de rijstroom per blok te geven: eerst elk blok rond de laatste melder alleen (beide richtingen), daarna twee blokken
    /// tegelijk in tegengestelde richting. Na elke poging even kijken of een melder de loc weer ziet.
    /// WAAROM (BUG #408, Marco 12-11 16:21: "daar zit ook een sectiescheiding waardoor hij vaak in 2 secties staat"): om 15:35 stond de loc op de grens van
    /// melder 12 (blok 2) en melder 1 (blok 1). Met hetzelfde rijcommando naar blok 1 en 2 werden melder 1 én 12 vrij en reed de loc niet meer, ook niet met
    /// rijstroom op alle blokken. Over de scheiding kan het commando in de twee blokken elkaar tegenwerken; één blok alleen (of tegengesteld) kan hem weer laten rijden.</summary>
    private async Task<List<int>> ZoekPerBlok408(bool alleenBijVerandering = false)
    {
        if (!_hw.LocCommandoVereistBlok) return new List<int>();
        var melders = new List<int> { _k.LocMelder };
        if (_k.LocVia > 0) melders.Add(_k.LocVia);
        foreach (var r0 in new[] { Richting.Vooruit, Richting.Achteruit }) foreach (var b in _k.KantBuren(_k.LocMelder, r0)) if (!melders.Contains(b)) melders.Add(b);
        var blokken = new List<int>();
        foreach (var m in melders) if (_k.Blokken.TryGetValue(m, out var bl) && !blokken.Contains(bl)) blokken.Add(bl);
        // BUG #415 (Marco 14-11 09:33): van melder 10 en zijn buren was het blok nog niet bekend, en toen werd het zoeken per blok helemaal overgeslagen.
        // Dan elk blok alleen proberen (achteruit en vooruit); paren van blokken dan niet (alle paren zou te lang duren). Op de baan werkte om 09:03 juist
        // "alleen blok 3".
        bool onbekend415 = blokken.Count == 0;
        if (onbekend415) blokken = AlleBlokken().ToList();
        if (blokken.Count == 0) return new List<int>();
        if (!onbekend415) blokken = blokken.Take(3).ToList();
        _log.Waarschuwing($"De loc staat waarschijnlijk op een sectiescheiding bij melder {_k.LocMelder}: rijstroom per blok proberen (blok {string.Join(", ", blokken)}) (BUG #408, nog niet getest op de baan).");
        int v = BaanVerkenner.ZoekKruipSnelheid(Math.Max(1, _ins.Kruipsnelheid), _ins.Verkensnelheid, 3);
        var pogingen = new List<(int blok, Richting r, int? tweede)>();
        foreach (var b in blokken) foreach (var r in new[] { Richting.Achteruit, Richting.Vooruit }) pogingen.Add((b, r, null));
        if (!onbekend415) for (int i = 0; i < blokken.Count; i++) for (int j = 0; j < blokken.Count; j++) if (i != j) pogingen.Add((blokken[i], Richting.Vooruit, blokken[j]));
        // BUG #410: daarna het blok van de loc tegen elk ander blok (het blok van de buurmelder kan nog onbekend zijn)
        // (ook als er wel twee bekend zijn: in de simulatie lag de scheiding bij 129 tegen blok 2 van melder 4, waarvan het blok nog niet bekend was)
        foreach (var ander in AlleBlokken().Where(x => !blokken.Contains(x)))
            {
                pogingen.Add((blokken[0], Richting.Vooruit, ander));
                pogingen.Add((ander, Richting.Vooruit, blokken[0]));
            }
        // BUG #410: wat hier eerder werkte als eerste proberen
        if (_k.BlokTruc.TryGetValue(_k.LocMelder, out var truc))
        {
            var d = truc.Split(',');
            if (d.Length >= 2 && int.TryParse(d[0], out var tb) && Enum.TryParse<Richting>(d[1], out var tr))
            {
                (int, Richting, int?) eerst = (tb, tr, d.Length > 2 && int.TryParse(d[2], out var t2) ? t2 : null);
                pogingen.Remove(eerst); pogingen.Insert(0, eerst);
                _log.Info($"Bij melder {_k.LocMelder} werkte eerder: {truc} (BUG #410).");
            }
        }
        int startMelder = _k.LocMelder;
        foreach (var (b, r, tweede) in pogingen)
        {
            _token.ThrowIfCancellationRequested();
            _monitor.Bijwerken();
            var t0 = _klok.Nu;
            StuurSnelheid(v, r, new[] { b });
            if (tweede is int b2) StuurSnelheid(v, r.Om(), new[] { b2 });
            bool gezien = false;
            while (_klok.Nu - t0 < TimeSpan.FromSeconds(6))
            {
                await _klok.Wacht(Poll);
                if (Kortsluiting()) break;
                if (_monitor.Bijwerken().Any(x => x.Tijd >= t0) || (!alleenBijVerandering && _monitor.Bezet.Count > 0)) { gezien = true; break; }
            }
            await Stop();
            _monitor.Bijwerken();
            if (Kortsluiting()) { await HerstelKortsluiting(new Dictionary<int, bool>(), r); _monitor.Bijwerken(); }
            if (gezien || (!alleenBijVerandering && _monitor.Bezet.Count > 0))
            {
                _k.BlokTruc[startMelder] = tweede is int t3 ? $"{b},{r},{t3}" : $"{b},{r}";
                string wat = tweede is int t ? $"blok {b} {r.Tekst()} en blok {t} {r.Om().Tekst()}" : $"alleen blok {b} {r.Tekst()}";
                _log.Vondst($"Loc weer gezien met rijstroom op {wat}: melder {string.Join(", ", _monitor.Bezet)} (BUG #408).");
                // WAT: staat de loc nog op twee melders, dan met HETZELFDE commando dat werkte verder kruipen tot er nog één melder bezet is.
                // WAAROM (BUG #413, Marco 14-11 09:03): op de baan werkte "alleen blok 3 achteruit" (loc op 14 en 129), maar het netjes parkeren daarna gaf
                // rijstroom naar alle blokken, en dat gaf meteen kortsluiting op de scheiding 14/129.
                if (_monitor.Bezet.Count > 1)
                {
                    var t1 = _klok.Nu;
                    StuurSnelheid(v, r, new[] { b });
                    if (tweede is int b3) StuurSnelheid(v, r.Om(), new[] { b3 });
                    while (_klok.Nu - t1 < TimeSpan.FromSeconds(10) && _monitor.Bezet.Count > 1 && !Kortsluiting()) { await _klok.Wacht(Poll); _monitor.Bijwerken(); }
                    await Stop();
                    _monitor.Bijwerken();
                    if (Kortsluiting()) { await HerstelKortsluiting(new Dictionary<int, bool>(), r); _monitor.Bijwerken(); }
                    _log.Info($"Met hetzelfde commando doorgekropen: nu melder {string.Join(", ", _monitor.Bezet)} (BUG #413, nog niet getest op de baan).");
                }
                if (_monitor.Bezet.Count == 0) { try { await Herparkeer(r, alleBlokken: true); } catch (PlanFout) { } }
                var res = _monitor.Bezet.ToList();
                if (res.Count > 0) return res;
            }
        }
        return new List<int>();
    }

    /// <summary>BUG #405: de wissels die waarschijnlijk bij melder m liggen: eerst die met een zone of relevantie bij m, daarna de rest.</summary>
    private List<int> KandidatenStroom405(int m)
    {
        var dichtbij = new List<int>();
        foreach (var a in _adressen)
        {
            if (_k.Zone(a).Contains(m)) { dichtbij.Add(a); continue; }
            if (_k.Relevant.Any(kv => kv.Value.Contains(a) && (kv.Key.Contains($">{m}:") || kv.Key.StartsWith($"{m}>")))) dichtbij.Add(a);
        }
        return dichtbij.Concat(_adressen.Except(dichtbij)).ToList();
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
        // WAT: is er niets te plannen, probeer dan één keer de andere aanname over hoe de loc staat (omgedraaid of niet). De eerste rit controleert dat meteen (#379/#386).
        // WAAROM (BUG #388, Marco 09-11 12:03: "je vraagt nog steeds om de loc op een bepaalde melder te zetten, terwijl je er gewoon zelf naar toe kan rijden"; log 09:26-09:28 en
        // 12:01-12:03): na de keerlus stond de loc volgens de kennis "omgedraaid". Vanaf 10, 129, 4, 11 en 1 vond de planning geen enkele weg naar de buitenring meer; na zes keer
        // "niet te plannen" stopte de verkenning als niet af, en dan vraagt het scherm om de loc op de startmelder te zetten.
        if (onbereikbaar.Count > 0 && Environment.GetEnvironmentVariable("MARCO2_DEBUG") == "1")
            _log.Info($"DEBUG bereikbaar vanaf {Hier}: {string.Join(" ", Dijkstra().Kosten.Keys.Select(x => $"{x.M}<{x.Via}{(x.H == Richting.Vooruit ? "V" : "A")}").Distinct().OrderBy(x => x))}");
        // BUG #390 (Marco 09-11 14:03: "hoe kan het nou dat je vindt dat de loc andersom rijdt, dat is echt onmogelijk"): de loc draait nooit om. Deze gok is daarom
        // uitgezet; sinds #389 vindt de planning zelf de weg uit de keerlus (129 -> 24 -> 21).
        if (onbereikbaar.Count > 0 && !_andereAannameGeprobeerd && false)
        {
            _andereAannameGeprobeerd = true;
            _k.Omgedraaid = !_k.Omgedraaid;
            _k.LocRichting = _k.LocRichting.Om();
            _k.LocVia = 0;
            _omwegCache.Clear();
            _log.Info($"Vanaf melder {_k.LocMelder} is niets te plannen; ik probeer de andere aanname over de stand van de loc (omgedraaid: {(_k.Omgedraaid ? "ja" : "nee")}) (BUG #388, nog niet getest op de baan).");
            var opnieuw = KiesTaak();
            if (opnieuw is not null) return opnieuw;
            _k.Omgedraaid = !_k.Omgedraaid;
            _k.LocRichting = _k.LocRichting.Om();
            _omwegCache.Clear();
            return null;
        }
        if (onbereikbaar.Count > 0)
        {
            _log.Info($"Nog {onbereikbaar.Count} taak/taken die vanaf melder {_k.LocMelder} nu niet te plannen zijn: {string.Join("; ", onbereikbaar.Take(4))}{(onbereikbaar.Count > 4 ? " …" : "")}. Laatste reden: {_planFout}.");
            _onbereikbaar = onbereikbaar.Count;
        }
        return null;
    }
    private int _onbereikbaar;
    /// <summary>BUG #404: de laatste taak faalde doordat een wissel op de plek van de loc niet om mocht.</summary>
    private bool _nietHier404;
    private int _syncAchterElkaar407;
    internal int _nietWeg410;
    private readonly Dictionary<string, int> _combinatie411 = new();   // BUG #411: hoe vaak per stap de conclusie 'combinatie' viel   // BUG #410: melder waar de loc niet weg kwam (geen doodlopend), 0 = niets   // BUG #407: hoogstens 3 keer achter elkaar de plaats bijstellen (geen eindeloze lus)
    private bool _herkansing404;
    /// <summary>BUG #402: tekst als de loc kwijt is (dan is de verkenning nooit voltooid).</summary>
    private string? _locKwijt402;
    private int _nietNetjesAchterElkaar;
    /// <summary>BUG #388: al geprobeerd met de andere aanname over de stand van de loc (wordt teruggezet zodra een taak lukt).</summary>
    private bool _andereAannameGeprobeerd;
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
                // BUG #388: zit de loc in een deel van de baan waar hij volgens de kennis niet uit kan (bijv. de binnenring met de keerlus, terwijl de buitenring wel bekend is),
                // dan ook een uitweg zoeken bij kortsluitingen in dat deel, ook als er vanaf die melder al een andere doorgang bekend is.
                var bereik388 = Dijkstra().Kosten.Keys.Select(x => x.M).ToHashSet();
                bool afgesneden388 = _k.Melders.Any(m => !bereik388.Contains(m) && !_k.Spookmelders.Contains(m));
                foreach (var g in _k.Stappen.Where(s => !s.Afgeleid && !s.Terug && s.Via > 0 && s.Uitkomst == StapUitkomst.Kortsluiting).GroupBy(s => s.IngangSleutel))
                {
                    var s0 = g.First();
                    // alleen als er vanuit deze melder in deze richting nog géén enkele doorgang bekend is
                    if (_k.Stappen.Any(s => s.Melder == s0.Melder && s.Richting == s0.Richting && s.Uitkomst != StapUitkomst.Kortsluiting)
                        && !(afgesneden388 && bereik388.Contains(s0.Melder))) continue;
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
        bool aGezien = false;   // BUG #380: kwam de eerste uitkomst in deze ronde nog terug?

        // WAT: rijdt de stap met precies stand A nog steeds naar de eerste uitkomst? Zo niet, dan was die oude waarneming niet (meer) betrouwbaar: vergeten, geen wissel.
        // WAAROM (BUG #380, Marco 05-11, module 2 op de baan, log 08:34-08:36): de stap 29 -> 21 achteruit was eens naar 129 gegaan en later naar 24. Bij het halveren gaf ELKE
        // proef 24, dus kwam steeds de eerste helft "schuldig" uit: adres 4, 10 en 12 werden na elkaar als wissel gevonden (4 en 12 bestaan niet). Stand A zelf werd nooit
        // opnieuw gereden.
        async Task<bool> EersteUitkomstKlopt()
        {
            _log.Stap($"Controle: de eerste uitkomst ({StapUitkomst.Tekst(a.Uitkomst)}) nog eens met precies die stand.");
            var u0 = await Proef(t.Via, t.Melder, t.Richting, new Dictionary<int, bool>(basis), herstel: new Dictionary<int, bool>());
            if (u0 is null || u0.Value == a.Uitkomst) return true;
            _log.Waarschuwing($"Stap {t.Via} -> {t.Melder} ({t.Richting.Tekst()}): met dezelfde stand als toen nu {StapUitkomst.Tekst(u0.Value)} in plaats van {StapUitkomst.Tekst(a.Uitkomst)}. De oude waarneming wordt vergeten; geen wissel toegeschreven (BUG #380, nog niet getest op de baan).");
            _k.Stappen.Remove(a);
            return false;
        }
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
            else { kandidaten = kandidaten.Except(helft).ToList(); uitkomstLos = null; aGezien = true; }
        }
        if (kandidaten.Count != 1) return false;
        int adres = kandidaten[0];
        // BUG #417 (Marco 14-11 11:49-11:51): ook zonder halveerproeven eerst controleren of stand A nog steeds de eerste uitkomst geeft. Er was één oude
        // waarneming "29 -> 21 -> 14" (24 niet gezien); daarna gaf elke losse controle 24, en zo werden na elkaar adres 4, 11, 12 en 2 "gevonden" bij melder 21.
        if (!aGezien && !await EersteUitkomstKlopt()) return true;
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
                // WAT: de tweede keer dezelfde combinatie bij dezelfde stap: de waarneming NIET meer vergeten. Er zijn dan twee uitkomsten voor (bijna) dezelfde
                // stand, en dan gebruikt de routeplanner deze stap met die stand niet meer om te navigeren.
                // WAAROM (BUG #411, simulatie met sectiescheiding, start 17: 349 keer dezelfde lus): de dekkingsrit reed 21 -> 24 -> 129 met 9 en 10 afbuigend,
                // de kruiswissel gaf kortsluiting, de proef concludeerde "combinatie" en vergat de kortsluiting, en de volgende dekkingsrit deed precies hetzelfde.
                string sleutel411 = "U|" + Kennis2.Sleutel(t.Via, t.Melder, t.Richting);
                _combinatie411[sleutel411] = _combinatie411.GetValueOrDefault(sleutel411) + 1;
                if (_combinatie411[sleutel411] >= 2 && false)   // BUG #411: uitgezet, maakte start 17 en 25 (zonder scheiding) veel slechter (720 min)
                    _log.Waarschuwing($"Het verschil bij {t.Via} -> {t.Melder} ({t.Richting.Tekst()}) komt weer door een combinatie; de waarneming met {StapUitkomst.Tekst(b.Uitkomst)} (adressen {string.Join(",", b.Stand)} afbuigend) blijft nu staan, zodat deze stap met die stand niet meer gepland wordt (BUG #411).");
                else
                {
                    _log.Waarschuwing($"Het verschil bij {t.Via} -> {t.Melder} ({t.Richting.Tekst()}) komt niet door één adres maar door een combinatie; de waarneming met {StapUitkomst.Tekst(b.Uitkomst)} (adressen {string.Join(",", b.Stand)} afbuigend) wordt vergeten.");
                    _k.Stappen.Remove(b);
                }
                // BUG #397 (simulatie met de loc op 25 bij de start: 12 uur lang dezelfde lus): de dekkingsrit zette steeds weer alle open adressen tegelijk om over dit stuk,
                // de kruiswissel gaf dan door een combinatie (9 en 10 samen) kortsluiting, de proef vond geen los adres en vergat de waarneming, en zo eindeloos. Nu gelden de
                // adressen die hier verschilden een groep: een dekkingsrit die over dit stuk komt, zet er hoogstens één van om (zo wordt elk apart getest, niet meer samen).
                var stukken397 = _k.Stappen.Where(x => x.Melder == t.Melder && x.Richting == t.Richting && x.Uitkomst > 0).Select(x => (t.Melder, x.Uitkomst)).Append((t.Via, t.Melder)).Distinct().ToList();
                foreach (var (van, naar) in stukken397) _k.NietSamen.Add($"{string.Join(",", alleVerschil.OrderBy(x => x))}|{van}>{naar}");
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
        // BUG #404: ook niet voltooid als er (na de herkansing) meer dan 2 taken overgeslagen zijn (09:20:48: 8 taken binnen een halve seconde twee keer mislukt en
        // overgeslagen, toch "voltooid"). Eén of twee overgeslagen taken komen ook in een complete verkenning voor (bijv. een uitweg na een kortsluiting die er echt
        // niet is); die staan als open punt in het rapport.
        k.Voltooid = zonderBlok.Count == 0 && _locKwijt402 is null && _onbereikbaar == 0 && _k.Overgeslagen.Count <= 2;
        if (_k.Overgeslagen.Count > 2) _log.Waarschuwing($"De verkenning is nog NIET af: {_k.Overgeslagen.Count} taken zijn overgeslagen (BUG #404).");
        // BUG #402: niet voltooid als de loc kwijt is of als er nog taken zijn die niet te plannen waren
        if (_locKwijt402 is not null && !k.Waarschuwingen.Contains(_locKwijt402)) k.Waarschuwingen.Add(_locKwijt402);
        if (_onbereikbaar > 0)
        {
            var w402 = $"De verkenning is nog NIET af: {_onbereikbaar} taak/taken konden vanaf melder {_k.LocMelder} niet gepland worden (BUG #402).";
            if (!k.Waarschuwingen.Contains(w402)) k.Waarschuwingen.Add(w402);
            _log.Waarschuwing(w402);
        }
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

    /// <summary>BUG #384: laatste keer dat de kaart tijdens een rit bewaard is (hoogstens elke 5 s).</summary>
    private DateTime _laatsteLiveBewaard = DateTime.MinValue;

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
