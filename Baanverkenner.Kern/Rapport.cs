using System.Net;
using System.Text;

namespace Baanverkenner.Kern;

/// <summary>Maakt van een baankaart een leesbaar rapport: HTML (om te bekijken/printen) en
/// platte tekst. Alle richtingen zijn ten opzichte van de testloc (vooruit/achteruit van de
/// decoder).</summary>
public static class Rapport
{
    // =====================================================================
    // Gedeelde afleidingen
    // =====================================================================

    public record WisselRegel(int Adres, string Ligging, string Rechtdoor, string Afbuigend, string Opmerking);

    public static List<WisselRegel> WisselRegels(Baankaart k)
    {
        var res = new List<WisselRegel>();
        foreach (var w in k.Wissels)
        {
            foreach (var obs in w.Waarnemingen)
            {
                string ligging = obs.Soort switch
                {
                    WaarnemingSoort.Splitsing => $"na melder {obs.NaMelder} ({obs.Richting.Tekst()}), puntzijde",
                    WaarnemingSoort.KortsluitingBijOmzetten => $"na melder {obs.NaMelder} ({obs.Richting.Tekst()}), van achteren bereden",
                    _ => $"na melder {obs.NaMelder} ({obs.Richting.Tekst()}), van achteren bereden"
                };
                string recht = obs.VolgendeBijRechtdoor is int r ? $"melder {r}" : (obs.Soort == WaarnemingSoort.LostKortsluitingOp ? "–" : "doodlopend / onbekend");
                string afb = obs.VolgendeBijAfbuigend is int a ? $"melder {a}"
                    : obs.Soort == WaarnemingSoort.KortsluitingBijOmzetten ? "kortsluiting"
                    : obs.Soort == WaarnemingSoort.LostKortsluitingOp ? "–"
                    : "geen melder bereikt";
                res.Add(new WisselRegel(w.Adres, ligging, recht, afb, obs.Toelichting));
            }
        }
        return res;
    }

    /// <summary>Welke wissel (adres + stand) hoort bij de overgang van melder a naar b in
    /// richting r, voor zover bekend.</summary>
    public static List<string> WisselsTussen(Baankaart k, int a, int b, Richting r)
    {
        var res = new List<string>();
        var gehad = new HashSet<int>();
        foreach (var w in k.Wissels)
            foreach (var obs in w.Waarnemingen)
            {
                if (obs.NaMelder == a && obs.Richting == r)
                {
                    if (obs.VolgendeBijRechtdoor == b && gehad.Add(w.Adres)) res.Add($"W{w.Adres} rechtdoor");
                    else if (obs.VolgendeBijAfbuigend == b && gehad.Add(w.Adres)) res.Add($"W{w.Adres} afbuigend");
                }
                // Dezelfde wissel andersom bereden (van achteren)
                if (obs.NaMelder == b && obs.Richting == r.Om() && obs.Soort == WaarnemingSoort.Splitsing)
                {
                    if (obs.VolgendeBijRechtdoor == a && gehad.Add(w.Adres)) res.Add($"W{w.Adres} rechtdoor (achterkant)");
                    else if (obs.VolgendeBijAfbuigend == a && gehad.Add(w.Adres)) res.Add($"W{w.Adres} afbuigend (achterkant)");
                }
            }
        return res.Distinct().ToList();
    }

    public static string TrajectTekst(Baankaart k, Traject t)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < t.Reeks.Count; i++)
        {
            if (i > 0)
            {
                var ws = WisselsTussen(k, t.Reeks[i - 1], t.Reeks[i], t.Richting);
                sb.Append(ws.Count > 0 ? $" → [{string.Join(", ", ws)}] → " : " → ");
            }
            sb.Append(t.Reeks[i]);
        }
        sb.Append(t.Einde switch
        {
            RitEinde.Doodlopend => "  ▮ doodlopend",
            RitEinde.Lus => "  ↺ lus",
            RitEinde.Kortsluiting or RitEinde.BekendKortsluitpunt => "  ⚡ kortsluitpunt",
            RitEinde.Maximum => "  … (veiligheidsgrens)",
            _ => ""
        });
        return sb.ToString();
    }

    private static string Volgenden(Baankaart k, int melder, Richting r) =>
        string.Join(", ", k.BekendeVolgende(melder, r));

    private static string Duur(Baankaart k)
    {
        var d = k.Bijgewerkt - k.Gestart;
        if (d <= TimeSpan.Zero) return "-";
        return d.TotalHours >= 1 ? $"{(int)d.TotalHours} u {d.Minutes} min" : $"{d.Minutes} min {d.Seconds} s";
    }

    // =====================================================================
    // HTML
    // =====================================================================

    public static string AlsHtml(Baankaart k)
    {
        string E(object? o) => WebUtility.HtmlEncode(o?.ToString() ?? "");
        var sb = new StringBuilder();
        sb.Append("""
<!DOCTYPE html>
<html lang="nl"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Baanverkenner - rapport</title>
<style>
:root{--ink:#1d2330;--muted:#5d6678;--line:#dfe3ea;--bg:#f6f7f9;--card:#fff;--accent:#1f6f5c;--warn:#a1520f;--bad:#a12a2a}
*{box-sizing:border-box}
body{margin:0;font:15px/1.5 "Segoe UI",system-ui,sans-serif;color:var(--ink);background:var(--bg)}
main{max-width:1100px;margin:0 auto;padding:24px 16px 60px}
h1{font-size:26px;margin:0 0 4px}
h2{font-size:19px;margin:34px 0 10px;padding-bottom:6px;border-bottom:2px solid var(--line)}
.sub{color:var(--muted);margin:0 0 18px}
.meta{display:grid;grid-template-columns:repeat(auto-fit,minmax(210px,1fr));gap:6px 20px;background:var(--card);border:1px solid var(--line);border-radius:10px;padding:14px 16px}
.meta b{display:block;font-size:12px;color:var(--muted);font-weight:600;text-transform:uppercase;letter-spacing:.03em}
.tegels{display:grid;grid-template-columns:repeat(auto-fit,minmax(150px,1fr));gap:10px;margin-top:14px}
.tegel{background:var(--card);border:1px solid var(--line);border-radius:10px;padding:12px 14px}
.tegel .n{font-size:28px;font-weight:700;color:var(--accent);line-height:1.1}
.tegel .l{color:var(--muted);font-size:13px}
table{width:100%;border-collapse:collapse;background:var(--card);border:1px solid var(--line);border-radius:10px;overflow:hidden}
th,td{text-align:left;padding:7px 10px;border-bottom:1px solid var(--line);vertical-align:top}
th{background:#eef1f5;font-size:13px}
tr:last-child td{border-bottom:none}
.num{font-variant-numeric:tabular-nums;font-weight:600}
.traject{font-family:Consolas,"Cascadia Mono",monospace;font-size:14px;background:var(--card);border:1px solid var(--line);border-radius:8px;padding:8px 12px;margin:6px 0;white-space:normal;word-spacing:1px}
.traject small{display:block;font-family:"Segoe UI",sans-serif;color:var(--muted);white-space:normal}
.ok{color:var(--accent);font-weight:600}.let{color:var(--warn);font-weight:600}.fout{color:var(--bad);font-weight:600}
.kaart{background:var(--card);border:1px solid var(--line);border-radius:10px;padding:12px 16px}
ul{margin:6px 0 0 18px;padding:0}
.uitleg{color:var(--muted);font-size:14px}
@media print{body{background:#fff}.tegel,.meta,table,.traject,.kaart{break-inside:avoid}}
</style></head><body><main>
""");
        sb.Append("<h1>Baanverkenner – rapport</h1>");
        sb.Append($"<p class=\"sub\">{(k.Voltooid ? "<span class=\"ok\">Verkenning voltooid</span>" : "<span class=\"let\">Verkenning (nog) niet voltooid – tussenstand</span>")} · bijgewerkt {E(k.Bijgewerkt.ToString("dd-MM-yyyy HH:mm"))}</p>");

        sb.Append("<div class=\"meta\">");
        sb.Append($"<div><b>Hardware</b>{E(k.Hardware)}</div>");
        sb.Append($"<div><b>Testloc</b>adres {k.LocAdres} ({k.LocStappen} stappen)</div>");
        sb.Append($"<div><b>Startmelder</b>{k.StartMelder}</div>");
        sb.Append($"<div><b>Geteste wisseladressen</b>{k.WisselAdresVan} t/m {k.WisselAdresTot}</div>");
        sb.Append($"<div><b>Gestart</b>{E(k.Gestart.ToString("dd-MM-yyyy HH:mm"))}</div>");
        sb.Append($"<div><b>Duur</b>{E(Duur(k))}</div>");
        sb.Append("</div>");

        int openKs = k.Kortsluitpunten.Count(x => !x.Opgelost);
        sb.Append("<div class=\"tegels\">");
        void Tegel(object n, string l) => sb.Append($"<div class=\"tegel\"><div class=\"n\">{E(n)}</div><div class=\"l\">{E(l)}</div></div>");
        Tegel(k.Melders.Count, "melders (secties)");
        Tegel(k.Wissels.Count, "wissels gevonden");
        Tegel(k.Kopsporen.Count, "doodlopende einden");
        Tegel(k.VoorgesteldeBlokken.Count, "voorgestelde blokken");
        Tegel(openKs, "open kortsluitpunten");
        Tegel(k.AdressenZonderEffect.Count, "adressen zonder effect");
        sb.Append("</div>");

        sb.Append("<p class=\"uitleg\">Richtingen (vooruit/achteruit) zijn steeds die van de testloc zelf. ‘Rechtdoor’ en ‘afbuigend’ zijn de commando’s die naar het adres gestuurd zijn – bij een omgekeerd aangesloten wisselmotor is dat fysiek andersom, maar de koppeling commando ↔ melder klopt altijd.</p>");

        // Wissels
        sb.Append("<h2>Wissels</h2>");
        var wr = WisselRegels(k);
        if (wr.Count == 0) sb.Append("<p>Geen wissels gevonden.</p>");
        else
        {
            sb.Append("<table><tr><th>Adres</th><th>Ligging</th><th>Rechtdoor</th><th>Afbuigend</th><th>Toelichting</th></tr>");
            foreach (var r in wr)
                sb.Append($"<tr><td class=\"num\">{r.Adres}</td><td>{E(r.Ligging)}</td><td>{E(r.Rechtdoor)}</td><td>{E(r.Afbuigend)}</td><td>{E(r.Opmerking)}</td></tr>");
            sb.Append("</table>");
        }

        // Trajecten
        sb.Append("<h2>Gereden trajecten</h2>");
        sb.Append("<p class=\"uitleg\">Elke basisrit van een opdracht, met de wissels die daarbij gepasseerd worden.</p>");
        foreach (var t in k.Trajecten)
            sb.Append($"<div class=\"traject\">{E(TrajectTekst(k, t))}<small>vanaf melder {t.Start}, {t.Richting.Tekst()}, {E(t.Configuratie)}</small></div>");

        // Melders
        sb.Append("<h2>Melders</h2>");
        bool metBlok = k.Melders.Any(m => m.DinamoBlok is not null);
        sb.Append("<table><tr><th>Melder</th>" + (metBlok ? "<th>Dinamo-blok</th>" : "") + "<th>Vooruit gevolgd door</th><th>Achteruit gevolgd door</th><th>Opmerking</th></tr>");
        foreach (var m in k.Melders)
        {
            var opm = new List<string>();
            if (k.Kopsporen.Any(x => x.Melder == m.Nummer)) opm.Add("einde (" + string.Join("/", k.Kopsporen.Where(x => x.Melder == m.Nummer).Select(x => x.Richting.Tekst())) + ")");
            if (m.Nummer == k.StartMelder) opm.Add("startmelder");
            string blok = metBlok ? $"<td>{(m.DinamoBlok is int b ? b.ToString() : "<span class=\"let\">onbekend</span>")}</td>" : "";
            sb.Append($"<tr><td class=\"num\">{m.Nummer}</td>{blok}<td>{E(Volgenden(k, m.Nummer, Richting.Vooruit))}</td><td>{E(Volgenden(k, m.Nummer, Richting.Achteruit))}</td><td>{E(string.Join(", ", opm))}</td></tr>");
        }
        sb.Append("</table>");

        // Blokken
        sb.Append("<h2>Voorgestelde blokken</h2>");
        sb.Append("<p class=\"uitleg\">Aaneengesloten melders zonder wissel ertussen" + (metBlok ? " en met hetzelfde Dinamo-blok" : "") + " zijn samengenomen. Dit is een voorstel voor de import in Modeltreinbesturing.</p>");
        sb.Append("<table><tr><th>Blok</th><th>Melders (in rijvolgorde)</th>" + (metBlok ? "<th>Dinamo-blok</th>" : "") + "<th>Grenst aan blok</th></tr>");
        foreach (var b in k.VoorgesteldeBlokken)
            sb.Append($"<tr><td class=\"num\">{b.Nummer}</td><td>{E(string.Join(" – ", b.Melders))}</td>" + (metBlok ? $"<td>{E(b.DinamoBlok?.ToString() ?? "?")}</td>" : "") + $"<td>{E(string.Join(", ", b.Buren))}</td></tr>");
        sb.Append("</table>");

        // Kopsporen
        sb.Append("<h2>Doodlopende einden</h2>");
        if (k.Kopsporen.Count == 0) sb.Append("<p>Geen.</p>");
        else
        {
            sb.Append("<div class=\"kaart\"><ul>");
            foreach (var e in k.Kopsporen) sb.Append($"<li>Na melder <b>{e.Melder}</b> ({e.Richting.Tekst()}) komt geen melder meer – kopspoor of stootjuk.</li>");
            sb.Append("</ul></div>");
        }

        // Kortsluitpunten en onverwachte terugwegen (BUG #40: niet meer allebei "kortsluiting"
        // noemen, en niet meer stellig "wissel" zeggen - dit kan ook een gemiste melder op een
        // recht stuk spoor zonder wissel zijn).
        sb.Append("<h2>Kortsluitpunten en onverwachte terugwegen</h2>");
        if (k.Kortsluitpunten.Count == 0) sb.Append("<p>Geen kortsluitingen of onverwachte terugwegen tijdens de basisritten.</p>");
        else
        {
            sb.Append("<table><tr><th>#</th><th>Soort</th><th>Plaats</th><th>Wisselstand</th><th>Keer</th><th>Dinamo-alarm</th><th>Status</th></tr>");
            foreach (var x in k.Kortsluitpunten)
            {
                string st = x.Opgelost ? $"<span class=\"ok\">opgelost: adres {x.OpgelostDoorAdres} op {(x.OpgelostMetAfbuigend == true ? "afbuigend" : "rechtdoor")}</span>"
                    : x.Opgegeven ? "<span class=\"fout\">opgegeven</span>" : "<span class=\"let\">open</span>";
                string soort = x.Soort == KortsluitpuntSoort.OnverwachteTerugweg ? "onverwachte terugweg (geen kortsluiting, mogelijk geen wissel)" : "kortsluiting";
                sb.Append($"<tr><td class=\"num\">{x.Id}</td><td>{soort}</td><td>na melder {x.NaMelder} ({x.Richting.Tekst()})</td><td>{E(x.Configuratie)}</td><td>{x.AantalKortsluitingen}</td><td>{(x.AlarmBlokken.Count > 0 ? "blok " + string.Join(", ", x.AlarmBlokken) : "-")}</td><td>{st}</td></tr>");
            }
            sb.Append("</table>");
        }

        // Adressen zonder effect
        sb.Append("<h2>Adressen zonder gevonden effect</h2>");
        sb.Append(k.AdressenZonderEffect.Count == 0 ? "<p>Geen.</p>"
            : $"<div class=\"kaart\"><p>{E(string.Join(", ", k.AdressenZonderEffect))}</p><p class=\"uitleg\">Hier zit geen wissel op die de verkenner kon bereiken: bijvoorbeeld een sein, ontkoppelrail, een ongebruikt adres, of een wissel met een onbekrachtigd puntstuk die alleen van achteren bereden wordt.</p></div>");

        // Waarschuwingen
        if (k.Waarschuwingen.Count > 0)
        {
            sb.Append("<h2>Aandachtspunten</h2><div class=\"kaart\"><ul>");
            foreach (var w in k.Waarschuwingen) sb.Append($"<li class=\"let\">{E(w)}</li>");
            sb.Append("</ul></div>");
        }
        var zonderBlok = k.Melders.Where(m => m.DinamoBlok is null).Select(m => m.Nummer).ToList();
        if (metBlok && zonderBlok.Count > 0)
            sb.Append($"<p class=\"let\">Melders zonder gevonden Dinamo-blok: {E(string.Join(", ", zonderBlok))}.</p>");

        sb.Append("<p class=\"uitleg\" style=\"margin-top:40px\">Gemaakt met de Baanverkenner (onderdeel van Modeltreinbesturing).</p>");
        sb.Append("</main></body></html>");
        return sb.ToString();
    }

    // =====================================================================
    // Platte tekst
    // =====================================================================

    public static string AlsTekst(Baankaart k)
    {
        var sb = new StringBuilder();
        void Kop(string t) { sb.AppendLine(); sb.AppendLine(t); sb.AppendLine(new string('-', t.Length)); }

        sb.AppendLine("BAANVERKENNER - RAPPORT");
        sb.AppendLine(new string('=', 60));
        sb.AppendLine($"Status        : {(k.Voltooid ? "voltooid" : "niet voltooid (tussenstand)")}");
        sb.AppendLine($"Hardware      : {k.Hardware}");
        sb.AppendLine($"Testloc       : adres {k.LocAdres} ({k.LocStappen} stappen)");
        sb.AppendLine($"Startmelder   : {k.StartMelder}");
        sb.AppendLine($"Wisseladressen: {k.WisselAdresVan} t/m {k.WisselAdresTot}");
        sb.AppendLine($"Gestart       : {k.Gestart:dd-MM-yyyy HH:mm}   duur: {Duur(k)}");
        sb.AppendLine($"Gevonden      : {k.Melders.Count} melders, {k.Wissels.Count} wissels, {k.Kopsporen.Count} doodlopende einden, {k.VoorgesteldeBlokken.Count} voorgestelde blokken");
        sb.AppendLine("(Richtingen zijn die van de testloc zelf.)");

        Kop("WISSELS");
        foreach (var r in WisselRegels(k))
            sb.AppendLine($"  Adres {r.Adres,-4} {r.Ligging}\n             rechtdoor: {r.Rechtdoor}   afbuigend: {r.Afbuigend}");
        if (k.Wissels.Count == 0) sb.AppendLine("  (geen)");

        Kop("GEREDEN TRAJECTEN");
        foreach (var t in k.Trajecten)
            sb.AppendLine($"  [{t.Richting.Tekst()}, {t.Configuratie}]\n     {TrajectTekst(k, t)}");

        Kop("MELDERS");
        bool metBlok = k.Melders.Any(m => m.DinamoBlok is not null);
        foreach (var m in k.Melders)
            sb.AppendLine($"  Melder {m.Nummer,-4}" + (metBlok ? $" blok {(m.DinamoBlok?.ToString() ?? "?"),-4}" : "") + $"  vooruit → {Volgenden(k, m.Nummer, Richting.Vooruit),-10} achteruit → {Volgenden(k, m.Nummer, Richting.Achteruit)}");

        Kop("VOORGESTELDE BLOKKEN");
        foreach (var b in k.VoorgesteldeBlokken)
            sb.AppendLine($"  Blok {b.Nummer,-3}: melders {string.Join(" - ", b.Melders)}" + (b.DinamoBlok is int d ? $" (Dinamo-blok {d})" : "") + $"   grenst aan: {string.Join(", ", b.Buren)}");

        Kop("DOODLOPENDE EINDEN");
        foreach (var e in k.Kopsporen) sb.AppendLine($"  na melder {e.Melder} ({e.Richting.Tekst()})");
        if (k.Kopsporen.Count == 0) sb.AppendLine("  (geen)");

        Kop("KORTSLUITPUNTEN EN ONVERWACHTE TERUGWEGEN");
        foreach (var x in k.Kortsluitpunten)
            sb.AppendLine($"  #{x.Id} [{(x.Soort == KortsluitpuntSoort.OnverwachteTerugweg ? "onverwachte terugweg, geen kortsluiting" : "kortsluiting")}] na melder {x.NaMelder} ({x.Richting.Tekst()}), {x.Configuratie}" + (x.AlarmBlokken.Count > 0 ? $" [Dinamo-alarm in blok {string.Join(", ", x.AlarmBlokken)}]" : "") + ": " +
                (x.Opgelost ? $"opgelost met adres {x.OpgelostDoorAdres} op {(x.OpgelostMetAfbuigend == true ? "afbuigend" : "rechtdoor")}" : x.Opgegeven ? "opgegeven" : "open"));
        if (k.Kortsluitpunten.Count == 0) sb.AppendLine("  (geen)");

        Kop("ADRESSEN ZONDER GEVONDEN EFFECT");
        sb.AppendLine("  " + (k.AdressenZonderEffect.Count == 0 ? "(geen)" : string.Join(", ", k.AdressenZonderEffect)));

        if (k.Waarschuwingen.Count > 0)
        {
            Kop("AANDACHTSPUNTEN");
            foreach (var w in k.Waarschuwingen) sb.AppendLine("  - " + w);
        }
        return sb.ToString();
    }
}
