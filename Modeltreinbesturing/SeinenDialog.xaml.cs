using System.Windows;
using System.Windows.Controls;
using Modeltreinbesturing.Model;

namespace Modeltreinbesturing;

public partial class SeinenDialog : Window
{
    private readonly BlokBeheerder _blokBeheerder;
    private readonly BaanOntwerpBeheerder _baanBeheerder;
    private readonly WisselstraatBeheerder _wisselstraatBeheerder;
    private readonly Action _herteken;

    private List<Sein> _seinenVolgorde = new();
    private bool _bezigMetSynchroniseren;

    public SeinenDialog(BlokBeheerder blokBeheerder, BaanOntwerpBeheerder baanBeheerder, WisselstraatBeheerder wisselstraatBeheerder, Action herteken)
    {
        InitializeComponent();
        _blokBeheerder = blokBeheerder;
        _baanBeheerder = baanBeheerder;
        _wisselstraatBeheerder = wisselstraatBeheerder;
        _herteken = herteken;

        BlokCombo.ItemsSource = _blokBeheerder.Blokken;
        WisselstraatCombo.ItemsSource = _wisselstraatBeheerder.Wisselstraten;
        TypeCombo.ItemsSource = Enum.GetValues<SeinType>();
        VulLijst();
    }

    private void VulLijst()
    {
        _seinenVolgorde = _baanBeheerder.Symbolen.OfType<Sein>().OrderBy(s => s.Adres).ToList();

        SeinenLijst.Items.Clear();
        foreach (var sein in _seinenVolgorde)
        {
            var aspect = SeinLogica.BepaalAspect(sein, _blokBeheerder, _wisselstraatBeheerder, _baanBeheerder.Symbolen.OfType<Sein>());
            string aspectTekst = aspect switch
            {
                SeinAspect.Rood => "rood",
                SeinAspect.Geel => "geel",
                SeinAspect.Groen => "groen",
                _ => "handmatig (geen koppeling)"
            };
            string gekoppeld = sein.IsRangeersein && sein.GekoppeldeWisselstraat != null
                ? $"wisselstraat {sein.GekoppeldeWisselstraat} (rangeersein)"
                : sein.GekoppeldBlok != null ? $"blok {sein.GekoppeldBlok.Nummer}" : "-";
            SeinenLijst.Items.Add($"Sein {sein.Adres} ({sein.Type}, tabblad {sein.Tabblad}) — gekoppeld aan: {gekoppeld} — seinbeeld: {aspectTekst}");
        }
    }

    private void SeinenLijst_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SeinenLijst.SelectedIndex >= 0 && SeinenLijst.SelectedIndex < _seinenVolgorde.Count)
        {
            var sein = _seinenVolgorde[SeinenLijst.SelectedIndex];
            _bezigMetSynchroniseren = true;
            BlokCombo.SelectedItem = sein.GekoppeldBlok;
            WisselstraatCombo.SelectedItem = sein.GekoppeldeWisselstraat;
            TypeCombo.SelectedItem = sein.Type;
            _bezigMetSynchroniseren = false;
        }
    }

    /// <summary>Type sein wijzigen (2-standen/3-standen/dwergsein/voorsein) - direct
    /// toegepast, geen aparte 'toepassen'-knop nodig. De guard voorkomt dat dit ook afgaat
    /// als TypeCombo alleen maar programmatisch bijgewerkt wordt vanuit
    /// SeinenLijst_SelectionChanged (anders zou elke sein-selectie het type van het NET
    /// geselecteerde sein overschrijven met wat er toevallig nog in de combo stond).</summary>
    private void Type_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_bezigMetSynchroniseren) return;
        if (SeinenLijst.SelectedIndex < 0 || SeinenLijst.SelectedIndex >= _seinenVolgorde.Count) return;
        if (TypeCombo.SelectedItem is not SeinType type) return;

        _seinenVolgorde[SeinenLijst.SelectedIndex].Type = type;
        int geselecteerdeIndex = SeinenLijst.SelectedIndex;
        VulLijst();
        SeinenLijst.SelectedIndex = geselecteerdeIndex;
        _herteken();
    }

    private void Koppelen_Click(object sender, RoutedEventArgs e)
    {
        if (SeinenLijst.SelectedIndex < 0 || SeinenLijst.SelectedIndex >= _seinenVolgorde.Count)
        {
            MessageBox.Show(this, "Selecteer eerst een sein.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (BlokCombo.SelectedItem is not Blok blok)
        {
            MessageBox.Show(this, "Kies eerst een blok om aan te koppelen.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        int geselecteerdeIndex = SeinenLijst.SelectedIndex;
        _seinenVolgorde[geselecteerdeIndex].GekoppeldBlok = blok;
        _seinenVolgorde[geselecteerdeIndex].IsRangeersein = false;
        _seinenVolgorde[geselecteerdeIndex].GekoppeldeWisselstraat = null;
        VulLijst();
        SeinenLijst.SelectedIndex = geselecteerdeIndex;
        _herteken();
    }

    /// <summary>"Rangeersein": een sein aan een hele WISSELSTRAAT koppelen i.p.v. een blok -
    /// zoals in het echte Koploper vaak vóór een wisselstraat met meerdere vertreksporen,
    /// waar één sein voor alle sporen tegelijk geldt. Zie SeinLogica.BepaalAspect voor de
    /// bijbehorende regel (groen bij ingestelde rijweg, rood bij eerste bezetmelding erna).</summary>
    private void KoppelenAanWisselstraat_Click(object sender, RoutedEventArgs e)
    {
        if (SeinenLijst.SelectedIndex < 0 || SeinenLijst.SelectedIndex >= _seinenVolgorde.Count)
        {
            MessageBox.Show(this, "Selecteer eerst een sein.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (WisselstraatCombo.SelectedItem is not Wisselstraat wisselstraat)
        {
            MessageBox.Show(this, "Kies eerst een wisselstraat om aan te koppelen.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        int geselecteerdeIndex = SeinenLijst.SelectedIndex;
        _seinenVolgorde[geselecteerdeIndex].GekoppeldeWisselstraat = wisselstraat;
        _seinenVolgorde[geselecteerdeIndex].IsRangeersein = true;
        _seinenVolgorde[geselecteerdeIndex].GekoppeldBlok = null;
        VulLijst();
        SeinenLijst.SelectedIndex = geselecteerdeIndex;
        _herteken();
    }

    private void Ontkoppelen_Click(object sender, RoutedEventArgs e)
    {
        if (SeinenLijst.SelectedIndex < 0 || SeinenLijst.SelectedIndex >= _seinenVolgorde.Count) return;

        int geselecteerdeIndex = SeinenLijst.SelectedIndex;
        _seinenVolgorde[geselecteerdeIndex].GekoppeldBlok = null;
        _seinenVolgorde[geselecteerdeIndex].IsRangeersein = false;
        _seinenVolgorde[geselecteerdeIndex].GekoppeldeWisselstraat = null;
        VulLijst();
        SeinenLijst.SelectedIndex = geselecteerdeIndex;
        _herteken();
    }

    private void Sluiten_Click(object sender, RoutedEventArgs e) => Close();
}
