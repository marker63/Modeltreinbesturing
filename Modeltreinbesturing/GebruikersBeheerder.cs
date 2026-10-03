using System.Security.Cryptography;
using Modeltreinbesturing.Model;

namespace Modeltreinbesturing;

/// <summary>
/// Beheert de accounts die toegang hebben tot de "bouwschermen". Hoort bij ÉÉN database/
/// project (wordt meegenomen in ProjectOpslag), niet globaal voor de hele applicatie - elke
/// nieuwe database krijgt bij het aanmaken zijn eigen eerste beheerder-account.
/// </summary>
public class GebruikersBeheerder
{
    public List<Gebruiker> Gebruikers { get; } = new();

    public bool HeeftAlEenBeheerder => Gebruikers.Any(g => g.IsAdmin);

    /// <summary>Maakt een nieuw account aan met een veilig gehasht wachtwoord (PBKDF2,
    /// 100.000 iteraties, eigen willekeurige salt per account - nooit het platte wachtwoord
    /// zelf bewaard). Geeft false terug (en maakt niets aan) als de naam al bestaat.</summary>
    public bool MaakGebruiker(string naam, string wachtwoord, bool isAdmin)
    {
        if (string.IsNullOrWhiteSpace(naam) || Gebruikers.Any(g => g.Naam.Equals(naam, StringComparison.OrdinalIgnoreCase)))
            return false;

        var saltBytes = RandomNumberGenerator.GetBytes(16);
        var hashBytes = Rfc2898DeriveBytes.Pbkdf2(wachtwoord, saltBytes, 100_000, HashAlgorithmName.SHA256, 32);
        Gebruikers.Add(new Gebruiker
        {
            Naam = naam,
            WachtwoordSalt = Convert.ToBase64String(saltBytes),
            WachtwoordHash = Convert.ToBase64String(hashBytes),
            IsAdmin = isAdmin
        });
        return true;
    }

    /// <summary>Controleert naam+wachtwoord tegen het opgeslagen hash+salt. Geeft de
    /// bijbehorende Gebruiker terug bij een geslaagde login, anders null.</summary>
    public Gebruiker? Verifieer(string naam, string wachtwoord)
    {
        var gebruiker = Gebruikers.FirstOrDefault(g => g.Naam.Equals(naam, StringComparison.OrdinalIgnoreCase));
        if (gebruiker is null) return null;

        var saltBytes = Convert.FromBase64String(gebruiker.WachtwoordSalt);
        var berekendeHash = Rfc2898DeriveBytes.Pbkdf2(wachtwoord, saltBytes, 100_000, HashAlgorithmName.SHA256, 32);
        var opgeslagenHash = Convert.FromBase64String(gebruiker.WachtwoordHash);

        // FixedTimeEquals i.p.v. een simpele byte-vergelijking, om timing-aanvallen op het
        // wachtwoord te bemoeilijken.
        return CryptographicOperations.FixedTimeEquals(berekendeHash, opgeslagenHash) ? gebruiker : null;
    }

    public void VerwijderGebruiker(Gebruiker gebruiker) => Gebruikers.Remove(gebruiker);

    public void Reset() => Gebruikers.Clear();
}
