namespace Baanverkenner.Kern;

public partial class BaanVerkenner
{
    /// <summary>BUG #305 (Marco 25-10 10:05: "Het is een zelflerend programma ... geen vragen meer om de loc steeds terug te zetten op de startmelder maar zelf een manier vinden
    /// om het op te lossen, als het na 10 minuten nog niet gelukt is dan pas een actie van mij vragen"): zo lang probeert de verkenner het zelf voordat hij de gebruiker vraagt.</summary>
    internal static readonly TimeSpan ZelfOplossenDuur = TimeSpan.FromMinutes(10);

    private bool _zelfOplossenBezig;

    /// <summary>BUG #305: probeert tot <see cref="ZelfOplossenDuur"/> lang zelf de loc (zonder kortsluiting) op <paramref name="doel"/> te krijgen, in rondes:
    /// (1) wachten tot een kortsluiting vanzelf weggaat; (2) wissels omzetten (eerst de gewone zoekroutine, daarna ook de wissels bij de melders waar de loc staat:
    /// rijdt de loc vanaf een zijtak naar de samenvoeging, dan heft omzetten de kortsluiting juist op, BUG #169); (3) staat de loc op de grens van twee melders, doorkruipen
    /// tot hij op één melder staat; (4) geen melder bezet: kruipend zoeken; (5) vanaf de melder waar hij staat zelf naar het doel rijden over een bekende weg.
    /// Geeft true als de loc op het doel staat.</summary>
    private async Task<bool> ZelfOplossen(int doel, string aanleiding)
    {
        if (_zelfOplossenBezig) return false;
        _zelfOplossenBezig = true;
        try
        {
            var eind = _klok.Nu + ZelfOplossenDuur;
            _log.Info($"Zelf oplossen (BUG #305, nog niet getest op de baan): de verkenner probeert maximaal {ZelfOplossenDuur.TotalMinutes:0} minuten zelf de loc op melder {doel} te krijgen voordat hij om hulp vraagt. Aanleiding: {EersteRegel(aanleiding)}");
            var onderLocGeprobeerd = new HashSet<string>();
            _zelfVermijd.Clear();   // BUG #313
            int ronde = 0;
            string? vorigeUitkomst = null;
            while (_klok.Nu < eind)
            {
                ronde++;
                // Zelfde uitkomst als de vorige ronde: dan een vastgehouden 'klem-oplossing' (#194/#218) loslaten, anders gebeurt steeds precies hetzelfde
                // (simulatie: wissel 6 en 9 bleven afbuigend en de loc reed elke ronde weer 4 -> 129 -> 10).
                string uitkomst = $"{Lijst(_monitor.Bezet)}|{string.Join(",", HuidigeAlarmBlokken())}";
                if (ronde > 1 && uitkomst == vorigeUitkomst && _klemOplossing.Count > 0)
                {
                    _log.Info($"Zelf oplossen, ronde {ronde}: zelfde situatie als de vorige ronde; de vastgehouden standen van wissel {Lijst(_klemOplossing.Keys.OrderBy(x => x).ToList())} worden losgelaten (BUG #305).");
                    _klemOplossing.Clear();
                }
                vorigeUitkomst = uitkomst;
                try
                {
                    if (await ZelfOplossenRonde(doel, ronde, onderLocGeprobeerd))
                    {
                        _log.Vondst($"Zelf opgelost in ronde {ronde}: de loc staat op melder {doel} (BUG #305).");
                        return true;
                    }
                }
                catch (NavigatieFout nf)
                {
                    _log.Waarschuwing($"Zelf oplossen, ronde {ronde}: {nf.Message}");
                    try { await StopLoc(); } catch (NavigatieFout) { }
                }
                await Wacht(TimeSpan.FromSeconds(3));
            }
            _log.Waarschuwing($"Zelf oplossen lukte niet binnen {ZelfOplossenDuur.TotalMinutes:0} minuten ({ronde} rondes): nu pas hulp van de gebruiker (BUG #305).");
            return false;
        }
        finally { _zelfOplossenBezig = false; }
    }

    private static string EersteRegel(string t) => t.Split('\n')[0];

    private async Task<bool> ZelfOplossenRonde(int doel, int ronde, HashSet<string> onderLocGeprobeerd)
    {
        // (1) kortsluiting: even wachten (Dinamo zet het alarm soms zelf uit)
        var tot = _klok.Nu + TimeSpan.FromSeconds(8);
        while (HuidigeAlarmBlokken().Count > 0 && _klok.Nu < tot) await Wacht(TimeSpan.FromMilliseconds(300));
        if (HuidigeAlarmBlokken().Count > 0)
        {
            // (2a) de gewone zoekroutine (laat de wissels onder de loc staan)
            if (ronde == 1 && await ProbeerAlarmWegTeKrijgen($"zelf oplossen (ronde {ronde})")) { }
            // (2b) de wissels bij de bezette melders, één voor één (elke wissel en stand maar één keer)
            else if (!await WisselOnderLocProberen(onderLocGeprobeerd)
                     // BUG #313 (autolog 25-10 12:45:08-12:49:01): daarna 22 rondes niets gedaan. Is alles bij de loc geprobeerd, dan opnieuw de volledige zoekroutine (alle wissels), en
                     // de lijst van geprobeerde wissels bij de loc begint weer leeg (de stand is intussen veranderd).
                     && !(onderLocGeprobeerd.Count > 0 && AllesBijLocGeprobeerd(onderLocGeprobeerd) && await NogEensAlles(onderLocGeprobeerd, ronde)))
            {
                _log.Info($"Zelf oplossen, ronde {ronde}: de kortsluiting in blok {string.Join(" en ", HuidigeAlarmBlokken())} blijft; volgende ronde.");
                return false;
            }
        }
        await Wacht(TimeSpan.FromSeconds(1));
        _monitor.Bijwerken();
        var bezet = _monitor.Bezet;
        if (bezet.Count == 1 && bezet.Contains(doel)) return true;

        // (3) op de grens van twee melders (de loc is niet langer dan een melder): doorkruipen naar één melder
        if (bezet.Count == 2 && !Kruis.OpKruis(bezet))
        {
            var r = _kaart.Ritten.LastOrDefault()?.Richting ?? Richting.Vooruit;
            int naar = bezet.Contains(doel) ? doel : bezet.OrderByDescending(m => _kaart.Ritten.LastOrDefault()?.Reeks.LastIndexOf(m) ?? -1).First();
            int ander = bezet.First(m => m != naar);
            _log.Info($"Zelf oplossen, ronde {ronde}: de loc staat op de grens van melder {ander} en {naar}; hij kruipt naar melder {naar}.");
            await Nastellen(naar, r, achter: ander);
            _monitor.Bijwerken();
            bezet = _monitor.Bezet;
            if (HuidigeAlarmBlokken().Count > 0) return false;
            if (bezet.Count == 1 && bezet.Contains(doel)) return true;
        }

        // (4) geen melder bezet: kruipend zoeken (eerst de laatste rijrichting, dan andersom)
        if (bezet.Count == 0)
        {
            var r = _kaart.Ritten.LastOrDefault()?.Richting ?? Richting.Vooruit;
            if (ronde % 2 == 0) r = r.Om();
            _log.Info($"Zelf oplossen, ronde {ronde}: geen melder bezet; de loc kruipt {r.Tekst()} tot een melder bezet raakt.");
            await KruipTotMelder(r, TimeSpan.FromSeconds(15));
            _monitor.Bijwerken();
            bezet = _monitor.Bezet;
            if (bezet.Count == 1 && bezet.Contains(doel)) return true;
        }

        // (5) op één melder: zelf naar het doel over een bekende weg
        if (bezet.Count == 1 && HuidigeAlarmBlokken().Count == 0)
        {
            _ingang = null;
            int van313 = bezet.First();
            var eerste313 = StapWeg(_kaart, Kruis, van313, doel, _zelfVermijd)?.FirstOrDefault();
            if (await ProbeerZelfNaarMelder(doel, _zelfVermijd)) return true;
            // BUG #313 (autolog 25-10 12:39:15, 12:41:02, 12:43:23): drie keer dezelfde weg 129 -> 14, drie keer naar 10. Een eerste stap die mislukte, wordt bij het zelf oplossen niet
            // nog eens genomen: de volgende ronde zoekt een andere weg (bijv. 129 -> 24 -> 21 ... -> 13).
            if (eerste313 is { } e313 && _zelfVermijd.Add((e313.Van, e313.Naar)))
                _log.Info($"Zelf oplossen, ronde {ronde}: de stap {e313.Van} -> {e313.Naar} lukte niet; de volgende ronde zoekt een andere weg (BUG #313, nog niet getest op de baan).");
            onderLocGeprobeerd.Clear();
        }
        return false;
    }

    /// <summary>BUG #313: stappen die bij het zelf oplossen mislukten (van, naar); de weg naar het doel gaat er niet meer over.</summary>
    private readonly HashSet<(int, int)> _zelfVermijd = new();

    private bool AllesBijLocGeprobeerd(HashSet<string> geprobeerd)
    {
        var bezet = _monitor.Bezet;
        string bij = string.Join(",", bezet.OrderBy(x => x));
        // BUG #315: alle wissels (niet alleen die bij de loc) zijn in de huidige stand een keer omgezet geprobeerd
        bool los = _ins.WisselAdressen().Where(a => !IsVolger(a))
            .All(a => geprobeerd.Contains($"{a}:{!(_wisselStand.TryGetValue(a, out var st) && st)}:{bij}"));
        var rs = _laatsteKortsluitContext?.Stand;
        bool combi = rs is null || !rs.Afbuigend.Any() || _ins.WisselAdressen().Where(a => !IsVolger(a) && !rs.Bevat(a)).All(a => geprobeerd.Contains($"C{a}:{rs.Sleutel}:{bij}"));
        return los && combi;
    }

    private async Task<bool> NogEensAlles(HashSet<string> geprobeerd, int ronde)
    {
        _log.Info($"Zelf oplossen, ronde {ronde}: alle wissels bij de loc zijn geprobeerd; nu weer de volledige zoekroutine en daarna de wissels bij de loc opnieuw (BUG #313).");
        geprobeerd.Clear();
        return await ProbeerAlarmWegTeKrijgen($"zelf oplossen (ronde {ronde})");
    }

    /// <summary>BUG #305: zet één nog niet geprobeerde wissel bij een bezette melder om en kijkt of de kortsluiting weggaat (zo niet: terug). Geeft true als het alarm weg is.</summary>
    private async Task<bool> WisselOnderLocProberen(HashSet<string> geprobeerd)
    {
        var bezet = _monitor.Bezet;
        var bijLoc = WisselsBijBezet(_kaart, bezet).Where(a => _ins.WisselAdressen().Contains(a) && !IsVolger(a)).ToHashSet();
        // BUG #315 (Marco 25-10 18:12: "ik zie achter elkaar zelf oplossen ronde x, maar ik hoor geen wissels schakelen"): in een verse verkenning kent de kaart bij melder 129 nog geen
        // wissels; er was dan niets te proberen en 28 rondes lang gebeurde er niets. Nu eerst de wissels bij de loc, dan de wissels die nu afbuigend staan (die zijn het meest verdacht),
        // dan alle andere; elke ronde één.
        var kandidaten = bijLoc.OrderBy(a => a)
            .Concat(_ins.WisselAdressen().Where(a => !bijLoc.Contains(a) && !IsVolger(a) && _wisselStand.TryGetValue(a, out var af) && af).OrderBy(a => a))
            .Concat(_ins.WisselAdressen().Where(a => !bijLoc.Contains(a) && !IsVolger(a) && !(_wisselStand.TryGetValue(a, out var af2) && af2)).OrderBy(a => a))
            .ToList();
        // BUG #315: daarna combinaties. Om 12:19 haalde wissel 10 de kortsluiting na 14 -> 129 -> 24 pas weg terwijl wissel 5 afbuigend bleef (de stand van de rit); de zoekroutine had 5
        // eerst teruggezet en daarna elke andere wissel alleen geprobeerd. Zijn alle wissels los geprobeerd, dan nog eens elke wissel met de afbuigende wissels van de rit weer afbuigend.
        var ritStand = _laatsteKortsluitContext?.Stand;
        bool alleLos = kandidaten.All(a => geprobeerd.Contains($"{a}:{!(_wisselStand.TryGetValue(a, out var s0) && s0)}:{string.Join(",", bezet.OrderBy(x => x))}"));
        if (alleLos && ritStand is { } rs && rs.Afbuigend.Any(x => _ins.WisselAdressen().Contains(x) && !IsVolger(x)))
        {
            foreach (var a in kandidaten.Where(x => !rs.Bevat(x)))
            {
                string sleutelC = $"C{a}:{rs.Sleutel}:{string.Join(",", bezet.OrderBy(x => x))}";
                if (!geprobeerd.Add(sleutelC)) continue;
                foreach (var x in rs.Afbuigend.Where(x => _ins.WisselAdressen().Contains(x) && !IsVolger(x) && !(_wisselStand.TryGetValue(x, out var sx) && sx) && !KlemStandVerboden(x, true))) await ZetWissel(x, true);   // BUG #320
                bool nuC = _wisselStand.TryGetValue(a, out var stc) && stc;
                _log.Info($"Zelf oplossen: combinatie - wissel {Lijst(rs.Afbuigend.ToList())} afbuigend (de stand van de rit) met wissel {a} naar {(!nuC ? "afbuigend" : "rechtdoor")} (BUG #315).");
                await ZetWissel(a, !nuC);
                for (int h = 1; h <= 10 && LaatsteCommandoTeruggemeld(a) == false && HuidigeAlarmBlokken().Count > 0; h++) { await Wacht(TimeSpan.FromSeconds(3)); await ZetWissel(a, !nuC, forceer: true); }
                await Wacht(TimeSpan.FromSeconds(4));
                if (HuidigeAlarmBlokken().Count == 0 && !_alarmWegVoorEigenTerugmelding)
                {
                    _log.Vondst($"De kortsluiting ging weg met wissel {Lijst(rs.Afbuigend.ToList())} afbuigend en wissel {a} {(!nuC ? "afbuigend" : "rechtdoor")} (zelf oplossen, combinatie, BUG #315). Nog geen bewijs: een rit zonder kortsluiting bevestigt het.");
                    return true;
                }
                await ZetWissel(a, nuC);
                return false;
            }
        }
        foreach (var a in kandidaten)
        {
            bool nu = _wisselStand.TryGetValue(a, out var st) && st;
            string sleutel = $"{a}:{!nu}:{string.Join(",", bezet.OrderBy(x => x))}";
            if (!geprobeerd.Add(sleutel)) continue;
            _log.Info(bijLoc.Contains(a)
                ? $"Zelf oplossen: wissel {a} bij de bezette melder ({Lijst(bezet)}) gaat naar {(!nu ? "afbuigend" : "rechtdoor")}; komt de loc van een zijtak naar de samenvoeging, dan kan dat de kortsluiting opheffen (BUG #169/#305)."
                : $"Zelf oplossen: wissel {a} gaat naar {(!nu ? "afbuigend" : "rechtdoor")} (de kaart kent bij melder {Lijst(bezet)} nog geen wissel; {(nu ? "hij stond afbuigend, dus verdacht" : "volgende kandidaat")}, BUG #315).");
            await ZetWissel(a, !nu);
            // BUG #315: tijdens een blijvende kortsluiting voert Dinamo een wisselcommando vaak niet uit (#301; 25-10 09:06, 10:26, 18:05). Dan het commando een halve minuut lang
            // elke 3 s opnieuw sturen: Dinamo zet de stroom zelf af en toe weer aan en voert het dan wel uit.
            for (int herhaal = 1; herhaal <= 10 && LaatsteCommandoTeruggemeld(a) == false && HuidigeAlarmBlokken().Count > 0; herhaal++)
            {
                if (herhaal == 1) _log.Info($"Zelf oplossen: Dinamo meldt wissel {a} niet terug (tijdens de kortsluiting voert Dinamo het commando vaak niet uit); het commando gaat elke 3 s opnieuw (BUG #315).");
                await Wacht(TimeSpan.FromSeconds(3));
                await ZetWissel(a, !nu, forceer: true);
            }
            if (LaatsteCommandoTeruggemeld(a) == false && HuidigeAlarmBlokken().Count > 0)
                _log.Waarschuwing($"Zelf oplossen: Dinamo voerde wissel {a} na 10 pogingen nog steeds niet uit (BUG #315).");
            if (_alarmWegVoorEigenTerugmelding && HuidigeAlarmBlokken().Count == 0)
            {
                _log.Info($"Zelf oplossen: de kortsluiting ging weg voordat Dinamo wissel {a} terugmeldde; dat was dus niet deze wissel (BUG #308). Wissel {a} gaat terug.");
                await ZetWissel(a, nu);
                return true;
            }
            // Dinamo meldt het opheffen soms pas na enkele seconden (autolog 25-10 10:31:37-10:31:38 en 10:24:46-10:24:47): 4 s wachten
            await Wacht(TimeSpan.FromSeconds(4));
            if (HuidigeAlarmBlokken().Count == 0)
            {
                _log.Vondst($"De kortsluiting ging weg nadat wissel {a} (bij de loc) naar {(!nu ? "afbuigend" : "rechtdoor")} ging (zelf oplossen, BUG #305). Nog geen bewijs: een rit zonder kortsluiting bevestigt het.");
                return true;
            }
            await ZetWissel(a, nu);
            await Wacht(TimeSpan.FromSeconds(1));
            return false;   // per ronde één wissel: tussen de rondes kan het alarm vanzelf weggaan
        }
        return false;
    }

    /// <summary>BUG #305: kruipt (naar alle blokken, het blok is dan onbekend) in richting <paramref name="r"/> tot er een melder bezet raakt of de tijd om is.</summary>
    private async Task KruipTotMelder(Richting r, TimeSpan max)
    {
        if (BlokVereist)
        {
            var blokken = _ins.DinamoBlokLijst();
            foreach (var b in blokken) _hw.ZetLocSnelheid(_ins.LocAdres, _ins.Kruipsnelheid, r == Richting.Vooruit, b, _ins.LocStappen);
            await Wacht(DinamoCyclus * blokken.Count);
        }
        else await StuurSnelheid(r, _ins.Kruipsnelheid);
        var eind = _klok.Nu + max;
        while (_klok.Nu < eind && HuidigeAlarmBlokken().Count == 0)
        {
            await Wacht(Poll);
            _monitor.Bijwerken();
            if (_monitor.Bezet.Count > 0) break;
        }
        await VolledigeStop();
    }
}
