using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Modeltreinbesturing.Model;

namespace Modeltreinbesturing;

public partial class TreinDialog : Window
{
    private readonly TreinBeheerder _treinBeheerder;
    private readonly TreintypeBeheerder _treintypeBeheerder;
    private readonly SnelheidsMetingBeheerder _snelheidsMetingBeheerder;
    private string? _huidigeFotoPad;

    public TreinDialog(TreinBeheerder treinBeheerder, TreintypeBeheerder treintypeBeheerder, SnelheidsMetingBeheerder snelheidsMetingBeheerder)
    {
        InitializeComponent();
        _treinBeheerder = treinBeheerder;
        _treintypeBeheerder = treintypeBeheerder;
        _snelheidsMetingBeheerder = snelheidsMetingBeheerder;
        TreintypeCombo.ItemsSource = _treintypeBeheerder.Treintypes;
        FunctieGebeurtenisCombo.ItemsSource = Enum.GetValues<LocGebeurtenis>();
        FunctieGebeurtenisCombo.SelectedIndex = 0;

        void OpMetingStatus(string tekst) => Dispatcher.Invoke(() => MetingStatusTekst.Text = tekst);
        void OpMetingVoltooid(double kmPerUur) => Dispatcher.Invoke(() => LaatsteMetingKmPerUur = kmPerUur);
        _snelheidsMetingBeheerder.StatusBericht += OpMetingStatus;
        _snelheidsMetingBeheerder.MetingVoltooid += OpMetingVoltooid;
        Closed += (_, _) =>
        {
            _snelheidsMetingBeheerder.StatusBericht -= OpMetingStatus;
            _snelheidsMetingBeheerder.MetingVoltooid -= OpMetingVoltooid;
        };

        VulLijst();
    }

    /// <summary>Het resultaat van de laatst voltooide meting (km/u) - los bijgehouden zodat
    /// "Gebruik deze meting" 'm kan wegschrijven naar de stappentabel van de trein die op
    /// dat moment geselecteerd is (kan een ANDERE trein zijn dan toen de meting werd
    /// gestart, dus bewust pas bij het klikken op die knop toepassen, niet automatisch).</summary>
    private double? LaatsteMetingKmPerUur;

    private void VulLijst()
    {
        TreinenLijst.ItemsSource = null;
        TreinenLijst.ItemsSource = _treinBeheerder.Treinen;
    }

    private void VulFunctiesLijst(Trein trein)
    {
        FunctiesLijst.ItemsSource = null;
        FunctiesLijst.ItemsSource = trein.Functies;
    }

    /// <summary>Toont de cumulatieve rijtijd/afstand leesbaar (uren/minuten, meters) i.p.v.
    /// kale seconden/centimeters.</summary>
    private void VulRijstatistiekenTekst(Trein trein)
    {
        var tijd = TimeSpan.FromSeconds(trein.TotaleRijtijdSeconden);
        string tijdTekst = tijd.TotalHours >= 1 ? $"{(int)tijd.TotalHours}u {tijd.Minutes}m" : $"{tijd.Minutes}m {tijd.Seconds}s";
        string afstandTekst = trein.TotaleAfgelegdeAfstandCm >= 100 ? $"{trein.TotaleAfgelegdeAfstandCm / 100:0.0} m" : $"{trein.TotaleAfgelegdeAfstandCm:0} cm";
        RijstatistiekenTekst.Text = $"{tijdTekst} / {afstandTekst}";
    }

    private void RijstatistiekenResetten_Click(object sender, RoutedEventArgs e)
    {
        if (TreinenLijst.SelectedItem is not Trein trein)
        {
            MessageBox.Show(this, "Selecteer eerst een trein.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var vraag = MessageBox.Show(this, $"Rijtijd/afstand-teller van '{trein.Omschrijving}' terugzetten naar 0?", "Modeltreinbesturing", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (vraag != MessageBoxResult.Yes) return;
        trein.TotaleRijtijdSeconden = 0;
        trein.TotaleAfgelegdeAfstandCm = 0;
        VulRijstatistiekenTekst(trein);
    }

    private void VulStappentabelLijst(Trein trein)
    {
        StappentabelLijst.ItemsSource = null;
        StappentabelLijst.ItemsSource = trein.Stappentabel.OrderBy(s => s.Stap).Select(s => $"Stap {s.Stap}: {s.KmPerUur} km/u").ToList();
    }

    /// <summary>Voegt een stap toe, of wijzigt 'm als die stap al in de tabel voorkomt
    /// (net als een echte meting: bestaat de stap al, dan wordt de oude meting overschreven).</summary>
    private void StapToevoegen_Click(object sender, RoutedEventArgs e)
    {
        if (TreinenLijst.SelectedItem is not Trein trein)
        {
            MessageBox.Show(this, "Selecteer eerst een trein.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!int.TryParse(NieuweStapBox.Text, out int stap) || stap < 0 ||
            !double.TryParse(NieuweStapSnelheidBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double snelheid) || snelheid < 0)
        {
            MessageBox.Show(this, "Vul een geldige stap (0 of hoger) en snelheid (0 of hoger, in km/u) in.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var bestaandeRegel = trein.Stappentabel.FirstOrDefault(s => s.Stap == stap);
        if (bestaandeRegel != null) bestaandeRegel.KmPerUur = snelheid;
        else trein.Stappentabel.Add(new DecoderStapSnelheid { Stap = stap, KmPerUur = snelheid });
        VulStappentabelLijst(trein);
    }

    private void StapVerwijderen_Click(object sender, RoutedEventArgs e)
    {
        if (TreinenLijst.SelectedItem is not Trein trein) return;
        if (StappentabelLijst.SelectedIndex < 0) return;
        var geselecteerdeStap = trein.Stappentabel.OrderBy(s => s.Stap).ElementAt(StappentabelLijst.SelectedIndex);
        trein.Stappentabel.Remove(geselecteerdeStap);
        VulStappentabelLijst(trein);
    }

    /// <summary>Vult de hele stappentabel in één keer met een lineaire reeks van stap 0
    /// (0 km/u, "stilstaand") tot de opgegeven hoogste stap (op de opgegeven maximumsnelheid)
    /// - géén echte meting, puur een handig startpunt om vandaaruit met de hand bij te
    /// stellen. Overschrijft de HELE bestaande tabel.</summary>
    private void StappentabelGenereren_Click(object sender, RoutedEventArgs e)
    {
        if (TreinenLijst.SelectedItem is not Trein trein)
        {
            MessageBox.Show(this, "Selecteer eerst een trein.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!int.TryParse(GenereerHoogsteStapBox.Text, out int hoogsteStap) || hoogsteStap < 1 ||
            !double.TryParse(GenereerMaxSnelheidBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double maxSnelheid) || maxSnelheid <= 0)
        {
            MessageBox.Show(this, "Vul een geldige hoogste stap (minstens 1) en maximumsnelheid (groter dan 0) in.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var vraag = MessageBox.Show(this, "Dit vervangt de HELE huidige stappentabel van deze loc door een nieuwe, lineaire reeks. Doorgaan?", "Modeltreinbesturing", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (vraag != MessageBoxResult.Yes) return;

        trein.Stappentabel.Clear();
        for (int stap = 0; stap <= hoogsteStap; stap++)
            trein.Stappentabel.Add(new DecoderStapSnelheid { Stap = stap, KmPerUur = Math.Round(maxSnelheid * stap / hoogsteStap, 1) });
        VulStappentabelLijst(trein);
    }

    private void StartMeting_Click(object sender, RoutedEventArgs e) => _snelheidsMetingBeheerder.StartMeting();

    private void MetingNoodstop_Click(object sender, RoutedEventArgs e) => _snelheidsMetingBeheerder.Noodstop();

    /// <summary>Schrijft het resultaat van de laatst voltooide meting naar de stappentabel
    /// van de op DIT moment geselecteerde trein, op de opgegeven stap - bewust een aparte
    /// knop i.p.v. automatisch wegschrijven, zodat je eerst kunt controleren of de meting
    /// zinnig is voordat 'm de tabel in gaat.</summary>
    private void MetingGebruiken_Click(object sender, RoutedEventArgs e)
    {
        if (LaatsteMetingKmPerUur is not double kmPerUur)
        {
            MessageBox.Show(this, "Er is nog geen meting voltooid om te gebruiken.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (TreinenLijst.SelectedItem is not Trein trein)
        {
            MessageBox.Show(this, "Selecteer eerst de trein waarvoor deze meting gold.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!int.TryParse(MetingStapBox.Text, out int stap) || stap < 0)
        {
            MessageBox.Show(this, "Vul in voor welke stap deze meting gold (0 of hoger).", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var bestaandeRegel = trein.Stappentabel.FirstOrDefault(s => s.Stap == stap);
        if (bestaandeRegel != null) bestaandeRegel.KmPerUur = kmPerUur;
        else trein.Stappentabel.Add(new DecoderStapSnelheid { Stap = stap, KmPerUur = kmPerUur });
        VulStappentabelLijst(trein);
        MetingStatusTekst.Text = $"Stap {stap} van '{trein.Omschrijving}' ingesteld op {kmPerUur:0.0} km/u.";
    }

    private void TreinenLijst_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (TreinenLijst.SelectedItem is not Trein trein) return;
        OmschrijvingBox.Text = trein.Omschrijving;
        _huidigeFotoPad = trein.FotoPad;
        VulFotoPreview();
        DecoderAdresBox.Text = trein.DecoderAdres.ToString();
        foreach (ComboBoxItem item in DecoderStappenCombo.Items)
            if (item.Content?.ToString() == trein.DecoderStappen.ToString()) { DecoderStappenCombo.SelectedItem = item; break; }
        RangerenBox.IsChecked = trein.Rangeren;
        OmgekeerdeRijrichtingBox.IsChecked = trein.OmgekeerdeRijrichting;
        AccelaratieBox.Text = trein.AccelaratieVan0Naar50Seconden.ToString(CultureInfo.InvariantCulture);
        RemBox.Text = trein.RemVan50Naar0Seconden.ToString(CultureInfo.InvariantCulture);
        DraaischijfAfstandBox.Text = trein.DraaischijfAfstand.ToString(CultureInfo.InvariantCulture);
        TreintypeCombo.SelectedItem = trein.Treintype;
        LengteBox.Text = trein.Lengte.ToString(CultureInfo.InvariantCulture);
        IsTreinstelBox.IsChecked = trein.IsTreinstel;
        MagNietKerenBox.IsChecked = trein.MagNietKeren;
        GeijktBox.IsChecked = trein.GebruikGeijkteSnelheid;
        VulRijstatistiekenTekst(trein);
        OnderhoudDrempelBox.Text = trein.OnderhoudDrempelUren.ToString(CultureInfo.InvariantCulture);
        VulStappentabelLijst(trein);
        VulFunctiesLijst(trein);
        VulKnechtCombo(trein);
    }

    /// <summary>Dubbeltractie-keuzelijst: alle andere treinen (nooit zichzelf, en nooit een
    /// trein die AL ergens knecht van is - dat zou een verwarrende dubbele koppeling geven).</summary>
    private void VulKnechtCombo(Trein baas)
    {
        KnechtCombo.ItemsSource = _treinBeheerder.Treinen
            .Where(t => t != baas && !_treinBeheerder.Treinen.Any(b => b.GekoppeldeKnecht == t))
            .ToList();
        KnechtCombo.SelectedItem = baas.GekoppeldeKnecht;
    }

    private void Koppelen_Click(object sender, RoutedEventArgs e)
    {
        if (TreinenLijst.SelectedItem is not Trein baas)
        {
            MessageBox.Show(this, "Selecteer eerst een trein (de 'baas').", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (KnechtCombo.SelectedItem is not Trein knecht)
        {
            MessageBox.Show(this, "Kies een trein om als 'knecht' te koppelen.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        baas.GekoppeldeKnecht = knecht;
        MessageBox.Show(this, $"'{knecht.Omschrijving}' rijdt vanaf nu automatisch mee met '{baas.Omschrijving}' (dubbeltractie) - staat altijd op hetzelfde blok.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Information);
        VulLijst();
        TreinenLijst.SelectedItem = baas;
    }

    private void Ontkoppelen_Click(object sender, RoutedEventArgs e)
    {
        if (TreinenLijst.SelectedItem is not Trein baas || baas.GekoppeldeKnecht is null) return;
        baas.GekoppeldeKnecht = null;
        VulLijst();
        TreinenLijst.SelectedItem = baas;
    }

    private void FunctieToevoegen_Click(object sender, RoutedEventArgs e)
    {
        if (TreinenLijst.SelectedItem is not Trein trein)
        {
            MessageBox.Show(this, "Selecteer eerst een trein.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (string.IsNullOrWhiteSpace(FunctieNaamBox.Text) || FunctieGebeurtenisCombo.SelectedItem is not LocGebeurtenis gebeurtenis)
        {
            MessageBox.Show(this, "Vul een naam in en kies een moment.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        int functieNummer = int.TryParse(FunctieNummerBox.Text, out int fn) ? fn : -1;
        trein.Functies.Add(new LocFunctie
        {
            Naam = FunctieNaamBox.Text,
            Gebeurtenis = gebeurtenis,
            FunctieNummer = functieNummer,
            Aan = FunctieAanBox.IsChecked == true,
            GeluidsBestand = string.IsNullOrWhiteSpace(FunctieGeluidBox.Text) ? null : FunctieGeluidBox.Text
        });
        FunctieNaamBox.Text = "";
        FunctieNummerBox.Text = "";
        FunctieAanBox.IsChecked = true;
        FunctieGeluidBox.Text = "";
        VulFunctiesLijst(trein);
    }

    /// <summary>Zoals Koploper's eigen "afspelen van een geluidsbestand (*.wav) op de
    /// computer" - een optioneel .wav-bestand kiezen dat bij deze functie hoort.</summary>
    /// <summary>Laadt de foto van _huidigeFotoPad in de preview, of toont niets als er geen
    /// pad is (of het bestand niet meer bestaat/geladen kan worden - bijv. verplaatst).</summary>
    private void VulFotoPreview()
    {
        if (string.IsNullOrWhiteSpace(_huidigeFotoPad) || !System.IO.File.Exists(_huidigeFotoPad))
        {
            FotoPreview.Source = null;
            return;
        }
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(_huidigeFotoPad);
            bitmap.CacheOption = BitmapCacheOption.OnLoad; // meteen inladen, niet het bestand vasthouden
            bitmap.EndInit();
            FotoPreview.Source = bitmap;
        }
        catch
        {
            FotoPreview.Source = null;
        }
    }

    private void FotoKiezen_Click(object sender, RoutedEventArgs e)
    {
        var dialoog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Afbeeldingen (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp",
            Title = "Foto kiezen"
        };
        if (dialoog.ShowDialog(this) != true) return;
        _huidigeFotoPad = dialoog.FileName;
        VulFotoPreview();
    }

    private void FotoVerwijderen_Click(object sender, RoutedEventArgs e)
    {
        _huidigeFotoPad = null;
        VulFotoPreview();
    }

    private void FunctieGeluidKiezen_Click(object sender, RoutedEventArgs e)
    {
        var dialoog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Geluidsbestanden (*.wav)|*.wav",
            Title = "Geluidsbestand kiezen"
        };
        if (dialoog.ShowDialog(this) == true)
            FunctieGeluidBox.Text = dialoog.FileName;
    }

    private void FunctieVerwijderen_Click(object sender, RoutedEventArgs e)
    {
        if (TreinenLijst.SelectedItem is not Trein trein) return;
        if (FunctiesLijst.SelectedItem is not LocFunctie functie) return;
        trein.Functies.Remove(functie);
        VulFunctiesLijst(trein);
    }

    private void Opslaan_Click(object sender, RoutedEventArgs e)
    {
        if (TreinenLijst.SelectedItem is not Trein trein)
        {
            MessageBox.Show(this, "Selecteer eerst een trein.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!double.TryParse(LengteBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double lengte))
        {
            MessageBox.Show(this, "Vul een geldige lengte in (een getal, 0 = geen controle).", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        trein.Omschrijving = OmschrijvingBox.Text;
        trein.FotoPad = _huidigeFotoPad;
        trein.OnderhoudDrempelUren = double.TryParse(OnderhoudDrempelBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double drempel) ? drempel : 0;
        trein.DecoderAdres = int.TryParse(DecoderAdresBox.Text, out int decoderAdres) ? decoderAdres : 0;
        trein.DecoderStappen = DecoderStappenCombo.SelectedItem is ComboBoxItem { Content: string stappenTekst } && int.TryParse(stappenTekst, out int stappen) ? stappen : 28;
        trein.Rangeren = RangerenBox.IsChecked == true;
        trein.OmgekeerdeRijrichting = OmgekeerdeRijrichtingBox.IsChecked == true;
        trein.AccelaratieVan0Naar50Seconden = double.TryParse(AccelaratieBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double accel) ? accel : 0;
        trein.RemVan50Naar0Seconden = double.TryParse(RemBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double rem) ? rem : 0;
        trein.DraaischijfAfstand = double.TryParse(DraaischijfAfstandBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double draaischijf) ? draaischijf : 0;
        trein.Treintype = TreintypeCombo.SelectedItem as Treintype;
        trein.Lengte = lengte;
        trein.IsTreinstel = IsTreinstelBox.IsChecked == true;
        trein.MagNietKeren = MagNietKerenBox.IsChecked == true;
        trein.GebruikGeijkteSnelheid = GeijktBox.IsChecked == true;
        VulLijst();
        TreinenLijst.SelectedItem = trein;
    }

    private void Nieuw_Click(object sender, RoutedEventArgs e)
    {
        string? naam = InputDialog.Vraag(this, "Nieuwe trein", "Omschrijving:", "Nieuwe trein");
        if (string.IsNullOrWhiteSpace(naam)) return;
        var trein = _treinBeheerder.NieuweTrein(naam);
        VulLijst();
        TreinenLijst.SelectedItem = trein;
    }

    private void Verwijderen_Click(object sender, RoutedEventArgs e)
    {
        if (TreinenLijst.SelectedItem is not Trein trein) return;
        var vraag = MessageBox.Show(this, $"Trein '{trein.Omschrijving}' verwijderen?", "Trein verwijderen", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (vraag != MessageBoxResult.Yes) return;
        _treinBeheerder.VerwijderTrein(trein);
        VulLijst();
    }

    private void Sluiten_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>Kennisblad "Onderhouden Locomotieven in Koploper" beschrijft precies dit:
    /// treinen exporteren naar een los bestand en weer importeren, zodat je
    /// locomotief-gegevens tussen projecten kunt hergebruiken. We gebruiken bewust een
    /// eigen lichte exportvorm (niet de echte Trein-klasse met ReferenceHandler.Preserve)
    /// omdat een Trein via Treintype naar een object in DIT project verwijst - in een ANDER
    /// project bestaat dat object niet. Bij het exporteren wordt daarom alleen de NAAM van
    /// het treintype meegenomen, en bij het importeren wordt op naam gezocht naar een
    /// treintype in het HUIDIGE project (Koploper doet feitelijk hetzelfde: "Na het
    /// importeren dien je het juiste treintype weer in te stellen").</summary>
    private record TreinExport(string Omschrijving, string? TreintypeOmschrijving, int DecoderAdres, int DecoderStappen,
        bool Rangeren, double DraaischijfAfstand, double Lengte, bool IsTreinstel,
        double AccelaratieVan0Naar50Seconden, double RemVan50Naar0Seconden,
        List<LocFunctie> Functies, bool OmgekeerdeRijrichting = false);

    /// <summary>Een leesbaar, administratief overzicht van het wagenpark - géén back-up-
    /// formaat zoals de .kctreinen-export hierboven (die is bedoeld om weer terug in te
    /// lezen), puur om te bekijken/printen/delen in een spreadsheetprogramma.</summary>
    private void MaterieellijstExporteren_Click(object sender, RoutedEventArgs e)
    {
        if (_treinBeheerder.Treinen.Count == 0)
        {
            MessageBox.Show(this, "Er zijn nog geen treinen om te exporteren.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dialoog = new Microsoft.Win32.SaveFileDialog { Filter = "CSV-bestand (*.csv)|*.csv", FileName = "materieellijst.csv" };
        if (dialoog.ShowDialog(this) != true) return;

        string CsvVeld(string tekst) => $"\"{tekst.Replace("\"", "\"\"")}\"";
        var regels = new List<string> { string.Join(";", new[] { "Omschrijving", "Treintype", "Decoderadres", "Lengte (cm)", "Treinstel", "Totale rijtijd", "Totale afstand" }.Select(CsvVeld)) };
        foreach (var trein in _treinBeheerder.Treinen)
        {
            var tijd = TimeSpan.FromSeconds(trein.TotaleRijtijdSeconden);
            string tijdTekst = tijd.TotalHours >= 1 ? $"{(int)tijd.TotalHours}u {tijd.Minutes}m" : $"{tijd.Minutes}m {tijd.Seconds}s";
            string afstandTekst = trein.TotaleAfgelegdeAfstandCm >= 100 ? $"{trein.TotaleAfgelegdeAfstandCm / 100:0.0} m" : $"{trein.TotaleAfgelegdeAfstandCm:0} cm";
            regels.Add(string.Join(";", new[]
            {
                trein.Omschrijving, trein.Treintype?.Omschrijving ?? "", trein.DecoderAdres.ToString(),
                trein.Lengte.ToString(CultureInfo.InvariantCulture), trein.IsTreinstel ? "Ja" : "Nee", tijdTekst, afstandTekst
            }.Select(CsvVeld)));
        }
        try
        {
            System.IO.File.WriteAllLines(dialoog.FileName, regels, System.Text.Encoding.UTF8);
            MessageBox.Show(this, $"{_treinBeheerder.Treinen.Count} trein(en) geëxporteerd naar {dialoog.FileName}.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Exporteren mislukt: {ex.Message}", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Exporteren_Click(object sender, RoutedEventArgs e)
    {
        if (_treinBeheerder.Treinen.Count == 0)
        {
            MessageBox.Show(this, "Er zijn nog geen treinen om te exporteren.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dialoog = new Microsoft.Win32.SaveFileDialog { Filter = "Modeltreinbesturing locomotieven (*.kctreinen)|*.kctreinen", FileName = "locomotieven.kctreinen" };
        if (dialoog.ShowDialog(this) != true) return;

        var export = _treinBeheerder.Treinen.Select(t => new TreinExport(
            t.Omschrijving, t.Treintype?.Omschrijving, t.DecoderAdres, t.DecoderStappen,
            t.Rangeren, t.DraaischijfAfstand, t.Lengte, t.IsTreinstel,
            t.AccelaratieVan0Naar50Seconden, t.RemVan50Naar0Seconden, t.Functies, t.OmgekeerdeRijrichting)).ToList();
        try
        {
            System.IO.File.WriteAllText(dialoog.FileName, System.Text.Json.JsonSerializer.Serialize(export, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            MessageBox.Show(this, $"{export.Count} trein(en) geëxporteerd naar {dialoog.FileName}.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Exporteren mislukt: {ex.Message}", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Importeren_Click(object sender, RoutedEventArgs e)
    {
        var dialoog = new Microsoft.Win32.OpenFileDialog { Filter = "Modeltreinbesturing locomotieven (*.kctreinen)|*.kctreinen" };
        if (dialoog.ShowDialog(this) != true) return;

        List<TreinExport>? import;
        try
        {
            import = System.Text.Json.JsonSerializer.Deserialize<List<TreinExport>>(System.IO.File.ReadAllText(dialoog.FileName));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Importeren mislukt: {ex.Message}", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        if (import is null || import.Count == 0)
        {
            MessageBox.Show(this, "Geen treinen gevonden in dit bestand.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        int nietGekoppeld = 0;
        foreach (var item in import)
        {
            var treintype = item.TreintypeOmschrijving != null
                ? _treintypeBeheerder.Treintypes.FirstOrDefault(t => t.Omschrijving == item.TreintypeOmschrijving)
                : null;
            if (item.TreintypeOmschrijving != null && treintype is null) nietGekoppeld++;

            _treinBeheerder.Treinen.Add(new Trein
            {
                Omschrijving = item.Omschrijving,
                Treintype = treintype,
                DecoderAdres = item.DecoderAdres,
                DecoderStappen = item.DecoderStappen,
                Rangeren = item.Rangeren,
                DraaischijfAfstand = item.DraaischijfAfstand,
                Lengte = item.Lengte,
                IsTreinstel = item.IsTreinstel,
                AccelaratieVan0Naar50Seconden = item.AccelaratieVan0Naar50Seconden,
                RemVan50Naar0Seconden = item.RemVan50Naar0Seconden,
                Functies = item.Functies,
                OmgekeerdeRijrichting = item.OmgekeerdeRijrichting
            });
        }
        VulLijst();
        string melding = $"{import.Count} trein(en) geïmporteerd.";
        if (nietGekoppeld > 0) melding += $" Let op: bij {nietGekoppeld} trein(en) is het treintype uit het bronbestand hier niet gevonden - stel dat handmatig opnieuw in.";
        MessageBox.Show(this, melding, "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
