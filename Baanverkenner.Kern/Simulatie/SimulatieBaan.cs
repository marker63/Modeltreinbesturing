using Modeltreinbesturing.Hardware;

namespace Baanverkenner.Kern.Simulatie;

/// <summary>
/// Een gesimuleerde modelbaan die zich gedraagt als echte hardware (IHardwareInterface):
/// secties met bezetmelders en lengte, wissels met adres en (on)gepolariseerd puntstuk,
/// stootjukken, een loc met decoder die zijn snelheid onthoudt. Optioneel gedraagt hij
/// zich als een Dinamo: rijcommando's komen alleen aan via het blok waar de loc staat,
/// commando's gaan één per 200 ms de deur uit, en de noodstop (F-bit) houdt alles stil.
///
/// Gebruik: om de Baanverkenner zonder echte baan uit te proberen, en voor de
/// geautomatiseerde test van het verkenalgoritme.
/// </summary>
public class SimulatieBaan : IHardwareInterface, IBlokAlarmBron
{
    // ---------------- Opbouw ----------------

    public enum Eind { A, B }
    public enum Poort { Stam, Recht, Af, KA1, KA2, KB1, KB2 }

    public class Sectie
    {
        public int Melder;          // 0 = geen bezetmelder
        public double Lengte;       // cm
        public int Blok;            // Dinamo-blok dat deze sectie voedt
        public Aansluiting?[] Einden = new Aansluiting?[2];
    }

    public class Wissel
    {
        public int Adres;
        public bool Gepolariseerd;
        public bool Afbuigend;
        public double Lengte = 12;
        public int Blok;
        public Aansluiting?[] Poorten = new Aansluiting?[7];
        /// <summary>Engelse wissel (BUG #150): vier benen KA1/KA2 (kant A) en KB1/KB2 (kant B) en twee motoren (<see cref="Adres"/> voor KA1, <see cref="Adres2"/> voor KA2).
        /// Een loc van kant A kiest zijn been op de motor van het been waar hij vandaan komt; van kant B naar A wordt de wissel van achteren bereden.</summary>
        public bool Kruis;
        public int Adres2;
        public bool Afbuigend2;
        /// <summary>Melder die de wissel zelf bewaakt (0 = geen; de kruiswissel is zelf een melder-sectie).</summary>
        public int Melder;
    }

    /// <summary>Waar een uiteinde op aansluit: een sectie-einde of een wisselpoort.</summary>
    public record Aansluiting(Sectie? Sectie, Eind SectieEind, Wissel? Wissel, Poort WisselPoort);

    private readonly List<Sectie> _secties = new();
    private readonly List<Wissel> _wissels = new();

    public Sectie NieuweSectie(int melder, double lengte, int blok)
    {
        var s = new Sectie { Melder = melder, Lengte = lengte, Blok = blok };
        _secties.Add(s);
        return s;
    }

    public Wissel NieuweWissel(int adres, bool gepolariseerd, int blok)
    {
        var w = new Wissel { Adres = adres, Gepolariseerd = gepolariseerd, Blok = blok };
        _wissels.Add(w);
        return w;
    }

    public void Verbind(Sectie a, Eind ea, Sectie b, Eind eb)
    {
        a.Einden[(int)ea] = new Aansluiting(b, eb, null, Poort.Stam);
        b.Einden[(int)eb] = new Aansluiting(a, ea, null, Poort.Stam);
    }

    public void Verbind(Wissel w, Poort p, Sectie s, Eind e)
    {
        w.Poorten[(int)p] = new Aansluiting(s, e, null, Poort.Stam);
        s.Einden[(int)e] = new Aansluiting(null, Eind.A, w, p);
    }

    /// <summary>Twee wissels direct aan elkaar (geen sectie ertussen).</summary>
    public void Verbind(Wissel a, Poort pa, Wissel b, Poort pb)
    {
        a.Poorten[(int)pa] = new Aansluiting(null, Eind.A, b, pb);
        b.Poorten[(int)pb] = new Aansluiting(null, Eind.A, a, pa);
    }

    /// <summary>BUG #150: Engelse wissel (kruiswissel) met eigen melder.</summary>
    public Wissel NieuweKruis(int adres, int adres2, int melder, int blok, double lengte)
    {
        var w = new Wissel { Adres = adres, Adres2 = adres2, Gepolariseerd = true, Blok = blok, Kruis = true, Melder = melder, Lengte = lengte };
        _wissels.Add(w);
        return w;
    }

    /// <summary>BUG #150: een wissel omzetten terwijl de loc er (deels) op staat geeft kortsluiting/ontsporing (zoals op de echte baan).</summary>
    public bool OmzettenOnderLocGeeftKortsluiting { get; set; }
    private List<(Wissel W, bool Stand1, bool Stand2)>? _omzetOud;
    /// <summary>Echte Dinamo (logs 14-10): bij een kortsluiting blijven de melders staan zoals ze waren ("Ook bezet op dat moment: melder X"). Standaard aan.</summary>
    public bool MeldersBevrorenBijKortsluiting { get; set; }
    /// <summary>BUG #181: strenger/realistischer: een loc die op de kruiswissel kortgesloten zit omdat de BUURWISSEL (bv. wissel 9 na melder 129) tegen hem in staat, komt vrij zodra die
    /// buurwissel wordt omgezet (gesplitste tak richting de samenvoeging, BUG #169). Standaard uit: de herstelroutes van de verkenner zijn er nog niet allemaal op berekend
    /// (zie BUGS.md #181, <c>STAP_STRENG=1</c> in de tests).</summary>
    public bool BuurwisselHeftKruisKortsluitingOp { get; set; }
    private HashSet<int>? _bevroren;

    /// <summary>De uitgang van een Engelse wissel voor een loc die via <paramref name="inn"/> binnenkomt, en of dat een kortsluiting geeft.</summary>
    internal static (Poort Uit, bool Kort) KruisUit(Wissel w, Poort inn)
    {
        bool m1 = w.Afbuigend, m2 = w.Afbuigend2;   // motor 13 (KA1) en motor 10 (KA2)
        Poort uit; bool kort = false;
        switch (inn)
        {
            case Poort.KA1: uit = m1 ? Poort.KB2 : Poort.KB1; break;
            case Poort.KA2: uit = m2 ? Poort.KB1 : Poort.KB2; break;
            case Poort.KB1: uit = m2 ? Poort.KA2 : Poort.KA1; kort = uit == Poort.KA1 && m1; break;
            default: uit = m1 ? Poort.KA1 : Poort.KA2; kort = uit == Poort.KA2 && m2; break;
        }
        // De kruiswissel is klein en grenst direct aan de volgende wissel: staat die tegen de ingang van de loc in (van achteren bereden), dan
        // ontstaat de kortsluiting al zodra de loc op de kruiswissel staat (zoals op de echte baan: log 14:38, opgeheven door de motor terug te zetten).
        if (!kort && w.Poorten[(int)uit] is { Wissel: { Kruis: false, Gepolariseerd: true } buur, WisselPoort: var bp } && bp != Poort.Stam && (bp == Poort.Af) != buur.Afbuigend)
            kort = true;
        return (uit, kort);
    }

    // ---------------- Instellingen van het gedrag ----------------

    /// <summary>Gedrag als Dinamo (blokgebonden rijcommando's, 200 ms-cyclus, F-bit).</summary>
    public bool DinamoGedrag { get; set; }
    /// <summary>Gedrag van de echte baan (BUG #34/#57): zodra de loc helemaal uit het blok is waarnaar het laatste rijcommando ging, blijft hij stilstaan. Standaard uit, zodat de DemoBaan (wissels zonder eigen melder) ongewijzigd werkt.</summary>
    public bool StaatStilZonderBlokcommando { get; set; }
    /// <summary>Gedrag van de echte baan (11:38-rit, BUG #61): een loc die vanuit stilstand met een deel in twee blokken staat, gaat alleen rijden als het rijcommando naar ALLE die blokken gaat (een voedend blok naast een onbekrachtigd blok remt de loc af). Een rijdende loc rolt gewoon door. Standaard uit.</summary>
    public bool StilstaanRemtOpGrens { get; set; }
    private readonly HashSet<int> _aangestuurd = new();
    /// <summary>Alleen voor tests (BUG #62): het N-de rijcommando (snelheid > 0) naar dit blok doet niets en veroorzaakt een kortsluitmelding (zoals een loc met een poot tegen een verkeerd staande wisseltong).</summary>
    public (int Blok, int Nummer)? KortsluitBijCommando { get; set; }
    private int _kortCommandoTeller;
    private DateTime? _kortMeldTijd;
    private bool _bewegend;
    /// <summary>Alleen voor tests (BUG #57): (melder waar het midden van de loc staat, blok, vooruit)-combinaties waarvoor een rijcommando met snelheid > 0 niets doet (waarde = hoe vaak het commando verloren gaat; daarna werkt het weer).</summary>
    public Dictionary<(int Melder, int Blok, bool Vooruit), int> GeblokkeerdeCommandos { get; } = new();
    /// <summary>Meldt kortsluiting via KortsluitingStatusGewijzigd (zoals Dinamo's F-bit).</summary>
    public bool MeldtKortsluiting { get; set; } = true;
    public double LocLengte { get; set; } = 20;
    /// <summary>Test: zoveel korte spookpulsen (120 ms) per minuut op willekeurige melders.</summary>
    public double SpookpulsenPerMinuut { get; set; }
    /// <summary>Test: kan de simulatie de melderstand opvragen (zoals Dinamo)?</summary>
    public bool StatusOpvraagbaar { get; set; } = true;
    private readonly Random _toeval = new(12345);
    private DateTime? _spookTot;
    private int _spookMelder;

    /// <summary>cm/s per snelheidsstap.</summary>
    public double CmPerSecondePerStap { get; set; } = 2.0;

    // ---------------- Toestand van de loc ----------------

    private object? _element;            // Sectie of Wissel
    private double _pos;                 // positie in het element (0..lengte)
    /// <summary>Alleen voor tests: positie van de loc in zijn huidige element.</summary>
    public double LocPositieTest() => _pos;
    private int _orientatie = 1;         // baanrichting (+1/-1 in het frame van het element) bij "vooruit"
    private Poort _wisselIn, _wisselUit; // frame binnen een wissel: 0 = ingang, lengte = uitgang
    private int _decoderStap;
    private bool _decoderVooruit = true;
    // De loc heeft een lengte: het spoor dat hij onder zich heeft wordt bijgehouden in een
    // eigen coördinaat _x (neemt toe bij vooruit rijden, af bij achteruit). Elk element dat
    // de loc (het midden) passeert krijgt een interval op die as; bezet = alle melders
    // waarvan het interval overlapt met [_x - lengte/2, _x + lengte/2]. Klopt ook na omkeren.
    private double _x;
    private readonly List<(double Van, double Tot, object Element)> _spoor = new();
    private readonly Dictionary<Sectie, int> _orientatieIn = new();   // laatste oriëntatie van de loc per sectie
    private bool _kortsluiting;
    private DateTime _volgendeHerstelpoging;
    private DateTime _fBitTot = DateTime.MinValue;
    private DateTime? _vorigeTik;
    private readonly Queue<Action> _wachtrij = new();
    private DateTime _volgendeCyclus = DateTime.MinValue;
    private readonly Dictionary<int, bool> _gemeld = new();
    private int _locAdres = 3;
    private int? _commandoBlok;
    /// <summary>Alleen voor tests (BUG #58): melder die tijdens het neerzetten kort vrij meldt terwijl de loc er nog op staat (en ook op een andere melder).</summary>
    public int FlikkerMelder { get; set; }
    public int FlikkerKeer { get; set; } = 1;
    private int _geflikkerd;
    private bool _flikkerTotActief() => _vorigeTik is DateTime t && t < _flikkerTot;
    private DateTime _flikkerTot = DateTime.MinValue;
    private DateTime _flikkerVolgende = DateTime.MinValue;
    private bool VerliesCommando((int, int, bool) sleutel)
    {
        if (!GeblokkeerdeCommandos.TryGetValue(sleutel, out var n) || n <= 0) return false;
        GeblokkeerdeCommandos[sleutel] = n - 1;
        return true;
    }

    public int LocAdres { get => _locAdres; set => _locAdres = value; }

    public void PlaatsLoc(Sectie s, double pos, int orientatie)
    {
        _element = s;
        _pos = Math.Clamp(pos, 0, s.Lengte);
        _orientatie = orientatie >= 0 ? 1 : -1;
        _kortsluiting = false; _omzetOud = null; AlarmUit();
        _x = 0;
        _spoor.Clear();
        // Interval van het huidige element: element-positie p = _pos + oriëntatie * (x - 0)
        double van = _orientatie > 0 ? -_pos : -(s.Lengte - _pos);
        double tot = _orientatie > 0 ? s.Lengte - _pos : _pos;
        _spoor.Add((van, tot, s));
    }

    /// <summary>BUG #142 (test): zet de loc op <paramref name="s"/> en laat hem met de kop al op de aansluitende sectie
    /// <paramref name="volgende"/> staan (de loc is langer dan de melder en staat dus op twee secties tegelijk).</summary>
    public void PlaatsLocOpGrens(Sectie s, double pos, int orientatie, Sectie volgende)
    {
        PlaatsLoc(s, pos, orientatie);
        var eerste = _spoor[0];
        if (_orientatie > 0) _spoor.Add((eerste.Tot, eerste.Tot + volgende.Lengte, volgende));
        else _spoor.Add((eerste.Van - volgende.Lengte, eerste.Van, volgende));
    }

    /// <summary>"De gebruiker zet de loc met de hand terug": midden op de sectie met deze
    /// melder, zelfde rijrichting van de loc t.o.v. de sectie als voorheen (best effort).</summary>
    public bool ZetLocOpMelder(int melder)
    {
        var s = _secties.FirstOrDefault(x => x.Melder == melder);
        if (s is null) return false;
        PlaatsLoc(s, s.Lengte / 2, _orientatieIn.TryGetValue(s, out var o) ? o : 1);
        _decoderStap = 0;
        StatusBericht?.Invoke($"[Simulatie] Loc met de hand op melder {melder} gezet.");
        BijwerkenMelders(forceer: true);
        return true;
    }

    public IReadOnlyList<Wissel> Wissels => _wissels;

    /// <summary>De tijd van de simulatie (laatste tik).</summary>
    protected DateTime SimTijd => _vorigeTik ?? DateTime.Now;

    /// <summary>Alleen voor tests/foutzoeken: waar staat de loc nu?</summary>
    public string ToestandTest() => (_element switch { Sectie s => $"sectie {s.Melder}", Wissel w => w.Kruis ? $"kruis {w.Melder} in {_wisselIn} uit {_wisselUit}" : $"wissel {w.Adres} in {_wisselIn} uit {_wisselUit}", _ => "geen" })
        + $" pos {_pos:0.0} orient {_orientatie} stap {_decoderStap} {(_decoderVooruit ? "vooruit" : "achteruit")} kortsluiting {_kortsluiting} bezet [{string.Join(",", BezetteMelders().OrderBy(x => x))}]";


    // ---------------- IHardwareInterface ----------------

    public bool Verbonden { get; private set; }
    public string Naam => DinamoGedrag ? "Simulatie-proefbaan (gedraagt zich als Dinamo)" : "Simulatie-proefbaan";
    public bool LocCommandoVereistBlok => DinamoGedrag;
    public bool KanMelderStatusOpvragen => StatusOpvraagbaar;
    public bool KlaarVoorVolgendeWisselCommando => true;

    public event Action<int, bool>? BezetmeldingGewijzigd;
    /// <summary>Dinamo meldt een kortsluiting per blok (BUG #43). Alleen als <see cref="MeldBlokAlarm"/> aan staat.</summary>
    public event Action<int, bool>? BlokAlarmGewijzigd;
    public bool MeldBlokAlarm { get; set; }
    private readonly HashSet<int> _alarmBlokken = new();
    private void AlarmAan(params int[] blokken) { if (!MeldBlokAlarm) return; foreach (var b in blokken) if (_alarmBlokken.Add(b)) BlokAlarmGewijzigd?.Invoke(b, true); }
    /// <summary>Echte Dinamo: de kortsluiting wordt een fractie van een seconde NA het ontstaan gemeld (eerst zie je de melder bezet worden).</summary>
    public double KortsluitMeldVertraging { get; set; } = 0.8;
    /// <summary>BUG #164 (hardwarelog 16-10 09:00-09:01): Dinamo meldt de kortsluiting van een vastzittende loc alleen zolang er een rijcommando naar de blokken
    /// staat; zodra de loc gestopt is verdwijnt het alarm (0,8 s na de stop) en bij het volgende rijcommando komt het meteen terug. De kortsluiting zelf blijft
    /// bestaan (de loc staat klem tot de wissel goed staat). Standaard uit.</summary>
    public bool AlarmAlleenBijRijstroom { get; set; }
    private DateTime? _stilSinds;
    private DateTime? _meldKortOp;
    private int _meldKortBlok;
    private void MeldKortsluitingStraks(int blok, DateTime nu)
    {
        _meldKortBlok = blok;
        if (KortsluitMeldVertraging <= 0) { AlarmAan(blok); if (MeldtKortsluiting) KortsluitingStatusGewijzigd?.Invoke(true); }
        else _meldKortOp = nu + TimeSpan.FromSeconds(KortsluitMeldVertraging);
    }
    private void AlarmUit() { _meldKortOp = null; if (!MeldBlokAlarm) return; foreach (var b in _alarmBlokken.ToList()) { _alarmBlokken.Remove(b); BlokAlarmGewijzigd?.Invoke(b, false); } }
    public event Action<string>? StatusBericht;
    public event Action<bool>? KortsluitingStatusGewijzigd;

    public Task VerbindenAsync(string comPoort)
    {
        Verbonden = true;
        StatusBericht?.Invoke($"[Simulatie] {Naam} actief.");
        // Zonder statusopvraag meldt de centrale alleen WIJZIGINGEN: de beginstand is
        // dan onbekend voor de verkenner (zoals bij een echte centrale).
        if (!StatusOpvraagbaar)
            foreach (var m in BezetteMelders()) _gemeld[m] = true;
        return Task.CompletedTask;
    }

    public void Ontkoppelen() => Verbonden = false;

    public virtual void ZetWissel(int adres, bool afbuigend) => InWachtrij(() =>
    {
        foreach (var w in _wissels.Where(w => w.Adres == adres || (w.Kruis && w.Adres2 == adres)))
        {
            bool tweede = w.Kruis && w.Adres2 == adres && w.Adres != adres;
            bool oud1 = w.Afbuigend, oud2 = w.Afbuigend2;
            if (tweede) w.Afbuigend2 = afbuigend; else w.Afbuigend = afbuigend;
            bool veranderd = oud1 != w.Afbuigend || oud2 != w.Afbuigend2;
            // BUG #150: omzetten terwijl de loc er (deels) op staat = kortsluiting/ontsporing
            if (veranderd && OmzettenOnderLocGeeftKortsluiting && !_kortsluiting && _element is not null
                && (ReferenceEquals(_element, w) || ElementenOnderLoc().Any(e => ReferenceEquals(e, w))))
            {
                _bevroren = BezetteMelders();
                _kortsluiting = true;
                BijwerkenMelders(false);
                _omzetOud = new() { (w, oud1, oud2) };
                _volgendeHerstelpoging = (_vorigeTik ?? DateTime.Now) + TimeSpan.FromSeconds(2);
                StatusBericht?.Invoke($"[Simulatie] KORTSLUITING: wissel {adres} omgezet terwijl de loc erop stond.");
                MeldKortsluitingStraks(w.Blok, _vorigeTik ?? DateTime.Now);
                continue;
            }
            // Terugzetten van de wissel die onder de loc is omgezet haalt de kortsluiting weg
            if (_kortsluiting && _omzetOud is { } oudeLijst && oudeLijst.All(o => o.W.Afbuigend == o.Stand1 && o.W.Afbuigend2 == o.Stand2))
            {
                _kortsluiting = false; _omzetOud = null; AlarmUit();
                if (MeldtKortsluiting) KortsluitingStatusGewijzigd?.Invoke(false);
            }
            // Zoals op de echte baan (log 09:24:50): een loc die van achteren tegen een verkeerd staande wissel kortgesloten zit, is vrij zodra de
            // wissel naar de tak wordt gezet waar de loc vandaan komt.
            else if (_kortsluiting && _omzetOud is null && ReferenceEquals(_element, w))
            {
                bool vrij = w.Kruis ? !KruisUit(w, _wisselIn).Kort : (_wisselIn == Poort.Af) == afbuigend;
                if (vrij)
                {
                    _kortsluiting = false; AlarmUit();
                    if (MeldtKortsluiting) KortsluitingStatusGewijzigd?.Invoke(false);
                }
            }
        }
        // BUG #181: de kortsluiting van een loc die van achteren tegen een verkeerd staande BUURWISSEL van de kruiswissel aan zit (8 -> kruiswissel -> wissel 9 afbuigend, van de
        // tak af) gaat ook weg als die buurwissel wordt omgezet, niet alleen als de kruismotoren omgaan (gesplitste tak richting de samenvoeging: BUG #169). Zonder dit bleef de
        // simulator na zo'n kortsluiting voorgoed vastzitten, terwijl de echte baan dan weer vrij is.
        if (BuurwisselHeftKruisKortsluitingOp && _kortsluiting && _omzetOud is null && _element is Wissel { Kruis: true } kw)
        {
            var (kUit, kort) = KruisUit(kw, _wisselIn);
            if (!kort)
            {
                _wisselUit = kUit;
                _kortsluiting = false; AlarmUit();
                if (MeldtKortsluiting) KortsluitingStatusGewijzigd?.Invoke(false);
            }
        }
        HardwareCommunicatieLog.Log("Uit", $"[Simulatie] wissel {adres} -> {(afbuigend ? "afbuigend" : "rechtdoor")}");
    });

    public void ZetSein(int adres, bool onveiligRood) { }

    public void ZetLocSnelheid(int decoderAdres, int stap, bool vooruit, int blokNummer = 0, int stappen = 126) => InWachtrij(() =>
    {
        if (decoderAdres != _locAdres) return;
        if (DinamoGedrag && !BlokkenVanLoc().Contains(blokNummer)) return; // komt niet aan
        if (DinamoGedrag && _element is Sectie huidig && stap > 0 && VerliesCommando((huidig.Melder, blokNummer, vooruit))) return; // test: dit commando gaat verloren
        if (KortsluitBijCommando is { } kc && stap > 0 && blokNummer == kc.Blok && ++_kortCommandoTeller == kc.Nummer)
        {
            _kortMeldTijd = (_vorigeTik ?? DateTime.Now) + TimeSpan.FromMilliseconds(600);
            return;
        }
        _commandoBlok = blokNummer;
        if (stap > 0 && _decoderStap > 0 && _decoderVooruit == vooruit) _aangestuurd.Add(blokNummer);
        else { _aangestuurd.Clear(); if (stap > 0) _aangestuurd.Add(blokNummer); }
        _decoderStap = Math.Max(0, stap);
        _decoderVooruit = vooruit;
    });

    public void ZetFunctie(int decoderAdres, int functieNummer, bool aan, int blokNummer = 0) { }

    public void Noodstop()
    {
        if (DinamoGedrag)
        {
            _fBitTot = (_vorigeTik ?? DateTime.MinValue) + TimeSpan.FromSeconds(1);
            // Een echte Dinamo kaatst de foutstatus terug - de verkenner moet dat negeren.
            if (MeldtKortsluiting) KortsluitingStatusGewijzigd?.Invoke(true);
        }
        else _decoderStap = 0;
    }

    public void VraagMelderStatusOp(int meldernummer) => InWachtrij(() =>
    {
        var bezet = BezetteMelders();
        BezetmeldingGewijzigd?.Invoke(meldernummer, bezet.Contains(meldernummer));
    });

    private void InWachtrij(Action a)
    {
        if (DinamoGedrag) _wachtrij.Enqueue(a);
        else a();
    }

    // ---------------- Tijd laten verstrijken ----------------

    /// <summary>Koppel aan IKlok.Getikt.</summary>
    public void Tik(DateTime nu)
    {
        if (_vorigeTik is null) { _vorigeTik = nu; return; }
        double dt = (nu - _vorigeTik.Value).TotalSeconds;
        _vorigeTik = nu;
        if (dt <= 0) return;

        // Dinamo-cyclus: één commando per 200 ms
        if (DinamoGedrag)
        {
            if (_volgendeCyclus == DateTime.MinValue) _volgendeCyclus = nu;
            while (nu >= _volgendeCyclus)
            {
                if (_wachtrij.Count > 0) _wachtrij.Dequeue()();
                _volgendeCyclus += TimeSpan.FromMilliseconds(200);
            }
            if (nu >= _fBitTot && _fBitTot != DateTime.MinValue)
            {
                _fBitTot = DateTime.MinValue;
                if (MeldtKortsluiting && !_kortsluiting) KortsluitingStatusGewijzigd?.Invoke(false);
            }
        }

        if (_meldKortOp is DateTime mk && nu >= mk)
        {
            _meldKortOp = null;
            if (_kortsluiting) { AlarmAan(_meldKortBlok); if (MeldtKortsluiting) KortsluitingStatusGewijzigd?.Invoke(true); }
        }
        if (AlarmAlleenBijRijstroom && _kortsluiting && DinamoGedrag)
        {
            if (_decoderStap == 0)
            {
                _stilSinds ??= nu;
                if (_alarmBlokken.Count > 0 && _meldKortOp is null && (nu - _stilSinds.Value).TotalSeconds >= 0.8) AlarmUit();
            }
            else
            {
                _stilSinds = null;
                if (_alarmBlokken.Count == 0 && _meldKortOp is null) MeldKortsluitingStraks(_meldKortBlok, nu);
            }
        }
        else _stilSinds = null;
        if (_kortMeldTijd is DateTime km && nu >= km) { _kortMeldTijd = null; KortsluitingStatusGewijzigd?.Invoke(true); }
        Beweeg(nu, dt);
        if (FlikkerMelder > 0 && _geflikkerd < FlikkerKeer && nu >= _flikkerVolgende && nu >= _flikkerTot)
        {
            var echt = BezetteMelders();
            if (echt.Contains(FlikkerMelder) && echt.Count > 1)
            {
                _flikkerTot = nu + TimeSpan.FromMilliseconds(900);
                _flikkerVolgende = _flikkerTot;
                _geflikkerd++;
            }
        }
        if (SpookpulsenPerMinuut > 0)
        {
            if (_spookTot is DateTime tot && nu >= tot) _spookTot = null;
            else if (_spookTot is null && _toeval.NextDouble() < SpookpulsenPerMinuut * dt / 60)
            {
                var kandidaten = _secties.Where(x => x.Melder > 0).ToList();
                _spookMelder = kandidaten[_toeval.Next(kandidaten.Count)].Melder;
                _spookTot = nu + TimeSpan.FromMilliseconds(120);
            }
        }
        BijwerkenMelders(forceer: false);
    }

    private void Beweeg(DateTime nu, double dt)
    {
        if (_element is null) return;
        bool fBit = DinamoGedrag && nu < _fBitTot;
        if (fBit || _decoderStap == 0) { _bewegend = false; return; }
        // Echte Dinamo (gezien op de baan, BUG #34/#57): een rijcommando gaat via een blok. Zodra de loc
        // helemaal uit dat blok is, komt er geen commando meer aan en blijft hij stilstaan.
        if (DinamoGedrag && StaatStilZonderBlokcommando && _commandoBlok is not null && !(_aangestuurd.Count > 0 ? _aangestuurd.Any(b => BlokkenVanLoc().Contains(b)) : BlokkenVanLoc().Contains(_commandoBlok.Value))) { _decoderStap = 0; _bewegend = false; return; }
        if (DinamoGedrag && StilstaanRemtOpGrens && !_bewegend)
        {
            var onder = BlokkenVanLoc();
            if (onder.Count > 1 && !onder.All(_aangestuurd.Contains)) return;
        }
        _bewegend = true;
        int v = _decoderVooruit ? 1 : -1;

        if (_kortsluiting)
        {
            if (nu < _volgendeHerstelpoging) return;
            _volgendeHerstelpoging = nu + TimeSpan.FromSeconds(2);
            // Booster probeert opnieuw: alleen als de loc van het puntstuk af rijdt (terug
            // naar de ingang van de wissel) verdwijnt de kortsluiting.
            if (_orientatie * v < 0)
            {
                _kortsluiting = false; _omzetOud = null; AlarmUit();
                if (MeldtKortsluiting) KortsluitingStatusGewijzigd?.Invoke(false);
            }
            else return;
        }

        double afstand = _decoderStap * CmPerSecondePerStap * dt;
        while (afstand > 1e-9 && !_kortsluiting)
        {
            int t = _orientatie * v;
            double lengte = ElementLengte(_element);
            double ruimte = t > 0 ? lengte - _pos : _pos;
            if (afstand <= ruimte)
            {
                _pos += t * afstand;
                _x += v * afstand;
                break;
            }
            _pos = t > 0 ? lengte : 0;
            _x += v * ruimte;
            afstand -= ruimte;
            if (!Overgang(t, v, nu)) break; // stootjuk of kortsluiting
        }
    }

    /// <summary>De loc verlaat het huidige element aan de kant van baanrichting t.</summary>
    private bool Overgang(int t, int v, DateTime nu)
    {
        Aansluiting? volgende;
        if (_element is Sectie s)
        {
            volgende = s.Einden[t > 0 ? (int)Eind.B : (int)Eind.A];
            _orientatieIn[s] = _orientatie;
        }
        else
        {
            var w = (Wissel)_element!;
            volgende = w.Poorten[(int)(t > 0 ? _wisselUit : _wisselIn)];
        }
        if (volgende is null) return false; // stootjuk: loc blijft duwen

        if (volgende.Sectie is { } ns)
        {
            _element = ns;
            bool bijA = volgende.SectieEind == Eind.A;
            _pos = bijA ? 0 : ns.Lengte;
            int tNieuw = bijA ? 1 : -1;
            _orientatie = tNieuw * v;
            Registreer(ns, ns.Lengte, v);
            return true;
        }

        var nw = volgende.Wissel!;
        var inPoort = volgende.WisselPoort;
        Poort uit;
        if (nw.Kruis)
        {
            var (kUit, kort) = KruisUit(nw, inPoort);
            if (kort)
            {
                _element = nw; _wisselIn = inPoort; _wisselUit = inPoort; _pos = 0.5;
                _orientatie = 1 * v;
                _omzetOud = null;
                _bevroren = BezetteMelders();
                _kortsluiting = true;
                BijwerkenMelders(false);
                _volgendeHerstelpoging = nu + TimeSpan.FromSeconds(2);
                StatusBericht?.Invoke($"[Simulatie] KORTSLUITING op de kruiswissel (melder {nw.Melder}).");
                MeldKortsluitingStraks(nw.Blok, nu);
                return false;
            }
            uit = kUit;
        }
        else if (inPoort == Poort.Stam) uit = nw.Afbuigend ? Poort.Af : Poort.Recht;
        else
        {
            bool klopt = (inPoort == Poort.Af) == nw.Afbuigend;
            if (!klopt && nw.Gepolariseerd)
            {
                // Van achteren tegen een verkeerd staande wissel: kortsluiting op het puntstuk
                _element = nw; _wisselIn = inPoort; _wisselUit = Poort.Stam; _pos = 0.5;
                _orientatie = 1 * v;
                _omzetOud = null;
                _bevroren = BezetteMelders();
                _kortsluiting = true;
                BijwerkenMelders(false);
                _volgendeHerstelpoging = nu + TimeSpan.FromSeconds(2);
                StatusBericht?.Invoke($"[Simulatie] KORTSLUITING op wissel {nw.Adres}.");
                MeldKortsluitingStraks(nw.Blok, nu);
                return false;
            }
            uit = Poort.Stam; // ongepolariseerd: wordt opengereden
        }
        _element = nw;
        _wisselIn = inPoort;
        _wisselUit = uit;
        _pos = 0;
        _orientatie = 1 * v;
        Registreer(nw, nw.Lengte, v);
        return true;
    }

    /// <summary>Legt het zojuist binnengereden element vast op de _x-as.</summary>
    private void Registreer(object element, double lengte, int v)
    {
        double a = _x, b = _x + v * lengte;
        // Alles wat vóór de loc ligt (in rijrichting) is van een eerdere rit en kan door
        // omgezette wissels niet meer kloppen: weg ermee.
        _spoor.RemoveAll(i => v > 0 ? i.Tot > a + 1e-6 : i.Van < a - 1e-6);
        _spoor.Add((Math.Min(a, b), Math.Max(a, b), element));
        _spoor.RemoveAll(i => Math.Abs(i.Van - _x) > 1000 && Math.Abs(i.Tot - _x) > 1000);
    }

    private static double Overlap(double a1, double b1, double a2, double b2) => Math.Min(b1, b2) - Math.Max(a1, a2);

    private IEnumerable<object> ElementenOnderLoc()
    {
        double a = _x - LocLengte / 2, b = _x + LocLengte / 2;
        return _spoor.Where(i => Overlap(i.Van, i.Tot, a, b) > 0).Select(i => i.Element);
    }

    private static double ElementLengte(object e) => e is Sectie s ? s.Lengte : ((Wissel)e).Lengte;

    // ---------------- Melders ----------------

    private HashSet<int> BezetteMelders()
    {
        var res = new HashSet<int>();
        if (_kortsluiting && MeldersBevrorenBijKortsluiting && _bevroren is not null) return new HashSet<int>(_bevroren);
        if (_kortsluiting || _element is null) return res; // geen spanning = geen stroomdetectie
        if (_element is Sectie s && s.Melder > 0) res.Add(s.Melder);
        if (_element is Wissel kw && kw.Melder > 0) res.Add(kw.Melder);
        if (_spookTot is not null) res.Add(_spookMelder);
        foreach (var e in ElementenOnderLoc())
        {
            if (e is Sectie sec && sec.Melder > 0) res.Add(sec.Melder);
            else if (e is Wissel wk && wk.Melder > 0) res.Add(wk.Melder);
        }
        if (FlikkerMelder > 0 && _flikkerTotActief()) res.Remove(FlikkerMelder);
        return res;
    }

    private HashSet<int> BlokkenVanLoc()
    {
        var res = new HashSet<int>();
        if (_element is Sectie s) res.Add(s.Blok);
        else if (_element is Wissel w) res.Add(w.Blok);
        foreach (var e in ElementenOnderLoc())
            res.Add(e is Sectie sec ? sec.Blok : ((Wissel)e).Blok);
        return res;
    }

    private void BijwerkenMelders(bool forceer)
    {
        var bezet = BezetteMelders();
        foreach (var m in _secties.Where(x => x.Melder > 0).Select(x => x.Melder).Concat(_wissels.Where(x => x.Melder > 0).Select(x => x.Melder)).Distinct())
        {
            bool b = bezet.Contains(m);
            bool oud = _gemeld.TryGetValue(m, out var g) && g;
            if (b != oud || (forceer && b))
            {
                _gemeld[m] = b;
                BezetmeldingGewijzigd?.Invoke(m, b);
            }
        }
    }
}
