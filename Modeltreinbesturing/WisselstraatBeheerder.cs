using Modeltreinbesturing.Model;

namespace Modeltreinbesturing;

/// <summary>
/// Beheert laag 3: de wisselstraten. Een wisselstraat mag alleen worden aangemaakt
/// tussen een blokpaar dat al een BlokRelatie heeft in laag 1 - de wisselstraat
/// beschrijft HOE er gereden wordt, niet OF dat mag.
/// </summary>
public class WisselstraatBeheerder
{
    public List<Wisselstraat> Wisselstraten { get; } = new();

    /// <summary>
    /// Maakt een nieuwe wisselstraat aan tussen van en naar, als daar een relatie voor bestaat.
    /// Retourneert null als die relatie ontbreekt (dan mag er geen wisselstraat komen).
    /// Volgnummer wordt automatisch 1 hoger dan de hoogste bestaande voor dit blokpaar.
    /// </summary>
    public Wisselstraat? NieuwWisselstraat(Blok van, Blok naar, BlokBeheerder blokBeheerder)
    {
        bool relatieBestaat = blokBeheerder.Relaties.Any(r => r.Van == van && r.Naar == naar);
        if (!relatieBestaat) return null;

        var bestaande = Wisselstraten.Where(w => w.Van == van && w.Naar == naar).ToList();
        int volgendVolgnummer = bestaande.Count == 0 ? 1 : bestaande.Max(w => w.Volgnummer) + 1;

        var wisselstraat = new Wisselstraat { Van = van, Naar = naar, Volgnummer = volgendVolgnummer };
        Wisselstraten.Add(wisselstraat);
        return wisselstraat;
    }

    public void VerwijderWisselstraat(Wisselstraat wisselstraat) => Wisselstraten.Remove(wisselstraat);

    /// <summary>Koploper: "Bij een volgende wisselstraat die bijna identiek is kun je de
    /// vorige wisselstraat kopiëren en dan slechts een enkele wissel en sein aanpassen" -
    /// maakt een nieuwe, ALTERNATIEVE wisselstraat tussen HETZELFDE blokpaar (met een eigen,
    /// automatisch volgende Volgnummer), met dezelfde wissels+standen en lijnen als
    /// startpunt om vandaaruit aan te passen (bijv. via dubbelklikken op een wissel in de
    /// lijst om de gewenste stand om te draaien). Bewust hetzelfde blokpaar - kopiëren naar
    /// een ANDER blokpaar zou de verkeerde, fysiek elders liggende wissels meenemen.</summary>
    public Wisselstraat KopieerWisselstraat(Wisselstraat bron)
    {
        var bestaande = Wisselstraten.Where(w => w.Van == bron.Van && w.Naar == bron.Naar).ToList();
        int volgendVolgnummer = bestaande.Count == 0 ? 1 : bestaande.Max(w => w.Volgnummer) + 1;

        var kopie = new Wisselstraat
        {
            Van = bron.Van,
            Naar = bron.Naar,
            Volgnummer = volgendVolgnummer,
            Wissels = bron.Wissels.Select(w => new WisselInWisselstraat { Wissel = w.Wissel, GewensteStand = w.GewensteStand }).ToList(),
            Kruiswisselstanden = bron.Kruiswisselstanden.Select(k => new KruiswisselInWisselstraat { Kruiswissel = k.Kruiswissel, StandAdres = k.StandAdres, StandAdres2 = k.StandAdres2 }).ToList(),
            Lijnen = bron.Lijnen.ToList()
        };
        Wisselstraten.Add(kopie);
        return kopie;
    }

    /// <summary>Ruimt alle wisselstraten op die dit blok gebruiken (aangeroepen als het blok zelf verwijderd wordt).</summary>
    public void VergeetBlok(Blok blok) => Wisselstraten.RemoveAll(w => w.Van == blok || w.Naar == blok);

    /// <summary>Wissel aan/uit klikken in een wisselstraat - legt de stand vast zoals deze
    /// op dit moment in het baanontwerp getekend staat, precies zoals de handleiding beschrijft.</summary>
    public void ToggleWissel(Wisselstraat wisselstraat, Wissel wissel)
    {
        var bestaand = wisselstraat.Wissels.FirstOrDefault(w => w.Wissel == wissel);
        if (bestaand != null)
            wisselstraat.Wissels.Remove(bestaand);
        else
            wisselstraat.Wissels.Add(new WisselInWisselstraat { Wissel = wissel, GewensteStand = wissel.Stand });
    }

    public void ToggleLijn(Wisselstraat wisselstraat, Lijn lijn)
    {
        if (!wisselstraat.Lijnen.Remove(lijn))
            wisselstraat.Lijnen.Add(lijn);
    }

    /// <summary>Zelfde principe als ToggleWissel hierboven, nu voor een Kruiswissel/Engelse
    /// wissel - legt de gewenste stand van BEIDE motoren onafhankelijk vast zoals die op dit
    /// moment bekend staan (zie Model.Kruiswissel.StandAdres/StandAdres2 - deze twee motoren
    /// hebben GEEN vaste onderlinge relatie meer, dus worden hier apart overgenomen in plaats
    /// van van elkaar afgeleid).</summary>
    public void ToggleKruiswissel(Wisselstraat wisselstraat, Kruiswissel kruiswissel)
    {
        var bestaand = wisselstraat.Kruiswisselstanden.FirstOrDefault(k => k.Kruiswissel == kruiswissel);
        if (bestaand != null)
            wisselstraat.Kruiswisselstanden.Remove(bestaand);
        else
            wisselstraat.Kruiswisselstanden.Add(new KruiswisselInWisselstraat { Kruiswissel = kruiswissel, StandAdres = kruiswissel.StandAdres, StandAdres2 = kruiswissel.StandAdres2 });
    }

    /// <summary>Ruimt dubbele wisselstraten voor hetzelfde blokpaar op: houdt per (Van,Naar)
    /// de meest complete (de meeste wissels+lijnen) over en verwijdert de rest, en hernummert
    /// de overgebleven Volgnummers daarna netjes op volgorde. Retourneert het aantal verwijderd.</summary>
    public int VerwijderDubbelen()
    {
        var groepen = Wisselstraten.GroupBy(w => (w.Van, w.Naar)).Where(g => g.Count() > 1).ToList();
        int verwijderd = 0;

        foreach (var groep in groepen)
        {
            var behouden = groep.OrderByDescending(w => w.Wissels.Count + w.Kruiswisselstanden.Count + w.Lijnen.Count).ThenBy(w => w.Volgnummer).First();
            foreach (var overbodig in groep.Where(w => w != behouden).ToList())
            {
                Wisselstraten.Remove(overbodig);
                verwijderd++;
            }
        }

        foreach (var groep in Wisselstraten.GroupBy(w => (w.Van, w.Naar)))
        {
            int volgnummer = 1;
            foreach (var w in groep.OrderBy(w => w.Volgnummer))
                w.Volgnummer = volgnummer++;
        }

        return verwijderd;
    }

    /// <summary>Alle wisselstraten tussen een blokpaar, op volgorde van volgnummer -
    /// zo probeert automatisch rijden ze straks in de juiste volgorde.</summary>
    public IEnumerable<Wisselstraat> WisselstratenTussen(Blok van, Blok naar) =>
        Wisselstraten.Where(w => w.Van == van && w.Naar == naar).OrderBy(w => w.Volgnummer);

    /// <summary>Wist alle wisselstraten - gebruikt bij "Nieuw project".</summary>
    public void Reset() => Wisselstraten.Clear();
}
