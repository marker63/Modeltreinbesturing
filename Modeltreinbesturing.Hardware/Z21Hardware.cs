using System.Net;
using System.Net.Sockets;

namespace Modeltreinbesturing.Hardware;

/// <summary>
/// Pakketten van het Z21 LAN-protocol (Roco/Fleischmann Z21, z21 start, SmartRail, DR5000 e.d.).
/// Apart gehouden van de verbindingslogica, zodat de bytes los te controleren zijn.
///
/// Bron: "Z21 LAN Protocol Specification" v1.13 van Z21/Roco. Elk pakket: DataLen (2 bytes,
/// little endian, incl. deze 4 header-bytes) + Header (2 bytes LE) + data. Voor LAN_X-pakketten
/// (Header 0x0040) is de laatste byte een XOR over X-Header en alle databytes ervoor.
/// Kruiscontrole van de bytelayouts gedaan met de open-source Traintastic (alleen de
/// protocolfeiten, geen code overgenomen).
///
/// STATUS: NOG NIET GETEST tegen een echte Z21 - alleen de byte-opbouw is nagerekend tegen
/// de voorbeelden uit de specificatie.
/// </summary>
public static class Z21Pakketten
{
    public const ushort HeaderGetSerialNumber = 0x10;
    public const ushort HeaderLogoff = 0x30;
    public const ushort HeaderLanX = 0x40;
    public const ushort HeaderSetBroadcastFlags = 0x50;
    public const ushort HeaderRmbusDataChanged = 0x80;
    public const ushort HeaderRmbusGetData = 0x81;
    public const ushort HeaderSystemStateDataChanged = 0x84;
    public const ushort HeaderSystemStateGetData = 0x85;

    /// <summary>0x00000001 rijden/schakelen (o.a. kortsluiting-melding), 0x00000002 R-Bus
    /// terugmelders, 0x00000100 systeemstatus.</summary>
    public const uint BroadcastFlagsNodig = 0x00000001 | 0x00000002 | 0x00000100;

    private static byte[] Pakket(ushort header, params byte[] data)
    {
        int lengte = 4 + data.Length;
        var p = new byte[lengte];
        p[0] = (byte)(lengte & 0xFF);
        p[1] = (byte)(lengte >> 8);
        p[2] = (byte)(header & 0xFF);
        p[3] = (byte)(header >> 8);
        Array.Copy(data, 0, p, 4, data.Length);
        return p;
    }

    /// <summary>LAN_X-pakket: X-Header + databytes + XOR-controlebyte.</summary>
    private static byte[] LanX(byte xHeader, params byte[] db)
    {
        var data = new byte[1 + db.Length + 1];
        data[0] = xHeader;
        byte xor = xHeader;
        for (int i = 0; i < db.Length; i++) { data[1 + i] = db[i]; xor ^= db[i]; }
        data[^1] = xor;
        return Pakket(HeaderLanX, data);
    }

    public static byte[] GetSerialNumber() => Pakket(HeaderGetSerialNumber);
    public static byte[] Logoff() => Pakket(HeaderLogoff);
    public static byte[] SystemStateGetData() => Pakket(HeaderSystemStateGetData);

    public static byte[] SetBroadcastFlags(uint flags) =>
        Pakket(HeaderSetBroadcastFlags, (byte)flags, (byte)(flags >> 8), (byte)(flags >> 16), (byte)(flags >> 24));

    /// <summary>Welke groep R-Bus-melders (groep 0 = melder 1..80, groep 1 = 81..160, ...).</summary>
    public static byte[] RmbusGetData(byte groep) => Pakket(HeaderRmbusGetData, groep);

    public static byte[] SetTrackPowerOn() => LanX(0x21, 0x81);
    public static byte[] SetTrackPowerOff() => LanX(0x21, 0x80);

    /// <summary>LAN_X_SET_STOP: noodstop van alle locs (spanning blijft op het spoor).</summary>
    public static byte[] SetStop() => LanX(0x80);

    /// <summary>LAN_X_SET_TURNOUT. <paramref name="adres"/> is 1-based (zoals in het baanontwerp);
    /// het protocol telt vanaf 0. <paramref name="uitgang2"/> kiest uitgang 2 (P=1) of 1 (P=0).
    /// <paramref name="activeer"/> true = uitgang aan, false = weer uit (verplicht na de puls).</summary>
    public static byte[] SetTurnout(int adres, bool uitgang2, bool activeer)
    {
        int fadr = Math.Clamp(adres, 1, 4096) - 1;
        byte db2 = (byte)(0x80 | (activeer ? 0x08 : 0x00) | (uitgang2 ? 0x01 : 0x00)); // 10Q0A00P, Q=0
        return LanX(0x53, (byte)(fadr >> 8), (byte)(fadr & 0xFF), db2);
    }

    private static (byte hoog, byte laag) LocAdres(int adres)
    {
        adres = Math.Clamp(adres, 1, 9999);
        return adres >= 128
            ? ((byte)(0xC0 | (adres >> 8)), (byte)(adres & 0xFF))
            : ((byte)0x00, (byte)(adres & 0x7F));
    }

    /// <summary>Snelheidsbyte RVVVVVVV. <paramref name="stap"/>: 0 = stop, daarna 1..stappen.
    /// Stap 1 is in het protocol de tweede waarde (waarde 1 is noodstop), vandaar de +1.</summary>
    public static byte SnelheidsByte(int stap, bool vooruit, int stappen)
    {
        byte richting = vooruit ? (byte)0x80 : (byte)0x00;
        if (stap <= 0) return richting;
        int v = stap + 1;
        if (stappen <= 14) return (byte)(richting | (v & 0x0F));
        if (stappen <= 28)
        {
            v += 2;
            return (byte)(richting | ((v >> 1) & 0x0F) | ((v & 0x01) << 4));
        }
        return (byte)(richting | (v & 0x7F));
    }

    /// <summary>LAN_X_SET_LOCO_DRIVE: DB0 = 0x10 (14 stappen), 0x12 (28) of 0x13 (126).</summary>
    public static byte[] SetLocoDrive(int adres, int stap, bool vooruit, int stappen)
    {
        var (hoog, laag) = LocAdres(adres);
        byte db0 = stappen <= 14 ? (byte)0x10 : stappen <= 28 ? (byte)0x12 : (byte)0x13;
        stap = Math.Clamp(stap, 0, stappen <= 14 ? 14 : stappen <= 28 ? 28 : 126);
        return LanX(0xE4, db0, hoog, laag, SnelheidsByte(stap, vooruit, stappen));
    }

    /// <summary>LAN_X_SET_LOCO_FUNCTION: TTNNNNNN, TT = 00 uit / 01 aan, N = functienummer.</summary>
    public static byte[] SetLocoFunction(int adres, int functie, bool aan)
    {
        var (hoog, laag) = LocAdres(adres);
        byte db3 = (byte)(((aan ? 1 : 0) << 6) | (Math.Clamp(functie, 0, 28) & 0x3F));
        return LanX(0xE4, 0xF8, hoog, laag, db3);
    }
}

/// <summary>
/// Roco/Fleischmann Z21 (en compatibele: z21 start, SmartRail, DR5000 van Digikeijs) via het
/// netwerk (UDP, poort 21105). Het "COM-poort"-veld in het scherm is hier het IP-adres van de
/// Z21 (bijv. 192.168.0.111, eventueel met ":poort").
///
/// STATUS: NOG NIET GETEST tegen een echte Z21. Gebouwd volgens de officiële specificatie
/// (zie Z21Pakketten). Eerst voorzichtig proberen met één loc en één wissel.
///
/// Wat werkt volgens de spec:
/// - Rijden (14/28/126 stappen) en functies F0-F28, noodstop (LAN_X_SET_STOP).
/// - Wissels/seinen als accessoire-adres; na de puls wordt de uitgang weer uitgeschakeld
///   (de spec eist dat de client dat zelf doet).
/// - Bezetmelders: R-Bus terugmelders; meldernummer n = ingang n (1-based, 80 per groep).
///   Z21 LocoNet-detectors en RailCom zijn nog niet gekoppeld.
/// - Kortsluiting: de Z21 meldt dit zelf (LAN_X_BC_TRACK_SHORT_CIRCUIT en systeemstatus) en
///   schakelt dan de spoorspanning UIT. Wij zetten die niet vanzelf weer aan.
/// - Houdt de verbinding levend (de Z21 gooit een client na een minuut stilte eruit).
///
/// Bekend open punt: welke wisselstand bij uitgang 1 of 2 hoort hangt van de bekabeling af
/// (de spec zegt dat uitdrukkelijk). Zie <see cref="AfbuigendIsUitgang2"/>.
/// </summary>
public class Z21Hardware : IHardwareInterface
{
    private const int StandaardPoort = 21105;
    private const int WisselPulsMs = 100; // voorbeeld uit de spec: "wait 100ms; deactivate"
    private static readonly TimeSpan Keepalive = TimeSpan.FromSeconds(15);

    private UdpClient? _udp;
    private CancellationTokenSource? _stop;
    private Task? _luisterTaak;
    private Task? _keepaliveTaak;
    private readonly object _slot = new();
    private readonly Dictionary<int, bool> _laatsteMelderStatus = new();
    private readonly HashSet<int> _aangevraagdeMelders = new();
    private bool _kortsluiting;

    public bool Verbonden { get; private set; }
    public string Naam => "Z21 (nog niet getest)";

    /// <summary>true: afbuigend = uitgang 2 (P=1), rechtdoor = uitgang 1 (P=0). Bij een
    /// wissel die precies andersom staat dit omdraaien. De Z21-specificatie laat dit
    /// bewust aan de bekabeling over.</summary>
    public bool AfbuigendIsUitgang2 { get; set; } = true;

    public bool KanMelderStatusOpvragen => true;

    public event Action<int, bool>? BezetmeldingGewijzigd;
    public event Action<string>? StatusBericht;
    public event Action<bool>? KortsluitingStatusGewijzigd;

    public async Task VerbindenAsync(string adres)
    {
        Ontkoppelen();
        try
        {
            var (host, poort) = SplitsAdres(adres);
            var udp = new UdpClient();
            udp.Connect(host, poort);
            _udp = udp;

            // Eerst kijken of er echt een Z21 antwoordt - UDP geeft zelf geen foutmelding.
            Verstuur(Z21Pakketten.GetSerialNumber(), keepalive: true);
            var antwoord = udp.ReceiveAsync();
            if (await Task.WhenAny(antwoord, Task.Delay(2000)) != antwoord)
                throw new TimeoutException($"Geen antwoord van een Z21 op {host}:{poort}. Controleer het IP-adres en of deze computer op hetzelfde netwerk zit als de Z21.");
            VerwerkDatagram(antwoord.Result.Buffer);

            Verbonden = true;
            _stop = new CancellationTokenSource();
            _luisterTaak = Task.Run(() => LuisterLus(udp, _stop.Token));
            _keepaliveTaak = Task.Run(() => KeepaliveLus(_stop.Token));

            Verstuur(Z21Pakketten.SetBroadcastFlags(Z21Pakketten.BroadcastFlagsNodig));
            Verstuur(Z21Pakketten.SystemStateGetData());
            Verstuur(Z21Pakketten.SetTrackPowerOn());
            StatusBericht?.Invoke($"Verbonden met Z21 op {host}:{poort}. Let op: deze koppeling is nog niet getest op een echte Z21.");
        }
        catch (Exception ex)
        {
            Ontkoppelen();
            throw new InvalidOperationException(ex is TimeoutException ? ex.Message : $"Kon niet verbinden met Z21 op {adres}: {ex.Message}", ex);
        }
    }

    public void Ontkoppelen()
    {
        if (_udp is null) return; // nooit verbonden geweest
        try { if (Verbonden) Verstuur(Z21Pakketten.Logoff()); } catch { }
        Verbonden = false;
        try { _stop?.Cancel(); } catch { }
        try { _udp.Close(); } catch { }
        _udp = null;
        _stop = null;
        lock (_slot) { _laatsteMelderStatus.Clear(); _aangevraagdeMelders.Clear(); }
        _kortsluiting = false;
        StatusBericht?.Invoke("Z21 ontkoppeld.");
    }

    /// <summary>"adres" of "adres:poort".</summary>
    private static (string host, int poort) SplitsAdres(string adres)
    {
        adres = (adres ?? "").Trim();
        if (adres.Length == 0) throw new ArgumentException("Vul het IP-adres van de Z21 in (bijv. 192.168.0.111).");
        int dubbel = adres.LastIndexOf(':');
        if (dubbel > 0 && adres.IndexOf(':') == dubbel && int.TryParse(adres[(dubbel + 1)..], out int p) && p is > 0 and < 65536)
            return (adres[..dubbel], p);
        return (adres, StandaardPoort);
    }

    private void Verstuur(byte[] pakket, bool keepalive = false)
    {
        var udp = _udp;
        if (udp is null) return;
        try
        {
            udp.Send(pakket, pakket.Length);
            HardwareCommunicatieLog.Log("Uit", $"[Z21] {Convert.ToHexString(pakket)}", keepalive: keepalive);
        }
        catch (Exception ex)
        {
            StatusBericht?.Invoke($"Kon niets versturen naar de Z21: {ex.Message}");
        }
    }

    public void ZetWissel(int adres, bool afbuigend)
    {
        bool uitgang2 = afbuigend == AfbuigendIsUitgang2;
        Verstuur(Z21Pakketten.SetTurnout(adres, uitgang2, activeer: true));
        // De spec eist dat de client de uitgang zelf weer uitschakelt (anders blijft de
        // wisselmotor onder stroom). Niet blokkerend, de aanroeper zit vaak op de UI-thread.
        _ = Task.Delay(WisselPulsMs).ContinueWith(_ => Verstuur(Z21Pakketten.SetTurnout(adres, uitgang2, activeer: false)));
    }

    public void ZetSein(int adres, bool onveiligRood) => ZetWissel(adres, onveiligRood);

    public void ZetLocSnelheid(int decoderAdres, int stap, bool vooruit, int blokNummer = 0, int stappen = 126) =>
        Verstuur(Z21Pakketten.SetLocoDrive(decoderAdres, stap, vooruit, stappen));

    public void ZetFunctie(int decoderAdres, int functieNummer, bool aan, int blokNummer = 0)
    {
        if (functieNummer is < 0 or > 28)
        {
            StatusBericht?.Invoke($"Z21: functie F{functieNummer} wordt niet ondersteund (alleen F0-F28).");
            return;
        }
        Verstuur(Z21Pakketten.SetLocoFunction(decoderAdres, functieNummer, aan));
    }

    public void Noodstop() => Verstuur(Z21Pakketten.SetStop());

    /// <summary>Vraagt de groep R-Bus-melders op waar dit meldernummer in zit; het antwoord komt
    /// als gewone melding binnen. Alleen voor dit meldernummer wordt ook een "vrij" doorgegeven.</summary>
    public void VraagMelderStatusOp(int meldernummer)
    {
        if (meldernummer < 1) return;
        lock (_slot) _aangevraagdeMelders.Add(meldernummer);
        Verstuur(Z21Pakketten.RmbusGetData((byte)((meldernummer - 1) / 80)));
    }

    private async Task KeepaliveLus(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(Keepalive, token);
                Verstuur(Z21Pakketten.SystemStateGetData(), keepalive: true);
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task LuisterLus(UdpClient udp, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var resultaat = await udp.ReceiveAsync(token);
                VerwerkDatagram(resultaat.Buffer);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested) StatusBericht?.Invoke($"Fout bij lezen van de Z21: {ex.Message}");
        }
    }

    /// <summary>Eén UDP-datagram kan meerdere pakketten achter elkaar bevatten.</summary>
    private void VerwerkDatagram(byte[] data)
    {
        int pos = 0;
        while (pos + 4 <= data.Length)
        {
            int lengte = data[pos] | (data[pos + 1] << 8);
            if (lengte < 4 || pos + lengte > data.Length) break;
            var pakket = new byte[lengte];
            Array.Copy(data, pos, pakket, 0, lengte);
            pos += lengte;
            try { VerwerkPakket(pakket); }
            catch (Exception ex) { StatusBericht?.Invoke($"Z21-bericht niet verwerkt: {ex.Message}"); }
        }
    }

    private void VerwerkPakket(byte[] p)
    {
        ushort header = (ushort)(p[2] | (p[3] << 8));
        bool ruis = header == Z21Pakketten.HeaderSystemStateDataChanged;
        HardwareCommunicatieLog.Log("In", $"[Z21] {Convert.ToHexString(p)}", keepalive: ruis);

        switch (header)
        {
            case Z21Pakketten.HeaderRmbusDataChanged when p.Length >= 15:
                VerwerkRmbus(p[4], p, 5);
                break;

            case Z21Pakketten.HeaderSystemStateDataChanged when p.Length >= 18:
                // CentralState (byte 16): 0x04 kortsluiting; CentralStateEx (byte 17): 0x04/0x08 kortsluiting extern/intern.
                ZetKortsluiting((p[16] & 0x04) != 0 || (p.Length > 17 && (p[17] & 0x0C) != 0));
                break;

            case Z21Pakketten.HeaderLanX when p.Length >= 7:
                if (p[4] == 0x61 && p[5] == 0x08) ZetKortsluiting(true);                  // LAN_X_BC_TRACK_SHORT_CIRCUIT
                else if (p[4] == 0x61 && p[5] == 0x01) ZetKortsluiting(false);            // spoorspanning weer aan
                else if (p[4] == 0x62 && p.Length >= 8) ZetKortsluiting((p[6] & 0x04) != 0); // LAN_X_STATUS_CHANGED
                break;
        }
    }

    private void VerwerkRmbus(int groep, byte[] p, int begin)
    {
        for (int i = 0; i < 80; i++)
        {
            bool bezet = (p[begin + (i >> 3)] & (1 << (i & 7))) != 0;
            int melder = groep * 80 + i + 1;
            bool geef;
            lock (_slot)
            {
                bool bekend = _laatsteMelderStatus.TryGetValue(melder, out bool vorige);
                // Eerste keer gezien: alleen "bezet" (of een expliciet opgevraagde melder) doorgeven,
                // anders zou elk van de 80 ingangen van de groep een "vrij" melden.
                geef = bekend ? vorige != bezet : (bezet || _aangevraagdeMelders.Contains(melder));
                _laatsteMelderStatus[melder] = bezet;
            }
            if (geef) BezetmeldingGewijzigd?.Invoke(melder, bezet);
        }
    }

    private void ZetKortsluiting(bool actief)
    {
        if (_kortsluiting == actief) return;
        _kortsluiting = actief; // eerst bijwerken, dan pas melden (zelfde les als BUG #29)
        StatusBericht?.Invoke(actief
            ? "Z21 meldt KORTSLUITING en schakelt de spoorspanning uit. Zet de spanning pas weer aan nadat je de oorzaak weg hebt (bijv. via de Z21-app of een Maus)."
            : "Z21: kortsluiting opgeheven.");
        KortsluitingStatusGewijzigd?.Invoke(actief);
    }
}
