using Modeltreinbesturing.Hardware;

namespace Baanverkenner.Kern;

/// <summary>BUG #195 (Marco 17-10 18:06, optie 6 'Rondrijden'): tweede fase. Alle melders met hun vervolgen zijn bekend; nu krijgt elke melder zijn Dinamo-blok.
/// Tijdens de eerste fase gingen alle snelheids- en richtingscommando's naar alle blokken, zonder bloknummers vast te leggen.</summary>
public partial class BaanVerkenner
{
    private bool _andereTrajectGemeld;   // BUG #287

    private async Task RondritBlokkenZoeken()
    {
        _andereTrajectGemeld = false;
        var zonder = _kaart.Melders.Where(m => m.DinamoBlok is null).Select(m => m.Nummer).OrderBy(x => x).ToList();
        _log.Stap($"Alle melders met hun vervolgen zijn bekend ({_kaart.Melders.Count} melders, {_kaart.Overgangen.Count(o => o.AantalKeer > 0)} overgangen, {_kaart.Wissels.Count} wissels, {_kaart.Kopsporen.Count} doodlopende einden). Nu worden de Dinamo-blokken gezocht voor melder {string.Join(", ", zonder)}.");
        _rondritBlokfase = true;
        _fase = "Blokken zoeken";
        MeldVoortgang();
        int mislukt = 0;
        var herkansing = new Dictionary<int, int>();   // BUG #282
        bool laatsteRonde = false;                     // BUG #292
        while (true)
        {
            var open = _kaart.Melders.Where(m => m.DinamoBlok is null && ProefMogelijk(m.Nummer)).Select(m => m.Nummer).ToList();
            // BUG #292 (simulatie keerlus 21-10): melder 8 lukte vijf keer niet via 129 -> 8 en werd opgegeven, terwijl hij eerder via 24 -> 8 wel bereikbaar was. Zijn alle pogingen op, dan
            // krijgen de melders van dit traject zonder blok één laatste ronde vanaf de plek waar de loc nu staat (vaak een andere weg).
            if (open.Count == 0 && !laatsteRonde)
            {
                laatsteRonde = true;
                var hier = HuidigePlek();
                var bereik = hier is int h0 ? Afstanden(_kaart, h0) : new Dictionary<int, int>();
                var nogmaals = _kaart.Melders.Where(m => m.DinamoBlok is null && (bereik.Count <= 1 || bereik.ContainsKey(m.Nummer))).Select(m => m.Nummer).ToList();
                if (nogmaals.Count > 0)
                {
                    foreach (var m in nogmaals) { _proefPogingen[m] = MaxProefPogingen - 1; herkansing[m] = 3; }
                    _log.Info($"Nog geen blok voor melder {Lijst(nogmaals)}: nog één ronde vanaf melder {hier} (BUG #292).");
                    continue;
                }
            }
            if (open.Count == 0) break;
            var nu = HuidigePlek();
            var afstand = nu is int n ? Afstanden(_kaart, n) : new Dictionary<int, int>();
            // BUG #287 (Marco 21-10 16:42: "je wil naar melder 4 rijden maar die zit totaal niet op dit traject"): bij een tweede, los traject (de pendelbaan) wilde de blokfase ook de melders
            // van het eerste traject bereiken. Alleen melders die vanaf de plek van de loc via bekende overgangen bereikbaar zijn, komen aan de beurt; de rest ligt op een ander traject.
            if (nu is int && afstand.Count > 1)
            {
                var elders = open.Where(m => !afstand.ContainsKey(m)).ToList();
                if (elders.Count > 0 && !_andereTrajectGemeld)
                {
                    _andereTrajectGemeld = true;
                    _log.Info($"Melder {Lijst(elders)} ligt niet op het traject waar de loc nu rijdt (geen verbinding via bekende overgangen): die blokken worden nu niet gezocht. Zet de loc later op dat traject en hervat (BUG #287).");
                }
                open = open.Where(afstand.ContainsKey).ToList();
                if (open.Count == 0) break;
            }
            int doel = open.OrderBy(m => afstand.TryGetValue(m, out var d) ? d : 999).ThenBy(m => m).First();
            await PauzeMoment();
            _bezig = $"blok zoeken voor melder {doel}";
            MeldVoortgang();
            _proefPogingen[doel] = _proefPogingen.GetValueOrDefault(doel) + 1;
            _monitor.Bijwerken();
            // WAT: staat de loc op de grens van twee melders, dan eerst naar één van die twee kruipen (de melder die het dichtst bij het doel ligt).
            // WAAROM (BUG #375, simulatie met stoppen en hervatten 04-11, blokfase 15:34-15:40): de loc stond op de grens van 24 en 129. Elke navigatie zag de andere van die twee meteen
            // als "onverwacht" en na drie herkansingen per melder (#282) bleven 4, 11, 17 en 25 zonder blok: verkenning niet voltooid.
            if (_monitor.Bezet.Count == 2 && HuidigeAlarmBlokken().Count == 0)
            {
                var twee = _monitor.Bezet.ToList();
                int Afst(int m) => Afstanden(_kaart, m).TryGetValue(doel, out var d) ? d : 999;
                int naar = twee.OrderBy(Afst).ThenBy(m => m).First(), ander = twee.First(m => m != naar);
                var r375 = _kaart.Ritten.LastOrDefault()?.Richting ?? Richting.Vooruit;
                _log.Info($"Blok zoeken: de loc staat op de grens van melder {ander} en {naar}; eerst naar melder {naar} (BUG #375, nog niet getest op de baan).");
                try { await Nastellen(naar, r375, achter: ander); } catch (NavigatieFout) { }
                _monitor.Bijwerken();
            }
            var bezetVoor = _monitor.Bezet.ToHashSet();
            try
            {
                var r = _kaart.Overgangen.Where(o => o.Naar == doel && o.AantalKeer > 0).Select(o => (Richting?)o.Richting).FirstOrDefault()
                        ?? _kaart.Overgangen.Where(o => o.Van == doel && o.AantalKeer > 0).Select(o => (Richting?)o.Richting.Om()).FirstOrDefault() ?? Richting.Vooruit;
                await NaarPlekMelder(doel, Configuratie.Basis);
                await ZetConfiguratie(Configuratie.Basis, spaarOnderLoc: true);
                await Nastellen(doel, r);
                _monitor.Bijwerken();
                if (!(_monitor.Bezet.Count == 1 && _monitor.Bezet.Contains(doel)))
                {
                    _log.Waarschuwing($"Blok zoeken voor melder {doel} uitgesteld: de loc staat niet alleen op die melder (bezet: {Lijst(_monitor.Bezet)}).");
                    mislukt++;
                    continue;
                }
                if (await BlokZoekenBijStart(doel)) { _log.Vondst($"Melder {doel} hoort bij Dinamo-blok {BlokVan(doel)}."); laatsteRonde = false; }   // BUG #292: na voortgang mag er weer een laatste ronde komen
                else mislukt++;
            }
            catch (NavigatieFout ex)
            {
                mislukt++;
                _log.Waarschuwing($"Blok zoeken voor melder {doel} lukte niet: {ex.Message}");
                // BUG #282 (autolog 21-10 15:32:47-15:33:45): de loc stond op de grens van 24 en 8; elke navigatie zag de andere van die twee meteen als 'onverwacht' en binnen
                // een minuut waren 9 melders opgegeven ("Geen Dinamo-blok gevonden voor melder 4, 17, 20, 25, 26, 27, 28, 29, 30"). Was de 'onverwachte' melder al bezet vóór de rit, dan
                // zegt dat niets over de weg: die poging telt niet (hoogstens 3 keer per melder).
                if (ex.Onverwacht is int ov && bezetVoor.Contains(ov) && herkansing.GetValueOrDefault(doel) < 3)
                {
                    herkansing[doel] = herkansing.GetValueOrDefault(doel) + 1;
                    _proefPogingen[doel] = Math.Max(0, _proefPogingen.GetValueOrDefault(doel) - 1);
                    _log.Info($"Melder {ov} was al bezet voordat de rit begon (de loc staat op {Lijst(bezetVoor)}): deze poging voor melder {doel} telt niet mee (BUG #282).");
                }
            }
            BewaarNu();
        }
        BlokkenUitKortsluiting();   // BUG #370
        var nog = _kaart.Melders.Where(m => m.DinamoBlok is null).Select(m => m.Nummer).OrderBy(x => x).ToList();
        _log.Info(nog.Count == 0 ? "Alle melders hebben nu een Dinamo-blok." : $"Geen Dinamo-blok gevonden voor melder {string.Join(", ", nog)}.");
    }
}
