using Modeltreinbesturing.Hardware;

namespace Baanverkenner.Kern;

/// <summary>Uitkomst van één rit.</summary>
public class RitResultaat
{
    public List<int> Reeks { get; } = new();
    public RitEinde Einde { get; set; }
    public Richting Richting { get; set; }
    /// <summary>BUG #64: de rit bleef hangen na een melder waarvan het vervolg (bij deze wisselstand) al bekend is: geen kopspoor.</summary>
    public bool BekendVervolgGemist { get; set; }
    /// <summary>BUG #285: de loc verdween van de melders na een melder zonder bekend vervolg, zonder dat Dinamo een kortsluiting meldde: waarschijnlijk het einde van het spoor
    /// (stootjuk, loc ontspoord of zonder stroom voorbij de laatste sectie), geen verkeerd bereden wissel.</summary>
    public bool EindeVanHetSpoor { get; set; }
    /// <summary>BUG #311: de andere melders die bezet waren op het moment van de kortsluiting (de loc stond er al deels in).</summary>
    public List<int> OokBezetBijKortsluiting { get; set; } = new();
}

/// <summary>Wat een rit moet doen. Zonder DoelMelder is het een verkenningsrit (rijden tot
/// er iets gebeurt); met DoelMelder een navigatierit naar een bekende melder.</summary>
public class RitDoel
{
    public Configuratie Configuratie { get; set; } = Configuratie.Basis;

    // Navigatie
    public int? DoelMelder { get; set; }
    public List<int>? VerwachtPad { get; set; }

    // Proefrit: vergelijken met de basisrit en vroeg stoppen
    public List<int>? Basis { get; set; }
    public int ExtraNaAfwijking { get; set; } = 2;

    /// <summary>Melders waarna (in deze rijrichting) een bekend kortsluitpunt volgt: daar
    /// stopt de rit al, zodat dezelfde kortsluiting niet steeds opnieuw gemaakt wordt.</summary>
    public HashSet<int> StopBijMelders { get; set; } = new();

    public bool IsNavigatie => DoelMelder is not null;

    /// <summary>BUG #107: tijdens deze rit zet de verkenner een wissel waarvan de kaart de tak al kent zelf goed zodra de loc op die tak komt
    /// en erdoor van achteren op de wissel af rijdt (anders volgt een kortsluiting die al verklaard kan worden).</summary>
    public bool StaartWissels { get; set; }

    /// <summary>BUG #107: het adres dat deze rit zelf test; dat wordt nooit automatisch gezet.</summary>
    public int? ProefAdres { get; set; }

    /// <summary>BUG #172: stap-voor-stap: de rit stopt na zoveel nieuwe melders (nooit op een kruiswissel-melder: dan rijdt hij één melder door, de uitgang).</summary>
    public int? MaxStappen { get; set; }

    /// <summary>BUG #172: de kruismotoren blijven precies zoals de rit ze kreeg (kruiswissel-proef).</summary>
    public bool GeenKruisVooraf { get; set; }
}

/// <summary>De loc kwam niet waar hij verwacht werd (bijv. een wissel die niet omging).</summary>
public class NavigatieFout : Exception
{
    public NavigatieFout(string melding) : base(melding) { }

    /// <summary>Bij een afwijking onderweg: de laatste melder die nog klopte en de melder
    /// die onverwacht bezet werd.</summary>
    public int? LaatsteGoed { get; init; }
    public int? Onverwacht { get; init; }
    /// <summary>BUG #97: de loc staat andersom (de eerste melder klopt alleen voor de tegengestelde richting).</summary>
    public bool LocAndersom { get; init; }
    /// <summary>BUG #145: de fout is een blijvende kortsluiting terwijl de loc stilstaat.</summary>
    public bool Kortsluiting { get; init; }
    /// <summary>BUG #164: richting en wisselstand van de rit waarop de afwijking optrad; daarmee kan de loc dezelfde weg terug (andere richting).</summary>
    public Richting? RitRichting { get; init; }
    public Configuratie? RitConfiguratie { get; init; }
}

/// <summary>BUG #173 (Marco 17-10): een wisselprobleem (Dinamo-uitval, loc die klem zit op een wissel) is begonnen. Alles wat daarna gezien is telt niet;
/// de loc rijdt terug naar een getest stuk baan en de opdracht begint daar opnieuw (alleen in de werkwijze 'Stap voor stap').</summary>
public class WisselProbleemFout : Exception
{
    public WisselProbleemFout(string melding) : base(melding) { }
}

public partial class BaanVerkenner
{
    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan DinamoCyclus = TimeSpan.FromMilliseconds(200);
    private const int MaxStopPassen = 4;

    private readonly Dictionary<int, bool> _wisselStand = new();
    private List<int> _laatsteKortsluitMelders = new();

    /// <summary>BUG #74: de ingangskant van de melder waar de loc nu staat: hij kwam vanaf <c>Via</c> op <c>Melder</c> aan, rijdend in
    /// <c>Richting</c>. Null = onbekend (begin, na kortsluiting, na met de hand terugzetten).</summary>
    private (int Via, int Melder, Richting Richting)? _ingang;
    private DateTime _negeerKortsluitingTot = DateTime.MinValue;
    private volatile bool _kortsluitingGemeld;
    /// <summary>BUG #57: aantal blokproeven per melder (een mislukte proef wordt nog één keer opnieuw
    /// geprobeerd, bij een volgende rit; daarna stuurt de verkenner voor die melder naar alle blokken).</summary>
    private readonly Dictionary<int, int> _proefPogingen = new();
    private const int MaxProefPogingen = 2;
    /// <summary>BUG #142: melders waar de loc na het stoppen altijd ook op een buurmelder staat (loc langer dan de melder).</summary>
    private readonly HashSet<int> _nietIsoleerbaar = new();
    private bool ProefMogelijk(int melder) => BlokVan(melder) is null && _proefPogingen.GetValueOrDefault(melder) < MaxProefPogingen && (!Rondrit || _rondritBlokfase);

    /// <summary>BUG #195: optie 6 'Rondrijden'.</summary>
    private bool Rondrit => _ins.Rondrit;
    /// <summary>BUG #195: pas als alle melders bekend zijn (tweede fase) mogen blokproeven plaatsvinden.</summary>
    private bool _rondritBlokfase;

    private bool BlokVereist => _hw.LocCommandoVereistBlok;

    /// <summary>BUG #62: de Dinamo meldde kortsluiting tijdens een blokproef (bijv. de loc staat met een poot tegen een
    /// verkeerd staande wisseltong). De proef houdt dan op; Rit() handelt het af als gewone kortsluiting.</summary>
    private bool _proefKortsluiting;
    private bool ProefKortsluitingGemeld()
    {
        if (!_kortsluitingGemeld || _klok.Nu < _negeerKortsluitingTot) return false;
        _proefKortsluiting = true;
        return true;
    }

    private int? BlokVan(int melder) => _kaart.Melders.FirstOrDefault(m => m.Nummer == melder)?.DinamoBlok;

    // BUG #43: Dinamo meldt een kortsluiting ook PER BLOK (Block Alarm, zie IBlokAlarmBron),
    // en bij ons spoor gebeurde dat zonder dat het globale F-bit ooit gezet werd - de
    // Baanverkenner zag daardoor niets en wachtte 30 s (en draaide daarna een 17 minuten
    // durende, nutteloze blokproef) terwijl de loc in de kortsluiting stond. Het event komt
    // op de seriële thread binnen, dus alles hieronder is vergrendeld.
    private readonly object _alarmSlot = new();
    private readonly HashSet<int> _alarmBlokken = new();
    private List<int> _laatsteKortsluitBlokken = new();
    private DateTime _laatsteKortsluitingOm = DateTime.MinValue;
    /// <summary>BUG #132: melder en richting van de laatste kortsluiting in een rit (het kortsluitpunt zelf wordt pas na de rit geregistreerd).</summary>
    private (int Melder, Richting Richting, Configuratie Stand)? _laatsteKortsluitContext;
    /// <summary>BUG #167b: de melders van de rit die in de laatste kortsluiting eindigde (voor de eerste stap: welke wissels worden van achteren bereden).</summary>
    private List<int> _laatsteKortsluitReeks = new();
    /// <summary>BUG #132: door de gebruiker aangewezen oplossing die op het kortsluitpunt van de lopende kortsluiting wacht (wordt in RegistreerKortsluitpunt toegepast).</summary>
    private (int Melder, Richting Richting, int Adres, bool Afbuigend, DateTime Tijd)? _wachtendeHandOplossing;

    /// <summary>BUG #141: de kortsluiting kwam terug terwijl de loc stilstond (geen rijcommando actief): de loc zit klem op de punten van een wissel
    /// en Dinamo zet de stroom steeds even terug en sluit hem weer af. Alleen het omzetten van die wissel (of de loc met de hand verplaatsen) helpt.</summary>
    private volatile bool _statischeKortsluiting;
    private readonly HashSet<int> _kruisTerugzettenGevraagd = new();
    private bool _alarmWegProbeerd;

    /// <summary>BUG #164 (hardwarelog 16-10 09:00:35): Dinamo meldde de kortsluiting op melder 12 één milliseconde NADAT de verkenner begon te stoppen. Alles wat binnen het
    /// negeervenster van een eigen stop binnenkomt gold als gevolg van die stop en werd weggegooid; het alarm was na de stop vanzelf weg, dus de kortsluiting werd nooit
    /// vastgelegd en de verkenner draaide daarna minuten blokproeven met een loc die klem zat. Per-blokalarmen zijn geen gevolg van onze stop (in een uur logboek
    /// kwam er bij 17 melders geen enkele bij een gewone stop): ze worden nu onthouden en na de rit alsnog als kortsluiting behandeld.</summary>
    private readonly List<(int Blok, DateTime Tijd)> _genegeerdeAlarmen = new();

    private void HardwareMeldtBlokAlarm(int blok, bool kortsluiting)
    {
        lock (_alarmSlot)
        {
            if (kortsluiting) _alarmBlokken.Add(blok);
            else _alarmBlokken.Remove(blok);
            if (kortsluiting && _klok.Nu < _negeerKortsluitingTot) _genegeerdeAlarmen.Add((blok, _klok.Nu));
        }
        if (kortsluiting && _voedingBlokken.Count == 0 && _laatsteKortsluitingOm != DateTime.MinValue
            && (_klok.Nu - _laatsteKortsluitingOm).TotalSeconds > 4 && (_klok.Nu - _laatsteKortsluitingOm).TotalMinutes < 5)
            _statischeKortsluiting = true;
        if (kortsluiting && _klok.Nu >= _negeerKortsluitingTot) _kortsluitingGemeld = true;
    }

    /// <summary>Marco 12:17: melden als Dinamo niet meer antwoordt (ook de keepalives blijven weg). Zonder antwoord ziet de verkenner geen
    /// melders en geen kortsluiting meer en komen stopcommando's mogelijk niet aan; het staat dan meteen in het logboek van de verkenner.</summary>
    internal volatile bool DinamoStil;
    private void HardwareMeldtStilte(bool stil, double seconden)
    {
        DinamoStil = stil;
        if (stil)
            _log.Waarschuwing($"DINAMO ANTWOORDT NIET MEER: al {seconden:0.0} s geen enkel bericht, ook geen keepalive. Melders en kortsluitmeldingen komen nu niet meer binnen en stopcommando's komen mogelijk niet aan. Controleer de kabel en herstart zo nodig Dinamo.");
        else
            _log.Info($"Dinamo antwoordt weer (na {seconden:0.0} s stilte). Controleer de loc en de wisselstanden voordat de verkenning verdergaat.");
    }

    /// <summary>BUG #125: is er in de kaart al een rit gemaakt die van <paramref name="start"/> in richting <paramref name="r"/> is vertrokken (een vervolgmelder
    /// gezien)? Dan is de blokkoppeling bewezen en wijst een loc die niet vertrekt op Dinamo/baanspanning, niet op het blok.</summary>
    /// <param name="cfg">BUG #181: bij deze wisselstand. Vertrok de loc eerder alleen bij een ANDERE stand (melder 12 vooruit met wissel 1 afbuigend), dan is een loc die nu niet vertrekt
    /// (wissel 1 rechtdoor = doodlopend) geen bewijs voor een Dinamo-storing.</param>
    internal static bool VertrekWasBewezen(Baankaart k, int start, Richting r, Configuratie? cfg = null) =>
        cfg is null ? k.Overgangen.Any(o => o.Van == start && o.Richting == r && o.AantalKeer > 0) : k.BekendeVolgende(start, r, cfg).Count > 0;

    private List<int> HuidigeAlarmBlokken()
    {
        lock (_alarmSlot) return _alarmBlokken.OrderBy(b => b).ToList();
    }

    private void HardwareMeldtKortsluiting(bool actief)
    {
        if (!actief) return;
        if (_klok.Nu < _negeerKortsluitingTot) return; // gevolg van onze eigen noodstop (F-bit)
        _kortsluitingGemeld = true;
    }

    // =====================================================================
    // Snelheid en stoppen
    // =====================================================================

    /// <summary>Stuurt een snelheid naar de loc. Bij Dinamo via de blokken van de melders
    /// waar de loc nu staat; is daarvan een blok onbekend, dan naar ALLE blokken (er staat
    /// maar één loc op de baan, dus dat kan veilig - het is alleen trager).</summary>
    /// <summary>BUG #61: de blokken die het laatste rijcommando kregen (leeg = de loc staat stil).</summary>
    private readonly HashSet<int> _voedingBlokken = new();

    /// <summary>BUG #140: met <paramref name="ookBlokVanMelder"/> krijgt ook het blok van die melder (het doel) het rijcommando,
    /// ook als de loc er nog niet op staat. Zonder dat stopt een loc die op de grens naar het volgende blok kruipt, omdat dat blok
    /// geen rijstroom heeft (log 15:01: loc bleef 1,5 minuut op een recht stuk voor melder 20 staan met alleen blok 7 onder stroom).</summary>
    private async Task StuurSnelheid(Richting r, int stap, int? ookBlokVanMelder = null)
    {
        bool vooruit = r == Richting.Vooruit;
        _voedingBlokken.Clear();
        if (!BlokVereist)
        {
            _hw.ZetLocSnelheid(_ins.LocAdres, stap, vooruit, 0, _ins.LocStappen);
            await Wacht(TimeSpan.FromMilliseconds(100));
            return;
        }
        var blokken = BlokkenVoorHuidigePlek();
        if (blokken is not null && ookBlokVanMelder is int extraMelder && BlokVan(extraMelder) is int extraBlok && !blokken.Contains(extraBlok))
            blokken.Add(extraBlok);
        blokken ??= _ins.DinamoBlokLijst();
        foreach (var b in blokken) _hw.ZetLocSnelheid(_ins.LocAdres, stap, vooruit, b, _ins.LocStappen);
        if (stap > 0) _voedingBlokken.UnionWith(blokken);
        await WachtOfAlarm(DinamoCyclus * blokken.Count, stap);
    }

    /// <summary>BUG #221 (hardwarelog 18-10 12:07:29): het rijcommando naar 9 blokken duurt 1,8 s en de verkenner wachtte die tijd helemaal af, ook als Dinamo
    /// intussen kortsluiting meldde (alarm 12:07:29.9, 'KORTSLUITING vermoed' pas 12:07:31.5, STOP voor blok 8/9 pas 12:07:32.9). Met een rijcommando (stap &gt; 0)
    /// wordt nu in stukjes van 100 ms gewacht en meteen gestopt met wachten zodra er een blokalarm is; de rit ziet het alarm dan direct.</summary>
    private async Task WachtOfAlarm(TimeSpan duur, int stap)
    {
        if (stap <= 0 || _hw is not IBlokAlarmBron) { await Wacht(duur); return; }
        var eind = _klok.Nu + duur;
        while (_klok.Nu < eind)
        {
            if (HuidigeAlarmBlokken().Count > 0) return;
            var rest = eind - _klok.Nu;
            await Wacht(rest < TimeSpan.FromMilliseconds(100) ? rest : TimeSpan.FromMilliseconds(100));
        }
    }

    /// <summary>BUG #61: staat de loc met een deel in meerdere secties en ontbreekt het rijcommando voor een
    /// (bekend) blok daarvan, dan gaat het commando ook daarheen. Op de baan (11:38, melder 132/133) vertrok
    /// de loc na een flikkering alleen met het blok van 132, terwijl hij ook in 133 stond: 30 s stilstand.
    /// Blokken die nog onbekend zijn blijven buiten schot (die loc moet zelf in de nieuwe sectie uitrollen).</summary>
    private async Task VoedOntbrekendeBlokken(IReadOnlyCollection<int> bezet, Richting r, int stap)
    {
        if (!BlokVereist || _voedingBlokken.Count == 0 || bezet.Count < 2) return;
        var ontbreekt = bezet.Select(BlokVan).Where(b => b is not null).Select(b => b!.Value).Distinct()
            .Where(b => !_voedingBlokken.Contains(b)).ToList();
        if (ontbreekt.Count == 0) return;
        _log.Info($"De loc staat op meerdere melders ({Lijst(bezet)}); rijcommando ook naar blok {string.Join(", ", ontbreekt)}.");
        foreach (var b in ontbreekt) _hw.ZetLocSnelheid(_ins.LocAdres, stap, r == Richting.Vooruit, b, _ins.LocStappen);
        _voedingBlokken.UnionWith(ontbreekt);
        await WachtOfAlarm(DinamoCyclus * ontbreekt.Count, stap);
    }

    /// <summary>Blokken van de melders waar de loc nu staat, of null als er (nog) één
    /// onbekend is of er niets bezet is.</summary>
    private List<int>? BlokkenVoorHuidigePlek()
    {
        var bezet = _monitor.Bezet;
        if (bezet.Count == 0) return null;
        var res = new List<int>();
        foreach (var m in bezet)
        {
            var b = BlokVan(m);
            if (b is null) return null;
            if (!res.Contains(b.Value)) res.Add(b.Value);
        }
        return res;
    }

    private async Task StuurNaarBlok(int blok, Richting r, int stap)
    {
        _voedingBlokken.Clear();
        if (stap > 0) _voedingBlokken.Add(blok);
        _hw.ZetLocSnelheid(_ins.LocAdres, stap, r == Richting.Vooruit, blok, _ins.LocStappen);
        await Wacht(DinamoCyclus);
    }

    /// <summary>BUG #69: rijcommando naar ALLE Dinamo-blokken tegelijk (er staat maar één loc op de baan, dus dat is veilig).
    /// Voor een loc die met een draaistel in een ander blok staat dan de melder aangeeft (log 16:22: een draaistel in blok 7,
    /// een draaistel in blok 3 op de kruiswissel): hij gaat alleen rijden als ELK blok onder hem het commando krijgt.</summary>
    private async Task StuurSnelheidAlleBlokken(Richting r, int stap)
    {
        _voedingBlokken.Clear();
        if (!BlokVereist) { await StuurSnelheid(r, stap); return; }
        var blokken = _ins.DinamoBlokLijst();
        foreach (var b in blokken) _hw.ZetLocSnelheid(_ins.LocAdres, stap, r == Richting.Vooruit, b, _ins.LocStappen);
        if (stap > 0) _voedingBlokken.UnionWith(blokken);
        await WachtOfAlarm(DinamoCyclus * blokken.Count, stap);
    }

    /// <summary>BUG #69: één korte poging met het rijcommando naar alle blokken tegelijk. True als er daarna een melder
    /// veranderde (de loc stond dus op een blokgrens en is nu gaan rijden). De loc wordt daarna volledig gestopt.</summary>
    private async Task<bool> ProbeerAlleBlokken(Richting r)
    {
        if (!BlokVereist) return false;
        _monitor.Bijwerken();
        var t0 = _klok.Nu;
        await StuurSnelheidAlleBlokken(r, _ins.Verkensnelheid);
        var eind = _klok.Nu + TimeSpan.FromSeconds(_ins.BlokproefKortSeconden);
        bool bewogen = false;
        while (_klok.Nu < eind)
        {
            await Wacht(Poll);
            if (_monitor.Bijwerken().Any(x => x.Tijd >= t0)) { bewogen = true; break; }
        }
        await VolledigeStop();
        return bewogen;
    }

    /// <summary>Loc stoppen. Waar mogelijk snel (snelheid 0 via het bekende blok); anders
    /// de volledige stop.</summary>
    private async Task StopLoc()
    {
        var oudeVoeding = _voedingBlokken.ToList();
        _voedingBlokken.Clear();
        if (!BlokVereist)
        {
            _hw.ZetLocSnelheid(_ins.LocAdres, 0, true, 0, _ins.LocStappen);
            await Wacht(TimeSpan.FromMilliseconds(1500));
            return;
        }
        var blokken = BlokkenVoorHuidigePlek();
        if (blokken is null)
        {
            await VolledigeStop();
            return;
        }
        // BUG #69: blokken die eerder een rijcommando kregen (bijv. alle blokken na een blokgrens-poging) ook op 0 zetten
        var extra = oudeVoeding.Where(b => !blokken.Contains(b)).ToList();
        foreach (var b in blokken) _hw.ZetLocSnelheid(_ins.LocAdres, 0, true, b, _ins.LocStappen);
        foreach (var b in extra) _hw.ZetLocSnelheid(_ins.LocAdres, 0, true, b, _ins.LocStappen);
        await Wacht(DinamoCyclus * (blokken.Count + extra.Count) + TimeSpan.FromMilliseconds(500));
    }

    /// <summary>Stop als niet bekend is via welk blok de loc bereikbaar is: de noodstop van
    /// de centrale vasthouden (alles staat direct stil) terwijl snelheid 0 naar ALLE blokken
    /// gaat; daarna controleren dat er echt niets meer beweegt.</summary>
    private async Task VolledigeStop()
    {
        _voedingBlokken.Clear();
        if (!BlokVereist)
        {
            _hw.Noodstop();
            _hw.ZetLocSnelheid(_ins.LocAdres, 0, true, 0, _ins.LocStappen);
            await Wacht(TimeSpan.FromMilliseconds(800));
            return;
        }
        // BUG #221: nog wachtende rijcommando's weggooien en de blokken met een alarm (en die van de bezette melders) eerst en met voorrang stoppen.
        // Daarna volgt de gewone stoppas langs alle blokken, zoals voorheen.
        _hw.VerwijderWachtendeSnelheidscommandosVoor(_ins.LocAdres);
        var eerst = StopVolgordeEerst(HuidigeAlarmBlokken().Concat((_klok.Nu - _laatsteKortsluitingOm).TotalSeconds < 5 ? _laatsteKortsluitBlokken : new List<int>()), _monitor.Bezet.Select(BlokVan).Where(b => b is not null).Select(b => b!.Value), _ins.DinamoBlokLijst());
        foreach (var b in eerst) _hw.ZetLocSnelheidUrgent(_ins.LocAdres, 0, true, b, _ins.LocStappen);
        var blokken = StopVolgorde(eerst, _ins.DinamoBlokLijst());
        var duur = DinamoCyclus * blokken.Count + TimeSpan.FromMilliseconds(400);
        // BUG #86 (Marco 13:02: "de loc stopte niet, bleef gewoon doorrijden na de stopmelding"): tijdens een kortsluiting heeft het getroffen blok
        // geen rijstroom, dus de stopcommando's die dan naar dat blok gaan komen bij de loc niet aan; zodra de kortsluiting weg is rijdt hij
        // met de oude snelheid door (hardwarelog 12:26:44: alarm blok 2 tot 45.48 en blok 3 tot 45.68, de stopframes voor blok 2 en 3 gingen
        // om 44.88 en 45.08). Daarom: was er tijdens de stoppas een blokalarm, wacht dan tot het weg is en stuur de hele stoppas opnieuw.
        for (int pas = 1; pas <= MaxStopPassen; pas++)
        {
            bool alarmTijdensPas = HuidigeAlarmBlokken().Count > 0 || (pas == 1 && (_klok.Nu - _laatsteKortsluitingOm).TotalSeconds < 3);
            _negeerKortsluitingTot = _klok.Nu + duur + TimeSpan.FromSeconds(3);
            _hw.Noodstop();
            foreach (var b in blokken) _hw.ZetLocSnelheid(_ins.LocAdres, 0, true, b, _ins.LocStappen);
            var eind = _klok.Nu + duur;
            while (_klok.Nu < eind)
            {
                await Wacht(TimeSpan.FromMilliseconds(400));
                _hw.Noodstop(); // F-bit vasthouden tot alle stopcommando's verstuurd zijn
                if (HuidigeAlarmBlokken().Count > 0) alarmTijdensPas = true;
            }
            if (!alarmTijdensPas || pas == MaxStopPassen) break;
            var wachtTot = _klok.Nu + TimeSpan.FromSeconds(6);
            while (HuidigeAlarmBlokken().Count > 0 && _klok.Nu < wachtTot) await Wacht(TimeSpan.FromMilliseconds(200));
            _log.Waarschuwing($"Dinamo had tijdens de stop een kortsluitmelding (blok {string.Join(" en ", HuidigeAlarmBlokken().Count > 0 ? HuidigeAlarmBlokken() : new List<int>())}{(HuidigeAlarmBlokken().Count > 0 ? "" : "nu weg")}): de stopcommando's zijn dan niet aangekomen. Stop wordt opnieuw gestuurd (poging {pas + 1} van {MaxStopPassen}).");
            await Wacht(TimeSpan.FromMilliseconds(500)); // rijstroom weer op de blokken
        }
        await Wacht(TimeSpan.FromMilliseconds(1500)); // F-bit loslaten + uitrollen
        _monitor.Bijwerken();

        // Controle: beweegt er nog iets? Dan is een stopcommando niet aangekomen.
        await Wacht(TimeSpan.FromMilliseconds(1500));
        if (_monitor.Bijwerken().Count > 0)
        {
            _log.Waarschuwing("Na de volledige stop veranderden er nog melders - stop wordt herhaald.");
            _hw.Noodstop();
            foreach (var b in blokken) _hw.ZetLocSnelheid(_ins.LocAdres, 0, true, b, _ins.LocStappen);
            await Wacht(DinamoCyclus * blokken.Count + TimeSpan.FromMilliseconds(1500));
            _monitor.Bijwerken();
        }
    }

    /// <summary>BUG #221: de blokken die bij een stop voorrang krijgen: eerst de alarmblokken, dan de blokken van de bezette melders (hoogstens 4, alleen blokken uit de lijst).</summary>
    internal static List<int> StopVolgordeEerst(IEnumerable<int> alarmBlokken, IEnumerable<int> blokkenBezet, IReadOnlyCollection<int> alle)
        => alarmBlokken.Concat(blokkenBezet).Where(alle.Contains).Distinct().Take(4).ToList();

    /// <summary>BUG #221: alle blokken, met <paramref name="eerst"/> vooraan (de rest in de gewone volgorde).</summary>
    internal static List<int> StopVolgorde(IReadOnlyList<int> eerst, IEnumerable<int> alle)
        => eerst.Concat(alle.Where(b => !eerst.Contains(b))).ToList();

    // =====================================================================
    // Wissels
    // =====================================================================

    private int _wisselCommandos;

    /// <summary>BUG #80: het laatst gestuurde wisselcommando, met de tijd en de toen bezette melders (voor de oorzaak van een ontsporing).</summary>
    private (int Adres, bool Afbuigend, DateTime Tijd, List<int> Bezet)? _laatsteWisselCommando;

    /// <summary>BUG #264 (autolog 21-10 08:46:18): de eerste rit over de binnenring kwam van 4 op 129 aan; pas toen zag de staartwisselregel dat 129 -> 14 wissel 5 afbuigend vraagt. Wissel 6
    /// (overloopset) ging mee en ligt bij 129, onder de loc: kortsluiting. Geeft de volger van <paramref name="meester"/> als die volgens de kaart bij een bezette melder ligt: de afbuigende tak
    /// van de meester (daar sluit de overloop aan) of een melder waar de kaart de volger kent. Anders null.</summary>
    private int? VolgerOnderLoc(int meester)
    {
        var paar = _ins.GekoppeldeParen().FirstOrDefault(p => p.Meester == meester);
        if (paar.Volger == 0) return null;
        _monitor.Bijwerken();
        var bezet = _monitor.Bezet;
        if (bezet.Count == 0) return null;
        bool tak = _kaart.Wissels.Where(w => w.Adres == meester).SelectMany(w => w.Waarnemingen)
                        .Any(o => o.Soort == WaarnemingSoort.Splitsing && o.VolgendeBijAfbuigend is int af && bezet.Contains(af));
        return tak || WisselsBijBezet(_kaart, bezet).Contains(paar.Volger) ? paar.Volger : null;
    }

    /// <summary>BUG #259: is dit adres de tweede wissel van een overloopset (schakelt alleen mee met zijn meester)?</summary>
    private bool IsVolger(int adres) => _ins.GekoppeldeParen().Any(p => p.Volger == adres);
    private bool _volgerBezig;

    /// <summary>BUG #259 (Marco 20-10 18:23-18:33): wissel 5 en 6 zijn een overloopset en staan op de baan altijd hetzelfde; wissel 6 is omgekeerd aangesloten. Een volger wordt nooit los
    /// gezet; zet de verkenner de meester, dan gaat de volger mee naar dezelfde stand op de baan (Dinamo-commando omgedraaid als één van beide omgekeerd is aangesloten).</summary>
    private async Task ZetWissel(int adres, bool afbuigend, bool forceer = false)
    {
        var paren = _ins.GekoppeldeParen();
        if (!_volgerBezig && paren.Any(p => p.Volger == adres)) return;
        if (!_volgerBezig) _uitgesteldeWissels.Remove(adres);   // BUG #271: een bewuste stand vervangt een uitgestelde
        await ZetWisselKern(adres, afbuigend, forceer);
        if (_volgerBezig) return;
        var omg = _ins.OmgekeerdeWissels();
        foreach (var p in paren.Where(p => p.Meester == adres))
        {
            bool baanAfb = afbuigend ^ omg.Contains(adres);
            bool dv = baanAfb ^ omg.Contains(p.Volger);
            if (!forceer && _wisselStand.TryGetValue(p.Volger, out var st) && st == dv) continue;
            _log.Info($"Wissel {p.Volger} schakelt mee met wissel {adres} (overloopset): op de baan beide {(baanAfb ? "afbuigend" : "rechtdoor")}, Dinamo-commando voor wissel {p.Volger}: {(dv ? "afbuigend" : "rechtdoor")} (BUG #259).");
            _volgerBezig = true;
            try { await ZetWisselKern(p.Volger, dv, forceer: true); }
            finally { _volgerBezig = false; }
        }
    }

    private async Task ZetWisselKern(int adres, bool afbuigend, bool forceer)
    {
        if (!forceer && _wisselStand.TryGetValue(adres, out var huidig) && huidig == afbuigend) return;
        if (!forceer) await VrijRijdenVoorWissel(adres);   // BUG #181
        long bevestigingenVoor = (_hw as IWisselBevestiging)?.AantalWisselBevestigingen ?? 0;
        long eigenVoor = (_hw as IWisselBevestiging)?.BevestigingenVan(adres, afbuigend) ?? -1;   // BUG #307
        bool alarmBijCommando = HuidigeAlarmBlokken().Count > 0;   // BUG #308
        _hw.ZetWissel(adres, afbuigend);
        // BUG #308 (autolog 25-10 10:35:29.727-10:35:30.328): de kortsluiting in blok 3 ging om 10:35:30.127 weg, 0,2 s VOORDAT Dinamo wissel 2 terugmeldde; toch heette het
        // "de kortsluiting ging weg nadat wissel 2 naar afbuigend is geschakeld" (de loc stond op 14, ver van wissel 2). Gaat een alarm weg voordat de eigen terugmelding er is,
        // dan heeft deze wissel het niet weggenomen. Meteen na het commando kijken (vóór de wachttijd), anders is de volgorde niet meer te zien.
        _alarmWegVoorEigenTerugmelding = false;
        if (eigenVoor >= 0 && alarmBijCommando && _hw is IWisselBevestiging bron308)
            for (int i = 0; i < 15 && bron308.BevestigingenVan(adres, afbuigend) <= eigenVoor; i++)
            {
                if (HuidigeAlarmBlokken().Count == 0) { _alarmWegVoorEigenTerugmelding = true; break; }
                await Wacht(TimeSpan.FromMilliseconds(100));
            }
        // BUG #181: tijdens het NAVIGEREN (de loc rijdt naar een andere plek) mag geen wissel omgaan die de kaart bij een nu bezette melder noemt: de loc staat er met een deel
        // op of tegen de tong (de gevaarlijkste overschrijving van een stand). Verkenningsritten zetten de wissel vóór de loc bewust om en tellen niet mee.
        if (_stapNavigeert && _wisselStand.TryGetValue(adres, out var vorige) && vorige != afbuigend && _monitor.Bezet.Count >= 2 && WisselsBijBezet(_kaart, _monitor.Bezet).Contains(adres))
        {
            string bij = string.Join("/", _monitor.Bezet.OrderBy(x => x));
            Kennis.Zaai("wissel-onder-loc", $"{adres}@{bij}", vorige ? "afbuigend" : "rechtdoor");
            Kennis.Leg("wissel-onder-loc", $"{adres}@{bij}", afbuigend ? "afbuigend" : "rechtdoor", $"navigatie zet wissel {adres} om terwijl melder {bij} bezet zijn (de loc staat dus op een grens)");
        }
        _wisselStand[adres] = afbuigend;
        if (!_volgerBezig) _laatsteWisselCommando = (adres, afbuigend, _klok.Nu, _monitor.Bezet.OrderBy(x => x).ToList());   // BUG #259: de meester telt, niet de volger
        // BUG #65: meten is weten - hoeveel wisselcommando's zijn er sinds het verbinden verstuurd? Valt de Dinamo ooit
        // weer uit (wissels bewegen niet meer), dan is in het log te zien na hoeveel commando's dat gebeurde.
        _wisselCommandos++;
        _log.Rijden($"Adres {adres}: {(afbuigend ? "afbuigend" : "rechtdoor")} (wisselcommando {_wisselCommandos} sinds het verbinden)");
        await Wacht(TimeSpan.FromMilliseconds(Math.Max(100, _ins.WisselPauzeMs)));
        // Dinamo: pas verder als het commando echt de deur uit is
        for (int i = 0; i < 40 && !_hw.KlaarVoorVolgendeWisselCommando; i++) await Wacht(DinamoCyclus);
        // BUG #93 (log 15:20:28): wissel 5 werd omgezet terwijl de loc op melder 14 stond (de wissel ligt vlak erachter) en de loc reed 0,6 s later weg;
        // 3,6 s later kortsluiting, en dezelfde rit zonder nieuw commando lukte wel. Ligt de wissel achter een melder waarop de loc nu staat, dan
        // krijgt de tong extra tijd voordat de loc weer wegrijdt.
        // BUG #126: eerst op de terugmelding wachten. Meldt Dinamo niets terug, dan beweegt er niets en zijn de extra wachttijden hieronder zinloos (kostte 3,6 s per commando).
        if (eigenVoor >= 0) _laatsteCommandoPerWissel[adres] = (afbuigend, eigenVoor);   // BUG #307
        bool? terugmelding = await WachtOpBevestiging(bevestigingenVoor);
        // BUG #223 (log 18-10 12:03:22-12:03:26): tijdens een kortsluiting kwam de terugmelding van wissel 2 pas na 3,6 s. Na 1 s telde dat als 'geen terugmelding' en daardoor
        // stonden rit #3 en #4 als ONBETROUWBAAR (Dinamo-uitval) in het rapport. Met een (recent) blokalarm langer wachten; komt er dan nog niets, dan telt het niet als uitval.
        if (terugmelding == false && AlarmRondWisselCommando())
        {
            if (_hw is IWisselBevestiging bron2)
                for (int i = 0; i < 40 && bron2.AantalWisselBevestigingen <= bevestigingenVoor; i++) await Wacht(TimeSpan.FromMilliseconds(100));
            if ((_hw as IWisselBevestiging)?.AantalWisselBevestigingen > bevestigingenVoor) terugmelding = true;
            else
            {
                _log.Info($"Wissel {adres}: geen terugmelding, maar Dinamo meldt op dit moment (of net) kortsluiting; dat telt niet als Dinamo-uitval.");
                terugmelding = null;
                _zonderTerugmeldingBijAlarm++;   // BUG #301
            }
        }
        var bezetNu = _monitor.Bezet;
        if (terugmelding == false) { /* geen extra wachttijd: de tong beweegt niet */ }
        else if (bezetNu.Count > 0 && _kaart.Wissels.Any(w => w.Adres == adres && w.Waarnemingen.Any(o => bezetNu.Contains(o.NaMelder))))
            await Wacht(TimeSpan.FromMilliseconds(ExtraWisselWachtMs));
        // BUG #123 (log 14-10 08:52:55 en 08:53:15): wissel 14 staat in de kaart bij melder 8, maar twee keer kwam er kortsluiting 2 s na het omzetten
        // van 14 terwijl de loc op melder 13 stond en 0,5-1,5 s later wegreed. De kaart kent niet altijd welke wissel bij welke melder ligt; staat de loc
        // ergens op een melder, dan krijgt de tong daarom altijd een korte extra wachttijd, ook als de kaart de wissel niet bij die melder noemt.
        else if (bezetNu.Count > 0)
            await Wacht(TimeSpan.FromMilliseconds(ExtraWisselWachtOnbekendMs));
        if (terugmelding == true) _zonderTerugmeldingBijAlarm = 0;   // BUG #301
        if (terugmelding is bool tm) await VerwerkBevestiging(adres, afbuigend, tm);
    }

    /// <summary>BUG #223: is er nu een blokalarm, of was er in de laatste 10 s een kortsluiting? Dan kan een terugmelding van Dinamo uitblijven of laat komen.</summary>
    /// <summary>BUG #273 (autolog 21-10 12:39:32-12:40:10): tijdens het zoeken naar een wissel die een blijvende kortsluiting opheft (de loc staat stil, Dinamo zet de stroom steeds even terug)
    /// bleven terugmeldingen van wissel 6, 2 en 1 weg; dat telde als Dinamo-uitval. De 'levensteken'-routine schakelde 25 s willekeurige wissels en stuurde daarna alle wissels opnieuw;
    /// het alarm was daarna weg en de verkenner schreef dat toe aan wissel 2 (Marco 12:41: "de loc stond meer dan 1 meter van wissel 2 af"). Zolang dat zoeken loopt, telt een
    /// ontbrekende terugmelding niet als uitval.</summary>
    private int _alarmZoekenBezig;
    private bool AlarmRondWisselCommando() => _alarmZoekenBezig > 0 || HuidigeAlarmBlokken().Count > 0 || (_klok.Nu - _laatsteKortsluitingOm).TotalSeconds < 10;

    /// <summary>BUG #157: JSON van de kaart vóór de eerste ontbrekende terugmelding (null zolang de terugmeldingen binnenkomen).</summary>
    private string? _kaartVoorUitval;
    /// <summary>BUG #173: opdracht-nummer en aantal geteste overgangen op het moment van de eerste ontbrekende terugmelding (begin van het wisselprobleem).</summary>
    private int _uitvalIdGrens = int.MaxValue, _uitvalGetest = -1;
    private DateTime _uitvalHersteldOm = DateTime.MinValue;
    /// <summary>BUG #157: de gebruiker koos "Nee" bij de uitvalmelding: alles daarna is onbetrouwbaar.</summary>
    private bool _onbetrouwbaar;

    /// <summary>BUG #157 (Marco 15-10 09:28): na de uitval van Dinamo leidde de blinde verkenning (wissel 5 en 2 werden niet uitgevoerd) tot een valse
    /// kortsluiting-oplossing en een valse 'onverwachte terugweg'. Bevindingen sinds de eerste ontbrekende terugmelding worden uit de kaart gehaald.</summary>
    private void TerugNaarVoorUitval(string reden)
    {
        _onbetrouwbaar = false;
        if (_kaartVoorUitval is null) return;
        try
        {
            var oud = Baankaart.VanJson(_kaartVoorUitval);
            _kaartVoorUitval = null;
            if (oud is null) return;
            var waarsch = _kaart.Waarschuwingen.ToList();
            // BUG #206: melders (en hun Dinamo-blok) volgen uit de bezetmeldingen, niet uit wisselcommando's; die blijven behouden zodat ze niet opnieuw ontdekt hoeven te worden.
            var meldersNu = _kaart.Melders.Select(m => (m.Nummer, m.DinamoBlok)).ToList();
            foreach (var p in typeof(Baankaart).GetProperties().Where(p => p.CanRead && p.CanWrite))
                p.SetValue(_kaart, p.GetValue(oud));
            _kaart.Waarschuwingen = waarsch;
            int behouden = 0;
            foreach (var (nr, blok) in meldersNu)
            {
                var m = _kaart.Melders.FirstOrDefault(x => x.Nummer == nr);
                if (m is null) { _kaart.Melders.Add(new MelderInfo { Nummer = nr, DinamoBlok = blok }); behouden++; }
                else if (m.DinamoBlok is null && blok is not null) { m.DinamoBlok = blok; behouden++; }
            }
            if (behouden > 0) _log.Info($"Na de Dinamo-uitval zijn {behouden} melder(s)/blokgegevens behouden: die komen uit de bezetmeldingen en niet uit wisselcommando's (BUG #206).");
            var tekst = $"Dinamo-uitval ({reden}): alle bevindingen sinds de eerste ontbrekende terugmelding zijn uit de kaart gehaald omdat ze niet betrouwbaar zijn.";
            _kaart.Waarschuwingen.Add(tekst);
            _log.Waarschuwing(tekst);
        }
        catch (Exception ex) { _log.Waarschuwing($"Kaart terugzetten na Dinamo-uitval mislukte: {ex.Message}"); }
    }

    /// <summary>BUG #157 (Marco 15-10 09:29): na het alarm eerst de loc stilzetten en dan een willekeurige (veilige) wissel blijven omschakelen tot Dinamo weer
    /// terugmeldt (2 terugmeldingen) of tot de tijd om is. Geeft true als Dinamo weer tot leven kwam.</summary>
    private async Task<bool> WachtOpLevenstekenDinamo(IWisselBevestiging bron)
    {
        try { _hw.Noodstop(); StopZonderWachten(); } catch { /* stilzetten is best effort */ }
        var rnd = new Random();
        long begin = bron.AantalWisselBevestigingen;
        var start = _klok.Nu;
        _log.Info("Dinamo reageert niet op wissel-commando's: de loc staat stil en een willekeurige wissel wordt steeds omgeschakeld om te zien of Dinamo weer tot leven komt (maximaal 2 minuten).");
        while (_klok.Nu - start < TimeSpan.FromSeconds(120))
        {
            var onder = WisselsOnderLoc();
            // BUG #294 (autolog 21-10 17:11:13-17:14:08): de levensteken-routine schakelde wissel 14 om met de loc op 24/8 (2,1 s later kortsluiting) en kon een volger los omzetten
            // (overloopset 5+6 uit elkaar). Nu nooit een wissel bij de loc of een overlooppartner daarvan, nooit een volger, en bij voorkeur een adres zonder gevonden effect.
            foreach (var a in WisselsBijBezet(_kaart, _monitor.Bezet)) onder.TryAdd(a, 0);
            foreach (var pa in _kaart.OverloopPartnersVan(onder.Keys).ToList()) onder.TryAdd(pa, 0);
            var kand = _ins.WisselAdressen().Where(a => !Kruis.IsMotor(a) && !onder.ContainsKey(a) && !IsVolger(a) && !_ins.GekoppeldeParen().Any(p => p.Meester == a && onder.ContainsKey(p.Volger))).ToList();
            var zonder = kand.Where(a => _kaart.AdressenZonderEffect.Contains(a)).ToList();
            if (zonder.Count > 0) kand = zonder;
            if (kand.Count == 0) return false;
            int adres = kand[rnd.Next(kand.Count)];
            bool stand = _wisselStand.TryGetValue(adres, out var h) && h;
            foreach (var wens in new[] { !stand, stand })
            {
                _hw.ZetWissel(adres, wens);
                await Wacht(TimeSpan.FromMilliseconds(Math.Max(100, _ins.WisselPauzeMs)));
                for (int i = 0; i < 40 && !_hw.KlaarVoorVolgendeWisselCommando; i++) await Wacht(DinamoCyclus);
                await Wacht(TimeSpan.FromMilliseconds(1500));
            }
            _wisselStand[adres] = stand;
            if (bron.AantalWisselBevestigingen - begin >= 2)
            {
                _log.Info($"Dinamo meldt wissel-commando's weer terug (na {(_klok.Nu - start).TotalSeconds:0} s, wissel {adres}). Dinamo is vanzelf hersteld.");
                _echo.Herstel();
                return true;
            }
        }
        _log.Waarschuwing("Dinamo kwam binnen 2 minuten niet tot leven (geen terugmeldingen op het omschakelen).");
        return false;
    }

    /// <summary>BUG #126: bewaakt of Dinamo de wissel-commando's terugmeldt (zie <see cref="EchoWaakhond"/>).</summary>
    private readonly EchoWaakhond _echo = new();

    /// <summary>BUG #126 (logs 14-10 0904, 0943 en 1022): Dinamo hield op met het uitvoeren van wissel-commando's (0904 vanaf 08:47:23, 1022 vanaf 10:19:38, 0943 de
    /// hele sessie) terwijl de interface keepalives bleef sturen. De wissels bewogen niet meer, de verkenner dacht van wel en 'ontdekte' daardoor wissels die er niet
    /// bij horen en kortsluitingen. Een wissel-commando dat Dinamo uitvoert, wordt in een statusbericht teruggemeld; blijft dat meerdere commando's achter elkaar weg,
    /// dan stopt de verkenner en vraagt de gebruiker Dinamo te controleren.</summary>
    private async Task<bool?> WachtOpBevestiging(long bevestigingenVoor)
    {
        if (_hw is not IWisselBevestiging bron) return null;
        for (int i = 0; i < 10 && bron.AantalWisselBevestigingen <= bevestigingenVoor; i++) await Wacht(TimeSpan.FromMilliseconds(100));
        return bron.AantalWisselBevestigingen > bevestigingenVoor;
    }

    /// <summary>BUG #308: bij het laatste wisselcommando verdween het alarm al voordat Dinamo deze wissel terugmeldde.</summary>
    private bool _alarmWegVoorEigenTerugmelding;

    /// <summary>BUG #307: per wissel het laatste commando (Dinamo-stand) en het aantal eigen terugmeldingen daarvoor.</summary>
    private readonly Dictionary<int, (bool Afbuigend, long EigenVoor)> _laatsteCommandoPerWissel = new();

    /// <summary>BUG #307: heeft Dinamo het laatste commando voor deze wissel (ooit, ook laat) teruggemeld? null = niet bekend. De uitvalbewaking (#126) blijft op het totaal
    /// werken; dit is alleen om een vondst niet toe te schrijven aan een wissel die niet is omgezet.</summary>
    private bool? LaatsteCommandoTeruggemeld(int adres)
    {
        if (_hw is not IWisselBevestiging bron || !_laatsteCommandoPerWissel.TryGetValue(adres, out var lc)) return null;
        return bron.BevestigingenVan(adres, lc.Afbuigend) > lc.EigenVoor;
    }

    private async Task VerwerkBevestiging(int adres, bool afbuigend, bool bevestigd)
    {
        if (_hw is not IWisselBevestiging bron) return;
        if (bevestigd) { _echo.Noteer(true); _kaartVoorUitval = null; return; }
        // BUG #157: de kaart zoals die was vóór de eerste ontbrekende terugmelding, om bij een uitval alle latere (onbetrouwbare) bevindingen weg te kunnen halen.
        if (_echo.Reeks == 0 && _kaartVoorUitval is null)
        {
            _kaartVoorUitval = _kaart.AlsJson();
            _uitvalIdGrens = _status.VolgendeOpdrachtId;     // BUG #173: opdrachten die hierna gepland worden zijn onbetrouwbaar
            _uitvalGetest = _status.GetesteOvergangen.Count;
        }
        _log.Rijden($"Wissel {adres}: geen terugmelding van Dinamo op het commando ({_echo.Reeks + 1} achter elkaar zonder).");
        if (!_echo.Noteer(false)) return;
        var tekst = _echo.OoitBevestigd
            ? $"Dinamo meldt de laatste {_echo.Reeks} wissel-commando's niet meer terug (laatste: wissel {adres} → {(afbuigend ? "afbuigend" : "rechtdoor")}), terwijl dat eerder in deze sessie wel gebeurde. De wissels worden dan niet meer omgezet."
            : $"Dinamo heeft in deze sessie nog geen enkel wissel-commando teruggemeld ({_echo.Reeks} commando's). Waarschijnlijk worden de wissels niet omgezet.";
        _log.Waarschuwing("DINAMO VOERT WISSELCOMMANDO'S NIET UIT: " + tekst);
        _kaart.Waarschuwingen.Add(tekst);
        try { bron.LogWisselUitval($"laatste commando: wissel {adres} {(afbuigend ? "afbuigend" : "rechtdoor")}"); } catch { /* diagnose mag de verkenning niet storen */ }
        if (VraagJaNee is null) { TerugNaarVoorUitval("zonder gebruiker om te vragen"); throw new OperationCanceledException(tekst); }
        // BUG #157 (Marco 15-10 09:29): eerst de loc stilzetten en een willekeurige wissel blijven omschakelen: komt Dinamo vanzelf weer tot leven,
        // dan hoeft de gebruiker niets te doen.
        bool vanzelfHersteld = await WachtOpLevenstekenDinamo(bron);
        bool? antwoord = vanzelfHersteld ? true : await VraagJaNee("Dinamo voert wissels niet uit",
            tekst + "\n\nWat u op de baan hoort en ziet is leidend: gaan er wissels om? Zet Dinamo en de baanspanning uit en weer aan (of controleer de kabel).\n\nJa = Dinamo is hersteld, alle wissels opnieuw sturen en doorgaan (bevindingen sinds de uitval worden weggehaald)\nNee = de wissels gaan wel om: doorgaan, maar alles vanaf nu staat in het rapport als ONBETROUWBAAR en de verkenning stopt bij 12 commando's achter elkaar zonder terugmelding\n(venster sluiten = verkenning stoppen, bevindingen sinds de uitval worden weggehaald)");
        if (antwoord is null) { TerugNaarVoorUitval("verkenning gestopt"); throw new OperationCanceledException("Gebruiker stopte: Dinamo voert wissel-commando's niet uit."); }
        if (antwoord == false)
        {
            if (_echo.Negeren)
            {
                TerugNaarVoorUitval("12 commando's zonder terugmelding");
                throw new OperationCanceledException("Dinamo meldt 12 wissel-commando's achter elkaar niet terug: verkenning gestopt. Zet Dinamo uit en aan en hervat.");
            }
            _echo.Negeren = true;
            _onbetrouwbaar = true;
            var wees = $"Vanaf {_klok.Nu:HH:mm:ss} meldt Dinamo wissel-commando's niet terug; de gebruiker zegt dat de wissels wel omgaan. Alles wat vanaf nu gevonden wordt is ONBETROUWBAAR (sommige commando's worden niet uitgevoerd) en wordt niet in het leerprofiel bewaard.";
            _log.Waarschuwing(wees);
            _kaart.Waarschuwingen.Add(wees);
            return;
        }
        TerugNaarVoorUitval(vanzelfHersteld ? "Dinamo reageerde vanzelf weer" : "Dinamo hersteld");
        if (!vanzelfHersteld) await HoortestNaHerstel();
        _echo.Herstel();
        // BUG #309 (autolog 25-10 10:17:38 en 10:53:06): na "Dinamo is vanzelf hersteld" stond er "hersteld volgens de gebruiker", terwijl de gebruiker niets gedaan had
        _log.Info($"Dinamo hersteld{(vanzelfHersteld ? " (vanzelf)" : " volgens de gebruiker")}: alle wissels worden opnieuw gestuurd. Bevindingen sinds de eerste ontbrekende terugmelding zijn niet betrouwbaar.");
        // BUG #173 (Marco 17-10: "de loc stond stil op wissel 5, toen de problemen met de wissels begonnen"): een wissel waar de loc op of tegenaan staat wordt
        // niet opnieuw gestuurd (ook niet in dezelfde stand), en de kruismotoren niet als de loc op de kruiswissel staat. Hij blijft zoals hij stond.
        var onderLoc = WisselsOnderLoc();
        bool opKruis = Kruis.OpKruis(_monitor.Bezet);
        foreach (var kv in _wisselStand.ToList())
        {
            if (onderLoc.TryGetValue(kv.Key, out var onderMelder) || (opKruis && Kruis.IsMotor(kv.Key)))
            {
                _log.Info($"Wissel {kv.Key} wordt na het herstel niet opnieuw gestuurd: de loc staat op of tegen die wissel{(onderLoc.ContainsKey(kv.Key) ? $" (melder {onderMelder})" : " (kruiswissel)")}.");
                continue;
            }
            long voor = bron.AantalWisselBevestigingen;
            _hw.ZetWissel(kv.Key, kv.Value);
            _laatsteWisselCommando = (kv.Key, kv.Value, _klok.Nu, _monitor.Bezet.OrderBy(x => x).ToList());
            await Wacht(TimeSpan.FromMilliseconds(Math.Max(100, _ins.WisselPauzeMs)));
            for (int i = 0; i < 40 && !_hw.KlaarVoorVolgendeWisselCommando; i++) await Wacht(DinamoCyclus);
            for (int i = 0; i < 10 && bron.AantalWisselBevestigingen <= voor; i++) await Wacht(TimeSpan.FromMilliseconds(100));
            _echo.Noteer(bron.AantalWisselBevestigingen > voor);
        }
        if (_echo.Reeks >= EchoWaakhond.Drempel(_echo.OoitBevestigd))
        {
            _log.Waarschuwing("Dinamo meldt na het herstel nog steeds niets terug: de verkenning stopt.");
            throw new OperationCanceledException("Dinamo meldt na het herstel nog steeds geen wissel-commando's terug.");
        }
        _uitvalHersteldOm = _klok.Nu;
        // BUG #173: stap voor stap negeert alles sinds het begin van het probleem en begint de opdracht opnieuw vanaf een getest stuk baan.
        if (Stap) throw new WisselProbleemFout("Dinamo had een uitval en is hersteld");
    }

    /// <summary>BUG #131 (Marco 14-10 13:25): zegt de gebruiker 'Dinamo is hersteld', dan wordt dat eerst getest: een willekeurige wissel gaat heen en weer en de
    /// gebruiker hoort of dat gebeurt. Hoort hij niets, dan moet Dinamo opnieuw worden herstart; dat herhaalt zich tot het klikt (of de gebruiker stopt).
    /// Het beeld van de wisselstanden blijft ongewijzigd (de wissel gaat terug naar zijn stand). Geeft false als de gebruiker toch zonder test doorgaat.</summary>
    private async Task<bool> HoortestNaHerstel()
    {
        if (VraagJaNee is null) return true;
        var rnd = new Random();
        for (int poging = 1; ; poging++)
        {
            var onder = WisselsOnderLoc();
            var kand = _ins.WisselAdressen().Where(a => !Kruis.IsMotor(a) && !onder.ContainsKey(a)).ToList();
            if (kand.Count == 0) return true;   // geen veilige wissel om te testen
            int adres = kand[rnd.Next(kand.Count)];
            bool stand = _wisselStand.TryGetValue(adres, out var h) && h;
            long voor = (_hw as IWisselBevestiging)?.AantalWisselBevestigingen ?? 0;
            _log.Info($"Hoortest (poging {poging}): wissel {adres} gaat heen en weer ({(stand ? "afbuigend" : "rechtdoor")} → {(!stand ? "afbuigend" : "rechtdoor")} → {(stand ? "afbuigend" : "rechtdoor")}).");
            foreach (var wens in new[] { !stand, stand })
            {
                _hw.ZetWissel(adres, wens);
                await Wacht(TimeSpan.FromMilliseconds(Math.Max(100, _ins.WisselPauzeMs)));
                for (int i = 0; i < 40 && !_hw.KlaarVoorVolgendeWisselCommando; i++) await Wacht(DinamoCyclus);
                await Wacht(TimeSpan.FromMilliseconds(1500));
            }
            _wisselStand[adres] = stand;
            long na = (_hw as IWisselBevestiging)?.AantalWisselBevestigingen ?? 0;
            if (_hw is IWisselBevestiging) _log.Info($"Hoortest: Dinamo meldde {na - voor} keer een uitgevoerd commando terug.");
            var gehoord = await VraagJaNee("Dinamo hersteld? Hoortest",
                $"Wissel {adres} is zojuist twee keer omgezet (heen en weer). Heeft u de wissel horen klikken en zien bewegen?\n\nJa = ja, gehoord: Dinamo werkt weer\nNee = niets gehoord: Dinamo moet nogmaals worden herstart\n(venster sluiten = verkenning stoppen)");
            if (gehoord is null) throw new OperationCanceledException("Gebruiker stopte: hoortest na het herstel van Dinamo.");
            if (gehoord == true) { _log.Info($"Hoortest geslaagd: de gebruiker hoorde wissel {adres} omgaan."); return true; }
            _log.Waarschuwing($"Hoortest mislukt: de gebruiker hoorde wissel {adres} niet omgaan. Dinamo moet opnieuw worden herstart.");
            var opnieuw = await VraagJaNee("Dinamo opnieuw herstarten",
                "Dinamo reageert nog steeds niet. Zet Dinamo en de baanspanning uit, wacht een paar seconden, zet ze weer aan en controleer de kabel.\n\nJa = opnieuw herstart, de hoortest wordt herhaald\nNee = toch doorgaan zonder werkende Dinamo (niet aanbevolen)\n(venster sluiten = verkenning stoppen)");
            if (opnieuw is null) throw new OperationCanceledException("Gebruiker stopte: Dinamo reageert na herstart nog niet.");
            if (opnieuw == false) { _log.Waarschuwing("Gebruiker gaat door ondanks mislukte hoortest."); return false; }
        }
    }

    /// <summary>BUG #123: extra wachttijd na het omzetten van een wissel terwijl de loc op een melder staat, als de kaart de wissel niet bij die melder noemt.</summary>
    private const int ExtraWisselWachtOnbekendMs = 1500;

    /// <summary>BUG #93: extra wachttijd na het omzetten van een wissel die vlak achter de melder ligt waarop de loc staat.</summary>
    private const int ExtraWisselWachtMs = 2000;

    /// <summary>BUG #80: wissels die bij een nu bezette melder LIGGEN (de loc staat er met een deel op of op de tong): een wissel waarvan
    /// bij die melder een kortsluiting bij omzetten of een kortsluiting-oplossende combinatie is gevonden (hij wordt dus van achteren
    /// bereden en ligt in of vlak voor die melder). Omzetten terwijl de loc erop staat laat hem ontsporen (Marco 09:39, wissel 10 bij melder 24).
    /// Resultaat: adres -> melder waar de loc staat.</summary>
    private Dictionary<int, int> WisselsOnderLoc()
    {
        var res = new Dictionary<int, int>();
        var bezet = _monitor.Bezet;
        if (bezet.Count == 0) return res;
        foreach (var w in _kaart.Wissels)
            foreach (var o in w.Waarnemingen)
                if (bezet.Contains(o.NaMelder) &&
                    (o.Soort == WaarnemingSoort.KortsluitingBijOmzetten || o.Soort == WaarnemingSoort.LostKortsluitingOp))
                { res[w.Adres] = o.NaMelder; break; }
        return res;
    }

    /// <param name="spaarOnderLoc">BUG #80: sla wissels over die onder de loc liggen (alleen gebruiken als er géén rit volgt, bijv. na een
    /// mislukte terugweg waarbij de gebruiker de loc met de hand heeft neergezet). Ze worden pas omgezet als de loc weg is.</param>
    /// <summary>BUG #165 (log 16-10 17:43: kaart kende '8 -> 24 -> 14' ook met motor 13 rechtdoor, terwijl de loc daar stond te hangen): de configuratie waarmee een
    /// rit gelegd is, kan afwijken van wat de wissels echt doen als een motor niet omgezet kon worden (loc op de kruiswissel) of een wissel onder de loc bleef.
    /// Voor wat de kaart onthoudt geldt de laatst gestuurde stand per adres; een onbekende stand blijft zoals gewenst.</summary>
    internal Configuratie WerkelijkeStand(Configuratie c)
    {
        var uit = c;
        foreach (var (a, st) in _wisselStand)
            if (c.Bevat(a) != st) uit = uit.MetStand(a, st);
        return uit;
    }

    private bool _vrijRijdenBezig;
    /// <summary>BUG #188: wissels waarvan we weten dat ze bij de melder liggen waar de loc nu staat, ook al staat dat nog niet in de kaart (net ontdekt door de proef).</summary>
    private readonly HashSet<int> _bekendBijBezet = new();
    /// <summary>BUG #194: proefwissels die het herstel (klem-ingreep) terugzette naar rechtdoor; vindt ProbeerAlarmWegTeKrijgen daarna een partner, dan gaan ze terug naar afbuigend (wissel 1 samen met wissel 2).</summary>
    /// <summary>BUG #219: met de melders waar de loc stond toen de wissel werd losgemaakt; alleen bij dezelfde plek mag hij als partner terug naar afbuigend.</summary>
    private readonly Dictionary<int, HashSet<int>> _losgemaakteProefWissels = new();
    /// <summary>BUG #194: wissels (adres, stand afbuigend) die net een alarm wegnamen, met de melders waar de loc toen stond; ze blijven staan zolang de loc daar nog (deels) staat.</summary>
    private readonly Dictionary<int, (HashSet<int> Melders, DateTime Tijd)> _klemOplossing = new();

    /// <summary>BUG #181 (Marco 17-10 11:46/11:51: "doorrijden totdat de laatste melder alleen actief is"): staat de loc nog op twee melders (de neus op de nieuwe melder, de staart
    /// nog op de vorige) en moet er een wissel om die de kaart bij een van die melders noemt, dan ligt de loc mogelijk op of tegen de tong. Eerst doorrijden tot alleen de laatste
    /// melder bezet is, daarna pas omzetten. In de simulator gaf het omzetten van wissel 14 met de loc op 8 en 24 (en wissel 9/6 op 129) kortsluiting die daarna niet meer
    /// weg te krijgen was. Nog niet getest op de baan.</summary>
    private (int Melder, int Rit) _vrijGecontroleerd;
    /// <summary>Wordt vóór elk wisselcommando aangeroepen (ZetWissel): ligt de wissel bij een bezette melder waar de loc net aankwam, dan eerst doorrijden tot de loc de tong voorbij is.</summary>
    /// <summary>BUG #183: ligt de wissel achter de loc? Dan komt de kaart hem tegen als overgang van de vorige melder naar de huidige.</summary>
    private static bool WisselLigtAchter(WisselVondst w, (int Via, int Melder, Richting Richting) ing)
        => w.Waarnemingen.Any(o => o.NaMelder == ing.Via && (o.VolgendeBijRechtdoor == ing.Melder || o.VolgendeBijAfbuigend == ing.Melder));

    private async Task VrijRijdenVoorWissel(int adres)
    {
        if (_vrijRijdenBezig || _ingang is not { } ing || Kruis.IsMotor(adres) || HuidigeAlarmBlokken().Count > 0) return;   // met een alarm is omzetten juist het herstel
        _monitor.Bijwerken();
        var bezet = _monitor.Bezet;
        if (bezet.Count == 0 || !bezet.Contains(ing.Melder) || bezet.Any(m => m != ing.Melder && m != ing.Via)) return;
        if (!WisselsBijBezet(_kaart, bezet).Contains(adres) && !_bekendBijBezet.Contains(adres)) return;
        // BUG #183: alleen een wissel ACHTER de loc (waar hij net overheen reed) is een probleem; een wissel VOOR de loc (verderop in de sectie) niet. Doorkruipen
        // zette de loc juist op die wissel (129: wissel 9 achter, wissel 6 voor; kruipen voor wissel 5 zette de loc op de tong van wissel 6).
        if (bezet.Count == 1 && _kaart.Wissels.FirstOrDefault(w => w.Adres == adres) is { } wk && !WisselLigtAchter(wk, ing)) return;
        // BUG #181: ook met alleen de nieuwe melder bezet kan de loc nog op de wisseltong staan (de tong ligt in het gebied van die melder): per aankomst één keer door naar de verste sectie
        if (bezet.Count == 1 && _vrijGecontroleerd == (ing.Melder, _kaart.Ritten.Count)) return;
        _vrijGecontroleerd = (ing.Melder, _kaart.Ritten.Count);
        _log.Info($"Wissel {adres} moet om, maar die ligt bij melder {Lijst(bezet)} waar de loc net aankwam: eerst doorrijden tot alleen melder {ing.Melder} bezet is en de loc de wissel voorbij is (anders staat hij op de tong).");
        _vrijRijdenBezig = true;
        try
        {
            if (bezet.Count > 1) await Nastellen(ing.Melder, ing.Richting, achter: ing.Via);
            else await VrijVanIngangsWissel(ing.Melder, ing.Richting, ookZonderWaarneming: true);
        }
        finally { _vrijRijdenBezig = false; }
    }

    /// <summary>BUG #271 (autolog 21-10 12:11:21-12:12:11): na opdracht #5 (loc op 4 en 129) moest wissel 9 voor de terugweg 4 -> ... -> 10 -> 129 -> 24 -> 21 (vooruit) rechtdoor, maar
    /// lag onder de loc en bleef afbuigend ("Hij wordt omgezet zodra de loc daar weg is"). Dat gebeurde niet: de loc reed 10 -> 129 -> 4 -> 11 de keerlus rond tot de rondjescontrole.
    /// Gespaarde wissels worden nu onthouden en tijdens de rit omgezet zodra de loc niet meer bij die wissel staat. Een nieuwe ZetConfiguratie vervangt de lijst.</summary>
    private readonly Dictionary<int, bool> _uitgesteldeWissels = new();

    private async Task ZetUitgesteldeWissels()
    {
        if (_uitgesteldeWissels.Count == 0) return;
        var bezet = _monitor.Bezet;
        if (bezet.Count == 0 || HuidigeAlarmBlokken().Count > 0) return;
        var onder = WisselsOnderLoc();
        foreach (var a in WisselsBijBezet(_kaart, bezet)) onder.TryAdd(a, 0);
        foreach (var pa in _kaart.OverloopPartnersVan(onder.Keys).ToList()) onder.TryAdd(pa, 0);
        foreach (var (a, wens) in _uitgesteldeWissels.ToList())
        {
            if (onder.ContainsKey(a) || (Kruis.IsMotor(a) && Kruis.OpKruis(bezet))) continue;
            _uitgesteldeWissels.Remove(a);
            if (_wisselStand.TryGetValue(a, out var nu) && nu == wens) continue;
            _log.Info($"Wissel {a} gaat nu alsnog naar {(wens ? "afbuigend" : "rechtdoor")}: de loc staat er niet meer bij (bezet: {Lijst(bezet)}) (BUG #271, nog niet getest op de baan).");
            await ZetWissel(a, wens);
        }
    }

    private async Task ZetConfiguratie(Configuratie c, bool spaarOnderLoc = false)
    {
        _uitgesteldeWissels.Clear();   // BUG #271
        var onder = spaarOnderLoc ? WisselsOnderLoc() : new Dictionary<int, int>();
        // BUG #181: is er GEEN alarm, dan blijven ook wissels die de kaart bij een bezette melder noemt staan zolang de loc daar staat (sim: wissel 5 en 9 werden na een onverwachte
        // melder 129 rechtgezet terwijl de loc op die tongen stond: kortsluiting). Bij een alarm kan juist zo'n wissel de oplossing zijn (BUG #169).
        if (spaarOnderLoc && _monitor.Bezet.Count > 0 && HuidigeAlarmBlokken().Count == 0)
            foreach (var a in WisselsBijBezet(_kaart, _monitor.Bezet).Where(x => !Kruis.IsMotor(x) || Kruis.Middens.Count == 0))   // BUG #250: zolang de kruiswissel niet bekend is (optie 6) is motor 13/10 een gewone wissel en wordt hij ook niet onder de loc omgezet
                onder.TryAdd(a, _monitor.Bezet.OrderBy(x => x).First());
        // BUG #254 (log 20-10 15:28:43): wissel 2 werd gespaard (loc op melder 12), maar zijn bewezen partner wissel 1 ging wel naar rechtdoor: precies de stand die de kortsluiting gaf
        // (kortsluitpunt #3), de blokproef voor melder 12 kon daardoor niet draaien. Een partner van een gespaarde wissel wordt ook gespaard.
        foreach (var pa in _kaart.OverloopPartnersVan(onder.Keys).ToList())
            if (_ins.WisselAdressen().Contains(pa)) onder.TryAdd(pa, onder.Values.First());
        foreach (var a in _ins.WisselAdressen())
        {
            if (IsVolger(a)) continue;   // BUG #259: gaat mee met zijn meester
            bool wens = c.Bevat(a);
            if (onder.TryGetValue(a, out var m) && !(_wisselStand.TryGetValue(a, out var nu) && nu == wens))
            {
                _log.Waarschuwing($"Wissel {a} wordt nu NIET omgezet: de loc staat op melder {m} waar die wissel ligt (omzetten met de loc erop laat hem ontsporen). Hij wordt omgezet zodra de loc daar weg is.");
                _uitgesteldeWissels[a] = wens;   // BUG #271
                continue;
            }
            // BUG #133: motor 10 of 13 omzetten terwijl de loc op de kruiswissel staat geeft altijd kortsluiting (#96). VoerOpdrachtUit controleert dat vooraf, maar
            // ZetConfiguratie wordt ook na een handmatige plaatsing en bij herstel aangeroepen (13:13:03 kaart 1323: wissel 10 werd zo onder de loc teruggezet).
            if (Kruis.IsMotor(a) && Kruis.OpKruis(_monitor.Bezet) && !(_wisselStand.TryGetValue(a, out var kn) && kn == wens))
            {
                _log.Waarschuwing($"Motor {a} wordt nu NIET omgezet: de loc staat op de kruiswissel (melder {Kruis.MiddenTekst}) en omzetten geeft kortsluiting.");
                continue;
            }
            // BUG #194: een stand die net een alarm wegnam (wissel 1 bij wissel 2) blijft staan zolang de loc nog op die melders staat; daarna mag hij terug.
            // BUG #218 (log 18-10 12:08:43-12:08:55): dit gold alleen met spaarOnderLoc. De terugweg na de proef (TerugNaar -> ZetConfiguratie zonder sparen) zette wissel 10
            // 12 s nadat hij de kortsluiting had weggenomen weer rechtdoor terwijl de loc nog op 24/129 stond: 0,2 s later weer kortsluiting en een hele zoekronde extra.
            // Nu altijd, zolang de loc nog alleen op (een deel van) die melders staat.
            if (_klemOplossing.TryGetValue(a, out var ko))
            {
                var bezetNu = _monitor.Bezet;
                bool nogOp = KlemNogOnderLoc(ko.Melders, bezetNu, spaarOnderLoc) && (_klok.Nu - ko.Tijd).TotalMinutes < 10;
                if (!nogOp) _klemOplossing.Remove(a);
                else if (!wens && _wisselStand.TryGetValue(a, out var ks) && ks)
                {
                    _log.Info($"Wissel {a} blijft afbuigend staan: dat haalde zojuist de kortsluiting weg en de loc staat nog op melder {string.Join(" en ", _monitor.Bezet.Where(ko.Melders.Contains).OrderBy(x => x))}.");
                    continue;
                }
            }
            await ZetWissel(a, wens);
        }
    }

    /// <summary>BUG #240 (autolog 18-10 14:53:16): tijdens de kortsluiting meldde melder 24 'vrij' (geen stroom), dus de klemoplossing van wissel 10 werd alleen aan melder 129
    /// gekoppeld. Toen de stroom terugkwam stond de loc weer op 24 en 129; dat was 'niet alleen op de klemmelders' en wissel 10 ging 16 ms later rechtdoor: opnieuw kortsluiting en een
    /// minuut zoeken. De melders van een klemoplossing zijn nu ook de melder van de kortsluiting en de melders die toen bezet waren.</summary>
    private HashSet<int> KlemMelders(IEnumerable<int> bezet)
    {
        var res = bezet.ToHashSet();
        if ((_klok.Nu - _laatsteKortsluitingOm).TotalMinutes < 5)
        {
            if (_laatsteKortsluitContext is { } ctx) res.Add(ctx.Melder);
            foreach (var m in _laatsteKortsluitMelders) res.Add(m);
        }
        return res;
    }

    /// <summary>BUG #219: hoort een losgemaakte proefwissel bij de kortsluiting op <paramref name="bezetNu"/>? Alleen als de loc toen op (een deel van) dezelfde melders stond.</summary>
    internal static bool PartnerHoortBijPlek(IReadOnlyCollection<int> losBij, IReadOnlyCollection<int> bezetNu)
        => losBij.Count == 0 || losBij.Any(bezetNu.Contains);

    /// <summary>BUG #218: staat de loc nog op de plek waar een klemoplossing het alarm wegnam? Ja als er een van die melders bezet is en (zonder sparen, dus bij
    /// het instellen van een nieuwe route) de loc nergens anders staat: is hij al deels op een andere melder, dan mag de route de wissel weer bepalen.</summary>
    internal static bool KlemNogOnderLoc(IReadOnlyCollection<int> klemMelders, IReadOnlyCollection<int> bezet, bool spaarOnderLoc)
        => bezet.Any(klemMelders.Contains) && (spaarOnderLoc || bezet.All(klemMelders.Contains));

    // =====================================================================
    // Blokproef (alleen Dinamo): welk blok voedt de sectie waar de loc staat?
    // =====================================================================

    private List<int> BlokKandidaten(int? eerst, int? melder = null)
    {
        var alle = _ins.DinamoBlokLijst();
        var gebruikt = _kaart.Melders.Where(m => m.DinamoBlok is not null).Select(m => m.DinamoBlok!.Value).ToHashSet();
        var res = new List<int>();
        // BUG #84: volgens de Koploper-referentie is dit een doorgangsmelder (wisselgebied zonder eigen blok, bv. 24 en 129):
        // de rijstroom komt dan van een wisselstraatblok (8/9), dus alleen die blokken proeven (de meest passende eerst).
        if (melder is int dm && Referentie is { } rf && rf.IsDoorgangsmelder(dm))
        {
            var wb = rf.WisselstraatBlokkenVoor(dm, _kaart.Melders.Where(x => x.Nummer != dm && x.DinamoBlok is not null).Select(x => x.DinamoBlok!.Value).ToHashSet())
                       .Where(alle.Contains).ToList();
            if (wb.Count > 0) return wb;
        }
        // BUG #84: bij een gewone melder het Koploper-blok eerst proberen.
        if (melder is int km && Referentie?.BlokVan(km) is int kb && alle.Contains(kb)) res.Add(kb);
        // BUG #62: een blok dat uit een eerdere verkenning van deze baan bekend is voor deze melder, eerst proberen
        // (een verkeerde hint kost hooguit één korte poging).
        if (melder is int mm && Voorkennis?.BlokHint(mm) is int hint && alle.Contains(hint)) res.Add(hint);
        if (eerst is int e0 && alle.Contains(e0) && !res.Contains(e0)) { res.Add(e0); eerst = null; }
        if (eerst is int e && alle.Contains(e) && !res.Contains(e)) res.Add(e);
        res.AddRange(alle.Where(b => !gebruikt.Contains(b) && !res.Contains(b)));
        res.AddRange(alle.Where(b => !res.Contains(b)));
        return res;
    }

    private void KoppelBlok(int melder, int blok)
    {
        Kennis.Leg("blok", melder.ToString(), blok.ToString(), "KoppelBlok");
        _kaart.Melder(melder).DinamoBlok = blok;
        if (Referentie is { } rf && rf.KlopptMetKoploper(melder, blok) == false)
            _log.Waarschuwing($"Melder {melder} lijkt rijstroom van Dinamo-blok {blok} te krijgen, maar volgens de Koploper-export is dat {rf.Omschrijving(melder)}. Controleer dit (kan een spookmelding zijn).");
        _log.Vondst($"Melder {melder} krijgt rijstroom van Dinamo-blok {blok}.");
        BewaarNu();
        MeldVoortgang();
    }

    /// <summary>BUG #288 (autolog 21-10 16:28:45-16:35:21): de blokproef voor melder 139 (doodlopend einde, vooruit) probeerde eerst vooruit, tegen het stootjuk: 12 blokken x 30 s op
    /// verkensnelheid, ruim 6 minuten, voordat 'nu de andere richting' volgde. Is de melder in een richting een bekend doodlopend einde, dan begint de proef in de andere richting.</summary>
    internal static Richting[] BlokproefRichtingen(Baankaart kaart, int melder)
    {
        bool vooruitDood = kaart.Kopsporen.Any(k => k.Melder == melder && k.Richting == Richting.Vooruit);
        bool achteruitDood = kaart.Kopsporen.Any(k => k.Melder == melder && k.Richting == Richting.Achteruit);
        return vooruitDood && !achteruitDood ? new[] { Richting.Achteruit, Richting.Vooruit } : new[] { Richting.Vooruit, Richting.Achteruit };
    }

    /// <summary>Bij de start: de loc staat ergens binnen melder <paramref name="melder"/>
    /// (positie onbekend). Elk blok krijgt om beurten een rijcommando; het blok waarbij
    /// binnen BlokproefLangSeconden een melder verandert, is het goede.</summary>
    private async Task<bool> BlokZoekenBijStart(int melder)
    {
        _log.Stap($"Blokproef voor melder {melder}: elk Dinamo-blok krijgt om beurten een rijcommando.");
        _monitor.Bijwerken();
        var bezetVoorProef = _monitor.Bezet.ToList();   // BUG #176: een melder die erbij komt ligt vóór de loc in de proefrichting
        foreach (var r in BlokproefRichtingen(_kaart, melder))
        {
            foreach (var blok in BlokKandidaten(null, melder))
            {
                _token.ThrowIfCancellationRequested();
                _monitor.Bijwerken();
                _log.Rijden($"Blokproef: blok {blok}, {r.Tekst()} …");
                var t0 = _klok.Nu;
                await StuurNaarBlok(blok, r, _ins.Verkensnelheid);
                var eind = _klok.Nu + TimeSpan.FromSeconds(_ins.BlokproefLangSeconden);
                bool gewijzigd = false;
                while (_klok.Nu < eind)
                {
                    await Wacht(Poll);
                    // BUG #55: alleen wijzigingen NA het commando tellen (een nasleep van de vorige rit gaf een verkeerde koppeling, 144 -> blok 10)
                    if (_monitor.Bijwerken().Any(x => x.Tijd >= t0)) { gewijzigd = true; break; }
                    // BUG #164 (hardwarelog 16-10 09:01-09:10): met een loc in een kortsluiting is er in het blok geen rijstroom; elk van de 18 proeven (9 minuten) kon dus
                    // alleen mislukken. Dat zegt niets over de blokkoppeling.
                    if (HuidigeAlarmBlokken().Count > 0 && _klok.Nu >= _negeerKortsluitingTot)
                    {
                        _log.Waarschuwing($"Dinamo meldt kortsluiting in blok {string.Join(" en ", HuidigeAlarmBlokken())}: zonder rijstroom kan geen enkel blok de loc laten rijden. De blokproef voor melder {melder} stopt; het ligt niet aan de blokkoppeling.");
                        await StuurNaarBlok(blok, r, 0);
                        await VolledigeStop();
                        return false;
                    }
                }
                if (gewijzigd)
                {
                    // Eerst koppelen, dan stoppen via de blokken waar de loc NU staat (hij
                    // kan al een sectie verder zijn - een 0 naar alleen het proefblok komt
                    // dan niet meer aan).
                    KoppelBlok(melder, blok);
                    _hw.ZetLocSnelheid(_ins.LocAdres, 0, r == Richting.Vooruit, blok, _ins.LocStappen);
                    await StopLoc();
                    // BUG #176: staat de loc nu deels op een melder die er tijdens de proef bij kwam, dan ligt die voor hem: terugkruipen. Zonder dit kroop hij eerst
                    // verder (op de baan 13/14: vooruit, dan pas terug) en schoot in de simulatie door tot in de volgende melder.
                    _monitor.Bijwerken();
                    int? erbij = !Stap ? null : _monitor.Bezet.Where(x => x != melder && !bezetVoorProef.Contains(x)).Select(x => (int?)x).FirstOrDefault();
                    await Nastellen(melder, r, voor: erbij);
                    return true;
                }
                await StuurNaarBlok(blok, r, 0);
            }
            _log.Info($"Geen enkel blok liet de loc {r.Tekst()} rijden - misschien staat hij aan het eind van een kopspoor; nu de andere richting.");
        }
        _log.Fout($"Geen enkel Dinamo-blok ({_ins.DinamoBlokken}) kon de loc op melder {melder} laten rijden. Controleer locadres, rijstappen en het blokbereik.");
        return false;
    }

    /// <summary>Tijdens het rijden: de loc staat net helemaal in een nieuwe sectie
    /// <paramref name="melder"/>, vlak achter de grens met <paramref name="vorige"/>. Een
    /// klein stukje terug laat de vorige melder snel weer bezet worden - maar alleen als het
    /// juiste blok het commando stuurt.</summary>
    private async Task<bool> BlokZoekenNaInrijden(int melder, int vorige, Richting r)
    {
        _log.Stap($"Nieuwe sectie: melder {melder}. Blokproef (steeds een klein stukje {r.Om().Tekst()}).");
        foreach (var blok in BlokKandidaten(BlokVan(vorige), melder))
        {
            _token.ThrowIfCancellationRequested();
            _monitor.Bijwerken();
            var t0 = _klok.Nu;
            await StuurNaarBlok(blok, r.Om(), _ins.Verkensnelheid);
            var eind = _klok.Nu + TimeSpan.FromSeconds(_ins.BlokproefKortSeconden);
            bool gewijzigd = false;
            while (_klok.Nu < eind)
            {
                await Wacht(Poll);
                if (_monitor.Bijwerken().Any(x => x.Tijd >= t0)) { gewijzigd = true; break; }
                if (ProefKortsluitingGemeld()) break;
            }
            if (_proefKortsluiting && !gewijzigd) { await StuurNaarBlok(blok, r.Om(), 0); return false; }
            if (gewijzigd)
            {
                KoppelBlok(melder, blok);
                _hw.ZetLocSnelheid(_ins.LocAdres, 0, r.Om() == Richting.Vooruit, blok, _ins.LocStappen);
                await StopLoc(); // via de blokken waar de loc nu staat (zie BlokZoekenBijStart)
                await Nastellen(melder, r.Om(), voor: vorige);
                return true;
            }
            await StuurNaarBlok(blok, r.Om(), 0);
            _log.Rijden($"Blok {blok}: geen beweging.");
        }
        return false;
    }

    /// <summary>BUG #57: tweede poging als "een klein stukje terug" bij geen enkel blok beweging gaf
    /// (op de baan: melder 132). De loc staat dan helemaal in de nieuwe sectie, dus elk blok dat hem
    /// hier in de rijrichting laat bewegen, voedt deze sectie. De loc rijdt daarbij gewoon door en
    /// de rit gaat verder.</summary>
    private async Task<bool> BlokZoekenDoorrijdend(int melder, Richting r)
    {
        _log.Info($"Melder {melder}: terugrijden gaf bij geen enkel blok beweging; nu per blok doorrijden ({r.Tekst()}).");
        foreach (var blok in BlokKandidaten(null, melder))
        {
            _token.ThrowIfCancellationRequested();
            _monitor.Bijwerken();
            var t0 = _klok.Nu;
            await StuurNaarBlok(blok, r, _ins.Verkensnelheid);
            var eind = _klok.Nu + TimeSpan.FromSeconds(_ins.BlokproefLangSeconden);
            bool gewijzigd = false;
            while (_klok.Nu < eind)
            {
                await Wacht(Poll);
                if (_monitor.Bijwerken().Any(x => x.Tijd >= t0)) { gewijzigd = true; break; }
                if (ProefKortsluitingGemeld()) break;
            }
            if (_proefKortsluiting && !gewijzigd) { await StuurNaarBlok(blok, r, 0); return false; }
            if (gewijzigd)
            {
                KoppelBlok(melder, blok);
                return true; // niet stoppen: de rit gaat door in dezelfde richting
            }
            await StuurNaarBlok(blok, r, 0);
            _log.Rijden($"Blok {blok}: geen beweging ({r.Tekst()}).");
        }
        return false;
    }

    /// <summary>BUG #142: blokproef voor een melder waar de loc nooit alleen op kan staan (loc langer dan de melder). De loc staat
    /// op <paramref name="melder"/> én op de buurmelder(s) <paramref name="buren"/> waarvan het blok al bekend is. Die blokken
    /// krijgen geen rijcommando; elk ander blok om beurten wel. Beweegt de loc, dan voedt dat blok het deel van de loc dat in
    /// <paramref name="melder"/> staat. De loc rijdt daarbij een stukje in de rijrichting en wordt daarna gestopt.</summary>
    private async Task<bool> BlokZoekenMetBuurmelder(int melder, IReadOnlyList<int> buren, Richting r)
    {
        var uit = buren.Select(b => BlokVan(b)!.Value).ToHashSet();
        var kandidaten = BlokKandidaten(null, melder).Where(b => !uit.Contains(b)).ToList();
        _log.Stap($"Melder {melder}: de loc staat ook op melder {Lijst(buren)} (blok {string.Join(", ", uit.OrderBy(x => x))}). Blokproef met alleen de overige blokken ({string.Join(", ", kandidaten)}).");
        foreach (var blok in kandidaten)
        {
            _token.ThrowIfCancellationRequested();
            _monitor.Bijwerken();
            var t0 = _klok.Nu;
            await StuurNaarBlok(blok, r, _ins.Verkensnelheid);
            var eind = _klok.Nu + TimeSpan.FromSeconds(_ins.BlokproefKortSeconden);
            bool gewijzigd = false;
            while (_klok.Nu < eind)
            {
                await Wacht(Poll);
                if (_monitor.Bijwerken().Any(x => x.Tijd >= t0)) { gewijzigd = true; break; }
                if (ProefKortsluitingGemeld()) break;
            }
            if (_proefKortsluiting && !gewijzigd) { await StuurNaarBlok(blok, r, 0); return false; }
            if (gewijzigd)
            {
                KoppelBlok(melder, blok);
                _hw.ZetLocSnelheid(_ins.LocAdres, 0, r == Richting.Vooruit, blok, _ins.LocStappen);
                await StopLoc();
                return true;
            }
            await StuurNaarBlok(blok, r, 0);
            _log.Rijden($"Blok {blok}: geen beweging.");
        }
        return false;
    }

    // =====================================================================
    // Neerzetten op een melder
    // =====================================================================

    /// <summary>BUG #58: richting waarin <paramref name="doel"/> ligt vanaf een van de bezette melders, volgens de al
    /// gemeten overgangen op de baankaart (null = onbekend).</summary>
    private Richting? RichtingNaarDoel(IEnumerable<int> bezet, int doel)
    {
        foreach (var m in bezet)
        {
            if (m == doel) continue;
            var o = _kaart.Overgangen.FirstOrDefault(x => x.Van == m && x.Naar == doel);
            if (o is not null) return o.Richting;
            var t = _kaart.Overgangen.FirstOrDefault(x => x.Van == doel && x.Naar == m);
            if (t is not null) return t.Richting.Om();
            // BUG #153: ook het leerprofiel (voorkennis) kent de richting; alleen een hint (staat de loc anders om, dan kost een verkeerde gok één poging).
            if (Voorkennis is { } lp)
            {
                var pm = lp.Overgangen.FirstOrDefault(x => x.Aantal > 0 && x.Van == m && x.Naar == doel);
                if (pm is not null) return pm.Richting;
                var pt = lp.Overgangen.FirstOrDefault(x => x.Aantal > 0 && x.Van == doel && x.Naar == m);
                if (pt is not null) return pt.Richting.Om();
            }
        }
        return null;
    }

    /// <summary>Zorgt dat alleen <paramref name="doel"/> bezet is. De loc reed laatst in
    /// <paramref name="laatsteRichting"/>. Staat hij nog half op de melder erachter
    /// (<paramref name="achter"/>), dan rijdt hij verder; staat hij er deels voorbij
    /// (<paramref name="voor"/>) of helemaal voorbij, dan kruipt hij terug. Onbekend: eerst
    /// verder, en bij doorschieten terug.</summary>
    /// <summary>BUG #215: true als de laatste overgang (voorlaatste -> laatste melder) eerder in dezelfde reeks al voorkwam.</summary>
    internal static bool HerhaaldeOvergang(List<int> reeks)
    {
        if (reeks.Count < 3) return false;
        int a = reeks[^2], b = reeks[^1];
        for (int i = 0; i + 1 < reeks.Count - 1; i++)
            if (reeks[i] == a && reeks[i + 1] == b) return true;
        return false;
    }

    private async Task Nastellen(int doel, Richting laatsteRichting, int? achter = null, int? voor = null)
    {
        await NastellenKern(doel, laatsteRichting, achter, voor);
        await VrijVanIngangsWissel(doel, laatsteRichting);
    }

    /// <summary>BUG #152 (Marco 07:48: "rijd van de verst verwijderde sectie naar het volgende blok, anders sta je soms al in de wissel; komt bij melder 5 van
    /// blok 3 al voor"): nastellen stopt zodra alleen de doelmelder bezet is, dus met de neus net op de melder en de rest van de loc nog in het
    /// wisselgebied aan de ingang. Weet de kaart dat bij deze melder een wissel ligt (kortsluiting of oplossing direct na deze melder), dan kruipt de loc daarna nog een stuk
    /// door in de rijrichting (een derde van de bekende tijd tot de volgende melder), zodat hij op de verste sectie van de melder staat en vrij van de wissel.
    /// Onbekende tijd of niet-bekende wissel: niets doen. Nog niet getest op de baan.</summary>
    private async Task VrijVanIngangsWissel(int doel, Richting r, bool ookZonderWaarneming = false)
    {
        bool wisselBij = ookZonderWaarneming || _kaart.Wissels.Any(w => w.Waarnemingen.Any(o => o.NaMelder == doel
            && (o.Soort == WaarnemingSoort.KortsluitingBijOmzetten || o.Soort == WaarnemingSoort.LostKortsluitingOp)));
        if (!wisselBij) return;
        _monitor.Bijwerken();
        if (!(_monitor.Bezet.Count == 1 && _monitor.Bezet.Contains(doel))) return;
        var uit = _kaart.Overgangen.Where(x => x.Van == doel && x.Richting == r && x.AantalMetingen > 0).ToList();
        double duur;
        if (uit.Count > 0)
        {
            double sec = uit.Min(x => x.GemiddeldeSeconden) * RefSnelheid / Math.Max(1, _ins.Verkensnelheid)
                         * Math.Max(1, _ins.Verkensnelheid) / Math.Max(1, _ins.Kruipsnelheid);
            duur = Math.Min(30, sec / 3);
        }
        else if (ookZonderWaarneming && _ins.StapDoorrijSeconden > 0)
        {
            // BUG #181: de tijd naar de volgende melder is nog niet gemeten (bij stap voor stap stopt elke rit op de nieuwe melder): dezelfde afstand als het doorrijden na de nieuwe
            // melder (Marco 11:18: minstens 2 s na het zien van de melder), maar met de kruipsnelheid.
            duur = Math.Min(30, _ins.StapDoorrijSeconden * Math.Max(1, _ins.Verkensnelheid) / Math.Max(1, _ins.Kruipsnelheid));
        }
        else return;
        if (duur < 1) return;
        _log.Rijden($"Melder {doel} heeft een wissel in het gebied: de loc kruipt nog {duur:0.0} s door {r.Tekst()} naar de verste sectie van de melder, zodat hij niet in de wissel blijft staan (niet getest op de baan).");
        await StuurSnelheid(r, _ins.Kruipsnelheid, null);
        var eind = _klok.Nu + TimeSpan.FromSeconds(duur);
        while (_klok.Nu < eind)
        {
            await Wacht(Poll);
            _monitor.Bijwerken();
            if (_monitor.Bezet.Any(m => m != doel)) break; // volgende melder gehaald: niet verder
        }
        await StopLoc();
        _monitor.Bijwerken();
        if (!_monitor.IsBezet(doel) || _monitor.Bezet.Count > 1)
            await NastellenKern(doel, r.Om(), null, null);
    }

    private async Task NastellenKern(int doel, Richting laatsteRichting, int? achter = null, int? voor = null)
    {
        await Wacht(TimeSpan.FromMilliseconds(500));
        _monitor.Bijwerken();
        var bezet = _monitor.Bezet;
        if (bezet.Count == 1 && bezet.Contains(doel)) return;

        Richting r;
        // BUG #58: staat de loc alleen op een melder waarvan de kaart al weet in welke richting het doel ligt
        // (gemeten overgang), dan kruipt hij daarheen i.p.v. te gokken.
        if (!bezet.Contains(doel)) r = RichtingNaarDoel(bezet, doel) ?? laatsteRichting.Om();
        // BUG #153: staat de loc op het doel en op een buurmelder waarvan de richting bekend is (kaart of voorkennis), dan weg van die buur kruipen
        // (simulatie met voorkennis: de loc kroop 50 s de verkeerde kant op, naar melder 20 i.p.v. terug van 30).
        else if (bezet.Count == 2 && RichtingNaarDoel(bezet, doel) is Richting naarDoel) r = naarDoel;
        else if (voor is int v && bezet.Contains(v)) r = laatsteRichting.Om();
        else if (achter is int a && bezet.Contains(a)) r = laatsteRichting;
        else r = laatsteRichting;

        _log.Rijden($"Neerzetten op melder {doel} (nu bezet: {Lijst(bezet)}), kruipen {r.Tekst()}.");
        int stilstaandePogingen = 0;
        for (int wissel = 0; wissel < 3; wissel++)
        {
            if (wissel > 0) _log.Rijden($"Neerzetten op melder {doel}: nu {r.Tekst()} (bezet: {Lijst(_monitor.Bezet)}).");
            // BUG #140: kroop de vorige poging niet vooruit (zelfde melders bezet), dan iets meer snelheid (maximaal de verkensnelheid).
            int stapNu = Math.Min(Math.Max(_ins.Kruipsnelheid, _ins.Verkensnelheid), _ins.Kruipsnelheid + 2 * stilstaandePogingen);
            await StuurSnelheid(r, stapNu, doel);
            _monitor.Bijwerken();
            bool doelWasBezet = _monitor.IsBezet(doel); // vers gemeten: een melder die net wegviel telt niet als 'doel bereikt en voorbij'
            var bezetBijStart = _monitor.Bezet.ToList();
            _log.Rijden($"Neerzetten op melder {doel}: snelheid {stapNu} {r.Tekst()} naar blok {string.Join(", ", _voedingBlokken.OrderBy(b => b))}.");
            // BUG #58: bij kruipsnelheid kost een stuk spoor veel tijd; de tijd per poging verdubbelt (15, 30, 60 s)
            // zodat de loc niet "net voor de melder" wordt afgebroken en daarna weer van richting wisselt.
            double basisTijd = Math.Max(15, _ins.MinSecondenTussenMelders);
            if (VerwachteTijd(_monitor.Bezet, doel) is double verwacht) // BUG #59: kruipend kost dezelfde afstand evenredig meer tijd
                basisTijd = Math.Max(basisTijd, Math.Min(120, verwacht * _ins.Verkensnelheid / Math.Max(1, _ins.Kruipsnelheid) * 1.5));
            var eind = _klok.Nu + TimeSpan.FromSeconds(Math.Min(180, basisTijd * (1 << wissel)));
            bool omdraaien = false;
            while (_klok.Nu < eind)
            {
                await Wacht(Poll);
                _monitor.Bijwerken();
                bezet = _monitor.Bezet;
                if (bezet.Count == 1 && bezet.Contains(doel)) break;
                if (doelWasBezet && !bezet.Contains(doel) && bezet.Count > 0)
                {
                    // Doorgeschoten? Een bezetmelder kan even wegvallen terwijl de loc er nog (deels) op staat:
                    // eerst kort afwachten voordat de richting omgekeerd wordt.
                    await Wacht(TimeSpan.FromMilliseconds(1500));
                    _monitor.Bijwerken();
                    bezet = _monitor.Bezet;
                    if (bezet.Count == 1 && bezet.Contains(doel)) break;
                    if (!bezet.Contains(doel) && bezet.Count > 0) { omdraaien = true; break; }
                }
                if (bezet.Contains(doel)) doelWasBezet = true;
            }
            await StopLoc();
            _monitor.Bijwerken();
            bezet = _monitor.Bezet;
            if (bezet.Count == 1 && bezet.Contains(doel)) return;
            if (bezet.OrderBy(x => x).SequenceEqual(bezetBijStart.OrderBy(x => x)) && !omdraaien)
            {
                stilstaandePogingen++;
                _log.Waarschuwing($"Neerzetten op melder {doel}: de loc is niet verplaatst (nog steeds bezet: {Lijst(bezet)}) na een poging met snelheid {stapNu}{(stilstaandePogingen < 3 ? "; de volgende poging krijgt iets meer snelheid" : "")}.");
            }
            else stilstaandePogingen = 0;
            if (!omdraaien && bezet.Contains(doel) && bezet.Count > 1 && wissel > 0) break;
            r = !bezet.Contains(doel) ? (RichtingNaarDoel(bezet, doel) ?? r.Om()) : r.Om();
        }
        if (!bezet.Contains(doel))
            throw new NavigatieFout($"De loc kon niet netjes op melder {doel} gezet worden (bezet: {Lijst(bezet)}).");
        if (bezet.Count > 1)
            _log.Waarschuwing($"Loc staat op melder {doel} maar ook nog op {Lijst(bezet.Where(m => m != doel))} - verder zo goed mogelijk.");
    }

    // =====================================================================
    // De rit zelf
    // =====================================================================

    /// <summary>BUG #55: langste tijd (s) die de loc nodig had om vanuit stilstand een nieuwe melder te halen.</summary>
    private double _langsteVertrekSeconden;
    /// <summary>BUG #200: melders waar de loc bij het neerzetten doorschoot.</summary>
    private readonly HashSet<int> _doorschietMelders = new();

    private int RefSnelheid => _kaart.RefSnelheid > 0 ? _kaart.RefSnelheid : _ins.Verkensnelheid;
    private int _startVerkensnelheid;
    private bool _snelheidVerlaagd;
    private double _ritMinDt = double.MaxValue, _ritMaxDt;

    /// <summary>BUG #59: verwachte tijd (s, bij de huidige verkensnelheid) om vanaf een van de bezette melders
    /// bij <paramref name="doel"/> te komen: eerst de metingen van deze verkenning, anders het leerprofiel.</summary>
    private double? VerwachteTijd(IEnumerable<int> bezet, int doel)
    {
        int v = Math.Max(1, _ins.Verkensnelheid);
        foreach (var m in bezet)
        {
            if (m == doel) continue;
            var o = _kaart.Overgangen.FirstOrDefault(x => x.AantalMetingen > 0 && ((x.Van == m && x.Naar == doel) || (x.Van == doel && x.Naar == m)));
            if (o is not null) return o.GemiddeldeSeconden * RefSnelheid / v;
            if (Voorkennis is { } lp)
            {
                var richtingen = new[] { Richting.Vooruit, Richting.Achteruit };
                foreach (var rr in richtingen)
                {
                    var t = lp.Verwacht(m, doel, rr, v) ?? lp.Verwacht(doel, m, rr, v);
                    if (t is double tt) return tt;
                }
            }
        }
        return null;
    }

    /// <summary>BUG #59: na elke rit de verkensnelheid voorzichtig bijstellen. Alleen op basis van gemeten tijden tussen
    /// melders tijdens het rijden (niet het vertrek vanuit stilstand): heel korte secties (&lt; 1,5 s) worden gemist of
    /// geven te weinig tijd om te stoppen, dus iets langzamer; zijn alle secties lang (&gt; 4 s) en duurt de langste meer
    /// dan de helft van de maximale wachttijd, dan iets sneller (nooit meer dan 1,5x de beginsnelheid).</summary>
    private void PasSnelheidAutomatischAan()
    {
        if (!_ins.AutoSnelheid || _ritMinDt == double.MaxValue) return;
        int v = _ins.Verkensnelheid;
        int ondergrens = Math.Max(_ins.Kruipsnelheid + 2, 5);
        int bovengrens = Math.Max(v, (int)Math.Round(_startVerkensnelheid * 1.5));
        int nieuw = v;
        string reden = "";
        if (_ritMinDt < 1.5 && v > ondergrens)
        {
            nieuw = Math.Max(ondergrens, v - Math.Max(1, v / 4));
            reden = $"kortste gemeten sectie {_ritMinDt:0.0} s is te kort";
            _snelheidVerlaagd = true; // daarna nooit meer verhogen: voorkomt heen-en-weer pendelen van de snelheid
        }
        else if (!_snelheidVerlaagd && _ritMinDt > 4 && _ritMaxDt > 0.5 * _ins.MaxSecondenTussenMelders && v < bovengrens)
        {
            nieuw = Math.Min(bovengrens, v + 2);
            reden = $"alle gemeten secties zijn lang (korste {_ritMinDt:0.0} s, langste {_ritMaxDt:0.0} s)";
        }
        if (nieuw == v) return;
        _log.Info($"Snelheid automatisch aangepast ({reden}).");
        PasSnelhedenAan(nieuw, Math.Min(_ins.Kruipsnelheid, Math.Max(2, nieuw - 2)));
    }

    /// <summary>BUG #284/#300: na hoeveel seconden zonder nieuwe melder de loc op een nog onbekend stuk naar kruipsnelheid gaat.</summary>
    private double KruipNaarEindeNa()
    {
        // BUG #300 (autolog 25-10 08:57:05-08:58:08): met de mediaan (3 s) ging de loc bij de eerste rondrit al na 7 s op kruipsnelheid, ook in de lange secties 29 -> 30, 27 -> 28 en
        // 25 -> 17 (12-14 s): de rit duurde veel langer en na 29 bijna 'doodlopend'. Nu: 0,8 x de langste gemeten sectietijd, tussen 8 en 14 s (zonder metingen 10 s).
        var tijden = _kaart.Overgangen.Where(o => o.AantalMetingen > 0 && o.GemiddeldeSeconden > 0).Select(o => o.GemiddeldeSeconden * RefSnelheid / Math.Max(1, _ins.Verkensnelheid)).ToList();
        return tijden.Count == 0 ? 10 : Math.Clamp(tijden.Max() * 0.8, 8, 14);
    }

    private TimeSpan WachttijdTussenMelders()
    {
        double max = _ins.MaxSecondenTussenMelders;
        if (!_ins.SlimmeTimeout) return TimeSpan.FromSeconds(max);
        double langste = _kaart.LangsteReistijd() * RefSnelheid / Math.Max(1, _ins.Verkensnelheid);
        int geleerd = 0;
        if (Voorkennis is { AantalMetingen: > 0 } lp)
        {
            langste = Math.Max(langste, lp.LangsteVerwacht(_ins.Verkensnelheid)); // BUG #59
            geleerd = Math.Min(3, lp.AantalMetingen);
        }
        // BUG #55: pas slim worden na een paar echte metingen, en nooit korter dan de tijd die de
        // loc nodig had om vanuit stilstand de eerste melder te halen (een lange sectie zoals 144
        // kost ~15 s; met 1 meting van 3 s werd de wachttijd 10 s en volgde onterecht "doodlopend").
        int metingen = _kaart.Overgangen.Sum(o => o.AantalMetingen) + geleerd;
        if (langste <= 0 || metingen < 3) return TimeSpan.FromSeconds(max);
        double s = Math.Clamp(langste * 3, _ins.MinSecondenTussenMelders, max);
        s = Math.Min(max, Math.Max(s, _langsteVertrekSeconden * 2));
        return TimeSpan.FromSeconds(s);
    }

    /// <summary>Rijdt vanaf de melder waar de loc nu staat in richting <paramref name="r"/>
    /// en legt elke nieuw bezette melder vast, tot een van de stopcondities optreedt.</summary>
    /// <summary>BUG #82 (logs 10:04 en 11:03): Dinamo meldde een kortsluiting in het blok van de melder waar de loc net stilstond (alarm kwam na
    /// "melder 24 bezet", terwijl de verkenner de volgende stap voorbereidde). De verkenner zette toch wissels om en startte de rit: het
    /// alarmvlaggetje werd bij de ritstart gewist en een alarm komt maar één keer binnen, dus de kortsluiting werd niet meer gezien.
    /// Nu: staat er een blokalarm, dan korte tijd wachten of het vanzelf verdwijnt; zo niet, dan geen wissels omzetten en niet rijden
    /// maar een navigatiefout (loc controleren/terugzetten, mogelijk staat een wissel verkeerd).</summary>
    private Task ControleerGeenAlarmVoorStap(string stap) => ControleerGeenAlarmVoorStapMet(stap, null, null);

    private async Task ControleerGeenAlarmVoorStapMet(string stap, int? startMelder, Richting? stapRichting)
    {
        _alarmStand.Clear();
        if (HuidigeAlarmBlokken().Count == 0) return;
        // BUG #158 (Marco 15-10 12:21, kruiswissel 24): deze kortsluiting hoort bij de melder waar de loc nu staat en de richting van de stap. Zonder dit
        // bleef de context van een oudere kortsluiting (129 vooruit) staan en kwam de correctie van de gebruiker bij het verkeerde kortsluitpunt terecht.
        if (startMelder is int sm && stapRichting is Richting sr)
            _laatsteKortsluitContext = (sm, sr, new Configuratie(_wisselStand.Where(w => w.Value).Select(w => w.Key).ToArray()));
        _laatsteKortsluitingOm = _klok.Nu;   // BUG #117: ook deze kortsluiting telt voor 'kort geleden een kortsluiting' (blokproef overslaan)
        _log.Waarschuwing($"Dinamo meldt kortsluiting in blok {string.Join(" en ", HuidigeAlarmBlokken())} voordat de volgende stap begint ({stap}). Even wachten of dat vanzelf verdwijnt.");
        for (int i = 0; i < AlarmWachtPogingen && HuidigeAlarmBlokken().Count > 0; i++)
            await Wacht(TimeSpan.FromMilliseconds(300));
        var nog = HuidigeAlarmBlokken();
        if (nog.Count == 0) { _log.Info("Het alarm is verdwenen; de verkenner gaat verder."); return; }
        _hw.Noodstop();
        // BUG #134 (kaart 1323, 13:19:52): op de kruiswissel zelf draaide de alarmroutine 75 s lang 22 wissels om zonder iets te vinden (de oorzaak is daar een
        // kruismotor, die de routine nooit aanraakt) en dat vergroot de kans dat Dinamo vastloopt. Op de kruiswissel meteen de gebruiker laten kijken.
        if (Kruis.OpKruis(_monitor.Bezet))
        {
            _log.Waarschuwing($"De loc staat op de kruiswissel (melder {Kruis.MiddenTekst}): de alarmroutine probeert geen losse wissels, de gebruiker wordt gevraagd welke motor verkeerd staat.");
            throw new NavigatieFout($"Dinamo meldt nog steeds kortsluiting in blok {string.Join(" en ", HuidigeAlarmBlokken())} terwijl de loc op de kruiswissel (melder {Kruis.MiddenTekst}) stilstaat ({stap}). Waarschijnlijk staat motor 10 of 13 verkeerd.") { Kortsluiting = true };
        }
        if (await ProbeerAlarmWegTeKrijgen(stap)) return; // BUG #108
        throw new NavigatieFout($"Dinamo meldt nog steeds kortsluiting in blok {string.Join(" en ", HuidigeAlarmBlokken())} terwijl de loc stilstaat ({stap}). De verkenner heeft zelf wissels geprobeerd zonder dat de kortsluiting wegging en rijdt niet verder.") { Kortsluiting = true };
    }
    private const int AlarmWachtPogingen = 10;

    /// <summary>BUG #117 (Marco 16:12, log 15:58): ProbeerAlarmWegTeKrijgen vond dat wissel 1 afbuigend de kortsluiting oplost ("de wissel blijft zo staan"),
    /// maar de stap die daarna volgde zette alle wissels meteen weer in de stand van de route (wissel 1 rechtdoor): na 0,8 s was de kortsluiting terug en
    /// de loc zat vast (11 minuten blokproef). De stand die het alarm weghaalde geldt nu voor de stap die er direct op volgt.</summary>
    private readonly Dictionary<int, bool> _alarmStand = new();

    /// <summary>BUG #117: past de wisselstand toe die net de kortsluiting heeft weggehaald (alleen tot de volgende alarmcontrole).</summary>
    internal Configuratie MetAlarmStand(Configuratie c)
    {
        foreach (var kv in _alarmStand)
        {
            if (c.Bevat(kv.Key) == kv.Value) continue;
            c = c.MetStand(kv.Key, kv.Value);
            _log.Info($"Wissel {kv.Key} blijft {(kv.Value ? "afbuigend" : "rechtdoor")} staan voor de volgende stap: dat haalde de kortsluiting weg.");
        }
        return c;
    }

    /// <summary>BUG #109: adressen van wissels die de kaart bij een van de bezette melders noemt (als melder voor het punt, of als tak erna). De loc
    /// staat er mogelijk met een deel op of tegen de tong aan: zulke wissels worden niet voor een proef omgezet.</summary>
    internal static HashSet<int> WisselsBijBezet(Baankaart kaart, IEnumerable<int> bezet)
    {
        var b = bezet.ToHashSet();
        return kaart.Wissels.Where(w => w.Waarnemingen.Any(o => b.Contains(o.NaMelder)
                                                              || (o.VolgendeBijRechtdoor is int r && b.Contains(r))
                                                              || (o.VolgendeBijAfbuigend is int a && b.Contains(a))))
                            .Select(w => w.Adres).ToHashSet();
    }

    /// <summary>BUG #108 (log 09:29): na de lus (melder 129 opnieuw bereikt) meldde Dinamo kortsluiting in blok 9 terwijl de loc stilstond. De
    /// verkenner gaf op ("zet geen wissels om") en de gebruiker moest zelf wissel 6 omzetten, waarna de kortsluiting weg was. Een kortsluiting
    /// bij een stilstaande loc betekent dat er stroom weg is en dat een wissel verkeerd staat; de verkenner schakelt nu zelf één voor één
    /// wissels om (eerst de wissels bij de bezette melders, dan de rest; nooit de kruiswissel-motoren 10 en 13) en kijkt of het alarm weggaat.
    /// Helpt het niet, dan gaat de wissel terug naar zijn oude stand. Geeft true als het alarm weg is.</summary>
    private async Task<bool> ProbeerAlarmWegTeKrijgen(string stap)
    {
        _alarmZoekenBezig++;   // BUG #273
        try { return await ProbeerAlarmWegTeKrijgenKern(stap); }
        finally { _alarmZoekenBezig--; }
    }

    /// <summary>BUG #301 (autolog 25-10 09:06:31-09:09:58): tijdens een blijvende kortsluiting in blok 8 en 9 meldde Dinamo geen enkel wisselcommando terug (wissel 6 t/m 14 en 1/2, elk twee keer):
    /// de commando's werden niet uitgevoerd en 3,5 minuut proberen hielp niets; Marco loste het daarna met de hand op (wissel 10). Blijven bij deze kortsluiting drie commando's achter elkaar
    /// zonder terugmelding, dan stopt het proberen en wordt meteen om hulp gevraagd.</summary>
    private int _zonderTerugmeldingBijAlarm;
    private bool _probeerZinloosGemeld;

    private async Task<bool> ProbeerAlarmWegTeKrijgenKern(string stap)
    {
        _zonderTerugmeldingBijAlarm = 0; _probeerZinloosGemeld = false;   // BUG #301
        var bezet = _monitor.Bezet.ToHashSet();
        // BUG #109 (Marco 12:03: "wissel 9 werd geschakeld terwijl de loc er bovenop stond"): wissels die bij een bezette melder liggen (de kaart
        // noemt die melder bij die wissel) worden nooit zonder toestemming geprobeerd: de loc staat er met een deel op of tegen de tong aan.
        var bijLoc = WisselsBijBezet(_kaart, bezet);
        var alle = _ins.WisselAdressen().Where(a => !Kruis.IsMotor(a) && !IsVolger(a)).ToList();   // BUG #259

        // BUG #169 (Marco 16-10 18:35): een wissel die van de SAMENGEVOEGDE kant (puntzijde) bereden wordt, kan een kortsluiting niet met zijn stand
        // veroorzaken en omzetten helpt dus niet; een wissel die vanaf een van de GESPLITSTE takken richting de samenvoeging bereden wordt, kan dat
        // wel (verkeerde stand = open gereden) en omzetten heft de kortsluiting dan op. Eerste stap: precies die wissels, niet alles blind proberen.
        var ctx = _laatsteKortsluitContext;
        var (achter, kop) = ctx is { } c0 && _monitor.Bezet.Contains(c0.Melder) ? WisselRollen(_kaart, c0.Melder, c0.Richting, _laatsteKortsluitReeks) : (new List<int>(), new HashSet<int>());
        achter = achter.Where(a => alle.Contains(a)).ToList();
        // BUG #231 (autolog 18-10 13:43:29-13:45:25): de loc kwam via de keerlus 10 -> 129 terug op melder 129 en kreeg kortsluiting. De kaart wist alleen dat 14 -> 129 met wissel 6
        // afbuigend kortsluiting geeft (wissel 6 rechtdoor hoort bij de tak van 14). De routine probeerde 1 min 45 s lang 14 andere adressen voordat wissel 6 (de oplossing) aan de beurt kwam.
        // Komt de loc vanaf een ANDERE melder op zo'n melder aan, dan rijdt hij van de andere tak richting de samenvoeging: die wissel gaat dan voorop (BUG #169).
        foreach (var a in AndereTakVerdachten(_kaart, bezet, LaatsteReeksBijLoc(bezet)))
            if (alle.Contains(a) && !achter.Contains(a))
            {
                achter.Insert(0, a);
                _log.Info($"Wissel {a}: de kaart weet dat afbuigend hier vanaf een andere ingang kortsluiting geeft; de loc kwam nu via de andere tak, dus wissel {a} is de eerste verdachte (BUG #231).");
            }
        var kopAdressen = kop.Where(a => !achter.Contains(a) && alle.Contains(a)).ToHashSet();
        // BUG #222: de proefwissel die de loc net van de puntzijde nam (wissel 5 bij 14 -> 129) staat nog niet in de kaart, maar hoort ook bij de 'samengevoegde kant'.
        if (PuntzijdeProefNu() is int pzw && alle.Contains(pzw)) { achter.Remove(pzw); kopAdressen.Add(pzw); }
        if (achter.Count > 0 || kopAdressen.Count > 0)
            // BUG #265 (autolog 21-10 08:15:40): bij een lege lijst stond er "wissel - wordt van een tak ..."; nu alleen het deel dat iets zegt
            _log.Info("Eerste stap bij de kortsluiting: " + string.Join("; ", new[]
            {
                achter.Count > 0 ? $"wissel {string.Join(", ", achter)} wordt van een tak richting de samenvoeging bereden (omzetten kan de kortsluiting opheffen)" : null,
                kopAdressen.Count > 0 ? $"wissel {string.Join(", ", kopAdressen.OrderBy(a => a))} wordt van de samengevoegde kant bereden en wordt niet omgezet (dat helpt daar niet)" : null
            }.Where(x => x is not null)) + ".");

        if (bijLoc.Count > 0) _log.Info($"Niet zonder toestemming geprobeerd: wissel {string.Join(", ", bijLoc.OrderBy(a => a))} (ligt bij een bezette melder, de loc staat er mogelijk op).");
        var alarmBlokkenVoor = HuidigeAlarmBlokken();   // BUG #174: de oplossing hoort alleen bij een kortsluitpunt met hetzelfde alarmblok
        _log.Waarschuwing($"Kortsluiting in blok {string.Join(" en ", HuidigeAlarmBlokken())} blijft terwijl de loc stilstaat ({stap}). De verkenner probeert zelf wissels om te zetten, behalve die bij een bezette melder ({string.Join(" en ", bezet.OrderBy(m => m))}).");

        // BUG #258: standen die de kaart voor deze stap al bewezen heeft blijven staan (wissel 5 en 6 bij 14 -> 129)
        var bewezen = BewezenStandenNu(bezet);
        if (bewezen.Count > 0)
            _log.Info($"Wissel {string.Join(", ", bewezen.Keys.OrderBy(a => a).Select(a => $"{a} ({(bewezen[a] ? "afbuigend" : "rechtdoor")})"))} wordt niet geprobeerd: die stand is voor deze stap al bewezen (BUG #258).");

        async Task<bool> Probeer(int a, string toelichting)
        {
            if (bewezen.ContainsKey(a)) return false;   // BUG #258
            if (_zonderTerugmeldingBijAlarm >= 3 && _hw is IWisselBevestiging)
            {
                if (!_probeerZinloosGemeld)
                {
                    _probeerZinloosGemeld = true;
                    _log.Waarschuwing($"Dinamo meldt tijdens deze kortsluiting de wisselcommando's niet terug (de laatste {_zonderTerugmeldingBijAlarm}): ze worden waarschijnlijk niet uitgevoerd. Verder proberen heeft geen zin; de verkenner vraagt nu om hulp (BUG #301).");
                }
                return false;
            }
            bool oud = _wisselStand.TryGetValue(a, out var nu) && nu;
            var herstelVoor = _uitvalHersteldOm;
            await ZetWissel(a, !oud, forceer: true);
            if (_alarmWegVoorEigenTerugmelding && HuidigeAlarmBlokken().Count == 0)
            {
                _log.Waarschuwing($"De kortsluiting ging weg voordat Dinamo wissel {a} terugmeldde: niet door deze wissel (Dinamo zette het alarm zelf uit). Niet vastgelegd; wissel {a} gaat terug (BUG #308, nog niet getest op de baan).");
                _kortsluitingGemeld = false;
                await ZetWissel(a, oud, forceer: true);
                return true;
            }
            var eind = _klok.Nu + TimeSpan.FromSeconds(2.5);
            while (HuidigeAlarmBlokken().Count > 0 && _klok.Nu < eind) await Wacht(TimeSpan.FromMilliseconds(300));
            if (HuidigeAlarmBlokken().Count == 0 && _uitvalHersteldOm != herstelVoor)
            {
                // BUG #273: tussendoor zijn alle wissels opnieuw gestuurd (Dinamo-herstel): het wegvallen van het alarm zegt niets over deze wissel
                _log.Waarschuwing($"De kortsluiting is weg, maar tijdens het omzetten van wissel {a} was er een Dinamo-herstel (andere wissels geschakeld en alle wissels opnieuw gestuurd). Dat wordt niet aan wissel {a} toegeschreven en niet vastgelegd (BUG #273).");
                _kortsluitingGemeld = false;
                return true;
            }
            if (HuidigeAlarmBlokken().Count == 0)
            {
                _log.Vondst($"De kortsluiting ging weg nadat wissel {a}{toelichting} naar {(oud ? "rechtdoor" : "afbuigend")} is geschakeld ({stap}). De wissel blijft zo staan. Let op: dit is nog geen bewijs, Dinamo zet het alarm ook zelf soms even uit; een rit zonder kortsluiting over dezelfde overgang bevestigt het (BUG #249).");
                _kortsluitingGemeld = false;
                _alarmStand[a] = !oud;
                RegistreerAlarmOplossing(a, !oud, bezet, alarmBlokkenVoor);
                // BUG #194 (Marco 17-10 16:48, log 16:01): wissel 1 haalde het alarm weg terwijl wissel 2 door het herstel op rechtdoor stond; de bewezen oplossing is 1 EN 2 afbuigend.
                // De proefwissel gaat dus terug naar afbuigend (blijft het alarm weg), en de stand blijft staan zolang de loc nog op deze melders staat.
                if (!oud)
                {
                    _klemOplossing[a] = (KlemMelders(bezet), _klok.Nu);   // BUG #240
                    foreach (var (pw, losBij) in _losgemaakteProefWissels.ToList())
                    {
                        if (pw == a || !(_wisselStand.TryGetValue(pw, out var pst) && !pst)) continue;
                        // BUG #219 (log 18-10 12:08:43): wissel 2 was om 12:03:22 bij melder 5/16 losgemaakt (afbuigend = bekende kortsluiting voor 16 -> 5) en stond nog in
                        // de lijst. Bij de kortsluiting op 24/129 werd hij 'weer' afbuigend gezet. Alleen een wissel die op dezelfde plek is losgemaakt hoort bij deze kortsluiting.
                        if (!PartnerHoortBijPlek(losBij, bezet))
                        {
                            _log.Info($"Wissel {pw} gaat niet terug naar afbuigend: die werd losgemaakt toen de loc op melder {Lijst(losBij)} stond, niet bij deze kortsluiting (melder {Lijst(bezet)}).");
                            continue;
                        }
                        await ZetWissel(pw, true, forceer: true);
                        var eind2 = _klok.Nu + TimeSpan.FromSeconds(2.5);
                        while (_klok.Nu < eind2) await Wacht(TimeSpan.FromMilliseconds(300));
                        if (HuidigeAlarmBlokken().Count == 0)
                        {
                            _log.Vondst($"Wissel {pw} is teruggezet op afbuigend (de stand van vóór het losmaken van de loc) naast wissel {a}: het alarm bleef weg. Dit is geen bewijs dat wissel {pw} iets oplost: het alarm was al weg door het omzetten van wissel {a}. De wissels blijven zo staan tot de loc de melder verlaten heeft (BUG #249, nog niet getest op de baan).");
                            _klemOplossing[pw] = (KlemMelders(bezet), _klok.Nu);   // BUG #240
                            _alarmStand[pw] = true;
                        }
                        else
                        {
                            await ZetWissel(pw, false, forceer: true);
                            _log.Waarschuwing($"Wissel {pw} samen met wissel {a} afbuigend gaf direct weer alarm: wissel {pw} blijft rechtdoor.");
                        }
                    }
                    _losgemaakteProefWissels.Clear();
                }
                return true;
            }
            await ZetWissel(a, oud, forceer: true);
            return false;
        }

        // BUG #198 (log 17-10 19:52-19:56): een kortsluiting bij de bezette melders 24 en 129 werd met wissels 1, 2 en 14 'opgelost' (alle in numerieke volgorde geprobeerd, de kortsluiting verdween
        // steeds vanzelf of tijdelijk), terwijl kortsluitpunt #3 al bewees dat wissel 5 afbuigend de oplossing is. Eerst de oplossing die de kaart zelf voor deze bezette melders kent.
        // BUG #261: eerst een wissel die voor de laatste stap een bekende verboden ingang is (ook als hij bij de loc ligt: hij is open gereden, terugzetten is het herstel, BUG #169)
        foreach (var a in BekendeVerbodenVoorLaatsteStap().Where(alle.Contains))
        {
            _log.Info($"Eerst wissel {a}: de kaart weet dat de laatste stap met wissel {a} afbuigend kortsluiting geeft (BUG #261).");
            if (await Probeer(a, " (bekende verboden ingang)")) return true;
        }
        var stNu = new Configuratie(_wisselStand.Where(w => w.Value).Select(w => w.Key).ToArray());
        foreach (var kpOpl in _kaart.Kortsluitpunten.Where(k => k.Opgelost && k.OpgelostMetAfbuigend == true && k.OpgelostDoorAdres is int && !Kruis.IsMotor(k.OpgelostDoorAdres!.Value)
                                                                && (bezet.Contains(k.NaMelder) || k.TweedeMelders.Any(bezet.Contains)) && k.Configuratie.Afbuigend.All(stNu.Bevat)).OrderBy(k => k.Id).ToList())
        {
            int oa = kpOpl.OpgelostDoorAdres!.Value;
            if (stNu.Bevat(oa) || bijLoc.Contains(oa) || !alle.Contains(oa)) continue;
            _log.Info($"Eerst de bekende oplossing van kortsluitpunt #{kpOpl.Id}: wissel {oa} afbuigend.");
            if (await Probeer(oa, $" (bekende oplossing van kortsluitpunt #{kpOpl.Id})")) return true;
        }

        // stap 1: de wissels die van achteren bereden worden en niet bij de loc liggen
        foreach (var a in achter.Where(a => !bijLoc.Contains(a)))
            if (await Probeer(a, " (van een tak richting de samenvoeging bereden)")) return true;

        // stap 2: de wissels die van achteren bereden worden en bij de loc liggen, plus de kruismotoren als de loc op de kruiswissel staat: met toestemming
        var verdachtEerst = achter.Where(a => bijLoc.Contains(a) && !bewezen.ContainsKey(a)).ToList();   // BUG #258
        if (Kruis.OpKruis(bezet))
            foreach (var m in KruisMotorenVolgorde(bezet)) if (!verdachtEerst.Contains(m)) verdachtEerst.Add(m);
        var gevraagd = new HashSet<int>();
        // BUG #305 (Marco 25-10 10:05: zelflerend, eerst zelf oplossen, pas na 10 minuten de gebruiker): niet meer om toestemming vragen. Komt de loc van een tak richting de
        // samenvoeging, dan kan omzetten de kortsluiting juist opheffen (BUG #169).
        if (verdachtEerst.Count > 0)
        {
            _log.Info($"Wissel {string.Join(" en ", verdachtEerst)} ligt bij de loc (melder {string.Join(" en ", bezet.OrderBy(x => x))}), die van een tak richting de samenvoeging rijdt: de verkenner probeert ze zelf één voor één (zonder te vragen, BUG #305).");
            foreach (var a in verdachtEerst) gevraagd.Add(a);
            foreach (var a in verdachtEerst)
                if (await Probeer(a, " (ligt bij de loc, zelf geprobeerd)")) return true;
        }

        // stap 3: de overige wissels, behalve die van de samengevoegde kant (omzetten helpt daar niet) en die bij de loc
        // BUG #198: wissels waarvan de kaart de plek kent en die ver van de bezette melders liggen komen als laatste; de wissels bij de loc (buren van de bezette melders) eerst.
        var burenKaart = Buren(_kaart);
        var dichtbij = bezet.ToHashSet();
        foreach (var bm in bezet) if (burenKaart.TryGetValue(bm, out var bb)) foreach (var n in bb) dichtbij.Add(n);
        int Afstand(int ad)
        {
            var wm = _kaart.Wissels.Where(w => w.Adres == ad).SelectMany(w => w.Waarnemingen)
                .SelectMany(o => new[] { (int?)o.NaMelder, o.Via, o.VolgendeBijRechtdoor, o.VolgendeBijAfbuigend }).Where(x => x is not null).Select(x => x!.Value).ToList();
            return wm.Count == 0 ? 1 : wm.Any(dichtbij.Contains) ? 0 : 2;
        }
        // BUG #253 (Marco 20-10 15:20: "je hebt de aanliggende sectie al 20 keer gevonden en weet nog niet welke wissel je moet schakelen"; log 15:21-15:24): de bekende oplossing van het
        // kortsluitpunt voor deze bezette melders (wissel 1 met wissel 2 afbuigend, kortsluitpunt #3) stond al; het alarm bleef toch. Dan ligt het niet aan een wissel in een andere sectie:
        // de routine zette 2 minuten lang alle 14 wissels om (42 wisselcommando's), twee keer achter elkaar. Staat de bekende oplossing al, dan komen alleen nog wissels in de
        // aanliggende sectie (Afstand 0) aan de beurt en geen blinde rondgang langs de rest.
        bool bekendeOplossingStaat = _kaart.Kortsluitpunten.Any(k => k.Opgelost && !k.Voorlopig && k.OpgelostMetAfbuigend == true && k.OpgelostDoorAdres is int oa2 && stNu.Bevat(oa2)
                                                                      && (bezet.Contains(k.NaMelder) || k.TweedeMelders.Any(bezet.Contains)) && k.Configuratie.Afbuigend.All(stNu.Bevat));
        if (bekendeOplossingStaat)
            _log.Info("De bekende oplossing van een kortsluitpunt bij deze melders staat al, maar het alarm blijft: alleen nog wissels in de aanliggende sectie worden geprobeerd, geen blinde rondgang langs alle wissels (BUG #253). Controleer op de baan of er iets op de melder(s) staat of ligt.");
        foreach (var a in alle.Where(a => !bijLoc.Contains(a) && !achter.Contains(a) && !kopAdressen.Contains(a) && (!bekendeOplossingStaat || Afstand(a) == 0)).OrderBy(Afstand).ThenBy(a => a))
            if (await Probeer(a, "")) return true;

        // BUG #147 (Marco 18:41: "nu weer wissel 14"; ook wissel 2 bij melder 5 om 17:27): de wissel die de kortsluiting veroorzaakt ligt bijna altijd bij de
        // melder waar de loc staat en werd dus nooit geprobeerd. Nooit zonder toestemming (#109): één keer vragen, de meest verdachte eerst.
        var verdacht = bijLoc.Where(a => !Kruis.IsMotor(a) && _ins.WisselAdressen().Contains(a) && !gevraagd.Contains(a) && !kopAdressen.Contains(a))
            .OrderByDescending(a => _kaart.Wissel(a).Waarnemingen.Any(o => o.Soort == WaarnemingSoort.KortsluitingBijOmzetten && bezet.Contains(o.NaMelder)))
            .ThenBy(a => a).ToList();
        if (verdacht.Count > 0)
        {
            _log.Info($"Geen enkele andere wissel haalt de kortsluiting weg; wissel {string.Join(" en ", verdacht)} bij de loc wordt nu zelf één voor één geprobeerd (zonder te vragen, BUG #305).");
            foreach (var a in verdacht)
                if (await Probeer(a, " (ligt bij de loc, zelf geprobeerd)")) return true;
        }
        // BUG #181: laatste redmiddel. De rollen (achter/kop) komen uit de kaart en kunnen ontbreken of net andersom liggen (sim: loc van 129 naar zijspoor 10, wissel 5 en 6 stonden als
        // 'samengevoegde kant' aangemerkt maar wissel 5 stond tegen de loc in). Dan blijft de loc voor altijd vastzitten. Nu worden ook die wissels (met toestemming, bij de loc) nog
        // één voor één geprobeerd voordat de verkenner opgeeft.
        var laatste = bijLoc.Where(a => !Kruis.IsMotor(a) && _ins.WisselAdressen().Contains(a) && kopAdressen.Contains(a)).OrderBy(a => a).ToList();
        if (laatste.Count > 0)
        {
            _log.Info($"Laatste redmiddel: wissel {string.Join(" en ", laatste)} lijkt van de samengevoegde kant bereden (omzetten helpt dan niet), maar dat is uit de kaart afgeleid; de verkenner probeert ze toch zelf (zonder te vragen, BUG #305).");
            foreach (var a in laatste)
                if (await Probeer(a, " (laatste redmiddel, ligt bij de loc, zelf geprobeerd)")) return true;
        }
        _log.Waarschuwing("Geen enkele wissel haalde de kortsluiting weg.");
        return false;
    }

    /// <summary>BUG #258 (Marco 20-10 18:23: "nu zet je wissel 5 verkeerd en laat je wissel 6 als eerste schakelen; je wist al hoe je van 129 naar 14 moest rijden, zowel 5 als 6 moesten
    /// afbuigend staan (overloopwisselset)"; autolog 18:08:17-18:08:41 en 18:10:28): bij een kortsluiting op 14/129 zette het herstel wissel 5 terug naar rechtdoor en probeerde het wissel 6
    /// als eerste, terwijl de kaart voor de stap 14 -> 129 al weet: wissel 5 afbuigend (splitsing) en wissel 6 niet Dinamo-afbuigend (kortsluiting, ingang 14). Dit zijn de standen die de
    /// kaart voor de stappen van <paramref name="reeks"/> rond de bezette melders bewezen heeft (adres -> Dinamo-stand afbuigend). Tegenstrijdige adressen vallen weg.</summary>
    internal static Dictionary<int, bool> BewezenStandenBijLoc(Baankaart kaart, IReadOnlyList<int> reeks, Richting r, IReadOnlyCollection<int> bezet)
    {
        var res = new Dictionary<int, bool>(); var strijdig = new HashSet<int>();
        void Voeg(int a, bool afb) { if (res.TryGetValue(a, out var b) && b != afb) strijdig.Add(a); res[a] = afb; }
        var verboden = kaart.VerbodenIngangen();
        // BUG #279 (autolog 21-10 14:52:30): na 29, 21, 24, 129, 10, 9, 1, 12, 11, 4 (achteruit) stond de loc op 4 en 129; de stap 24 -> 129 van het BEGIN van de rit (wissel 9 rechtdoor) gold
        // als bewezen voor de plek van de loc, en wissel 9 (de oplossing, afbuigend voor 4 -> 129) werd niet geprobeerd. Alleen de laatste stappen tellen: vanaf het eind zolang de
        // aankomstmelder van de stap bezet is.
        for (int i = reeks.Count - 2; i >= 0; i--)
        {
            int x = reeks[i], y = reeks[i + 1];
            if (!bezet.Contains(y)) break;
            foreach (var e in kaart.WisselEisenVoorStapDetail(x, y, r)) Voeg(e.Adres, e.Afbuigend);
            foreach (var v in verboden.Where(v => v.Via == x && v.Na == y && v.R == r)) Voeg(v.Adres, false);
        }
        foreach (var a in strijdig) res.Remove(a);
        return res;
    }

    /// <summary>BUG #258: de bewezen standen (zie <see cref="BewezenStandenBijLoc"/>) die nu ook zo staan; die worden bij een kortsluiting niet omgezet.</summary>
    private Dictionary<int, bool> BewezenStandenNu(IReadOnlyCollection<int> bezet)
    {
        var reeks = LaatsteReeksBijLoc(bezet);
        Richting? r = _laatsteKortsluitContext?.Richting ?? _kaart.Ritten.LastOrDefault()?.Richting;
        if (reeks.Count < 2 || r is not Richting rr) return new Dictionary<int, bool>();
        return BewezenStandenBijLoc(_kaart, reeks, rr, bezet).Where(kv => _wisselStand.TryGetValue(kv.Key, out var st) && st == kv.Value).ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    /// <summary>BUG #261: wissels die nu afbuigend staan en voor de laatste stap van de kortsluitrit een bekende verboden ingang zijn (via -> na, richting).</summary>
    private List<int> BekendeVerbodenVoorLaatsteStap()
    {
        // BUG #261 (autolog 20-10 18:45:10, 18:47:58, 18:50:23): de kortsluiting werd gemeld vóór de terugweg (context 24 achteruit), terwijl de stap die hem veroorzaakte 14 -> 24 vooruit
        // was (de laatste rit). Daarom ook de laatste gereden rit die op een bezette melder eindigt, met zijn eigen richting.
        _monitor.Bijwerken();
        var bezet = _monitor.Bezet;
        var stappen = new List<(int U, int X, Richting R)>();
        if (_laatsteKortsluitReeks.Count >= 2 && _laatsteKortsluitContext?.Richting is Richting rk) stappen.Add((_laatsteKortsluitReeks[^2], _laatsteKortsluitReeks[^1], rk));
        if (_kaart.Ritten.LastOrDefault() is { } rit && rit.Reeks.Count >= 2 && (bezet.Count == 0 || bezet.Contains(rit.Reeks[^1]))) stappen.Add((rit.Reeks[^2], rit.Reeks[^1], rit.Richting));
        var verboden = _kaart.VerbodenIngangen();
        return stappen.SelectMany(st => verboden.Where(v => v.Via == st.U && v.Na == st.X && v.R == st.R))
                      .Where(v => _wisselStand.TryGetValue(v.Adres, out var w) && w && !IsVolger(v.Adres))
                      .Select(v => v.Adres).Distinct().OrderBy(x => x).ToList();
    }

    /// <summary>BUG #231: de laatste gereden melderreeks die eindigt op een melder waar de loc nu staat (eerst die van de laatste kortsluiting, anders de laatste rit).</summary>
    private IReadOnlyList<int> LaatsteReeksBijLoc(IReadOnlyCollection<int> bezet)
    {
        if (_laatsteKortsluitReeks.Count >= 2 && bezet.Contains(_laatsteKortsluitReeks[^1])) return _laatsteKortsluitReeks;
        var rit = _kaart.Ritten.LastOrDefault();
        return rit is not null && rit.Reeks.Count >= 2 && bezet.Contains(rit.Reeks[^1]) ? rit.Reeks : Array.Empty<int>();
    }

    /// <summary>BUG #231: wissels met een waarneming 'afbuigend geeft kortsluiting na melder X vanaf ingang V', terwijl de loc nu op X staat en er vanaf een andere melder U (niet V)
    /// aankwam (laatste stap van <paramref name="reeks"/>). Die wissel wordt dan van de andere tak richting de samenvoeging bereden: omzetten kan de kortsluiting opheffen.</summary>
    internal static List<int> AndereTakVerdachten(Baankaart kaart, IReadOnlyCollection<int> bezet, IReadOnlyList<int> reeks)
    {
        if (reeks.Count < 2) return new List<int>();
        int x = reeks[^1], u = reeks[^2];
        if (!bezet.Contains(x)) return new List<int>();
        return kaart.Wissels.Where(w => w.Waarnemingen.Any(o => o.Soort == WaarnemingSoort.KortsluitingBijOmzetten && o.NaMelder == x && o.Via is int v && v != u))
                            .Select(w => w.Adres).OrderBy(a => a).ToList();
    }

    /// <summary>BUG #169: de kruismotoren in de volgorde waarin ze verdacht zijn voor een loc die op de kruiswissel staat: eerst de motor waarvan de huidige stand
    /// bij de ingang waarlangs de loc binnenkwam nooit gezien is (de kaart weet dan dat die combinatie niet werkt), daarna de rest.</summary>
    private List<int> KruisMotorenVolgorde(IEnumerable<int> bezet)
    {
        var motoren = Kruis.Motoren.OrderBy(m => m).ToList();
        int? midden = Kruis.Middens.FirstOrDefault(bezet.Contains) is int mm && mm != 0 ? mm : null;
        int via = _laatsteKortsluitReeks.Count >= 2 && midden is int md && _laatsteKortsluitReeks[^1] == md ? _laatsteKortsluitReeks[^2] : 0;
        if (midden is not int mid || via == 0) return motoren;
        var gezien = KruisBewijs.GezienBijIngang(_kaart, Kruis, mid, via);
        if (gezien.Count == 0) return motoren;
        bool Verdacht(int m) { bool nu = _wisselStand.TryGetValue(m, out var st) && st; return !gezien.Any(g => g.Bevat(m) == nu); }
        return motoren.OrderByDescending(Verdacht).ThenBy(m => m).ToList();
    }

    /// <summary>BUG #169 (Marco 16-10 18:35): welke wissels worden door een loc die op <paramref name="x"/> staat en in richting <paramref name="r"/> verder wil van
    /// een TAK richting de samenvoeging bereden (<c>Achter</c>: de stand kan kortsluiting geven, omzetten kan helpen) en welke van de SAMENGEVOEGDE kant
    /// (<c>Kop</c>: omzetten helpt niet)? Uit de splitsingen van de kaart (puntzijde na melder N in richting D: rechtdoor naar R, afbuigend naar A):
    /// staat de loc op R of A en rijdt hij tegen D in, dan gaat hij van achteren op die wissel af; staat hij op N en rijdt hij in D, dan van voren.
    /// Ook de laatste stap van <paramref name="reeks"/> telt mee (de loc kan de wissel net voorbij zijn gereden). Wat zowel van achteren als van voren
    /// voorkomt, geldt als van achteren.</summary>
    internal static (List<int> Achter, HashSet<int> Kop) WisselRollen(Baankaart kaart, int x, Richting r, IReadOnlyList<int>? reeks)
    {
        var achter = new List<int>(); var kop = new HashSet<int>();
        foreach (var w in kaart.Wissels)
            foreach (var o in w.Waarnemingen.Where(q => q.Soort == WaarnemingSoort.Splitsing))
            {
                if (o.VolgendeBijRechtdoor is int rd && o.VolgendeBijAfbuigend is int af && rd == af) continue;
                if (r == o.Richting.Om() && (o.VolgendeBijRechtdoor == x || o.VolgendeBijAfbuigend == x)) { if (!achter.Contains(w.Adres)) achter.Add(w.Adres); }
                else if (r == o.Richting && o.NaMelder == x) kop.Add(w.Adres);
            }
        // BUG #170: een wissel waarvan de kaart al weet dat afbuigend op precies deze ingang kortsluiting geeft (loc staat op de melder na de ingang, zelfde
        // rijrichting) is de eerste verdachte. Wijzen drie of meer wissels naar dezelfde ingang, dan is dat een gedeelde oorzaak (bv. een kruismotor) of ruis
        // en zegt het niets over een enkele wissel.
        var bekend = kaart.VerbodenIngangen().Where(v => v.Na == x && v.R == r).ToList();
        if (bekend.Select(v => v.Adres).Distinct().Count() <= 2)
            foreach (var v in bekend)
                if (!achter.Contains(v.Adres)) achter.Insert(0, v.Adres);
        if (reeks is { Count: >= 2 })
            foreach (var e in kaart.WisselEisenVoorStapDetail(reeks[^2], reeks[^1], r))
            {
                if (e.Achter) { if (!achter.Contains(e.Adres)) achter.Add(e.Adres); }
                else kop.Add(e.Adres);
            }
        return (achter, kop);
    }

    /// <summary>BUG #223: hoort een alarmoplossing (loc op <paramref name="melders"/>, alarm in <paramref name="blokken"/>) bij kortsluitpunt <paramref name="kp"/>? Alleen voor analyse en tests:
    /// automatisch toepassen op een later vastgelegd punt gaf in de simulatie (start 5) valse oplossingen, ook als voorlopige oplossing (BUG #204).</summary>
    internal static bool AlarmOplossingPastBij(Kortsluitpunt kp, IReadOnlyCollection<int> melders, IReadOnlyCollection<int> blokken)
        => (melders.Contains(kp.NaMelder) || kp.TweedeMelders.Any(melders.Contains))
           && (blokken.Count == 0 || kp.AlarmBlokken.Count == 0 || kp.AlarmBlokken.Any(blokken.Contains));

    /// <summary>BUG #144 (Marco 16:29, log 16:23-16:26): wissel 9 afbuigend haalde de kortsluiting na melder 129 (rit 4 -> 129) weg, maar dat werd alleen
    /// voor de eerstvolgende stap onthouden en niet bij het kortsluitpunt vastgelegd: #2 bleef 'open', de wissel stond nergens in het rapport en de
    /// volgende rit 4 -> 129 zou weer met wissel 9 rechtdoor beginnen (opnieuw kortsluiting, opnieuw 50 s wissels proberen). Nu wordt de wissel die het
    /// alarm wegnam als oplossing vastgelegd bij het nieuwste nog niet opgeloste kortsluitpunt waarvan de melder bezet is (de loc staat er nog).
    /// Een wissel die al in de stand van het punt zit, een kruismotor die daar niets kan oplossen of een wissel die op rechtdoor werd gezet telt niet.</summary>
    private void RegistreerAlarmOplossing(int adres, bool afbuigend, IEnumerable<int> bezet, IReadOnlyCollection<int>? alarmBlokken = null)
    {
        var b = bezet.ToHashSet();
        // BUG #310 (autolog 25-10 11:29:59-11:30:50): de kortsluiting kwam achteruit na 129 (loc op 129/4, keerlus), maar de 'oplossing' werd bij kortsluitpunt #1 (na 24, VOORUIT,
        // met 129 als tweede melder) vastgelegd. Is de rijrichting van deze kortsluiting bekend, dan alleen een kortsluitpunt met dezelfde richting.
        Richting? rk310 = _laatsteKortsluitContext?.Richting;
        var kp = _kaart.Kortsluitpunten.Where(k => k.Soort == KortsluitpuntSoort.Kortsluiting && !k.Opgelost && !k.Opgegeven
                                                   && (rk310 is null || k.Richting == rk310)
                                                   && (b.Contains(k.NaMelder) || k.TweedeMelders.Any(b.Contains))
                                                   && (alarmBlokken is not { Count: > 0 } || k.AlarmBlokken.Count == 0 || k.AlarmBlokken.Any(alarmBlokken.Contains)))
                                       .OrderByDescending(k => k.Id).FirstOrDefault();
        if (kp is null)
        {
            return;
        }
        if (!_kaart.RegistreerHandmatigeOplossing(kp, adres, afbuigend, Kruis)) return;
        kp.Voorlopig = true;   // BUG #204/#211/#217: alleen uit het alarm afgeleid, ritten bevestigen of weerleggen dit
        _log.Vondst($"Kortsluitpunt #{kp.Id} (na melder {kp.NaMelder}, {kp.Richting.Tekst()}, {kp.Configuratie}) is voorlopig opgelost door wissel {adres} op afbuigend: " +
                    "het alarm verdween nadat de verkenner die wissel omzette. Dit wordt voortaan in deze verkenning toegepast tot ritten het bevestigen of weerleggen (nog niet getest op de baan).");
        BewaarNu();
    }

    /// <summary>BUG #93 (log 15:20): na kortsluitpunt #2 (wissel 5 afbuigend na melder 14) lukte dezelfde rit meteen daarna, zonder dat er iets
    /// veranderd was (melder 129 bereikt). Zo'n kortsluiting was tijdelijk: het punt blijft in het rapport, maar de verkenner gaat er geen
    /// wissels voor proberen (dat kost alleen nieuwe kortsluitingen).</summary>
    private void MarkeerTijdelijkeKortsluiting(int start, Richting r, Configuratie c, RitResultaat uitkomst)
    {
        if (uitkomst.Einde == RitEinde.Kortsluiting || uitkomst.Reeks.Count < 2 || uitkomst.Reeks[0] != start) return;
        foreach (var kp in _kaart.Kortsluitpunten.Where(k => !k.Opgelost && !k.Opgegeven && !k.Tijdelijk && k.Soort == KortsluitpuntSoort.Kortsluiting
                                                             && k.NaMelder == start && k.Richting == r && k.Configuratie.Equals(c)))
        {
            kp.Tijdelijk = true;
            _log.Vondst($"Kortsluitpunt #{kp.Id} (na melder {start}, {r.Tekst()}, {kp.Configuratie}) bleek tijdelijk: dezelfde rit lukte nu wel (melder {uitkomst.Reeks[1]}). " +
                        "Waarschijnlijk was een tong nog niet (helemaal) omgeschakeld; er worden geen wissels voor geprobeerd.");
        }
    }

    /// <summary>BUG #223: de afbuigende wissels op het moment dat de loc vertrok (null als de rit niet vertrok).</summary>
    private List<int>? _ritStartStand;

    private async Task<RitResultaat> Rit(int start, Richting r, RitDoel doel)
    {
        _ritStartStand = null;
        // BUG #248 (autolog 18-10 16:01:10-16:02:25): wissel 6 werd bij de proef van adres 6 (14 -> 129) losgemaakt. Twee ritten later (terugweg na de proef van adres 7) haalde de bekende
        // oplossing van kortsluitpunt #1 (wissel 10) een andere kortsluiting weg en ging wissel 6 'als partner' weer afbuigend (Dinamo; op de baan rechtdoor): de loc reed de keerlus in
        // naar melder 10. Een losgemaakte proefwissel hoort alleen bij de kortsluiting van zijn eigen proef en de terugweg daarna; een nieuwe verkenningsrit begint zonder.
        if (!doel.IsNavigatie && _losgemaakteProefWissels.Count > 0) _losgemaakteProefWissels.Clear();
        _ritMinDt = double.MaxValue; _ritMaxDt = 0;
        lock (_alarmSlot) _genegeerdeAlarmen.Clear();
        var uitkomst = await RitUitvoeren(start, r, doel);
        // BUG #164: een blokalarm dat binnenkwam terwijl de verkenner aan het stoppen was (en daarom genegeerd werd) is een echte kortsluiting als de rit eindigt
        // op een nieuwe plek of een onverwachte afwijking. Zo ontstaat hetzelfde kortsluitpunt als wanneer de melding een fractie eerder was gekomen.
        if (!doel.IsNavigatie && uitkomst.Einde is RitEinde.AfwijkingGevonden or RitEinde.Doodlopend)
        {
            List<int> genegeerd;
            lock (_alarmSlot) genegeerd = _genegeerdeAlarmen.Select(g => g.Blok).Distinct().OrderBy(b => b).ToList();
            if (genegeerd.Count > 0)
            {
                _log.Waarschuwing($"Dinamo meldde kortsluiting in blok {string.Join(" en ", genegeerd)} op het moment dat de verkenner na melder {uitkomst.Reeks[^1]} stopte. Dat is geen gevolg van de eigen stop maar een echte kortsluiting: de rit telt als kortsluiting na melder {uitkomst.Reeks[^1]} (BUG #164).");
                uitkomst = await Kortsluiting(uitkomst, "Dinamo meldde kortsluiting tijdens het stoppen", genegeerd);
            }
        }
        MarkeerTijdelijkeKortsluiting(start, r, doel.Configuratie, uitkomst);
        if (_monitor.Vastzitters is { Count: > 0 } vast306 && uitkomst.Reeks.Count > 1) { int eerste306 = uitkomst.Reeks[0]; uitkomst.Reeks.RemoveAll(x => vast306.Contains(x) && x != eerste306); }   // BUG #306
        _kaart.Ritten.Add(new GeredenRit
        {
            Nr = _kaart.Ritten.Count + 1, Tijd = DateTime.Now, Navigatie = doel.IsNavigatie, Start = start, Richting = r,
            // BUG #223 (rapport 18-10 12:21): bij ritten met een kortsluiting stond hier de stand NA het herstel (rit #3 'geen' i.p.v. 2, #7 '2, 5, 10' i.p.v. 5, #13 'geen' i.p.v. 10).
            Afbuigend = _ritStartStand ?? _wisselStand.Where(kv => kv.Value).Select(kv => kv.Key).OrderBy(a => a).ToList(),
            Reeks = uitkomst.Reeks.ToList(), Einde = uitkomst.Einde, Onbetrouwbaar = _onbetrouwbaar || _echo.Reeks > 0
        });
        foreach (var t in _kaart.ToetsVoorlopigeOplossingen()) _log.Vondst("Voorlopige oplossing getoetst (BUG #204): " + t);
        // BUG #74: waar kwam de loc vandaan? (Na een kortsluiting/herstel is dat niet meer te weten.)
        if (uitkomst.Einde == RitEinde.Kortsluiting) _ingang = null;
        else if (uitkomst.Reeks.Count >= 2) _ingang = (uitkomst.Reeks[^2], uitkomst.Reeks[^1], r);
        else if (_ingang is { } oud && oud.Melder != uitkomst.Reeks[^1]) _ingang = null;
        PasSnelheidAutomatischAan();
        try
        {
            if (Leerprofiel is { } lp && !_onbetrouwbaar && _echo.Reeks == 0) { lp.Leer(_kaart, RefSnelheid); ProfielBijgewerkt?.Invoke(lp); }
        }
        catch (Exception ex) { _log.Waarschuwing($"Leerprofiel bewaren mislukte: {ex.Message}"); }
        return uitkomst;
    }

    /// <summary>BUG #311 (autolog 25-10 12:16:39-12:18:34): de basisrit (wissel 2 afbuigend) gaf kortsluiting na 129 (13, 5, 12, 11, 4, 129); de proef met wissel 3 erbij gaf dezelfde
    /// kortsluiting, maar 129 was nog niet als nieuwe melder geteld (de loc stond er al deels in: "ook bezet: 129"). De proef heette daardoor 'kortsluiting na 4 met adres 3' en wissel 3
    /// (bestaat op de baan niet) werd een vondst. Zelfde kortsluiting = zelfde reeks, of de reeks van de basis zonder de laatste melder terwijl die laatste melder al bezet was.</summary>
    internal static bool ZelfdeKortsluitingAlsBasis(RitResultaat basis, RitResultaat proef)
    {
        if (basis.Einde != RitEinde.Kortsluiting || proef.Einde != RitEinde.Kortsluiting) return false;
        if (proef.Reeks.SequenceEqual(basis.Reeks)) return true;
        return basis.Reeks.Count >= 2 && proef.Reeks.Count == basis.Reeks.Count - 1 && proef.Reeks.SequenceEqual(basis.Reeks.Take(proef.Reeks.Count))
               && proef.OokBezetBijKortsluiting.Contains(basis.Reeks[^1]);
    }

    /// <summary>BUG #227: ligt <paramref name="m"/> volgens de kaart aan de andere kant van <paramref name="start"/> (start -> m minstens 3 keer in de andere richting gereden en
    /// nooit vaker in richting <paramref name="r"/>)?</summary>
    internal static bool IsAchterkantMelder(Baankaart kaart, int start, Richting r, int m)
    {
        int Telling(Richting rr) => kaart.Overgangen.Where(o => o.Van == start && o.Naar == m && o.Richting == rr).Sum(o => o.AantalKeer)
                                   + kaart.Overgangen.Where(o => o.Van == m && o.Naar == start && o.Richting == rr.Om()).Sum(o => o.AantalKeer);
        int anders = Telling(r.Om()), zelfde = Telling(r);
        return anders >= 3 && zelfde * 3 <= anders;
    }

    /// <summary>BUG #97: de eerste stap <paramref name="start"/> -> <paramref name="m"/> volgt de basisrit niet, is in deze richting met deze wisselstand
    /// nooit gezien, maar hoort wel bij de tegengestelde richting terwijl de basisrit wel bekend is: de loc staat andersom.</summary>
    internal static bool IsLocAndersom(Baankaart kaart, int start, Richting r, int m, List<int> basis, Configuratie cfg)
        => basis.Count >= 2 && basis[0] == start && basis[1] != m
           && kaart.BekendeVolgende(start, r.Om()).Contains(m)
           && kaart.BekendeVolgende(start, r).Contains(basis[1])
           && !kaart.Overgangen.Any(x => x.Van == start && x.Naar == m && x.Richting == r && x.GezienBijConfiguraties.Contains(cfg.Sleutel));

    private async Task<RitResultaat> RitUitvoeren(int start, Richting r, RitDoel doel)
    {
        var res = new RitResultaat { Richting = r };
        res.Reeks.Add(start);
        _monitor.Bijwerken();
        var cfgNu = doel.Configuratie;   // BUG #107: de werkelijke stand; kan tijdens de rit veranderen door ZetStaartWissels
        _ritDoel = doel;

        // BUG #71 (log 16:48): een navigatie begon vanaf melder 13 terwijl de loc na een herstel na kortsluiting op melder 8 stond.
        // De rit "13 -> 8" werd daarna als echte overgang in de kaart gezet (13 achteruit -> 8, bestaat niet) en zo ook de omgekeerde
        // (8 vooruit -> 13). Staat de loc niet op de startmelder maar op precies één andere melder, dan klopt de aanname niet:
        // doel al bereikt, op het verwachte pad verder vanaf de werkelijke plek, anders een navigatiefout (loc terugzetten).
        if (doel.IsNavigatie && _monitor.Bezet is { Count: 1 } werkelijkBezet && !werkelijkBezet.Contains(start))
        {
            int werkelijk = werkelijkBezet.First();
            if (werkelijk == doel.DoelMelder)
            {
                _log.Waarschuwing($"De loc staat niet op melder {start} maar al op het doel, melder {werkelijk}. Geen rit nodig en er wordt geen overgang {start} -> {werkelijk} vastgelegd.");
                res.Reeks[0] = werkelijk;
                res.Einde = RitEinde.DoelBereikt;
                return res;
            }
            if (doel.VerwachtPad is { } verwacht && verwacht.Contains(werkelijk))
            {
                _log.Waarschuwing($"De loc staat niet op melder {start} maar op melder {werkelijk} (op het verwachte pad): de rit begint daar.");
                start = werkelijk;
                res.Reeks[0] = werkelijk;
            }
            else
                throw new NavigatieFout($"De loc moest vanaf melder {start} naar melder {doel.DoelMelder} rijden, maar staat op melder {werkelijk} (niet op het verwachte pad).");
        }

        // BUG #73: ook een verkenningsrit (geen navigatie) begint alleen vanaf de startmelder. Staat de loc na een mislukte terugweg op een
        // andere melder, dan wordt die rit niet vanaf een verkeerd aangenomen plek gestart (dat gaf kortsluitingen en valse overgangen).
        if (!doel.IsNavigatie && _monitor.Bezet is { Count: 1 } elders && !elders.Contains(start))
            throw new NavigatieFout($"De rit moest beginnen op melder {start}, maar de loc staat op melder {elders.First()}.");

        if (doel.IsNavigatie && start == doel.DoelMelder)
        {
            res.Einde = RitEinde.DoelBereikt;
            return res;
        }

        // Bij Dinamo met een (nog) onbekend blok gaat StuurSnelheid vanzelf naar alle
        // blokken; de sectie wordt gekoppeld zodra de loc er helemaal in staat.

        await ControleerGeenAlarmVoorStapMet($"rit {r.Tekst()} vanaf melder {start}", start, r);
        // BUG #220 (log 18-10 12:09:56): tijdens het alarmherstel (stilstaande loc) viel melder 24 weg en kwam hij terug toen de stroom terugkwam. Die melding bleef in de monitor
        // staan en de rit telde 'melder 24 bezet' als eerste gereden melder (129 -> 24 achteruit; BUG #181 overschreef daardoor de juiste richting vooruit). Alles wat veranderde
        // terwijl de loc nog stilstond hoort niet bij de rit.
        var stilWijzigingen = _monitor.Bijwerken();
        if (stilWijzigingen.Count > 0)
            _log.Info($"Voor het vertrek veranderde de stand van melder {Lijst(stilWijzigingen.Select(w => w.Melder).Distinct())} terwijl de loc stilstond (stroom weg/terug na een kortsluiting): dat telt niet als gereden.");
        // BUG #182: een navigatie volgt de stand die de route nodig heeft (bv. wissel 6 afbuigend naar melder 10). De stand die eerder een
        // kortsluiting wegnam (laatste redmiddel: wissel 6 rechtdoor) mag die route niet overschrijven; komt de kortsluiting terug, dan volgt
        // opnieuw het herstel.
        if (!doel.IsNavigatie) cfgNu = MetAlarmStand(cfgNu);

        int snelheid = _ins.Verkensnelheid;
        _log.Rijden(doel.IsNavigatie
            ? $"Rijden {r.Tekst()} van melder {start} naar melder {doel.DoelMelder}."
            : $"Verkenningsrit {r.Tekst()} vanaf melder {start} ({doel.Configuratie}).");
        if (doel.StaartWissels && !doel.IsNavigatie) cfgNu = await ZetStaartWissels(start, r, cfgNu, doel);
        if (!doel.IsNavigatie && !doel.GeenKruisVooraf) cfgNu = await ZetKruisVooraf(start, r, cfgNu, doel);   // BUG #135
        _ritStartStand = _wisselStand.Where(kv => kv.Value).Select(kv => kv.Key).OrderBy(a => a).ToList();   // BUG #223
        await StuurSnelheid(r, snelheid);

        var laatsteWijziging = _klok.Nu;
        var tijdVorigeMelder = _klok.Nu;
        bool onderbroken = true;          // eerste overgang: vertrek vanuit stilstand, niet meten
        DateTime? leegSinds = null;
        int? afwijkingIndex = null;
        bool doelInzicht = false;
        bool kruipVoorDoel = false;
        bool blokGecontroleerd = false;
        bool wachttijdVerlengd = false;   // BUG #55: eenmalige bevestigingsronde voordat "doodlopend" geconcludeerd wordt
        bool vervolgHerhaald = false;     // BUG #64: bij een bekend vervolg eenmalig het rijcommando herhalen
        DateTime? heelInNieuweSectieSinds = null;
        // BUG #164 (hardwarelog 16-10 09:00:44): het rijcommando naar alle blokken duurt ~1,8 s en het alarm kwam al tijdens het versturen binnen; hier werd de vlag
        // daarna gewist en een alarm komt maar één keer binnen. De rit zag niets meer, wachtte 30 s en draaide daarna een blokproef met een loc die klem zat.
        // Een alarm dat nog loopt blijft dus gelden.
        _kortsluitingGemeld = HuidigeAlarmBlokken().Count > 0;

        int? achterkantGenegeerd = null;   // BUG #227
        bool kruipNaarEinde = false;       // BUG #284
        var uitgesteld268 = new List<MelderMonitor.Wijziging>();   // BUG #268
        DateTime? stapStopTot = null;   // BUG #175: tot wanneer de loc na de laatste melder van een stap-rit nog doorrijdt
        while (true)
        {
            await Wacht(Poll);

            if (_gepauzeerd)
            {
                await StopLoc();
                await WachtTotHervat();
                await StuurSnelheid(r, doelInzicht ? _ins.Kruipsnelheid : snelheid);
                laatsteWijziging = _klok.Nu;
                onderbroken = true;
            }

            var wijzigingen = _monitor.Bijwerken();
            var bezet = _monitor.Bezet;

            // ---- Kortsluiting? ----
            if ((_kortsluitingGemeld || HuidigeAlarmBlokken().Count > 0) && _klok.Nu >= _negeerKortsluitingTot)
            {
                // BUG #164: een navigatierit die direct bij de start in een kortsluiting loopt: de loc zit klem op een wissel (bijv. wissel 1 bij wissel 2). Eerst zelf
                // wissels proberen (met een proefrit per wissel) voordat de rit als mislukt geldt en de gebruiker de loc moet terugzetten.
                if (doel.IsNavigatie && res.Reeks.Count == 1 && !_klemProbeActief && _hw is IBlokAlarmBron)
                {
                    _log.Waarschuwing($"De loc kan niet weg van melder {start} ({r.Tekst()}): Dinamo meldt direct kortsluiting in blok {string.Join(" en ", HuidigeAlarmBlokken())}. De loc zit waarschijnlijk klem op een wissel.");
                    await VolledigeStop();
                    if (await ProbeerKlemWegTeKrijgen(start, r, doel, $"rit {r.Tekst()} vanaf melder {start}"))
                    {
                        _kortsluitingGemeld = false;
                        await StuurSnelheid(r, snelheid);
                        laatsteWijziging = _klok.Nu;
                        onderbroken = true;
                        continue;
                    }
                }
                return await Kortsluiting(res, "de centrale meldt een kortsluiting/fout");
            }
            if (stapStopTot is DateTime stopTijd && _klok.Nu >= stopTijd)
            {
                // BUG #179 (Marco 17-10 11:46: "doorrijden totdat de laatste melder niet meer actief is en de nieuwe melder alleen actief is"): na de minimale doorrijtijd
                // stopt de loc pas als hij helemaal in de nieuwe sectie staat, dus de melder erachter weer vrij is. Zo is de hele loc langs de wisseltongen van die sectie
                // gereden. Hoogstens 20 s extra (een sectie die korter is dan de loc, of een melder die blijft hangen).
                bool staartNog = res.Reeks.Count >= 2 && bezet.Contains(res.Reeks[^2]) && bezet.Contains(res.Reeks[^1]) && _klok.Nu < stopTijd + TimeSpan.FromSeconds(20);
                if (!staartNog)
                {
                    if (res.Reeks.Count >= 2 && bezet.Contains(res.Reeks[^2]))
                        _log.Info($"  gestopt op melder {res.Reeks[^1]}: melder {res.Reeks[^2]} bleef na de doorrijtijd en 20 s extra nog bezet.");
                    await StopLoc();
                    res.Einde = RitEinde.StapKlaar;
                    return res;
                }
            }
            if (bezet.Count == 0)
            {
                leegSinds ??= _klok.Nu;
                if (_klok.Nu - leegSinds.Value > TimeSpan.FromSeconds(_ins.LegeMeldersSeconden))
                {
                    // BUG #285 (autolog 21-10 16:00:07-16:00:33, Marco 16:02: "daar was het einde van het pendelspoor en reed de loc tegen het stootjuk aan en ontspoorde daar")
                    if (!doel.IsNavigatie && res.Reeks.Count >= 2 && HuidigeAlarmBlokken().Count == 0 && _kaart.BekendeVolgende(res.Reeks[^1], r).Count == 0)
                        res.EindeVanHetSpoor = true;
                    return await Kortsluiting(res, $"geen enkele melder meer bezet na melder {res.Reeks[^1]}");
                }
            }
            else leegSinds = null;
            await VoedOntbrekendeBlokken(bezet, r, doelInzicht ? _ins.Kruipsnelheid : snelheid);

            // ---- Nieuwe melders ----
            // BUG #54: een loc die met één draaistel al in de VOLGENDE melder staat (bijv. deels in 144
            // en deels in 133) geeft voor die volgende melder nooit een "bezet geworden"-melding:
            // hij was al bezet. Daardoor zag de verkenner de vooruitgang niet ("vertrok niet",
            // verkeerde doodlopend-conclusie, melder ontbreekt in de baankaart). Wordt de huidige
            // melder VRIJ terwijl precies één andere, nog niet bekende melder bezet blijft, dan staat
            // de loc daar duidelijk in: behandel die als zojuist bezet geworden.
            var nieuweBezetten = wijzigingen.Where(w => w.Bezet).ToList();
            // BUG #268 (hardwarelog 21-10 11:21:55.588): Dinamo meldde melder 137 en 138 bezet 0,4 s vóór 'KORTSLUITING in blok 9' en 0,8 s later weer vrij. De navigatie zag 'onverwacht melder 137',
            // weerlegde daarmee voorkennis en meldde "Staat er een wissel niet goed?". Een melder die de kaart niet kent, tijdens een navigatie (die alleen bekende melders hoort te zien), telt
            // pas na 1,5 s (0,8 s echo + ontdendertijd): komt er in die tijd een kortsluiting, dan behandelt de kortsluitregel het (bovenaan de lus); is hij weer vrij, dan was het een echo en telt hij niet.
            if (doel.IsNavigatie)
            {
                foreach (var w in uitgesteld268.Where(u => _klok.Nu - u.Tijd >= TimeSpan.FromMilliseconds(1500) || wijzigingen.Any(x => x.Melder == u.Melder && !x.Bezet)).ToList())
                {
                    uitgesteld268.Remove(w);
                    if (bezet.Contains(w.Melder) && !wijzigingen.Any(x => x.Melder == w.Melder && !x.Bezet)) nieuweBezetten.Add(w);
                    else _log.Info($"  melder {w.Melder} (onbekend in de kaart) was maar even bezet: geen sectie die de loc bereikte, telt niet (BUG #268).");
                }
                foreach (var w in nieuweBezetten.Where(n => _klok.Nu - n.Tijd < TimeSpan.FromMilliseconds(1500) && !_kaart.Melders.Any(x => x.Nummer == n.Melder)).ToList())
                {
                    nieuweBezetten.Remove(w);
                    if (uitgesteld268.All(u => u.Melder != w.Melder)) uitgesteld268.Add(w);
                }
            }
            // BUG #278 (autolog 21-10 13:55:33-14:15:35, twintig minuten): elke rit 'vooruit vanaf 13' kwam op melder 5 (13 -> 5 is achteruit gereden): de loc stond andersom. #227 zag melder 5
            // als staart, maar zodra melder 13 vrij werd telde de regel hieronder (#54) melder 5 toch als bereikt; daarna 'onverwacht melder 5', een overschreven rijrichting (5 > 13) en steeds
            // opnieuw neerzetten. Wordt de startmelder vrij terwijl de loc op die achterkantmelder staat, dan rijdt de loc de verkeerde kant op: stoppen en melden dat hij andersom staat.
            if (res.Reeks.Count == 1 && achterkantGenegeerd is int ag278 && bezet.Contains(ag278) && !bezet.Contains(res.Reeks[0]))
            {
                await StopLoc();
                string t278 = $"De loc rijdt vanaf melder {res.Reeks[0]} {r.Tekst()} naar melder {ag278}, maar {res.Reeks[0]} -> {ag278} is {r.Om().Tekst()} gereden: de loc staat andersom (neus de verkeerde kant op). De rit is gestopt, er is niets vastgelegd. Draai de loc om (of zet hem andersom neer) (BUG #278).";
                _kaart.Waarschuwingen.Add(t278);
                _log.Waarschuwing(t278);
                throw new NavigatieFout(t278) { LaatsteGoed = res.Reeks[0], Onverwacht = ag278, LocAndersom = true };
            }
            if (bezet.Count > 0 && (wijzigingen.Any(w => !w.Bezet && w.Melder == res.Reeks[^1]) || !bezet.Contains(res.Reeks[^1])))
            {
                var verborgen = bezet.Where(x => !res.Reeks.Contains(x) && nieuweBezetten.All(n => n.Melder != x) && uitgesteld268.All(u => u.Melder != x) && x != achterkantGenegeerd).ToList();
                if (verborgen.Count == 1)
                {
                    _log.Info($"Melder {res.Reeks[^1]} is vrij maar melder {verborgen[0]} was al bezet (de loc stond met een deel al in die melder): melder {verborgen[0]} telt als bereikt.");
                    nieuweBezetten.Add(new MelderMonitor.Wijziging(verborgen[0], true, _klok.Nu));
                }
                else if (verborgen.Count > 1)
                    _log.Waarschuwing($"Melder {res.Reeks[^1]} is vrij maar meerdere melders blijven bezet ({Lijst(verborgen)}): volgorde onduidelijk, niet automatisch toegevoegd.");
            }
            foreach (var w in nieuweBezetten)
            {
                int m = w.Melder;
                if (m == res.Reeks[^1]) continue;
                if (res.Reeks.Count >= 2 && m == res.Reeks[^2])
                {
                    if (bezet.Contains(res.Reeks[^1])) continue; // flikkeren op de grens
                    _log.Waarschuwing($"Melder {m} werd weer bezet terwijl de loc {r.Tekst()} reed - rolt de loc terug of rijdt hij de verkeerde kant op?");
                    continue;
                }
                // BUG #227 (log 18-10 13:17:18): de eerste melder na het vertrek ligt volgens de kaart aan de ANDERE kant van de startmelder (21 -> 29 is vooruit 12x gereden, de rit gaat
                // achteruit): een staart- of flikkermelding (melder 29 was 0,8 s bezet). Niet als gereden melder tellen. Volgt daarna een melder die verder aan die kant ligt, dan staat de loc andersom.
                if (res.Reeks.Count == 1 && achterkantGenegeerd is null && IsAchterkantMelder(_kaart, res.Reeks[0], r, m))
                {
                    achterkantGenegeerd = m;
                    _log.Waarschuwing($"Melder {m} werd bezet bij het vertrek {r.Tekst()} vanaf melder {res.Reeks[0]}, maar die ligt volgens de kaart aan de andere kant ({res.Reeks[0]} -> {m} is {r.Om().Tekst()} gereden): staart van de loc of een flikkering, telt niet als gereden melder (BUG #227).");
                    continue;
                }
                if (res.Reeks.Count == 1 && achterkantGenegeerd is int ag && _kaart.BekendeVolgende(ag, r.Om()).Contains(m) && m != res.Reeks[0])
                {
                    await StopLoc();
                    string t227 = $"Na melder {ag} werd melder {m} bezet: de loc rijdt vanaf melder {res.Reeks[0]} de kant van {r.Om().Tekst()} op, dus hij staat andersom (neus de verkeerde kant op). De rit is gestopt, er is niets vastgelegd (BUG #227).";
                    _kaart.Waarschuwingen.Add(t227);
                    _log.Waarschuwing(t227);
                    throw new NavigatieFout(t227) { LaatsteGoed = res.Reeks[0], Onverwacht = m, LocAndersom = true };
                }

                if (doel.IsNavigatie && doelInzicht && res.Reeks[^1] == doel.DoelMelder)
                {
                    // Doel wel bereikt maar net doorgeschoten naar de volgende melder:
                    // terugkruipen in plaats van alarm slaan.
                    await StopLoc();
                    _log.Rijden($"  melder {m} bezet: doorgeschoten, terug naar melder {doel.DoelMelder}.");
                    if (_doorschietMelders.Add(doel.DoelMelder!.Value))
                        _log.Info($"Melder {doel.DoelMelder} onthouden als melder waar de loc doorschiet: een volgende rit hiernaartoe gaat al op de melder ervoor op kruipsnelheid (BUG #200).");
                    await Nastellen(doel.DoelMelder!.Value, r, voor: m);
                    res.Einde = RitEinde.DoelBereikt;
                    return res;
                }
                if (doel.IsNavigatie && doel.VerwachtPad is { } pad && !pad.Contains(m))
                {
                    await StopLoc();
                    int laatsteGoed238 = bezet.Where(x => x != m && pad.Contains(x)).OrderByDescending(x => pad.LastIndexOf(x)).Cast<int?>().FirstOrDefault() ?? res.Reeks[^1];
                    foreach (var t238 in _kaart.WeerlegVoorkennis(pad, r, doel.Configuratie, null, laatsteGoed238)) { _log.Waarschuwing("Voorkennis weerlegd (BUG #238): " + t238 + "."); _kaart.Waarschuwingen.Add(t238); }
                    throw new NavigatieFout($"Onderweg naar melder {doel.DoelMelder} werd onverwacht melder {m} bezet (verwacht: {Lijst(pad)}). Staat er een wissel niet goed?")
                    {
                        // De loc kan bij vertrek al half op de volgende melder gestaan hebben
                        // (dan kwam die nooit als "nieuw" binnen): neem de verste melder van
                        // het verwachte pad die op dit moment nog bezet is.
                        LaatsteGoed = bezet.Where(x => x != m && pad.Contains(x)).OrderByDescending(x => pad.LastIndexOf(x)).Cast<int?>().FirstOrDefault() ?? res.Reeks[^1],
                        Onverwacht = m,
                        RitRichting = r,
                        RitConfiguratie = doel.Configuratie
                    };
                }

                // BUG #97 (log 16:03): de loc stond op melder 13 de verkeerde kant op. Rit "13 achteruit" ging 13 -> 14 (dat is vooruit) en de
                // verkenner liet hem de hele baan rondrijden tot hij bij wissel 2 (afbuigend) kortsluiting kreeg. Stap 1 van een verkenningsrit
                // die niet de basisrit volgt, terwijl die overgang in deze richting nooit met deze wisselstand is gezien maar wel in de
                // TEGENGESTELDE richting, betekent: de loc staat andersom. Meteen stoppen, niets vastleggen.
                if (!doel.IsNavigatie && res.Reeks.Count == 1 && doel.Basis is { Count: >= 2 } bas && IsLocAndersom(_kaart, res.Reeks[0], r, m, bas, cfgNu))
                {
                    await StopLoc();
                    string t = $"De loc reed vanaf melder {res.Reeks[0]} ({r.Tekst()}) naar melder {m}, maar {r.Tekst()} gaat hier naar melder {bas[1]}; naar melder {m} gaat de loc {r.Om().Tekst()}. De loc staat dus andersom (neus de verkeerde kant op). De rit is meteen gestopt, er is niets vastgelegd.";
                    _kaart.Waarschuwingen.Add(t);
                    _log.Waarschuwing(t);
                    throw new NavigatieFout(t) { LaatsteGoed = res.Reeks[0], Onverwacht = m, LocAndersom = true };
                }

                double? dt = onderbroken ? null : (w.Tijd - tijdVorigeMelder).TotalSeconds;
                if (onderbroken && wijzigingen.Contains(w))
                    _langsteVertrekSeconden = Math.Max(_langsteVertrekSeconden, (_klok.Nu - laatsteWijziging).TotalSeconds);
                wachttijdVerlengd = false;
                vervolgHerhaald = false;
                // BUG #59: tijden altijd omgerekend naar de referentiesnelheid van de kaart, en niet vastleggen
                // als de loc al kruipend het doel in zicht heeft (kruipsnelheid zegt niets over de sectielengte).
                if (dt is double gemeten && !doelInzicht)
                {
                    _ritMinDt = Math.Min(_ritMinDt, gemeten);
                    _ritMaxDt = Math.Max(_ritMaxDt, gemeten);
                }
                double? dtOpgeslagen = dt is double d1 && !doelInzicht ? d1 * _ins.Verkensnelheid / RefSnelheid : null;
                // BUG #165: vastleggen met de stand waarin de wissels WERKELIJK stonden (ZetConfiguratie slaat een motor over die onder de loc ligt).
                var cfgWerkelijk = WerkelijkeStand(cfgNu);
                Kennis.Leg("rijrichting", $"{res.Reeks[^1]}>{m}", r.Tekst(), "gereden rit");
                _kaart.RegistreerOvergang(res.Reeks[^1], m, r, cfgWerkelijk, dtOpgeslagen);
                // BUG #74: ingangskant onthouden. Binnen de rit is het de melder daarvoor; bij de eerste stap van de rit de ingang van
                // de vorige rit, mits de loc daar nog in dezelfde richting rijdt en niet met de hand verplaatst is.
                int? ingangVia = res.Reeks.Count >= 2 ? res.Reeks[^2]
                    : _ingang is { } ing0 && ing0.Melder == res.Reeks[0] && ing0.Richting == r ? ing0.Via : null;
                if (ingangVia is int iv && iv != m) _kaart.RegistreerDoorgang(iv, res.Reeks[^1], m, r, cfgWerkelijk);
                bool herhaling = res.Reeks.Contains(m);
                res.Reeks.Add(m);
                if (kruipNaarEinde)
                {
                    // BUG #284: er kwam toch een nieuwe melder; de rit gaat weer met de gewone snelheid verder
                    kruipNaarEinde = false;
                    snelheid = _ins.Verkensnelheid;
                    if (!doelInzicht) await StuurSnelheid(r, snelheid);
                }
                tijdVorigeMelder = w.Tijd;
                laatsteWijziging = _klok.Nu;
                onderbroken = false;
                _log.Rijden($"  melder {m}{(BlokVan(m) is int bl ? $" (blok {bl})" : "")} bezet{(dt is double s ? $" (na {s:0.0} s)" : "")}");
                BewaarNu(); // BUG #54: baankaart/rapport meteen bijwerken (stond tot de opdracht klaar was op de oude stand)

                // BUG #34 (gebruikerswaarneming: "je ziet de loc op bezet melder 5 en 16 en je
                // gaat allerlei melders en blokken aansturen, dat moet beter kunnen" - en
                // concreet: bij een HERHAALDE rit 16→5, waarvan het blok van melder 5 al
                // bekend was, bleef de loc na melder 5 gewoon stilstaan en werd dat ten
                // onrechte als "doodlopend" gemeld, terwijl de EERSTE keer (toen het blok van
                // melder 5 nog onbekend was) de rit wél gewoon doorreed naar melder 13).
                // Oorzaak: Dinamo's rijcommando's gaan ALTIJD via een blokadres (zie
                // DinamoHardware's klasse-uitleg: "DCC-commando's worden altijd VIA EEN BLOK
                // verstuurd, niet rechtstreeks naar een decoderadres") - een commando dat naar
                // blok 4 ging, bereikt de loc niet meer zodra hij fysiek blok 3 induit. Dit
                // stuk code stuurde de rijsnelheid maar ÉÉN keer, bij de START van de hele rit
                // (naar het blok van de toen-huidige melder) - zodra de loc een GRENS
                // overstak naar een volgend, AL bekend blok, kwam er nooit een nieuw
                // snelheidscommando voor dát blok. De eerste keer "werkte" het toevallig omdat
                // de blokproef voor melder 5 (toen nog onbekend) via Nastellen zelf al een
                // vers commando naar het zojuist gevonden blok 3 stuurde - puur een
                // bijwerking, geen opzet. Fix: zodra een nieuwe melder met een AL BEKEND blok
                // bezet raakt, wordt de rijsnelheid (gewoon door, of kruipend als het doel al
                // in zicht is) opnieuw gestuurd naar het blok van die nieuwe melder - zo blijft
                // de loc ook doorrijden over een melder-grens heen waarvoor geen blokproef
                // meer nodig is. Is het blok van deze melder nog NIET bekend, dan verandert er
                // hier niets - dat blijft precies zoals voorheen via de blokproef hieronder
                // lopen.
                // BUG #57: ook voor een melder waarvan het blok onbekend is en waar GEEN blokproef meer komt:
                // anders blijft de loc daar stilstaan (het oude blokcommando bereikt hem niet meer) en wordt
                // dat ten onrechte als doodlopend/kopspoor gemeld. StuurSnelheid gaat dan naar alle blokken.
                if (BlokVereist && (BlokVan(m) is not null || !ProefMogelijk(m)))
                    await StuurSnelheid(r, doelInzicht ? _ins.Kruipsnelheid : snelheid);

                if (doel.IsNavigatie)
                {
                    // BUG #195 (simulatie optie 6): een navigatie die zijn doel niet haalt (de stand om er te komen ontbrak, bv. wissel 6 bleef rechtdoor door de alarmstand) reed eindeloos
                    // rondjes over de lus (600 minuten). Meer melders dan de kaart er heeft x 2 = de loc rijdt rondjes: stoppen en de aanroeper laten herstellen.
                    // BUG #215 (log 18-10 10:22-10:26): 47 melders = 7 rondjes over een keerlus van 7 melders (4 minuten) voordat de grens hierboven werd bereikt. Dezelfde gerichte overgang
                    // (vorige melder -> deze melder) voor de tweede keer terwijl de wissels niet veranderen betekent: de loc rijdt rondjes en haalt zijn doel zo nooit.
                    if (res.Reeks.Count >= 4 && m != doel.DoelMelder && HerhaaldeOvergang(res.Reeks))
                    {
                        await StopLoc();
                        throw new NavigatieFout($"De loc bereikte melder {doel.DoelMelder} niet: hij reed voor de tweede keer {res.Reeks[^2]} -> {m} (hij rijdt rondjes); de rit is gestopt op melder {m}.");
                    }
                    if (res.Reeks.Count > Math.Max(20, 2 * _kaart.Melders.Count) && m != doel.DoelMelder)
                    {
                        await StopLoc();
                        throw new NavigatieFout($"De loc bereikte melder {doel.DoelMelder} niet na {res.Reeks.Count} melders (hij rijdt rondjes); de rit is gestopt op melder {m}.");
                    }
                    // BUG #303 (autolog 25-10 09:31): bij een navigatierit eindigt deze tak met 'continue', dus de uitgestelde wissels (#271) verderop werden in een navigatie
                    // nooit omgezet ("Hij wordt omgezet zodra de loc daar weg is" gebeurde niet). Nu ook hier.
                    await ZetUitgesteldeWissels();   // BUG #271/#303
                    // BUG #200 (Marco 17-10: loc schiet bij het neerzetten op melder 24 door naar 21): op een melder waar de loc eerder doorschoot gaat hij al op de melder ervoor naar kruipsnelheid.
                    if (!doelInzicht && !kruipVoorDoel && doel.VerwachtPad is { Count: >= 2 } vp200 && vp200[^1] == doel.DoelMelder && m == vp200[^2] && _doorschietMelders.Contains(doel.DoelMelder!.Value))
                    {
                        kruipVoorDoel = true;
                        _log.Rijden($"  melder {m}: de laatste melder voor melder {doel.DoelMelder}, waar de loc eerder doorschoot; nu op kruipsnelheid.");
                        await StuurSnelheid(r, _ins.Kruipsnelheid);
                    }
                    if (m == doel.DoelMelder && !doelInzicht)
                    {
                        doelInzicht = true;
                        await StuurSnelheid(r, _ins.Kruipsnelheid);
                    }
                    continue;
                }

                if (herhaling)
                {
                    await StopLoc();
                    res.Einde = RitEinde.Lus;
                    return res;
                }
                if (res.Reeks.Count > _ins.MaxMeldersPerRit)
                {
                    await StopLoc();
                    res.Einde = RitEinde.Maximum;
                    return res;
                }
                if (doel.Basis is { } basis)
                {
                    int idx = res.Reeks.Count - 1;
                    if (afwijkingIndex is null && (idx >= basis.Count || basis[idx] != m)) afwijkingIndex = idx;
                    if (afwijkingIndex is int ai && idx - ai >= doel.ExtraNaAfwijking)
                    {
                        await StopLoc();
                        res.Einde = RitEinde.AfwijkingGevonden;
                        return res;
                    }
                    if (afwijkingIndex is null && idx == basis.Count - 1)
                    {
                        await StopLoc();
                        res.Einde = RitEinde.GelijkAanBasis;
                        return res;
                    }
                }
                if (doel.StopBijMelders.Contains(m))
                {
                    await StopLoc();
                    _log.Rijden($"  gestopt op melder {m}: dit is het bekende kortsluitpunt (voorbij deze melder volgt de kortsluiting). De loc staat dus op het kortsluitpunt, niet ervoor.");
                    res.Einde = RitEinde.BekendKortsluitpunt;
                    return res;
                }
                if (doel.MaxStappen is int maxStappen && res.Reeks.Count - 1 >= maxStappen && !Kruis.Middens.Contains(m))
                {
                    // BUG #175 (Marco 17-10 10:03: "je stopt heel snel in een meldsectie, ruim voor de wisseltongen, daardoor zie je bijna nooit een kortsluiting als je
                    // een wissel naar de samenvoeging toe berijdt"): de loc rijdt nog even door in de nieuwe sectie voordat hij stopt.
                    if (_ins.StapDoorrijSeconden > 0 && stapStopTot is null)
                    {
                        stapStopTot = _klok.Nu + TimeSpan.FromSeconds(_ins.StapDoorrijSeconden);
                        continue;
                    }
                    await StopLoc();
                    res.Einde = RitEinde.StapKlaar;
                    return res;
                }
                await ZetUitgesteldeWissels();   // BUG #271
                // BUG #107: de rit gaat door; ligt voor de loc een wissel waar hij van achteren op af rijdt, dan nu goed zetten.
                if (doel.StaartWissels) cfgNu = await ZetStaartWissels(m, r, cfgNu, doel);
                else if (doel.IsNavigatie) cfgNu = await ZetVooruitRegels(m, r, cfgNu, doel);   // BUG #280
                if (!doel.IsNavigatie && !doel.GeenKruisVooraf) cfgNu = await ZetKruisVooraf(m, r, cfgNu, doel);   // BUG #135
            }

            // ---- Helemaal in een nieuwe, nog onbekende sectie (Dinamo): blokproef ----
            bool heelInNieuwe = BlokVereist && res.Reeks.Count >= 2 && bezet.Count == 1 && bezet.Contains(res.Reeks[^1])
                && ProefMogelijk(res.Reeks[^1]);
            if (!heelInNieuwe) heelInNieuweSectieSinds = null;
            else heelInNieuweSectieSinds ??= _klok.Nu;
            if (heelInNieuwe && _klok.Nu - heelInNieuweSectieSinds!.Value >= TimeSpan.FromSeconds(_ins.BlokproefInrijSeconden))
            {
                heelInNieuweSectieSinds = null;
                await VolledigeStop();
                int proefMelder = res.Reeks[^1];
                // BUG #61 (rit van 10-10 11:38, melder 136): de loc rolde tijdens het stoppen met een deel de
                // volgende melder in en staat nu op TWEE secties. Een blokproef met één blok kan hem dan niet laten
                // bewegen (het voedende blok remt tegen het onbekrachtigde blok), dus 3 x 30 s verloren en geen
                // blok gevonden. Eerst netjes terugkruipen tot alleen deze melder bezet is (alle blokken), dan pas de proef.
                _monitor.Bijwerken();
                var naStop = _monitor.Bezet;
                if (!(naStop.Count == 1 && naStop.Contains(proefMelder)))
                {
                    _log.Info($"Melder {proefMelder}: de loc staat na het stoppen op meerdere melders ({Lijst(naStop)}); eerst terugzetten voordat het blok gezocht wordt.");
                    // BUG #142: een melder die al eens niet te isoleren was (de loc is langer dan de melder), niet opnieuw
                    // tot anderhalve minuut proberen terug te kruipen: meteen de proef met twee melders.
                    if (!_nietIsoleerbaar.Contains(proefMelder) && naStop.Contains(proefMelder) && naStop.FirstOrDefault(x => x != proefMelder, -1) is int andere and >= 0)
                        await Nastellen(proefMelder, r, voor: andere);
                    _monitor.Bijwerken();
                    naStop = _monitor.Bezet;
                    if (!(naStop.Count == 1 && naStop.Contains(proefMelder)))
                    {
                        _nietIsoleerbaar.Add(proefMelder);
                        // BUG #142 (log 15:48: melder 16 en 129 bleven zonder Dinamo-blok, 5 x ruim 80 s verloren): de loc is langer
                        // dan de melder en staat dus altijd ook op een buurmelder. Is van die buurmelder(s) het blok bekend, dan
                        // kan toch gemeten worden: die blokken krijgen geen rijcommando, de overige om beurten. Beweegt de loc,
                        // dan voedt dat blok het deel van de loc dat in deze melder staat.
                        var buren = naStop.Where(x => x != proefMelder).ToList();
                        if (naStop.Contains(proefMelder) && buren.Count > 0 && buren.All(x => BlokVan(x) is not null) && ProefMogelijk(proefMelder))
                        {
                            _proefPogingen[proefMelder] = _proefPogingen.GetValueOrDefault(proefMelder) + 1;
                            _proefKortsluiting = false;
                            bool tweeGelukt = await BlokZoekenMetBuurmelder(proefMelder, buren, r);
                            if (_proefKortsluiting)
                            {
                                _proefKortsluiting = false;
                                _proefPogingen[proefMelder]--;
                                _log.Waarschuwing($"Kortsluiting tijdens de blokproef bij melder {proefMelder}. Staat de loc met een poot tegen een wisseltong die niet goed staat?");
                                return await Kortsluiting(res, $"kortsluiting tijdens de blokproef bij melder {proefMelder}");
                            }
                            if (!tweeGelukt)
                                _log.Waarschuwing($"Voor melder {proefMelder} is ook met de loc op meerdere melders geen Dinamo-blok gevonden{(_proefPogingen[proefMelder] < MaxProefPogingen ? " - bij een volgende rit wordt het nog eens geprobeerd" : "")}.");
                        }
                        else
                        {
                            // Geen bruikbare buurblokken: geen proef nu, de rit gaat door; het blok wordt bij een volgende rit alsnog gezocht.
                            _log.Waarschuwing($"Melder {proefMelder}: kon de loc niet op alleen die melder zetten - blokproef overgeslagen, rit gaat door.");
                        }
                        _monitor.Bijwerken();
                        await StuurSnelheid(r, doelInzicht ? _ins.Kruipsnelheid : snelheid);
                        laatsteWijziging = _klok.Nu;
                        onderbroken = true;
                        continue;
                    }
                }
                _proefPogingen[proefMelder] = _proefPogingen.GetValueOrDefault(proefMelder) + 1;
                _proefKortsluiting = false;
                bool proefGelukt = await BlokZoekenNaInrijden(proefMelder, res.Reeks[^2], r);
                if (!proefGelukt && !_proefKortsluiting) proefGelukt = await BlokZoekenDoorrijdend(proefMelder, r);
                if (_proefKortsluiting)
                {
                    // BUG #62: kortsluiting tijdens de proef (bijv. loc met een poot tegen een verkeerd staande wisseltong).
                    // Niet blind alle blokken blijven proberen: stoppen en als gewone kortsluiting afhandelen.
                    _proefKortsluiting = false;
                    _proefPogingen[proefMelder]--;
                    _log.Waarschuwing($"Kortsluiting tijdens de blokproef bij melder {proefMelder}. Staat de loc met een poot tegen een wisseltong die niet goed staat? Zet zo nodig de wissel bij melder {proefMelder} met de hand goed.");
                    return await Kortsluiting(res, $"kortsluiting tijdens de blokproef bij melder {proefMelder}");
                }
                if (!proefGelukt)
                    _log.Waarschuwing((_proefPogingen[proefMelder] < MaxProefPogingen
                        ? $"Voor melder {proefMelder} is (nog) geen Dinamo-blok gevonden - bij een volgende rit wordt het opnieuw geprobeerd; intussen gaat het rijcommando naar alle blokken."
                        : $"Voor melder {proefMelder} is geen Dinamo-blok gevonden - de verkenner stuurt hier voortaan naar alle blokken.")
                        + $" Geen enkel blok gaf beweging: staat de loc stil door een kortsluiting of een wissel bij melder {proefMelder} die niet goed staat? Controleer dat.");
                _monitor.Bijwerken();
                await StuurSnelheid(r, doelInzicht ? _ins.Kruipsnelheid : snelheid);
                laatsteWijziging = _klok.Nu;
                onderbroken = true;
                continue;
            }

            // ---- Navigatie: doel helemaal ingereden ----
            if (doel.IsNavigatie && doelInzicht && bezet.Count == 1 && bezet.Contains(doel.DoelMelder!.Value))
            {
                await StopLoc();
                await Nastellen(doel.DoelMelder.Value, r, achter: res.Reeks.Count >= 2 ? res.Reeks[^2] : null);
                res.Einde = RitEinde.DoelBereikt;
                return res;
            }

            // ---- BUG #284 (Marco 21-10 16:02: "daar was het einde van het pendelspoor en reed de loc tegen het stootjuk aan en ontspoorde daar"; autolog 15:50:32-15:51:08): na de laatste
            // melder van een nog onbekend stuk reed de loc tot 36 s op verkensnelheid door voordat 'doodlopend' volgde. Staat de loc helemaal in een melder waarvan nog geen vervolg in deze
            // richting bekend is, en komt er na een gewone sectietijd geen nieuwe melder, dan gaat hij op kruipsnelheid verder: een stootjuk wordt dan zacht geraakt.
            if (Rondrit && !doel.IsNavigatie && !kruipNaarEinde && snelheid > _ins.Kruipsnelheid && res.Reeks.Count >= 1 && bezet.Count == 1 && bezet.Contains(res.Reeks[^1])
                && _kaart.BekendeVolgende(res.Reeks[^1], r).Count == 0 && (_klok.Nu - laatsteWijziging).TotalSeconds > KruipNaarEindeNa())
            {
                kruipNaarEinde = true;
                snelheid = _ins.Kruipsnelheid;
                _log.Info($"Na melder {res.Reeks[^1]} ({r.Tekst()}) is nog geen vervolg bekend en na {(_klok.Nu - laatsteWijziging).TotalSeconds:0} s komt er geen nieuwe melder: de loc gaat op kruipsnelheid verder (mogelijk een stootjuk, BUG #284, nog niet getest op de baan).");
                await StuurSnelheidAlleBlokken(r, snelheid);   // ook het blok van de volgende (nog onbekende) sectie, anders blijft de loc op de blokgrens staan
            }

            // ---- Te lang geen nieuwe melder ----
            var grens = wachttijdVerlengd ? TimeSpan.FromSeconds(_ins.MaxSecondenTussenMelders) : WachttijdTussenMelders();
            // BUG #60: bij een al bekend kopspoor (zelfde richting en wisselstand, en de loc is echt vertrokken) niet de hele
            // wachttijd tegen het stootjuk blijven draaien: kort wachten, dan keren.
            bool kopBekend = !doel.IsNavigatie && res.Reeks.Count >= 2
                && _kaart.Kopsporen.Any(k => k.Melder == res.Reeks[^1] && k.Richting == r && k.Configuratie == cfgNu.ToString());
            if (kopBekend)
            {
                double kopSec = Math.Max(_ins.MinSecondenTussenMelders, 12.0 * RefSnelheid / Math.Max(1, _ins.Verkensnelheid));
                if (kopSec < grens.TotalSeconds) grens = TimeSpan.FromSeconds(kopSec);
            }
            if (_klok.Nu - laatsteWijziging > grens)
            {
                if (kopBekend) _log.Info($"Melder {res.Reeks[^1]} is een bekend kopspoor: de loc keert zonder verder te wachten.");
                if (!kopBekend && !wachttijdVerlengd && grens < TimeSpan.FromSeconds(_ins.MaxSecondenTussenMelders))
                {
                    // BUG #55: nog niet concluderen dat dit een kopspoor/stootjuk is: een lange sectie
                    // kost meer tijd dan de geleerde wachttijd. Eenmalig doorrijden tot de maximale wachttijd.
                    wachttijdVerlengd = true;
                    _log.Info($"Na melder {res.Reeks[^1]} al {grens.TotalSeconds:0} s geen nieuwe melder - nog niet 'doodlopend': de loc rijdt door tot de maximale wachttijd ({_ins.MaxSecondenTussenMelders} s).");
                    continue;
                }
                // BUG #64: na deze melder is bij deze wisselstand al eens een vervolgmelder gevonden (ook de omgekeerde
                // overgang telt). Dan is dit geen kopspoor maar blijft de loc hangen (slecht contact, tong, stroomonderbreking):
                // één keer het rijcommando herhalen en nog een keer wachten voordat er iets uit geconcludeerd wordt.
                var bekendVervolg = _kaart.BekendeVolgende(res.Reeks[^1], r, cfgNu);
                bool eersteBlokcontrole = BlokVereist && !Rondrit && res.Reeks.Count == 1 && !blokGecontroleerd && !doelInzicht;
                if (bekendVervolg.Count > 0 && !vervolgHerhaald && !eersteBlokcontrole && !kopBekend)
                {
                    vervolgHerhaald = true;
                    _log.Info($"Na melder {res.Reeks[^1]} al {grens.TotalSeconds:0} s geen nieuwe melder, maar het vervolg ({Lijst(bekendVervolg)}) is bij deze wisselstand al eerder gevonden: de loc blijft waarschijnlijk hangen - rijcommando wordt herhaald, nu naar alle Dinamo-blokken (BUG #69: de loc kan op een blokgrens staan).");
                    await StuurSnelheidAlleBlokken(r, snelheid);
                    laatsteWijziging = _klok.Nu;
                    continue;
                }
                await StopLoc();
                // BUG #181: staat deze melder in deze rijrichting al als doodlopend einde in de kaart (bij welke wisselstand ook), dan staat de loc tegen het stootjuk (stap voor stap
                // rijdt hem na de melder nog even door): geen Dinamo-storing en geen blokproef, maar gewoon weer een doodlopend einde.
                bool kopHierBekend = res.Reeks.Count == 1 && _kaart.Kopsporen.Any(k => k.Melder == start && k.Richting == r);
                if (kopHierBekend) _log.Info($"De loc vertrok niet van melder {start}: dat is een bekend doodlopend einde ({r.Tekst()}); geen blokproef nodig.");
                // BUG #210 (Marco 18-10 09:38; log 09:36-09:37): 9 s na het herstel van een Dinamo-uitval reed de loc 31 s lang niet weg van melder 13 (geen enkele melder-melding in het
                // hardwarelog) en dat werd als 'afbuigend: doodlopend' van wissel 11 opgeslagen - een wissel die fysiek niet bestaat. In optie 6 (Rondrit) wordt de blokcontrole hieronder overgeslagen,
                // dus niets stopte die conclusie. Vertrekt de loc kort na een Dinamo-uitval niet, dan zegt de rit niets over de baan: de opdracht wordt opnieuw gereden.
                if (!doel.IsNavigatie && res.Reeks.Count == 1 && !kopHierBekend && _klok.Nu - _uitvalHersteldOm < TimeSpan.FromMinutes(15))
                {
                    var uitvalTekst = $"De loc vertrok niet van melder {start} ({r.Tekst()}), {(_klok.Nu - _uitvalHersteldOm).TotalSeconds:0} s na het herstel van Dinamo. Dat zegt niets over de wissels (Dinamo reageert nog niet goed op rijcommando's): de rit telt niet en de opdracht wordt opnieuw gereden.";
                    _log.Waarschuwing(uitvalTekst);
                    throw new NavigatieFout(uitvalTekst);
                }
                if (BlokVereist && !Rondrit && res.Reeks.Count == 1 && !blokGecontroleerd && !doelInzicht && !kopHierBekend)
                {
                    // De loc is helemaal niet vertrokken: klopt de blokkoppeling van deze
                    // melder wel? Eén keer opnieuw bepalen voordat we iets concluderen.
                    blokGecontroleerd = true;

                    // BUG #33 (gebruikerswaarneming: "je ziet de loc op bezet melder 5 en 16
                    // en je gaat allerlei melders en blokken aansturen, dat moet beter
                    // kunnen"): hieronder stond voorheen DIRECT de koppeling weggegooid
                    // (`DinamoBlok = null`) gevolgd door `BlokZoekenBijStart` - een VOLLEDIGE
                    // blokproef die ELK bekend Dinamo-blok, in BEIDE richtingen, elk
                    // BlokproefLangSeconden (hier 30 sec) lang uitprobeert. Voor een baan met
                    // 16 blokken is dat in het ergste geval 16 × 2 × 30 = 960 sec (16 minuten)
                    // - terwijl de software het blok voor déze melder vaak AL kende (hier
                    // bijvoorbeeld blok 3 voor melder 5, een paar minuten eerder zelf
                    // gevonden). Fix: eerst GOEDKOOP herbevestigen via het AL bekende blok
                    // (één poging, met de korte BlokproefKortSeconden-wachttijd in plaats van
                    // de lange) - lukt dat, dan is er geen reden om de koppeling weg te gooien
                    // en de dure volledige zoektocht te starten. Pas als deze snelle
                    // herbevestiging ECHT niets oplevert (het blok klopt dus waarschijnlijk
                    // niet meer - bijv. een wissel die stiekem omgezet is), vervalt de
                    // koppeling alsnog en volgt exact dezelfde volledige blokproef als
                    // voorheen - dit pad is dus NIET verwijderd, alleen niet meer de EERSTE
                    // stap.
                    int? bekendBlok = BlokVan(start);
                    bool bevestigd = false;
                    bool alleBlokken = false;       // BUG #69: de loc stond op een blokgrens: verder rijden met het commando naar alle blokken
                    if (bekendBlok is int bb)
                    {
                        _log.Rijden($"De loc vertrok niet van melder {start} - eerst het al bekende Dinamo-blok {bb} nog eens proberen voordat de volledige blokproef start.");
                        var t0Herbevestiging = _klok.Nu;
                        await StuurNaarBlok(bb, r, _ins.Verkensnelheid);
                        var eindHerbevestiging = _klok.Nu + TimeSpan.FromSeconds(_ins.BlokproefKortSeconden);
                        while (_klok.Nu < eindHerbevestiging)
                        {
                            await Wacht(Poll);
                            if (_monitor.Bijwerken().Any(x => x.Tijd >= t0Herbevestiging)) { bevestigd = true; break; }
                        }
                        if (bevestigd)
                        {
                            _hw.ZetLocSnelheid(_ins.LocAdres, 0, r == Richting.Vooruit, bb, _ins.LocStappen);
                            await StopLoc();
                            await Nastellen(start, r);
                        }
                        else
                        {
                            await StuurNaarBlok(bb, r, 0);
                        }
                    }
                    // BUG #69 (log 16:22): de loc stond met een draaistel in blok 7 en een draaistel in blok 3 (kruiswissel)
                    // en reed niet weg met alleen het bekende blok. Eerst één korte poging naar ALLE blokken tegelijk; rijdt hij
                    // dan wel, dan staat hij op een blokgrens en blijft de koppeling van deze melder gewoon bewaard.
                    if (!bevestigd && bekendBlok is not null)
                    {
                        _log.Rijden($"De loc vertrok niet met blok {bekendBlok} - nu één korte poging met het rijcommando naar alle Dinamo-blokken tegelijk (staat hij met een draaistel op een blokgrens?).");
                        if (await ProbeerAlleBlokken(r))
                        {
                            _log.Waarschuwing($"De loc reed pas weg toen alle Dinamo-blokken het rijcommando kregen: hij stond waarschijnlijk met een draaistel op een blokgrens (bijv. in twee blokken op een kruiswissel). De koppeling van melder {start} (blok {bekendBlok}) blijft bewaard.");
                            // Niet terugzetten (dan staat hij zo weer op de grens): de rit gaat gewoon verder vanaf waar hij nu staat,
                            // met het commando naar alle blokken.
                            bevestigd = true;
                            alleBlokken = true;
                        }
                    }
                    // BUG #66: het blok van deze melder is bekend en er was kort geleden een kortsluiting: dan ligt het niet aan
                    // de blokkoppeling maar staat de loc waarschijnlijk op een open gereden of verkeerd staande wissel (log 15:14:
                    // 2 x 9 minuten blokproef met de loc vast op de kruiswissel, blok 8 bleef kortsluiting melden). Niet 18 blokken
                    // x 30 s proberen maar direct de gebruiker vragen de loc terug te zetten.
                    // BUG #164: ook bij een nog onbekend blok (nieuwe melder) is een blokproef zinloos als de kortsluiting bij déze melder hoort (log 09:01: 9 minuten blokproef
                    // voor melder 12 met een loc die klem zat).
                    bool kortsluitingHierBij = _laatsteKortsluitContext is { } kcx && kcx.Melder == start;
                    if (!bevestigd && (bekendBlok is not null || kortsluitingHierBij || HuidigeAlarmBlokken().Count > 0)
                        && (_klok.Nu - _laatsteKortsluitingOm < TimeSpan.FromMinutes(10) || HuidigeAlarmBlokken().Count > 0))
                    {
                        if (!_klemProbeActief && _hw is IBlokAlarmBron && await ProbeerKlemWegTeKrijgen(start, r, doel, $"de loc vertrok niet van melder {start}"))
                            bevestigd = true;
                        else
                        {
                        _log.Waarschuwing($"De loc vertrok niet van melder {start} (blok {(bekendBlok is null ? "nog onbekend" : bekendBlok.ToString())}) en er is kort geleden een kortsluiting geweest: waarschijnlijk staat hij op een open gereden of verkeerd staande wissel (bijv. een kruiswissel met maar één van de twee motoren op afbuigend). De volledige blokproef wordt overgeslagen.");
                        await VraagLocTerugTeZetten(start, $"De loc rijdt niet weg van melder {start}, kort na een kortsluiting. Staat er een wissel (kruiswissel?) open gereden of in een gemengde stand? Zet de wissels goed en de loc op melder {start}.");
                        bevestigd = true;
                        }
                    }
                    // BUG #125 (Marco 14-10 09:42: Dinamo reageerde niet meer, wissels schakelden niet, na het uitzetten van de netspanning kwam er geen melding): de
                    // interface bleef keepalives sturen, dus de stiltemelding (#81) ging niet af. De loc vertrok om 09:41:59 niet van melder 13, terwijl hij daar de hele
                    // ochtend probleemloos vertrok met blok 3: dat is geen blokprobleem. Vroeger volgde een blokproef van minuten die ook niets kon vinden.
                    if (!bevestigd && bekendBlok is not null && VertrekWasBewezen(_kaart, start, r, cfgNu))
                    {
                        var tekst = $"De loc vertrok niet van melder {start} (blok {bekendBlok}), terwijl hij van deze melder eerder in deze verkenning wel gewoon vertrok. Dat lijkt geen blokprobleem maar een Dinamo of baanspanning die niet meer reageert (wissels schakelen dan ook niet).";
                        _log.Waarschuwing("DINAMO REAGEERT WAARSCHIJNLIJK NIET OP COMMANDO'S: " + tekst);
                        _kaart.Waarschuwingen.Add(tekst);
                        if (VraagJaNee is null) throw new OperationCanceledException(tekst);
                        var antwoord = await VraagJaNee("Dinamo reageert niet",
                            tekst + "\n\nControleer de netspanning/baanspanning van Dinamo en de kabel; herstart Dinamo zo nodig en zet de loc op melder " + start + ".\n\nJa = Dinamo is hersteld, opnieuw proberen (alle wissels worden opnieuw gestuurd)\nNee = het ligt aan de blokkoppeling, blokproef starten\n(venster sluiten = verkenning stoppen)");
                        if (antwoord is null) throw new OperationCanceledException("Gebruiker stopte: Dinamo reageert niet.");
                        if (antwoord == true)
                        {
                            await HoortestNaHerstel();
                            _echo.Herstel();
                            var onderLoc2 = WisselsOnderLoc();
                            bool opKruis2 = Kruis.OpKruis(_monitor.Bezet);
                            foreach (var kv in _wisselStand.ToList())
                            {
                                if (onderLoc2.ContainsKey(kv.Key) || (opKruis2 && Kruis.IsMotor(kv.Key))) { _log.Info($"Wissel {kv.Key} wordt niet opnieuw gestuurd: de loc staat op of tegen die wissel."); continue; }   // BUG #173
                                await ZetWissel(kv.Key, kv.Value, forceer: true);
                            }
                            _log.Info("Dinamo hersteld volgens de gebruiker: alle wissels zijn opnieuw gestuurd; de rit wordt hervat.");
                            _uitvalHersteldOm = _klok.Nu;
                            if (Stap) throw new WisselProbleemFout("Dinamo reageerde niet en is hersteld");   // BUG #173
                            bevestigd = true;
                        }
                    }
                    if (!bevestigd)
                    {
                        _log.Waarschuwing($"De loc vertrok niet van melder {start}. Blokkoppeling wordt gecontroleerd.");
                        Kennis.Wis("blok", start.ToString(), "blokproef bij start (loc vertrok niet)");
                        _kaart.Melder(start).DinamoBlok = null;
                        // BUG #69: een mislukte blokproef mag een eerder vastgesteld blok niet wissen (log 15:41-16:01: 2 x 9 minuten
                        // geen beweging door een verkeerd staande wissel, daarna was blok 8 van melder 24 weg en kreeg de melder
                        // later het verkeerde blok 2).
                        if (!await BlokZoekenBijStart(start) && bekendBlok is int behoud)
                        {
                            _kaart.Melder(start).DinamoBlok = behoud;
                            _log.Waarschuwing($"De blokproef gaf geen resultaat: het eerder vastgestelde blok {behoud} van melder {start} blijft bewaard.");
                            BewaarNu();
                        }
                    }
                    _monitor.Bijwerken();
                    if (alleBlokken) await StuurSnelheidAlleBlokken(r, snelheid);
                    else await StuurSnelheid(r, snelheid);
                    laatsteWijziging = _klok.Nu;
                    onderbroken = true;
                    continue;
                }
                if (doel.IsNavigatie)
                {
                    if (doelInzicht && bezet.Contains(doel.DoelMelder!.Value))
                    {
                        // Doel wel bereikt maar het einde (stootjuk) laat hem niet verder:
                        // de loc staat zo goed als het kan op de doelmelder.
                        await Nastellen(doel.DoelMelder.Value, r, achter: res.Reeks.Count >= 2 ? res.Reeks[^2] : null);
                        res.Einde = RitEinde.DoelBereikt;
                        return res;
                    }
                    throw new NavigatieFout($"Melder {doel.DoelMelder} werd niet bereikt: al {WachttijdTussenMelders().TotalSeconds:0} s geen nieuwe melder na melder {res.Reeks[^1]}.");
                }
                res.Einde = RitEinde.Doodlopend;
                res.BekendVervolgGemist = bekendVervolg.Count > 0;
                return res;
            }
        }
    }

    // =====================================================================
    // Kortsluiting
    // =====================================================================

    /// <summary>BUG #81: waarschuwingstekst als de centrale al ≥ 2 s niets (ook geen keepalive) meer
    /// stuurde, anders null. Een vastgelopen Dinamo geeft geen echte kortsluiting.</summary>
    public static string? StilteWaarschuwing(IHardwareInterface hw)
    {
        if (hw is IVerbindingsBron vb && vb.SecondenSindsLaatsteOntvangst is { } stilte && stilte >= 2.0)
            return $"Let op: er is al {stilte:0.0} s geen enkel bericht (ook geen keepalive) van Dinamo ontvangen. Dinamo is waarschijnlijk vastgelopen en deze kortsluitmelding/stilte is dan geen echte kortsluiting: herstart Dinamo.";
        return null;
    }

    private RitDoel? _ritDoel;

    /// <summary>BUG #107 (Marco 09:26: "wissel 5 stond verkeerd, je kon dit niet oplossen terwijl je de route al zou moeten kennen"; log 09:22):
    /// de controlerit reed 4 -> 129 -> 14 (achteruit) met wissel 5 rechtdoor. De kaart weet uit de splitsing van wissel 5 (na melder 14 vooruit:
    /// rechtdoor 24, afbuigend 129) dat van 129 naar 14 rijden van achteren door die wissel gaat en dat hij dan afbuigend moet staan.
    /// Dit zijn de wissels waar een loc die op <paramref name="melder"/> staat of net aankomt en in rijrichting <paramref name="r"/> verder
    /// rijdt van achteren (vanaf de tak waar die melder aan ligt) op af rijdt, met de stand die daarvoor nodig is. Een adres waarvoor de
    /// splitsingen elkaar tegenspreken wordt weggelaten.</summary>
    internal static List<(int Adres, bool Afbuigend)> StaartWissels(Baankaart kaart, int melder, Richting r, Func<int, bool>? stand = null)
    {
        var wens = new Dictionary<int, bool>(); var strijdig = new HashSet<int>();
        void Voeg(int adres, bool afb)
        {
            if (wens.TryGetValue(adres, out var bestaand) && bestaand != afb) strijdig.Add(adres);
            wens[adres] = afb;
        }
        // BUG #108 (log 11:12): de oude regel ("op melder 24 achteruit staat de loc op de rechtdoor-tak van wissel 5, dus wissel 5 rechtdoor") klopte
        // alleen als de loc van 24 naar 14 rijdt. Met wissel 10 afbuigend rijdt hij naar 129; wissel 5 ging toen eerst naar rechtdoor en pas bij
        // melder 129 (loc al bijna bij de tong) naar afbuigend: 0,65 s later kortsluiting in blok 9, en de partnerzoektocht concludeerde ten
        // onrechte dat wissel 3 en 5 'overloopwissels' zijn. Nu kijkt de verkenner vooruit: waar gaat de loc vanaf deze melder heen (uit de
        // splitsingen en de stand van de wissels, anders uit de bekende overgangen) en welke wissel moet voor die stap, of de stap daarna, van
        // achteren goed staan? Is er niets bekend over de vervolgmelders, dan geldt de oude regel.
        var volgende = VerwachteVolgende(kaart, melder, r, stand);
        if (volgende.Count > 0)
        {
            foreach (var y in volgende)
                foreach (var e in kaart.WisselEisenVoorStapDetail(melder, y, r).Where(e => e.Achter)) Voeg(e.Adres, e.Afbuigend);
            // de stap daarna: alleen wissels die ver genoeg weg liggen en waar alle bekende vervolgen hetzelfde van vragen
            var tweede = new Dictionary<int, HashSet<bool>>(); bool allesBekend = true;
            foreach (var y in volgende)
            {
                var zs = VerwachteVolgende(kaart, y, r, null);
                if (zs.Count == 0) { allesBekend = false; continue; }
                foreach (var z in zs)
                    foreach (var e in kaart.WisselEisenVoorStapDetail(y, z, r).Where(e => e.Achter))
                    {
                        if (!tweede.TryGetValue(e.Adres, out var set)) tweede[e.Adres] = set = new HashSet<bool>();
                        set.Add(e.Afbuigend);
                    }
            }
            if (allesBekend)
                foreach (var kv in tweede.Where(kv => kv.Value.Count == 1 && !wens.ContainsKey(kv.Key))) Voeg(kv.Key, kv.Value.First());
        }
        else
        {
            foreach (var w in kaart.Wissels)
                foreach (var o in w.Waarnemingen.Where(x => x.Soort == WaarnemingSoort.Splitsing && r == x.Richting.Om()))
                {
                    bool? afb = o.VolgendeBijRechtdoor == melder ? false : o.VolgendeBijAfbuigend == melder ? true : null;
                    if (afb is null) continue;
                    Voeg(w.Adres, afb.Value);
                }
        }
        return wens.Where(kv => !strijdig.Contains(kv.Key)).OrderBy(kv => kv.Key).Select(kv => (kv.Key, kv.Value)).ToList();
    }

    /// <summary>BUG #260 (autolog 20-10 18:24:26-18:24:50 en 18:34:53): opdracht #10 (afbuigende tak van wissel 1 na melder 12) reed met stand 1, 9, 10 (9 en 10 waren nodig om via de keerlus
    /// bij melder 12 te komen) en ging daarna 12 -> 5 -> 13 -> 14 -> 24 met wissel 10 afbuigend: kortsluiting, terwijl de kaart al wist dat 14 -> 24 met wissel 10 afbuigend kortsluiting geeft
    /// (bekende ingang). Wissels die voor de komende stap (of de stap daarna, als die eenduidig bekend is) een bekende verboden ingang zijn, met de huidige stand afbuigend.</summary>
    internal static List<int> VerbodenVooruit(Baankaart kaart, int melder, Richting r, Func<int, bool> stand)
    {
        var res = new HashSet<int>();
        var verboden = kaart.VerbodenIngangen().Where(v => v.R == r).ToList();
        var volgende = VerwachteVolgende(kaart, melder, r, stand);
        foreach (var y in volgende)
        {
            foreach (var v in verboden.Where(v => v.Via == melder && v.Na == y && stand(v.Adres))) res.Add(v.Adres);
            var zs = VerwachteVolgende(kaart, y, r, stand);
            if (volgende.Count == 1 && zs.Count == 1)
                foreach (var v in verboden.Where(v => v.Via == y && v.Na == zs[0] && stand(v.Adres))) res.Add(v.Adres);
        }
        return res.OrderBy(a => a).ToList();
    }

    /// <summary>BUG #267 (autolog 21-10 11:20:44-11:21:47): opdracht #5 (stand: wissel 10) reed achteruit door de keerlus en kwam 11 -> 4 -> 129; met wissel 9 rechtdoor kortsluiting,
    /// terwijl kortsluitpunt #3 al bewezen had dat de stap 4 -> 129 (achteruit) wissel 9 afbuigend vraagt. Die oplossing hoorde bij stand '2' (wissel 2+1 van de opdracht toen) en werd
    /// daardoor niet gebruikt. Hier: bewezen (niet voorlopige) oplossingen van kortsluitpunten waarvan de laatste stap van de route de komende stap is (of de eenduidig bekende stap daarna),
    /// met de oplossingswissel nu niet in de goede stand.</summary>
    internal static List<(int Adres, bool Afbuigend)> OplossingenVooruit(Baankaart kaart, int melder, Richting r, Func<int, bool> stand)
    {
        var stappen = new HashSet<(int, int)>();
        var volgende = VerwachteVolgende(kaart, melder, r, stand);
        foreach (var y in volgende)
        {
            stappen.Add((melder, y));
            var zs = VerwachteVolgende(kaart, y, r, stand);
            if (volgende.Count == 1 && zs.Count == 1) stappen.Add((y, zs[0]));
        }
        var res = new Dictionary<int, bool>(); var strijdig = new HashSet<int>();
        foreach (var kp in kaart.Kortsluitpunten.Where(k => k.Opgelost && !k.Voorlopig && k.OpgelostDoorAdres is int && k.OpgelostMetAfbuigend is bool && k.Richting == r))
        {
            var pad = kp.Route.LastOrDefault()?.Pad;
            if (pad is null || pad.Count < 2 || !stappen.Contains((pad[^2], pad[^1]))) continue;
            int a = kp.OpgelostDoorAdres!.Value; bool afb = kp.OpgelostMetAfbuigend!.Value;
            // BUG #298: alleen als de loc daarna dezelfde kant op gaat als de rit waarmee de oplossing bewezen werd
            if (OplossingVolgende(kaart, kp, a, afb) is int vv && !stappen.Contains((kp.NaMelder, vv)) && VerwachteVolgende(kaart, kp.NaMelder, r, stand) is { Count: 1 } vg && vg[0] != vv) continue;
            if (stand(a) == afb) continue;
            if (res.TryGetValue(a, out var b) && b != afb) strijdig.Add(a);
            res[a] = afb;
        }
        return res.Where(kv => !strijdig.Contains(kv.Key)).OrderBy(kv => kv.Key).Select(kv => (kv.Key, kv.Value)).ToList();
    }

    /// <summary>BUG #108: de melders waar de loc vanaf <paramref name="melder"/> in richting <paramref name="r"/> naartoe kan: bij een bekende
    /// splitsing met de kop op deze melder bepaalt de stand van die wissel (zoals bekend in <paramref name="stand"/>; onbekend = rechtdoor) de tak,
    /// anders de bekende overgangen.</summary>
    internal static List<int> VerwachteVolgende(Baankaart kaart, int melder, Richting r, Func<int, bool>? stand)
    {
        var kop = kaart.Wissels.SelectMany(w => w.Waarnemingen.Where(o => o.Soort == WaarnemingSoort.Splitsing && o.NaMelder == melder && o.Richting == r)
                                                             .Select(o => (w.Adres, o))).ToList();
        if (kop.Count > 0 && stand != null)
        {
            var res = new List<int>();
            foreach (var (adres, o) in kop)
            {
                int? y = stand(adres) ? o.VolgendeBijAfbuigend : o.VolgendeBijRechtdoor;
                if (y is int yy) res.Add(yy);
            }
            if (res.Count > 0) return res.Distinct().ToList();
        }
        var bekend = kaart.BekendeVolgende(melder, r);
        return stand is null || bekend.Count < 2 ? bekend : VolgendeBijStand(kaart, melder, r, bekend, stand);
    }

    /// <summary>BUG #269 (autolog 21-10 11:50:55-12:07:10, zesmaal): opdracht #5 (21 achteruit, wissel 10 afbuigend) kwam op melder 129 en de staartwisselregel wilde wissel 5 (+6) afbuigend
    /// zetten "voor de stap 129 -> 14". Maar met 5 en 6 op de baan rechtdoor gaat de loc 129 -> 10; de kaart heeft voor 129 geen splitsing, alleen overgangen naar 4, 10 en 14, en de regel
    /// rekende met allemaal. Alleen #264 (volger 6 onder de loc) hield het omzetten tegen; anders had de staartwissel de route van de opdracht veranderd. Nu: zijn er meer bekende vervolgen,
    /// dan blijven de vervolgen over die passen bij de huidige stand van de wissels die de vervolgen van elkaar onderscheiden (een wissel die bij elk vervolg steeds dezelfde stand had en
    /// tussen de vervolgen verschilt). Blijft er niets over, dan gelden alle vervolgen (zoals voorheen).</summary>
    internal static List<int> VolgendeBijStand(Baankaart kaart, int melder, Richting r, List<int> bekend, Func<int, bool> stand)
    {
        var (vast, onderscheid) = Onderscheid(kaart, melder, r, bekend);
        if (onderscheid.Count == 0) return bekend;
        var res = bekend.Where(y => !vast.ContainsKey(y) || onderscheid.All(a => vast[y][a] == stand(a))).ToList();
        return res.Count > 0 ? res : bekend;
    }

    /// <summary>BUG #269/#270: per bekend vervolg van <paramref name="melder"/> de wissels die bij elke rit naar dat vervolg dezelfde stand hadden, en de wissels die de vervolgen van
    /// elkaar onderscheiden. Per vervolg tellen de standen van de overgang in deze rijrichting; alleen als die er niet is de omgekeerde overgang (die kan in een andere stand gereden zijn,
    /// zoals 14 -> 24 vooruit met wissel 10 afbuigend: dat was de verboden ingang).</summary>
    internal static (Dictionary<int, Dictionary<int, bool>> Vast, List<int> Onderscheid) Onderscheid(Baankaart kaart, int melder, Richting r, List<int> bekend)
    {
        var vooruit = new Dictionary<int, List<HashSet<int>>>(); var terug = new Dictionary<int, List<HashSet<int>>>();
        foreach (var o in kaart.Overgangen)
        {
            bool heen = o.Van == melder && o.Richting == r;
            int? y = heen ? o.Naar : o.Naar == melder && o.Richting == r.Om() ? o.Van : null;
            if (y is not int yy || !bekend.Contains(yy)) continue;
            var doel = heen ? vooruit : terug;
            if (!doel.TryGetValue(yy, out var l)) doel[yy] = l = new List<HashSet<int>>();
            foreach (var s in o.GezienBijConfiguraties) l.Add(Configuratie.VanSleutel(s).Afbuigend.ToHashSet());
        }
        var standen = new Dictionary<int, List<HashSet<int>>>();
        foreach (var y in bekend)
        {
            if (vooruit.TryGetValue(y, out var l) && l.Count > 0) standen[y] = l;
            else if (terug.TryGetValue(y, out var t) && t.Count > 0) standen[y] = t;
        }
        var vast = new Dictionary<int, Dictionary<int, bool>>();   // vervolg -> adres -> vaste stand
        if (standen.Count < 2 || standen.Count < bekend.Count) return (vast, new List<int>());
        var adressen = standen.Values.SelectMany(l => l.SelectMany(x => x)).Distinct().ToList();
        foreach (var (y, l) in standen)
        {
            vast[y] = new Dictionary<int, bool>();
            foreach (var a in adressen)
            {
                bool eerste = l[0].Contains(a);
                if (l.All(x => x.Contains(a) == eerste)) vast[y][a] = eerste;
            }
        }
        var onderscheid = adressen.Where(a => vast.Values.All(d => d.ContainsKey(a)) && vast.Values.Select(d => d[a]).Distinct().Count() > 1).OrderBy(a => a).ToList();
        return (vast, onderscheid);
    }

    /// <summary>BUG #296 (Marco 21-10 17:34: "hoe vaak ben je nu al gestruikeld op wissel 10 en 13 ... een kruiswissel: kom ik van been A dan kan ik naar C of D, van B ook naar C of D"):
    /// de stand die de doorgang <paramref name="via"/> -> <paramref name="melder"/> -> <paramref name="naar"/> vraagt, uit de doorgangen (ingangsbeen -> uitgangsbeen) die de loc gereden heeft.
    /// Per uitgangsbeen telt per wissel hoe vaak hij afbuigend stond (een enkele afwijkende rit is ruis); een wissel die bij alle uitgangsbenen van deze ingang een vaste stand heeft en
    /// tussen de uitgangsbenen verschilt, bepaalt het been. Zo onthoudt de kaart voor elk been van de kruiswissel bij 24 welke stand van 10 en 13 bij welk ander been hoort.</summary>
    internal static Dictionary<int, bool> DoorgangEisen(Baankaart kaart, int via, int melder, int naar, Richting r)
    {
        var uit = kaart.Doorgangen.Where(d => d.Via == via && d.Melder == melder && d.Richting == r && d.AantalKeer > 0).ToList();
        if (uit.Count < 2 || !uit.Any(d => d.Volgende == naar)) return new();
        var vast = new Dictionary<int, Dictionary<int, bool>>();
        foreach (var d in uit)
        {
            var tel = d.Tellers.Count > 0 ? d.Tellers : d.Configuraties.ToDictionary(c => c, _ => 1);
            int n = tel.Values.Sum();
            if (n == 0) continue;
            var v = new Dictionary<int, bool>();
            foreach (var a in uit.SelectMany(x => x.Tellers.Count > 0 ? x.Tellers.Keys.ToList() : x.Configuraties).SelectMany(s => Configuratie.VanSleutel(s).Afbuigend).Distinct())
            {
                double p = tel.Where(kv => Configuratie.VanSleutel(kv.Key).Bevat(a)).Sum(kv => kv.Value) / (double)n;
                if (p >= 0.8) v[a] = true; else if (p <= 0.2) v[a] = false;
            }
            vast[d.Volgende] = v;
        }
        if (!vast.ContainsKey(naar) || vast.Count < 2) return new();
        var onderscheid = vast[naar].Keys.Where(a => vast.Values.All(x => x.ContainsKey(a)) && vast.Values.Select(x => x[a]).Distinct().Count() > 1);
        return onderscheid.ToDictionary(a => a, a => vast[naar][a]);
    }

    /// <summary>BUG #270: de standen die de stap <paramref name="van"/> -> <paramref name="naar"/> volgens de gereden overgangen vraagt (alleen de wissels die de vervolgen van
    /// <paramref name="van"/> onderscheiden).</summary>
    internal static Dictionary<int, bool> StapEisenUitOvergangen(Baankaart kaart, int van, int naar, Richting r)
    {
        var bekend = kaart.BekendeVolgende(van, r);
        if (bekend.Count < 2 || !bekend.Contains(naar)) return new();
        var (vast, onderscheid) = Onderscheid(kaart, van, r, bekend);
        if (!vast.TryGetValue(naar, out var d)) return new();
        return onderscheid.ToDictionary(a => a, a => d[a]);
    }

    /// <summary>BUG #107: zet de wissels uit <see cref="StaartWissels"/> goed voordat de loc erop afrijdt. Overgeslagen worden: het adres dat de
    /// rit zelf test, de twee kruiswisselmotoren (daar geldt de kruiswissel-referentie), adressen buiten het geteste bereik en een wissel die de
    /// rit zelf bewust afbuigend heeft gezet. Geeft de werkelijke stand terug.</summary>
    /// <summary>BUG #162 (log 14:40:41): achteruit via melder 21 zette de verkenner wissel 9 afbuigend "voor de loc er is" (voor 24 -> 14), maar de lopende
    /// rit had motor 10 afbuigend: de loc reed 24 -> 129 en wissel 9 afbuigend geeft op die ingang kortsluiting. Een staartwissel die afbuigend moet,
    /// wordt niet gezet als een kruismotor afbuigend staat en de kaart die wissel voor die rijrichting als verboden ingang kent.</summary>
    internal static bool StaartWisselToegestaan(Baankaart kaart, int adres, bool afb, Richting r, Configuratie cfg, KruisKennis kruis)
        => !(afb && kruis.Motoren.Any(cfg.Bevat) && kaart.VerbodenIngangen().Any(v => v.Adres == adres && v.R == r));

    /// <summary>BUG #280 (autolog 21-10 14:51:25-14:52:15): de navigatie 29 -> 21 -> 24 -> 129 -> 10 -> 9 -> 1 -> 12 -> 11 -> 4 (achteruit) zette geen wissels onderweg: wissel 9 moest bij 24 -> 129
    /// rechtdoor (#260) en bij 4 -> 129 afbuigend (#267), maar navigaties gebruiken de staartwisselregel niet. Bij 4 schoot de loc door naar 129 met wissel 9 rechtdoor: kortsluiting. Deze twee
    /// regels (bewezen oplossing en verboden ingang vlak voor de loc) gelden nu ook tijdens een navigatie.</summary>
    private async Task<Configuratie> ZetVooruitRegels(int melder, Richting r, Configuratie cfg, RitDoel doel)
    {
        // BUG #267: een bewezen kortsluitoplossing voor de komende stap (wissel 9 afbuigend voor 4 -> 129) gaat nu al om, ook als hij bij een andere stand van de opdracht gevonden is
        foreach (var (a, afb) in OplossingenVooruit(_kaart, melder, r, x => _wisselStand.TryGetValue(x, out var st) && st))
        {
            if (doel.ProefAdres == a || Kruis.IsMotor(a) || IsVolger(a) || !_ins.WisselAdressen().Contains(a)) continue;
            if (doel.IsNavigatie && WisselsBijBezet(_kaart, _monitor.Bezet).Contains(a)) continue;   // #280: niet bij de loc
            if (VolgerOnderLoc(a) is not null) continue;   // #264
            _log.Info($"Melder {melder}: wissel {a} gaat naar {(afb ? "afbuigend" : "rechtdoor")} voordat de loc er is: een eerder opgelost kortsluitpunt bewees dat de komende stap die stand vraagt (BUG #267).");
            await ZetWissel(a, afb);
            cfg = cfg.MetStand(a, afb);
        }
        // BUG #260: een bekende verboden ingang vlak voor de loc gaat altijd naar rechtdoor (ook als de stand van de opdracht hem afbuigend heeft), behalve het geteste adres zelf
        foreach (var a in VerbodenVooruit(_kaart, melder, r, x => _wisselStand.TryGetValue(x, out var st) && st))
        {
            if (doel.ProefAdres == a || Kruis.IsMotor(a) || IsVolger(a) || !_ins.WisselAdressen().Contains(a)) continue;
            if (doel.IsNavigatie && WisselsBijBezet(_kaart, _monitor.Bezet).Contains(a)) continue;   // #280: niet bij de loc
            _log.Info($"Melder {melder}: wissel {a} gaat naar rechtdoor voordat de loc er is: de kaart weet dat de loc daar verderop met wissel {a} afbuigend kortsluiting krijgt (bekende ingang, BUG #260).");
            await ZetWissel(a, false);
            cfg = cfg.MetStand(a, false);
        }
        return cfg;
    }

    private async Task<Configuratie> ZetStaartWissels(int melder, Richting r, Configuratie cfg, RitDoel doel)
    {
        foreach (var (adres, afb) in StaartWissels(_kaart, melder, r, a => _wisselStand.TryGetValue(a, out var st) ? st : cfg.Bevat(a)))
        {
            if (doel.ProefAdres == adres) continue;
            if (Kruis.IsMotor(adres)) continue;
            if (!_ins.WisselAdressen().Contains(adres)) continue;
            if (doel.Configuratie.Bevat(adres) && !afb) continue;
            if (!StaartWisselToegestaan(_kaart, adres, afb, r, cfg, Kruis))
            {
                _log.Info($"Melder {melder}: wissel {adres} blijft rechtdoor: met een kruismotor afbuigend gaat de loc een andere kant op en wissel {adres} afbuigend geeft daar kortsluiting (bekende ingang).");
                continue;
            }
            if (_wisselStand.TryGetValue(adres, out var nu) && nu == afb) { cfg = cfg.MetStand(adres, afb); continue; }
            if (VolgerOnderLoc(adres) is int volger)
            {
                _log.Waarschuwing($"Melder {melder}: wissel {adres} zou nu naar {(afb ? "afbuigend" : "rechtdoor")} moeten, maar wissel {volger} (overloopset) gaat dan mee en die ligt bij de melder waar de loc al staat ({Lijst(_monitor.Bezet)}). " +
                                  $"Hij wordt nu niet omgezet (omzetten met de loc erop geeft kortsluiting); de loc rijdt met de huidige stand verder. De kaart kent deze stap nu, de volgende keer gaan beide al om voordat de loc er is (BUG #264, nog niet getest op de baan).");
                continue;
            }
            _log.Info($"Melder {melder}: de loc rijdt {r.Tekst()} van achteren op wissel {adres} af (de kaart kent die splitsing); wissel {adres} gaat nu naar {(afb ? "afbuigend" : "rechtdoor")} voordat de loc er is.");
            await ZetWissel(adres, afb);
            cfg = cfg.MetStand(adres, afb);
        }
        cfg = await ZetVooruitRegels(melder, r, cfg, doel);
        return cfg;
    }

    /// <summary>BUG #135 (Marco 14:12, log 14:08:52-14:09:08): de verkenningsrit 15 -> 8 -> 24 met wissel 14 afbuigend reed de kruiswissel in vanaf melder 8 met motor
    /// 13 rechtdoor: kortsluiting. De eigen kaart had al twee keer gezien dat een loc vanaf melder 8 alleen met motor 13 afbuigend door de kruiswissel komt.
    /// Rijdt de loc volgens de kaart (bij de huidige wisselstand, kruismotoren niet meegeteld) binnen twee stappen een kruiswissel in, en is voor die ingang
    /// een andere motorstand bewezen dan nu staat, dan worden de motoren nu gezet, terwijl de loc er nog niet is. Wat de rit zelf wil (de motoren van de opdracht
    /// en het geteste adres) blijft ongemoeid; staat de loc al op de kruiswissel, dan gebeurt er niets. Zonder bewijs verandert er niets.</summary>
    private async Task<Configuratie> ZetKruisVooraf(int melder, Richting r, Configuratie cfg, RitDoel doel)
    {
        if (Kruis.Middens.Count == 0 || Kruis.Motoren.Count == 0 || Kruis.OpKruis(_monitor.Bezet)) return cfg;
        var ingangen = KruisBewijs.Ingangen(_kaart, Kruis, melder, r, cfg);
        if (ingangen.Count == 0) ingangen = KruisBewijs.Ingangen(_kaart, Kruis, melder, r, cfg, ruim: true);   // BUG #171
        if (ingangen.Count == 0) return cfg;
        var motoren = Kruis.Motoren.OrderBy(x => x).ToList();
        var vast = motoren.Where(m => doel.Configuratie.Bevat(m) || doel.ProefAdres == m).ToHashSet();
        var keuzes = new List<(Configuratie Stand, int Midden, int Ingang)>();
        foreach (var (midden, ingang) in ingangen)
            if (KruisBewijs.KiesStand(KruisBewijs.GezienBijIngang(_kaart, Kruis, midden, ingang), cfg, motoren, vast) is { } stand)
                keuzes.Add((stand, midden, ingang));
        if (keuzes.Count == 0 || keuzes.Select(k => k.Stand.Sleutel).Distinct().Count() > 1) return cfg;   // geen bewijs, of tegenstrijdig: niets doen
        var (doelStand, kruisMelder, via) = keuzes[0];
        var wijzig = motoren.Where(m => !vast.Contains(m) && cfg.Bevat(m) != doelStand.Bevat(m)).ToList();
        if (wijzig.Count == 0) return cfg;
        _log.Info($"Melder {melder}: de loc rijdt {r.Tekst()} de kruiswissel (melder {kruisMelder}) in vanaf melder {via}. Vanaf die kant is eerder alleen gereden met {KruisBewijs.StandTekst(motoren, doelStand)}; " +
                  $"{string.Join(" en ", wijzig.Select(m => $"motor {m}"))} {(wijzig.Count == 1 ? "gaat" : "gaan")} nu om voordat de loc er is.");
        foreach (var m in wijzig)
        {
            await ZetWissel(m, doelStand.Bevat(m));
            cfg = cfg.MetStand(m, doelStand.Bevat(m));
        }
        return cfg;
    }

    /// <summary>BUG #107: de wissels die de laatste stap van de rit van achteren doorloopt, met de stand die de kaart daarvoor kent.</summary>
    internal static List<(int Adres, bool Afbuigend)> EisenLaatsteStap(Baankaart kaart, List<int> reeks, Richting r) =>
        reeks.Count < 2 ? new()
            : kaart.WisselEisenVoorStapDetail(reeks[^2], reeks[^1], r).Where(e => e.Achter).Select(e => (e.Adres, e.Afbuigend)).Distinct().ToList();

    /// <summary>BUG #107: staat een wissel die de laatste stap van achteren doorloopt niet zoals de kaart eist, dan is dat de verklaring van de
    /// kortsluiting en schakelt de verkenner hem zelf (dat deed de gebruiker in het log van 09:24 ook, en toen was de kortsluiting meteen weg).
    /// Daarna kruipt de loc door tot hij alleen nog op de laatste melder staat. Geeft true als dat gelukt is.</summary>
    private async Task<bool> HerstelBekendeWisselstand(RitResultaat res)
    {
        var eisen = EisenLaatsteStap(_kaart, res.Reeks, res.Richting)
            .Where(e => e.Adres != _ritDoel?.ProefAdres && !Kruis.IsMotor(e.Adres)
                        && _ins.WisselAdressen().Contains(e.Adres))
            .Where(e => !(_wisselStand.TryGetValue(e.Adres, out var nu) && nu == e.Afbuigend)).ToList();
        if (eisen.Count == 0) return false;
        int van = res.Reeks[^2], naar = res.Reeks[^1];
        foreach (var (adres, afb) in eisen)
        {
            _log.Waarschuwing($"De kaart weet dat de stap {van} -> {naar} ({res.Richting.Tekst()}) van achteren door wissel {adres} gaat en dat die dan {(afb ? "afbuigend" : "rechtdoor")} moet staan; hij stond {(_wisselStand.TryGetValue(adres, out var nu0) && nu0 ? "afbuigend" : "rechtdoor")}. Dat verklaart deze kortsluiting: de verkenner schakelt wissel {adres} nu zelf.");
            await ZetWissel(adres, afb, forceer: true);
        }
        _kortsluitingGemeld = false;
        if (_hw.KanMelderStatusOpvragen) foreach (var m in _monitor.Bezet) _hw.VraagMelderStatusOp(m);
        var eind = _klok.Nu + TimeSpan.FromSeconds(8);
        while (HuidigeAlarmBlokken().Count > 0 && _klok.Nu < eind) await Wacht(TimeSpan.FromMilliseconds(300));
        if (HuidigeAlarmBlokken().Count > 0)
        {
            _log.Waarschuwing($"De kortsluiting in blok {string.Join(" en ", HuidigeAlarmBlokken())} blijft ook na het schakelen; de gebruiker wordt gevraagd.");
            return false;
        }
        _kortsluitingGemeld = false;
        _negeerKortsluitingTot = _klok.Nu + TimeSpan.FromSeconds(12);
        try
        {
            await Nastellen(naar, res.Richting, achter: van);
            _log.Info($"Loc staat weer op melder {naar}.");
            return true;
        }
        catch (NavigatieFout f)
        {
            _log.Waarschuwing($"Na het schakelen lukte het neerzetten op melder {naar} niet: {f.Message}");
            return false;
        }
    }

    private bool _klemProbeActief;
    /// <summary>BUG #245: wissel die de #230-regel het laatst zelf terugzette (niet nog eens omzetten).</summary>
    private (int Adres, DateTime Tijd)? _teruggezet230;

    /// <summary>BUG #164 (Marco 16-10 09:13: "je kon de situatie rond wissel 2 en 1 niet zelf oplossen?"): de loc zit klem op een wissel, maar Dinamo meldt de kortsluiting
    /// alleen zolang er een rijcommando loopt; bij een stilstaande loc verdwijnt het alarm na 0,8 s. De alarmroutine (#108) kijkt of het alarm na het omzetten van
    /// een wissel weggaat en ziet dan niets. Hier zit het rijcommando vast aan de proef: wissel omzetten, de loc even laten rijden, en kijken of het alarm
    /// wegblijft en de loc echt beweegt. Zo ja, dan blijft die wissel staan en wordt hij als oplossing van het kortsluitpunt vastgelegd (zoals #108/#144).
    /// Nooit kruismotoren, nooit wissels die bij een bezette melder liggen (de loc kan erop staan, #109) en nooit wissels die de rit zelf nodig heeft.
    /// Alle wissels worden teruggezet als er niets helpt. Nog niet getest op de baan.</summary>
    private async Task<bool> ProbeerKlemWegTeKrijgen(int start, Richting r, RitDoel doel, string stap)
    {
        _alarmZoekenBezig++;   // BUG #294: tijdens het losmaken telt een ontbrekende terugmelding niet als Dinamo-uitval (zie #273)
        try { return await ProbeerKlemWegTeKrijgenKern(start, r, doel, stap); }
        finally { _alarmZoekenBezig--; }
    }

    private async Task<bool> ProbeerKlemWegTeKrijgenKern(int start, Richting r, RitDoel doel, string stap)
    {
        if (_klemProbeActief || _hw is not IBlokAlarmBron) return false;
        // BUG #173 (Marco 17-10): zit de loc klem (vaak op of tegen een wissel), dan eerst terug het geteste stuk op in plaats van andere wissels te proberen.
        if (Stap && await ProbeerTerugNaarGetest(start, r))
            throw new WisselProbleemFout($"De loc zat klem ({stap}) en is teruggereden naar een getest stuk baan");
        _monitor.Bijwerken();
        var bezet = _monitor.Bezet.ToHashSet();
        var bijLoc = WisselsBijBezet(_kaart, bezet);
        var nodig = doel.Configuratie.Afbuigend.ToHashSet();
        var kandidaten = _ins.WisselAdressen().Where(a => !Kruis.IsMotor(a) && !IsVolger(a) && !bijLoc.Contains(a) && !nodig.Contains(a)).ToList();
        // BUG #169: wissels die van de samengevoegde kant bereden worden helpen niet (overslaan); wissels die van een tak richting de samenvoeging bereden worden gaan voorop.
        var (achterKlem, kopKlem) = WisselRollen(_kaart, start, r, null);
        kandidaten = kandidaten.Where(a => !kopKlem.Contains(a) || achterKlem.Contains(a)).OrderByDescending(a => achterKlem.Contains(a)).ThenBy(a => a).ToList();
        // BUG #230 (autolog 18-10 13:31:23-13:33:52): wissel 5 werd net omgezet terwijl de loc op melder 14 stond (de neus bij de tong), en direct daarna zat de loc klem. De routine
        // probeerde 2 minuten lang alle andere wissels (elk met een kortsluiting) en sloeg juist wissel 5 over. Is de laatste wissel kort geleden omgezet terwijl de loc op dezelfde melders
        // stond, dan gaat eerst die terug naar de vorige stand (daarin stond de loc er zonder kortsluiting). Gaat het alarm weg, dan wordt de stap afgebroken en later opnieuw gereden.
        // BUG #245 (autolog 18-10 15:39:23-15:39:37): de terugzetting door deze regel zelf (wissel 1 terug naar afbuigend) werd bij de volgende klem weer als 'laatst omgezette wissel'
        // gezien en ging opnieuw om, met de loc erop: heen en weer onder de loc. Een wissel die deze regel net zelf terugzette, gaat niet nog eens om.
        // BUG #255 (log 20-10 16:35:39): wissel 2 (bij melder 5/12/13) werd als 'laatst omgezet' teruggezet terwijl de loc op melder 129 en 10 stond: 3 melderstappen verder, dus geen verdachte.
        if (_laatsteWisselCommando is { } lw && !Kruis.IsMotor(lw.Adres) && (_klok.Nu - lw.Tijd).TotalSeconds <= 15 && lw.Bezet.Count > 0 && lw.Bezet.ToHashSet().SetEquals(bezet) && _kaart.LigtNabij(lw.Adres, bezet)
            && !(_teruggezet230 is { } tz && tz.Adres == lw.Adres && lw.Tijd >= tz.Tijd && (_klok.Nu - tz.Tijd).TotalMinutes < 5))
        {
            _log.Waarschuwing($"Wissel {lw.Adres} is {(_klok.Nu - lw.Tijd).TotalSeconds:0.0} s geleden naar {(lw.Afbuigend ? "afbuigend" : "rechtdoor")} gezet terwijl de loc al op melder {Lijst(bezet)} stond; daarvoor stond hij er zonder kortsluiting. Wissel {lw.Adres} gaat eerst terug (BUG #230).");
            _teruggezet230 = (lw.Adres, _klok.Nu);
            await ZetWissel(lw.Adres, !lw.Afbuigend, forceer: true);
            var eindT = _klok.Nu + TimeSpan.FromSeconds(3);
            while (HuidigeAlarmBlokken().Count > 0 && _klok.Nu < eindT) await Wacht(TimeSpan.FromMilliseconds(200));
            if (HuidigeAlarmBlokken().Count == 0)
            {
                _kortsluitingGemeld = false;
                _statischeKortsluiting = false;
                throw new NavigatieFout($"De loc zat klem bij melder {start} doordat wissel {lw.Adres} werd omgezet terwijl de loc er al stond; de wissel staat weer terug. De stap ({stap}) wordt later opnieuw gereden.");
            }
            _log.Info($"Na het terugzetten van wissel {lw.Adres} is de kortsluiting er nog; de andere wissels worden geprobeerd.");
        }
        if (kandidaten.Count == 0) return false;
        _klemProbeActief = true;
        try
        {
            _log.Waarschuwing($"De loc zit klem ({stap}). De verkenner probeert zelf wissels om te zetten (met een korte proefrit per wissel), behalve {(bijLoc.Count > 0 ? "wissel " + string.Join(", ", bijLoc.OrderBy(a => a)) + " (ligt bij een bezette melder), " : "")}de kruismotoren en wat deze rit nodig heeft (nog niet getest op de baan).");
            foreach (var a in kandidaten)
            {
                _token.ThrowIfCancellationRequested();
                bool oud = _wisselStand.TryGetValue(a, out var nu) && nu;
                await ZetWissel(a, !oud, forceer: true);
                var vrij = await VrijheidsProef(r);
                // BUG #173 (Marco 17-10: "de vondst van wissel 1 was puur toeval, hij stond meters van de wissel vandaan, waarschijnlijk timing"): een wissel waarvan de
                // kaart niet weet dat hij bij deze plek ligt wordt niet als oplossing vastgelegd, en zeker niet kort na een Dinamo-uitval of in 'Stap voor stap'.
                if (vrij && (Stap || _klok.Nu - _uitvalHersteldOm < TimeSpan.FromMinutes(15)) && !achterKlem.Contains(a))
                {
                    _log.Waarschuwing($"De loc reed weer nadat wissel {a} was omgezet, maar de kaart weet niet dat wissel {a} bij melder {start} ligt. Dat is vermoedelijk toeval (timing): er wordt niets vastgelegd en wissel {a} gaat terug naar {(oud ? "afbuigend" : "rechtdoor")}.");
                    await ZetWissel(a, oud, forceer: true);
                    _kortsluitingGemeld = false;
                    _statischeKortsluiting = false;
                    return true;
                }
                if (vrij && _kaart.AdressenZonderEffect.Contains(a) && !_kaart.Wissels.Any(w => w.Adres == a && w.Waarnemingen.Count > 0))
                {
                    // BUG #290 (autolog 21-10 16:55:06): "de loc reed weer zonder kortsluiting nadat wissel 3 naar afbuigend is geschakeld"; adres 3 had in deze verkenning nog nooit enig
                    // effect (op de baan bestaat wissel 3 niet). De korte proefrit zelf maakte de loc los. Dat wordt niet aan het adres toegeschreven; het adres gaat terug.
                    _log.Waarschuwing($"De loc reed weer zonder kortsluiting, maar adres {a} heeft in deze verkenning nooit effect gehad: de proefrit zelf maakte de loc waarschijnlijk los. Dit wordt niet als oplossing vastgelegd en adres {a} gaat terug (BUG #290).");
                    _kortsluitingGemeld = false;
                    _statischeKortsluiting = false;
                    await ZetWissel(a, oud, forceer: true);
                    return true;
                }
                if (vrij)
                {
                    _log.Vondst($"De loc reed weer zonder kortsluiting nadat wissel {a} naar {(oud ? "rechtdoor" : "afbuigend")} is geschakeld ({stap}). De wissel blijft zo staan (nog niet getest op de baan).");
                    _kortsluitingGemeld = false;
                    _statischeKortsluiting = false;
                    _alarmStand[a] = !oud;
                    RegistreerAlarmOplossing(a, !oud, bezet);
                    return true;
                }
                await ZetWissel(a, oud, forceer: true);
            }
            _log.Waarschuwing("Geen enkele wissel maakte de loc vrij.");
            return false;
        }
        finally { _klemProbeActief = false; }
    }

    /// <summary>BUG #173: de loc zit klem bij <paramref name="start"/> (rit in richting <paramref name="r"/>). Is de weg tegengesteld aan die rit een geteste overgang met alle wissels
    /// rechtdoor, dan rijdt de loc die kort terug (hij kwam daar vandaan). Beweegt hij zonder alarm, dan is hij vrij. Nog niet getest op de baan.</summary>
    private async Task<bool> ProbeerTerugNaarGetest(int start, Richting r)
    {
        var terug = _kaart.Overgangen.FirstOrDefault(x => x.Van == start && x.Richting == r.Om() && x.AantalKeer > 0 && x.GezienBijConfiguraties.Contains(""));
        if (terug is null) return false;
        _log.Waarschuwing($"De loc zit klem bij melder {start}. Eerst terug het geteste stuk op (richting melder {terug.Naar}, {r.Om().Tekst()}, alle wissels rechtdoor) in plaats van wissels uit te proberen.");
        await ZetConfiguratie(Configuratie.Basis, spaarOnderLoc: true);
        bool vrij = await VrijheidsProef(r.Om());
        _log.Info(vrij ? "De loc reed terug: hij is vrij." : "De loc bewoog niet terug of er was alarm.");
        return vrij;
    }

    /// <summary>BUG #164: stuurt de loc kort weg in richting <paramref name="r"/> en meldt true als hij beweegt zonder dat Dinamo kortsluiting meldt. De loc wordt
    /// daarna altijd gestopt.</summary>
    private async Task<bool> VrijheidsProef(Richting r)
    {
        _monitor.Bijwerken();
        _kortsluitingGemeld = false;
        _negeerKortsluitingTot = DateTime.MinValue;
        var t0 = _klok.Nu;
        bool alarm = false, bewogen = false;
        await StuurSnelheidAlleBlokken(r, _ins.Verkensnelheid);
        var eind = t0 + TimeSpan.FromSeconds(10);
        while (_klok.Nu < eind)
        {
            await Wacht(Poll);
            if (HuidigeAlarmBlokken().Count > 0) { alarm = true; break; }
            if (_monitor.Bijwerken().Any(x => x.Tijd >= t0)) { bewogen = true; break; }
        }
        await VolledigeStop();
        _kortsluitingGemeld = false;
        return bewogen && !alarm && HuidigeAlarmBlokken().Count == 0;
    }

    private async Task<RitResultaat> Kortsluiting(RitResultaat res, string reden, List<int>? alarmBlokken = null)
    {
        int x = res.Reeks[^1];
        _laatsteKortsluitBlokken = HuidigeAlarmBlokken();
        if (_laatsteKortsluitBlokken.Count == 0 && alarmBlokken is { Count: > 0 }) _laatsteKortsluitBlokken = alarmBlokken.ToList();
        // BUG #75: welke andere melder is nu ook bezet? Daar staat de loc met een deel al in: wijst naar de betrokken wissel.
        _laatsteKortsluitMelders = _monitor.Bezet.Where(m => m != x).OrderBy(m => m).ToList();
        res.OokBezetBijKortsluiting = _laatsteKortsluitMelders.ToList();   // BUG #311
        _hw.Noodstop();
        _laatsteKortsluitingOm = _klok.Nu;
        _laatsteKortsluitContext = (x, res.Richting, new Configuratie(_wisselStand.Where(w => w.Value).Select(w => w.Key).ToArray()));
        _laatsteKortsluitReeks = res.Reeks.ToList();
        // BUG #222: koos de loc met de proefwissel de afbuigende tak en reed hij daarna nog verder, dan is die wissel van de puntzijde bereden en kan hij deze kortsluiting niet geven.
        _puntzijdeProef = _ritDoel?.ProefAdres is int pp && ProefWisselVanPuntzijdeBereden(_ritDoel!.Basis, res.Reeks)
            ? (pp, _monitor.Bezet.Append(x).ToHashSet(), _klok.Nu) : null;
        string alarmTekst = _laatsteKortsluitBlokken.Count > 0
            ? $" Dinamo meldt kortsluiting in blok {string.Join(" en ", _laatsteKortsluitBlokken)}."
            : "";
        if (_laatsteKortsluitMelders.Count > 0) alarmTekst += $" Ook bezet op dat moment: melder {string.Join(" en ", _laatsteKortsluitMelders)} (daar zit de loc deels in)." ;
        _log.Waarschuwing($"KORTSLUITING/ONTSPORING vermoed na melder {x} ({res.Richting.Tekst()}): {reden}.{alarmTekst} Noodstop.");
        // BUG #81: reageerde Dinamo op dat moment nog?
        if (StilteWaarschuwing(_hw) is { } stiltetekst) _log.Waarschuwing(stiltetekst);
        if (_laatsteWisselCommando is { } lw && (_klok.Nu - lw.Tijd).TotalSeconds <= 8 && lw.Bezet.Count > 0)
        {
            var onderToen = _kaart.Wissels.Where(w => w.Adres == lw.Adres)
                .SelectMany(w => w.Waarnemingen).Where(o => lw.Bezet.Contains(o.NaMelder) &&
                    (o.Soort == WaarnemingSoort.KortsluitingBijOmzetten || o.Soort == WaarnemingSoort.LostKortsluitingOp)).Any();
            _log.Waarschuwing($"Let op: {(_klok.Nu - lw.Tijd).TotalSeconds:0.0} s voor deze kortsluiting is wissel {lw.Adres} naar {(lw.Afbuigend ? "afbuigend" : "rechtdoor")} geschakeld terwijl melder {string.Join(" en ", lw.Bezet)} bezet was." +
                (onderToen ? $" Wissel {lw.Adres} ligt bij die melder: de loc stond er waarschijnlijk op of tegen de tong aan en is daardoor ontspoord of kortgesloten." : ""));
        }
        await VolledigeStop();
        res.Einde = RitEinde.Kortsluiting;
        _kortsluitingenTotaal++;
        if (!await HerstelBekendeWisselstand(res))
            await HerstelNaKortsluiting(x, res.Richting);
        return res;
    }

    /// <summary>Probeert de loc na een kortsluiting zelf terug te laten rijden naar de
    /// laatste melder. Lukt dat niet, dan wordt de gebruiker gevraagd hem terug te zetten.</summary>
    /// <summary>BUG #141 (log 14-10 15:30-15:32): de kortsluiting na wissel 2 afbuigend kwam steeds terug terwijl de loc stilstond (Dinamo zette de
    /// stroom elke ~20 s terug en sloot hem weer af, 4 keer in 95 s) en het terugrijden/neerzetten had geen enkele kans. Het hielp pas toen wissel 2 terug naar
    /// rechtdoor ging (kortsluiting 1 s later weg). Komt de kortsluiting terug bij een stilstaande loc en is de wissel die de rit zelf proefde (afbuigend) de
    /// verdachte, dan gaat die meteen terug naar rechtdoor. Kruismotoren blijven buiten schot (#96). De partnerzoektocht zet hem zo nodig weer afbuigend.</summary>
    /// <summary>BUG #222: de proefwissel van de laatste kortsluiting als die van de puntzijde bereden is (zie <see cref="ProefWisselVanPuntzijdeBereden"/>), met de melders waar de loc toen stond.</summary>
    private (int Adres, HashSet<int> Melders, DateTime Tijd)? _puntzijdeProef;

    /// <summary>BUG #222 (log 18-10 12:07:21-12:07:39): rit 13 -> 14 -> 129 -> 24 met wissel 5 afbuigend (de basisrit ging 13 -> 14 -> 24). De loc nam bij wissel 5 de afbuigende tak (129) en
    /// reed daarna nog een melder verder; de kortsluiting kwam pas na 24 (wissel 10, de samenvoeging). Wissel 5 is dus van de puntzijde bereden; terugzetten helpt niet (BUG #169) en de loc
    /// heeft die stand juist nodig om terug te rijden. True als de reeks van de basis afwijkt en na de eerste afwijkende melder nog minstens één melder volgt.</summary>
    internal static bool ProefWisselVanPuntzijdeBereden(IReadOnlyList<int>? basis, IReadOnlyList<int> reeks)
    {
        if (basis is null || reeks.Count < 3) return false;
        int d = -1;
        for (int i = 0; i < reeks.Count; i++)
            if (i >= basis.Count || reeks[i] != basis[i]) { d = i; break; }
        return d >= 1 && reeks.Count - 1 > d;
    }

    /// <summary>BUG #222: geldt <see cref="_puntzijdeProef"/> nog (zelfde plek, minder dan 10 minuten)?</summary>
    private int? PuntzijdeProefNu()
        => _puntzijdeProef is { } pz && (_klok.Nu - pz.Tijd).TotalMinutes < 10 && _monitor.Bezet.Any(pz.Melders.Contains) ? pz.Adres : null;

    /// <summary>BUG #241: wissels die <see cref="LosMakenProefWissel"/> bij dit herstel terugzette (om achteraf te melden of het vermoeden klopte).</summary>
    private readonly List<int> _zojuistLosgemaakt = new();

    /// <summary>BUG #256 (autolog 20-10 17:53:45 en 17:59:17): na een proef of opdracht zette de terugweg een wissel om die bij de melder ligt waar de loc stilstond (wissel 9 met de loc op
    /// melder 129, wissel 13 met de loc op 24); de loc is langer dan zo'n melder en stond op de tong: direct kortsluiting ("de loc kan niet weg"), en bij het zelf terugrijden daarna gebeurde
    /// hetzelfde nog eens. Niet omzetten (de spaarregels) laat de loc de verkeerde kant op rijden (17:56:17: naar 4 in plaats van 24). Wat een mens doet: eerst een stukje terug, van de wissel
    /// af, dan omzetten en weer vooruit. Moet voor deze stap een wissel om die bij een bezette melder ligt, dan rijdt de loc eerst met de huidige stand terug naar de vorige melder (een bekende,
    /// eenduidige overgang met deze stand), tot de huidige melder vrij is. Geeft de nieuwe start en het verlengde pad, of null als dat niet nodig of niet mogelijk is. Nog niet getest op de baan.</summary>
    private async Task<(int Van, List<int> Pad)?> UitwijkenVoorWissels(int van, Richting r, List<int> pad, Configuratie cfg)
    {
        _monitor.Bijwerken();
        var bezet = _monitor.Bezet.ToHashSet();
        if (bezet.Count == 0 || !bezet.Contains(van) || HuidigeAlarmBlokken().Count > 0) return null;
        var teOm = WisselsBijBezet(_kaart, bezet).Where(a => !Kruis.IsMotor(a) && _ins.WisselAdressen().Contains(a)
            && _wisselStand.TryGetValue(a, out var st) && st != cfg.Bevat(a)).OrderBy(a => a).ToList();
        if (teOm.Count == 0) return null;
        var nu = new Configuratie(_wisselStand.Where(kv => kv.Value).Select(kv => kv.Key).ToArray());
        var terug = _kaart.BekendeVolgende(van, r.Om(), nu).Where(x => !pad.Contains(x)).ToList();
        // BUG #281 (autolog 21-10 15:31:28-15:33:45): "Wissel 14 moet om voor de stap vanaf melder 8 ... er is geen eenduidige melder om eerst naar terug te rijden", terwijl vanaf 8 vooruit
        // alleen 15 bekend is. De overgang 8 -> 15 was nooit met precies deze wisselstand gereden. Is er bij deze stand niets bekend, dan geldt een vervolg dat bij elke stand het enige is.
        if (terug.Count == 0)
        {
            var alle = _kaart.BekendeVolgende(van, r.Om()).Where(x => !pad.Contains(x)).ToList();
            if (alle.Count == 1) terug = alle;
        }
        // BUG #291 (simulatie keerlus 14:31:44 en baan 21-10 15:20:18): vanaf 129 zijn achteruit 4, 10 en 14 bekend; welke de loc neemt, hangt af van de huidige stand van wissel 5/6/9.
        // Met de vervolgen die bij de huidige stand passen (#269) is er vaak toch één.
        if (terug.Count != 1)
        {
            var bijStand = VerwachteVolgende(_kaart, van, r.Om(), a => _wisselStand.TryGetValue(a, out var st2) && st2).Where(x => !pad.Contains(x)).ToList();
            if (bijStand.Count == 1) terug = bijStand;
        }
        if (terug.Count != 1)
        {
            _log.Info($"Wissel {Lijst(teOm)} moet om voor de stap vanaf melder {van}, maar ligt bij de melder waar de loc staat; er is geen eenduidige melder om eerst naar terug te rijden.");
            return null;
        }
        int p = terug[0];
        if (WisselsBijBezet(_kaart, new[] { p }).Intersect(teOm).Any()) return null;   // de wissel ligt dan tussen p en van: daar staat de loc er na het terugrijden nog op
        _log.Info($"Wissel {Lijst(teOm)} moet om voor de stap vanaf melder {van}, maar ligt bij de melder waar de loc staat (de loc kan op de tong staan). Eerst terug naar melder {p} met de huidige stand, dan omzetten (BUG #256, nog niet getest op de baan).");
        await Rit(van, r.Om(), new RitDoel { Configuratie = nu, DoelMelder = p, VerwachtPad = new List<int> { van, p } });
        _monitor.Bijwerken();
        if (_monitor.Bezet.Contains(van)) await Nastellen(p, r.Om(), achter: van);
        _monitor.Bijwerken();
        if (_monitor.Bezet.Contains(van) || !_monitor.Bezet.Contains(p)) return null;
        var nieuwPad = new List<int> { p };
        nieuwPad.AddRange(pad[0] == van ? pad : new List<int> { van }.Concat(pad));
        return (p, nieuwPad);
    }

    private async Task<bool> LosMakenProefWissel()
    {
        // BUG #150: blijft het alarm ook na de stop aan (Dinamo meldt niet opnieuw), dan telt dat ook als vastzittende loc.
        if (!_statischeKortsluiting && HuidigeAlarmBlokken().Count == 0) return false;
        // Verdacht: de proefwissel van de rit, of (zonder proef) de afbuigende wissels van de rijstand van deze rit.
        var kandidaten = _ritDoel?.ProefAdres is int p0 ? new List<int> { p0 }
            : (_ritDoel?.Configuratie.Afbuigend.ToList() ?? new List<int>());
        kandidaten = kandidaten.Where(a => !IsVolger(a) && _wisselStand.TryGetValue(a, out var afb) && afb).ToList();   // BUG #259
        // BUG #258: zonder proefwissel geen wissel losmaken waarvan de kaart de afbuigende stand voor deze stap al bewezen heeft (wissel 5 bij 14 -> 129)
        if (_ritDoel?.ProefAdres is null)
        {
            _monitor.Bijwerken();
            var bew = BewezenStandenNu(_monitor.Bezet);
            var bewLos = kandidaten.Where(a => bew.TryGetValue(a, out var b) && b).ToList();
            if (bewLos.Count > 0)
            {
                kandidaten = kandidaten.Except(bewLos).ToList();
                _log.Info($"Wissel {Lijst(bewLos)} wordt niet losgemaakt: afbuigend is voor deze stap al bewezen (BUG #258).");
            }
        }
        // BUG #261 (Marco 20-10 18:46: "je weet heel goed waar wissel 10 ligt en je rijdt vol in een kortsluitmelding die je niet direct kon oplossen"; autolog 18:24:59-18:25:09): bij de
        // kortsluiting na 14 -> 24 met wissel 1, 9 en 10 afbuigend gingen alle drie terug, terwijl de kaart wist dat 14 -> 24 met wissel 10 afbuigend kortsluiting geeft. Is een van de
        // verdachten een bekende verboden ingang voor de laatste stap, dan gaat alleen die terug.
        var bekend261 = BekendeVerbodenVoorLaatsteStap().Where(kandidaten.Contains).ToList();
        if (bekend261.Count > 0 && bekend261.Count < kandidaten.Count)
        {
            // BUG #304 (simulatie): de stap kan ook uit de laatste rit komen terwijl de kortsluitreeks maar één melder heeft; [^2] gaf dan 'Index was out of range' en de verkenning stopte.
            string stap304 = _laatsteKortsluitReeks.Count >= 2 ? $" ({_laatsteKortsluitReeks[^2]} -> {_laatsteKortsluitReeks[^1]})" : "";
            _log.Info($"Alleen wissel {Lijst(bekend261)} gaat terug: de kaart weet dat de laatste stap{stap304} met die wissel afbuigend kortsluiting geeft (BUG #261).");
            kandidaten = bekend261;
        }
        // BUG #272 (autolog 21-10 12:31:55, Marco 12:41: "het probleem lag bij wissel 5"): met de loc op 24 en 129 ging wissel 5 'los' naar rechtdoor; volger 6 ligt bij 129 en schakelde mee
        // onder de loc. Een meester waarvan de volger bij een bezette melder ligt, wordt niet losgemaakt.
        foreach (var vk in kandidaten.Where(a => VolgerOnderLoc(a) is not null).ToList())
        {
            kandidaten.Remove(vk);
            _log.Info($"Wissel {vk} wordt niet losgemaakt: wissel {VolgerOnderLoc(vk)} (overloopset) schakelt mee en ligt bij de melder waar de loc staat ({Lijst(_monitor.Bezet)}) (BUG #272).");
        }
        if (PuntzijdeProefNu() is int pz && kandidaten.Remove(pz))
            _log.Info($"Wissel {pz} wordt niet losgemaakt: de loc nam bij die wissel de afbuigende tak en reed daarna verder ({Lijst(_laatsteKortsluitReeks)}), dus hij is van de puntzijde bereden; omzetten helpt dan niet (BUG #169) en de loc heeft die stand nodig om terug te rijden.");
        if (kandidaten.Count == 0) return false;
        bool iets = false;
        foreach (var pa in kandidaten)
        {
            // BUG #150: een kruismotor mag nooit zomaar onder de loc om (#96), maar de verkenner zette hem zojuist zelf als proef en de loc zit klem
            // (log 14:38: terugzetten haalde de kortsluiting weg). Daarom eenmalig vragen; zonder vraagvenster of bij Nee gebeurt er niets.
            if (Kruis.IsMotor(pa))
            {
                if (VraagJaNee is null || _kruisTerugzettenGevraagd.Contains(pa)) continue;
                _kruisTerugzettenGevraagd.Add(pa);
                var toe = await VraagJaNee("Kruismotor terugzetten onder de loc?",
                    $"De loc zit klem op de kruiswissel en de kortsluiting komt steeds terug. De verkenner zette motor {pa} zelf als proef op afbuigend.\n\nJa = motor {pa} gaat nu terug naar rechtdoor (de loc staat erop).\nNee = er verandert niets; zet de loc zelf terug.");
                if (toe != true) continue;
            }
            _statischeKortsluiting = false;
            // BUG #241 (autolog 18-10 15:06): de melding zei stellig "hij zit klem op wissel 13", maar het bleek wissel 14 (13 terugzetten hielp niet). Het is een vermoeden:
            // de afbuigende wissel van deze rit is de eerste verdachte. Of het klopte, meldt HerstelNaKortsluiting daarna.
            _log.Waarschuwing($"De kortsluiting komt steeds terug terwijl de loc stilstaat: waarschijnlijk zit hij klem op wissel {pa} (afbuigend gezet voor deze rit). Die wissel gaat nu terug naar rechtdoor om de loc vrij te maken; anders blijft Dinamo de stroom steeds even terugzetten en weer afsluiten.");
            await ZetWissel(pa, false, forceer: true);
            _losgemaakteProefWissels[pa] = _monitor.Bezet.ToHashSet();
            _zojuistLosgemaakt.Add(pa);
            iets = true;
        }
        if (!iets) return false;
        await Wacht(TimeSpan.FromSeconds(2));
        _kortsluitingGemeld = false;
        return true;
    }

    private async Task HerstelNaKortsluiting(int melder, Richting richtingVanRit)
    {
        var terug = richtingVanRit.Om();
        for (int poging = 1; poging <= 2; poging++)
        {
            _zojuistLosgemaakt.Clear();
            await LosMakenProefWissel();
            await Wacht(TimeSpan.FromSeconds(1));
            // BUG #241: zeggen of het vermoeden klopte, zodat het logboek niet blijft beweren dat de loc op die wissel klem zat
            if (_zojuistLosgemaakt.Count > 0)
                _log.Info(HuidigeAlarmBlokken().Count > 0
                    ? $"Wissel {Lijst(_zojuistLosgemaakt)} terugzetten haalde de kortsluiting niet weg: de loc zit daar niet klem. De andere wissels worden geprobeerd."
                    : $"Wissel {Lijst(_zojuistLosgemaakt)} terugzetten haalde de kortsluiting weg: de loc zat daar klem.");
            // BUG #150: staat de loc stil en blijft het alarm, dan zit hij klem op een wissel (meestal de samenvoegende partner van de proefwissel,
            // bv. wissel 1 bij wissel 2): eerst wissels proberen (de ene na de andere, bij de loc alleen met toestemming), niet blind terugrijden.
            if (!_alarmWegProbeerd && HuidigeAlarmBlokken().Count > 0)
            {
                _alarmWegProbeerd = true;
                try { if (await ProbeerAlarmWegTeKrijgen("herstel na kortsluiting")) { _kortsluitingGemeld = false; } }
                finally { _alarmWegProbeerd = false; }
                // BUG #150: haalde geen enkele wissel het alarm weg, dan is blind achteruitrijden zinloos (de loc staat klem en rijdt nergens heen of
                // schuift met alarm verder); meteen de gebruiker vragen in plaats van nog een poging die de loc alleen verder laat zakken.
                if (HuidigeAlarmBlokken().Count > 0) break;
            }
            _kortsluitingGemeld = false;
            // BUG #144b (Marco 16:54, log 16:51): de loc stond na de kortsluiting op melder 5 en met zijn achterkant nog op melder 16 (de loc is langer dan
            // een melder). 'Alleen op melder 5' kan door terugrijden nooit bereikt worden: de loc reed 16 en 5 voorbij naar melder 15, bleef midden op het
            // rechte stuk staan en de gebruiker moest hem terugzetten. Is het alarm weg en staat de loc nog precies waar hij bij de kortsluiting stond
            // (melder x en alleen de melders die toen ook bezet waren), dan blijft hij staan.
            _monitor.Bijwerken();
            var nuBezet = _monitor.Bezet;
            if (nuBezet.Contains(melder) && nuBezet.Count > 1 && HuidigeAlarmBlokken().Count == 0
                && nuBezet.All(m => m == melder || _laatsteKortsluitMelders.Contains(m)))
            {
                _log.Info($"Loc staat nog op melder {melder} en deels op melder {string.Join(" en ", nuBezet.Where(m => m != melder).OrderBy(m => m))} (de loc staat op de grens van twee melders) en het alarm is weg: hij blijft staan waar hij staat.");
                return;
            }
            // BUG #150: staat de loc al precies op de melder (alleen die bezet) en is het alarm weg, dan blijft hij staan; terugrijden zou hem
            // voorbij de melder laten rijden (simulatie: na de kortsluiting bij melder 12 reed de loc door naar 13 en moest worden teruggezet).
            if (nuBezet.Count == 1 && nuBezet.Contains(melder) && HuidigeAlarmBlokken().Count == 0)
            {
                _log.Info($"Loc staat al op melder {melder} en het alarm is weg: hij blijft staan.");
                return;
            }
            _negeerKortsluitingTot = _klok.Nu + TimeSpan.FromSeconds(12);
            _log.Rijden($"Herstel: loc {terug.Tekst()} terug naar melder {melder} (poging {poging}).");
            // Naar alle blokken: de loc staat op een stuk waarvan het blok onbekend is.
            if (BlokVereist)
            {
                var blokken = _ins.DinamoBlokLijst();
                foreach (var b in blokken) _hw.ZetLocSnelheid(_ins.LocAdres, _ins.Kruipsnelheid, terug == Richting.Vooruit, b, _ins.LocStappen);
                await Wacht(DinamoCyclus * blokken.Count);
            }
            else await StuurSnelheid(terug, _ins.Kruipsnelheid);

            var eind = _klok.Nu + TimeSpan.FromSeconds(10);
            bool terugOpMelder = false;
            while (_klok.Nu < eind)
            {
                await Wacht(Poll);
                _monitor.Bijwerken();
                var bezet = _monitor.Bezet;
                if (bezet.Count == 1 && bezet.Contains(melder)) { terugOpMelder = true; break; }
            }
            await VolledigeStop();
            _kortsluitingGemeld = false;
            if (terugOpMelder || _monitor.IsBezet(melder))
            {
                await Nastellen(melder, terug);
                _log.Info($"Loc staat weer op melder {melder}.");
                return;
            }
        }
        await VraagLocTerugTeZetten(melder, $"Er was een kortsluiting of ontsporing direct na melder {melder} en de loc kon niet zelf terugrijden.");
    }

    /// <summary>BUG #79: vraagt of er een wissel verkeerd staat; zo ja welke, en schakelt die meteen (opnieuw). Geeft true als de loc daarna
    /// al precies op <paramref name="melder"/> staat (dan hoeft hij niet verplaatst te worden).</summary>
    private async Task<bool> ControleerVerkeerdeWissel(int melder, string aanleiding)
    {
        if (VraagJaNee is null || VraagWisselAdres is null) return false;
        bool geschakeld = false;
        while (true)
        {
            var afb = _wisselStand.Where(kv => kv.Value).Select(kv => kv.Key).OrderBy(x => x).ToList();
            string verdacht = afb.Count > 0 ? $"\n\nDe verkenner heeft deze wissels op AFBUIGEND gezet en niet teruggezet: {string.Join(", ", afb)}. Dat zijn de eerste verdachten." : "";
            var ja = await VraagJaNee("Staat er een wissel verkeerd?",
                $"{aanleiding}{verdacht}\n\nStaat er een wissel verkeerd (open gereden, of een motor die niet goed omgeschakeld is)?\n\nJa = je geeft het wisseladres op en de verkenner schakelt die wissel meteen.\nNee = je zet de loc met de hand terug.\nAnnuleren = de verkenning stopt.");
            if (ja is null) throw new OperationCanceledException("Gebruiker stopte bij het terugzetten van de loc.");
            if (ja == false) break;
            var adres = await VraagWisselAdres("Welke wisselmotor?", $"Welk wisseladres staat verkeerd? ({_ins.WisselAdressen().First()} t/m {_ins.WisselAdressen().Last()})");
            if (adres is not int a) continue;
            if (!_ins.WisselAdressen().Contains(a))
            {
                _log.Waarschuwing($"Adres {a} ligt niet in het geteste bereik ({_ins.WisselAdressen().First()} t/m {_ins.WisselAdressen().Last()}); genegeerd.");
                continue;
            }
            // De gebruiker zegt dat deze wissel VERKEERD staat: dus altijd naar de tegenovergestelde stand schakelen (niet opnieuw dezelfde
            // stand sturen). Bekend is de stand die het programma het laatst stuurde; anders vragen we welke stand hij nu heeft.
            bool afbuigend;
            if (_wisselStand.TryGetValue(a, out var bekend)) afbuigend = !bekend;
            else
            {
                var nu = await VraagJaNee("Welke stand staat nu?", $"Staat wissel {a} nu op AFBUIGEND?\n\nJa = hij staat afbuigend (wordt rechtdoor gezet)\nNee = hij staat rechtdoor (wordt afbuigend gezet)");
                if (nu is null) throw new OperationCanceledException("Gebruiker stopte bij het terugzetten van de loc.");
                afbuigend = nu != true;
            }
            _log.Info($"Op verzoek van de gebruiker staat wissel {a} verkeerd en wordt naar {(afbuigend ? "afbuigend" : "rechtdoor")} geschakeld (tegenovergestelde stand).");
            await ZetWissel(a, afbuigend, forceer: true);
            geschakeld = true;
            // BUG #277 (autolog 21-10 12:44:08-12:44:20): wissel 14 werd op verzoek omgezet en meteen vastgelegd als oplossing van kortsluitpunt #2, maar Dinamo bleef kortsluiting in blok 4 melden.
            // Blijft het alarm na het omzetten (3 s), dan is dit geen oplossing en wordt er niets vastgelegd.
            if (_hw is IBlokAlarmBron)
            {
                var eind277 = _klok.Nu + TimeSpan.FromSeconds(3);
                while (HuidigeAlarmBlokken().Count > 0 && _klok.Nu < eind277) await Wacht(TimeSpan.FromMilliseconds(300));
                if (HuidigeAlarmBlokken().Count > 0)
                {
                    _log.Waarschuwing($"Wissel {a} is op verzoek naar {(afbuigend ? "afbuigend" : "rechtdoor")} gezet, maar Dinamo meldt nog steeds kortsluiting in blok {string.Join(" en ", HuidigeAlarmBlokken())}: dit wordt niet als oplossing van de kortsluiting vastgelegd (BUG #277).");
                    continue;
                }
            }
            // BUG #87 (Marco 13:01: "telkens zet ik wissel 1 in de juiste stand en dan is de kortsluiting weg, waarom wordt dit niet vastgehouden?"):
            // dat de gebruiker deze wissel na een kortsluiting corrigeerde is de oplossing van dat kortsluitpunt, en wordt voortaan toegepast.
            // BUG #132 (kaart 14-10 13:23): de oplossing hoort bij het kortsluitpunt van de lopende kortsluiting (zelfde melder en richting), niet bij het
            // laatste onopgeloste punt. BUG #136 (log 14:09:58): dat punt bestaat meestal al (de rit is dan afgelopen en geregistreerd); alleen als het er nog niet is,
            // wacht de oplossing tot RegistreerKortsluitpunt het aanmaakt.
            if (_laatsteKortsluitContext is { } kc && (_klok.Nu - _laatsteKortsluitingOm).TotalMinutes < 15)
            {
                var kpBestaand = ZoekKortsluitpunt(kc.Melder, kc.Richting, kc.Stand);
                if (kpBestaand is not null) KoppelHandOplossing(kpBestaand, a, afbuigend);
                else
                {
                    _wachtendeHandOplossing = (kc.Melder, kc.Richting, a, afbuigend, _klok.Nu);
                    _log.Info($"Wissel {a} op {(afbuigend ? "afbuigend" : "rechtdoor")} geldt als oplossing van de kortsluiting na melder {kc.Melder} ({kc.Richting.Tekst()}); wordt bij dat kortsluitpunt vastgelegd.");
                }
            }
        }
        if (!geschakeld) return false;
        _kortsluitingGemeld = false;
        if (_hw.KanMelderStatusOpvragen)
        {
            _hw.VraagMelderStatusOp(melder);
            foreach (var m in _monitor.Bezet) _hw.VraagMelderStatusOp(m);
        }
        await Wacht(TimeSpan.FromSeconds(2));
        _monitor.Bijwerken();
        var bezet = _monitor.Bezet;
        if (bezet.Count == 1 && bezet.Contains(melder))
        {
            _log.Info($"De loc staat al op melder {melder}; verder zonder de loc te verplaatsen.");
            return true;
        }
        return false;
    }

    /// <summary>Vraagt de gebruiker de loc met de hand op een melder te zetten, en
    /// controleert daarna of die melder echt (alleen) bezet is.</summary>
    /// <summary>BUG #96: waar de neus van de loc heen moet wijzen. De verkenner rekent 'vooruit' = de kant van de melders die de kaart na deze melder
    /// vooruit kent; de loc moet er dus met de neus naar toe staan, anders rijdt hij bij 'vooruit' de andere kant op.</summary>
    internal static string OrientatieTekst(Baankaart kaart, int melder)
    {
        var vooruit = kaart.BekendeVolgende(melder, Richting.Vooruit);
        var achteruit = kaart.BekendeVolgende(melder, Richting.Achteruit);
        string Lijst2(List<int> l) => string.Join(" of ", l);
        if (vooruit.Count > 0 && achteruit.Count > 0)
            return $"RIJRICHTING: de NEUS van de loc naar melder {Lijst2(vooruit)} en de achterkant naar melder {Lijst2(achteruit)} (zo reed hij ook: 'vooruit' gaat van melder {Lijst2(achteruit)} naar {Lijst2(vooruit)}).";
        if (vooruit.Count > 0) return $"RIJRICHTING: de NEUS van de loc naar melder {Lijst2(vooruit)}.";
        if (achteruit.Count > 0) return $"RIJRICHTING: de ACHTERKANT van de loc naar melder {Lijst2(achteruit)} (de neus de andere kant op).";
        return "Zet de loc in dezelfde rijrichting als waarmee de verkenning gestart is.";
    }

    private string OrientatieTekst(int melder) => OrientatieTekst(_kaart, melder);

    /// <summary>BUG #103 (Marco 08:07: "alweer een paar keer de loc op melder 13 moeten terugzetten"; log 08:01): na een mislukte opdracht werd direct
    /// om hulp gevraagd, terwijl de kortsluiting twee seconden na het terugzetten van de wissels vanzelf weg was (hardwarelog 08:01:27) en de loc
    /// gewoon over de bekende route terug had gekund. Nu: wissels staan al op basis; wacht tot het alarm weg is; staat de loc op precies één melder
    /// van de route (of al thuis), dan rijdt de verkenner zelf terug. Lukt dat niet (alarm blijft, loc andersom, plek niet op de route, nieuwe
    /// fout), dan volgt als vanouds de vraag aan de gebruiker.</summary>
    private async Task<bool> ProbeerZelfTerugNaarStart(List<Etappe> route, NavigatieFout nf)
    {
        if (Stap) return await ProbeerZelfNaarMelder(RouteDoel(route));   // BUG #172
        if (nf.LocAndersom) return false;
        var eind = _klok.Nu + TimeSpan.FromSeconds(10);
        while (HuidigeAlarmBlokken().Count > 0 && _klok.Nu < eind) await Wacht(TimeSpan.FromMilliseconds(300));
        if (HuidigeAlarmBlokken().Count > 0)
        {
            _log.Info($"De kortsluiting in blok {string.Join(" en ", HuidigeAlarmBlokken())} blijft; de verkenner rijdt niet zelf terug.");
            return false;
        }
        await Wacht(TimeSpan.FromSeconds(1));
        _monitor.Bijwerken();
        var bezet = _monitor.Bezet;
        // BUG #302 (autolog 25-10 09:31:43): na 'onverwacht melder 4' stond de loc op de grens van 4 en 129 en vroeg de verkenner om hulp. De loc is niet langer dan een melder
        // (Marco 21-10): twee bezette melders = op de grens. Hij kruipt nu in dezelfde richting door tot alleen de onverwachte melder bezet is en rijdt dan zelf terug.
        if (bezet.Count == 2 && nf.Onverwacht is int ov302 && bezet.Contains(ov302) && nf.RitRichting is Richting rr302 && !Kruis.OpKruis(bezet))
        {
            int ander302 = bezet.First(m => m != ov302);
            _log.Info($"De loc staat op de grens van melder {ander302} en {ov302}: hij kruipt {rr302.Tekst()} door tot alleen melder {ov302} bezet is (BUG #302, nog niet getest op de baan).");
            await Nastellen(ov302, rr302, achter: ander302);
            _monitor.Bijwerken();
            bezet = _monitor.Bezet;
            if (HuidigeAlarmBlokken().Count > 0) { _log.Info("Bij het doorkruipen kwam er een kortsluiting; de verkenner rijdt niet zelf terug."); return false; }
        }
        if (bezet.Count != 1) { _log.Info($"De loc staat niet op precies één melder (bezet: {Lijst(bezet)}); de verkenner rijdt niet zelf terug."); return false; }
        int x = bezet.Single();
        var idx = ZelfTerugIndex(route, x, _kaart.StartMelder);
        if (idx is null)
        {
            // BUG #106 (log 09:06): een opdracht die op de startmelder zelf begint heeft een lege route, dus "ligt niet op de route terug" terwijl
            // de loc op de bekende basis-lus stond. Dan de kortste bekende weg met alle wissels rechtdoor.
            // BUG #164: een melder waar de loc net onverwacht terechtkwam (bijv. 12 na een klem-oplossing) staat nog niet als overgang op de kaart.
            // De loc rijdt dan dezelfde weg terug (andere richting, zelfde wisselstand) naar de laatste melder die klopte en gaat vandaar verder.
            var voorstap = new List<(int Van, int Naar, Richting Richting, Configuratie Cfg)>();
            int vanaf = x;
            if (nf.Onverwacht == x && nf.LaatsteGoed is int lg && lg != x && nf.RitRichting is Richting rr && nf.RitConfiguratie is Configuratie rc)
            {
                // Wat de loc daar vrij kreeg (kortsluitoplossing, bijv. wissel 1 afbuigend) moet ook op de terugweg zo staan: dezelfde klem zit er andersom ook.
                var terugCfg = rc;
                foreach (var ko in _kaart.AlleKortsluitOplossingen().Where(o => o.NaMelder == lg && o.Richting == rr)) terugCfg = terugCfg.MetStand(ko.Adres, ko.Afbuigend);
                voorstap.Add((x, lg, rr.Om(), terugCfg)); vanaf = lg;
            }
            // BUG #232: met een voorstap (x -> lg) komt de loc op lg aan vanaf x; keren op lg mag dan alleen terug naar x.
            int? aankVia = voorstap.Count > 0 ? x : _ingang is { } ing232 && ing232.Melder == x ? ing232.Via : null;
            Richting? aankR = voorstap.Count > 0 ? voorstap[^1].Richting : _ingang is { } ing232b && ing232b.Melder == x ? ing232b.Richting : null;
            var basisWeg = ZelfTerugWeg(_kaart, vanaf, _kaart.StartMelder);
            if (basisWeg is { Count: > 0 } bw && aankVia is int av && aankR is Richting ar && !KerenToegestaan(av, ar, bw[0].Naar, bw[0].Richting)) basisWeg = null;
            var restWeg = basisWeg?.Select(w => (w.Van, w.Naar, w.Richting, Cfg: Configuratie.Basis)).ToList()
                      ?? ZelfTerugWegAlleStanden(_kaart, vanaf, _kaart.StartMelder, aankVia, aankR);
            var weg = restWeg is null ? null : voorstap.Concat(restWeg).ToList();
            if (voorstap.Count > 0) basisWeg = null;
            if (weg is null) { _log.Info($"Melder {x} ligt niet op de route terug en er is geen bekende weg naar melder {_kaart.StartMelder}; de verkenner rijdt niet zelf terug."); return false; }
            _log.Info($"De kortsluiting is weg en de loc staat op melder {x}: de verkenner rijdt zelf terug naar melder {_kaart.StartMelder} over de bekende {(basisWeg is null ? "weg (met de wisselstanden waarmee die gezien is)" : "basisweg")} ({Lijst(new[] { x }.Concat(weg.Select(s => s.Naar)).ToList())}) (niet getest op de baan).");
            _kortsluitingGemeld = false;
            try
            {
                int k = 0;
                while (k < weg.Count)
                {
                    var r = weg[k].Richting;
                    var stapCfg = weg[k].Cfg;
                    var pad = new List<int> { weg[k].Van };
                    while (k < weg.Count && weg[k].Richting == r && weg[k].Cfg.Sleutel == stapCfg.Sleutel) { pad.Add(weg[k].Naar); k++; }
                    var cfg = MetEisen(pad, r, KiesBijIngang(pad[0], r, pad, stapCfg));
                    await ControleerGeenAlarmVoorStap($"terugweg {pad[0]} -> {pad[^1]}");
                    if (await UitwijkenVoorWissels(pad[0], r, pad, cfg) is { } uw) { pad = uw.Pad; cfg = MetEisen(pad, r, cfg); }   // BUG #256
                    await ZetConfiguratie(cfg);
                    var terug = await Rit(pad[0], r, new RitDoel { Configuratie = cfg, DoelMelder = pad[^1], VerwachtPad = pad });
                    if (terug.Einde == RitEinde.Kortsluiting) throw new NavigatieFout($"Op de terugweg naar melder {pad[^1]} volgde een kortsluiting.");
                }
            }
            catch (NavigatieFout f)
            {
                _log.Waarschuwing($"Zelf terugrijden mislukte: {f.Message}");
                await ZetConfiguratie(Configuratie.Basis, spaarOnderLoc: true);
                return false;
            }
            await ZetConfiguratie(Configuratie.Basis, spaarOnderLoc: true);   // BUG #275: niet onder de loc (wissel 5 bij 14/129, 12:38:36)
            await Wacht(TimeSpan.FromSeconds(1));
            _monitor.Bijwerken();
            idx = -1;
        }
        if (idx.Value >= 0)
        {
            _log.Info($"De kortsluiting is weg en de loc staat op melder {x}: de verkenner rijdt zelf terug naar melder {_kaart.StartMelder} (niet getest op de baan).");
            _kortsluitingGemeld = false;
            try { await TerugNaarHuis(route.Take(idx.Value + 1).ToList()); }
            catch (NavigatieFout f)
            {
                _log.Waarschuwing($"Zelf terugrijden mislukte: {f.Message}");
                await ZetConfiguratie(Configuratie.Basis, spaarOnderLoc: true);
                return false;
            }
            await Wacht(TimeSpan.FromSeconds(1));
            _monitor.Bijwerken();
        }
        var nu = _monitor.Bezet;
        if (nu.Count == 1 && nu.Contains(_kaart.StartMelder)) { _log.Info($"Loc staat weer op melder {_kaart.StartMelder}."); return true; }
        _log.Info($"Na het terugrijden staat de loc niet (alleen) op melder {_kaart.StartMelder} (bezet: {Lijst(nu)}).");
        return false;
    }

    private async Task VraagLocTerugTeZetten(int melder, string aanleiding, bool zelfEerst = true)
    {
        // BUG #305: eerst maximaal 10 minuten zelf oplossen, pas daarna de gebruiker. Komt deze vraag van binnen het zelf oplossen, dan geen vraag: die ronde is mislukt.
        if (_zelfOplossenBezig) throw new NavigatieFout($"{EersteRegel(aanleiding)} (het zelf oplossen gaat door)");
        if (zelfEerst && await ZelfOplossen(melder, aanleiding)) return;
        for (int poging = 1; poging <= 3; poging++)
        {
            // BUG #94 (Marco 15:46: "ik zette de loc terug in melder 13 en hij reed gelijk weg"): Dinamo onthoudt per blok het laatste rijcommando.
            // De stop ging alleen naar de blokken waar de loc net stond; blok 3 had nog "achteruit 12" van de rit eerder (hardwarelog 15:39:07),
            // dus een loc die daar met de hand neergezet wordt rijdt meteen weg. Daarom vóór elke vraag eerst snelheid 0 naar ALLE blokken.
            if (BlokVereist)
            {
                _log.Info("Voor het terugzetten van de loc: stop naar alle blokken, zodat een blok met een oud rijcommando de neergezette loc niet laat wegrijden.");
                await VolledigeStop();
            }
            // BUG #79 (Marco 07:59: "wisselmotor 10 stond verkeerd"): eerst vragen of er een wissel verkeerd staat en die dan meteen schakelen;
            // alleen als dat niet zo is (of de loc daarna nog niet goed staat) moet de loc met de hand verplaatst worden.
            if (poging == 1 && await ControleerVerkeerdeWissel(melder, aanleiding)) return;
            // BUG #96 (Marco 16:05: "de loc de verkeerde rijrichting op"): de verkenner weet in welke richting de loc op deze melder moet staan
            // (vooruit = naar de volgende melder uit de kaart), maar zei dat niet. Nu staat de gewenste stand van de neus in de vraag.
            bool verder = await _vraag(
                "Loc terugzetten",
                $"{aanleiding}\n\nZet de testloc (adres {_ins.LocAdres}) met de hand op melder {melder}, zodat ALLEEN die melder bezet is. {OrientatieTekst(melder)} Controleer ook of de loc niet ontspoord is.\n\nKlik daarna op OK. (Annuleren stopt de verkenning.)");
            if (!verder) throw new OperationCanceledException("Gebruiker stopte bij het terugzetten van de loc.");
            _kortsluitingGemeld = false;
            // BUG #82: de gebruiker heeft de loc gecontroleerd/verplaatst (en zo nodig Dinamo herstart): een oud blokalarm geldt niet meer.
            lock (_alarmSlot) _alarmBlokken.Clear();
            // BUG #74: de loc is met de hand verplaatst en de gebruiker heeft intussen vaak wissels gecontroleerd of rechtgezet; ook kan een
            // open gereden wissel (kruiswissel!) fysiek anders staan dan we denken. Zonder terugmelding weet het programma dat niet, en
            // ZetWissel stuurt niets als de onthouden stand al klopt. Daarom alle standen vergeten: de wissels worden opnieuw gestuurd.
            _wisselStand.Clear();
            _ingang = null;
            if (_hw.KanMelderStatusOpvragen)
            {
                _hw.VraagMelderStatusOp(melder);
                foreach (var m in _monitor.Bezet) _hw.VraagMelderStatusOp(m);
            }
            await Wacht(TimeSpan.FromSeconds(2));
            _monitor.Bijwerken();
            var bezet = _monitor.Bezet;
            if (bezet.Contains(melder))
            {
                if (bezet.Count > 1) _log.Waarschuwing($"Naast melder {melder} zijn ook {Lijst(bezet.Where(m => m != melder))} bezet.");
                _log.Info($"Gebruiker heeft de loc op melder {melder} gezet.");
                return;
            }
            _log.Waarschuwing($"Melder {melder} meldt zich niet bezet (bezet: {Lijst(bezet)}).");
        }
        throw new OperationCanceledException($"Loc kon niet op melder {melder} gezet worden.");
    }

    // =====================================================================
    // Hulp
    // =====================================================================

    private async Task Wacht(TimeSpan duur)
    {
        _token.ThrowIfCancellationRequested();
        await _klok.Wacht(duur, _token);
    }

    private static string Lijst(IEnumerable<int> melders)
    {
        var l = melders.ToList();
        return l.Count == 0 ? "geen" : string.Join(", ", l);
    }
}

/// <summary>BUG #126: telt achter elkaar verstuurde wissel-commando's zonder terugmelding van Dinamo. Na 3 (eerder wel terugmeldingen gezien) of 4 (nog nooit)
/// is het alarm. Een terugmelding zet de teller terug. Na "Nee" van de gebruiker (de wissels gaan wel om) blijft het stil.</summary>
internal sealed class EchoWaakhond
{
    public int Reeks { get; private set; }
    public bool OoitBevestigd { get; private set; }
    public bool Negeren { get; set; }
    public static int Drempel(bool ooitBevestigd) => ooitBevestigd ? 3 : 4;

    /// <returns>true als het alarm nu af moet gaan.</returns>
    public bool Noteer(bool bevestigd)
    {
        if (bevestigd) { Reeks = 0; OoitBevestigd = true; return false; }
        Reeks++;
        return Negeren ? Reeks >= 12 : Reeks >= Drempel(OoitBevestigd);
    }

    /// <summary>Na herstel door de gebruiker: de reeks begint opnieuw; een tweede uitval wordt na 3 commando's weer gemeld.</summary>
    public void Herstel() { Reeks = 0; }
}
