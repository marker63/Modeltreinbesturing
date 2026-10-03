# Baanverkenner – handleiding

De Baanverkenner brengt je baan automatisch in kaart met één testloc. Je hoeft vooraf niets over de baan in te voeren. Het programma zoekt zelf uit:

- welke **bezetmelders** er zijn en in welke volgorde de loc ze tegenkomt, per rijrichting;
- welke **wisseladressen** er echt een wissel aansturen, waar die wissel ligt en welke stand waar naartoe leidt (alle adressen in het opgegeven bereik worden één voor één geprobeerd);
- waar de baan **doodloopt** (kopsporen, stootjukken) en waar **lussen** zitten;
- bij **Dinamo**: welk Dinamo-blok elke sectie van rijstroom voorziet;
- een **voorstel voor blokken**: aaneengesloten secties zonder wissel ertussen (en met hetzelfde Dinamo-blok).

Het resultaat is een leesbaar rapport (HTML of tekst), een logboek, en een **baankaart** (.json) die later in Modeltreinbesturing geïmporteerd kan worden.

---

## Eerst proberen zonder baan

Kies bij *Centrale* een **Simulatie-proefbaan** en klik op *Verbinden* en daarna op *Start*. De proefbaan is een ovaal met een inhaalspoor, een kopspoor met een zijspoor en nog een zijspoor. Met *Simulatie-tempo* bepaal je hoe snel het gaat. Op 20× sneller duurt een volledige verkenning een paar minuten. Zo zie je precies wat de verkenner doet, inclusief kortsluitingen en hoe hij die oplost.

## Voorbereiding op de echte baan

1. **Alleen de testloc op de baan.** Haal alle andere voertuigen weg: elke bezette melder die niet van de testloc is, verstoort de meting.
2. **Kies een goede testloc:** een losse loc zonder wagons, die betrouwbaar door wissels rijdt en op lage snelheid niet hapert.
3. **Zet de loc helemaal binnen één melder.** Die melder wordt de *startmelder*. De kant waar de loc heen rijdt als je "vooruit" geeft, is in het rapport steeds "vooruit".
4. **Blijf bij de noodstop**, zeker de eerste keer. De grote rode knop stopt direct alles en zet de verkenning op pauze.

## Instellingen

| Instelling | Uitleg |
|---|---|
| Decoderadres / rijstappen | Van de testloc. De rijstappen moeten overeenkomen met de decoder (CV29), anders reageert hij niet op het snelheidscommando. |
| Verkensnelheid | De "redelijke snelheid" van de ritten. Liever te laag dan te hoog. |
| Kruipsnelheid | Voor het precies neerzetten op een melder. |
| Adressen van / t/m | Alle adressen in dit bereik worden geprobeerd. Een groter bereik duurt langer. |
| Pauze na commando | Tijd die een wissel krijgt om om te gaan. |
| Dinamo: blokken | Blokadressen die geprobeerd worden bij het koppelen van secties aan blokken, bijv. `1-16`. |

Onder *Tijden en veiligheid* staan de wachttijden. Dit zijn de belangrijkste:

- **Max. seconden tussen melders (30):** komt er zo lang geen nieuwe melder, dan staat de loc tegen een stootjuk. Zodra er reistijden gemeten zijn, wacht de verkenner korter (3× de langste gemeten reistijd).
- **Geen melder bezet → kortsluiting na (3 s):** bij kortsluiting valt de spanning weg en daarmee ook de stroomdetectie. Heeft je baan onbewaakte stukken (wissels zonder melder) waar de loc langer "onzichtbaar" is, verhoog dit dan.

## Wat er gebeurt tijdens de verkenning

1. **Startmelder bepalen.** Bij Dinamo wordt de stand van alle melders opgevraagd.
2. **Blokkoppeling (alleen Dinamo).** Elk blok krijgt om beurten een rijcommando, tot er een melder verandert (maximaal 30 s per blok). Onderweg gebeurt dit opnieuw zodra de loc helemaal in een nieuwe sectie staat. De loc rijdt daarvoor eerst nog 1,5 s door en stopt dan. Daarna probeert hij steeds een klein stukje terug te rijden, zodat een fout blok al na een paar seconden duidelijk is.
3. **Alle wissels op rechtdoor.**
4. **Opdrachten.** Vanaf een melder in één richting:
   - eerst de **basisrit** (twee keer, ter controle), tot de loc doodloopt of weer op een bekende melder komt;
   - dan voor **elk nog onbekend adres**: omzetten, dezelfde rit, vergelijken, terugrijden, adres terugzetten.
     - Wijkt de rit af na melder X? Dan ligt daar een wissel (van de puntzijde bereden), en weet de verkenner welke stand waarheen gaat.
     - Geeft omzetten meteen een kortsluiting? Dan ligt daar een wissel die van achteren bereden wordt.
   - Elke nieuwe tak en elk doodlopend einde (andersom terug) wordt een nieuwe opdracht.
5. **Kortsluitpunten oplossen.** Loopt een basisrit op een kortsluiting, dan onthoudt de verkenner die plek en rijdt hij er voortaan niet meer overheen. Aan het eind probeert hij de plek op te lossen met de wissels die inmiddels bekend zijn, met maximaal 3 kortsluitingen per plek.

**Bij een kortsluiting** stopt alles direct (noodstop), en rijdt de loc zelf terug naar de laatste melder. Lukt dat niet, dan vraagt het programma je de loc met de hand terug te zetten.

**Onderbreken mag.** De voortgang wordt steeds bewaard. Met *Vorige verkenning hervatten* ga je later verder: zet de loc dan weer op de startmelder, met dezelfde kant vooruit.

## Het resultaat

- **Rapport bekijken:** opent het rapport in je browser: wissels (adres, ligging, standen), gereden trajecten met de wissels erin, melders met hun buren, voorgestelde blokken, kopsporen, kortsluitpunten en adressen zonder effect.
- **Rapport opslaan (HTML/tekst):** om te bewaren of te printen.
- **Baankaart opslaan:** het .json-bestand voor de latere import in Modeltreinbesturing.
- **Logboek opslaan:** alles wat er gebeurd is, met tijden.
- **Hardwarelog opslaan:** de ruwe communicatie met de centrale (zonder keepalives), handig bij problemen.

"Rechtdoor" en "afbuigend" in het rapport zijn de **commando's** die naar het adres gestuurd zijn. Is een wisselmotor omgekeerd aangesloten, dan is het fysiek andersom, maar de koppeling commando ↔ melder klopt altijd.

## Beperkingen, eerlijk gezegd

- **Wissels met ongepolariseerd puntstuk** die van achteren tegen de stand in bereden worden, geven geen kortsluiting. De loc rijdt de wissel dan stil open. De verkenner merkt dat pas op de terugweg (de loc gaat een andere kant op). Hij legt het vast als "opengereden wissel vermoed" en vraagt je dan één keer de loc terug te zetten.
- **Een adres zonder gevonden effect** is geen bewijs dat er niets op zit. Het kan ook een wissel zijn die de loc alleen van achteren passeert zonder kortsluiting, of een wissel op een stuk dat de loc niet kon bereiken.
- **Reistijden** zijn globaal. Bij Dinamo gaan commando's één per 200 ms de deur uit, dus de metingen hebben daardoor wat speling.
- **Hoe lang het duurt** hangt vooral af van het aantal adressen en melders. Reken op een paar uur voor een middelgrote baan met 32 adressen.
- Draaischijven, keerlussen met ompoolmodule en kruiswissels met een eigen rijadres zijn nog niet specifiek getest.
