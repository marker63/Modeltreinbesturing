using System.Windows;
using Modeltreinbesturing.Model;

namespace Modeltreinbesturing;

public partial class GebruikersBeherenDialog : Window
{
    private readonly GebruikersBeheerder _gebruikersBeheerder;
    private readonly Gebruiker _huidigeGebruiker;

    public GebruikersBeherenDialog(GebruikersBeheerder gebruikersBeheerder, Gebruiker huidigeGebruiker)
    {
        InitializeComponent();
        _gebruikersBeheerder = gebruikersBeheerder;
        _huidigeGebruiker = huidigeGebruiker;
        VulLijst();
    }

    private void VulLijst()
    {
        GebruikersLijst.ItemsSource = null;
        GebruikersLijst.ItemsSource = _gebruikersBeheerder.Gebruikers.ToList();
    }

    private void NieuwAccount_Click(object sender, RoutedEventArgs e)
    {
        var dialoog = new NieuwAccountDialog(_gebruikersBeheerder, verplichtBeheerder: false) { Owner = this };
        if (dialoog.ShowDialog() == true) VulLijst();
    }

    private void Verwijderen_Click(object sender, RoutedEventArgs e)
    {
        if (GebruikersLijst.SelectedItem is not Gebruiker gebruiker) return;

        if (gebruiker == _huidigeGebruiker)
        {
            MessageBox.Show(this, "Je kunt je eigen account niet verwijderen terwijl je ermee bent ingelogd.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        // Nooit de laatste beheerder kunnen verwijderen - anders is er straks geen enkel
        // account meer dat nog een nieuw account kan aanmaken, en zit iedereen buiten de
        // bouwschermen.
        if (gebruiker.IsAdmin && _gebruikersBeheerder.Gebruikers.Count(g => g.IsAdmin) <= 1)
        {
            MessageBox.Show(this, "Dit is de laatste beheerder - er moet altijd minstens één beheerder overblijven.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var vraag = MessageBox.Show(this, $"Account '{gebruiker.Naam}' verwijderen?", "Modeltreinbesturing", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (vraag != MessageBoxResult.Yes) return;

        _gebruikersBeheerder.VerwijderGebruiker(gebruiker);
        VulLijst();
    }

    private void Sluiten_Click(object sender, RoutedEventArgs e) => Close();
}
