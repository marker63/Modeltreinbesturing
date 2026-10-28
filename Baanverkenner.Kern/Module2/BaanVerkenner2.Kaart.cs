namespace Baanverkenner.Kern;

// =====================================================================================================================
//  MODULE 2 - de baankaart opbouwen uit de kennis (zelfde formaat als module 1)
//
//  WAT: overgangen, doorgangen, wissels (met hun waarnemingen), doodlopende einden, kortsluitpunten, blokken en voorstellen worden
//  bij elk bewaarmoment opnieuw uit Kennis2 gemaakt.
//  WAAROM: zo kan de kaart nooit iets anders zeggen dan wat er werkelijk gereden is, en werken rapport, melderschema, blokkenschema en
//  wisselstraten precies zoals bij module 1.
// =====================================================================================================================

public partial class BaanVerkenner2
{
    /// <summary>WAT: de wisselstand van een stap, beperkt tot de wissels die in die knoop bewezen invloed hebben (plus hun volgers).
    /// WAAROM: de wisselstraten rekenen met 'de minimale stand waarmee de overgang gereden is'; de standen van wissels elders op de baan horen daar niet bij.</summary>
    private Configuratie Beperkt(StapWaarneming s)
    {
        var rel = _k.RelevantInKnoop(s.Melder, s.Richting);
        var afb = new List<int>();
        foreach (var a in rel)
        {
            bool st = s.Stand.Contains(a);
            if (st) afb.Add(a);
            foreach (var (v, dv) in Volgers(a, st)) if (dv) afb.Add(v);
        }
        return new Configuratie(afb.ToArray());
    }

    /// <summary>WAT: bouwt de baankaart opnieuw op uit de kennis van module 2.</summary>
    private void ProjecteerKaart()
    {
        var k = _status.Kaart;
        k.Programma = "Baanverkenner module 2";
        k.Overgangen.Clear(); k.Doorgangen.Clear(); k.Wissels.Clear(); k.Kopsporen.Clear(); k.Kortsluitpunten.Clear();
        foreach (var m in _k.Melders) k.Melder(m).DinamoBlok = _k.Blokken.TryGetValue(m, out var b) ? b : null;
        if (k.StartMelder == 0) k.StartMelder = _k.LocMelder;

        foreach (var s in _k.Stappen.Where(s => !s.Afgeleid && s.Uitkomst > 0))
        {
            var c = Beperkt(s);
            k.RegistreerOvergang(s.Melder, s.Uitkomst, s.Richting, c, s.Metingen > 0 ? s.Seconden : null);
            var o = k.Overgangen.First(x => x.Van == s.Melder && x.Naar == s.Uitkomst && x.Richting == s.Richting);
            o.AantalKeer += Math.Max(0, s.Aantal - 1);
            if (s.Via > 0 && !s.Terug) k.RegistreerDoorgang(s.Via, s.Melder, s.Uitkomst, s.Richting, c);
        }
        foreach (var s in _k.Stappen.Where(s => !s.Afgeleid && s.Uitkomst == StapUitkomst.Doodlopend))
            k.RegistreerKopspoor(s.Melder, s.Richting, Beperkt(s));
        int id = 1;
        foreach (var g in _k.Stappen.Where(s => !s.Afgeleid && s.Uitkomst == StapUitkomst.Kortsluiting).GroupBy(s => (s.Via, s.Melder, s.Richting, Beperkt(s).Sleutel)))
        {
            var s0 = g.First();
            var vondst = _k.Vondsten.FirstOrDefault(v => v.Melder == s0.Melder && v.Richting == s0.Richting && (v.Afbuigend == StapUitkomst.Kortsluiting || v.Rechtdoor == StapUitkomst.Kortsluiting));
            bool opgelost = _k.Stappen.Any(x => x.Melder == s0.Melder && x.Richting == s0.Richting && x.Uitkomst > 0 && (s0.Via == 0 || x.Via == s0.Via));
            k.Kortsluitpunten.Add(new Kortsluitpunt
            {
                Id = id++, NaMelder = s0.Melder, Richting = s0.Richting, Configuratie = Beperkt(s0), AantalKortsluitingen = g.Sum(x => x.Aantal),
                Route = s0.Via > 0 ? new List<Etappe> { new() { Van = s0.Via, Naar = s0.Melder, Richting = s0.Richting, Pad = new List<int> { s0.Via, s0.Melder }, Configuratie = Beperkt(s0) } } : new List<Etappe>(),
                Opgelost = opgelost,
                OpgelostDoorAdres = opgelost ? vondst?.Adres : null,
                OpgelostMetAfbuigend = opgelost && vondst is not null ? vondst.Rechtdoor == StapUitkomst.Kortsluiting : null
            });
        }
        // gevonden wissels, met de volgers van de overloopsets (zelfde plek, eigen Dinamo-stand)
        foreach (var v in _k.Vondsten.OrderBy(v => v.Adres))
        {
            VoegWaarnemingToe(k, v.Adres, v, false, "");
            foreach (var (vol, dvBijRd) in Volgers(v.Adres, false))
                VoegWaarnemingToe(k, vol, v, dvBijRd, $"Schakelt mee met wissel {v.Adres} (overloopset). ");
        }
        k.KruisMidden = _k.Melders.Where(m => _k.ViaAfhankelijk(m, Richting.Vooruit) || _k.ViaAfhankelijk(m, Richting.Achteruit)).OrderBy(m => m).ToList();
        var paren = _k.GerichteOvergangen.ToList();
        k.AdressenZonderEffect = _adressen.Where(a => !_k.IsGevonden(a) && paren.Count > 0 && paren.All(p => _k.IsGetest(a, p.Van, p.Naar))).ToList();
        try
        {
            k.VoorgesteldeBlokken = BlokVoorstel.Bereken(k);
            k.Wisselstraatvoorstellen = WisselstraatVoorstel.Bereken(k);
        }
        catch (Exception ex) { _log.Waarschuwing($"Blokvoorstel/wisselstraten konden niet berekend worden: {ex.Message}"); }
    }

    /// <summary>Voegt de waarneming van vondst <paramref name="v"/> toe bij adres <paramref name="adres"/>. <paramref name="omgedraaid"/> = dit adres krijgt bij
    /// 'meester rechtdoor' het Dinamo-commando afbuigend (omgekeerd aangesloten volger), dus rechtdoor en afbuigend wisselen.</summary>
    private static void VoegWaarnemingToe(Baankaart k, int adres, WisselVondst2 v, bool omgedraaid, string voor)
    {
        var w = k.Wissels.FirstOrDefault(x => x.Adres == adres);
        if (w is null) { w = new WisselVondst { Adres = adres }; k.Wissels.Add(w); k.Wissels.Sort((a, b) => a.Adres.CompareTo(b.Adres)); }
        int? rd = omgedraaid ? v.Afbuigend : v.Rechtdoor;
        int? af = omgedraaid ? v.Rechtdoor : v.Afbuigend;
        string T(int? u) => u is int x ? StapUitkomst.Tekst(x) : "nog niet gereden";
        var ob = new WisselWaarneming
        {
            NaMelder = v.Melder, Via = v.Via > 0 ? v.Via : null, Richting = v.Richting,
            Configuratie = v.Stand.Count == 0 ? "alle rechtdoor" : "afbuigend: " + string.Join(", ", v.Stand),
            Toelichting = voor + $"Module 2: vanaf melder {v.Via} door melder {v.Melder} ({v.Richting.Tekst()}): rechtdoor -> {T(rd)}, afbuigend -> {T(af)}."
        };
        if (af == StapUitkomst.Kortsluiting && rd is int r1 && r1 > 0)
        { ob.Soort = WaarnemingSoort.KortsluitingBijOmzetten; ob.VolgendeBijRechtdoor = r1; }
        else if (rd == StapUitkomst.Kortsluiting && af is int a1 && a1 > 0)
        { ob.Soort = WaarnemingSoort.Splitsing; ob.VolgendeBijAfbuigend = a1; }
        else
        {
            ob.Soort = WaarnemingSoort.Splitsing;
            ob.VolgendeBijRechtdoor = rd is int r2 && r2 > 0 ? r2 : null;
            ob.VolgendeBijAfbuigend = af is int a2 && a2 > 0 ? a2 : null;
        }
        w.Waarnemingen.Add(ob);
    }
}
