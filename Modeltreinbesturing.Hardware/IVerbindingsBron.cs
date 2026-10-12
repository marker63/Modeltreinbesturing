namespace Modeltreinbesturing.Hardware;

/// <summary>
/// BUG #81: optionele uitbreiding op <see cref="IHardwareInterface"/> voor koppelingen die
/// bijhouden wanneer er voor het laatst een bericht (ook een keepalive) van de centrale
/// binnenkwam. Zo is een vastgelopen centrale (Dinamo stuurt niets meer terug terwijl wij
/// wel blijven sturen) te onderscheiden van een echte kortsluiting. Alleen Dinamo.
/// </summary>
public interface IVerbindingsBron
{
    /// <summary>Seconden sinds het laatste ontvangen bericht (keepalives meegeteld);
    /// null als er nog niets ontvangen is.</summary>
    double? SecondenSindsLaatsteOntvangst { get; }

    /// <summary>Wordt afgevuurd als de centrale ophoudt met antwoorden (stil = true, tweede waarde = seconden stilte tot nu toe, vanaf de drempel)
    /// en als er weer bericht komt (stil = false, tweede waarde = duur van de stilte). Komt op een achtergrondthread binnen.</summary>
    event Action<bool, double>? StilteGewijzigd;
}
