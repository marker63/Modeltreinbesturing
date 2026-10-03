namespace Baanverkenner.Kern;

/// <summary>Tijdsbron van de verkenner. In het echte programma gewoon de wandklok; bij de
/// geautomatiseerde test een virtuele klok, zodat een verkenning van uren in seconden
/// nagespeeld kan worden.</summary>
public interface IKlok
{
    DateTime Nu { get; }
    Task Wacht(TimeSpan duur, CancellationToken token = default);
    /// <summary>Vuurt na elke wachtstap - de gesimuleerde proefbaan gebruikt dit om de loc
    /// te laten bewegen.</summary>
    event Action<DateTime>? Getikt;
}

public class EchteKlok : IKlok
{
    public DateTime Nu => DateTime.Now;
    public event Action<DateTime>? Getikt;

    public async Task Wacht(TimeSpan duur, CancellationToken token = default)
    {
        if (duur > TimeSpan.Zero) await Task.Delay(duur, token);
        Getikt?.Invoke(Nu);
    }
}

public class VirtueleKlok : IKlok
{
    private static readonly TimeSpan Stap = TimeSpan.FromMilliseconds(10);
    public DateTime Nu { get; private set; } = new DateTime(2026, 1, 1, 12, 0, 0);
    public event Action<DateTime>? Getikt;

    public Task Wacht(TimeSpan duur, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var eind = Nu + duur;
        do
        {
            var volgende = Nu + Stap;
            Nu = volgende > eind ? eind : volgende;
            Getikt?.Invoke(Nu);
        } while (Nu < eind);
        return Task.CompletedTask;
    }
}

/// <summary>Wandklok die sneller loopt: voor de simulatie-proefbaan, zodat een verkenning
/// van anderhalf uur in een paar minuten te bekijken is.</summary>
public class VersneldeKlok : IKlok
{
    private readonly DateTime _start = DateTime.Now;
    private readonly System.Diagnostics.Stopwatch _sw = System.Diagnostics.Stopwatch.StartNew();
    private readonly double _factor;

    public VersneldeKlok(double factor) { _factor = Math.Max(1, factor); }

    public DateTime Nu => _start + TimeSpan.FromTicks((long)(_sw.Elapsed.Ticks * _factor));
    public event Action<DateTime>? Getikt;

    public async Task Wacht(TimeSpan duur, CancellationToken token = default)
    {
        if (duur > TimeSpan.Zero) await Task.Delay(TimeSpan.FromTicks(Math.Max(1, (long)(duur.Ticks / _factor))), token);
        Getikt?.Invoke(Nu);
    }
}
