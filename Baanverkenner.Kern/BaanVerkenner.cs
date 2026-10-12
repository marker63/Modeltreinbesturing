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
    /// <summary>BUG #74: de voltooide kaart wordt uitgebreid met een tweede, los traject: de volgende start bepaalt een nieuwe startmelder
    /// (waar de loc nu staat) en laat alles wat al gevonden is staan.</summary>
    public bool Uitbreiden { get; set; }
    /// <summary>BUG #104: 'Voltooide baan nogmaals checken' loopt. De verkenner werkt dan op <see cref="ControleKaart"/>; <see cref="Kaart"/>
    /// (de voltooide kaart) blijft ongewijzigd en wordt alleen aan het eind vergeleken.</summary>
    public bool Controle { get; set; }
    public Baankaart? ControleKaart { get; set; }
    /// <summary>Aantal waarnemingen per wissel waarmee de controlekaart begon (alles daarboven is tijdens de controle gezien).</summary>
    public Dictionary<int, int> ControleWisselSeed { get; set; } = new();

    /// <summary>BUG #104: zet een voltooide verkenning klaar voor een controleronde. De bestaande kaart blijft onaangeroerd. De nieuwe
    /// (controle)kaart begint met de kennis die kortsluitingen voorkomt (wissels, doorgangen, kortsluitoplossingen, Dinamo-blok per melder),
    /// maar zonder overgangen, kopsporen en kortsluitpunten: die moeten opnieuw gereden worden.</summary>
    public void VoorControle(DateTime nu)
    {
        if (!Kaart.Voltooid) throw new InvalidOperationException("Alleen een voltooide verkenning kan gecontroleerd worden.");
        var o = Kaart;
        static List<T> Kloon<T>(List<T> l) => JsonSerializer.Deserialize<List<T>>(JsonSerializer.Serialize(l, Baankaart.JsonOpties), Baankaart.JsonOpties)!;
        var k = new Baankaart
        {
            Gestart = nu, Hardware = o.Hardware, LocAdres = o.LocAdres, LocStappen = o.LocStappen, RefSnelheid = o.RefSnelheid,
            StartMelder = o.StartMelder, WisselAdresVan = o.WisselAdresVan, WisselAdresTot = o.WisselAdresTot,
            EerdereStartMelders = o.EerdereStartMelders.ToList(),
            Melders = o.Melders.Select(m => new MelderInfo { Nummer = m.Nummer, DinamoBlok = m.DinamoBlok }).ToList(),
            Wissels = Kloon(o.Wissels), Doorgangen = Kloon(o.Doorgangen), BekendeKortsluitOplossingen = Kloon(o.BekendeKortsluitOplossingen)
        };
        ControleKaart = k;
        ControleWisselSeed = k.Wissels.ToDictionary(w => w.Adres, w => w.Waarnemingen.Count);
        Controle = true;
        Uitbreiden = false;
        Wachtrij.Clear(); Huidig = null;
        GeplandeOpdrachten.Clear(); GetesteOvergangen.Clear(); Geidentificeerd.Clear();
        VolgendeOpdrachtId = 1; VolgendeKortsluitId = 1; AfgerondeOpdrachten = 0; KortsluitingenTotaal = 0;
    }

    /// <summary>BUG #74: maakt van een (voltooide) verkenning een verkenning die verder gaat op een tweede, fysiek los traject. Alles wat
    /// gevonden is (melders, overgangen, wissels, doorgangen, Dinamo-blokken, geïdentificeerde adressen) blijft staan. Alleen de wachtrij
    /// wordt leeggemaakt en de kaart telt weer als 'niet voltooid' tot de nieuwe verkenning klaar is.</summary>
    public void VoorUitbreiding()
    {
        Kaart.Voltooid = false;
        Wachtrij.Clear();
        Huidig = null;
        Uitbreiden = true;
    }

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
    private Baankaart _kaart => _status.Controle && _status.ControleKaart is { } ck ? ck : _status.Kaart;

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
    /// <summary>BUG #84: referentie uit een Koploper-export (blok per melder, wisselstraatblokken 8/9). Alleen een hulpmiddel voor de blokproef.</summary>
    public KoploperReferentie? Referentie { get; set; }
    /// <summary>Wordt na elke rit aangeroepen zodat het programma het profiel kan bewaren.</summary>
    public Action<LeerProfiel>? ProfielBijgewerkt { get; set; }

    /// <summary>BUG #79: Ja/Nee/Annuleren-vraag aan de gebruiker (true = Ja, false = Nee, null = Annuleren). Samen met
    /// <see cref="VraagWisselAdres"/> gebruikt om eerst te vragen of er een wissel verkeerd staat, voordat de loc verplaatst moet worden.
    /// Niet ingesteld (bijv. in tests) = alleen de loc-terugzetvraag.</summary>
    public Func<string, string, Task<bool?>>? VraagJaNee { get; set; }
    /// <summary>BUG #79: vraagt een wisseladres (null = geannuleerd).</summary>
    public Func<string, string, Task<int?>>? VraagWisselAdres { get; set; }

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
            bool uitbreiden = _status.Uitbreiden && _status.Kaart.StartMelder != 0;
            bool hervat = _status.Kaart.StartMelder != 0 && !uitbreiden;
            bool controleRit = _status.Controle;
            if (!hervat && !uitbreiden && !_status.Controle)
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
            _log.Stap(_status.Controle ? "Voltooide baan wordt nogmaals gecontroleerd (de bestaande kaart blijft ongewijzigd; aan het eind volgt een vergelijking)." : uitbreiden ? "Voltooide verkenning wordt uitgebreid met een tweede traject." : hervat ? "Verkenning wordt hervat." : "Verkenning start.");
            // BUG #87: bewezen kortsluitoplossingen uit eerdere verkenningen meenemen.
            if (Leerprofiel is { } lpo)
            {
                int nieuwOpl = 0;
                foreach (var o in lpo.KortsluitOplossingen)
                    if (!_kaart.BekendeKortsluitOplossingen.Any(x => x.Sleutel == o.Sleutel)) { _kaart.BekendeKortsluitOplossingen.Add(o); nieuwOpl++; }
                if (nieuwOpl > 0) _log.Info($"{nieuwOpl} kortsluitoplossing(en) uit eerdere verkenningen overgenomen (bv. een extra wissel die bij een stand hoort); die worden meteen toegepast.");
            }
            // BUG #98: overgangen uit ritten met de loc andersom (spiegelbeeld van een veel vaker gereden overgang) uit de kaart halen.
            foreach (var t in _kaart.VerwijderSpiegelOvergangen())
                _log.Waarschuwing($"Overgang {t} is verwijderd: de omgekeerde overgang in dezelfde richting is veel vaker gereden (waarschijnlijk met de loc andersom).");
            if (Leerprofiel is { AantalMetingen: > 0 } lp) _log.Info($"Leerprofiel: {lp.Overgangen.Count} overgangen uit eerdere verkenningen (tijden worden omgerekend naar verkensnelheid {_ins.Verkensnelheid}).");
            _log.Info($"Hardware: {_hw.Naam}. Testloc adres {_ins.LocAdres} ({_ins.LocStappen} stappen), verkensnelheid {_ins.Verkensnelheid}, kruipsnelheid {_ins.Kruipsnelheid}.");
            _log.Info($"Wisseladressen {_kaart.WisselAdresVan} t/m {_kaart.WisselAdresTot}." + (BlokVereist ? $" Dinamo-blokken: {_ins.DinamoBlokken}." : ""));

            await BepaalStart(hervat);
            if (uitbreiden)
            {
                _status.Uitbreiden = false; // een onderbreking hierna is een gewone hervatting vanaf de nieuwe startmelder
                _kaart.Voltooid = false;
                BewaarNu();
            }

            _fase = "Wissels in beginstand";
            MeldVoortgang();
            _log.Stap("Alle adressen in het bereik op rechtdoor zetten (commando gestuurd; de centrale meldt de werkelijke stand van de wisselmotoren niet terug).");
            foreach (var a in _ins.WisselAdressen()) await ZetWissel(a, false, forceer: true);

            if (!hervat || (_status.Controle && _status.GeplandeOpdrachten.Count == 0))
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
            if (_status.Controle) RondControleAf();
            _log.Stap(controleRit ? "Controle voltooid." : "Verkenning voltooid.");
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
            _hw.HerhaalBekendeMelderStanden(); // BUG #101: de Dinamo beantwoordt de aanvragen niet; de laatst gemelde stand wel gebruiken
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
            await ControleerUitbreiding(bezet.First());
            _kaart.StartMelder = bezet.First();
            _log.Vondst($"De loc staat op melder {_kaart.StartMelder}: dit is de startmelder.");
        }
        else if (bezet.Count == 0)
        {
            _log.Info("Geen bezette melder bekend (de centrale meldt alleen wijzigingen). De loc gaat zelf op zoek.");
            int gevonden = await ZoekEersteMelder();
            await ControleerUitbreiding(gevonden);
            _kaart.StartMelder = gevonden;
        }
        else throw new InvalidOperationException($"Er zijn nog steeds meerdere melders bezet ({Lijst(bezet)}).");

        _kaart.Melder(_kaart.StartMelder);
        if (BlokVereist && BlokVan(_kaart.StartMelder) is null)
            if (!await BlokZoekenBijStart(_kaart.StartMelder))
                throw new InvalidOperationException("Geen Dinamo-blok gevonden voor de startmelder.");
        BewaarNu();
    }

    /// <summary>BUG #74: bij het uitbreiden van een voltooide kaart is de nieuwe startmelder bijna altijd een melder die nog niet in de kaart
    /// staat (een los traject). Is hij al bekend, dan wordt gevraagd of dat de bedoeling is. De oude startmelder blijft bewaard.</summary>
    private async Task ControleerUitbreiding(int nieuw)
    {
        if (!_status.Uitbreiden) return;
        if (_kaart.Melders.Any(m => m.Nummer == nieuw))
        {
            _log.Waarschuwing($"Melder {nieuw} is al bekend uit de vorige verkenning.");
            bool door = await _vraag("Melder al bekend",
                $"De loc staat op melder {nieuw}, en die is al bekend uit de vorige verkenning. Voor het tweede, losse traject moet de loc op dat andere traject staan.\n\nToch doorgaan met melder {nieuw} als nieuwe startmelder?");
            if (!door) throw new OperationCanceledException("Uitbreiden gestopt: de loc staat nog op het eerste traject.");
        }
        if (_kaart.StartMelder != 0 && _kaart.StartMelder != nieuw && !_kaart.EerdereStartMelders.Contains(_kaart.StartMelder))
            _kaart.EerdereStartMelders.Add(_kaart.StartMelder);
        _log.Vondst($"Uitbreiding: de kaart behoudt {_kaart.Melders.Count} melders en {_kaart.Wissels.Count} wissels; er komt een tweede traject bij vanaf melder {nieuw}.");
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

    /// <param name="vooraan">BUG #102: het vervolg van een opgelost kortsluitpunt komt vooraan in de wachtrij: de plek waar de nieuwe melders te vinden
    /// zijn is bekend, dus daar gaat het eerst heen (en niet pas na alle andere adrestests).</param>
    private void Plan(Opdracht o, bool vooraan = false)
    {
        if (_status.GeplandeOpdrachten.Contains(o.Sleutel)) return;
        o.Id = _status.VolgendeOpdrachtId++;
        o.VolgendAdres = _ins.WisselAdressen().First();
        o.Route = VerkortRoute(o.Route, o.Start);
        _status.GeplandeOpdrachten.Add(o.Sleutel);
        if (vooraan) _status.Wachtrij.Insert(0, o); else _status.Wachtrij.Add(o);
        _log.Info($"Nieuwe opdracht #{o.Id}: vanaf melder {o.Start} {o.Richting.Tekst()} ({o.Configuratie}) - {o.Reden}.");
    }

    /// <summary>BUG #103 (log 08:06): de route naar melder 129 liep 14 -> 129 -> 24 en daarna nog een hele ronde 24 -> ... -> 14 -> 129 (75 s), terwijl de loc al
    /// op de heenweg door 129 kwam. Twee verkortingen, beide zonder iets aan de rijrichting of de wisselstanden te veranderen:
    /// (A) een etappe die een gesloten ronde is (begint en eindigt op dezelfde melder) brengt de loc nergens: weg ermee;
    /// (B) komt de startmelder eerder op de route al voor, dan stopt de route daar.</summary>
    internal static List<Etappe> VerkortRoute(List<Etappe> route, int start)
    {
        if (route.Count == 0) return route;
        var r = route.Where(e => !(e.Van == e.Naar && e.Pad.Count > 2)).ToList();
        for (int i = 0; i < r.Count; i++)
        {
            var e = r[i];
            for (int k = 1; k < e.Pad.Count; k++)
            {
                if (e.Pad[k] != start) continue;
                if (i == r.Count - 1 && k == e.Pad.Count - 1) return r; // het normale geval: de route eindigt op de startmelder
                var kort = r.Take(i).ToList();
                kort.Add(new Etappe { Configuratie = e.Configuratie, Richting = e.Richting, Van = e.Van, Naar = start, Pad = e.Pad.Take(k + 1).ToList() });
                return kort;
            }
        }
        return r;
    }

    /// <summary>BUG #103: waar de loc na een mislukte opdracht zelf naartoe kan rijden. -1 = de loc staat al op de startmelder van de verkenning,
    /// anders de index van de laatste etappe die op melder <paramref name="x"/> eindigt; null als de plek niet op de route ligt.</summary>
    internal static int? ZelfTerugIndex(List<Etappe> route, int x, int huisMelder)
    {
        if (x == huisMelder) return -1;
        int idx = route.FindLastIndex(e => e.Naar == x);
        return idx < 0 ? null : idx;
    }

    /// <summary>BUG #106: kortste bekende weg (aantal stappen) van <paramref name="van"/> naar <paramref name="naar"/> over overgangen die met alle
    /// wissels rechtdoor gereden zijn, elke stap in zijn eigen rijrichting. Null als er geen is; lege lijst als van == naar.</summary>
    internal static List<(int Van, int Naar, Richting Richting)>? ZelfTerugWeg(Baankaart kaart, int van, int naar)
    {
        if (van == naar) return new();
        string basis = Configuratie.Basis.Sleutel;
        var vorige = new Dictionary<int, (int Van, Richting R)>();
        var rij = new Queue<int>(); rij.Enqueue(van);
        var gezien = new HashSet<int> { van };
        while (rij.Count > 0)
        {
            int m = rij.Dequeue();
            foreach (var o in kaart.Overgangen.Where(o => o.Van == m && o.GezienBijConfiguraties.Contains(basis)).OrderBy(o => o.Naar))
            {
                if (!gezien.Add(o.Naar)) continue;
                vorige[o.Naar] = (m, o.Richting);
                if (o.Naar == naar)
                {
                    var weg = new List<(int, int, Richting)>();
                    int cur = naar;
                    while (cur != van) { var (v, r) = vorige[cur]; weg.Add((v, cur, r)); cur = v; }
                    weg.Reverse();
                    return weg;
                }
                rij.Enqueue(o.Naar);
            }
        }
        return null;
    }

    private static List<Etappe> RouteMet(List<Etappe> basis, Configuratie c, Richting r, List<int> pad)
    {
        var res = basis.ToList();
        if (pad.Count >= 2)
            res.Add(new Etappe { Configuratie = c, Richting = r, Van = pad[0], Naar = pad[^1], Pad = pad.ToList() });
        return res;
    }

    /// <summary>BUG #87: de stand waarmee de opdracht echt gereden wordt: de stand van de opdracht plus de extra wissel die een eerder opgelost
    /// kortsluitpunt voor deze start en stand vereist (de kortsluiting komt anders steeds terug, bv. wissel 2 afbuigend zonder wissel 1).</summary>
    /// <summary>BUG #98: het pad dat een rit vanaf <paramref name="start"/> volgens de kaart (en voor melder 24 de kruiswissel-referentie) zeker volgt,
    /// zolang er per stap precies één vervolg overblijft.</summary>
    internal static Configuratie KruisIngangStand(List<int> pad, Configuratie c, int? bronAdres, out string? uitleg)
    {
        uitleg = null;
        int i = pad.IndexOf(KruiswisselReferentie.Midden);
        if (i < 1) return c;
        int ingang = pad[i - 1];
        var bewezen = KruiswisselReferentie.Verbindingen.Where(v => v.Bewezen && (v.MelderA == ingang || v.MelderB == ingang)).ToList();
        if (bewezen.Count == 0) return c;
        var delen = new List<string>();
        foreach (var (motor, waarden) in new[] { (KruiswisselReferentie.Motor13, bewezen.Select(v => v.Afbuigend13)), (KruiswisselReferentie.Motor10, bewezen.Select(v => v.Afbuigend10)) })
        {
            if (motor == bronAdres) continue;
            var vast = waarden.Where(w => w.HasValue).Select(w => w!.Value).Distinct().ToList();
            if (vast.Count == 1 && c.Bevat(motor) != vast[0])
            { c = c.MetStand(motor, vast[0]); delen.Add($"motor {motor} {(vast[0] ? "afbuigend" : "rechtdoor")}"); }
        }
        if (delen.Count > 0)
            uitleg = $"Het pad {string.Join(" - ", pad)} gaat de kruiswissel in vanaf melder {ingang}; alle bewezen verbindingen vanaf die kant vragen {string.Join(" en ", delen)}, dat wordt vooraf gezet.";
        return c;
    }

    internal static List<int> VoorspeldPad(Baankaart k, int start, Richting r, Configuratie c)
    {
        var pad = new List<int> { start };
        for (int i = 0; i < 8; i++)
        {
            int cur = pad[^1];
            var kand = k.BekendeVolgende(cur, r, c);
            if (kand.Count == 0) kand = k.BekendeVolgende(cur, r);
            if (cur == KruiswisselReferentie.Midden && pad.Count >= 2)
            {
                bool PastRef(int x) => KruiswisselReferentie.Zoek(pad[^2], x) is { Bewezen: true } v
                    && (v.Afbuigend13 is not bool a13 || a13 == c.Bevat(KruiswisselReferentie.Motor13))
                    && (v.Afbuigend10 is not bool a10 || a10 == c.Bevat(KruiswisselReferentie.Motor10));
                var gefilterd = kand.Where(PastRef).ToList();
                if (gefilterd.Count == 0) gefilterd = k.BekendeVolgende(cur, r).Where(PastRef).ToList();
                kand = gefilterd;
            }
            // alleen als er ook kandidaten zonder strijd overblijven: een kandidaat die een andere wisselstand eist dan nu (bv. wissel 5 afbuigend
            // naar 129 terwijl 5 rechtdoor staat) is dan een andere weg dan de bedoelde
            if (kand.Count > 1)
            {
                var zonderStrijd = kand.Where(x => !k.WisselEisenVoorStap(cur, x, r).Any(e => c.Bevat(e.Adres) != e.Afbuigend)).ToList();
                if (zonderStrijd.Count > 0) kand = zonderStrijd;
            }
            if (kand.Count != 1 || pad.Contains(kand[0])) break;
            pad.Add(kand[0]);
        }
        return pad;
    }

    private Configuratie RijStand(Opdracht o)
    {
        var c = _kaart.MetBekendeOplossingenVoorStart(o.Start, o.Richting, o.Configuratie, out var wijz);
        if (wijz.Count > 0)
            _log.Info($"Opdracht #{o.Id} ({o.Configuratie}) rijdt met een bekende extra wisselstand: {string.Join("; ", wijz)}. Rijstand: {c}.");

        // BUG #93: is er vanaf deze start in deze richting precies één vervolg bekend, dan hoeft de verkenner niet te raden: de wissels
        // die dat vervolg nodig heeft worden meteen goed gezet (bv. 129 -> 24 -> 21 vraagt wissel 10 afbuigend, anders kortsluiting
        // in blok 9). Alleen wissels die extra AFBUIGEND moeten worden; wat de opdracht zelf test (BronAdres) of rechtdoor wil, blijft staan.
        // BUG #98 (Marco 16:24: "weer vergeten hoe wissel 14 moest staan"): niet alleen de eerste stap, het hele voorspelbare vervolg (ook door
        // de kruiswissel: 14 -> 24 -> 8 met motor 13 afbuigend) telt mee. 24 -> 8 vraagt wissel 14 afbuigend; dat was bekend maar werd pas
        // toegepast na de kortsluiting in blok 4.
        var pad = VoorspeldPad(_kaart, o.Start, o.Richting, c);
        if (pad.Count >= 2)
        {
            var extra = _kaart.MetWisselEisen(pad, o.Richting, c, out _);
            var toe = c; var toegepast = new List<string>();
            foreach (var a in extra.Afbuigend.Where(x => !c.Bevat(x) && x != o.BronAdres))
            { toe = toe.MetStand(a, true); toegepast.Add($"{a} afbuigend"); }
            if (toegepast.Count > 0)
            {
                _log.Info($"Opdracht #{o.Id}: vanaf melder {o.Start} ({o.Richting.Tekst()}) is het vervolg {string.Join(" - ", pad)} al bekend; daarvoor moet {string.Join(", ", toegepast)} staan. Rijstand: {toe}.");
                c = toe;
            }
        }
        // BUG #98 (Marco 16:27: "weer het kruiswissel"): rijdt de loc volgens het voorspelde pad de kruiswissel in vanaf een been waarvan ALLE bewezen
        // verbindingen dezelfde stand van motor 13 (of 10) vragen, dan staat die motor vóór het inrijden goed, ook als de uitgang nog niet bekend is
        // (8 -> 24: alleen 14 is bewezen en vraagt motor 13 afbuigend; zonder dat gaf 8 -> 24 kortsluiting in blok 8).
        c = KruisIngangStand(pad, c, o.BronAdres, out var kruisUitleg);
        if (kruisUitleg is not null) _log.Info($"Opdracht #{o.Id}: {kruisUitleg} Rijstand: {c}.");
        return c;
    }

    private async Task VoerOpdrachtUit(Opdracht o)
    {
        _handmatigeOplossingGemeld = false;
        o.Route = VerkortRoute(o.Route, o.Start); // ook voor opdrachten uit een eerder opgeslagen voortgang
        _fase = $"Opdracht #{o.Id}";
        _bezig = $"vanaf melder {o.Start} {o.Richting.Tekst()} ({o.Configuratie})";
        MeldVoortgang();
        _log.Stap($"Opdracht #{o.Id}: {_bezig} - {o.Reden}.");

        try
        {
            var rijstand = RijStand(o);
            await NaarStartVan(o.Route, rijstand);
            // BUG #96 (Marco 16:01: "weer kortsluiting in het kruiswissel en motor 13 staat verkeerd"): motor 13 of 10 omzetten terwijl de loc op melder 24
            // staat geeft ALTIJD kortsluiting (log 15:40, 15:48, 14:40). Is dat hier nodig en kon het niet vooraf (#95), dan wordt de opdracht
            // overgeslagen en de loc terug gebracht, in plaats van weer een kortsluiting te veroorzaken.
            if (_monitor.Bezet.Contains(KruiswisselReferentie.Midden))
            {
                var huidig = new Configuratie(_wisselStand.Where(w => w.Value).Select(w => w.Key).ToArray());
                var omTeZetten = new[] { KruiswisselReferentie.Motor13, KruiswisselReferentie.Motor10 }
                    .Where(a => (_wisselStand.TryGetValue(a, out var nu) ? nu : false) != rijstand.Bevat(a)).ToList();
                if (omTeZetten.Count > 0)
                {
                    var tekst = $"Opdracht vanaf melder {o.Start} {o.Richting.Tekst()} ({o.Configuratie}) is overgeslagen: motor {string.Join(" en ", omTeZetten)} zou omgezet moeten worden terwijl de loc op de kruiswissel (melder {KruiswisselReferentie.Midden}) staat, en dat geeft altijd kortsluiting. Vanaf deze ingangskant is de combinatie niet bewezen; niets omgezet.";
                    _log.Waarschuwing(tekst);
                    _kaart.Waarschuwingen.Add(tekst);
                    await TerugNaarHuis(o.Route, huidig);
                    return;
                }
            }
            await ZetConfiguratie(rijstand);

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
                // BUG #106 (log 09:06): adres 14 afbuigend op de basisrit 17 -> 8 gaf zelf een kortsluiting, want de kaart weet al dat 14 daar
                // rechtdoor moet staan. De rit stopt nu vóór zo'n stap (op melder 17) in plaats van de kortsluiting zelf te veroorzaken.
                foreach (var s in EisConflictStops(_kaart, basis.Reeks, o.Richting, a)) kortsluitStops.Add(s);

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
                // BUG #92 (log 14:38): wissel 10 afbuigend gaf kortsluiting zodra de loc op melder 24 (de kruiswissel) kwam en bleef die geven
                // zolang de loc daar stond. De partnerzoektocht (andere wissels meeschakelen) zette toen wissel 5 afbuigend terwijl de loc op de
                // kruiswissel stond, gaf het op ("zet geen wissels om") en liet Marco de loc terugzetten, terwijl de kortsluiting pas weg was toen
                // wissel 10 weer rechtdoor stond (14:39:13 -> opgeheven 14:39:15). Motor 13 en 10 zijn geen overloopwissels: hun stand per rit staat
                // in de kruiswissel-referentie. Dus geen partnerzoektocht; direct terugzetten (hieronder) en de kortsluiting als bevinding vastleggen.
                if (IsKruiswisselMotorKortsluiting(a, proef))
                {
                    _log.Vondst($"Adres {a} afbuigend geeft kortsluiting zodra de loc op melder {KruiswisselReferentie.Midden} (kruiswissel) komt ({Lijst(proef.Reeks)}). " +
                                "Motor 13 en 10 zijn geen overloopwissels: geen partnerzoektocht, wissel wordt meteen teruggezet.");
                }
                else if (proef.Einde == RitEinde.Kortsluiting && !zelfdeKortsluiting && proef.Reeks.Count >= 1)
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
            await ZetConfiguratie(Configuratie.Basis, spaarOnderLoc: true);
            if (!await ProbeerZelfTerugNaarStart(o.Route, nf))
                await VraagLocTerugTeZetten(_kaart.StartMelder, $"{nf.Message}\n\nDe verkenner gaat terug naar de startmelder.");
            o.Pogingen++;
            if (o.Pogingen < 2)
            {
                // BUG #102 (Marco 17:01, log 15:19): zei de gebruiker net welke wissel de kortsluiting oplost, dan meteen opnieuw rijden met die
                // wissel erbij; anders bleef de opdracht 23 minuten achteraan de wachtrij liggen terwijl het volgende blok zo in beeld kwam.
                bool direct = _handmatigeOplossingGemeld;
                _handmatigeOplossingGemeld = false;
                PlaatsHerpoging(_status.Wachtrij, o, direct);
                _log.Info(direct
                    ? $"Opdracht #{o.Id} wordt direct opnieuw gereden: de gebruiker gaf net de oplossing van de kortsluiting aan."
                    : $"Opdracht #{o.Id} wordt later nog één keer geprobeerd.");
            }
            else
            {
                _kaart.Waarschuwingen.Add($"Opdracht vanaf melder {o.Start} {o.Richting.Tekst()} ({o.Configuratie}) is overgeslagen: {nf.Message}");
            }
        }
    }

    internal static void PlaatsHerpoging(List<Opdracht> wachtrij, Opdracht o, bool direct)
    {
        if (direct) wachtrij.Insert(0, o); else wachtrij.Add(o);
    }

    private bool _handmatigeOplossingGemeld;

    /// <summary>BUG #92: eindigde de proefrit met wissel <paramref name="adres"/> afbuigend in kortsluiting op melder 24 en is dat adres een van de
    /// twee kruiswisselmotoren (13 of 10)?</summary>
    internal static bool IsKruiswisselMotorKortsluiting(int adres, RitResultaat proef)
        => (adres == KruiswisselReferentie.Motor13 || adres == KruiswisselReferentie.Motor10)
           && proef.Einde == RitEinde.Kortsluiting && proef.Reeks.Count >= 1 && proef.Reeks[^1] == KruiswisselReferentie.Midden;

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
        }, vooraan: true);
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
            var rit = await Rit(o.Start, o.Richting, new RitDoel { Configuratie = RijStand(o), StopBijMelders = stops });
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
        var tweede = soort == KortsluitpuntSoort.Kortsluiting ? _laatsteKortsluitMelders.Where(m => m != x).ToList() : new List<int>();
        if (bestaand is not null)
        {
            foreach (var t in tweede) if (!bestaand.TweedeMelders.Contains(t)) bestaand.TweedeMelders.Add(t);
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
            TweedeMelders = tweede,
            Route = RouteMet(route, c, r, reeks),
            AantalKortsluitingen = 1
        };
        _kaart.Kortsluitpunten.Add(k);
        _log.Vondst(soort == KortsluitpuntSoort.OnverwachteTerugweg
            ? $"Onverwachte terugweg #{k.Id}: direct na melder {x} ({r.Tekst()}, {c}) kwam de loc terug via een andere melder dan verwacht. Geen kortsluiting - mogelijk een wissel die van achteren in de verkeerde stand bereden wordt, mogelijk een melder die de eerste keer niet geregistreerd is; wordt zo mogelijk later opgelost."
            : $"Kortsluitpunt #{k.Id}: direct na melder {x} ({r.Tekst()}, {c})." + (alarmBlokken.Count > 0 ? $" Dinamo meldde kortsluiting in blok {string.Join(" en ", alarmBlokken)}." : "") + (tweede.Count > 0 ? $" Ook bezet: melder {string.Join(" en ", tweede)}." : "") + " Waarschijnlijk een wissel die van achteren in de verkeerde stand bereden wordt; wordt later opgelost.");
    }

    /// <summary>BUG #106: melders waar de adrestest moet stoppen omdat de volgende stap van de basisrit volgens de kaart eist dat adres
    /// <paramref name="a"/> rechtdoor staat, terwijl de test het afbuigend zet. Zonder stop veroorzaakt de test zelf een kortsluiting.</summary>
    internal static HashSet<int> EisConflictStops(Baankaart kaart, List<int> reeks, Richting r, int a)
    {
        var res = new HashSet<int>();
        for (int i = 1; i + 1 < reeks.Count; i++)
            if (kaart.WisselEisenVoorStapDetail(reeks[i], reeks[i + 1], r).Any(e => e.Adres == a && !e.Afbuigend))
                res.Add(reeks[i]);
        return res;
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
        foreach (var k in _kaart.Kortsluitpunten.Where(k => !k.Opgelost && !k.Opgegeven && !k.Tijdelijk).ToList())
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
            // BUG #75: de tweede bezette melder op het moment van de kortsluiting wijst de verdachte wissels aan; die gaan eerst.
            var hints = _kaart.HintsBijTweedeMelder(k.NaMelder, k.TweedeMelders, k.Configuratie);
            kandidaten = kandidaten.Distinct()
                .Where(kd => k.Configuratie.Bevat(kd.Adres) != kd.Afbuigend && !k.GeprobeerdeAdressen.Contains(kd.Adres))
                .ToList();
            var hintKandidaten = hints.Where(h => k.Configuratie.Bevat(h.Adres) != h.Afbuigend && !k.GeprobeerdeAdressen.Contains(h.Adres) && _ins.WisselAdressen().Contains(h.Adres)).ToList();
            if (hintKandidaten.Count > 0)
            {
                _log.Info($"Kortsluitpunt #{k.Id}: omdat ook melder {string.Join(" en ", k.TweedeMelders)} bezet was, gaan eerst deze wissels aan de beurt: {string.Join(", ", hintKandidaten.Select(h => $"{h.Adres} {(h.Afbuigend ? "afbuigend" : "rechtdoor")}"))}.");
                kandidaten = hintKandidaten.Concat(kandidaten.Where(kd => !hintKandidaten.Any(h => h.Adres == kd.Adres))).ToList();
            }

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
                        }, vooraan: true);
                        nieuwWerk = true;
                    }
                    if (proefKortsluiting || proefOpgelost) BewaarNu();
                    try
                    {
                        await TerugNaar(k.NaMelder, rit, c);
                        await TerugNaarHuis(k.Route, c);
                    }
                    catch (NavigatieFout nf) when (proefKortsluiting || proefOpgelost)
                    {
                        _log.Waarschuwing($"Kortsluitpunt #{k.Id}: de uitkomst van de proef met adres {adres} is vastgelegd, maar de weg terug mislukte: {nf.Message}");
                        await ZetConfiguratie(Configuratie.Basis, spaarOnderLoc: true);
                        if (!await ProbeerZelfTerugNaarStart(k.Route, nf))
                            await VraagLocTerugTeZetten(_kaart.StartMelder, $"{nf.Message}\n\nDe uitkomst van de proef is bewaard. De verkenner gaat terug naar de startmelder.");
                    }
                    if (proefKortsluiting) continue;
                    if (proefOpgelost) break;
                }
                catch (NavigatieFout nf)
                {
                    _log.Waarschuwing($"Kortsluitpunt #{k.Id}: {nf.Message}");
                    await ZetConfiguratie(Configuratie.Basis, spaarOnderLoc: true);
                    if (!await ProbeerZelfTerugNaarStart(k.Route, nf))
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

    /// <summary>BUG #74: staat de loc op een melder waar hij vanaf een bekende kant binnenkwam (kruiswissel-sectie) en rijdt hij in dezelfde
    /// richting door, dan telt die ingangskant mee bij de keuze van de wisselstand voor de eerste stap (zie
    /// <see cref="Baankaart.KiesConfiguratieBijIngang"/>).</summary>
    private Configuratie KiesBijIngang(int melder, Richting r, List<int> pad, Configuratie gewenst)
    {
        if (_ingang is not { } ing || ing.Melder != melder || ing.Richting != r || pad.Count < 2 || pad[0] != melder) return gewenst;
        var kies = _kaart.KiesConfiguratieBijIngang(ing.Via, melder, r, pad[1], gewenst);
        if (!kies.Equals(gewenst))
            _log.Info($"Ingangskant: de loc kwam vanaf melder {ing.Via} op melder {melder} aan. Daarbij leidde '{gewenst}' eerder naar een andere melder dan {pad[1]}; voor de eerste stap geldt nu '{kies}'.");
        return kies;
    }

    /// <summary>BUG #77: bij navigeren over een bekend pad moet elke stap de wisselstand hebben die uit de gevonden splitsingen volgt (zie
    /// <see cref="Baankaart.WisselEisenVoorStap"/>), ook als de stand van de etappe of van een proef iets anders zegt.</summary>
    private Configuratie MetEisen(List<int> pad, Richting r, Configuratie c)
    {
        var nieuw = _kaart.MetWisselEisen(pad, r, c, out var wijz);
        if (wijz.Count > 0)
            _log.Info($"Wisselstand voor het pad {string.Join("-", pad)} ({r.Tekst()}) aangepast aan wat over de wissels bekend is: {string.Join(", ", wijz)}. Nieuwe stand: {nieuw}.");
        // BUG #83: ook wat opgeloste kortsluitpunten al bewezen hebben.
        var metOplossing = _kaart.MetBekendeKortsluitOplossingen(pad, r, nieuw, out var wijz2);
        if (wijz2.Count > 0)
            _log.Info($"Wisselstand voor het pad {string.Join("-", pad)} ({r.Tekst()}) aangepast om een bekende kortsluiting te vermijden: {string.Join("; ", wijz2)}. Nieuwe stand: {metOplossing}.");
        // BUG #85: de vaste kruiswissel-referentie (motor 13 en 10 bij een rit door melder 24) gaat voor alles wat uit kortsluitingen is afgeleid.
        var metKruis = KruiswisselReferentie.PasToe(pad, metOplossing, out var wijz3);
        if (wijz3.Count > 0)
            _log.Info($"Wisselstand voor het pad {string.Join("-", pad)} ({r.Tekst()}) aangepast aan de kruiswissel-referentie: {string.Join("; ", wijz3)}. Nieuwe stand: {metKruis}.");
        return metKruis;
    }

    /// <summary>BUG #95 (Marco 15:51: opdracht 24 achteruit met wissel 10 afbuigend gaf elke keer kortsluiting): begint de opdracht op de kruiswissel
    /// (melder 24) en moet daar motor 13 of 10 anders staan dan nu, dan wordt die wissel omgezet terwijl de loc op de kruiswissel staat - kortsluiting.
    /// Is de ingang (het been waar de loc vandaan komt) volgens de referentie samen met de gewenste standen bewezen, dan worden die standen al
    /// VOOR het inrijden van melder 24 gezet; bij het neerzetten hoeft er dan niets meer onder de loc om.</summary>
    internal static Configuratie KruisAankomstStand(Etappe e, Configuratie cfg, Configuratie doel, out string? uitleg)
    {
        uitleg = null;
        if (e.Naar != KruiswisselReferentie.Midden || e.Pad.Count < 2) return cfg;
        int ingang = e.Pad[^2];
        bool w13 = doel.Bevat(KruiswisselReferentie.Motor13), w10 = doel.Bevat(KruiswisselReferentie.Motor10);
        if (cfg.Bevat(KruiswisselReferentie.Motor13) == w13 && cfg.Bevat(KruiswisselReferentie.Motor10) == w10) return cfg;
        bool Past(KruiswisselReferentie.Verbinding v) => v.Bewezen
            && (v.MelderA == ingang || v.MelderB == ingang)
            && (v.Afbuigend13 is not bool a || a == w13) && (v.Afbuigend10 is not bool b || b == w10);
        if (!KruiswisselReferentie.Verbindingen.Any(Past)) return cfg;
        var nieuw = cfg.MetStand(KruiswisselReferentie.Motor13, w13).MetStand(KruiswisselReferentie.Motor10, w10);
        uitleg = $"De opdracht begint op de kruiswissel (melder {KruiswisselReferentie.Midden}); de loc komt van melder {ingang}. Motor 13 {(w13 ? "afbuigend" : "rechtdoor")} en 10 {(w10 ? "afbuigend" : "rechtdoor")} worden al VOOR het inrijden gezet (referentie: dit past bij die ingang), zodat er niets onder de staande loc omgezet hoeft te worden.";
        return nieuw;
    }

    private async Task NaarStartVan(List<Etappe> route, Configuratie? doel = null)
    {
        for (int i = 0; i < route.Count; i++)
        {
            var e = route[i];
            var cfg = MetEisen(e.Pad, e.Richting, KiesBijIngang(e.Van, e.Richting, e.Pad, e.Configuratie));
            if (doel is not null && i == route.Count - 1)
            {
                cfg = KruisAankomstStand(e, cfg, doel, out var uitleg);
                if (uitleg is not null) _log.Info(uitleg);
            }
            await ControleerGeenAlarmVoorStap($"navigatie {e.Van} -> {e.Naar}");
            await ZetConfiguratie(cfg);
            await Rit(e.Van, e.Richting, new RitDoel { Configuratie = cfg, DoelMelder = e.Naar, VerwachtPad = e.Pad });
        }
    }

    /// <param name="eersteConfiguratie">BUG #73: na een proef met een extra wissel op een kruiswissel-sectie blijft de wisselstand van die
    /// proef staan voor het EERSTE stuk van de terugweg; pas daarna volgen de standen van de route.</param>
    private async Task TerugNaarHuis(List<Etappe> route, Configuratie? eersteConfiguratie = null)
    {
        for (int i = route.Count - 1; i >= 0; i--)
        {
            var e = route[i];
            var cfg = i == route.Count - 1 && eersteConfiguratie is not null ? eersteConfiguratie : e.Configuratie;
            var pad = e.Pad.ToList();
            pad.Reverse();
            cfg = MetEisen(pad, e.Richting.Om(), KiesBijIngang(e.Naar, e.Richting.Om(), pad, cfg));
            await ControleerGeenAlarmVoorStap($"terugweg {e.Naar} -> {e.Van}");
            await ZetConfiguratie(cfg);
            await Rit(e.Naar, e.Richting.Om(), new RitDoel { Configuratie = cfg, DoelMelder = e.Van, VerwachtPad = pad });
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

    /// <summary>BUG #105: de weg VOORUIT (in de rijrichting van de rit) van de laatste melder van <paramref name="rit"/> naar <paramref name="start"/>,
    /// als elke stap daarvan eerder met precies wisselstand <paramref name="c"/> is gereden, er nergens een keuze is (één vervolgmelder) en de weg
    /// strikt korter is dan de weg terug langs de rit. Anders null. De loc moet nu op de laatste melder staan.</summary>
    internal static List<int>? DoorrijdenNaarStart(Baankaart kaart, int start, RitResultaat rit, Configuratie c, ISet<int> bezet)
    {
        if (rit.Reeks.Count < 3) return null;
        int huidig = rit.Reeks[^1];
        if (!bezet.Contains(huidig)) return null;
        var pad = new List<int> { huidig };
        for (int i = 0; i < rit.Reeks.Count - 2; i++)
        {
            int van = huidig;
            var volg = kaart.Overgangen
                .Where(o => o.Van == van && o.Richting == rit.Richting && o.GezienBijConfiguraties.Contains(c.Sleutel))
                .Select(o => o.Naar).Distinct().ToList();
            if (volg.Count != 1) return null;
            huidig = volg[0];
            if (pad.Contains(huidig) && huidig != start) return null;
            pad.Add(huidig);
            if (huidig == start) return pad;
        }
        return null;
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
        // BUG #105 (log 08:46): na de proef met wissel 2 afbuigend stond de loc op melder 5, één melder voor de startmelder 13 van de lus. Hij reed de hele
        // lus achteruit terug (15 melders, ruim twee minuten) in plaats van één melder vooruit. Is de weg vooruit naar de startmelder bekend (elke stap
        // eerder gereden met precies deze wisselstand, zonder keuze) en korter dan de weg terug, dan wordt die genomen.
        var door = DoorrijdenNaarStart(_kaart, start, rit, c, bezet);
        if (door is not null)
        {
            _log.Info($"Terug naar melder {start} door verder te rijden ({Lijst(door)}, {rit.Richting.Tekst()}) in plaats van {rit.Reeks.Count - 1} melders terug: die weg is eerder met deze wisselstand gereden.");
            var cDoor = MetEisen(door, rit.Richting, KiesBijIngang(door[0], rit.Richting, door, c));
            await ControleerGeenAlarmVoorStap($"doorrijden naar melder {start}");
            await ZetConfiguratie(cDoor);
            var klaar = await Rit(door[0], rit.Richting, new RitDoel { Configuratie = cDoor, DoelMelder = start, VerwachtPad = door });
            if (klaar.Einde == RitEinde.Kortsluiting)
                throw new NavigatieFout($"Bij het doorrijden naar melder {start} volgde een kortsluiting: de loc staat niet op melder {start}.");
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
        // BUG #77 (log 18:58): de terugweg 14 -> 129 -> 24 -> 21 werd met wissel 5 rechtdoor gereden terwijl 14 -> 129 alleen met wissel 5
        // afbuigend bestaat: kortsluiting. Voor de terugweg gelden de bekende wisselstanden per stap.
        c = MetEisen(pad, rit.Richting.Om(), KiesBijIngang(vanaf, rit.Richting.Om(), pad, c));
        await ControleerGeenAlarmVoorStap($"terugweg naar melder {start}");
        await ZetConfiguratie(c);
        var terug = await Rit(vanaf, rit.Richting.Om(), new RitDoel { Configuratie = c, DoelMelder = start, VerwachtPad = pad });
        // BUG #73 (log 17:48 en 17:54): kreeg de loc op de terugweg kortsluiting, dan was hij NIET terug op de startmelder. Het resultaat werd
        // genegeerd, de volgende proefrit begon vanaf een verkeerd aangenomen plek en de loc werd steeds opnieuw handmatig verplaatst.
        if (terug.Einde == RitEinde.Kortsluiting)
            throw new NavigatieFout($"Op de terugweg naar melder {start} volgde een kortsluiting: de loc staat niet op melder {start}.");
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
        _kaart.Wisselstraatvoorstellen = WisselstraatVoorstel.Bereken(_kaart);
        _kaart.Voltooid = true;
        _fase = "Voltooid";
        _bezig = "";
        _log.Vondst($"Resultaat: {_kaart.Melders.Count} melders, {_kaart.Wissels.Count} wissels, {_kaart.Kopsporen.Count} doodlopende einden, {_kaart.VoorgesteldeBlokken.Count} voorgestelde blokken, {_kaart.AdressenZonderEffect.Count} adressen zonder gevonden effect.");
    }

    /// <summary>BUG #104: vergelijkt de controlekaart met de voltooide kaart, zet het verslag bij de voltooide kaart (de rest ervan blijft
    /// zoals het was) en zet de controle uit.</summary>
    private void RondControleAf()
    {
        var oud = _status.Kaart;
        var nieuw = _status.ControleKaart!;
        var verslag = ControleVergelijking.Vergelijk(oud, nieuw, _status.ControleWisselSeed, _status.Geidentificeerd.ToHashSet(), _klok.Nu);
        oud.ControleVerslagen.Add(verslag);
        _status.Controle = false; // vanaf hier is _kaart weer de bestaande, voltooide kaart
        _log.Vondst(verslag.Samenvatting);
        foreach (var r in verslag.Regels) { if (r.StartsWith("NIEUW") || r.StartsWith("WISSEL")) _log.Waarschuwing(r); else _log.Info(r); }
        _fase = "Voltooid";
        BewaarNu();
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
        RitEinde.BekendKortsluitpunt => "gestopt op het bekende kortsluitpunt (de loc staat op de laatste melder vóór de kortsluiting)",
        RitEinde.GelijkAanBasis => "zelfde als basisrit",
        RitEinde.AfwijkingGevonden => "afwijking gevonden",
        RitEinde.Maximum => "maximaal aantal melders",
        RitEinde.DoelBereikt => "doel bereikt",
        _ => e.ToString()
    };
}
