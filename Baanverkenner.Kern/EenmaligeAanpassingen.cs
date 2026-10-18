namespace Baanverkenner.Kern;

/// <summary>BUG #239 (Marco 18-10 15:01: "waarom heb je er niet zelf even een omzetting voor 6 ingezet die 1 malig werd uitgevoerd, en bij een volgende versie er weer uithalen"):
/// instellingen die Claude op grond van de logs en Marco's waarnemingen één keer zelf aanpast, zodat Marco ze niet hoeft in te vullen. Elke aanpassing heeft een vaste sleutel en
/// wordt per instellingenbestand maar één keer uitgevoerd (de sleutel komt in <see cref="VerkenInstellingen.UitgevoerdeAanpassingen"/>). Draait Marco een aanpassing daarna zelf
/// terug in het scherm, dan blijft dat zo. Niet hard gecodeerd in de logica: het zijn gewone instellingen die in het scherm te zien en te wijzigen zijn. Een aanpassing die niet
/// meer nodig is (alle instellingenbestanden hebben hem gehad) mag in een volgende versie uit de lijst.</summary>
public static class EenmaligeAanpassingen
{
    private record Aanpassing(string Sleutel, string Uitleg, Func<VerkenInstellingen, bool> Voer);

    private static readonly List<Aanpassing> Lijst = new()
    {
        // Marco 18-10 14:06: op de baan stond wissel 6 rechtdoor terwijl Dinamo 'afbuigend' stuurde en terugmeldde (hardwarelog 13:45:23); #237: alle andere meldingen kloppen.
        new("239-wissel6-omgekeerd", "Wissel 6 staat nu bij 'Omgekeerd aangesloten' (op de baan gezien op 18-10: Dinamo 'afbuigend' = baan rechtdoor).", ins =>
        {
            var set = ins.OmgekeerdeWissels();
            if (set.Contains(6)) return false;
            set.Add(6);
            ins.OmgekeerdAangesloten = string.Join(", ", set.OrderBy(a => a));
            return true;
        }),
    };

    /// <summary>Voert de nog niet uitgevoerde aanpassingen uit. Geeft de uitleg van wat er veranderd is (leeg = niets veranderd).</summary>
    public static List<string> PasToe(VerkenInstellingen ins)
    {
        var res = new List<string>();
        foreach (var a in Lijst)
        {
            if (ins.UitgevoerdeAanpassingen.Contains(a.Sleutel)) continue;
            if (a.Voer(ins)) res.Add(a.Uitleg);
            ins.UitgevoerdeAanpassingen.Add(a.Sleutel);
        }
        return res;
    }

    /// <summary>Zijn er aanpassingen die nog niet zijn uitgevoerd? (dan moet het instellingenbestand na <see cref="PasToe"/> bewaard worden)</summary>
    public static bool Open(VerkenInstellingen ins) => Lijst.Any(a => !ins.UitgevoerdeAanpassingen.Contains(a.Sleutel));
}
