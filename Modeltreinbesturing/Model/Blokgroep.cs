namespace Modeltreinbesturing.Model;

/// <summary>
/// "Onderhouden blokgroepen" uit het echte Koploper: een groep blokken die onderling
/// gelijkwaardige alternatieven zijn (bijv. meerdere parallelle opstelsporen in een
/// schaduwstation, allemaal bereikbaar via dezelfde inrit). Twee eigenschappen zijn
/// van belang, precies zoals in Koploper:
/// - EnkeleTreinbeweging: er mag maar in ÉÉN blok van de hele groep tegelijk een trein
///   actief zijn (rijdend of gereserveerd) - de andere blokken in de groep gelden
///   ondertussen als "bezet" voor nieuwe ritten, ook al staat er zelf geen trein.
/// - GecombineerdeStopmelder: de blokken in de groep delen dezelfde stopmelder-aanpak,
///   zodat je niet voor elk afzonderlijk opstelspoor een eigen stopmelder-configuratie
///   hoeft te maken.
/// </summary>
public class Blokgroep
{
    public string Naam { get; set; } = "";
    public List<Blok> Blokken { get; set; } = new();
    public bool EnkeleTreinbeweging { get; set; } = true;
    public bool GecombineerdeStopmelder { get; set; } = true;

    public override string ToString() => $"{Naam} ({Blokken.Count} blokken)";
}
