using Modeltreinbesturing.Model;

namespace Modeltreinbesturing;

/// <summary>Beheert de onderhoudslog (incidenten en wijzigingen) - zie Model.OnderhoudsRecord
/// voor de achtergrond.</summary>
public class OnderhoudsBeheerder
{
    public List<OnderhoudsRecord> Records { get; } = new();

    public void VerwijderRecord(OnderhoudsRecord record) => Records.Remove(record);

    /// <summary>Alle records voor een specifiek object, meest recente eerst - handig om
    /// snel de geschiedenis van bijv. "Wissel 12" te zien. Simpele, hoofdletterongevoelige
    /// tekstmatch (geen exacte-object-verwijzing, zie Model.OnderhoudsRecord voor waarom).</summary>
    public IEnumerable<OnderhoudsRecord> VoorObject(string objectTekst) =>
        Records.Where(r => r.Object.Contains(objectTekst, StringComparison.OrdinalIgnoreCase)).OrderByDescending(r => r.Datum);

    public void Reset() => Records.Clear();
}
