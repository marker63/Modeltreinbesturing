using Modeltreinbesturing.Hardware;

namespace Baanverkenner.Kern;

/// <summary>Uitkomst van één rit.</summary>
public class RitResultaat
{
    public List<int> Reeks { get; } = new();
    public RitEinde Einde { get; set; }
    public Richting Richting { get; set; }
}

/// <summary>Wat een rit moet doen. Zonder DoelMelder is het een verkenningsrit (rijden tot
/// er iets gebeurt); met DoelMelder een navigatierit naar een bekende melder.</summary>
public class RitDoel
{
    public Configuratie Configuratie { get; set; } = Configuratie.Basis;

    // Navigatie
    public int? DoelMelder { get; set; }
    public List<int>? VerwachtPad { get; set; }

    // Proefrit: vergelijken met de basisrit en vroeg stoppen
    public List<int>? Basis { get; set; }
    public int ExtraNaAfwijking { get; set; } = 2;

    /// <summary>Melders waarna (in deze rijrichting) een bekend kortsluitpunt volgt: daar
    /// stopt de rit al, zodat dezelfde kortsluiting niet steeds opnieuw gemaakt wordt.</summary>
    public HashSet<int> StopBijMelders { get; set; } = new();

    public bool IsNavigatie => DoelMelder is not null;
}

/// <summary>De loc kwam niet waar hij verwacht werd (bijv. een wissel die niet omging).</summary>
public class NavigatieFout : Exception
{
    public NavigatieFout(string melding) : base(melding) { }

    /// <summary>Bij een afwijking onderweg: de laatste melder die nog klopte en de melder
    /// die onverwacht bezet werd.</summary>
    public int? LaatsteGoed { get; init; }
    public int? Onverwacht { get; init; }
}

public partial class BaanVerkenner
{
    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan DinamoCyclus = TimeSpan.FromMilliseconds(200);

    private readonly Dictionary<int, bool> _wisselStand = new();
    private DateTime _negeerKortsluitingTot = DateTime.MinValue;
    private volatile bool _kortsluitingGemeld;
    private readonly HashSet<int> _blokproefMislukt = new();

    private bool BlokVereist => _hw.LocCommandoVereistBlok;

    private int? BlokVan(int melder) => _kaart.Melders.FirstOrDefault(m => m.Nummer == melder)?.DinamoBlok;

    private void HardwareMeldtKortsluiting(bool actief)
    {
        if (!actief) return;
        if (_klok.Nu < _negeerKortsluitingTot) return; // gevolg van onze eigen noodstop (F-bit)
        _kortsluitingGemeld = true;
    }

    // =====================================================================
    // Snelheid en stoppen
    // =====================================================================

    /// <summary>Stuurt een snelheid naar de loc. Bij Dinamo via de blokken van de melders
    /// waar de loc nu staat; is daarvan een blok onbekend, dan naar ALLE blokken (er staat
    /// maar één loc op de baan, dus dat kan veilig - het is alleen trager).</summary>
    private async Task StuurSnelheid(Richting r, int stap)
    {
        bool vooruit = r == Richting.Vooruit;
        if (!BlokVereist)
        {
            _hw.ZetLocSnelheid(_ins.LocAdres, stap, vooruit, 0, _ins.LocStappen);
            await Wacht(TimeSpan.FromMilliseconds(100));
            return;
        }
        var blokken = BlokkenVoorHuidigePlek();
        blokken ??= _ins.DinamoBlokLijst();
        foreach (var b in blokken) _hw.ZetLocSnelheid(_ins.LocAdres, stap, vooruit, b, _ins.LocStappen);
        await Wacht(DinamoCyclus * blokken.Count);
    }

    /// <summary>Blokken van de melders waar de loc nu staat, of null als er (nog) één
    /// onbekend is of er niets bezet is.</summary>
    private List<int>? BlokkenVoorHuidigePlek()
    {
        var bezet = _monitor.Bezet;
        if (bezet.Count == 0) return null;
        var res = new List<int>();
        foreach (var m in bezet)
        {
            var b = BlokVan(m);
            if (b is null) return null;
            if (!res.Contains(b.Value)) res.Add(b.Value);
        }
        return res;
    }

    private async Task StuurNaarBlok(int blok, Richting r, int stap)
    {
        _hw.ZetLocSnelheid(_ins.LocAdres, stap, r == Richting.Vooruit, blok, _ins.LocStappen);
        await Wacht(DinamoCyclus);
    }

    /// <summary>Loc stoppen. Waar mogelijk snel (snelheid 0 via het bekende blok); anders
    /// de volledige stop.</summary>
    private async Task StopLoc()
    {
        if (!BlokVereist)
        {
            _hw.ZetLocSnelheid(_ins.LocAdres, 0, true, 0, _ins.LocStappen);
            await Wacht(TimeSpan.FromMilliseconds(600));
            return;
        }
        var blokken = BlokkenVoorHuidigePlek();
        if (blokken is null)
        {
            await VolledigeStop();
            return;
        }
        foreach (var b in blokken) _hw.ZetLocSnelheid(_ins.LocAdres, 0, true, b, _ins.LocStappen);
        await Wacht(DinamoCyclus * blokken.Count + TimeSpan.FromMilliseconds(500));
    }

    /// <summary>Stop als niet bekend is via welk blok de loc bereikbaar is: de noodstop van
    /// de centrale vasthouden (alles staat direct stil) terwijl snelheid 0 naar ALLE blokken
    /// gaat; daarna controleren dat er echt niets meer beweegt.</summary>
    private async Task VolledigeStop()
    {
        if (!BlokVereist)
        {
            _hw.Noodstop();
            _hw.ZetLocSnelheid(_ins.LocAdres, 0, true, 0, _ins.LocStappen);
            await Wacht(TimeSpan.FromMilliseconds(800));
            return;
        }
        var blokken = _ins.DinamoBlokLijst();
        var duur = DinamoCyclus * blokken.Count + TimeSpan.FromMilliseconds(400);
        _negeerKortsluitingTot = _klok.Nu + duur + TimeSpan.FromSeconds(3);
        _hw.Noodstop();
        foreach (var b in blokken) _hw.ZetLocSnelheid(_ins.LocAdres, 0, true, b, _ins.LocStappen);
        var eind = _klok.Nu + duur;
        while (_klok.Nu < eind)
        {
            await Wacht(TimeSpan.FromMilliseconds(400));
            _hw.Noodstop(); // F-bit vasthouden tot alle stopcommando's verstuurd zijn
        }
        await Wacht(TimeSpan.FromMilliseconds(1500)); // F-bit loslaten + uitrollen
        _monitor.Bijwerken();

        // Controle: beweegt er nog iets? Dan is een stopcommando niet aangekomen.
        await Wacht(TimeSpan.FromMilliseconds(1500));
        if (_monitor.Bijwerken().Count > 0)
        {
            _log.Waarschuwing("Na de volledige stop veranderden er nog melders - stop wordt herhaald.");
            _hw.Noodstop();
            foreach (var b in blokken) _hw.ZetLocSnelheid(_ins.LocAdres, 0, true, b, _ins.LocStappen);
            await Wacht(DinamoCyclus * blokken.Count + TimeSpan.FromMilliseconds(1500));
            _monitor.Bijwerken();
        }
    }

    // =====================================================================
    // Wissels
    // =====================================================================

    private async Task ZetWissel(int adres, bool afbuigend, bool forceer = false)
    {
        if (!forceer && _wisselStand.TryGetValue(adres, out var huidig) && huidig == afbuigend) return;
        _hw.ZetWissel(adres, afbuigend);
        _wisselStand[adres] = afbuigend;
        _log.Rijden($"Adres {adres} → {(afbuigend ? "afbuigend" : "rechtdoor")}");
        await Wacht(TimeSpan.FromMilliseconds(Math.Max(100, _ins.WisselPauzeMs)));
        // Dinamo: pas verder als het commando echt de deur uit is
        for (int i = 0; i < 40 && !_hw.KlaarVoorVolgendeWisselCommando; i++) await Wacht(DinamoCyclus);
    }

    private async Task ZetConfiguratie(Configuratie c)
    {
        foreach (var a in _ins.WisselAdressen())
            await ZetWissel(a, c.Bevat(a));
    }

    // =====================================================================
    // Blokproef (alleen Dinamo): welk blok voedt de sectie waar de loc staat?
    // =====================================================================

    private List<int> BlokKandidaten(int? eerst)
    {
        var alle = _ins.DinamoBlokLijst();
        var gebruikt = _kaart.Melders.Where(m => m.DinamoBlok is not null).Select(m => m.DinamoBlok!.Value).ToHashSet();
        var res = new List<int>();
        if (eerst is int e && alle.Contains(e)) res.Add(e);
        res.AddRange(alle.Where(b => !gebruikt.Contains(b) && !res.Contains(b)));
        res.AddRange(alle.Where(b => !res.Contains(b)));
        return res;
    }

    private void KoppelBlok(int melder, int blok)
    {
        _kaart.Melder(melder).DinamoBlok = blok;
        _log.Vondst($"Melder {melder} krijgt rijstroom van Dinamo-blok {blok}.");
        MeldVoortgang();
    }

    /// <summary>Bij de start: de loc staat ergens binnen melder <paramref name="melder"/>
    /// (positie onbekend). Elk blok krijgt om beurten een rijcommando; het blok waarbij
    /// binnen BlokproefLangSeconden een melder verandert, is het goede.</summary>
    private async Task<bool> BlokZoekenBijStart(int melder)
    {
        _log.Stap($"Blokproef voor melder {melder}: elk Dinamo-blok krijgt om beurten een rijcommando.");
        foreach (var r in new[] { Richting.Vooruit, Richting.Achteruit })
        {
            foreach (var blok in BlokKandidaten(null))
            {
                _token.ThrowIfCancellationRequested();
                _monitor.Bijwerken();
                _log.Rijden($"Blokproef: blok {blok}, {r.Tekst()} …");
                await StuurNaarBlok(blok, r, _ins.Verkensnelheid);
                var eind = _klok.Nu + TimeSpan.FromSeconds(_ins.BlokproefLangSeconden);
                bool gewijzigd = false;
                while (_klok.Nu < eind)
                {
                    await Wacht(Poll);
                    if (_monitor.Bijwerken().Count > 0) { gewijzigd = true; break; }
                }
                if (gewijzigd)
                {
                    // Eerst koppelen, dan stoppen via de blokken waar de loc NU staat (hij
                    // kan al een sectie verder zijn - een 0 naar alleen het proefblok komt
                    // dan niet meer aan).
                    KoppelBlok(melder, blok);
                    _hw.ZetLocSnelheid(_ins.LocAdres, 0, r == Richting.Vooruit, blok, _ins.LocStappen);
                    await StopLoc();
                    await Nastellen(melder, r);
                    return true;
                }
                await StuurNaarBlok(blok, r, 0);
            }
            _log.Info($"Geen enkel blok liet de loc {r.Tekst()} rijden - misschien staat hij aan het eind van een kopspoor; nu de andere richting.");
        }
        _log.Fout($"Geen enkel Dinamo-blok ({_ins.DinamoBlokken}) kon de loc op melder {melder} laten rijden. Controleer locadres, rijstappen en het blokbereik.");
        return false;
    }

    /// <summary>Tijdens het rijden: de loc staat net helemaal in een nieuwe sectie
    /// <paramref name="melder"/>, vlak achter de grens met <paramref name="vorige"/>. Een
    /// klein stukje terug laat de vorige melder snel weer bezet worden - maar alleen als het
    /// juiste blok het commando stuurt.</summary>
    private async Task<bool> BlokZoekenNaInrijden(int melder, int vorige, Richting r)
    {
        _log.Stap($"Nieuwe sectie: melder {melder}. Blokproef (steeds een klein stukje {r.Om().Tekst()}).");
        foreach (var blok in BlokKandidaten(BlokVan(vorige)))
        {
            _token.ThrowIfCancellationRequested();
            _monitor.Bijwerken();
            await StuurNaarBlok(blok, r.Om(), _ins.Verkensnelheid);
            var eind = _klok.Nu + TimeSpan.FromSeconds(_ins.BlokproefKortSeconden);
            bool gewijzigd = false;
            while (_klok.Nu < eind)
            {
                await Wacht(Poll);
                if (_monitor.Bijwerken().Count > 0) { gewijzigd = true; break; }
            }
            if (gewijzigd)
            {
                KoppelBlok(melder, blok);
                _hw.ZetLocSnelheid(_ins.LocAdres, 0, r.Om() == Richting.Vooruit, blok, _ins.LocStappen);
                await StopLoc(); // via de blokken waar de loc nu staat (zie BlokZoekenBijStart)
                await Nastellen(melder, r.Om(), voor: vorige);
                return true;
            }
            await StuurNaarBlok(blok, r.Om(), 0);
            _log.Rijden($"Blok {blok}: geen beweging.");
        }
        _log.Waarschuwing($"Voor melder {melder} is geen Dinamo-blok gevonden - de verkenner stuurt hier voortaan naar alle blokken.");
        return false;
    }

    // =====================================================================
    // Neerzetten op een melder
    // =====================================================================

    /// <summary>Zorgt dat alleen <paramref name="doel"/> bezet is. De loc reed laatst in
    /// <paramref name="laatsteRichting"/>. Staat hij nog half op de melder erachter
    /// (<paramref name="achter"/>), dan rijdt hij verder; staat hij er deels voorbij
    /// (<paramref name="voor"/>) of helemaal voorbij, dan kruipt hij terug. Onbekend: eerst
    /// verder, en bij doorschieten terug.</summary>
    private async Task Nastellen(int doel, Richting laatsteRichting, int? achter = null, int? voor = null)
    {
        await Wacht(TimeSpan.FromMilliseconds(500));
        _monitor.Bijwerken();
        var bezet = _monitor.Bezet;
        if (bezet.Count == 1 && bezet.Contains(doel)) return;

        Richting r;
        if (!bezet.Contains(doel)) r = laatsteRichting.Om();
        else if (voor is int v && bezet.Contains(v)) r = laatsteRichting.Om();
        else if (achter is int a && bezet.Contains(a)) r = laatsteRichting;
        else r = laatsteRichting;

        _log.Rijden($"Neerzetten op melder {doel} (nu bezet: {Lijst(bezet)}), kruipen {r.Tekst()}.");
        for (int wissel = 0; wissel < 3; wissel++)
        {
            if (wissel > 0) _log.Rijden($"Neerzetten op melder {doel}: nu {r.Tekst()} (bezet: {Lijst(_monitor.Bezet)}).");
            bool doelWasBezet = _monitor.IsBezet(doel);
            await StuurSnelheid(r, _ins.Kruipsnelheid);
            var eind = _klok.Nu + TimeSpan.FromSeconds(Math.Max(15, _ins.MinSecondenTussenMelders));
            bool omdraaien = false;
            while (_klok.Nu < eind)
            {
                await Wacht(Poll);
                _monitor.Bijwerken();
                bezet = _monitor.Bezet;
                if (bezet.Count == 1 && bezet.Contains(doel)) break;
                if (doelWasBezet && !bezet.Contains(doel) && bezet.Count > 0) { omdraaien = true; break; } // doorgeschoten
                if (bezet.Contains(doel)) doelWasBezet = true;
            }
            await StopLoc();
            _monitor.Bijwerken();
            bezet = _monitor.Bezet;
            if (bezet.Count == 1 && bezet.Contains(doel)) return;
            if (!omdraaien && bezet.Contains(doel) && bezet.Count > 1 && wissel > 0) break;
            r = r.Om();
        }
        if (!bezet.Contains(doel))
            throw new NavigatieFout($"De loc kon niet netjes op melder {doel} gezet worden (bezet: {Lijst(bezet)}).");
        if (bezet.Count > 1)
            _log.Waarschuwing($"Loc staat op melder {doel} maar ook nog op {Lijst(bezet.Where(m => m != doel))} - verder zo goed mogelijk.");
    }

    // =====================================================================
    // De rit zelf
    // =====================================================================

    private TimeSpan WachttijdTussenMelders()
    {
        double max = _ins.MaxSecondenTussenMelders;
        if (!_ins.SlimmeTimeout) return TimeSpan.FromSeconds(max);
        double langste = _kaart.LangsteReistijd();
        if (langste <= 0) return TimeSpan.FromSeconds(max);
        double s = Math.Clamp(langste * 3, _ins.MinSecondenTussenMelders, max);
        return TimeSpan.FromSeconds(s);
    }

    /// <summary>Rijdt vanaf de melder waar de loc nu staat in richting <paramref name="r"/>
    /// en legt elke nieuw bezette melder vast, tot een van de stopcondities optreedt.</summary>
    private async Task<RitResultaat> Rit(int start, Richting r, RitDoel doel)
    {
        var res = new RitResultaat { Richting = r };
        res.Reeks.Add(start);
        _monitor.Bijwerken();

        if (doel.IsNavigatie && start == doel.DoelMelder)
        {
            res.Einde = RitEinde.DoelBereikt;
            return res;
        }

        // Bij Dinamo met een (nog) onbekend blok gaat StuurSnelheid vanzelf naar alle
        // blokken; de sectie wordt gekoppeld zodra de loc er helemaal in staat.

        int snelheid = _ins.Verkensnelheid;
        _log.Rijden(doel.IsNavigatie
            ? $"Rijden {r.Tekst()} van melder {start} naar melder {doel.DoelMelder}."
            : $"Verkenningsrit {r.Tekst()} vanaf melder {start} ({doel.Configuratie}).");
        await StuurSnelheid(r, snelheid);

        var laatsteWijziging = _klok.Nu;
        var tijdVorigeMelder = _klok.Nu;
        bool onderbroken = true;          // eerste overgang: vertrek vanuit stilstand, niet meten
        DateTime? leegSinds = null;
        int? afwijkingIndex = null;
        bool doelInzicht = false;
        bool blokGecontroleerd = false;
        DateTime? heelInNieuweSectieSinds = null;
        _kortsluitingGemeld = false;

        while (true)
        {
            await Wacht(Poll);

            if (_gepauzeerd)
            {
                await StopLoc();
                await WachtTotHervat();
                await StuurSnelheid(r, doelInzicht ? _ins.Kruipsnelheid : snelheid);
                laatsteWijziging = _klok.Nu;
                onderbroken = true;
            }

            var wijzigingen = _monitor.Bijwerken();
            var bezet = _monitor.Bezet;

            // ---- Kortsluiting? ----
            if (_kortsluitingGemeld && _klok.Nu >= _negeerKortsluitingTot)
                return await Kortsluiting(res, "de centrale meldt een kortsluiting/fout");
            if (bezet.Count == 0)
            {
                leegSinds ??= _klok.Nu;
                if (_klok.Nu - leegSinds.Value > TimeSpan.FromSeconds(_ins.LegeMeldersSeconden))
                    return await Kortsluiting(res, $"geen enkele melder meer bezet na melder {res.Reeks[^1]}");
            }
            else leegSinds = null;

            // ---- Nieuwe melders ----
            foreach (var w in wijzigingen.Where(w => w.Bezet))
            {
                int m = w.Melder;
                if (m == res.Reeks[^1]) continue;
                if (res.Reeks.Count >= 2 && m == res.Reeks[^2])
                {
                    if (bezet.Contains(res.Reeks[^1])) continue; // flikkeren op de grens
                    _log.Waarschuwing($"Melder {m} werd weer bezet terwijl de loc {r.Tekst()} reed - rolt de loc terug of rijdt hij de verkeerde kant op?");
                    continue;
                }
                if (doel.IsNavigatie && doelInzicht && res.Reeks[^1] == doel.DoelMelder)
                {
                    // Doel wel bereikt maar net doorgeschoten naar de volgende melder:
                    // terugkruipen in plaats van alarm slaan.
                    await StopLoc();
                    _log.Rijden($"  melder {m} bezet: doorgeschoten, terug naar melder {doel.DoelMelder}.");
                    await Nastellen(doel.DoelMelder!.Value, r, voor: m);
                    res.Einde = RitEinde.DoelBereikt;
                    return res;
                }
                if (doel.IsNavigatie && doel.VerwachtPad is { } pad && !pad.Contains(m))
                {
                    await StopLoc();
                    throw new NavigatieFout($"Onderweg naar melder {doel.DoelMelder} werd onverwacht melder {m} bezet (verwacht: {Lijst(pad)}). Staat er een wissel niet goed?")
                    {
                        // De loc kan bij vertrek al half op de volgende melder gestaan hebben
                        // (dan kwam die nooit als "nieuw" binnen): neem de verste melder van
                        // het verwachte pad die op dit moment nog bezet is.
                        LaatsteGoed = bezet.Where(x => x != m && pad.Contains(x)).OrderByDescending(x => pad.LastIndexOf(x)).Cast<int?>().FirstOrDefault() ?? res.Reeks[^1],
                        Onverwacht = m
                    };
                }

                double? dt = onderbroken ? null : (w.Tijd - tijdVorigeMelder).TotalSeconds;
                _kaart.RegistreerOvergang(res.Reeks[^1], m, r, doel.Configuratie, dt);
                bool herhaling = res.Reeks.Contains(m);
                res.Reeks.Add(m);
                tijdVorigeMelder = w.Tijd;
                laatsteWijziging = _klok.Nu;
                onderbroken = false;
                _log.Rijden($"  melder {m} bezet{(dt is double s ? $" (na {s:0.0} s)" : "")}");

                // BUG #34 (gebruikerswaarneming: "je ziet de loc op bezet melder 5 en 16 en je
                // gaat allerlei melders en blokken aansturen, dat moet beter kunnen" - en
                // concreet: bij een HERHAALDE rit 16→5, waarvan het blok van melder 5 al
                // bekend was, bleef de loc na melder 5 gewoon stilstaan en werd dat ten
                // onrechte als "doodlopend" gemeld, terwijl de EERSTE keer (toen het blok van
                // melder 5 nog onbekend was) de rit wél gewoon doorreed naar melder 13).
                // Oorzaak: Dinamo's rijcommando's gaan ALTIJD via een blokadres (zie
                // DinamoHardware's klasse-uitleg: "DCC-commando's worden altijd VIA EEN BLOK
                // verstuurd, niet rechtstreeks naar een decoderadres") - een commando dat naar
                // blok 4 ging, bereikt de loc niet meer zodra hij fysiek blok 3 induit. Dit
                // stuk code stuurde de rijsnelheid maar ÉÉN keer, bij de START van de hele rit
                // (naar het blok van de toen-huidige melder) - zodra de loc een GRENS
                // overstak naar een volgend, AL bekend blok, kwam er nooit een nieuw
                // snelheidscommando voor dát blok. De eerste keer "werkte" het toevallig omdat
                // de blokproef voor melder 5 (toen nog onbekend) via Nastellen zelf al een
                // vers commando naar het zojuist gevonden blok 3 stuurde - puur een
                // bijwerking, geen opzet. Fix: zodra een nieuwe melder met een AL BEKEND blok
                // bezet raakt, wordt de rijsnelheid (gewoon door, of kruipend als het doel al
                // in zicht is) opnieuw gestuurd naar het blok van die nieuwe melder - zo blijft
                // de loc ook doorrijden over een melder-grens heen waarvoor geen blokproef
                // meer nodig is. Is het blok van deze melder nog NIET bekend, dan verandert er
                // hier niets - dat blijft precies zoals voorheen via de blokproef hieronder
                // lopen.
                if (BlokVereist && BlokVan(m) is not null)
                    await StuurSnelheid(r, doelInzicht ? _ins.Kruipsnelheid : snelheid);

                if (doel.IsNavigatie)
                {
                    if (m == doel.DoelMelder && !doelInzicht)
                    {
                        doelInzicht = true;
                        await StuurSnelheid(r, _ins.Kruipsnelheid);
                    }
                    continue;
                }

                if (herhaling)
                {
                    await StopLoc();
                    res.Einde = RitEinde.Lus;
                    return res;
                }
                if (res.Reeks.Count > _ins.MaxMeldersPerRit)
                {
                    await StopLoc();
                    res.Einde = RitEinde.Maximum;
                    return res;
                }
                if (doel.Basis is { } basis)
                {
                    int idx = res.Reeks.Count - 1;
                    if (afwijkingIndex is null && (idx >= basis.Count || basis[idx] != m)) afwijkingIndex = idx;
                    if (afwijkingIndex is int ai && idx - ai >= doel.ExtraNaAfwijking)
                    {
                        await StopLoc();
                        res.Einde = RitEinde.AfwijkingGevonden;
                        return res;
                    }
                    if (afwijkingIndex is null && idx == basis.Count - 1)
                    {
                        await StopLoc();
                        res.Einde = RitEinde.GelijkAanBasis;
                        return res;
                    }
                }
                if (doel.StopBijMelders.Contains(m))
                {
                    await StopLoc();
                    _log.Rijden($"  gestopt: na melder {m} volgt een bekend kortsluitpunt.");
                    res.Einde = RitEinde.BekendKortsluitpunt;
                    return res;
                }
            }

            // ---- Helemaal in een nieuwe, nog onbekende sectie (Dinamo): blokproef ----
            bool heelInNieuwe = BlokVereist && res.Reeks.Count >= 2 && bezet.Count == 1 && bezet.Contains(res.Reeks[^1])
                && BlokVan(res.Reeks[^1]) is null && !_blokproefMislukt.Contains(res.Reeks[^1]);
            if (!heelInNieuwe) heelInNieuweSectieSinds = null;
            else heelInNieuweSectieSinds ??= _klok.Nu;
            if (heelInNieuwe && _klok.Nu - heelInNieuweSectieSinds!.Value >= TimeSpan.FromSeconds(_ins.BlokproefInrijSeconden))
            {
                heelInNieuweSectieSinds = null;
                await VolledigeStop();
                if (!await BlokZoekenNaInrijden(res.Reeks[^1], res.Reeks[^2], r)) _blokproefMislukt.Add(res.Reeks[^1]);
                _monitor.Bijwerken();
                await StuurSnelheid(r, doelInzicht ? _ins.Kruipsnelheid : snelheid);
                laatsteWijziging = _klok.Nu;
                onderbroken = true;
                continue;
            }

            // ---- Navigatie: doel helemaal ingereden ----
            if (doel.IsNavigatie && doelInzicht && bezet.Count == 1 && bezet.Contains(doel.DoelMelder!.Value))
            {
                await StopLoc();
                await Nastellen(doel.DoelMelder.Value, r, achter: res.Reeks.Count >= 2 ? res.Reeks[^2] : null);
                res.Einde = RitEinde.DoelBereikt;
                return res;
            }

            // ---- Te lang geen nieuwe melder ----
            if (_klok.Nu - laatsteWijziging > WachttijdTussenMelders())
            {
                await StopLoc();
                if (BlokVereist && res.Reeks.Count == 1 && !blokGecontroleerd && !doelInzicht)
                {
                    // De loc is helemaal niet vertrokken: klopt de blokkoppeling van deze
                    // melder wel? Eén keer opnieuw bepalen voordat we iets concluderen.
                    blokGecontroleerd = true;

                    // BUG #33 (gebruikerswaarneming: "je ziet de loc op bezet melder 5 en 16
                    // en je gaat allerlei melders en blokken aansturen, dat moet beter
                    // kunnen"): hieronder stond voorheen DIRECT de koppeling weggegooid
                    // (`DinamoBlok = null`) gevolgd door `BlokZoekenBijStart` - een VOLLEDIGE
                    // blokproef die ELK bekend Dinamo-blok, in BEIDE richtingen, elk
                    // BlokproefLangSeconden (hier 30 sec) lang uitprobeert. Voor een baan met
                    // 16 blokken is dat in het ergste geval 16 × 2 × 30 = 960 sec (16 minuten)
                    // - terwijl de software het blok voor déze melder vaak AL kende (hier
                    // bijvoorbeeld blok 3 voor melder 5, een paar minuten eerder zelf
                    // gevonden). Fix: eerst GOEDKOOP herbevestigen via het AL bekende blok
                    // (één poging, met de korte BlokproefKortSeconden-wachttijd in plaats van
                    // de lange) - lukt dat, dan is er geen reden om de koppeling weg te gooien
                    // en de dure volledige zoektocht te starten. Pas als deze snelle
                    // herbevestiging ECHT niets oplevert (het blok klopt dus waarschijnlijk
                    // niet meer - bijv. een wissel die stiekem omgezet is), vervalt de
                    // koppeling alsnog en volgt exact dezelfde volledige blokproef als
                    // voorheen - dit pad is dus NIET verwijderd, alleen niet meer de EERSTE
                    // stap.
                    int? bekendBlok = BlokVan(start);
                    bool bevestigd = false;
                    if (bekendBlok is int bb)
                    {
                        _log.Rijden($"De loc vertrok niet van melder {start} - eerst het al bekende Dinamo-blok {bb} nog eens proberen voordat de volledige blokproef start.");
                        await StuurNaarBlok(bb, r, _ins.Verkensnelheid);
                        var eindHerbevestiging = _klok.Nu + TimeSpan.FromSeconds(_ins.BlokproefKortSeconden);
                        while (_klok.Nu < eindHerbevestiging)
                        {
                            await Wacht(Poll);
                            if (_monitor.Bijwerken().Count > 0) { bevestigd = true; break; }
                        }
                        if (bevestigd)
                        {
                            _hw.ZetLocSnelheid(_ins.LocAdres, 0, r == Richting.Vooruit, bb, _ins.LocStappen);
                            await StopLoc();
                            await Nastellen(start, r);
                        }
                        else
                        {
                            await StuurNaarBlok(bb, r, 0);
                        }
                    }
                    if (!bevestigd)
                    {
                        _log.Waarschuwing($"De loc vertrok niet van melder {start}. Blokkoppeling wordt gecontroleerd.");
                        _kaart.Melder(start).DinamoBlok = null;
                        await BlokZoekenBijStart(start);
                    }
                    _monitor.Bijwerken();
                    await StuurSnelheid(r, snelheid);
                    laatsteWijziging = _klok.Nu;
                    onderbroken = true;
                    continue;
                }
                if (doel.IsNavigatie)
                {
                    if (doelInzicht && bezet.Contains(doel.DoelMelder!.Value))
                    {
                        // Doel wel bereikt maar het einde (stootjuk) laat hem niet verder:
                        // de loc staat zo goed als het kan op de doelmelder.
                        await Nastellen(doel.DoelMelder.Value, r, achter: res.Reeks.Count >= 2 ? res.Reeks[^2] : null);
                        res.Einde = RitEinde.DoelBereikt;
                        return res;
                    }
                    throw new NavigatieFout($"Melder {doel.DoelMelder} werd niet bereikt: al {WachttijdTussenMelders().TotalSeconds:0} s geen nieuwe melder na melder {res.Reeks[^1]}.");
                }
                res.Einde = RitEinde.Doodlopend;
                return res;
            }
        }
    }

    // =====================================================================
    // Kortsluiting
    // =====================================================================

    private async Task<RitResultaat> Kortsluiting(RitResultaat res, string reden)
    {
        int x = res.Reeks[^1];
        _hw.Noodstop();
        _log.Waarschuwing($"KORTSLUITING/ONTSPORING vermoed na melder {x} ({res.Richting.Tekst()}): {reden}. Noodstop.");
        await VolledigeStop();
        res.Einde = RitEinde.Kortsluiting;
        _kortsluitingenTotaal++;
        await HerstelNaKortsluiting(x, res.Richting);
        return res;
    }

    /// <summary>Probeert de loc na een kortsluiting zelf terug te laten rijden naar de
    /// laatste melder. Lukt dat niet, dan wordt de gebruiker gevraagd hem terug te zetten.</summary>
    private async Task HerstelNaKortsluiting(int melder, Richting richtingVanRit)
    {
        var terug = richtingVanRit.Om();
        for (int poging = 1; poging <= 2; poging++)
        {
            await Wacht(TimeSpan.FromSeconds(1));
            _kortsluitingGemeld = false;
            _negeerKortsluitingTot = _klok.Nu + TimeSpan.FromSeconds(12);
            _log.Rijden($"Herstel: loc {terug.Tekst()} terug naar melder {melder} (poging {poging}).");
            // Naar alle blokken: de loc staat op een stuk waarvan het blok onbekend is.
            if (BlokVereist)
            {
                var blokken = _ins.DinamoBlokLijst();
                foreach (var b in blokken) _hw.ZetLocSnelheid(_ins.LocAdres, _ins.Kruipsnelheid, terug == Richting.Vooruit, b, _ins.LocStappen);
                await Wacht(DinamoCyclus * blokken.Count);
            }
            else await StuurSnelheid(terug, _ins.Kruipsnelheid);

            var eind = _klok.Nu + TimeSpan.FromSeconds(10);
            bool terugOpMelder = false;
            while (_klok.Nu < eind)
            {
                await Wacht(Poll);
                _monitor.Bijwerken();
                var bezet = _monitor.Bezet;
                if (bezet.Count == 1 && bezet.Contains(melder)) { terugOpMelder = true; break; }
            }
            await VolledigeStop();
            _kortsluitingGemeld = false;
            if (terugOpMelder || _monitor.IsBezet(melder))
            {
                await Nastellen(melder, terug);
                _log.Info($"Loc staat weer op melder {melder}.");
                return;
            }
        }
        await VraagLocTerugTeZetten(melder, $"Er was een kortsluiting of ontsporing direct na melder {melder} en de loc kon niet zelf terugrijden.");
    }

    /// <summary>Vraagt de gebruiker de loc met de hand op een melder te zetten, en
    /// controleert daarna of die melder echt (alleen) bezet is.</summary>
    private async Task VraagLocTerugTeZetten(int melder, string aanleiding)
    {
        for (int poging = 1; poging <= 3; poging++)
        {
            bool verder = await _vraag(
                "Loc terugzetten",
                $"{aanleiding}\n\nZet de testloc (adres {_ins.LocAdres}) met de hand op melder {melder}, zodat ALLEEN die melder bezet is. Controleer ook of de loc niet ontspoord is.\n\nKlik daarna op OK. (Annuleren stopt de verkenning.)");
            if (!verder) throw new OperationCanceledException("Gebruiker stopte bij het terugzetten van de loc.");
            _kortsluitingGemeld = false;
            if (_hw.KanMelderStatusOpvragen)
            {
                _hw.VraagMelderStatusOp(melder);
                foreach (var m in _monitor.Bezet) _hw.VraagMelderStatusOp(m);
            }
            await Wacht(TimeSpan.FromSeconds(2));
            _monitor.Bijwerken();
            var bezet = _monitor.Bezet;
            if (bezet.Contains(melder))
            {
                if (bezet.Count > 1) _log.Waarschuwing($"Naast melder {melder} zijn ook {Lijst(bezet.Where(m => m != melder))} bezet.");
                _log.Info($"Gebruiker heeft de loc op melder {melder} gezet.");
                return;
            }
            _log.Waarschuwing($"Melder {melder} meldt zich niet bezet (bezet: {Lijst(bezet)}).");
        }
        throw new OperationCanceledException($"Loc kon niet op melder {melder} gezet worden.");
    }

    // =====================================================================
    // Hulp
    // =====================================================================

    private async Task Wacht(TimeSpan duur)
    {
        _token.ThrowIfCancellationRequested();
        await _klok.Wacht(duur, _token);
    }

    private static string Lijst(IEnumerable<int> melders)
    {
        var l = melders.ToList();
        return l.Count == 0 ? "geen" : string.Join(", ", l);
    }
}
