using System.Text.Json;
using Modeltreinbesturing.Hardware;

namespace Baanverkenner.Kern;

/// <summary>Eén verkenopdracht: vanaf melder Start, in Richting, met de wissels in
/// Configuratie de basisrit rijden en daarna elk nog onbekend adres één voor één omzetten
/// en dezelfde rit opnieuw rijden.</summary>
public class Opdracht
{
    public int Id { get; set; }
    public Configuratie Configuratie { get; set; } = Configuratie.Basis;
    public int Start { get; set; }
    public Richting Richting { get; set; }
    /// <summary>Hoe de loc vanaf de startmelder van de verkenning hier komt.</summary>
    public List<Etappe> Route { get; set; } = new();
    public string Reden { get; set; } = "";
    /// <summary>Voor hervatten: het eerste adres dat nog getest moet worden.</summary>
    public int VolgendAdres { get; set; }
    public int Pogingen { get; set; }
    /// <summary>Gezet als deze opdracht de andere tak verkent van een wissel die eerder
    /// alleen van achteren gevonden werd (kortsluiting bij omzetten). De basisrit legt dan
    /// meteen vast waar die tak heen gaat.</summary>
    public int? BronAdres { get; set; }
    public int? BronRechtdoorVolgende { get; set; }
    /// <summary>BUG #39: als deze opdracht zelf geen nieuwe melder bereikt (de basisrit blijft
    /// meteen stilstaan), worden de nog niet geïdentificeerde adressen toch, één voor één,
    /// getest - in plaats van de opdracht over te slaan. Zo kan een tweede wissel die, samen
    /// met een al afbuigend gezette wissel, een anders doodlopende tak alsnog opent, gevonden
    /// worden.</summary>
    public bool TestCombinaties { get; set; }
    public string Sleutel => $"{Configuratie.Sleutel}|{Start}|{Richting}";
}

/// <summary>Alles wat nodig is om een onderbroken verkenning te hervatten.</summary>
public class VerkenStatus
{
    public VerkenInstellingen Instellingen { get; set; } = new();
    public Baankaart Kaart { get; set; } = new();
    public List<Opdracht> Wachtrij { get; set; } = new();
    public Opdracht? Huidig { get; set; }
    public List<string> GeplandeOpdrachten { get; set; } = new();
    public List<string> GetesteOvergangen { get; set; } = new();
    public List<int> Geidentificeerd { get; set; } = new();
    public int VolgendeOpdrachtId { get; set; } = 1;
    public int VolgendeKortsluitId { get; set; } = 1;
    public int AfgerondeOpdrachten { get; set; }
    public int KortsluitingenTotaal { get; set; }

    public string AlsJson() => JsonSerializer.Serialize(this, Baankaart.JsonOpties);
    public static VerkenStatus? VanJson(string json) => JsonSerializer.Deserialize<VerkenStatus>(json, Baankaart.JsonOpties);
}

public record VerkenVoortgang(
    string Fase,
    string Bezig,
    int AfgerondeOpdrachten,
    int OpenOpdrachten,
    int GevondenMelders,
    int GevondenWissels,
    int TeTestenAdressen,
    int Kortsluitingen);

/// <summary>
/// De Baanverkenner: brengt met één testloc de bezetmelders, hun volgorde, de wissels
/// (alle adressen één voor één), kopsporen, lussen en - bij Dinamo - de koppeling tussen
/// secties en blokken volledig zelf in kaart.
///
/// Werkwijze in het kort:
/// 1. Startmelder bepalen (waar staat de loc), bij Dinamo het blok daarvan zoeken.
/// 2. Alle adressen in het bereik op rechtdoor zetten.
/// 3. Opdrachten afwerken. Een opdracht = vanaf een melder in één richting de basisrit
///    rijden, en dan per nog onbekend adres: omzetten, zelfde rit, vergelijken, terug.
///    Wijkt de rit af na melder X, dan ligt die wissel (splitsend) na X; geeft omzetten
///    meteen kortsluiting, dan wordt hij van achteren bereden. Elke nieuwe tak en elk
///    kopspoor (andersom terug) wordt een nieuwe opdracht.
/// 4. Kortsluitpunten oplossen met de wissels die inmiddels bekend zijn.
/// 5. Adressen die nergens effect hadden apart vermelden, blokken voorstellen.
/// </summary>
public partial class BaanVerkenner
{
    private readonly IHardwareInterface _hw;
    private readonly VerkenInstellingen _ins;
    private readonly IKlok _klok;
    private readonly VerkenLog _log;
    private readonly MelderMonitor _monitor;
    private readonly Func<string, string, Task<bool>> _vraag;
    private readonly VerkenStatus _status;
    private Baankaart _kaart => _status.Kaart;

    private CancellationToken _token;
    private volatile bool _gepauzeerd;
    private int _kortsluitingenTotaal
    {
        get => _status.KortsluitingenTotaal;
        set => _status.KortsluitingenTotaal = value;
    }
    private string _fase = "Voorbereiden";
    private string _bezig = "";

    /// <summary>Wordt na elke belangrijke stap aangeroepen met de actuele status, zodat het
    /// programma een voortgangsbestand kan wegschrijven (hervatten na onderbreking).</summary>
    public Action<VerkenStatus>? Bewaren { get; set; }
    public event Action<VerkenVoortgang>? VoortgangGewijzigd;

    /// <summary>BUG #59: geleerde tijden uit eerdere verkenningen (mag null zijn).</summary>
    public LeerProfiel? Leerprofiel { get; set; }
    /// <summary>Wordt na elke rit aangeroepen zodat het programma het profiel kan bewaren.</summary>
    public Action<LeerProfiel>? ProfielBijgewerkt { get; set; }

    public VerkenLog Log => _log;
    public Baankaart Kaart => _kaart;
    public VerkenStatus Status => _status;
    public bool Gepauzeerd => _gepauzeerd;

    /// <param name="vraag">Stelt de gebruiker een vraag (titel, tekst); true = OK/doorgaan,
    /// false = stoppen.</param>
    /// <param name="hervatVan">Eerder bewaarde status om te hervatten, of null.</param>
    public BaanVerkenner(IHardwareInterface hw, VerkenInstellingen ins, IKlok klok, VerkenLog log,
        Func<string, string, Task<bool>> vraag, VerkenStatus? hervatVan = null)
    {
        _hw = hw;
        _klok = klok;
        _log = log;
        _vraag = vraag;
        _status = hervatVan ?? new VerkenStatus { Instellingen = ins };
        _ins = _status.Instellingen;
        _monitor = new MelderMonitor(klok, () => _ins.OntdenderMs);
    }

    /// <summary>Snelheden aanpassen terwijl de verkenning loopt. Geldt vanaf het
    /// eerstvolgende snelheidscommando (de verkenner leest de waarden steeds opnieuw).</summary>
    public void PasSnelhedenAan(int verkensnelheid, int kruipsnelheid)
    {
        if (verkensnelheid == _ins.Verkensnelheid && kruipsnelheid == _ins.Kruipsnelheid) return;
        _log.Info($"Snelheden aangepast: verkensnelheid {_ins.Verkensnelheid} → {verkensnelheid}, kruipsnelheid {_ins.Kruipsnelheid} → {kruipsnelheid}.");
        _ins.Verkensnelheid = verkensnelheid;
        _ins.Kruipsnelheid = kruipsnelheid;
    }

    public void Pauzeer() { _gepauzeerd = true; _log.Info("Pauze gevraagd - de loc stopt."); MeldVoortgang(); }
    public void Hervat() { _gepauzeerd = false; _log.Info("Verkenning hervat."); MeldVoortgang(); }

    private async Task WachtTotHervat()
    {
        _fase = "Gepauzeerd";
        MeldVoortgang();
        while (_gepauzeerd) await Wacht(TimeSpan.FromMilliseconds(200));
    }

    private async Task PauzeMoment()
    {
        if (_gepauzeerd) await WachtTotHervat();
    }

    // =====================================================================
    // Hoofdlijn
    // =====================================================================

    public async Task VoerUit(CancellationToken token)
    {
        _token = token;
        _hw.BezetmeldingGewijzigd += _monitor.Ontvang;
        _hw.KortsluitingStatusGewijzigd += HardwareMeldtKortsluiting;
        if (_hw is IBlokAlarmBron alarmBron) alarmBron.BlokAlarmGewijzigd += HardwareMeldtBlokAlarm;
        try
        {
            bool hervat = _status.Kaart.StartMelder != 0;
            if (!hervat)
            {
                _kaart.Gestart = _klok.Nu;
                _kaart.Hardware = _hw.Naam;
                _kaart.LocAdres = _ins.LocAdres;
                _kaart.LocStappen = _ins.LocStappen;
                _kaart.WisselAdresVan = _ins.WisselAdressen().First();
                _kaart.WisselAdresTot = _ins.WisselAdressen().Last();
            }
            if (_kaart.RefSnelheid <= 0) _kaart.RefSnelheid = _ins.Verkensnelheid;
            _startVerkensnelheid = _ins.Verkensnelheid;
            _log.Stap(hervat ? "Verkenning wordt hervat." : "Verkenning start.");
            if (Leerprofiel is { AantalMetingen: > 0 } lp) _log.Info($"Leerprofiel: {lp.Overgangen.Count} overgangen uit eerdere verkenningen (tijden worden omgerekend naar verkensnelheid {_ins.Verkensnelheid}).");
            _log.Info($"Hardware: {_hw.Naam}. Testloc adres {_ins.LocAdres} ({_ins.LocStappen} stappen), verkensnelheid {_ins.Verkensnelheid}, kruipsnelheid {_ins.Kruipsnelheid}.");
            _log.Info($"Wisseladressen {_kaart.WisselAdresVan} t/m {_kaart.WisselAdresTot}." + (BlokVereist ? $" Dinamo-blokken: {_ins.DinamoBlokken}." : ""));

            await BepaalStart(hervat);

            _fase = "Wissels in beginstand";
            MeldVoortgang();
            _log.Stap("Alle adressen in het bereik op rechtdoor zetten (commando gestuurd; de centrale meldt de werkelijke stand van de wisselmotoren niet terug).");
            foreach (var a in _ins.WisselAdressen()) await ZetWissel(a, false, forceer: true);

            if (!hervat)
            {
                Plan(new Opdracht { Configuratie = Configuratie.Basis, Start = _kaart.StartMelder, Richting = Richting.Vooruit, Reden = "start, vooruit" });
                Plan(new Opdracht { Configuratie = Configuratie.Basis, Start = _kaart.StartMelder, Richting = Richting.Achteruit, Reden = "start, achteruit" });
            }
            else if (_status.Huidig is { } onderbroken)
            {
                _status.Wachtrij.Insert(0, onderbroken);
                _status.Huidig = null;
            }

            while (true)
            {
                while (_status.Wachtrij.Count > 0)
                {
                    if (_status.AfgerondeOpdrachten >= _ins.MaxOpdrachten)
                    {
                        _kaart.Waarschuwingen.Add($"Veiligheidsgrens van {_ins.MaxOpdrachten} opdrachten bereikt - verkenning afgebroken.");
                        _log.Waarschuwing(_kaart.Waarschuwingen[^1]);
                        _status.Wachtrij.Clear();
                        break;
                    }
                    var o = _status.Wachtrij[0];
                    _status.Wachtrij.RemoveAt(0);
                    _status.Huidig = o;
                    BewaarNu();
                    await VoerOpdrachtUit(o);
                    _status.Huidig = null;
                    _status.AfgerondeOpdrachten++;
                    BewaarNu();
                }
                if (!await LosKortsluitpuntenOp()) break;
            }

            Afronden();
            _log.Stap("Verkenning voltooid.");
        }
        catch (OperationCanceledException)
        {
            _log.Waarschuwing("Verkenning gestopt. De voortgang is bewaard; later hervatten kan.");
            try { _hw.Noodstop(); StopZonderWachten(); } catch { }
            throw;
        }
        catch (Exception ex)
        {
            _log.Fout($"Onverwachte fout: {ex.Message}");
            try { _hw.Noodstop(); StopZonderWachten(); } catch { }
            throw;
        }
        finally
        {
            BewaarNu();
            _hw.BezetmeldingGewijzigd -= _monitor.Ontvang;
            _hw.KortsluitingStatusGewijzigd -= HardwareMeldtKortsluiting;
            if (_hw is IBlokAlarmBron alarmBron2) alarmBron2.BlokAlarmGewijzigd -= HardwareMeldtBlokAlarm;
            MeldVoortgang();
        }
    }

    private void StopZonderWachten()
    {
        if (!BlokVereist) { _hw.ZetLocSnelheid(_ins.LocAdres, 0, true, 0, _ins.LocStappen); return; }
        foreach (var b in _ins.DinamoBlokLijst()) _hw.ZetLocSnelheid(_ins.LocAdres, 0, true, b, _ins.LocStappen);
    }

    /// <summary>Noodstop van buitenaf (knop in het venster).</summary>
    public void Noodstop()
    {
        _hw.Noodstop();
        StopZonderWachten();
        _log.Waarschuwing("NOODSTOP door gebruiker.");
    }

    // =====================================================================
    // Start
    // =====================================================================

    private async Task BepaalStart(bool hervat)
    {
        _fase = "Startpositie bepalen";
        MeldVoortgang();

        if (_hw.KanMelderStatusOpvragen)
        {
            _log.Stap($"Stand van melder 1 t/m {_ins.HoogsteMelder} opvragen…");
            for (int m = 1; m <= _ins.HoogsteMelder; m++) _hw.VraagMelderStatusOp(m);
            await Wacht(DinamoCyclus * (_ins.HoogsteMelder + 5) + TimeSpan.FromMilliseconds(_ins.OntdenderMs));
        }
        else await Wacht(TimeSpan.FromMilliseconds(_ins.OntdenderMs + 500));
        _monitor.Bijwerken();

        if (hervat)
        {
            int s = _kaart.StartMelder;
            var bezetNu = _monitor.Bezet;
            if (!(bezetNu.Count == 1 && bezetNu.Contains(s)))
                await VraagLocTerugTeZetten(s, $"De verkenning wordt hervat vanaf de startmelder {s}.");
            return;
        }

        var bezet = _monitor.Bezet;
        if (bezet.Count > 1)
        {
            _log.Waarschuwing($"Meerdere melders bezet: {Lijst(bezet)}.");
            bool verder = await _vraag("Meerdere melders bezet",
                $"Er zijn meerdere melders bezet ({Lijst(bezet)}).\n\nAlleen de testloc mag op de baan staan, helemaal binnen één melder. Haal andere voertuigen weg of zet de loc iets op, en klik OK.");
            if (!verder) throw new OperationCanceledException();
            if (_hw.KanMelderStatusOpvragen) foreach (var m in bezet) _hw.VraagMelderStatusOp(m);
            await Wacht(TimeSpan.FromSeconds(2));
            _monitor.Bijwerken();
            bezet = _monitor.Bezet;
        }

        if (bezet.Count == 1)
        {
            _kaart.StartMelder = bezet.First();
            _log.Vondst($"De loc staat op melder {_kaart.StartMelder}: dit is de startmelder.");
        }
        else if (bezet.Count == 0)
        {
            _log.Info("Geen bezette melder bekend (de centrale meldt alleen wijzigingen). De loc gaat zelf op zoek.");
            _kaart.StartMelder = await ZoekEersteMelder();
        }
        else throw new InvalidOperationException($"Er zijn nog steeds meerdere melders bezet ({Lijst(bezet)}).");

        _kaart.Melder(_kaart.StartMelder);
        if (BlokVereist && BlokVan(_kaart.StartMelder) is null)
            if (!await BlokZoekenBijStart(_kaart.StartMelder))
                throw new InvalidOperationException("Geen Dinamo-blok gevonden voor de startmelder.");
        BewaarNu();
    }

    private async Task<int> ZoekEersteMelder()
    {
        foreach (var r in new[] { Richting.Vooruit, Richting.Achteruit })
        {
            if (BlokVereist)
            {
                foreach (var blok in _ins.DinamoBlokLijst())
                {
                    _monitor.Bijwerken();
                    await StuurNaarBlok(blok, r, _ins.Verkensnelheid);
                    var eind = _klok.Nu + TimeSpan.FromSeconds(_ins.BlokproefLangSeconden);
                    while (_klok.Nu < eind)
                    {
                        await Wacht(Poll);
                        var w = _monitor.Bijwerken().FirstOrDefault(x => x.Bezet);
                        if (w is not null)
                        {
                            await StuurNaarBlok(blok, r, 0);
                            await VolledigeStop();
                            _log.Vondst($"Loc gevonden op melder {w.Melder} (blok {blok} liet hem rijden).");
                            // Dit blok voedde de plek waar de loc stond; niet zeker dezelfde
                            // sectie als de nieuwe melder, dus niet koppelen - de blokproef
                            // doet dat straks netjes.
                            await Nastellen(w.Melder, r);
                            return w.Melder;
                        }
                    }
                    await StuurNaarBlok(blok, r, 0);
                }
            }
            else
            {
                await StuurSnelheid(r, _ins.Kruipsnelheid);
                var eind = _klok.Nu + TimeSpan.FromSeconds(_ins.MaxSecondenTussenMelders);
                while (_klok.Nu < eind)
                {
                    await Wacht(Poll);
                    var w = _monitor.Bijwerken().FirstOrDefault(x => x.Bezet);
                    if (w is not null)
                    {
                        await StopLoc();
                        await Nastellen(w.Melder, r);
                        _log.Vondst($"Loc gevonden op melder {w.Melder}.");
                        return w.Melder;
                    }
                }
                await StopLoc();
            }
        }
        throw new InvalidOperationException("De loc liet geen enkele melder bezet worden. Controleer locadres, rijstappen en of de loc op een bewaakte sectie staat.");
    }

    // =====================================================================
    // Opdrachten
    // =====================================================================

    private void Plan(Opdracht o)
    {
        if (_status.GeplandeOpdrachten.Contains(o.Sleutel)) return;
        o.Id = _status.VolgendeOpdrachtId++;
        o.VolgendAdres = _ins.WisselAdressen().First();
        _status.GeplandeOpdrachten.Add(o.Sleutel);
        _status.Wachtrij.Add(o);
        _log.Info($"Nieuwe opdracht #{o.Id}: vanaf melder {o.Start} {o.Richting.Tekst()} ({o.Configuratie}) - {o.Reden}.");
    }

    private static List<Etappe> RouteMet(List<Etappe> basis, Configuratie c, Richting r, List<int> pad)
    {
        var res = basis.ToList();
        if (pad.Count >= 2)
            res.Add(new Etappe { Configuratie = c, Richting = r, Van = pad[0], Naar = pad[^1], Pad = pad.ToList() });
        return res;
    }

    private async Task VoerOpdrachtUit(Opdracht o)
    {
        _fase = $"Opdracht #{o.Id}";
        _bezig = $"vanaf melder {o.Start} {o.Richting.Tekst()} ({o.Configuratie})";
        MeldVoortgang();
        _log.Stap($"Opdracht #{o.Id}: {_bezig} - {o.Reden}.");

        try
        {
            await NaarStartVan(o.Route);
            await ZetConfiguratie(o.Configuratie);

            // ---- Basisrit ----
            var basis = await Basisrit(o);
            if (basis is null) { await TerugNaarHuis(o.Route); return; }

            if (o.BronAdres is int bron && basis.Reeks.Count >= 2
                && !_kaart.Wissel(bron).Waarnemingen.Any(w => w.Soort == WaarnemingSoort.Splitsing && w.NaMelder == o.Start && w.Richting == o.Richting))
            {
                _kaart.Wissel(bron).Waarnemingen.Add(new WisselWaarneming
                {
                    Soort = WaarnemingSoort.Splitsing,
                    NaMelder = o.Start,
                    Richting = o.Richting,
                    VolgendeBijRechtdoor = o.BronRechtdoorVolgende,
                    VolgendeBijAfbuigend = basis.Reeks[1],
                    Configuratie = o.Configuratie.Zonder(bron).ToString(),
                    Toelichting = $"Na melder {o.Start} ({o.Richting.Tekst()}): rechtdoor → melder {o.BronRechtdoorVolgende?.ToString() ?? "?"}, afbuigend → melder {basis.Reeks[1]} (puntzijde, gevonden door andersom te rijden)."
                });
                _log.Vondst($"Adres {bron}, andere kant: na melder {o.Start} ({o.Richting.Tekst()}) afbuigend → melder {basis.Reeks[1]}.");
            }

            // ---- Iets nieuws om te testen? ----
            var overgangen = Overgangen(basis.Reeks, o.Richting);
            var nieuw = overgangen.Where(s => !_status.GetesteOvergangen.Contains(s)).ToList();
            if (nieuw.Count == 0 && !o.TestCombinaties)
            {
                _log.Info("Deze rit bevat geen overgangen die niet al met alle adressen getest zijn - adrestest overgeslagen.");
                await TerugNaarHuis(o.Route);
                return;
            }
            // BUG #39: bij een TestCombinaties-opdracht komt de basisrit zelf meestal geen
            // stap verder (vandaar dat hij anders overgeslagen zou worden) - toch wordt hier
            // doorgegaan naar de adrestest hieronder, zie de toelichting bij Opdracht.TestCombinaties.

            // ---- Alle adressen één voor één ----
            foreach (var a in _ins.WisselAdressen())
            {
                if (a < o.VolgendAdres) continue;
                o.VolgendAdres = a;
                if (_status.Geidentificeerd.Contains(a) || o.Configuratie.Bevat(a)) continue;
                await PauzeMoment();
                _bezig = $"opdracht #{o.Id}: adres {a} testen";
                MeldVoortgang();
                BewaarNu();

                // BUG #37. Gebruikerswaarneming: in één tussenstand werden 6 verschillende
                // wisseladressen (4, 6, 7, 8, 9, 10) ALLEMAAL gemeld als "wissel na melder 28,
                // van achteren bereden, afbuigend -> kortsluiting tussen melder 28 en 26" - met
                // exact dezelfde tekst. Zes losse DCC-adressen kunnen onmogelijk dezelfde
                // fysieke wissel zijn; de overgangen-statistiek laat ook zien dat 28->26 toen
                // nog maar in 8 van de 36 pogingen daadwerkelijk lukte. De rest strandde dus
                // toch al, ONGEACHT welk adres er net getest werd - dit was dus al een eigen,
                // apart kortsluitpunt (kortsluitpunt #1, na melder 28 vooruit, alle wissels
                // rechtdoor), niet iets wat door deze zes adressen werd VEROORZAAKT. Oorzaak:
                // `kortsluitStops` werd maar ÉÉN keer berekend, VOORDAT deze foreach-lus over
                // alle wisseladressen begon.
                // Zodra adres 4 toevallig als eerste tegen dat al bestaande, wankele punt
                // aanliep en zo kortsluitpunt #1 deed ontstaan, bleef de rest van de adressen
                // (6, 7, 8, 9, 10, ...) in DEZELFDE opdracht toch nog gewoon doorrijden tot
                // voorbij melder 28 - want hun proefrit kreeg nog de VERSE, bijgewerkte lijst
                // met bekende kortsluitpunten niet te zien. Elke van die adressen liep daardoor
                // zelf ook (opnieuw, onnodig) tegen dezelfde al bekende kortsluiting aan, en
                // kreeg dat ten onrechte als EIGEN vondst toegeschreven. Fix: de lijst met
                // bekende kortsluitpunten nu bij elk adres opnieuw ophalen (in plaats van ervoor
                // vastgezet), zodat een kortsluitpunt dat tijdens deze lus ontdekt wordt
                // meteen ook de nog te testen adressen behoedt - geen herhaalde onnodige
                // kortsluitingen én geen valse toeschrijvingen meer aan adressen die er niets
                // mee te maken hebben.
                var kortsluitStops = KortsluitStops(o.Configuratie, o.Richting);

                await ZetWissel(a, true);
                var proef = await Rit(o.Start, o.Richting, new RitDoel
                {
                    Configuratie = o.Configuratie.Met(a),
                    Basis = basis.Reeks,
                    ExtraNaAfwijking = _ins.ExtraMeldersNaAfwijking,
                    StopBijMelders = kortsluitStops
                });

                // BUG #63. Gebruikerswaarneming (13:34 en 13:40): (1) na wissel 2 afbuigend een
                // kortsluiting, waarna wissel 2 meteen weer rechtdoor werd gezet: op een
                // "overloopverbinding" liggen twee wissels achter elkaar en moeten er BEIDE
                // afbuigend staan; de loc reed tegen de tong van de tweede aan. Een wissel die
                // je net gezet hebt en waarna de loc kortsluiting krijgt, blijft dus staan en
                // de andere wissels worden één voor één meegeschakeld (zie PartnerwisselZoeken).
                // (2) In het log van 13:40 stond de loc op de afbuigende tak (melder 12) en werd
                // wissel 2 teruggezet VOORDAT de loc terugreed: hij reed met de achterkant tegen
                // de tong van een rechtdoor staande wissel, kwam niet van zijn plaats en de
                // blokproef (9 blokken x 2 richtingen x 30 s) wachtte ruim 9 minuten.
                // Regel: is de loc via een andere weg gereden dan de basisrit (de weg wijkt af),
                // dan gaat hij ook met dezelfde wisselstand terug; pas daarna wordt teruggezet.
                // Volgde de proefrit de basisrit, dan blijft BUG #35 gelden (eerst terugzetten).
                var cA0 = o.Configuratie.Met(a);
                bool afwijkendePad = ProefWijktAf(basis.Reeks, proef.Reeks);
                bool zelfdeKortsluiting = basis.Einde == RitEinde.Kortsluiting && proef.Reeks.SequenceEqual(basis.Reeks);
                (int Partner, RitResultaat Rit)? partner = null;
                if (proef.Einde == RitEinde.Kortsluiting && !zelfdeKortsluiting && proef.Reeks.Count >= 1)
                    partner = await PartnerwisselZoeken(o, a, proef);

                if (afwijkendePad)
                {
                    await TerugNaarMetControle(o, proef, cA0);
                    await ZetWissel(a, false);
                }
                else
                {
                    // BUG #35: de loc staat hier altijd stil; eerst terugzetten, dan de terugrit.
                    await ZetWissel(a, false);
                    await TerugNaarMetControle(o, proef, o.Configuratie);
                }

                Vergelijk(o, basis, proef, a);
                if (partner is { } pp) VerwerkPartner(o, basis, proef, a, pp.Partner, pp.Rit);
            }
            o.VolgendAdres = int.MaxValue;
            foreach (var s in overgangen)
                if (!_status.GetesteOvergangen.Contains(s)) _status.GetesteOvergangen.Add(s);

            await TerugNaarHuis(o.Route);
        }
        catch (NavigatieFout nf)
        {
            _log.Waarschuwing($"Opdracht #{o.Id}: {nf.Message}");
            await ZetConfiguratie(Configuratie.Basis);
            await VraagLocTerugTeZetten(_kaart.StartMelder, $"{nf.Message}\n\nDe verkenner gaat terug naar de startmelder.");
            o.Pogingen++;
            if (o.Pogingen < 2)
            {
                _log.Info($"Opdracht #{o.Id} wordt later nog één keer geprobeerd.");
                _status.Wachtrij.Add(o);
            }
            else
            {
                _kaart.Waarschuwingen.Add($"Opdracht vanaf melder {o.Start} {o.Richting.Tekst()} ({o.Configuratie}) is overgeslagen: {nf.Message}");
            }
        }
    }

    private static bool ProefWijktAf(List<int> basis, List<int> proef)
    {
        for (int i = 0; i < proef.Count; i++)
            if (i >= basis.Count || proef[i] != basis[i]) return true;
        return false;
    }

    /// <summary>BUG #63: de loc stond (na een kortsluiting en herstel) op de laatste melder van
    /// <paramref name="proef"/> met wissel <paramref name="a"/> afbuigend. Dat wissel blijft zo
    /// staan; alle andere wissels worden één voor één ook op afbuigend gezet (dichtstbijzijnde
    /// adres eerst, hoogstens MaxPartnerProeven) om te kijken of de kortsluiting weg is. Bij een
    /// succes staat de loc weer op die laatste melder, met alleen <paramref name="a"/> afbuigend.</summary>
    private async Task<(int Partner, RitResultaat Rit)?> PartnerwisselZoeken(Opdracht o, int a, RitResultaat proef)
    {
        int x = proef.Reeks[^1];
        var cA = o.Configuratie.Met(a);
        var kandidaten = PartnerVolgorde(_kaart, _ins.WisselAdressen(), a, x, o.Configuratie, _ins.MaxPartnerProeven);
        _log.Info($"Kortsluiting na melder {x} met adres {a} op afbuigend: dat wissel blijft afbuigend staan en de andere wissels worden één voor één meegeschakeld ({kandidaten.Count} adressen) om te zien of de kortsluiting verdwijnt (overloopwissels).");
        foreach (var b in kandidaten)
        {
            await PauzeMoment();
            // BUG #66: elke partnerproef begint met de loc netjes ALLEEN op melder x. Staat hij (na een kortsluiting op een
            // kruiswissel) half op twee secties, dan rijden verdere proeven niet weg en kosten ze alleen tijd (log 15:14).
            _monitor.Bijwerken();
            var bezetNu = _monitor.Bezet;
            if (!(bezetNu.Count == 1 && bezetNu.Contains(x)))
            {
                _log.Waarschuwing($"Partnerzoektocht voor adres {a} afgebroken: de loc staat niet netjes op melder {x} (bezet: {Lijst(bezetNu.OrderBy(m => m).ToList())}). Verder proberen heeft pas zin als de loc weer goed staat.");
                return null;
            }
            _bezig = $"opdracht #{o.Id}: adres {a} afbuigend laten staan, partner {b} proberen";
            MeldVoortgang();
            var cAb = cA.Met(b);
            await ZetWissel(b, true);
            var rit = await Rit(x, o.Richting, new RitDoel { Configuratie = cAb, Basis = new List<int> { x }, ExtraNaAfwijking = 0 });
            await TerugNaar(x, rit, cAb);
            await ZetWissel(b, false);
            if (rit.Einde == RitEinde.Kortsluiting || rit.Reeks.Count < 2)
            {
                _log.Rijden($"Adres {b} samen met {a}: {(rit.Einde == RitEinde.Kortsluiting ? "nog steeds kortsluiting" : "geen melder verder")}.");
                continue;
            }
            _log.Vondst($"Kortsluiting opgelost: adres {a} én {b} samen op afbuigend (overloopwissels) → melder {rit.Reeks[1]} na melder {x}.");
            return (b, rit);
        }
        _log.Info($"Geen enkele partner voor adres {a} gevonden die de kortsluiting na melder {x} oplost.");
        return null;
    }

    /// <summary>BUG #66: volgorde waarin partnerwissels geprobeerd worden. Eerst de wissels waarvan al een waarneming bij melder
    /// <paramref name="x"/> bestaat (bijv. de tweede motor van een Engelse wissel/kruiswissel), daarna de rest op afstand van
    /// adres <paramref name="a"/>. Al afbuigende wissels en <paramref name="a"/> zelf vallen af.</summary>
    internal static List<int> PartnerVolgorde(Baankaart kaart, IEnumerable<int> alle, int a, int x, Configuratie c, int max)
    {
        bool BijMelder(int b) => kaart.Wissels.Any(w => w.Adres == b && w.Waarnemingen.Any(o => o.NaMelder == x || o.VolgendeBijRechtdoor == x || o.VolgendeBijAfbuigend == x));
        return alle.Where(b => b != a && !c.Bevat(b))
            .OrderBy(b => BijMelder(b) ? 0 : 1).ThenBy(b => Math.Abs(b - a)).ThenBy(b => b)
            .Take(Math.Max(0, max)).ToList();
    }

    private void VerwerkPartner(Opdracht o, RitResultaat basis, RitResultaat proef, int a, int b, RitResultaat rit)
    {
        var cA = o.Configuratie.Met(a);
        var cAb = cA.Met(b);
        int x = proef.Reeks[^1];
        var kp = _kaart.Kortsluitpunten.FirstOrDefault(k => k.NaMelder == x && k.Richting == o.Richting && k.Configuratie.Equals(cA));
        if (kp is null)
        {
            RegistreerKortsluitpunt(o, proef.Reeks, cA, o.Route, o.Richting);
            kp = _kaart.Kortsluitpunten.FirstOrDefault(k => k.NaMelder == x && k.Richting == o.Richting && k.Configuratie.Equals(cA));
        }
        if (kp is null) return;
        kp.Opgelost = true;
        kp.OpgelostDoorAdres = b;
        kp.OpgelostMetAfbuigend = true;
        kp.GeprobeerdeAdressen.Add(b);
        MarkeerGevonden(b, new WisselWaarneming
        {
            Soort = WaarnemingSoort.LostKortsluitingOp,
            NaMelder = x,
            Richting = o.Richting,
            VolgendeBijRechtdoor = null,
            VolgendeBijAfbuigend = rit.Reeks[1],
            Configuratie = cA.ToString(),
            Toelichting = $"Na melder {x} ({o.Richting.Tekst()}) alleen door te rijden als adres {a} én {b} afbuigend staan (overloopwissels, {b} wordt hier van achteren bereden) → melder {rit.Reeks[1]}."
        });
        // De losse opdracht "afbuigende tak van {a}" zou hier opnieuw kortsluiting geven.
        _status.Wachtrij.RemoveAll(w => w.Configuratie.Equals(cA) && w.Reden.StartsWith($"afbuigende tak van adres {a} "));
        Plan(new Opdracht
        {
            Configuratie = cAb,
            Start = x,
            Richting = o.Richting,
            Route = kp.Route,
            Reden = $"verder voorbij opgelost kortsluitpunt #{kp.Id} (adres {a} en {b} samen afbuigend)"
        });
    }

    /// <summary>De basisrit (zonder omgezet adres), bij de instelling "herhalen" twee keer,
    /// en bij verschil een derde keer als scheidsrechter.</summary>
    private async Task<RitResultaat?> Basisrit(Opdracht o)
    {
        var stops = KortsluitStops(o.Configuratie, o.Richting);
        RitResultaat? eerste = null;
        int aantal = _ins.BasisritHerhalen ? 2 : 1;
        var ritten = new List<RitResultaat>();
        for (int i = 0; i < aantal + 1; i++)
        {
            if (i == aantal && (ritten.Count < 2 || ritten[0].Reeks.SequenceEqual(ritten[1].Reeks))) break;
            _bezig = $"opdracht #{o.Id}: basisrit {(i == 0 ? "" : $"(controle {i})")}";
            MeldVoortgang();
            var rit = await Rit(o.Start, o.Richting, new RitDoel { Configuratie = o.Configuratie, StopBijMelders = stops });
            _log.Info($"Basisrit: {Lijst(rit.Reeks)} - einde: {EindeTekst(rit.Einde)}.");
            VerwerkEinde(o, rit);
            await TerugNaarMetControle(o, rit, o.Configuratie);
            ritten.Add(rit);
            eerste ??= rit;
            // Na een kortsluiting niet nog eens dezelfde kortsluiting opzoeken.
            if (rit.Einde == RitEinde.Kortsluiting) { stops = KortsluitStops(o.Configuratie, o.Richting); }
        }
        if (ritten.Count >= 2 && !ritten[0].Reeks.SequenceEqual(ritten[1].Reeks) && ritten[0].Einde != RitEinde.Kortsluiting)
        {
            var winnaar = ritten.Count == 3 && ritten[2].Reeks.SequenceEqual(ritten[1].Reeks) ? ritten[1] : ritten[0];
            string w = $"Basisrit vanaf melder {o.Start} {o.Richting.Tekst()} ({o.Configuratie}) gaf niet steeds dezelfde melders: {string.Join(" / ", ritten.Select(r => Lijst(r.Reeks)))}. Gebruikt: {Lijst(winnaar.Reeks)}. Controleer deze melders op spookmeldingen.";
            _kaart.Waarschuwingen.Add(w);
            _log.Waarschuwing(w);
            eerste = winnaar;
        }
        if (eerste is not null)
        {
            // De rit met kortsluiting heeft de loc teruggezet; de volgende basisrit (als die er
            // was) stopte vóór het kortsluitpunt. Voor het vergelijken is de korte versie goed.
            var gekozen = ritten.LastOrDefault(r => r.Reeks.SequenceEqual(eerste.Reeks)) ?? eerste;
            _kaart.Trajecten.Add(new Traject { Configuratie = o.Configuratie, Start = o.Start, Richting = o.Richting, Reeks = gekozen.Reeks.ToList(), Einde = gekozen.Einde });
            return gekozen;
        }
        return null;
    }

    /// <summary>Legt vast wat het einde van een (basis)rit betekent en plant vervolgopdrachten.</summary>
    private void VerwerkEinde(Opdracht o, RitResultaat rit)
    {
        int laatste = rit.Reeks[^1];
        switch (rit.Einde)
        {
            case RitEinde.Doodlopend:
                if (rit.BekendVervolgGemist)
                {
                    // BUG #64: geen kopspoor registreren en geen "terug vanaf het einde" plannen.
                    _log.Waarschuwing($"Na melder {laatste} ({o.Richting.Tekst()}) kwam geen melder, maar het vervolg ({Lijst(_kaart.BekendeVolgende(laatste, o.Richting, o.Configuratie))}) is bij deze wisselstand al eerder gevonden: de loc is waarschijnlijk blijven hangen. Dit is geen kopspoor en wordt zo niet opgeslagen.");
                    break;
                }
                if (!_kaart.Kopsporen.Any(k => k.Melder == laatste && k.Richting == o.Richting))
                    _log.Vondst($"Doodlopend: na melder {laatste} ({o.Richting.Tekst()}) kwam geen melder meer - kopspoor of stootjuk.");
                _kaart.RegistreerKopspoor(laatste, o.Richting, o.Configuratie);
                if (rit.Reeks.Count >= 2)
                    Plan(new Opdracht
                    {
                        Configuratie = o.Configuratie,
                        Start = laatste,
                        Richting = o.Richting.Om(),
                        Route = RouteMet(o.Route, o.Configuratie, o.Richting, rit.Reeks),
                        Reden = $"terug vanaf het einde bij melder {laatste}"
                    });
                break;
            case RitEinde.Lus:
                _log.Vondst($"Lus: melder {laatste} werd opnieuw bereikt ({Lijst(rit.Reeks)}).");
                break;
            case RitEinde.Kortsluiting:
                RegistreerKortsluitpunt(o, rit.Reeks, o.Configuratie, o.Route, o.Richting);
                break;
        }
    }

    // BUG #40. Gebruikerswaarneming: "bezetmelder 136 zit in blok 11 en is gewoon goed
    // berijdbaar, maar je geeft aan dat daar kortsluiting is, geen idee hoe je daar bij kwam."
    // Melder 136 kreeg nooit écht een kortsluiting (de hardware meldde niets) - de terugrit
    // kwam alleen via een andere melder terug dan de heenrit ging gereden was (TerugNaarMetControle
    // ving dat op als NavigatieFout). Dat is precies het "ongepolariseerd puntstuk: geen
    // kortsluiting" geval dat in de eigen toelichtingstekst van TerugNaarMetControle al
    // benoemd werd, maar het kwam toch als "Kortsluitpunt"/"kortsluiting" in het logboek en
    // rapport terecht, omdat beide gevallen (een echte elektrische kortsluiting én een
    // opengereden wissel) dezelfde Kortsluitpunt-boekhouding deelden. Fix: RegistreerKortsluitpunt
    // krijgt er een Soort bij, zodat het rapport een opengereden wissel niet meer "kortsluiting"
    // noemt - de boekhouding (StopBijMelders, LosKortsluitpuntenOp) blijft ongewijzigd, die mag
    // beide soorten gewoon blijven vermijden/proberen op te lossen.
    private void RegistreerKortsluitpunt(Opdracht o, List<int> reeks, Configuratie c, List<Etappe> route, Richting r,
        KortsluitpuntSoort soort = KortsluitpuntSoort.Kortsluiting)
    {
        int x = reeks[^1];
        var bestaand = _kaart.Kortsluitpunten.FirstOrDefault(k => k.NaMelder == x && k.Richting == r && k.Configuratie.Equals(c));
        var alarmBlokken = soort == KortsluitpuntSoort.Kortsluiting ? _laatsteKortsluitBlokken.ToList() : new List<int>();
        if (bestaand is not null)
        {
            bestaand.AantalKortsluitingen++;
            foreach (var b in alarmBlokken) if (!bestaand.AlarmBlokken.Contains(b)) bestaand.AlarmBlokken.Add(b);
            return;
        }
        var k = new Kortsluitpunt
        {
            Id = _status.VolgendeKortsluitId++,
            NaMelder = x,
            Richting = r,
            Configuratie = c,
            Soort = soort,
            AlarmBlokken = alarmBlokken,
            Route = RouteMet(route, c, r, reeks),
            AantalKortsluitingen = 1
        };
        _kaart.Kortsluitpunten.Add(k);
        _log.Vondst(soort == KortsluitpuntSoort.OnverwachteTerugweg
            ? $"Onverwachte terugweg #{k.Id}: direct na melder {x} ({r.Tekst()}, {c}) kwam de loc terug via een andere melder dan verwacht. Geen kortsluiting - mogelijk een wissel die van achteren in de verkeerde stand bereden wordt, mogelijk een melder die de eerste keer niet geregistreerd is; wordt zo mogelijk later opgelost."
            : $"Kortsluitpunt #{k.Id}: direct na melder {x} ({r.Tekst()}, {c})." + (alarmBlokken.Count > 0 ? $" Dinamo meldde kortsluiting in blok {string.Join(" en ", alarmBlokken)}." : "") + " Waarschijnlijk een wissel die van achteren in de verkeerde stand bereden wordt; wordt later opgelost.");
    }

    private HashSet<int> KortsluitStops(Configuratie c, Richting r) =>
        _kaart.Kortsluitpunten.Where(k => !k.Opgelost && k.Richting == r && k.Configuratie.Equals(c)).Select(k => k.NaMelder).ToHashSet();

    private static List<string> Overgangen(List<int> reeks, Richting r)
    {
        var res = new List<string>();
        for (int i = 0; i + 1 < reeks.Count; i++) res.Add($"{reeks[i]}>{reeks[i + 1]}:{r}");
        return res;
    }

    // =====================================================================
    // Vergelijken: wat deed dit adres?
    // =====================================================================

    private void Vergelijk(Opdracht o, RitResultaat basis, RitResultaat proef, int a)
    {
        var b = basis.Reeks;
        var t = proef.Reeks;
        int i = 0;
        while (i < b.Count && i < t.Count && b[i] == t[i]) i++;
        var c = o.Configuratie;
        var cA = c.Met(a);

        // (1) Omzetten gaf kortsluiting vóór er iets anders gebeurde
        if (proef.Einde == RitEinde.Kortsluiting && i == t.Count && t.Count <= b.Count
            && !(basis.Einde == RitEinde.Kortsluiting && t.Count == b.Count))
        {
            int x = t[^1];
            int? q = t.Count < b.Count ? b[t.Count] : null;
            MarkeerGevonden(a, new WisselWaarneming
            {
                Soort = WaarnemingSoort.KortsluitingBijOmzetten,
                NaMelder = x,
                Richting = o.Richting,
                VolgendeBijRechtdoor = q,
                VolgendeBijAfbuigend = null,
                Configuratie = c.ToString(),
                Toelichting = q is null
                    ? $"Op afbuigend: kortsluiting direct na melder {x}."
                    : $"Op afbuigend: kortsluiting tussen melder {x} en {q}. Deze wissel wordt hier waarschijnlijk van achteren (samenvoegend) bereden."
            });
            if (q is int qq)
            {
                // Andersom (vanaf q) is dit de puntzijde: daar met afbuigend de andere tak in.
                Plan(new Opdracht
                {
                    Configuratie = cA,
                    Start = qq,
                    Richting = o.Richting.Om(),
                    Route = RouteMet(o.Route, c, o.Richting, b.Take(t.Count + 1).ToList()),
                    Reden = $"andere tak van adres {a} verkennen (vanaf melder {qq}, andersom)",
                    BronAdres = a,
                    BronRechtdoorVolgende = x
                });
            }
            return;
        }

        // (2) Precies hetzelfde gedrag: geen effect hier
        if (i == t.Count && i == b.Count)
        {
            _log.Rijden($"Adres {a}: geen verschil.");
            return;
        }
        if (i == t.Count && proef.Einde is RitEinde.GelijkAanBasis or RitEinde.BekendKortsluitpunt)
        {
            _log.Rijden($"Adres {a}: geen verschil.");
            return;
        }

        // (3) Afwijking na melder x
        if (i >= 1)
        {
            int x = t[i - 1];
            int? rechtdoor = i < b.Count ? b[i] : null;
            int? afbuigend = i < t.Count ? t[i] : null;
            if (rechtdoor is null && afbuigend is null) { _log.Rijden($"Adres {a}: geen verschil."); return; }

            string toel = (rechtdoor, afbuigend) switch
            {
                (int r1, int a1) => $"Na melder {x} ({o.Richting.Tekst()}): rechtdoor → melder {r1}, afbuigend → melder {a1}.",
                (null, int a2) => $"Na melder {x} ({o.Richting.Tekst()}): rechtdoor loopt dood, afbuigend → melder {a2}.",
                (int r2, null) => proef.Einde == RitEinde.Kortsluiting
                    ? $"Na melder {x} ({o.Richting.Tekst()}): rechtdoor → melder {r2}, afbuigend → kortsluiting (tak loopt op een verkeerd staande wissel)."
                    : $"Na melder {x} ({o.Richting.Tekst()}): rechtdoor → melder {r2}, afbuigend → geen melder bereikt (doodlopend stuk zonder melder?).",
                _ => ""
            };
            MarkeerGevonden(a, new WisselWaarneming
            {
                Soort = WaarnemingSoort.Splitsing,
                NaMelder = x,
                Richting = o.Richting,
                VolgendeBijRechtdoor = rechtdoor,
                VolgendeBijAfbuigend = afbuigend,
                Configuratie = c.ToString(),
                Toelichting = toel
            });

            if (afbuigend is not null)
            {
                var routeTotX = RouteMet(o.Route, c, o.Richting, t.Take(i).ToList());
                Plan(new Opdracht
                {
                    Configuratie = cA,
                    Start = x,
                    Richting = o.Richting,
                    Route = routeTotX,
                    Reden = $"afbuigende tak van adres {a} na melder {x}"
                });
                if (proef.Einde == RitEinde.Kortsluiting)
                {
                    // Nieuw kortsluitpunt in de afbuigende tak
                    RegistreerKortsluitpunt(o, t.Skip(i - 1).ToList(), cA, routeTotX, o.Richting);
                }
                else if (proef.Einde == RitEinde.Doodlopend && !proef.BekendVervolgGemist)
                {
                    _kaart.RegistreerKopspoor(t[^1], o.Richting, cA);
                }
            }
            // BUG #36. Gebruikerswaarneming: wissel 2 ligt pas een stukje VERDER dan waar
            // melder 5 begint te reageren - rijdend vanaf melder 16 licht melder 5 in BEIDE
            // standen van wissel 2 op, dus "afbuigend → geen melder bereikt" hier betekent
            // niet automatisch een echt doodlopend stuk. De loc kan de wissel vanaf melder 16
            // alleen van de afbuigende kant (puntzijde omgekeerd/trailing) naderen, en dat kan
            // - afhankelijk van hoe de wissel precies ligt - vastlopen zonder nieuwe melder of
            // kortsluiting te geven, zelfs als er verderop wél degelijk een nieuwe, nog
            // onbekende melder ligt. Net als bij een kortsluiting-bij-omzetten (zie hierboven)
            // is de enige manier om dat zeker te weten: dezelfde wissel nog eens testen, maar
            // dan vanaf de ANDERE, al bekende kant (de rechtdoor-vervolgmelder, achteruit) -
            // vanaf daar wordt de wissel van de puntzijde (facing) benaderd, wat altijd een
            // eenduidig resultaat geeft.
            if (afbuigend is null && rechtdoor is int r2b)
            {
                Plan(new Opdracht
                {
                    Configuratie = cA,
                    Start = r2b,
                    Richting = o.Richting.Om(),
                    Route = RouteMet(o.Route, c, o.Richting, b.Take(i + 1).ToList()),
                    Reden = $"andere kant van adres {a} verkennen (vanaf melder {r2b}, andersom): vanaf melder {x} ({o.Richting.Tekst()}) leek afbuigend doodlopend, maar de wissel ligt mogelijk pas ná melder {x} en wordt dan hier van de puntzijde benaderd",
                    BronAdres = a,
                    BronRechtdoorVolgende = x
                });

                // BUG #39. Gebruikerswaarneming (na de voltooide verkenning van 04-10-2026):
                // "Als je vanuit bezetmelder 13 wissel 1 en 2 afbuigend had gezet en naar blok 5
                // was gereden [...] had je in blok 1 terechtgekomen." Blok 1 en 2 bleven voor de
                // verkenner onbereikbaar (adres 1 staat in "adressen zonder effect"), terwijl
                // wissel 1 en 2 pas SAMEN een route openen - wissel 2 alléén op afbuigend geeft
                // hier dus terecht "geen melder bereikt", maar dat betekent niet dat de
                // afbuigende tak nergens heen gaat, alleen dat er een TWEEDE wissel (hier: adres
                // 1) nog in de weg staat in zijn standaardstand. De verkenner testte tot nu toe
                // alleen één adres per keer tegen de vaste basisrit, en sloeg een opdracht die
                // zelf geen melder verder kwam (`nieuw.Count == 0` in VoerOpdrachtUit) altijd
                // over - zo'n combinatie van twee wissels kon dus nooit ontdekt worden. Fix:
                // naast het bestaande testen "van de andere kant" (hierboven, bug #36) wordt nu
                // ook, vanaf hetzelfde punt en in dezelfde richting maar met adres {a} al op
                // afbuigend gezet, een opdracht gepland die wél alle (nog niet geïdentificeerde)
                // adressen één voor één test, ook al rijdt de basisrit van die opdracht zelf
                // nergens heen. Zo wordt gevonden of een TWEEDE adres, in combinatie met {a},
                // de afbuigende tak van {a} alsnog opent.
                Plan(new Opdracht
                {
                    Configuratie = cA,
                    Start = x,
                    Richting = o.Richting,
                    Route = RouteMet(o.Route, c, o.Richting, t.Take(i).ToList()),
                    Reden = $"combinaties met adres {a} testen (vanaf melder {x}, {o.Richting.Tekst()}): afbuigend leek hier alleen doodlopend, maar misschien opent een tweede wissel, samen met {a} op afbuigend, alsnog een route",
                    TestCombinaties = true
                });
            }
            return;
        }

        _log.Rijden($"Adres {a}: afwijkend gedrag direct bij de start ({Lijst(t)}) - niet eenduidig, genegeerd.");
    }

    private void MarkeerGevonden(int adres, WisselWaarneming w)
    {
        _kaart.Wissel(adres).Waarnemingen.Add(w);
        if (!_status.Geidentificeerd.Contains(adres)) _status.Geidentificeerd.Add(adres);
        _log.Vondst($"WISSEL gevonden - adres {adres}: {w.Toelichting}");
        MeldVoortgang();
    }

    // =====================================================================
    // Kortsluitpunten oplossen
    // =====================================================================

    /// <summary>Probeert openstaande kortsluitpunten op te lossen met de wissels die nu
    /// bekend zijn. Geeft true als er daardoor nieuwe opdrachten gepland zijn.</summary>
    private async Task<bool> LosKortsluitpuntenOp()
    {
        bool nieuwWerk = false;
        foreach (var k in _kaart.Kortsluitpunten.Where(k => !k.Opgelost && !k.Opgegeven).ToList())
        {
            _fase = "Kortsluitpunten oplossen";
            _bezig = $"kortsluitpunt #{k.Id} na melder {k.NaMelder}";
            MeldVoortgang();

            // Kandidaten: bekende wissels waarvan (andersom gereden) een tak naar deze melder leidt.
            var kandidaten = new List<(int Adres, bool Afbuigend)>();
            foreach (var w in _kaart.Wissels)
                foreach (var obs in w.Waarnemingen.Where(x => x.Soort == WaarnemingSoort.Splitsing && x.Richting == k.Richting.Om()))
                {
                    if (obs.VolgendeBijAfbuigend == k.NaMelder) kandidaten.Add((w.Adres, true));
                    if (obs.VolgendeBijRechtdoor == k.NaMelder) kandidaten.Add((w.Adres, false));
                }
            kandidaten = kandidaten.Distinct()
                .Where(kd => k.Configuratie.Bevat(kd.Adres) != kd.Afbuigend && !k.GeprobeerdeAdressen.Contains(kd.Adres))
                .ToList();

            // BUG #63: kan geen bekende wissel de kortsluiting verklaren (of lukte dat niet), dan
            // blijven de wissels uit de configuratie van dit kortsluitpunt afbuigend staan en worden alle
            // andere wissels één voor één ook op afbuigend gezet (overloopwissels: twee wissels
            // achter elkaar, beide afbuigend). Dichtstbijzijnde adres eerst.
            int anker = k.Configuratie.Afbuigend.Length > 0 ? k.Configuratie.Afbuigend.Max() : 0;
            var partners = k.Configuratie.Afbuigend.Length == 0 ? new List<int>() : _ins.WisselAdressen()
                .Where(b => !k.Configuratie.Bevat(b) && !k.GeprobeerdeAdressen.Contains(b) && !kandidaten.Any(kd => kd.Adres == b))
                .OrderBy(b => Math.Abs(b - anker)).ThenBy(b => b)
                .Take(Math.Max(0, _ins.MaxPartnerProeven)).ToList();
            var partnerSet = partners.ToHashSet();
            foreach (var b in partners) kandidaten.Add((b, true));

            if (kandidaten.Count == 0)
            {
                _log.Info($"Kortsluitpunt #{k.Id} (na melder {k.NaMelder}): nog geen bekende wissel die het kan verklaren.");
                continue;
            }

            foreach (var (adres, afb) in kandidaten)
            {
                if (k.AantalKortsluitingen >= _ins.MaxKortsluitingenPerPlek) break;
                k.GeprobeerdeAdressen.Add(adres);
                var c = k.Configuratie.MetStand(adres, afb);
                _log.Stap($"Kortsluitpunt #{k.Id}: proberen met adres {adres} op {(afb ? "afbuigend" : "rechtdoor")}" + (partnerSet.Contains(adres) ? $" (naast {k.Configuratie}, overloopwissel?)." : "."));
                try
                {
                    await NaarStartVan(k.Route);
                    await ZetConfiguratie(c);
                    var rit = await Rit(k.NaMelder, k.Richting, new RitDoel { Configuratie = c, Basis = new List<int> { k.NaMelder }, ExtraNaAfwijking = 0 });
                    // BUG #72 (log 16:48): de uitkomst van de proef wordt EERST vastgelegd, daarna pas gaat de loc terug. Mislukte de weg terug
                    // (NavigatieFout), dan ging het gelukte resultaat (adres 13 afbuigend -> melder 14) verloren en telde het kortsluitpunt als opgegeven.
                    bool proefKortsluiting = rit.Einde == RitEinde.Kortsluiting;
                    bool proefOpgelost = !proefKortsluiting && rit.Reeks.Count >= 2;
                    if (proefKortsluiting)
                    {
                        k.AantalKortsluitingen++;
                        _log.Info($"Adres {adres} lost kortsluitpunt #{k.Id} niet op.");
                    }
                    else if (proefOpgelost)
                    {
                        k.Opgelost = true;
                        k.OpgelostDoorAdres = adres;
                        k.OpgelostMetAfbuigend = afb;
                        _kaart.Wissel(adres).Waarnemingen.Add(new WisselWaarneming
                        {
                            Soort = WaarnemingSoort.LostKortsluitingOp,
                            NaMelder = k.NaMelder,
                            Richting = k.Richting,
                            VolgendeBijRechtdoor = afb ? null : rit.Reeks[1],
                            VolgendeBijAfbuigend = afb ? rit.Reeks[1] : null,
                            Configuratie = k.Configuratie.ToString(),
                            Toelichting = $"Na melder {k.NaMelder} ({k.Richting.Tekst()}) alleen door te rijden met adres {adres} op {(afb ? "afbuigend" : "rechtdoor")} (van achteren bereden) → melder {rit.Reeks[1]}."
                        });
                        _log.Vondst($"Kortsluitpunt #{k.Id} opgelost: adres {adres} op {(afb ? "afbuigend" : "rechtdoor")} → melder {rit.Reeks[1]}.");
                        Plan(new Opdracht
                        {
                            Configuratie = c,
                            Start = k.NaMelder,
                            Richting = k.Richting,
                            Route = k.Route,
                            Reden = $"verder voorbij opgelost kortsluitpunt #{k.Id}"
                        });
                        nieuwWerk = true;
                    }
                    if (proefKortsluiting || proefOpgelost) BewaarNu();
                    try
                    {
                        await TerugNaar(k.NaMelder, rit, c);
                        await TerugNaarHuis(k.Route);
                    }
                    catch (NavigatieFout nf) when (proefKortsluiting || proefOpgelost)
                    {
                        _log.Waarschuwing($"Kortsluitpunt #{k.Id}: de uitkomst van de proef met adres {adres} is vastgelegd, maar de weg terug mislukte: {nf.Message}");
                        await ZetConfiguratie(Configuratie.Basis);
                        await VraagLocTerugTeZetten(_kaart.StartMelder, $"{nf.Message}\n\nDe uitkomst van de proef is bewaard. De verkenner gaat terug naar de startmelder.");
                    }
                    if (proefKortsluiting) continue;
                    if (proefOpgelost) break;
                }
                catch (NavigatieFout nf)
                {
                    _log.Waarschuwing($"Kortsluitpunt #{k.Id}: {nf.Message}");
                    await ZetConfiguratie(Configuratie.Basis);
                    await VraagLocTerugTeZetten(_kaart.StartMelder, $"{nf.Message}\n\nDe verkenner gaat terug naar de startmelder.");
                }
            }
            if (!k.Opgelost && k.AantalKortsluitingen >= _ins.MaxKortsluitingenPerPlek)
            {
                k.Opgegeven = true;
                string w = $"Kortsluitpunt na melder {k.NaMelder} ({k.Richting.Tekst()}) opgegeven na {k.AantalKortsluitingen} kortsluitingen.";
                _kaart.Waarschuwingen.Add(w);
                _log.Waarschuwing(w);
            }
            BewaarNu();
        }
        await ZetConfiguratie(Configuratie.Basis);
        return nieuwWerk;
    }

    // =====================================================================
    // Navigeren
    // =====================================================================

    private async Task NaarStartVan(List<Etappe> route)
    {
        foreach (var e in route)
        {
            await ZetConfiguratie(e.Configuratie);
            await Rit(e.Van, e.Richting, new RitDoel { Configuratie = e.Configuratie, DoelMelder = e.Naar, VerwachtPad = e.Pad });
        }
    }

    private async Task TerugNaarHuis(List<Etappe> route)
    {
        for (int i = route.Count - 1; i >= 0; i--)
        {
            var e = route[i];
            await ZetConfiguratie(e.Configuratie);
            var pad = e.Pad.ToList();
            pad.Reverse();
            await Rit(e.Naar, e.Richting.Om(), new RitDoel { Configuratie = e.Configuratie, DoelMelder = e.Van, VerwachtPad = pad });
        }
        await ZetConfiguratie(Configuratie.Basis);
    }

    /// <summary>Terug naar de start van de opdracht. Gaat de loc op de terugweg een andere
    /// kant op dan hij heen kwam, dan was dit een onverwachte terugweg: geen kortsluiting, maar
    /// ook geen geldige/voorspelbare rijweg (BUG #40 - dit is niet per se een opengereden
    /// wissel: op een recht stuk spoor zonder wissel kan dit ook door een gemiste meting bij de
    /// heenrit ontstaan). Die overgang wordt dan uit de kaart gehaald en net als een
    /// kortsluitpunt behandeld, zodat volgende ritten er vóór stoppen en de oplosfase - als
    /// er wél een wissel in de buurt gevonden wordt - de juiste wisselstand kan proberen.</summary>
    private async Task TerugNaarMetControle(Opdracht o, RitResultaat rit, Configuratie c)
    {
        try
        {
            await TerugNaar(o.Start, rit, c);
        }
        catch (NavigatieFout nf) when (nf.LaatsteGoed is int y && nf.Onverwacht is int z)
        {
            int idx = rit.Reeks.LastIndexOf(y);
            if (idx >= 1)
            {
                int w = rit.Reeks[idx - 1];
                // BUG #40. Gebruikerswaarneming: "bezetmelder 136 zit in blok 11 en is
                // gewoon goed berijdbaar, [...] geen idee hoe je daar bij kwam" - en later:
                // "blok 10, 11, 12 is een recht stukje spoor waar je alleen heen en weer kan
                // pendelen [...]", dus zonder wissel. De tekst hieronder zei eerder stellig
                // "die wissel staat van achteren tegen de rijrichting in" - maar hier bleek
                // helemaal geen wissel te liggen. Een terugweg via een andere melder dan
                // verwacht kan dus ook zonder wissel ontstaan, bijvoorbeeld een melder die bij
                // de eerste rit niet (op tijd) geregistreerd werd. Deze tekst beweert daarom
                // niet meer dat het om een wissel gaat.
                string tekst = $"Onverwachte terugweg na melder {w}/{y} ({o.Richting.Tekst()}, {c}): heen ging de loc van {w} naar {y}, terug ging hij na {y} naar {z} - niet terug naar {w}. Geen kortsluiting. Mogelijk ligt hier een wissel die van achteren in de verkeerde stand bereden wordt, mogelijk is dit een gewoon recht stuk spoor en is een melder bij de heenrit niet geregistreerd. Deze overgang is niet in de kaart opgenomen.";
                _kaart.Waarschuwingen.Add(tekst);
                _log.Waarschuwing(tekst);
                _kaart.Overgangen.RemoveAll(x => x.Van == w && x.Naar == y && x.Richting == o.Richting && x.GezienBijConfiguraties.All(k => k == c.Sleutel));
                RegistreerKortsluitpunt(o, rit.Reeks.Take(idx).ToList(), c, o.Route, o.Richting, KortsluitpuntSoort.OnverwachteTerugweg);
            }
            throw;
        }
    }

    /// <summary>Na een rit terug naar de startmelder van die rit (achteruit langs dezelfde
    /// melders - meteen ook de controle dat de weg terug klopt).</summary>
    private async Task TerugNaar(int start, RitResultaat rit, Configuratie c)
    {
        var bezet = _monitor.Bezet;
        if (rit.Reeks[^1] == start || (bezet.Count == 1 && bezet.Contains(start)))
        {
            await Nastellen(start, rit.Richting, achter: rit.Reeks.Count >= 2 && rit.Reeks[^1] == start ? rit.Reeks[^2] : null);
            return;
        }
        var pad = rit.Reeks.ToList();
        pad.Reverse();
        int vanaf = pad[0];
        if (!bezet.Contains(vanaf))
        {
            // Na het stoppen kan de loc net een melder verder staan (meegenomen in het pad).
            var extra = bezet.FirstOrDefault(m => !pad.Contains(m));
            if (extra != 0) { pad.Insert(0, extra); vanaf = extra; }
        }
        await Rit(vanaf, rit.Richting.Om(), new RitDoel { Configuratie = c, DoelMelder = start, VerwachtPad = pad });
    }

    // =====================================================================
    // Afronden
    // =====================================================================

    private void Afronden()
    {
        var gevonden = _status.Geidentificeerd.ToHashSet();
        foreach (var k in _kaart.Kortsluitpunten.Where(k => k.OpgelostDoorAdres is not null)) gevonden.Add(k.OpgelostDoorAdres!.Value);
        _kaart.AdressenZonderEffect = _ins.WisselAdressen().Where(a => !gevonden.Contains(a)).ToList();
        _kaart.VoorgesteldeBlokken = BlokVoorstel.Bereken(_kaart);
        _kaart.Voltooid = true;
        _fase = "Voltooid";
        _bezig = "";
        _log.Vondst($"Resultaat: {_kaart.Melders.Count} melders, {_kaart.Wissels.Count} wissels, {_kaart.Kopsporen.Count} doodlopende einden, {_kaart.VoorgesteldeBlokken.Count} voorgestelde blokken, {_kaart.AdressenZonderEffect.Count} adressen zonder gevonden effect.");
    }

    private void BewaarNu()
    {
        _kaart.Bijgewerkt = _klok.Nu;
        try { Bewaren?.Invoke(_status); }
        catch (Exception ex) { _log.Waarschuwing($"Voortgang bewaren mislukte: {ex.Message}"); }
    }

    private void MeldVoortgang()
    {
        int teTesten = _ins.WisselAdressen().Count(a => !_status.Geidentificeerd.Contains(a));
        VoortgangGewijzigd?.Invoke(new VerkenVoortgang(
            _gepauzeerd ? "Gepauzeerd" : _fase, _bezig,
            _status.AfgerondeOpdrachten, _status.Wachtrij.Count + (_status.Huidig is null ? 0 : 1),
            _kaart.Melders.Count, _kaart.Wissels.Count, teTesten, _kortsluitingenTotaal));
    }

    public static string EindeTekst(RitEinde e) => e switch
    {
        RitEinde.Doodlopend => "doodlopend (geen nieuwe melder binnen de wachttijd)",
        RitEinde.Lus => "lus (melder opnieuw bereikt)",
        RitEinde.Kortsluiting => "kortsluiting/ontsporing",
        RitEinde.BekendKortsluitpunt => "gestopt vóór bekend kortsluitpunt",
        RitEinde.GelijkAanBasis => "zelfde als basisrit",
        RitEinde.AfwijkingGevonden => "afwijking gevonden",
        RitEinde.Maximum => "maximaal aantal melders",
        RitEinde.DoelBereikt => "doel bereikt",
        _ => e.ToString()
    };
}
