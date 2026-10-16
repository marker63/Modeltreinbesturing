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

    /// <summary>Alle standen van de kruismotoren waarmee de loc eerder via <paramref name="via"/> de kruiswissel <paramref name="midden"/> in- en doorreed.
    /// BUG #165 (log 16-10 17:41: de kaart kende 14 -> 24 -> 8 ook met motor 13 rechtdoor, één keer, naast vele keren afbuigend; daardoor zette de verkenner motor 13 niet
    /// vooraf en bleef de loc op de kruiswissel hangen): een kruisstand telt alleen mee als hij minstens half zo vaak gezien is als de meest geziene kruisstand van die
    /// doorgang (tellers per configuratie, opgeteld per kruisstand). Zonder tellers (oude kaart) telt alles.</summary>
    public static List<Configuratie> GezienBijIngang(Baankaart kaart, KruisKennis kruis, int midden, int via, int? volgende = null)
    {
        var res = new List<Configuratie>();
        foreach (var d in kaart.Doorgangen.Where(d => d.Melder == midden && d.Via == via && (volgende is null || d.Volgende == volgende)))
        {
            var tel = new Dictionary<string, (Configuratie P, int N)>();
            foreach (var s in d.Configuraties)
            {
                var p = Project(Configuratie.VanSleutel(s), kruis.Motoren);
                int n = d.Tellers.Count == 0 ? 1 : d.Tellers.GetValueOrDefault(s, 1);
                tel[p.Sleutel] = tel.TryGetValue(p.Sleutel, out var o) ? (p, o.N + n) : (p, n);
            }
            int max = tel.Count == 0 ? 0 : tel.Values.Max(t => t.N);
            foreach (var (p, n) in tel.Values)
                if ((d.Tellers.Count == 0 || n * 2 >= max) && !res.Any(x => x.Equals(p))) res.Add(p);
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
        var rest = ZonderMotoren(cfg, kruis.Motoren);
        bool Past(Overgang o) => o.GezienBijConfiguraties.Any(s => ZonderMotoren(Configuratie.VanSleutel(s), kruis.Motoren).Equals(rest));
        if (exact.Count > 0)
        {
            // BUG #148 (Marco 18:50, log 18:46): de kaart had voor 17 -> 8 ook een waarneming met alleen wissel 14 afbuigend (ruis), dus gold 8 -> 17 als
            // enig vervolg en motor 13 werd niet vooraf gezet; 15 -> 8 -> 24 gaf kortsluiting. Is er bij een NIET-lege stand (zonder de kruismotoren)
            // ook een kruiswissel-vervolg dat exact bij die stand gezien is, dan telt dat vervolg mee. Bij de lege stand (de gewone lus) niet.
            if (rest.Afbuigend.Length == 0) return exact;
            var extra = kaart.Overgangen.Where(o => o.Van == van && o.Richting == r && Past(o)).Select(o => o.Naar)
                .Concat(kaart.Overgangen.Where(o => o.Naar == van && o.Richting == r.Om() && Past(o)).Select(o => o.Van))
                .Where(kruis.Middens.Contains);
            return exact.Concat(extra).Distinct().OrderBy(x => x).ToList();
        }
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

    /// <summary>BUG #165 (log 16-10 17:47-17:50): de adrestest reed 15 -> 8 -> 24 -> 14 met motor 13 rechtdoor (de opdracht kende alleen wissel 14) en gaf elke keer kortsluiting
    /// op de kruiswissel; die kortsluitingen werden daarna aan wissel 1 en 3 toegeschreven (valse bevindingen). Zonder referentie gebruikt de rijstand nu het eigen bewijs van
    /// de kaart: voor elke doorgang X -> midden -> Y in het voorspelde pad de kruisstand waarmee die eerder gereden is. Motoren die de opdracht zelf vastlegt blijven staan.</summary>
    public static Configuratie PasEigenBewijsToe(Baankaart kaart, KruisKennis kruis, List<int> pad, Configuratie c, ISet<int> vast, out List<string> uitleg)
    {
        uitleg = new List<string>();
        if (kruis.Middens.Count == 0 || kruis.Motoren.Count == 0) return c;
        var motoren = kruis.Motoren.OrderBy(x => x).ToList();
        var gezet = new HashSet<int>();
        for (int i = 1; i + 1 < pad.Count; i++)
        {
            if (!kruis.Middens.Contains(pad[i])) continue;
            var gezien = GezienBijIngang(kaart, kruis, pad[i], pad[i - 1], pad[i + 1]);
            if (KiesStand(gezien, c, motoren, vast) is not { } stand) continue;
            foreach (var m in motoren)
            {
                if (vast.Contains(m) || gezet.Contains(m) || c.Bevat(m) == stand.Bevat(m)) continue;
                c = c.MetStand(m, stand.Bevat(m)); gezet.Add(m);
                uitleg.Add($"{pad[i - 1]} -> {pad[i]} -> {pad[i + 1]} is eerder alleen gereden met motor {m} {(stand.Bevat(m) ? "afbuigend" : "rechtdoor")}");
            }
        }
        return c;
    }

    public static string StandTekst(IEnumerable<int> motoren, Configuratie c) =>
        string.Join(" en ", motoren.OrderBy(x => x).Select(m => $"motor {m} {(c.Bevat(m) ? "afbuigend" : "rechtdoor")}"));
}
