namespace Modeltreinbesturing.Model;

/// <summary>Een wissel zoals opgenomen in een wisselstraat, met de gewenste stand voor die wisselstraat.</summary>
public class WisselInWisselstraat
{
    public required Wissel Wissel { get; set; }
    public WisselStand GewensteStand { get; set; }

    public override string ToString() => $"Wissel {Wissel.Adres}: {GewensteStand}";
}

/// <summary>GEVONDEN GAT (gebruikerswaarneming: "er komen lijntjes bij in plaats van dat de
/// wisseltongen paars worden" - de Wisselstraat-tool herkende een Kruiswissel/Engelse
/// wissel helemaal niet als geldig doel, dus een klik erop viel stilzwijgend terug op een
/// naastgelegen lijn): zelfde principe als WisselInWisselstraat hierboven, nu voor een
/// Kruiswissel.
///
/// GEBRUIKERSCORRECTIE, FUNDAMENTEEL (zie Model.Kruiswissel voor de volledige, fysiek
/// bevestigde toelichting): een Engelse wissel/dubbele kruiswissel heeft GEEN "gewenste
/// stand van de hele kruiswissel" (het vorige, foute uitgangspunt was Rechtdoor/Overstap
/// geforceerd hetzelfde of tegengesteld op beide motoren) - elke rijweg heeft zijn EIGEN,
/// soms gemengde combinatie van de twee motoren nodig (bevestigd: blok7->blok3 =
/// Adres:Afbuigend/Adres2:Afbuigend, blok4->blok3 = Adres:Rechtdoor/Adres2:Rechtdoor,
/// blok7->blok1 = Adres:Rechtdoor/Adres2:Afbuigend). Deze wisselstraat legt daarom de stand
/// van Adres en Adres2 nu volledig onafhankelijk vast, precies zoals bij een gewone Wissel.</summary>
public class KruiswisselInWisselstraat
{
    public required Kruiswissel Kruiswissel { get; set; }
    public WisselStand StandAdres { get; set; }
    public WisselStand StandAdres2 { get; set; }

    public override string ToString() => $"Kruiswissel {Kruiswissel.Adres}={StandAdres}/{Kruiswissel.Adres2}={StandAdres2}";
}

/// <summary>
/// Laag 3: een wisselstraat legt vast welke wissels (in welke stand) en lijnen gezet
/// moeten worden om van "Van" naar "Naar" te kunnen rijden. Moet altijd overeenkomen
/// met een bestaande BlokRelatie (laag 1) - een wisselstraat kan geen route uitvinden
/// die daar niet in staat.
/// </summary>
public class Wisselstraat
{
    public required Blok Van { get; set; }
    public required Blok Naar { get; set; }

    /// <summary>Volgnummer: er kunnen meerdere (alternatieve) wisselstraten tussen hetzelfde
    /// blokpaar bestaan. Tijdens automatisch rijden wordt eerst volgnummer 1 geprobeerd, enz.</summary>
    public int Volgnummer { get; set; } = 1;

    public List<WisselInWisselstraat> Wissels { get; set; } = new();
    public List<KruiswisselInWisselstraat> Kruiswisselstanden { get; set; } = new();
    public List<Lijn> Lijnen { get; set; } = new();

    public override string ToString() =>
        $"Blok {Van.Nummer} -> {Naar.Nummer} (#{Volgnummer}) - {Wissels.Count} wissel(s), {Kruiswisselstanden.Count} kruiswissel(s), {Lijnen.Count} lijn(en)";
}
