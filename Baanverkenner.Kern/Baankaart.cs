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

    /// <summary>Maakt de configuratie terug uit een Sleutel (BUG #74).</summary>
    public static Configuratie VanSleutel(string sleutel) =>
        new(sleutel.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(int.Parse).ToArray());

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

/// <summary>BUG #74: een DOORGANG door een melder: de loc kwam vanaf melder <see cref="Via"/> melder <see cref="Melder"/> binnen
/// (rijdend in <see cref="Richting"/>) en reed daarna door naar <see cref="Volgende"/>, met de wissels in
/// <see cref="Configuraties"/>. Bij een gewone melder is dat altijd hetzelfde; bij een kruiswissel-sectie (Engelse wissel in één melder)
/// hangt het vervolg af van de ingangskant (Via) én de wisselstanden. Een doorgang geldt ook omgekeerd (Volgende -> Melder -> Via,
/// andere richting), met dezelfde wisselstanden.</summary>
public class Doorgang
{
    public int Via { get; set; }
    public int Melder { get; set; }
    public int Volgende { get; set; }
    public Richting Richting { get; set; }
    public int AantalKeer { get; set; }
    public List<string> Configuraties { get; set; } = new();
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
    /// <summary>BUG #43: de Dinamo-blokken waarvoor de centrale op het moment van de
    /// kortsluiting zelf een kortsluiting-alarm meldde (Block Alarm). Leeg als de
    /// hardware dit niet meldt (andere centrale, of alleen via het F-bit).</summary>
    public List<int> AlarmBlokken { get; set; } = new();
    /// <summary>BUG #75: de andere melder(s) die op het moment van de kortsluiting bezet waren (naast NaMelder): daar stond de loc
    /// met een deel al in. Wijst naar de wissel die erbij betrokken is.</summary>
    public List<int> TweedeMelders { get; set; } = new();
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
    /// <summary>BUG #59: de verkensnelheid waarop de tijden in Overgangen (GemiddeldeSeconden) zijn omgerekend (0 = onbekend/oudere kaart).</summary>
    public int RefSnelheid { get; set; }
    public int StartMelder { get; set; }
    /// <summary>BUG #74: startmelders van eerdere deelverkenningen als de kaart is uitgebreid met een tweede, los traject
    /// (zie <see cref="VerkenStatus.VoorUitbreiding"/>). <see cref="StartMelder"/> is altijd de meest recente.</summary>
    public List<int> EerdereStartMelders { get; set; } = new();
    public int WisselAdresVan { get; set; }
    public int WisselAdresTot { get; set; }

    public List<MelderInfo> Melders { get; set; } = new();
    public List<Overgang> Overgangen { get; set; } = new();
    public List<WisselVondst> Wissels { get; set; } = new();
    public List<Eindpunt> Kopsporen { get; set; } = new();
    public List<Traject> Trajecten { get; set; } = new();
    public List<Kortsluitpunt> Kortsluitpunten { get; set; } = new();
    /// <summary>BUG #74: per melder welke ingangskant (Via) bij welk vervolg (Volgende) hoort, met de wisselstanden. Oudere kaarten hebben
    /// dit veld niet (leeg).</summary>
    public List<Doorgang> Doorgangen { get; set; } = new();
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
        // BUG #55: een overgang vanaf deze melder in deze richting bewijst dat het (bij deze wissels) geen kopspoor is.
        Kopsporen.RemoveAll(k => k.Melder == van && k.Richting == r && k.Configuratie == c.ToString());
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

    /// <summary>BUG #74: legt de doorgang via -> melder -> volgende vast, en meteen ook de omgekeerde (volgende -> melder -> via).</summary>
    public void RegistreerDoorgang(int via, int melder, int volgende, Richting r, Configuratie c)
    {
        Zet(via, melder, volgende, r);
        Zet(volgende, melder, via, r.Om());

        void Zet(int v, int m, int n, Richting richting)
        {
            var d = Doorgangen.FirstOrDefault(x => x.Via == v && x.Melder == m && x.Volgende == n && x.Richting == richting);
            if (d is null)
            {
                d = new Doorgang { Via = v, Melder = m, Volgende = n, Richting = richting };
                Doorgangen.Add(d);
            }
            d.AantalKeer++;
            if (!d.Configuraties.Contains(c.Sleutel)) d.Configuraties.Add(c.Sleutel);
        }
    }

    /// <summary>BUG #74: welke wisselstand (configuratie) moet de loc voor de EERSTE stap hebben als hij op <paramref name="melder"/> staat,
    /// daar vanaf <paramref name="via"/> binnenkwam en in <paramref name="r"/> naar <paramref name="volgende"/> wil rijden? Is voor deze
    /// ingangskant gezien dat <paramref name="gewenst"/> ergens ANDERS heen leidt, en is er een andere stand waarvan bevestigd is dat hij naar
    /// <paramref name="volgende"/> leidt, dan komt die stand terug (de stand die het dichtst bij de gewenste ligt). In alle andere gevallen
    /// blijft <paramref name="gewenst"/> gelden: zonder bewijs verandert er niets.</summary>
    public Configuratie KiesConfiguratieBijIngang(int via, int melder, Richting r, int volgende, Configuratie gewenst)
    {
        var bij = Doorgangen.Where(d => d.Via == via && d.Melder == melder && d.Richting == r).ToList();
        if (bij.Count == 0) return gewenst;
        bool gewenstLeidtElders = bij.Any(d => d.Volgende != volgende && d.Configuraties.Contains(gewenst.Sleutel));
        bool gewenstBevestigd = bij.Any(d => d.Volgende == volgende && d.Configuraties.Contains(gewenst.Sleutel));
        if (!gewenstLeidtElders || gewenstBevestigd) return gewenst;
        var kandidaten = bij.Where(d => d.Volgende == volgende).SelectMany(d => d.Configuraties).Distinct()
            .Select(Configuratie.VanSleutel).ToList();
        if (kandidaten.Count == 0) return gewenst;
        int Verschil(Configuratie k) => k.Afbuigend.Except(gewenst.Afbuigend).Count() + gewenst.Afbuigend.Except(k.Afbuigend).Count();
        return kandidaten.OrderBy(Verschil).ThenBy(k => k.Sleutel, StringComparer.Ordinal).First();
    }

    /// <summary>BUG #75: bij een kortsluiting na <paramref name="naMelder"/> was ook <paramref name="tweede"/> bezet. Is de overgang tussen die
    /// twee melders (in welke richting ook) eerder bij een andere wisselstand gereden, dan zijn de wissels die in die stand anders staan dan in
    /// <paramref name="kortsluitStand"/> de eerste verdachten: (adres, afbuigend) = de stand die de kortsluiting zou kunnen oplossen.</summary>
    public List<(int Adres, bool Afbuigend)> HintsBijTweedeMelder(int naMelder, IEnumerable<int> tweede, Configuratie kortsluitStand)
    {
        var res = new List<(int, bool)>();
        foreach (var t in tweede.Where(t => t != naMelder))
            foreach (var o in Overgangen.Where(o => (o.Van == naMelder && o.Naar == t) || (o.Van == t && o.Naar == naMelder)))
                foreach (var sleutel in o.GezienBijConfiguraties)
                {
                    var gezien = Configuratie.VanSleutel(sleutel);
                    foreach (var a in gezien.Afbuigend.Except(kortsluitStand.Afbuigend)) if (!res.Contains((a, true))) res.Add((a, true));
                    foreach (var a in kortsluitStand.Afbuigend.Except(gezien.Afbuigend)) if (!res.Contains((a, false))) res.Add((a, false));
                }
        return res;
    }

    /// <summary>BUG #74: melders waar, in dezelfde richting, meer dan één combinatie ingangskant/vervolg is gezien (kruising, of een
    /// wissel die van twee kanten bereden is). Voor het rapport.</summary>
    public List<IGrouping<int, Doorgang>> OpvallendeDoorgangen() =>
        Doorgangen.GroupBy(d => (d.Melder, d.Richting)).Where(g => g.Count() > 1)
            .SelectMany(g => g).GroupBy(d => d.Melder).OrderBy(g => g.Key).ToList();

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

    /// <summary>BUG #64: de melders die na <paramref name="van"/> in richting <paramref name="r"/> volgen, volgens wat al
    /// eens is gereden. Een gevonden overgang X→Y (r) geldt met dezelfde wisselstand ook omgekeerd: Y→X (andersom). Zonder
    /// <paramref name="c"/> telt elke wisselstand mee (rapport); met <paramref name="c"/> alleen die stand.</summary>
    public List<int> BekendeVolgende(int van, Richting r, Configuratie? c = null)
    {
        bool Past(Overgang o) => c is null || o.GezienBijConfiguraties.Contains(c.Sleutel);
        var res = Overgangen.Where(o => o.Van == van && o.Richting == r && Past(o)).Select(o => o.Naar)
            .Concat(Overgangen.Where(o => o.Naar == van && o.Richting == r.Om() && Past(o)).Select(o => o.Van));
        return res.Distinct().OrderBy(x => x).ToList();
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
