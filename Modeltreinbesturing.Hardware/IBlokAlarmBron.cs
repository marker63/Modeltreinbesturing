namespace Modeltreinbesturing.Hardware;

/// <summary>
/// BUG #43: optionele uitbreiding op <see cref="IHardwareInterface"/> voor koppelingen die
/// PER BLOK melden dat er een alarm (kortsluiting) is - nu alleen Dinamo (datagram 0x30/0x32,
/// "Block Alarm"). Bewust een aparte interface in plaats van een extra lid op
/// IHardwareInterface: DCC-EX, Intellibox en de simulatie kunnen dit niet, en hoeven er dus
/// niets van te weten. Gebruik: <c>if (hw is IBlokAlarmBron bron) bron.BlokAlarmGewijzigd += ...;</c>
///
/// LET OP: het event komt op de achtergrondthread van de seriële poort binnen, niet op de
/// UI-thread (zelfde als BezetmeldingGewijzigd/KortsluitingStatusGewijzigd).
/// </summary>
public interface IBlokAlarmBron
{
    /// <summary>(Dinamo-blok zoals in de software, dus 1-based = Blok.Nummer; true = er is
    /// kortsluiting in dat blok, false = die melding is weer opgeheven).</summary>
    event Action<int, bool>? BlokAlarmGewijzigd;
}
