# Lijst: Dinamo voert wissel-commando's niet uit (uitval)

Bijgehouden op verzoek van Marco (15-10-2026 07:59). Eén regel per uitval: wanneer het begon, wat het laatste commando met terugmelding was, wat het eerste zonder terugmelding was en wat er daarna gebeurde.
Een uitval is: Dinamo meldt wissel-commando's niet meer terug (terugmelding `4A 94 xx` ontbreekt) en de wissels gaan niet om. Melders, snelheid en stop blijven meestal werken.

| Datum | Begin uitval | Laatste commando met terugmelding | Eerste commando zonder terugmelding | Verloop / opmerking | Bron |
|---|---|---|---|---|---|
| 15-10-2026 | 07:55:16 (na 29 min 46 s sinds verbinden, 44 wisselcommando's) | wissel 8 rechtdoor, 07:55:15.329 | wissel 9 afbuigend, 07:55:16.330 | Daarna ook wissel 9 rechtdoor (07:56:53) en wissel 10 afbuigend (07:56:55) zonder terugmelding. Melding "DINAMO VOERT WISSELCOMMANDO'S NIET UIT" om 07:56:56. Hoortest 07:57:29: wissel 1 heen en weer, 0 terugmeldingen, Marco hoorde de wissel niet omgaan. Verkenning gestopt 07:57:48. Melders en snelheidscommando's werkten de hele tijd gewoon door. 264 terugmeldingen voor de uitval. | hardwarelog + baanverkenner-log 15-10 07:58 |
| 14-10-2026 | meerdere (ongeveer 12 uitvalmomenten in alle hardwarelogs van die dag) | - | 8 van de 12 begonnen bij een commando naar wissel 9 (of 10) | Zie onderzoek in BUGS.md (na #134). Korte uitvallen: wachtrij wordt later alsnog uitgevoerd; lange uitvallen: tot het einde van de sessie. | BUGS.md |
| 14-10-2026 (log 0904) | 08:47:23 | onbekend | onbekend | Terugmeldingen kwamen tot het einde van de sessie (09:00) niet meer terug (25 commando's). Zie #126. | BUGS.md #126 |
| 14-10-2026 (log 1022) | 10:19:38 | onbekend | onbekend | 32 commando's lang geen terugmelding. Na ongeveer 2 u 10 min. Zie #126. | BUGS.md #126 |
| 14-10-2026 (log 0943) | hele sessie | - | - | Geen enkele terugmelding in de hele sessie. Zie #126. | BUGS.md #126 |
| eerder (3, 4, 10, 11 oktober) | niet uit te maken | - | - | Vaak lange reeksen zonder terugmelding; toen was niet altijd duidelijk of de terugmelding bestond of werd vastgelegd. | BUGS.md #126 |

## Wat het patroon tot nu toe zegt
- 15-10: 1 t/m 8 werken, het eerste commando naar 9 is het begin van de uitval (het kan zijn dat wissel 9 en hoger op een tweede print / ander adresblok zitten dan 1 t/m 8; dat is een hypothese).
- 14-10: 8 van de 12 uitvalmomenten begonnen bij wissel 9 of 10. Dat past erbij.
- Het ligt niet aan kortsluiting, F-bit, toggle-bit of tellers in ons protocol (BUGS.md, onderzoek na #134).
- Nieuw: er stond dit keer GEEN regel 'Diagnose uitval (BUG #138)' in het hardwarelog. De diagnose gaat alleen af bij 'Dinamo antwoordt niet', niet bij 'wisselcommando's zonder terugmelding'. Aangepast in BUG #154.

Nieuwe uitvallen worden hier onderaan de tabel toegevoegd (nieuwste bovenaan).
