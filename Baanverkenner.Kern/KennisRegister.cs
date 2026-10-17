namespace Baanverkenner.Kern;

/// <summary>BUG #181 (Marco 17-10 12:01): "het lijkt of je heel veel standen en melders niet juist vasthoudt en telkens weer overschrijft - ga dat ook maar bijhouden".
/// Eén ding dat eenmaal vastgesteld is (blok van een melder, kruiswissel-melder, rijrichting van een overgang, stand van een wissel waar de loc op staat...)
/// mag alleen veranderen als daar een reden voor is. Dit register onthoudt per feit de laatste waarde en legt elke afwijkende nieuwe waarde (of wissing) vast,
/// met de bron. In het log staat dan "OVERSCHREVEN"; in de kaart/het rapport staat de lijst, en de regressietests eisen dat die lijst in de simulator leeg blijft.</summary>
public sealed class KennisRegister
{
    public sealed class Overschrijving
    {
        public DateTime Tijd { get; set; }
        public string Soort { get; set; } = "";
        public string Sleutel { get; set; } = "";
        public string Oud { get; set; } = "";
        public string Nieuw { get; set; } = "";
        public string Bron { get; set; } = "";
        public override string ToString() => $"{Soort} {Sleutel}: {Oud} -> {Nieuw} ({Bron})";
    }

    private readonly Dictionary<string, string> _waarden = new();
    private readonly List<Overschrijving> _lijst = new();
    public IReadOnlyList<Overschrijving> Lijst => _lijst;
    public int Bevestigingen { get; private set; }
    /// <summary>Wordt aangeroepen bij elke overschrijving (log).</summary>
    public Action<Overschrijving>? Gemeld { get; set; }
    public Func<DateTime> Nu { get; set; } = () => DateTime.Now;

    /// <summary>Legt een feit vast zonder het als overschrijving te tellen (laden van een eerdere kaart).</summary>
    public void Zaai(string soort, string sleutel, string waarde) => _waarden[soort + "|" + sleutel] = waarde;

    /// <summary>Legt een feit vast. Geeft true terug als dit een eerder vastgelegde, andere waarde overschreef.</summary>
    public bool Leg(string soort, string sleutel, string waarde, string bron)
    {
        string k = soort + "|" + sleutel;
        if (_waarden.TryGetValue(k, out var oud) && oud != waarde)
        {
            Meld(soort, sleutel, oud, waarde, bron);
            _waarden[k] = waarde;
            return true;
        }
        if (oud is not null) Bevestigingen++;
        _waarden[k] = waarde;
        return false;
    }

    /// <summary>Een eerder vastgelegd feit wordt tijdelijk gewist (bv. blok van een melder op null voor een nieuwe blokproef). De oude waarde blijft onthouden: komt dezelfde waarde
    /// terug (of wordt het oude blok hersteld), dan is er niets verloren; komt er een andere waarde voor in de plaats, dan meldt <see cref="Leg"/> een overschrijving.
    /// Een wissing alleen telt dus niet, maar wordt wel geteld in <see cref="Wissingen"/>.</summary>
    public void Wis(string soort, string sleutel, string bron)
    {
        if (_waarden.ContainsKey(soort + "|" + sleutel)) Wissingen++;
    }
    public int Wissingen { get; private set; }

    public string? Waarde(string soort, string sleutel) => _waarden.TryGetValue(soort + "|" + sleutel, out var w) ? w : null;

    private void Meld(string soort, string sleutel, string oud, string nieuw, string bron)
    {
        var o = new Overschrijving { Tijd = Nu(), Soort = soort, Sleutel = sleutel, Oud = oud, Nieuw = nieuw, Bron = bron };
        _lijst.Add(o);
        Gemeld?.Invoke(o);
    }
}
