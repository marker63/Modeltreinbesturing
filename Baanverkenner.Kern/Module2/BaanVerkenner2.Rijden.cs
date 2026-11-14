using Modeltreinbesturing.Hardware;

namespace Baanverkenner.Kern;

// =====================================================================================================================
//  MODULE 2 - rijden, stoppen, parkeren, wissels zetten, kortsluiting oplossen
//
//  Vaste regels (uit 357 bugs van module 1 en de afspraken met Marco):
//   - Een wissel gaat alleen om als de loc stilstaat, NIET op een melder van het wisselgebied van die wissel (Zone), en een nog niet
//     gevonden adres alleen als de loc netjes op één melder geparkeerd staat.
//   - Elk wisselcommando gaat één keer naar Dinamo; daarna wachten op de terugmelding (= de puls is gegeven, BUG #352). Geen
//     terugmelding = niet uitgevoerd: later opnieuw sturen.
//   - Rijcommando's gaan naar het Dinamo-blok van elke melder waar de loc op komt (of naar alle blokken zolang dat blok onbekend is),
//     anders blijft de loc op de blokgrens staan (BUG #34/#57/#140).
//   - Parkeren = doorrijden tot de loc alleen nog op de nieuwe melder staat en dan nog een stukje (het midden als de lengte bekend is).
//   - Kortsluiting: eerst de wissels terugzetten die voor deze proef zijn omgezet (Marco BUG #169: van de gesplitste kant bereden lost
//     omzetten de kortsluiting op), anders langzaam terugrijden; pas na 10 minuten iets aan Marco vragen (BUG #305).
// =====================================================================================================================

public partial class BaanVerkenner2
{
    internal enum RitEinde2 { Geparkeerd, Afwijking, Kortsluiting, Doodlopend, Maximum }

    /// <summary>Wat een rit moet doen.</summary>
    internal sealed class RitOpdracht2
    {
        public Richting Richting;
        /// <summary>De melders die na elkaar verwacht worden (zonder de melder waar de loc nu staat).</summary>
        public List<int> Verwacht = new();
        /// <summary>Bij een andere melder dan verwacht: daar parkeren en stoppen (anders doorrijden).</summary>
        public bool StopBijAfwijking = true;
        /// <summary>Na de verwachte melders doorrijden zolang wat volgt nog onbekend is (verkennen).</summary>
        public bool Verkennen;
        /// <summary>Na de verwachte melders nog zoveel (onbekende) stappen rijden en dan parkeren (een proef).</summary>
        public int ExtraStappen;
        /// <summary>Meteen parkeren op de melder waar de loc nu staat (alleen netjes op één melder gaan staan).</summary>
        public bool ParkeerMeteen;
        public bool AlleBlokken;
        /// <summary>Niets vastleggen (bijv. alleen netjes gaan staan na een kortsluiting).</summary>
        public bool NietVastleggen;
        public int Max = 60;
        public string Reden = "";
        /// <summary>Bij kortsluiting eerst deze wissels zo zetten (de stand die bewezen goed ging), zie <see cref="HerstelKortsluiting"/>.</summary>
        public Dictionary<int, bool>? Herstel;
    }

    internal sealed class RitUitslag2
    {
        public List<int> Reeks = new();
        public RitEinde2 Einde;
        public int? AfwijkingIndex;
        public List<double> Seconden = new();
        /// <summary>BUG #386: de eerste stap paste niet bij de kaart en de plek van de loc aan het begin was niet zeker: die stap niet vastleggen.</summary>
        public bool EersteStapOnzeker;
    }

    private bool _kortStatus;
    private DateTime _negeerKortTot = DateTime.MinValue;
    private readonly HashSet<int> _gestuurd = new();
    private bool _decoderVooruit = true;
    private int _huidigeStap;

    private void KortsluitStatus(bool actief) => _kortStatus = actief;

    /// <summary>WAT: is er nu kortsluiting? Bij Dinamo het blokalarm (de eigen noodstop geeft geen blokalarm); anders de algemene foutmelding,
    /// behalve vlak na een eigen noodstop (Dinamo kaatst die terug).</summary>
    private bool Kortsluiting()
    {
        if (_hw is IBlokAlarmBron b) return b.ActieveAlarmBlokken.Count > 0;
        return _kortStatus && _klok.Nu > _negeerKortTot;
    }

    private List<int> AlleBlokken() => _hw.LocCommandoVereistBlok ? _ins.DinamoBlokLijst() : new List<int> { 0 };

    /// <summary>De Dinamo-blokken voor de melders <paramref name="melders"/>: bekende blokken eerst; is er één onbekend, dan daarna alle andere blokken.</summary>
    private List<int> BlokkenVoor(IEnumerable<int> melders, bool alles = false)
    {
        if (!_hw.LocCommandoVereistBlok) return new List<int> { 0 };
        var res = new List<int>();
        bool onbekend = alles;
        foreach (var m in melders)
        {
            if (_k.Blokken.TryGetValue(m, out var b)) { if (!res.Contains(b)) res.Add(b); }
            else onbekend = true;
        }
        if (onbekend || res.Count == 0) foreach (var b in AlleBlokken()) if (!res.Contains(b)) res.Add(b);
        return res;
    }

    private string _laatsteVoeding385 = "";

    private bool DecoderVooruit(Richting r) => (r == Richting.Vooruit) ^ _k.Omgedraaid;

    /// <summary>WAT: stuurt het rijcommando naar de blokken (zonder te wachten; Dinamo verwerkt ze één per 200 ms).</summary>
    private void StuurSnelheid(int stap, Richting r, IEnumerable<int> blokken)
    {
        _decoderVooruit = DecoderVooruit(r);
        _huidigeStap = stap;
        var lijst385 = blokken.ToList();
        // BUG #385 (Marco 08-11 09:31: "ik zie niet in de logfile welk blok van spanning voorzien wordt"): zoals module 1 in het logboek zetten naar welke blokken het rijcommando
        // gaat, alleen als dat verandert (anders bij elke herhaling dezelfde regel).
        string tekst385 = $"snelheid {stap} {r.Tekst()} naar blok {(lijst385.Count == AlleBlokken().Count() && lijst385.Count > 1 ? "alle (" + string.Join(", ", lijst385.OrderBy(x => x)) + ")" : string.Join(", ", lijst385.OrderBy(x => x)))}";
        if (tekst385 != _laatsteVoeding385) { _laatsteVoeding385 = tekst385; _log.Rijden($"  Rijstroom: {tekst385}."); }
        foreach (var b in lijst385)
        {
            _hw.ZetLocSnelheid(_ins.LocAdres, stap, _decoderVooruit, b, _ins.LocStappen);
            if (stap > 0) _gestuurd.Add(b);
        }
    }

    /// <summary>WAT: de loc stoppen. Snelheid 0 met voorrang naar de blokken waar de loc staat; zijn dat er veel of onbekend, dan houdt de Dinamo-noodstop
    /// (F-bit) de loc meteen vast tot alle stopcommando's verstuurd zijn. Daarna controleren dat er niets meer beweegt.</summary>
    private async Task Stop()
    {
        _hw.VerwijderWachtendeSnelheidscommandosVoor(_ins.LocAdres);
        var bezet = _monitor.Bezet.ToList();
        var blokken = BlokkenVoor(bezet);
        foreach (var b in _gestuurd) if (!blokken.Contains(b)) blokken.Add(b);
        bool hard = blokken.Count > 2;
        for (int pas = 0; pas < 3; pas++)
        {
            if (hard) { _hw.Noodstop(); _negeerKortTot = _klok.Nu + TimeSpan.FromSeconds(4); }
            foreach (var b in blokken) _hw.ZetLocSnelheidUrgent(_ins.LocAdres, 0, _decoderVooruit, b, _ins.LocStappen);
            var eind = _klok.Nu + DinamoCyclus * blokken.Count + TimeSpan.FromMilliseconds(300);
            var volgendeF = _klok.Nu + TimeSpan.FromMilliseconds(400);
            while (_klok.Nu < eind)
            {
                await _klok.Wacht(TimeSpan.FromMilliseconds(100));
                if (hard && _klok.Nu >= volgendeF) { _hw.Noodstop(); volgendeF = _klok.Nu + TimeSpan.FromMilliseconds(400); _negeerKortTot = _klok.Nu + TimeSpan.FromSeconds(4); }
            }
            _gestuurd.Clear();
            _huidigeStap = 0;
            await _klok.Wacht(TimeSpan.FromMilliseconds(hard ? 1200 : 600));
            _monitor.Bijwerken();
            await _klok.Wacht(TimeSpan.FromMilliseconds(800));
            if (_monitor.Bijwerken().Count == 0 || Kortsluiting()) return;
            _log.Waarschuwing("Na het stoppen veranderden er nog melders: stop opnieuw, nu naar alle blokken.");
            blokken = AlleBlokken();
            hard = true;
        }
    }

    /// <summary>Stop zonder wachten (bij afbreken).</summary>
    private void NoodstopZonderWachten()
    {
        _hw.Noodstop();
        foreach (var b in BlokkenVoor(_monitor.Bezet, alles: true)) _hw.ZetLocSnelheidUrgent(_ins.LocAdres, 0, _decoderVooruit, b, _ins.LocStappen);
    }

    /// <summary>WAT: hoe lang de loc na het vrijkomen van de vorige melder nog doorrijdt (op kruipsnelheid) om te parkeren. Is bekend hoe lang hij alleen op
    /// deze melder staat, dan de helft daarvan (het midden); anders de instelling 'doorrijden na een melder'.</summary>
    private double Doorrij(int m)
    {
        if (_k.SoloTijd.TryGetValue(m, out var t) && t > 0) return Math.Clamp(t / 2, 0.4, 8);
        return Math.Max(0.5, _ins.StapDoorrijSeconden);
    }

    /// <summary>De stand die nu op de baan staat (Dinamo-commando's), als lijst van afbuigende adressen (met volgers).</summary>
    private List<int> HuidigeStand() => _k.Standen.Where(kv => kv.Value).Select(kv => kv.Key).OrderBy(a => a).ToList();
    private bool Fysiek(int a) => _k.Standen.TryGetValue(a, out var s) && s;

    // ================================================================ de rit zelf

    /// <summary>
    /// WAT: één rit: optrekken, melders volgen, op tijd naar kruipsnelheid, parkeren of stoppen. Legt alle gereden stappen vast. Bij kortsluiting wordt die
    /// meteen zelf opgelost (zie <see cref="HerstelKortsluiting"/>, met de wissels uit <see cref="RitOpdracht2.Herstel"/>) en gaat de loc netjes terug op de
    /// laatste melder vóór de kortsluiting staan.
    /// WAAROM: alle soorten ritten (navigeren, verkennen, proef, terug) gebruiken dezelfde regels; dat voorkomt dat het ene soort rit iets anders doet dan
    /// het andere (bij module 1 een bron van veel fouten).
    /// </summary>
    /// <summary>WAT: de werkwijze van Marco (09-11 17:23): bij verkennen na elke nieuwe melder meteen stoppen en daar de blokproef doen, dan pas verder.
    /// Met MARCO2_OUD=1 (alleen in de simulatie) de oude werkwijze, om te vergelijken. (Ook geprobeerd: de wisseltest over 1-2 secties en eerst alles bij de nieuwe sectie
    /// uitzoeken; dat maakte de simulatie veel slechter (11/23 melders of 113 min) en is daarom niet overgenomen.)</summary>
    internal static bool W3a => Environment.GetEnvironmentVariable("MARCO2_OUD") != "1";

    internal async Task<RitUitslag2> Rijd(RitOpdracht2 op)
    {
        var bekendVoor394 = _k.Melders.ToHashSet();
        var h = op.Richting;
        bool omgedraaidInRit = false;
        int draaiIndex = -1;
        var res = new RitUitslag2();
        _monitor.Bijwerken();
        var bezet = new HashSet<int>(_monitor.Bezet);
        int start = _k.LocMelder;
        if (!bezet.Contains(start) && bezet.Count > 0) start = bezet.First();
        res.Reeks.Add(start);
        int startVia = _k.LocVia;
        bool terug = h != _k.LocRichting && !op.ParkeerMeteen && startVia > 0;
        var stand = HuidigeStand();
        var verwacht = op.Verwacht.ToList();
        while (verwacht.Count > 0 && verwacht[0] == start) verwacht.RemoveAt(0);   // daar staat de loc al
        bool parkeren = op.ParkeerMeteen || (op.Verwacht.Count > 0 && verwacht.Count == 0 && !op.Verkennen && op.ExtraStappen == 0);
        DateTime? soloSinds = parkeren && bezet.Count == 1 ? _klok.Nu : null;
        int verken = Math.Max(1, _ins.Verkensnelheid), kruip = Math.Max(1, Math.Min(_ins.Kruipsnelheid, _ins.Verkensnelheid));
        int snelheid = parkeren ? kruip : verken;
        var laatsteWijziging = _klok.Nu;
        var binnenTijd = _klok.Nu;
        DateTime? soloStart = null; int soloSnelheid = 0;
        int stilPogingen = 0;

        // Een nieuwe melder is bezet geworden: vastleggen, vergelijken met wat verwacht werd, en beslissen of de loc hier gaat parkeren.
        void Binnen(int n, DateTime tijd)
        {
            if (soloStart is DateTime ss && soloSnelheid > 0 && res.Reeks.Count >= 2)
            {
                // hoe lang stond de loc alleen op de vorige melder? (om later netjes in het midden te parkeren)
                double t = (tijd - ss).TotalSeconds * soloSnelheid / kruip;
                int vorige = res.Reeks[^1];
                _k.SoloTijd[vorige] = _k.SoloTijd.TryGetValue(vorige, out var oud) && oud > 0 ? (oud + t) / 2 : t;
            }
            soloStart = null;
            soloSinds = null;   // het doorrijstuk telt pas vanaf het moment dat de loc alleen op de NIEUWE melder staat
            // Rijdt de loc andersom dan gedacht? Komt hij melder n binnen vanaf de kant waar volgens de kaart juist de andere richting heen gaat, dan is de
            // loc omgedraaid (keerlus, of met de hand andersom neergezet): vanaf nu geldt decoder 'vooruit' als de andere kaartrichting.
            int vorigeM = res.Reeks[^1];
            bool tegen = _k.Stappen.Any(x => !x.Terug && x.Melder == n && x.Richting == h && x.Uitkomst == vorigeM)
                         || _k.Stappen.Any(x => !x.Terug && x.Melder == vorigeM && x.Richting == h.Om() && x.Uitkomst == n);
            // BUG #379 (Marco 04-11 10:17-10:20): de route 4 -> 11 -> 12 werd achteruit gereden; daarna reed de proef vooruit van 12 naar 11, precies zoals de planning dacht
            // (die stap was bekend uit een terugrit). Omdat terugritten hier niet meetelden, zag de verkenner een "omgedraaide loc" en draaide alle richtingen om; drie keer heen en
            // weer, tot de taak werd overgeslagen. Een stap die al eens zo gereden is (ook als terugrit), bevestigt de richting.
            bool mee = _k.Stappen.Any(x => x.Melder == vorigeM && x.Richting == h && x.Uitkomst == n);
            // BUG #386 (Marco 08-11 10:01: "je blijft maar problemen rapporteren met de rijrichting"; log 09:49-09:56): elke keer was het de EERSTE melder van een rit, direct na
            // een kortsluitherstel, "netjes op één melder"-kruipen of 30 s stilstaan. Dan staat de loc op een grens en is niet zeker op welke melder hij echt begon; de afwijkende
            // eerste melder zegt dan niets over de rijrichting. Alleen een stap tussen twee melders die de loc in DEZE rit zelf bereikte, telt nog als bewijs van een omgedraaide loc.
            if (tegen && !mee && res.Reeks.Count == 1)
            {
                res.EersteStapOnzeker = true;
                _log.Info($"Melder {n} paste niet bij de rijrichting vanaf melder {vorigeM}; de loc stond waarschijnlijk niet (alleen) op melder {vorigeM}. Rijrichting blijft zoals hij was; deze stap wordt niet vastgelegd (BUG #386, nog niet getest op de baan).");
                tegen = false;
            }
            if (tegen && !mee)
            {
                _k.Omgedraaid = !_k.Omgedraaid;
                h = h.Om();
                // BUG #390 (Marco 09-11 14:03): de loc zelf draait nooit om. Wat hier gebeurt: de loc heeft de keerlus (129 - 4 - 11 - 12 - 1 - 9 - 10 - 129) gereden en rijdt
                // daarna met dezelfde decoderrichting de andere kant op over de baan. De kaartrichting (vooruit/achteruit) wordt daarom vanaf hier gespiegeld.
                _log.Vondst($"Keerlus gereden: van melder {vorigeM} naar {n} is volgens de kaart {h.Tekst()}. De loc rijdt gewoon door in dezelfde decoderrichting; alleen de kaartrichting wordt vanaf nu gespiegeld.");
                omgedraaidInRit = true;
                draaiIndex = res.Reeks.Count;   // index die n zo krijgt
            }
            double naSec = (tijd - binnenTijd).TotalSeconds;
            res.Seconden.Add(naSec * snelheid / verken);
            binnenTijd = tijd;
            res.Reeks.Add(n);
            // BUG #384 (Marco 08-11 09:29): net als in module 1 elke nieuwe melder in het logboek, met blok en tijd sinds de vorige melder.
            int? blok384 = _status.Kaart.Melders.FirstOrDefault(x => x.Nummer == n)?.DinamoBlok;
            _log.Rijden($"  melder {n}{(blok384 is int b384 ? $" (blok {b384})" : "")} bezet (na {naSec:0.0} s)");
            // BUG #384: ook het melderschema meteen laten zien wat er gereden wordt (de kaart wordt na de rit weer netjes uit de kennis opgebouwd).
            try
            {
                var kaart384 = _status.Kaart;
                kaart384.Melder(n);
                if (res.Reeks.Count >= 2 && res.Reeks[^2] > 0)
                    kaart384.RegistreerOvergang(res.Reeks[^2], n, h, new Configuratie(_k.Standen.Where(kv => kv.Value).Select(kv => kv.Key).ToArray()), null);
                if (_klok.Nu - _laatsteLiveBewaard > TimeSpan.FromSeconds(5))
                {
                    _laatsteLiveBewaard = _klok.Nu;
                    kaart384.Bijgewerkt = _klok.Nu;
                    Bewaren?.Invoke(_status);
                }
            }
            catch (Exception ex) { _log.Waarschuwing($"Kaart tijdens de rit bijwerken mislukte: {ex.Message}"); }
            if (omgedraaidInRit) { res.AfwijkingIndex ??= res.Reeks.Count - 1; parkeren = true; }
            int idx = res.Reeks.Count - 2;   // index in 'verwacht'
            if (idx < verwacht.Count)
            {
                if (n != verwacht[idx])
                {
                    res.AfwijkingIndex ??= res.Reeks.Count - 1;
                    _log.Rijden($"Melder {n} bezet, verwacht was melder {verwacht[idx]}.");
                    if (op.StopBijAfwijking) parkeren = true;
                }
                else if (idx == verwacht.Count - 1 && !op.Verkennen && op.ExtraStappen == 0) parkeren = true;
            }
            else
            {
                int extra = idx - verwacht.Count + 1;
                if (op.ExtraStappen > 0 && extra >= op.ExtraStappen) parkeren = true;
                if (op.Verkennen && (_k.Voorspel(res.Reeks[^2], n, h, Fysiek) is not null || LusGesloten(res.Reeks))) parkeren = true;
                // BUG #394 (werkwijze van Marco, 09-11 17:23): bij verkennen stoppen op de eerste nieuwe melder (of een melder waarvan het blok nog onbekend is). Daar volgt
                // meteen de blokproef (begin van de takenlus); daarna rijdt de verkenning verder.
                if (op.Verkennen && W3a && (!bekendVoor394.Contains(n) || (_hw.LocCommandoVereistBlok && !_k.Blokken.ContainsKey(n)))) parkeren = true;
                if (!op.Verkennen && op.ExtraStappen == 0) parkeren = true;
            }
            if (res.Reeks.Count > op.Max) parkeren = true;
            if (parkeren) snelheid = kruip;
            // rijstroom ook naar het blok van de nieuwe melder (anders blijft de loc op de blokgrens staan)
            StuurSnelheid(snelheid, h, BlokkenVoor(new[] { n }.Concat(bezet)));
        }

        _log.Rijden($"Rit {(op.Reden.Length > 0 ? "(" + op.Reden + ") " : "")}{h.Tekst()} vanaf melder {start}" + (verwacht.Count > 0 ? $", verwacht: {string.Join(", ", verwacht)}" : "") +
                    (op.Verkennen ? ", daarna verkennen" : "") + (op.ExtraStappen > 0 ? $", daarna {op.ExtraStappen} onbekende stap(pen)" : "") + $" (wissels afbuigend: {(stand.Count == 0 ? "geen" : string.Join(",", stand))}).");
        if (!op.NietVastleggen) _k.Ritten++;
        StuurSnelheid(snelheid, h, BlokkenVoor(bezet, op.AlleBlokken));
        // staat de loc al op de grens met de eerste verwachte melder, dan telt die als binnengekomen
        if (verwacht.Count > 0 && bezet.Contains(verwacht[0])) Binnen(verwacht[0], _klok.Nu);

        RitEinde2? einde = null;
        while (einde is null)
        {
            await _klok.Wacht(Poll);
            if (_token.IsCancellationRequested) { NoodstopZonderWachten(); _token.ThrowIfCancellationRequested(); }
            if (_gepauzeerd)
            {
                await Stop();
                while (_gepauzeerd) { _token.ThrowIfCancellationRequested(); await _klok.Wacht(TimeSpan.FromMilliseconds(200)); }
                StuurSnelheid(snelheid, h, BlokkenVoor(_monitor.Bezet, op.AlleBlokken));
                laatsteWijziging = _klok.Nu;
            }
            if (Kortsluiting()) { einde = RitEinde2.Kortsluiting; break; }
            foreach (var w in _monitor.Bijwerken().OrderBy(x => x.Tijd))
            {
                laatsteWijziging = _klok.Nu;
                if (w.Bezet)
                {
                    if (!bezet.Add(w.Melder)) continue;
                    if (res.Reeks.Count >= 2 && w.Melder == res.Reeks[^2]) continue;   // even terug op de melder waar de loc net vandaan kwam (flikkering of uitrollen)
                    Binnen(w.Melder, w.Tijd);
                }
                else
                {
                    bezet.Remove(w.Melder);
                    if (bezet.Count == 1 && bezet.Contains(res.Reeks[^1]))
                    {
                        if (parkeren) soloSinds ??= _klok.Nu;
                        soloStart = _klok.Nu; soloSnelheid = snelheid;
                    }
                }
            }
            if (parkeren && soloSinds is null && bezet.Count == 1 && bezet.Contains(res.Reeks[^1])) soloSinds = _klok.Nu;
            if (parkeren && soloSinds is DateTime s0 && bezet.Count == 1 && (_klok.Nu - s0).TotalSeconds >= Doorrij(res.Reeks[^1]))
            {
                einde = res.AfwijkingIndex is null ? RitEinde2.Geparkeerd : RitEinde2.Afwijking;
                break;
            }
            // ---- staat de loc stil?
            double grens = parkeren ? 12 : Math.Max(10, _ins.MaxSecondenTussenMelders);
            if ((_klok.Nu - laatsteWijziging).TotalSeconds > grens)
            {
                if (stilPogingen++ == 0)
                {
                    _log.Rijden("De loc lijkt stil te staan: rijcommando naar alle blokken.");
                    StuurSnelheid(snelheid, h, AlleBlokken());
                    laatsteWijziging = _klok.Nu - TimeSpan.FromSeconds(grens / 2);
                    continue;
                }
                einde = parkeren ? (res.AfwijkingIndex is null ? RitEinde2.Geparkeerd : RitEinde2.Afwijking) : RitEinde2.Doodlopend;
                break;
            }
        }
        await Stop();
        res.Einde = einde.Value;
        if (res.Einde == RitEinde2.Kortsluiting && res.Reeks.Count >= 2 && bezet.Contains(res.Reeks[^1]) && bezet.Contains(res.Reeks[^2]))
        {
            // De loc stond nog op beide melders: de kortsluiting zit in de knoop tussen die twee (de wisseltong ligt al in het gebied van de nieuwe melder,
            // zoals 16 -> 5 bij wissel 2). De nieuwe melder is dus niet echt bereikt.
            _log.Rijden($"Kortsluiting op de grens van melder {res.Reeks[^2]} en {res.Reeks[^1]}: telt als kortsluiting na melder {res.Reeks[^2]}.");
            res.Reeks.RemoveAt(res.Reeks.Count - 1);
            if (res.Seconden.Count > res.Reeks.Count - 1) res.Seconden.RemoveAt(res.Seconden.Count - 1);
            if (res.AfwijkingIndex >= res.Reeks.Count) res.AfwijkingIndex = null;
        }
        if (res.Einde == RitEinde2.Geparkeerd && res.Reeks.Count > op.Max) res.Einde = RitEinde2.Maximum;
        _log.Rijden($"Rit klaar ({res.Einde}): {string.Join(", ", res.Reeks)}.");
        // BUG #384 (Marco 08-11 09:29: "het melderschema wordt niet gevuld in de eerste rit"): de kaart (waar het melderschema en het blokkenschema uit tekenen) werd alleen bij
        // het bewaren na een hele taak opgebouwd, en de eerste ronde is één lange taak. Nu na elke rit bewaren (zoals module 1 na elke opdracht).
        BewaarNu();
        if (!op.NietVastleggen) LegRitVast(op, res, startVia, terug, stand, h, draaiIndex);
        // waar staat de loc nu?
        bool gewisseld = h != _k.LocRichting;
        _k.LocRichting = h;
        if (res.Reeks.Count >= 2) { _k.LocMelder = res.Reeks[^1]; _k.LocVia = res.Reeks[^2]; }
        else { _k.LocMelder = res.Reeks[0]; _k.LocVia = gewisseld ? 0 : startVia; }
        if (res.Einde == RitEinde2.Kortsluiting)
        {
            await HerstelKortsluiting(op.Herstel ?? new Dictionary<int, bool>(), h);
            // Netjes terug op de laatste melder vóór de kortsluiting. Voor de planner blijft de toestand 'op melder m, gekomen van Via, rijrichting h':
            // de volgende rit de andere kant op is dan een gewone omkeer naar Via (zeker, ook in een Engelse wissel: de loc rijdt zijn eigen spoor terug).
            int m = _k.LocMelder, via = _k.LocVia;
            await Herparkeer(h.Om());
            // WAT: waar de parkeerrit de loc het laatst zag (de rit zelf werkt de plaats bij). WAAROM (BUG #406, Marco 12-11 13:00): de parkeerrit na de
            // kortsluiting bij melder 24 eindigde op melder 10 ("24, 10"), daarna was even geen melder bezet. Dan werd de plaats teruggezet op 24 en plande
            // de verkenner vanaf 24 terwijl de loc bij 10 stond: de volgende rit liep vast en de loc raakte kwijt.
            int naPark = _k.LocMelder, naParkVia = _k.LocVia;
            _monitor.Bijwerken();
            var na = _monitor.Bezet.ToList();
            if (na.Count == 1 && na[0] == via && via > 0) { _k.LocMelder = via; _k.LocVia = m; _k.LocRichting = h.Om(); }
            else if (na.Count == 0 && naPark != m && naPark > 0)
            {
                _k.LocMelder = naPark; _k.LocVia = naParkVia == naPark ? m : naParkVia; _k.LocRichting = h.Om();
                _log.Info($"Na de kortsluiting reed de loc bij het netjes parkeren door naar melder {naPark}; daar staat hij nu (BUG #406, nog niet getest op de baan).");
            }
            else { _k.LocMelder = m; _k.LocVia = via; _k.LocRichting = h; }
            return res;
        }
        _monitor.Bijwerken();
        if (res.Einde is RitEinde2.Geparkeerd or RitEinde2.Afwijking or RitEinde2.Maximum && _monitor.Bezet.Count == 1) await BlokProefAlsNodig();
        return res;
    }

    /// <summary>Is de lus rond (de laatste overgang is in deze rit al eens gereden)?</summary>
    private static bool LusGesloten(List<int> reeks)
    {
        if (reeks.Count < 3) return false;
        int a = reeks[^2], b = reeks[^1];
        for (int i = 1; i < reeks.Count - 1; i++) if (reeks[i - 1] == a && reeks[i] == b) return true;
        return false;
    }

    /// <summary>WAT: legt alle stappen van een rit vast als <see cref="StapWaarneming"/>. WAAROM: elke rit is bewijs; ook een navigatierit.</summary>
    private void LegRitVast(RitOpdracht2 op, RitUitslag2 res, int startVia, bool terug, List<int> stand, Richting h, int draaiIndex)
    {
        var r = res.Reeks;
        // BUG #391: bij een onzekere eerste stap is ook de tweede melder twijfelachtig (log 14:45:55: rit 129, 10, 24 gaf de stap "10 -> 24", terwijl de loc van 129
        // naar 24 reed en 10 alleen even aanraakte). Daarom pas vastleggen vanaf de derde melder, zonder herkomst.
        for (int i = res.EersteStapOnzeker ? 3 : 1; i < r.Count; i++)
        {
            // bleek de loc halverwege andersom te rijden (keerlus), dan hebben de stappen daarvóór nog de oude kaartrichting; bij de eerste stap is alles de nieuwe
            var hi = draaiIndex > 1 && i < draaiIndex ? h.Om() : h;
            bool eersteTerug = i == 1 && terug && draaiIndex != 1;
            int via = res.EersteStapOnzeker && i == 3 ? 0 : i >= 2 ? r[i - 2] : (eersteTerug || draaiIndex == 1 ? 0 : startVia);
            _k.Leg(via, r[i - 1], hi, stand, r[i], _klok.Nu, terug: eersteTerug, seconden: i - 1 < res.Seconden.Count ? res.Seconden[i - 1] : 0);
            _gereden412?.Add((r[i - 1], r[i], hi));   // BUG #412: voor de controle (welke overgangen zijn gereden)
        }
        // BUG #402: "doodlopend" alleen vastleggen als de loc nog op de laatste melder staat; is geen enkele melder meer bezet, dan is de loc kwijt (ontspoord,
        // stuk zonder stroom, of een stuk zonder melder) en zegt dat niets over het spoor.
        _monitor.Bijwerken();
        bool locKwijt = res.Einde == RitEinde2.Doodlopend && _monitor.Bezet.Count == 0;
        if (locKwijt)
            _log.Waarschuwing($"Na melder {r[^1]} ({h.Tekst()}) is geen enkele melder meer bezet: de loc is kwijt, dit wordt NIET als doodlopend vastgelegd (BUG #402).");
        // WAT: is de loc in deze rit geen melder verder gekomen, terwijl al bekend is waar hij met deze wisselstand uitkomt, dan is het geen doodlopend spoor
        // maar kon de loc niet weg. Niet vastleggen; daarna probeert de verkenner de rijstroom per blok (#408).
        // WAAROM (BUG #410, Marco 12-11 16:24): "na melder 129 (vooruit) komt geen nieuwe melder: doodlopend", terwijl 129 vooruit met wissel 9 rechtdoor al vaak
        // naar 24 was gereden. De loc stond op de sectiescheiding en kreeg alleen rijstroom van blok 9.
        if (!locKwijt && res.Einde == RitEinde2.Doodlopend && r.Count == 1)
        {
            int viaD = terug ? 0 : startVia;
            var bekend = _k.Voorspel(viaD, r[0], h, a => stand.Contains(a));
            if (bekend is { Uitkomst: > 0 } vb)
            {
                locKwijt = true;
                _nietWeg410 = r[0];
                _log.Waarschuwing($"De loc kwam niet weg van melder {r[0]} ({h.Tekst()}), terwijl hij met deze wisselstand al eens naar melder {vb.Uitkomst} reed: niet als doodlopend vastgelegd, eerst rijstroom per blok proberen (BUG #410, nog niet getest op de baan).");
            }
        }
        if (!locKwijt && res.Einde is RitEinde2.Kortsluiting or RitEinde2.Doodlopend)
        {
            int via = r.Count >= 2 ? r[^2] : (terug ? 0 : startVia);
            _k.Leg(via, r[^1], h, stand, res.Einde == RitEinde2.Kortsluiting ? StapUitkomst.Kortsluiting : StapUitkomst.Doodlopend, _klok.Nu, terug: r.Count == 1 && terug);
            if (res.Einde == RitEinde2.Doodlopend) _log.Vondst($"Na melder {r[^1]} ({h.Tekst()}) komt geen nieuwe melder: doodlopend (of de loc kon niet verder).");
        }
        foreach (var m in r) _status.Kaart.Melder(m);
        _status.Kaart.Ritten.Add(new GeredenRit
        {
            Nr = _status.Kaart.Ritten.Count + 1, Tijd = _klok.Nu, Navigatie = op.Reden.StartsWith("naar"), Start = r[0], Richting = h, Afbuigend = stand.ToList(), Reeks = r.ToList(),
            Einde = res.Einde switch { RitEinde2.Kortsluiting => RitEinde.Kortsluiting, RitEinde2.Doodlopend => RitEinde.Doodlopend, RitEinde2.Maximum => RitEinde.Maximum, RitEinde2.Afwijking => RitEinde.AfwijkingGevonden, _ => RitEinde.DoelBereikt }
        });
    }

    /// <summary>WAT: alleen netjes op één melder gaan staan (kruipen in richting <paramref name="r"/> tot de loc alleen op één melder staat, plus het doorrijstuk).</summary>
    private async Task Herparkeer(Richting r, bool alleBlokken = false)
    {
        _monitor.Bijwerken();
        var bezet = _monitor.Bezet.ToList();
        if (bezet.Count > 0 && !bezet.Contains(_k.LocMelder)) _k.LocMelder = bezet[0];
        await Rijd(new RitOpdracht2 { Richting = r, ParkeerMeteen = true, AlleBlokken = alleBlokken, NietVastleggen = true, Reden = "netjes op één melder" });
    }

    // ================================================================ Dinamo-blok per melder (fase 6)

    /// <summary>WAT: staat de loc geparkeerd op een melder waarvan het blok onbekend is, dan krijgt elk blok om beurten een kort kruipcommando terug naar de
    /// vorige melder (buurblokken eerst). Het blok waarbij de loc beweegt, is het blok van deze melder. WAAROM: zonder blok kan de loc later niet met één
    /// commando gestuurd worden, en het rapport en de blokvoorstellen hebben het nodig. Een stukje terug kost per blok maar een paar seconden.</summary>
    private async Task BlokProefAlsNodig()
    {
        if (!_hw.LocCommandoVereistBlok) return;
        int m = _k.LocMelder;
        if (_k.Blokken.ContainsKey(m) || _k.LocVia <= 0) return;
        _monitor.Bijwerken();
        if (_monitor.Bezet.Count != 1 || !_monitor.Bezet.Contains(m)) return;
        var terug = _k.LocRichting.Om();
        var kandidaten = new List<int>();
        void Voeg(int? b) { if (b is int x && AlleBlokken().Contains(x) && !kandidaten.Contains(x)) kandidaten.Add(x); }
        Voeg(_k.Blokken.TryGetValue(_k.LocVia, out var bv) ? bv : null);
        Voeg(Referentie?.BlokVan(m));
        foreach (var s in _k.Stappen.Where(s => s.Melder == m && s.Uitkomst > 0)) Voeg(_k.Blokken.TryGetValue(s.Uitkomst, out var b2) ? b2 : null);
        foreach (var s in _k.Stappen.Where(s => s.Uitkomst == m)) Voeg(_k.Blokken.TryGetValue(s.Melder, out var b3) ? b3 : null);
        foreach (var b in AlleBlokken().Where(b => !_k.Blokken.ContainsValue(b))) Voeg(b);
        foreach (var b in AlleBlokken()) Voeg(b);
        int kruip = Math.Max(1, _ins.Kruipsnelheid);
        _log.Stap($"Blokproef voor melder {m}: elk blok om beurten een kort kruipcommando terug naar melder {_k.LocVia}.");
        foreach (var b in kandidaten)
        {
            _token.ThrowIfCancellationRequested();
            _monitor.Bijwerken();
            var t0 = _klok.Nu;
            StuurSnelheid(kruip, terug, new[] { b });
            bool bewogen = false;
            // BUG #377 (Marco 04-11 10:04, eerste rit met module 2 op de baan: "geen enkel blok liet de loc op melder 24/14/21 bewegen"): het wachten was de halve
            // doorrijtijd bij verkensnelheid, maar de proef kruipt. Bij kruipsnelheid duurt dezelfde afstand veel langer, en na het parkeren staat de loc soms
            // een hele melder van de vorige af. Nu: de hele melder bij kruipsnelheid, plus 3 s, hoogstens de ingestelde blokproeftijd.
            double factor = Math.Max(1, _ins.Verkensnelheid) / (double)kruip;
            double soloKruip = (_k.SoloTijd.TryGetValue(m, out var st377) && st377 > 0 ? st377 : 2 * Doorrij(m)) * factor;
            var eind = _klok.Nu + TimeSpan.FromSeconds(Math.Min(Math.Max(10, _ins.BlokproefLangSeconden), soloKruip + 3) + DinamoCyclus.TotalSeconds);
            while (_klok.Nu < eind)
            {
                await _klok.Wacht(Poll);
                if (Kortsluiting()) break;
                if (_monitor.Bijwerken().Any(x => x.Tijd >= t0)) { bewogen = true; break; }
            }
            if (bewogen)
            {
                _gestuurd.Add(b);
                await Stop();
                _k.Blokken[m] = b;
                _status.Kaart.Melder(m).DinamoBlok = b;
                _log.Vondst($"Melder {m} krijgt rijstroom van Dinamo-blok {b}.");
                if (Referentie?.BlokVan(m) is int kb && kb != b) _log.Waarschuwing($"Volgens de Koploper-export hoort melder {m} bij blok {kb}, niet bij {b}.");
                // weer netjes op melder m gaan staan (bij een korte melder kan de loc daarbij al op de volgende komen; dan staat hij daar)
                int via = _k.LocVia; var r = _k.LocRichting;
                _k.LocMelder = m;
                var her = await Rijd(new RitOpdracht2 { Richting = r, ParkeerMeteen = true, NietVastleggen = true, Reden = "terug na blokproef" });
                if (her.Reeks.Count >= 2) { _k.LocMelder = her.Reeks[^1]; _k.LocVia = her.Reeks[^2]; }
                else { _k.LocMelder = m; _k.LocVia = via; }
                _k.LocRichting = r;
                return;
            }
            _hw.ZetLocSnelheid(_ins.LocAdres, 0, _decoderVooruit, b, _ins.LocStappen);
            await _klok.Wacht(DinamoCyclus);
        }
        _gestuurd.Clear();
        _log.Waarschuwing($"Geen enkel blok liet de loc op melder {m} bewegen; het blok blijft onbekend.");
    }

    // ================================================================ wissels

    /// <summary>De volger(s) van een meester en het Dinamo-commando dat ze krijgen: op de baan dezelfde stand (bij een omgekeerd aangesloten wissel het tegenovergestelde commando).</summary>
    private IEnumerable<(int Volger, bool Stand)> Volgers(int meester, bool afb)
    {
        foreach (var p in _paren.Where(p => p.Meester == meester))
        {
            bool baan = afb ^ _omgekeerd.Contains(meester);
            yield return (p.Volger, baan ^ _omgekeerd.Contains(p.Volger));
        }
    }

    /// <summary>WAT: mag adres a nu omgezet worden? Een gevonden wissel niet als de loc op een melder van zijn wisselgebied staat (ook dat van zijn volger);
    /// een nog niet gevonden adres alleen als de loc netjes op één melder staat. WAAROM: nooit een wissel onder de loc omzetten (ontsporing, Marco 26-10 15:21).</summary>
    /// <summary>BUG #387: een taak mislukte alleen omdat de loc op een grens stond; dat telt niet als mislukte poging (de hoofdlus parkeert eerst netjes).</summary>
    private bool _nietNetjes387;

    /// <summary>BUG #395/#397: het omzetten of terugzetten van wissel a hief een kortsluiting op, dus ligt a onder of vlak bij de loc: de melders waar de loc nu staat
    /// horen voortaan bij de zone van a (daar wordt a niet meer omgezet; simulatie met start op 25: 864 keer wissel 10 omgezet met de loc op 24).</summary>
    private void OnthoudOnderLoc395(int a)
    {
        _monitor.Bijwerken();
        var bij = _k.ExtraZone.TryGetValue(a, out var h395) ? h395 : _k.ExtraZone[a] = new HashSet<int>();
        bij.UnionWith(_monitor.Bezet);
        if (_k.LocMelder > 0) bij.Add(_k.LocMelder);
    }

    private bool Veilig(int a, IReadOnlyCollection<int> bezet, out string reden)
    {
        reden = "";
        var zone = _k.Zone(a);
        foreach (var (v, _) in Volgers(a, false)) zone.UnionWith(_k.Zone(v));
        if (zone.Count > 0)
        {
            var raak = bezet.Where(zone.Contains).ToList();
            if (raak.Count > 0) { reden = $"de loc staat op melder {string.Join("/", raak)}, waar wissel {a} ligt"; _nietHier404 = true; return false; }
            // BUG #404 (Marco 12-11 09:25: "voltooid, maar maar 1 wissel"): is bekend waar de wissel ligt (ook als dat alleen uit een opgeheven kortsluiting komt, #395),
            // dan is elke andere plek veilig, net als in de planning (VeiligOp). In #395 gold hier daarna nog de eis "geen kruising"; de planning wist dat niet, zette
            // wissel 9 op melder 24 (de kruiswissel) in het plan, het zetten weigerde, en om 09:20:48 faalden zo binnen een halve seconde alle taken.
            return true;
        }
        if (bezet.Count != 1) { reden = $"de loc staat niet netjes op één melder ({string.Join("/", bezet)})"; _nietNetjes387 = true; return false; }
        if (!ProefPlek(bezet.First())) { reden = $"melder {bezet.First()} is een kruising of te kort om daar een nog niet gevonden adres om te zetten"; _nietHier404 = true; return false; }
        return true;
    }

    /// <summary>WAT: zet een reeks adressen (alleen de adressen die anders staan). Eerst controleren of ALLES veilig kan; anders niets.</summary>
    private async Task<bool> ZetStand(IReadOnlyDictionary<int, bool> stand, string reden = "")
    {
        _monitor.Bijwerken();
        var bezet = _monitor.Bezet.ToList();
        var nodig = stand.Where(kv => _adressen.Contains(kv.Key) && Fysiek(kv.Key) != kv.Value).ToList();
        // BUG #387 (simulatie met Marco's voortgang van 08-11): de loc stond na een kortsluitherstel op de grens van 129 en 4. Elke taak faalde daardoor binnen dezelfde
        // tiende seconde ("niet netjes op één melder"), werd overgeslagen, en de verkenning stopte als niet af. Eerst netjes op één melder gaan staan en dan opnieuw kijken.
        if (nodig.Count > 0 && bezet.Count == 2 && !Kortsluiting())
        {
            try { await Herparkeer(_k.LocRichting.Om(), alleBlokken: true); } catch (PlanFout) { }
            _monitor.Bijwerken();
            bezet = _monitor.Bezet.ToList();
            if (bezet.Count == 1) _k.LocMelder = bezet[0];
        }
        foreach (var (a, _) in nodig)
            if (!Veilig(a, bezet, out var waarom))
            {
                _log.Waarschuwing($"Wissel {a} wordt niet omgezet: {waarom}.");
                return false;
            }
        foreach (var (a, v) in nodig) await ZetAdres(a, v);
        return true;
    }

    /// <summary>WAT: één adres zetten, met de volger(s) van de overloopset. Zonder <paramref name="forceer"/> alleen als de stand anders is.</summary>
    private async Task ZetAdres(int a, bool afb, bool forceer = false)
    {
        if (!forceer && _k.Standen.TryGetValue(a, out var s) && s == afb) return;
        await Commando(a, afb);
        foreach (var (v, dv) in Volgers(a, afb))
            if (forceer || !_k.Standen.TryGetValue(v, out var sv) || sv != dv) await Commando(v, dv);
    }

    /// <summary>WAT: één wisselcommando naar Dinamo en wachten op de terugmelding (= de puls is gegeven). Komt die niet, dan na een pauze opnieuw, met steeds
    /// langere pauzes (Dinamo voerde soms een tijd niets uit, BUG #173). Na 10 minuten zonder succes wordt Marco gevraagd Dinamo te controleren.</summary>
    private async Task Commando(int a, bool afb)
    {
        var bron = _hw as IWisselBevestiging;
        var t0 = _klok.Nu;
        int[] pauzes = { 0, 2, 5, 10, 20, 30 };
        bool gevraagd = false;
        for (int poging = 0; ; poging++)
        {
            _token.ThrowIfCancellationRequested();
            if (_hw is IWisselPuls p && p.DinamoHold)
                for (int i = 0; i < 25 && p.DinamoHold; i++) await Wacht(TimeSpan.FromMilliseconds(200));
            long voor = bron?.BevestigingenVan(a, afb) ?? -1;
            long totaalVoor = bron?.AantalWisselBevestigingen ?? 0;
            _hw.ZetWissel(a, afb);
            _log.Rijden($"Adres {a}: {(afb ? "afbuigend" : "rechtdoor")}{(poging > 0 ? $" (poging {poging + 1})" : "")}.");
            if (bron is null)
            {
                await Wacht(TimeSpan.FromMilliseconds(Math.Max(200, _ins.WisselPauzeMs)));
                _k.Standen[a] = afb;
                return;
            }
            var eind = _klok.Nu + TimeSpan.FromSeconds(4);
            bool ok = false;
            while (_klok.Nu < eind)
            {
                await Wacht(TimeSpan.FromMilliseconds(100));
                if (voor >= 0 ? bron.BevestigingenVan(a, afb) > voor : bron.AantalWisselBevestigingen > totaalVoor) { ok = true; break; }
            }
            if (ok)
            {
                _k.Standen[a] = afb;
                await Wacht(TimeSpan.FromMilliseconds(300));   // de tong de tijd geven
                return;
            }
            int pauze = pauzes[Math.Min(poging, pauzes.Length - 1)];
            _log.Waarschuwing($"Wissel {a}: geen terugmelding van Dinamo (niet uitgevoerd). Over {pauze} s opnieuw.");
            if (!gevraagd && _klok.Nu - t0 > ZelfZoekenMaximaal)
            {
                gevraagd = true;
                _log.Waarschuwing("Dinamo voert al 10 minuten geen wisselcommando uit: Marco gevraagd Dinamo te controleren.");
                await _vraag("Dinamo voert geen wisselcommando's uit", $"Dinamo meldt al 10 minuten geen enkel wisselcommando terug (laatste: wissel {a}). Controleer Dinamo (eventueel even uit en aan) en klik daarna op OK.");
            }
            if (pauze > 0) await Wacht(TimeSpan.FromSeconds(pauze));
        }
    }

    // ================================================================ kortsluiting

    /// <summary>
    /// WAT: de loc staat in een kortsluiting na melder <paramref name="melder"/> (rijrichting <paramref name="r"/>). Eerst de adressen in <paramref name="terug"/>
    /// terugzetten (de wissels die voor deze rit anders stonden dan in een stand die bewezen goed ging); helpt dat niet, dan langzaam terugrijden; dan de wissels
    /// van die knoop één voor één proberen. Pas na 10 minuten wordt Marco gevraagd de loc terug te zetten.
    /// WAAROM: Marco 25-10 (BUG #305): zelf oplossen; BUG #169: een wissel van de gesplitste kant bereden: omzetten heft de kortsluiting op.
    /// </summary>
    /// <summary>BUG #414: hoe vaak de kortsluitproef moet lukken voordat een wissel als oorzaak geldt (Marco: "herhaal deze test 3x").</summary>
    private const int KortsluitProefHerhalen = 3;

    private async Task<bool> HerstelKortsluiting(Dictionary<int, bool> terug, Richting r)
    {
        _k.Kortsluitingen++;
        int melder = _k.LocMelder;
        _log.Waarschuwing($"KORTSLUITING na melder {melder} ({r.Tekst()}). Zelf oplossen.");
        var t0 = _klok.Nu;
        await Stop();
        // 1. de voor deze rit omgezette wissels terug (van de gesplitste kant bereden: dat lost het op)
        // Eén voor één, en na elke wissel kijken: zodra de kortsluiting weg is, gaat er niets meer om (de loc kan dan op een volgende wissel staan).
        foreach (var (a, v) in terug.Where(kv => Fysiek(kv.Key) != kv.Value).ToList())
        {
            await ZetAdres(a, v);
            if (await WachtOpGeenKortsluiting(TimeSpan.FromSeconds(2))) { _log.Info($"Zelf opgelost: wissel {a} terugzetten hief de kortsluiting op."); OnthoudOnderLoc395(a); return true; }
        }
        // 1b. KORTSLUITPROEF volgens Marco's stappenplan (14-11, "Stappen plan.docx"; akkoord 09:18 om wissels om te zetten terwijl de loc stilstaat, ook als hij
        // er vlak bij of op staat). WAT: met de loc STIL de wissels van deze plek één voor één omzetten, steeds 5 s wachten (de Dinamo voert elk datagram als
        // aparte puls uit, #352). Verdwijnt de kortsluiting, dan de proef herhalen (terug: kortsluiting moet terugkomen; weer om: weg) tot 3 keer gelukt.
        // Lukt dat, dan blijft de wissel in de stand die de kortsluiting oploste en ligt hij "achter deze sectie".
        // WAAROM: eerder werd eerst teruggereden; dan verdwijnt de kortsluiting doordat de loc wegrijdt en weet je niet welke wissel het was
        // (zo ontstond ook de valse vondst "wissel 3", #406). Met de loc stil kan alleen de wissel de oorzaak zijn.
        // volgorde: de wissels van deze knoop, dan die in de buurt van deze melder, dan de rest
        var proefKand = _k.RelevantInKnoop(melder, r).Concat(_adressen.Where(a => _k.Zone(a).Contains(melder) || _k.Zone(a).Contains(_k.LocVia)))
                          .Concat(_adressen).Distinct().ToList();
        foreach (var a in proefKand)
        {
            if (_klok.Nu - t0 >= ZelfZoekenMaximaal / 2) break;
            if (!Kortsluiting()) { _log.Info("Zelf opgelost: de kortsluiting is weg."); return true; }
            bool oud = Fysiek(a);
            await ZetAdres(a, !oud);
            if (!await WachtOpGeenKortsluiting(TimeSpan.FromSeconds(5))) { await ZetAdres(a, oud); await Wacht(TimeSpan.FromSeconds(1)); continue; }
            // herhalen: terug moet de kortsluiting teruggeven, weer om moet hem opheffen
            int gelukt = 1;
            while (gelukt < KortsluitProefHerhalen)
            {
                await ZetAdres(a, oud);
                if (await WachtOpGeenKortsluiting(TimeSpan.FromSeconds(5))) break;          // kwam niet terug: niet zeker
                await ZetAdres(a, !oud);
                if (!await WachtOpGeenKortsluiting(TimeSpan.FromSeconds(5))) break;         // ging niet weg: niet zeker
                gelukt++;
            }
            if (gelukt >= KortsluitProefHerhalen && !Kortsluiting())
            {
                _log.Vondst($"Kortsluitproef na melder {melder} ({r.Tekst()}): wissel {a} {(!oud ? "afbuigend" : "rechtdoor")} hief de kortsluiting {gelukt} keer op; deze wissel ligt achter deze sectie en blijft zo staan (stappenplan Marco 14-11, BUG #414, nog niet getest op de baan).");
                OnthoudOnderLoc395(a);
                return true;
            }
            // niet zeker: zorg dat de kortsluiting weg is als dat met deze stand kan, anders terug naar de oude stand
            if (Kortsluiting()) { await ZetAdres(a, !Fysiek(a)); await WachtOpGeenKortsluiting(TimeSpan.FromSeconds(5)); }
            if (!Kortsluiting()) { _log.Info($"Kortsluiting weg na omzetten van wissel {a}, maar de herhaling klopte niet ({gelukt} van {KortsluitProefHerhalen}): niet als oorzaak vastgelegd (BUG #414)."); return true; }
        }
        // 2. langzaam terugrijden (van het puntstuk af)
        for (int poging = 0; poging < 2; poging++)
        {
            StuurSnelheid(Math.Max(1, _ins.Kruipsnelheid), r.Om(), BlokkenVoor(_monitor.Bezet, alles: true));
            bool weg = await WachtOpGeenKortsluiting(TimeSpan.FromSeconds(8));
            await Wacht(TimeSpan.FromSeconds(1));
            await Stop();
            if (weg && !Kortsluiting()) { _log.Info("Zelf opgelost: langzaam terugrijden hief de kortsluiting op."); return true; }
        }
        // 3. de wissels van deze knoop (en daarna alle adressen) één voor één proberen
        var kandidaten = _k.RelevantInKnoop(melder, r).Concat(_adressen).Distinct().ToList();
        while (_klok.Nu - t0 < ZelfZoekenMaximaal)
        {
            foreach (var a in kandidaten)
            {
                bool oud = Fysiek(a);
                await ZetAdres(a, !oud);
                if (await WachtOpGeenKortsluiting(TimeSpan.FromSeconds(3)))
                {
                    _log.Info($"Zelf opgelost: wissel {a} omzetten hief de kortsluiting op.");
                    // BUG #395: deze wissel ligt dus onder of vlak bij de loc. Niet terugzetten zolang de loc op deze melder(s) staat (simulatie: wissel 14 hief de kortsluiting
                    // op melder 8 op; de volgende proef zette hem meteen terug, met de loc nog op 8 = omgezet onder de loc).
                    OnthoudOnderLoc395(a);
                    return true;
                }
                await ZetAdres(a, oud);
                if (_klok.Nu - t0 >= ZelfZoekenMaximaal) break;
            }
            StuurSnelheid(Math.Max(1, _ins.Kruipsnelheid), r.Om(), BlokkenVoor(_monitor.Bezet, alles: true));
            await Wacht(TimeSpan.FromSeconds(6));
            await Stop();
            if (!Kortsluiting()) { _log.Info("Zelf opgelost: terugrijden hief de kortsluiting op."); return true; }
        }
        _log.Waarschuwing("Zelf oplossen lukte niet in 10 minuten. Gebruiker heeft de loc gevraagd terug te zetten.");
        int doel = _k.LocVia > 0 ? _k.LocVia : melder;
        await _vraag("Loc terugzetten", $"De loc zit na melder {melder} in een kortsluiting die niet zelf op te lossen was. Zet de loc met de hand op melder {doel}, zodat de verkenning verder kan, en klik op OK.");
        await Wacht(TimeSpan.FromSeconds(2));
        _monitor.Bijwerken();
        _k.LocMelder = doel; _k.LocVia = 0;
        return !Kortsluiting();
    }

    private async Task<bool> WachtOpGeenKortsluiting(TimeSpan max)
    {
        var eind = _klok.Nu + max;
        while (_klok.Nu < eind)
        {
            await Wacht(TimeSpan.FromMilliseconds(100));
            if (!Kortsluiting()) { await Wacht(TimeSpan.FromMilliseconds(600)); if (!Kortsluiting()) return true; }
        }
        return !Kortsluiting();
    }
}
