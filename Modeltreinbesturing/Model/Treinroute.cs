namespace Modeltreinbesturing.Model;

/// <summary>
/// Laag 4: een vaste treinroute. Legt alleen het startblok en de keuzes op splitsingen
/// vast - een rechte lijn zonder keuze wordt tijdens het rijden automatisch afgeleid
/// uit de BlokRelaties (laag 1), precies zoals in de echte Koploper-handleiding staat.
/// Dat is bewust: er is dan niets om te vergeten toe te voegen bij een rechte lijn.
/// </summary>
public class Treinroute
{
    public string Omschrijving { get; set; } = "Route";
    public required Blok Startblok { get; set; }

    /// <summary>Koploper: "soms is het handig als een vaste treinroute vanuit meerdere
    /// richtingsblokken gestart kan worden" - extra, gelijkwaardige startblokken naast
    /// Startblok zelf, voor als er meerdere fysieke ingangen naar dezelfde eerste stap van
    /// de route leiden (bijv. een spoor dat vanuit twee kanten dezelfde wisselstraat in
    /// kan). Staat de gekozen trein bij het starten van de rit op een van deze blokken
    /// i.p.v. op Startblok zelf, dan wordt de route gewoon vanaf DAT blok opgebouwd - de
    /// rest van de route (Keuzeblokken) blijft hetzelfde, ervan uitgaande dat het
    /// alternatieve blok verderop weer in hetzelfde pad uitkomt. Leeg (de standaard)
    /// betekent: alleen het letterlijke Startblok telt, zoals voorheen.</summary>
    public List<Blok> AlternatieveStartblokken { get; set; } = new();

    /// <summary>De op splitsingen gekozen vervolgblokken, in de volgorde waarin de route ze tegenkomt.</summary>
    public List<Blok> Keuzeblokken { get; set; } = new();

    /// <summary>"Geldt voor: Treintype" uit Koploper's Stamgegevens-tabblad. Optioneel - zonder
    /// treintype gebruikt de simulator de standaard (referentie)tijden.</summary>
    public Treintype? Treintype { get; set; }

    /// <summary>Maximumsnelheid (km/u) voor DEZE route zelf - los van de snelheidslimieten
    /// per blok en per treintype. Matcht Koploper's eigen regel: "de toegestane snelheid is
    /// de laagste snelheid die geldt voor rijweg, locomotief of treinsoort" - dus een extra,
    /// derde plafond bovenop de twee die we al hadden. 0 (de standaard) = geen eigen limiet,
    /// alleen blok/treintype tellen dan mee, precies het oude gedrag.</summary>
    public double MaxSnelheid { get; set; }

    /// <summary>"Blijf wachten in bestemming" uit Stamgegevens: het eindblok van deze route
    /// blijft na aankomst bewust bezet staan (net als bij Opstellen), in plaats van meteen
    /// weer vrijgegeven te worden - zonder dat je daar de aparte Opstellen-knop voor nodig hebt.</summary>
    public bool BlijfWachtenInBestemming { get; set; }

    /// <summary>"Hoge prioriteit" uit Stamgegevens: als deze route niet kan starten omdat het
    /// pad bezet is, mag hij in de wachtrij vóór routes zonder hoge prioriteit.</summary>
    public bool HogePrioriteit { get; set; }

    /// <summary>"Dagdeel" (echt Koploper-begrip): "De waarde van de logische actie
    /// 'Dagdeel' wordt ook gebruikt bij de keuze van de pendeltrein uit het
    /// schaduwstation." Beperkt WANNEER deze route mag starten, onafhankelijk van
    /// Geldigheid (die gaat over WELKE trein, dit gaat over WANNEER op de dag).</summary>
    public DagdeelGeldigheid DagdeelGeldigheid { get; set; } = DagdeelGeldigheid.Beide;

    /// <summary>Geplande vertrektijd voor de tijdklok-gestuurde dienstregeling (eigen
    /// functie, geen Koploper-begrip) - null (de standaard) betekent geen dienstregeling
    /// voor deze route, dan blijft alles zoals voorheen (handmatig of automatisch starten
    /// zonder tijdstip). Staat dit WEL, dan probeert de snelle klok deze route automatisch
    /// te starten zodra de klok dit tijdstip bereikt (mits het startblok op dat moment vrij
    /// is - anders wordt het gewoon overgeslagen, geen wachtrij).</summary>
    public TimeSpan? GeplandeVertrektijd { get; set; }

    /// <summary>"Bestemmingsblok" uit Stamgegevens: optionele controle dat deze route ook
    /// daadwerkelijk daar eindigt. Puur ter bewaking - het echte pad wordt nog steeds
    /// afgeleid uit Startblok+Keuzeblokken, dit veld verandert dat pad niet zelf.</summary>
    public Blok? Bestemmingsblok { get; set; }

    /// <summary>Automatisch rijden: negeert Keuzeblokken volledig. Bij elke splitsing kiest
    /// de trein tijdens het rijden zelf het eerste vrije en toegestane vervolgblok (niet
    /// bezet/gereserveerd, geen richtingsverbod, geen stopverbod) - is er geen enkele
    /// beperking op een blok ingesteld, dan is dat blok standaard toegestaan.</summary>
    public bool Automatisch { get; set; }

    /// <summary>"Info"-tabblad uit Stamgegevens: vrije notitie, bijv. om vast te leggen
    /// waarom deze route/regel bestaat. Puur documentatie, geen effect op het rijden.</summary>
    public string Info { get; set; } = "";

    /// <summary>"Geldt voor" uit Stamgegevens - bepaalt VOOR WELKE TREINEN deze route
    /// (regel) gebruikt mag worden, zie GeldigheidsSoort voor de betekenis per soort.</summary>
    public GeldigheidsSoort Geldigheid { get; set; } = GeldigheidsSoort.Alle;

    /// <summary>Bij Geldigheid=Treintype of Treintype/Rijwindow: voor welke treintypes deze
    /// route geldt (alle treinen van dat type, tenzij ook GeldigeTreinen een aanvullende
    /// beperking oplegt bij Treintype/Rijwindow).</summary>
    public List<Treintype> GeldigeTreintypes { get; set; } = new();

    /// <summary>Bij Geldigheid=Locomotief, Rijwindow of Treintype/Rijwindow: voor welke
    /// individuele treinen deze route geldt.</summary>
    public List<Trein> GeldigeTreinen { get; set; } = new();

    /// <summary>"Uitgesloten wissels" uit het Divers-tabblad: deze wissels worden door deze
    /// route NIET automatisch omgezet, ook al liggen ze op het pad - bijv. omdat ze
    /// handmatig op een vaste stand moeten blijven staan.</summary>
    public List<Wissel> UitgeslotenWissels { get; set; } = new();

    /// <summary>Herhalen: na een natuurlijke aankomst (dus NIET bij Opstellen/Blijf wachten
    /// in bestemming, en niet bij handmatig stoppen) rijdt de trein via een automatisch
    /// gegenereerde TERUGRIT (automatisch rijden, met dit route se Startblok als
    /// bestemming) weer terug naar het startpunt - géén teleportatie. Pas als die terugrit
    /// daadwerkelijk is aangekomen, begint de heenroute opnieuw. De terugrit mag via
    /// dezelfde weg terug gaan of via een andere weg - net zoals automatisch rijden dat
    /// altijd doet: het eerste vrije/toegestane vervolgblok.</summary>
    public bool Herhalen { get; set; }

    /// <summary>Wordt alleen door de simulator zelf gezet op een automatisch aangemaakte
    /// terugrit-route (zie Herhalen) - nooit door de gebruiker. Voorkomt dat een terugrit
    /// zelf ook weer een terugrit van een terugrit zou genereren.</summary>
    public bool IsAutomatischeTerugrit { get; set; }

    /// <summary>Bij een automatisch aangemaakte terugrit (IsAutomatischeTerugrit=true): de
    /// oorspronkelijke heenroute die na aankomst weer gestart moet worden.</summary>
    public Treinroute? OorspronkelijkeRoute { get; set; }

    /// <summary>Mag deze route met deze trein gereden worden, volgens Geldigheid? Bij Alle
    /// altijd true; bij Treinstel kijkt hij naar Trein.IsTreinstel; de andere soorten kijken
    /// naar de vastgelegde lijsten. Als er geen trein gekozen is (trein=null) mag alleen een
    /// Alle-route zonder specifieke trein rijden - de overige soorten hebben nu eenmaal een
    /// trein nodig om te kunnen toetsen.</summary>
    public bool IsGeldigVoor(Trein? trein) => Geldigheid switch
    {
        GeldigheidsSoort.Alle => true,
        GeldigheidsSoort.Treinstel => trein?.IsTreinstel == true,
        GeldigheidsSoort.Treintype => trein?.Treintype != null && GeldigeTreintypes.Contains(trein.Treintype),
        GeldigheidsSoort.TreintypeEnRijwindow => trein != null && trein.Treintype != null &&
            GeldigeTreintypes.Contains(trein.Treintype) && GeldigeTreinen.Contains(trein),
        GeldigheidsSoort.Locomotief => trein != null && GeldigeTreinen.Contains(trein),
        GeldigheidsSoort.Rijwindow => trein != null && GeldigeTreinen.Contains(trein),
        _ => true
    };

    public override string ToString() => Treintype != null ? $"{Omschrijving} ({Treintype.Omschrijving})" : Omschrijving;
}

/// <summary>
/// "Geldt voor" uit Koploper's Stamgegevens-tabblad: bepaalt hoe een variabele treinroute
/// gekoppeld wordt aan treinen. Rijwindow = per trein losstaand aan te vinken (in onze
/// simulator: dezelfde mechaniek als Locomotief, aangezien wij geen aparte, persistente
/// rijvensters per trein bijhouden zoals de echte Koploper). Treintype = geldt voor ALLE
/// treinen van een gekozen type. Treintype/Rijwindow = alleen treinen van een gekozen type
/// EN die ook los zijn aangevinkt. Alle = altijd geldig. Treinstel = geldt voor elke trein
/// met IsTreinstel=true, ongeacht treintype. Locomotief = per individuele trein aan te vinken.
/// </summary>
public enum GeldigheidsSoort { Rijwindow, Treintype, TreintypeEnRijwindow, Alle, Treinstel, Locomotief }

public enum DagdeelGeldigheid { Beide, AlleenOchtend, AlleenMiddag }
