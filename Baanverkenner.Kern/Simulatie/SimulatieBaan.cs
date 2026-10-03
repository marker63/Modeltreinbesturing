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
public class SimulatieBaan : IHardwareInterface
{
    // ---------------- Opbouw ----------------

    public enum Eind { A, B }
    public enum Poort { Stam, Recht, Af }

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
        public Aansluiting?[] Poorten = new Aansluiting?[3];
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

    // ---------------- Instellingen van het gedrag ----------------

    /// <summary>Gedrag als Dinamo (blokgebonden rijcommando's, 200 ms-cyclus, F-bit).</summary>
    public bool DinamoGedrag { get; set; }
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

    public int LocAdres { get => _locAdres; set => _locAdres = value; }

    public void PlaatsLoc(Sectie s, double pos, int orientatie)
    {
        _element = s;
        _pos = Math.Clamp(pos, 0, s.Lengte);
        _orientatie = orientatie >= 0 ? 1 : -1;
        _kortsluiting = false;
        _x = 0;
        _spoor.Clear();
        // Interval van het huidige element: element-positie p = _pos + oriëntatie * (x - 0)
        double van = _orientatie > 0 ? -_pos : -(s.Lengte - _pos);
        double tot = _orientatie > 0 ? s.Lengte - _pos : _pos;
        _spoor.Add((van, tot, s));
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

    // ---------------- IHardwareInterface ----------------

    public bool Verbonden { get; private set; }
    public string Naam => DinamoGedrag ? "Simulatie-proefbaan (gedraagt zich als Dinamo)" : "Simulatie-proefbaan";
    public bool LocCommandoVereistBlok => DinamoGedrag;
    public bool KanMelderStatusOpvragen => StatusOpvraagbaar;
    public bool KlaarVoorVolgendeWisselCommando => true;

    public event Action<int, bool>? BezetmeldingGewijzigd;
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

    public void ZetWissel(int adres, bool afbuigend) => InWachtrij(() =>
    {
        foreach (var w in _wissels.Where(w => w.Adres == adres)) w.Afbuigend = afbuigend;
        HardwareCommunicatieLog.Log("Uit", $"[Simulatie] wissel {adres} -> {(afbuigend ? "afbuigend" : "rechtdoor")}");
    });

    public void ZetSein(int adres, bool onveiligRood) { }

    public void ZetLocSnelheid(int decoderAdres, int stap, bool vooruit, int blokNummer = 0, int stappen = 126) => InWachtrij(() =>
    {
        if (decoderAdres != _locAdres) return;
        if (DinamoGedrag && !BlokkenVanLoc().Contains(blokNummer)) return; // komt niet aan
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

        Beweeg(nu, dt);
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
        if (fBit || _decoderStap == 0) return;
        int v = _decoderVooruit ? 1 : -1;

        if (_kortsluiting)
        {
            if (nu < _volgendeHerstelpoging) return;
            _volgendeHerstelpoging = nu + TimeSpan.FromSeconds(2);
            // Booster probeert opnieuw: alleen als de loc van het puntstuk af rijdt (terug
            // naar de ingang van de wissel) verdwijnt de kortsluiting.
            if (_orientatie * v < 0)
            {
                _kortsluiting = false;
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
        if (inPoort == Poort.Stam) uit = nw.Afbuigend ? Poort.Af : Poort.Recht;
        else
        {
            bool klopt = (inPoort == Poort.Af) == nw.Afbuigend;
            if (!klopt && nw.Gepolariseerd)
            {
                // Van achteren tegen een verkeerd staande wissel: kortsluiting op het puntstuk
                _element = nw; _wisselIn = inPoort; _wisselUit = Poort.Stam; _pos = 0.5;
                _orientatie = 1 * v;
                _kortsluiting = true;
                _volgendeHerstelpoging = nu + TimeSpan.FromSeconds(2);
                StatusBericht?.Invoke($"[Simulatie] KORTSLUITING op wissel {nw.Adres}.");
                if (MeldtKortsluiting) KortsluitingStatusGewijzigd?.Invoke(true);
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
        if (_kortsluiting || _element is null) return res; // geen spanning = geen stroomdetectie
        if (_element is Sectie s && s.Melder > 0) res.Add(s.Melder);
        if (_spookTot is not null) res.Add(_spookMelder);
        foreach (var e in ElementenOnderLoc())
            if (e is Sectie sec && sec.Melder > 0) res.Add(sec.Melder);
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
        foreach (var m in _secties.Where(x => x.Melder > 0).Select(x => x.Melder).Distinct())
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
