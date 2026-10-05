# Modeltreinbesturing

Een C#/WPF-programma om een modeltreinbaan digitaal te ontwerpen en automatisch te
besturen, geïnspireerd op het bekende (commerciële) modelspoor-besturingsprogramma
**Koploper** van PaHaSOFT. Modeltreinbesturing is een onafhankelijke, open source
implementatie en heeft geen enkele officiële band met Koploper of PaHaSOFT.

> **Over de totstandkoming:** de code in dit project is grotendeels
> ontwikkeld met behulp van [Claude](https://www.anthropic.com/claude), de
> AI-assistent van Anthropic, als programmeerhulpmiddel. Zie de sectie
> [Herkomst / vermelding](#herkomst--vermelding) hieronder voor meer detail.
> Dit is dan ook een goede eerste start, geen kant-en-klaar, volledig
> getest product — hulp bij testen en verbeteren is van harte welkom, zie
> [Bekend / nog te doen](#bekend--nog-te-doen).

## Over dit project

Modeltreinbesturing is opgebouwd uit vier lagen, net als het origineel waar het
door geïnspireerd is:

1. **Blokkenschema** — de logische indeling van je baan: welke blokken zijn er, en
   tussen welke blokken kan een trein rijden?
2. **Baanontwerp** — de visuele tekening van de baan zelf (rails, wissels, seinen,
   perrons), gekoppeld aan de blokken uit laag 1.
3. **Wisselstraten** — welke wissels in welke stand moeten staan om van het ene
   blok naar het andere te rijden.
4. **Treinroutes** — waar treinen daadwerkelijk rijden, handmatig of automatisch.

Zie de ingebouwde gebruiksaanwijzing (menu **Help**, of gewoon **F1** in elk
venster) voor een volledig overzicht van alle functies, sneltoetsen en tips.

### Enkele hoogtepunten

- Volledig ontworpen baanoverzicht met wissels, driewegwissels, Engelse
  wissels/kruiswissels, seinen, perrons, bezetmelders en meer.
- Automatisch én handmatig rijden, met richtingsverboden, stopverboden,
  blokgroepen, dynamische treinlengte-afhandeling en meer.
- Een apart "kijkscherm" om de baan tijdens het rijden te bedienen zonder het
  bewerk-scherm te hoeven gebruiken.
- Hardware-ondersteuning voor **Dinamo/VPEB**, **Uhlenbrock Intellibox
  (LocoNet)** en **DCC-EX**.
- Automatische, tijdgestempelde backups bij het afsluiten, met de mogelijkheid
  om een eerdere versie terug te openen.

## Bouwen en starten

Dit is een standaard .NET 8 WPF-project. Open `Modeltreinbesturing.sln` in
Visual Studio (2022 of nieuwer) en druk op **Start** (F5).

## Zelfstandig draaien, zonder Visual Studio

Wil je het programma gewoon als los .exe-bestand kunnen starten (bijvoorbeeld
om het te testen zonder zelf te programmeren), dan kun je een zogeheten
*self-contained* build maken. Dat pakt ook meteen de .NET-runtime zelf in, dus
de ontvanger hoeft niets extra's te installeren.

**Optie 1 — via Visual Studio:** rechtsklik het project in de
Solution Explorer → **Publish...** → kies een *Folder*-doel → in de
publish-instellingen: Deployment mode = *Self-contained*, Target runtime =
*win-x64* (of *win-x86*/*win-arm64*, afhankelijk van je doelsysteem) → **Publish**.
Het resultaat staat daarna in de opgegeven map, met daarin
`Modeltreinbesturing.exe`.

**Optie 2 — via de command line** (heb je wél de .NET 8 SDK nodig, niet alleen
de runtime):

```
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

Dit maakt één enkel, zelfstandig `.exe`-bestand
(`bin\Release\net8.0-windows\win-x64\publish\Modeltreinbesturing.exe`) dat je
zo kunt kopiëren en delen — geen Visual Studio of .NET-installatie nodig bij de
ontvanger.

## Meewerken

Dit project staat open voor iedereen die het wil uitproberen, verbeteren of
uitbreiden — nieuwe hardware-koppelingen, extra symbolen, betere
automatiseringslogica, noem maar op. Pull requests en issues zijn welkom. Zie
[CONTRIBUTING.md](CONTRIBUTING.md) voor de codeerconventies en een paar
bekende valkuilen in deze codebase, en de sectie hieronder voor waar
extra hulp goed van pas zou komen.

## Bekend / nog te doen

Dit is een groeiend project — onderstaande punten zijn een eerlijk overzicht
van wat nog niet (volledig) klopt of nog niet getest is, juist als startpunt
voor wie wil meehelpen:

- **Hardware: Dinamo/VPEB is getest op een echte baan, de rest niet.** De
  Dinamo-koppeling (snelheid, functies, wissels, seinen, bezetmelders) draait
  inmiddels tegen echte apparatuur en is daarbij met Wireshark-opnames van de
  originele Koploper vergeleken. De Intellibox (LocoNet)- en DCC-EX-koppelingen
  zijn met beste weten volgens de specificaties gebouwd maar nog niet
  uitgeprobeerd - test voorzichtig en begin met één wissel.
- **Baanverkenner koppelen aan het project:** via Beheren > *Baankaart
  vergelijken* leg je een baankaart naast je project. Dit signaleert alleen,
  het importeert (nog) niet automatisch.
- **Snelheidsijking is nog niet in de praktijk getest** (vereist twee echte
  bezetmelders op een bekend meettraject).
- **Dubbeltractie herkent de baas/knecht-koppeling nog niet automatisch** (zoals
  het origineel via geijkte snelheden doet) — bij ons moet je dit zelf
  aangeven.
- **De precieze vertaalslag tussen Koploper's "uit-via-naar"-principe** (elke
  overgang heeft een eigen keer-vinkje) en ons eigen, eenvoudigere
  relatiemodel is een bewust vereenvoudigde, additieve aanpak — geen
  volledige herbouw. Zie de code-comments bij `Model/BlokRelatie.cs` voor de
  precieze afweging.
- **Algemene UI/UX-polijsting** is welkom — dit project is in korte tijd
  functioneel breed uitgebouwd, maar niet elk scherm is even grondig
  gebruikstest.

## Herkomst / vermelding

Grote delen van deze applicatie zijn ontwikkeld met behulp van
[Claude](https://www.anthropic.com/claude), de AI-assistent van Anthropic, als
hulpmiddel bij het programmeren. De functionele keuzes, het testen en de
uiteindelijke verantwoordelijkheid voor het project liggen bij de menselijke
auteur(s)/bijdragers.

## Licentie

Dit project is vrijgegeven onder de [MIT-licentie](LICENSE) — vrij te
gebruiken, aan te passen en te verspreiden, ook commercieel, zolang de
licentietekst en copyrightvermelding behouden blijven.
