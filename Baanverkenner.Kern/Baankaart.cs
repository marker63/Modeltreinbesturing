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

/// <summary>BUG #104: het verslag van één controleronde (vergelijking van de nieuwe rit met de bestaande kaart).</summary>
public class ControleVerslag
{
    public DateTime Datum { get; set; }
    public string Samenvatting { get; set; } = "";
    public bool Verschillen { get; set; }
    public List<string> Regels { get; set; } = new();
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
/// <summary>BUG #87: een bewezen kortsluitoplossing: rijdend in richting <see cref="Richting"/> vanaf <see cref="Start"/> voorbij <see cref="NaMelder"/>
/// gaf de stand <see cref="Stand"/> kortsluiting, en wissel <see cref="Adres"/> op <see cref="Afbuigend"/> losse dat op. Wordt ook in het leerprofiel
/// bewaard, zodat een volgende verkenning dezelfde kortsluiting niet opnieuw opzoekt.</summary>
public class KortsluitOplossing
{
    public int NaMelder { get; set; }
    /// <summary>BUG #150: de melder waar de loc vandaan kwam (de ingang); 0 = onbekend. Met Via geldt de oplossing voor die ingang bij elke wisselstand.</summary>
    public int Via { get; set; }
    public Richting Richting { get; set; }
    public string Stand { get; set; } = "";
    public int Start { get; set; }
    public int Adres { get; set; }
    public bool Afbuigend { get; set; }
    public string Bron { get; set; } = "";
    public string Sleutel => $"{NaMelder}|{Richting}|{Stand}|{Start}|{Adres}|{Afbuigend}";
    public string IngangSleutel => $"{Via}>{NaMelder}|{Richting}|{Adres}|{Afbuigend}";
}

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
    /// <summary>BUG #93: dezelfde rit (zelfde start, richting en wisselstand) lukte bij herhaling zonder iets te veranderen: de kortsluiting was
    /// tijdelijk (bijv. een tong die te laat omschakelde) en wordt niet verder "opgelost".</summary>
    public bool Tijdelijk { get; set; }
    public int? OpgelostDoorAdres { get; set; }
    public bool? OpgelostMetAfbuigend { get; set; }
    public List<int> GeprobeerdeAdressen { get; set; } = new();
}

public enum RitEinde
{
    Doodlopend,          // geen nieuwe melder binnen de wachttijd: stootjuk/kopspoor
    Lus,                 // een eerder gepasseerde melder werd opnieuw bezet
    Kortsluiting,        // melders vielen weg / hardware meldde kortsluiting
    BekendKortsluitpunt, // bewust gestopt op de melder waarna een al bekend kortsluitpunt volgt
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
    /// <summary>BUG #88: gelijk aan het Dinamo-bloknummer als dat bekend en uniek is (anders een intern nummer, nooit getoond). Toon altijd <see cref="Label"/>.</summary>
    public int Nummer { get; set; }
    /// <summary>De naam die getoond wordt: het ECHTE Dinamo-bloknummer, of "onbekend (melder X)" als dat niet gevonden is. Geen verzonnen nummers.</summary>
    public string Label => DinamoBlok is int d ? (Nummer is >= 1000 and < 90000 ? $"{d} (deel {Nummer / 1000 + 1})" : d.ToString()) : $"onbekend (melder {(Melders.Count > 0 ? Melders[0].ToString() : "?")})";
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

    /// <summary>BUG #87: kortsluitoplossingen uit eerdere verkenningen (leerprofiel) of door de gebruiker aangewezen.</summary>
    public List<KortsluitOplossing> BekendeKortsluitOplossingen { get; set; } = new();
    /// <summary>BUG #129: Engelse wissels van deze verkenning (motoren, bijv. "13+10"), de melder(s) van de kruiswissel (opgegeven of ontdekt) en of de vaste referentie is gebruikt.</summary>
    public string KruisMotoren { get; set; } = "";
    public List<int> KruisMidden { get; set; } = new();
    public bool KruisReferentie { get; set; }

    private class OplossingItem { public Configuratie K = Configuratie.Basis; public int Via; public int NaMelder; public int Start; public int Adres; public bool Afbuigend; public string Label = ""; }

    private List<OplossingItem> Oplossingen(Richting r)
    {
        var res = new List<OplossingItem>();
        foreach (var k in Kortsluitpunten.Where(k => k.Opgelost && k.OpgelostDoorAdres is not null && k.OpgelostMetAfbuigend is not null && k.Richting == r && k.Configuratie.Afbuigend.Length > 0))
            res.Add(new OplossingItem
            {
                K = k.Configuratie, NaMelder = k.NaMelder, Via = k.Route.Count > 0 && k.Route[^1].Pad.Count > 1 && k.Route[^1].Pad[^1] == k.NaMelder ? k.Route[^1].Pad[^2] : 0, Adres = k.OpgelostDoorAdres!.Value, Afbuigend = k.OpgelostMetAfbuigend!.Value,
                Start = k.Route.Count > 0 && k.Route[^1].Pad.Count > 0 && k.Route[^1].Pad[^1] == k.NaMelder ? k.Route[^1].Pad[0] : 0,
                Label = $"kortsluitpunt #{k.Id}"
            });
        foreach (var o in BekendeKortsluitOplossingen.Where(o => o.Richting == r))
        {
            var cfg = Configuratie.VanSleutel(o.Stand);
            if (cfg.Afbuigend.Length == 0) continue;
            res.Add(new OplossingItem { K = cfg, Via = o.Via, NaMelder = o.NaMelder, Adres = o.Adres, Afbuigend = o.Afbuigend, Start = o.Start, Label = "een eerder gevonden oplossing" });
        }
        return res;
    }

    /// <summary>BUG #146 (Marco 17:50, kortsluitpunt #2): wissel 2 afbuigend alleen gaf kortsluiting na melder 12 (achteruit), terwijl dezelfde combinatie
    /// (2 met 1) na melder 5 (vooruit) al als overloop bewezen was. Een overloop-paar geldt in beide richtingen: is voor wissel <c>w</c> bewezen dat wissel
    /// <c>p</c> er afbuigend bij moet (een gereden oplossing van wissel <c>p</c>, of oplossingen in beide richtingen), dan krijgt een rit met <c>w</c>
    /// afbuigend ook <c>p</c> afbuigend. <paramref name="behalve"/> is het adres dat de opdracht zelf test.</summary>
    public Configuratie MetOverloopPartners(Configuratie c, int? behalve, KruisKennis? kruis, out List<string> wijz)
    {
        wijz = new List<string>();
        foreach (var k in Kortsluitpunten.Where(k => k.Soort == KortsluitpuntSoort.Kortsluiting && k.Opgelost && k.OpgelostMetAfbuigend == true
                                                     && k.OpgelostDoorAdres is not null && k.Configuratie.Afbuigend.Length == 1).OrderBy(k => k.Id))
        {
            int w = k.Configuratie.Afbuigend[0], p = k.OpgelostDoorAdres!.Value;
            if (!c.Bevat(w) || c.Bevat(p) || p == behalve || (kruis?.IsMotor(p) ?? false)) continue;
            bool gereden = Wissels.Any(x => x.Adres == p && x.Waarnemingen.Any(o => o.Soort == WaarnemingSoort.LostKortsluitingOp && o.Configuratie == k.Configuratie.ToString()));
            bool beideRichtingen = Kortsluitpunten.Any(o => o.Id != k.Id && o.Opgelost && o.OpgelostDoorAdres == p && o.OpgelostMetAfbuigend == true
                                                           && o.Configuratie.Equals(k.Configuratie) && o.Richting != k.Richting);
            if (!gereden && !beideRichtingen) continue;
            c = c.MetStand(p, true);
            wijz.Add($"{p} afbuigend (wissel {w} afbuigend heeft {p} als overloop-partner, kortsluitpunt #{k.Id})");
        }
        return c;
    }

    /// <summary>Alle bewezen oplossingen (uit opgeloste kortsluitpunten en bekende oplossingen), om in het leerprofiel te bewaren.</summary>
    public List<KortsluitOplossing> AlleKortsluitOplossingen()
    {
        var res = new List<KortsluitOplossing>();
        foreach (var r in new[] { Richting.Vooruit, Richting.Achteruit })
            foreach (var o in Oplossingen(r))
                res.Add(new KortsluitOplossing { NaMelder = o.NaMelder, Via = o.Via, Richting = r, Stand = o.K.Sleutel, Start = o.Start, Adres = o.Adres, Afbuigend = o.Afbuigend, Bron = o.Label });
        // BUG #124: oplossingen waarbij de oplossende wissel zelf in de geteste stand zit, komen niet in het leerprofiel (ze ontstonden uit wissels die onder de loc omgezet werden).
        return res.Where(x => !Configuratie.VanSleutel(x.Stand).Bevat(x.Adres)).GroupBy(x => x.Sleutel).Select(g => g.First()).ToList();
    }

    /// <summary>BUG #87: de gebruiker heeft aangewezen dat wissel <paramref name="adres"/> verkeerd stond en nu op <paramref name="afbuigend"/> staat;
    /// daarmee verdween de kortsluiting van <paramref name="kp"/>: dat is dus de oplossing van dat kortsluitpunt.</summary>
    public bool RegistreerHandmatigeOplossing(Kortsluitpunt kp, int adres, bool afbuigend, KruisKennis? kruis = null)
    {
        // BUG #137: een kruismotor kan alleen een kortsluiting oplossen waar de loc langs de kruiswissel komt.
        if (kruis is not null && !KruisOplossingMogelijk(kp, adres, kruis)) return false;
        // BUG #122: een wissel die zelf in de geteste stand zit terugzetten (bv. wissel 2 rechtdoor bij stand '2 afbuigend') is geen extra wissel
        // die de kortsluiting oplost, maar gewoon een andere stand; die zou later ten onrechte als 'oplossing' worden toegepast.
        if (kp.Opgelost || kp.Configuratie.Bevat(adres) || !afbuigend) return false;
        kp.Opgelost = true;
        kp.OpgelostDoorAdres = adres;
        kp.OpgelostMetAfbuigend = afbuigend;
        if (!kp.GeprobeerdeAdressen.Contains(adres)) kp.GeprobeerdeAdressen.Add(adres);
        return true;
    }

    /// <summary>BUG #137 (kaart 14:11): kortsluitpunt #1 (na melder 12, route 13-5-12) stond "opgelost door motor 10", terwijl die route niet langs de kruiswissel
    /// komt: de oplossing van een ander kortsluitpunt was aan het verkeerde punt gehangen (#132). Een kruismotor kan een kortsluiting alleen oplossen als de
    /// melder, de tweede melder of de route van het punt de kruiswissel raakt. Is de kruiswissel nog onbekend (geen melder), dan wordt niets afgekeurd.</summary>
    public static bool KruisOplossingMogelijk(Kortsluitpunt kp, int adres, KruisKennis kruis)
    {
        if (!kruis.IsMotor(adres) || kruis.Middens.Count == 0) return true;
        return kruis.Middens.Contains(kp.NaMelder) || kp.TweedeMelders.Any(kruis.Middens.Contains)
            || kp.Route.Any(e => e.Pad.Any(kruis.Middens.Contains));
    }

    /// <summary>BUG #137: heropent kortsluitpunten waarvan de "oplossing" een kruismotor is die daar onmogelijk iets kan oplossen (zie
    /// <see cref="KruisOplossingMogelijk"/>). Geeft per heropend punt een uitleg terug.</summary>
    public List<string> HerstelOnmogelijkeKruisOplossingen(KruisKennis kruis)
    {
        var res = new List<string>();
        foreach (var kp in Kortsluitpunten.Where(k => k.Opgelost && k.OpgelostDoorAdres is int a && !KruisOplossingMogelijk(k, a, kruis)).ToList())
        {
            int adres = kp.OpgelostDoorAdres!.Value;
            kp.Opgelost = false;
            kp.OpgelostDoorAdres = null;
            kp.OpgelostMetAfbuigend = null;
            kp.GeprobeerdeAdressen.Remove(adres);
            res.Add($"Kortsluitpunt #{kp.Id} (na melder {kp.NaMelder}, {kp.Richting.Tekst()}, {kp.Configuratie}) stond als opgelost door motor {adres}, maar die route komt niet langs de kruiswissel (melder {string.Join(", ", kruis.Middens.OrderBy(x => x))}); de oplossing hoorde bij een ander kortsluitpunt. Het punt staat weer open.");
        }
        return res;
    }

    /// <summary>BUG #87: een opdracht begint op <paramref name="start"/> in richting <paramref name="r"/> met stand <paramref name="c"/>. Is daar eerder
    /// een kortsluiting bij deze stand opgelost met een extra wissel, dan krijgt de ritstand die wissel meteen mee (anders volgt dezelfde kortsluiting opnieuw).</summary>
    public Configuratie MetBekendeOplossingenVoorStart(int start, Richting r, Configuratie c, out List<string> wijzigingen)
    {
        wijzigingen = new List<string>();
        // BUG #122: een toegepaste oplossing kan een volgende oplossing mogelijk maken (stand + extra wissel is zelf weer een geteste stand): herhalen tot er niets meer verandert.
        for (int ronde = 0; ronde < 5; ronde++)
        {
            bool veranderd = false;
            foreach (var o in Oplossingen(r).Where(o => (o.Start == start || BereiktMelder(start, o.NaMelder, r, c)) && !o.K.Bevat(o.Adres)))
            {
                if (!o.K.Afbuigend.All(c.Bevat) || c.Bevat(o.Adres) == o.Afbuigend) continue;
                c = c.MetStand(o.Adres, o.Afbuigend);
                veranderd = true;
                wijzigingen.Add($"{o.Adres} {(o.Afbuigend ? "afbuigend" : "rechtdoor")} ({o.Label}: stand {o.K} gaf kortsluiting na melder {o.NaMelder})");
            }
            if (!veranderd) break;
        }
        return c;
    }
    /// <summary>BUG #121 (log 18:14, opdracht vanaf melder 5): de bekende oplossing van kortsluitpunt #1 (wissel 1 bij wissel 2 afbuigend, kortsluiting na melder 12) werd
    /// alleen toegepast bij een opdracht die op dezelfde startmelder (13) begon als toen. Begint de opdracht op een melder LATER op dezelfde weg (5), dan is de weg
    /// van daar naar de melder van de kortsluiting even goed bekend: rijdt de loc volgens de kaart (stappen met deze wisselstand eerder gereden, zonder keuze)
    /// van <paramref name="van"/> naar <paramref name="doel"/>, dan geldt de oplossing ook.</summary>
    internal bool BereiktMelder(int van, int doel, Richting r, Configuratie c)
    {
        int huidig = van; var gezien = new HashSet<int> { huidig };
        for (int i = 0; i < 60; i++)
        {
            if (huidig == doel) return true;
            var volg = Overgangen.Where(x => x.Van == huidig && x.Richting == r && x.GezienBijConfiguraties.Contains(c.Sleutel)).Select(x => x.Naar).Distinct().ToList();
            if (volg.Count != 1) return false;
            huidig = volg[0];
            if (!gezien.Add(huidig)) return huidig == doel;
        }
        return false;
    }

    public List<int> AdressenZonderEffect { get; set; } = new();
    public List<VoorgesteldBlok> VoorgesteldeBlokken { get; set; } = new();
    /// <summary>BUG #78: voorstel voor wisselstraten per gereden overgang (zie <see cref="WisselstraatVoorstel"/>). Oudere kaarten: leeg.</summary>
    public List<WisselstraatVoorstel> Wisselstraatvoorstellen { get; set; } = new();
    public List<string> Waarschuwingen { get; set; } = new();
    /// <summary>BUG #104: uitkomsten van 'Voltooide baan nogmaals checken'. De kaart zelf blijft bij een controle ongewijzigd; dit zijn alleen de verslagen.</summary>
    public List<ControleVerslag> ControleVerslagen { get; set; } = new();

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

    /// <summary>BUG #77: welke wisselstanden zijn nodig voor de stap <paramref name="van"/> -> <paramref name="naar"/> in richting <paramref name="r"/>?
    /// Uit de gevonden splitsingen (puntzijde na melder N, rechtdoor naar R, afbuigend naar A): rijdend van N naar R of A (kop) moet de wissel op
    /// die tak staan; rijdend van R of A terug naar N (van achteren, andere richting) moet hij ook op die tak staan, anders wordt hij open gereden
    /// of volgt kortsluiting. Een omgekeerde overgang uit de kaart (X->Y gezien, dus Y->X ook) zegt hier niets over: die zegt niet in welke stand
    /// de wissel stond toen er van achteren doorheen gereden werd.</summary>
    public List<(int Adres, bool Afbuigend)> WisselEisenVoorStap(int van, int naar, Richting r) =>
        WisselEisenVoorStapDetail(van, naar, r).Select(e => (e.Adres, e.Afbuigend)).Distinct().ToList();

    /// <summary>Zelfde als <see cref="WisselEisenVoorStap"/>, met erbij of de stap van achteren door de wissel gaat (BUG #78).</summary>
    public List<(int Adres, bool Afbuigend, bool Achter)> WisselEisenVoorStapDetail(int van, int naar, Richting r)
    {
        var res = new List<(int, bool, bool)>();
        foreach (var w in Wissels)
            foreach (var o in w.Waarnemingen.Where(x => x.Soort == WaarnemingSoort.Splitsing))
            {
                // BUG #143 (log 16:13, wissel 1 en 2): een splitsing waarvan rechtdoor en afbuigend naar DEZELFDE melder gaan (de beide
                // standen van een overloopwissel-paar leiden naar melder 5) zegt niets over de stand die een stap vraagt. Eerder werd
                // daar 'rechtdoor' uit gehaald en zette de verkenner wissel 1 en 2 om terwijl de loc in de kruising stond.
                if (o.VolgendeBijRechtdoor is int rd && o.VolgendeBijAfbuigend is int af && rd == af) continue;
                bool kop = van == o.NaMelder && r == o.Richting;
                bool achter = naar == o.NaMelder && r == o.Richting.Om();
                if (kop && o.VolgendeBijRechtdoor == naar) res.Add((w.Adres, false, false));
                else if (kop && o.VolgendeBijAfbuigend == naar) res.Add((w.Adres, true, false));
                else if (achter && o.VolgendeBijRechtdoor == van) res.Add((w.Adres, false, true));
                else if (achter && o.VolgendeBijAfbuigend == van) res.Add((w.Adres, true, true));
            }
        return res.Distinct().ToList();
    }

    private static bool IngangInPad(List<int> pad, int via, int na)
    {
        for (int i = 0; i + 1 < pad.Count; i++) if (pad[i] == via && pad[i + 1] == na) return true;
        return false;
    }

    /// <summary>BUG #83 (Marco 11:21: "de kortsluitingen op dezelfde plekken keren steeds terug, terwijl de oplossing al bekend is"): een opgelost
    /// kortsluitpunt zegt: rijdend in richting R voorbij melder M gaf de stand K kortsluiting, en het extra schakelen van wissel A gaf
    /// geen kortsluiting meer. Dat werd alleen bij het oplossen zelf gebruikt; de routes naar een kortsluitpunt en de terugwegen
    /// bewaarden de oude stand K (bijv. wissel 10 afbuigend zonder wissel 5) en liepen steeds opnieuw in dezelfde kortsluiting.
    /// Hier wordt een stand die een opgelost kortsluitpunt bevat (alle afbuigende wissels van K staan afbuigend) aangepast: wissel A krijgt de
    /// opgeloste stand, tenzij een eis van dit pad (<see cref="MetWisselEisen"/>) het tegendeel vraagt; dan gaan de wissels van K die niet
    /// afbuigend hoeven weer rechtdoor. Open kortsluitpunten worden niet gebruikt (daar is nog niets over bewezen).</summary>
    public Configuratie MetBekendeKortsluitOplossingen(List<int> pad, Richting r, Configuratie c, out List<string> wijzigingen)
    {
        wijzigingen = new List<string>();
        var beschermd = AltijdAfbuigendOpPad(pad, r);
        var eisen = new Dictionary<int, bool>(); var strijdig = new HashSet<int>();
        for (int i = 0; i + 1 < pad.Count; i++)
            foreach (var (a, afb) in WisselEisenVoorStap(pad[i], pad[i + 1], r))
            {
                if (eisen.TryGetValue(a, out var bestaand) && bestaand != afb) strijdig.Add(a);
                eisen[a] = afb;
            }
        // BUG #100 (log 16:29): twee opgeloste kortsluitpunten die voor hetzelfde adres het tegenovergestelde zeggen (13 afbuigend na
        // melder 14, 13 rechtdoor na melder 24) zetten elkaar elke ronde terug; de uitkomst hing van het toeval van het aantal rondes af
        // (in het log 20 keer dezelfde regel). Een adres dat al door een oplossing is gezet en door een andere oplossing de andere kant
        // op zou moeten, blijft nu staan zoals het voor deze functie was (de oorspronkelijke stand) en wordt eenmaal gemeld.
        var origineel = c; var gezet = new Dictionary<int, bool>(); var botsend = new HashSet<int>();
        for (int ronde = 0; ronde < 5; ronde++)
        {
            bool veranderd = false;
            foreach (var k in Oplossingen(r).Where(k => pad.Contains(k.NaMelder))
                .Concat(Oplossingen(r.Om()).Where(k => k.Via > 0 && k.Afbuigend && IngangInPad(pad, k.NaMelder, k.Via))))            {
                // BUG #150: een oplossing met bekende ingang (Via -> NaMelder) geldt voor die ingang bij elke wisselstand, niet alleen bij de
                // stand waarin de kortsluiting toevallig gevonden werd (een samenvoegende wissel staat voor één ingang goed).
                bool ingangGeldt = k.Via > 0 && k.Afbuigend && (IngangInPad(pad, k.Via, k.NaMelder) || IngangInPad(pad, k.NaMelder, k.Via)); // ook omgekeerd: dezelfde wissel op hetzelfde stuk spoor
                if (!ingangGeldt && !k.K.Afbuigend.All(c.Bevat)) continue;
                int a = k.Adres; bool s = k.Afbuigend;
                if (botsend.Contains(a)) continue;
                if (gezet.TryGetValue(a, out var eerder) && eerder != s)
                {
                    botsend.Add(a);
                    c = origineel.Bevat(a) ? c.MetStand(a, true) : c.Zonder(a);
                    wijzigingen.Add($"{a}: de opgeloste kortsluitpunten op dit pad zeggen verschillende dingen over dit adres; het blijft op '{(origineel.Bevat(a) ? "afbuigend" : "rechtdoor")}' staan");
                    veranderd = true;
                    continue;
                }
                if (c.Bevat(a) == s) continue;
                // BUG #112: een oplossing mag geen wissel rechtdoor zetten die een stap van dit pad altijd afbuigend nodig had.
                if (!s && beschermd.Contains(a)) continue;
                bool eisTegen = !strijdig.Contains(a) && eisen.TryGetValue(a, out var e) && e != s;
                if (!eisTegen)
                {
                    c = c.MetStand(a, s);
                    gezet[a] = s;
                    wijzigingen.Add($"{a} {(s ? "afbuigend" : "rechtdoor")} ({k.Label} na melder {k.NaMelder} was daarmee opgelost)");
                    veranderd = true;
                    continue;
                }
                // De oplossing botst met een eis van dit pad: dan mag de stand die de kortsluiting gaf hier niet voorkomen.
                var weg = k.K.Afbuigend.Where(w => !(eisen.TryGetValue(w, out var ew) && ew) && !beschermd.Contains(w)).ToList();
                if (weg.Count == 0) continue;
                foreach (var w in weg) c = c.Zonder(w);
                wijzigingen.Add($"{string.Join(", ", weg)} rechtdoor ({k.Label}: die stand gaf kortsluiting na melder {k.NaMelder}, en de oplossing, wissel {a} {(s ? "afbuigend" : "rechtdoor")}, kan op dit pad niet)");
                veranderd = true;
            }
            if (!veranderd) break;
        }
        return c;
    }

    /// <summary>BUG #112 (Marco 13:46, log 12:31): een stap die minstens twee keer is gereden, steeds met dezelfde wissel(s) afbuigend, heeft die
    /// wissel(s) afbuigend nodig. Wissel 2 heeft geen splitsing in de kaart (alleen "kortsluiting bij omzetten"), dus de eisen van het pad kenden
    /// hem niet; de bekende oplossing 'wissel 2 rechtdoor' (voor 5 -> 16) werd ook op het pad 5-12-11-4-129-14 toegepast, waardoor de loc
    /// naar 16 reed. Alleen gebruikt om te voorkomen dat zo'n wissel rechtdoor wordt gezet, nooit om er een om te zetten.</summary>
    public HashSet<int> AltijdAfbuigendOpPad(List<int> pad, Richting r)
    {
        var res = new HashSet<int>();
        for (int i = 0; i + 1 < pad.Count; i++)
        {
            var o = Overgangen.FirstOrDefault(x => x.Van == pad[i] && x.Naar == pad[i + 1] && x.Richting == r);
            if (o is null || o.AantalKeer < 2 || o.GezienBijConfiguraties.Count == 0) continue;
            HashSet<int>? gemeen = null;
            foreach (var sleutel in o.GezienBijConfiguraties)
            {
                var af = Configuratie.VanSleutel(sleutel).Afbuigend;
                if (gemeen is null) gemeen = new HashSet<int>(af); else gemeen.IntersectWith(af);
            }
            if (gemeen is not null) res.UnionWith(gemeen);
        }
        return res;
    }

    /// <summary>BUG #77: de wisselstand <paramref name="c"/> aangevuld met de eisen van alle stappen van <paramref name="pad"/>. Tegenstrijdige
    /// eisen voor een adres (twee stappen willen het tegenovergestelde) worden niet toegepast; dat adres blijft zoals het was.</summary>
    public Configuratie MetWisselEisen(List<int> pad, Richting r, Configuratie c, out List<string> wijzigingen)
    {
        wijzigingen = new List<string>();
        var eisen = new Dictionary<int, bool>(); var strijdig = new HashSet<int>();
        for (int i = 0; i + 1 < pad.Count; i++)
            foreach (var (a, afb) in WisselEisenVoorStap(pad[i], pad[i + 1], r))
            {
                if (eisen.TryGetValue(a, out var bestaand) && bestaand != afb) strijdig.Add(a);
                eisen[a] = afb;
            }
        var nieuw = c;
        foreach (var (a, afb) in eisen)
        {
            if (strijdig.Contains(a) || nieuw.Bevat(a) == afb) continue;
            nieuw = nieuw.MetStand(a, afb);
            wijzigingen.Add($"{a} {(afb ? "afbuigend" : "rechtdoor")}");
        }
        return nieuw;
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

    /// <summary>BUG #98: een overgang X->Y (r) waarvan Y->X in dezelfde richting veel vaker is gereden (minstens 3 keer zo vaak) is een spiegelbeeld
    /// uit een rit met de loc andersom: een spoor loopt in één richting maar één kant op. Verwijdert die overgangen; geeft ze terug als tekst.</summary>
    public List<string> VerwijderSpiegelOvergangen()
    {
        var weg = Overgangen.Where(o => Overgangen.Any(t => t.Van == o.Naar && t.Naar == o.Van && t.Richting == o.Richting
                                                              && t.AantalKeer >= 3 * Math.Max(1, o.AantalKeer))).ToList();
        foreach (var o in weg) Overgangen.Remove(o);
        return weg.Select(o => $"{o.Van} -> {o.Naar} ({o.Richting.Tekst()}, {o.AantalKeer}x, stand {string.Join("/", o.GezienBijConfiguraties.Select(x => x == "" ? "alle rechtdoor" : x))})").ToList();
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
