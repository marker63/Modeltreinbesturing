using Modeltreinbesturing.Model;

namespace Modeltreinbesturing;

/// <summary>Beheert de blokgroepen ("Onderhouden blokgroepen" in Koploper).</summary>
public class BlokgroepBeheerder
{
    public List<Blokgroep> Blokgroepen { get; } = new();

    /// <summary>Bij welke groep (indien van toepassing) hoort dit blok? Een blok zit in
    /// hooguit één groep tegelijk in onze eenvoudige implementatie.</summary>
    public Blokgroep? GroepVan(Blok blok) => Blokgroepen.FirstOrDefault(g => g.Blokken.Contains(blok));

    public void Reset() => Blokgroepen.Clear();
}
