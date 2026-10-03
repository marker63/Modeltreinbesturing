using System.Windows.Threading;
using Modeltreinbesturing.Model;

namespace Modeltreinbesturing;

/// <summary>
/// Beheert de "snelle klok" en het automatisch starten van routes die een geplande
/// vertrektijd hebben. Puur de klok-mechaniek zelf; het ECHTE starten van een route gebeurt
/// via een callback naar TreinrouteWindow (die weet hoe StartAutomatischeRoute werkt en
/// welke trein bij welk startblok hoort) - deze klasse bepaalt alleen WANNEER dat moet.
/// </summary>
public class SnelleKlokBeheerder
{
    public SnelleKlokInstellingen Instellingen { get; } = new();

    /// <summary>Vuurt elke tick af (ongeveer 1x per reële seconde als de klok actief is) met
    /// de nieuwe HuidigeTijd - voor een live klok-weergave in de UI.</summary>
    public event Action<TimeSpan>? KlokGetikt;

    /// <summary>Vuurt af zodra de klok een route's GeplandeVertrektijd bereikt/gepasseerd
    /// is - de ontvanger (TreinrouteWindow) probeert die route dan daadwerkelijk te
    /// starten en meldt zelf (via de eigen Log) of dat lukte.</summary>
    public event Action<Treinroute>? VertrektijdBereikt;

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly HashSet<Treinroute> _vandaagAlGeprobeerd = new();
    private Func<IEnumerable<Treinroute>>? _routesOpvragen;

    public SnelleKlokBeheerder()
    {
        _timer.Tick += (_, _) => Tick();
    }

    /// <summary>Moet één keer gekoppeld worden (door TreinrouteWindow) zodat deze beheerder
    /// weet welke routes er bestaan, zonder zelf een rechtstreekse afhankelijkheid naar
    /// TreinrouteBeheerder nodig te hebben.</summary>
    public void KoppelRoutesBron(Func<IEnumerable<Treinroute>> routesOpvragen) => _routesOpvragen = routesOpvragen;

    public void Start()
    {
        Instellingen.Actief = true;
        _timer.Start();
    }

    public void Stop()
    {
        Instellingen.Actief = false;
        _timer.Stop();
    }

    private void Tick()
    {
        var vorigeTijd = Instellingen.HuidigeTijd;
        double minutenPerTick = 1.0 / Math.Max(0.1, Instellingen.SecondenPerModelMinuut);
        var nieuweTijd = Instellingen.HuidigeTijd.Add(TimeSpan.FromMinutes(minutenPerTick));

        // Dagwissel (23:xx -> 00:xx): de klok "wikkelt" terug naar het begin van de dag, en
        // elke route mag dan weer opnieuw geprobeerd worden vandaag.
        if (nieuweTijd.Days > 0)
        {
            nieuweTijd = nieuweTijd.Subtract(TimeSpan.FromDays(nieuweTijd.Days));
            _vandaagAlGeprobeerd.Clear();
        }
        Instellingen.HuidigeTijd = nieuweTijd;
        KlokGetikt?.Invoke(nieuweTijd);

        if (_routesOpvragen == null) return;
        foreach (var route in _routesOpvragen())
        {
            if (route.GeplandeVertrektijd is not TimeSpan vertrektijd) continue;
            if (_vandaagAlGeprobeerd.Contains(route)) continue;
            // "Bereikt" = de klok is dit tick-interval OVER de vertrektijd heen gegaan
            // (of stond er al precies op) - dekt af dat een tick de exacte seconde kan
            // overspringen bij een hoge kloksnelheid.
            if (vorigeTijd <= vertrektijd && nieuweTijd >= vertrektijd)
            {
                _vandaagAlGeprobeerd.Add(route);
                VertrektijdBereikt?.Invoke(route);
            }
        }
    }

    /// <summary>Bij het laden van een ander project of "Nieuw": de klok EN de dag-voortgang
    /// terugzetten, anders zou een route die "vandaag" al geprobeerd is in het NIEUWE
    /// project ten onrechte overgeslagen blijven.</summary>
    public void Reset()
    {
        Instellingen.HuidigeTijd = new TimeSpan(8, 0, 0);
        Instellingen.SecondenPerModelMinuut = 10;
        Instellingen.Actief = false;
        _timer.Stop();
        _vandaagAlGeprobeerd.Clear();
    }
}
