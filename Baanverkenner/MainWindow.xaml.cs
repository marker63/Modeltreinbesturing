using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Baanverkenner.Kern;
using Baanverkenner.Kern.Simulatie;
using Modeltreinbesturing.Hardware;

namespace Baanverkenner;

/// <summary>Eén regel in een van de loglijsten (tekst + kleur).</summary>
public class LogItem
{
    public string Tekst { get; init; } = "";
    public Brush Kleur { get; init; } = Brushes.Black;
    public FontWeight Gewicht { get; init; } = FontWeights.Normal;
}

public partial class MainWindow : Window
{
    private record CentraleKeuze(string Naam, string Soort)
    {
        public override string ToString() => Naam;
        public bool IsSimulatie => Soort.StartsWith("Sim");
        public bool IsDinamo => Soort is "Dinamo" or "SimDinamo";
    }

    private static readonly CentraleKeuze[] Centrales =
    {
        new("VPEB Dinamo", "Dinamo"),
        new("DCC-EX", "DccEx"),
        new("Intellibox (LocoNet)", "Intellibox"),
        new("Roco/Fleischmann Z21 (netwerk, nog niet getest)", "Z21"),
        new("Simulatie-proefbaan, als Dinamo (zonder echte baan)", "SimDinamo"),
        new("Simulatie-proefbaan, als DCC-centrale (zonder echte baan)", "SimDcc"),
    };

    private static readonly string Map = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Modeltreinbesturing", "Baanverkenner");
    private static readonly string VoortgangPad = Path.Combine(Map, "voortgang.json");
    private static readonly string InstellingenPad = Path.Combine(Map, "instellingen.json");

    private class OpgeslagenKeuzes
    {
        public string Centrale { get; set; } = "Dinamo";
        public string? ComPoort { get; set; }
        public int SimTempo { get; set; } = 20;
        public VerkenInstellingen Instellingen { get; set; } = new();
    }

    private IHardwareInterface? _hw;
    private SimulatieBaan? _sim;
    private BaanVerkenner? _verkenner;
    private VerkenLog? _log;
    private CancellationTokenSource? _cts;
    private bool _bezig;
    private DateTime _startTijd;
    private string? _laatsteStatusJson;
    private readonly DispatcherTimer _tijdTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly ObservableCollection<LogItem> _logRegels = new();
    private readonly ObservableCollection<LogItem> _hardwareRegels = new();

    public MainWindow()
    {
        InitializeComponent();

        InterfaceCombo.ItemsSource = Centrales;
        LocStappenCombo.ItemsSource = new[] { 14, 28, 126 };
        SimSnelheidCombo.ItemsSource = new[] { "1x (echte tijd)", "5x sneller", "20x sneller", "50x sneller" };
        LogLijst.ItemsSource = _logRegels;
        HardwareLijst.ItemsSource = _hardwareRegels;
        try { ComPoortCombo.ItemsSource = SerialPort.GetPortNames(); } catch { /* geen poorten */ }

        LaadKeuzes();

        _tijdTimer.Tick += (_, _) =>
        {
            if (_bezig) TellerTijd.Text = $"Tijd: {DateTime.Now - _startTijd:h\\:mm\\:ss}";
        };
        HardwareCommunicatieLog.NieuweRegel += HardwareRegelBinnen;
        WerkKnoppenBij();
    }

    // =====================================================================
    // Instellingen lezen/schrijven
    // =====================================================================

    private void LaadKeuzes()
    {
        var k = new OpgeslagenKeuzes();
        try
        {
            if (File.Exists(InstellingenPad))
                k = JsonSerializer.Deserialize<OpgeslagenKeuzes>(File.ReadAllText(InstellingenPad)) ?? k;
        }
        catch { /* beschadigd: standaardwaarden */ }

        InterfaceCombo.SelectedItem = Centrales.FirstOrDefault(c => c.Soort == k.Centrale) ?? Centrales[0];
        ComPoortCombo.Text = k.ComPoort ?? "";
        SimSnelheidCombo.SelectedIndex = k.SimTempo switch { 1 => 0, 5 => 1, 50 => 3, _ => 2 };
        ToonInstellingen(k.Instellingen);
    }

    private void BewaarKeuzes(VerkenInstellingen ins)
    {
        try
        {
            Directory.CreateDirectory(Map);
            var k = new OpgeslagenKeuzes
            {
                Centrale = (InterfaceCombo.SelectedItem as CentraleKeuze)?.Soort ?? "Dinamo",
                ComPoort = ComPoortCombo.Text,
                SimTempo = SimTempo(),
                Instellingen = ins
            };
            File.WriteAllText(InstellingenPad, JsonSerializer.Serialize(k, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* comfort, geen ramp */ }
    }

    private int SimTempo() => SimSnelheidCombo.SelectedIndex switch { 0 => 1, 1 => 5, 3 => 50, _ => 20 };

    private void ToonInstellingen(VerkenInstellingen i)
    {
        LocAdresBox.Text = i.LocAdres.ToString();
        LocStappenCombo.SelectedItem = i.LocStappen is 14 or 28 or 126 ? i.LocStappen : 28;
        VerkensnelheidBox.Text = i.Verkensnelheid.ToString();
        KruipsnelheidBox.Text = i.Kruipsnelheid.ToString();
        AdresVanBox.Text = i.WisselAdresVan.ToString();
        AdresTotBox.Text = i.WisselAdresTot.ToString();
        WisselPauzeBox.Text = i.WisselPauzeMs.ToString();
        DinamoBlokkenBox.Text = i.DinamoBlokken;
        HoogsteMelderBox.Text = i.HoogsteMelder.ToString();
        MaxSecBox.Text = i.MaxSecondenTussenMelders.ToString();
        SlimmeTimeoutCheck.IsChecked = i.SlimmeTimeout;
        MinSecBox.Text = i.MinSecondenTussenMelders.ToString();
        LegeMeldersBox.Text = i.LegeMeldersSeconden.ToString("0.0#");
        MaxKortsluitBox.Text = i.MaxKortsluitingenPerPlek.ToString();
        OntdenderBox.Text = i.OntdenderMs.ToString();
        BlokLangBox.Text = i.BlokproefLangSeconden.ToString();
        BlokKortBox.Text = i.BlokproefKortSeconden.ToString();
        BlokInrijBox.Text = i.BlokproefInrijSeconden.ToString("0.0#");
        ExtraMeldersBox.Text = i.ExtraMeldersNaAfwijking.ToString();
        BasisHerhalenCheck.IsChecked = i.BasisritHerhalen;
    }

    private static int Geheel(TextBox box, string naam, int min, int max)
    {
        if (!int.TryParse(box.Text.Trim(), out int v) || v < min || v > max)
            throw new FormatException($"{naam}: vul een geheel getal in van {min} t/m {max}.");
        return v;
    }

    private static double Getal(TextBox box, string naam, double min, double max)
    {
        var t = box.Text.Trim().Replace(',', '.');
        if (!double.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) || v < min || v > max)
            throw new FormatException($"{naam}: vul een getal in van {min} t/m {max}.");
        return v;
    }

    private VerkenInstellingen LeesInstellingen()
    {
        var i = new VerkenInstellingen
        {
            LocAdres = Geheel(LocAdresBox, "Decoderadres", 1, 10239),
            LocStappen = LocStappenCombo.SelectedItem is int st ? st : 28,
            Verkensnelheid = Geheel(VerkensnelheidBox, "Verkensnelheid", 1, 126),
            Kruipsnelheid = Geheel(KruipsnelheidBox, "Kruipsnelheid", 1, 126),
            WisselAdresVan = Geheel(AdresVanBox, "Adressen van", 1, 2048),
            WisselAdresTot = Geheel(AdresTotBox, "Adressen t/m", 1, 2048),
            WisselPauzeMs = Geheel(WisselPauzeBox, "Pauze na wisselcommando", 50, 10000),
            DinamoBlokken = DinamoBlokkenBox.Text.Trim(),
            HoogsteMelder = Geheel(HoogsteMelderBox, "Hoogste meldernummer", 1, 2048),
            MaxSecondenTussenMelders = Geheel(MaxSecBox, "Max. seconden tussen melders", 5, 600),
            SlimmeTimeout = SlimmeTimeoutCheck.IsChecked == true,
            MinSecondenTussenMelders = Geheel(MinSecBox, "Min. seconden tussen melders", 2, 600),
            LegeMeldersSeconden = Getal(LegeMeldersBox, "Kortsluittijd", 0.5, 60),
            MaxKortsluitingenPerPlek = Geheel(MaxKortsluitBox, "Max. kortsluitingen per plek", 1, 20),
            OntdenderMs = Geheel(OntdenderBox, "Ontdenderen", 0, 5000),
            BlokproefLangSeconden = Geheel(BlokLangBox, "Blokproef bij start", 3, 120),
            BlokproefKortSeconden = Geheel(BlokKortBox, "Blokproef onderweg", 2, 60),
            BlokproefInrijSeconden = Getal(BlokInrijBox, "Doorrijden vóór blokproef", 0, 20),
            ExtraMeldersNaAfwijking = Geheel(ExtraMeldersBox, "Extra melders na afwijking", 0, 10),
            BasisritHerhalen = BasisHerhalenCheck.IsChecked == true
        };
        if (i.Kruipsnelheid > i.Verkensnelheid)
            throw new FormatException($"De kruipsnelheid ({i.Kruipsnelheid}) is hoger dan de verkensnelheid ({i.Verkensnelheid}). Verhoog ook de verkensnelheid, of kies een lagere kruipsnelheid.");
        if (i.LocStappen <= 28 && i.Verkensnelheid > i.LocStappen)
            throw new FormatException($"De verkensnelheid is hoger dan het aantal rijstappen ({i.LocStappen}).");
        if ((InterfaceCombo.SelectedItem as CentraleKeuze)?.IsDinamo == true && i.DinamoBlokLijst().Count == 0)
            throw new FormatException("Vul bij Dinamo de blokken in, bijvoorbeeld 1-16.");
        if (i.MinSecondenTussenMelders > i.MaxSecondenTussenMelders)
            throw new FormatException("De minimale wachttijd tussen melders is groter dan de maximale.");
        return i;
    }

    // =====================================================================
    // Verbinding
    // =====================================================================

    private void InterfaceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (InterfaceCombo.SelectedItem is not CentraleKeuze k) return;
        var zichtbaarCom = k.IsSimulatie ? Visibility.Collapsed : Visibility.Visible;
        ComPoortLabel.Visibility = zichtbaarCom;
        if (ComPoortLabel is System.Windows.Controls.TextBlock poortTekst) poortTekst.Text = k.Soort == "Z21" ? "IP-adres" : "COM-poort";
        ComPoortCombo.Visibility = zichtbaarCom;
        SimSnelheidLabel.Visibility = k.IsSimulatie ? Visibility.Visible : Visibility.Collapsed;
        SimSnelheidCombo.Visibility = SimSnelheidLabel.Visibility;
        DinamoGroep.IsEnabled = k.IsDinamo;
        if (k.IsSimulatie)
        {
            // De proefbaan heeft vaste adressen/blokken: handige standaardwaarden invullen.
            if (AdresTotBox.Text == "32") AdresTotBox.Text = "16";
            if (DinamoBlokkenBox.Text == "1-16") DinamoBlokkenBox.Text = "1-8";
            if (HoogsteMelderBox.Text == "64") HoogsteMelderBox.Text = "16";
        }
    }

    private async void Verbinden_Click(object sender, RoutedEventArgs e)
    {
        if (InterfaceCombo.SelectedItem is not CentraleKeuze k) return;
        OntkoppelHuidige();
        VerbindenKnop.IsEnabled = false;
        VerbindingStatus.Text = "Verbinden…";
        try
        {
            IHardwareInterface hw;
            _sim = null;
            switch (k.Soort)
            {
                case "Dinamo": hw = new DinamoHardware(); break;
                case "DccEx": hw = new DccExHardware(); break;
                case "Intellibox": hw = new IntelliboxHardware(); break;
                case "Z21": hw = new Z21Hardware(); break;
                default:
                    _sim = DemoBaan.Maak(k.Soort == "SimDinamo");
                    hw = _sim;
                    break;
            }
            hw.StatusBericht += HardwareStatus;
            string poort = k.IsSimulatie ? "SIM" : ComPoortCombo.Text.Trim();
            if (!k.IsSimulatie && poort.Length == 0) throw new InvalidOperationException("Kies eerst een COM-poort.");
            await hw.VerbindenAsync(poort);
            _hw = hw;
            VerbindingStatus.Text = k.IsSimulatie
                ? "Verbonden met de simulatie-proefbaan: een ovaal met inhaalspoor, kopspoor en twee zijsporen (melders 1-11, wissels 1, 2, 5, 9 en 14). De loc (adres 3) staat op melder 1."
                : $"Verbonden met {hw.Naam} op {poort}.";
            if (k.IsSimulatie) LocAdresBox.Text = "3";
            BewaarKeuzesStil();
        }
        catch (Exception ex)
        {
            _hw = null;
            _sim = null;
            VerbindingStatus.Text = "Niet verbonden.";
            MessageBox.Show(this, ex.Message, "Verbinden mislukt", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            WerkKnoppenBij();
        }
    }

    private void BewaarKeuzesStil()
    {
        try { BewaarKeuzes(LeesInstellingen()); } catch { /* ongeldige invoer: niet bewaren */ }
    }

    private void Ontkoppelen_Click(object sender, RoutedEventArgs e)
    {
        OntkoppelHuidige();
        VerbindingStatus.Text = "Niet verbonden.";
        WerkKnoppenBij();
    }

    private void OntkoppelHuidige()
    {
        if (_hw is null) return;
        try
        {
            _hw.StatusBericht -= HardwareStatus;
            _hw.Ontkoppelen();
        }
        catch { }
        _hw = null;
        _sim = null;
    }

    private void HardwareStatus(string s) => HardwareCommunicatieLog.Log("Info", s);

    private void HardwareRegelBinnen(HardwareCommunicatieLog.LogRegel r)
    {
        if (r.Tekst.Contains("keepalive")) return; // Dinamo: 5x per seconde, geen informatie
        Dispatcher.InvokeAsync(() =>
        {
            var kleur = r.Richting switch { "Uit" => Brushes.DarkOrange, "In" => Brushes.SteelBlue, _ => Brushes.DarkGoldenrod };
            _hardwareRegels.Add(new LogItem { Tekst = $"{r.Tijdstip:HH:mm:ss.fff}  {r.Richting,-4} {r.Tekst}", Kleur = kleur });
            while (_hardwareRegels.Count > 3000) _hardwareRegels.RemoveAt(0);
        });
    }

    // =====================================================================
    // Starten / hervatten / pauze / stop / noodstop
    // =====================================================================

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_hw is null || !_hw.Verbonden)
        {
            MessageBox.Show(this, "Maak eerst verbinding met de centrale (stap 1).", "Niet verbonden", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        VerkenInstellingen ins;
        try { ins = LeesInstellingen(); }
        catch (FormatException ex)
        {
            MessageBox.Show(this, ex.Message, "Controleer de instellingen", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        // BUG #32 (gebruikerscasus: verkenning liep vast - de loc "kon niet netjes op melder
        // 16 gezet worden" en kwam uiteindelijk bijna een hele ronde verder op melder 29/30
        // terecht): bytes-analyse van de hardware-log liet zien dat dit NIET de bekende
        // kortsluitstatus-vlucht was (die trad hier tweemaal kort op en werd beide keren
        // netjes opgeheven, precies zoals BUG #31 bedoeld is) - de loc schoot simpelweg door
        // tijdens het "kruipen" (Nastellen hieronder in BaanVerkenner.Rijden.cs), omdat de
        // kruipsnelheid in deze sessie op 20 stond (verkensnelheid 28, dus bijna even snel als
        // "vol gas") op een baan waar een heel blok in minder dan 1 seconde wordt doorlopen
        // (zie de bezetmeldingen in het log, vaak <1 sec na elkaar). "Kruipen" is bedoeld om
        // heel langzaam precies op de juiste melder te stoppen; bij zo'n hoge kruipsnelheid kan
        // de software dat venster nooit op tijd zien en schiet de loc door voordat er ooit
        // gestopt wordt. De bestaande validatie controleerde alleen "kruip ≤ verken", niet of
        // kruip ABSOLUUT laag genoeg is om nog echt te kunnen "kruipen" - vandaar deze extra,
        // niet-blokkerende waarschuwing (de gebruiker kan nog steeds bewust doorgaan).
        if (ins.Kruipsnelheid > 10 && ins.Kruipsnelheid * 2 > ins.Verkensnelheid)
        {
            var kruipWaarschuwing = MessageBox.Show(this,
                $"De kruipsnelheid ({ins.Kruipsnelheid}) is vrij hoog ten opzichte van de verkensnelheid ({ins.Verkensnelheid}). " +
                "\"Kruipen\" is bedoeld om heel langzaam precies op de juiste melder te stoppen - bij een hoge kruipsnelheid, " +
                "zeker op een baan met korte blokken, kan de software niet op tijd ingrijpen en schiet de loc ver door " +
                "(dit veroorzaakte eerder een mislukte verkenning: de loc kwam bijna een hele ronde verder terecht).\n\n" +
                "Een veel lagere kruipsnelheid (bijvoorbeeld 3-5) wordt aangeraden.\n\n" +
                $"Toch doorgaan met kruipsnelheid {ins.Kruipsnelheid}?",
                "Kruipsnelheid lijkt hoog", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (kruipWaarschuwing != MessageBoxResult.Yes) return;
        }

        BewaarKeuzes(ins);

        var vorige = LaadVoortgang();
        if (vorige is not null && !vorige.Kaart.Voltooid)
        {
            var r = MessageBox.Show(this,
                "Er is nog een onderbroken verkenning. Een nieuwe verkenning overschrijft die.\n\nToch een nieuwe verkenning starten? (Kies Nee om eerst die te hervatten met de knop 'Vorige verkenning hervatten'.)",
                "Onderbroken verkenning", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;
        }

        string bereik = $"{Math.Min(ins.WisselAdresVan, ins.WisselAdresTot)} t/m {Math.Max(ins.WisselAdresVan, ins.WisselAdresTot)}";
        var ok = MessageBox.Show(this,
            $"Voordat de verkenning begint:\n\n" +
            $"•  Alleen de testloc (adres {ins.LocAdres}) staat op de baan, helemaal binnen één melder. Haal alle andere voertuigen weg.\n" +
            $"•  De vooruit-richting van de loc wordt de 'vooruit' in het rapport.\n" +
            $"•  Alle adressen {bereik} worden geschakeld, ook seinen en ontkoppelrails op die adressen.\n" +
            $"•  Bij een wissel die van achteren verkeerd staat volgt een kortsluiting; de verkenner stopt dan direct en rijdt terug.\n" +
            $"•  Blijf in de buurt van de noodstop, zeker de eerste keer.\n\n" +
            $"Starten?",
            "Verkenning starten", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (ok != MessageBoxResult.Yes) return;

        await VoerVerkenningUit(ins, null);
    }

    private async void HervatVorige_Click(object sender, RoutedEventArgs e)
    {
        var st = LaadVoortgang();
        if (st is null)
        {
            MessageBox.Show(this, "Er is geen bewaarde verkenning gevonden.", "Hervatten", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (st.Kaart.Voltooid)
        {
            MessageBox.Show(this, "De laatste verkenning is al voltooid. Het resultaat kun je nog steeds bekijken en opslaan.", "Hervatten", MessageBoxButton.OK, MessageBoxImage.Information);
            ToonResultaat(st);
            return;
        }
        if (_hw is null || !_hw.Verbonden)
        {
            MessageBox.Show(this, "Maak eerst verbinding met dezelfde centrale als bij de onderbroken verkenning.", "Niet verbonden", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        // Snelheden en tijden: wat NU in het scherm staat (die mag je tussendoor aanpassen).
        // Loc, rijstappen, adresbereik en Dinamo-blokken: van de onderbroken verkenning,
        // want anders klopt de bewaarde voortgang niet meer.
        VerkenInstellingen scherm;
        try { scherm = LeesInstellingen(); }
        catch (FormatException ex)
        {
            MessageBox.Show(this, ex.Message, "Controleer de instellingen", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var vast = st.Instellingen;
        var behouden = new List<string>();
        if (scherm.LocAdres != vast.LocAdres) behouden.Add($"decoderadres {vast.LocAdres}");
        if (scherm.LocStappen != vast.LocStappen) behouden.Add($"{vast.LocStappen} rijstappen");
        if (scherm.WisselAdresVan != vast.WisselAdresVan || scherm.WisselAdresTot != vast.WisselAdresTot) behouden.Add($"adressen {vast.WisselAdresVan} t/m {vast.WisselAdresTot}");
        if (scherm.DinamoBlokken != vast.DinamoBlokken) behouden.Add($"Dinamo-blokken {vast.DinamoBlokken}");
        vast.Verkensnelheid = scherm.Verkensnelheid;
        vast.Kruipsnelheid = scherm.Kruipsnelheid;
        vast.WisselPauzeMs = scherm.WisselPauzeMs;
        vast.HoogsteMelder = scherm.HoogsteMelder;
        vast.MaxSecondenTussenMelders = scherm.MaxSecondenTussenMelders;
        vast.SlimmeTimeout = scherm.SlimmeTimeout;
        vast.MinSecondenTussenMelders = scherm.MinSecondenTussenMelders;
        vast.LegeMeldersSeconden = scherm.LegeMeldersSeconden;
        vast.MaxKortsluitingenPerPlek = scherm.MaxKortsluitingenPerPlek;
        vast.OntdenderMs = scherm.OntdenderMs;
        vast.BlokproefLangSeconden = scherm.BlokproefLangSeconden;
        vast.BlokproefKortSeconden = scherm.BlokproefKortSeconden;
        vast.BlokproefInrijSeconden = scherm.BlokproefInrijSeconden;
        vast.ExtraMeldersNaAfwijking = scherm.ExtraMeldersNaAfwijking;
        vast.BasisritHerhalen = scherm.BasisritHerhalen;
        if (vast.LocStappen <= 28 && (vast.Verkensnelheid > vast.LocStappen || vast.Kruipsnelheid > vast.LocStappen))
        {
            MessageBox.Show(this, $"De onderbroken verkenning gebruikt {vast.LocStappen} rijstappen; de snelheden mogen daar niet boven komen.", "Controleer de instellingen", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        ToonInstellingen(vast);
        BewaarKeuzes(vast);
        string behoudenTekst = behouden.Count == 0 ? ""
            : $"\n\nLet op: {string.Join(", ", behouden)} blijven zoals bij de start van deze verkenning (anders klopt de bewaarde voortgang niet).";
        var ok = MessageBox.Show(this,
            $"De onderbroken verkenning (gestart {st.Kaart.Gestart:dd-MM-yyyy HH:mm}, {st.Kaart.Wissels.Count} wissels en {st.Kaart.Melders.Count} melders gevonden) wordt hervat met verkensnelheid {vast.Verkensnelheid} en kruipsnelheid {vast.Kruipsnelheid}.{behoudenTekst}\n\n" +
            $"Zet de testloc (adres {vast.LocAdres}) op startmelder {st.Kaart.StartMelder}, met dezelfde kant vooruit als de vorige keer.\n\nHervatten?",
            "Verkenning hervatten", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (ok != MessageBoxResult.Yes) return;
        if (_sim is not null) _sim.ZetLocOpMelder(st.Kaart.StartMelder);
        await VoerVerkenningUit(st.Instellingen, st);
    }

    private async Task VoerVerkenningUit(VerkenInstellingen ins, VerkenStatus? hervat)
    {
        if (_hw is null) return;
        var hw = _hw;

        IKlok klok;
        if (_sim is not null)
        {
            var vk = new VersneldeKlok(SimTempo());
            vk.Getikt += _sim.Tik;
            klok = vk;
        }
        else klok = new EchteKlok();

        _logRegels.Clear();
        _log = new VerkenLog(klok);
        _log.NieuweRegel += r => Dispatcher.InvokeAsync(() => VoegLogToe(r));
        if (hervat is not null)
            _log.Info($"Hervat vanaf opgeslagen voortgang ({hervat.AfgerondeOpdrachten} opdrachten al afgerond).");

        _verkenner = new BaanVerkenner(hw, ins, klok, _log, VraagGebruiker, hervat);
        _verkenner.VoortgangGewijzigd += v => Dispatcher.InvokeAsync(() => ToonVoortgang(v));
        _verkenner.Bewaren = st =>
        {
            // Wordt aangeroepen op de thread van de verkenner: hier (en alleen hier) is de
            // kaart stabiel, dus hier maken we de kopie voor het scherm en het bestand.
            var json = st.AlsJson();
            var tekst = Rapport.AlsTekst(st.Kaart);
            try
            {
                Directory.CreateDirectory(Map);
                File.WriteAllText(VoortgangPad, json);
            }
            catch { /* volgende keer beter */ }
            Dispatcher.InvokeAsync(() =>
            {
                _laatsteStatusJson = json;
                ResultaatBox.Text = tekst;
            });
        };

        LiveVerkenBox.Text = ins.Verkensnelheid.ToString();
        LiveKruipBox.Text = ins.Kruipsnelheid.ToString();
        _cts = new CancellationTokenSource();
        _bezig = true;
        _startTijd = DateTime.Now;
        _tijdTimer.Start();
        PauzeKnop.Content = "Pauze";
        FaseTekst.Text = hervat is null ? "Verkenning gestart" : "Verkenning hervat";
        WerkKnoppenBij();
        Tabs.SelectedIndex = 0;

        var verkenner = _verkenner;
        var token = _cts.Token;
        try
        {
            await Task.Run(() => verkenner.VoerUit(token));
            FaseTekst.Text = "Verkenning voltooid";
            BezigTekst.Text = "Bekijk het resultaat of sla het rapport en de baankaart op.";
            ToonResultaat(verkenner.Status);
            MessageBox.Show(this,
                $"De verkenning is voltooid.\n\n{verkenner.Kaart.Melders.Count} melders, {verkenner.Kaart.Wissels.Count} wissels, {verkenner.Kaart.Kopsporen.Count} doodlopende einden en {verkenner.Kaart.VoorgesteldeBlokken.Count} voorgestelde blokken gevonden.\n\nMet 'Rapport bekijken' zie je het volledige overzicht.",
                "Klaar", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (OperationCanceledException)
        {
            FaseTekst.Text = "Verkenning gestopt";
            BezigTekst.Text = "De voortgang is bewaard. Met 'Vorige verkenning hervatten' ga je later verder.";
            ToonResultaat(verkenner.Status);
        }
        catch (Exception ex)
        {
            FaseTekst.Text = "Verkenning afgebroken door een fout";
            BezigTekst.Text = ex.Message;
            ToonResultaat(verkenner.Status);
            MessageBox.Show(this, $"De verkenning is afgebroken:\n\n{ex.Message}\n\nDe voortgang is bewaard; na het oplossen kun je hervatten.",
                "Fout", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _bezig = false;
            _tijdTimer.Stop();
            _cts?.Dispose();
            _cts = null;
            WerkKnoppenBij();
        }
    }

    private void ToonResultaat(VerkenStatus st)
    {
        // Alleen aanroepen als de verkenner NIET (meer) loopt, of met een losse kopie.
        _laatsteStatusJson = st.AlsJson();
        ResultaatBox.Text = Rapport.AlsTekst(st.Kaart);
        Tabs.SelectedItem = ResultaatTab;
    }

    /// <summary>Vraag van de verkenner aan de gebruiker (bijv. "zet de loc terug op melder
    /// X"). Draait op de UI-thread; de verkenner wacht zolang.</summary>
    private Task<bool> VraagGebruiker(string titel, string tekst)
    {
        return Dispatcher.InvokeAsync(() =>
        {
            var sim = _sim;
            string extra = sim is not null ? "\n\n(Simulatie: bij OK zet het programma de loc zelf terug.)" : "";
            var r = MessageBox.Show(this, tekst + extra, titel, MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (r == MessageBoxResult.OK && sim is not null)
            {
                var m = Regex.Match(tekst, @"op melder (\d+), zodat");
                if (m.Success) sim.ZetLocOpMelder(int.Parse(m.Groups[1].Value));
            }
            return r == MessageBoxResult.OK;
        }).Task;
    }

    private void LiveToepassen_Click(object sender, RoutedEventArgs e)
    {
        if (_verkenner is null || !_bezig) return;
        try
        {
            int verken = Geheel(LiveVerkenBox, "Verkensnelheid", 1, 126);
            int kruip = Geheel(LiveKruipBox, "Kruipsnelheid", 1, 126);
            int stappen = _verkenner.Status.Instellingen.LocStappen;
            if (kruip > verken)
                throw new FormatException($"De kruipsnelheid ({kruip}) is hoger dan de verkensnelheid ({verken}).");
            if (stappen <= 28 && verken > stappen)
                throw new FormatException($"De decoder heeft {stappen} rijstappen; de verkensnelheid mag daar niet boven komen.");
            _verkenner.PasSnelhedenAan(verken, kruip);
            VerkensnelheidBox.Text = verken.ToString();
            KruipsnelheidBox.Text = kruip.ToString();
            BewaarKeuzes(_verkenner.Status.Instellingen);
        }
        catch (FormatException ex)
        {
            MessageBox.Show(this, ex.Message, "Snelheid aanpassen", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Pauze_Click(object sender, RoutedEventArgs e)
    {
        if (_verkenner is null || !_bezig) return;
        if (_verkenner.Gepauzeerd)
        {
            _verkenner.Hervat();
            PauzeKnop.Content = "Pauze";
        }
        else
        {
            _verkenner.Pauzeer();
            PauzeKnop.Content = "Hervat";
        }
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        if (!_bezig) return;
        var r = MessageBox.Show(this, "De verkenning stoppen? De voortgang blijft bewaard, je kunt later hervatten.",
            "Stoppen", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r == MessageBoxResult.Yes) _cts?.Cancel();
    }

    private void Noodstop_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_verkenner is not null && _bezig)
            {
                _verkenner.Noodstop();
                if (!_verkenner.Gepauzeerd)
                {
                    _verkenner.Pauzeer();
                    PauzeKnop.Content = "Hervat";
                }
                MessageBox.Show(this, "Noodstop gegeven. De verkenning staat op pauze.\n\nControleer de loc (ontspoord? staat hij nog op een melder?) en klik dan op 'Hervat', of op 'Stoppen'.",
                    "Noodstop", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else if (_hw is not null)
            {
                _hw.Noodstop();
                if (int.TryParse(LocAdresBox.Text, out int adres))
                {
                    int stappen = LocStappenCombo.SelectedItem is int s ? s : 28;
                    if (_hw.LocCommandoVereistBlok)
                    {
                        var blokken = new VerkenInstellingen { DinamoBlokken = DinamoBlokkenBox.Text }.DinamoBlokLijst();
                        foreach (var b in blokken) _hw.ZetLocSnelheid(adres, 0, true, b, stappen);
                    }
                    else _hw.ZetLocSnelheid(adres, 0, true, 0, stappen);
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Noodstop", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void WerkKnoppenBij()
    {
        bool verbonden = _hw is not null && _hw.Verbonden;
        InstellingenPaneel.IsEnabled = !_bezig;
        LivePaneel.IsEnabled = _bezig;
        VerbindenKnop.IsEnabled = !_bezig;
        OntkoppelenKnop.IsEnabled = !_bezig && verbonden;
        StartKnop.IsEnabled = !_bezig && verbonden;
        HervatVorigeKnop.IsEnabled = !_bezig;
        PauzeKnop.IsEnabled = _bezig;
        StopKnop.IsEnabled = _bezig;
        NoodstopKnop.IsEnabled = verbonden;
    }

    // =====================================================================
    // Weergave
    // =====================================================================

    private void VoegLogToe(VerkenLog.Regel r)
    {
        var (kleur, gewicht) = r.Soort switch
        {
            LogSoort.Stap => (Brushes.MidnightBlue, FontWeights.SemiBold),
            LogSoort.Vondst => (Brushes.SeaGreen, FontWeights.SemiBold),
            LogSoort.Waarschuwing => (Brushes.DarkOrange, FontWeights.SemiBold),
            LogSoort.Fout => (Brushes.Firebrick, FontWeights.Bold),
            LogSoort.Rijden => (Brushes.DimGray, FontWeights.Normal),
            _ => (Brushes.Black, FontWeights.Normal)
        };
        _logRegels.Add(new LogItem { Tekst = VerkenLog.Opmaak(r), Kleur = kleur, Gewicht = gewicht });
        while (_logRegels.Count > 5000) _logRegels.RemoveAt(0);
        LogLijst.ScrollIntoView(_logRegels[^1]);
    }

    private void ToonVoortgang(VerkenVoortgang v)
    {
        FaseTekst.Text = v.Fase;
        BezigTekst.Text = v.Bezig;
        TellerMelders.Text = $"Melders: {v.GevondenMelders}";
        TellerWissels.Text = $"Wissels: {v.GevondenWissels}";
        TellerAdressen.Text = $"Nog te testen adressen: {v.TeTestenAdressen}";
        TellerOpdrachten.Text = $"Opdrachten: {v.AfgerondeOpdrachten} klaar, {v.OpenOpdrachten} open";
        TellerKortsluit.Text = $"Kortsluitingen: {v.Kortsluitingen}";
    }

    // =====================================================================
    // Exporteren
    // =====================================================================

    private static VerkenStatus? LaadVoortgang()
    {
        try
        {
            if (File.Exists(VoortgangPad)) return VerkenStatus.VanJson(File.ReadAllText(VoortgangPad));
        }
        catch { }
        return null;
    }

    /// <summary>Een veilige kopie van de huidige baankaart (ook tijdens een lopende verkenning).</summary>
    private Baankaart? HuidigeKaart()
    {
        string? json = _laatsteStatusJson;
        if (json is null && File.Exists(VoortgangPad))
        {
            try { json = File.ReadAllText(VoortgangPad); } catch { }
        }
        if (json is null) return null;
        var kaart = VerkenStatus.VanJson(json)?.Kaart;
        if (kaart is not null && !kaart.Voltooid)
            kaart.VoorgesteldeBlokken = BlokVoorstel.Bereken(kaart); // tussenstand: voorstel alvast tonen
        return kaart;
    }

    private Baankaart? KaartOfMelding()
    {
        var k = HuidigeKaart();
        if (k is null)
            MessageBox.Show(this, "Er is nog geen resultaat. Start eerst een verkenning.", "Geen resultaat", MessageBoxButton.OK, MessageBoxImage.Information);
        return k;
    }

    private string? KiesBestand(string standaardNaam, string filter)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog { FileName = standaardNaam, Filter = filter, AddExtension = true };
        return dlg.ShowDialog(this) == true ? dlg.FileName : null;
    }

    private static string Datumstempel() => DateTime.Now.ToString("yyyy-MM-dd_HHmm");

    private void Schrijf(string? pad, string inhoud)
    {
        if (pad is null) return;
        try
        {
            File.WriteAllText(pad, inhoud, System.Text.Encoding.UTF8);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Opslaan mislukt", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RapportOpenen_Click(object sender, RoutedEventArgs e)
    {
        var k = KaartOfMelding();
        if (k is null) return;
        try
        {
            var pad = Path.Combine(Path.GetTempPath(), "baanverkenner-rapport.html");
            File.WriteAllText(pad, Rapport.AlsHtml(k), System.Text.Encoding.UTF8);
            Process.Start(new ProcessStartInfo(pad) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Rapport openen mislukt", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RapportHtml_Click(object sender, RoutedEventArgs e)
    {
        var k = KaartOfMelding();
        if (k is null) return;
        Schrijf(KiesBestand($"baanrapport_{Datumstempel()}.html", "Webpagina (*.html)|*.html"), Rapport.AlsHtml(k));
    }

    private void RapportTekst_Click(object sender, RoutedEventArgs e)
    {
        var k = KaartOfMelding();
        if (k is null) return;
        Schrijf(KiesBestand($"baanrapport_{Datumstempel()}.txt", "Tekstbestand (*.txt)|*.txt"), Rapport.AlsTekst(k));
    }

    private void Baankaart_Click(object sender, RoutedEventArgs e)
    {
        var k = KaartOfMelding();
        if (k is null) return;
        if (!k.Voltooid)
        {
            var r = MessageBox.Show(this, "De verkenning is nog niet voltooid. De baankaart bevat alleen wat tot nu toe gevonden is. Toch opslaan?",
                "Tussenstand", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;
        }
        Schrijf(KiesBestand($"baankaart_{Datumstempel()}.json", "Baankaart (*.json)|*.json"), k.AlsJson());
    }

    private void LogOpslaan_Click(object sender, RoutedEventArgs e)
    {
        if (_log is null)
        {
            MessageBox.Show(this, "Er is nog geen logboek in deze sessie.", "Logboek", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        Schrijf(KiesBestand($"baanverkenner-log_{Datumstempel()}.txt", "Tekstbestand (*.txt)|*.txt"), _log.AlsTekst());
    }

    private void HardwareLog_Click(object sender, RoutedEventArgs e)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Baanverkenner - hardware-communicatie (zonder keepalives)");
        foreach (var r in HardwareCommunicatieLog.Regels)
            sb.AppendLine($"{r.Tijdstip:yyyy-MM-dd HH:mm:ss.fff}  {r.Richting,-4} {r.Tekst}");
        Schrijf(KiesBestand($"hardwarelog_{Datumstempel()}.txt", "Tekstbestand (*.txt)|*.txt"), sb.ToString());
    }

    // =====================================================================
    // Afsluiten
    // =====================================================================

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_bezig)
        {
            var r = MessageBox.Show(this, "Er loopt nog een verkenning. Afsluiten stopt de loc en bewaart de voortgang. Afsluiten?",
                "Afsluiten", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) { e.Cancel = true; return; }
            try { _verkenner?.Noodstop(); } catch { }
            _cts?.Cancel();
        }
        HardwareCommunicatieLog.NieuweRegel -= HardwareRegelBinnen;
        OntkoppelHuidige();
    }
}
