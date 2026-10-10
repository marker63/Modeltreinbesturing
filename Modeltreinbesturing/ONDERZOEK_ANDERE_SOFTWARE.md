# Onderzoek andere modelspoorsoftware en DCC-standaarden (10-10-2026, nacht)

Doel: verbeteringen voor de Baanverkenner vinden bij Rocrail, Win-Digipet, JMRI, iTrain, BTrain en de NMRA/NEM/RCN-standaarden.
Werkwijze: een onderzoeksagent heeft de bronnen opgehaald; ik heb de samenvatting hieronder niet zelf regel voor regel tegen de bronnen
nagelezen. Wat staat als "zeker" is door de agent uit de bron gelezen; "vermoeden" is niet gecontroleerd. Niets hiervan is getest op de baan.

## Wat de bronnen zeggen (met bron)
1. **Geen enkel gevestigd pakket leert een baan automatisch.** iTrain (berros.eu/en/itrain/background.php), BTrain (github.com/jean-bovet/BTrain) en
   Rocrail hebben handmatig getekende of gedefinieerde blokken en routes. De Baanverkenner vult dus een gat. Rocrail-wikipagina's gaven deels geen
   tekst; wat daar mogelijk wel staat is niet gecontroleerd.
2. **Octrooi US 6.848.657** (image-ppubs.uspto.gov, "Dynamic self-teaching train track layout learning and control system"): vrijwel dezelfde aanpak
   als de onze. Een voertuig rijdt de baan af, elke detectie komt in volgorde in een tabel met buren links en rechts; bij wissels wordt elke stand
   doorgereden en het bereikte blok hoort bij die stand; een kolom "indirect" markeert blokken die elkaar uitsluiten bij kruisingen. Er staat niets
   in over wissels zonder terugmelding, kortsluiting of loclengte. Alleen als inspiratie; let op de juridische kant bij letterlijk overnemen.
3. **Engelse wissel.** Rocrail (wiki.rocrail.net/doku.php?id=switch-gen-en): "Double Crossing" = 2 motoren, 2 adressen, tot 4 standen; "Single Crossing"
   2 motoren, 3 standen. Of de twee adressen op afzonderlijke motoren worden afgebeeld en hoe de ingangskant meespeelt staat daar niet.
   dccwiki.com/Crossing: bij een dubbele kruiswissel wordt aanbevolen alle motoren van een route in een keer te zetten (routes).
   JMRI (EditLayoutSlip): een slip = twee wissels met per stand een toestand voor beide.
4. **Wisselstand zonder terugmelding.** JMRI onderscheidt "commanded" (laatst bevolen) en "known" (bevestigd), met INCONSISTENT als tussentoestand
   (jmri.org/help/en/html/doc/Technical/TurnoutFeedback.shtml). Win-Digipet-gebruikers lossen het zonder terugmelding op met een virtuele terugmelding
   (wissel geldt als bezet zolang een naburige melder bezet is) en bij routes met "alleen schakelen als de opgeslagen stand anders is".
   Rocrail vergrendelt een wissel zolang het bijbehorende blok niet vrij is, en wacht na een commando een vaste tijd (0-1000 ms) voor de frog-omschakeling.
5. **DCC (NMRA S-9.2.1, S-9.2).** Accessory-pakket heeft een aan/uit-bit; de actieve tijd per uitgang staat in CV 515-518. De norm schrijft geen vast
   aantal herhalingen voor: "as frequently as possible". Terugmelding van de stand valt buiten de basisnorm (RailCom/NEM 672, ESU SwitchPilot: de ECoS vraagt
   bij RailCom herhaaldelijk de stand op). RCN-213 (adressering) en RCN-217 (RailCom) kon de agent niet lezen. Of de Dinamo zich aan deze DCC-norm houdt is niet gecontroleerd.
6. **Loc langer dan de sectie.** Rocrail gebruikt per blok "enter" en "in" (volledig binnen); JMRI-blokvolging leidt uit de bezette buren en de richting af waar
   de trein staat; BTrain en Win-Digipet houden rekening met treinlengte. Over ontdenderen/flikkeren van melders vond de agent weinig.
7. RailCom-adresdetectie per melder zou per sectie de loc kunnen melden. Vermoeden: de Dinamo met blokgebonden rijstroom heeft dat niet.

## Wat we er mee doen (volgorde van waarde voor onze problemen)
| # | Idee | Status |
|---|------|--------|
| 1 | Onthouden welke wisselstand bij welke stap hoort en die bij navigeren afdwingen (kop én van achteren) | **Gebouwd als #77** (uit de log van 19:03: terugweg 14 -> 129 met wissel 5 rechtdoor gaf kortsluiting) |
| 2 | Ingangskant/doorgangen bij kruiswissel (octrooi: "indirect"-kolom, Rocrail: routes zetten alle motoren) | Gebouwd als #74 |
| 3 | Tweede bezette melder bij kortsluiting wijst de betrokken wissel aan | Gebouwd als #75 |
| 4 | Per wissel "bevolen" en "bekend" (JMRI) met toestand "onbekend" na kortsluiting of open rijden; onbekende wissels altijd opnieuw sturen vóór ze nodig zijn | Deels (na handmatig terugzetten alles opnieuw sturen, #74). Nog te bouwen: ook na kortsluiting/trailing door een verkeerd staande wissel |
| 5 | Wissels nooit omzetten zolang de loc op twee melders staat (staart nog op de wissel); eerst terugkruipen of vooruit tot één melder | Open, zie hieronder |
| 6 | Engelse wissel als eigen object: tabel (ingangskant, motor A, motor B) -> uitgang, alle motorcombinaties afgaan | Open; gegevens zitten al in de doorgangen |
| 7 | Overgang pas vastleggen na stabiele bezetting + ontdendertijd; overlap A/B als grensaanwijzing en schatting loclengte | Deels (MelderMonitor heeft een stabiele set); loclengte-schatting open |
| 8 | Wisselcommando's herhalen en na opstarten niet blind aannemen | Open; eerst meten (#65: aantal commando's staat in het log) |

## Open punten die uit de log van 19:03 volgen
- De omgekeerde-overgang-regel (X->Y gezien, dus Y->X ook) is voor wissels onwaar: 129->14 achteruit "lukte" met wissel 5 rechtdoor (van achteren door de
  tongen geduwd), maar 14->129 vooruit bestaat alleen met wissel 5 afbuigend. #77 corrigeert dat voor navigeren; verkennen zelf gebruikt het nog niet.
- De loc staat na zo'n rit met de staart nog op 129 (twee melders bezet). Wissel 5 omzetten terwijl de staart erop staat geeft kortsluiting (zie hypothese H3). Idee 5.
- Adressen 11, 12 en 13 geven ieder op zichzelf dezelfde uitkomst (13 -> 24 zonder 14). Waarschijnlijk zijn dat meerdere adressen op dezelfde motor/kruiswissel, of een
  ander verband; aan Marco vragen welke adressen fysiek bij elkaar horen.
