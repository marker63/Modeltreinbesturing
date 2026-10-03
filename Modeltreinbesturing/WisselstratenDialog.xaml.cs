using System.Windows;
using System.Windows.Input;
using Modeltreinbesturing.Model;

namespace Modeltreinbesturing;

public partial class WisselstratenDialog : Window
{
    private readonly WisselstraatBeheerder _wisselstraatBeheerder;
    private readonly Action _naVerwijderen;

    public WisselstratenDialog(WisselstraatBeheerder wisselstraatBeheerder, Action naVerwijderen)
    {
        InitializeComponent();
        _wisselstraatBeheerder = wisselstraatBeheerder;
        _naVerwijderen = naVerwijderen;
        VulLijst();
    }

    private void VulLijst()
    {
        WisselstratenLijst.ItemsSource = null;
        WisselstratenLijst.ItemsSource = _wisselstraatBeheerder.Wisselstraten;
        VulWisselsLijst();
    }

    private void VulWisselsLijst()
    {
        WisselsLijst.ItemsSource = null;
        if (WisselstratenLijst.SelectedItem is not Wisselstraat geselecteerd) return;

        if (geselecteerd.Wissels.Count == 0)
        {
            WisselsLijst.Items.Clear();
            WisselsLijst.Items.Add("(nog geen wissels — voeg ze toe via de Wisselstraat-tool in het baanontwerp: klik dit blokpaar opnieuw aan, klik daarna de wissel zelf op de tekening)");
        }
        else
        {
            WisselsLijst.ItemsSource = geselecteerd.Wissels;
        }
    }

    private void WisselstratenLijst_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => VulWisselsLijst();

    private void WisselsLijst_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (WisselsLijst.SelectedItem is WisselInWisselstraat wisselInfo)
        {
            wisselInfo.GewensteStand = wisselInfo.GewensteStand == WisselStand.Rechtdoor ? WisselStand.Afbuigend : WisselStand.Rechtdoor;
            VulWisselsLijst();
            _naVerwijderen(); // hergebruikt als algemene "er is iets gewijzigd, herteken"-callback
        }
    }

    private void Activeren_Click(object sender, RoutedEventArgs e)
    {
        if (WisselstratenLijst.SelectedItem is not Wisselstraat geselecteerd)
        {
            MessageBox.Show(this, "Selecteer eerst een wisselstraat.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (geselecteerd.Wissels.Count == 0)
        {
            MessageBox.Show(this, "Deze wisselstraat heeft nog geen wissels om te zetten.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        foreach (var wisselInfo in geselecteerd.Wissels)
            wisselInfo.Wissel.Stand = wisselInfo.GewensteStand;

        _naVerwijderen(); // hergebruikt als algemene "er is iets gewijzigd, herteken"-callback
        MessageBox.Show(this,
            $"Wisselstraat {geselecteerd} geactiveerd — {geselecteerd.Wissels.Count} wissel(s) gezet.",
            "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Kopieren_Click(object sender, RoutedEventArgs e)
    {
        if (WisselstratenLijst.SelectedItem is not Wisselstraat bron)
        {
            MessageBox.Show(this, "Selecteer eerst een wisselstraat om te kopiëren.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var kopie = _wisselstraatBeheerder.KopieerWisselstraat(bron);
        VulLijst();
        WisselstratenLijst.SelectedItem = kopie;
        MessageBox.Show(this,
            $"Nieuwe wisselstraat aangemaakt: {kopie}. Pas de gewenste standen aan door in de lijst hieronder op de betreffende wissel te dubbelklikken.",
            "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Verwijderen_Click(object sender, RoutedEventArgs e)
    {
        if (WisselstratenLijst.SelectedItem is Wisselstraat geselecteerd)
        {
            var vraag = MessageBox.Show(this, $"Wisselstraat '{geselecteerd}' verwijderen? Dit kan niet ongedaan gemaakt worden.",
                "Wisselstraat verwijderen", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (vraag != MessageBoxResult.Yes) return;

            _wisselstraatBeheerder.VerwijderWisselstraat(geselecteerd);
            VulLijst();
            _naVerwijderen();
        }
    }

    private void DubbelenOpruimen_Click(object sender, RoutedEventArgs e)
    {
        var groepen = _wisselstraatBeheerder.Wisselstraten
            .GroupBy(w => (w.Van, w.Naar))
            .Where(g => g.Count() > 1)
            .ToList();

        if (groepen.Count == 0)
        {
            MessageBox.Show(this, "Geen dubbele wisselstraten gevonden.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        int totaalTeVerwijderen = groepen.Sum(g => g.Count() - 1);
        var bevestiging = MessageBox.Show(this,
            $"Er zijn {groepen.Count} blokpaar(en) met meerdere wisselstraten. Per paar wordt de meest complete (de meeste wissels/lijnen) behouden en worden er {totaalTeVerwijderen} overbodige verwijderd. Doorgaan?",
            "Dubbele wisselstraten opruimen", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (bevestiging != MessageBoxResult.Yes) return;

        int verwijderd = _wisselstraatBeheerder.VerwijderDubbelen();
        VulLijst();
        _naVerwijderen();
        MessageBox.Show(this, $"{verwijderd} dubbele wisselstraat(en) verwijderd.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Sluiten_Click(object sender, RoutedEventArgs e) => Close();
}
