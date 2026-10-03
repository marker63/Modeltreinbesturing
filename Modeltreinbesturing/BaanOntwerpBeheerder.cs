using System.Windows;
using System.Windows.Media;
using Modeltreinbesturing.Model;

namespace Modeltreinbesturing;

/// <summary>
/// Beheert laag 2: het baanontwerp. Blokken zelf horen bij de BlokBeheerder (laag 1) -
/// hier wordt alleen bijgehouden WAAR een blok getekend staat en op welk tabblad,
/// als puur visueel gegeven. Een blok hoeft niet expliciet "aangemaakt" te worden in
/// deze laag: zodra het in laag 1 bestaat, verschijnt het hier (net als in Koploper zelf).
/// </summary>
public class BaanOntwerpBeheerder
{
    public List<BaanSymbool> Symbolen { get; } = new();

    private readonly Dictionary<Blok, Point> _blokPosities = new();
    private readonly Dictionary<Blok, int> _blokTabbladen = new();

    /// <summary>Positie van een blok in het baanontwerp. Nieuwe blokken verschijnen automatisch
    /// bovenaan tabblad 1, precies zoals in de handleiding beschreven staat.</summary>
    public Point PositieVanBlok(Blok blok)
    {
        if (!_blokPosities.TryGetValue(blok, out var positie))
        {
            positie = new Point(20 + (_blokPosities.Count % 10) * 70, 20);
            _blokPosities[blok] = positie;
            _blokTabbladen[blok] = 1;
        }
        return positie;
    }

    public int TabbladVanBlok(Blok blok)
    {
        PositieVanBlok(blok); // zorgt dat het blok een default entry heeft
        return _blokTabbladen[blok];
    }

    public void VerplaatsBlok(Blok blok, Point nieuwePositie) => _blokPosities[blok] = nieuwePositie;

    public void ZetBlokOpTabblad(Blok blok, int tabblad) => _blokTabbladen[blok] = tabblad;

    /// <summary>Alle bekende blokposities, voor opslaan naar bestand.</summary>
    public IEnumerable<(Blok Blok, Point Positie, int Tabblad)> AlleBlokPosities() =>
        _blokPosities.Select(kv => (kv.Key, kv.Value, _blokTabbladen.TryGetValue(kv.Key, out var t) ? t : 1));

    /// <summary>Zet een blokpositie rechtstreeks, gebruikt bij het laden van een bestand
    /// (in tegenstelling tot PositieVanBlok geen automatische standaardplaatsing).</summary>
    public void HerstelBlokPositie(Blok blok, Point positie, int tabblad)
    {
        _blokPosities[blok] = positie;
        _blokTabbladen[blok] = tabblad;
    }

    /// <summary>Verwijdert alle laag-2 gegevens van een blok (aangeroepen als het blok zelf verwijderd wordt).</summary>
    public void VergeetBlok(Blok blok)
    {
        _blokPosities.Remove(blok);
        _blokTabbladen.Remove(blok);
        foreach (var symbool in Symbolen.OfType<IGekoppeldAanBlok>())
            if (symbool.GekoppeldBlok == blok) symbool.GekoppeldBlok = null;

        foreach (var lijn in Symbolen.OfType<Lijn>())
            for (int i = 0; i < lijn.PuntBlokAnkers.Count; i++)
                if (lijn.PuntBlokAnkers[i] == blok) lijn.PuntBlokAnkers[i] = null;
    }

    /// <summary>Ruimt lijnpunten op die aan een verwijderde wissel verankerd waren.</summary>
    public void VergeetWissel(Wissel wissel)
    {
        foreach (var lijn in Symbolen.OfType<Lijn>())
            for (int i = 0; i < lijn.PuntWisselAnkers.Count; i++)
                if (lijn.PuntWisselAnkers[i] == wissel) lijn.PuntWisselAnkers[i] = null;
    }

    public void VoegSymboolToe(BaanSymbool symbool) => Symbolen.Add(symbool);

    public void VerwijderSymbool(BaanSymbool symbool)
    {
        Symbolen.Remove(symbool);
        if (symbool is Wissel wissel) VergeetWissel(wissel);
    }

    /// <summary>Koppelen/loskoppelen van een symbool (lijn of sein) aan een blok - klik-aan/klik-uit,
    /// zoals in de handleiding. Werkt voor alles dat IGekoppeldAanBlok implementeert.</summary>
    public void ToggleKoppeling(IGekoppeldAanBlok symbool, Blok blok)
    {
        symbool.GekoppeldBlok = symbool.GekoppeldBlok == blok ? null : blok;
    }

    /// <summary>Wisselgeometrie - hoofdrichting + afbuiging als eenheidsvectoren, puur op
    /// basis van Wissel-eigen eigenschappen (HoofdrichtingGraden/AfbuigingLinksom), dus
    /// zonder enige UI-afhankelijkheid. Bewust EEN EIGEN KOPIE van dezelfde berekening die
    /// ook in BaanontwerpWindow.xaml.cs staat (voor het tekenen) - NIET samengevoegd, om
    /// geen risico te lopen op de toch al kwetsbare, meermaals gecorrigeerde
    /// wissel-tekenlogica daar. Puur geometrisch, dus onschadelijke duplicatie.</summary>
    /// <summary>Vertaalt een hoek (altijd een veelvoud van 45°) naar een RASTER-STAP i.p.v.
    /// een cos/sin-eenheidsvector - zie de uitgebreide toelichting bij de gelijknamige
    /// methode in BaanontwerpWindow.xaml.cs (bewust een EIGEN kopie hier, om geen risico te
    /// lopen op de kwetsbare wissel-tekenlogica daar). Kort: cos/sin geeft bij een
    /// diagonale hoek een IRRATIONEEL getal, waardoor een pootlengte daarop gebaseerd
    /// vrijwel nooit exact op een rasterkruispunt landt - een raster-stap (bij een
    /// diagonale richting 1 rasterstap in BEIDE assen tegelijk, als een koningszet) lost
    /// dat op.</summary>
    private static (int Dx, int Dy) RasterRichtingsstap(double hoekGraden)
    {
        int index = (int)Math.Round(((hoekGraden % 360) + 360) % 360 / 45.0) % 8;
        return index switch
        {
            0 => (1, 0), 1 => (1, 1), 2 => (0, 1), 3 => (-1, 1),
            4 => (-1, 0), 5 => (-1, -1), 6 => (0, -1), _ => (1, -1)
        };
    }

    /// <summary>De 3 ankerpunten van een wissel: [instroom, rechtdoor, afbuigend]. Sinds de
    /// Koploper-geïnspireerde compacte weergave is wissel.X,Y de INSTROOM-hoek zelf (geen
    /// centrum meer) - zie de uitgebreide toelichting bij de gelijknamige methode in
    /// BaanontwerpWindow.xaml.cs. MOET dezelfde RasterGrootte/pootRastercellen-waarden
    /// aanhouden als daar (16 resp. 1).</summary>
    public static IEnumerable<Point> WisselAnkerpunten(Wissel wissel)
    {
        const double rasterGrootte = 16;
        const int pootRastercellen = 1;
        var (dx, dy) = RasterRichtingsstap(wissel.HoofdrichtingGraden);
        double afbuigHoekGraden = wissel.HoofdrichtingGraden + (wissel.AfbuigingLinksom ? -45 : 45);
        var (adx, ady) = RasterRichtingsstap(afbuigHoekGraden);
        double stap = pootRastercellen * rasterGrootte;
        yield return new Point(wissel.X, wissel.Y);
        yield return new Point(wissel.X + dx * stap, wissel.Y + dy * stap);
        yield return new Point(wissel.X + adx * stap, wissel.Y + ady * stap);
    }

    private static int DichtstbijzijndWisselpunt(List<Point> wisselpunten, Point lijnpunt)
    {
        int beste = 0;
        double kleinsteAfstand = double.MaxValue;
        for (int k = 0; k < wisselpunten.Count; k++)
        {
            double afstand = (wisselpunten[k] - lijnpunt).LengthSquared;
            if (afstand < kleinsteAfstand) { kleinsteAfstand = afstand; beste = k; }
        }
        return beste;
    }

    /// <summary>De 4 hoekpunten van een Kruiswissel/Engelse wissel - MOET dezelfde geometrie
    /// gebruiken als de tekenmethode in BaanontwerpWindow.xaml.cs (index 0/1 = de eerste
    /// rechte lijn door het midden, index 2/3 = de tweede, 45 graden gedraaid). Gebruikt
    /// door VindPadMetWisselstanden om lijnen die coördinaat-gewijs op een hoekpunt
    /// eindigen te herkennen, ook als de PuntKruiswisselAnkers-verwijzing zelf ontbreekt
    /// (zie de toelichting daar).</summary>
    private static IEnumerable<Point> KruiswisselAnkerpunten(Kruiswissel kruiswissel)
    {
        const double halveLengte = 20 * 0.9;
        foreach (double hoekOffset in new[] { 0.0, 45.0 })
        {
            double hoekRad = (kruiswissel.HoofdrichtingGraden + hoekOffset) * Math.PI / 180.0;
            var richting = new Vector(Math.Cos(hoekRad), Math.Sin(hoekRad));
            yield return new Point(kruiswissel.X - richting.X * halveLengte, kruiswissel.Y - richting.Y * halveLengte);
            yield return new Point(kruiswissel.X + richting.X * halveLengte, kruiswissel.Y + richting.Y * halveLengte);
        }
    }


    /// <summary>Zoekt een pad tussen twee blokken door het lijnen-/ankerpuntennetwerk EN
    /// bepaalt daarbij voor elke tegengekomen wissel welke STAND nodig is om dat pad
    /// daadwerkelijk te kunnen berijden - in tegenstelling tot BaanontwerpWindow's
    /// VindCorridor (die de HUIDIGE, al ingestelde stand van een wissel als gegeven
    /// aanneemt, voor de reservering-kleuring), probeert deze zoektocht BEIDE takken van
    /// elke wissel en onthoudt welke stand nodig was voor het uiteindelijk gevonden pad.
    /// Gebruikt door TreinrouteWindow.AutomatischeStapProberen om wissels daadwerkelijk in
    /// de juiste stand te zetten (incl. het echte hardware-commando) VOORDAT een trein
    /// automatisch een nieuw blok kiest - dit ONTBRAK VOLLEDIG voor automatisch rijden.</summary>
    /// <summary>
    /// Zoekt, via de getekende lijnen, welke wissels op welke stand moeten staan om van
    /// "van" naar "naar" te kunnen rijden.
    ///
    /// GEVONDEN, STRUCTUREEL GAT (gebruikersmelding: wissels werden aangestuurd voor een
    /// volledig geïsoleerd, wisselloos pendelspoor - bevestigd door de gebruiker: "de
    /// blokken staan helemaal los van alle andere blokken"): deze zoekfunctie doorliep een
    /// lijn voorheen ALTIJD in zijn geheel, van index 0 tot het eind, ONGEACHT waar "van"
    /// (of, verderop, het instappunt vanuit een vorige lijn/wissel) zich daadwerkelijk op
    /// die lijn bevindt. Een ankerpunt dat fysiek aan de ANDERE kant van dat instappunt zit
    /// - dus feitelijk "achter" de trein, niet bereikbaar zonder eerst te keren - werd zo
    /// ten onrechte toch meegenomen. Dat verklaart precies hoe een volledig los baanvakje
    /// via zo'n "verkeerde kant op"-ontdekking een pad kon vinden met een wissel die er
    /// helemaal niets mee te maken heeft.
    ///
    /// Elke lijn in de wachtrij draagt nu zijn EIGEN instapindex en -richting mee, en wordt
    /// alleen nog in die richting gescand (een blok/wissel-anker zit fysiek altijd aan één
    /// van de twee uiteinden van een lijn - zit een anker onverwacht niet aan een uiteinde,
    /// dan is de richting niet eenduidig af te leiden en worden voor de zekerheid beide
    /// kanten onderzocht, zodat er nooit een echt bestaand pad ten onrechte gemist wordt).
    /// </summary>
    /// <summary>
    /// Zoekt, via de getekende lijnen, welke wissels (en Engelse wissels) op welke stand
    /// moeten staan om van "van" naar "naar" te kunnen rijden.
    ///
    /// GEVONDEN, STRUCTUREEL GAT (gebruikersmelding: wissels werden aangestuurd voor een
    /// volledig geïsoleerd, wisselloos pendelspoor - bevestigd door de gebruiker: "de
    /// blokken staan helemaal los van alle andere blokken"): deze zoekfunctie doorliep een
    /// lijn voorheen ALTIJD in zijn geheel, van index 0 tot het eind, ONGEACHT waar "van"
    /// (of, verderop, het instappunt vanuit een vorige lijn/wissel) zich daadwerkelijk op
    /// die lijn bevindt. Een ankerpunt dat fysiek aan de ANDERE kant van dat instappunt zit
    /// - dus feitelijk "achter" de trein, niet bereikbaar zonder eerst te keren - werd zo
    /// ten onrechte toch meegenomen. Dat verklaart precies hoe een volledig los baanvakje
    /// via zo'n "verkeerde kant op"-ontdekking een pad kon vinden met een wissel die er
    /// helemaal niets mee te maken heeft.
    ///
    /// Elke lijn in de wachtrij draagt nu zijn EIGEN instapindex en -richting mee, en wordt
    /// alleen nog in die richting gescand (een blok/wissel/kruiswissel-anker zit fysiek
    /// altijd aan één van de twee uiteinden van een lijn - zit een anker onverwacht niet aan
    /// een uiteinde, dan is de richting niet eenduidig af te leiden en worden voor de
    /// zekerheid beide kanten onderzocht, zodat er nooit een echt bestaand pad ten onrechte
    /// gemist wordt).
    ///
    /// GEBRUIKERSVERZOEK ("Engelse wissel"): een Kruiswissel heeft 4 aansluitpunten (indices
    /// 0-3, zie Lijn.PuntKruiswisselAnkerIndex - deze index staat, in tegenstelling tot bij
    /// een gewone Wissel, al PER LIJNPUNT vast sinds het tekenen, dus geen aparte
    /// afstandsberekening nodig). Index 0<->1 en 2<->3 zijn de twee rechte lijnen door het
    /// midden - altijd geldig, ook bij een PASSIEVE (niet-Engelse) kruiswissel, want dat is
    /// nu eenmaal een gewone kruising zonder schakelfunctie. Index 0<->2 en 1<->3 zijn de
    /// "ondiepe hoek"-paren waarlangs een ECHTE Engelse wissel (IsEngels) ook kan overstappen
    /// - vereist dan de Overstap-stand. Index 0<->3 en 1<->2 (de "scherpe hoek"-paren)
    /// bestaan fysiek nooit, ongeacht IsEngels - een trein kan daar nooit overheen.
    /// </summary>
    public (List<(Wissel Wissel, WisselStand BenodigdeStand)> Wisselstanden, List<(Kruiswissel Kruiswissel, KruiswisselStand BenodigdeStand)> Kruiswisselstanden, List<Lijn> Lijnen)? VindPadMetWisselstanden(Blok van, Blok naar)
    {
        var alleLijnen = Symbolen.OfType<Lijn>().ToList();

        // GEVONDEN, FUNDAMENTEEL GAT (gebruikerslay-out: blok3->blok4 bleek onbereikbaar,
        // ook zonder dat er een defecte wissel tussen zat): een lijn-aan-lijn-verbinding
        // (PuntLijnAnkers) bleek in de praktijk vaak maar ÉÉN kant op opgeslagen - lijn A
        // verwijst naar lijn B, maar lijn B heeft zelf GEEN verwijzing terug naar A
        // (waarschijnlijk legt de tekentool de koppeling alleen vast bij de lijn die als
        // TWEEDE getekend is). Deze zoekfunctie zocht bij zo'n overgang altijd naar een
        // verwijzing TERUG naar de lijn waar je vandaan kwam, om het juiste instappunt te
        // bepalen - stond die er niet, dan viel de code terug op instapindex 0, wat vaak
        // toevallig fout was en het pad daar stil liet doodlopen, ook al bestond de fysieke
        // verbinding gewoon. Oplossing: bouw hier EENMALIG een volledig SYMMETRISCHE
        // verbindingstabel op basis van COÖRDINATEN (niet de opgeslagen verwijzing zelf) -
        // heeft lijn A's punt i dezelfde X/Y als lijn B's punt j, dan verbindt dat punt de
        // twee lijnen, ongeacht welke kant dat oorspronkelijk had vastgelegd.
        var symmetrischeVerbinding = new Dictionary<(Lijn, int), (Lijn, int)>();
        foreach (var lijn in alleLijnen)
        {
            for (int i = 0; i < lijn.Punten.Count && i < lijn.PuntLijnAnkers.Count; i++)
            {
                if (lijn.PuntLijnAnkers[i] is not Lijn andereLijn) continue;
                if (symmetrischeVerbinding.ContainsKey((lijn, i))) continue; // al gevonden (via de andere kant)
                for (int j = 0; j < andereLijn.Punten.Count; j++)
                {
                    if ((andereLijn.Punten[j] - lijn.Punten[i]).LengthSquared > 0.01) continue;
                    symmetrischeVerbinding[(lijn, i)] = (andereLijn, j);
                    symmetrischeVerbinding[(andereLijn, j)] = (lijn, i);
                    break;
                }
            }
        }

        // GEVONDEN, VERWANT GAT (gebruikersvraag: "kan dit door de Engelse wissel komen?" -
        // klopte, al bleek de oorzaak subtieler dan de eigenlijke schakellogica): lijnen die
        // een tekenpunt EXACT op een van de 4 hoekpunten van een Kruiswissel hebben, bleken
        // in de praktijk vaak GEEN PuntKruiswisselAnkers-verwijzing te hebben - de
        // koppeling werd bij het tekenen kennelijk niet altijd vastgelegd, ook al staat het
        // punt er coördinaat-gewijs precies op. Zonder die verwijzing was zo'n lijn voor
        // deze zoekfunctie een doodlopend eind, ook al bestond de fysieke aansluiting op de
        // kruiswissel gewoon. Zelfde oplossing als hierboven bij lijn-aan-lijn: bouw een
        // COÖRDINAAT-gebaseerde tabel op, onafhankelijk van of de verwijzing zelf is
        // vastgelegd.
        var kruiswisselVerbinding = new Dictionary<(Lijn, int), (Kruiswissel, int)>();
        foreach (var kruiswissel in Symbolen.OfType<Kruiswissel>())
        {
            var hoekpunten = KruiswisselAnkerpunten(kruiswissel).ToList();
            foreach (var lijn in alleLijnen)
            {
                for (int i = 0; i < lijn.Punten.Count; i++)
                {
                    for (int hoekIndex = 0; hoekIndex < hoekpunten.Count; hoekIndex++)
                    {
                        if ((lijn.Punten[i] - hoekpunten[hoekIndex]).LengthSquared > 0.01) continue;
                        kruiswisselVerbinding[(lijn, i)] = (kruiswissel, hoekIndex);
                        break;
                    }
                }
            }
        }

        var bezochteLijnen = new HashSet<Lijn>();
        var wachtrij = new Queue<(Lijn Lijn, int StartIndex, int Richting,
            List<(Wissel Wissel, WisselStand Stand)> PadWisselstanden,
            List<(Kruiswissel Kruiswissel, KruiswisselStand Stand)> PadKruiswisselstanden,
            List<Lijn> PadLijnen)>();

        void VoegToe(Lijn lijn, int instapIndex, List<(Wissel, WisselStand)> pad, List<(Kruiswissel, KruiswisselStand)> kruispad, List<Lijn> padLijnen)
        {
            bool kanVoorwaarts = instapIndex < lijn.Punten.Count - 1;
            bool kanAchterwaarts = instapIndex > 0;
            if (kanVoorwaarts) wachtrij.Enqueue((lijn, instapIndex, 1, pad, kruispad, padLijnen));
            if (kanAchterwaarts) wachtrij.Enqueue((lijn, instapIndex, -1, pad, kruispad, padLijnen));
        }

        // Zie de toelichting bij kruiswisselVerbinding hierboven: eerst de EXPLICIETE
        // verwijzing proberen (zoals bij een gewone Wissel), en alleen als die ontbreekt
        // terugvallen op de coördinaat-tabel.
        bool KruiswisselOpPunt(Lijn l, int idx, Dictionary<(Lijn, int), (Kruiswissel, int)> tabel, out Kruiswissel gevonden, out int hoekIndex)
        {
            if (idx < l.PuntKruiswisselAnkers.Count && l.PuntKruiswisselAnkers[idx] is Kruiswissel expliciet)
            {
                gevonden = expliciet;
                hoekIndex = l.PuntKruiswisselAnkerIndex[idx];
                return true;
            }
            if (tabel.TryGetValue((l, idx), out var uitTabel))
            {
                gevonden = uitTabel.Item1;
                hoekIndex = uitTabel.Item2;
                return true;
            }
            gevonden = null!;
            hoekIndex = 0;
            return false;
        }

        foreach (var startLijn in alleLijnen.Where(l => l.PuntBlokAnkers.Contains(van)))
        {
            bezochteLijnen.Add(startLijn);
            int vanIndex = startLijn.PuntBlokAnkers.IndexOf(van);
            VoegToe(startLijn, vanIndex, new List<(Wissel, WisselStand)>(), new List<(Kruiswissel, KruiswisselStand)>(), new List<Lijn> { startLijn });
        }

        while (wachtrij.Count > 0)
        {
            var (lijn, startIndex, richting, padWisselstanden, padKruiswisselstanden, padLijnen) = wachtrij.Dequeue();
            // Lokale, groeiende kopieën (zelfde reden als bij VindCorridor's eerdere
            // bugfix: als DEZE lijn zowel een wissel/kruiswissel-anker als, verderop, het
            // bestemmingsblok heeft, moet die meegenomen worden in het uiteindelijke
            // resultaat).
            var padWisselstandenTotNu = new List<(Wissel, WisselStand)>(padWisselstanden);
            var padKruiswisselstandenTotNu = new List<(Kruiswissel, KruiswisselStand)>(padKruiswisselstanden);

            for (int i = startIndex; i >= 0 && i < lijn.Punten.Count; i += richting)
            {
                if (i < lijn.PuntBlokAnkers.Count && lijn.PuntBlokAnkers[i] == naar)
                    return (padWisselstandenTotNu, padKruiswisselstandenTotNu, padLijnen);

                if (i < lijn.PuntWisselAnkers.Count && lijn.PuntWisselAnkers[i] is Wissel wissel)
                {
                    var wisselpunten = WisselAnkerpunten(wissel).ToList();
                    int binnenkomstIndex = DichtstbijzijndWisselpunt(wisselpunten, lijn.Punten[i]);

                    foreach (var anderelijn in alleLijnen)
                    {
                        if (bezochteLijnen.Contains(anderelijn)) continue;
                        for (int j = 0; j < anderelijn.PuntWisselAnkers.Count && j < anderelijn.Punten.Count; j++)
                        {
                            if (anderelijn.PuntWisselAnkers[j] != wissel) continue;
                            int uitgangIndex = DichtstbijzijndWisselpunt(wisselpunten, anderelijn.Punten[j]);
                            // Instroom(0)<->rechtdoor(1) vereist Rechtdoor; instroom(0)<->
                            // afbuigend(2) vereist Afbuigend. Rechtdoor<->afbuigend zonder via
                            // instroom bestaat fysiek niet bij een gewone wissel - overslaan.
                            WisselStand? benodigdeStand = (binnenkomstIndex, uitgangIndex) switch
                            {
                                (0, 1) or (1, 0) => WisselStand.Rechtdoor,
                                (0, 2) or (2, 0) => WisselStand.Afbuigend,
                                _ => null
                            };
                            if (benodigdeStand != null)
                            {
                                bezochteLijnen.Add(anderelijn);
                                var nieuwePadWisselstanden = new List<(Wissel, WisselStand)>(padWisselstandenTotNu) { (wissel, benodigdeStand.Value) };
                                VoegToe(anderelijn, j, nieuwePadWisselstanden, padKruiswisselstandenTotNu, new List<Lijn>(padLijnen) { anderelijn });
                            }
                            break;
                        }
                    }
                }
                else if (KruiswisselOpPunt(lijn, i, kruiswisselVerbinding, out var kruiswissel, out int binnenkomstIndex))
                {
                    foreach (var anderelijn in alleLijnen)
                    {
                        if (bezochteLijnen.Contains(anderelijn)) continue;
                        for (int j = 0; j < anderelijn.Punten.Count; j++)
                        {
                            if (!KruiswisselOpPunt(anderelijn, j, kruiswisselVerbinding, out var anderKruiswissel, out int uitgangIndex) || anderKruiswissel != kruiswissel) continue;
                            KruiswisselStand? benodigdeStand = (binnenkomstIndex, uitgangIndex) switch
                            {
                                (0, 1) or (1, 0) or (2, 3) or (3, 2) => KruiswisselStand.Rechtdoor,
                                (0, 2) or (2, 0) or (1, 3) or (3, 1) when kruiswissel.IsEngels => KruiswisselStand.Overstap,
                                _ => null
                            };
                            if (benodigdeStand != null)
                            {
                                bezochteLijnen.Add(anderelijn);
                                var nieuwePadKruiswisselstanden = new List<(Kruiswissel, KruiswisselStand)>(padKruiswisselstandenTotNu) { (kruiswissel, benodigdeStand.Value) };
                                VoegToe(anderelijn, j, padWisselstandenTotNu, nieuwePadKruiswisselstanden, new List<Lijn>(padLijnen) { anderelijn });
                            }
                            break;
                        }
                    }
                }
                else if (symmetrischeVerbinding.TryGetValue((lijn, i), out var doel) && !bezochteLijnen.Contains(doel.Item1))
                {
                    bezochteLijnen.Add(doel.Item1);
                    // Het juiste instappunt komt nu rechtstreeks uit de vooraf opgebouwde,
                    // symmetrische (coördinaat-gebaseerde) verbindingstabel - niet meer
                    // afhankelijk van of DEZE specifieke lijn toevallig zelf een verwijzing
                    // terug had opgeslagen (zie de toelichting hierboven).
                    VoegToe(doel.Item1, doel.Item2, new List<(Wissel, WisselStand)>(padWisselstandenTotNu), new List<(Kruiswissel, KruiswisselStand)>(padKruiswisselstandenTotNu), new List<Lijn>(padLijnen) { doel.Item1 });
                }
            }
        }
        return null;
    }

    /// <summary>Wist het hele baanontwerp - gebruikt bij "Nieuw project".</summary>
    public void Reset()
    {
        Symbolen.Clear();
        _blokPosities.Clear();
        _blokTabbladen.Clear();
    }
}
