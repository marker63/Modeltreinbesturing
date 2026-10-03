namespace Modeltreinbesturing.Hardware;

/// <summary>
/// Abstractie voor een fysieke hardware-koppeling (Dinamo, Intellibox, of geen - puur
/// simulatie). De rest van Modeltreinbesturing (BaanontwerpWindow/TreinrouteWindow) praat
/// alleen tegen deze interface, nooit rechtstreeks tegen een SerialPort - zo kan de
/// simulator gewoon blijven werken zonder hardware, en kan er later een derde
/// interface bijkomen zonder de rest van de applicatie te hoeven aanpassen.
/// </summary>
public interface IHardwareInterface
{
    bool Verbonden { get; }
    string Naam { get; }

    /// <summary>Probeert verbinding te maken. Geeft bij falen een Exception met een
    /// begrijpelijke Nederlandse melding (poort bezet, geen respons, etc.).</summary>
    Task VerbindenAsync(string comPoort);

    void Ontkoppelen();

    /// <summary>Zet een wissel op afbuigend (true) of rechtdoor (false) via het adres
    /// zoals ingesteld in het baanontwerp (Wissel.Adres).</summary>
    void ZetWissel(int adres, bool afbuigend);

    /// <summary>Zet een sein-aspect via het adres (Sein.Adres). Simpel gehouden tot
    /// rood/niet-rood, want niet elke hardware-koppeling ondersteunt evenveel aspecten
    /// per adres op dezelfde manier.</summary>
    void ZetSein(int adres, bool onveiligRood);

    /// <summary>Handmatig een specifieke loc rechtstreeks aansturen (decoderadres,
    /// snelheidsstap 0=stilstand, en rijrichting) - los van de route-gebaseerde simulatie,
    /// voor als je een trein in een blok handmatig een stukje wilt verplaatsen (in beide
    /// rijrichtingen). Matcht Koploper's eigen "rijwindow" van een loc.
    /// <paramref name="blokNummer"/>: het blok (Blok.Nummer) waar deze loc op dit moment
    /// staat - 0 als onbekend/niet geplaatst. DCC-EX/LocoNet/Simulatie adresseren de
    /// decoder rechtstreeks en negeren dit; Dinamo's protocol stuurt DCC-commando's
    /// echter altijd VIA een blok (zie DinamoHardware) en heeft dit dus wél nodig.
    /// <paramref name="stappen"/>: het aantal snelheidsstappen waarin deze loc's decoder
    /// daadwerkelijk is geconfigureerd (Trein.DecoderStappen, standaard 28) - GEVONDEN VIA
    /// EEN ECHTE KOPLOPER-VERGELIJKING (Wireshark-opname naast onze eigen communicatielog):
    /// Dinamo kent zowel een 28-staps ALS een 126-staps snelheidscommando, en de decoder
    /// luistert alleen naar het commando dat overeenkomt met zijn EIGEN CV29-configuratie -
    /// het andere commando wordt simpelweg genegeerd, ook al is het qua Dinamo-protocol
    /// volledig correct. Andere interfaces negeren dit vooralsnog.</summary>
    void ZetLocSnelheid(int decoderAdres, int stap, bool vooruit, int blokNummer = 0, int stappen = 126);

    /// <summary>Een DCC-functie (F0 t/m F28, bijv. F0=licht, F1=geluid) van een specifieke
    /// loc aan/uit zetten - dit is het daadwerkelijke hardware-commando achter een
    /// loc-functie (Model.LocFunctie), los van het eventuele lokale .wav-geluidje dat de
    /// software zelf ook nog kan afspelen. Zonder dit commando zou een loc-functie alleen
    /// een geluidje op de PC laten horen, zonder de fysieke decoder ooit echt aan te
    /// sturen - dat was precies het gat dat hiermee gedicht wordt.
    /// <paramref name="blokNummer"/>: zie ZetLocSnelheid hierboven - zelfde reden.</summary>
    void ZetFunctie(int decoderAdres, int functieNummer, bool aan, int blokNummer = 0);

    /// <summary>GEBRUIKERSVERZOEK ("de wissels worden bij het opstarten niet sequentieel
    /// geïnitialiseerd") - de EIGENLIJKE oorzaak, dieper dan de eerder gerepareerde race
    /// condition in HardwareBeheerder: DIE wachtrij regelt alleen hoe snel commando's naar
    /// Huidige.ZetWissel worden AANGELEVERD, niet hoe snel ze DAADWERKELIJK over de kabel
    /// gaan - dat laatste bepaalt Dinamo's EIGEN, onderliggende verzendwachtrij
    /// (_teVersturen), die vast in Dinamo's eigen ~200ms-protocolcyclus afhandelt, met
    /// GEEN weet van WisselRustpauzeMilliseconden. Stond die rij bij het opstarten al vol
    /// (bijv. met de bezetmelder-statusopvraag), dan werden de wisselcommando's daar
    /// gewoon achteraan gezet en alsnog in Dinamo's eigen, snellere tempo afgevuurd zodra
    /// de rij aan hen toekwam - de 600ms-pauze had daar geen grip meer op. Deze property
    /// (standaard true - alleen Dinamo heeft zo'n eigen wachtrij) geeft HardwareBeheerder
    /// iets om op te wachten: pas het VOLGENDE commando aanleveren als DEZE rij leeg is,
    /// zodat de ingestelde pauze alsnog echt op de kabel terechtkomt.</summary>
    bool KlaarVoorVolgendeWisselCommando => true;

    /// <summary>BAANVERKENNER: true als ZetLocSnelheid/ZetFunctie een blokNummer nodig
    /// hebben om de loc te bereiken (Dinamo: DCC-commando's gaan altijd via een blok).
    /// DCC-EX/LocoNet/Simulatie adresseren de decoder rechtstreeks - standaard false.</summary>
    bool LocCommandoVereistBlok => false;

    /// <summary>BAANVERKENNER: true als deze koppeling de HUIDIGE stand van een melder
    /// actief kan opvragen (VraagMelderStatusOp hieronder). Standaard false.</summary>
    bool KanMelderStatusOpvragen => false;

    /// <summary>Vraagt de huidige stand van een melder op; het antwoord komt binnen via het
    /// gewone BezetmeldingGewijzigd-event. Standaard doet dit niets - DinamoHardware heeft
    /// zijn eigen, al bestaande publieke VraagMelderStatusOp die dit lid automatisch
    /// invult (gewone C#-regel: een publieke methode met dezelfde handtekening in de klasse
    /// gaat vóór deze standaardimplementatie).</summary>
    void VraagMelderStatusOp(int meldernummer) { }

    /// <summary>Meldt een gewijzigde bezetmelding: (meldernummer, bezet).</summary>
    event Action<int, bool>? BezetmeldingGewijzigd;

    /// <summary>Noodstop: stopt onmiddellijk ALLE aangesloten voertuigen, ongeacht welk
    /// blok/adres. Elke hardware-koppeling implementeert dit met zijn eigen ingebouwde
    /// noodstop-mechanisme (niet via losse ZetWissel/ZetSein-commando's per voertuig),
    /// zodat het net zo direct werkt als op de echte hardware zelf.</summary>
    void Noodstop();

    /// <summary>Vrije-tekst statusregels voor het logpaneel (verbonden/ontkoppeld/fouten).</summary>
    event Action<string>? StatusBericht;

    /// <summary>GEBRUIKERSVERZOEK ("Koploper kan een melding geven als hij kortsluiting
    /// ziet, kan je die ook toevoegen?"): losse, gestructureerde melding (true=actief,
    /// false=voorbij) voor Dinamo's eigen foutstatus (F-bit, §2.2 - dekt kortsluiting én
    /// andere fouten die Dinamo zelf detecteert). Los van StatusBericht hierboven (dat is
    /// vrije tekst voor de log) zodat de UI dit specifiek kan gebruiken om een duidelijk
    /// zichtbare melding te tonen/verbergen, i.p.v. dat de gebruiker de verborgen
    /// hardware-log moet openen om dit te zien. Optioneel om te implementeren (alleen
    /// DinamoHardware kan dit daadwerkelijk detecteren); andere koppelingen laten 'm
    /// gewoon nooit vuren.</summary>
    event Action<bool>? KortsluitingStatusGewijzigd;
}
