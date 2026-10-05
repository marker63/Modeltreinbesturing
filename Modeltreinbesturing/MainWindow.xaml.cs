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

public partial class MainWindow : Window
{
    private readonly BlokBeheerder _beheerder = new();
    private readonly BaanOntwerpBeheerder _baanBeheerder = new();
    private readonly WisselstraatBeheerder _wisselstraatBeheerder = new();
    private readonly RichtingsverbodBeheerder _richtingsverbodBeheerder = new();
    private readonly StopverbodBeheerder _stopverbodBeheerder = new();
    private readonly StopverbodStilstandBeheerder _stopverbodStilstandBeheerder = new();
    private readonly TreinrouteBeheerder _routeBeheerder;
    private readonly TreintypeBeheerder _treintypeBeheerder = new();
    private readonly TreinBeheerder _treinBeheerder = new();
    private readonly HardwareBeheerder _hardwareBeheerder = new();
    private readonly SnelheidsMetingBeheerder _snelheidsMetingBeheerder;

    private readonly BlokgroepBeheerder _blokgroepBeheerder = new();
    private readonly GebruikersBeheerder _gebruikersBeheerder = new();
    private readonly ActieBeheerder _actieBeheerder = new();
    private readonly OnderhoudsBeheerder _onderhoudsBeheerder = new();
    private readonly SnelleKlokBeheerder _snelleKlokBeheerder = new();
    private readonly DispatcherTimer _beursModusTimer = new() { Interval = TimeSpan.FromSeconds(15) };
    private Gebruiker? _ingelogdeGebruiker;
    private BaanontwerpWindow? _baanontwerpWindow;
    private BaanontwerpWindow? _baanoverzichtKijkWindow;
    private HelpWindow? _helpWindow;
    private HardwareLogDialog? _hardwareLogDialog;
    private TreinrouteWindow? _treinrouteWindow;

    private const double BlokGrootte = 50;

    // Sleep-status
    private Blok? _gesleeptBlok;
    private double _sleepStartX, _sleepStartY;
    private bool _isSlepen;

    private Blok? _geselecteerdBlok;
    private BlokRelatie? _geselecteerdRelatie;

    public MainWindow()
    {
        InitializeComponent();
        VensterInstellingen.Toepassen(this, "MainWindow");
        _snelheidsMetingBeheerder = new SnelheidsMetingBeheerder(_hardwareBeheerder);
        _beursModusTimer.Tick += (_, _) => { if (SimulatieInstellingen.BeursModusActief) StartNieuweRittenVoorStilstaandeLocs(); };
        _beursModusTimer.Start();
        // Zie _melderHerbevestigingsTimer hierboven: periodiek vangnet tegen een incidenteel
        // uitblijvend spontaan Switch-event van Dinamo zelf. Alleen zinvol (en alleen
        // verstuurd) als er daadwerkelijk met Dinamo verbonden is - VraagVolgendeMelderStatusOp
        // stuurt zelf al niets als dat niet zo is (zie HardwareBeheerder.VraagMelderStatusOp).
        _melderHerbevestigingsTimer.Tick += (_, _) => { if (_hardwareBeheerder.Huidige is DinamoHardware) VraagVolgendeMelderStatusOp(); };
        _melderHerbevestigingsTimer.Start();
        Closing += (_, _) => VensterInstellingen.Bewaren(this, "MainWindow");
        Closing += (_, _) => BackupBeheerder.MaakBackup(_beheerder, _baanBeheerder, _wisselstraatBeheerder, _routeBeheerder, _treintypeBeheerder, _richtingsverbodBeheerder, _stopverbodBeheerder, _stopverbodStilstandBeheerder, _treinBeheerder, _gebruikersBeheerder, _actieBeheerder, _snelheidsMetingBeheerder, _onderhoudsBeheerder, _snelleKlokBeheerder);
        _routeBeheerder = new TreinrouteBeheerder(_richtingsverbodBeheerder);
        _beheerder.BezettingGewijzigd += Redraw;
        // Blok-acties (zie ActieBeheerder/Model.BlokActie) - reageert op elke bezet-
        // statuswijziging, ongeacht welk venster/simulatiepad die veroorzaakte.
        _beheerder.BlokBezetVeranderd += _actieBeheerder.Verwerk;
        _beheerder.BlokGereserveerdVeranderd += _actieBeheerder.VerwerkReservering;
        KoploperKleuren.SchemaGewijzigd += Redraw;

        // Bezetmeldingen die van de aangesloten hardware binnenkomen doorzetten naar het
        // bijbehorende blok - zoek het blok met een Bezetmeldpunt met dit meldernummer.
        // Bij elke wisseling van hardware (zie HardwareDialog) opnieuw abonneren, want dat
        // is een nieuw IHardwareInterface-object met een eigen event.
        _hardwareBeheerder.HardwareGewijzigd += () => _hardwareBeheerder.Huidige.BezetmeldingGewijzigd += Bezetmelding_VanHardware;
        _hardwareBeheerder.Huidige.BezetmeldingGewijzigd += Bezetmelding_VanHardware;

        // BUG #28/#29: zodra Dinamo's kortsluiting/foutstatus weer opgeheven wordt, worden
        // alle wissels opnieuw hun gewenste stand gestuurd (zie HardwareBeheerder.
        // HerinitialiseerAlleWissels) - een puls die tijdens de fout onderbroken werd, kan
        // een wissel anders fysiek in de verkeerde stand achterlaten zonder dat de software
        // dat ooit kon weten. BUG #29: dit event komt van DinamoHardware.Poort_DataReceived,
        // dat op de achtergrondthread van SerialPort.DataReceived loopt, NIET op de UI-
        // thread - HerinitialiseerAlleWissels raakt wél UI-gebonden state aan (Symbolen,
        // Redraw via WisselStandGewijzigd). Daarom hier, net als Bezetmelding_VanHardware
        // hieronder, altijd via Dispatcher.Invoke - nooit rechtstreeks vanuit de
        // hardwarelaag zelf (die heeft ook geen Dispatcher).
        _hardwareBeheerder.KortsluitingStatusGewijzigd += actief =>
        {
            if (actief) return;
            Dispatcher.Invoke(() =>
            {
                int aantal = _hardwareBeheerder.HerinitialiseerAlleWissels(_baanBeheerder);
                if (aantal > 0)
                    HardwareCommunicatieLog.Log("Info", $"Kortsluiting/foutstatus opgeheven - voor de zekerheid {aantal} wissel(s)/driewegwissel(s)/kruiswissel(s) opnieuw hun stand gestuurd (zie BUG #28).");
            });
        };

        // Automatisch de meest recente backup laden bij het opstarten, zodat je nooit
        // zelf aan Opslaan hoeft te denken - puur een vangnet, dus als het laden om wat
        // voor reden dan ook mislukt (bijv. een beschadigd backup-bestand), gaat het
        // programma gewoon leeg verder in plaats van vast te lopen.
        var laatsteBackup = BackupBeheerder.LaatsteBackupPad();
        if (laatsteBackup != null)
        {
            try
            {
                // Dezelfde volledige laad-routine als bij handmatig laden - inclusief de
                // Redraw() aan het eind. Die ontbrak hier eerder: de data werd wel degelijk
                // geladen, maar het canvas werd nooit ververst om het ook te LATEN zien,
                // vandaar de blanco baan die leek alsof er niets geladen was.
                LaadBestandEnVerversAlles(laatsteBackup);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"De meest recente automatische backup kon niet geladen worden ({ex.Message}). Er is niets geladen - kies eventueel Bestand -> Oudere backup openen voor een vorige versie.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // Automatisch opnieuw verbinden met de laatst gebruikte hardware-interface (zie
        // HardwareInstellingen) - zonder dit moest je na elke herstart handmatig weer naar
        // bijv. Dinamo op COM10 terugschakelen, ook al had je dat de vorige keer al
        // ingesteld. Puur een vangnet net als de backup hierboven: als het niet lukt (kabel
        // er niet in, poort bezet, ...) blijft de applicatie gewoon in Simulatie staan i.p.v.
        // vast te lopen - de reden verschijnt in de Hardware-communicatielog.
        VerbindMetOpgeslagenHardwareIndienBeschikbaar();

        // Eén scherm voor de eindgebruiker: het kijkscherm gaat automatisch open en dit
        // (technische, data-houdende) hoofdvenster verdwijnt naar de achtergrond. De andere
        // "bouwschermen" (dit blokkenschema, het gewone baanontwerp, treinroutes) blijven
        // gewoon bestaan en bereikbaar - via het kijkscherm is er vooralsnog geen eigen
        // ingang naar dit venster, dus voorlopig moet je het via Taakbeheer/Alt-Tab weer
        // tevoorschijn halen als je als "beheerder" iets wilt aanpassen. Dat hoort bij een
        // latere stap (een echte rollenscheiding) - voor nu is dit puur "verstop het
        // technische venster, toon alleen het kijkscherm".
        Loaded += (_, _) =>
        {
            OpenOfActiveerKijkscherm();
            Hide();
        };
    }

    /// <summary>Spookmelding-detectie (echt Koploper-begrip): een bezetmelding van de
    /// hardware voor een blok waarvoor de SOFTWARE geen enkele reden heeft om bezetting te
    /// verwachten - niet al bezet, niet gereserveerd voor een actieve rit, niet handmatig
    /// bezet gezet. Dat wijst meestal op een hardware-glitch (slecht railcontact, een
    /// storende decoder) i.p.v. een echte trein. Hergebruikt bewust het bestaande
    /// Foutmelding-mechanisme (blinkend paars) i.p.v. een aparte status/kleur te verzinnen.</summary>
    /// <summary>Deze methode wordt aangeroepen vanuit HardwareBeheerder.BezetmeldingGewijzigd
    /// - bij een echte, seriële-poort-gekoppelde interface vuurt dat event op de
    /// ACHTERGRONDTHREAD van SerialPort.DataReceived (standaard .NET-gedrag), NIET de UI-
    /// thread. BUGFIX (gebruikersmelding: crash met "ItemsControl is inconsistent with its
    /// items source" tijdens het tekenen/koppelen van blokken): de blok-lookup gebeurde
    /// voorheen VÓÓR de Dispatcher.Invoke, dus op die achtergrondthread - een race
    /// condition met de UI-thread die op datzelfde moment de Blokken-lijst kan wijzigen
    /// (bijv. tijdens het tekenen). Nu gebeurt de HELE methode, inclusief de lookup, binnen
    /// Dispatcher.Invoke - dus altijd op de UI-thread, nooit gelijktijdig met een UI-
    /// wijziging aan dezelfde lijst.</summary>
    private void Bezetmelding_VanHardware(int meldernummer, bool bezet)
    {
        Dispatcher.Invoke(() =>
        {
            var blok = _beheerder.Blokken.FirstOrDefault(b => b.Bezetmeldpunten.Any(m => m.MeldernNummer == meldernummer));
            _beheerder.MeldRuweMelderStatus(meldernummer, bezet, blok); // ALTIJD, ook zonder blok - zie Meldpuntenverkenner
            if (blok is null) return;
            _beheerder.RegistreerMelderStatus(meldernummer, bezet);

            // Zie TreinrouteWindow.ProbeerVroegeAankomstBevestiging: een trein met een
            // fysieke lengte kan, tijdens de overgang naar het volgende blok, dat blok al
            // fysiek bereiken vóórdat de software het als kandidaat gekozen/gereserveerd
            // had - dat is dan GEEN spookmelding maar een vroege, geldige bevestiging.
            // Wordt hier als eerste geprobeerd, vóór de spookmelding-check hieronder.
            if (bezet && _treinrouteWindow != null && _treinrouteWindow.ProbeerVroegeAankomstBevestiging(blok, bezet))
            {
                _beheerder.ZetBezet(blok, bezet);
                return;
            }

            // GEVONDEN, EIGEN SIGNAAL (gebruikersverzoek: "kan je de vooruit/achteruit-
            // detectie signaleren" - een loc die soms fysiek de verkeerde kant op rijdt,
            // waardoor het systeem in de war raakt): dit is een heel ANDER symptoom dan een
            // gewone spookmelding (waar de software HELEMAAL NIETS verwachtte) - hier
            // verwacht de software wél iets, namelijk vooruitgang naar een ANDER blok, en
            // meldt in plaats daarvan het blok dat net verlaten is zich weer bezet. Zie
            // TreinrouteWindow.VindTreinDieMogelijkTerugrijdt voor de volledige toelichting
            // (incl. waarom een legitieme keerbeweging hier bewust NIET in meegaat).
            //
            // GEVONDEN, DEFINITIEVE OORZAAK ("treinen rijden de verkeerde kant op" - bij
            // live-testen 3x herhaald binnen 30 sec, telkens een ANDER meldpunt van
            // hetzelfde blok (blok 5, melders 26, 25, 17)): deze check ontbrak de IsStaart-
            // uitzondering die de spookmelding-check hieronder WEL al had. Een blok met
            // meerdere meldpunten (zie StartStaartFase/multi-sensor-blok in
            // TreinrouteWindow) blijft na vertrek nog een tijdje legitiem "bezet" op zijn
            // EIGEN staart-fase: de trein passeert zijn resterende meldpunten gewoon nog
            // fysiek, één voor één, terwijl de kop al naar het volgende blok onderweg is.
            // Dat is GEEN terugrijdende trein - het is precies hetzelfde, al lang bekende
            // "staart-fase"-verschijnsel. Zonder deze uitzondering zag elke volgende
            // staart-melder van hetzelfde blok eruit als een NIEUWE terugrijd-gebeurtenis,
            // en werd de trein (en op de koop toe: de gebruiker, via de kijkscherm-knop)
            // een paar keer per minuut onnodig gekeerd. Nu wordt een bezetmelding van een
            // blok dat zelf nog in zijn eigen staart-fase zit, nooit meer als "terugrijdend"
            // aangemerkt - precies zoals het al nooit als spookmelding werd aangemerkt.
            //
            // TWEEDE, AANVULLENDE UITZONDERING (zelfde live-test, hetzelfde blok 5):
            // bovenstaande IsStaart-uitzondering alleen bleek NIET genoeg voor een blok dat
            // via de kijkscherm-Go-knop vers (zonder enig bevestigd eigen meldpunt) op de
            // baan gezet was - zie TreinrouteWindow._synthetischVrijgegevenBlokken voor de
            // volledige toelichting. Zo'n blok wordt namelijk NIET via een echte
            // vrijmelding vrijgegeven (dus IsStaart staat dan allang weer op false tegen de
            // tijd dat de trein z'n fysieke lengte nog over het blok uitrolt), maar via
            // indirect bewijs (aankomst bij het volgende blok) - precies het scenario
            // waarin melders 26, 25 en 17 van blok 5 elk apart als "terugrijdend" werden
            // aangezien terwijl de trein gewoon, met zijn volle lengte, nog over zijn eigen
            // meldpunten aan het wegrijden was.
            bool synthetischVrijgegeven = _treinrouteWindow?.IsSynthetischVrijgegeven(blok) ?? false;
            var terugrijdendeTrein = (bezet && !_beheerder.IsStaart(blok) && !synthetischVrijgegeven) ? _treinrouteWindow?.VindTreinDieMogelijkTerugrijdt(blok) : null;
            if (terugrijdendeTrein != null)
            {
                // GEBRUIKERSVERZOEK ("kan je dat herkennen en de trein dan DIRECT keren,
                // zodat ik dit niet steeds handmatig hoef te herstellen"): voorheen stopte
                // dit ALTIJD de hele sessie (NOODSTOP) en toonde een blokkerend venster dat
                // pas verder liet rijden nadat de gebruiker het zelf had opgeheven - precies
                // wat de gebruiker nu automatisch wil laten afhandelen. Hergebruikt bewust
                // dezelfde, al zorgvuldig afgebakende detectie hierboven (die een legitieme
                // keerbeweging uitsluit) - alleen de AFHANDELING verandert: in plaats van
                // stoppen+wachten op de gebruiker, wordt de rijrichting nu direct
                // omgekeerd (KeerTreinIndienActief, inclusief de eerder gevonden broadcast-
                // fix naar alle blokken) en gaat de rit gewoon door. Geen noodstop, geen
                // blokkerend venster meer - wel een duidelijke, niet-blokkerende logregel en
                // geluid, want dit wijst meestal op een omgekeerde rijrichting op de decoder
                // (CV29) of een bekabelingsfout die de moeite waard blijft om na te kijken,
                // ook al herstelt de software zichzelf.
                if (SimulatieInstellingen.GeluidenIngeschakeld)
                {
                    try { System.Media.SystemSounds.Exclamation.Play(); } catch { /* geluid is bijzaak */ }
                }
                ZorgVoorOpenTreinrouteWindow();
                // GEVONDEN GAT (bytes-analyse: hetzelfde meldpunt kan binnen 1-2 sec meerdere
                // keren flikkeren als een trein, door zijn lengte, precies op de grens van
                // een korte, geïsoleerde sectie balanceert - elke flikkering triggerde hier
                // eerder een NIEUWE correctie, die zichzelf zo kon tegenspreken): gebruikt nu
                // de afkoelperiode-wrapper - een resultaat van null betekent "genegeerd,
                // afkoelperiode nog actief", niet "mislukt".
                string? resultaat = _treinrouteWindow!.KeerTreinAutomatischMetAfkoeling(terugrijdendeTrein);
                if (resultaat != null)
                    _treinrouteWindow.Log($"Rijrichting-correctie: blok {blok.Nummer} (melder {meldernummer}) - het blok dat '{terugrijdendeTrein.Omschrijving}' net verlaten heeft, meldde zich weer bezet terwijl de software VOORUIT naar een ander blok stuurde. Automatisch gekeerd: {resultaat} Dit wijst meestal op een omgekeerde rijrichting op de decoder (CV29) of een bekabelingsfout - de moeite waard om te controleren, ook al is er nu automatisch gecorrigeerd.");
                _beheerder.ZetBezet(blok, bezet);
                return;
            }

            // GEVONDEN GAT (gebruikerswaarneming: spookmelding op een blok dat op dat moment
            // gewoon in zijn EIGEN staart-fase zat - de trein was bezig te vertrekken, en een
            // van zijn resterende meldpunten meldde zich, geheel volgens verwachting, alsnog
            // bezet): deze check kende alleen IsBezet/IsGereserveerd/IsHandmatigBezet, maar
            // IsStaart helemaal niet - terwijl een blok in staart-fase per definitie nog
            // "actief betrokken" is (de trein passeert het nog fysiek), ongeacht of het
            // daarnaast ook nog als IsBezet staat. Een blok in staart-fase hoort hier nooit
            // als onverwacht/spookmelding gezien te worden.
            bool onverwacht = bezet && !_beheerder.IsBezet(blok) && !_beheerder.IsGereserveerd(blok) && !_beheerder.IsHandmatigBezet(blok) && !_beheerder.IsStaart(blok);
            // Alleen bij de EERSTE keer dat dit specifieke blok als spookmelding opvalt een
            // pop-up tonen - een storende sensor kan anders continu meldingen blijven geven,
            // en dan zou een venster-per-melding het scherm blokkeren. Het blinkende blok
            // (Foutmelding) blijft daarna gewoon zichtbaar totdat je 'm zelf opheft.
            if (onverwacht && !_beheerder.IsFoutmelding(blok))
            {
                _beheerder.ZetFoutmelding(blok, true);

                // Zoals het echte Koploper: "laat het programma een waarschuwend geluid
                // horen en activeert een noodstop. Alle treinen komen dan acuut tot
                // stilstand." - dit is geen puur informatieve melding maar een
                // veiligheidsmaatregel (een spookmelding kan een botsing/ontsporing
                // betekenen die al aan de gang is), dus expliciet ALLE rijdende treinen
                // stoppen, niet alleen het betrokken blok markeren.
                if (SimulatieInstellingen.GeluidenIngeschakeld)
                {
                    try { System.Media.SystemSounds.Exclamation.Play(); } catch { /* geluid is bijzaak, nooit de noodstop hierdoor laten mislukken */ }
                }
                ZorgVoorOpenTreinrouteWindow();
                _treinrouteWindow!.VoerNoodstopUit($"spookmelding blok {blok.Nummer}");

                MessageBox.Show(this,
                    $"Spookmelding: blok {blok.Nummer} (melder {meldernummer}) meldt zich bezet, terwijl er geen rit of handmatige bezetting op dit blok bekend is.\n\nAlle rijdende treinen zijn direct gestopt (NOODSTOP), net als het echte Koploper bij een spookmelding doet. Meestal een hardware-glitch (railcontact/decoder) - controleer de baan. Het blok knippert nu totdat je de foutmelding zelf opheft.",
                    "Modeltreinbesturing - spookmelding", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            // GEVONDEN, STRUCTUREEL GAT (bytes-analyse: een blok met MEERDERE meldpunten -
            // bijv. een Voorblok- én een Stopsectie-melder - werd VRIJ verklaard zodra
            // ÉÉN van die meldpunten vrij meldde, ook als een ANDER meldpunt van datzelfde
            // blok op dat moment nog gewoon bezet stond. Zo kon een late, echte
            // bezetmelding van de "resterende" sensor daarna als spookmelding gezien
            // worden - de software had het blok immers al (te vroeg) vrij verklaard op
            // basis van een ANDERE sensor. Een blok mag pas ECHT vrij als GEEN van zijn
            // meldpunten meer bezet meldt.
            //
            // "Onbekend" telt hier BEWUST ook als "nog bezet" (dus GEEN vrijgave): een net
            // handmatig geplaatste loc claimt een blok zonder dat er ooit een echte melding
            // voor GEWEEST is - zo'n meldpunt heeft dan simpelweg nog geen enkele status in
            // _beheerder.MelderIsBezet staan. Zonder deze regel zou het EERSTE meldpunt
            // van een blok dat toevallig vrij meldt het blok alsnog voortijdig vrijgeven,
            // terwijl een ANDER meldpunt van hetzelfde blok (nog nooit gehoord, dus
            // "onbekend") in werkelijkheid nog gewoon op de loc staat.
            bool anderMeldpuntNogBezet = !bezet && blok.Bezetmeldpunten.Any(m =>
                m.MeldernNummer != meldernummer && (_beheerder.MelderIsBezet(m.MeldernNummer) ?? true));
            if (!anderMeldpuntNogBezet)
                _beheerder.ZetBezet(blok, bezet);
        });
    }

    // --- Muis-interactie -------------------------------------------------

    private void SchemaCanvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var positie = e.GetPosition(SchemaCanvas);

        // Rechtermuisknop op een blok doet voorlopig niets extra's (laag 2/3 komt later);
        // rechtermuisknop op lege plek = nieuw blok, zoals in de handleiding.
        if (BlokOpPositie(positie) is null)
        {
            var blok = _beheerder.NieuwBlok(positie.X - BlokGrootte / 2, positie.Y - BlokGrootte / 2);
            _geselecteerdBlok = blok;
            Redraw();
        }
    }

    private void SchemaCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var positie = e.GetPosition(SchemaCanvas);
        var blok = BlokOpPositie(positie);

        if (blok != null && e.ClickCount == 2)
        {
            // Alleen automatisch een stootblok toevoegen als het blok NU pas Kopspoor
            // wordt - niet bij elke opslag van een blok dat al langer Kopspoor is, anders
            // komt een bewust verwijderd stootblok telkens weer terug bij de volgende keer
            // dat je iets anders aan hetzelfde blok bewerkt.
            var vorigTypeVoorDialoog = blok.Type;
            var dialoog = new BlokEigenschappenDialog(blok, _beheerder) { Owner = this };
            if (dialoog.ShowDialog() == true)
            {
                if (vorigTypeVoorDialoog != BlokType.Kopspoor && blok.Type == BlokType.Kopspoor) ZorgVoorStootblok(blok);
                Redraw();
            }
            return;
        }

        _geselecteerdBlok = blok;
        _geselecteerdRelatie = null; // klik op leeg/blok deselecteert een eventueel geselecteerde relatie

        if (blok != null)
        {
            _gesleeptBlok = blok;
            _sleepStartX = blok.SchemaX;
            _sleepStartY = blok.SchemaY;
            _isSlepen = true;
            SchemaCanvas.CaptureMouse();
        }

        Redraw();
    }

    private void SchemaCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isSlepen || _gesleeptBlok is null) return;

        // Bij Ctrl+slepen (loc verplaatsen/laten rijden, zie MouseLeftButtonUp) blijft het
        // BLOK zelf gewoon op zijn plek staan - alleen de muisaanwijzer beweegt, er is geen
        // reden om het blok tijdens het slepen zelf ook heen en weer te laten schuiven. Dat
        // schuiven was hinderlijk en had niets te maken met het daadwerkelijk verplaatsen
        // van het blok.
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;

        var positie = e.GetPosition(SchemaCanvas);
        _gesleeptBlok.SchemaX = Math.Max(0, positie.X - BlokGrootte / 2);
        _gesleeptBlok.SchemaY = Math.Max(0, positie.Y - BlokGrootte / 2);
        Redraw();
    }

    private void SchemaCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isSlepen || _gesleeptBlok is null) return;

        var positie = e.GetPosition(SchemaCanvas);
        var doelBlok = BlokOpPositie(positie, uitzonderen: _gesleeptBlok);

        if (doelBlok != null)
        {
            // Losgelaten bovenop een ander blok: dit betekent "leg een relatie vast",
            // niet verplaatsen - dus positie terugzetten naar waar het slepen begon.
            _gesleeptBlok.SchemaX = _sleepStartX;
            _gesleeptBlok.SchemaY = _sleepStartY;

            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                // Ctrl+sleep = "verplaats de loc" (zoals in Koploper: een loc naar een ander
                // blok slepen om 'm daar automatisch te laten rijden). Gebruikt nu dezelfde
                // pathfinding als het kijkscherm (VindPadNaarBlok) i.p.v. alleen een DIRECT
                // verbonden blok toe te staan - en geeft ALTIJD een duidelijke melding,
                // zowel bij succes als wanneer er geen toegestaan pad bestaat, i.p.v. in dat
                // laatste geval stilletjes niets te doen.
                var trein = _beheerder.LocOpBlok(_gesleeptBlok);
                if (trein is null)
                {
                    MessageBox.Show(this, "Dit blok heeft geen geplaatste loc om te verslepen.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else if (_gesleeptBlok == doelBlok)
                {
                    // Losgelaten op hetzelfde blok: niets te doen.
                }
                else
                {
                    LocNaarAnderBlokGesleept_Verwerken(trein, doelBlok);
                }
            }
            else
            {
                _beheerder.LegRelatieVast(_gesleeptBlok, doelBlok);
                ZorgVoorUitgaandSein(_gesleeptBlok);
            }
        }
        // anders: losgelaten op lege plek = gewoon verplaatst, positie staat al goed.

        _isSlepen = false;
        _gesleeptBlok = null;
        SchemaCanvas.ReleaseMouseCapture();
        Redraw();
    }

    /// <summary>
    /// Zorgt dat een blok met minstens 1 uitgaande relatie een sein heeft aan zijn
    /// uitgang, gekoppeld aan dat blok zelf - net als bij echte blokbeveiliging staat
    /// op de grens van 2 blokken een sein dat de toegang tot het volgende blok bewaakt
    /// (zie SeinLogica). Doet niets als het blok al zo'n sein heeft, of nog geen
    /// enkele uitgaande relatie. De positie wordt geschat op basis van de richting
    /// naar het (eerste bekende) vervolgblok, aan de rechterkant van die rijrichting -
    /// een benadering, want de exacte spoorgeometrie kent het systeem niet.
    /// </summary>
    private void ZorgVoorUitgaandSein(Blok van)
    {
        bool heeftAlSein = _baanBeheerder.Symbolen.OfType<Sein>().Any(s => s.GekoppeldBlok == van);
        if (heeftAlSein) return;

        var naar = _beheerder.VolgendeBlokken(van).FirstOrDefault();
        if (naar is null) return;

        var vanPos = _baanBeheerder.PositieVanBlok(van);
        var naarPos = _baanBeheerder.PositieVanBlok(naar);
        var vanCentrum = new Point(vanPos.X + BlokGrootte / 2, vanPos.Y + BlokGrootte / 2);
        var naarCentrum = new Point(naarPos.X + BlokGrootte / 2, naarPos.Y + BlokGrootte / 2);

        var richting = naarCentrum - vanCentrum;
        if (richting.Length < 0.01) richting = new Vector(1, 0);
        richting.Normalize();
        var rechtsVanRijrichting = new Vector(-richting.Y, richting.X); // rechterkant, in schermcoördinaten (Y omlaag)

        const double afstandUitBlok = 15;   // net voorbij de rand van het blok, aan de uitgang
        const double zijOffset = 16;        // een stukje naar rechts van de rijrichting

        var seinPositie = vanCentrum + richting * (BlokGrootte / 2 + afstandUitBlok) + rechtsVanRijrichting * zijOffset;

        int volgendSeinAdres = _baanBeheerder.Symbolen.OfType<Sein>().Select(s => s.Adres).DefaultIfEmpty(-1).Max() + 1;
        var sein = new Sein
        {
            X = seinPositie.X,
            Y = seinPositie.Y,
            Tabblad = _baanBeheerder.TabbladVanBlok(van),
            GekoppeldBlok = van,
            Adres = volgendSeinAdres
        };
        _baanBeheerder.VoegSymboolToe(sein);
    }

    /// <summary>
    /// Zorgt dat een Kopspoor-blok een stootblok heeft aan het einde van het spoor - het
    /// echte, fysieke einde dat een kopspoor onderscheidt van een gewoon doorgaand blok.
    /// Positie: als er een blok is waarvandaan je dit blok binnenrijdt, staat het stootblok
    /// verder in diezelfde richting (voorbij het blok, weg van waar de trein vandaan komt).
    /// Zonder bekende inkomende relatie een simpele standaardrichting.
    /// </summary>
    private void ZorgVoorStootblok(Blok blok)
    {
        bool heeftAl = _baanBeheerder.Symbolen.OfType<Stootblok>().Any(s => s.GekoppeldBlok == blok);
        if (heeftAl) return;

        // Richting bepalen op basis van de ECHTE spoor-geometrie (de lijn die aan dit blok
        // verankerd is), niet op basis van een rechte lijn tussen twee blok-middelpunten -
        // dat laatste hoeft totaal niet overeen te komen met hoe het spoor er in het
        // baanontwerp daadwerkelijk ligt (bochten, wissels ertussen, ...) en gaf daardoor
        // regelmatig een stootblok op een onlogische plek/richting.
        var (stootblokPositie, richting) = BepaalStootblokPlaatsing(blok);

        _baanBeheerder.VoegSymboolToe(new Stootblok
        {
            X = stootblokPositie.X,
            Y = stootblokPositie.Y,
            Tabblad = _baanBeheerder.TabbladVanBlok(blok),
            GekoppeldBlok = blok,
            Richting = richting
        });
    }

    /// <summary>Zie de gelijknamige methode in BaanontwerpWindow voor de volledige uitleg -
    /// gebruikt de richting van de daadwerkelijk aan dit blok verankerde lijn, valt terug op
    /// de oude blokmiddelpunt-aanpak als er nog geen lijn getekend is.</summary>
    private (Point Positie, Vector Richting) BepaalStootblokPlaatsing(Blok blok)
    {
        var lijnBijBlok = _baanBeheerder.Symbolen.OfType<Lijn>().FirstOrDefault(l => l.PuntBlokAnkers.Contains(blok));
        if (lijnBijBlok != null)
        {
            int index = lijnBijBlok.PuntBlokAnkers.IndexOf(blok);
            var ankerPunt = lijnBijBlok.Punten[index];
            int buurIndex = index == 0 ? Math.Min(1, lijnBijBlok.Punten.Count - 1) : index - 1;
            var buurPunt = lijnBijBlok.Punten[buurIndex];
            var lijnRichting = ankerPunt - buurPunt;
            if (lijnRichting.Length >= 0.01)
            {
                lijnRichting.Normalize();
                return (ankerPunt + lijnRichting * 15, lijnRichting);
            }
        }

        var blokPos = _baanBeheerder.PositieVanBlok(blok);
        var blokCentrum = new Point(blokPos.X + BlokGrootte / 2, blokPos.Y + BlokGrootte / 2);
        var inkomendeRelatie = _beheerder.Relaties.FirstOrDefault(r => r.Naar == blok);
        Vector richting;
        if (inkomendeRelatie != null)
        {
            var vanPos = _baanBeheerder.PositieVanBlok(inkomendeRelatie.Van);
            var vanCentrum = new Point(vanPos.X + BlokGrootte / 2, vanPos.Y + BlokGrootte / 2);
            richting = blokCentrum - vanCentrum;
            if (richting.Length < 0.01) richting = new Vector(1, 0);
            richting.Normalize();
        }
        else
        {
            richting = new Vector(1, 0);
        }
        return (blokCentrum + richting * (BlokGrootte / 2 + 15), richting);
    }

    /// <summary>
    /// Repareert dubbele/onbekende adressen (meestal Adres=0 bij iedereen, van vóór het
    /// adres-veld bestond in oudere baanbestanden) door elke tweede-en-verdere symbool met
    /// hetzelfde adres een nieuw, uniek adres te geven. Laat unieke adressen met rust.
    /// </summary>
    private void HerstelDubbeleAdressen()
    {
        var seinen = _baanBeheerder.Symbolen.OfType<Sein>().ToList();
        if (seinen.Count > 0)
        {
            var gezien = new HashSet<int>();
            int volgend = seinen.Max(s => s.Adres) + 1;
            foreach (var sein in seinen)
                if (!gezien.Add(sein.Adres)) sein.Adres = volgend++;
        }

        var wissels = _baanBeheerder.Symbolen.OfType<Wissel>().ToList();
        if (wissels.Count > 0)
        {
            var gezien = new HashSet<int>();
            int volgend = wissels.Max(w => w.Adres) + 1;
            foreach (var wissel in wissels)
                if (!gezien.Add(wissel.Adres)) wissel.Adres = volgend++;
        }
    }

    /// <summary>Schuift alle blokken (in het blokkenschema) zo op dat de meest linkse/bovenste
    /// een kleine positieve marge heeft - nodig sinds SchemaCanvas een vast canvas met
    /// origine (0,0) in een ScrollViewer is: een blok met een negatieve positie ligt dan
    /// buiten het scrolbare gebied en is nooit meer te bereiken.</summary>
    private void NormaliseerBlokPosities()
    {
        if (_beheerder.Blokken.Count == 0) return;

        const double marge = 20;
        double minX = _beheerder.Blokken.Min(b => b.SchemaX);
        double minY = _beheerder.Blokken.Min(b => b.SchemaY);
        double verschuivingX = minX < marge ? marge - minX : 0;
        double verschuivingY = minY < marge ? marge - minY : 0;
        if (verschuivingX == 0 && verschuivingY == 0) return;

        foreach (var blok in _beheerder.Blokken)
        {
            blok.SchemaX += verschuivingX;
            blok.SchemaY += verschuivingY;
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F1)
        {
            Gebruiksaanwijzing_Click(sender, e);
            return;
        }
        if (e.Key == Key.Delete && _geselecteerdBlok != null)
        {
            int aantalRelaties = _beheerder.Relaties.Count(r => r.Van == _geselecteerdBlok || r.Naar == _geselecteerdBlok);
            int aantalRoutes = _routeBeheerder.Treinroutes.Count(r =>
                r.Startblok == _geselecteerdBlok || r.Keuzeblokken.Contains(_geselecteerdBlok));
            int aantalWisselstraten = _wisselstraatBeheerder.Wisselstraten.Count(w =>
                w.Van == _geselecteerdBlok || w.Naar == _geselecteerdBlok);

            var vraag = MessageBox.Show(this,
                $"Blok {_geselecteerdBlok.Nummer} verwijderen? Dit ruimt ook op: {aantalRelaties} relatie(s), " +
                $"{aantalWisselstraten} wisselstraat(straten), {aantalRoutes} route(s) die dit blok gebruiken, " +
                "plus het gekoppelde sein en eventuele stootblok in het baanontwerp. Dit kan niet ongedaan gemaakt worden.",
                "Blok verwijderen", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (vraag != MessageBoxResult.Yes) return;

            _baanBeheerder.VergeetBlok(_geselecteerdBlok); // ook laag-2 positie/koppelingen opruimen
            _wisselstraatBeheerder.VergeetBlok(_geselecteerdBlok); // en laag-3 wisselstraten die dit blok gebruiken
            _routeBeheerder.VergeetBlok(_geselecteerdBlok); // en laag-4 routes die dit blok gebruiken
            _richtingsverbodBeheerder.VergeetBlok(_geselecteerdBlok);
            _stopverbodBeheerder.VergeetBlok(_geselecteerdBlok);
            _stopverbodStilstandBeheerder.VergeetBlok(_geselecteerdBlok);
            _actieBeheerder.VergeetBlok(_geselecteerdBlok);
            _beheerder.VerwijderBlok(_geselecteerdBlok);
            _geselecteerdBlok = null;
            Redraw();
        }
        else if (e.Key == Key.Delete && _geselecteerdRelatie != null)
        {
            var relatie = _geselecteerdRelatie;
            int aantalWisselstraten = _wisselstraatBeheerder.Wisselstraten.Count(w => w.Van == relatie.Van && w.Naar == relatie.Naar);

            var vraag = MessageBox.Show(this,
                $"Relatie blok {relatie.Van.Nummer} -> blok {relatie.Naar.Nummer} verwijderen?" +
                (aantalWisselstraten > 0 ? $" Let op: {aantalWisselstraten} wisselstraat(straten) gebruiken dit blokpaar en werken hierna niet meer." : "") +
                " Dit kan niet ongedaan gemaakt worden.",
                "Relatie verwijderen", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (vraag != MessageBoxResult.Yes) return;

            _beheerder.Relaties.Remove(relatie);
            _geselecteerdRelatie = null;
            Redraw();
        }
        else if (e.Key == Key.K && _geselecteerdRelatie != null)
        {
            // Snelle sneltoets voor Koploper's "uit-via-naar"-keer-vinkje (zie ook
            // Beheren -> Relaties beheren voor een overzicht van alle relaties tegelijk).
            _geselecteerdRelatie.Keer = !_geselecteerdRelatie.Keer;
            Redraw();
        }
    }

    private void OpenBaanontwerp_Click(object sender, RoutedEventArgs e)
    {
        if (_baanontwerpWindow is null || !_baanontwerpWindow.IsLoaded)
        {
            _baanontwerpWindow = new BaanontwerpWindow(_beheerder, _baanBeheerder, _wisselstraatBeheerder, _treinBeheerder, _actieBeheerder, _hardwareBeheerder, _onderhoudsBeheerder, _routeBeheerder, treinrouteWindowOpvragen: () => _treinrouteWindow)
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterScreen // kan getriggerd worden terwijl MainWindow verborgen is
            };
            // Gebruikersverzoek: "in alle schermen een programma afsluiten in de menubalk" -
            // dit bouwscherm had voorheen HELEMAAL geen menubalk (puur een werkbalk), dus
            // deze afsluit-actie kon nergens naartoe gestuurd worden. Zelfde afhandeling als
            // de al bestaande kijkscherm-variant hieronder.
            _baanontwerpWindow.ProgrammaAfsluitenGevraagd += () => Close();
            _baanontwerpWindow.Show();
        }
        else
        {
            _baanontwerpWindow.Activate();
        }
    }

    /// <summary>Een tweede, schoon baanoverzicht-venster puur om te bekijken tijdens het
    /// rijden - net als het echte Koploper waar het baanoverzicht ook als kijkscherm dient.
    /// Los van _baanontwerpWindow, dus je kunt allebei tegelijk open hebben (bijv. editeren
    /// op het ene scherm, meekijken op het andere). Bevat sinds kort ook het "Overzicht
    /// locomotieven" als vast paneel (niet meer een los venster) - zo heb je als
    /// eindgebruiker aan dit ene scherm genoeg.</summary>
    private void OpenBaanoverzichtKijkModus_Click(object sender, RoutedEventArgs e) => OpenOfActiveerKijkscherm();

    /// <summary>Vereist een geslaagde login (eenmalig per sessie) voordat de meegegeven
    /// actie mag draaien - poort naar de bouwschermen. Het kijkscherm zelf gebruikt dit
    /// NERGENS, dat blijft bewust vrij toegankelijk. Als er nog helemaal geen beheerder
    /// bestaat (zou normaal niet moeten kunnen, want Nieuw_Click dwingt er één af) wordt
    /// die eerst alsnog aangemaakt.</summary>
    private void MetLoginToegang(Action actie)
    {
        if (_ingelogdeGebruiker != null)
        {
            actie();
            return;
        }

        if (!_gebruikersBeheerder.HeeftAlEenBeheerder)
        {
            var eersteBeheerderDialoog = new NieuwAccountDialog(_gebruikersBeheerder, verplichtBeheerder: true) { Owner = _baanoverzichtKijkWindow ?? (Window)this };
            if (eersteBeheerderDialoog.ShowDialog() != true) return; // geannuleerd: geen toegang
        }

        var loginDialoog = new LoginDialog(_gebruikersBeheerder) { Owner = _baanoverzichtKijkWindow ?? (Window)this };
        if (loginDialoog.ShowDialog() != true) return; // geannuleerd of mislukt: geen toegang

        _ingelogdeGebruiker = loginDialoog.IngelogdeGebruiker;
        actie();
    }

    private void OpenOfActiveerKijkscherm()
    {
        if (_baanoverzichtKijkWindow is null || !_baanoverzichtKijkWindow.IsLoaded)
        {
            _baanoverzichtKijkWindow = new BaanontwerpWindow(_beheerder, _baanBeheerder, _wisselstraatBeheerder, _treinBeheerder, _actieBeheerder, _hardwareBeheerder, _onderhoudsBeheerder, _routeBeheerder, kijkModus: true, treinrouteWindowOpvragen: () => _treinrouteWindow)
            {
                Owner = this,
                // CenterScreen i.p.v. CenterOwner: dit venster kan ook geopend/geactiveerd
                // worden nadat MainWindow zichzelf al verborgen heeft (Hide() in de
                // constructor) - centreren op een verborgen owner kan verrassend uitpakken.
                WindowStartupLocation = WindowStartupLocation.CenterScreen
            };
            _baanoverzichtKijkWindow.LocNaarAnderBlokGesleept += LocNaarAnderBlokGesleept_Verwerken;
            // Vanuit kijk-modus toegang tot de bouwschermen: Blokkenschema = dit venster
            // (MainWindow) weer tevoorschijn halen, de andere twee gewoon via de bestaande
            // open-methodes. Elk van de drie vereist eerst een geslaagde login (zie
            // MetLoginToegang) - het kijkscherm zelf blijft bewust vrij van elke beveiliging.
            _baanoverzichtKijkWindow.BlokkenschemaGevraagd += () => MetLoginToegang(() => { Show(); Activate(); });
            _baanoverzichtKijkWindow.BaanontwerpBewerkenGevraagd += () => MetLoginToegang(() => OpenBaanontwerp_Click(this, new RoutedEventArgs()));
            _baanoverzichtKijkWindow.TreinroutesGevraagd += () => MetLoginToegang(() => OpenTreinroutes_Click(this, new RoutedEventArgs()));
            // Bewust GEEN MetLoginToegang hier - programma afsluiten (en dus een backup
            // laten maken) is geen bouwscherm-actie, dat moet iedereen kunnen doen.
            _baanoverzichtKijkWindow.ProgrammaAfsluitenGevraagd += () => Close();
            _baanoverzichtKijkWindow.AlleTreinenGaanRijdenGevraagd += AlleTreinenGaanRijden;
            _baanoverzichtKijkWindow.AlleTreinenOpstellenGevraagd += AlleTreinenOpstellen;
            _baanoverzichtKijkWindow.AlleTreinenStoppenGevraagd += AlleTreinenStoppenOpBestemming;
            _baanoverzichtKijkWindow.AlleTreinenRustigStoppenGevraagd += () => { _treinrouteWindow?.AlleTreinenRustigStoppen(); };
            _baanoverzichtKijkWindow.LocVerwijderdGevraagd += LocVerwijderdGevraagd_Verwerken;
            // Deze drie werken rechtstreeks op TreinrouteWindow (indien het al bestaat -
            // een trein kan alleen "actief" zijn als er al ooit een rit gestart is, dus
            // ZorgVoorOpenTreinrouteWindow is hier niet nodig, net als bij LocVerwijderdGevraagd).
            _baanoverzichtKijkWindow.LocPauzerenGevraagd += trein => _treinrouteWindow?.PauzeerTreinIndienActief(trein) ?? $"'{trein.Omschrijving}' rijdt op dit moment geen route.";
            _baanoverzichtKijkWindow.LocHervattenGevraagd += trein => _treinrouteWindow?.HervatTreinIndienGepauzeerd(trein) ?? $"'{trein.Omschrijving}' staat niet gepauzeerd.";
            _baanoverzichtKijkWindow.LocKerenGevraagd += trein => _treinrouteWindow?.KeerTreinIndienActief(trein) ?? $"'{trein.Omschrijving}' rijdt op dit moment geen route.";
            _baanoverzichtKijkWindow.LocReserveringOpheffenGevraagd += trein => _treinrouteWindow?.HefReserveringOpIndienActief(trein) ?? $"'{trein.Omschrijving}' rijdt op dit moment geen route.";
            _baanoverzichtKijkWindow.Show();
        }
        else
        {
            _baanoverzichtKijkWindow.Activate();
        }
    }

    /// <summary>Een loc is in het kijkscherm naar een ANDER blok gesleept dan waar hij al
    /// stond - zoek zelf een pad daarnaartoe (rekening houdend met richtingsverboden en
    /// andere beperkingen, zie TreinrouteBeheerder.VindPadNaarBlok) en start daar
    /// automatisch een rit voor, net zoals de bestaande Ctrl+sleep-functie in het
    /// blokkenschema dat al deed voor een DIRECT verbonden blok - nu voor een willekeurig
    /// blok ergens verderop in de baan.</summary>
    /// <summary>Kijkscherm-knop "Opstellen" (parkeerbord): stuurt ELKE geplaatste loc naar
    /// het dichtstbijzijnde vrije Opstelspoor-blok (zoekt zelf het pad, net als bij
    /// slepen). Een loc die al ergens op een Opstelspoor-blok staat wordt overgeslagen.</summary>
    public void AlleTreinenOpstellen()
    {
        var opstelsporen = _beheerder.Blokken.Where(b => b.Type == BlokType.Opstelspoor).ToList();
        if (opstelsporen.Count == 0)
        {
            MessageBox.Show(this, "Er is geen enkel blok van het type 'Opstelspoor' op deze baan.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        int aantalGestart = 0;
        foreach (var trein in _treinBeheerder.Treinen)
        {
            var huidigBlok = _beheerder.BlokVanLoc(trein);
            if (huidigBlok is null || huidigBlok.Type == BlokType.Opstelspoor) continue;

            // Kortste pad naar elk opstelspoor-blok berekenen en het dichtstbijzijnde
            // (in aantal blokken) toegestane pad kiezen.
            Treinroute? kortsteRoute = null;
            int kortstePadLengte = int.MaxValue;
            foreach (var opstelspoor in opstelsporen)
            {
                var kandidaatRoute = _routeBeheerder.VindPadNaarBlok(huidigBlok, opstelspoor, _beheerder, trein.Treintype);
                if (kandidaatRoute is null) continue;
                int padLengte = _routeBeheerder.BerekenVolledigPad(kandidaatRoute, _beheerder).Count;
                if (padLengte < kortstePadLengte)
                {
                    kortstePadLengte = padLengte;
                    kortsteRoute = kandidaatRoute;
                }
            }
            if (kortsteRoute is null) continue; // geen enkel opstelspoor bereikbaar voor deze loc

            kortsteRoute.Omschrijving = $"Opstellen: {trein.Omschrijving} -> blok {kortsteRoute.Bestemmingsblok?.Nummer}";
            kortsteRoute.Treintype = trein.Treintype;
            _routeBeheerder.Treinroutes.Add(kortsteRoute);
            ZorgVoorOpenTreinrouteWindow();
            _treinrouteWindow!.StartAutomatischeRoute(kortsteRoute, trein);
            aantalGestart++;
        }
        MessageBox.Show(this, $"{aantalGestart} loc(s) gestuurd naar een opstelspoor.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>Kijkscherm-knop "Go" (groen): laat alle rijdende treinen tegelijk vertrekken
    /// (wachttijd overslaan). Kijkscherm-knop "Stop" (stopbord): stopt alle treinen die al
    /// op hun bestemming zijn aangekomen (stilstaan). Beide doen niets als er nog geen
    /// enkele trein rijdt (TreinrouteWindow nog niet open).</summary>
    /// <summary>Kijkscherm-knop "Go" (groen): "alle treinen laten rijden en dan continu
    /// blijven rijden, afhankelijk van wachttijden en restricties" - dus TWEE dingen:
    /// (1) elke geplaatste loc die nog NIET rijdt krijgt een nieuwe, doelloze automatische
    /// rit (Automatisch=true, geen Bestemmingsblok - blijft daardoor voor altijd zelf een
    /// vervolgblok kiezen, precies zoals "automatisch rijden" dat al doet, tot een
    /// noodstop/handmatige stop), en (2) elke trein die al reed maar op dit moment wacht
    /// (bijv. bij een station) mag zijn wachttijd overslaan.</summary>
    public void AlleTreinenGaanRijden()
    {
        int nieuweRitten = StartNieuweRittenVoorStilstaandeLocs();
        int hervat = _treinrouteWindow != null ? _treinrouteWindow.GaAlleTreinen() : 0;

        if (nieuweRitten == 0 && hervat == 0)
            MessageBox.Show(this, "Er zijn geen geplaatste locs om te laten rijden.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Information);
        else
            MessageBox.Show(this, $"{nieuweRitten} nieuwe rit(ten) gestart, {hervat} trein(en) lieten hun wachttijd overslaan.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>De kern van "Go", zonder popup - elke geplaatste loc die nog niet rijdt
    /// krijgt een nieuwe, doelloze automatische rit. Herbruikt door zowel de interactieve
    /// Go-knop hierboven als de stille, periodieke Beursmodus hieronder (die anders om de
    /// paar seconden ongevraagd een popup zou tonen).</summary>
    /// <summary>Start automatisch rijden voor precies één, al geplaatste (stilstaande) loc -
    /// gebruikt door zowel StartNieuweRittenVoorStilstaandeLocs (alle locs tegelijk) als
    /// TreinActieVerwerken (kijkscherm: één specifieke trein "hervatten").</summary>
    private bool HervatEenTrein(Trein trein)
    {
        var huidigBlok = _beheerder.BlokVanLoc(trein);
        if (huidigBlok is null) return false; // niet geplaatst, niets te doen
        if (_treinrouteWindow != null && _treinrouteWindow.IsTreinActief(trein)) return false; // rijdt al

        var route = new Treinroute
        {
            Omschrijving = $"Automatisch rijden: {trein.Omschrijving} vanaf blok {huidigBlok.Nummer}",
            Startblok = huidigBlok,
            Automatisch = true,
            Treintype = trein.Treintype
        };
        _routeBeheerder.Treinroutes.Add(route);
        ZorgVoorOpenTreinrouteWindow();
        return _treinrouteWindow!.StartAutomatischeRoute(route, trein);
    }

    private int StartNieuweRittenVoorStilstaandeLocs()
    {
        int nieuweRitten = 0;
        foreach (var trein in _treinBeheerder.Treinen)
            if (HervatEenTrein(trein)) nieuweRitten++;
        return nieuweRitten;
    }

    /// <summary>GEVONDEN GAT (gebruikersmelding: "de stop knop werkt niet, hij laat de
    /// trein gewoon doorrijden"): deze knop deed voorheen ALLEEN StopAlleTreinenOpBestemming
    /// (dat FILTERT op t.Timer==null - dus juist NIET de treinen die actief onderweg zijn
    /// of op een melding wachten, wat nu net de enige treinen zijn die je met een
    /// "Stop"-knop wilt kunnen afbreken). De naam "OpBestemming" is intern gebleven om niet
    /// overal de event-bedrading te hoeven aanpassen, maar het GEDRAG is nu een echte,
    /// volledige stop: VoerNoodstopUit (stopt alle actieve ritten DIRECT, plus een
    /// expliciet snelheid=0-commando naar elke bekende, geplaatste loc - ook een die
    /// buiten de software om nog doorreed op een resterend commando).</summary>
    public void AlleTreinenStoppenOpBestemming()
    {
        if (_treinrouteWindow is null)
        {
            StopAlleBekendeGeplaatsteLocs("bij de Stop-knop (nog geen enkele rit gestart deze sessie)");
            MessageBox.Show(this, "Er is deze sessie nog geen enkele automatische rit gestart - voor de zekerheid is er wel een expliciet stopcommando naar elke bekende, geplaatste loc gestuurd.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _treinrouteWindow.VoerNoodstopUit("gebruiker: Stop-knop (kijkscherm)");
        MessageBox.Show(this, "Alle rijdende treinen zijn direct gestopt (inclusief een expliciet stopcommando naar elke bekende, geplaatste loc, ook als die buiten de software om nog doorreed).", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>GEVONDEN, FUNDAMENTELER GAT (bytes-analyse: een hele testsessie zonder
    /// enig verstuurd snelheidscommando, en toch bleef een loc kilometers doorrijden en
    /// spookmelding na spookmelding veroorzaken): Dinamo blijft het LAATST gegeven
    /// snelheidscommando per blok/decoder voor ALTIJD herhalen naar de rails, ongeacht of
    /// dat commando uit deze sessie komt of uit een sessie geleden (bijv. omdat de
    /// software toen afsloot zonder ooit expliciet snelheid=0 te sturen). Daarom hier, bij
    /// elke (opnieuw) verbinding met echte hardware, een expliciet snelheid=0-commando naar
    /// ELKE bekende, GEPLAATSTE loc - zodat een "vergeten" commando van een vorige sessie
    /// nooit zomaar kan blijven doorlopen. Zie ook TreinrouteWindow.VoerNoodstopUit, die
    /// exact dezelfde aanpak gebruikt (hier bewust GEEN kopie via TreinrouteWindow, want die
    /// hoeft nog niet eens te bestaan/open te zijn op het moment dat er verbonden wordt).</summary>
    /// <summary>GEVONDEN VEILIGHEIDSGAT (gebruikerswaarneming: trein bleef fysiek pendelen
    /// tussen blok3/4 terwijl de software dacht dat hij in blok5 stond, en het "stop
    /// bekende geplaatste locs"-commando dus he verkeerde bloadres (5) kreeg - een oud
    /// commando van vóór deze sessie bleef op blok3/4 se adres gewoon actief): dit stuurde
    /// tot nu toe het stopcommando ALLEEN naar het blok waar de software DENKT dat de loc
    /// staat (BlokVanLoc) - staat die onthouden positie om wat voor reden dan ook niet meer
    /// (goed), dan bereikt het stopcommando de loc domweg niet, en blijft hij ongezien
    /// doorrijden. Nu wordt het stopcommando voor élke bekende loc naar ÉLK blok in het
    /// hele project gestuurd - ongeacht waar de software denkt dat de loc staat. Dat is
    /// onschadelijk voor blokken waar de loc niet is (de Dinamo-decoder luistert toch
    /// alleen naar het bloadres waar hij fysiek op staat), maar garandeert dat de loc
    /// hoe dan ook een stopcommando krijgt op het bloadres waar hij WEL op reageert.</summary>
    private void StopAlleBekendeGeplaatsteLocs(string reden)
    {
        int aantal = 0;
        var alleBlokken = _beheerder.Blokken.ToList();
        // Zie TreinrouteWindow.AlleKruiswisselRijAdressen voor de volledige toelichting
        // (Model.Kruiswissel.RijAdres) - een kruiswissel-sectie die elektrisch los van alle
        // aangrenzende blokken staat, heeft dit stopcommando ook nodig.
        var kruiswisselAdressen = _baanBeheerder.Symbolen.OfType<Kruiswissel>().Select(k => k.RijAdres).Where(a => a > 0).Distinct().ToList();
        foreach (var trein in _treinBeheerder.Treinen)
        {
            if (trein.DecoderAdres <= 0) continue;
            // BUGFIX: zie HardwareBeheerder.StuurLocSnelheidCommandoGepauzeerd - deze
            // eenmalige, niet-urgente veiligheidsbroadcast (10-30+ commando's ineens) ging
            // voorheen ongepauzeerd de deur uit en verdrong daarmee de wissel-initialisatie
            // die hier vlak voor plaatsvindt.
            foreach (var blok in alleBlokken)
                _hardwareBeheerder.StuurLocSnelheidCommandoGepauzeerd(trein.DecoderAdres, 0, true, blok.Nummer, trein.DecoderStappen);
            foreach (int kruiswisselAdres in kruiswisselAdressen)
                _hardwareBeheerder.StuurLocSnelheidCommandoGepauzeerd(trein.DecoderAdres, 0, true, kruiswisselAdres, trein.DecoderStappen);
            aantal++;
        }
        if (aantal > 0)
            HardwareCommunicatieLog.Log("Info", $"Expliciet snelheid=0 gestuurd naar {aantal} bekende loc(s), naar ALLE {alleBlokken.Count} blokken in het project (niet alleen de onthouden positie) {reden} - voorkomt dat een 'vergeten' commando van een vorige sessie blijft doorlopen, ook als de onthouden positie niet meer klopt.");
    }

    /// <summary>Zie de aanroep in de constructor hierboven - probeert eenmalig, bij het
    /// opstarten, opnieuw te verbinden met de laatst BEWAARDE hardware-keuze
    /// (HardwareInstellingen). "async void" is hier bewust (net als HardwareDialog.
    /// Verbinden_Click): dit wordt vanuit de constructor aangeroepen, dus er is geen Task
    /// om op te wachten - fouten worden zelf afgevangen en gelogd i.p.v. de constructor te
    /// laten crashen.</summary>
    private async void VerbindMetOpgeslagenHardwareIndienBeschikbaar()
    {
        var keuze = HardwareInstellingen.Laad();
        if (keuze is null || keuze.InterfaceType == "Simulatie" || string.IsNullOrEmpty(keuze.ComPoort)) return;
        var nieuw = HardwareInstellingen.MaakInterface(keuze.InterfaceType);
        if (nieuw is null) return; // onbekend/verouderd type-label, gewoon in Simulatie blijven

        nieuw.StatusBericht += bericht => Dispatcher.Invoke(() => HardwareCommunicatieLog.Log("Info", bericht));
        try
        {
            await nieuw.VerbindenAsync(keuze.ComPoort);
            _hardwareBeheerder.WisselHardware(nieuw);
            HardwareCommunicatieLog.Log("Info", $"Automatisch opnieuw verbonden met de laatst gebruikte hardware: {nieuw.Naam} op {keuze.ComPoort}.");
            // GEBRUIKERSVERZOEK ("er blijken bij het opstarten wissels in de verkeerde
            // stand te staan, kan je bij het opstarten alle wissels even initialiseren?"):
            // voorheen alleen de wissels met AltijdInitialiseren aangevinkt (een bewuste
            // per-wissel keuze voor iets anders - zie Model.Wissel.AltijdInitialiseren).
            // Nu ALTIJD alle wissels, driewegwissels én (Engelse) kruiswissels, ongeacht die
            // vlag - de software se eigen Stand-waarde is de enige bron van waarheid die ze
            // heeft, dus opnieuw versturen bij elke (her)verbinding kan nooit kwaad en
            // herstelt precies dit soort afwijkingen (bijv. een wissel die tussen sessies
            // door handmatig is versteld).
            // GEBRUIKERSVERZOEK ("ik hoorde bij het starten geen enkele wissel omgaan,
            // misschien handig om ze bij het opstarten 2x te schakelen zodat ze hoorbaar
            // om zijn gegaan - dat is nu tijdens het testen wel handig"): stuur eerst
            // bewust de TEGENOVERGESTELDE stand, en pas daarna de echte gewenste stand -
            // dat garandeert ALTIJD hoorbare beweging tijdens het testen, ongeacht of de
            // wissel toevallig al in de juiste stand stond (wat bij "gewoon opnieuw dezelfde
            // stand sturen" natuurlijk geen enkele beweging geeft, en dus niets bewijst).
            // BUG #28: deze drie foreach-lussen stonden hier EN (apart onderhouden, dus
            // vatbaar voor uit-elkaar-lopen) in HardwareDialog.InitialiseerWisselsMetVlag -
            // nu één gedeelde implementatie, zie HardwareBeheerder.HerinitialiseerAlleWissels.
            int totaalGeinitialiseerd = _hardwareBeheerder.HerinitialiseerAlleWissels(_baanBeheerder);
            if (totaalGeinitialiseerd > 0)
                HardwareCommunicatieLog.Log("Info", $"{totaalGeinitialiseerd} wissel(s)/driewegwissel(s)/kruiswissel(s) geïnitialiseerd (opnieuw hun stand verstuurd) bij het verbinden.");

            // GEBRUIKERSVRAAG ("lees je bij opstarten alle bezetmelders uit?") - TERECHT
            // PUNT, tot nu toe NIET: de software vroeg de status van een melder alleen op
            // het moment dat ze er specifiek op ging wachten (VraagMelderStatusOp, zie
            // TreinrouteWindow) - de WARE status van elke andere sensor bleef tot dan toe
            // volledig onbekend. Dat is precies waarom een vers geplaatste loc op een
            // sensor kon staan zonder dat de software dat wist (zie StartStaartFase: "geen
            // enkel meldpunt bevestigd bezet" - de oorzaak van het permanent-bezet-blok-
            // gat van hiervoor). Nu wordt bij elke (opnieuw) verbinding de ACTUELE status
            // van ELK bekend meldpunt in het hele project meteen opgevraagd - feiten
            // verzamelen zodra dat kan, in plaats van pas als er toevallig naar gevraagd
            // wordt.
            if (_hardwareBeheerder.Huidige.KanMelderStatusOpvragen)
                VraagAlleMelderStatusOp("bij het verbinden");

            StopAlleBekendeGeplaatsteLocs("bij het (opnieuw) verbinden");
        }
        catch (Exception ex)
        {
            // BEWUST de opgeslagen keuze niet overschrijven/wissen bij een mislukte poging -
            // de kabel zit er nu misschien even niet in, maar morgen weer wel. Gewoon in
            // Simulatie blijven en de reden loggen i.p.v. een blokkerend dialoogvenster tonen
            // (dit gebeurt immers ongevraagd bij het opstarten).
            HardwareCommunicatieLog.Log("Info", $"Automatisch verbinden met de laatst gebruikte hardware ({keuze.InterfaceType} op {keuze.ComPoort}) is mislukt: {ex.Message}. Blijft in simulatiemodus - kies handmatig een andere poort via Beheren -> Hardware-interface als dat nodig is.");
        }
    }

    /// <summary>Vraagt de ACTUELE status van elk bekend meldpunt in het hele project op bij
    /// Dinamo (zie HardwareBeheerder.VraagMelderStatusOp). Voorheen alleen aangeroepen
    /// vanuit VerbindMetOpgeslagenHardwareIndienBeschikbaar/HardwareDialog, één keer per
    /// (opnieuw) verbinden - zie _melderHerbevestigingsTimer hieronder voor waarom dit nu
    /// ook PERIODIEK gebeurt.</summary>
    private void VraagAlleMelderStatusOp(string reden)
    {
        var alleMelders = _beheerder.Blokken.SelectMany(b => b.Bezetmeldpunten).Select(m => m.MeldernNummer).Where(m => m > 0).Distinct().ToList();
        foreach (var meldernummer in alleMelders)
            _hardwareBeheerder.VraagMelderStatusOp(meldernummer);
        if (alleMelders.Count > 0)
            HardwareCommunicatieLog.Log("Info", $"Status van alle {alleMelders.Count} bekende bezetmelder(s) opgevraagd {reden}.");
    }

    /// <summary>GEBRUIKERSCORRECTIE ("deze baan rijdt al jaren perfect met Koploper, dus de
    /// bekabeling/sensoren zijn niet het probleem"): terecht - en dat betekent dat een
    /// incidenteel UITBLIJVEND spontaan Switch-event (bijv. melder 15, eerder ook melder 8)
    /// niet aan onze kant hoeft te liggen om toch nooit bij onze software aan te komen. Een
    /// recente fix (zie DinamoHardware.Poort_DataReceived) loste al een ECHTE bug op waarbij
    /// WIJ zelf af en toe een binnengekomen event verloren door onvolledige berichten niet
    /// over meerdere .Read()-aanroepen heen te bewaren - maar dat verklaart niet elk geval:
    /// als Dinamo zelf (in zijn eigen firmware, door ruis op de bus, of simpelweg omdat het
    /// toevallige moment van wisselen van bezet-status niet overeenkomt met een stabiele
    /// meetwaarde) een spontane melding een enkele keer NIET verstuurt, dan was er nooit
    /// iets om te missen - geen hoeveelheid eigen bugfixes kan dat herstellen. Koploper
    /// overleeft dit soort incidentele gaten vermoedelijk (mede) doordat het periodiek,
    /// actief de status van elk bekend meldpunt blijft navragen in plaats van louter te
    /// wachten op spontane meldingen - zo'n gemiste melding wordt dan vanzelf binnen enkele
    /// seconden alsnog rechtgezet via het eerstvolgende antwoord op zo'n navraag.
    ///
    /// GEVONDEN, STRUCTUREEL GAT IN DEZE HELE AANPAK (gebruikersverzoek: "controleer alle
    /// wachtrijen heel goed en simuleer het zelf" - naar aanleiding van "trein valt stil"/
    /// "staat stil, melder niet ontvangen binnen 30 sec" die bleven terugkomen ondanks de
    /// eerdere wachtrij-fixes): een simulatie van de volledige keten (HardwareBeheerder se
    /// twee wachtrijen + DinamoHardware se eigen _teVersturen/_teVersturenPrioriteit +
    /// _stuurTimer) legt een FUNDAMENTELER capaciteitsprobleem bloot, los van welke
    /// wachtrij-volgorde er ook gekozen wordt: Dinamo se eigen seriële zendcyclus verwerkt
    /// hooguit 1 datagram per 200ms (5/sec, zie DinamoHardware._stuurTimer) - dat is de
    /// HARDE bovengrens voor ALLES samen (melderstatus, snelheid, wissels). Deze timer
    /// vroeg tot nu toe ALLE (vaak 30) bekende meldpunten in ÉÉN keer op, elke 4 seconden -
    /// dat is op zichzelf al 30/4=7,5 aanvragen/sec, DUS MEER dan die 5/sec-bovengrens, HEEL
    /// ANDER VERKEER AL BUITEN BESCHOUWING GELATEN. Met een simulatie bevestigd: zelfs met
    /// een rijdende trein en wisselverkeer volledig weggedacht, loopt de wachtrij bij déze
    /// herhaalfrequentie en dit aantal meldpunten ALTIJD, onvermijdelijk, onbeperkt op -
    /// een rustpauze/prioriteitswissel verplaatst dat probleem dan ook alleen maar, lost het
    /// nooit op. Fix: in plaats van alle meldpunten in één klap, nu één meldpunt per tik,
    /// ROUND-ROBIN door de hele lijst. EERSTE POGING (300ms/melder) bleek bij simulatie
    /// (zie scratchpad queue_sim2.py, tot 3600 sec gesimuleerd met een volgehouden,
    /// worst-case snelheidsramp + deze melderstatus-navraag samen) ZELF ALWEER boven de
    /// 5/sec-bovengrens uit te komen zodra er ook een snelheidsramp liep (3,33/sec melder +
    /// 3,5/sec ramp-budget die toen bij StuurSnelheidNaarHardware hoorde = 6,83/sec, dus
    /// structureel te veel) - de simulatie liet de wachtrij dan alsnog onbeperkt oplopen.
    /// Deze 600ms/melder nu WEL, samen met het 2,2/sec-rambudget hieronder (zie
    /// StuurSnelheidNaarHardware), getest tot en met 1 uur volgehouden worst-case belasting
    /// (continu een kort blok met veel stappen nemen, geen enkele pauze) zonder dat de
    /// wachtrij ooit meer dan een paar items lang wordt: 600ms komt overeen met zo'n 1,67
    /// aanvragen/sec, en een volledige cyclus van 30 meldpunten duurt zo'n 18 seconden -
    /// trager dan de oorspronkelijke "binnen enkele seconden"-doelstelling hierboven, maar
    /// dat is de bewust aanvaarde prijs voor een harde garantie dat deze navraag nooit meer,
    /// in combinatie met rijdend verkeer, een onbeperkt groeiende achterstand veroorzaakt.</summary>
    private readonly DispatcherTimer _melderHerbevestigingsTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };

    /// <summary>Ronde-robin-index voor VraagVolgendeMelderStatusOp hieronder - onthoudt welk
    /// meldpunt bij de VOLGENDE tik aan de beurt is, zodat elke tik maar één aanvraag
    /// verstuurt in plaats van de hele lijst in één keer (zie _melderHerbevestigingsTimer
    /// hierboven voor de volledige toelichting).</summary>
    private int _melderHerbevestigingsIndex;

    /// <summary>Vervangt de oude "alle meldpunten in één klap elke 4 seconden"-aanpak (zie
    /// _melderHerbevestigingsTimer hierboven) - stuurt bij elke tik (nu elke 600ms) precies
    /// ÉÉN meldpunt-aanvraag, round-robin door de volledige, actuele lijst van bekende
    /// meldpunten. Logt bewust alleen bij het ROND-ZIJN van een volledige cyclus (niet bij
    /// elke individuele aanvraag) - anders zou de hardware-communicatielog bij 30 meldpunten
    /// elke 18 seconden een aparte regel per meldpunt krijgen, puur ruis voor wie de log leest
    /// op zoek naar daadwerkelijke gebeurtenissen.</summary>
    private void VraagVolgendeMelderStatusOp()
    {
        var alleMelders = _beheerder.Blokken.SelectMany(b => b.Bezetmeldpunten).Select(m => m.MeldernNummer).Where(m => m > 0).Distinct().ToList();
        if (alleMelders.Count == 0) return;
        if (_melderHerbevestigingsIndex >= alleMelders.Count) _melderHerbevestigingsIndex = 0;
        _hardwareBeheerder.VraagMelderStatusOp(alleMelders[_melderHerbevestigingsIndex]);
        _melderHerbevestigingsIndex++;
        if (_melderHerbevestigingsIndex >= alleMelders.Count)
        {
            _melderHerbevestigingsIndex = 0;
            HardwareCommunicatieLog.Log("Info", $"Status van alle {alleMelders.Count} bekende bezetmelder(s) opgevraagd periodieke herbevestiging (round-robin cyclus afgerond).");
        }
    }

    /// <summary>Loc verwijderen vanuit het kijkscherm - stopt eerst een eventueel lopende
    /// rit (anders zou de simulator de loc bij de volgende stap gewoon weer ergens neer-
    /// zetten), en haalt 'm dan pas echt van de baan. TreinrouteWindow hoeft hiervoor niet
    /// zichtbaar te worden - dit werkt net als de andere kijkscherm-acties, ook al bestaat
    /// het venster mogelijk (onzichtbaar) al op de achtergrond, zie ZorgVoorOpenTreinrouteWindow.</summary>
    private void LocVerwijderdGevraagd_Verwerken(Trein trein)
    {
        if (_treinrouteWindow != null)
            _treinrouteWindow.StopTreinIndienActief(trein);

        var huidigBlok = _beheerder.BlokVanLoc(trein);
        if (huidigBlok != null)
        {
            _beheerder.VerwijderLoc(huidigBlok);
            _beheerder.ZetHandmatigBezet(huidigBlok, false); // zie BaanCanvas_Drop (BaanontwerpWindow) - symmetrisch weer opheffen
        }
        // Zelfde reden als bij LocWeghalen_Click hieronder: VerwijderLoc vuurt geen event
        // af, dus zonder dit blijft het gele decodernummer op MainWindow's eigen
        // blokkenschema-canvas staan als dat venster toevallig (door een beheerder) open is.
        Redraw();
    }

    /// <summary>Zorgt dat TreinrouteWindow BESTAAT (nodig zodat de simulatie/timers achter
    /// de schermen kunnen draaien wanneer het kijkscherm een rit start), maar toont het
    /// venster NIET - anders zou elke kijkscherm-actie (loc slepen, Go, Opstellen) het
    /// bouwscherm alsnog zichtbaar en dus bewerkbaar maken, zonder dat daar ooit voor
    /// ingelogd is. Alleen de expliciete, login-gated OpenTreinroutes_Click mag het venster
    /// écht laten zien.</summary>
    /// <summary>Zorgt dat TreinrouteWindow BESTAAT (nodig zodat de simulatie/timers achter
    /// de schermen kunnen draaien wanneer het kijkscherm een rit start), maar toont het
    /// venster NIET - anders zou elke kijkscherm-actie (loc slepen, Go, Opstellen) het
    /// bouwscherm alsnog zichtbaar en dus bewerkbaar maken, zonder dat daar ooit voor
    /// ingelogd is. Alleen de expliciete, login-gated OpenTreinroutes_Click mag het venster
    /// écht laten zien.
    /// BELANGRIJK: bestaan wordt hier bewust alleen op "is het veld null" gecontroleerd,
    /// NIET op .IsLoaded - een venster dat nooit .Show() heeft gekregen blijft IsLoaded=false
    /// voor altijd, dus die check zou hier bij ELKE aanroep een compleet NIEUW venster
    /// aanmaken en de lopende simulatie daarin weggooien (precies de bug die dit veroorzaakte:
    /// meerdere treinen bleven op "Handmatig" staan na Go, omdat elke volgende trein in
    /// dezelfde bulk-actie het venster van de vorige trein alweer verving).</summary>
    private void ZorgVoorOpenTreinrouteWindow()
    {
        if (_treinrouteWindow is null)
        {
            _treinrouteWindow = new TreinrouteWindow(_beheerder, _routeBeheerder, _baanBeheerder, _wisselstraatBeheerder, _treintypeBeheerder, _stopverbodBeheerder, _stopverbodStilstandBeheerder, _treinBeheerder, _hardwareBeheerder, _blokgroepBeheerder, _snelleKlokBeheerder)
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterScreen
            };
            // Bewust GEEN .Show() hier - zie de uitleg hierboven.
            // Als het venster later ALSNOG écht gesloten wordt (via de expliciete
            // bouwscherm-route, waar het venster wél zichtbaar is en dus een sluitknop
            // heeft), moet het veld weer op null - anders denkt de rest van de code dat er
            // nog een bruikbaar venster bestaat terwijl het al weg is.
            _treinrouteWindow.Closed += (_, _) => _treinrouteWindow = null;
            _treinrouteWindow.ProgrammaAfsluitenGevraagd += () => Close();
        }
    }

    private void LocNaarAnderBlokGesleept_Verwerken(Trein trein, Blok doelBlok)
    {
        var huidigBlok = _beheerder.BlokVanLoc(trein);
        if (huidigBlok is null) return;

        var route = _routeBeheerder.VindPadNaarBlok(huidigBlok, doelBlok, _beheerder, trein.Treintype);
        if (route is null)
        {
            MessageBox.Show(this, $"Er is geen toegestaan pad gevonden van blok {huidigBlok.Nummer} naar blok {doelBlok.Nummer} (bijv. door een richtingsverbod).", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        route.Omschrijving = $"Loc {trein.Omschrijving}: blok {huidigBlok.Nummer} -> blok {doelBlok.Nummer}";
        route.Treintype = trein.Treintype;
        _routeBeheerder.Treinroutes.Add(route);

        // WindowStartupLocation.CenterOwner NIET gebruiken hier: dit kan ook vanuit het
        // kijkscherm getriggerd worden terwijl MainWindow zelf verborgen is (Hide(), zie de
        // constructor) - een venster centreren op een VERBORGEN owner kan op een onverwachte
        // plek terechtkomen, waardoor het lijkt of er niets gebeurt. CenterScreen is hier
        // veiliger.
        ZorgVoorOpenTreinrouteWindow();
        // GEEN .Activate() hier - dat zou het (bewust onzichtbare) TreinrouteWindow alsnog
        // op het scherm laten verschijnen. De simulatie draait prima door zonder dat het
        // venster ooit zichtbaar wordt; alleen de expliciete, login-gated
        // OpenTreinroutes_Click mag het venster echt tonen.
        _treinrouteWindow!.StartAutomatischeRoute(route, trein);
    }

    /// <summary>Één gedeeld Help-venster - net als bij Baanontwerp/Treinroutes wordt een
    /// al openstaand venster gewoon naar voren gehaald i.p.v. een tweede exemplaar te
    /// openen. Owner=this zorgt dat het Help-venster met MainWindow meegaat (bijv. bij
    /// minimaliseren), maar het is bewust GEEN modaal venster - je kunt gewoon verder
    /// klikken in de rest van het programma terwijl de gebruiksaanwijzing open staat.</summary>
    private void Gebruiksaanwijzing_Click(object sender, RoutedEventArgs e)
    {
        if (_helpWindow is null || !_helpWindow.IsLoaded)
        {
            _helpWindow = new HelpWindow { Owner = this };
            _helpWindow.Show();
        }
        else
        {
            _helpWindow.Activate();
        }
    }

    private void OverProgramma_Click(object sender, RoutedEventArgs e)
    {
        var dialoog = new AboutDialog { Owner = this };
        dialoog.ShowDialog();
    }

    private void OpenTreinroutes_Click(object sender, RoutedEventArgs e)
    {
        if (_treinrouteWindow is null)
        {
            _treinrouteWindow = new TreinrouteWindow(_beheerder, _routeBeheerder, _baanBeheerder, _wisselstraatBeheerder, _treintypeBeheerder, _stopverbodBeheerder, _stopverbodStilstandBeheerder, _treinBeheerder, _hardwareBeheerder, _blokgroepBeheerder, _snelleKlokBeheerder)
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterScreen // kan getriggerd worden terwijl MainWindow verborgen is
            };
            _treinrouteWindow.Closed += (_, _) => _treinrouteWindow = null;
            _treinrouteWindow.ProgrammaAfsluitenGevraagd += () => Close();
        }
        // Altijd expliciet .Show() aanroepen (niet alleen bij een nieuw venster): het
        // venster kan al ONZICHTBAAR bestaan doordat het kijkscherm het op de achtergrond
        // liet draaien (zie ZorgVoorOpenTreinrouteWindow) - .Activate() alleen zou zo'n
        // nog-nooit-getoond venster niet zichtbaar maken. Show() op een al zichtbaar
        // venster is onschadelijk.
        _treinrouteWindow.Show();
        _treinrouteWindow.Activate();
    }

    private void Exporteren_Click(object sender, RoutedEventArgs e)
    {
        var dialoog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "PNG-afbeelding (*.png)|*.png",
            FileName = "blokkenschema.png"
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

            MessageBox.Show(this, $"Blokkenschema geëxporteerd naar {dialoog.FileName}.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Exporteren mislukt: {ex.Message}", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void GaNaarBlok_Click(object sender, RoutedEventArgs e) => GaNaarBlok();

    private void GaNaarBlokBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) GaNaarBlok();
    }

    /// <summary>Scrolt het blokkenschema zodat het opgegeven bloknummer gecentreerd in
    /// beeld komt, en selecteert het (zelfde blauwe markering als een gewone klik) zodat
    /// je meteen ziet welk blok het is - handig nu baanindelingen honderden blokken
    /// groot kunnen zijn en je niet steeds handmatig hoeft rond te scrollen.</summary>
    private void GaNaarBlok()
    {
        if (!int.TryParse(GaNaarBlokBox.Text, out int nummer))
        {
            MessageBox.Show(this, "Vul een geldig bloknummer in.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var blok = _beheerder.Blokken.FirstOrDefault(b => b.Nummer == nummer);
        if (blok is null)
        {
            MessageBox.Show(this, $"Blok {nummer} bestaat niet.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _geselecteerdBlok = blok;
        double doelX = Math.Max(0, blok.SchemaX + BlokGrootte / 2 - SchemaScrollViewer.ViewportWidth / 2);
        double doelY = Math.Max(0, blok.SchemaY + BlokGrootte / 2 - SchemaScrollViewer.ViewportHeight / 2);
        SchemaScrollViewer.ScrollToHorizontalOffset(doelX);
        SchemaScrollViewer.ScrollToVerticalOffset(doelY);
        Redraw();
    }

    private void HardwareInterface_Click(object sender, RoutedEventArgs e)
    {
        var dialoog = new HardwareDialog(_hardwareBeheerder, _baanBeheerder, _beheerder) { Owner = this };
        dialoog.ShowDialog();
    }

    /// <summary>GEBRUIKERSVERZOEK: het rijstroom-adres van een elektrisch geïsoleerde
    /// sectie (bijv. een kruiswissel) waarvan het adres onbekend is, empirisch vinden
    /// door een reeks kandidaat-adressen af te lopen en te kijken welke een bezetmelder
    /// laat wisselen. Niet-modaal, net als het Help-venster - blijft open terwijl je
    /// tegelijk de baan in de gaten houdt.</summary>
    private void AdresZoeken_Click(object sender, RoutedEventArgs e)
    {
        var dialoog = new AdresZoekenDialog(_hardwareBeheerder, _beheerder, _treinBeheerder) { Owner = this };
        dialoog.Show();
    }

    /// <summary>GEBRUIKERSVERZOEK ("kan je ook zoiets maken als we voor de rijstroom-
    /// nummering hebben gemaakt, voor het opzoeken van de wisselnummers") - het
    /// wissel-equivalent van AdresZoeken_Click hierboven. Zie WisselAdresZoekenDialog voor
    /// het belangrijkste verschil: hier is geen automatische herkenning mogelijk (geen
    /// terugmelding van een wisselstand), dus dit scherm doorloopt alleen de adressen -
    /// zelf kijken/luisteren welke fysieke wissel beweegt blijft nodig.</summary>
    private void WisselAdresZoeken_Click(object sender, RoutedEventArgs e)
    {
        var dialoog = new WisselAdresZoekenDialog(_hardwareBeheerder) { Owner = this };
        dialoog.Show();
    }

    /// <summary>GEBRUIKERSVERZOEK: live zien welke bezetmelder oplicht terwijl je de loc met
    /// de hand over de baan duwt - vooral bedoeld om melders die nog aan geen enkel blok
    /// gekoppeld zijn (zoals melder 8 eerder) aan een fysieke plek te koppelen. Niet-modaal,
    /// zodat het scherm open kan blijven terwijl je met de baan bezig bent.</summary>
    private void MeldpuntenVerkenner_Click(object sender, RoutedEventArgs e)
    {
        var dialoog = new MeldpuntenVerkennerDialog(_beheerder) { Owner = this };
        dialoog.Show();
    }

    /// <summary>Niet-modaal (zoals de Help-gebruiksaanwijzing) - zodat je 'm open kunt
    /// laten staan terwijl je ondertussen andere schermen bedient/test.</summary>
    private void HardwareLogTonen_Click(object sender, RoutedEventArgs e)
    {
        if (_hardwareLogDialog is null || !_hardwareLogDialog.IsLoaded)
        {
            _hardwareLogDialog = new HardwareLogDialog { Owner = this };
            _hardwareLogDialog.Show();
        }
        else
        {
            _hardwareLogDialog.Activate();
        }
    }

    /// <summary>Gebruikersvraag: "hoe bereken je de gemiddelde tijd... kan ik dat ook ergens
    /// inzichtelijk maken" - dit venster laat precies zien wat BlokBeheerder.
    /// RegistreerGeleerdeReistijd tot nu toe geleerd heeft, per blok.</summary>
    private void GeleerdeReistijdenBekijken_Click(object sender, RoutedEventArgs e)
    {
        new GeleerdeReistijdenDialog(_beheerder, _treinBeheerder) { Owner = this }.ShowDialog();
    }

    /// <summary>Gebruikerscorrectie tijdens uitgebreid hardware-testen: een blok kan een
    /// GELEERDE reistijd (Blok.GeleerdeReistijden) hebben overgehouden aan een
    /// afwijkende, te snelle meting uit een eerdere testsessie (bijv. van vóór een
    /// software-fix die het gedrag inmiddels beter maakt) - dat zorgt dan voor een te
    /// korte veiligheidsdrempel (MeldTreinVastgelopen/StartStaartFase), met een voortijdig
    /// gestopte loc tot gevolg terwijl hij feitelijk nog gewoon goed onderweg was. Dit wist
    /// die geleerde waarden voor ALLE blokken/richtingen/locs - de eerstvolgende rit naar
    /// elk blok begint dan weer met de generieke, ruime standaardwaarde en leert vanaf daar
    /// opnieuw op.</summary>
    private void GeleerdeReistijdenWissen_Click(object sender, RoutedEventArgs e)
    {
        int aantal = _beheerder.Blokken.Sum(b => b.GeleerdeReistijden.Count);
        if (aantal == 0)
        {
            MessageBox.Show(this, "Er staan nog nergens geleerde reistijden geregistreerd.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show(this, $"Geleerde reistijden ({aantal} combinatie(s) van blok/richting/loc) wissen? De eerstvolgende rit naar zo'n blok gebruikt dan weer de ruime standaard-veiligheidsdrempel en leert vanaf daar opnieuw op.",
            "Modeltreinbesturing", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        foreach (var blok in _beheerder.Blokken) blok.GeleerdeReistijden.Clear();
        MessageBox.Show(this, $"Geleerde reistijden ({aantal} combinatie(s)) gewist.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>Alleen een BEHEERDER (niet elke ingelogde bouwscherm-gebruiker) mag hier
    /// komen - vereist dus eerst een login (als er nog geen actieve sessie is), en
    /// controleert daarna expliciet IsAdmin.</summary>
    private void GebruikersBeheren_Click(object sender, RoutedEventArgs e)
    {
        MetLoginToegang(() =>
        {
            if (_ingelogdeGebruiker?.IsAdmin != true)
            {
                MessageBox.Show(this, "Alleen een beheerder mag gebruikers beheren.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var dialoog = new GebruikersBeherenDialog(_gebruikersBeheerder, _ingelogdeGebruiker) { Owner = this };
            dialoog.ShowDialog();
        });
    }

    private void TreintypesBeheren_Click(object sender, RoutedEventArgs e)
    {
        var dialoog = new TreintypeDialog(_treintypeBeheerder, _treinBeheerder, _routeBeheerder, _richtingsverbodBeheerder, _stopverbodBeheerder, _stopverbodStilstandBeheerder) { Owner = this };
        dialoog.ShowDialog();
    }

    private void TreinenBeheren_Click(object sender, RoutedEventArgs e)
    {
        var dialoog = new TreinDialog(_treinBeheerder, _treintypeBeheerder, _snelheidsMetingBeheerder) { Owner = this };
        dialoog.ShowDialog();
    }

    private void KleurenschemaAanpassen_Click(object sender, RoutedEventArgs e)
    {
        var dialoog = new KleurenschemaDialog { Owner = this };
        dialoog.ShowDialog();
    }

    private void LocPlaatsen_Click(object sender, RoutedEventArgs e)
    {
        if (_geselecteerdBlok is null)
        {
            MessageBox.Show(this, "Selecteer eerst een blok door het aan te klikken.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (_treinBeheerder.Treinen.Count == 0)
        {
            MessageBox.Show(this, "Er zijn nog geen treinen om te plaatsen - maak er eerst een aan via 'Treinen beheren'.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dialoog = new LocKiezenDialog(_treinBeheerder, _geselecteerdBlok) { Owner = this };
        if (dialoog.ShowDialog() == true && dialoog.GekozenTrein != null)
        {
            _beheerder.PlaatsLoc(_geselecteerdBlok, dialoog.GekozenTrein);
            _beheerder.ZetHandmatigBezet(_geselecteerdBlok, true); // zie BaanCanvas_Drop (BaanontwerpWindow) - voorkomt een spookmelding bij de eerstvolgende echte bezetmelding
            Redraw();
        }
    }

    private void LocWeghalen_Click(object sender, RoutedEventArgs e)
    {
        if (_geselecteerdBlok is null)
        {
            MessageBox.Show(this, "Selecteer eerst een blok door het aan te klikken.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _beheerder.VerwijderLoc(_geselecteerdBlok);
        _beheerder.ZetHandmatigBezet(_geselecteerdBlok, false); // zie LocPlaatsen_Click - symmetrisch weer opheffen
        Redraw();
    }

    private void RichtingsverbodenBeheren_Click(object sender, RoutedEventArgs e)
    {
        var dialoog = new RichtingsverbodDialog(_beheerder, _richtingsverbodBeheerder, _treintypeBeheerder) { Owner = this };
        dialoog.ShowDialog();
        Redraw();
    }

    private void StopverbodenBeheren_Click(object sender, RoutedEventArgs e)
    {
        var dialoog = new StopverbodDialog(_beheerder, _stopverbodBeheerder, _treintypeBeheerder) { Owner = this };
        dialoog.ShowDialog();
        Redraw();
    }

    private void StopverbodStilstandBeheren_Click(object sender, RoutedEventArgs e)
    {
        var dialoog = new StopverbodStilstandDialog(_beheerder, _stopverbodStilstandBeheerder, _treintypeBeheerder) { Owner = this };
        dialoog.ShowDialog();
        Redraw();
    }

    private void OverzichtBeperkingen_Click(object sender, RoutedEventArgs e)
    {
        var dialoog = new BeperkingenOverzichtDialog(_richtingsverbodBeheerder, _stopverbodBeheerder, _stopverbodStilstandBeheerder, _routeBeheerder) { Owner = this };
        dialoog.ShowDialog();
    }

    private void BlokgroepenBeheren_Click(object sender, RoutedEventArgs e)
    {
        var dialoog = new BlokgroepenDialog(_blokgroepBeheerder, _beheerder) { Owner = this };
        dialoog.ShowDialog();
    }

    private void RelatiesBeheren_Click(object sender, RoutedEventArgs e)
    {
        var dialoog = new RelatiesDialog(_beheerder) { Owner = this };
        dialoog.ShowDialog();
        Redraw();
    }

    private void ActiesBeheren_Click(object sender, RoutedEventArgs e)
    {
        var dialoog = new ActiesDialog(_beheerder, _baanBeheerder, _actieBeheerder) { Owner = this };
        dialoog.ShowDialog();
    }

    private void AlgemeneInstellingenBeheren_Click(object sender, RoutedEventArgs e)
    {
        var dialoog = new AlgemeneInstellingenDialog(_beheerder, _snelheidsMetingBeheerder) { Owner = this };
        dialoog.ShowDialog();
    }

    private void OnderhoudBeheren_Click(object sender, RoutedEventArgs e)
    {
        var dialoog = new OnderhoudsDialog(_onderhoudsBeheerder) { Owner = this };
        dialoog.ShowDialog();
    }

    private void DashboardTonen_Click(object sender, RoutedEventArgs e)
    {
        var dialoog = new DashboardDialog(_beheerder, _baanBeheerder, _treinBeheerder, _routeBeheerder, _onderhoudsBeheerder, _treinrouteWindow) { Owner = this };
        dialoog.ShowDialog();
    }

    private void BaanControleren_Click(object sender, RoutedEventArgs e)
    {
        var dialoog = new BaanControleDialog(_beheerder, _baanBeheerder, _routeBeheerder) { Owner = this };
        dialoog.ShowDialog();
    }

    private void BaankaartVergelijken_Click(object sender, RoutedEventArgs e)
    {
        var kies = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Kies een baankaart van de Baanverkenner",
            Filter = "Baankaart (*.json)|*.json|Alle bestanden (*.*)|*.*"
        };
        if (kies.ShowDialog(this) != true) return;

        Baanverkenner.Kern.Baankaart? kaart;
        try
        {
            kaart = Baanverkenner.Kern.Baankaart.VanJson(System.IO.File.ReadAllText(kies.FileName));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Dit bestand is geen geldige baankaart:\n{ex.Message}", "Baankaart vergelijken", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (kaart is null)
        {
            MessageBox.Show(this, "Het bestand is leeg.", "Baankaart vergelijken", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var meldingen = new BaankaartVergelijker().Vergelijk(kaart, _beheerder, _baanBeheerder);
        var dialoog = new BaanControleDialog(
            "Baankaart vergelijken",
            $"Vergelijking van '{System.IO.Path.GetFileName(kies.FileName)}' ({kaart.Melders.Count} melders, {kaart.Wissels.Count} wissels met effect) met dit project. Puur signalerend - er wordt niets aangepast.",
            meldingen) { Owner = this };
        dialoog.ShowDialog();
    }

    private void HandmatigBezet_Click(object sender, RoutedEventArgs e)
    {
        if (_geselecteerdBlok is null)
        {
            MessageBox.Show(this, "Selecteer eerst een blok door het aan te klikken.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        bool nieuweStatus = !_beheerder.IsHandmatigBezet(_geselecteerdBlok);
        _beheerder.ZetHandmatigBezet(_geselecteerdBlok, nieuweStatus);
        Redraw();
    }

    private void Foutmelding_Click(object sender, RoutedEventArgs e)
    {
        if (_geselecteerdBlok is null)
        {
            MessageBox.Show(this, "Selecteer eerst een blok door het aan te klikken.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        bool nieuweStatus = !_beheerder.IsFoutmelding(_geselecteerdBlok);
        _beheerder.ZetFoutmelding(_geselecteerdBlok, nieuweStatus);
        Redraw();
    }

    private void Opslaan_Click(object sender, RoutedEventArgs e)
    {
        var dialoog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Modeltreinbesturing baan (*.json)|*.json",
            FileName = "baan.json"
        };
        if (dialoog.ShowDialog() == true)
        {
            try
            {
                ProjectOpslag.SlaOp(dialoog.FileName, _beheerder, _baanBeheerder, _wisselstraatBeheerder, _routeBeheerder, _treintypeBeheerder, _richtingsverbodBeheerder, _stopverbodBeheerder, _stopverbodStilstandBeheerder, _treinBeheerder, _gebruikersBeheerder, _actieBeheerder, _snelheidsMetingBeheerder, _onderhoudsBeheerder, _snelleKlokBeheerder);
                RecenteProjecten.Vermeld(dialoog.FileName);
                // Zie HuidigProject: na "Opslaan als" is DIT bestand vanaf nu het geladen
                // project (bijv. bij een naam-wijziging) - titelbalk gelijk mee bijwerken.
                HuidigProject.Zet(dialoog.FileName);
                Title = $"Modeltreinbesturing - Onderhouden blokken — {HuidigProject.Bestandsnaam}";
                MessageBox.Show(this, "Baan opgeslagen.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Opslaan mislukt: {ex.Message}", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void Nieuw_Click(object sender, RoutedEventArgs e)
    {
        var vraag = MessageBox.Show(this,
            "Nieuw project beginnen? Alles wat nu open staat (blokken, baanontwerp, wisselstraten, routes) gaat verloren als je het nog niet hebt opgeslagen.",
            "Nieuw project", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (vraag != MessageBoxResult.Yes) return;

        _beheerder.Reset();
        _baanBeheerder.Reset();
        _wisselstraatBeheerder.Reset();
        _routeBeheerder.Reset();
        _richtingsverbodBeheerder.Reset();
        _stopverbodBeheerder.Reset();
        _stopverbodStilstandBeheerder.Reset();
        _treintypeBeheerder.Reset();
        _treinBeheerder.Reset();
        _gebruikersBeheerder.Reset();
        _actieBeheerder.Reset();
        _onderhoudsBeheerder.Reset();
        _snelleKlokBeheerder.Reset();
        var nieuweMeetinstellingen = new SnelheidsMetingInstellingen();
        _snelheidsMetingBeheerder.Instellingen.MeldernNummer1 = nieuweMeetinstellingen.MeldernNummer1;
        _snelheidsMetingBeheerder.Instellingen.MeldernNummer2 = nieuweMeetinstellingen.MeldernNummer2;
        _snelheidsMetingBeheerder.Instellingen.LengteTrajectMm = nieuweMeetinstellingen.LengteTrajectMm;
        _snelheidsMetingBeheerder.Instellingen.Modelschaal = nieuweMeetinstellingen.Modelschaal;
        _snelheidsMetingBeheerder.Instellingen.ManierVanMeten = nieuweMeetinstellingen.ManierVanMeten;
        SimulatieInstellingen.StandaardUitrolSeconden = 1.0;
        SimulatieInstellingen.WisselRustpauzeMilliseconden = 100;
        _ingelogdeGebruiker = null;
        _geselecteerdBlok = null;

        // Zie HuidigProject: een vers, nog niet opgeslagen project heeft nog geen pad - de
        // titelbalk/log moeten dat ook zo tonen, i.p.v. het pad van het vorige project te
        // blijven claimen.
        HuidigProject.Wis();
        Title = $"Modeltreinbesturing - Onderhouden blokken — {HuidigProject.Bestandsnaam}";

        _baanontwerpWindow?.Close();
        _baanontwerpWindow = null;
        _treinrouteWindow?.Close();
        _treinrouteWindow = null;
        // BUGFIX (gebruikersmelding: kijkscherm bleef de OUDE database tonen na "Nieuw"):
        // dit venster werd hier nooit meegenomen - alleen de twee bouwschermen. Zoals bij
        // de andere twee: sluiten zodat een verse aanmaak de nieuwe data pakt - maar
        // ANDERS dan de andere twee, meteen weer automatisch openen als het al open
        // stond, want het kijkscherm is voor de eindgebruiker (zonder login) vaak het
        // ENIGE zichtbare venster - simpelweg laten verdwijnen zou hem zonder scherm zetten.
        bool kijkschermStondOpen = _baanoverzichtKijkWindow is { IsLoaded: true };
        _baanoverzichtKijkWindow?.Close();
        _baanoverzichtKijkWindow = null;

        Redraw();
        if (kijkschermStondOpen) OpenOfActiveerKijkscherm();

        // Een nieuwe database moet altijd meteen zijn eerste (echte) beheerder krijgen -
        // anders is er straks helemaal geen account meer dat toegang tot de bouwschermen
        // kan geven. Het kijkscherm zelf heeft hier niets mee te maken, dat blijft vrij
        // toegankelijk.
        var eersteBeheerderDialoog = new NieuwAccountDialog(_gebruikersBeheerder, verplichtBeheerder: true) { Owner = this };
        eersteBeheerderDialoog.ShowDialog();
    }

    private void Laden_Click(object sender, RoutedEventArgs e)
    {
        var dialoog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Modeltreinbesturing baan (*.json)|*.json"
        };
        if (dialoog.ShowDialog() == true)
        {
            try
            {
                LaadBestandEnVerversAlles(dialoog.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Laden mislukt: {ex.Message}", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    /// <summary>Vult "Recent geopend" met de laatst gebruikte projecten, telkens opnieuw
    /// zodra het submenu geopend wordt (zodat een net-toegevoegd bestand ook meteen
    /// verschijnt, zonder het hele venster te hoeven herstarten).</summary>
    private void RecenteProjectenMenu_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        RecenteProjectenMenu.Items.Clear();
        var recente = RecenteProjecten.Lijst();
        if (recente.Count == 0)
        {
            RecenteProjectenMenu.Items.Add(new MenuItem { Header = "(nog geen recente projecten)", IsEnabled = false });
            return;
        }
        foreach (var pad in recente)
        {
            var item = new MenuItem { Header = pad };
            item.Click += (_, _) =>
            {
                try { LaadBestandEnVerversAlles(pad); }
                catch (Exception ex) { MessageBox.Show(this, $"Laden mislukt: {ex.Message}", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Error); }
            };
            RecenteProjectenMenu.Items.Add(item);
        }
    }

    /// <summary>Kiezen uit een eerdere automatische backup - zelfde nazorg als gewoon laden
    /// (seinen aanvullen, adressen uniek maken, blokposities normaliseren, open vensters
    /// verversen), zodat een backup net zo betrouwbaar opent als een handmatig bestand.</summary>
    /// <summary>Sluit het programma expliciet netjes af (via Close(), dus met het normale
    /// Closing-event dat de backup maakt en de vensterpositie onthoudt). Belangrijk om dit
    /// te gebruiken i.p.v. bijv. Visual Studio's rode stopknop tijdens het debuggen: die
    /// breekt het proces hard af, waardoor GEEN opruimcode meer draait - dus ook geen
    /// backup wordt gemaakt. Deze knop garandeert dat de procedure altijd wordt gevolgd.</summary>
    private void ProgrammaAfsluiten_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>Verbergt dit venster weer (i.p.v. het te sluiten) - voor als een beheerder
    /// het blokkenschema via het kijkscherm-menu heeft geopend om iets aan te passen, en
    /// daarna gewoon weer terug wil naar het kijkscherm zonder meteen het HELE programma af
    /// te sluiten (dat zou Close() wél doen, want dat vuurt de volledige afsluit-procedure
    /// inclusief backup af). Dit venster blijft gewoon bestaan op de achtergrond, precies
    /// zoals het standaard al doet sinds het opstarten.</summary>
    private void VerbergBlokkenschema_Click(object sender, RoutedEventArgs e) => Hide();

    private void OudereBackupOpenen_Click(object sender, RoutedEventArgs e)
    {
        var dialoog = new OudereBackupDialog { Owner = this };
        if (dialoog.ShowDialog() != true || dialoog.GekozenPad is null) return;
        try
        {
            LaadBestandEnVerversAlles(dialoog.GekozenPad);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Backup laden mislukt: {ex.Message}", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LaadBestandEnVerversAlles(string pad)
    {
        ProjectOpslag.Laad(pad, _beheerder, _baanBeheerder, _wisselstraatBeheerder, _routeBeheerder, _treintypeBeheerder, _richtingsverbodBeheerder, _stopverbodBeheerder, _stopverbodStilstandBeheerder, _treinBeheerder, _gebruikersBeheerder, _actieBeheerder, _snelheidsMetingBeheerder, _onderhoudsBeheerder, _snelleKlokBeheerder);
        RecenteProjecten.Vermeld(pad);
        // GEBRUIKERSVERZOEK ("kan je ook ergens in het scherm vermelden welke JSON er geladen
        // is, en ook in de log, dan kan daar nooit twijfel over zijn"): zie HuidigProject - dit
        // is de ENE, centrale plek waar elke manier van laden (opstarten, Openen, Recent
        // geopend, Oudere backup openen) doorheen loopt, dus hier zetten volstaat voor alle
        // vensters. Titelbalk van dit venster meteen mee bijwerken.
        HuidigProject.Zet(pad);
        Title = $"Modeltreinbesturing - Onderhouden blokken — {HuidigProject.Bestandsnaam}";
        // Een geladen database heeft zijn eigen gebruikerslijst - een sessie-login van
        // vóór het laden hoort daar niet meer bij, dus opnieuw inloggen bij de eerstvolgende
        // bouwscherm-poging.
        _ingelogdeGebruiker = null;
        _geselecteerdBlok = null;

        // Oudere baanbestanden aanvullen met ontbrekende uitgaande seinen.
        foreach (var blok in _beheerder.Blokken) ZorgVoorUitgaandSein(blok);

        // Oudere baanbestanden van vóór het adres-veld hadden overal Adres=0 -
        // die dubbele adressen hier uniek maken.
        HerstelDubbeleAdressen();

        // Blokken kunnen een (licht) negatieve positie hebben van vóór het
        // blokkenschema een vast, scrollbaar canvas met origine (0,0) kreeg - daar kan
        // niet naartoe gescrold worden, dus zulke blokken terugschuiven binnen beeld.
        NormaliseerBlokPosities();

        // Oudere baanbestanden kennen sommige, later toegevoegde ankerlijsten op Lijn nog
        // niet (bijv. PuntKruiswisselAnkers) - die blijven na het inlezen leeg terwijl
        // Punten al wél gevuld is, wat een crash gaf bij het eerstvolgende verslepen van
        // zo'n lijnpunt (ArgumentOutOfRangeException). Trek dit voor ELKE lijn recht.
        foreach (var lijn in _baanBeheerder.Symbolen.OfType<Lijn>()) lijn.HerstelAnkerLijstenLengte();

        // Als een van de andere vensters al open staat, laat de geladen gegevens correct
        // meekomen door ze opnieuw te openen in plaats van de oude stand te tonen.
        _baanontwerpWindow?.Close();
        _baanontwerpWindow = null;
        _treinrouteWindow?.Close();
        _treinrouteWindow = null;
        // BUGFIX (gebruikersmelding: kijkscherm bleef de OUDE database tonen na het laden
        // van een ander bestand): dit venster werd hier nooit meegenomen - alleen de twee
        // bouwschermen. Zoals bij de andere twee: sluiten zodat een verse aanmaak de
        // nieuwe data pakt - maar ANDERS dan de andere twee, meteen weer automatisch
        // openen als het al open stond, want het kijkscherm is voor de eindgebruiker
        // (zonder login) vaak het ENIGE zichtbare venster - simpelweg laten verdwijnen
        // zou hem zonder scherm zetten.
        bool kijkschermStondOpen = _baanoverzichtKijkWindow is { IsLoaded: true };
        _baanoverzichtKijkWindow?.Close();
        _baanoverzichtKijkWindow = null;

        Redraw();
        if (kijkschermStondOpen) OpenOfActiveerKijkscherm();
    }

    // --- Hit-testing -------------------------------------------------------

    private Blok? BlokOpPositie(Point positie, Blok? uitzonderen = null)
    {
        foreach (var blok in _beheerder.Blokken)
        {
            if (blok == uitzonderen) continue;
            bool binnenX = positie.X >= blok.SchemaX && positie.X <= blok.SchemaX + BlokGrootte;
            bool binnenY = positie.Y >= blok.SchemaY && positie.Y <= blok.SchemaY + BlokGrootte;
            if (binnenX && binnenY) return blok;
        }
        return null;
    }

    // --- Tekenen -------------------------------------------------------

    private void Redraw()
    {
        SchemaCanvas.Children.Clear();

        foreach (var relatie in _beheerder.Relaties)
        {
            try { TekenRelatie(relatie); }
            catch (Exception ex)
            {
                // Eén probleem-relatie mag niet het hele scherm blanco maken - de rest
                // moet gewoon zichtbaar blijven, en de fout moet zichtbaar zijn i.p.v. stil
                // te verdwijnen (dat gaf eerder een compleet leeg blokkenschema-venster).
                System.Diagnostics.Debug.WriteLine($"Fout bij tekenen relatie {relatie.Van?.Nummer}->{relatie.Naar?.Nummer}: {ex}");
            }
        }

        foreach (var blok in _beheerder.Blokken)
        {
            try { TekenBlok(blok, blok == _geselecteerdBlok); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Fout bij tekenen blok {blok.Nummer}: {ex}");
                MessageBox.Show(this, $"Kon blok {blok.Nummer} niet tekenen: {ex.Message}\n\nDe rest van het schema wordt wel getoond. Meld deze foutmelding.", "Modeltreinbesturing - tekenfout", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private void TekenBlok(Blok blok, bool geselecteerd)
    {
        bool bezet = _beheerder.IsBezet(blok);
        bool handmatig = _beheerder.IsHandmatigBezet(blok);
        bool staart = _beheerder.IsStaart(blok);
        bool gereserveerd = _beheerder.IsGereserveerd(blok);
        bool foutmelding = _beheerder.IsFoutmelding(blok) && _beheerder.FoutmeldingKnipperAan;
        bool netVrijgegeven = _beheerder.IsNetVrijgegeven(blok) && !bezet && !gereserveerd;
        var rechthoek = new Rectangle
        {
            Width = BlokGrootte,
            Height = BlokGrootte,
            Fill = foutmelding ? KoploperKleuren.Foutmelding : handmatig ? KoploperKleuren.HandmatigBezet : staart ? KoploperKleuren.Staartindicatie : bezet ? KoploperKleuren.Bezet : gereserveerd ? KoploperKleuren.Gereserveerd : netVrijgegeven ? KoploperKleuren.NetVrijgegeven : blok.Vergrendeld ? KoploperKleuren.Vergrendeld : KoploperKleuren.Vrij,
            Stroke = geselecteerd ? KoploperKleuren.Geselecteerd : Brushes.Black,
            StrokeThickness = geselecteerd ? 3 : 1
        };
        Canvas.SetLeft(rechthoek, blok.SchemaX);
        Canvas.SetTop(rechthoek, blok.SchemaY);
        SchemaCanvas.Children.Add(rechthoek);

        var tekst = new TextBlock
        {
            Text = blok.Nummer.ToString(),
            FontWeight = FontWeights.Bold,
            Foreground = bezet || gereserveerd ? KoploperKleuren.TekstOpDonkereAchtergrond : KoploperKleuren.TekstOpLichteAchtergrond,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(tekst, blok.SchemaX + BlokGrootte / 2 - 6);
        Canvas.SetTop(tekst, blok.SchemaY + BlokGrootte / 2 - 8);
        SchemaCanvas.Children.Add(tekst);

        if (blok.Type != BlokType.Normaal)
        {
            var typeLabel = new TextBlock
            {
                Text = blok.Type switch { BlokType.Kopspoor => "K", BlokType.Station => "S", _ => "O" },
                FontSize = 9,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.DarkSlateBlue,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(typeLabel, blok.SchemaX + 2);
            Canvas.SetTop(typeLabel, blok.SchemaY + 2);
            SchemaCanvas.Children.Add(typeLabel);
        }

        var locOpBlok = _beheerder.LocOpBlok(blok);
        if (locOpBlok != null)
        {
            // Koploper toont bewust GEEN treinnaam/-nummer in het blokje, alleen het
            // locdecoder-nummer (zie de bevestiging uit een echte Koploper-gebruikers-
            // beschrijving). Zonder ingesteld decoderadres valt dit terug op de naam, zodat
            // een nog niet volledig ingevulde trein niet gewoon leeg in beeld komt.
            string locWeergave = locOpBlok.DecoderAdres > 0 ? locOpBlok.DecoderAdres.ToString() : locOpBlok.Omschrijving;
            var locIcoon = new Ellipse
            {
                Width = 16,
                Height = 16,
                Fill = Brushes.Gold,
                Stroke = Brushes.Black,
                StrokeThickness = 1,
                ToolTip = $"Loc: {locOpBlok.Omschrijving}" + (locOpBlok.DecoderAdres > 0 ? $" (decoder {locOpBlok.DecoderAdres})" : ""),
                IsHitTestVisible = false
            };
            Canvas.SetLeft(locIcoon, blok.SchemaX + BlokGrootte - 12);
            Canvas.SetTop(locIcoon, blok.SchemaY - 8);
            SchemaCanvas.Children.Add(locIcoon);

            var locLabel = new TextBlock
            {
                Text = locWeergave,
                FontSize = 8,
                Foreground = Brushes.DarkGoldenrod,
                FontWeight = FontWeights.Bold,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(locLabel, blok.SchemaX);
            Canvas.SetTop(locLabel, blok.SchemaY - 14);
            SchemaCanvas.Children.Add(locLabel);
        }
    }

    private void TekenRelatie(BlokRelatie relatie)
    {
        var van = MiddenpuntVan(relatie.Van);
        var naar = MiddenpuntVan(relatie.Naar);
        bool geselecteerd = relatie == _geselecteerdRelatie;

        var lijn = new Line
        {
            X1 = van.X,
            Y1 = van.Y,
            X2 = naar.X,
            Y2 = naar.Y,
            Stroke = geselecteerd ? Brushes.Red : Brushes.SteelBlue,
            StrokeThickness = geselecteerd ? 4 : 2,
            IsHitTestVisible = true,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        lijn.MouseLeftButtonDown += (_, e) =>
        {
            _geselecteerdRelatie = relatie;
            _geselecteerdBlok = null;
            Redraw();
            e.Handled = true; // niet laten doorvallen naar de lege-plek-klik van het canvas
        };
        SchemaCanvas.Children.Add(lijn);

        TekenPijlpunt(van, naar);
    }

    private Point MiddenpuntVan(Blok blok) =>
        new(blok.SchemaX + BlokGrootte / 2, blok.SchemaY + BlokGrootte / 2);

    /// <summary>Tekent een klein driehoekje op 70% van de lijn, wijzend naar "naar" - geeft de rijrichting aan.</summary>
    private void TekenPijlpunt(Point van, Point naar)
    {
        var richting = naar - van;
        if (richting.Length < 1) return;
        richting.Normalize();

        var basis = new Point(van.X + (naar.X - van.X) * 0.7, van.Y + (naar.Y - van.Y) * 0.7); // punt op 70% van de lijn

        var loodrecht = new Vector(-richting.Y, richting.X);
        const double pijlLengte = 10, pijlBreedte = 6;

        var punt1 = basis + richting * pijlLengte / 2;
        var punt2 = basis - richting * pijlLengte / 2 + loodrecht * pijlBreedte / 2;
        var punt3 = basis - richting * pijlLengte / 2 - loodrecht * pijlBreedte / 2;

        var pijl = new Polygon
        {
            Points = new PointCollection { punt1, punt2, punt3 },
            Fill = Brushes.SteelBlue,
            IsHitTestVisible = false
        };
        SchemaCanvas.Children.Add(pijl);
    }
}
