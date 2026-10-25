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

    public MelderMonitor(IKlok klok, Func<int> ontdenderMs, Func<int, bool>? isBekend = null)
    {
        _klok = klok;
        _ontdenderMs = ontdenderMs;
        _isBekend = isBekend;
    }

    /// <summary>BUG #262 (Marco 20-10 18:58: "knap dat je melders 138 en 137 gevonden hebt, die bestaan volgens het melderblokkenschema niet"; hardwarelog 18-10 14:52:38 en 20-10 18:43:34,
    /// 18:56:29): Dinamo meldt melder 137 en 138 echt, maar alleen terwijl de loc stilstaat of kruipt op 129/10, en ze knipperen elke 0,6 s aan en uit. Een melder die de kaart nog niet kent
    /// en binnen <see cref="FlikkerVenster"/> minstens <see cref="FlikkerWissels"/> keer van stand wisselt, is geen bezetmelder van een sectie: hij telt niet mee als bezet en levert geen
    /// wijzigingen meer (de hele sessie). Melders die de kaart al kent vallen hier nooit onder.</summary>
    public static readonly TimeSpan FlikkerVenster = TimeSpan.FromSeconds(6);
    public const int FlikkerWissels = 6;
    private readonly Func<int, bool>? _isBekend;
    private readonly Dictionary<int, List<DateTime>> _wisselTijden = new();
    private readonly HashSet<int> _flikkeraars = new();
    /// <summary>BUG #262: wordt aangeroepen (één keer per melder) als een melder als knipperende niet-sectiemelder wordt herkend.</summary>
    public Action<int>? FlikkeraarGevonden { get; set; }
    public IReadOnlyCollection<int> Flikkeraars { get { lock (_slot) return _flikkeraars.ToList(); } }

    /// <summary>BUG #306 (autolog 25-10 09:50:05): Dinamo meldde melder 268 (bestaat niet) bezet tussen 25 en 17 en daarna nooit meer vrij. De loc is niet langer dan een melder
    /// (Marco 21-10), dus een melder die bezet blijft terwijl daarna <see cref="VastzitterNaMelders"/> andere melders bezet raken, is een vastzittende melding en geen loc: hij telt
    /// niet meer mee als bezet tot Dinamo hem weer vrij meldt. Alleen melders die de kaart al kent tellen als 'verder'.</summary>
    public const int VastzitterNaMelders = 3;
    private readonly Dictionary<int, HashSet<int>> _naBezet = new();
    private readonly HashSet<int> _vastzitters = new();
    public Action<int>? VastzitterGevonden { get; set; }
    public IReadOnlyCollection<int> Vastzitters { get { lock (_slot) return _vastzitters.ToList(); } }

    /// <summary>Handler voor IHardwareInterface.BezetmeldingGewijzigd (elke thread).</summary>
    public void Ontvang(int melder, bool bezet)
    {
        int? nieuw = null;
        lock (_slot)
        {
            if (!_ruw.TryGetValue(melder, out var r) || r.Bezet != bezet)
            {
                _ruw[melder] = new Ruw(bezet, _klok.Nu);
                if (r is not null && !_flikkeraars.Contains(melder) && _isBekend is { } bekend && !bekend(melder))
                {
                    if (!_wisselTijden.TryGetValue(melder, out var lijst)) _wisselTijden[melder] = lijst = new List<DateTime>();
                    lijst.Add(_klok.Nu);
                    lijst.RemoveAll(t => _klok.Nu - t > FlikkerVenster);
                    if (lijst.Count >= FlikkerWissels)
                    {
                        _flikkeraars.Add(melder);
                        _stabiel.Remove(melder);
                        nieuw = melder;
                    }
                }
            }
        }
        if (nieuw is int n) FlikkeraarGevonden?.Invoke(n);
    }

    /// <summary>Geeft de wijzigingen die sinds de vorige aanroep stabiel geworden zijn, in
    /// de volgorde waarin ze op de baan gebeurden.</summary>
    public List<Wijziging> Bijwerken()
    {
        var nu = _klok.Nu;
        var drempel = TimeSpan.FromMilliseconds(Math.Max(0, _ontdenderMs()));
        var res = new List<Wijziging>();
        var gevonden = new List<int>();
        lock (_slot)
        {
            foreach (var (melder, ruw) in _ruw)
            {
                if (_flikkeraars.Contains(melder)) continue;   // BUG #262
                if (_vastzitters.Contains(melder))
                {
                    // BUG #306: pas weer gewoon als Dinamo hem echt vrij meldt
                    if (!ruw.Bezet && nu - ruw.Sinds >= drempel) { _vastzitters.Remove(melder); _stabiel[melder] = false; }
                    continue;
                }
                bool stabiel = _stabiel.TryGetValue(melder, out var s) && s;
                if (ruw.Bezet != stabiel && nu - ruw.Sinds >= drempel)
                {
                    _stabiel[melder] = ruw.Bezet;
                    res.Add(new Wijziging(melder, ruw.Bezet, ruw.Sinds));
                }
            }
            // BUG #306: welke melders raakten bezet terwijl een andere melder bezet bleef
            foreach (var w in res.OrderBy(x => x.Tijd))
            {
                if (w.Bezet)
                {
                    // alleen melders die de kaart al kent tellen (een kortsluitecho als 137/138 terwijl de loc stilstaat telt niet, simulatie #268)
                    if (_isBekend is null || _isBekend(w.Melder))
                        foreach (var (m, na) in _naBezet) if (m != w.Melder) na.Add(w.Melder);
                    _naBezet[w.Melder] = new HashSet<int>();
                }
                else _naBezet.Remove(w.Melder);
            }
            foreach (var (m, na) in _naBezet.ToList())
            {
                if (na.Count < VastzitterNaMelders || !(_stabiel.TryGetValue(m, out var sb) && sb)) continue;
                _vastzitters.Add(m);
                _stabiel[m] = false;
                _naBezet.Remove(m);
                res.RemoveAll(x => x.Melder == m);
                gevonden.Add(m);
            }
        }
        foreach (var m in gevonden) VastzitterGevonden?.Invoke(m);
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
