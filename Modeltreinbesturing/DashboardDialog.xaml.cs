using System.Windows;
using Modeltreinbesturing.Model;

namespace Modeltreinbesturing;

public partial class DashboardDialog : Window
{
    private string _volledigRapport = "";

    public DashboardDialog(BlokBeheerder blokBeheerder, BaanOntwerpBeheerder baanBeheerder, TreinBeheerder treinBeheerder,
        TreinrouteBeheerder routeBeheerder, OnderhoudsBeheerder onderhoudsBeheerder, TreinrouteWindow? treinrouteWindow)
    {
        InitializeComponent();

        var symbolen = baanBeheerder.Symbolen;
        int aantalWissels = symbolen.OfType<Wissel>().Count();
        int aantalSeinen = symbolen.OfType<Sein>().Count();
        BaanoverzichtTekst.Text =
            $"Blokken: {blokBeheerder.Blokken.Count}\n" +
            $"Wissels: {aantalWissels}\n" +
            $"Seinen: {aantalSeinen}\n" +
            $"Vastgelegde treinroutes: {routeBeheerder.Treinroutes.Count}\n" +
            $"Locomotieven: {treinBeheerder.Treinen.Count}";

        double totaleRijtijd = treinBeheerder.Treinen.Sum(t => t.TotaleRijtijdSeconden);
        double totaleAfstand = treinBeheerder.Treinen.Sum(t => t.TotaleAfgelegdeAfstandCm);
        var tijd = TimeSpan.FromSeconds(totaleRijtijd);
        string tijdTekst = tijd.TotalHours >= 1 ? $"{(int)tijd.TotalHours}u {tijd.Minutes}m" : $"{tijd.Minutes}m {tijd.Seconds}s";
        string afstandTekst = totaleAfstand >= 100 ? $"{totaleAfstand / 100:0.0} m" : $"{totaleAfstand:0} cm";
        var meesteRijtijd = treinBeheerder.Treinen.OrderByDescending(t => t.TotaleRijtijdSeconden).FirstOrDefault(t => t.TotaleRijtijdSeconden > 0);
        RijstatistiekenTekst.Text =
            $"Totale rijtijd: {tijdTekst}\n" +
            $"Totale afgelegde afstand: {afstandTekst}" +
            (meesteRijtijd != null ? $"\nMeeste rijtijd: '{meesteRijtijd.Omschrijving}'" : "");

        int aantalIncidentenOpen = onderhoudsBeheerder.Records.Count(r => r.Soort == OnderhoudsSoort.Incident && !r.Afgerond);
        int aantalIncidentenTotaal = onderhoudsBeheerder.Records.Count(r => r.Soort == OnderhoudsSoort.Incident);
        int aantalWijzigingen = onderhoudsBeheerder.Records.Count(r => r.Soort == OnderhoudsSoort.Wijziging);
        OnderhoudTekst.Text =
            $"Open incidenten: {aantalIncidentenOpen} (van {aantalIncidentenTotaal} totaal)\n" +
            $"Geregistreerde wijzigingen: {aantalWijzigingen}";

        // Aandachtspunten: objecten met 3 of meer incidenten (open of gesloten) zijn
        // mogelijk een chronisch probleem, geen losse pech - een simpele, maar bruikbare
        // signalering die de losse incidentenlijst zelf niet direct laat zien.
        var chronisch = onderhoudsBeheerder.Records
            .Where(r => r.Soort == OnderhoudsSoort.Incident)
            .GroupBy(r => r.Object)
            .Where(g => g.Count() >= 3)
            .Select(g => $"- {g.Key}: {g.Count()} incidenten")
            .ToList();
        AandachtspuntenTekst.Text = chronisch.Count > 0
            ? "Objecten met 3 of meer gemelde incidenten (mogelijk een chronisch probleem, geen losse pech):\n" + string.Join("\n", chronisch)
            : "Geen opvallende patronen gevonden.";

        // Drempel van 10 minuten: lang genoeg om normale wachttijden (bijv. bij een
        // station of wisselstraat-vertraging) niet als vals alarm te tonen, kort genoeg om
        // een écht vastgelopen of vergeten trein toch tijdig te signaleren.
        if (treinrouteWindow == null)
        {
            StilstaandeTreinenTekst.Text = "(kan vanuit dit scherm niet gecontroleerd worden - open het Dashboard via Beheren in het blokkenschema voor deze informatie)";
        }
        else
        {
            var stilstaand = treinrouteWindow.LangStilstaandeTreinen(TimeSpan.FromMinutes(10)).ToList();
            StilstaandeTreinenTekst.Text = stilstaand.Count == 0
                ? "Geen treinen die langer dan 10 minuten geen voortgang boeken."
                : string.Join("\n", stilstaand.Select(s => $"- '{(s.Trein?.Omschrijving ?? "onbekende loc")}' op route '{s.RouteOmschrijving}': {(int)s.StilVoor.TotalMinutes} minuten geen voortgang"));
        }

        // Onderhoud nodig: locs waarvan de ingestelde OnderhoudDrempelUren (0 = geen
        // drempel ingesteld, zie Treinen beheren) overschreden is.
        var onderhoudNodig = treinBeheerder.Treinen
            .Where(t => t.OnderhoudDrempelUren > 0 && t.TotaleRijtijdSeconden / 3600.0 >= t.OnderhoudDrempelUren)
            .Select(t => $"- '{t.Omschrijving}': {t.TotaleRijtijdSeconden / 3600.0:0.0}u gereden (drempel: {t.OnderhoudDrempelUren:0.0}u)")
            .ToList();
        OnderhoudNodigTekst.Text = onderhoudNodig.Count == 0
            ? "Geen enkele loc heeft zijn onderhoudsdrempel bereikt (of er is nog geen drempel ingesteld bij Treinen beheren)."
            : string.Join("\n", onderhoudNodig);

        _volledigRapport =
            $"Modeltreinbesturing - Dashboard\nGegenereerd: {DateTime.Now:dd-MM-yyyy HH:mm}\n{new string('=', 40)}\n\n" +
            $"Baanoverzicht\n{new string('-', 20)}\n{BaanoverzichtTekst.Text}\n\n" +
            $"Rijstatistieken (alle locs samen)\n{new string('-', 20)}\n{RijstatistiekenTekst.Text}\n\n" +
            $"Incidenten en wijzigingen\n{new string('-', 20)}\n{OnderhoudTekst.Text}\n\n" +
            $"Aandachtspunten\n{new string('-', 20)}\n{AandachtspuntenTekst.Text}\n\n" +
            $"Mogelijk vastgelopen/vergeten treinen\n{new string('-', 20)}\n{StilstaandeTreinenTekst.Text}\n\n" +
            $"Locs met onderhoud nodig\n{new string('-', 20)}\n{OnderhoudNodigTekst.Text}\n";
    }

    private void Exporteren_Click(object sender, RoutedEventArgs e)
    {
        var dialoog = new Microsoft.Win32.SaveFileDialog { Filter = "Tekstbestand (*.txt)|*.txt", FileName = "dashboard.txt" };
        if (dialoog.ShowDialog(this) != true) return;
        try
        {
            System.IO.File.WriteAllText(dialoog.FileName, _volledigRapport, System.Text.Encoding.UTF8);
            MessageBox.Show(this, $"Dashboard geëxporteerd naar {dialoog.FileName}.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Exporteren mislukt: {ex.Message}", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Sluiten_Click(object sender, RoutedEventArgs e) => Close();
}
