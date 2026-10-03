using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using Modeltreinbesturing.Model;

namespace Modeltreinbesturing;

/// <summary>GEBRUIKERSVERZOEK ("kan je een testroutine maken die alle bezetmelders/adressen
/// 1 voor 1 aanstuurt en kijkt of er een andere bezetmelder actief wordt"): voor een
/// elektrisch geïsoleerde sectie (zoals een kruiswissel) waarvan het rijstroom-adres
/// onbekend is - het bloknummer hoeft namelijk NIETS te maken te hebben met welk
/// meldernummer erbij hoort (zie Model.Blok: elk blok heeft zijn EIGEN Nummer als
/// rijstroom-adres, los van zijn Bezetmeldpunten) - blijft experimenteel uitproberen de
/// enige betrouwbare manier. Dit scherm automatiseert dat: doorloopt een reeks adressen,
/// stuurt de gekozen loc daar telkens kort een lage snelheid naartoe, en vergelijkt de
/// bezetmelder-standen vóór en na elk venster - een gevonden verschil wijst sterk op het
/// juiste adres (de loc is dan gaan rijden en heeft een naburige melder geraakt).</summary>
public partial class AdresZoekenDialog : Window
{
    private readonly HardwareBeheerder _hardwareBeheerder;
    private readonly BlokBeheerder _blokBeheerder;
    private readonly TreinBeheerder _treinBeheerder;

    private DispatcherTimer? _timer;
    private List<int> _teTestenAdressen = new();
    private int _huidigeIndex;
    private Dictionary<int, bool?> _melderStandVoorVenster = new();
    private List<int> _alleBekendeMelders = new();
    private Trein? _gekozenLoc;
    private int _testsnelheid;

    public AdresZoekenDialog(HardwareBeheerder hardwareBeheerder, BlokBeheerder blokBeheerder, TreinBeheerder treinBeheerder)
    {
        InitializeComponent();
        _hardwareBeheerder = hardwareBeheerder;
        _blokBeheerder = blokBeheerder;
        _treinBeheerder = treinBeheerder;

        LocCombo.ItemsSource = _treinBeheerder.Treinen;
        LocCombo.DisplayMemberPath = "Omschrijving";
        if (_treinBeheerder.Treinen.Count > 0) LocCombo.SelectedIndex = 0;

        Closing += (_, _) => StopTest("venster gesloten");
    }

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        if (LocCombo.SelectedItem is not Trein gekozenLoc || gekozenLoc.DecoderAdres <= 0)
        {
            StatusTekst.Text = "Kies eerst een loc met een geldig decoderadres.";
            return;
        }
        if (!int.TryParse(VanAdresBox.Text, out int vanAdres) || vanAdres < 1 ||
            !int.TryParse(TotAdresBox.Text, out int totAdres) || totAdres < vanAdres ||
            !int.TryParse(WachttijdBox.Text, out int wachttijdSec) || wachttijdSec < 1 ||
            !int.TryParse(SnelheidBox.Text, out int snelheid) || snelheid < 1)
        {
            StatusTekst.Text = "Controleer de ingevoerde getallen (adresbereik, testduur, snelheid).";
            return;
        }

        _gekozenLoc = gekozenLoc;
        _testsnelheid = snelheid;
        _teTestenAdressen = Enumerable.Range(vanAdres, totAdres - vanAdres + 1).ToList();
        _huidigeIndex = -1;
        // Dezelfde manier waarop de rest van de software "alle bekende meldpunten"
        // verzamelt (zie MainWindow.VerbindMetOpgeslagenHardwareIndienBeschikbaar) - alle
        // meldernummers die ergens in een Blok voorkomen, ongeacht welk blok.
        _alleBekendeMelders = _blokBeheerder.Blokken.SelectMany(b => b.Bezetmeldpunten).Select(m => m.MeldernNummer).Where(m => m > 0).Distinct().ToList();

        LogLijst.Items.Clear();
        LogLijst.Items.Add($"Start: adres {vanAdres} t/m {totAdres}, {wachttijdSec} sec per adres, snelheid {snelheid}, loc '{gekozenLoc.Omschrijving}'.");
        StartKnop.IsEnabled = false;
        StopKnop.IsEnabled = true;
        LocCombo.IsEnabled = false;
        VanAdresBox.IsEnabled = false;
        TotAdresBox.IsEnabled = false;
        WachttijdBox.IsEnabled = false;
        SnelheidBox.IsEnabled = false;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(wachttijdSec) };
        _timer.Tick += (_, _) => VolgendeStap();
        VolgendeStap(); // meteen het eerste adres starten, niet pas na de eerste wachttijd
        _timer.Start();
    }

    private void VolgendeStap()
    {
        // Sluit het VORIGE venster af (indien van toepassing): snelheid=0 en de
        // meldervergelijking loggen, vóórdat het volgende adres begint.
        if (_huidigeIndex >= 0 && _gekozenLoc != null)
        {
            int vorigAdres = _teTestenAdressen[_huidigeIndex];
            _hardwareBeheerder.StuurLocSnelheidCommando(_gekozenLoc.DecoderAdres, 0, true, vorigAdres, _gekozenLoc.DecoderStappen);
            var gewijzigd = _alleBekendeMelders.Where(m => _blokBeheerder.MelderIsBezet(m) != _melderStandVoorVenster.GetValueOrDefault(m)).ToList();
            if (gewijzigd.Count > 0)
                LogLijst.Items.Add($"  -> Adres {vorigAdres}: meldpunt(en) {string.Join(", ", gewijzigd)} van stand gewisseld tijdens dit venster - MOGELIJK het juiste adres!");
        }

        _huidigeIndex++;
        if (_gekozenLoc is null || _huidigeIndex >= _teTestenAdressen.Count)
        {
            StopTest("klaar - alle adressen doorlopen");
            return;
        }

        int adres = _teTestenAdressen[_huidigeIndex];
        _melderStandVoorVenster = _alleBekendeMelders.ToDictionary(m => m, m => _blokBeheerder.MelderIsBezet(m));
        StatusTekst.Text = $"Test adres {adres} ({_huidigeIndex + 1} van {_teTestenAdressen.Count})...";
        LogLijst.Items.Add($"Test adres {adres}...");
        if (LogLijst.Items.Count > 0) LogLijst.ScrollIntoView(LogLijst.Items[LogLijst.Items.Count - 1]);
        _hardwareBeheerder.StuurLocSnelheidCommando(_gekozenLoc.DecoderAdres, _testsnelheid, true, adres, _gekozenLoc.DecoderStappen);
    }

    private void Stop_Click(object sender, RoutedEventArgs e) => StopTest("handmatig gestopt");

    private void StopTest(string reden)
    {
        if (_timer is null) return; // niet actief, niets te doen
        _timer.Stop();
        _timer = null;
        if (_huidigeIndex >= 0 && _huidigeIndex < _teTestenAdressen.Count && _gekozenLoc != null)
            _hardwareBeheerder.StuurLocSnelheidCommando(_gekozenLoc.DecoderAdres, 0, true, _teTestenAdressen[_huidigeIndex], _gekozenLoc.DecoderStappen);
        StatusTekst.Text = $"Gestopt ({reden}).";
        LogLijst.Items.Add($"Gestopt: {reden}.");
        StartKnop.IsEnabled = true;
        StopKnop.IsEnabled = false;
        LocCombo.IsEnabled = true;
        VanAdresBox.IsEnabled = true;
        TotAdresBox.IsEnabled = true;
        WachttijdBox.IsEnabled = true;
        SnelheidBox.IsEnabled = true;
    }

    private void Sluiten_Click(object sender, RoutedEventArgs e) => Close();
}
