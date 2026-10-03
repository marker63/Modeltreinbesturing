using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using Modeltreinbesturing.Model;

namespace Modeltreinbesturing;

/// <summary>
/// Slaat de hele baan (alle vier lagen) op als één JSON-bestand en laadt het weer terug.
/// Gebruikt ReferenceHandler.Preserve zodat een Blok dat op meerdere plekken wordt
/// gebruikt (in de Blokken-lijst, als GekoppeldBlok van een lijn, als Van/Naar van een
/// wisselstraat) na het laden weer hetzelfde object is - net zo cruciaal als in de
/// live-applicatie, anders raken koppelingen na een herstart alsnog los.
/// </summary>
public static class ProjectOpslag
{
    private static readonly JsonSerializerOptions Opties = new()
    {
        WriteIndented = true,
        ReferenceHandler = ReferenceHandler.Preserve
    };

    public static void SlaOp(string pad, BlokBeheerder blokBeheerder, BaanOntwerpBeheerder baanBeheerder, WisselstraatBeheerder wisselstraatBeheerder, TreinrouteBeheerder routeBeheerder, TreintypeBeheerder treintypeBeheerder, RichtingsverbodBeheerder richtingsverbodBeheerder, StopverbodBeheerder stopverbodBeheerder, StopverbodStilstandBeheerder stopverbodStilstandBeheerder, TreinBeheerder treinBeheerder, GebruikersBeheerder gebruikersBeheerder, ActieBeheerder actieBeheerder, SnelheidsMetingBeheerder snelheidsMetingBeheerder, OnderhoudsBeheerder onderhoudsBeheerder, SnelleKlokBeheerder snelleKlokBeheerder)
    {
        var bestand = new ProjectBestand
        {
            Blokken = blokBeheerder.Blokken.ToList(),
            Relaties = blokBeheerder.Relaties.ToList(),
            Symbolen = baanBeheerder.Symbolen.ToList(),
            Wisselstraten = wisselstraatBeheerder.Wisselstraten.ToList(),
            Treinroutes = routeBeheerder.Treinroutes.ToList(),
            Treintypes = treintypeBeheerder.Treintypes.ToList(),
            RangeerTreintype = treintypeBeheerder.RangeerTreintype,
            Treinen = treinBeheerder.Treinen.ToList(),
            Richtingsverboden = richtingsverbodBeheerder.Richtingsverboden.ToList(),
            StopverbodenBlokken = stopverbodBeheerder.Verboden.ToList(),
            StopverbodenStilstand = stopverbodStilstandBeheerder.Verboden.ToList(),
            Gebruikers = gebruikersBeheerder.Gebruikers.ToList(),
            Acties = actieBeheerder.Acties.ToList(),
            OnderhoudsRecords = onderhoudsBeheerder.Records.ToList(),
            SnelleKlok = snelleKlokBeheerder.Instellingen,
            StandaardUitrolSeconden = SimulatieInstellingen.StandaardUitrolSeconden,
            WisselRustpauzeMilliseconden = SimulatieInstellingen.WisselRustpauzeMilliseconden,
            SnelheidsMeting = snelheidsMetingBeheerder.Instellingen,
            BlokPosities = baanBeheerder.AlleBlokPosities()
                .Select(p => new BlokPositieItem { BlokNummer = p.Blok.Nummer, X = p.Positie.X, Y = p.Positie.Y, Tabblad = p.Tabblad })
                .ToList(),
            LocPosities = blokBeheerder.AlleLocPosities()
                .Select(p => new LocPositieItem { BlokNummer = p.Blok.Nummer, DecoderAdres = p.Trein.DecoderAdres, TreinOmschrijving = p.Trein.Omschrijving })
                .ToList()
        };

        string json = JsonSerializer.Serialize(bestand, Opties);
        File.WriteAllText(pad, json);
    }

    public static void Laad(string pad, BlokBeheerder blokBeheerder, BaanOntwerpBeheerder baanBeheerder, WisselstraatBeheerder wisselstraatBeheerder, TreinrouteBeheerder routeBeheerder, TreintypeBeheerder treintypeBeheerder, RichtingsverbodBeheerder richtingsverbodBeheerder, StopverbodBeheerder stopverbodBeheerder, StopverbodStilstandBeheerder stopverbodStilstandBeheerder, TreinBeheerder treinBeheerder, GebruikersBeheerder gebruikersBeheerder, ActieBeheerder actieBeheerder, SnelheidsMetingBeheerder snelheidsMetingBeheerder, OnderhoudsBeheerder onderhoudsBeheerder, SnelleKlokBeheerder snelleKlokBeheerder)
    {
        string json = File.ReadAllText(pad);
        var bestand = JsonSerializer.Deserialize<ProjectBestand>(json, Opties);
        if (bestand is null) return;

        blokBeheerder.Blokken.Clear();
        blokBeheerder.Blokken.AddRange(bestand.Blokken);
        blokBeheerder.Relaties.Clear();
        blokBeheerder.Relaties.AddRange(bestand.Relaties);

        baanBeheerder.Symbolen.Clear();
        baanBeheerder.Symbolen.AddRange(bestand.Symbolen);

        // Oudere baanbestanden (van vóór PuntLijnAnkers/PuntLijnAnkerIndex bestonden) kunnen
        // ankerlijsten hebben die korter zijn dan Punten - hier gelijktrekken zodat niets
        // crasht op een index buiten bereik.
        foreach (var lijn in baanBeheerder.Symbolen.OfType<Model.Lijn>())
        {
            while (lijn.PuntBlokAnkers.Count < lijn.Punten.Count) lijn.PuntBlokAnkers.Add(null);
            while (lijn.PuntWisselAnkers.Count < lijn.Punten.Count) lijn.PuntWisselAnkers.Add(null);
            while (lijn.PuntLijnAnkers.Count < lijn.Punten.Count) lijn.PuntLijnAnkers.Add(null);
            while (lijn.PuntLijnAnkerIndex.Count < lijn.Punten.Count) lijn.PuntLijnAnkerIndex.Add(-1);
        }

        wisselstraatBeheerder.Wisselstraten.Clear();
        wisselstraatBeheerder.Wisselstraten.AddRange(bestand.Wisselstraten);

        routeBeheerder.Treinroutes.Clear();
        routeBeheerder.Treinroutes.AddRange(bestand.Treinroutes);

        treintypeBeheerder.Treintypes.Clear();
        treintypeBeheerder.Treintypes.AddRange(bestand.Treintypes);
        treintypeBeheerder.RangeerTreintype = bestand.RangeerTreintype;
        treintypeBeheerder.VulStandaardTypesIndienLeeg(); // oudere bestanden van vóór treintypes bestonden

        treinBeheerder.Treinen.Clear();
        treinBeheerder.Treinen.AddRange(bestand.Treinen);

        richtingsverbodBeheerder.Richtingsverboden.Clear();
        richtingsverbodBeheerder.Richtingsverboden.AddRange(bestand.Richtingsverboden);

        stopverbodBeheerder.Verboden.Clear();
        stopverbodBeheerder.Verboden.AddRange(bestand.StopverbodenBlokken);

        stopverbodStilstandBeheerder.Verboden.Clear();
        stopverbodStilstandBeheerder.Verboden.AddRange(bestand.StopverbodenStilstand);

        gebruikersBeheerder.Gebruikers.Clear();
        gebruikersBeheerder.Gebruikers.AddRange(bestand.Gebruikers);

        actieBeheerder.Acties.Clear();
        actieBeheerder.Acties.AddRange(bestand.Acties);

        onderhoudsBeheerder.Records.Clear();
        onderhoudsBeheerder.Records.AddRange(bestand.OnderhoudsRecords);

        snelleKlokBeheerder.Instellingen.HuidigeTijd = bestand.SnelleKlok.HuidigeTijd;
        snelleKlokBeheerder.Instellingen.SecondenPerModelMinuut = bestand.SnelleKlok.SecondenPerModelMinuut;
        snelleKlokBeheerder.Stop(); // de klok start altijd stilstaand na het laden, de gebruiker
                                    // zet 'm zelf weer aan (voorkomt dat een net-geopend project
                                    // meteen ongevraagd routes begint te starten).

        SimulatieInstellingen.StandaardUitrolSeconden = bestand.StandaardUitrolSeconden;
        SimulatieInstellingen.WisselRustpauzeMilliseconden = bestand.WisselRustpauzeMilliseconden;

        // SnelheidsMetingBeheerder.Instellingen is alleen-lezen (blijft hetzelfde object
        // gedurende de levensduur van de beheerder) - dus de afzonderlijke waarden
        // overnemen i.p.v. het object te vervangen.
        snelheidsMetingBeheerder.Instellingen.MeldernNummer1 = bestand.SnelheidsMeting.MeldernNummer1;
        snelheidsMetingBeheerder.Instellingen.MeldernNummer2 = bestand.SnelheidsMeting.MeldernNummer2;
        snelheidsMetingBeheerder.Instellingen.LengteTrajectMm = bestand.SnelheidsMeting.LengteTrajectMm;
        snelheidsMetingBeheerder.Instellingen.Modelschaal = bestand.SnelheidsMeting.Modelschaal;
        snelheidsMetingBeheerder.Instellingen.ManierVanMeten = bestand.SnelheidsMeting.ManierVanMeten;

        var blokkenPerNummer = bestand.Blokken.ToDictionary(b => b.Nummer);
        foreach (var p in bestand.BlokPosities)
            if (blokkenPerNummer.TryGetValue(p.BlokNummer, out var blok))
                baanBeheerder.HerstelBlokPositie(blok, new Point(p.X, p.Y), p.Tabblad);

        // Loc-posities herstellen: eerst proberen op decoderadres (waarschijnlijker uniek),
        // anders terugvallen op de omschrijving. Een trein/blok die niet meer bestaat wordt
        // gewoon overgeslagen (bijv. handmatig verwijderd sinds de backup) i.p.v. te crashen.
        // GEVONDEN GAT (gebruikersmelding: "er kwam direct een spookmelding binnen" bij het
        // laden/opstarten): PlaatsLoc zet alleen de loc-op-blok-koppeling, niet IsBezet -
        // dus de EERSTE echte bezetmelding van de hardware voor zo'n hersteld blok werd
        // altijd als onverwachte spookmelding gezien, ook al stond de loc daar volgens het
        // project gewoon terecht. Zelfde fix als bij de handmatige plaats-acties in
        // BaanontwerpWindow/MainWindow: ZetHandmatigBezet erbij, zodat een geladen positie
        // net zo "bekend" is als een net-door-de-gebruiker-geplaatste loc.
        blokBeheerder.VerwijderAlleLocs();
        foreach (var p in bestand.LocPosities)
        {
            if (!blokkenPerNummer.TryGetValue(p.BlokNummer, out var blok)) continue;
            var trein = (p.DecoderAdres > 0 ? bestand.Treinen.FirstOrDefault(t => t.DecoderAdres == p.DecoderAdres) : null)
                        ?? bestand.Treinen.FirstOrDefault(t => t.Omschrijving == p.TreinOmschrijving);
            if (trein != null)
            {
                blokBeheerder.PlaatsLoc(blok, trein);
                blokBeheerder.ZetHandmatigBezet(blok, true);
            }
        }
    }
}
