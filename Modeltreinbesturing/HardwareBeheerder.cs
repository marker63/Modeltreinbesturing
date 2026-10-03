using System.Linq;
using System.Windows.Threading;
using Modeltreinbesturing.Hardware;
using Modeltreinbesturing.Model;

namespace Modeltreinbesturing;

/// <summary>Houdt de op dit moment actieve hardware-interface bij (standaard: simulatie)
/// en meldt een wisseling, zodat BaanontwerpWindow/TreinrouteWindow hun wissel/sein-
/// commando's naar de juiste plek kunnen sturen zonder zelf te hoeven weten welke
/// hardware (of geen) er is gekozen.</summary>
public class HardwareBeheerder
{
    public IHardwareInterface Huidige { get; private set; } = new SimulatieHardware();
    public event Action? HardwareGewijzigd;
    /// <summary>Doorgeven van Huidige.KortsluitingStatusGewijzigd (zie IHardwareInterface),
    /// zodat de UI (BaanontwerpWindow) dit kan tonen zonder zelf de actieve
    /// hardware-interface te hoeven kennen - zelfde patroon als HardwareGewijzigd
    /// hierboven.</summary>
    public event Action<bool>? KortsluitingStatusGewijzigd;

    /// <summary>GEVONDEN GAT (gebruikerswaarneming: "tijdens het initialiseren zie ik de
    /// wissels op het scherm niet van positie wijzigen, en erna staat bijv. wissel 5 op het
    /// scherm in een andere stand dan in werkelijkheid"): BaanontwerpWindow tekent een
    /// wissel/driewegwissel/kruiswissel aan de hand van diens (levende) Stand-property,
    /// maar hertekende het scherm tot nu toe ALLEEN naar aanleiding van
    /// BlokBeheerder.BezettingGewijzigd en KoploperKleuren.SchemaGewijzigd - geen van
    /// beide vuurt als er puur een wissel wordt gezet zonder dat de bezetting van een blok
    /// verandert (precies het geval bij de opstart-initialisatie in HardwareDialog/
    /// MainWindow, en ook bij een los, vroegtijdig wisselcommando vanuit TreinrouteWindow
    /// vlak voordat een trein het blok daadwerkelijk bereikt/verlaat). Bovendien is het
    /// kijkscherm een APARTE BaanontwerpWindow-instantie dan het bouwscherm (zie
    /// MainWindow._baanoverzichtKijkWindow) - beide moeten dus onafhankelijk gewaarschuwd
    /// worden. StuurWisselCommando is de ENE plek waar letterlijk elk wisselcommando
    /// doorheen gaat (init, dubbelklik, wisselstraat-logica, handmatige testdialoog), dus
    /// dat is de juiste, centrale plek om dit te melden - de aanroeper heeft de bijbehorende
    /// Stand-property altijd al (vlak) vóór of na deze aanroep gezet, dus een meteen
    /// aansluitende Redraw() toont altijd de actuele, juiste stand.</summary>
    public event Action? WisselStandGewijzigd;

    public HardwareBeheerder()
    {
        _wachtrijTimer.Tick += (_, _) => VerwerkVolgendeCommando();
        Huidige.StatusBericht += DoorsturenNaarLog;
        Huidige.KortsluitingStatusGewijzigd += DoorgevenKortsluitingStatus;
    }

    /// <summary>StatusBericht-meldingen (bijv. "dit commando is nog niet geïmplementeerd
    /// voor deze interface") kwamen voorheen ALLEEN terecht in het aparte hardware-
    /// instellingenscherm (HardwareDialog), dat vaak niet open staat - een gebruiker die
    /// puur de Hardware-communicatielog checkte, zag dus GEEN enkele aanwijzing waarom er
    /// bijv. geen loc-commando's verschenen (Dinamo: rijden/functies zijn eerlijk niet
    /// geïmplementeerd, spec-gat). Nu wordt elk StatusBericht ALTIJD ook naar die log
    /// doorgestuurd, ongeacht of het instellingenscherm open staat.</summary>
    private void DoorsturenNaarLog(string bericht) => HardwareCommunicatieLog.Log("Info", bericht);

    /// <summary>BUG #28 (gebruikersmelding: "bij opstarten direct een kortsluitmelding op
    /// het rijscherm in het rood, gaat na lange wachttijd in blok 3 ineens toch rijden" +
    /// "wissel 14 op het scherm afbuigend, maar fysiek nog gewoon rechtdoor"): wordt hier
    /// gezet via RegistreerBaanBeheerderVoorAutomatischeHerinitialisatie, zodat
    /// DoorgevenKortsluitingStatus hieronder, zodra een kortsluiting weer opgeheven wordt,
    /// zelfstandig alle wissels/driewegwissels/kruiswissels opnieuw hun gewenste stand kan
    /// sturen - zie HerinitialiseerAlleWissels hieronder voor de volledige toelichting.
    /// Null zolang MainWindow deze koppeling nog niet gelegd heeft (bijv. heel vroeg in de
    /// opstart) - dan gebeurt er simpelweg niets extra's, zoals voorheen.</summary>
    private BaanOntwerpBeheerder? _baanBeheerderVoorHerinit;

    public void RegistreerBaanBeheerderVoorAutomatischeHerinitialisatie(BaanOntwerpBeheerder baanBeheerder) =>
        _baanBeheerderVoorHerinit = baanBeheerder;

    private void DoorgevenKortsluitingStatus(bool actief)
    {
        KortsluitingStatusGewijzigd?.Invoke(actief);
        // Alleen bij het OPHEFFEN van een fout (actief==false) - zie DinamoHardware.
        // Poort_DataReceived: dat gebeurt uitsluitend op de overgang "was fout, nu niet
        // meer", nooit herhaald zolang de fout al bekend was. Precies het moment waarop
        // eventueel tijdens de fout verloren/onderbroken wisselcommando's hersteld moeten
        // worden.
        if (!actief && _baanBeheerderVoorHerinit != null)
        {
            int aantal = HerinitialiseerAlleWissels(_baanBeheerderVoorHerinit);
            if (aantal > 0)
                HardwareCommunicatieLog.Log("Info", $"Kortsluiting/foutstatus opgeheven - voor de zekerheid {aantal} wissel(s)/driewegwissel(s)/kruiswissel(s) opnieuw hun stand gestuurd (zie BUG #28: een puls tijdens de fout kan een wissel fysiek in de verkeerde stand hebben achtergelaten zonder dat de software dat kon weten).");
        }
    }

    /// <summary>Verzamelt en (opnieuw) verstuurt de gewenste stand van ALLE wissels,
    /// driewegwissels en (Engelse) kruiswissels - gedeelde kern achter zowel de
    /// opstart-initialisatie (MainWindow.VerbindMetOpgeslagenHardwareIndienBeschikbaar/
    /// HardwareDialog.InitialiseerWisselsMetVlag, die deze nu allebei aanroepen i.p.v. hun
    /// eigen, losse kopie van dezelfde drie foreach-lussen) als de automatische
    /// HERinitialisatie na een Dinamo-kortsluitherstel hierboven.
    ///
    /// BUG #28 (gebruikerswaarneming: wissel 14 stond op het scherm op "afbuigend", maar
    /// bleek fysiek gewoon op "rechtdoor" te staan - de loc reed dan ook daadwerkelijk
    /// rechtdoor blok 5 in): het Dinamo-magneetartikel-commando is EEN PULS zonder enige
    /// terugmelding (geen sensor op de wisselstand zelf) - StuurWisselCommando weet dus
    /// NOOIT zeker of een wissel daadwerkelijk is omgegaan, het verstuurt alleen het
    /// commando en de rest van de software neemt succes aan. Gebeurt er TUSSENDOOR een
    /// echte, fysieke kortsluiting (bijv. precies tijdens zo'n puls), dan kan de spoel een
    /// te korte/onderbroken puls krijgen en toch in de oude stand blijven staan, terwijl de
    /// software allang "klaar" denkt te zijn - een desync die daarvoor pas aan het licht
    /// kwam zodra een trein de verkeerde kant op reed. Een eerdere fix loste dit al op voor
    /// het MOMENT van verbinden (altijd expliciet initialiseren), maar een latere
    /// kortsluiting/foutherstel MIDDEN in een sessie werd tot nu toe nergens opgevangen.
    /// Nu: zodra Dinamo's foutstatus weer opgeheven wordt (DoorgevenKortsluitingStatus
    /// hierboven), worden ALTIJD alle wissels/driewegwissels/kruiswissels opnieuw hun
    /// gewenste stand gestuurd, puur als veiligheidsnet - een overbodige herhaling (de
    /// wissel stond toch al goed) kan nooit kwaad, en dit is voor een ECHT
    /// gedesynchroniseerde wissel de enige praktische manier om software en fysieke baan
    /// weer in lijn te krijgen zonder dat de gebruiker zelf iedere wissel met de hand moet
    /// natrekken.</summary>
    public int HerinitialiseerAlleWissels(BaanOntwerpBeheerder baanBeheerder)
    {
        var alleWissels = baanBeheerder.Symbolen.OfType<Wissel>().ToList();
        foreach (var wissel in alleWissels)
        {
            bool gewenstAfbuigend = wissel.Stand == WisselStand.Afbuigend;
            bool voorHardware = wissel.OmgekeerdePolariteit ? !gewenstAfbuigend : gewenstAfbuigend;
            StuurWisselCommando(wissel.Adres, !voorHardware);
            StuurWisselCommando(wissel.Adres, voorHardware);
            if (wissel.GekoppeldeOverloopwissel != null)
            {
                var overloop = wissel.GekoppeldeOverloopwissel;
                bool overloopVoorHardware = overloop.OmgekeerdePolariteit ? !gewenstAfbuigend : gewenstAfbuigend;
                StuurWisselCommando(overloop.Adres, !overloopVoorHardware);
                StuurWisselCommando(overloop.Adres, overloopVoorHardware);
            }
        }
        var alleDriewegwissels = baanBeheerder.Symbolen.OfType<Driewegwissel>().ToList();
        foreach (var driewegwissel in alleDriewegwissels)
        {
            bool gewenstAfbuigend = driewegwissel.Stand != DriewegwisselStand.Rechtdoor;
            StuurWisselCommando(driewegwissel.Adres, !gewenstAfbuigend);
            StuurWisselCommando(driewegwissel.Adres, gewenstAfbuigend);
        }
        var alleKruiswissels = baanBeheerder.Symbolen.OfType<Kruiswissel>().Where(k => k.IsEngels).ToList();
        foreach (var kruiswissel in alleKruiswissels)
        {
            bool afbuigend1 = kruiswissel.StandAdres == WisselStand.Afbuigend;
            bool afbuigend2 = kruiswissel.StandAdres2 == WisselStand.Afbuigend;
            if (kruiswissel.Adres > 0)
            {
                StuurWisselCommando(kruiswissel.Adres, !afbuigend1);
                StuurWisselCommando(kruiswissel.Adres, afbuigend1);
            }
            if (kruiswissel.Adres2 > 0)
            {
                StuurWisselCommando(kruiswissel.Adres2, !afbuigend2);
                StuurWisselCommando(kruiswissel.Adres2, afbuigend2);
            }
        }
        return alleWissels.Count + alleDriewegwissels.Count + alleKruiswissels.Count;
    }

    public void WisselHardware(IHardwareInterface nieuw)
    {
        Huidige.StatusBericht -= DoorsturenNaarLog;
        Huidige.KortsluitingStatusGewijzigd -= DoorgevenKortsluitingStatus;
        Huidige = nieuw;
        Huidige.StatusBericht += DoorsturenNaarLog;
        Huidige.KortsluitingStatusGewijzigd += DoorgevenKortsluitingStatus;
        HardwareGewijzigd?.Invoke();
    }

    // Koploper: "wanneer verschillende wissels en seinen tegelijkertijd veranderd moeten
    // worden zorgt Koploper ervoor dat dit één voor één gedaan wordt met een korte
    // (instelbare) tussenpauze" - zie SimulatieInstellingen.WisselRustpauzeMilliseconden.
    // Elk commando (ook het allereerste in een burst) wacht op zijn eigen tik van
    // _wachtrijTimer, én op KlaarVoorVolgendeWisselCommando (zie VerwerkVolgendeCommando) -
    // zo geldt de pauze altijd, consistent, ongeacht wat er bij Dinamo zelf nog in de rij
    // staat.
    // [diagnose]-logging (volgnummer per commando, gelogd bij zowel het in de rij zetten als
    // het er daadwerkelijk uit halen) heeft hier tijdelijk gestaan om te bewijzen dat deze
    // wachtrij zelf strikt FIFO blijft, ook als de daadwerkelijke Dinamo-bytes in een eerdere
    // test in een andere volgorde leken te verschijnen. Bevestigd (log 10:13:49 e.v.): de
    // wisselcommando's gaan wel degelijk als EERSTE, binnen ~1 seconde na het verbinden, en
    // correct gevormd de deur uit - het "geen klik gehoord"-probleem zit dus niet (meer) in
    // deze wachtrij, en de diagnose-regels zijn weer verwijderd om de hardware-log
    // overzichtelijk te houden.
    // GEVONDEN, KRITIEKE REGRESSIE (gebruikersmelding: "aan het einde staat de kruiswissel
    // weer verkeerd, zowel 10 als 13" - bytes-analyse van het hardware-communicatielog
    // bevestigt dit): deze hele wachtrij was tot nu toe ÉÉN enkele, strikt-FIFO Queue voor
    // ALLES wat via InWachtrijZetten gaat - zowel tijdkritische wissel-/seincommando's
    // (direct nodig voor een veilige routevoortgang) als routinematige, niet-urgente
    // melderstatus-aanvragen (VraagMelderStatusOp, incl. de periodieke herbevestiging uit
    // MainWindow._melderHerbevestigingsTimer, die ELKE 4 seconden 30 nieuwe aanvragen
    // achteraan toevoegt). Bij één klein project ging dat nog goed, maar een project met
    // 30 bekende bezetmelders voegt zo 30 aanvragen/4 sec = 7,5 items/sec toe, terwijl deze
    // wachtrij er maar 1 per WisselRustpauzeMilliseconden (hier 600ms = 1,67 items/sec)
    // verwerkt - de wachtrij GROEIT dus ONBEPERKT zodra de hardware verbonden is en de
    // periodieke herbevestiging actief is. Een wisselcommando dat DAARNA pas in de rij
    // komt (bijv. de kruiswissel die voor de volgende stap omgezet moet worden) belandt
    // ACHTERAAN die steeds verder groeiende berg melderstatus-aanvragen, en komt zo
    // mogelijk nooit (of pas na vele minuten) daadwerkelijk aan bod - precies het "de
    // kruiswissel staat aan het einde verkeerd"-symptoom: niet omdat de software een
    // verkeerde stand berekende, maar omdat het JUISTE commando domweg nog in de rij stond
    // te wachten toen de trein het stuk spoor allang nodig had.
    //
    // Fix: twee aparte wachtrijen in plaats van één - een PRIORITEITS-wachtrij voor wissel-
    // en seincommando's (veiligheidskritisch, direct nodig voor de rijweg) en een NORMALE
    // wachtrij voor alles wat kan wachten (melderstatus-aanvragen, bulk-snelheidsbroadcasts).
    // VerwerkVolgendeCommando hieronder verwerkt de prioriteits-wachtrij altijd eerst, en
    // valt pas op de normale wachtrij terug als die leeg is. Zo kan een nieuw wissel-
    // commando nooit meer achter een opgelopen melderstatus-achterstand vast komen te
    // zitten, ongeacht hoe lang die achterstand al is - exact dezelfde pauze
    // (WisselRustpauzeMilliseconden) blijft gelden, alleen de VOLGORDE waarin de twee
    // soorten verkeer aan de beurt komen is nu niet meer puur FIFO.
    private readonly Queue<Action> _prioriteitsWachtrij = new();
    private readonly Queue<Action> _commandoWachtrij = new();
    private readonly DispatcherTimer _wachtrijTimer = new();

    public void StuurWisselCommando(int adres, bool afbuigend)
    {
        InPrioriteitsWachtrijZetten(() => Huidige.ZetWissel(adres, afbuigend));
        WisselStandGewijzigd?.Invoke();
    }

    public void StuurSeinCommando(int adres, bool onveiligRood) => InPrioriteitsWachtrijZetten(() => Huidige.ZetSein(adres, onveiligRood));

    /// <summary>GEVONDEN, KRITIEK GAT (gebruikerswaarneming: "ik heb geen initialisatie
    /// gehoord" - de wissels bewogen niet hoorbaar bij het (opnieuw) verbinden, ondanks de
    /// "eerst tegenovergesteld dan gewenst"-aanpak hierboven): MainWindow/HardwareDialog
    /// riepen na het initialiseren van de wissels ALTIJD ook DinamoHardware.
    /// VraagMelderStatusOp aan voor ELK bekend meldpunt (soms 30+) - maar deden dat
    /// RECHTSTREEKS op de DinamoHardware-instantie, dus volledig BUITEN deze wachtrij/
    /// KlaarVoorVolgendeWisselCommando-pacing om. Elk van die aanroepen duwt (via ZetWissel/
    /// VerstuurDatagram, zie DinamoHardware) in één keer, zonder enige pauze, een nieuw
    /// datagram in Dinamo se EIGEN verzendwachtrij (_teVersturen). Omdat deze hele methode
    /// synchroon doorloopt VOORDAT de _wachtrijTimer ook maar één tik heeft kunnen geven,
    /// stond _teVersturen tegen de tijd dat de wisselcommando's eindelijk aan de beurt
    /// kwamen al vol met tientallen melderstatus-aanvragen - KlaarVoorVolgendeWisselCommando
    /// (die simpelweg _teVersturenPrioriteit.Count==0 checkt) bleef daardoor minutenlang
    /// "nee" zeggen EN de wisselcommando's droppelden pas veel later, ver uitgesmeerd, naar
    /// buiten - precies het "geen hoorbare initialisatie op het verwachte moment"-effect.
    /// Deze methode stuurde een melderstatus-aanvraag DAAROM EEN TIJDLANG via dezelfde
    /// wachtrij/pauze als wissel-/seincommando's.
    ///
    /// GEVONDEN, VERVOLGGAT (gebruikersmelding: "trein valt stil met spookmelding" -
    /// bytes-analyse: decoderadres 5408's snelheidsramp, die normaal in stappen van
    /// ~100-200ms verloopt, liep na een paar minuten draaien opeens stappen van ~2
    /// SECONDEN, totdat de trein feitelijk stilviel): die "via dezelfde wachtrij"-fix
    /// hierboven loste het connect-moment op, maar MainWindow._melderHerbevestigingsTimer
    /// voegt ELKE 4 SECONDEN opnieuw 30 nieuwe aanvragen toe aan _commandoWachtrij, die
    /// maar 1 per WisselRustpauzeMilliseconden (hier 600ms = 1,67/sec) verwerkt verwerkt -
    /// netto 7,5 aanvragen/sec erbij tegen 1,67/sec eraf, dus _commandoWachtrij groeit,
    /// zolang de hardware verbonden blijft, ONBEPERKT (exact dezelfde wiskunde als de
    /// eerder gevonden kruiswissel-bug hierboven, nu toegepast op melderstatus i.p.v.
    /// wisselcommando's). Dat raakt weliswaar niet meer de wisselcommando's zelf (die
    /// hebben intussen hun eigen prioriteits-wachtrij), maar de STEEDS DOORLOPENDE,
    /// 1-per-600ms-druppel vanuit die achterstand belandt via dinamo.VraagMelderStatusOp
    /// wél in DinamoHardware se GEWONE (niet-prioriteits) _teVersturen - exact dezelfde
    /// lijst/stuur-timer (1 pakketje/200ms) die ook de snelheidsramp (ZetLocSnelheid, via
    /// StuurLocSnelheidCommando hieronder, BEWUST buiten elke wachtrij om) gebruikt. Die
    /// voortdurende extra belasting drukt de effectieve snelheid-aan-doorvoer omlaag,
    /// precies de waargenomen 2-seconden-per-stap vertraging - en een inmiddels zo'n
    /// oude, aan een allang-niet-meer-actuele situatie beantwoordende melderstatus kan de
    /// software later verrassen als "onverwachte" (spook)melding.
    /// ECHTE FIX: een melderstatus-AANVRAAG is, anders dan een wissel-/seincommando, geen
    /// fysieke motorbeweging - er is geen enkele mechanische reden om deze dezelfde
    /// "rustpauze tussen wisselcommando's" (WisselRustpauzeMilliseconden) op te leggen. Nu
    /// de wissel-/seincommando's toch al hun eigen, van deze wachtrij onafhankelijke
    /// prioriteits-lane hebben (zie hierboven) - en KlaarVoorVolgendeWisselCommando puur op
    /// die lane let, niet op _teVersturen - is er geen reden meer om melderstatus-aanvragen
    /// via DEZE, 600ms-gepauzeerde wachtrij te laten lopen. Gaat daarom, net als
    /// StuurLocSnelheidCommando/StuurFunctieCommando hieronder, nu RECHTSTREEKS naar de
    /// hardware - de enige échte beperking (Dinamo se eigen 200ms-seriële-cyclus) wordt
    /// toch al, onafhankelijk hiervan, door DinamoHardware se eigen _teVersturen/_stuurTimer
    /// afgedwongen; een TWEEDE, kunstmatige 600ms-pauze daar bovenop diende geen enkel doel
    /// meer en veroorzaakte alleen deze onbeperkte achterstand.</summary>
    public void VraagMelderStatusOp(int meldernummer)
    {
        if (Huidige is DinamoHardware dinamo)
            dinamo.VraagMelderStatusOp(meldernummer);
    }

    /// <summary>Handmatig rijden: BEWUST NIET via dezelfde wachtrij/rustpauze als wissel-
    /// en sein-commando's - een handmatige snelheidsregelaar (bijv. een schuifbalk) moet
    /// direct reageren, niet achter een burst wissel-commando's aan wachten.</summary>
    /// <param name="blokNummer">Het blok (Blok.Nummer) waar deze loc op dit moment staat -
    /// 0 als onbekend. Alleen relevant voor Dinamo (die DCC-commando's per blok routeert,
    /// zie IHardwareInterface); andere interfaces negeren dit.</param>
    /// <param name="stappen">Het aantal snelheidsstappen waarin de decoder daadwerkelijk is
    /// geconfigureerd (Trein.DecoderStappen). Alleen relevant voor Dinamo (28-staps vs
    /// 126-staps commando, zie IHardwareInterface).</param>
    public void StuurLocSnelheidCommando(int decoderAdres, int stap, bool vooruit, int blokNummer = 0, int stappen = 126) => Huidige.ZetLocSnelheid(decoderAdres, stap, vooruit, blokNummer, stappen);

    /// <summary>GEVONDEN, ZELFDE GAT ALS VraagMelderStatusOp HIERBOVEN (gebruikerswaarneming:
    /// "ik hoor nog steeds geen enkel wissel schakelen", ook na de VraagMelderStatusOp-fix):
    /// naast de melderstatus-vloedgolf bleek MainWindow.StopAlleBekendeGeplaatsteLocs (bij
    /// élke (opnieuw) verbinding aangeroepen, vlak NA de wissel-initialisatie) óók
    /// rechtstreeks, ongepauzeerd StuurLocSnelheidCommando aan te roepen - voor ELKE bekende
    /// loc naar ELK blok in het project (dus al snel 10-30+ commando's ineens, zie die
    /// methode). Dat is BEWUST zo voor een interactieve snelheidsregelaar (StuurLocSnelheid
    /// Commando hierboven moet direct reageren), maar een eenmalige, niet-urgente
    /// veiligheidsbroadcast bij het verbinden heeft die directheid niet nodig en overspoelde
    /// zo alsnog Dinamo se verzendwachtrij, nog vóórdat (of terwijl) de - inmiddels wél
    /// gepauzeerde - wisselcommando's en melderstatus-aanvragen aan de beurt kwamen. Deze
    /// variant stuurt zo'n bulk-broadcast daarom WEL via dezelfde wachtrij/pauze, zodat hij
    /// niet langer voordringt. NOOIT gebruiken voor een actieve noodstop tijdens het rijden
    /// (TreinrouteWindow.VoerNoodstopUit) - daar moet een stopcommando juist zo snel
    /// mogelijk, zonder wachtrij, verstuurd worden.</summary>
    public void StuurLocSnelheidCommandoGepauzeerd(int decoderAdres, int stap, bool vooruit, int blokNummer = 0, int stappen = 126) =>
        InWachtrijZetten(() => Huidige.ZetLocSnelheid(decoderAdres, stap, vooruit, blokNummer, stappen));

    /// <summary>Een loc-functie (F0-F28) daadwerkelijk naar de decoder sturen - net als
    /// StuurLocSnelheidCommando hierboven BEWUST NIET via de wissel/sein-wachtrij (directe
    /// respons nodig, geen zin om op een rustpauze te wachten voor bijv. een lichtje).</summary>
    /// <param name="blokNummer">Zie StuurLocSnelheidCommando hierboven.</param>
    public void StuurFunctieCommando(int decoderAdres, int functieNummer, bool aan, int blokNummer = 0) => Huidige.ZetFunctie(decoderAdres, functieNummer, aan, blokNummer);

    /// <summary>GEVONDEN, KRITIEKE RACE CONDITION (gebruikerswaarneming: "de wissels worden
    /// bij het opstarten niet sequentieel geïnitialiseerd, waardoor het kruiswissel in de
    /// verkeerde stand stond" - en, eerder, losse, aaneengeplakte hardwarebytes): het
    /// eerste commando in een burst werd altijd METEEN, synchroon verwerkt (zie de
    /// verwijderde regel hieronder) - waardoor de wachtrij na élk commando weer leeg was,
    /// de timer meteen weer stopte, en het VOLGENDE commando (nog binnen dezelfde
    /// synchrone lus, bijv. een foreach over alle wissels) OOK als "eerste, geen wachtrij"
    /// werd behandeld. WisselRustpauzeMilliseconden werd zo voor een REEKS commando'sin de
    /// praktijk nooit toegepast - ze vuurden allemaal ogenblikkelijk na elkaar, ongeacht de
    /// ingestelde pauze. Nu verwerkt UITSLUITEND de timer se eigen Tick nog commando's -
    /// ook het allereerste - zodat de pauze altijd, consistent, voor ELK commando geldt.
    /// Enige nadeel: een LOS, geïsoleerd commando (bijv. één wisselklik) wacht nu ook zelf
    /// die ene pauze voordat het verstuurd wordt, i.p.v. instant te gaan - een kleine,
    /// nauwelijks merkbare vertraging, ruimschoots opwegend tegen de ernst van deze bug.</summary>
    private void InPrioriteitsWachtrijZetten(Action commando)
    {
        _prioriteitsWachtrij.Enqueue(commando);
        StartWachtrijTimerIndienNodig();
    }

    private void InWachtrijZetten(Action commando)
    {
        _commandoWachtrij.Enqueue(commando);
        StartWachtrijTimerIndienNodig();
    }

    private void StartWachtrijTimerIndienNodig()
    {
        if (!_wachtrijTimer.IsEnabled)
        {
            _wachtrijTimer.Interval = TimeSpan.FromMilliseconds(SimulatieInstellingen.WisselRustpauzeMilliseconden);
            _wachtrijTimer.Start();
        }
    }

    private void VerwerkVolgendeCommando()
    {
        // Zie _prioriteitsWachtrij hierboven: ALTIJD eerst een eventueel wachtend wissel-/
        // seincommando verwerken, pas daarna de normale (niet-urgente) wachtrij - zo kan een
        // opgelopen achterstand aan melderstatus-aanvragen een nieuw wisselcommando nooit
        // meer blokkeren.
        var wachtrij = _prioriteitsWachtrij.Count > 0 ? _prioriteitsWachtrij : _commandoWachtrij;
        if (wachtrij.Count == 0)
        {
            _wachtrijTimer.Stop();
            return;
        }
        // Zie IHardwareInterface.KlaarVoorVolgendeWisselCommando: pas het volgende
        // commando daadwerkelijk aanleveren als Dinamo se EIGEN verzendwachtrij leeg is -
        // anders wacht dit commando gewoon nog een tik (de timer blijft lopen, er wordt
        // niets overgeslagen) in plaats van alsnog vast achteraan bij Dinamo te belanden.
        if (!Huidige.KlaarVoorVolgendeWisselCommando) return;
        var commando = wachtrij.Dequeue();
        commando();
        if (_prioriteitsWachtrij.Count == 0 && _commandoWachtrij.Count == 0) _wachtrijTimer.Stop();
    }
}
