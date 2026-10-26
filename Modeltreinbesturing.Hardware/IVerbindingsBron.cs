namespace Modeltreinbesturing.Hardware;

/// <summary>
/// BUG #81: optionele uitbreiding op <see cref="IHardwareInterface"/> voor koppelingen die
/// bijhouden wanneer er voor het laatst een bericht (ook een keepalive) van de centrale
/// binnenkwam. Zo is een vastgelopen centrale (Dinamo stuurt niets meer terug terwijl wij
/// wel blijven sturen) te onderscheiden van een echte kortsluiting. Alleen Dinamo.
/// </summary>
public interface IVerbindingsBron
{
    /// <summary>Seconden sinds het laatste ontvangen bericht (keepalives meegeteld);
    /// null als er nog niets ontvangen is.</summary>
    double? SecondenSindsLaatsteOntvangst { get; }

    /// <summary>Wordt afgevuurd als de centrale ophoudt met antwoorden (stil = true, tweede waarde = seconden stilte tot nu toe, vanaf de drempel)
    /// en als er weer bericht komt (stil = false, tweede waarde = duur van de stilte). Komt op een achtergrondthread binnen.</summary>
    event Action<bool, double>? StilteGewijzigd;
}

/// <summary>BUG #126: Dinamo meldt elk uitgevoerd wissel-commando terug in een statusbericht. Deze teller loopt op bij elke zo'n terugmelding;
/// loopt hij niet op terwijl er wissel-commando's verstuurd worden, dan voert Dinamo ze niet uit. Alleen Dinamo.</summary>
public interface IWisselBevestiging
{
    /// <summary>Aantal terugmeldingen van wissel-commando's sinds het verbinden.</summary>
    long AantalWisselBevestigingen { get; }

    /// <summary>BUG #307: aantal terugmeldingen van precies deze wissel in deze stand (Dinamo-stand) sinds het verbinden; -1 = niet bekend (dan telt alleen het totaal).
    /// Nodig omdat Dinamo na een uitval nog oude terugmeldingen van andere wissels herhaalt (autolog 25-10 10:17:47: wissel 8 werd nooit teruggemeld, maar de echo's van wissel 7
    /// telden als bevestiging).</summary>
    long BevestigingenVan(int adres, bool afbuigend) => -1;

    /// <summary>BUG #154: de verkenner constateert dat wisselcommando's niet meer worden teruggemeld; de hardware schrijft dan dezelfde
    /// uitvaldiagnose in het hardwarelog als bij 'Dinamo antwoordt niet' (#138). Standaard doet dit niets (simulator, stubs).</summary>
    void LogWisselUitval(string reden) { }
}

/// <summary>BUG #352 (Marco 26-10 13:53/13:56, hardwarelog 26-10): hoe een wisselcommando naar de centrale gaat. Dinamo voert elk verstuurd magneetartikel-datagram als een aparte puls uit
/// (en meldt elk apart terug). Met 6 identieke datagrammen per commando liep de wachtrij in Dinamo vanmiddag tot minuten achter. Alleen Dinamo (en de simulatie).</summary>
public interface IWisselPuls
{
    /// <summary>Hoe vaak hetzelfde wisseldatagram achter elkaar wordt verstuurd (elke keer = een puls in Dinamo).</summary>
    int WisselHerhalingen { get; set; }

    /// <summary>Pulstijd voor één adres in 1/60 s (Dinamo-specificatie 'Magneetartikel', standaard 12 = 200 ms); 0 = standaard.</summary>
    void ZetPulsTijd(int adres, int zestigsten);

    /// <summary>Dinamo heeft het HOLD-bit gezet: de buffers raken vol, liefst even geen nieuwe opdrachten sturen (specificatie, header bit 4).</summary>
    bool DinamoHold { get; }
}

