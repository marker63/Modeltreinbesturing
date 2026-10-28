namespace Baanverkenner.Kern;

/// <summary>
/// WAT: wat het venster van de Baanverkenner van een verkenner nodig heeft (starten, pauzeren, noodstop, voortgang, bewaren).
/// WAAROM: Marco kan kiezen tussen module 1 (<see cref="BaanVerkenner"/>, de oorspronkelijke verkenner) en module 2
/// (<see cref="BaanVerkenner2"/>, de nieuwe opzet volgens het stappenplan van 27-10). Beide maken dezelfde baankaart, dus
/// rapport, melderschema, blokkenschema en wisselstraten werken met allebei.
/// </summary>
public interface IBaanVerkenner
{
    Task VoerUit(CancellationToken token);
    VerkenStatus Status { get; }
    Baankaart Kaart { get; }
    VerkenLog Log { get; }
    bool Gepauzeerd { get; }
    void Pauzeer();
    void Hervat();
    void Noodstop();
    void PasSnelhedenAan(int verkensnelheid, int kruipsnelheid);
    event Action<VerkenVoortgang>? VoortgangGewijzigd;
    Action<VerkenStatus>? Bewaren { get; set; }
    IReadOnlyCollection<int> BezetteMelders { get; }
    /// <summary>Optioneel: blok per melder uit de Koploper-export (alleen als hint; mag null zijn).</summary>
    KoploperReferentie? Referentie { get; set; }
}
