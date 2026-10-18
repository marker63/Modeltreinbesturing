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

## #31 - Aanhoudende Dinamo-foutstatus liet de verstuur-wachtrij dichtslibben met
   tientallen identieke "Reset Fault"-commando's - alles werd daardoor traag,
   de loc ging pas lang na het indrukken van "Go" rijden
**Status:** Gefixt. Gebruikerswaarneming: "ik ben al weer uren bezig ... vanmorgen
reed hij nog een aantal rondjes zonder problemen en nu staat hij weer constant
stil ... ik vind alles enorm traag, de loc ging pas zeer laat rijden nadat ik op
Go had gedrukt." Bytes-analyse van de hardware-log (111 sec sessie) liet een
scheve verdeling zien: 190x "Reset Fault", 208x een niet-herkend periodiek
melder-statusverzoek, maar maar 86x een echt snelheidscommando. Oorzaak:
`StuurResetFault()` wordt (bewust, zie de toelichting daarbij) opnieuw
aangeroepen op ELKE ontvangstcyclus zolang Dinamo's eigen F-bit=1 blijft staan -
nodig omdat Dinamo anders voor altijd in foutmodus kan blijven hangen als de
eerste reset nooit aankomt/verwerkt wordt. `VerstuurDatagram`'s dedup-check (die
een BYTE-IDENTIEKE, nog niet verstuurde herhaling overslaat) gold echter
UITSLUITEND voor commando's MET een snelheidsSleutel (decoderAdres+blokNummer) -
Reset Fault heeft die sleutel niet. Resultaat: bij een fout die een tijdje
aanhield (in dit log ruim 90 sec), stapelden zich tientallen identieke, nog
niet verstuurde Reset Fault-pakketten op in de normale verstuur-wachtrij, ALLE
vóór de inmiddels ook wachtende, echte snelheids-/wisselcommando's (zoals het
commando dat "Go" zelf had moeten versturen) - die moesten vervolgens, via de
200ms-per-pakket-cyclus, één voor één achter die hele stapel aansluiten voordat
ze ooit verstuurd werden.
**Fix:** de byte-identieke-dedup in `VerstuurDatagram` geldt nu ook voor
commando's ZONDER sleutel (null == null telt als een geldige match) - er staat
zo nooit meer dan ÉÉN nog niet verstuurde Reset Fault (of, voor
`VraagMelderStatusOp`, twee aanvragen voor PRECIES dezelfde melder) in de
wachtrij. De herhaal-semantiek zelf (net zo lang blijven proberen als de fout
aanhoudt) blijft volledig intact: zodra die ene verstuurd is, mag de volgende
ontvangstcyclus gewoon weer een nieuwe toevoegen als de fout nog steeds actief
is - alleen de ONGECONTROLEERDE OPSTAPELING is weg.
**Belangrijke nuance:** dit verklaart waarom "alles traag" aanvoelde en waarom
"Go" lang op zich liet wachten, maar verklaart niet met zekerheid de losse
melder-timeouts (melder 15/8 "niet ontvangen binnen 39 sec") uit hetzelfde log -
dat kan zowel een (verder vertraagd) actief statusverzoek zijn geweest dat ook
achter de stapel moest wachten, als een echt gemiste fysieke bezetmelding
tijdens de aanhoudende Dinamo-foutstatus. Mocht een melder-timeout zich blijven
herhalen NA deze fix, dan wijst dat eerder naar de tweede mogelijkheid.

## #32 - Baanverkenner: "nastellen" (kruipen naar de juiste melder) schoot ver door
   bij een te hoge kruipsnelheid - verkenning liep vast met "De loc kon niet
   netjes op melder 16 gezet worden"
**Status:** Gedeeltelijk verzacht (waarschuwing toegevoegd), niet volledig
"gefixt" - zie nuance hieronder. Gebruikerswaarneming: "duurt heel lang voor de
loc gaat rijden, geeft constant kortsluitmeldingen en rijdt een stukje stopt
rijdt weer een stukje en blijft dan definitief staan." Bytes- en loganalyse van
een meegestuurde Baanverkenner-sessie (hardwarelog + baanverkenner-log) liet
twee LOSSE dingen zien:
1. Twee kortsluitmeldingen vroeg in de sessie (tijdens het systematisch op
   "rechtdoor" zetten van alle wisseladressen 1 t/m 32 - elke ~0,6 sec een
   nieuwe spoel bekrachtigen kan op een echte baan best een kort, voorbijgaand
   stroompiekje geven) - BEIDE keren werd de foutstatus netjes en zonder
   herhaalstorm opgeheven, dus BUG #31's fix werkt hier zoals bedoeld: geen
   opstapeling van Reset Fault-commando's meer.
2. Het ECHTE, uiteindelijke vastlopen (~16 sec na het laatst bekende punt
   schoot de loc van melder 5 door naar melder 29/30 - bijna een hele ronde
   verder) had NIETS met kortsluiting te maken, maar met de ingestelde
   kruipsnelheid: deze sessie gebruikte kruipsnelheid 20 bij verkensnelheid 28
   (bijna "vol gas"), terwijl `Nastellen` in BaanVerkenner.Rijden.cs bedoeld is
   om met een ECHTE kruipsnelheid net op de juiste melder te stoppen. Op deze
   baan wordt een heel blok in minder dan 1 seconde doorlopen (zie de
   bezetmeldingen in het log) - bij kruipsnelheid 20 kan de software de
   doelmelder onmogelijk op tijd zien worden, dus de ingebouwde
   "doorgeschoten"-detectie (die vereist dat de doelmelder eerst ECHT bezet
   werd gezien) kreeg nooit de kans om te vuren, en de loc crosste gewoon door
   tot het volledige wachtvenster (15+ sec) verstreken was.
**Fix (voorzichtig, alleen een waarschuwing - zie nuance):** een niet-
blokkerende bevestigingsvraag bij het starten van een verkenning als de
kruipsnelheid groot is (>10 én meer dan de helft van de verkensnelheid),
met uitleg waarom dat risicovol is en een concreet lager advies (3-5).
**Belangrijke nuance:** dit lost het ONDERLIGGENDE gat in `Nastellen`'s
doorschiet-detectie niet op (die blijft afhankelijk van het ooit zien van de
doelmelder als bezet, vóór hij weer vrijkomt) - bij een combinatie van hoge
snelheid + zeer korte blokken kan dat gat nog steeds toeslaan, ook bij een
kruipsnelheid die de waarschuwing niet triggert. Een robuustere oplossing (bijv.
direct ingrijpen zodra een melder bezet raakt die niet het doel/de buur is, in
plaats van te wachten tot het hele venster verstreken is) raakt kernlogica
in `Nastellen` die op meerdere plekken in BaanVerkenner.Rijden.cs hergebruikt
wordt - dat wordt pas aangepakt met een echte testrit erna, niet blind vanuit
een log alleen (zie het user-instructie "test het eerst grondig").
**Praktisch advies voor nu:** zet de kruipsnelheid voor deze baan laag (3-5)
en herhaal de verkenning.

## #33 - Baanverkenner gooide een AL bekende blokkoppeling weg en deed een
   volledige (minutenlange) blokproef opnieuw, ook als maar één korte
   herbevestiging nodig was
**Status:** Gefixt. Gebruikerswaarneming: "je ziet de loc op bezet melder 5 en
16 en je gaat allerlei melders en blokken aansturen, dat moet beter kunnen."
Analyse van een meegestuurd baanverkenner-log liet zien dat de verkenner,
nadat de loc (vanaf een eerder genesteld "kopspoor" op melder 5, waar hij ook
nog deels op melder 16 stond) niet terugreed naar melder 16, de bestaande
koppeling "melder 5 → Dinamo-blok 3" - een paar minuten eerder in diezelfde
sessie zelf gevonden - gewoon WEGGOOIDE en een volledige `BlokZoekenBijStart`
startte: ELK bekend Dinamo-blok, in BEIDE richtingen, elk BlokproefLangSeconden
(hier 30 sec) lang geprobeerd. Op een baan met 16 Dinamo-blokken is dat in het
ergste geval 16 × 2 × 30 = 960 seconden (16 minuten) voor iets waarvan de
software het antwoord al wist. Precies dit liet de verkenning "allerlei
melders en blokken aansturen" terwijl de gebruiker toekeek, en precies dit
maakte de sessie na ~8 minuten nog steeds niets verder.
**Fix:** vóór de dure volledige blokproef wordt nu eerst, als de melder al een
bekend blok heeft, dat ene blok nog één keer kort geprobeerd (met de KORTE
BlokproefKortSeconden-wachttijd in plaats van de lange) - lukt dat, dan wordt
de loc gewoon teruggezet op de startmelder en gaat de rit normaal verder,
zonder de koppeling te verliezen. Pas als die snelle herbevestiging ECHT niets
oplevert (de koppeling klopt dan waarschijnlijk echt niet meer, bijvoorbeeld
door een omgezette wissel), vervalt de koppeling alsnog en volgt exact dezelfde
volledige blokproef als voorheen - dat vangnet is dus niet weggehaald, alleen
niet meer de EERSTE stap.
**Nog niet aangepakt (bewust, zie nuance bij bug #32):** de ALLERSEERSTE
blokproef (wanneer een melder nog NOOIT een blok had) blijft noodzakelijkerwijs
traag voor blokken die NIET het goede blok zijn (elk zo'n blok moet de volle
BlokproefLangSeconden afwachten om zeker te zijn dat er niets gebeurt) - dat is
inherent aan "veilig een onbekend blok uitsluiten", niet aan een gemiste
afkorting. Wie dat sneller wil, kan `BlokproefLangSeconden` zelf verlagen in de
instellingen (ten koste van een kleinere veiligheidsmarge).

## #34 - Baanverkenner stuurde de rijsnelheid maar ÉÉN keer per rit naar het
   blok van de STARTmelder - bij een al bekend vervolgblok bleef de loc daarna
   gewoon stilstaan (en dat werd dan ten onrechte "doodlopend" genoemd)
**Status:** Gefixt - dit is vermoedelijk de kernoorzaak van de "onlogische
handelingen"/"lijkt wel of je ze niet opslaat"-waarneming. Gebruiker: "Als je 1
keer van melder 16 in blok 4 naar melder 5 in blok 3 vooruit gereden bent moet
je dat al onthouden en bij een volgende poging weet je dat al... en blijf je
doorrijden in dezelfde richting." De koppeling werd WEL onthouden (zie
baankaart.json: melder 5 → Dinamo-blok 3 bleef tussen pogingen bewaard) - het
echte probleem zat in wat de software met die kennis deed. Bytes/logvergelijk
van twee identieke pogingen (16 → 5, vooruit) binnen dezelfde sessie:
- 1e poging (blok van melder 5 nog ONBEKEND): rit kwam keurig tot bij melder 5
  én reed gewoon door naar melder 13.
- 2e poging (blok van melder 5 nu AL BEKEND, dus GEEN blokproef meer nodig):
  rit kwam bij melder 5 en bleef daar stilstaan tot de wachttijd verstreek -
  gemeld als "doodlopend" (kopspoor), terwijl de baan daar helemaal niet
  doodloopt.
Oorzaak: Dinamo's rijcommando's gaan ALTIJD via een blokadres (zie de
klasse-uitleg bovenaan `DinamoHardware.cs`: "DCC-commando's worden altijd VIA
EEN BLOK verstuurd, niet rechtstreeks naar een decoderadres") - een
snelheidscommando dat naar blok 4 verstuurd is, bereikt de loc niet meer zodra
hij fysiek blok 3 binnenrijdt. `Rit()` in BaanVerkenner.Rijden.cs stuurde de
rijsnelheid echter maar ÉÉN keer, helemaal aan het BEGIN van de hele rit (naar
het blok van de toen-huidige melder) - zodra de loc nadien een grens overstak
naar een volgend, AL bekend blok, kwam er nooit een nieuw snelheidscommando
voor dát blok, dus verloor de loc daar feitelijk de aansturing. Dat de EERSTE
poging (blok nog onbekend) wél doorreed, was puur toeval: de blokproef
(`BlokZoekenNaInrijden`) stuurt via `Nastellen` zelf al een vers commando naar
het zojuist gevonden blok, als bijwerking van het preciezer neerzetten - geen
bewuste "blijf rijden"-logica.
**Fix:** zodra een nieuwe melder met een AL BEKEND blok bezet raakt, stuurt
`Rit()` nu opnieuw de rijsnelheid (gewoon door, of kruipend als het doel al in
zicht is) naar het blok van die nieuwe melder. Is het blok nog NIET bekend, dan
verandert er niets - dat loopt nog steeds via de bestaande blokproef. Dit raakt
zowel gewone verkenningsritten als navigatieritten (naar een specifieke
doelmelder), want beide liepen tegen exact dezelfde aanname aan.
**Nog open, apart van deze fix:** in dezelfde sessie weigerde de loc ook eens
"achteruit" terug te rijden van melder 5 naar 16 en kwam in plaats daarvan bij
melder 15 uit ("Onderweg naar melder 16 werd onverwacht melder 15 bezet”) -
dat wijst eerder op een wissel die bij "achteruit vanaf 5" een ANDERE route
oplevert dan "achteruit vanaf 16" (mogelijk door de Engelse wissel/kruiswissel
op deze testbaan), niet op dezelfde blok-aansturingsfout. Dit is NIET blind
"gefixt" - eerst een nieuwe test na bug #34 afwachten; als dit blijft
terugkomen, apart oppakken met een eigen bugnummer.

## #35 - Baanverkenner reed tijdens een wisselproef terug naar de startmelder
   terwijl de zojuist geteste wissel nog in de testafstand (afbuigend) stond -
   dat gaf een kortsluiting tegen de verkeerd staande wisseltong
**Status:** Gefixt. Gebruiker (tijdens een nog lopende verkenning, rechtstreeks
waargenomen): "de rit van melder 16 naar melder 5 plots niet meer werkte, dit
kwam nadat je wissel 2 had omgeschakeld naar afbuigend. Dit veroorzaakt een
kortsluiting omdat de loc tegen de wisseltong aanrijd die in de verkeerde stand
staat." Log bevestigt dit exact: 08:46:42 adres 2 → afbuigend, 08:46:45 melder
5 bezet (heenrit kwam keurig aan), 08:47:16 "Rijden achteruit van melder 5 naar
melder 16" (de terugrit van de wisselproef) - en die terugrit kwam niet eens op
gang ("De loc vertrok niet van melder 5"). Omdat dat (dankzij bug #33's fix)
eerst als een mogelijk blok-probleem werd opgevat, startte de verkenner daarna
ook nog een volledige, trage blokproef voor melder 5 (blok 1, 2, 9, 10, 11,
12, 13, ... elk 30 s) - terwijl het blok van melder 5 allang bekend was (3) en
daar niets mis mee was.
Oorzaak: in `VoerOpdrachtUit` (BaanVerkenner.cs) werd het geteste adres pas
teruggezet NA de terugrit naar de startmelder:
```
await ZetWissel(a, true);
var proef = await Rit(...);           // heenrit, met wissel a op afbuigend
await TerugNaarMetControle(o, proef, o.Configuratie.Met(a));  // terugrit - óók nog met a op afbuigend!
await ZetWissel(a, false);            // pas nu weer terug naar de bekende stand
```
De heenrit (16 → 5) bleek dus geen gebruik te maken van wissel 2 in afbuigende
stand (anders was de loc nooit gewoon bij melder 5 aangekomen) - maar de
terugrit (5 → 16, dus van de ANDERE kant door hetzelfde wisselpunt) liep via
precies dat punt wél tegen de nog steeds afbuigend staande wisseltong aan.
**Fix:** het geteste adres wordt nu al teruggezet naar de bekende (rechtdoor)
stand vlak NADAT de heenrit stopt, VOORDAT de terugrit wordt gestart - dus:
```
var proef = await Rit(...);
await ZetWissel(a, false);            // nu al terug, vóór de terugrit
await TerugNaarMetControle(o, proef, o.Configuratie);   // terugrit met de bekende, werkende wisselstand
```
Dit is veilig: `Rit()` stopt de loc altijd volledig voordat hij teruggeeft, dus
de wissel wordt alleen omgezet terwijl de loc stilstaat, nooit onder een rijdende
loc. Blijkt de terugweg ondanks dat tóch af te wijken (bijvoorbeeld omdat de
heenrit wél degelijk over de afbuigende tak liep), dan vangt de bestaande
`TerugNaarMetControle`-foutafhandeling dat al op zoals voor elke andere wissel
die van achteren verkeerd staat (wordt als kortsluitpunt/"opengereden wissel"
geregistreerd) - dat pad bestond al en is voor deze fix niet aangepast.
**Geleerde les/voor vervolgonderzoek:** de gebruiker beschreef ook een manier om
zo'n geval verder te benutten (wissel bewust in de geleerde stand terugzetten,
nog een melder verder rijden ter bevestiging, en de afbuigende tak apart vanaf
de startmelder verkennen met een eigen vermelding in de geleerde lijst). Dat
is in essentie al hoe de bestaande adrestest-loop werkt (`Vergelijk` + de
geregistreerde `WisselWaarneming`/kortsluitpunt-afhandeling) zodra de hierboven
beschreven terugrit niet meer onnodig faalt; er is geen aparte extra logica
voor nodig. Dit moet wel opnieuw getest worden op de echte baan, want de
volledige wisselproef voor melder 5 (veroorzaakt door deze bug) was nog niet
afgerond toen dit gemeld werd.

## #36 - Een wisselproef die "afbuigend → geen melder bereikt" concludeerde werd
   altijd als (vrijwel) doodlopend geregistreerd - ook als de wissel vanaf de
   geteste kant gewoon niet goed te beoordelen was en er verderop wél een
   nieuwe melder lag
**Status:** Gefixt, dankzij een hele precieze uitleg van de gebruiker over de
fysieke ligging van wissel 2 (direct na bug #35's fix getest - zie daar).
Samengevat, in de gebruiker's eigen woorden: "je krijgt in beide standen van
wissel 2 keurig de melding dat de loc melder 5 activeert, echter na een heel
klein stukje in het bereik van melder 5 ligt pas de wisseltong van wissel 2."
Dat betekent: vanaf melder 16 gereden licht melder 5 ALTIJD op, ongeacht de
stand van wissel 2 - de wissel zelf ligt pas een stukje verder, nog binnen
melder 5's eigen bereik. Rijd je vanaf melder 16 door met wissel 2 afbuigend,
dan nader je die wissel van de kant die niet overeenkomt met de afbuigende
stand (vanuit 16 bezien is "rechtdoor" de kant die bij melder 5's sectie
hoort) - de loc liep dan gewoon vast zonder nieuwe melder (geen kortsluiting,
simpelweg geen beweging meer), wat de verkenner ten onrechte als "doodlopend
stuk" opvatte. In werkelijkheid ligt er, benaderd van de ANDERE kant (vanaf
melder 13, achteruit - de kant die de wissel van de puntzijde nadert), een
heel normale, nog onbekende melder achter die afbuigende stand - precies zoals
bij een "kortsluiting-bij-omzetten" (zie bug-afhandeling hierboven: "wissel
wordt van achteren/samenvoegend bereden"), alleen dan zonder kortsluiting.
Oorzaak: in `Vergelijk` (BaanVerkenner.cs) werd voor het geval "rechtdoor geeft
een bekende vervolgmelder, afbuigend geeft géén nieuwe melder" alleen een
waarneming vastgelegd (en bij een "doodlopend"-rit zelfs een kopspoor
geregistreerd) - er werd nooit, zoals bij een kortsluiting vóór het bekende
punt, een extra test gepland vanaf de andere, al bekende kant.
**Fix:** exact dezelfde aanpak als bij "kortsluiting-bij-omzetten" hierboven,
nu ook toegepast op dit geval: zodra een wisselproef concludeert "rechtdoor →
melder X (bekend), afbuigend → geen nieuwe melder", plant de verkenner een
extra opdracht om die wissel nog eens te testen, maar dan vanaf melder X in de
omgekeerde richting (dus van de puntzijde, "facing", benaderd) - zo wordt het
onderscheid tussen "echt doodlopend" en "wissel verkeerd benaderd vanaf deze
kant" alsnog gemaakt, zonder dat de gebruiker dit handmatig moet uitvoeren.
**Nog te bevestigen:** dit is nog niet op de echte baan getest (de verkenning
liep op het moment van melden net opnieuw vast op de bug #34-geleerde
"onverwachte melder 15 i.p.v. 16"-afwijking, een apart, nog openstaand punt -
zie bug #34's "nog open"-notitie). Graag deze fix samen met een hernieuwde
poging voor wissel 2 testen.

## #37 - Binnen één opdracht werden meerdere, totaal verschillende wisseladressen
   (4, 6, 7, 8, 9, 10) ten onrechte allemaal beschuldigd van exact dezelfde
   kortsluiting bij melder 28, terwijl dat gewoon een al bekend, apart,
   wankel punt was
**Status:** Gefixt - gevonden bij een tussentijdse controle-log die de
gebruiker expliciet stuurde om op onjuistheden/verbeteringen te checken.
Het "Wissels"-overzicht in het rapport liet zes adressen zien die stuk voor
stuk woordelijk hetzelfde meldden: "na melder 28 (vooruit), van achteren
bereden... afbuigend: kortsluiting tussen melder 28 en 26." Zes losse
DCC-adressen kunnen onmogelijk dezelfde fysieke wissel zijn. De
overgangen-statistiek in de baankaart liet ook zien dat de overgang 28→26 op
dat moment al in slechts 8 van de 36 pogingen daadwerkelijk lukte - dus dit
was al een eigen, apart, wankel punt (kortsluitpunt #1, "na melder 28
vooruit, alle wissels rechtdoor"), onafhankelijk van welk wisseladres er
toevallig naast werd getest.
Oorzaak: in `VoerOpdrachtUit` (BaanVerkenner.cs) werd de lijst met bekende
kortsluitpunten (`kortsluitStops`, gebruikt om de proefrit vóór zo'n bekend
punt te laten stoppen) maar ÉÉN keer berekend, VOORDAT de lus over alle
wisseladressen begon. Zodra adres 4 (als eerste in de lijst) toevallig tegen
dat al bestaande wankele punt aanliep en zo kortsluitpunt #1 deed ontstaan,
kregen de daarna geteste adressen (6, 7, 8, 9, 10, ...) binnen DEZELFDE
opdracht de bijgewerkte lijst niet te zien - hun proefrit reed dus telkens
weer onnodig door tot voorbij melder 28, liep daar zelf ook (opnieuw) tegen
dezelfde al bekende kortsluiting aan, en kreeg dat vervolgens ten onrechte als
eigen vondst toegeschreven. Dat betekende: vijf extra, volledig vermijdbare
echte kortsluitingen (met noodstop + herstelrit erbovenop) voor iets wat na
de eerste keer al bekend was.
**Fix:** de lijst met bekende kortsluitpunten wordt nu bij elk wisseladres
opnieuw opgehaald, in plaats van één keer vooraf vastgezet. Zo behoedt een
kortsluitpunt dat halverwege de lus ontdekt wordt meteen ook de nog te testen
adressen erna - geen herhaalde onnodige kortsluitingen meer, en geen valse
toeschrijvingen aan adressen die er niets mee te maken hadden.
**Nog aanwezig in eerder opgeslagen voortgang:** de zes foutieve
wisselwaarnemingen (adres 4, 6, 7, 8, 9, 10) staan al in de eerder opgeslagen
`baankaart.json` en worden niet automatisch gecorrigeerd - ze zijn verder
onschadelijk (ze markeren allemaal dezelfde, op zich al terechte scheiding
tussen blok 5 en blok 6 bij melder 28/26) en kunnen gewoon blijven staan.

## #38 - Voorgestelde blokken werden onnodig gesplitst bij een wissel met een
   bevestigde dood lopende aftakking, ook als de melders al hetzelfde
   Dinamo-blok hadden

**Gebruikerswaarneming:** "Apart koploper meld namelijk dat blok 3 bezet is
bij melders 5 en 13 en 14" - Koploper (gebaseerd op de echte, al jarenlang
foutloos rijdende bedrading) meldt melder 5, 13 en 14 als ÉÉN bezet blok. De
Baanverkenner stelde hier echter 3 losse blokken voor (voorgesteld blok 1:
melder 5, blok 3: melder 13, blok 4: melder 14). Gevraagd wat wisseladres 2
en 5 (die deze splitsing veroorzaakten) fysiek zijn, antwoordde de gebruiker:
"Wissel 2 bevind zich in bezetmelder 5, de andere 2 bezetmelders zitten
vanaf melder 16 gerekend na melder 5."

**Analyse (uit de baankaart.json van 12:40):** de overgangen bevestigen de
echte volgorde 16 → 5 → 13 → 14 → 24 precies zoals de gebruiker aangaf.
Melder 5, 13 én 14 hebben alle drie `DinamoBlok: 3` - dus elektrisch gezien
is dit één ononderbroken rijstroomsectie. Wisseladres 2 (gevonden "na melder
5 (vooruit): rechtdoor → melder 13, afbuigend → geen melder bereikt") en
wisseladres 5 (gevonden "na melder 14 (achteruit): rechtdoor → melder 13,
afbuigend → geen melder bereikt") zijn dus allebei - van weerszijden, dus
dubbel bevestigd - een wissel met een echte dood lopende aftakking (kopspoor)
binnen datzelfde elektrische blok.

**Oorzaak:** `BlokVoorstel.Bereken()` (`BlokVoorstel.cs`) splitste melders in
losse voorgestelde blokken zodra er ÓÓK een gevonden wissel tussen zat
(`viaWissel`), zelfs wanneer beide melders al een bekend, gelijk Dinamo-blok
hadden. Maar een gelijk Dinamo-blok betekent dat de melders dezelfde
elektrische sectie delen: een trein wordt daar altijd als één geheel bezet
gemeld, wisselstand of niet, en ook als er een (dood lopende) aftakking in
die sectie ligt. Een gevonden wissel mag dat dus nooit overrulen - de echte
bedrading (waarmee Koploper al jaren foutloos rijdt) is het sterkere bewijs
dan de wissel-topologie die de Baanverkenner zelf aflegt.

**Fix:** `Samen(a, b)` in `BlokVoorstel.cs` kijkt nu eerst naar het
Dinamo-blok: zijn melder a en b al bekend en gelijk qua Dinamo-blok, dan
horen ze altijd bij elkaar, ook als er een wissel tussen gevonden is. Alleen
als het Dinamo-blok van (één van) de melders onbekend is, wordt - net als
voorheen - naar de gevonden wissels gekeken om te bepalen of ze gesplitst
moeten worden. Hierdoor stelt de Baanverkenner melder 5, 13 en 14 nu als
één blok voor, zoals Koploper ook al deed.

## #39 - Een doodlopende afbuigende tak werd nooit opnieuw getest in
   combinatie met een TWEEDE wisseladres

**Gebruikerswaarneming (na de voltooide verkenning van 04-10-2026):**
"blok 6 en 7 heb je heel vaak gereden maar zie ik niet juist terug, en de
blokken 1 en 2 had je gewoon kunnen rijden. Als je vanuit bezetmelder 13
wissel 1 en 2 afbuigend had gezet en naar blok 5 was gereden en dan in
dezelfde richting verder had je in blok 1 terechtgekomen en als je dan
wissel 6 en 9 op rechtdoor had gezet was je vanuit blok 1 in blok 2
terechtgekomen."

**Analyse:** blok 1 (melders 1-9-10) en blok 2 (melders 4-11-12) komen in
het rapport helemaal niet voor, en adres 1 staat in "adressen zonder
gevonden effect" - niet omdat wisseladres 1 geen echte wissel is, maar omdat
hij **pas samen met adres 2 op afbuigend** een route opent. Wisseladres 2
alleen op afbuigend geeft terecht "geen melder bereikt" (bevestigd in bug
#36/#38), maar dat kwam niet doordat die tak nergens heen gaat - er staat
een TWEEDE wissel (adres 1) nog in de weg, in zijn standaardstand.

**Oorzaak:** de verkenner test per opdracht altijd maar één adres per keer
tegen de basisrit (`foreach (var a in _ins.WisselAdressen())` in
`VoerOpdrachtUit`). Als die ene testrit zelf geen enkele nieuwe melder
bereikt (`nieuw.Count == 0`), werd de hele opdracht overgeslagen - inclusief
het testen van de overige adressen. Een combinatie van twee wissels die
allebei op afbuigend moeten staan voor er een route ontstaat, kon zo nooit
ontdekt worden: zodra adres 2 alleen al vastliep, werd nergens nog geprobeerd
of een ander adres (zoals adres 1), samen met adres 2, de tak alsnog opent.

**Fix:** naast de bestaande "andere kant"-test (bug #36) plant de verkenner
nu, zodra een afbuigende tak "geen melder bereikt" geeft, ook een opdracht
die vanaf hetzelfde punt, in dezelfde richting, met dat adres al op afbuigend
gezet, gewoon ALLE nog niet geïdentificeerde adressen één voor één test -
ook als de basisrit van die opdracht zelf nergens verder komt
(`Opdracht.TestCombinaties`, die de bestaande "sla over als er niets nieuws
is"-regel in `VoerOpdrachtUit` nu bewust negeert). Zo wordt een tweede
wissel die, in combinatie, een route opent, bij de volgende verkenning
automatisch gevonden - zonder dat we hoeven te gokken welk adres dat precies
is.

**Nog open:** blok 6 en 7 (20-27-28 en 21-29-30) zijn in het rapport wél
gewoon correct terechtgekomen (ze zaten op de hoofdlus en zijn tientallen
keren bereden) - dat deel van de gebruikersopmerking lijkt te gaan over de
leesbaarheid van de vergelijkingstabel/het blokkenschema, niet over een
fout in de verkenning zelf. Zodra de volgende verkenning (met deze fix) ook
blok 1 en 2 gevonden heeft, kan het blokkenschema opnieuw gecontroleerd
worden.

## #40 - Een onverwachte terugweg werd altijd "kortsluiting" genoemd én
   altijd aan een wissel toegeschreven, ook waar geen wissel ligt

**Gebruikerswaarneming:** "bezetmelder 136 zit in blok 11 en is gewoon goed
berijdbaar, maar je geeft aan dat daar kortsluiting is, geen idee hoe je
daar bij kwam." En, nadat eerst aan een opengereden wissel gedacht werd:
"Hoe kom je bij een opengereden wissel, Blok 10, 11, 12 is een recht stukje
spoor waar je alleen heen en weer kan pendelen om treinen te ijken."

**Analyse (uit het logboek):** de loc reed achteruit van melder 144 via 136
naar 132 (verwacht en correct). Op de terugweg (vooruit) kwam hij na melder
132 echter niet terug bij 136, maar bij melder 133. De hardware meldde
hierbij geen enkele storing - dit was dus geen elektrische kortsluiting.

Dit werd toch opgeslagen en getoond als "Kortsluitpunt", en de bijbehorende
tekst beweerde stellig dat er "een wissel van achteren tegen de rijrichting
in" zou liggen. Beide waren fout: blok 10/11/12 is, zoals de gebruiker
aangeeft, een recht stuk pendelspoor zonder wissel - de onverwachte terugweg
kwam dus ergens anders door, waarschijnlijk een melder (133) die bij de
heenrit niet (op tijd) geregistreerd is.

**Oorzaak:** `TerugNaarMetControle` nam bij elke afwijkende terugweg
automatisch aan dat dit een "opengereden wissel" was (een ongepolariseerd,
onbekrachtigd puntstuk) - zowel in de interne boekhouding (hergebruikte de
Kortsluitpunt-structuur) als in de getoonde tekst. Dat is een redelijke
eerste gok op een spoor MET wissels, maar een onterechte, te stellige
aanname op een stuk spoor zonder wissel.

**Fix:** de nieuwe `Soort`-waarde (zie ook hierboven) heet nu neutraal
"OnverwachteTerugweg" in plaats van "OpengeredenWissel", en alle teksten
(logboek, rapport, code-commentaar) noemen dit voortaan "een onverwachte
terugweg (geen kortsluiting)" zonder te beweren dat het om een wissel gaat
- met als mogelijke oorzaken zowel "een wissel die verkeerd bereden wordt"
als "een melder die de eerste keer niet geregistreerd is". De boekhouding
(ritten stoppen er voor, de oplosfase probeert zo mogelijk een wisseladres)
blijft ongewijzigd - die werkt toch alleen als er daadwerkelijk een
wisseladres bij gevonden wordt, en doet dan niets fout bij een stuk spoor
zonder wissel.

---

_Laatst bijgewerkt: zie git-historie van dit bestand zodra het project op
GitHub staat._

## #41 - Een blok met foutmelding (paars) werd nergens als "niet beschikbaar"
   gezien bij het kiezen van een volgend blok of het starten van een route

**Status:** Gefixt (nog niet getest op de echte baan).

**Achtergrond:** in het geheugen stond sinds eerder een openstaand punt: "als ik
een blok handmatig bezet meld of defect meld rijden er toch treinen naartoe, dat
mag natuurlijk niet". Nooit afgemaakt onderzocht.

**Analyse:** de kandidaat-filters in `AutomatischeStapProberen` (en de
vroege-aankomst-, terugval- en start-controles) keken alleen naar `IsBezet` en
`IsGereserveerd`. Handmatig bezet zit al in `IsBezet` (`ZetHandmatigBezet` voegt
het blok aan `_bezetteBlokken` toe) - dat deel van de klacht klopte dus niet
meer. Een blok met **foutmelding** (de paarse latch bij spookmelding of
rijrichting-mismatch) staat echter in een aparte verzameling en werd nergens
meegeteld. Terwijl juist daar de software niet zeker meer weet of er een loc
staat.

**Fix:** nieuwe `BlokBeheerder.IsGeblokkeerdVoorRit(blok)` = bezet OF gereserveerd
OF foutmelding. Alle 7 plekken in `TreinrouteWindow` (3 kandidaat-filters, de
terugvalcheck, de startcontrole bij Automatisch, de conflictcheck bij vaste
routes incl. het eigen-loc-op-startblok-geval, en de blokgroep-check) gebruiken
nu die ene regel, zodat ze niet meer uit elkaar kunnen lopen.

**Les uit de Baanverkenner:** dezelfde soort fout (twee bijna-identieke
controles die niet allebei bijgewerkt worden) kwam daar ook voor (#37). Een
gedeelde helper is de structurele oplossing.

## #42 - Geen manier om een Baanverkenner-baankaart met het project te vergelijken

**Status:** Gefixt (nieuwe functie, nog niet getest op de echte baan).

**Aanleiding:** de Baanverkenner is bedoeld om uiteindelijk de baan in
Modeltreinbesturing te krijgen ("resultaten later importeren"), maar de koppeling
bestond nog niet - de vergelijking in `BLOKKENSCHEMA.md` moest met de hand en met
losse Python-scripts gebeuren.

**Oplossing:** Beheren > "Baankaart vergelijken (Baanverkenner)..." leest een
`baankaart_*.json` en toont, signalerend (niets wordt aangepast), in
`BaankaartVergelijker`: (1) melders die de verkenner zag maar in geen blok
zitten, (2) projectmelders die de verkenner nooit zag, (3) melders waarvan het
Dinamo-blok afwijkt van het blok waarin ze staan, (4) overloop-melders in
meerdere blokken (info), (5) bereden overgangen zonder relatie in het project
(pas "waarschuwing" bij 3 of meer keer, anders meetstoring-info), (6) kopsporen
die geen Kopspoor-blok zijn, (7) wisseladressen met/zonder bevestigd effect,
(8) kortsluitpunten en onverwachte terugwegen. De logica is op de echte
testbaan-JSON en de voltooide baankaart van 04-10-2026 nagerekend: ze vindt
precies melder 129, de overloop-melder 24 (blok 3/4/7) en één zelden geziene
overgang 28->5 - dezelfde bevindingen als in `BLOKKENSCHEMA.md`.

## #43 - Dinamo's kortsluiting-alarm per blok (Block Alarm) werd genegeerd; de
   Baanverkenner zag een echte kortsluiting niet en verloor er 17 minuten mee

**Status:** Gefixt (nog niet getest op de echte baan).

**Gevonden via:** het open-source project Traintastic (traintastic.org), dat Dinamo-
ondersteuning bouwt. Hun `dinamomessages.hpp` beschrijft een "BlockAlarm"-datagram
(`0x30`, bit 1 = kortsluiting) dat wij nooit verwerkten. Daarna in onze eigen logs
gezocht: 14 ontvangen datagrammen van dit type, allemaal onopgemerkt gebleven.

**Het bewijs (log van 04-10-2026, 08:46):** de Baanverkenner zette adres 2 op
afbuigend met de loc op melder 5 (op de wisseltong). 08:46:45.6 "melder 5 bezet";
08:46:46.3 en .5 kwamen `4AB28282` en `0AB283C1` binnen = Block Alarm met
kortsluiting in Dinamo-blok 3 en 4 (0-based 2 en 3). Het globale F-bit werd niet
gezet - de Baanverkenner wachtte op dat F-bit en zag dus niets. Gevolg: 30 s
niets, "de loc vertrok niet van melder 5", dan de volledige blokproef over 16
blokken (08:47:54 - 09:04:07) en de conclusie "geen enkel Dinamo-blok kon de loc
laten rijden". Dat gebeurde in dezelfde vorm nog twee keer (09:04:45, 09:18:55).
De bijbehorende 0x30 (geen kortsluiting meer) volgde steeds kort nadat het wissel
weer rechtdoor stond.

**Fix:**
- Nieuwe `IBlokAlarmBron` (Hardware): optionele interface voor koppelingen die per
  blok alarmen melden; alleen `DinamoHardware` implementeert hem.
- `DinamoHardware` herkent `(0011000S)(bbbbbbb)`, vuurt `BlokAlarmGewijzigd` bij
  een echte wijziging (blok 1-based) en meldt het in het statusbericht/de
  communicatielog ("Dinamo meldt KORTSLUITING in blok N").
- `DinamoLogVertaler` toont het datagram als "Blok-alarm".
- Baanverkenner: een blok-alarm met kortsluiting telt nu als kortsluiting (zelfde pad
  als het F-bit), noodstop volgt meteen. Het alarm-blok staat in de logregel, in
  `Kortsluitpunt.AlarmBlokken` en in het rapport (kolom "Dinamo-alarm").

**Bonus-bevestiging voor #40:** in het log van melder 136 (blok 10/11/12) staat
géén Block Alarm. Dat bevestigt dat daar geen kortsluiting was.

**Niet gedaan (bewust):** Modeltreinbesturing zelf zet bij zo'n alarm geen blok op
foutmelding en stopt niets - zoals je eerder zei laat Koploper alleen een melding
zien en blijven de treinen rijden. Het alarm staat nu wel in de hardwarelog.

---

## #44 - Geen ondersteuning voor Roco/Fleischmann Z21 (NIEUW, NOG NIET GETEST)

**Aanleiding:** Marco vroeg om zoveel mogelijk uit online bronnen (Traintastic,
officiële specificaties) in de software te verwerken, met de vermelding dat nieuwe
koppelingen nog niet getest zijn.

**Toegevoegd:** `Z21Hardware` (Modeltreinbesturing.Hardware), volgens de officiële
"Z21 LAN Protocol Specification" v1.13: UDP poort 21105, loc-snelheid (14/28/126
stappen), functies, wissels (pulse van 100 ms en daarna uitschakelen), seinen als
wissel, noodstop, kortsluitstatus en R-Bus-bezetmelders (broadcast-flags 0x103).
Keepalive elke 15 s. Beschikbaar in Hardware-dialoog (IP-adres) en in de Baanverkenner.

**STATUS: NOG NIET GETEST tegen een echte Z21.** Alleen de byte-opbouw is nagerekend.

**Let op:**
- Wisselrichting (rechtdoor/afbuigend) is niet uit de bronnen te halen:
  `AfbuigendIsUitgang2` is instelbaar; controleer met één wissel.
- Bij verbinden wordt de railspanning ingeschakeld; na een kortsluiting schakelt de
  Z21 zelf uit en wordt niet automatisch weer ingeschakeld.
- Melderstatus-opvraag (`KanMelderStatusOpvragen`) werkt nu voor elke koppeling die
  dat meldt, niet meer alleen Dinamo.

---

## #45 - Bij het opstarten rijdt de trein al voordat je iets doet; stopcommando's kwamen 40 s te laat; wissels schakelen twee keer; hardwarelog ging verloren

**Gemeld (10-10-2026, 07:57):** "Voordat ik ook maar iets gedaan heb begint de trein al te
rijden en blijven de wissels maar schakelen, ook na het initialiseren." Daarbij: in de
eerste testrit reed de trein de andere kant op (gereserveerd 3 naar 7, gereden 3 naar 4),
en er werd niet ingegrepen. Het log van die eerste rit was niet bewaard.

**Bewijs uit het log van 07:57:39 - 07:59:00 (alles nagerekend op de ruwe regels):**
- 07:57:39.9 verbonden, 07:57:40.5 meldt melder 13 (blok 3) bezet met Dinamo's F-bit aan:
  Dinamo stond bij het verbinden nog in foutstatus van de vorige sessie (we hadden net
  daarvoor afgesloten: backup 07:57:33, herstart 07:57:40).
- Direct daarna spookmelding blok 3 -> NOODSTOP, met een modale MessageBox midden in de
  verwerking die op de seriële-poort-thread draaide (`Dispatcher.Invoke`).
- Er ging in 40 s maar EEN Reset Fault uit (07:57:40.2); de tweede pas om 07:58:20.6. In
  eerdere, goede logs (bv. 03-10 09:47:34) waren twee Reset Faults vlak achter elkaar
  genoeg en was de fout binnen een halve seconde gewist.
- De stopcommando's (snelheid 0, 22 stuks voor 2 locs) gingen pas uit om 07:58:20.8 tot
  07:58:26.8, dus 40,8 s na het verbinden. Zodra de foutstatus gewist is hervat Dinamo
  meteen de oude snelheid van de vorige sessie: dat is de trein die "uit zichzelf" ging
  rijden.
- Van 07:57:40 tot 07:57:58 was de verbinding vrijwel dood: 18 s lang kwam er niets binnen
  en ging er hooguit een pakketje per seconde uit; om 07:57:58.2 volgde een vloedgolf van
  ~60 binnenkomende datagrammen. Tegelijk stonden 96 wisselpakketjes (8 wissels x 12) in
  een verkeerde, door elkaar lopende volgorde (toggle-bits wisselden niet netjes af:
  4B,4B,0B,0B...) - een teken dat meerdere verstuur-tikken tegelijk liepen.
- Om 07:58:20.66 volgde een TWEEDE volledige wisselronde (nog eens 96 pakketjes): de
  BUG #28-regel "foutstatus opgeheven -> alle wissels opnieuw sturen" ging af voor de
  opstartfout, bovenop de wissel-initialisatie die al gedaan was. Dat is het "wissels
  blijven schakelen, ook na het initialiseren".
- "Dinamo meldt zelf een foutstatus" en "foutstatus is opgeheven" stonden steeds DUBBEL
  in het log, met 1 ms ertussen: de ontvangst liep op twee threads tegelijk.

**Oorzaken (vier, die elkaar versterkten):**
1. Reset Fault, stopcommando's en melderopvraag zaten in dezelfde gewone wachtrij (of
   achter de wisselwachtrij). Een stopcommando kon zo achter 30 melderopvragen en een
   hele wissel-initialisatie blijven hangen.
2. Na het verbinden werd meteen alles gestuurd (wissels, 30 melderopvragen) terwijl
   Dinamo nog in foutstatus zat en alles negeerde/vertraagde.
3. Verstuur-tikken konden overlappen (`System.Threading.Timer`) en ontvangst kon op
   meerdere threads tegelijk lopen: pakketjes door elkaar, dubbele meldingen.
4. De spookmelding-MessageBox draaide binnen `Dispatcher.Invoke` op de seriële thread, en
   de BUG #28-herinitialisatie ook: de hardwarelaag wachtte op de gebruiker/UI.

**Fix:**
- `DinamoHardware`: nieuwe URGENTE wachtrij, bij elke tik als eerste volledig geleegd.
  Reset Fault en alle stopcommando's lopen daarover (`ZetLocSnelheidUrgent`); een urgent
  stopcommando haalt ook nog niet verstuurde, verouderde snelheidscommando's voor
  dezelfde loc+blok weg. Urgente pakketjes dragen zelf nooit het PC-F-bit.
- `DinamoHardware.VerbindenAsync` wacht nu (max. 4 s) tot Reset Fault gewerkt heeft
  (twee schone leesbeurten) voordat de aanroeper iets anders mag sturen. Tijdens die
  opstartfase worden geen bezetmeldingen doorgegeven en geen kortsluiting-events gevuurd
  (de opstartfout is geen kortsluiting en mag de BUG #28-herinitialisatie niet starten).
- Verzenden: `Monitor.TryEnter` - een tik die nog loopt wordt niet ingehaald. Ontvangen:
  `lock`. Ontkoppelen stuurt eerst nog de klaarstaande stopcommando's, en sluit de poort
  op een aparte thread (voorkomt de bekende SerialPort-Close/Dispatcher-deadlock).
- `MainWindow`: volgorde na verbinden is nu STOP -> wissels -> melderopvraag (ook in de
  Hardware-dialoog bij handmatig verbinden). Hardware-events gaan via `BeginInvoke`; de
  spookmelding-MessageBox wordt pas na de verwerking getoond (Background-prioriteit).
- Bij afsluiten van het programma worden alle bekende locs eerst gestopt en wordt de
  verbinding netjes gesloten, zodat Dinamo geen oude snelheid onthoudt.
- `TreinrouteWindow.VoerNoodstopUit` gebruikt dezelfde urgente stop (stond in de gewone
  wachtrij).
- Voor koppelingen zonder blokadressering (Intellibox, DCC-EX, Z21, simulatie) is nu een
  stopcommando per loc genoeg in plaats van een per blok.
- `HardwareCommunicatieLog` bewaart elke sessie automatisch in
  `%AppData%\Modeltreinbesturing\Logs\hardwarelog_*.txt` (de 20 nieuwste), inclusief de
  ritlog-regels ("reserveert 3 naar 7", NOODSTOP, enz.). Nooit meer een log kwijt.

**NIET bewezen / nog open:** waarom de seriële verbinding 18 s lang vrijwel stilviel.
De waarschijnlijkste verklaring is dat Dinamo in foutstatus (en/of de USB-seriële
adapter) nauwelijks data accepteert terwijl wij 100+ pakketjes stuurden; de nieuwe
opstartvolgorde stuurt tijdens de fout alleen nog Reset Fault. Als dit bij een volgende
test toch terugkomt, graag het log uit de map hierboven meesturen.

**De verkeerde kant op in de eerste rit (3 naar 7, gereden 3 naar 4):** zie BUG #46.
(Een eerder opgeschreven vermoeden over een verkeerd staande wissel is door de gebruiker
terecht verworpen en hier verwijderd: het ging om een ontbrekende richtingsbepaling.)

## #46 - Startrichting van een rit werd niet per loc onthouden

**Melding:** reservering 3 naar 7 (rijrichting vooruit) maar de loc reed de andere kant op;
niets greep in. De software hoort de richting uit route/reservering te halen en op te slaan.

**Oorzaak:** de startrichting was een enkele waarde per STARTBLOK (`Blok.GeleerdeStartrichtingVooruit`),
niet per loc, niet in de backup per loc, en werd ook door AUTOMATISCHE keercorrecties
(flikkerend meldpunt) overschreven. Blok 3 stond in de projecten op `achteruit` na ~7
wisselende correcties binnen seconden. Na afloop van een rit of na een herstart was de
werkelijke richting van de loc dus onbekend en viel de rit terug op die vervuilde waarde of op
"vooruit".

**Fix:**
- `Trein.LaatsteRichtingVooruit` + `LaatsteRichtingBlokNummer`: de loc onthoudt zijn eigen
  richting bij elk vertrek, elke blokovergang en elke keercorrectie; wordt met het project en
  de backup opgeslagen. Alleen geldig zolang de loc nog in dat blok staat.
- Nieuwe rit: eerst de richting van de loc zelf, dan de geleerde blokwaarde, dan vooruit.
  Een eerste stap met Keer/kopspoor wordt door de bestaande `VereistKeren` omgedraaid.
  De gekozen bron staat altijd in het log (`[Rit] Startrichting ... bron: ...`).
- De per-blok geleerde waarde wordt alleen nog door een bewuste keer-actie van de gebruiker
  bijgewerkt, niet meer door automatische correcties.

**NIET bewezen:** niet op de baan getest. Een loc die je met de hand plaatst heeft nog geen
onthouden richting; de eerste rit gebruikt dan de blokwaarde/vooruit. Staat de loc dan
andersom, gebruik eenmalig de keer-knop: dat wordt vanaf dan onthouden. Blok 3 heeft nog de
oude, mogelijk vervuilde waarde `achteruit` in bestaande projecten.

## #47 - Rit 3 naar 7: kruiswissel te laat/fout, daarna terug naar blok 3 gesprongen

**Melding (log 10-10 08:21):** de loc reed goed van 3 richting 7, maar beide kruiswisselmotoren
stonden fout; pas toen de loc op de tongen stond schakelden ze. Na handmatig terugduwen reed
hij blok 7 in, maar daarna ging het opnieuw fout (noodstop spookmelding blok 6).

**Oorzaak 1 (bewezen in het log): sprong terug naar blok 3.** Melder 24 hoort bij de blokken
3, 4 en 7 (de kruiswissel). Bij aankomst in blok 7 (melder 24) zocht `Bezetmelding_VanHardware`
"het blok van melder 24" en vond blok 3. `ProbeerVroegeAankomstBevestiging` zag dat als bewijs
dat de trein al in het volgende blok 3 stond (7 -> 3 is een geldige relatie), zette de rit
terug naar blok 3 en plande 3 -> 4 (log: "Blok 3 meldt zich al bezet ..."; "wachtend op:
15 (blok 3)"). De loc reed intussen gewoon door naar 6; melder 20 (blok 6) werd dus een
spookmelding. Het zetten van de wissels naar blok 4 om 08:21:46 (13 en 10 naar rechtdoor)
gebeurde daardoor midden onder de loc.
**Fix:** een melder die ook bij het HUIDIGE blok hoort is geen onafhankelijk bewijs; die
gebeurtenis is al de aankomst zelf. Geen stap, geen spookmelding, wel een logregel.

**Oorzaak 2 (NIET bewezen): fysieke wisselstand.** Het log toont dat de wisselstraat 3 -> 7
(wissel 5 rechtdoor, kruiswissel 13/10 afbuigend/afbuigend) overeenkwam met wat de software
dacht (opstart-initialisatie stuurde precies die stand), dus de software stuurde bij vertrek
terecht niets. Waarom de motoren fysiek toch anders stonden is uit het log niet te halen.
**Maatregel:** bij de EERSTE stap van een rit vanuit stilstand (automatisch rijden en eerste
stuk van een vaste route) wordt voor elke wissel en elke kruiswisselmotor op dat stuk altijd
een commando gestuurd, ook als de software de stand al goed denkt. Verderop in de rit blijft
het "alleen bij echte wijziging" (veiligheid bij wagons).
Als het toch fout gaat: graag de fysieke stand van wissel 5, 13 en 10 noteren op het moment
van vertrek en het log meesturen.

## #48 - Kruiswissel 13/10: de loc gaat de andere kant op dan de wisselstraat zegt (OPEN, data)

**Waarnemingen (3 ritten vanaf blok 3, kruiswissel = motor 13 en 10):**
| Rit | Wisselstraat stuurde | Loc reed naar |
|---|---|---|
| 1e testrit (log kwijt) | 3 naar 7: 13/10 = afbuigend/afbuigend | blok 4 |
| 08:21 | 3 naar 7: afbuigend/afbuigend (opstart-stand, geen commando) | bleef op de tongen staan, kortsluiting blok 8; na omzetten naar rechtdoor/rechtdoor + terugduwen: blok 7 |
| 08:35 | 3 naar 4: rechtdoor/rechtdoor (13 en 10 beide gestuurd) | blok 7 (spookmelding, noodstop) |

Alle drie komen overeen met: vanuit blok 3 leidt rechtdoor/rechtdoor fysiek naar blok 7 en
afbuigend/afbuigend naar blok 4, dus omgekeerd aan wat de wisselstraten 3 naar 4 (0/0) en
3 naar 7 (1/1) zeggen. De wisselstraat 3 naar 7 is in v24 door MIJ toegevoegd als kopie van
7 naar 3 (aanname dat het omgekeerde pad dezelfde stand heeft); 3 naar 4 stond er al. Dat de
omgekeerde wisselstraten 7 naar 3 (1/1) en 4 naar 3 (0/0) wel getest zouden zijn, past hier
niet bij: mogelijk is er sindsdien iets aan de motoren/adressen veranderd.
**Status:** NIET opgelost en niet bewezen. Eenvoudige test: in het baanontwerp de Engelse
wissel handmatig op rechtdoor/rechtdoor zetten en kijken waar een loc vanuit blok 3 heen
rijdt; daarna afbuigend/afbuigend. Dan de wisselstraten 3 naar 4, 3 naar 7 (en 4 naar 3,
7 naar 3) corrigeren. Wissel 5 hoort bij 3 naar 7, 7 naar 3 en 4 naar 3 maar niet bij
3 naar 4: ook controleren.
Opmerking bij #46: de "verkeerde kant op" van de 1e testrit is hiermee waarschijnlijk geen
richtingsfout maar dit; #46 blijft als verbetering (richting per loc onthouden) staan.

## #49 - Rijrichting per relatie (van blok A naar blok B) werd nergens vastgelegd

**Melding:** "de loc reserveert van 3 naar 4 en moet volgens de baanindeling achteruit rijden"
(3 naar 7 is vooruit). Controle van het projectbestand: `BlokRelatie` had alleen
Van, Naar, Keer en Kans; er stond dus nergens vooruit/achteruit per overgang. De software
begon met een standaard/geleerde richting en draaide die alleen om bij Keer-vinkjes. Voor een
blok met twee uitgangen in tegengestelde richting (3 naar 7 vooruit, 3 naar 4 achteruit) kan
dat niet kloppen. Dit verklaart de 3 verkeerde starts van 10-10 beter dan een wisselfout
(#48 is daarmee waarschijnlijk deels achterhaald; de waarnemingen blijven staan).

**Fix:**
- `BlokRelatie.RijrichtingVooruit` (vooruit / achteruit / niet vastgelegd), opgeslagen in
  project en backup.
- Instellen: Beheren -> Relaties beheren -> knop "Rijrichting" (klik schakelt door:
  niet vastgelegd, vooruit, achteruit).
- Eerste stap van een rit: de vastgelegde richting van die relatie wint van onthouden/
  geleerde/standaard richting (vaste routes: bij de start; automatisch rijden: bij het
  vertrek zodra het eerste blok gekozen is). Het log zegt welke bron beslist heeft.
- Is er niets vastgelegd voor die relatie dan staat er een WAARSCHUWING in het log.
- Een bewuste keer-actie van jou bij de eerste stap legt de richting voor die relatie vast.
- Verderop in de rit wordt een afwijking tussen vastgelegde richting en Keer-mechanisme alleen
  gelogd (nooit midden in de rit omkeren).

**Te doen in jouw project:** 3 naar 7 = vooruit, 3 naar 4 = achteruit invullen, en daarna de
overige relaties van blokken met meerdere uitgangen (zie waarschuwingen in het log).
**NIET getest op de baan.**

## #50 - Vastgestelde kruiswisselgegevens gingen verloren tussen sessies
Marco: de kruiswisselstanden per rit zijn eerder vastgesteld en moeten altijd meegenomen worden.
Oorzaak: ze stonden alleen verspreid in oude chats/BUGS-regels, en 3 -> 7 was door mij als
spiegel van 7 -> 3 bijgemaakt (BUG #23) zonder het als "niet getest" te markeren.
**Fix:** `KRUISWISSEL.md` (feiten, tabel met herkomst per rit, regels) en `CLAUDE.md` in de
repo-root (wordt in elke nieuwe sessie automatisch gelezen) + geheugen bijgewerkt.
**Open:** de werkelijke stand voor 3 -> 7 en 3 -> 4 moet nog fysiek bevestigd worden (zie #48).

## #51 - Loc rijdt bij het opstarten nog steeds van blok 3 naar 4 de verkeerde kant op
Marco (08:59): "nog steeds rijd hij bij het opstarten van blok 3 naar 4 de verkeerde kant op."
**Oorzaak (aannemelijk, nog niet met een nieuw log bewezen):** `RijrichtingVooruit` (#49) staat
standaard op "niet vastgelegd". Zolang dat voor 3 -> 4 niet is ingevuld, geldt nog de oude
volgorde (onthouden/geleerde/standaard richting) en kwam er alleen een WAARSCHUWING in het log.
Dus ook met de #49-versie rijdt hij fout zolang die data ontbreekt.
**Fix:** op de echte baan (niet in simulatie) start een rit NIET meer als de eerste stap geen
vastgelegde rijrichting heeft: vaste routes weigeren de start, automatisch rijden stopt de loc
voordat er snelheid gegeven wordt; telkens met een melding + FOUT in het log. Geen gokken meer.
**Te doen door Marco:** 3 -> 4 = achteruit, 3 -> 7 = vooruit (en andere blokken met meerdere
uitgangen) invullen via Relaties beheren -> Rijrichting. Stuur anders het log van de start:
daar staat de regel "Eerste stap ..." of "[Rit] Startrichting ... bron: ..." in.
**NIET getest op de baan en NIET gecompileerd** (geen .NET in deze omgeving): bouw eerst even.
Kruiswisselstanden blijven open (#48).

## #52 - Op blok 7 wordt blok 3 niet gekozen: loc rijdt "7 naar 6" de verkeerde kant op
Marco (09:01, log 08:58): na 3 -> 4 -> 5 -> 6 -> 7 reserveert de loc 7 naar 6 en rijdt fout.
**Oorzaak (uit het log):** melder 24 hoort bij blok 3, 4 en 7. Bij aankomst in blok 7 koppelt de
software melder 24 aan het EERSTE blok met die melder (blok 3) en zette blok 3 BEZET (mijn
#47-fix liet die ZetBezet staan). Dus was 7 -> 3 geen kandidaat, "Geen bruikbare vooruit-optie
vanaf blok 7", terugval naar 6 met keren.
**Fix:** een gedeelde melder zet het blok niet meer bezet als de trein in een ander blok staat
dat dezelfde melder heeft (`IsGedeeldeMelderVanAnderBlok`).
**Ook uit dit log:** de start 3 -> 4 ging weer fout (oude build zonder ingevulde richting; je
keerde handmatig en de relatie 3 -> 4 kreeg daardoor 'achteruit', daarna ging 4-5-6-7 goed).
**NIET gecompileerd, NIET getest op de baan.**

## #53 - Baanverkenner niet gekoppeld aan de rijrichting per blokrelatie
Marco (09:19): de vastgestelde rijrichting/kruiswisselgegevens moeten ook aan de Baanverkenner
gekoppeld zijn. De verkenner mat de richting per melderovergang, maar niets nam dat over.
**Fix:** `RijrichtingImporter` in "Beheren -> Baankaart vergelijken": zet de overgangen om naar
`BlokRelatie.RijrichtingVooruit` en biedt aan om ALLEEN lege relaties in te vullen.
- Nooit overschrijven: een afwijking van wat jij vastlegde wordt alleen gemeld.
- Melders in meerdere blokken (zoals 24) zijn niet ondubbelzinnig: alleen melding, niet invullen.
- Polariteit van de test-loc: wordt afgeleid uit al vastgelegde relaties; zijn die allemaal
  tegengesteld dan worden de verkenner-richtingen omgedraaid; zijn ze gemengd dan niets invullen.
  Zonder enige vastgelegde relatie: waarschuwing dat normale polariteit is aangenomen.
**Niet gedekt:** kruiswissel- en wisselstanden uit de verkenner worden nog NIET overgenomen
(blijft handmatig bevestigen, zie KRUISWISSEL.md / #48). NIET gecompileerd, NIET getest.

## #54 - Baanverkenner: loc die deels in de volgende melder staat; baankaart niet actueel
Marco (09:35, test Baanverkenner): "melder 133 wordt niet gezien, deze zit echt in blok 11, maar
de loc stond met 1 draaistel nog in melder 144 en het andere in melder 133 en dat gaf een probleem."
**Oorzaak 1 (uit de bestanden, zeker):** de baankaart/rapport werd alleen bewaard na een hele
opdracht. Het log laat zien dat melder 136 (09:28:28) en 133 (09:28:31) wél bezet gemeld werden,
maar de geëxporteerde baankaart stamt van 09:28:11 en kent ze niet. Daarom meldde de vergelijking
"melder 133 niet gezien". **Fix:** bewaren bij elke nieuwe melder en elke blokkoppeling.
**Oorzaak 2 (aannemelijk, niet bewezen):** staat de loc met een deel al in de volgende melder, dan
komt voor die melder nooit een "bezet geworden"-melding (hij was al bezet). De verkenner zag dan
geen vooruitgang. **Fix:** wordt de huidige melder vrij terwijl precies één andere, nog niet
opgenomen melder bezet blijft, dan telt die als bereikt (gelogd). Bij meerdere: alleen waarschuwing.
**Niet opgelost / onbekend:** waarom bij 09:29-09:32 alle drie de blokken 10/11/12 "vooruit" geen
beweging gaven terwijl daarna blok 11 achteruit wel reed, kan ik uit het log niet verklaren (is de
loc tussendoor met de hand verplaatst?). NIET gecompileerd, NIET getest met de baan.

## #55 - Baanverkenner: lange sectie => onterecht "doodlopend/kopspoor", verkeerde blokkoppeling
Marco (09:52): "de tijd dat je in een blok rijdt is net iets te kort voor de bloklengte met deze
snelheden; ik zie steeds dat een blok als kopspoor herkend wordt dat het niet is." Testbaan: alleen
blok 10, 11, 12, geen wissels (melders 143-144-136-133).
**Oorzaak (uit het log 09:40-09:50, jouw hypothese klopt):** de slimme wachttijd leerde van maar
één meting (136 -> 133: 3,0 s) en werd dus 10 s (minimum). Sectie 144 is veel langer: de loc deed
er 16 s over (09:43:39 -> 09:43:55). Na 10 s concludeerde de verkenner "doodlopend" (09:44:32,
09:47:09), legde 144 en 133 als kopspoor vast en startte onnodig blokproeven. Die cascade gaf:
"vertrok niet", "melder 133 niet bereikt" en een FOUTE koppeling melder 144 -> blok 10 (09:48:53,
0,25 s na de start van de proef: een melderwijziging van de vorige beweging werd als reactie op het
commando gezien). Jouw blokindeling zegt 144 = blok 12.
**Fix:** (1) wachttijd pas 'slim' na 3 echte metingen, en nooit korter dan 2x de langste tijd om
vanuit stilstand een melder te halen; (2) voor 'doodlopend' geldt eerst eenmalig de maximale
wachttijd (30 s) als bevestiging (gelogd); (3) blokproeven tellen alleen melderwijzigingen NA het
rijcommando; (4) een kopspoor-vondst wordt verwijderd zodra later blijkt dat de loc vanaf die melder
in die richting (zelfde wisselstand) wel verder kan.
**Let op voor je volgende test:** de verkeerde koppeling 144 -> blok 10 zit nog in je hervatte
verkenning; begin met een nieuwe verkenning (of zet 144 terug op blok 12). NIET gecompileerd, NIET
getest op de baan.

## #56 - Baankaart uit de Baanverkenner importeren in de hoofdmodule
Marco (10:10): bij een nieuwe baan alle melders met hun blokken leren in de Baanverkenner en die
daarna meteen in de hoofdmodule laden.
**Gebouwd:** menu "Baankaart importeren (Baanverkenner)..." (`BaankaartImporter.cs`, `MainWindow`).
- Blok.Nummer = Dinamo-blok. Melders zonder Dinamo-blok worden overgeslagen (waarschuwing).
- Bestaand blok krijgt ontbrekende melders; anders wordt een nieuw blok gemaakt. Melders die al in een
  projectblok zitten (overloop, bv. melder 24) worden niet opnieuw toegevoegd.
- Type Kopspoor alleen als een kopspoor-melder hoogstens 1 buurmelder heeft (anders vals kopspoor, zie #55).
- Relaties (beide richtingen, keuze in dialoog) uit de overgangen, met RichtingsBezetmelding en
  Rijrichting (#49/#53). Bestaande rijrichtingen, wisselstanden en blokken worden NOOIT overschreven.
- Nieuwe blokken krijgen een plek op het schema (raster onder de bestaande), stootblok bij kopspoor,
  uitgaand sein bij nieuwe relaties (afspraak: nieuw blok = ook seinen).
- Eerst een overzicht van alle meldingen, dan "behouden?" (Nee = alles terug via `Momentopname`).
**Wel getest (Tools/CompileCheck, `dotnet run --project Tools/CompileCheck`):** de importerlogica met de
echte baankaart van 04-10: blokken 3-8, geen vals kopspoor, blok 3 = melders 5/13/14, 3->4 achteruit,
3->8 vooruit, tweede import verandert niets, overloop-melder 24, vastgelegde richtingen blijven,
ongedaan maken werkt.
**Vervolg (zelfde ronde, Marco 10:24 "zo veel mogelijk, zelflerend, een leek moet het kunnen gebruiken"):**
- Dienst: elk NIEUW blok krijgt een automatische route (`DienstAanvuller`, Automatisch = true, GEEN verzonnen
  vertrektijd). Bestaande routes blijven ongemoeid. Getest in Tools/CompileCheck (5 nieuwe routes, bestaande
  route intact, tweede keer niets, herstel).
- Wissels: wisseladressen uit de verkenning die nog niet op het baanontwerp staan worden als losse wissel in een
  rij onderaan gezet (slepen naar de goede plek). Wisselstanden en wisselstraten worden NIET aangemaakt;
  kruiswisselstanden uit KRUISWISSEL.md blijven leidend.
**NIET gedaan:** wisselstraten/standen uit de verkenning; tijden in de dienstregeling.
**NIET gecompileerd:** de WPF-kant (MainWindow-handler incl. routes en wissels, menu). NIET getest op de baan.

## #57 - Baanverkenner: loc blijft op melder 132 staan, meldt onterecht kopspoor, 132 krijgt geen Dinamo-blok
Marco (10:28, volledige testrit 09:57-10:24, melders 143-144-136-133-132-140-139): "hij komt niet in het
einde van blok 10 maar stopt op melder 132; dat is niet het einde van dit pendeltraject."
**Oorzaak (uit verkenner-log en hardwarelog):** (1) bij melder 132 gaf de blokproef "een klein stukje terug" bij
blok 11, 10 en 12 geen beweging (10:05:16-10:05:36). Daarna werd 132 voorgoed als "proef mislukt" onthouden.
(2) Voor een melder zonder bekend blok stuurde de verkenner bij aankomst GEEN nieuw rijcommando (het oude
commando naar blok 11 bereikt de loc niet meer in blok 10 - zie #34). Alleen de allereerste rit reed door omdat de
mislukte proef nog één keer naar alle blokken stuurde (10:05:39: 140, 10:06:16: 139). Bij elke volgende rit
(10:08, 10:11, ... 10:24) bleef de loc op 132 staan, "doodlopend" en dus een vals kopspoor 132 + geen blok voor 132.
**Fix:** (a) een melder zonder bekend blok waar geen blokproef meer komt krijgt het rijcommando alsnog naar alle
blokken; (b) mislukte proef wordt bij een volgende rit nog één keer opnieuw geprobeerd (max. 2 pogingen per melder);
(c) nieuwe tweede proef "doorrijden": als terugrijden bij geen enkel blok beweging geeft, rijdt de loc per blok in
de rijrichting door (max. BlokproefLangSeconden per blok); het blok dat hem laat bewegen voedt die sectie, en de rit
gaat gewoon verder.
**Getest (Tools/CompileCheck, simulator met het pendeltraject van de baan, loc stopt zonder blokcommando, commando's bij 132
gaan verloren):** vóór de fix precies Marco's symptomen (132 zonder blok, vals kopspoor 132, rit stopt); na de fix
worden alle 7 melders gevonden, krijgt 132 blok 10, is 132 geen kopspoor en zijn 139 en 143 wel kopspoor
(twee scenario's: commando eenmalig verloren, en proef twee keer mislukt). Waarom het "terug" bij 132 op de baan
echt niets deed weet ik niet; de simulator bootst alleen het gevolg na.
**NIET getest op de baan.** De demobaan-simulatie heb ik NIET als regressietest kunnen opnemen (in mijn testopzet
liep die ook zonder deze wijziging niet door).
**Let op voor je volgende test:** begin een nieuwe verkenning (de bewaarde kaart bevat het valse kopspoor 132 en 132 zonder blok).

## #58 - Baanverkenner: loc stopt net voor melder 143 ("kon niet netjes op melder 143 gezet worden"), verkenning gestopt
Marco (10:45, nieuwe verkenning 10:41-10:43): "de loc moet van 144 naar 143, maar net voordat hij in 143 komt stopt hij en
zegt dat 143 niet bereikt is; hij had iets langer moeten doorrijden, hij reed ook niet zo snel."
**Oorzaak (uit verkenner- en hardwarelog 10:42:47-10:43:21):** na de geslaagde blokproef bij 143 moest de loc weer precies op 143
gezet worden ("Neerzetten", kruipsnelheid 5). (1) Na 0,4 s kruipen vooruit meldde de verkenner 143 even vrij terwijl 144 bezet
bleef; dat telde direct als "doorgeschoten" en de loc kroop 15 s ACHTERUIT (10:42:49-10:43:04), diep in de lange sectie 144. (2) De
terugweg kreeg maar 15 s: bij kruipsnelheid is dat niet genoeg om dezelfde afstand terug te leggen, dus de loc stopte
"net voor 143" (10:43:05-10:43:20) en de opdracht brak af met de foutmelding, waarna de hele verkenning stopte.
**Fix:** (a) een wegvallende bezetmelding telt pas als doorgeschoten als ze 1,5 s later nog steeds weg is; (b) staat de loc alleen op
een melder waarvan de baankaart al weet in welke richting het doel ligt (gemeten overgang, hier 144 -> 143 vooruit), dan kruipt hij
die kant op i.p.v. te gokken (ook bij de richting van de volgende poging); (c) de tijd per poging verdubbelt (15, 30, 60 s).
**Getest:** de bestaande simulatie-scenario's (nu ook met een lange proefrit terug en een flikkerende melder 143) blijven slagen.
Het precieze foutgeval (melder valt even weg tijdens het kruipen) heb ik in de simulator NIET kunnen laten falen met de oude code,
dus er is geen test die bewijst dat dit exact de fout was; de oorzaak komt uit de logs. NIET getest op de baan.

## #59 - Baanverkenner: zelflerend (geleerde tijden) en optioneel automatische snelheid
Marco (10:50): "leer van de tijden uit eerdere logs, en pas eventueel de snelheden tijdens het testen automatisch aan."
**Gebouwd:**
- `LeerProfiel` (Baanverkenner.Kern): onthoudt per overgang de reistijd omgerekend naar snelheid (seconden x snelheidsstap),
  dus bruikbaar bij elke verkensnelheid. Wordt na elke rit bijgewerkt en bewaard in `%AppData%\Baanverkenner\leerprofiel.json`
  (zelfde map als voortgang.json). Zonder eigen profiel start de app met `leerprofiel_zaad.json` (de gemeten tijden uit jouw
  verkenning van 10-10 09:57-10:24: o.a. 144->143 17 s, 144->136 17,4 s, 136->133 3,0 s bij snelheid 12).
- Gebruik: (1) slimme wachttijd tussen melders is meteen bruikbaar (ook bij weinig metingen in deze verkenning, begrensd door Min/Max);
  (2) bij het neerzetten op een melder (kruipen) krijgt de loc minimaal de verwachte tijd x (verkensnelheid / kruipsnelheid) x 1,5;
  (3) tijden in de baankaart zijn nu altijd omgerekend naar de referentiesnelheid (`Baankaart.RefSnelheid`) en worden niet meer
  vastgelegd als de loc al kruipend het doel in zicht heeft (dat gaf vroeger onzuivere gemiddelden).
- Automatische snelheid (instelling "Verkensnelheid zelf bijstellen", STANDAARD UIT, experimenteel): na elke rit lager (-25%,
  nooit onder kruipsnelheid+2) als de kortste gemeten sectie < 1,5 s is; hoger (+2, max 1,5x beginsnelheid) als alle secties > 4 s
  zijn en de langste > helft van de maximale wachttijd; na een verlaging nooit meer verhogen (geen heen-en-weer).
**Getest (Tools/CompileCheck):** profiel bewaren/laden, omrekenen bij andere snelheid (17 s bij 12 = 34 s bij 6), dezelfde kaart
twee keer aanbieden telt niet dubbel, tweede run met profiel is niet langzamer, echte tijden uit de kaart van 10-10 10:24
worden overgenomen, auto-snelheid verlaagt bij een extreem korte sectie en laat een normale baan op 12.
**Bekende beperking:** in de simulator met een extreem korte sectie (30 cm) en auto-snelheid AAN mislukte de laatste terugrit
("Loc kon niet op melder 144 gezet worden") terwijl dezelfde baan zonder auto-snelheid wel slaagde. Daarom staat het standaard uit.
Het leerprofiel zelf is niet de oorzaak. Het profiel kan hooguit een wachttijd inkorten/verlengen (binnen Min/Max); een gewijzigde baan met
dezelfde meldernummers geeft dan een onjuiste schatting, daarom verwijder je bij een andere baan `leerprofiel.json`.
**NIET gecompileerd:** de WPF-kant van de Baanverkenner (vinkje, laden/bewaren van het profiel, csproj-regel voor het zaadbestand).
NIET getest op de baan.

## #60 - GEPLAND (Marco, 11:34): bekend kopspoor = meteen keren, niet tegen het stootjuk laten doordraaien
Marco: "als je al weet dat een melder echt doodlopend is, laat je de loc toch steeds langere tijd tegen het stootjuk doordraaien;
dat is zonde van de loc. Weet je al dat de melder aan een stootjuk grenst (kopspoor), dan mag je meteen keren."
**Nog NIET gebouwd** (bewust voor een volgende versie). Aanpak: in `Rit()` (Baanverkenner.Kern/BaanVerkenner.Rijden.cs): als de loc een melder
bereikt die in `Baankaart.Kopsporen` staat voor deze richting en wisselconfiguratie (of in het leerprofiel als doodlopend is geleerd),
dan niet wachten op "geen nieuwe melder binnen de wachttijd" maar direct stoppen en keren (res.Einde = Doodlopend zonder wachttijd).
Let op: alleen bij een echt kopspoor (<= 1 buurmelder, zie #56) en bij dezelfde wisselconfiguratie; nooit voor een melder die alleen
"doodlopend" leek door stilvallen (#55, #57). Eerste keer dat een einde ontdekt wordt blijft de wachttijd nodig. Ook de Nastellen-kruip
tegen het stootjuk (#58) en de blokproef bij een bekend kopspoor kunnen dan korter.

## #61 - Baanverkenner: loc staat op twee secties en komt niet meer in beweging (rit 10-10 11:38, melder 136 en 132)
**Eerst de goede uitkomst:** de rit van 11:02-11:38 (36 min 20 s) was voor het eerst een volledig geslaagde verkenning van het pendeltraject
(blok 10 = melders 132/140/139, blok 11 = melder 133, blok 12 = melders 136/144/143; kopsporen 143 en 139; 5 trajecten, 10 gemeten
overgangen). Die kaart/het rapport/de logs staan als referentie in `Tools/CompileCheck/testdata/*_pendel_2026-10-10_1138_geslaagd.*`
en de importer/dienst/leerprofiel-tests (`GeslaagdeRitTest`) draaien erop. `Baanverkenner/leerprofiel_zaad.json` is ververst met de
tijden van 10:25 + 11:38 (10 overgangen). Let op: heen en terug zijn NIET gelijk (136->133 = 3 s, 133->136 = 12 s bij snelheid 12,
omdat de tijd tussen twee "bezet"-meldingen van de positie van de melders afhangt) - dus tijden nooit spiegelen.

**Wat er mis ging (uit de hardwarelog, 2 plekken, zelfde oorzaak):** een loc die vanuit stilstand met een deel in twee secties
(= twee Dinamo-blokken) staat, komt niet in beweging als het rijcommando maar naar EEN van die blokken gaat (3 x 30 s, 0 beweging;
pas bij een commando naar alle blokken reed hij binnen 2 s weg).
1. Melder 136 (11:06:53): de loc rolde tijdens de stopprocedure (VolledigeStop, ~3 s na de melding "136 bezet" omdat 136 maar 3 s
   lang is) met de neus in 133 (`4AD184E1` = melder 133 bezet, 0,4 s voor de stop). De blokproef zocht daarna blok voor blok:
   12, 10, 11 (6 s elk) en 10, 11, 12 (30 s elk) zonder beweging; blok 12 WAS goed, maar de loc stond ook in blok 11. Verlies: ~110 s en
   melder 136 kreeg pas bij de rit erna zijn blok.
2. Melder 132 (11:09:54): na het neerzetten flikkerde melder 133 (bezet/vrij/bezet binnen een seconde); het rijcommando ging op dat
   moment alleen naar blok 10 (133 stond even "vrij"), terwijl de loc ook in 133 (blok 11) stond. 30 s stilstand, daarna "doodlopend na 132"
   (onterecht; 140/139 volgen) - de rit herstelde zichzelf pas door de terugrit.
**Fix (Baanverkenner.Kern/BaanVerkenner.Rijden.cs):**
- Na de volledige stop vóór een blokproef: staat de loc op meer dan alleen de nieuwe melder, dan eerst `Nastellen` (terugkruipen met alle
  blokken) tot alleen die melder bezet is; lukt dat niet, dan geen proef maar de rit gaat door (de proef komt bij een volgende rit; geen
  poging geteld).
- `VoedOntbrekendeBlokken`: staat de loc op meerdere melders en ontbreekt het rijcommando voor het (bekende) blok van een van die melders,
  dan krijgt dat blok het commando er alsnog bij (`_voedingBlokken` houdt bij welke blokken het laatste commando kregen). Blokken die
  nog onbekend zijn blijven buiten schot, zodat de blokproef-opzet (loc rolt zelf helemaal de nieuwe sectie in) ongewijzigd blijft.
**Test:** simulator kreeg `StilstaanRemtOpGrens` (vanuit stilstand op twee blokken rijdt de loc alleen weg als alle blokken het commando
krijgen) en het rijden met meerdere blokken tegelijk. Bij sectielengte 136 = 50 en 52 cm (de loc staat na de stop op de grens) faalde
de OUDE code (blokproef mislukt) en slaagt de nieuwe; 15 sectielengtes + flikkerende melders 132/133/136 groen. ALLES OK.
**Eerlijk:** het tweede punt (flikkering bij 132) liet zich in de simulator niet afdwingen; de oorzaak komt uit de hardwarelog en de
fix is in de simulator alleen op "geen verslechtering" getest. NOG NIET GETEST OP DE BAAN.
**Nog open uit deze rit:** ~8 van de 36 minuten gingen op aan wachten bij bekende doodlopende einden (-> #60, gepland).

## #62 - Baanverkenner: rit 11:45-12:15 (hoofdbaan, melders 5-30): kortsluiting niet herkend in de blokproef, blokken niet onthouden, wachten bij bekend kopspoor (+ #60 gebouwd)
**Waarneming (Marco, logs 12:15 + eigen waarneming):** ~17 van de eerste 30 minuten gingen verloren. Melder 16, 17 en 28 kregen geen blok (BUG #61, nog niet
in die rit aanwezig; in de hardwarelog teruggevonden: melder 16 bezet 11:57:03, melder 5 bezet 11:57:06, stop 11:57:06,5 = loc op twee secties; zelfde bij 17/8;
bij melder 21 flikkerde 24 op het moment van het rijcommando). Daarna stond de loc vooruit rijdend met de achterkant nog op wissel 6 (afbuigend) en met de
poot tegen de tong van wissel 5 (stond rechtdoor) = kortsluiting; hij wachtte tot Marco wissel 5 afbuigend zette. De route 15-16-5-13 gebruikt wissel 5 en 6 dus
via hun afbuigende poot, terwijl de basisrit "alle wissels rechtdoor" is.
**Onderzocht, géén bug:** wissel 10/13 (kruiswissel) WAREN geïnitialiseerd (11:47:46-58, 6 frames per adres, echo 0x90 = rechtdoor, geen enkel afbuigend-commando in
de log t/m 12:15). De Dinamo meldt de echte stand van de motoren niet terug; de melding "alle adressen op rechtdoor" is een gestuurd commando (tekst aangepast).
**Gebouwd (Baanverkenner.Kern):**
1. Kortsluiting tijdens de blokproef (`BlokZoekenNaInrijden`/`BlokZoekenDoorrijdend`): `ProefKortsluitingGemeld()` stopt de proef direct; `Rit` handelt het af als gewone kortsluiting
   (noodstop + herstel + Dinamo-blokalarm in de melding) met het advies de wissel bij die melder met de hand goed te zetten. Het proefpogingen-tellertje telt zo'n poging niet mee.
   Niet gewijzigd: `BlokZoekenBijStart` (start van de verkenning).
2. Blokhints: `LeerProfiel.BlokHints` (melder -> blok uit eerdere kaarten); de blokproef probeert dat blok EERST (een foute hint kost één korte poging). Oude profielbestanden blijven laadbaar.
   Het zaadprofiel bevat nu de koppelingen van 4 oktober + de ritten van 10-10 (o.a. 16 = blok 4, 17 = 5, 28 = 6, 24 = 8) en de Baanverkenner vult een bestaand profiel daarmee aan.
3. BUG #60 gebouwd: bij een al bekend kopspoor (zelfde richting en wisselstand, loc is echt vertrokken, geen navigatie-rit) wordt niet de hele wachttijd en niet de verlengde
   wachttijd (#55) afgewacht maar max(MinSecondenTussenMelders, 12 s bij ref.snelheid); daarna keert hij. Een kopspoor dat later toch een volgende melder blijkt te hebben wordt nog steeds
   uit de kaart gehaald (Baankaart regel ~220), dus een verkeerd geleerd kopspoor corrigeert zichzelf.
4. Hardwarelog in het geheugen: 2000 -> 20000 regels (een half uur verkenning paste niet; de sessielog op schijf had alles al).
5. Logregel bij de start: "commando gestuurd; de centrale meldt de werkelijke stand van de wisselmotoren niet terug". Waarschuwing bij een mislukte blokproef noemt kortsluiting/wissel als mogelijke oorzaak.
**Test (CompileCheck, ALLES OK):** blokhints (tweede run 0 i.p.v. 3 mislukte blokpogingen; oud profiel laadbaar; hints van 16/17/28 uit de kaart van 4 oktober); #60: eerste volledige run
1363 s i.p.v. 1474 s en tweede run 1283 s; kortsluiting in de blokproef met de simulator (nieuw: `KortsluitBijCommando`): herkend en gemeld, verkenning daarna 914 s i.p.v. 1044 s; een scan van 42
kortsluit-momenten gaf met de nieuwe code nooit een slechtere uitkomst (zelfde aantal melders, altijd even snel of sneller).
**Bewust NIET gebouwd (te riskant zonder baan):** automatisch wissels afbuigend zetten om een tongkortsluiting op te lossen. Wissel 5/6 moeten voor deze route afbuigend staan; de verkenner weet niet welke wissel
bij welke melder hoort voordat hij die getest heeft. Tip bij een rit met zulke wissels: vooraf met de hand goed zetten, of het stuk eerst zonder die wissels verkennen. Ook niet gebouwd: een proef met
het buurblok erbij (alternatief voor het terugkruipen uit #61).
NOG NIET GETEST OP DE BAAN (alleen simulator). De WPF-aanpassing (zaadhints samenvoegen in Baanverkenner/MainWindow.xaml.cs) is niet gecompileerd.

## #63 - Baanverkenner: overloopwissels (twee wissels achter elkaar) en terugrijden met de verkeerde wisselstand (Marco 13:34 en logs 13:40)
**Waarneming (Marco):** na wissel 2 afbuigend kwam een kortsluiting; de verkenner zette wissel 2 meteen weer rechtdoor. Wissel 2 werd direct gevolgd door wissel 1 (rechtdoor), de loc reed
tegen de tong van wissel 1. Op een overloopverbinding moeten BEIDE wissels afbuigend staan; wissels mogen niet opengereden worden. Regel van Marco: krijg je na het berijden van een net gezet
wissel kortsluiting, laat dat wissel staan en schakel alle andere wissels één voor één en kijk of de kortsluiting weg is.
**Tweede oorzaak (log 13:40, melder 12):** bij adres 2 afbuigend reed de loc van melder 5 naar melder 12 (afbuigende tak). Daarna werd wissel 2 teruggezet NAAR RECHTDOOR vóórdat de loc terugreed;
hij stond met de achterkant tegen de tong en kwam niet van zijn plaats. De blokproef probeerde daarna 9 blokken x 2 richtingen x 30 s (ruim 9 min) en de verkenner gaf op (loc moest met de hand terug).
**Gebouwd (Baanverkenner.Kern):**
1. Terugrit na de proef: volgde de proef een andere weg dan de basisrit (`ProefWijktAf`), dan rijdt de loc met dezelfde wisselstand terug en wordt het adres pas daarna teruggezet.
   Volgde de proef de basisrit, dan blijft BUG #35 gelden (eerst terugzetten, dan terug).
2. `PartnerwisselZoeken` (adresloop in `VoerOpdrachtUit`): kortsluiting in de proef -> het geteste wissel blijft afbuigend, de andere wissels worden één voor één ook afbuigend geprobeerd
   (dichtstbijzijnde adres eerst, `MaxPartnerProeven` = 8). Succes: kortsluitpunt opgelost door dat adres, waarneming `LostKortsluitingOp`, vervolgopdracht met beide afbuigend; de losse opdracht
   voor alleen het eerste adres (zou weer kortsluiten) wordt uit de wachtrij gehaald.
3. `LosKortsluitpuntenOp`: kan geen bekende wissel een kortsluitpunt (basisrit van een opdracht met wissels afbuigend) verklaren of lukt dat niet, dan blijven de wissels uit die configuratie
   afbuigend en worden de overige adressen één voor één erbij geprobeerd (zelfde limiet). Dit is het geval uit Marco's waarneming (opdracht "afbuigende tak van adres 2").
**Test (CompileCheck, ALLES OK):** `OverloopTest` (wissel 2 + 1 achter elkaar: melder 4 bereikt, kortsluitpunt opgelost door adres 1, beide wissels in het rapport) en `AfbuigendeTakTerugTest`
(stomp spoor op de afbuigende tak, terug met afbuigend); beide FALEN met de oude code (loc niet terug op melder 1).
NOG NIET GETEST OP DE BAAN (alleen simulator). Een proef met een partner kost per adres een extra rit; bij veel wissels is het maximum 8 per kortsluitpunt.

## #64 - Baanverkenner: geleerde vervolgmelders vasthouden (Marco 13:58)
**Waarneming (Marco):** een blok/melder waarvan het vervolg al bekend is heeft in meldingen en rapport soms ineens "geen vervolg meer". Onnodig.
**Oorzaak:** een gevonden overgang X→Y werd alleen in die ene richting onthouden. Dezelfde route omgekeerd (Y→X andersom, zelfde wisselstand) is fysiek altijd berijdbaar, maar werd nergens afgeleid:
(1) het rapport toonde lege "achteruit →" bij melders die alleen vooruit gereden waren (bijv. 14, 24, 28); (2) hing de loc even (slecht contact, tong), dan werd dat meteen "doodlopend"
(kopspoor opgeslagen, "terug vanaf het einde" gepland) of een navigatierit brak af, ook al was het vervolg al bekend.
**Gebouwd (Baanverkenner.Kern):**
1. `Baankaart.BekendeVolgende(van, richting, configuratie?)`: gereden overgangen + de omgekeerde ervan, alleen voor dezelfde wisselstand (zonder configuratie: alle standen, voor het rapport).
   Wissel-tussen-twee-melders kan dit niet verstoren: de afleiding geldt alleen binnen één en dezelfde wisselstand. Gemeten tijden worden niet gespiegeld (heen en terug zijn niet gelijk).
2. Rapport (tekst en html) gebruikt dit.
3. `Rit`: blijft de loc hangen na een melder met bekend vervolg (zelfde wisselstand), dan wordt één keer het rijcommando herhaald en opnieuw gewacht (geldt ook voor navigatieritten) vóór er iets uit geconcludeerd wordt.
4. Blijft het daarna uit: einde "doodlopend" met `BekendVervolgGemist`: GEEN kopspoor opgeslagen, geen "terug vanaf het einde" gepland, wel een waarschuwing.
**Test:** `OmgekeerdeOvergangTest` met de kaart van 13:40 (14 achteruit → 13, 28 achteruit → 27, andere wisselstand niets afgeleid, rapport zonder lege regel); alle bestaande scenario's ongewijzigd groen.
NIET in de simulator getest: het hangen van de loc zelf (sim kent geen hangende loc). NOG NIET GETEST OP DE BAAN.

## OPEN VRAAG voor de volgende versie (Marco, 14:14)
Controleren of alles wat de Baanverkenner (#61-#64) heeft geleerd ook in het Modeltreinbesturing-programma zelf (`BaankaartImporter`, `BaankaartVergelijker`, `RijrichtingImporter`) verwerkt wordt:
- Overloopwissels (twee wissels samen afbuigend, `LostKortsluitingOp` met partner, kortsluitpunt opgelost door adres b): de importer gebruikt wisselwaarnemingen nog niet.
- Omgekeerde overgangen (#64): importer leest overgangen al in beide richtingen (Van of Naar), dus waarschijnlijk al goed - nog bevestigen met de kaart van 13:40.
- Blokhints/leerprofiel hoeven niet naar het hoofdprogramma.

## #65 - OPEN (Marco 14:24/14:30): wissels schakelden fysiek niet meer, software stuurde wel en Dinamo bevestigde
**Waarneming:** run 14:06-14:22: alle commando's verstuurd en door de Dinamo bevestigd (zelfde frames als 13:xx), maar fysiek ging geen wissel om; alle adressen "geen verschil" (om 13:28 gaf adres 2
afbuigend nog melder 12). Eerste rit 13->14 kreeg direct na de initialisatie kortsluiting in blok 3 (ook om 13:05). Oplossing door Marco: Dinamo-systeem spanningsloos maken en opnieuw opstarten; daarna
werden de wissels weer geïnitialiseerd en reageerden ze (nog te bevestigen na de voortgezette testrit).
**Hypothese (Marco, niet bewezen):** zoals in Koploper "Wissels niet gezet storing": een volgelopen buffer in de Dinamo. Wij sturen elk wisselcommando 3x (6 frames) en bij elke adrestest twee commando's.
**Mogelijke maatregelen (nog NIET gebouwd, eerst Marco's akkoord):** (1) wissel-effectcontrole: een wissel met bewezen effect (bijv. adres 2 -> melder 12) die later geen effect meer heeft stopt de verkenner met de melding
"wissel reageert niet, Dinamo opnieuw opstarten"; (2) aantal wisselcommando's tellen en loggen (hoeveel voor de storing begon); (3) eventueel minder herhalingen of meer pauze tussen wisselcommando's, alleen na bewijs uit de log.
**Gebouwd (14:35, maatregel 2):** elke logregel "Adres N → afbuigend/rechtdoor" noemt nu "(wisselcommando X sinds het verbinden)". Wissels niet meer bewegen? Dan staat in het log na hoeveel commando's dat gebeurde.
Maatregel 1 (effectcontrole) en 3 (rustiger sturen) nog niet gebouwd. NOG NIET GETEST OP DE BAAN.

## #66 - Baanverkenner: vast op de kruiswissel na partnerzoektocht (logs 15:14, melder 24, blok 8)
**Waarneming (Marco):** de loc staat al lange tijd vast in blok 7 net voor de kruiswissel. Log: wissel 10 gevonden (na melder 24 achteruit: rechtdoor -> 14, afbuigend -> 129, nieuwe melder). Daarna adres 13 afbuigend
(zonder 10) -> kortsluiting in blok 8 op melder 24 (gemengde kruiswisselstand 13 afbuigend/10 rechtdoor, zie KRUISWISSEL.md). Mijn #63-partnerzoektocht probeerde als partner eerst 12, 14, 11, 15 (op afstand van 13) in plaats van 10;
de loc stond na Herstel op 21 én 24 en reed niet meer weg. Daarna 2 x een volledige blokproef (18 blokken x 30 s = 9 min elk) terwijl blok 8 steeds weer kortsluiting meldde.
**Oorzaak:** (1) partnervolgorde alleen op adresafstand; (2) doorgaan met proeven terwijl de loc niet netjes op één melder staat; (3) blokproef na een kortsluiting terwijl het blok van de melder al bekend is.
**Gebouwd:** (1) `PartnerVolgorde`: wissels met een waarneming bij die melder (zoals 10 bij 24) eerst, dan op afstand; (2) elke partnerproef begint alleen als de loc precies op melder x staat, anders waarschuwing en stop;
(3) vertrekt de loc niet, is het blok bekend en was er <10 min geleden een kortsluiting, dan geen blokproef maar meteen de vraag de loc/wissels goed te zetten.
**Test:** `PartnerVolgordeTest` (10 vooraan bij melder 24, geteste adres en al afbuigende wissels vallen af); alle bestaande scenario's groen. (2) en (3) zijn NIET in de simulator getest (vragen een loc die vastzit). NOG NIET GETEST OP DE BAAN.
**Gezien in dezelfde logs (goed):** de wisselteller werkt (38 commando's in ~40 min, geen storing); wissel 10 correct gevonden; geen valse "geen vervolg" meer in het rapport (#64).

## Leermateriaal 15:27 (Marco): stand van kruiswissel-motor 13 foutief, software keerde netjes
Alleen om van te leren, geen nieuwe versie nodig. Opgeslagen als `Tools/CompileCheck/testdata/*kruiswissel13_fout_2026-10-10_1527.*` (kaart + log). Wat het laat zien: adres 13 afbuigend (10 rechtdoor = gemengde kruiswisselstand) geeft bij het binnenrijden van melder 24
direct kortsluiting in blok 8 (en even blok 7); de centrale meldde het binnen 1 s, de noodstop en de herstelrit (kruipen terug naar melder 24) volgden meteen. Dit is het gedrag dat BUG #66 verder beschermt (geen blind doorproberen als de loc niet netjes staat).

## Leermateriaal 15:42 (Marco): stand van kruiswissel-motor 10 foutief, wacht op vervolg
Alleen om van te leren, geen nieuwe versie nodig. Opgeslagen als `Tools/CompileCheck/testdata/*motor10_fout_2026-10-10_1542.*` (kaart + log). Wat het laat zien: wissel 10 en 13 en 14 gevonden; 37 wisselcommando's sinds het verbinden en geen storing. Opdracht "24 achteruit, afbuigend: 10": de loc vertrok niet van melder 24 (15:41:42), het opnieuw proberen van het bekende blok 8 hielp niet en de volledige blokproef startte (15:41:48). De #66-uitzondering (korte kortsluiting <10 min geleden) gold hier niet omdat de laatste kortsluiting al langer geleden was, terwijl de oorzaak (verkeerd staande wisselmotor) dezelfde is.
Suggestie voor een latere versie (NIET gebouwd, alleen op verzoek): ook bij een bekend blok en een net geschakelde wissel in dezelfde rit de volledige blokproef overslaan en de gebruiker vragen de wisselstand te controleren. Nog niet getest op de baan.

## #67 - OPEN (Marco 15:53): melding "Controleer locadres, rijstappen…" halverwege een testrit is onnodig
Marco ziet halverwege een lopende testrit (zelfde loc, zelfde aantal rijstappen, al uren in gebruik) ineens de melding "De loc liet geen enkele melder bezet worden. Controleer locadres, rijstappen en of de loc op een bewaakte sectie staat." Die tekst komt uit `BaanVerkenner.cs` (loc zoeken, ~regel 373) en is bedoeld voor het begin van een verkenning. Halverwege is de echte oorzaak bijna nooit adres of rijstappen (die zijn dan bewezen goed) maar een loc die niet rijdt/geen contact maakt, een verkeerd staande wissel of een loc die tussen secties staat.
Voorstel (NIET gebouwd, Marco: "nog geen fix, wel onthouden"): na een geslaagde melder in deze sessie een andere melding geven ("De loc is niet teruggevonden; controleer of de loc op de rails staat, contact maakt en of een wissel verkeerd staat") en het locadres/rijstappen-advies alleen vóór de eerste melder tonen. Nog niet getest op de baan.

## Leermateriaal 16:03 (Marco): vervolg motor 10 foutief, na ruim 20 minuten weer goed gezet, software loste het zelf op
Alleen om van te leren, geen nieuwe versie nodig. Opgeslagen als `Tools/CompileCheck/testdata/*motor10_fout_hersteld_2026-10-10_1603.*` (kaart + log). Vervolg op 15:42.
Wat het laat zien:
- Opdracht #3 (24 achteruit, afbuigend: 10) vertrok 15:41:11 niet. Er volgden twee volledige blokproeven (9 blokken x vooruit/achteruit x 30 s = ruim 9 minuten elk, 15:41:48-15:50:56 en 15:52:43-16:01:50), beide met FOUT "Geen enkel Dinamo-blok kon de loc op melder 24 laten rijden". Tussendoor werd het rijcommando herhaald (#64) en de rit terecht als "geen kopspoor, niet opgeslagen" behandeld.
- Na het herstellen van de wissel (16:03) ging de software zelf verder: adres 10 terug naar rechtdoor, loc rijdt vooruit naar melder 13. Geen handmatig ingrijpen in de software nodig.
- Neveneffect: door de mislukte blokproef is het eerder gevonden Dinamo-blok 8 van melder 24 in de kaart weggevallen (kaart: DinamoBlok null, rapport "Melders zonder gevonden Dinamo-blok: 24, 129"). Kennis die al vaststond ging verloren.
Suggesties voor een latere versie (NIET gebouwd, alleen op verzoek): (1) bij een melder met bekend Dinamo-blok en een net geschakelde wissel niet 2x18 blokken proberen maar na het eerste mislukte herhalen stoppen/pauzeren met een duidelijke vraag "controleer de stand van wisselmotor 10"; (2) een mislukte blokproef mag een eerder vastgesteld Dinamo-blok niet wissen (zelfde lijn als #64); (3) samen met #67 (onnodige locadres-melding). Nog niet getest op de baan.

## #68 - OPEN (Marco 16:09): vraag-dialoog bij loc die niet vertrekt door vermoedelijk verkeerd staande wisselmotor
Aanvulling op suggestie (1) van het leermateriaal 16:03. Marco wil, na de melding "controleer de stand van wisselmotor N", een vraag: "Heb je de wisselmotor met de hand omgezet?"
- **Ja** -> de verkenning gaat verder (de laatst gegeven opdracht opnieuw proberen met dezelfde wisselstand; geen blokproef).
- **Nee** -> vraag: "Moet de rit worden afgebroken?" Bij ja: rit afbreken en de verkenning hervatten vanaf het startpunt (loc terugzetten op de startmelder, zoals bij een hervatting).
Ontwerpnotities (NIET gebouwd; Marco: eerst onthouden): geldt voor de situatie "loc vertrekt niet van een melder met bekend Dinamo-blok terwijl in deze rit net een wissel is geschakeld"; de wacht tijdens de vraag mag onbeperkt zijn (geen blokproef van 20 minuten); de vraag hoort bij #67 en leermateriaal 16:03 (blokproef mag bekend blok niet wissen). Nog niet getest op de baan.

## #69 - Baanverkenner: loc blijft steken op een blokgrens, en een mislukte blokproef wist een bekend Dinamo-blok (Marco 16:24, log 16:22)
Waarneming: de loc bleef steken terwijl hij van blok 7 naar blok 3 reed, precies op de blokscheiding: een draaistel in blok 7, een draaistel in blok 3 op de kruiswissel; het wissel stond goed.
Oorzaak uit het log (twee dingen achter elkaar):
1. Om 15:41:48 (loc vertrok niet door de verkeerd staande wisselmotor 10) werd het bekende blok 8 van melder 24 gewist (`DinamoBlok = null`) en de volledige blokproef mislukte twee keer (2 x 9 min). Het blok kwam nooit terug.
2. Om 16:05 kwam de loc bij melder 24 aan, het blok was onbekend, dus een nieuwe blokproef met de loc op de blokgrens. Een Dinamo-loc rijdt vanuit stilstand alleen als ELK blok onder hem het rijcommando krijgt (BUG #61), dus blok 8, 7 en 1 gaven "geen beweging"; blok 2 gaf een toevallige melderwijziging en werd vastgelegd als blok van melder 24 (fout! was 8). Daarna 30 s geen beweging, opdracht #4 mislukt, 13 minuten wachten tot Marco de loc terugzette (16:22).
Fix (gebouwd, getest in de simulator, NIET getest op de baan):
- Een mislukte volledige blokproef zet het eerder vastgestelde blok terug (de koppeling gaat alleen verloren als de proef een ander blok vindt).
- Rijdt de loc niet weg van een melder met bekend blok: na de snelle herbevestiging nu één korte poging met het rijcommando naar ALLE Dinamo-blokken tegelijk (`ProbeerAlleBlokken`). Rijdt hij dan wel, dan staat hij op een blokgrens: melding, het bekende blok blijft bewaard, de rit gaat verder met het commando naar alle blokken (geen volledige blokproef van 18 blokken).
- Het herhaalde rijcommando bij een bekend vervolg (#64) gaat nu ook naar alle blokken.
- `StopLoc` zet ook blokken op snelheid 0 die eerder een rijcommando kregen maar niet meer onder de loc liggen (anders kon een "alle blokken"-commando blijven staan).
Test: `BlokgrensTest` in Tools/CompileCheck (loc met zijn kop in een wisselstuk zonder melder, blok 9): oude code faalt, nieuwe code rijdt door en houdt blok 1 bewaard; alle andere regressietests blijven OK.
Niet gebouwd: het herstel-pad zelf (blok terugzetten na mislukte proef) heeft geen eigen test; de blokgrens-situatie bij de blokproef zelf (loc nog niet vertrokken, blok onbekend) is niet aangepast.
Leermateriaal: `Tools/CompileCheck/testdata/*blok24_fout_blok2_2026-10-10_1622.*` (kaart: melder 24 staat daar ten onrechte op blok 2, moet blok 8 zijn - handmatig herstellen in de lopende baankaart is niet mogelijk; bij hervatten laadt hij blok 2).
Let op voor Marco: de lopende baankaart van 16:22 bevat nog het FOUTE blok 2 voor melder 24. De nieuwe versie corrigeert dat niet vanzelf; zie de vraag in het antwoord.

## #70 - OPEN (Marco 16:42): na de start van de loc schakelen er nog wissels om, timing van de eerste initialisatie checken
Waarneming van Marco: nadat de loc gestart was, schakelden er nog een paar wissels om. Verdenking: de beginstand (alle adressen op rechtdoor) is nog niet fysiek klaar als de eerste rit begint.
Wat het log (15:21) laat zien: 20 wisselcommando's met 0,6 s ertussen (15:21:19.1 - 15:21:30.5, elk een burst van 6 frames), de eerste rit start 0,6 s na het laatste commando (15:21:31.1). De software wacht dus niet op het fysiek omzetten en de centrale meldt geen motorstand terug; de Dinamo kan de laatste commando's nog uit zijn buffer afhandelen en de motoren hebben omzettijd. Aan te sluiten bij #65 (buffer/commando's tellen).
Idee voor een latere versie (NIET gebouwd, Marco: "geen nieuwe versie nodig, alleen onthouden"): na de initialisatie (en na elk omzetten van een groep wissels) een wachttijd instellen (bijv. 2-3 s per laatste commando, of instelbaar), de loc pas daarna laten starten, en in het log tonen hoe lang er gewacht is. Eerst meten: tijd tussen laatste wisselcommando en eerste rit in meer logs vergelijken met de waarnemingen van Marco. Nog niet getest op de baan.

## #71 - Baanverkenner: valse overgang 13 -> 8 door navigatie vanaf een verkeerd aangenomen plek (Marco 16:56, log 16:55)
Marco: blok 3 en 4 vormen samen een kleine cirkel met wissels, blok 1 en 2 ook; de volgorde van de melders en de richting waarin ze gezien worden zijn daar cruciaal.
Gevonden in het log/de kaart van 16:55:
1. De kaart bevat een overgang 13 achteruit -> 8 (alleen gezien in de basisconfiguratie) die op de baan niet bestaat; het rapport toont daardoor ook "melder 8 vooruit -> 13" (omgekeerde overgang). Echte buren: 13 achteruit -> 5, 8 achteruit -> 17 en 24, 15 achteruit -> 8. Oorzaak: na een Herstel na kortsluiting stond de loc op melder 8, terwijl de navigatie ervan uitging dat hij op melder 13 stond; de rit "13 -> 8" werd daarna als echte overgang vastgelegd.
2. Zelfde opdracht (24 vooruit, afbuigend 14) geeft verschillende paden (24,8,15,16,5,13,14,24 / 24,21,29,30,20,27,28,26,25,17,8 / 24,8): melder 24 is een kruiswissel-sectie; het vervolg hangt af van de kant waar de loc binnenkwam. Een positie "op 24" is daarom niet genoeg, er is een ingangskant ("via-melder") nodig. NIET gebouwd (ontwerpwijziging, eerst met Marco bespreken).
3. Mogelijk te vroeg omgezet wissel 14 terwijl de staart van de loc nog op het wissel stond (bezet {8,24}, "Adres 14 -> rechtdoor" 2,8 s na "melder 8 bezet", kortsluiting 16:48:05). Alleen een hypothese, niet bewezen. NIET gebouwd.
4. De kortsluiting van 16:43 na melder 8 vooruit met 14 afbuigend is de verwachte kortsluiting door het wissel dat van achteren bereden wordt.
Fix (gebouwd, getest in de simulator, NIET getest op de baan): in `RitUitvoeren` controleert een navigatie of de loc echt op de startmelder staat. Staat hij op precies één andere melder: is dat het doel -> geen rit, geen overgang; ligt die op het verwachte pad -> de rit begint daar; anders `NavigatieFout` (loc terugzetten). Er wordt dus nooit meer een overgang vastgelegd vanuit een aangenomen plek.
Test: `NavigatieOnjuisteStartTest` (oude code faalt beide controles, nieuwe code slaagt).
Handmatig opschonen in de lopende voortgang.json (programma gesloten): verwijder in Overgangen de regel met Van 13, Naar 8, Richting Achteruit. Het leerprofiel kan hierdoor ook een verkeerde looptijd 13 -> 8 hebben.
Leermateriaal: `Tools/CompileCheck/testdata/*kleine_cirkels*_2026-10-10_1655.*`.

## #72 - Baanverkenner: een gelukte proef bij een kortsluitpunt ging verloren doordat de weg terug mislukte (Marco 17:07, log 16:40-17:01)
Waarneming: kortsluitpunt #2 (na melder 24 achteruit, wissel 14 afbuigend) werd "opgegeven na 3 kortsluitingen", terwijl de proef met adres 13 afbuigend om 16:48:29-35 WEL slaagde (loc reed 24 -> 14, geen kortsluiting).
Oorzaak: in `LosKortsluitpuntenOp` werd de uitkomst van de proef pas beoordeeld NA `TerugNaar` en `TerugNaarHuis`. Die weg terug zette motor 13 weer op rechtdoor terwijl de loc in de kruiswissel-sectie 24 stond, de loc reed vervolgens naar een andere melder/kortsluiting (16:48:46), er kwam een `NavigatieFout`, en de uitkomst werd nooit vastgelegd.
Fix (gebouwd, regressietests OK, geen eigen test voor de mislukte terugweg, NIET getest op de baan): de uitkomst (kortsluiting of opgelost, inclusief wisselwaarneming en vervolgopdracht) wordt eerst vastgelegd en bewaard; mislukt daarna de weg terug, dan volgt een waarschuwing en de vraag de loc terug te zetten, maar de uitkomst blijft staan.
Leermateriaal: `Tools/CompileCheck/testdata/*voltooid_kruiswissel24_2026-10-10_1707.*` (oude run voor #71: bevat de valse overgang 13 -> 8).
Waargenomen maar NIET opgelost (open):
- Alle vijf de kortsluitingen "na melder 8 vooruit" (16:48:05, 16:55:16, 16:57:53, 16:58:55, 17:00:58) volgden op "Adres 14 -> rechtdoor" direct nadat de loc van melder 24 naar melder 8 was gereden (melder 24 was dan al vrij, 1,7 s na "melder 8 bezet"). Waar wissel 14 afbuigend bleef staan (16:41:24, 16:58:22, 16:59:24) reed dezelfde rit 8 -> 15 zonder kortsluiting. Sterke aanwijzing dat wissel 14 omgezet wordt terwijl de staart van de loc nog op het wissel staat (het wissel heeft geen melder). Mogelijke maatregel: na aankomst via een wissel eerst een korte afstand doorrijden voordat dat wissel omgezet wordt.
- Kortsluitpunt #3 "opgelost: adres 13 afbuigend -> melder 15" is een misleidende registratie: 13 bepaalt of de loc vanaf melder 14 via de kruiswissel naar 8 of naar 21 gaat. Het echte probleem was dat de rit 24 -> 8 via 21 ... 17 naar 8 liep en 8 van de verkeerde kant (17) binnenkwam. Hoort bij het ingangskant-probleem van melder 24.
- Ingangskant ("via-melder") voor de kruiswissel-sectie 24 is nog steeds niet gebouwd. Zie KRUISWISSEL.md.

## Nieuw (Marco 17:23): blokkenschema tekenen in de Baanverkenner (stap 1 van 2)
Gebouwd: `Baanverkenner.Kern/Blokkenschema.cs` (indeling + SVG), venster `BlokkenschemaWindow` met knop "Blokkenschema tonen" in de Baanverkenner (opslaan als PNG of SVG) en een hoofdstuk "Blokkenschema" in het HTML-rapport. Blokken staan in de volgorde waarin de testloc ze bereed; pijl = in die richting gereden; bij elke verbinding melders, rijrichting en wissel; oranje = melder met twee vervolgen; stippellijn = Dinamo-blok onbekend.
Getest: de SVG-uitvoer met de baankaart van 17:07 (test `BlokkenschemaTest`, plus bekeken als afbeelding). Het WPF-venster is NIET gecompileerd en NIET getest (kan hier niet) - nog niet getest op de baan/pc.
Stap 2 (NIET gebouwd, wacht op akkoord van Marco): knop "Importeren in Modeltreinbesturing". Open punten: de Baanverkenner is een los programma; automatisch importeren vraagt een overdrachtsbestand of een gedeelde projectmap; de bestaande import (`BaankaartImporter`, menu Beheren) blijft de bron en wordt hergebruikt.

## Nieuw (Marco 17:32): knop "Importeren in Modeltreinbesturing" (stap 2 van 2)
Marco: de tekening ziet er goed uit. Gebouwd: knop in de Baanverkenner zet de baankaart klaar in `%AppData%\Modeltreinbesturing\Baanverkenner\voor_import\baankaart_voor_import.json` (`BaankaartOverdracht`) en biedt aan Modeltreinbesturing te openen (zoekt `Modeltreinbesturing.exe` naast of vlak bij de Baanverkenner). In Modeltreinbesturing biedt Beheren > Baankaart importeren eerst de klaargezette baankaart aan (Ja = die gebruiken, Nee = zelf kiezen); na behouden van de import wordt het bestand hernoemd naar `baankaart_verwerkt_<tijd>.json`. De bestaande import (`BaankaartImporter`, met terugdraaien) is ongewijzigd en blijft de bron.
Bewust NIET volledig automatisch: de import vraagt nog steeds de bevestigingen (beide richtingen? behouden?), omdat jouw vastgestelde standen en richtingen nooit overschreven mogen worden.
Getest: `OverdrachtTest` (klaarzetten, lezen, verwerken). De WPF-delen (knop, vraag in Modeltreinbesturing) zijn NIET gecompileerd en NIET getest op de pc/baan.

## #73 - Baanverkenner: na een mislukte terugweg begint de volgende rit vanaf een verkeerd aangenomen plek (Marco 17:51 en 17:54, logs nieuwe run 17:18-17:54)
Waarneming: Marco schoof de loc steeds terug naar melder 24 (maar een paar cm, de kruiswissel zit IN sectie 24); "wisselmotor 13 staat dan steeds verkeerd".
Wat het log laat zien:
1. 17:45:50 kortsluiting na melder 24 vooruit met adres 10 afbuigend (terecht: wissel 10 wordt van achteren bereden). De partnerproef (adres 5 + 10 afbuigend) slaagde: 24 -> 8.
2. De terugweg naar melder 13 (24 -> 14 -> 13 achteruit, alle wissels rechtdoor) gaf om 17:48:25 weer kortsluiting. Die kortsluiting werd niet als fout van de terugweg behandeld: de verkenner ging gewoon door met het volgende adres (11, 12, 13), alsof de loc op melder 13 stond. Elke volgende "verkenningsrit vanaf melder 13" begon dus met de loc op melder 24, kwam niet weg of gaf weer kortsluiting, en Marco moest de loc zes keer verplaatsen (17:48, 17:49, 17:51, 17:53).
3. Dat de loc vanaf melder 24 (na aankomst vanaf melder 8) naar melder 14 alleen rijdt met motor 13 op afbuigend, past bij het eerdere log (16:48:29: 8 -> 24 -> 14 lukt met 13 afbuigend, niet met 13 rechtdoor) en bij 17:52:07 in dit log (adres 13 afbuigend: de loc reed 24 -> 14). Dit is een HYPOTHESE (niet veldgetest): op de kruiswissel bepaalt motor 13 welke twee benen verbonden zijn (rechtdoor: 21 <-> 14, afbuigend: 8 <-> 14).
Fix (gebouwd, getest in de simulator voor de controles, NIET getest op de baan):
- `TerugNaar`: kortsluiting op de terugweg geeft nu een `NavigatieFout` (de loc is dan niet terug op de start). Resultaat: de opdracht wordt later opnieuw geprobeerd en Marco wordt EEN keer gevraagd de loc op de startmelder te zetten.
- `RitUitvoeren`: ook een verkenningsrit (geen navigatie) begint niet meer als de loc op precies een andere melder staat dan de startmelder (zelfde controle als #71, nu voor alle ritten). Test: `NavigatieOnjuisteStartTest` onderdeel c.
- `TerugNaarHuis` (kortsluitpunt-proeven): het eerste stuk van de terugweg gebruikt de wisselstand van de proef (bijv. 13 afbuigend), pas daarna die van de route. Reden: 16:48:46 gaf kortsluiting doordat motor 13 onder de loc terug op rechtdoor werd gezet.
NIET opgelost (open, ontwerpkeuze met Marco): het ingangskant-probleem van sectie 24. De terugweg na een partnerproef (24 -> 14 nadat de loc via 8 binnenkwam) heeft motor 13 afbuigend nodig; de verkenner weet dat niet zonder dat hij die (via, melder, vervolg)-combinatie onthoudt. Voorstel: "doorgangen" (voorgaande melder, melder, volgende melder, wisselstand) vastleggen en gebruiken bij het terugrijden. Pas bouwen na akkoord.
Leermateriaal: `Tools/CompileCheck/testdata/*kruiswissel24_nieuwe_run_2026-10-10_175*.*`.

## #74 - Baanverkenner: doorgangen (ingangskant) onthouden, wissels opnieuw sturen na handmatig terugzetten (Marco 18:09)
Marco: het onthouden van de ingangskant (voorgaande melder, melder, volgende melder, wisselstand) is "een hele belangrijke stap" die hij al een paar keer voorgesteld had. Gebouwd:
1. `Baankaart.Doorgangen` (nieuw, oudere kaarten laden gewoon met een lege lijst): per doorgang via-melder, melder, volgende melder, rijrichting, wisselstanden en aantal keer. Elke gereden stap legt de doorgang vast, en meteen ook de omgekeerde (volgende -> melder -> via, andere richting, zelfde wisselstanden). Ook over de ritgrens heen: de ingangskant van de vorige rit wordt meegenomen (alleen als de loc niet met de hand is verplaatst en niet in een kortsluiting terechtkwam).
2. Gebruik bij terugnavigeren (`NaarStartVan`, `TerugNaarHuis`): staat de loc op een melder waar hij vanaf een bekende kant binnenkwam en rijdt hij in dezelfde richting door, dan wordt de wisselstand voor de EERSTE stap gecontroleerd (`Baankaart.KiesConfiguratieBijIngang`). Alleen bij bewijs wordt er iets veranderd: als met deze ingangskant de gewenste stand eerder ergens ANDERS heen leidde, en er is een stand waarvan bevestigd is dat hij naar de gewenste volgende melder leidt, dan geldt die stand (de stand die het dichtst bij de gewenste ligt). Zonder bewijs blijft alles zoals het was. Het log meldt elke keer dat dit gebeurt ("Ingangskant: ...").
3. Het rapport (tekst en html) heeft een hoofdstuk "Doorgangen (ingangskant)" met alleen de melders waar het vervolg afhangt van de ingangskant (zoals 24).
4. NIEUW, mogelijke oorzaak van "wisselmotor 13 staat steeds verkeerd": na het met de hand terugzetten van de loc (`VraagLocTerugTeZetten`) vergat de verkenner nooit welke stand hij dacht te hebben gestuurd, en `ZetWissel` stuurt niets als die onthouden stand al klopt. Een open gereden of met de hand verlegde motor (kruiswissel!) bleef dus verkeerd staan terwijl het programma dacht dat alles goed stond (de centrale meldt de werkelijke stand niet terug). Nu worden na elk handmatig terugzetten alle onthouden standen vergeten, zodat alle wissels in het volgende stuk opnieuw gestuurd worden. Dit kost wat extra wisselcommando's (zie #65, nog open: het aantal commando's staat in het log).
Getest in de simulator: `DoorgangTest` (kruiswissel-scenario 14/21/8 met standen, onbekende ingang, onbekend vervolg, JSON heen en terug, oude kaart, rapport, en een gesimuleerde rit die de doorgangen over de ritgrens vastlegt). NIET getest op de baan. De simulator heeft geen echte Engelse wissel, dus de keuze van de wisselstand bij een echte kruiswissel is alleen op kaartniveau getest.
Beperking: het geleerde komt pas na een eerste keer rijden door de kruising; wat nog nooit gereden is, kan de verkenner niet weten.

## Nieuw (Marco 18:09): een voltooide verkenning uitbreiden met een tweede los traject
Marco heeft twee fysiek gescheiden trajecten (blokken 10, 11, 12 en de overige blokken) en nu twee losse kaartbestanden. Gebouwd: de knop "Vorige verkenning hervatten / uitbreiden" biedt bij een voltooide verkenning aan die uit te breiden. Alles blijft staan (melders, overgangen, wissels, doorgangen, Dinamo-blokken, geïdentificeerde adressen); je zet de loc op het andere traject, die melder wordt de nieuwe startmelder (de oude blijft bewaard in `EerdereStartMelders` en staat in het rapport). Is die melder al bekend uit het eerste traject, dan volgt een vraag. De uitbreiding is daarna een gewone, hervatbare verkenning; aan het eind staat alles in een kaart, ook de voorgestelde blokken en het schema.
Getest in de simulator: `UitbreidenTest` (twee losse stukken spoor 1-2-3 en 4-5: vijf melders in een kaart, geen valse overgang ertussen, beide startmelders). Het WPF-deel (knop/vragen) is NIET gecompileerd en NIET getest. Voor de twee bestaande kaartbestanden: die kunnen apart in Modeltreinbesturing worden geïmporteerd (de import plaatst nieuwe blokken netjes onder het bestaande schema); samenvoegen is dus niet nodig.

## #75 - Baanverkenner: tweede bezette melder bij een kortsluiting wijst de betrokken wissel aan (Marco 18:53)
Idee van Marco: bij een kortsluiting kijken welke tweede melder op dat moment actief is, dan weet je al welke wissel erbij betrokken is. Gebouwd: bij elke kortsluiting wordt vastgelegd welke andere melder(s) bezet waren (`Kortsluitpunt.TweedeMelders`, in log en rapport). Bij het oplossen gaan de wissels die dat verklaren eerst aan de beurt: is de overgang tussen de kortsluitmelder en de tweede melder eerder bij een andere wisselstand gereden, dan zijn de wissels die in die stand anders stonden de eerste verdachten (`Baankaart.HintsBijTweedeMelder`). Zonder tweede melder, of zonder eerder gereden overgang, blijft de oude volgorde.
Getest: `TweedeMelderTest` (24 met tweede melder 8 -> wissel 5 afbuigend; met 21 -> wissel 10 terug naar rechtdoor). NIET getest op de baan: of bij een echte kortsluiting de tweede melder altijd bezet is, hangt af van wanneer de melders wegvallen (bij "geen enkele melder meer bezet" is er geen tweede melder en blijft alles zoals het was).

## #76 - Baanverkenner: melding "gestopt vóór bekend kortsluitpunt" klopt niet met de werkelijke plek (Marco 19:00)
De verkenner stopt op de melder waarna een bekend kortsluitpunt volgt; de loc staat dan al op dat kortsluitpunt (de laatste melder vóór de kortsluiting), niet ervoor. Alleen de tekst was misleidend. Nu: log "gestopt op melder X: dit is het bekende kortsluitpunt…" en in het rapport "gestopt op het bekende kortsluitpunt". Het gedrag is ongewijzigd. Tests: ALLES OK. Of de loc op de baan precies op de melder stopt (traagheid) is nog niet nagekeken.

## #77 - Baanverkenner: terugweg met een wisselstand die bij die stap niet bestaat geeft kortsluiting (Marco 19:07, log 18:57-19:03)
Log 18:58:45: de proefrit 21 -> 24 -> 129 -> 14 (achteruit, wissel 10 afbuigend, wissel 5 rechtdoor) eindigde op melder 14 met 129 nog bezet (staart op de wissel). De terugweg 14 -> 129 -> 24 -> 21 werd met dezelfde stand gestart (wissel 5 rechtdoor), terwijl 14 -> 129 alleen met wissel 5 afbuigend bestaat: kortsluiting in blok 3 en 9, en Marco moest de loc weer terugzetten. Oorzaak: de kaart nam aan dat een overgang X -> Y bij een stand ook Y -> X bij die stand geeft; voor een wissel die van achteren bereden wordt (129 -> 14 lukte "toevallig" met wissel 5 rechtdoor, de tongen werden open gereden) is dat niet waar.
Fix: `Baankaart.WisselEisenVoorStap` leidt uit de gevonden splitsingen af welke stand een stap vraagt (kop en van achteren) en `MetWisselEisen` vult de stand van een navigatiepad daarmee aan. Toegepast op `NaarStartVan`, `TerugNaarHuis` en `TerugNaar`; het log meldt elke aanpassing. Tegenstrijdige eisen binnen een pad worden niet toegepast. Test: `WisselEisenTest` met de baankaart van 19:03 (14-129-24-21 vooruit met 10 -> 5 en 10 afbuigend; 14-24 vraagt 5 rechtdoor).
Nog niet getest op de baan. Verkennen zelf (de proefritten) gebruikt de eisen bewust niet, want daar probeert de verkenner juist standen uit.
Overige waarnemingen uit dit log (NIET opgelost):
- Kortsluitpunt #6 (19:03, 24 -> 8 vooruit met 5 en 10 afbuigend) terwijl dat eerder twee keer lukte: de loc was met de hand op 24 gezet en vertrok eerst niet. Mogelijk stond een motor fysiek anders (zie #74, alle wissels worden nu na handmatig terugzetten opnieuw gestuurd; dit log kwam uit een versie waarin dat al zat, dus oorzaak ook elders mogelijk).
- Wissels 11, 12 en 13 geven alle drie afzonderlijk dezelfde uitkomst (13 -> 24 zonder 14): vraag aan Marco welke adressen fysiek bij elkaar horen.
- Idee: wissels niet omzetten zolang de loc op twee melders staat (de staart zit dan nog op de wissel). Zie ONDERZOEK_ANDERE_SOFTWARE.md punt 5.
Onderzoek naar Rocrail, Win-Digipet, JMRI en de NMRA-normen: `Modeltreinbesturing/ONDERZOEK_ANDERE_SOFTWARE.md`.

## #78 - Baanverkenner: wisselstraten meteen mee afleiden (A) en importeren in Modeltreinbesturing (B) (Marco 07:53/07:56)
- A (`Baanverkenner.Kern/Wisselstraten.cs`): per gereden overgang een voorstel met de benodigde wissels en standen, uit wat de verkenning al weet (splitsingen, kortsluiting bij omzetten, gereden stand als "aangenomen"). Er wordt niets extra gereden. Staat in het rapport ("Wisselstraten (voorstel)") en in de baankaart-JSON. Alternatieven (bijv. 13 -> 24 via wissel 11, 12 of 13) worden apart getoond.
- B (`Modeltreinbesturing/WisselstraatImporter.cs`, gekoppeld in `BaankaartImporteren_Click`): eerst een voorbeeld met meldingen, pas na "Ja" worden de wisselstraten toegevoegd. Alleen waar een BlokRelatie bestaat; een blokpaar dat al een wisselstraat heeft blijft ongemoeid; wissels die niet op het baanontwerp staan worden gemeld en de wisselstraat wordt dan overgeslagen; een kruiswissel krijgt beide motoren (eis + overige motor uit de gereden stand); alternatieven krijgen Volgnummer op volgorde van aantal keer gezien. "Aangenomen" eisen geven een waarschuwing.
- C (actief controleren door de straat echt te rijden) is NIET gebouwd; eerst aan Marco vragen of hij dat wil.
- Test: CompileCheck `WisselstratenTest` en `WisselstraatImportTest`. De WPF-koppeling is hier niet te compileren. Nog niet getest op de baan.

## #79 - Baanverkenner: eerst vragen "Staat er een wissel verkeerd?" voordat de loc verplaatst moet worden (Marco 07:59)
- Bij `VraagLocTerugTeZetten` komt eerst Ja/Nee/Annuleren: Ja = adres van de wisselmotor vragen en die meteen (opnieuw) schakelen (naar de stand die het programma bedoelde, anders eerst vragen welke stand); herhaalbaar voor meerdere wissels. Staat de loc daarna alleen op de melder, dan hoeft hij niet verplaatst. Nee = de oude vraag om de loc te verplaatsen. Annuleren stopt de verkenning.
- Test: CompileCheck `VerkeerdeWisselVraagTest`. De dialogen in het WPF-venster (`VraagJaNeeGebruiker`, `VraagAdresGebruiker`) zijn hier niet gecompileerd. Nog niet getest op de baan.
- **Aanpassing (Marco 08:24, log 08:11-08:22):** de eerste versie stuurde bij "wissel 14 staat verkeerd" steeds dezelfde stand (afbuigend) opnieuw, vier keer achter elkaar. Nu wordt "verkeerd" altijd opgevat als: schakel naar de TEGENOVERGESTELDE stand van de bekende stand (afbuigend -> rechtdoor, rechtdoor -> afbuigend; nog een keer "verkeerd" draait hem weer terug). Is de stand niet bekend, dan vraagt de verkenner of de wissel nu afbuigend staat. Test: `VerkeerdeWisselVraagTest`. Nog niet getest op de baan.
- **Uitzoeken (Marco 08:27, log 08:18-08:22):** de wissel die "verkeerd" stond (14) was door de verkenner zelf afbuigend gezet en gelaten. Verloop: bij de wisselscan van adres 14 (afbuigend, hele lus) kwam kortsluiting na melder 8; daarna blijft 14 volgens BUG #63 bewust afbuigend staan terwijl partnerwissels (13, ...) geprobeerd worden (overloopwissels). De loc vertrok toen niet meer van melder 8, en de gebruiker moest 14 handmatig terugzetten. Dus: een goed werkende partnerzoektocht laat een wissel afbuigend; lukt het niet, dan zit de loc vast op de verkeerd staande wissel. Verbetering: de vraag "Staat er een wissel verkeerd?" noemt nu de wissels die de verkenner zelf afbuigend heeft gezet en niet teruggezet (eerste verdachten, hier 13 en 14). Nog niet opgelost en nog te bekijken: de partnerzoektocht afbreken zodra de loc na het omzetten niet meer vertrekt, en wissel `a` dan eerst terugzetten. Nog niet getest op de baan.

## #80 - Baanverkenner: wissel omgezet terwijl de loc erop staat laat hem ontsporen (Marco 09:39, log 09:07)
- Oorzaak: na een mislukte terugweg zette de gebruiker de loc met de hand op melder 24 (kruiswissel). Daarna zette de verkenner direct ALLE wissels terug naar rechtdoor (`ZetConfiguratie(Basis)` in de `NavigatieFout`-afhandeling, 09:07:15-26), ook wissel 10 die net op afbuigend was gezet en waar de loc op stond: wissel 10 schakelde onder de loc door, de loc ontspoorde in melder 24.
- Oplossing: `WisselsOnderLoc()` bepaalt welke wissels bij een nu bezette melder liggen (waarneming "kortsluiting bij omzetten" of "lost kortsluiting op" bij die melder: de wissel wordt van achteren bereden en ligt in of vlak voor die melder). `ZetConfiguratie(Basis, spaarOnderLoc: true)` slaat die wissels over (log: "Wissel X wordt nu NIET omgezet..."); gebruikt op de drie plaatsen waar na een mislukte terugweg wordt teruggezet zonder dat er direct een rit volgt. Ze worden omgezet bij de volgende opdracht, als de loc weg is. Bij een gewone rit blijft het gedrag ongewijzigd (de loc staat dan voor de wissel).
- Detectie achteraf: bij een kortsluiting/ontsporing binnen 8 s na een wisselcommando komt in het log "Let op: ... wissel X is geschakeld terwijl melder Y bezet was", met de toevoeging dat de wissel bij die melder ligt als dat uit de kaart blijkt.
- Beperking: wissels waarvan nog geen kortsluiting-waarneming bij die melder bestaat worden niet herkend. Test: `WisselOnderLocTest`. Nog niet getest op de baan.

## #81 - Dinamo vastgelopen: stilte van de centrale was niet te zien in de logs (Marco 10:07, log 09:58-10:04)
- Aanleiding: Dinamo meldde kortsluiting terwijl de loc er geen gaf; na een herstart van Dinamo ging alles weer goed. Keepalives (lege berichten, 5x per seconde) staan bewust niet in het hardwarelog, dus achteraf was niet vast te stellen of Dinamo nog antwoordde.
- Wat het log wel liet zien: van 09:58:53 tot 10:02:59 kwam er vrijwel geen databericht van Dinamo (ritten van 10:00:48 en 10:01:40 zonder één bericht), terwijl wij bleven sturen. Twee kortsluitmeldingen in die tijd (10:00:54, 10:01:46) hadden niet de zin "Dinamo meldt kortsluiting in blok". Bewijs is dat niet: ook eerder op de dag hadden 14 van de 66 ritten geen databerichten.
- Oplossing: `DinamoHardware` onthoudt het tijdstip van elk ontvangen bericht, keepalives meegeteld (`IVerbindingsBron.SecondenSindsLaatsteOntvangst`). Is het ≥ 2 s stil terwijl wij sturen, dan komt er één regel in het hardwarelog ("GEEN bericht (ook geen keepalive) van Dinamo sinds x s ... Dinamo reageert niet meer") en bij herstel "Weer bericht van Dinamo ontvangen na x s stilte". Bij een kortsluitvermoeden zet de verkenner in het log een waarschuwing dat Dinamo waarschijnlijk vastliep en herstart moet worden (`BaanVerkenner.StilteWaarschuwing`).
- **Correctie (11:05):** de aanname dat Dinamo toen vastliep is NIET onderbouwd. In het hardwarelog van 10:04 staan wel degelijk Dinamo-meldingen in de 'stilte': blokalarm 8 wisselde van 09:58:31 tot 10:00:13 steeds aan/uit (en was 09:59:10-10:00:13 continu aan), daarna F-bit-fouten tot 10:02:07. Dinamo reageerde dus. De meting uit #81 blijft nuttig maar was niet de oorzaak, zie #82.
- Niet gebouwd: automatisch stoppen of een dialoog bij stilte; eerst zien hoe vaak het voorkomt.
- Test: CompileCheck `DinamoStilteTest` (de logica rond de drempel). De Dinamo-kant zelf (tijdmeting in `VerwerkOntvangenData`) kan hier niet getest worden. Nog niet getest op de baan.

## #82 - Dinamo-blokalarm vlak voor de volgende stap werd genegeerd; wissels omgezet en gereden in een kortsluiting (Marco 11:05, logs 10:04 en 11:03)
- Beide logs eindigen op dezelfde stap: de loc komt op melder 24 (kruiswissel) met wissel 10 afbuigend, 1,5-2,4 s na "melder 24 bezet" meldt Dinamo kortsluiting in blok 8 (10:04:39,483 en 11:03:39,015), en daarna zet de verkenner 5, 13 en 14 om en start "Rijden vooruit van melder 24 naar melder 8" (10:04:42 en 11:03:41), terwijl het alarm nog actief is. Dinamo stuurde tot het einde van het log gewoon door (5 berichten per seconde): er is geen aanwijzing voor een vastloper.
- Oorzaak in de code: `RitUitvoeren` zet bij de start `_kortsluitingGemeld = false`. Het blokalarm kwam tijdens de voorbereiding binnen en wordt maar één keer gemeld (alleen bij wijziging), dus tijdens de rit was het alarm onzichtbaar. Ook de wissels (13 en 5 liggen bij melder 24) werden onder de loc omgezet.
- Oplossing: `ControleerGeenAlarmVoorStap`: staat er een blokalarm vóór het omzetten van de wissels of de ritstart (navigatie, terugweg, `Rit`), dan 3 s wachten of het verdwijnt; zo niet, noodstop en een NavigatieFout. Er komt dan de vraag "Staat er een wissel verkeerd?" (#79) met de verdachten. Zet de gebruiker de loc terug, dan wordt het oude blokalarm vergeten (Dinamo herstarten is dan toegestaan).
- Waarschijnlijke achterliggende oorzaak (niet opgelost): de kortsluitproef #6 zet wissel 10 afbuigend en laat de loc dan 13 -> 24 rijden; wissel 5 gaat pas daarna naar afbuigend. Wissel 10 afbuigend op melder 24 gaf eerder al kortsluiting (kortsluitpunt #1), ook nu: 10:59:24, 11:02:31 en 11:03:39. Vraag aan Marco hoe dit anders moet.
- Test: CompileCheck `AlarmVoorStapTest`. Nog niet getest op de baan.

## #83 - Opgeloste kortsluitpunten werden niet op routes en terugwegen toegepast; dezelfde kortsluitingen kwamen steeds terug (Marco 11:21)
- Aanleiding: Marco ziet na uren testen steeds kortsluitingen op dezelfde plekken waarvan de oplossing al bekend is.
- Oorzaak (uit de baankaart van 11:03): kortsluitpunt #1 (na melder 24 vooruit, alleen wissel 10 afbuigend) is opgelost met wissel 5 afbuigend, en #4 (alleen 5 afbuigend) met wissel 10 afbuigend: 5 en 10 horen samen. Die kennis werd alleen gebruikt binnen het oplossen zelf. De routes naar kortsluitpunt #6 en #7 bewaarden de stand van toen (13 -> 24 met alleen 10 afbuigend). Bij elke proef van #6 (13 adressen geprobeerd) reed de loc daarom eerst 13 -> 14 -> 24 met 10 afbuigend en 5 rechtdoor (5 rechtdoor is nodig voor 14 -> 24), dus telkens opnieuw die kortsluiting: 10:59:24, 11:02:31, 11:03:39.
- Oplossing: `Baankaart.MetBekendeKortsluitOplossingen` (aangeroepen vanuit `MetEisen`, dus voor elke etappe van de route, de terugweg naar huis en de terugweg na een proef): bevat de stand alle afbuigende wissels van een OPGELOST kortsluitpunt op een melder van het pad (zelfde rijrichting), dan krijgt de oplosser (wissel A) de opgeloste stand. Botst dat met een eis van het pad (bijv. 5 moet rechtdoor voor 14 -> 24), dan gaan de wissels van die kortsluitstand weer rechtdoor. Open kortsluitpunten worden niet gebruikt. Alles komt in het log ("... aangepast om een bekende kortsluiting te vermijden: ...").
- Effect op het voorbeeld: 13 -> 14 -> 24 rijdt nu met alle wissels rechtdoor (bewezen bij 13 -> 14 en 14 -> 24); 24 -> 8 blijft 5, 10, 13, 14 afbuigend.
- Niet opgelost: kortsluitpunt #6 zelf staat nog open en de proef probeert nog steeds adres voor adres (nu zonder de overbodige kortsluitingen onderweg); de wissels die gekoppeld lijken (5 en 10, 9 en 10) worden niet apart als paar opgeslagen. De kruiswisselstanden uit KRUISWISSEL.md zijn niet aangeraakt.
- Test: CompileCheck `KortsluitOplossingTest` (met de baankaart van 11:03 in `Tools/CompileCheck/testdata`). Nog niet getest op de baan.

## #84 - Blok 8 en 9 ontstonden "uit het niets": Koploper-referentie voor de blokproef (Marco 12:35 / 12:43)
- Aanleiding: Marco heeft in Koploper geen blokken 8 en 9, maar de verkenner vond ze steeds opnieuw.
- Onderzoek van `Koploper_export_testbaan_002.xlsx` (kolommen Blokkaart / Blok / Wisselstraat, onderaan): blokkaart 1-7 en 10-12 zijn gewone blokken, maar blokkaart 8 en 9 hebben GEEN blok, alleen wisselstraat-overgangen. 8: 1>4, 1>7, 3>7, 4>1, 4>3, 7>1, 7>3. 9: 1>2, 1>4, 1>7, 2>3, 3>7, 4>1, 4>3, 7>1, 7>3. Marco's vermoeden klopt: de rijstroom van het wisselgebied (melder 24 en 129) wordt via de wisselstraat aangestuurd. Melder 24 (1>4, 4>1, 4>3, 7>1, 7>3) past in 8 en 9, melder 129 (1>2, 1>4, 4>1, 7>1) alleen in 9; dus 129 -> 9 en 24 -> 8. Dat is precies wat de blokproef vond. De eerdere conclusie "Koploper heeft geen blokken 8/9" was dus onjuist: de blokken bestaan, alleen als wisselstraatblok.
- Oplossing: `KoploperReferentie` (leest de export zonder extra pakketten, of het meegeleverde `Baanverkenner/koploper_referentie.json`). In `BlokKandidaten`: een doorgangsmelder (melder zonder Koploper-blok, alleen overgangen) proeft alleen de passende wisselstraatblokken (meest passende eerst, al gebruikte achteraan); een gewone melder proeft eerst zijn Koploper-blok. `KoppelBlok` waarschuwt als de gevonden koppeling afwijkt van Koploper (spookmelding?). De blokproef zelf blijft altijd meten; zonder referentiebestand werkt alles als voorheen.
- Test: CompileCheck `KoploperReferentieTest`. Nog niet getest op de baan.

## #85 - Kruiswisselstanden (motor 13 en 10) werden steeds overschreven; vaste kruiswissel-referentie (Marco 12:55)
- Aanleiding: het kruiswissel blijft problemen geven; Marco vermoedt dat kortsluitingen en onthouden routes de standen van 13 en 10 overschrijven.
- Oplossing: `KruiswisselReferentie` (Kern): de vier benen met alle bezetmelders in volgorde (plusfiguur, nu ook in het rapport als SVG en tekst) en per verbinding de bewezen stand van motor 13 en 10 (14<->21: rechtdoor/rechtdoor; 14<->8: 13 afbuigend, 10 vrij; 129<->21: 13 rechtdoor, 10 afbuigend; 129<->8 onbekend, niets afgedwongen). `PasToe` wordt als LAATSTE stap van `MetEisen` toegepast op elke doorgang X -> 24 -> Y van een pad (navigatie, terugweg, proefterugweg), dus ook na `MetBekendeKortsluitOplossingen` (#83). Paden die op/bij 24 beginnen of eindigen en tegenstrijdige eisen blijven onaangeroerd. Het afdwingen staat in het log ("aangepast aan de kruiswissel-referentie").
- Niet gedaan: de standen zijn nog NIET in een wisselstraat van Modeltreinbesturing gezet: die hebben andere (veldgetest genoemde) waarden voor 7 -> 3 en 4 -> 3 (zie KRUISWISSEL.md); eerst Marco's bevestiging nodig.
- Test: CompileCheck `KruiswisselReferentieTest`. Nog niet getest op de baan.

## #86 - De loc bleef doorrijden na de stopmelding bij een kortsluiting (Marco 13:02)
- Oorzaak (hardwarelog 12:26:44): Dinamo meldde kortsluiting in blok 2 (tot 45.48) en blok 3 (tot 45.68); een blok in kortsluiting heeft geen rijstroom, dus de stopframes voor blok 2 en 3 (44.88 en 45.08) bereikten de loc niet. Zodra de kortsluiting weg was reed hij met de oude snelheid door (alarm kwam om 48.28 terug) en de stop werd nooit herhaald: de bestaande controle keek alleen naar melderwijzigingen.
- Oplossing: `VolledigeStop` stuurt de hele stoppas opnieuw (max. 4 passen) als er tijdens de stop, of in de 3 s ervoor, een blokalarm was; eerst wachten tot het alarm weg is (max. 6 s) en 0,5 s tot de rijstroom terug is. Staat in het log ("de stopcommando's zijn dan niet aangekomen").
- Test: CompileCheck `StopHerhalingTest`. Nog niet getest op de baan.

## #87 - Een met de hand gecorrigeerde wissel (wissel 1 bij wissel 2 afbuigend) werd niet onthouden (Marco 13:01)
- Aanleiding: opdracht 13 achteruit met wissel 2 afbuigend gaf steeds kortsluiting na melder 12; Marco zette telkens wissel 1 goed (via de vraag "Staat er een wissel verkeerd?") en dan was de kortsluiting weg, maar de verkenner leerde dat niet: kortsluitpunt #1 bleef open en de opdracht werd elke keer opnieuw zonder wissel 1 gereden. Daarbij begon elke nieuwe verkenning ook weer van nul: bewezen kortsluitoplossingen stonden niet in het leerprofiel.
- Oplossing: (1) `ControleerVerkeerdeWissel` legt de correctie van de gebruiker vast als oplossing van het laatste open kortsluitpunt (`Baankaart.RegistreerHandmatigeOplossing`). (2) Een opdracht rijdt voortaan met de extra wissel van een bekend opgelost kortsluitpunt voor dezelfde start, richting en stand (`RijStand`, `MetBekendeOplossingenVoorStart`), dus 13 achteruit met wissel 2 rijdt met wissel 1 erbij. (3) Alle bewezen oplossingen staan in het leerprofiel (`KortsluitOplossingen`) en worden bij een volgende verkenning meteen overgenomen (`Baankaart.BekendeKortsluitOplossingen`); `MetBekendeKortsluitOplossingen` (#83) gebruikt ze ook.
- Let op: een onjuiste aanwijzing van de gebruiker zou ook onthouden worden; verwijderen kan door `leerprofiel.json` te verwijderen of de regel onder `KortsluitOplossingen` te wissen.
- Test: CompileCheck `HandmatigeOplossingTest` (baankaart van 12:58). Nog niet getest op de baan.

## #88 - Geen verzonnen bloknummers meer, alleen echte Dinamo-bloknummers (Marco 13:03)
- Het rapport, het blokkenschema en de wisselstraten toonden "Blok 1 t/m 8" (volgnummers van de verkenner) naast de Dinamo-blokken; dat gaf verwarring (bv. "Blok 7" = Dinamo-blok 8).
- Oplossing: `VoorgesteldBlok.Nummer` is nu het Dinamo-bloknummer en `Label` toont alleen dat nummer; onbekend heet "onbekend (melder X)", een tweede losse groep met hetzelfde Dinamo-blok "N (deel 2)". Rapport (html en tekst), blokkenschema ("Dinamo-blok N") en wisselstraten ("Dinamo-blok 3 -> Dinamo-blok 8") gebruiken dat. Oudere baankaarten krijgen ook de juiste labels.
- Test: CompileCheck `EchteBloknummersTest`. Nog niet getest op de baan (WPF-vensters ongecompileerd).

## #89 - Het pluskruis stond bij een nieuwe verkenning al helemaal gevuld (Marco 13:44)
- De kruiswissel-weergave (#85) toonde altijd alle vaste referentiemelders en standen, ook als de verkenning nog niets gevonden had; dat suggereerde dat het al ontdekt was.
- Oplossing: het kruis blijft staan (met de vier benen) maar een melder staat er pas in als deze verkenning hem gevonden heeft (`KruiswisselReferentie.AlsSvg/AlsHtml/AlsTekst` met de baankaart); anders blanco. Het Dinamo-blok boven een been staat er pas als het gevonden is. In de tabel/tekst staan de standen van motor 13 en 10 pas bij een verbinding waarvan melder 24 en beide benen gevonden zijn, anders "nog niet gevonden door deze verkenning". De vaste referentie die de verkenner zelf gebruikt (#85) verandert niet.
- Test: CompileCheck `KruiswisselReferentieTest`. Niet apart opgeleverd (Marco: nog geen nieuwe versie nodig); zit in de volgende zip.

## #90 - Blokkenschema: melders op de zijde van het buurblok, kruiswissel als X (Marco 14:06/14:13)
- Wens: elke bezetmelder staat op de zijde van de rechthoek waar de pijl naar het aangrenzende blok vertrekt; liggen er meerdere melders tussen de pijlen, dan in de volgorde van de aangrenzende melders. Rond de kruiswissel een X in plaats van een plus.
- Oplossing (`Baanverkenner.Kern/Blokkenschema.cs`): per melderpaar een eigen verbinding (`SchemaVerbinding.VanMelder/NaarMelder`); grensmelders zijn vlakjes (`SchemaMelder`) op de rand, zijde bepaald uit de richting naar het buurblok, volgorde op de plaats van de buren (zelfde volgorde aan beide uiteinden, dus geen kruisende lijnen); een vak wordt breder/hoger als er veel vlakjes op één zijde staan. De kruiswissel (melder 24 met 3-4 buren) is een X met de buurmelders (14, 129, 21, 8) in de hoeken, hoeken zo toegewezen dat ze naar het betreffende blok wijzen; de naam staat aan de kant zonder been. Labels worden niet meer over vakken, melders of andere labels geplaatst; de tekening wordt tot het nodige formaat bijgesneden.
- `Baanverkenner/BlokkenschemaWindow.xaml.cs` tekent dezelfde indeling (vlakjes en X). Dit WPF-venster is hier NIET gecompileerd; de SVG in het rapport is wel visueel gecontroleerd.
- Test: CompileCheck `BlokkenschemaTest`. Nog niet getest op de baan.

## #91 - Kruiswissel-referentie ook in Modeltreinbesturing (Marco 14:32)
- Wens: de referentie voor motor 13 en 10 (#85) ook in de Modeltreinbesturing-module gebruiken.
- Oplossing: `KruiswisselReferentie.ZoekOpBlok(blokA, blokB)` (Kern) en nieuw `Modeltreinbesturing/KruiswisselControle.cs`; "Controleer mijn baan" (`BaanControleBeheerder`, nu met de wisselstraten) legt de kruiswisselstanden van elke wisselstraat naast de referentie en meldt verschillen als Info. Het WPF-scherm roept dit aan via `BaanControleDialog` (optionele parameter).
- BEWUST ALLEEN SIGNALEREN: er wordt niets aangepast of gespiegeld. Jouw veldgetest standen blijven leidend (KRUISWISSEL.md); de wisselstraten zijn dus ongewijzigd.
- Uitkomst op de huidige wisselstraten (testbaan-v24): afwijkend zijn 7->3 (motor 13 en 10), 4->3 (motor 13) en 3->4 (motor 13); 7->1, 3->7 en 1->4 (onbekend) niet. Marco moet bevestigen welke bron klopt (omgekeerde polariteit van de motor?) voordat er iets verandert.
- Test: CompileCheck `KruiswisselControleTest`. WPF-deel hier niet gecompileerd. Nog niet getest op de baan.

## #92 - Sweep met wissel 10 afbuigend: kortsluiting op melder 24, verkenner gaf op en vroeg de loc terug te zetten (Marco 14:42)
- Log 14:38-14:40 (versie zonder #89-#91): de adrestest "afbuigend: 10" gaf kortsluiting zodra de loc op melder 24 kwam; de kortsluiting bleef zolang wissel 10 afbuigend stond en was weg 2 s nadat hij weer rechtdoor stond (14:39:13 -> 14:39:15). De loc en de andere tongen stonden dus goed. De partnerzoektocht (overloopwissels) zette ondertussen wissel 5 afbuigend terwijl de loc op de kruiswissel stond, gaf op ("zet geen wissels om") en liet Marco de loc naar melder 13 terugzetten.
- Oplossing: bij een kortsluiting op melder 24 met adres 13 of 10 afbuigend geen partnerzoektocht meer (motor 13 en 10 zijn geen overloopwissels; hun stand per rit staat in de kruiswissel-referentie, #85): het adres wordt direct teruggezet en de bevinding gelogd. `BaanVerkenner.IsKruiswisselMotorKortsluiting`, aanroep in de adres-lus van `VoerOpdrachtUit`.
- Nog NIET opgelost: de adrestest rijdt zelf nog steeds één keer in die kortsluiting (wissel 10 afbuigend op 14 -> 24); dat is het gedrag van de adrestest en levert juist de informatie. Een stap verder (die rit overslaan op basis van de referentie) is niet gedaan: de referentie bewijst alleen dat 10 rechtdoor werkt, niet dat 10 afbuigend altijd kortsluit.
- Test: CompileCheck `KruisMotorKortsluitingTest`. Nog niet getest op de baan.

## #93 - Wissel 5 ontspoord/kortsluiting (te laat geschakeld?) en kortsluiting 129 -> 24 met wissel 10 rechtdoor (Marco 15:23)
- Log 15:20-15:22: (a) na het commando wissel 5 afbuigend (15:20:28.4) startte de loc 0,6 s later; 3,6 s na het commando kortsluiting. Dezelfde rit zonder nieuw commando lukte daarna. Of de tong echt te laat omschakelde is NIET te bewijzen: de Dinamo-echo komt uit een poll-cyclus (~1,2 s per adres). (b) Kortsluiting met wissel 2 afbuigend: de oplossing "wissel 1 erbij" (#87) staat opgeslagen in kortsluitpunt #1 en in het leerprofiel en wordt voortaan meteen toegepast. (c) De verkenner reed 129 -> 24 vooruit met wissel 10 rechtdoor (kortsluiting blok 9), terwijl de kaart al wist dat 129 -> 24 -> 21 wissel 10 afbuigend vraagt.
- Oplossing (a): `ZetWissel` wacht 2 s extra als de wissel direct achter een stilstaande loc ligt (`ExtraWisselWachtMs`); een kortsluitpunt dat bij dezelfde rit zonder wijziging alsnog lukt, krijgt `Tijdelijk` (`MarkeerTijdelijkeKortsluiting`), staat zo in log, rapport en schema en telt niet meer als open (ook niet in `LosKortsluitpuntenOp`).
- Oplossing (c): `RijStand` zet, als vanaf de start in die richting precies één vervolgmelder bekend is, de wissels die dat vervolg nodig heeft meteen goed, maar alleen extra AFBUIGEND en nooit het adres dat de opdracht zelf test (`BronAdres`).
- Tests: CompileCheck `TijdelijkeKortsluitingTest`, `KenVervolgTest`. Nog niet getest op de baan.

## #94 - Loc reed meteen weg zodra Marco hem terugzette op melder 13 (Marco 15:46)
- Log 15:39-15:43: opdracht #7 (24 achteruit, wissel 10 afbuigend) gaf kortsluiting in blok 8; de verkenner vroeg de loc terug te zetten op 13. De loc reed na het neerzetten meteen weg tot blok 7, na nog eens terugzetten weer, en stopte pas bij de ontsporing / de eindstop 15:43:34.
- Oorzaak: Dinamo onthoudt per blok het laatste rijcommando. De stop (15:40:21-23) ging alleen naar blok 8 en 7, waar de loc stond. Blok 3 (melder 13) had nog "achteruit stap 12" van de rit van 15:39:07 (A8 82 CC) en kreeg daarna nooit snelheid 0. Een loc die op zo'n blok wordt neergezet pakt dat commando op. Er ging tussen 15:40:23 en 15:43:13 niets meer uit de PC: de loc reed dus op een onthouden commando.
- Oplossing: `VraagLocTerugTeZetten` stuurt vóór elke vraag eerst `VolledigeStop()` (snelheid 0 naar ALLE blokken, wacht tot een blokalarm weg is, #86). Het staat in het log: "Voor het terugzetten van de loc: stop naar alle blokken...".
- Test: CompileCheck `StopVoorTerugzettenTest`. Nog niet getest op de baan.
- OPEN (niet opgelost, want niet te bewijzen zonder baan): opdracht #7 begon op melder 24 met de loc vanaf 21, en zette wissel 10 afbuigend onder de staande loc: kortsluiting (zelfde verschijnsel als #92). `ZetConfiguratie` spaart wissels onder de loc alleen bij een terugweg, niet hier. Omzetten vóór het inrijden van 24 kan alleen als de combinatie ingang/uitgang bewezen is; de uitgang is bij een verkenning nog onbekend.

## #95 - Opdracht op de kruiswissel (24 achteruit, wissel 10 afbuigend) gaf bij elke hervatting weer kortsluiting (Marco 15:51)
- Log 15:46-15:49 (nog zonder #94): de loc reed achteruit naar 24 (aankomst vanaf 21), daarna zette de verkenner wissel 10 afbuigend ONDER de staande loc (15:48:37,6) -> kortsluiting blok 8; na het wachten ging wissel 10 weer rechtdoor (15:48:41,4). Dat zijn de twee schakelingen die Marco hoorde. Opdracht #7 kon zo nooit slagen en kwam na elke hervatting terug. De loc reed daarna ook weer weg (zelfde oorzaak als #94, die hersteld is; de hervatting was vóór die versie gestart).
- Oplossing: `KruisAankomstStand` (BaanVerkenner): begint de opdracht op melder 24 en wijkt de gewenste stand van motor 13/10 af van de rijstand, dan worden die twee motoren al VOOR het inrijden van 24 gezet, maar alleen als de referentie (`KruiswisselReferentie.Verbindingen`, alleen bewezen) zegt dat de ingang waar de loc vandaan komt samen met die standen bestaat (bv. ingang 21 met 10 afbuigend = 129<->21). Anders blijft alles zoals het was. `NaarStartVan(route, rijstand)`; `VoerOpdrachtUit` geeft de rijstand mee. Er wordt dus niets meer onder de staande loc omgezet.
- Test: CompileCheck `KruisAankomstTest` (6 gevallen). Nog niet getest op de baan.

## #96 - Loc in de verkeerde rijrichting neergezet; motor 13/10 omgezet onder de loc op de kruiswissel (Marco 16:01 en 16:05)
- Marco: de loc werd twee keer in de verkeerde rijrichting op melder 13 gezet, terwijl de verkenner weet welke kant hij op reed; en weer kortsluiting op de kruiswissel met motor 13 verkeerd.
- Oorzaak rijrichting: de vraag "Zet de loc op melder N" zei niets over de stand van de neus; 'vooruit' in de verkenner is de kant van de volgende melders uit de kaart. Oplossing: `OrientatieTekst` in de vraag, bv. "RIJRICHTING: de NEUS van de loc naar melder 14 en de achterkant naar melder 5". Test `OrientatieTekstTest`.
- Kruiswissel: uit alle logs (14:40, 15:40, 15:48, 10-10 eerder) volgt dat een commando voor motor 13 of 10 terwijl de loc op melder 24 staat altijd binnen 0,2 s een kortsluiting geeft. Oplossing: in `VoerOpdrachtUit` wordt, als de loc na het naar de start rijden op melder 24 staat en motor 13/10 nog omgezet zou moeten worden (kon vooraf niet, zie #95), de opdracht overgeslagen met een waarschuwing en gaat de loc terug zonder iets om te zetten (`TerugNaarHuis` met de huidige stand).
- NIET opgelost / niet bewezen: waarom motor 13 fysiek verkeerd staat (Dinamo geeft alleen het laatste commando terug, geen echte tongstand). De logs van de laatste mislukte run ontbreken.
- Nog niet getest op de baan.

## #97 - Loc stond op melder 13 andersom; de rit "13 achteruit" reed de hele baan rond tot kortsluiting bij wissel 2 (Marco 16:08)
- Log 16:03-16:06 (hardwarelog): opdracht "13 achteruit, wissel 1+2 afbuigend" (16:03:46-47). Blokcommando's daarna: blok 3 -> 8 -> 7 -> 6 -> 5 -> 4 -> 3: de loc reed 13 -> 14 -> 24 -> 21 -> ... -> 16 -> 5 (dus de tegengestelde kant op) en kwam na ruim 80 s van achteren bij wissel 2 (afbuigend) aan: kortsluiting in blok 3 (16:05:17). Dat is wat Marco zag ("wissel 2 afbuigend open rijden van blok 4 naar 3"). Wissel 2 afbuigend geeft volgens de eigen kaart kortsluiting; het was een gevolg van de rit, niet de oorzaak.
- Oorzaak: de loc stond andersom (neus de verkeerde kant op). De eerste melder (14) hoort in de kaart bij 13 VOORUIT; de verkenningsrit controleerde de eerste stap niet en liet hem de hele baan rondrijden (de afwijking van de basisrit wordt pas na ExtraNaAfwijking melders beoordeeld).
- Oplossing: `IsLocAndersom` in `RitUitvoeren`: wijkt bij een verkenningsrit de EERSTE stap af van de basisrit, hoort die stap alleen bij de tegengestelde richting, is hij in deze richting met deze wisselstand nooit gezien en is de basisstap wel bekend, dan stopt de loc direct, wordt er niets in de kaart vastgelegd en volgt de normale 'zet de loc terug'-vraag met de uitleg dat de loc andersom staat. Test: CompileCheck `LocAndersomTest` (echte kaart van 16:08, 4 gevallen).
- Opmerking: de neus-tekst uit #96 stond er in feite al; die is dus geen echte oplossing. De kaart van 16:08 bevat de foute rit al (13 -> 14 achteruit bij wissel 1,2), en ook andere overgangen met wissel 13 afbuigend (13 -> 5 vooruit, 14 -> 13 vooruit) zijn verdacht (mogelijk ook met de loc andersom gereden): controleren voor die kaart verder gebruikt wordt.
- Nog niet getest op de baan.

## #98 - Wissel 14 vergeten bij 14 -> 24 -> 8; kaart bevatte ritten met de loc andersom (Marco 16:24)
- Log 16:19-16:21: opdracht #8 "14 vooruit, motor 13 afbuigend" reed 14 -> 24 -> 8 en gaf kortsluiting in blok 4 direct na melder 8. 24 -> 8 vraagt wissel 14 afbuigend (bekend uit de splitsing van wissel 14 en uit de wisselstraten); de verkenner zette die pas na de kortsluiting ("Wisselstand voor het pad 8-24-14 aangepast").
- Oorzaak 1: `RijStand` (#93) paste alleen de eerste stap van het bekende vervolg toe. Oplossing: `VoorspeldPad` volgt het zeker bekende pad over meerdere stappen (per stap precies een vervolg; bij 24 de kruiswissel-referentie; kandidaten die een andere wisselstand eisen dan nu vallen af zolang er andere overblijven) en zet de eisen van dat hele pad meteen goed (alleen extra afbuigend, nooit de wissel die de opdracht zelf test).
- Oorzaak 2: de kaart bevatte overgangen uit ritten met de loc andersom (bv. 14 -> 13 vooruit, 13 -> 5 vooruit bij wissel 13 afbuigend) die het voorspellen verstoorden. Oplossing: `Baankaart.VerwijderSpiegelOvergangen` haalt bij elke start/hervatting overgangen weg waarvan het spiegelbeeld in dezelfde richting minstens 3x zo vaak is gereden; elke verwijdering staat als waarschuwing in het log.
- Tests: CompileCheck `VoorspeldPadTest` en spiegel-controles op de echte kaart. Nog niet getest op de baan.

## #99 - Kruiswissel: motor 13 niet vooraf gezet bij een rit 8 -> 24 (Marco 16:27)
- Log 16:23: opdracht #9 "8 achteruit, wissel 14 afbuigend" reed 8 -> 24 met motor 13 rechtdoor: kortsluiting in blok 8 direct na melder 24. De enige bewezen verbinding vanaf melder 8 (14 <-> 8) vraagt motor 13 afbuigend. #95 zette de motoren alleen als de loc op 24 MOEST stoppen; een rit die door 24 heen gaat kreeg niets vooraf. Marco zette motor 13 met de hand om (16:25).
- Oplossing: `KruisIngangStand` (in `RijStand`): gaat het voorspelde pad (`VoorspeldPad`) de kruiswissel in, dan krijgt motor 13 (en 10) vooraf de stand waar ALLE bewezen verbindingen vanaf die ingang het over eens zijn (8: motor 13 afbuigend; 21: motor 13 rechtdoor; 14: twee verschillende standen bewezen, dus niets afdwingen). De motor die de opdracht zelf test blijft ongemoeid. De motor wordt gezet terwijl de loc nog ver weg staat.
- Test: CompileCheck `KruisIngangTest`. Nog niet getest op de baan.
- Let op: het log van 16:26 komt van de versie van 16:18 (zonder #97/#98/#99).

## #100 - Tegenstrijdige opgeloste kortsluitpunten zetten elkaar terug (log 16:29, oude build)
Pad 14-129-24 (vooruit): kortsluitpunt #2 zegt 13 afbuigend, #4 zegt 13 rechtdoor. `MetBekendeKortsluitOplossingen` zette ze 5 rondes lang
om en om (20 identieke regels in het log); de uitkomst hing van het toeval van het aantal rondes af. Nu: een adres dat door twee
oplossingen tegenovergesteld wordt gevraagd blijft op zijn oorspronkelijke stand en wordt eenmaal gemeld. Test: `BotsendeOplossingenTest`
(echte kaart 16:29; faalt zonder de fix met 20 regels, slaagt met 2).
Gecontroleerd in hetzelfde log (geen softwarefout aangetoond): de kortsluiting 24 -> 129 -> 14 (16:28:23) had precies de stand die de kaart
voor deze overgang bewezen kent (5 en 10 afbuigend, 13 rechtdoor; 24->129 achteruit 3x eerder goed gegaan, commando's in de hardwarelog aanwezig).
Oorzaak daarvan niet bewezen. Nog niet getest op de baan.
De kaart van 16:29 is volledig bruikbaar: de 60 verdachte overgangen (telling 1, spiegel van een overgang met telling 38-48) worden bij
hervatten door `VerwijderSpiegelOvergangen` (#98) opgeruimd; de echte data blijft.

## #101 - Loc die al goed staat blijft onzichtbaar bij hervatten (log 16:35: "Melder 13 meldt zich niet bezet (bezet: geen)")
Oorzaak (uit alle hardwarelogs van 10 en 11 oktober: 0 van ~2400 melder-aanvragen werd ooit beantwoord): de Dinamo beantwoordt
`VraagMelderStatusOp` niet; hij meldt alleen wijzigingen. De stand die hij bij het verbinden meldt (hier: melder 13 bezet) werd in de
opstartfase bewust niet doorgegeven (anders onderdrukt de wijzigingscontrole het latere antwoord) en daarna nergens meer gebruikt. Stond de loc
al goed en pakte je hem niet op, dan kwam er nooit een bericht en dacht de verkenner dat er niets bezet was: drie keer "loc terugzetten", daarna gestopt.
Fix: `DinamoHardware` onthoudt de laatst gemelde stand per melder (ook uit de opstartfase) en geeft die via het nieuwe
`IHardwareInterface.HerhaalBekendeMelderStanden()` door; `BepaalStart` roept dat aan direct na de aanvragen. Latere wijzigingen winnen altijd.
Test: `OpstartStandTest` (met de echte frames uit het log). Nog niet getest op de baan; WPF-project niet te compileren hier.

## #102 - Na een opgelost kortsluitpunt meteen verder rijden (Marco 17:01)
Log 15:19: Marco gaf aan dat wissel 1 de kortsluiting bij wissel 2 oplost. Opdracht #5 (13 achteruit met 2, nu ook met 1) werd achteraan de wachtrij gezet en
tot 15:43 niet meer gereden; 23 minuten geen vooruitgang bij melder 12. Nu: (a) een opdracht die mislukte terwijl de gebruiker net de oplossing opgaf
komt vooraan en wordt direct opnieuw gereden (de bekende oplossing, #87, zet wissel 1 er zelf bij); (b) het vervolg "verder voorbij opgelost
kortsluitpunt" (zowel via adrestest als via de oplosfase) komt vooraan de wachtrij. Test: `HerpogingVolgordeTest`. Nog niet getest op de baan.
Gemeten (7 logs, 319 min): de adrestests (87 ritten met 1 adres, 91 min) zijn de grootste tijdpost; groeperen is nog niet gebouwd (voorstel gedaan).

## #103 - Loc steeds met de hand op melder 13 terugzetten + route met een overbodige ronde (Marco 08:07, log 07:58-08:06)
(a) Opdracht #12 (start melder 129): na de mislukte rit (wissel 9 afbuigend gaf kortsluiting in blok 9, 08:01:00) werden de wissels op basis gezet en
meteen aan de gebruiker gevraagd de loc op 13 te zetten. De hardwarelog laat zien dat de kortsluiting 2 s na het terugzetten van wissel 9 al weg was
(08:01:27). Nu: na een mislukte opdracht (en bij de twee plekken in de oplosfase) wacht de verkenner tot het alarm weg is (max. 10 s) en rijdt zelf terug
over de route als de loc op precies één melder van die route staat (`ProbeerZelfTerugNaarStart`). Blijft het alarm, staat de loc andersom, ligt de plek niet
op de route of mislukt de terugrit, dan vraagt hij het zoals voorheen.
(b) De route naar 129 was 13-14, 14-129-24, en dan nog een hele ronde 24-21-...-14-129 (75 s heen, even lang terug), terwijl 129 al op de heenweg gepasseerd werd.
`VerkortRoute`: gesloten rondes in een route vervallen en de route stopt zodra de startmelder eerder voorkomt (rijrichting en wisselstanden blijven zoals ze zijn).
Toegepast bij het plannen van een opdracht en bij het uitvoeren (ook voor opgeslagen opdrachten).
Niet opgelost: 08:04:59 kortsluiting in blok 9 bij het binnenrijden van 129 vanaf melder 4 (wissel 9 stond rechtdoor, moet afbuigend); de rit werd als lus
beëindigd, er is geen kortsluitpunt voor aangemaakt en het alarm bleef tot jij wissel 9 omzette. Dat moet nog uitgezocht.
Tests: `RouteVerkortTest`. Zelf terugrijden zelf is niet gesimuleerd; nog niet getest op de baan.

## #104 - Knop "Voltooide baan nogmaals checken" (Marco 08:26)
Hervatten van een voltooide verkenning verkent niets opnieuw (wachtrij leeg, alle opdrachten staan als afgerond). Nieuw: een controleronde.
`VerkenStatus.VoorControle()` maakt een aparte controlekaart: die begint met de kennis die kortsluitingen voorkomt (wissels, doorgangen, kortsluitoplossingen,
Dinamo-blok per melder) maar zonder overgangen, kopsporen en kortsluitpunten; alles wordt opnieuw gereden. De voltooide kaart wordt NIET gewijzigd.
Aan het eind vergelijkt `ControleVergelijking` beide (ontbrekende/nieuwe overgangen, wissels die anders vastgelegd zijn, doodlopende einden, kortsluitpunten,
adressen zonder effect). Het verslag staat bij de kaart (`ControleVerslagen`), in het logboek, in het rapport (tekst en html) en in een venster.
Een onderbroken controle hervat je met de bestaande hervat-knop. Bij het starten wordt `voortgang_voor_controle_….json` als kopie bewaard.
Beperkingen: Dinamo-blokken en bekende wisselstanden worden in de controle gebruikt, niet opnieuw bewezen; een kaart met meerdere trajecten controleert alleen het
traject van de huidige startmelder.
Test: `ControleRondeTest` (simulatie: geen verschillen, kaart ongewijzigd, verschillen worden gemeld, JSON-rondreis). WPF-deel (knop, vragen, venster) niet
gecompileerd hier en nog niet getest op de baan.

## #105 - Na een kortsluiting vlak voor de startmelder de hele lus achteruit terug (Marco 08:49, controle 08:38-08:47)
Controle-log 08:45:29: adres 2 afbuigend gaf (bekend) kortsluiting na melder 5, de loc stond één melder voor de startmelder 13 van de lus. `TerugNaar` reed de
rit omgekeerd terug: 5-16-15-8-17-25-26-28-27-20-30-29-21-24-14-13, 15 melders, ruim twee minuten, terwijl 5 -> 13 vooruit één melder is.
Nu: `DoorrijdenNaarStart` zoekt de weg vooruit naar de startmelder. Alleen als elke stap eerder met precies de huidige wisselstand is gereden, er nergens een keuze is
(één vervolgmelder), de weg strikt korter is dan terug en de loc nog op de laatste melder van de rit staat. Anders gaat alles zoals voorheen. Wisselstanden gaan
door `MetEisen`. Test: `DoorrijdenTest` (5 gevallen). Niet gesimuleerd als volledige rit; nog niet getest op de baan.
Bevestigd uit het hardwarelog: de loc reed niet "vanzelf", hij kreeg om 08:46:49 het rijcommando van de verkenner (2 s na wissel 2 -> rechtdoor).

## #106 - Wissel 14: de controle veroorzaakte zelf een kortsluiting en kon de loc niet terugrijden (Marco 09:09, log 09:06)
Oorzaak (hardwarelog/log 09:04-09:07): de adrestest zette adres 14 afbuigend tijdens de basisrit, die daarna 17 -> 8 vooruit moest rijden. Volgens de kaart (splitsing
na melder 8 achteruit: rechtdoor naar 17, afbuigend naar 24) vraagt die stap wissel 14 rechtdoor, dus de test veroorzaakte zelf de kortsluiting in blok 4
(begin 09:04:09, weg 2 s nadat 14 weer rechtdoor stond). Daarna zette de partnerzoektocht wissel 13 om en gaf de verkenner op. `ProbeerZelfTerugNaarStart`
zei "melder 8 ligt niet op de route terug" omdat de route van een opdracht die op de startmelder begint leeg is, dus moest Marco de loc zelf op 13 zetten.
Nu: (a) `EisConflictStops`: de rit stopt op de melder vóór een stap waarvan de kaart eist dat het geteste adres rechtdoor staat (hier melder 17); dat telt in
`Vergelijk` als "geen verschil" en veroorzaakt geen kortsluiting. (b) `ZelfTerugWeg`: ligt de loc niet op de route, dan rijdt de verkenner de kortste bekende weg
(overgangen gereden met alle wissels rechtdoor, elke stap in eigen rijrichting) naar de startmelder, met wisselstanden via `MetEisen`.
Test: `Wissel14Test` (stop op 17, geen stop bij ander adres of andere richting, weg 13 -> 8 in 12 stappen, geen weg bij onbekend/alleen-afbuigend).
Gevolg voor de controleronde: adres 14 wordt zo mogelijk niet op die stap opnieuw bewezen; het verslag kan dat als 'niet opnieuw gevonden' tonen. De ritten zelf
zijn niet gesimuleerd; nog niet getest op de baan.
Niet onderzocht: de kortsluiting van 09:06:45 bij opdracht #2 (13 achteruit, na melder 16).

## #107 Wissel 5 stond verkeerd tijdens een verkenningsrit (log 09:22/09:25)
Oorzaak: wissels werden alleen vooraf per rit gezet; de controlerit 4 -> 129 -> 14 achteruit liep van achteren op wissel 5 (rechtdoor) en gaf kortsluiting,
terwijl de kaart (Splitsing, 14 -> 129 afbuigend) dat al wist. Herstel lukte niet en de gebruiker moest "wissel 5 verkeerd" melden.
Oplossing: (A) tijdens verkenningsritten zet de verkenner de staartwissels die de kaart kent zelf bij de volgende melder (`StaartWissels`/`ZetStaartWissels`,
niet het adres dat de rit zelf test); (B) bij kortsluiting schakelt hij een bekende, afwijkende staartwissel van de laatste stap zelf, wacht tot het alarm weg is
en stelt de loc daarna na (`HerstelBekendeWisselstand`). Simulatie: SimulatieBaan haalt de kortsluiting weg als de vastgelopen wissel goed gezet wordt.
Test: `StaartWisselTest`. Nog niet getest op de baan; het WPF-project is hier niet te compileren.

## #108 Kortsluiting bleef na de lus; verkenner gaf op (log 09:31)
Oorzaak: na de lus (melder 129 opnieuw bereikt, 09:29) bleef Dinamo kortsluiting in blok 9 melden terwijl de loc stilstond. `ControleerGeenAlarmVoorStap`
gaf op ("zet geen wissels om"); de gebruiker moest wissel 6 omzetten en toen was het alarm weg. Dat is precies wat de verkenner zelf moet kunnen.
Oplossing: `ProbeerAlarmWegTeKrijgen`: de verkenner schakelt zelf wissels om (eerst die bij de bezette melders, dan de rest; nooit de kruiswissel-motoren 10 en 13),
kijkt 2,5 s of het alarm weggaat, zet een wissel die niet helpt terug, laat een wissel die wel helpt staan en legt dat vast als vondst. Pas als niets helpt volgt de navigatiefout.
Beperking: de gevonden stand wordt (nog) niet als vaste eis in de kaart opgenomen; een volgende rit die alle wissels opnieuw zet kan hem weer terugzetten.
Test: `AlarmVoorStapTest` (uitgebreid). Nog niet getest op de baan; het WPF-project is hier niet te compileren.

## #109 Wissel 5 te laat omgezet op de kleine lus 24 - 129 - 14 (log 11:12)
Oorzaak: de staartwissel-regel van #107 nam aan dat een loc die op melder 24 achteruit staat naar 14 rijdt, dus zette hij wissel 5 op rechtdoor en pas bij melder 129
(loc al bijna bij de tong) op afbuigend. 11:08:36: 0,65 s na dat commando kortsluiting in blok 9; de partnerzoektocht concludeerde daarna ten onrechte dat wissel 3 en 5
'overloopwissels' zijn (de kaart van dat rapport bevat die bevinding niet, maar het log wel).
Oplossing: `StaartWissels` kijkt vooruit (`VerwachteVolgende`): vanaf de melder waar de loc staat volgt de verkenner de bekende splitsing met de huidige wisselstand
(wissel 10 afbuigend op 24 achteruit = naar 129) en zet de wissel die de stap daarna van achteren nodig heeft (wissel 5 afbuigend) meteen, alleen als alle bekende
vervolgen hetzelfde vragen. Is er niets bekend over de vervolgmelders, dan geldt de oude regel.
Test: `StaartWisselTest` (3 nieuwe regels). Nog niet getest op de baan.
Niet opgelost: kortsluiting in blok 3 na melder 14 vooruit (11:12:19, wissel 5 afbuigend, loc daarvoor door Marco neergezet) en 11:09:23 (wissel 4 afbuigend als proef,
bedoelde bevinding). Eerste lijkt op het eerder 'tijdelijke' kortsluitpunt #2 (oplossing wissel 13 afbuigend, botst met #4); oorzaak nog onbekend.

## #110 Wissel 9 omgezet terwijl de loc erop stond (log 11:51 en 11:37, Marco 12:03)
Oorzaak: de partnerzoektocht (en sinds #108 de alarmherstel-proef) zette wissels om terwijl de loc op melder x stond, ook wissels die bij die melder liggen.
11:51:56 en 11:52:09: wissel 9 (punt direct na melder 129) naar afbuigend en terug naar rechtdoor met de loc op 129; daarna kortsluiting en ontsporing.
`WisselsOnderLoc` (BUG #80) kende alleen wissels met een kortsluiting-waarneming bij de melder, niet een gewone splitsing.
Oplossing: `WisselsBijBezet` (elke wissel die de kaart bij een bezette melder noemt, als punt of als tak). De partnerzoektocht en `ProbeerAlarmWegTeKrijgen`
slaan die wissels over en melden dat in het log. Gevolg: een partner die bij melder x ligt (bijv. wissel 5 bij 129) wordt niet meer automatisch bewezen; de verkenner
vraagt dan zo nodig de gebruiker. Ritten die met de loc op de startmelder hun wissels zetten (de normale gang) zijn niet veranderd.
Test: `StaartWisselTest` (2 regels). Nog niet getest op de baan.
Open: of wissel 9 op 11:51:56 of op 11:52:09 de ontsporing veroorzaakte is uit het log niet af te leiden; beide gevallen zijn nu afgedekt.

## #111 Melden als Dinamo niet meer antwoordt (Marco 12:17)
Tot nu toe stond de stilte (geen enkel bericht, ook geen keepalive, sinds 2 s) alleen in de hardwarelog en pas bij een kortsluitmelding in het verkenner-log.
Nu: `IVerbindingsBron.StilteGewijzigd` (Dinamo vuurt af bij begin en einde van de stilte); de verkenner zet direct een waarschuwing in zijn logboek
("DINAMO ANTWOORDT NIET MEER ...") en een melding als Dinamo weer antwoordt (`DinamoStil`). De verkenner stopt niet zelf: stopcommando's komen bij een stille Dinamo toch niet aan.
Test: `DinamoStilteTest` (2 regels). Nog niet getest op de baan (de echte Dinamo-koppeling is hier niet te draaien); het WPF-project is niet gecompileerd.
Mogelijke uitbreiding: een pop-up in de WPF-app; de waarschuwing staat nu in het logboek.

## #112 Controleresultaat niet op te slaan of af te drukken; "55 punten verschil" verkeerd gelezen (Marco 13:27)
Het resultaat stond alleen in een MessageBox (max. 12 regels, niet te selecteren of op te slaan). Het rapport van 12:32 zegt: 25 punten om te bekijken, 55 onderdelen kwamen overeen;
de oude zin "25 punt(en) om te bekijken; 55 onderdelen ... kwamen overeen" las als 55 verschillen.
Oplossing: de zin is nu "Controle: N verschil(len) om te bekijken; daarnaast kwamen M onderdelen wel overeen met de kaart." Na de controle opent een eigen venster (`ToonControleVerslag`)
met ALLE regels genummerd en de knoppen Opslaan als tekst, Opslaan als webpagina, Kopiëren, Afdrukken en Sluiten. `Rapport.ControleAlsTekst/ControleAlsHtml` maken de losse tekst.
Het volledige verslag stond en staat ook in 'Rapport opslaan' (tekst/html) onder "Controles van de baan".
Test: controle-test in CompileCheck (2 regels, tekst/html). Het WPF-venster en het afdrukken zijn NIET getest en niet gecompileerd (hier niet mogelijk); nog niet getest op de baan.
Let op: de 25 verschillen van 12:32 komen uit een rit vóór #109/#110; een deel kan door de toenmalige wisselfouten komen. Stuur bij een nieuwe controle het verslag.

## #113 Wissel 2 werd tijdens de controle verkeerd gezet: loc reed 5 -> 16 in plaats van 5 -> 12 (Marco 13:46, log 12:31)
Opdracht #12 (14 achteruit, afbuigend 1, 2, 11) rijdt 13 -> 5 -> 12 -> 11 -> 4 -> 129 -> 14. De stand werd gecorrigeerd met "2 rechtdoor (kortsluitpunt #4 ... kan op dit pad niet)",
terwijl 5 -> 12 wissel 2 afbuigend nodig heeft. Wissel 2 heeft in de kaart geen splitsing (alleen "kortsluiting bij omzetten"), dus de eisen van het pad kenden hem niet en de
bekende oplossing 'wissel 2 rechtdoor' (hoort bij 5 -> 16) werd ook op dit pad toegepast. Gevolg: de loc reed op 12:31:34 en 12:32:02 naar melder 16 (onverwacht bezet) met wissel 1
afbuigend, kortsluiting, de verkenner reed zelf terug en gaf het op.
Oplossing: `Baankaart.AltijdAfbuigendOpPad`: een stap die minstens 2x is gereden en waarbij steeds dezelfde wissels afbuigend stonden (5 -> 12: altijd 2) beschermt die wissels;
`MetBekendeKortsluitOplossingen` zet ze niet meer rechtdoor (noch via een oplossing, noch via de botsregel). Het wordt nooit gebruikt om een wissel om te zetten.
Test: `AltijdAfbuigendTest` (4 regels). Nog niet getest op de baan; het WPF-project is niet gecompileerd.
Open: (1) de controle hervatte op 12:07 vanaf opgeslagen voortgang van eerdere mislukte ritten (14 opdrachten): daarin zitten onjuiste bevindingen (wissel 3 'afbuigend' op het pad 13-14-24-21
gaf de kortsluiting van 12:08:07; wissel 11 -> 138; wissel 1/3 als 'nieuw'). Start de controle opnieuw vanaf nul in plaats van te hervatten. (2) Wat precies "wissel 2 afbuigend" tegen de
kortsluiting aan was, is uit het log niet met zekerheid vast te stellen: het log laat 12:31 wissel 2 juist rechtdoor zien. Zie ook de antwoordtekst.

## #114 Controle kon niet opnieuw gestart worden (Marco 13:52)
'Baan checken' meldde bij een onderbroken controle alleen "hervat die met de knop Vorige verkenning hervatten". Een controle met onjuiste bevindingen uit eerdere mislukte ritten (zie #113)
kon daardoor niet worden weggegooid. Nu vraagt de knop: Ja = opnieuw beginnen (de onderbroken controle wordt vervangen door een schone controlekaart uit de voltooide kaart, die ongewijzigd blijft;
een kopie van de voortgang wordt bewaard), Nee = niets doen en later hervatten.
Test: controle-test (1 regel, `VoorControle` opnieuw). Het WPF-vraagvenster is niet getest en niet gecompileerd; nog niet getest op de baan.

## #115 Proefrit reed tegen een bekende kortsluiting aan (wissel 2) en zette wissel 10 terug met de loc op de kruiswissel (Marco 15:04, log 13:56-15:03)
Dit was de schone controle (0 opdrachten afgerond), dus #113/#114 werkten; twee andere fouten:
1. 14:02:48: de adrestest van wissel 2 afbuigend (rit vooruit vanaf 13) reed de hele lus en kreeg bij melder 5 de kortsluiting die de kaart al kent
   ("wissel 2: afbuigend = kortsluiting na melder 5, van achteren bereden"). De proefstops kenden alleen openstaande kortsluitpunten, niet de waarneming van de wissel zelf.
2. 14:37:27: de adrestest van wissel 10 afbuigend stopte op melder 24 (eisconflict, #106) en zette wissel 10 direct terug terwijl de loc over 24/21 de kruiswissel
   op stond; 2 s later kortsluiting in blok 8 die niet meer wegging (ook niet na het omzetten van alle andere wissels, #108) tot Marco de loc zelf corrigeerde.
Oplossing: (1) `BekendeOmzetKortsluitStops`: de adrestest stopt op de melder waarna dit adres afbuigend al kortsluiting gaf (zelfde richting en basisstand).
(2) `VeiligeStops`: ligt de wissel volgens de kaart direct na de stopmelder (waarneming met die NaMelder), dan stopt de rit op de melder ervoor, zodat de wissel zonder loc erop
wordt teruggezet. Dit geldt voor alle stops van de adrestest (ook #106 en openstaande kortsluitpunten).
Test: `ProefStopTest` (5 regels). Nog niet getest op de baan; het WPF-project is niet gecompileerd.
Gevolg: bij het zelfde proefadres wordt de kortsluiting niet opnieuw 'bewezen'; de bekende waarneming blijft in de kaart staan. De controle meldt zo'n wissel mogelijk als "niet opnieuw vastgesteld".
Open: of de loc bij stoppen op de voorlaatste melder altijd ver genoeg van de tong staat (bij een lange loc op een kort blok) is niet getest.

## #116 Overloopwissels ontbraken in de wisselstraatvoorstellen en dus in de import (Marco 15:56)
Het rapport gaf voor 5 -> 12 alleen "wissel 2 afbuigend (aangenomen)", terwijl de kaart weet dat wissel 1 mee afbuigend moet staan (overloopwissel die de kortsluiting oplost).
Oplossing: `Wisselstraten.VoegOverloopToe`: een bekende kortsluitoplossing (zelfde richting, NaMelder is begin of eind van de overgang) wordt als extra eis `EisBron.Overloop`
toegevoegd ("overloopwissel, bewezen") als alle afbuigende wissels van de configuratie al in de eisen staan en het oplossingsadres er nog niet in zit.
`WisselstraatImporter` neemt alle `v.Eisen` over, dus de overloopwissels komen daarmee in de ingelezen wisselstraten; alleen `Aangenomen` blijft een waarschuwing.
Test: `OverloopStraatTest` (4 regels: 5 -> 12 geeft wissel 2 én wissel 1 afbuigend). Nog niet getest op de baan; het WPF-project is niet gecompileerd.
Let op: de voorstellen worden bij het exporteren van de baankaart berekend; exporteer de kaart opnieuw met deze versie en importeer die.

## #117 Proefrit reed weer in de bekende kortsluiting na melder 12; de oplossing werd direct teruggezet en de loc zat 12 minuten vast (Marco 16:12, log 15:58-16:11)
Verloop: 15:58:16 adrestest wissel 2 afbuigend, rit achteruit vanaf 13 -> 5 -> 12; kortsluiting in blok 2 (kortsluitpunt #1, al opgelost met wissel 1 afbuigend).
De alarmroutine (#108) zette wissel 1 afbuigend: alarm weg (15:58:32.130), melding "de wissel blijft zo staan". Maar de terugweg zette alle wissels meteen weer in de
stand van de route (wissel 1 rechtdoor, 15:58:32.330): na 0,8 s was de kortsluiting terug (15:58:32.931), de loc vertrok niet meer, de blokproef (9 blokken x 2 richtingen
x 30 s) duurde 11 minuten, tot Marco de loc zelf op melder 13 zette.
Drie oorzaken, drie fouten:
1. De proefrit van een adres kent alleen de stops voor open kortsluitpunten en omzet-waarnemingen in dezelfde richting; een al OPGELOST kortsluitpunt met bekende oplossing werd niet gebruikt.
   Nu: `ProefStandMetOplossing`: is bij precies deze stand (basis + proefadres) en startmelder/richting al een kortsluiting opgelost met een extra wissel, dan rijdt de proef met die wissel
   (hier: wissel 2 én 1 afbuigend). Daarna wordt het extra adres weer in zijn oude stand gezet. Kruiswissel-motoren 13 en 10 doen hier niet aan mee (hun stand staat in de referentie).
2. De stand die het alarm wegnam gold maar voor één moment. Nu: `_alarmStand` wordt gevuld door `ProbeerAlarmWegTeKrijgen` en `MetAlarmStand` past hem toe op de stap die er direct op volgt
   (rit, navigatie, terugweg, doorrijden); bij de eerstvolgende alarmcontrole zonder alarm vervalt hij.
3. De blokproef werd niet overgeslagen (#66) omdat `_laatsteKortsluitingOm` alleen bij kortsluiting TIJDENS een rit werd gezet. Nu ook bij een alarm voor een stap, en bij een actief alarm
   wordt de blokproef altijd overgeslagen (dan ligt het niet aan de blokkoppeling).
Test: `ProefOplossingTest` (6 regels). Nog niet getest op de baan; het WPF-project is niet gecompileerd.
Gevolg voor de kaart: de proef van wissel 2 achteruit kan nu eindelijk de splitsing van wissel 2 na melder 5 (achteruit: rechtdoor 16, afbuigend 12) vastleggen, die tot nu toe ontbrak.
Open: bij het terugzetten van het extra adres (wissel 1) na de proef kan de loc nog dicht bij die wissel staan (niet getest).

## #118 Proefritten rekenden zonder de extra wissel van de rijstand en reden in bekende omzet-kortsluitingen (Marco 16:34, log 16:12-16:25, versie vóór #117)
Opdracht #3 ('afbuigend: 5') rijdt met een bekende extra wissel (10 afbuigend, kortsluitpunt #3): rijstand 5+10. De adrestest gebruikte voor de proefritten de stand van de opdracht
ZONDER die extra wissel (`o.Configuratie.Met(a)`), terwijl wissel 10 fysiek afbuigend bleef staan. Gevolgen:
1. 16:18:34 proef wissel 2 afbuigend: de kaart kent 'wissel 2 afbuigend = kortsluiting na melder 5' (waargenomen met alles rechtdoor). De #115-stop eiste exact dezelfde basisstand ('5' of '5,10'
   is niet 'alles rechtdoor'), herkende niets en de loc reed tegen de kortsluiting aan (loc half op 16 en 5, 90 s herstel).
2. 16:24:38 proef wissel 6 afbuigend: waargenomen bij '5, 10'; de proef rekende met '5' -> geen stop, kortsluiting in blok 9 met de loc op melder 129 waar wissel 6 ligt; de partnerzoektocht
   mocht (terecht) wissel 9 en 10 niet omzetten, geen enkele andere wissel hielp, de loc zat vast tot Marco 16:32 wissel 5 en 6 zelf corrigeerde (9 minuten).
Oplossing: (1) de adrestest gaat nu uit van de werkelijke rijstand (stops, proefstand, terugweg, extra wissel terugzetten en partnerzoektocht). (2) `ConfiguratieTekstIsDeelVan`: een omzet-kortsluiting die
met wissels K afbuigend is waargenomen, geldt ook als er elders nog andere wissels afbuigend staan (alle wissels van K moeten wel afbuigend staan). (3) Valt de stop op de startmelder zelf
(de wissel ligt direct na de startmelder, bv. wissel 6 na 129 bij start 14), dan zou de rit er dwars doorheen rijden; dat adres wordt in die opdracht overgeslagen met een melding.
Test: `proefstop (#118)` (4 regels, naast de aangepaste #115-regel). Nog niet getest op de baan; het WPF-project is niet gecompileerd.
Gevolg: wissel 6 wordt in een opdracht die bij melder 14 begint niet getest (komt op een andere opdracht/startmelder aan de beurt). Open: de terugweg na een afgebroken proef reed 14 melders achteruit
(85 s) omdat 5 -> 13 -> 14 niet eerder met precies stand '5' is gereden; werkt wel, maar traag.

## #119 Kruiswissel (motor 10) omgezet terwijl de loc er nog half op stond (Marco 17:14, log 17:11:52 en 17:13:41)
Oorzaak: de opdracht begon op melder 14 (vlak naast de kruiswissel in melder 24). Motor 10 (en 5) werden pas omgezet NADAT de loc op 14 aangekomen was
(`ZetConfiguratie(rijstand)` na `NaarStartVan`, en bij opdracht #5 voor de etappe 14 -> 129 -> 24); de loc reikte dan nog tot in de kruiswissel en ontspoorde
(kortsluiting blok 3, daarna dezelfde fout bij de herhaling). #95/#96/#98 dekten alleen het geval dat de etappe zelf op melder 24 eindigt of de loc op 24 staat.
Fix: `KruisVoorafStand` (BaanVerkenner.cs): motor 13 en 10 worden al gezet in de etappe VOOR de loc de kruiswissel-omgeving bereikt (laatste etappe van de aanloop:
standen van de rijstand; tussenetappe: standen van de volgende etappe), zolang de huidige etappe zelf niet via melder 24 rijdt.
Test: `KruisVoorafTest` (4 regels). Nog niet getest op de baan; het WPF-project is niet gecompileerd.
Open: staat de loc bij de start van een opdracht al op 14 (geen aanloop), dan kan de stand nog onder de loc omgezet worden.

## #120 Hardware-communicatie op het scherm toonde alleen In-berichten (Marco 17:16)
Oorzaak: de Dinamo stuurt 5x per seconde een In-statusbericht; de lijst (3000 regels) liep daarmee vol en scrolde niet mee, zodat de zeldzame Uit-berichten niet
te zien waren. In het hardwarelog-bestand staan ze wel (bv. 17:11:46 en 17:11:49). Fix: herhaalde In-statusberichten worden standaard verborgen
(keuzevak bovenaan het tabblad) en de lijst scrolt mee. Nog niet getest op de baan; het WPF-project is niet gecompileerd.

## #121 Terugweg, proefritten en opdrachtstand liepen uiteen (Marco 18:19, log 17:23-18:18, controleronde; ook de oorzaak van 17:01 en 17:11)
Gemeenschappelijke oorzaak: op meerdere plekken werd de NOMINALE stand van de opdracht gebruikt in plaats van de werkelijke rijstand (opdracht + bekende extra wissels),
of de stand na een terugweg werd niet hersteld voordat de volgende proef begon. Gevonden in dit log:
1. Basisrit (18:16:37, ook 17:01:58): `TerugNaarMetControle(o, rit, o.Configuratie)` gaf de terugweg de stand 'afbuigend: 2' terwijl de rit met 'afbuigend: 1, 2'
   gereden was. Wissel 1 ging rechtdoor terwijl de loc op melder 12 (met de staart op 5) stond: kortsluiting, ontsporing, NavigatieFout. Een tweede basisrit
   begon bovendien met de verkeerde stand. Fix: terugweg met de rijstand van de basisrit.
2. Bekende oplossing werd gemist (18:14:39): de oplossing van kortsluitpunt #1 (wissel 1 bij wissel 2 afbuigend) werd alleen toegepast als de opdracht op dezelfde
   startmelder (13) begon; opdracht #4 begon op melder 5 (verderop dezelfde weg) en kreeg wissel 1 niet. Fix: `Baankaart.BereiktMelder`; de oplossing geldt ook
   als de loc volgens de kaart (stappen met deze stand eerder gereden, zonder keuze) vanaf de startmelder de melder van de kortsluiting bereikt.
3. Valse vondsten (18:11:42-18:12:55): de terugweg naar startmelder 24 zette motor 13 rechtdoor (oude 'oplossing' van kortsluitpunt #4); op de kruiswissel zelf kan
   hij daarna niet meer om. De proefritten heetten 'afbuigend: 6, 13, 14' maar reden met 13 rechtdoor en kwamen op melder 21 uit: de verkenner legde wissel 6, 7,
   8 en 9 vast als splitsing na melder 24 (onjuist) en plande er vier opdrachten voor. Fixes: (a) eindigt de terugweg op melder 24, dan krijgen motor 13 en 10
   vóór het inrijden de stand van de opdracht (`KruisStandBijTerugweg`, via de referentie); (b) vóór elke proef wordt de werkelijke stand gelijk gemaakt aan de
   rijstand (`HerstelRijstand`; wissels bij een bezette melder blijven staan); kan een kruiswisselmotor niet omdat de loc op melder 24 staat, dan wordt dat adres
   niet getest en staat dat in het log, in plaats van een valse vondst.
4. Adres 10 testen met de loc op melder 24 (18:12:55) gaf altijd kortsluiting die bleef staan. Motor 13 en 10 worden vanaf een opdracht op melder 24 niet meer getest.
Tests: `oplossing (#121)` (4 regels), `kruisterugweg (#121)` (1 regel). Nog niet getest op de baan; het WPF-project is niet gecompileerd.
Open: (a) opdracht #4 zette wissel 2 om terwijl de loc op melder 5 stond; de waarschuwing 'wissel 2 ligt bij melder 5' was hier misleidend (de echte oorzaak was het
ontbrekende wissel 1, zie 2), maar het omzetten van de opdrachtwissel direct vóór de loc is niet vooraf gedaan (alleen de kruiswissel, #119); (b) de oude
'oplossing' van kortsluitpunt #4 (13 afbuigend bij 24 vooruit) is in deze controle ronde met een goede rit tegengesproken (WISSEL 13 gevonden 17:44) maar wordt nog
niet automatisch ingetrokken; (c) het wissel-6-verhaal van 17:24 is nog niet te verklaren zonder log van dat moment.

## #122 Controle 18:36 nagekeken: wissels bij de startmelder te laat gezet, onterechte 'oplossingen' (Marco 18:38)
Bevindingen uit de volledige controleronde (17:23-18:36, 35 punten):
1. Opdracht #4 (melder 5 achteruit, wissel 2 afbuigend, 18:18): wissel 1 en 2 liggen bij startmelder 5 en werden omgezet toen de loc al op 5 stond: meteen
   kortsluiting in blok 3. Dezelfde rit slaagde om 17:50 doordat de wissels gezet waren terwijl de loc nog op melder 13 stond. Gevolg: onterecht kortsluitpunt #2/#5
   en een onterechte 'oplossing' (de handmatige 'wissel 2 rechtdoor' van Marco was alleen de geteste wissel terugzetten). Dat verklaart ook de melding 'wissel 2 ligt bij melder 5'.
   Fix: `StartmelderWisselsVooraf` zet in de laatste etappe van `NaarStartVan` alle wissels die de kaart bij de aankomstmelder noemt al op hun rijstand, behalve
   wissels bij een melder waar de loc zelf nog op staat, wissels die de route zelf eist en de kruiswissel-motoren (die hebben hun eigen regels, #95/#119).
2. Rijstand 18:18 had alleen wissel 1; om 17:50 was het een keten (2 -> 1+2 -> 1+2+5...). Fix: `MetBekendeOplossingenVoorStart` herhaalt tot niets meer verandert (max. 5 rondes).
3. Een handmatige correctie waarbij de gebruiker een wissel TERUGZET die zelf in de geteste stand zit, of een wissel op rechtdoor zet, wordt niet meer als
   oplossing van het kortsluitpunt vastgelegd (`RegistreerHandmatigeOplossing`). Bestaande opgeslagen oplossingen (kortsluitpunt #4 en #5 in de kaart van 18:36) blijven staan:
   tests (#100/#112) leunen op het bestaande gedrag; zie open punten.
4. Valse vondsten in de kaart/controle (wissel 6, 7, 8, 9 'na melder 24' en de opdrachten #5-#8, 'Adressen zonder gevonden effect' 3,4,5,10,11,12,14 tegenover 3,4,7,8,11,12)
   komen uit de adrestest van 18:11 (build vóór #121: motor 13 stond fout). Voor wissel 13 (24 vooruit: rechtdoor -> 21, afbuigend -> 8) en wissel 2 (5 achteruit:
   rechtdoor -> 16, afbuigend -> 12) wijst alles op echte vondsten. Dit is met #121 (HerstelRijstand) al afgevangen; een nieuwe controleronde met de nieuwe build moet dit bevestigen.
5. 18 overgangen in het gebied 129/blok 1-4 zijn in deze controle niet gereden doordat opdrachten werden overgeslagen na aanhoudende kortsluiting
   (14 vooruit afb 5, 24 achteruit afb 10, 129 vooruit afb 5/9/10, 24 achteruit afb 5/6/10). Waarschijnlijk dezelfde wissel-onder-de-loc-oorzaak; niet los opgelost.
Tests: `StartmelderVoorafTest` (5 regels: vooraf zetten, wissel bij loc blijft staan, wissel onder loc, terugzetten is geen oplossing, keten 2 -> 1+2 -> 1+2+5).
Nog niet getest op de baan; het WPF-project is niet gecompileerd.
Open: (a) oude 'oplossingen' kortsluitpunt #4 (24 vooruit, 13 rechtdoor) en #5 (5 achteruit, 2 rechtdoor) zijn in de kaart nog aanwezig en worden niet ingetrokken;
(b) staat de loc bij de start al op de startmelder (geen aanloop), dan kunnen opdrachtwissels nog onder de loc omgezet worden; (c) wissel 6 van 17:24 is nog niet te verklaren.

## #123 Controle 14-10 (08:07-09:00) nagekeken: twee kortsluitingen na wisselschakeling op melder 13 (Marco 09:05)
Resultaat van de controleronde met de #122-build: geen valse vondsten meer (wissel 2 en 13 worden opnieuw bevestigd, 6/7/8/9 niet meer als splitsing na 24); opdracht #3
(24 vooruit, 13 afbuigend + 14) en #4 (5 achteruit, 2 afbuigend + 1) liepen uiteindelijk goed; kortsluitpunten 12 en 5 (achteruit) kwamen niet meer voor; de kruiswissel
kreeg motor 13 vóór het inrijden (geen blijvende kortsluiting in blok 8 meer, zoals op 12-10 18:07-18:13).
Open: 08:52:55 (opdracht #3) en 08:53:15 (opdracht #4): kortsluiting 2 s na het omzetten van wissel 14 terwijl de loc op melder 13 stond en 0,5-1,5 s later wegreed. Wissel 14 staat
in de kaart alleen bij melder 8 (het #93-mechanisme gaf dus geen extra wachttijd). Dezelfde commando's om 08:53:36 gaven geen kortsluiting. Fix: na elk wisselcommando terwijl de loc
op een melder staat wacht de verkenner altijd extra (1,5 s als de kaart de wissel niet bij die melder noemt; 2 s als dat wel zo is: `ExtraWisselWachtOnbekendMs`).
Niet te testen zonder baan. Nog niet getest op de baan; het WPF-project is niet gecompileerd. De oorzaak is een hypothese (tong nog onderweg); komt het terug, dan graag noteren waar wissel 14 fysiek ligt.
Uitleg 'Adressen zonder gevonden effect' (nu 1,3,...,14): geen fout, in een controle zijn alleen wissel 2 en 13 opnieuw gevonden; de rest telt dan als 'geen effect'.
Open: de 18 overgangen in het gebied 129/blok 1-4 en de wissels 5, 6, 9, 10, 14 worden door een controle niet opnieuw gereden, omdat de controle alleen opnieuw ontdekt wat ze
via 'start vooruit/achteruit' tegenkomt. Oplossing vraagt een routeplanner uit de kaart voor opdrachten vanuit de kaart; nog niet gebouwd (wacht op akkoord van Marco).
Opmerking opdracht #4: de wissels 1 en 2 worden hier nog NIET vooraf gezet (wissel 2 hangt in de kaart ook aan melder 13, wissel 1 is niet aan een melder gekoppeld); ze werden om 08:57:29
op melder 5 omgezet en dat ging nu goed dankzij de wachttijd.

## #124 Voorbereiding op een volledig nieuwe verkenning (Marco 09:22)
1. Vooraf zetten bij de startmelder (#122) was te voorzichtig: opdracht #4 van 14-10 zette wissel 1 en 2 pas op melder 5 om (08:57:29), omdat wissel 2 in de kaart ook aan melder 13 hangt
   (vervolgmelder) en wissel 1 nog niet aan een melder gekoppeld is. Nu telt alleen een wissel die de kaart ACHTER een melder noemt waar de loc nog staat of langskomt (NaMelder)
   als 'onder de loc'; wissels die alleen als vervolgmelder aan die melder hangen en wissels waar de kaart nog niets van weet (maar die de rijstand afbuigend wil) worden vooraf gezet.
2. Het leerprofiel nam 'oplossingen' over waarbij de oplossende wissel zelf in de geteste stand zat (kortsluitpunt #4: 13 afbuigend -> 13 rechtdoor; #5: 2 afbuigend -> 2 rechtdoor).
   Die ontstonden uit wissels die onder de loc omgezet werden (zie #122) en zouden een nieuwe verkenning meteen verkeerd sturen (13 rechtdoor bij 24 vooruit, terwijl 13 afbuigend 24 -> 8
   bewezen is). Ze worden niet meer in het leerprofiel bewaard en niet meer uit een bestaand leerprofiel overgenomen.
Tests: `startmelder vooraf (#124)`, `leerprofiel (#124)` in `StartmelderVoorafTest`. Nog niet getest op de baan; het WPF-project is niet gecompileerd.
Open: de controle rijdt het gebied rond 129 niet opnieuw (zie #123); oorzaak van de twee kortsluitingen na wissel 14 op melder 13 blijft een hypothese.

## #125 Dinamo reageert niet meer, zonder melding (Marco 09:46, log 09:10-09:43)
Het log van 09:10-09:41 is schoon (geen enkele kortsluiting in 30 minuten, 12 adressen getest, wisselschakelingen vooraf/wachttijden werkten). Om 09:41:59 vertrok de loc niet meer van
melder 13; Marco zag dat de wissels niet meer schakelden en onderbrak de netspanning van Dinamo, zonder melding. Oorzaak: de interface bleef berichten (keepalives) sturen, dus de
stiltemelding (#81, 'geen enkel bericht') ging niet af; de verkenner zag alleen 'loc vertrok niet' en startte een blokproef (blok 3 telkens opnieuw, minuten lang) die niets kan vinden.
Fix: vertrekt de loc niet van een melder waar hij in deze kaart al eerder wel van vertrok (`VertrekWasBewezen`: bekende overgang vanaf die melder in die richting), dan is de blokkoppeling
bewezen. De verkenner meldt dan 'DINAMO REAGEERT WAARSCHIJNLIJK NIET OP COMMANDO'S' in het logboek, toont een pop-up (Ja = hersteld, alle wissels opnieuw sturen en de rit hervatten;
Nee = toch blokproef; sluiten = stoppen) en slaat de blokproef over.
Beperking: of Dinamo de WISSELcommando's uitvoert, meldt de centrale niet terug; de herkenning werkt dus pas bij de eerstvolgende rit waarbij de loc niet vertrekt. Nog niet getest op de baan;
het WPF-project is niet gecompileerd (de pop-up gebruikt de bestaande VraagJaNee van het hoofdvenster). Test `dinamo stil (#125)`.
Open: een directe controle 'reageert Dinamo?' (bijv. de baanspanning of een antwoord op elk commando) is niet mogelijk zonder meer protocolkennis.

## #126 Dinamo voert wissel-commando's niet meer uit, verkenner merkt het niet (Marco 10:23/10:24, logs 0904, 0943, 1022)
Dinamo meldt elk UITGEVOERD wissel-commando terug in een statusbericht met dezelfde twee databytes (commando `4B 94 81 8C` -> terugmelding `4A 94 81 A1`; data1 = 0b001xxxxx, dus bit 6 en 5 nul, bit 4 één).
Gemeten in de hardwarelogs: log 1022 meldde elk commando terug tot 10:19:35; vanaf 10:19:38 geen enkele meer (32 commando's lang). Daarna 'ontdekte' de verkenner wissels 3, 4, 5, 6 en maakte opdracht #5-#8,
omdat de wissels niet bewogen (dat verklaart ook 'wissels waar hij niet in de buurt rijdt' en de loc die heen en weer reed tussen blok 3 en 2). Log 0943: de hele sessie geen enkele terugmelding.
Log 0904, tot nu toe als 'schoon' beoordeeld: de terugmeldingen stopten om 08:47:23 en kwamen tot het einde (09:00) niet meer terug (25 commando's). De twee 'onverklaarde' kortsluitingen van
08:52:55 en 08:53:15 (wissel 14, #123) vielen daar middenin; het is dus waarschijnlijker dat de wissel toen niet omging dan dat de tong 'nog onderweg' was. Wat de kaart van 0904 na 08:47:23 aan
wissel-bevindingen bevat (vooral wissels 8 t/m 13) is daarom niet betrouwbaar.
Eerdere logs (3, 4, 10, 11 oktober) hebben vaak lange reeksen zonder terugmelding; de terugmelding bestaat dus niet altijd of is toen niet vastgelegd.
Fix: `DinamoHardware` telt de terugmeldingen (`IWisselBevestiging.AantalWisselBevestigingen`). `BaanVerkenner.ZetWissel` wacht na elk commando tot 1,0 s op een terugmelding (`WachtOpBevestiging`/`VerwerkBevestiging`).
Na 3 commando's zonder terugmelding (eerder in de sessie wel gezien) of 4 (nog nooit gezien) staat 'DINAMO VOERT WISSELCOMMANDO'S NIET UIT' in het logboek en in de kaartwaarschuwingen, en verschijnt
een pop-up: Ja = Dinamo hersteld (alle wissels opnieuw sturen, blijft het stil dan stopt de verkenning), Nee = de wissels gaan wel om (controle uit voor de rest van de sessie), sluiten = stoppen.
Zonder pop-upfunctie stopt de verkenning. Tests: `echo (#126)` (10 controles). Gezonde Dinamo geeft nooit een melding.
Nog niet getest op de baan; het WPF-project is niet gecompileerd. Kleine bijwerking: elk wisselcommando duurt tot ~0,4 s langer (wachten op de terugmelding).
Open: bevindingen van de paar commando's tot het alarm (maximaal 3) worden niet teruggedraaid; een rit die al liep gaat na 'Ja' gewoon door. Wat Dinamo doet uitvallen is onbekend (0904 na ~39 min, 1022
na ~2 u 10 min; wel bijzonder dat de stroom van de interface bleef doorgaan). Notitie bij #125: de beperking 'Dinamo meldt niet terug' gold niet; #126 gebruikt die terugmelding wel.

Vervolg #126 (Marco 10:35, log 1033: netspanning onderbroken, melding kwam, maar traag): in log 1033 kostte elk commando zonder terugmelding 3,6 s (10:33:23 -> 10:33:27), omdat de extra wachttijden
van #93/#123 (1,5-2 s, tong laten uitzwaaien) ook doorliepen terwijl er niets beweegt, plus 1,5 s wachten op de terugmelding. Nu: eerst 1,0 s op de terugmelding wachten, en blijft die uit dan vervalt de
extra wachttijd. Zonder eerdere terugmelding in de sessie is het alarm na 4 commando's (was 6): ongeveer 4 x 1,4 s i.p.v. 6 x 3,6 s. Test aangepast. Nog niet getest op de baan; het WPF-project is niet gecompileerd.

## #127 Melder 14 blijft 'bezet' bij elke nieuwe verkenning, ook na Dinamo herstarten (Marco 10:40)
Oorzaak: de Dinamo meldt alleen WIJZIGINGEN en nooit welke melders vrij zijn; bij een aanvraag antwoordt hij niet (#101). `DinamoHardware` onthoudt daarom de laatst gemelde stand per melder
(`_bekendeStand`) en `BepaalStart` geeft die bij elke nieuwe verkenning door als 'bezet'. De hardware blijft verbonden zolang je in de Baanverkenner niet opnieuw op Verbinden klikt, dus dat
geheugen overleeft meerdere verkenningen. Is een 'vrij'-melding gemist (Dinamo herstart of de verbinding viel stil terwijl melder 14 bezet was), dan blijft 14 voor altijd bezet. Het venster
'Meerdere melders bezet' hielp niet: OK vroeg alleen opnieuw dezelfde onthouden stand door. Dat sluit aan op de waarneming van Marco: de loc staat alleen op 13, het programma meldt 13 en 14.
In log 1033 is dit zelf nog niet te zien (daar kwam 14 pas bezet door de blokproef); de aangeleverde bestanden zijn dezelfde als die van 10:35 en bevatten de herstarte verkenning niet.
Fix: nieuwe `IHardwareInterface.WisMelderGeheugen(melders)` (Dinamo: wist de onthouden stand en meldt de gewiste bezette melders als vrij). Aangeroepen (1) na OK in 'Meerdere melders bezet'
voor precies die melders, zodat een spookmelder verdwijnt; (2) automatisch voor alle melders zodra Dinamo stil valt (`ControleerStilte`). Het logboek toont nu bij de start
'Bezet bij de start volgens Dinamo (laatst gemelde stand): ...'. Test `MelderGeheugenWisTest` (5 controles).
Let op: staat de loc echt op 13 en wordt 13 ook gewist, dan komt er geen nieuwe melding tot de loc beweegt; de verkenner zoekt de loc dan zelf (bestaand pad 'Geen bezette melder bekend').
Nog niet getest op de baan; het WPF-project is niet gecompileerd.
Open: het leerprofiel (94 overgangen, 13 kortsluitoplossingen uit eerdere verkenningen) wordt ook bij een nieuwe verkenning overgenomen; Marco zegt dat niets van eerdere verkenningen onthouden mag
worden. Dat is een bewuste eerdere keuze (#122/#124); wacht op bevestiging wat er precies uit moet.

## #128 Niets uit eerdere verkenningen meenemen zonder bevestiging (Marco 10:41/10:48)
Marco: bij een nieuwe verkenning mag niets van eerdere verkenningen onthouden worden; het is de bedoeling alles vanaf nul te leren om te controleren of fouten echt opgelost zijn.
Het leerprofiel (reistijden, blokhints, kortsluitoplossingen) werd tot nu toe stilzwijgend gebruikt (log 1033: '13 kortsluitoplossing(en) uit eerdere verkenningen overgenomen', 'Leerprofiel: 94 overgangen').
Nu: bij het starten van een nieuwe verkenning (niet bij hervatten) verschijnt een pop-up 'Gegevens van eerdere verkenningen gebruiken?' met de aantallen; Ja = gebruiken, Nee of venster sluiten of
geen pop-upfunctie = alles vanaf nul (standaard). Het profiel wordt in beide gevallen wel verder bijgewerkt voor later. Bij hervatten van een onderbroken verkenning wordt niets extra overgenomen
(de kaart bevat al wat die verkenning zelf leerde). De Koploper-referentie (#84) is geen eerdere verkenning en blijft ongemoeid. Test `VoorkennisVraagTest` (Ja, Nee, gesloten, geen functie).
Gevolg: zonder voorkennis ziet een nieuwe verkenning ook de bekende kortsluitingen weer opnieuw (bv. wissel 1/13/14 bij bepaalde ritten); dat is de bedoeling om te kunnen controleren.
Nog niet getest op de baan; het WPF-project is niet gecompileerd.

## #129 Engelse wissels worden bij de start opgegeven; geen vaste kruiswissel meer in een nieuwe verkenning (Marco 11:08/11:10)
Marco zag in een verkenning vanaf nul (log 1106) dat de verkenner al wist dat melder 24 een kruiswissel met motor 13 en 10 is. Oorzaak: `KruiswisselReferentie` (melder 24, motor 13/10, vier benen,
'bewezen' verbindingen uit eerdere sessies/baankaarten) was vast in de code en werd altijd gebruikt: kruismotoren niet testen met de loc op 24, standen afdwingen, rapport en blokkenschema.
Fix: nieuw invoerveld 'Engelse wissels' (sectie 4 van het startscherm; `VerkenInstellingen.KruisMotoren`): per Engelse wissel de motoren, bijv. `13+10` of `13+10, 3+4`; optioneel met melder `13+10@24`; leeg = geen.
De verkenner houdt per verkenning een `KruisKennis` bij (motoren, kruiswissel-melder(s), referentie aan/uit; ook in de baankaart bewaard en bij hervatten teruggelezen).
- Zonder referentie: kruismotoren worden niet als overloopwissel behandeld (geen partnerzoektocht), nooit omgezet met de loc op een bekende kruiswissel, en de melder van de kruiswissel wordt ontdekt:
  kortsluiting bij omzetten van een kruismotor -> die melder telt vanaf dan als kruiswissel (`LeerKruisMidden`, logregel 'is (waarschijnlijk) de kruiswissel'). Tot dan is de eerste keer een kortsluiting
  onvermijdelijk, tenzij de melder is opgegeven (`13+10@24`).
- De vaste referentie (melder 24, motor 13/10, bewezen standen, vooraf zetten bij kruiswissel) is alleen actief als de gebruiker in de voorkennis-pop-up (#128) 'Ja' kiest; die pop-up noemt dat nu.
- Rapport en blokkenschema tonen zonder referentie alleen 'Engelse wissel: motor ..., kruiswissel in melder ...'; het schema tekent het kruis alleen voor een bekende kruiswissel-melder.
Tests: `KruisKennisTest` (12 controles). Nog niet getest op de baan; het WPF-project is niet gecompileerd (nieuw tekstveld in `MainWindow.xaml`/`.xaml.cs`).
Let op: een bestaande instellingenbestand heeft geen Engelse wissels; voer eenmalig `13+10` in (of `13+10@24`). Oude baankaarten zonder deze velden tonen het kruis niet meer in het blokkenschema.
Open: de 'Controle' van een voltooide kaart gebruikt de Engelse-wisselgegevens uit die kaart; alleen voorkennis 'Ja' schakelt de vaste referentie weer in.

## #130 Valse 'overloopverbinding' door de alarmcontrole: wissel 14 + 12 samen afbuigend gaf zelf een kortsluiting (Marco 11:42, log 1142)
Marco zag in de verkenning van 10:58 (versie #128) bij wissel 14 een fout. Wat er gebeurde: wissel 14 afbuigend gaf kortsluiting zodra de loc op melder 8 kwam (11:32). In de partnerzoektocht (#63, partner 12) bleef de
kortsluiting staan terwijl de loc stilstond; `ProbeerAlarmWegTeKrijgen` (#108) haalde haar weg door wissel 14 terug te zetten naar rechtdoor en de stap daarna (#117) hield 14 rechtdoor. De rit 8 -> 15 slaagde dus met 12 afbuigend en
14 RECHTDOOR, maar de partnerzoektocht legde vast: 'overloopwissels, 14 én 12 samen afbuigend -> 15' (kortsluitpunt #2 opgelost, wissel 12 'alleen door te rijden als 14 én 12 afbuigend'). Daarna plande hij opdracht #9 met 12 en 14
beide afbuigend; bij aankomst op melder 8 kwam meteen weer kortsluiting (blok 4), die ruim een minuut bleef staan terwijl de alarmcontrole alle andere wissels probeerde (12, 13, 14 zijn 'bij de loc' en worden niet omgezet, #109).
Dinamo hield daarna op met terugmelden (11:37:16, melding #126) en Marco moest Dinamo herstarten en wissel 14 met de hand terugzetten.
Fix: `PartnerwisselZoeken` ziet nu of de alarmcontrole tijdens de proefrit de wisselstand veranderd heeft (`_alarmStand`). Zo ja, dan bewijst de geslaagde rit geen overloopverbinding: er wordt geen partner en geen
'verder voorbij kortsluitpunt'-opdracht vastgelegd, de zoektocht stopt (anders volgt per partner weer een minuut alarmcontrole) en het logboek legt uit waarom ('Dat bewijst geen overloopverbinding a+b'). De loc rijdt terug met de werkelijke stand.
Test: `PartnerAlarmIngreepTest` (gesimuleerd dezelfde reeks als in het log; de echte overloop-test `OverloopTest` blijft groen). Nog niet getest op de baan.
Let op voor oude kaarten: bevindingen uit dezelfde reeks in log 1142 zijn verdacht: wissel 12 'alleen met 14 erbij' en wissel 1 'alleen met 2 erbij' (11:15; daar reed 5 -> 16 met wissel 1 afbuigend en wissel 2 rechtdoor).
Open: als de kortsluiting wél blijft staan en de schuldige wissel bij de loc ligt (#109), zet de verkenner hem niet zelf terug; de bestaande pop-up 'Staat er een wissel verkeerd?' (#79) vraagt Marco dan, zoals nu.

## #131 Na 'Dinamo is hersteld' eerst een hoortest: een willekeurige wissel gaat heen en weer (Marco 13:25)
Marco: 'Als ik op "Is Dinamo hersteld?" ja antwoord, schakel dan een willekeurige wissel heen en weer en vraag of ik dat gehoord heb. Zo nee, dan moet Dinamo nogmaals herstart worden.'
Fix: `HoortestNaHerstel` (Baanverkenner.Rijden.cs) draait in beide 'Dinamo hersteld'-vragen (#125 'Dinamo reageert niet' en #126 'Dinamo voert wissels niet uit'), vóór het opnieuw sturen van alle wissels.
Een willekeurige wissel (nooit een kruismotor, nooit een wissel onder de loc) gaat naar de andere stand en terug (1,5 s wachttijd per commando); daarna de vraag 'Heeft u de wissel horen klikken?'. Nee -> pop-up 'Dinamo opnieuw herstarten' ->
de test wordt herhaald, tot het klikt (venster sluiten = verkenning stopt; 'Nee' op de herstartvraag = toch doorgaan, gelogd). De wisselstanden in het programma blijven ongewijzigd. De terugmeldingen van Dinamo tijdens de test staan in het log (alleen ter info, Marco's gehoor is leidend).
Test: `HoortestEnKruisTest` (Nee -> herstart -> Ja, twee commando's per poging, standen ongewijzigd) en de aangepaste echo-test. Nog niet getest op de baan.

## #132 Handmatige oplossing werd aan het verkeerde kortsluitpunt gehangen (Marco 13:25, kaart 1323)
Kaart 1323: 'wissel 10 afbuigend' was de oplossing van de kortsluiting na melder 24 (kortsluitpunt #2), maar werd bij het oudere #1 (na melder 12) vastgelegd (`LastOrDefault` van alle onopgeloste punten; #2 wordt pas na de rit geregistreerd).
#2 bleef onopgelost en werd om 13:19 opnieuw geprobeerd; #1 kreeg een onjuiste oplossing. LET OP in de kaart 1323: 'KP#1 opgelost door 10' is onjuist (hoort bij #2), en de tekst 'ook in volgende verkenningen' klopte niet (alleen bij voorkennis 'Ja', #128).
Fix: de oplossing wacht (`_wachtendeHandOplossing`, met melder en richting van de lopende kortsluiting) en wordt in `RegistreerKortsluitpunt` aan het kortsluitpunt met dezelfde melder en richting gekoppeld (nieuw of bestaand). De logtekst is gecorrigeerd.
Test: `HoortestEnKruisTest` (#12 blijft open, #24 opgelost door 10). Nog niet getest op de baan.

## #133 Kruismotor omzetten terwijl de loc op de kruiswissel staat (kaart 1323, 13:13:03)
Wissel 10 werd om 13:13:03 teruggezet naar rechtdoor terwijl de loc op melder 24 stond: zelf veroorzaakte kortsluiting. `VoerOpdrachtUit` bewaakte dit (#96), maar `ZetConfiguratie` ook bij handmatige plaatsing en herstel wordt aangeroepen.
Fix: `ZetConfiguratie` slaat een kruismotor over zolang de loc op de kruiswissel staat (logmelding 'Motor X wordt nu NIET omgezet'); hij gaat om zodra de loc weg is. Test: `HoortestEnKruisTest`. Nog niet getest op de baan.

## #134 Alarmroutine draaide 75 s lang 22 wissels om op de kruiswissel (kaart 1323, 13:19:52)
Bleef een kortsluiting staan met de loc op de kruiswissel, dan probeerde `ProbeerAlarmWegTeKrijgen` alle andere wissels (de schuldige is daar een kruismotor, die nooit geprobeerd wordt): 75 s en 22 commando's zonder resultaat, en veel commando's vergroten de kans op een Dinamo-uitval (zie hieronder).
Fix: staat de loc op de kruiswissel, dan geeft `ControleerGeenAlarmVoorStap` meteen een navigatiefout, waarna de pop-up 'Staat er een wissel verkeerd?' (#79) Marco vraagt welke motor verkeerd staat. Test: `HoortestEnKruisTest`. Nog niet getest op de baan.

## Onderzoek: waarom reageert Dinamo niet meer? (Marco 13:25; geen code-oorzaak gevonden)
Analyse van alle hardwarelogs van 14-10: van ongeveer 12 uitvalmomenten begonnen er 8 bij een commando naar wissel 9 (of 10). Er zijn korte en lange uitvallen; tijdens korte uitvallen worden commando's in de wachtrij later alsnog uitgevoerd. Het ligt NIET aan kortsluiting, F-bit, toggle-bit of tellers in ons protocol (die staan bij uitval gewoon goed).
Bronnen (dinamousers.net, koploperforum.nl): wissels worden aangestuurd door een OC32-module (maximaal 16 wissels per module, rode led brandt bij schakelen); een defecte of te warme OC32 kan ophouden met reageren. Dit is een hypothese, niet bewezen.
Advies: controleer de module waar wissel 9 (en 10) op zit (bedrading hoeft niet, maar warmte, voeding en de rode led bij schakelen wel). Voorstel (wacht op Marco): een lijst met over te slaan wisseladressen (bijv. 9) en een snellere wisselinitialisatie (nu ~30 s).

## #135 Kruiswisselbewaking werkte niet zonder referentie (Marco 14:12, kaart 1411)
Bij een lege verkenning (voorkennis 'Nee', #128) is `Kruis.MetReferentie` onwaar. De kruisbewaking (juiste kruismotorstand bij aankomst en vooraf) gebruikte alleen die referentie en stond dus uit: de loc reed vier keer (14:09:07-14:09:59, blok 8) met een kruismotor verkeerd de kruiswissel op. De Dinamo-echo's waren goed (0 van 59 commando's zonder echo): het waren echte kortsluitingen.
Fix: `KruisAankomstStand` en `KruisVoorafStand` gebruiken nu ook het eigen bewijs uit de kaart (`Doorgangen`, `Overgangen`) via `KruisBewijs` (KruisKennis.cs). Daarnaast zet `ZetKruisVooraf` (Baanverkenner.Rijden.cs) de kruismotoren vooruit voordat de loc bij de kruiswissel aankomt (zoals `ZetStaartWissels`, #107), ook op verkenningsritten via melder 8. Bewijs eerst met exacte configuratie zoeken, anders pas de niet-kruisdelen vergelijken (voorkomt dat een gewone ronde 15-8-17 voor een kruisingang wordt aangezien).
Beperking: de eerste keer dat de loc via een nog onbekende arm de kruiswissel op rijdt, is er nog geen bewijs en kan één kortsluiting volgen. Test: `KruisZonderReferentieTest`. Nog niet getest op de baan.

## #136 Regressie van #132: handmatige oplossing bij een al bestaand kortsluitpunt (kaart 1411)
Mijn #132 loste de oplossing pas op in `RegistreerKortsluitpunt` (na de rit). In de stroom van 14:09 bestond kortsluitpunt #3 al, dus de oplossing werd nooit gekoppeld. Eerlijk: #132 was onvolledig.
Fix: `ControleerVerkeerdeWissel` zoekt met `ZoekKortsluitpunt(melder, richting, stand)` het bestaande punt en past de oplossing direct toe (`KoppelHandOplossing`); alleen als er nog geen punt is wacht de oplossing (#132). Logtekst: 'Dit wordt voortaan in deze verkenning meteen toegepast; in een volgende verkenning alleen als u bij de voorkennis-vraag Ja antwoordt'. Test: `KruisZonderReferentieTest`. Nog niet getest op de baan.

## #137 Onmogelijke kruismotor-oplossing op een kortsluitpunt (kaart 1323 en 1411)
Kortsluitpunt #1 stond in de kaart als 'opgelost door 10', maar de route van dat punt raakt de kruiswissel niet: een kruismotor kan daar nooit de oplossing zijn (vergiftigd door de verkeerde koppeling uit #132 en door Dinamo-uitvalperiodes). KP#2 'door 10' is wel aannemelijk.
Fix: `Baankaart.KruisOplossingMogelijk` weigert zo'n oplossing, en `HerstelOnmogelijkeKruisOplossingen` (aangeroepen in `InitKruis`) heropent zulke punten in een hervatte kaart en meldt dat in het log en onder de waarschuwingen ('Kaart hersteld (BUG #137)'). Marco's kaart 1411 wordt dus bij hervatten automatisch hersteld: KP#1 staat daarna weer open. Test: `KruisZonderReferentieTest`. Nog niet getest op de baan.
Externe bronnen: Digitrax KB127 en dccwiki (Crossing): een Engelse wissel/kruiswissel heeft twee tongen/motoren, die samen de weg bepalen; geen nieuwe oorzaak, wel bevestiging van het model. Let op: bewijs in de kaart uit Dinamo-uitvalperiodes (bijv. '10' op 14<->21, '13' op overgang 8->24 achteruit) kan onjuist zijn.

Correctie op de Dinamo-uitval (Marco 14:35): Marco heeft nog geen OC32 maar de oude Dinamo-printen voor de wissels; later stapt hij over op de OC32. De OC32-hypothese hierboven geldt dus nu niet. Oorzaak van de uitval blijft onbekend; de wisseluitgangen van de oude print (wissel 9 en 10) zijn de eerste kandidaat, niet bewezen. Geen lijst met over te slaan wissels en geen snellere initialisatie: alles wordt getest (Marco's keuze, betrouwbaarheid eerst).

## #138 Extra diagnose bij Dinamo-uitval (Marco 14:39)
Marco wil meer loggen en meer bronnen: er komt bij elke uitval een regel 'Diagnose uitval (BUG #138)' in het hardwarelog (`DinamoHardware.UitvalDiagnose`): volgnummer van de uitval in de sessie, welke wisselcommando's binnen 3 s rond het laatste bericht van Dinamo lagen (met tijdverschil), de laatste vier wisselcommando's, aantal commando's de laatste 10 en 60 s, een teller per wisseladres (uitvallen/commando's), de lengte van de drie wachtrijen en de poortstatus (open, BytesToWrite, BytesToRead, CTS, DSR). Schrijffouten naar de poort komen nu als 'SCHRIJFFOUT' in het log (voorheen alleen in de statusbalk).
Bronnen (dinamousers.net): (1) accessory-pakketten worden bij DCC niet herhaald, een storing of kortsluiting op de bus kan een wisselcommando laten missen (thread 'OC32 mist regelmatig DCC-opdrachten'); (2) in 'OC32 issue' volgde het probleem (wissel 5 en 7, later 8 en 9) de aansluiting/bank van de uitgangen, niet de wissels; (3) 'Algemene wisselstoring': een module waarvan de processor meer dan lauw wordt is vermoedelijk defect. Dit gaat over de OC32; Marco heeft nog de oude Dinamo-printen, dus alleen als aanwijzing. Nog niet getest op de baan; de Poortstatus-regels zijn alleen met de stub gecompileerd.

## #139 Terugmeldingen van wissels leesbaar loggen (Marco 14:47)
De terugmeldingen stonden al als ruwe bytes in het hardwarelog (en werden geteld voor #126), maar waren niet leesbaar: de logvertaler toonde ze als 'Onbekend/niet gedecodeerd'.
Nu staat bij elke terugmelding in het hardwarelog '<- terugmelding: wissel N rechtdoor/afbuigend' en toont de vertaler (export) het type 'Terugmelding wissel/sein' met adres en stand. Opbouw is gelijk aan het commando (0001 0 C AA, aaaaaaa), gecontroleerd aan echte regels uit log 1411 (90 80 = wissel 1 rechtdoor, 94 89 = wissel 10 afbuigend). De diagnoseregel bij een Dinamo-uitval (#138) noemt ook de laatste vier terugmeldingen met tijd.
Test: `WisselTerugmeldingTest`. Nog niet getest op de baan.

## #140 Loc strandde voor melder 20: het doelblok kreeg geen rijcommando (Marco 15:02, log 1501) + live vertaling
Wat het log laat zien (14:50:43-14:52:22): de blokproef voor melder 20 liet de loc een stukje terugrollen van melder 20 naar melder 30. `Nastellen` (neerzetten) stuurde daarna het kruipcommando eerst naar blok 7 én 6, maar brak na 2 s af omdat het een melder die net wegviel voor 'doorgeschoten' hield (verouderde meting van `doelWasBezet`). De twee volgende pogingen (30 s en 60 s) gingen alleen naar blok 7 (de enige bezette melder, 30), nooit naar blok 6 waar melder 20 ligt: de loc kwam op de blokgrens tot stilstand, op een recht stuk zonder wissel. Daarna: 'De loc kon niet netjes op melder 20 gezet worden'. Het kwam alleen voor omdat doel en loc in verschillende blokken lagen en de loc nog niet op het doel stond.
Fix: (1) `StuurSnelheid` krijgt een optionele doelmelder; `Nastellen` voedt daarmee altijd ook het blok van het doel. (2) `doelWasBezet` wordt na het rijcommando vers gemeten. (3) Beweegt de loc een hele poging niet (zelfde melders bezet), dan krijgt de volgende poging 2 stappen meer snelheid (maximaal de verkensnelheid), met een waarschuwing in het log. (4) Het log meldt per poging naar welke blokken het rijcommando ging ('Neerzetten op melder 20: snelheid 5 vooruit naar blok 6, 7').
Test: `NastellenAnderBlokTest` (alleen het rijcommando naar blok 6 en 7 wordt gecontroleerd; de simulatie laat de loc ook zonder de fix doorrollen, dus het echte vastlopen is hier niet nagebootst). Nog niet getest op de baan.
Live vertaling (Marco: 'kan je in het live log de vertaling gelijk opnemen?'): elke regel in het hardwarelog (live venster én export) krijgt achter de hex-bytes meteen de vertaling: 'snelheid 12 vooruit, blok 7, loc 5408', 'STOP blok 7', 'wissel/sein 3 afbuigend/rood', 'terugmelding wissel 3 afbuigend', 'melder 30 bezet', 'KORTSLUITING in blok 3'. Daarbij is gecorrigeerd dat de vertaler het blok 0-gebaseerd toonde (op de kabel staat blok-1); nu staat overal het Dinamo-blok zoals in het programma. De herkenning van herhaalde statusberichten in de Baanverkenner (`IsHerhaaldStatusBericht`) leest alleen nog de hex-bytes. Test: `WisselTerugmeldingTest` (echte regels uit log 1501). Nog niet getest op de baan.
Overig in log 1501: de 'KORTSLUITING in blok 3' van 15:00:46 viel samen met het neerzetten van de loc op melder 13 door Marco (3 s later opgeheven); geen programmafout.

## #141 Loc zat 95 s klem op wissel 2 afbuigend: kortsluiting kwam steeds terug (Marco 15:33, log 1532)
Log 15:30:36-15:32:13: de proefrit met wissel 2 afbuigend gaf kortsluiting in blok 3 en 4 (loc half op melder 16 en 5). Het hardwarelog laat zien dat Dinamo de stroom elke ~20 s terugzette en meteen weer afsloot (15:30:44, 15:31:05, 15:31:30, 15:32:10), óók terwijl de loc stilstond (alleen stopcommando's). Het terugrijden en neerzetten (2 x 65 s, snelheid 5 en 7) had dus geen kans; de loc zat klem op de punten van wissel 2. Pas toen de verkenner wissel 2 om 15:32:12 terugzette naar rechtdoor was de kortsluiting 1 s later weg.
Fix: komt een kortsluiting terug terwijl er geen rijcommando actief is (> 4 s na de eerste, `_statischeKortsluiting`), en is de wissel die de rit zelf proefde afbuigend, dan zet `LosMakenProefWissel` die wissel aan het begin van het herstel meteen terug naar rechtdoor (log: 'zit klem op wissel N'). Een kruismotor wordt nooit zo omgezet (#96). `PartnerwisselZoeken` zet de wissel voor een partnerproef zo nodig weer afbuigend (log: 'stond tijdens het herstel weer op rechtdoor').
Verwachte winst: ~90 s en drie keer minder kortsluiting per ontdekte wissel die de loc klem zet. Risico: de punten gaan onder de loc om; bij een loc die al klem zit in een kortsluiting kan het weinig erger maken, maar ontsporen is niet uit te sluiten. Test: `StatischeKortsluitingTest` (alleen de beslislogica; de simulatie kent geen kortsluiting die blijft staan bij een stilstaande loc). Nog niet getest op de baan.
Gezondheid Dinamo in log 1532: geen uitval, geen stilte, geen schrijffout, 114 terugmeldingen van wissels, 19 minuten.
Open (voorstel, wacht op Marco): melder 16 en 28 houden 'Dinamo-blok onbekend' omdat de loc (lang) altijd op twee melders tegelijk stopt (16+5 en 28+26) en dan de blokproef wordt overgeslagen. Mogelijke oplossing: het blok afleiden door uitsluiting (de blokken 1, 2 en 9 zijn nog niet gebruikt) en/of de blokproef doen met de loc op twee melders.

## #142 Melder 16 (en 129) kregen nooit een Dinamo-blok en kostten 5 x ruim 80 s (Marco 15:49, tussentijds rapport log 1548, versie bug140)
Log 15:13-15:48 (35 min, alleen wissel 1-11 klaar): bij elke ronde stopte de loc op melder 16 en 5 tegelijk. `Nastellen` probeerde 3 x de loc op alleen melder 16 te krijgen (15:24:20-15:25:42, 82 s), dat kan niet: de loc is langer dan melder 16, en zonder bekend blok van 16 krijgt alleen blok 3 een commando. Daarna "blokproef overgeslagen" en bij de volgende ronde hetzelfde (de poging werd niet geteld). Zo kostte melder 16 in totaal ruim 10 van de 35 minuten en bleef zijn blok onbekend. Melder 129 (na wissel 5 afbuigend, loc op 14 en 129) kreeg om dezelfde reden geen blok.
Fix: `BlokZoekenMetBuurmelder`. Staat de loc na het stoppen op de proefmelder én op buurmelder(s) waarvan het blok bekend is, dan krijgen de blokken van die buren geen rijcommando en de overige blokken om beurten (6 s, de rijrichting van de rit). Beweegt de loc, dan voedt dat blok het deel van de loc in de proefmelder (KoppelBlok, stoppen, rit gaat door). Dit is gemeten, niet geraden (geen afleiding door uitsluiting). `_nietIsoleerbaar` onthoudt dat een melder niet te isoleren is, zodat `Nastellen` daar niet nog eens 80 s verspilt; de poging telt nu mee voor `MaxProefPogingen`. Geen bruikbare buur: zoals voorheen overslaan.
Test: `BuurmelderBlokproefTest` (nieuwe simulatiehulp `PlaatsLocOpGrens`: loc op 16 en 5, melder 16 echt blok 2: blok 3 krijgt geen commando, blok 2 wordt gevonden). Nog niet getest op de baan. Risico: de loc rijdt per proefblok een stukje in de rijrichting (hooguit tot melder 5 of verder, gestopt zodra hij beweegt).
Overig log 1548: Dinamo gezond (0 uitval, 0 schrijffouten, 209 terugmeldingen, 35 min); wissel 5 afbuigend -> 129 gevonden; wissel 10 afbuigend gaf kortsluiting op de kruiswissel (melder 24, motor 10): in 20 s hersteld, geen klem; wissel 2 klem 95 s zoals in #141 (dit log draaide nog zonder #141). Open: rapport noemt wissel 10 als gewone wissel 'van achteren bereden' terwijl het een kruismotor is (alleen weergave).
Open (bewust uitgesteld, Marco 16:10): het rapport noemt wissel 10 een gewone wissel 'van achteren bereden' terwijl het een kruismotor is. Alleen weergave; oplossen als er een grotere baan getest wordt.

## #143 Wissel 1 en 2 (overloop-paar bij melder 12) werden door de verkenner zelf omgezet terwijl de loc in de kruising stond (Marco 16:24, log 1621, versie bug140)
Log 16:13-16:20: opdracht #3 (13 achteruit, wissel 2 afbuigend) kwam via 5 op melder 12; kortsluiting in blok 2 ging weg met wissel 1 afbuigend (paar 1+2 afbuigend = juiste stand). De loc stond daarna op melder 12 en 5, dus in de kruising. De terugrit 12 -> 5 -> 13 is met precies die stand eerder goed gereden (16:15:43). Toch meldde het log om 16:16:04: 'Wisselstand voor het pad 12-5-13 (vooruit) aangepast aan wat over de wissels bekend is: 2 rechtdoor'; de verkenner zette wissel 1 (16:16:04) en wissel 2 (16:16:11) naar rechtdoor onder de loc. Daarna stonden de tongen in de praktijk tussen beide standen (loc klem), het programma dacht rechtdoor, kortsluiting in blok 2 en 3, en geen enkele proefwissel hielp meer (17:33 'Geen enkele wissel haalde de kortsluiting weg') tot Marco 1 en 2 met de hand weer afbuigend zette. Er schakelde dus NIETS na het stoppen om: beide omzettingen staan als commando in het log.
Oorzaak: de splitsing van wissel 2 (na melder 13 achteruit: rechtdoor -> melder 5, afbuigend -> melder 5) heeft twee gelijke uitgangen. `WisselEisenVoorStapDetail` haalde daaruit 'rechtdoor' (de eerste vergelijking won) en eiste wissel 2 rechtdoor voor de stap 5 -> 13 vooruit. Bij een overloop-paar zegt zo'n splitsing niets.
Fix: `Baankaart.WisselEisenVoorStapDetail` slaat een splitsing over waarvan rechtdoor en afbuigend naar dezelfde melder gaan. Test: `OverloopPaarEisenTest` (echte baankaart van 16:20, `testdata/baankaart_2026-10-14_1621.json`); zonder de fix wordt wissel 2 weer rechtdoor gezet (getest), met de fix blijft '1, 2 afbuigend' staan. Gewone splitsingen (wissel 5, 14) werken nog.
Nog niet getest op de baan. Open: het blijft riskant om een wissel om te zetten terwijl de loc in de kruising staat; ik heb dat niet breder verboden omdat gewone ritten wissels juist bij de startmelder zetten.
Overig in dit log (zelfde run als #142, dus nog zonder #141/#142): melder 12 heeft nog geen blok (loc staat op 12 en 5; #142 dekt dat nu); wissel 13 (kruismotor) afbuigend gaf kortsluiting op melder 24 en is in ~85 s hersteld; wissel 14 gevonden (na 8 achteruit: rechtdoor 17, afbuigend 24: de binnenring); wissel 1 + 2 samen als overloop-paar bewezen (afbuigend 1+2 geeft 12).

## #144 Wissel 9 afbuigend haalde de kortsluiting na melder 129 weg, maar werd nergens vastgelegd (Marco 16:29, log 16:27)
16:23:20 kortsluiting in blok 9 bij de rit 4 -> 129 (achteruit); de loc zat 3 minuten vast op 129+4. Op 16:25:46 haalde wissel 9 afbuigend het alarm weg
(0,8 s na het commando; wissel 5 stond al sinds 16:23:18 afbuigend en kon dus niet de oorzaak zijn). De alarmroutine (`ProbeerAlarmWegTeKrijgen`) onthield
dat alleen voor de eerstvolgende stap: kortsluitpunt #2 bleef 'open', wissel 9 stond nergens in het rapport en de volgende rit 4 -> 129 zou weer met wissel 9
rechtdoor beginnen (opnieuw kortsluiting en weer ~50 s wissels proberen).
Oplossing: `RegistreerAlarmOplossing` legt de wissel die het alarm wegnam vast als oplossing van het nieuwste nog niet opgeloste kortsluitpunt waarvan de melder
(of tweede melder) bezet is. Daarna past `MetBekendeKortsluitOplossingen` hem voortaan zelf toe. Test: `AlarmOplossingVastleggenTest` (faalt zonder de fix).
Overig uit dit log: (D) de kortsluiting op 16:26:53 na melder 5 is #143 (nog de oude versie); (A) wissel 5 om 16:23:18 was NIET de oorzaak; (C) Dinamo gaf na het
commando voor wissel 9 (16:25:46) geen terugmeldingen meer (bekend patroon rond wissel 9/10, zie #138-notities); wissel 2 en 5 zijn toen niet uitgevoerd,
de gebruiker herstelde Dinamo en alle wissels zijn daarna opnieuw gestuurd. Nog niet getest op de baan.

## #144b Loc stond na de kortsluiting midden op een recht stuk stil (Marco 16:54, log 16:53)
16:51:12 kortsluiting na melder 5 (rit 16 -> 5 met alleen wissel 2 afbuigend; de proefwissel, bekend gedrag). De loc stond op melder 16 + 5. Wissel 2 ging terug
naar rechtdoor en het alarm was om 16:51:21 weg. Daarna reed `HerstelNaKortsluiting` de loc 'achteruit terug naar melder 5' en wachtte tot ALLEEN melder 5 bezet was.
Dat kan niet: de loc is langer dan een melder, dus achteruit rijden geeft 16+5 -> 16 -> 16+15 -> 15 en nooit '5 alleen'. De loc stond zo op melder 15
(midden op het rechte stuk, geen wissel in de buurt) en de gebruiker moest hem terugzetten (16:52:59).
Oplossing: is het alarm weg en staat de loc nog op melder x plus alleen melders die bij de kortsluiting ook bezet waren, dan blijft hij staan en rijdt de
herstelroutine niet. Het oude gedrag blijft voor alle andere gevallen (bijv. alarm blijft, of de loc is al verder verschoven).
Test: `HerstelBlijftStaanTest` (faalt zonder de fix: loc werd verplaatst). Nog niet getest op de baan.

## BUG #145 – wissel 2 geeft steeds opnieuw kortsluiting bij aankomst op melder 5 (Marco 17:29, log 17:20–17:29)
Opdracht #4 (vanaf melder 5 vooruit, afbuigend 1+2) reed eerst naar melder 5 (vanaf 16, met de loc deels op 16) en zette daarbij 1 en 2 al afbuigend.
Kortsluitpunt #1 (aankomst op 5 met wissel 2 afbuigend, ook bezet 16) stond als 'opgelost door wissel 1', maar die oplossing was gevonden met de loc
stilstaand (alarmroutine) en geldt niet voor de aankomst: 17:25 gaf 1+2 afbuigend opnieuw kortsluiting in blok 3 en 4. Daarna werd de opdracht nog een
keer geprobeerd en bleef het punt 'opgelost'.
Oplossing: (1) kortsluiting (blijvend alarm) bij de aankomst op de startmelder van een opdracht met een stand die het kortsluitpunt bevat trekt de
oplossing in (`WeerlegAankomstOplossing`), het punt telt als opgegeven en de opdracht wordt niet opnieuw gepland; (2) een opdracht waarvan de aankomst
zo'n opgegeven kortsluitpunt raakt wordt voortaan overgeslagen en als bevinding gemeld (`BekendeAankomstKortsluiting`). Wissel 2 afbuigend blijft in het
rapport dus 'kortsluiting'. Let op: een kaart waarin kortsluitpunt #1 nog als opgelost staat rijdt deze aankomst nog één keer (daarna is het ingetrokken).
Test: `AankomstOplossingWeerlegdTest`. Nog niet getest op de baan.

## BUG #146 – wissel 1+2 al afbuigend tijdens het aanrijden van melder 5, en wissel 2 alleen afbuigend achteruit (Marco 17:50, log 17:20–17:50)
Twee oorzaken achter de steeds terugkerende wissel-2-kortsluiting:
(1) Opdracht #4 (vanaf melder 5 vooruit, 1+2 afbuigend) zette 1 en 2 al afbuigend tijdens het aanrijden vanaf 16 (#122 + bekende oplossing van
kortsluitpunt #1). De kaart toont dat precies die aankomst (route eindigt op 5, vooruit) kortsluiting gaf. De partnerrit die 5 -> 13 wel goed reed,
zette 1+2 pas toen de loc op melder 5 stond. Nu: `AankomstZonderKortsluitWissels` houdt de wissels van zo'n kortsluitpunt (en de bijbehorende oplossingswissel)
tijdens die aankomst rechtdoor; `ZetConfiguratie(rijstand)` zet ze direct na aankomst, zoals de partnerrit.
(2) Opdracht #3 (13 achteruit, wissel 2 afbuigend) reed alleen met wissel 2 en gaf kortsluiting na melder 12 (kortsluitpunt #2); de gebruiker moest wissel 1
aanwijzen. Het overloop-paar 2+1 was vooruit al bewezen maar gold alleen in die richting. Nu: `MetOverloopPartners` (in `RijStand`) voegt de partner
in beide richtingen toe zodra het paar bewezen is (gereden oplossing of oplossingen in beide richtingen); de opdracht die de partner zelf test krijgt hem niet.
Naast #145 (intrekken bij herhaalde kortsluiting) blijft dit vangnet bestaan. Test: `AankomstEnOverloopTest`. Nog niet getest op de baan.
Open: of wissel 1+2 zetten terwijl de loc op melder 5 staat veilig is (Marco's regel: niet onder de loc omzetten) - de partnerrit deed het eerder zonder
probleem, maar dat is de eerste keer op de baan.

## BUG #147 – de verkenner vindt de schuldige wissel bij de loc zelf niet (wissel 14 bij melder 8; Marco 18:41, log 18:32–18:37)
Opdracht #7 (24 vooruit, wissel 13 afbuigend) kreeg kortsluiting bij melder 8; wissel 14 (ligt bij melder 8) moest afbuigend. De alarmroutine
(#108/#109) slaat wissels bij een bezette melder over (omzetten met de loc erop kan ontsporen), terwijl juist die wissel bijna altijd de oorzaak is
(ook wissel 2 bij melder 5 om 17:27). Resultaat: "Geen enkele wissel haalde de kortsluiting weg" en de gebruiker moest hem zelf aanwijzen.
Oplossing: haalt geen andere wissel het alarm weg, dan vraagt de verkenner één keer (Ja/Nee) of hij de wissel(s) bij de bezette melder met de stilstaande
loc mag proberen; de wissels waarvan de kaart al weet dat afbuigend daar kortsluiting geeft, eerst. Bij Ja en een verdwenen alarm wordt de wissel als
oplossing van het kortsluitpunt vastgelegd (#144). Bij Nee verandert er niets (de regel van #109 blijft). Test: `AlarmWisselBijLocTest`. Nog niet getest op de baan.
Open (niet gedaan): de eerste kortsluiting voorkomen door bij een nieuwe aankomstkant van een samenvoegende wissel (14 bij melder 8 vanaf 24) die wissel
vooraf afbuigend te zetten; daarvoor is de kaart te onzeker (eerdere 24 -> 8-ritten stonden als gelukt terwijl Dinamo toen geen wissels uitvoerde).

## BUG #148 – motor 13 niet vooraf gezet bij 15 -> 8 -> 24 achteruit met wissel 14 afbuigend (Marco 18:50, log 18:44–18:48)
Opdracht #8 (vanaf melder 15 achteruit, wissel 14 afbuigend) reed de kruiswissel (melder 24) in vanaf melder 8 met motor 13 rechtdoor: kortsluiting in
blok 8; de gebruiker moest motor 13 aanwijzen (kortsluitpunt #6). De vooruitblik van #135 (`ZetKruisVooraf`) had dat moeten voorkomen: 24 -> 8 met 13 en 14
afbuigend was net bewezen. Oorzaak: de kaart bevatte ook een waarneming 17 -> 8 met alleen wissel 14 afbuigend (ruis), dus gold 17 als enig vervolg van 8 en
`VolgendeBijStand` keek niet verder naar de kruiswissel. Nu: bij een niet-lege stand (zonder de kruismotoren) telt een kruiswissel-vervolg dat exact bij die stand
gezien is naast het exacte vervolg mee; bij de lege stand (gewone lus 15 -> 8 -> 17) verandert er niets. Test: `KruisRuisVoorafTest` (kaart van 18:49).
Nog niet getest op de baan. Opmerking: "13,14" in veel andere ritten is geen doorgeven van de oplossing: opdracht #7 (13 afbuigend) en de oplossing 14 werden
daarna als rijstand gebruikt; dat is bedoeld.

## BUG #149 – wissel bij de startmelder (1+2 bij melder 5) werd steeds onder de staande loc omgezet (Marco 17:29, 17:50, 18:41, 18:57; log 18:50–18:54)
Opdracht #4 (5 vooruit, 1+2 afbuigend) reed de loc via melder 16 naar melder 5 en zette 1 en 2 pas om toen hij er stond: kortsluiting in blok 3, "geen
enkele wissel haalt het weg", gebruiker moet ingrijpen. Dit is de oorzaak van alle wissel-2-problemen van vanmiddag: een samenvoegende wissel staat voor
één ingang goed (16 -> 5: rechtdoor; 12 -> 5: 1 en 2 afbuigend) en kan niet omgezet worden terwijl de loc op het samenkomstpunt staat. De kaart wist dit al
(Doorgangen: melder 5 vooruit vanaf 12 met 1,2; vanaf 16 alleen zonder 2). Nu: `AankomstViaBewezenIngang` kiest, als een wissel van de rijstand bij de startmelder
ligt en alleen een andere ingang bewezen is dan die van de normale route, een bekende weg in de rijrichting naar die ingang (`ZoekWegInRichting`, kortste weg over
eerder gereden overgangen, kleinste bewezen wisselstand per overgang; hier 13-14-129-4-11-12-5 met 5, 1, 2 afbuigend) en rijdt de loc daar naartoe, met de wissels al gezet
vóór de aankomst. #146 (wissels pas na aankomst zetten) geldt alleen nog voor dezelfde ingang als het kortsluitpunt. Test: `AankomstViaIngangTest` (kaart 18:55).
Nog niet getest op de baan. Open: werkt alleen als de kaart de ingang al bewezen heeft en er een weg in dezelfde rijrichting naartoe bekend is; anders blijft de normale route.

## BUG #150 – grote ronde zonder fouten, daarna fout na fout: verspilde proeven en vastzittende loc (Marco 19:24, alle logs van de afgelopen weken)
Er is nu een nagebouwde Miniworld-baan in de simulator (`MiniworldBaan`, hypothese uit de kaarten: NIET door Marco bevestigd) met Dinamo-gedrag (blokalarm, kortsluiting
kort na de melder, "ook bezet op dat moment", gepolariseerde wissels, kruiswissel 24). De volledige verkenning vanaf nul wordt daarop herhaald (`MiniworldRitTest`).
Gevonden en opgelost:
1. Partnerzoektocht na elke kortsluiting (8-9 extra kortsluitingen per fout, 30 -> 7 in de simulatie): alleen nog als de loc een afwijkend pad reed; anders geen partnerzoektocht;
   de kruismotoren komen in de partnervolgorde vóór de rest.
2. Blijft het alarm aan nadat de loc is gestopt (Dinamo meldt niet opnieuw), dan telt dat nu ook als vastzittende loc: de proefwissel gaat terug naar rechtdoor (#141) in plaats van dat
   de loc blind achteruit rijdt tot melder 15 (zelfde fout als #144b in het echte log). Kruismotoren blijven buiten schot (#96, regressietest #141).
Stand: de eerste kortsluiting is nu hersteld zonder dat de gebruiker ingrijpt (simulatie vanaf melder 17). Een volledige rit zonder enige kortsluiting is NIET gehaald: bij de eerste
aankomst op een onbekende samenvoegende wissel en de kruiswissel is een kortsluiting inherent aan beginnen zonder voorkennis; na de kruiswissel-kortsluiting blijft de loc vastzitten
(motor 13 mag niet onder de loc worden omgezet, #96) en moet de gebruiker de loc terugzetten. Nog niet getest op de baan.
Open: voorkennis-run meten (0 kortsluitingen als doel), kortsluitoplossingen per ingang i.p.v. per volledige stand, misleidende hint in `Kortsluiting()`, wissel 10 weergave.

## BUG #151 – blokken zichtbaar in het log en kortsluitoplossingen per ingang (Marco 07:27: "we gaan steeds meer vanuit blokken denken als we ze weten")
1. Het log noemt bij elke bezette melder nu het Dinamo-blok ("melder 5 (blok 3) bezet"), zodra het blok bekend is.
2. `KortsluitOplossing` heeft nu `Via` (de melder waar de loc vandaan kwam). Een oplossing met bekende ingang (bijv. 5 -> 12 achteruit: wissel 1 afbuigend) wordt in
   `MetBekendeKortsluitOplossingen` voor die ingang bij elke wisselstand toegepast en ook omgekeerd (12 -> 5, dezelfde wissel op hetzelfde spoor), niet alleen bij de
   volledige stand waarin hij gevonden werd. Het leerprofiel bewaart `Via` mee. Nog niet getest op de baan; nog geen eigen regressietest voor de omgekeerde toepassing.
3. Herstel na kortsluiting (`HerstelNaKortsluiting`): bij blijvend alarm gaan de afbuigende wissels van de rit terug (niet alleen de proefwissel); daarna worden de overige wissels
   één voor één geprobeerd (`ProbeerAlarmWegTeKrijgen`); staat de loc al precies op de melder dan blijft hij staan; haalt niets het alarm weg dan wordt direct de gebruiker gevraagd,
   niet blind teruggereden. Een kruismotor onder de loc terugzetten kan alleen na eenmalige toestemming (Ja/Nee-vraag; #96 blijft: nooit zonder vraag).
4. Simulator: het verbindingsstuk van overloop 1+2 hoort bij melder 5 (zonder melder viel de loc stil, een simulatie-artefact).
Stand simulatie vanaf melder 17: 6 kortsluitingen (was 2 + vastzitten), melders 4, 5, 8, 11-17, 20, 21, 24-30, 129 gevonden; de rit is NIET voltooid: na de kortsluiting bij de kruiswissel
(24 vooruit) moet de gebruiker de loc terugzetten, en de simulator kan de loc daarna niet neerzetten. De run met voorkennis (`MINI_VOORKENNIS=1`) kon niet gemeten worden:
de simulator zet de loc niet op de startmelder (placement-artefact). Nog niet getest op de baan.

## BUG #152 – loc blijft na het nastellen in het wisselgebied staan (Marco 07:48: "rijd van de verst verwijderde sectie naar het volgende blok, anders sta je soms al in de wissel; komt bij melder 5 van blok 3 al voor")
`Nastellen` stopte zodra alleen de doelmelder bezet was: de neus net op de melder, de rest van de loc nog in het wisselgebied aan de ingang (melder 5: wissel 1+2). Nu kruipt de loc
na het nastellen nog een derde van de bekende tijd tot de volgende melder door in de rijrichting (maximaal 30 s, stopt zodra een volgende melder bezet raakt), alleen als de kaart bij die
melder een wissel kent (waarneming "kortsluiting bij omzetten" of "loste kortsluiting op" met die melder). Onbekende tijd of geen bekende wissel: niets. Nog niet getest op de baan.
Ook nog niet in de simulator uitgeoefend (de Miniworld-run komt niet in dit geval); regressietests blijven groen. Open: de plaats van de wissel (ingang of uitgang van de melder) is
uit de kaart niet zeker af te leiden; Marco's voorbeeld (melder 5) is de enige reden om aan te nemen dat het de ingang is.

## BUG #153 – nastellen kroop met voorkennis de verkeerde kant op (simulatie tweede run, start melder 30)
Stond de loc op het doel én op een buurmelder (30 + 20) dan kroop hij in de laatste rijrichting, dus verder de buurmelder in (50 s, daarna nog twee pogingen), en gaf hij op
("De loc kon niet netjes op melder 30 gezet worden"). `RichtingNaarDoel` kijkt nu ook in het leerprofiel (voorkennis) en `Nastellen` kruipt bij precies twee bezette melders
weg van de buur als de richting bekend is. Het is alleen een hint (staat de loc anders om dan in de vorige verkenning, dan kost een verkeerde gok één poging zoals eerder).
Gemeten: tweede run met voorkennis start nu en rijdt 32 minuten (voorheen stopte hij na 3 minuten). Nog niet getest op de baan.
Bevinding (niet opgelost): de tweede run met voorkennis krijgt nog dezelfde twee kortsluitingen (wissel 2 bij melder 5, kruiswissel 24). Het leerprofiel bewaart alleen
overgangen (tijden), blokhints en `KortsluitOplossingen`; de eerste run leverde daarvan maar 1 oplossing op (5 -> 12 achteruit: wissel 1). De kortsluitpunten zelf (welke ingang
met welke wissel kortsluit) en de doorgangen per ingang worden niet in het leerprofiel bewaard. Voorstel: die ook bewaren, per ingang (Via -> Na, richting, wissel), en bij voorkennis
vooraf toepassen.

## BUG #154 – Dinamo-uitval 15-10 07:55 en uitvallijst (Marco 07:59)
Log 15-10 07:25-07:57: de verkenning werkte tot 07:55:15 normaal (264 terugmeldingen); vanaf het eerste commando naar wissel 9 (07:55:16.330, afbuigend) kwam er geen terugmelding meer, ook niet bij wissel 9
rechtdoor en wissel 10. Melders, snelheid en stop bleven werken. De hoortest (07:57:29) bevestigde het: wissel 1 ging niet om. Dit is dezelfde uitval als in #126 en het onderzoek na #134
(8 van de 12 uitvalmomenten op 14-10 begonnen bij wissel 9 of 10). De uitvallen staan nu met tijdstip in `Modeltreinbesturing/DINAMO_UITVAL.md`.
Bevinding: de uitvaldiagnose (#138) staat niet in het hardwarelog bij deze uitval; hij gaat alleen af bij 'Dinamo antwoordt niet'. Opgelost: bij 'DINAMO VOERT WISSELCOMMANDO'S NIET UIT' schrijft de hardware nu dezelfde diagnose in het hardwarelog (`IWisselBevestiging.LogWisselUitval`, standaard niets voor simulator en stubs). Hypothese (niet bewezen): wissel 1 t/m 8 en 9 t/m 14
hangen aan een verschillende print of adresblok; het eerste commando naar de tweede print legt de wisseluitgang vast. Nog niet getest op de baan.
Kaart 15-10: voor de uitval is niets onbetrouwbaars gevonden (wissel 2 en 5 gevonden voor 07:50); de rest van de kaart tot en met wissel 8 klopt dus, wissel 9 en hoger zijn niet getest.

## #155 - Kortsluiting bij wissel 2 genoteerd als "tussen 5 en 13" i.p.v. als ingang 16 → 5 (15-10 08:24)
Marco: de loc reed van 16 naar 5 en kreeg kortsluiting; genoteerd moet worden "16 → 5 niet rijden als wissel 2 afbuigend staat".
Oorzaak: de waarneming KortsluitingBijOmzetten kende alleen NaMelder (5) en het vervolg bij rechtdoor (13), niet de melder waar de loc vandaan kwam.
Oplossing: `WisselWaarneming.Via` (ingang, 16) wordt gevuld uit de gereden reeks; toelichting en rapport (tekst/html) noemen nu "ingang 16 → 5 (vooruit): niet rijden als afbuigend";
wisselstraat 16 → 5 vooruit eist nu "wissel 2 rechtdoor (anders kortsluiting)" (was "geen wissel nodig"). Test in CompileCheck (kaart 08:24). Nog niet getest op de baan.

## #156 - Rapport toonde maar een paar basisritten; Marco wil iedere rit met blokken, melders en wisselstanden (15-10 08:57)
Het rapport ("Gereden trajecten") bevatte alleen de gekozen basisritten. Nu bewaart de verkenner iedere gereden rit (verkenning en navigatie) in de baankaart (`Ritten`)
en toont het rapport (tekst en html) een lijst "Alle ritten": nummer, tijd (einde rit), soort, start en richting, afbuigende wissels, per blok de melders in volgorde
(bijv. `3: 5, 13, 14 → 4: 8, 15, 16`) en hoe de rit eindigde (lus, doodlopend, KORTSLUITING). Blok "?" = blok van de melder nog onbekend. Test in CompileCheck. Nog niet getest op de baan.

## #157 - Na uitval van Dinamo toch conclusies getrokken uit onbetrouwbare ritten (15-10 09:03-09:19, Marco 09:28/09:29)
Na het alarm "Dinamo voert wissels niet uit" koos de gebruiker "Nee" (wissel 9 ging wel om); daardoor stond de controle de rest van de sessie uit. Wissel 5 en 2 werden daarna niet uitgevoerd
(zie DINAMO_UITVAL.md), waaruit een valse oplossing van kortsluitpunt #3 en een valse 'onverwachte terugweg' #4 ontstonden.
Oplossing:
- Bij het alarm gaat de loc eerst stil en wordt een willekeurige veilige wissel steeds heen en weer omgeschakeld (max 2 min). Komt Dinamo weer terugmelden, dan gaat de verkenner zonder vraag verder.
- De kaart van vóór de eerste ontbrekende terugmelding wordt bewaard; bij herstel ("Ja"), stoppen of de 2-minutentest zonder succes gaan alle bevindingen sinds die eerste ontbrekende terugmelding weer uit de kaart.
- "Nee" zet de controle niet meer uit: alles daarna staat als ONBETROUWBAAR in de lijst "Alle ritten" en de waarschuwingen, wordt niet in het leerprofiel bewaard, en bij 12 commando's achter elkaar zonder terugmelding stopt de verkenning.
Tests in CompileCheck (3 nieuwe + 1 aangepast). Nog niet getest op de baan.

## #158/#159 - Kruiswissel-lus (motor 10 steeds verkeerd) en weer 16 → 5 met wissel 2 afbuigend (15-10 12:21/12:24)
Oorzaak:
- Kortsluitpunt na melder 24 werd steeds opnieuw "opgelost" en weer teruggedraaid: een handmatige correctie ("wissel 10 verkeerd") werd aan de verkeerde context gekoppeld, een rechtdoor-oplossing voor motor 10 werd afgewezen en NavigatieFouten werden niet geteld, dus de lus eindigde nooit.
- Kortsluitpunt #3 ("na melder 5 vooruit opgelost door wissel 2 afbuigend", ontstaan tijdens een Dinamo-uitval) is het omgekeerde van wat bekend was: 16 → 5 met wissel 2 afbuigend IS de kortsluiting (#155). Oudere waarnemingen hadden geen Via (ingang), dus het werd niet tegengehouden.
Oplossing:
- Een rechtdoor-stand van kruismotor 10/13 telt als verklaring van het kortsluitpunt; handmatige correcties worden via de laatste context aan het juiste punt gekoppeld; 3 navigatiefouten op één punt = opgegeven (geen oneindige lus).
- Nieuw: `Baankaart.VerbodenIngangen()` leidt voor oudere waarnemingen zonder Via de ingang af uit de doorgangen; `MetVerbodenIngangen` zet zulke wissels op een pad weer rechtdoor; proeven met een verboden ingang worden overgeslagen; `HerstelStrijdigeOplossingen` trekt een oplossing in die zelf de verboden ingang afbuigend neemt (met waarschuwing).
- `ControleerGeenAlarmVoorStap` kreeg een overload met startmelder/richting (testen blijven werken).
Test: CompileCheck (kaart van 12:20 als testdata). Nog niet getest op de baan.

## #160 - Verkenning 15-10 12:42 (voltooid): kruismotor 10 bij aankomst op melder 129 vastgehouden; ingang ontbrak in rapport
Analyse van de voltooide verkenning (log 12:32-12:41, hardwarelog 12:42): geen Dinamo-uitval meer in deze sessie, 16 → 5 is niet meer met wissel 2 afbuigend gereden (#159 werkt: kortsluitpunt #3 werd bij het hervatten ingetrokken), kortsluitpunt #9 (kruismotor 10) werd verklaard en niet meer opnieuw geprobeerd.
Gevonden:
- Opdracht #12 (12:33): `AankomstZonderKortsluitWissels` hield bij de aankomst op melder 129 ook wissel 10 rechtdoor vast (kortsluitpunt #8 had 6+10 afbuigend). Maar 24 → 129 heeft motor 10 afbuigend nodig: de loc reed 24 → 14, valse melding "wissel niet goed", opdracht overgeslagen. Hardwarelog bevestigt: wissel 10 kreeg in die sessie geen enkel commando.
- Rapport toonde bij wissel 2 en 14 nog "na melder 5/8" zonder ingang, omdat oudere waarnemingen geen Via hadden.
- Wissel 11 stond onder "adressen zonder gevonden effect" terwijl hij waarnemingen heeft.
Oplossing: wissels die de route zelf eist en kruismotoren op een route over de kruiswissel worden bij de aankomst niet meer vastgehouden; ontbrekende Via wordt bij laden en afronden afgeleid uit de doorgangen (wissel 2: 16 → 5, wissel 14: 17 → 8, wissel 10: 14 → 24); wissels met waarnemingen tellen mee als "effect gevonden".
Test in CompileCheck (kaart 12:42). Nog niet getest op de baan.

## #161 - Controle (15-10 13:48, zonder versie 160) reed alleen de basisronde; schijnverschil bij adressen
Oorzaak: de controle begint zonder overgangen en plande alleen "start, vooruit" en "start, achteruit". De zijtakken (melder 129, 10, 9, 1, 12, 11, 4 en 14 → 129, 24 → 8, 8 → 24, 5 → 12) werden nooit bereikt, omdat ze alleen via een afbuigende stand te bereiken zijn en de controle geen adrestest doet. Het verslag meldde daardoor 18 overgangen 'niet gereden' en 9 wissels 'niet opnieuw vastgesteld'. Daarnaast telde 'adressen zonder effect: nu 1-14' als verschil, terwijl de controle geen adrestest doet.
Oplossing: na de basisrondes plant de controle elk traject uit de bestaande kaart (zelfde startmelder, richting, wisselstand), met een route over de basis of over een ander traject dat langs de startmelder loopt (`PlanControleTrajecten`, `ControleRoute`). Trajecten die op een bekend kortsluitpunt zonder bekende oplossing eindigen worden niet gereden. De vergelijking van 'adressen zonder effect' is vervallen.
Test in CompileCheck (simulatie: alle overgangen opnieuw gereden; kaart 12:42: route naar 129 via 21 met motor 10 afbuigend). Nog niet getest op de baan.

## #162 - Controle duurde uren en liep steeds vast op wissel 9 en de kruiswissel (15-10 13:58-16:17, Marco 16:19)
Analyse van log 13:58-16:17 (2356 regels): ruim tien keer moest de loc worden teruggezet (wissel 9 'verkeerd', 2 en 1 'klem', kruiswissel 24/129).
Oorzaken:
1. `VoorControle` nam `BekendeKortsluitOplossingen` over, maar die lijst is leeg (de oplossingen staan in de opgeloste kortsluitpunten). De controle begon dus zonder enige kennis en vond alle kortsluitingen opnieuw, inclusief 'wissel 2 afbuigend zonder wissel 1' en 'wissel 9 afbuigend bij 24 → 129'.
2. De controle testte per opdracht alle 14 adressen opnieuw (elke test een hele ronde, met wissels in vreemde standen) en probeerde kortsluitpunten uit: dat is een nieuwe verkenning, geen controle. Met #161 (meer trajecten) werd het uren.
3. Log 14:40:41: bij de adrestest met motor 10 afbuigend zette `ZetStaartWissels` wissel 9 afbuigend "voor de loc er is" (goed voor 24 → 14), maar de loc reed met motor 10 afbuigend 24 → 129, de verboden ingang voor wissel 9 afbuigend: kortsluiting in blok 9, loc op de kruiswissel.
Oplossing:
- De controle start met `AlleKortsluitOplossingen()` van de kaart.
- In de controle: geen adrestest meer en geen kortsluitpunten uitproberen; de rit wordt gereden, vergeleken en gemeld.
- `StaartWisselToegestaan`: een staartwissel gaat niet afbuigend als een kruismotor afbuigend staat en de kaart die wissel voor die richting als verboden ingang kent.
Nieuwe bevindingen uit het verslag van 16:19 (niet als fout): wissel 2 na melder 5 achteruit: rechtdoor 16, afbuigend 12; overgangen 1 → 9, 9 → 10, 12 → 1 vooruit; wissel 1 'niet rijden 1 → 12 achteruit afbuigend'. De kaart wordt hierdoor niet gewijzigd.
Test in CompileCheck (kaart 12:42). Nog niet getest op de baan.

## #163 - Hele Modeltreinbesturing optimaliseren met de kennis uit de kaart (15-10 16:22, Marco; gekozen: automatisch overnemen)
Nieuw: `WisselstraatCorrectie` (Modeltreinbesturing). Na het openen van een baankaart en het importeren van nieuwe wisselstraten worden de BESTAANDE wisselstraten naast de kaart gelegd:
- Wissel in een wisselstraat in de stand die de kaart als kortsluiting bewezen heeft (bijv. 16 → 5 met wissel 2 afbuigend) wordt omgezet.
- Een wissel die de kaart voor die rit eist maar in de wisselstraat ontbreekt, wordt toegevoegd.
Veiligheid: alleen eisen die voor alle voorstellen van het blokpaar gelden; "aangenomen" eisen nooit; een verschil dat alleen uit een splitsing komt wordt gemeld, niet gewijzigd; kruiswissel-motoren (13/10) worden nooit aangeraakt (alleen gemeld); geen wisselstraten toegevoegd of verwijderd. Na afloop dialoog met alle meldingen en de vraag "behouden?" (Nee = alles ongedaan).
Wiring: `MainWindow.WisselstratenCorrigeren` na `WisselstratenImporteren`. Het WPF-deel kon hier niet gecompileerd worden (alleen de klasse zelf is getest in CompileCheck).
Nog niet getest op de baan.

## #164 - Loc klem bij wissel 1/2 (melder 12) werd niet zelf opgelost (16-10 09:00-09:13, Marco: "Je kon de situatie rond wissel 2 en 1 niet zelf oplossen?")
Oorzaken (log 09:00-09:13):
1. Het blokalarm van Dinamo viel in het negeervenster van de stop bij een NIEUWE melder (`_negeerKortsluitingTot`) en werd weggegooid; de rit zag daardoor "geen kortsluiting" en de loc bleef kortgesloten staan.
2. Racetoestand bij ritstart: `_kortsluitingGemeld` werd op false gezet terwijl het alarm al actief was.
3. Een zinloze blokproef (9 minuten) met een onbekend blok terwijl de kortsluiting nog stond.
4. Er was geen manier om een klem te herkennen als het alarm alleen onder rijstroom bestaat (Dinamo meldt het niet als er niet gereden wordt).
Oplossing:
- `_genegeerdeAlarmen`: alarmen in het negeervenster worden onthouden; na de rit alsnog als kortsluiting behandeld (`Kortsluiting(..., alarmBlokken)`).
- `RitUitvoeren`/`BlokZoekenBijStart`: alarmlijst (`HuidigeAlarmBlokken()`) telt mee bij start en in de lus; geen blokproef bij actief alarm.
- `ProbeerKlemWegTeKrijgen` + `VrijheidsProef`: zit de loc klem aan het begin van een navigatierit, dan zet de verkenner zelf wissels in de buurt om (nooit kruismotoren, nooit wissels onder een bezette melder, wat de rit nodig heeft blijft staan), elk met een korte proefrit; wat werkt wordt als kortsluitoplossing vastgelegd.
- Terugweg: `ZelfTerugWegAlleStanden` (ook overgangen met afbuigende wissels en omgekeerde overgangen) en een voorstap van de onverwachte melder (bijv. 12) terug naar de laatste goede melder met de stand die de loc vrij maakte (`NavigatieFout.RitRichting/RitConfiguratie`).
Test: CompileCheck (alles OK) + Miniworld-simulatie met meldvertraging 0.25-0.6 s: vanaf 0.40 s lost de verkenner het zelf op en vraagt Marco niets (0 vragen); bij 0.25-0.30 s komt de loc vrij, maar de terugrit 12 -> 5 geeft in de simulatie nog kortsluiting in blok 3 en dan vraagt de verkenner nog 3x om de loc terug te zetten (open, zie hieronder).
Open: de simulatie van de terugrit vanaf 12 is een hypothese van de baan; echte gedrag onbekend. Drie Dinamo-uitvallen van 15-10 (14:48, 14:55, 15:27) nog niet in DINAMO_UITVAL.md.
Nog niet getest op de baan.

## #165 - Kruiswissel (melder 24) en wissel 14 gaven steeds nieuwe kortsluitingen en valse bevindingen (16-10 17:23-17:50, Marco: "Dat kruiswissel en wissel 14 blijven toch moeilijk"; wil plusteken in rapport en meerdere melders/volgorde bijhouden)
Oorzaken (log 17:27-17:50):
1. De kaart legde de wisselstand vast die de rit WILDE, niet die de wissels HADDEN: ZetConfiguratie slaat motor 13/10 over als de loc op de kruiswissel staat. Zo kreeg doorgang 14 -> 24 -> 8 ook de standen 'alle rechtdoor' en '14' en 8 -> 24 -> 14 idem.
2. Eén keer ruis blokkeerde het voor-zetten van motor 13: `KiesStand` doet niets als de gewenste stand 'ook ergens gezien' is. Gevolg: 15 -> 8 -> 24 -> 14 reed met motor 13 rechtdoor (hangen 17:43, kortsluiting 17:47 en 17:50).
3. De adrestest-rijstand (`RijStand`) gebruikte zonder referentie het eigen bewijs van de kaart niet voor de kruiswissel (alleen `KruisIngangStand`, referentie). Elke proefrit zette motor 13 weer rechtdoor ('Rijstand hersteld voor de proef') en de kortsluiting werd toegeschreven aan wissel 1 en 3. Wissel 1 'niet rijden 8 -> 24 als afbuigend' in het rapport is dus een VALSE bevinding (motor 13 stond fout).
4. Wissel 14 afbuigend voor 24 <-> 8 was bekend (KRUISWISSEL.md) maar in een run vanaf nul niet; de eerste 24 -> 8 gaf kortsluiting (17:27) en kostte 9 minuten plus een Dinamo-uitval.
Oplossing:
- `WerkelijkeStand`: Overgangen en Doorgangen worden met de werkelijke wisselstand vastgelegd.
- `Doorgang.Tellers` (per configuratie) en `KruisBewijs.GezienBijIngang`: een kruisstand telt alleen mee als hij minstens half zo vaak gezien is als de meest geziene van die doorgang (en per uitgang mogelijk: `volgende`).
- `KruisBewijs.PasEigenBewijsToe` in `RijStand`: voor elke doorgang ingang -> midden -> uitgang van het voorspelde pad (twee melders vóór en na het midden) krijgen de kruismotoren de stand uit het eigen bewijs; motoren die de opdracht zelf vastlegt blijven staan.
- `KruisPlus`: het plusteken (benen met alle bezetmelders in volgorde, Dinamo-blok, motorstanden per rit door melder 24) staat weer in het tekst- en html-rapport, ook zonder referentie, alleen met wat deze verkenning zelf vond.
Open: wissel 14 voor 24 <-> 8 wordt zonder referentie nog steeds pas na de eerste kortsluiting geleerd; de valse bevindingen van deze run (wissel 1, 3) moeten bij een nieuwe run niet meer voorkomen. Dinamo-uitval na herhaalde kortsluitingen (17:29) staat nog niet in DINAMO_UITVAL.md.
Test: CompileCheck (ALLES OK, KruisPlusTest met de kaart van 17:50). Nog niet getest op de baan.

## #166 – Proeven resetten motor 13 en geven valse kortsluitingen (16-10, 18:04: "je zit in een loop")
- Log 17:45-18:02: wissel 13 is zes keer als verkeerd gemeld (17:45, 17:49, 17:54, 17:57, 17:59, 18:02). Elke keer eerst "Rijstand hersteld voor de proef: wissel 13 stond afbuigend en moet rechtdoor staan", daarna kortsluiting na melder 24 (15 -> 8 -> 24), toegeschreven aan het proefadres (wissel 1, 3, 4, 7, 8).
- Oorzaak: de rijstand van een opdracht wordt berekend VOOR de basisrit; het voorspelde pad stopt dan op melder 24, dus motor 13 bleef rechtdoor. De basisrit reed wel goed door 24 naar 14 (13 afbuigend, via vooraf zetten of handmatige oplossing), maar `HerstelRijstand` zette 13 voor elke proef terug naar de rijstand. Mijn eerdere fixes (#164, #165) keken alleen naar de basisrit/registratie en hebben dit niet gezien.
- Fix: `KruisStandUitBasis` - reed de basisrit door de kruiswissel (melder met voorganger én opvolger), dan wordt de werkelijke stand van de kruismotoren de rijstand voor alle proeven (behalve motoren die de opdracht zelf vastlegt). Test `KruisStandBasisTest`.
- Nog niet getest op de baan. Een kaart met eerdere valse vondsten (1/3/4/7/8 "8 -> 24 achteruit = kortsluiting") herstelt zich niet; opnieuw beginnen vanaf nul.

## #167 – Plusteken: twee blokken per poot (16-10, 18:18)
- Marco vroeg om per poot van het plusteken twee blokken (met alle bezetmelders in volgorde) te tonen; #165 kapte elke poot af bij de eerste blokwissel.
- Fix: `KruisPlus.Maak` laat elke poot doorlopen tot twee Dinamo-blokken; bij een splitsing staat "daarna X (blok n) of Y (blok m)", een lus naar een ander been wordt benoemd. Test in `KruisPlusTest`.
- Nog niet getest op de baan.

## #168 – Plusteken: bij een splitsing beide takken tonen (16-10, 18:24)
- Marco: ook bij een poot met één blok controleren, en bij een splitsing na het eerste blok beide takken tekenen (daar ontstaan en blijken de problemen).
- Fix: `KruisPlus.Verleng` (gedeelde logica); splitst een poot (west na 5: 16 of 12), dan worden alle takken met hun blokken doorgevolgd en in tekst en html-tekening (oranje takken naast elkaar) getoond. Poten zonder splitsing blijven zoals ze zijn. Test in `KruisPlusTest`.
- Nog niet getest op de baan; de html-tekening is alleen gecontroleerd op opbouw (geen visuele controle hier).

## #169 – Eerste stap bij kortsluiting: wissels die van een tak richting de samenvoeging bereden worden (16-10, 18:35)
- Regel van Marco: een wissel die van de samengevoegde kant (puntzijde) bereden wordt, kan een kortsluiting niet met zijn stand veroorzaken en omzetten helpt niet; een wissel die vanaf een van de gesplitste takken richting de samenvoeging bereden wordt, kan wel kortsluiting geven en omzetten heft die op.
- Eerder: `ProbeerAlarmWegTeKrijgen` probeerde blind alle wissels 1..14 (ruim 50 s, extra kortsluitingen), de wissels bij de loc (de echte verdachten) pas als laatste, en de kruismotoren nooit.
- Fix: `WisselRollen(kaart, melder, richting, reeks)` bepaalt uit de splitsingen van de kaart (en de laatste stap van de rit) welke wissels van achteren (`Achter`) en welke van de samengevoegde kant (`Kop`) bereden worden. Volgorde nu: (1) `Achter`-wissels die niet bij de loc liggen, (2) `Achter`-wissels bij de loc en, als de loc op de kruiswissel staat, de kruismotoren (verdachtste eerst, via `KruisBewijs`) - met een vraag aan de gebruiker, (3) de overige wissels, (4) rest bij de loc met vraag. `Kop`-wissels worden nooit omgezet. Zelfde filter en volgorde in `ProbeerKlemWegTeKrijgen`. Test `WisselRollenTest`.
- Nog niet getest op de baan. Werkt alleen voor wissels waarvan de kaart de splitsing al kent; een nog onbekende wissel blijft in stap 3 aan bod komen.

## #170 – Opdrachten "andere tak van adres N, vanaf melder 14" blijven op wissel 14 hangen (16-10, 19:26)
- Log 18:46-19:24: vijf keer kortsluiting na melder 8 (vooruit) in blok 4, steeds met wissel 14 afbuigend. Elke keer duurde het ~100 s voor de verkenner wissel 14 rechtdoor zette (eerst alle andere wissels geprobeerd, wissel 14 pas na de vraag), en de volgende opdracht (#12, #13, #14, ...) begon weer met wissel 14 afbuigend.
- Oorzaak: een opdracht "andere tak van adres N verkennen (vanaf melder 14, andersom)" erft de configuratie van de ontdekkingsrit (`afbuigend: 14`, nodig voor 15 -> 8 -> 24). De rit vanaf 14 vooruit (14-24-21-29-30-20-27-28-26-25-17-8) heeft wissel 14 niet nodig, maar kwam ermee op melder 8 vanaf 17 aan: een bekende kortsluitingsingang (`VerbodenIngangen`). `VoorspeldPad` keek maar 8 stappen vooruit (stopte bij 26) en `MetVerbodenIngangen` werd voor de rijstand van een opdracht niet gebruikt (alleen voor de proefritten).
- Fix 1: `RijStand` gebruikt het hele bekende pad (`VoorspeldPad` met 30 stappen) en zet wissels die daarop een verboden ingang afbuigend zouden nemen rechtdoor (niet het geteste adres, niet de kruismotoren).
- Fix 2: `WisselRollen` (#169) zet een wissel waarvan de kaart al weet dat afbuigend op precies deze ingang kortsluiting geeft vooraan bij de verdachten, zodat de eerste stap bij de kortsluiting meteen die wissel is (niet pas na ~100 s). Wijzen drie of meer wissels naar dezelfde ingang (de valse waarnemingen 8 -> 24 van wissel 1, 3, 4, 7, 8, 11, 12), dan telt dat niet als verdachte.
- Test `VerbodenPadTest` met de kaart van 19:24. Nog niet getest op de baan.

## BUG #171 (16-10, 19:35-19:55, na "200 versies, rondjes")
- Log 19:41:44-54: kortsluitpunt-proef "adres 1 afbuigend": rit 129 -> 24 met wissel 1 en 5 afbuigend. Motor 10 werd niet vooraf op afbuigend gezet (kortsluiting in blok 8 en 9, Marco moest wissel 10 zelf omzetten), en de proef werd toch als "opgelost" vastgelegd (valse LostKortsluitingOp, valse overgang 129 -> 24 met '1,5').
- Oorzaak 1: `KruisBewijs.Ingangen` zocht het vervolg van 129 alleen bij precies deze wisselstand. Kende de kaart bij {1,5} geen vervolg, dan was er "geen ingang", dus geen kruisbewijs, terwijl 129 -> 24 -> 21 vijf keer met motor 10 afbuigend gereden was. Fix: `ZetKruisVooraf` probeert, als er geen ingang is, `Ingangen(..., ruim: true)` (elk ooit gezien vervolg vanaf die melder in die richting).
- Oorzaak 2: de proef keek alleen of de rit zelf met `Kortsluiting` eindigde; Dinamo meldde de kortsluiting 6 ms na aankomst op 24. Fix: na de proef 1,5 s wachten; is er alarm, dan telt de proef als kortsluiting (geen "opgelost").
- Test `KruisRuimTest` met de kaart van 19:55. Nog niet getest op de baan.
- Open (niet opgelost, eerlijk): de opdracht rijdt de hele ronde tot het bekende kortsluitpunt bij melder 8 meerdere keren (basisrit + eigen rit, ~2,5 min per keer); wissel 14 (8 achteruit: rechtdoor 17 / afbuigend 24, in de opgeschoonde kaart van 10-10 wel bekend) wordt in deze "vanaf nul"-run nog niet geleerd.

## BUG #172 – Nieuwe werkwijze "Stap voor stap" (16-10, 20:26, voorstel van Marco)
- Voorstel van Marco: loc midden in een blok zetten, steeds één melder verder rijden, wissels één voor één omzetten, de nieuwe melder met de wisselstand noteren, en na een kortsluiting op de terugweg wissels proberen.
- Nieuw: keuzevakje "Stap voor stap verkennen" (`VerkenInstellingen.StapVoorStap`), náást de bestaande werkwijze (die blijft ongewijzigd). Code in `BaanVerkenner.StapVoorStap.cs` plus kleine haken in `BaanVerkenner.cs` en `.Rijden.cs`.
- Werking: elke rit is hooguit één melder (`RitDoel.MaxStappen`, einde `StapKlaar`); na elke rit komt op de nieuwe melder de volgende open plek (`PlanVervolg`). De dichtstbijzijnde opdracht gaat voor (`KiesVolgendeOpdracht`); de loc rijdt zelf over eerder gereden stukken naar de plek (`NaarPlekMelder`, `StapWeg`) en blijft anders staan waar hij is. Per wissel wordt een eigen rit gereden; kortsluiting op de terugweg gebruikt de bestaande oplos-stappen (#169).
- Kruiswissel: een melder met vier buren is de kruiswissel (maar één per motorpaar; melder 129 heeft ook vier buren door twee gewone wissels). Elke ingang krijgt een eigen proef van twee melders met stand ∅, motor A, motor B (`VoerKruisProefUit`); de loc staat dan helemaal op de ingang, nooit op de kruiswissel zelf. Een stand zonder doorgaand spoor geeft een kortsluiting en wordt als bewijs genoteerd; daarna rijdt de loc eerst de kruiswissel uit met de motorstand die staat (`VerlaatKruiswissel`). Aanbeveling: geef de Engelse wissel op als `10+13@24`, dan is de kruiswissel vanaf het begin bekend.
- Opgelost onderweg: terugweg na een proef gebruikte de motorstand van de proef (loc ging via 129 i.p.v. 14); kruiswissel-detectie markeerde 129 ten onrechte; wissel 1+2 overloop liep vast doordat de proef wissel 1 telkens terugzette.
- Test in de simulatie (`StapVoorStapTest`, start 17): voltooid na ~260 min virtuele tijd, 21 melders, 3 kortsluitingen (oude werkwijze: 9 kortsluitingen, niet voltooid). Het is traag: ruim 4 uur voor een baan die de oude werkwijze in een uur zou doen als het lukte.
- NOG NIET GETEST OP DE BAAN. Open: groepsproeven (meerdere wissels in één rit) zijn bewust niet gebouwd; de terugrit herhaalt veel; bij start 13 in de simulatie faalt de OUDE werkwijze op "Neerzetten" (niet onderzocht, mogelijk simulatie-artefact).
- #172b (17-10): het keuzevakje stond in het ingeklapte blok "5. Tijden en veiligheid (geavanceerd)" en was daardoor niet te vinden. Nu een eigen, altijd zichtbare groep "Werkwijze" boven blok 5. Nog niet getest op de baan (WPF hier niet te compileren; xml gecontroleerd).

## BUG #173 – Wisselprobleem: info daarna negeren, terug naar een getest stuk baan (17-10, 08:23, log 08:03-08:19)
- Log: 08:15:58 hield Dinamo op met terugmelden (wissel 9, 11), de loc stond stil op melder 14 bij wissel 5. Na het herstel (08:17:36) stuurde de verkenner ALLE wissels opnieuw, óók wissel 5 onder de loc, en reed meteen verder met de onderbroken proef (wissel 11 afbuigend). De loc vertrok 31 s niet. De klemroutine probeerde toen wissels en meldde als VONDST dat wissel 1 de loc vrij maakte. Marco: wissel 1 ligt meters van de loc, dat was toeval/timing.
- Gevolg in de kaart: wissel 1 bleef afbuigend staan en rit 71 kwam met stand "1,11" op 14 -> 24 in de kaart (vervuild).
- Fix 1: na het herstel worden wissels waar de loc op of tegenaan staat niet opnieuw gestuurd (ook niet in dezelfde stand), en de kruismotoren niet als de loc op de kruiswissel staat (`VerwerkBevestiging`, ook het pad "Dinamo reageert niet").
- Fix 2 (alleen 'Stap voor stap'): na een Dinamo-uitval of een klemmende loc gooit de verkenner de lopende opdracht weg (`WisselProbleemFout`). Alles sinds het begin van het probleem vervalt: opdrachten die daarna gepland waren, geteste overgangen, de kaart (was al zo). De loc rijdt naar de dichtstbijzijnde veilige melder (geen kruiswissel, geen bekende wissel erbij, geen kortsluitpunt, eerder gereden met alle wissels rechtdoor; `KiesVeiligePlek`), en de opdracht begint daar opnieuw (`HerstelNaWisselProbleem`). Maximaal 3 keer per opdracht.
- Fix 3: zit de loc klem, dan rijdt hij eerst kort terug het geteste stuk op (`ProbeerTerugNaarGetest`) in plaats van andere wissels uit te proberen.
- Fix 4: een wissel waarvan de kaart niet weet dat hij bij deze plek ligt, wordt niet meer als oplossing vastgelegd als de loc daarna weer rijdt (in 'Stap voor stap' en 15 minuten na een Dinamo-uitval). Hij gaat terug naar zijn oude stand: "vermoedelijk toeval (timing)".
- Tests: `StapUitvalTest` (nieuwe `UitvalSimulatieBaan`: Dinamo voert 75 s lang geen wissels uit; start 17; uitval op commando 60, 300 en 520: telkens herkend, terug naar een veilige melder, verkenning voltooid met 21 melders en 3 kortsluitingen) en `VeiligePlekTest` met de kaart van 08:19 (vanaf melder 14 of 24 terug naar 13).
- NOG NIET GETEST OP DE BAAN. De simulatie kent geen loc die vastzit op een wissel, dus fix 3 en 4 zijn alleen gelezen en gecompileerd, niet in de simulatie doorlopen. Ook onbekend: waarom de loc 31 s niet vertrok; de gok is dat hij op wissel 5 vastzat doordat die onder hem werd gestuurd (fix 1).
- Open vraag: om 08:14:23 werd wissel 5 afbuigend gezet terwijl de loc nog (deels) op melder 14 stond; de kortsluiting daarna is vastgelegd als "wissel 5 afbuigend = kortsluiting 14 -> 24, van achteren bereden". Dat kan door de loc op de tong komen. Niet aangepast; Marco beslist of die conclusie klopt.

## BUG #174 - Dinamo-blokken van alle melders bekend; samengevoegd blok "melder 24" weg (Marco 17-10 08:55)
- Bevestiging van Marco (17-10 08:36) bij #173: wissel 5 afbuigend = kortsluiting als je van 14 naar 24 rijdt. Die conclusie klopt.
- Oorzaak 1: in 'Stap voor stap' stopt een rit op de nieuwe melder, vóór de blokproef. Alleen startmelders kregen hun Dinamo-blok; de rest bleef onbekend en `BlokVoorstel` voegde alle onbekende melders samen tot één blok ("melder 24").
- Fix 1 (`BlokVoorstel`): heeft de kaart blok-info, dan worden melders zonder bekend Dinamo-blok NIET meer samengevoegd (elke onbekende melder is een eigen blok).
- Fix 2 (`PlanBlokProeven`/`VoerBlokProefUit`, `Opdracht.BlokProef`): elke melder zonder Dinamo-blok krijgt een eigen kleine opdracht (erheen met alle wissels rechtdoor, neerzetten, blokproef). Ze draaien pas als er geen ritten meer zijn (een blokproef direct midden in de verkenning liet de loc in de kruiswissel stranden). Zou de weg erheen een wissel omzetten die bij de melder ligt waar de loc nu staat, dan wordt de proef overgeslagen (kortsluitgevaar).
- Fix 3 (kruiswissel-motoren 10 en 13): bij de kruisproeven komt naast "elke motor apart" nu ook de gecombineerde stand (10 en 13 samen afbuigend) aan bod, maar alleen vanaf een ingang met één doorgaand spoor (anders kortsluiting). Het alarm-blokfilter (`RegistreerAlarmOplossing`) legt een oplossing alleen vast bij het blok van het alarm (wissel 5 werd eerder aan oude kortsluitpunten gekoppeld).
- Tests: `OnbekendeBlokkenTest` (kaart van 08:51: elke onbekende melder een eigen blok), `StapVoorStapTest` eist nu dat bijna alle melders (max. 2 zonder) hun Dinamo-blok kennen. In de sim: start 17 voltooid, 21 melders, 3 kortsluitingen, alleen melder 4 en 129 zonder blok; CompileCheck ALLES OK.
- Beperkingen: melder 129 en 4 krijgen in de sim geen blok (de weg erheen is niet veilig). De sim kent de echte baan niet: de eerdere kaart noemde melders 5, 14, 20, 21, 24, 27, 29, 30 zonder blok, dat moet op de baan blijken. De rit duurt langer (313 i.p.v. 262 sim-minuten) door de blokproeven. Ik heb niet kunnen bewijzen dat de ontbrekende standen van motor 10/13 op de baan nu gevonden worden.
- NOG NIET GETEST OP DE BAAN.

## BUG #175 - Eén melder extra terug (aanloop) en doorrijden in de sectie (Marco 17-10 09:44 en 10:03, log 08:03-09:44)
- Marco: "je rijdt maar net 1 melder terug terwijl ik 1 extra terug had gevraagd, daardoor heb je wissel 14 compleet gemist en sta je op wissel 5 te kort op de wissel" en "je stopt heel snel in een meldsectie, ruim voor de wisseltongen, daardoor zie je bijna nooit een kortsluiting als je een wissel naar de samenvoeging toe berijdt".
- Log: de proefritten van 'Stap voor stap' begonnen op de startmelder zelf (08:12 melder 14) en de loc bleef er staan terwijl wissel 1-14 werden omgezet; wissel 5 (vlak bij 14) afbuigend gaf zo 08:14:27 kortsluiting met de loc op de tong. Wissel 14 ligt voor melder 8 (17 -> 8): een rit die op 8 begint komt er nooit langs; in de hele lus is hij nooit gemeten.
- Fix 1 (aanloop, `StapAanloop`, standaard aan, keuzevakje in "Werkwijze"): een opdracht begint één melder eerder (`KiesAanloop`, `Opdracht.Aanloop`): de basisrit is twee melders, de loc rijdt na de proef terug naar die aanloopmelder en staat dus niet op de wissels bij de startmelder. Alleen als: de opdracht met alle wissels rechtdoor rijdt, de aanloopmelder geen eigen wissel heeft, de rit geen eerder gevonden oplossing meedraagt en de startmelder niet de kruiswissel is.
- Fix 2 (doorrijden, `StapDoorrijSeconden`, standaard 1 s, keuzevakje): na het bereiken van de nieuwe melder rijdt de loc nog 1 s door (alarm blijft bewaakt) voordat hij stopt, zodat hij de wisseltongen in die sectie berijdt.
- Fix 3 (`StapWeg`): een stap die zelf gereden is gaat voor de omgekeerde stand van de tegenovergestelde rit. Voorbeeld: 5 -> 12 lukte met wissel 2, 12 -> 5 vraagt 1 en 2; de omgekeerde stand was te klein en de loc bleef op 12 staan.
- Tests: `AanloopTest` (eerste ~30 sim-minuten met aanloop en doorrijden: aanloop wordt gebruikt, wissel 14 wordt gemeten, weinig kortsluitingen). De volledige Stap-run in de sim draait zonder aanloop en doorrijden (voltooid, 21 melders, 3 kortsluitingen).
- EERLIJK: met aanloop en/of doorrijden aan loopt de volledige sim-run vast (aanloop alleen: na 219 min in het herstel bij de kruiswissel; doorrijden alleen: na 120-236 min; beide: na 33 min). Telkens is het herstel na een kortsluiting bij een wissel waar de loc met een deel op staat (8/24, 14/129) de oorzaak: de kortsluiting die Marco nu juist wil zien, wordt in de sim niet netjes opgelost. Op de echte baan onbekend. Beide opties zijn uit te zetten.
- NOG NIET GETEST OP DE BAAN.
- Niet gedaan: alle fouten uit het logboek van 09:44 vergeleken met alle eerdere logs; sectielengtes uit snelheid en bezettijd (was al aangekondigd).

## BUG #176 - Dinamo-blok van een nieuwe melder pas heel laat bekend (Marco 17-10 10:27, log 10:20-10:26)
- Marco: "kan je verklaren waarom van deze melders (5 en 14) de blokken nog niet bekend zijn?"
- Oorzaak: sinds #174 krijgt elke melder zonder blok een eigen blokproef-opdracht, maar die staat bewust achteraan de wachtrij (straf in `KiesVolgendeOpdracht`) omdat vooraan zetten de loc in de simulatie liet stranden. In een lopende verkenning komen ze dus pas aan de beurt als alle ritten klaar zijn. In het log van 10:20-10:26 (6 minuten, tussenstand) waren 14 (10:25:36) en 5 (10:25:56) net gevonden en stond opdracht #4 'Dinamo-blok van melder 14 bepalen' nog te wachten.
- Fix (`BlokProefNaRit`, aangeroepen in `Basisrit` vóór het terugrijden): staat de loc na de basisrit van 'Stap voor stap' op een nieuwe melder zonder blok, dan volgt de blokproef meteen, mits: alle wissels rechtdoor in de rijstand, de melder niet de startmelder of de kruiswissel is, er geen bekende wissel bij die melder ligt, geen buurmelder van de kruiswissel, en de melder minstens twee keer bereikt is (zie hieronder). Daarna zet Nastellen de loc terug (deels op de melder ervoor: verder rijden; deels op een andere buurmelder: terug).
- Fix 2 (`BlokZoekenBijStart`, alleen Stap voor stap): kwam er tijdens de proef een melder bij (de loc staat nu deels op de melder vóór hem), dan kruipt de loc terug in plaats van eerst verder. In het log van 10:20:54 gebeurde dit al: 'kruipen vooruit' op 13/14, daarna pas achteruit; in de simulatie schoot de loc daardoor door tot in de kruiswissel (24).
- Beperking, eerlijk: 'minstens twee keer bereikt' is een noodrem. Zonder die regel (eerste keer meteen) liet de proef bij melder 24 (de kruiswissel, die dan nog niet herkend is) de loc in de simulatie in de verkeerde kruismotor-stand achter en liep de volledige run vast (16 resp. 20 melders). Met de regel is de volledige simulatie groen (21 melders, 3 kortsluitingen, voltooid), maar de blokken komen dan pas bij een volgende rit naar die melder: in de simulatie ongeveer na 1,5 uur voor de eerste, niet binnen enkele minuten.
- Dit lost Marco's geval (5 en 14) dus maar deels op: 14 wordt bereikt door 15 proefritten (telt mee), 5 pas na een tweede rit er naartoe. Een betere herkenning van de kruiswissel vóór de eerste keer zou de regel overbodig maken; niet gedaan.
- NOG NIET GETEST OP DE BAAN.
- Vervolg #176 (Marco 10:50: "alles zo snel mogelijk en betrouwbaar herkennen"), geprobeerd en teruggedraaid: de blokproef-opdracht van een melder die al twee keer bereikt is niet meer achteraan zetten (straf in `KiesVolgendeOpdracht` weg). Resultaat in de simulatie: de volledige run strandde weer (195 min, 18 melders) en de blokken kwamen niet eerder. Niet nog eens proberen zonder eerst het herstel na een kortsluiting bij de kruiswissel (129 -> 24, 24 -> 21) betrouwbaar te maken. Vroeg en betrouwbaar de kruiswissel herkennen kan nu alleen door de melder op te geven (Engelse wissels `13+10@24`); automatisch blijft het 'vier verschillende buren'.

## BUG #177 - Rare tekens in het logboek (Marco 17-10 10:53: "adres 5 rare tekens afbuigend (wisselcommando 27 sinds het verbinden)")
- Oorzaak: de logregel gebruikte een pijl (U+2192) en andere logregels een weglatingsteken (U+2026) en pijlen (9 plaatsen). Die tekens staan in het logvenster (lettertype zonder die tekens) en in sommige editors als blokje of gekke tekens. Het bestand zelf is UTF-8 met BOM.
- Fix: `VerkenLog.Schrijf` vervangt pijlen en het weglatingsteken door gewone tekens ('->', '<-', '<->', '...') voor het logboek (venster en bestand). De wisselregel is nu 'Adres 5: afbuigend (wisselcommando 27 sinds het verbinden)'. Letters als é en ó blijven staan (vooral 'vóór'); in het rapport zijn de pijlen ongewijzigd (het rapport is html/UTF-8).
- Ook hersteld: in #176 stond in een commentaar in BaanVerkenner.Rijden.cs een verkeerd gecodeerde 'ó' (ongeldig UTF-8, 'vóór'); alle .cs-bestanden zijn nu gecontroleerd en geldig UTF-8.
- NOG NIET GETEST OP DE BAAN (het logvenster zelf kan hier niet bekeken worden; de tekst is in de test gecontroleerd).

## BUG #178 - Doorrijden na de melder: zeker 2 seconden (Marco 17-10 11:18)
- Marco: "je rijdt nog niet ver genoeg door na wissel 5; je moet na het zien van de melder nog zeker 2 sec doorrijden voordat je een sluiting kan zien van een wisseltong die je open rijdt."
- Fix: `StapDoorrijSeconden` (BUG #175) staat nu standaard op 2,0 s (was 1,0). In het scherm is het keuzevakje een invoerveld (seconden, 0 = uit, komma of punt, 0 t/m 10), zodat het te verlengen is zonder nieuwe versie. Een bewaarde oude standaardwaarde van 1 s wordt bij het laden 2 s; elke andere bewuste waarde blijft staan.
- Test: de bestaande tests zijn groen (volledige Stap-run in de simulatie met doorrijden uit, `AanloopTest` met de nieuwe standaard 2 s). Een kortsluiting bij een wissel naar de samenvoeging toe is in de simulatie niet nagebootst op de tong; het aantal seconden is dus afgesproken op jouw waarneming, niet gemeten.
- Let op: bij een heel korte sectie kan de loc in 2 s de volgende melder halen; dan stopt hij daar (de rit telt dan twee melders).
- NOG NIET GETEST OP DE BAAN.

## BUG #179 - Doorrijden tot de melder erachter vrij is (Marco 17-10 11:46)
- Marco: "heeft het een reden dat je steeds stopt als er nog 2 bezetmelders actief zijn? Doorrijden totdat de laatste melder niet meer actief is en de nieuwe melder alleen actief is."
- Antwoord: nee, geen inhoudelijke reden. De stap-rit stopte op het eerste bezet-signaal van de nieuwe melder (BUG #172: één melder per rit), later met een vaste doorrijtijd (#175/#178), zonder te kijken of de loc al helemaal in de sectie stond. Met een deel van de loc nog in de vorige sectie heeft hij de wisseltongen van de nieuwe sectie hooguit met de neus bereden.
- Fix (`Rit`, stap-stop): na de doorrijtijd (`StapDoorrijSeconden`, nu minimaal) rijdt de loc door totdat de melder erachter vrij is en alleen de nieuwe melder bezet is; hoogstens 20 s extra. Blijft de melder erachter hangen (of is de sectie korter dan de loc), dan stopt hij na 20 s met een regel in het logboek ('bleef na de doorrijtijd en 20 s extra nog bezet'). Komt er een volgende melder bij, dan geldt de laatste als nieuwe.
- Staat doorrijden uit (0 s), dan stopt de loc zoals voorheen meteen.
- Test: alle bestaande tests groen (21 melders, voltooid, zonder doorrijden). Met doorrijden aan (STAP_DOORRIJ=2) loopt de volledige simulatie na 119 min vast bij 20 van 21 melders: hetzelfde herstelprobleem na een kortsluiting bij de kruiswissel als bij #175 (niet opgelost). 
- NOG NIET GETEST OP DE BAAN.

## BUG #180 - Herstel in de simulatie en blokproef op de eerste melder (Marco 17-10 11:51), NIET OPGELOST, onderzocht en teruggedraaid
- Opdracht: (1) het herstel na een kortsluiting bij de kruiswissel in de simulatie altijd direct oppakken; (2) als de loc na het doorrijden alleen op de nieuwe melder staat, daar meteen de blokproef doen, ook als dat vaker kortsluiting geeft.
- Onderzocht (2): de eerste-keer-regel van #176 ('melder minstens twee keer bereikt') weghalen laat de volledige simulatie weer vastlopen (16 melders, 123 min). Daarom staat die regel er nog in. Dit is geen eigen fout van de blokproef maar het herstel hieronder.
- Onderzocht (1), twee oorzaken die de simulatie laten stranden:
  a. Navigatie 129 -> 14 voor een kruisproef (opdracht #43, motor 10 afbuigend): `NaarStartVan` zet voor de laatste etappe alle standen al vooraf (wissel 5 afbuigend, 6 rechtdoor, motor 10 afbuigend, 14 rechtdoor) terwijl de loc nog op melder 129 staat. Wissel 5 ligt vlak bij 129 en wordt van de samengevoegde kant bereden: direct kortsluiting in blok 9, en omzetten lost dat niet op (CLAUDE.md, BUG #169). Een poging om wissels bij een bezette melder niet vooraf te zetten (maar onderweg, `StaartWissels`) liet de gewone run al na 52 min vastlopen en is teruggedraaid.
  b. Rit 24 -> 21 met motor 13 nog afbuigend van een vorige rit: de loc rijdt naar 14, het terugrijden 14 -> 24 -> 21 gaat de verkeerde kant op (13), de verkenner raakt de loc kwijt. De kruiswissel is op dat moment nog niet herkend (nog geen vier buren), dus de navigatie weet niet dat de motoren er toe doen. Een extra herkenning uit 'twee vervolgmelders afhankelijk van een kruismotor' (`KruisUitMotorBewijs`) werd niet getriggerd, omdat navigatieritten hun wisselstand niet als bewijs in de overgangen zetten; teruggedraaid.
- Volgende stap (niet gedaan): in navigatieritten de werkelijke motorstand als bewijs vastleggen, zodat de kruiswissel met motorbewijs eerder herkend wordt; en de standen voor de laatste etappe pas zetten zodra de loc de melder met die wissels verlaten heeft.

## BUG #181 - #185 (Marco 17-10, 12:01-12:55: standen/melders worden overschreven; loc staat midden in een meldsectie stil; "simulator met ALLE probleemgevallen")
- #181 `KennisRegister` (nieuw): houdt per soort (blok, kruismelder, rijrichting, wissel-onder-loc) bij wat vaststaat en meldt elke overschrijving
  (log "OVERSCHREVEN (BUG #181)" en `Baankaart.Overschrijvingen`). Wissen bij de blokproef telt niet als overschrijving. De regressietest eist 0 overschrijvingen.
- #181 (oorzaak echte log 12:28): een wissel omgezet terwijl de loc er net op aankwam (de tong ligt binnen het gebied van de melder) gaf kortsluiting/stilstand.
  Opgelost met `VrijRijdenVoorWissel` (eerst doorrijden tot alleen die melder bezet is; overgeslagen bij alarm, kruismotor of geen verse aankomst),
  `VrijVanIngangsWissel` met terugvalduur, wissels bij een bezette melder sparen (alleen zonder alarm), `kopHierBekend`, `VertrekWasBewezen` met configuratie,
  "laatste redmiddel" in `ProbeerAlarmWegTeKrijgen`, en `KruisUitMotorBewijs` (motorstand als bewijs bij `DetecteerKruis`).
- #182 een navigatierit volgt de stand die de route nodig heeft; de stand uit een eerder alarmherstel (`MetAlarmStand`) geldt alleen nog voor verkenningsritten.
- #183 `VrijRijdenVoorWissel` kruipt alleen door voor een wissel ACHTER de loc (waar hij net overheen reed), niet voor een wissel VOOR de loc in dezelfde sectie
  (129: wissel 9 achter, wissel 6 voor; het kruipen voor wissel 5 zette de loc op de tong van wissel 6). Via `WisselLigtAchter`.
- #184 na een adrestest blijven de proefwissels en de bekende oplossingswissel staan als de loc op een melder staat waar die wissel ligt (`ZetWisselNaProef`);
  eerder werd wissel 1 bij melder 12 eerst rechtgezet en bij de terugweg weer afbuigend: twee keer omzetten onder de loc, elke keer kortsluiting (36 s herstel, 9 keer per ronde).
- #185 een kruisproef (alleen de twee motoren getest) telde als "alle adressen getest" voor die overgang. Daardoor werd de adrestest van 129 -> 24 overgeslagen,
  wissel 9 (afbuigend naar 4 - 11 - 12) nooit gevonden en meldde de verkenning "voltooid" met melder 4 en 11 ontbrekend. Niet meer als getest gemarkeerd.
- Simulator: (1) strenge buurwissel-regel (BUG #169: omzetten heft de kortsluiting op de kruiswissel op) is nu STANDAARD (`BuurwisselHeftKruisKortsluitingOp`);
  de gewone variant blijft testbaar; (2) de gesimuleerde gebruiker zet de loc nu ECHT terug op de gevraagde melder (`ZetLocOpMelder`); eerder bleef de loc staan waar hij stond
  en liep elke test na "zet de loc terug" vast; (3) nieuwe tests `KennisRegisterTest`, `SimBuurwisselTest`, `StapMatrixTest`; de tests draaien nu op Marco's echte instelling
  (doorrijden 2 s + aanloop). Resultaat start 17 en 5, beide simulatiemodi: alle 21 melders, wissel 9 gevonden, 0 overschrijvingen, 7-9 kortsluitingen (kruismotoren geven er inherent enkele).
- NOG OPEN (door de matrix gemeten, `MATRIX_START=13,30,129,12,4,10 dotnet run --project Tools/CompileCheck`):
  * start op de binnenring (melder 4, 11, 12) of het zijspoor (melder 10): de verkenning meldt "voltooid" met 2-6 van de 21 melders. De eerste rit van 4 naar 129 vraagt wissel 9
    afbuigend; de oplossing van een kortsluiting tijdens de TERUGWEG werd niet als kortsluitpunt vastgelegd, dus elke volgende terugweg herhaalt het herstel (12 keer in één opdracht).
    Een poging om het vervolg na een opgelost punt altijd in te plannen en om na het herstel door te rijden tot één melder bezet is, verbeterde start 12 (4 -> 6 melders) maar maakte start 5 slechter;
    teruggedraaid.
  * start 13: 18-20 van 21 melders; start 30 en 129: te veel kortsluitingen (14-20).
  * "voltooid" wordt gemeld terwijl er nog onbekend spoor is: de afronding moet kloppen met het aantal ontdekte uiteinden.
- Alles hierboven: nog niet getest op de baan.

## BUG #187 – Blokproef pas bij tweede bezoek van een nieuwe melder (17-10, echte log 14:06–14:16, melder 13/5 en 14/24)
Marco: bij een onbekende melder volledig doorrijden tot alleen die melder bezet is en dan direct de blokkentest doen. De code wachtte met de blokproef tot de melder twee keer bereikt was (regel uit #176), waardoor de adrestests van de overgang 13→14 tientallen keren werden herhaald vóór melder 14 een blok had.
Oplossing: tweede-bezoekregel in `BlokProefNaRit` verwijderd; `Nastellen` zet de loc eerst helemaal op de nieuwe melder, daarna volgt direct `BlokZoekenBijStart`. Test in `StapMatrixTest`. CompileCheck: ALLES OK (start 17 en 5, beide simulatiemodi, start 13 voltooid).
NOG NIET GETEST OP DE BAAN.
Aanvulling: de blokproef start alleen als uitsluitend de nieuwe melder bezet is; anders wordt hij uitgesteld met een waarschuwing in het logboek. NOG NIET GETEST OP DE BAAN.

## NOG OPEN voor de volgende versie (Marco 17-10 15:03, nu nog geen actie)
- Wisselstanden gelden in beide richtingen: heeft de kaart 5→12 (achteruit) via wissel 2 (en 1) afbuigend, dan moet de terugrit 12→5 (vooruit) dezelfde wisselstand krijgen en in het blokkenschema/de wisselstraten staan. Nu staat de stand alleen bij de heenrichting. Bij de volgende versie direct meenemen.

## BUG #188 – ANALYSE 14:53-log (17-10), NOG NIET OPGELOST (niet geleverd, omdat de simulatie ervan terugviel)
Wat de log laat zien (Marco 14:56/14:58):
1. Melder 12 (na wissel 2 afbuigend, achteruit vanaf 5) krijgt geen blokproef: de loc stopt met melder 5 nog actief, krijgt kortsluiting, het herstel (wissel 1 afbuigend) laat de loc "staan waar hij staat" (op 12 en 5 samen) en er volgt geen doorrijden tot alleen melder 12 bezet is, dus ook geen blokproef. Daarna wordt de overgang 5→12 nog 13 keer opnieuw getest voordat melder 12 een blok heeft.
2. Dat wissel 1 mee afbuigend moet bij wissel 2 werd om 14:44:16 gevonden maar pas om 14:49:50 bij het kortsluitpunt vastgelegd (het punt bestaat pas na de rit); de rit van 14:48:47 reed daardoor weer in dezelfde kortsluiting.
Geprobeerde oplossingen (allemaal teruggedraaid): (a) na herstel doorrijden + blokproef in HerstelNaKortsluiting; (b) wachtende zelf-gevonden alarmoplossing koppelen aan het kortsluitpunt zodra het bestaat; (c) Splitsing meetellen in VrijVanIngangsWissel; (d) wissel na afwijkende proef niet onder de loc terugzetten. Elk gaf in de simulatie valse wissels (3,4,7,8...) of ontbrekende melders bij start 5/13/17; zonder deze wijzigingen slagen start 17 en 5.
Oorzaak van de valse wissels (nog te verhelpen): na TerugNaarMetControle staat de loc op melder 5 met zijn staart nog op de tong van wissel 2; wissel 2 wordt dan direct teruggezet (kaart kent hem nog niet, WisselsBijBezet leeg), blijft in de sim onder de loc staan en de volgende proef (wissel 3) rijdt daardoor naar melder 12 en krijgt de eer. Zonder eerst deze wisselvolgorde te herstellen is (a)/(b) niet veilig in te voeren.

## BUG #189 – Verkenning pas "voltooid" als elk Dinamo-blok bekend is (17-10)
Meting (BENCH=1, 16 simulatieruns): 8 runs meldden ten onrechte "voltooid" terwijl melders nog geen Dinamo-blok hadden; na de wijziging 1. Een verkenning waarvan een melder geen blok heeft, eindigt nu als "Gestopt (tussenstand)" met een waarschuwing in de kaart. Nog niet getest op de baan.

## BUG #191 – Proefwissel pas terugzetten nadat de loc ervoor is doorgereden (17-10)
Na een afwijkende proef werd de proefwissel onder de staart van de loc teruggezet (valse wissels 3, 4, 7, 8, 11, 12 in de simulatie). `_bekendBijBezet` laat `VrijRijdenVoorWissel` eerst doorrijden. Meting: neutraal (geen verslechtering). Nog niet getest op de baan.

## BUG #190 – verworpen op basis van meting (17-10)
Voorgestelde wijziging verslechterde de BENCH-totalen en is niet doorgevoerd.

## BUG #192 – Wissel 1 bleef rechtdoor bij de aankomst op melder 12 terwijl wissel 2 afbuigend moest (Marco 17-10, log 16:01)
Oorzaak: regel #146 (`AankomstZonderKortsluitWissels`) hield de oplossingspartner (wissel 1) rechtdoor tijdens de aankomst, ook als wissel 2 voor de route nodig was. Dat is precies de stand (2 afbuigend zonder 1) die de kortsluiting gaf: de loc zat vast tussen wissel 1 en 2, de blokproef voor melder 12 faalde (blok onbekend) en de binnencirkel werd nooit bereikt.
Oplossing: is een wissel van het kortsluitpunt zelf nodig voor de route, dan blijft zijn partner ook afbuigend (wissel 1 samen met wissel 2). Bewijs: test op de echte kaart van 16:01 (5 -> 12 houdt 1 en 2 afbuigend); BENCH ongewijzigd. Nog niet getest op de baan.
Nog open: de verkenner zette na de "oplossing" (VONDST 15:59:34) wissel 1 meteen weer terug; zie BUG #188 (onthouden van de oplossing + doorrijden + blokproef).

## BUG #193 – Kruiswissel (motor 10 en 13) niet ontdekt (Marco 17-10 16:09 en 16:28)
Oorzaak: kruiswissel werd alleen herkend bij 4 buren of als een motor was omgezet; in stap-voor-stap werden de motoren overgeslagen zolang de kruiswissel onbekend was (kip-en-ei). Melder 24 had 3 buren (14, 21, 8), het vierde been (129, de binnencirkel) is alleen met motor 10 afbuigend bereikbaar. Daardoor bleef de hele binnencirkel (blok 1 en 2) onbezocht.
Oplossing: herkenning door uitsluiting (`KruisUitUitsluiting`): motoren opgegeven, wachtrij leeg en precies één melder met drie of meer buren die geen gevonden wissel verklaart; daarna krijgt die melder de gewone kruisproeven (motor 10 en 13 per ingang). Test op de echte kaart van 16:01 geeft melder 24.
Marco (16:28) vroeg de motoren gewoon mee te schakelen in de testrun. Dat is geprobeerd (motoren niet overslaan in de adrestest zolang de kruiswissel onbekend is) en GEMETEN: de eigen regressietests faalden (start 5: verkenning rondt niet af, 20 van 21 melders; start 17: 11 kortsluitingen boven de grens). Daarom NIET doorgevoerd; de motoren komen via de uitsluiting aan bod. Nog niet getest op de baan.
Meting BENCH (16 runs) met alleen de uitsluiting: melders 208 -> 210; valse wissels 4 -> 4; kortsluitingen 118 -> 127 (de motorproeven zelf); alle regressietests OK.
Blokkenschema: gevonden wissels worden als gele blokjes (W2, W14, ...) in het blok getekend (test op kaart 16:01).

## BUG #194 – Wissel 1 samen met wissel 2: de oplossing blijft staan (Marco 17-10 16:48, log 16:01)
Wat er in het log gebeurde: na de kortsluiting zette het herstel wissel 2 terug naar rechtdoor (klem-ingreep), wissel 1 afbuigend haalde het alarm weg (15:59:34.683) en 1 ms later zette de verkenner wissel 1 weer op rechtdoor. Het neerzetten en de blokproef reden dus met 1 en 2 rechtdoor; de kortsluiting kwam terug en de loc bleef staan.
Oplossing (naast #192, dat de aankomst met 1+2 afbuigend regelt): (1) de proefwissel die het herstel terugzette (wissel 2) gaat terug naar afbuigend zodra een partner (wissel 1) het alarm wegnam; blijft het alarm weg, dan vormen ze samen de oplossing, komt het terug dan blijft wissel 2 rechtdoor; (2) een stand die net het alarm wegnam wordt niet teruggezet (ook niet door `HerstelRijstand`/herstel na de proef) zolang de loc nog op die melders staat.
Meting BENCH: melders 210, kortsluitingen 127, valse wissels 4 ongewijzigd; "wissel omgezet onder de loc" 99 -> 137 (het terugzetten van wissel 2 gebeurt per definitie met de loc erbij). Regressietests OK. Nog niet getest op de baan.

## BUG #195 – Optie 6 "Rondrijden" (Marco 17-10 18:06: "we gooien het over een andere boeg")
Opdracht: ontdekken waar de loc staat, in één richting rijden met rijcommando naar alle blokken, alleen de volgorde van de melders noteren (nog geen bloknummers), bij een kopspoor keren, bij een lus of heel pendeltraject één wisselmotor omzetten en opnieuw rijden tot kortsluiting (wissel van achteren) of een nieuwe lus; kruiswissel als gewone wissel; gedetailleerd log; pas daarna blokken zoeken. Optie 6 in het scherm, optie 5 gaat dan uit.
Gebouwd op de bestaande rit-, lus-, kopspoor- en kortsluitmachinerie (de gewone werkwijze), met: `VerkenInstellingen.Rondrit`; geen blokproeven en geen blokcontrole tijdens ritten (`ProefMogelijk` is onwaar tot de blokfase); `Kruis` leeg (motoren 10 en 13 zijn gewone adressen); `ExtraMeldersNaAfwijking` = `MaxMeldersPerRit` (na een andere melder doorrijden tot lus, kopspoor of kortsluiting); `RondritBlokkenZoeken` (nieuw bestand `BaanVerkenner.Rondrit.cs`) zoekt aan het eind per melder het blok; scherm: checkbox 6, wederzijds uitgesloten met 5.
Gevonden en opgelost tijdens de meting: een navigatie die haar doel niet haalde reed eindeloos rondjes (600 minuten in de simulatie); nu stopt ze na 2x het aantal melders met een NavigatieFout. Verkenning telt als niet af als opdrachten zijn overgeslagen en er geen wissel is gevonden.
Meting (Miniworld-simulatie, gewone en strenge sim, `RONDRIT=1`): start 17/5/13/30: alle 21 melders; start 129: 20; wissels 2,5,6,9,10,13,14 gevonden (30: één valse wissel 4); start 5 volledig voltooid met alle 21 blokken (245 min simulatietijd, 6 kortsluitingen). Ter vergelijking stap voor stap: gemiddeld 13 van 21 melders per run (208/16).
BEKENDE BEPERKING: start op het zijspoor (melder 12, 4, 10 in de simulatie) vindt maar 3-5 melders; de loc komt er niet doorheen en keert na drie mislukte pogingen. De verkenning meldt dan 'nog NIET af'. De werkwijze keert nu altijd terug naar de startmelder na een opdracht; Marco's wens (steeds doorrijden vanaf waar de loc staat) is hier niet volledig gebouwd. Wissel 1 (overloop) wordt in de simulatie niet gevonden. Het WPF-scherm kon hier niet gecompileerd worden. Nog niet getest op de baan.

## BUG #196 – Optie 6: basisrit dubbel gereden en verouderde stops (log 17-10 20:05, Marco: "het duurt nogal lang")
Uit het log van de eerste echte run van optie 6 (1 u 41 min, 66 ritten, 19 melders): elke basisrit werd twee keer gereden (rit #1 en #2, #6 en #7, ...), met een derde als scheidsrechter wanneer de ritten verschilden; dat was een kwart van alle ritten. Bij optie 5 (stap voor stap) was dat al uitgezet (BUG #172), bij optie 6 niet.
Verder werden de stops bij bekende kortsluitpunten alleen aan het begin van `Basisrit` berekend. Werd kortsluitpunt #3 tijdens de eerste rit opgelost (wissel 5 afbuigend), dan stopte de volgende rit met 5 en 10 afbuigend nog steeds op melder 14 ("gestopt op het bekende kortsluitpunt") en volgde nog een scheidsrechter-rit (rit #63, #65 en #67). Dat leek op een lus, maar eindigde na de scheidsrechter-rit; mijn eerdere analyse ("vrijwel zeker een lus") was te stellig.
Oplossing: optie 6 rijdt de basisrit één keer; de stops worden voor elke rit opnieuw uitgerekend. Nog niet getest op de baan.

## BUG #197 – Wissel 5 stond na de verkeerde melder; twee valse "onverwachte terugwegen" (log 17-10 20:05)
(1) Het rapport had "wissel 5: na melder 13 rechtdoor -> 14, afbuigend -> 14". Uit rit #54 blijkt dat 13 -> 14 voor beide standen gelijk is en de splitsing pas na melder 14 ligt (rechtdoor 24, afbuigend 129). De "andere kant"-opdracht zocht de splitsing altijd direct na de startmelder. Nu (`ZoekAndereKantSplitsing`) wordt bij een gelijke eerste melder de rit zonder de wissel (zelfde start en richting) erbij gehaald en de eerste afwijking genomen; is er niets te vinden, dan komt er geen splitsing "14 -> 14" in de kaart. Gevolg: ook de wissels die vooruit gezet worden (BUG #107/#108, wissel 10 voor de stap 129 -> 24) krijgen de juiste plek; in het log werd wissel 10 om 19:51:51 pas op melder 129 omgezet, 2,2 s voor de kortsluiting.
(2) Open punt #1 ("heen 16 naar 5, terug 5 naar 16, niet terug naar 16"): de loc ging gewoon terug naar de melder waar hij vandaan kwam; het verschil zat in de verwachte route van de navigatie (wissel 2). Dat telt niet meer als onverwachte terugweg.
(3) Open punt #4 (heen 8 naar 24, terug 24 naar 21): melder 24 heeft drie of meer buren (kruiswissel); daar hoeft de terugweg niet langs de heenweg te gaan. De overgang 8 -> 24 blijft in de kaart staan (met wissel 14 afbuigend) en er komt geen kortsluitpunt voor.
Nog niet getest op de baan.

## BUG #198 – Kortsluiting bij een stilstaande loc: eerst de bekende oplossing (log 17-10 19:52-19:56)
De loc stond met melder 24 en 129 bezet in een kortsluiting. De verkenner probeerde wissels in numerieke volgorde (1, 2, ...) en noemde wissel 1, 2 en 14 achtereenvolgens "oplossing" (het alarm verdween steeds tijdelijk), terwijl kortsluitpunt #3 al bewees dat wissel 5 afbuigend de oplossing is. Dat kostte ruim 4 minuten en liet wissel 1, 2, 5 en 14 afbuigend staan voor de volgende rit.
Oplossing: (1) eerst de oplossing van een opgelost kortsluitpunt waarvan de melder (of tweede melder) bezet is en waarvan de stand klopt; (2) de overige wissels in de volgorde: dichtbij de bezette melders (of hun buren), onbekende plek, daarna wissels waarvan de kaart de plek elders kent. Nog niet getest op de baan.

## BUG #199 – Melderschema, en "adressen zonder effect" ook in een tussenstand (Marco 17-10 20:14)
Nieuw: `Melderschema` (alle melders als punten, gereden overgangen als lijnen, wissel en stand bij de lijn, melders met drie of meer buren oranje, het Dinamo-blok eronder als dat bekend is). In het HTML-rapport onder het blokkenschema en als venster ("Melderschema tonen" in het scherm, WPF-venster niet gecompileerd hier). De plaatsing is deterministisch (ring van de langste rit + korte krachtsimulatie).
Het rapport van 20:05 meldde "0 adressen zonder effect" omdat die lijst pas bij afronden werd berekend; nu bij elke tussenstand, met de tekst dat sommige adressen misschien nog niet getest zijn. Nog niet getest op de baan.

## BUG #200 – OPGELOST (nog niet getest op de baan): loc schiet door naar melder 21 bij het neerzetten op melder 24 (log 17-10 19:59, 20:03, 20:04)
Bij elke terugkeer naar melder 24 ("Neerzetten op melder 24") schiet de loc door naar 21, waarna "doorgeschoten, terug naar melder 24" volgt: 20 tot 30 seconden per keer, en bij het kruipen terug staat de loc een tijd deels in twee melders. Nog niet opgelost; vraagt een kortere remweg of een neerzetmelder vóór 24 voor deze ingang.
Oplossing: de verkenner onthoudt een melder waar de loc bij het neerzetten doorschoot (`_doorschietMelders`); een volgende navigatierit er naartoe gaat al op de melder ervoor op kruipsnelheid. Leert dus per melder, zonder vaste instelling. Pas na de eerste keer doorschieten actief. Nog niet getest op de baan.

## BUG #201 – OPGELOST (nog niet getest op de baan): wissels zonder effect worden in elke richting en combinatie opnieuw gereden (log 17-10, rit #13-20, #38-50, #54-57)
Wissel 3, 4, 7, 8, 9 en 12 deden volgens de ritten niets, maar kostten elk twee tot drie volledige rondes van circa 1 min 45 s (20 tot 30 minuten). Gevolgd Marco's regel "één wisselmotor tegelijk" is dat nu zo bedoeld; versnellen kan door adressen te groeperen (meerdere tegelijk omzetten, bij verschil halveren) of door een adres over te slaan dat al een volledige lus zonder effect had. Beide wijken af van die regel: eerst Marco vragen.
Oplossing: `RouteAlGedekt`: een adres wordt in een proef overgeslagen als dezelfde reeks (of omgekeerd) al eens betrouwbaar met alleen dat adres afbuigend is gereden en gelijk aan de basis bleef (geen waarneming, niet de kruismotoren). Eerste, grovere versie (alleen randen) brak de simulatie (wissel 9 niet gevonden) en is vervangen. Nog niet getest op de baan.

## BUG #202 – OPGELOST (nog niet getest op de baan): wisselstanden gelden in beide richtingen (Marco 17-10, zie ook #188)
Staat 5 -> 12 vast met wissel 2 (en 1) afbuigend, dan moet 12 -> 5 dezelfde stand krijgen, ook in blokkenschema en wisselstraten. In het rapport van 20:05 staat 12 -> 5 nog met "wissel 2 afbuigend (uit splitsing), wissel 4 afbuigend (aangenomen)" en 5 -> 12 met "wissel 1 afbuigend (bewezen)": de twee richtingen zeggen verschillende dingen. Nog niet gebouwd.
Oplossing: `WisselstraatVoorstel.MaakSymmetrisch`: bij precies één voorstel per richting krijgen beide dezelfde bewezen eisen (kortsluiting gaat voor splitsing, dan overloop); niet-bewezen 'aangenomen' eisen die de andere richting niet kent vervallen. Test op de kaart van 11:46. Nog niet getest op de baan.

## BUG #203 – OPEN: optie 6 rijdt na elke opdracht terug naar de startmelder (Marco 17-10 18:06)
Marco wil steeds doorrijden vanaf waar de loc staat. De opdrachtwachtrij kiest nu de dichtstbijzijnde opdracht, maar de navigatie en de terugweg (`TerugNaarMetControle`) brengen de loc nog terug naar de start van de opdracht. Nog niet gebouwd.
Stand 18-10: onderzocht, bewust niet gebouwd. Optie 6 gebruikt de klassieke wachtrij (elke opdracht begint vanaf de startmelder van de kaart, `TerugNaarHuis`); de 'loc blijft waar hij staat'-weg bestaat alleen in stap voor stap (`Stap`: `KiesVolgendeOpdracht`, `HuidigePlek`, navigatie vanaf elke plek) en optie 6 sluit die uit (#195). Doorrijden vanaf de huidige plek in optie 6 vraagt dat die navigatie ook in de klassieke opdrachtstart komt; dat is een grotere ombouw die ik niet zonder baantest durf te doen. Voorstel: eerst de optie 6-ritten van de laatste run bekijken. Blijft OPEN.

## BUG #204 – OPGELOST (nog niet getest op de baan): valse "oplossingen" bij een kortsluiting met stilstaande loc kunnen in de kaart terechtkomen (log 17-10 19:53-19:56)
BUG #198 laat de verkenner eerst de bekende oplossing proberen, maar `Probeer` blijft elke wissel die het alarm 2,5 s wegneemt als oplossing vastleggen (`RegistreerAlarmOplossing`). Bij een terugkerend alarm (Dinamo zet de stroom steeds even terug) is dat onbetrouwbaar: wissel 1, 2 en 14 werden zo "oplossing" en bleven afbuigend staan. Nog te doen: een oplossing pas vastleggen als de loc daarna werkelijk weg kan rijden, en bij een volgend alarm met die stand weer intrekken (zoals `WeerlegAankomstOplossing` doet bij aankomst).
Oplossing: oplossingen uit alleen het alarm zijn `Voorlopig` en worden na elke rit en bij Afronden aan de ritten getoetst (`ToetsVoorlopigeOplossingen`): bevestigd, vervangen of ingetrokken. Nog niet getest op de baan.

## BUG #205 – OPEN: wissel 7 en 6 aan de 129-kant en de kortsluiting van 19:51 zijn nog niet goed uit elkaar gehaald
De kortsluiting na melder 129 (rit met 5, 7, 10 afbuigend) kwam door wissel 10 die 2,2 s ervoor onder de loc werd omgezet (opgelost via de splitsing van wissel 5, BUG #197), maar de verkenner schreef hem aan wissel 7 toe en sloeg de proef over. Controleren in de volgende echte run of wissel 7 (en 6: afbuigend gaf doodlopend zonder melder) nu goed gevonden worden; anders de proef met 7 afbuigend en 10 al voor melder 129 omgezet herhalen.
Stand 18-10: oorzaken zijn met #208, #210, #213 en #217 weggenomen (valse toeschrijving aan wissel 6/7/11 via achtergebleven stand of alarm-ruis). Bevestiging kan alleen met een echte run; blijft OPEN tot de volgende run laat zien dat 7 en 6 goed gevonden worden.

## BUG #206 – OPGELOST (nog niet getest op de baan): tussenstand na Dinamo-uitval haalt bevindingen uit de kaart (log 19:21, 19:54, 19:56)
Na "geen terugmelding van Dinamo" worden alle bevindingen sinds de eerste ontbrekende terugmelding verwijderd. Dat is veilig, maar kostte rit #13 tot en met #28 (bijna 10 minuten) opnieuw. Nog te doen: de wissel- en melderbevindingen waarvan de terugmelding wel kwam behouden, of na het herstel alleen de opdracht opnieuw rijden waarin de uitval zat.
Oplossing (deels): na het terugzetten van de kaart blijven melders en hun Dinamo-blok behouden (die komen uit bezetmeldingen, niet uit wisselcommando's). Wissel- en overgangsbevindingen sinds de uitval blijven bewust weg, want zonder terugmelding is niet te bewijzen dat de wissel omging. Nog niet getest op de baan.

## BUG #207 – OPGELOST (nog niet getest op de baan): rapport toont bij een overgang alle geteste wisselstanden in plaats van de wissels die er echt toe doen (Marco 18-10 08:26)
Marco: "soms vind je bij de overgang van melder x naar y andere wisselstanden, maar fysiek verandert er niets." De kaart (Overgangen.GezienBijConfiguraties en het rapportdeel "Doorgangen") bewaart voor elke overgang alle rijstanden waarin hij gereden is, ook wissels die niet op dat stuk liggen (melder 5 vooruit vanaf 16 -> 13: zeventien "standen", terwijl geen enkele van belang is). Dat leest alsof de baan verandert. Nog te doen: per overgang alleen de wissels tonen die er aantoonbaar toe doen (uit splitsingen en kortsluitingen), de rest als "geen invloed gezien", en per richting (van de puntzijde of van een tak) aangeven wat nodig is. Hangt samen met #202.
Oplossing: het rapport (html en tekst) toont per doorgang alleen de minimale rijstanden (`Rapport.DoorgangStanden`); is de kleinste stand de basisstand, dan staat er 'geen wissel van invloed gezien'. Nog niet getest op de baan.

## BUG #208 – OPGELOST (nog niet getest op de baan): valse vondst "wissel 11" door een achtergebleven wisselstand (Marco 18-10 08:31)
Marco: wissel 11 bestaat fysiek niet. Log 17-10: 19:02:41 wissel 5 werd als klemoplossing afbuigend gezet ("blijft zo staan"); 19:06 reed opdracht "vanaf melder 13 achteruit (afbuigend: 11)" met wissel 5 nog afbuigend; 24 -> 14 gaf daardoor kortsluiting (wissel 5 afbuigend zonder wissel 10 afbuigend). Het alarm verdween pas toen wissel 5 rechtdoor ging, niet bij wissel 11, maar het programma schreef de kortsluiting toch aan 11 toe (19:08:39 "WISSEL gevonden - adres 11").
Oplossing: `HerstelRijstand` onthoudt nu welke wissels niet naar de rijstand teruggezet konden worden (klemoplossing of loc staat erbij) in `_proefAfwijkend`. Geeft een proef dan kortsluiting, dan wordt die niet aan het proefadres toegeschreven (waarschuwing in het log), en wordt het adres later opnieuw getest. CompileCheck-suite: ALLES OK.
Nog open: (1) een regressietest met een gesimuleerde rit voor dit geval ontbreekt (de engine is hier alleen via de kaarttests te toetsen); (2) de al bestaande valse waarneming "wissel 11" in kaart 17-10 20:05 en de bijbehorende vervolgopdracht moeten bij het volgende laden worden verwijderd. Hangt samen met #194 en #204.

## BUG #209 – OPGELOST (nog niet getest op de baan): proef start terwijl een klemoplossing-wissel niet naar de rijstand terug kon (Marco 18-10 09:18, log 09:11-09:15)
Na #208 wordt wissel 11 niet meer ten onrechte gevonden, maar de oorzaak blijft: wissel 5 stond sinds 09:11:21 afbuigend (klemoplossing) en `HerstelRijstand` liet hem staan omdat de loc na het neerzetten nog deels op melder 14 stond (daar ligt wissel 5). De proef met adres 11 (09:13:24) werd toch gereden en gaf weer kortsluiting; pas 09:15:13 ging wissel 5 rechtdoor. Gevolg: de proef is verspild en er komen configuraties als "5,11" en "5,10" in Overgangen/Doorgangen.
Te doen: (1) als `_proefAfwijkend` niet leeg is, de proef niet rijden maar eerst de loc helemaal van de betreffende melder af laten rijden, `HerstelRijstand` herhalen en pas dan de proef starten; (2) configuraties met een afwijkende wissel niet in `GezienBijConfiguraties`/Doorgangen opnemen; (3) regressietest met simulatie. Nog niet getest op de baan. Hangt samen met #194, #204 en #208.

**Oplossing (18-10):** (1) staat een klemoplossing-wissel nog onder de loc, dan zet de verkenner de loc eerst op de startmelder (`Nastellen`) en herstelt de rijstand opnieuw; lukt dat niet, dan gaat de proef door met een waarschuwing, maar #208 schrijft een kortsluiting niet aan het proefadres toe (de proef overslaan liet in de simulatie adressen ongetest, dus dat is bewust niet gedaan). (2) Opruimstap voor adressen die niet bestaan: optionele instelling `NietBestaandeWissels` (standaard LEEG, Marco 18-10: niet hard coderen, het programma moet zelflerend blijven; wissels 3, 4, 7, 8, 11, 12 zijn alleen als analysekennis bekend): zulke adressen worden niet meer getest of omgezet, en bij het laden van een kaart verwijderd (wisselslijst, '5,11' wordt '5', doorgangen, kortsluitpunten, oplossingen, ritten; wisselstraten worden opnieuw berekend). Getest met de echte kaart van 18-10 09:50 in CompileCheck (ALLES OK). Niet gedaan: configuraties met een afwijkende wissel buiten `GezienBijConfiguraties` houden.

## BUG #210 – OPGELOST (nog niet getest op de baan): valse wissel 11 na een Dinamo-uitval doordat de loc niet wegreed (Marco 18-10 09:38)
Log 18-10: 09:36:08 wisselcommando's kregen geen terugmelding (Dinamo-uitval), 09:36:35 Dinamo hersteld, 09:36:44 rit vooruit vanaf melder 13 met wissel 11 afbuigend. Het hardwarelog laat zien dat de loc 31 s lang niet wegreed (geen enkele melder-melding, daarna STOP). Omdat de blokcontrole bij "loc vertrok niet" in optie 6 (Rondrit) wordt overgeslagen, werd het resultaat "doodlopend" en dus "WISSEL gevonden - adres 11: afbuigend -> geen melder bereikt" met twee vervolgopdrachten (#7, #8). Wissel 11 bestaat fysiek niet.
Oplossing: vertrekt de loc niet van de startmelder (alleen die melder in de reeks) binnen 15 minuten na het herstel van een Dinamo-uitval, dan wordt dat niet als doodlopend geregistreerd maar als NavigatieFout: de opdracht telt niet en wordt (één keer) opnieuw gereden. CompileCheck-suite: ALLES OK; een gesimuleerde rit voor dit geval ontbreekt nog (de simulatie kent geen Dinamo-uitval met een loc die niet wegrijdt).
Nog open: de al opgeslagen valse waarneming "wissel 11" (en "5,11" in de doorgangen) staat nog in kaart 09:37 en moet met de hand of bij het laden verwijderd worden. Hangt samen met #208, #209 en #173.

## BUG #211 – OPGELOST (nog niet getest op de baan): partnerzoektocht na een kortsluiting registreert "oplossingen" die niets bewijzen (log 09:20)
Na de kortsluiting bij melder 24 (09:19:45) ging het alarm weg door wissel 13 (09:20:29). Daarna werden 11 en 14 "als partner" afbuigend gezet met de melding "het alarm blijft weg - samen vormen ze de oplossing" (09:20:34 en 09:20:39). Het alarm was al weg, dus elk adres (ook een niet-bestaand adres als 11) lijkt dan een partner. Te doen: een partner alleen noemen als het alarm door het omzetten van dat partneradres wegging of terugkwam, en nooit een adres tot partner maken dat nog nooit een effect liet zien. Hangt samen met #204.
Oplossing: `PartnerwisselZoeken` zet vooraf afwijkende wissels terug of stopt (hardening); naast #213 en #208. Nog niet getest op de baan.

## BUG #212 – OPGELOST (nog niet getest op de baan): wissel 14 ontbreekt in de wisselslijst terwijl de overgang 8 -> 24 bij wissel 14 afbuigend is gereden (kaart 09:37)
Kaart 18-10 09:37: Overgangen bevat 8 -> 24 (achteruit, afbuigend: 14), melder 24 heeft in het rapport vier buren (8, 14, 21, 129), maar Wissels bevat alleen 2, 5, 10, 11 en 13, en adres 14 staat bij "adressen zonder gevonden effect". De oplossing "adres 14 én 12 samen op afbuigend (overloopwissels)" van 09:21:56 is niet als wissel vastgelegd, en dat is terecht: zij klopt niet (zie #213, het alarm ging weg door wissel 13). Dit punt blijft staan voor de vraag of 14 een eigen waarneming moet krijgen (8 -> 24 met 14 afbuigend). Te doen: uitzoeken waarom die oplossing geen wisselwaarneming en geen kortsluitpunt opleverde, en 14 (met 12 als partner indien bewezen) vastleggen. Let op: eerst nagaan of 12 echt bestaat (zie #211).
Oplossing: de valse oplossing (#213) komt niet meer in de kaart en `VulOntbrekendeOplossingWaarnemingen` vult in Afronden bij elk opgelost kortsluitpunt de ontbrekende waarneming bij het oplossingswissel aan. Wissel 14 zelf wordt door de proef (zonder vals alarm-bewijs) alsnog gevonden. Nog niet getest op de baan.

## BUG #213 – OPGELOST (nog niet getest op de baan): 'Kortsluiting opgelost: adres 14 én 12 samen' terwijl wissel 13 het alarm weghaalde (Marco 18-10 09:43)
Log 09:19-09:22: de rit met 14 afbuigend gaf kortsluiting bij 24 (09:19:45). Het alarm verdween om 09:20:28 (Dinamo: 'geen kortsluiting meer in blok 8'), één seconde na het commando 'wissel 13 afbuigend' (09:20:27). Pas daarna, 09:20:34 en 09:20:39, werden 11 en 14 afbuigend gezet ('samen vormen ze de oplossing') en 09:21:56 volgde 'Kortsluiting opgelost: adres 14 én 12 samen'. Het alarm was dan al weg, dus elke partner lijkt een oplossing; ik schreef dat in #212 ook ten onrechte over. Marco: de timing klopte niet. Er is in het hardwarelog tussen 09:18 en 09:23 geen enkel commando voor wissel 1; het enige commando vlak voor het verdwijnen van het alarm was wissel 13 (kanttekening: Dinamo zet het alarm ook zelf steeds even op en af, zie 09:18:42 en 09:19:46, dus ook 13 is geen volledig bewijs).
Oplossing: `PartnerwisselZoeken` doet niets (geen partnerzoektocht, niets vastleggen) zolang een andere wissel afbuigend staat die het alarm net wegnam (klemoplossing, loc nog op die melders, < 10 min), met een waarschuwing in het log. CompileCheck-suite: ALLES OK; geen gesimuleerde rit voor dit geval. Hangt samen met #194, #204 en #211.

## BUG #214 – OPGELOST (nog niet getest op de baan): kortsluitpunt #2 blijft 'open' terwijl 5 + 10 afbuigend inmiddels foutloos is gereden (rapport 18-10 09:50)
Kortsluitpunt #2 (na melder 14 achteruit, afbuigend: 10, alarm blok 3 en 9) staat in het rapport nog op 'open'. In dezelfde kaart is wissel 5 als splitsing na melder 14 (vooruit) gevonden (rechtdoor 24, afbuigend 129) en zijn ritten #57 en #58 (13 -> 14 -> 129 -> 24 -> ... -> lus) met 5 en 10 afbuigend zonder kortsluiting gereden. Het kortsluitpunt is dus opgelost door wissel 5 (samen met 10) en moet 'opgelost met adres 5 op afbuigend' tonen, net als #1 ('opgelost met adres 1'). Te doen: een kortsluitpunt als opgelost markeren zodra een rit met precies die overgang en een extra wissel zonder kortsluiting is gereden, en de wisselstraten daarop aanpassen. Daarnaast bevat het rapport nog de valse wissel 11 (zie #210), ook in de wisselstraat 14 -> 13 ('wissel 11 rechtdoor (uit splitsing)').

**Oplossing (18-10):** `Baankaart.LosKortsluitpuntenOpUitRitten` (bij het afronden): een open kortsluitpunt is opgelost als gewone ritten over dezelfde overgang (ook omgekeerd, wisselstanden gelden in beide richtingen) zonder kortsluiting zijn gereden met de stand van het punt plus precies één extra wissel die in al die ritten afbuigend stond. Met de kaart van 09:50 geeft dat kortsluitpunt #2 = adres 5 afbuigend (ritten #57, #58). Bewust niet tijdens de verkenning (dat veranderde de simulatie). De wissel 11 in de wisselstraat verdwijnt via #209.

## BUG #215 – OPGELOST (nog niet getest op de baan): loc reed 7 rondjes over een keerlus op de terugweg (Marco 18-10 10:44, log 10:22-10:26)
Rit #66 (13 → 14 → 129 → 4 → 11 → 12 → 1 → 9 → 10 → 129, wissel 2, 5, 9, 10 afbuigend) eindigde op melder 129 (kortsluiting, wissel 6 loste het op). De terugweg naar melder 13 nam het omgekeerde pad (129, 10, 9, 1, 12, 11, 4, 129, 14, 13) met één vaste wisselstand (5, 6, 9, 10 afbuigend). Bij het tweede passeren van 129 stond de stand nog hetzelfde, dus de loc nam weer 10 in plaats van 14: 47 melders (7 rondjes, 4 minuten) tot de grens "2 x aantal melders" van #195 de rit stopte. Dit WAS dus een echte lus.
Oplossing: (1) `KnipLus`: komt een melder twee keer in het terugpad voor en is de overgang na de tweede keer bekend, dan wordt de lus eruit geknipt (hier: 129, 14, 13). (2) Navigatie stopt nu bij de tweede keer dezelfde gerichte overgang (`HerhaaldeOvergang`), dus na één rondje (~1 minuut) in plaats van 47 melders; daarna volgt de bestaande herstelpoging. Tests in CompileCheck (ALLES OK). Onbekend: of het gesneden pad op de baan met stand 5 + 10 (en 6 rechtdoor) werkelijk 129 → 14 geeft.

## BUG #216 – OPGELOST (nog niet getest op de baan): elke proef zet wissel 14 eerst rechtdoor en dan weer afbuigend (log 10:40-10:43)
Tijdens de proeven voor adres 1, 3, 4, 7 (vooruit vanaf melder 14) herstelt `HerstelRijstand` wissel 14 naar rechtdoor, waarna de proef hem met de bekende oplossing van kortsluitpunt #3 meteen weer afbuigend zet. Dat kost per proef twee wisselcommando's (~4 s) en is zinloos, maar geeft geen fout. Te doen: de rijstand van de proef vooraf met de bekende oplossing combineren zodat 14 niet heen en weer gaat.
Oplossing: de bekende oplossing van de proef (bijv. wissel 14) is nu het doel van `HerstelRijstand`, zodat 14 niet eerst terug en meteen weer om gaat. Nog niet getest op de baan.

## BUG #217 – OPGELOST (nog niet getest op de baan): onjuiste oplossing 'wissel 6' voor kortsluitpunt #2 laat de loc steeds vastlopen op melder 129 (Marco 18-10 11:48, log 11:26-11:38)
Kortsluitpunt #2 (129 → 14 achteruit, afbuigend 10) stond sinds 10:22 "opgelost door wissel 6 afbuigend" (het alarm verdween toen wissel 6 werd omgezet, maar dat is de bekende alarm-timing, zie #204/#211). Tegelijk is bekend dat 14 → 129 vooruit met wissel 6 afbuigend kortsluiting geeft; dat is dezelfde overgang andersom, en wisselstanden gelden in beide richtingen. De controle van #159 (`HerstelStrijdigeOplossingen`) keek alleen naar dezelfde richting en trok de oplossing dus niet in. Gevolg: elke terugweg 129 → 14 → 13 zette 6 afbuigend ("kortsluitpunt #2 was daarmee opgelost"): 11:27 kortsluiting direct na 24, 11:31-11:34 de loc klem op 129 (alle 14 wissels geprobeerd, 3 minuten), kortsluitpunt #5 en #6 daardoor ook (de 'oplossing met wissel 1' van #6 en 'wissel 3' van #5 zijn ruis).
Oplossing: `OplossingStrijdigMetVerbod` kijkt ook naar de omgekeerde overgang. `HerstelStrijdigeOplossingen` trekt zo'n oplossing in (het punt blijft zoeken, niet 'opgegeven'), `RegistreerHandmatigeOplossing` accepteert zo'n wissel niet meer, en bij het afronden wordt de controle opnieuw gedaan, daarna leidt #214 het juiste adres (5) af uit de ritten. Test met de echte kaart van 11:46 in CompileCheck (ALLES OK). Bestaande ruis in #5 en #6 uit deze sessie wordt niet automatisch weggehaald; de kaart opnieuw laten verkennen met de nieuwe versie is schoner.

## BUG #218 – OPGELOST (nog niet getest op de baan): klemoplossing-wissel onder de loc teruggezet (log 18-10 12:08:43-12:08:55)
Wissel 10 afbuigend haalde om 12:08:43 de kortsluiting op 24/129 weg ("blijft zo staan tot de loc de melder verlaten heeft"), maar de terugweg na de proef (`TerugNaar` -> `ZetConfiguratie` zonder `spaarOnderLoc`) zette hem om 12:08:55 weer rechtdoor terwijl de loc nog op 129 stond: 0,2 s later weer kortsluiting in blok 8/9 en een tweede zoekronde langs alle wissels (1 minuut). `ControleerGeenAlarmVoorStap` wiste bovendien `_alarmStand` voordat `MetAlarmStand` hem kon toepassen.
Oplossing: `ZetConfiguratie` spaart een klemoplossing nu altijd zolang de loc nog alleen op (een deel van) die melders staat (`KlemNogOnderLoc`); met `spaarOnderLoc` zoals voorheen bij elke overlap. Test in CompileCheck.

## BUG #219 – OPGELOST (nog niet getest op de baan): wissel 2 'weer' afbuigend gezet bij een kortsluiting elders (log 18-10 12:08:43)
`_losgemaakteProefWissels` onthield wissel 2 van de kortsluiting op 5/16 (12:03:22, afbuigend = bekende kortsluiting 16 -> 5). Bij de kortsluiting op 24/129 werd hij als 'partner' weer afbuigend gezet en kwam hij in rit #7 als "afbuigend: 2, 5, 10". Nu wordt per losgemaakte wissel bewaard waar de loc toen stond; alleen bij dezelfde plek mag hij terug (`PartnerHoortBijPlek`).

## BUG #220 – OPGELOST (nog niet getest op de baan): melder die terugkomt na een kortsluiting telde als gereden melder (log 18-10 12:09:56)
Tijdens het alarmherstel viel melder 24 weg (geen stroom) en kwam hij om 12:09:56 terug. De rit achteruit vanaf 129 begon om 12:09:57.9 en telde 'melder 24 bezet' als eerste gereden melder; BUG #181 overschreef daardoor de juiste richting 129>24 (vooruit) met achteruit, en het rapport kreeg "melder 129 achteruit -> 14, 24". Nu worden wijzigingen die tijdens de stilstand (alarmcontrole vóór de rit) binnenkwamen eerst weggewerkt. De al opgeslagen kaart van 12:21 bevat de foute richting nog (129 achteruit -> 24).

## BUG #221 – OPGELOST (nog niet getest op de baan): STOP pas 3 s na de kortsluitmelding (hardwarelog 18-10 12:07:29-12:07:33)
Het rijcommando naar 9 blokken duurt 1,8 s en werd helemaal afgewacht, ook toen Dinamo om 12:07:29.9 kortsluiting meldde; daarna ging de stoppas in vaste volgorde 1..9, dus blok 8/9 kregen de STOP om 12:07:32.9. Nu: (1) wachten na een rijcommando stopt zodra er een blokalarm is (`WachtOfAlarm`); (2) `VolledigeStop` gooit wachtende snelheidscommando's weg (`VerwijderWachtendeSnelheidscommandosVoor`, nu ook in `IHardwareInterface`) en stuurt de STOP eerst met voorrang naar de alarmblokken en de blokken van de bezette melders (`StopVolgordeEerst`), daarna de gewone stoppas.

## BUG #222 – OPGELOST (nog niet getest op de baan): klemdiagnose koos de wissel die van de puntzijde bereden was (log 18-10 12:07:39)
Rit 13 -> 14 -> 129 -> 24 met wissel 5 afbuigend; de kortsluiting kwam pas na 24 (wissel 10, de samenvoeging). Toch "hij zit klem op wissel 5" en wissel 5 ging terug naar rechtdoor, en in beide zoekrondes werd 5 ook nog omgezet terwijl de loc op 129 stond. Regel #169: van de puntzijde bereden helpt omzetten niet. Nu: nam de loc met de proefwissel de afbuigende tak en reed hij daarna nog een melder verder (`ProefWisselVanPuntzijdeBereden`), dan wordt die wissel niet losgemaakt en in `ProbeerAlarmWegTeKrijgen` als 'samengevoegde kant' overgeslagen.

## BUG #223 – DEELS OPGELOST (nog niet getest op de baan): rapport klopt niet bij ritten met een kortsluiting (rapport 18-10 12:21)
(1) Opgelost: de kolom 'wissels afbuigend' toonde de stand NA het herstel (rit #3 'geen' i.p.v. 2, #7 '2, 5, 10' i.p.v. 5, #13 'geen' i.p.v. 10); nu de stand bij vertrek. (2) Opgelost: rit #3/#4 stonden als ONBETROUWBAAR (Dinamo-uitval) omdat de terugmelding van wissel 2 tijdens de kortsluiting pas na 3,6 s kwam; met een (recent) blokalarm wacht de verkenner nu tot 4 s en telt een uitblijvende terugmelding niet als uitval. (3) NIET opgelost: kortsluitpunt #1 (na 24, afbuigend 5) blijft 'open' omdat het pas na het alarmherstel wordt vastgelegd. Automatisch de alarmoplossing (wissel 10) aan het later vastgelegde punt hangen is gebouwd en weer verwijderd: in de simulatie (start 5) gaf dat valse oplossingen (20 i.p.v. 21 melders, 13 kortsluitingen), ook als voorlopige oplossing (#204). Wissel 10 afbuigend als oplossing wordt door de kaart wel geaccepteerd (test met de kaart van 12:21); de wisselstraat 14 -> 129 noemt wissel 10 nog niet. Te doen: oplossing pas vastleggen na een rit die hem bevestigt.

## BUG #224 – OPGELOST (nog niet getest op de baan): onnodig doorkruipen "wissel 5 ligt bij melder 13" (log 18-10 12:10:18)
Na de proef werd wissel 5 als 'bij de bezette melder' gemarkeerd (`_bekendBijBezet`), terwijl hij pas na melder 14 splitst; de loc kroop 4,8 s door. Nu alleen als de afwijking direct na de startmelder begint (`EersteAfwijking` == 1).

## BUG #225 – OPGELOST: standvraag van melder 1-64 stond als "onbekend commando" in de hardwarelog (18-10 11:57:26)
`DinamoLogVertaler` kent nu het uitgaande commando (1100SSSS)(sssssss): "stand opvragen van melder N".

## BUG #226 – NIEUW (Marco 18-10 12:31): automatische tussenstand elke 10 minuten
Tijdens een verkenning schrijft de Baanverkenner elke `AutoTussenstandMinuten` (standaard 10, 0 = uit) en aan het einde automatisch baanrapport (txt + html), baankaart, logboek en hardwarelog naar `AutoTussenstandMap` (standaard `C:\Marco\Autolog`), met `LAATSTE.txt` (tijdstip + namen van de nieuwste set). Fouten bij het schrijven komen alleen in het logboek. Kern-deel getest in CompileCheck; het WPF-deel (timer in `MainWindow`) is hier niet gecompileerd en nog niet getest op de baan.

## BUG #227 – OPGELOST (nog niet getest op de baan): melder aan de andere kant bij het vertrek gaf een valse splitsing van wissel 10 (autolog 18-10 13:25, log 13:17-13:24)
Opdracht #5 (vanaf 21 achteruit, wissel 10 afbuigend): bij het vertrek meldde melder 29 0,8 s 'bezet' (13:17:18; 21 -> 29 is 12x vooruit gereden, dus 29 ligt aan de andere kant). De rit telde 21 -> 29 -> 24 -> 129 en de proef gaf de valse vondst "wissel 10 na melder 21 (achteruit): rechtdoor 24, afbuigend 29". Gevolgen: 13:21:33 eiste de kaart wissel 10 rechtdoor voor 129 -> 24 -> 21 (kortsluiting in blok 9, de loc klem op 24/129, Marco moest hem terugzetten) en 13:24:38 zette de basisrit wissel 10 afbuigend bij melder 21 (plus "OVERSCHREVEN 21>29").
Oplossing: (1) in de rit telt de eerste melder na het vertrek niet als hij volgens de kaart aan de andere kant van de startmelder ligt (`IsAchterkantMelder`: minstens 3x in de andere richting gereden); volgt daarna een melder die verder aan die kant ligt, dan stopt de rit met 'loc andersom'. (2) Bij het laden/hervatten ruimt `VerwijderAchterkantOvergangen` zulke overgangen op (X -> Y waarvan X -> Y in de andere richting 3x zo vaak gereden is), de stap erna (29 -> 24) en de splitsingen en doorgangen die erop steunen. Getest met de kaart van 13:25 in CompileCheck (ALLES OK, simulatie start 17 en 5: 21 melders).
Ook gezien, nog niet aangepakt: de kortsluiting van 13:20:51 (129 -> 14 achteruit, wissel 5 pas op melder 129 afbuigend gezet, 2,2 s ervoor) werd aan proefadres 1 toegeschreven; de partnerzoektocht stopte terecht (#211) en er kwam geen valse wissel 1 in de kaart. Met de echte splitsing na melder 24 (die opdracht #5 nu opnieuw kan vinden) zet de verkenner wissel 5 al op melder 24 (BUG #108, twee stappen vooruit).

## BUG #228 – OPGELOST (nog niet getest op de baan): wissel 5 pas omgezet toen de loc al op melder 14 stond (autolog 18-10 13:31:23)
Opdracht #6 navigeerde 13 -> 14 en daarna 14 -> 129 -> 24 (vooruit). Wissel 5 (splitsing na melder 14) ging pas afbuigend toen de loc op 14 stond met de neus bij de tong: direct kortsluiting in blok 3 ("de loc kan niet weg van melder 14"). Nu zet `GrensWisselsVooraf` een wissel waarvan de puntzijde direct na de grensmelder ligt en die de volgende etappe in dezelfde richting nodig heeft al tijdens de etappe ervoor (niet als die etappe hem zelf nodig heeft, niet bij een kering, nooit kruismotoren). Test met de kaart van 13:33.

## BUG #229 – OPGELOST (nog niet getest op de baan): wissel 10 vooraf rechtdoor gezet terwijl kortsluitpunt #1 hem afbuigend eist (autolog 18-10 13:31:23)
Achter elkaar in het log: "10 afbuigend (kortsluitpunt #1 na melder 24 was daarmee opgelost)" en "Wissels bij startmelder 24 worden al gezet voordat de loc daar aankomt: 10 rechtdoor". `StartmelderWisselsVooraf` keek alleen naar de splitsingen, niet naar bekende kortsluitoplossingen. Nu blijft zo'n wissel staan tot de loc op de startmelder is. Test met de kaart van 13:33.

## BUG #230 – OPGELOST (nog niet getest op de baan): klemroutine sloeg juist de net omgezette wissel over (autolog 18-10 13:31:36-13:33:52)
Na #228 probeerde `ProbeerKlemWegTeKrijgen` ruim 2 minuten alle andere adressen (1 t/m 12, elk met een proefrit en een kortsluiting) en sloeg wissel 5 over ("ligt bij een bezette melder"), terwijl die 5 s eerder onder de loc was omgezet; Marco stopte de verkenning. Nu gaat eerst de laatst omgezette wissel terug als die minder dan 15 s geleden is omgezet terwijl de loc op dezelfde melders stond (in die stand stond de loc er zonder kortsluiting). Is het alarm dan weg, dan breekt de stap af en wordt hij later opnieuw gereden (met #228 staat wissel 5 dan op tijd). Geen simulatietest voor dit geval.

## BUG #231 – OPGELOST (nog niet getest op de baan): na de keerlus 10 -> 129 kwam wissel 6 pas als laatste aan de beurt (autolog 18-10 13:43:29-13:45:25)
Proefrit met wissel 9 afbuigend: 14 -> 129 -> 4 -> 11 -> 12 -> 1 -> 9 -> 10 -> 129 (een keerlus). Bij de tweede aankomst op 129 (vanaf 10) kortsluiting in blok 1 en 9. De alarmroutine probeerde 14 adressen (1 min 45 s) voordat wissel 6 (met toestemming) de oplossing bleek. De kaart wist al: "14 -> 129 met wissel 6 afbuigend geeft kortsluiting" (wissel 6 rechtdoor = tak van 14). Nu: kwam de loc vanaf een ANDERE melder op zo'n melder aan, dan is die wissel de eerste verdachte (`AndereTakVerdachten`, regel #169) en wordt hij als eerste (met toestemming, hij ligt bij de loc) geprobeerd. Test met de kaart van 13:49.

## BUG #232 – OPGELOST (nog niet getest op de baan; zie onder): terugweg uit de keerlus via 129 -> 14 gaat steeds naar melder 10 (autolog 18-10 13:45:29 en 13:48:30)
Na de keerlus (aankomst op 129 vanaf 10, wissel 6 afbuigend als klemoplossing) knipt `KnipLus` (#215) de terugweg tot "129 -> 14 achteruit". Maar met wissel 6 afbuigend (onder de loc, mag niet om) gaat achteruit vanaf 129 altijd terug naar 10; twee keer gezien. Ook het doorkruipen op 129 (13:46:01) bracht de loc op melder 4 (wissel 9 aan de andere kant van 129), en daarna gaf wissel 9 rechtdoor weer kortsluiting. Conclusie: de loc is langer dan melder 129 en staat daar op wissel 6 én wissel 9 tegelijk; vanaf 129 kan hij niet keren met een andere stand dan waarmee hij aankwam. Te doen (eerst Marco vragen): (1) klopt het dat 4 - 11 - 12 - 1 - 9 - 10 een keerlus is die via wissel 6 weer op 129 uitkomt? De simulatiebaan (`MiniworldBaan`) heeft 10 nu als doodlopend zijspoor en kan dit geval dus niet nabootsen; (2) daarna KnipLus niet laten knippen op de plek waar de loc staat als daar een klemwissel ligt, en de terugweg de lus laten afmaken (met wissel 6 rechtdoor gezet terwijl de loc op 4/11 staat) of vooruit via wissel 9 rechtdoor naar 24 laten rijden.

## BUG #233 – OPGELOST (Marco 18-10 13:50): automatische tussenstand moet in C:\Marco zelf, niet in C:\Marco\Autolog
Standaard `AutoTussenstandMap` is nu `C:\Marco`. Staat in de bewaarde instellingen nog de oude standaard `C:\Marco\Autolog`, dan wordt die ook `C:\Marco` (`AutoTussenstand.Map`); een zelf gekozen andere map blijft. LAATSTE.txt staat dus ook in C:\Marco. Test in CompileCheck.

**Oplossing #232 (18-10 14:20, Marco: "dat moet je zelf ontdekt hebben"):** de logs bewijzen de lus (rit 13:42: 14, 129, 4, 11, 12, 1, 9, 10, 129). (1) De simulatie kent nu optioneel deze lus (`MiniworldBaan.Keerlus`: 12 -> wissel 1 rechtdoor -> 1 - 9 - 10 -> wissel 6 afbuigend -> 129; 1, 9, 10 in blok 1); `KeerlusTest` bootst het probleem na (zonder fix 2x "onverwacht melder 10 (verwacht: 129, 14)"). (2) `KnipLus` knipt niet meer op de plek waar de loc staat. (3) De zoektocht naar een weg terug (`ZelfTerugWegAlleStanden`) houdt bij vanaf welke melder en in welke richting de loc ergens aankomt; keren mag alleen terug naar die melder (`KerenToegestaan`), ook voor de plek waar de loc nu staat (`_ingang`). Resultaat in de keerlus-simulatie: 0x de verkeerde terugweg, 23 melders. Bestaande tests ongewijzigd (ALLES OK).

## BUG #234 – OPEN: in de keerlus-simulatie lukt de blokfase niet (15 melders zonder blok)
Gezien in `KeerlusTest` (ook al vóór #232): in de blokfase van optie 6 rijdt de loc bij elk vertrek de andere kant op dan de kaart verwacht ("melder 13 werd bezet bij het vertrek achteruit vanaf melder 5"), waarna elke navigatie een NavigatieFout geeft en 15 melders geen Dinamo-blok krijgen. Nog uitzoeken of de loc in de simulatie door de lus werkelijk gedraaid is (dan moet de verkenner een gedraaide loc herkennen) of dat de nagebouwde lus niet klopt. Niet op de baan gezien.

## BUG #235 – OPGELOST (nog niet getest op de baan): wissel 6 'afbuigend' gemeld terwijl hij op de baan rechtdoor stond (Marco 18-10 14:06)
Nagekeken (hardwarelog 13:45:23): de verkenner stuurde 'wissel 6 afbuigend' en Dinamo meldde zelf 'terugmelding wissel 6 afbuigend'; Marco zag op de baan dat wissel 6 rechtdoor stond. Wissel 6 is dus omgekeerd aangesloten (Dinamo-afbuigend = baan-rechtdoor). De verkenner blijft intern met de Dinamo-commando's rekenen (dan klopt de kaart met wat verstuurd wordt en met Koploper), maar er is een instelling "Omgekeerd aangesloten" (`OmgekeerdAangesloten`, bijv. "6", standaard leeg; in het scherm bij de Dinamo-instellingen). Elke logregel die zo'n wissel met een stand noemt krijgt dan "[wissel 6 is omgekeerd aangesloten: 'afbuigend' in deze melding is op de baan rechtdoor en andersom]", en het rapport (tekst en html) zegt het bij de wissels. Niet hard gecodeerd: Marco vult het zelf in. WPF-deel niet gecompileerd hier.

## BUG #236 – OPGELOST (nog niet getest op de baan): oude 'oplossing' uit de voorkennis gaf een kortsluiting bij een andere ingang (autolog 18-10 14:18-14:20)
Nieuwe verkenning met voorkennis (29 oplossingen overgenomen). De proef met wissel 2 afbuigend kreeg de oude oplossing "5 vooruit, stand 2 -> wissel 1 afbuigend" (zonder ingang, Via = 0) mee, terwijl die bij de overloop 12 -> 5 hoort; de loc kwam vanaf 16 op melder 5: kortsluiting (rit #3). Nu past `MetBekendeOplossingenVoorStart` een oplossing niet toe als (1) de ingang van de oplossing bekend is en de bekende weg op een andere ingang uitkomt (`ViaBijAankomst`), of (2) de kaart weet dat een wissel uit de stand van de oplossing op déze ingang afbuigend kortsluiting geeft (zelfde regel als #217). De eerste keer (nog geen verbod, oude oplossing zonder ingang) kan het nog één keer gebeuren. Let op: de voorkennis bevat ook oplossingen met adres 3, 4, 7, 8, 11, 12 (bestaan op de baan niet, ruis uit eerdere sessies). Tests met de kaart van 14:24.

## BUG #237 – GECONTROLEERD, GEEN FOUT (Marco 18-10 14:27: "het zit in de herstelfunctie die omgekeerd werkt"): alle wisselstand-meldingen naast de hardwarelog gelegd
Script `Tools/wisselstand_controle.py`: elke melding met een wisselstand ("Adres N: ... (wisselcommando)", "nadat wissel N ... naar X is geschakeld", "staat weer op afbuigend", "klem op wissel N ... gaat terug naar rechtdoor", klemroutine) uit 7 logsets van 18-10 (12:21 t/m 14:24) is vergeleken met het Dinamo-commando dat werkelijk de deur uitging. 456 meldingen, 0 verschillen (1 melding aan het eind van een log zonder hardwareregel, door het afkappen). De herstelfunctie stuurt dus wat hij meldt; ook de terugmeldingen van Dinamo zijn gelijk aan het commando. Het verschil bij wissel 6 zit in de aansluiting van die wissel (Dinamo 'afbuigend' = op de baan rechtdoor), zie #235. Of andere wissels ook omgekeerd zijn aangesloten kan de software niet zien (Dinamo meldt alleen het commando terug, niet de werkelijke tongstand): dat kan alleen op de baan worden vastgesteld, en dan in "Omgekeerd aangesloten" worden ingevuld.

## BUG #238 – OPGELOST (nog niet getest op de baan): foute oplossingen uit de voorkennis worden nooit meer vergeten (autolog 18-10 14:24-14:28)
Nieuwe verkenning met voorkennis (14:14). (1) 14:25:19 de terugweg 24 -> 129 -> 14 -> 13 (achteruit) kreeg "6 afbuigend (een eerder gevonden oplossing na melder 14)" uit het leerprofiel; dat is de onjuiste oplossing van #217 (door #217 uit de kaart gehaald, maar het leerprofiel bewaart alles). De loc reed naar 10 (14:26:22) en Marco moest hem terugzetten. (2) 14:24:06 de proef met wissel 5 kreeg uit de voorkennis "1 afbuigend, 13 afbuigend" mee (ruis) en liep toch in een kortsluiting na 24.
Oplossing: `Baankaart.WeerlegVoorkennis`: gaat een navigatie mis direct na de ingang van een toegepaste voorkennis-oplossing (zelfde ingang, en niet een wissel die de kaart voor die stap zelf afbuigend eist), of geeft een proef met een extra wissel uit de voorkennis toch kortsluiting na de melder van die oplossing, dan wordt de oplossing weerlegd: weg uit de kaart en in `WeerlegdeOplossingen`; `LeerProfiel.LeerKortsluitOplossingen` haalt weerlegde oplossingen ook uit het leerprofiel en neemt ze niet opnieuw over. Test met de kaart van 14:24. Advies aan Marco: de huidige voorkennis bevat veel ruis; een nieuwe verkenning zonder voorkennis ("Nee") is het schoonst.
