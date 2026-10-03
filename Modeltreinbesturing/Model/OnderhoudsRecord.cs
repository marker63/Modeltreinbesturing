namespace Modeltreinbesturing.Model;

/// <summary>Een INCIDENT is iets dat kapot/mis is (een storing, een defect, onverwacht
/// gedrag); een WIJZIGING is iets dat bewust aangepast/vervangen/onderhouden is - net als
/// Incident- en Change Management in een professionele ITSM-omgeving, hier toegepast op de
/// objecten van de modelbaan (blokken, wissels, seinen, locs, ...).</summary>
public enum OnderhoudsSoort { Incident, Wijziging }

/// <summary>
/// Eén registratie in de onderhoudslog, gekoppeld aan een los te benoemen OBJECT (bijv.
/// "Wissel 12", "Loc NS 1263", "Blok 34") - bewust een vrije tekst i.p.v. een structurele
/// verwijzing naar een specifiek modelobject, zodat de log blijft werken ook als het
/// object zelf later verwijderd/hernoemd wordt, en zodat je ook onderhoud aan iets buiten
/// het model zelf (bijv. "rails sectie 3, los stuk voeding") kunt vastleggen.
/// </summary>
public class OnderhoudsRecord
{
    public OnderhoudsSoort Soort { get; set; } = OnderhoudsSoort.Incident;

    /// <summary>Vrije tekst - welk object dit betreft, bijv. "Wissel 12" of "Loc NS 1263".</summary>
    public string Object { get; set; } = "";

    public string Titel { get; set; } = "";
    public string Omschrijving { get; set; } = "";
    public DateTime Datum { get; set; } = DateTime.Now;

    /// <summary>Voor een Incident: is het opgelost? Voor een Wijziging: is 'm daadwerkelijk
    /// uitgevoerd (i.p.v. alleen gepland/voorgenomen)? Eén gedeeld begrip i.p.v. twee aparte
    /// statusvelden, om het model eenvoudig te houden.</summary>
    public bool Afgerond { get; set; }

    /// <summary>Pad naar een foto, optioneel - bijv. bewijs van de schade bij een incident,
    /// of een foto van het vervangen onderdeel bij een wijziging.</summary>
    public string? FotoPad { get; set; }

    public override string ToString()
    {
        string soort = Soort == OnderhoudsSoort.Incident ? "Incident" : "Wijziging";
        string status = Afgerond ? (Soort == OnderhoudsSoort.Incident ? "opgelost" : "uitgevoerd") : (Soort == OnderhoudsSoort.Incident ? "open" : "gepland");
        return $"[{soort}, {status}] {Datum:dd-MM-yyyy} — {Object}: {Titel}";
    }
}
