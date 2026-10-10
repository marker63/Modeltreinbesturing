namespace Modeltreinbesturing.Model;

/// <summary>
/// Een individuele trein - één specifiek exemplaar, met zijn eigen lengte. Een Treintype
/// is een categorie (bijv. "Intercity") en kan meerdere Treinen bevatten (bijv. "IC 123",
/// "IC 456"), elk met een eigen fysieke lengte. Treintype bepaalt de snelheid, Trein
/// bepaalt de lengte.
/// </summary>
public class Trein
{
    public string Omschrijving { get; set; } = "";
    public Treintype? Treintype { get; set; }

    /// <summary>Het DCC-decoderadres van deze loc. Koploper toont in de blokjes op het
    /// scherm bewust GEEN treinnamen/-nummers, alleen dit locdecoder-nummer - dat volgen
    /// we hier ook zo (zie MainWindow's loc-markering).</summary>
    public int DecoderAdres { get; set; }

    /// <summary>Aantal snelheidsstappen dat de decoder ondersteunt (14/28/128 zijn de
    /// gangbare waarden) - een apart veld in Koploper's "Onderhouden locomotieven",
    /// los van de simpelere Min/Gemiddelde/MaxSnelheid op het Treintype. Puur
    /// informatief/documentatie hier (onze simulator rekent in km/u, niet in decoderstappen).</summary>
    public int DecoderStappen { get; set; } = 28;

    /// <summary>Sommige decoders zijn (per ongeluk, of bewust bij het inbouwen) met een
    /// omgekeerde rijrichting geprogrammeerd (CV29, bit 0) t.o.v. wat de software als
    /// "vooruit" beschouwt - de loc rijdt dan fysiek de verkeerde kant op terwijl de
    /// software netjes "vooruit" stuurt, wat de bezetmelder-volgorde in de war stuurt (zie
    /// TreinrouteWindow.VindTreinDieMogelijkTerugrijdt/de rijrichting-waarschuwing).
    /// Herprogrammeren van de decoder zelf is de eigenlijke oplossing, maar dat is niet
    /// altijd meteen mogelijk (geen programmeerstation bij de hand, of je wilt de CV's van
    /// de fabrikant niet aanraken) - deze schakelaar compenseert het gewoon in software:
    /// elk vooruit/achteruit-commando dat naar de ECHTE hardware gaat wordt voor deze loc
    /// omgedraaid vlak vóór verzending. Heeft GEEN effect op de simulatie zelf (het
    /// blokschema/de rijrichting-logica blijft gewoon uitgaan van de "normale" richting) -
    /// puur een correctie op het allerlaatste, uitgaande hardware-commando.</summary>
    public bool OmgekeerdeRijrichting { get; set; }

    /// <summary>BUG #46: de laatst bekende rijrichting van deze loc (in het logische
    /// blokkenframe, dus vóór de OmgekeerdeRijrichting-correctie) en het blok waar hij toen
    /// stond. Wordt bij elke blokovergang/keercorrectie bijgewerkt en meegesaved in het
    /// project/de backup, zodat een nieuwe rit vanaf dat blok de richting van de loc zelf
    /// gebruikt i.p.v. één waarde per blok. Alleen geldig zolang de loc nog in dat blok staat.</summary>
    public bool? LaatsteRichtingVooruit { get; set; }
    public int? LaatsteRichtingBlokNummer { get; set; }

    /// <summary>Pad naar een foto van de fysieke loc, optioneel - handig om locs uit elkaar
    /// te houden in het overzicht als je er veel hebt. Net als GeluidsBestand hieronder een
    /// PAD, geen ingebedde afbeelding, om het projectbestand compact te houden.</summary>
    public string? FotoPad { get; set; }

    /// <summary>"Rangeer"-modus (zie Koploper's Rijwindow/tabblad Algemeen): schakelt de
    /// massasimulatie (optrekken/afremmen) tijdelijk uit voor deze trein, zodat 'm
    /// direct op volle snelheid/direct stilstaand reageert - handig voor precies
    /// manoeuvreren. Blijft aan/uit tussen ritten (niet per-rit gereset).</summary>
    public bool Rangeren { get; set; }

    /// <summary>Dubbeltractie (Koploper 9.2+): deze trein (als "baas") is gekoppeld aan een
    /// tweede loc (de "knecht"), die vanaf nu automatisch precies hetzelfde blok krijgt als
    /// deze trein - alsof ze fysiek aan elkaar gekoppeld zijn en samen bewegen. Alleen de
    /// baas rijdt daadwerkelijk een route (RijdendeTrein); de knecht volgt puur passief mee
    /// via BlokBeheerder.PlaatsLoc/VerwijderLoc, precies zoals Koploper's eigen "zoekt zelf
    /// uit welke lok de baas is en welke de knecht" - alleen hier expliciet door de
    /// gebruiker gekoppeld i.p.v. automatisch herkend op basis van geijkte snelheden.</summary>
    public Trein? GekoppeldeKnecht { get; set; }

    /// <summary>Koploper's "geijkte snelheid" (Onderhouden locomotieven -> tabblad
    /// Stappen/IJking): de snelheid van een loc wordt in werkelijkheid geregeld via
    /// DECODERSTAPPEN (0 t/m DecoderStappen hierboven), niet via een simpel Max/Gemiddeld/
    /// Min-drietal - "de instelling van de locomotief bepaalt uiteindelijk welke
    /// instellingen gebruikt worden, dit geldt voor de locomotief, niet voor het
    /// treintype!". Het echte Koploper meet dit automatisch: de loc rijdt tussen twee
    /// bezetmelders op een bekende afstand (heen-en-weer of een rondje), de gemeten tijd
    /// wordt via de schaal (bijv. 1:87 voor H0, 1:160 voor N) omgerekend naar een
    /// schaalsnelheid per stap, van de hoogste stap naar beneden totdat de loc "kennelijk
    /// stilstaat" (extreem lange tijd tussen de melders). Wij hebben geen fysieke
    /// terugmeldhardware om dat automatisch te doen, dus GEEN meetwizard - in plaats
    /// daarvan vul je deze tabel zelf in (voor simulatiedoeleinden), eventueel geholpen
    /// door de "genereer een lineaire reeks"-knop in het scherm als startpunt.
    /// GebruikGeijkteSnelheid=false (de standaard) betekent: gebruik gewoon de snelheid van
    /// het treintype, zoals voorheen - de tabel wordt dan genegeerd.</summary>
    public bool GebruikGeijkteSnelheid { get; set; }
    public List<DecoderStapSnelheid> Stappentabel { get; set; } = new();

    /// <summary>Cumulatieve rij-statistieken per loc - net als iTrain's "travelled time and
    /// distance indicators per loc": handig om bijvoorbeeld onderhoud aan de fysieke loc te
    /// plannen op basis van daadwerkelijk gereden tijd/afstand, net zoals bij een echte
    /// trein. TotaleAfgelegdeAfstandCm telt alleen mee voor blokken waar Blok.LengteCm is
    /// ingevuld (0 = niet meegeteld voor dat blok). Beide zijn puur optellend; alleen de
    /// gebruiker kan ze terugzetten (via "Teller resetten" bij Treinen beheren).</summary>
    public double TotaleRijtijdSeconden { get; set; }
    public double TotaleAfgelegdeAfstandCm { get; set; }

    /// <summary>Onderhoudsdrempel in uren - optioneel (0 = geen drempel ingesteld). Zodra
    /// TotaleRijtijdSeconden dit overschrijdt, signaleert het Dashboard dat deze loc aan
    /// onderhoud toe is (wielen smeren, contacten schoonmaken, etc.) - net als een
    /// kilometrage-gebaseerde onderhoudsbeurt-herinnering bij een echte auto. Sluit aan op
    /// de al bestaande incidenten/wijzigingen-log: daar kun je het uitgevoerde onderhoud
    /// dan als Wijziging vastleggen.</summary>
    public double OnderhoudDrempelUren { get; set; }

    /// <summary>Alleen relevant bij AUTOMATISCH rijden - deze loc mag NOOIT gedwongen
    /// worden om te keren (op een kopspoor of via een Keer-relatie), net als in de echte
    /// spoorwereld: een loc-getrokken goederen-/passagierstrein mag niet zomaar achteruit
    /// over de hoofdbaan (de machinist moet voorop zitten). HARDE regel, geen terugval -
    /// bij het kiezen van een vervolgblok wordt vooruitgekeken (zie TreinrouteWindow.
    /// KanVeiligBlijvenRijden): elke kandidaat die uiteindelijk ALTIJD in een keer-situatie
    /// eindigt, wordt ONVOORWAARDELIJK uitgesloten. Blijft er dan niets bruikbaars over,
    /// dan wacht de trein gewoon (net als bij "alles bezet") totdat er weer een geldige
    /// optie vrijkomt - hij keert dan NOOIT, ook niet als laatste redmiddel. Alleen een
    /// trein ZONDER dit kenmerk mag keren - bijv. voor rangeerbewegingen.</summary>
    public bool MagNietKeren { get; set; }

    /// <summary>Draaischijf-afstand (cm): hoever de locomotief na de EERSTE detectie op de
    /// brug van een draaischijf moet doorrijden om goed te stoppen - een veld uit
    /// Koploper's eigen "Onderhouden locomotieven". 0 = geen draaischijf-gebruik bij
    /// automatisch rijden. We hebben (nog) geen apart draaischijf-symbool/-simulatie, dit
    /// is puur het ontbrekende gegevensveld, ter volledigheid van het locomotievenscherm.</summary>
    public double DraaischijfAfstand { get; set; }

    /// <summary>Eigen massasimulatie van DEZE loc (seconden), los van het treintype - 0
    /// betekent "geen eigen instelling, gebruik gewoon het treintype". "Koploper zal
    /// altijd de traagste massa simulatie kiezen. Indien de massa simulatie voor het
    /// treintype trager is dan die van de locomotief, zal Koploper de traagste gebruiken."
    /// Zie BouwGebeurtenissen in TreinrouteWindow voor waar dit wordt toegepast.</summary>
    public double AccelaratieVan0Naar50Seconden { get; set; }
    public double RemVan50Naar0Seconden { get; set; }

    /// <summary>Lengte in cm - 0 betekent niet ingesteld/geen lengtecontrole voor deze trein.</summary>
    public double Lengte { get; set; }

    /// <summary>Is deze trein (ook) een treinstel - een kenmerk BOVENOP het gewone treintype,
    /// gebruikt door Treinroute.Geldigheid = Treinstel om te bepalen voor welke treinen een
    /// variabele route geldt.</summary>
    public bool IsTreinstel { get; set; }

    /// <summary>Loc-functies (verlichting, hoorn, ...) die automatisch afgaan op bepaalde
    /// momenten tijdens het rijden - zoals Koploper's "Onderhouden locomotieven" - tabblad
    /// "Functies uitgebreid". Puur gesimuleerd/gelogd (geen echte hardware), maar wel op
    /// dezelfde momenten als de echte simulator-levenscyclus.</summary>
    public List<LocFunctie> Functies { get; set; } = new();

    public override string ToString() => Treintype != null ? $"{Omschrijving} ({Treintype.Omschrijving})" : Omschrijving;

    /// <summary>Leidt Max/Gemiddelde/Minimumsnelheid (km/u) af uit de stappentabel, voor
    /// gebruik door de (tijd-gebaseerde) simulator - die rekent nu eenmaal in deze drie
    /// kengetallen, niet in losse decoderstappen. Max = hoogste stap in de tabel, Min =
    /// laagste stap met een snelheid boven 0 (een stap op 0 km/u betekent "hier staat de
    /// loc stil", net als in het echte Koploper-meetproces), Gemiddeld = de middelste stap
    /// van de tabel, als redelijke representatieve "kruissnelheid". Lege tabel (nog niets
    /// ingevuld) geeft (0,0,0) terug.</summary>
    public (double Max, double Gemiddeld, double Minimum) AfgeleideSnelheden()
    {
        var stappenMetSnelheid = Stappentabel.Where(s => s.KmPerUur > 0).OrderBy(s => s.Stap).ToList();
        if (stappenMetSnelheid.Count == 0) return (0, 0, 0);
        double max = stappenMetSnelheid.Max(s => s.KmPerUur);
        double min = stappenMetSnelheid.Min(s => s.KmPerUur);
        double gemiddeld = stappenMetSnelheid[stappenMetSnelheid.Count / 2].KmPerUur;
        return (max, gemiddeld, min);
    }
}

/// <summary>Op welk moment in de rit-levenscyclus een loc-functie automatisch afgaat -
/// dezelfde momenten als in Koploper's eigen "Functies uitgebreid"-tabblad.</summary>
public enum LocGebeurtenis
{
    VerlaatBlok,
    AankomstEnRijdtDoor,
    AankomstEnStopt,
    GestoptBijVerplichteStop,
    GaatWeerRijden
}

public class LocFunctie
{
    public string Naam { get; set; } = "";
    public LocGebeurtenis Gebeurtenis { get; set; }

    /// <summary>DCC-functienummer (F0 t/m F28) - het daadwerkelijke commando dat naar de
    /// hardware/decoder gestuurd wordt (zie HardwareBeheerder.StuurFunctieCommando). -1
    /// (de standaard) betekent: geen hardware-functienummer gekoppeld, puur het geluid
    /// hieronder afspelen zoals voorheen (bijv. voor een functie die alleen een lokaal
    /// PC-geluidje moet geven, zonder ook echt de decoder aan te sturen).</summary>
    public int FunctieNummer { get; set; } = -1;

    /// <summary>Of dit functienummer bij deze gebeurtenis AAN of UIT gezet moet worden -
    /// bijv. één LocFunctie-regel "bij VerlaatBlok: F0 aan" en een aparte tweede regel
    /// "bij AankomstEnStopt: F0 uit" om het licht/geluid weer netjes te laten stoppen.
    /// Alleen relevant als FunctieNummer >= 0 is.</summary>
    public bool Aan { get; set; } = true;

    /// <summary>Optioneel pad naar een .wav-bestand dat afgespeeld wordt zodra deze functie
    /// afgaat - zoals het echte Koploper: "Afspelen van een geluidsbestand (*.wav) op de
    /// computer" is één van de manieren waarop Koploper geluid kan aansturen (naast een
    /// geluidsdecoder-functie, die wij WEL kunnen aansturen zolang FunctieNummer is
    /// ingevuld - zie hierboven). Leeg = geen lokaal geluid.</summary>
    public string? GeluidsBestand { get; set; }

    public override string ToString() =>
        $"{Naam} — bij: {Gebeurtenis}" + (FunctieNummer >= 0 ? $" (F{FunctieNummer} {(Aan ? "aan" : "uit")})" : "") + (string.IsNullOrEmpty(GeluidsBestand) ? "" : $" 🔊");
}

/// <summary>Eén regel in Trein.Stappentabel: bij deze digitale (decoder)stap hoort deze
/// schaalsnelheid (km/u) - precies de kolommen uit Koploper's eigen "Overzicht digitale
/// stap (schaal) kilometer per uur"-tabel.</summary>
public class DecoderStapSnelheid
{
    public int Stap { get; set; }
    public double KmPerUur { get; set; }
}

