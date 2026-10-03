using System.Windows;
using Modeltreinbesturing.Model;

namespace Modeltreinbesturing;

public partial class StopverbodStilstandDialog : Window
{
    private readonly BlokBeheerder _blokBeheerder;
    private readonly StopverbodStilstandBeheerder _beheerder;
    private readonly TreintypeBeheerder _treintypeBeheerder;

    public StopverbodStilstandDialog(BlokBeheerder blokBeheerder, StopverbodStilstandBeheerder beheerder, TreintypeBeheerder treintypeBeheerder)
    {
        InitializeComponent();
        _blokBeheerder = blokBeheerder;
        _beheerder = beheerder;
        _treintypeBeheerder = treintypeBeheerder;

        VanCombo.ItemsSource = _blokBeheerder.Blokken;
        NaarCombo.ItemsSource = _blokBeheerder.Blokken;
        UitCombo.ItemsSource = _blokBeheerder.Blokken;
        TreintypeCombo.ItemsSource = _treintypeBeheerder.Treintypes;
        VulLijst();
    }

    private void VulLijst()
    {
        VerbodenLijst.ItemsSource = null;
        VerbodenLijst.ItemsSource = _beheerder.Verboden;
    }

    private void TreintypeWissen_Click(object sender, RoutedEventArgs e) => TreintypeCombo.SelectedItem = null;

    private void Toevoegen_Click(object sender, RoutedEventArgs e)
    {
        if (VanCombo.SelectedItem is not Blok van || NaarCombo.SelectedItem is not Blok naar)
        {
            MessageBox.Show(this, "Kies zowel een 'Van'- als een 'Naar'-blok.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var uit = UitCombo.SelectedItem as Blok;
        var treintype = TreintypeCombo.SelectedItem as Treintype;
        _beheerder.NieuwVerbod(van, naar, uit, treintype);
        VulLijst();
    }

    private void Verwijderen_Click(object sender, RoutedEventArgs e)
    {
        if (VerbodenLijst.SelectedItem is not Richtingsverbod verbod) return;
        _beheerder.VerwijderVerbod(verbod);
        VulLijst();
    }

    private void Sluiten_Click(object sender, RoutedEventArgs e) => Close();
}
