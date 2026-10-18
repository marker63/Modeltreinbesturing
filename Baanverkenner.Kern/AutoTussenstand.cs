using System.Text;

namespace Baanverkenner.Kern;

/// <summary>BUG #226 (Marco 18-10 12:31: "schrijf de logfiles iedere 10 minuten naar de directory"): schrijft tijdens een verkenning automatisch een tussenstand weg,
/// met dezelfde bestandsnamen als de knoppen in het scherm (baanrapport_, baanverkenner-log_, hardwarelog_, baankaart_ + datum_tijd). Daarnaast komt er
/// <c>LAATSTE.txt</c> in de map met het tijdstip en de namen van de nieuwste set, zodat een controle meteen weet welke bestanden bij elkaar horen.
/// Schrijven mag de verkenning nooit storen: fouten komen alleen in de teruggegeven tekst.</summary>
public static class AutoTussenstand
{
    public static string Stempel(DateTime nu) => nu.ToString("yyyy-MM-dd_HHmm");

    /// <summary>BUG #233 (Marco 18-10 13:50: "het moet C:\Marco\ zijn"): de eerste versie schreef standaard naar C:\Marco\Autolog en die waarde kan al in de bewaarde
    /// instellingen staan. Die oude standaardwaarde wordt C:\Marco; een zelf gekozen andere map blijft zoals hij is.</summary>
    public static string Map(string? ingesteld)
    {
        var m = (ingesteld ?? "").Trim().TrimEnd('\\', '/');
        if (m.Length == 0 || string.Equals(m, @"C:\Marco\Autolog", StringComparison.OrdinalIgnoreCase)) return @"C:\Marco";
        return m;
    }

    /// <summary>Is het tijd voor een nieuwe tussenstand?</summary>
    public static bool IsTijd(DateTime? vorige, DateTime nu, int minuten)
        => minuten > 0 && (vorige is null || (nu - vorige.Value).TotalMinutes >= minuten);

    /// <summary>Schrijft de set. Geeft de geschreven bestanden terug (of een foutmelding in <paramref name="fout"/>).</summary>
    public static List<string> Schrijf(string map, Baankaart? kaart, string? logTekst, string? hardwareTekst, DateTime nu, string reden, out string? fout)
    {
        fout = null;
        var res = new List<string>();
        try
        {
            Directory.CreateDirectory(map);
            string st = Stempel(nu);
            void Een(string naam, string? inhoud)
            {
                if (inhoud is null) return;
                var pad = Path.Combine(map, naam);
                File.WriteAllText(pad, inhoud, Encoding.UTF8);
                res.Add(naam);
            }
            if (kaart is not null)
            {
                Een($"baanrapport_{st}.txt", Rapport.AlsTekst(kaart));
                Een($"baanrapport_{st}.html", Rapport.AlsHtml(kaart));
                Een($"baankaart_{st}.json", kaart.AlsJson());
            }
            Een($"baanverkenner-log_{st}.txt", logTekst);
            Een($"hardwarelog_{st}.txt", hardwareTekst);
            var sb = new StringBuilder();
            sb.AppendLine($"Laatste automatische tussenstand: {nu:yyyy-MM-dd HH:mm:ss} ({reden})");
            foreach (var r in res) sb.AppendLine(r);
            File.WriteAllText(Path.Combine(map, "LAATSTE.txt"), sb.ToString(), Encoding.UTF8);
        }
        catch (Exception ex) { fout = ex.Message; }
        return res;
    }
}
