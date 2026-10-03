using System;
using System.Windows;
using System.Windows.Media;
using Modeltreinbesturing.Model;

namespace Modeltreinbesturing;

/// <summary>Eén regel in de live-lijst van de Meldpuntenverkenner.</summary>
public class MeldpuntRegel
{
    public required string Tekst { get; set; }
    public required Brush Kleur { get; set; }
    public required FontWeight Gewicht { get; set; }
}

/// <summary>GEBRUIKERSVERZOEK ("we zijn de kluts kwijt welke melder waar hoort, kan je een
/// verkenner bouwen") - toont LIVE elke binnenkomende bezetmelding, inclusief meldpunten
/// die nog aan geen enkel blok gekoppeld zijn (zie BlokBeheerder.RuweMelderStatusGewijzigd).
/// Bewust GEEN automatische aansturing - de gebruiker duwt zelf de loc over de baan en ziet
/// hier live welk meldernummer daarbij oplicht. Veiliger en directer dan een adressen-sweep
/// voor dit specifieke doel (een onbekende melder aan een fysieke plek koppelen).</summary>
public partial class MeldpuntenVerkennerDialog : Window
{
    private readonly BlokBeheerder _blokBeheerder;
    private readonly System.Collections.ObjectModel.ObservableCollection<MeldpuntRegel> _regels = new();

    public MeldpuntenVerkennerDialog(BlokBeheerder blokBeheerder)
    {
        InitializeComponent();
        _blokBeheerder = blokBeheerder;
        LogLijst.ItemsSource = _regels;
        _blokBeheerder.RuweMelderStatusGewijzigd += OpMelderStatusGewijzigd;
        Closing += (_, _) => _blokBeheerder.RuweMelderStatusGewijzigd -= OpMelderStatusGewijzigd;
    }

    private void OpMelderStatusGewijzigd(int meldernummer, bool bezet, Blok? blok)
    {
        Dispatcher.Invoke(() =>
        {
            bool onbekend = blok is null;
            if (AlleenOnbekendeBox.IsChecked == true && !onbekend) return;

            string tijd = DateTime.Now.ToString("HH:mm:ss.fff");
            string tekst = onbekend
                ? $"{tijd}  melder {meldernummer}: {(bezet ? "BEZET" : "vrij")}  <-- NOG NIET GEKOPPELD AAN EEN BLOK"
                : $"{tijd}  melder {meldernummer}: {(bezet ? "bezet" : "vrij")}  (blok {blok!.Nummer})";

            _regels.Add(new MeldpuntRegel
            {
                Tekst = tekst,
                Kleur = onbekend ? Brushes.DarkOrange : Brushes.Black,
                Gewicht = onbekend ? FontWeights.Bold : FontWeights.Normal
            });
            if (_regels.Count > 0) LogLijst.ScrollIntoView(_regels[_regels.Count - 1]);
        });
    }

    private void Wissen_Click(object sender, RoutedEventArgs e) => _regels.Clear();

    private void Sluiten_Click(object sender, RoutedEventArgs e) => Close();
}
