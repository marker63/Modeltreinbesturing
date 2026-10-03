using System.IO.Ports;
using System.Linq;
using System.Windows;
using Modeltreinbesturing.Hardware;
using Modeltreinbesturing.Model;

namespace Modeltreinbesturing;

public partial class HardwareDialog : Window
{
    private readonly HardwareBeheerder _hardwareBeheerder;
    private readonly BaanOntwerpBeheerder _baanBeheerder;
    private readonly BlokBeheerder _blokBeheerder;

    public HardwareDialog(HardwareBeheerder hardwareBeheerder, BaanOntwerpBeheerder baanBeheerder, BlokBeheerder blokBeheerder)
    {
        InitializeComponent();
        _hardwareBeheerder = hardwareBeheerder;
        _baanBeheerder = baanBeheerder;
        _blokBeheerder = blokBeheerder;
        VulComPoorten();
        VoorselecteerOpgeslagenKeuze();
        StatusTekst.Text = _hardwareBeheerder.Huidige.Verbonden
            ? $"Verbonden: {_hardwareBeheerder.Huidige.Naam}."
            : "Niet verbonden.";
        _hardwareBeheerder.Huidige.StatusBericht += Bericht_Ontvangen;
    }

    /// <summary>Zet de radio-knoppen en COM-poort-keuze alvast op de laatst BEWAARDE keuze
    /// (zie HardwareInstellingen) - onafhankelijk van of er op dit moment daadwerkelijk
    /// verbinding is (dat toont StatusTekst hierboven al apart). Puur een handig
    /// startpunt; de gebruiker kan het gewoon wijzigen en op Verbinden drukken.</summary>
    private void VoorselecteerOpgeslagenKeuze()
    {
        var keuze = HardwareInstellingen.Laad();
        if (keuze is null) return;
        switch (keuze.InterfaceType)
        {
            case "Dinamo": DinamoBox.IsChecked = true; break;
            case "Intellibox": IntelliboxBox.IsChecked = true; break;
            case "DccEx": DccExBox.IsChecked = true; break;
            default: GeenBox.IsChecked = true; break;
        }
        if (keuze.ComPoort != null && ComPoortCombo.Items.Contains(keuze.ComPoort))
            ComPoortCombo.SelectedItem = keuze.ComPoort;
    }

    private void VulComPoorten()
    {
        ComPoortCombo.ItemsSource = SerialPort.GetPortNames();
        if (ComPoortCombo.Items.Count > 0) ComPoortCombo.SelectedIndex = 0;
    }

    private void Vernieuwen_Click(object sender, RoutedEventArgs e) => VulComPoorten();

    private void Bericht_Ontvangen(string bericht)
    {
        Dispatcher.Invoke(() => StatusTekst.Text = bericht);
    }

    private async void Verbinden_Click(object sender, RoutedEventArgs e)
    {
        if (GeenBox.IsChecked == true)
        {
            _hardwareBeheerder.WisselHardware(new SimulatieHardware());
            HardwareInstellingen.Bewaar("Simulatie", null);
            StatusTekst.Text = "Simulatiemodus actief.";
            return;
        }
        if (ComPoortCombo.SelectedItem is not string comPoort)
        {
            MessageBox.Show(this, "Kies eerst een COM-poort.", "Modeltreinbesturing", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        IHardwareInterface nieuw = DccExBox.IsChecked == true ? new DccExHardware()
            : IntelliboxBox.IsChecked == true ? new IntelliboxHardware()
            : new DinamoHardware();
        nieuw.StatusBericht += Bericht_Ontvangen;
        try
        {
            await nieuw.VerbindenAsync(comPoort);
            _hardwareBeheerder.WisselHardware(nieuw);
            HardwareInstellingen.Bewaar(HardwareInstellingen.TypeVan(nieuw), comPoort);
            StatusTekst.Text = $"Verbonden: {nieuw.Naam} op {comPoort}.";
            InitialiseerWisselsMetVlag();
            VraagAlleMelderStatusOp();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Verbinden mislukt", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>GEBRUIKERSVERZOEK ("er blijken bij het opstarten wissels in de verkeerde
    /// stand te staan, kan je bij het opstarten alle wissels even initialiseren?") -
    /// voorheen alleen wissels met Koploper's "altijd initialiseren"-vlag (een bewuste
    /// per-wissel keuze voor iets anders - zie Model.Wissel.AltijdInitialiseren). Nu ALTIJD
    /// alle wissels, driewegwissels én (Engelse) kruiswissels, ongeacht die vlag - zie
    /// MainWindow.VerbindMetOpgeslagenHardwareIndienBeschikbaar voor exact dezelfde,
    /// uitgebreide logica (nu hier ook voor het HANDMATIGE verbinden).</summary>
    private void InitialiseerWisselsMetVlag()
    {
        // BUG #28: gedeelde implementatie met MainWindow.VerbindMetOpgeslagenHardwareIndien
        // Beschikbaar - zie HardwareBeheerder.HerinitialiseerAlleWissels (ook gebruikt om na
        // een kortsluitherstel midden in een sessie automatisch opnieuw te initialiseren).
        int totaal = _hardwareBeheerder.HerinitialiseerAlleWissels(_baanBeheerder);
        if (totaal > 0)
            StatusTekst.Text += $" {totaal} wissel(s)/driewegwissel(s)/kruiswissel(s) geïnitialiseerd.";
    }

    /// <summary>Gebruikersverzoek ("dat lijkt mij zeer verstandig, voorkomt veel
    /// problemen") - hetzelfde als MainWindow.VerbindMetOpgeslagenHardwareIndienBeschikbaar
    /// doet bij het automatisch opnieuw verbinden bij opstarten, nu ook hier voor het
    /// HANDMATIGE verbinden (bijv. als je halverwege een sessie de kabel weer instopt):
    /// vraagt de ACTUELE status van elk bekend meldpunt in het hele project meteen op,
    /// in plaats van pas te weten te komen wat een sensor doet zodra er toevallig naar
    /// gevraagd wordt (zie StartStaartFase voor de bug die dit voorkomt).</summary>
    private void VraagAlleMelderStatusOp()
    {
        if (_hardwareBeheerder.Huidige is not DinamoHardware) return;
        // BUGFIX: zie HardwareBeheerder.VraagMelderStatusOp - dit ging voorheen rechtstreeks
        // naar DinamoHardware, buiten de wissel-/seinwachtrij om, en kon zo de wissel-
        // initialisatie hierboven (InitialiseerWisselsMetVlag) verdringen/vertragen.
        var alleMelders = _blokBeheerder.Blokken.SelectMany(b => b.Bezetmeldpunten).Select(m => m.MeldernNummer).Where(m => m > 0).Distinct().ToList();
        foreach (var meldernummer in alleMelders)
            _hardwareBeheerder.VraagMelderStatusOp(meldernummer);
        if (alleMelders.Count > 0)
            StatusTekst.Text += $" Status van {alleMelders.Count} bekende bezetmelder(s) opgevraagd.";
    }

    private void Ontkoppelen_Click(object sender, RoutedEventArgs e)
    {
        _hardwareBeheerder.Huidige.Ontkoppelen();
        _hardwareBeheerder.WisselHardware(new SimulatieHardware());
        HardwareInstellingen.Bewaar("Simulatie", null);
        StatusTekst.Text = "Ontkoppeld - terug naar simulatiemodus.";
    }

    private void Sluiten_Click(object sender, RoutedEventArgs e)
    {
        _hardwareBeheerder.Huidige.StatusBericht -= Bericht_Ontvangen;
        Close();
    }
}
