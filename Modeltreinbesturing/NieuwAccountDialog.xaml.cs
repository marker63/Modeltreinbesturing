using System.Windows;

namespace Modeltreinbesturing;

public partial class NieuwAccountDialog : Window
{
    private readonly GebruikersBeheerder _gebruikersBeheerder;
    private readonly bool _verplichtBeheerder;

    /// <summary>
    /// <param name="verplichtBeheerder">True voor de allereerste account van een nieuwe
    /// database - dan MOET het een beheerder zijn en is de beheerder-checkbox vast
    /// aangevinkt en uitgeschakeld (kan niet uitgezet worden).</param>
    /// </summary>
    public NieuwAccountDialog(GebruikersBeheerder gebruikersBeheerder, bool verplichtBeheerder)
    {
        InitializeComponent();
        _gebruikersBeheerder = gebruikersBeheerder;
        _verplichtBeheerder = verplichtBeheerder;

        if (_verplichtBeheerder)
        {
            Title = "Eerste beheerder aanmaken";
            UitlegTekst.Text = "Deze database heeft nog geen enkele beheerder. Maak hieronder de eerste (echte) beheerder aan - dit account kan later zelf weer andere accounts toevoegen. Het kijkscherm blijft ondertussen gewoon vrij toegankelijk, zonder wachtwoord.";
            IsAdminBox.IsChecked = true;
            IsAdminBox.IsEnabled = false;
        }
        else
        {
            UitlegTekst.Text = "Nieuw account voor toegang tot de bouwschermen (blokkenschema, baanontwerp, treinroutes).";
        }
    }

    private void Aanmaken_Click(object sender, RoutedEventArgs e)
    {
        string naam = NaamBox.Text.Trim();
        string wachtwoord = WachtwoordBox.Password;

        if (string.IsNullOrWhiteSpace(naam))
        {
            MessageBox.Show(this, "Vul een gebruikersnaam in.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (wachtwoord.Length < 4)
        {
            MessageBox.Show(this, "Kies een wachtwoord van minstens 4 tekens.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (wachtwoord != WachtwoordHerhaalBox.Password)
        {
            MessageBox.Show(this, "De twee wachtwoorden komen niet overeen.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        bool isAdmin = _verplichtBeheerder || IsAdminBox.IsChecked == true;
        if (!_gebruikersBeheerder.MaakGebruiker(naam, wachtwoord, isAdmin))
        {
            MessageBox.Show(this, $"Er bestaat al een account met de naam '{naam}'.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }
}
