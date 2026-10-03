using System.Windows;
using System.Windows.Media.Imaging;
using Modeltreinbesturing.Model;

namespace Modeltreinbesturing;

public partial class OnderhoudsDialog : Window
{
    private readonly OnderhoudsBeheerder _onderhoudsBeheerder;
    private string? _gekozenFotoPad;

    public OnderhoudsDialog(OnderhoudsBeheerder onderhoudsBeheerder, string? vooringevuldObject = null)
    {
        InitializeComponent();
        _onderhoudsBeheerder = onderhoudsBeheerder;
        SoortCombo.ItemsSource = Enum.GetValues<OnderhoudsSoort>();
        SoortCombo.SelectedIndex = 0;
        VulLijst();
        if (!string.IsNullOrWhiteSpace(vooringevuldObject))
        {
            ObjectBox.Text = vooringevuldObject;
            FilterBox.Text = vooringevuldObject; // toont meteen de bestaande geschiedenis van dit object, indien aanwezig
            TitelBox.Focus();
        }
    }

    /// <summary>Laadt de foto van _gekozenFotoPad in de preview, of toont niets als er geen
    /// pad is (of het bestand niet meer bestaat/geladen kan worden - bijv. verplaatst).</summary>
    private void VulFotoPreview()
    {
        if (string.IsNullOrWhiteSpace(_gekozenFotoPad) || !System.IO.File.Exists(_gekozenFotoPad))
        {
            FotoPreview.Source = null;
            return;
        }
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(_gekozenFotoPad);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
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
        _gekozenFotoPad = dialoog.FileName;
        VulFotoPreview();
    }

    private void FotoVerwijderen_Click(object sender, RoutedEventArgs e)
    {
        _gekozenFotoPad = null;
        VulFotoPreview();
    }

    private void VulLijst()
    {
        var filter = FilterBox.Text;
        RecordsLijst.ItemsSource = null;
        RecordsLijst.ItemsSource = string.IsNullOrWhiteSpace(filter)
            ? _onderhoudsBeheerder.Records.OrderByDescending(r => r.Datum).ToList()
            : _onderhoudsBeheerder.VoorObject(filter).ToList();
    }

    private void FilterBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => VulLijst();

    private void Toevoegen_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ObjectBox.Text) || string.IsNullOrWhiteSpace(TitelBox.Text))
        {
            MessageBox.Show(this, "Vul zowel een object als een titel in.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var record = new OnderhoudsRecord
        {
            Soort = SoortCombo.SelectedItem is OnderhoudsSoort s ? s : OnderhoudsSoort.Incident,
            Object = ObjectBox.Text.Trim(),
            Titel = TitelBox.Text.Trim(),
            Omschrijving = OmschrijvingBox.Text.Trim(),
            Afgerond = AfgerondBox.IsChecked == true,
            Datum = DateTime.Now,
            FotoPad = _gekozenFotoPad
        };
        _onderhoudsBeheerder.Records.Add(record);
        TitelBox.Clear();
        OmschrijvingBox.Clear();
        AfgerondBox.IsChecked = false;
        _gekozenFotoPad = null;
        VulFotoPreview();
        VulLijst();
        RecordsLijst.SelectedItem = record;
    }

    private void AfgerondOmschakelen_Click(object sender, RoutedEventArgs e)
    {
        if (RecordsLijst.SelectedItem is not OnderhoudsRecord record)
        {
            MessageBox.Show(this, "Selecteer eerst een registratie.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        record.Afgerond = !record.Afgerond;
        VulLijst();
        RecordsLijst.SelectedItem = record;
    }

    private void Verwijderen_Click(object sender, RoutedEventArgs e)
    {
        if (RecordsLijst.SelectedItem is not OnderhoudsRecord record) return;
        _onderhoudsBeheerder.VerwijderRecord(record);
        VulLijst();
    }

    /// <summary>Bij selectie de velden vullen met de gekozen registratie, zodat je 'm kunt
    /// bekijken (de omschrijving past vaak niet volledig in de lijst-regel zelf) - bewust
    /// GEEN "wijzigen"-knop: een onderhoudslog hoort append-only te blijven, aanpassen kan
    /// door 'm te verwijderen en opnieuw toe te voegen.</summary>
    private void RecordsLijst_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (RecordsLijst.SelectedItem is not OnderhoudsRecord record) return;
        SoortCombo.SelectedItem = record.Soort;
        ObjectBox.Text = record.Object;
        TitelBox.Text = record.Titel;
        OmschrijvingBox.Text = record.Omschrijving;
        AfgerondBox.IsChecked = record.Afgerond;
        _gekozenFotoPad = record.FotoPad;
        VulFotoPreview();
    }

    private void Sluiten_Click(object sender, RoutedEventArgs e) => Close();
}
