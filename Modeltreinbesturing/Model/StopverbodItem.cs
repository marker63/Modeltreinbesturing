namespace Modeltreinbesturing.Model;

/// <summary>
/// Eén regel in het stopverbod: een blok waar niet gestopt mag worden, optioneel alleen
/// voor een specifiek treintype. Treintype=null (de standaard) betekent: geldt voor ALLE
/// treintypes, precies het gedrag van vóór deze uitbreiding.
/// </summary>
public class StopverbodItem
{
    public required Blok Blok { get; set; }
    public Treintype? Treintype { get; set; }

    public override string ToString() =>
        Treintype != null ? $"Blok {Blok.Nummer} — alleen {Treintype.Omschrijving}" : $"Blok {Blok.Nummer} (alle treintypes)";
}
