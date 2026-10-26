using Modeltreinbesturing.Hardware;

namespace Baanverkenner.Kern.Simulatie;

/// <summary>BUG #173: simulatie met een Dinamo-uitval zoals op de baan (17-10 08:16): vanaf een bepaald wisselcommando voert Dinamo gedurende een tijd geen
/// wissel-commando's uit en meldt ze niet terug; daarna werkt alles weer. Meldt wel terug als het commando uitgevoerd is (<see cref="IWisselBevestiging"/>).</summary>
public class UitvalSimulatieBaan : SimulatieBaan, IWisselBevestiging, IWisselPuls
{
    private long _bevestigingen;
    private int _teller;
    private DateTime? _uitvalStart;

    public long AantalWisselBevestigingen => _bevestigingen;
    private readonly Dictionary<(int, bool), long> _perStand = new();
    public long BevestigingenVan(int adres, bool afbuigend) => _perStand.TryGetValue((adres, afbuigend), out var n) ? n : 0;   // BUG #307

    /// <summary>Het wisselcommando (tellend sinds het verbinden) waarmee de uitval begint.</summary>
    public int UitvalVanafCommando { get; set; } = int.MaxValue;
    public TimeSpan UitvalDuur { get; set; } = TimeSpan.FromSeconds(75);
    /// <summary>Het aantal commando's dat tijdens de uitval niet is uitgevoerd.</summary>
    public int GenegeerdeCommandos { get; private set; }

    /// <summary>Nachtwerk 25/26-10 (logs 25-10 09:06, 18:05, 19:25: "geen terugmelding, maar Dinamo meldt kortsluiting"): tijdens een kortsluiting voert Dinamo een wisselcommando vaak niet
    /// uit. Bij aan wordt tijdens een kortsluiting de helft van de wisselcommando's (willekeurig, vaste reeks) genegeerd (niet uitgevoerd en niet teruggemeld).</summary>
    public bool NegeerHelftBijKortsluiting { get; set; }
    private readonly Random _rnd = new(42);
    public int GenegeerdBijKortsluiting { get; private set; }

    // ---- BUG #352: Dinamo voert elk verstuurd wisseldatagram als aparte puls uit, één tegelijk (hardwarelog 26-10 middag:
    // terugmeldingen tot minuten na het commando). Met VertraagdeUitvoering aan gaat elk commando als WisselHerhalingen pulsen in een wachtrij; pas bij de uitvoering van een puls
    // verandert de wissel en komt de terugmelding.
    public bool VertraagdeUitvoering { get; set; }
    public int WisselHerhalingen { get; set; } = 6;
    public TimeSpan PulsDuur { get; set; } = TimeSpan.FromMilliseconds(400);
    public void ZetPulsTijd(int adres, int zestigsten) { }
    public bool DinamoHold => _pulsen.Count > 40;
    private readonly Queue<(int Adres, bool Afbuigend)> _pulsen = new();
    private DateTime _volgendePuls = DateTime.MinValue;
    /// <summary>De langste tijd die een puls in de wachtrij stond.</summary>
    public TimeSpan LangsteAchterstand { get; private set; }
    private readonly Queue<DateTime> _pulsTijden = new();

    protected override void NaTik(DateTime nu)
    {
        if (!VertraagdeUitvoering) return;
        while (_pulsen.Count > 0 && nu >= _volgendePuls)
        {
            var (a, afb) = _pulsen.Dequeue();
            var t0 = _pulsTijden.Dequeue();
            if (nu - t0 > LangsteAchterstand) LangsteAchterstand = nu - t0;
            VoerUit(a, afb);
            _volgendePuls = nu + PulsDuur;
        }
    }

    private void VoerUit(int adres, bool afbuigend)
    {
        base.ZetWissel(adres, afbuigend);
        _bevestigingen++;
        _perStand[(adres, afbuigend)] = _perStand.TryGetValue((adres, afbuigend), out var n) ? n + 1 : 1;   // BUG #307
    }

    public override void ZetWissel(int adres, bool afbuigend)
    {
        _teller++;
        var nu = SimTijd;
        if (_teller >= UitvalVanafCommando) _uitvalStart ??= nu;
        if (_uitvalStart is { } s && nu - s < UitvalDuur) { GenegeerdeCommandos++; return; }
        if (NegeerHelftBijKortsluiting && HeeftKortsluiting && _rnd.NextDouble() < 0.5) { GenegeerdBijKortsluiting++; return; }
        if (VertraagdeUitvoering)
        {
            for (int i = 0; i < Math.Max(1, WisselHerhalingen); i++) { _pulsen.Enqueue((adres, afbuigend)); _pulsTijden.Enqueue(SimTijd); }
            return;
        }
        VoerUit(adres, afbuigend);
    }
}
