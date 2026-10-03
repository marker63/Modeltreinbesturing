using System.Windows;
using Modeltreinbesturing.Model;

namespace Modeltreinbesturing;

public partial class RelatiesDialog : Window
{
    private readonly BlokBeheerder _blokBeheerder;

    public RelatiesDialog(BlokBeheerder blokBeheerder)
    {
        InitializeComponent();
        _blokBeheerder = blokBeheerder;
        VulLijst();
    }

    private void VulLijst()
    {
        RelatiesLijst.ItemsSource = null;
        RelatiesLijst.ItemsSource = _blokBeheerder.Relaties;
    }

    private void KeerOmschakelen_Click(object sender, RoutedEventArgs e)
    {
        if (RelatiesLijst.SelectedItem is not BlokRelatie relatie)
        {
            MessageBox.Show(this, "Selecteer eerst een relatie in de lijst.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        relatie.Keer = !relatie.Keer;
        VulLijst();
        RelatiesLijst.SelectedItem = relatie;
    }

    /// <summary>Koploper's "Kans"-kolom (Richtingen-tabblad) - zie Model.BlokRelatie.Kans
    /// voor de volledige achtergrond. Simpele invoerdialoog i.p.v. een inline-editable
    /// kolom, zelfde stijl als de rest van dit venster (één actie, één knop).</summary>
    private void KansWijzigen_Click(object sender, RoutedEventArgs e)
    {
        if (RelatiesLijst.SelectedItem is not BlokRelatie relatie)
        {
            MessageBox.Show(this, "Selecteer eerst een relatie in de lijst.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        string? invoer = InputDialog.Vraag(this, "Modeltreinbesturing - Kans wijzigen",
            $"Kans-gewicht voor {relatie.Van.Nummer} -> {relatie.Naar.Nummer} (1 = standaard, hoger = vaker gekozen bij vrij automatisch rijden):",
            relatie.Kans.ToString());
        if (string.IsNullOrWhiteSpace(invoer)) return;
        if (!int.TryParse(invoer, out int nieuweKans) || nieuweKans < 1)
        {
            MessageBox.Show(this, "Vul een geheel getal van 1 of hoger in.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        relatie.Kans = nieuweKans;
        VulLijst();
        RelatiesLijst.SelectedItem = relatie;
    }

    private void Sluiten_Click(object sender, RoutedEventArgs e) => Close();
}
