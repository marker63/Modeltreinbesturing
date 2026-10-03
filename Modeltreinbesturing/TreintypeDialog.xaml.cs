using System.Globalization;
using System.Windows;
using Modeltreinbesturing.Model;

namespace Modeltreinbesturing;

public partial class TreintypeDialog : Window
{
    private readonly TreintypeBeheerder _treintypeBeheerder;
    private readonly TreinBeheerder? _treinBeheerder;
    private readonly TreinrouteBeheerder? _routeBeheerder;
    private readonly RichtingsverbodBeheerder? _richtingsverbodBeheerder;
    private readonly StopverbodBeheerder? _stopverbodBeheerder;
    private readonly StopverbodStilstandBeheerder? _stopverbodStilstandBeheerder;

    public TreintypeDialog(TreintypeBeheerder treintypeBeheerder, TreinBeheerder? treinBeheerder = null, TreinrouteBeheerder? routeBeheerder = null,
        RichtingsverbodBeheerder? richtingsverbodBeheerder = null, StopverbodBeheerder? stopverbodBeheerder = null, StopverbodStilstandBeheerder? stopverbodStilstandBeheerder = null)
    {
        InitializeComponent();
        _treintypeBeheerder = treintypeBeheerder;
        _treinBeheerder = treinBeheerder;
        _routeBeheerder = routeBeheerder;
        _richtingsverbodBeheerder = richtingsverbodBeheerder;
        _stopverbodBeheerder = stopverbodBeheerder;
        _stopverbodStilstandBeheerder = stopverbodStilstandBeheerder;
        VulLijst();
    }

    private void VulLijst()
    {
        TreintypesLijst.ItemsSource = null;
        TreintypesLijst.ItemsSource = _treintypeBeheerder.Treintypes;

        // De lijst met treintypes kan net gewijzigd zijn (nieuw/verwijderd) - de
        // rangeertreintype-keuzelijst gebruikt dezelfde onderliggende lijst, dus die moet
        // hier ook opnieuw gekoppeld worden (een kale List<T> vernieuwt een ComboBox niet
        // vanzelf bij Add/Remove, alleen bij het opnieuw instellen van ItemsSource).
        var huidigeSelectie = _treintypeBeheerder.RangeerTreintype;
        RangeerTreintypeCombo.ItemsSource = null;
        RangeerTreintypeCombo.ItemsSource = _treintypeBeheerder.Treintypes;
        RangeerTreintypeCombo.SelectedItem = _treintypeBeheerder.Treintypes.Contains(huidigeSelectie!) ? huidigeSelectie : null;
    }

    private void RangeerTreintypeZetten_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        _treintypeBeheerder.RangeerTreintype = RangeerTreintypeCombo.SelectedItem as Treintype;
        MessageBox.Show(this, _treintypeBeheerder.RangeerTreintype != null
            ? $"'{_treintypeBeheerder.RangeerTreintype.Omschrijving}' is nu het database-brede rangeertreintype."
            : "Er is geen rangeertreintype meer aangewezen - Rangeer betekent dan weer alleen 'geen massasimulatie', zonder aparte snelheden.",
            "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void TreintypesLijst_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (TreintypesLijst.SelectedItem is not Treintype type) return;

        OmschrijvingBox.Text = type.Omschrijving;
        MaxSnelheidBox.Text = type.MaxSnelheid.ToString(CultureInfo.InvariantCulture);
        GemiddeldeSnelheidBox.Text = type.GemiddeldeSnelheid.ToString(CultureInfo.InvariantCulture);
        MinimumSnelheidBox.Text = type.MinimumSnelheid.ToString(CultureInfo.InvariantCulture);
        AccelaratieBox.Text = type.AccelaratieVan0Naar50Seconden.ToString(CultureInfo.InvariantCulture);
        RemBox.Text = type.RemVan50Naar0Seconden.ToString(CultureInfo.InvariantCulture);
        WachttijdKerenBox.Text = type.WachttijdBijKerenSeconden.ToString(CultureInfo.InvariantCulture);
        VertrekvertragingBox.Text = type.VertrekvertragingSeconden.ToString(CultureInfo.InvariantCulture);
        StopkansBox.Text = type.StopkansPercentBijNormaalBlok.ToString(CultureInfo.InvariantCulture);
        MinWachttijdStopBox.Text = type.MinWachttijdBijStopSeconden.ToString(CultureInfo.InvariantCulture);
        MaxWachttijdStopBox.Text = type.MaxWachttijdBijStopSeconden.ToString(CultureInfo.InvariantCulture);

        VulBloktypeMatrix(type);
        VulTreinroutesLijst(type);
        VulGekoppeldeTreinenLijst(type);
    }

    /// <summary>Vult de "Treintype per bloktype"-matrix - via BloktypeGedragVoor() zodat
    /// er altijd zinnige waarden staan, ook als er nog geen expliciete regel voor dit
    /// bloktype bestaat (dan tonen we de terugvalwaarde, die je meteen kunt aanpassen).</summary>
    private void VulBloktypeMatrix(Treintype type)
    {
        var normaal = type.BloktypeGedragVoor(BlokType.Normaal);
        NormaalStopkansBox.Text = normaal.StopkansPercent.ToString(CultureInfo.InvariantCulture);
        NormaalMinBox.Text = normaal.MinWachttijd.ToString(CultureInfo.InvariantCulture);
        NormaalMaxBox.Text = normaal.MaxWachttijd.ToString(CultureInfo.InvariantCulture);

        var station = type.BloktypeGedragVoor(BlokType.Station);
        StationStopkansBox.Text = station.StopkansPercent.ToString(CultureInfo.InvariantCulture);
        StationMinBox.Text = station.MinWachttijd.ToString(CultureInfo.InvariantCulture);
        StationMaxBox.Text = station.MaxWachttijd.ToString(CultureInfo.InvariantCulture);

        var kopspoor = type.BloktypeGedragVoor(BlokType.Kopspoor);
        KopspoorStopkansBox.Text = kopspoor.StopkansPercent.ToString(CultureInfo.InvariantCulture);
        KopspoorMinBox.Text = kopspoor.MinWachttijd.ToString(CultureInfo.InvariantCulture);
        KopspoorMaxBox.Text = kopspoor.MaxWachttijd.ToString(CultureInfo.InvariantCulture);

        var opstelspoor = type.BloktypeGedragVoor(BlokType.Opstelspoor);
        OpstelspoorStopkansBox.Text = opstelspoor.StopkansPercent.ToString(CultureInfo.InvariantCulture);
        OpstelspoorMinBox.Text = opstelspoor.MinWachttijd.ToString(CultureInfo.InvariantCulture);
        OpstelspoorMaxBox.Text = opstelspoor.MaxWachttijd.ToString(CultureInfo.InvariantCulture);
    }

    private void VulTreinroutesLijst(Treintype type)
    {
        TreinroutesLijst.Items.Clear();
        if (_routeBeheerder is null)
        {
            TreinroutesLijst.Items.Add("(geen routes beschikbaar - open dit scherm via Treinroutes)");
            return;
        }
        foreach (var route in _routeBeheerder.Treinroutes)
            TreinroutesLijst.Items.Add(route.Treintype == type ? $"✓ {route.Omschrijving}" : $"   {route.Omschrijving}");
    }

    private void TreinroutesLijst_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (TreintypesLijst.SelectedItem is not Treintype type) return;
        if (_routeBeheerder is null) return;
        if (TreinroutesLijst.SelectedIndex < 0 || TreinroutesLijst.SelectedIndex >= _routeBeheerder.Treinroutes.Count) return;

        var route = _routeBeheerder.Treinroutes[TreinroutesLijst.SelectedIndex];
        route.Treintype = route.Treintype == type ? null : type;
        VulTreinroutesLijst(type);
    }

    private void VulGekoppeldeTreinenLijst(Treintype type)
    {
        GekoppeldeTreinenLijst.ItemsSource = null;
        if (_treinBeheerder is null)
        {
            GekoppeldeTreinenLijst.Items.Clear();
            GekoppeldeTreinenLijst.Items.Add("(geen treinen beschikbaar - open dit scherm via Treintypes beheren in het hoofdvenster)");
            return;
        }
        GekoppeldeTreinenLijst.ItemsSource = _treinBeheerder.Treinen.Where(t => t.Treintype == type).ToList();
    }

    private void Opslaan_Click(object sender, RoutedEventArgs e)
    {
        if (TreintypesLijst.SelectedItem is not Treintype type)
        {
            MessageBox.Show(this, "Selecteer eerst een treintype.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!TryGetal(MaxSnelheidBox, out double max) || !TryGetal(GemiddeldeSnelheidBox, out double gem) || !TryGetal(MinimumSnelheidBox, out double min) ||
            !TryGetal(AccelaratieBox, out double accelaratie) || !TryGetal(RemBox, out double rem) ||
            !TryGetal(WachttijdKerenBox, out double wachttijdKeren) || !TryGetal(VertrekvertragingBox, out double vertrekvertraging) ||
            !TryGetal(StopkansBox, out double stopkans) || !TryGetal(MinWachttijdStopBox, out double minWachttijdStop) || !TryGetal(MaxWachttijdStopBox, out double maxWachttijdStop) ||
            !TryGetal(NormaalStopkansBox, out double normaalStopkans) || !TryGetal(NormaalMinBox, out double normaalMin) || !TryGetal(NormaalMaxBox, out double normaalMax) ||
            !TryGetal(StationStopkansBox, out double stationStopkans) || !TryGetal(StationMinBox, out double stationMin) || !TryGetal(StationMaxBox, out double stationMax) ||
            !TryGetal(KopspoorStopkansBox, out double kopspoorStopkans) || !TryGetal(KopspoorMinBox, out double kopspoorMin) || !TryGetal(KopspoorMaxBox, out double kopspoorMax) ||
            !TryGetal(OpstelspoorStopkansBox, out double opstelspoorStopkans) || !TryGetal(OpstelspoorMinBox, out double opstelspoorMin) || !TryGetal(OpstelspoorMaxBox, out double opstelspoorMax))
        {
            MessageBox.Show(this, "Vul overal geldige getallen in.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        type.Omschrijving = OmschrijvingBox.Text;
        type.MaxSnelheid = max;
        type.GemiddeldeSnelheid = gem;
        type.MinimumSnelheid = min;
        type.AccelaratieVan0Naar50Seconden = accelaratie;
        type.RemVan50Naar0Seconden = rem;
        type.WachttijdBijKerenSeconden = wachttijdKeren;
        type.VertrekvertragingSeconden = vertrekvertraging;
        type.StopkansPercentBijNormaalBlok = Math.Clamp(stopkans, 0, 100);
        type.MinWachttijdBijStopSeconden = minWachttijdStop;
        type.MaxWachttijdBijStopSeconden = maxWachttijdStop;

        type.BloktypeGedrag.Clear();
        type.BloktypeGedrag.Add(new TreintypeBloktypeGedrag { BlokType = BlokType.Normaal, StopkansPercent = Math.Clamp(normaalStopkans, 0, 100), MinWachttijdSeconden = normaalMin, MaxWachttijdSeconden = normaalMax });
        type.BloktypeGedrag.Add(new TreintypeBloktypeGedrag { BlokType = BlokType.Station, StopkansPercent = Math.Clamp(stationStopkans, 0, 100), MinWachttijdSeconden = stationMin, MaxWachttijdSeconden = stationMax });
        type.BloktypeGedrag.Add(new TreintypeBloktypeGedrag { BlokType = BlokType.Kopspoor, StopkansPercent = Math.Clamp(kopspoorStopkans, 0, 100), MinWachttijdSeconden = kopspoorMin, MaxWachttijdSeconden = kopspoorMax });
        type.BloktypeGedrag.Add(new TreintypeBloktypeGedrag { BlokType = BlokType.Opstelspoor, StopkansPercent = Math.Clamp(opstelspoorStopkans, 0, 100), MinWachttijdSeconden = opstelspoorMin, MaxWachttijdSeconden = opstelspoorMax });

        VulLijst();
        TreintypesLijst.SelectedItem = type;
    }

    private static bool TryGetal(System.Windows.Controls.TextBox box, out double waarde) =>
        double.TryParse(box.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out waarde);

    private void Nieuw_Click(object sender, RoutedEventArgs e)
    {
        string? naam = InputDialog.Vraag(this, "Nieuw treintype", "Omschrijving:", "Nieuw treintype");
        if (string.IsNullOrWhiteSpace(naam)) return;
        var type = _treintypeBeheerder.NieuwTreintype(naam);
        VulLijst();
        TreintypesLijst.SelectedItem = type;
    }

    private void Verwijderen_Click(object sender, RoutedEventArgs e)
    {
        if (TreintypesLijst.SelectedItem is not Treintype type) return;
        var vraag = MessageBox.Show(this, $"Treintype '{type.Omschrijving}' verwijderen?", "Treintype verwijderen", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (vraag != MessageBoxResult.Yes) return;
        _treintypeBeheerder.VerwijderTreintype(type);
        _treinBeheerder?.VergeetTreintype(type);
        _richtingsverbodBeheerder?.VergeetTreintype(type);
        _stopverbodBeheerder?.VergeetTreintype(type);
        _stopverbodStilstandBeheerder?.VergeetTreintype(type);
        VulLijst();
    }

    private void Sluiten_Click(object sender, RoutedEventArgs e) => Close();
}
