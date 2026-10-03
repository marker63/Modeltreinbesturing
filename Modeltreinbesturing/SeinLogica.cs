using Modeltreinbesturing.Model;

namespace Modeltreinbesturing;

public enum SeinAspect { Rood, Geel, Groen }

/// <summary>
/// Bepaalt wat een gekoppeld sein moet tonen, volgens het echte NS-seinstelsel/blokbeveiliging:
/// "op de grens van 2 blokken staat een sein, dat de toegang tot het VOLGENDE blok beveiligt"
/// (VPEB Blokbeveiliging). Sein.GekoppeldBlok is dus het blok waar de trein UIT komt (aan de
/// uitrijdende kant van dat blok staat het sein) - het sein bewaakt niet dat blok zelf, maar het
/// blok erna. Cascade voor geel: "sein van het blok waar de trein in rijdt op rood, het blok
/// daarachter op geel, het blok daar weer achter op groen" (VVRV cursusmateriaal).
/// </summary>
public static class SeinLogica
{
    /// <summary>
    /// Welk blok komt er ná bronBlok, langs de op dit moment actieve route? Bij een splitsing
    /// (1 of meer wisselstraten vanaf bronBlok) is dat het doelblok van de wisselstraat waarvan
    /// de wissels op dit moment daadwerkelijk zo staan (GewensteStand komt overeen met de echte
    /// Stand). Zonder splitsing is dat gewoon de ene BlokRelatie-opvolger. Null als dat niet
    /// eenduidig is (doodlopend blok, of een onopgeloste/ambigue splitsing).
    /// </summary>
    public static Blok? BepaalBewaaktBlok(Blok bronBlok, BlokBeheerder blokBeheerder, WisselstraatBeheerder wisselstraatBeheerder)
    {
        var wisselstratenVanaf = wisselstraatBeheerder.Wisselstraten.Where(w => w.Van == bronBlok).ToList();
        if (wisselstratenVanaf.Count > 0)
        {
            var actieve = wisselstratenVanaf
                .Where(w => w.Wissels.Count > 0 && w.Wissels.All(x => x.Wissel.Stand == x.GewensteStand))
                .ToList();
            return actieve.Count == 1 ? actieve[0].Naar : null;
        }

        var opties = blokBeheerder.VolgendeBlokken(bronBlok).ToList();
        return opties.Count == 1 ? opties[0] : null;
    }

    /// <summary>Null betekent: geen koppeling, de handmatige Stand van het sein geldt.
    /// alleSeinen is optioneel maar nodig voor SeinType.Voorsein (om het volgende sein op
    /// de route te kunnen opzoeken) - zonder deze parameter valt een voorsein terug op
    /// groen. maxDiepte beschermt tegen een (verkeerd geconfigureerde) cirkel van
    /// voorseinen die naar elkaar verwijzen - zonder deze limiet zou dat een oneindige
    /// recursie/StackOverflow geven i.p.v. gewoon een minder mooi seinbeeld.</summary>
    public static SeinAspect? BepaalAspect(Sein sein, BlokBeheerder blokBeheerder, WisselstraatBeheerder wisselstraatBeheerder, IEnumerable<Sein>? alleSeinen = null, int maxDiepte = 20)
    {
        // Rangeersein aan een wisselstraat gaat vóór een gewone blokkoppeling: "Het sein
        // toont groen bij een ingestelde rijweg en valt weer op rood bij de eerste
        // bezetmelding na de wisselstraat" - dus groen zolang de wissels van deze
        // wisselstraat in de gewenste stand staan, en weer rood zodra het blok NA de
        // wisselstraat bezet raakt. Handig vóór een wisselstraat met meerdere
        // vertreksporen, waar één sein voor alle sporen tegelijk geldt.
        if (sein.IsRangeersein && sein.GekoppeldeWisselstraat != null)
        {
            var wisselstraat = sein.GekoppeldeWisselstraat;
            bool rijwegIngesteld = wisselstraat.Wissels.Count > 0 && wisselstraat.Wissels.All(w => w.Wissel.Stand == w.GewensteStand);
            if (!rijwegIngesteld) return SeinAspect.Rood;
            return blokBeheerder.IsBezet(wisselstraat.Naar) ? SeinAspect.Rood : SeinAspect.Groen;
        }

        if (sein.GekoppeldBlok is null) return null;

        var bewaakt = BepaalBewaaktBlok(sein.GekoppeldBlok, blokBeheerder, wisselstraatBeheerder);
        if (bewaakt is null) return SeinAspect.Groen; // geen (eenduidig) vervolgblok: niets te waarschuwen

        // Voorsein: geeft geen eigen stopgebod, kondigt puur het aspect van het
        // EERSTVOLGENDE sein op de route aan - dus niet de gewone rood/geel/groen-cascade
        // op basis van bezetting, maar simpelweg het aspect van dat andere sein overnemen.
        if (sein.Type == SeinType.Voorsein)
        {
            if (maxDiepte <= 0) return SeinAspect.Groen; // veiligheidsklep tegen een cirkel van voorseinen
            var volgendSein = alleSeinen?.FirstOrDefault(s => s != sein && s.GekoppeldBlok == bewaakt);
            return volgendSein != null ? BepaalAspect(volgendSein, blokBeheerder, wisselstraatBeheerder, alleSeinen, maxDiepte - 1) ?? SeinAspect.Groen : SeinAspect.Groen;
        }

        SeinAspect resultaat;
        if (blokBeheerder.IsBezet(bewaakt))
        {
            resultaat = SeinAspect.Rood;
        }
        else
        {
            var daarna = BepaalBewaaktBlok(bewaakt, blokBeheerder, wisselstraatBeheerder);
            resultaat = daarna != null && blokBeheerder.IsBezet(daarna) ? SeinAspect.Geel : SeinAspect.Groen;
        }

        // 2-standen seinen (en het functioneel identieke dwergsein) tonen nooit geel - waar
        // een 3-standen sein zou waarschuwen met geel, blijft dit type gewoon op groen staan.
        if (resultaat == SeinAspect.Geel && (sein.Type == SeinType.Standaard2Standen || sein.Type == SeinType.Dwergsein))
            resultaat = SeinAspect.Groen;

        return resultaat;
    }
}
