namespace Modeltreinbesturing.Model;

public enum BezetmeldpuntRol
{
    /// <summary>Dekt het hele blok, exclusief de stopsectie. Meldt: "trein is aangekomen".</summary>
    Voorblok,

    /// <summary>Dekt het laatste stukje van het blok. Meldt: "trein staat/moet stoppen".</summary>
    Stopsectie
}

/// <summary>
/// Een bezetmeldpunt: de "ogen" van het systeem. Gekoppeld aan een fysiek
/// meldernummer op het digitale systeem.
/// </summary>
public class Bezetmeldpunt
{
    public int MeldernNummer { get; set; }
    public BezetmeldpuntRol Rol { get; set; }

    /// <summary>Koploper: "ik heb bij de stopmelder (2e melder) opgegeven dat het vorige
    /// blok vrijgegeven mag worden" - standaard AAN (matcht ons bestaande, altijd-vrijgeven
    /// gedrag). Zet je dit UIT voor een specifiek meldpunt, dan blijft het blok dat de
    /// trein zojuist verliet bewust bezet/gereserveerd staan zodra DEZE melder de aankomst
    /// van de trein meldt - bijv. voor een schaduwstation waar je de vrijgave liever pas
    /// later, expliciet, wilt laten gebeuren. Het blok komt dan pas weer vrij bij het einde
    /// van de rit (het eindblok wordt daar sowieso altijd vrijgegeven) of handmatig.
    /// LET OP: onze engine kent geen exact-gedocumenteerde 1-op-1 vertaling van Koploper's
    /// eigen trigger-mechaniek hiervoor - dit is een best-effort, eigen interpretatie:
    /// bij ONS wordt het vorige blok vrijgegeven zodra de trein bij DEZE melder aankomt
    /// (ongeacht Voorblok/Stopsectie-rol), dus dat is ook waar deze vlag op aangrijpt.</summary>
    public bool MagVorigBlokVrijgeven { get; set; } = true;

    /// <summary>Alleen relevant bij Rol=Stopsectie: "dynamische lengte" zoals het echte
    /// Koploper dat kent (vooral bij schaduwstations) - een blok kan MEERDERE stopsecties
    /// hebben, elk geschikt tot een bepaalde maximale treinlengte (cm), zodat een korte
    /// trein eerder stopt en een lange trein verder het blok in rijdt. 0 = geen limiet
    /// (geschikt voor elke lengte, bijv. de enige/standaard stopsectie in een gewoon blok).</summary>
    public double MaxTreinlengte { get; set; }

    public override string ToString()
    {
        string basis = Rol == BezetmeldpuntRol.Stopsectie && MaxTreinlengte > 0
            ? $"Melder {MeldernNummer} (Stopsectie, tot {MaxTreinlengte:0} cm)"
            : $"Melder {MeldernNummer} ({Rol})";
        return MagVorigBlokVrijgeven ? basis : $"{basis} — vorig blok blijft bezet";
    }
}
