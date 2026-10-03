namespace Modeltreinbesturing;

/// <summary>GEBRUIKERSVERZOEK ("kan je ook ergens in het scherm vermelden welke JSON er
/// geladen is, en ook in de log, dan kan daar nooit twijfel over zijn"): een simpele,
/// proces-brede plek om het pad van het NU geladen projectbestand vast te houden, zodat elk
/// venster (blokkenschema, treinroutes/log, kijkscherm) precies hetzelfde, actuele pad kan
/// tonen zonder dat ze onderling een verwijzing naar elkaar nodig hebben. Wordt uitsluitend
/// gezet vanuit MainWindow.LaadBestandEnVerversAlles (de ene, centrale plek waar zowel het
/// opstarten met de laatste backup, "Openen", "Recent geopend" als "Oudere backup openen"
/// allemaal doorheen lopen) - dus altijd correct, ongeacht via welke weg een project geopend
/// werd. Puur een string, geen projectgegevens zelf.</summary>
public static class HuidigProject
{
    /// <summary>Volledig pad van het momenteel geladen projectbestand, of null als er nog
    /// nooit iets geladen is in deze sessie (zou in de praktijk niet moeten voorkomen, want
    /// het programma laadt bij het opstarten altijd meteen de laatste backup/het laatst
    /// geopende project - maar defensief toch nullable, i.p.v. een verzonnen placeholder-tekst).</summary>
    public static string? Pad { get; private set; }

    /// <summary>Alleen de bestandsnaam (zonder map), voor compacte weergave in een titelbalk
    /// - valt terug op "(geen project geladen)" zolang Pad nog null is.</summary>
    public static string Bestandsnaam => Pad != null ? System.IO.Path.GetFileName(Pad) : "(geen project geladen)";

    public static void Zet(string pad) => Pad = pad;

    /// <summary>Voor "Nieuw project": een vers, nog nooit opgeslagen project heeft geen pad -
    /// laat de titelbalk/log dat ook zo tonen, i.p.v. het pad van het vorige project te
    /// blijven claimen.</summary>
    public static void Wis() => Pad = null;
}
