# Wensenlijst (niet dringend, ooit oppakken)

Dingen die Marco heeft genoemd en die bewust nog niet gedaan worden.

## Baanverkenner
- **Duur in het rapport (Marco, 11-10 08:07):** het rapport toont nu de tijd tussen de start van de eerste testrit en het huidige moment (bijv. 14 uur). Dat moet de opgetelde duur van alle testritten zijn. Nog niets mee gedaan.
- **Overzicht ontdekte wissels in het rapport (Marco, 11-10 08:42):** het wisseladresbereik staat nu op 1 t/m 20, alleen voor de test; zoveel wissels heeft de baan niet. Gewenst: in het rapport een overzicht welke adressen wel en welke niet ontdekt zijn (wel = een effect gevonden, niet = getest zonder effect of nog niet getest). Hoeft niet meteen in een nieuwe versie. Nog niets mee gedaan.

## Modeltreinbesturing: volgende release (voorbereid op verzoek van Marco, 12-10 15:52)
Antwoord op de vraag "zijn alle verbeteringen uit de Baanverkenner ook in Modeltreinbesturing?": **nee, niet alle.** Het hoofdprogramma deelt wel de hardwarelaag
(`Modeltreinbesturing.Hardware`: Dinamo-verzendlaag, startup-handshake, urgente stop, hardwarelog), maar de logica uit de Baanverkenner (`Baanverkenner.Kern`) zit er niet in.
Bijna alles uit #61-#115 is verkenlogica (ritten, proefstops, controle) en hoort daar. Voor het hoofdprogramma zijn dit de kandidaten, nog NIET gebouwd en NIET getest op de baan:
1. **Dinamo-stilte melden (#81, #111).** De hardwarelaag meldt het al (`IVerbindingsBron.StilteGewijzigd`, `SecondenSindsLaatsteOntvangst`), maar het hoofdprogramma luistert er niet naar.
   Voorstel: statusbalk-melding en een eenmalige melding "Dinamo antwoordt niet meer" met "weer verbonden" erachter.
2. **Blokalarm van Dinamo (#43, #82).** `IBlokAlarmBron` wordt alleen door de Baanverkenner gebruikt. Voorstel: kortsluiting per blok zichtbaar op het kijkscherm/bouwscherm
   (blok rood) en geen wissel- of rijcommando's naar het alarmblok zolang het alarm staat.
3. **Wissel niet omzetten terwijl de loc erop staat (#80, #110).** In de Baanverkenner afgedekt via `WisselsBijBezet`. Controleren of routes/wisselstraten in het hoofdprogramma
   een wissel kunnen omzetten terwijl het blok bezet is (kruiswissel 13/10 in melder 24 in het bijzonder), en dat zo nodig blokkeren of uitstellen.
4. **Overloopwissels uit de baankaart importeren (open vraag bij #64) — OVERLOOPWISSELS GEDAAN in #116, nog niet getest op de baan.** `BaankaartImporter` gebruikt de wisselwaarnemingen nog niet. Voorstel: bij het
   importeren de combinaties "wissel A én B afbuigend" meenemen in de wisselstraten (nu alleen voorgesteld, niet ingelezen).
5. **Opstartstand van de Dinamo bewaren (#101)** en het hervatten met de loc op de juiste plek: bekijken of het hoofdprogramma dezelfde 'loc staat al goed' situatie heeft bij het opstarten.
6. **Wisseleffect-controle (open vanaf #65).** Een wissel met bewezen effect die later geen effect meer heeft, melden als "wissel reageert niet, Dinamo opnieuw opstarten".
Niet overnemen: controleverslag (#112), opnieuw beginnen (#114), proefstops (#115), staartwissels (#107-#109): die horen bij het verkennen.
Volgende stap als Marco akkoord geeft: eerst 1 en 2 (klein, hardware is al klaar), daarna 3 en 4 na bekijken van de code. Nog niets gebouwd.
