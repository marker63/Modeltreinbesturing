# Bugboek Modeltreinbesturing

Genummerd logboek van elke gevonden en (waar vermeld) gefixte bug, zodat we niet
steeds dezelfde fout opnieuw tegenkomen. Nieuwe bugs komen ONDERAAN met het
eerstvolgende nummer, ongeacht in welk bestand ze zitten. Verwijs in code-comments
naar het nummer (bijv. "BUG #28") zodat de bug in de broncode en hier terug te
vinden is.

Status: **Open** (nog niet gefixt), **Gefixt** (opgelost en in het project), of
**Verworpen** (onderzocht, bleek geen bug / bewust gedrag).

---

## #1 - Dinamo F-bit bij opstarten altijd op 1 (geen enkele loc bewoog)
**Status:** Gefixt. Dinamo start met een eigen Fault-bit die pas via een expliciet
"Reset Fault"-commando (§3.1) opgeheven wordt. Zonder dat commando rijdt geen enkel
voertuig, ook niet als de snelheidscommando's zelf al correct zijn.
Zie `DinamoHardware.VerbindenAsync`/`StuurResetFault`.

## #2 - Magneetartikel-commando (wissel zetten) verkeerd formaat
**Status:** Gefixt. Prefix moest "0001" zijn (niet "0010"), en de optionele
tijd-byte (0x0c) moet altijd meegestuurd worden - Koploper laat 'm nooit weg.
Zie `DinamoHardware.ZetWissel`.

## #3 - Wisselpuls te kort voor een zware (Engelse) kruiswissel
**Status:** Gefixt. Eén enkel commando gaf een te korte puls voor een kruiswissel
met twee motoren. Wordt nu 6x snel achter elkaar (binnen enkele ms, niet verspreid
over meerdere 200ms-ticks) herhaald. Zie `DinamoHardware.ZetWissel`/
`AantalHerhalingenWisselcommando`.

## #4 - Switch-event (bezetmelding) werd nooit herkend
**Status:** Gefixt. Bitmasker-check testte een patroon dat na de eerdere maskering
nooit waar kon zijn, en de bezet/vrij-bit (C) stond op de verkeerde positie.
Zie `DinamoHardware.Poort_DataReceived`.

## #5 - Bezetmelding kon spoorloos verdwijnen bij ongelukkige seriële timing
**Status:** Gefixt. `SerialPort.DataReceived` garandeert geen volledige
datagrammen per aanroep; een halverwege doorgeknipt bericht werd voorheen domweg
overgeslagen. Nu een persistente buffer (`_ontvangstBuffer`) die onvolledige
berichten bewaart tot de rest binnen is.

## #6 - Periodieke melderstatus-herbevestiging triggerde een valse rijrichting-omkering
**Status:** Gefixt. Een ONGEWIJZIGDE "nog steeds bezet"-herhaling werd aangezien
voor een nieuwe bezetmelding, wat een automatische richtingscorrectie en
vervolgens een spookmelding-noodstop veroorzaakte. Nu alleen nog een ECHTE
wijziging (of de eerste waarneming) doorgegeven. Zie `_laatstDoorgegevenStatus`.

## #7 - Eén enkele FIFO-wachtrij voor wisselcommando's én melderstatus-aanvragen
**Status:** Gefixt. Bij een project met veel bezetmelders groeide de wachtrij
onbeperkt (periodieke herbevestiging voegt sneller toe dan de rustpauze
verwerkt), waardoor een urgent wisselcommando soms minutenlang vastzat achter een
opgelopen achterstand. Nu een aparte prioriteits-wachtrij (wissel/sein) en een
normale wachtrij (melderstatus e.d.), prioriteit altijd eerst verwerkt.

## #8 - Melderstatus-aanvragen en bulk-snelheidsbroadcasts verdrongen de
   wissel-initialisatie / vertraagden de snelheidsramp
**Status:** Gefixt. Beide gingen rechtstreeks, ongepauzeerd de hardware in en
overspoelden Dinamo's eigen verzendwachtrij. Melderstatus-aanvragen gaan nu
rechtstreeks (geen reden voor een kunstmatige pauze bij een niet-fysieke
aanvraag), losse loc-snelheidscommando's blijven direct, en bulk-broadcasts
(`StuurLocSnelheidCommandoGepauzeerd`) lopen nu via de normale wachtrij.

## #9 - Kruiswissel (Engelse wissel) als één gecombineerde Stand gemodelleerd
**Status:** Gefixt (architectuur). De twee motoren (Adres/Adres2) zijn fysiek
volledig onafhankelijk en kunnen dus ook een "gemengde" stand hebben. Model nu
met eigen `StandAdres`/`StandAdres2` per motor i.p.v. één afgeleide `Stand` +
polariteitsvlag.

## #10 - Geen manier om een gemengde kruiswissel-combinatie te TEKENEN/vastleggen
**Status:** Gefixt. Dubbelklikken op een Engelse wissel in het bouwscherm kon
alleen beide motoren SYMMETRISCH togglen. Nu: Ctrl+dubbelklik = alleen motor 1
(Adres), Alt+dubbelklik = alleen motor 2 (Adres2), elk met eigen hardwarecommando.
Zie `BaanontwerpWindow.xaml.cs`.

## #11 - Kijkscherm-keercorrectie werd stil overschreven door een stale ramp-timer
**Status:** Gefixt. `StuurSnelheidNaarHardware` start een `DispatcherTimer` die de
snelheid geleidelijk opbouwt; die berekent de rijrichting ÉÉN keer bij de start en
bleef die rijrichting daarna op elke volgende tik hergebruiken - ook als de
gebruiker via de keer-knop (`KeerTreinIndienActief`) de richting inmiddels alweer
had omgedraaid. Fix: die ramp-timer wordt nu EERST gestopt voordat de gecorrigeerde
richting verstuurd wordt. Geverifieerd met een losse Python-simulatie van de
exacte bytes-reeks.

## #12 - Wissel 14 (blok4/5-pendel) kreeg nooit de kans om gekozen te worden
**Status:** Gefixt. De "voorkom heen-en-weer pendelen"-regel sloot VorigBlok altijd
categorisch uit zodra er een alternatief was, ook als VorigBlok verderop zelf nog
een eigen, ANDER vervolgblok had (dus geen echte pendel maar een legitieme
aftakking). Nu wordt eerst onderzocht of de weg heen en de weg terug dezelfde
wissels/kruiswissel gebruiken voordat VorigBlok als "dood" bestempeld wordt.

## #13 - Trein stopte altijd bij de EERSTE sectie van een (tijdelijk) doodlopend blok
**Status:** Gefixt. Als er nog geen vervolgblok beschikbaar was, stopte de trein
meteen bij de aankomstmelder i.p.v. door te rollen tot de echte, verst gelegen
stopmelder van dat blok. Zie `BepaalStopmelder`/de "stopmelderNogNietBereikt"-check
in `AutomatischeStapProberen`.

## #14 - Trein bleef voor altijd wachten als het enige vervolgblok via een defecte
   wissel onbereikbaar was, zonder ooit te stoppen OF te loggen
**Status:** Gefixt (in twee stappen): eerst duidelijke logging toegevoegd (max 1x
per 10 sec) die onderscheid maakt tussen "alles bezet", "defecte wissel
onderweg" en "doel vergrendeld"; daarna alsnog een expliciet stopcommando
toegevoegd zodat de trein niet op zijn oude snelheid blijft doorrollen terwijl
hij "wacht".

## #15 - Geen terugvalroute als het enige vervolgblok (tijdelijk) niet beschikbaar is
**Status:** Gefixt. Als allerlaatste redmiddel mag de trein terugkeren naar
VorigBlok (fysieke kering), ook als die overgang normaal via een Richtingsverbod
is uitgeschakeld voor gewoon automatisch rijden.

## #16 - Terugval naar VorigBlok (#15) leidde zelf weer tot een spookmelding+noodstop
**Status:** Gefixt. Eenmaal teruggekeerd naar bijv. blok 4, koos de GEWONE
kandidaat-keuze daarna gewoon weer "terug naar blok 5" (met hogere kans dan
vooruit). Opgelost met gerichte, symmetrische Richtingsverboden
("vanuit X niet terug naar het blok waar je net vandaan kwam").

## #17 - Spookmelding 4,8 sec na vertrek (veel te snel voor de 30-sec-veiligheidstimeout)
**Status:** Gefixt. Bij de EERSTE stap van een rit is `VorigBlok` nog null, dus de
richtingsspecifieke meldpunt-opzoeking viel terug op een blok dat alleen een
Voorblok-melder had (geen apart Stopsectie-meldpunt) en leverde niets op -
`StartStaartFase` viel dan stilzwijgend terug op een vaste 0,8-sec-timer. Nu valt
`BepaalVertrekmelder` in dat geval terug op de Voorblok-melder zelf.

## #18 - Zelflerende bezetmelder-volgorde sneed een complete lijst terug naar 1 entry
**Status:** Gefixt. Het waarnemingsvenster sluit al bij de aankomstmelder, dus een
latere stopmelder (bij een blok waar gekeerd moet worden) werd voor DIE
waarneming nooit meegenomen. Een nieuwe waarneming mag de lijst nu nooit korter
maken dan wat er al stond.

## #19 - Permanent-bezet-blok: een vers geplaatste loc op een sensor werd niet herkend
**Status:** Gefixt. Alleen spontane Switch-events (overgangen) werden verwerkt;
een loc die al vóór de start op de verwachte melder stond, gaf nooit zo'n
overgang. Nu wordt bij elke (her)verbinding de ACTUELE status van elk bekend
meldpunt opgevraagd (`VraagAlleMelderStatusOp`), en wordt ook het "antwoord op
een aanvraag"-patroon (11CSSSS) herkend, niet alleen het spontane (10CSSSS).

## #20 - Kritieke veiligheidstimeout die een aankomst "aannam" zonder bevestiging
**Status:** Verworpen/verwijderd op uitdrukkelijk gebruikersverzoek ("dit is niet
realistisch als de loc niet fysiek verder gaat"). Vervangen door een expliciete
`Vastgelopen`-vlag: de rit pauzeert volledig totdat de echte melding alsnog
binnenkomt of de gebruiker ingrijpt - geen aanname meer.

## #21 - Noodstop-mechanisme stuurde (bewust) commando's naar een nooit-gebruikte
   placeholder-trein
**Status:** Verworpen - bleek bewust, gedocumenteerd defense-in-depth-gedrag
(`VoerNoodstopUit` stuurt expres naar ALLE trein×blok-combinaties, niet alleen
actieve), geen bug.

## #22 - JSON-project: duplicate `$id` bij het toevoegen van een nieuwe wisselstraat
**Status:** Gefixt. Een handmatig/script-matig toegevoegde wisselstraat kopieerde
een embedded object (Kruiswissel) in plaats van een `{"$ref": ...}` te gebruiken,
waardoor hetzelfde `$id` twee keer voorkwam en het project niet meer laadde. Fix:
altijd `$ref` naar het canonieke object, nooit een kopie.

## #23 - Wisselstraten blok7→blok3 en blok4→blok3 bestonden, blok3→blok7 ontbrak
**Status:** Gefixt. Toegevoegd als spiegeling van de al veldgeteste blok7→blok3
wisselstraat (zelfde kruiswissel-combinatie + wissel5).

## #24/#25/#26 - blok7→blok1, blok1→blok7, blok4→blok1
**Status:** Open/uitgesteld. De kruiswissel-motorcombinatie voor blok7↔blok1 is
bekend (13=Rechtdoor, 10=Afbuigend) maar het is nog niet bevestigd of daar - net
als bij de andere twee kruiswissel-routes - ook wissel 5 (of een andere gewone
wissel) bij nodig is. blok4→blok1 heeft nog helemaal geen BlokRelatie. Met
bug #10 (Ctrl/Alt-toggle) kan dit nu wel zelf in het bouwscherm vastgelegd
worden.

## #27 - Trein reed van blok7 ongereserveerd door naar blok6 (spookmelding)
**Status:** Gefixt. Zelfde architectuurfout als #11, nu in
`AutomatischeStapProberen`: als er nog geen kandidaat-vervolgblok is, wacht de
trein BEWUST op de echte stopmelder van het huidige blok voordat er geremd wordt
- maar een nog actieve snelheids-ramp-timer van de vorige stap bleef ondertussen
gewoon doortikken en de snelheid verder VERHOGEN. De trein schoot zo, nog vóórdat
de stopmelder van blok7 ooit afging, door naar het niet-gereserveerde blok6. Fix:
de ramp-timer wordt nu meteen gestopt zodra blijkt dat er geen kandidaat is.

## #28 - Wissel bleef na een kortsluiting fysiek in de oude stand, scherm toonde de
   gewenste (nieuwe) stand alsof het gelukt was
**Status:** Gefixt (gedeeltelijk, zie "Belangrijke beperking" hieronder).
Waargenomen: bij het opstarten direct een kortsluitmelding (rood) op het
rijscherm, na een lange wachttijd ging de trein alsnog rijden in blok3; later
bleek wissel 14 op het scherm "afbuigend" te tonen terwijl hij fysiek nog gewoon
op "rechtdoor" stond, en de loc reed dan ook daadwerkelijk rechtdoor blok5 in.
Oorzaak: het Dinamo-magneetartikel-commando is een PULS zonder enige
terugmelding - er is geen sensor die de fysieke wisselstand zelf bevestigt. Als
er tijdens zo'n puls een echte, fysieke kortsluiting optreedt, kan de spoel een
te korte/onderbroken puls krijgen en in de oude stand blijven staan, terwijl de
software allang "klaar" denkt te zijn. Fix: zodra Dinamo's foutstatus weer wordt
opgeheven, stuurt de software nu automatisch ALLE wissels/driewegwissels/
kruiswissels opnieuw hun gewenste stand (`HardwareBeheerder.
HerinitialiseerAlleWissels`, gedeeld met de bestaande connect-tijd-initialisatie
in plaats van twee losse kopieën van dezelfde logica).
**Belangrijke beperking:** dit is en blijft een veiligheidsnet, geen garantie -
zonder een echte terugmeldsensor per wissel kan de software een fysiek
foutgegane stand nooit met 100% zekerheid detecteren/herstellen. Bij
terugkerende twijfel over wissel 14 specifiek: controleer na een kortsluiting
even visueel of hij inderdaad is meegekomen.

## #29 - BUG #28-fix veroorzaakte zelf een crash-lus: honderden keren per minuut
   "Fout bij lezen van Dinamo: The calling thread cannot access this object
   because a different thread owns it"
**Status:** Gefixt. Gevonden direct na het publiceren van #28 (hardware-log van
een sessie die zelfs niet "gestart" was - alleen verbinden al gaf de ruis).
Oorzaak, in twee lagen:
1. `DinamoHardware.Poort_DataReceived` loopt op de achtergrondthread van
   `SerialPort.DataReceived`, niet op de WPF-UI-thread. De #28-fix riep vanuit
   die thread rechtstreeks `HerinitialiseerAlleWissels` aan, die UI-gebonden
   state aanraakt (`Symbolen`, en via `WisselStandGewijzigd` een `Redraw()`) -
   dat mag alleen op de UI-thread, exact zoals `MainWindow.
   Bezetmelding_VanHardware` dat al via `Dispatcher.Invoke` deed voor
   soortgelijke hardware-events. Fix: de herinitialisatie-aanroep is verplaatst
   naar `MainWindow` (die wél een Dispatcher heeft) en daar in
   `Dispatcher.Invoke` gewrapt; `HardwareBeheerder.DoorgevenKortsluitingStatus`
   doet weer precies wat de naam zegt: alleen doorgeven.
2. Dieper gat, ONAFHANKELIJK van punt 1 en voortaan structureel voorkomen: de
   exception uit punt 1 werd pas helemaal onderaan `Poort_DataReceived`
   opgevangen - NA de code die zou moeten bijhouden "was dit al bekend als
   fout" (`_dinamoMeldeFoutVorigeKeer`), maar VOOR de regel die die vlag
   bijwerkte. Die vlag bleef daardoor permanent op "fout" staan, dus elke
   volgende aanroep (er komt continu serieel verkeer binnen) zag opnieuw een
   "net opgeheven"-overgang, probeerde het event opnieuw, kreeg dezelfde
   exception, ad infinitum. Fix: de vlag wordt nu ALTIJD eerst bijgewerkt,
   vóórdat het event verstuurd wordt - een crashende abonnee kan deze
   boekhouding dus nooit meer corrumperen, wat er ook misgaat.
**Les voor volgende keer:** elk event dat vanuit `DinamoHardware`/
`HardwareBeheerder` komt (dus van de hardware-achtergrondthread) moet door de
ONTVANGER in `Dispatcher.Invoke` gewrapt worden zodra die UI aanraakt - nooit
ervan uitgaan dat de aanroeper dat al voor je regelt.

## #30 - "Rijrichting keren" leek niets te doen - correctie kwam pas enkele
   seconden later aan (pas toen de loc blok4 al bereikt had)
**Status:** Gefixt. Gebruikerswaarneming: "De reservering stond naar blok 7 ik
zag dat de loc de verkeerde kant op reed en drukte op rijrichting keren maar er
gebeurde niets, pas toen hij in blok 4 kwam keerde hij ineens, dat was wel
fijn." De correctie werd dus niet genegeerd - hij kwam alleen veel te laat aan.
Oorzaak, gevonden via byte-analyse van de hardware-log: de software stuurt
snelheidscommando's naar de Dinamo via één wachtrij die, door de 200ms-per-
pakket seriële cadans van de Dinamo-master, hooguit 5 commando's per seconde
kan versturen. `VerstuurDatagram` slaat daarbij BEWUST alleen een
BYTE-IDENTIEKE herhaling voor dezelfde (decoderAdres, blokNummer)-combinatie
over (zie het bestaande commentaar daar) - een eerdere, agressievere versie die
ook inhoudelijk ANDERE commando's voor diezelfde combinatie verving, is ooit
teruggedraaid omdat die een keer een echt vertrek-daarna-stopcommando weggooide.
Gevolg hier: toen de gebruiker op "keren" drukte, stonden er van de vorige
(foutgerichte) snelheidsramp nog meerdere, inmiddels achterhaalde
snelheidsstappen in de wachtrij voor dezelfde blokken - de nieuwe,
gecorrigeerde burst moest daar gewoon, in volgorde, achteraan aansluiten. Bij
zes snelle correcties achter elkaar (zoals in de log te zien) liep dat op tot
ruim 10 seconden vertraging, precies de tijd die de loc nodig had om van blok7
naar blok4 te rijden.
**Fix:** nieuwe, bewust SMALLE methode `DinamoHardware.
VerwijderWachtendeSnelheidscommandosVoor(decoderAdres)` (doorgegeven via
`HardwareBeheerder`) die alle nog niet verstuurde snelheidscommando's voor één
decoder, over alle blokken, uit de wachtrij verwijdert. Deze wordt UITSLUITEND
aangeroepen op de twee momenten waarop de software zelf een bewust, nieuw
besluit neemt dat alles ervoor overruled: `TreinrouteWindow.
KeerTreinIndienActief` (handmatige/automatische richtingscorrectie) en de
"geen kandidaat, nu stoppen"-beslissing in `AutomatischeStapProberen` (bug
#27). Op die twee momenten is elke oudere, nog wachtende snelheidswaarde voor
deze decoder per definitie achterhaald, dus dit raakt de eerder teruggedraaide
"vertrek-dan-stop"-regressie niet: die ging over twee onafhankelijke besluiten
tijdens normaal rijden, dit gaat over een expliciet, aanwijsbaar
correctiemoment. De algemene coalescing-regel in `VerstuurDatagram` zelf is
niet aangepast.

---

_Laatst bijgewerkt: zie git-historie van dit bestand zodra het project op
GitHub staat._
