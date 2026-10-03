using Modeltreinbesturing.Model;

namespace Modeltreinbesturing;

/// <summary>Beheert de richtingsverboden en kan toetsen of een overgang is toegestaan.</summary>
public class RichtingsverbodBeheerder
{
    public List<Richtingsverbod> Richtingsverboden { get; } = new();

    public Richtingsverbod NieuwVerbod(Blok van, Blok naar, Blok? uit, Treintype? treintype = null)
    {
        var verbod = new Richtingsverbod { Van = van, Naar = naar, Uit = uit, Treintype = treintype };
        Richtingsverboden.Add(verbod);
        return verbod;
    }

    public void VerwijderVerbod(Richtingsverbod verbod) => Richtingsverboden.Remove(verbod);

    /// <summary>Is de overgang vorige -> van -> naar toegestaan voor dit treintype? vorige
    /// mag null zijn (bijv. het startblok van een route heeft geen "vorige"). treintype mag
    /// ook null zijn (onbekend/niet meegegeven) - dan tellen alleen de verboden die voor
    /// ALLE treintypes gelden (Treintype=null op het verbod zelf).</summary>
    public bool IsToegestaan(Blok? vorige, Blok van, Blok naar, Treintype? treintype = null) =>
        !Richtingsverboden.Any(v => v.Van == van && v.Naar == naar && (v.Uit == null || v.Uit == vorige) && (v.Treintype == null || v.Treintype == treintype));

    /// <summary>Ruimt verboden op die een verwijderd blok gebruiken.</summary>
    public void VergeetBlok(Blok blok) =>
        Richtingsverboden.RemoveAll(v => v.Van == blok || v.Naar == blok || v.Uit == blok);

    public void VergeetTreintype(Treintype treintype) => Richtingsverboden.RemoveAll(v => v.Treintype == treintype);

    public void Reset() => Richtingsverboden.Clear();
}
