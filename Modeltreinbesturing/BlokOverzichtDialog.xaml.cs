using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using Modeltreinbesturing.Model;

namespace Modeltreinbesturing;

/// <summary>Gebruikersverzoek: "als ik rechtsklik op een blok dat ik dan een overzicht krijg
/// van letterlijk alle eigenschappen die betrekking hebben op dat blok en als er een loc
/// staat ook die van de loc... verdelen over tabbladen." Bewust ALLEEN-LEZEN: bewerken kan
/// via de bestaande schermen (Blok-eigenschappen in het bouwscherm, Treinen beheren) - dit
/// is puur een snel, compleet overzicht voor de eindgebruiker in het kijkscherm, zonder
/// daarvoor te hoeven inloggen als beheerder.</summary>
public partial class BlokOverzichtDialog : Window
{
    private readonly OnderhoudsBeheerder _onderhoudsBeheerder;
    private readonly Blok _blok;

    public BlokOverzichtDialog(Blok blok, BlokBeheerder blokBeheerder, OnderhoudsBeheerder onderhoudsBeheerder, string? rijstatus)
    {
        InitializeComponent();
        _blok = blok;
        _onderhoudsBeheerder = onderhoudsBeheerder;
        Title = $"Blokoverzicht - blok {blok.Nummer}";

        BlokTekst.Text = BouwBlokTekst(blok);
        MeldersTekst.Text = BouwMeldersTekst(blok);
        StatusOverzichtTekst.Text = BouwStatusTekst(blok, blokBeheerder);
        var trein = blokBeheerder.LocOpBlok(blok);
        LocTekst.Text = trein != null ? BouwLocTekst(trein, rijstatus) : "(geen loc op dit blok geplaatst)";
    }

    private static string BouwBlokTekst(Blok b)
    {
        var sb = new StringBuilder();
        void Regel(string label, object? waarde) => sb.AppendLine($"{label,-28}: {waarde}");

        Regel("Nummer", b.Nummer);
        Regel("Omschrijving", string.IsNullOrWhiteSpace(b.Omschrijving) ? "(geen)" : b.Omschrijving);
        Regel("Type", b.Type);
        Regel("Max. treinlengte", b.MaxTreinlengte > 0 ? $"{b.MaxTreinlengte} cm" : "(onbeperkt)");
        Regel("Actie bij te lange trein", b.TeLangeTreinActie);
        Regel("Max. snelheid", b.MaxSnelheid > 0 ? $"{b.MaxSnelheid} km/u" : "(geen limiet)");
        Regel("Uitrol-override", b.UitrolSecondenOverride >= 0 ? $"{b.UitrolSecondenOverride} sec" : "(standaard)");
        Regel("Vergrendeld", b.Vergrendeld ? $"Ja - {b.VergrendelReden ?? "(geen reden opgegeven)"}" : "Nee");
        Regel("Lengte", b.LengteCm > 0 ? $"{b.LengteCm} cm" : "(niet ingevuld)");
        Regel("Positie op schema", $"X={b.SchemaX:0}, Y={b.SchemaY:0}");
        return sb.ToString();
    }

    private static string BouwMeldersTekst(Blok b)
    {
        var sb = new StringBuilder();
        sb.AppendLine("-- Generieke bezetmeldpunten --");
        if (b.Bezetmeldpunten.Count == 0)
            sb.AppendLine("(geen)");
        foreach (var m in b.Bezetmeldpunten)
            sb.AppendLine($"Melder {m.MeldernNummer,-6} rol={m.Rol,-10} vorig blok vrijgeven={m.MagVorigBlokVrijgeven}" +
                          (m.MaxTreinlengte > 0 ? $" max.treinlengte={m.MaxTreinlengte}cm" : ""));

        sb.AppendLine();
        sb.AppendLine("-- Richtingsafhankelijke bezetmeldingen --");
        if (b.RichtingsBezetmeldingen.Count == 0)
            sb.AppendLine("(geen)");
        foreach (var r in b.RichtingsBezetmeldingen.OrderBy(r => r.VanBlok.Nummer))
            sb.AppendLine(r.ToString());

        sb.AppendLine();
        sb.AppendLine("-- Geleerde reistijden (naar dit blok) --");
        if (b.GeleerdeReistijden.Count == 0)
            sb.AppendLine("(nog geen enkele echte meting)");
        foreach (var g in b.GeleerdeReistijden.OrderBy(g => g.VanBlok.Nummer).ThenBy(g => g.Trein.Omschrijving))
            sb.AppendLine(g.ToString());

        return sb.ToString();
    }

    private static string BouwStatusTekst(Blok b, BlokBeheerder beheerder)
    {
        var sb = new StringBuilder();
        void Regel(string label, bool waarde) => sb.AppendLine($"{label,-20}: {(waarde ? "JA" : "nee")}");
        Regel("Bezet", beheerder.IsBezet(b));
        Regel("Gereserveerd", beheerder.IsGereserveerd(b));
        Regel("Staart", beheerder.IsStaart(b));
        Regel("Foutmelding", beheerder.IsFoutmelding(b));
        Regel("Handmatig bezet", beheerder.IsHandmatigBezet(b));
        return sb.ToString();
    }

    private static string BouwLocTekst(Trein t, string? rijstatus)
    {
        var sb = new StringBuilder();
        void Regel(string label, object? waarde) => sb.AppendLine($"{label,-28}: {waarde}");

        Regel("Omschrijving", t.Omschrijving);
        Regel("Treintype", t.Treintype?.Omschrijving ?? "(geen)");
        Regel("Decoderadres", t.DecoderAdres);
        Regel("Decoderstappen", t.DecoderStappen);
        Regel("Rijrichting omgekeerd", t.OmgekeerdeRijrichting);
        Regel("Rangeer-modus", t.Rangeren);
        Regel("Mag niet keren", t.MagNietKeren);
        Regel("Treinstel", t.IsTreinstel);
        Regel("Lengte", t.Lengte > 0 ? $"{t.Lengte} cm" : "(niet ingevuld)");
        Regel("Acceleratie 0->50", $"{t.AccelaratieVan0Naar50Seconden} sec");
        Regel("Rem 50->0", $"{t.RemVan50Naar0Seconden} sec");
        Regel("Totale rijtijd", TimeSpan.FromSeconds(t.TotaleRijtijdSeconden).ToString(@"hh\:mm\:ss"));
        Regel("Totale afgelegde afstand", $"{(t.TotaleAfgelegdeAfstandCm / 100.0).ToString("0.0", CultureInfo.InvariantCulture)} m");
        if (t.GekoppeldeKnecht != null) Regel("Gekoppelde knecht-loc", t.GekoppeldeKnecht.Omschrijving);

        sb.AppendLine();
        sb.AppendLine("-- Actuele rijstatus --");
        Regel("Status", rijstatus ?? "Staat stil (geen actieve rit bekend)");

        return sb.ToString();
    }

    private void OnderhoudMelden_Click(object sender, RoutedEventArgs e)
    {
        new OnderhoudsDialog(_onderhoudsBeheerder, vooringevuldObject: $"Blok {_blok.Nummer}") { Owner = this }.ShowDialog();
    }

    private void Sluiten_Click(object sender, RoutedEventArgs e) => Close();
}
