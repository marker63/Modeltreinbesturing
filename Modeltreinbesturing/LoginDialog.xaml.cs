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
