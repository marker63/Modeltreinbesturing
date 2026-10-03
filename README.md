# Modeltreinbesturing + Baanverkenner

Open `Modeltreinbesturing.sln` in Visual Studio. De solution bevat vier projecten:

| Project | Wat |
|---|---|
| **Modeltreinbesturing** | Het besturingsprogramma (zie `Modeltreinbesturing/README.md`). |
| **Modeltreinbesturing.Hardware** | Gedeelde bibliotheek met alle hardware-koppelingen (Dinamo, DCC-EX, Intellibox, simulatie) en het communicatielog. Beide programma's gebruiken deze - een fix hoeft maar op één plek. |
| **Baanverkenner** | Los programma dat met één testloc de baan automatisch in kaart brengt: melders, wissels (alle adressen), kopsporen, lussen en de Dinamo-blokkoppeling. Zie `Baanverkenner/HANDLEIDING.md`. |
| **Baanverkenner.Kern** | De verkenlogica zonder scherm, plus een simulatie-proefbaan om zonder echte baan te oefenen. |

Het programma dat start bij F5 kies je met rechtermuisknop op het project → *Set as Startup Project*.

Licentie: MIT (zie `Modeltreinbesturing/LICENSE`).
