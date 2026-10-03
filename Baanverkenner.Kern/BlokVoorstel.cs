namespace Baanverkenner.Kern;

/// <summary>Stelt blokken voor: aaneengesloten melders (secties) ZONDER wissel ertussen en
/// met hetzelfde Dinamo-blok (of allebei zonder blokinformatie) worden samen één blok. Dit
/// is een voorstel - bij het importeren in Modeltreinbesturing kan het nog aangepast worden.</summary>
public static class BlokVoorstel
{
    public static List<VoorgesteldBlok> Bereken(Baankaart kaart)
    {
        var melders = kaart.Melders.Select(m => m.Nummer).ToList();
        var blokVan = kaart.Melders.ToDictionary(m => m.Nummer, m => m.DinamoBlok);

        var buren = melders.ToDictionary(m => m, _ => new HashSet<int>());
        foreach (var o in kaart.Overgangen)
        {
            if (!buren.ContainsKey(o.Van) || !buren.ContainsKey(o.Naar)) continue;
            buren[o.Van].Add(o.Naar);
            buren[o.Naar].Add(o.Van);
        }

        var viaWissel = new HashSet<(int, int)>();
        void Markeer(int a, int? b)
        {
            if (b is not int bb) return;
            viaWissel.Add((Math.Min(a, bb), Math.Max(a, bb)));
        }
        foreach (var w in kaart.Wissels)
            foreach (var obs in w.Waarnemingen)
            {
                Markeer(obs.NaMelder, obs.VolgendeBijRechtdoor);
                Markeer(obs.NaMelder, obs.VolgendeBijAfbuigend);
            }

        bool Samen(int a, int b) =>
            !viaWissel.Contains((Math.Min(a, b), Math.Max(a, b)))
            && Equals(blokVan.GetValueOrDefault(a), blokVan.GetValueOrDefault(b));

        var groepVan = new Dictionary<int, int>();
        var groepen = new List<List<int>>();
        foreach (var start in melders)
        {
            if (groepVan.ContainsKey(start)) continue;
            var groep = new List<int>();
            var rij = new Queue<int>();
            rij.Enqueue(start);
            groepVan[start] = groepen.Count;
            while (rij.Count > 0)
            {
                var m = rij.Dequeue();
                groep.Add(m);
                foreach (var n in buren[m])
                    if (!groepVan.ContainsKey(n) && Samen(m, n))
                    {
                        groepVan[n] = groepen.Count;
                        rij.Enqueue(n);
                    }
            }
            groepen.Add(OrdenAlsKetting(groep, buren));
        }

        var res = new List<VoorgesteldBlok>();
        for (int i = 0; i < groepen.Count; i++)
        {
            var g = groepen[i];
            var b = new VoorgesteldBlok
            {
                Nummer = i + 1,
                Melders = g,
                DinamoBlok = blokVan.GetValueOrDefault(g[0])
            };
            foreach (var m in g)
                foreach (var n in buren[m])
                    if (groepVan[n] != i && !b.Buren.Contains(groepVan[n] + 1)) b.Buren.Add(groepVan[n] + 1);
            b.Buren.Sort();
            res.Add(b);
        }
        return res;
    }

    /// <summary>Zet de melders van één groep in rijvolgorde (vanaf een uiteinde).</summary>
    private static List<int> OrdenAlsKetting(List<int> groep, Dictionary<int, HashSet<int>> buren)
    {
        var set = groep.ToHashSet();
        int start = groep.OrderBy(m => buren[m].Count(n => set.Contains(n))).ThenBy(m => m).First();
        var res = new List<int> { start };
        var gezien = new HashSet<int> { start };
        while (true)
        {
            var volgende = buren[res[^1]].Where(n => set.Contains(n) && !gezien.Contains(n)).OrderBy(n => n).FirstOrDefault();
            if (volgende == 0) break;
            res.Add(volgende);
            gezien.Add(volgende);
        }
        res.AddRange(groep.Where(m => !gezien.Contains(m)).OrderBy(m => m));
        return res;
    }
}
