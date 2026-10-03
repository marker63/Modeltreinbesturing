using System.Windows;
using Modeltreinbesturing.Model;

namespace Modeltreinbesturing;

public partial class StopverbodDialog : Window
{
    private readonly BlokBeheerder _blokBeheerder;
    private readonly StopverbodBeheerder _stopverbodBeheerder;
    private readonly TreintypeBeheerder _treintypeBeheerder;

    public StopverbodDialog(BlokBeheerder blokBeheerder, StopverbodBeheerder stopverbodBeheerder, TreintypeBeheerder treintypeBeheerder)
    {
        InitializeComponent();
        _blokBeheerder = blokBeheerder;
        _stopverbodBeheerder = stopverbodBeheerder;
        _treintypeBeheerder = treintypeBeheerder;
        BlokCombo.ItemsSource = _blokBeheerder.Blokken;
        // Bewust GEEN "alle treintypes"-item in de lijst zelf - een lege selectie (niets
        // gekozen) betekent al gewoon "alle treintypes", dat is de standaardstand van een
        // ComboBox toch al.
        TreintypeCombo.ItemsSource = _treintypeBeheerder.Treintypes;
        VulLijst();
    }

    private void VulLijst()
    {
        VerbodenLijst.ItemsSource = null;
        VerbodenLijst.ItemsSource = _stopverbodBeheerder.Verboden;
    }

    private void Toevoegen_Click(object sender, RoutedEventArgs e)
    {
        if (BlokCombo.SelectedItem is not Blok blok)
        {
            MessageBox.Show(this, "Kies eerst een blok.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var treintype = TreintypeCombo.SelectedItem as Treintype;
        _stopverbodBeheerder.ToevoegenVerbod(blok, treintype);
        VulLijst();
    }

    private void TreintypeWissen_Click(object sender, RoutedEventArgs e) => TreintypeCombo.SelectedItem = null;

    private void Verwijderen_Click(object sender, RoutedEventArgs e)
    {
        if (VerbodenLijst.SelectedItem is not StopverbodItem verbod) return;
        _stopverbodBeheerder.VerwijderVerbod(verbod);
        VulLijst();
    }

    private void Sluiten_Click(object sender, RoutedEventArgs e) => Close();
}
