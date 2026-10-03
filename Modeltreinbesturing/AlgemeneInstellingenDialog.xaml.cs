using System.Globalization;
using System.Linq;
using System.Windows;
using Modeltreinbesturing.Model;

namespace Modeltreinbesturing;

public partial class AlgemeneInstellingenDialog : Window
{
    private readonly BlokBeheerder _blokBeheerder;
    private readonly SnelheidsMetingBeheerder _snelheidsMetingBeheerder;

    public AlgemeneInstellingenDialog(BlokBeheerder blokBeheerder, SnelheidsMetingBeheerder snelheidsMetingBeheerder)
    {
        InitializeComponent();
        _blokBeheerder = blokBeheerder;
        _snelheidsMetingBeheerder = snelheidsMetingBeheerder;

        StandaardUitrolBox.Text = SimulatieInstellingen.StandaardUitrolSeconden.ToString(CultureInfo.InvariantCulture);
        WisselRustpauzeBox.Text = SimulatieInstellingen.WisselRustpauzeMilliseconden.ToString(CultureInfo.InvariantCulture);
        GeluidenIngeschakeldBox.IsChecked = SimulatieInstellingen.GeluidenIngeschakeld;

        var alleMelders = _blokBeheerder.Blokken.SelectMany(b => b.Bezetmeldpunten).Select(m => m.MeldernNummer).Distinct().OrderBy(n => n).ToList();
        Melder1Combo.ItemsSource = alleMelders;
        Melder2Combo.ItemsSource = alleMelders;
        Melder1Combo.SelectedItem = _snelheidsMetingBeheerder.Instellingen.MeldernNummer1 > 0 ? _snelheidsMetingBeheerder.Instellingen.MeldernNummer1 : null;
        Melder2Combo.SelectedItem = _snelheidsMetingBeheerder.Instellingen.MeldernNummer2 > 0 ? _snelheidsMetingBeheerder.Instellingen.MeldernNummer2 : null;
        LengteTrajectBox.Text = _snelheidsMetingBeheerder.Instellingen.LengteTrajectMm.ToString(CultureInfo.InvariantCulture);
        ModelschaalBox.Text = _snelheidsMetingBeheerder.Instellingen.Modelschaal.ToString(CultureInfo.InvariantCulture);
        ManierVanMetenCombo.ItemsSource = Enum.GetValues<ManierVanMeten>();
        ManierVanMetenCombo.SelectedItem = _snelheidsMetingBeheerder.Instellingen.ManierVanMeten;
    }

    private void Opslaan_Click(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(StandaardUitrolBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double uitrol) || uitrol < 0)
        {
            MessageBox.Show(this, "Vul een geldige uitroltijd in (0 of hoger).", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!double.TryParse(WisselRustpauzeBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double rustpauze) || rustpauze < 0)
        {
            MessageBox.Show(this, "Vul een geldige rustpauze in (0 of hoger, in milliseconden).", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Snelheidsmeting-velden zijn optioneel om al meteen compleet in te vullen (je kunt
        // dit later nog aanvullen) - alleen valideren als er iets is ingevuld dat niet klopt.
        bool heeftMelder1 = Melder1Combo.SelectedItem is int;
        bool heeftMelder2 = Melder2Combo.SelectedItem is int;
        if (heeftMelder1 != heeftMelder2 || (heeftMelder1 && heeftMelder2 && (int)Melder1Combo.SelectedItem! == (int)Melder2Combo.SelectedItem!))
        {
            MessageBox.Show(this, "Kies óf geen van beide melders, óf twee VERSCHILLENDE melders.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!double.TryParse(LengteTrajectBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double lengte) || lengte < 0 ||
            !double.TryParse(ModelschaalBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double schaal) || schaal <= 0)
        {
            MessageBox.Show(this, "Vul een geldige trajectlengte (mm, 0 of hoger) en modelschaal (groter dan 0) in.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SimulatieInstellingen.StandaardUitrolSeconden = uitrol;
        SimulatieInstellingen.WisselRustpauzeMilliseconden = rustpauze;
        SimulatieInstellingen.GeluidenIngeschakeld = GeluidenIngeschakeldBox.IsChecked == true;

        _snelheidsMetingBeheerder.Instellingen.MeldernNummer1 = heeftMelder1 ? (int)Melder1Combo.SelectedItem! : 0;
        _snelheidsMetingBeheerder.Instellingen.MeldernNummer2 = heeftMelder2 ? (int)Melder2Combo.SelectedItem! : 0;
        _snelheidsMetingBeheerder.Instellingen.LengteTrajectMm = lengte;
        _snelheidsMetingBeheerder.Instellingen.Modelschaal = schaal;
        _snelheidsMetingBeheerder.Instellingen.ManierVanMeten = ManierVanMetenCombo.SelectedItem is ManierVanMeten mvm ? mvm : ManierVanMeten.HeenEnWeer;

        // Zachte waarschuwing, geen blokkade - echte baanindelingen kunnen best afwijken.
        // Bevestigd via twee onafhankelijke Koploper-bronnen: "voor het ijken/meten van
        // snelheden is een stuk spoor van een vaste lengte nodig (tussen 50 en 100 cm)" -
        // een preciezere, betere bevestigde waarde dan de eerdere (afgekapte) schatting.
        string trajectWaarschuwing = lengte > 0 && (lengte < 500 || lengte > 1000)
            ? "\n\nLet op: een trajectlengte van 50 tot 100 cm (500-1000 mm) wordt meestal aangeraden voor een betrouwbare meting - de huidige waarde wijkt daarvan af."
            : "";
        MessageBox.Show(this, $"Instellingen opgeslagen.{trajectWaarschuwing}", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Sluiten_Click(object sender, RoutedEventArgs e) => Close();
}
