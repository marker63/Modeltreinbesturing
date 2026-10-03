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

    public void Schrijf(LogSoort soort, string tekst)
    {
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
