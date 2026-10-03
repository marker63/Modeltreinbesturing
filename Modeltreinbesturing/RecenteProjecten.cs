using System.IO;
using System.Text.Json;

namespace Modeltreinbesturing;

/// <summary>Onthoudt de laatst geopende projectbestanden tussen sessies, zodat je niet
/// steeds handmatig naar hetzelfde bestand hoeft te bladeren. Puur een lijstje bestandspaden
/// - GEEN projectgegevens zelf (die blijven in het eigen .json-bestand). Zelfde
/// opslagpatroon als VensterInstellingen.cs.</summary>
public static class RecenteProjecten
{
    private static readonly string Pad = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Modeltreinbesturing", "recente-projecten.json");

    private const int MaxAantal = 8;

    private static List<string>? _cache;

    private static List<string> Laad()
    {
        if (_cache != null) return _cache;
        try
        {
            if (File.Exists(Pad))
            {
                var json = File.ReadAllText(Pad);
                _cache = JsonSerializer.Deserialize<List<string>>(json) ?? new();
                return _cache;
            }
        }
        catch
        {
            // Een beschadigd/onleesbaar instellingenbestand mag het opstarten van de app
            // nooit blokkeren - gewoon met een lege lijst verdergaan.
        }
        _cache = new();
        return _cache;
    }

    private static void BewaarNaarSchijf()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Pad)!);
            File.WriteAllText(Pad, JsonSerializer.Serialize(_cache));
        }
        catch
        {
            // Puur comfort - een schrijffout hier mag de rest van de applicatie niet verstoren.
        }
    }

    /// <summary>De meest recent geopende projecten eerst. Bestanden die inmiddels
    /// verplaatst/verwijderd zijn worden er stilletjes uit gefilterd (geen zin een
    /// dood pad in de lijst te tonen).</summary>
    public static List<string> Lijst() => Laad().Where(File.Exists).ToList();

    /// <summary>Zet dit pad bovenaan (of voegt het toe als het er nog niet in stond) - roep
    /// dit aan bij zowel het OPSLAAN als het LADEN van een project.</summary>
    public static void Vermeld(string projectPad)
    {
        var lijst = Laad();
        lijst.RemoveAll(p => string.Equals(p, projectPad, StringComparison.OrdinalIgnoreCase));
        lijst.Insert(0, projectPad);
        while (lijst.Count > MaxAantal) lijst.RemoveAt(lijst.Count - 1);
        BewaarNaarSchijf();
    }
}
