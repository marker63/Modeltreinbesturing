using Modeltreinbesturing.Model;

namespace Modeltreinbesturing;

/// <summary>
/// Stopverbod stilstand: verbiedt dat een route eindigt (stil blijft staan) op blok Naar,
/// als de trein daarvoor via blok Van kwam (optioneel alleen als hij uit Uit kwam) - het
/// DOORRIJDEN via die overgang blijft gewoon toegestaan, alleen het STILSTAAN als
/// eindbestemming niet. Hergebruikt de Richtingsverbod-vorm (Van/Naar/Uit), want dat is
/// exact hetzelfde soort regel als in Koploper's eigen "Stopverbod stilstand"-tabblad.
/// </summary>
public class StopverbodStilstandBeheerder
{
    public List<Richtingsverbod> Verboden { get; } = new();

    public Richtingsverbod NieuwVerbod(Blok van, Blok naar, Blok? uit, Treintype? treintype = null)
    {
        var verbod = new Richtingsverbod { Van = van, Naar = naar, Uit = uit, Treintype = treintype };
        Verboden.Add(verbod);
        return verbod;
    }

    public void VerwijderVerbod(Richtingsverbod verbod) => Verboden.Remove(verbod);

    /// <summary>Mag de trein op blok naar blijven stilstaan, gegeven dat hij van vorige
    /// naar van reed en vervolgens naar naar, voor dit treintype? (vorige mag null zijn.)</summary>
    public bool IsStilstaanToegestaan(Blok? vorige, Blok van, Blok naar, Treintype? treintype = null) =>
        !Verboden.Any(v => v.Van == van && v.Naar == naar && (v.Uit == null || v.Uit == vorige) && (v.Treintype == null || v.Treintype == treintype));

    public void VergeetBlok(Blok blok) => Verboden.RemoveAll(v => v.Van == blok || v.Naar == blok || v.Uit == blok);

    public void VergeetTreintype(Treintype treintype) => Verboden.RemoveAll(v => v.Treintype == treintype);

    public void Reset() => Verboden.Clear();
}
