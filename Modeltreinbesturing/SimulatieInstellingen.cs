namespace Modeltreinbesturing;

/// <summary>
/// Centrale, tijdens het draaien aanpasbare instelling voor de simulatiesnelheid - een
/// vermenigvuldigingsfactor die op alle rij-gerelateerde timer-intervallen in TreinrouteWindow
/// wordt toegepast. 1.0 = normaal (het oorspronkelijke, standaard tempo). Een HOGERE factor
/// = LANGZAMER (elke stap duurt langer), een LAGERE factor = SNELLER. Bewust een losse,
/// statische klasse i.p.v. een instantie-veld op TreinrouteWindow: zo is de instelling ook
/// vanuit het kijkscherm (dat dat venster niet per se zichtbaar heeft) direct te wijzigen,
/// en blijft de waarde ook behouden als TreinrouteWindow zelf ondertussen opnieuw wordt
/// aangemaakt.
/// </summary>
public static class SimulatieInstellingen
{
    private static double _vertragingsFactor = 1.0;

    /// <summary>0.25 (4x zo snel) tot 5.0 (5x zo langzaam) - buiten die grenzen wordt de
    /// simulatie ofwel onbruikbaar snel (klapperende blokken, gemiste stappen) ofwel
    /// onwerkbaar traag.</summary>
    public static double VertragingsFactor
    {
        get => _vertragingsFactor;
        set => _vertragingsFactor = Math.Clamp(value, 0.25, 5.0);
    }

    private static double _standaardUitrolSeconden = 1.0;

    /// <summary>Koploper's "berekende stopplaats na... cm" (Algemeen -> Instellingen per
    /// database -> tabblad Bestemming/Snelheid): een loc bereikt de stopmelder al op
    /// minimumsnelheid, maar staat dan nog niet écht stil - er is nog een fysieke
    /// uitrolafstand nodig. Onze simulatie is TIJD- i.p.v. afstand/cm-gebaseerd, dus dit is
    /// het tijd-equivalent: hoe lang het nog duurt tussen het bereiken van de stopsectie en
    /// het echt stilstaan. Standaard 1.0 seconde (een bewust gekozen, kleine, merkbare
    /// vertraging - geen exacte cm-naar-seconden-omrekening, want we houden geen schaal of
    /// snelheid-in-cm/sec bij). Een individueel blok kan dit overschrijven, zie
    /// Blok.UitrolSecondenOverride.</summary>
    public static double StandaardUitrolSeconden
    {
        get => _standaardUitrolSeconden;
        set => _standaardUitrolSeconden = Math.Max(0, value);
    }

    /// <summary>Koploper: "alle geluiden en rookgeneratoren van treinen in- of
    /// uitschakelen" - een globale, database-brede aan/uit-schakelaar voor loc-functies met
    /// een gekoppeld .wav-geluidsbestand. Standaard aan (bestaand gedrag ongewijzigd voor
    /// wie dit nooit aanraakt). Bewust een sessie-instelling zoals VertragingsFactor, geen
    /// database-veld: geluid is puur een lokale, hoorbare beleving op DIT toestel, geen
    /// gegeven dat inhoudelijk bij de baan zelf hoort.</summary>
    public static bool GeluidenIngeschakeld { get; set; } = true;

    private static double _geluidsVolume = 1.0;

    /// <summary>Volume voor loc-functiegeluiden, 0.0 (stil) tot 1.0 (volledig) - een simpele
    /// aan/uit-schakelaar (GeluidenIngeschakeld hierboven) alleen bleek niet genoeg, een
    /// modelspoorder wil vaak juist wat zachter i.p.v. helemaal stil. Sessie-instelling.</summary>
    public static double GeluidsVolume
    {
        get => _geluidsVolume;
        set => _geluidsVolume = Math.Clamp(value, 0.0, 1.0);
    }

    /// <summary>Donkere modus voor het kijkscherm - een treinkamer is vaak bewust gedimd
    /// voor de sfeer/betere zichtbaarheid van de verlichte baan, dus een fel wit scherm
    /// ernaast is dan onprettig. Puur een sessie-instelling (zoals GeluidenIngeschakeld),
    /// geen database-veld - een voorkeur van de kijker op DIT moment, niet van de baan zelf.</summary>
    public static bool DonkereModus { get; set; }

    /// <summary>"Beursmodus": laat de baan onbemand, continu doordraaien voor publiek -
    /// periodiek wordt gecontroleerd of er geplaatste locs stilstaan (bijv. na een
    /// noodstop of het bereiken van een bestemmingsblok) en die krijgen dan automatisch,
    /// stilletjes (geen popup) een nieuwe rit, net als de "Go"-knop maar dan vanzelf
    /// herhaald. Sessie-instelling, geen database-veld.</summary>
    public static bool BeursModusActief { get; set; }

    // GEBRUIKERSVERZOEK ("ik heb geen enkele wissel horen schakelen bij het opstarten, ik
    // verwachtte ze 1 voor 1 in de juiste stand te gaan"): 100ms was in de praktijk te kort
    // - de bytes vertrekken weliswaar op tijd, maar een fysieke wisselmotor heeft vaak
    // langer nodig om daadwerkelijk te bewegen én tot stilstand te komen. Bij zo'n korte
    // pauze onderbreekt het volgende commando 'm dan mogelijk halverwege, waardoor je
    // weinig tot niets hoort bewegen. Ruimer gezet zodat elke wissel echt de tijd krijgt.
    // GEBRUIKERSCORRECTIE ("wissel5 schakelde zeer laat, motor13 ging fout doordat de
    // trein de kruiswissel al bereikte vóórdat die klaar was met bewegen"): 600ms bleek
    // te lang voor tijdkritisch, vooruitlopend wisselzetten tijdens een rijdende trein -
    // bij een kruiswissel (2 motoren na elkaar) en een wissel ervoor liep de TOTALE
    // opbouwtijd (3x deze pauze, plus elke motor se eigen bewegingstijd) op tot ruim 2,5
    // seconde, wat te lang bleek t.o.v. hoe snel de trein de kruiswissel na blokaankomst
    // bereikt. Terug naar een korte waarde: de eerder gerepareerde wachtrij-volgorde (geen
    // race condition meer) EN de "wacht op Dinamo se eigen verzendrij"-backpressure
    // (KlaarVoorVolgendeWisselCommando) zorgen samen al voor betrouwbare, niet-overlappende
    // verzending, afgestemd op Dinamo se eigen ~200ms-cyclus - een extra, kunstmatige pauze
    // hierbovenop is daardoor niet meer nodig voor de betrouwbaarheid, en kost tijdens het
    // rijden alleen maar kostbare seconden.
    private static double _wisselRustpauzeMilliseconden = 100;

    /// <summary>Koploper's "rustpauze" (Algemeen -> Instellingen per database -> tabblad
    /// Seinen/wissels): "wanneer verschillende wissels en seinen tegelijkertijd veranderd
    /// moeten worden zorgt Koploper ervoor dat dit één voor één gedaan wordt met een korte
    /// (instelbare) tussenpauze" - om een echte digitale bus niet te overbelasten met te
    /// veel gelijktijdige accessoire-commando's. Alleen relevant voor de HARDWARE-commando's
    /// zelf (via HardwareBeheerder.StuurWisselCommando/StuurSeinCommando); onze eigen
    /// software-status (Wissel.Stand, logging) wordt altijd meteen bijgewerkt, ongeacht deze
    /// pauze. Standaard 100 ms, database-breed, WEL opgeslagen (zoals StandaardUitrolSeconden).</summary>
    public static double WisselRustpauzeMilliseconden
    {
        get => _wisselRustpauzeMilliseconden;
        set => _wisselRustpauzeMilliseconden = Math.Max(0, value);
    }
}
