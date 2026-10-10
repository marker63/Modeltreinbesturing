using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Modeltreinbesturing.Hardware;
using Modeltreinbesturing.Model;

namespace Modeltreinbesturing;

public partial class TreinrouteWindow : Window
{
    private readonly BlokBeheerder _blokBeheerder;
    private readonly TreinrouteBeheerder _routeBeheerder;
    private readonly BaanOntwerpBeheerder _baanBeheerder;
    private readonly WisselstraatBeheerder _wisselstraatBeheerder;
    private readonly TreintypeBeheerder _treintypeBeheerder;
    private readonly StopverbodBeheerder _stopverbodBeheerder;
    private readonly StopverbodStilstandBeheerder _stopverbodStilstandBeheerder;
    private readonly TreinBeheerder _treinBeheerder;

    private const double BlokGrootte = 50;

    private Treinroute? _routeInOpbouw;
    private bool _bouwModusActief;
    private string? _pendingNaam;

    /// <summary>Eén rijdende trein: eigen pad, eigen gebeurtenissenreeks, eigen timer. Meerdere
    /// kunnen tegelijk in _actieveTreinen staan, zodat er nu echt meerdere routes tegelijk
    /// kunnen rijden - net als een echte controlekamer met meerdere treinen tegelijk.</summary>
    private class RijdendeTrein
    {
        public required Treinroute Route;
        public required List<Blok> Pad;
        public required List<(Blok Blok, Bezetmeldpunt? Melder, double WachttijdSeconden)> Gebeurtenissen;
        public int GebeurtenisIndex;
        public Blok? HuidigBlok;
        public Blok? VorigBlok; // voor Richtingsverbod bij automatisch rijden
        public bool IsOpstelrit;
        public DispatcherTimer? Timer;
        public Trein? Trein;
        public Dictionary<Blok, TeLangeTreinActie> TeLangeBlokken = new();

        /// <summary>Handmatig gepauzeerd door de gebruiker (kijkscherm) - het blok blijft
        /// gewoon bezet en alle reserveringen verderop blijven intact, alleen de timer
        /// staat stil. Zie PauzeerTreinIndienActief/HervatTreinIndienGepauzeerd.</summary>
        public bool IsGepauzeerd;

        /// <summary>De FYSIEKE rijrichting van deze rit op dit moment - START altijd
        /// "vooruit" (true), wisselt om bij elk kopspoor of elke Keer-relatie onderweg (zie
        /// VerwerkEventueelKeren hieronder). Gebruikt voor het daadwerkelijke hardware-
        /// commando (LogSnelheidsopbouw) - EERDER werd hier altijd "vooruit" verstuurd,
        /// ongeacht een kopspoor/Keer-relatie, wat gebruikerscorrectie bevestigde als een
        /// echt, praktisch probleem (de loc keerde nooit fysiek om).</summary>
        public bool RijdtVooruit = true;

        /// <summary>Laatst verzonden/nagestreefde snelheidsstap - nodig als startpunt voor
        /// een geleidelijke op-/afbouw (zie StuurSnelheidNaarHardware) i.p.v. steeds een
        /// directe sprong. Een eventuele LOPENDE ramp wordt bijgehouden in
        /// ActieveSnelheidsRampTimer, zodat een nieuw commando de vorige ramp eerst netjes
        /// kan afbreken (anders zouden twee ramps elkaar tegenwerken).</summary>
        public int HuidigeSnelheidStap;
        public DispatcherTimer? ActieveSnelheidsRampTimer;

        /// <summary>GEVONDEN, KRITIEK GAT (bytes-analyse: een loc die bij aankomst in een
        /// nieuw blok plotseling stilstaat, terwijl er nooit een stopcommando verstuurd
        /// is): elk Dinamo-blok is een eigen, ELEKTRISCH GESCHEIDEN rijstroom-sectie met
        /// een eigen zender - een eerder commando "via blok 12" bereikt de loc niet meer
        /// zodra hij fysiek blok 11 is ingereden, ook al blijft de gewenste snelheid
        /// ONVERANDERD. StuurSnelheidNaarHardware vergeleek voorheen ALLEEN de
        /// snelheidswaarde (HuidigeSnelheidStap) om te bepalen of er iets te versturen
        /// viel - dit onthoudt via WELK blok dat laatste commando daadwerkelijk verstuurd
        /// is, zodat een blokwissel (ook zonder snelheidswijziging) altijd een vers
        /// commando naar het NIEUWE blok afdwingt.</summary>
        public int LaatstGebruikteBlokVoorSnelheid;

        /// <summary>Gebruikersverzoek ("bezetmelders en volgorde zelflerend maken"): alle
        /// meldernummers die tijdens het WACHTEN op de huidige stap ECHT bezet gemeld zijn
        /// (in de volgorde waarin ze binnenkwamen), en die al bekend zijn als een van de
        /// Bezetmeldpunten van het doelblok - zie TreinrouteWindow.Bezetmelding_VanHardware/
        /// LeerBezetmelderVolgorde. Wordt bij elke nieuwe stap geleegd (AutomatischeStapProberen/
        /// PlanVolgendeGebeurtenis, samen met WachtBegonnenOp).</summary>
        public List<int> GeobserveerdeMeldersTijdensWachten = new();

        /// <summary>ALLEEN voor automatisch rijden: het blok dat OP DIT MOMENT gereserveerd
        /// is als eerstvolgende stap - null als er niets gereserveerd is (bijv. net
        /// aangekomen, nog geen nieuwe stap geprobeerd). BELANGRIJK, reden waarom dit een
        /// EIGEN veld nodig heeft i.p.v. gewoon in Pad kijken: bij automatisch rijden wordt
        /// het volgende blok in AutomatischeStapProberen METEEN gereserveerd, maar pas
        /// TOEGEVOEGD AAN Pad zodra de volledige stapwachttijd voorbij is en de trein er
        /// al aangekomen IS (op dat moment is de reservering alweer opgeheven!) - Pad bevat
        /// het gereserveerde blok dus NOOIT gedurende de periode dat het daadwerkelijk
        /// gereserveerd staat. Gebruikt door ActieveGereserveerdeTrajecten voor de
        /// wissel/lijn-reservering-kleuring in het kijkscherm.</summary>
        public Blok? GereserveerdVolgendBlok;

        /// <summary>ALLEEN relevant bij ECHTE hardware verbonden (niet Simulatie): het
        /// meldernummer waar deze rit OP DIT MOMENT op wacht voor de eerstvolgende stap -
        /// null als er niet op een echte melding gewacht wordt (Simulatie-modus, of dit
        /// blok heeft geen meldpunt ingesteld). Zie PlanVolgendeGebeurtenis/
        /// AutomatischeStapProberen/Bezetmelding_VanHardware: zodra de ECHTE hardware deze
        /// melder als bezet meldt, wordt de aankomst direct verwerkt.</summary>
        public int? WachtOpMeldernNummer;

        /// <summary>Tijdstip waarop het wachten op WachtOpMeldernNummer hierboven begon - zie
        /// Bezetmelding_VanHardware: het verschil tussen dit tijdstip en het moment van de
        /// ECHTE bezetmelding is de daadwerkelijk gemeten reistijd, die BlokBeheerder.
        /// RegistreerGeleerdeReistijd bijhoudt (Blok.GeleerdeReistijden) - de basis
        /// voor "de trein leert zelf hoe lang hij ergens over doet" i.p.v. een gesimuleerde
        /// schatting.</summary>
        public DateTime? WachtBegonnenOp;

        /// <summary>GEVONDEN, ARCHITECTUREEL GAT (gebruikerswaarneming: "de loc stopt nu
        /// zodra hij in de 1e sectie van blok 10 komt, en keert daar al" - een blok met
        /// meerdere secties werd bij de EERSTE (aankomst-)melder al als "compleet
        /// aangekomen" beschouwd, waarna een VASTE wachttijd (WachttijdBijKerenSeconden)
        /// bepaalde wanneer er weer vertrokken/gekeerd werd - puur een timer, geen feit,
        /// exact het patroon dat we juist overal probeerden uit te bannen. Als WachtOpMeldernNummer
        /// hierboven op dit moment gebruikt wordt om te wachten op de ECHTE stopmelder
        /// (het fysieke uiteinde, niet de eerste sectie) VOORDAT er gekeerd wordt, staat
        /// hier de wachttijd die daarna alsnog moet volgen (zie VerwerkAutomatischeAankomst/
        /// VerwerkStopmelderVoorKeren) - null in alle andere gevallen.</summary>
        public double? WachtOpStopmelderVoorKerenWachttijd;

        /// <summary>GEVONDEN GAT (gebruikerswaarneming: de veiligheidstimer voor
        /// WachtOpMeldernNummer bleef gewoon doortellen vanaf het (verkeerd gerichte)
        /// vertrek, ook nadat de gebruiker de rijrichting halverwege handmatig corrigeerde
        /// via de keer-knop - het budget was zo grotendeels al verbruikt vóórdat de trein
        /// uberhaupt de juiste kant op reed): onthoudt de wachttijd (seconden) die bij het
        /// starten van de huidige WachtOpMeldernNummer-wachtperiode is gebruikt, zodat
        /// KeerTreinIndienActief die desgewenst met een vers budget kan herstarten.</summary>
        public double? HuidigeWachtIntervalSeconden;

        /// <summary>Zie AutomatischeStapProberen: onthoudt wanneer voor het laatst een
        /// "geen bruikbaar volgend blok"-waarschuwing gelogd is, zodat een stilzwijgende
        /// oneindige herhaal-lus (elke seconde opnieuw proberen) niet ELKE seconde opnieuw
        /// een logregel geeft, maar wel gegarandeerd zichtbaar blijft.</summary>
        public DateTime? LaatsteGeenKandidatenWaarschuwing;

        /// <summary>GEVONDEN GAT (bytes-analyse: melder 21 wisselde 3x in 1,6 sec van stand -
        /// een trein die door zijn lengte precies op de grens van een korte, geïsoleerde
        /// sectie (zoals een kruiswissel) balanceert, kan zo'n meldpunt laten FLIKKEREN. De
        /// automatische richting-correctie (zie MainWindow.Bezetmelding_VanHardware) kon
        /// hierdoor zichzelf tegenspreken - meerdere keren vlak na elkaar keren, in plaats
        /// van de trein er voorbij te helpen): onthoudt wanneer voor het laatst automatisch
        /// gekeerd is voor DEZE trein, zodat een tweede trigger vlak daarna genegeerd wordt -
        /// de eerste correctie krijgt zo de kans om daadwerkelijk effect te hebben voordat
        /// een volgende (mogelijk foutieve) trigger opnieuw ingrijpt.</summary>
        public DateTime? LaatsteAutomatischeRichtingCorrectie;

        /// <summary>GEVONDEN, FUNDAMENTELE GEDRAGSFOUT (gebruikerscorrectie: "de
        /// veiligheidstimeout wil ik er echt uit hebben, dit is niet realistisch als de loc
        /// niet fysiek verder gaat - het scherm moet niet de indruk wekken dat hij wel
        /// rijdt"): er bestond eerder een "veiligheids-timeout" die, als de verwachte echte
        /// bezetmelding te lang uitbleef, de aankomst gewoon MAAR AANNAM en de simulatie
        /// liet doorlopen - inclusief het scherm dat een blok als "bezet" toonde terwijl de
        /// fysieke loc daar misschien nooit is aangekomen. Dat mechanisme is VERWIJDERD. In
        /// plaats daarvan wordt deze vlag gezet zodra de wachttijd verstrijkt zonder echte
        /// bevestiging: de rit PAUZEERT dan volledig (geen enkele verdere automatische stap
        /// meer) totdat de melding alsnog binnenkomt (Bezetmelding_VanHardware herstelt
        /// dan automatisch, zie daar) of de gebruiker zelf ingrijpt. Zie
        /// MeldTreinVastgelopen voor de volledige afhandeling.</summary>
        public bool Vastgelopen;

        /// <summary>Gebruikersverzoek (kijkscherm): "alle locs rijden naar het einde van hun
        /// gereserveerde rijweg en stoppen daar, dus geen noodstop maar gewoon een rustig
        /// stoppen" - zie TreinrouteWindow.AlleTreinenRustigStoppen (via het kijkscherm). Wordt gecontroleerd op
        /// PRECIES het moment dat een al ingezette/gereserveerde stap ECHT (bevestigd)
        /// voltooid is - dus NOOIT halverwege een overgang, en nooit op basis van een
        /// aanname. Zet zichzelf terug op false zodra de stop daadwerkelijk is uitgevoerd.</summary>
        public bool StopNaDezeStap;

        /// <summary>Wanneer deze rit voor het laatst daadwerkelijk vorderde (een gebeurtenis
        /// verwerkte) - voor het signaleren van een mogelijk vastgelopen/vergeten rit op het
        /// Dashboard (zie LangStilstaandeTreinen hieronder).</summary>
        public DateTime LaatsteVoortgang = DateTime.Now;

        /// <summary>Het treintype dat DEZE SPECIFIEKE rit daadwerkelijk gebruikt voor
        /// snelheid/gedrag - normaal gewoon Route.Treintype, maar bij Rangeren wordt dit
        /// (als er een rangeertreintype is aangewezen) vervangen door dát treintype. Bewust
        /// een EIGEN veld i.p.v. Route.Treintype rechtstreeks aan te passen: Route is een
        /// gedeeld object dat ook voor toekomstige ritten weer gewoon zijn eigen treintype
        /// moet gebruiken.</summary>
        public required Treintype? EffectiefTreintype;

        /// <summary>Zoals Koploper's "Overzicht locomotieven": elke actieve trein toont hier
        /// zijn eigen regel, zodat je 'm individueel kunt aanklikken voor Stop/Ga.</summary>
        public override string ToString()
        {
            string status = HuidigBlok != null ? $"blok {HuidigBlok.Nummer}" : "vertrekt";
            string soort = IsOpstelrit ? " [opstellen]" : "";
            return $"{Route.Omschrijving}{soort} — {status}";
        }
    }

    // Simulator: gebruikt bezetmeldpunten van een blok als die zijn ingesteld (via
    // Eigenschappen blok), anders valt hij terug op een vaste tijd - zo kan de logica
    // getest worden zonder dat er al hardware/echte melders aangesloten hoeven te zijn.
    private readonly List<RijdendeTrein> _actieveTreinen = new();

    /// <summary>Meldernummer -> blok, voor blokken die momenteel in de staart-fase zitten
    /// (net verlaten, nog niet definitief vrij) EN bij echte hardware wachten op een ECHTE
    /// vrijmelding (bezet=false) van hun eigen stopmelder - zie StartStaartFase. Nodig omdat
    /// Bezetmelding_VanHardware zo'n binnenkomende vrijmelding moet kunnen matchen aan het
    /// juiste blok om de staart-fase daadwerkelijk (en veilig) te kunnen beëindigen.</summary>
    private readonly Dictionary<int, Blok> _staartWachtMelders = new();
    // GEVONDEN, KRITIEK GAT (gebruikerswaarneming: route "blok3->blok4->blok3(andere
    // zijde)" - een lus - liep na de eerste ronde definitief vast, omdat blok3 nooit
    // "vrij" werd na een VERS vertrek zonder ook maar één bevestigd meldpunt: zonder
    // enige melder om op te wachten (zie StartStaartFase, "geen enkel meldpunt was bij
    // vertrek al bevestigd bezet"-tak) bleef dit blok voor de rest van de sessie
    // permanent bezet/staart - onschuldig voor een rechtdoor-route die er nooit meer
    // langskomt, maar FATAAL voor een lus die er juist weer naar terug moet). Houdt bij
    // welke blokken zo'n "leeg" vertrek hadden, zodat VerwerkAutomatischeAankomst
    // hieronder de bevestigde aankomst bij het VOLGENDE blok kan gebruiken als het enige
    // betrouwbare bewijs dat het blok toch echt verlaten is - beter dat dan voor altijd
    // wachten op een melding die nooit komt (dezelfde afweging als de bestaande "geen
    // korte afsluitende timer meer bij échte hardware"-fix hierboven, nu voor het geval
    // ÉCHT geen enkele melder ooit meer gaat helpen).
    private readonly HashSet<Blok> _legeStaartFaseBlokken = new();

    /// <summary>GEVONDEN, DEFINITIEVE OORZAAK ("treinen rijden de verkeerde kant op" bij
    /// live-testen - een trein, vers via de kijkscherm-Go-knop op een blok gezet zonder
    /// ooit een eigen meldpunt bevestigd te hebben, werd bij vertrek binnen ~30 sec
    /// driemaal (melders 26, 25, 17 van hetzelfde blok) automatisch "gekeerd"): als een
    /// blok via de hierboven beschreven "_legeStaartFaseBlokken"-noodgreep vrijverklaard
    /// wordt (BeeindigStaartFase, aangeroepen op grond van de aankomst bij het VOLGENDE
    /// blok, NIET op grond van een eigen, echte vrijmelding), is er in werkelijkheid
    /// HELEMAAL NIETS bekend over of de fysieke trein dit blok al daadwerkelijk heeft
    /// vrijgemaakt - er was immers nooit een eigen meldpunt om op te wachten. Zo'n blok
    /// hoort daarom hierna GEEN basis te zijn voor VindTreinDieMogelijkTerugrijdt: elke
    /// nog binnenkomende bezetmelding van zijn eigen meldpunten (de trein die, zoals een
    /// trein nu eenmaal doet, geleidelijk met zijn volle lengte over de resterende
    /// sensoren van het blok rolt) is dan onvermijdelijk NIEUW voor de software, en zou
    /// zonder deze uitzondering telkens opnieuw als "terugrijdend" aangezien worden -
    /// precies wat er gebeurde. Een blok dat wél een eigen, bevestigd meldpunt had bij
    /// vertrek heeft dit probleem niet (zie IsStaart-uitzondering in
    /// MainWindow.Bezetmelding_VanHardware): dat blok blijft gewoon "staart" totdat een
    /// ECHTE vrijmelding binnenkomt. Dit is het aanvullende geval voor een blok zonder
    /// ENKEL eigen bevestigd meldpunt, waar die normale weg niet beschikbaar is. Wordt
    /// pas weer verwijderd bij de VOLGENDE, verse vertrek-poging vanaf dit blok (zie
    /// StartStaartFase) - dus zodra de trein hier weer écht doorheen rijdt, telt de
    /// detectie gewoon weer normaal mee.</summary>
    private readonly HashSet<Blok> _synthetischVrijgegevenBlokken = new();

    /// <summary>Gebruikerswaarneming (spookmelding vlak na een vrijmelding van dezelfde
    /// melder die net een staart-fase afrondde): fysieke bezetmelders kunnen kortstondig
    /// "stuiteren" - een vrijmelding gevolgd door, binnen een fractie van een seconde, een
    /// nieuwe bezetmelding van DEZELFDE sensor. Zie de vrij-branch van
    /// Bezetmelding_VanHardware hieronder voor de volledige toelichting.</summary>
    private readonly Dictionary<int, DispatcherTimer> _staartVrijDebounceTimers = new();

    /// <summary>Hoe lang een "vrij"-melding van een staart-wacht-melder moet standhouden
    /// voordat de staart-fase daadwerkelijk wordt afgerond (en het blok dus echt vrijgegeven
    /// wordt) - een korte, vaste marge tegen sensor-stuiteren. Bewust kort: dit mag de
    /// normale, niet-stuiterende afhandeling niet merkbaar vertragen.</summary>
    private const int StaartVrijmeldingDebounceMilliseconden = 400;

    private bool _venstergesloten;

    /// <summary>Routes die niet konden starten (pad bezet) en wachten tot het vrijkomt.
    /// Wordt bij elke StopTrein opnieuw geprobeerd, in volgorde van prioriteit
    /// (HogePrioriteit eerst, dan de volgorde waarin ze in de wachtrij kwamen).</summary>
    private readonly List<Treinroute> _wachtrij = new();
    private bool _geldigheidBijwerkenActief;

    private readonly List<string> _log = new();
    private bool _spanningAan = true;
    private readonly HardwareBeheerder _hardwareBeheerder;
    private readonly BlokgroepBeheerder _blokgroepBeheerder;
    private readonly SnelleKlokBeheerder _snelleKlokBeheerder;

    /// <summary>Gebruikersverzoek: "in alle schermen een programma afsluiten in de
    /// menubalk" - dit venster had helemaal geen menubalk. MainWindow verzorgt de
    /// daadwerkelijke afhandeling (sluit de HELE applicatie), net als bij BaanontwerpWindow.</summary>
    public Action? ProgrammaAfsluitenGevraagd;

    public TreinrouteWindow(BlokBeheerder blokBeheerder, TreinrouteBeheerder routeBeheerder, BaanOntwerpBeheerder baanBeheerder, WisselstraatBeheerder wisselstraatBeheerder, TreintypeBeheerder treintypeBeheerder, StopverbodBeheerder stopverbodBeheerder, StopverbodStilstandBeheerder stopverbodStilstandBeheerder, TreinBeheerder treinBeheerder, HardwareBeheerder hardwareBeheerder, BlokgroepBeheerder blokgroepBeheerder, SnelleKlokBeheerder snelleKlokBeheerder)
    {
        InitializeComponent();
        VensterInstellingen.Toepassen(this, "TreinrouteWindow");
        Closing += (_, _) => VensterInstellingen.Bewaren(this, "TreinrouteWindow");
        // Dit venster wordt tegenwoordig soms bewust NOOIT getoond (zie MainWindow's
        // kijkscherm-integratie: de simulatie moet ook onzichtbaar op de achtergrond kunnen
        // draaien) - .IsLoaded blijft dan voor altijd false, dus die eigenschap kan NERGENS
        // meer gebruikt worden als "is dit venster nog geldig/open"-check (dat brak eerder
        // alle timer-callbacks stil af, zie _venstergesloten hieronder). Deze eigen vlag is
        // de betrouwbare vervanger: start op false, wordt pas true bij een ECHTE sluiting.
        Closed += (_, _) => _venstergesloten = true;
        _blokBeheerder = blokBeheerder;
        _routeBeheerder = routeBeheerder;
        _baanBeheerder = baanBeheerder;
        _wisselstraatBeheerder = wisselstraatBeheerder;
        _treintypeBeheerder = treintypeBeheerder;
        _stopverbodBeheerder = stopverbodBeheerder;
        _stopverbodStilstandBeheerder = stopverbodStilstandBeheerder;
        _treinBeheerder = treinBeheerder;
        _hardwareBeheerder = hardwareBeheerder;
        _blokgroepBeheerder = blokgroepBeheerder;
        _snelleKlokBeheerder = snelleKlokBeheerder;
        // Zie Bezetmelding_VanHardware hieronder: laat een vaste-route-rit direct op een
        // ECHTE bezetmelding reageren i.p.v. altijd op de gesimuleerde wachttijd te wachten.
        // Zelfde re-abonneer-patroon als MainWindow.Bezetmelding_VanHardware bij een
        // latere hardware-wissel (HardwareGewijzigd).
        _hardwareBeheerder.HardwareGewijzigd += () => _hardwareBeheerder.Huidige.BezetmeldingGewijzigd += Bezetmelding_VanHardware;
        _hardwareBeheerder.Huidige.BezetmeldingGewijzigd += Bezetmelding_VanHardware;
        _snelleKlokBeheerder.KoppelRoutesBron(() => _routeBeheerder.Treinroutes);
        _snelleKlokBeheerder.KlokGetikt += tijd => Dispatcher.Invoke(() => KlokTekst.Text = tijd.ToString(@"hh\:mm"));
        _snelleKlokBeheerder.VertrektijdBereikt += route => Dispatcher.Invoke(() => VertrektijdBereikt(route));
        KlokTekst.Text = _snelleKlokBeheerder.Instellingen.HuidigeTijd.ToString(@"hh\:mm");
        KlokActiefBox.IsChecked = _snelleKlokBeheerder.Instellingen.Actief;
        KlokSnelheidBox.Text = _snelleKlokBeheerder.Instellingen.SecondenPerModelMinuut.ToString(System.Globalization.CultureInfo.InvariantCulture);
        TreintypeCombo.ItemsSource = _treintypeBeheerder.Treintypes;
        BestemmingsblokCombo.ItemsSource = _blokBeheerder.Blokken;
        GeldigeTreintypesLijst.ItemsSource = _treintypeBeheerder.Treintypes;
        GeldigeTreinenLijst.ItemsSource = _treinBeheerder.Treinen;
        UitgeslotenWisselsLijst.ItemsSource = _baanBeheerder.Symbolen.OfType<Wissel>().ToList();
        AlternatieveStartblokkenLijst.ItemsSource = _blokBeheerder.Blokken;
        TreinCombo.ItemsSource = _treinBeheerder.Treinen;
        KoploperKleuren.SchemaGewijzigd += Redraw;
        VulRoutesLijst();
        VulSpanningKnop();
        Redraw();

        // "Dagdeel" (AM/PM), zoals Koploper's eigen logische actie voor een vaste
        // dienstregeling - leest nu af van dezelfde snelle klok als hierboven (voorheen
        // een eigen, losse, ALTIJD-actieve klok - samengevoegd tot één, door de gebruiker
        // zelf aan/uit te zetten "Klok loopt"-systeem).
        _snelleKlokBeheerder.KlokGetikt += _ => Dispatcher.Invoke(VulDagdeelWeergave);
        VulDagdeelWeergave();

        // GEBRUIKERSVERZOEK ("kan je ook ergens in het scherm vermelden welke JSON er
        // geladen is, en ook in de log, dan kan daar nooit twijfel over zijn"): zie
        // HuidigProject. Titelbalk van DIT venster (waar ook de log/hardware-communicatie
        // draait) toont de bestandsnaam, en de allereerste logregel van elke sessie met dit
        // venster benoemt het volledige pad - zodat een later gekopieerde/geplakte log nooit
        // voor discussie vatbaar is over welk project erbij hoorde.
        Title = $"Modeltreinbesturing - Treinroutes — {HuidigProject.Bestandsnaam}";
        Log($"Geladen project: {HuidigProject.Pad ?? "(geen project geladen)"}");
    }

    private void VulDagdeelWeergave()
    {
        bool isOchtend = _snelleKlokBeheerder.Instellingen.HuidigeTijd.Hours < 12;
        DagdeelTekst.Text = isOchtend ? "AM" : "PM";
    }

    /// <summary>Handmatig het dagdeel omzetten (bijv. voor het testen van Event Actions die
    /// op AM/PM reageren) - springt de klok naar het begin van de andere helft van de dag.</summary>
    private void WisselDagdeel_Click(object sender, RoutedEventArgs e)
    {
        bool isOchtend = _snelleKlokBeheerder.Instellingen.HuidigeTijd.Hours < 12;
        _snelleKlokBeheerder.Instellingen.HuidigeTijd = isOchtend ? new TimeSpan(12, 0, 0) : new TimeSpan(0, 0, 0);
        KlokTekst.Text = _snelleKlokBeheerder.Instellingen.HuidigeTijd.ToString(@"hh\:mm");
        VulDagdeelWeergave();
        Log($"Dagdeel handmatig gewisseld naar {DagdeelTekst.Text}.");
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        KoploperKleuren.SchemaGewijzigd -= Redraw;
        _snelleKlokBeheerder.Stop();
        foreach (var trein in _actieveTreinen.ToList())
            StopTrein(trein, null, magHerhalen: false);
    }

    // --- Log -------------------------------------------------

    public void Log(string bericht)
    {
        string regel = $"[{DateTime.Now:HH:mm:ss}] {bericht}";
        // BUG #45: ook in het (automatisch bewaarde) hardwarelog zetten, zodat beslissingen van de
        // software ("reserveert 3 naar 7", "NOODSTOP ...") op schijf naast de bytes staan en na
        // afsluiten niet verloren zijn.
        HardwareCommunicatieLog.Log("Info", "[Rit] " + bericht);
        _log.Add(regel);
        LogLijst.Items.Add(regel);
        if (LogLijst.Items.Count > 0)
            LogLijst.ScrollIntoView(LogLijst.Items[^1]);
    }

    private void KopieerLog_Click(object sender, RoutedEventArgs e)
    {
        if (_log.Count == 0)
        {
            StatusTekst.Text = "Nog niets om te kopiëren.";
            return;
        }
        try
        {
            Clipboard.SetText(string.Join(Environment.NewLine, _log));
            StatusTekst.Text = $"Log gekopieerd naar klembord ({_log.Count} regels).";
        }
        catch (Exception ex)
        {
            StatusTekst.Text = $"Kopiëren mislukt: {ex.Message}";
        }
    }

    // --- Route opbouwen -------------------------------------------------

    private void NieuweRoute_Click(object sender, RoutedEventArgs e)
    {
        string? naam = InputDialog.Vraag(this, "Nieuwe treinroute", "Naam van de route:", $"Route {_routeBeheerder.Treinroutes.Count + 1}");
        if (string.IsNullOrWhiteSpace(naam)) return;

        _pendingNaam = naam;
        _routeInOpbouw = null;
        _bouwModusActief = true;
        RoutesLijst.SelectedItem = null;
        StatusTekst.Text = "Klik het startblok.";
        Log($"Nieuwe route '{naam}' gestart — kies het startblok.");
        Redraw();
    }

    /// <summary>Kopieert de geselecteerde route compleet - inclusief Keuzeblokken en alle
    /// regels (Geldigheid, Treintype, Bestemmingsblok, Automatisch, enz.) - zodat je niet
    /// een vergelijkbare route helemaal opnieuw hoeft op te bouwen. Blok/Treintype/Trein-
    /// referenties worden GEDEELD (het zijn bestaande entiteiten, geen eigen data van de
    /// route), de lijsten zelf (Keuzeblokken/GeldigeTreintypes/GeldigeTreinen) worden wel
    /// als nieuwe, onafhankelijke lijst gekopieerd.</summary>
    private void DupliceerRoute_Click(object sender, RoutedEventArgs e)
    {
        if (RoutesLijst.SelectedItem is not Treinroute bron)
        {
            StatusTekst.Text = "Selecteer eerst een route om te dupliceren.";
            return;
        }

        var kopie = new Treinroute
        {
            Omschrijving = $"{bron.Omschrijving} (kopie)",
            Startblok = bron.Startblok,
            Keuzeblokken = new List<Blok>(bron.Keuzeblokken),
            Treintype = bron.Treintype,
            BlijfWachtenInBestemming = bron.BlijfWachtenInBestemming,
            HogePrioriteit = bron.HogePrioriteit,
            Bestemmingsblok = bron.Bestemmingsblok,
            Automatisch = bron.Automatisch,
            Info = bron.Info,
            Geldigheid = bron.Geldigheid,
            GeldigeTreintypes = new List<Treintype>(bron.GeldigeTreintypes),
            GeldigeTreinen = new List<Trein>(bron.GeldigeTreinen)
        };
        _routeBeheerder.Treinroutes.Add(kopie);
        VulRoutesLijst();
        RoutesLijst.SelectedItem = kopie;
        StatusTekst.Text = $"Route '{bron.Omschrijving}' gedupliceerd naar '{kopie.Omschrijving}'.";
        Log($"--- Route '{bron.Omschrijving}' gedupliceerd naar '{kopie.Omschrijving}' ---");
    }

    private void Exporteren_Click(object sender, RoutedEventArgs e)
    {
        var dialoog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "PNG-afbeelding (*.png)|*.png",
            FileName = "treinroutes.png"
        };
        if (dialoog.ShowDialog() != true) return;

        try
        {
            double breedte = Math.Max(SchemaCanvas.ActualWidth, 800);
            double hoogte = Math.Max(SchemaCanvas.ActualHeight, 600);

            var bitmap = new RenderTargetBitmap((int)breedte, (int)hoogte, 96, 96, PixelFormats.Pbgra32);
            var achtergrond = new Rectangle { Width = breedte, Height = hoogte, Fill = Brushes.White };
            achtergrond.Measure(new Size(breedte, hoogte));
            achtergrond.Arrange(new Rect(0, 0, breedte, hoogte));
            bitmap.Render(achtergrond);
            bitmap.Render(SchemaCanvas);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = System.IO.File.Create(dialoog.FileName))
                encoder.Save(stream);

            StatusTekst.Text = $"Treinroutes-schema geëxporteerd naar {dialoog.FileName}.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Exporteren mislukt: {ex.Message}", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Klaar_Click(object sender, RoutedEventArgs e)
    {
        if (_routeInOpbouw is null)
        {
            StatusTekst.Text = "Nog geen route gestart - klik eerst op een blok in het schema om het startblok vast te leggen.";
            return;
        }
        Log($"Route '{_routeInOpbouw.Omschrijving}' handmatig afgesloten bij blok {BerekenHuidigBlok().Nummer}.");
        AfsluitenRoute($"Route '{_routeInOpbouw.Omschrijving}' opgeslagen tot en met blok {BerekenHuidigBlok().Nummer}.");
    }

    private void Sluiten_Click(object sender, RoutedEventArgs e) => Close();
    private void ProgrammaAfsluiten_Click(object sender, RoutedEventArgs e) => ProgrammaAfsluitenGevraagd?.Invoke();

    private void Gebruiksaanwijzing_Click(object sender, RoutedEventArgs e)
    {
        new HelpWindow { Owner = this }.Show();
    }

    // --- Simulator: gebruikt bezetmeldpunten als die zijn ingesteld -------------------------------------------------

    private void StartRijden_Click(object sender, RoutedEventArgs e) => StartenMetRijden(opstelrit: false);

    private void Opstellen_Click(object sender, RoutedEventArgs e)
    {
        if (_bouwModusActief)
        {
            StatusTekst.Text = "Rond eerst de route af (Klaar/Escape) voordat je gaat opstellen.";
            return;
        }
        if (RoutesLijst.SelectedItem is not Treinroute route)
        {
            StatusTekst.Text = "Selecteer eerst een route in de lijst om op te stellen.";
            return;
        }
        var eindPad = _routeBeheerder.BerekenVolledigPad(route, _blokBeheerder);
        if (eindPad.Count == 0 || eindPad[^1].Type != BlokType.Opstelspoor)
        {
            StatusTekst.Text = $"Route '{route.Omschrijving}' eindigt niet in een blok van het type Opstelspoor — kies een route die daar eindigt, of stel het eindblok in via Eigenschappen blok.";
            return;
        }
        StartenMetRijden(opstelrit: true);
    }

    private void Vrijgeven_Click(object sender, RoutedEventArgs e)
    {
        if (RoutesLijst.SelectedItem is not Treinroute route)
        {
            StatusTekst.Text = "Selecteer eerst de route waarvan je het opgestelde eindblok wilt vrijgeven.";
            return;
        }
        var eindPad = _routeBeheerder.BerekenVolledigPad(route, _blokBeheerder);
        if (eindPad.Count == 0)
        {
            StatusTekst.Text = "Deze route heeft geen bekend eindblok.";
            return;
        }

        var eindblok = eindPad[^1];
        if (!_blokBeheerder.IsBezet(eindblok))
        {
            StatusTekst.Text = $"Blok {eindblok.Nummer} is niet bezet — er staat daar niets om vrij te geven.";
            return;
        }

        _blokBeheerder.ZetBezet(eindblok, false);
        LogSeinenVoorBlok(eindblok, false);
        string bericht = $"Blok {eindblok.Nummer} vrijgegeven — trein staat niet meer als opgesteld geregistreerd.";
        StatusTekst.Text = bericht;
        Log($"--- {bericht} ---");
        Redraw();
    }

    /// <summary>
    /// Gemeenschappelijke start voor zowel gewoon rijden overdag (opstelrit=false, blok wordt
    /// na aankomst weer vrijgegeven) als opstellen voor de nacht (opstelrit=true, het eindblok
    /// blijft bewust bezet/geparkeerd staan totdat er handmatig weer mee gereden wordt).
    /// </summary>
    /// <summary>Extern aan te roepen (bijv. vanuit MainWindow's "loc naar volgend blok
    /// slepen"-gebaar) om een route - meestal net aangemaakt en al toegevoegd aan
    /// _routeBeheerder - direct automatisch te laten rijden, zonder dat de gebruiker 'm
    /// eerst zelf in de lijst hoeft te selecteren.</summary>
    /// <summary>Actieve ritten die al langer dan de opgegeven drempel geen enkele
    /// gebeurtenis meer hebben verwerkt - meestal een vastgelopen rit (wachtend op een
    /// blok dat niet vrijkomt) of een simpelweg vergeten trein. Puur signalerend, voor het
    /// Dashboard.</summary>
    public IEnumerable<(Trein? Trein, string RouteOmschrijving, TimeSpan StilVoor)> LangStilstaandeTreinen(TimeSpan drempel) =>
        _actieveTreinen
            .Where(rt => DateTime.Now - rt.LaatsteVoortgang >= drempel)
            .Select(rt => (rt.Trein, rt.Route.Omschrijving, DateTime.Now - rt.LaatsteVoortgang));

    public bool StartAutomatischeRoute(Treinroute route, Trein? trein)
    {
        VulRoutesLijst();
        // De UI-selecties hieronder zijn nu puur voor VISUELE consistentie (zodat het
        // venster, als het toevallig open staat, laat zien welke route/trein net gestart
        // is) - StartenMetRijden hangt er met routeOverride/treinOverride niet meer
        // inhoudelijk van af, zie de toelichting daar.
        RoutesLijst.SelectedItem = route;
        TreinCombo.SelectedItem = trein;
        return StartenMetRijden(opstelrit: false, routeOverride: route, treinOverride: trein);
    }

    /// <summary>De snelle klok heeft de geplande vertrektijd van deze route bereikt - probeer
    /// 'm te starten met de trein die (indien aanwezig) al op het startblok staat. Lukt
    /// het niet (startblok bezet/gereserveerd, geen geldige trein, etc.), dan wordt dat
    /// gewoon gelogd en verder niets geforceerd - geen wachtrij, dit is een eenmalige poging
    /// op het geplande tijdstip zelf.</summary>
    private void VertrektijdBereikt(Treinroute route)
    {
        var trein = _blokBeheerder.LocOpBlok(route.Startblok);
        bool gelukt = StartAutomatischeRoute(route, trein);
        string tijdTekst = _snelleKlokBeheerder.Instellingen.HuidigeTijd.ToString(@"hh\:mm");
        Log(gelukt
            ? $"--- Dienstregeling ({tijdTekst}): route '{route.Omschrijving}' automatisch gestart. ---"
            : $"--- Dienstregeling ({tijdTekst}): route '{route.Omschrijving}' kon niet gestart worden (startblok bezet/gereserveerd of geen geldige trein) - overgeslagen. ---");
    }

    /// <summary>Bepaalt welk treintype DEZE SPECIFIEKE rit daadwerkelijk gebruikt voor
    /// snelheid/gedrag. Volgorde (hoogste prioriteit eerst): (1) Rangeren - een operationele,
    /// tijdelijke keuze van de gebruiker gaat voor; (2) geijkte snelheid van de loc zelf -
    /// een permanent, gemeten (hier: handmatig ingevoerd) kenmerk van DEZE loc; (3) het
    /// gewone route/treintype, zoals voorheen. Geeft bij (1) en (2) een NIEUW, tijdelijk
    /// Treintype-object terug (nooit toegevoegd aan _treintypeBeheerder.Treintypes, puur
    /// voor de duur van deze ene rit) - Route.Treintype zelf blijft ongewijzigd, want dat is
    /// een gedeeld object dat voor toekomstige ritten weer zijn eigen treintype moet
    /// gebruiken.</summary>
    private Treintype? BepaalEffectiefTreintype(Trein? trein, Treintype? routeTreintype, bool rangeert)
    {
        if (rangeert && _treintypeBeheerder.RangeerTreintype != null)
            return _treintypeBeheerder.RangeerTreintype;

        if (trein?.GebruikGeijkteSnelheid == true)
        {
            var (max, gemiddeld, min) = trein.AfgeleideSnelheden();
            return new Treintype
            {
                Omschrijving = $"(geijkt: {trein.Omschrijving})",
                MaxSnelheid = max,
                GemiddeldeSnelheid = gemiddeld,
                MinimumSnelheid = min
            };
        }

        return routeTreintype;
    }

    private bool StartenMetRijden(bool opstelrit, bool stil = false, Treinroute? routeOverride = null, Trein? treinOverride = null)
    {
        if (!_spanningAan)
        {
            if (!stil)
            {
                StatusTekst.Text = "De spanning staat uit (rood spiegelei) - zet 'm eerst aan voordat er een trein kan rijden.";
                Log("--- Route geweigerd: spanning staat uit ---");
            }
            return false;
        }
        if (_bouwModusActief)
        {
            if (!stil) StatusTekst.Text = "Rond eerst de route af (Klaar/Escape) voordat je gaat rijden.";
            return false;
        }
        // GEVONDEN KWETSBAARHEID (gebruikersmelding: net geplaatste loc, "Go" zegt toch
        // "geen locs" terwijl de loc zichtbaar op zijn blok stond): deze methode las de te
        // starten route/trein voorheen UITSLUITEND uit RoutesLijst.SelectedItem/TreinCombo.
        // SelectedItem - StartAutomatischeRoute (o.a. "Go") zette die selecties weliswaar
        // programmatisch vlak vóór deze aanroep, maar dat maakt de boel onnodig afhankelijk
        // van WPF-selectiegedrag i.p.v. gewoon de al bekende route/trein rechtstreeks door
        // te geven. routeOverride/treinOverride hieronder omzeilen dat: als ze zijn
        // meegegeven (zoals nu vanuit StartAutomatischeRoute) is er geen enkele
        // afhankelijkheid meer van of een UI-selectie wel of niet "bleef hangen".
        var route = routeOverride ?? RoutesLijst.SelectedItem as Treinroute;
        if (route is null)
        {
            if (!stil) StatusTekst.Text = "Selecteer eerst een route in de lijst om te laten rijden.";
            return false;
        }
        if (_actieveTreinen.Any(t => t.Route == route))
        {
            if (!stil) StatusTekst.Text = $"Route '{route.Omschrijving}' rijdt al — er kan niet twee keer tegelijk mee gereden worden.";
            return false;
        }

        // Dagdeel-beperking: "de waarde van de logische actie 'Dagdeel' wordt ook gebruikt
        // bij de keuze van de pendeltrein uit het schaduwstation" - een route kan aan AM of
        // PM gebonden zijn, onafhankelijk van welke trein er precies mee mag rijden.
        bool huidigDagdeelIsOchtend = _snelleKlokBeheerder.Instellingen.HuidigeTijd.Hours < 12;
        if (route.DagdeelGeldigheid == DagdeelGeldigheid.AlleenOchtend && !huidigDagdeelIsOchtend)
        {
            if (!stil)
            {
                StatusTekst.Text = $"Route '{route.Omschrijving}' geldt alleen in de ochtend (AM) - het is nu PM.";
                Log($"--- Route '{route.Omschrijving}' geweigerd: geldt alleen AM, huidig dagdeel is PM ---");
            }
            return false;
        }
        if (route.DagdeelGeldigheid == DagdeelGeldigheid.AlleenMiddag && huidigDagdeelIsOchtend)
        {
            if (!stil)
            {
                StatusTekst.Text = $"Route '{route.Omschrijving}' geldt alleen in de middag (PM) - het is nu AM.";
                Log($"--- Route '{route.Omschrijving}' geweigerd: geldt alleen PM, huidig dagdeel is AM ---");
            }
            return false;
        }

        // Automatisch rijden: geen vast pad, de trein kiest onderweg zelf bij elke splitsing
        // het eerste vrije/toegestane vervolgblok. Zie AutomatischTick.
        if (route.Automatisch)
        {
            var gekozenTreinAuto = treinOverride ?? (TreinCombo.SelectedItem as Trein);
            if (!route.IsGeldigVoor(gekozenTreinAuto))
            {
                if (!stil)
                {
                    string reden = gekozenTreinAuto is null
                        ? $"deze route geldt alleen voor bepaalde treinen (Geldigheid: {route.Geldigheid}), maar er is geen trein gekozen"
                        : $"trein '{gekozenTreinAuto.Omschrijving}' valt niet onder de Geldigheid ({route.Geldigheid}) van deze route";
                    StatusTekst.Text = $"Route '{route.Omschrijving}' kan niet rijden: {reden}.";
                    Log($"--- Route '{route.Omschrijving}' geweigerd: {reden} ---");
                }
                return false;
            }
            // GEVONDEN REGRESSIE (gebruikersmelding: "Go" zegt "geen locs" terwijl er wél
            // een loc handmatig op het startblok stond): sinds een eerdere fix markeert een
            // handmatig geplaatste loc zijn blok terecht als IsBezet (voor spookmelding-
            // detectie), maar DEZE check gebruikte IsBezet ook als "startblok is vrij"-
            // voorwaarde - dus een blok met precies de loc erop die je WILT laten rijden
            // werd zelf als blokkade gezien. Fix: IsBezet is GEEN probleem als het exact de
            // loc is die deze route gaat rijden (dat IS de bedoeling van "Go" - vanaf een
            // geplaatste loc starten); alleen een reservering (een ANDERE rit is al
            // onderweg hierheen) blijft dan nog een echte blokkade.
            var locOpStartblok = _blokBeheerder.LocOpBlok(route.Startblok);
            bool eigenLocOpStartblok = gekozenTreinAuto != null && locOpStartblok == gekozenTreinAuto;
            bool startblokBeschikbaar = eigenLocOpStartblok
                ? !_blokBeheerder.IsGereserveerd(route.Startblok) && !_blokBeheerder.IsFoutmelding(route.Startblok)
                : !_blokBeheerder.IsGeblokkeerdVoorRit(route.Startblok);
            if (startblokBeschikbaar)
            {
                bool rangeertAuto = gekozenTreinAuto?.Rangeren == true;
                var effectiefTreintypeAuto = BepaalEffectiefTreintype(gekozenTreinAuto, route.Treintype, rangeertAuto);
                var autoTrein = new RijdendeTrein
                {
                    Route = route,
                    Pad = new List<Blok> { route.Startblok },
                    Gebeurtenissen = new(),
                    IsOpstelrit = opstelrit,
                    Trein = gekozenTreinAuto,
                    HuidigBlok = route.Startblok,
                    EffectiefTreintype = effectiefTreintypeAuto
                };
                // Zie Blok.GeleerdeStartrichtingVooruit - dit IS de verse-start-situatie
                // (route.Startblok, nog geen stap gezet) waar dat voor bedoeld is.
                if (route.Startblok.GeleerdeStartrichtingVooruit is bool geleerdeStartrichtingAuto)
                    autoTrein.RijdtVooruit = geleerdeStartrichtingAuto;
                // Was dit blok "handmatig bezet" (een net geplaatste loc, zie BaanCanvas_Drop
                // e.a.) - dat wordt het nu een GEWONE bezetting door deze automatische rit,
                // dezelfde overgang als bij elke volgende stap onderweg.
                if (eigenLocOpStartblok) _blokBeheerder.ZetHandmatigBezet(route.Startblok, false);
                _blokBeheerder.ZetBezet(route.Startblok, true);
                _actieveTreinen.Add(autoTrein);
                StatusTekst.Text = $"Route '{route.Omschrijving}' rijdt automatisch vanaf blok {route.Startblok.Nummer}...";
                Log($"--- Start automatisch rijden: route '{route.Omschrijving}' vanaf blok {route.Startblok.Nummer} — {_actieveTreinen.Count} trein(en) nu actief ---");
                Redraw();
                PlanVolgendeAutomatischeStap(autoTrein);
                return true;
            }
            if (!stil) StatusTekst.Text = $"Route '{route.Omschrijving}' kan niet starten: startblok {route.Startblok.Nummer} is al bezet/gereserveerd.";
            return false;
        }

        // Welke trein rijdt er? Nodig ZOWEL voor het bepalen van een eventueel alternatief
        // startblok (Koploper: "soms is het handig als een vaste treinroute vanuit meerdere
        // richtingsblokken gestart kan worden") als voor de latere treinlengte-check.
        var gekozenTrein = treinOverride ?? (TreinCombo.SelectedItem as Trein);

        // Staat de gekozen trein op een van de vastgelegde alternatieve startblokken i.p.v.
        // op het letterlijke Startblok, bouw de route dan vanaf DAT blok op - de rest van
        // het pad (Keuzeblokken) blijft ongewijzigd van toepassing.
        Blok? werkelijkStartblok = null;
        if (gekozenTrein != null && route.AlternatieveStartblokken.Count > 0)
        {
            var huidigBlokVanTrein = _blokBeheerder.BlokVanLoc(gekozenTrein);
            if (huidigBlokVanTrein != null && huidigBlokVanTrein != route.Startblok && route.AlternatieveStartblokken.Contains(huidigBlokVanTrein))
                werkelijkStartblok = huidigBlokVanTrein;
        }

        var pad = _routeBeheerder.BerekenVolledigPad(route, _blokBeheerder, werkelijkStartblok);

        // Bestemmingsblok: als vastgelegd, moet de route ook echt daar eindigen - anders is
        // er ergens onderweg een gewijzigde relatie/richtingsverbod/doodlopend spoor dat de
        // route niet meer laat aankomen waar bedoeld.
        if (route.Bestemmingsblok != null && pad[^1] != route.Bestemmingsblok)
        {
            if (!stil)
            {
                StatusTekst.Text = $"Route '{route.Omschrijving}' kan niet rijden: komt uit bij blok {pad[^1].Nummer} in plaats van het vastgelegde bestemmingsblok {route.Bestemmingsblok.Nummer}.";
                Log($"--- Route '{route.Omschrijving}' geweigerd: eindigt bij blok {pad[^1].Nummer} i.p.v. bestemmingsblok {route.Bestemmingsblok.Nummer} ---");
            }
            return false;
        }

        // Treinlengte: welke blokken op het pad zijn te kort voor de gekozen trein? Wat er
        // dan gebeurt is een eigenschap van het BLOK (TeLangeTreinActie), niet van de route -
        // "mag niet stoppen" weigert de rit, de andere twee accepteren 'm en zorgen tijdens
        // het rijden voor extra bezet-tijd op het vorige blok/wisselstraat.

        // Geldigheid: mag deze route wel gereden worden met deze (of geen) trein?
        if (!route.IsGeldigVoor(gekozenTrein))
        {
            if (!stil)
            {
                string reden = gekozenTrein is null
                    ? $"deze route geldt alleen voor bepaalde treinen (Geldigheid: {route.Geldigheid}), maar er is geen trein gekozen"
                    : $"trein '{gekozenTrein.Omschrijving}' valt niet onder de Geldigheid ({route.Geldigheid}) van deze route";
                StatusTekst.Text = $"Route '{route.Omschrijving}' kan niet rijden: {reden}.";
                Log($"--- Route '{route.Omschrijving}' geweigerd: {reden} ---");
            }
            return false;
        }

        // Treinlengte: welke trein rijdt er, en zijn er blokken op het pad die te kort zijn
        // voor die trein? Wat er dan gebeurt is een eigenschap van het BLOK (TeLangeTreinActie),
        // niet van de route - "mag niet stoppen" weigert de rit, de andere twee accepteren 'm
        // en zorgen tijdens het rijden voor extra bezet-tijd op het vorige blok/wisselstraat.
        var teLangeBlokken = new Dictionary<Blok, TeLangeTreinActie>();
        if (gekozenTrein != null && gekozenTrein.Lengte > 0)
        {
            foreach (var blok in pad)
            {
                if (blok.MaxTreinlengte <= 0 || blok.MaxTreinlengte >= gekozenTrein.Lengte) continue;
                if (blok.TeLangeTreinActie.HasFlag(TeLangeTreinActie.MagNietStoppen))
                {
                    if (!stil)
                    {
                        StatusTekst.Text = $"Route '{route.Omschrijving}' kan niet rijden met trein '{gekozenTrein.Omschrijving}': blok {blok.Nummer} is te kort ({blok.MaxTreinlengte} cm) en de trein mag daar niet stoppen.";
                        Log($"--- Route '{route.Omschrijving}' geweigerd: blok {blok.Nummer} te kort voor trein '{gekozenTrein.Omschrijving}' (mag niet stoppen) ---");
                    }
                    return false;
                }
                if (blok.TeLangeTreinActie != TeLangeTreinActie.GeenActie)
                    teLangeBlokken[blok] = blok.TeLangeTreinActie;
            }
        }

        // Stopverbod: weiger te starten als het pad een blok bevat waar niet gestopt mag
        // worden - in onze simulator krijgt elk blok in het pad een stopmelding, dus dit
        // wordt tegen het hele uitgeklapte pad getoetst, niet alleen het eindblok.
        var verbodenStopBlok = _stopverbodBeheerder.EersteConflict(pad, route.Treintype);
        if (verbodenStopBlok != null)
        {
            if (!stil)
            {
                StatusTekst.Text = $"Route '{route.Omschrijving}' kan niet rijden: blok {verbodenStopBlok.Nummer} heeft een stopverbod.";
                Log($"--- Route '{route.Omschrijving}' geweigerd: stopverbod op blok {verbodenStopBlok.Nummer} ---");
            }
            return false;
        }

        // Stopverbod stilstand: verbiedt alleen dat de route híér eindigt (stilstaan als
        // eindbestemming) - doorrijden via dezelfde overgang blijft toegestaan, dus dit
        // wordt alleen tegen de LAATSTE stap van het pad getoetst, niet het hele pad.
        if (pad.Count >= 2)
        {
            var eindblok = pad[^1];
            var voorlaatsteBlok = pad[^2];
            var blokDaarvoor = pad.Count >= 3 ? pad[^3] : null;
            if (!_stopverbodStilstandBeheerder.IsStilstaanToegestaan(blokDaarvoor, voorlaatsteBlok, eindblok, route.Treintype))
            {
                if (!stil)
                {
                    StatusTekst.Text = $"Route '{route.Omschrijving}' kan niet rijden: stopverbod stilstand op blok {eindblok.Nummer} (vanaf blok {voorlaatsteBlok.Nummer}).";
                    Log($"--- Route '{route.Omschrijving}' geweigerd: stopverbod stilstand op blok {eindblok.Nummer} ---");
                }
                return false;
            }
        }

        // Defecte wissel: een route die een wisselstraat met een als DEFECT gemarkeerde
        // wissel nodig heeft, mag niet starten - net als bij een echte kapotte/vastgelopen
        // wissel op de baan. Dit is een configuratieprobleem (lost zichzelf niet op door te
        // wachten), dus vóór de botsingsdetectie gecontroleerd, net als de andere
        // configuratie-weigeringen hierboven.
        for (int i = 0; i < pad.Count - 1; i++)
        {
            var wisselstraatVoorDefectCheck = _wisselstraatBeheerder.WisselstratenTussen(pad[i], pad[i + 1]).FirstOrDefault();
            var defecteWissel = wisselstraatVoorDefectCheck?.Wissels.FirstOrDefault(w => w.Wissel.IsDefect && !route.UitgeslotenWissels.Contains(w.Wissel));
            if (defecteWissel != null)
            {
                if (!stil)
                {
                    StatusTekst.Text = $"Route '{route.Omschrijving}' kan niet rijden: wissel {defecteWissel.Wissel.Adres} is gemarkeerd als defect.";
                    Log($"--- Route '{route.Omschrijving}' geweigerd: wissel {defecteWissel.Wissel.Adres} is defect ---");
                }
                return false;
            }
        }

        // Botsingsdetectie: weiger te starten als een blok op dit pad al bezet is, of
        // gereserveerd/bezet door een andere op dit moment actieve trein - anders zouden
        // twee treinen ongemerkt hetzelfde stuk spoor kunnen claimen. Dit is ook de enige
        // reden om een route in de wachtrij te zetten (zie InWachtrijZetten_Click) - de
        // andere weigeringen zijn configuratieproblemen die vanzelf niet oplossen.
        // UITZONDERING (zelfde regressie/fix als hierboven bij Automatisch): het
        // STARTBLOK (pad[0]) telt niet mee als "al bezet" als het precies de loc is die
        // deze route gaat rijden - dat is immers exact de bedoeling van starten vanaf een
        // geplaatste loc, geen botsing. Elk ANDER blok verderop op het pad blijft
        // onvoorwaardelijk gecontroleerd.
        var conflicterendBlok = pad.FirstOrDefault(b =>
        {
            if (b == pad[0] && gekozenTrein != null && _blokBeheerder.LocOpBlok(b) == gekozenTrein)
                return _blokBeheerder.IsGereserveerd(b) || _blokBeheerder.IsFoutmelding(b); // eigen loc op het startblok: alleen een reservering is nog een echte blokkade
            return _blokBeheerder.IsGeblokkeerdVoorRit(b);
        });
        if (conflicterendBlok != null)
        {
            if (!stil)
            {
                var conflicterendeTrein = _actieveTreinen.FirstOrDefault(t => t.Pad.Contains(conflicterendBlok));
                string reden = conflicterendeTrein != null
                    ? $"blok {conflicterendBlok.Nummer} is al in gebruik door route '{conflicterendeTrein.Route.Omschrijving}'"
                    : $"blok {conflicterendBlok.Nummer} is al bezet/gereserveerd";
                StatusTekst.Text = $"Route '{route.Omschrijving}' kan nu niet starten: {reden}. Probeer het later opnieuw, of zet 'm in de wachtrij.";
                Log($"--- Route '{route.Omschrijving}' geweigerd: {reden} ---");
            }
            return false;
        }

        // Blokgroepen met "enkele treinbeweging" (bijv. parallelle opstelsporen in een
        // schaduwstation): mag er maar in ÉÉN blok van de hele groep tegelijk een trein
        // actief zijn - de andere blokken in dezelfde groep gelden dan ook als bezet, zelfs
        // als er zelf geen trein in staat.
        foreach (var blok in pad)
        {
            var groep = _blokgroepBeheerder.GroepVan(blok);
            if (groep is null || !groep.EnkeleTreinbeweging) continue;

            var conflicterendGroepsblok = groep.Blokken
                .Where(b => b != blok && _blokBeheerder.IsGeblokkeerdVoorRit(b))
                .FirstOrDefault();
            if (conflicterendGroepsblok is null) continue;

            if (!stil)
            {
                string reden = $"blok {blok.Nummer} zit in blokgroep '{groep.Naam}' (enkele treinbeweging), en blok {conflicterendGroepsblok.Nummer} in diezelfde groep is al in gebruik";
                StatusTekst.Text = $"Route '{route.Omschrijving}' kan nu niet starten: {reden}.";
                Log($"--- Route '{route.Omschrijving}' geweigerd: {reden} ---");
            }
            return false;
        }

        // Rangeren: gebruik het aangewezen rangeertreintype (Koploper: "Algemeen ->
        // Instellingen per database") i.p.v. de eigen route/treintype-snelheid van de
        // trein, zodat een rangerende loc ook écht langzamer rijdt - niet alleen "geen
        // massasimulatie" (het oude, eenvoudigere gedrag zonder deze koppeling). Zonder
        // Rangeren: geijkte snelheid van de loc zelf (indien aangezet) gaat voor de
        // snelheid van het treintype.
        bool rangeert = gekozenTrein?.Rangeren == true;
        var effectiefTreintype = BepaalEffectiefTreintype(gekozenTrein, route.Treintype, rangeert);

        var trein = new RijdendeTrein
        {
            Route = route,
            Pad = pad,
            Gebeurtenissen = BouwGebeurtenissen(pad, effectiefTreintype, rangeert, gekozenTrein?.Lengte ?? 0, gekozenTrein, route.MaxSnelheid, _blokBeheerder.Relaties),
            IsOpstelrit = opstelrit,
            Trein = gekozenTrein,
            EffectiefTreintype = effectiefTreintype,
            TeLangeBlokken = teLangeBlokken
        };
        // Zie Blok.GeleerdeStartrichtingVooruit: een verse start (dit IS er altijd een op
        // dit punt, trein.VorigBlok is nog nooit gezet) gebruikt de eerder geleerde,
        // door de gebruiker zelf bevestigde rijrichting voor dit specifieke startblok, als
        // die er is - anders blijft de standaardwaarde (vooruit) gewoon staan.
        if (pad[0].GeleerdeStartrichtingVooruit is bool geleerdeStartrichting)
            trein.RijdtVooruit = geleerdeStartrichting;
        _actieveTreinen.Add(trein);

        // Was het startblok "handmatig bezet" (een net geplaatste loc) - net als bij
        // Automatisch hierboven wordt dat nu een gewone bezetting door deze rit. LET OP:
        // ZetHandmatigBezet(...,false) haalt het blok ook uit de "gewone" bezet-lijst (zie
        // BlokBeheerder) - dus meteen daarna weer expliciet ZetBezet(...,true), anders zou
        // het blok een fractie lijken vrij te zijn terwijl de loc er nog gewoon op staat.
        if (gekozenTrein != null && _blokBeheerder.LocOpBlok(pad[0]) == gekozenTrein)
        {
            _blokBeheerder.ZetHandmatigBezet(pad[0], false);
            _blokBeheerder.ZetBezet(pad[0], true);
        }

        foreach (var blok in pad) _blokBeheerder.ZetGereserveerd(blok, true);
        StatusTekst.Text = opstelrit ? $"Rijdt route '{route.Omschrijving}' om op te stellen..." : $"Rijdt route '{route.Omschrijving}'...";
        string treintypeTekst = route.Treintype != null ? $" [treintype: {route.Treintype.Omschrijving}]" : "";
        string treinTekst = gekozenTrein != null ? $" [trein: {gekozenTrein.Omschrijving}]" : "";
        string rangerenTekst = gekozenTrein?.Rangeren == true ? " [RANGEREN: massasimulatie uit]" : "";
        Log($"--- Start rijden: route '{route.Omschrijving}' ({string.Join(" -> ", pad.Select(b => b.Nummer))}){(opstelrit ? " [opstelrit]" : "")}{treintypeTekst}{treinTekst}{rangerenTekst} — {_actieveTreinen.Count} trein(en) nu actief ---");
        Log($"Volledig pad gereserveerd (geel).");

        int aantalWisselsGezetVoorRoute = ZetWisselsVoorRoute(pad, route);
        Redraw();

        var gekoppeldeSeinen = _baanBeheerder.Symbolen.OfType<Sein>()
            .Where(s => s.GekoppeldBlok != null && pad.Contains(s.GekoppeldBlok))
            .ToList();
        Log(gekoppeldeSeinen.Count == 0
            ? "Geen seinen gekoppeld aan blokken op dit pad (dus geen sein-meldingen te verwachten in deze rit)."
            : $"{gekoppeldeSeinen.Count} sein(en) gekoppeld op dit pad: blok(ken) {string.Join(", ", gekoppeldeSeinen.Select(s => s.GekoppeldBlok!.Nummer))}.");

        // Zie AutomatischeStapProberen voor de volledige toelichting bij dit gat
        // (gebruikersmelding: "ik zag enkele keren dat de wissel verkeerd stond waardoor de
        // trein verkeerd reed") - hetzelfde geldt hier: pas nadat de wisselstraten voor de
        // HELE route zijn gezet, mag de trein bij ECHTE hardware ook daadwerkelijk vertrekken,
        // en dat vertrek moet de ingestelde rustpauze afwachten als er iets te zetten viel.
        if (aantalWisselsGezetVoorRoute > 0 && _hardwareBeheerder.Huidige is not SimulatieHardware)
        {
            var wisselWachtTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(SimulatieInstellingen.WisselRustpauzeMilliseconden) };
            wisselWachtTimer.Tick += (_, _) =>
            {
                wisselWachtTimer.Stop();
                if (_venstergesloten) return;
                PlanVolgendeGebeurtenis(trein);
            };
            wisselWachtTimer.Start();
        }
        else
        {
            PlanVolgendeGebeurtenis(trein);
        }
        return true;
    }

    /// <summary>
    /// Zet een pad blokken om in een reeks gesimuleerde bezetmeldingen: elk blok met een
    /// Voorblok-meldpunt geeft eerst die melding (trein aangekomen), elk blok met een
    /// Stopsectie-meldpunt daarna die melding (trein stopt/remt). Een blok zonder enig
    /// meldpunt krijgt één generieke aankomstmelding op een vaste tijd, als terugvaloptie
    /// zolang er nog geen meldpunten zijn ingesteld.
    /// </summary>
    /// <summary>
    /// Zet automatisch alle wissels die bij deze route horen op de juiste stand - net als
    /// een echt beveiligingssysteem dat de hele wisselstraat vastlegt zodra een route wordt
    /// aangevraagd, in plaats van dat de gebruiker vooraf elke wissel met de hand moet zetten.
    /// Voor elk opeenvolgend blokpaar in het pad wordt de eerste passende wisselstraat
    /// (laagste volgnummer) gebruikt; is er geen wisselstraat voor dat blokpaar (een gewoon
    /// doorgaand stuk zonder splitsing), dan is er ook niets om te zetten.
    /// </summary>
    /// <summary>Gebruikerswaarneming: "de loc reserveerde naar blok 2 terwijl de wissel naar
    /// blok 4 stond en de ANDERE wissel ernaast op defect stond" - de trein kon dus nooit
    /// echt bij blok 2 komen, maar de software had dat niet voorzien bij het KIEZEN van de
    /// kandidaat (dat gebeurt puur op basis van de blok-topologie, los van of de wissels
    /// onderweg daadwerkelijk in de juiste stand GEZET kunnen worden). Een defecte wissel
    /// kan niet aangestuurd worden (zie ZetWisselsTussenBlokken: "if (wissel.IsDefect)
    /// continue") en blijft dus gewoon in zijn HUIDIGE stand staan - staat die toevallig al
    /// naar de andere kant, dan is dit pad simpelweg niet haalbaar, ongeacht wat de
    /// topologie beweert. Controleert dat hier, VOORDAT een kandidaat gekozen wordt, i.p.v.
    /// pas achteraf te ontdekken dat de trein ergens anders is uitgekomen dan gereserveerd.</summary>
    /// <summary>Gebruikersverzoek: "zo expliciet mogelijk alles maken, want alleen de
    /// kortste route is erg saai" - bij MEERDERE fysiek geldige paden tussen dezelfde twee
    /// blokken (bijv. een lus waar een blok aan twee kanten aan zijn buur grenst) had de
    /// automatische padzoekfunctie geen enkele voorkeur en geen manier om aan te geven
    /// welke bedoeld was - hij stopte gewoon zodra hij ÉÉN geldig pad vond, willekeurig
    /// welke. Is er voor dit blokpaar een Wisselstraat gedefinieerd (tot nu toe alleen
    /// gebruikt voor vaste routes), dan wordt die nu ALTIJD verkozen boven de automatische
    /// zoekfunctie - dat geeft volledige, expliciete controle. Geen Wisselstraat
    /// gedefinieerd? Dan verandert er niets: gewoon de bestaande zoekfunctie.</summary>
    private bool PadIsFysiekHaalbaar(Blok van, Blok naar)
    {
        var wisselstraat = _wisselstraatBeheerder.WisselstratenTussen(van, naar).FirstOrDefault();
        if (wisselstraat != null)
        {
            foreach (var wisselInfo in wisselstraat.Wissels)
            {
                var wisselCheck = ActueleWissel(wisselInfo.Wissel); // zie ActueleWissel - de wisselstraat-kopie kan verouderd zijn
                if (wisselCheck.IsDefect && wisselCheck.Stand != wisselInfo.GewensteStand) return false;
            }
            // GEVONDEN GAT (gebruikerswaarneming: "er komen lijntjes bij in plaats van dat de
            // wisseltongen paars worden" - de Wisselstraat-tool kende een Kruiswissel/Engelse
            // wissel niet als geldig doel): dezelfde "kan niet aangestuurd worden"-check als
            // bij een gewone wissel hierboven, maar dan via de motor-adressen (een
            // Kruiswissel heeft geen eigen IsDefect-vlag).
            foreach (var kruiswisselInfo in wisselstraat.Kruiswisselstanden)
            {
                var kruis = ActueleKruiswissel(kruiswisselInfo.Kruiswissel); // zie ActueleKruiswissel - de wisselstraat-kopie kan verouderd zijn
                if (!kruis.IsEngels) continue; // passief - kan altijd rechtdoor
                // Zie Model.Kruiswissel: Adres en Adres2 zijn onafhankelijke motoren, dus
                // apart vergelijken in plaats van via één gedeelde "stand van de kruiswissel".
                bool afwijking = kruis.StandAdres != kruiswisselInfo.StandAdres || kruis.StandAdres2 != kruiswisselInfo.StandAdres2;
                if (afwijking && kruis.Adres <= 0 && kruis.Adres2 <= 0)
                    return false;
            }
            return true;
        }

        var resultaat = _baanBeheerder.VindPadMetWisselstanden(van, naar);
        if (resultaat is null) return false; // geen pad gevonden - sowieso niet haalbaar
        foreach (var (wissel, benodigdeStand) in resultaat.Value.Wisselstanden)
        {
            if (wissel.IsDefect && wissel.Stand != benodigdeStand) return false;
        }
        foreach (var (kruiswissel, benodigdeStand) in resultaat.Value.Kruiswisselstanden)
        {
            // Een PASSIEVE kruiswissel (geen IsEngels) heeft geen stand om te "missen" -
            // Rechtdoor staat daar altijd al open, dus die blokkeert nooit een pad.
            if (kruiswissel.IsEngels && kruiswissel.Stand != benodigdeStand)
            {
                // Zelfde "kan niet aangestuurd worden"-redenering als bij een gewone
                // wissel hierboven, maar dan via de motor-adressen (een Kruiswissel heeft
                // geen eigen IsDefect-vlag - beide motoren zijn ofwel gekoppeld aan een
                // adres (bruikbaar) ofwel niet (Adres/Adres2 <= 0, nooit aan te sturen).
                if (kruiswissel.Adres <= 0 && kruiswissel.Adres2 <= 0) return false;
            }
        }
        return true;
    }

    /// <summary>ONTBRAK VOLLEDIG voor automatisch rijden (gebruikersmelding: "ik zie geen
    /// wissel-berichten") - in tegenstelling tot ZetWisselsVoorRoute hieronder (dat ALLEEN
    /// voor vaste routes werkt, en bovendien een geregistreerde Wisselstraat vereist), werkt
    /// dit voor ELKE trein-naar-trein-overgang, ook zonder Wisselstraten, via het
    /// ankerpuntennetwerk (BaanOntwerpBeheerder.VindPadMetWisselstanden - probeert BEIDE
    /// takken van elke wissel om te bepalen welke stand nodig is, in tegenstelling tot de
    /// kleuring die de HUIDIGE stand als gegeven aanneemt).</summary>
    /// <summary>Zet alle wissels op het pad tussen "van" en "naar" in de juiste stand.
    /// Stuurt ALTIJD een commando voor elke wissel op het pad, ook als de software zelf al
    /// denkt dat de wissel in de juiste stand staat (gebruikersverzoek: liever een keer te
    /// veel een commando dan een wissel die stiekem, buiten de software om, in de verkeerde
    /// stand terecht is gekomen). Geeft terug hoeveel wissels daadwerkelijk een commando
    /// kregen (0 als er geen wissels op het pad zaten, of geen pad gevonden werd) - de
    /// aanroeper gebruikt dit om te bepalen of er gewacht moet worden voordat de trein mag
    /// vertrekken (zie GEVONDEN GAT hieronder bij de aanroepplekken).</summary>
    private int ZetWisselsTussenBlokken(Blok van, Blok naar)
    {
        // Zie PadIsFysiekHaalbaar hierboven voor de volledige toelichting: een expliciet
        // gedefinieerde Wisselstraat voor dit blokpaar wordt ALTIJD verkozen boven de
        // automatische padzoekfunctie - dat geeft volledige controle bij meerdere fysiek
        // geldige paden tussen dezelfde twee blokken.
        var wisselstraat = _wisselstraatBeheerder.WisselstratenTussen(van, naar).FirstOrDefault();
        if (wisselstraat != null)
        {
            int viaWisselstraat = 0;
            foreach (var wisselInfo in wisselstraat.Wissels)
            {
                var wissel = ActueleWissel(wisselInfo.Wissel); // zie ActueleWissel - de wisselstraat-kopie kan verouderd zijn, dus NOOIT direct wisselInfo.Wissel bijwerken
                if (wissel.IsDefect) continue;
                // GEBRUIKERSCORRECTIE (veiligheid: "ik zie wissel 2 direct achter de loc
                // omschakelen, als er wagons achter hangen dan ontsporen die - dit hebben we
                // al eerder gehad"): hieronder stond voorheen ALTIJD een hardwarecommando,
                // ook als de wissel al precies in de gewenste stand stond (bewuste eerdere
                // keuze: "liever een keer te veel dan een stiekem verkeerd staande wissel").
                // Die aanname is fout gebleken: een wissel die al goed staat heeft HELEMAAL
                // GEEN commando nodig, en een motor die toch nog een keer aangestuurd wordt
                // kan de tong laten schokken/lopen terwijl een trein (met wagons) er nog
                // overheen rijdt - precies het "vroegtijdig alvast zetten"-scenario. Nu
                // ALLEEN nog een commando bij een ECHTE standwijziging - staat de wissel al
                // goed, dan wordt er niets verstuurd en kan er dus ook niets bewegen terwijl
                // er nog een trein overheen rijdt. Dit maakt het verbindings-tijd
                // initialiseren (dat de software-Stand met de fysieke stand synchroniseert)
                // des te belangrijker: alleen dan is deze vergelijking betrouwbaar.
                bool moetSchakelen = wissel.Stand != wisselInfo.GewensteStand;
                wissel.Stand = wisselInfo.GewensteStand;
                if (wissel.GekoppeldeOverloopwissel != null) wissel.GekoppeldeOverloopwissel.Stand = wisselInfo.GewensteStand;
                if (!moetSchakelen) continue;
                bool afbuigendGewenst = wisselInfo.GewensteStand == WisselStand.Afbuigend;
                // Zie Model.Wissel.OmgekeerdePolariteit - zelfde principe als bij
                // Kruiswissel.Adres2OmgekeerdePolariteit hieronder, maar dan voor een gewone
                // wissel met precies deze ene, fysiek omgekeerd bedrade motor.
                _hardwareBeheerder.StuurWisselCommando(wissel.Adres, wissel.OmgekeerdePolariteit ? !afbuigendGewenst : afbuigendGewenst);
                viaWisselstraat++;
            }
            // GEBRUIKERSCORRECTIE, FUNDAMENTEEL (zie Model.Kruiswissel voor de volledige,
            // fysiek bevestigde toelichting): Adres en Adres2 zijn twee VOLLEDIG onafhankelijke
            // motoren met elk hun eigen, per rijweg vastgelegde stand (StandAdres/StandAdres2
            // op deze Wisselstraat) - geen gedeelde "stand van de kruiswissel" en geen vaste
            // (gelijk of tegengesteld) relatie meer tussen de twee motoren. Elke motor apart
            // vergelijken en apart aansturen, precies als twee gewone wissels.
            foreach (var kruiswisselInfo in wisselstraat.Kruiswisselstanden)
            {
                var kruis = ActueleKruiswissel(kruiswisselInfo.Kruiswissel);
                if (!kruis.IsEngels) continue; // passief - geen adres, niets te sturen
                if (kruis.Adres > 0 && kruis.StandAdres != kruiswisselInfo.StandAdres)
                {
                    kruis.StandAdres = kruiswisselInfo.StandAdres;
                    _hardwareBeheerder.StuurWisselCommando(kruis.Adres, kruiswisselInfo.StandAdres == WisselStand.Afbuigend);
                    viaWisselstraat++;
                }
                if (kruis.Adres2 > 0 && kruis.StandAdres2 != kruiswisselInfo.StandAdres2)
                {
                    kruis.StandAdres2 = kruiswisselInfo.StandAdres2;
                    _hardwareBeheerder.StuurWisselCommando(kruis.Adres2, kruiswisselInfo.StandAdres2 == WisselStand.Afbuigend);
                    viaWisselstraat++;
                }
                // Stand blijft alleen bijgewerkt als eenvoudige weergave-indicatie (zie
                // Model.Kruiswissel.Stand) - "Overstap" als minstens één motor afbuigend
                // staat, puur voor het icoontje in het baanontwerp.
                kruis.Stand = (kruis.StandAdres == WisselStand.Afbuigend || kruis.StandAdres2 == WisselStand.Afbuigend)
                    ? KruiswisselStand.Overstap : KruiswisselStand.Rechtdoor;
            }
            return viaWisselstraat;
        }

        var resultaat = _baanBeheerder.VindPadMetWisselstanden(van, naar);
        if (resultaat is null) return 0; // geen ankerpuntenpad gevonden - niets te zetten
        // GEBRUIKERSVERZOEK (diagnose: "ik heb nog niet gezien dat hij bij wissel 14 de
        // korte route nam" - blok4/blok5 en vergelijkbare overgangen hebben GEEN expliciete
        // Wisselstraat, dus lopen altijd via deze automatische padzoekfunctie, die van meerdere
        // fysiek bestaande paden altijd hetzelfde ene pad teruggeeft - welk pad dat precies is,
        // was tot nu toe alleen achteraf, indirect af te leiden uit de hardwarecommando's zelf.
        // Deze regel maakt dat direct zichtbaar in het programmalog, per overgang.
        Log($"[Wisselzoeker] Blok {van.Nummer} -> blok {naar.Nummer}: automatisch pad gevonden via {resultaat.Value.Lijnen.Count} lijnstuk(ken), wissels [{string.Join(", ", resultaat.Value.Wisselstanden.Select(w => $"{w.Wissel.Adres}={w.BenodigdeStand}"))}], kruiswissels [{string.Join(", ", resultaat.Value.Kruiswisselstanden.Select(k => $"{k.Kruiswissel.Adres}/{k.Kruiswissel.Adres2}={k.BenodigdeStand}"))}].");
        int aantalGezet = 0;
        foreach (var (wissel, benodigdeStand) in resultaat.Value.Wisselstanden)
        {
            if (wissel.IsDefect) continue;
            // Zie de toelichting hierboven bij de wisselstraat-tak: alleen sturen bij een
            // ECHTE standwijziging, nooit "voor de zekerheid" een wissel opnieuw aansturen
            // die al goed staat - dat kan een trein die er nog overheen rijdt raken.
            bool moetSchakelen = wissel.Stand != benodigdeStand;
            wissel.Stand = benodigdeStand;
            if (wissel.GekoppeldeOverloopwissel != null) wissel.GekoppeldeOverloopwissel.Stand = benodigdeStand;
            if (!moetSchakelen) continue;
            bool afbuigendGewenst = benodigdeStand == WisselStand.Afbuigend;
            _hardwareBeheerder.StuurWisselCommando(wissel.Adres, wissel.OmgekeerdePolariteit ? !afbuigendGewenst : afbuigendGewenst);
            aantalGezet++;
        }
        aantalGezet += ZetKruiswisselstanden(resultaat.Value.Kruiswisselstanden);
        return aantalGezet;
    }

    /// <summary>GEBRUIKERSCORRECTIE, FUNDAMENTEEL (zie Model.Kruiswissel voor de volledige,
    /// fysiek bevestigde toelichting): een Engelse wissel heeft GEEN "stand van de hele
    /// kruiswissel" die beide motoren gelijk (of vast tegengesteld) aanstuurt - elke rijweg
    /// heeft zijn eigen, soms gemengde combinatie van de twee motoren nodig, en die is NIET
    /// uit de geometrie van de automatische padzoekfunctie af te leiden (die geeft hier maar
    /// één gedeelde KruiswisselStand terug). Vroeger gokte deze methode dan ook simpelweg
    /// "beide motoren dezelfde/tegengestelde stand" - dat is precies waarom motor 10
    /// (Adres2) hier telkens weer verkeerd kwam te staan op elk pad zonder expliciete
    /// Wisselstraat. In plaats van te blijven gokken: een ECHTE Engelse wissel wordt hier nu
    /// NOOIT meer automatisch/geraden gezet. Elke rijweg door een Engelse wissel moet een
    /// expliciete Wisselstraat hebben (met StandAdres/StandAdres2 apart vastgelegd, zie
    /// Model.KruiswisselInWisselstraat) - ontbreekt die, dan wordt dat hier duidelijk gelogd
    /// in plaats van er blind een mogelijk verkeerde stand overheen te sturen. Een PASSIEVE
    /// kruiswissel (IsEngels=false) heeft geen adres en blijft hier gewoon overgeslagen.</summary>
    private int ZetKruiswisselstanden(List<(Kruiswissel Kruiswissel, KruiswisselStand BenodigdeStand)> kruiswisselstanden)
    {
        foreach (var (kruiswissel, _) in kruiswisselstanden)
        {
            if (!kruiswissel.IsEngels) continue; // passieve kruising - geen adres, niets te sturen
            Log($"WAARSCHUWING: Engelse wissel {kruiswissel.Adres}/{kruiswissel.Adres2} ligt op een automatisch gevonden pad zonder expliciete Wisselstraat - NIET gezet (de twee motoren hebben een per-rijweg-specifieke stand nodig die niet automatisch af te leiden is). Leg voor deze overgang een Wisselstraat aan met de juiste stand voor beide motoren.");
        }
        return 0;
    }

    /// <summary>Controleert of het VERLATEN van "huidig" een fysieke richtingsomkering
    /// vereist - een kopspoor (per definitie een doodlopend spoor waar je moet keren), of
    /// de relatie waarmee de trein HIER is AANGEKOMEN was gemarkeerd met Keer
    /// (BlokRelatie.Keer, "uit-via-naar"-principe). EERDERE BEPERKING (gebruikerscorrectie:
    /// "de loc wordt niet gekeerd"): de rijrichting werd nergens bijgehouden, dus stuurde
    /// het programma bij elk snelheidscommando altijd "vooruit". Zo ja: stuurt eerst een
    /// stopcommando in de HUIDIGE richting (een echte loc kan niet instant omkeren terwijl
    /// hij nog rijdt), draait dan RijdtVooruit om - de aanroeper stuurt daarna zelf de
    /// nieuwe-richting-vertreksnelheid via de bestaande LogSnelheidsopbouw(optrekken:true).</summary>
    /// <summary>Bepaalt of het bereiken van "naar" (vanaf "van") een fysieke
    /// richtingsomkering vereist - een kopspoor (per definitie doodlopend), OF een
    /// Keer-gemarkeerde relatie (BlokRelatie.Keer, "uit-via-naar"-principe) tussen die
    /// twee blokken, ook als "naar" zelf een GEWOON, niet-doodlopend blok is. Gedeelde
    /// bron van waarheid, gebruikt door zowel VerwerkEventueelKeren (het daadwerkelijke
    /// omkeren) als de aankomst-afremlogica hieronder (vaste + automatisch) - EERDER
    /// keek de aankomst-afremlogica ALLEEN naar BlokType.Kopspoor, niet naar de
    /// Keer-relatie-variant, wat dezelfde soort inconsistentie gaf als bij de kopspoor-fix
    /// hiervoor: een gewoon blok met een Keer-relatie kreeg geen nette afremming bij
    /// aankomst, alleen een instant stop (via VerwerkEventueelKeren) vlak vóór vertrek.</summary>
    private bool VereistKeren(Blok? van, Blok naar)
    {
        var relatie = van != null ? _blokBeheerder.Relaties.FirstOrDefault(r => r.Van == van && r.Naar == naar) : null;
        return naar.Type == BlokType.Kopspoor || relatie?.Keer == true;
    }

    /// <summary>Kan "blok" via het "niet-keer-netwerk" (alle overgangen waarvoor
    /// VereistKeren false is) uiteindelijk weer bij ZICHZELF uitkomen? Zo ja, zit het op
    /// een cyclus - de trein kan daar dus voor onbepaalde tijd rondjes blijven rijden
    /// zonder ooit te hoeven keren. Gebruikt door KanVeiligBlijvenRijden hieronder.</summary>
    private bool BlokKanZichzelfBereiken(Blok blok)
    {
        var bezocht = new HashSet<Blok>();
        var stapel = new Stack<Blok>(_blokBeheerder.VolgendeBlokken(blok).Where(b => !VereistKeren(blok, b)));
        while (stapel.Count > 0)
        {
            var huidig = stapel.Pop();
            if (huidig == blok) return true;
            if (!bezocht.Add(huidig)) continue;
            foreach (var volgende in _blokBeheerder.VolgendeBlokken(huidig).Where(b => !VereistKeren(huidig, b)))
                stapel.Push(volgende);
        }
        return false;
    }

    /// <summary>Voor Trein.MagNietKeren: kan de trein, startend bij "start", voor
    /// onbepaalde tijd blijven doorrijden zonder OOIT gedwongen te worden te keren? Dit is
    /// zo als "start" zelf op een cyclus zit (BlokKanZichzelfBereiken), OF als "start" via
    /// het niet-keer-netwerk een ANDER blok kan bereiken dat wél op zo'n cyclus zit -
    /// puur op de STATISCHE blok-relatie-topologie, ONAFHANKELIJK van de actuele bezet/
    /// gereserveerd-status van andere treinen (die verandert voortdurend, dus een
    /// "veilig"-oordeel gebaseerd daarop zou toch meteen weer kunnen verouderen - de vaste
    /// spooraanleg zelf verandert niet).</summary>
    private bool KanVeiligBlijvenRijden(Blok start)
    {
        if (BlokKanZichzelfBereiken(start)) return true;
        var bezocht = new HashSet<Blok> { start };
        var stapel = new Stack<Blok>(new[] { start });
        while (stapel.Count > 0)
        {
            var huidig = stapel.Pop();
            foreach (var volgende in _blokBeheerder.VolgendeBlokken(huidig).Where(b => !VereistKeren(huidig, b)))
            {
                if (!bezocht.Add(volgende)) continue;
                if (BlokKanZichzelfBereiken(volgende)) return true;
                stapel.Push(volgende);
            }
        }
        return false;
    }

    private void VerwerkEventueelKeren(RijdendeTrein trein, Blok? vorigBlok, Blok huidig)
    {
        // GEVONDEN, KRITIEKE BUG (gebruikersmelding: "loc reed bij starten direct tegen
        // stootjuk" - een VERSE rit die start OP een kopspoor keerde daar al bij de
        // ALLEREERSTE stap, nog vóórdat de trein ooit bewogen had): VereistKeren(vorigBlok,
        // huidig) geeft TRUE zodra huidig.Type==Kopspoor, ONGEACHT vorigBlok - dus ook bij
        // vorigBlok==null (de eerste stap van een verse rit, waar nog geen "aankomst die om
        // een omkering vraagt" heeft plaatsgevonden). Keren is alleen zinvol als de trein
        // ECHT ergens vandaan kwam en nu de andere kant op moet; bij de allereerste stap is
        // er geen eerdere rijrichting om te "corrigeren" - de rijrichting waarmee de
        // trein/route al begint (Trein.OmgekeerdeRijrichting, of gewoon de standaard) blijft
        // dan gewoon staan, en de trein vertrekt in de richting die de baan daadwerkelijk
        // vrijlaat (weg van het stootblok), niet ernaartoe.
        if (vorigBlok is null) return;
        if (!VereistKeren(vorigBlok, huidig)) return;

        // BUGFIX (gebruikersmelding: log toont stap 0 maar de loc bewoog toch): dit
        // stopcommando ging voorheen RECHTSTREEKS naar de hardware, ZONDER een eventueel
        // AL LOPENDE ramp (bijv. een net gestart optrekken) af te breken - die bleef dan
        // onafhankelijk doortikken en stuurde zijn EIGEN, steeds hogere stappen, TERWIJL dit
        // stopcommando ertussendoor 0 stuurde. Twee gelijktijdig actieve, elkaar
        // tegensprekende commandoreeksen - de loc luisterde naar wat toevallig het laatst
        // binnenkwam, vandaar het verschil tussen log en waargenomen gedrag. Nu wordt de
        // lopende ramp EERST netjes afgebroken en de bijgehouden stap teruggezet, zodat een
        // volgende ramp (het optrekken in de nieuwe richting, direct hierna door de
        // aanroeper) altijd vanaf een correcte, consistente 0 start. In de praktijk is dit
        // inmiddels vooral een bevestigend vangnet: de daadwerkelijke afremming gebeurt al
        // bij aankomst (zie hieronder), dus de loc staat hier meestal al op 0.
        trein.ActieveSnelheidsRampTimer?.Stop();
        trein.ActieveSnelheidsRampTimer = null;
        if (trein.Trein != null && trein.Trein.DecoderAdres > 0)
        {
            bool vooruitVoorHardware = trein.Trein.OmgekeerdeRijrichting ? !trein.RijdtVooruit : trein.RijdtVooruit; // zie Trein.OmgekeerdeRijrichting
            int blokVoorStop = trein.HuidigBlok?.Nummer ?? 0;
            _hardwareBeheerder.StuurLocSnelheidCommando(trein.Trein.DecoderAdres, 0, vooruitVoorHardware, blokVoorStop, trein.Trein.DecoderStappen);
            trein.LaatstGebruikteBlokVoorSnelheid = blokVoorStop; // zie RijdendeTrein.LaatstGebruikteBlokVoorSnelheid
        }
        trein.HuidigeSnelheidStap = 0;

        trein.RijdtVooruit = !trein.RijdtVooruit;
        Log($"[Route '{trein.Route.Omschrijving}'] Loc '{trein.Trein?.Omschrijving}' keert bij blok {huidig.Nummer} - rijdt vanaf nu {(trein.RijdtVooruit ? "vooruit" : "achteruit")}.");
    }

    /// <summary>Zet alle wisselstraten voor het HELE pad van deze route in één keer, bij het
    /// vertrek. Geeft het TOTAAL aantal daadwerkelijk gezette wissels terug (0 = geen
    /// wisselstraat op dit pad, of niets te zetten) - de aanroeper gebruikt dit om te bepalen
    /// of er, voordat de trein daadwerkelijk vertrekt, eerst gewacht moet worden tot de
    /// wissels fysiek zijn omgelopen (zie de aanroepplek voor de volledige toelichting).</summary>
    private int ZetWisselsVoorRoute(List<Blok> pad, Treinroute route)
    {
        int totaalGezet = 0;
        for (int i = 0; i < pad.Count - 1; i++)
        {
            var wisselstraat = _wisselstraatBeheerder.WisselstratenTussen(pad[i], pad[i + 1]).FirstOrDefault();
            if (wisselstraat is null || (wisselstraat.Wissels.Count == 0 && wisselstraat.Kruiswisselstanden.Count == 0)) continue;

            // ActueleWissel: zie de toelichting bij die methode - vergelijk/werk NOOIT de
            // (mogelijk verouderde) wisselstraat-kopie zelf bij, altijd de levende Wissel uit
            // Symbolen. Ook UitgeslotenWissels vergelijkt daarom op Adres i.p.v. op
            // objectgelijkheid, want route.UitgeslotenWissels bevat wél de echte Symbolen-
            // instantie (via het bouwscherm gekozen), terwijl w.Wissel hier de - mogelijk
            // andere - wisselstraat-kopie is.
            var uitgesloten = wisselstraat.Wissels.Where(w => route.UitgeslotenWissels.Any(u => u.Adres == w.Wissel.Adres)).ToList();
            var defect = wisselstraat.Wissels.Where(w => ActueleWissel(w.Wissel).IsDefect).ToList();
            var teZetten = wisselstraat.Wissels.Except(uitgesloten).Except(defect).ToList();

            // GEBRUIKERSCORRECTIE (veiligheid, zie ZetWisselsTussenBlokken voor de volledige
            // toelichting): alleen daadwerkelijk aansturen bij een ECHTE standwijziging -
            // een wissel die al goed staat blijft met rust, ook al is dit een "vaste route"
            // die het pad altijd opnieuw instelt bij vertrek.
            var werkelijkGeschakeld = new List<WisselInWisselstraat>();
            foreach (var wisselInfo in teZetten)
            {
                var wissel = ActueleWissel(wisselInfo.Wissel);
                bool moetSchakelen = wissel.Stand != wisselInfo.GewensteStand;
                wissel.Stand = wisselInfo.GewensteStand;
                // Overloopwissel: de gekoppelde partner altijd meebewegen, ook tijdens
                // automatisch ingestelde wisselstraten - net als een echte fysieke koppeling.
                if (wissel.GekoppeldeOverloopwissel != null)
                    wissel.GekoppeldeOverloopwissel.Stand = wisselInfo.GewensteStand;
                if (!moetSchakelen) continue;
                bool afbuigendGewenst = wisselInfo.GewensteStand == WisselStand.Afbuigend;
                _hardwareBeheerder.StuurWisselCommando(wissel.Adres, wissel.OmgekeerdePolariteit ? !afbuigendGewenst : afbuigendGewenst);
                totaalGezet++;
                werkelijkGeschakeld.Add(wisselInfo);
            }

            if (werkelijkGeschakeld.Count > 0)
                Log($"Wisselstraat {wisselstraat} ingesteld: " +
                    string.Join(", ", werkelijkGeschakeld.Select(w => $"wissel {w.Wissel.Adres} -> {w.GewensteStand}")) + ".");
            if (uitgesloten.Count > 0)
                Log($"Wisselstraat {wisselstraat}: wissel(s) {string.Join(", ", uitgesloten.Select(w => w.Wissel.Adres))} bewust niet omgezet (uitgesloten voor deze route).");

            // GEVONDEN GAT (gebruikerswaarneming: "er komen lijntjes bij in plaats van dat de
            // wisseltongen paars worden" - een Kruiswissel/Engelse wissel kon nog niet in een
            // Wisselstraat worden opgenomen, ook niet voor vaste routes): zelfde aansturing
            // als bij vrij automatisch rijden (ZetWisselsTussenBlokken) - beide motoren
            // (Adres en Adres2) samen, in dezelfde stand. Geen "uitgesloten"-concept hier
            // (dat bestaat alleen voor gewone Wissel via route.UitgeslotenWissels) - een
            // Kruiswissel in de wisselstraat wordt dus altijd gezet, tenzij hij passief is
            // (geen adres nodig) of geen van beide motoren een adres heeft. Ook hier: alleen
            // bij een ECHTE standwijziging.
            // Zie ZetWisselsTussenBlokken (dezelfde toelichting): Adres en Adres2 zijn
            // onafhankelijke motoren, elk met hun eigen, in deze Wisselstraat vastgelegde
            // stand - geen gedeelde/afgeleide kruiswissel-stand meer.
            foreach (var kruiswisselInfo in wisselstraat.Kruiswisselstanden)
            {
                var kruis = ActueleKruiswissel(kruiswisselInfo.Kruiswissel);
                if (!kruis.IsEngels) continue;
                int kruiswisselGezet = 0;
                if (kruis.Adres > 0 && kruis.StandAdres != kruiswisselInfo.StandAdres)
                {
                    kruis.StandAdres = kruiswisselInfo.StandAdres;
                    _hardwareBeheerder.StuurWisselCommando(kruis.Adres, kruiswisselInfo.StandAdres == WisselStand.Afbuigend);
                    kruiswisselGezet++;
                }
                if (kruis.Adres2 > 0 && kruis.StandAdres2 != kruiswisselInfo.StandAdres2)
                {
                    kruis.StandAdres2 = kruiswisselInfo.StandAdres2;
                    _hardwareBeheerder.StuurWisselCommando(kruis.Adres2, kruiswisselInfo.StandAdres2 == WisselStand.Afbuigend);
                    kruiswisselGezet++;
                }
                kruis.Stand = (kruis.StandAdres == WisselStand.Afbuigend || kruis.StandAdres2 == WisselStand.Afbuigend)
                    ? KruiswisselStand.Overstap : KruiswisselStand.Rechtdoor;
                if (kruiswisselGezet > 0)
                {
                    totaalGezet += kruiswisselGezet;
                    Log($"Wisselstraat {wisselstraat} ingesteld: kruiswissel {kruis.Adres}={kruiswisselInfo.StandAdres}/{kruis.Adres2}={kruiswisselInfo.StandAdres2}.");
                }
            }
        }
        return totaalGezet;
    }

    private const double StationInstaptijd = 2.5;

    /// <summary>Eén gedeelde generator voor de stopkans-op-een-gewoon-blok-functie - static
    /// omdat BouwGebeurtenissen zelf ook static is (geen aparte state per instantie nodig,
    /// en een nieuwe Random() per aanroep zou bij snel-achter-elkaar-aanroepen dezelfde seed
    /// kunnen krijgen).</summary>
    private static readonly Random _stopkansGenerator = new();

    /// <summary>Referentiesnelheid (km/u) waarop de standaardtijden (1.2s/1.0s/1.5s) gebaseerd
    /// zijn - een treintype dat sneller rijdt dan dit krijgt kortere reistijden tussen
    /// meldpunten, een langzamer type (bijv. Rangeren) juist langere.</summary>
    private const double ReferentieSnelheid = 70;

    /// <summary>"Dynamische lengte" zoals het echte Koploper dat kent (vooral bij
    /// schaduwstations): kiest bij MEERDERE stopsecties in hetzelfde blok de KRAPST
    /// passende voor deze treinlengte, zodat een korte trein eerder stopt en een lange
    /// trein zo ver mogelijk het blok in rijdt - i.p.v. altijd blindelings de eerste
    /// stopsectie te pakken. Een stopsectie met MaxTreinlengte=0 past altijd (geen limiet).
    /// Zonder bekende treinlengte (treinLengte&lt;=0) of zonder enige geschikte stopsectie:
    /// terugval op de eerste stopsectie in het blok, zoals voorheen.</summary>
    /// <summary>"Optimale lengte" zoals het echte Koploper bij automatisch rijden met
    /// meerdere mogelijke bestemmingsblokken (bijv. een schaduwstation met parallelle
    /// opstelsporen): niet zomaar het eerste vrije blok pakken, maar het blok waarvan de
    /// stopsectie het KRAPST bij de treinlengte past - zo blijft er voor een korte trein
    /// een langer opstelspoor over voor een latere lange trein, in plaats van willekeurig
    /// het eerste (misschien te lange, dus verspillende) spoor te bezetten. Gebruikt
    /// dezelfde KiesStopsectie-logica als de wachttijd-berekening, nu toegepast om tussen
    /// KANDIDAAT-BLOKKEN te kiezen i.p.v. tussen stopsecties binnen één blok. Zonder
    /// bekende treinlengte, of als geen van de kandidaten een MaxTreinlengte heeft
    /// ingesteld: gewoon het eerste kandidaat-blok, zoals voorheen.
    /// Koploper (schaduwstation-voorbeeld): "moet er twee blokken vooruit worden
    /// gekeken" - bij een splitsing waar de kandidaten zelf nog GEEN lengte-beperkte
    /// opstelsporen zijn (bijv. een tussenliggend wisselstraat-blok vóór de daadwerkelijke,
    /// parallelle opstelsporen), wordt daarom ook een stap verder gekeken: welke kandidaat
    /// leidt naar het best passende blok twee stappen vooruit?</summary>
    private Blok KiesOptimaleKandidaat(Blok huidig, List<Blok> kandidaten, double treinLengte)
    {
        if (kandidaten.Count <= 1) return kandidaten[0];

        if (treinLengte > 0)
        {
            var metPassendeStopsectie = kandidaten
                .Select(b => (Blok: b, Stopsectie: KiesStopsectie(b, treinLengte)))
                .Where(x => x.Stopsectie != null && x.Stopsectie.MaxTreinlengte > 0 && x.Stopsectie.MaxTreinlengte >= treinLengte)
                .OrderBy(x => x.Stopsectie!.MaxTreinlengte)
                .ToList();
            if (metPassendeStopsectie.Count > 0) return metPassendeStopsectie[0].Blok;

            var metTweeStappenVooruit = kandidaten
                .Select(b => (Blok: b, BesteVerderop: _blokBeheerder.VolgendeBlokken(b)
                    .Select(b2 => KiesStopsectie(b2, treinLengte))
                    .Where(s => s != null && s.MaxTreinlengte > 0 && s.MaxTreinlengte >= treinLengte)
                    .OrderBy(s => s!.MaxTreinlengte)
                    .FirstOrDefault()))
                .Where(x => x.BesteVerderop != null)
                .OrderBy(x => x.BesteVerderop!.MaxTreinlengte)
                .ToList();
            if (metTweeStappenVooruit.Count > 0) return metTweeStappenVooruit[0].Blok;
        }

        // GEEN van de kandidaten heeft een aantoonbare voorkeur (geen lengte-beperking die
        // hier een rol speelt, of de treinlengte is onbekend) - "gooi de dobbelsteen"
        // (gebruikersverzoek: "mag hij rechtdoor en mag hij linksaf dan moet hij de bekende
        // dobbelsteen gooien"): willekeurig kiezen tussen de gelijkwaardige opties, i.p.v.
        // altijd domweg de eerste - anders zou een trein bij elke herhaling exact dezelfde
        // route blijven kiezen. De kandidatenlijst is op dit punt al gefilterd op alles wat
        // een ECHTE beperking is (bezet/gereserveerd, richtingsverbod, stopverbod,
        // treinlengte, en - bij MagNietKeren - geen doodlopende route, zie
        // AutomatischeStapProberen/KanVeiligBlijvenRijden) - wat hier overblijft is dus
        // per definitie een vrije keuze.
        //
        // GEWOGEN dobbelsteen (Koploper's "Kans"-kolom, Richtingen-tabblad, zie
        // Model.BlokRelatie.Kans): een relatie zonder expliciet vastgelegd gewicht (of
        // helemaal geen BlokRelatie-object voor dit Van/Naar-paar) telt als gewicht 1 -
        // dus zonder ooit een Kans in te stellen blijft dit exact de oude, gelijke-kansen-
        // dobbelsteen van hierboven.
        var gewichten = kandidaten.Select(k => Math.Max(1, _blokBeheerder.Relaties.FirstOrDefault(r => r.Van == huidig && r.Naar == k)?.Kans ?? 1)).ToList();
        int totaalGewicht = gewichten.Sum();
        int worp = _stopkansGenerator.Next(totaalGewicht);
        int lopendeSom = 0;
        for (int i = 0; i < kandidaten.Count; i++)
        {
            lopendeSom += gewichten[i];
            if (worp < lopendeSom) return kandidaten[i];
        }
        return kandidaten[^1]; // theoretisch onbereikbaar (het bereik van worp dekt altijd totaalGewicht), veilige terugval
    }

    private static Bezetmeldpunt? KiesStopsectie(Blok blok, double treinLengte)
    {
        var stopsecties = blok.Bezetmeldpunten.Where(m => m.Rol == BezetmeldpuntRol.Stopsectie).ToList();
        if (stopsecties.Count <= 1 || treinLengte <= 0) return stopsecties.FirstOrDefault();

        var passende = stopsecties
            .Where(m => m.MaxTreinlengte <= 0 || m.MaxTreinlengte >= treinLengte)
            .OrderBy(m => m.MaxTreinlengte <= 0 ? double.MaxValue : m.MaxTreinlengte)
            .FirstOrDefault();
        return passende ?? stopsecties.OrderByDescending(m => m.MaxTreinlengte).First(); // trein te lang voor alles: de ruimste nemen
    }

    /// <summary>Zoekt, voor een gegeven meldernummer, het bijbehorende Bezetmeldpunt-object
    /// op het blok (voor MagVorigBlokVrijgeven e.d.) - of maakt een simpel, standaard
    /// object aan (MagVorigBlokVrijgeven=true, de standaardwaarde) als dat meldernummer
    /// nergens los als Bezetmeldpunt gedefinieerd is. Gebruikt door BepaalAankomstmelder/
    /// BepaalStopmelder hieronder om een Blok.RichtingsBezetmeldingen-entry (een kaal
    /// meldernummer) te vertalen naar hetzelfde Bezetmeldpunt-type dat de rest van de
    /// simulatie-engine al overal verwacht.</summary>
    private static Bezetmeldpunt GeefBezetmeldpuntVoorMelder(Blok blok, int meldernummer, BezetmeldpuntRol terugvalRol) =>
        blok.Bezetmeldpunten.FirstOrDefault(m => m.MeldernNummer == meldernummer)
        ?? new Bezetmeldpunt { MeldernNummer = meldernummer, Rol = terugvalRol };

    /// <summary>Bepaalt het "aankomst"-meldpunt voor het bereiken van "naar" vanuit "van" -
    /// zie Model.RichtingsBezetmelding voor de volledige achtergrond (gebaseerd op
    /// Koploper's "Bezetmeldingen"-tabblad, richtingsafhankelijke bezetmelder-volgorde).
    /// Geeft voorrang aan een richtingsspecifieke configuratie (EERSTE meldernummer in de
    /// "Uit blok"-lijst) en valt terug op het generieke Bezetmeldpunt met Rol=Voorblok als
    /// er voor DEZE SPECIFIEKE aankomstrichting niets is ingesteld.</summary>
    private static Bezetmeldpunt? BepaalAankomstmelder(Blok naar, Blok? van)
    {
        var richting = van != null ? naar.RichtingsBezetmeldingen.FirstOrDefault(r => r.VanBlok == van) : null;
        if (richting != null && richting.MeldernNummers.Count > 0)
            return GeefBezetmeldpuntVoorMelder(naar, richting.MeldernNummers[0], BezetmeldpuntRol.Voorblok);
        return naar.Bezetmeldpunten.FirstOrDefault(m => m.Rol == BezetmeldpuntRol.Voorblok);
    }

    /// <summary>Zusje van BepaalAankomstmelder hierboven, maar dan voor het "moet stoppen"-
    /// signaal: LAATSTE meldernummer in de richtingsspecifieke lijst, of (terugval) de
    /// generieke, lengte-bewuste KiesStopsectie hierboven.</summary>
    private static Bezetmeldpunt? BepaalStopmelder(Blok naar, Blok? van, double treinLengte)
    {
        var richting = van != null ? naar.RichtingsBezetmeldingen.FirstOrDefault(r => r.VanBlok == van) : null;
        if (richting != null && richting.MeldernNummers.Count > 0)
            return GeefBezetmeldpuntVoorMelder(naar, richting.MeldernNummers[^1], BezetmeldpuntRol.Stopsectie);
        return KiesStopsectie(naar, treinLengte);
    }

    /// <summary>Voor StartStaartFase: welke melder moet een ECHTE vrijmelding geven om te
    /// bevestigen dat een net-verlaten blok daadwerkelijk leeg is? Iets RUIMER dan
    /// BepaalStopmelder hierboven (dat specifiek is voor "waar moet de trein stoppen bij
    /// AANKOMST", een ander doel): valt, als er geen Stopsectie geconfigureerd is, ALSNOG
    /// terug op de Voorblok-melder van het blok zelf.
    ///
    /// GEVONDEN GAT (bytes-analyse: een spookmelding 4,8 sec na vertrek - te snel voor de
    /// 30-sec-veiligheidstimeout, dus die was helemaal niet aangesproken): bij de EERSTE
    /// stap van een verse rit is er nog geen "vorig blok" bekend (trein.VorigBlok is dan
    /// null), dus de richtingsspecifieke opzoeking valt terug op KiesStopsectie - en een
    /// blok dat ALLEEN een Voorblok-melder heeft (geen apart Stopsectie-meldpunt, heel
    /// gangbaar bij een simpel blok met maar één sensor) levert daar dan NIETS op.
    /// StartStaartFase viel zo stilzwijgend terug op de oude, vaste 0.8-sec-timer - exact
    /// dezelfde bug als eerder, alleen dan via een andere weg terug binnengeslopen. Voor
    /// zo'n blok is de Voorblok-melder de ENIGE beschikbare sensor, dus die is hier een
    /// zinvolle laatste terugval in plaats van helemaal niets.</summary>
    private static Bezetmeldpunt? BepaalVertrekmelder(Blok verlatenBlok, Blok? vanBlok, double treinLengte) =>
        BepaalStopmelder(verlatenBlok, vanBlok, treinLengte) ?? verlatenBlok.Bezetmeldpunten.FirstOrDefault(m => m.Rol == BezetmeldpuntRol.Voorblok);

    /// <summary>Gebruikersverzoek: "is het een idee om de bezetmelders en volgorde
    /// zelflerend te maken... zodat ik dat niet allemaal handmatig in hoef te vullen" -
    /// bouwt/verfijnt automatisch een RichtingsBezetmelding voor (van, naar) op basis van
    /// wat er ECHT, in de juiste volgorde, gemeld is tijdens DEZE specifieke overgang (zie
    /// RijdendeTrein.GeobserveerdeMeldersTijdensWachten).
    ///
    /// Twee bewuste grenzen:
    /// - Overschrijft NOOIT een handmatig ingevoerde entry (RichtingsBezetmelding.
    ///   AutomatischGeleerd==false) - de software respecteert dan altijd jouw eigen,
    ///   bewuste configuratie. Alleen aanmaken als er nog niets voor deze richting stond,
    ///   of verfijnen als de bestaande entry zelf ook al automatisch geleerd was.
    /// - "Laatste observatie wint", geen voortschrijdend gemiddelde zoals bij de reistijd
    ///   zelf - een volgorde van meldernummers laat zich niet zinvol middelen, en in de
    ///   praktijk verandert de fysieke sensorvolgorde toch niet vanzelf.</summary>
    private void LeerBezetmelderVolgorde(Blok naar, Blok? van, List<int> waargenomenVolgorde)
    {
        if (van is null || waargenomenVolgorde.Count == 0) return;
        var bestaand = naar.RichtingsBezetmeldingen.FirstOrDefault(r => r.VanBlok == van);
        if (bestaand != null && !bestaand.AutomatischGeleerd) return; // respecteer een handmatige configuratie

        if (bestaand != null)
        {
            if (bestaand.MeldernNummers.SequenceEqual(waargenomenVolgorde)) return; // ongewijzigd

            // GEVONDEN, ECHTE REGRESSIE (gebruikerswaarneming: een zorgvuldig herstelde
            // lijst van 3 meldpunten werd na de eerstvolgende geslaagde rit alweer
            // teruggebracht naar 1): het waarnemingsvenster (GeobserveerdeMeldersTijdensWachten)
            // sluit al zodra de AANKOMSTmelder bevestigt - bij een blok dat moet keren,
            // vuren de LATERE meldpunten (het echte uiteinde, nodig voor BepaalStopmelder)
            // pas TIJDENS de aparte "wacht op stopmelder"-fase daarna, dus die worden voor
            // DEZE specifieke waarneming nooit meegenomen. Zonder deze check zou de
            // zelflerende functie zo een volledige, drie-melders-lange lijst steeds weer
            // terugsnoeien naar een onvolledige lijst van 1 - en daarmee de aparte "wacht
            // écht op het uiteinde voordat er gekeerd wordt"-logica alsnog buitenspel
            // zetten (BepaalStopmelder pakt dan immers dezelfde ene melder als aankomst).
            // Een nieuwe waarneming mag de lijst dus nooit KORTER maken dan wat er al
            // stond - alleen even lang of langer (bijv. omdat de eerdere lijst zelf ook
            // onvolledig was en deze keer wél alles gevangen is).
            if (waargenomenVolgorde.Count < bestaand.MeldernNummers.Count) return;

            Log($"Bezetmelder-volgorde voor blok {naar.Nummer} (vanuit blok {van.Nummer}) automatisch bijgewerkt: {string.Join(" ", waargenomenVolgorde)} (was: {string.Join(" ", bestaand.MeldernNummers)}).");
            bestaand.MeldernNummers = waargenomenVolgorde.ToList();
        }
        else
        {
            Log($"Bezetmelder-volgorde voor blok {naar.Nummer} (vanuit blok {van.Nummer}) automatisch geleerd: {string.Join(" ", waargenomenVolgorde)}.");
            naar.RichtingsBezetmeldingen.Add(new RichtingsBezetmelding { VanBlok = van, MeldernNummers = waargenomenVolgorde.ToList(), AutomatischGeleerd = true });
        }
    }

    private static List<(Blok Blok, Bezetmeldpunt? Melder, double WachttijdSeconden)> BouwGebeurtenissen(List<Blok> pad, Treintype? treintype, bool rangeren = false, double treinLengte = 0, Trein? trein = null, double routeMaxSnelheid = 0, List<BlokRelatie>? relaties = null)
    {
        double treintypeSnelheid = treintype?.GemiddeldeSnelheid ?? 0;

        // Massa simulatie (Onderhouden treintypes): de volle 0->50 en 50->0-tijden zijn
        // bedoeld voor één keer optrekken/afremmen, niet om letterlijk bij elk blok op te
        // tellen (anders duurt een rit met veel blokken absurd lang). We tellen daarom een
        // deel van die tijd mee bij elke voorblok- resp. stopsectie-melding: een fractie van
        // de acceleratietijd bij het optrekken naar het volgende blok, en een fractie van de
        // remtijd bij het insturen van elk blok - realistischer dan niets, zonder de rit
        // onbruikbaar traag te maken.
        // "Rangeren" (Koploper's Rijwindow/tabblad Algemeen) schakelt deze massasimulatie
        // bewust helemaal uit - de trein reageert dan direct, voor precies manoeuvreren.
        // "Koploper zal altijd de traagste massa simulatie kiezen" tussen treintype en de
        // individuele loc - een loc kan een eigen (afwijkende) massasimulatie hebben; als
        // die trager is dan het treintype, geldt de tragere. 0 op de loc = geen eigen
        // instelling, gewoon het treintype gebruiken.
        double accelaratieBasis = Math.Max(treintype?.AccelaratieVan0Naar50Seconden ?? 0, trein?.AccelaratieVan0Naar50Seconden ?? 0);
        double remBasis = Math.Max(treintype?.RemVan50Naar0Seconden ?? 0, trein?.RemVan50Naar0Seconden ?? 0);
        double accelaratieAandeel = rangeren ? 0 : accelaratieBasis * 0.25;
        double remAandeel = rangeren ? 0 : remBasis * 0.25;
        double vertrekvertraging = treintype?.VertrekvertragingSeconden ?? 0;
        double wachttijdBijKeren = treintype?.WachttijdBijKerenSeconden ?? 0;

        var lijst = new List<(Blok, Bezetmeldpunt?, double)>();
        if (pad.Count == 0) return lijst;

        lijst.Add((pad[0], null, 0)); // startblok: trein staat er al bij het begin

        for (int i = 1; i < pad.Count; i++)
        {
            var blok = pad[i];

            // Traject-snelheid (Stamgegevens blok): Koploper houdt altijd de LAAGSTE
            // snelheid aan van treintype, het traject zelf én de route zelf - een snelle
            // trein moet dus afremmen voor een blok met een lagere limiet, voor een route
            // met een eigen (lagere) snelheidslimiet, en een langzame trein blijft
            // vanzelfsprekend ook langzaam op een sneller traject. Matcht Koploper's eigen
            // regel: "de toegestane snelheid is de laagste snelheid die geldt voor rijweg,
            // locomotief of treinsoort" - hier dus de laagste van treintype/blok/route.
            double effectieveSnelheid = treintypeSnelheid;
            if (blok.MaxSnelheid > 0 && (effectieveSnelheid <= 0 || blok.MaxSnelheid < effectieveSnelheid))
                effectieveSnelheid = blok.MaxSnelheid;
            if (routeMaxSnelheid > 0 && (effectieveSnelheid <= 0 || routeMaxSnelheid < effectieveSnelheid))
                effectieveSnelheid = routeMaxSnelheid;
            double snelheidsFactor = effectieveSnelheid > 0 ? Math.Clamp(ReferentieSnelheid / effectieveSnelheid, 0.3, 4.0) : 1.0;

            var voorblok = BepaalAankomstmelder(blok, pad[i - 1]);
            var stopsectie = BepaalStopmelder(blok, pad[i - 1], treinLengte);

            // Stopkans/wachttijd per (treintype, bloktype) - de volledige matrix uit
            // Koploper's "Onderhouden gegevens per treintype/bloktype". Vervangt de
            // eerdere, aparte Station-altijd-stoppen + Normaal-blok-alleen-stopkans-logica
            // door één uniform mechanisme dat voor ALLE bloktypes geldt (dus ook een
            // treintype dat een Station-blok met stopkans 0% gewoon voorbijrijdt, precies
            // het klassieke "goederendieseltrein stopt niet op het station"-voorbeeld).
            double willekeurigeStopExtra = 0;
            if (treintype != null)
            {
                var (stopkansPercent, minWacht, maxWacht) = treintype.BloktypeGedragVoor(blok.Type);
                if (stopkansPercent > 0 && _stopkansGenerator.NextDouble() * 100 < stopkansPercent)
                {
                    double min = Math.Min(minWacht, maxWacht);
                    double max = Math.Max(minWacht, maxWacht);
                    willekeurigeStopExtra = min + _stopkansGenerator.NextDouble() * (max - min);
                }
            }
            else if (blok.Type == BlokType.Station)
            {
                willekeurigeStopExtra = StationInstaptijd; // geen treintype bekend (bijv. rangeerrit): oud, vast gedrag als terugval
            }

            // Vertrekvertraging: alleen bij de allereerste beweging van deze rit, net als een
            // echt sein dat pas na een korte vertraging op groen "vertaalt" naar het echt wegrijden.
            double vertrekExtra = i == 1 ? vertrekvertraging : 0;

            // Wachttijd bij keren: een kopspoor is per definitie waar een trein moet keren,
            // en/of Koploper's eigen "uit-via-naar"-principe: een relatie die zelf als
            // "Keer" gemarkeerd is (bijv. een normaal blok waar de trein terug moet naar
            // waar hij vandaan kwam) - zie Model.BlokRelatie.Keer voor de achtergrond.
            var gebruikteRelatie = i > 0 ? relaties?.FirstOrDefault(r => r.Van == pad[i - 1] && r.Naar == blok) : null;
            double kerenExtra = (blok.Type == BlokType.Kopspoor || gebruikteRelatie?.Keer == true) ? wachttijdBijKeren : 0;

            // Uitroltijd ("berekende stopplaats na... cm" uit het echte Koploper): alleen
            // relevant bij een ECHTE, volledige stop - dus het laatste blok van het pad, of
            // een blok waar de trein sowieso al moet keren/wachten. Niet bij een blok waar
            // de stopsectie alleen ter bezetmelding dient terwijl de trein gewoon doorrijdt.
            bool ditIsHetLaatsteBlok = i == pad.Count - 1;
            double uitrolExtra = 0;
            if (ditIsHetLaatsteBlok || kerenExtra > 0 || willekeurigeStopExtra > 0)
                uitrolExtra = blok.UitrolSecondenOverride >= 0 ? blok.UitrolSecondenOverride : SimulatieInstellingen.StandaardUitrolSeconden;

            if (voorblok != null)
                lijst.Add((blok, voorblok, 1.2 * snelheidsFactor + accelaratieAandeel + vertrekExtra));
            else
                lijst.Add((blok, null, 1.5 * snelheidsFactor + accelaratieAandeel + vertrekExtra)); // geen meldpunt ingesteld: vaste tijd als terugval

            if (stopsectie != null)
                lijst.Add((blok, stopsectie, 1.0 * snelheidsFactor + remAandeel + kerenExtra + willekeurigeStopExtra + uitrolExtra)); // station/stop: extra wachttijd bij de stopmelding
            else if (kerenExtra > 0 || willekeurigeStopExtra > 0)
                lijst.Add((blok, null, kerenExtra + willekeurigeStopExtra + uitrolExtra)); // station/kopspoor/stop zonder eigen stopsectie-meldpunt: los extra oponthoud
        }

        return lijst;
    }

    /// <summary>Referentiewachttijd (seconden) voor één stap in het automatisch rijden -
    /// geen bezetmeldpunten hier, dus gewoon een vaste tijd, geschaald op treintype-snelheid
    /// net als de rest van de simulator.</summary>
    private const double AutomatischeStapSeconden = 1.3;

    /// <summary>Kern van "automatisch rijden": kijkt of er een vrij en toegestaan
    /// vervolgblok is (niet bezet/gereserveerd, geen richtingsverbod, geen stopverbod - een
    /// blok zonder enige beperking is standaard toegestaan) en kiest de EERSTE die voldoet.
    /// Is er niks beschikbaar, dan wordt het gewoon over een seconde opnieuw geprobeerd -
    /// de trein wacht dus vanzelf tot er iets vrijkomt, in plaats van vast te lopen.</summary>
    /// <summary>Optioneel: `wachttijdSeconden` overschrijft de standaard 1 seconde - gebruikt
    /// door de aanroeper na aankomst bij een kopspoor, om daar de ECHTE, ingestelde
    /// WachttijdBijKerenSeconden (Treintype) aan te houden i.p.v. de normale, korte
    /// stap-voor-stap-retry-tijd (een kopspoor is per definitie doodlopend: de trein moet
    /// daar altijd eerst volledig afremmen/stoppen, dan een tijdje wachten, en pas daarna
    /// omkeren en wegrijden - zie ook VerwerkEventueelKeren).</summary>
    private void PlanVolgendeAutomatischeStap(RijdendeTrein trein, double wachttijdSeconden = 1)
    {
        trein.Timer?.Stop();
        trein.Timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(wachttijdSeconden * SimulatieInstellingen.VertragingsFactor) };
        trein.Timer.Tick += (_, _) => AutomatischeStapProberen(trein);
        trein.Timer.Start();
    }

    private void AutomatischeStapProberen(RijdendeTrein trein)
    {
        trein.Timer?.Stop();
        var huidig = trein.HuidigBlok!;

        var kandidaten = _blokBeheerder.VolgendeBlokken(huidig)
            .Where(b => _routeBeheerder.IsOvergangToegestaan(trein.VorigBlok, huidig, b, trein.EffectiefTreintype))
            .Where(b => _stopverbodBeheerder.IsStoppenToegestaan(b, trein.EffectiefTreintype))
            .Where(b => !_blokBeheerder.IsGeblokkeerdVoorRit(b))
            .Where(b => PadIsFysiekHaalbaar(huidig, b))
            .ToList();

        // Treinlengte: een blok met MagNietStoppen voor deze trein telt niet mee als kandidaat.
        if (trein.Trein != null && trein.Trein.Lengte > 0)
        {
            kandidaten = kandidaten.Where(b =>
                b.MaxTreinlengte <= 0 || b.MaxTreinlengte >= trein.Trein.Lengte ||
                !b.TeLangeTreinActie.HasFlag(TeLangeTreinActie.MagNietStoppen)).ToList();
        }

        // Voorkom heen-en-weer pendelen: als er nog een ANDERE optie is dan het blok waar de
        // trein net vandaan kwam, kies die - anders zou een trein bij elke terugkeer naar een
        // eerder blok daar zomaar weer naartoe kunnen springen en voor altijd tussen twee
        // blokken heen en weer blijven gaan, ook als er verderop nog van alles vrij is. Alleen
        // als teruggaan de ENIGE optie is (bijv. een kopspoor waar je moet keren) blijft het toegestaan.
        //
        // GEVONDEN GAT (gebruikersverzoek: wissel 14 moest bij blok 4 een 50%-kans krijgen
        // om terug naar blok 3 te rijden - dat gebeurde nooit): deze regel sloot VorigBlok
        // altijd categorisch uit zodra er een ander alternatief was, ook als VorigBlok zelf
        // via een HELEMAAL ANDERE wissel/route bereikt wordt dan waarmee de trein er net
        // vandaan kwam en VorigBlok verderop, gezien vanuit zichzelf, nog een eigen ANDER
        // vervolgblok heeft dan "huidig" (bijv. blok 3 kan zowel naar blok 4 als naar blok 7 -
        // een terugkeer naar blok 3 is dan geen doodlopende pendel, maar gewoon een legitieme
        // aftakking die verderop weer verder gaat). Alleen als VorigBlok, eenmaal daar
        // aangekomen, GEEN ander bruikbaar vervolgblok heeft dan weer terug naar "huidig" (het
        // klassieke twee-blokken-heen-en-weer-scenario) blijft de bestaande, harde uitsluiting
        // van kracht.
        if (trein.VorigBlok != null && kandidaten.Count > 1)
        {
            // GEBRUIKERSCORRECTIE: mijn eerdere aanname dat blok 5 een "kaal stuk spoor
            // zonder eigen wissel" zou zijn, klopte niet - wissel 14 zit WEL degelijk fysiek
            // tussen blok 4 en blok 5 (bevestigd door de gebruiker). Een "bestaat er een
            // wissel op het terugpad"-check (zoals hier eerder stond) kan dus NIET
            // onderscheiden tussen de gewenste blok3/4-pendel en de ongewenste blok4/5-pendel
            // - beide gebruiken immers (deels) dezelfde wissel. Zie VerwerkAutomatischeAankomst/
            // VertrekVanBlokUitvoeren en Model.BlokRelatie.Keer voor de ECHTE, in de
            // Koploper-brondata bevestigde oplossing: het is geen kwestie van deze kandidaat
            // uitsluiten, maar van 'm - als hij gekozen wordt - daadwerkelijk als een keerpunt
            // te behandelen (BlokRelatie.Keer=true), zodat de loc dan ook FYSIEK omkeert i.p.v.
            // dat de software een omkering aanneemt die de loc nooit uitvoert.
            bool vorigBlokIsDoodlopendVoorPendel = !_blokBeheerder.VolgendeBlokken(trein.VorigBlok)
                .Where(b => b != huidig)
                .Any(b => _routeBeheerder.IsOvergangToegestaan(huidig, trein.VorigBlok, b, trein.EffectiefTreintype)
                    && _stopverbodBeheerder.IsStoppenToegestaan(b, trein.EffectiefTreintype)
                    && !_blokBeheerder.IsGeblokkeerdVoorRit(b)
                    && PadIsFysiekHaalbaar(trein.VorigBlok, b));
            // GEVONDEN GAT (fysiek bevestigd door de gebruiker: wissel 14 zit tussen blok 4
            // en blok 5, en in AFBUIGENDE stand loopt hij via de kruiswissel en wissel 5
            // terug naar blok 3 - een VOORWAARTSE (Keer=false) aftakking, geen kop-staart-
            // pendel op hetzelfde stuk spoor). De bovenstaande "heeft VorigBlok verderop nog
            // een eigen andere uitweg"-check ziet dit niet: blok 3's ENIGE eigen vervolg
            // vanuit zichzelf is toevallig ook weer "terug naar huidig" (blok 4, hetzelfde
            // blok waar de trein nu net vandaan komt) - dus werd VorigBlok hier altijd als
            // doodlopende pendel bestempeld en dus altijd uitgesloten, ondanks een ingestelde
            // Kans>0 op de Van=4/Naar=3-relatie. Gevolg: wissel 14 werd in de praktijk NOOIT
            // aangestuurd. Onderscheid nu ZELF nog een keer: gebruikt de weg terug naar
            // "huidig" (VorigBlok->huidig, de normale vervolgstap) DEZELFDE wissels/
            // kruiswissel als de weg die we NU als kandidaat overwegen (huidig->VorigBlok)?
            // Zo ja, is dit een echte pendel op hetzelfde spoor (uitsluiten, oude gedrag).
            // Zo nee - twee aantoonbaar VERSCHILLENDE fysieke paden heen en terug - dan is
            // het geen pendel maar een legitieme lus/aftakking die de trein gewoon voorwaarts
            // verder helpt, en mag VorigBlok best kandidaat blijven.
            if (vorigBlokIsDoodlopendVoorPendel)
            {
                var wisselsHeen = WisselAdressenOpPad(huidig, trein.VorigBlok);
                var kruiswisselsHeen = KruiswisselRijAdressenOpPad(huidig, trein.VorigBlok);
                var wisselsTerug = WisselAdressenOpPad(trein.VorigBlok, huidig);
                var kruiswisselsTerug = KruiswisselRijAdressenOpPad(trein.VorigBlok, huidig);
                bool zelfdeSpoorHeenEnTerug = wisselsHeen.Intersect(wisselsTerug).Any()
                    || kruiswisselsHeen.Intersect(kruiswisselsTerug).Any()
                    || (wisselsHeen.Count == 0 && kruiswisselsHeen.Count == 0 && wisselsTerug.Count == 0 && kruiswisselsTerug.Count == 0);
                if (zelfdeSpoorHeenEnTerug)
                    kandidaten = kandidaten.Where(b => b != trein.VorigBlok).ToList();
            }
        }

        // "Mag niet keren": HARDE regel, geen terugvaloptie - een loc-getrokken trein
        // (goederentrein, passagierstrein) mag in de echte spoorwereld nooit gedwongen
        // achteruit over de hoofdbaan rijden (de machinist moet voorop zitten). Kandidaten
        // die uiteindelijk altijd op een kopspoor/Keer-relatie uitkomen worden dus
        // ONVOORWAARDELIJK uitgesloten (zie KanVeiligBlijvenRijden) - GEEN terugval naar
        // toch-maar-keren bij een impasse. Blijft er dan niets over, dan wacht de trein
        // gewoon (net als bij "alles bezet") - hij loopt daardoor nooit "af" (RondRitAf),
        // want VolgendeBlokken(huidig) hieronder blijft ONGEFILTERD, dus de doodlopend-
        // check ziet de kopspoor-aansluiting nog gewoon staan en concludeert terecht dat
        // het spoor niet ECHT doodloopt voor deze trein - alleen (nog) niet bruikbaar is.
        // Alleen een trein ZONDER dit kenmerk mag - bijv. voor rangeerbewegingen - gewoon
        // keren zoals voorheen.
        if (trein.Trein?.MagNietKeren == true)
            kandidaten = kandidaten.Where(KanVeiligBlijvenRijden).ToList();

        if (kandidaten.Count == 0)
        {
            // GEVONDEN, KRITIEK VEILIGHEIDSGAT (hardware-log 11:40:39-11:42:14: trein reed
            // vanuit blok7 door tot in blok6 ZONDER dat blok6 ooit gereserveerd was, en viel
            // daar stil met een spookmelding - gebruikersdiagnose: "er waren nog geen
            // vervolgblokken gereserveerd dus hij had in de laatste sectie van blok 7 moeten
            // stoppen". Exact dezelfde architectuurfout als eerder al gevonden en gefixt bij
            // KeerTreinIndienActief (zie de uitgebreide toelichting daar): een nog actieve
            // snelheids-ramp-timer van de VORIGE stap (bezig met optrekken naar de daar
            // geldende doelsnelheid) werd hier NIET stopgezet zodra bleek dat er voor de
            // HUIDIGE stap geen enkele kandidaat is. Verderop in deze methode (de
            // "stopmelderNogNietBereikt"-wachtlus hieronder) wordt BEWUST nog niet geremd -
            // de trein mag immers gewoon doorrollen tot de ECHTE stopmelder van blok7 bereikt
            // is - maar de ramp-timer bleef, zolang dat wachten duurde, gewoon doortikken en
            // de snelheid verder VERHOGEN (bytes-analyse: stap 13->14, nog steeds "vooruit",
            // precies tijdens dit wachten). Daardoor kon de trein, nog vóórdat blok7's eigen
            // stopmelder ooit afging, al voorbij het bedoelde stopblok doorschieten en een
            // niet-gereserveerd volgend blok (hier: blok 6) inrijden. Door de ramp hier,
            // METEEN zodra blijkt dat er geen kandidaat is, te stoppen - ongeacht welke van de
            // onderstaande sub-paden (doodlopend, wachten op stopmelder, terugval, definitieve
            // stop) uiteindelijk gekozen wordt - blijft de trein op zijn HUIDIGE snelheid
            // doorrollen (niet verder versnellen) totdat een van die sub-paden expliciet een
            // nieuwe snelheid (meestal 0) stuurt.
            trein.ActieveSnelheidsRampTimer?.Stop();
            trein.ActieveSnelheidsRampTimer = null;

            // Niks vrij/toegestaan - doodlopend, of gewoon alles bezet. Bij een doodlopend
            // spoor (geen vervolgblokken sowieso) is de rit klaar; anders wachten en opnieuw
            // proberen zodra er iets vrijkomt (elke seconde, geen speciale trigger nodig).
            if (!_blokBeheerder.VolgendeBlokken(huidig).Any())
            {
                RondRitAf(trein, huidig, $"Automatische rit klaar: doodlopend bij blok {huidig.Nummer}.");
                return;
            }

            // GEBRUIKERSVERZOEK ("stopt alleen nog niet in de laatste sectie" - de trein
            // stopte bij een tijdelijk doodlopend blok altijd bij de EERSTE (aankomst-)
            // sectie, ook als er verderop nog meer secties van hetzelfde blok waren):
            // zelfde "wacht op het echte stootjuk"-principe als bij een Kopspoor-aankomst,
            // nu toegepast op elk blok waar (nog) geen kandidaat gevonden wordt. Is er een
            // aparte, nog niet bereikte stopmelder (het VERST gelegen meldpunt van huidig,
            // gezien vanuit de richting waar de trein vandaan kwam) - dan blijft de trein
            // gewoon doorrollen (hij is toch al voorbezet, zie AutomatischeStapProberen) in
            // plaats van hier al te stoppen. Pas als de stopmelder ECHT bereikt is (of er
            // geen aparte stopmelder bestaat) wordt hieronder daadwerkelijk gestopt.
            var stopMelderVoorDoodlopend = BepaalStopmelder(huidig, trein.VorigBlok, trein.Trein?.Lengte ?? 0);
            var aankomstMelderVoorDoodlopend = BepaalAankomstmelder(huidig, trein.VorigBlok);
            bool stopmelderNogNietBereikt = _hardwareBeheerder.Huidige is not SimulatieHardware
                && stopMelderVoorDoodlopend != null && stopMelderVoorDoodlopend.MeldernNummer > 0
                && (aankomstMelderVoorDoodlopend == null || stopMelderVoorDoodlopend.MeldernNummer != aankomstMelderVoorDoodlopend.MeldernNummer)
                && _blokBeheerder.MelderIsBezet(stopMelderVoorDoodlopend.MeldernNummer) != true;
            if (stopmelderNogNietBereikt)
            {
                PlanVolgendeAutomatischeStap(trein);
                return;
            }

            // GEBRUIKERSVERZOEK ("als blok 6 bijvoorbeeld (tijdelijk) niet beschikbaar is,
            // moet hij ook kijken of er een andere mogelijkheid is om verder te rijden, en
            // niet stil blijven staan") EN LATER, UITDRUKKELIJK ("altijd een weg terug
            // creëren, dus in dit geval van blok 5 terug naar blok 4"): als ALLERLAATSTE
            // redmiddel terugkeren naar VorigBlok, ook als die overgang normaal via een
            // Richtingsverbod is uitgeschakeld voor gewoon automatisch rijden (zie Model.
            // Richtingsverbod/bijv. blok5->blok4: die stond uit omdat een WILLEKEURIGE,
            // naast een normale vooruit-optie gekozen terugkeer tot fysieke oscillatie kon
            // leiden). GEVONDEN, KRITIEK GAT bij de EERSTE live test van een eerdere versie
            // van deze terugval (log 10:02-10:05, spookmelding blok 4 + NOODSTOP): de
            // terugkeer 5->4 zelf (de fysieke kering) verliep prima, maar bij blok 4
            // aangekomen bleek blok 5 - het blok waar de trein net vandaan kwam - voor de
            // GEWONE kandidaat-keuze gewoon weer een toegestane, en met Kans 2 tegen 1 zelfs
            // VAKER gekozen, vervolgstap dan blok 3. Geen dubbele/foute keer-opdracht dus
            // (Van=4,Naar=3 staat terecht op Keer=false, en Van=4,Naar=5 ook - de loc hoefde
            // voor geen van beide nogmaals te keren) - het was de gewone candidate-selectie
            // bij blok 4 die, zodra de trein daar via deze terugval arriveerde, zelf gewoon
            // weer voor "terug naar blok 5" koos. GEBRUIKERSCORRECTIE/-INZICHT: Koploper kent
            // hiervoor exact dit soort "komend vanuit X, naar Y"-regels per blok (vergelijk
            // Model.Richtingsverbod.Uit, dat precies dit al ondersteunt maar hier nog niet
            // voor dit specifieke geval was ingezet). Daarom nu drie gerichte, symmetrische
            // Richtingsverboden toegevoegd (Van=4,Naar=5,Uit=5 / Van=5,Naar=6,Uit=6 /
            // Van=6,Naar=7,Uit=7 - zie de baan-JSON): die verbieden specifiek "terug naar het
            // blok waar je net vandaan kwam", maar ALLEEN wanneer je er net vandaan kwam -
            // de doodgewone voorwaartse route (bijv. vanuit blok 3 naar blok 4 naar blok 5)
            // blijft hierdoor volledig onaangeroerd. Daarmee is de GEVONDEN oorzaak van de
            // noodstop gericht gedicht, en kan deze terugval weer veilig geprobeerd worden -
            // test dit scenario (bijv. blok 6 weer vergrendelen) alsnog voorzichtig opnieuw.
            Blok? terugvalKandidaat = null;
            if (trein.VorigBlok != null
                && _blokBeheerder.Relaties.Any(r => r.Van == huidig && r.Naar == trein.VorigBlok)
                && !_blokBeheerder.IsGeblokkeerdVoorRit(trein.VorigBlok)
                && _stopverbodBeheerder.IsStoppenToegestaan(trein.VorigBlok, trein.EffectiefTreintype)
                && PadIsFysiekHaalbaar(huidig, trein.VorigBlok))
            {
                terugvalKandidaat = trein.VorigBlok;
                Log($"[Route '{trein.Route.Omschrijving}'] Geen bruikbare vooruit-optie vanaf blok {huidig.Nummer} - gebruikt als allerlaatste redmiddel de terugkeer naar blok {terugvalKandidaat.Nummer} (normaal uitgeschakeld voor gewoon rijden, maar nu de enige overgebleven optie). Let hier extra op: dit vereist een fysieke kering.");
                kandidaten = new List<Blok> { terugvalKandidaat };
            }

            if (terugvalKandidaat is null)
            {
            // GEVONDEN GAT (gebruikersmelding: bij een complexer traject bleef de rit
            // stilzwijgend hangen na Go, zonder enig hardwarecommando EN zonder enige
            // foutmelding): dit pad - topologisch is er wél een vervolgblok, maar na de
            // PadIsFysiekHaalbaar-check (defecte wissels) blijft er niets bruikbaars over -
            // probeerde voorheen stilzwijgend elke seconde opnieuw, voor altijd, zonder ooit
            // iets te loggen. Nu, hoogstens eens per 10 sec (dit kan immers een normale,
            // tijdelijke situatie zijn, bijv. gewoon wachten tot een ander blok vrijkomt),
            // een duidelijke regel zodat dit nooit meer onzichtbaar blijft hangen.
            if (trein.LaatsteGeenKandidatenWaarschuwing is null || (DateTime.Now - trein.LaatsteGeenKandidatenWaarschuwing.Value).TotalSeconds >= 10)
            {
                trein.LaatsteGeenKandidatenWaarschuwing = DateTime.Now;
                var structureelMogelijk = _blokBeheerder.VolgendeBlokken(huidig).ToList();
                var doorDefectWisselGeblokkeerd = structureelMogelijk.Where(b => !PadIsFysiekHaalbaar(huidig, b)).Select(b => b.Nummer).ToList();
                // GEVONDEN GAT (gebruikerswaarneming: melding zei "vermoedelijk gewoon alles
                // bezet/gereserveerd" terwijl de ECHTE reden was dat het enige zinvolle
                // vervolgblok VERGRENDELD stond): VolgendeBlokken filtert vergrendelde
                // blokken al bij de bron weg (zie BlokBeheerder.VolgendeBlokken), dus die
                // kwamen nooit in structureelMogelijk terecht en werden hier dus ook nooit
                // genoemd. Kijk daarom ook naar de RUWE relaties (inclusief vergrendelde
                // doelen) om dit alsnog expliciet te kunnen benoemen.
                var doorVergrendelingGeblokkeerd = _blokBeheerder.Relaties.Where(r => r.Van == huidig && r.Naar.Vergrendeld).Select(r => r.Naar.Nummer).Distinct().ToList();
                string reden = doorDefectWisselGeblokkeerd.Count > 0
                    ? $" Blok(ken) {string.Join(", ", doorDefectWisselGeblokkeerd)} zijn topologisch wel verbonden maar fysiek onbereikbaar (vermoedelijk een defecte wissel onderweg in de verkeerde stand)."
                    : doorVergrendelingGeblokkeerd.Count > 0
                        ? $" Blok(ken) {string.Join(", ", doorVergrendelingGeblokkeerd)} zijn verbonden maar staan VERGRENDELD (zie de bloktekst hierboven) - zet dat uit als dat niet de bedoeling is."
                        : " Vermoedelijk gewoon alles bezet/gereserveerd.";
                Log($"[Route '{trein.Route.Omschrijving}'] Geen bruikbaar volgend blok vanaf blok {huidig.Nummer} - blijft elke seconde opnieuw proberen." + reden);
            }
            // GEVONDEN VEILIGHEIDSGAT (gebruikerswaarneming: trein reed gewoon door tot in
            // blok7's meldpunten, TERWIJL de software de hele tijd "geen bruikbaar volgend
            // blok" bleef melden - blok7 was dus NOOIT gereserveerd): dit pad logde alleen
            // een waarschuwing, maar stuurde nooit een stopcommando. De trein bleef daardoor
            // gewoon doorrollen op de snelheid die hij al had van de VORIGE stap (het
            // voorbezetten naar het huidige blok) - er was immers geen nieuwe kandidaat om
            // naartoe te rijden, maar ook niemand die zei "stop dan maar". Zelfde
            // "stuur naar elk blok"-aanpak als bij de drie eerdere vondsten (verbinden,
            // vastlopen, keren): garandeert dat de trein daadwerkelijk stilstaat terwijl hij
            // wacht op een kandidaat, ongeacht welk bloadres hij op dit moment beluistert.
            if (trein.Trein != null && trein.Trein.DecoderAdres > 0)
            {
                trein.ActieveSnelheidsRampTimer?.Stop();
                trein.ActieveSnelheidsRampTimer = null;
                // BUG #30: zelfde opruiming als bij KeerTreinIndienActief - zonder dit moest
                // dit stopcommando alsnog, per blok, achter nog wachtende, inmiddels
                // achterhaalde snelheidscommando's van de net gestopte ramp aansluiten.
                _hardwareBeheerder.VerwijderWachtendeSnelheidscommandosVoor(trein.Trein.DecoderAdres);
                bool vooruitVoorHardwareGeenKandidaat = trein.Trein.OmgekeerdeRijrichting ? !trein.RijdtVooruit : trein.RijdtVooruit;
                foreach (var blok in _blokBeheerder.Blokken)
                    _hardwareBeheerder.StuurLocSnelheidCommando(trein.Trein.DecoderAdres, 0, vooruitVoorHardwareGeenKandidaat, blok.Nummer, trein.Trein.DecoderStappen);
                foreach (int kruiswisselAdres in AlleKruiswisselRijAdressen())
                    _hardwareBeheerder.StuurLocSnelheidCommando(trein.Trein.DecoderAdres, 0, vooruitVoorHardwareGeenKandidaat, kruiswisselAdres, trein.Trein.DecoderStappen);
                trein.HuidigeSnelheidStap = 0;
            }
            PlanVolgendeAutomatischeStap(trein);
            return;
            } // if (terugvalKandidaat is null)
        }

        var volgendBlok = KiesOptimaleKandidaat(huidig, kandidaten, trein.Trein?.Lengte ?? 0);
        _blokBeheerder.ZetGereserveerd(volgendBlok, true);
        trein.GereserveerdVolgendBlok = volgendBlok;
        int aantalWisselsGezet = ZetWisselsTussenBlokken(huidig, volgendBlok);

        // GEVONDEN GAT (gebruikersmelding: "ik zag enkele keren dat de wissel verkeerd stond
        // waardoor de trein verkeerd reed"): er zat geen enkele wachttijd tussen het
        // versturen van een wisselcommando en het moment dat de trein al de opdracht kreeg
        // om te gaan rijden - een wissel heeft echter FYSIEKE tijd nodig om over te lopen
        // (vooral bij langzaam-lopende wisselaandrijvingen). Reed de trein al vóórdat de
        // wissel klaar was met bewegen, dan kon hij de verkeerde kant op gaan. Nu wordt, ALLEEN
        // als er ook echt wissels gezet zijn EN het geen simulatie betreft, eerst de ingestelde
        // rustpauze (SimulatieInstellingen.WisselRustpauzeMilliseconden) afgewacht voordat de
        // trein daadwerkelijk vertrekt - bij een overgang zonder wissels verandert er niets.
        if (aantalWisselsGezet > 0 && _hardwareBeheerder.Huidige is not SimulatieHardware)
        {
            var wisselWachtTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(SimulatieInstellingen.WisselRustpauzeMilliseconden) };
            wisselWachtTimer.Tick += (_, _) =>
            {
                wisselWachtTimer.Stop();
                if (_venstergesloten) return;
                VertrekVanBlokUitvoeren(trein, huidig, volgendBlok);
            };
            wisselWachtTimer.Start();
        }
        else
        {
            VertrekVanBlokUitvoeren(trein, huidig, volgendBlok);
        }
    }

    /// <summary>Het daadwerkelijke vertrek uit "huidig" richting "volgendBlok" - losgetrokken
    /// uit AutomatischeStapProberen zodat dit, indien nodig, pas NA de wissel-rustpauze
    /// uitgevoerd wordt (zie de aanroep hierboven) in plaats van meteen na het kiezen van de
    /// kandidaat.</summary>
    private void VertrekVanBlokUitvoeren(RijdendeTrein trein, Blok huidig, Blok volgendBlok)
    {
        VerwerkEventueelKeren(trein, trein.VorigBlok, huidig);

        // GEVONDEN GAT (gebruikerswaarneming: "reserveert terug naar blok 6 maar rijd
        // fysiek de andere kant op" - resulteerde in een noodstop): dit gebeurt specifiek
        // wanneer het enige overgebleven, haalbare kandidaat-blok toevallig het blok is
        // waar de trein NET vandaan kwam (bijv. omdat alle andere routes defect/vergrendeld
        // zijn - zie eerdere sessie, blok1/3 bewust defect). Topologisch is dat een omkering
        // van de rijrichting - fysiek moet de trein dus OMDRAAIEN. Maar dit is geen
        // Kopspoor-aankomst (dat dekt VerwerkEventueelKeren hierboven al) - het is gewoon
        // een normaal blok waarvan de ENIGE volgende stap toevallig "terug" is. Zonder deze
        // check bleef trein.RijdtVooruit gewoon op zijn oude waarde staan, en werd er dus
        // in dezelfde fysieke richting doorgereden als daarvoor - de trein reed dan verder
        // DOOR het huidige blok heen (en voorbij), in plaats van terug te keren.
        // GEVONDEN, ARCHITECTUREEL GAT (gebruikerswaarneming: blok3/4 en blok1/2 vormen
        // elk een GESLOTEN LUS via een kruiswissel - de trein rijdt daar altijd VOORUIT
        // rond, in dezelfde fysieke richting, zonder ooit te hoeven keren, ook al komt
        // "volgende stap" daarbij toevallig overeen met het blok waar de trein vandaan
        // kwam). Deze check keek voorheen ALLEEN naar "is het volgende blok toevallig
        // hetzelfde als het vorige" - dat klopt wel voor een echt doodlopend spoor (de
        // blok7<->3/1-pendel via bewust defecte wissels), maar niet voor zo'n lus: daar is
        // "terug bij het vorige blok" gewoon een NORMALE, voorwaartse ronde via een andere
        // fysieke weg. VereistKeren (BlokRelatie.Keer, al langer de bestaande bron van
        // waarheid voor "moet hier fysiek gekeerd worden") maakt dit onderscheid al
        // correct - GEBRUIK ZE DUS OOK HIER, in plaats van de eigen, te simpele aanname.
        // Vereist WEL dat de gebruiker Keer=true zet op de relatie(s) waar echt gekeerd
        // moet worden (bijv. blok7->3 en blok7->1) - blijft dat ongezet, dan behandelt de
        // software elke terugkeer naar het vorige blok voortaan als een lus (geen keren).
        bool ditIsEenOmkering = VereistKeren(huidig, volgendBlok);
        if (ditIsEenOmkering)
        {
            trein.RijdtVooruit = !trein.RijdtVooruit;
            Log($"[Route '{trein.Route.Omschrijving}'] Volgende stap ({volgendBlok.Nummer}) vereist keren (Keer-relatie of kopspoor) - rijrichting omgekeerd naar {(trein.RijdtVooruit ? "vooruit" : "achteruit")}.");
        }
        // GEVONDEN, KRITIEK GAT (bytes-analyse: melder15 - het meldpunt waar de trein
        // blok4 oorspronkelijk vanuit blok3 BINNENkwam - meldde zich na een omkering als
        // hierboven opnieuw bezet, PRECIES nadat de staart-fase van blok4 net had gemeld
        // "afgerond" - spookmelding): normaal (een vertrek VOORUIT, weg van waar de trein
        // vandaan kwam) is de STOPMELDER van huidig (het verst gelegen meldpunt, gezien
        // vanuit trein.VorigBlok) terecht het laatst te verwachten meldpunt - de trein
        // verlaat huidig immers in die richting. Maar bij de omkering hierboven rijdt de
        // trein het WEL AL doorlopen stuk van huidig gewoon TERUG - dan is niet die
        // stopmelder, maar juist het AANKOMSTmelder (het meldpunt waar de trein huidig
        // oorspronkelijk binnenkwam) het punt dat als laatste, opnieuw, bezet én weer vrij
        // gaat melden. Zonder deze correctie ging de staart-fase op het verkeerde (te
        // vroeg bereikbare) meldpunt wachten, meldde zichzelf te vroeg "klaar", en zag de
        // onvermijdelijke, normale doorkomst over het aankomstmelder aan voor een
        // spookmelding.
        var vertrekMelderVoorHuidig = ditIsEenOmkering
            ? BepaalAankomstmelder(huidig, volgendBlok)
            : BepaalVertrekmelder(huidig, trein.VorigBlok, trein.Trein?.Lengte ?? 0);

        // GEVONDEN, FUNDAMENTELER GAT (gebruikersmelding: "ik moet de loc eerst handmatig
        // laten rijden, dan komt hij pas in beweging" - bij ECHTE hardware kwam er
        // domweg NOOIT een eerste snelheidscommando uit, tot iemand 'm handmatig aanstootte):
        // het daadwerkelijke "optrekken"-commando (LogSnelheidsopbouw) stond hierbeneden pas
        // in VerwerkAutomatischeAankomst - dat wordt pas aangeroepen NADAT de aankomst bij
        // volgendBlok al bevestigd is (echte melding, of de veiligheids-timeout). Bij echte
        // hardware ontstond zo een klassieke kip-en-ei-vergrendeling: de software wachtte op
        // een bezetmelding van volgendBlok, maar had de loc nog helemaal geen opdracht
        // gegeven om daar ooit naartoe te RIJDEN - dus die melding kwam simpelweg nooit
        // (behalve na de 30-seconden-veiligheids-timeout, die dan alsnog met terugwerkende
        // kracht "aankomst" verklaarde terwijl de loc feitelijk nog gewoon stilstond op
        // "huidig"). Fix: de loc krijgt nu HIER, DIRECT bij het kiezen van het volgende blok
        // (na een eventuele wissel-rustpauze, zie hierboven), al de opdracht om daadwerkelijk
        // te gaan rijden - de wachttijd/melder hieronder is dan pas echt "hoe lang duurt de
        // rit die al bezig is", niet "wanneer mag hij beginnen". Geldt bewust ook voor
        // Simulatie (geen enkel nadeel: de snelheidsopbouw-animatie speelt daardoor nu netjes
        // bij vertrek i.p.v. ineens samengeperst vlak vóór aankomst).
        LogLocFuncties(trein, LocGebeurtenis.VerlaatBlok);
        if (huidig.Type == BlokType.Station) LogLocFuncties(trein, LocGebeurtenis.GaatWeerRijden);
        LogSnelheidsopbouw(trein, optrekken: true, huidig, voorbezetBlok: volgendBlok);
        StartStaartFase(huidig, StaartduurSeconden, vertrekMelderVoorHuidig);
        // Harde garantie bovenop StartStaartFase se eigen (snapshot-gebaseerde) selectie:
        // bij een omkering moet het aankomstmelder van huidig ALTIJD expliciet worden
        // meegewacht, ongeacht of de snapshot op het moment van StartStaartFase toevallig
        // net wel of niet "bevestigd bezet" liet zien voor dat specifieke meldpunt - de
        // trein rijdt er hoe dan ook nog een keer overheen bij het terugkeren.
        if (ditIsEenOmkering && vertrekMelderVoorHuidig != null && vertrekMelderVoorHuidig.MeldernNummer > 0)
            _staartWachtMelders[vertrekMelderVoorHuidig.MeldernNummer] = huidig;

        // Zelfde "laagste van treintype/blok/route"-regel als bij vaste routes
        // (BouwGebeurtenissen) - hier los toegepast omdat automatisch rijden zijn eigen,
        // reactieve snelheidsberekening heeft (geen vooraf uitgeklapt pad).
        double effectieveAutomatischeSnelheid = trein.EffectiefTreintype?.GemiddeldeSnelheid ?? 0;
        if (volgendBlok.MaxSnelheid > 0 && (effectieveAutomatischeSnelheid <= 0 || volgendBlok.MaxSnelheid < effectieveAutomatischeSnelheid))
            effectieveAutomatischeSnelheid = volgendBlok.MaxSnelheid;
        if (trein.Route.MaxSnelheid > 0 && (effectieveAutomatischeSnelheid <= 0 || trein.Route.MaxSnelheid < effectieveAutomatischeSnelheid))
            effectieveAutomatischeSnelheid = trein.Route.MaxSnelheid;
        double snelheidsFactor = effectieveAutomatischeSnelheid > 0 ? Math.Clamp(ReferentieSnelheid / effectieveAutomatischeSnelheid, 0.3, 4.0) : 1.0;

        // GEVONDEN GAT (gebruikersmelding: "op het scherm beweegt hij, zonder dat er een
        // bezetmelder-wijziging is gezien"): vrij automatisch rijden gebruikte hier ALTIJD
        // een vaste, gesimuleerde staptijd (AutomatischeStapSeconden) - de eerdere fix voor
        // ECHTE-bezetmelder-gating raakte alleen vaste routes (PlanVolgendeGebeurtenis), NIET
        // dit stuk. Zelfde principe nu ook hier: bij ECHTE hardware EN een Voorblok-meldpunt
        // op het gekozen volgende blok, wacht deze stap primair op de ECHTE bezetmelding (zie
        // Bezetmelding_VanHardware) - de timer hieronder wordt dan een veiligheids-timeout,
        // GEEN gesimuleerde tijd meer (zie PlanVolgendeGebeurtenis hieronder voor de volledige
        // toelichting bij Blok.GeleerdeReistijden/RegistreerGeleerdeReistijd).
        var voorblokMelder = BepaalAankomstmelder(volgendBlok, huidig);
        bool wachtOpEchteMelding = _hardwareBeheerder.Huidige is not SimulatieHardware && voorblokMelder != null && voorblokMelder.MeldernNummer > 0;
        trein.WachtOpMeldernNummer = wachtOpEchteMelding ? voorblokMelder!.MeldernNummer : null;
        trein.WachtBegonnenOp = null;
        trein.GeobserveerdeMeldersTijdensWachten.Clear();

        double intervalSeconden;
        if (wachtOpEchteMelding)
        {
            trein.WachtBegonnenOp = DateTime.Now;
            // Meteen de HUIDIGE stand opvragen i.p.v. alleen te wachten op een spontane
            // overgang - zie DinamoHardware.VraagMelderStatusOp voor de volledige
            // toelichting (dekt het geval dat de loc al precies op deze melder staat).
            if (_hardwareBeheerder.Huidige is DinamoHardware)
                _hardwareBeheerder.VraagMelderStatusOp(voorblokMelder!.MeldernNummer);
            double? geleerdeReistijd = _blokBeheerder.GeefGeleerdeReistijd(volgendBlok, huidig, trein.Trein);
            intervalSeconden = geleerdeReistijd.HasValue
                ? Math.Max(geleerdeReistijd.Value * GeleerdeReistijdVastlopenMarge, 30.0)
                : OnbekendeReistijdVastlopenSeconden;
            trein.HuidigeWachtIntervalSeconden = intervalSeconden;
        }
        else
        {
            intervalSeconden = AutomatischeStapSeconden * snelheidsFactor * SimulatieInstellingen.VertragingsFactor;
            trein.HuidigeWachtIntervalSeconden = null;
        }

        var stapTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(intervalSeconden) };
        stapTimer.Tick += (_, _) =>
        {
            if (wachtOpEchteMelding)
                MeldTreinVastgelopen(trein, voorblokMelder!.MeldernNummer, intervalSeconden);
            else
                VerwerkAutomatischeAankomst(trein, huidig, volgendBlok);
        };
        trein.Timer = stapTimer;
        stapTimer.Start();
    }

    /// <summary>De daadwerkelijke "aankomst"-verwerking van één stap vrij automatisch
    /// rijden - EXACT dezelfde inhoud als voorheen de inline stapTimer.Tick-lambda in
    /// AutomatischeStapProberen, nu een losse, herbruikbare methode zodat zowel de
    /// gesimuleerde/veiligheids-timeout-timer hierboven ALS Bezetmelding_VanHardware (bij
    /// een ECHTE bezetmelding) 'm kunnen aanroepen zonder de kandidaatkeuze-logica opnieuw
    /// te hoeven doorlopen (die al vastligt in trein.GereserveerdVolgendBlok).</summary>
    private void VerwerkAutomatischeAankomst(RijdendeTrein trein, Blok huidig, Blok volgendBlok)
    {
        trein.Timer?.Stop();
        trein.WachtOpMeldernNummer = null;
        if (_venstergesloten) return;

        // Het "optrekken"/vertrek-commando (LogLocFuncties VerlaatBlok/GaatWeerRijden,
        // LogSnelheidsopbouw optrekken:true, StartStaartFase) is verplaatst naar
        // AutomatischeStapProberen, vlak nadat volgendBlok gekozen is - zie de toelichting
        // daar. Hier gaat het alleen nog om de AANKOMST-bevestiging (bezet/gereserveerd-
        // boekhouding, eventueel afremmen, en de volgende stap plannen).
        trein.VorigBlok = huidig;
        trein.HuidigBlok = volgendBlok;
        trein.Pad.Add(volgendBlok);
        trein.GereserveerdVolgendBlok = null;
        _blokBeheerder.ZetStaart(volgendBlok, false);
        _blokBeheerder.ZetBezet(volgendBlok, true);
        _blokBeheerder.ZetGereserveerd(volgendBlok, false);
        LogSeinenVoorBlok(volgendBlok, true);
        bool moetKerenBijAankomst = VereistKeren(huidig, volgendBlok);

        // GEVONDEN GAT (gebruikerswaarneming: "stopt nog steeds in de eerste sectie van het
        // eindblok" - de vorige fix (in AutomatischeStapProberen se "geen kandidaat"-tak)
        // hielp niet, want die tak werd hier vaak HELEMAAL NIET bereikt: zodra het vorige
        // blok se eigen staart-fase toevallig OOK net rond dit moment afrondde, was er WEL
        // meteen een kandidaat beschikbaar, en werd er dus meteen vertrokken vanaf de EERSTE
        // (aankomst-)melder, zonder ooit de rest van dit blok te doorkruisen): dit stond
        // hieronder voorheen aan moetKerenBijAankomst GEKOPPELD - alleen een Kopspoor-
        // aankomst wachtte op de echte, verderop gelegen stopmelder. Maar "eerst helemaal
        // doorrollen naar het uiteinde vóórdat de volgende stap gekozen wordt" is evengoed
        // zinvol bij een GEWOON blok met meerdere secties - niet alleen bij een Kopspoor.
        // Nu geldt dit voor ELK blok met een aparte, nog niet bereikte stopmelder: de
        // candidate-keuze (AutomatischeStapProberen, via PlanVolgendeAutomatischeStap) wordt
        // pas gedaan NADAT de stopmelder echt bevestigt - de trein blijft tot dan gewoon op
        // zijn al voorbezette snelheid doorrollen (geen nodeloze tussenstop bij een normale,
        // ononderbroken doorrit).
        var aankomstMelderVoorVergelijking = BepaalAankomstmelder(volgendBlok, huidig);
        var stopMelderVoorKeren = BepaalStopmelder(volgendBlok, huidig, trein.Trein?.Lengte ?? 0);
        bool heeftApartWerkendeStopmelder = stopMelderVoorKeren != null && stopMelderVoorKeren.MeldernNummer > 0
            && (aankomstMelderVoorVergelijking == null || stopMelderVoorKeren.MeldernNummer != aankomstMelderVoorVergelijking.MeldernNummer);
        bool wachtOpEchteStopmelding = heeftApartWerkendeStopmelder && _hardwareBeheerder.Huidige is not SimulatieHardware;

        LogLocFuncties(trein, volgendBlok.Type == BlokType.Station || moetKerenBijAankomst ? LocGebeurtenis.AankomstEnStopt : LocGebeurtenis.AankomstEnRijdtDoor);
        if ((volgendBlok.Type == BlokType.Station || moetKerenBijAankomst) && !wachtOpEchteStopmelding)
            LogSnelheidsopbouw(trein, optrekken: false, volgendBlok);

        // De loc-markering (zichtbaar icoontje in het blokkenschema) moet meeschuiven
        // met de trein, anders blijft hij achter op het startblok terwijl de bezetting
        // al lang verder is.
        if (trein.Trein != null)
        {
            _blokBeheerder.VerwijderLoc(huidig);
            _blokBeheerder.PlaatsLoc(volgendBlok, trein.Trein);
        }

        string bericht = $"[Route '{trein.Route.Omschrijving}', automatisch] Blok {volgendBlok.Nummer} bezet.";
        StatusTekst.Text = bericht;
        Log(bericht);
        // Zie _legeStaartFaseBlokken hierboven: deze bevestigde aankomst is het enige
        // betrouwbare bewijs dat huidig (waar geen enkele melder ooit iets van liet horen)
        // daadwerkelijk verlaten is - rond die staart-fase hier alsnog af, in plaats van
        // voor altijd te blijven wachten op een melding die nooit komt. Zonder deze fix
        // bleef zo'n blok voor de rest van de sessie permanent bezet/staart, wat een route
        // die er later weer langs moet (zoals een lus) definitief blokkeerde.
        if (_legeStaartFaseBlokken.Contains(huidig))
        {
            Log($"[Route '{trein.Route.Omschrijving}'] Blok {huidig.Nummer} had geen enkel eigen meldpunt om op te wachten (vers vertrek zonder bevestiging) - de bevestigde aankomst bij blok {volgendBlok.Nummer} wordt nu gebruikt als bewijs dat blok {huidig.Nummer} alsnog vrij is.");
            BeeindigStaartFase(huidig);
            // Zie _synthetischVrijgegevenBlokken hierboven: deze vrijverklaring is INDIRECT
            // bewijs (via het volgende blok), geen echte eigen vrijmelding - behandel een
            // eventuele latere, nog binnenkomende bezetmelding van dit blok's eigen
            // meldpunten daarom niet als "terugrijdend".
            _synthetischVrijgegevenBlokken.Add(huidig);
        }
        Redraw();

        // Automatisch rijden MET een vastgelegd bestemmingsblok stopt daar vanzelf -
        // nodig voor "Herhalen" (zie StopTrein): een terugrit is óók gewoon automatisch
        // rijden, maar dan met het startblok van de heenroute als bestemming.
        if (trein.Route.Bestemmingsblok != null && volgendBlok == trein.Route.Bestemmingsblok)
        {
            RondRitAf(trein, volgendBlok, $"Aangekomen bij bestemmingsblok {volgendBlok.Nummer}.");
            return;
        }

        // Gebruikersverzoek (kijkscherm): "alle locs rijden naar het einde van hun
        // gereserveerde rijweg en stoppen daar, dus geen noodstop maar gewoon een rustig
        // stoppen" - zie TreinrouteWindow.AlleTreinenRustigStoppen (via het kijkscherm). Bewust NA de aankomst-
        // boekhouding hierboven (blok netjes bezet, sein gezet, loc-icoon verplaatst) maar
        // VOOR het kiezen van een NIEUW volgend blok - de trein maakt de al ingezette
        // (gereserveerde) stap dus altijd gewoon af, en stopt pas ALS die stap voltooid is.
        // Precies hetzelfde principe als MeldTreinVastgelopen/StartStaartFase: nooit iets
        // afbreken op basis van een aanname, alleen op basis van een al bevestigd feit.
        if (trein.StopNaDezeStap)
        {
            trein.StopNaDezeStap = false;
            RondRitAf(trein, volgendBlok, $"Rustig gestopt bij blok {volgendBlok.Nummer} (op verzoek via het kijkscherm).");
            return;
        }

        // Bij een kopspoor OF een Keer-relatie (per definitie een vereiste
        // richtingsomkering, zie VereistKeren) moet de trein hier altijd eerst de
        // volledige, ingestelde WachttijdBijKerenSeconden wachten (al afgeremd/gestopt
        // via de LogSnelheidsopbouw-aanroep hierboven) VOORDAT er weer een vervolgstap
        // geprobeerd wordt - dat vervolgstap-moment is precies waar VerwerkEventueelKeren
        // (aan het begin van AutomatischeStapProberen) de rijrichting omdraait en weer
        // wegrijdt.
        double wachttijdNaAankomst = moetKerenBijAankomst
            ? (trein.EffectiefTreintype?.WachttijdBijKerenSeconden ?? 10)
            : 1;

        // GEVONDEN, ARCHITECTUREEL GAT (gebruikerswaarneming: "de loc stopt nu zodra hij in
        // de 1e sectie van blok 10 komt, en keert daar al" - hij hoort pas bij het ECHTE,
        // fysieke uiteinde te keren): bij een blok met meerdere secties (Bezetmeldpunten)
        // is de AANKOMSTmelder die deze stap al bevestigd heeft (BepaalAankomstmelder,
        // meestal de EERSTE sectie) niet per se hetzelfde punt als waar veilig gekeerd kan
        // worden (BepaalStopmelder, het FYSIEKE uiteinde/laatste sectie). Keren op basis van
        // alleen een VASTE wachttijd na die eerste melding is precies het "timer i.p.v.
        // feit"-patroon dat we juist overal proberen te vermijden - de wachttijd is
        // immers een schatting voor de HELE resterende afstand tot het uiteinde, niet een
        // bevestiging dat die daadwerkelijk al afgelegd is. Is er een ANDERE, echte
        // stopmelder beschikbaar (heeftApartWerkendeStopmelder/wachtOpEchteStopmelding,
        // hierboven al berekend - zie ook de toelichting daar over WAAROM er hierboven dan
        // ook nog GEEN afremcommando verstuurd is), wacht dan EERST op DIE echte melding
        // voordat er daadwerkelijk gekeerd wordt - de wachttijd hierboven wordt dan pas
        // toegepast NA die bevestiging (bijv. voor eventuele instaptijd), niet als
        // vervanging ervoor.
        if (wachtOpEchteStopmelding)
        {
            // GEVONDEN, ARCHITECTUREEL GAT (gebruikerswaarneming: "ik heb geen enkele
            // reservering naar blok3 gezien vanuit blok7" - de trein reed toch door, maar
            // de wissels werden pas gezet NADAT de trein er al doorheen was): bij DEZE
            // opstelling valt de stopmelder van volgendBlok (hierboven, stopMelderVoorKeren)
            // toevallig SAMEN met het fysieke punt waar de trein het DAAROPVOLGENDE blok al
            // binnenrijdt (bijv. een korte, gedeelde kruiswissel-sectie - hetzelfde meldpunt
            // hoort dan bij BEIDE blokken se Bezetmeldpunten-lijst). Zonder deze fix wachtte
            // de candidate-keuze (en dus ZetWisselsTussenBlokken, inclusief de kruiswissel-
            // stroomfix) tot NA die stopmelder - te laat, de trein was er dan al doorheen
            // met de wissel nog in zijn OUDE stand. Is er nu, VOORDAT we op de stopmelder
            // gaan wachten, precies ÉÉN fysiek haalbare kandidaat vanaf volgendBlok, zet
            // dan alvast PROACTIEF de wissels (en de kruiswissel-stroom) daarvoor - precies
            // zoals AutomatischeStapProberen dat normaal pas LATER zou doen, nu vast vooruit
            // geschoven zodat de wissel echt op tijd staat. Bewust ALLEEN bij precies één
            // kandidaat (niet bij twee-of-meer) - bij meerdere opties zou vooruit kiezen
            // een gok zijn, en dat is hier niet de bedoeling.
            var vroegeKandidaten = _blokBeheerder.VolgendeBlokken(volgendBlok)
                .Where(b => _routeBeheerder.IsOvergangToegestaan(huidig, volgendBlok, b, trein.EffectiefTreintype))
                .Where(b => _stopverbodBeheerder.IsStoppenToegestaan(b, trein.EffectiefTreintype))
                .Where(b => !_blokBeheerder.IsGeblokkeerdVoorRit(b))
                .Where(b => PadIsFysiekHaalbaar(volgendBlok, b))
                .ToList();
            // GEVONDEN GAT (gebruikerswaarneming: "trein raakt de weg kwijt in blok 6" -
            // bevestigd via de diagnostische log: de software week soms terug naar blok5,
            // het blok waar hij net vandaan kwam): AutomatischeStapProberen sluit
            // trein.VorigBlok bewust uit zodra er nog een ANDERE optie is ("voorkom
            // heen-en-weer pendelen", zie daar) - maar DEZE vroege, proactieve
            // kandidatenlijst (die alleen de latere, definitieve keuze vooruit-schuift bij
            // precies één optie) deed die uitsluiting niet. Bij een baan waar - zoals hier -
            // twee kanten oprijden standaard is toegestaan, ziet deze lijst dan zowel
            // "verder" als "terug naar net verlaten blok" als kandidaat (2, niet 1), en
            // slaat de proactieve wisselzetting zichzelf dus stilzwijgend over. Erger nog:
            // zonder deze uitsluiting kan een LATERE herberekening (elders, met dezelfde
            // twee kandidaten) alsnog het verkeerde, achterwaartse blok kiezen. Dezelfde
            // regel hier toepassen, voor consistent gedrag.
            // GEVONDEN GAT (fysiek bevestigd door de gebruiker: wissel 14 tussen blok 4/5
            // gaat in afbuigende stand via de kruiswissel en wissel 5 terug naar blok 3 - een
            // ANDER fysiek pad dan de gewone heenweg blok3->blok4 via wissel 2 alleen): de
            // blote "b != huidig"-uitsluiting hierboven negeerde dat onderscheid volledig en
            // sloot blok 3 hier dus ALTIJD uit zodra er meer dan één kandidaat was, ook al
            // gebruikt de eventuele terugweg (blok4->blok3) heel andere wissels dan de heenweg
            // (blok3->blok4) die de trein al reed. Gevolg: deze proactieve voorspelling ging
            // altijd (ten onrechte, met valse zekerheid) uit van blok5 als enige vervolgstap
            // en zette daar dan ook meteen de wissels voor - ook op momenten dat de latere,
            // ECHTE keuze (zie AutomatischeStapProberen, met dezelfde fix) alsnog blok3 koos.
            // Dat gaf precies het "onnodig schakelen" dat de gebruiker meldde: eerst
            // (voorbarig) de wissels voor blok5 zetten, om ze vervolgens - zodra de echte,
            // latere keuze een andere kant op ging - alsnog meteen weer om te moeten zetten.
            // Zelfde onderscheid als daar: alleen uitsluiten als heen- en terugweg
            // daadwerkelijk hetzelfde spoor (dezelfde wissel/kruiswissel) gebruiken.
            //
            // GEVONDEN, ERNSTIG VEILIGHEIDSGAT (gebruikerswaarneming: "de loc passeert
            // wissel 14 en direct als hij over de wissel is richting blok 5 schakelt de
            // wissel om richting het kruiswissel en blok 3"): deze uitsluiting liep alleen
            // als vroegeKandidaten.Count > 1 was. Maar als blok5 hier om een andere reden
            // (bijv. nog Bezet/Gereserveerd op het exacte controle-moment) al vóór deze
            // check uit de lijst viel, bleef er precies 1 kandidaat over - blok 3, DE
            // PENDEL DIE DEZE CHECK NU juist zou moeten afvangen - en sloeg de hele
            // uitsluiting zichzelf dus over (Count>1 gaf false). Vervolgens werd blok3
            // hieronder als "vroegeKandidaten.Count == 1" kritiekloos geaccepteerd, en werd
            // wissel 14 + het kruiswissel omgezet naar de 4->3-diagonaal TERWIJL de trein nog
            // gewoon richting blok 5 onderweg was - precies de fysiek gevaarlijke situatie
            // die gemeld is. Deze check moet dus ALTIJD lopen zodra huidig bekend is, niet
            // alleen bij 2-of-meer kandidaten, zodat een pendel-kandidaat nooit ongefilterd
            // als "de enige optie" kan doorglippen.
            if (huidig != null)
            {
                var wisselsHeen = WisselAdressenOpPad(huidig, volgendBlok);
                var kruiswisselsHeen = KruiswisselRijAdressenOpPad(huidig, volgendBlok);
                vroegeKandidaten = vroegeKandidaten.Where(b =>
                {
                    if (b != huidig) return true;
                    var wisselsTerug = WisselAdressenOpPad(volgendBlok, huidig);
                    var kruiswisselsTerug = KruiswisselRijAdressenOpPad(volgendBlok, huidig);
                    bool zelfdeSpoorHeenEnTerug = wisselsHeen.Intersect(wisselsTerug).Any()
                        || kruiswisselsHeen.Intersect(kruiswisselsTerug).Any()
                        || (wisselsHeen.Count == 0 && kruiswisselsHeen.Count == 0 && wisselsTerug.Count == 0 && kruiswisselsTerug.Count == 0);
                    return !zelfdeSpoorHeenEnTerug;
                }).ToList();
            }
            if (vroegeKandidaten.Count == 1)
            {
                // GEVONDEN, ARCHITECTUREEL GAT (09:51-09:54-testrit: FOUT "melder 15 niet
                // ontvangen binnen 30 sec" bij blok 4, direct nadat de trein via wissel 14 van
                // blok5 terugkeerde naar blok4 - zie WisselAdressenOpPad hierboven voor de
                // volledige toelichting): als het NIEUWE, proactief te zetten pad hieronder een
                // gewone wissel of kruiswissel deelt met het pad dat de trein LETTERLIJK zojuist
                // nog gebruikte om bij volgendBlok aan te komen, dan zou die wissel hier
                // omgezet worden terwijl de trein er fysiek nog (deels) op kan staan. Sla de
                // proactieve zetting dan over - de normale, latere candidate-keuze in
                // AutomatischeStapProberen zet 'm alsnog, op het moment dat de trein al
                // verder van de wissel af is.
                var wisselAdressenAankomst = huidig != null ? WisselAdressenOpPad(huidig, volgendBlok) : new List<int>();
                var kruiswisselAdressenAankomst = huidig != null ? KruiswisselRijAdressenOpPad(huidig, volgendBlok) : new List<int>();
                var wisselAdressenVoorLog = WisselAdressenOpPad(volgendBlok, vroegeKandidaten[0]);
                var kruiswisselAdressenVoorLog = KruiswisselRijAdressenOpPad(volgendBlok, vroegeKandidaten[0]);
                bool deeltWisselMetZojuistAfgelegdPad = wisselAdressenAankomst.Intersect(wisselAdressenVoorLog).Any()
                    || kruiswisselAdressenAankomst.Intersect(kruiswisselAdressenVoorLog).Any();

                if (deeltWisselMetZojuistAfgelegdPad)
                {
                    Log($"[Route '{trein.Route.Omschrijving}'] Wissels naar blok {vroegeKandidaten[0].Nummer} NIET alvast gezet - dat pad deelt een wissel/kruiswissel met het pad waarmee de trein zojuist bij blok {volgendBlok.Nummer} is aangekomen, en die zou dus onder de (nog niet volledig vrije) trein omgezet worden. Wordt alsnog gezet zodra de gewone stap-logica dit later oppakt.");
                }
                else
                {
                    ZetWisselsTussenBlokken(volgendBlok, vroegeKandidaten[0]);
                    if (trein.Trein != null && trein.Trein.DecoderAdres > 0)
                    {
                        bool vooruitVoorVroegeWissel = trein.Trein.OmgekeerdeRijrichting ? !trein.RijdtVooruit : trein.RijdtVooruit;
                        foreach (int kruiswisselAdres in kruiswisselAdressenVoorLog)
                            _hardwareBeheerder.StuurLocSnelheidCommando(trein.Trein.DecoderAdres, trein.HuidigeSnelheidStap, vooruitVoorVroegeWissel, kruiswisselAdres, trein.Trein.DecoderStappen);
                    }
                    // GEBRUIKERSVERZOEK ("kan je voortaan ook de rij-stroomnummers en de
                    // bloknummers in de log zetten, dat is heel handig, minder misverstanden") -
                    // noemt nu expliciet het rijstroom-adres van een eventuele kruiswissel op
                    // dit pad, zodat direct duidelijk is of (en welk) los rijstroom-adres hier
                    // wordt meegestuurd - dit was precies de bron van eerdere verwarring tussen
                    // een meldernummer en een rijstroom-adres die toevallig hetzelfde cijfer
                    // hadden (melder 8 versus rijstroom-adres 8).
                    string kruiswisselDeel = kruiswisselAdressenVoorLog.Count > 0
                        ? $" (incl. rijstroom-adres {string.Join("/", kruiswisselAdressenVoorLog)} voor de kruiswissel-sectie)"
                        : "";
                    Log($"[Route '{trein.Route.Omschrijving}'] Wissels naar blok {vroegeKandidaten[0].Nummer} alvast gezet, vooruitlopend op de echte stopmelder van blok {volgendBlok.Nummer}{kruiswisselDeel} - er is maar één mogelijke vervolgstap, dit voorkomt dat de trein de wissel bereikt vóórdat die de juiste stand heeft.");
                }
            }
            trein.WachtOpMeldernNummer = stopMelderVoorKeren!.MeldernNummer;
            trein.WachtBegonnenOp = DateTime.Now;
            // GEVONDEN GAT (gebruikerswaarneming: "trein stopt na enige tijd nadat hij in
            // blok 6 de weg kwijtraakt" - bevestigd via de nieuwe diagnostische logregel:
            // WachtOpMeldernNummer stond op 26, een melder die helemaal niet bij blok6
            // hoort): deze nieuwe wachtperiode (voor de STOPMELDER, na de aankomst-
            // bevestiging hierboven) wiste GeobserveerdeMeldersTijdensWachten niet - in
            // tegenstelling tot AutomatischeStapProberen, die dat WEL doet aan het begin
            // van de (eerdere) wachtperiode voor de aankomstmelder. Wat er nog in de lijst
            // stond van die eerdere fase (of, bij "vroege aankomst", van een heel andere
            // blok-context) bleef zo onterecht meetellen zodra LeerBezetmelderVolgorde
            // hierna de zelflerende volgorde bijwerkt - en een automatisch geleerde entry
            // (zoals blok6's "Uit blok5") werd zo stilzwijgend verontreinigd met een melder
            // die er niets mee te maken had.
            trein.GeobserveerdeMeldersTijdensWachten.Clear();
            trein.WachtOpStopmelderVoorKerenWachttijd = wachttijdNaAankomst;
            if (_hardwareBeheerder.Huidige is DinamoHardware)
                _hardwareBeheerder.VraagMelderStatusOp(stopMelderVoorKeren.MeldernNummer);

            double? geleerdeReistijdTotStop = _blokBeheerder.GeefGeleerdeReistijd(volgendBlok, huidig, trein.Trein);
            double stopWachtSeconden = geleerdeReistijdTotStop.HasValue
                ? Math.Max(geleerdeReistijdTotStop.Value * GeleerdeReistijdVastlopenMarge, 30.0)
                : OnbekendeReistijdVastlopenSeconden;
            trein.HuidigeWachtIntervalSeconden = stopWachtSeconden;
            var stopTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(stopWachtSeconden) };
            stopTimer.Tick += (_, _) => MeldTreinVastgelopen(trein, stopMelderVoorKeren.MeldernNummer, stopWachtSeconden);
            trein.Timer = stopTimer;
            stopTimer.Start();
            return;
        }
        PlanVolgendeAutomatischeStap(trein, wachttijdNaAankomst);
    }

    /// <summary>Rondt een rit af (vaste route óf automatisch): parkeert het eindblok
    /// bewust (net als Opstellen) als IsOpstelrit of BlijfWachtenInBestemming aanstaat,
    /// anders een gewone StopTrein. Gedeeld tussen PlanVolgendeGebeurtenis (vaste routes)
    /// en AutomatischeStapProberen (automatisch rijden), die dit voorheen niet consistent
    /// deden - bij automatisch rijden werd het eindblok altijd losgelaten, ook als "Blijf
    /// wachten in bestemming" aanstond.</summary>
    private void RondRitAf(RijdendeTrein trein, Blok eindblok, string aankomstBericht)
    {
        if (trein.IsOpstelrit || trein.Route.BlijfWachtenInBestemming)
        {
            trein.Timer?.Stop();
            trein.Timer = null;
            _actieveTreinen.Remove(trein);
            string opstelBericht = trein.IsOpstelrit
                ? $"Trein opgesteld in blok {eindblok.Nummer} (opstelspoor) — blijft geparkeerd tot je opnieuw met deze route rijdt."
                : $"Trein blijft wachten in bestemming (blok {eindblok.Nummer}) — blijft daar staan tot je opnieuw met deze route rijdt.";
            StatusTekst.Text = opstelBericht;
            Log($"--- {opstelBericht} ---");
            Redraw();
        }
        else
        {
            StopTrein(trein, aankomstBericht);
        }
    }

    /// <summary>Logt de stapsgewijze snelheidsopbouw/-afbouw zoals de echte Koploper doet
    /// ("laat iedere seconde de snelheid van die trein met 1 stap toenemen tot dat de
    /// toegestane snelheid is bereikt... komt de trein in het doelblok, dan laat Koploper
    /// de trein op dezelfde manier afremmen") - puur zichtbaarheid/logging, de daadwerkelijke
    /// timing wordt al elders geregeld (BouwGebeurtenissen/AutomatischeStapSeconden).
    /// Rangeren (massasimulatie uit) laat de trein bewust direct op snelheid staan.</summary>
    /// <summary>Naast de al bestaande logregel/GUI-snelheidsweergave hieronder: stuurt nu
    /// ook DAADWERKELIJK een snelheidscommando naar de hardware - dit ONTBRAK LANGE TIJD
    /// VOLLEDIG (gebruikersmelding: "ik zie geen commando's naar de loc"): de route-
    /// simulatie hield alleen tijd/bezetting bij, zonder de fysieke loc ooit ook echt aan
    /// te sturen (in tegenstelling tot de losse "Handmatig rijden"-slider, die dit al wel
    /// deed). Deze methode is de EXACTE plek waar zowel vaste als automatische routes al
    /// hun snelheids-momenten aan doorgeven, dus dat is nu ook de plek waar de hardware
    /// het commando krijgt - geen aparte, dubbele aanroepen nodig bij elke Log-plek.</summary>
    /// <summary>GEVONDEN, ARCHITECTUREEL GAT (gebruikerswaarneming: loc bleef stilstaan
    /// PRECIES op een kruiswissel-sectie die elektrisch geïsoleerd is van alle aangrenzende
    /// blokken - zie Model.Kruiswissel.RijAdres): bepaalt of er, tussen van en naar, een
    /// Kruiswissel met een EIGEN, apart ingesteld rijstroom-adres ligt - zo ja, dan moet elk
    /// snelheidscommando voor dit traject ook naar DIE sectie gestuurd worden, naast de
    /// twee aangrenzende blokken zelf. Kijkt eerst naar een eventuele Wisselstraat (dezelfde
    /// voorkeursvolgorde als overal elders: expliciet boven automatisch gezocht), anders
    /// naar de gewone padzoekfunctie. Retourneert een lege lijst als er geen kruiswissel op
    /// het pad ligt, of als de kruiswissel(s) die er wel liggen geen apart adres nodig
    /// hebben (RijAdres=0, de standaard - onschadelijk, want dan verandert er niets aan het
    /// bestaande gedrag).</summary>
    /// <summary>GEVONDEN, STRUCTUREEL GAT (bytes-analyse: het projectbestand toonde
    /// RijAdres=8 bij de kruiswissel in Symbolen, maar de software bleef toch naar adres 24
    /// sturen): een Wisselstraat bewaart zijn EIGEN kopie van de kruiswissel-gegevens
    /// (Kruiswisselstanden[].Kruiswissel) - bij het opslaan/laden van een project bleek dit
    /// een aparte, aan het zicht onttrokken KOPIE te worden in plaats van een verwijzing
    /// naar hetzelfde object als in Symbolen. Een latere wijziging via het bouwscherm
    /// (Shift+dubbelklik) werkt dus alleen de Symbolen-kopie bij; de Wisselstraat-kopie
    /// bleef stilzwijgend verouderd. Zoekt daarom altijd de ACTUELE, levende Kruiswissel in
    /// Symbolen op (via Adres/Adres2, die samen een kruiswissel uniek identificeren) in
    /// plaats van blind op de kopie in de Wisselstraat te vertrouwen - valt terug op de
    /// meegegeven kopie zelf als er om wat voor reden dan ook geen match gevonden wordt.</summary>
    private Kruiswissel ActueleKruiswissel(Kruiswissel mogelijkVerouderd) =>
        _baanBeheerder.Symbolen.OfType<Kruiswissel>().FirstOrDefault(k => k.Adres == mogelijkVerouderd.Adres && k.Adres2 == mogelijkVerouderd.Adres2) ?? mogelijkVerouderd;

    /// <summary>GEVONDEN, ZELFDE GAT ALS ActueleKruiswissel HIERBOVEN, nu bevestigd voor een
    /// GEWONE wissel (gebruikerswaarneming: "op wissel 5 rijdt de trein rechtdoor, zoals ook
    /// moet, maar op het scherm wordt hij nog steeds als afbuigend getekend" - het
    /// hardwarecommando ging dus wel naar het juiste adres, dus de fysieke stand kwam goed;
    /// alleen de op het SCHERM getoonde Wissel-instantie bleef achter): een Wisselstraat
    /// bewaart net als bij Kruiswissel zijn eigen kopie van elke wissel (WisselInWisselstraat.
    /// Wissel) - bij het opslaan/laden van een project (of soms al eerder, afhankelijk van de
    /// volgorde waarin objecten voor het eerst geserialiseerd worden) kan dit een aparte kopie
    /// worden in plaats van een verwijzing naar hetzelfde object als in Symbolen. Deze
    /// methode zoekt daarom, net als bij een Kruiswissel, altijd de ACTUELE, levende Wissel in
    /// Symbolen op via het Adres (dat is uniek per wissel) - dat is het object dat ook
    /// daadwerkelijk getekend wordt - in plaats van blind op de kopie in de Wisselstraat te
    /// vertrouwen. Valt terug op de meegegeven kopie zelf als er om wat voor reden dan ook
    /// geen match gevonden wordt.</summary>
    private Wissel ActueleWissel(Wissel mogelijkVerouderd) =>
        _baanBeheerder.Symbolen.OfType<Wissel>().FirstOrDefault(w => w.Adres == mogelijkVerouderd.Adres) ?? mogelijkVerouderd;

    private List<int> KruiswisselRijAdressenOpPad(Blok van, Blok naar)
    {
        var wisselstraat = _wisselstraatBeheerder.WisselstratenTussen(van, naar).FirstOrDefault();
        if (wisselstraat != null)
            return wisselstraat.Kruiswisselstanden.Select(k => ActueleKruiswissel(k.Kruiswissel).RijAdres).Where(a => a > 0).Distinct().ToList();

        var resultaat = _baanBeheerder.VindPadMetWisselstanden(van, naar);
        if (resultaat is null) return new List<int>();
        return resultaat.Value.Kruiswisselstanden.Select(k => ActueleKruiswissel(k.Kruiswissel).RijAdres).Where(a => a > 0).Distinct().ToList();
    }

    /// <summary>GEVONDEN, ARCHITECTUREEL GAT (09:51-09:54-testrit: trein reed van blok5 terug
    /// naar blok4 - een echte Keer-relatie, zie BlokRelatie(Van=5,Naar=4).Keer - en zakte daar
    /// stil, "melder 15 niet ontvangen binnen 30 sec", TERWIJL de log liet zien dat de
    /// proactieve wisselzetting hierboven (vroegeKandidaten.Count==1) precies 1,4-1,8 sec na de
    /// bevestigde aankomstmelding (melder 8) al wissel 14 omgooide naar de volgende stap
    /// (blok4->blok3). Wissel 14 is echter DEZELFDE fysieke wissel als die de trein een
    /// paar honderd milliseconden eerder zelf nog gebruikte om van blok5 náár blok4 te komen
    /// (bevestigd door de gebruiker: "tussen blok5 en blok4 zit fysiek een wissel en wel
    /// wissel 14") - de raw hardwarelog laat na die omzetting GEEN ENKELE bezetmelding meer
    /// zien (niet alleen de verwachte melder 15, ook niet de tussenliggende melder 16), tot aan
    /// de geforceerde stop 28 sec later. Dat past bij een wissel die onder een nog rijdende
    /// (of net gepasseerde, nog niet volledig vrije) trein wordt omgezet - met een kans op een
    /// ontspoorde as op de tongen, of op een kortstondige stroomonderbreking van de
    /// kruiswissel-sectie zelf, in beide gevallen genoeg om alle verdere meldingen stil te
    /// leggen. Bepaalt daarom welke gewone wissel-adressen op het ZOJUIST afgelegde pad
    /// (huidig->volgendBlok) lagen, ter vergelijking met het NIEUWE, proactief te zetten pad
    /// hieronder.</summary>
    private List<int> WisselAdressenOpPad(Blok van, Blok naar)
    {
        var wisselstraat = _wisselstraatBeheerder.WisselstratenTussen(van, naar).FirstOrDefault();
        if (wisselstraat != null)
            return wisselstraat.Wissels.Select(w => ActueleWissel(w.Wissel).Adres).Where(a => a > 0).Distinct().ToList();

        var resultaat = _baanBeheerder.VindPadMetWisselstanden(van, naar);
        if (resultaat is null) return new List<int>();
        return resultaat.Value.Wisselstanden.Select(w => ActueleWissel(w.Wissel).Adres).Where(a => a > 0).Distinct().ToList();
    }

    /// <summary>Alle apart ingestelde kruiswissel-rijstroom-adressen in het hele project
    /// (RijAdres &gt; 0), ongeacht welk specifiek pad er nu bereden wordt - gebruikt door de
    /// diverse veiligheids-broadcasts (verbinden, noodstop, vastgelopen, richting-correctie)
    /// die toch al naar ELK blok sturen: zo'n commando is onschadelijk voor een kruiswissel
    /// waar de trein niet op staat, maar garandeert dat een trein die daar WEL op staat
    /// hoe dan ook bereikt wordt.</summary>
    private List<int> AlleKruiswisselRijAdressen() =>
        _baanBeheerder.Symbolen.OfType<Kruiswissel>().Select(k => k.RijAdres).Where(a => a > 0).Distinct().ToList();

    /// <summary>Stuurt een REALISTISCHE, geleidelijke snelheidsopbouw/-afbouw naar de
    /// hardware, i.p.v. één directe sprong - gebruikt de werkelijke bloklengte (Blok.
    /// LengteCm) om een REDELIJKE ramp-duur te bepalen (geen exacte natuurkundige
    /// berekening, gewoon een praktische aanname: hoe langer het blok, hoe meer tijd voor
    /// een nette opbouw). EERDERE BEPERKING (gebruikerscorrectie): snelheid sprong altijd
    /// direct van de volle stap naar 0 of andersom, ook bij het keren.
    /// Breekt een eventueel AL LOPENDE ramp voor deze trein eerst netjes af, zodat twee
    /// ramps elkaar nooit tegenwerken (bijv. een keer-stop vlak na een net gestart optrekken).</summary>
    /// <summary>Stuurt een geleidelijke snelheids-ramp naar de hardware.
    /// <paramref name="voorbezetBlok"/>: GEVONDEN, KRITIEK GAT (gebruikerswaarneming: "zodra
    /// de loc in blok 10 is en blok 11 geheel verlaten heeft staat hij al stil"): elk
    /// Dinamo-blok is een eigen, elektrisch gescheiden rijstroom-sectie met een eigen
    /// zender (zie ook RijdendeTrein.LaatstGebruikteBlokVoorSnelheid hierboven) - het
    /// vertrekcommando werd tot nu toe ALLEEN via het HUIDIGE blok verstuurd, omdat de
    /// software de aankomst bij het volgende blok nog niet bevestigd had (en dus, terecht,
    /// nog niet dacht "ik ben daar al"). Zodra de loc echter fysiek de grens oversteekt
    /// vóórdat die bevestiging er is - heel normaal, de trein wacht niet op de software -
    /// bereikt het huidige-blok-commando hem niet meer, en het NIEUWE blok heeft nog nooit
    /// een rijopdracht gekregen: de loc komt dan vanzelf, stilzwijgend tot stilstand, exact
    /// op de blokgrens. Daarom wordt de HELE ramp nu ook naar dit (nog niet bevestigde)
    /// volgende blok "voorbezet" - net als hoe echte DCC-blokbesturingssystemen de
    /// eerstvolgende sectie al vooraf met dezelfde snelheid aansturen, zodat er nooit een
    /// gat in de aansturing zit tijdens de overgang zelf. Optioneel (null = geen).</summary>
    private void StuurSnelheidNaarHardware(RijdendeTrein trein, int doelStap, double bloklengteCm, Blok? voorbezetBlok = null)
    {
        trein.ActieveSnelheidsRampTimer?.Stop();
        trein.ActieveSnelheidsRampTimer = null;

        if (trein.Trein is null || trein.Trein.DecoderAdres <= 0) return;
        int decoderAdres = trein.Trein.DecoderAdres;
        int stappen = trein.Trein.DecoderStappen;
        bool vooruit = trein.Trein.OmgekeerdeRijrichting ? !trein.RijdtVooruit : trein.RijdtVooruit; // zie Trein.OmgekeerdeRijrichting
        int blokNummer = trein.HuidigBlok?.Nummer ?? 0;
        int? voorbezetBlokNummer = voorbezetBlok?.Nummer;
        int vanStap = trein.HuidigeSnelheidStap;
        var kruiswisselAdressen = (voorbezetBlok != null && trein.HuidigBlok != null)
            ? KruiswisselRijAdressenOpPad(trein.HuidigBlok, voorbezetBlok)
            : new List<int>();

        // Zie RijdendeTrein.LaatstGebruikteBlokVoorSnelheid: een blokwissel dwingt ALTIJD
        // een vers commando af, ook als de snelheid zelf niet verandert - anders blijft de
        // loc in het nieuwe blok stilstaan omdat DAT blok zijn eigen zender nog nooit een
        // opdracht kreeg. Is er een voorbezetBlok opgegeven, dan is er hoe dan ook iets te
        // versturen (naar dat blok, ongeacht of de snelheid zelf verandert).
        bool blokVeranderd = blokNummer != trein.LaatstGebruikteBlokVoorSnelheid;
        if (vanStap == doelStap && !blokVeranderd && voorbezetBlokNummer is null) return; // echt niets te doen

        void StuurNaarBeideBlokken(int stap)
        {
            _hardwareBeheerder.StuurLocSnelheidCommando(decoderAdres, stap, vooruit, blokNummer, stappen);
            if (voorbezetBlokNummer.HasValue)
                _hardwareBeheerder.StuurLocSnelheidCommando(decoderAdres, stap, vooruit, voorbezetBlokNummer.Value, stappen);
            foreach (int kruiswisselAdres in kruiswisselAdressen)
                _hardwareBeheerder.StuurLocSnelheidCommando(decoderAdres, stap, vooruit, kruiswisselAdres, stappen);
            trein.LaatstGebruikteBlokVoorSnelheid = blokNummer;
        }

        if (vanStap == doelStap)
        {
            // Geen ramp nodig (de snelheid zelf verandert niet) - wel één enkel
            // "hervestigings"-commando naar het huidige blok (en, indien opgegeven, alvast
            // naar het voorbezette volgende blok), anders blijft de loc daar stilstaan
            // ondanks een intern "correcte" HuidigeSnelheidStap.
            StuurNaarBeideBlokken(doelStap);
            return;
        }

        int richting = doelStap > vanStap ? 1 : -1;
        int aantalStappen = Math.Abs(doelStap - vanStap);
        // 1 seconde per 50cm bloklengte, met een ondergrens/bovengrens zodat het nooit te
        // snel (onrealistisch abrupt) of te traag (onwerkbaar bij het testen) aanvoelt.
        // Bloklengte 0 (niet ingevuld) = onbekend, dan een neutrale, vaste aanname.
        double totaleDuurSeconden = bloklengteCm > 0 ? Math.Clamp(bloklengteCm / 50.0, 1.0, 8.0) : 2.0;
        // GEVONDEN, STRUCTUREEL GAT (gebruikersverzoek: "controleer alle wachtrijen heel
        // goed en simuleer het zelf" - n.a.v. herhaalde "trein valt stil"/"melder niet
        // ontvangen binnen 30 sec"-meldingen): de ondergrens van 0,1 sec hieronder bepaalt
        // hoe vaak StuurNaarBeideBlokken hierboven vuurt - en die stuurt per tik 1 tot 3
        // aparte snelheidscommando's (huidig blok + evt. voorbezet blok + evt. kruiswissel-
        // rijadressen). Bij een KORT blok met VEEL stappen (bijv. 14 stappen, bloklengte
        // geclamped op het 1-sec-minimum) kwam intervalSeconden hierdoor op 1/14≈0,07 sec,
        // oftewel tot 3/0,07≈43 commando's/sec vanuit ÉÉN enkele rijdende trein - TERWIJL
        // Dinamo se eigen seriële zendcyclus (DinamoHardware._stuurTimer) hooguit 1
        // datagram/200ms (5/sec) verwerkt, VOOR AL het verkeer samen (snelheid, melder-
        // status, wissels). Een simulatie van de volledige wachtrij-keten bevestigt: zo'n
        // piek loopt de niet-prioritaire _teVersturen-wachtrij onvermijdelijk op, met als
        // zichtbaar gevolg precies de waargenomen "stappen van 2 seconden i.p.v. 100-200ms"
        // vertraging tot de trein feitelijk stilvalt. Fix: de ondergrens schaalt nu mee met
        // het aantal doelen dat per tik daadwerkelijk verstuurd wordt, zodat het totale
        // aantal commando's/sec vanuit déze ene ramp nooit boven een veilig budget kan
        // uitkomen - in plaats van de belasting pas ACHTERAF, in een al overvolle wachtrij,
        // te proberen op te lossen. EERSTE POGING (budget 3,5/sec) bleek, samen met de
        // melderstatus-navraag (zie MainWindow._melderHerbevestigingsTimer) opgeteld, zelf
        // alweer boven de 5/sec-hardwaregrens uit te komen (simulatie: queue_sim2.py) -
        // beide budgetten moeten SAMEN onder die grens blijven, niet elk afzonderlijk. Met
        // de melderstatus-navraag op 600ms/melder (~1,67/sec) blijft er ~3,3/sec over;
        // 2,2/sec hier gekozen voor ruime marge (gezamenlijk ~3,9/sec, nog altijd ruim onder
        // de 5/sec-grens) - bevestigd stabiel (wachtrij loopt NOOIT op) in een 1 uur durende
        // worst-case simulatie (continu kortste-blok-met-meeste-stappen-geval, zonder pauze)
        // MET ÉÉN trein. TWEEDE GEVONDEN GAT bij diezelfde simulatie: met 2 of meer treinen
        // die TEGELIJK, continu zo'n kort-blok-veel-stappen-ramp draaien, telt elke ramp zijn
        // EIGEN budget onafhankelijk - samen ALSNOG boven de 5/sec-grens, en de wachtrij
        // liep dan (simulatie) weer onbeperkt op, nu pas na een paar minuten in plaats van
        // meteen. Het budget hieronder is daarom nu een GEDEELD, systeembreed budget, geen
        // vast getal per trein: gedeeld door het aantal op dit moment daadwerkelijk actieve
        // treinen (_actieveTreinen), zodat 1 trein het volledige budget krijgt, 2 treinen elk
        // de helft, enzovoort - het totaal blijft zo altijd onder de grens, ongeacht hoeveel
        // treinen er tegelijk rijden. Opnieuw bevestigd stabiel (tot en met 3 treinen,
        // 1 uur worst-case) via dezelfde simulatie.
        int aantalDoelenPerTik = 1 + (voorbezetBlokNummer.HasValue ? 1 : 0) + kruiswisselAdressen.Count;
        int aantalActieveTreinen = Math.Max(1, _actieveTreinen.Count(t => t.ActieveSnelheidsRampTimer != null) + 1); // +1: deze ramp zelf telt nog niet mee in _actieveTreinen totdat hij hieronder gestart wordt
        double minimumIntervalSeconden = aantalDoelenPerTik / (2.2 / aantalActieveTreinen);
        double intervalSeconden = Math.Max(minimumIntervalSeconden, totaleDuurSeconden / aantalStappen) * SimulatieInstellingen.VertragingsFactor;

        int huidigeStap = vanStap;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(intervalSeconden) };
        timer.Tick += (_, _) =>
        {
            if (_venstergesloten) { timer.Stop(); return; }
            huidigeStap += richting;
            trein.HuidigeSnelheidStap = huidigeStap;
            StuurNaarBeideBlokken(huidigeStap);
            if (huidigeStap == doelStap)
            {
                timer.Stop();
                trein.ActieveSnelheidsRampTimer = null;
            }
        };
        trein.ActieveSnelheidsRampTimer = timer;
        timer.Start();
    }

    private void LogSnelheidsopbouw(RijdendeTrein trein, bool optrekken, Blok? relevantBlok, Blok? voorbezetBlok = null)
    {
        // Live snelheid bijhouden voor het "Overzicht locomotieven"-venster - los van de
        // Rangeren/doelSnelheid-checks hieronder (die zijn puur voor de logregel), dus
        // vóór de vroege returns.
        if (trein.Trein != null)
            _treinBeheerder.ZetSnelheid(trein.Trein, optrekken ? (trein.EffectiefTreintype?.GemiddeldeSnelheid ?? 0) : 0);

        // Het daadwerkelijke hardware-commando (nu via een geleidelijke ramp, zie
        // StuurSnelheidNaarHardware) - ALTIJD versturen zolang er een decoderadres bekend
        // is. Zonder decoderadres wordt dit duidelijk gelogd i.p.v. stilzwijgend
        // overgeslagen - anders lijkt het net alsof er iets stuk is, terwijl het gewoon een
        // ontbrekende instelling betreft (zie Treinen beheren -> Decoderadres).
        if (trein.Trein != null)
        {
            if (trein.Trein.DecoderAdres > 0)
            {
                int doelStap = optrekken ? BepaalDecoderStapVoorSnelheid(trein.Trein, trein.EffectiefTreintype?.GemiddeldeSnelheid ?? 50) : 0;
                StuurSnelheidNaarHardware(trein, doelStap, relevantBlok?.LengteCm ?? 0, voorbezetBlok);
            }
            else
            {
                Log($"[Route '{trein.Route.Omschrijving}'] Geen snelheidscommando verstuurd voor '{trein.Trein.Omschrijving}': geen decoderadres ingesteld (zie Treinen beheren).");
            }
        }

        if (trein.Trein is null || trein.EffectiefTreintype is null) return;
        double doelSnelheid = trein.EffectiefTreintype.GemiddeldeSnelheid;
        if (doelSnelheid <= 0) return;

        if (trein.Trein.Rangeren)
        {
            Log($"[Route '{trein.Route.Omschrijving}'] Rangeren: {(optrekken ? "direct op snelheid" : "direct stilstaand")}, {doelSnelheid:0} km/u (geen massasimulatie).");
            return;
        }

        const int stappen = 4;
        var stapWaarden = new List<string>();
        for (int s = 1; s <= stappen; s++)
        {
            double snelheid = optrekken ? doelSnelheid * s / stappen : doelSnelheid * (stappen - s) / stappen;
            stapWaarden.Add($"{snelheid:0} km/u");
        }
        Log($"[Route '{trein.Route.Omschrijving}'] Snelheid stapsgewijs {(optrekken ? "opgebouwd" : "afgebouwd")}: {string.Join(" -> ", stapWaarden)}.");
    }

    /// <summary>Wat er vroeger de "veiligheids-timeout" heette, GEEN gesimuleerde
    /// voortgang meer, maar puur "hoe lang wachten we op een echte bezetmelding voordat we
    /// het als vastgelopen beschouwen" - zie MeldTreinVastgelopen. Zodra er minstens één
    /// ECHTE meting voor het doelblok is (Blok.GeleerdeReistijden), is DIE (met een
    /// marge) de basis; de allereerste keer dat een blok bereden wordt (nog geen meting)
    /// een generieke, ruime, vaste waarde.</summary>
    private const double GeleerdeReistijdVastlopenMarge = 2.5;
    private const double OnbekendeReistijdVastlopenSeconden = 30.0;

    /// <summary>GEVONDEN, FUNDAMENTELE GEDRAGSFOUT (gebruikerscorrectie: "de veiligheids-
    /// timeout wil ik er echt uit hebben, dit is niet realistisch als de loc niet fysiek
    /// verder gaat - het scherm moet niet de indruk wekken dat hij wel rijdt, er moet
    /// gewoon een foutmelding komen: trein X staat stil in blok Y"): dit vervangt het oude
    /// "ga toch verder na de timeout"-gedrag volledig. Zet trein.Vastgelopen, stopt de
    /// fysieke loc expliciet (veiligheid: de software weet vanaf hier niet meer zeker waar
    /// de loc daadwerkelijk is, dus niet laten doorrijden naar een onbekende bestemming),
    /// toont een blijvende foutmelding (hetzelfde Foutmelding-mechanisme als een
    /// spookmelding - paars, knippert, moet je zelf opheffen of de melding komt alsnog
    /// binnen) en PAUZEERT de rit volledig: geen enkel blok wordt verder als bezet
    /// aangenomen, geen volgende stap wordt gepland. trein.WachtOpMeldernNummer/
    /// WachtBegonnenOp blijven BEWUST staan (niet gewist) - komt de melding alsnog (al is
    /// het laat) binnen, dan pakt Bezetmelding_VanHardware de rit gewoon weer op en heft de
    /// foutmelding zelf op.</summary>
    private void MeldTreinVastgelopen(RijdendeTrein trein, int meldernummer, double gewachtSeconden)
    {
        trein.Timer?.Stop();
        // GEVONDEN, ACUUT VEILIGHEIDSGAT (gebruikerswaarneming: "trein valt stil... terwijl
        // alle wissels al goed staan", plus rechtstreeks bewijs uit een hardware-log: alle
        // bezetmeldingen liepen gewoon door tot en met de stopmelder van het BLOK ERNA, deze
        // timer vuurde pas NADAT de trein al lang verder was - waarschijnlijk omdat een
        // binnenkomende melder om een nog onbekende reden niet gematcht werd tegen
        // trein.WachtOpMeldernNummer, zie Bezetmelding_VanHardware). Voordat de precieze
        // oorzaak DAARVAN gevonden is, moet dit vangnet in elk geval voorkomen dat een
        // AANTOONBAAR verder gereden trein alsnog een fysiek stopcommando krijgt: als het
        // blok waar de trein volgens de software nog in staat via de GEWONE, aparte
        // bezet-boekhouding (StartStaartFase/BeeindigStaartFase - onafhankelijk van
        // WachtOpMeldernNummer) inmiddels al als vrij geboekt staat, is dat afdoende bewijs
        // dat de trein al verder is - dan NIET stoppen, alleen loggen en de rit laten
        // doorlopen (de eerstvolgende ECHTE melding pakt de rest vanzelf weer op).
        if (trein.HuidigBlok != null && !_blokBeheerder.IsBezet(trein.HuidigBlok))
        {
            Log($"[Route '{trein.Route.Omschrijving}'] Verlopen veiligheidswachttijd voor melder {meldernummer} genegeerd: blok {trein.HuidigBlok.Nummer} staat via de gewone bezet-boekhouding al weer vrij, dus de trein is duidelijk al verder gereden (vermoedelijk een melder die niet correct werd herkend als routevoortgang) - GEEN stopcommando gestuurd, rit loopt door.");
            return;
        }
        trein.Vastgelopen = true;

        if (trein.Trein != null && trein.Trein.DecoderAdres > 0 && trein.HuidigBlok != null)
        {
            trein.ActieveSnelheidsRampTimer?.Stop();
            trein.ActieveSnelheidsRampTimer = null;
            bool vooruitVoorHardware = trein.Trein.OmgekeerdeRijrichting ? !trein.RijdtVooruit : trein.RijdtVooruit;
            // GEVONDEN VEILIGHEIDSGAT (gebruikerswaarneming: "loc krijgt het heen en weer
            // tussen blok 4 en blok 3" - de trein bleef fysiek doorrijden NA een
            // vastgelopen-melding): dit stuurde het stopcommando voorheen ALLEEN naar
            // trein.HuidigBlok (de laatst BEVESTIGDE positie) - maar bij het voorbezetten
            // (zie AutomatischeStapProberen) krijgt het VOLGENDE blok AL hetzelfde
            // snelheidscommando, vóórdat de aankomst daar bevestigd is. Blijft die
            // bevestiging uit (precies de situatie die tot deze vastgelopen-melding leidt),
            // dan bleef de trein gewoon doorrollen op het adres van dat voorbezette,
            // nooit-bevestigde blok - het stopcommando bereikte hem daar nooit. Zelfde
            // oplossing als bij MainWindow.StopAlleBekendeGeplaatsteLocs: stuur het
            // stopcommando naar ELK blok in het project, ongeacht waar de software denkt
            // dat de trein staat - onschadelijk voor blokken waar hij niet is, maar
            // garandeert dat hij hoe dan ook stopt.
            foreach (var blok in _blokBeheerder.Blokken)
                _hardwareBeheerder.StuurLocSnelheidCommando(trein.Trein.DecoderAdres, 0, vooruitVoorHardware, blok.Nummer, trein.Trein.DecoderStappen);
            foreach (int kruiswisselAdres in AlleKruiswisselRijAdressen())
                _hardwareBeheerder.StuurLocSnelheidCommando(trein.Trein.DecoderAdres, 0, vooruitVoorHardware, kruiswisselAdres, trein.Trein.DecoderStappen);
            trein.LaatstGebruikteBlokVoorSnelheid = trein.HuidigBlok.Nummer; // zie RijdendeTrein.LaatstGebruikteBlokVoorSnelheid
            trein.HuidigeSnelheidStap = 0;
        }

        string blokTekst = trein.HuidigBlok != null ? $"blok {trein.HuidigBlok.Nummer}" : "een onbekend blok";
        string bericht = $"FOUT: trein '{trein.Trein?.Omschrijving}' staat stil in {blokTekst} - melder {meldernummer} niet ontvangen binnen {gewachtSeconden:0} sec. Rit gepauzeerd, geen blok verder als bezet aangenomen. Controleer de loc/bezetmelder/bekabeling.";
        Log($"[Route '{trein.Route.Omschrijving}'] {bericht}");
        StatusTekst.Text = bericht;
        if (trein.HuidigBlok != null) _blokBeheerder.ZetFoutmelding(trein.HuidigBlok, true);
        Redraw();
    }

    private void PlanVolgendeGebeurtenis(RijdendeTrein trein)
    {
        trein.Timer?.Stop();
        trein.WachtOpMeldernNummer = null;
        trein.WachtBegonnenOp = null;
        trein.GeobserveerdeMeldersTijdensWachten.Clear();

        if (trein.GebeurtenisIndex >= trein.Gebeurtenissen.Count)
        {
            var eindblok = trein.Pad[^1];
            RondRitAf(trein, eindblok, $"Aangekomen bij eindblok {eindblok.Nummer}.");
            return;
        }

        var (doelBlok, melder, wachttijd) = trein.Gebeurtenissen[trein.GebeurtenisIndex];

        // GEVONDEN, FUNDAMENTELER GAT (gebruikersmelding: "ik moet de loc eerst handmatig
        // laten rijden, dan komt hij pas in beweging" - zelfde kip-en-ei-vergrendeling als
        // bij vrij automatisch rijden, zie AutomatischeStapProberen voor de volledige
        // toelichting): het "optrekken"-commando stond voorheen pas in RijTimer_Tick, dat
        // pas aangeroepen wordt NADAT deze stap al bevestigd is (echte melding, of de
        // veiligheids-timeout) - de software wachtte dus op een bezetmelding van een blok
        // waar de loc nog nooit opdracht toe had gekregen om heen te rijden. Nu hier, DIRECT
        // bij het plannen van deze stap, al de opdracht om daadwerkelijk te gaan rijden.
        if (trein.HuidigBlok != null && trein.HuidigBlok != doelBlok)
        {
            // trein.VorigBlok wordt (bewust) alleen bij automatisch rijden bijgehouden -
            // voor een vaste route wordt het blok VÓÓR trein.HuidigBlok hier afgeleid uit
            // de al berekende Gebeurtenissen-lijst zelf (2 terug: index-1 is trein.HuidigBlok
            // zelf, index-2 is het blok daarvóór). GebeurtenisIndex is op dit punt nog niet
            // opgehoogd voor DEZE stap (dat gebeurt pas aan het eind van RijTimer_Tick),
            // dus dezelfde berekening als voorheen in RijTimer_Tick zelf.
            Blok? blokVoorHuidig = trein.GebeurtenisIndex >= 2 ? trein.Gebeurtenissen[trein.GebeurtenisIndex - 2].Blok : null;
            VerwerkEventueelKeren(trein, blokVoorHuidig, trein.HuidigBlok);
            LogLocFuncties(trein, LocGebeurtenis.VerlaatBlok);
            if (trein.HuidigBlok.Type == BlokType.Station) LogLocFuncties(trein, LocGebeurtenis.GaatWeerRijden);
            LogSnelheidsopbouw(trein, optrekken: true, trein.HuidigBlok, voorbezetBlok: doelBlok);

            // Bij een te lange trein voor het NIEUWE blok (VorigBlokLangerBezetHouden en/of
            // WisselstraatLangerBezetHouden, onafhankelijk aan te vinken) blijft het net
            // verlaten blok extra lang als staart bezet - de trein steekt er dan letterlijk
            // nog gedeeltelijk in.
            double staartDuur = StaartduurSeconden;
            if (trein.TeLangeBlokken.TryGetValue(doelBlok, out var actie) &&
                (actie.HasFlag(TeLangeTreinActie.VorigBlokLangerBezetHouden) || actie.HasFlag(TeLangeTreinActie.WisselstraatLangerBezetHouden)))
            {
                staartDuur *= 3;
                var redenen = new List<string>();
                if (actie.HasFlag(TeLangeTreinActie.VorigBlokLangerBezetHouden)) redenen.Add("vorig blok");
                if (actie.HasFlag(TeLangeTreinActie.WisselstraatLangerBezetHouden)) redenen.Add("wisselstraat");
                Log($"[Route '{trein.Route.Omschrijving}'] Blok {doelBlok.Nummer} te kort voor deze trein — {string.Join(" en ", redenen)} blijft langer bezet ({trein.HuidigBlok.Nummer}).");
            }
            // Koploper: "bij de stopmelder heb ik opgegeven dat het vorige blok
            // vrijgegeven mag worden" - staat deze vlag UIT op de melder die deze aankomst
            // meldt, dan blijft het vorige blok bewust bezet i.p.v. de gebruikelijke
            // korte staart-fase te doorlopen.
            if (melder == null || melder.MagVorigBlokVrijgeven)
                StartStaartFase(trein.HuidigBlok, staartDuur, BepaalVertrekmelder(trein.HuidigBlok, blokVoorHuidig, trein.Trein?.Lengte ?? 0));
            else
                Log($"[Route '{trein.Route.Omschrijving}'] Blok {trein.HuidigBlok.Nummer} blijft bewust bezet (melder {melder.MeldernNummer}: 'vorig blok vrijgeven' staat uit).");
        }

        // ECHTE hardware (geen Simulatie) EN een meldpunt met een geldig meldernummer:
        // deze stap wacht primair op Bezetmelding_VanHardware, de timer hieronder is dan
        // een veiligheids-timeout in plaats van de eigenlijke voortgangsbron.
        bool wachtOpEchteMelding = _hardwareBeheerder.Huidige is not SimulatieHardware && melder != null && melder.MeldernNummer > 0;
        trein.WachtOpMeldernNummer = wachtOpEchteMelding ? melder!.MeldernNummer : null;

        double intervalSeconden;
        if (wachtOpEchteMelding)
        {
            trein.WachtBegonnenOp = DateTime.Now;
            // Meteen de HUIDIGE stand opvragen i.p.v. alleen te wachten op een spontane
            // overgang - zie DinamoHardware.VraagMelderStatusOp voor de volledige
            // toelichting (dekt het geval dat de loc al precies op deze melder staat).
            if (_hardwareBeheerder.Huidige is DinamoHardware)
                _hardwareBeheerder.VraagMelderStatusOp(melder!.MeldernNummer);
            double? geleerdeReistijd = _blokBeheerder.GeefGeleerdeReistijd(doelBlok, trein.HuidigBlok, trein.Trein);
            intervalSeconden = geleerdeReistijd.HasValue
                ? Math.Max(geleerdeReistijd.Value * GeleerdeReistijdVastlopenMarge, 30.0)
                : OnbekendeReistijdVastlopenSeconden;
            trein.HuidigeWachtIntervalSeconden = intervalSeconden;
        }
        else
        {
            trein.HuidigeWachtIntervalSeconden = null;
            // Simulatiemodus, of dit blok heeft geen meldpunt: de gesimuleerde bloklengte/
            // snelheid-formule blijft hier de enige beschikbare bron (er is immers geen
            // enkele echte meting mogelijk zonder hardware).
            intervalSeconden = Math.Max(wachttijd, 0.1) * SimulatieInstellingen.VertragingsFactor;
        }

        trein.Timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(intervalSeconden) };
        trein.Timer.Tick += (_, _) =>
        {
            if (wachtOpEchteMelding)
                MeldTreinVastgelopen(trein, melder!.MeldernNummer, intervalSeconden);
            else
                RijTimer_Tick(trein);
        };
        trein.Timer.Start();
    }

    /// <summary>Reageert op een ECHTE bezetmelding van de hardware (zie IHardwareInterface.
    /// BezetmeldingGewijzigd) - vuurt, net als MainWindow.Bezetmelding_VanHardware, op de
    /// ACHTERGRONDTHREAD van de seriële poort, dus alles hier binnen Dispatcher.Invoke.
    /// Zoekt de rit die OP DIT MOMENT op precies dit meldernummer wacht (WachtOpMeldernNummer,
    /// gezet door PlanVolgendeGebeurtenis resp. AutomatischeStapProberen) en verwerkt de
    /// aankomst DIRECT, i.p.v. te wachten tot de gesimuleerde (veiligheids-timeout-)tijd
    /// verstrijkt - dit is de kern van "laat automatisch rijden op echte bezetmelders
    /// reageren i.p.v. puur op een interne klok". Werkt voor BEIDE automatisch-rijden-
    /// varianten: een vaste route (Gebeurtenissen, via RijTimer_Tick) en vrij automatisch
    /// rijden zonder vaste route (het al gekozen, gereserveerde volgende blok staat dan in
    /// trein.GereserveerdVolgendBlok, via VerwerkAutomatischeAankomst).
    /// Alleen bezet=true (aankomst) triggert een nieuwe stap; bezet=false (vertrek/
    /// vrijmelding) wordt hier gebruikt om een lopende staart-fase (zie StartStaartFase)
    /// daadwerkelijk, op basis van een ECHTE melding, af te ronden i.p.v. op een vaste,
    /// gesimuleerde duur te vertrouwen.
    ///
    /// LEREN (zie Blok.GeleerdeReistijden): de tijd tussen WachtBegonnenOp en dit
    /// moment is de daadwerkelijk gemeten reistijd naar het zojuist bereikte blok - wordt
    /// hier bijgewerkt VOORDAT de aankomst zelf verwerkt wordt, zodat een eventuele volgende
    /// stap (als die toevallig hetzelfde blok weer als doel heeft) meteen van de bijgewerkte
    /// waarde profiteert.</summary>
    private void Bezetmelding_VanHardware(int meldernummer, bool bezet)
    {
        if (!bezet)
        {
            // Zie StartStaartFase/BeeindigStaartFase: een ECHTE vrijmelding van precies de
            // melder waar een staart-fase op wacht, rondt die nu af - i.p.v. te wachten op
            // de veiligheids-timeout daar (die er, net als bij aankomst, alleen is om nooit
            // voor altijd vast te lopen als de melding om wat voor reden dan ook uitblijft).
            //
            // GEVONDEN, ECHTE SENSORBOUNCE (gebruikerswaarneming: spookmelding vlak na zo'n
            // vrijmelding): fysieke bezetmelders kunnen kortstondig "stuiteren" - vrij,
            // gevolgd door binnen een fractie van een seconde weer bezet, van DEZELFDE
            // sensor. Werd het blok DIRECT op deze eerste "vrij" vrijgegeven, dan ziet de
            // bezetmelding-verwerking die stuiterende tweede melding als een complete
            // verrassing (het blok was er net "vrij" verklaard) - een spookmelding, terwijl
            // er fysiek niets vreemds gebeurde. Daarom nu een korte, vaste marge
            // (StaartVrijmeldingDebounceMilliseconden): pas als de melder die hele periode
            // ECHT vrij blijft (geen nieuwe bezetmelding van dezelfde melder, zie de
            // bezet-branch hieronder die deze timer annuleert), wordt de staart-fase pas
            // echt afgerond.
            Dispatcher.Invoke(() =>
            {
                if (_venstergesloten) return;
                if (!_staartWachtMelders.TryGetValue(meldernummer, out var staartBlok)) return;

                if (_staartVrijDebounceTimers.TryGetValue(meldernummer, out var bestaandeTimer))
                {
                    bestaandeTimer.Stop();
                    _staartVrijDebounceTimers.Remove(meldernummer);
                }
                var debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(StaartVrijmeldingDebounceMilliseconden) };
                debounceTimer.Tick += (_, _) =>
                {
                    debounceTimer.Stop();
                    _staartVrijDebounceTimers.Remove(meldernummer);
                    if (_venstergesloten) return;
                    // Nogmaals opzoeken (niet de eerder gevangen staartBlok blindelings
                    // hergebruiken): in de tussentijd kan de staart-fase al op een andere
                    // manier afgerond zijn (bijv. via de veiligheids-timeout).
                    if (_staartWachtMelders.TryGetValue(meldernummer, out var nogSteedsStaartBlok))
                    {
                        _staartWachtMelders.Remove(meldernummer);
                        // Multi-sensor-blok (zie StartStaartFase hierboven): pas ECHT
                        // afronden als geen ENKEL ander meldpunt van datzelfde blok nog op
                        // zijn eigen vrijmelding staat te wachten.
                        bool anderMeldpuntNogWachtend = _staartWachtMelders.Values.Contains(nogSteedsStaartBlok);
                        if (!anderMeldpuntNogWachtend)
                        {
                            Log($"Echte vrijmelding van melder {meldernummer} ontvangen - staart-fase van blok {nogSteedsStaartBlok.Nummer} afgerond.");
                            BeeindigStaartFase(nogSteedsStaartBlok);
                        }
                        else
                        {
                            Log($"Echte vrijmelding van melder {meldernummer} ontvangen voor blok {nogSteedsStaartBlok.Nummer} - wacht nog op ander meldpunt van hetzelfde blok voordat het blok echt vrij is.");
                        }
                    }
                };
                _staartVrijDebounceTimers[meldernummer] = debounceTimer;
                debounceTimer.Start();
            });
            return;
        }
        Dispatcher.Invoke(() =>
        {
            // Zie hierboven: een bezetmelding van dezelfde melder terwijl er nog een
            // vrijmelding-debounce voor loopt, is de melder die terugstuitert naar bezet -
            // de eerdere "vrij" was dus niet definitief. Annuleer de pending afronding: het
            // blok blijft gewoon bezet, precies zoals het al was vóór de stuiterende
            // vrijmelding, dus GEEN spookmelding-kans meer voor deze bezetmelding.
            if (_staartVrijDebounceTimers.TryGetValue(meldernummer, out var lopendeDebounce))
            {
                lopendeDebounce.Stop();
                _staartVrijDebounceTimers.Remove(meldernummer);
            }

            // GEVONDEN, RESTEREND GAT (bytes-analyse: een melder werd pas bezet EEN FRACTIE
            // NA het vertrek zelf - dus stond nog niet in de momentopname die StartStaartFase
            // bij vertrek maakte, en werd daardoor niet meegenomen in de multi-sensor-
            // vrijgave: het blok kon zo alsnog te vroeg vrijgegeven worden, want deze melder
            // "telde niet mee"). Een melder die bezet meldt terwijl zijn EIGEN blok op dit
            // moment nog in de staart-fase zit, hoort daar altijd bij, ook als hij niet in
            // de oorspronkelijke momentopname zat - de trein passeert 'm immers duidelijk nog
            // tijdens het wegrijden.
            var staartBlokVanDezeMelder = _blokBeheerder.Blokken.FirstOrDefault(b =>
                _blokBeheerder.IsStaart(b) && b.Bezetmeldpunten.Any(m => m.MeldernNummer == meldernummer));
            // GEVONDEN, DEFINITIEVE OORZAAK (gebruikerswaarneming: "trein raakt de weg kwijt
            // in blok 6" - bevestigd via de diagnostische logregels: blok7 werd bij een
            // volgende ronde als "niet vrij" uitgesloten, terwijl zijn eigen stopmelder allang
            // gepasseerd was): deze regel overschreef _staartWachtMelders[meldernummer]
            // ONVOORWAARDELIJK, ook als hij al op een ANDER blok stond. Een meldernummer dat
            // - zoals hier melder24 - bij TWEE blokken hoort (het gedeelde punt op een
            // kruiswissel-overgang, bijv. blok3 én blok7) kan dus de JUISTE, eerder door
            // StartStaartFase gezette registratie van het ene blok laten overschrijven door
            // het andere, simpelweg omdat dat andere blok toevallig eerder in de bloklijst
            // staat. Blok7 se eigen staart-fase sloot daardoor nooit af, en bleef voor altijd
            // "bezet" - waardoor het bij een latere ronde ten onrechte als kandidaat werd
            // uitgesloten. Nu: alleen invullen als er nog HELEMAAL GEEN registratie voor deze
            // melder bestaat - een bestaande, eerder gezette registratie (voor welk blok dan
            // ook) blijft met rust.
            if (staartBlokVanDezeMelder != null && !_staartWachtMelders.ContainsKey(meldernummer))
                _staartWachtMelders[meldernummer] = staartBlokVanDezeMelder;

            if (_venstergesloten) return;

            // Gebruikersverzoek ("bezetmelders en volgorde zelflerend maken"): voor ELKE
            // trein die op dit moment op een echte melding wacht, dit meldernummer aan de
            // waargenomen volgorde toevoegen als het al bekend is als een van de
            // Bezetmeldpunten van de VERWACHTE bestemming (dus ALLEEN meldernummers die je
            // al aan dat blok gekoppeld hebt, in welke volgorde dan ook - de software
            // verzint zelf geen nieuwe blok-toewijzingen, alleen de volgorde erbinnen).
            foreach (var wachtendeTrein in _actieveTreinen.Where(t => t.WachtBegonnenOp != null))
            {
                var bestemming = wachtendeTrein.Route.Automatisch
                    ? wachtendeTrein.GereserveerdVolgendBlok
                    : (wachtendeTrein.GebeurtenisIndex < wachtendeTrein.Gebeurtenissen.Count ? wachtendeTrein.Gebeurtenissen[wachtendeTrein.GebeurtenisIndex].Blok : null);
                if (bestemming != null
                    && bestemming.Bezetmeldpunten.Any(m => m.MeldernNummer == meldernummer)
                    && !wachtendeTrein.GeobserveerdeMeldersTijdensWachten.Contains(meldernummer))
                {
                    wachtendeTrein.GeobserveerdeMeldersTijdensWachten.Add(meldernummer);
                }
            }

            var trein = _actieveTreinen.FirstOrDefault(t => t.WachtOpMeldernNummer == meldernummer);
            if (trein is null)
            {
                // GEVONDEN GAT (zie MeldTreinVastgelopen voor de volledige toelichting): een
                // melder die geen enkele actieve trein verwacht, werd tot nu toe volledig
                // stilzwijgend genegeerd - geen spoor in de log, dus onmogelijk te
                // onderscheiden van "hoort hier gewoon niet bij" versus "hoorde hier WEL bij
                // maar WachtOpMeldernNummer stond om een onbekende reden op iets anders".
                // Alleen loggen als er MINSTENS één trein daadwerkelijk op EEN melder aan het
                // wachten is (WachtOpMeldernNummer!=null) - anders is dit gewoon een normale
                // melding buiten elke actieve wachtperiode om, niet interessant genoeg om de
                // log mee te vullen.
                var welWachtend = _actieveTreinen.Where(t => t.WachtOpMeldernNummer != null).ToList();
                if (welWachtend.Count > 0)
                    Log($"Melder {meldernummer} ontvangen, maar geen enkele actieve trein wacht op precies dit meldernummer (wel wachtend op: {string.Join(", ", welWachtend.Select(t => $"{t.WachtOpMeldernNummer} (blok {t.HuidigBlok?.Nummer.ToString() ?? "?"})"))}) - genegeerd voor de routevoortgang.");
                return;
            }
            Log($"[Route '{trein.Route.Omschrijving}'] Echte bezetmelding van melder {meldernummer} ontvangen - gaat direct verder.");

            // GEVONDEN, ARCHITECTUREEL GAT (zie RijdendeTrein.WachtOpStopmelderVoorKerenWachttijd
            // voor de volledige toelichting): dit is GEEN nieuwe blok-aankomst, maar de
            // bevestiging dat de trein het FYSIEKE UITEINDE van het blok waar hij al in
            // staat bereikt heeft - pas NU is keren daadwerkelijk veilig. Volledig apart
            // van de normale aankomst-afhandeling hieronder (geen doelBlok, geen nieuwe
            // geleerde-reistijd-registratie voor een blok-overgang - er IS geen overgang).
            if (trein.WachtOpStopmelderVoorKerenWachttijd is double wachttijdNaStopmelding)
            {
                trein.WachtOpStopmelderVoorKerenWachttijd = null;
                // GEVONDEN GAT (bytes-analyse: een spookmelding op het volgende blok, zonder
                // dat er ooit een "wissels alvast gezet"/"blok bezet"-logregel voor die stap
                // verscheen): deze melder (de stopmelder waarop hierboven gewacht werd) is nu
                // verwerkt, maar trein.WachtOpMeldernNummer bleef tot nu toe op DEZE waarde
                // staan - een vervuild/verouderd wachtdoel dat, als de eerstvolgende
                // AutomatischeStapProberen-poging toevallig geen kandidaat vindt en dus blijft
                // herhalen, voor de rest van de rit nooit meer klopt met wat er werkelijk nog
                // relevant is. Expliciet wissen, net als VerwerkAutomatischeAankomst dat aan
                // zijn eigen begin ook doet - de eerstvolgende PlanVolgendeAutomatischeStap
                // hieronder zet, zodra er een kandidaat gevonden wordt, gewoon weer een eigen,
                // verse WachtOpMeldernNummer.
                trein.WachtOpMeldernNummer = null;
                if (trein.Vastgelopen)
                {
                    trein.Vastgelopen = false;
                    if (trein.HuidigBlok != null) _blokBeheerder.ZetFoutmelding(trein.HuidigBlok, false);
                    Log($"[Route '{trein.Route.Omschrijving}'] Trein '{trein.Trein?.Omschrijving}' was vastgelopen bij blok {trein.HuidigBlok?.Nummer.ToString() ?? "?"} - stopmelder {meldernummer} alsnog ontvangen, rit hervat.");
                }
                // Zie VerwerkAutomatischeAankomst: het afremmen werd BEWUST tot hier
                // uitgesteld (de trein reed tot nu toe gewoon door, op de snelheid die al
                // bij vertrek voorbezet was) - nu de ECHTE stopmelder (het fysieke
                // uiteinde) bevestigt, is dit het eerste, juiste moment om daadwerkelijk
                // af te remmen naar stilstand, vlak vóórdat er gekeerd wordt.
                if (trein.HuidigBlok != null) LogSnelheidsopbouw(trein, optrekken: false, trein.HuidigBlok);
                PlanVolgendeAutomatischeStap(trein, wachttijdNaStopmelding);
                return;
            }

            // Herstel van een eerder gemelde "vastgelopen"-status (zie MeldTreinVastgelopen):
            // de melding kwam alsnog binnen, dus de rit kan gewoon verder - de foutmelding
            // wordt opgeheven. BEWUST geen geleerde-reistijd-registratie hieronder in dit
            // geval: de gemeten tijd (WachtBegonnenOp tot nu) zou dan de hele stilstand-
            // periode meetellen, wat de toekomstige "hoe lang duurt dit normaal"-schatting
            // juist zou verpesten. De WAARGENOMEN VOLGORDE blijft wel gewoon bruikbaar (de
            // volgorde van sensoren verandert niet doordat het een keer lang duurde), dus
            // LeerBezetmelderVolgorde hieronder gebeurt WEL in beide gevallen.
            bool herstelVanVastlopen = trein.Vastgelopen;
            if (herstelVanVastlopen)
            {
                trein.Vastgelopen = false;
                if (trein.HuidigBlok != null) _blokBeheerder.ZetFoutmelding(trein.HuidigBlok, false);
                Log($"[Route '{trein.Route.Omschrijving}'] Trein '{trein.Trein?.Omschrijving}' was vastgelopen bij blok {trein.HuidigBlok?.Nummer.ToString() ?? "?"} - melder {meldernummer} alsnog ontvangen, rit hervat.");
            }

            var doelBlok = trein.Route.Automatisch
                ? trein.GereserveerdVolgendBlok
                : (trein.GebeurtenisIndex < trein.Gebeurtenissen.Count ? trein.Gebeurtenissen[trein.GebeurtenisIndex].Blok : null);

            if (!herstelVanVastlopen && trein.WachtBegonnenOp is DateTime begonnen && doelBlok != null)
            {
                // trein.HuidigBlok is op dit punt nog het VERTREKBLOK (de overgang naar
                // doelBlok is nog niet verwerkt, dat gebeurt hieronder pas) - dus precies
                // de "van"-richting die RegistreerGeleerdeReistijd nodig heeft.
                _blokBeheerder.RegistreerGeleerdeReistijd(doelBlok, trein.HuidigBlok, trein.Trein, (DateTime.Now - begonnen).TotalSeconds);
            }
            if (doelBlok != null)
                LeerBezetmelderVolgorde(doelBlok, trein.HuidigBlok, trein.GeobserveerdeMeldersTijdensWachten);

            if (trein.Route.Automatisch)
            {
                if (trein.HuidigBlok != null && trein.GereserveerdVolgendBlok != null)
                    VerwerkAutomatischeAankomst(trein, trein.HuidigBlok, trein.GereserveerdVolgendBlok);
            }
            else
            {
                RijTimer_Tick(trein);
            }
        });
    }

    /// <summary>Rekent een gewenste schaalsnelheid (km/u, uit EffectiefTreintype) om naar
    /// een decoderstap - gebruikt de ECHTE, per-loc gemeten Stappentabel als die beschikbaar
    /// is (GebruikGeijkteSnelheid), anders een simpele lineaire schatting op basis van de
    /// treintype-maximumsnelheid en het aantal decoderstappen van de loc (DecoderStappen) -
    /// een ruwe aanname bij gebrek aan een echte ijking, geen vervanging daarvoor.</summary>
    private static int BepaalDecoderStapVoorSnelheid(Trein trein, double gewenstKmU)
    {
        if (trein.GebruikGeijkteSnelheid && trein.Stappentabel.Count > 0)
            return trein.Stappentabel.OrderBy(s => Math.Abs(s.KmPerUur - gewenstKmU)).First().Stap;

        double maxKmU = 100; // fallback als er geen enkel treintype bekend is
        double factor = Math.Clamp(gewenstKmU / maxKmU, 0, 1);
        return (int)Math.Round(factor * Math.Max(1, trein.DecoderStappen));
    }

    /// <summary>Logt welke loc-functies van deze trein op dit moment "afgaan" - zoals
    /// Koploper's "Onderhouden locomotieven" - "Functies uitgebreid", die functies koppelt
    /// aan dezelfde soort simulator-momenten. Speelt ook een eventueel gekoppeld lokaal
    /// .wav-geluid af, en stuurt (als er een FunctieNummer is ingevuld) het bijbehorende
    /// DCC-functiecommando daadwerkelijk naar de hardware.</summary>
    private void LogLocFuncties(RijdendeTrein trein, LocGebeurtenis gebeurtenis)
    {
        if (trein.Trein is null) return;
        foreach (var functie in trein.Trein.Functies.Where(f => f.Gebeurtenis == gebeurtenis))
        {
            Log($"[Route '{trein.Route.Omschrijving}'] Loc '{trein.Trein.Omschrijving}': functie '{functie.Naam}' geactiveerd ({gebeurtenis}).");
            SpeelGeluidAf(functie.GeluidsBestand);
            if (functie.FunctieNummer < 0) continue; // deze functie heeft bewust geen hardwarekoppeling, puur geluid
            if (trein.Trein.DecoderAdres > 0)
                _hardwareBeheerder.StuurFunctieCommando(trein.Trein.DecoderAdres, functie.FunctieNummer, functie.Aan, trein.HuidigBlok?.Nummer ?? 0);
            else
                Log($"[Route '{trein.Route.Omschrijving}'] Geen functiecommando verstuurd voor '{trein.Trein.Omschrijving}' (F{functie.FunctieNummer}): geen decoderadres ingesteld (zie Treinen beheren).");
        }
    }

    /// <summary>Zoals Koploper's eigen "afspelen van een geluidsbestand (*.wav) op de
    /// computer" - één van de manieren waarop Koploper een loc-functie hoorbaar kan maken.
    /// SoundPlayer.Play() speelt asynchroon af (blokkeert de simulator niet); een ontbrekend
    /// of ongeldig bestand mag de rit nooit laten crashen, dus alles in een try/catch.</summary>
    private void SpeelGeluidAf(string? bestandspad)
    {
        if (string.IsNullOrWhiteSpace(bestandspad)) return;
        if (!SimulatieInstellingen.GeluidenIngeschakeld) return;
        try
        {
            // MediaPlayer i.p.v. SoundPlayer: die laatste heeft GEEN volumeregeling. Ook
            // hier GEEN 'using'/Close() meteen na Play() - dat zou het geluid afkappen
            // voordat het afspelen klaar is; de garbage collector ruimt 'm later vanzelf op.
            var speler = new System.Windows.Media.MediaPlayer { Volume = SimulatieInstellingen.GeluidsVolume };
            speler.Open(new Uri(bestandspad));
            speler.Play();
        }
        catch (Exception ex)
        {
            Log($"Geluid '{bestandspad}' kon niet afgespeeld worden: {ex.Message}");
        }
    }

    private void LogSeinenVoorBlok(Blok blok, bool bezet)
    {
        foreach (var sein in _baanBeheerder.Symbolen.OfType<Sein>().Where(s => s.GekoppeldBlok != null))
        {
            var bewaakt = SeinLogica.BepaalBewaaktBlok(sein.GekoppeldBlok!, _blokBeheerder, _wisselstraatBeheerder);
            if (bewaakt != blok) continue;

            var aspect = SeinLogica.BepaalAspect(sein, _blokBeheerder, _wisselstraatBeheerder, _baanBeheerder.Symbolen.OfType<Sein>());
            string aspectTekst = aspect switch
            {
                SeinAspect.Rood => "rood (bezet)",
                SeinAspect.Geel => "geel (rem voor volgend rood sein)",
                _ => "groen (vrij)"
            };
            Log($"Sein gekoppeld aan blok {sein.GekoppeldBlok!.Nummer} (bewaakt blok {bewaakt.Nummer}) -> {aspectTekst}.");
            _hardwareBeheerder.StuurSeinCommando(sein.Adres, aspect == SeinAspect.Rood);
        }
    }

    private const double StaartduurSeconden = 0.8;
    private const double HerhalenPauzeSeconden = 3;

    /// <summary>Zet een blok tijdelijk op "staart" (een blok dat de trein net verlaten heeft
    /// maar nog niet helemaal vrijgegeven is) in plaats van meteen abrupt naar vrij te springen -
    /// zoals Koploper's eigen "Lijnkleur: staartindicatie".
    ///
    /// GEVONDEN, FUNDAMENTEEL GAT (bytes-analyse: een spookmelding 4 seconden na vertrek,
    /// exact op het blok dat net verlaten was): dit gebruikte altijd een VASTE, GESIMULEERDE
    /// duur (StaartduurSeconden, 0.8 sec) om het blok weer vrij te verklaren - ook bij echte
    /// hardware. Een echte, late bezetmelding van het net-verlaten blok (de fysieke staart
    /// van de trein was simpelweg nog niet helemaal weg) kwam daardoor ALTIJD te laat voor
    /// een software-state die het blok allang als vrij beschouwde -> spookmelding, exact
    /// hetzelfde patroon als eerder bij aankomst-detectie, nu bij vertrek.
    /// <paramref name="vertrekMelder"/>: de melder (Stopsectie/laatste-in-de-richtings-
    /// reeks) waarvan een ECHTE vrijmelding (bezet=false) betekent "de staart is nu echt
    /// weg" - bij echte hardware wordt DAAROP gewacht i.p.v. op de vaste duurSeconden.
    ///
    /// GEBRUIKERSCORRECTIE (nogmaals, en terecht: "alleen dingen doen op basis van
    /// feiten en geen aannames door timers"): dit riep eerder, als de vrijmelding te lang
    /// uitbleef, ALSNOG BeeindigStaartFase aan ("toch als vrij aangenomen") - exact
    /// dezelfde fout als de oorspronkelijke "veiligheids-timeout" bij aankomst, alleen dan
    /// bij vertrek. Dat is nu VERWIJDERD: blijft de echte vrijmelding te lang uit, dan
    /// wordt dat ALLEEN gemeld (een blijvende waarschuwing, geen popup/noodstop zoals
    /// MeldTreinVastgelopen - het blok blijft namelijk gewoon veilig "bezet" staan, er rijdt
    /// niets de verkeerde kant op) - het blok blijft simpelweg "staart" totdat de melding
    /// alsnog binnenkomt (Bezetmelding_VanHardware verwerkt 'm dan alsnog, wanneer dat ook
    /// is) of totdat een mens er zelf naar kijkt. Geen enkel blok wordt hier meer vrij
    /// verklaard op basis van een timer - alleen op basis van een echte melding.</summary>
    /// <summary>Start de staart-fase voor het verlaten blok - wacht (bij echte hardware) op
    /// vrijmelding voordat het blok echt vrijgegeven wordt.
    ///
    /// GEVONDEN, STRUCTUREEL GAT (bytes-analyse: een blok met MEERDERE meldpunten - bijv.
    /// blok 12 met 143/144/136 - werd al vrij verklaard zodra ALLEEN de gekozen
    /// "vertrekmelder" vrij meldde, terwijl de trein de ANDERE meldpunten van datzelfde
    /// blok nog gewoon aan het passeren was tijdens het verder wegrijden. Een van die
    /// andere, verderop gelegen sensoren die daarna alsnog (heel legitiem) bezet meldde,
    /// zag er dan uit als een spookmelding - het blok was immers al te vroeg vrij
    /// verklaard. Exact dezelfde reden waarom MainWindow.Bezetmelding_VanHardware een blok
    /// pas vrijgeeft als GEEN ENKEL meldpunt meer bezet is (zie BlokBeheerder.
    /// MelderIsBezet) - nu geldt diezelfde regel ook hier: bij echte hardware wordt op de
    /// vrijmelding van ELK meldpunt gewacht dat op dit moment daadwerkelijk bezet staat,
    /// niet alleen op de ene vertrekmelder. Een meldpunt waarvan de status nooit bezet was
    /// (bijv. een sectie die een kortere trein nooit bereikt) hoeft niet vrijgemeld te
    /// worden - dat zou anders tot een eeuwige, nooit-opgeloste staart-fase leiden.</summary>
    private void StartStaartFase(Blok blok, double duurSeconden, Bezetmeldpunt? vertrekMelder)
    {
        _blokBeheerder.ZetStaart(blok, true);
        // Een vers vertrek vanaf dit blok begint met een schone lei: een eventuele oude
        // "synthetisch vrijgegeven"-aantekening van een VORIGE passage (zie
        // _synthetischVrijgegevenBlokken) is niet meer relevant - dit vertrek krijgt zijn
        // eigen, normale staart-fase/beoordeling.
        _synthetischVrijgegevenBlokken.Remove(blok);

        bool wachtOpEchteHardware = _hardwareBeheerder.Huidige is not SimulatieHardware;
        // GEVONDEN, KRITIEK GAT (gebruikerswaarneming: "blok 10 gekleurd bleef" - permanent
        // bezet/staart, nooit meer vrijgegeven): dit voegde voorheen de vertrekmelder ALTIJD
        // toe, ook als diens status nog helemaal nooit bevestigd bezet was - bijv. bij een
        // rit die STARTTE op dit blok (een verse plaatsing, nooit via een echte melding
        // gearriveerd). Als die melder dan in werkelijkheid nooit bezet ís geweest (de loc
        // stond fysiek niet exact op dat specifieke sensorpunt), komt er ook NOOIT een
        // vrijmelding van - en bleef dit blok voor de rest van de sessie permanent
        // "bezet"/"staart" staan, wat op zijn beurt latere ritten in de war stuurde. Nu
        // wordt de vertrekmelder ALLEEN nog meegenomen als zijn status ECHT bevestigd bezet
        // is - is dat voor GEEN ENKEL meldpunt het geval (dus ook de vertrekmelder niet),
        // dan is er simpelweg niets betrouwbaars om op te wachten, en valt deze staart-fase
        // terug op de gesimuleerde tijd (zie de "geen enkel meldpunt bezet"-tak hieronder) -
        // beter dat dan voor altijd wachten op een melding die nooit komt.
        var teVolgenMelders = wachtOpEchteHardware
            ? blok.Bezetmeldpunten.Where(m => m.MeldernNummer > 0 && _blokBeheerder.MelderIsBezet(m.MeldernNummer) == true)
                                   .Select(m => m.MeldernNummer).Distinct().ToList()
            : new List<int>();

        if (teVolgenMelders.Count > 0)
        {
            foreach (var meldernummer in teVolgenMelders)
            {
                // GEVONDEN, TWEEDE PLEK VAN DEZELFDE OORZAAK (zie de vergelijkbare fix
                // verderop in Bezetmelding_VanHardware voor de volledige toelichting): dit
                // is de INITIËLE, bewuste start van een staart-fase-tracking - maar als een
                // meldernummer dat hier in de momentopname zit (zoals melder24, gedeeld
                // tussen blok3 en blok7) OP DAT MOMENT nog een NIET-AFGERONDE registratie
                // voor een ANDER blok heeft (bijv. blok7's eigen, nog lopende staart-fase),
                // overschreef deze regel die eerdere, nog actieve registratie klakkeloos -
                // waardoor DAT andere blok se staart-fase nooit meer afgerond kon worden en
                // voor altijd "bezet" bleef staan. Een bestaande registratie voor een ander
                // blok blijft daarom nu met rust; alleen een melder zonder (of met een
                // registratie voor DIT blok) wordt hier ingevuld.
                if (_staartWachtMelders.TryGetValue(meldernummer, out var bestaandBlok) && bestaandBlok != blok)
                    continue;
                _staartWachtMelders[meldernummer] = blok;
                if (_hardwareBeheerder.Huidige is DinamoHardware)
                    _hardwareBeheerder.VraagMelderStatusOp(meldernummer);
            }

            // GEEN timer die het blok alsnog vrijgeeft - alleen een timer die, als een
            // vrijmelding erg lang uitblijft, dat ÉÉN KEER meldt (zodat je het kunt zien
            // en onderzoeken), zonder verder iets aan de blok-status te veranderen. Het
            // blok blijft daarna gewoon "bezet"/"staart" totdat alle echte meldingen
            // binnen zijn.
            var waarschuwingsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(OnbekendeReistijdVastlopenSeconden) };
            waarschuwingsTimer.Tick += (_, _) =>
            {
                waarschuwingsTimer.Stop();
                // De ACTUELE _staartWachtMelders-inhoud, niet de oorspronkelijke
                // momentopname (teVolgenMelders) - kan inmiddels dynamisch aangevuld zijn
                // met melders die pas na vertrek bezet raakten, zie Bezetmelding_VanHardware.
                var nogWachtend = _staartWachtMelders.Where(kv => kv.Value == blok).Select(kv => kv.Key).ToList();
                if (nogWachtend.Count > 0)
                    Log($"Waarschuwing: nog steeds geen echte vrijmelding van meldpunt(en) {string.Join(", ", nogWachtend)} ontvangen voor blok {blok.Nummer} (net verlaten) - blok blijft voorlopig bezet totdat de melding(en) alsnog binnenkomen. Controleer de bezetmelder/bekabeling.");
            };
            waarschuwingsTimer.Start();
            return;
        }

        // GEVONDEN, KRITIEK GAT (bytes+stacktrace-analyse: BeeindigStaartFase bleek
        // aangeroepen te worden door PRECIES deze korte tak - de fysieke trein had bij
        // ECHTE hardware nog 6 SECONDEN nodig om zijn eerste meldpunt daadwerkelijk te
        // bereiken, terwijl deze timer de staart-fase al na 0,8 sec afsloot): deze korte,
        // vaste tijd is uitsluitend zinvol in SIMULATIEMODUS, waar niets anders de
        // staart-fase ooit zou afronden. Bij ECHTE hardware, maar met GEEN enkel op dit
        // moment bevestigd bezet meldpunt (bijv. een loc die precies op een sensor staat
        // die nooit een bezet-melding heeft gestuurd, zie de connect-time status-opvraag),
        // betekent dit NIET dat er geen echte melding meer komt - de dynamische
        // herregistratie hieronder (Bezetmelding_VanHardware, "een melder die bezet meldt
        // terwijl zijn blok in staart-fase zit hoort daar altijd bij") vangt een latere,
        // ECHTE bezetmelding nog gewoon op. Bij echte hardware daarom GEEN korte, actief
        // afsluitende timer meer - alleen dezelfde lange waarschuwingstimer als hierboven
        // (puur informatief, verandert niets aan de blok-status). Alleen in ECHTE
        // simulatie (geen hardware aangesloten) blijft de korte timer de enige bron.
        if (wachtOpEchteHardware)
        {
            _legeStaartFaseBlokken.Add(blok);
            var waarschuwingsTimerFallback = new DispatcherTimer { Interval = TimeSpan.FromSeconds(OnbekendeReistijdVastlopenSeconden) };
            waarschuwingsTimerFallback.Tick += (_, _) =>
            {
                waarschuwingsTimerFallback.Stop();
                var nogWachtend = _staartWachtMelders.Where(kv => kv.Value == blok).Select(kv => kv.Key).ToList();
                if (nogWachtend.Count > 0)
                    Log($"Waarschuwing: nog steeds geen echte vrijmelding van meldpunt(en) {string.Join(", ", nogWachtend)} ontvangen voor blok {blok.Nummer} (net verlaten) - blok blijft voorlopig bezet totdat de melding(en) alsnog binnenkomen. Controleer de bezetmelder/bekabeling.");
                else
                    Log($"Waarschuwing: nog steeds geen ENKELE echte bezetmelding ontvangen voor blok {blok.Nummer} (net verlaten, geen enkel meldpunt was bij vertrek al bevestigd bezet) - blok blijft voorlopig bezet totdat er alsnog een melding binnenkomt. Controleer de bezetmelder/bekabeling.");
            };
            waarschuwingsTimerFallback.Start();
            return;
        }

        // Echte simulatiemodus (geen hardware aangesloten): geen enkele melder kan hier
        // ooit binnenkomen, dus de gesimuleerde duur blijft de enige beschikbare bron.
        var staartTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(duurSeconden * SimulatieInstellingen.VertragingsFactor) };
        staartTimer.Tick += (_, _) =>
        {
            staartTimer.Stop();
            BeeindigStaartFase(blok);
        };
        staartTimer.Start();
    }

    /// <summary>De daadwerkelijke afhandeling van "staart-fase voorbij" - apart getrokken
    /// zodat zowel de (veiligheids-)timer hierboven als een ECHTE, tijdig ontvangen
    /// vrijmelding (Bezetmelding_VanHardware) dezelfde, ene plek gebruiken.</summary>
    private void BeeindigStaartFase(Blok blok)
    {
        _legeStaartFaseBlokken.Remove(blok);
        _blokBeheerder.ZetStaart(blok, false);
        _blokBeheerder.ZetBezet(blok, false);

        // GEBRUIKERSWAARNEMING ("blok 3 gaf bij het opstarten een spookmelding doordat ik de
        // trein fysiek plaatste, geen probleem mee, maar nadat de trein een hele ronde
        // gereden had en blok 3 weer verliet bleef het blok paars knipperen - de melding was
        // allang opgelost"): een Foutmelding (paars knipperend, zie ZetFoutmelding) werd tot
        // nu toe NOOIT automatisch opgeheven door gewoon normaal gebruik - alleen handmatig
        // (rechtsklik) of via de aparte Vastgelopen-herstelpaden in Bezetmelding_VanHardware.
        // Maar het bereiken van HIER betekent dat dit blok zojuist een volledige, ECHT
        // bevestigde bezet-dan-vrij-cyclus heeft afgerond (BeeindigStaartFase wordt alleen
        // aangeroepen ná een eerdere, bevestigde bezetmelding) - precies zo'n bevestigd feit
        // als de bestaande Vastgelopen-herstelpaden al gebruiken om een Foutmelding op te
        // heffen. Een eventuele oudere Foutmelding (bijv. van een eenmalige spookmelding bij
        // het handmatig plaatsen van een loc, ver vóór deze cyclus) mag dus gerust vervallen.
        if (_blokBeheerder.IsFoutmelding(blok))
        {
            _blokBeheerder.ZetFoutmelding(blok, false);
            Log($"Blok {blok.Nummer} heeft zojuist een volledige, echt bevestigde bezet/vrij-cyclus afgerond - de eerdere foutmelding op dit blok is daarmee vervallen.");
        }

        if (_venstergesloten) return;
        LogSeinenVoorBlok(blok, false);
        Redraw();
    }

    private void RijTimer_Tick(RijdendeTrein trein)
    {
        trein.Timer?.Stop();
        trein.LaatsteVoortgang = DateTime.Now;

        var (blok, melder, wachttijd) = trein.Gebeurtenissen[trein.GebeurtenisIndex];
        // Rijstatistieken (iTrain: "travelled time and distance indicators per loc"): de
        // wachttijd van DEZE gebeurtenis is precies de modeltijd die zojuist verstreken is
        // om hier te komen - onafhankelijk van SimulatieInstellingen.VertragingsFactor (de
        // kijksnelheid-instelling verandert alleen hoe snel je ERNAAR kijkt, niet hoeveel
        // modeltijd het "in werkelijkheid" kostte).
        if (trein.Trein != null) trein.Trein.TotaleRijtijdSeconden += wachttijd;
        if (trein.HuidigBlok != null && trein.HuidigBlok != blok)
        {
            // Het vertrek-commando (VerwerkEventueelKeren, LogLocFuncties VerlaatBlok/
            // GaatWeerRijden, LogSnelheidsopbouw optrekken:true, StartStaartFase incl. de
            // TeLangeBlokken-staartduur-berekening) is verplaatst naar PlanVolgendeGebeurtenis,
            // vlak na het plannen van DEZE stap - zie de toelichting daar. Hier gaat het
            // alleen nog om de AANKOMST-boekhouding.

            // Afgelegde afstand: het net verlaten blok telt mee, als daar een lengte voor
            // is ingevuld (0 = niet meegeteld, bijv. nog niet opgemeten).
            if (trein.Trein != null && trein.HuidigBlok.LengteCm > 0)
                trein.Trein.TotaleAfgelegdeAfstandCm += trein.HuidigBlok.LengteCm;
        }
        var vorigBlokVoorLoc = trein.HuidigBlok;
        trein.HuidigBlok = blok;
        _blokBeheerder.ZetStaart(blok, false); // mocht dit blok toevallig nog een staart van een eerdere passage zijn
        _blokBeheerder.ZetBezet(blok, true);
        if (trein.Trein != null)
        {
            if (vorigBlokVoorLoc != null) _blokBeheerder.VerwijderLoc(vorigBlokVoorLoc);
            _blokBeheerder.PlaatsLoc(blok, trein.Trein);
        }
        _blokBeheerder.ZetGereserveerd(blok, false); // niet meer "onderweg ernaartoe", de trein is er nu
        LogSeinenVoorBlok(blok, true);

        bool ditIsHetEindblok = trein.GebeurtenisIndex == trein.Gebeurtenissen.Count - 1;
        // trein.VorigBlok wordt (bewust) alleen bij automatisch rijden bijgehouden - voor
        // een vaste route wordt het vorige blok hier afgeleid uit de al berekende
        // Gebeurtenissen-lijst zelf.
        Blok? vorigBlokVoorKeercheck = trein.GebeurtenisIndex > 0 ? trein.Gebeurtenissen[trein.GebeurtenisIndex - 1].Blok : null;
        bool moetHierStoppen = ditIsHetEindblok || blok.Type == BlokType.Station || VereistKeren(vorigBlokVoorKeercheck, blok);
        LogLocFuncties(trein, moetHierStoppen ? LocGebeurtenis.AankomstEnStopt : LocGebeurtenis.AankomstEnRijdtDoor);
        if (moetHierStoppen) LogSnelheidsopbouw(trein, optrekken: false, blok);
        // Een Station-blok is een "verplichte stop" (instaptijd) - dat is een apart moment
        // uit Koploper's "Functies uitgebreid", los van de gewone AankomstEnStopt.
        if (blok.Type == BlokType.Station) LogLocFuncties(trein, LocGebeurtenis.GestoptBijVerplichteStop);

        string bericht = melder != null
            ? $"[Route '{trein.Route.Omschrijving}'] Blok {blok.Nummer}: melder {melder.MeldernNummer} ({melder.Rol}) bezet."
            : blok.Type == BlokType.Station
                ? $"[Route '{trein.Route.Omschrijving}'] Blok {blok.Nummer} (station): instaptijd."
                : $"[Route '{trein.Route.Omschrijving}'] Blok {blok.Nummer}: geen bezetmeldpunt ingesteld voor dit blok, gebruikt standaardtijd.";
        StatusTekst.Text = bericht;
        Log(bericht);
        Redraw();

        // Gebruikersverzoek (kijkscherm): "alle locs rijden naar het einde van hun
        // gereserveerde rijweg en stoppen daar, dus geen noodstop maar gewoon een rustig
        // stoppen" - zie TreinrouteWindow.AlleTreinenRustigStoppen (via het kijkscherm). Bewust NA de aankomst-
        // boekhouding hierboven maar VOOR de volgende gebeurtenis - de trein maakt de al
        // ingezette stap dus altijd gewoon af (dit blok is al ECHT bereikt), en stopt dan
        // meteen i.p.v. door te rijden naar de rest van de vaste route.
        if (trein.StopNaDezeStap)
        {
            trein.StopNaDezeStap = false;
            RondRitAf(trein, blok, $"Rustig gestopt bij blok {blok.Nummer} (op verzoek via het kijkscherm).");
            return;
        }

        trein.GebeurtenisIndex++;
        PlanVolgendeGebeurtenis(trein);
    }

    private void StopRijden_Click(object sender, RoutedEventArgs e)
    {
        if (RoutesLijst.SelectedItem is not Treinroute route)
        {
            StatusTekst.Text = "Selecteer eerst de route die je wilt stoppen.";
            return;
        }
        var trein = _actieveTreinen.FirstOrDefault(t => t.Route == route);
        if (trein is null)
        {
            StatusTekst.Text = $"Route '{route.Omschrijving}' rijdt op dit moment niet.";
            return;
        }
        StopTrein(trein, "Rijden gestopt.", magHerhalen: false);
    }

    /// <summary>Noodstop: stopt ALLE actief rijdende treinen in één keer, ongeacht welke
    /// route er in de lijst geselecteerd staat - net als het F-bit/noodstop-concept uit de
    /// Dinamo-specificatie (§2.2: het F-bit stopt in één klap alle voertuigen). Herhalen
    /// wordt bewust uitgezet (magHerhalen: false) - een noodstop mag nooit vanzelf weer op
    /// gang komen, de gebruiker moet zelf bewust opnieuw op "Start rijden" drukken.</summary>
    private void Noodstop_Click(object sender, RoutedEventArgs e) => VoerNoodstopUit();

    /// <summary>Publiek zodat MainWindow's spookmelding-afhandeling dezelfde noodstop kan
    /// activeren als de knop hier - net als het echte Koploper: "Wanneer de trein in een
    /// blok terecht komt waar deze niet hoort te zijn laat het programma een waarschuwend
    /// geluid horen en activeert een noodstop. Alle treinen komen dan acuut tot
    /// stilstand." (bron: praktijkervaring van een Koploper-gebruiker).</summary>
    public void VoerNoodstopUit(string? reden = null)
    {
        // Het hardware-signaal altijd sturen, ook als de software geen enkele trein als
        // actief bijhoudt - er kan altijd iets fysieks rijden dat de software niet volgt
        // (handmatig gestart, een resterend commando, etc.). Dit is precies waarom een
        // noodstop nooit afhankelijk mag zijn van wat de software toevallig denkt te weten.
        _hardwareBeheerder.Huidige.Noodstop();

        string redenTekst = reden != null ? $" ({reden})" : "";

        // GEVONDEN, FUNDAMENTELER GAT (bytes-analyse: een hele testsessie zonder ook maar
        // één verstuurd snelheidscommando, en toch bleef een loc kilometers doorrijden en
        // de ene spookmelding na de andere veroorzaken): Noodstop() hierboven zet Dinamo's
        // F-bit maar voor een paar cycli (~1 sec) - de spec is daar glashelder over: "Zodra
        // een datagram ontvangen wordt met F=0 nemen de voertuigen de oorspronkelijke
        // snelheid weer aan." Dinamo blijft namelijk voor ALTIJD het laatst gegeven
        // snelheidscommando per blok/decoder herhalen, ongeacht of dat commando uit DEZE
        // sessie komt of een sessie geleden (bijv. omdat de software toen afsloot zonder
        // ooit expliciet snelheid=0 te sturen) - onze F-bit-noodstop is dus hooguit een
        // korte pauze, GEEN daadwerkelijke, blijvende stop voor zo'n "vergeten" commando.
        // Daarom hier ALSNOG een expliciet snelheid=0-commando naar ELKE bekende, GEPLAATSTE
        // loc, ongeacht of de software 'm op dit moment als "actief rijdend" bijhoudt - dat
        // dekt precies het geval waar de software niets van een rit afweet maar de fysieke
        // loc toch nog gewoon doorrijdt.
        // GEVONDEN, KRITIEK VEILIGHEIDSGAT (gebruikerswaarneming: noodstop-melding, maar de
        // trein bleek fysiek nog MINUTENLANG door te rijden, tot in blokken die de software
        // allang gepasseerd dacht - terwijl de software dacht dat de trein nog in blok 7
        // stond): dit stuurde het ECHTE stopcommando alleen naar BlokVanLoc(trein) - de door
        // de software ONTHOUDEN positie. Maar door het voorbezetten heeft het VOLGENDE blok
        // op dat moment al hetzelfde snelheidscommando gekregen, vóórdat de aankomst daar
        // bevestigd is. Precies bij een noodstop vlak ná een vertrek/omkering (het moment
        // waarop dit gat het hardst toeslaat) kan de trein dus al op een ANDER bloadres
        // luisteren dan de software denkt - het stopcommando bereikte hem dan nooit. Zelfde
        // oplossing als bij StopAlleBekendeGeplaatsteLocs, MeldTreinVastgelopen,
        // KeerTreinIndienActief en de "geen kandidaat"-stop: stuur naar ELK blok in het
        // project, ongeacht waar de software denkt dat de loc staat. Dit is de
        // veiligheidskritische route (noodstop) - hier is geen enkele onzekerheid toelaatbaar.
        // GEVONDEN GAT (bytes-analyse: een stop-broadcast met richting "vooruit" terwijl de
        // loc op dat moment ECHT achteruit reed): in tegenstelling tot MeldTreinVastgelopen
        // en de "geen kandidaat"-stop in AutomatischeStapProberen (die allebei de
        // WERKELIJKE, actuele rijrichting van de trein gebruiken) stuurde dit altijd
        // hardcoded "vooruit", ongeacht wat de trein op dat moment echt deed. Een Dinamo-
        // snelheidscommando's richting-bit hoort bij een specifieke fysieke rijrichting - een
        // "stop"-commando met de VERKEERDE richting kan de decoder, afhankelijk van de
        // interne implementatie, nog een laatste keer kort laten schokken/bewegen voordat
        // hij echt stilstaat. Zoek daarom, als deze Trein een actieve rit heeft, de bijbehorende
        // RijdendeTrein op en gebruik diens werkelijke richting - exact dezelfde berekening
        // als elders (OmgekeerdeRijrichting-correctie inbegrepen); zonder actieve rit (trein
        // stond toch al stil, of reed alleen handmatig) blijft "vooruit" de veilige, neutrale
        // terugval.
        foreach (var trein in _treinBeheerder.Treinen)
        {
            if (trein.DecoderAdres <= 0) continue;
            var actieveRit = _actieveTreinen.FirstOrDefault(rt => rt.Trein == trein);
            bool vooruitVoorHardware = actieveRit != null
                ? (trein.OmgekeerdeRijrichting ? !actieveRit.RijdtVooruit : actieveRit.RijdtVooruit)
                : true;
            // BUG #45: via de voorrangs-wachtrij (urgent) i.p.v. de gewone wachtrij - in het log
            // van 10-10-2026 bleef dit stopcommando 40 s achter melderopvraag en wissel-
            // initialisatie hangen terwijl de loc gewoon doorreed.
            _hardwareBeheerder.StuurStopNaarAlleBlokken(trein.DecoderAdres, vooruitVoorHardware,
                _blokBeheerder.Blokken.Select(b => b.Nummer).Concat(AlleKruiswisselRijAdressen()), trein.DecoderStappen);
        }

        int aantal = _actieveTreinen.Count;
        if (aantal == 0)
        {
            StatusTekst.Text = $"NOODSTOP verstuurd naar de hardware{redenTekst}. Er stond in de software geen enkele trein als rijdend geregistreerd (wel een expliciet stopcommando naar elke geplaatste loc gestuurd, zie toelichting).";
            Log($"=== NOODSTOP geactiveerd{redenTekst} (geen actieve treinen in de software, wel expliciet snelheid=0 naar elke geplaatste loc gestuurd) ===");
            return;
        }
        foreach (var trein in _actieveTreinen.ToList())
            StopTrein(trein, $"NOODSTOP{redenTekst}: route '{trein.Route.Omschrijving}' direct gestopt.", magHerhalen: false, langzaamAfremmen: false);

        StatusTekst.Text = $"NOODSTOP{redenTekst}: {aantal} rijdende trein(en) direct gestopt.";
        Log($"=== NOODSTOP geactiveerd{redenTekst}: {aantal} trein(en) direct gestopt ===");
    }

    /// <summary>Stopt een rit - centrale plek voor ALLE manieren waarop een rit eindigt
    /// (bestemming bereikt, handmatig gestopt, gepauzeerd via verwijderen, noodstop).
    /// <paramref name="langzaamAfremmen"/>: GEVONDEN, BELANGRIJK GAT (gebruikersverzoek:
    /// "we willen de treinen altijd langzaam laten afremmen voor hij tot stilstand komt"):
    /// dit riep hier voorheen ALLEEN _treinBeheerder.ZetSnelheid(trein.Trein, 0) aan - dat
    /// is PUUR de weergavewaarde voor het Overzicht locomotieven, er werd HELEMAAL GEEN
    /// echt hardwarecommando verstuurd. Een normaal gestopte rit (bestemming bereikt, de
    /// "Stop deze loc"-knop, enz.) liet de fysieke loc dus gewoon op zijn laatst
    /// gecommandeerde snelheid doorrijden. Nu, standaard, een ECHTE, GELEIDELIJKE
    /// afremming (dezelfde ramp als bij aankomst op een station - LogSnelheidsopbouw,
    /// verdeeld over de bloklengte, dus geen abrupte sprong naar 0). Bij een NOODSTOP
    /// (VoerNoodstopUit hierboven) is dat bewust UIT: bij een onverwachte situatie moet de
    /// loc zo snel mogelijk stoppen, geleidelijk afremmen zou dan juist het probleem zijn
    /// dat we willen vermijden - bovendien is daar al apart een direct stopcommando naar
    /// alle bekende locs verstuurd (zie hierboven), dus dit zou toch alleen maar een
    /// overbodige, trage ramp toevoegen.</summary>
    private void StopTrein(RijdendeTrein trein, string? bericht, bool magHerhalen = true, bool langzaamAfremmen = true)
    {
        bool wasBezig = trein.Timer != null;
        trein.Timer?.Stop();
        trein.Timer = null;
        if (trein.Trein != null)
        {
            if (langzaamAfremmen) LogSnelheidsopbouw(trein, optrekken: false, trein.HuidigBlok);
            else _treinBeheerder.ZetSnelheid(trein.Trein, 0); // het ECHTE, abrupte stopcommando is voor dit geval al apart verstuurd (zie VoerNoodstopUit)
        }

        if (trein.HuidigBlok != null)
        {
            _blokBeheerder.ZetBezet(trein.HuidigBlok, false);
            LogSeinenVoorBlok(trein.HuidigBlok, false);
        }
        // GEVONDEN GAT (gebruikersmelding: "als ik de loc uit het blok haal blijven de
        // reserveringen staan"): trein.GereserveerdVolgendBlok - het blok dat AL gekozen/
        // gereserveerd is als volgende stap, maar nog niet daadwerkelijk bereikt (dus nog
        // niet in trein.Pad, dat pas bij BEVESTIGDE aankomst een blok toevoegt, zie
        // VerwerkAutomatischeAankomst) - werd hieronder NOOIT meegenomen, want de foreach-
        // lus loopt alleen over trein.Pad. Expliciet los vrijgeven, VOOR die lus (zodat het
        // ook meteen klopt als GereserveerdVolgendBlok toevallig al wél in Pad zou zitten).
        if (trein.GereserveerdVolgendBlok != null)
        {
            _blokBeheerder.ZetGereserveerd(trein.GereserveerdVolgendBlok, false);
            trein.GereserveerdVolgendBlok = null;
        }
        // eventuele nog-gereserveerde (maar niet bereikte) blokken van déze trein weer vrijgeven -
        // niet alle bezetting/reservering globaal wissen, want er kunnen andere treinen rijden.
        foreach (var blok in trein.Pad)
        {
            if (_blokBeheerder.IsGereserveerd(blok)) _blokBeheerder.ZetGereserveerd(blok, false);
            if (_blokBeheerder.IsStaart(blok))
            {
                _blokBeheerder.ZetStaart(blok, false);
                _blokBeheerder.ZetBezet(blok, false);
                // GEVONDEN GAT (gebruikersmelding: een "geen vrijmelding ontvangen"-
                // waarschuwing voor melder 136 kwam nog 10 sec ná een noodstop die de hele
                // rit al gestopt had): een lopende _staartWachtMelders-registratie voor dit
                // blok (zie StartStaartFase) werd hier nooit opgeruimd - dus de eigen
                // veiligheids-timeout van de staart-fase bleef gewoon doorlopen (en zou een
                // latere, late vrijmelding voor deze melder ook nog aan een allang gestopte
                // rit proberen te koppelen).
                foreach (var meldernummer in _staartWachtMelders.Where(kv => kv.Value == blok).Select(kv => kv.Key).ToList())
                    _staartWachtMelders.Remove(meldernummer);
            }
        }

        _actieveTreinen.Remove(trein);

        if (bericht != null)
        {
            StatusTekst.Text = bericht;
            if (wasBezig) Log($"--- [Route '{trein.Route.Omschrijving}'] {bericht} ({_actieveTreinen.Count} trein(en) nog actief) ---");
        }
        Redraw();
        ProbeerWachtrij();

        // Herhalen: een simpele pendeldienst-lus. Na een korte pauze "teleporteert" de trein
        // terug naar het startblok en begint de route opnieuw - er wordt geen terugrit
        // gesimuleerd, dit is bewust een vereenvoudiging. Geldt niet bij handmatig
        // stoppen/venster sluiten (magHerhalen=false van die aanroepen).
        if (magHerhalen && trein.Route.Herhalen)
        {
            var afgerondeRoute = trein.Route;
            var eindblok = trein.HuidigBlok ?? afgerondeRoute.Startblok;
            var gebruikteTrein = trein.Trein;

            var herhaalTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(HerhalenPauzeSeconden * SimulatieInstellingen.VertragingsFactor) };
            herhaalTimer.Tick += (_, _) =>
            {
                herhaalTimer.Stop();
                if (_venstergesloten) return;

                var oorspronkelijkeSelectie = RoutesLijst.SelectedItem;
                var oorspronkelijkeTreinKeuze = TreinCombo.SelectedItem;

                if (!afgerondeRoute.IsAutomatischeTerugrit)
                {
                    // Heenroute is klaar - GEEN teleportatie: stuur een echte terugrit terug
                    // naar het startblok van de heenroute, via automatisch rijden (dus het
                    // eerste vrije/toegestane vervolgblok - dat kan dezelfde weg terug zijn,
                    // of een andere weg, precies zoals gevraagd).
                    var terugrit = new Treinroute
                    {
                        Omschrijving = $"{afgerondeRoute.Omschrijving} (terugrit)",
                        Startblok = eindblok,
                        Bestemmingsblok = afgerondeRoute.Startblok,
                        Automatisch = true,
                        Herhalen = true,
                        IsAutomatischeTerugrit = true,
                        OorspronkelijkeRoute = afgerondeRoute,
                        Treintype = afgerondeRoute.Treintype
                    };
                    _routeBeheerder.Treinroutes.Add(terugrit);
                    VulRoutesLijst();
                    RoutesLijst.SelectedItem = terugrit;
                    TreinCombo.SelectedItem = gebruikteTrein;
                    Log($"--- Route '{afgerondeRoute.Omschrijving}' klaar - terugrit gestart naar startblok {afgerondeRoute.Startblok.Nummer} ---");
                    StartenMetRijden(opstelrit: false);
                }
                else
                {
                    // Terugrit is pas écht "aangekomen" als hij daadwerkelijk het bestemmingsblok
                    // (het startblok van de heenroute) heeft bereikt - niet bij een doodlopend
                    // spoor onderweg. Zonder deze check zou de heenroute onterecht herstarten
                    // terwijl de trein ergens halverwege is blijven steken.
                    bool echtAangekomen = eindblok == afgerondeRoute.Bestemmingsblok;
                    var heenroute = afgerondeRoute.OorspronkelijkeRoute;
                    _routeBeheerder.Treinroutes.Remove(afgerondeRoute);
                    VulRoutesLijst();

                    if (echtAangekomen && heenroute != null)
                    {
                        RoutesLijst.SelectedItem = heenroute;
                        TreinCombo.SelectedItem = gebruikteTrein;
                        Log($"--- Terugrit aangekomen bij blok {afgerondeRoute.Bestemmingsblok?.Nummer} - route '{heenroute.Omschrijving}' begint opnieuw ---");
                        StartenMetRijden(opstelrit: false);
                    }
                    else if (heenroute != null)
                    {
                        Log($"--- Terugrit van route '{heenroute.Omschrijving}' liep vast bij blok {eindblok.Nummer}, niet aangekomen bij startblok {afgerondeRoute.Bestemmingsblok?.Nummer} - Herhalen gestopt, grijp zelf in ---");
                    }
                }

                RoutesLijst.SelectedItem = oorspronkelijkeSelectie;
                TreinCombo.SelectedItem = oorspronkelijkeTreinKeuze;
            };
            herhaalTimer.Start();
        }
    }

    private void InWachtrijZetten_Click(object sender, RoutedEventArgs e)
    {
        if (RoutesLijst.SelectedItem is not Treinroute route)
        {
            StatusTekst.Text = "Selecteer eerst een route in de lijst.";
            return;
        }
        if (_wachtrij.Contains(route))
        {
            StatusTekst.Text = $"Route '{route.Omschrijving}' staat al in de wachtrij.";
            return;
        }
        _wachtrij.Add(route);
        Log($"--- Route '{route.Omschrijving}' in de wachtrij gezet{(route.HogePrioriteit ? " (hoge prioriteit)" : "")} ---");
        VulWachtrijLijst();
        ProbeerWachtrij();
    }

    /// <summary>Probeert elke route in de wachtrij te starten, in volgorde van prioriteit
    /// (HogePrioriteit eerst). Aangeroepen na elke StopTrein, want dan kan er ruimte
    /// vrijgekomen zijn. Routes die nog steeds niet kunnen, blijven gewoon staan.</summary>
    private void ProbeerWachtrij()
    {
        if (_wachtrij.Count == 0) return;

        var oorspronkelijkeSelectie = RoutesLijst.SelectedItem;
        foreach (var route in _wachtrij.OrderByDescending(r => r.HogePrioriteit).ToList())
        {
            RoutesLijst.SelectedItem = route;
            if (StartenMetRijden(opstelrit: false, stil: true))
            {
                _wachtrij.Remove(route);
                Log($"--- Route '{route.Omschrijving}' uit de wachtrij gestart ---");
            }
        }
        RoutesLijst.SelectedItem = oorspronkelijkeSelectie;
        VulWachtrijLijst();
    }

    private void VulWachtrijLijst()
    {
        // Elke keer al een NIEUWE lijst (door .ToList()), dus de tussenstap via null is
        // hier niet eens nodig - gewoon direct toewijzen is voldoende en simpeler.
        WachtrijLijst.ItemsSource = _wachtrij.OrderByDescending(r => r.HogePrioriteit)
            .Select(r => r.HogePrioriteit ? $"★ {r.Omschrijving}" : r.Omschrijving)
            .ToList();
    }

    private void SchemaCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_bouwModusActief) return;

        var blok = BlokOpPositie(e.GetPosition(SchemaCanvas));
        if (blok is null) return;

        if (_routeInOpbouw is null)
        {
            _routeInOpbouw = _routeBeheerder.NieuweTreinroute(blok, _pendingNaam ?? "Route");
            Log($"Startblok {blok.Nummer} gekozen.");
            WerkStatusBij();
            return;
        }

        var (huidig, mogelijkheden, compleet) = _routeBeheerder.VolgendeKeuze(_routeInOpbouw, _blokBeheerder);
        if (compleet) return; // build mode had al afgesloten moeten zijn
        if (mogelijkheden.Count <= 1) return; // geen echte keuze, klikken doet hier niets

        if (!mogelijkheden.Contains(blok))
        {
            StatusTekst.Text = $"Blok {blok.Nummer} is nu niet mogelijk vanaf blok {huidig.Nummer}. Kies een van de gemarkeerde (oranje) blokken.";
            Log($"Ongeldige keuze: blok {blok.Nummer} is niet bereikbaar vanaf blok {huidig.Nummer}.");
            return;
        }

        _routeInOpbouw.Keuzeblokken.Add(blok);
        Log($"Keuze op splitsing bij blok {huidig.Nummer}: blok {blok.Nummer} gekozen.");
        WerkStatusBij();
    }

    private Blok BerekenHuidigBlok() =>
        _routeBeheerder.BerekenVolledigPad(_routeInOpbouw!, _blokBeheerder)[^1];

    private void WerkStatusBij()
    {
        var (huidig, mogelijkheden, compleet) = _routeBeheerder.VolgendeKeuze(_routeInOpbouw!, _blokBeheerder);

        if (compleet)
        {
            Log($"Route '{_routeInOpbouw!.Omschrijving}' compleet — eindigt doodlopend bij blok {huidig.Nummer}.");
            AfsluitenRoute($"Route '{_routeInOpbouw!.Omschrijving}' compleet — eindigt doodlopend bij blok {huidig.Nummer}.");
        }
        else
        {
            StatusTekst.Text = $"Bij blok {huidig.Nummer} is er een keuze: klik een van de gemarkeerde blokken ({string.Join(", ", mogelijkheden.Select(b => b.Nummer))}), of druk 'Klaar' om hier te stoppen.";
            Redraw();
        }
    }

    private void AfsluitenRoute(string statusBericht)
    {
        _bouwModusActief = false;
        StatusTekst.Text = statusBericht;
        VulRoutesLijst();
        RoutesLijst.SelectedItem = _routeInOpbouw;
        _routeInOpbouw = null;
        Redraw();
    }

    // --- Routelijst -------------------------------------------------

    /// <summary>Ververst de routelijst. GEEN ItemsSource=null-dan-opnieuw-zetten meer -
    /// dat destructieve patroon bleek een InvalidOperationException ("ItemsControl is
    /// inconsistent with its items source") te kunnen geven zodra dit kort na elkaar of
    /// binnen elkaar aangeroepen wordt (bijv. door Herhalen/terugritten die elkaar snel
    /// opvolgen). Items.Refresh() is de door WPF bedoelde, veilige manier om een ListBox
    /// te laten zien dat de onderliggende lijst is gewijzigd.</summary>
    private void VulRoutesLijst()
    {
        if (RoutesLijst.ItemsSource == null)
            RoutesLijst.ItemsSource = _routeBeheerder.Treinroutes;
        else
            RoutesLijst.Items.Refresh();
    }

    private void RoutesLijst_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_bouwModusActief) return;
        if (RoutesLijst.SelectedItem is Treinroute route)
        {
            var pad = _routeBeheerder.BerekenVolledigPad(route, _blokBeheerder);
            StatusTekst.Text = $"Route '{route.Omschrijving}': {string.Join(" -> ", pad.Select(b => b.Nummer))}";
            TreintypeCombo.SelectedItem = route.Treintype;
            RouteMaxSnelheidBox.Text = route.MaxSnelheid.ToString(CultureInfo.InvariantCulture);
            BlijfWachtenBox.IsChecked = route.BlijfWachtenInBestemming;
            HogePrioriteitBox.IsChecked = route.HogePrioriteit;
            (route.DagdeelGeldigheid switch
            {
                DagdeelGeldigheid.AlleenOchtend => DagdeelOchtendBox,
                DagdeelGeldigheid.AlleenMiddag => DagdeelMiddagBox,
                _ => DagdeelBeideBox
            }).IsChecked = true;
            HerhalenBox.IsChecked = route.Herhalen;
            GeplandeVertrektijdBox.Text = route.GeplandeVertrektijd?.ToString(@"hh\:mm") ?? "";
            BestemmingsblokCombo.SelectedItem = route.Bestemmingsblok;
            AutomatischBox.IsChecked = route.Automatisch;
            InfoBox.Text = route.Info;
            VulGeldigheidUI(route);
        }
        Redraw();
    }

    /// <summary>Zet de radiobutton + de twee selectielijstjes op wat er in de route is
    /// vastgelegd, en toont/verbergt de lijstjes al naar gelang welke soort Geldigheid dat
    /// nodig heeft. _geldigheidBijwerkenActief voorkomt dat het opnieuw-vullen van de
    /// ListBox-selectie zelf weer als een wijziging door de gebruiker gezien wordt.</summary>
    private void VulGeldigheidUI(Treinroute route)
    {
        _geldigheidBijwerkenActief = true;

        (route.Geldigheid switch
        {
            GeldigheidsSoort.Treintype => GeldigheidTreintypeBox,
            GeldigheidsSoort.TreintypeEnRijwindow => GeldigheidTreintypeEnRijwindowBox,
            GeldigheidsSoort.Rijwindow => GeldigheidRijwindowBox,
            GeldigheidsSoort.Treinstel => GeldigheidTreinstelBox,
            GeldigheidsSoort.Locomotief => GeldigheidLocomotiefBox,
            _ => GeldigheidAlleBox
        }).IsChecked = true;

        GeldigeTreintypesLijst.SelectedItems.Clear();
        foreach (var type in route.GeldigeTreintypes)
            if (GeldigeTreintypesLijst.Items.Contains(type)) GeldigeTreintypesLijst.SelectedItems.Add(type);

        GeldigeTreinenLijst.SelectedItems.Clear();
        foreach (var trein in route.GeldigeTreinen)
            if (GeldigeTreinenLijst.Items.Contains(trein)) GeldigeTreinenLijst.SelectedItems.Add(trein);

        UitgeslotenWisselsLijst.SelectedItems.Clear();
        foreach (var wissel in route.UitgeslotenWissels)
            if (UitgeslotenWisselsLijst.Items.Contains(wissel)) UitgeslotenWisselsLijst.SelectedItems.Add(wissel);

        AlternatieveStartblokkenLijst.SelectedItems.Clear();
        foreach (var blok in route.AlternatieveStartblokken)
            if (AlternatieveStartblokkenLijst.Items.Contains(blok)) AlternatieveStartblokkenLijst.SelectedItems.Add(blok);

        bool toontTreintypes = route.Geldigheid is GeldigheidsSoort.Treintype or GeldigheidsSoort.TreintypeEnRijwindow;
        bool toontTreinen = route.Geldigheid is GeldigheidsSoort.TreintypeEnRijwindow or GeldigheidsSoort.Rijwindow or GeldigheidsSoort.Locomotief;
        GeldigeTreintypesLabel.Visibility = toontTreintypes ? Visibility.Visible : Visibility.Collapsed;
        GeldigeTreintypesLijst.Visibility = toontTreintypes ? Visibility.Visible : Visibility.Collapsed;
        GeldigeTreinenLabel.Visibility = toontTreinen ? Visibility.Visible : Visibility.Collapsed;
        GeldigeTreinenLijst.Visibility = toontTreinen ? Visibility.Visible : Visibility.Collapsed;

        _geldigheidBijwerkenActief = false;
    }

    private void Geldigheid_Changed(object sender, RoutedEventArgs e)
    {
        if (_geldigheidBijwerkenActief) return;
        if (RoutesLijst.SelectedItem is not Treinroute route) return;

        route.Geldigheid = sender switch
        {
            var s when s == GeldigheidTreintypeBox => GeldigheidsSoort.Treintype,
            var s when s == GeldigheidTreintypeEnRijwindowBox => GeldigheidsSoort.TreintypeEnRijwindow,
            var s when s == GeldigheidRijwindowBox => GeldigheidsSoort.Rijwindow,
            var s when s == GeldigheidTreinstelBox => GeldigheidsSoort.Treinstel,
            var s when s == GeldigheidLocomotiefBox => GeldigheidsSoort.Locomotief,
            _ => GeldigheidsSoort.Alle
        };
        VulGeldigheidUI(route);
        StatusTekst.Text = $"Route '{route.Omschrijving}' geldt nu voor: {route.Geldigheid}.";
    }

    private void GeldigeTreintypesLijst_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_geldigheidBijwerkenActief) return;
        if (RoutesLijst.SelectedItem is not Treinroute route) return;
        route.GeldigeTreintypes = GeldigeTreintypesLijst.SelectedItems.Cast<Treintype>().ToList();
    }

    private void GeldigeTreinenLijst_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_geldigheidBijwerkenActief) return;
        if (RoutesLijst.SelectedItem is not Treinroute route) return;
        route.GeldigeTreinen = GeldigeTreinenLijst.SelectedItems.Cast<Trein>().ToList();
    }

    private void UitgeslotenWisselsLijst_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_geldigheidBijwerkenActief) return;
        if (RoutesLijst.SelectedItem is not Treinroute route) return;
        route.UitgeslotenWissels = UitgeslotenWisselsLijst.SelectedItems.Cast<Wissel>().ToList();
    }

    private void AlternatieveStartblokkenLijst_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_geldigheidBijwerkenActief) return;
        if (RoutesLijst.SelectedItem is not Treinroute route) return;
        route.AlternatieveStartblokken = AlternatieveStartblokkenLijst.SelectedItems.Cast<Blok>().Where(b => b != route.Startblok).ToList();
    }

    private void BlijfWachtenBox_Changed(object sender, RoutedEventArgs e)
    {
        if (RoutesLijst.SelectedItem is Treinroute route)
            route.BlijfWachtenInBestemming = BlijfWachtenBox.IsChecked == true;
    }

    private void HogePrioriteitBox_Changed(object sender, RoutedEventArgs e)
    {
        if (RoutesLijst.SelectedItem is Treinroute route)
            route.HogePrioriteit = HogePrioriteitBox.IsChecked == true;
    }

    private void GeplandeVertrektijdZetten_Click(object sender, RoutedEventArgs e)
    {
        if (RoutesLijst.SelectedItem is not Treinroute route)
        {
            StatusTekst.Text = "Selecteer eerst een route in de lijst.";
            return;
        }
        string tekst = GeplandeVertrektijdBox.Text.Trim();
        if (string.IsNullOrEmpty(tekst))
        {
            route.GeplandeVertrektijd = null;
            StatusTekst.Text = $"Route '{route.Omschrijving}' heeft geen dienstregeling-vertrektijd meer.";
            return;
        }
        if (!TimeSpan.TryParseExact(tekst, @"hh\:mm", CultureInfo.InvariantCulture, out var vertrektijd))
        {
            StatusTekst.Text = "Vul een geldige tijd in het formaat uu:mm in (bijv. 14:05), of laat leeg voor geen dienstregeling.";
            return;
        }
        route.GeplandeVertrektijd = vertrektijd;
        StatusTekst.Text = $"Route '{route.Omschrijving}' krijgt een geplande vertrektijd van {tekst}.";
    }

    private void KlokActiefToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (KlokActiefBox.IsChecked == true) _snelleKlokBeheerder.Start();
        else _snelleKlokBeheerder.Stop();
    }

    private void KlokSnelheidZetten_Click(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(KlokSnelheidBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double seconden) || seconden <= 0)
        {
            StatusTekst.Text = "Vul een geldig aantal seconden per modelminuut in (groter dan 0).";
            return;
        }
        _snelleKlokBeheerder.Instellingen.SecondenPerModelMinuut = seconden;
        StatusTekst.Text = $"Kloksnelheid: {seconden} sec per modelminuut.";
    }

    /// <summary>Exporteert een leesbaar "spoorboekje": alle routes met een geplande
    /// vertrektijd, op tijd gesorteerd - puur ter referentie/print, geen back-up-formaat.</summary>
    private void SpoorboekjeExporteren_Click(object sender, RoutedEventArgs e)
    {
        var routesMetTijd = _routeBeheerder.Treinroutes
            .Where(r => r.GeplandeVertrektijd != null)
            .OrderBy(r => r.GeplandeVertrektijd)
            .ToList();
        if (routesMetTijd.Count == 0)
        {
            MessageBox.Show(this, "Er zijn nog geen routes met een geplande vertrektijd - stel die eerst in bij de betreffende route (zie 'Geplande vertrektijd' hieronder).", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dialoog = new Microsoft.Win32.SaveFileDialog { Filter = "Tekstbestand (*.txt)|*.txt", FileName = "spoorboekje.txt" };
        if (dialoog.ShowDialog(this) != true) return;

        var regels = new List<string> { "Dienstregeling", new string('=', 40), "" };
        foreach (var route in routesMetTijd)
        {
            string treintype = route.Treintype?.Omschrijving ?? "onbekend treintype";
            string bestemming = route.Bestemmingsblok != null ? $"blok {route.Bestemmingsblok.Nummer}" : "geen vast bestemmingsblok";
            regels.Add($"{route.GeplandeVertrektijd:hh\\:mm}  {route.Omschrijving}  ({treintype}, vanaf blok {route.Startblok.Nummer}, naar {bestemming})");
        }
        try
        {
            System.IO.File.WriteAllLines(dialoog.FileName, regels, System.Text.Encoding.UTF8);
            MessageBox.Show(this, $"Spoorboekje met {routesMetTijd.Count} route(s) geëxporteerd naar {dialoog.FileName}.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Exporteren mislukt: {ex.Message}", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Dagdeel_Changed(object sender, RoutedEventArgs e)
    {
        if (RoutesLijst.SelectedItem is not Treinroute route) return;
        route.DagdeelGeldigheid = DagdeelOchtendBox.IsChecked == true ? DagdeelGeldigheid.AlleenOchtend
            : DagdeelMiddagBox.IsChecked == true ? DagdeelGeldigheid.AlleenMiddag
            : DagdeelGeldigheid.Beide;
    }

    private void HerhalenBox_Changed(object sender, RoutedEventArgs e)
    {
        if (RoutesLijst.SelectedItem is Treinroute route)
            route.Herhalen = HerhalenBox.IsChecked == true;
    }

    private void ZetBestemmingsblok_Click(object sender, RoutedEventArgs e)
    {
        if (RoutesLijst.SelectedItem is not Treinroute route)
        {
            StatusTekst.Text = "Selecteer eerst een route in de lijst.";
            return;
        }
        route.Bestemmingsblok = BestemmingsblokCombo.SelectedItem as Blok;
        StatusTekst.Text = route.Bestemmingsblok != null
            ? $"Route '{route.Omschrijving}' moet nu eindigen bij blok {route.Bestemmingsblok.Nummer}."
            : $"Route '{route.Omschrijving}' heeft geen vastgelegd bestemmingsblok meer.";
    }

    private void AutomatischBox_Changed(object sender, RoutedEventArgs e)
    {
        if (RoutesLijst.SelectedItem is Treinroute route)
            route.Automatisch = AutomatischBox.IsChecked == true;
    }

    private void InfoBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (RoutesLijst.SelectedItem is Treinroute route)
            route.Info = InfoBox.Text;
    }

    private void ZetTreintype_Click(object sender, RoutedEventArgs e)
    {
        if (RoutesLijst.SelectedItem is not Treinroute route)
        {
            StatusTekst.Text = "Selecteer eerst een route in de lijst.";
            return;
        }
        route.Treintype = TreintypeCombo.SelectedItem as Treintype;
        VulRoutesLijst();
        RoutesLijst.SelectedItem = route;
        StatusTekst.Text = route.Treintype != null
            ? $"Route '{route.Omschrijving}' geldt nu voor treintype '{route.Treintype.Omschrijving}'."
            : $"Route '{route.Omschrijving}' heeft geen specifiek treintype meer (standaardtijden).";
    }

    /// <summary>Route-eigen maximumsnelheid: Koploper's regel "de toegestane snelheid is
    /// de laagste snelheid die geldt voor rijweg, locomotief of treinsoort" - dit is de
    /// derde, route-eigen limiet naast blok en treintype.</summary>
    private void ZetRouteMaxSnelheid_Click(object sender, RoutedEventArgs e)
    {
        if (RoutesLijst.SelectedItem is not Treinroute route)
        {
            StatusTekst.Text = "Selecteer eerst een route in de lijst.";
            return;
        }
        if (!double.TryParse(RouteMaxSnelheidBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double maxSnelheid) || maxSnelheid < 0)
        {
            StatusTekst.Text = "Vul een geldig getal in (0 of hoger; 0 = geen eigen limiet).";
            return;
        }
        route.MaxSnelheid = maxSnelheid;
        VulRoutesLijst();
        RoutesLijst.SelectedItem = route;
        StatusTekst.Text = maxSnelheid > 0
            ? $"Route '{route.Omschrijving}' heeft nu een eigen maximumsnelheid van {maxSnelheid} km/u."
            : $"Route '{route.Omschrijving}' heeft geen eigen snelheidslimiet meer (alleen blok/treintype tellen mee).";
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F1)
        {
            Gebruiksaanwijzing_Click(sender, e);
            return;
        }
        if (e.Key == Key.Escape && _bouwModusActief)
        {
            if (_routeInOpbouw != null)
            {
                Log($"Route '{_routeInOpbouw.Omschrijving}' geannuleerd (Escape).");
                _routeBeheerder.VerwijderTreinroute(_routeInOpbouw);
            }
            _routeInOpbouw = null;
            _bouwModusActief = false;
            StatusTekst.Text = "Geannuleerd. Klik 'Nieuwe route' om opnieuw te beginnen.";
            VulRoutesLijst();
            Redraw();
        }
        else if (e.Key == Key.Escape && !_bouwModusActief)
        {
            // Buiten het opbouwen van een nieuwe route om is Escape de noodstop-sneltoets -
            // zo hoef je niet eerst met de muis naar de knop te zoeken.
            Noodstop_Click(sender, e);
        }
        else if (e.Key == Key.F6)
        {
            // F6 is Koploper's EIGEN sneltoets voor "stop alle treinen" - naast Escape (onze
            // eigen toevoeging) ook deze aanhouden, voor wie het echte Koploper gewend is.
            Noodstop_Click(sender, e);
        }
        else if (e.Key == Key.Delete && !_bouwModusActief && RoutesLijst.SelectedItem is Treinroute geselecteerd)
        {
            var vraag = MessageBox.Show(this, $"Route '{geselecteerd.Omschrijving}' verwijderen? Dit kan niet ongedaan gemaakt worden.",
                "Route verwijderen", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (vraag != MessageBoxResult.Yes) return;

            Log($"Route '{geselecteerd.Omschrijving}' verwijderd.");
            _routeBeheerder.VerwijderTreinroute(geselecteerd);
            VulRoutesLijst();
            StatusTekst.Text = "Route verwijderd.";
            Redraw();
        }
    }

    // --- Hit-testing & tekenen -------------------------------------------------

    private Blok? BlokOpPositie(Point positie)
    {
        foreach (var blok in _blokBeheerder.Blokken)
        {
            if (positie.X >= blok.SchemaX && positie.X <= blok.SchemaX + BlokGrootte &&
                positie.Y >= blok.SchemaY && positie.Y <= blok.SchemaY + BlokGrootte)
                return blok;
        }
        return null;
    }

    private void Redraw()
    {
        SchemaCanvas.Children.Clear();

        foreach (var relatie in _blokBeheerder.Relaties)
            TekenRelatieLijn(relatie);

        var opbouwPad = _routeInOpbouw != null
            ? _routeBeheerder.BerekenVolledigPad(_routeInOpbouw, _blokBeheerder)
            : new List<Blok>();

        var mogelijkheden = new List<Blok>();
        if (_routeInOpbouw != null)
        {
            var (_, mog, compleet) = _routeBeheerder.VolgendeKeuze(_routeInOpbouw, _blokBeheerder);
            if (!compleet) mogelijkheden = mog;
        }

        var weergavePad = !_bouwModusActief && RoutesLijst.SelectedItem is Treinroute geselecteerd
            ? _routeBeheerder.BerekenVolledigPad(geselecteerd, _blokBeheerder)
            : new List<Blok>();

        foreach (var blok in _blokBeheerder.Blokken)
            TekenBlok(blok, opbouwPad.Contains(blok), mogelijkheden.Contains(blok), weergavePad.Contains(blok));

        VulActieveTreinenLijst();
    }

    /// <summary>Toont de actieve treinen als lijst met statustekst - net als het echte
    /// Koploper "rijwindow": de kleuren op de blokken/lijnen zelf blijven het domein van het
    /// baanontwerp (hou dat venster open om de trein echt te zien rijden), hier gaat het om
    /// een overzicht van wélke routes er nu draaien en waar ze staan.</summary>
    private void VulActieveTreinenLijst()
    {
        ActieveTreinenLijst.Items.Clear();
        foreach (var trein in _actieveTreinen)
            ActieveTreinenLijst.Items.Add(trein);
        VulSpanningKnop();
    }

    /// <summary>Het rode/groene "spiegelei" uit Koploper's eigen werkbalk: spanning/centrale
    /// eenheid aan/uit. Kan alleen omgeschakeld worden als er geen enkele trein rijdt - net
    /// als in het echte Koploper ("Zodra er een trein gaat rijden worden de spiegeleieren
    /// grijs en kunnen niet geactiveerd worden").</summary>
    private void Spanning_Click(object sender, RoutedEventArgs e)
    {
        if (_actieveTreinen.Count > 0) return; // zou niet moeten kunnen (knop is dan disabled), voor de zekerheid
        _spanningAan = !_spanningAan;
        VulSpanningKnop();
        Log(_spanningAan ? "Spanning AAN gezet." : "Spanning UIT gezet.");
        StatusTekst.Text = _spanningAan ? "Spanning aan." : "Spanning uit - er kan nu geen trein rijden.";
    }

    private void VulSpanningKnop()
    {
        SpanningEllipse.Fill = _spanningAan ? Brushes.ForestGreen : Brushes.Firebrick;
        SpanningKnop.IsEnabled = _actieveTreinen.Count == 0;
    }

    /// <summary>Zoals in Koploper's "Overzicht locomotieven": een STOP-knop per individuele
    /// trein, los van de route-brede/globale opties, om alleen déze rit direct te stoppen.</summary>
    private void StopGeselecteerdeTrein_Click(object sender, RoutedEventArgs e)
    {
        if (ActieveTreinenLijst.SelectedItem is not RijdendeTrein trein)
        {
            StatusTekst.Text = "Selecteer eerst een rijdende trein in de lijst.";
            return;
        }
        StopTrein(trein, $"Trein op route '{trein.Route.Omschrijving}' individueel gestopt (Stop-knop).", magHerhalen: false);
    }

    /// <summary>Koploper's "Go"-knop: vroegtijdig vertrek, de resterende wachttijd
    /// overslaan door de lopende stap-timer meteen te laten afgaan.
    ///
    /// GEVONDEN, ERNSTIGE BUG (gebruikersmelding: "hij stopte deze keer in de 1e sectie
    /// van blok 11 waar hij eerder gewoon doorreed naar de 3e sectie" - veroorzaakt door
    /// een TWEEDE druk op "Go" terwijl de trein al MIDDEN in een overgang zat, wachtend op
    /// een ECHTE bezetmelding): "wachttijd overslaan" herstartte voorheen ONVOORWAARDELIJK
    /// de kandidaatkeuze (AutomatischeStapProberen/PlanVolgendeGebeurtenis) - ook voor een
    /// trein die op dat moment op een ECHTE bezetmelding wachtte (trein.WachtOpMeldernNummer
    /// != null), fysiek al onderweg naar een AL GEKOZEN volgend blok. Dat koos dan een
    /// NIEUWE (mogelijk andere) kandidaat en zette de wissels OPNIEUW - terwijl de fysieke
    /// loc al MIDDEN over die wissels reed, met een verkeerd gerouteerde/vastgelopen loc
    /// tot gevolg. "Wachttijd overslaan" is alleen zinvol voor een GESIMULEERDE vertraging
    /// (waar "doe maar alsof de tijd al verstreken is" onschadelijk is) - nooit voor het
    /// wachten op iets ECHTS, waar dat precies het probleem is dat MeldTreinVastgelopen
    /// net had opgelost. Een trein die op een echte melding wacht wordt hier nu dus met
    /// rust gelaten.</summary>
    private void GaGeselecteerdeTrein_Click(object sender, RoutedEventArgs e)
    {
        if (ActieveTreinenLijst.SelectedItem is not RijdendeTrein trein)
        {
            StatusTekst.Text = "Selecteer eerst een rijdende trein in de lijst.";
            return;
        }
        if (trein.WachtOpMeldernNummer != null)
        {
            StatusTekst.Text = $"Trein op route '{trein.Route.Omschrijving}' wacht al op een ECHTE bezetmelding (melder {trein.WachtOpMeldernNummer}) - 'wachttijd overslaan' is dan niet veilig, met rust gelaten.";
            return;
        }
        trein.Timer?.Stop();
        if (trein.Route.Automatisch) AutomatischeStapProberen(trein);
        else PlanVolgendeGebeurtenis(trein);
        StatusTekst.Text = $"Trein op route '{trein.Route.Omschrijving}': wachttijd overgeslagen (Go-knop).";
        Log($"[Route '{trein.Route.Omschrijving}'] Vroegtijdig vertrek (Go-knop) - wachttijd overgeslagen.");
    }

    /// <summary>Of deze trein op dit moment al onderdeel is van een actieve rit - gebruikt
    /// door MainWindow om te bepalen welke geplaatste locs nog een NIEUWE rit nodig hebben
    /// bij de "Go (alle treinen)"-knop.</summary>
    public bool IsTreinActief(Trein trein) => _actieveTreinen.Any(t => t.Trein == trein);

    /// <summary>GEVONDEN ARCHITECTUURGAT (gebruikersinzicht, letterlijk correct doorzien:
    /// "een loc staat tijdelijk in meerdere blokken tegelijk doordat hij een bepaalde
    /// lengte heeft - het oude blok blijft nog bezet terwijl de eerste bezetmelder van het
    /// NIEUWE blok al afgaat" - en dat gaf een spookmelding): bij vrij automatisch rijden
    /// kiest (en reserveert) AutomatischeStapProberen het "volgende blok" pas NADAT de
    /// aankomst in het HUIDIGE blok al bevestigd is, gevolgd door een korte
    /// verwerkingspauze (wachttijdNaAankomst, meestal ~1 sec) VOORDAT de software
    /// daadwerkelijk kiest/reserveert - maar de trein zelf blijft gewoon fysiek doorrijden
    /// tijdens die pauze (zie VerwerkAutomatischeAankomst: optrekken gebeurt al bij
    /// aankomst zelf). Bij een kort blok/lange trein/trage software-cyclus kan de
    /// voorkant van de trein het volgende blok dus al fysiek bereiken (en diens
    /// Voorblok-melder laten afgaan) VOORDAT de software dat blok als kandidaat gekozen
    /// heeft - een terechte, geen spook-melding.
    ///
    /// Wordt door MainWindow.Bezetmelding_VanHardware aangeroepen VOORDAT die een
    /// spookmelding overweegt: is "blok" een fysiek geldige eerstvolgende stap voor een
    /// trein die er met zijn HUIDIGE blok vlak naast staat, en had de software voor DIE
    /// trein nog HELEMAAL GEEN kandidaat gekozen (GereserveerdVolgendBlok is null)? Dan is
    /// dit geen glitch maar simpelweg de fysieke werkelijkheid die de software's eigen
    /// (nog niet gemaakte) keuze voor is - de software volgt dan alsnog direct: reserveert
    /// het blok en verwerkt de overgang meteen, i.p.v. te wachten tot zijn eigen volgende
    /// beslismoment. Bij een VASTE route komt dit niet voor (die reserveert het HELE pad
    /// al bij vertrek, dus zo'n blok is dan al lang gereserveerd) - alleen relevant voor
    /// route.Automatisch. Retourneert true als dit event hiermee is afgehandeld (MainWindow
    /// moet dan geen spookmelding meer overwegen).</summary>
    public bool ProbeerVroegeAankomstBevestiging(Blok blok, bool bezet)
    {
        if (!bezet) return false;
        foreach (var trein in _actieveTreinen)
        {
            if (!trein.Route.Automatisch) continue; // vaste routes reserveren hun hele pad al vooraf, zie hierboven
            if (trein.HuidigBlok is null || trein.GereserveerdVolgendBlok != null) continue;
            if (blok == trein.VorigBlok && VereistKeren(trein.HuidigBlok, blok)) continue; // een terugrit naar een ECHT keerpunt is GEEN "vroege bevestiging" - laat VindTreinDieMogelijkTerugrijdt dit als rijrichting-signaal oppikken. Bij een lus (VereistKeren=false, zie VertrekVanBlokUitvoeren) is dit gewoon een normale, voorwaartse ronde - die WEL als vroege bevestiging mag gelden.
            if (!_blokBeheerder.VolgendeBlokken(trein.HuidigBlok).Contains(blok)) continue;

            var huidig = trein.HuidigBlok;
            _blokBeheerder.ZetGereserveerd(blok, true);
            trein.GereserveerdVolgendBlok = blok;
            ZetWisselsTussenBlokken(huidig, blok);
            // GEVONDEN, KRITIEK GAT (gebruikerswaarneming: loc bleef stilstaan PRECIES op
            // een kruiswissel-sectie met een eigen rijstroom-adres (Model.Kruiswissel.
            // RijAdres) - de trein was al fysiek voorbij blok7 EN over de kruiswissel heen,
            // maar dit "vroege aankomst"-pad stuurde NOOIT een snelheidscommando voor DEZE
            // overgang: het vertrouwt volledig op de snelheid die blok6/7 al eerder kregen
            // (via de normale VertrekVanBlokUitvoeren-voorbezetten-route, die HIER nooit
            // aangeroepen wordt). Voor een gewoon blok is dat onschadelijk (geen ander blok
            // ertussen dat stroom nodig heeft) - maar een kruiswissel-sectie met een eigen
            // adres kreeg zo NOOIT stroom tijdens de normale rit, alleen (veel te laat) via
            // de veiligheids-broadcasts. Stuur daarom hier alsnog expliciet de trein se
            // HUIDIGE snelheid naar elk kruiswissel-rijadres dat op dit stukje pad ligt.
            if (trein.Trein != null && trein.Trein.DecoderAdres > 0)
            {
                bool vooruitVoorKruiswissel = trein.Trein.OmgekeerdeRijrichting ? !trein.RijdtVooruit : trein.RijdtVooruit;
                var kruiswisselAdressenVroeg = KruiswisselRijAdressenOpPad(huidig, blok);
                foreach (int kruiswisselAdres in kruiswisselAdressenVroeg)
                    _hardwareBeheerder.StuurLocSnelheidCommando(trein.Trein.DecoderAdres, trein.HuidigeSnelheidStap, vooruitVoorKruiswissel, kruiswisselAdres, trein.Trein.DecoderStappen);
                // GEBRUIKERSVERZOEK (rijstroom-/bloknummers expliciet in de log, ter
                // voorkoming van verwarring tussen een meldernummer en een rijstroom-adres
                // die toevallig hetzelfde cijfer hebben): dit pad stuurde stroom naar de
                // kruiswissel altijd stilzwijgend - nu staat er een regel voor.
                if (kruiswisselAdressenVroeg.Count > 0)
                    Log($"[Route '{trein.Route.Omschrijving}'] Rijstroom-adres {string.Join("/", kruiswisselAdressenVroeg)} (kruiswissel-sectie tussen blok {huidig.Nummer} en blok {blok.Nummer}) meegestuurd op de huidige snelheid.");
            }
            Log($"[Route '{trein.Route.Omschrijving}', automatisch] Blok {blok.Nummer} meldt zich al bezet vóórdat de software 'm als volgende stap had gekozen (de trein staat, door zijn lengte, al deels in dit blok terwijl {huidig.Nummer} nog bezet is) - neemt deze fysieke bevestiging direct over i.p.v. een spookmelding te tonen.");
            // GEVONDEN, KRITIEK GAT (gebruikerswaarneming: "blok7 blijft steeds bezet staan"
            // - de trein reed prima door, maar het blok dat hij net verliet bleef voor
            // altijd als bezet staan): dit "vroege aankomst"-pad ging altijd RECHTSTREEKS
            // naar VerwerkAutomatischeAankomst (aankomst verwerken), maar sloeg de stap
            // ERVOOR over die normaal bij een vertrek hoort - StartStaartFase voor huidig
            // (het blok dat we verlaten). Zonder die aanroep komen huidig se eigen
            // meldpunten NOOIT in de wachtlijst terecht, dus wordt BeeindigStaartFase er
            // ook nooit voor aangeroepen - huidig blijft daardoor voor altijd "bezet",
            // hoe vaak de trein er ook al voorbij is. Zelfde aanroep als VertrekVanBlok
            // Uitvoeren voor een normaal vertrek doet.
            StartStaartFase(huidig, StaartduurSeconden, BepaalVertrekmelder(huidig, trein.VorigBlok, trein.Trein?.Lengte ?? 0));
            VerwerkAutomatischeAankomst(trein, huidig, blok);
            return true;
        }
        return false;
    }

    /// <summary>Gebruikersverzoek ("kan je de vooruit/achteruit-detectie signaleren"): een
    /// loc die fysiek de VERKEERDE kant op rijdt (omgekeerde rijrichting op de decoder -
    /// CV29 -, of een bekabelingsfout) laat het blok dat hij net verlaten heeft
    /// (trein.VorigBlok) weer bezet melden, terwijl de software 'm VOORUIT naar een ANDER
    /// blok stuurt - een heel ander symptoom dan een gewone spookmelding (waar de software
    /// GEEN ENKEL blok verwachtte), en verdient dus een eigen, specifieke waarschuwing
    /// i.p.v. de generieke spookmelding-tekst.
    ///
    /// GEEN foutmelding als de trein daar WEL naartoe hoort te gaan (GereserveerdVolgendBlok
    /// == VorigBlok) - dat is namelijk precies wat een legitieme keerbeweging bij een
    /// kopspoor doet (zie VerwerkEventueelKeren): terugrijden naar het blok waar hij
    /// vandaan kwam, na het keren, is dan de BEDOELING.
    ///
    /// Alleen voor vrij automatisch rijden (trein.VorigBlok wordt bewust alleen daar
    /// bijgehouden, zie de bestaande code-commentaren elders in dit bestand) - bij een
    /// vaste route is dit signaal niet beschikbaar.</summary>
    public Trein? VindTreinDieMogelijkTerugrijdt(Blok blok) =>
        _actieveTreinen.FirstOrDefault(t => t.Route.Automatisch && t.VorigBlok == blok && t.GereserveerdVolgendBlok != blok)?.Trein;

    /// <summary>Zie _synthetischVrijgegevenBlokken hierboven - gebruikt door
    /// MainWindow.Bezetmelding_VanHardware om, naast de IsStaart-uitzondering, ook dit
    /// geval (blok zonder enig eigen bevestigd meldpunt bij vertrek) uit te sluiten van de
    /// terugrijdend-detectie.</summary>
    public bool IsSynthetischVrijgegeven(Blok blok) => _synthetischVrijgegevenBlokken.Contains(blok);

    /// <summary>Voor elke actieve rit: alle opeenvolgende blokparen (Van,Naar) in het pad
    /// waarbij Naar OP DIT MOMENT gereserveerd is - dit is precies het exacte traject dat
    /// de wissel/lijn-kleuring in het kijkscherm moet volgen (zie BaanontwerpWindow.
    /// VindCorridor). Voor automatisch rijden levert dit meestal 1 paar op (er wordt maar
    /// 1 blok tegelijk gereserveerd); voor een vaste route kunnen dit er meerdere zijn
    /// (het hele pad wordt in 1x gereserveerd bij de start).</summary>
    /// <summary>Voor elke actieve rit: het/de opeenvolgende blokpaar/paren (Van,Naar) die
    /// OP DIT MOMENT daadwerkelijk gereserveerd zijn - het exacte traject dat de
    /// wissel/lijn-reservering-kleuring in het kijkscherm moet volgen. AUTOMATISCH rijden
    /// en VASTE routes werken hier fundamenteel anders (zie GereserveerdVolgendBlok's
    /// documentatie voor waarom) en hebben dus allebei hun eigen aanpak nodig:
    /// - Automatisch: (HuidigBlok, GereserveerdVolgendBlok) - Pad bevat het gereserveerde
    ///   blok NOOIT gedurende de reservering zelf, dus daar kijken zou hier niets vinden.
    /// - Vast: elk (Pad[i],Pad[i+1])-paar waarbij Pad[i+1] gereserveerd is - bij een vaste
    ///   route wordt het HELE pad in 1x gereserveerd bij de start, dus Pad bevat wél al
    ///   alle relevante blokken.</summary>
    public IEnumerable<(Blok Van, Blok Naar)> ActieveGereserveerdeTrajecten()
    {
        foreach (var rit in _actieveTreinen)
        {
            if (rit.Route.Automatisch)
            {
                if (rit.HuidigBlok != null && rit.GereserveerdVolgendBlok != null)
                    yield return (rit.HuidigBlok, rit.GereserveerdVolgendBlok);
            }
            else
            {
                for (int i = 0; i + 1 < rit.Pad.Count; i++)
                    if (_blokBeheerder.IsGereserveerd(rit.Pad[i + 1]))
                        yield return (rit.Pad[i], rit.Pad[i + 1]);
            }
        }
    }

    /// <summary>Leesbare rijstatus van deze trein, puur voor overzichtsschermen
    /// (BlokOverzichtDialog/TreinActieDialog) - null als er helemaal geen actieve rit
    /// bekend is (staat stil, nooit gestart of de reservering is al opgeheven). In
    /// tegenstelling tot Pauzeer/Hervat/Stop hieronder heeft dit BEWUST geen enkel
    /// neveneffect - puur een opvraag.</summary>
    public string? TreinRijStatus(Trein trein)
    {
        var rit = _actieveTreinen.FirstOrDefault(t => t.Trein == trein);
        if (rit is null) return null;
        if (rit.Vastgelopen) return "Vastgelopen";
        if (rit.IsGepauzeerd) return "Gepauzeerd";
        if (rit.StopNaDezeStap) return "Rijdt automatisch (stopt na deze stap)";
        return "Rijdt automatisch";
    }

    /// <summary>Gebruikersverzoek ("actuele snelheid" ontbrak in het Overzicht
    /// locomotieven): de daadwerkelijk LEVENDE snelheid van deze trein in km/u, direct
    /// afgeleid van de decoderstap die OP DIT MOMENT echt naar de hardware verstuurd wordt
    /// (RijdendeTrein.HuidigeSnelheidStap, die de ramp-timer continu bijwerkt) - in
    /// tegenstelling tot TreinBeheerder.HuidigeSnelheid (dat de EIND-doelsnelheid van een
    /// rit onthoudt, al bij VERTREK in één keer gezet - dus tijdens het optrekken/afremmen
    /// zelf NIET de werkelijke, actuele snelheid laat zien, alleen waar de rit naartoe
    /// gaat). Null als er geen actieve rit voor deze trein bekend is; de aanroeper valt dan
    /// terug op TreinBeheerder.HuidigeSnelheid.</summary>
    public double? HuidigeSnelheidKmU(Trein trein)
    {
        var rit = _actieveTreinen.FirstOrDefault(t => t.Trein == trein);
        if (rit is null) return null;
        if (trein.GebruikGeijkteSnelheid && trein.Stappentabel.Count > 0)
            return trein.Stappentabel.OrderBy(s => Math.Abs(s.Stap - rit.HuidigeSnelheidStap)).FirstOrDefault()?.KmPerUur;
        double factor = Math.Clamp((double)rit.HuidigeSnelheidStap / Math.Max(1, trein.DecoderStappen), 0, 1);
        return factor * 100;
    }

    /// <summary>Gebruikersverzoek: "een knop om de loc te keren zodat als hij verkeerd om
    /// rijdt ik dat kan oplossen zonder de loc van de baan te hoeven halen." Stuurt DIRECT,
    /// zonder ramp of ceremonie, een omgekeerd rijrichting-commando op de HUIDIGE snelheid
    /// naar de hardware - dit is een acute correctie, geen nette snelheidsopbouw. Werkt
    /// zowel voor een actief rijdende trein (keert meteen om, verderop kan dit de
    /// automatische logica in de war brengen als de trein middenin een blok-overgang zit -
    /// bewust gebruikersinitiatief, niet iets wat de software zelf zou doen) als voor een
    /// stilstaande/gepauzeerde trein.</summary>
    public string KeerTreinIndienActief(Trein trein)
    {
        var rit = _actieveTreinen.FirstOrDefault(t => t.Trein == trein);
        if (rit is null) return $"'{trein.Omschrijving}' rijdt op dit moment geen route - er is niets te keren.";
        rit.RijdtVooruit = !rit.RijdtVooruit;

        // GEVONDEN, KRITIEK VEILIGHEIDSGAT (bytes-analyse: een kijkscherm-keercorrectie
        // vlak na een automatisch vertrek werd een fractie van een seconde later gewoon weer
        // overschreven - de trein versnelde daarna gewoon verder in de OUDE (verkeerde)
        // richting, tot een spookmelding op een heel ander blok): StuurSnelheidNaarHardware
        // start bij elk vertrek een eigen DispatcherTimer die de snelheid geleidelijk opbouwt
        // ("ramp") - die timer berekent "vooruit" ÉÉN keer bij de start en gebruikt die
        // vastgezette waarde vervolgens bij ELKE volgende tik, ook als rit.RijdtVooruit
        // ondertussen (hier, door deze keercorrectie) alweer is omgedraaid. Zolang die oude
        // ramp nog loopt, stuurt zijn eerstvolgende tik dus alsnog de OUDE richting - en
        // overschrijft daarmee stil de zojuist hieronder verstuurde, gecorrigeerde
        // richting/snelheid. Een actieve ramp moet daarom hier EERST gestopt worden, exact
        // zoals StuurSnelheidNaarHardware dat zelf ook doet bij zijn eigen start - de
        // hieronder verstuurde, acute correctie is daarmee de laatste stem, niet een tussenin
        // overschreven tussenstap.
        rit.ActieveSnelheidsRampTimer?.Stop();
        rit.ActieveSnelheidsRampTimer = null;

        // Zie Blok.GeleerdeStartrichtingVooruit: is dit een correctie bij een VERSE start
        // (nog geen vorig blok bekend - dus precies het moment waarop de software zelf geen
        // enkele manier had om de juiste richting te weten), dan wordt de zojuist door de
        // gebruiker bevestigde, NIEUWE richting vanaf nu onthouden voor dit startblok - een
        // feit, net bevestigd, niet een aanname.
        if (rit.VorigBlok is null && rit.HuidigBlok != null)
        {
            rit.HuidigBlok.GeleerdeStartrichtingVooruit = rit.RijdtVooruit;
            Log($"[Route '{rit.Route.Omschrijving}'] Startrichting voor blok {rit.HuidigBlok.Nummer} onthouden: voortaan {(rit.RijdtVooruit ? "vooruit" : "achteruit")} bij een verse start vanaf dit blok.");
        }

        if (trein.DecoderAdres > 0 && rit.HuidigBlok != null)
        {
            bool vooruitVoorHardware = trein.OmgekeerdeRijrichting ? !rit.RijdtVooruit : rit.RijdtVooruit;
            // GEVONDEN VEILIGHEIDSGAT (gebruikerswaarneming: keer-correctie leidde meteen
            // daarna tot een spookmelding op het VERTREKBLOK - de trein bleek fysiek gewoon
            // in de oude richting te zijn blijven rijden): dit stuurde de gecorrigeerde
            // richting voorheen ALLEEN naar rit.HuidigBlok - maar bij het voorbezetten (zie
            // AutomatischeStapProberen) heeft het VOLGENDE blok op dat moment AL hetzelfde
            // snelheidscommando (in de OUDE richting) gekregen. Was de trein op het moment
            // van de correctie al zo ver dat de decoder naar dat volgende bloadres luistert,
            // dan bereikte de correctie hem daar nooit - hij bleef gewoon de oude kant op
            // rijden. Zelfde oplossing als bij StopAlleBekendeGeplaatsteLocs en
            // MeldTreinVastgelopen: stuur de (gecorrigeerde) snelheid naar ELK blok in het
            // project, ongeacht waar de software denkt dat de trein staat.
            //
            // BUG #30 (gebruikerswaarneming: "ik drukte op rijrichting keren maar er
            // gebeurde niets, pas toen hij in blok 4 kwam keerde hij ineens"): deze
            // ALL-blocks-burst hieronder loste BUG #27's probleem (correctie bereikte het
            // verkeerde blok niet) al op, maar niet HOE SNEL hij aankomt - zie
            // DinamoHardware.VerwijderWachtendeSnelheidscommandosVoor voor de volledige
            // toelichting: zonder deze opruiming moest elke correctie-regel hieronder
            // alsnog, per blok, achter een stapel inmiddels achterhaalde, nog niet
            // verstuurde snelheidscommando's van de VORIGE (foutgerichte) ramp aansluiten -
            // bij meerdere snelle correcties kon dat meerdere seconden duren voordat de
            // loc de nieuwe richting daadwerkelijk kreeg. Nu wordt eerst alles wat voor
            // deze decoder nog klaarstaat weggegooid - het is sowieso net overruled door
            // dit bewuste besluit - zodat de burst hieronder voorin de rij komt.
            _hardwareBeheerder.VerwijderWachtendeSnelheidscommandosVoor(trein.DecoderAdres);
            foreach (var blok in _blokBeheerder.Blokken)
                _hardwareBeheerder.StuurLocSnelheidCommando(trein.DecoderAdres, rit.HuidigeSnelheidStap, vooruitVoorHardware, blok.Nummer, trein.DecoderStappen);
            foreach (int kruiswisselAdres in AlleKruiswisselRijAdressen())
                _hardwareBeheerder.StuurLocSnelheidCommando(trein.DecoderAdres, rit.HuidigeSnelheidStap, vooruitVoorHardware, kruiswisselAdres, trein.DecoderStappen);
            rit.LaatstGebruikteBlokVoorSnelheid = rit.HuidigBlok.Nummer; // zie RijdendeTrein.LaatstGebruikteBlokVoorSnelheid
        }

        // GEVONDEN GAT (gebruikerswaarneming: "weer zo'n irritante timer en vastgelopen" -
        // de trein reed 18 sec de VERKEERDE kant op vóór deze correctie, en liep vervolgens
        // vast omdat er nog maar 12 van de 30 seconden veiligheidsbudget over was): als de
        // software op dit moment op een ECHTE melding wacht (WachtOpMeldernNummer), dan telde
        // die veiligheids-timer gewoon door vanaf het oorspronkelijke - verkeerd gerichte -
        // vertrek, ONGEACHT deze handmatige correctie. Zinvolle voortgang begint feitelijk
        // pas NU, dus krijgt de wachttijd hier een volledig VERS budget - de oude, deels
        // verstreken timer wordt vervangen door een nieuwe met dezelfde, volledige duur.
        if (rit.WachtOpMeldernNummer is int wachtMelder && rit.HuidigeWachtIntervalSeconden is double interval)
        {
            rit.Timer?.Stop();
            rit.WachtBegonnenOp = DateTime.Now;
            var versTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(interval) };
            versTimer.Tick += (_, _) => MeldTreinVastgelopen(rit, wachtMelder, interval);
            rit.Timer = versTimer;
            versTimer.Start();
            Log($"[Route '{rit.Route.Omschrijving}'] Veiligheidswachttijd voor melder {wachtMelder} opnieuw gestart met een vers budget van {interval:0} sec, vanaf het moment van de handmatige richtingscorrectie.");
        }

        Log($"--- [Route '{rit.Route.Omschrijving}'] Rijrichting van trein '{trein.Omschrijving}' handmatig omgekeerd (kijkscherm) - rijdt nu {(rit.RijdtVooruit ? "vooruit" : "achteruit")}. ---");
        return $"'{trein.Omschrijving}' rijdt nu {(rit.RijdtVooruit ? "vooruit" : "achteruit")}.";
    }

    /// <summary>Zelfde interval als bij MeldTreinVastgelopen-achtige veiligheidswachttijden
    /// (voldoende om een fysiek trage, maar echte tweede gebeurtenis niet per ongeluk te
    /// negeren) - de tijd die een automatische richting-correctie (zie MainWindow.
    /// Bezetmelding_VanHardware) minstens moet krijgen voordat een VOLGENDE trigger voor
    /// dezelfde trein serieus wordt genomen.</summary>
    private const double RichtingCorrectieAfkoelingSeconden = 5;

    /// <summary>GEVONDEN GAT (bytes-analyse: een meldpunt vlak bij een korte, geïsoleerde
    /// sectie zoals een kruiswissel kan binnen een paar seconden meerdere keren flikkeren
    /// als een trein er, door zijn lengte, precies op de grens van balanceert - elke
    /// flikkering triggerde een NIEUWE automatische richting-correctie, die zichzelf zo kon
    /// tegenspreken in plaats van de trein er voorbij te helpen): wrapt
    /// KeerTreinIndienActief met een korte afkoelperiode PER TREIN - een tweede trigger
    /// vlak na de eerste wordt genegeerd (gelogd, niet stil), zodat de eerste correctie de
    /// kans krijgt om daadwerkelijk effect te hebben.</summary>
    public string? KeerTreinAutomatischMetAfkoeling(Trein trein)
    {
        var rit = _actieveTreinen.FirstOrDefault(t => t.Trein == trein);
        if (rit is null) return null;
        if (rit.LaatsteAutomatischeRichtingCorrectie is DateTime laatste && (DateTime.Now - laatste).TotalSeconds < RichtingCorrectieAfkoelingSeconden)
        {
            Log($"[Route '{rit.Route.Omschrijving}'] Automatische richting-correctie voor '{trein.Omschrijving}' genegeerd - {RichtingCorrectieAfkoelingSeconden:0} sec afkoelperiode nog niet verstreken sinds de vorige correctie (vermoedelijk een flikkerend meldpunt, geen nieuwe echte gebeurtenis).");
            return null;
        }
        rit.LaatsteAutomatischeRichtingCorrectie = DateTime.Now;
        return KeerTreinIndienActief(trein);
    }

    /// <summary>Pauzeert deze SPECIFIEKE trein (zonder de rit te annuleren) - het huidige
    /// blok blijft gewoon bezet en alle reserveringen verderop blijven intact, alleen de
    /// timer stopt. Kan later exact vanaf hetzelfde punt hervat worden. Andere treinen
    /// worden niet beïnvloed.</summary>
    public string PauzeerTreinIndienActief(Trein trein)
    {
        var rit = _actieveTreinen.FirstOrDefault(t => t.Trein == trein);
        if (rit is null) return $"'{trein.Omschrijving}' rijdt op dit moment geen route.";
        if (rit.Timer is null) return $"'{trein.Omschrijving}' staat al stil (geen lopende timer om te pauzeren).";
        rit.Timer.Stop();
        rit.Timer = null;
        rit.IsGepauzeerd = true;
        // Zie StopTrein voor de volledige toelichting bij dit gat: hier stond voorheen
        // ALLEEN _treinBeheerder.ZetSnelheid(trein, 0) - puur de weergavewaarde, GEEN echt
        // hardwarecommando. "Pauzeren" is geen noodstop, dus hier ook een echte,
        // geleidelijke afremming i.p.v. de loc gewoon te laten doorrijden.
        LogSnelheidsopbouw(rit, optrekken: false, rit.HuidigBlok);
        Log($"--- [Route '{rit.Route.Omschrijving}'] Trein '{trein.Omschrijving}' gepauzeerd (kijkscherm) - blok blijft bezet, reserveringen blijven staan. ---");
        return $"'{trein.Omschrijving}' gepauzeerd - blok blijft bezet, reservering blijft staan.";
    }

    /// <summary>Hervat een gepauzeerde rit - voor een VASTE route exact vanaf het
    /// eerstvolgende gebeurtenis met zijn oorspronkelijke wachttijd; voor AUTOMATISCH
    /// rijden via dezelfde dispatch als de Go-knop (AutomatischeStapProberen), aangezien
    /// die geen vaste Gebeurtenissen-lijst met wachttijd gebruikt maar elke seconde
    /// opnieuw een vervolgblok probeert te kiezen. EERDERE BUG: deze methode las voorheen
    /// altijd Gebeurtenissen[GebeurtenisIndex], wat bij een automatische rit een
    /// onafgevangen IndexOutOfRangeException gaf (en daarmee de hele applicatie liet
    /// crashen) omdat die lijst voor dat type rit niet op dezelfde manier gevuld is.</summary>
    public string HervatTreinIndienGepauzeerd(Trein trein)
    {
        var rit = _actieveTreinen.FirstOrDefault(t => t.Trein == trein);
        if (rit is null || !rit.IsGepauzeerd) return $"'{trein.Omschrijving}' staat niet gepauzeerd.";
        rit.IsGepauzeerd = false;
        if (rit.Route.Automatisch) AutomatischeStapProberen(rit);
        else PlanVolgendeGebeurtenis(rit);
        Log($"--- [Route '{rit.Route.Omschrijving}'] Trein '{trein.Omschrijving}' hervat (kijkscherm). ---");
        return $"'{trein.Omschrijving}' hervat.";
    }

    /// <summary>Heft de reservering(en) van deze trein op - het huidige blok blijft bezet
    /// (de loc blijft fysiek staan waar hij stond), maar alle NOG NIET bereikte,
    /// gereserveerde blokken verderop komen vrij en de geplande rit vervalt (kan niet meer
    /// hervat worden - wel opnieuw een nieuwe rit starten vanaf waar de loc nu staat).
    /// Werkt zowel op een gepauzeerde als op een nog rijdende trein.</summary>
    public string HefReserveringOpIndienActief(Trein trein)
    {
        var rit = _actieveTreinen.FirstOrDefault(t => t.Trein == trein);
        if (rit is null) return $"'{trein.Omschrijving}' rijdt op dit moment geen route.";
        rit.Timer?.Stop();
        foreach (var blok in rit.Pad)
        {
            if (_blokBeheerder.IsGereserveerd(blok)) _blokBeheerder.ZetGereserveerd(blok, false);
            if (_blokBeheerder.IsStaart(blok)) { _blokBeheerder.ZetStaart(blok, false); _blokBeheerder.ZetBezet(blok, false); }
        }
        _actieveTreinen.Remove(rit);
        Redraw();
        ProbeerWachtrij();
        string blokTekst = rit.HuidigBlok != null ? $"blok {rit.HuidigBlok.Nummer}" : "zijn huidige blok";
        Log($"--- [Route '{rit.Route.Omschrijving}'] Reservering van '{trein.Omschrijving}' opgeheven (kijkscherm) - loc blijft op {blokTekst} staan. ---");
        return $"Reservering van '{trein.Omschrijving}' opgeheven - loc blijft staan op {blokTekst}.";
    }

    /// <summary>Stopt de rit van deze SPECIFIEKE trein, indien hij op dit moment rijdt -
    /// voor het kijkscherm's "Loc verwijderen"-knop: een loc die stiekem nog "aan het
    /// rijden" is terwijl je 'm net van de baan hebt gehaald (bijv. omdat hij defect is)
    /// zou anders bij de eerstvolgende stap gewoon weer ergens op de baan verschijnen.</summary>
    public void StopTreinIndienActief(Trein trein)
    {
        var actieveRit = _actieveTreinen.FirstOrDefault(t => t.Trein == trein);
        if (actieveRit != null)
            StopTrein(actieveRit, $"Trein op route '{actieveRit.Route.Omschrijving}' gestopt (loc verwijderd vanuit kijkscherm).", magHerhalen: false);
    }

    /// <summary>Publieke bulk-variant van de "Go"-knop, voor het kijkscherm: laat ALLE
    /// rijdende treinen tegelijk hun wachttijd overslaan en naar het volgende blok gaan -
    /// "alle treinen laten rijden".
    ///
    /// GEVONDEN, ERNSTIGE BUG - zie GaGeselecteerdeTrein_Click voor de volledige
    /// toelichting: een trein die al op een ECHTE bezetmelding wacht (fysiek onderweg naar
    /// een AL GEKOZEN volgend blok) wordt hier nu bewust NIET meer herstart - dat zou de
    /// kandidaatkeuze/wisselstand midden in een fysieke overgang overnieuw kunnen doen,
    /// met een verkeerd gerouteerde loc tot gevolg.</summary>
    public int GaAlleTreinen()
    {
        var treinen = _actieveTreinen.Where(t => t.Timer != null && t.WachtOpMeldernNummer is null).ToList();
        foreach (var trein in treinen)
        {
            trein.Timer?.Stop();
            if (trein.Route.Automatisch) AutomatischeStapProberen(trein);
            else PlanVolgendeGebeurtenis(trein);
            Log($"[Route '{trein.Route.Omschrijving}'] Vroegtijdig vertrek (Go-knop, kijkscherm) - wachttijd overgeslagen.");
        }
        int wachtOpEchteMelding = _actieveTreinen.Count(t => t.Timer != null && t.WachtOpMeldernNummer != null);
        StatusTekst.Text = wachtOpEchteMelding > 0
            ? $"{treinen.Count} trein(en) laten vertrekken (Go, kijkscherm) - {wachtOpEchteMelding} trein(en) wachten al op een ECHTE bezetmelding en zijn met rust gelaten (niet veilig te versnellen)."
            : $"{treinen.Count} trein(en) laten vertrekken (Go, kijkscherm).";
        return treinen.Count;
    }

    /// <summary>Gebruikersverzoek (kijkscherm): "alle locs rijden naar het einde van hun
    /// gereserveerde rijweg en stoppen daar, dus geen noodstop maar gewoon een rustig
    /// stoppen." Zet StopNaDezeStap voor elke actieve trein - elke trein maakt zijn AL
    /// INGEZETTE, gereserveerde stap dus gewoon af (RijTimer_Tick/VerwerkAutomatischeAankomst
    /// controleren deze vlag PRECIES op het moment dat die stap ECHT, bevestigd voltooid is)
    /// en stopt dan pas. In tegenstelling tot een noodstop dus GEEN acute onderbreking - een
    /// trein die al onderweg is naar zijn eerstvolgende blok wordt daarin niet gestoord,
    /// alleen de stap DAARNA vervalt.</summary>
    public int AlleTreinenRustigStoppen()
    {
        var treinen = _actieveTreinen.Where(t => !t.StopNaDezeStap).ToList();
        foreach (var trein in treinen)
            trein.StopNaDezeStap = true;
        StatusTekst.Text = treinen.Count > 0
            ? $"{treinen.Count} trein(en) stoppen rustig zodra hun huidige, al ingezette stap voltooid is."
            : "Geen enkele trein rijdt op dit moment.";
        return treinen.Count;
    }

    /// <summary>Koploper's rijwindow heeft een popup-menu om "de laatste vijf blokken te
    /// tonen waar de trein is door gereden" - hier als eigen knop, want wij hebben geen
    /// rechtsklik-popup-menu op de loc-afbeelding zoals het echte Koploper.</summary>
    private void LaatsteBlokkenTonen_Click(object sender, RoutedEventArgs e)
    {
        if (ActieveTreinenLijst.SelectedItem is not RijdendeTrein trein)
        {
            StatusTekst.Text = "Selecteer eerst een rijdende trein in de lijst.";
            return;
        }
        var laatsteBlokken = trein.Pad.TakeLast(5).Select(b => b.Nummer.ToString());
        MessageBox.Show(this,
            $"Route '{trein.Route.Omschrijving}' - laatste {Math.Min(5, trein.Pad.Count)} blokken (oud -> nieuw):\n\n{string.Join(" -> ", laatsteBlokken)}",
            "Laatste blokken", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void TekenRelatieLijn(BlokRelatie relatie)
    {
        var van = new Point(relatie.Van.SchemaX + BlokGrootte / 2, relatie.Van.SchemaY + BlokGrootte / 2);
        var naar = new Point(relatie.Naar.SchemaX + BlokGrootte / 2, relatie.Naar.SchemaY + BlokGrootte / 2);
        var lijn = new Line { X1 = van.X, Y1 = van.Y, X2 = naar.X, Y2 = naar.Y, Stroke = Brushes.LightGray, StrokeThickness = 2, IsHitTestVisible = false };
        SchemaCanvas.Children.Add(lijn);
    }

    /// <summary>Kleuren volgens Koploper's eigen kleurenschema-categorieën (tabblad 'kleuren'
    /// in instellingen per database): vrije lijn = grijs, bezet = rood, gereserveerd = oud-goud,
    /// geselecteerd = blauw - geen kleur-per-trein-confetti, dat was geen Koploper-stijl.</summary>
    private void TekenBlok(Blok blok, bool inOpbouwPad, bool isKeuzeMogelijkheid, bool inWeergavePad)
    {
        bool bezet = _blokBeheerder.IsBezet(blok);
        bool handmatig = _blokBeheerder.IsHandmatigBezet(blok);
        bool staart = _blokBeheerder.IsStaart(blok);
        bool gereserveerd = _blokBeheerder.IsGereserveerd(blok);
        bool foutmelding = _blokBeheerder.IsFoutmelding(blok) && _blokBeheerder.FoutmeldingKnipperAan;

        var fill = foutmelding ? KoploperKleuren.Foutmelding
            : handmatig ? KoploperKleuren.HandmatigBezet
            : staart ? KoploperKleuren.Staartindicatie
            : bezet ? KoploperKleuren.Bezet
            : gereserveerd ? KoploperKleuren.Gereserveerd
            : isKeuzeMogelijkheid ? Brushes.Orange
            : inOpbouwPad ? KoploperKleuren.Geselecteerd
            : inWeergavePad ? Brushes.LightGray
            : KoploperKleuren.Vrij;
        bool geaccentueerd = bezet || gereserveerd || isKeuzeMogelijkheid || inOpbouwPad || inWeergavePad;

        var rechthoek = new Rectangle
        {
            Width = BlokGrootte,
            Height = BlokGrootte,
            Fill = fill,
            Stroke = geaccentueerd ? Brushes.DimGray : Brushes.Black,
            StrokeThickness = geaccentueerd ? 2 : 1
        };
        Canvas.SetLeft(rechthoek, blok.SchemaX);
        Canvas.SetTop(rechthoek, blok.SchemaY);
        SchemaCanvas.Children.Add(rechthoek);

        var tekst = new TextBlock
        {
            Text = blok.Nummer.ToString(),
            FontWeight = FontWeights.Bold,
            Foreground = bezet || gereserveerd ? Brushes.White : Brushes.Black,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(tekst, blok.SchemaX + BlokGrootte / 2 - 6);
        Canvas.SetTop(tekst, blok.SchemaY + BlokGrootte / 2 - 8);
        SchemaCanvas.Children.Add(tekst);
    }
}
