using System.IO;

namespace Modeltreinbesturing;

/// <summary>
/// Automatische backups: bij het sluiten van het programma wordt de HELE baan (alle vier
/// lagen, incl. wisselstanden en loc-posities - zie ProjectOpslag) tijdgestempeld
/// weggeschreven, en bij het openen wordt de meest recente automatisch weer geladen. Zo
/// hoef je niet zelf aan Opslaan te denken. Losstaand van de gewone Bestand -> Opslaan/
/// Laden-bestanden (die blijven gewoon werken zoals voorheen) - dit is puur een vangnet.
/// </summary>
public static class BackupBeheerder
{
    private static readonly string BackupMap = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Modeltreinbesturing", "Backups");

    /// <summary>Hoeveel backups er maximaal bewaard blijven - de oudste worden na elke
    /// nieuwe backup automatisch opgeruimd, zodat de map niet onbeperkt aangroeit.</summary>
    private const int MaxBackups = 40;

    private static string NieuwBackupPad() =>
        Path.Combine(BackupMap, $"backup_{DateTime.Now:yyyy-MM-dd_HHmmss}.json");

    /// <summary>Alle bewaarde backups, nieuwste eerst - voor het "Oudere backup openen"-menu.</summary>
    public static List<(string Pad, DateTime Tijdstip)> AlleBackups()
    {
        if (!Directory.Exists(BackupMap)) return new();
        return Directory.GetFiles(BackupMap, "backup_*.json")
            .Select(p => (Pad: p, Tijdstip: File.GetLastWriteTime(p)))
            .OrderByDescending(b => b.Tijdstip)
            .ToList();
    }

    /// <summary>Meest recente backup, of null als er nog geen enkele backup bestaat (bijv.
    /// de allereerste keer dat het programma gestart wordt).</summary>
    public static string? LaatsteBackupPad() => AlleBackups().FirstOrDefault().Pad;

    /// <summary>Schrijft een nieuwe, tijdgestempelde backup weg en ruimt daarna de oudste
    /// backups op tot MaxBackups. Faalt de backup zelf (bijv. schijf vol), dan wordt dat
    /// stil genegeerd - een mislukte backup mag het sluiten van het programma nooit
    /// blokkeren.</summary>
    public static void MaakBackup(BlokBeheerder blokBeheerder, BaanOntwerpBeheerder baanBeheerder, WisselstraatBeheerder wisselstraatBeheerder, TreinrouteBeheerder routeBeheerder, TreintypeBeheerder treintypeBeheerder, RichtingsverbodBeheerder richtingsverbodBeheerder, StopverbodBeheerder stopverbodBeheerder, StopverbodStilstandBeheerder stopverbodStilstandBeheerder, TreinBeheerder treinBeheerder, GebruikersBeheerder gebruikersBeheerder, ActieBeheerder actieBeheerder, SnelheidsMetingBeheerder snelheidsMetingBeheerder, OnderhoudsBeheerder onderhoudsBeheerder, SnelleKlokBeheerder snelleKlokBeheerder)
    {
        try
        {
            Directory.CreateDirectory(BackupMap);
            ProjectOpslag.SlaOp(NieuwBackupPad(), blokBeheerder, baanBeheerder, wisselstraatBeheerder, routeBeheerder, treintypeBeheerder, richtingsverbodBeheerder, stopverbodBeheerder, stopverbodStilstandBeheerder, treinBeheerder, gebruikersBeheerder, actieBeheerder, snelheidsMetingBeheerder, onderhoudsBeheerder, snelleKlokBeheerder);

            foreach (var (pad, _) in AlleBackups().Skip(MaxBackups))
            {
                try { File.Delete(pad); }
                catch { /* een backup die niet opgeruimd kan worden is geen reden om te stoppen */ }
            }
        }
        catch
        {
            // Backup is puur een vangnet, geen kernfunctie - een schrijffout hier mag het
            // sluiten van het programma nooit verstoren.
        }
    }
}
