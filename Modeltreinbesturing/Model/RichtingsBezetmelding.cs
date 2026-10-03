namespace Modeltreinbesturing.Model;

/// <summary>
/// Koploper's "Bezetmeldingen"-tabblad in de blokeigenschappen: per richting WAARUIT een
/// blok bereikt wordt (dus per "Uit blok") een eigen, GEORDENDE lijst bezetmelder-nummers
/// die verwacht worden af te gaan terwijl de trein via DIE SPECIFIEKE route het blok
/// binnenrijdt - in plaats van één generiek Voorblok/Stopsectie-meldpunt (Bezetmeldpunt)
/// dat voor elke aankomstrichting hetzelfde zou zijn. Nodig omdat de fysieke sensoren die
/// afgaan (en de VOLGORDE waarin) kunnen verschillen per wissel-pad, ook al land je
/// uiteindelijk in hetzelfde blok - en omdat "moet deze loc hier keren" soms ook per
/// aankomstrichting verschilt (zie Model.BlokRelatie.Keer, die dat al wél per richting
/// regelt - dit is het bezetmelder-equivalent daarvan).
///
/// De EERSTE melder in MeldernNummers is het "aankomst"-signaal (vervangt Bezetmeldpunt.
/// Rol=Voorblok, maar dan alleen voor DEZE richting); de LAATSTE is het "moet stoppen"-
/// signaal (vervangt Rol=Stopsectie). Tussenliggende melders worden momenteel alleen
/// gebruikt om te voorkomen dat ze onterecht als spookmelding gezien worden - ze sturen
/// zelf geen aparte simulatiestap aan (Koploper's volledige, stapsgewijze opvolging van
/// elke tussenliggende melder is hier bewust vereenvoudigd tot "eerste = aankomst, laatste
/// = stop", in lijn met hoe Bezetmeldpunt dat nu ook al doet).
///
/// Is er voor een specifieke (VanBlok, dit blok)-combinatie GEEN entry aangemaakt, dan
/// valt de simulatie terug op de generieke Bezetmeldpunten van het blok (Voorblok/
/// Stopsectie) - bestaande projecten/configuratie blijven dus gewoon werken zonder dat je
/// dit ooit hoeft in te vullen.
/// </summary>
public class RichtingsBezetmelding
{
    public required Blok VanBlok { get; set; }

    /// <summary>Geordende meldernummers, bijv. [1, 9, 10] - exact zoals Koploper's "Te
    /// verwachten bezetmelders"-kolom (ruimte-gescheiden weergegeven in de UI).</summary>
    public List<int> MeldernNummers { get; set; } = new();

    /// <summary>Koploper: "Alternatieve stopplaats" - optioneel een ANDER blok waar de
    /// trein in plaats daarvan tot stilstand komt als de reguliere stopsectie onverwacht
    /// al bezet is (bijv. door een andere trein). Nog niet gebruikt in de simulatie-engine
    /// zelf (best-effort placeholder die wel al het datamodel van Koploper matcht) - een
    /// latere uitbreiding kan hier concreet gedrag aan koppelen.</summary>
    public Blok? AlternatieveStopplaats { get; set; }

    /// <summary>Koploper: "Omlopen" - staat dit AAN, dan mag deze richting ook gebruikt
    /// worden als de trein via een lus weer op hetzelfde blok terugkomt i.p.v. dat als een
    /// nieuwe, aparte aankomst te behandelen. Nog niet gebruikt in de simulatie-engine zelf
    /// (best-effort placeholder, zie AlternatieveStopplaats hierboven).</summary>
    public bool MagOmlopen { get; set; }

    /// <summary>Gebruikersverzoek ("bezetmelders en volgorde zelflerend maken"): staat AAN
    /// als deze entry NIET handmatig door de gebruiker is ingevoerd (Blok-eigenschappen-
    /// dialoog), maar automatisch is afgeleid uit echte, waargenomen bezetmeldingen tijdens
    /// het rijden (zie TreinrouteWindow.LeerBezetmelderVolgorde). Een automatisch geleerde
    /// entry mag door een latere, andere waarneming worden bijgewerkt; een handmatig
    /// ingevoerde entry (AutomatischGeleerd=false, de standaardwaarde) wordt NOOIT
    /// automatisch overschreven - de software respecteert dan altijd jouw eigen,
    /// bewuste configuratie.</summary>
    public bool AutomatischGeleerd { get; set; }

    public override string ToString() =>
        $"Uit blok {VanBlok.Nummer}: {string.Join(" ", MeldernNummers)}" + (AutomatischGeleerd ? " (automatisch geleerd)" : "");
}
