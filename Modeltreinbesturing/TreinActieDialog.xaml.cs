using System;
using System.Windows;
using Modeltreinbesturing.Model;

namespace Modeltreinbesturing;

/// <summary>Gebruikersverzoek (kijkscherm): "als ik op het blok klik waar de trein staat dat
/// ik dan de mogelijkheid krijg om deze ene trein tijdelijk te stoppen, en eventueel later
/// weer te starten, of als hij defect blijkt uit het blok te halen" - en later aangevuld
/// met: "een knop om de loc te keren zodat als hij verkeerd om rijdt ik dat kan oplossen
/// zonder de loc van de baan te hoeven halen." Hergebruikt bewust de AL BESTAANDE
/// Pauzeren/Hervatten/Keren/Reservering-opheffen-mechanismen (voorheen alleen bereikbaar
/// via de knoppen bij een in "Overzicht locomotieven" geselecteerde loc) - dus GEEN nieuwe
/// rij-logica, puur een handigere, directe ingang ernaartoe vanaf het blok zelf. Zelfde
/// patroon als die bestaande knoppen: alle acties blijven aanklikbaar (geen vooraf
/// berekende aan/uit-status, foutgevoelig om precies te bepalen) en de methode zelf meldt
/// via de teruggegeven tekst of de actie wel/niet van toepassing was.</summary>
public partial class TreinActieDialog : Window
{
    public TreinActieDialog(Trein trein, Func<string> pauzeer, Func<string> hervat, Func<string> keer, Func<string> reserveringOpheffen, Action verwijderen)
    {
        InitializeComponent();
        OmschrijvingTekst.Text = $"Loc '{trein.Omschrijving}' (decoderadres {trein.DecoderAdres})";
        StatusTekst.Text = "Kies een actie.";

        PauzeerKnop.Click += (_, _) => StatusTekst.Text = pauzeer();
        HervatKnop.Click += (_, _) => StatusTekst.Text = hervat();
        KeerKnop.Click += (_, _) => StatusTekst.Text = keer();
        ReserveringOpheffenKnop.Click += (_, _) => StatusTekst.Text = reserveringOpheffen();
        VerwijderKnop.Click += (_, _) => { verwijderen(); Close(); }; // deze loc is na verwijderen niet meer relevant voor dit dialoogvenster
    }

    private void Sluiten_Click(object sender, RoutedEventArgs e) => Close();
}
