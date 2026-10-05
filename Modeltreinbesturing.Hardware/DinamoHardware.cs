using System.IO.Ports;

namespace Modeltreinbesturing.Hardware;

/// <summary>
/// VPEB Dinamo (RM-H/RM-U/UCCI), geïmplementeerd volgens de "Dinamo Interface
/// Specificatie 3.2" (Leon J.A. van Perlo, 28 sep 2016) die de gebruiker heeft
/// aangeleverd.
///
/// Communicatielaag (§2): RS232, 19200 bps, 8 databits, ODD pariteit. Een datagram
/// bestaat uit: header-byte (bit7=0) + 0..7 data-bytes (elk bit7=1, 7 bits payload) +
/// checksum-byte (bit7=1). Header-byte layout: bit6=Toggle, bit5=Fault, bit4=Hold,
/// bit3=1 (protocol 3.x, normaal datagram), bit2-0 = aantal databytes (0-7). Checksum:
/// zodanig dat de som van alle bytes in het datagram (bit7 genegeerd, dus modulo 128)
/// nul is.
///
/// Vereenvoudiging t.o.v. de volledige spec: dit is geen volledig master-slave
/// handshake-protocol met hertransmissie bij uitblijven van een geldig antwoord binnen
/// 200ms (§2.1) - dat vergt een eigen wacht-op-antwoord-lus. Hier wordt periodiek
/// verstuurd (elke 200ms) en worden inkomende Switch-events (bezetmeldingen)
/// opportunistisch verwerkt. Voor de meeste toepassingen (wissels zetten, simpele
/// bezetmelding) is dit voldoende; bij zeer drukke/onbetrouwbare verbindingen zou een
/// echte ack-gebaseerde lus robuuster zijn.
///
/// Rijden/functies (§3.4, "Besturing van snelheid en functies"): de gebruiker heeft de
/// volledige spec alsnog aangeleverd, dus dit stuk (eerder een bewust NIET-geïmplementeerd
/// gat) is nu wel gebouwd - "DCC 126 Snelh." voor snelheid, "DCC Functiegroep 1,2a,2b"
/// voor F0 t/m F12 en "DCC advanced control" (F13-F20/F21-F28-subcommando's) voor de rest.
/// BELANGRIJK ARCHITECTUUR-VERSCHIL met DccEx/Intellibox: Dinamo's DCC-commando's worden
/// altijd VIA EEN BLOK verstuurd (niet rechtstreeks naar een decoderadres) - vandaar de
/// blokNummer-parameter op ZetLocSnelheid/ZetFunctie (zie IHardwareInterface), die de
/// aanroeper (TreinrouteWindow/BaanontwerpWindow) meegeeft op basis van waar de loc op dat
/// moment staat (BlokBeheerder.BlokVanLoc / RijdendeTrein.HuidigBlok). Zonder bekend blok
/// kan dus geen commando verstuurd worden - dat wordt dan duidelijk gemeld i.p.v. blok 0
/// (een ander, geldig blok) te gebruiken. Nog NIET getest tegen echte hardware.
/// </summary>
public class DinamoHardware : IHardwareInterface, IBlokAlarmBron
{
    private SerialPort? _poort;
    private System.Threading.Timer? _stuurTimer;
    // GEVONDEN, DERDE LAAG VAN HETZELFDE QUEUE-GAT (gebruikerswaarneming: "reservering ging
    // naar blok 6 maar de trein reed niet" nadat blok 6 een tijdlang VERGRENDELD had gestaan):
    // tijdens zo'n wachtperiode stuurt AutomatischeStapProberen ELKE SECONDE opnieuw een
    // stop-commando naar ALLE blokken (en alle kruiswissel-rijadressen) - bewust zo gebouwd
    // (zie TreinrouteWindow, "GEVONDEN VEILIGHEIDSGAT") zodat de loc ECHT stilstaat, ongeacht
    // op welk blokadres hij toevallig luistert. Bij een wachtperiode van 1-2 minuten (zoals
    // hier) stapelen die herhaalde stop-commando's zich op in deze simpele FIFO-wachtrij
    // (net als het wissel-probleem hierboven, maar dan snelheidscommando's ACHTER ELKAAR i.p.v.
    // achter een wisselcommando) - bij 5 pakketjes/sec verzendcapaciteit en bijv. 8
    // stop-commando's/sec aan nieuwe aanvoer (7 blokken + kruiswisselsecties) loopt de
    // achterstand op tot ver boven de 30-37 sec vastloop-veiligheidstimeout. Het ECHTE
    // vertrekcommando (zodra blok 6 weer vrijkomt) werd keurig aangeboden, maar kwam pas
    // ver NA die timeout daadwerkelijk de poort uit - de software had toen allang "vastgelopen"
    // geconcludeerd terwijl de loc feitelijk nog op het allang verouderde stop-commando stond
    // te wachten.
    //
    // EERSTE FIXPOGING was te agressief gebleken (testrit 09:21-09:22: een ECHT
    // vertrekcommando verdween spoorloos, direct na aankomst in blok 4 - geen enkel
    // snelheidscommando >0 kwam de poort meer uit): die versie verving bij elk nieuw
    // commando VOOR dezelfde (decoderAdres, blokNummer)-combinatie domweg het oude, nog
    // niet verstuurde commando, ongeacht de WAARDE. Als er - door twee kort na elkaar
    // lopende aanroepen voor dezelfde trein (bijv. een alsnog afgaande oude herhaal-timer
    // vlak na een echte aankomst) - eerst een ECHT vertrekcommando (snelheid > 0) en
    // vlak daarna een ANDER, zelfstandig stop-commando (snelheid = 0) voor diezelfde
    // combinatie in de wachtrij belandden, ving die "vervang het oude" logica niet alleen
    // de bedoelde verouderde HERHALINGEN, maar ook zo'n echt vers, inhoudelijk ANDER
    // vertrekcommando - dat werd dan alsnog overschreven door het latere stop-commando,
    // zonder enige foutmelding. Twee WERKELIJK verschillende, opeenvolgende besluiten voor
    // dezelfde loc+blok (eerst "rijden", dan "stoppen", of andersom) horen allebei, in
    // volgorde, verstuurd te worden - alleen EXACTE, inhoudsloze herhalingen (zelfde
    // snelheid, zelfde richting, zelfde blok - zoals de herhaalde stop-broadcast hierboven,
    // die 130 sec lang byte-voor-byte identiek blijft) mogen zonder risico worden
    // samengevoegd. Fix: in plaats van "vervang het oude commando voor deze sleutel",
    // nu "sla een NIEUW commando over als er al een BYTE-IDENTIEK (dus inhoudelijk
    // exact hetzelfde) commando voor dezelfde sleutel klaarstaat". Zo wordt de herhaalde
    // stop-broadcast nog steeds tot hooguit 1 wachtend exemplaar per blok teruggebracht
    // (dezelfde oplossing als hierboven bedoeld), maar wordt een inhoudelijk ANDER
    // commando - of het nu rijden-na-stoppen of stoppen-na-rijden is - nooit meer
    // stilzwijgend weggegooid; dat komt gewoon, in de juiste volgorde, alsnog de deur uit.
    // Functie-/resetfault-commando's (zeldzaam, geen herhalingsrisico) blijven gewoon FIFO,
    // zonder sleutel.
    private readonly List<(byte[] Payload, (int DecoderAdres, int BlokNummer)? SnelheidsSleutel)> _teVersturen = new();
    // GEVONDEN, ECHT VEILIGHEIDSGAT (gebruikerswaarneming + bytes-analyse: wissel 14 werd pas
    // 5 SECONDEN nadat de software 'm had besloten daadwerkelijk fysiek omgezet, terwijl de
    // loc er op dat moment al overheen reed): dit was geen logica-fout in WANNEER de wissel
    // gezet wordt, maar een wachtrij-probleem: ZetWissel/ZetSein en de snelheidscommando's
    // (die tijdens een rijdende trein continu, tot elke 100ms, opnieuw ververst worden - zie
    // TreinrouteWindow's snelheidsramp) belandden allemaal in dezelfde, simpele FIFO-
    // _teVersturen-wachtrij, die de stuur-timer hieronder maar 1 pakketje per 200ms
    // leegtrekt. Bij een drukke snelheidsramp (tot 2-3 pakketjes per 100ms, dus tot 6x
    // sneller dan de wachtrij leegloopt) kon een net ingevoerd wisselcommando dus zomaar 20-
    // 30 pakketjes (4-6 sec) achteraan moeten wachten voordat hij verstuurd werd - exact de
    // vertraging die hier waargenomen is. Wissel-/seincommando's (ZetWissel/ZetSein) gaan
    // daarom nu in een aparte, PRIORITEITS-wachtrij die de stuur-timer altijd eerst leegt,
    // vóór de gewone snelheids-/functiecommando's - zo hoeft een wisselcommando nooit meer
    // achter een snelheidsrefresh-backlog te wachten.
    private readonly Queue<byte[]> _teVersturenPrioriteit = new();
    private readonly object _vergrendeling = new();
    private bool _toggleBit;
    private int _faultBitResterendeCycli;
    private bool _dinamoMeldeFoutVorigeKeer; // voor het loggen van alleen de OVERGANG (aan/uit), niet elke 200ms opnieuw

    public bool Verbonden { get; private set; }
    public string Naam => "VPEB Dinamo";

    /// <summary>Zie IHardwareInterface: Dinamo stuurt DCC-commando's altijd via een blok.</summary>
    public bool LocCommandoVereistBlok => true;

    /// <summary>Zie IHardwareInterface en VraagMelderStatusOp hieronder.</summary>
    public bool KanMelderStatusOpvragen => true;

    // GEVONDEN, TWEEDE LAAG VAN HETZELFDE GAT (testrit 15:53-15:55: exact dezelfde 5+ sec
    // vertraging kwam TERUG, ook na de prioriteits-wachtrij hierboven): het bleek niet
    // genoeg om ZetWissel/ZetSein zelf voor te laten dringen BINNEN deze klasse - de
    // AANROEPER (HardwareBeheerder.VerwerkVolgendeCommando) haalt een wisselcommando pas
    // uit ZIJN eigen wachtrij (_commandoWachtrij) op het moment dat deze property "true"
    // teruggeeft, en gaf het tot nu toe pas vrij zodra OOK _teVersturen (de gewone snelheids-
    // /functiecommando's) helemaal leeg was. Maar StuurLocSnelheidCommando (de snelheidsramp,
    // tot elke 100ms ververst) gaat BEWUST buiten die wachtrij om rechtstreeks naar
    // _teVersturen (zie HardwareBeheerder) - dus zolang de ramp bezig is/ blijft bijvullen,
    // bleef _teVersturen.Count>0 en gaf deze property dus GEEN groen licht, waardoor het
    // wisselcommando in _commandoWachtrij zelf al die tijd geblokkeerd bleef en nooit eens de
    // kans kreeg om (via ZetWissel) in _teVersturenPrioriteit terecht te komen - de
    // prioriteits-wachtrij hierboven werd zo feitelijk nooit bereikt. Deze property moet dus
    // uitsluitend gaan over wisselverkeer ONDERLING (is het VORIGE wissel-/seincommando al
    // daadwerkelijk de deur uit, zodat Koploper se ingestelde rustpauze gerespecteerd blijft
    // tussen opeenvolgende wisselcommando's) - niet over onafhankelijk snelheidsverkeer, dat
    // toch al altijd achter de prioriteits-wachtrij aansluit (zie VerstuurVolgendeInWachtrij).
    public bool KlaarVoorVolgendeWisselCommando
    {
        get { lock (_vergrendeling) return _teVersturenPrioriteit.Count == 0; }
    }

    public event Action<int, bool>? BezetmeldingGewijzigd;
    public event Action<string>? StatusBericht;
    public event Action<bool>? KortsluitingStatusGewijzigd;

    /// <summary>BUG #43: Dinamo meldt een kortsluiting niet alleen via het globale F-bit in de
    /// header, maar ook PER BLOK met een eigen 2-byte "Block Alarm"-datagram:
    /// (0011000S)(bbbbbbb) - byte 1 = 0x30 | (kortsluiting ? 0x02 : 0) | blokbit 7, byte 2 =
    /// blokbit 6..0 (0-based, dus +1 voor Blok.Nummer). Bewezen uit de echte logs van
    /// 04-10-2026: tijdens "adres 2 afbuigend" met de loc op melder 5 kwam 0,7 s later
    /// `4AB28282` + `0AB283C1` binnen = kortsluiting in blok 3 én 4, zonder dat het F-bit ooit
    /// werd gezet. Onze code negeerde deze datagrammen tot nu toe. Bron voor de
    /// bitindeling: Traintastic (BlockAlarm in dinamomessages.hpp), bevestigd tegen onze logs.</summary>
    public event Action<int, bool>? BlokAlarmGewijzigd;
    private readonly Dictionary<int, bool> _laatsteBlokAlarm = new();

    public Task VerbindenAsync(string comPoort)
    {
        return Task.Run(() =>
        {
            try
            {
                // Een eventuele AL BESTAANDE verbinding EERST netjes afsluiten - anders
                // blijft die oude SerialPort de poort op OS-niveau vasthouden (ook al heeft
                // de applicatie er zelf geen actieve verwijzing meer naar), waardoor een
                // nieuwe poging - OOK vanuit dezelfde applicatie - "Access to the path
                // 'COMx' is denied" kan geven (gebruikersmelding: kreeg deze foutmelding
                // pas na een eerdere, al geslaagde verbinding, niet meteen).
                Ontkoppelen();

                _poort = new SerialPort(comPoort, 19200, Parity.Odd, 8, StopBits.One)
                {
                    ReadTimeout = 500,
                    WriteTimeout = 500
                };
                _poort.DataReceived += Poort_DataReceived;
                _poort.Open();
                Verbonden = true;
                StatusBericht?.Invoke($"Verbonden met Dinamo op {comPoort} (19200 baud, odd pariteit).");

                // De PC is Master en moet continu datagrammen sturen (NULL-datagram als er
                // niets te versturen is) - anders gaat Dinamo na 2 seconden stilte zelf in
                // Fault-mode. 200ms is de in de spec genoemde praktische ronde-tijd.
                _stuurTimer = new System.Threading.Timer(_ => VerstuurVolgendeInWachtrij(), null, 200, 200);

                // KRITIEKE STAP (gebruikersbevinding, via bytes-analyse van 3 aangeleverde
                // logs): in ALLE geteste sessies bleek Dinamo's EIGEN F(ault)-bit al vanaf
                // het allereerste ontvangen datagram op 1 te staan - dus Dinamo dacht de
                // hele tijd in foutmodus te zitten. De spec is daar glashelder over: "Er
                // rijden geen voertuigen zolang F=1." Dit verklaart dus waarom er nooit een
                // loc bewoog, OOK NADAT het snelheidscommando zelf al correct bleek (bytes/
                // checksums klopten telkens exact) - Dinamo negeerde ze simpelweg zolang hij
                // in deze staat zat. Zonder dit "Reset Fault"-commando (§3.1) blijft Dinamo
                // daar permanent in hangen; het is dus onderdeel van een nette opstart-
                // handshake, niet iets dat vanzelf overgaat (in tegenstelling tot onze
                // EIGEN, tijdelijke PC->Dinamo F-bit via Noodstop() hierboven, die na een
                // paar cycli vanzelf weer loslaat).
                StuurResetFault();
            }
            catch (Exception ex)
            {
                Verbonden = false;
                throw new InvalidOperationException($"Kon niet verbinden met Dinamo op {comPoort}: {ex.Message}", ex);
            }
        });
    }

    public void Ontkoppelen()
    {
        if (_poort is null) return; // nog nooit verbonden geweest - niets te ontkoppelen, geen misleidende melding nodig
        _stuurTimer?.Dispose();
        _stuurTimer = null;
        _poort.DataReceived -= Poort_DataReceived;
        try { _poort.Close(); } catch { /* poort was mogelijk al weg */ }
        _poort.Dispose();
        _poort = null;
        Verbonden = false;
        StatusBericht?.Invoke("Dinamo ontkoppeld.");
    }

    /// <summary>"Reset Fault" (§3.1, System Control): (0000001)(0000000) - "Schoont de
    /// foutstatus van Dinamo inclusief de aangesloten subsystemen." Zie VerbindenAsync
    /// hierboven voor waarom dit cruciaal is. Wordt zowel bij het verbinden verstuurd als
    /// automatisch herhaald zolang Poort_DataReceived Dinamo's eigen F-bit=1 blijft zien
    /// (zie hieronder) - zodat de verbinding ook herstelt van een latere, ECHTE Dinamo-
    /// fout (bijv. kortsluiting), niet alleen van de opstartstatus.</summary>
    private void StuurResetFault() => VerstuurDatagram(new byte[] { 0x01, 0x00 });

    /// <summary>Magneetartikel-commando (§3.3): "(0010CMM) (mmmmmmm) [(tijd)]" - zet een
    /// puls op een wisselspoel. MMmmmmmmm (9 bits, 0..511) is het spoelnummer, C is de
    /// gewenste stand (0=rechtdoor, 1=afbuigend).
    /// GEVONDEN, DEFINITIEF BEVESTIGDE FORMAATFOUT (Wireshark-USB-vergelijking tussen de
    /// ECHTE Koploper-software en deze applicatie, op DEZELFDE hardware/wissel-adressen -
    /// exact dezelfde methode die destijds de loc-snelheidsbug aan het licht bracht):
    /// Koploper's daadwerkelijk over de kabel verstuurde bytes bleken een prefix van
    /// "0001" te gebruiken, NIET "0010" zoals de spec-tekst hierboven (en de eerdere
    /// implementatie) deed vermoeden - en Koploper laat de optionele tijd-byte NOOIT weg
    /// (observeerde consequent 0x0c, ongeacht wisseladres/stand). Zonder deze twee
    /// correcties herkende de echte Dinamo-centrale het commando kennelijk niet - de
    /// software claimde iets te versturen (en deed dat byte-technisch ook, richting de
    /// USB-poort), maar de wissel bewoog fysiek nooit.
    /// TWEEDE, DIEPER GAT (gebruikerswaarneming: "wissels zijn niet defect, in Koploper
    /// werkt alles perfect" - gevolgd door onderzoek naar hoe een kruiswissel/Engelse
    /// wissel daadwerkelijk werkt): een webbron over Koploper's eigen DCC-verkeer
    /// beschrijft expliciet "Koploper stuurt het wisselcommando meerdere keren uit
    /// afhankelijk van de gekozen bekrachtigingstijd - standaard 250ms, dus 10x om de
    /// ca. 25ms hetzelfde commando". Onze software stuurde het maar ÉÉN KEER, met een
    /// vaste tijd-byte waarvan we AANNAMEN dat Dinamo die zelf gebruikt om de puls intern
    /// te verlengen - een aanname die nooit geverifieerd is. Als dat niet (voldoende)
    /// gebeurt, krijgt een zwaardere kruiswissel (twee motoren) een te korte puls om
    /// volledig door te slaan, terwijl een lichtere gewone wissel er nog net genoeg aan
    /// heeft - exact het patroon dat we zagen: Dinamo bevestigt het commando keurig (het
    /// ONTVANGT en BEGRIJPT het) - de puls was alleen te kort om 'm ook echt af te maken.
    /// Stuurt het commando daarom meerdere keren - zie VerstuurVolgendeInWachtrij/
    /// VerstuurEnkelPakket voor de VIERDE, meest recente laag van dit probleem (gebruikers-
    /// melding "Beide wisseltongen staan verkeerd", NA deze 3x-fix): die herhalingen gingen
    /// voorheen verspreid over meerdere 200ms-ticks de deur uit (grote gaten ertussen), wat
    /// voor een zware kruiswissel nog altijd niet genoeg bleek. Worden nu, dankzij die fix,
    /// ACHTER ELKAAR (binnen enkele milliseconden) verstuurd - zodat de puls daadwerkelijk
    /// aaneengesloten blijft in plaats van een paar incidentele "tikken" met rust ertussen.</summary>
    public void ZetWissel(int adres, bool afbuigend)
    {
        int a = Math.Clamp(adres - 1, 0, 511); // 0-based, zelfde conventie als IntelliboxHardware
        byte byte1 = (byte)(0b0001_0000 | ((afbuigend ? 1 : 0) << 2) | ((a >> 7) & 0x03));
        byte byte2 = (byte)(a & 0x7F);
        byte byte3 = 0x0c; // tijd - Koploper laat dit nooit weg, altijd deze waarde geobserveerd
        for (int i = 0; i < AantalHerhalingenWisselcommando; i++)
            VerstuurDatagram(new byte[] { byte1, byte2, byte3 }, prioriteit: true);
    }

    /// <summary>Zie ZetWissel hierboven voor de volledige toelichting - hoeveel keer
    /// hetzelfde wissel-/seincommando verstuurd wordt. Worden sinds de laatste fix (zie
    /// VerstuurVolgendeInWachtrij) ACHTER ELKAAR verstuurd i.p.v. verspreid over meerdere
    /// 200ms-ticks, dus dit aantal verhoogd van 3 naar 6 (Koploper doet dit zelf 10x/25ms;
    /// 6 snel-achter-elkaar-verstuurde identieke commando's geeft ruim voldoende marge,
    /// zonder de prioriteits-wachtrij nodeloos lang te maken) - geeft extra zekerheid dat
    /// ook een zware, twee-motorige kruiswissel een lang genoeg aaneengesloten puls krijgt.</summary>
    private const int AantalHerhalingenWisselcommando = 6;

    /// <summary>Gebruikt hetzelfde Magneetartikel-commando (§3.3) als ZetWissel (incl.
    /// dezelfde, Wireshark-geverifieerde prefix/tijd-byte-correctie - zie ZetWissel): elk
    /// aspect van het sein krijgt een eigen "spoel"-adres (rood/veilig als de twee
    /// standen). Dat is een aanname (veel Dinamo-seinopstellingen werken zo, maar niet
    /// per se allemaal) - werkt het niet met jouw aansluiting, dan zul je dit moeten
    /// aanpassen naar een Digitale Uitgang (§3.3) i.p.v. een magneetartikel.</summary>
    /// <summary>Noodstop volgens §2.2 van de specificatie: het F-bit (Fault) in de header-
    /// byte. "Als deze [door de PC] gezet is worden alle voertuigen gestopt." Dit is een
    /// aanhoudende status, geen los commando - dus we houden 'm een paar verstuurcycli
    /// (~1 seconde bij 200ms/cyclus) aan om zeker te zijn dat Dinamo 'm ontvangt, en laten
    /// daarna vanzelf weer los (Dinamo hervat dan de snelheid van vóór de noodstop, exact
    /// zoals de spec beschrijft) - de daadwerkelijke rit-afhandeling is al door de
    /// software-noodstop in TreinrouteWindow gedaan, dit is puur de fysieke "kill"-puls.</summary>
    public void Noodstop() => _faultBitResterendeCycli = 5;

    public void ZetSein(int adres, bool onveiligRood)
    {
        int a = Math.Clamp(adres - 1, 0, 511);
        byte byte1 = (byte)(0b0001_0000 | ((onveiligRood ? 1 : 0) << 2) | ((a >> 7) & 0x03));
        byte byte2 = (byte)(a & 0x7F);
        byte byte3 = 0x0c;
        for (int i = 0; i < AantalHerhalingenWisselcommando; i++)
            VerstuurDatagram(new byte[] { byte1, byte2, byte3 }, prioriteit: true);
    }

    /// <summary>"Switch Request" (§3.5): "(11CSSSS)(sssssss)" - vraagt de HUIDIGE stand van
    /// een specifieke schakelaar/bezetmelder op (C in het commando zelf is niet relevant,
    /// zie spec: "In het commando is C niet relevant"). Dinamo antwoordt met hetzelfde
    /// bitpatroon terug (herkend in Poort_DataReceived, C=1 betekent daar actief/bezet).
    ///
    /// GEVONDEN GAT (gebruikerscasus: een loc die al PRECIES op de verwachte melder stond
    /// toen de rit startte - de software wachtte daarna voor altijd op een "wordt bezet"-
    /// OVERGANG die nooit kwam, want Dinamo meldt alleen WIJZIGINGEN spontaan, nooit de
    /// reeds-bestaande stand): zonder dit commando was de enige optie afwachten tot
    /// MeldTreinVastgelopen de rit na de wachttijd als vastgelopen bestempelt - correct qua
    /// eerlijkheid, maar onnodig als de melder in werkelijkheid allang bezet is. Door deze
    /// stand nu ACTIEF op te vragen zodra we op een melder gaan wachten (zie
    /// TreinrouteWindow.PlanVolgendeGebeurtenis/AutomatischeStapProberen), krijgen we
    /// meteen antwoord i.p.v. te moeten wachten op een overgang die er nooit komt.</summary>
    public void VraagMelderStatusOp(int meldernummer)
    {
        int a = Math.Clamp(meldernummer - 1, 0, 2047); // 0-based, zelfde conventie als de bezetmelding zelf (Poort_DataReceived: +1 bij het binnenkomen)
        byte byte1 = (byte)(0b1100_0000 | ((a >> 7) & 0x0F));
        byte byte2 = (byte)(a & 0x7F);
        VerstuurDatagram(new byte[] { byte1, byte2 });
    }

    /// <summary>Blokadres (§3.4, "Bbbbbbbb"): een 8-bits blokadres, MAAR opgesplitst over 2
    /// bytes van de spec's eigen 7-bits payload - de MSB ("B") zit als LAATSTE bit van het
    /// COMMANDO-byte (bijv. "010110B"), de overige 7 bits ("bbbbbbb") vormen het volgende,
    /// volledige databyte. Alle blok-georiënteerde §3.4/3.5-commando's (Link/Unlink/
    /// Kickstart/Blokbesturing/DCC-commando's) delen deze opbouw.
    ///
    /// GEVONDEN VIA EEN ECHTE KOPLOPER-VERGELIJKING (Wireshark-opname van Koploper's eigen
    /// USB-verkeer naast onze eigen hardware-communicatielog, voor exact dezelfde loc op
    /// exact dezelfde fysieke sectie): Dinamo's EIGEN blokadressering is 0-based (het eerste
    /// fysieke blok heeft adres 0), terwijl Blok.Nummer in deze software 1-based is (het
    /// eerst aangemaakte blok krijgt Nummer=1). Koploper stuurde voor deze sectie
    /// consequent blokadres 0, terwijl onze software (vóór deze fix) 1 stuurde - dus
    /// protocol-correcte commando's, maar naar de VERKEERDE fysieke rail-sectie. Vandaar
    /// de "-1" hieronder.</summary>
    private static (byte CommandoMsbBit, byte BlokLsbByte) SplitsBlokAdres(int blokNummer)
    {
        int b = Math.Clamp(blokNummer - 1, 0, 255);
        return ((byte)((b >> 7) & 0x01), (byte)(b & 0x7F));
    }

    /// <summary>Decoderadres-bytes (§3.4, toelichting bij "DCC snelheid/functies"): een
    /// KORT (7-bits, 0..127) adres is gewoon 1 databyte ("ddddddd"), GEEN 2e byte
    /// meesturen. Een LANG (14-bits, tot 10239) adres stuurt WEL een 2e byte
    /// ("[(DDDDDDD)]") mee - LET OP: al is dat 2e byte toevallig 0, de AANWEZIGHEID ervan
    /// alleen al maakt het een lang-adres-interpretatie voor Dinamo. Dinamo vult zelf de
    /// vereiste top-2-bits (altijd 11) van het hoge deel aan - wij sturen alleen de 14
    /// daadwerkelijke adresbits.</summary>
    private static byte[] DecoderAdresBytes(int decoderAdres)
    {
        int a = Math.Clamp(decoderAdres, 0, 10239);
        return a <= 127
            ? new[] { (byte)a }
            : new[] { (byte)(a & 0x7F), (byte)((a >> 7) & 0x7F) };
    }

    /// <summary>Snelheid - kiest tussen twee Dinamo-commando's (§3.4) op basis van hoeveel
    /// stappen de decoder daadwerkelijk is geconfigureerd (Trein.DecoderStappen):
    ///
    /// GEVONDEN VIA EEN ECHTE KOPLOPER-VERGELIJKING (zie SplitsBlokAdres hierboven voor de
    /// opzet): eerder werd hier ALTIJD het 126-staps commando gebruikt (met als redenering
    /// "fijner en moderner"), maar Koploper bleek voor deze loc (28 stappen, de standaard-
    /// waarde in deze software) consequent het OUDERE 28-staps commando te sturen. Een
    /// decoder luistert maar naar één van de twee, afhankelijk van zijn eigen CV29-
    /// configuratie - het andere commando is voor hem onherkenbaar/genegeerd, ook al is het
    /// qua Dinamo-protocol zelf volledig correct opgebouwd. Dat verklaart dus waarom er
    /// nooit fysieke beweging was, ondanks bevestigd correcte bytes op de kabel.
    ///
    /// - stappen &lt;=28: "DCC Snelheid" (28-staps): (010100B)(bbbbbbb)(1RSSSSS)(ddddddd)
    ///   [(DDDDDDD)] - SSSSS=0..28 (5 bits).
    /// - stappen &gt;28: "DCC 126 Snelh." (126-staps): (010110B)(bbbbbbb)(000000R)
    ///   (SSSSSSS)(ddddddd)[(DDDDDDD)] - S=0..126 (7 bits).
    ///
    /// Vereist een BLOK (zie IHardwareInterface): zonder bekend blok kan Dinamo het
    /// commando niet routeren naar de juiste rail-sectie, dus wordt dan duidelijk gemeld
    /// i.p.v. blok 0 (een geldig, ander blok) te gebruiken.</summary>
    public void ZetLocSnelheid(int decoderAdres, int stap, bool vooruit, int blokNummer = 0, int stappen = 126)
    {
        if (blokNummer <= 0)
        {
            StatusBericht?.Invoke($"Kan geen snelheidscommando naar Dinamo sturen voor decoderadres {decoderAdres}: onbekend in welk blok deze loc staat (niet geplaatst?).");
            return;
        }
        var (msb, blokByte) = SplitsBlokAdres(blokNummer);
        List<byte> payload;
        if (stappen <= 28)
        {
            byte commando28 = (byte)(0b0101000 | msb);
            byte byte3 = (byte)(0b1000000 | ((vooruit ? 1 : 0) << 5) | Math.Clamp(stap, 0, 28));
            payload = new List<byte> { commando28, blokByte, byte3 };
        }
        else
        {
            byte commando126 = (byte)(0b0101100 | msb);
            byte controle = (byte)(vooruit ? 1 : 0);
            byte snelheid = (byte)Math.Clamp(stap, 0, 126);
            payload = new List<byte> { commando126, blokByte, controle, snelheid };
        }
        payload.AddRange(DecoderAdresBytes(decoderAdres));
        // Zie _teVersturen/VerstuurDatagram hierboven: coalesce-sleutel zodat een vers
        // snelheidscommando voor deze loc+blok nooit achter een stapel verouderde,
        // nog niet verstuurde commando's voor DEZELFDE combinatie hoeft te wachten.
        VerstuurDatagram(payload.ToArray(), snelheidsSleutel: (decoderAdres, blokNummer));
    }

    // Functies worden per GROEP verstuurd (§3.4 "DCC Functiegroep 1,2a,2b" en §3.4 "DCC
    // advanced control" F13-F20/F21-F28) - één commando zet ALLE bits van zijn groep
    // tegelijk, dus moet de laatst bekende stand van de REST van die groep meesturen,
    // anders zou het aanzetten van bijv. F2 per ongeluk F1/F3/F4 uitzetten. Bijgehouden per
    // DECODERADRES (niet per blok - functies horen bij de decoder, ongeacht via welk blok
    // het commando toevallig verstuurd wordt). Zelfde patroon als IntelliboxHardware's
    // DIRF/SND-state-tracking hierboven, alleen met Dinamo's eigen groepsindeling.
    private readonly Dictionary<int, bool> _laatsteLicht = new(); // F0
    private readonly Dictionary<int, bool[]> _laatsteF1TotF4 = new();
    private readonly Dictionary<int, bool[]> _laatsteF5TotF8 = new();
    private readonly Dictionary<int, bool[]> _laatsteF9TotF12 = new();
    private readonly Dictionary<int, bool[]> _laatsteF13TotF20 = new();
    private readonly Dictionary<int, bool[]> _laatsteF21TotF28 = new();

    /// <summary>"DCC Functiegroep 1,2a,2b" (§3.4): (010100B)(bbbbbbb)(0XXFFFF)(ddddddd)
    /// [(DDDDDDD)] - XX=00/01 stuurt F1-F4 (bit0=F1) + licht(F0) uit/aan, XX=10 stuurt
    /// F9-F12 (bit0=F9), XX=11 stuurt F5-F8 (bit0=F5). Drie aparte "sub-groepen" onder
    /// hetzelfde commando-nummer, wij sturen steeds maar 1 sub-groep tegelijk (degene die
    /// zojuist gewijzigd is).</summary>
    private void VerstuurFunctiegroep1(int decoderAdres, int blokNummer, int subGroep, bool[] vierBits)
    {
        var (msb, blokByte) = SplitsBlokAdres(blokNummer);
        byte commando = (byte)(0b0101000 | msb);
        bool licht = _laatsteLicht.TryGetValue(decoderAdres, out var l) && l;
        // XX: 00=F1-4+lichtUit, 01=F1-4+lichtAan, 10=F9-12, 11=F5-8
        int xx = subGroep switch { 1 => licht ? 0b01 : 0b00, 2 => 0b11, 3 => 0b10, _ => 0 };
        byte ffff = (byte)((vierBits[0] ? 1 : 0) | (vierBits[1] ? 2 : 0) | (vierBits[2] ? 4 : 0) | (vierBits[3] ? 8 : 0));
        byte byte3 = (byte)((xx << 4) | ffff);
        var payload = new List<byte> { commando, blokByte, byte3 };
        payload.AddRange(DecoderAdresBytes(decoderAdres));
        VerstuurDatagram(payload.ToArray());
    }

    /// <summary>"DCC advanced control" F13..F20 / F21..F28 (§3.4, subcommando's 000010F/
    /// 000011F): (010110B)(bbbbbbb)(00001{0,1}F)(fffffff)(ddddddd)[(DDDDDDD)] - F is het
    /// hoogste functienummer van de groep (F20 resp. F28), fffffff de overige 7 (bit0 =
    /// laagste van de groep, F13 resp. F21).</summary>
    private void VerstuurAdvancedFunctiegroep(int decoderAdres, int blokNummer, int laagsteNummer, bool[] achtBits)
    {
        var (msb, blokByte) = SplitsBlokAdres(blokNummer);
        byte commando = (byte)(0b0101100 | msb);
        int subCode = laagsteNummer == 13 ? 0b0000100 : 0b0000110; // 000010F resp. 000011F, F apart hieronder
        byte hoogsteBit = (byte)(achtBits[7] ? 1 : 0);
        byte byte3 = (byte)(subCode | hoogsteBit);
        byte byte4 = 0;
        for (int i = 0; i < 7; i++) if (achtBits[i]) byte4 |= (byte)(1 << i);
        var payload = new List<byte> { commando, blokByte, byte3, byte4 };
        payload.AddRange(DecoderAdresBytes(decoderAdres));
        VerstuurDatagram(payload.ToArray());
    }

    /// <summary>Zet een enkele functie (F0 t/m F28) - werkt de bijgehouden groepsstand
    /// voor deze decoder bij en verstuurt daarna ALLEEN de groep waarin functieNummer valt
    /// (zie klasse-uitleg hierboven over waarom een hele groep tegelijk moet).
    /// Vereist een BLOK, zelfde reden als ZetLocSnelheid hierboven.</summary>
    public void ZetFunctie(int decoderAdres, int functieNummer, bool aan, int blokNummer = 0)
    {
        if (blokNummer <= 0)
        {
            StatusBericht?.Invoke($"Kan geen functiecommando (F{functieNummer}) naar Dinamo sturen voor decoderadres {decoderAdres}: onbekend in welk blok deze loc staat (niet geplaatst?).");
            return;
        }
        switch (functieNummer)
        {
            case 0:
                _laatsteLicht[decoderAdres] = aan;
                var f1tot4Bij0 = _laatsteF1TotF4.TryGetValue(decoderAdres, out var bestaandBij0) ? bestaandBij0 : new bool[4];
                VerstuurFunctiegroep1(decoderAdres, blokNummer, 1, f1tot4Bij0);
                break;
            case >= 1 and <= 4:
                var f1tot4 = _laatsteF1TotF4.TryGetValue(decoderAdres, out var bestaand1) ? bestaand1 : new bool[4];
                f1tot4[functieNummer - 1] = aan;
                _laatsteF1TotF4[decoderAdres] = f1tot4;
                VerstuurFunctiegroep1(decoderAdres, blokNummer, 1, f1tot4);
                break;
            case >= 5 and <= 8:
                var f5tot8 = _laatsteF5TotF8.TryGetValue(decoderAdres, out var bestaand2) ? bestaand2 : new bool[4];
                f5tot8[functieNummer - 5] = aan;
                _laatsteF5TotF8[decoderAdres] = f5tot8;
                VerstuurFunctiegroep1(decoderAdres, blokNummer, 2, f5tot8);
                break;
            case >= 9 and <= 12:
                var f9tot12 = _laatsteF9TotF12.TryGetValue(decoderAdres, out var bestaand3) ? bestaand3 : new bool[4];
                f9tot12[functieNummer - 9] = aan;
                _laatsteF9TotF12[decoderAdres] = f9tot12;
                VerstuurFunctiegroep1(decoderAdres, blokNummer, 3, f9tot12);
                break;
            case >= 13 and <= 20:
                var f13tot20 = _laatsteF13TotF20.TryGetValue(decoderAdres, out var bestaand4) ? bestaand4 : new bool[8];
                f13tot20[functieNummer - 13] = aan;
                _laatsteF13TotF20[decoderAdres] = f13tot20;
                VerstuurAdvancedFunctiegroep(decoderAdres, blokNummer, 13, f13tot20);
                break;
            case >= 21 and <= 28:
                var f21tot28 = _laatsteF21TotF28.TryGetValue(decoderAdres, out var bestaand5) ? bestaand5 : new bool[8];
                f21tot28[functieNummer - 21] = aan;
                _laatsteF21TotF28[decoderAdres] = f21tot28;
                VerstuurAdvancedFunctiegroep(decoderAdres, blokNummer, 21, f21tot28);
                break;
            default:
                StatusBericht?.Invoke($"F{functieNummer} valt buiten het door Dinamo ondersteunde bereik (F0 t/m F28).");
                break;
        }
    }

    /// <summary>BUG #30 (gebruikerswaarneming: "de reservering stond naar blok 7, ik zag dat
    /// de loc de verkeerde kant op reed en drukte op rijrichting keren maar er gebeurde
    /// niets, pas toen hij in blok 4 kwam keerde hij ineens"): bytes-analyse van de
    /// hardware-log liet zien dat de OUDE, foutgerichte snelheidsramp gewoon 10+ seconden
    /// bleef doorlopen EN -oplopen (stap 1 t/m 9, richting "achteruit"), dwars door zes
    /// achtereenvolgende, in de app-log keurig bevestigde "handmatig omgekeerd"-correcties
    /// heen, terwijl de bijbehorende correctie-pakketten (snelheid herhaaldelijk op 0,
    /// richting "vooruit") er één voor één, met seconden ertussen, pas veel later uit
    /// kwamen. Oorzaak: VerstuurDatagram hieronder slaat voor eenzelfde (decoderAdres,
    /// blokNummer)-combinatie BEWUST alleen een BYTE-IDENTIEKE herhaling over (zie die
    /// toelichting) - een inhoudelijk ANDER commando (nieuwe snelheid/richting) wordt nooit
    /// verwijderd, want een eerdere, agressievere versie die dat WEL deed bleek een keer een
    /// echt vertrek- of stopcommando te hebben weggegooid. Daardoor bleven de AL in de
    /// wachtrij staande, inmiddels achterhaalde achteruit-stappen van de ramp (die vóór de
    /// correctie al klaarstonden, en - zolang de ramp-timer nog niet gestopt was - ook
    /// erna nog een paar keer bijvulden) gewoon, in volgorde, vóór de nieuwe correctie staan
    /// - de 200ms-seriële-cyclus liet die stapel pas geleidelijk leeglopen, met de correctie
    /// zelf er telkens achteraan.
    /// ECHTE FIX, bewust SMALLER dan de eerder teruggedraaide aanpak hierboven (die gold
    /// voor ALLE snelheidscommando's, altijd): deze methode verwijdert, ALLEEN op het
    /// expliciete moment van een bewuste richtingscorrectie (TreinrouteWindow.
    /// KeerTreinIndienActief) of een "geen kandidaat, nu stoppen"-beslissing, gericht alle
    /// nog niet verstuurde, wachtende snelheidscommando's voor DEZE decoder, over ALLE
    /// blokken - vlak VOORDAT de nieuwe, gecorrigeerde burst zelf de wachtrij in gaat. Op
    /// dat moment is elke oudere, nog wachtende snelheidswaarde voor deze decoder per
    /// definitie achterhaald (de gebruiker/software heeft zojuist bewust een nieuw besluit
    /// genomen), dus dit raakt de eerder teruggedraaide "vertrek-dan-stop"-regressie niet:
    /// die ging over twee ONAFHANKELIJK, kort na elkaar genomen besluiten tijdens NORMAAL
    /// rijden, dit gaat over expliciet weggooien van alles wat vóór EEN SPECIFIEK,
    /// aanwijsbaar correctiemoment al verouderd was.</summary>
    public void VerwijderWachtendeSnelheidscommandosVoor(int decoderAdres)
    {
        lock (_vergrendeling)
        {
            _teVersturen.RemoveAll(item => item.SnelheidsSleutel?.DecoderAdres == decoderAdres);
        }
    }

    /// <summary>Bouwt een compleet, correct geframed datagram (header + data + checksum,
    /// zie klasse-commentaar) en zet het in de verstuur-wachtrij; de stuur-timer haalt
    /// het er op tijd weer uit. Rechtstreeks versturen zou de 200ms-cadans van de PC-als-
    /// Master kunnen verstoren.</summary>
    private void VerstuurDatagram(byte[] payload, bool prioriteit = false, (int DecoderAdres, int BlokNummer)? snelheidsSleutel = null)
    {
        if (payload.Length > 7)
            throw new ArgumentException("Normaal Dinamo-datagram ondersteunt max. 7 databytes (jumbo-datagrammen zijn hier niet geïmplementeerd).");
        lock (_vergrendeling)
        {
            if (prioriteit)
            {
                _teVersturenPrioriteit.Enqueue(payload);
                return;
            }
            // Zie _teVersturen hierboven: alleen een BYTE-IDENTIEKE, nog niet verstuurde
            // herhaling voor dezelfde (decoderAdres, blokNummer)-combinatie wordt
            // overgeslagen (dat is per definitie geen nieuwe informatie - precies het
            // geval bij de herhaalde stop-broadcast). Een inhoudelijk ANDER commando voor
            // dezelfde combinatie (andere snelheid en/of richting) wordt NOOIT verwijderd
            // of overgeslagen - dat zou een echt vertrek- of stopcommando kunnen wissen dat
            // net iets eerder al klaarstond. Zo'n commando sluit gewoon, in volgorde,
            // achteraan aan.
            //
            // BUG #31 (gebruikerswaarneming: "alles is enorm traag, de loc ging pas zeer
            // laat rijden nadat ik op Go had gedrukt", gevonden tijdens een sessie met een
            // langere, aanhoudende Dinamo-foutstatus): de dedup hierboven gold voorheen
            // UITSLUITEND voor commando's MET een snelheidsSleutel. StuurResetFault (geen
            // sleutel, geen prioriteit) wordt echter door Poort_DataReceived opnieuw
            // aangeroepen op ELKE ontvangstcyclus zolang Dinamo's eigen F-bit=1 blijft
            // staan (bewust zo ontworpen, zie StuurResetFault - dat moet ZO blijven voor
            // een trage/verloren eerste reset). Zonder sleutel werd echter NOOIT gecontroleerd
            // of er al een identieke, nog niet verstuurde Reset Fault in de wachtrij stond -
            // dus bij een aanhoudende fout (in dit log ruim 90 seconden) stapelden zich
            // tientallen BYTE-IDENTIEKE Reset Fault-pakketten op in _teVersturen, allemaal
            // vóór de al wachtende snelheids-/wisselcommando's, die daardoor alsnog, één
            // voor één via de 200ms-cyclus, achter die hele stapel moesten aansluiten.
            // Fix: de byte-identieke-dedup geldt nu ook zonder sleutel (null == null is een
            // geldige, bedoelde match) - er staat zo nooit meer dan ÉÉN ongeverzonden Reset
            // Fault (of, voor VraagMelderStatusOp, twee aanvragen voor PRECIES dezelfde
            // melder) in de wachtrij, zonder de herhaal-semantiek zelf aan te tasten: zodra
            // die ene verstuurd is, mag en zal een volgende cyclus er gewoon weer één
            // toevoegen als de fout nog steeds aanhoudt.
            if (_teVersturen.Any(item => item.SnelheidsSleutel == snelheidsSleutel && item.Payload.AsSpan().SequenceEqual(payload)))
                return; // exact dezelfde, nog niet verstuurde herhaling - niets nieuws te melden
            _teVersturen.Add((payload, snelheidsSleutel));
        }
    }

    /// <summary>GEVONDEN, VIERDE LAAG VAN HETZELFDE KRUISWISSEL-PROBLEEM (gebruikersmelding,
    /// NA de prioriteits-wachtrij-fix hierboven en NA AantalHerhalingenWisselcommando=3:
    /// "Beide wisseltongen staan verkeerd" - fysiek bevestigd dat BEIDE motoren van de
    /// Engelse wissel de puls niet volledig afmaken, TERWIJL bytes-analyse bevestigde dat
    /// de 3 herhalingen voor beide adressen wel degelijk, en ruim op tijd, de deur uit
    /// gingen): de 3 herhalingen van ZetWissel/ZetSein werden voorheen hier ÉÉN voor ÉÉN
    /// verwerkt - dus verspreid over 3 aparte ticks van de stuur-timer (elke 200ms), een
    /// totale spreiding van ~600ms met grote GATEN ertussen. Koploper's eigen, bewezen
    /// werkende methode stuurt zijn 10 herhalingen juist zeer DICHT op elkaar (iedere
    /// ~25ms) - praktisch een ononderbroken stroom identieke commando's, geen 3 incidentele
    /// "tikken" met rust ertussen. Als Dinamo (of de aangesloten spoelaandrijving) de spoel
    /// alleen bekrachtigd houdt zolang er vrijwel ononderbroken nieuwe commando's binnenkomen
    /// - en dus tussen twee met 200ms tussenruimte verstuurde, identieke commando's weer kan
    /// "ontspannen" - dan leverden onze 3 verspreide tikken voor een zware, twee-motorige
    /// kruiswissel mogelijk nooit een aaneengesloten puls lang genoeg om 'm volledig te laten
    /// doorslaan, ook al was de TOTALE opgetelde tijd (600ms) op papier langer dan Koploper's
    /// 250ms. Fix: zodra de stuur-timer vuurt en er prioriteits-commando's klaarstaan, nu de
    /// HELE prioriteits-wachtrij in één keer leegtrekken en ACHTER ELKAAR (zonder 200ms-pauze
    /// ertussen, enkel beperkt door de seriële baudrate - een paar milliseconden per
    /// datagram) versturen, in plaats van steeds maar 1 per tick. Dat brengt de dichtheid
    /// van onze herhalingen voor hetzelfde wisselcommando veel dichter bij Koploper's eigen,
    /// fysiek bewezen werkende patroon, zonder de 200ms-cyclus voor al het overige
    /// Dinamo-verkeer (snelheid/melder-polls/keepalive) aan te tasten - die loopt voor de
    /// rest gewoon door zoals voorheen.</summary>
    private void VerstuurVolgendeInWachtrij()
    {
        if (_poort is null || !Verbonden) return;

        List<byte[]> teVersturenPakketten;
        lock (_vergrendeling)
        {
            if (_teVersturenPrioriteit.Count > 0)
            {
                // Alle klaarstaande prioriteits-commando's (de 3 herhalingen van dezelfde
                // ZetWissel/ZetSein-aanroep) in één keer, achter elkaar - zie toelichting
                // hierboven.
                teVersturenPakketten = new List<byte[]>(_teVersturenPrioriteit);
                _teVersturenPrioriteit.Clear();
            }
            else if (_teVersturen.Count > 0)
            {
                teVersturenPakketten = new List<byte[]> { _teVersturen[0].Payload };
                _teVersturen.RemoveAt(0);
            }
            else
                teVersturenPakketten = new List<byte[]> { Array.Empty<byte>() }; // leeg = NULL-datagram (keepalive)
        }

        foreach (var payload in teVersturenPakketten)
            VerstuurEnkelPakket(payload);
    }

    private void VerstuurEnkelPakket(byte[] payload)
    {
        try
        {
            _toggleBit = !_toggleBit;
            bool faultBit = _faultBitResterendeCycli > 0;
            if (faultBit) _faultBitResterendeCycli--;
            byte header = (byte)(((_toggleBit ? 1 : 0) << 6) | ((faultBit ? 1 : 0) << 5) | (1 << 3) | (payload.Length & 0x07)); // bit7=0 (header), bit6=Toggle, bit5=Fault (noodstop), bit3=1 (protocol 3.x normaal)
            var pakket = new byte[1 + payload.Length + 1];
            pakket[0] = header;
            int som = header & 0x7F;
            for (int i = 0; i < payload.Length; i++)
            {
                byte dataByte = (byte)(0x80 | (payload[i] & 0x7F));
                pakket[1 + i] = dataByte;
                som += dataByte & 0x7F;
            }
            byte checksum = (byte)(0x80 | ((128 - (som % 128)) % 128));
            pakket[^1] = checksum;
            _poort!.Write(pakket, 0, pakket.Length);
            HardwareCommunicatieLog.Log("Uit", $"[Dinamo] {Convert.ToHexString(pakket)}{(payload.Length == 0 ? " (keepalive)" : "")}", keepalive: payload.Length == 0);
        }
        catch (Exception ex)
        {
            StatusBericht?.Invoke($"Fout bij versturen naar Dinamo: {ex.Message}");
        }
    }

    /// <summary>Zoekt in de ontvangen bytes naar een Switch-event: "(10CSSSS) (sssssss)"
    /// (§3.5) - C=1 geactiveerd/bezet, C=0 gedeactiveerd/vrij, SSSSsssssss = 11-bits
    /// schakelaar/bezetmelder-adres (0..2047). Werkt op basis van het bit7=0-patroon van
    /// een header-byte om de start van een datagram te herkennen; geen volledige
    /// framing-validatie/hersynchronisatie zoals de spec voor productiekwaliteit zou
    /// vereisen (zie klasse-commentaar over de vereenvoudiging).
    ///
    /// GEVONDEN EN GEFIXTE BUG (gebruikersmelding: "hij luistert niet naar bezetmelders,
    /// en die zie ik ook niet terug in de log"): het patroon "10CSSSS" is een 7-BITS
    /// veld (bit6=1,bit5=0,bit4=C,bit3-0=SSSS) - data1 is hier al eerder gemaskeerd met
    /// "&amp; 0x7F" (bit7 dus altijd 0). De oude check "(data1 &amp; 0b1100_0000) ==
    /// 0b1000_0000" test echter bit7+bit6 tegen een patroon met bit7=1 - dat kan NOOIT
    /// waar zijn na die maskering, dus een Switch-event werd altijd genegeerd, ongeacht
    /// wat Dinamo daadwerkelijk stuurde (bevestigd: de eerste 2 aangeleverde logs bevatten
    /// destijds toevallig nog geen enkel echt event om te parsen - pas een latere,
    /// 3e log met een echte bezetmelding bevestigde de fix daadwerkelijk werkt). Bijkomend
    /// gevonden: de C-bit (bezet/vrij) stond op de verkeerde positie (bit5 i.p.v. bit4).
    /// Beide gefixt: check nu bit6/bit5 (0x60/0x40), C op bit4 (0x10).
    ///
    /// TWEEDE GEVONDEN PROBLEEM (zelfde 3e log): Dinamo's EIGEN F-bit (bit5 van de
    /// HEADER-byte, dus van buffer[i] zelf, niet van data1) stond in ALLE 3 logs vanaf
    /// het allereerste ontvangen datagram op 1 - zie StuurResetFault hierboven voor de
    /// volledige toelichting. Hier wordt dat nu herkend en, zolang het aanhoudt,
    /// automatisch met een Reset Fault beantwoord.
    ///
    /// DERDE UITBREIDING: naast het spontane "10CSSSS"-event herkent dit nu OOK het
    /// "11CSSSS"-patroon (bit6=1,bit5=1) - het ANTWOORD op VraagMelderStatusOp hierboven
    /// (§3.5, Switch Request/Response). Zelfde adres/C-bit-opbouw, dus verder identiek
    /// verwerkt. Nodig voor het geval een loc al PRECIES op de verwachte melder staat
    /// vóórdat een rit start: Dinamo meldt dan nooit spontaan een "wordt bezet"-overgang
    /// (die stand was immers al zo), dus zonder actief opvragen zou de software voor
    /// altijd op een overgang wachten die nooit komt.</summary>
    // GEVONDEN, KRITIEKE BUG (gebruikerscorrectie: "deze baan rijdt al jaren perfect met
    // Koploper" - terecht, dus de oorzaak van een incidenteel gemiste bezetmelding (bijv.
    // melder 15, eerder ook melder 8) kan niet in de bekabeling/sensoren zitten, en bleek
    // uiteindelijk hier): Poort_DataReceived las voorheen bij ELKE aanroep een VERS,
    // LEEG buffertje (`new byte[128]`) en verwerkte UITSLUITEND wat in DIE ENE .Read()-
    // aanroep binnenkwam - zonder enige herinnering aan de vorige aanroep. .NET se
    // SerialPort.DataReceived-event garandeert echter GEEN volledige datagrammen per
    // aanroep: het event kan al vuren zodra er pas 1 of enkele bytes binnen zijn, met de
    // rest van hetzelfde fysieke bericht (bijv. de twee databytes + checksum van een
    // Switch-event) pas in een VOLGENDE aanroep. Kwam een bericht zo toevallig middenin
    // doorgeknipt te worden, dan zag de vorige code in de EERSTE aanroep een header-byte
    // zonder genoeg volgende bytes (`i + aantalDataBytes >= gelezen`) en sloeg 'm domweg
    // over - voorgoed, want de TWEEDE aanroep begon weer met een leeg buffertje en had geen
    // flauw benul dat er net een onvolledig bericht aan vooraf was gegaan. Zo verdween een
    // bezetmelding spoorloos, puur afhankelijk van toevallige USB/seriële-poort-timing -
    // precies het "soms wel, soms niet"-patroon dat op verschillende testritten bij
    // verschillende meldernummers opdook. Dit is nu opgelost met een PERSISTENTE buffer
    // (_ontvangstBuffer) die over meerdere aanroepen heen bewaard blijft: een onvolledig
    // bericht aan het einde wordt simpelweg laten staan tot de ontbrekende bytes er ook
    // zijn, in plaats van verloren te gaan. Logging en verwerking gebeuren nu in één pas
    // (voorheen twee aparte scans, met het risico dat ze ooit uit de pas zouden lopen).</summary>
    private readonly List<byte> _ontvangstBuffer = new();

    /// <summary>GEVONDEN, KRITIEKE REGRESSIE (eigen periodieke-herbevestiging-timer,
    /// _melderHerbevestigingsTimer in MainWindow - zie VraagAlleMelderStatusOp): een
    /// Switch Request/Response-antwoord op een periodieke herbevestiging komt voor ELKE
    /// bekende melder binnen, OOK als de status sinds de vorige keer helemaal niet
    /// veranderd is - Dinamo herhaalt dan gewoon "nog steeds bezet"/"nog steeds vrij".
    /// BezetmeldingGewijzigd werd echter bij ELK binnengekomen Switch-event/antwoord
    /// botweg opnieuw afgevuurd, ongeacht of de waarde daadwerkelijk wijzigde - in
    /// strijd met wat de eigen naam van het event ("Gewijzigd") belooft, en precies de
    /// oorzaak van de NOODSTOP uit het log van 2026-10-02 08:12: blok 4 (het blok dat
    /// net verlaten was) bleef nog even legitiem bezet (staart van de trein), de
    /// periodieke herbevestiging bevestigde die ONGEWIJZIGDE bezet-status 4 seconden
    /// later gewoon opnieuw, en MainWindow.Bezetmelding_VanHardware zag dat aan voor een
    /// GLASHELDERE NIEUWE bezetmelding - precies het signaal waarmee de
    /// rijrichting-correctie ("blok dat net verlaten is meldde zich weer bezet, dus
    /// decoder rijdt averechts") een heus achteruit-rijdende trein herkent. Resultaat:
    /// een volkomen normale voorwaartse rit werd automatisch gekeerd, wat op zijn beurt
    /// de spookmelding-NOODSTOP triggerde.
    ///
    /// Fix: per meldernummer de laatst doorgegeven status onthouden en
    /// BezetmeldingGewijzigd alleen nog vuren bij een ECHTE wijziging (of de EERSTE keer
    /// dat een meldernummer uberhaupt gezien wordt - nodig voor het geval in de
    /// hierboven staande "DERDE UITBREIDING"-toelichting, een loc die al vóór de start
    /// van een rit op de verwachte melder staat). Een periodieke herbevestiging die
    /// slechts een ongewijzigde status herhaalt, bereikt de rest van het programma nu
    /// simpelweg niet meer - precies zoals een event met de naam "Gewijzigd" hoort te
    /// werken.</summary>
    private readonly Dictionary<int, bool> _laatstDoorgegevenStatus = new();

    private void Poort_DataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        try
        {
            var tijdelijk = new byte[128];
            int gelezen = _poort!.Read(tijdelijk, 0, tijdelijk.Length);
            for (int t = 0; t < gelezen; t++) _ontvangstBuffer.Add(tijdelijk[t]);

            bool dinamoMeldtFout = false;
            int i = 0;
            while (i < _ontvangstBuffer.Count)
            {
                if ((_ontvangstBuffer[i] & 0x80) != 0) { i++; continue; } // niet een header-byte, sla over

                int aantalDataBytes = _ontvangstBuffer[i] & 0x07;
                int berichtLengte = 1 + aantalDataBytes + 1; // header + databytes + checksum
                if (i + berichtLengte > _ontvangstBuffer.Count)
                {
                    // Bericht nog niet compleet - wacht op de rest bij een volgende
                    // DataReceived-aanroep. NIET verder scannen: alles vanaf hier blijft
                    // in de buffer staan zodat het straks als geheel verwerkt wordt.
                    break;
                }

                bool ditIsKeepalive = aantalDataBytes == 0;
                HardwareCommunicatieLog.Log("In", $"[Dinamo] {Convert.ToHexString(_ontvangstBuffer.GetRange(i, berichtLengte).ToArray())}", keepalive: ditIsKeepalive);

                if ((_ontvangstBuffer[i] & 0x20) != 0) dinamoMeldtFout = true; // Dinamo's eigen F-bit

                if (aantalDataBytes >= 2)
                {
                    byte data1 = (byte)(_ontvangstBuffer[i + 1] & 0x7F);
                    byte data2 = (byte)(_ontvangstBuffer[i + 2] & 0x7F);
                    if ((data1 & 0x7C) == 0x30)
                    {
                        // BUG #43: Block Alarm - zie BlokAlarmGewijzigd. Alleen doorgeven bij een
                        // echte wijziging; een "geen kortsluiting" voor een blok dat nooit in
                        // kortsluiting stond is geen nieuws.
                        int alarmBlok = (((data1 & 0x01) << 7) | data2) + 1; // 0-based -> 1-based
                        bool alarmKortsluiting = (data1 & 0x02) != 0;
                        _laatsteBlokAlarm.TryGetValue(alarmBlok, out bool alarmVorige);
                        if (alarmVorige != alarmKortsluiting)
                        {
                            _laatsteBlokAlarm[alarmBlok] = alarmKortsluiting;
                            StatusBericht?.Invoke(alarmKortsluiting
                                ? $"Dinamo meldt KORTSLUITING in blok {alarmBlok}."
                                : $"Dinamo: kortsluiting in blok {alarmBlok} is opgeheven.");
                            BlokAlarmGewijzigd?.Invoke(alarmBlok, alarmKortsluiting);
                        }
                    }
                    else
                    // patroon "10CSSSS" (spontaan event, bit6=1,bit5=0) OF "11CSSSS"
                    // (antwoord op een statusaanvraag, bit6=1,bit5=1) - beide zijn qua
                    // adres/C-bit-opbouw identiek, dus hier gewoon één gezamenlijke check
                    // (bit6 moet 1 zijn, bit5 maakt niet uit welke van de twee patronen het is).
                    if ((data1 & 0x40) == 0x40)
                    {
                        bool bezet = (data1 & 0x10) != 0; // C = bit4
                        int adres = (((data1 & 0x0F) << 7) | data2) + 1; // 0-based -> 1-based
                        // Zie _laatstDoorgegevenStatus hierboven: alleen doorgeven aan de
                        // rest van het programma als dit een ECHTE wijziging is (of de
                        // eerste keer dat dit adres gezien wordt) - een periodieke
                        // herbevestiging die slechts "nog steeds dezelfde status" meldt
                        // mag hier NOOIT als een nieuwe gebeurtenis doorkomen.
                        if (!_laatstDoorgegevenStatus.TryGetValue(adres, out bool vorigeStatus) || vorigeStatus != bezet)
                        {
                            _laatstDoorgegevenStatus[adres] = bezet;
                            BezetmeldingGewijzigd?.Invoke(adres, bezet);
                        }
                    }
                }

                i += berichtLengte;
            }
            // Alles tot en met positie i is ofwel een volledig verwerkt bericht, ofwel een
            // definitief overgeslagen niet-header-byte - dat mag nu veilig weg. Een eventueel
            // onvolledig bericht vanaf i blijft staan voor de volgende aanroep.
            if (i > 0) _ontvangstBuffer.RemoveRange(0, i);
            // Veiligheidsklep: een structureel kapot/nooit-kloppend fragment (bijv. ruis bij
            // het openen van de poort) mag de buffer niet voor altijd laten doorgroeien.
            if (_ontvangstBuffer.Count > 256) _ontvangstBuffer.Clear();

            // BUG #29 (gebruikersmelding: "Fout bij lezen van Dinamo: The calling thread
            // cannot access this object because a different thread owns it" bleef, na ÉÉN
            // enkele echte F-bit-overgang, honderden keren per minuut herhalen): deze hele
            // methode loopt op de achtergrondthread van SerialPort.DataReceived. Als een
            // abonnee van KortsluitingStatusGewijzigd (zie BUG #28 in HardwareBeheerder/
            // MainWindow) een exception gooit - bijv. omdat hij UI-gebonden state aanraakt
            // zonder naar de UI-thread te marshallen - werd die exception hiervoor pas
            // helemaal onderaan Poort_DataReceived opgevangen, NA deze methode maar VOOR
            // de regel die _dinamoMeldeFoutVorigeKeer bijwerkte. Resultaat: die vlag bleef
            // voor altijd op "true" staan, dus elke VOLGENDE aanroep (er komt voortdurend
            // serieel verkeer binnen) zag opnieuw "was fout, nu niet meer", loggede opnieuw
            // "opgeheven", riep het event opnieuw aan, kreeg opnieuw dezelfde exception - een
            // oneindige lus van exact datzelfde berichtenpaar. Nu wordt de vlag ALTIJD eerst
            // bijgewerkt, vóórdat het event naar buiten gaat - een crashende abonnee kan deze
            // eigen boekhouding dus nooit meer corrumperen, ongeacht wat die abonnee fout
            // doet.
            bool wasFout = _dinamoMeldeFoutVorigeKeer;
            _dinamoMeldeFoutVorigeKeer = dinamoMeldtFout;
            if (dinamoMeldtFout)
            {
                if (!wasFout)
                {
                    // GEBRUIKERSCORRECTIE ("koploper geeft alleen een melding, laat alle
                    // treinen gewoon rijden ook in het blok met de kortsluiting, Dinamo
                    // blijft gewoon rijspanning geven"): eerdere tekst hier beweerde ten
                    // onrechte dat dit "alle voertuigen" stilzet - dat was een te brede
                    // lezing van §2.2 (die specifiek beschrijft wat er gebeurt als de PC
                    // ZELF het F-bit zet via Noodstop() hieronder, niet wat Dinamo's EIGEN,
                    // door Dinamo gedetecteerde foutmelding betekent). Onze software stuurt
                    // hier ook geen enkel stop-commando naartoe - dit is puur informatief.
                    StatusBericht?.Invoke("Dinamo meldt zelf een foutstatus (F-bit, bijv. kortsluiting op het spoor) - stuur automatisch een Reset Fault-commando. Dit is puur informatief: Dinamo blijft gewoon rijspanning geven, treinen rijden door.");
                    KortsluitingStatusGewijzigd?.Invoke(true);
                }
                StuurResetFault();
            }
            else if (wasFout)
            {
                StatusBericht?.Invoke("Dinamo's foutstatus is opgeheven.");
                KortsluitingStatusGewijzigd?.Invoke(false);
            }
        }
        catch (TimeoutException) { /* niets gelezen binnen de timeout, prima */ }
        catch (Exception ex)
        {
            StatusBericht?.Invoke($"Fout bij lezen van Dinamo: {ex.Message}");
        }
    }
}
