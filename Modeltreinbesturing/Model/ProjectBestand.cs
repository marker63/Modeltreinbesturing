using Modeltreinbesturing.Model;

namespace Modeltreinbesturing;

/// <summary>Eén regel blokpositie in het baanontwerp - blok wordt gekoppeld via zijn Nummer
/// (Dictionary&lt;Blok, Point&gt; serialiseert niet netjes naar JSON, dit wel).</summary>
public class BlokPositieItem
{
    public int BlokNummer { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public int Tabblad { get; set; } = 1;
}

/// <summary>Eén regel loc-positie: welke trein op welk blok "staat" - net als
/// BlokPositieItem gekoppeld via Nummer/Omschrijving i.p.v. een object-referentie, want
/// een runtime Dictionary&lt;Blok, Trein&gt; serialiseert niet betrouwbaar. Decoderadres
/// (indien >0) is de eerste keuze om de trein na het laden terug te vinden - waarschijnlijker
/// uniek dan de omschrijving, die wordt alleen als terugval gebruikt.</summary>
public class LocPositieItem
{
    public int BlokNummer { get; set; }
    public int DecoderAdres { get; set; }
    public string TreinOmschrijving { get; set; } = "";
}

/// <summary>
/// De volledige inhoud van een Modeltreinbesturing-projectbestand: alle vier lagen samen,
/// zodat er maar één opslaan/laden-actie nodig is.
/// </summary>
public class ProjectBestand
{
    public List<Blok> Blokken { get; set; } = new();
    public List<BlokRelatie> Relaties { get; set; } = new();
    public List<BaanSymbool> Symbolen { get; set; } = new();
    public List<BlokPositieItem> BlokPosities { get; set; } = new();
    public List<LocPositieItem> LocPosities { get; set; } = new();
    public List<Wisselstraat> Wisselstraten { get; set; } = new();
    public List<Treinroute> Treinroutes { get; set; } = new();
    public List<Treintype> Treintypes { get; set; } = new();

    /// <summary>Koploper's "Algemeen -> Instellingen per database": welk treintype als
    /// rangeertreintype fungeert. Null = geen koppeling (Rangeer betekent dan alleen "geen
    /// massasimulatie", geen aparte snelheden).</summary>
    public Treintype? RangeerTreintype { get; set; }
    public List<Trein> Treinen { get; set; } = new();
    public List<Richtingsverbod> Richtingsverboden { get; set; } = new();
    public List<StopverbodItem> StopverbodenBlokken { get; set; } = new();
    public List<Richtingsverbod> StopverbodenStilstand { get; set; } = new();

    /// <summary>De accounts die toegang hebben tot de bouwschermen - hoort bij DEZE ene
    /// database/dit ene project, net als de rest. Het kijkscherm blijft altijd vrij
    /// toegankelijk, zonder account.</summary>
    public List<Gebruiker> Gebruikers { get; set; } = new();

    /// <summary>Blok-acties (zie Model.BlokActie) - Koploper-achtige eenvoudige automatisering:
    /// een blok-gebeurtenis (bezet/vrij) zet automatisch een schakelaar op de baan om.</summary>
    public List<BlokActie> Acties { get; set; } = new();

    /// <summary>Onderhoudslog (incidenten/wijzigingen per object) - zie Model.OnderhoudsRecord.</summary>
    public List<OnderhoudsRecord> OnderhoudsRecords { get; set; } = new();

    /// <summary>Snelle klok voor de tijdklok-gestuurde dienstregeling (eigen functie).</summary>
    public SnelleKlokInstellingen SnelleKlok { get; set; } = new();

    /// <summary>Koploper's "Algemeen -> Instellingen per database -> Bestemming/Snelheid ->
    /// berekende stopplaats na... cm" - hier het tijd-equivalent (zie SimulatieInstellingen).
    /// Standaard 1.0 seconde, hoort bij de database zelf (niet zoals de simulatiesnelheid,
    /// die is puur een sessie-instelling en wordt bewust NIET opgeslagen).</summary>
    public double StandaardUitrolSeconden { get; set; } = 1.0;

    /// <summary>Koploper's "rustpauze" tussen wissel/sein-commando's (Algemeen ->
    /// Instellingen per database -> Seinen/wissels).</summary>
    public double WisselRustpauzeMilliseconden { get; set; } = 50;

    /// <summary>Koploper's "Algemeen -> Instellingen per database -> Snelheid/Bestemming ->
    /// Snelheidsmeting" - de meetconfiguratie voor het ijken van locomotiefsnelheden.</summary>
    public SnelheidsMetingInstellingen SnelheidsMeting { get; set; } = new();
}
