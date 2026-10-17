using Modeltreinbesturing.Hardware;

namespace Baanverkenner.Kern;

/// <summary>BUG #195 (Marco 17-10 18:06, optie 6 'Rondrijden'): tweede fase. Alle melders met hun vervolgen zijn bekend; nu krijgt elke melder zijn Dinamo-blok.
/// Tijdens de eerste fase gingen alle snelheids- en richtingscommando's naar alle blokken, zonder bloknummers vast te leggen.</summary>
public partial class BaanVerkenner
{
    private async Task RondritBlokkenZoeken()
    {
        var zonder = _kaart.Melders.Where(m => m.DinamoBlok is null).Select(m => m.Nummer).OrderBy(x => x).ToList();
        _log.Stap($"Alle melders met hun vervolgen zijn bekend ({_kaart.Melders.Count} melders, {_kaart.Overgangen.Count(o => o.AantalKeer > 0)} overgangen, {_kaart.Wissels.Count} wissels, {_kaart.Kopsporen.Count} doodlopende einden). Nu worden de Dinamo-blokken gezocht voor melder {string.Join(", ", zonder)}.");
        _rondritBlokfase = true;
        _fase = "Blokken zoeken";
        MeldVoortgang();
        int mislukt = 0;
        while (true)
        {
            var open = _kaart.Melders.Where(m => m.DinamoBlok is null && ProefMogelijk(m.Nummer)).Select(m => m.Nummer).ToList();
            if (open.Count == 0) break;
            var nu = HuidigePlek();
            var afstand = nu is int n ? Afstanden(_kaart, n) : new Dictionary<int, int>();
            int doel = open.OrderBy(m => afstand.TryGetValue(m, out var d) ? d : 999).ThenBy(m => m).First();
            await PauzeMoment();
            _bezig = $"blok zoeken voor melder {doel}";
            MeldVoortgang();
            _proefPogingen[doel] = _proefPogingen.GetValueOrDefault(doel) + 1;
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
            }
            BewaarNu();
        }
        var nog = _kaart.Melders.Where(m => m.DinamoBlok is null).Select(m => m.Nummer).OrderBy(x => x).ToList();
        _log.Info(nog.Count == 0 ? "Alle melders hebben nu een Dinamo-blok." : $"Geen Dinamo-blok gevonden voor melder {string.Join(", ", nog)}.");
    }
}
