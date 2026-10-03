namespace Baanverkenner.Kern;

/// <summary>Volgt de bezetmelders. Meldingen van de hardware komen binnen op de thread van
/// de seriële poort; de verkenner vraagt periodiek (Bijwerken) de ONTDENDERDE wijzigingen
/// op: een melder telt pas als bezet of vrij als hij OntdenderMs lang stabiel is. Zo kan
/// één flikkering geen verkeerde tak in de baankaart zetten.</summary>
public class MelderMonitor
{
    public record Wijziging(int Melder, bool Bezet, DateTime Tijd);

    private record Ruw(bool Bezet, DateTime Sinds);

    private readonly object _slot = new();
    private readonly Dictionary<int, Ruw> _ruw = new();
    private readonly Dictionary<int, bool> _stabiel = new();
    private readonly IKlok _klok;
    private readonly Func<int> _ontdenderMs;

    public MelderMonitor(IKlok klok, Func<int> ontdenderMs)
    {
        _klok = klok;
        _ontdenderMs = ontdenderMs;
    }

    /// <summary>Handler voor IHardwareInterface.BezetmeldingGewijzigd (elke thread).</summary>
    public void Ontvang(int melder, bool bezet)
    {
        lock (_slot)
        {
            if (!_ruw.TryGetValue(melder, out var r) || r.Bezet != bezet)
                _ruw[melder] = new Ruw(bezet, _klok.Nu);
        }
    }

    /// <summary>Geeft de wijzigingen die sinds de vorige aanroep stabiel geworden zijn, in
    /// de volgorde waarin ze op de baan gebeurden.</summary>
    public List<Wijziging> Bijwerken()
    {
        var nu = _klok.Nu;
        var drempel = TimeSpan.FromMilliseconds(Math.Max(0, _ontdenderMs()));
        var res = new List<Wijziging>();
        lock (_slot)
        {
            foreach (var (melder, ruw) in _ruw)
            {
                bool stabiel = _stabiel.TryGetValue(melder, out var s) && s;
                if (ruw.Bezet != stabiel && nu - ruw.Sinds >= drempel)
                {
                    _stabiel[melder] = ruw.Bezet;
                    res.Add(new Wijziging(melder, ruw.Bezet, ruw.Sinds));
                }
            }
        }
        res.Sort((a, b) => a.Tijd.CompareTo(b.Tijd));
        return res;
    }

    /// <summary>Melders die op dit moment (ontdenderd) bezet zijn.</summary>
    public HashSet<int> Bezet
    {
        get { lock (_slot) return _stabiel.Where(kv => kv.Value).Select(kv => kv.Key).ToHashSet(); }
    }

    public bool IsBezet(int melder)
    {
        lock (_slot) return _stabiel.TryGetValue(melder, out var s) && s;
    }
}
