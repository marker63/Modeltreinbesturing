namespace Baanverkenner.Kern;

/// <summary>
/// BUG #129 (Marco 14-10 11:10): wat de verkenner over de Engelse wissels (kruiswissels) weet, hoort bij DEZE verkenning. De gebruiker geeft bij de start op
/// hoeveel Engelse wissels er zijn en welke motoren erin zitten (bijv. "13+10" of "13+10, 3+4"; optioneel met de melder van de kruiswissel: "13+10@24").
/// De melder zelf wordt anders ontdekt (kortsluiting zodra een van die motoren omgezet wordt en de loc erop staat). De vaste referentie uit
/// <see cref="KruiswisselReferentie"/> (melder 24, motoren 13 en 10, bewezen verbindingen) wordt alleen gebruikt als de gebruiker kiest om gegevens uit
/// eerdere verkenningen te gebruiken.
/// </summary>
public sealed class KruisKennis
{
    public HashSet<int> Motoren { get; } = new();
    public HashSet<int> Middens { get; } = new();
    /// <summary>De vaste referentie (benen, bewezen verbindingen, melder 24, motor 13/10) is actief.</summary>
    public bool MetReferentie { get; set; }

    public bool IsMotor(int adres) => Motoren.Contains(adres);
    public bool OpKruis(IEnumerable<int> bezet) => Middens.Count > 0 && bezet.Any(Middens.Contains);

    /// <summary>De kennis die de vaste referentie al had (melder 24, motor 13 en 10); gebruikt als standaard in statische hulpmethoden en tests.</summary>
    public static KruisKennis Referentie()
    {
        var k = new KruisKennis { MetReferentie = true };
        k.Motoren.Add(KruiswisselReferentie.Motor13); k.Motoren.Add(KruiswisselReferentie.Motor10);
        k.Middens.Add(KruiswisselReferentie.Midden);
        return k;
    }

    /// <summary>"13+10", "13+10@24", "13+10, 3+4@30": elk groepje is een Engelse wissel; getallen na een plus zijn de motoren, na een @ de melder.</summary>
    public static KruisKennis Parse(string? tekst)
    {
        var k = new KruisKennis();
        if (string.IsNullOrWhiteSpace(tekst)) return k;
        foreach (var groep in tekst.Split(new[] { ',', ';', '/' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var delen = groep.Split('@', 2);
            foreach (var m in delen[0].Split(new[] { '+', ' ', '&' }, StringSplitOptions.RemoveEmptyEntries))
                if (int.TryParse(m.Trim(), out int a) && a > 0) k.Motoren.Add(a);
            if (delen.Length == 2 && int.TryParse(delen[1].Trim(), out int melder) && melder > 0) k.Middens.Add(melder);
        }
        return k;
    }

    public string MotorenTekst => string.Join("+", Motoren.OrderBy(x => x));
    public string MiddenTekst => string.Join(", ", Middens.OrderBy(x => x));
}

/// <summary>
/// BUG #135 (Marco 14-10 14:12, log 14:07-14:11): alle bewaking van de kruiswissel (motoren vooraf zetten, niet omzetten onder de loc) hing aan de vaste
/// referentie (<see cref="KruisKennis.MetReferentie"/>) en aan de vaste melder 24 / motor 13 / 10. In een verkenning vanaf nul (#128, #129) stond die uit:
/// opdracht 24 vooruit (13 afbuigend) werd overgeslagen en een verkenningsrit 15 -> 8 -> 24 reed met motor 13 rechtdoor de kruiswissel in (kortsluiting),
/// terwijl de eigen kaart al twee keer had gezien dat een loc vanaf melder 8 alleen met motor 13 afbuigend door de kruiswissel komt.
/// Dit hulpmiddel leidt uit de EIGEN kaart (<see cref="Baankaart.Doorgangen"/>) af welke motorstanden bij welke ingang bewezen zijn.
/// </summary>
public static class KruisBewijs
{
    /// <summary>De stand van alleen de kruismotoren.</summary>
    public static Configuratie Project(Configuratie c, IEnumerable<int> motoren) => new(motoren.Where(c.Bevat).ToArray());

    /// <summary>De configuratie zonder de kruismotoren (voor het vergelijken van de rest van de wisselstand).</summary>
    public static Configuratie ZonderMotoren(Configuratie c, IEnumerable<int> motoren) => new(c.Afbuigend.Except(motoren).ToArray());

    /// <summary>Alle standen van de kruismotoren waarmee de loc eerder via <paramref name="via"/> de kruiswissel <paramref name="midden"/> in- en doorreed.</summary>
    public static List<Configuratie> GezienBijIngang(Baankaart kaart, KruisKennis kruis, int midden, int via)
    {
        var res = new List<Configuratie>();
        foreach (var d in kaart.Doorgangen.Where(d => d.Melder == midden && d.Via == via))
            foreach (var s in d.Configuraties)
            {
                var p = Project(Configuratie.VanSleutel(s), kruis.Motoren);
                if (!res.Any(x => x.Equals(p))) res.Add(p);
            }
        return res;
    }

    /// <summary>Welke stand van de kruismotoren moet er gezet worden bij een ingang, gegeven wat daar eerder bewezen is? Null = niets veranderen
    /// (geen bewijs, de gewenste stand is zelf bewezen, of de enige bewezen standen vragen iets anders van een motor die vastligt).
    /// <paramref name="vast"/> zijn motoren die de rit zelf bewust zo wil (de opdracht of de proef): die worden nooit aangepast.</summary>
    public static Configuratie? KiesStand(IReadOnlyList<Configuratie> gezien, Configuratie gewenst, IReadOnlyCollection<int> motoren, ISet<int> vast)
    {
        if (gezien.Count == 0) return null;
        var gw = Project(gewenst, motoren);
        var kand = gezien.Where(g => vast.All(m => g.Bevat(m) == gw.Bevat(m))).ToList();
        if (kand.Count == 0 || kand.Any(g => g.Equals(gw))) return null;
        int Verschil(Configuratie g) => motoren.Count(m => g.Bevat(m) != gw.Bevat(m));
        return kand.OrderBy(Verschil).ThenBy(g => g.Afbuigend.Length).ThenBy(g => g.Sleutel, StringComparer.Ordinal).First();
    }

    /// <summary>De melders waar de loc vanaf <paramref name="van"/> in richting <paramref name="r"/> naartoe reed bij deze wisselstand. Eerst telt de
    /// EXACT gereden stand (kruismotoren inbegrepen); die is het meest betrouwbaar. Is die stand nooit gereden, dan telt de stand zonder de kruismotoren
    /// (de kruismotoren bepalen niet welke melder er VOOR de kruiswissel volgt). De eerste regel voorkomt dat een oude of afwijkende waarneming (bv. 8 -> 24
    /// met alleen motor 13 afbuigend, in de kaart van 14:11) de gewone lus 15 -> 8 -> 17 voor een kruiswissel-ingang laat doorgaan.</summary>
    public static List<int> VolgendeBijStand(Baankaart kaart, KruisKennis kruis, int van, Richting r, Configuratie cfg)
    {
        var exact = kaart.BekendeVolgende(van, r, cfg);
        if (exact.Count > 0) return exact;
        var rest = ZonderMotoren(cfg, kruis.Motoren);
        bool Past(Overgang o) => o.GezienBijConfiguraties.Any(s => ZonderMotoren(Configuratie.VanSleutel(s), kruis.Motoren).Equals(rest));
        return kaart.Overgangen.Where(o => o.Van == van && o.Richting == r && Past(o)).Select(o => o.Naar)
            .Concat(kaart.Overgangen.Where(o => o.Naar == van && o.Richting == r.Om() && Past(o)).Select(o => o.Van))
            .Distinct().OrderBy(x => x).ToList();
    }

    /// <summary>De kruiswissels waar de loc vanaf <paramref name="melder"/> volgens de kaart binnen één of twee stappen in rijdt, met de ingang (het been
    /// waarvandaan hij erin gaat). Twee stappen vooruit, zodat de motoren gezet zijn voordat de loc het laatste been inrijdt.</summary>
    public static List<(int Midden, int Ingang)> Ingangen(Baankaart kaart, KruisKennis kruis, int melder, Richting r, Configuratie cfg)
    {
        var res = new List<(int, int)>();
        if (kruis.Middens.Count == 0 || kruis.Middens.Contains(melder)) return res;
        foreach (var y in VolgendeBijStand(kaart, kruis, melder, r, cfg))
        {
            if (kruis.Middens.Contains(y)) { if (!res.Contains((y, melder))) res.Add((y, melder)); continue; }
            foreach (var z in VolgendeBijStand(kaart, kruis, y, r, cfg))
                if (kruis.Middens.Contains(z) && !res.Contains((z, y))) res.Add((z, y));
        }
        return res;
    }

    public static string StandTekst(IEnumerable<int> motoren, Configuratie c) =>
        string.Join(" en ", motoren.OrderBy(x => x).Select(m => $"motor {m} {(c.Bevat(m) ? "afbuigend" : "rechtdoor")}"));
}
