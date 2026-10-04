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
            _log.Stap(hervat ? "Verkenning wordt hervat." : "Verkenning start.");
            _log.Info($"Hardware: {_hw.Naam}. Testloc adres {_ins.LocAdres} ({_ins.LocStappen} stappen), verkensnelheid {_ins.Verkensnelheid}, kruipsnelheid {_ins.Kruipsnelheid}.");
            _log.Info($"Wisseladressen {_kaart.WisselAdresVan} t/m {_kaart.WisselAdresTot}." + (BlokVereist ? $" Dinamo-blokken: {_ins.DinamoBlokken}." : ""));

            await BepaalStart(hervat);

            _fase = "Wissels in beginstand";
            MeldVoortgang();
            _log.Stap("Alle adressen in het bereik op rechtdoor zetten.");
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
            if (nieuw.Count == 0)
            {
                _log.Info("Deze rit bevat geen overgangen die niet al met alle adressen getest zijn - adrestest overgeslagen.");
                await TerugNaarHuis(o.Route);
                return;
            }

            // ---- Alle adressen één voor één ----
            var kortsluitStops = KortsluitStops(o.Configuratie, o.Richting);
            foreach (var a in _ins.WisselAdressen())
            {
                if (a < o.VolgendAdres) continue;
                o.VolgendAdres = a;
                if (_status.Geidentificeerd.Contains(a) || o.Configuratie.Bevat(a)) continue;
                await PauzeMoment();
                _bezig = $"opdracht #{o.Id}: adres {a} testen";
                MeldVoortgang();
                BewaarNu();

                await ZetWissel(a, true);
                var proef = await Rit(o.Start, o.Richting, new RitDoel
                {
                    Configuratie = o.Configuratie.Met(a),
                    Basis = basis.Reeks,
                    ExtraNaAfwijking = _ins.ExtraMeldersNaAfwijking,
                    StopBijMelders = kortsluitStops
                });
                // BUG #35 (gebruikerswaarneming: wissel 2 stond nog op afbuigend toen de loc
                // achteruit van melder 5 terug naar melder 16 moest rijden; dat veroorzaakte een
                // kortsluiting omdat de loc tegen de wisseltong aanreed die voor DIE rijrichting
                // in de verkeerde stand stond). Vroeger werd het geteste adres pas NA
                // TerugNaarMetControle (de terugrit) weer teruggezet - de terugrit werd dus
                // altijd nog met de wissel in de testafstand (afbuigend) gereden, ook als de
                // heenrit er helemaal niet echt doorheen kwam. De loc staat hier altijd stil
                // (Rit() stopt hem voor elke return), dus de wissel nu al terugzetten - VOORDAT
                // de terugrit begint - is een veilige, statische omzetting en laat de terugrit
                // over exact dezelfde (bekende, werkende) wisselstand lopen als de basisrit.
                // Blijkt de terugweg daardoor toch af te wijken (de heenrit ging écht via de
                // afbuigende tak), dan vangt TerugNaarMetControle dat al op zoals voor elke
                // andere wissel die van achteren verkeerd staat (NavigatieFout -> als
                // kortsluitpunt/"opengereden wissel" geregistreerd) - dat pad bestond al en
                // wordt hier niet aangeraakt.
                await ZetWissel(a, false);
                await TerugNaarMetControle(o, proef, o.Configuratie);

                Vergelijk(o, basis, proef, a);
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

    private void RegistreerKortsluitpunt(Opdracht o, List<int> reeks, Configuratie c, List<Etappe> route, Richting r)
    {
        int x = reeks[^1];
        var bestaand = _kaart.Kortsluitpunten.FirstOrDefault(k => k.NaMelder == x && k.Richting == r && k.Configuratie.Equals(c));
        if (bestaand is not null) { bestaand.AantalKortsluitingen++; return; }
        var k = new Kortsluitpunt
        {
            Id = _status.VolgendeKortsluitId++,
            NaMelder = x,
            Richting = r,
            Configuratie = c,
            Route = RouteMet(route, c, r, reeks),
            AantalKortsluitingen = 1
        };
        _kaart.Kortsluitpunten.Add(k);
        _log.Vondst($"Kortsluitpunt #{k.Id}: direct na melder {x} ({r.Tekst()}, {c}). Waarschijnlijk een wissel die van achteren in de verkeerde stand bereden wordt; wordt later opgelost.");
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
                else if (proef.Einde == RitEinde.Doodlopend)
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
                _log.Stap($"Kortsluitpunt #{k.Id}: proberen met adres {adres} op {(afb ? "afbuigend" : "rechtdoor")}.");
                try
                {
                    await NaarStartVan(k.Route);
                    await ZetConfiguratie(c);
                    var rit = await Rit(k.NaMelder, k.Richting, new RitDoel { Configuratie = c, Basis = new List<int> { k.NaMelder }, ExtraNaAfwijking = 0 });
                    await TerugNaar(k.NaMelder, rit, c);
                    await TerugNaarHuis(k.Route);
                    if (rit.Einde == RitEinde.Kortsluiting)
                    {
                        k.AantalKortsluitingen++;
                        _log.Info($"Adres {adres} lost kortsluitpunt #{k.Id} niet op.");
                        continue;
                    }
                    if (rit.Reeks.Count >= 2)
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
                        break;
                    }
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
    /// kant op dan hij heen kwam, dan is er heen vrijwel zeker een wissel van achteren
    /// OPENGEREDEN (ongepolariseerd puntstuk: geen kortsluiting, maar ook geen geldige
    /// rijweg). Die overgang wordt dan uit de kaart gehaald en als kortsluitpunt behandeld,
    /// zodat volgende ritten er vóór stoppen en de oplosfase de juiste wisselstand zoekt.</summary>
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
                string tekst = $"Opengereden wissel vermoed tussen melder {w} en {y} ({o.Richting.Tekst()}, {c}): heen ging de loc van {w} naar {y}, terug ging hij na {y} naar {z}. Die wissel staat van achteren tegen de rijrichting in (ongepolariseerd puntstuk: geen kortsluiting). Deze overgang is niet in de kaart opgenomen.";
                _kaart.Waarschuwingen.Add(tekst);
                _log.Waarschuwing(tekst);
                _kaart.Overgangen.RemoveAll(x => x.Van == w && x.Naar == y && x.Richting == o.Richting && x.GezienBijConfiguraties.All(k => k == c.Sleutel));
                RegistreerKortsluitpunt(o, rit.Reeks.Take(idx).ToList(), c, o.Route, o.Richting);
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
