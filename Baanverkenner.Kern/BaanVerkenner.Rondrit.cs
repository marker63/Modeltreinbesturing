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
        while (true)
        {
            var open = _kaart.Melders.Where(m => m.DinamoBlok is null && ProefMogelijk(m.Nummer)).Select(m => m.Nummer).ToList();
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
                if (await BlokZoekenBijStart(doel)) _log.Vondst($"Melder {doel} hoort bij Dinamo-blok {BlokVan(doel)}.");
                else mislukt++;
            }
            catch (NavigatieFout ex)
            {
                mislukt++;
                _log.Waarschuwing($"Blok zoeken voor melder {doel} lukte niet: {ex.Message}");
                // BUG #282 (autolog 21-10 15:32:47-15:33:45): de loc stond op 24 en 8 (langer dan melder 8); elke navigatie zag de andere van die twee meteen als 'onverwacht' en binnen
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
        var nog = _kaart.Melders.Where(m => m.DinamoBlok is null).Select(m => m.Nummer).OrderBy(x => x).ToList();
        _log.Info(nog.Count == 0 ? "Alle melders hebben nu een Dinamo-blok." : $"Geen Dinamo-blok gevonden voor melder {string.Join(", ", nog)}.");
    }
}
