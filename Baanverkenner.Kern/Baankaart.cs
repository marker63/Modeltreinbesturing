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
    /// <summary>BUG #165: hoe vaak elke configuratie bij deze doorgang gezien is (ruis van 1 keer valt zo weg tegen de gewone standen). Oude kaarten hebben dit niet.</summary>
    public Dictionary<string, int> Tellers { get; set; } = new();
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
    /// <summary>BUG #155: bij KortsluitingBijOmzetten de melder waar de loc vandaan kwam (de ingang): Via → NaMelder (richting) geeft kortsluiting als dit adres afbuigend staat.</summary>
    public int? Via { get; set; }
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
    /// <summary>BUG #204/#211/#217: de oplossing is alleen afgeleid uit het wegvallen van het alarm bij een stilstaande loc (Dinamo zet het alarm zelf aan en uit) en nog niet door een rit bewezen.
    /// <see cref="Baankaart.ToetsVoorlopigeOplossingen"/> bevestigt, vervangt of trekt haar in zodra ritten over dezelfde overgang dat laten zien.</summary>
    public bool Voorlopig { get; set; }
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
    DoelBereikt,         // (navigatie) doelmelder bereikt
    StapKlaar            // BUG #172: stap-voor-stap: de rit is zo lang als gevraagd (MaxStappen) en stopt
}

public class Traject
{
    public Configuratie Configuratie { get; set; } = Configuratie.Basis;
    public int Start { get; set; }
    public Richting Richting { get; set; }
    public List<int> Reeks { get; set; } = new();
    public RitEinde Einde { get; set; }
}

/// <summary>BUG #156: elke gereden rit (verkenning én navigatie), voor het rapport "Alle ritten" met blokken, melders en wisselstanden.</summary>
public class GeredenRit
{
    public int Nr { get; set; }
    public DateTime Tijd { get; set; }
    public bool Navigatie { get; set; }
    public int Start { get; set; }
    public Richting Richting { get; set; }
    /// <summary>Adressen die tijdens de rit afbuigend stonden.</summary>
    public List<int> Afbuigend { get; set; } = new();
    public List<int> Reeks { get; set; } = new();
    public RitEinde Einde { get; set; }
    /// <summary>BUG #157: gereden terwijl Dinamo wissel-commando's niet terugmeldde; de uitkomst is niet betrouwbaar.</summary>
    public bool Onbetrouwbaar { get; set; }
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
    /// <summary>BUG #235: wissels die omgekeerd zijn aangesloten (Dinamo 'afbuigend' = op de baan rechtdoor); alleen voor het rapport.</summary>
    public List<int> OmgekeerdeWissels { get; set; } = new();

    /// <summary>BUG #247 (Marco 18-10 16:07: "je zet in het blokkenschema dat wissel 5 afbuigend en wissel 6 niet afbuigend moet staan om van 14 naar 129 te gaan, maar ze moeten
    /// beide afbuigend staan"): de verkenner rekent met de Dinamo-commando's; bij een omgekeerd aangesloten wissel is dat op de baan andersom. Schema's en rapport tonen voor zo'n
    /// wissel de stand OP DE BAAN, met de Dinamo-stand erbij. Kort: "afb."/"rd." (normaal) of "rd. (baan)"/"afb. (baan)" (omgekeerd).</summary>
    public string StandKort(int adres, bool dinamoAfbuigend)
        => OmgekeerdeWissels.Contains(adres) ? (dinamoAfbuigend ? "rd." : "afb.") + " (baan)" : (dinamoAfbuigend ? "afb." : "rd.");
    /// <summary>BUG #247: lange vorm, bijv. "afbuigend" of "afbuigend op de baan (Dinamo rechtdoor)".</summary>
    public string StandLang(int adres, bool dinamoAfbuigend)
        => OmgekeerdeWissels.Contains(adres) ? $"{(dinamoAfbuigend ? "rechtdoor" : "afbuigend")} op de baan (Dinamo {(dinamoAfbuigend ? "afbuigend" : "rechtdoor")})" : (dinamoAfbuigend ? "afbuigend" : "rechtdoor");
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
    /// <summary>BUG #238: sleutels van oplossingen uit de voorkennis die in deze verkenning weerlegd zijn; het leerprofiel neemt ze niet meer over en haalt ze weg.</summary>
    public List<string> WeerlegdeOplossingen { get; set; } = new();

    /// <summary>BUG #238 (autolog 18-10 14:24-14:26): de voorkennis bevat oplossingen die niet kloppen (ruis uit eerdere sessies, o.a. "wissel 6 afbuigend voor 129 -> 14 achteruit",
    /// door #217 al uit de kaart gehaald maar nog in het leerprofiel). Een oplossing uit de voorkennis wordt weerlegd als de rit waarin ze werd toegepast juist daar misging:
    /// een kortsluiting na de melder van de oplossing (<paramref name="kortsluitNa"/>), of een onverwachte melder direct na de ingang van de oplossing (<paramref name="afwijkingNa"/> -> volgende
    /// melder van het pad). Geeft per weerlegde oplossing een tekst; ze verdwijnt uit de kaart en komt in <see cref="WeerlegdeOplossingen"/>.</summary>
    public List<string> WeerlegVoorkennis(IReadOnlyList<int> pad, Richting r, Configuratie c, int? kortsluitNa, int? afwijkingNa)
    {
        var res = new List<string>();
        foreach (var o in BekendeKortsluitOplossingen.ToList())
        {
            if (!o.Afbuigend || !c.Bevat(o.Adres)) continue;
            bool raak = false;
            if (kortsluitNa is int x && o.NaMelder == x && (o.Richting == r || o.Richting == r.Om())) raak = true;
            if (afwijkingNa is int y)
            {
                int i = pad.ToList().IndexOf(y);
                // alleen een oplossing met precies deze ingang, en niet een wissel die de kaart voor deze stap zelf afbuigend eist (wissel 5 voor 129 -> 14 is wel nodig)
                if (i >= 0 && i + 1 < pad.Count && o.NaMelder == pad[i + 1] && o.Via == y && o.Richting == r
                    && !WisselEisenVoorStap(y, pad[i + 1], r).Contains((o.Adres, true))) raak = true;
            }
            if (!raak) continue;
            BekendeKortsluitOplossingen.Remove(o);
            if (!WeerlegdeOplossingen.Contains(o.Sleutel)) WeerlegdeOplossingen.Add(o.Sleutel);
            res.Add($"eerder gevonden oplossing 'wissel {o.Adres} afbuigend' (na melder {o.NaMelder}, {o.Richting.Tekst()}, stand {(o.Stand == "" ? "alle rechtdoor" : o.Stand)}{(o.Via > 0 ? $", ingang {o.Via}" : "")}) klopt niet: de rit met die stand ging daar mis");
        }
        return res;
    }
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

    /// <summary>BUG #254 (Marco 20-10, log 15:28:43): een opgelost kortsluitpunt zegt "wissel W afbuigend alleen geeft kortsluiting, met wissel P erbij niet". Wissel P en W horen dan bij
    /// elkaar: kan W niet omgezet worden (de loc staat erop), dan mag P ook niet omgezet worden, anders ontstaat precies de stand die de kortsluiting gaf (2 afbuigend zonder 1).
    /// Geeft de partners P van de wissels in <paramref name="vast"/>. Alleen bewezen oplossingen (niet voorlopig).</summary>
    public HashSet<int> OverloopPartnersVan(IEnumerable<int> vast)
    {
        var v = vast.ToHashSet(); var res = new HashSet<int>();
        if (v.Count == 0) return res;
        foreach (var k in Kortsluitpunten.Where(k => k.Soort == KortsluitpuntSoort.Kortsluiting && k.Opgelost && !k.Voorlopig && k.OpgelostMetAfbuigend == true && k.OpgelostDoorAdres is int))
        {
            int p = k.OpgelostDoorAdres!.Value;
            if (k.Configuratie.Afbuigend.Any(v.Contains) && !v.Contains(p)) res.Add(p);
            if (v.Contains(p)) foreach (var w in k.Configuratie.Afbuigend) if (!v.Contains(w)) res.Add(w);
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
        // BUG #217: een wissel die dezelfde overgang (ook omgekeerd) afbuigend juist kortsluiting geeft kan die kortsluiting niet oplossen.
        if (OplossingStrijdigMetVerbod(kp, adres)) return false;
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
            // BUG #246 (autolog 18-10 15:58-16:00): de proeven van opdracht #4 (vanaf 14, stand 5 + proefadres) reden elk opnieuw in kortsluitpunt #1 na melder 24, hoewel de oplossing
            // (wissel 10 afbuigend) bekend was: de weg 14 -> 129 -> 24 was alleen met stand '5' gereden, nooit met '1, 5' of '3, 5'. Zit de stand van de oplossing in de proefstand,
            // dan telt ook de weg die met de stand van de oplossing gereden is (zoals bij een opdracht die op de startmelder van de oplossing begint).
            foreach (var o in Oplossingen(r).Where(o => (o.Start == start || BereiktMelder(start, o.NaMelder, r, c)
                                                          || (o.K.Afbuigend.All(c.Bevat) && BereiktMelder(start, o.NaMelder, r, o.K))) && !o.K.Bevat(o.Adres)))
            {
                // BUG #236 (autolog 18-10 14:18:28-14:19:58): een eerder gevonden oplossing (wissel 1 bij wissel 2 afbuigend) hoort bij de ingang 12 -> 5 (overloop); de proef
                // reed vanaf 13 via de hele ring en kwam vanaf 16 op melder 5: kortsluiting ondanks de 'oplossing'. Weet de kaart via welke melder de loc op die melder aankomt
                // en is dat een andere ingang dan die van de oplossing, dan geldt de oplossing hier niet.
                var viaNu = ViaBijAankomst(start, o.NaMelder, r, c) ?? ViaBijAankomst(start, o.NaMelder, r, Configuratie.Basis);
                if (o.Via != 0 && viaNu is int via && via != o.Via) continue;
                // BUG #236: ook zonder bekende ingang (oude oplossingen hebben Via = 0): weet de kaart dat een wissel uit de stand van de oplossing afbuigend op déze ingang
                // kortsluiting geeft (wissel 2 afbuigend van 16 naar 5), dan kan die oplossing hier niets oplossen.
                if (viaNu is int v2 && o.K.Afbuigend.Any(a => VerbodenIngangen().Contains((v2, o.NaMelder, r, a)))) continue;
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
    /// <summary>BUG #236: volgt de bekende weg (stappen eerder gereden met stand <paramref name="c"/>, zonder keuze) van <paramref name="van"/> naar <paramref name="doel"/> en geeft de melder
    /// vlak voor <paramref name="doel"/> (de ingang). Null als de weg niet eenduidig bekend is of van == doel.</summary>
    internal int? ViaBijAankomst(int van, int doel, Richting r, Configuratie c)
    {
        if (van == doel) return null;
        int huidig = van; var gezien = new HashSet<int> { huidig };
        for (int i = 0; i < 60; i++)
        {
            var volg = Overgangen.Where(x => x.Van == huidig && x.Richting == r && x.GezienBijConfiguraties.Contains(c.Sleutel)).Select(x => x.Naar).Distinct().ToList();
            if (volg.Count != 1) return null;
            if (volg[0] == doel) return huidig;
            huidig = volg[0];
            if (!gezien.Add(huidig)) return null;
        }
        return null;
    }

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
    /// <summary>BUG #181: lijst van overschreven vastgestelde feiten (blok van een melder, kruismelder, rijrichting, wissel omgezet onder de loc).</summary>
    public List<string> Overschrijvingen { get; set; } = new();
    public List<GeredenRit> Ritten { get; set; } = new();
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
            d.Tellers[c.Sleutel] = d.Tellers.GetValueOrDefault(c.Sleutel) + 1;
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

    /// <summary>BUG #252 (Marco 20-10: "de aanliggende sectie heb je al 20 keer gevonden en je weet nog niet welke wissel je moet schakelen"): hoe ver (in melderstappen)
    /// ligt elk al waargenomen wissel van <paramref name="melder"/>? 0 = de wissel is bij die melder zelf waargenomen (als splitsing na die melder of als
    /// vervolg ervan); 1 = bij een aanliggende melder (een overgang in een van beide richtingen), enzovoort tot <paramref name="maxStappen"/>.
    /// Wissels die niet voorkomen liggen verder weg of zijn nog onbekend. Zo kiest de zoeker de wissels in de aanliggende sectie, niet op adresnummer.</summary>
    public Dictionary<int, int> WisselAfstanden(int melder, int maxStappen = 2)
    {
        var afstand = new Dictionary<int, int> { [melder] = 0 };
        var rij = new Queue<int>(); rij.Enqueue(melder);
        while (rij.Count > 0)
        {
            int m = rij.Dequeue();
            if (afstand[m] >= maxStappen) continue;
            foreach (var o in Overgangen)
            {
                int buur = o.Van == m ? o.Naar : o.Naar == m ? o.Van : 0;
                if (buur != 0 && !afstand.ContainsKey(buur)) { afstand[buur] = afstand[m] + 1; rij.Enqueue(buur); }
            }
        }
        var res = new Dictionary<int, int>();
        foreach (var w in Wissels)
            foreach (var o in w.Waarnemingen)
                foreach (var m in new[] { (int?)o.NaMelder, o.VolgendeBijRechtdoor, o.VolgendeBijAfbuigend })
                    if (m is int mm && afstand.TryGetValue(mm, out var d) && (!res.TryGetValue(w.Adres, out var oud) || d < oud)) res[w.Adres] = d;
        return res;
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

    /// <summary>BUG #159 (Marco 15-10 12:24: "van 16 naar 5 met wissel 2 afbuigend, daar wist je al heel lang van dat je dat niet mocht rijden"): ingangen
    /// (Via -> Na in richting R) waarbij <c>Adres</c> afbuigend kortsluiting geeft. Bron: waarnemingen KortsluitingBijOmzetten met bekende ingang (Via); bij
    /// oudere kaarten zonder Via wordt de ingang afgeleid uit de doorgangen: de ingang waarvandaan het vervolg altijd met deze wissel rechtdoor
    /// gereden is, terwijl een andere ingang naar hetzelfde vervolg hem juist afbuigend vraagt.</summary>
    private int? AfgeleideVia(int adres, WisselWaarneming ob)
    {
        if (ob.VolgendeBijRechtdoor is not int q) return null;
        var rd = Doorgangen.Where(d => d.Melder == ob.NaMelder && d.Richting == ob.Richting && d.Volgende == q && d.AantalKeer >= 3).ToList();
        var zonder = rd.Where(d => d.Configuraties.All(c => !Configuratie.VanSleutel(c).Bevat(adres))).ToList();
        var met = rd.Where(d => d.Configuraties.Any(c => Configuratie.VanSleutel(c).Bevat(adres))).ToList();
        return zonder.Count == 1 && met.Count >= 1 ? zonder[0].Via : null;
    }

    /// <summary>BUG #160: oudere waarnemingen KortsluitingBijOmzetten zonder ingang krijgen die alsnog (afgeleid uit de doorgangen), zodat het rapport
    /// 'ingang 16 → 5' toont en elke latere berekening dezelfde ingang kent.</summary>
    public int VulOntbrekendeVia()
    {
        int n = 0;
        foreach (var w in Wissels)
            foreach (var ob in w.Waarnemingen.Where(x => x.Soort == WaarnemingSoort.KortsluitingBijOmzetten && x.Via is null))
                if (AfgeleideVia(w.Adres, ob) is int v)
                {
                    ob.Via = v; n++;
                    ob.Toelichting = $"Niet rijden van melder {v} naar {ob.NaMelder} ({ob.Richting.Tekst()}) als wissel {w.Adres} afbuigend staat: kortsluiting (ingang afgeleid uit de doorgangen). " + ob.Toelichting;
                }
        return n;
    }

    public List<(int Via, int Na, Richting R, int Adres)> VerbodenIngangen()
    {
        var res = new List<(int, int, Richting, int)>();
        foreach (var w in Wissels)
            foreach (var ob in w.Waarnemingen.Where(x => x.Soort == WaarnemingSoort.KortsluitingBijOmzetten))
            {
                int? via = ob.Via ?? AfgeleideVia(w.Adres, ob);
                if (via is int v && !res.Contains((v, ob.NaMelder, ob.Richting, w.Adres))) res.Add((v, ob.NaMelder, ob.Richting, w.Adres));
            }
        return res;
    }

    /// <summary>BUG #159: raakt deze route een ingang waarbij <paramref name="adres"/> afbuigend kortsluiting geeft?</summary>
    public bool IngangVerboden(IEnumerable<Etappe> route, int adres)
    {
        var verboden = VerbodenIngangen().Where(v => v.Adres == adres).ToList();
        if (verboden.Count == 0) return false;
        foreach (var e in route)
            for (int i = 0; i + 1 < e.Pad.Count; i++)
                if (verboden.Any(v => v.Via == e.Pad[i] && v.Na == e.Pad[i + 1] && v.R == e.Richting)) return true;
        return false;
    }

    /// <summary>BUG #159: zet wissels die op dit pad een verboden ingang afbuigend zouden nemen weer rechtdoor, tenzij een andere stap van het pad die
    /// wissel afbuigend eist.</summary>
    public Configuratie MetVerbodenIngangen(List<int> pad, Richting r, Configuratie c, out List<string> wijzigingen)
    {
        wijzigingen = new List<string>();
        var verboden = VerbodenIngangen();
        if (verboden.Count == 0) return c;
        var eisAfb = new HashSet<int>();
        for (int i = 0; i + 1 < pad.Count; i++)
            foreach (var (a, afb) in WisselEisenVoorStap(pad[i], pad[i + 1], r)) if (afb) eisAfb.Add(a);
        for (int i = 0; i + 1 < pad.Count; i++)
            foreach (var v in verboden.Where(v => v.Via == pad[i] && v.Na == pad[i + 1] && v.R == r))
                if (c.Bevat(v.Adres) && !eisAfb.Contains(v.Adres))
                {
                    c = c.Zonder(v.Adres);
                    wijzigingen.Add($"{v.Adres} rechtdoor (rijden van {v.Via} naar {v.Na} met wissel {v.Adres} afbuigend geeft kortsluiting)");
                }
        return c;
    }

    /// <summary>BUG #159: een 'oplossing' die zelf de verboden ingang afbuigend neemt (kortsluitpunt #3 van 15-10: na melder 5 vooruit 'opgelost door wissel 2
    /// afbuigend', terwijl 16 -> 5 met wissel 2 afbuigend juist de kortsluiting is) is onjuist: ze ontstond tijdens een Dinamo-uitval. Zo'n oplossing wordt
    /// ingetrokken.</summary>
    /// <summary>BUG #217 (log 18-10 10:22 en 11:31-11:34): kortsluitpunt #2 (129 -> 14 achteruit) stond "opgelost door wissel 6", terwijl 14 -> 129 vooruit met wissel 6 afbuigend
    /// kortsluiting geeft (dezelfde overgang andersom; wisselstanden gelden in beide richtingen). De verkenner zette 6 daarna steeds afbuigend voor de terugweg en zat vast op 129.
    /// True als <paramref name="adres"/> afbuigend voor de overgang van dit kortsluitpunt (zelfde of omgekeerde richting) als verboden ingang bekend is.</summary>
    public bool OplossingStrijdigMetVerbod(Kortsluitpunt kp, int adres)
    {
        var pad = kp.Route.Count > 0 ? kp.Route[^1].Pad : new List<int>();
        int via = pad.Count >= 2 && pad[^1] == kp.NaMelder ? pad[^2] : (kp.TweedeMelders.Count == 1 ? kp.TweedeMelders[0] : -1);
        foreach (var v in VerbodenIngangen().Where(v => v.Adres == adres))
        {
            if (v.Na == kp.NaMelder && v.R == kp.Richting && (via < 0 || v.Via == via)) return true;
            if (via >= 0 && v.Via == kp.NaMelder && v.Na == via && v.R == kp.Richting.Om()) return true;
        }
        return false;
    }

    public List<string> HerstelStrijdigeOplossingen()
    {
        var res = new List<string>();
        var verboden = VerbodenIngangen();
        foreach (var kp in Kortsluitpunten.Where(k => k.Opgelost && k.OpgelostMetAfbuigend == true && k.OpgelostDoorAdres is int).ToList())
        {
            int a = kp.OpgelostDoorAdres!.Value;
            var pad = kp.Route.Count > 0 ? kp.Route[^1].Pad : new List<int>();
            int via = pad.Count >= 2 && pad[^1] == kp.NaMelder ? pad[^2] : -1;
            bool zelfde = verboden.Any(v => v.Adres == a && v.Na == kp.NaMelder && v.R == kp.Richting && (via < 0 || v.Via == via));
            if (!zelfde)
            {
                // BUG #217: dezelfde overgang andersom: het punt blijft zoeken (niet opgegeven), want een andere wissel kan het nog oplossen
                if (!OplossingStrijdigMetVerbod(kp, a)) continue;
                kp.Opgelost = false; kp.OpgelostDoorAdres = null; kp.OpgelostMetAfbuigend = null;
                res.Add($"Kortsluitpunt #{kp.Id} (na melder {kp.NaMelder}, {kp.Richting.Tekst()}): de oplossing 'wissel {a} afbuigend' is ingetrokken, want dezelfde overgang andersom (melder {kp.NaMelder} -> {via}) met wissel {a} afbuigend geeft juist kortsluiting.");
                continue;
            }
            kp.Opgelost = false; kp.OpgelostDoorAdres = null; kp.OpgelostMetAfbuigend = null; kp.Opgegeven = true;
            res.Add($"Kortsluitpunt #{kp.Id} (na melder {kp.NaMelder}, {kp.Richting.Tekst()}): de oplossing 'wissel {a} afbuigend' is ingetrokken, want rijden naar melder {kp.NaMelder} met wissel {a} afbuigend geeft juist kortsluiting.");
        }
        return res;
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

    /// <summary>BUG #227 (log 18-10 13:17:18): bij het vertrek achteruit vanaf melder 21 meldde melder 29 (de andere kant, 21 -> 29 is vooruit 12x gereden) 0,8 s 'bezet'. De rit telde
    /// 21 -> 29 -> 24 -> 129 (achteruit) en de proef gaf een valse splitsing 'wissel 10 na melder 21 (achteruit): rechtdoor 24, afbuigend 29'. Daardoor eiste de kaart wissel 10 rechtdoor
    /// voor 129 -> 24 -> 21 (13:21:33, kortsluiting) en zette de basisrit wissel 10 afbuigend bij melder 21 (13:24:38). Deze opruiming haalt weg: (1) een overgang X -> Y (r) waarvan
    /// X -> Y in de andere richting minstens 3 keer zo vaak gereden is; (2) de stap daarna Y -> Z (r) als Z een bekende opvolger van X in r is en die stap niet vaker en niet met andere
    /// standen gereden is; (3) splitsingen en doorgangen die op zo'n overgang steunen. Geeft per onderdeel een melding.</summary>
    public List<string> VerwijderAchterkantOvergangen()
    {
        var res = new List<string>();
        var vals = Overgangen.Where(o => Overgangen.Any(t => t.Van == o.Van && t.Naar == o.Naar && t.Richting == o.Richting.Om() && t.AantalKeer >= 3 * Math.Max(1, o.AantalKeer))).ToList();
        foreach (var o in vals)
        {
            Overgangen.Remove(o);
            res.Add($"overgang {o.Van} -> {o.Naar} ({o.Richting.Tekst()}, {o.AantalKeer}x): {o.Van} -> {o.Naar} is {o.Richting.Om().Tekst()} veel vaker gereden; melder {o.Naar} lag aan de andere kant van de loc");
            var echt = BekendeVolgende(o.Van, o.Richting);
            foreach (var na in Overgangen.Where(t => t.Van == o.Naar && t.Richting == o.Richting && echt.Contains(t.Naar) && t.AantalKeer <= o.AantalKeer
                                                    && t.GezienBijConfiguraties.All(o.GezienBijConfiguraties.Contains)).ToList())
            {
                Overgangen.Remove(na);
                res.Add($"overgang {na.Van} -> {na.Naar} ({na.Richting.Tekst()}, {na.AantalKeer}x): volgde op de valse overgang {o.Van} -> {o.Naar}");
            }
            foreach (var w in Wissels)
            {
                int n = w.Waarnemingen.RemoveAll(x => x.Soort == WaarnemingSoort.Splitsing && x.NaMelder == o.Van && x.Richting == o.Richting
                                                      && (x.VolgendeBijRechtdoor == o.Naar || x.VolgendeBijAfbuigend == o.Naar));
                if (n > 0) res.Add($"splitsing van wissel {w.Adres} na melder {o.Van} ({o.Richting.Tekst()}) met een tak naar melder {o.Naar}");
            }
            int d = Doorgangen.RemoveAll(x => x.Richting == o.Richting && ((x.Melder == o.Van && x.Volgende == o.Naar) || (x.Via == o.Van && x.Melder == o.Naar)));
            if (d > 0) res.Add($"{d} doorgang(en) via {o.Van} -> {o.Naar} ({o.Richting.Tekst()})");
        }
        return res;
    }

    public void RegistreerKopspoor(int melder, Richting r, Configuratie c)
    {
        if (!Kopsporen.Any(k => k.Melder == melder && k.Richting == r))
            Kopsporen.Add(new Eindpunt { Melder = melder, Richting = r, Configuratie = c.ToString() });
    }

    /// <summary>BUG #209/#210 (Marco 18-10: "wissel 11 bestaat niet"): haalt alles weg wat aan een wisseladres hangt dat op de baan niet bestaat: waarnemingen, de adres-afhankelijke
    /// kortsluitoplossingen, en het adres in configuraties van overgangen, doorgangen, trajecten, ritten en kortsluitpunten (de overgang zelf is wel echt gereden en blijft, met
    /// de stand zonder dat adres). Geeft een melding per opgeruimd onderdeel. Nog niet getest op de baan.</summary>
    public List<string> VerwijderNietBestaandeAdressen(IEnumerable<int> adressen)
    {
        var meldingen = new List<string>();
        foreach (var a in adressen.Distinct().OrderBy(x => x))
        {
            string Zonder(string sleutel) => string.IsNullOrEmpty(sleutel) ? sleutel : Configuratie.VanSleutel(sleutel).Zonder(a).Sleutel;
            bool Heeft(string sleutel) => !string.IsNullOrEmpty(sleutel) && Configuratie.VanSleutel(sleutel).Bevat(a);
            int n = 0;

            n = Wissels.RemoveAll(w => w.Adres == a);
            if (n > 0) meldingen.Add($"wissel {a} (niet bestaand) uit de wisselslijst gehaald");

            n = 0;
            foreach (var o in Overgangen.Where(o => o.GezienBijConfiguraties.Any(Heeft)))
            {
                o.GezienBijConfiguraties = o.GezienBijConfiguraties.Select(Zonder).Distinct().ToList();
                n++;
            }
            if (n > 0) meldingen.Add($"{n} overgang(en): stand met adres {a} vervangen door de stand zonder dat adres");

            n = 0;
            foreach (var d in Doorgangen.Where(d => d.Configuraties.Any(Heeft) || d.Tellers.Keys.Any(Heeft)))
            {
                var nieuweTellers = new Dictionary<string, int>();
                foreach (var kv in d.Tellers) { var z = Zonder(kv.Key); nieuweTellers[z] = nieuweTellers.GetValueOrDefault(z) + kv.Value; }
                d.Tellers = nieuweTellers;
                d.Configuraties = d.Configuraties.Select(Zonder).Distinct().ToList();
                n++;
            }
            if (n > 0) meldingen.Add($"{n} doorgang(en): adres {a} uit de standen gehaald");

            n = 0;
            foreach (var t in Trajecten.Where(t => t.Configuratie.Bevat(a))) { t.Configuratie = t.Configuratie.Zonder(a); n++; }
            foreach (var k in Kopsporen.Where(k => Heeft(k.Configuratie))) { k.Configuratie = Zonder(k.Configuratie); n++; }
            foreach (var r in Ritten) if (r.Afbuigend.Remove(a)) n++;
            if (n > 0) meldingen.Add($"{n} traject(en)/kopspoor/rit(ten): adres {a} uit de stand gehaald");

            n = 0;
            foreach (var k in Kortsluitpunten)
            {
                bool geraakt = false;
                if (k.Configuratie.Bevat(a)) { k.Configuratie = k.Configuratie.Zonder(a); geraakt = true; }
                foreach (var e in k.Route.Where(e => e.Configuratie.Bevat(a))) { e.Configuratie = e.Configuratie.Zonder(a); geraakt = true; }
                if (k.GeprobeerdeAdressen.Remove(a)) geraakt = true;
                if (k.OpgelostDoorAdres == a) { k.Opgelost = false; k.OpgelostDoorAdres = null; k.OpgelostMetAfbuigend = null; geraakt = true; }
                if (geraakt) n++;
            }
            if (n > 0) meldingen.Add($"{n} kortsluitpunt(en): adres {a} verwijderd (een oplossing door dit adres is weer open)");

            n = BekendeKortsluitOplossingen.RemoveAll(o => o.Adres == a);
            foreach (var o in BekendeKortsluitOplossingen.Where(o => Heeft(o.Stand))) { o.Stand = Zonder(o.Stand); n++; }
            if (n > 0) meldingen.Add($"{n} bekende kortsluitoplossing(en) met adres {a} aangepast/verwijderd");

            if (AdressenZonderEffect.Remove(a)) meldingen.Add($"adres {a} uit 'adressen zonder effect' gehaald");
            if (Wisselstraatvoorstellen.Count > 0) { Wisselstraatvoorstellen = new(); meldingen.Add("wisselstraatvoorstellen worden opnieuw berekend"); }
        }
        return meldingen;
    }

    /// <summary>BUG #214 (rapport 18-10 09:50): een open kortsluitpunt (na melder N in richting R vanaf melder V, met stand S) is opgelost zodra een gewone rit over precies die overgang
    /// (V en N na elkaar, in dezelfde richting, of omgekeerd in de andere richting: wisselstanden gelden in beide richtingen) zonder kortsluiting is gereden met alle wissels van S afbuigend
    /// plus precies één extra wissel die in elke zo'n rit afbuigend stond. Is dat extra adres niet eenduidig (meerdere kandidaten in alle ritten), dan gebeurt er niets.
    /// Geeft een melding per opgelost kortsluitpunt. Nog niet getest op de baan.</summary>
    public List<string> LosKortsluitpuntenOpUitRitten()
    {
        var res = new List<string>();
        foreach (var k in Kortsluitpunten.Where(k => !k.Opgelost && !k.Opgegeven && !k.Tijdelijk && k.Soort == KortsluitpuntSoort.Kortsluiting && k.Configuratie.Afbuigend.Length > 0))
        {
            int via = k.Route.Count > 0 && k.Route[^1].Pad.Count > 1 && k.Route[^1].Pad[^1] == k.NaMelder ? k.Route[^1].Pad[^2] : (k.TweedeMelders.Count == 1 ? k.TweedeMelders[0] : 0);
            if (via == 0) continue;
            var ritten = Ritten.Where(r => !r.Onbetrouwbaar && r.Einde != RitEinde.Kortsluiting && r.Einde != RitEinde.BekendKortsluitpunt && k.Configuratie.Afbuigend.All(r.Afbuigend.Contains) && HeeftOvergang(r, via, k.NaMelder, k.Richting)).ToList();
            if (ritten.Count == 0) continue;
            var extra = ritten.Select(r => r.Afbuigend.Except(k.Configuratie.Afbuigend).ToHashSet()).Aggregate((x, y) => { x.IntersectWith(y); return x; });
            if (extra.Count != 1) continue;
            int adres = extra.First();
            k.Opgelost = true; k.OpgelostDoorAdres = adres; k.OpgelostMetAfbuigend = true;
            var w = Wissel(adres);
            if (!w.Waarnemingen.Any(x => x.Soort == WaarnemingSoort.LostKortsluitingOp && x.NaMelder == k.NaMelder && x.Richting == k.Richting))
                w.Waarnemingen.Add(new WisselWaarneming
                {
                    Soort = WaarnemingSoort.LostKortsluitingOp, NaMelder = k.NaMelder, Via = via, Richting = k.Richting, VolgendeBijAfbuigend = null,
                    Configuratie = k.Configuratie.ToString(),
                    Toelichting = $"Afgeleid uit {ritten.Count} rit(ten) zonder kortsluiting over {via} → {k.NaMelder} met adres {adres} afbuigend (naast {string.Join(", ", k.Configuratie.Afbuigend)})."
                });
            res.Add($"kortsluitpunt #{k.Id} opgelost met adres {adres} afbuigend (afgeleid uit ritten {string.Join(", ", ritten.Select(r => "#" + r.Nr))})");
        }
        return res;
    }

    /// <summary>BUG #212: elk opgelost kortsluitpunt met een oplossingsadres hoort bij dat wissel een waarneming "lost kortsluiting op" te hebben (anders ontbreekt het wissel in de wisselslijst
    /// terwijl de kaart het adres wel als oplossing kent). Voegt de ontbrekende waarneming toe en geeft een melding per toevoeging. Nog niet getest op de baan.</summary>
    public List<string> VulOntbrekendeOplossingWaarnemingen()
    {
        var res = new List<string>();
        foreach (var k in Kortsluitpunten.Where(k => k.Opgelost && k.OpgelostDoorAdres is int && !k.Voorlopig))
        {
            int adres = k.OpgelostDoorAdres!.Value;
            var w = Wissel(adres);
            if (w.Waarnemingen.Any(x => x.Soort == WaarnemingSoort.LostKortsluitingOp && x.NaMelder == k.NaMelder && x.Richting == k.Richting)) continue;
            int via = k.Route.Count > 0 && k.Route[^1].Pad.Count > 1 && k.Route[^1].Pad[^1] == k.NaMelder ? k.Route[^1].Pad[^2] : (k.TweedeMelders.Count == 1 ? k.TweedeMelders[0] : 0);
            w.Waarnemingen.Add(new WisselWaarneming
            {
                Soort = WaarnemingSoort.LostKortsluitingOp, NaMelder = k.NaMelder, Via = via == 0 ? null : via, Richting = k.Richting, VolgendeBijAfbuigend = null,
                Configuratie = k.Configuratie.ToString(),
                Toelichting = $"Aangevuld (BUG #212): kortsluitpunt #{k.Id} is opgelost met adres {adres} {(k.OpgelostMetAfbuigend == true ? "afbuigend" : "rechtdoor")}."
            });
            res.Add($"waarneming voor wissel {adres} aangevuld (kortsluitpunt #{k.Id})");
        }
        return res;
    }

    /// <summary>BUG #204/#211/#217: een voorlopige oplossing (alleen uit het alarm afgeleid) wordt getoetst aan de gereden ritten over dezelfde overgang (ook omgekeerd). Rijdt een rit
    /// zonder kortsluiting over de overgang met de stand van het punt plus extra wissels, en bevat de doorsnede van die extra wissels het adres van de oplossing, dan is zij bewezen. Bevat die
    /// doorsnede het adres niet, dan was zij ruis: bij precies één ander adres wordt zij daardoor vervangen, anders is het punt weer open. Geeft een melding per wijziging.</summary>
    public List<string> ToetsVoorlopigeOplossingen()
    {
        var res = new List<string>();
        foreach (var k in Kortsluitpunten.Where(k => k.Voorlopig && k.Opgelost && k.OpgelostDoorAdres is int && k.Configuratie.Afbuigend.Length > 0))
        {
            int x = k.OpgelostDoorAdres!.Value;
            var extra = ExtraUitRitten(k, out var ritten);
            if (extra is null) continue;
            if (extra.Contains(x)) { k.Voorlopig = false; res.Add($"kortsluitpunt #{k.Id}: oplossing wissel {x} bevestigd door ritten {string.Join(", ", ritten.Select(r => "#" + r.Nr))}"); continue; }
            if (extra.Count == 1)
            {
                int e = extra.First();
                k.OpgelostDoorAdres = e; k.OpgelostMetAfbuigend = true; k.Voorlopig = false;
                if (!k.GeprobeerdeAdressen.Contains(e)) k.GeprobeerdeAdressen.Add(e);
                res.Add($"kortsluitpunt #{k.Id}: de voorlopige oplossing wissel {x} was ruis; ritten {string.Join(", ", ritten.Select(r => "#" + r.Nr))} bewijzen wissel {e}");
            }
            else if (extra.Count == 0) continue;
            else
            {
                k.Opgelost = false; k.OpgelostDoorAdres = null; k.OpgelostMetAfbuigend = null; k.Voorlopig = false;
                res.Add($"kortsluitpunt #{k.Id}: de voorlopige oplossing wissel {x} is niet bevestigd door ritten {string.Join(", ", ritten.Select(r => "#" + r.Nr))}; het punt is weer open");
            }
        }
        return res;
    }

    /// <summary>De wissels (naast de stand van het punt) die afbuigend stonden in ALLE betrouwbare ritten zonder kortsluiting over de overgang van het punt; null als er geen zo'n rit is.</summary>
    private HashSet<int>? ExtraUitRitten(Kortsluitpunt k, out List<GeredenRit> ritten)
    {
        ritten = new List<GeredenRit>();
        int via = k.Route.Count > 0 && k.Route[^1].Pad.Count > 1 && k.Route[^1].Pad[^1] == k.NaMelder ? k.Route[^1].Pad[^2] : (k.TweedeMelders.Count == 1 ? k.TweedeMelders[0] : 0);
        if (via == 0) return null;
        ritten = Ritten.Where(r => !r.Onbetrouwbaar && r.Einde != RitEinde.Kortsluiting && r.Einde != RitEinde.BekendKortsluitpunt && k.Configuratie.Afbuigend.All(r.Afbuigend.Contains) && HeeftOvergang(r, via, k.NaMelder, k.Richting)).ToList();
        if (ritten.Count == 0) return null;
        return ritten.Select(r => r.Afbuigend.Except(k.Configuratie.Afbuigend).ToHashSet()).Aggregate((a, b) => { a.IntersectWith(b); return a; });
    }

    private static bool HeeftOvergang(GeredenRit r, int via, int na, Richting richting)
    {
        for (int i = 0; i + 1 < r.Reeks.Count; i++)
        {
            if (r.Richting == richting && r.Reeks[i] == via && r.Reeks[i + 1] == na) return true;
            if (r.Richting == richting.Om() && r.Reeks[i] == na && r.Reeks[i + 1] == via) return true;
        }
        return false;
    }

    public double LangsteReistijd() => Overgangen.Where(o => o.AantalMetingen > 0).Select(o => o.GemiddeldeSeconden).DefaultIfEmpty(0).Max();

    // ---- Opslaan/laden ----

    public static readonly JsonSerializerOptions JsonOpties = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public string AlsJson() => JsonSerializer.Serialize(this, JsonOpties);
    public static Baankaart? VanJson(string json)
    {
        var k = JsonSerializer.Deserialize<Baankaart>(json, JsonOpties);
        k?.VulOntbrekendeVia();   // BUG #160
        return k;
    }
}
