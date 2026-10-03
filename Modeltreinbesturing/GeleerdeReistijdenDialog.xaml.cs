using System.Linq;
using System.Windows;
using Modeltreinbesturing.Model;

namespace Modeltreinbesturing;

public partial class GeleerdeReistijdenDialog : Window
{
    private readonly BlokBeheerder _blokBeheerder;
    private readonly TreinBeheerder _treinBeheerder;

    public GeleerdeReistijdenDialog(BlokBeheerder blokBeheerder, TreinBeheerder treinBeheerder)
    {
        InitializeComponent();
        _blokBeheerder = blokBeheerder;
        _treinBeheerder = treinBeheerder;
        VulLijst();
    }

    /// <summary>Zie GeleerdeReistijdBewerkenDialog voor de volledige achtergrond: doorbreekt
    /// de vicieuze cirkel waarbij een blok met een langer traject nooit een echte meting
    /// kan halen omdat de generieke veiligheidsdrempel de rit steeds voortijdig afbreekt.</summary>
    private void ToevoegenBewerken_Click(object sender, RoutedEventArgs e)
    {
        var dialoog = new GeleerdeReistijdBewerkenDialog(_blokBeheerder, _treinBeheerder) { Owner = this };
        if (dialoog.ShowDialog() == true) VulLijst();
    }

    private void VulLijst()
    {
        var regels = _blokBeheerder.Blokken
            .SelectMany(b => b.GeleerdeReistijden.Select(g => (Blok: b, Geleerd: g)))
            .OrderBy(x => x.Blok.Nummer)
            .Select(x => string.IsNullOrWhiteSpace(x.Blok.Omschrijving)
                ? $"Blok {x.Blok.Nummer} {x.Geleerd}"
                : $"Blok {x.Blok.Nummer} ({x.Blok.Omschrijving}) {x.Geleerd}")
            .ToList();
        if (regels.Count == 0)
            regels.Add("(nog geen enkele echte meting - elk blok gebruikt nu de generieke standaarddrempel)");
        ReistijdenLijst.ItemsSource = regels;
    }

    /// <summary>Zelfde bevestiging/gedrag als MainWindow.GeleerdeReistijdenWissen_Click - hier
    /// nogmaals aangeboden zodat je het overzicht en het wissen niet in twee aparte
    /// schermen hoeft te zoeken.</summary>
    private void AllesWissen_Click(object sender, RoutedEventArgs e)
    {
        int aantal = _blokBeheerder.Blokken.Sum(b => b.GeleerdeReistijden.Count);
        if (aantal == 0)
        {
            MessageBox.Show(this, "Er staan nog nergens geleerde reistijden geregistreerd.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show(this, $"Geleerde reistijden ({aantal} combinatie(s) van blok/richting/loc) wissen? De eerstvolgende rit naar zo'n blok gebruikt dan weer de ruime standaard-veiligheidsdrempel en leert vanaf daar opnieuw op.",
            "Modeltreinbesturing", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        foreach (var blok in _blokBeheerder.Blokken) blok.GeleerdeReistijden.Clear();
        VulLijst();
    }

    private void Sluiten_Click(object sender, RoutedEventArgs e) => Close();
}
