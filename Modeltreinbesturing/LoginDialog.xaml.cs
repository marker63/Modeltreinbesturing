using System.Windows;
using System.Windows.Input;
using Modeltreinbesturing.Model;

namespace Modeltreinbesturing;

public partial class LoginDialog : Window
{
    private readonly GebruikersBeheerder _gebruikersBeheerder;

    public Gebruiker? IngelogdeGebruiker { get; private set; }

    public LoginDialog(GebruikersBeheerder gebruikersBeheerder)
    {
        InitializeComponent();
        _gebruikersBeheerder = gebruikersBeheerder;
    }

    private void WachtwoordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Inloggen_Click(sender, new RoutedEventArgs());
    }

    /// <summary>Marco 10-11: uitleg wat je kunt doen als je het wachtwoord vergeten bent (het wachtwoord zelf is nergens terug te halen, alleen een hash wordt bewaard).</summary>
    private void Vergeten_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(this,
            "Een vergeten wachtwoord is niet terug te halen: het programma bewaart alleen een versleutelde controlewaarde, nooit het wachtwoord zelf.\n\n"
            + "Wat wel kan: in het kijkscherm, menu Account -> 'Nieuwe baan beginnen (met nieuwe beheerder)'. Dat kan zonder inloggen; het opgeslagen bestand van de huidige baan "
            + "blijft op schijf staan. Weet je wel het wachtwoord van een ander beheerdersaccount, log daarmee in en wijzig het via Beheren -> Gebruikers beheren.",
            "Wachtwoord vergeten", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Inloggen_Click(object sender, RoutedEventArgs e)
    {
        var gebruiker = _gebruikersBeheerder.Verifieer(NaamBox.Text.Trim(), WachtwoordBox.Password);
        if (gebruiker is null)
        {
            MessageBox.Show(this, "Onjuiste gebruikersnaam of wachtwoord.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Warning);
            WachtwoordBox.Clear();
            WachtwoordBox.Focus();
            return;
        }
        IngelogdeGebruiker = gebruiker;
        DialogResult = true;
    }
}
