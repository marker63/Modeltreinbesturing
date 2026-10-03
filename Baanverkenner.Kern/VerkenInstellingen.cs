namespace Baanverkenner.Kern;

/// <summary>Alle instellingen van één verkenning. Wordt ook in het voortgangsbestand
/// bewaard, zodat een hervatte verkenning met exact dezelfde waarden verdergaat.</summary>
public class VerkenInstellingen
{
    // ---- De testloc ----
    /// <summary>Decoderadres van de loc waarmee getest wordt.</summary>
    public int LocAdres { get; set; } = 3;
    /// <summary>Aantal snelheidsstappen van de decoder (14/28/126) - bepaalt bij Dinamo
    /// welk snelheidscommando gestuurd wordt.</summary>
    public int LocStappen { get; set; } = 28;
    /// <summary>"Redelijke snelheid" voor de verkenningsritten (in decoderstappen).</summary>
    public int Verkensnelheid { get; set; } = 8;
    /// <summary>Lage snelheid voor het precies neerzetten op een melder.</summary>
    public int Kruipsnelheid { get; set; } = 3;

    // ---- Wissels ----
    /// <summary>Alle adressen in dit bereik worden één voor één geprobeerd.</summary>
    public int WisselAdresVan { get; set; } = 1;
    public int WisselAdresTot { get; set; } = 32;
    /// <summary>Wachttijd na elk wisselcommando (spoel/motor moet klaar zijn).</summary>
    public int WisselPauzeMs { get; set; } = 600;

    // ---- Dinamo / blokken ----
    /// <summary>Dinamo-blokadressen die geprobeerd worden bij het koppelen van secties aan
    /// blokken, bijv. "1-16" of "1-8,12,20-24".</summary>
    public string DinamoBlokken { get; set; } = "1-16";
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
    public int MinSecondenTussenMelders { get; set; } = 10;
    /// <summary>Als tijdens het rijden GEEN enkele melder meer bezet is gedurende deze tijd,
    /// wordt dat als kortsluiting/ontsporing behandeld (stroomdetectie valt weg zonder
    /// stroom). Verhogen als de baan onbewaakte stukken heeft (bijv. wissels zonder
    /// melder) waar de loc even "onzichtbaar" is.</summary>
    public double LegeMeldersSeconden { get; set; } = 3.0;
    /// <summary>Maximaal aantal kortsluitingen op dezelfde plek tijdens het oplossen van
    /// een kortsluitpunt; daarna geeft de verkenner die plek op.</summary>
    public int MaxKortsluitingenPerPlek { get; set; } = 3;
    /// <summary>Een melder telt pas als bezet/vrij als hij zo lang stabiel is (filtert
    /// spookmeldingen/flikkeren).</summary>
    public int OntdenderMs { get; set; } = 300;
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

    public IEnumerable<int> WisselAdressen()
    {
        int van = Math.Max(1, Math.Min(WisselAdresVan, WisselAdresTot));
        int tot = Math.Max(WisselAdresVan, WisselAdresTot);
        for (int a = van; a <= tot; a++) yield return a;
    }
}
