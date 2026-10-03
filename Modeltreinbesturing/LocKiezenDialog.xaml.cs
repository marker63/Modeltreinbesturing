using System.Windows;
using Modeltreinbesturing.Model;

namespace Modeltreinbesturing;

public partial class LocKiezenDialog : Window
{
    public Trein? GekozenTrein { get; private set; }

    public LocKiezenDialog(TreinBeheerder treinBeheerder, Blok blok)
    {
        InitializeComponent();
        OmschrijvingTekst.Text = $"Welke trein plaats je op blok {blok.Nummer}?";
        TreinCombo.ItemsSource = treinBeheerder.Treinen;
        if (treinBeheerder.Treinen.Count > 0) TreinCombo.SelectedIndex = 0;
    }

    private void Plaatsen_Click(object sender, RoutedEventArgs e)
    {
        if (TreinCombo.SelectedItem is not Trein trein)
        {
            MessageBox.Show(this, "Kies eerst een trein (of maak er een aan via 'Treinen beheren').", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        GekozenTrein = trein;
        DialogResult = true;
    }

    private void Annuleren_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
