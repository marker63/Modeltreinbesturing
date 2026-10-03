namespace Modeltreinbesturing.Model;

/// <summary>Een door de software zelf, op basis van ECHTE bezetmeldingen, GEMETEN reistijd
/// naar een specifiek blok - apart bijgehouden per AANKOMSTRICHTING (VanBlok) én per LOC
/// (Trein). Dat laatste is bewust: een goederenloc en een hogesnelheidstrein doen over
/// exact hetzelfde traject een heel andere tijd, en één gedeeld gemiddelde zou voor beide
/// verkeerd zijn. De rijsnelheid zelf hoeft er niet nog eens los bovenop bijgehouden te
/// worden - in normaal automatisch bedrijf rijdt dezelfde loc toch steeds met ongeveer
/// dezelfde kruissnelheid (bepaald door Trein.EffectiefTreintype), dus dat zit al impliciet
/// in de koppeling aan de loc zelf.
///
/// Zie BlokBeheerder.RegistreerGeleerdeReistijd/GeefGeleerdeReistijd voor hoe dit
/// bijgewerkt en opgezocht wordt (voortschrijdend gemiddelde: 70% oud/30% nieuw, net als
/// de oude, generieke versie hiervoor die dit vervangt).</summary>
public class GeleerdeReistijd
{
    public required Blok VanBlok { get; set; }
    public required Trein Trein { get; set; }
    public double Seconden { get; set; }

    public override string ToString() => $"vanuit blok {VanBlok.Nummer}, loc '{Trein.Omschrijving}': {Seconden:0.0} sec";
}
