namespace Modeltreinbesturing.Hardware;

/// <summary>De standaard "hardware": doet niets echts, alles blijft pure software-
/// simulatie zoals de rest van Modeltreinbesturing al werkte. Dit is expliciet gemaakt als
/// een eigen implementatie (in plaats van "geen interface = null-checks overal") zodat
/// alle aanroepende code hetzelfde blijft, ongeacht of er straks wel/geen hardware is.</summary>
public class SimulatieHardware : IHardwareInterface
{
    public bool Verbonden { get; private set; }
    public string Naam => "Simulatie (geen hardware)";

    public event Action<int, bool>? BezetmeldingGewijzigd;
    public event Action<string>? StatusBericht;
    public event Action<bool>? KortsluitingStatusGewijzigd; // nooit gevuurd - alleen Dinamo kan dit daadwerkelijk detecteren

    public Task VerbindenAsync(string comPoort)
    {
        Verbonden = true;
        StatusBericht?.Invoke("Simulatiemodus actief - geen echte hardware aangesloten.");
        return Task.CompletedTask;
    }

    public void Ontkoppelen()
    {
        Verbonden = false;
    }

    public void ZetWissel(int adres, bool afbuigend)
    {
        HardwareCommunicatieLog.Log("Uit", $"[Simulatie] ZetWissel(adres={adres}, afbuigend={afbuigend}) - geen echte hardware, puur ter info.");
    }

    public void ZetSein(int adres, bool onveiligRood)
    {
        HardwareCommunicatieLog.Log("Uit", $"[Simulatie] ZetSein(adres={adres}, onveiligRood={onveiligRood}) - geen echte hardware, puur ter info.");
    }

    public void ZetLocSnelheid(int decoderAdres, int stap, bool vooruit, int blokNummer = 0, int stappen = 126)
    {
        HardwareCommunicatieLog.Log("Uit", $"[Simulatie] ZetLocSnelheid(decoderAdres={decoderAdres}, stap={stap}, vooruit={vooruit}) - geen echte hardware, puur ter info.");
    }

    public void ZetFunctie(int decoderAdres, int functieNummer, bool aan, int blokNummer = 0)
    {
        HardwareCommunicatieLog.Log("Uit", $"[Simulatie] ZetFunctie(decoderAdres={decoderAdres}, F{functieNummer}, aan={aan}) - geen echte hardware, puur ter info.");
    }

    public void Noodstop()
    {
        HardwareCommunicatieLog.Log("Uit", "[Simulatie] Noodstop() - geen echte hardware, de software-noodstop in TreinrouteWindow regelt dit al.");
    }
}
