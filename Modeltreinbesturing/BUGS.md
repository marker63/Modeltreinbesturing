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
