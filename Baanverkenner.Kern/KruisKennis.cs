namespace Baanverkenner.Kern;

/// <summary>
/// BUG #129 (Marco 14-10 11:10): wat de verkenner over de Engelse wissels (kruiswissels) weet, hoort bij DEZE verkenning. De gebruiker geeft bij de start op
/// hoeveel Engelse wissels er zijn en welke motoren erin zitten (bijv. "13+10" of "13+10, 3+4"; optioneel met de melder van de kruiswissel: "13+10@24").
/// De melder zelf wordt anders ontdekt (kortsluiting zodra een van die motoren omgezet wordt en de loc erop staat). De vaste referentie uit
/// <see cref="KruiswisselReferentie"/> (melder 24, motoren 13 en 10, bewezen verbindingen) wordt alleen gebruikt als de gebruiker kiest om gegevens uit
/// eerdere verkenningen te gebruiken.
/// </summary>
public sealed class KruisKennis
{
    public HashSet<int> Motoren { get; } = new();
    public HashSet<int> Middens { get; } = new();
    /// <summary>De vaste referentie (benen, bewezen verbindingen, melder 24, motor 13/10) is actief.</summary>
    public bool MetReferentie { get; set; }

    public bool IsMotor(int adres) => Motoren.Contains(adres);
    public bool OpKruis(IEnumerable<int> bezet) => Middens.Count > 0 && bezet.Any(Middens.Contains);

    /// <summary>De kennis die de vaste referentie al had (melder 24, motor 13 en 10); gebruikt als standaard in statische hulpmethoden en tests.</summary>
    public static KruisKennis Referentie()
    {
        var k = new KruisKennis { MetReferentie = true };
        k.Motoren.Add(KruiswisselReferentie.Motor13); k.Motoren.Add(KruiswisselReferentie.Motor10);
        k.Middens.Add(KruiswisselReferentie.Midden);
        return k;
    }

    /// <summary>"13+10", "13+10@24", "13+10, 3+4@30": elk groepje is een Engelse wissel; getallen na een plus zijn de motoren, na een @ de melder.</summary>
    public static KruisKennis Parse(string? tekst)
    {
        var k = new KruisKennis();
        if (string.IsNullOrWhiteSpace(tekst)) return k;
        foreach (var groep in tekst.Split(new[] { ',', ';', '/' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var delen = groep.Split('@', 2);
            foreach (var m in delen[0].Split(new[] { '+', ' ', '&' }, StringSplitOptions.RemoveEmptyEntries))
                if (int.TryParse(m.Trim(), out int a) && a > 0) k.Motoren.Add(a);
            if (delen.Length == 2 && int.TryParse(delen[1].Trim(), out int melder) && melder > 0) k.Middens.Add(melder);
        }
        return k;
    }

    public string MotorenTekst => string.Join("+", Motoren.OrderBy(x => x));
    public string MiddenTekst => string.Join(", ", Middens.OrderBy(x => x));
}
