using Modeltreinbesturing.Model;

namespace Modeltreinbesturing;

/// <summary>Beheert de individuele treinen (elk met een eigen lengte), los van de
/// treintypes (die alleen de snelheden bepalen).</summary>
public class TreinBeheerder
{
    public List<Trein> Treinen { get; } = new();

    /// <summary>Live snelheid (km/u) tijdens het rijden - 0 als de trein stilstaat/niet
    /// rijdt. Bijgehouden door TreinrouteWindow's simulator, hier centraal opgeslagen (i.p.v.
    /// alleen binnen dat ene venster) zodat ook een los "Overzicht locomotieven"-venster dit
    /// kan tonen zonder dat TreinrouteWindow per se open hoeft te staan.</summary>
    private readonly Dictionary<Trein, double> _snelheid = new();

    /// <summary>Live snelheid van deze trein - direct bijgehouden, of (bij dubbeltractie)
    /// gespiegeld van de "baas" waar deze trein als "knecht" aan gekoppeld is (alleen de
    /// baas rijdt daadwerkelijk een route, dus alleen daar wordt de snelheid rechtstreeks
    /// bijgewerkt) - zonder deze spiegeling zou een meerijdende knecht in het overzicht
    /// altijd als "Handmatig" i.p.v. "Rijdend" getoond worden.</summary>
    public double HuidigeSnelheid(Trein trein)
    {
        if (_snelheid.TryGetValue(trein, out var s)) return s;
        var baas = Treinen.FirstOrDefault(t => t.GekoppeldeKnecht == trein);
        return baas != null ? HuidigeSnelheid(baas) : 0;
    }

    public void ZetSnelheid(Trein trein, double snelheidKmU) => _snelheid[trein] = snelheidKmU;

    public Trein NieuweTrein(string omschrijving) 
    {
        var trein = new Trein { Omschrijving = omschrijving };
        Treinen.Add(trein);
        return trein;
    }

    public void VerwijderTrein(Trein trein)
    {
        Treinen.Remove(trein);
        _snelheid.Remove(trein);
        // Als deze trein als "knecht" aan een andere trein gekoppeld was, die koppeling
        // ook opruimen - anders blijft de "baas" verwijzen naar een trein die niet meer bestaat.
        foreach (var baas in Treinen)
            if (baas.GekoppeldeKnecht == trein) baas.GekoppeldeKnecht = null;
    }

    /// <summary>Ruimt verwijzingen naar een verwijderd treintype op.</summary>
    public void VergeetTreintype(Treintype type)
    {
        foreach (var trein in Treinen)
            if (trein.Treintype == type) trein.Treintype = null;
    }

    public void Reset()
    {
        Treinen.Clear();
        _snelheid.Clear();
    }
}
