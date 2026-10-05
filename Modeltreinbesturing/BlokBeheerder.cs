using System.Windows.Threading;
using Modeltreinbesturing.Model;

namespace Modeltreinbesturing;

/// <summary>
/// Beheert laag 1: de blokken en hun onderlinge relaties. Geen tekening,
/// geen automatisch rijden - puur het schema.
/// </summary>
public class BlokBeheerder
{
    public List<Blok> Blokken { get; } = new();
    public List<BlokRelatie> Relaties { get; } = new();

    private readonly HashSet<Blok> _bezetteBlokken = new();
    private readonly HashSet<Blok> _gereserveerdeBlokken = new();
    private readonly HashSet<Blok> _handmatigBezetteBlokken = new();
    private readonly HashSet<Blok> _staartBlokken = new();
    private readonly HashSet<Blok> _foutmeldingBlokken = new();
    private readonly HashSet<Blok> _netVrijgegevenBlokken = new();
    private readonly Dictionary<Blok, Trein> _locOpBlok = new();
    private readonly DispatcherTimer _foutmeldingKnipperTimer;

    /// <summary>Of een blok-met-foutmelding op dit moment de foutkleur moet tonen (aan) of de
    /// normale kleur (uit) - Koploper laat deze knipperen, dus dit wisselt elke halve seconde.</summary>
    public bool FoutmeldingKnipperAan { get; private set; } = true;

    public BlokBeheerder()
    {
        _foutmeldingKnipperTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _foutmeldingKnipperTimer.Tick += (_, _) =>
        {
            FoutmeldingKnipperAan = !FoutmeldingKnipperAan;
            if (_foutmeldingBlokken.Count > 0) BezettingGewijzigd?.Invoke();
        };
        _foutmeldingKnipperTimer.Start();
    }

    /// <summary>Vuurt zodra een blok bezet/vrij/gereserveerd wordt gemeld door de simulator, zodat
    /// andere vensters (blokkenschema, baanontwerp) meteen kunnen bijkleuren - net als in Koploper zelf.</summary>
    public event Action? BezettingGewijzigd;

    /// <summary>Gerichte variant van BezettingGewijzigd - geeft WELK blok en de NIEUWE
    /// bezet-stand mee (het generieke event doet dat niet, dat is puur een "iets is
    /// gewijzigd, herteken maar"-seintje). Nodig voor ActieBeheerder om te kunnen bepalen
    /// welke blok-actie(s) moeten afgaan bij een aankomst/vertrek.</summary>
    public event Action<Blok, bool>? BlokBezetVeranderd;

    /// <summary>GEBRUIKERSVERZOEK ("we zijn de kluts kwijt welke melder waar hoort") -
    /// vuurt voor ELKE binnengekomen bezetmelding, ook een melder die nog bij GEEN enkel
    /// blok hoort (zie MainWindow.Bezetmelding_VanHardware: zo'n melding werd voorheen
    /// stilzwijgend genegeerd, dus onbekende melders bleven volledig onzichtbaar). De
    /// nieuwe Meldpuntenverkenner (zie MeldpuntenVerkennerDialog) abonneert zich hierop om
    /// live te tonen wat er binnenkomt - inclusief een duidelijke markering voor melders
    /// die nog aan geen enkel blok gekoppeld zijn.</summary>
    public event Action<int, bool, Blok?>? RuweMelderStatusGewijzigd;

    /// <summary>Meldt een ruwe bezetmelding en vuurt RuweMelderStatusGewijzigd - apart van
    /// RegistreerMelderStatus (dat alleen de status opslaat) zodat de Meldpuntenverkenner
    /// ELKE binnenkomende melding live kan tonen, ook vóór/zonder dat er een blok bij hoort.</summary>
    public void MeldRuweMelderStatus(int meldernummer, bool bezet, Blok? blok)
    {
        _laatsteMelderStatus[meldernummer] = bezet;
        RuweMelderStatusGewijzigd?.Invoke(meldernummer, bezet, blok);
    }

    public bool IsBezet(Blok blok) => _bezetteBlokken.Contains(blok);

    /// <summary>BUG #41: kan een rit dit blok NU als volgend blok krijgen? Nee als het bezet
    /// is (ook handmatig bezet, dat zit in _bezetteBlokken), gereserveerd is, OF een
    /// foutmelding heeft. Een foutmelding (paars: spookmelding/rijrichting-latch) betekent
    /// dat de software niet meer zeker weet of er een loc staat - dan mag er zeker geen
    /// tweede trein naartoe gestuurd worden. Eén plek voor deze regel, zodat de kandidaat-
    /// filters en de start-controles niet meer uit elkaar kunnen lopen.</summary>
    public bool IsGeblokkeerdVoorRit(Blok blok) => IsBezet(blok) || IsGereserveerd(blok) || IsFoutmelding(blok);

    /// <summary>Gereserveerd = onderdeel van het pad van een rijdende trein, maar de trein is er nog niet -
    /// het tussenstation tussen "vrij" en "bezet", zoals in Koploper's eigen kleurenschema.</summary>
    public bool IsGereserveerd(Blok blok) => _gereserveerdeBlokken.Contains(blok);

    /// <summary>Handmatig bezet = een eigen kleurcategorie in Koploper's kleurenschema
    /// ("Lijnkleur: handmatig bezet") - een blok dat je zelf even als bezet markeert, bijvoorbeeld
    /// om te testen hoe seinen/wisselstraten reageren, zonder er echt een trein voor te laten rijden.
    /// Telt voor de rest van het systeem (seinen, routes) gewoon mee als bezet.</summary>
    public bool IsHandmatigBezet(Blok blok) => _handmatigBezetteBlokken.Contains(blok);

    public void ZetHandmatigBezet(Blok blok, bool bezet)
    {
        bool gewijzigd = bezet ? _handmatigBezetteBlokken.Add(blok) : _handmatigBezetteBlokken.Remove(blok);
        if (bezet) _bezetteBlokken.Add(blok);
        else _bezetteBlokken.Remove(blok);
        if (gewijzigd) BezettingGewijzigd?.Invoke();
    }

    /// <summary>Staartindicatie = het blok dat een trein net verlaten heeft, maar nog niet
    /// helemaal "vrij" is verklaard - een eigen kleurcategorie in Koploper ("Lijnkleur:
    /// staartindicatie"), voor het staartgedeelte van de trein terwijl die al deels in het
    /// volgende blok rijdt.</summary>
    public bool IsStaart(Blok blok) => _staartBlokken.Contains(blok);

    public void ZetStaart(Blok blok, bool staart)
    {
        bool gewijzigd = staart ? _staartBlokken.Add(blok) : _staartBlokken.Remove(blok);
        if (gewijzigd) BezettingGewijzigd?.Invoke();
    }

    /// <summary>Blok met foutmelding - eigen kleurcategorie in Koploper ("Lijnkleur: blok met
    /// foutmelding"), die in het echte programma knippert (afwisselend met de normale kleur).
    /// Puur handmatig te zetten hier, voor testdoeleinden - het systeem detecteert zelf nog
    /// geen echte fouten.</summary>
    public bool IsFoutmelding(Blok blok) => _foutmeldingBlokken.Contains(blok);

    /// <summary>Laatst ontvangen status van elke INDIVIDUELE bezetmelder - gedeeld tussen
    /// MainWindow (algemene bezet/vrij-afhandeling, incl. spookmelding-detectie) en
    /// TreinrouteWindow (staart-fase-afhandeling), zodat beide dezelfde, ene bron van
    /// waarheid gebruiken over welke meldpunten van een blok op dit moment daadwerkelijk
    /// bezet zijn - eerder stonden dit twee LOSSE trackings, wat een multi-sensor-blok kon
    /// laten vrijgeven terwijl een van zijn andere meldpunten nog gewoon bezet stond.</summary>
    private readonly Dictionary<int, bool> _laatsteMelderStatus = new();

    public void RegistreerMelderStatus(int meldernummer, bool bezet) => _laatsteMelderStatus[meldernummer] = bezet;

    /// <summary>Is deze specifieke melder op dit moment (voor zover bekend) bezet? Null =
    /// nog nooit een melding van ontvangen (onbekend).</summary>
    public bool? MelderIsBezet(int meldernummer) => _laatsteMelderStatus.TryGetValue(meldernummer, out var bezet) ? bezet : null;


    public void ZetFoutmelding(Blok blok, bool foutmelding)
    {
        bool gewijzigd = foutmelding ? _foutmeldingBlokken.Add(blok) : _foutmeldingBlokken.Remove(blok);
        if (gewijzigd) BezettingGewijzigd?.Invoke();
    }

    /// <summary>"Net vrijgegeven" - een kort groen oplichten van een blok net nadat het weer
    /// vrijkomt, vóór het teruggaat naar de normale "vrij"-kleur. Authentiek Koploper-gedrag:
    /// "Een rode lijn is een bezet blok, geel is vooruit gereserveerd, groen wordt net
    /// vrijgegeven, zwart is onbezet." Vervalt vanzelf na een korte periode, net als de
    /// Staartindicatie.</summary>
    public bool IsNetVrijgegeven(Blok blok) => _netVrijgegevenBlokken.Contains(blok);

    private void MarkeerNetVrijgegeven(Blok blok)
    {
        _netVrijgegevenBlokken.Add(blok);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _netVrijgegevenBlokken.Remove(blok);
            BezettingGewijzigd?.Invoke();
        };
        timer.Start();
    }

    public void ZetBezet(Blok blok, bool bezet)
    {
        bool wasBezetOfGereserveerd = _bezetteBlokken.Contains(blok) || _gereserveerdeBlokken.Contains(blok);
        bool gewijzigd = bezet ? _bezetteBlokken.Add(blok) : _bezetteBlokken.Remove(blok);
        if (!bezet)
        {
            _handmatigBezetteBlokken.Remove(blok); // vrijgeven via de simulator heft ook een handmatige markering op
            if (wasBezetOfGereserveerd) MarkeerNetVrijgegeven(blok);
        }
        if (gewijzigd)
        {
            BezettingGewijzigd?.Invoke();
            BlokBezetVeranderd?.Invoke(blok, bezet);
        }
    }

    /// <summary>Gerichte variant, net als BlokBezetVeranderd hierboven maar dan voor
    /// reservering - nodig voor blok-acties die op RESERVERING moeten reageren i.p.v. pas
    /// op echte bezetting (bijv. een overweg: die moet al dicht zijn VOORDAT de trein
    /// aankomt, dus reageert op de reservering van het blok waar de overweg in ligt, niet
    /// pas op de bezetmelding zelf - matcht hoe het echte Koploper dit ook beschrijft).</summary>
    public event Action<Blok, bool>? BlokGereserveerdVeranderd;

    public void ZetGereserveerd(Blok blok, bool gereserveerd)
    {
        bool wasBezetOfGereserveerd = _bezetteBlokken.Contains(blok) || _gereserveerdeBlokken.Contains(blok);
        bool gewijzigd = gereserveerd ? _gereserveerdeBlokken.Add(blok) : _gereserveerdeBlokken.Remove(blok);
        if (!gereserveerd && wasBezetOfGereserveerd && !_bezetteBlokken.Contains(blok)) MarkeerNetVrijgegeven(blok);
        if (gewijzigd)
        {
            BezettingGewijzigd?.Invoke();
            BlokGereserveerdVeranderd?.Invoke(blok, gereserveerd);
        }
    }

    public void MaakAlleVrij()
    {
        if (_bezetteBlokken.Count == 0 && _gereserveerdeBlokken.Count == 0 && _handmatigBezetteBlokken.Count == 0 && _staartBlokken.Count == 0) return;
        _bezetteBlokken.Clear();
        _gereserveerdeBlokken.Clear();
        _handmatigBezetteBlokken.Clear();
        _staartBlokken.Clear();
        BezettingGewijzigd?.Invoke();
    }

    /// <summary>Wist het hele blokkenschema - gebruikt bij "Nieuw project".</summary>
    public void Reset()
    {
        Blokken.Clear();
        Relaties.Clear();
        _bezetteBlokken.Clear();
        _gereserveerdeBlokken.Clear();
        _handmatigBezetteBlokken.Clear();
        _staartBlokken.Clear();
        _foutmeldingBlokken.Clear();
        _locOpBlok.Clear();
    }

    /// <summary>Welke trein (loc) op dit moment op dit blok "staat" - een puur visuele/
    /// bedienings-koppeling (zoals in Koploper zelf: je zet een loc op de baan door 'm in een
    /// blok te plaatsen), los van de eigenlijke rij-simulatie in TreinrouteWindow.</summary>
    public Trein? LocOpBlok(Blok blok) => _locOpBlok.TryGetValue(blok, out var trein) ? trein : null;

    /// <summary>Verwerkt een ECHT gemeten reistijd (seconden) naar "naar" vanuit "van", voor
    /// deze specifieke "trein" (loc) - zie Model.GeleerdeReistijd voor de volledige
    /// achtergrond (per richting én per loc, i.p.v. één gedeeld gemiddelde). Voortschrijdend
    /// gemiddelde (70% oud/30% nieuw) i.p.v. domweg de laatste meting overschrijven, zodat
    /// één keer een uitzonderlijk trage/snelle rit (bijv. een handmatige tussenstop) de
    /// verwachting niet meteen helemaal omgooit - maar het past zich, na een paar ritten,
    /// wel degelijk aan als de daadwerkelijke rijtijd structureel verandert.
    /// "van" of "trein" onbekend (null) -> geen richting/loc om aan toe te schrijven, dus
    /// bewust niets registreren i.p.v. een zinloze/onbruikbare meting op te slaan.</summary>
    public void RegistreerGeleerdeReistijd(Blok naar, Blok? van, Trein? trein, double secondenGemeten)
    {
        if (secondenGemeten <= 0 || van is null || trein is null) return;
        var bestaand = naar.GeleerdeReistijden.FirstOrDefault(g => g.VanBlok == van && g.Trein == trein);
        if (bestaand != null)
            bestaand.Seconden = bestaand.Seconden * 0.7 + secondenGemeten * 0.3;
        else
            naar.GeleerdeReistijden.Add(new GeleerdeReistijd { VanBlok = van, Trein = trein, Seconden = secondenGemeten });
    }

    /// <summary>Zusje van RegistreerGeleerdeReistijd hierboven: de bijbehorende opzoeking -
    /// null als er voor deze exacte combinatie (blok, richting, loc) nog geen enkele echte
    /// meting is geweest (dan valt de aanroeper terug op een generieke standaardwaarde).</summary>
    public double? GeefGeleerdeReistijd(Blok naar, Blok? van, Trein? trein)
    {
        if (van is null || trein is null) return null;
        return naar.GeleerdeReistijden.FirstOrDefault(g => g.VanBlok == van && g.Trein == trein)?.Seconden;
    }

    /// <summary>Omgekeerde opzoeking: op welk blok staat deze trein op dit moment, indien
    /// bekend - voor het "Overzicht locomotieven"-venster.</summary>
    /// <summary>Blok van deze trein - inclusief dubbeltractie: staat de trein zelf niet
    /// direct op een blok, maar is 'm gekoppeld als "knecht" aan een andere trein (die wél
    /// direct op een blok staat), dan geldt het blok van die andere ("baas") trein. Werkt
    /// ook voor een keten van meerdere gekoppelde locs.</summary>
    public Blok? BlokVanLoc(Trein trein)
    {
        var directBlok = _locOpBlok.FirstOrDefault(kv => kv.Value == trein).Key;
        if (directBlok != null) return directBlok;

        foreach (var (blok, baas) in _locOpBlok)
        {
            var bezocht = new HashSet<Trein> { baas };
            var knecht = baas.GekoppeldeKnecht;
            while (knecht != null && bezocht.Add(knecht))
            {
                if (knecht == trein) return blok;
                knecht = knecht.GekoppeldeKnecht;
            }
        }
        return null;
    }

    /// <summary>Alle huidige loc-posities, voor opslaan naar een projectbestand/backup.</summary>
    public IEnumerable<(Blok Blok, Trein Trein)> AlleLocPosities() => _locOpBlok.Select(kv => (kv.Key, kv.Value));

    public void PlaatsLoc(Blok blok, Trein trein) => _locOpBlok[blok] = trein;

    public void VerwijderLoc(Blok blok) => _locOpBlok.Remove(blok);

    /// <summary>Wist alle loc-posities in één keer - gebruikt bij het laden van een project/
    /// backup, zodat oude loc-posities van vóór het laden niet blijven hangen.</summary>
    public void VerwijderAlleLocs() => _locOpBlok.Clear();

    /// <summary>
    /// Nieuw blok op de opgegeven positie in het schema. Nummer wordt automatisch
    /// bepaald als 1 hoger dan het hoogste bestaande nummer.
    /// </summary>
    public Blok NieuwBlok(double schemaX, double schemaY)
    {
        int volgendNummer = Blokken.Count == 0 ? 1 : Blokken.Max(b => b.Nummer) + 1;
        var blok = new Blok { Nummer = volgendNummer, SchemaX = schemaX, SchemaY = schemaY };
        Blokken.Add(blok);
        return blok;
    }

    /// <summary>
    /// Verwijdert een blok en alle relaties waar dit blok bij betrokken is
    /// (zodat er nooit een relatie kan overblijven die naar een niet-bestaand blok wijst).
    /// </summary>
    public void VerwijderBlok(Blok blok)
    {
        Relaties.RemoveAll(r => r.Van == blok || r.Naar == blok);
        Blokken.Remove(blok);
        _bezetteBlokken.Remove(blok);
        _gereserveerdeBlokken.Remove(blok);
        _handmatigBezetteBlokken.Remove(blok);
        _staartBlokken.Remove(blok);
        _foutmeldingBlokken.Remove(blok);
        _locOpBlok.Remove(blok);
    }

    /// <summary>
    /// Legt een rij-relatie vast: van het ene blok kan naar het andere gereden worden.
    /// Dit is de "sleep een blok op een ander blok"-actie uit de handleiding.
    /// Doet niets als de relatie al bestaat (geen duplicaten).
    /// </summary>
    public BlokRelatie? LegRelatieVast(Blok van, Blok naar)
    {
        if (van == naar) return null; // een blok verwijst nooit naar zichzelf
        var bestaat = Relaties.FirstOrDefault(r => r.Van == van && r.Naar == naar);
        if (bestaat != null) return bestaat;

        var relatie = new BlokRelatie { Van = van, Naar = naar };
        Relaties.Add(relatie);
        return relatie;
    }

    public void VerwijderRelatie(BlokRelatie relatie) => Relaties.Remove(relatie);

    /// <summary>Alle blokken die vanuit dit blok rechtstreeks bereikbaar zijn.</summary>
    public IEnumerable<Blok> VolgendeBlokken(Blok van) =>
        Relaties.Where(r => r.Van == van && !r.Naar.Vergrendeld).Select(r => r.Naar);

    /// <summary>
    /// Een "keuzeblok" is een blok waar meer dan 1 vervolgblok mogelijk is - hier moet
    /// een Treinroute (laag 4) straks expliciet een keuze vastleggen. Overal elders
    /// (0 of 1 vervolgblok) kan het automatisch worden afgeleid.
    /// </summary>
    public bool IsKeuzeblok(Blok blok) => VolgendeBlokken(blok).Count() > 1;
}
