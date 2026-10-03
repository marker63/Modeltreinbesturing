using System.Globalization;
using System.Linq;
using System.Windows;
using Modeltreinbesturing.Model;

namespace Modeltreinbesturing;

/// <summary>Gebruikersverzoek: doorbreekt een vicieuze cirkel die tijdens het testen aan het
/// licht kwam - een blok met een langer traject (bijv. tot het uiteinde van een pendelspoor)
/// kan de generieke 30-seconden-veiligheidsdrempel nooit halen, wordt daardoor STEEDS als
/// "vastgelopen" afgebroken vóórdat de echte aankomst ooit bevestigd wordt - en OMDAT een
/// vastgelopen-stop bewust NIET leert (dat zou de meting verpesten), kan zo'n blok NOOIT
/// vanzelf een correcte tijd leren. Dit venster laat je een REDELIJKE, eigen inschatting
/// vooraf invullen, zodat de eerste ECHTE poging genoeg tijd krijgt.</summary>
public partial class GeleerdeReistijdBewerkenDialog : Window
{
    private readonly BlokBeheerder _blokBeheerder;

    public GeleerdeReistijdBewerkenDialog(BlokBeheerder blokBeheerder, TreinBeheerder treinBeheerder)
    {
        InitializeComponent();
        _blokBeheerder = blokBeheerder;
        NaarBlokCombo.ItemsSource = _blokBeheerder.Blokken.OrderBy(b => b.Nummer).ToList();
        VanBlokCombo.ItemsSource = _blokBeheerder.Blokken.OrderBy(b => b.Nummer).ToList();
        LocCombo.ItemsSource = treinBeheerder.Treinen.OrderBy(t => t.Omschrijving).ToList();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (NaarBlokCombo.SelectedItem is not Blok naar || VanBlokCombo.SelectedItem is not Blok van || LocCombo.SelectedItem is not Trein trein)
        {
            MessageBox.Show(this, "Kies een naar-blok, een vanuit-blok en een loc.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (naar == van)
        {
            MessageBox.Show(this, "'Naar blok' en 'vanuit blok' moeten verschillend zijn.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!double.TryParse(SecondenBox.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double seconden) || seconden <= 0)
        {
            MessageBox.Show(this, "Vul een geldig aantal seconden (groter dan 0) in.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var bestaand = naar.GeleerdeReistijden.FirstOrDefault(g => g.VanBlok == van && g.Trein == trein);
        if (bestaand != null) bestaand.Seconden = seconden;
        else naar.GeleerdeReistijden.Add(new GeleerdeReistijd { VanBlok = van, Trein = trein, Seconden = seconden });

        DialogResult = true;
    }
}
