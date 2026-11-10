using System.Windows;
using Modeltreinbesturing.Model;

namespace Modeltreinbesturing;

/// <summary>
/// WAT: venster om het wachtwoord van één account te wijzigen.
/// WAAROM (Marco 10-11): er was geen menu om een wachtwoord te wijzigen. Voor je eigen account vraagt het eerst het huidige wachtwoord;
/// een beheerder die het wachtwoord van een ander account opnieuw instelt (Gebruikers beheren) hoeft dat niet te weten.
/// </summary>
public partial class WachtwoordWijzigenDialog : Window
{
    private readonly GebruikersBeheerder _gebruikersBeheerder;
    private readonly Gebruiker _gebruiker;
    private readonly bool _huidigVragen;

    public WachtwoordWijzigenDialog(GebruikersBeheerder gebruikersBeheerder, Gebruiker gebruiker, bool huidigVragen = true)
    {
        InitializeComponent();
        _gebruikersBeheerder = gebruikersBeheerder;
        _gebruiker = gebruiker;
        _huidigVragen = huidigVragen;
        UitlegTekst.Text = huidigVragen
            ? $"Nieuw wachtwoord voor je eigen account '{gebruiker.Naam}'."
            : $"Nieuw wachtwoord instellen voor account '{gebruiker.Naam}' (het oude wachtwoord is daarvoor niet nodig).";
        if (!huidigVragen) HuidigPaneel.Visibility = Visibility.Collapsed;
    }

    private void Wijzigen_Click(object sender, RoutedEventArgs e)
    {
        if (_huidigVragen && _gebruikersBeheerder.Verifieer(_gebruiker.Naam, HuidigBox.Password) is null)
        {
            MessageBox.Show(this, "Het huidige wachtwoord klopt niet.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Warning);
            HuidigBox.Clear();
            HuidigBox.Focus();
            return;
        }
        if (NieuwBox.Password.Length < 4)
        {
            MessageBox.Show(this, "Kies een wachtwoord van minstens 4 tekens.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (NieuwBox.Password != HerhaalBox.Password)
        {
            MessageBox.Show(this, "De twee nieuwe wachtwoorden komen niet overeen.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _gebruikersBeheerder.WijzigWachtwoord(_gebruiker, NieuwBox.Password);
        DialogResult = true;
    }
}
