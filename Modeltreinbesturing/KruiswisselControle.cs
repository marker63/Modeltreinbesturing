using Modeltreinbesturing.Model;
using Baanverkenner.Kern;

namespace Modeltreinbesturing;

/// <summary>
/// BUG #91. Legt de kruiswisselstanden (motor 13 en 10) van de wisselstraten naast de kruiswissel-referentie uit de Baanverkenner
/// (<see cref="KruiswisselReferentie"/>, bewezen uit de baankaart en de logs). Puur SIGNALEREND: er wordt NIETS aangepast, want de
/// standen die Marco veldgetest heeft blijven leidend (zie KRUISWISSEL.md). Een verschil kan betekenen dat de ene bron fout is, maar
/// ook dat de motoren in de wisselstraat een omgekeerde polariteit hebben; dat moet Marco bevestigen.
/// </summary>
public static class KruiswisselControle
{
    private static string St(WisselStand s) => s == WisselStand.Afbuigend ? "afbuigend" : "rechtdoor";

    public static List<BaanControleMelding> Controleer(IEnumerable<Wisselstraat> wisselstraten)
    {
        var meldingen = new List<BaanControleMelding>();
        foreach (var w in wisselstraten.OrderBy(x => x.Van.Nummer).ThenBy(x => x.Naar.Nummer).ThenBy(x => x.Volgnummer))
        {
            var verb = KruiswisselReferentie.ZoekOpBlok(w.Van.Nummer, w.Naar.Nummer);
            if (verb is not { Bewezen: true }) continue;
            foreach (var kw in w.Kruiswisselstanden)
            {
                if (kw.Kruiswissel.Adres != KruiswisselReferentie.Motor13 || kw.Kruiswissel.Adres2 != KruiswisselReferentie.Motor10) continue;
                var verschillen = new List<string>();
                if (verb.Afbuigend13 is bool a13 && (kw.StandAdres == WisselStand.Afbuigend) != a13)
                    verschillen.Add($"motor 13 staat in de wisselstraat {St(kw.StandAdres)}, de referentie zegt {(a13 ? "afbuigend" : "rechtdoor")}");
                if (verb.Afbuigend10 is bool a10 && (kw.StandAdres2 == WisselStand.Afbuigend) != a10)
                    verschillen.Add($"motor 10 staat in de wisselstraat {St(kw.StandAdres2)}, de referentie zegt {(a10 ? "afbuigend" : "rechtdoor")}");
                if (verschillen.Count == 0) continue;
                meldingen.Add(new BaanControleMelding("Info",
                    $"Kruiswissel in wisselstraat blok {w.Van.Nummer} -> {w.Naar.Nummer} (#{w.Volgnummer}) wijkt af van de kruiswissel-referentie: {string.Join("; ", verschillen)}. " +
                    "Er is niets aangepast; de veldgetest stand blijft leidend. Controleer op de baan welke van de twee klopt (mogelijk omgekeerde polariteit van de motor)."));
            }
        }
        return meldingen;
    }
}
