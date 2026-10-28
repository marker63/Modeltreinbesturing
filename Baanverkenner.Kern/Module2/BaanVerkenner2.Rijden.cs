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

    private bool DecoderVooruit(Richting r) => (r == Richting.Vooruit) ^ _k.Omgedraaid;

    /// <summary>WAT: stuurt het rijcommando naar de blokken (zonder te wachten; Dinamo verwerkt ze één per 200 ms).</summary>
    private void StuurSnelheid(int stap, Richting r, IEnumerable<int> blokken)
    {
        _decoderVooruit = DecoderVooruit(r);
        _huidigeStap = stap;
        foreach (var b in blokken)
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
    internal async Task<RitUitslag2> Rijd(RitOpdracht2 op)
    {
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
            bool mee = _k.Stappen.Any(x => !x.Terug && x.Melder == vorigeM && x.Richting == h && x.Uitkomst == n);
            if (tegen && !mee)
            {
                _k.Omgedraaid = !_k.Omgedraaid;
                h = h.Om();
                _log.Vondst($"De loc rijdt andersom dan gedacht (van melder {vorigeM} naar {n} is volgens de kaart {h.Tekst()}): rijrichtingen worden vanaf nu omgedraaid (keerlus of omgedraaide loc).");
                omgedraaidInRit = true;
                draaiIndex = res.Reeks.Count;   // index die n zo krijgt
            }
            res.Seconden.Add((tijd - binnenTijd).TotalSeconds * snelheid / verken);
            binnenTijd = tijd;
            res.Reeks.Add(n);
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
            _monitor.Bijwerken();
            var na = _monitor.Bezet.ToList();
            if (na.Count == 1 && na[0] == via && via > 0) { _k.LocMelder = via; _k.LocVia = m; _k.LocRichting = h.Om(); }
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
        for (int i = 1; i < r.Count; i++)
        {
            // bleek de loc halverwege andersom te rijden (keerlus), dan hebben de stappen daarvóór nog de oude kaartrichting; bij de eerste stap is alles de nieuwe
            var hi = draaiIndex > 1 && i < draaiIndex ? h.Om() : h;
            bool eersteTerug = i == 1 && terug && draaiIndex != 1;
            int via = i >= 2 ? r[i - 2] : (eersteTerug || draaiIndex == 1 ? 0 : startVia);
            _k.Leg(via, r[i - 1], hi, stand, r[i], _klok.Nu, terug: eersteTerug, seconden: i - 1 < res.Seconden.Count ? res.Seconden[i - 1] : 0);
        }
        if (res.Einde is RitEinde2.Kortsluiting or RitEinde2.Doodlopend)
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
            var eind = _klok.Nu + TimeSpan.FromSeconds(Doorrij(m) + 3 + DinamoCyclus.TotalSeconds);
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
    private bool Veilig(int a, IReadOnlyCollection<int> bezet, out string reden)
    {
        reden = "";
        var zone = _k.Zone(a);
        foreach (var (v, _) in Volgers(a, false)) zone.UnionWith(_k.Zone(v));
        if (zone.Count > 0)
        {
            var raak = bezet.Where(zone.Contains).ToList();
            if (raak.Count > 0) { reden = $"de loc staat op melder {string.Join("/", raak)}, waar wissel {a} ligt"; return false; }
            return true;
        }
        if (bezet.Count != 1) { reden = $"de loc staat niet netjes op één melder ({string.Join("/", bezet)})"; return false; }
        if (!ProefPlek(bezet.First())) { reden = $"melder {bezet.First()} is een kruising of te kort om daar een nog niet gevonden adres om te zetten"; return false; }
        return true;
    }

    /// <summary>WAT: zet een reeks adressen (alleen de adressen die anders staan). Eerst controleren of ALLES veilig kan; anders niets.</summary>
    private async Task<bool> ZetStand(IReadOnlyDictionary<int, bool> stand, string reden = "")
    {
        _monitor.Bijwerken();
        var bezet = _monitor.Bezet.ToList();
        var nodig = stand.Where(kv => _adressen.Contains(kv.Key) && Fysiek(kv.Key) != kv.Value).ToList();
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
            if (await WachtOpGeenKortsluiting(TimeSpan.FromSeconds(2))) { _log.Info($"Zelf opgelost: wissel {a} terugzetten hief de kortsluiting op."); return true; }
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
                if (await WachtOpGeenKortsluiting(TimeSpan.FromSeconds(3))) { _log.Info($"Zelf opgelost: wissel {a} omzetten hief de kortsluiting op."); return true; }
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
