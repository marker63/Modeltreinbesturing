using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Modeltreinbesturing;

/// <summary>Een venster met de gebruiksaanwijzing van Modeltreinbesturing, opgebouwd uit
/// onderwerpen (links een lijst, rechts de inhoud) met een eenvoudige zoekfunctie.
/// Bewust GEEN los .md/.txt-bestand dat los van de code kan raken - alle inhoud staat
/// hier direct in code, zodat een nieuwe functie en de bijbehorende uitleg in dezelfde
/// wijziging meegenomen kunnen worden.</summary>
public partial class HelpWindow : Window
{
    private record Onderwerp(string Titel, string[] Paragrafen);

    private readonly List<Onderwerp> _onderwerpen;

    public HelpWindow()
    {
        InitializeComponent();
        _onderwerpen = BouwOnderwerpen();
        VulOnderwerpenLijst(_onderwerpen);
        OnderwerpenLijst.SelectedIndex = 0;
    }

    private void VulOnderwerpenLijst(List<Onderwerp> onderwerpen)
    {
        OnderwerpenLijst.ItemsSource = null;
        OnderwerpenLijst.ItemsSource = onderwerpen.Select(o => o.Titel).ToList();
    }

    private void OnderwerpenLijst_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (OnderwerpenLijst.SelectedIndex < 0) return;
        string titel = (string)OnderwerpenLijst.SelectedItem;
        var onderwerp = _onderwerpen.FirstOrDefault(o => o.Titel == titel);
        if (onderwerp != null) InhoudViewer.Document = BouwDocument(onderwerp);
    }

    /// <summary>Eenvoudige zoekfunctie: filtert de onderwerpenlijst op onderwerpen waarvan
    /// de titel OF een van de paragrafen de zoekterm bevat (niet hoofdlettergevoelig).</summary>
    private void ZoekBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        string zoekterm = ZoekBox.Text.Trim();
        if (zoekterm.Length == 0)
        {
            VulOnderwerpenLijst(_onderwerpen);
            ZoekResultaatTekst.Text = "";
            if (OnderwerpenLijst.Items.Count > 0) OnderwerpenLijst.SelectedIndex = 0;
            return;
        }

        var gevonden = _onderwerpen
            .Where(o => o.Titel.Contains(zoekterm, StringComparison.OrdinalIgnoreCase)
                     || o.Paragrafen.Any(p => p.Contains(zoekterm, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        VulOnderwerpenLijst(gevonden);
        ZoekResultaatTekst.Text = gevonden.Count == 0 ? "Niets gevonden." : $"{gevonden.Count} onderwerp(en) gevonden.";
        if (gevonden.Count > 0) OnderwerpenLijst.SelectedIndex = 0;
    }

    private static FlowDocument BouwDocument(Onderwerp onderwerp)
    {
        var doc = new FlowDocument { FontSize = 13, PagePadding = new Thickness(4) };
        doc.Blocks.Add(new Paragraph(new Run(onderwerp.Titel) { FontSize = 20, FontWeight = FontWeights.Bold })
        {
            Margin = new Thickness(0, 0, 0, 12)
        });
        foreach (var tekst in onderwerp.Paragrafen)
        {
            // Regels die met "### " beginnen worden een subkopje, de rest gewone tekst -
            // een heel lichte eigen "opmaaktaal" i.p.v. losse Paragraph-per-alinea overal
            // in BouwOnderwerpen te moeten schrijven.
            if (tekst.StartsWith("### "))
            {
                doc.Blocks.Add(new Paragraph(new Run(tekst[4..]) { FontWeight = FontWeights.Bold, FontSize = 15 })
                {
                    Margin = new Thickness(0, 14, 0, 4)
                });
            }
            else if (tekst.StartsWith("- "))
            {
                doc.Blocks.Add(new Paragraph(new Run("•  " + tekst[2..])) { Margin = new Thickness(12, 2, 0, 2) });
            }
            else
            {
                doc.Blocks.Add(new Paragraph(new Run(tekst)) { Margin = new Thickness(0, 0, 0, 8) });
            }
        }
        return doc;
    }

    private static List<Onderwerp> BouwOnderwerpen() => new()
    {
        new Onderwerp("Welkom", new[]
        {
            "Modeltreinbesturing is een clone van het bekende modelspoor-besturingsprogramma Koploper (PaHaSOFT). Het programma is opgebouwd uit vier lagen, elk in een eigen venster, die je in principe in deze volgorde doorloopt bij het opzetten van een nieuwe baan.",
            "### De vier lagen",
            "- Laag 1 - Blokkenschema (dit venster): de LOGISCHE indeling. Welke blokken zijn er, en tussen welke blokken kan een trein rijden?",
            "- Laag 2 - Baanontwerp: de VISUELE tekening van de baan zelf (rails, wissels, seinen, perrons), gekoppeld aan de blokken uit laag 1.",
            "- Laag 3 - Wisselstraten: welke wissels moeten in welke stand staan om van blok A naar blok B te rijden.",
            "- Laag 4 - Treinroutes: waar treinen daadwerkelijk gaan rijden, vast of automatisch.",
            "Deze volgorde is niet toevallig gekozen: elke laag bouwt voort op de vorige. Begin dus bij het blokkenschema, ook al is dat het minst 'mooie' scherm - de rest werkt er direct op verder.",
            "Gebruik de zoekbalk hierboven om snel een specifiek onderwerp te vinden, of blader door de lijst links.",
            "### Over dit project",
            "Modeltreinbesturing is open source: iedereen mag het gebruiken, testen en uitbreiden. Zie het bijgeleverde LICENSE-bestand (MIT) voor de precieze voorwaarden, en README.md voor achtergrond en bijdrage-informatie.",
            "Grote delen van deze applicatie zijn gebouwd met behulp van Claude (Anthropic), als hulpmiddel bij het programmeren."
        }),

        new Onderwerp("Sneltoetsenoverzicht", new[]
        {
            "Een verzamelde lijst van alle sneltoetsen in het programma - er zijn er inmiddels best wat bijgekomen.",
            "### Baanontwerp (laag 2, bewerk-modus)",
            "- Delete: geselecteerd symbool verwijderen.",
            "- Ctrl+Z: verwijdering ongedaan maken (geeft het laatst verwijderde symbool terug).",
            "- Ctrl+Y: opnieuw uitvoeren (verwijdert het net-teruggegeven symbool weer).",
            "### Blokkenschema (laag 1)",
            "- Rechtermuisknop op een lege plek: nieuw blok.",
            "- Delete (op een geselecteerd blok of relatie): verwijderen.",
            "- K (op een geselecteerde relatie): 'Keer' aan/uit (Koploper's 'uit-via-naar'-keer-vinkje).",
            "- Ctrl+sleep (blok met geplaatste loc naar een direct verbonden blok): loc daarheen verplaatsen en automatisch laten rijden.",
            "### Algemeen",
            "- F1: gebruiksaanwijzing (dit scherm) openen, vanuit vrijwel elk venster.",
            "- Kijkscherm: klik op een wissel om 'm handmatig om te zetten; rechtsklik op een wissel voor defect/hersteld; klik op een blok met een loc erop voor pauzeren/hervatten/reservering opheffen/verwijderen; rechtsklik op een blok voor een volledig overzicht (met een knop om vandaaruit alsnog een incident te melden)."
        }),

        new Onderwerp("Blokkenschema (laag 1)", new[]
        {
            "Het blokkenschema-venster (dit venster) toont je baan als een eenvoudig schema van rechthoeken (blokken) verbonden door lijnen (relaties) - een logisch schema, geen echte tekening van de rails.",
            "### Een blok toevoegen",
            "Rechtsklik op een lege plek om een nieuw blok te maken. Dubbelklik een blok om de eigenschappen te openen: nummer, omschrijving, type (Normaal/Kopspoor/Opstelspoor/Station), maximale treinlengte met bijbehorende actie, en de bezetmeldpunten.",
            "### Relaties",
            "Sleep een blok bovenop een ander blok om een relatie (rijrichting) te maken - dit legt vast dat een trein van het ene blok naar het andere mag rijden. Een relatie is zelf ook aanklikbaar (wordt rood/dikker) en met Delete te verwijderen.",
            "Koploper's eigen 'uit-via-naar'-principe (letterlijk van de ontwikkelaar: 'de basis van het gehele systeem'): elke overgang heeft een eigen 'keer loc'-vinkje dat bepaalt of de loc bij DIE specifieke overgang fysiek van rijrichting moet wisselen - bijvoorbeeld een normaal blok waar de trein weer terug moet naar waar hij vandaan kwam. Selecteer een relatie en druk op K om 'Keer' aan/uit te zetten (of gebruik Beheren -> Relaties beheren voor een overzicht van alle relaties tegelijk). Een relatie met Keer aan krijgt dezelfde wachttijd-behandeling als een kopspoor.",
            "Bij Treinen beheren kun je een loc 'Mag nooit gedwongen keren' meegeven (alleen relevant bij automatisch rijden) - net als in de echte spoorwereld, waar een loc-getrokken trein niet zomaar achteruit mag rijden. Bij het kiezen van een vervolgblok kijkt het programma dan vooruit of die keuze de trein uiteindelijk altijd op een kopspoor of Keer-relatie laat uitkomen, en sluit zo'n keuze dan ONVOORWAARDELIJK uit - GEEN terugval: blijft er niets bruikbaars over, dan wacht de trein gewoon (net als bij 'alles bezet'), hij keert nooit, ook niet als laatste redmiddel. Zet dit uit voor een loc die je juist voor rangeerbewegingen gebruikt, waar keren wel moet kunnen.",
            "### Bezetmeldpunten",
            "Elk blok heeft doorgaans twee meldpunten nodig: een Voorblok-melder (dekt het hele blok, meldt 'trein is aangekomen') en een Stopsectie-melder (dekt het laatste stukje, meldt 'trein moet stoppen'). Een blok kan ook MEERDERE stopsecties hebben met een eigen maximale treinlengte - handig bij schaduwstations, zodat een korte trein eerder stopt en een lange trein verder doorrijdt ('dynamische lengte').",
            "### Overige knoppen",
            "- Loc plaatsen/weghalen: zet een trein op een blok (ook mogelijk vanuit het baanontwerp).",
            "- Handmatig bezetten/vrijgeven: markeer een blok tijdelijk als bezet, bijvoorbeeld om wisselstraten/seinen te testen zonder een echte trein.",
            "- Foutmelding zetten/opheffen: laat een blok knipperen, bijvoorbeeld na een spookmelding (zie het onderwerp Bezetmelders).",
            "- Ga naar blok: scrolt en centreert direct naar een bloknummer - handig bij een grote baan.",
            "Het menu bovenin (Bestand/Vensters/Beheren/Geselecteerd blok) geeft toegang tot alle overige schermen: treintypes, treinen, richtingsverboden, stopverboden, blokgroepen, kleurenschema, hardware-aansluiting, en het overzicht van alle beperkingen."
        }),

        new Onderwerp("Baanontwerp tekenen (laag 2)", new[]
        {
            "Open het baanontwerp via Vensters -> Baanontwerp openen. Hier teken je de daadwerkelijke rails, wissels, seinen en perrons - de echte tekening van je baan, in tegenstelling tot het abstracte blokkenschema.",
            "### Lijnen tekenen",
            "Kies de Lijn-tool en klik om een kort lijnstukje neer te zetten. Ga daarna naar Verplaatsen en sleep de losse eindpunten naar hun uiteindelijke plek - ze klappen vast op nabije ankerpunten (van een blok, wissel, of een andere lijn) of anders op de dichtstbijzijnde 45°-hoek. Let op: je moet vrij precies op een lijn klikken om 'm te selecteren.",
            "### Symbolen plaatsen",
            "De werkbalk bovenin heeft een tool per symbooltype: Wissel, Sein, Stootblok, Perron, Tekst, Schakelaar, Pijl, Ontkoppelrail, Bezetmelder. Kies een tool en klik op de gewenste plek om het symbool te plaatsen.",
            "### Gegevens koppelen aan blok",
            "Met de 'Koppelen aan blok'-tool klik je eerst een blok aan, en klik je daarna lijnen/seinen/bezetmelders aan om ze aan dat blok te koppelen - een lijn gekoppeld aan het GESELECTEERDE blok kleurt felgroen, aan een ander blok oranje. Dit is nodig zodat het programma tijdens het rijden weet welk stukje spoor bij welk blok hoort (en dus welke kleur het moet krijgen).",
            "### Wissels",
            "Dubbelklik een wissel om de stand om te zetten (rechtdoor/afbuigend). Shift+dubbelklik stelt het adres in. Rechtsklik draait de richting in stappen van 45°, Shift+rechtsklik spiegelt de afbuigende poot. Ctrl+rechtsklik zet 'altijd initialiseren' aan/uit (rode ring) - stuurt de stand bij elke verbinding met hardware opnieuw, ook als de software denkt dat 'm al goed staat.",
            "### Wissel op defect zetten",
            "Alt+rechtsklik op een wissel markeert 'm als defect (rode X erover) - nogmaals Alt+rechtsklikken herstelt 'm weer. Een route die een wisselstraat met een defecte wissel nodig heeft, weigert te starten, en automatisch/hardware-omzetten slaat een defecte wissel altijd over. Handig om een kapotte of vastgelopen fysieke wissel na te bootsen zonder de baan zelf aan te passen.",
            "### Overloopwissel koppelen",
            "Met deze tool klik je twee wissels na elkaar aan om ze te koppelen (paarse stippellijn) - ze zetten voortaan altijd samen om, zoals een echte overloopwissel tussen twee parallelle sporen. Dezelfde wissel nogmaals klikken ontkoppelt weer.",
            "### Seinen",
            "Een sein toont automatisch rood/geel/groen op basis van het gekoppelde blok (rood=bezet, geel=blok-daarna-bezet, groen=vrij). Sleep een sein vrij rond en gebruik rechtsklik om 'm in stappen van 45° te draaien. Een sein kan ook als 'rangeersein' aan een hele wisselstraat gekoppeld worden (via Seinen beheren) in plaats van aan een blok - handig vóór een wisselstraat met meerdere vertreksporen.",
            "### Type sein",
            "Via Seinen beheren kies je ook het TYPE van elk sein afzonderlijk: 'Standaard 3 standen' (rood/geel/groen, de standaard), 'Standaard 2 standen' (alleen rood/groen, toont nooit geel), 'Dwergsein' (een klein rangeersein met een kleiner symbool en ook maar 2 lampposities), en 'Voorsein' (kondigt vooraf het seinbeeld van het EERSTVOLGENDE sein op de route aan, in plaats van zijn eigen blok te bewaken - handig om een machinist tijdig te laten afremmen, net als in het echt).",
            "### Tabbladen",
            "Rechtsklik onderin voor een nieuw tabblad bij grote banen. Een blok kan op meerdere tabbladen tegelijk zichtbaar zijn (Shift+dubbelklik op een blok wisselt het tabblad waarop het staat).",
            "### Wisselstraten",
            "Kies de Wisselstraat-tool om een van-blok en een naar-blok aan te klikken, en dan de wissels die daarbij horen. Bij Vensters -> Wisselstraten beheren kun je bestaande wisselstraten bekijken/corrigeren (dubbelklik een wissel om de gewenste stand om te draaien), handmatig activeren, of 'Kopiëren (nieuwe variant)' gebruiken - net als het echte Koploper: maakt een nieuwe, alternatieve wisselstraat tussen HETZELFDE blokpaar aan (met een eigen volgnummer, tijdens automatisch rijden achtereenvolgens geprobeerd), gevuld met dezelfde wissels/standen als startpunt, handig als je maar één of twee standen hoeft aan te passen voor een bijna-identieke variant.",
            "### Exporteren",
            "Elk venster heeft een 'Exporteer als afbeelding'-knop om een momentopname als plaatje te bewaren."
        }),

        new Onderwerp("Kijk-modus", new[]
        {
            "Naast het gewone baanontwerp (om te tekenen) is er een tweede, schoon venster puur om de baan te BEKIJKEN tijdens het rijden - open via Vensters -> Baanoverzicht (kijk-modus) in het blokkenschema-venster.",
            "In kijk-modus is er geen werkbalk en kun je niets bewerken (geen slepen van symbolen, geen nieuwe symbolen, geen Delete) - puur scrollen en meekijken. Ankerpunt-handjes en de oranje/paarse bewerk-kleuren van gekoppelde lijnen zijn hier ook onzichtbaar/uitgeschakeld, zodat alleen de echte rijstatussen (bezet/gereserveerd/net vrijgegeven) opvallen. Uitzonderingen, bedoeld voor eindgebruikers die de baan bedienen zonder het bewerk-scherm te hoeven openen: klik op een wissel om 'm handmatig om te zetten (geen dubbelklik nodig, een defecte wissel kan hier niet omgezet worden); rechtsklik op een wissel om 'm defect te zetten/herstellen; klik op een blok MET een loc erop voor een klein actiemenu (pauzeren/hervatten/reservering opheffen/verwijderen, zie ook 'Direct boven Handmatig rijden' hieronder - hetzelfde, maar dan direct vanaf het blok); rechtsklik op een blok (zonder loc, of ernaast) voor een volledig overzicht (zie het onderwerp 'Incidenten en onderhoud'); sleep een locomotief vanuit het Overzicht locomotieven naartoe om 'm op een blok te plaatsen; en Ctrl+sleep een blok MET een geplaatste loc naar een ANDER blok om die loc daar automatisch naartoe te laten rijden (zoekt zelf een toegestaan pad). Bovenin staat een menu met 'Bouwschermen' (om alsnog bij het blokkenschema/baanontwerp/treinroutes te komen - dat vereist wél inloggen, zie 'Accounts en toegang'; het blokkenschema heeft zelf een menu-item 'Verberg dit venster' om weer terug te gaan naar puur het kijkscherm, zonder het hele programma af te sluiten) en 'Programma afsluiten' (sluit de HELE applicatie netjes af, inclusief de automatische backup - géén login nodig, want dit is geen bouwscherm-actie). Verder een rij met vijf knoppen: Go (laat alle wachtende treinen meteen vertrekken), Opstellen (stuurt elke geplaatste loc naar het dichtstbijzijnde vrije opstelspoor), Stop (NOODSTOP: stopt ALLE rijdende treinen direct, ook onderweg - inclusief een expliciet stopcommando naar elke bekende, geplaatste loc, voor het geval er nog eentje buiten de software om doorreed), 'Rustig stoppen' (GEEN noodstop: elke rijdende trein maakt zijn huidige, al ingezette stap gewoon af en stopt daarna netjes, in plaats van halverwege een overgang te worden afgebroken), en 'Loc uit blok halen' (haalt de in het Overzicht locomotieven GESELECTEERDE loc van zijn blok af, bijv. omdat hij defect is - stopt automatisch eerst een eventuele lopende rit; de loc zelf blijft gewoon in de lijst staan met blok '-', er verdwijnt dus niets uit de treinenlijst zelf), en een Simulatiesnelheid-keuze (Snel/Normaal/Langzaam/Zeer langzaam) - handig als het allemaal te snel gaat om goed te volgen, werkt direct door, ook terwijl er al treinen rijden. Ernaast: 'Standaard uitroltijd bij stoppen' (zie het onderwerp Uitroltijd) en 'Geluiden aan' - Koploper's eigen 'alle geluiden en rookgeneratoren van treinen in- of uitschakelen', schakelt de .wav-geluiden van loc-functies globaal uit/aan.",
            "Onder het Overzicht locomotieven staat een sectie 'Handmatig rijden' - selecteer een geplaatste loc, kies Vooruit/Achteruit en schuif de stap-regelaar om de decoder rechtstreeks aan te sturen (los van de route-gebaseerde simulatie), net als Koploper's eigen 'rijwindow' van een loc. Vereist een decoderadres (Treinen beheren) en werkt alleen daadwerkelijk met echte, verbonden hardware - zonder hardware gaan de commando's nergens naartoe (net als bij Wisselstraten/Seinen).",
            "Direct boven 'Handmatig rijden' staan drie knoppen die werken op de in het Overzicht locomotieven GESELECTEERDE loc, zonder de andere treinen te beïnvloeden: 'Pauzeren' stopt alleen die ene loc - het blok blijft bezet en alle reserveringen verderop blijven gewoon staan, andere treinen rijden door. 'Hervatten' laat 'm daarna exact vanaf hetzelfde punt weer verder rijden. 'Reservering opheffen' is het alternatief voor Hervatten: de loc blijft op zijn huidige blok staan, maar alle nog niet bereikte, gereserveerde blokken verderop komen vrij en de geplande rit vervalt - kan dan niet meer hervat worden, wel een nieuwe rit starten vanaf waar de loc nu staat. Dit is dus iets anders dan 'Loc uit blok halen' hierboven, dat de loc ook echt van de baan af haalt.",
            "Je kunt het kijkscherm en het gewone baanontwerp tegelijk open hebben - bijvoorbeeld bewerken op het ene scherm, live meekijken op het andere."
        }),

        new Onderwerp("Kleuren op de baan", new[]
        {
            "De kleur van een blok of spoorlijn vertelt je de status:",
            "- Vrij (standaardkleur): niets aan de hand.",
            "- Bezet (rood): er staat een trein.",
            "- Gereserveerd (goudkleurig): een rijdende trein is hier onderweg naartoe, maar nog niet aangekomen.",
            "- Net vrijgegeven (groen, kort): het blok was net bezet/gereserveerd en is zojuist vrijgekomen - flitst even groen op voordat het teruggaat naar de normale kleur.",
            "- Handmatig bezet (bruin): jij hebt het zelf als bezet gemarkeerd, niet een echte trein.",
            "- Staartindicatie: het blok dat een trein net verlaten heeft, nog niet helemaal vrij.",
            "- Foutmelding (paars, knipperend): er is iets mis - bijvoorbeeld een spookmelding.",
            "- Wissels kleuren mee: staat een wissel in een wisselstraat die op dit moment gereserveerd is voor een rijdende trein, dan krijgt de al ingestelde (actieve) poot van die wissel dezelfde goudkleurige Gereserveerd-kleur - zo zie je in één oogopslag in welke richting de wissel voor die reservering ligt.",
            "Via Beheren -> Kleurenschema aanpassen kun je elke kleur zelf naar smaak instellen."
        }),

        new Onderwerp("Bezetmelders en spookmelding", new[]
        {
            "Bezetmelders zijn de 'ogen' van het systeem - ze melden of een stuk spoor bezet is. In het blok-eigenschappenscherm koppel je een meldernummer aan een blok (Voorblok of Stopsectie-rol); in het baanontwerp kun je ditzelfde meldpunt ook als zichtbaar cirkeltje op de tekening plaatsen (Bezetmelder-tool), zodat je in één oogopslag ziet waar een melder ongeveer zit.",
            "### Spookmelding",
            "Als de aangesloten hardware een blok bezet meldt terwijl er geen enkele rit, reservering of handmatige bezetting op dat blok bekend is, herkent het programma dat als een 'spookmelding' - meestal een hardware-glitch (slecht railcontact, een storende decoder). Net als het echte Koploper is dit geen puur informatieve melding: er klinkt een waarschuwingsgeluid en er wordt DIRECT een NOODSTOP geactiveerd - alle rijdende treinen komen acuut tot stilstand, precies zoals bij het indrukken van de NOODSTOP-knop zelf. Pas daarna verschijnt de pop-up. Het blok gaat knipperen (Foutmelding-status) totdat je 'm zelf opheft via 'Foutmelding opheffen', en je moet daarna zelf de gestopte treinen weer laten rijden.",
            "### Vorig blok vrijgeven",
            "Elk bezetmeldpunt heeft een vinkje 'Mag vorig blok vrijgeven' (standaard AAN) - net als Koploper's eigen 'bij de stopmelder heb ik opgegeven dat het vorige blok vrijgegeven mag worden'. Zet je dit UIT, dan blijft het blok dat de trein hiervoor verliet bewust bezet staan zodra deze melder de aankomst meldt, i.p.v. na een korte staart-fase automatisch weer vrij te komen - handig voor bijvoorbeeld een schaduwstation waar je de vrijgave liever pas later, expliciet, wilt laten gebeuren. Zo'n bewust-bezet-gehouden blok komt in elk geval weer vrij zodra de trein zijn eindbestemming bereikt."
        }),

        new Onderwerp("Treinen en treintypes", new[]
        {
            "Via Beheren -> Treintypes beheren stel je de algemene rij-eigenschappen in per type trein: minimum/gemiddelde/maximumsnelheid, massasimulatie (optrek-/remtijd), wachttijd bij keren, vertrekvertraging.",
            "Via Beheren -> Treinen beheren voer je de individuele locomotieven/treinstellen in, los van het treintype:",
            "- Decoderadres: dit nummer toont het programma in de blokjes op het scherm, NIET de naam van de trein - net als in het echte Koploper.",
            "- Decoderstappen (14/28/128): puur informatief, welk type decoder de loc heeft.",
            "- Eigen accel./rem-tijd: als een specifieke loc trager optrekt/afremt dan het treintype voorschrijft, kun je dat hier instellen - het programma gebruikt dan automatisch de TRAAGSTE van loc en treintype samen.",
            "- Rangeer-modus: schakelt de massasimulatie voor deze trein tijdelijk helemaal uit, voor precies manoeuvreren.",
            "- Lengte: gebruikt voor treinlengte-controles per blok en voor het kiezen van de best passende stopsectie/bestemmingsblok.",
            "- Loc-functies: acties (bijv. een fluitsignaal, licht) die automatisch afgaan op een gekozen moment tijdens het rijden (blok verlaten/aankomen/stoppen). Vul een DCC-functienummer (F0=meestal het licht) in om dit ook DAADWERKELIJK naar de decoder te sturen (met echte, verbonden hardware) - kies Aan of Uit per regel, bijv. één regel 'bij VerlaatBlok: F0 aan' en een aparte regel 'bij AankomstEnStopt: F0 uit' om het weer netjes te laten stoppen. Optioneel kun je er ook een .wav-geluidsbestand aan koppelen, dat lokaal op de computer hoorbaar wordt (onafhankelijk van het functienummer).",
            "### Dubbeltractie",
            "Bij Treinen beheren kun je twee treinen koppelen ('baas' + 'knecht'), net als Koploper's dubbeltractie - alleen de baas rijdt daadwerkelijk een route, de knecht komt automatisch mee te staan op precies hetzelfde blok, en wordt ook in het overzicht als 'rijdend' getoond zodra de baas rijdt. Werkt ook voor een keten van meerdere gekoppelde locs (bijv. baas -> knecht 1 -> knecht 2). Ontkoppelen kan met de bijbehorende knop.",
            "### Rijstatistieken per loc",
            "Bij Treinen beheren zie je per loc een cumulatieve teller: totale rijtijd en totale afgelegde afstand - net als iTrain's 'travelled time and distance indicators per loc', handig om bijvoorbeeld onderhoud aan een fysieke loc te plannen op basis van daadwerkelijk gereden tijd, net als bij een echte trein. De rijtijd telt automatisch mee zodra een loc een route rijdt (ongeacht de kijksnelheid-instelling); de afgelegde afstand telt alleen mee voor blokken waar bij de blokeigenschappen een 'werkelijke bloklengte' is ingevuld (standaard 0/onbekend). Met 'Teller resetten' zet je beide terug naar 0, bijvoorbeeld na onderhoud.",
            "### Rangeertreintype",
            "Bij Treintypes beheren staat bovenaan een database-brede instelling: welk treintype als HET rangeertreintype fungeert (net als Koploper's 'Algemeen -> Instellingen per database'). Zet een gebruiker 'Rangeer' aan op een loc (bij het starten van een rit), dan gebruikt de simulatie voor die rit de snelheden van dit aangewezen treintype in plaats van de eigen route/treintype-snelheid - meestal een stuk langzamer, precies zoals een rangeerbeweging in het echt ook trager gaat. Zonder aangewezen rangeertreintype betekent Rangeer alleen 'geen massasimulatie' (het eenvoudigere gedrag).",
            "### Geijkte snelheid",
            "Bij Treinen beheren kun je 'Geijkte snelheid' aanzetten voor een specifieke loc, met een eigen STAPPENTABEL (decoderstap -> km/u) - net als Koploper's 'Onderhouden locomotieven -> Stappen/IJking': de snelheid van een loc wordt in werkelijkheid via decoderstappen geregeld, niet via een simpel Max/Gemiddeld/Min-drietal. 'De instelling van de locomotief bepaalt uiteindelijk welke instellingen gebruikt worden, dit geldt voor de LOCOMOTIEF, niet voor het treintype' - deze tabel gaat dan voor de snelheid van het gekoppelde treintype, voor DEZE ene loc.",
            "Twee manieren om de tabel te vullen: HANDMATIG (per stap een km/u-waarde intypen via 'Toevoegen/wijzigen', of 'Genereer' voor een snel lineair startpunt om daarna bij te stellen), of via een ECHTE METING - net als het echte Koploper: stel eenmalig (bij Beheren -> Algemene instellingen, geldt voor alle locs) de twee bezetmelders en de exacte trajectlengte (mm) ertussen in, klik daarna in dit scherm op 'Start meting', en laat de loc op de te meten decoderstap over dat traject rijden. Zodra de eerste bezetmelder meldt start de tijdmeting, zodra de tweede meldt stopt 'm - de gemeten tijd wordt via de modelschaal (bijv. 87 voor 1:87 H0, 160 voor 1:160 N) omgerekend naar een echte schaalsnelheid, die je met 'Gebruik deze meting' in de tabel zet voor de opgegeven stap. Herhaal dit van de hoogste stap naar beneden, net als in het echt.",
            "Deze meting werkt ALLEEN met echte, aangesloten hardware die de bezetmelders daadwerkelijk stuurt (Beheren -> Hardware-interface) - zonder hardware komt er nooit een bezetmelding binnen, dus blijft 'Start meting' voor altijd wachten. De implementatie is wel al volledig gebouwd en klaar: zodra er hardware is aangesloten, werkt de meting meteen. Tot die tijd blijft de handmatige tabel-invoer de manier om te simuleren. (Rangeren gaat, indien aangezet, voor geijkte snelheid.)",
            "### Treintype per bloktype",
            "Bij Treintypes beheren zit een tabblad 'Treintype per bloktype' - hier stel je, net als in het echte Koploper, per bloktype (Normaal/Station/Kopspoor/Opstelspoor) een stopkans en een minimale/maximale wachttijd in. Station staat standaard op 100% (stopt altijd, matcht het oude gedrag); zet de stopkans voor een treintype op 0% bij Station om dat treintype een station gewoon te laten voorbijrijden zonder te stoppen - het klassieke voorbeeld van een goederentrein die niet stopt op een station waar wel personentreinen stoppen."
        }),

        new Onderwerp("Treinroutes - vast rijden", new[]
        {
            "Open Vensters -> Treinroutes. Klik 'Nieuwe route' en klik daarna de blokken aan in de volgorde waarin de trein moet rijden (Escape rondt af, of klik 'Klaar').",
            "### Rijden",
            "Selecteer de route en druk op 'Start rijden'. Kies eventueel eerst een trein in de keuzelijst om de decoderadres/massasimulatie/lengte van die specifieke loc mee te nemen.",
            "### Belangrijke instellingen per route",
            "- Blijf wachten in bestemming / Opstellen: het eindblok blijft bewust bezet in plaats van meteen weer vrij te worden.",
            "- Hoge prioriteit: gaat voor bij een wachtrij.",
            "- Herhalen (pendeldienst): de trein rijdt automatisch een ECHTE terugrit terug naar het startblok zodra hij is aangekomen, en herhaalt dit.",
            "- Bestemmingsblok: bewaakt dat de route ook echt daar eindigt (nodig voor Herhalen).",
            "- Geldt voor: bepaalt WELKE trein(en) met deze route mogen rijden (alle, een treintype, een specifieke trein, ...).",
            "- Max. snelheid voor deze route: een derde, eigen snelheidslimiet naast blok en treintype - Koploper's eigen regel: 'de toegestane snelheid is de laagste snelheid die geldt voor rijweg, locomotief of treinsoort'. 0 = geen eigen limiet.",
            "- Mag rijden in dagdeel: AM/PM/Beide - de route weigert te starten buiten het gekozen dagdeel (zie het onderwerp Dagdeel).",
            "- Uitgesloten wissels: wissels die deze route bewust NIET automatisch omzet.",
            "- Alternatieve startblokken: net als Koploper's 'soms is het handig als een vaste treinroute vanuit meerdere richtingsblokken gestart kan worden' - staat de gekozen trein bij het starten op een van deze blokken i.p.v. op het letterlijke Startblok, dan wordt de route gewoon vanaf DAT blok gereden (de rest van het pad blijft ongewijzigd van toepassing)."
        }),

        new Onderwerp("Uitroltijd (berekende stopplaats)", new[]
        {
            "Het echte Koploper kent 'berekende stopplaats na... cm' (Algemeen -> Instellingen per database -> Bestemming/Snelheid): een loc bereikt de stopmelder al op minimumsnelheid, maar staat dan nog niet écht stil - er is nog een fysieke uitrolafstand nodig (advies daar: 10 cm voor N-schaal, ca. 18 cm voor H0).",
            "Onze simulatie is TIJD- in plaats van afstand-gebaseerd, dus dit is het tijd-equivalent: hoe lang het na het bereiken van de stopmelder nog duurt voordat de trein écht stilstaat. In te stellen in het kijkscherm ('Standaard uitroltijd bij stoppen') - geldt voor de hele database.",
            "Een individueel blok kan deze standaardwaarde overschrijven via zijn eigen blokeigenschappen ('Uitroltijd bij stoppen', -1 = gebruik de standaardwaarde, 0 is een geldige eigen keuze voor 'dit blok heeft geen uitroltijd nodig').",
            "### Algemene instellingen",
            "Via Beheren -> Algemene instellingen (net als Koploper's 'Algemeen -> Instellingen per database') staan alle database-brede instellingen bij elkaar: de standaard uitroltijd hierboven, de rustpauze tussen wissel/sein-commando's (voorkomt dat de digitale bus overbelast raakt als er meerdere tegelijk moeten worden omgezet), geluiden aan/uit, en de snelheidsmeting-instellingen (bezetmelders/trajectlengte/modelschaal voor geijkte snelheden) - dat laatste hoef je dus maar ÉÉN keer in te stellen, niet opnieuw per loc."
        }),

        new Onderwerp("Incidenten en wijzigingen", new[]
        {
            "Via Beheren -> Incidenten en wijzigingen kun je een onderhoudslog bijhouden per object - los te koppelen aan bijv. 'Wissel 12', 'Loc NS 1263' of 'Blok 34' (vrije tekst, geen vaste koppeling aan een specifiek modelobject - de log blijft dus ook werken als je het object later hernoemt of verwijdert). Dit scherm is ook rechtstreeks vanuit het kijkscherm te openen (menu bovenin, geen inloggen nodig), zodat ook eindgebruikers kunnen zien wat er aan de baan gebeurd is - en zelf een incident kunnen melden terwijl ze de baan bedienen.",
            "Een INCIDENT is iets dat kapot/mis is (een storing, onverwacht gedrag, een defect); een WIJZIGING is iets dat je bewust hebt aangepast, vervangen of onderhouden. Beide krijgen een titel, omschrijving, datum en een 'Afgerond'-status (voor een incident: opgelost; voor een wijziging: uitgevoerd i.p.v. alleen gepland).",
            "Typ in het zoekveld bovenin een objectnaam om snel de geschiedenis van dat ene object te zien. De log is bewust append-only (geen 'wijzigen'-knop) - aanpassen kan door een registratie te verwijderen en opnieuw toe te voegen.",
            "Zowel bij een loc (Treinen beheren) als bij een incident/wijziging kun je een foto koppelen (alleen een bestandspad, geen ingebedde afbeelding - houdt het projectbestand compact)."
        }),

        new Onderwerp("Snelle klok en dienstregeling", new[]
        {
            "Bovenin het Treinroutes-venster staat een klok (eigen functie, geen Koploper-begrip) - een 'snelle klok' die sneller loopt dan de werkelijke tijd, zodat een realistische dienstregeling toch in een overzienbare speelsessie past. Zet 'm aan met 'Klok loopt', en stel de snelheid in via 'sec/modelmin' (hoeveel echte seconden er verstrijken per gesimuleerde modelminuut - lager is sneller, de standaard 10 komt neer op 6x zo snel als echt).",
            "Bij een route kun je een 'Geplande vertrektijd' instellen (uu:mm, leeg = geen dienstregeling voor deze route). Zodra de klok dit tijdstip bereikt, probeert het programma die route automatisch te starten met de trein die op dat moment op het startblok staat. Lukt dat niet (blok bezet/gereserveerd, geen trein aanwezig), dan wordt de poging simpelweg overgeslagen en gelogd - er komt geen wachtrij, en de route wordt pas de volgende dag (na middernacht op de klok) weer geprobeerd.",
            "De klok zelf en de kloksnelheid worden opgeslagen bij het project; de 'loopt'-status niet - na het laden staat de klok altijd stil, zodat een net-geopend project niet meteen ongevraagd routes gaat starten."
        }),

        new Onderwerp("Donkere modus, zoeken, ongedaan maken en dashboard", new[]
        {
            "Donkere modus (kijkscherm-menu) past de achtergrond en spoorkleur aan - prettiger bij een gedimde treinkamer.",
            "De zoekbalk in het baanontwerp vindt een blok (nummer), wissel/sein (adres) of loc (naam/decoderadres) en springt er direct naartoe, ook als dat op een ander tabblad staat.",
            "'Toon vrije opstelsporen' (naast de zoekbalk) laat zien welke Opstelspoor-blokken op dit moment niet bezet, gereserveerd of vergrendeld zijn - handig bij het wegzetten van meerdere locs aan het eind van een sessie. Bij precies één resultaat springt het scherm er meteen naartoe.",
            "Onderin het kijkscherm staat een brede, lage statistiekgrafiek (laatste 5 minuten): geel is het actuele aantal rijdende locs, blauw het aantal locs dat stilstaat in een blok, en de witte gestreepte lijn het lopende daggemiddelde van het aantal rijdende locs (sinds middernacht, reset vanzelf bij een dagwissel).",
            "Ongedaan maken (Ctrl+Z) geeft het laatst VERWIJDERDE symbool terug; Opnieuw (Ctrl+Y) verwijdert 'm weer. Dit is bewust beperkt tot verwijderen (niet tot verplaatsen of andere bewerkingen) - dat is de veiligste, meest voorkomende 'oeps'-situatie om terug te draaien.",
            "Het Dashboard (Beheren-menu, of vanuit het kijkscherm) geeft in één oogopslag een overzicht: aantallen blokken/wissels/seinen/routes/locs, de totale rijtijd/afstand van alle locs samen, het aantal open incidenten, een signalering van objecten met 3 of meer gemelde incidenten (mogelijk een chronisch probleem), een lijst met locs die hun ingestelde onderhoudsdrempel (uren rijtijd, in te stellen bij Treinen beheren) hebben bereikt, en (alleen vanuit het blokkenschema zelf, niet vanuit het kijkscherm) een lijst van treinen die al langer dan 10 minuten geen enkele voortgang boeken - handig om een vastgelopen of vergeten rit op te sporen. Onderin staat een 'Exporteer als tekstbestand...'-knop om het hele overzicht te bewaren of te delen.",
            "'Controleer mijn baan' (Beheren-menu) scant op veelvoorkomende configuratiefouten: blokken zonder bezetmelder, geïsoleerde blokken zonder enige verbinding, wissels die nergens aan verankerd zijn, routes die niet bij hun vastgelegde bestemmingsblok uitkomen, en dienstregelingsconflicten (twee routes met dezelfde geplande vertrektijd vanaf hetzelfde startblok, die dus nooit allebei kunnen starten). Puur signalerend - lost niets automatisch op.",
            "Rechtsklik in het kijkscherm op een blok (niet op een wissel, die heeft daar al zijn eigen rechtsklik-gedrag) opent een volledig, alleen-lezen overzicht van dat blok (eigenschappen, bezetmelders, actuele status, en - als er een loc op staat - ook diens gegevens en rijstatus, verdeeld over tabbladen). Daarin staat ook een knop 'Onderhoud melden voor dit blok...' - dat opent de onderhoudslog met het objectveld al ingevuld, handig om ter plekke een incident te melden voor precies dat blok.",
            "Via Help -> Over Modeltreinbesturing (ook vanuit het kijkscherm) zie je het versienummer en een link naar de broncode.",
            "'Test alle wissels' (in het baanontwerp-bewerkscherm, náást 'Wisselstraten beheren') zet elke wissel op de baan achtereenvolgens om, met een instelbare pauze ertussen - handig om na het aansluiten van hardware in één keer te controleren of alles bedraad is.",
            "'Beursmodus' (kijkscherm) is bedoeld voor onbemand doordraaien op een tentoonstelling of clubavond: elke 15 seconden krijgt elke geplaatste loc die stilstaat automatisch (zonder melding) een nieuwe rit - net als steeds zelf op 'Go' klikken, maar dan vanzelf herhaald.",
            "Bij Treinen beheren staat naast de gewone (technische) export ook 'Materieellijst (CSV)...' - een leesbaar overzicht van je hele wagenpark, te openen in Excel of een vergelijkbaar programma, voor administratie.",
            "Naast 'Geluiden aan' in het kijkscherm staat een volumeregelaar - een simpele aan/uit-schakelaar bleek niet genoeg, soms wil je gewoon wat zachter.",
            "Bij de blokeigenschappen kun je een blok 'Tijdelijk vergrendelen' (met een optionele reden) - handig als je bijv. rails aan het schoonmaken of repareren bent. Geen enkele automatische rit of pathfinding stuurt dan nog een trein naar dat blok, maar het telt NIET als een storing (Foutmelding) in de onderhoudslog. Handmatig een loc erop plaatsen blijft wel mogelijk."
        }),

        new Onderwerp("Automatisch rijden", new[]
        {
            "Vink 'Automatisch rijden' aan bij het aanmaken van een route. De trein kiest dan zelf, bij elke splitsing, het eerste vrije en toegestane vervolgblok - je hoeft geen vast pad op te geven.",
            "Als er meerdere gelijkwaardige bestemmingsblokken mogelijk zijn (bijvoorbeeld een schaduwstation met parallelle opstelsporen), kiest het programma niet zomaar het eerste vrije blok, maar het blok waarvan de stopsectie het best (krapst) bij de lengte van de trein past - 'optimale lengte', net als het echte Koploper. Zitten de daadwerkelijke, lengte-beperkte opstelsporen niet direct bij de splitsing zelf maar een blok verderop (bijv. achter een tussenliggend wisselstraat-blok), dan kijkt het programma ook nog een stap verder om alsnog de beste keuze te maken - Koploper's eigen schaduwstation-voorbeeld beschrijft dit letterlijk als 'twee blokken vooruit kijken'.",
            "Om te voorkomen dat een trein blijft heen-en-weer pendelen tussen twee blokken, gaat de trein alleen terug naar het blok waar hij net vandaan kwam als dat werkelijk de ENIGE optie is.",
            "Stel een Bestemmingsblok in om de automatische rit netjes te laten eindigen (bijvoorbeeld nodig voor Herhalen)."
        }),

        new Onderwerp("Beperkingen en blokgroepen", new[]
        {
            "### Richtingsverbod",
            "Verbiedt een specifieke overgang tussen twee blokken (optioneel alleen als de trein uit een bepaald derde blok komt). Optioneel ook alleen voor een gekozen treintype - laat je dat leeg, dan geldt het verbod voor alle treintypes.",
            "### Stopverbod",
            "Blokken waar een trein onderweg niet mag stoppen (maar wel doorheen mag rijden). Ook hier optioneel te beperken tot één treintype.",
            "### Stopverbod stilstand",
            "Net iets milder: de route mag hier niet EINDIGEN, maar gewoon doorrijden mag wel. Ook hier optioneel te beperken tot één treintype.",
            "### Blokgroepen",
            "Bundel onderling gelijkwaardige blokken (bijv. parallelle opstelsporen in een schaduwstation) tot een groep met 'enkele treinbeweging': er mag dan maar in één blok van de HELE groep tegelijk een trein actief zijn, ook als een specifiek blok daarbinnen zelf toevallig vrij is. In te stellen via Beheren -> Blokgroepen beheren.",
            "### Acties",
            "Een eenvoudige automatisering, geïnspireerd op Koploper's uitgebreidere Sequens-systeem: laat een blok-gebeurtenis automatisch een Schakelaar op de baan omzetten. Vier mogelijke gebeurtenissen: 'Bezet'/'Vrij' (bijv. een 'vertrek toegestaan'-lampje dat aangaat zodra een trein een blok binnenrijdt) en 'Gereserveerd'/'Niet meer gereserveerd' (bijv. een overweg: die moet al dicht zijn VOORDAT de trein er is, dus reageert op de reservering van het blok - precies zoals het echte Koploper een overweg beschrijft: 'zodra de trein het blok reserveert schakelt de decoder in'). Optioneel 'Puls (sec)': net als een Koploper-praktijkvoorbeeld (RhB-Modelbaan, stationsomroep) waar de schakelaar een externe geluidsmodule aanstuurt en na 0,5 sec zichzelf weer terugzet - veel geluidsmodules/relais reageren op zo'n korte, momentane puls, niet op een blijvend signaal. 0 (de standaard) betekent gewoon een blijvende stand, zoals voorheen. In te stellen via Beheren -> Acties beheren; er moet al minstens één Schakelaar-symbool op de baan staan.",
            "### Overzicht beperkingen",
            "Via Beheren -> Overzicht beperkingen zie je in één alleen-lezen scherm alle richtingsverboden, stopverboden, stopverbod-stilstand-regels en uitgesloten wissels per route bij elkaar."
        }),

        new Onderwerp("Dagdeel (AM/PM)", new[]
        {
            "Bovenin het Treinroutes-venster staat een eenvoudige, versnelde klok met een AM/PM-aanduiding (elke seconde in het echt is één gesimuleerde minuut). Klik 'Wissel AM/PM' om handmatig naar het andere dagdeel te springen.",
            "Elke route kan ingesteld worden op 'Beide', 'AM' of 'PM' (bij de route-instellingen). Een route die alleen 'AM' toegestaan is, weigert te starten als het dagdeel PM is, en omgekeerd - precies zoals het echte Koploper het dagdeel gebruikt om te bepalen welke pendeltrein uit een schaduwstation mag vertrekken."
        }),

        new Onderwerp("Noodstop en spanning", new[]
        {
            "### Noodstop",
            "De rode 'NOODSTOP (alles stoppen)'-knop in Treinroutes stopt onmiddellijk ELKE rijdende trein, ongeacht welke route geselecteerd is, en zet Herhalen bewust uit. Ook te bereiken met Escape (buiten het opbouwen van een route) of F6 (Koploper's eigen sneltoets). De noodstop wordt ook naar aangesloten hardware gestuurd, zelfs als de software zelf geen enkele trein als actief bijhoudt.",
            "### Spanning aan/uit (het 'spiegelei')",
            "De ronde groene/rode knop helemaal links in de werkbalk zet de spanning aan/uit, net als in het echte Koploper. Zolang er een trein rijdt, is deze knop grijs en niet te bedienen - je kunt de spanning pas uitzetten als alles is gestopt."
        }),

        new Onderwerp("Hardware aansluiten", new[]
        {
            "Via Beheren -> Hardware-interface kies je een COM-poort en het type centrale: Dinamo/VPEB, Uhlenbrock Intellibox (LocoNet) of DCC-EX (de open-source/DIY-centrale). Zonder verbinding werkt alles gewoon in software-simulatie (het standaard gedrag, ook prima om zonder hardware te oefenen).",
            "Bij een succesvolle verbinding worden alle wissels met 'altijd initialiseren' aangevinkt automatisch opnieuw naar de hardware gestuurd, zodat software en fysieke wissel gegarandeerd synchroon lopen.",
            "Belangrijk: dit is nog niet tegen echte fysieke apparatuur getest. Test voorzichtig en begin met één wissel.",
            "### Decoder-vertraging: cruciaal voor goed afremmen",
            "Zoals het echte Koploper's eigen handleiding het stelt: 'het is belangrijk dat je de afrem- en optrekvertraging van de decoder in de locomotieven op de MINIMALE waarde instelt, alleen dan kan Koploper de treinen goed laten afremmen' - je kunt eigenlijk stellen dat Koploper (en dus ook dit programma) de vertraging van optrekken en afremmen zelf moet bepalen (via de massasimulatie/Accelaratie- en Rem-velden bij Treintypes/Treinen beheren), niet de decoder. Staat de decoder's EIGEN optrek-/afremvertraging (CV3/CV4) nog op een hoge waarde, dan gaan de twee vertragingen elkaar tegenwerken en klopt het rijgedrag niet meer met wat hier is ingesteld - zet die CV's dus op de decoder zelf op minimaal voordat je met echte hardware gaat rijden.",
            "### Cruise-control: vereist voor schaalsnelheid",
            "Kies decoders met CRUISE-CONTROL (ook wel belastingcompensatie/BEMF genoemd) - een optie die de motor bij wisselende belasting (bijv. bergop, of meer wagons) toch met een gelijk toerental laat lopen, dus een constante snelheid. Dit is een MUST als je treinen op schaalsnelheid wilt laten rijden en mooi wilt laten optrekken/afremmen (zie ook Geijkte snelheid) - decoders zonder cruise-control geven problemen, omdat de werkelijke snelheid dan wisselt met de belasting, terwijl dit programma uitgaat van een vaste, voorspelbare snelheid per decoderstap.",
            "### Snelheidsmeting: lengte van het meettraject",
            "Voor het ijken van locomotieven (zie Geijkte snelheid) is een stuk spoor van vaste lengte nodig, met aan beide uiteinden een even lang meldstuk - typisch tussen de 50 en 100 cm, zoals Koploper's eigen documentatie het stelt. Te kort geeft een onnauwkeurige tijdmeting, te lang kost onnodig veel spoorlengte en tijd per meting."
        }),

        new Onderwerp("Rijden met echte hardware: geleerd gedrag", new[]
        {
            "De laatst gebruikte hardware-keuze (interface + COM-poort) wordt machinebreed onthouden en bij het opstarten automatisch opnieuw geprobeerd - je hoeft dus niet elke keer opnieuw te verbinden. Wissel je bewust terug naar Simulatie, dan blijft dat ook zo bij de volgende start.",
            "### Geen aannames, alleen feiten",
            "Bij echte hardware wacht de simulatie ALTIJD op een echte bezetmelding voordat een blok als bereikt/vrij wordt beschouwd - er wordt nergens 'gewoon aangenomen' dat iets gelukt is na een bepaalde tijd. Blijft een verwachte melding te lang uit, dan verschijnt een duidelijke, blijvende foutmelding ('trein X staat stil in blok Y', net als een spookmelding: knippert totdat je 'm zelf opheft of de melding alsnog binnenkomt) - de rit pauzeert dan, in plaats van dat het scherm een beweging toont die er misschien niet is.",
            "### Rijrichting per loc",
            "Rijdt een loc fysiek de verkeerde kant op (bijv. omdat hij andersom op de rails is gezet, of de decoder's CV29 andersom staat)? Bij Treinen beheren staat een vinkje 'Decoder is andersom geprogrammeerd' dat dit puur in software compenseert, zonder de decoder zelf te hoeven herprogrammeren.",
            "Rijdt een loc onverwacht terug het blok in waar hij net vandaan kwam (i.p.v. vooruit verder), dan toont het programma daarvoor een aparte, herkenbare waarschuwing (in plaats van een generieke spookmelding) - dat wijst meestal op precies zo'n omgekeerde rijrichting.",
            "### Geleerde reistijden",
            "Voor elke combinatie van (blok, aankomstrichting, loc) onthoudt het programma zelf hoe lang een echte overgang duurde (een voortschrijdend gemiddelde) - een goederenloc en een hogesnelheidstrein krijgen zo elk hun eigen, realistische verwachting, in plaats van één gedeeld gemiddelde. Bekijken via Beheren -> Geleerde reistijden bekijken; wissen (bijv. na een rare, afwijkende meting) via Beheren -> Geleerde reistijden wissen.",
            "### Bezetmelder-volgorde: handmatig of zelflerend",
            "Bij de blokeigenschappen kun je, per aankomstrichting ('Uit blok'), een geordende reeks meldernummers opgeven - nodig omdat de sensoren die afgaan (en in welke volgorde) kunnen verschillen per aanrijroute naar hetzelfde blok. BELANGRIJK: de volgorde moet de ECHTE, FYSIEKE volgorde zijn waarin de sensoren afgaan (eerste = direct bij binnenkomst, laatste = pas bij het echte eindpunt/stootblok) - dit bepaalt hoe het programma 'aankomst bevestigd' en 'veilig om te keren' uit elkaar houdt, en is dus NIET per se hetzelfde als hoe Koploper's eigen, gelijknamige 'Bezetmeldingen'-tabblad een soortgelijke lijst gebruikt. Twijfel je over de juiste volgorde, laat het veld dan leeg: zodra de meldernummers zelf al aan het blok gekoppeld zijn (via de gewone Bezetmeldpunten-lijst, zonder volgorde), leert het programma de volgorde vanzelf uit wat er tijdens het echte rijden binnenkomt - dat kan nooit verkeerd om zijn. Een handmatig ingevoerde volgorde wordt nooit automatisch overschreven, dus als die later toch niet blijkt te kloppen, moet je 'm zelf aanpassen of verwijderen (dan neemt het zelflerende systeem het weer over)."
        }),

        new Onderwerp("Accounts en toegang", new[]
        {
            "Het kijkscherm is voor iedereen vrij toegankelijk, zonder wachtwoord - dat blijft altijd zo. De bouwschermen (blokkenschema, baanontwerp bewerken, treinroutes) zijn wel beveiligd: daarvoor moet je inloggen met een account.",
            "### Twee soorten accounts",
            "- Beheerder: kan de bouwschermen gebruiken EN zelf nieuwe accounts aanmaken/verwijderen (via Beheren -> Gebruikers beheren, alleen zichtbaar/bruikbaar als je zelf als beheerder bent ingelogd).",
            "- Gewone gebruiker: kan de bouwschermen gebruiken en daar aanpassingen maken, maar kan geen andere accounts beheren.",
            "### Eerste keer",
            "Bij het aanmaken van een nieuwe database (Bestand -> Nieuw) wordt meteen gevraagd om de eerste (echte) beheerder aan te maken - een database zonder minstens één beheerder kan niet, anders zou niemand ooit nog een account kunnen toevoegen.",
            "### Inloggen",
            "Zodra je vanuit het kijkscherm een bouwscherm probeert te openen (via het menu 'Bouwschermen'), verschijnt er een inlogscherm. Je hoeft dit maar één keer per sessie te doen - daarna blijf je ingelogd totdat je het programma afsluit.",
            "Wachtwoorden worden nooit als platte tekst bewaard, alleen een veilige hash (PBKDF2 met een eigen willekeurige salt per account)."
        }),

        new Onderwerp("Sneltoetsen", new[]
        {
            "### Algemeen (alle vensters)",
            "- Ctrl+D: geselecteerd symbool dupliceren (baanontwerp).",
            "- Delete: geselecteerd item verwijderen.",
            "- Escape: actie annuleren / noodstop (afhankelijk van venster en context).",
            "### Baanontwerp",
            "- Rechtsklik wissel: richting 45° draaien.",
            "- Shift+rechtsklik wissel: afbuiging spiegelen.",
            "- Ctrl+rechtsklik wissel: 'altijd initialiseren' aan/uit.",
            "- Shift+dubbelklik blok: naar volgend tabblad.",
            "- Shift+dubbelklik wissel: adres instellen.",
            "- Shift+dubbelklik perron: breedte/hoogte instellen.",
            "### Treinroutes",
            "- Escape (buiten route opbouwen): noodstop.",
            "- F6: noodstop (Koploper's eigen sneltoets)."
        }),

        new Onderwerp("Tips en bekende beperkingen", new[]
        {
            "- Een lijn kan aan een net-niet-45°-ankerpunt vastklikken - dit is bekend gedrag, klik precies op het gewenste ankerpuntje (zichtbaar als klein blokje) om het te vermijden.",
            "- 'Wisselstraat langer bezet houden' is een benadering (hergebruikt de staart-timer) en geen exacte lock.",
            "- Foutmelding wordt automatisch gezet bij een spookmelding, maar moet je zelf weer opheffen.",
            "- Er wordt automatisch een backup gemaakt bij het NETJES afsluiten van het programma (via Bestand -> Programma afsluiten, of het venster gewoon sluiten), en bij opstarten wordt de meest recente automatisch geladen. Belangrijk: als je het programma via Visual Studio's rode stopknop afbreekt tijdens het debuggen, wordt GEEN backup gemaakt - dat is een hard afgebroken proces zonder opruimcode. Gebruik Bestand -> Programma afsluiten als je zeker wilt zijn van een backup.",
            "- Bij een build-fout in Visual Studio die niets met code te maken heeft (bijv. 'kan bestand niet kopiëren'): sluit eerst alle draaiende Modeltreinbesturing.exe-processen via Taakbeheer voordat je opnieuw bouwt."
        })
    };
}
