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
    /// <summary>BUG #172: proef van een kruiswissel-ingang (twee melders, precies de gevraagde motorstanden, geen adrestest).</summary>
    public bool KruisProef { get; set; }
    /// <summary>BUG #174: alleen het Dinamo-blok van deze melder bepalen (loc erheen, neerzetten, blokproef); geen rit, geen adrestest.</summary>
    public bool BlokProef { get; set; }
    /// <summary>BUG #175 (Marco 17-10 09:44: "1 extra melder terug"): bij stap voor stap begint de opdracht één melder vóór de eigenlijke startmelder (Start is dan die
    /// aanloopmelder). De loc staat zo niet op de wissels bij de startmelder, en wissels vóór de startmelder (zoals wissel 14 bij melder 8) worden ook gemeten.</summary>
    public bool Aanloop { get; set; }
    /// <summary>BUG #173: hoe vaak deze opdracht is afgebroken door een wisselprobleem (Dinamo-uitval, klem) en opnieuw begonnen is.</summary>
    public int UitvalHersteld { get; set; }
    public string Sleutel => $"{Configuratie.Sleutel}|{Start}|{Richting}{(BlokProef ? "|blok" : "")}";
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
            Wissels = Kloon(o.Wissels), Doorgangen = Kloon(o.Doorgangen), BekendeKortsluitOplossingen = Kloon(o.AlleKortsluitOplossingen()),   // BUG #162: ook de oplossingen uit de opgeloste kortsluitpunten (die lijst was leeg: de controle begon zonder kennis)
            
            KruisMotoren = o.KruisMotoren, KruisMidden = o.KruisMidden.ToList(), KruisReferentie = o.KruisReferentie
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
    /// <summary>BUG #181: register van vastgestelde feiten; elke overschrijving wordt gelogd en bewaard (zie <see cref="KennisRegister"/>).</summary>
    public KennisRegister Kennis { get; } = new();
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
    /// <summary>BUG #128: voorkennis uit eerdere verkenningen wordt alleen gebruikt als de gebruiker dat bij de start bevestigt (standaard: alles vanaf nul).</summary>
    private bool _voorkennis;
    /// <summary>BUG #129: Engelse wissels van deze verkenning (door de gebruiker opgegeven of ontdekt).</summary>
    internal KruisKennis Kruis { get; set; } = new();
    private LeerProfiel? Voorkennis => _voorkennis ? Leerprofiel : null;
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
        if (_hw is IVerbindingsBron stilteBron) stilteBron.StilteGewijzigd += HardwareMeldtStilte;
        try
        {
            if (_ins.Rondrit)
            {
                _ins.StapVoorStap = false;   // BUG #195: optie 6 sluit optie 5 uit
                _ins.ExtraMeldersNaAfwijking = Math.Max(_ins.ExtraMeldersNaAfwijking, _ins.MaxMeldersPerRit);   // na een andere melder gewoon doorrijden tot lus, kopspoor of kortsluiting
                _log.Stap("Optie 6 (rondrijden): eerst alle melders en hun vervolgen ontdekken, zonder bloknummers; rijcommando's gaan naar alle blokken. Pas daarna worden de Dinamo-blokken gezocht (nog niet getest op de baan).");
            }
            ZaaiKennis();
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
            // BUG #128: niets uit eerdere verkenningen meenemen zonder bevestiging van de gebruiker (Marco 10:48: alles vanaf nul leren om te controleren of fouten echt zijn opgelost).
            _voorkennis = false;
            if (!hervat && Leerprofiel is { } lp0 && (lp0.AantalMetingen > 0 || lp0.KortsluitOplossingen.Count > 0 || lp0.BlokHints.Count > 0))
            {
                var vraag = $"Er zijn gegevens uit eerdere verkenningen bewaard: {lp0.Overgangen.Count} overgangen (reistijden), {lp0.KortsluitOplossingen.Count} kortsluitoplossingen en {lp0.BlokHints.Count} blokhints, plus de vaste kruiswissel-referentie (melder {KruiswisselReferentie.Midden}, motor {KruiswisselReferentie.Motor13} en {KruiswisselReferentie.Motor10}).\n\nJa = deze gegevens gebruiken (sneller, maar fouten uit eerdere verkenningen kunnen daardoor verborgen blijven)\nNee = helemaal vanaf nul leren (aanbevolen om te controleren of fouten echt zijn opgelost)";
                bool? antw = VraagJaNee is null ? false : await VraagJaNee("Gegevens van eerdere verkenningen gebruiken?", vraag);
                _voorkennis = antw == true;
                _log.Info(_voorkennis ? "Gegevens uit eerdere verkenningen worden gebruikt (gekozen door de gebruiker)." : "Alles begint vanaf nul: er wordt niets overgenomen uit eerdere verkenningen. Deze verkenning leert wel voor later.");
            }
            InitKruis(hervat);
            // BUG #87: bewezen kortsluitoplossingen uit eerdere verkenningen meenemen.
            if (Voorkennis is { } lpo)
            {
                int nieuwOpl = 0;
                foreach (var o in lpo.KortsluitOplossingen)
                    if (Configuratie.VanSleutel(o.Stand).Bevat(o.Adres)) continue;   // BUG #124: 'wissel uit de geteste stand terugzetten' is geen oplossing (alleen een andere stand)
                    else if (!_kaart.BekendeKortsluitOplossingen.Any(x => x.Sleutel == o.Sleutel)) { _kaart.BekendeKortsluitOplossingen.Add(o); nieuwOpl++; }
                if (nieuwOpl > 0) _log.Info($"{nieuwOpl} kortsluitoplossing(en) uit eerdere verkenningen overgenomen (bv. een extra wissel die bij een stand hoort); die worden meteen toegepast.");
            }
            // BUG #98: overgangen uit ritten met de loc andersom (spiegelbeeld van een veel vaker gereden overgang) uit de kaart halen.
            foreach (var t in _kaart.VerwijderSpiegelOvergangen())
                _log.Waarschuwing($"Overgang {t} is verwijderd: de omgekeerde overgang in dezelfde richting is veel vaker gereden (waarschijnlijk met de loc andersom).");
            foreach (var t in _kaart.VerwijderAchterkantOvergangen())   // BUG #227
                _log.Waarschuwing($"Uit de kaart verwijderd (BUG #227): {t}.");
            if (Voorkennis is { AantalMetingen: > 0 } lp) _log.Info($"Leerprofiel: {lp.Overgangen.Count} overgangen uit eerdere verkenningen (tijden worden omgerekend naar verkensnelheid {_ins.Verkensnelheid}).");
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
                    var o = Stap ? KiesVolgendeOpdracht(_kaart, HuidigePlek(), _status.Wachtrij) : _status.Wachtrij[0];
                    _status.Wachtrij.Remove(o);
                    _status.Huidig = o;
                    BewaarNu();
                    try { await VoerOpdrachtUit(o); }
                    catch (WisselProbleemFout wf) when (Stap)
                    {
                        await HerstelNaWisselProbleem(o, wf.Message);   // BUG #173
                        continue;
                    }
                    _status.Huidig = null;
                    _status.AfgerondeOpdrachten++;
                    BewaarNu();
                }
                if (_status.Controle && PlanControleTrajecten()) continue;   // BUG #161
                if (_status.Controle) break;   // BUG #162: een kortsluiting in de controle wordt gemeld, niet urenlang opnieuw uitgeprobeerd
                if (!await LosKortsluitpuntenOp()) break;
            }

            if (Rondrit && BlokVereist && !_status.Controle) await RondritBlokkenZoeken();
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
            if (_hw is IVerbindingsBron stilteBron2) stilteBron2.StilteGewijzigd -= HardwareMeldtStilte;
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

    /// <summary>BUG #129: bepaalt de Engelse wissels van deze verkenning: bij hervatten die uit de kaart, anders wat de gebruiker opgaf. De vaste referentie
    /// (melder 24, motor 13/10) telt alleen mee als de gebruiker koos voor gegevens uit eerdere verkenningen.</summary>
    private void InitKruis(bool hervat)
    {
        if (Rondrit)
        {
            // BUG #195: bij 'Rondrijden' is een kruiswissel een gewone wissel; de motoren worden net als elk ander adres geprobeerd en de kruiswissel volgt uit de kortsluitingen en vervolgen.
            Kruis = new KruisKennis();
            BewaarKruis();
            _log.Info("Optie 6 (rondrijden): de kruiswissel wordt behandeld als een gewone wissel; motor " + (string.IsNullOrWhiteSpace(_ins.KruisMotoren) ? "(geen opgegeven)" : _ins.KruisMotoren) + " wordt als gewoon wisseladres geprobeerd.");
            return;
        }
        if (hervat && (_kaart.KruisMotoren != "" || _kaart.KruisReferentie))
        {
            Kruis = KruisKennis.Parse(_kaart.KruisMotoren);
            foreach (var m in _kaart.KruisMidden) Kruis.Middens.Add(m);
            Kruis.MetReferentie = _kaart.KruisReferentie;
        }
        else
        {
            Kruis = KruisKennis.Parse(_ins.KruisMotoren);
            if (_voorkennis)
            {
                var r = KruisKennis.Referentie();
                foreach (var m in r.Motoren) Kruis.Motoren.Add(m);
                foreach (var m in r.Middens) Kruis.Middens.Add(m);
                Kruis.MetReferentie = true;
            }
        }
        BewaarKruis();
        foreach (var t in _kaart.VerwijderNietBestaandeAdressen(_ins.NietBestaandeAdressen())) { _log.Waarschuwing("Kaart opgeruimd (BUG #209): " + t); _kaart.Waarschuwingen.Add(t); }
        foreach (var t in _kaart.ToetsVoorlopigeOplossingen()) _log.Vondst("Voorlopige oplossing getoetst (BUG #204): " + t);
        foreach (var t in _kaart.HerstelStrijdigeOplossingen()) { _log.Waarschuwing("Kaart hersteld (BUG #159): " + t); _kaart.Waarschuwingen.Add(t); }
        foreach (var t in _kaart.HerstelOnmogelijkeKruisOplossingen(Kruis)) { _log.Waarschuwing("Kaart hersteld (BUG #137): " + t); _kaart.Waarschuwingen.Add(t); }
        _log.Info(Kruis.Motoren.Count == 0
            ? "Engelse wissels: geen opgegeven; er worden geen kruiswissel-regels toegepast."
            : $"Engelse wissels: motor {Kruis.MotorenTekst}" + (Kruis.Middens.Count > 0 ? $", kruiswissel in melder {Kruis.MiddenTekst}" : ", melder nog onbekend (wordt ontdekt)") + (Kruis.MetReferentie ? "; vaste referentie uit eerdere verkenningen actief." : ". Alles wordt vanaf nul ontdekt."));
    }

    /// <summary>BUG #181: alles wat de geladen kaart al weet geldt als vastgesteld; een afwijkende nieuwe waarde is dan een overschrijving.</summary>
    private void ZaaiKennis()
    {
        Kennis.Nu = () => _klok.Nu;
        Kennis.Gemeld = o =>
        {
            _log.Waarschuwing($"OVERSCHREVEN (BUG #181): {o}");
            if (!_kaart.Overschrijvingen.Any(x => x.ToString() == o.ToString())) _kaart.Overschrijvingen.Add(o.ToString());
        };
        foreach (var m in _kaart.Melders.Where(x => x.DinamoBlok is not null)) Kennis.Zaai("blok", m.Nummer.ToString(), m.DinamoBlok!.Value.ToString());
        foreach (var m in _kaart.KruisMidden) Kennis.Zaai("kruismelder", Kruis?.MotorenTekst ?? "", m.ToString());
        foreach (var og in _kaart.Overgangen.Where(x => x.AantalKeer > 0)) Kennis.Zaai("rijrichting", $"{og.Van}>{og.Naar}", og.Richting.Tekst());
    }

    private void BewaarKruis()
    {
        _kaart.KruisMotoren = Kruis.MotorenTekst;
        _kaart.KruisMidden = Kruis.Middens.OrderBy(x => x).ToList();
        _kaart.KruisReferentie = Kruis.MetReferentie;
    }

    /// <summary>BUG #129: een kruiswissel-melder die onderweg ontdekt wordt (kortsluiting bij omzetten van een kruismotor met de loc erop).</summary>
    private void LeerKruisMidden(int melder)
    {
        if (melder <= 0 || !Kruis.Middens.Add(melder)) return;
        Kennis.Leg("kruismelder", Kruis.MotorenTekst, melder.ToString(), "LeerKruisMidden");
        BewaarKruis();
        _log.Vondst($"Melder {melder} is (waarschijnlijk) de kruiswissel van motor {Kruis.MotorenTekst}: omzetten van die motoren met de loc erop geeft kortsluiting.");
    }

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
        _log.Info("Bezet bij de start volgens Dinamo (laatst gemelde stand): " + (_monitor.Bezet.Count == 0 ? "geen" : Lijst(_monitor.Bezet)) + ".");

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
            // BUG #127: de Dinamo meldt alleen wijzigingen. Een melder die in het geheugen van het programma 'bezet' staat maar waarvan de 'vrij'-melding gemist is
            // (Dinamo herstart, stilte), bleef na OK voor altijd bezet. Het geheugen van deze melders wordt nu gewist: wat echt bezet is, meldt zich opnieuw (loc
            // iets opzetten) of wordt door de loc zelf gevonden.
            _hw.WisMelderGeheugen(bezet);
            _log.Info($"Onthouden bezetstand van melder {Lijst(bezet)} gewist; alleen nieuwe meldingen van Dinamo tellen.");
            _monitor.Bijwerken();
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
        if (BlokVereist && !Rondrit && BlokVan(_kaart.StartMelder) is null)
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
        if (Stap && o.Route.Count > 3) o.Route = o.Route.TakeLast(3).ToList();   // BUG #172: de route is hier alleen boekhouding; de loc kiest zelf zijn weg
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

    /// <summary>BUG #164: zoals <see cref="ZelfTerugWeg"/>, maar valt terug op overgangen die bij een andere wisselstand gezien zijn als er geen weg met alle
    /// wissels rechtdoor is (de loc kwam bij een kortsluiting-oplossing op een onverwachte melder, bijv. 12 achter wissel 1 en 2). Per stap de bekende
    /// configuratie met de minste afbuigende wissels. Null als er ook zo geen weg is.</summary>
    internal static List<(int Van, int Naar, Richting Richting, Configuratie Cfg)>? ZelfTerugWegAlleStanden(Baankaart kaart, int van, int naar)
    {
        if (van == naar) return new();
        var vorige = new Dictionary<int, (int Van, Richting R, Configuratie C)>();
        var rij = new Queue<int>(); rij.Enqueue(van);
        var gezien = new HashSet<int> { van };
        while (rij.Count > 0)
        {
            int m = rij.Dequeue();
            // Eerst de overgangen die in deze richting gereden zijn, daarna de omgekeerde van overgangen die naar m toe liepen (de loc rijdt dan dezelfde
            // route de andere kant op; zo komt de loc terug van een melder die alleen als eindpunt van een rit bekend is, zoals 12 achter wissel 1 en 2).
            var opties = kaart.Overgangen.Where(o => o.Van == m && o.GezienBijConfiguraties.Count > 0).OrderBy(o => o.Naar)
                .Select(o => (Naar: o.Naar, R: o.Richting, O: o))
                .Concat(kaart.Overgangen.Where(o => o.Naar == m && o.GezienBijConfiguraties.Count > 0).OrderBy(o => o.Van)
                    .Select(o => (Naar: o.Van, R: o.Richting.Om(), O: o)));
            foreach (var (nxt, rch, o) in opties)
            {
                if (!gezien.Add(nxt)) continue;
                var c = o.GezienBijConfiguraties.Select(Configuratie.VanSleutel).OrderBy(x => x.Afbuigend.Length).ThenBy(x => x.Sleutel, StringComparer.Ordinal).First();
                vorige[nxt] = (m, rch, c);
                if (nxt == naar)
                {
                    var weg = new List<(int, int, Richting, Configuratie)>();
                    int cur = naar;
                    while (cur != van) { var (v, r, cc) = vorige[cur]; weg.Add((v, cur, r, cc)); cur = v; }
                    weg.Reverse();
                    return weg;
                }
                rij.Enqueue(nxt);
            }
        }
        return null;
    }

    /// <summary>BUG #161 (Marco 15-10 13:51: de controle reed alleen de twee basisrondes; 18 overgangen met melder 129, 4, 11, 12, 1, 9, 10 waren 'niet gereden'):
    /// de controle begint zonder overgangen en plande daarom alleen "start, vooruit" en "start, achteruit". Nu plant ze daarna ook elk traject uit de bestaande kaart
    /// (zelfde startmelder, richting en wisselstand), met een route over de basis of over een ander traject dat langs de startmelder loopt. Trajecten die op een
    /// bekend kortsluitpunt zonder bekende oplossing eindigen worden niet gereden. Geeft true als er iets nieuws gepland is.</summary>
    private bool PlanControleTrajecten()
    {
        var oud = _status.Kaart;
        bool nieuw = false;
        foreach (var t in oud.Trajecten)
        {
            if (t.Reeks.Count < 2 || t.Einde == RitEinde.BekendKortsluitpunt) continue;
            if (t.Einde == RitEinde.Kortsluiting && !oud.BekendeKortsluitOplossingen.Any(s => s.NaMelder == t.Reeks[^1] && s.Richting == t.Richting)) continue;
            var o = new Opdracht { Configuratie = t.Configuratie, Start = t.Start, Richting = t.Richting, Reden = "controle: traject uit de kaart" };
            if (_status.GeplandeOpdrachten.Contains(o.Sleutel)) continue;
            var route = ControleRoute(oud, t, oud.Trajecten, new HashSet<Traject>());
            if (route is null) { _status.GeplandeOpdrachten.Add(o.Sleutel); _log.Info($"Controle: traject vanaf melder {t.Start} {t.Richting.Tekst()} ({t.Configuratie}) kan niet bereikt worden over de bekende wegen; overgeslagen."); continue; }
            o.Route = route;
            Plan(o);
            nieuw = true;
        }
        return nieuw;
    }

    internal static List<Etappe>? ControleRoute(Baankaart oud, Traject t, List<Traject> alle, HashSet<Traject> bezocht)
    {
        if (!bezocht.Add(t)) return null;
        var weg = ZelfTerugWeg(oud, oud.StartMelder, t.Start);
        if (weg is not null) return StappenNaarEtappes(weg);
        foreach (var a in alle.Where(a => !ReferenceEquals(a, t) && a.Einde != RitEinde.BekendKortsluitpunt && a.Reeks.Contains(t.Start)))
        {
            int i = a.Reeks.IndexOf(t.Start);
            var tot = ControleRoute(oud, a, alle, bezocht);
            if (tot is null) continue;
            if (i > 0) tot.Add(new Etappe { Configuratie = a.Configuratie, Richting = a.Richting, Van = a.Start, Naar = t.Start, Pad = a.Reeks.Take(i + 1).ToList() });
            return tot;
        }
        return null;
    }

    internal static List<Etappe> StappenNaarEtappes(List<(int Van, int Naar, Richting Richting)> stappen)
    {
        var res = new List<Etappe>();
        foreach (var s in stappen)
        {
            if (res.Count > 0 && res[^1].Richting == s.Richting && res[^1].Pad[^1] == s.Van) { res[^1].Pad.Add(s.Naar); res[^1].Naar = s.Naar; }
            else res.Add(new Etappe { Configuratie = Configuratie.Basis, Richting = s.Richting, Van = s.Van, Naar = s.Naar, Pad = new List<int> { s.Van, s.Naar } });
        }
        return res;
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

    internal static List<int> VoorspeldPad(Baankaart k, int start, Richting r, Configuratie c, bool metReferentie = true, int maxStappen = 8)
    {
        var pad = new List<int> { start };
        for (int i = 0; i < maxStappen; i++)
        {
            int cur = pad[^1];
            var kand = k.BekendeVolgende(cur, r, c);
            if (kand.Count == 0) kand = k.BekendeVolgende(cur, r);
            if (metReferentie && cur == KruiswisselReferentie.Midden && pad.Count >= 2)
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
        var pad = VoorspeldPad(_kaart, o.Start, o.Richting, c, Kruis.MetReferentie);
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
        string? kruisUitleg = null;
        if (Kruis.MetReferentie) c = KruisIngangStand(pad, c, o.BronAdres, out kruisUitleg);
        if (kruisUitleg is not null) _log.Info($"Opdracht #{o.Id}: {kruisUitleg} Rijstand: {c}.");
        // BUG #165: zonder referentie telt het eigen bewijs van de kaart voor de kruiswissel in het voorspelde pad (ingang, midden en uitgang).
        if (!Kruis.MetReferentie && pad.Count >= 3)
        {
            var vast = Kruis.Motoren.Where(m => o.Configuratie.Bevat(m) || o.BronAdres == m).ToHashSet();
            var cEigen = KruisBewijs.PasEigenBewijsToe(_kaart, Kruis, pad, c, vast, out var eigenUitleg);
            if (eigenUitleg.Count > 0) { _log.Info($"Opdracht #{o.Id}: {string.Join("; ", eigenUitleg)} (eigen kaart). Rijstand: {cEigen}."); c = cEigen; }
        }
        // BUG #170 (Marco 16-10 19:26: "je blijft 10 keer hangen op wissel 14"): een opdracht "andere tak van adres N, vanaf melder 14 andersom" erft de
        // wissels van de ontdekkingsrit (wissel 14 afbuigend, nodig voor 15 -> 8 -> 24). De rit vanaf 14 vooruit (14 - 24 - 21 - ... - 17 - 8) heeft die
        // wissel niet nodig, maar kwam met wissel 14 afbuigend aan op melder 8 vanaf 17: een BEKENDE kortsluiting (verboden ingang). Het voorspelde pad was
        // te kort (9 melders) om dat te zien. Nu telt het hele bekende pad (tot een lus) en gaan wissels die op dat pad een verboden ingang afbuigend zouden
        // nemen weer rechtdoor, behalve het geteste adres en kruismotoren.
        var padLang = VoorspeldPad(_kaart, o.Start, o.Richting, c, Kruis.MetReferentie, 30);
        if (padLang.Count >= 2)
        {
            var cV = _kaart.MetVerbodenIngangen(padLang, o.Richting, c, out var verbWijz);
            foreach (var m in Kruis.Motoren) cV = cV.MetStand(m, c.Bevat(m));
            if (o.BronAdres is int bronV) cV = cV.MetStand(bronV, c.Bevat(bronV));
            if (!cV.Equals(c))
            {
                _log.Info($"Opdracht #{o.Id}: het bekende pad {string.Join(" - ", padLang)} neemt een bekende kortsluitingsingang; " +
                          $"wissels die daar afbuigend kortsluiting geven gaan rechtdoor ({string.Join("; ", verbWijz)}). Rijstand: {cV}.");
                c = cV;
            }
        }
        // BUG #146: een bewezen overloop-paar (wissel 2 met 1) geldt in beide richtingen.
        c = _kaart.MetOverloopPartners(c, o.BronAdres, Kruis, out var partnerWijz);
        if (partnerWijz.Count > 0) _log.Info($"Opdracht #{o.Id}: {string.Join("; ", partnerWijz)}. Rijstand: {c}.");
        return c;
    }

    /// <summary>BUG #149 (Marco 18:57: "hoe meer je leerde, des te meer fouten"): een opdracht met een wissel BIJ de startmelder (wissel 1 en 2 bij melder 5) die
    /// afbuigend moet, vraagt dat de loc de startmelder binnenkomt vanaf de kant waar die stand bij hoort (12 -> 5 met 1 en 2 afbuigend; 16 -> 5 met beide
    /// rechtdoor). Eerder werd de loc steeds vanaf de verkeerde kant (16) aangereden en werden 1 en 2 daarna onder de staande loc omgezet: kortsluiting
    /// (17:29, 17:50, 18:41, 18:51). De kaart weet dit al (Doorgangen). Is voor de gewenste stand alleen een andere ingang bewezen dan die van de normale route,
    /// en is er een bekende weg in de rijrichting van de opdracht naar die ingang, dan wordt de loc daarlangs naar de startmelder gebracht.
    /// Anders null (normale route).</summary>
    internal static List<Etappe>? AankomstViaBewezenIngang(Baankaart kaart, Opdracht o, Configuratie rijstand, KruisKennis kruis, out string? uitleg)
    {
        uitleg = null;
        if (o.Route.Count == 0 || rijstand.Afbuigend.Length == 0) return null;
        var laatste = o.Route[^1];
        if (laatste.Naar != o.Start || laatste.Pad.Count < 2) return null;
        // alleen als een wissel van de rijstand bij de startmelder zelf ligt
        var bijStart = WisselsBijBezet(kaart, new[] { o.Start }).Where(a => rijstand.Bevat(a) && !kruis.IsMotor(a)).ToList();
        if (bijStart.Count == 0) return null;
        int normaleIngang = laatste.Pad[^2];
        var doorgangen = kaart.Doorgangen.Where(d => d.Melder == o.Start && d.Richting == o.Richting && d.Configuraties.Contains(rijstand.Sleutel)).ToList();
        if (doorgangen.Count == 0 || doorgangen.Any(d => d.Via == normaleIngang)) return null;
        var kandidaten = doorgangen.Select(d => d.Via).Distinct().OrderBy(x => x).ToList();
        int thuis = o.Route[0].Van;
        foreach (int via in kandidaten)
        {
            var weg = ZoekWegInRichting(kaart, thuis, o.Start, via, o.Richting, out var cfg);
            if (weg is null) continue;
            var c = cfg;
            foreach (var a in rijstand.Afbuigend) c = c.MetStand(a, true);
            uitleg = $"Opdracht #{o.Id} vraagt wissel {string.Join(" en ", bijStart)} afbuigend bij startmelder {o.Start}. Dat is alleen bewezen als de loc melder {o.Start} binnenkomt vanaf melder {via} " +
                     $"(vanaf melder {normaleIngang} niet). De loc rijdt daarom {o.Richting.Tekst()} via {string.Join(" - ", weg)} ({c}) en de wissels staan al goed voordat hij op melder {o.Start} komt.";
            return new List<Etappe> { new Etappe { Configuratie = c, Richting = o.Richting, Van = weg[0], Naar = o.Start, Pad = weg } };
        }
        return null;
    }

    /// <summary>BUG #149: kortste weg van <paramref name="van"/> naar <paramref name="doel"/> die uitsluitend over eerder in richting <paramref name="r"/> gereden
    /// overgangen loopt (ook de omgekeerde van de tegenovergestelde richting) en het doel binnenkomt vanaf <paramref name="via"/>. <paramref name="cfg"/> is de
    /// vereniging van de kleinste gereden wisselstand per overgang.</summary>
    internal static List<int>? ZoekWegInRichting(Baankaart kaart, int van, int doel, int via, Richting r, out Configuratie cfg)
    {
        cfg = Configuratie.Basis;
        var kleinste = new Dictionary<(int, int), Configuratie>();
        foreach (var ov in kaart.Overgangen)
        {
            if (ov.GezienBijConfiguraties.Count == 0) continue;
            var k = ov.GezienBijConfiguraties.Select(Configuratie.VanSleutel).OrderBy(x => x.Afbuigend.Length).ThenBy(x => x.Sleutel, StringComparer.Ordinal).First();
            if (ov.Richting == r) { if (!kleinste.TryGetValue((ov.Van, ov.Naar), out var b) || k.Afbuigend.Length < b.Afbuigend.Length) kleinste[(ov.Van, ov.Naar)] = k; }
            else if (ov.Richting == r.Om()) { if (!kleinste.TryGetValue((ov.Naar, ov.Van), out var b) || k.Afbuigend.Length < b.Afbuigend.Length) kleinste[(ov.Naar, ov.Van)] = k; }
        }
        var vorige = new Dictionary<int, int> { [van] = van };
        var rij = new Queue<int>(); rij.Enqueue(van);
        while (rij.Count > 0)
        {
            int cur = rij.Dequeue();
            if (cur == via && kleinste.ContainsKey((via, doel))) break;
            foreach (var (kant, _) in kleinste.Where(x => x.Key.Item1 == cur).OrderBy(x => x.Key.Item2))
            {
                int n = kant.Item2;
                if (vorige.ContainsKey(n) || n == doel) continue;
                vorige[n] = cur; rij.Enqueue(n);
            }
        }
        if (!vorige.ContainsKey(via) || !kleinste.ContainsKey((via, doel))) return null;
        var pad = new List<int> { doel };
        for (int x = via; ; x = vorige[x]) { pad.Insert(0, x); if (x == van) break; }
        if (pad.Count < 2 || pad.Take(pad.Count - 1).Distinct().Count() != pad.Count - 1) return null;
        var c = Configuratie.Basis;
        for (int i = 0; i + 1 < pad.Count; i++)
            foreach (var a in kleinste[(pad[i], pad[i + 1])].Afbuigend) c = c.MetStand(a, true);
        cfg = c;
        return pad;
    }

    private int AantalOplossingen() => _kaart.Kortsluitpunten.Count(k => k.Opgelost) + _kaart.BekendeKortsluitOplossingen.Count;

    /// <summary>BUG #175: de melder vlak vóór <paramref name="o"/>.Start (in de rijrichting van de opdracht), als de loc daar over een eerder gereden stuk met deze wisselstand
    /// vandaan komt. Geen aanloop bij de kruiswissel zelf.</summary>
    private int? KiesAanloop(Opdracht o)
    {
        if (!_ins.StapAanloop) return null;
        if (Kruis.Middens.Contains(o.Start) || o.Configuratie.Afbuigend.Length > 0) return null;   // opdrachten met een eigen wisselstand beginnen gewoon op hun startmelder
        // een ingang van de (bekende) kruiswissel heeft een eigen proef (VoerKruisProefUit); daar de loc een melder verder terug laten beginnen laat hem door de kruiswissel rijden
        if (_kaart.Overgangen.Any(x => x.AantalKeer > 0 && ((x.Van == o.Start && Kruis.Middens.Contains(x.Naar)) || (x.Naar == o.Start && Kruis.Middens.Contains(x.Van))))) return null;
        string sl = o.Configuratie.Sleutel;
        var kand = _kaart.Overgangen.Where(x => x.Naar == o.Start && x.Richting == o.Richting && x.AantalKeer > 0 && x.Van != o.Start
                                                 && !Kruis.Middens.Contains(x.Van)
                                                 && (x.GezienBijConfiguraties.Contains(sl) || (o.Configuratie.Afbuigend.Length == 0 && x.GezienBijConfiguraties.Contains(""))))
            .ToList();
        // de aanloopmelder zelf moet veilig zijn: ligt daar een wissel (de loc staat er dan op bij het omzetten), dan helpt een aanloop niet
        kand = kand.Where(x => WisselsBijBezet(_kaart, new[] { x.Van }).Count == 0).ToList();
        // draagt de rit al een eerder gevonden oplossing (extra afbuigende wissel) mee, dan blijft het bij de gewone start: dat zou de rit over het kortsluitpunt van die oplossing sturen
        kand = kand.Where(x => { _kaart.MetBekendeOplossingenVoorStart(x.Van, o.Richting, o.Configuratie, out var w); return w.Count == 0; }).ToList();
        if (kand.Count == 0) return null;
        var plek = HuidigePlek();
        return kand.OrderByDescending(x => x.Van == plek).ThenByDescending(x => x.AantalKeer).ThenBy(x => x.Van).First().Van;
    }

    /// <summary>BUG #176: staat de loc na de basisrit op een nieuwe melder zonder Dinamo-blok, dan volgt de blokproef meteen (nu hoeft de loc er niet nog eens heen).
    /// Alleen als de rijstand alle wissels rechtdoor heeft, er geen bekende wissel bij die melder ligt en de loc er werkelijk staat. Nog niet getest op de baan.</summary>
    private async Task BlokProefNaRit(Opdracht o, RitResultaat basis, Configuratie rijstand)
    {
        if (!BlokVereist) return;
        int m = basis.Reeks[^1];
        _monitor.Bijwerken();
        if (m == _kaart.StartMelder || !ProefMogelijk(m) || Kruis.Middens.Contains(m) || Kruis.OpKruis(_monitor.Bezet)) return;
        if (rijstand.Afbuigend.Length > 0 || !_monitor.Bezet.Contains(m)) return;   // na de basisrit staat de loc nog met een deel in de melder ervoor: nastellen zet hem helemaal op m
        if (WisselsBijBezet(_kaart, new[] { m }).Count > 0) return;
        // BUG #187 (Marco 17-10 14:20, echte log melder 13/5 en 14/24: "je zou helemaal doorrijden in die bezetmelder en dan de blokkentest doen, nu stop je al direct als de
        // nieuwe bezetmelder gezien wordt"): de blokproef volgt direct bij de EERSTE aankomst op een nieuwe melder, nadat de loc helemaal is doorgereden tot alleen die melder bezet is
        // (Nastellen hieronder). De tweede-bezoekregel (BUG #176) is vervallen.
        // een melder naast de kruiswissel: de loc zou bij het neerzetten de kruiswissel in kunnen rijden; die krijgt zijn blokproef los (VoerBlokProefUit)
        if (Buren(_kaart).TryGetValue(m, out var bm) && bm.Any(Kruis.Middens.Contains)) return;
        _bezig = $"blokproef voor melder {m}";
        MeldVoortgang();
        await Nastellen(m, o.Richting, achter: basis.Reeks[^2]);
        // BUG #187: de blokproef start alleen als ALLEEN de nieuwe melder bezet is (de oude melder mag niet meer actief zijn); lukt dat niet, dan geen proef op twee melders
        _monitor.Bijwerken();
        if (!(_monitor.Bezet.Count == 1 && _monitor.Bezet.Contains(m)))
        {
            _log.Waarschuwing($"Blokproef voor melder {m} uitgesteld: de loc staat niet alleen op melder {m} (bezet: {Lijst(_monitor.Bezet)}).");
            return;
        }
        _proefPogingen[m] = _proefPogingen.GetValueOrDefault(m) + 1;
        await BlokZoekenBijStart(m);
        // De blokproef laat de loc een stuk rijden, vooruit of achteruit. Terugzetten op m: staat hij deels op de melder voor m (waar hij vandaan kwam) dan verder in
        // de rijrichting, staat hij deels op een andere buurmelder dan terug. Zonder dit gokt Nastellen op de rijrichting en schiet de loc in de simulatie door.
        _monitor.Bijwerken();
        if (_monitor.Bezet.Count > 0)
        {
            int achter = basis.Reeks[^2];
            int? voor = _monitor.Bezet.Where(x => x != m && x != achter).Select(x => (int?)x).FirstOrDefault();
            await Nastellen(m, o.Richting, achter: achter, voor: voor);
        }
        BewaarNu();
    }

    private async Task VoerOpdrachtUit(Opdracht o)
    {
        await VoerOpdrachtUitKern(o);
        if (Stap) NaOpdrachtStap();
    }

    private async Task VoerOpdrachtUitKern(Opdracht o)
    {
        _handmatigeOplossingGemeld = false;
        if (Stap && !o.Aanloop && !o.KruisProef && !o.BlokProef && KiesAanloop(o) is int aanloopMelder)
        {
            _log.Info($"Opdracht #{o.Id}: de proefritten beginnen één melder eerder, op melder {aanloopMelder} (de loc staat dan niet op de wissels bij melder {o.Start} en wissels vóór melder {o.Start} worden ook gemeten).");
            o.Start = aanloopMelder;
            o.Aanloop = true;
        }
        o.Route = VerkortRoute(o.Route, o.Start); // ook voor opdrachten uit een eerder opgeslagen voortgang
        _fase = $"Opdracht #{o.Id}";
        _bezig = $"vanaf melder {o.Start} {o.Richting.Tekst()} ({o.Configuratie})";
        MeldVoortgang();
        _log.Stap($"Opdracht #{o.Id}: {_bezig} - {o.Reden}.");

        // BUG #172: een kruiswissel-proef rijdt met precies de gevraagde motorstanden.
        _stapDoel = o.Start;
        var rijstand = (o.KruisProef || o.BlokProef) ? o.Configuratie : RijStand(o);
        // BUG #145: een opdracht waarvan de aankomst op de startmelder al kortsluiting gaf (en niet bewezen oplosbaar is) wordt niet opnieuw gereden.
        // BUG #149: liever vanaf de bewezen ingang aankomen dan een wissel bij de startmelder onder de staande loc omzetten.
        string? ingangUitleg = null;
        var viaIngang = Stap ? null : AankomstViaBewezenIngang(_kaart, o, rijstand, Kruis, out ingangUitleg);   // BUG #172: stap voor stap kiest zelf de kortste weg
        if (ingangUitleg is not null) _log.Info(ingangUitleg);
        if (viaIngang is null && BekendeAankomstKortsluiting(o, rijstand) is { } bekend)
        {
            var t = $"Opdracht #{o.Id} (vanaf melder {o.Start} {o.Richting.Tekst()}, {o.Configuratie}) is overgeslagen: aankomst op melder {o.Start} met {bekend.Configuratie} geeft kortsluiting (kortsluitpunt #{bekend.Id}) en daar is geen werkende oplossing voor gevonden.";
            _log.Waarschuwing(t);
            if (!_kaart.Waarschuwingen.Contains(t)) _kaart.Waarschuwingen.Add(t);
            return;
        }
        try
        {
            if (o.KruisProef) { await VoerKruisProefUit(o); return; }
            if (o.BlokProef) { await VoerBlokProefUit(o); return; }   // BUG #174
            await NaarStartVan(viaIngang ?? o.Route, rijstand);
            // BUG #96 (Marco 16:01: "weer kortsluiting in het kruiswissel en motor 13 staat verkeerd"): motor 13 of 10 omzetten terwijl de loc op melder 24
            // staat geeft ALTIJD kortsluiting (log 15:40, 15:48, 14:40). Is dat hier nodig en kon het niet vooraf (#95), dan wordt de opdracht
            // overgeslagen en de loc terug gebracht, in plaats van weer een kortsluiting te veroorzaken.
            if (Kruis.OpKruis(_monitor.Bezet))
            {
                var huidig = new Configuratie(_wisselStand.Where(w => w.Value).Select(w => w.Key).ToArray());
                var omTeZetten = Kruis.Motoren.OrderBy(x => x)
                    .Where(a => (_wisselStand.TryGetValue(a, out var nu) ? nu : false) != rijstand.Bevat(a)).ToList();
                if (omTeZetten.Count > 0)
                {
                    var tekst = $"Opdracht vanaf melder {o.Start} {o.Richting.Tekst()} ({o.Configuratie}) is overgeslagen: motor {string.Join(" en ", omTeZetten)} zou omgezet moeten worden terwijl de loc op de kruiswissel (melder {Kruis.MiddenTekst}) staat, en dat geeft altijd kortsluiting. Vanaf deze ingangskant is de combinatie niet bewezen; niets omgezet.";
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

            // BUG #166 (Marco 16-10 18:04: "je zit in een loop", wissel 13 zes keer als verkeerd gemeld): de basisrit 15 -> 8 -> 24 -> 14 reed goed met motor 13 afbuigend,
            // maar de rijstand van de opdracht (berekend VOOR de basisrit, pad nog maar tot melder 24) had 13 rechtdoor. HerstelRijstand zette 13 daarom voor
            // elke proef terug ("stond afbuigend en moet rechtdoor staan"); elke proefrit gaf kortsluiting na melder 24 en die werd aan het proefadres
            // (wissel 1, 3, 4, 7, 8) toegeschreven: valse vondsten en een volgende opdracht met dezelfde fout. Een basisrit die door de kruiswissel is gereden
            // heeft bewezen welke stand van de kruismotoren werkt: die stand wordt de rijstand voor alle proeven.
            var rijstandBasis = KruisStandUitBasis(rijstand, basis.Reeks, Kruis, ad => _wisselStand.TryGetValue(ad, out var stnu) ? stnu : null,
                Kruis.Motoren.Where(m => o.Configuratie.Bevat(m) || o.BronAdres == m).ToHashSet(), out var basisUitleg);
            if (basisUitleg is not null)
            {
                _log.Info($"Opdracht #{o.Id}: {basisUitleg} Rijstand voor de proeven: {rijstandBasis}.");
                rijstand = rijstandBasis;
            }

            if (Stap) PlanVervolg(o, basis);   // BUG #172

            if (o.BronAdres is int bron && basis.Reeks.Count >= 2)
            {
                // BUG #197 (log 17-10 20:05: "wissel 5: na melder 13 rechtdoor -> 14, afbuigend -> 14"): de andere kant van een wissel werd altijd direct na de startmelder gezocht.
                // Ligt de wissel pas verderop (hier tussen 14 en 24/129: beide ritten beginnen met 13, 14), dan is de eerste melder gelijk en hoort de splitsing bij de melder
                // vóór het eerste verschil met de bekende rit zonder deze wissel.
                var splits = ZoekAndereKantSplitsing(_kaart, o, basis.Reeks, bron);
                if (splits is { } sp && !_kaart.Wissel(bron).Waarnemingen.Any(w => w.Soort == WaarnemingSoort.Splitsing && w.NaMelder == sp.Na && w.Richting == o.Richting))
                {
                    _kaart.Wissel(bron).Waarnemingen.Add(new WisselWaarneming
                    {
                        Soort = WaarnemingSoort.Splitsing,
                        NaMelder = sp.Na,
                        Richting = o.Richting,
                        VolgendeBijRechtdoor = sp.Rechtdoor,
                        VolgendeBijAfbuigend = sp.Afbuigend,
                        Configuratie = o.Configuratie.Zonder(bron).ToString(),
                        Toelichting = $"Na melder {sp.Na} ({o.Richting.Tekst()}): rechtdoor → melder {sp.Rechtdoor?.ToString() ?? "?"}, afbuigend → melder {sp.Afbuigend} (puntzijde, gevonden door andersom te rijden)."
                    });
                    _log.Vondst($"Adres {bron}, andere kant: na melder {sp.Na} ({o.Richting.Tekst()}) afbuigend → melder {sp.Afbuigend}.");
                }
            }

            // BUG #162 (Marco 15-10 16:19: "de controle heeft uren geduurd, wat niet echt nodig is"): een controle rijdt de trajecten van de kaart en vergelijkt;
            // alle adressen opnieuw testen (veertien extra rondes per opdracht, met wissels in vreemde standen) is geen controle maar een nieuwe verkenning.
            if (_status.Controle)
            {
                _log.Info("Controle: de rit is gereden en wordt vergeleken met de kaart; de adressen worden niet opnieuw getest.");
                await TerugNaarHuis(o.Route);
                return;
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
            int oplossingenBekend = AantalOplossingen();
            foreach (var a in _ins.WisselAdressen())
            {
                if (a < o.VolgendAdres) continue;
                // BUG #172: werd tijdens deze opdracht een kortsluiting opgelost met een extra wissel (wissel 1 bij wissel 2), dan rijden de volgende proeven daar meteen mee;
                // anders zette de proef die wissel na elke rit terug terwijl de loc ernaast stond en kwam de kortsluiting steeds terug.
                if (Stap && AantalOplossingen() != oplossingenBekend)
                {
                    oplossingenBekend = AantalOplossingen();
                    var nieuwStand = RijStand(o);
                    foreach (var x in nieuwStand.Afbuigend.Where(x => !rijstand.Bevat(x) && !Kruis.IsMotor(x) && x != a)) rijstand = rijstand.Met(x);
                }
                o.VolgendAdres = a;
                if (_status.Geidentificeerd.Contains(a) || o.Configuratie.Bevat(a)) continue;
                // BUG #201: een adres zonder enig effect hoeft niet opnieuw als elk stuk van deze rit al eens met dat adres afbuigend is gereden (zie RouteAlGedekt). Nog steeds één wissel tegelijk.
                if (!Kruis.IsMotor(a) && RouteAlGedekt(_kaart, basis.Reeks, o.Richting, a))
                {
                    _log.Info($"Adres {a} wordt in opdracht #{o.Id} niet opnieuw getest: elk stuk van deze rit is al eerder met adres {a} afbuigend gereden zonder effect (BUG #201).");
                    continue;
                }
                // BUG #172: de kruismotoren hebben alleen effect op de kruiswissel zelf; die krijgt een eigen proef (VoerKruisProefUit) vanaf elke ingang.
                // BUG #193 (Marco 17-10 16:28: "zet wisselmotoren 10 en 13 gewoon om tijdens de testrun, dan ontdek je snel de kruiswissel"): zolang de kruiswissel nog niet bekend is,
                // gaan de motoren gewoon mee in de adrestest; de rit laat zien waar ze geschakeld werden (andere melder, kortsluiting) en daaruit volgt de kruiswissel.
                if (Stap && Kruis.IsMotor(a)) continue;
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
                // BUG #118 (Marco 16:34, log 16:12-16:25): de opdracht reed met een extra wissel (opdracht 'afbuigend: 5' rijdt met 10 erbij, de rijstand), maar de
                // proefritten rekenden met de stand van de opdracht ZONDER die extra wissel. Daardoor werd een bekende omzet-kortsluiting (wissel 2 na melder 5,
                // wissel 6 na melder 129) niet herkend en reed de proef er tegenaan. Alles in de adrestest gaat nu uit van de werkelijke rijstand.
                var kortsluitStops = KortsluitStops(rijstand, o.Richting);
                foreach (var s in KortsluitStops(o.Configuratie, o.Richting)) kortsluitStops.Add(s);
                // BUG #106 (log 09:06): adres 14 afbuigend op de basisrit 17 -> 8 gaf zelf een kortsluiting, want de kaart weet al dat 14 daar
                // rechtdoor moet staan. De rit stopt nu vóór zo'n stap (op melder 17) in plaats van de kortsluiting zelf te veroorzaken.
                foreach (var s in EisConflictStops(_kaart, basis.Reeks, o.Richting, a)) kortsluitStops.Add(s);
                // BUG #115 (log 14:02 en 14:37): (1) wissel 2 afbuigend is al bekend als kortsluiting na melder 5; de proefrit reed er toch tegenaan.
                // (2) wissel 10 lag onder de loc (kruiswissel in melder 24) toen de rit daar stopte en hij werd teruggezet: kortsluiting. Stop dus
                // vóór de melder waar de wissel ligt, zodat hij zonder loc erop teruggezet wordt.
                foreach (var s in BekendeOmzetKortsluitStops(_kaart, a, o.Richting, rijstand)) kortsluitStops.Add(s);
                kortsluitStops = VeiligeStops(_kaart, basis.Reeks, a, kortsluitStops);
                // BUG #118: ligt de bekende kortsluiting (of de wissel zelf) direct na de startmelder, dan komt de stop op de startmelder zelf te liggen en
                // wordt die nooit 'bereikt': de rit zou er dwars doorheen rijden. Dan wordt dit adres hier niet getest.
                if (kortsluitStops.Contains(o.Start))
                {
                    _log.Info($"Adres {a} wordt in deze opdracht niet getest: de kaart weet dat omzetten direct na melder {o.Start} ({o.Richting.Tekst()}) kortsluiting geeft; de loc zou er bovenop staan.");
                    continue;
                }

                // BUG #121 (log 18:12:55): adres 10 werd getest met de loc op melder 24 (de opdracht begon daar): motor 13 of 10 omzetten terwijl de loc op de
                // kruiswissel staat geeft ALTIJD kortsluiting (zie #96), en de kortsluiting bleef daarna staan. Die adressen worden vanaf melder 24 niet getest.
                if (Kruis.IsMotor(a) && Kruis.OpKruis(_monitor.Bezet))
                {
                    _log.Info($"Adres {a} wordt in opdracht #{o.Id} niet getest: de loc staat op de kruiswissel (melder {Kruis.MiddenTekst}) en motor {a} omzetten onder de loc geeft altijd kortsluiting.");
                    continue;
                }
                // BUG #121 (log 18:11:57): na een terugweg kan de stand afwijken van de rijstand (motor 13 stond rechtdoor terwijl de opdracht hem afbuigend
                // vraagt); de proefrit heette 'afbuigend: 6, 13, 14' maar reed met 13 rechtdoor en leverde valse vondsten op (wissel 6, 7, 8 en 9 'splitsen').
                // Voor elke proef wordt de stand dus eerst weer gelijk gemaakt aan de rijstand; kan dat niet veilig, dan wordt dit adres niet getest.
                // BUG #216: de proef rijdt met de bekende oplossing van een kortsluitpunt erbij (bijv. wissel 14); die stand alvast als doel nemen, anders zet het herstel hem eerst terug en de proef meteen weer om.
                ProefStandMetOplossing(_kaart, _ins.WisselAdressen(), o.Start, o.Richting, rijstand, a, out var voorExtra, Kruis);
                if (!await HerstelRijstand(rijstand, a, voorExtra))
                {
                    _log.Waarschuwing($"Adres {a} wordt in opdracht #{o.Id} nu niet getest: de wisselstand kon niet veilig gelijk gemaakt worden aan de rijstand ({rijstand}); een proef met een andere stand zou valse vondsten geven.");
                    continue;
                }
                // BUG #209: kon een klemoplossing-wissel niet naar de rijstand terug (de loc stond nog op de melders waar hij het alarm wegnam), dan begon de proef met een afwijkende stand en gaf
                // "5,11"/"5,10"-vervuiling. Eerst de loc volledig op de startmelder zetten en opnieuw herstellen; lukt het dan nog niet, dan gaat de proef door maar #208 voorkomt een valse vondst (de proef overslaan liet adressen ongetest).
                if (_klemAfwijkend.Count > 0)
                {
                    var afwLijst = string.Join(", ", _klemAfwijkend.OrderBy(x => x));
                    _monitor.Bijwerken();
                    if (!(_monitor.Bezet.Count == 1 && _monitor.Bezet.Contains(o.Start)))
                    {
                        _log.Info($"Adres {a}: wissel {afwLijst} kon niet naar de rijstand terug; de loc wordt eerst op melder {o.Start} gezet en daarna wordt de stand opnieuw hersteld.");
                        await Nastellen(o.Start, o.Richting);
                        if (!await HerstelRijstand(rijstand, a, voorExtra)) { _log.Waarschuwing($"Adres {a} wordt in opdracht #{o.Id} nu niet getest: de wisselstand kon niet veilig gelijk gemaakt worden aan de rijstand ({rijstand})."); continue; }
                    }
                    if (_klemAfwijkend.Count > 0)
                        _log.Waarschuwing($"Adres {a}: wissel {afwLijst} staat nog anders dan de rijstand ({rijstand}) en kan niet omgezet worden; de proef gaat door, maar een kortsluiting wordt niet aan adres {a} toegeschreven (#208).");
                }
                await ZetWissel(a, true);
                // BUG #117 (Marco 16:12, log 15:58): wissel 2 afbuigend (rit achteruit vanaf 13) reed weer in de kortsluiting na melder 12 die de kaart al
                // kent én waarvan de oplossing bekend is (wissel 1 afbuigend). De proefrit krijgt die oplossing nu mee, net als een gewone opdracht.
                var proefCfg = ProefStandMetOplossing(_kaart, _ins.WisselAdressen(), o.Start, o.Richting, rijstand, a, out var proefExtra, Kruis);
                foreach (var (ex, st) in proefExtra) await ZetWissel(ex, st);
                if (proefExtra.Count > 0)
                    _log.Info($"Adres {a} afbuigend gaf eerder kortsluiting; de proef rijdt met de bekende oplossing: {string.Join(", ", proefExtra.Select(x => $"{x.Adres} {(x.Afbuigend ? "afbuigend" : "rechtdoor")}"))}.");
                var proef = await Rit(o.Start, o.Richting, new RitDoel
                {
                    Configuratie = proefCfg,
                    Basis = basis.Reeks,
                    ExtraNaAfwijking = _ins.ExtraMeldersNaAfwijking,
                    StopBijMelders = kortsluitStops,
                    StaartWissels = true,
                    ProefAdres = a
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
                var cA0 = proefCfg;
                bool afwijkendePad = ProefWijktAf(basis.Reeks, proef.Reeks);
                bool zelfdeKortsluiting = basis.Einde == RitEinde.Kortsluiting && proef.Reeks.SequenceEqual(basis.Reeks);
                (int Partner, RitResultaat Rit)? partner = null;
                // BUG #92 (log 14:38): wissel 10 afbuigend gaf kortsluiting zodra de loc op melder 24 (de kruiswissel) kwam en bleef die geven
                // zolang de loc daar stond. De partnerzoektocht (andere wissels meeschakelen) zette toen wissel 5 afbuigend terwijl de loc op de
                // kruiswissel stond, gaf het op ("zet geen wissels om") en liet Marco de loc terugzetten, terwijl de kortsluiting pas weg was toen
                // wissel 10 weer rechtdoor stond (14:39:13 -> opgeheven 14:39:15). Motor 13 en 10 zijn geen overloopwissels: hun stand per rit staat
                // in de kruiswissel-referentie. Dus geen partnerzoektocht; direct terugzetten (hieronder) en de kortsluiting als bevinding vastleggen.
                if (IsKruiswisselMotorKortsluiting(a, proef, Kruis))
                {
                    LeerKruisMidden(proef.Reeks[^1]);
                    _log.Vondst($"Adres {a} afbuigend geeft kortsluiting zodra de loc op melder {proef.Reeks[^1]} (kruiswissel) komt ({Lijst(proef.Reeks)}). " +
                                "Een kruiswissel-motor is geen overloopwissel: geen partnerzoektocht, wissel wordt meteen teruggezet.");
                }
                else if (proef.Einde == RitEinde.Kortsluiting && !zelfdeKortsluiting && proef.Reeks.Count >= 1)
                {
                    // BUG #150 (nagebouwde baan, 35 kortsluitingen in één verkenning waarvan 25 partnerproeven): reed de loc met dit adres afbuigend precies dezelfde melders
                    // als de basisrit en volgt daarna de kortsluiting, dan wordt de wissel van achteren (samenvoegend) bereden en is afbuigend gewoon de verkeerde stand voor
                    // deze ingang (bijv. wissel 2 afbuigend voor 16 -> 5). Een partnerwissel kan dat niet oplossen: elke proef is dan een extra, nutteloze kortsluiting.
                    // Een partner heeft alleen zin als de afbuigende stand de loc een ANDERE weg laat nemen dan de basisrit (de tak verschilt).
                    if (!afwijkendePad)
                        _log.Info($"Kortsluiting na melder {proef.Reeks[^1]} met adres {a} afbuigend, terwijl de loc dezelfde melders reed als bij rechtdoor ({Lijst(proef.Reeks)}): de wissel wordt hier van achteren bereden en afbuigend is voor deze ingang verkeerd. Geen partnerzoektocht (die kan dit niet oplossen en kost alleen extra kortsluitingen).");
                    else
                        partner = await PartnerwisselZoeken(o, a, proef, rijstand);
                }

                if (afwijkendePad)
                {
                    await TerugNaarMetControle(o, proef, cA0);
                    // BUG #191 (meting: met 'doorrijden en blokproef na herstel' kwamen 24 valse wissels, zonder dit 0): de wissel die de loc net een andere kant op stuurde ligt bij de
                    // melder waar de loc nu staat, maar de kaart kent hem pas na Vergelijk. Hij werd daardoor direct onder de loc teruggezet (de loc stond nog met zijn staart op de tong);
                    // de volgende proef reed dan de verkeerde tak en kreeg de eer. Nu eerst doorrijden tot de loc de wissel voorbij is (VrijRijdenVoorWissel), dan pas terugzetten.
                    // BUG #224 (log 18-10 12:10:18): wissel 5 splitst pas na melder 14; terug op melder 13 ligt hij een hele melder achter de loc. Het doorkruipen (4,8 s,
                    // "wissel 5 ligt bij melder 13") was dan niet nodig. Alleen als de afwijking direct na de startmelder begint, ligt de wissel bij de startmelder.
                    bool wisselBijStart = EersteAfwijking(basis.Reeks, proef.Reeks) == 1;
                    if (wisselBijStart) _bekendBijBezet.Add(a);
                    try { await ZetWissel(a, false); } finally { _bekendBijBezet.Remove(a); }
                    foreach (var (ex, _) in proefExtra) await ZetWisselNaProef(ex, rijstand.Bevat(ex), oplossingWissel: true);
                }
                else
                {
                    // BUG #35: de loc staat hier altijd stil; eerst terugzetten, dan de terugrit.
                    await ZetWisselNaProef(a, false);
                    foreach (var (ex, _) in proefExtra) await ZetWisselNaProef(ex, rijstand.Bevat(ex), oplossingWissel: true);
                    await TerugNaarMetControle(o, proef, rijstand);
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
            if (nf.Kortsluiting && WeerlegAankomstOplossing(o, rijstand)) o.Pogingen = 2;   // BUG #145: niet nog eens dezelfde rit
            await ZetConfiguratie(Configuratie.Basis, spaarOnderLoc: true);
            if (!await ProbeerZelfTerugNaarStart(o.Route, nf))
                await VraagLocTerugTeZetten(Stap ? RouteDoel(o.Route) : _kaart.StartMelder, $"{nf.Message}\n\n{(Stap ? "De verkenner gaat naar de plek van deze opdracht." : "De verkenner gaat terug naar de startmelder.")}");
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

    /// <summary>BUG #166: reed de basisrit door de kruiswissel heen (een melder van <paramref name="kruis"/>.Middens met een voorgaande én een volgende melder), dan is
    /// de stand van de kruismotoren tijdens die rit bewezen. Die stand (<paramref name="stand"/>: true = afbuigend, null = onbekend) vervangt die van
    /// <paramref name="rijstand"/>, behalve voor motoren die de opdracht zelf vastlegt (<paramref name="vast"/>). Geen kruiswissel in de rit: ongewijzigd.</summary>
    internal static Configuratie KruisStandUitBasis(Configuratie rijstand, IReadOnlyList<int> reeks, KruisKennis kruis, Func<int, bool?> stand, ISet<int> vast, out string? uitleg)
    {
        uitleg = null;
        int i = -1;
        for (int k = 1; k + 1 < reeks.Count; k++) if (kruis.Middens.Contains(reeks[k])) { i = k; break; }
        if (i < 0) return rijstand;
        var c = rijstand; var wijz = new List<string>();
        foreach (var m in kruis.Motoren.OrderBy(x => x))
        {
            if (vast.Contains(m)) continue;
            if (stand(m) is not bool st || st == rijstand.Bevat(m)) continue;
            c = c.MetStand(m, st); wijz.Add($"motor {m} {(st ? "afbuigend" : "rechtdoor")}");
        }
        if (wijz.Count > 0)
            uitleg = $"de basisrit reed goed door de kruiswissel ({reeks[i - 1]} - {reeks[i]} - {reeks[i + 1]}) met {string.Join(", ", wijz)}; die stand blijft de rijstand.";
        return c;
    }

    /// <summary>BUG #121: maakt de werkelijke wisselstand gelijk aan <paramref name="rijstand"/> (behalve het adres dat de proef zelf omzet). Wissels die bij een nu
    /// bezette melder liggen blijven zoals ze staan; kan een kruiswisselmotor niet omgezet worden omdat de loc op de kruiswissel staat, dan false.</summary>
    /// <summary>BUG #208: wissels die bij de laatste <see cref="HerstelRijstand"/> niet naar de rijstand teruggezet konden worden (de loc stond erbij of de stand was een klemoplossing).
    /// Een proefrit met zo'n afwijkende stand mag geen kortsluiting aan het proefadres toeschrijven.</summary>
    private readonly HashSet<int> _proefAfwijkend = new();
    /// <summary>BUG #209: het deel van <see cref="_proefAfwijkend"/> dat een nog actieve klemoplossing is (daar start de proef niet mee).</summary>
    private readonly HashSet<int> _klemAfwijkend = new();

    /// <summary>BUG #201: true als adres <paramref name="a"/> geen enkel effect had (geen waarneming in de kaart) en elke overgang van <paramref name="reeks"/> (ook omgekeerd) al eens is
    /// gereden met dat adres afbuigend. Dan kan een nieuwe proef over dit stuk niets nieuws opleveren. Een adres met waarnemingen of een reeks korter dan twee melders: altijd false.</summary>
    internal static bool RouteAlGedekt(Baankaart kaart, List<int> reeks, Richting r, int a)
    {
        if (reeks.Count < 2) return false;
        if (kaart.Wissels.Any(w => w.Adres == a && w.Waarnemingen.Count > 0)) return false;
        var omgekeerd = reeks.AsEnumerable().Reverse().ToList();
        // Alleen een eerdere betrouwbare rit over exact dezelfde reeks (of omgekeerd) met adres a afbuigend en zonder ander resultaat telt.
        return kaart.Ritten.Any(g => !g.Onbetrouwbaar && g.Einde == RitEinde.GelijkAanBasis && g.Afbuigend.Count == 1 && g.Afbuigend[0] == a
            && (g.Reeks.SequenceEqual(reeks) || g.Reeks.SequenceEqual(omgekeerd)));
    }

    private async Task<bool> HerstelRijstand(Configuratie rijstand, int proefAdres, List<(int Adres, bool Afbuigend)>? extra = null)
    {
        _proefAfwijkend.Clear();
        _klemAfwijkend.Clear();
        var bijBezet = WisselsBijBezet(_kaart, _monitor.Bezet);
        var teDoen = new List<(int Adres, bool Afb)>();
        foreach (var adres in _ins.WisselAdressen())
        {
            if (adres == proefAdres) continue;
            bool wens = extra is not null && extra.Any(e => e.Adres == adres) ? extra.First(e => e.Adres == adres).Afbuigend : rijstand.Bevat(adres);
            bool nu = _wisselStand.TryGetValue(adres, out var st) && st;
            if (nu == wens) continue;
            if (Kruis.IsMotor(adres) && Kruis.OpKruis(_monitor.Bezet)) return false;
            // BUG #194: een stand die net het alarm wegnam (wissel 1 bij wissel 2) niet terugzetten zolang de loc nog op die melders staat
            if (!wens && nu && _klemOplossing.TryGetValue(adres, out var ko) && _monitor.Bezet.Any(ko.Melders.Contains) && (_klok.Nu - ko.Tijd).TotalMinutes < 10) { _proefAfwijkend.Add(adres); _klemAfwijkend.Add(adres); continue; }
            if (bijBezet.Contains(adres)) { _proefAfwijkend.Add(adres); continue; }   // ligt bij de melder waar de loc staat: niet onder de loc omzetten (de staartwissels regelt de rit zelf, #107)
            teDoen.Add((adres, wens));
        }
        foreach (var (adres, afb) in teDoen)
        {
            _log.Info($"Rijstand hersteld voor de proef: wissel {adres} stond {(afb ? "rechtdoor" : "afbuigend")} en moet {(afb ? "afbuigend" : "rechtdoor")} staan.");
            await ZetWissel(adres, afb);
        }
        return true;
    }

    internal static void PlaatsHerpoging(List<Opdracht> wachtrij, Opdracht o, bool direct)
    {
        if (direct) wachtrij.Insert(0, o); else wachtrij.Add(o);
    }

    private bool _handmatigeOplossingGemeld;

    /// <summary>BUG #92: eindigde de proefrit met wissel <paramref name="adres"/> afbuigend in kortsluiting op melder 24 en is dat adres een van de
    /// twee kruiswisselmotoren (13 of 10)?</summary>
    internal static bool IsKruiswisselMotorKortsluiting(int adres, RitResultaat proef, KruisKennis? kruis = null)
    {
        kruis ??= KruisKennis.Referentie();
        if (!kruis.IsMotor(adres) || proef.Einde != RitEinde.Kortsluiting || proef.Reeks.Count < 1) return false;
        // BUG #129: is de kruiswissel-melder nog niet bekend (vanaf nul), dan telt elke kortsluiting van een kruismotor als zodanig: die melder wordt dan ontdekt
        return kruis.Middens.Count == 0 || kruis.Middens.Contains(proef.Reeks[^1]);
    }

    /// <summary>BUG #224: de plaats in <paramref name="proef"/> waar die voor het eerst van <paramref name="basis"/> afwijkt (-1 als hij niet afwijkt).</summary>
    internal static int EersteAfwijking(IReadOnlyList<int> basis, IReadOnlyList<int> proef)
    {
        for (int i = 0; i < proef.Count; i++)
            if (i >= basis.Count || proef[i] != basis[i]) return i;
        return -1;
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
    private async Task<(int Partner, RitResultaat Rit)?> PartnerwisselZoeken(Opdracht o, int a, RitResultaat proef, Configuratie? rijstand = null)
    {
        int x = proef.Reeks[^1];
        // BUG #213 (Marco 18-10 09:43, log 09:19-09:22): wissel 13 haalde het alarm weg (09:20:27 commando, 09:20:28 'geen kortsluiting meer'), pas 7 s later werd 14 als 'partner' afbuigend
        // gezet en 09:21:56 stond er 'Kortsluiting opgelost: adres 14 én 12 samen'. Staat er een wissel afbuigend die net het alarm wegnam (klemoplossing), dan is elke partnerproef
        // zinloos: het alarm is al weg, dus alles lijkt een oplossing. Geen partnerzoektocht en niets vastleggen.
        var klemAnders = _klemOplossing.Where(kv => kv.Key != a && _wisselStand.TryGetValue(kv.Key, out var kst) && kst
                && (_klok.Nu - kv.Value.Tijd).TotalMinutes < 10 && _monitor.Bezet.Any(kv.Value.Melders.Contains))
            .Select(kv => kv.Key).OrderBy(k => k).ToList();
        if (klemAnders.Count > 0)
        {
            _log.Waarschuwing($"Geen partnerzoektocht voor adres {a}: wissel {string.Join(", ", klemAnders)} haalde het alarm net weg en staat nog afbuigend. Het alarm is dus al weg; een 'oplossing' met adres {a} en een partner zou niets bewijzen.");
            return null;
        }
        var cBasis = rijstand ?? o.Configuratie;   // BUG #118: de werkelijke rijstand, niet de nominale stand van de opdracht
        var cA = cBasis.Met(a);
        var kandidaten = PartnerVolgorde(_kaart, _ins.WisselAdressen(), a, x, cBasis, _ins.MaxPartnerProeven, Kruis.Motoren.ToList());
        _log.Info($"Kortsluiting na melder {x} met adres {a} op afbuigend: dat wissel blijft afbuigend staan en de andere wissels worden één voor één meegeschakeld ({kandidaten.Count} adressen) om te zien of de kortsluiting verdwijnt (overloopwissels).");
        // BUG #109 (Marco 12:03): de partnerproef zette wissel 9 om terwijl de loc op melder 129 stond, waar die wissel ligt (hij heeft daar zijn
        // punt) en ontspoorde. Wissels die de kaart bij melder x noemen worden niet meer als partner geprobeerd.
        var bijX = WisselsBijBezet(_kaart, new[] { x });
        if (kandidaten.Any(bijX.Contains))
            _log.Info($"Niet als partner geprobeerd: wissel {string.Join(", ", kandidaten.Where(bijX.Contains).OrderBy(k => k))} (ligt bij melder {x} waar de loc staat; omzetten met de loc erop laat hem ontsporen).");
        kandidaten = kandidaten.Where(k => !bijX.Contains(k)).ToList();
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
            if (_wisselStand.TryGetValue(a, out var aStand) && !aStand)
            {
                _log.Info($"Wissel {a} stond tijdens het herstel weer op rechtdoor (BUG #141); hij gaat voor de partnerproef terug naar afbuigend.");
                await ZetWissel(a, true);
            }
            _bezig = $"opdracht #{o.Id}: adres {a} afbuigend laten staan, partner {b} proberen";
            MeldVoortgang();
            var cAb = cA.Met(b);
            await ZetWissel(b, true);
            // BUG #211: een partnerproef bewijst alleen iets als de werkelijke stand precies a + b (+ de rijstand) is. Staat er nog een andere wissel afbuigend (bijvoorbeeld een klemoplossing
            // of een achtergebleven proefstand), dan kan die de kortsluiting weghalen en lijkt elk adres een partner. Zulke wissels gaan eerst terug; kan dat niet, dan geen proef.
            var afwijkendPartner = _wisselStand.Where(kv => kv.Value != cAb.Bevat(kv.Key) && kv.Key != b).Select(kv => kv.Key).OrderBy(k => k).ToList();
            foreach (var w in afwijkendPartner.Where(w => !bijX.Contains(w)).ToList())
            {
                await ZetWissel(w, cAb.Bevat(w), forceer: true);
                afwijkendPartner.Remove(w);
            }
            if (afwijkendPartner.Count > 0)
            {
                _log.Waarschuwing($"Partnerzoektocht voor adres {a} afgebroken: wissel {string.Join(", ", afwijkendPartner)} staat anders dan de proefstand ({cAb}) en ligt bij de loc. Zo'n proef zou niets bewijzen (BUG #211).");
                await ZetWissel(b, false);
                return null;
            }
            _alarmStand.Clear();
            var rit = await Rit(x, o.Richting, new RitDoel { Configuratie = cAb, Basis = new List<int> { x }, ExtraNaAfwijking = 0 });
            // BUG #130 (Marco 11:42, log 11:32-11:36): de kortsluiting bleef staan terwijl de loc stilstond; ProbeerAlarmWegTeKrijgen haalde haar weg door
            // wissel 14 (= a) terug te zetten naar rechtdoor en de rit lukte daarna. Dat de rit slaagde bewijst dan alleen dat a afbuigend de kortsluiting
            // geeft, NIET dat a en b samen afbuigend een overloopverbinding vormen. De verkenner nam dat toch aan ("14 én 12 samen afbuigend"), plande daar
            // een opdracht voor, stuurde beide wissels afbuigend en kreeg bij aankomst op melder 8 opnieuw een kortsluiting die 5 minuten bleef staan
            // (Dinamo viel daarna uit). Is de stand tijdens de rit door de alarmcontrole veranderd, dan is de proef niet geldig.
            var ingreep = _alarmStand.ToList();
            bool alarmIngegrepen = ingreep.Count > 0;
            var werkelijk = alarmIngegrepen ? MetAlarmStand(cAb) : cAb;
            await TerugNaar(x, rit, werkelijk);
            await ZetWissel(b, false);
            if (alarmIngegrepen)
            {
                _log.Waarschuwing($"Adres {a} afbuigend met adres {b} geeft kortsluiting die pas wegging toen de verkenner de stand veranderde ({string.Join(", ", ingreep.Select(kv =>$"wissel {kv.Key} {(kv.Value ? "afbuigend" : "rechtdoor")}"))}). " +
                                  $"Dat bewijst geen overloopverbinding {a}+{b}: er wordt niets vastgelegd en geen verdere partner geprobeerd.");
                return null;
            }
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
    internal static List<int> PartnerVolgorde(Baankaart kaart, IEnumerable<int> alle, int a, int x, Configuratie c, int max, IReadOnlyCollection<int>? kruisMotoren = null)
    {
        kruisMotoren ??= Array.Empty<int>();
        return PartnerVolgordeIntern(kaart, alle, a, x, c, max, kruisMotoren);
    }

    private static List<int> PartnerVolgordeIntern(Baankaart kaart, IEnumerable<int> alle, int a, int x, Configuratie c, int max, IReadOnlyCollection<int> kruisMotoren)
    {
        bool BijMelder(int b) => kaart.Wissels.Any(w => w.Adres == b && w.Waarnemingen.Any(o => o.NaMelder == x || o.VolgendeBijRechtdoor == x || o.VolgendeBijAfbuigend == x));
        // BUG #150: de motoren van de Engelse wissel (door de gebruiker opgegeven) zijn verreweg de meest voorkomende partners (kruiswissel-referentie, 14-10: 24 vooruit met wissel 5 afbuigend
        // vroeg wissel 10): die gaan vóór de rest op afstand, ook als de kruiswissel-melder nog niet ontdekt is. Zonder opgegeven motoren verandert er niets.
        return alle.Where(b => b != a && !c.Bevat(b))
            .OrderBy(b => BijMelder(b) ? 0 : kruisMotoren.Contains(b) ? 1 : 2).ThenBy(b => Math.Abs(b - a)).ThenBy(b => b)
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

    /// <summary>BUG #197: waar splitst de rit met wissel <paramref name="bron"/> afbuigend van de rit zonder? Eerst direct na de startmelder (zoals voorheen, met de
    /// rechtdoor-melder uit de opdracht); is die melder gelijk aan de eerste melder van de rit, dan wordt de bekende rit zonder deze wissel (zelfde start en richting) gezocht
    /// en de eerste afwijking genomen. Null als er niets te zeggen valt (dan wordt geen valse splitsing "14 -> 14" vastgelegd).</summary>
    internal static (int Na, int? Rechtdoor, int Afbuigend)? ZoekAndereKantSplitsing(Baankaart kaart, Opdracht o, List<int> reeks, int bron)
    {
        if (reeks.Count < 2) return null;
        if (o.BronRechtdoorVolgende != reeks[1]) return (o.Start, o.BronRechtdoorVolgende, reeks[1]);
        var zonder = o.Configuratie.Zonder(bron);
        var ref0 = kaart.Trajecten.Where(t => t.Start == o.Start && t.Richting == o.Richting && t.Reeks.Count > 2 && t.Configuratie.Afbuigend.All(zonder.Bevat))
                                   .OrderBy(t => t.Configuratie.Afbuigend.Length).FirstOrDefault();
        if (ref0 is null) return null;
        for (int x = 1; x < Math.Min(ref0.Reeks.Count, reeks.Count); x++)
            if (ref0.Reeks[x] != reeks[x]) return (reeks[x - 1], ref0.Reeks[x], reeks[x]);
        return null;
    }

    /// <summary>De basisrit (zonder omgezet adres), bij de instelling "herhalen" twee keer,
    /// en bij verschil een derde keer als scheidsrechter.</summary>
    private async Task<RitResultaat?> Basisrit(Opdracht o)
    {
        var stops = KortsluitStops(o.Configuratie, o.Richting);
        RitResultaat? eerste = null;
        // BUG #172: stap voor stap vergelijkt elke proef toch met deze ene korte rit.
        // BUG #196 (log 17-10 20:05, Marco: "het duurt nogal lang"): ook bij optie 6 (rondrijden) was elke basisrit dubbel gereden (rit #1 en #2, #6 en #7, ...), met een derde als de overgangen
        // verschilden: een kwart van alle ritten. Optie 6 vergelijkt toch alleen de melders onderling, een enkele basisrit volstaat.
        int aantal = _ins.BasisritHerhalen && !Stap && !Rondrit ? 2 : 1;
        var ritten = new List<RitResultaat>();
        for (int i = 0; i < aantal + 1; i++)
        {
            if (i == aantal && (ritten.Count < 2 || ritten[0].Reeks.SequenceEqual(ritten[1].Reeks))) break;
            _bezig = $"opdracht #{o.Id}: basisrit {(i == 0 ? "" : $"(controle {i})")}";
            MeldVoortgang();
            var rijstandBasis = RijStand(o);
            // BUG #196 (log 17-10 20:03:54): de stops werden alleen aan het begin berekend. Werd het kortsluitpunt tijdens de eerste rit opgelost (wissel 5 afbuigend bij kortsluitpunt #3),
            // dan stopte de volgende basisrit (met 5 en 10 afbuigend) nog steeds op melder 14 alsof de kortsluiting er nog was. Elke rit rekent de stops opnieuw uit.
            if (i > 0) stops = KortsluitStops(o.Configuratie, o.Richting);
            var rit = await Rit(o.Start, o.Richting, new RitDoel { Configuratie = rijstandBasis, StopBijMelders = stops, StaartWissels = true, MaxStappen = Stap ? (o.Aanloop ? 2 : 1) : null });
            _log.Info($"Basisrit: {Lijst(rit.Reeks)} - einde: {EindeTekst(rit.Einde)}.");
            VerwerkEinde(o, rit);
            // BUG #176 (Marco 17-10 10:27: "waarom is het blok van deze melders nog niet bekend?"): de blokproef van een nieuwe melder kwam pas na alle ritten, terwijl
            // de loc er na deze rit al staat. Nu meteen, maar alleen als dat veilig is (alle wissels rechtdoor, geen bekende wissel bij die melder).
            if (Stap && !o.KruisProef && rit.Einde == RitEinde.StapKlaar && rit.Reeks.Count >= 2) await BlokProefNaRit(o, rit, rijstandBasis);
            // BUG #121 (log 18:16:37 en 17:01:58): de terugweg ging uit van de NOMINALE stand van de opdracht (afbuigend: 2) in plaats van de werkelijke
            // rijstand (afbuigend: 1, 2). Wissel 1 werd rechtdoor gezet terwijl de loc er nog op stond (kortsluiting, ontsporing); een tweede basisrit
            // begon bovendien met de verkeerde stand.
            await TerugNaarMetControle(o, rit, rijstandBasis);
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
            if (!Stap) _kaart.Trajecten.Add(new Traject { Configuratie = o.Configuratie, Start = o.Start, Richting = o.Richting, Reeks = gekozen.Reeks.ToList(), Einde = gekozen.Einde });   // BUG #172: stap voor stap bouwt de trajecten aan het eind (BouwTrajecten)
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

    /// <summary>BUG #136: het kortsluitpunt van de kortsluiting met deze melder en richting; bij meer punten dat met dezelfde wisselstand, anders het nieuwste
    /// nog niet opgeloste.</summary>
    /// <summary>BUG #145: kortsluitpunt waarbij de loc via dezelfde aankomst op <paramref name="o"/>.Start (de route eindigt daar) met een wisselstand
    /// die in <paramref name="rijstand"/> zit kortsluiting gaf en dat niet bewezen is op te lossen.</summary>
    internal Kortsluitpunt? BekendeAankomstKortsluiting(Opdracht o, Configuratie rijstand)
        => _kaart.Kortsluitpunten.Where(k => k.Soort == KortsluitpuntSoort.Kortsluiting && !k.Opgelost && k.Opgegeven
                                             && k.NaMelder == o.Start && k.Richting == o.Richting && k.Configuratie.Afbuigend.Length > 0
                                             && k.Route.Count > 0 && k.Route[^1].Naar == o.Start
                                             && k.Configuratie.Afbuigend.All(rijstand.Bevat))
                                .OrderByDescending(k => k.Id).FirstOrDefault();

    /// <summary>BUG #145 (Marco 17:29, steeds weer wissel 2): gaf de aankomst op de startmelder opnieuw kortsluiting terwijl een eerder kortsluitpunt daar als
    /// 'opgelost door wissel X' stond, dan klopte die oplossing niet voor deze aankomst (hij was gevonden met de loc stilstaand). De oplossing wordt
    /// ingetrokken en het punt telt als opgegeven, zodat de opdracht niet telkens weer tegen dezelfde kortsluiting aanrijdt.</summary>
    internal bool WeerlegAankomstOplossing(Opdracht o, Configuratie rijstand)
    {
        var kp = _kaart.Kortsluitpunten.Where(k => k.Soort == KortsluitpuntSoort.Kortsluiting && k.NaMelder == o.Start && k.Richting == o.Richting
                                                    && k.Configuratie.Afbuigend.Length > 0 && k.Route.Count > 0 && k.Route[^1].Naar == o.Start
                                                    && k.Configuratie.Afbuigend.All(rijstand.Bevat) && !k.Opgegeven)
                                       .OrderByDescending(k => k.Id).FirstOrDefault();
        if (kp is null) return false;
        if (kp.Opgelost) _log.Vondst($"Kortsluitpunt #{kp.Id}: de oplossing (wissel {kp.OpgelostDoorAdres}) werkt niet bij aankomst op melder {o.Start}; de kortsluiting kwam terug met {rijstand}. De oplossing wordt ingetrokken.");
        kp.Opgelost = false; kp.OpgelostDoorAdres = null; kp.OpgelostMetAfbuigend = null;
        kp.Opgegeven = true; kp.AantalKortsluitingen++;
        BewaarNu();
        return true;
    }

    private Kortsluitpunt? ZoekKortsluitpunt(int melder, Richting r, Configuratie stand)
    {
        var kand = _kaart.Kortsluitpunten.Where(k => k.Soort == KortsluitpuntSoort.Kortsluiting && k.NaMelder == melder && k.Richting == r).ToList();
        return kand.Where(k => k.Configuratie.Equals(stand)).OrderByDescending(k => k.Id).FirstOrDefault()
               ?? kand.Where(k => !k.Opgelost && !k.Opgegeven).OrderByDescending(k => k.Id).FirstOrDefault();
    }

    /// <summary>BUG #132/#136: legt een door de gebruiker aangewezen oplossing vast bij het kortsluitpunt.</summary>
    private bool KoppelHandOplossing(Kortsluitpunt kp, int adres, bool afbuigend)
    {
        // BUG #158: de gebruiker zette een kruismotor die in de geteste stand AFBUIGEND stond terug op rechtdoor en de kortsluiting was weg. Dan is niet
        // een extra wissel de oplossing, maar die motor zelf: in die stand rijdt de loc hier nooit goed. Het punt is daarmee verklaard; niet verder zoeken
        // met andere wisseladressen (dat gaf een eindeloze reeks proeven, telkens met dezelfde verkeerde motor).
        if (!afbuigend && Kruis.IsMotor(adres) && kp.Configuratie.Bevat(adres) && !kp.Opgelost)
        {
            kp.Opgegeven = true;
            if (!kp.GeprobeerdeAdressen.Contains(adres)) kp.GeprobeerdeAdressen.Add(adres);
            string uitleg = $"Kortsluitpunt #{kp.Id} (na melder {kp.NaMelder}, {kp.Richting.Tekst()}, {kp.Configuratie}) is verklaard door motor {adres}: afbuigend geeft hier kortsluiting, rechtdoor niet (volgens de gebruiker). Er wordt niet meer naar andere wissels gezocht; de verkenner rijdt hier voortaan met motor {adres} rechtdoor.";
            _kaart.Waarschuwingen.Add(uitleg);
            _log.Vondst(uitleg);
            _handmatigeOplossingGemeld = true;
            BewaarNu();
            return true;
        }
        if (!_kaart.RegistreerHandmatigeOplossing(kp, adres, afbuigend, Kruis)) return false;
        _log.Vondst($"Kortsluitpunt #{kp.Id} (na melder {kp.NaMelder}, {kp.Richting.Tekst()}, {kp.Configuratie}) is volgens de gebruiker opgelost door wissel {adres} op {(afbuigend ? "afbuigend" : "rechtdoor")}. Dit wordt voortaan in deze verkenning meteen toegepast; in een volgende verkenning alleen als u bij de voorkennis-vraag 'Ja' antwoordt.");
        _handmatigeOplossingGemeld = true; // BUG #102
        BewaarNu();
        return true;
    }

    /// <summary>BUG #132: past een door de gebruiker aangewezen oplossing toe op het kortsluitpunt van de kortsluiting waarbij hij is gegeven (zelfde melder en richting).</summary>
    private void PasWachtendeHandOplossingToe(Kortsluitpunt kp)
    {
        if (_wachtendeHandOplossing is not { } w) return;
        if (w.Melder != kp.NaMelder || w.Richting != kp.Richting) return;
        _wachtendeHandOplossing = null;
        if ((_klok.Nu - w.Tijd).TotalMinutes >= 15 || kp.Soort != KortsluitpuntSoort.Kortsluiting) return;
        KoppelHandOplossing(kp, w.Adres, w.Afbuigend);
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
            PasWachtendeHandOplossingToe(bestaand);
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
        PasWachtendeHandOplossingToe(k);
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

    /// <summary>BUG #115: melders waarna wissel <paramref name="a"/> afbuigend al eerder kortsluiting gaf (waarneming 'KortsluitingBijOmzetten' in dezelfde
    /// richting en dezelfde basisstand). De adrestest stopt daar in plaats van de kortsluiting nog eens op te zoeken.</summary>
    /// <summary>BUG #117: de stand voor de proefrit van adres <paramref name="a"/> afbuigend: heeft de kaart bij precies deze stand (basis + a) al een
    /// kortsluiting opgelost met een extra wissel, dan krijgt de proefrit die wissel mee. Kruiswissel-motoren (13 en 10) blijven buiten beschouwing:
    /// hun stand staat vast in de referentie en ze worden nooit extra omgezet. <paramref name="extra"/> = de extra wissels met hun stand in de proef.</summary>
    internal static Configuratie ProefStandMetOplossing(Baankaart kaart, IEnumerable<int> adressen, int start, Richting r, Configuratie basis, int a,
                                                        out List<(int Adres, bool Afbuigend)> extra, KruisKennis? kruis = null)
    {
        kruis ??= KruisKennis.Referentie();
        extra = new List<(int, bool)>();
        var cA = basis.Met(a);
        if (kruis.IsMotor(a)) return cA;
        var metOpl = kaart.MetBekendeOplossingenVoorStart(start, r, cA, out _);
        foreach (var x in adressen)
            if (x != a && metOpl.Bevat(x) != cA.Bevat(x)) extra.Add((x, metOpl.Bevat(x)));
        if (extra.Any(e => kruis.IsMotor(e.Adres)))
        {
            extra.Clear();
            return cA;
        }
        return metOpl;
    }

    internal static HashSet<int> BekendeOmzetKortsluitStops(Baankaart kaart, int a, Richting r, Configuratie c) =>
        kaart.Wissels.Where(w => w.Adres == a).SelectMany(w => w.Waarnemingen)
            .Where(o => o.Soort == WaarnemingSoort.KortsluitingBijOmzetten && o.Richting == r && ConfiguratieTekstIsDeelVan(o.Configuratie, c))
            .Select(o => o.NaMelder).ToHashSet();

    /// <summary>BUG #118: staan alle wissels die in <paramref name="tekst"/> (zoals <see cref="Configuratie.ToString"/>) afbuigend stonden, ook in <paramref name="c"/> afbuigend?
    /// Een kortsluiting die ontstond toen alleen die wissels afbuigend stonden, ontstaat ook als er elders nog een wissel extra afbuigend staat.</summary>
    internal static bool ConfiguratieTekstIsDeelVan(string tekst, Configuratie c)
    {
        if (tekst == Configuratie.Basis.ToString()) return true;
        const string kop = "afbuigend: ";
        if (!tekst.StartsWith(kop)) return false;
        var delen = tekst.Substring(kop.Length).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return delen.All(d => int.TryParse(d, out var n) && c.Bevat(n));
    }

    /// <summary>BUG #115: ligt wissel <paramref name="a"/> volgens de kaart direct na de stopmelder (waarneming met die NaMelder), dan zou de loc er bij het stoppen bovenop staan en
    /// bij het terugzetten van de wissel kortsluiting of ontsporing krijgen. De stop schuift dan op naar de melder ervoor in <paramref name="reeks"/>.</summary>
    internal static HashSet<int> VeiligeStops(Baankaart kaart, List<int> reeks, int a, IEnumerable<int> stops)
    {
        var res = new HashSet<int>();
        foreach (var m in stops)
        {
            int idx = reeks.IndexOf(m);
            if (idx >= 1 && kaart.Wissels.Any(w => w.Adres == a && w.Waarnemingen.Any(o => o.NaMelder == m))) res.Add(reeks[idx - 1]);
            else res.Add(m);
        }
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
        // BUG #208 (Marco 18-10 08:31: "wissel 11 kan ik fysiek niet vinden"; log 17-10 19:06-19:08): wissel 5 stond nog afbuigend (klemoplossing van 19:02) terwijl wissel 11 getest werd; 24 -> 14
        // met wissel 5 afbuigend geeft kortsluiting, en dat werd aan wissel 11 toegeschreven (het alarm ging weg toen wissel 5 rechtdoor ging, niet 11). Staat bij een proef met kortsluiting
        // een andere wissel dan de rijstand (niet terug te zetten), dan zegt de proef niets over het proefadres.
        if (proef.Einde == RitEinde.Kortsluiting && _proefAfwijkend.Count > 0)
        {
            string afwTekst = $"Proef met adres {a}: kortsluiting, maar wissel {string.Join(", ", _proefAfwijkend.OrderBy(x => x))} stond niet in de rijstand (kon niet teruggezet worden). De kortsluiting wordt daarom niet aan adres {a} toegeschreven; het adres wordt later opnieuw getest.";
            _log.Waarschuwing(afwTekst);
            _proefAfwijkend.Clear();
            return;
        }
        _proefAfwijkend.Clear();
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
                Via = t.Count >= 2 ? t[^2] : null,
                Richting = o.Richting,
                VolgendeBijRechtdoor = q,
                VolgendeBijAfbuigend = null,
                Configuratie = c.ToString(),
                Toelichting = q is null
                    ? (t.Count >= 2 ? $"Niet rijden van melder {t[^2]} naar {x} ({o.Richting.Tekst()}) als wissel {a} afbuigend staat: kortsluiting." : $"Op afbuigend: kortsluiting direct na melder {x}.")
                    : (t.Count >= 2 ? $"Niet rijden van melder {t[^2]} naar {x} ({o.Richting.Tekst()}) als wissel {a} afbuigend staat: kortsluiting (wissel wordt van achteren bereden; rechtdoor gaat het verder naar {q})."
                        : $"Op afbuigend: kortsluiting tussen melder {x} en {q}. Deze wissel wordt hier waarschijnlijk van achteren (samenvoegend) bereden.")
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

            // BUG #158: op de kruiswissel is een kruismotor die in de geteste stand afbuigend staat en daar al eerder kortsluiting gaf (waarneming
            // KortsluitingBijOmzetten bij dezelfde melder en richting) de verklaring; geen partner-wissels proberen.
            if (Kruis.Middens.Contains(k.NaMelder))
            {
                int motor = k.Configuratie.Afbuigend.FirstOrDefault(a => Kruis.IsMotor(a) && _kaart.Wissels.Any(w => w.Adres == a
                    && w.Waarnemingen.Any(x => x.Soort == WaarnemingSoort.KortsluitingBijOmzetten && x.NaMelder == k.NaMelder && x.Richting == k.Richting)));
                if (motor != 0)
                {
                    k.Opgegeven = true;
                    string uitleg = $"Kortsluitpunt #{k.Id} (na melder {k.NaMelder}, {k.Richting.Tekst()}, {k.Configuratie}) is al verklaard: motor {motor} afbuigend geeft op de kruiswissel bij deze melder en richting kortsluiting. Niet verder zoeken.";
                    _kaart.Waarschuwingen.Add(uitleg);
                    _log.Vondst(uitleg);
                    BewaarNu();
                    continue;
                }
            }
            int navigatieFouten = 0;

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
                // BUG #159: geen proef waarvan de route een verboden ingang met dit adres afbuigend neemt.
                if (afb && _kaart.IngangVerboden(k.Route, adres))
                {
                    k.GeprobeerdeAdressen.Add(adres);
                    _log.Info($"Kortsluitpunt #{k.Id}: adres {adres} op afbuigend wordt niet geprobeerd; de route daarheen neemt een ingang waarvan bekend is dat dat kortsluiting geeft.");
                    continue;
                }
                k.GeprobeerdeAdressen.Add(adres);
                var c = k.Configuratie.MetStand(adres, afb);
                _log.Stap($"Kortsluitpunt #{k.Id}: proberen met adres {adres} op {(afb ? "afbuigend" : "rechtdoor")}" + (partnerSet.Contains(adres) ? $" (naast {k.Configuratie}, overloopwissel?)." : "."));
                try
                {
                    _stapDoel = k.NaMelder;
                    await NaarStartVan(k.Route);
                    await ZetConfiguratie(c);
                    var rit = await Rit(k.NaMelder, k.Richting, new RitDoel { Configuratie = c, Basis = new List<int> { k.NaMelder }, ExtraNaAfwijking = 0 });
                    // BUG #72 (log 16:48): de uitkomst van de proef wordt EERST vastgelegd, daarna pas gaat de loc terug. Mislukte de weg terug
                    // (NavigatieFout), dan ging het gelukte resultaat (adres 13 afbuigend -> melder 14) verloren en telde het kortsluitpunt als opgegeven.
                    bool proefKortsluiting = rit.Einde == RitEinde.Kortsluiting;
                    // BUG #171 (log 19:41:51): de loc kwam op melder 24 en Dinamo meldde 6 ms later kortsluiting in blok 8 en 9; de proef gold als 'opgelost'
                    // (valse LostKortsluitingOp). Een proef is pas opgelost als er na aankomst even geen alarm is.
                    if (!proefKortsluiting && rit.Reeks.Count >= 2)
                    {
                        await Wacht(TimeSpan.FromMilliseconds(1500));
                        if (HuidigeAlarmBlokken().Count > 0 || _kortsluitingGemeld)
                        {
                            _log.Waarschuwing($"Kortsluitpunt #{k.Id}: de loc kwam op melder {rit.Reeks[^1]}, maar Dinamo meldt kortsluiting in blok {string.Join(" en ", HuidigeAlarmBlokken())}. Dat telt niet als opgelost.");
                            proefKortsluiting = true;
                        }
                    }
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
                            await VraagLocTerugTeZetten(Stap ? RouteDoel(k.Route) : _kaart.StartMelder, $"{nf.Message}\n\nDe uitkomst van de proef is bewaard. {(Stap ? "De verkenner gaat naar de plek van deze opdracht." : "De verkenner gaat terug naar de startmelder.")}");
                    }
                    if (proefKortsluiting) continue;
                    if (proefOpgelost) break;
                }
                catch (NavigatieFout nf)
                {
                    _log.Waarschuwing($"Kortsluitpunt #{k.Id}: {nf.Message}");
                    // BUG #158: een proef die stukloopt op een blijvende kortsluiting (loc stilstaand) telde nergens mee: het kortsluitpunt kwam nooit aan
                    // zijn maximum en de verkenner bleef eindeloos nieuwe adressen proberen (12:00-12:20: elke 2 minuten opnieuw, motor 10 steeds afbuigend).
                    navigatieFouten++;
                    if (nf.Kortsluiting) k.AantalKortsluitingen++;
                    await ZetConfiguratie(Configuratie.Basis, spaarOnderLoc: true);
                    if (!await ProbeerZelfTerugNaarStart(k.Route, nf))
                        await VraagLocTerugTeZetten(Stap ? RouteDoel(k.Route) : _kaart.StartMelder, $"{nf.Message}\n\n{(Stap ? "De verkenner gaat naar de plek van deze opdracht." : "De verkenner gaat terug naar de startmelder.")}");
                    if (navigatieFouten >= 3)
                    {
                        k.Opgegeven = true;
                        string w3 = $"Kortsluitpunt #{k.Id} (na melder {k.NaMelder}, {k.Richting.Tekst()}) opgegeven: {navigatieFouten} proeven achter elkaar liepen vast op een navigatiefout of blijvende kortsluiting.";
                        _kaart.Waarschuwingen.Add(w3);
                        _log.Waarschuwing(w3);
                        break;
                    }
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

    /// <summary>BUG #184 (sim, herhaalde kortsluiting na elke adrestest): na de proefrit staat de loc stil op de eindmelder; een wissel die de kaart bij die melder noemt (bv. wissel 1 bij
    /// melder 12) werd daar eerst rechtgezet en bij de terugweg weer afbuigend gezet: twee keer omzetten onder de loc, elke keer kortsluiting (en 36 s herstel). Zulke wissels blijven nu staan;
    /// de terugweg zet ze op de stand die de route nodig heeft, nadat de loc ver genoeg voorbij is. Nog niet getest op de baan.</summary>
    private async Task ZetWisselNaProef(int adres, bool afbuigend, bool oplossingWissel = false)
    {
        _monitor.Bijwerken();
        if (!Kruis.IsMotor(adres) && _monitor.Bezet.Count > 0 && (oplossingWissel || WisselsBijBezet(_kaart, _monitor.Bezet).Contains(adres))
            && !(_wisselStand.TryGetValue(adres, out var nu) && nu == afbuigend))
        {
            _log.Info($"Wissel {adres} blijft nu staan: de loc staat op een melder waar die wissel ligt; de terugweg zet hem op de stand van de route.");
            return;
        }
        await ZetWissel(adres, afbuigend);
    }

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
        // BUG #159: een verboden ingang (bijv. 16 -> 5 met wissel 2 afbuigend) wordt nooit gereden, ook niet via een geleerde oplossing.
        metOplossing = _kaart.MetVerbodenIngangen(pad, r, metOplossing, out var wijz4);
        if (wijz4.Count > 0)
            _log.Info($"Wisselstand voor het pad {string.Join("-", pad)} ({r.Tekst()}) aangepast om een bekende kortsluiting te vermijden: {string.Join("; ", wijz4)}. Nieuwe stand: {metOplossing}.");
        // BUG #85: de vaste kruiswissel-referentie (motor 13 en 10 bij een rit door melder 24) gaat voor alles wat uit kortsluitingen is afgeleid.
        var wijz3 = new List<string>();
        var metKruis = Kruis.MetReferentie ? KruiswisselReferentie.PasToe(pad, metOplossing, out wijz3) : metOplossing;
        if (wijz3.Count > 0)
            _log.Info($"Wisselstand voor het pad {string.Join("-", pad)} ({r.Tekst()}) aangepast aan de kruiswissel-referentie: {string.Join("; ", wijz3)}. Nieuwe stand: {metKruis}.");
        return metKruis;
    }

    /// <summary>BUG #95 (Marco 15:51: opdracht 24 achteruit met wissel 10 afbuigend gaf elke keer kortsluiting): begint de opdracht op de kruiswissel
    /// (melder 24) en moet daar motor 13 of 10 anders staan dan nu, dan wordt die wissel omgezet terwijl de loc op de kruiswissel staat - kortsluiting.
    /// Is de ingang (het been waar de loc vandaan komt) volgens de referentie samen met de gewenste standen bewezen, dan worden die standen al
    /// VOOR het inrijden van melder 24 gezet; bij het neerzetten hoeft er dan niets meer onder de loc om.</summary>
    internal static Configuratie KruisAankomstStand(Etappe e, Configuratie cfg, Configuratie doel, out string? uitleg)
        => KruisAankomstStand(e, cfg, doel, KruisKennis.Referentie(), null, out uitleg);

    /// <summary>BUG #135: zoals hierboven, maar voor de Engelse wissels van DEZE verkenning (<paramref name="kruis"/>) en met het eigen bewijs uit de kaart
    /// (een eerdere doorgang via dezelfde ingang met dezelfde motorstand); de vaste referentie telt alleen mee als die actief is.</summary>
    internal static Configuratie KruisAankomstStand(Etappe e, Configuratie cfg, Configuratie doel, KruisKennis kruis, Baankaart? kaart, out string? uitleg)
    {
        uitleg = null;
        if (!kruis.Middens.Contains(e.Naar) || e.Pad.Count < 2 || kruis.Motoren.Count == 0) return cfg;
        int ingang = e.Pad[^2];
        var motoren = kruis.Motoren.OrderBy(x => x).ToList();
        var gewenst = KruisBewijs.Project(doel, motoren);
        if (KruisBewijs.Project(cfg, motoren).Equals(gewenst)) return cfg;
        string? bron = null;
        if (kruis.MetReferentie && e.Naar == KruiswisselReferentie.Midden)
        {
            bool w13 = doel.Bevat(KruiswisselReferentie.Motor13), w10 = doel.Bevat(KruiswisselReferentie.Motor10);
            bool Past(KruiswisselReferentie.Verbinding v) => v.Bewezen
                && (v.MelderA == ingang || v.MelderB == ingang)
                && (v.Afbuigend13 is not bool a || a == w13) && (v.Afbuigend10 is not bool b || b == w10);
            if (KruiswisselReferentie.Verbindingen.Any(Past)) bron = "referentie";
        }
        if (bron is null && kaart is not null && KruisBewijs.GezienBijIngang(kaart, kruis, e.Naar, ingang).Any(g => g.Equals(gewenst))) bron = "eigen kaart";
        if (bron is null) return cfg;
        var nieuw = cfg;
        foreach (var m in motoren) nieuw = nieuw.MetStand(m, gewenst.Bevat(m));
        uitleg = $"De opdracht begint op de kruiswissel (melder {e.Naar}); de loc komt van melder {ingang}. {KruisBewijs.StandTekst(motoren, gewenst)} worden al VOOR het inrijden gezet ({bron}: dit past bij die ingang), zodat er niets onder de staande loc omgezet hoeft te worden.";
        return nieuw;
    }

    /// <summary>BUG #119 (Marco 17:14: "het kruiswissel werd geschakeld terwijl de loc er half op stond"): de opdracht begon op melder 14, naast de
    /// kruiswissel. Pas NA aankomst op 14 werden motor 10 (en 5) omgezet; de loc reikte dan nog tot in de kruiswissel en ontspoorde. Eindigt de
    /// laatste etappe van de aanloop niet op of via de kruiswissel zelf, dan worden motor 13 en 10 al VOOR die laatste etappe op de gewenste stand
    /// gezet, terwijl de loc nog ver van de kruiswissel staat.</summary>
    internal static Configuratie KruisVoorafStand(Etappe e, Configuratie cfg, Configuratie doel, out string? uitleg)
        => KruisVoorafStand(e, cfg, doel, KruisKennis.Referentie(), out uitleg);

    /// <summary>BUG #135: voor de Engelse wissels van deze verkenning; werkt dus ook zonder de vaste referentie (verkenning vanaf nul).</summary>
    internal static Configuratie KruisVoorafStand(Etappe e, Configuratie cfg, Configuratie doel, KruisKennis kruis, out string? uitleg)
    {
        uitleg = null;
        if (kruis.Middens.Count == 0 || kruis.Motoren.Count == 0 || e.Pad.Any(kruis.Middens.Contains)) return cfg;   // dat geval regelt KruisAankomstStand
        var motoren = kruis.Motoren.OrderBy(x => x).ToList();
        var gewenst = KruisBewijs.Project(doel, motoren);
        if (KruisBewijs.Project(cfg, motoren).Equals(gewenst)) return cfg;
        uitleg = $"{KruisBewijs.StandTekst(motoren, gewenst)} worden al gezet voordat de loc de startmelder {e.Naar} inrijdt: daar staat hij vlak bij de kruiswissel en omzetten onder de loc laat hem ontsporen.";
        foreach (var m in motoren) cfg = cfg.MetStand(m, gewenst.Bevat(m));
        return cfg;
    }

    private async Task NaarStartVan(List<Etappe> route, Configuratie? doel = null)
    {
        // BUG #172: stap voor stap: niet via de startmelder, maar over de kortste bekende weg vanaf waar de loc nu staat.
        if (Stap && !_stapNavigeert)
        {
            await NaarPlekMelder(RouteDoel(route), doel);
            return;
        }
        for (int i = 0; i < route.Count; i++)
        {
            var e = route[i];
            var cfg = MetEisen(e.Pad, e.Richting, KiesBijIngang(e.Van, e.Richting, e.Pad, e.Configuratie));
            if (doel is not null && i == route.Count - 1)
            {
                // BUG #135: ook in een verkenning vanaf nul (zonder vaste referentie) met de eigen kaart als bewijs
                cfg = KruisAankomstStand(e, cfg, doel, Kruis, _kaart, out var uitleg);
                if (uitleg is not null) _log.Info(uitleg);
                cfg = KruisVoorafStand(e, cfg, doel, Kruis, out var uitleg2);   // BUG #119
                if (uitleg2 is not null) _log.Info(uitleg2);
                cfg = StartmelderWisselsVooraf(_kaart, e, cfg, doel, out var uitleg4, Kruis);   // BUG #122
                if (uitleg4 is not null) _log.Info(uitleg4);
            }
            else if (i < route.Count - 1)
            {
                // BUG #119 (log 17:13:41): de volgende etappe (14 -> ... -> 24 -> ...) rijdt door de kruiswissel; motor 10 werd pas omgezet toen de loc al op
                // melder 14 stond. De standen van de kruiswissel voor de volgende etappe worden daarom al in deze etappe gezet.
                var volg = route[i + 1];
                var cVolg = _kaart.MetWisselEisen(volg.Pad, volg.Richting, KiesBijIngang(volg.Van, volg.Richting, volg.Pad, volg.Configuratie), out _);
                cfg = KruisVoorafStand(e, cfg, cVolg, Kruis, out var uitleg3);   // BUG #135: ook zonder vaste referentie
                if (uitleg3 is not null) _log.Info(uitleg3);
            }
            if (doel is not null && i == route.Count - 1)
            {
                // BUG #146 (Marco 17:29/17:50, wissel 2 bij melder 5): zie AankomstZonderKortsluitWissels
                cfg = AankomstZonderKortsluitWissels(_kaart, e, cfg, out var uitleg5);
                if (uitleg5 is not null) _log.Info(uitleg5);
            }
            await ControleerGeenAlarmVoorStap($"navigatie {e.Van} -> {e.Naar}");
            cfg = MetAlarmStand(cfg);
            await ZetConfiguratie(cfg);
            await Rit(e.Van, e.Richting, new RitDoel { Configuratie = cfg, DoelMelder = e.Naar, VerwachtPad = e.Pad });
        }
    }

    /// <summary>BUG #146 (Marco 17:29 en 17:50): reed de loc eerder via DEZELFDE laatste etappe (zelfde aankomstmelder en -richting) in een kortsluiting terwijl
    /// wissel(s) W afbuigend stonden (kortsluitpunt met die route), dan blijven W (en de wissel die daar als oplossing bij hoort) tijdens die aankomst rechtdoor.
    /// Ze worden pas gezet als de loc op de startmelder staat, zoals de partnerrit deed die wissel 1+2 wel goed reed (5 -> 13). Zo botst #122 (wissels bij de
    /// startmelder vooraf zetten) niet meer met een bekende kortsluiting bij het aanrijden vanaf melder 16.</summary>
    internal static Configuratie AankomstZonderKortsluitWissels(Baankaart kaart, Etappe e, Configuratie cfg, out string? uitleg, KruisKennis? kruis = null)
    {
        uitleg = null;
        // BUG #160 (log 12:33, opdracht #12): wissel 10 (kruismotor) werd tijdens de aankomst op melder 129 vastgehouden, terwijl 24 -> 129 hem afbuigend
        // nodig heeft: de loc reed 24 -> 14, er kwam een onterechte 'verkeerde wissel'-melding en de opdracht werd overgeslagen. Wissels die de route zelf
        // afbuigend eist (en kruismotoren op een route over de kruiswissel) blijven dus gewoon staan.
        kruis ??= KruisKennis.Referentie();
        var nodig = kaart.MetWisselEisen(e.Pad, e.Richting, Configuratie.Basis, out _);
        bool overKruis = e.Pad.Any(kruis.Middens.Contains);
        var wissels = new SortedSet<int>();
        foreach (var k in kaart.Kortsluitpunten.Where(k => k.Soort == KortsluitpuntSoort.Kortsluiting && k.Configuratie.Afbuigend.Length > 0 && k.Route.Count > 0
                                                           && k.Route[^1].Naar == e.Naar && k.Route[^1].Richting == e.Richting && k.NaMelder == e.Naar
                                                           && (k.Route[^1].Pad.Count < 2 || e.Pad.Count < 2 || k.Route[^1].Pad[^2] == e.Pad[^2])))   // BUG #149: alleen dezelfde ingang
        {
            foreach (var a in k.Configuratie.Afbuigend) wissels.Add(a);
            // BUG #192 (Marco 17-10, log 16:01): wissel 2 is voor deze route nodig (kan niet rechtdoor blijven); dan mag zijn oplossingspartner (wissel 1, 'wissel 1 met
            // wissel 2') niet rechtdoor blijven, want 2 afbuigend zonder 1 is precies de stand die de kortsluiting gaf. De loc reed zo opnieuw in dezelfde kortsluiting.
            bool eigenNodig = k.Configuratie.Afbuigend.Any(a => nodig.Bevat(a));
            if (k.Opgelost && k.OpgelostMetAfbuigend == true && k.OpgelostDoorAdres is int oa && !eigenNodig) wissels.Add(oa);
        }
        var weg = wissels.Where(a => cfg.Bevat(a) && !nodig.Bevat(a) && !(overKruis && kruis.IsMotor(a))).ToList();
        if (weg.Count == 0) return cfg;
        foreach (var a in weg) cfg = cfg.MetStand(a, false);
        uitleg = $"Aankomst op melder {e.Naar}: wissel {string.Join(" en ", weg)} blijft nog rechtdoor (de aankomst met die wissel afbuigend gaf eerder kortsluiting); ze worden gezet zodra de loc op melder {e.Naar} staat.";
        return cfg;
    }

    /// <summary>BUG #122 (log 18:18, opdracht #4 vanaf melder 5, wissel 2 afbuigend): de wissels 1 en 2 liggen bij startmelder 5 en werden pas omgezet toen de
    /// loc al op melder 5 stond: direct kortsluiting, een onterecht kortsluitpunt en een onterechte 'oplossing'. Dezelfde rit slaagde om 17:50 toen deze wissels
    /// gezet waren terwijl de loc nog op melder 13 stond. De wissels die de kaart bij de aankomstmelder van de laatste etappe noemt (en niet bij een
    /// melder waar de loc eerder op die etappe staat) krijgen daarom hun rijstand al tijdens de laatste etappe, dus voordat de loc aankomt.</summary>
    internal static Configuratie StartmelderWisselsVooraf(Baankaart kaart, Etappe e, Configuratie cfg, Configuratie doel, out string? uitleg, KruisKennis? kruis = null)
    {
        kruis ??= KruisKennis.Referentie();
        uitleg = null;
        if (e.Pad.Count < 2) return cfg;
        // Onder de loc: wissels die de kaart ACHTER een melder noemt waar de loc nog staat of langskomt (NaMelder). Een wissel die alleen als vervolgmelder
        // aan zo'n melder hangt (wissel 2 bij 13, 14 -> 5) ligt ertussen en mag vooraf (17:50 op 12-10 ging dat goed).
        var wachtMelders = e.Pad.Take(e.Pad.Count - 1).ToHashSet();
        var onderLoc = kaart.Wissels.Where(w => w.Waarnemingen.Any(o => wachtMelders.Contains(o.NaMelder))).Select(w => w.Adres).ToHashSet();
        var bijStart = WisselsBijBezet(kaart, new[] { e.Naar });
        // Wissels waarvan de kaart nog niets weet (bv. wissel 1, alleen bekend als oplossing) en die de rijstand afbuigend wil: ook vooraf.
        foreach (var a in doel.Afbuigend)
            if (!kaart.Wissels.Any(w => w.Adres == a && w.Waarnemingen.Count > 0)) bijStart.Add(a);
        var eis = kaart.MetWisselEisen(e.Pad, e.Richting, Configuratie.Basis, out _);
        var gezet = new List<string>();
        foreach (var a in bijStart.OrderBy(x => x))
        {
            if (kruis.IsMotor(a)) continue;   // die hebben hun eigen regels (#95, #119)
            if (onderLoc.Contains(a) || eis.Bevat(a) || cfg.Bevat(a) == doel.Bevat(a)) continue;
            cfg = cfg.MetStand(a, doel.Bevat(a));
            gezet.Add($"{a} {(doel.Bevat(a) ? "afbuigend" : "rechtdoor")}");
        }
        if (gezet.Count > 0) uitleg = $"Wissels bij startmelder {e.Naar} worden al gezet voordat de loc daar aankomt: {string.Join(", ", gezet)}.";
        return cfg;
    }

    /// <summary>BUG #121 (log 18:11:42): de terugweg naar startmelder 24 (de kruiswissel) zette motor 13 rechtdoor door een oude 'oplossing' (stand 13 afbuigend gaf
    /// kortsluiting), terwijl de opdracht (24 vooruit, 13 afbuigend) hem afbuigend nodig had. Op de kruiswissel zelf kan hij daarna niet meer omgezet worden;
    /// de proefritten reden daardoor met een andere stand dan ze meldden. Eindigt de terugweg op de kruiswissel, dan krijgen motor 13 en 10 vóór het inrijden de
    /// stand van de opdracht, als de referentie die combinatie vanaf deze ingangskant bewezen heeft.</summary>
    private Configuratie KruisStandBijTerugweg(List<int> pad, Richting r, int start, Configuratie cfg, Configuratie opdrachtStand)
    {
        if (!Kruis.Middens.Contains(start) || pad.Count < 2) return cfg;   // BUG #135: ook zonder vaste referentie
        var nieuw = KruisAankomstStand(new Etappe { Van = pad[0], Naar = start, Richting = r, Pad = pad }, cfg, opdrachtStand, Kruis, _kaart, out var uitleg);
        if (uitleg is not null) _log.Info("Terugweg: " + uitleg);
        return nieuw;
    }

    /// <param name="eersteConfiguratie">BUG #73: na een proef met een extra wissel op een kruiswissel-sectie blijft de wisselstand van die
    /// proef staan voor het EERSTE stuk van de terugweg; pas daarna volgen de standen van de route.</param>
    private async Task TerugNaarHuis(List<Etappe> route, Configuratie? eersteConfiguratie = null)
    {
        if (Stap) return;   // BUG #172: de loc blijft waar hij is; de volgende opdracht rijdt zelf naar zijn plek
        for (int i = route.Count - 1; i >= 0; i--)
        {
            var e = route[i];
            var cfg = i == route.Count - 1 && eersteConfiguratie is not null ? eersteConfiguratie : e.Configuratie;
            var pad = e.Pad.ToList();
            pad.Reverse();
            cfg = MetEisen(pad, e.Richting.Om(), KiesBijIngang(e.Naar, e.Richting.Om(), pad, cfg));
            await ControleerGeenAlarmVoorStap($"terugweg {e.Naar} -> {e.Van}");
            cfg = MetAlarmStand(cfg);
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
                // BUG #197 (log 17-10 20:05, open punt #1 "heen 16 naar 5, terug 5 naar 16 - niet terug naar 16"): ging de loc terug naar de melder waar hij vandaan kwam, dan is dat
                // geen onverwachte terugweg maar gewoon terug; het verschil zat in de verwachte route van de navigatie (hier wissel 2). Geen kortsluitpunt aanmaken.
                if (nf.Onverwacht == w)
                {
                    _log.Info($"Terugweg na melder {w}/{y}: de loc kwam terug op melder {w}, dus gewoon de weg waarlangs hij heen kwam; geen onverwachte terugweg (de verwachte route van de navigatie klopte niet).");
                    throw;
                }
                // BUG #197 (log 17-10 19:15, open punt #4: 8 naar 24 en terug 24 naar 21): melder y heeft al drie of meer buren (kruiswissel of splitsing in dezelfde melder): daar hoeft de
                // terugweg niet langs de heenweg te gaan. De heenweg 'w naar y' is dan wel gewoon gereden en blijft in de kaart; het is geen mislukte overgang.
                if (Buren(_kaart).TryGetValue(y, out var burenY) && burenY.Count >= 3)
                {
                    string uitleg = $"Melder {y} heeft meer dan twee buren ({string.Join(", ", burenY.OrderBy(x => x))}), dus een kruiswissel of splitsing in of bij die melder. De overgang {w} naar {y} ({o.Richting.Tekst()}, {c}) is gereden en blijft in de kaart; dat de loc daarna via melder {nf.Onverwacht} terugkwam is hier te verwachten.";
                    _log.Info(uitleg);
                    throw;
                }
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
    /// <summary>BUG #215: knipt een lus uit een pad. Komt melder X eerst op plaats i en later op plaats j voor (i &lt; j), en kent de kaart de overgang X -> pad[j+1] in
    /// <paramref name="richting"/>, dan wordt het pad pad[..i] + pad[j..]. Bij meerdere kandidaten wordt de langste lus eerst geknipt.</summary>
    internal static List<int> KnipLus(Baankaart kaart, List<int> pad, Richting richting)
    {
        var res = pad.ToList();
        bool geknipt = true;
        while (geknipt)
        {
            geknipt = false;
            for (int i = 0; i < res.Count && !geknipt; i++)
                for (int j = res.Count - 2; j > i; j--)
                {
                    if (res[j] != res[i]) continue;
                    if (!kaart.Overgangen.Any(o => o.Van == res[j] && o.Naar == res[j + 1] && o.Richting == richting && (o.AantalKeer > 0 || o.GezienBijConfiguraties.Count > 0))) continue;
                    res = res.Take(i).Concat(res.Skip(j)).ToList();
                    geknipt = true;
                    break;
                }
        }
        return res;
    }

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
            cDoor = KruisStandBijTerugweg(door, rit.Richting, start, cDoor, c);
            await ControleerGeenAlarmVoorStap($"doorrijden naar melder {start}");
            cDoor = MetAlarmStand(cDoor);
            await ZetConfiguratie(cDoor);
            var klaar = await Rit(door[0], rit.Richting, new RitDoel { Configuratie = cDoor, DoelMelder = start, VerwachtPad = door });
            if (klaar.Einde == RitEinde.Kortsluiting)
                throw new NavigatieFout($"Bij het doorrijden naar melder {start} volgde een kortsluiting: de loc staat niet op melder {start}.");
            return;
        }
        var pad = rit.Reeks.ToList();
        pad.Reverse();
        // BUG #215 (log 18-10 10:22): na een rit die op een keerlus (129-4-11-12-1-9-10-129) eindigde reed de terugweg het omgekeerde pad met één vaste wisselstand: de loc bleef 7 rondjes in de lus
        // (47 melders, 4 minuten). Komt een melder twee keer in het terugpad voor, dan wordt de lus eruit geknipt als de overgang daarna bekend is.
        var geknipt = KnipLus(_kaart, pad, rit.Richting.Om());
        if (geknipt.Count < pad.Count)
        {
            _log.Info($"Terugweg naar melder {start}: de lus in het pad ({Lijst(pad)}) wordt overgeslagen, de loc rijdt {Lijst(geknipt)} (niet getest op de baan).");
            pad = geknipt;
        }
        int vanaf = pad[0];
        if (!bezet.Contains(vanaf))
        {
            // Na het stoppen kan de loc net een melder verder staan (meegenomen in het pad).
            var extra = bezet.FirstOrDefault(m => !pad.Contains(m));
            if (extra != 0) { pad.Insert(0, extra); vanaf = extra; }
        }
        // BUG #77 (log 18:58): de terugweg 14 -> 129 -> 24 -> 21 werd met wissel 5 rechtdoor gereden terwijl 14 -> 129 alleen met wissel 5
        // afbuigend bestaat: kortsluiting. Voor de terugweg gelden de bekende wisselstanden per stap.
        var cOpdracht = c;
        c = MetEisen(pad, rit.Richting.Om(), KiesBijIngang(vanaf, rit.Richting.Om(), pad, c));
        c = KruisStandBijTerugweg(pad, rit.Richting.Om(), start, c, cOpdracht);
        await ControleerGeenAlarmVoorStap($"terugweg naar melder {start}");
        c = MetAlarmStand(c);
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

    /// <summary>BUG #160/#198: de adressen waar (nog) geen effect van gezien is. Ook in een tussenstand bijgehouden (het rapport van 17-10 20:05 meldde "0 adressen zonder effect"
    /// terwijl wissel 3, 4, 7, 8, 9 en 12 volgens de ritten niets deden); in een tussenstand kunnen er nog ongeteste adressen bij staan.</summary>
    private void BerekenAdressenZonderEffect()
    {
        var gevonden = _status.Geidentificeerd.ToHashSet();
        foreach (var k in _kaart.Kortsluitpunten.Where(k => k.OpgelostDoorAdres is not null)) gevonden.Add(k.OpgelostDoorAdres!.Value);
        foreach (var w in _kaart.Wissels.Where(w => w.Waarnemingen.Count > 0)) gevonden.Add(w.Adres);   // BUG #160: wissel 11 stond in de lijst 'zonder effect' terwijl hij waarnemingen had
        _kaart.AdressenZonderEffect = _ins.WisselAdressen().Where(a => !gevonden.Contains(a)).ToList();
    }

    private void Afronden()
    {
        _kaart.VulOntbrekendeVia();   // BUG #160
        if (Stap) BouwTrajecten();   // BUG #172
        // BUG #214: pas bij het afronden (niet tijdens de verkenning, anders verandert het wat de verkenner nog probeert)
        foreach (var t in _kaart.ToetsVoorlopigeOplossingen()) _log.Vondst("Voorlopige oplossing getoetst (BUG #204): " + t);
        foreach (var t in _kaart.HerstelStrijdigeOplossingen()) { _log.Waarschuwing("Kaart hersteld (BUG #217): " + t); _kaart.Waarschuwingen.Add(t); }
        foreach (var t in _kaart.LosKortsluitpuntenOpUitRitten()) _log.Vondst("Kortsluitpunt afgeleid (BUG #214): " + t);
        foreach (var t in _kaart.VulOntbrekendeOplossingWaarnemingen()) _log.Info("Kaartcontrole (BUG #212): " + t);
        BerekenAdressenZonderEffect();
        _kaart.VoorgesteldeBlokken = BlokVoorstel.Bereken(_kaart);
        _kaart.Wisselstraatvoorstellen = WisselstraatVoorstel.Bereken(_kaart);
        // BUG #188 (meting: 7 van 16 simulatieruns meldden 'voltooid' met 2-4 van de 21 melders, steeds met een melder zonder Dinamo-blok): een verkenning waarin nog een melder
        // zijn blok niet kent is niet af. Het rapport blijft een tussenstand en noemt de melders, zodat Marco niet denkt dat de baan klaar is.
        var zonderBlok = BlokVereist ? _kaart.Melders.Where(m => m.DinamoBlok is null).Select(m => m.Nummer).OrderBy(x => x).ToList() : new List<int>();
        _kaart.Voltooid = zonderBlok.Count == 0;
        // BUG #195: bij optie 6 is een verkenning waarin opdrachten zijn overgeslagen en geen enkele wissel is gevonden niet af (de loc kwam er niet doorheen) (simulatie: start op het zijspoor 12 meldde 'voltooid' met 5 van 21 melders).
        if (Rondrit && !_kaart.Wissels.Any(w => w.Waarnemingen.Count > 0) && _kaart.Waarschuwingen.Any(w => w.Contains("is overgeslagen")))
        {
            _kaart.Voltooid = false;
            var t2 = "De verkenning is gestopt maar nog NIET af: een of meer opdrachten zijn overgeslagen (de loc kwam er niet doorheen), dus niet alles is bereden. Zet de loc op een andere plek van de baan (bij voorkeur op een hoofdlus) en start opnieuw; de kaart blijft bewaard.";
            if (!_kaart.Waarschuwingen.Contains(t2)) { _kaart.Waarschuwingen.Add(t2); _log.Waarschuwing(t2); }
        }
        if (zonderBlok.Count > 0)
        {
            var t = $"De verkenning is gestopt maar nog NIET af: van melder {string.Join(", ", zonderBlok)} is het Dinamo-blok niet bepaald, dus dit is een tussenstand. Start opnieuw (de kaart blijft bewaard) of zet de loc op een van die melders.";
            _log.Waarschuwing(t);
            if (!_kaart.Waarschuwingen.Contains(t)) _kaart.Waarschuwingen.Add(t);
        }
        _fase = _kaart.Voltooid ? "Voltooid" : "Gestopt (tussenstand)";
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
        BerekenAdressenZonderEffect();
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
        RitEinde.StapKlaar => "stap gereden",
        _ => e.ToString()
    };
}
