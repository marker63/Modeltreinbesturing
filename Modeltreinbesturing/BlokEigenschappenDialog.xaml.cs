using System.Collections.ObjectModel;
using System.Windows;
using Modeltreinbesturing.Model;

namespace Modeltreinbesturing;

public partial class BlokEigenschappenDialog : Window
{
    private readonly Blok _blok;
    private readonly BlokBeheerder _blokBeheerder;
    private readonly ObservableCollection<Bezetmeldpunt> _meldpunten;
    private readonly ObservableCollection<RichtingsBezetmelding> _richtingsMeldingen;

    public BlokEigenschappenDialog(Blok blok, BlokBeheerder blokBeheerder)
    {
        InitializeComponent();
        _blok = blok;
        _blokBeheerder = blokBeheerder;

        Title = $"Eigenschappen blok {blok.Nummer}";
        NummerBox.Text = blok.Nummer.ToString();
        OmschrijvingBox.Text = blok.Omschrijving;

        TypeCombo.ItemsSource = Enum.GetValues<BlokType>();
        TypeCombo.SelectedItem = blok.Type;

        MaxTreinlengteBox.Text = blok.MaxTreinlengte.ToString(System.Globalization.CultureInfo.InvariantCulture);
        MaxSnelheidBox.Text = blok.MaxSnelheid.ToString(System.Globalization.CultureInfo.InvariantCulture);
        UitrolSecondenBox.Text = blok.UitrolSecondenOverride.ToString(System.Globalization.CultureInfo.InvariantCulture);
        LengteCmBox.Text = blok.LengteCm.ToString(System.Globalization.CultureInfo.InvariantCulture);
        VergrendeldBox.IsChecked = blok.Vergrendeld;
        VergrendelRedenBox.Text = blok.VergrendelReden ?? "";

        MagNietStoppenBox.IsChecked = blok.TeLangeTreinActie.HasFlag(TeLangeTreinActie.MagNietStoppen);
        VorigBlokLangerBezetBox.IsChecked = blok.TeLangeTreinActie.HasFlag(TeLangeTreinActie.VorigBlokLangerBezetHouden);
        WisselstraatLangerBezetBox.IsChecked = blok.TeLangeTreinActie.HasFlag(TeLangeTreinActie.WisselstraatLangerBezetHouden);

        _meldpunten = new ObservableCollection<Bezetmeldpunt>(blok.Bezetmeldpunten);
        MeldpuntenLijst.ItemsSource = _meldpunten;

        RolCombo.ItemsSource = Enum.GetValues<BezetmeldpuntRol>();
        RolCombo.SelectedIndex = 0;

        _richtingsMeldingen = new ObservableCollection<RichtingsBezetmelding>(blok.RichtingsBezetmeldingen);
        RichtingsMeldingenLijst.ItemsSource = _richtingsMeldingen;
        VanBlokCombo.ItemsSource = _blokBeheerder.Blokken.Where(b => b != _blok).OrderBy(b => b.Nummer).ToList();
    }

    private void Toevoegen_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(MeldernummerBox.Text, out int meldernummer))
        {
            MessageBox.Show(this, "Vul een geldig meldernummer in (een getal).", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var rol = (BezetmeldpuntRol)(RolCombo.SelectedItem ?? BezetmeldpuntRol.Voorblok);
        double.TryParse(MaxTreinlengteMelderBox.Text, out double maxTreinlengte);
        _meldpunten.Add(new Bezetmeldpunt { MeldernNummer = meldernummer, Rol = rol, MaxTreinlengte = maxTreinlengte, MagVorigBlokVrijgeven = MagVorigBlokVrijgevenBox.IsChecked != false });
        MeldernummerBox.Clear();
        MaxTreinlengteMelderBox.Clear();
        MagVorigBlokVrijgevenBox.IsChecked = true;
        MeldernummerBox.Focus();
    }

    private void Verwijderen_Click(object sender, RoutedEventArgs e)
    {
        if (MeldpuntenLijst.SelectedItem is Bezetmeldpunt geselecteerd)
            _meldpunten.Remove(geselecteerd);
    }

    /// <summary>Koploper's "Bezetmeldingen"-tabblad - zie Model.RichtingsBezetmelding voor
    /// de volledige achtergrond. "Toevoegen/bijwerken": is er al een entry voor dit
    /// "Uit blok", dan wordt die vervangen (er kan er maar één per bronblok zijn) i.p.v.
    /// een dubbele toe te voegen.</summary>
    private void RichtingsMeldingToevoegen_Click(object sender, RoutedEventArgs e)
    {
        if (VanBlokCombo.SelectedItem is not Blok vanBlok)
        {
            MessageBox.Show(this, "Kies eerst uit welk blok deze aankomstrichting komt.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var stukken = MeldernummersVolgordeBox.Text.Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
        var nummers = new List<int>();
        foreach (var stuk in stukken)
        {
            if (!int.TryParse(stuk, out int nummer))
            {
                MessageBox.Show(this, $"'{stuk}' is geen geldig meldernummer. Vul meldernummers in op volgorde, gescheiden door spaties (bijv. '1 9 10').", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            nummers.Add(nummer);
        }
        if (nummers.Count == 0)
        {
            MessageBox.Show(this, "Vul minimaal één meldernummer in.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var bestaand = _richtingsMeldingen.FirstOrDefault(r => r.VanBlok == vanBlok);
        if (bestaand != null) _richtingsMeldingen.Remove(bestaand); // bijwerken = de oude vervangen
        _richtingsMeldingen.Add(new RichtingsBezetmelding { VanBlok = vanBlok, MeldernNummers = nummers });
        MeldernummersVolgordeBox.Clear();
    }

    private void RichtingsMeldingVerwijderen_Click(object sender, RoutedEventArgs e)
    {
        if (RichtingsMeldingenLijst.SelectedItem is RichtingsBezetmelding geselecteerd)
            _richtingsMeldingen.Remove(geselecteerd);
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(NummerBox.Text, out int nieuwNummer))
        {
            MessageBox.Show(this, "Vul een geldig bloknummer in (een getal).", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (nieuwNummer != _blok.Nummer && _blokBeheerder.Blokken.Any(b => b != _blok && b.Nummer == nieuwNummer))
        {
            MessageBox.Show(this, $"Bloknummer {nieuwNummer} is al in gebruik door een ander blok.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!double.TryParse(MaxTreinlengteBox.Text, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double maxTreinlengte))
        {
            MessageBox.Show(this, "Vul een geldige max treinlengte in (een getal, 0 = onbeperkt).", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!double.TryParse(MaxSnelheidBox.Text, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double maxSnelheid))
        {
            MessageBox.Show(this, "Vul een geldige max snelheid in (een getal, 0 = geen eigen limiet).", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!double.TryParse(UitrolSecondenBox.Text, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double uitrolSeconden))
        {
            MessageBox.Show(this, "Vul een geldige uitroltijd in (een getal, -1 = gebruik de standaardwaarde).", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!double.TryParse(LengteCmBox.Text, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double lengteCm) || lengteCm < 0)
        {
            MessageBox.Show(this, "Vul een geldige bloklengte in (0 of hoger, in cm).", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _blok.Nummer = nieuwNummer;
        _blok.Omschrijving = OmschrijvingBox.Text;
        _blok.Type = (BlokType)(TypeCombo.SelectedItem ?? BlokType.Normaal);
        _blok.MaxTreinlengte = maxTreinlengte;
        _blok.MaxSnelheid = maxSnelheid;
        _blok.UitrolSecondenOverride = uitrolSeconden;
        _blok.LengteCm = lengteCm;
        _blok.Vergrendeld = VergrendeldBox.IsChecked == true;
        _blok.VergrendelReden = string.IsNullOrWhiteSpace(VergrendelRedenBox.Text) ? null : VergrendelRedenBox.Text.Trim();
        var actie = TeLangeTreinActie.GeenActie;
        if (MagNietStoppenBox.IsChecked == true) actie |= TeLangeTreinActie.MagNietStoppen;
        if (VorigBlokLangerBezetBox.IsChecked == true) actie |= TeLangeTreinActie.VorigBlokLangerBezetHouden;
        if (WisselstraatLangerBezetBox.IsChecked == true) actie |= TeLangeTreinActie.WisselstraatLangerBezetHouden;
        _blok.TeLangeTreinActie = actie;
        _blok.Bezetmeldpunten = _meldpunten.ToList();
        _blok.RichtingsBezetmeldingen = _richtingsMeldingen.ToList();
        DialogResult = true;
    }

    private void Annuleren_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
