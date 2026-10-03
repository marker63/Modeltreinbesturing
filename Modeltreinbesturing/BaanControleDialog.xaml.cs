using System.Windows;

namespace Modeltreinbesturing;

public partial class BaanControleDialog : Window
{
    public BaanControleDialog(BlokBeheerder blokBeheerder, BaanOntwerpBeheerder baanBeheerder, TreinrouteBeheerder routeBeheerder)
    {
        InitializeComponent();
        var meldingen = new BaanControleBeheerder().VoerControleUit(blokBeheerder, baanBeheerder, routeBeheerder);

        SamenvattingTekst.Text = meldingen.Count == 0
            ? "Geen bijzonderheden gevonden."
            : $"{meldingen.Count} punt(en) gevonden om naar te kijken.";
        MeldingenLijst.ItemsSource = meldingen.Select(m => $"[{m.Ernst}] {m.Omschrijving}").ToList();
    }

    private void Sluiten_Click(object sender, RoutedEventArgs e) => Close();
}
