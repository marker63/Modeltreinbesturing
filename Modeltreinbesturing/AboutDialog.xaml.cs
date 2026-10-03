using System.Reflection;
using System.Windows;

namespace Modeltreinbesturing;

public partial class AboutDialog : Window
{
    /// <summary>Pas dit aan zodra de repository daadwerkelijk online staat - een duidelijke
    /// placeholder is eerlijker dan een verzonnen link die nergens naartoe leidt.</summary>
    private const string RepositoryUrl = "https://github.com/<gebruikersnaam>/Modeltreinbesturing";

    public AboutDialog()
    {
        InitializeComponent();
        var versie = Assembly.GetExecutingAssembly().GetName().Version;
        VersieTekst.Text = versie != null ? $"Versie {versie.Major}.{versie.Minor}.{versie.Build}" : "Versie onbekend";
        RepositoryUrlBox.Text = RepositoryUrl;
    }

    private void Sluiten_Click(object sender, RoutedEventArgs e) => Close();
}
