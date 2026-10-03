using System.Linq;
using System.Windows;
using System.Windows.Media;
using Modeltreinbesturing.Hardware;

namespace Modeltreinbesturing;

public partial class HardwareLogDialog : Window
{
    public HardwareLogDialog()
    {
        InitializeComponent();
        foreach (var regel in HardwareCommunicatieLog.Regels)
            VoegRegelToe(regel);
        HardwareCommunicatieLog.NieuweRegel += OpNieuweRegel;
    }

    /// <summary>Kan van een ANDERE thread komen (bijv. de seriële poort se eigen
    /// DataReceived-event, dat niet per se op de UI-thread afgaat) - vandaar Dispatcher.
    /// Invoke i.p.v. rechtstreeks de UI aan te raken.</summary>
    private void OpNieuweRegel(HardwareCommunicatieLog.LogRegel regel) =>
        Dispatcher.Invoke(() => VoegRegelToe(regel));

    private void VoegRegelToe(HardwareCommunicatieLog.LogRegel regel)
    {
        var item = new System.Windows.Controls.ListBoxItem
        {
            Content = $"{regel.Tijdstip:HH:mm:ss.fff} [{regel.Richting,3}] {regel.Tekst}",
            Foreground = regel.Richting == "Uit" ? Brushes.DarkOrange : regel.Richting == "In" ? Brushes.SteelBlue : Brushes.Goldenrod
        };
        RegelsLijst.Items.Add(item);
        while (RegelsLijst.Items.Count > 2000) RegelsLijst.Items.RemoveAt(0);
        if (AutomatischScrollenBox.IsChecked == true && RegelsLijst.Items.Count > 0)
            RegelsLijst.ScrollIntoView(RegelsLijst.Items[^1]);
    }

    private void Wissen_Click(object sender, RoutedEventArgs e)
    {
        HardwareCommunicatieLog.Wissen();
        RegelsLijst.Items.Clear();
    }

    /// <summary>Exporteert de VOLLEDIGE, onderliggende geschiedenis (HardwareCommunicatieLog.
    /// Regels) i.p.v. alleen wat op dit moment zichtbaar is in de lijst - beide zijn
    /// normaal gesproken gelijk, maar dit is de meest betrouwbare bron.</summary>
    private void Exporteren_Click(object sender, RoutedEventArgs e)
    {
        if (HardwareCommunicatieLog.Regels.Count == 0)
        {
            MessageBox.Show(this, "Er is nog niets gelogd om te exporteren.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dialoog = new Microsoft.Win32.SaveFileDialog { Filter = "Tekstbestand (*.txt)|*.txt", FileName = "hardware-communicatielog.txt" };
        if (dialoog.ShowDialog(this) != true) return;

        var regels = HardwareCommunicatieLog.Regels
            .Select(r => $"{r.Tijdstip:yyyy-MM-dd HH:mm:ss.fff} [{r.Richting,3}] {r.Tekst}");
        try
        {
            System.IO.File.WriteAllLines(dialoog.FileName, regels, System.Text.Encoding.UTF8);
            MessageBox.Show(this, $"{HardwareCommunicatieLog.Regels.Count} regel(s) geëxporteerd naar {dialoog.FileName}.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Exporteren mislukt: {ex.Message}", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Vertaalt de VOLLEDIGE geschiedenis naar leesbare CSV-rijen (Dinamo: elk
    /// commando/elke melding gedecodeerd naar type/blok/decoderadres/snelheid/etc. i.p.v.
    /// kale hex) - zie Hardware/DinamoLogVertaler.cs. Voor een andere hardware-interface dan
    /// Dinamo (die geen eigen bytes-protocol heeft om te decoderen) komt gewoon "Onbekend" in
    /// de Type-kolom te staan met de oorspronkelijke tekst in Toelichting - nooit een
    /// crash of een stilzwijgend weggelaten regel.</summary>
    private void ExporterenLeesbaar_Click(object sender, RoutedEventArgs e)
    {
        if (HardwareCommunicatieLog.Regels.Count == 0)
        {
            MessageBox.Show(this, "Er is nog niets gelogd om te exporteren.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dialoog = new Microsoft.Win32.SaveFileDialog { Filter = "CSV-bestand (*.csv)|*.csv", FileName = "hardware-communicatielog-leesbaar.csv" };
        if (dialoog.ShowDialog(this) != true) return;

        try
        {
            string csv = DinamoLogVertaler.VertaalLogRegels(HardwareCommunicatieLog.Regels);
            // UTF-8 MET BOM: Excel herkent het bestand anders soms als ANSI en verknoeit é/ë e.d.
            System.IO.File.WriteAllText(dialoog.FileName, csv, new System.Text.UTF8Encoding(true));
            MessageBox.Show(this, $"{HardwareCommunicatieLog.Regels.Count} regel(s) vertaald en geëxporteerd naar {dialoog.FileName}.\n\nDirect te openen in Excel (dubbelklikken volstaat).", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Exporteren mislukt: {ex.Message}", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e) =>
        HardwareCommunicatieLog.NieuweRegel -= OpNieuweRegel;

    private void Sluiten_Click(object sender, RoutedEventArgs e) => Close();
}
