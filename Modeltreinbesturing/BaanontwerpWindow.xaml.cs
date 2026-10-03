using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Globalization;
using Modeltreinbesturing.Model;

namespace Modeltreinbesturing;

public partial class BaanontwerpWindow : Window
{
    private readonly BlokBeheerder _blokBeheerder;
    private readonly BaanOntwerpBeheerder _baanBeheerder;
    private readonly WisselstraatBeheerder _wisselstraatBeheerder;
    private readonly TreinBeheerder _treinBeheerder;
    private readonly ActieBeheerder _actieBeheerder;
    private readonly HardwareBeheerder _hardwareBeheerder;
    private readonly OnderhoudsBeheerder _onderhoudsBeheerder;
    private readonly TreinrouteBeheerder _routeBeheerder;

    /// <summary>Levert, op het moment zelf, de HUIDIGE TreinrouteWindow-instantie op (kan
    /// null zijn - er hoeft nog nooit een rit gestart te zijn) - voor de reservering-
    /// kleuring (zie BerekenGereserveerdeCorridors). Bewust een Func i.p.v. een vaste
    /// referentie: TreinrouteWindow bestaat vaak nog niet op het moment dat dit venster
    /// zelf aangemaakt wordt (MainWindow maakt 'm pas lazy aan bij de eerste rit), dus een
    /// vaste referentie zou hier meestal null blijven ook nadat er allang een rit rijdt.</summary>
    private readonly Func<TreinrouteWindow?>? _treinrouteWindowOpvragen;

    private const double BlokGrootte = 50;
    private const double SymboolGrootte = 20;

    private enum Tool { Verplaatsen, Lijn, Wissel, Sein, Stootblok, Perron, Tekst, Schakelaar, Pijl, Ontkoppelrail, Koppelen, Wisselstraat, Bezetmelder, Overloopwissel, Driewegwissel, Kruiswissel, EngelseWissel }
    private Tool _huidigTool = Tool.Verplaatsen;

    // Verplaatsen
    private Blok? _gesleeptBlok;
    private BaanSymbool? _gesleeptSymbool;
    private Vector _sleepOffset; // verschil tussen waar je klikte en het echte positiepunt van het blok/symbool, zodat slepen niet "springt"
    private BaanSymbool? _geselecteerdSymbool;

    // Lijn tekenen
    private Lijn? _lijnInAanleg;
    private Lijn? _gesleeptLijnPunt;
    private int _gesleeptLijnPuntIndex = -1;

    // Koppelen
    private Blok? _koppelGeselecteerdBlok;
    private Wissel? _overloopwisselEersteKeuze;
    private Blok? _kijkGesleeptLocBlok;

    // Wisselstraat: eerst van-blok, dan naar-blok kiezen; daarna wissels/lijnen aan-/uitklikken
    private Blok? _wisselstraatVanKandidaat;
    private Wisselstraat? _wisselstraatActief;

    // Tabbladen
    private int _huidigTabblad = 1;
    private int _maxTabblad = 1;

    /// <summary>Kijk-modus: een tweede, schoon venster dat de baan live toont tijdens het
    /// rijden, zonder tekengereedschap - precies zoals het echte Koploper's baanoverzicht
    /// ook als kijkscherm dient, niet alleen als tekenscherm. Alle bewerkende acties
    /// (nieuw symbool, slepen, verwijderen, standen omzetten) zijn in deze modus
    /// uitgeschakeld; de baan blijft gewoon live meekleuren via dezelfde Redraw als het
    /// normale baanontwerp-venster.</summary>
    private readonly bool _kijkModus;

    /// <summary>Wordt aangeroepen (alleen in kijk-modus) als een loc naar een blok wordt
    /// gesleept dat AFWIJKT van waar hij al staat - MainWindow hangt zich hierop af om de
    /// echte pathfinding + het starten van een rit te verzorgen (dit venster kent zelf de
    /// TreinrouteBeheerder/TreinrouteWindow-machinerie niet, dat blijft bij MainWindow om
    /// niet nog meer afhankelijkheden hierin te hoeven doorgeven).</summary>
    public Action<Trein, Blok>? LocNaarAnderBlokGesleept;

    /// <summary>Vanuit kijk-modus toegang tot de bouwschermen - dit venster kent zelf de
    /// vereiste beheerders daarvoor niet allemaal (bijv. TreinrouteBeheerder), dus net als
    /// bij LocNaarAnderBlokGesleept laat MainWindow zich hierop inhaken om het daadwerkelijk
    /// openen te verzorgen.</summary>
    public Action? BlokkenschemaGevraagd;
    public Action? BaanontwerpBewerkenGevraagd;
    public Action? TreinroutesGevraagd;

    /// <summary>Programma netjes afsluiten vanuit het kijkscherm - zodat de automatische
    /// backup ook wordt gemaakt als je (zoals de meeste eindgebruikers) alleen dit venster
    /// ooit open hebt gehad. Roept via MainWindow gewoon Close() aan, precies zoals
    /// Bestand -> Programma afsluiten dat daar al deed.</summary>
    public Action? ProgrammaAfsluitenGevraagd;

    /// <summary>De drie kijkscherm-knoppen (Go/Opstellen/Stop) - werken op ALLE treinen
    /// tegelijk, dus geen parameters nodig; MainWindow verzorgt de daadwerkelijke uitvoering
    /// (kent de TreinrouteBeheerder/TreinrouteWindow-machinerie, dit venster niet).</summary>
    public Action? AlleTreinenGaanRijdenGevraagd;
    public Action? AlleTreinenOpstellenGevraagd;
    public Action? AlleTreinenStoppenGevraagd;

    /// <summary>Gebruikersverzoek: "alle locs rijden naar het einde van hun gereserveerde
    /// rijweg en stoppen daar, dus geen noodstop maar gewoon een rustig stoppen." Losstaand
    /// van AlleTreinenStoppenGevraagd hierboven (dat is inmiddels de acute noodstop) - dit
    /// is de nette variant.</summary>
    public Action? AlleTreinenRustigStoppenGevraagd;

    /// <summary>Loc uit zijn blok halen (bijv. defect) - werkt op de in LocLijst
    /// GESELECTEERDE trein. Alleen de blok-koppeling verdwijnt, de trein zelf blijft
    /// bestaan (dus ook in LocLijst zichtbaar). MainWindow verzorgt de daadwerkelijke
    /// afhandeling (moet eventueel eerst een lopende rit stoppen, wat dit venster zelf
    /// niet kan).</summary>
    public Action<Trein>? LocVerwijderdGevraagd;

    /// <summary>Drie gerichte acties op de in LocLijst GESELECTEERDE trein - net als
    /// LocVerwijderdGevraagd hierboven kan dit venster zelf niet bij de rijdende-treinen-
    /// administratie (die zit in TreinrouteWindow), dus MainWindow verzorgt de daadwerkelijke
    /// afhandeling en geeft een statusmelding terug om hier te tonen.</summary>
    public Func<Trein, string>? LocPauzerenGevraagd;
    public Func<Trein, string>? LocHervattenGevraagd;
    /// <summary>Gebruikersverzoek: "een knop om de loc te keren zodat als hij verkeerd om
    /// rijdt ik dat kan oplossen zonder de loc van de baan te hoeven halen."</summary>
    public Func<Trein, string>? LocKerenGevraagd;
    public Func<Trein, string>? LocReserveringOpheffenGevraagd;

    private readonly DispatcherTimer? _locOverzichtTimer;
    private Point _locSleepStartpunt;

    // Statistiekgrafiek onderin het kijkscherm - bemonstert elke seconde mee met de
    // bestaande locOverzicht-timer, geen aparte timer nodig.
    private readonly List<(int InBeweging, int InBlokken)> _statistiekGeschiedenis = new();
    private const int StatistiekMaxSamples = 300; // 300 sec = 5 minuten geschiedenis
    private DateTime _daggemiddeldeDatum = DateTime.Now.Date;

    /// <summary>Welke trein de handmatige-rijden-slider OP DIT MOMENT vertegenwoordigt - zie
    /// LocLijst_SelectionChanged: voorkomt dat de elke-seconde-verversende _locOverzichtTimer
    /// (VulLocLijst, die steeds NIEUWE LocRegel-objecten aanmaakt) de slider onbedoeld
    /// terugzet naar 0 zodra dezelfde loc opnieuw "geselecteerd" wordt na elke ververing.</summary>
    private Trein? _laatsteHandmatigeSliderTrein;

    /// <summary>Zie VulLocLijst/LocLijst_SelectionChanged: staat op true GEDURENDE een
    /// verversing van de LocLijst, zodat de kortstondige "leeg"-selectie die Items.Clear()
    /// daar veroorzaakt niet als een echte deselectie door de gebruiker wordt behandeld.</summary>
    private bool _vultLocLijst;
    private double _daggemiddeldeSom;
    private int _daggemiddeldeAantalSamples;

    /// <summary>Gebruikersverzoek: "de mogelijkheid om meerdere kolommen toe te voegen en/of
    /// niet meer zichtbaar te maken, zoals actuele snelheid, treintype, treinlengte, aantal
    /// gereden uren en meters en wat je zelf denkt dat nog handig is." Elke rij: (sleutel -
    /// voor LocLijstInstellingen en de bindingnaam op LocRegel, kolomkop, breedte,
    /// standaard-zichtbaar). Sleutel == de eigenschapsnaam op LocRegel, zodat de binding
    /// hieronder (BouwLocLijstKolommen) puur op deze lijst kan vertrouwen zonder een aparte
    /// vertaaltabel te hoeven bijhouden.</summary>
    private static readonly (string Sleutel, string Header, double Breedte, bool StandaardZichtbaar)[] BeschikbareLocKolommen =
    {
        (nameof(LocRegel.DecoderTekst), "Decoder", 65, true),
        (nameof(LocRegel.Omschrijving), "Kenmerk", 140, true),
        (nameof(LocRegel.SnelheidTekst), "Snelheid", 75, true),
        (nameof(LocRegel.BlokTekst), "Blok", 55, true),
        (nameof(LocRegel.StatusTekst), "Status", 140, true),
        (nameof(LocRegel.TreintypeTekst), "Treintype", 100, false),
        (nameof(LocRegel.TreinlengteTekst), "Lengte", 65, false),
        (nameof(LocRegel.RijtijdTekst), "Rijtijd", 80, false),
        (nameof(LocRegel.AfstandTekst), "Afstand", 75, false),
        (nameof(LocRegel.DecoderStappenTekst), "Stappen", 60, false),
        (nameof(LocRegel.RangerenTekst), "Rangeren", 65, false),
        (nameof(LocRegel.MagNietKerenTekst), "Mag niet keren", 95, false),
        (nameof(LocRegel.OmgekeerdeRijrichtingTekst), "Rijr. omgekeerd", 95, false),
        (nameof(LocRegel.GeijktTekst), "IJking", 110, false),
    };

    /// <summary>De op dit moment zichtbare kolommen (sleutels uit BeschikbareLocKolommen
    /// hierboven) - geladen bij het opstarten uit LocLijstInstellingen, of de ingebouwde
    /// standaardkeuze als er nog nooit iets bewaard is.</summary>
    private List<string> _zichtbareLocKolommen = LocLijstInstellingen.Laad()
        ?? BeschikbareLocKolommen.Where(k => k.StandaardZichtbaar).Select(k => k.Sleutel).ToList();

    /// <summary>Bouwt de GridView-kolommen van LocLijst opnieuw op, op basis van
    /// _zichtbareLocKolommen - aangeroepen bij het opstarten en telkens als de gebruiker
    /// via het "Kolommen..."-scherm de zichtbare kolommen aanpast. Bewust programmatisch
    /// i.p.v. vast in de XAML: dat is de enige manier om de kolomkeuze pas TEN TIJDE VAN
    /// UITVOEREN te kunnen aanpassen.</summary>
    private void BouwLocLijstKolommen()
    {
        var gridView = new GridView();
        foreach (var kolom in BeschikbareLocKolommen)
        {
            if (!_zichtbareLocKolommen.Contains(kolom.Sleutel)) continue;
            gridView.Columns.Add(new GridViewColumn
            {
                Header = kolom.Header,
                Width = kolom.Breedte,
                DisplayMemberBinding = new System.Windows.Data.Binding(kolom.Sleutel)
            });
        }
        LocLijst.View = gridView;
    }

    private void LocKolommen_Click(object sender, RoutedEventArgs e)
    {
        var dialoog = new LocKolommenDialog(BeschikbareLocKolommen, _zichtbareLocKolommen) { Owner = this };
        if (dialoog.ShowDialog() == true)
        {
            _zichtbareLocKolommen = dialoog.GekozenSleutels;
            LocLijstInstellingen.Bewaar(_zichtbareLocKolommen);
            BouwLocLijstKolommen();
            VulLocLijst();
        }
    }

    private record LocRegel(Trein Trein, string DecoderTekst, string Omschrijving, string SnelheidTekst, string BlokTekst, string StatusTekst,
        string TreintypeTekst, string TreinlengteTekst, string RijtijdTekst, string AfstandTekst,
        string DecoderStappenTekst, string RangerenTekst, string MagNietKerenTekst, string OmgekeerdeRijrichtingTekst, string GeijktTekst);

    public BaanontwerpWindow(BlokBeheerder blokBeheerder, BaanOntwerpBeheerder baanBeheerder, WisselstraatBeheerder wisselstraatBeheerder, TreinBeheerder treinBeheerder, ActieBeheerder actieBeheerder, HardwareBeheerder hardwareBeheerder, OnderhoudsBeheerder onderhoudsBeheerder, TreinrouteBeheerder routeBeheerder, bool kijkModus = false, Func<TreinrouteWindow?>? treinrouteWindowOpvragen = null)
    {
        InitializeComponent();
        _kijkModus = kijkModus;
        if (_kijkModus)
        {
            // GEBRUIKERSVERZOEK ("kan je ook ergens in het scherm vermelden welke JSON er
            // geladen is... dan kan daar nooit twijfel over zijn"): dit kijkscherm is voor
            // de eindgebruiker vaak het ENIGE zichtbare venster tijdens het rijden - dus
            // precies waar dit het meest opvalt. Zie HuidigProject.
            Title = $"Modeltreinbesturing - Baanoverzicht (kijk-modus) — {HuidigProject.Bestandsnaam}";
            WerkbalkScrollViewer.Visibility = Visibility.Collapsed;
            LocOverzichtPaneel.Visibility = Visibility.Visible;
            BouwLocLijstKolommen();
            KijkModusMenu.Visibility = Visibility.Visible;
            AlleTreinenKnoppenBalk.Visibility = Visibility.Visible;
            SimulatiesnelheidBalk.Visibility = Visibility.Visible;
            UitrolBalk.Visibility = Visibility.Visible;
            StatistiekGrafiekCanvas.Visibility = Visibility.Visible;
            StandaardUitrolBox.Text = SimulatieInstellingen.StandaardUitrolSeconden.ToString(System.Globalization.CultureInfo.InvariantCulture);
            GeluidenIngeschakeldBox.IsChecked = SimulatieInstellingen.GeluidenIngeschakeld;
            VolumeSlider.Value = SimulatieInstellingen.GeluidsVolume * 100;
            DonkereModusBox.IsChecked = SimulatieInstellingen.DonkereModus;
            BeursModusBox.IsChecked = SimulatieInstellingen.BeursModusActief;
            _locOverzichtTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _locOverzichtTimer.Tick += (_, _) => { VulLocLijst(); BemonsterEnTekenStatistiekGrafiek(); };
            _locOverzichtTimer.Start();
            Closed += (_, _) => _locOverzichtTimer.Stop();
        }
        VensterInstellingen.Toepassen(this, _kijkModus ? "BaanoverzichtKijkWindow" : "BaanontwerpWindow");
        Closing += (_, _) => VensterInstellingen.Bewaren(this, _kijkModus ? "BaanoverzichtKijkWindow" : "BaanontwerpWindow");
        _blokBeheerder = blokBeheerder;
        _baanBeheerder = baanBeheerder;
        _wisselstraatBeheerder = wisselstraatBeheerder;
        _treinBeheerder = treinBeheerder;
        _actieBeheerder = actieBeheerder;
        _hardwareBeheerder = hardwareBeheerder;
        _onderhoudsBeheerder = onderhoudsBeheerder;
        _routeBeheerder = routeBeheerder;
        _treinrouteWindowOpvragen = treinrouteWindowOpvragen;
        _blokBeheerder.BezettingGewijzigd += Redraw;
        Closed += (_, _) => _blokBeheerder.BezettingGewijzigd -= Redraw;
        KoploperKleuren.SchemaGewijzigd += Redraw;
        Closed += (_, _) => KoploperKleuren.SchemaGewijzigd -= Redraw;
        // BUGFIX (gebruikerswaarneming: wissels updaten niet zichtbaar tijdens/na het
        // initialiseren, en het kijkscherm bleef daardoor soms een oude stand tonen zoals
        // bij wissel 5) - zie HardwareBeheerder.WisselStandGewijzigd voor de volledige
        // toelichting. Zonder dit abonnement hertekent dit venster een wissel-stand-
        // wijziging pas zodra er toevallig ook een blok-bezetting wijzigt.
        _hardwareBeheerder.WisselStandGewijzigd += Redraw;
        Closed += (_, _) => _hardwareBeheerder.WisselStandGewijzigd -= Redraw;
        // GEBRUIKERSVERZOEK ("Koploper kan een melding geven als hij kortsluiting ziet"):
        // toon/verberg de rode banner (zie XAML) zolang Dinamo's eigen foutstatus actief
        // is - Dispatcher.Invoke omdat dit event vanaf de seriële-poort-achtergrondthread
        // komt (zelfde reden als bij StatusBericht elders in de app).
        Action<bool> kortsluitingHandler = actief => Dispatcher.Invoke(() =>
            KortsluitingBanner.Visibility = actief ? Visibility.Visible : Visibility.Collapsed);
        _hardwareBeheerder.KortsluitingStatusGewijzigd += kortsluitingHandler;
        Closed += (_, _) => _hardwareBeheerder.KortsluitingStatusGewijzigd -= kortsluitingHandler;

        // Zorg dat bestaande tabbladen (bijvoorbeeld uit een geladen baanbestand) niet
        // onbereikbaar worden - het hoogste al gebruikte tabbladnummer wordt het maximum.
        int hoogsteBlok = _blokBeheerder.Blokken.Count == 0 ? 1 : _blokBeheerder.Blokken.Max(b => _baanBeheerder.TabbladVanBlok(b));
        int hoogsteSymbool = _baanBeheerder.Symbolen.Count == 0 ? 1 : _baanBeheerder.Symbolen.Max(s => s.Tabblad);
        _maxTabblad = Math.Max(1, Math.Max(hoogsteBlok, hoogsteSymbool));

        VulTabbladenBalk();
        if (_kijkModus) VulLocLijst();
        Redraw();
    }

    // --- Overzicht locomotieven (kijk-modus) ------------------------------

    private void VulLocLijst()
    {
        var geselecteerdeTrein = (LocLijst.SelectedItem as LocRegel)?.Trein;
        // Zie LocLijst_SelectionChanged: deze vlag markeert de KORTSTONDIGE "leeg"-toestand
        // die Items.Clear() hieronder veroorzaakt (SelectedItem wordt daardoor even null,
        // wat SelectionChanged laat vuren) als onderdeel van een gewone verversing, niet als
        // een echte, door de gebruiker bedoelde deselectie.
        _vultLocLijst = true;
        try
        {
            LocLijst.Items.Clear();
            var treinrouteWindow = _treinrouteWindowOpvragen?.Invoke();
            foreach (var trein in _treinBeheerder.Treinen)
            {
                // Gebruikersverzoek: "actuele snelheid" - de live decoderstap-gebaseerde
                // snelheid (zie TreinrouteWindow.HuidigeSnelheidKmU) geeft, in tegenstelling
                // tot TreinBeheerder.HuidigeSnelheid, ook TIJDENS het optrekken/afremmen de
                // echte snelheid weer, niet meteen de einddoelsnelheid van de hele rit.
                double snelheid = treinrouteWindow?.HuidigeSnelheidKmU(trein) ?? _treinBeheerder.HuidigeSnelheid(trein);
                var blok = _blokBeheerder.BlokVanLoc(trein);
                // De rijkere status (Vastgelopen/Gepauzeerd/"stopt na deze stap") komt van
                // TreinrouteWindow, als dat venster al eens geopend is - anders (nog geen
                // enkele rit ooit gestart) de simpele, altijd beschikbare basisstatus.
                string? rijstatus = trein != null ? treinrouteWindow?.TreinRijStatus(trein) : null;
                string status = rijstatus ?? (blok is null ? "Niet geplaatst" : snelheid > 0 ? "Rijdend" : "Handmatig");
                var regel = new LocRegel(
                    trein,
                    trein.DecoderAdres > 0 ? trein.DecoderAdres.ToString() : "-",
                    trein.Omschrijving,
                    snelheid > 0 ? $"{snelheid:0} km/u" : "-",
                    blok?.Nummer.ToString() ?? "-",
                    status,
                    trein.Treintype?.Omschrijving ?? "-",
                    trein.Lengte > 0 ? $"{trein.Lengte:0} cm" : "-",
                    TimeSpan.FromSeconds(trein.TotaleRijtijdSeconden).ToString(@"hh\:mm\:ss"),
                    $"{(trein.TotaleAfgelegdeAfstandCm / 100.0).ToString("0.0", CultureInfo.InvariantCulture)} m",
                    trein.DecoderStappen.ToString(),
                    trein.Rangeren ? "Ja" : "Nee",
                    trein.MagNietKeren ? "Ja" : "Nee",
                    trein.OmgekeerdeRijrichting ? "Ja" : "Nee",
                    trein.GebruikGeijkteSnelheid ? "Geijkt" : "Stappen verdeeld");
                LocLijst.Items.Add(regel);
                if (trein == geselecteerdeTrein) LocLijst.SelectedItem = regel;
            }
        }
        finally
        {
            _vultLocLijst = false;
        }
    }

    /// <summary>Bemonstert elke seconde (samen met VulLocLijst) hoeveel locs op dit moment
    /// rijden vs. geplaatst-maar-stilstaand in een blok, en tekent de statistiekgrafiek
    /// onderin het kijkscherm opnieuw. Het daggemiddelde is een lopend gemiddelde sinds
    /// middernacht van de HUIDIGE kalenderdag (reset vanzelf bij een dagwissel) - geen
    /// koppeling met de snelle klok (die zit in een ander venster/beheerder en is een
    /// gesimuleerde tijd, dit is puur de werkelijke dag).</summary>
    private void BemonsterEnTekenStatistiekGrafiek()
    {
        if (DateTime.Now.Date != _daggemiddeldeDatum)
        {
            _daggemiddeldeDatum = DateTime.Now.Date;
            _daggemiddeldeSom = 0;
            _daggemiddeldeAantalSamples = 0;
        }

        int inBeweging = 0, inBlokken = 0;
        foreach (var trein in _treinBeheerder.Treinen)
        {
            if (_blokBeheerder.BlokVanLoc(trein) is null) continue; // niet geplaatst, telt nergens in mee
            if (_treinBeheerder.HuidigeSnelheid(trein) > 0) inBeweging++;
            else inBlokken++;
        }

        _daggemiddeldeSom += inBeweging;
        _daggemiddeldeAantalSamples++;

        _statistiekGeschiedenis.Add((inBeweging, inBlokken));
        while (_statistiekGeschiedenis.Count > StatistiekMaxSamples) _statistiekGeschiedenis.RemoveAt(0);

        TekenStatistiekGrafiek();
    }

    /// <summary>Tekent de drie lijnen (wit=daggemiddelde, geel=actueel in beweging,
    /// blauw=in blokken stilstaand) op StatistiekGrafiekCanvas. Y-as schaalt automatisch
    /// mee met het totale aantal locomotieven (of 1, om delen door 0 te voorkomen als er
    /// nog geen enkele loc is aangemaakt).</summary>
    private void StatistiekGrafiekCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => TekenStatistiekGrafiek();

    private void TekenStatistiekGrafiek()
    {        StatistiekGrafiekCanvas.Children.Clear();
        double breedte = StatistiekGrafiekCanvas.ActualWidth;
        double hoogte = StatistiekGrafiekCanvas.ActualHeight;
        if (breedte <= 0 || hoogte <= 0 || _statistiekGeschiedenis.Count < 2) return;

        double maxWaarde = Math.Max(1, _treinBeheerder.Treinen.Count);
        double daggemiddelde = _daggemiddeldeAantalSamples > 0 ? _daggemiddeldeSom / _daggemiddeldeAantalSamples : 0;

        double X(int index) => _statistiekGeschiedenis.Count <= 1 ? 0 : index / (double)(_statistiekGeschiedenis.Count - 1) * breedte;
        double Y(double waarde) => hoogte - Math.Clamp(waarde / maxWaarde, 0, 1) * hoogte;

        var puntenInBeweging = new PointCollection();
        var puntenInBlokken = new PointCollection();
        for (int i = 0; i < _statistiekGeschiedenis.Count; i++)
        {
            puntenInBeweging.Add(new Point(X(i), Y(_statistiekGeschiedenis[i].InBeweging)));
            puntenInBlokken.Add(new Point(X(i), Y(_statistiekGeschiedenis[i].InBlokken)));
        }

        // Daggemiddelde is een enkele waarde (geen geschiedenis nodig) - een simpele
        // horizontale lijn over de volle breedte.
        StatistiekGrafiekCanvas.Children.Add(new Line
        {
            X1 = 0, Y1 = Y(daggemiddelde), X2 = breedte, Y2 = Y(daggemiddelde),
            Stroke = Brushes.White, StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 3, 2 }
        });
        StatistiekGrafiekCanvas.Children.Add(new Polyline { Points = puntenInBlokken, Stroke = Brushes.DeepSkyBlue, StrokeThickness = 1.5 });
        StatistiekGrafiekCanvas.Children.Add(new Polyline { Points = puntenInBeweging, Stroke = Brushes.Gold, StrokeThickness = 1.5 });
    }

    /// <summary>Handmatig rijden: vult het stappenbereik van de slider aan op basis van de
    /// geselecteerde trein (DecoderStappen, standaard 28) en toont een duidelijke uitleg
    /// als handmatig rijden voor deze regel niet mogelijk is (geen loc geselecteerd, of de
    /// loc heeft geen decoderadres ingesteld).
    ///
    /// GEVONDEN BUG (gebruikersmelding: "als ik de slider naar rechts zette ging hij
    /// vanzelf terug naar 0"): dit venster ververst in kijk-modus elke seconde de hele
    /// LocLijst (_locOverzichtTimer -> VulLocLijst), met TELKENS NIEUWE LocRegel-objecten -
    /// ook als het dezelfde trein betreft. Elke ververing "selecteert" zo'n nieuw object
    /// opnieuw, wat SelectionChanged opnieuw laat vuren, wat deze methode ONVOORWAARDELIJK
    /// de slider liet terugzetten naar 0 (en dat stuurde ook nog eens een ECHT
    /// stop-commando naar de hardware) - dus elke seconde werd elke handmatig ingestelde
    /// snelheid stilzwijgend weer teruggedraaid. Fix: alleen de slider aanpassen als het
    /// daadwerkelijk een ANDERE trein betreft dan de vorige keer (_laatsteHandmatigeSliderTrein) -
    /// bij een simpele ververing van dezelfde trein blijft de slider met rust.</summary>
    private void LocLijst_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LocLijst.SelectedItem is not LocRegel regel)
        {
            if (_vultLocLijst) return; // kortstondige "leeg"-toestand tijdens het verversen van de lijst zelf (zie VulLocLijst) - de her-selectie komt er zo aan, niets doen
            HandmatigRijdenInfoTekst.Text = "Selecteer eerst een geplaatste loc hierboven.";
            _laatsteHandmatigeSliderTrein = null;
            return;
        }
        if (regel.Trein.DecoderAdres <= 0)
        {
            HandmatigRijdenInfoTekst.Text = $"'{regel.Trein.Omschrijving}' heeft geen decoderadres ingesteld (Treinen beheren) - handmatig rijden is dan niet mogelijk.";
            _laatsteHandmatigeSliderTrein = null;
            return;
        }
        HandmatigRijdenInfoTekst.Text = _hardwareBeheerder.Huidige.Verbonden
            ? $"'{regel.Trein.Omschrijving}' (decoderadres {regel.Trein.DecoderAdres}) - schuif de stap-regelaar om te rijden."
            : $"'{regel.Trein.Omschrijving}' (decoderadres {regel.Trein.DecoderAdres}) - geen hardware verbonden, commando's gaan nergens naartoe (zie Beheren -> Hardware-interface).";
        if (regel.Trein == _laatsteHandmatigeSliderTrein) return; // zelfde loc als daarvoor, alleen de lijst is ververst - slider met rust laten
        _laatsteHandmatigeSliderTrein = regel.Trein;
        HandmatigeStapSlider.Maximum = regel.Trein.DecoderStappen > 0 ? regel.Trein.DecoderStappen : 28;
        HandmatigeStapSlider.Value = 0;
    }

    private void HandmatigeSnelheid_Changed(object sender, RoutedEventArgs e)
    {
        HandmatigeSnelheidVersturen();
    }

    private void HandmatigeSnelheid_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        HandmatigeSnelheidVersturen();
    }

    private void HandmatigeSnelheidVersturen()
    {
        if (HandmatigeStapTekst == null) return; // nog niet volledig geïnitialiseerd (constructor)
        int stap = (int)Math.Round(HandmatigeStapSlider.Value);
        HandmatigeStapTekst.Text = stap.ToString();
        if (LocLijst.SelectedItem is not LocRegel regel || regel.Trein.DecoderAdres <= 0) return;
        int blokNummer = _blokBeheerder.BlokVanLoc(regel.Trein)?.Nummer ?? 0;
        bool vooruit = VooruitBox.IsChecked == true;
        if (regel.Trein.OmgekeerdeRijrichting) vooruit = !vooruit; // zie Trein.OmgekeerdeRijrichting - compensatie voor een decoder die andersom geprogrammeerd is
        _hardwareBeheerder.StuurLocSnelheidCommando(regel.Trein.DecoderAdres, stap, vooruit, blokNummer, regel.Trein.DecoderStappen);
    }

    private void HandmatigStoppen_Click(object sender, RoutedEventArgs e)
    {
        HandmatigeStapSlider.Value = 0; // vuurt HandmatigeSnelheid_Changed vanzelf al aan (stap 0 versturen)
    }

    private void LocLijst_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _locSleepStartpunt = e.GetPosition(null);
    }

    private void LocLijst_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        if (LocLijst.SelectedItem is not LocRegel regel) return;

        var huidigPunt = e.GetPosition(null);
        if (Math.Abs(huidigPunt.X - _locSleepStartpunt.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(huidigPunt.Y - _locSleepStartpunt.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        DragDrop.DoDragDrop(LocLijst, regel.Trein, DragDropEffects.Move);
    }

    /// <summary>Loc verwijderen vanuit het kijkscherm - bewust NIET gekoppeld aan een login
    /// (net als de rest van het kijkscherm): elke eindgebruiker moet een loc kunnen
    /// weghalen, bijvoorbeeld omdat hij defect is.</summary>
    /// <summary>Loc uit zijn huidige blok halen (bijv. defect) vanuit het kijkscherm -
    /// bewust NIET gekoppeld aan een login (net als de rest van het kijkscherm): elke
    /// eindgebruiker moet dit kunnen doen. Haalt de loc alleen van de BAAN af (verwijdert
    /// de blok-koppeling) - de loc zelf blijft gewoon in deze lijst staan (nu met blok "-"
    /// en status "Niet geplaatst"), zodat je 'm later weer ergens kunt neerzetten.</summary>
    private void LocVerwijderen_Click(object sender, RoutedEventArgs e)
    {
        if (LocLijst.SelectedItem is not LocRegel regel)
        {
            StatusTekst.Text = "Selecteer eerst een loc in de lijst.";
            return;
        }
        if (_blokBeheerder.BlokVanLoc(regel.Trein) is null)
        {
            StatusTekst.Text = $"'{regel.Trein.Omschrijving}' staat nergens geplaatst.";
            return;
        }
        LocVerwijderdGevraagd?.Invoke(regel.Trein);
        StatusTekst.Text = $"Loc '{regel.Trein.Omschrijving}' uit het blok gehaald - blijft gewoon in deze lijst staan om later weer te plaatsen.";
        // VerwijderLoc zelf vuurt geen enkel event af (het is een kale Dictionary-
        // verwijdering), dus zonder deze aanroep zou het gele decodernummer op de
        // baantekening blijven staan totdat er toevallig om een andere reden opnieuw
        // getekend wordt.
        Redraw();
    }

    /// <summary>Gemeenschappelijke voorcontrole voor de drie nieuwe knoppen hieronder - ze
    /// werken alleen zinvol op een geselecteerde, GEPLAATSTE loc.</summary>
    private bool ProbeerGeselecteerdeTrein(out Trein trein)
    {
        if (LocLijst.SelectedItem is LocRegel regel && _blokBeheerder.BlokVanLoc(regel.Trein) != null)
        {
            trein = regel.Trein;
            return true;
        }
        trein = null!;
        StatusTekst.Text = "Selecteer eerst een geplaatste loc in de lijst.";
        return false;
    }

    private void LocPauzeren_Click(object sender, RoutedEventArgs e)
    {
        if (!ProbeerGeselecteerdeTrein(out var trein)) return;
        StatusTekst.Text = LocPauzerenGevraagd?.Invoke(trein) ?? "Pauzeren is nu niet beschikbaar.";
    }

    private void LocHervatten_Click(object sender, RoutedEventArgs e)
    {
        if (!ProbeerGeselecteerdeTrein(out var trein)) return;
        StatusTekst.Text = LocHervattenGevraagd?.Invoke(trein) ?? "Hervatten is nu niet beschikbaar.";
    }

    private void LocReserveringOpheffen_Click(object sender, RoutedEventArgs e)
    {
        if (!ProbeerGeselecteerdeTrein(out var trein)) return;
        StatusTekst.Text = LocReserveringOpheffenGevraagd?.Invoke(trein) ?? "Reservering opheffen is nu niet beschikbaar.";
    }

    // --- Tabbladen -------------------------------------------------------

    private void VulTabbladenBalk()
    {
        TabbladenBalk.Children.Clear();

        for (int t = 1; t <= _maxTabblad; t++)
        {
            var knop = new RadioButton
            {
                Content = $"Tabblad {t}",
                GroupName = "Tabblad",
                Margin = new Thickness(4),
                Tag = t,
                IsChecked = t == _huidigTabblad
            };
            knop.Checked += TabbladKnop_Checked;
            TabbladenBalk.Children.Add(knop);
        }

        var nieuwKnop = new Button { Content = "+ Nieuw tabblad", Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(8, 4, 4, 4) };
        nieuwKnop.Click += NieuwTabblad_Click;
        TabbladenBalk.Children.Add(nieuwKnop);
    }

    private void TabbladKnop_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: int tabbladNummer })
        {
            _huidigTabblad = tabbladNummer;
            Redraw();
        }
    }

    private void NieuwTabblad_Click(object sender, RoutedEventArgs e)
    {
        _maxTabblad++;
        _huidigTabblad = _maxTabblad;
        VulTabbladenBalk();
        Redraw();
    }

    // --- Tool selectie -------------------------------------------------

    private void Tool_Checked(object sender, RoutedEventArgs e)
    {
        RondLijnInAanlegAf();
        _koppelGeselecteerdBlok = null;
        _wisselstraatVanKandidaat = null;
        _wisselstraatActief = null;

        _huidigTool = sender switch
        {
            var s when s == ToolLijn => Tool.Lijn,
            var s when s == ToolWissel => Tool.Wissel,
            var s when s == ToolSein => Tool.Sein,
            var s when s == ToolStootblok => Tool.Stootblok,
            var s when s == ToolPerron => Tool.Perron,
            var s when s == ToolTekst => Tool.Tekst,
            var s when s == ToolSchakelaar => Tool.Schakelaar,
            var s when s == ToolPijl => Tool.Pijl,
            var s when s == ToolOntkoppelrail => Tool.Ontkoppelrail,
            var s when s == ToolBezetmelder => Tool.Bezetmelder,
            var s when s == ToolOverloopwissel => Tool.Overloopwissel,
            var s when s == ToolDriewegwissel => Tool.Driewegwissel,
            var s when s == ToolKruiswissel => Tool.Kruiswissel,
            var s when s == ToolEngelseWissel => Tool.EngelseWissel,
            var s when s == ToolKoppelen => Tool.Koppelen,
            var s when s == ToolWisselstraat => Tool.Wisselstraat,
            _ => Tool.Verplaatsen
        };

        // StatusTekst bestaat nog niet als deze Checked-event afgaat tijdens het
        // opbouwen van het venster zelf (de eerste RadioButton begint als "aangevinkt").
        if (StatusTekst is null) return;

        StatusTekst.Text = _huidigTool switch
        {
            Tool.Verplaatsen => "Sleep een los eindpunt van een lijn om 'm te verankeren/verplaatsen, of sleep een heel blok/symbool (dus ook een sein, om 'm vrij te positioneren). Klik + Delete verwijdert een symbool (blokken verwijder je in het blokkenschema-venster). Ctrl+D dupliceert het geselecteerde symbool. Dubbelklik een blok: eigenschappen (omschrijving, type, bezetmeldpunten, ...). Shift+dubbelklik een blok: naar volgend tabblad (bij meerdere tabbladen). Dubbelklik een wissel: stand omzetten (rechtdoor/afbuigend). Shift+dubbelklik een wissel: adres instellen. Rechtsklik een wissel: richting 45° draaien. Shift+rechtsklik een wissel: afbuiging spiegelen. Ctrl+rechtsklik een wissel: 'altijd initialiseren' aan/uit (rode ring = aan; stuurt de stand bij het verbinden met hardware altijd opnieuw). Alt+rechtsklik een wissel: defect zetten/herstellen (rode X = defect; routes die deze wissel nodig hebben starten dan niet). Rechtsklik een pijl: 45° draaien. Rechtsklik een sein: 45° draaien. Dubbelklik een bezetmelder: meldernummer instellen (ter documentatie). Dubbelklik een driewegwissel: stand cyclen (links/rechtdoor/rechts). Shift+dubbelklik een driewegwissel: adres instellen. Rechtsklik een driewegwissel: richting 45° draaien. Rechtsklik een kruiswissel: 45° draaien. Dubbelklik een Engelse wissel: overstap-stand omzetten. Shift+dubbelklik een Engelse wissel: adres instellen. Ctrl+rechtsklik een Engelse wissel: 'altijd initialiseren' aan/uit. Dubbelklik een perron: omschrijving instellen. Shift+dubbelklik een perron: breedte/hoogte instellen (net als Koploper's eigen 'hoekpunten' voor het perron). Dubbelklik een schakelaar: aan/uit.",
            Tool.Lijn => "Klik om een kort lijnstukje neer te zetten. Ga daarna naar Verplaatsen en sleep de losse eindpunten naar waar ze moeten komen - ze klappen vast op nabije ankerpunten (blok/wissel/andere lijn) of anders op 45°.",
            Tool.Koppelen => "Klik eerst een blok aan, klik daarna lijnen of seinen om ze te koppelen/loskoppelen (lijn kleurt oranje; sein volgt daarna automatisch de bezetting).",
            Tool.Tekst => "Klik om een tekstsymbool te plaatsen (naam later aan te passen).",
            Tool.Wisselstraat => "Klik het van-blok, klik daarna het naar-blok (moet een bestaande relatie zijn). Klik daarna wissels/lijnen aan om ze toe te voegen (kleuren paars). Escape rondt af.",
            Tool.Overloopwissel => "Klik de eerste wissel, klik daarna de tweede - ze worden gekoppeld (paarse stippellijn) en zetten voortaan altijd samen om, zoals een echte overloopwissel tussen twee parallelle sporen. Dezelfde wissel nogmaals aanklikken heft de koppeling weer op.",
            _ => $"Klik om een {ToolNaam(_huidigTool)} te plaatsen."
        };
        Redraw();
    }

    private static string ToolNaam(Tool t) => t switch
    {
        Tool.Wissel => "wissel",
        Tool.Sein => "sein",
        Tool.Stootblok => "stootblok",
        Tool.Perron => "perron",
        _ => t.ToString().ToLower()
    };

    private void Sluiten_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>Eigen instantie hier (niet gedeeld met MainWindow) - simpelste aanpak, een
    /// eventueel dubbel Help-venster als je het vanuit meerdere schermen tegelijk opent is
    /// een acceptabele kleine imperfectie t.o.v. de complexiteit van een venster-overstijgend
    /// gedeeld exemplaar.</summary>
    private void Gebruiksaanwijzing_Click(object sender, RoutedEventArgs e)
    {
        new HelpWindow { Owner = this }.Show();
    }

    private void OpenBlokkenschema_Click(object sender, RoutedEventArgs e) => BlokkenschemaGevraagd?.Invoke();

    private void OpenBaanontwerpBewerken_Click(object sender, RoutedEventArgs e) => BaanontwerpBewerkenGevraagd?.Invoke();

    private void OpenTreinroutesVanuitKijkscherm_Click(object sender, RoutedEventArgs e) => TreinroutesGevraagd?.Invoke();

    private void ProgrammaAfsluiten_Click(object sender, RoutedEventArgs e) => ProgrammaAfsluitenGevraagd?.Invoke();

    private void OnderhoudVanuitKijkscherm_Click(object sender, RoutedEventArgs e)
    {
        var dialoog = new OnderhoudsDialog(_onderhoudsBeheerder) { Owner = this };
        dialoog.ShowDialog();
    }

    private void DashboardVanuitKijkscherm_Click(object sender, RoutedEventArgs e)
    {
        var dialoog = new DashboardDialog(_blokBeheerder, _baanBeheerder, _treinBeheerder, _routeBeheerder, _onderhoudsBeheerder, null) { Owner = this };
        dialoog.ShowDialog();
    }

    private void OverProgrammaVanuitKijkscherm_Click(object sender, RoutedEventArgs e)
    {
        var dialoog = new AboutDialog { Owner = this };
        dialoog.ShowDialog();
    }

    private void AlleTreinenGaanRijden_Click(object sender, RoutedEventArgs e) => AlleTreinenGaanRijdenGevraagd?.Invoke();

    private void AlleTreinenOpstellen_Click(object sender, RoutedEventArgs e) => AlleTreinenOpstellenGevraagd?.Invoke();

    private void AlleTreinenStoppen_Click(object sender, RoutedEventArgs e) => AlleTreinenStoppenGevraagd?.Invoke();
    private void AlleTreinenRustigStoppen_Click(object sender, RoutedEventArgs e) => AlleTreinenRustigStoppenGevraagd?.Invoke();

    /// <summary>Simulatiesnelheid aanpassen - werkt direct door, ook terwijl er al treinen
    /// rijden (de nieuwe factor geldt vanaf de eerstvolgende timer die gestart wordt; een
    /// al lopende wachttijd loopt nog op de oude snelheid af). Geen login nodig, net als de
    /// rest van het kijkscherm.</summary>
    private void Simulatiesnelheid_Changed(object sender, RoutedEventArgs e)
    {
        SimulatieInstellingen.VertragingsFactor = sender switch
        {
            _ when sender == SnelheidSnelBox => 0.5,
            _ when sender == SnelheidLangzaamBox => 2.0,
            _ when sender == SnelheidZeerLangzaamBox => 4.0,
            _ => 1.0
        };
        StatusTekst.Text = $"Simulatiesnelheid: {(string)((RadioButton)sender).Content}.";
    }

    /// <summary>Standaard uitroltijd instellen (database-brede instelling, zie
    /// SimulatieInstellingen.StandaardUitrolSeconden) - een individueel blok kan dit
    /// overschrijven via zijn eigen blokeigenschappen.</summary>
    private void StandaardUitrolZetten_Click(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(StandaardUitrolBox.Text, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double seconden) || seconden < 0)
        {
            StatusTekst.Text = "Vul een geldige uitroltijd in (0 of hoger).";
            return;
        }
        SimulatieInstellingen.StandaardUitrolSeconden = seconden;
        StatusTekst.Text = $"Standaard uitroltijd bij stoppen: {seconden} sec.";
    }

    /// <summary>Koploper: "alle geluiden en rookgeneratoren van treinen in- of
    /// uitschakelen" - schakelt de .wav-geluiden van loc-functies globaal aan/uit.</summary>
    private void GeluidenToggle_Changed(object sender, RoutedEventArgs e)
    {
        SimulatieInstellingen.GeluidenIngeschakeld = GeluidenIngeschakeldBox.IsChecked == true;
        StatusTekst.Text = SimulatieInstellingen.GeluidenIngeschakeld ? "Geluiden ingeschakeld." : "Geluiden uitgeschakeld.";
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (VolumeSlider == null) return; // nog niet volledig geïnitialiseerd (constructor)
        SimulatieInstellingen.GeluidsVolume = VolumeSlider.Value / 100.0;
    }

    private void DonkereModusToggle_Changed(object sender, RoutedEventArgs e)
    {
        SimulatieInstellingen.DonkereModus = DonkereModusBox.IsChecked == true;
        Redraw();
    }

    private void BeursModusToggle_Changed(object sender, RoutedEventArgs e)
    {
        SimulatieInstellingen.BeursModusActief = BeursModusBox.IsChecked == true;
        StatusTekst.Text = SimulatieInstellingen.BeursModusActief
            ? "Beursmodus AAN - stilstaande locs krijgen elke 15 seconden automatisch een nieuwe rit."
            : "Beursmodus uit.";
    }

    /// <summary>Loc plaatsen vanuit het BAANOVERZICHT zelf - zoals het echte Koploper: "Het is
    /// ook mogelijk het plaatje van de locomotief in het 'Overzicht locomotieven' te slepen
    /// naar het baanoverzicht." Wij hebben (nog) geen sleep-vanuit-lijst, maar wel dezelfde
    /// klik-en-kies-aanpak als MainWindow, nu ook hier - werkte eerder alleen in het
    /// blokkenschema, wat een andere plek is dan waar het echte Koploper dit laat gebeuren.
    /// Werkt op het laatst aangeklikte blok (_gesleeptBlok, gezet zodra je een blok
    /// aanklikt/verplaatst in de Verplaatsen-tool).</summary>
    private void LocPlaatsen_Click(object sender, RoutedEventArgs e)
    {
        if (_gesleeptBlok is null)
        {
            MessageBox.Show(this, "Klik eerst een blok aan (in de Verplaatsen-tool).", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (_treinBeheerder.Treinen.Count == 0)
        {
            MessageBox.Show(this, "Er zijn nog geen treinen om te plaatsen - maak er eerst een aan via 'Treinen beheren' (in het blokkenschema-venster).", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dialoog = new LocKiezenDialog(_treinBeheerder, _gesleeptBlok) { Owner = this };
        if (dialoog.ShowDialog() == true && dialoog.GekozenTrein != null)
        {
            _blokBeheerder.PlaatsLoc(_gesleeptBlok, dialoog.GekozenTrein);
            // Markeert het blok als handmatig bezet, zodat een daaropvolgende ECHTE
            // bezetmelding van de hardware niet als spookmelding wordt gezien (zie
            // BaanCanvas_Drop hieronder voor de volledige toelichting - dit is hetzelfde
            // gat, maar dan voor het plaatsen via de "Loc plaatsen"-knop i.p.v. slepen).
            _blokBeheerder.ZetHandmatigBezet(_gesleeptBlok, true);
            Redraw();
        }
    }

    private void LocWeghalen_Click(object sender, RoutedEventArgs e)
    {
        if (_gesleeptBlok is null)
        {
            MessageBox.Show(this, "Klik eerst een blok aan (in de Verplaatsen-tool).", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _blokBeheerder.VerwijderLoc(_gesleeptBlok);
        _blokBeheerder.ZetHandmatigBezet(_gesleeptBlok, false); // zie LocPlaatsen_Click - symmetrisch weer opheffen
        Redraw();
    }

    private void GaNaarBlok_Click(object sender, RoutedEventArgs e) => GaNaarBlok();

    private void GaNaarBlokBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) GaNaarBlok();
    }

    /// <summary>Zelfde functie als in MainWindow, maar dan voor het baanontwerp: schakelt
    /// eerst naar het juiste tabblad als het blok daar niet op staat, en scrolt dan naar
    /// de daadwerkelijke positie in het baanontwerp (BaanCanvas, niet het blokkenschema).</summary>
    private void GaNaarBlok()
    {
        if (!int.TryParse(GaNaarBlokBox.Text, out int nummer))
        {
            StatusTekst.Text = "Vul een geldig bloknummer in.";
            return;
        }
        var blok = _blokBeheerder.Blokken.FirstOrDefault(b => b.Nummer == nummer);
        if (blok is null)
        {
            StatusTekst.Text = $"Blok {nummer} bestaat niet.";
            return;
        }

        int tabblad = _baanBeheerder.TabbladVanBlok(blok);
        if (tabblad != _huidigTabblad)
        {
            _huidigTabblad = tabblad;
            VulTabbladenBalk();
        }

        var positie = _baanBeheerder.PositieVanBlok(blok);
        double doelX = Math.Max(0, positie.X + BlokGrootte / 2 - BaanScrollViewer.ViewportWidth / 2);
        double doelY = Math.Max(0, positie.Y + BlokGrootte / 2 - BaanScrollViewer.ViewportHeight / 2);
        BaanScrollViewer.ScrollToHorizontalOffset(doelX);
        BaanScrollViewer.ScrollToVerticalOffset(doelY);
        StatusTekst.Text = $"Gesprongen naar blok {nummer}.";
        Redraw();
    }

    /// <summary>Springt naar een willekeurige positie op het canvas van het gegeven
    /// tabblad - hulpmethode achter Zoeken(), maar ook los bruikbaar.</summary>
    private void SpringNaarPositie(int tabblad, Point positie)
    {
        if (tabblad != _huidigTabblad)
        {
            _huidigTabblad = tabblad;
            VulTabbladenBalk();
        }
        double doelX = Math.Max(0, positie.X - BaanScrollViewer.ViewportWidth / 2);
        double doelY = Math.Max(0, positie.Y - BaanScrollViewer.ViewportHeight / 2);
        BaanScrollViewer.ScrollToHorizontalOffset(doelX);
        BaanScrollViewer.ScrollToVerticalOffset(doelY);
        Redraw();
    }

    private void Zoeken_Click(object sender, RoutedEventArgs e) => Zoeken();

    private void ZoekBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Zoeken();
    }

    /// <summary>Brede zoekfunctie over blokken/wissels/seinen/treinen - handig bij een
    /// grote baan met veel objecten. Volgorde bij een numerieke invoer: eerst een
    /// bloknummer-match (meest gebruikte geval), dan wissel-adres, dan sein-adres. Bij
    /// niet-numerieke invoer: een (deel van een) locomotief-omschrijving.</summary>
    private void Zoeken()
    {
        string zoekterm = ZoekBox.Text.Trim();
        if (string.IsNullOrEmpty(zoekterm))
        {
            StatusTekst.Text = "Vul iets in om te zoeken.";
            return;
        }

        if (int.TryParse(zoekterm, out int nummer))
        {
            var blok = _blokBeheerder.Blokken.FirstOrDefault(b => b.Nummer == nummer);
            if (blok != null)
            {
                SpringNaarPositie(_baanBeheerder.TabbladVanBlok(blok), new Point(_baanBeheerder.PositieVanBlok(blok).X + BlokGrootte / 2, _baanBeheerder.PositieVanBlok(blok).Y + BlokGrootte / 2));
                StatusTekst.Text = $"Gevonden: blok {nummer}.";
                return;
            }
            var wissel = _baanBeheerder.Symbolen.OfType<Wissel>().FirstOrDefault(w => w.Adres == nummer);
            if (wissel != null)
            {
                SpringNaarPositie(wissel.Tabblad, new Point(wissel.X, wissel.Y));
                StatusTekst.Text = $"Gevonden: wissel {nummer}.";
                return;
            }
            var sein = _baanBeheerder.Symbolen.OfType<Sein>().FirstOrDefault(s => s.Adres == nummer);
            if (sein != null)
            {
                SpringNaarPositie(sein.Tabblad, new Point(sein.X, sein.Y));
                StatusTekst.Text = $"Gevonden: sein {nummer}.";
                return;
            }
        }

        // Geen (of geen numerieke) match tot nu toe: probeer een loc op naam/decoderadres.
        var trein = _treinBeheerder.Treinen.FirstOrDefault(t =>
            t.Omschrijving.Contains(zoekterm, StringComparison.OrdinalIgnoreCase) ||
            (int.TryParse(zoekterm, out int decoderAdres) && t.DecoderAdres == decoderAdres));
        if (trein != null)
        {
            var blokVanTrein = _blokBeheerder.BlokVanLoc(trein);
            if (blokVanTrein != null)
            {
                SpringNaarPositie(_baanBeheerder.TabbladVanBlok(blokVanTrein), new Point(_baanBeheerder.PositieVanBlok(blokVanTrein).X + BlokGrootte / 2, _baanBeheerder.PositieVanBlok(blokVanTrein).Y + BlokGrootte / 2));
                StatusTekst.Text = $"Gevonden: loc '{trein.Omschrijving}', staat op blok {blokVanTrein.Nummer}.";
            }
            else
            {
                StatusTekst.Text = $"Loc '{trein.Omschrijving}' gevonden, maar staat nergens geplaatst.";
            }
            return;
        }

        StatusTekst.Text = $"Niets gevonden voor '{zoekterm}' (geprobeerd: bloknummer, wissel-adres, sein-adres, loc-naam/decoderadres).";
    }

    private void ToonVrijeOpstelsporen_Click(object sender, RoutedEventArgs e) => ToonVrijeOpstelsporen();

    /// <summary>Handig bij het wegzetten van meerdere locs aan het eind van een sessie -
    /// een blok telt hier als "vrij" als het niet bezet, gereserveerd óf vergrendeld is
    /// (een vergrendeld opstelspoor is wel degelijk fysiek leeg, maar bewust buiten gebruik
    /// gezet, dus niet zinvol om als beschikbaar te tonen).</summary>
    private void ToonVrijeOpstelsporen()
    {
        var vrijeOpstelsporen = _blokBeheerder.Blokken
            .Where(b => b.Type == BlokType.Opstelspoor && !_blokBeheerder.IsBezet(b) && !_blokBeheerder.IsGereserveerd(b) && !b.Vergrendeld)
            .OrderBy(b => b.Nummer)
            .ToList();

        if (vrijeOpstelsporen.Count == 0)
        {
            StatusTekst.Text = _blokBeheerder.Blokken.Any(b => b.Type == BlokType.Opstelspoor)
                ? "Geen enkel opstelspoor is op dit moment vrij."
                : "Er zijn nog geen blokken van het type 'Opstelspoor' aangemaakt.";
            return;
        }

        if (vrijeOpstelsporen.Count == 1)
        {
            var blok = vrijeOpstelsporen[0];
            SpringNaarPositie(_baanBeheerder.TabbladVanBlok(blok), new Point(_baanBeheerder.PositieVanBlok(blok).X + BlokGrootte / 2, _baanBeheerder.PositieVanBlok(blok).Y + BlokGrootte / 2));
            StatusTekst.Text = $"Eén vrij opstelspoor gevonden: blok {blok.Nummer}.";
            return;
        }

        StatusTekst.Text = $"{vrijeOpstelsporen.Count} vrije opstelsporen: blok " + string.Join(", ", vrijeOpstelsporen.Select(b => b.Nummer)) + ".";
    }

    private void WisselstratenBeheren_Click(object sender, RoutedEventArgs e)
    {
        var dialoog = new WisselstratenDialog(_wisselstraatBeheerder, Redraw) { Owner = this };
        dialoog.ShowDialog();
    }

    private void WisselTest_Click(object sender, RoutedEventArgs e)
    {
        var dialoog = new WisselTestDialog(_baanBeheerder, _hardwareBeheerder, Redraw) { Owner = this };
        dialoog.ShowDialog();
    }

    private void SeinenBeheren_Click(object sender, RoutedEventArgs e)
    {
        var dialoog = new SeinenDialog(_blokBeheerder, _baanBeheerder, _wisselstraatBeheerder, Redraw) { Owner = this };
        dialoog.ShowDialog();
    }

    private void Exporteren_Click(object sender, RoutedEventArgs e)
    {
        var dialoog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "PNG-afbeelding (*.png)|*.png",
            FileName = $"baanontwerp-tabblad{_huidigTabblad}.png"
        };
        if (dialoog.ShowDialog() != true) return;

        try
        {
            double breedte = Math.Max(BaanCanvas.ActualWidth, 800);
            double hoogte = Math.Max(BaanCanvas.ActualHeight, 600);

            var bitmap = new RenderTargetBitmap((int)breedte, (int)hoogte, 96, 96, PixelFormats.Pbgra32);
            var achtergrond = new Rectangle { Width = breedte, Height = hoogte, Fill = Brushes.White };
            achtergrond.Measure(new Size(breedte, hoogte));
            achtergrond.Arrange(new Rect(0, 0, breedte, hoogte));
            bitmap.Render(achtergrond);
            bitmap.Render(BaanCanvas);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = System.IO.File.Create(dialoog.FileName))
                encoder.Save(stream);

            StatusTekst.Text = $"Baanontwerp geëxporteerd naar {dialoog.FileName}.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Exporteren mislukt: {ex.Message}", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // --- Muis-interactie -------------------------------------------------

    private void BaanCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_kijkModus)
        {
            var kijkPositie = e.GetPosition(BaanCanvas);

            // Ctrl+sleep = een geplaatste loc naar een ander blok verslepen, precies zoals
            // het al bestaande Ctrl+sleep-gebaar in het blokkenschema - maar dat bestond
            // alleen daar; hier de kijkscherm-eigen variant. Begint een sleep als er Ctrl
            // wordt ingedrukt boven een blok MET een loc erop.
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && BlokOpPositie(kijkPositie) is Blok kijkBlokMetLoc && _blokBeheerder.LocOpBlok(kijkBlokMetLoc) != null)
            {
                _kijkGesleeptLocBlok = kijkBlokMetLoc;
                BaanCanvas.CaptureMouse();
                StatusTekst.Text = $"Loc van blok {kijkBlokMetLoc.Nummer} vasthouden en naar het doelblok slepen, dan loslaten.";
                return;
            }

            // De andere toegestane bewerking in kijk-modus: een wissel handmatig omzetten
            // door er gewoon op te klikken (geen dubbelklik nodig zoals in het gewone
            // baanontwerp - dit is het operationele kijkscherm, snel schakelen moet kunnen).
            // Een defecte wissel kan bewust niet omgezet worden.
            if (SymboolOpPositie(kijkPositie) is Wissel kijkWissel)
            {
                if (kijkWissel.IsDefect)
                {
                    StatusTekst.Text = $"Wissel {kijkWissel.Adres} is defect en kan niet handmatig omgezet worden.";
                    return;
                }
                kijkWissel.Stand = kijkWissel.Stand == WisselStand.Rechtdoor ? WisselStand.Afbuigend : WisselStand.Rechtdoor;
                if (kijkWissel.GekoppeldeOverloopwissel != null)
                    kijkWissel.GekoppeldeOverloopwissel.Stand = kijkWissel.Stand;
                // GEVONDEN GAT (gebruikerswaarneming: "ik klik op de wissel maar er gebeurt
                // fysiek niets" - dit bleek het KIJKSCHERM te zijn, een ANDER code-pad dan
                // het bouwscherm-dubbelklik dat eerder al gerepareerd werd): precies
                // hetzelfde gat, nu hier ook gedicht - stuur het echte hardwarecommando mee.
                bool afbuigendKijkWissel = kijkWissel.Stand == WisselStand.Afbuigend;
                _hardwareBeheerder.StuurWisselCommando(kijkWissel.Adres, kijkWissel.OmgekeerdePolariteit ? !afbuigendKijkWissel : afbuigendKijkWissel);
                if (kijkWissel.GekoppeldeOverloopwissel != null)
                {
                    var kijkOverloop = kijkWissel.GekoppeldeOverloopwissel;
                    _hardwareBeheerder.StuurWisselCommando(kijkOverloop.Adres, kijkOverloop.OmgekeerdePolariteit ? !afbuigendKijkWissel : afbuigendKijkWissel);
                }
                StatusTekst.Text = $"Wissel {kijkWissel.Adres} handmatig gezet op: {kijkWissel.Stand}.";
                Redraw();
                return;
            }

            // GEBRUIKERSVERZOEK (uitbreiding op de kijkscherm-wisselfix hierboven): een
            // driewegwissel had in het kijkscherm nog HELEMAAL GEEN klik-optie - alleen het
            // bouwscherm (via dubbelklik). Zie de toelichting bij het bouwscherm-dubbelklik
            // hierboven voor de beperking (Links/Rechts niet van elkaar te onderscheiden
            // met dit ene adres).
            if (SymboolOpPositie(kijkPositie) is Driewegwissel kijkDriewegwissel)
            {
                kijkDriewegwissel.Stand = kijkDriewegwissel.Stand switch
                {
                    DriewegwisselStand.Links => DriewegwisselStand.Rechtdoor,
                    DriewegwisselStand.Rechtdoor => DriewegwisselStand.Rechts,
                    _ => DriewegwisselStand.Links
                };
                _hardwareBeheerder.StuurWisselCommando(kijkDriewegwissel.Adres, kijkDriewegwissel.Stand != DriewegwisselStand.Rechtdoor);
                StatusTekst.Text = $"Driewegwissel {kijkDriewegwissel.Adres} handmatig gezet op: {kijkDriewegwissel.Stand}.";
                Redraw();
                return;
            }

            // GEBRUIKERSVERZOEK (uitbreiding op de kijkscherm-wisselfix hierboven): een
            // kruiswissel/Engelse wissel had in het kijkscherm nog HELEMAAL GEEN klik-optie
            // - alleen het bouwscherm kon 'm (via dubbelklik) omzetten. Zelfde principe als
            // een gewone wissel: één klik, geen dubbelklik nodig, met een echt
            // hardwarecommando erbij.
            if (SymboolOpPositie(kijkPositie) is Kruiswissel kijkKruiswissel)
            {
                if (!kijkKruiswissel.IsEngels)
                {
                    StatusTekst.Text = "Dit is een gewone (passieve) kruiswissel - geen stand om te wijzigen.";
                    return;
                }
                // GEBRUIKERSCORRECTIE (zie Model.Kruiswissel): de twee motoren van een Engelse
                // wissel hebben GEEN vaste onderlinge relatie - elke rijweg heeft zijn eigen,
                // soms gemengde combinatie nodig. Voor een simpele handmatige testklik in het
                // kijkscherm is er geen rijweg bekend, dus toggelen we hier beide motoren
                // gezamenlijk (eenvoudige symmetrische toggle, prima voor handmatig testen);
                // voor een specifieke rijweg legt de gebruiker de exacte combinatie vast via
                // een Wisselstraat (zie WisselstraatBeheerder.ToggleKruiswissel).
                bool afbuigendKijkKruiswissel = kijkKruiswissel.StandAdres != WisselStand.Afbuigend;
                kijkKruiswissel.StandAdres = afbuigendKijkKruiswissel ? WisselStand.Afbuigend : WisselStand.Rechtdoor;
                kijkKruiswissel.StandAdres2 = afbuigendKijkKruiswissel ? WisselStand.Afbuigend : WisselStand.Rechtdoor;
                kijkKruiswissel.Stand = afbuigendKijkKruiswissel ? KruiswisselStand.Overstap : KruiswisselStand.Rechtdoor;
                if (kijkKruiswissel.Adres > 0) _hardwareBeheerder.StuurWisselCommando(kijkKruiswissel.Adres, afbuigendKijkKruiswissel);
                if (kijkKruiswissel.Adres2 > 0) _hardwareBeheerder.StuurWisselCommando(kijkKruiswissel.Adres2, afbuigendKijkKruiswissel);
                StatusTekst.Text = $"Engelse wissel handmatig gezet op: motor {kijkKruiswissel.Adres}={kijkKruiswissel.StandAdres}, motor {kijkKruiswissel.Adres2}={kijkKruiswissel.StandAdres2}.";
                Redraw();
                return;
            }

            // GEVONDEN GAT (gebruikersmelding: paarse blokken die "niet zo horen te zijn" -
            // een Foutmelding, bijv. door een spookmelding, blijft bewust blinken totdat de
            // gebruiker 'm zelf opheft, zie MainWindow.Bezetmelding_VanHardware): dat opheffen
            // kon voorheen ALLEEN via het bouwscherm (blok selecteren + Foutmelding-knop),
            // waar je voor moet inloggen als beheerder - de eindgebruiker die puur het
            // kijkscherm gebruikt had geen manier om een gecontroleerde anomalie weg te
            // klikken. Nu, net als bij een wissel hierboven, gewoon een klik op het
            // (knipperende) blok zelf.
            if (BlokOpPositie(kijkPositie) is Blok kijkFoutBlok && _blokBeheerder.IsFoutmelding(kijkFoutBlok))
            {
                _blokBeheerder.ZetFoutmelding(kijkFoutBlok, false);
                StatusTekst.Text = $"Foutmelding op blok {kijkFoutBlok.Nummer} opgeheven.";
                Redraw();
                return;
            }

            // Gebruikersverzoek: "als ik op het blok klik waar de trein staat dat ik dan de
            // mogelijkheid krijg om deze ene trein tijdelijk te stoppen, en eventueel later
            // weer te starten, of als hij defect blijkt uit het blok te halen." Hergebruikt
            // bewust de AL BESTAANDE Pauzeren/Hervatten/Reservering-opheffen-mechanismen
            // hieronder (LocPauzerenGevraagd e.d., tot nu toe alleen bereikbaar via de
            // knoppen bij een in LocLijst geselecteerde loc) - nu ook direct vanaf het blok
            // zelf, zonder eerst in de lijst te hoeven zoeken. Alleen als GEEN van de
            // bovenstaande, specifiekere kliks (wissel, foutmelding) van toepassing was.
            if (BlokOpPositie(kijkPositie) is Blok kijkTreinBlok && _blokBeheerder.LocOpBlok(kijkTreinBlok) is Trein kijkTrein)
            {
                var actieDialoog = new TreinActieDialog(kijkTrein,
                    pauzeer: () => LocPauzerenGevraagd?.Invoke(kijkTrein) ?? "Pauzeren is nu niet beschikbaar.",
                    hervat: () => LocHervattenGevraagd?.Invoke(kijkTrein) ?? "Hervatten is nu niet beschikbaar.",
                    keer: () => { var r = LocKerenGevraagd?.Invoke(kijkTrein) ?? "Keren is nu niet beschikbaar."; Redraw(); return r; },
                    reserveringOpheffen: () => LocReserveringOpheffenGevraagd?.Invoke(kijkTrein) ?? "Reservering opheffen is nu niet beschikbaar.",
                    verwijderen: () => { LocVerwijderdGevraagd?.Invoke(kijkTrein); Redraw(); })
                { Owner = this };
                actieDialoog.ShowDialog();
                return;
            }
            return;
        }

        var positie = e.GetPosition(BaanCanvas);

        if (e.ClickCount == 2 && SymboolOpPositie(positie) is Wissel dubbelklikWissel)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                string? nieuwAdres = InputDialog.Vraag(this, "Wissel eigenschappen", "Adres (decodernummer):", dubbelklikWissel.Adres.ToString());
                if (nieuwAdres != null)
                {
                    if (int.TryParse(nieuwAdres, out int adres))
                    {
                        dubbelklikWissel.Adres = adres;
                        StatusTekst.Text = $"Wissel adres gezet op {adres}.";
                    }
                    else
                    {
                        StatusTekst.Text = "Ongeldig adres — vul een getal in.";
                    }
                }
                // GEVONDEN, FYSIEK GAT (gebruikerswaarneming: "wissel 5 staat op het scherm
                // afbuigend, wat niet juist is, maar fysiek staat hij terecht rechtdoor") -
                // zie Model.Wissel.OmgekeerdePolariteit: exact hetzelfde principe als bij een
                // Engelse wissel se tweede motor (Adres2OmgekeerdePolariteit hieronder), maar
                // dan voor een gewone wissel met precies deze ene, fysiek omgekeerd bedrade
                // motor. Zelfde Shift+dubbelklik-eigenschappenschermpje als bij een adres.
                string? omgekeerdWissel = InputDialog.Vraag(this, "Wissel eigenschappen", "Staat deze wisselmotor fysiek omgekeerd bedraad? (j/n):", dubbelklikWissel.OmgekeerdePolariteit ? "j" : "n");
                if (omgekeerdWissel != null)
                    dubbelklikWissel.OmgekeerdePolariteit = omgekeerdWissel.Trim().ToLowerInvariant().StartsWith("j");
                Redraw();
                return;
            }

            dubbelklikWissel.Stand = dubbelklikWissel.Stand == WisselStand.Rechtdoor ? WisselStand.Afbuigend : WisselStand.Rechtdoor;
            // Overloopwissel: de gekoppelde partner altijd meebewegen, precies zoals de
            // fysieke koppeling van twee echte wissels dat zou doen.
            if (dubbelklikWissel.GekoppeldeOverloopwissel != null)
                dubbelklikWissel.GekoppeldeOverloopwissel.Stand = dubbelklikWissel.Stand;
            // GEVONDEN GAT (gebruikerswaarneming: "als ik de wissel op het scherm omzet gaat
            // hij fysiek niet om") - dubbelklikken paste tot nu toe ALLEEN de interne Stand
            // aan, zonder ooit een echt hardwarecommando te sturen. Voor handmatig testen
            // (precies waar de gebruiker dit voor gebruikte) is dat onbruikbaar - stuur er
            // dus nu ook daadwerkelijk het bijbehorende commando naartoe.
            bool afbuigendVoorHardware = dubbelklikWissel.Stand == WisselStand.Afbuigend;
            _hardwareBeheerder.StuurWisselCommando(dubbelklikWissel.Adres, dubbelklikWissel.OmgekeerdePolariteit ? !afbuigendVoorHardware : afbuigendVoorHardware);
            if (dubbelklikWissel.GekoppeldeOverloopwissel != null)
            {
                var dubbelklikOverloop = dubbelklikWissel.GekoppeldeOverloopwissel;
                _hardwareBeheerder.StuurWisselCommando(dubbelklikOverloop.Adres, dubbelklikOverloop.OmgekeerdePolariteit ? !afbuigendVoorHardware : afbuigendVoorHardware);
            }
            StatusTekst.Text = $"Wissel gezet op: {dubbelklikWissel.Stand}." + (dubbelklikWissel.GekoppeldeOverloopwissel != null ? $" Overloopwissel-partner {dubbelklikWissel.GekoppeldeOverloopwissel.Adres} meebewogen." : "");
            Redraw();
            return;
        }

        if (e.ClickCount == 2 && SymboolOpPositie(positie) is Driewegwissel dubbelklikDriewegwissel)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                string? nieuwAdres = InputDialog.Vraag(this, "Driewegwissel eigenschappen", "Adres (decodernummer):", dubbelklikDriewegwissel.Adres.ToString());
                if (nieuwAdres != null)
                {
                    if (int.TryParse(nieuwAdres, out int adres)) dubbelklikDriewegwissel.Adres = adres;
                    else StatusTekst.Text = "Ongeldig adres — vul een getal in.";
                }
                Redraw();
                return;
            }

            // Cyclet door de drie standen: Links -> Rechtdoor -> Rechts -> Links.
            dubbelklikDriewegwissel.Stand = dubbelklikDriewegwissel.Stand switch
            {
                DriewegwisselStand.Links => DriewegwisselStand.Rechtdoor,
                DriewegwisselStand.Rechtdoor => DriewegwisselStand.Rechts,
                _ => DriewegwisselStand.Links
            };
            // GEVONDEN, ZELFDE GAT ALS EERDER BIJ WISSEL/KRUISWISSEL: dubbelklikken stuurde
            // nooit een echt hardwarecommando. LET OP - onvolledige oplossing: het
            // onderliggende protocol kent maar 2 standen (rechtdoor/afbuigend) per adres,
            // en Driewegwissel heeft maar 1 adres voor 3 standen - Links én Rechts sturen
            // hier dus BEIDE hetzelfde "afbuigend"-commando, ze zijn vanuit de hardware
            // gezien niet van elkaar te onderscheiden. Rechtdoor is het enige stand die wél
            // eenduidig is. Nodig: opheldering hoe de fysieke driewegwissel Links vs Rechts
            // laat afhangen van dit ene adres, voordat dit verder verfijnd kan worden.
            _hardwareBeheerder.StuurWisselCommando(dubbelklikDriewegwissel.Adres, dubbelklikDriewegwissel.Stand != DriewegwisselStand.Rechtdoor);
            StatusTekst.Text = $"Driewegwissel gezet op: {dubbelklikDriewegwissel.Stand}.";
            Redraw();
            return;
        }

        if (e.ClickCount == 2 && SymboolOpPositie(positie) is Kruiswissel dubbelklikKruiswissel)
        {
            if (!dubbelklikKruiswissel.IsEngels)
            {
                StatusTekst.Text = "Dit is een gewone (passieve) kruiswissel - geen stand om te wijzigen. Gebruik de Engelse wissel-tool voor een schakelbare kruising.";
                return;
            }
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                string? nieuwAdres = InputDialog.Vraag(this, "Engelse wissel eigenschappen", "Adres motor 1 (decodernummer):", dubbelklikKruiswissel.Adres.ToString());
                if (nieuwAdres != null)
                {
                    if (int.TryParse(nieuwAdres, out int adres)) dubbelklikKruiswissel.Adres = adres;
                    else StatusTekst.Text = "Ongeldig adres — vul een getal in.";
                }
                // Gebruikersbevestigd (Roco Engelse wissel): twee ONAFHANKELIJKE
                // wisselmotoren, dus twee losse adressen nodig - zie Model.Kruiswissel.Adres2.
                string? nieuwAdres2 = InputDialog.Vraag(this, "Engelse wissel eigenschappen", "Adres motor 2 (decodernummer):", dubbelklikKruiswissel.Adres2.ToString());
                if (nieuwAdres2 != null)
                {
                    if (int.TryParse(nieuwAdres2, out int adres2)) dubbelklikKruiswissel.Adres2 = adres2;
                    else StatusTekst.Text = "Ongeldig adres — vul een getal in.";
                }
                // GEBRUIKERSVERZOEK ("de kruiswissel-sectie heeft een eigen rijstroom-adres
                // nodig, los van blok3/4/7") - zie Model.Kruiswissel.RijAdres. 0 = niet
                // ingesteld = geen apart adres nodig (onschadelijk als de sectie gewoon via
                // een aangrenzend blok gevoed wordt).
                string? nieuwRijAdres = InputDialog.Vraag(this, "Engelse wissel eigenschappen", "Rijstroom-adres van de kruiswissel-sectie zelf (0 = geen apart adres nodig):", dubbelklikKruiswissel.RijAdres.ToString());
                if (nieuwRijAdres != null)
                {
                    if (int.TryParse(nieuwRijAdres, out int rijAdres)) dubbelklikKruiswissel.RijAdres = rijAdres;
                    else StatusTekst.Text = "Ongeldig adres — vul een getal in.";
                }
                // GEBRUIKERSCORRECTIE, FUNDAMENTEEL (zie Model.Kruiswissel): de oude vraag
                // "staat motor 2 fysiek omgekeerd bedraad?" ging uit van een foutief model
                // (één gedeelde stand, motor 2 hooguit tegengesteld). In werkelijkheid hebben
                // de twee motoren van een Roco Engelse wissel elk hun eigen, per-rijweg
                // vastgelegde stand die niet uit één "omgekeerde polariteit"-vlag is af te
                // leiden - deze vraag is daarom vervallen. De exacte combinatie per rijweg
                // wordt vastgelegd via een Wisselstraat (WisselstraatBeheerder.ToggleKruiswissel).
                Redraw();
                return;
            }

            // GEVONDEN GAT (gebruiker: wissel 14 -> Engelse wissel naar blok 3 vereist de
            // GEMENGDE combinatie motor13=Rechtdoor/motor10=Rechtdoor resp. een andere mix
            // voor blok 1 - zie Model.Kruiswissel. Een Wisselstraat legt zo'n combinatie vast
            // via WisselstraatBeheerder.ToggleKruiswissel, maar die methode neemt simpelweg de
            // HUIDIGE StandAdres/StandAdres2 van de kruiswissel over zoals die op dit moment in
            // de tekening staat. Tot nu toe kon de tekening zelf echter NOOIT een gemengde
            // combinatie bereiken: zowel deze dubbelklik hierbeneden als de kijkscherm-klik
            // hierboven zetten altijd BEIDE motoren gelijk. Een Wisselstraat met een echt
            // gemengde combinatie (zoals blok7->blok1: motor13=Rechtdoor/motor10=Afbuigend, of
            // de hier benodigde blok4->blok3/blok4->blok1-rijwegen) kon dus NOOIT aangemaakt
            // worden - precies de reden dat wissel 14 nooit een werkende Afbuigend-rijweg kreeg.
            // Nieuw: Ctrl+dubbelklik toggelt ALLEEN motor 1 (Adres), Alt+dubbelklik ALLEEN
            // motor 2 (Adres2) - zo kan de gebruiker eerst de gewenste (eventueel gemengde)
            // combinatie in de tekening zetten en die daarna via de Wisselstraat-tool
            // (klikken terwijl een wisselstraat actief is) vastleggen. De gewone dubbelklik
            // hieronder (geen modifier) blijft ongewijzigd de simpele, symmetrische testtoggle.
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                bool afbuigendMotor1 = dubbelklikKruiswissel.StandAdres != WisselStand.Afbuigend;
                dubbelklikKruiswissel.StandAdres = afbuigendMotor1 ? WisselStand.Afbuigend : WisselStand.Rechtdoor;
                dubbelklikKruiswissel.Stand = (dubbelklikKruiswissel.StandAdres == WisselStand.Afbuigend || dubbelklikKruiswissel.StandAdres2 == WisselStand.Afbuigend)
                    ? KruiswisselStand.Overstap : KruiswisselStand.Rechtdoor;
                if (dubbelklikKruiswissel.Adres > 0) _hardwareBeheerder.StuurWisselCommando(dubbelklikKruiswissel.Adres, afbuigendMotor1);
                StatusTekst.Text = $"Engelse wissel: ALLEEN motor {dubbelklikKruiswissel.Adres} gezet op {dubbelklikKruiswissel.StandAdres} (motor {dubbelklikKruiswissel.Adres2} blijft {dubbelklikKruiswissel.StandAdres2}).";
                Redraw();
                return;
            }
            if (Keyboard.Modifiers == ModifierKeys.Alt)
            {
                bool afbuigendMotor2 = dubbelklikKruiswissel.StandAdres2 != WisselStand.Afbuigend;
                dubbelklikKruiswissel.StandAdres2 = afbuigendMotor2 ? WisselStand.Afbuigend : WisselStand.Rechtdoor;
                dubbelklikKruiswissel.Stand = (dubbelklikKruiswissel.StandAdres == WisselStand.Afbuigend || dubbelklikKruiswissel.StandAdres2 == WisselStand.Afbuigend)
                    ? KruiswisselStand.Overstap : KruiswisselStand.Rechtdoor;
                if (dubbelklikKruiswissel.Adres2 > 0) _hardwareBeheerder.StuurWisselCommando(dubbelklikKruiswissel.Adres2, afbuigendMotor2);
                StatusTekst.Text = $"Engelse wissel: ALLEEN motor {dubbelklikKruiswissel.Adres2} gezet op {dubbelklikKruiswissel.StandAdres2} (motor {dubbelklikKruiswissel.Adres} blijft {dubbelklikKruiswissel.StandAdres}).";
                Redraw();
                return;
            }

            // GEBRUIKERSCORRECTIE (zie Model.Kruiswissel): geen vaste onderlinge relatie
            // tussen de twee motoren. Voor een simpele handmatige dubbelklik-test (geen
            // specifieke rijweg bekend) toggelen we beide motoren gezamenlijk; de exacte,
            // soms gemengde combinatie per rijweg legt de gebruiker vast via een Wisselstraat.
            bool afbuigendVoorHardwareKruiswissel = dubbelklikKruiswissel.StandAdres != WisselStand.Afbuigend;
            dubbelklikKruiswissel.StandAdres = afbuigendVoorHardwareKruiswissel ? WisselStand.Afbuigend : WisselStand.Rechtdoor;
            dubbelklikKruiswissel.StandAdres2 = afbuigendVoorHardwareKruiswissel ? WisselStand.Afbuigend : WisselStand.Rechtdoor;
            dubbelklikKruiswissel.Stand = afbuigendVoorHardwareKruiswissel ? KruiswisselStand.Overstap : KruiswisselStand.Rechtdoor;
            // Zelfde gat als bij een gewone wissel hierboven: dubbelklikken stuurde nooit
            // een echt hardwarecommando - voor handmatig testen (zoals hier) juist essentieel.
            if (dubbelklikKruiswissel.Adres > 0) _hardwareBeheerder.StuurWisselCommando(dubbelklikKruiswissel.Adres, afbuigendVoorHardwareKruiswissel);
            if (dubbelklikKruiswissel.Adres2 > 0) _hardwareBeheerder.StuurWisselCommando(dubbelklikKruiswissel.Adres2, afbuigendVoorHardwareKruiswissel);
            StatusTekst.Text = $"Engelse wissel gezet op: motor {dubbelklikKruiswissel.Adres}={dubbelklikKruiswissel.StandAdres}, motor {dubbelklikKruiswissel.Adres2}={dubbelklikKruiswissel.StandAdres2}.";
            Redraw();
            return;
        }

        if (e.ClickCount == 2 && SymboolOpPositie(positie) is Tekst dubbelklikTekst)
        {
            string? nieuweTekst = InputDialog.Vraag(this, "Tekst aanpassen", "Inhoud:", dubbelklikTekst.Inhoud);
            if (nieuweTekst != null) dubbelklikTekst.Inhoud = nieuweTekst;
            Redraw();
            return;
        }

        if (e.ClickCount == 2 && SymboolOpPositie(positie) is Perron dubbelklikPerron)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                // Zoals Koploper's eigen "toolwindow waarin de vier hoekpunten vermeld
                // staan" - bij ons vereenvoudigd tot breedte/hoogte, want ons Perron blijft
                // (net als de meeste echte Koploper-voorbeelden) gewoon rechthoekig.
                string? nieuweBreedte = InputDialog.Vraag(this, "Perron afmeting", "Breedte (bijv. 90 voor hoekpunten 0,0/90,0/90,H/0,H):", dubbelklikPerron.Breedte.ToString(System.Globalization.CultureInfo.InvariantCulture));
                if (nieuweBreedte != null && double.TryParse(nieuweBreedte, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double breedte) && breedte > 0)
                    dubbelklikPerron.Breedte = breedte;

                string? nieuweHoogte = InputDialog.Vraag(this, "Perron afmeting", "Hoogte (bijv. 20 voor hoekpunten 0,0/W,0/W,20/0,20):", dubbelklikPerron.Hoogte.ToString(System.Globalization.CultureInfo.InvariantCulture));
                if (nieuweHoogte != null && double.TryParse(nieuweHoogte, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double hoogte) && hoogte > 0)
                    dubbelklikPerron.Hoogte = hoogte;

                StatusTekst.Text = $"Perron-afmeting: {dubbelklikPerron.Breedte:0} x {dubbelklikPerron.Hoogte:0}.";
                Redraw();
                return;
            }
            string? nieuweOmschrijving = InputDialog.Vraag(this, "Perron aanpassen", "Omschrijving (bijv. 'Spoor 3'):", dubbelklikPerron.Omschrijving);
            if (nieuweOmschrijving != null) dubbelklikPerron.Omschrijving = nieuweOmschrijving;
            Redraw();
            return;
        }

        if (e.ClickCount == 2 && SymboolOpPositie(positie) is Schakelaar dubbelklikSchakelaar)
        {
            dubbelklikSchakelaar.Aan = !dubbelklikSchakelaar.Aan;
            StatusTekst.Text = $"Schakelaar '{dubbelklikSchakelaar.Omschrijving}' gezet op: {(dubbelklikSchakelaar.Aan ? "aan" : "uit")}.";
            Redraw();
            return;
        }

        if (e.ClickCount == 2 && SymboolOpPositie(positie) is Ontkoppelrail dubbelklikOntkoppelrail)
        {
            string? nieuwAdresOntkoppelrail = InputDialog.Vraag(this, "Ontkoppelrail eigenschappen", "Adres (decodernummer):", dubbelklikOntkoppelrail.Adres.ToString());
            if (nieuwAdresOntkoppelrail != null)
            {
                if (int.TryParse(nieuwAdresOntkoppelrail, out int adresOntkoppelrail))
                {
                    dubbelklikOntkoppelrail.Adres = adresOntkoppelrail;
                    StatusTekst.Text = $"Ontkoppelrail adres gezet op {adresOntkoppelrail}.";
                }
                else
                {
                    StatusTekst.Text = "Ongeldig adres — vul een getal in.";
                }
            }
            Redraw();
            return;
        }

        if (e.ClickCount == 2 && SymboolOpPositie(positie) is Bezetmelder dubbelklikBezetmelder)
        {
            string? nieuwMeldernummer = InputDialog.Vraag(this, "Bezetmelder eigenschappen", "Meldernummer (ter documentatie, optioneel):", dubbelklikBezetmelder.MeldernNummer.ToString());
            if (nieuwMeldernummer != null)
            {
                if (int.TryParse(nieuwMeldernummer, out int meldernummer))
                {
                    dubbelklikBezetmelder.MeldernNummer = meldernummer;
                    StatusTekst.Text = $"Bezetmelder-nummer gezet op {meldernummer}.";
                }
                else
                {
                    StatusTekst.Text = "Ongeldig nummer — vul een getal in.";
                }
            }
            Redraw();
            return;
        }
        if (e.ClickCount == 2 && BlokOpPositie(positie) is Blok dubbelklikBlok)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && _maxTabblad > 1)
            {
                int volgende = _baanBeheerder.TabbladVanBlok(dubbelklikBlok) % _maxTabblad + 1;
                _baanBeheerder.ZetBlokOpTabblad(dubbelklikBlok, volgende);
                StatusTekst.Text = $"Blok {dubbelklikBlok.Nummer} verplaatst naar tabblad {volgende}.";
                Redraw();
                return;
            }

            // Zelfde reden als in MainWindow: alleen bij een echte overgang naar Kopspoor
            // een stootblok toevoegen, niet bij elke opslag van een blok dat al Kopspoor was.
            var vorigTypeVoorDialoog = dubbelklikBlok.Type;
            var blokDialoog = new BlokEigenschappenDialog(dubbelklikBlok, _blokBeheerder) { Owner = this };
            if (blokDialoog.ShowDialog() == true)
            {
                if (vorigTypeVoorDialoog != BlokType.Kopspoor && dubbelklikBlok.Type == BlokType.Kopspoor) ZorgVoorStootblok(dubbelklikBlok);
                Redraw();
            }
            return;
        }

        switch (_huidigTool)
        {
            case Tool.Verplaatsen:
                StartVerplaatsen(positie);
                break;

            case Tool.Lijn:
                // Zoals in Koploper: één klik zet een KORT lijnstukje (2 punten) neer op de
                // aanklikplek - de precieze vorm/koppeling stel je daarna in door de losse
                // eindpunten te verslepen (zie StartVerplaatsen/BaanCanvas_MouseMove), niet
                // door hier al een hele polylijn met klikken op te bouwen.
                var (stubAnkerPositie, stubAnkerBlok, stubAnkerWissel, stubAnkerLijn, stubAnkerLijnIndex, stubAnkerKruiswissel, stubAnkerKruiswisselIndex) = DichtstbijzijndAnkerpunt(positie);
                var eerstePunt = stubAnkerPositie ?? UitlijnenIndienNodig(positie);
                var tweedePunt = new Point(eerstePunt.X + SymboolGrootte * 0.8, eerstePunt.Y);

                var nieuweLijnStub = new Lijn { Tabblad = _huidigTabblad, X = eerstePunt.X, Y = eerstePunt.Y };
                nieuweLijnStub.Volgnummer = _baanBeheerder.Symbolen.OfType<Lijn>().Select(l => l.Volgnummer).DefaultIfEmpty(0).Max() + 1;
                nieuweLijnStub.VoegPuntToe(eerstePunt, stubAnkerBlok, stubAnkerWissel, stubAnkerLijn, stubAnkerLijnIndex, stubAnkerKruiswissel, stubAnkerKruiswisselIndex);
                nieuweLijnStub.VoegPuntToe(tweedePunt, null, null);
                _baanBeheerder.VoegSymboolToe(nieuweLijnStub);
                _geselecteerdSymbool = nieuweLijnStub;
                StatusTekst.Text = "Nieuw lijnstukje geplaatst - sleep de losse eindpunten (in Verplaatsen) naar waar ze moeten komen; ze klappen vast op nabije ankerpunten of op 45°.";
                Redraw();
                break;

            case Tool.Wissel:
                var wisselPositie = UitlijnenIndienNodig(positie);
                int volgendWisselAdres = _baanBeheerder.Symbolen.OfType<Wissel>().Select(w => w.Adres).DefaultIfEmpty(-1).Max() + 1;
                // Nieuwe wissels wisselen automatisch af rechts/links-afbuigend, zodat je niet
                // per ongeluk alleen maar rechts-afbuigende wissels neerzet - Shift+rechtsklik
                // blijft daarna de manier om het per wissel nog om te zetten.
                bool volgendeLinksom = _baanBeheerder.Symbolen.OfType<Wissel>().Count() % 2 == 1;
                _baanBeheerder.VoegSymboolToe(new Wissel { X = wisselPositie.X, Y = wisselPositie.Y, Tabblad = _huidigTabblad, Adres = volgendWisselAdres, AfbuigingLinksom = volgendeLinksom });
                Redraw();
                break;

            case Tool.Sein:
                var seinPositie = UitlijnenIndienNodig(positie);
                int volgendSeinAdres = _baanBeheerder.Symbolen.OfType<Sein>().Select(s => s.Adres).DefaultIfEmpty(-1).Max() + 1;
                _baanBeheerder.VoegSymboolToe(new Sein { X = seinPositie.X, Y = seinPositie.Y, Tabblad = _huidigTabblad, Adres = volgendSeinAdres });
                Redraw();                break;

            case Tool.Stootblok:
                var stootblokPositie = UitlijnenIndienNodig(positie);
                _baanBeheerder.VoegSymboolToe(new Stootblok { X = stootblokPositie.X, Y = stootblokPositie.Y, Tabblad = _huidigTabblad });
                Redraw();
                break;

            case Tool.Perron:
                var perronPositie = UitlijnenIndienNodig(positie);
                string? perronNaam = InputDialog.Vraag(this, "Nieuw perron", "Omschrijving (bijv. 'Spoor 3'), leeg mag ook:", "");
                _baanBeheerder.VoegSymboolToe(new Perron { X = perronPositie.X, Y = perronPositie.Y, Tabblad = _huidigTabblad, Omschrijving = perronNaam ?? "" });
                Redraw();
                break;

            case Tool.Tekst:
                var tekstPositie = UitlijnenIndienNodig(positie);
                _baanBeheerder.VoegSymboolToe(new Tekst { X = tekstPositie.X, Y = tekstPositie.Y, Tabblad = _huidigTabblad });
                Redraw();
                break;

            case Tool.Schakelaar:
                var schakelaarPositie = UitlijnenIndienNodig(positie);
                string? schakelaarNaam = InputDialog.Vraag(this, "Nieuwe schakelaar", "Omschrijving (bijv. 'Verlichting dorp'), leeg mag ook:", "");
                _baanBeheerder.VoegSymboolToe(new Schakelaar { X = schakelaarPositie.X, Y = schakelaarPositie.Y, Tabblad = _huidigTabblad, Omschrijving = schakelaarNaam ?? "" });
                Redraw();
                break;

            case Tool.Pijl:
                var pijlPositie = UitlijnenIndienNodig(positie);
                _baanBeheerder.VoegSymboolToe(new Pijl { X = pijlPositie.X, Y = pijlPositie.Y, Tabblad = _huidigTabblad });
                Redraw();
                break;

            case Tool.Ontkoppelrail:
                var ontkoppelrailPositie = UitlijnenIndienNodig(positie);
                int volgendOntkoppelrailAdres = _baanBeheerder.Symbolen.OfType<Ontkoppelrail>().Select(o => o.Adres).DefaultIfEmpty(-1).Max() + 1;
                _baanBeheerder.VoegSymboolToe(new Ontkoppelrail { X = ontkoppelrailPositie.X, Y = ontkoppelrailPositie.Y, Tabblad = _huidigTabblad, Adres = volgendOntkoppelrailAdres });
                Redraw();
                break;

            case Tool.Bezetmelder:
                var bezetmelderPositie = UitlijnenIndienNodig(positie);
                _baanBeheerder.VoegSymboolToe(new Bezetmelder { X = bezetmelderPositie.X, Y = bezetmelderPositie.Y, Tabblad = _huidigTabblad });
                Redraw();
                break;

            case Tool.Driewegwissel:
                var driewegwisselPositie = UitlijnenIndienNodig(positie);
                int volgendDriewegwisselAdres = _baanBeheerder.Symbolen.OfType<Driewegwissel>().Select(w => w.Adres).DefaultIfEmpty(-1).Max() + 1;
                _baanBeheerder.VoegSymboolToe(new Driewegwissel { X = driewegwisselPositie.X, Y = driewegwisselPositie.Y, Tabblad = _huidigTabblad, Adres = volgendDriewegwisselAdres });
                Redraw();
                break;

            case Tool.Kruiswissel:
                var kruiswisselPositie = UitlijnenIndienNodig(positie);
                _baanBeheerder.VoegSymboolToe(new Kruiswissel { X = kruiswisselPositie.X, Y = kruiswisselPositie.Y, Tabblad = _huidigTabblad });
                Redraw();
                break;

            case Tool.EngelseWissel:
                var engelseWisselPositie = UitlijnenIndienNodig(positie);
                int volgendEngelseWisselAdres = _baanBeheerder.Symbolen.OfType<Kruiswissel>().Where(k => k.IsEngels).Select(k => k.Adres).DefaultIfEmpty(-1).Max() + 1;
                _baanBeheerder.VoegSymboolToe(new Kruiswissel { X = engelseWisselPositie.X, Y = engelseWisselPositie.Y, Tabblad = _huidigTabblad, IsEngels = true, Adres = volgendEngelseWisselAdres });
                Redraw();
                break;

            case Tool.Koppelen:
                var blok = BlokOpPositie(positie);
                if (blok != null)
                {
                    _koppelGeselecteerdBlok = blok;
                }
                else if (_koppelGeselecteerdBlok != null && SymboolOpPositie(positie) is IGekoppeldAanBlok koppelbaarDoel)
                {
                    _baanBeheerder.ToggleKoppeling(koppelbaarDoel, _koppelGeselecteerdBlok);
                }
                Redraw();
                break;

            case Tool.Wisselstraat:
                BehandelWisselstraatKlik(positie);
                break;

            case Tool.Overloopwissel:
                if (SymboolOpPositie(positie) is Wissel gekozenWissel)
                {
                    if (_overloopwisselEersteKeuze is null)
                    {
                        _overloopwisselEersteKeuze = gekozenWissel;
                        StatusTekst.Text = $"Wissel {gekozenWissel.Adres} gekozen - klik nu de wissel waarmee 'm gekoppeld moet worden.";
                    }
                    else if (gekozenWissel == _overloopwisselEersteKeuze)
                    {
                        // Zelfde wissel nogmaals aangeklikt: ontkoppelen (van zichzelf én de partner).
                        if (_overloopwisselEersteKeuze.GekoppeldeOverloopwissel != null)
                            _overloopwisselEersteKeuze.GekoppeldeOverloopwissel.GekoppeldeOverloopwissel = null;
                        _overloopwisselEersteKeuze.GekoppeldeOverloopwissel = null;
                        StatusTekst.Text = $"Overloopwissel-koppeling van wissel {_overloopwisselEersteKeuze.Adres} opgeheven.";
                        _overloopwisselEersteKeuze = null;
                    }
                    else
                    {
                        // Symmetrisch koppelen: allebei kant elkaar aan, en beide standen
                        // meteen gelijktrekken (de tweede wissel volgt de eerste).
                        _overloopwisselEersteKeuze.GekoppeldeOverloopwissel = gekozenWissel;
                        gekozenWissel.GekoppeldeOverloopwissel = _overloopwisselEersteKeuze;
                        gekozenWissel.Stand = _overloopwisselEersteKeuze.Stand;
                        StatusTekst.Text = $"Wissel {_overloopwisselEersteKeuze.Adres} en {gekozenWissel.Adres} gekoppeld als overloopwissel - ze zetten voortaan altijd samen om.";
                        _overloopwisselEersteKeuze = null;
                    }
                    Redraw();
                }
                break;
        }
    }

    private void BehandelWisselstraatKlik(Point positie)
    {
        var blok = BlokOpPositie(positie);

        if (_wisselstraatActief is null)
        {
            // Nog geen actieve wisselstraat: eerst van-blok, dan naar-blok kiezen
            if (blok is null) return;

            if (_wisselstraatVanKandidaat is null)
            {
                _wisselstraatVanKandidaat = blok;
                StatusTekst.Text = $"Van-blok: {blok.Nummer}. Klik nu het naar-blok.";
            }
            else
            {
                var wisselstraat = _wisselstraatBeheerder.NieuwWisselstraat(_wisselstraatVanKandidaat, blok, _blokBeheerder);
                if (wisselstraat is null)
                {
                    StatusTekst.Text = $"Geen relatie {_wisselstraatVanKandidaat.Nummer}->{blok.Nummer} in het blokkenschema — leg die eerst vast. Opnieuw beginnen: klik een van-blok.";
                    _wisselstraatVanKandidaat = null;
                }
                else
                {
                    _wisselstraatActief = wisselstraat;
                    StatusTekst.Text = $"Wisselstraat {wisselstraat} actief — klik wissels/lijnen om toe te voegen, Escape rondt af.";
                }
            }
        }
        else
        {
            // Actieve wisselstraat: wissels/lijnen aan-/uitklikken
            var symbool = SymboolOpPositie(positie);
            if (symbool is Wissel wissel)
            {
                _wisselstraatBeheerder.ToggleWissel(_wisselstraatActief, wissel);
                StatusTekst.Text = $"Wisselstraat {_wisselstraatActief}: {_wisselstraatActief.Wissels.Count} wissel(s), {_wisselstraatActief.Lijnen.Count} lijn(en). Escape rondt af.";

                // Waarschuw als dezelfde wissel met dezelfde gewenste stand ook al in een
                // ANDERE wisselstraat vanaf hetzelfde van-blok zit maar naar een ander doel
                // leidt - dat is precies de fout die tot een verkeerd rijdende wissel leidt.
                var toegevoegd = _wisselstraatActief.Wissels.FirstOrDefault(x => x.Wissel == wissel);
                if (toegevoegd != null)
                {
                    var conflict = _wisselstraatBeheerder.Wisselstraten
                        .Where(w => w != _wisselstraatActief && w.Van == _wisselstraatActief.Van && w.Naar != _wisselstraatActief.Naar)
                        .FirstOrDefault(w => w.Wissels.Any(x => x.Wissel == wissel && x.GewensteStand == toegevoegd.GewensteStand));
                    if (conflict != null)
                        StatusTekst.Text += $" LET OP: wissel {wissel.Adres} staat met dezelfde stand ook al in {conflict} — zet de wissel eerst in de juiste stand voordat je 'm hier toevoegt, of corrigeer het later via 'Wisselstraten beheren'.";
                }
            }
            else if (symbool is Lijn lijn)
            {
                _wisselstraatBeheerder.ToggleLijn(_wisselstraatActief, lijn);
                StatusTekst.Text = $"Wisselstraat {_wisselstraatActief}: {_wisselstraatActief.Wissels.Count} wissel(s), {_wisselstraatActief.Lijnen.Count} lijn(en). Escape rondt af.";
            }
            else if (symbool is Kruiswissel kruiswissel)
            {
                _wisselstraatBeheerder.ToggleKruiswissel(_wisselstraatActief, kruiswissel);
                StatusTekst.Text = $"Wisselstraat {_wisselstraatActief}: {_wisselstraatActief.Wissels.Count} wissel(s), {_wisselstraatActief.Kruiswisselstanden.Count} kruiswissel(s), {_wisselstraatActief.Lijnen.Count} lijn(en). Escape rondt af.";
            }
        }

        Redraw();
    }

    private void StartVerplaatsen(Point positie)
    {
        var (lijnPuntLijn, lijnPuntIndex) = LijnPuntOpPositie(positie);
        if (lijnPuntLijn != null)
        {
            _gesleeptLijnPunt = lijnPuntLijn;
            _gesleeptLijnPuntIndex = lijnPuntIndex;
            _gesleeptBlok = null;
            _gesleeptSymbool = null;
            _geselecteerdSymbool = lijnPuntLijn;
            BaanCanvas.CaptureMouse();
            Redraw();
            return;
        }

        var blok = BlokOpPositie(positie);
        if (blok != null)
        {
            _gesleeptBlok = blok;
            _sleepOffset = positie - _baanBeheerder.PositieVanBlok(blok);
            _geselecteerdSymbool = null;
            BaanCanvas.CaptureMouse();
            return;
        }

        var symbool = SymboolOpPositie(positie);
        _gesleeptSymbool = symbool;
        _geselecteerdSymbool = symbool;
        if (symbool != null)
        {
            _sleepOffset = positie - new Point(symbool.X, symbool.Y);
            BaanCanvas.CaptureMouse();
        }
        Redraw();
    }

    private void BaanCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        var positie = e.GetPosition(BaanCanvas);

        if (_gesleeptLijnPunt != null)
        {
            var punten = _gesleeptLijnPunt.Punten;
            Point? referentiePunt = _gesleeptLijnPuntIndex > 0 ? punten[_gesleeptLijnPuntIndex - 1]
                : punten.Count > 1 ? punten[1] : (Point?)null;

            var (ankerPositie, ankerBlok, ankerWissel, ankerLijn, ankerLijnIndex, ankerKruiswissel, ankerKruiswisselIndex) = DichtstbijzijndAnkerpunt(positie);

            // voorkom dat een punt aan zichzelf verankert
            if (ankerLijn == _gesleeptLijnPunt && ankerLijnIndex == _gesleeptLijnPuntIndex)
            {
                ankerPositie = null; ankerBlok = null; ankerWissel = null; ankerLijn = null; ankerLijnIndex = -1; ankerKruiswissel = null; ankerKruiswisselIndex = -1;
            }

            // Let op: een gevonden ANKERPUNT gaat altijd voor, ongeacht de resulterende
            // hoek - een koppeling tussen twee blokken/wissels/lijnen mag altijd, ook als
            // die twee net niet perfect op een 45°-lijn met elkaar liggen (bijv. omdat de
            // ene lijn een paar pixels lager getekend is dan de andere). De 45°-regel geldt
            // alleen als TERUGVAL wanneer er geen echt ankerpunt gevonden wordt - daarmee
            // blijft "vrij" tekenen netjes op het rooster, zonder het koppelen zelf onnodig
            // te blokkeren. (Eerder liet dit venster koppelen wél afhangen van de hoek, wat
            // te streng bleek: twee lijnen die je bij elkaar wilt brengen konden daardoor
            // soms niet meer koppelen.)
            Point nieuwPunt;
            if (ankerPositie != null)
            {
                nieuwPunt = ankerPositie.Value;
            }
            else
            {
                nieuwPunt = UitlijnenIndienNodig(positie);
                if (referentiePunt != null)
                {
                    nieuwPunt = KlapVastOp45Graden(referentiePunt.Value, nieuwPunt);

                    // Tweede kans: ligt er, nu we toch op de 45°-lijn zitten, een echt
                    // ankerpunt vlakbij DIE lijn (i.p.v. vlakbij de ruwe muispositie)? Dat
                    // vangt het geval waarin je net niet precies genoeg op het zichtbare
                    // ankerpuntje klikte, maar de 45°-richting er toch naartoe wijst -
                    // zonder dit zou zo'n lijn een toevallig-dichtbij-ogend, maar in werke-
                    // lijkheid NIET verankerd eindpunt krijgen. Ook hier geldt: wordt er
                    // een ankerpunt gevonden, dan geldt dat - de hoek is verder niet meer
                    // van belang zodra er een echte koppeling is.
                    var (tweedeKansPositie, tweedeKansBlok, tweedeKansWissel, tweedeKansLijn, tweedeKansLijnIndex, tweedeKansKruiswissel, tweedeKansKruiswisselIndex) = DichtstbijzijndAnkerpunt(nieuwPunt);
                    bool isZelfde = tweedeKansLijn == _gesleeptLijnPunt && tweedeKansLijnIndex == _gesleeptLijnPuntIndex;
                    if (tweedeKansPositie != null && !isZelfde)
                    {
                        nieuwPunt = tweedeKansPositie.Value;
                        ankerBlok = tweedeKansBlok;
                        ankerWissel = tweedeKansWissel;
                        ankerLijn = tweedeKansLijn;
                        ankerLijnIndex = tweedeKansLijnIndex;
                        ankerKruiswissel = tweedeKansKruiswissel;
                        ankerKruiswisselIndex = tweedeKansKruiswisselIndex;
                    }
                }
            }

            _gesleeptLijnPunt.Punten[_gesleeptLijnPuntIndex] = nieuwPunt;
            _gesleeptLijnPunt.PuntBlokAnkers[_gesleeptLijnPuntIndex] = ankerBlok;
            _gesleeptLijnPunt.PuntWisselAnkers[_gesleeptLijnPuntIndex] = ankerWissel;
            _gesleeptLijnPunt.PuntLijnAnkers[_gesleeptLijnPuntIndex] = ankerLijn;
            _gesleeptLijnPunt.PuntLijnAnkerIndex[_gesleeptLijnPuntIndex] = ankerLijnIndex;
            _gesleeptLijnPunt.PuntKruiswisselAnkers[_gesleeptLijnPuntIndex] = ankerKruiswissel;
            _gesleeptLijnPunt.PuntKruiswisselAnkerIndex[_gesleeptLijnPuntIndex] = ankerKruiswisselIndex;
            Redraw();
            return;
        }

        if (_gesleeptBlok != null)
        {
            // Niet de HOEK van het blok snappen, maar het MIDDEN - want de ankerpunten
            // liggen op een vaste afstand vanaf het midden (zie BlokAnkerpunten), en
            // BlokGrootte/2 (25) is zelf geen veelvoud van RasterGrootte. Door het midden
            // te snappen (i.p.v. de hoek) komen de ankerpunten wél altijd op het raster
            // uit, ongeacht dat BlokGrootte zelf geen mooi veelvoud is.
            var ruweHoek = positie - _sleepOffset;
            var ruwMidden = new Point(ruweHoek.X + BlokGrootte / 2, ruweHoek.Y + BlokGrootte / 2);
            var gesnaptMidden = UitlijnenIndienNodig(ruwMidden);
            var nieuwePositie = new Point(gesnaptMidden.X - BlokGrootte / 2, gesnaptMidden.Y - BlokGrootte / 2);
            var vorigePositie = _baanBeheerder.PositieVanBlok(_gesleeptBlok);
            var verschuiving = nieuwePositie - vorigePositie;

            _baanBeheerder.VerplaatsBlok(_gesleeptBlok, nieuwePositie);

            // Gekoppelde seinen (aan de uitgang van dit blok) verschuiven automatisch mee,
            // zodat ze niet los komen te staan van het blok waar ze bij horen.
            foreach (var sein in _baanBeheerder.Symbolen.OfType<Sein>().Where(s => s.GekoppeldBlok == _gesleeptBlok))
            {
                sein.X += verschuiving.X;
                sein.Y += verschuiving.Y;
            }

            // Ankerpunten van lijnen die bij dit blok horen verschuiven ook mee, zodat
            // de aansluiting op het blok behouden blijft.
            foreach (var lijn in _baanBeheerder.Symbolen.OfType<Lijn>())
            {
                for (int i = 0; i < lijn.Punten.Count; i++)
                {
                    if (i >= lijn.PuntBlokAnkers.Count) break; // veiligheidscheck bij oudere/inconsistente data
                    if (lijn.PuntBlokAnkers[i] == _gesleeptBlok)
                        lijn.Punten[i] += verschuiving;
                }
            }

            Redraw();
        }
        else if (_gesleeptSymbool != null)
        {
            // BUGFIX (gebruikersmelding: "ankerpunten van een wissel willen niet snappen
            // naar een ander wissel"): dit sleepte voorheen VOLLEDIG ongesnapt (geen
            // raster, geen ankerpunt) - alleen het verslepen van een LOS LIJNEINDPUNT
            // snapte al wél (via DichtstbijzijndAnkerpunt). Nu het raster exact aansluit
            // op alle ankerpunten (zie de eerdere wissel-raster-fix), is rastersnap tijdens
            // het slepen zelf voldoende: het symbool springt van rastercel naar rastercel,
            // en omdat alle andere elementen (wissels/blokken/lijnpunten) ook op het raster
            // liggen, sluit dat vanzelf netjes aan.
            var nieuwePositie = UitlijnenIndienNodig(positie - _sleepOffset);
            var verschuivingSymbool = nieuwePositie - new Point(_gesleeptSymbool.X, _gesleeptSymbool.Y);

            if (_gesleeptSymbool is Lijn lijn)
            {
                // alle punten met dezelfde verschuiving meenemen, zodat de vorm van de lijn behouden blijft
                for (int i = 0; i < lijn.Punten.Count; i++)
                    lijn.Punten[i] += verschuivingSymbool;

                // en lijnpunten van ANDERE lijnen die aan (een punt van) deze lijn verankerd zijn
                foreach (var andereLijn in _baanBeheerder.Symbolen.OfType<Lijn>())
                {
                    if (andereLijn == lijn) continue;
                    for (int i = 0; i < andereLijn.Punten.Count; i++)
                    {
                        if (i >= andereLijn.PuntLijnAnkers.Count) break;
                        if (andereLijn.PuntLijnAnkers[i] == lijn)
                            andereLijn.Punten[i] += verschuivingSymbool;
                    }
                }
            }
            else if (_gesleeptSymbool is Wissel gesleeptWissel)
            {
                // ankerpunten van lijnen die bij deze wissel horen verschuiven mee
                foreach (var andereLijn in _baanBeheerder.Symbolen.OfType<Lijn>())
                {
                    for (int i = 0; i < andereLijn.Punten.Count; i++)
                    {
                        if (i >= andereLijn.PuntWisselAnkers.Count) break;
                        if (andereLijn.PuntWisselAnkers[i] == gesleeptWissel)
                            andereLijn.Punten[i] += verschuivingSymbool;
                    }
                }
            }

            _gesleeptSymbool.X = nieuwePositie.X;
            _gesleeptSymbool.Y = nieuwePositie.Y;
            Redraw();
        }
    }

    /// <summary>Ontvangt een sleepoperatie vanuit het Overzicht locomotieven - zie
    /// MainWindow.SchemaCanvas_Drop voor dezelfde uitleg, hier het baanontwerp-equivalent.
    /// Niet in kijk-modus toegestaan - loc's plaatsen is een bewerkende actie.</summary>
    /// <summary>Ontvangt een sleepoperatie vanuit het Overzicht locomotieven - zet de
    /// gesleepte trein neer op het blok waar losgelaten wordt. Bewust ALLEEN in kijk-modus:
    /// dit is een operationele actie (een loc ergens neerzetten om mee te rijden), geen
    /// baanontwerp-bewerking, en hoort dus bij het kijkscherm - niet bij het tekenscherm,
    /// waar "Loc plaatsen" al via een knop/dialoogje gaat.</summary>
    private void BaanCanvas_Drop(object sender, DragEventArgs e)
    {
        if (!_kijkModus) return;
        if (!e.Data.GetDataPresent(typeof(Trein))) return;
        if (e.Data.GetData(typeof(Trein)) is not Trein trein) return;

        var positie = e.GetPosition(BaanCanvas);
        var doelBlok = BlokOpPositie(positie);
        if (doelBlok is null) return;

        var huidigBlok = _blokBeheerder.BlokVanLoc(trein);
        if (huidigBlok != null && huidigBlok != doelBlok && LocNaarAnderBlokGesleept != null)
        {
            // Al ergens anders geplaatst en naar een ANDER blok gesleept: niet zomaar
            // teleporteren, maar MainWindow laten uitzoeken hoe de loc daar zelf naartoe
            // kan rijden (pathfinding + een rit starten) - zie MainWindow voor de
            // daadwerkelijke afhandeling.
            LocNaarAnderBlokGesleept(trein, doelBlok);
            return;
        }

        // Nog niet geplaatst, of losgelaten op het blok waar de loc al staat: gewoon
        // rechtstreeks neerzetten, geen rit nodig.
        _blokBeheerder.PlaatsLoc(doelBlok, trein);
        // GEVONDEN GAT (gebruikersmelding: "als ik de loc van de baan haalde en weer
        // terugzette kreeg ik weer een spookmelding"): PlaatsLoc zet ALLEEN de interne
        // loc-op-blok-koppeling, maar markeert het blok NERGENS als "verwacht bezet" (niet
        // IsBezet, niet IsGereserveerd, niet IsHandmatigBezet) - dus een daaropvolgende,
        // ECHTE bezetmelding van de hardware voor precies dit blok werd door
        // MainWindow.Bezetmelding_VanHardware ALTIJD als "onverwacht" or spookmelding
        // gezien, ook al had de gebruiker de loc net zelf, bewust, hier neergezet.
        // ZetHandmatigBezet doet precies wat hier nodig is: markeert het blok als bezet
        // (telt voor seinen/routes gewoon mee, zie BlokBeheerder) EN sluit het uit van de
        // spookmelding-detectie.
        _blokBeheerder.ZetHandmatigBezet(doelBlok, true);
        StatusTekst.Text = $"Loc '{trein.Omschrijving}' geplaatst op blok {doelBlok.Nummer}.";
        Redraw();
    }

    private void BaanCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_kijkGesleeptLocBlok != null)
        {
            var kijkLosgelatenPositie = e.GetPosition(BaanCanvas);
            var kijkDoelBlok = BlokOpPositie(kijkLosgelatenPositie);
            var kijkTrein = _blokBeheerder.LocOpBlok(_kijkGesleeptLocBlok);
            if (kijkDoelBlok != null && kijkTrein != null && kijkDoelBlok != _kijkGesleeptLocBlok)
                LocNaarAnderBlokGesleept?.Invoke(kijkTrein, kijkDoelBlok);
            _kijkGesleeptLocBlok = null;
            BaanCanvas.ReleaseMouseCapture();
            return;
        }

        // Duidelijke terugkoppeling of dit eindpunt écht verankerd is, i.p.v. dat je dat
        // alleen aan de kleur van de lijn hoeft af te lezen - voorkomt het "ik dacht dat
        // hij gekoppeld was" misverstand.
        if (_gesleeptLijnPunt != null)
        {
            bool isVerankerd = _gesleeptLijnPunt.PuntBlokAnkers[_gesleeptLijnPuntIndex] != null
                || _gesleeptLijnPunt.PuntWisselAnkers[_gesleeptLijnPuntIndex] != null
                || _gesleeptLijnPunt.PuntLijnAnkers[_gesleeptLijnPuntIndex] != null
                || _gesleeptLijnPunt.PuntKruiswisselAnkers[_gesleeptLijnPuntIndex] != null;
            StatusTekst.Text = isVerankerd
                ? "Lijnpunt verankerd aan een blok/wissel/andere lijn."
                : "Lijnpunt NIET verankerd - staat los, alleen vastgeklapt op 45°.";
        }

        _gesleeptBlok = null;
        _gesleeptSymbool = null;
        _gesleeptLijnPunt = null;
        _gesleeptLijnPuntIndex = -1;
        BaanCanvas.ReleaseMouseCapture();
    }

    private void BaanCanvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_kijkModus)
        {
            // Zoals links-klik de stand omzet, zet rechtsklik in kijk-modus een wissel
            // defect/hersteld - zo kunnen eindgebruikers dit ook doen zonder het
            // bewerk-scherm te hoeven openen. Geen Alt-toets nodig hier (anders dan in het
            // bewerk-scherm): kijk-modus is bedoeld voor eindgebruikers die de baan
            // bedienen, niet voor wie de sneltoetsen uit het bewerk-scherm kent.
            var kijkDefectPositie = e.GetPosition(BaanCanvas);
            if (SymboolOpPositie(kijkDefectPositie) is Wissel kijkDefectWissel)
            {
                kijkDefectWissel.IsDefect = !kijkDefectWissel.IsDefect;
                StatusTekst.Text = kijkDefectWissel.IsDefect
                    ? $"Wissel {kijkDefectWissel.Adres} gemarkeerd als DEFECT."
                    : $"Wissel {kijkDefectWissel.Adres}: defect-markering opgeheven.";
                Redraw();
            }
            else if (BlokOpPositie(kijkDefectPositie) is Blok kijkRechtsklikBlok)
            {
                // Gebruikersverzoek: "als ik rechtsklik op een blok dat ik dan een overzicht
                // krijg van letterlijk alle eigenschappen die betrekking hebben op dat blok
                // en als er een loc staat ook die van de loc." Verving de eerdere, directe
                // OnderhoudsDialog-snelkoppeling: die zit nu ALS KNOP in het nieuwe
                // overzicht (BlokOverzichtDialog), dus die functionaliteit is niet
                // verdwenen, alleen één klik dieper.
                var trein = _blokBeheerder.LocOpBlok(kijkRechtsklikBlok);
                var treinrouteWindow = _treinrouteWindowOpvragen?.Invoke();
                string? rijstatus = trein != null ? treinrouteWindow?.TreinRijStatus(trein) : null;
                var overzichtDialoog = new BlokOverzichtDialog(kijkRechtsklikBlok, _blokBeheerder, _onderhoudsBeheerder, rijstatus) { Owner = this };
                overzichtDialoog.ShowDialog();
            }
            return;
        }

        var positie = e.GetPosition(BaanCanvas);

        // rechtsklik op een wissel (buiten het lijnen-tekenen om) draait de hoofdrichting 45°
        // verder (8 standen, net als de echte wissel-iconen); Shift+rechtsklik spiegelt de
        // afbuigende poot naar de andere kant.
        if (_lijnInAanleg is null && SymboolOpPositie(positie) is Wissel wissel)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
            {
                // Wissel op defect zetten/herstellen - een route die deze wissel nodig heeft
                // weigert te starten, en automatisch/hardware-omzetten slaat 'm bewust over.
                wissel.IsDefect = !wissel.IsDefect;
                StatusTekst.Text = wissel.IsDefect
                    ? $"Wissel {wissel.Adres} gemarkeerd als DEFECT - routes die deze wissel nodig hebben, starten niet meer."
                    : $"Wissel {wissel.Adres}: defect-markering opgeheven.";
            }
            else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                // "Altijd initialiseren": Koploper's eigen wissel-eigenschap die de stand bij
                // het verbinden met hardware altijd opnieuw verstuurt, ook als de software
                // denkt dat 'm al goed staat.
                wissel.AltijdInitialiseren = !wissel.AltijdInitialiseren;
                StatusTekst.Text = $"Wissel {wissel.Adres}: altijd initialiseren = {(wissel.AltijdInitialiseren ? "aan" : "uit")}.";
            }
            else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                wissel.AfbuigingLinksom = !wissel.AfbuigingLinksom;
                StatusTekst.Text = $"Wissel afbuiging gespiegeld: nu {(wissel.AfbuigingLinksom ? "linksom" : "rechtsom")}.";
            }
            else
            {
                wissel.HoofdrichtingGraden = (wissel.HoofdrichtingGraden + 45) % 360;
                StatusTekst.Text = $"Wissel-richting gedraaid naar {wissel.HoofdrichtingGraden}°.";
            }
            Redraw();
            return;
        }

        // rechtsklik op een driewegwissel draait de richting 45° verder; Ctrl+rechtsklik zet
        // 'altijd initialiseren' aan/uit, net als bij een gewone wissel.
        if (_lijnInAanleg is null && SymboolOpPositie(positie) is Driewegwissel driewegwissel)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                driewegwissel.AltijdInitialiseren = !driewegwissel.AltijdInitialiseren;
                StatusTekst.Text = $"Driewegwissel {driewegwissel.Adres}: altijd initialiseren = {(driewegwissel.AltijdInitialiseren ? "aan" : "uit")}.";
            }
            else
            {
                driewegwissel.HoofdrichtingGraden = (driewegwissel.HoofdrichtingGraden + 45) % 360;
                StatusTekst.Text = $"Driewegwissel-richting gedraaid naar {driewegwissel.HoofdrichtingGraden}°.";
            }
            Redraw();
            return;
        }

        // rechtsklik op een kruiswissel draait 'm 45° verder (bijv. tussen + en × vorm)
        if (_lijnInAanleg is null && SymboolOpPositie(positie) is Kruiswissel kruiswissel)
        {
            if (kruiswissel.IsEngels && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                kruiswissel.AltijdInitialiseren = !kruiswissel.AltijdInitialiseren;
                StatusTekst.Text = $"Engelse wissel {kruiswissel.Adres}: altijd initialiseren = {(kruiswissel.AltijdInitialiseren ? "aan" : "uit")}.";
            }
            else
            {
                kruiswissel.HoofdrichtingGraden = (kruiswissel.HoofdrichtingGraden + 45) % 360;
                StatusTekst.Text = $"{(kruiswissel.IsEngels ? "Engelse wissel" : "Kruiswissel")} gedraaid naar {kruiswissel.HoofdrichtingGraden}°.";
            }
            Redraw();
            return;
        }

        // rechtsklik op een pijl draait 'm 45 graden verder
        if (_lijnInAanleg is null && SymboolOpPositie(positie) is Pijl pijl)
        {
            pijl.HoekGraden = (pijl.HoekGraden + 45) % 360;
            StatusTekst.Text = $"Pijl gedraaid naar {pijl.HoekGraden}°.";
            Redraw();
            return;
        }

        // rechtsklik op een sein draait 'm 45 graden verder - zodat hij visueel bij de
        // richting van het spoor past in plaats van altijd verticaal te staan
        if (_lijnInAanleg is null && SymboolOpPositie(positie) is Sein rechtsklikSein)
        {
            rechtsklikSein.HoekGraden = (rechtsklikSein.HoekGraden + 45) % 360;
            StatusTekst.Text = $"Sein gedraaid naar {rechtsklikSein.HoekGraden}°.";
            Redraw();
            return;
        }

        // anders: rechtermuisknop rondt een lijn-in-aanleg af
        RondLijnInAanlegAf();
        Redraw();
    }

    /// <summary>Voegt de lijn-in-aanleg toe aan het baanontwerp als hij minstens 2 punten heeft
    /// (anders wordt hij gewoon weggegooid), en zet de sleutel weer op null.</summary>
    private void RondLijnInAanlegAf()
    {
        if (_lijnInAanleg is null) return;
        if (_lijnInAanleg.Punten.Count >= 2)
        {
            _baanBeheerder.VoegSymboolToe(_lijnInAanleg);
            StatusTekst.Text = $"Lijn met {_lijnInAanleg.Punten.Count} punten toegevoegd.";
        }
        _lijnInAanleg = null;
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F1)
        {
            Gebruiksaanwijzing_Click(sender, e);
            return;
        }
        if (_kijkModus) return; // puur bekijken - geen enkele andere sneltoets voert een bewerking uit

        if (e.Key == Key.Escape)
        {
            RondLijnInAanlegAf();
            _koppelGeselecteerdBlok = null;
            if (_wisselstraatActief != null)
            {
                StatusTekst.Text = $"Wisselstraat {_wisselstraatActief} afgerond ({_wisselstraatActief.Wissels.Count} wissel(s), {_wisselstraatActief.Lijnen.Count} lijn(en)). Klik een nieuw van-blok voor de volgende, of kies een andere tool.";
            }
            _wisselstraatVanKandidaat = null;
            _wisselstraatActief = null;
            Redraw();
        }
        else if (e.Key == Key.Delete && _geselecteerdSymbool != null)
        {
            if (_geselecteerdSymbool is Schakelaar verwijderdeSchakelaar) _actieBeheerder.VergeetSchakelaar(verwijderdeSchakelaar);
            _ongedaanMakenStack.Push(_geselecteerdSymbool);
            _opnieuwStack.Clear();
            _baanBeheerder.VerwijderSymbool(_geselecteerdSymbool);
            _geselecteerdSymbool = null;
            Redraw();
        }
        else if (e.Key == Key.Z && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            OngedaanMaken();
        }
        else if (e.Key == Key.Y && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            OpnieuwUitvoeren();
        }
        else if (e.Key == Key.D && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && _geselecteerdSymbool != null)
        {
            var nieuwePositie = new Point(_geselecteerdSymbool.X, _geselecteerdSymbool.Y) + new Vector(RasterGrootte, RasterGrootte);
            var kopie = Dupliceer(_geselecteerdSymbool, nieuwePositie);
            if (kopie != null)
            {
                _baanBeheerder.VoegSymboolToe(kopie);
                _geselecteerdSymbool = kopie;
                StatusTekst.Text = "Symbool gedupliceerd (Ctrl+D).";
                Redraw();
            }
        }
    }

    /// <summary>Maakt een kopie van een symbool op een nieuwe positie (Ctrl+D) - net als de
    /// handleiding beschrijft ("het laatst toegevoegde item herhalen"). Adressen van
    /// wissels/seinen/ontkoppelrails krijgen een nieuw, uniek volgnummer; koppelingen aan
    /// een blok/wissel worden gedeeld (niet zelf ook gedupliceerd).</summary>
    private BaanSymbool? Dupliceer(BaanSymbool bron, Point nieuwePositie)
    {
        switch (bron)
        {
            case Lijn lijn:
                var nieuweLijn = new Lijn { Tabblad = lijn.Tabblad, X = nieuwePositie.X, Y = nieuwePositie.Y, GekoppeldBlok = lijn.GekoppeldBlok };
                nieuweLijn.Volgnummer = _baanBeheerder.Symbolen.OfType<Lijn>().Select(l => l.Volgnummer).DefaultIfEmpty(0).Max() + 1;
                var verschil = nieuwePositie - lijn.Punten[0];
                for (int i = 0; i < lijn.Punten.Count; i++)
                    nieuweLijn.VoegPuntToe(lijn.Punten[i] + verschil, lijn.PuntBlokAnkers[i], lijn.PuntWisselAnkers[i]);
                return nieuweLijn;

            case Wissel wissel:
                int nieuwAdresWissel = _baanBeheerder.Symbolen.OfType<Wissel>().Select(w => w.Adres).DefaultIfEmpty(-1).Max() + 1;
                return new Wissel { Tabblad = wissel.Tabblad, X = nieuwePositie.X, Y = nieuwePositie.Y, Adres = nieuwAdresWissel, Stand = wissel.Stand, HoofdrichtingGraden = wissel.HoofdrichtingGraden, AfbuigingLinksom = wissel.AfbuigingLinksom };

            case Sein sein:
                int nieuwAdresSein = _baanBeheerder.Symbolen.OfType<Sein>().Select(s => s.Adres).DefaultIfEmpty(-1).Max() + 1;
                return new Sein { Tabblad = sein.Tabblad, X = nieuwePositie.X, Y = nieuwePositie.Y, Adres = nieuwAdresSein, Stand = sein.Stand, GekoppeldBlok = sein.GekoppeldBlok };

            case Stootblok stootblok:
                return new Stootblok { Tabblad = stootblok.Tabblad, X = nieuwePositie.X, Y = nieuwePositie.Y, GekoppeldBlok = stootblok.GekoppeldBlok, Richting = stootblok.Richting };

            case Perron perron:
                return new Perron { Tabblad = perron.Tabblad, X = nieuwePositie.X, Y = nieuwePositie.Y, Breedte = perron.Breedte, Hoogte = perron.Hoogte, Omschrijving = perron.Omschrijving };

            case Tekst tekst:
                return new Tekst { Tabblad = tekst.Tabblad, X = nieuwePositie.X, Y = nieuwePositie.Y, Inhoud = tekst.Inhoud };

            case Schakelaar schakelaar:
                return new Schakelaar { Tabblad = schakelaar.Tabblad, X = nieuwePositie.X, Y = nieuwePositie.Y, Omschrijving = schakelaar.Omschrijving, Aan = schakelaar.Aan };

            case Pijl pijl:
                return new Pijl { Tabblad = pijl.Tabblad, X = nieuwePositie.X, Y = nieuwePositie.Y, HoekGraden = pijl.HoekGraden };

            case Ontkoppelrail ontkoppelrail:
                int nieuwAdresOntkoppelrail = _baanBeheerder.Symbolen.OfType<Ontkoppelrail>().Select(o => o.Adres).DefaultIfEmpty(-1).Max() + 1;
                return new Ontkoppelrail { Tabblad = ontkoppelrail.Tabblad, X = nieuwePositie.X, Y = nieuwePositie.Y, Adres = nieuwAdresOntkoppelrail };

            case Bezetmelder bezetmelder:
                return new Bezetmelder { Tabblad = bezetmelder.Tabblad, X = nieuwePositie.X, Y = nieuwePositie.Y, GekoppeldBlok = bezetmelder.GekoppeldBlok, MeldernNummer = bezetmelder.MeldernNummer };

            case Driewegwissel driewegwissel:
                int nieuwAdresDriewegwissel = _baanBeheerder.Symbolen.OfType<Driewegwissel>().Select(w => w.Adres).DefaultIfEmpty(-1).Max() + 1;
                return new Driewegwissel { Tabblad = driewegwissel.Tabblad, X = nieuwePositie.X, Y = nieuwePositie.Y, Adres = nieuwAdresDriewegwissel, HoofdrichtingGraden = driewegwissel.HoofdrichtingGraden };

            case Kruiswissel kruiswissel:
                int nieuwAdresEngelseWissel = kruiswissel.IsEngels
                    ? _baanBeheerder.Symbolen.OfType<Kruiswissel>().Where(k => k.IsEngels).Select(k => k.Adres).DefaultIfEmpty(-1).Max() + 1
                    : 0;
                return new Kruiswissel { Tabblad = kruiswissel.Tabblad, X = nieuwePositie.X, Y = nieuwePositie.Y, HoofdrichtingGraden = kruiswissel.HoofdrichtingGraden, IsEngels = kruiswissel.IsEngels, Adres = nieuwAdresEngelseWissel };

            default:
                return null;
        }
    }

    // --- Hit-testing -------------------------------------------------------

    /// <summary>Ongedaan maken/opnieuw voor VERWIJDERDE symbolen - bewust een beperkte,
    /// veilige eerste versie (niet voor verplaatsen/andere bewerkingen): het teruggeven van
    /// exact hetzelfde, al bestaande object-exemplaar is de enige aanpak die geen risico
    /// loopt op referentie-corruptie (een JSON-snapshot/herstel van de Symbolen-lijst zou
    /// de ankerpunten naar Blok-objecten BUITEN die lijst - in _blokBeheerder.Blokken -
    /// verkeerd opnieuw opbouwen als losse kopieën in plaats van de echte, levende blokken).
    /// Nieuwe verwijdering maakt de Opnieuw-stack leeg (standaard undo/redo-gedrag).</summary>
    private readonly Stack<BaanSymbool> _ongedaanMakenStack = new();
    private readonly Stack<BaanSymbool> _opnieuwStack = new();

    private void OngedaanMaken_Click(object sender, RoutedEventArgs e) => OngedaanMaken();

    private void OpnieuwUitvoeren_Click(object sender, RoutedEventArgs e) => OpnieuwUitvoeren();

    /// <summary>Ctrl+Z: geeft het laatst verwijderde symbool weer terug. Werkt puur op basis
    /// van het teruggegeven, oorspronkelijke object-exemplaar - geen heropbouw, dus alle
    /// ankerpunten van/naar dit symbool blijven correct verbonden.</summary>
    private void OngedaanMaken()
    {
        if (_ongedaanMakenStack.Count == 0)
        {
            StatusTekst.Text = "Niets om ongedaan te maken.";
            return;
        }
        var symbool = _ongedaanMakenStack.Pop();
        _baanBeheerder.Symbolen.Add(symbool);
        _opnieuwStack.Push(symbool);
        _geselecteerdSymbool = symbool;
        StatusTekst.Text = "Verwijdering ongedaan gemaakt (Ctrl+Y om opnieuw te verwijderen).";
        Redraw();
    }

    /// <summary>Ctrl+Y: verwijdert het laatst-ongedaan-gemaakte symbool opnieuw.</summary>
    private void OpnieuwUitvoeren()
    {
        if (_opnieuwStack.Count == 0)
        {
            StatusTekst.Text = "Niets om opnieuw uit te voeren.";
            return;
        }
        var symbool = _opnieuwStack.Pop();
        if (symbool is Schakelaar schakelaar) _actieBeheerder.VergeetSchakelaar(schakelaar);
        _baanBeheerder.VerwijderSymbool(symbool);
        _ongedaanMakenStack.Push(symbool);
        if (_geselecteerdSymbool == symbool) _geselecteerdSymbool = null;
        StatusTekst.Text = "Verwijdering opnieuw uitgevoerd.";
        Redraw();
    }

    /// <summary>Zoekt het EXACTE traject (welke wissels/lijnen ertussen liggen) tussen
    /// twee SPECIFIEKE, opeenvolgende blokken van een rit - GEEN algemene "wat is er
    /// allemaal bereikbaar vanaf dit blok"-verkenning (dat bleek fundamenteel verkeerd,
    /// gebruikerscorrectie: kleurde ook takken/lijnen die niets met de daadwerkelijke rit
    /// te maken hadden, zelfs de niet-gebruikte kant van latere wissels in de keten).
    /// Start bij elke lijn verankerd aan 'van', volgt de KETEN door (via lijn-aan-lijn-
    /// ankers, en via wissels met dezelfde instroom/actieve-tak-beperking als eerder), en
    /// stopt zodra een lijn verankerd aan 'naar' gevonden wordt - dat exacte pad (en geen
    /// millimeter meer) is wat gekleurd moet worden. Retourneert null als er geen pad
    /// gevonden wordt (bijv. omdat de baan tussen deze twee blokken niet volledig met
    /// lijnen/ankers getekend is).</summary>
    private (HashSet<Wissel> Wissels, HashSet<Lijn> Lijnen)? VindCorridor(Blok van, Blok naar)
    {
        var alleLijnen = _baanBeheerder.Symbolen.OfType<Lijn>().ToList();
        var bezochteLijnen = new HashSet<Lijn>();
        var wachtrij = new Queue<(Lijn Lijn, List<Lijn> PadLijnen, List<Wissel> PadWissels)>();
        foreach (var startLijn in alleLijnen.Where(l => l.PuntBlokAnkers.Contains(van)))
        {
            bezochteLijnen.Add(startLijn);
            wachtrij.Enqueue((startLijn, new List<Lijn> { startLijn }, new List<Wissel>()));
        }

        while (wachtrij.Count > 0)
        {
            var (lijn, padLijnen, padWissels) = wachtrij.Dequeue();
            // Lokale, GROEIENDE kopie i.p.v. de oorspronkelijke padWissels rechtstreeks
            // gebruiken - BUG die hiermee gefixt wordt: als DEZE ENE lijn zowel een
            // wissel-anker (op een vroeger punt) als het bestemmingsblok-anker (op een
            // later punt) heeft, moet die net-gevonden wissel WEL meegenomen worden in het
            // teruggegeven resultaat - met de oorspronkelijke, onveranderde padWissels zou
            // die wissel er ten onrechte niet in zitten (gebruikersmelding: wisseltongen
            // kleuren niet mee, ondanks dat de aangrenzende lijnen wél kleurden).
            var padWisselsTotNu = new List<Wissel>(padWissels);
            for (int i = 0; i < lijn.Punten.Count; i++)
            {
                if (i < lijn.PuntBlokAnkers.Count && lijn.PuntBlokAnkers[i] == naar)
                    return (new HashSet<Wissel>(padWisselsTotNu), new HashSet<Lijn>(padLijnen)); // gevonden!

                if (i < lijn.PuntWisselAnkers.Count && lijn.PuntWisselAnkers[i] is Wissel wissel)
                {
                    padWisselsTotNu.Add(wissel);
                    // Alleen verder via lijnen die aan INSTROOM of de op dat moment
                    // ACTIEVE tak van deze wissel verankerd zijn - een trein kan de
                    // niet-ingestelde tak fysiek niet passeren. Welk van de 3 wisselpunten
                    // een verankering betreft staat niet los opgeslagen, dus geometrisch
                    // bepaald: welk van de 3 punten (instroom/rechtdoor/afbuigend) ligt
                    // het dichtst bij het aansluitende lijnpunt.
                    var wisselpunten = WisselAnkerpunten(wissel).ToList();
                    int actieveIndex = wissel.Stand == WisselStand.Rechtdoor ? 1 : 2;
                    foreach (var anderelijn in alleLijnen)
                    {
                        if (bezochteLijnen.Contains(anderelijn)) continue;
                        for (int j = 0; j < anderelijn.PuntWisselAnkers.Count && j < anderelijn.Punten.Count; j++)
                        {
                            if (anderelijn.PuntWisselAnkers[j] != wissel) continue;
                            var lijnpunt = anderelijn.Punten[j];
                            int dichtstbijzijndeIndex = 0;
                            double kleinsteAfstand = double.MaxValue;
                            for (int k = 0; k < wisselpunten.Count; k++)
                            {
                                double afstand = (wisselpunten[k] - lijnpunt).LengthSquared;
                                if (afstand < kleinsteAfstand) { kleinsteAfstand = afstand; dichtstbijzijndeIndex = k; }
                            }
                            if (dichtstbijzijndeIndex == 0 || dichtstbijzijndeIndex == actieveIndex)
                            {
                                bezochteLijnen.Add(anderelijn);
                                wachtrij.Enqueue((anderelijn, new List<Lijn>(padLijnen) { anderelijn }, new List<Wissel>(padWisselsTotNu)));
                            }
                            break;
                        }
                    }
                }
                else if (i < lijn.PuntLijnAnkers.Count && lijn.PuntLijnAnkers[i] is Lijn volgendeLijn && !bezochteLijnen.Contains(volgendeLijn))
                {
                    bezochteLijnen.Add(volgendeLijn);
                    wachtrij.Enqueue((volgendeLijn, new List<Lijn>(padLijnen) { volgendeLijn }, new List<Wissel>(padWisselsTotNu)));
                }
                // Een ander soort anker (of een ander blok dan 'naar'): doodlopend voor
                // deze zoektocht, geen actie nodig.
            }
        }
        return null;
    }

    /// <summary>Alle wissels/lijnen die op dit moment daadwerkelijk deel uitmaken van het
    /// exacte traject van een actieve reservering - berekend aan het BEGIN van elke
    /// Redraw (zie Redraw hieronder) via VindCorridor per (Van,Naar)-paar uit
    /// TreinrouteWindow.ActieveGereserveerdeTrajecten. Leeg als er geen TreinrouteWindow-
    /// verwijzing beschikbaar is (bijv. nog geen enkele rit ooit gestart).</summary>
    private readonly HashSet<Wissel> _gereserveerdeCorridorWissels = new();
    private readonly HashSet<Lijn> _gereserveerdeCorridorLijnen = new();

    private void BerekenGereserveerdeCorridors()
    {
        _gereserveerdeCorridorWissels.Clear();
        _gereserveerdeCorridorLijnen.Clear();
        var treinrouteWindow = _treinrouteWindowOpvragen?.Invoke();
        if (treinrouteWindow is null) return;
        foreach (var (van, naar) in treinrouteWindow.ActieveGereserveerdeTrajecten())
        {
            var corridor = VindCorridor(van, naar);
            if (corridor is null) continue;
            _gereserveerdeCorridorWissels.UnionWith(corridor.Value.Wissels);
            _gereserveerdeCorridorLijnen.UnionWith(corridor.Value.Lijnen);
        }
    }

    private Blok? BlokOpPositie(Point positie)
    {
        foreach (var blok in _blokBeheerder.Blokken)
        {
            if (_baanBeheerder.TabbladVanBlok(blok) != _huidigTabblad) continue;
            var p = _baanBeheerder.PositieVanBlok(blok);
            if (positie.X >= p.X && positie.X <= p.X + BlokGrootte &&
                positie.Y >= p.Y && positie.Y <= p.Y + BlokGrootte)
                return blok;
        }
        return null;
    }

    private const double AnkerSnapAfstand = 9; // kleiner en preciezer dan voorheen (was 12) - minder kans dat je per ongeluk aan een verkeerd, dichtbij ankerpunt vastklikt
    private const double LijnPuntHandleGrootte = 10; // zichtbare handle-grootte én klikmarge om 'm op te pakken
    private const double AnkerHandleGrootte = 6; // zichtbare ankerpuntjes op blokken/wissels - kleiner en duidelijker (was 8)

    /// <summary>De 4 ankerpunten van een blok: midden boven/onder/links/rechts van het vierkant.</summary>
    private IEnumerable<Point> BlokAnkerpunten(Blok blok)
    {
        var p = _baanBeheerder.PositieVanBlok(blok);
        double midX = p.X + BlokGrootte / 2, midY = p.Y + BlokGrootte / 2;
        // BEWUST NIET BlokCirkelStraal (die is puur voor de VISUELE grootte van het
        // rondje) - de ankerpunten hebben een EIGEN, functionele afstand nodig die een
        // veelvoud van RasterGrootte is, anders landen ze nooit op een rasterkruispunt
        // (gebruikersmelding: blokken snapten wel op het raster, maar op hun hoek/
        // buitenlijn, niet op de daadwerkelijke ankerpunten - net als bij de eerdere
        // wissel-rasterfix, maar dan voor blokken).
        double ankerAfstand = RasterGrootte;
        yield return new Point(midX, midY - ankerAfstand); // boven
        yield return new Point(midX, midY + ankerAfstand); // onder
        yield return new Point(midX - ankerAfstand, midY); // links
        yield return new Point(midX + ankerAfstand, midY); // rechts
    }

    /// <summary>De 3 ankerpunten van een wissel: instroom, rechtdoor-uitgang, afbuigende uitgang -
    /// dezelfde punten die ook de 3 pootjes van het wisselsymbool tekenen.</summary>
    /// <summary>Vertaalt een hoek (altijd een veelvoud van 45°, zie HoofdrichtingGraden)
    /// naar een RASTER-STAP i.p.v. een cos/sin-eenheidsvector - bijv. 0°→(1,0), 45°→(1,1),
    /// 90°→(0,1). Nodig voor WisselAnkerpunten hieronder: Math.Cos/Sin geeft bij een
    /// diagonale hoek (45/135/225/315°) een IRRATIONEEL getal (±1/√2) - een pootlengte
    /// gebaseerd op zo'n vermenigvuldiging landt daardoor vrijwel nooit exact op een
    /// rasterkruispunt, zelfs als het wisselcentrum zelf wél netjes op het raster staat
    /// (gebruikersmelding: "de wissels willen niet aan elkaar verankeren"). Met een
    /// RASTER-STAP (net als een koningszet bij schaken: bij een diagonale richting 1
    /// rasterstap in BEIDE assen tegelijk) landt een pootuiteinde altijd EXACT op het
    /// raster.</summary>
    private static (int Dx, int Dy) RasterRichtingsstap(double hoekGraden)
    {
        int index = (int)Math.Round(((hoekGraden % 360) + 360) % 360 / 45.0) % 8;
        return index switch
        {
            0 => (1, 0), 1 => (1, 1), 2 => (0, 1), 3 => (-1, 1),
            4 => (-1, 0), 5 => (-1, -1), 6 => (0, -1), _ => (1, -1)
        };
    }

    /// <summary>Aantal rastercellen per poot - de wissel past nu compact binnen 1 rastercel
    /// (net als het echte Koploper: instroom/rechtdoor/afbuigend op 3 van de 4 hoeken van
    /// die cel), i.p.v. eerder 2 cellen vanuit een centrum. MOET gelijk blijven aan
    /// dezelfde constante in BaanOntwerpBeheerder.cs.</summary>
    private const int WisselPootRastercellen = 1;

    /// <summary>wissel.X,Y is de INSTROOM-hoek zelf (GEEN centrum meer waar de poten
    /// symmetrisch vanaf stralen) - rechtdoor/afbuigend liggen dan op de andere 2 hoeken
    /// van dezelfde rastercel. Matcht hoe het echte Koploper een wissel compact weergeeft:
    /// bijv. een rechtsafbuigende wissel, horizontaal rechtdoor, heeft instroom
    /// linksboven, rechtdoor rechtsboven, afbuigend rechtsonder - de rechtdoor-poot is dan
    /// de bovenrand van de cel, de afbuigende poot de diagonaal linksboven-rechtsonder.</summary>
    private IEnumerable<Point> WisselAnkerpunten(Wissel wissel)
    {
        var (dx, dy) = RasterRichtingsstap(wissel.HoofdrichtingGraden);
        double afbuigHoekGraden = wissel.HoofdrichtingGraden + (wissel.AfbuigingLinksom ? -45 : 45);
        var (adx, ady) = RasterRichtingsstap(afbuigHoekGraden);
        double stap = WisselPootRastercellen * RasterGrootte;
        yield return new Point(wissel.X, wissel.Y);                          // instroom
        yield return new Point(wissel.X + dx * stap, wissel.Y + dy * stap);  // rechtdoor
        yield return new Point(wissel.X + adx * stap, wissel.Y + ady * stap); // afbuigend
    }

    /// <summary>Zoekt het dichtstbijzijnde ankerpunt (van een blok of wissel op het huidige
    /// tabblad) binnen snap-afstand. Retourneert (null, null, null) als er niets dichtbij is -
    /// dan wordt gewoon het geklikte punt zelf gebruikt, zonder verankering.</summary>
    /// <summary>De 4 (ongename, gelijkwaardige) eindpunten van een kruiswissel - dezelfde
    /// geometrie als de tekencode (2 sporen, 45° uit elkaar, HoofdrichtingGraden-roteerbaar).</summary>
    private IEnumerable<Point> KruiswisselAnkerpunten(Kruiswissel kruiswissel)
    {
        double halveLengte = SymboolGrootte * 0.9;
        foreach (double hoekOffset in new[] { 0.0, 45.0 })
        {
            double hoekRad = (kruiswissel.HoofdrichtingGraden + hoekOffset) * Math.PI / 180.0;
            var richting = new Vector(Math.Cos(hoekRad), Math.Sin(hoekRad));
            yield return new Point(kruiswissel.X - richting.X * halveLengte, kruiswissel.Y - richting.Y * halveLengte);
            yield return new Point(kruiswissel.X + richting.X * halveLengte, kruiswissel.Y + richting.Y * halveLengte);
        }
    }

    private (Point? Positie, Blok? Blok, Wissel? Wissel, Lijn? Lijn, int LijnPuntIndex, Kruiswissel? Kruiswissel, int KruiswisselPuntIndex) DichtstbijzijndAnkerpunt(Point positie)
    {
        Point? beste = null;
        Blok? besteBlok = null;
        Wissel? besteWissel = null;
        Lijn? besteLijn = null;
        int besteLijnIndex = -1;
        Kruiswissel? besteKruiswissel = null;
        int besteKruiswisselIndex = -1;
        double kleinsteAfstand = AnkerSnapAfstand;

        foreach (var blok in _blokBeheerder.Blokken)
        {
            if (_baanBeheerder.TabbladVanBlok(blok) != _huidigTabblad) continue;
            foreach (var anker in BlokAnkerpunten(blok))
            {
                double afstand = (positie - anker).Length;
                if (afstand < kleinsteAfstand) { kleinsteAfstand = afstand; beste = anker; besteBlok = blok; besteWissel = null; besteLijn = null; besteLijnIndex = -1; besteKruiswissel = null; besteKruiswisselIndex = -1; }
            }
        }

        foreach (var wissel in _baanBeheerder.Symbolen.OfType<Wissel>().Where(w => w.Tabblad == _huidigTabblad))
        {
            foreach (var anker in WisselAnkerpunten(wissel))
            {
                double afstand = (positie - anker).Length;
                if (afstand < kleinsteAfstand) { kleinsteAfstand = afstand; beste = anker; besteBlok = null; besteWissel = wissel; besteLijn = null; besteLijnIndex = -1; besteKruiswissel = null; besteKruiswisselIndex = -1; }
            }
        }

        foreach (var kruiswissel in _baanBeheerder.Symbolen.OfType<Kruiswissel>().Where(k => k.Tabblad == _huidigTabblad))
        {
            int puntIndex = 0;
            foreach (var anker in KruiswisselAnkerpunten(kruiswissel))
            {
                double afstand = (positie - anker).Length;
                if (afstand < kleinsteAfstand) { kleinsteAfstand = afstand; beste = anker; besteBlok = null; besteWissel = null; besteLijn = null; besteLijnIndex = -1; besteKruiswissel = kruiswissel; besteKruiswisselIndex = puntIndex; }
                puntIndex++;
            }
        }

        // Lijnen mogen ook rechtstreeks aan elkaar vastzitten - elk punt van elke bestaande
        // lijn is ook een geldig ankerpunt voor een nieuw lijnpunt.
        foreach (var lijn in _baanBeheerder.Symbolen.OfType<Lijn>().Where(l => l.Tabblad == _huidigTabblad))
        {
            for (int i = 0; i < lijn.Punten.Count; i++)
            {
                double afstand = (positie - lijn.Punten[i]).Length;
                if (afstand < kleinsteAfstand) { kleinsteAfstand = afstand; beste = lijn.Punten[i]; besteBlok = null; besteWissel = null; besteLijn = lijn; besteLijnIndex = i; besteKruiswissel = null; besteKruiswisselIndex = -1; }
            }
        }

        return (beste, besteBlok, besteWissel, besteLijn, besteLijnIndex, besteKruiswissel, besteKruiswisselIndex);
    }

    /// <summary>Klapt een nieuw lijnpunt vast op de dichtstbijzijnde hoek van 45° t.o.v. het
    /// vorige punt (horizontaal/verticaal/diagonaal) - net als de wissels nu ook alleen in
    /// 8 richtingen staan. Alleen toegepast als het punt NIET al op een ankerpunt vastklikte
    /// (een echt ankerpunt - blok/wissel/andere lijn - gaat altijd voor de hoek-regel).</summary>
    private static Point KlapVastOp45Graden(Point vorige, Point ruw)
    {
        var verschil = ruw - vorige;
        if (verschil.Length < 0.01) return ruw;

        double hoek = Math.Atan2(verschil.Y, verschil.X);
        double stap = Math.PI / 4;
        double gekliktehoek = Math.Round(hoek / stap) * stap;
        var richting = new Vector(Math.Cos(gekliktehoek), Math.Sin(gekliktehoek));
        return vorige + richting * verschil.Length;
    }

    /// <summary>Ligt punt B op een veelvoud van 45° gezien vanaf punt A? (met een kleine
    /// tolerantie). Gebruikt om te bepalen of een ankerpunt wel/niet bruikbaar is voor een
    /// lijnsegment: een ankerpunt dat niet op zo'n hoek ligt, mag een lijn NIET rechtstreeks
    /// verbinden - dan moet je in plaats daarvan een tweede, wél op 45° aansluitend segment
    /// toevoegen, net als bij het echte spoorontwerp.</summary>
    private static bool HoekIsVeelvoudVan45(Point a, Point b)
    {
        var verschil = b - a;
        if (verschil.Length < 0.5) return true; // vrijwel hetzelfde punt: geen zinvolle hoek, niet blokkeren
        double hoek = Math.Atan2(verschil.Y, verschil.X);
        double stap = Math.PI / 4;
        double afwijking = Math.Abs(hoek - Math.Round(hoek / stap) * stap);
        const double tolerantieGraden = 3;
        return afwijking <= tolerantieGraden * Math.PI / 180;
    }

    /// <summary>Zoekt of er een specifiek LIJNPUNT (van welke lijn dan ook, op het huidige
    /// tabblad) vlak bij deze positie ligt - een strakkere marge dan de algemene ankersnap,
    /// zodat je een los eindpunt precies kunt oppakken zonder de hele lijn te verslepen.</summary>
    private (Lijn? Lijn, int Index) LijnPuntOpPositie(Point positie)
    {
        const double puntKlikStraal = LijnPuntHandleGrootte; // even groot als de zichtbare handle, zodat je precies raakt wat je ziet
        foreach (var lijn in _baanBeheerder.Symbolen.OfType<Lijn>().Where(l => l.Tabblad == _huidigTabblad))
        {
            for (int i = 0; i < lijn.Punten.Count; i++)
                if ((positie - lijn.Punten[i]).Length <= puntKlikStraal)
                    return (lijn, i);
        }
        return (null, -1);
    }

    /// <summary>GEVONDEN GAT (gebruikerswaarneming: "de kruiswissel wordt niet paars, er
    /// komen lijntjes bij" - ook NA het toevoegen van Kruiswissel-ondersteuning aan de
    /// Wisselstraat-tool): dit doorliep Symbolen voorheen in OPSLAGVOLGORDE en gaf het
    /// EERSTE gevonden treffer terug, ongeacht of dat een symbool (Blok/Wissel/Kruiswissel/
    /// Sein) of een lijn was. Bij een kruiswissel komen typisch VIER lijnen samen, vlak bij
    /// of zelfs door het symbool heen - stond zo'n lijn toevallig eerder in de lijst, dan
    /// "won" die lijn altijd, ook als de klik dichter bij (of zelfs precies op) de
    /// kruiswissel zelf lag. Nu EERST alle symbolen (niet-lijnen) proberen - preciezer,
    /// bewust bedoeld om aangeklikt te worden - en alleen als GEEN daarvan matcht alsnog
    /// naar lijnen kijken. Dit lost het niet alleen voor de Wisselstraat-tool op, maar voor
    /// ELKE plek die op een symbool klikt (dubbelklikken, slepen, enz.).</summary>
    private BaanSymbool? SymboolOpPositie(Point positie)
    {
        foreach (var symbool in _baanBeheerder.Symbolen)
        {
            if (symbool.Tabblad != _huidigTabblad) continue;
            if (symbool is Lijn) continue; // eerste ronde: alleen echte symbolen
            if (Math.Abs(positie.X - symbool.X) < SymboolGrootte / 2 + 4 &&
                Math.Abs(positie.Y - symbool.Y) < SymboolGrootte / 2 + 4)
                return symbool;
        }
        foreach (var symbool in _baanBeheerder.Symbolen)
        {
            if (symbool.Tabblad != _huidigTabblad) continue;
            if (symbool is Lijn lijn && AfstandTotLijn(positie, lijn) < 6) return lijn;
        }
        return null;
    }

    private static double AfstandTotLijn(Point p, Lijn lijn)
    {
        if (lijn.Punten.Count < 2) return double.MaxValue;

        double kleinsteAfstand = double.MaxValue;
        for (int i = 0; i < lijn.Punten.Count - 1; i++)
            kleinsteAfstand = Math.Min(kleinsteAfstand, AfstandTotSegment(p, lijn.Punten[i], lijn.Punten[i + 1]));
        return kleinsteAfstand;
    }

    private static double AfstandTotSegment(Point p, Point a, Point b)
    {
        var ab = b - a;
        double lengteKwadraat = ab.LengthSquared;
        if (lengteKwadraat < 0.0001) return (p - a).Length;

        double t = Math.Max(0, Math.Min(1, Vector.Multiply(p - a, ab) / lengteKwadraat));
        var projectie = a + ab * t;
        return (p - projectie).Length;
    }

    // --- Tekenen -------------------------------------------------------

    private const double RasterGrootte = 16;

    private void ToonRaster_Changed(object sender, RoutedEventArgs e) => Redraw();

    /// <summary>Rondt een punt af op het raster als "Uitlijnen op raster" aanstaat - net als
    /// in de handleiding beschreven ("de meeste objecten worden netjes op een raster
    /// uitgelijnd"). Anders wordt het punt ongewijzigd teruggegeven.</summary>
    private Point UitlijnenIndienNodig(Point p)
    {
        if (UitlijnenOpRasterBox.IsChecked != true) return p;
        return new Point(Math.Round(p.X / RasterGrootte) * RasterGrootte, Math.Round(p.Y / RasterGrootte) * RasterGrootte);
    }

    private void TekenRaster()
    {
        if (ToonRasterBox.IsChecked != true) return;

        double breedte = BaanCanvas.ActualWidth > 0 ? BaanCanvas.ActualWidth : 1400;
        double hoogte = BaanCanvas.ActualHeight > 0 ? BaanCanvas.ActualHeight : 800;

        for (double x = 0; x < breedte; x += RasterGrootte)
            BaanCanvas.Children.Add(new Line { X1 = x, Y1 = 0, X2 = x, Y2 = hoogte, Stroke = Brushes.Gainsboro, StrokeThickness = 1, IsHitTestVisible = false });
        for (double y = 0; y < hoogte; y += RasterGrootte)
            BaanCanvas.Children.Add(new Line { X1 = 0, Y1 = y, X2 = breedte, Y2 = y, Stroke = Brushes.Gainsboro, StrokeThickness = 1, IsHitTestVisible = false });
    }

    private void Redraw()
    {
        BaanCanvas.Children.Clear();
        BerekenGereserveerdeCorridors(); // reservering-status kan gewijzigd zijn sinds de vorige tekenbeurt

        // Donkere modus: alleen zinvol in het kijkscherm (in bewerk-modus wil je juist de
        // vertrouwde, heldere werkbalk-kleuren houden voor precisiewerk).
        BaanCanvas.Background = _kijkModus && SimulatieInstellingen.DonkereModus
            ? new SolidColorBrush(Color.FromRgb(24, 24, 28))
            : Brushes.White;

        TekenRaster();

        // Lijnen eerst (liggen "onder" de blokken), dan de blokken, en dan pas de overige
        // symbolen (wissels, seinen, perrons, ...) zodat die nooit achter een blok verdwijnen.
        // Alleen het huidige tabblad wordt getoond.
        foreach (var symbool in _baanBeheerder.Symbolen.OfType<Lijn>().Where(s => s.Tabblad == _huidigTabblad))
            TekenSymbool(symbool);

        foreach (var blok in _blokBeheerder.Blokken.Where(b => _baanBeheerder.TabbladVanBlok(b) == _huidigTabblad))
            TekenBlok(blok);

        foreach (var symbool in _baanBeheerder.Symbolen.Where(s => s.Tabblad == _huidigTabblad))
            if (symbool is not Lijn) TekenSymbool(symbool);

        if (_lijnInAanleg != null && _lijnInAanleg.Punten.Count > 0)
        {
            // preview: de lijn-in-aanleg zelf (als losse polylijn, want hij staat nog niet
            // in Symbolen) plus een rood puntje op elk al geplaatst punt
            var preview = new Polyline
            {
                Points = new PointCollection(_lijnInAanleg.Punten),
                Stroke = Brushes.DimGray,
                StrokeDashArray = new DoubleCollection { 4, 2 },
                StrokeThickness = 2,
                IsHitTestVisible = false
            };
            BaanCanvas.Children.Add(preview);

            foreach (var punt in _lijnInAanleg.Punten)
            {
                var marker = new Ellipse { Width = 6, Height = 6, Fill = Brushes.Red, IsHitTestVisible = false };
                Canvas.SetLeft(marker, punt.X - 3);
                Canvas.SetTop(marker, punt.Y - 3);
                BaanCanvas.Children.Add(marker);
            }
        }
    }

    /// <summary>Straal van het kleine ronde blokmarkeringetje ZOALS GETEKEND (echte Koploper-
    /// baanoverzichten tonen blokken als kleine cirkels op de lijn, niet als grote vierkanten -
    /// zie "Onderhouden baanoverzicht" in de handleiding). BlokGrootte zelf blijft de logische
    /// klik-/sleepgrootte, voor het gemak iets ruimer dan wat je daadwerkelijk ziet.
    /// 14 (i.p.v. de eerdere 10.5) geeft genoeg breedte voor een 4-cijferig decodernummer
    /// (bijv. "9999", het praktische maximum) - LOSGEKOPPELD van BlokAnkerpunten's eigen,
    /// functionele ankerAfstand (die blijft RasterGrootte, ongeacht deze visuele straal).</summary>
    private const double BlokCirkelStraal = 14;

    /// <summary>Bouwt de tekst voor het "beweeg hier je muis"-informatievenstertje op een
    /// blok - zoals de handleiding beschrijft dat je bij het echte baanoverzicht met de
    /// muis over een blok kunt gaan (of rechtsklikken) om info op te vragen.</summary>
    private static string BlokTooltipTekst(Blok blok, bool bezet, bool handmatig, bool staart, bool gereserveerd, bool foutmelding)
    {
        string status = foutmelding ? "foutmelding"
            : handmatig ? "handmatig bezet"
            : staart ? "staartindicatie"
            : bezet ? "bezet"
            : gereserveerd ? "gereserveerd"
            : "vrij";

        var regels = new List<string> { $"Blok {blok.Nummer}" };
        if (!string.IsNullOrWhiteSpace(blok.Omschrijving)) regels.Add(blok.Omschrijving);
        regels.Add($"Type: {blok.Type}");
        regels.Add($"Status: {status}");
        regels.Add(blok.Bezetmeldpunten.Count == 0
            ? "Geen bezetmeldpunten ingesteld"
            : "Bezetmeldpunten: " + string.Join(", ", blok.Bezetmeldpunten.Select(m => $"{m.MeldernNummer} ({m.Rol})")));
        return string.Join("\n", regels);
    }

    /// <summary>Zorgt dat een Kopspoor-blok een stootblok heeft aan het einde van het spoor -
    /// zelfde logica als MainWindow.ZorgVoorUitgaandSein, hier ook nodig omdat je vanuit het
    /// baanontwerp zelf nu ook Eigenschappen blok kunt openen en een blok op Kopspoor kunt zetten.</summary>
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

    /// <summary>Zoekt de lijn die aan dit blok verankerd is en gebruikt de richting van dát
    /// lijnsegment (vanaf het naastgelegen punt naar het ankerpunt, dus de richting van de
    /// rijrichting het kopspoor in) om te bepalen waar het stootblok moet komen: net voorbij
    /// het ankerpunt, in het verlengde van het spoor. Valt terug op de oude
    /// blokmiddelpunt-tot-blokmiddelpunt-aanpak als er nog geen lijn aan dit blok hangt
    /// (bijv. bij een gloednieuw blok waar het spoor nog getekend moet worden).</summary>
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
        var inkomendeRelatie = _blokBeheerder.Relaties.FirstOrDefault(r => r.Naar == blok);
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

    private void TekenBlok(Blok blok)
    {
        var p = _baanBeheerder.PositieVanBlok(blok);
        var centrum = new Point(p.X + BlokGrootte / 2, p.Y + BlokGrootte / 2);
        bool geselecteerd = blok == _koppelGeselecteerdBlok || blok == _wisselstraatVanKandidaat ||
                             blok == _wisselstraatActief?.Van || blok == _wisselstraatActief?.Naar;
        bool bezet = _blokBeheerder.IsBezet(blok);
        bool handmatig = _blokBeheerder.IsHandmatigBezet(blok);
        bool staart = _blokBeheerder.IsStaart(blok);
        bool gereserveerd = _blokBeheerder.IsGereserveerd(blok);
        bool foutmelding = _blokBeheerder.IsFoutmelding(blok) && _blokBeheerder.FoutmeldingKnipperAan;
        bool netVrijgegeven = _blokBeheerder.IsNetVrijgegeven(blok) && !bezet && !gereserveerd;

        // Klein zwart(-achtig) blokje met het nummer erin, net als het echte Koploper
        // ("Het gele nummer in een zwart blokje... geeft wel het idee weer van een Domino
        // tableau") - i.p.v. het eerdere rondje met het nummer los ernaast. Staat een loc
        // op dit blok, dan toont het blokje het LOCDECODER-nummer i.p.v. het bloknummer
        // (dezelfde conventie als het blokkenschema-venster: Koploper toont nooit een
        // treinnaam in het blokje, alleen het decodernummer).
        var locOpBlok = _blokBeheerder.LocOpBlok(blok);
        string blokjeTekst = locOpBlok != null
            ? (locOpBlok.DecoderAdres > 0 ? locOpBlok.DecoderAdres.ToString() : locOpBlok.Omschrijving)
            : blok.Nummer.ToString();
        Brush blokjeTekstKleur = locOpBlok != null ? Brushes.Gold
            : (foutmelding || handmatig || staart || bezet) ? Brushes.White
            : Brushes.Black;
        string blokjeToolTip = BlokTooltipTekst(blok, bezet, handmatig, staart, gereserveerd, foutmelding)
            + (blok.Vergrendeld ? $"\nVergrendeld{(string.IsNullOrWhiteSpace(blok.VergrendelReden) ? "" : $": {blok.VergrendelReden}")}" : "")
            + (locOpBlok != null ? $"\nLoc: {locOpBlok.Omschrijving}" + (locOpBlok.DecoderAdres > 0 ? $" (decoder {locOpBlok.DecoderAdres})" : "") : "");

        var blokje = new Rectangle
        {
            Width = BlokCirkelStraal * 2,
            Height = BlokCirkelStraal * 2,
            Fill = foutmelding ? KoploperKleuren.Foutmelding : handmatig ? KoploperKleuren.HandmatigBezet : staart ? KoploperKleuren.Staartindicatie : bezet ? KoploperKleuren.Bezet : gereserveerd ? KoploperKleuren.Gereserveerd : netVrijgegeven ? KoploperKleuren.NetVrijgegeven : blok.Vergrendeld ? KoploperKleuren.Vergrendeld : KoploperKleuren.VrijBlokMarkering,
            Stroke = geselecteerd ? KoploperKleuren.Geselecteerd : Brushes.Black,
            StrokeThickness = geselecteerd ? 3 : 1.5,
            ToolTip = blokjeToolTip
        };
        Canvas.SetLeft(blokje, centrum.X - BlokCirkelStraal);
        Canvas.SetTop(blokje, centrum.Y - BlokCirkelStraal);
        BaanCanvas.Children.Add(blokje);

        var blokjeLabel = new TextBlock
        {
            Text = blokjeTekst,
            FontSize = blokjeTekst.Length > 2 ? 9 : 11,
            FontWeight = FontWeights.Bold,
            Foreground = blokjeTekstKleur,
            IsHitTestVisible = false,
            TextAlignment = TextAlignment.Center,
            Width = BlokCirkelStraal * 2
        };
        Canvas.SetLeft(blokjeLabel, centrum.X - BlokCirkelStraal);
        Canvas.SetTop(blokjeLabel, centrum.Y - (blokjeTekst.Length > 2 ? 6 : 7));
        BaanCanvas.Children.Add(blokjeLabel);

        if (blok.Type != BlokType.Normaal)
        {
            var typeLabel = new TextBlock
            {
                Text = blok.Type switch { BlokType.Kopspoor => "K", BlokType.Station => "S", _ => "O" },
                FontSize = 8,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.DarkSlateBlue,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(typeLabel, centrum.X - BlokCirkelStraal - 2);
            Canvas.SetTop(typeLabel, centrum.Y - BlokCirkelStraal - 12);
            BaanCanvas.Children.Add(typeLabel);
        }

        // Zichtbare ankerpunten (net als bij lijn-eindpunten) - alleen in Verplaatsen, waar
        // je er ook echt een lijn naartoe kunt slepen. Zo hoef je niet te gokken waar precies
        // het ankerpunt zit als er meerdere dicht bij elkaar liggen. In kijk-modus altijd
        // onzichtbaar - daar kan toch niets versleept worden, dus zijn ze puur ruis.
        if (_huidigTool == Tool.Verplaatsen && !_kijkModus)
        {
            foreach (var anker in BlokAnkerpunten(blok))
            {
                var ankerHandle = new Rectangle
                {
                    Width = AnkerHandleGrootte,
                    Height = AnkerHandleGrootte,
                    Fill = Brushes.DeepSkyBlue,
                    Stroke = Brushes.Black,
                    StrokeThickness = 1
                };
                Canvas.SetLeft(ankerHandle, anker.X - AnkerHandleGrootte / 2);
                Canvas.SetTop(ankerHandle, anker.Y - AnkerHandleGrootte / 2);
                BaanCanvas.Children.Add(ankerHandle);
            }
        }
    }

    private void TekenSymbool(BaanSymbool symbool)
    {
        bool geselecteerd = symbool == _geselecteerdSymbool;

        switch (symbool)
        {
            case Lijn lijn when lijn.Punten.Count >= 2:
                bool lijnInWisselstraat = _wisselstraatActief?.Lijnen.Contains(lijn) == true;
                bool lijnBezet = lijn.GekoppeldBlok != null && _blokBeheerder.IsBezet(lijn.GekoppeldBlok);
                // Gereserveerd: (1) de gewone check via het eigen GekoppeldBlok van de lijn,
                // en (2), als aanvulling, via het ankerpuntennetwerk - een tussenliggende
                // of afbuigende lijn heeft namelijk lang niet altijd zelf een GekoppeldBlok
                // ingesteld (in een echte baan bleek dit voor zo'n 30% van de lijnen zo te
                // zijn), maar hoort qua rails-verbinding wel degelijk bij een gereserveerd
                // blok - zonder deze aanvulling blijft zo'n lijn dan onterecht "gewoon zwart".
                bool lijnGereserveerdViaCorridor = lijn.GekoppeldBlok is null && _gereserveerdeCorridorLijnen.Contains(lijn);
                bool lijnGereserveerd = (lijn.GekoppeldBlok != null && _blokBeheerder.IsGereserveerd(lijn.GekoppeldBlok)) || lijnGereserveerdViaCorridor;
                bool lijnNetVrijgegeven = lijn.GekoppeldBlok != null && _blokBeheerder.IsNetVrijgegeven(lijn.GekoppeldBlok) && !lijnBezet && !lijnGereserveerd;
                // Tijdens Koppelen-tool met een geselecteerd blok: onderscheid maken tussen
                // "gekoppeld aan DIT geselecteerde blok" (fel groen) en "gekoppeld aan een
                // ANDER blok" (het gewone oranje) - anders lijkt het net of elke koppeling
                // zich bij het nieuw geselecteerde blok voegt, terwijl je gewoon een
                // eerdere, andere koppeling nog ziet staan.
                bool lijnGekoppeldAanHuidigeSelectie = _huidigTool == Tool.Koppelen && _koppelGeselecteerdBlok != null && lijn.GekoppeldBlok == _koppelGeselecteerdBlok;
                var lijnVorm = new Polyline
                {
                    Points = new PointCollection(lijn.Punten),
                    Stroke = lijnBezet ? KoploperKleuren.Bezet
                        : lijnGereserveerd ? KoploperKleuren.Gereserveerd
                        : lijnNetVrijgegeven ? KoploperKleuren.NetVrijgegeven
                        // De oranje/paars/groene koppel-/bewerk-indicatoren zijn puur
                        // hulpmiddelen tijdens het TEKENEN (welke lijn hoort bij welk blok/
                        // wisselstraat) - in kijk-modus is er niets te bewerken, dus die
                        // kleuren gewoon overslaan. Zo vallen de echte rijstatussen
                        // (bezet/gereserveerd/net vrijgegeven) veel beter op tegen een
                        // rustige zwarte baan.
                        : _kijkModus ? (geselecteerd ? KoploperKleuren.Geselecteerd : SimulatieInstellingen.DonkereModus ? Brushes.Gainsboro : KoploperKleuren.SpoorLijn)
                        : lijnInWisselstraat ? Brushes.MediumPurple
                        : lijnGekoppeldAanHuidigeSelectie ? Brushes.LimeGreen
                        : lijn.GekoppeldBlok != null ? Brushes.DarkOrange
                        : geselecteerd ? KoploperKleuren.Geselecteerd : KoploperKleuren.SpoorLijn,
                    StrokeThickness = lijnBezet || lijnGereserveerd || lijnNetVrijgegeven ? 4 : 3
                };
                BaanCanvas.Children.Add(lijnVorm);

                // Klein volgnummer bij het midden van de lijn - puur informatief, net als
                // in het echte Koploper ("De nummering van de lijntjes heeft niets te maken
                // met de bloknummers, dit is puur informatief om de lijntjes van elkaar te
                // kunnen onderscheiden"). Niet in kijk-modus: daar moet de baan juist rustig
                // ogen, en heeft dit label geen enkele functie.
                if (!_kijkModus && lijn.Volgnummer > 0)
                {
                    // Geometrisch midden (gemiddelde van alle punten) i.p.v. een index-
                    // gebaseerde benadering - werkt ook netjes bij een lijn met meer dan 2
                    // punten (bijv. na het uitrekken/verankeren aan andere lijnen).
                    var lijnMidden = new Point(lijn.Punten.Average(p => p.X), lijn.Punten.Average(p => p.Y));
                    var lijnNummerLabel = new TextBlock
                    {
                        Text = lijn.Volgnummer.ToString(),
                        FontSize = 8,
                        Foreground = Brushes.Gray,
                        IsHitTestVisible = false
                    };
                    Canvas.SetLeft(lijnNummerLabel, lijnMidden.X + 3);
                    Canvas.SetTop(lijnNummerLabel, lijnMidden.Y - 12);
                    BaanCanvas.Children.Add(lijnNummerLabel);
                }

                // Losse eindpunt-handles, zichtbaar en aanklikbaar - net als de echte
                // ankerpunt-handeltjes in Koploper's baanontwerp. Alleen getoond in de
                // Verplaatsen-tool, waar je ze ook echt kunt oppakken (zie StartVerplaatsen).
                if (_huidigTool == Tool.Verplaatsen && !_kijkModus)
                {
                    for (int i = 0; i < lijn.Punten.Count; i++)
                    {
                        bool wordtGesleept = _gesleeptLijnPunt == lijn && _gesleeptLijnPuntIndex == i;
                        bool heeftAnker = (i < lijn.PuntBlokAnkers.Count && lijn.PuntBlokAnkers[i] != null)
                            || (i < lijn.PuntWisselAnkers.Count && lijn.PuntWisselAnkers[i] != null)
                            || (i < lijn.PuntLijnAnkers.Count && lijn.PuntLijnAnkers[i] != null)
                            || (i < lijn.PuntKruiswisselAnkers.Count && lijn.PuntKruiswisselAnkers[i] != null);
                        var handle = new Rectangle
                        {
                            Width = LijnPuntHandleGrootte,
                            Height = LijnPuntHandleGrootte,
                            Fill = wordtGesleept ? Brushes.OrangeRed : heeftAnker ? Brushes.LimeGreen : Brushes.Red,
                            Stroke = Brushes.Black,
                            StrokeThickness = 1
                        };
                        Canvas.SetLeft(handle, lijn.Punten[i].X - LijnPuntHandleGrootte / 2);
                        Canvas.SetTop(handle, lijn.Punten[i].Y - LijnPuntHandleGrootte / 2);
                        BaanCanvas.Children.Add(handle);
                    }
                }
                break;

            case Wissel wissel:
                bool wisselInWisselstraat = _wisselstraatActief?.Wissels.Any(w => w.Wissel == wissel) == true;
                // GEBRUIKERSWAARNEMING ("deze wissel staat soms als gereserveerd terwijl de
                // trein niet eens in de buurt is"): wissel5 zit zowel in de "7->3"- als de
                // "4->3"-Wisselstraat. De oudere Wisselstraat-brede check hieronder toonde
                // 'm als gereserveerd zodra ÉÉN van de twee eindpunten van ÉÉN van beide
                // Wisselstraten gereserveerd was - ook als de trein via de ANDERE
                // Wisselstraat reed, en deze wissel daar dus niets mee te maken had. De
                // corridor-berekening hieronder (VindCorridor/BerekenGereserveerdeCorridors)
                // is exact, per-traject - dat volstaat op zichzelf en is de enige die nog
                // gebruikt wordt.
                bool wisselViaCorridorGereserveerd = _gereserveerdeCorridorWissels.Contains(wissel);
                bool wisselGereserveerd = !wisselInWisselstraat && wisselViaCorridorGereserveerd;
                bool wisselHeeftAccent = wisselInWisselstraat || wisselGereserveerd || geselecteerd;
                var wisselAccentKleur = wisselInWisselstraat ? Brushes.MediumPurple
                    : wisselGereserveerd ? KoploperKleuren.Gereserveerd
                    : KoploperKleuren.Geselecteerd; // (alleen relevant als wisselHeeftAccent, dus hier komt alleen 'geselecteerd' nog in aanmerking)

                // Compacte, Koploper-geïnspireerde weergave: de wissel past in 1 rastercel,
                // instroom/rechtdoor/afbuigend op 3 van de 4 hoeken (wissel.X,Y = instroom
                // zelf, zie WisselAnkerpunten). Bijv. een rechtsafbuigende wissel, horizontaal
                // rechtdoor: instroom linksboven, rechtdoor rechtsboven (bovenrand van de
                // cel), afbuigend rechtsonder (diagonaal linksboven-rechtsonder).
                var (wisselDx, wisselDy) = RasterRichtingsstap(wissel.HoofdrichtingGraden);
                double wisselAfbuigHoek = wissel.HoofdrichtingGraden + (wissel.AfbuigingLinksom ? -45 : 45);
                var (wisselAdx, wisselAdy) = RasterRichtingsstap(wisselAfbuigHoek);
                double wisselStap = WisselPootRastercellen * RasterGrootte;
                var rechtdoorPunt = new Point(wissel.X + wisselDx * wisselStap, wissel.Y + wisselDy * wisselStap);
                var afbuigendPunt = new Point(wissel.X + wisselAdx * wisselStap, wissel.Y + wisselAdy * wisselStap);

                // Zie Model.Wissel.OmgekeerdeWeergave: puur voor DIT getekende symbool, de
                // "echte" wissel.Stand (bron van waarheid voor wisselstraten/hardware) blijft
                // ongemoeid - alleen welke poot hier als "actief" getekend wordt, wordt omgedraaid.
                var wisselWeergaveStand = wissel.OmgekeerdeWeergave
                    ? (wissel.Stand == WisselStand.Rechtdoor ? WisselStand.Afbuigend : WisselStand.Rechtdoor)
                    : wissel.Stand;

                // Overloopwissel-koppeling zichtbaar maken: een dunne paarse stippellijn
                // tussen de twee gekoppelde wissels - alleen getekend vanuit de wissel met
                // het laagste adres, anders zou elk paar twee keer (over elkaar) getekend worden.
                if (wissel.GekoppeldeOverloopwissel != null && wissel.Adres <= wissel.GekoppeldeOverloopwissel.Adres)
                {
                    BaanCanvas.Children.Add(new Line
                    {
                        X1 = wissel.X, Y1 = wissel.Y,
                        X2 = wissel.GekoppeldeOverloopwissel.X, Y2 = wissel.GekoppeldeOverloopwissel.Y,
                        Stroke = Brushes.MediumPurple, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 3, 3 },
                        IsHitTestVisible = false,
                        ToolTip = $"Overloopwissel-koppeling: wissel {wissel.Adres} + {wissel.GekoppeldeOverloopwissel.Adres} zetten altijd samen om."
                    });
                }

                // Rechtdoor-poot: "in ruste"-kleur (lichtblauw) als dit de huidige Stand is
                // en er geen reservering/bezetting is; de reservering/bezet-kleur als die er
                // wél is; anders de neutrale, niet-actieve kleur (de rail is er fysiek wel,
                // alleen niet de gekozen weg).
                BaanCanvas.Children.Add(new Line
                {
                    X1 = wissel.X, Y1 = wissel.Y,
                    X2 = rechtdoorPunt.X, Y2 = rechtdoorPunt.Y,
                    Stroke = wisselWeergaveStand == WisselStand.Rechtdoor ? (wisselHeeftAccent ? wisselAccentKleur : KoploperKleuren.WisselInRuste) : KoploperKleuren.WisselNietActievePoot,
                    StrokeThickness = wisselWeergaveStand == WisselStand.Rechtdoor ? 3 : 1,
                    IsHitTestVisible = false
                });

                // Afbuigende poot: zelfde principe, als rechte diagonaal (net als het echte
                // Koploper: een rechte lijn van hoek naar hoek, geen curve).
                BaanCanvas.Children.Add(new Line
                {
                    X1 = wissel.X, Y1 = wissel.Y,
                    X2 = afbuigendPunt.X, Y2 = afbuigendPunt.Y,
                    Stroke = wisselWeergaveStand == WisselStand.Afbuigend ? (wisselHeeftAccent ? wisselAccentKleur : KoploperKleuren.WisselInRuste) : KoploperKleuren.WisselNietActievePoot,
                    StrokeThickness = wisselWeergaveStand == WisselStand.Afbuigend ? 3 : 1,
                    IsHitTestVisible = false
                });

                // Klein knooppuntje op de instroom-hoek, voor duidelijke aanklikbaarheid.
                var wisselHart = new Ellipse
                {
                    Width = 8, Height = 8,
                    Fill = wisselHeeftAccent ? wisselAccentKleur : KoploperKleuren.WisselInRuste
                };
                Canvas.SetLeft(wisselHart, wissel.X - 4);
                Canvas.SetTop(wisselHart, wissel.Y - 4);
                BaanCanvas.Children.Add(wisselHart);

                // "Altijd initialiseren" (Ctrl+rechtsklik) zichtbaar maken met een klein
                // rood randje om het hart - anders is deze instelling onzichtbaar.
                if (wissel.AltijdInitialiseren)
                {
                    var initialiseerRing = new Ellipse
                    {
                        Width = 12, Height = 12,
                        Stroke = Brushes.Red,
                        StrokeThickness = 1.5,
                        Fill = Brushes.Transparent,
                        ToolTip = "Altijd initialiseren: stuurt de stand bij het verbinden met hardware altijd opnieuw (Ctrl+rechtsklik om uit te zetten)."
                    };
                    Canvas.SetLeft(initialiseerRing, wissel.X - 6);
                    Canvas.SetTop(initialiseerRing, wissel.Y - 6);
                    BaanCanvas.Children.Add(initialiseerRing);
                }

                var wisselAdresLabel = new TextBlock
                {
                    Text = wissel.Adres.ToString(),
                    FontSize = 9,
                    Foreground = Brushes.DimGray,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(wisselAdresLabel, wissel.X - 5);
                Canvas.SetTop(wisselAdresLabel, wissel.Y + 6);
                BaanCanvas.Children.Add(wisselAdresLabel);

                // Defect (Alt+rechtsklik) zichtbaar maken met een dikke rode X over het hele
                // wissel-hart - een stuk opvallender dan de dunne "altijd initialiseren"-ring,
                // want dit is een storing die niet over het hoofd gezien mag worden.
                if (wissel.IsDefect)
                {
                    var defectTooltip = $"Wissel {wissel.Adres}: DEFECT (Alt+rechtsklik om te herstellen). Routes die deze wissel nodig hebben, starten niet.";
                    foreach (var (dx1, dy1, dx2, dy2) in new[] { (-7.0, -7.0, 7.0, 7.0), (-7.0, 7.0, 7.0, -7.0) })
                    {
                        BaanCanvas.Children.Add(new Line
                        {
                            X1 = wissel.X + dx1, Y1 = wissel.Y + dy1, X2 = wissel.X + dx2, Y2 = wissel.Y + dy2,
                            Stroke = Brushes.Red, StrokeThickness = 3, ToolTip = defectTooltip
                        });
                    }
                }

                // Zichtbare ankerpunten - dezelfde 3 punten als WisselAnkerpunten (instroom/
                // rechtdoor/afbuigend), zodat je niet hoeft te gokken welke je pakt als ze
                // dicht bij elkaar liggen. Niet in kijk-modus, puur ruis daar.
                if (_huidigTool == Tool.Verplaatsen && !_kijkModus)
                {
                    foreach (var anker in WisselAnkerpunten(wissel))
                    {
                        var wisselAnkerHandle = new Rectangle
                        {
                            Width = AnkerHandleGrootte,
                            Height = AnkerHandleGrootte,
                            Fill = Brushes.DeepSkyBlue,
                            Stroke = Brushes.Black,
                            StrokeThickness = 1
                        };
                        Canvas.SetLeft(wisselAnkerHandle, anker.X - AnkerHandleGrootte / 2);
                        Canvas.SetTop(wisselAnkerHandle, anker.Y - AnkerHandleGrootte / 2);
                        BaanCanvas.Children.Add(wisselAnkerHandle);
                    }
                }
                break;

            case Sein sein:
                var aspect = SeinLogica.BepaalAspect(sein, _blokBeheerder, _wisselstraatBeheerder, _baanBeheerder.Symbolen.OfType<Sein>());
                SeinAspect effectiefAspect = aspect ?? (sein.Stand == SeinStand.Onveilig ? SeinAspect.Rood : SeinAspect.Groen);

                // Een lichtsein-kop zoals langs het Nederlandse spoor: een zwart kastje met 3
                // kleine lampjes onder elkaar, groen bovenaan en rood ALTIJD onderaan (net als
                // een echt sein), waarvan er telkens maar één brandt. Geen paaltje eronder -
                // de kop zelf is het symbool. Alles staat in een eigen Canvas-groepje zodat het
                // in zijn geheel gedraaid kan worden (HoekGraden, in stappen van 45°) - de
                // draai gebeurt om het eigen middelpunt van het sein, het adreslabel blijft
                // bewust rechtop staan (leesbaarheid) en draait dus niet mee.
                // Een dwergsein is in het echt fysiek een stuk kleiner en heeft maar 2 lampjes
                // (geen geel-positie) - hier zichtbaar gemaakt met een kleiner kastje en maar 2
                // lampposities i.p.v. 3. Een 2-standen hoofdsein blijft qua GROOTTE gelijk aan
                // een 3-standen sein (dat klopt ook in het echt), alleen de middelste (gele)
                // positie brandt door de aspectlogica hierboven nooit.
                bool isDwergsein = sein.Type == SeinType.Dwergsein;
                double lampDiameter = isDwergsein ? 4 : 6;
                double kopBreedte = isDwergsein ? 8 : 11;
                double kopVulling = 2;
                int aantalLampposities = isDwergsein ? 2 : 3;
                double kopHoogte = lampDiameter * aantalLampposities + kopVulling * 2;

                var seinGroep = new Canvas
                {
                    RenderTransform = new RotateTransform(sein.HoekGraden, 0, 0)
                };
                Canvas.SetLeft(seinGroep, sein.X);
                Canvas.SetTop(seinGroep, sein.Y);

                // Zelfde onderscheid als bij een lijn: tijdens Koppelen-tool met een
                // geselecteerd blok, een sein gekoppeld aan PRECIES dat blok fel groen
                // omranden, zodat je niet in de war raakt met een sein dat toevallig aan
                // een ANDER blok gekoppeld is.
                bool seinGekoppeldAanHuidigeSelectie = _huidigTool == Tool.Koppelen && _koppelGeselecteerdBlok != null && sein.GekoppeldBlok == _koppelGeselecteerdBlok;
                var seinKop = new Rectangle
                {
                    Width = kopBreedte,
                    Height = kopHoogte,
                    RadiusX = 2,
                    RadiusY = 2,
                    Fill = Brushes.Black,
                    Stroke = seinGekoppeldAanHuidigeSelectie ? Brushes.LimeGreen : geselecteerd ? KoploperKleuren.Geselecteerd : Brushes.DimGray,
                    StrokeThickness = seinGekoppeldAanHuidigeSelectie || geselecteerd ? 2 : 1
                };
                Canvas.SetLeft(seinKop, -kopBreedte / 2);
                Canvas.SetTop(seinKop, -kopHoogte / 2);
                seinGroep.Children.Add(seinKop);

                // Van boven naar onder: groen, geel, rood - rood zit in het echt altijd onderaan
                // (t.o.v. de eigen rotatie van het sein, dus "onderaan" draait mee). Een
                // dwergsein slaat de gele positie helemaal over (fysiek maar 2 lampjes).
                (SeinAspect Aspect, Brush Aan)[] lampen = isDwergsein
                    ? new[]
                      {
                          (SeinAspect.Groen, KoploperKleuren.SeinGroen),
                          (SeinAspect.Rood, KoploperKleuren.SeinRood)
                      }
                    : new[]
                      {
                          (SeinAspect.Groen, KoploperKleuren.SeinGroen),
                          (SeinAspect.Geel, KoploperKleuren.SeinGeel),
                          (SeinAspect.Rood, KoploperKleuren.SeinRood)
                      };
                for (int i = 0; i < lampen.Length; i++)
                {
                    var lamp = new Ellipse
                    {
                        Width = lampDiameter,
                        Height = lampDiameter,
                        Fill = lampen[i].Aspect == effectiefAspect ? lampen[i].Aan : Brushes.DimGray,
                        IsHitTestVisible = false
                    };
                    Canvas.SetLeft(lamp, -lampDiameter / 2);
                    Canvas.SetTop(lamp, -kopHoogte / 2 + kopVulling / 2 + i * lampDiameter);
                    seinGroep.Children.Add(lamp);
                }
                BaanCanvas.Children.Add(seinGroep);

                var seinAdresLabel = new TextBlock
                {
                    Text = sein.Adres.ToString(),
                    FontSize = 9,
                    Foreground = Brushes.DimGray,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(seinAdresLabel, sein.X - 5);
                Canvas.SetTop(seinAdresLabel, sein.Y + 8);
                BaanCanvas.Children.Add(seinAdresLabel);
                break;

            case Stootblok stootblok:
                var loodrecht = new Vector(-stootblok.Richting.Y, stootblok.Richting.X);
                if (loodrecht.Length < 0.01) loodrecht = new Vector(0, 1);
                loodrecht.Normalize();
                var stootblokVorm = new Line
                {
                    X1 = stootblok.X - loodrecht.X * SymboolGrootte / 2,
                    Y1 = stootblok.Y - loodrecht.Y * SymboolGrootte / 2,
                    X2 = stootblok.X + loodrecht.X * SymboolGrootte / 2,
                    Y2 = stootblok.Y + loodrecht.Y * SymboolGrootte / 2,
                    Stroke = geselecteerd ? Brushes.Red : Brushes.Black,
                    StrokeThickness = 5
                };
                BaanCanvas.Children.Add(stootblokVorm);
                break;

            case Perron perron:
                var perronVorm = new Rectangle
                {
                    Width = perron.Breedte,
                    Height = perron.Hoogte,
                    Fill = KoploperKleuren.Perron,
                    Stroke = geselecteerd ? Brushes.Red : Brushes.Black,
                    StrokeThickness = geselecteerd ? 2 : 1
                };
                Canvas.SetLeft(perronVorm, perron.X - perron.Breedte / 2);
                Canvas.SetTop(perronVorm, perron.Y - perron.Hoogte / 2);
                BaanCanvas.Children.Add(perronVorm);

                if (!string.IsNullOrWhiteSpace(perron.Omschrijving))
                {
                    var perronLabel = new TextBlock
                    {
                        Text = perron.Omschrijving,
                        FontSize = 10,
                        FontWeight = FontWeights.Bold,
                        Foreground = Brushes.Black,
                        IsHitTestVisible = false
                    };
                    Canvas.SetLeft(perronLabel, perron.X - perron.Breedte / 2);
                    Canvas.SetTop(perronLabel, perron.Y + perron.Hoogte / 2 + 2);
                    BaanCanvas.Children.Add(perronLabel);
                }
                break;

            case Tekst tekst:
                var tekstVorm = new TextBlock
                {
                    Text = tekst.Inhoud,
                    Foreground = geselecteerd ? Brushes.Red : Brushes.Black,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(tekstVorm, tekst.X);
                Canvas.SetTop(tekstVorm, tekst.Y);
                BaanCanvas.Children.Add(tekstVorm);
                break;

            case Schakelaar schakelaar:
                var schakelaarVorm = new Ellipse
                {
                    Width = SymboolGrootte * 0.7,
                    Height = SymboolGrootte * 0.7,
                    Fill = schakelaar.Aan ? Brushes.Gold : Brushes.LightGray,
                    Stroke = geselecteerd ? KoploperKleuren.Geselecteerd : Brushes.Black,
                    StrokeThickness = geselecteerd ? 2 : 1,
                    ToolTip = string.IsNullOrWhiteSpace(schakelaar.Omschrijving)
                        ? $"Schakelaar ({(schakelaar.Aan ? "aan" : "uit")})"
                        : $"{schakelaar.Omschrijving} ({(schakelaar.Aan ? "aan" : "uit")})"
                };
                Canvas.SetLeft(schakelaarVorm, schakelaar.X - SymboolGrootte * 0.35);
                Canvas.SetTop(schakelaarVorm, schakelaar.Y - SymboolGrootte * 0.35);
                BaanCanvas.Children.Add(schakelaarVorm);

                if (!string.IsNullOrWhiteSpace(schakelaar.Omschrijving))
                {
                    var schakelaarLabel = new TextBlock
                    {
                        Text = schakelaar.Omschrijving,
                        FontSize = 9,
                        Foreground = Brushes.DimGray,
                        IsHitTestVisible = false
                    };
                    Canvas.SetLeft(schakelaarLabel, schakelaar.X - SymboolGrootte * 0.35);
                    Canvas.SetTop(schakelaarLabel, schakelaar.Y + SymboolGrootte * 0.4);
                    BaanCanvas.Children.Add(schakelaarLabel);
                }
                break;

            case Pijl pijl:
                double pijlLengte = SymboolGrootte * 0.8;
                double hoekRad = pijl.HoekGraden * Math.PI / 180;
                var pijlRichting = new Vector(Math.Cos(hoekRad), Math.Sin(hoekRad));
                var pijlStart = new Point(pijl.X - pijlRichting.X * pijlLengte / 2, pijl.Y - pijlRichting.Y * pijlLengte / 2);
                var pijlEind = new Point(pijl.X + pijlRichting.X * pijlLengte / 2, pijl.Y + pijlRichting.Y * pijlLengte / 2);
                var pijlKleur = geselecteerd ? KoploperKleuren.Geselecteerd : Brushes.SlateGray;

                BaanCanvas.Children.Add(new Line { X1 = pijlStart.X, Y1 = pijlStart.Y, X2 = pijlEind.X, Y2 = pijlEind.Y, Stroke = pijlKleur, StrokeThickness = 2, IsHitTestVisible = false });

                var pijlLoodrecht = new Vector(-pijlRichting.Y, pijlRichting.X);
                const double pijlpuntLengte = 7, pijlpuntBreedte = 5;
                var pijlpuntBasis = pijlEind - pijlRichting * pijlpuntLengte;
                var pijlpunt = new Polygon
                {
                    Points = new PointCollection
                    {
                        pijlEind,
                        pijlpuntBasis + pijlLoodrecht * pijlpuntBreedte / 2,
                        pijlpuntBasis - pijlLoodrecht * pijlpuntBreedte / 2
                    },
                    Fill = pijlKleur,
                    IsHitTestVisible = false
                };
                BaanCanvas.Children.Add(pijlpunt);
                break;

            case Ontkoppelrail ontkoppelrail:
                var ontkoppelrailVorm = new Polygon
                {
                    Points = new PointCollection
                    {
                        new Point(ontkoppelrail.X, ontkoppelrail.Y - SymboolGrootte * 0.35),
                        new Point(ontkoppelrail.X + SymboolGrootte * 0.35, ontkoppelrail.Y),
                        new Point(ontkoppelrail.X, ontkoppelrail.Y + SymboolGrootte * 0.35),
                        new Point(ontkoppelrail.X - SymboolGrootte * 0.35, ontkoppelrail.Y)
                    },
                    Fill = Brushes.Gold,
                    Stroke = geselecteerd ? KoploperKleuren.Geselecteerd : Brushes.Black,
                    StrokeThickness = geselecteerd ? 2 : 1,
                    ToolTip = $"Ontkoppelrail (adres {ontkoppelrail.Adres})"
                };
                BaanCanvas.Children.Add(ontkoppelrailVorm);

                var ontkoppelrailLabel = new TextBlock
                {
                    Text = ontkoppelrail.Adres.ToString(),
                    FontSize = 9,
                    Foreground = Brushes.DimGray,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(ontkoppelrailLabel, ontkoppelrail.X - 5);
                Canvas.SetTop(ontkoppelrailLabel, ontkoppelrail.Y + SymboolGrootte * 0.4);
                BaanCanvas.Children.Add(ontkoppelrailLabel);
                break;

            case Bezetmelder bezetmelder:
                // "Kleine cirkeltjes in de lijnen", zoals het echte Koploper bezetmelders
                // toont - grijs/niet gekoppeld tot je 'm koppelt (via de Koppelen-tool),
                // daarna gekleurd op de bezetstatus van dat blok (dezelfde aanpak als de
                // rest van de tekening - we houden geen aparte per-melder status bij,
                // alleen per blok).
                bool bezetmelderBezet = bezetmelder.GekoppeldBlok != null && _blokBeheerder.IsBezet(bezetmelder.GekoppeldBlok);
                var bezetmelderVorm = new Ellipse
                {
                    Width = 8,
                    Height = 8,
                    Fill = bezetmelder.GekoppeldBlok is null ? Brushes.LightGray : bezetmelderBezet ? KoploperKleuren.Bezet : KoploperKleuren.Vrij,
                    Stroke = geselecteerd ? KoploperKleuren.Geselecteerd : Brushes.Black,
                    StrokeThickness = geselecteerd ? 2 : 1,
                    ToolTip = bezetmelder.GekoppeldBlok != null
                        ? $"Bezetmelder{(bezetmelder.MeldernNummer > 0 ? $" {bezetmelder.MeldernNummer}" : "")} — blok {bezetmelder.GekoppeldBlok.Nummer}"
                        : "Bezetmelder (nog niet gekoppeld aan een blok)"
                };
                Canvas.SetLeft(bezetmelderVorm, bezetmelder.X - 4);
                Canvas.SetTop(bezetmelderVorm, bezetmelder.Y - 4);
                BaanCanvas.Children.Add(bezetmelderVorm);

                if (bezetmelder.MeldernNummer > 0)
                {
                    var bezetmelderLabel = new TextBlock
                    {
                        Text = bezetmelder.MeldernNummer.ToString(),
                        FontSize = 8,
                        Foreground = Brushes.DimGray,
                        IsHitTestVisible = false
                    };
                    Canvas.SetLeft(bezetmelderLabel, bezetmelder.X - 5);
                    Canvas.SetTop(bezetmelderLabel, bezetmelder.Y + 6);
                    BaanCanvas.Children.Add(bezetmelderLabel);
                }
                break;

            case Driewegwissel driewegwissel:
                // Zelfde grondvorm als een gewone wissel, maar met DRIE benen i.p.v. twee:
                // instroom + rechtdoor + links-afbuigend + rechts-afbuigend (45° aan
                // weerszijden van de hoofdrichting). De actieve poot (Stand) is dik/gekleurd,
                // de andere twee dun/grijs.
                var driewegAccentKleur = geselecteerd ? KoploperKleuren.Geselecteerd : KoploperKleuren.WisselActievePoot;
                double driewegHalveLengte = SymboolGrootte * 0.9;
                double driewegHoekRad = driewegwissel.HoofdrichtingGraden * Math.PI / 180.0;
                var driewegRichting = new Vector(Math.Cos(driewegHoekRad), Math.Sin(driewegHoekRad));

                BaanCanvas.Children.Add(new Line
                {
                    X1 = driewegwissel.X - driewegRichting.X * driewegHalveLengte, Y1 = driewegwissel.Y - driewegRichting.Y * driewegHalveLengte,
                    X2 = driewegwissel.X, Y2 = driewegwissel.Y,
                    Stroke = driewegAccentKleur, StrokeThickness = 2, IsHitTestVisible = false
                });

                (DriewegwisselStand Stand, double HoekOffset)[] driewegPoten =
                {
                    (DriewegwisselStand.Rechtdoor, 0),
                    (DriewegwisselStand.Links, -45),
                    (DriewegwisselStand.Rechts, 45)
                };
                foreach (var (stand, hoekOffset) in driewegPoten)
                {
                    double driewegPootHoekRad = (driewegwissel.HoofdrichtingGraden + hoekOffset) * Math.PI / 180.0;
                    var potRichting = new Vector(Math.Cos(driewegPootHoekRad), Math.Sin(driewegPootHoekRad));
                    bool actief = driewegwissel.Stand == stand;
                    BaanCanvas.Children.Add(new Line
                    {
                        X1 = driewegwissel.X, Y1 = driewegwissel.Y,
                        X2 = driewegwissel.X + potRichting.X * driewegHalveLengte, Y2 = driewegwissel.Y + potRichting.Y * driewegHalveLengte,
                        Stroke = actief ? driewegAccentKleur : KoploperKleuren.WisselNietActievePoot,
                        StrokeThickness = actief ? 3 : 1,
                        IsHitTestVisible = false
                    });
                }

                var driewegHart = new Ellipse
                {
                    Width = 8, Height = 8,
                    Fill = driewegAccentKleur,
                    ToolTip = $"Driewegwissel {driewegwissel.Adres} — stand: {driewegwissel.Stand}"
                };
                Canvas.SetLeft(driewegHart, driewegwissel.X - 4);
                Canvas.SetTop(driewegHart, driewegwissel.Y - 4);
                BaanCanvas.Children.Add(driewegHart);

                var driewegLabel = new TextBlock { Text = driewegwissel.Adres.ToString(), FontSize = 9, Foreground = Brushes.DimGray, IsHitTestVisible = false };
                Canvas.SetLeft(driewegLabel, driewegwissel.X + 6);
                Canvas.SetTop(driewegLabel, driewegwissel.Y - 18);
                BaanCanvas.Children.Add(driewegLabel);
                break;

            case Kruiswissel kruiswissel:
                // Passieve kruising: twee rechte sporen die elkaar op één punt kruisen,
                // zonder eigen wisselstand - een trein op elk spoor rijdt gewoon rechtdoor.
                // Getekend als twee lijnen door hetzelfde middelpunt, 45° uit elkaar (een
                // scherpe kruisingshoek, zoals een echte kruiswissel/kruising ook heeft -
                // niet haaks), samen roteerbaar via HoofdrichtingGraden. Bij IsEngels=true
                // (Engelse wissel) komen er ook twee diagonale "overstap"-verbindingen bij,
                // dik/gekleurd zodra Stand=Overstap actief is - de schakelbare variant.
                double kruisHalveLengte = SymboolGrootte * 0.9;
                var kruisKleur = geselecteerd ? KoploperKleuren.Geselecteerd : KoploperKleuren.SpoorLijn;
                var kruisEindpunten = new List<Point>();
                foreach (double hoekOffset in new[] { 0.0, 45.0 })
                {
                    double kruisHoekRad = (kruiswissel.HoofdrichtingGraden + hoekOffset) * Math.PI / 180.0;
                    var kruisRichting = new Vector(Math.Cos(kruisHoekRad), Math.Sin(kruisHoekRad));
                    var puntA = new Point(kruiswissel.X - kruisRichting.X * kruisHalveLengte, kruiswissel.Y - kruisRichting.Y * kruisHalveLengte);
                    var puntB = new Point(kruiswissel.X + kruisRichting.X * kruisHalveLengte, kruiswissel.Y + kruisRichting.Y * kruisHalveLengte);
                    kruisEindpunten.Add(puntA);
                    kruisEindpunten.Add(puntB);
                    BaanCanvas.Children.Add(new Line
                    {
                        X1 = puntA.X, Y1 = puntA.Y, X2 = puntB.X, Y2 = puntB.Y,
                        Stroke = kruisKleur, StrokeThickness = 3, IsHitTestVisible = false
                    });
                }

                if (kruiswissel.IsEngels)
                {
                    var overstapKleur = geselecteerd ? KoploperKleuren.Geselecteerd : KoploperKleuren.WisselActievePoot;
                    bool overstapActief = kruiswissel.Stand == KruiswisselStand.Overstap;
                    // Verbindt de 4 eindpunten kruislings (elk eindpunt van spoor 1 met het
                    // dichtstbijzijnde eindpunt van spoor 2) - de fysieke "overstap"-rails
                    // van een echte Engelse wissel, alleen dik/gekleurd getoond als de stand
                    // ook echt op Overstap staat (anders dun/grijs, net als een gewone wissel-poot).
                    var overstapParen = new[] { (0, 2), (0, 3), (1, 2), (1, 3) }
                        .OrderBy(paar => (kruisEindpunten[paar.Item1] - kruisEindpunten[paar.Item2]).Length)
                        .Take(2);
                    foreach (var (indexA, indexB) in overstapParen)
                    {
                        BaanCanvas.Children.Add(new Line
                        {
                            X1 = kruisEindpunten[indexA].X, Y1 = kruisEindpunten[indexA].Y,
                            X2 = kruisEindpunten[indexB].X, Y2 = kruisEindpunten[indexB].Y,
                            Stroke = overstapActief ? overstapKleur : KoploperKleuren.WisselNietActievePoot,
                            StrokeThickness = overstapActief ? 3 : 1,
                            IsHitTestVisible = false
                        });
                    }
                    var engelseLabel = new TextBlock { Text = kruiswissel.Adres.ToString(), FontSize = 9, Foreground = Brushes.DimGray, IsHitTestVisible = false };
                    Canvas.SetLeft(engelseLabel, kruiswissel.X + 6);
                    Canvas.SetTop(engelseLabel, kruiswissel.Y - 18);
                    BaanCanvas.Children.Add(engelseLabel);
                }

                var kruisHart = new Rectangle
                {
                    Width = 7, Height = 7,
                    Fill = Brushes.DimGray,
                    ToolTip = kruiswissel.IsEngels
                        ? $"Engelse wissel — motor {kruiswissel.Adres}: {kruiswissel.StandAdres}, motor {kruiswissel.Adres2}: {kruiswissel.StandAdres2}"
                        : "Kruiswissel (passieve kruising, geen eigen stand)"
                };
                Canvas.SetLeft(kruisHart, kruiswissel.X - 3.5);
                Canvas.SetTop(kruisHart, kruiswissel.Y - 3.5);
                BaanCanvas.Children.Add(kruisHart);

                // Zichtbare ankerpunten - dezelfde 4 punten als KruiswisselAnkerpunten, net
                // als bij blokken/wissels hierboven (was aanvankelijk vergeten bij het
                // toevoegen van het kruiswissel-ankerpuntensysteem - gebruikersmelding:
                // "geen ankerpunten gezien"). Niet in kijk-modus, puur ruis daar.
                if (_huidigTool == Tool.Verplaatsen && !_kijkModus)
                {
                    foreach (var anker in KruiswisselAnkerpunten(kruiswissel))
                    {
                        var kruiswisselAnkerHandle = new Rectangle
                        {
                            Width = AnkerHandleGrootte,
                            Height = AnkerHandleGrootte,
                            Fill = Brushes.DeepSkyBlue,
                            Stroke = Brushes.Black,
                            StrokeThickness = 1
                        };
                        Canvas.SetLeft(kruiswisselAnkerHandle, anker.X - AnkerHandleGrootte / 2);
                        Canvas.SetTop(kruiswisselAnkerHandle, anker.Y - AnkerHandleGrootte / 2);
                        BaanCanvas.Children.Add(kruiswisselAnkerHandle);
                    }
                }
                break;
        }
    }
}
