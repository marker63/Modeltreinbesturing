using System.Windows;
using Modeltreinbesturing.Model;

namespace Modeltreinbesturing;

public partial class ActiesDialog : Window
{
    private readonly BlokBeheerder _blokBeheerder;
    private readonly BaanOntwerpBeheerder _baanBeheerder;
    private readonly ActieBeheerder _actieBeheerder;

    public ActiesDialog(BlokBeheerder blokBeheerder, BaanOntwerpBeheerder baanBeheerder, ActieBeheerder actieBeheerder)
    {
        InitializeComponent();
        _blokBeheerder = blokBeheerder;
        _baanBeheerder = baanBeheerder;
        _actieBeheerder = actieBeheerder;

        BlokCombo.ItemsSource = _blokBeheerder.Blokken;
        GebeurtenisCombo.ItemsSource = Enum.GetValues<BlokActieGebeurtenis>();
        GebeurtenisCombo.SelectedIndex = 0;
        SchakelaarCombo.ItemsSource = _baanBeheerder.Symbolen.OfType<Schakelaar>().ToList();
        StandCombo.ItemsSource = new[] { "Aan", "Uit" };
        StandCombo.SelectedIndex = 0;
        VulLijst();
    }

    private void VulLijst()
    {
        ActiesLijst.ItemsSource = null;
        ActiesLijst.ItemsSource = _actieBeheerder.Acties;
    }

    private void Toevoegen_Click(object sender, RoutedEventArgs e)
    {
        if (BlokCombo.SelectedItem is not Blok blok)
        {
            MessageBox.Show(this, "Kies eerst een blok.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (SchakelaarCombo.SelectedItem is not Schakelaar schakelaar)
        {
            MessageBox.Show(this, "Kies een schakelaar - zet er eerst een neer in het baanontwerp als de lijst leeg is.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var gebeurtenis = (BlokActieGebeurtenis)GebeurtenisCombo.SelectedItem;
        bool nieuweStand = StandCombo.SelectedItem as string == "Aan";
        double.TryParse(PulsSecondenBox.Text, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double pulsSeconden);
        _actieBeheerder.NieuweActie(blok, gebeurtenis, schakelaar, nieuweStand, Math.Max(0, pulsSeconden));
        VulLijst();
    }

    private void Verwijderen_Click(object sender, RoutedEventArgs e)
    {
        if (ActiesLijst.SelectedItem is not BlokActie actie) return;
        _actieBeheerder.VerwijderActie(actie);
        VulLijst();
    }

    private void Sluiten_Click(object sender, RoutedEventArgs e) => Close();
}
