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

    /// <summary>Het wisselcommando (tellend sinds het verbinden) waarmee de uitval begint.</summary>
    public int UitvalVanafCommando { get; set; } = int.MaxValue;
    public TimeSpan UitvalDuur { get; set; } = TimeSpan.FromSeconds(75);
    /// <summary>Het aantal commando's dat tijdens de uitval niet is uitgevoerd.</summary>
    public int GenegeerdeCommandos { get; private set; }

    public override void ZetWissel(int adres, bool afbuigend)
    {
        _teller++;
        var nu = SimTijd;
        if (_teller >= UitvalVanafCommando) _uitvalStart ??= nu;
        if (_uitvalStart is { } s && nu - s < UitvalDuur) { GenegeerdeCommandos++; return; }
        base.ZetWissel(adres, afbuigend);
        _bevestigingen++;
    }
}
