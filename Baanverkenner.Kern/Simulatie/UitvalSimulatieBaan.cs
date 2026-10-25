using Modeltreinbesturing.Hardware;

namespace Baanverkenner.Kern.Simulatie;

/// <summary>BUG #173: simulatie met een Dinamo-uitval zoals op de baan (17-10 08:16): vanaf een bepaald wisselcommando voert Dinamo gedurende een tijd geen
/// wissel-commando's uit en meldt ze niet terug; daarna werkt alles weer. Meldt wel terug als het commando uitgevoerd is (<see cref="IWisselBevestiging"/>).</summary>
public class UitvalSimulatieBaan : SimulatieBaan, IWisselBevestiging
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

    public override void ZetWissel(int adres, bool afbuigend)
    {
        _teller++;
        var nu = SimTijd;
        if (_teller >= UitvalVanafCommando) _uitvalStart ??= nu;
        if (_uitvalStart is { } s && nu - s < UitvalDuur) { GenegeerdeCommandos++; return; }
        if (NegeerHelftBijKortsluiting && HeeftKortsluiting && _rnd.NextDouble() < 0.5) { GenegeerdBijKortsluiting++; return; }
        base.ZetWissel(adres, afbuigend);
        _bevestigingen++;
        _perStand[(adres, afbuigend)] = _perStand.TryGetValue((adres, afbuigend), out var n) ? n + 1 : 1;   // BUG #307
    }
}
