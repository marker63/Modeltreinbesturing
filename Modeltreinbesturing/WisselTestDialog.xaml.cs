using System.Windows;
using System.Windows.Threading;
using Modeltreinbesturing.Model;

namespace Modeltreinbesturing;

public partial class WisselTestDialog : Window
{
    private readonly BaanOntwerpBeheerder _baanBeheerder;
    private readonly HardwareBeheerder _hardwareBeheerder;
    private readonly Action _naElkeStap;
    private readonly DispatcherTimer _timer = new();
    private List<Wissel> _teTesten = new();
    private int _index;

    public WisselTestDialog(BaanOntwerpBeheerder baanBeheerder, HardwareBeheerder hardwareBeheerder, Action naElkeStap)
    {
        InitializeComponent();
        _baanBeheerder = baanBeheerder;
        _hardwareBeheerder = hardwareBeheerder;
        _naElkeStap = naElkeStap;
        _timer.Tick += (_, _) => VolgendeStap();
    }

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(PauzeBox.Text, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double pauzeSeconden) || pauzeSeconden <= 0)
        {
            VoortgangTekst.Text = "Vul een geldige pauze in (groter dan 0 seconden).";
            return;
        }
        _teTesten = _baanBeheerder.Symbolen.OfType<Wissel>().ToList();
        if (_teTesten.Count == 0)
        {
            VoortgangTekst.Text = "Geen wissels gevonden op de baan.";
            return;
        }
        _index = 0;
        ResultatenLijst.Items.Clear();
        _timer.Interval = TimeSpan.FromSeconds(pauzeSeconden);
        _timer.Start();
        VolgendeStap(); // meteen de eerste, niet pas na de eerste pauze wachten
    }

    /// <summary>Zet één wissel om (rechtdoor -> afbuigend, of andersom als 'm al afbuigend
    /// stond) en stuurt hetzelfde commando naar de hardware - werkt dus ALLEEN fysiek als
    /// er echte hardware verbonden is, net als bij een gewone routestart.</summary>
    private void VolgendeStap()
    {
        if (_index >= _teTesten.Count)
        {
            _timer.Stop();
            VoortgangTekst.Text = $"Klaar - {_teTesten.Count} wissel(s) getest.";
            return;
        }
        var wissel = _teTesten[_index];
        wissel.Stand = wissel.Stand == WisselStand.Rechtdoor ? WisselStand.Afbuigend : WisselStand.Rechtdoor;
        bool afbuigendVoorHardware = wissel.Stand == WisselStand.Afbuigend;
        _hardwareBeheerder.StuurWisselCommando(wissel.Adres, wissel.OmgekeerdePolariteit ? !afbuigendVoorHardware : afbuigendVoorHardware);
        _naElkeStap();

        _index++;
        VoortgangTekst.Text = $"Bezig: wissel {wissel.Adres} ({_index}/{_teTesten.Count})...";
        ResultatenLijst.Items.Add($"Wissel {wissel.Adres} -> {wissel.Stand}");
        ResultatenLijst.ScrollIntoView(ResultatenLijst.Items[^1]);
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        VoortgangTekst.Text = $"Gestopt na {_index} van de {_teTesten.Count} wissel(s).";
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e) => _timer.Stop();

    private void Sluiten_Click(object sender, RoutedEventArgs e) => Close();
}
