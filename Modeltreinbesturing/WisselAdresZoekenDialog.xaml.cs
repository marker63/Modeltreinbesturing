using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace Modeltreinbesturing;

/// <summary>GEBRUIKERSVERZOEK ("kan je ook zoiets maken als we voor de rijstroom-nummering
/// hebben gemaakt, voor het opzoeken van de wisselnummers") - het wissel-equivalent van
/// AdresZoekenDialog. Belangrijk verschil met die rijstroom-variant: daar kon de software
/// zelf een gewijzigde bezetmelder herkennen als automatisch bewijs van het juiste adres.
/// Voor een wisselstand bestaat in deze opstelling geen automatische terugmelding - net
/// als bij het uitzoeken van wissel4/wissel7/etc. vandaag blijft dus zelf kijken of
/// luisteren welke fysieke wissel beweegt de enige manier. Dit scherm automatiseert alleen
/// het STUREN van de commando's (met een pauze ertussen om de tijd te hebben te kijken),
/// niet de HERKENNING van welk adres het juiste is.</summary>
public partial class WisselAdresZoekenDialog : Window
{
    private readonly HardwareBeheerder _hardwareBeheerder;

    private DispatcherTimer? _timer;
    private List<int> _teTestenAdressen = new();
    private int _huidigeIndex;
    private bool _huidigAfbuigend;

    public WisselAdresZoekenDialog(HardwareBeheerder hardwareBeheerder)
    {
        InitializeComponent();
        _hardwareBeheerder = hardwareBeheerder;
        Closing += (_, _) => StopTest("venster gesloten");
    }

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(VanAdresBox.Text, out int vanAdres) || vanAdres < 1 ||
            !int.TryParse(TotAdresBox.Text, out int totAdres) || totAdres < vanAdres ||
            !int.TryParse(PauzeBox.Text, out int pauzeSec) || pauzeSec < 1)
        {
            StatusTekst.Text = "Controleer de ingevoerde getallen (adresbereik, pauze).";
            return;
        }

        _teTestenAdressen = Enumerable.Range(vanAdres, totAdres - vanAdres + 1).ToList();
        _huidigeIndex = -1;

        LogLijst.Items.Clear();
        LogLijst.Items.Add($"Start: adres {vanAdres} t/m {totAdres}, {pauzeSec} sec per adres.");
        StartKnop.IsEnabled = false;
        StopKnop.IsEnabled = true;
        VanAdresBox.IsEnabled = false;
        TotAdresBox.IsEnabled = false;
        PauzeBox.IsEnabled = false;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(pauzeSec) };
        _timer.Tick += (_, _) => VolgendeStap();
        VolgendeStap(); // meteen het eerste adres starten, niet pas na de eerste pauze wachten
        _timer.Start();
    }

    private void VolgendeStap()
    {
        _huidigeIndex++;
        if (_huidigeIndex >= _teTestenAdressen.Count)
        {
            StopTest("klaar - alle adressen doorlopen");
            return;
        }

        int adres = _teTestenAdressen[_huidigeIndex];
        // Telkens de stand wisselen (niet steeds dezelfde stand sturen) - een wissel die al
        // toevallig in de gestuurde stand staat, beweegt anders niet en valt zo ten onrechte
        // niet op.
        _huidigAfbuigend = !_huidigAfbuigend;
        StatusTekst.Text = $"Test adres {adres} ({_huidigeIndex + 1} van {_teTestenAdressen.Count}) - kijk/luister of er iets beweegt...";
        LogLijst.Items.Add($"Test adres {adres} ({(_huidigAfbuigend ? "afbuigend" : "rechtdoor")})...");
        if (LogLijst.Items.Count > 0) LogLijst.ScrollIntoView(LogLijst.Items[LogLijst.Items.Count - 1]);
        _hardwareBeheerder.StuurWisselCommando(adres, _huidigAfbuigend);
    }

    private void Stop_Click(object sender, RoutedEventArgs e) => StopTest("handmatig gestopt");

    private void StopTest(string reden)
    {
        if (_timer is null) return; // niet actief, niets te doen
        _timer.Stop();
        _timer = null;
        StatusTekst.Text = $"Gestopt ({reden}).";
        LogLijst.Items.Add($"Gestopt: {reden}.");
        StartKnop.IsEnabled = true;
        StopKnop.IsEnabled = false;
        VanAdresBox.IsEnabled = true;
        TotAdresBox.IsEnabled = true;
        PauzeBox.IsEnabled = true;
    }

    private void Sluiten_Click(object sender, RoutedEventArgs e) => Close();
}
