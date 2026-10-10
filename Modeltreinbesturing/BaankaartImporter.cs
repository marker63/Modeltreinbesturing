using Baanverkenner.Kern;
using Modeltreinbesturing.Model;

namespace Modeltreinbesturing;

/// <summary>
/// BUG #56: laadt een door de Baanverkenner geleerde baankaart in het project: blokken (met hun
/// melders), blokrelaties en de rijrichting per relatie. Bedoeld voor een NIEUWE baan, maar
/// veilig op een bestaand project:
///  - bestaande blokken, melders, relaties en reeds vastgelegde rijrichtingen worden NOOIT
///    gewijzigd of overschreven; er wordt alleen toegevoegd wat ontbreekt;
///  - een melder die al in een ander blok van het project zit (overloop-melder zoals 24 bij
///    blok 3/4/7) wordt niet nog eens toegevoegd en maakt geen eigen blok;
///  - melders zonder bekend Dinamo-blok worden niet geïmporteerd (wel gemeld);
///  - alles is te herstellen met <see cref="Momentopname"/> (de aanroeper vraagt eerst bevestiging).
/// Conventie (BLOKKENSCHEMA.md / BUG #38): Blok.Nummer = Dinamo-blok.
/// Niet gedaan (bewust): wissels tekenen, dienstregeling/routes aanmaken - die vragen keuzes van de gebruiker.
/// </summary>
public class BaankaartImporter
{
    public class Resultaat
    {
        public List<Blok> NieuweBlokken { get; } = new();
        public List<(Blok Blok, int Melder)> ToegevoegdeMelders { get; } = new();
        public List<BlokRelatie> NieuweRelaties { get; } = new();
        public int IngevuldeRichtingen { get; set; }
        public List<BaanControleMelding> Meldingen { get; } = new();
        public bool IsLeeg => NieuweBlokken.Count == 0 && ToegevoegdeMelders.Count == 0 && NieuweRelaties.Count == 0 && IngevuldeRichtingen == 0;
    }

    /// <summary>true (standaard): voor elke gereden overgang ook de relatie in de gereden richting,
    /// dus een lijn met kopsporen kan heen én terug. false: alleen overgangen die de testloc
    /// "vooruit" reed, plus de weg terug uit een kopspoor.</summary>
    public bool BeideRichtingen { get; set; } = true;

    /// <summary>Wat nodig is om een import ongedaan te maken.</summary>
    public class Momentopname
    {
        private List<Blok> _blokken = new();
        private List<BlokRelatie> _relaties = new();
        private Dictionary<Blok, List<Bezetmeldpunt>> _meldpunten = new();
        private Dictionary<Blok, List<RichtingsBezetmelding>> _richtingsmeldingen = new();
        private Dictionary<BlokRelatie, bool?> _richting = new();

        public static Momentopname Maak(BlokBeheerder bb)
        {
            var m = new Momentopname { _blokken = bb.Blokken.ToList(), _relaties = bb.Relaties.ToList() };
            foreach (var b in bb.Blokken)
            {
                m._meldpunten[b] = b.Bezetmeldpunten.ToList();
                m._richtingsmeldingen[b] = b.RichtingsBezetmeldingen.ToList();
            }
            foreach (var r in bb.Relaties) m._richting[r] = r.RijrichtingVooruit;
            return m;
        }

        public void Herstel(BlokBeheerder bb)
        {
            bb.Blokken.Clear(); bb.Blokken.AddRange(_blokken);
            bb.Relaties.Clear(); bb.Relaties.AddRange(_relaties);
            foreach (var b in _blokken)
            {
                b.Bezetmeldpunten = _meldpunten[b].ToList();
                b.RichtingsBezetmeldingen = _richtingsmeldingen[b].ToList();
            }
            foreach (var r in _relaties) r.RijrichtingVooruit = _richting[r];
        }
    }

    public Resultaat Importeer(Baankaart kaart, BlokBeheerder bb)
    {
        var res = new Resultaat();

        if (!kaart.Voltooid)
            res.Meldingen.Add(new BaanControleMelding("Waarschuwing", "Deze baankaart komt van een NIET voltooide verkenning: alleen wat al gevonden is wordt geïmporteerd. Importeer later opnieuw na een voltooide verkenning (al toegevoegde onderdelen worden dan niet dubbel aangemaakt)."));

        // ---- melder -> Dinamo-blok ----
        var dinamoVanMelder = new Dictionary<int, int>();
        foreach (var m in kaart.Melders.OrderBy(m => m.Nummer))
        {
            if (m.DinamoBlok is int d && d > 0) dinamoVanMelder[m.Nummer] = d;
            else res.Meldingen.Add(new BaanControleMelding("Waarschuwing", $"Melder {m.Nummer} heeft geen bekend Dinamo-blok (de verkenner kon het blok niet vaststellen) - niet geïmporteerd."));
        }

        // ---- welke melders zitten al in het project ----
        Dictionary<int, List<Blok>> ProjectBlokkenPerMelder()
        {
            var d = new Dictionary<int, List<Blok>>();
            foreach (var b in bb.Blokken)
                foreach (var p in b.Bezetmeldpunten)
                {
                    if (p.MeldernNummer <= 0) continue;
                    if (!d.TryGetValue(p.MeldernNummer, out var l)) d[p.MeldernNummer] = l = new List<Blok>();
                    if (!l.Contains(b)) l.Add(b);
                }
            return d;
        }
        var bestaand = ProjectBlokkenPerMelder();

        // ---- groepen per Dinamo-blok ----
        var groepen = dinamoVanMelder.GroupBy(kv => kv.Value).OrderBy(g => g.Key);
        // Een echt kopspoor (stootjuk) is een melder die in één richting doodloopt EN maar één buurmelder
        // heeft (alleen waar je vandaan kwam). Een melder die in één richting doodloopt maar in de andere
        // richting wel naar meerdere melders gaat (bijv. 17 en 28 bij de wissels) is GEEN kopspoor.
        int Buren(int melder) => kaart.Overgangen
            .Where(o => o.Van == melder || o.Naar == melder)
            .Select(o => o.Van == melder ? o.Naar : o.Van).Where(x => x != melder).Distinct().Count();
        var kopsporenMelders = kaart.Kopsporen.Select(k => k.Melder).Where(m => Buren(m) <= 1).ToHashSet();
        foreach (var k in kaart.Kopsporen.Where(k => Buren(k.Melder) > 1).Select(k => k.Melder).Distinct())
            res.Meldingen.Add(new BaanControleMelding("Info", $"Melder {k} liep in één richting dood maar heeft meerdere buurmelders - dus geen kopspoor (blok blijft Normaal)."));
        foreach (var g in groepen)
        {
            int nummer = g.Key;
            var melders = Volgorde(g.Select(kv => kv.Key).ToList(), kaart);
            var nieuw = melders.Where(m => !bestaand.ContainsKey(m)).ToList();
            var bezet = melders.Where(m => bestaand.ContainsKey(m)).ToList();
            var bestaandBlok = bb.Blokken.FirstOrDefault(b => b.Nummer == nummer);

            if (nieuw.Count == 0)
            {
                res.Meldingen.Add(new BaanControleMelding("Info", $"Dinamo-blok {nummer}: alle melders ({string.Join(", ", melders)}) zitten al in het project (blok {string.Join("/", bezet.SelectMany(m => bestaand[m]).Distinct().Select(b => b.Nummer))}) - niets aangemaakt."));
                continue;
            }

            if (bestaandBlok is null)
            {
                bool kop = melders.Any(kopsporenMelders.Contains);
                var blok = new Blok { Nummer = nummer, Type = kop ? BlokType.Kopspoor : BlokType.Normaal };
                foreach (var m in nieuw)
                {
                    bool stop = kop && m == nieuw[^1] && kopsporenMelders.Contains(m);
                    blok.Bezetmeldpunten.Add(new Bezetmeldpunt { MeldernNummer = m, Rol = stop ? BezetmeldpuntRol.Stopsectie : BezetmeldpuntRol.Voorblok });
                    res.ToegevoegdeMelders.Add((blok, m));
                }
                bb.Blokken.Add(blok);
                res.NieuweBlokken.Add(blok);
                if (bezet.Count > 0)
                    res.Meldingen.Add(new BaanControleMelding("Info", $"Blok {nummer} aangemaakt met melders {string.Join(", ", nieuw)}. Melder(s) {string.Join(", ", bezet)} horen ook bij Dinamo-blok {nummer}, maar zitten al in een ander blok van het project (overloop) - daar gelaten."));
                else
                    res.Meldingen.Add(new BaanControleMelding("Info", $"Blok {nummer} aangemaakt ({(kop ? "kopspoor, " : "")}melders {string.Join(", ", nieuw)})."));
            }
            else
            {
                foreach (var m in nieuw)
                {
                    bestaandBlok.Bezetmeldpunten.Add(new Bezetmeldpunt { MeldernNummer = m, Rol = BezetmeldpuntRol.Voorblok });
                    res.ToegevoegdeMelders.Add((bestaandBlok, m));
                }
                res.Meldingen.Add(new BaanControleMelding("Info", $"Bestaand blok {nummer}: melder(s) {string.Join(", ", nieuw)} toegevoegd (stond(en) er nog niet in)."));
            }
            bestaand = ProjectBlokkenPerMelder();
        }

        // ---- relaties uit de gereden overgangen ----
        Blok? EenduidigBlok(int melder)
            => bestaand.TryGetValue(melder, out var l) && l.Count == 1 ? l[0] : null;

        var gemeldOnduidelijk = new HashSet<(int, int)>();
        foreach (var o in kaart.Overgangen.OrderBy(o => o.Van).ThenBy(o => o.Naar))
        {
            if (!bestaand.ContainsKey(o.Van) || !bestaand.ContainsKey(o.Naar)) continue; // melder zonder blok: al gemeld
            var a = EenduidigBlok(o.Van);
            var b = EenduidigBlok(o.Naar);
            if (a is null || b is null)
            {
                if (bestaand[o.Van].Intersect(bestaand[o.Naar]).Any()) continue; // binnen hetzelfde blok
                if (gemeldOnduidelijk.Add((o.Van, o.Naar)))
                    res.Meldingen.Add(new BaanControleMelding("Info", $"Overgang melder {o.Van} -> {o.Naar} ({o.Richting.Tekst()}) hoort bij een melder die in meerdere blokken zit (overloop) - relatie niet automatisch aangemaakt; leg die zelf vast (Relaties beheren)."));
                continue;
            }
            if (a == b) continue;
            bool mag = BeideRichtingen || o.Richting == Richting.Vooruit || a.Type == BlokType.Kopspoor;
            if (!mag) continue;
            if (bb.Relaties.Any(r => r.Van == a && r.Naar == b)) continue;
            var rel = bb.LegRelatieVast(a, b);
            if (rel is null) continue;
            res.NieuweRelaties.Add(rel);
            if (o.AantalKeer < 2)
                res.Meldingen.Add(new BaanControleMelding("Info", $"Relatie {a.Nummer} -> {b.Nummer} is maar {o.AantalKeer}x gezien tijdens de verkenning - controleer of die klopt."));
        }

        // ---- meldervolgorde per binnenkomende relatie (RichtingsBezetmelding) ----
        foreach (var blok in res.NieuweBlokken)
        {
            if (blok.Bezetmeldpunten.Count < 2) continue;
            var meldersVanBlok = blok.Bezetmeldpunten.Select(p => p.MeldernNummer).ToList();
            foreach (var rel in bb.Relaties.Where(r => r.Naar == blok))
            {
                var vanMelders = rel.Van.Bezetmeldpunten.Select(p => p.MeldernNummer).ToHashSet();
                int? ingang = null;
                foreach (var o in kaart.Overgangen)
                {
                    if (vanMelders.Contains(o.Van) && meldersVanBlok.Contains(o.Naar)) { ingang = o.Naar; break; }
                    if (vanMelders.Contains(o.Naar) && meldersVanBlok.Contains(o.Van)) { ingang = o.Van; break; }
                }
                if (ingang is null) continue;
                var volgorde = VanafIngang(meldersVanBlok, ingang.Value, kaart);
                if (volgorde.Count != meldersVanBlok.Count) continue; // onvolledig: laat de standaardvolgorde gelden
                if (blok.RichtingsBezetmeldingen.Any(x => x.VanBlok == rel.Van)) continue;
                blok.RichtingsBezetmeldingen.Add(new RichtingsBezetmelding { VanBlok = rel.Van, MeldernNummers = volgorde });
            }
        }

        // ---- rijrichting per relatie (zelfde regels als BUG #53: alleen leeg invullen) ----
        var richting = new RijrichtingImporter().Bepaal(kaart, bb);
        foreach (var v in richting.Invullen) v.Relatie.RijrichtingVooruit = v.Vooruit;
        res.IngevuldeRichtingen = richting.Invullen.Count;
        // De per-relatie "Info"-regels van de richting-importer zijn hier overbodig; waarschuwingen blijven.
        res.Meldingen.AddRange(richting.Meldingen.Where(m => m.Ernst != "Info" || m.Omschrijving.Contains("omgedraaid") || m.Omschrijving.Contains("ondubbelzinnig")));

        // ---- wissels: niet getekend, wel gemeld ----
        foreach (var w in kaart.Wissels.OrderBy(w => w.Adres))
            res.Meldingen.Add(new BaanControleMelding("Info", $"Wisseladres {w.Adres} heeft bij de verkenning een andere route gegeven. Als er nog geen wissel met dit adres op het baanontwerp staat, wordt er na bevestigen een losse wissel onderaan gezet (stand en wisselstraat leg jij zelf vast)."));

        res.Meldingen.Add(new BaanControleMelding("Info", "Na bevestigen krijgt elk nieuw blok een automatische route (zonder vertrektijd); bestaande routes en dienstregeling blijven ongewijzigd. Seinen en stootblokken van nieuwe blokken worden door het hoofdscherm erbij gezet."));
        return res;
    }

    /// <summary>Melders van één Dinamo-blok in rijvolgorde (voorwaartse overgangen binnen de groep).</summary>
    internal static List<int> Volgorde(List<int> melders, Baankaart kaart)
    {
        if (melders.Count <= 1) return melders.OrderBy(m => m).ToList();
        var set = melders.ToHashSet();
        var volgende = melders.ToDictionary(m => m, _ => new List<int>());
        var heeftVoorganger = new HashSet<int>();
        foreach (var o in kaart.Overgangen)
        {
            if (!set.Contains(o.Van) || !set.Contains(o.Naar)) continue;
            int van = o.Richting == Richting.Vooruit ? o.Van : o.Naar;
            int naar = o.Richting == Richting.Vooruit ? o.Naar : o.Van;
            if (!volgende[van].Contains(naar)) { volgende[van].Add(naar); heeftVoorganger.Add(naar); }
        }
        var res = new List<int>();
        var bezocht = new HashSet<int>();
        void Loop(int start)
        {
            var m = start;
            while (bezocht.Add(m))
            {
                res.Add(m);
                var n = volgende[m].Where(x => !bezocht.Contains(x)).OrderBy(x => x).Cast<int?>().FirstOrDefault();
                if (n is null) break;
                m = n.Value;
            }
        }
        foreach (var start in melders.Where(m => !heeftVoorganger.Contains(m)).OrderBy(m => m)) Loop(start);
        foreach (var rest in melders.OrderBy(m => m)) if (!bezocht.Contains(rest)) Loop(rest);
        return res;
    }

    /// <summary>Melders van een blok in de volgorde waarin je ze tegenkomt als je bij <paramref name="ingang"/> binnenkomt.</summary>
    internal static List<int> VanafIngang(List<int> melders, int ingang, Baankaart kaart)
    {
        var set = melders.ToHashSet();
        var buren = melders.ToDictionary(m => m, _ => new List<int>());
        foreach (var o in kaart.Overgangen)
        {
            if (!set.Contains(o.Van) || !set.Contains(o.Naar)) continue;
            if (!buren[o.Van].Contains(o.Naar)) buren[o.Van].Add(o.Naar);
            if (!buren[o.Naar].Contains(o.Van)) buren[o.Naar].Add(o.Van);
        }
        var res = new List<int> { ingang };
        var m2 = ingang;
        while (true)
        {
            var n = buren[m2].Where(x => !res.Contains(x)).OrderBy(x => x).Cast<int?>().FirstOrDefault();
            if (n is null) break;
            res.Add(n.Value);
            m2 = n.Value;
        }
        return res;
    }
}
