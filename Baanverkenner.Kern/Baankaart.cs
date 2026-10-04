using System.Text.Json;
using System.Text.Json.Serialization;

namespace Baanverkenner.Kern;

/// <summary>Rijrichting ten opzichte van de DECODER van de testloc (dus niet "links/rechts"
/// op de baan). Alles in de baankaart is in deze termen vastgelegd.</summary>
public enum Richting { Vooruit, Achteruit }

public static class RichtingHulp
{
    public static Richting Om(this Richting r) => r == Richting.Vooruit ? Richting.Achteruit : Richting.Vooruit;
    public static string Tekst(this Richting r) => r == Richting.Vooruit ? "vooruit" : "achteruit";
    public static string Pijl(this Richting r) => r == Richting.Vooruit ? "→" : "←";
}

/// <summary>Een wisselconfiguratie: welke adressen op AFBUIGEND staan (alle overige in het
/// geteste bereik staan op RECHTDOOR). Onveranderlijk; vergelijkbaar via Sleutel.</summary>
public sealed class Configuratie
{
    public static readonly Configuratie Basis = new(Array.Empty<int>());

    public int[] Afbuigend { get; }

    [JsonConstructor]
    public Configuratie(int[] afbuigend) { Afbuigend = afbuigend.Distinct().OrderBy(a => a).ToArray(); }

    public bool Bevat(int adres) => Array.BinarySearch(Afbuigend, adres) >= 0;
    public Configuratie Met(int adres) => new(Afbuigend.Append(adres).ToArray());
    public Configuratie Zonder(int adres) => new(Afbuigend.Where(a => a != adres).ToArray());
    public Configuratie MetStand(int adres, bool afbuigend) => afbuigend ? Met(adres) : Zonder(adres);

    [JsonIgnore]
    public string Sleutel => string.Join(",", Afbuigend);
    public override string ToString() => Afbuigend.Length == 0 ? "alle wissels rechtdoor" : "afbuigend: " + string.Join(", ", Afbuigend);
    public override bool Equals(object? obj) => obj is Configuratie c && c.Sleutel == Sleutel;
    public override int GetHashCode() => Sleutel.GetHashCode();
}

public class MelderInfo
{
    public int Nummer { get; set; }
    /// <summary>Dinamo-blok dat deze sectie van rijstroom voorziet (null = onbekend/niet van
    /// toepassing bij DCC-EX/Intellibox).</summary>
    public int? DinamoBlok { get; set; }
}

/// <summary>Een waargenomen melderovergang: na melder Van werd melder Naar bezet, rijdend
/// in Richting, met de wissels in Configuratie.</summary>
public class Overgang
{
    public int Van { get; set; }
    public int Naar { get; set; }
    public Richting Richting { get; set; }
    public int AantalKeer { get; set; }
    /// <summary>Gemiddelde tijd (s) tussen het bezet worden van Van en van Naar bij
    /// verkensnelheid - 0 als nooit ongestoord gemeten.</summary>
    public double GemiddeldeSeconden { get; set; }
    public int AantalMetingen { get; set; }
    public List<string> GezienBijConfiguraties { get; set; } = new();
}

public enum WaarnemingSoort
{
    /// <summary>Omzetten liet de loc na melder NaMelder een ANDERE kant op rijden: een
    /// wissel die van de puntzijde (splitsend) bereden wordt.</summary>
    Splitsing,
    /// <summary>Omzetten gaf kortsluiting direct na NaMelder: een wissel die van de
    /// achterkant (samenvoegend) bereden wordt in de verkeerde stand - of een splitsende
    /// wissel waarvan de afbuigende tak meteen op een verkeerd staande wissel stuit.</summary>
    KortsluitingBijOmzetten,
    /// <summary>Een eerder gevonden kortsluitpunt bleek met deze wissel in deze stand
    /// opgelost te worden.</summary>
    LostKortsluitingOp
}

public class WisselWaarneming
{
    public WaarnemingSoort Soort { get; set; }
    public int NaMelder { get; set; }
    public Richting Richting { get; set; }
    /// <summary>Volgende melder met dit adres op RECHTDOOR (null = onbekend/niet bereikt).</summary>
    public int? VolgendeBijRechtdoor { get; set; }
    /// <summary>Volgende melder met dit adres op AFBUIGEND (null = onbekend/kortsluiting).</summary>
    public int? VolgendeBijAfbuigend { get; set; }
    public string Configuratie { get; set; } = "";
    public string Toelichting { get; set; } = "";
}

public class WisselVondst
{
    public int Adres { get; set; }
    public List<WisselWaarneming> Waarnemingen { get; set; } = new();
}

public class Eindpunt
{
    public int Melder { get; set; }
    public Richting Richting { get; set; }
    public string Configuratie { get; set; } = "";
}

/// <summary>BUG #40: twee heel verschillende dingen werden allebei "kortsluitpunt" genoemd:
/// een echte elektrische kortsluiting (RitEinde.Kortsluiting - de hardware meldt storing), en
/// een onverwachte terugweg (TerugNaarMetControle: de loc kwam terug via een andere melder
/// dan hij heen ging). De hardware meldt daarbij GEEN kortsluiting - er is dus géén storing,
/// alleen een onvoorspelbare rijweg. De oorzaak is niet per se een wissel: op een recht stuk
/// spoor zonder wissel kan dit ook door een gemiste meting bij de heenrit ontstaan, dus deze
/// soort beweert niet welke oorzaak het is. Beide soorten worden nog wel op dezelfde manier
/// opgeslagen en vermeden (StopBijMelders), maar het rapport moet ze niet meer allebei
/// "kortsluiting" noemen.</summary>
public enum KortsluitpuntSoort { Kortsluiting, OnverwachteTerugweg }

public class Kortsluitpunt
{
    public int Id { get; set; }
    public int NaMelder { get; set; }
    public Richting Richting { get; set; }
    public KortsluitpuntSoort Soort { get; set; } = KortsluitpuntSoort.Kortsluiting;
    public Configuratie Configuratie { get; set; } = Configuratie.Basis;
    /// <summary>Hoe de loc hier komt (vanaf de startmelder).</summary>
    public List<Etappe> Route { get; set; } = new();
    public int AantalKortsluitingen { get; set; }
    public bool Opgelost { get; set; }
    public bool Opgegeven { get; set; }
    public int? OpgelostDoorAdres { get; set; }
    public bool? OpgelostMetAfbuigend { get; set; }
    public List<int> GeprobeerdeAdressen { get; set; } = new();
}

public enum RitEinde
{
    Doodlopend,          // geen nieuwe melder binnen de wachttijd: stootjuk/kopspoor
    Lus,                 // een eerder gepasseerde melder werd opnieuw bezet
    Kortsluiting,        // melders vielen weg / hardware meldde kortsluiting
    BekendKortsluitpunt, // bewust gestopt vóór een al bekend kortsluitpunt
    GelijkAanBasis,      // proefrit volgde de basisrit tot het einde: niets nieuws
    AfwijkingGevonden,   // proefrit week af van de basisrit en reed nog even door
    Maximum,             // veiligheidsgrens aantal melders
    DoelBereikt          // (navigatie) doelmelder bereikt
}

public class Traject
{
    public Configuratie Configuratie { get; set; } = Configuratie.Basis;
    public int Start { get; set; }
    public Richting Richting { get; set; }
    public List<int> Reeks { get; set; } = new();
    public RitEinde Einde { get; set; }
}

public class Etappe
{
    public Configuratie Configuratie { get; set; } = Configuratie.Basis;
    public Richting Richting { get; set; }
    public int Van { get; set; }
    public int Naar { get; set; }
    /// <summary>Verwachte melders van Van tot en met Naar.</summary>
    public List<int> Pad { get; set; } = new();
}

public class VoorgesteldBlok
{
    public int Nummer { get; set; }
    public List<int> Melders { get; set; } = new();
    public int? DinamoBlok { get; set; }
    public List<int> Buren { get; set; } = new();
}

/// <summary>Het resultaat van een verkenning: alles wat over de baan ontdekt is. Dit is
/// het bestand dat later in Modeltreinbesturing geïmporteerd kan worden.</summary>
public class Baankaart
{
    public int FormaatVersie { get; set; } = 1;
    public string Programma { get; set; } = "Baanverkenner";
    public DateTime Gestart { get; set; }
    public DateTime Bijgewerkt { get; set; }
    public bool Voltooid { get; set; }
    public string Hardware { get; set; } = "";
    public int LocAdres { get; set; }
    public int LocStappen { get; set; }
    public int StartMelder { get; set; }
    public int WisselAdresVan { get; set; }
    public int WisselAdresTot { get; set; }

    public List<MelderInfo> Melders { get; set; } = new();
    public List<Overgang> Overgangen { get; set; } = new();
    public List<WisselVondst> Wissels { get; set; } = new();
    public List<Eindpunt> Kopsporen { get; set; } = new();
    public List<Traject> Trajecten { get; set; } = new();
    public List<Kortsluitpunt> Kortsluitpunten { get; set; } = new();
    public List<int> AdressenZonderEffect { get; set; } = new();
    public List<VoorgesteldBlok> VoorgesteldeBlokken { get; set; } = new();
    public List<string> Waarschuwingen { get; set; } = new();

    // ---- Hulpmethoden voor het vastleggen ----

    public MelderInfo Melder(int nummer)
    {
        var m = Melders.FirstOrDefault(x => x.Nummer == nummer);
        if (m is null)
        {
            m = new MelderInfo { Nummer = nummer };
            Melders.Add(m);
            Melders.Sort((a, b) => a.Nummer.CompareTo(b.Nummer));
        }
        return m;
    }

    public void RegistreerOvergang(int van, int naar, Richting r, Configuratie c, double? seconden)
    {
        Melder(van); Melder(naar);
        var o = Overgangen.FirstOrDefault(x => x.Van == van && x.Naar == naar && x.Richting == r);
        if (o is null)
        {
            o = new Overgang { Van = van, Naar = naar, Richting = r };
            Overgangen.Add(o);
        }
        o.AantalKeer++;
        if (!o.GezienBijConfiguraties.Contains(c.Sleutel)) o.GezienBijConfiguraties.Add(c.Sleutel);
        if (seconden is double s && s > 0)
        {
            o.GemiddeldeSeconden = (o.GemiddeldeSeconden * o.AantalMetingen + s) / (o.AantalMetingen + 1);
            o.AantalMetingen++;
        }
    }

    public WisselVondst Wissel(int adres)
    {
        var w = Wissels.FirstOrDefault(x => x.Adres == adres);
        if (w is null)
        {
            w = new WisselVondst { Adres = adres };
            Wissels.Add(w);
            Wissels.Sort((a, b) => a.Adres.CompareTo(b.Adres));
        }
        return w;
    }

    public void RegistreerKopspoor(int melder, Richting r, Configuratie c)
    {
        if (!Kopsporen.Any(k => k.Melder == melder && k.Richting == r))
            Kopsporen.Add(new Eindpunt { Melder = melder, Richting = r, Configuratie = c.ToString() });
    }

    public double LangsteReistijd() => Overgangen.Where(o => o.AantalMetingen > 0).Select(o => o.GemiddeldeSeconden).DefaultIfEmpty(0).Max();

    // ---- Opslaan/laden ----

    public static readonly JsonSerializerOptions JsonOpties = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public string AlsJson() => JsonSerializer.Serialize(this, JsonOpties);
    public static Baankaart? VanJson(string json) => JsonSerializer.Deserialize<Baankaart>(json, JsonOpties);
}
