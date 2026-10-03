using System.Text.Json.Serialization;
using System.Windows;

namespace Modeltreinbesturing.Model;

/// <summary>Symbolen die (optioneel) aan een blok gekoppeld kunnen worden, zodat ze
/// automatisch meekleuren met de bezetting van dat blok - net als in Koploper.</summary>
public interface IGekoppeldAanBlok
{
    Blok? GekoppeldBlok { get; set; }
}

/// <summary>
/// Laag 2: basis voor alles wat in het baanontwerp getekend kan worden.
/// Puur visueel - kent geen rij-logica, dat blijft bij Blok (laag 1).
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "soort")]
[JsonDerivedType(typeof(Lijn), typeDiscriminator: "lijn")]
[JsonDerivedType(typeof(Wissel), typeDiscriminator: "wissel")]
[JsonDerivedType(typeof(Sein), typeDiscriminator: "sein")]
[JsonDerivedType(typeof(Stootblok), typeDiscriminator: "stootblok")]
[JsonDerivedType(typeof(Perron), typeDiscriminator: "perron")]
[JsonDerivedType(typeof(Tekst), typeDiscriminator: "tekst")]
[JsonDerivedType(typeof(Schakelaar), typeDiscriminator: "schakelaar")]
[JsonDerivedType(typeof(Pijl), typeDiscriminator: "pijl")]
[JsonDerivedType(typeof(Ontkoppelrail), typeDiscriminator: "ontkoppelrail")]
[JsonDerivedType(typeof(Bezetmelder), typeDiscriminator: "bezetmelder")]
[JsonDerivedType(typeof(Driewegwissel), typeDiscriminator: "driewegwissel")]
[JsonDerivedType(typeof(Kruiswissel), typeDiscriminator: "kruiswissel")]
public abstract class BaanSymbool
{
    public double X { get; set; }
    public double Y { get; set; }
    public int Tabblad { get; set; } = 1;
}

/// <summary>Een lijn stelt een stuk rail voor. Kan optioneel gekoppeld zijn aan een blok
/// zodat de lijn meekleurt als het blok bezet is - de koppeling is puur visueel/optioneel,
/// en blijft altijd mogelijk ongeacht of dat blok op dit moment bezet is.</summary>
public class Lijn : BaanSymbool, IGekoppeldAanBlok
{
    public List<Point> Punten { get; set; } = new();
    public Blok? GekoppeldBlok { get; set; }

    /// <summary>Per punt in Punten (zelfde index/lengte): het blok waar dat punt aan
    /// verankerd is, of null. Hoogstens één van PuntBlokAnkers/PuntWisselAnkers/PuntLijnAnkers
    /// is niet-null per index. Een ankerpunt verschuift automatisch mee als het blok/de
    /// wissel/de andere lijn versleept wordt, zodat de lijn verbonden blijft - net als
    /// aansluitpunten in het echte baanontwerp.</summary>
    public List<Blok?> PuntBlokAnkers { get; set; } = new();

    /// <summary>Per punt in Punten (zelfde index/lengte): de wissel waar dat punt aan
    /// verankerd is, of null. Zie ook PuntBlokAnkers.</summary>
    public List<Wissel?> PuntWisselAnkers { get; set; } = new();

    /// <summary>Per punt in Punten (zelfde index/lengte): een ANDERE lijn waar dat punt aan
    /// verankerd is (lijnen mogen ook rechtstreeks aan elkaar vastzitten, niet alleen aan
    /// blokken/wissels), of null. Zie ook PuntBlokAnkers.</summary>
    public List<Lijn?> PuntLijnAnkers { get; set; } = new();

    /// <summary>Bij welk punt-index van PuntLijnAnkers[i] dit punt vastzit (alleen relevant
    /// als PuntLijnAnkers[i] niet null is).</summary>
    public List<int> PuntLijnAnkerIndex { get; set; } = new();

    /// <summary>Per punt in Punten (zelfde index/lengte): de kruiswissel waar dat punt aan
    /// verankerd is, of null. Een kruiswissel heeft (in tegenstelling tot een gewone
    /// Wissel, met genoemde instroom/rechtdoor/afbuigend-poten) 4 gelijkwaardige,
    /// ongename eindpunten - PuntKruiswisselAnkerIndex hieronder geeft aan welk van de 4
    /// dat precies is. Zie ook PuntBlokAnkers.</summary>
    public List<Kruiswissel?> PuntKruiswisselAnkers { get; set; } = new();

    /// <summary>Welk van de 4 eindpunten van PuntKruiswisselAnkers[i] (index 0-3, zie
    /// BaanontwerpWindow.KruiswisselAnkerpunten) dit punt vastzit - alleen relevant als
    /// PuntKruiswisselAnkers[i] niet null is.</summary>
    public List<int> PuntKruiswisselAnkerIndex { get; set; } = new();

    /// <summary>Puur informatief volgnummer, net als in het echte Koploper: "De nummering
    /// van de lijntjes heeft niets te maken met de bloknummers, dit is puur informatief om
    /// de lijntjes van elkaar te kunnen onderscheiden." Automatisch toegekend bij het
    /// tekenen (zie StartNieuweLijn in BaanontwerpWindow), heeft geen enkele functionele
    /// betekenis.</summary>
    public int Volgnummer { get; set; }

    /// <summary>Voegt een punt toe en houdt alle ankerlijsten gegarandeerd gelijk in lengte aan Punten.</summary>
    public void VoegPuntToe(Point punt, Blok? blokAnker, Wissel? wisselAnker, Lijn? lijnAnker = null, int lijnAnkerIndex = -1, Kruiswissel? kruiswisselAnker = null, int kruiswisselAnkerIndex = -1)
    {
        Punten.Add(punt);
        PuntBlokAnkers.Add(blokAnker);
        PuntWisselAnkers.Add(wisselAnker);
        PuntLijnAnkers.Add(lijnAnker);
        PuntLijnAnkerIndex.Add(lijnAnkerIndex);
        PuntKruiswisselAnkers.Add(kruiswisselAnker);
        PuntKruiswisselAnkerIndex.Add(kruiswisselAnkerIndex);
    }

    /// <summary>Trekt alle ankerlijsten weer gelijk in lengte aan Punten - nodig na het
    /// LADEN van een oudere projectopslag: een ouder opgeslagen bestand kent NIEUWERE
    /// ankerlijsten (bijv. PuntKruiswisselAnkers, pas later toegevoegd dan de andere) nog
    /// niet, dus die blijven na het inlezen leeg (0 elementen) terwijl Punten al wél
    /// gevuld is - een directe indexering op zo'n lijst (bijv. tijdens het verslepen van
    /// een lijnpunt) geeft dan een ArgumentOutOfRangeException. Vult ontbrekende posities
    /// aan met null/-1 (= "nergens aan verankerd"), precies zoals een nieuw punt dat ook
    /// zou krijgen.</summary>
    public void HerstelAnkerLijstenLengte()
    {
        while (PuntBlokAnkers.Count < Punten.Count) PuntBlokAnkers.Add(null);
        while (PuntWisselAnkers.Count < Punten.Count) PuntWisselAnkers.Add(null);
        while (PuntLijnAnkers.Count < Punten.Count) PuntLijnAnkers.Add(null);
        while (PuntLijnAnkerIndex.Count < Punten.Count) PuntLijnAnkerIndex.Add(-1);
        while (PuntKruiswisselAnkers.Count < Punten.Count) PuntKruiswisselAnkers.Add(null);
        while (PuntKruiswisselAnkerIndex.Count < Punten.Count) PuntKruiswisselAnkerIndex.Add(-1);
    }
}

public enum WisselStand { Rechtdoor, Afbuigend }

/// <summary>
/// Hoe de wissel getekend wordt: HoofdrichtingGraden is de rijrichting van de rechtdoor-poot
/// (0=oost/rechts, 90=zuid/beneden, enz., in stappen van 45° - dus 8 richtingen, net als de
/// echte wissel-iconen in Koploper's toolvenster). AfbuigingLinksom bepaalt of de afbuigende
/// poot 45° naar links of naar rechts van die hoofdrichting wijst.
/// </summary>
public class Wissel : BaanSymbool
{
    public int Adres { get; set; }
    public WisselStand Stand { get; set; } = WisselStand.Rechtdoor;
    public double HoofdrichtingGraden { get; set; }
    public bool AfbuigingLinksom { get; set; }

    /// <summary>"Altijd initialiseren" (Koploper's eigen wissel-eigenschap): stuurt bij het
    /// verbinden met hardware de huidige stand altijd opnieuw naar het adres, ook als de
    /// software denkt dat de stand al goed staat - zorgt dat software en fysieke wissel
    /// altijd synchroon lopen na opstarten, ook als de wissel handmatig is versteld terwijl
    /// er geen verbinding was.</summary>
    public bool AltijdInitialiseren { get; set; }

    /// <summary>Handmatig als defect gemarkeerd (bijv. een vastgelopen/kapotte fysieke
    /// wissel) - een route die een wisselstraat met deze wissel nodig heeft, weigert te
    /// starten, en automatisch/hardware-omzetten slaat deze wissel bewust over.</summary>
    public bool IsDefect { get; set; }

    /// <summary>"Overloopwissel" (Koploper's eigen "Vastleggen overloopwissels"): twee
    /// gewone wissels die aan elkaar gekoppeld zijn, zodat ze ALTIJD samen omzetten - beide
    /// rechtdoor of beide afbuigend - voor het wisselen tussen twee parallelle sporen. De
    /// koppeling werkt symmetrisch: wissel A koppelt aan B betekent ook B koppelt aan A.</summary>
    public Wissel? GekoppeldeOverloopwissel { get; set; }

    /// <summary>GEVONDEN, FYSIEK GAT (gebruikerswaarneming: "wissel 5 staat op het scherm
    /// afbuigend wat niet juist is, maar fysiek staat hij wel terecht rechtdoor" - dit hield
    /// stand ook na de ActueleWissel-fix voor het losstaand-duplicaat-object-probleem, en
    /// trad al direct bij de kale opstart-initialisatie op, dus VOOR er ooit een
    /// wisselstraat/duplicaat-object bij betrokken kan zijn): exact hetzelfde, al langer
    /// bekende verschijnsel als bij Kruiswissel.Adres2OmgekeerdePolariteit hierboven, maar nu
    /// bij een GEWONE wissel met maar één motor - de bekabeling van precies DEZE ene
    /// wisselmotor blijkt fysiek met omgekeerde polariteit aangesloten, waardoor het
    /// commando dat de software als "afbuigend" verstuurt, deze ene motor juist rechtdoor
    /// laat lopen (en andersom). Dat is geen software-standfout (de software, het scherm en
    /// het verstuurde commando zijn onderling allemaal consistent-fout, exact synchroon) maar
    /// een fysiek gegeven van deze motor - vandaar dat eerdere fixes (die allemaal over
    /// consistentie tussen verschillende software-kopieën van dezelfde wissel gingen) dit
    /// niet oplosten. Alleen relevant voor het commando dat naar de hardware gaat: het
    /// scherm blijft gewoon Stand tonen (dat IS immers de gewenste/bedoelde stand), enkel het
    /// fysieke commando wordt omgedraaid - net als bij een Kruiswissel met deze vlag.</summary>
    public bool OmgekeerdePolariteit { get; set; }

    /// <summary>GEBRUIKERSVERZOEK ("wissel 5 staat fysiek op rechtdoor maar op het scherm
    /// toont hij afbuigend, graag de weergave op het scherm omkeren"): dit speelde al eerder
    /// bij dezelfde wissel (zie OmgekeerdePolariteit hierboven) - die keer bleek de ECHTE
    /// oorzaak een fysiek omgekeerd bedrade wisselmotor, gecorrigeerd via het HARDWARE-
    /// commando, bewust zonder het scherm aan te passen (Stand IS immers de bedoelde/gewenste
    /// stand). Deze keer, na die eerdere fix, blijft er toch weer een mismatch tussen scherm
    /// en fysieke stand over - de gebruiker vraagt nu expliciet om puur de WEERGAVE op het
    /// scherm om te draaien voor deze ene wissel, los van Stand zelf (die blijft de bron van
    /// waarheid voor wisselstraten/hardware-commando's) en los van OmgekeerdePolariteit (die
    /// blijft uitsluitend het hardware-commando beïnvloeden). Alleen BaanontwerpWindow's
    /// TekenSymbool (het getekende rechtdoor/afbuigend-symbool) kijkt hiernaar.</summary>
    public bool OmgekeerdeWeergave { get; set; }

    public override string ToString() => $"Wissel {Adres}";
}

public enum DriewegwisselStand { Links, Rechtdoor, Rechts }

/// <summary>Een driewegwissel: één wissel-hart met DRIE mogelijke standen i.p.v. twee -
/// links, rechtdoor, rechts. Functioneel vergelijkbaar met een gewone Wissel, maar met een
/// eigen 3-standen-enum. Bewust GEEN automatische lijn-ankering zoals een gewone Wissel
/// (dat zou de al complexe ankerlogica in Lijn nog verder uitbreiden) - wel vrij te
/// plaatsen, draaien en van stand te wisselen, en te koppelen aan hardware.</summary>
public class Driewegwissel : BaanSymbool
{
    public int Adres { get; set; }
    public DriewegwisselStand Stand { get; set; } = DriewegwisselStand.Rechtdoor;
    public double HoofdrichtingGraden { get; set; }
    public bool AltijdInitialiseren { get; set; }

    public override string ToString() => $"Driewegwissel {Adres}";
}

/// <summary>Een kruiswissel/kruising: twee sporen die elkaar kruisen op één punt, zonder
/// zelf een rijrichting te kiezen (passief) - een trein op elk van beide sporen rijdt
/// gewoon rechtdoor. Dit is de gangbare, eenvoudige "kruising"; een volledige Engelse
/// wissel (met wél schakelbare overstap-mogelijkheden tussen de twee sporen) is
/// functioneel uitgebreider en is hier bewust niet nagebouwd.</summary>
public enum KruiswisselStand { Rechtdoor, Overstap }

/// <summary>Een kruiswissel/kruising: twee sporen die elkaar kruisen op één punt.
/// Standaard PASSIEF (IsEngels=false): een trein op elk spoor rijdt gewoon rechtdoor,
/// zonder enige schakeling - de gewone "kruising". Met IsEngels=true wordt het een echte
/// Engelse wissel met een eigen schakelbare overstap tussen de twee sporen op het
/// kruispunt zelf.
///
/// GEBRUIKERSCORRECTIE, FUNDAMENTEEL (fysiek uitgelegd door de gebruiker aan de hand van
/// zijn echte Roco Engelse wissel, getekend als een "+"-vorm: linkerpoot naar blok 7,
/// rechterpoot naar blok 1, bovenpoot naar blok 4, onderpoot naar blok 3 - de linkerpoot+
/// bovenpoot zitten mechanisch aan motor 13 (Adres) gekoppeld, de rechterpoot+onderpoot aan
/// motor 10 (Adres2)): de eerdere aanname hierboven ("ALTIJD SAMEN aangestuurd, Rechtdoor=
/// beide rechtdoor, Overstap=beide afbuigend, een gemengde combinatie bestaat niet") is
/// AANTOONBAAR FOUT gebleken. Bevestigde, werkelijke standen per rijweg door dit kruiswissel:
///   blok 7 -> blok 3: Adres=Afbuigend, Adres2=Afbuigend
///   blok 4 -> blok 3: Adres=Rechtdoor, Adres2=Rechtdoor
///   blok 7 -> blok 1: Adres=Rechtdoor, Adres2=Afbuigend  (GEMENGDE combinatie - bestaat dus WEL)
/// (en later nog blok 4 -> blok 1, nog te bevestigen) - dezelfde standen gelden voor de
/// omgekeerde rijrichting. Een Engelse wissel/dubbele kruiswissel heeft dus geëcht VIER
/// onafhankelijke rijwegen (twee rechtdoor, twee diagonaal), en welke combinatie van de twee
/// motoren bij welke rijweg hoort is GEEN vaste (gelijk-aan-elkaar of altijd-tegengesteld)
/// regel - dat kan alleen per rijweg expliciet vastgelegd worden (zie
/// Model.KruiswisselInWisselstraat.StandAdres/StandAdres2). Adres en Adres2 zijn daarom nu
/// twee volledig onafhankelijke wisselmotoren, elk met hun eigen, expliciet benodigde stand
/// per rijweg - er bestaat geen zinvolle "stand van de hele kruiswissel" meer los van een
/// specifieke rijweg.</summary>
public class Kruiswissel : BaanSymbool
{
    public double HoofdrichtingGraden { get; set; }

    public bool IsEngels { get; set; }
    public int Adres { get; set; }

    /// <summary>Tweede, VOLLEDIG onafhankelijke wisselmotor - alleen relevant bij IsEngels.
    /// 0 = (nog) niet ingesteld. Zie de toelichting hierboven: deze motor krijgt NIET meer
    /// automatisch dezelfde (of tegengestelde) stand als Adres - elke rijweg door dit
    /// kruiswissel (KruiswisselInWisselstraat) legt de stand van Adres en Adres2 apart vast.</summary>
    public int Adres2 { get; set; }

    /// <summary>Laatst bekende/gecommandeerde stand van motor Adres - bijgewerkt zodra een
    /// rijweg dit kruiswissel daadwerkelijk zet (zie ZetKruiswisselstanden/
    /// ZetWisselsTussenBlokken). Gebruikt om bij het (opnieuw) verbinden dezelfde, laatst
    /// bekende stand opnieuw te versturen (zie MainWindow.VerbindMetOpgeslagenHardwareIndien
    /// Beschikbaar) - net als bij een gewone Wissel.Stand, maar dan per motor apart.</summary>
    public WisselStand StandAdres { get; set; } = WisselStand.Rechtdoor;

    /// <summary>Zelfde als StandAdres hierboven, maar dan voor motor Adres2 - VOLLEDIG
    /// onafhankelijk, zie de toelichting bij de klasse.</summary>
    public WisselStand StandAdres2 { get; set; } = WisselStand.Rechtdoor;

    /// <summary>Alleen nog gebruikt als eenvoudige weergave-indicatie in het baanontwerp
    /// (welk icoontje getekend wordt) en voor de simpele, symmetrische handmatige testknop
    /// (Shift+dubbelklik) - GEEN bron van waarheid meer voor het daadwerkelijk aansturen van
    /// de hardware, dat gebeurt nu uitsluitend via StandAdres/StandAdres2 hierboven. Zie de
    /// klasse-toelichting: een enkele "stand van de hele kruiswissel" kan de vier echte
    /// rijwegen niet meer correct representeren.</summary>
    public KruiswisselStand Stand { get; set; } = KruiswisselStand.Rechtdoor;

    /// <summary>Zelfde betekenis als bij een gewone Wissel - alleen relevant als IsEngels.
    /// Geldt voor BEIDE motoren (Adres en Adres2) samen.</summary>
    public bool AltijdInitialiseren { get; set; }

    /// <summary>GEVONDEN, ARCHITECTUREEL GAT (gebruikerswaarneming: loc bleef stilstaan
    /// PRECIES op de kruiswissel - handmatig rijden via blok3, blok4 EN blok7 se adres
    /// bewoog de loc geen van drieën): dit model ging er tot nu toe van uit dat ELK stukje
    /// spoor bij een Blok hoort, en dus via een Blok se eigen rijstroom-adres aangestuurd
    /// wordt. Bij deze kruiswissel-opstelling klopt dat niet - de kruiswissel-sectie zelf
    /// is een eigen, elektrisch geïsoleerd gebied, los van alle aangrenzende blokken, met
    /// een EIGEN rijstroom-adres nodig om een daar-staande loc te kunnen aansturen. 0 =
    /// (nog) niet ingesteld (dan wordt er, zoals voorheen, alleen naar de aangrenzende
    /// blokken gestuurd - onschadelijk voor een kruiswissel die WEL gewoon via een
    /// aangrenzend blok gevoed wordt).</summary>
    public int RijAdres { get; set; }

    /// <summary>VERVALLEN als sturingsmechanisme (zie de klasse-toelichting hierboven): dit
    /// veld probeerde het "motor 10 staat steeds verkeerd"-probleem op te lossen door aan te
    /// nemen dat beide motoren ALTIJD dezelfde (of een vast tegengestelde) stand horen te
    /// krijgen, en dat elke afwijking dus bekabelings-polariteit moest zijn. De gebruiker
    /// heeft (fysiek, per rijweg getest) aangetoond dat dit uitgangspunt zelf fout was: de
    /// twee motoren hebben gewoon een eigen, per rijweg verschillende, soms GEMENGDE
    /// combinatie nodig (bijv. blok7->blok1: Adres=Rechtdoor/Adres2=Afbuigend) - geen vaste
    /// polariteitsregel kan dat uitdrukken. StandAdres/StandAdres2 hierboven leggen nu voor
    /// elke rijweg de daadwerkelijk benodigde, absolute stand van elke motor apart vast, dus
    /// is er geen inversie meer nodig. Dit veld blijft alleen bestaan zodat oudere
    /// projectbestanden nog inlezen, maar wordt nergens meer gebruikt om te sturen.</summary>
    public bool Adres2OmgekeerdePolariteit { get; set; }

    public override string ToString() => IsEngels ? (Adres2 > 0 ? $"Engelse wissel {Adres}/{Adres2}" : $"Engelse wissel {Adres}") : "Kruiswissel";
}

public enum SeinType
{
    /// <summary>Het gangbare 3-standen lichtsein (rood/geel/groen) - het huidige, bestaande
    /// gedrag, blijft de standaard voor nieuw geplaatste seinen.</summary>
    Standaard3Standen,

    /// <summary>Het eenvoudigere 2-standen lichtsein (alleen rood/groen) - zoals het echte
    /// Koploper's documentatie het beschrijft: "Standaard heeft Koploper alleen 2-standen
    /// (rood/groen) seinen" als uitgangspunt, een 3-standen sein is er de uitzondering op.
    /// Toont nooit geel: waar een 3-standen sein geel zou tonen (blok-daarna-bezet), toont
    /// dit type gewoon door-groen.</summary>
    Standaard2Standen,

    /// <summary>Een dwergsein: een klein rangeersein, in het echt fysiek kleiner dan een
    /// hoofdsein en met een eigen aspect-paar - hier vereenvoudigd tot dezelfde 2-standen
    /// rood/groen-logica als Standaard2Standen, maar met een kleiner getekend symbool.</summary>
    Dwergsein,

    /// <summary>Een voorsein: kondigt het aspect van het EERSTVOLGENDE sein op de route
    /// vooraf aan (geen eigen stopgebod), zodat een machinist tijdig kan afremmen - net als
    /// in het echt. Zoekt het sein dat gekoppeld is aan het bewaakte blok van dit sein en
    /// toont diens aspect. Zonder zo'n gevonden sein: groen (niets te melden).</summary>
    Voorsein
}

public enum SeinStand { Onveilig, Veilig }

/// <summary>Een sein. Als het gekoppeld is aan een blok, wordt de stand automatisch
/// afgeleid van de bezetting van dat blok (rood zolang bezet) - precies zoals in Koploper.
/// Zonder koppeling blijft de handmatig gezette Stand gelden.</summary>
public class Sein : BaanSymbool, IGekoppeldAanBlok
{
    public int Adres { get; set; }
    public SeinStand Stand { get; set; } = SeinStand.Onveilig;
    public Blok? GekoppeldBlok { get; set; }

    /// <summary>Welk type sein dit is (2-standen/3-standen/dwergsein/voorsein) - bepaalt
    /// welke aspecten mogelijk zijn en hoe SeinLogica het aspect berekent. Standaard het
    /// gangbare 3-standen lichtsein, zoals voorheen (bestaand gedrag onveranderd voor wie
    /// dit veld niet expliciet wijzigt).</summary>
    public SeinType Type { get; set; } = SeinType.Standaard3Standen;

    /// <summary>Draaihoek in graden (stappen van 45°) - standaard 0 = rechtop (verticaal),
    /// net als voorheen. Vrij te positioneren/draaien zodat het sein visueel bij de
    /// richting van het spoor past, in plaats van altijd verticaal te staan.</summary>
    public double HoekGraden { get; set; }

    /// <summary>"Rangeersein" gekoppeld aan een WISSELSTRAAT i.p.v. (of naast) een blok -
    /// zoals in het echte Koploper: "Het sein toont groen bij een ingestelde rijweg en
    /// valt weer op rood bij de eerste bezetmelding na de wisselstraat." Vaak gebruikt
    /// vóór een wisselstraat, waar één sein voor meerdere vertreksporen tegelijk geldt.
    /// Als dit gezet is, heeft het voorrang op GekoppeldBlok in SeinLogica.</summary>
    public bool IsRangeersein { get; set; }
    public Wisselstraat? GekoppeldeWisselstraat { get; set; }
}

public class Stootblok : BaanSymbool, IGekoppeldAanBlok
{
    public Blok? GekoppeldBlok { get; set; }

    /// <summary>De richting van het spoor ter plekke - het stootblok zelf wordt loodrecht
    /// hierop getekend (dwars op de rijrichting), zoals een echte bufferstop.</summary>
    public Vector Richting { get; set; } = new Vector(1, 0);
}

/// <summary>Een bezetmelder als zichtbaar symbool ("kleine cirkeltjes in de lijnen", zoals
/// het echte Koploper ze in het baanoverzicht toont) - los van het abstracte
/// Bezetmeldpunt in Blok.Bezetmeldpunten (MeldernNummer+Rol), dat blijft de daadwerkelijke
/// simulatie-/koppelingslogica sturen. Dit symbool is puur de VISUELE plaatsing op de baan:
/// MeldernNummer (optioneel) is alleen om te kunnen zien welk abstract meldpunt hier
/// ongeveer hoort, geen aparte staat.</summary>
public class Bezetmelder : BaanSymbool, IGekoppeldAanBlok
{
    public Blok? GekoppeldBlok { get; set; }
    public int MeldernNummer { get; set; }
}

public class Perron : BaanSymbool
{
    public double Breedte { get; set; } = 80;
    public double Hoogte { get; set; } = 12;
    public string Omschrijving { get; set; } = "";
}

public class Tekst : BaanSymbool
{
    public string Inhoud { get; set; } = "tekst";
}

/// <summary>Een schakelaar: voor verschillende doeleinden te gebruiken, zoals het aan-/
/// uitzetten van de verlichting in een dorp - zoals de handleiding het omschrijft. Puur
/// een aan/uit-status, geen koppeling aan een blok.</summary>
public class Schakelaar : BaanSymbool
{
    public string Omschrijving { get; set; } = "";
    public bool Aan { get; set; }
}

/// <summary>Een pijl: geeft de richting aan waarin de trein rijdt in een blok of
/// wisselstraat. Puur informatief - Koploper werkt net zo goed zonder, zoals de
/// handleiding vermeldt.</summary>
public class Pijl : BaanSymbool
{
    public double HoekGraden { get; set; }
}

/// <summary>Een ontkoppelrail: een los stuk rail met een eigen decoderadres, waarmee
/// wagons automatisch losgekoppeld kunnen worden - een ontkoppelrail heeft, in
/// tegenstelling tot een wissel, geen "stand".</summary>
public class Ontkoppelrail : BaanSymbool
{
    public int Adres { get; set; }
}
