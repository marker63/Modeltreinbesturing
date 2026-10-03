# Meewerken aan Modeltreinbesturing

Fijn dat je overweegt om mee te helpen! Dit document beschrijft de
conventies die in dit project gehanteerd worden, en een aantal concrete
valkuilen die tijdens de ontwikkeling zijn tegengekomen — met de bedoeling
dat je ze niet opnieuw hoeft te ontdekken.

## Taal en naamgeving

- **Alle code — klassenamen, methodes, variabelen, comments — is in het
  Nederlands.** Dit is een bewuste, consistente keuze door het hele project
  heen; nieuwe code sluit daar het beste bij aan.
- XML-doc-comments (`/// <summary>`) leggen niet alleen uit *wat* iets doet,
  maar vaak ook *waarom* — zeker bij niet-voor-de-hand-liggende keuzes. Dat
  mag je gerust ook zo blijven doen; het scheelt latere lezers (inclusief
  jezelf over een paar maanden) veel puzzelwerk.

## Architectuur in het kort

Het programma is opgebouwd uit vier lagen (zie ook README.md en de
ingebouwde gebruiksaanwijzing, F1):

1. **Blokkenschema** (`MainWindow`) — het logische model: blokken, relaties,
   bezetmeldpunten. Dit venster is normaal **verborgen**; het draait op de
   achtergrond en houdt de kernstatus bij.
2. **Baanontwerp** (`BaanontwerpWindow`) — de visuele tekening. Dit venster
   heeft een dubbele rol: met `kijkModus:false` is het het bewerkscherm, met
   `kijkModus:true` is het het "kijkscherm" dat de eindgebruiker standaard
   ziet.
3. **Wisselstraten** (`WisselstraatBeheerder`, `WisselstratenDialog`) — welke
   wissels in welke stand moeten staan voor een bepaalde blok-naar-blok-rit.
4. **Treinroutes** (`TreinrouteWindow`) — de daadwerkelijke rijsimulatie,
   zowel vaste routes als automatisch rijden.

Elke laag heeft een eigen `...Beheerder`-klasse die de data en logica van
die laag bijhoudt; de vensters zijn vooral verantwoordelijk voor tekenen en
gebruikersinteractie.

## Bekende valkuilen

Deze zijn er, met vallen en opstaan, tijdens de ontwikkeling uitgekomen —
het scheelt tijd om ze te kennen voordat je erin trapt:

- **`Window.IsLoaded` is GEEN betrouwbare "is dit venster nog open?"-check**
  zodra een venster bewust nooit `.Show()` krijgt (zoals `TreinrouteWindow`,
  dat op de achtergrond blijft draaien voor de simulatie terwijl het
  kijkscherm actief is). Zo'n venster heeft `IsLoaded == false`, permanent —
  zowel van buitenaf (`venster.IsLoaded`) als van binnenuit
  (`this.IsLoaded` in een event-handler). Gebruik in plaats daarvan een
  expliciete `is null`-check gecombineerd met een eigen boolean die je zelf
  bijhoudt via het venster se `Closed`-event.
- **`WindowStartupLocation.CenterOwner` gaat mis als de eigenaar (owner)
  verborgen kan zijn** — het venster verschijnt dan op een onverwachte
  plek. Gebruik `CenterScreen` voor vensters die vanuit het kijkscherm
  geopend worden.
- **Bezetting/reservering wijzigen via `BlokBeheerder.PlaatsLoc`/
  `VerwijderLoc` vuurt GEEN events af.** Roep je deze ergens nieuw aan, denk
  er dan zelf aan om `Redraw()` (of het relevante event) aan te roepen —
  anders blijft het scherm de oude situatie tonen.
- **Wissels staan zelden letterlijk "in" de rechthoek van een blok.** Als je
  iets moet koppelen tussen een blok en een wissel/lijn op basis van hun
  positie op het canvas, gebruik dan het bestaande
  ankerpuntennetwerk (`Lijn.PuntBlokAnkers`/`PuntWisselAnkers`/
  `PuntLijnAnkers`) om de daadwerkelijke railverbinding te volgen, in plaats
  van een geschatte pixelafstand — dat laatste bleek in de praktijk
  onbetrouwbaar (zie de git-historie/PR's rond de wissel-kleurcodering voor
  een concreet voorbeeld van waarom).
- **XAML-tagtelling als snelle sanity-check kan valse alarmen geven** door
  losse `->`-tekens in tooltip-teksten of commentaar (elk zo'n teken telt
  als een extra `<`/`>`-paar-mismatch zonder dat er iets mis is). Bij een
  onverwachte mismatch: eerst tellen hoeveel `->`-voorkomens het bestand
  heeft voordat je een echte fout aanneemt.
- **Bij het toevoegen van een blok in het baanontwerp**, denk aan de
  bijbehorende seinen en de dienstregeling in dezelfde wijziging — dat hoort
  bij elkaar.

## Pull requests

- Kleine, gerichte PR's zijn makkelijker te beoordelen dan grote,
  alles-in-één wijzigingen.
- Test in ieder geval dat het project nog compileert en dat de
  ingebouwde gebruiksaanwijzing (F1) bijgewerkt is als je gebruikersgedrag
  wijzigt.
- Nieuwe hardware-koppelingen (naast Dinamo/Intellibox/DCC-EX) zijn zeer
  welkom, evenals daadwerkelijke tests tegen fysieke apparatuur — dat is op
  dit moment de grootste blinde vlek van het project.
