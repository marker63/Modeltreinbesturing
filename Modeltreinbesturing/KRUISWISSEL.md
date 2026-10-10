# Kruiswissel (Engelse wissel 13/10) - wat vaststaat en wat niet

Dit bestand is de bron van waarheid voor de kruiswissel-standen. NIEUWE SESSIES: eerst lezen,
en nooit een wisselstraat "spiegelen" of afleiden zonder dit te controleren.

## Vaste feiten (door Marco vastgesteld, veldgetest)
- De Engelse wissel heeft twee onafhankelijke motoren: Adres 13 (StandAdres) en Adres 2 = 10
  (StandAdres2). Per rijweg een eigen, soms gemengde combinatie; er is GEEN vaste polariteitsregel
  (`Adres2OmgekeerdePolariteit` is vervallen en wordt niet meer gebruikt).
- Melder 24 is de kruiswissel-sectie en hoort bij de blokken 3, 4 en 7 tegelijk.
  De sectie heeft een eigen rijstroom-adres (Dinamo-blok 8, `Kruiswissel.RijAdres`).
- Blok 3/4 en blok 1/2 vormen elk een gesloten lus via de kruiswissel.
- Marco, 10-10-2026: vanuit blok 3 is de rit naar blok 7 *vooruit* en de rit naar blok 4
  *achteruit* (baanindeling). Zie BUG #49: dit staat nu per relatie in `BlokRelatie.RijrichtingVooruit`.

## Standen in de wisselstraten (project testbaan-v24, 10-04-2026)
Stand: 0 = rechtdoor, 1 = afbuigend. Kruiswissel = (motor 13, motor 10).

| Rit | Wissels | Kruiswissel | Herkomst |
|---|---|---|---|
| 7 -> 3 | wissel 5 = 0 | (1, 1) | veldgetest door Marco |
| 4 -> 3 | wissel 5 = 0, wissel 14 = 1 | (0, 0) | veldgetest door Marco |
| 3 -> 4 | wissel 2 = 0 | (0, 0) | herkomst niet vastgelegd (v20, vermoedelijk door Marco) |
| 3 -> 7 | wissel 5 = 0 | (1, 1) | **door Claude toegevoegd als spiegel van 7 -> 3 (BUG #23) - NIET veldgetest** |
| 7 <-> 1 | onbekend | motor 13 = 0, motor 10 = 1 | bekend uit eerdere sessie, nog niet in een wisselstraat (BUG #24-26) |

## Waarnemingen 10-10-2026 die de spiegel-aanname tegenspreken (BUG #48)
- Rit vanuit blok 3 met kruiswissel (1,1) (bedoeld 3->7): eerste testrit reed naar blok 4; tweede
  rit bleef op de tongen staan met kortsluiting in blok 8; na omzetten naar (0,0) reed de loc blok 7 in.
- Rit met (0,0) (bedoeld 3->4): de loc reed naar blok 7 (spookmelding).
- Dit past bij: vanuit blok 3 (richting vooruit) geeft (0,0) toegang tot 7 en (1,1) tot 4,
  terwijl 7->3 (1,1) en 4->3 (0,0) veldgetest zijn. De rit naar 4 is volgens Marco achteruit, dus de
  richting waarin blok 3 de kruiswissel verlaat verschilt en de stand van de rit 3 -> x hoeft niet gelijk
  te zijn aan die van x -> 3. NOG NIET BEWEZEN: eerst fysiek testen (kruiswissel handmatig zetten,
  loc in blok 3 in de juiste richting laten rijden) en dan dit bestand aanvullen.

## Regels voor wijzigingen
1. Een kruiswisselstand in een wisselstraat pas als "veldgetest" markeren als Marco het fysiek bevestigd heeft.
2. Nooit een omgekeerde rit afleiden uit de heenrit (een lus gebruikt de kruiswissel in een andere richting).
3. Rijrichting (vooruit/achteruit) hoort bij de relatie en is net zo belangrijk als de stand.


## Baanverkenner
De Baanverkenner meet rijrichting per melderovergang; `Beheren -> Baankaart vergelijken` kan daaruit lege `RijrichtingVooruit`-velden invullen (BUG #53). Kruiswissel-/wisselstanden uit de verkenner worden NIET automatisch overgenomen; jouw vastgestelde stands blijven leidend.

## Uit de Koploper-export testbaan_002 (Marco, 10-10-2026; bron: Koploper_export_testbaan_002.xlsx, NIET veldgetest door Claude)
Welke blok-overgangen gebruiken welke melder (Koploper-blokken 1-7, niet de Dinamo-blokken):
- Melder 24: 1 -> 4, 4 -> 1, 4 -> 3, 7 -> 1, 7 -> 3 (dus niet 3 -> 4 of 3 -> 7).
- Melder 129: 1 -> 2, 1 -> 4, 4 -> 1, 7 -> 1.
- Melder 14: 2 -> 3, 4 -> 3, 7 -> 3 (14 hoort bij blok 3, aan de kruiswissel-kant).
- Melder 8: 1 -> 4, 3 -> 4, 5 -> 4 (8 hoort bij blok 4); melder 21: 1 -> 7, 3 -> 7, 6 -> 7 (blok 7).
- Wissels in Koploper: 1, 2, 5, 6, 9, 10 (hoort bij 13), 13, 14.
Gevolgtrekking (voorlopig): melder 24 wordt ook voor blok 1 gebruikt (niet alleen 3, 4 en 7) en melder 129 hoort bij blok 1. De kruiswissel heeft vier benen: kant 8/21 (blok 4/7) en kant 14/129 (blok 3/1); de motoren 10 en 13 bepalen welk been bij welk been hoort. Het beschikbare vervolg vanaf melder 24 hangt dus af van de ingangskant en de motorstanden (zie BUG #71, punt 2).
