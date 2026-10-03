namespace Modeltreinbesturing.Model;

public enum BlokActieGebeurtenis
{
    /// <summary>Blok wordt bezet (een trein komt aan).</summary>
    Bezet,

    /// <summary>Blok wordt weer vrij (de trein vertrekt/is doorgereden).</summary>
    Vrij,

    /// <summary>Blok wordt GERESERVEERD - dus VOORDAT de trein er echt aankomt, zodra een
    /// route dit blok claimt. Voor iets als een overweg is dit precies wat je wilt: die
    /// moet al dicht zijn voordat de trein er is, niet pas zodra hij er al is. Matcht hoe
    /// het echte Koploper een overweg beschrijft: "zodra de trein het blok reserveert...
    /// schakelt de decoder in".</summary>
    Gereserveerd,

    /// <summary>Blok is niet meer gereserveerd (route afgerond of geannuleerd) - voor een
    /// overweg: nu weer opendoen.</summary>
    NietMeerGereserveerd
}

/// <summary>
/// Een eenvoudige "actie": Koploper kent een uitgebreid Sequens-systeem waarbij een actie
/// kan reageren op tijd, de aankomst van een trein of de stand van een schakelaar, en dan
/// een hele reeks opdrachten uitvoert (bijv. een overweg bedienen). Dit is een bewust
/// kleinere, wél direct bruikbare eerste stap daarvan: een blok-gebeurtenis (bezet/vrij/
/// gereserveerd/niet meer gereserveerd) zet automatisch een Schakelaar op de baan in een
/// bepaalde stand - bijvoorbeeld een "Abfahrerlaubnis"-lampje dat aangaat zodra een trein
/// een blok binnenrijdt (RhB-Modelbaan-voorbeeld), of een overweg die al bij RESERVERING
/// dichtgaat, ruim vóór de trein er daadwerkelijk is (Koploperforum-voorbeeld).
/// </summary>
public class BlokActie
{
    public required Blok TriggerBlok { get; set; }
    public BlokActieGebeurtenis Gebeurtenis { get; set; } = BlokActieGebeurtenis.Bezet;
    public required Schakelaar DoelSchakelaar { get; set; }
    public bool NieuweStand { get; set; } = true;

    /// <summary>Koploper-praktijkvoorbeeld (RhB-Modelbaan, stationsomroep): een schakelaar
    /// die na aankomst van een trein een externe geluidsmodule aanstuurt, moet vaak een
    /// KORTE PULS zijn i.p.v. een blijvende stand ("de gebruikte schakelaar na 0,5 sec
    /// terugzetten") - veel geluidsmodules/relais reageren op een MOMENTANE schakeling,
    /// niet op een blijvend aan-signaal. 0 (de standaard) betekent: geen automatisch
    /// terugzetten, de schakelaar blijft gewoon op NieuweStand staan totdat een andere
    /// actie 'm weer omzet - exact het bestaande gedrag van vóór deze toevoeging.</summary>
    public double TerugzettenNaSeconden { get; set; }

    public override string ToString()
    {
        string gebeurtenisTekst = Gebeurtenis switch
        {
            BlokActieGebeurtenis.Bezet => "wordt bezet",
            BlokActieGebeurtenis.Vrij => "wordt vrij",
            BlokActieGebeurtenis.Gereserveerd => "wordt gereserveerd",
            _ => "is niet meer gereserveerd"
        };
        string terugzetTekst = TerugzettenNaSeconden > 0 ? $" (puls, {TerugzettenNaSeconden:0.0} sec)" : "";
        return $"Blok {TriggerBlok.Nummer} {gebeurtenisTekst} → schakelaar '{DoelSchakelaar.Omschrijving}' op {(NieuweStand ? "aan" : "uit")}{terugzetTekst}";
    }
}
