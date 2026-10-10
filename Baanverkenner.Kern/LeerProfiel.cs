using System.Text.Json;

namespace Baanverkenner.Kern;

/// <summary>
/// BUG #59 (zelflerend): onthoudt hoe lang de testloc over elke overgang tussen twee melders doet,
/// omgerekend naar snelheid (seconden x snelheidsstap), zodat een volgende verkenning van dezelfde baan
/// meteen realistische wachttijden en kruiptijden heeft, ook bij een andere verkensnelheid.
/// Het profiel is alleen een hulpmiddel voor tijden: een verkeerde of verouderde waarde kan hooguit
/// een wachttijd verlengen of inkorten (begrensd door Min/Max in de instellingen), nooit de baankaart zelf veranderen.
/// </summary>
public class LeerProfiel
{
    public class Meting
    {
        public int Van { get; set; }
        public int Naar { get; set; }
        public Richting Richting { get; set; }
        /// <summary>Gemiddelde seconden x snelheidsstap (dus onafhankelijk van de snelheid).</summary>
        public double SecondenXStap { get; set; }
        public int Aantal { get; set; }
    }

    public int FormaatVersie { get; set; } = 1;

    /// <summary>BUG #62: welk Dinamo-blok een melder de vorige keer voedde. Alleen een hint voor de blokproef (die blijft
    /// altijd gewoon controleren), zodat het eerste blok dat geprobeerd wordt meestal al goed is.</summary>
    public class BlokHintItem { public int Melder { get; set; } public int Blok { get; set; } }
    public List<BlokHintItem> BlokHints { get; set; } = new();
    public int? BlokHint(int melder) => BlokHints.FirstOrDefault(h => h.Melder == melder)?.Blok;
    public List<Meting> Overgangen { get; set; } = new();

    /// <summary>Verwachte seconden voor deze overgang bij <paramref name="snelheid"/>, of null als onbekend.</summary>
    public double? Verwacht(int van, int naar, Richting r, int snelheid)
    {
        var m = Overgangen.FirstOrDefault(x => x.Van == van && x.Naar == naar && x.Richting == r && x.Aantal > 0);
        return m is null || snelheid <= 0 ? null : m.SecondenXStap / snelheid;
    }

    /// <summary>Langste verwachte seconden bij <paramref name="snelheid"/> over alle bekende overgangen (0 = niets bekend).</summary>
    public double LangsteVerwacht(int snelheid)
        => snelheid <= 0 ? 0 : Overgangen.Where(x => x.Aantal > 0).Select(x => x.SecondenXStap / snelheid).DefaultIfEmpty(0).Max();

    public int AantalMetingen => Overgangen.Sum(x => x.Aantal);

    /// <summary>Neemt de gemeten overgangen van een baankaart over. De tijden in de kaart gelden voor
    /// <paramref name="kaartSnelheid"/>. Een bestaande waarde wordt alleen vervangen door een kaart met MEER metingen,
    /// zodat dezelfde kaart meermaals aanbieden niets dubbel telt.</summary>
    /// <summary>Neemt alleen de melder-blokkoppelingen van een kaart over (ook van een kaart zonder bruikbare tijden).</summary>
    public void LeerBlokHints(Baankaart kaart)
    {
        foreach (var mm in kaart.Melders.Where(x => x.DinamoBlok is not null))
        {
            var h = BlokHints.FirstOrDefault(x => x.Melder == mm.Nummer);
            if (h is null) BlokHints.Add(new BlokHintItem { Melder = mm.Nummer, Blok = mm.DinamoBlok!.Value });
            else h.Blok = mm.DinamoBlok!.Value;
        }
    }

    public void Leer(Baankaart kaart, int kaartSnelheid)
    {
        if (kaartSnelheid <= 0) return;
        LeerBlokHints(kaart);
        foreach (var o in kaart.Overgangen.Where(o => o.AantalMetingen > 0 && o.GemiddeldeSeconden > 0))
        {
            var m = Overgangen.FirstOrDefault(x => x.Van == o.Van && x.Naar == o.Naar && x.Richting == o.Richting);
            if (m is null)
            {
                Overgangen.Add(new Meting { Van = o.Van, Naar = o.Naar, Richting = o.Richting, SecondenXStap = o.GemiddeldeSeconden * kaartSnelheid, Aantal = o.AantalMetingen });
            }
            else if (o.AantalMetingen >= m.Aantal)
            {
                m.SecondenXStap = o.GemiddeldeSeconden * kaartSnelheid;
                m.Aantal = o.AantalMetingen;
            }
        }
    }

    public string AlsJson() => JsonSerializer.Serialize(this, Baankaart.JsonOpties);
    public static LeerProfiel? VanJson(string json) => JsonSerializer.Deserialize<LeerProfiel>(json, Baankaart.JsonOpties);

    /// <summary>Laadt een profiel; een ontbrekend of kapot bestand geeft een leeg profiel (leren is nooit verplicht).</summary>
    public static LeerProfiel Laad(string pad)
    {
        try { if (File.Exists(pad)) return VanJson(File.ReadAllText(pad)) ?? new LeerProfiel(); }
        catch { }
        return new LeerProfiel();
    }

    public void Bewaar(string pad)
    {
        var map = Path.GetDirectoryName(pad);
        if (!string.IsNullOrEmpty(map)) Directory.CreateDirectory(map);
        File.WriteAllText(pad, AlsJson());
    }
}
