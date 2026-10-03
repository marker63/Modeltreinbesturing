using System.IO.Ports;

namespace Modeltreinbesturing.Hardware;

/// <summary>
/// Uhlenbrock Intellibox (I/II/Basic/IB-Com) via het ingebouwde LocoNet-protocol over
/// een seriële (RS232/USB-CDC) verbinding. LocoNet is een open, gedocumenteerd protocol
/// (niet Uhlenbrock-specifiek) - vandaar dat dit met vertrouwen geïmplementeerd is, in
/// tegenstelling tot Dinamo's eigen gesloten protocol.
///
/// Seriële instellingen volgens Uhlenbrock's eigen documentatie (Intellibox-I, Special
/// Option SO2=4 voor LocoNet-protocol, SO5=1 voor 1 stopbit): 19200 baud, 8 databits,
/// 1 stopbit, geen pariteit. Oudere Intellibox-firmware gebruikte 2400 baud als default -
/// controleer dit in de Special Options van je eigen Intellibox als verbinden niet lukt.
///
/// Alleen de basiscommando's zijn hier geïmplementeerd (wissel zetten via OPC_SW_REQ,
/// bezetmelding lezen via OPC_INPUT_REP, handmatige loc-snelheid via een slot-aanvraag +
/// OPC_LOCO_SPD/OPC_LOCO_DIRF) - dit is de kern van wat Koploper-achtige software nodig
/// heeft, maar NIET getest tegen echte hardware in deze sessie. Test dit zelf voorzichtig
/// (bijv. met één wissel) voordat je een hele baan erop aansluit.
/// </summary>
public class IntelliboxHardware : IHardwareInterface
{
    private SerialPort? _poort;

    // LocoNet werkt met "slots": voordat je een loc kunt aansturen, moet je eerst een
    // slot AANVRAGEN voor dat DCC-adres (OPC_LOCO_ADR) en wachten tot het command
    // station antwoordt met het toegewezen slotnummer (OPC_SL_RD_DATA) - in
    // tegenstelling tot Dinamo/DCC-EX is dit dus geen enkel, direct commando.
    private readonly Dictionary<int, int> _adresNaarSlot = new();
    private readonly Dictionary<int, (int Stap, bool Vooruit)> _wachtendeSnelheidscommandos = new();

    // Voor het correct samenstellen van de DIRF/SND-bytes (zie VerstuurDirf/VerstuurSnd
    // hieronder): LocoNet stopt de rijrichting EN F0-F4 in dezelfde byte, en F5-F8 in een
    // andere - een los ZetFunctie-commando moet dus de LAATST BEKENDE overige standen
    // erbij houden, anders zou je bijv. het licht uitzetten door alleen de snelheid te
    // wijzigen. Bijgehouden per DECODERADRES (niet per slot, want een adres kan tijdelijk
    // nog geen slot hebben terwijl er al wel een functiewens bekend is).
    private readonly Dictionary<int, bool> _laatsteRichting = new();
    private readonly Dictionary<int, bool[]> _laatsteF0TotF4 = new();
    private readonly Dictionary<int, bool[]> _laatsteF5TotF8 = new();

    public bool Verbonden { get; private set; }
    public string Naam => "Uhlenbrock Intellibox (LocoNet)";

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

                _poort = new SerialPort(comPoort, 19200, Parity.None, 8, StopBits.One)
                {
                    ReadTimeout = 500,
                    WriteTimeout = 500
                };
                _poort.DataReceived += Poort_DataReceived;
                _poort.Open();
                Verbonden = true;
                StatusBericht?.Invoke($"Verbonden met Intellibox op {comPoort} (LocoNet, 19200 baud).");
            }
            catch (Exception ex)
            {
                Verbonden = false;
                throw new InvalidOperationException($"Kon niet verbinden met Intellibox op {comPoort}: {ex.Message}", ex);
            }
        });
    }

    public void Ontkoppelen()
    {
        if (_poort is null) return; // nog nooit verbonden geweest - niets te ontkoppelen, geen misleidende melding nodig
        _poort.DataReceived -= Poort_DataReceived;
        try { _poort.Close(); } catch { /* poort was mogelijk al weg */ }
        _poort.Dispose();
        _poort = null;
        Verbonden = false;
        StatusBericht?.Invoke("Intellibox ontkoppeld.");
    }

    /// <summary>OPC_IDLE (0x85): het universele LocoNet "emergency stop/global power
    /// off"-commando, zonder verdere data-bytes - elk aangesloten LocoNet-apparaat
    /// (command station, decoders) hoort dit direct te herkennen en alle voertuigen te
    /// stoppen, ongeacht adres. Dit is dus het LocoNet-equivalent van Dinamo's F-bit.</summary>
    public void Noodstop()
    {
        if (_poort is null || !Verbonden) return;
        VerstuurPakket(new byte[] { 0x85 });
    }

    /// <summary>OPC_SW_REQ (0xB0): "zet wissel" - het LocoNet-standaardcommando dat vrijwel
    /// elke command station/decoder begrijpt. sw1/sw2 coderen het 11-bits adres, de
    /// gewenste stand (C-bit) en dat het een "aan"-commando is (bit 4, altijd 1 om de
    /// spoel echt te bekrachtigen).</summary>
    public void ZetWissel(int adres, bool afbuigend)
    {
        if (_poort is null || !Verbonden) return;
        int a = adres - 1; // LocoNet-adressen zijn 0-based, Koploper-achtige adressen meestal 1-based
        byte sw1 = (byte)(a & 0x7F);
        byte sw2 = (byte)(((a >> 7) & 0x0F) | 0x10 | (afbuigend ? 0x00 : 0x20));
        VerstuurPakket(new byte[] { 0xB0, sw1, sw2 });
    }

    /// <summary>Veel LocoNet sein-decoders gebruiken gewoon hetzelfde OPC_SW_REQ als een
    /// wissel: elk aspect van het sein krijgt een eigen "wissel"-adres (rood/veilig als de
    /// twee standen). Dat is een aanname, geen universele standaard - werkt het niet met
    /// jouw specifieke sein-decoder, dan zul je dit moeten aanpassen aan hoe die precies
    /// geadresseerd is. Zie ZetWissel voor dezelfde onderliggende opbouw.</summary>
    public void ZetSein(int adres, bool onveiligRood)
    {
        if (_poort is null || !Verbonden) return;
        int a = adres - 1;
        byte sw1 = (byte)(a & 0x7F);
        byte sw2 = (byte)(((a >> 7) & 0x0F) | 0x10 | (onveiligRood ? 0x00 : 0x20));
        VerstuurPakket(new byte[] { 0xB0, sw1, sw2 });
    }

    private void VerstuurPakket(byte[] bytes)
    {
        try
        {
            byte checksum = 0xFF;
            foreach (var b in bytes) checksum ^= b;
            var pakket = new byte[bytes.Length + 1];
            Array.Copy(bytes, pakket, bytes.Length);
            pakket[^1] = checksum;
            _poort!.Write(pakket, 0, pakket.Length);
            HardwareCommunicatieLog.Log("Uit", $"[Intellibox] {Convert.ToHexString(pakket)}");
        }
        catch (Exception ex)
        {
            StatusBericht?.Invoke($"Fout bij versturen naar Intellibox: {ex.Message}");
        }
    }

    /// <summary>Vraagt een LocoNet-slot aan als dat nog niet bekend is voor dit adres
    /// (OPC_LOCO_ADR, 0xBF) en bewaart het snelheidscommando ondertussen als "wachtend" -
    /// zodra het command station antwoordt met OPC_SL_RD_DATA (zie Poort_DataReceived)
    /// wordt het alsnog verstuurd. Is het slot al bekend van een eerdere aanroep, dan
    /// gaat het meteen door zonder wachten.</summary>
    /// <remarks><paramref name="blokNummer"/> wordt genegeerd: LocoNet adresseert de
    /// decoder rechtstreeks via een slot, in tegenstelling tot Dinamo (zie
    /// IHardwareInterface). <paramref name="stappen"/> wordt ook genegeerd - LocoNet se
    /// DIRF-byte werkt altijd met 0-127 rechtstreeks.</remarks>
    public void ZetLocSnelheid(int decoderAdres, int stap, bool vooruit, int blokNummer = 0, int stappen = 126)
    {
        if (_poort is null || !Verbonden) return;
        _laatsteRichting[decoderAdres] = vooruit;
        if (_adresNaarSlot.TryGetValue(decoderAdres, out int slot))
        {
            VerstuurPakket(new byte[] { 0xA0, (byte)slot, (byte)Math.Clamp(stap, 0, 127) });
            VerstuurDirf(decoderAdres, slot);
        }
        else
        {
            _wachtendeSnelheidscommandos[decoderAdres] = (stap, vooruit);
            VerstuurPakket(new byte[] { 0xBF, (byte)((decoderAdres >> 7) & 0x7F), (byte)(decoderAdres & 0x7F) });
        }
    }

    /// <summary>OPC_LOCO_DIRF (0xA1) combineert de rijrichting (bit 5) MET de functies F0
    /// t/m F4 (bit4=F0, bit0-3=F1/F2/F3/F4 - een veelgebruikte, maar niet 100% universeel
    /// gedocumenteerde LocoNet-bitindeling, dus test dit voorzichtig tegen je eigen
    /// decoder) in ÉÉN byte - vandaar dat een los ZetFunctie-commando voor F0-F4 de LAATST
    /// BEKENDE richting/overige-functies moet meenemen, anders zou je die onbedoeld
    /// resetten.</summary>
    private void VerstuurDirf(int decoderAdres, int slot)
    {
        bool vooruit = !_laatsteRichting.TryGetValue(decoderAdres, out var r) || r;
        var f = _laatsteF0TotF4.TryGetValue(decoderAdres, out var bestaand) ? bestaand : new bool[5];
        byte dirf = (byte)((vooruit ? 0x00 : 0x20) | (f[0] ? 0x10 : 0) | (f[1] ? 0x01 : 0) | (f[2] ? 0x02 : 0) | (f[3] ? 0x04 : 0) | (f[4] ? 0x08 : 0));
        VerstuurPakket(new byte[] { 0xA1, (byte)slot, dirf });
    }

    /// <summary>OPC_LOCO_SND (0xA2): bit0-3 = F5/F6/F7/F8 (zelfde soort gangbare, niet
    /// 100% gegarandeerde LocoNet-bitindeling als bij DIRF hierboven).</summary>
    private void VerstuurSnd(int decoderAdres, int slot)
    {
        var f = _laatsteF5TotF8.TryGetValue(decoderAdres, out var bestaand) ? bestaand : new bool[4];
        byte snd = (byte)((f[0] ? 0x01 : 0) | (f[1] ? 0x02 : 0) | (f[2] ? 0x04 : 0) | (f[3] ? 0x08 : 0));
        VerstuurPakket(new byte[] { 0xA2, (byte)slot, snd });
    }

    /// <summary>F0 t/m F8 zijn met de bovenstaande DIRF/SND-commando's te versturen; F9 en
    /// hoger vereisen LocoNet's "extended function"-pakketten (OPC_IMM_PACKET, rauwe NMRA-
    /// functiegroeppakketten) - dat is hier EERLIJK NIET geïmplementeerd (net als Dinamo's
    /// ontbrekende rijcommando): in plaats van een commando te verzinnen dat mogelijk
    /// verkeerd is, wordt dit duidelijk gemeld i.p.v. iets te versturen.</summary>
    /// <remarks><paramref name="blokNummer"/> wordt genegeerd, zelfde reden als bij
    /// ZetLocSnelheid hierboven.</remarks>
    public void ZetFunctie(int decoderAdres, int functieNummer, bool aan, int blokNummer = 0)
    {
        if (_poort is null || !Verbonden) return;
        if (functieNummer is >= 0 and <= 4)
        {
            var f = _laatsteF0TotF4.TryGetValue(decoderAdres, out var bestaand) ? bestaand : new bool[5];
            f[functieNummer] = aan;
            _laatsteF0TotF4[decoderAdres] = f;
            if (_adresNaarSlot.TryGetValue(decoderAdres, out int slot)) VerstuurDirf(decoderAdres, slot);
        }
        else if (functieNummer is >= 5 and <= 8)
        {
            var f = _laatsteF5TotF8.TryGetValue(decoderAdres, out var bestaand) ? bestaand : new bool[4];
            f[functieNummer - 5] = aan;
            _laatsteF5TotF8[decoderAdres] = f;
            if (_adresNaarSlot.TryGetValue(decoderAdres, out int slot)) VerstuurSnd(decoderAdres, slot);
        }
        else
        {
            StatusBericht?.Invoke($"F{functieNummer} wordt niet ondersteund voor Intellibox/LocoNet - alleen F0 t/m F8 (hogere functienummers vereisen 'extended function'-pakketten, hier niet geïmplementeerd).");
        }
    }

    /// <summary>OPC_INPUT_REP (0xB2): bezetmelding/sensor-verandering. OPC_SL_RD_DATA
    /// (0xE7, 14 bytes): antwoord op een slot-aanvraag (OPC_LOCO_ADR) - byte 2 is het
    /// toegewezen slotnummer, bytes 4+9 samen het DCC-adres (laag/hoog 7-bits helften,
    /// zelfde opbouw als bij het versturen). Ruwe, minimale verwerking - genoeg om
    /// BezetmeldingGewijzigd te vullen en wachtende snelheidscommando's alsnog te
    /// versturen, geen volledige LocoNet-framing/foutafhandeling (echte productiecode zou
    /// een nette pakket-buffer met lengte-detectie per opcode moeten hebben).</summary>
    private void Poort_DataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        try
        {
            var buffer = new byte[64];
            int gelezen = _poort!.Read(buffer, 0, buffer.Length);
            HardwareCommunicatieLog.Log("In", $"[Intellibox] {Convert.ToHexString(buffer, 0, gelezen)}");
            for (int i = 0; i + 2 < gelezen; i++)
            {
                if (buffer[i] == 0xB2)
                {
                    int adres = ((buffer[i + 1] & 0x7F) | ((buffer[i + 2] & 0x0F) << 7)) + 1;
                    bool bezet = (buffer[i + 2] & 0x10) != 0;
                    BezetmeldingGewijzigd?.Invoke(adres, bezet);
                }
                else if (buffer[i] == 0xE7 && i + 13 < gelezen)
                {
                    int slot = buffer[i + 2];
                    int decoderAdres = (buffer[i + 4] & 0x7F) | ((buffer[i + 9] & 0x7F) << 7);
                    _adresNaarSlot[decoderAdres] = slot;
                    if (_wachtendeSnelheidscommandos.TryGetValue(decoderAdres, out var wachtend))
                    {
                        _wachtendeSnelheidscommandos.Remove(decoderAdres);
                        VerstuurPakket(new byte[] { 0xA0, (byte)slot, (byte)Math.Clamp(wachtend.Stap, 0, 127) });
                        VerstuurDirf(decoderAdres, slot);
                    }
                }
            }
        }
        catch (TimeoutException) { /* niets gelezen binnen de timeout, prima */ }
        catch (Exception ex)
        {
            StatusBericht?.Invoke($"Fout bij lezen van Intellibox: {ex.Message}");
        }
    }
}
