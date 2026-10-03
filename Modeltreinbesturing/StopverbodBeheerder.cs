using Modeltreinbesturing.Model;

namespace Modeltreinbesturing;

/// <summary>
/// Stopverbod: een lijst blokken waar een trein niet mag stoppen, zoals Koploper's
/// "Onderhouden variabele treinroute" - Stopverbod-tabblad. Omdat in onze simulator elk
/// blok in een pad een stopmelding krijgt (zie TreinrouteWindow.BouwGebeurtenissen), wordt
/// dit gecontroleerd tegen het VOLLEDIGE uitgeklapte pad van een route: zit er een verboden
/// blok in, dan mag die route niet gereden worden. Elk verbod kan optioneel aan één
/// specifiek treintype gekoppeld worden (Item.Treintype=null = geldt voor alle treintypes).
/// </summary>
public class StopverbodBeheerder
{
    public List<StopverbodItem> Verboden { get; } = new();

    public void ToevoegenVerbod(Blok blok, Treintype? treintype = null)
    {
        if (!Verboden.Any(v => v.Blok == blok && v.Treintype == treintype))
            Verboden.Add(new StopverbodItem { Blok = blok, Treintype = treintype });
    }

    public void VerwijderVerbod(StopverbodItem verbod) => Verboden.Remove(verbod);

    public bool IsStoppenToegestaan(Blok blok, Treintype? treintype = null) =>
        !Verboden.Any(v => v.Blok == blok && (v.Treintype == null || v.Treintype == treintype));

    /// <summary>Het eerste verboden blok in dit pad voor dit treintype, of null als het pad
    /// geen enkel (voor dit treintype geldend) verboden stopblok bevat.</summary>
    public Blok? EersteConflict(IEnumerable<Blok> pad, Treintype? treintype = null) =>
        pad.FirstOrDefault(b => !IsStoppenToegestaan(b, treintype));

    public void VergeetBlok(Blok blok) => Verboden.RemoveAll(v => v.Blok == blok);

    public void VergeetTreintype(Treintype treintype) => Verboden.RemoveAll(v => v.Treintype == treintype);

    public void Reset() => Verboden.Clear();
}
