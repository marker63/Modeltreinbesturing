namespace Modeltreinbesturing.Model;

/// <summary>
/// Een "snelle klok" (fast clock) zoals veel modelspoorders die kennen: een virtuele klok
/// die sneller loopt dan de werkelijke tijd, zodat een dienstregeling met realistische
/// vertrektijden toch in een overzienbare speelsessie past. HuidigeTijd is database-breed
/// (hoort bij de baan, niet bij deze sessie) en wordt opgeslagen zodat je een sessie later
/// kunt hervatten waar je gebleven was.
/// </summary>
public class SnelleKlokInstellingen
{
    public TimeSpan HuidigeTijd { get; set; } = new TimeSpan(8, 0, 0);

    /// <summary>Hoeveel ECHTE seconden er verstrijken per gesimuleerde modelminuut - lager
    /// is dus sneller. 10 (de standaard) betekent 6x zo snel als de werkelijke tijd.</summary>
    public double SecondenPerModelMinuut { get; set; } = 10;

    public bool Actief { get; set; }
}
