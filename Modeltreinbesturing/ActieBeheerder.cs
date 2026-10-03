using System.Windows.Threading;
using Modeltreinbesturing.Model;

namespace Modeltreinbesturing;

/// <summary>
/// Beheert de blok-acties (zie Model.BlokActie) en past ze toe zodra een blok van bezet-
/// status wisselt. Abonneert zich op BlokBeheerder.BlokBezetVeranderd (een gerichte variant
/// van het bestaande, generieke BezettingGewijzigd-event, die WEL het gewijzigde blok en de
/// nieuwe stand meegeeft - nodig om te kunnen bepalen welke actie(s) moeten afgaan).
/// </summary>
public class ActieBeheerder
{
    public List<BlokActie> Acties { get; } = new();

    public void NieuweActie(Blok triggerBlok, BlokActieGebeurtenis gebeurtenis, Schakelaar doelSchakelaar, bool nieuweStand, double terugzettenNaSeconden = 0)
    {
        Acties.Add(new BlokActie { TriggerBlok = triggerBlok, Gebeurtenis = gebeurtenis, DoelSchakelaar = doelSchakelaar, NieuweStand = nieuweStand, TerugzettenNaSeconden = terugzettenNaSeconden });
    }

    public void VerwijderActie(BlokActie actie) => Acties.Remove(actie);

    /// <summary>Wordt aangeroepen bij elke bezet-statuswijziging (zie BlokBeheerder.
    /// BlokBezetVeranderd) - zet de gekoppelde schakelaar(s) om als er een passende actie
    /// bestaat voor dit blok en deze gebeurtenis.</summary>
    public void Verwerk(Blok blok, bool bezet)
    {
        var gebeurtenis = bezet ? BlokActieGebeurtenis.Bezet : BlokActieGebeurtenis.Vrij;
        foreach (var actie in Acties.Where(a => a.TriggerBlok == blok && a.Gebeurtenis == gebeurtenis))
        {
            actie.DoelSchakelaar.Aan = actie.NieuweStand;
            PlanTerugzetten(actie);
        }
    }

    /// <summary>Zelfde als Verwerk hierboven, maar dan voor RESERVERING i.p.v. echte
    /// bezetting (zie BlokBeheerder.BlokGereserveerdVeranderd) - voor bijv. een overweg die
    /// al dicht moet gaan zodra een route dit blok claimt, ruim vóór de trein er is.</summary>
    public void VerwerkReservering(Blok blok, bool gereserveerd)
    {
        var gebeurtenis = gereserveerd ? BlokActieGebeurtenis.Gereserveerd : BlokActieGebeurtenis.NietMeerGereserveerd;
        foreach (var actie in Acties.Where(a => a.TriggerBlok == blok && a.Gebeurtenis == gebeurtenis))
        {
            actie.DoelSchakelaar.Aan = actie.NieuweStand;
            PlanTerugzetten(actie);
        }
    }

    /// <summary>Koploper-praktijkvoorbeeld (RhB-Modelbaan, stationsomroep): een schakelaar
    /// die een externe geluidsmodule aanstuurt moet vaak een KORTE PULS zijn i.p.v. een
    /// blijvende stand, omdat veel geluidsmodules/relais op een momentane schakeling
    /// reageren, niet op een blijvend signaal. Plant, als TerugzettenNaSeconden>0 staat,
    /// een eenmalige timer die de schakelaar na die tijd weer terugzet naar de
    /// TEGENOVERGESTELDE stand van NieuweStand.</summary>
    private void PlanTerugzetten(BlokActie actie)
    {
        if (actie.TerugzettenNaSeconden <= 0) return;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(actie.TerugzettenNaSeconden) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            actie.DoelSchakelaar.Aan = !actie.NieuweStand;
        };
        timer.Start();
    }

    /// <summary>Ruimt acties op die een verwijderd blok gebruiken.</summary>
    public void VergeetBlok(Blok blok) => Acties.RemoveAll(a => a.TriggerBlok == blok);

    /// <summary>Ruimt acties op die een verwijderde schakelaar gebruiken.</summary>
    public void VergeetSchakelaar(Schakelaar schakelaar) => Acties.RemoveAll(a => a.DoelSchakelaar == schakelaar);

    public void Reset() => Acties.Clear();
}
