using System.IO.Ports;
using System.Text;

namespace Modeltreinbesturing.Hardware;

/// <summary>
/// DCC-EX (dcc-ex.com), een populaire open-source/DIY DCC-centrale (Arduino/ESP32-
/// gebaseerd). In tegenstelling tot Dinamo en LocoNet gebruikt DCC-EX een simpel,
/// LEESBAAR tekst-protocol via de seriële poort: elk commando/antwoord is een regel
/// tussen &lt; en &gt;, bijvoorbeeld "&lt;a 25 0 1&gt;" of "&lt;Q 3&gt;". Dat maakt deze
/// koppeling een stuk eenvoudiger dan de binaire Dinamo/LocoNet-protocollen hierboven.
///
/// Gebruikte commando's (zie de officiële DCC-EX Serial Command Reference):
/// - Wissel/sein (accessoire-adres): "&lt;a ADRES SUBADRES STAND&gt;" - stuurt een rauw
///   NMRA DCC-accessoirepakket. Subadres altijd 0 gehouden (wij hebben geen apart
///   subadres-veld op Wissel/Sein), STAND 0=dicht/veilig, 1=open/afbuigend/onveilig.
/// - Loc-snelheid: "&lt;t REGISTER CAB SPEED DIRECTION&gt;" - het standaard throttle-
///   commando, rechtstreeks stateless (geen slot-aanvraag vooraf nodig, in tegenstelling
///   tot LocoNet).
/// - Noodstop: "&lt;!&gt;" - stopt onmiddellijk alle locomotieven.
/// - Bezetmelding: DCC-EX stuurt zelf "&lt;Q ID&gt;" (actief/bezet) en "&lt;q ID&gt;"
///   (inactief/vrij) zodra een sensor van staat wisselt - de sensoren zelf worden op de
///   DCC-EX-centrale gedefinieerd (eigen configuratie/myAutomation.h), niet vanuit deze
///   applicatie; wij luisteren hier alleen naar de doorgestuurde meldingen.
/// Standaard seriële snelheid: 115200 baud (de gangbare DCC-EX-standaard via USB).
/// </summary>
public class DccExHardware : IHardwareInterface
{
    private SerialPort? _poort;
    private readonly StringBuilder _ontvangstBuffer = new();

    public bool Verbonden { get; private set; }
    public string Naam => "DCC-EX";

    public event Action<int, bool>? BezetmeldingGewijzigd;
    public event Action<string>? StatusBericht;
    public event Action<bool>? KortsluitingStatusGewijzigd; // nooit gevuurd - alleen Dinamo kan dit daadwerkelijk detecteren

    public Task VerbindenAsync(string comPoort)
    {
        return Task.Run(() =>
        {
            try
            {
                // Zie de uitgebreide toelichting in DinamoHardware - een oude, nog niet
                // netjes afgesloten verbinding houdt de poort anders op OS-niveau vast.
                Ontkoppelen();

                _poort = new SerialPort(comPoort, 115200, Parity.None, 8, StopBits.One)
                {
                    ReadTimeout = 500,
                    WriteTimeout = 500,
                    NewLine = ">"
                };
                _poort.DataReceived += Poort_DataReceived;
                _poort.Open();
                Verbonden = true;
                StatusBericht?.Invoke($"Verbonden met DCC-EX op {comPoort} (115200 baud).");
            }
            catch (Exception ex)
            {
                Verbonden = false;
                throw new Exception($"Kon niet verbinden met DCC-EX op {comPoort}: {ex.Message}");
            }
        });
    }

    public void Ontkoppelen()
    {
        if (_poort is null) return; // nog nooit verbonden geweest - niets te ontkoppelen, geen misleidende melding nodig
        try { _poort.Close(); }
        catch { /* niets te doen, we sluiten toch af */ }
        _poort = null;
        Verbonden = false;
        StatusBericht?.Invoke("DCC-EX ontkoppeld.");
    }

    /// <summary>"&lt;a ADRES SUBADRES STAND&gt;" - rauw NMRA DCC-accessoirepakket, precies
    /// zoals de DCC-EX Serial Command Reference dit documenteert voor ad-hoc
    /// wissel-/accessoiresturing zonder vooraf een "Turnout"-object te hoeven definiëren
    /// op de centrale zelf.</summary>
    public void ZetWissel(int adres, bool afbuigend) => VerstuurCommando($"<a {adres} 0 {(afbuigend ? 1 : 0)}>");

    /// <summary>Zelfde onderliggende commando als ZetWissel - een sein-adres wordt hier,
    /// net als bij Dinamo/Intellibox, als een accessoire-achtig adres behandeld (een
    /// aanname, geen universele standaard - decoderafhankelijk).</summary>
    public void ZetSein(int adres, bool onveiligRood) => VerstuurCommando($"<a {adres} 0 {(onveiligRood ? 1 : 0)}>");

    /// <summary>"&lt;!&gt;" is DCC-EX's eigen commando voor een noodstop van ALLE
    /// locomotieven tegelijk.</summary>
    public void Noodstop() => VerstuurCommando("<!>");

    /// <summary>"&lt;t REGISTER CAB SPEED DIRECTION&gt;" - DCC-EX's standaard throttle-
    /// commando (Serial Command Reference): REGISTER is een intern registernummer (1
    /// volstaat, DCC-EX beheert dit zelf per CAB-adres), CAB is het DCC-adres van de
    /// locomotief, SPEED 0-126 (0=stilstand), DIRECTION 1=vooruit/0=achteruit. In
    /// tegenstelling tot LocoNet vereist dit GEEN aparte slot-aanvraag vooraf - een
    /// rechtstreeks, stateless commando, precies zoals ZetWissel/ZetSein hierboven.</summary>
    /// <remarks><paramref name="blokNummer"/> wordt genegeerd: DCC-EX adresseert de
    /// decoder rechtstreeks, in tegenstelling tot Dinamo (zie IHardwareInterface).
    /// <paramref name="stappen"/> wordt ook genegeerd - DCC-EX se "&lt;t&gt;"-commando
    /// accepteert altijd 0-126 rechtstreeks, ongeacht de decoder-configuratie.</remarks>
    public void ZetLocSnelheid(int decoderAdres, int stap, bool vooruit, int blokNummer = 0, int stappen = 126) =>
        VerstuurCommando($"<t 1 {decoderAdres} {Math.Clamp(stap, 0, 126)} {(vooruit ? 1 : 0)}>");

    /// <summary>"&lt;F cab funct state&gt;" - DCC-EX's officiële, directe functiecommando
    /// (Technical Reference for Throttle Developers / Command Reference), bijv. "&lt;F 3 0
    /// 1&gt;" zet het licht (F0) van loc 3 aan. Net zo simpel/stateless als ZetLocSnelheid
    /// hierboven - geen slot-aanvraag of bitmasker-opbouw nodig, in tegenstelling tot
    /// LocoNet hieronder.</summary>
    /// <remarks><paramref name="blokNummer"/> wordt genegeerd, zelfde reden als hierboven.</remarks>
    public void ZetFunctie(int decoderAdres, int functieNummer, bool aan, int blokNummer = 0) =>
        VerstuurCommando($"<F {decoderAdres} {functieNummer} {(aan ? 1 : 0)}>");

    private void VerstuurCommando(string commando)
    {
        if (_poort is null || !Verbonden) return;
        try
        {
            _poort.Write(commando);
            HardwareCommunicatieLog.Log("Uit", $"[DCC-EX] {commando}");
        }
        catch (Exception ex)
        {
            StatusBericht?.Invoke($"Kon commando '{commando}' niet versturen naar DCC-EX: {ex.Message}");
        }
    }

    /// <summary>Verzamelt binnenkomende bytes tot een complete "&lt;...&gt;"-regel en
    /// verwerkt die dan - de seriële poort kan een bericht in meerdere stukjes afleveren,
    /// dus we bufferen tot we een sluitende '&gt;' zien.</summary>
    private void Poort_DataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        if (_poort is null) return;
        string binnen;
        try { binnen = _poort.ReadExisting(); }
        catch { return; }

        _ontvangstBuffer.Append(binnen);
        string buffer = _ontvangstBuffer.ToString();

        int start;
        while ((start = buffer.IndexOf('<')) >= 0)
        {
            int einde = buffer.IndexOf('>', start);
            if (einde < 0) break; // bericht nog niet compleet, wachten op de rest
            string bericht = buffer.Substring(start + 1, einde - start - 1);
            VerwerkBericht(bericht);
            buffer = buffer[(einde + 1)..];
        }
        _ontvangstBuffer.Clear();
        _ontvangstBuffer.Append(buffer);
    }

    /// <summary>"Q ID" = sensor ID is actief (bezet), "q ID" = sensor ID is inactief
    /// (vrij) - de twee enige sensor-berichten die DCC-EX ongevraagd verstuurt.</summary>
    private void VerwerkBericht(string bericht)
    {
        HardwareCommunicatieLog.Log("In", $"[DCC-EX] <{bericht}>");
        var delen = bericht.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (delen.Length < 2) return;

        if (delen[0] == "Q" && int.TryParse(delen[1], out int bezetId))
            BezetmeldingGewijzigd?.Invoke(bezetId, true);
        else if (delen[0] == "q" && int.TryParse(delen[1], out int vrijId))
            BezetmeldingGewijzigd?.Invoke(vrijId, false);
    }
}
