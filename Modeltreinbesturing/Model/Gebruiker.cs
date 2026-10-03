namespace Modeltreinbesturing.Model;

/// <summary>
/// Een account dat toegang heeft tot de "bouwschermen" (blokkenschema, baanontwerp
/// bewerken, treinroutes) - het kijkscherm zelf heeft bewust GEEN account nodig, dat blijft
/// voor iedereen vrij toegankelijk. Twee niveaus:
/// - Echte beheerder (IsAdmin=true): kan ook andere accounts aanmaken/verwijderen.
/// - Gewone bouwscherm-gebruiker (IsAdmin=false): kan de bouwschermen gebruiken en daar
///   aanpassingen maken, maar geen andere accounts beheren.
/// Het wachtwoord wordt NOOIT als platte tekst bewaard - alleen een PBKDF2-hash + eigen
/// salt (zie GebruikersBeheerder).
/// </summary>
public class Gebruiker
{
    public string Naam { get; set; } = "";
    public string WachtwoordHash { get; set; } = "";
    public string WachtwoordSalt { get; set; } = "";
    public bool IsAdmin { get; set; }

    public override string ToString() => Naam + (IsAdmin ? " (beheerder)" : "");
}
