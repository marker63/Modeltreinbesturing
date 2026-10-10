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
