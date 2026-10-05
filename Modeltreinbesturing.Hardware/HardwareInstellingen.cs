using System.IO;
using System.Text.Json;

namespace Modeltreinbesturing.Hardware;

/// <summary>Onthoudt welke hardware-interface (Simulatie/DCC-EX/Intellibox/Dinamo) en welke
/// COM-poort het laatst gekozen zijn, machine-breed - net als VensterInstellingen.cs, dus
/// geen projectgegevens (blijft dus ook gewoon staan als je een ander project opent/laadt).
/// Zonder dit moest je na elke herstart van de applicatie handmatig opnieuw naar Dinamo op
/// COM10 (of welke poort dan ook) terugschakelen, ook al had je dat de vorige keer al
/// ingesteld - inclusief het bewust terugzetten naar Simulatie, wat zonder dit óók steeds
/// weer vergeten werd (de applicatie start namelijk altijd met een kale, nieuwe
/// SimulatieHardware() als beginwaarde - zie HardwareBeheerder).</summary>
public static class HardwareInstellingen
{
    private static readonly string Pad = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Modeltreinbesturing", "hardware-instellingen.json");

    public record Keuze(string InterfaceType, string? ComPoort);

    /// <summary>Leest de laatst bewaarde keuze - null als er nog nooit iets bewaard is, of
    /// als het bestand om wat voor reden dan ook niet leesbaar is (nooit het opstarten van
    /// de applicatie blokkeren, zelfde filosofie als VensterInstellingen).</summary>
    public static Keuze? Laad()
    {
        try
        {
            if (File.Exists(Pad))
                return JsonSerializer.Deserialize<Keuze>(File.ReadAllText(Pad));
        }
        catch
        {
            // Beschadigd/onleesbaar instellingenbestand: gewoon zonder opgeslagen keuze
            // verdergaan (blijft dan simpelweg in Simulatie staan), niet laten crashen.
        }
        return null;
    }

    /// <summary>Bewaart de keuze - aan te roepen zodra de gebruiker EXPLICIET een interface
    /// kiest (HardwareDialog.Verbinden_Click, inclusief bewust "Geen"/Simulatie) of
    /// ontkoppelt (Ontkoppelen_Click). NIET aanroepen bij een automatisch mislukte
    /// heraansluitpoging bij het opstarten - dat mag de eerder bewaarde, kennelijk ooit wel
    /// werkende keuze niet overschrijven (bijv. de kabel zit er nu even niet in, maar
    /// morgen weer wel).</summary>
    public static void Bewaar(string interfaceType, string? comPoort)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Pad)!);
            File.WriteAllText(Pad, JsonSerializer.Serialize(new Keuze(interfaceType, comPoort)));
        }
        catch
        {
            // Opslaan van deze voorkeur is puur comfort - een schrijffout hier mag de rest
            // van de applicatie niet verstoren.
        }
    }

    /// <summary>Het korte type-label dat Bewaar/Laad gebruikt, afgeleid van het daadwerkelijke
    /// IHardwareInterface-object - één centrale plek die weet welke klasse bij welk label
    /// hoort, zodat MaakInterface hieronder altijd de spiegeling blijft van dit label.</summary>
    public static string TypeVan(IHardwareInterface iface) => iface switch
    {
        DinamoHardware => "Dinamo",
        DccExHardware => "DccEx",
        IntelliboxHardware => "Intellibox",
        Z21Hardware => "Z21",
        _ => "Simulatie"
    };

    /// <summary>Maakt een NIEUWE, nog niet verbonden instantie van het opgegeven type -
    /// null bij een onbekend/verouderd label (bijv. een oudere versie van dit
    /// instellingenbestand met een inmiddels verwijderd interface-type).</summary>
    public static IHardwareInterface? MaakInterface(string type) => type switch
    {
        "Dinamo" => new DinamoHardware(),
        "DccEx" => new DccExHardware(),
        "Intellibox" => new IntelliboxHardware(),
        "Z21" => new Z21Hardware(),
        "Simulatie" => new SimulatieHardware(),
        _ => null
    };
}
