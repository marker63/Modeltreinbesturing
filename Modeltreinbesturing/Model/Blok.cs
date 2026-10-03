namespace Modeltreinbesturing.Model;

/// <summary>Het soort blok, zoals in het echte Koploper "Eigenschappen blok"-scherm - beïnvloedt
/// hoe het blok zich gedraagt (een kopspoor heeft geen "erna", een opstelspoor is bedoeld om
/// een trein langer te laten staan).</summary>
public enum BlokType { Normaal, Kopspoor, Opstelspoor, Station }

/// <summary>Wat er moet gebeuren als een te lange trein (langer dan MaxTreinlengte) dit
/// blok berijdt - vastgelegd per blok, zoals de gebruiker aangaf: dit is een eigenschap
/// van het BLOK, niet van de route of de trein zelf.</summary>
/// <summary>Wat er moet gebeuren als een te lange trein (langer dan MaxTreinlengte) dit
/// blok berijdt - vastgelegd per blok, zoals de gebruiker aangaf: dit is een eigenschap
/// van het BLOK, niet van de route of de trein zelf. [Flags]: onafhankelijk aan/uit te
/// vinken, niet een keuze uit één - je kunt bijvoorbeeld zowel "vorig blok langer bezet"
/// als "wisselstraat langer bezet" tegelijk aanvinken.</summary>
[Flags]
public enum TeLangeTreinActie
{
    /// <summary>Geen speciale actie nodig (bijv. omdat dit blok geen bekende lengte-beperking heeft).</summary>
    GeenActie = 0,
    /// <summary>De trein mag hier simpelweg niet stoppen als hij te lang is.</summary>
    MagNietStoppen = 1,
    /// <summary>Het vorige blok moet langer bezet blijven (de staart van de trein steekt er nog in).</summary>
    VorigBlokLangerBezetHouden = 2,
    /// <summary>De wisselstraat waarmee dit blok bereikt wordt, moet langer bezet/vastgezet blijven.</summary>
    WisselstraatLangerBezetHouden = 4
}

/// <summary>
/// Laag 1: een blok is puur het logische concept - een stuk baan waar
/// (normaal gesproken) maar 1 trein tegelijk mag zijn. Een blok kent
/// hier GEEN tekening, sein of positie - dat is allemaal laag 2.
/// </summary>
public class Blok
{
    /// <summary>Uniek volgnummer, automatisch toegekend bij aanmaken, door de gebruiker vrij te wijzigen.</summary>
    public int Nummer { get; set; }

    /// <summary>Vrije omschrijving, bijvoorbeeld "station perron 3".</summary>
    public string Omschrijving { get; set; } = "";

    public BlokType Type { get; set; } = BlokType.Normaal;

    /// <summary>
    /// Positie in het "onderhouden blokken"-schema (laag 1 eigen overzicht,
    /// los van waar het blok in het baanontwerp getekend staat).
    /// </summary>
    public double SchemaX { get; set; }
    public double SchemaY { get; set; }

    /// <summary>
    /// Bezetmeldpunten van dit blok. Minimaal 2 (voorblok + stopsectie) zodra
    /// het blok automatisch bereden moet kunnen worden - mag leeg zijn zolang
    /// het blok alleen nog maar logisch bestaat.
    /// </summary>
    public List<Bezetmeldpunt> Bezetmeldpunten { get; set; } = new();

    /// <summary>Bloklengte in cm - 0 betekent onbeperkt/niet ingesteld. Uit Koploper's
    /// Stamgegevens: "Bloklengte van blok", getoetst tegen de Lengte van de rijdende Trein.</summary>
    public double MaxTreinlengte { get; set; }

    /// <summary>Wat er moet gebeuren als een trein langer is dan MaxTreinlengte - alleen
    /// relevant als MaxTreinlengte > 0.</summary>
    public TeLangeTreinActie TeLangeTreinActie { get; set; } = TeLangeTreinActie.GeenActie;

    /// <summary>Maximumsnelheid (km/u) op dit traject/blok - 0 betekent geen eigen limiet
    /// (dan geldt gewoon de snelheid van het treintype). Koploper houdt altijd de LAAGSTE
    /// van (a) de max. snelheid van de loc/treintype en (b) de max. snelheid van het
    /// traject aan: "Indien een locomotief niet sneller kan dan 80km/u, zal deze op een
    /// traject waar een maximum snelheid is vastgelegd van 120km/u niet harder gaan dan
    /// 80km/u" - en omgekeerd net zo goed.</summary>
    public double MaxSnelheid { get; set; }

    /// <summary>Koploper's "Aanvulling Blokgegevens -> Doorrijden cm/sec" per blok: een
    /// eigen, dit-blok-specifieke uitroltijd (zie SimulatieInstellingen.
    /// StandaardUitrolSeconden voor de database-brede standaard). -1 (de standaard hier)
    /// betekent: gebruik de database-brede standaardwaarde. 0 is een geldige, EXPLICIETE
    /// keuze (dit blok heeft GEEN uitroltijd nodig, bijv. een kort rangeerspoor).</summary>
    public double UitrolSecondenOverride { get; set; } = -1;

    /// <summary>Tijdelijk buiten gebruik gezet door de gebruiker zelf (bijv. rails aan het
    /// schoonmaken/repareren) - GEEN storing (dat is Foutmelding, via ZetFoutmelding), maar
    /// een bewuste, eigen keuze: geen enkele automatische rit of pathfinding stuurt een
    /// trein hier nog naartoe (`BlokBeheerder.VolgendeBlokken` sluit een vergrendeld blok
    /// uit als mogelijk vervolgblok), maar het blok telt niet mee in de onderhoudslog/
    /// spookmelding-logica. Handmatig een loc hierop plaatsen blijft wel mogelijk - dat is
    /// een bewuste keuze van de gebruiker zelf, geen automatisme.</summary>
    public bool Vergrendeld { get; set; }

    /// <summary>Optionele, vrije toelichting bij Vergrendeld (bijv. "rails wordt gereinigd").</summary>
    public string? VergrendelReden { get; set; }

    /// <summary>Werkelijke lengte van dit bloktraject in cm - optioneel (0 = onbekend/niet
    /// meegeteld), los van MaxTreinlengte (dat is een CAPACITEITSGRENS voor stoppen, dit is
    /// de daadwerkelijke railafstand). Gebruikt voor de "afgelegde afstand"-teller per loc
    /// (Trein.TotaleAfgelegdeAfstandCm) - net als iTrain's "travelled time and distance
    /// indicators per loc".</summary>
    public double LengteCm { get; set; }

    /// <summary>Door de software zelf GEMETEN reistijden naar dit blok - per aankomst-
    /// richting én per loc, gebaseerd op ECHTE bezetmelder-tijdmetingen (zie
    /// TreinrouteWindow.Bezetmelding_VanHardware). Zie Model.GeleerdeReistijd voor de
    /// volledige achtergrond (vervangt de vroegere, enkelvoudige GeleerdeReistijdSeconden -
    /// één gedeeld gemiddelde voor alle locs/richtingen samen bleek te grof: een
    /// goederenloc en een hogesnelheidstrein doen over hetzelfde traject een heel andere
    /// tijd). NIET op de gesimuleerde bloklengte/snelheid-formule (LogSnelheidsopbouw e.d.)
    /// gebaseerd - die formule blijft alleen bestaan voor puur simulatiegebruik zonder
    /// hardware. Zodra er een echte hardware-interface actief is, gebruikt de rijsimulatie
    /// in plaats daarvan gewoon "hoe lang duurde het de vorige keer(en) echt, voor DEZE
    /// loc, vanuit DEZE richting" (een voortschrijdend gemiddelde - zie BlokBeheerder.
    /// RegistreerGeleerdeReistijd) als verwachte/maximale wachttijd, in plaats van te
    /// blijven schatten.</summary>
    public List<GeleerdeReistijd> GeleerdeReistijden { get; set; } = new();

    /// <summary>Gebruikersverzoek: "ik snap niet waarom de loc steeds bij het opstarten de
    /// verkeerde kant op rijdt" - bij een VERSE start (geen vorig blok bekend) heeft de
    /// software geen enkele manier om de fysiek juiste rijrichting te weten; ze gebruikt dan
    /// gewoon de standaardwaarde (vooruit), wat voor sommige startblokken structureel
    /// verkeerd kan zijn. Bewust GEEN automatische detectie op basis van meldervolgorde-
    /// timing (dat zou weer een aanname zijn, precies wat we overal proberen te vermijden) -
    /// in plaats daarvan onthoudt de software vanaf nu simpelweg welke richting KLOPTE, de
    /// eerste keer dat de gebruiker dat zelf via de keer-knop corrigeert bij een verse start
    /// vanaf dit blok (zie TreinrouteWindow.KeerTreinIndienActief) - en past die daarna
    /// automatisch toe bij elke volgende verse start vanaf hetzelfde blok. Null = nog niet
    /// geleerd, gebruik de standaardwaarde.</summary>
    public bool? GeleerdeStartrichtingVooruit { get; set; }

    /// <summary>Koploper's "Bezetmeldingen"-tabblad: per aankomstrichting ("Uit blok") een
    /// eigen, geordende bezetmelder-sequentie - zie Model.RichtingsBezetmelding. Heeft
    /// voorrang boven de generieke Bezetmeldpunten hierboven zodra er voor de daadwerkelijke
    /// aankomstrichting een entry bestaat; Bezetmeldpunten blijft de TERUGVAL voor
    /// richtingen zonder eigen configuratie (dus bestaande projecten blijven gewoon
    /// werken zonder dat dit ooit ingevuld hoeft te worden).</summary>
    public List<RichtingsBezetmelding> RichtingsBezetmeldingen { get; set; } = new();

    public override string ToString() => $"Blok {Nummer}" + (string.IsNullOrWhiteSpace(Omschrijving) ? "" : $" ({Omschrijving})");
}

