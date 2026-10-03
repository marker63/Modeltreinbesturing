using System.Windows;
using System.Windows.Controls;
using Modeltreinbesturing.Model;

namespace Modeltreinbesturing;

public partial class BlokgroepenDialog : Window
{
    private readonly BlokgroepBeheerder _blokgroepBeheerder;
    private readonly BlokBeheerder _blokBeheerder;

    public BlokgroepenDialog(BlokgroepBeheerder blokgroepBeheerder, BlokBeheerder blokBeheerder)
    {
        InitializeComponent();
        _blokgroepBeheerder = blokgroepBeheerder;
        _blokBeheerder = blokBeheerder;
        BlokCombo.ItemsSource = _blokBeheerder.Blokken;
        VulGroepenLijst();
    }

    private void VulGroepenLijst()
    {
        GroepenLijst.ItemsSource = null;
        GroepenLijst.ItemsSource = _blokgroepBeheerder.Blokgroepen;
    }

    private void VulBlokkenLijst(Blokgroep groep)
    {
        BlokkenLijst.Items.Clear();
        foreach (var blok in groep.Blokken)
            BlokkenLijst.Items.Add(blok);
    }

    private void GroepenLijst_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GroepenLijst.SelectedItem is not Blokgroep groep)
        {
            NaamBox.Text = "";
            BlokkenLijst.Items.Clear();
            return;
        }
        NaamBox.Text = groep.Naam;
        EnkeleTreinbewegingBox.IsChecked = groep.EnkeleTreinbeweging;
        GecombineerdeStopmelderBox.IsChecked = groep.GecombineerdeStopmelder;
        VulBlokkenLijst(groep);
    }

    private void NieuweGroep_Click(object sender, RoutedEventArgs e)
    {
        var groep = new Blokgroep { Naam = $"Blokgroep {_blokgroepBeheerder.Blokgroepen.Count + 1}" };
        _blokgroepBeheerder.Blokgroepen.Add(groep);
        VulGroepenLijst();
        GroepenLijst.SelectedItem = groep;
    }

    private void VerwijderGroep_Click(object sender, RoutedEventArgs e)
    {
        if (GroepenLijst.SelectedItem is not Blokgroep groep) return;
        _blokgroepBeheerder.Blokgroepen.Remove(groep);
        VulGroepenLijst();
    }

    private void NaamBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (GroepenLijst.SelectedItem is not Blokgroep groep) return;
        groep.Naam = NaamBox.Text;
        int index = GroepenLijst.SelectedIndex;
        VulGroepenLijst();
        GroepenLijst.SelectedIndex = index;
    }

    private void Opties_Changed(object sender, RoutedEventArgs e)
    {
        if (GroepenLijst.SelectedItem is not Blokgroep groep) return;
        groep.EnkeleTreinbeweging = EnkeleTreinbewegingBox.IsChecked == true;
        groep.GecombineerdeStopmelder = GecombineerdeStopmelderBox.IsChecked == true;
    }

    private void BlokToevoegen_Click(object sender, RoutedEventArgs e)
    {
        if (GroepenLijst.SelectedItem is not Blokgroep groep)
        {
            MessageBox.Show(this, "Selecteer eerst een groep.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (BlokCombo.SelectedItem is not Blok blok)
        {
            MessageBox.Show(this, "Kies eerst een blok.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (_blokgroepBeheerder.GroepVan(blok) != null)
        {
            MessageBox.Show(this, $"Blok {blok.Nummer} zit al in een andere groep.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        groep.Blokken.Add(blok);
        VulBlokkenLijst(groep);
    }

    private void BlokVerwijderen_Click(object sender, RoutedEventArgs e)
    {
        if (GroepenLijst.SelectedItem is not Blokgroep groep) return;
        if (BlokkenLijst.SelectedItem is not Blok blok) return;
        groep.Blokken.Remove(blok);
        VulBlokkenLijst(groep);
    }

    private void Sluiten_Click(object sender, RoutedEventArgs e) => Close();
}
