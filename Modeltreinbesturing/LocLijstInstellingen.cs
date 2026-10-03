using System.IO;
using System.Text.Json;

namespace Modeltreinbesturing;

/// <summary>Onthoudt welke kolommen zichtbaar zijn in "Overzicht locomotieven" (kijkscherm) -
/// machine-breed, net als VensterInstellingen/HardwareInstellingen, dus los van een
/// specifiek projectbestand (dezelfde kolomkeuze geldt voor elk project dat je opent).
/// Gebruikersverzoek: "de mogelijkheid om meerdere kolommen toe te voegen en/of niet meer
/// zichtbaar te maken."</summary>
public static class LocLijstInstellingen
{
    private static readonly string Pad = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Modeltreinbesturing", "loclijst-kolommen.json");

    /// <summary>Leest de laatst bewaarde kolomkeuze (een lijst van kolom-sleutels, zie
    /// BaanontwerpWindow.BeschikbareLocKolommen) - null als er nog nooit iets bewaard is
    /// (dan gebruikt de aanroeper de ingebouwde standaardkeuze).</summary>
    public static List<string>? Laad()
    {
        try
        {
            if (File.Exists(Pad))
                return JsonSerializer.Deserialize<List<string>>(File.ReadAllText(Pad));
        }
        catch
        {
            // Beschadigd/onleesbaar instellingenbestand: gewoon met de standaardkeuze
            // verdergaan, niet laten crashen.
        }
        return null;
    }

    public static void Bewaar(List<string> zichtbareKolomSleutels)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Pad)!);
            File.WriteAllText(Pad, JsonSerializer.Serialize(zichtbareKolomSleutels));
        }
        catch
        {
            // Opslaan van deze voorkeur is puur comfort - een schrijffout hier mag de rest
            // van de applicatie niet verstoren.
        }
    }
}
