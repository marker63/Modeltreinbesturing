# Wisselcontrole uit de hardwarelogs (Marco's stap-voor-stap methode, 14-11-2026)

Bron: alle 20 hardwarelogs van de baanverkenner van 4 t/m 14 november
(`%AppData%\Modeltreinbesturing\Logs\hardwarelog_Baanverkenner_*.txt`). Daarin staat precies wat de Dinamo
terugmeldde: welke melder bezet werd en hoe de wissels op dat moment stonden (terugmelding = de puls is gegeven).
Geteld: hoe vaak de loc van melder A naar melder B reed bij elke wisselstand, en hoe vaak er daarna een kortsluiting kwam
(kortsluitingen binnen 15 s samengenomen; kortsluitingen terwijl wissels werden omgezet niet meegeteld).

Alle standen hieronder zijn **Dinamo-commando's**. Wissel 6 en 9 zijn omgekeerd aangesloten (op de baan andersom).
Wissel 1 en 2, en 5 en 6, zijn overloopsets: ze staan op de baan altijd samen.

## Overloop 2/1 (bij melder 5 en 12)

| Van -> naar | wissel 2 (+1) | gereden | kortsluiting daarna |
|---|---|---|---|
| 5 -> 16 | rechtdoor | 43 | 0 |
| 5 -> 12 | afbuigend | 18 | 0 |
| 16 -> 5 | rechtdoor | 48 | 0 |
| 16 -> 5 | afbuigend (verkeerde poot) | 15 | 24 (melder 5 wordt nog bezet, dan kortsluiting) |
| 12 -> 5 | afbuigend | 24 | 0 |
| 12 -> 1 | rechtdoor | 70 | 0 |
| 1 -> 12 | rechtdoor | 52 | 0 |
| 1 -> 12 | afbuigend (verkeerde poot) | 25 | 26 |

Klopt helemaal met Marco: 5 -> 16 rechtdoor, 5 -> 12 afbuigend, en andersom hetzelfde. Nooit anders gezien.

## Overloop 5/6 (bij melder 14, 129 en 10)

| Van -> naar | wissel 5 (+6) | gereden | kortsluiting daarna |
|---|---|---|---|
| 14 -> 24 | rechtdoor | 132 | 7 |
| 14 -> 129 | afbuigend | 41 | 1 |
| 129 -> 14 | afbuigend | 39 | 0 |
| 24 -> 14 | rechtdoor | 82 | 0 |
| 24 -> 14 | afbuigend (verkeerde poot) | 39 | 48 |
| 129 -> 10 | rechtdoor | 86 | 12 |
| 10 -> 129 | rechtdoor | 84 | **71** (zie "keerlus" hieronder) |
| 10 -> 129 | afbuigend (verkeerde poot) | 28 | 33 |

Klopt met Marco: rechtdoor 14 <-> kruiswissel (24) en 10 <-> 129; afbuigend 14 <-> 129.

## Wissel 9 (bij melder 129; omgekeerd aangesloten)

| Van -> naar | wissel 9 (Dinamo) | op de baan | gereden | kortsluiting daarna |
|---|---|---|---|---|
| 129 -> 4 | afbuigend | rechtdoor | 93 | 0 |
| 4 -> 129 | afbuigend | rechtdoor | 71 | 2 |
| 4 -> 129 | rechtdoor (verkeerde poot) | afbuigend | 33 | 52 |
| 129 -> 24 | rechtdoor | afbuigend | 67 | 14 |
| 24 -> 129 | rechtdoor | afbuigend | 57 | 27 |

Klopt met Marco: op de baan afbuigend = 129 <-> kruiswissel, rechtdoor = 129 <-> binnenring (4).

## Wissel 14 (bij melder 8)

| Van -> naar | wissel 14 | gereden | kortsluiting daarna |
|---|---|---|---|
| 8 -> 17 | rechtdoor | 106 | 0 |
| 17 -> 8 | rechtdoor | 88 | 0 |
| 17 -> 8 | afbuigend (verkeerde poot) | 18 | 24 |
| 8 -> 24 | afbuigend | 13 | 2 |
| 24 -> 8 | afbuigend | 12 | 0 |
| 24 -> 8 | rechtdoor (verkeerde poot) | 1 | 8 |

## Kruiswissel 24 (motoren 10 en 13)

| Rit | motor 10 | motor 13 | gereden |
|---|---|---|---|
| 14 -> 24 -> 21 | rechtdoor | rechtdoor | 88 |
| 21 -> 24 -> 14 | rechtdoor | rechtdoor | 73 |
| 21 -> 24 -> 129 | afbuigend | rechtdoor | 46 |
| 129 -> 24 -> 21 | afbuigend | rechtdoor | 32 |
| 8 <-> 24 <-> 14 of 129 | - | - | niet gereden in deze logs (KRUISWISSEL.md 10-10: 8 -> 24 -> 14 met motor 13 afbuigend) |

Kortsluiting op 24: komend van 21 met motor 13 afbuigend (24x), komend van 129 met motor 10 rechtdoor (10x).

## Wat verder opvalt

1. **Keerlus-grens 10 / 129.** Van 10 naar 129 met de overloop goed (rechtdoor) volgt in 71 van de 84 ritten toch een
   kortsluiting, de andere kant op (129 -> 10) maar 12 van de 86. Melder 10 hoort bij Dinamo-blok 1, melder 129 bij blok 9.
   Dat past bij de keerlus: daar sluit de lus zich, en met hetzelfde rijcommando op beide blokken werken de twee kanten
   elkaar tegen. Dit is geen wisselprobleem. Vraag aan Marco: hoe stuurt Koploper dit stuk (blok 1 en 9) aan?
2. **Melders die soms niet melden.** 14 -> 21 (13x) en 10 -> 24 / 24 -> 10 (20x): daar werd de kruiswissel 24 of melder 129
   niet gezien. Zo'n gemiste melder gaf eerder valse wissels (#417).
3. **Melders die niet bestaan.** 268 (tussen 25-17, 29-30, 21-...), 137, 138, 3, 23, 31, 32 verschijnen kort tussen twee
   echte melders. Behandelen als spookmelders.
4. **Verkeerde poot.** Komt de loc een wissel van de verkeerde gesplitste kant binnen, dan wordt de melder van de wissel
   nog bezet en volgt daarna de kortsluiting (16 -> 5, 24 -> 14, 17 -> 8, 4 -> 129, 1 -> 12). Precies zoals Marco beschreef.
5. **Verkenning 14-11 15:38 (versie bug420):** voltooid in 1 u 8 min, 23 melders, 9 blokken, wissels 1, 2, 5, 6, 9, 10, 13, 14,
   geen valse wissels in het eindresultaat. Alle gevonden standen kloppen met de tabellen hierboven.
