namespace Modeltreinbesturing.Model;

/// <summary>
/// Een richtingsverbod: verbiedt dat een trein van Van naar Naar rijdt, optioneel alleen
/// als hij daarvoor uit Uit kwam (Uit=null betekent: altijd verboden, ongeacht waarvandaan).
/// Zoals Koploper's "Onderhouden variabele treinroute" - Richtingsverbod-tabblad, met
/// dezelfde "Van -> Naar (uit Uit)"-notatie.
/// </summary>
public class Richtingsverbod
{
    public required Blok Van { get; set; }
    public required Blok Naar { get; set; }
    public Blok? Uit { get; set; }

    /// <summary>Optioneel: geldt dit verbod alleen voor een specifiek treintype? Null (de
    /// standaard) betekent: geldt voor ALLE treintypes, zoals voorheen (dus bestaand
    /// gedrag/opgeslagen data blijft ongewijzigd werken). Hergebruikt ook door
    /// StopverbodStilstandBeheerder, die dit model deelt.</summary>
    public Treintype? Treintype { get; set; }

    public override string ToString()
    {
        string basis = Uit != null ? $"{Van.Nummer} -> {Naar.Nummer} (uit {Uit.Nummer})" : $"{Van.Nummer} -> {Naar.Nummer} (altijd)";
        return Treintype != null ? $"{basis} — alleen {Treintype.Omschrijving}" : basis;
    }
}
