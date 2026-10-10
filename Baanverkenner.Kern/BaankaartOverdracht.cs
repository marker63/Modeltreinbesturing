namespace Baanverkenner.Kern;

/// <summary>Vaste plek waar de Baanverkenner de baankaart neerzet voor de import in Modeltreinbesturing
/// (twee losse programma's op dezelfde pc).</summary>
public static class BaankaartOverdracht
{
    public static string Map => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Modeltreinbesturing", "Baanverkenner", "voor_import");

    public static string Pad => Path.Combine(Map, "baankaart_voor_import.json");

    public static string Zet(Baankaart kaart)
    {
        Directory.CreateDirectory(Map);
        File.WriteAllText(Pad, kaart.AlsJson(), new System.Text.UTF8Encoding(false));
        return Pad;
    }

    /// <summary>Is er een nog niet verwerkte baankaart klaargezet? Geeft het tijdstip terug.</summary>
    public static DateTime? Klaar() => File.Exists(Pad) ? File.GetLastWriteTime(Pad) : null;

    /// <summary>Markeert de overdracht als verwerkt (hernoemt het bestand, zodat het niet nog eens aangeboden wordt).</summary>
    public static void MarkeerVerwerkt()
    {
        try
        {
            if (!File.Exists(Pad)) return;
            string doel = Path.Combine(Map, $"baankaart_verwerkt_{DateTime.Now:yyyy-MM-dd_HHmmss}.json");
            File.Move(Pad, doel);
        }
        catch { /* niet erg: het bestand wordt dan nog een keer aangeboden */ }
    }
}
