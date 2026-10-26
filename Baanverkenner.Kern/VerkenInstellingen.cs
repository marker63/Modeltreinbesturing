namespace Baanverkenner.Kern;

/// <summary>Alle instellingen van één verkenning. Wordt ook in het voortgangsbestand
/// bewaard, zodat een hervatte verkenning met exact dezelfde waarden verdergaat.</summary>
public class VerkenInstellingen
{
    // ---- De testloc ----
    /// <summary>Decoderadres van de loc waarmee getest wordt.</summary>
    public int LocAdres { get; set; } = 3;
    /// <summary>BUG #129: Engelse wissels (kruiswissels) van deze baan: motoren per wissel, bijv. "13+10" of "13+10, 3+4"; optioneel met melder "13+10@24". Leeg = geen.</summary>
    public string KruisMotoren { get; set; } = "";
    /// <summary>Aantal snelheidsstappen van de decoder (14/28/126) - bepaalt bij Dinamo
    /// welk snelheidscommando gestuurd wordt.</summary>
    public int LocStappen { get; set; } = 28;
    /// <summary>"Redelijke snelheid" voor de verkenningsritten (in decoderstappen).</summary>
    public int Verkensnelheid { get; set; } = 8;
    /// <summary>Lage snelheid voor het precies neerzetten op een melder.</summary>
    public int Kruipsnelheid { get; set; } = 3;
    /// <summary>BUG #59: de verkenner past de verkensnelheid zelf voorzichtig aan (lager bij heel korte secties, iets hoger als alle secties lang zijn). Standaard UIT: in de simulator gaf een heel korte sectie na verlagen een mislukte terugrit.</summary>
    public bool AutoSnelheid { get; set; } = false;

    // ---- Wissels ----
    /// <summary>Alle adressen in dit bereik worden één voor één geprobeerd.</summary>
    public int WisselAdresVan { get; set; } = 1;
    public int WisselAdresTot { get; set; } = 32;
    /// <summary>Wachttijd na elk wisselcommando (spoel/motor moet klaar zijn).</summary>
    public int WisselPauzeMs { get; set; } = 600;
    /// <summary>BUG #352: hoe vaak elk wisselcommando achter elkaar naar Dinamo gaat. Dinamo voert elke keer een aparte puls uit; met 6 liep de wachtrij in Dinamo op 26-10 tot minuten
    /// achter. Standaard 1. Gaat de kruiswissel dan niet goed om, dan helpt eerder een langere pulstijd (KruisPulsTijd) dan meer herhalingen.</summary>
    public int WisselHerhalingen { get; set; } = 1;
    /// <summary>BUG #352: pulstijd voor de kruismotoren (KruisMotoren) in 1/60 s; standaard 24 = 0,4 s (gewone wissels: 12 = 0,2 s, de Dinamo-standaard).</summary>
    public int KruisPulsTijd { get; set; } = 24;

    // ---- Dinamo / blokken ----
    /// <summary>Dinamo-blokadressen die geprobeerd worden bij het koppelen van secties aan
    /// blokken, bijv. "1-16" of "1-8,12,20-24".</summary>
    public string DinamoBlokken { get; set; } = "1-16";
    /// <summary>BUG #235 (Marco 18-10 14:06: "je meldt wissel 6 afbuigend maar hij stond rechtdoor"): wissels waarvan het Dinamo-commando 'afbuigend' op de baan rechtdoor
    /// is (motor andersom aangesloten), bijv. "6". Alleen voor de meldingen in logboek en rapport: de verkenner blijft intern met de Dinamo-commando's rekenen, zodat de
    /// kaart klopt met wat er verstuurd wordt. Leeg = geen.</summary>
    public string OmgekeerdAangesloten { get; set; } = "";
    /// <summary>BUG #239: sleutels van eenmalige aanpassingen (<see cref="EenmaligeAanpassingen"/>) die op deze instellingen al zijn uitgevoerd.</summary>
    public List<string> UitgevoerdeAanpassingen { get; set; } = new();

    /// <summary>BUG #244 (autolog 18-10 15:25-15:35): bij het hervatten gebruikt de verkenner de instellingen van de onderbroken verkenning (<paramref name="vast"/>). Wissel 6
    /// 'omgekeerd aangesloten' (eenmalige aanpassing #239, in het scherm gezet) kwam daardoor niet in de hervatte verkenning: geen uitleg bij 'Adres 6: afbuigend'. Instellingen die
    /// alleen de meldingen betreffen, komen daarom uit het scherm (<paramref name="scherm"/>).</summary>
    public static void NeemMeldingInstellingenOver(VerkenInstellingen vast, VerkenInstellingen scherm)
    {
        vast.OmgekeerdAangesloten = scherm.OmgekeerdAangesloten;
        vast.GekoppeldeWissels = scherm.GekoppeldeWissels;   // BUG #259
        foreach (var s in scherm.UitgevoerdeAanpassingen) if (!vast.UitgevoerdeAanpassingen.Contains(s)) vast.UitgevoerdeAanpassingen.Add(s);
    }

    /// <summary>BUG #259 (Marco 20-10 18:23-18:33: "5 en 6 vormen een overloopwisselset", "altijd samen", "denk vooral aan het omgekeerd aansturen van wissel 6"): paren wissels die op de baan
    /// altijd dezelfde stand hebben, bijv. "5+6". De eerste is de meester: wordt die gezet, dan gaat de tweede mee naar dezelfde stand OP DE BAAN (bij een omgekeerd aangesloten wissel is dat
    /// het tegenovergestelde Dinamo-commando). De tweede wordt nooit los gezet of getest. Leeg = geen.</summary>
    public string GekoppeldeWissels { get; set; } = "";

    /// <summary>BUG #259: (meester, volger) uit <see cref="GekoppeldeWissels"/>.</summary>
    public List<(int Meester, int Volger)> GekoppeldeParen()
    {
        var res = new List<(int, int)>();
        foreach (var deel in GekoppeldeWissels.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var ab = deel.Split('+');
            if (ab.Length == 2 && int.TryParse(ab[0].Trim(), out var m) && int.TryParse(ab[1].Trim(), out var v) && m > 0 && v > 0 && m != v
                && !res.Any(x => x.Item1 == v || x.Item2 == v || x.Item2 == m)) res.Add((m, v));
        }
        return res;
    }

    /// <summary>BUG #235: de adressen uit <see cref="OmgekeerdAangesloten"/>.</summary>
    public HashSet<int> OmgekeerdeWissels()
        => OmgekeerdAangesloten.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(x => int.TryParse(x.Trim(), out var a) ? a : 0).Where(a => a > 0).ToHashSet();
    /// <summary>Hoogste meldernummer dat bij de start opgevraagd wordt (alleen als de
    /// hardware de melderstand kan opvragen, zoals Dinamo).</summary>
    public int HoogsteMelder { get; set; } = 64;
    /// <summary>Blokproef vanaf een onbekende plek (bij de start): max. wachttijd per blok
    /// op een melderwijziging.</summary>
    public int BlokproefLangSeconden { get; set; } = 30;
    /// <summary>Blokproef net na het inrijden van een nieuwe sectie (korte weg terug naar
    /// de vorige melder): max. wachttijd per blok.</summary>
    public int BlokproefKortSeconden { get; set; } = 6;
    /// <summary>Na het helemaal binnenrijden van een nieuwe sectie nog zo lang doorrijden
    /// vóór de blokproef - zodat de loc niet meer met zijn wielen op een (onbewaakt)
    /// wisselstuk van het vorige blok staat.</summary>
    public double BlokproefInrijSeconden { get; set; } = 1.5;

    // ---- Veiligheid / tijden ----
    /// <summary>Maximale tijd tussen twee melderwijzigingen tijdens het rijden. Daarna:
    /// "doodlopend" (loc staat tegen een stootjuk).</summary>
    public int MaxSecondenTussenMelders { get; set; } = 30;
    /// <summary>Slimme time-out: zodra er reistijden gemeten zijn, wacht de verkenner
    /// hooguit 3x de langste gemeten reistijd (maar nooit korter dan MinSecondenTussenMelders
    /// en nooit langer dan MaxSecondenTussenMelders).</summary>
    public bool SlimmeTimeout { get; set; } = true;
    /// <summary>BUG #226 (Marco 18-10 12:31): tijdens een verkenning elke zoveel minuten automatisch een tussenstand (rapport, logboek, hardwarelog, baankaart) naar
    /// <see cref="AutoTussenstandMap"/> schrijven, zodat de logs zonder handwerk geanalyseerd kunnen worden. 0 = uit.</summary>
    public int AutoTussenstandMinuten { get; set; } = 10;
    /// <summary>BUG #226: map voor de automatische tussenstanden (wordt aangemaakt als hij nog niet bestaat). BUG #233 (Marco 18-10 13:50): standaard C:\Marco zelf, niet C:\Marco\Autolog.</summary>
    public string AutoTussenstandMap { get; set; } = @"C:\Marco";
    public int MinSecondenTussenMelders { get; set; } = 10;
    /// <summary>Als tijdens het rijden GEEN enkele melder meer bezet is gedurende deze tijd,
    /// wordt dat als kortsluiting/ontsporing behandeld (stroomdetectie valt weg zonder
    /// stroom). Verhogen als de baan onbewaakte stukken heeft (bijv. wissels zonder
    /// melder) waar de loc even "onzichtbaar" is.</summary>
    public double LegeMeldersSeconden { get; set; } = 3.0;
    /// <summary>Maximaal aantal kortsluitingen op dezelfde plek tijdens het oplossen van
    /// een kortsluitpunt; daarna geeft de verkenner die plek op.</summary>
    public int MaxKortsluitingenPerPlek { get; set; } = 3;
    /// <summary>BUG #63: na een kortsluiting met een net afbuigend gezet wissel: hoeveel andere
    /// wissels er (naast dat wissel) één voor één ook op afbuigend worden geprobeerd (overloopwissels).</summary>
    public int MaxPartnerProeven { get; set; } = 8;
    /// <summary>Een melder telt pas als bezet/vrij als hij zo lang stabiel is (filtert
    /// spookmeldingen/flikkeren).</summary>
    public int OntdenderMs { get; set; } = 300;
    /// <summary>BUG #172 (Marco 16-10 20:23): werkwijze 'Stap voor stap'. De loc rijdt na een opdracht niet meer terug naar de startmelder maar blijft waar hij is;
    /// elke basis- en proefrit is één melder lang (twee als daarna de kruiswissel volgt) en de dichtstbijzijnde open plek komt eerst aan de beurt.
    /// De kruiswissel krijgt een eigen proef met de vier standen van de kruismotoren.</summary>
    public bool StapVoorStap { get; set; }
    /// <summary>BUG #195 (Marco 17-10 18:06, optie 6 'Rondrijden'): eerst alleen de melders en hun vervolgen ontdekken, zonder bloknummers. De loc rijdt vanaf zijn plek in één richting met
    /// rijcommando's naar ALLE blokken tot een kopspoor (dan keren) of een lus; daarna gaat steeds één wisselmotor om en wordt het traject opnieuw gereden tot een nieuwe melder, een lus
    /// of een kortsluiting. Een kruiswissel is een gewone wissel. Pas als alle melders met hun vervolgen bekend zijn, worden de Dinamo-blokken gezocht. Sluit 'Stap voor stap' uit.</summary>
    public bool Rondrit { get; set; }
    /// <summary>BUG #175 (Marco 17-10 09:44): bij 'Stap voor stap' beginnen de proefritten één melder vóór de startmelder en rijdt de loc na de proef daar ook naartoe terug,
    /// zodat hij niet op de wissels bij de startmelder staat terwijl die omgezet worden en de wissels vóór de startmelder ook gemeten worden.</summary>
    public bool StapAanloop { get; set; } = true;
    /// <summary>BUG #175: seconden die de loc bij 'Stap voor stap' na het bereiken van de nieuwe melder nog doorrijdt, zodat hij de wisseltongen in die sectie berijdt
    /// en een kortsluiting bij een wissel naar de samenvoeging toe zichtbaar wordt. 0 = meteen stoppen.</summary>
    public double StapDoorrijSeconden { get; set; } = 2.0;   // BUG #178: Marco 17-10 11:18: na het zien van de melder zeker 2 s doorrijden

    /// <summary>Basisrit twee keer rijden en vergelijken ("voor de zekerheid").</summary>
    public bool BasisritHerhalen { get; set; } = true;
    /// <summary>Hoeveel melders er na de eerste afwijkende melder nog doorgereden wordt
    /// voordat de proefrit stopt. Standaard 0: de rest verkent een aparte opdracht, en de
    /// weg terug gaat dan alleen over de net omgezette wissel (geen risico op een van
    /// achteren opengereden wissel onderweg).</summary>
    public int ExtraMeldersNaAfwijking { get; set; } = 0;
    /// <summary>Veiligheidsgrens: maximaal aantal melders in één rit.</summary>
    public int MaxMeldersPerRit { get; set; } = 60;
    /// <summary>Veiligheidsgrens: maximaal aantal verkenopdrachten.</summary>
    public int MaxOpdrachten { get; set; } = 300;

    /// <summary>Ontleedt DinamoBlokken ("1-8,12,20-24") tot een gesorteerde lijst.</summary>
    public List<int> DinamoBlokLijst()
    {
        var res = new SortedSet<int>();
        foreach (var deel in (DinamoBlokken ?? "").Split(',', ';', ' '))
        {
            var d = deel.Trim();
            if (d.Length == 0) continue;
            var streep = d.Split('-');
            if (streep.Length == 2 && int.TryParse(streep[0], out int van) && int.TryParse(streep[1], out int tot))
            {
                if (tot < van) (van, tot) = (tot, van);
                for (int i = Math.Max(1, van); i <= Math.Min(tot, 256); i++) res.Add(i);
            }
            else if (int.TryParse(d, out int enkel) && enkel >= 1 && enkel <= 256) res.Add(enkel);
        }
        return res.ToList();
    }

    /// <summary>BUG #209/#210: wisseladressen die op de baan niet bestaan (Marco 18-10: wissel 11 bestaat niet). Ze worden niet getest, niet omgezet en uit een bestaande kaart gehaald.
    /// Komma-gescheiden lijst.</summary>
    public string NietBestaandeWissels { get; set; } = "";

    public List<int> NietBestaandeAdressen() =>
        (NietBestaandeWissels ?? "").Split(',', ';', ' ').Select(x => x.Trim()).Where(x => int.TryParse(x, out _)).Select(int.Parse).Distinct().ToList();

    public IEnumerable<int> WisselAdressen()
    {
        int van = Math.Max(1, Math.Min(WisselAdresVan, WisselAdresTot));
        int tot = Math.Max(WisselAdresVan, WisselAdresTot);
        var geen = NietBestaandeAdressen();
        for (int a = van; a <= tot; a++) if (!geen.Contains(a)) yield return a;
    }
}
