using System.Diagnostics;
using Modeltreinbesturing.Model;

namespace Modeltreinbesturing;

/// <summary>
/// De ECHTE meetlogica achter Koploper's snelheidsijking - geen simulatie van een meting,
/// maar een werkelijke tijdmeting tussen twee bezetmelders, gebaseerd op
/// IHardwareInterface.BezetmeldingGewijzigd. Zolang er geen echte hardware aangesloten is
/// (SimulatieHardware, de standaard) vuurt dat event nooit af, dus een gestarte meting
/// wacht dan voor altijd - dat is geen bug maar precies zoals het hoort: de IMPLEMENTATIE
/// is klaar en correct, ze kan alleen pas daadwerkelijk een resultaat opleveren zodra er
/// echte hardware met echte bezetmelders is aangesloten.
/// </summary>
public class SnelheidsMetingBeheerder
{
    private readonly HardwareBeheerder _hardwareBeheerder;
    public SnelheidsMetingInstellingen Instellingen { get; } = new();

    private bool _actief;
    private bool _wachtOpMelder1 = true;
    private Stopwatch? _stopwatch;

    /// <summary>Vuurt af zodra een meting succesvol is afgerond, met de berekende
    /// schaalsnelheid (km/u) als resultaat.</summary>
    public event Action<double>? MetingVoltooid;

    /// <summary>Statusregels voor het meetscherm (gestart/wachtend/voltooid/afgebroken).</summary>
    public event Action<string>? StatusBericht;

    public SnelheidsMetingBeheerder(HardwareBeheerder hardwareBeheerder)
    {
        _hardwareBeheerder = hardwareBeheerder;
        _hardwareBeheerder.HardwareGewijzigd += Herabonneer;
        Herabonneer();
    }

    /// <summary>Elke keer als de actieve hardware-koppeling wisselt (bijv. van simulatie
    /// naar een echte Dinamo-verbinding) moet er opnieuw geabonneerd worden op het NIEUWE
    /// IHardwareInterface-exemplaar. -= eerst voorkomt een dubbel abonnement als dit
    /// (onwaarschijnlijk maar voor de zekerheid) meerdere keren op hetzelfde exemplaar
    /// zou worden aangeroepen.</summary>
    private void Herabonneer()
    {
        _hardwareBeheerder.Huidige.BezetmeldingGewijzigd -= OpBezetmeldingGewijzigd;
        _hardwareBeheerder.Huidige.BezetmeldingGewijzigd += OpBezetmeldingGewijzigd;
    }

    public bool StartMeting()
    {
        if (Instellingen.MeldernNummer1 <= 0 || Instellingen.MeldernNummer2 <= 0 || Instellingen.LengteTrajectMm <= 0)
        {
            StatusBericht?.Invoke("Vul eerst de meetinstellingen in: twee bezetmelders en de trajectlengte (mm).");
            return false;
        }
        if (!_hardwareBeheerder.Huidige.Verbonden)
        {
            StatusBericht?.Invoke("Geen hardware verbonden - zonder echte bezetmelders komt er nooit een meting binnen. Verbind eerst via Beheren -> Hardware-interface (of gebruik ondertussen de handmatige tabel-invoer).");
        }
        _actief = true;
        _wachtOpMelder1 = true;
        _stopwatch = null;
        StatusBericht?.Invoke($"Meting actief - laat de loc rijden en wacht tot melder {Instellingen.MeldernNummer1} bezet meldt...");
        return true;
    }

    public void Noodstop()
    {
        _actief = false;
        _stopwatch = null;
        StatusBericht?.Invoke("Meting afgebroken.");
    }

    private void OpBezetmeldingGewijzigd(int meldernummer, bool bezet)
    {
        if (!_actief || !bezet) return;

        if (_wachtOpMelder1 && meldernummer == Instellingen.MeldernNummer1)
        {
            _stopwatch = Stopwatch.StartNew();
            _wachtOpMelder1 = false;
            StatusBericht?.Invoke($"Melder {meldernummer} bezet - tijdmeting gestart, wacht op melder {Instellingen.MeldernNummer2}...");
        }
        else if (!_wachtOpMelder1 && meldernummer == Instellingen.MeldernNummer2 && _stopwatch != null)
        {
            _stopwatch.Stop();
            double seconden = _stopwatch.Elapsed.TotalSeconds;
            _actief = false;

            if (seconden <= 0)
            {
                StatusBericht?.Invoke("Meting mislukt: tijdmeting gaf 0 seconden (te snel voor een betrouwbare meting).");
                return;
            }

            // Model-mm/sec omrekenen naar een echte schaalsnelheid in km/u via de
            // modelschaal (bijv. 1:87): de gemeten afstand is 87x kleiner dan in het echt,
            // dus vermenigvuldig de gemeten snelheid met de schaal om de "echte"
            // (schaal-)snelheid te krijgen die een echte trein op ware grootte zou hebben.
            double modelMmPerSec = Instellingen.LengteTrajectMm / seconden;
            double echteMPerSec = modelMmPerSec / 1000.0 * Instellingen.Modelschaal;
            double kmPerUur = echteMPerSec * 3.6;

            StatusBericht?.Invoke($"Meting voltooid: {seconden:0.00} sec over {Instellingen.LengteTrajectMm:0} mm ⇒ {kmPerUur:0.0} km/u (schaalsnelheid).");
            MetingVoltooid?.Invoke(kmPerUur);
        }
    }
}
