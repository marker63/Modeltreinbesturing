using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Modeltreinbesturing.Hardware;

/// <summary>
/// Vertaalt een Dinamo-hardwarecommunicatielog (de ruwe hex-bytes zoals HardwareCommunicatieLog
/// ze vastlegt, bijv. via HardwareLogDialog's "Exporteren...") naar leesbare, in Excel te openen
/// rijen - één kolom per betekenisvol veld (type, blok, decoderadres, snelheid, richting,
/// wissel/sein-adres+stand, meldernummer+bezet) in plaats van kale hexadecimale bytes.
///
/// Puur een SPIEGEL van de encodering in DinamoHardware.cs (VerstuurDatagram/ZetLocSnelheid/
/// ZetFunctie/ZetWissel/ZetSein/Poort_DataReceived) - hier bewust GEEN gedeelde helpers
/// hergebruikt (zelfstandig gedecodeerd vanaf de rauwe bytes), zodat dit ook echt onafhankelijk
/// bevestigt dat de verstuurde bytes kloppen, in plaats van simpelweg de eigen encodering te
/// herhalen. Bedoeld om bijvoorbeeld naast een gelijktijdige opname van de ECHTE
/// Koploper-software op dezelfde seriële lijn (via een RS232-sniffer/splitter) regel voor
/// regel te kunnen vergelijken wat er precies verschilt, wanneer puur bytes-nakijken tegen de
/// specificatie niet genoeg houvast meer biedt.
/// </summary>
public static class DinamoLogVertaler
{
    private static readonly string[] Kolommen =
    {
        "Tijdstip", "Richting", "Type", "Blok", "Decoderadres", "Snelheid", "Rijrichting",
        "Wissel/Sein-adres", "Stand", "Meldernummer", "Bezet", "Toelichting", "Ruwe bytes"
    };

    /// <summary>Vertaalt rechtstreeks vanuit het geheugen (HardwareCommunicatieLog.Regels) -
    /// formatteert elke regel eerst naar exact hetzelfde tekstformaat als de bestaande, ruwe
    /// "Exporteren..."-knop (HardwareLogDialog.Exporteren_Click) gebruikt, zodat er maar één
    /// plek is die het regelformaat kent.</summary>
    public static string VertaalLogRegels(IEnumerable<HardwareCommunicatieLog.LogRegel> regels) =>
        VertaalLogBestand(regels.Select(r => $"{r.Tijdstip:yyyy-MM-dd HH:mm:ss.fff} [{r.Richting,3}] {r.Tekst}"));

    /// <summary>Vertaalt de volledige inhoud van een geëxporteerd hardware-communicatielog-
    /// bestand naar CSV-tekst - puntkomma als scheidingsteken (opent in de Nederlandse Excel
    /// zonder importwizard) en een header-regel. Regels die niet als Dinamo-datagram herkend
    /// worden (bijv. van een andere hardware-interface, of gewoon onherkenbaar) komen er ALSNOG
    /// in te staan, met Type="Onbekend"/"Info" en de oorspronkelijke tekst in Toelichting - er
    /// wordt nooit stilzwijgend een regel weggelaten.
    ///
    /// GEVONDEN BUG (ontdekt door een echte USB-wireshark-opname naast dit exportbestand te
    /// leggen): één "In"-logregel kan MEERDERE, ACHTER ELKAAR GEPLAKTE datagrammen bevatten -
    /// dit gebeurt zodra Poort_DataReceived een heel stuk seriële data in één keer ontvangt
    /// (bijv. na een korte periode waarin de UI-thread bezet was met een modaal dialoogvenster
    /// zoals de spookmelding, waardoor binnengekomen bytes zich opstapelen in de OS-buffer
    /// totdat de volgende leesbeurt) - de RAUWE regel bevat dan gewoon alle bytes achter
    /// elkaar, correct qua data, maar visueel als één lange klodder hex. Deze vertaler nam
    /// voorheen alleen het EERSTE datagram van zo'n regel mee en liet de rest stilzwijgend
    /// vallen. Fix: VertaalRegels (nu meervoud) loopt door de HELE hex-string en levert per
    /// gevonden datagram een eigen CSV-rij, precies zoals Poort_DataReceived zelf ook al
    /// door de hele ontvangen buffer heen loopt bij het interpreteren.</summary>
    public static string VertaalLogBestand(IEnumerable<string> ruweRegels)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(";", Kolommen));
        foreach (var ruweRegel in ruweRegels)
        {
            if (string.IsNullOrWhiteSpace(ruweRegel)) continue;
            foreach (var v in VertaalRegels(ruweRegel))
            {
                sb.AppendLine(string.Join(";",
                    v.Tijdstip, v.Richting, v.Type, v.Blok, v.DecoderAdres, v.Snelheid, v.RijRichting,
                    v.WisselOfSeinAdres, v.Stand, v.MelderNummer, v.Bezet, Escape(v.Toelichting), v.RuweBytes));
            }
        }
        return sb.ToString();
    }

    private static string Escape(string tekst) =>
        tekst.IndexOfAny(new[] { ';', '"', '\n', '\r' }) >= 0 ? "\"" + tekst.Replace("\"", "\"\"") + "\"" : tekst;

    private record VertaaldeRegel(string Tijdstip, string Richting, string Type, string Blok,
        string DecoderAdres, string Snelheid, string RijRichting, string WisselOfSeinAdres,
        string Stand, string MelderNummer, string Bezet, string Toelichting, string RuweBytes);

    private static VertaaldeRegel Leeg(string tijdstip, string richting, string type, string toelichting, string ruw) =>
        new(tijdstip, richting, type, "", "", "", "", "", "", "", "", toelichting, ruw);

    // Voorbeeldregel: "2026-09-03 08:37:13.558 [Uit] [Dinamo] 4EAC818181A0AAB9"
    private static readonly Regex RegelPatroon = new(
        @"^(?<tijd>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d+)\s*\[\s*(?<richting>Uit|In|Info)\s*\]\s*(?<rest>.*)$",
        RegexOptions.Compiled);

    private static readonly Regex HexPatroon = new(@"\[Dinamo\]\s*([0-9A-Fa-f]+)", RegexOptions.Compiled);

    private static IEnumerable<VertaaldeRegel> VertaalRegels(string ruweRegel)
    {
        string ingekort = ruweRegel.TrimEnd('\r', '\n');
        var m = RegelPatroon.Match(ingekort);
        if (!m.Success) { yield return Leeg("", "", "Onbekend", "Regel niet herkend als logregel (afkomstig van een andere versie/interface?).", ingekort); yield break; }

        string tijdstip = m.Groups["tijd"].Value;
        string richting = m.Groups["richting"].Value;
        string rest = m.Groups["rest"].Value.Trim();

        if (richting == "Info") { yield return new VertaaldeRegel(tijdstip, richting, "Info", "", "", "", "", "", "", "", "", rest, ""); yield break; }

        var hexMatch = HexPatroon.Match(rest);
        if (!hexMatch.Success) { yield return Leeg(tijdstip, richting, "Onbekend", "Geen Dinamo-hexadecimale bytes gevonden op deze regel (andere hardware-interface geselecteerd geweest?).", rest); yield break; }

        byte[]? alleBytes;
        try { alleBytes = Convert.FromHexString(hexMatch.Groups[1].Value); }
        catch { alleBytes = null; }
        if (alleBytes is null) { yield return Leeg(tijdstip, richting, "Onbekend", "Ongeldige hexadecimale tekst.", rest); yield break; }

        // Eén regel kan MEERDERE, achter elkaar geplakte datagrammen bevatten (zie
        // klasse-uitleg bij VertaalLogBestand) - dus hier net als Poort_DataReceived door de
        // HELE buffer heen lopen i.p.v. alleen het eerste datagram te pakken.
        int i = 0;
        int gevonden = 0;
        while (i < alleBytes.Length)
        {
            if ((alleBytes[i] & 0x80) != 0) { i++; continue; } // geen header-byte, sla over
            int lengte = alleBytes[i] & 0x07;
            int totaal = lengte + 2; // header + data + checksum
            if (i + totaal > alleBytes.Length) break; // onvolledig staartje, niets meer te vertalen
            yield return VertaalDatagram(tijdstip, richting, alleBytes[i..(i + totaal)]);
            gevonden++;
            i += totaal;
        }
        if (gevonden == 0)
            yield return Leeg(tijdstip, richting, "Onbekend", "Geen enkel geldig datagram gevonden in deze bytes.", hexMatch.Groups[1].Value.ToUpperInvariant());
    }

    private static VertaaldeRegel VertaalDatagram(string tijdstip, string richting, byte[] bytes)
    {
        string ruw = Convert.ToHexString(bytes);
        if (bytes.Length < 2) return Leeg(tijdstip, richting, "Onbekend", "Te kort voor een geldig datagram (minimaal header+checksum).", ruw);

        byte header = bytes[0];
        int lengte = header & 0x07;
        bool toggle = (header & 0x40) != 0;
        bool fault = (header & 0x20) != 0;
        string faultTekst = fault ? "Dinamo's eigen FAULT-bit (F) staat gezet - er rijden dan geen voertuigen totdat dit weer op 0 staat." : "";

        int beschikbaar = Math.Max(0, bytes.Length - 2); // min header en checksum
        int werkelijkeLengte = Math.Min(lengte, beschikbaar);
        var data = new byte[werkelijkeLengte];
        for (int i = 0; i < werkelijkeLengte; i++) data[i] = (byte)(bytes[1 + i] & 0x7F);

        string toggleInfo = $"toggle={(toggle ? 1 : 0)}";

        if (data.Length == 0)
            return Leeg(tijdstip, richting, fault ? "Keepalive/NULL (FAULT)" : "Keepalive/NULL",
                CombineerToelichting(toggleInfo, faultTekst), ruw);

        // === Uitgaand: onze eigen commando's (spiegelt DinamoHardware.cs) ===
        if (richting == "Uit")
        {
            if (data.Length == 2 && data[0] == 0x01 && data[1] == 0x00)
                return new VertaaldeRegel(tijdstip, richting, "Reset Fault", "", "", "", "", "", "", "", "",
                    CombineerToelichting("Systeemcommando: schoont Dinamo's eigen foutstatus.", faultTekst), ruw);

            // Magneetartikel (wissel/sein): (0001CMM)(mmmmmmm)[(tijd)] - byte1 top-5-bits vast
            // 00010. GEVONDEN, DEFINITIEF BEVESTIGDE FORMAATFOUT (Wireshark-USB-vergelijking
            // met de echte Koploper-software - zie Hardware/DinamoHardware.cs.ZetWissel voor
            // de volledige toelichting): dit was voorheen 0x20 (prefix "0010"), Koploper
            // bleek daadwerkelijk 0x10 ("0001") te gebruiken, altijd MET een tijd-byte (3
            // databytes, nooit 2).
            if ((data.Length == 2 || data.Length == 3) && (data[0] & 0xF8) == 0x10)
            {
                bool afbuigendOfRood = (data[0] & 0x04) != 0;
                int spoel = ((data[0] & 0x03) << 7) | data[1];
                string tijdToelichting = data.Length == 3 ? $" Tijd-byte: {data[2]}." : " Geen tijd-byte (Dinamo-default).";
                return new VertaaldeRegel(tijdstip, richting, "Magneetartikel (wissel/sein)", "", "", "", "",
                    (spoel + 1).ToString(CultureInfo.InvariantCulture), afbuigendOfRood ? "afbuigend/rood" : "rechtdoor/veilig",
                    "", "", CombineerToelichting("Adres is 1-based (spoelnummer+1), zelfde als ZetWissel/ZetSein." + tijdToelichting, faultTekst), ruw);
            }

            // DCC-commando's delen "010110B"(0x2C|msb, snelheid+F13-28) of "010100B"(0x28|msb, F0-F12)
            byte commandoBasis = (byte)(data[0] & 0xFE);
            byte blokMsb = (byte)(data[0] & 0x01);

            if (commandoBasis == 0x2C && (data.Length == 5 || data.Length == 6) && data[2] <= 1)
            {
                // DCC 126 Snelh.: (010110B)(bbbbbbb)(000000R)(SSSSSSS)(ddddddd)[(DDDDDDD)]
                int blok = (blokMsb << 7) | data[1];
                bool vooruit = data[2] == 1;
                int snelheid = data[3];
                int adres = data.Length == 5 ? data[4] : data[4] | (data[5] << 7);
                return new VertaaldeRegel(tijdstip, richting, "Snelheid (DCC 126-staps)", blok.ToString(CultureInfo.InvariantCulture),
                    adres.ToString(CultureInfo.InvariantCulture), snelheid.ToString(CultureInfo.InvariantCulture),
                    vooruit ? "vooruit" : "achteruit", "", "", "", "", faultTekst, ruw);
            }

            if (commandoBasis == 0x2C && (data.Length == 5 || data.Length == 6) && data[2] is >= 4 and <= 7)
            {
                // DCC F13..F20 / F21..F28: (010110B)(bbbbbbb)(00001{0,1}F)(fffffff)(ddddddd)[(DDDDDDD)]
                int blok = (blokMsb << 7) | data[1];
                bool tweedeGroep = (data[2] & 0x02) != 0; // 0b0000100=F13-20, 0b0000110=F21-28
                int laagsteNummer = tweedeGroep ? 21 : 13;
                int hoogsteBit = data[2] & 0x01;
                int adres = data.Length == 5 ? data[4] : data[4] | (data[5] << 7);
                var actieveFuncties = new List<string>();
                for (int i = 0; i < 7; i++) if ((data[3] & (1 << i)) != 0) actieveFuncties.Add($"F{laagsteNummer + i}");
                if (hoogsteBit != 0) actieveFuncties.Add($"F{laagsteNummer + 7}");
                string functieTekst = actieveFuncties.Count > 0 ? string.Join(",", actieveFuncties) + " AAN (rest van deze groep UIT)" : $"F{laagsteNummer}..F{laagsteNummer + 7} allemaal UIT";
                return new VertaaldeRegel(tijdstip, richting, $"Functies F{laagsteNummer}-F{laagsteNummer + 7}", blok.ToString(CultureInfo.InvariantCulture),
                    adres.ToString(CultureInfo.InvariantCulture), "", "", "", "", "", "",
                    CombineerToelichting(functieTekst, faultTekst), ruw);
            }

            if (commandoBasis == 0x28 && (data.Length == 4 || data.Length == 5) && (data[2] & 0x40) != 0)
            {
                // DCC Snelheid (28-staps): (010100B)(bbbbbbb)(1RSSSSS)(ddddddd)[(DDDDDDD)] -
                // GEVONDEN GAT (gebruikersmelding: de leesbare CSV toonde onzinnige, snel
                // wisselende functiecombinaties tijdens een simpele snelheidsramp): toen
                // DinamoHardware.ZetLocSnelheid dit 28-staps commando kreeg (zelfde
                // commandobasis 010100B als Functiegroep1, alleen onderscheiden door bit6 van
                // byte3), is deze VERTALER daar niet gelijktijdig op aangepast - elk
                // snelheidscommando werd hierdoor ten onrechte als een functiegroep-wijziging
                // gelezen. Nu eerst op bit6 gecontroleerd, exact zoals de encoder het ook
                // onderscheidt.
                int blok = (blokMsb << 7) | data[1];
                bool vooruit = (data[2] & 0x20) != 0;
                int snelheid = data[2] & 0x1F;
                int adres = data.Length == 4 ? data[3] : data[3] | (data[4] << 7);
                return new VertaaldeRegel(tijdstip, richting, "Snelheid (DCC 28-staps)", blok.ToString(CultureInfo.InvariantCulture),
                    adres.ToString(CultureInfo.InvariantCulture), snelheid.ToString(CultureInfo.InvariantCulture),
                    vooruit ? "vooruit" : "achteruit", "", "", "", "", faultTekst, ruw);
            }

            if (commandoBasis == 0x28 && (data.Length == 4 || data.Length == 5))
            {
                // DCC Functiegroep 1,2a,2b: (010100B)(bbbbbbb)(0XXFFFF)(ddddddd)[(DDDDDDD)]
                int blok = (blokMsb << 7) | data[1];
                int xx = (data[2] >> 4) & 0x03;
                int ffff = data[2] & 0x0F;
                int adres = data.Length == 4 ? data[3] : data[3] | (data[4] << 7);
                string groepTekst = xx switch
                {
                    0 => "Licht UIT, " + FunctieBits(ffff, 1),
                    1 => "Licht AAN, " + FunctieBits(ffff, 1),
                    2 => FunctieBits(ffff, 9),
                    3 => FunctieBits(ffff, 5),
                    _ => "?"
                };
                return new VertaaldeRegel(tijdstip, richting, "Functies F0-F12 (groep)", blok.ToString(CultureInfo.InvariantCulture),
                    adres.ToString(CultureInfo.InvariantCulture), "", "", "", "", "", "",
                    CombineerToelichting(groepTekst, faultTekst), ruw);
            }

            return new VertaaldeRegel(tijdstip, richting, "Onbekend commando", "", "", "", "", "", "", "", "",
                CombineerToelichting($"Header niet herkend als een van onze eigen commando's ({toggleInfo}).", faultTekst), ruw);
        }

        // === Inkomend: van Dinamo (spiegelt Poort_DataReceived) ===
        if (data.Length == 2 && (data[0] & 0x60) == 0x40)
        {
            // Switch-event: (10CSSSS)(sssssss)
            bool bezet = (data[0] & 0x10) != 0;
            int melder = (((data[0] & 0x0F) << 7) | data[1]) + 1; // 0-based -> 1-based
            return new VertaaldeRegel(tijdstip, richting, "Bezetmelding (Switch-event)", "", "", "", "", "", "",
                melder.ToString(CultureInfo.InvariantCulture), bezet ? "BEZET" : "vrij", faultTekst, ruw);
        }

        return new VertaaldeRegel(tijdstip, richting, "Onbekend/niet gedecodeerd", "", "", "", "", "", "", "", "",
            CombineerToelichting($"Inkomend datagram met data, maar geen herkend patroon ({toggleInfo}).", faultTekst), ruw);
    }

    private static string FunctieBits(int ffff, int laagsteNummer)
    {
        var actief = new List<string>();
        for (int i = 0; i < 4; i++) if ((ffff & (1 << i)) != 0) actief.Add($"F{laagsteNummer + i}");
        return actief.Count > 0 ? string.Join(",", actief) + " AAN" : $"F{laagsteNummer}-F{laagsteNummer + 3} UIT";
    }

    private static string CombineerToelichting(string primair, string faultTekst) =>
        string.IsNullOrEmpty(faultTekst) ? primair : $"{primair} | {faultTekst}";
}
