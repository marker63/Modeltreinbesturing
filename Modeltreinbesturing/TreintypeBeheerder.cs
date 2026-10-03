using Modeltreinbesturing.Model;

namespace Modeltreinbesturing;

/// <summary>Beheert de treintypes. Wordt gevuld met een paar herkenbare standaardtypes
/// (zoals in Koploper's eigen demo-database gezien: Rangeren, Goederentrein licht/zwaar,
/// Intercity, Personen) zodat er meteen iets bruikbaars staat.</summary>
public class TreintypeBeheerder
{
    public List<Treintype> Treintypes { get; } = new();

    /// <summary>Koploper's "Algemeen -> Instellingen per database -> Algemeen": welk
    /// treintype als HET rangeertreintype fungeert. Activeert een gebruiker "Rangeer" op
    /// een loc (Trein.Rangeren=true), dan gebruikt de simulator VOOR DIE RIT de snelheden
    /// van dit treintype i.p.v. de eigen route/treintype-snelheid van de trein - meestal
    /// een stuk langzamer, precies zoals een rangeerbeweging in het echt ook trager gaat
    /// dan een gewone rit. Null = geen koppeling, dan blijft Rangeren alleen "geen
    /// massasimulatie" betekenen (het oude, eenvoudigere gedrag).</summary>
    public Treintype? RangeerTreintype { get; set; }

    public TreintypeBeheerder()
    {
        VulStandaardTypesIndienLeeg();
    }

    public void VulStandaardTypesIndienLeeg()
    {
        if (Treintypes.Count > 0) return;
        var rangeren = new Treintype { Omschrijving = "Rangeren", MaxSnelheid = 20, GemiddeldeSnelheid = 15, MinimumSnelheid = 5 };
        Treintypes.Add(rangeren);
        Treintypes.Add(new Treintype { Omschrijving = "Goederentrein licht", MaxSnelheid = 80, GemiddeldeSnelheid = 60, MinimumSnelheid = 20 });
        Treintypes.Add(new Treintype { Omschrijving = "Goederentrein zwaar", MaxSnelheid = 60, GemiddeldeSnelheid = 40, MinimumSnelheid = 15 });
        Treintypes.Add(new Treintype { Omschrijving = "Intercity", MaxSnelheid = 140, GemiddeldeSnelheid = 100, MinimumSnelheid = 30 });
        Treintypes.Add(new Treintype { Omschrijving = "Personentrein", MaxSnelheid = 100, GemiddeldeSnelheid = 70, MinimumSnelheid = 20 });
        // Net als het echte Koploper: het meegeleverde "Rangeren"-treintype is meteen ook
        // het aangewezen rangeertreintype, zodat dit direct werkt zonder eerst apart
        // ingesteld te hoeven worden.
        RangeerTreintype = rangeren;
    }

    public Treintype NieuwTreintype(string omschrijving)
    {
        var type = new Treintype { Omschrijving = omschrijving };
        Treintypes.Add(type);
        return type;
    }

    public void VerwijderTreintype(Treintype type)
    {
        Treintypes.Remove(type);
        if (RangeerTreintype == type) RangeerTreintype = null;
    }

    /// <summary>Wist alle treintypes - gebruikt bij "Nieuw project" (de standaardtypes komen
    /// er daarna weer automatisch bij, net als een nieuwe Koploper-database).</summary>
    public void Reset()
    {
        Treintypes.Clear();
        VulStandaardTypesIndienLeeg();
    }
}
