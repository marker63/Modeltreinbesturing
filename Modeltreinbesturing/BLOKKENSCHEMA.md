# Blokkenschema - vergelijking Baanverkenner vs. bestaande baan (testbaan-v24-plus-3naar7-fix.json)

Gemaakt na de voltooide verkenning van 04-10-2026 (6 u 12 min, 17 melders, 8
wissels, 7 voorgestelde blokken). Vergeleken met het bestand dat
Modeltreinbesturing al die tijd gebruikt: `testbaan-v24-plus-3naar7-fix.json`
(10 echte blokken, 7 wissels/kruiswissel, 10 seinen).

## 1. Blok-voor-blok vergelijking

| Baanverkenner (voorgesteld) | Echte blok (testbaan-JSON) | Oordeel |
|---|---|---|
| Blok 1: melders 5-13-14 (Dinamo-blok 3) | **Blok 3**: melders 14-13-5-**24** | ✅ Klopt. Melder 24 is in de echte baan een *overloop-melder*: hij staat ook in blok 4 en blok 7 (zie §3). Dat is precies waarom bug #38 nodig was - zonder die fix had de verkenner dit in 3 losse blokjes gezet. |
| Blok 2: melders 8-15-16 (Dinamo-blok 4) | **Blok 4**: melders **24**-16-15 | ⚠️ Bijna goed, maar melder **8 ontbreekt** in de echte blok 4. Zie §2. |
| Blok 3: melders 17-25-26 (Dinamo-blok 5) | **Blok 5**: melders 17-25-26 | ✅ Exacte match. |
| Blok 4: melders 20-27-28 (Dinamo-blok 6) | **Blok 6**: melders 27-28-20 | ✅ Exacte match. |
| Blok 5: melders 21-29-30 (Dinamo-blok 7) | **Blok 7**: melders 21-29-30-**24** | ✅ Klopt (24 is weer de overloop-melder, zie §3). |
| Blok 6: melder 24 (Dinamo-blok 8) | *(geen eigen blok - zie §3)* | ℹ️ Geen fout: melder 24 krijgt in de echte baan geen eigen blok, maar wordt als overloop-sectie meegeteld in blok 3, 4 én 7. Dat is een bewuste seingevingskeuze die de verkenner niet kan weten (hij ziet alleen dat 24 een eigen Dinamo-blok heeft). |
| Blok 7: melder 129 | *(bestaat nog niet)* | 🆕 Nieuwe melder, zie §4. |
| *(niet bereikt)* | Blok 1: melders 1-9-10 | De testloc is hier nooit geweest tijdens deze verkenning (buiten de gereden lus). |
| *(niet bereikt)* | Blok 2: melders 4-11-12 | Idem. |
| *(niet bereikt)* | Blok 10/11/12: melders 130-144 (rangeergedeelte) | Idem - apart rangeerstation, niet op deze testlus. |

**Conclusie:** op de stukken die de verkenner wél bereden heeft, komt het voorstel
op 2 details na (§2 en §4) exact overeen met de echte, al jarenlang gebruikte
baan. Bug #38 is hiermee bevestigd correct.

## 2. Melder 8 ontbreekt in blok 4

Baanverkenner reed **8 → 15 → 16** (vooruit, 95-98 keer, zeer stabiel) en
**16 → 15 → 8** (achteruit, even stabiel) - dit is een net zo'n betrouwbare,
veelbereden sectie als de rest van blok 4. In `testbaan-v24-plus-3naar7-fix.json`
staat blok 4 echter maar met melders 24, 16 en 15 - melder 8 zit in geen enkel
blok. Vermoedelijk is melder 8 destijds niet meegenomen toen blok 4 werd
aangemaakt in Modeltreinbesturing (of pas later bijgeplaatst op de baan zelf).

**Voorstel:** melder 8 toevoegen als bezetmeldpunt aan blok 4 (naast 24, 16, 15).

## 3. Melder 24 als overloop-melder tussen blok 3, 4 en 7

In de echte baan zit melder 24 in de bezetmeldpunten van **drie** blokken (3, 4
en 7), terwijl hij zijn eigen Dinamo-blok (8) heeft. Dat is een bewuste
overlap/naderings-sectie: zo kan het systeem zien of een trein een blok
voorbij de wissel/het grensteken is doorgereden, zonder dat melder 24 een eigen
sein/blok nodig heeft. De Baanverkenner stelt melder 24 terecht voor als los
Dinamo-blok (hij kent de echte seingevingskeuze niet), maar dit is geen fout -
bij het importeren voegt Marco melder 24 gewoon toe aan blok 3, 4 én 7, precies
zoals het al stond.

## 4. Nieuwe melder 129

Melder 129 is één keer gezien (traject 14 → 129 → 24, bij configuratie
"afbuigend: 5") en komt in geen van de 10 echte blokken voor. Dit is een
echte nieuwe vondst: waarschijnlijk een extra bezetmelder die na het maken
van de huidige baankaart is bijgeplaatst (bijvoorbeeld op een kort stukje
tussen melder 14 en 24, via de afbuigende tak van wisseladres 5). Dinamo-blok
onbekend (nooit los gezien). **Aanbeveling:** fysiek controleren waar melder
129 zit voordat dit wordt toegevoegd aan het blokkenschema - de verkenner heeft
dit maar één keer geregistreerd.

## 5. Wissels: welke adressen zijn echt?

| Baanverkenner-adres | Gevonden gedrag | Echte wissel in testbaan-JSON? |
|---|---|---|
| 2 | Echte splitsing na melder 5 (dubbel bevestigd, afbuigend = doodlopend) | ✅ Ja - wissel, adres 2 |
| 5 | Echte splitsing na melder 14 (dubbel bevestigd, afbuigend = doodlopend) | ✅ Ja - wissel, adres 5 |
| 4, 7, 8 | "Kortsluiting tussen melder 28 en 26" (identiek voor alle drie, zie bug #37) | ❌ Geen wissel-symbool met dit adres in de baan |
| 6, 9 | Zelfde kortsluiting-symptoom als 4/7/8 | ✅ Ja - beide bestaan als losse wissel, adres 6 en adres 9 |
| 10 | Zelfde kortsluiting-symptoom als 4/7/8 | ⚠️ Bestaat wel, maar niet als losse wissel: adres 10 is het **tweede** decoderadres van de kruiswissel (adres 13 + adres 2 10) |

Dit bevestigt het vermoeden uit bug #37: rond melder 28 reageren **zes**
adressen (4, 6, 7, 8, 9, 10) identiek op "afbuigend", maar maar twee daarvan
(6 en 9) zijn een losse, echte wissel, en adres 10 hoort bij de kruiswissel.
Adres 4, 7 en 8 horen bij **geen enkel** wissel-symbool in de huidige baan.
Vermoedelijk delen deze adressen elektrisch een uitgang of bus met adres 6/9/10
in de Dinamo-bedrading bij melder 28, en geeft het omzetten van 4/7/8 daardoor
hetzelfde (storings)effect zonder dat ze zelf iets aansturen. Dit is geen
software-kwestie, maar de moeite van het even navragen bij de bedrading waard
- al is dat verder aan Marco om te checken, niet iets om nu te "fixen".

---

_Zie `baankaart_2026-10-04_1452.json` (Baanverkenner) en
`testbaan-v24-plus-3naar7-fix.json` (Modeltreinbesturing) voor de volledige
brondata van deze vergelijking._
