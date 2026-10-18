using System.Text;

namespace Baanverkenner.Kern;

public enum LogSoort { Info, Stap, Rijden, Vondst, Waarschuwing, Fout }

/// <summary>Logboek van één verkenning: elke rit, elke melder, elk wisselcommando en elke
/// vondst, met tijd. Leesbaar exporteerbaar als tekstbestand.</summary>
public class VerkenLog
{
    public record Regel(DateTime Tijd, LogSoort Soort, string Tekst);

    private readonly object _slot = new();
    private readonly List<Regel> _regels = new();
    private readonly IKlok _klok;

    public VerkenLog(IKlok klok) { _klok = klok; }

    public event Action<Regel>? NieuweRegel;

    /// <summary>BUG #235: wissels die omgekeerd zijn aangesloten (Dinamo 'afbuigend' = op de baan rechtdoor). Een logregel die zo'n wissel met een stand noemt krijgt een uitleg erachter.</summary>
    public HashSet<int> OmgekeerdeWissels { get; set; } = new();

    /// <summary>BUG #235: zet achter een tekst die wissel/adres N met 'afbuigend' of 'rechtdoor' noemt (N omgekeerd aangesloten) wat dat op de baan is.</summary>
    public static string MetBaanstand(string tekst, IReadOnlyCollection<int> omgekeerd)
    {
        if (omgekeerd.Count == 0 || !(tekst.Contains("afbuigend") || tekst.Contains("rechtdoor"))) return tekst;
        var genoemd = omgekeerd.Where(a => System.Text.RegularExpressions.Regex.IsMatch(tekst, $@"\b(?:[Ww]issels?|[Aa]dres(?:sen)?)\s+(?:\d+\s*(?:,|en)\s*)*{a}\b")
                                        || System.Text.RegularExpressions.Regex.IsMatch(tekst, $@"afbuigend:\s*(?:\d+\s*,\s*)*{a}\b")).OrderBy(a => a).ToList();
        if (genoemd.Count == 0) return tekst;
        return tekst + $" [wissel {string.Join(", ", genoemd)} is omgekeerd aangesloten: 'afbuigend' in deze melding is op de baan rechtdoor en andersom]";
    }

    public void Schrijf(LogSoort soort, string tekst)
    {
        // BUG #177 (Marco 17-10 10:53: "rare tekens bij adres 5 afbuigend"): pijlen en het weglatingsteken staan in het venster en in sommige editors als blokjes of
        // gekke tekens. In het logboek dus gewone tekens.
        tekst = MetBaanstand(tekst, OmgekeerdeWissels);   // BUG #235
        tekst = tekst.Replace("\u2192", "->").Replace("\u2190", "<-").Replace("\u2194", "<->").Replace("\u2026", "...");
        var r = new Regel(_klok.Nu, soort, tekst);
        lock (_slot) _regels.Add(r);
        NieuweRegel?.Invoke(r);
    }

    public void Info(string t) => Schrijf(LogSoort.Info, t);
    public void Stap(string t) => Schrijf(LogSoort.Stap, t);
    public void Rijden(string t) => Schrijf(LogSoort.Rijden, t);
    public void Vondst(string t) => Schrijf(LogSoort.Vondst, t);
    public void Waarschuwing(string t) => Schrijf(LogSoort.Waarschuwing, t);
    public void Fout(string t) => Schrijf(LogSoort.Fout, t);

    public IReadOnlyList<Regel> Regels { get { lock (_slot) return _regels.ToList(); } }

    public static string SoortTekst(LogSoort s) => s switch
    {
        LogSoort.Stap => "STAP  ",
        LogSoort.Rijden => "rijden",
        LogSoort.Vondst => "VONDST",
        LogSoort.Waarschuwing => "LET OP",
        LogSoort.Fout => "FOUT  ",
        _ => "info  "
    };

    public static string Opmaak(Regel r) => $"{r.Tijd:yyyy-MM-dd HH:mm:ss.fff}  {SoortTekst(r.Soort)}  {r.Tekst}";

    public string AlsTekst()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Baanverkenner - logboek");
        sb.AppendLine(new string('=', 60));
        foreach (var r in Regels) sb.AppendLine(Opmaak(r));
        return sb.ToString();
    }
}
