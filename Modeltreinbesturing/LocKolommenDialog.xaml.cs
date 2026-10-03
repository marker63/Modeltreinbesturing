using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Modeltreinbesturing;

/// <summary>Gebruikersverzoek: "de mogelijkheid om meerdere kolommen toe te voegen en/of
/// niet meer zichtbaar te maken" - simpele aanvinklijst, één regel per beschikbare kolom
/// (zie BaanontwerpWindow.BeschikbareLocKolommen, dat deze klasse als parameter meegeeft
/// zodat de beschikbare-kolommenlijst maar op één plek hoeft te staan).</summary>
public partial class LocKolommenDialog : Window
{
    private readonly List<(string Sleutel, CheckBox Box)> _regels = new();

    /// <summary>Pas geldig NADAT ShowDialog() true teruggeeft (op OK geklikt) - zie
    /// BaanontwerpWindow.LocKolommen_Click.</summary>
    public List<string> GekozenSleutels { get; private set; } = new();

    public LocKolommenDialog((string Sleutel, string Header, double Breedte, bool StandaardZichtbaar)[] beschikbareKolommen, List<string> huidigZichtbaar)
    {
        InitializeComponent();
        foreach (var kolom in beschikbareKolommen)
        {
            var box = new CheckBox
            {
                Content = kolom.Header,
                Margin = new Thickness(0, 0, 0, 8),
                IsChecked = huidigZichtbaar.Contains(kolom.Sleutel)
            };
            _regels.Add((kolom.Sleutel, box));
            KolommenPaneel.Children.Add(box);
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        GekozenSleutels = _regels.Where(r => r.Box.IsChecked == true).Select(r => r.Sleutel).ToList();
        DialogResult = true;
    }
}
