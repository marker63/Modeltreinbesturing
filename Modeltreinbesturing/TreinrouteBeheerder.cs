using Modeltreinbesturing.Model;

namespace Modeltreinbesturing;

/// <summary>
/// Beheert laag 4: de vaste treinroutes. De kernlogica hier is BerekenVolledigPad -
/// die klapt een route (startblok + alleen de keuzes op splitsingen) uit tot de
/// volledige rij blokken, door bij een enkel mogelijk vervolgblok dat automatisch te
/// volgen en alleen bij een echte splitsing de vastgelegde keuze te gebruiken.
/// </summary>
public class TreinrouteBeheerder
{
    public List<Treinroute> Treinroutes { get; } = new();

    private readonly RichtingsverbodBeheerder _richtingsverbodBeheerder;

    public TreinrouteBeheerder(RichtingsverbodBeheerder richtingsverbodBeheerder)
    {
        _richtingsverbodBeheerder = richtingsverbodBeheerder;
    }

    public Treinroute NieuweTreinroute(Blok startblok, string omschrijving)
    {
        var route = new Treinroute { Startblok = startblok, Omschrijving = omschrijving };
        Treinroutes.Add(route);
        return route;
    }

    public void VerwijderTreinroute(Treinroute route) => Treinroutes.Remove(route);

    /// <summary>Ruimt routes/keuzes op die dit blok gebruiken (aangeroepen als het blok verwijderd wordt).</summary>
    public void VergeetBlok(Blok blok)
    {
        Treinroutes.RemoveAll(r => r.Startblok == blok);
        foreach (var route in Treinroutes)
        {
            route.Keuzeblokken.RemoveAll(b => b == blok);
            route.AlternatieveStartblokken.RemoveAll(b => b == blok);
        }
    }

    /// <summary>
    /// Klapt een route uit tot de volledige, aaneengesloten rij blokken. Bij 0 (toegestane)
    /// vervolgblokken stopt het pad (doodlopend/einde van de vastgelegde route, of alle
    /// mogelijke vervolgstappen zijn verboden via een Richtingsverbod). Bij 1 vervolgblok
    /// wordt dat automatisch gevolgd. Bij >1 wordt de eerstvolgende, nog niet gebruikte entry
    /// uit Keuzeblokken gebruikt; ontbreekt die, of is de vastgelegde keuze inmiddels verboden,
    /// dan stopt het pad daar.
    /// </summary>
    /// <summary>Zoekt zelf een pad van 'van' naar 'naar' (kortste, in aantal blokken, via
    /// een simpele BFS) door de relaties, met inachtneming van richtingsverboden - en
    /// bouwt daar direct een kant-en-klare, ad-hoc Treinroute van (Startblok+Keuzeblokken,
    /// zodat BerekenVolledigPad 'm daarna weer feilloos kan uitklappen). Voor het
    /// kijkscherm: sleep een loc naar een willekeurig ander blok en hij zoekt zelf zijn
    /// weg, "afhankelijk van de restricties" - net als automatisch rijden, maar dan met
    /// een concreet doel i.p.v. reactief de eerste vrije afslag kiezen. Retourneert null
    /// als er geen (toegestaan) pad bestaat.</summary>
    public Treinroute? VindPadNaarBlok(Blok van, Blok naar, BlokBeheerder blokBeheerder, Treintype? treintype = null)
    {
        if (van == naar) return null;

        var vorigeBlok = new Dictionary<Blok, Blok?> { [van] = null };
        var wachtrij = new Queue<Blok>();
        wachtrij.Enqueue(van);
        bool gevonden = false;

        while (wachtrij.Count > 0 && !gevonden)
        {
            var huidig = wachtrij.Dequeue();
            var vorige = vorigeBlok[huidig];
            foreach (var volgend in blokBeheerder.VolgendeBlokken(huidig).Where(b => _richtingsverbodBeheerder.IsToegestaan(vorige, huidig, b, treintype)))
            {
                if (vorigeBlok.ContainsKey(volgend)) continue;
                vorigeBlok[volgend] = huidig;
                if (volgend == naar) { gevonden = true; break; }
                wachtrij.Enqueue(volgend);
            }
        }
        if (!vorigeBlok.ContainsKey(naar)) return null; // geen (toegestaan) pad gevonden

        var pad = new List<Blok> { naar };
        var stap = vorigeBlok[naar];
        while (stap != null)
        {
            pad.Insert(0, stap);
            stap = vorigeBlok.TryGetValue(stap, out var v) ? v : null;
        }

        // Keuzeblokken opbouwen door PRECIES dezelfde vertakkingslogica te volgen als
        // BerekenVolledigPad zelf hanteert - alleen op een echte splitsing (>1 toegestane
        // optie) wordt de gekozen vervolgstap vastgelegd, anders zou de Keuzeblokken-index
        // uit de pas gaan lopen met wat BerekenVolledigPad straks zelf weer opnieuw bepaalt.
        var route = new Treinroute { Startblok = pad[0], Treintype = treintype };
        Blok? vorigeStap = null;
        for (int i = 0; i < pad.Count - 1; i++)
        {
            var huidigStap = pad[i];
            var volgendeStap = pad[i + 1];
            var opties = blokBeheerder.VolgendeBlokken(huidigStap).Where(b => _richtingsverbodBeheerder.IsToegestaan(vorigeStap, huidigStap, b, treintype)).ToList();
            if (opties.Count > 1) route.Keuzeblokken.Add(volgendeStap);
            vorigeStap = huidigStap;
        }
        route.Bestemmingsblok = naar;
        return route;
    }

    /// <summary>werkelijkStartblok: optioneel, voor Koploper's "alternatieve startblokken"
    /// - staat de gekozen trein op zo'n alternatief blok i.p.v. op route.Startblok zelf, dan
    /// geeft de aanroeper dat blok hier door en wordt het pad daarvandaan opgebouwd i.p.v.
    /// vanaf route.Startblok. Null (de standaard) betekent: gewoon route.Startblok, zoals
    /// voorheen.</summary>
    public List<Blok> BerekenVolledigPad(Treinroute route, BlokBeheerder blokBeheerder, Blok? werkelijkStartblok = null)
    {
        var start = werkelijkStartblok ?? route.Startblok;
        var pad = new List<Blok> { start };
        Blok? vorige = null;
        var huidige = start;
        int keuzeIndex = 0;
        var bezocht = new HashSet<Blok> { start }; // simpele cyclus-bescherming

        while (true)
        {
            var volgende = blokBeheerder.VolgendeBlokken(huidige)
                .Where(b => _richtingsverbodBeheerder.IsToegestaan(vorige, huidige, b, route.Treintype))
                .ToList();
            Blok stap;

            if (volgende.Count == 0) break;

            if (volgende.Count == 1)
            {
                stap = volgende[0];
            }
            else
            {
                if (keuzeIndex >= route.Keuzeblokken.Count) break;
                stap = route.Keuzeblokken[keuzeIndex];
                keuzeIndex++;
                if (!volgende.Contains(stap)) break;
            }

            if (!bezocht.Add(stap)) break;
            pad.Add(stap);
            vorige = huidige;
            huidige = stap;
        }

        return pad;
    }

    /// <summary>
    /// Voor het interactief opbouwen van een route: waar staat de route nu, en welke
    /// (toegestane) vervolgblokken zijn er? Compleet=true betekent doodlopend/alles verboden.
    /// </summary>
    public (Blok Huidig, List<Blok> Mogelijkheden, bool Compleet) VolgendeKeuze(Treinroute route, BlokBeheerder blokBeheerder)
    {
        var pad = BerekenVolledigPad(route, blokBeheerder);
        var laatste = pad[^1];
        var vorige = pad.Count >= 2 ? pad[^2] : (Blok?)null;
        var volgende = blokBeheerder.VolgendeBlokken(laatste)
            .Where(b => _richtingsverbodBeheerder.IsToegestaan(vorige, laatste, b, route.Treintype))
            .ToList();
        return (laatste, volgende, volgende.Count == 0);
    }

    /// <summary>Wist alle treinroutes - gebruikt bij "Nieuw project".</summary>
    public void Reset() => Treinroutes.Clear();

    /// <summary>Doorgeefmethode naar de eigen (private) RichtingsverbodBeheerder - zodat andere
    /// vensters (zoals TreinrouteWindow's automatisch-rijden-logica) een overgang kunnen
    /// toetsen zonder dat ze zelf ook een aparte RichtingsverbodBeheerder-referentie nodig hebben.</summary>
    public bool IsOvergangToegestaan(Blok? vorige, Blok van, Blok naar, Treintype? treintype = null) => _richtingsverbodBeheerder.IsToegestaan(vorige, van, naar, treintype);
}
