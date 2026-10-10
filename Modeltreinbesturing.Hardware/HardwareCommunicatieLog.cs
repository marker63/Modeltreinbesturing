namespace Modeltreinbesturing.Hardware;

/// <summary>
/// Centraal, gedeeld communicatielogboek voor ALLE hardware-koppelingen (Dinamo/Intellibox/
/// DCC-EX/Simulatie) - elke implementatie roept Log() aan op het moment dat er
/// daadwerkelijk iets naar de poort geschreven of van de poort gelezen wordt, zodat een
/// apart venster (HardwareLogDialog) dit live kan tonen. Vooral bedoeld om te helpen
/// diagnosticeren of/wat er precies mis is met de communicatie, wanneer software-logica
/// alleen (zoals bij de wissel-reservering-kleuring) niet genoeg houvast bood.
/// </summary>
public static class HardwareCommunicatieLog
{
    public record LogRegel(DateTime Tijdstip, string Richting, string Tekst);

    /// <summary>Vuurt bij ELKE nieuwe regel - een open HardwareLogDialog abonneert zich
    /// hierop voor een live weergave.</summary>
    public static event Action<LogRegel>? NieuweRegel;

    private const int MaxRegels = 2000;
    private static readonly object _vergrendeling = new();
    private static readonly List<LogRegel> _regels = new();

    /// <summary>Alle regels sinds het opstarten (of sinds de laatste Wissen()) - voor een
    /// HardwareLogDialog die pas NA een aantal regels geopend wordt, zodat de geschiedenis
    /// niet verloren is.
    ///
    /// GEVONDEN, ECHTE RACE CONDITION (gebruikersmelding: NullReferenceException in de
    /// debugger, "regel was null", tijdens HardwareLogDialog's constructor-foreach over
    /// deze lijst): Log() hieronder wordt aangeroepen vanuit DE SERIËLE POORT SE EIGEN
    /// ACHTERGRONDTHREAD (Poort_DataReceived in elke Hardware-klasse), terwijl deze
    /// property doorgaans vanaf de UI-thread gelezen/doorlopen wordt (bijv. hier via
    /// foreach) - zonder enige synchronisatie kan het schrijven (List&lt;T&gt;.Add, dat
    /// intern soms de backing array moet HERALLOCEREN) precies samenvallen met het lezen
    /// (foreach van diezelfde lijst), wat een corrupte/lege lees-toestand kan opleveren -
    /// vandaar een element dat plotseling null lijkt, ook al is er nooit expliciet null
    /// toegevoegd. Fix: Log() muteert _regels nu ALTIJD onder lock, en deze property geeft
    /// een SNAPSHOT (een kopie) terug, ook onder lock - zodat een aanroeper altijd een
    /// stabiele, op dat moment bevroren lijst doorloopt, ongeacht wat een andere thread
    /// daarna nog aan _regels zelf verandert.</summary>
    public static IReadOnlyList<LogRegel> Regels
    {
        get { lock (_vergrendeling) return _regels.ToList(); }
    }

    /// <summary>Richting is altijd "Uit" (naar de hardware toe) of "In" (van de hardware
    /// vandaan) - een vast, herkenbaar onderscheid voor de weergave.
    ///
    /// GEVONDEN, BELANGRIJK GAT (gebruikersmelding: een geëxporteerd logbestand bevatte,
    /// na een paar minuten wachten voordat er geëxporteerd werd, alleen nog keepalive-
    /// verkeer - de eigenlijke testgebeurtenissen (waar het allemaal om ging) waren allang
    /// uit het buffer verdwenen): Dinamo stuurt elke ~200ms een keepalive (nul-datagram)
    /// in BEIDE richtingen - bij een vaste regel-limiet (MaxRegels) vult dat het hele
    /// buffer al binnen een paar minuten, ook als er verder niets interessants gebeurt.
    /// Een keepalive heeft geen enkele diagnostische waarde ACHTERAF (bewijst alleen "de
    /// verbinding leefde op dat moment") - <paramref name="keepalive"/> zorgt dat zo'n
    /// regel wél nog live verschijnt (NieuweRegel vuurt hieronder sowieso, voor een open
    /// HardwareLogDialog), maar NIET bewaard wordt in de geschiedenis/export - zodat
    /// MaxRegels voortaan 2000 daadwerkelijk BETEKENISVOLLE regels dekt, in plaats van
    /// grotendeels keepalive-ruis.</summary>
    public static void Log(string richting, string tekst, bool keepalive = false)
    {
        var regel = new LogRegel(DateTime.Now, richting, tekst);
        if (!keepalive)
        {
            lock (_vergrendeling)
            {
                _regels.Add(regel);
                while (_regels.Count > MaxRegels) _regels.RemoveAt(0);
                SchrijfNaarSessieBestand(regel);
            }
        }
        NieuweRegel?.Invoke(regel);
    }

    public static void Wissen()
    {
        lock (_vergrendeling) _regels.Clear(); // het sessiebestand op schijf blijft bewust ongemoeid
    }

    // BUG #45 (gebruikersmelding 10-10-2026: "de logging van de eerste testrit heb ik niet
    // opgeslagen"): het log leefde alleen in het geheugen (max. 2000 regels) en ging verloren
    // bij het afsluiten of crashen. Nu schrijft elke niet-keepalive-regel meteen ook naar een
    // sessiebestand in %AppData%\Modeltreinbesturing\Logs (zelfde opmaak als de handmatige
    // export). Per start van het programma een nieuw bestand; de 20 nieuwste blijven bewaard.
    private static StreamWriter? _sessieBestand;
    private static bool _sessieBestandPogingGedaan;

    /// <summary>Pad van het sessielogbestand van deze start (null als aanmaken niet lukte).</summary>
    public static string? SessieLogPad { get; private set; }

    private static void SchrijfNaarSessieBestand(LogRegel regel)
    {
        try
        {
            if (_sessieBestand is null)
            {
                if (_sessieBestandPogingGedaan) return; // één poging per start; loggen mag nooit het programma hinderen
                _sessieBestandPogingGedaan = true;
                string proces = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "programma");
                string map = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Modeltreinbesturing", "Logs");
                Directory.CreateDirectory(map);
                foreach (var oud in new DirectoryInfo(map).GetFiles($"hardwarelog_{proces}_*.txt").OrderByDescending(f => f.CreationTimeUtc).Skip(19))
                {
                    try { oud.Delete(); } catch { /* in gebruik of al weg - maakt niet uit */ }
                }
                SessieLogPad = Path.Combine(map, $"hardwarelog_{proces}_{DateTime.Now:yyyy-MM-dd_HHmmss}.txt");
                _sessieBestand = new StreamWriter(new FileStream(SessieLogPad, FileMode.Create, FileAccess.Write, FileShare.ReadWrite), new System.Text.UTF8Encoding(false)) { AutoFlush = true };
                _sessieBestand.WriteLine($"# {proces} - hardware-communicatielog van deze sessie (automatisch bewaard, elke regel direct weggeschreven)");
            }
            _sessieBestand.WriteLine($"{regel.Tijdstip:yyyy-MM-dd HH:mm:ss.fff} [{regel.Richting,4}] {regel.Tekst}");
        }
        catch
        {
            _sessieBestand = null; // schijf vol / geen rechten: stil doorgaan zonder bestand
        }
    }
}
