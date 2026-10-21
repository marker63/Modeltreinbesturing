# Afspraken voor Claude in dit project (Modeltreinbesturing, Marco - Miniworld Rotterdam)

Lees bij elke nieuwe sessie eerst: `Modeltreinbesturing/BUGS.md` (genummerd, geen bug twee keer
oplossen), `Modeltreinbesturing/KRUISWISSEL.md` (wat vaststaat over de kruiswissel 13/10 en wat
niet) en `Modeltreinbesturing/BLOKKENSCHEMA.md`.

## Vaste afspraken van Marco
- Altijd Nederlands, consistent (geen Spaanse/andere woorden ertussen).
- Alle bugs en dingen die problemen kunnen geven direct oplossen, niet laten liggen. Genummerd in BUGS.md.
- De bedrading van de baan hoeft niet gecontroleerd te worden (rijdt al jaren perfect met Koploper).
- Eerst zelf zo veel mogelijk testen/simuleren voordat Marco er tijd aan besteedt. Wat niet getest
  kon worden altijd expliciet "nog niet getest op de baan" noemen.
- Nieuw blok op de baan toevoegen = in dezelfde ronde ook seinen en dienst bijwerken.
- Wisselstanden/kruiswissel-standen en rijrichtingen die Marco heeft vastgesteld nooit overschrijven
  of "spiegelen"; bij twijfel vragen. Eerder vastgestelde gegevens altijd opzoeken en meenemen.
- Wissel van de samengevoegde kant (puntzijde) bereden: omzetten lost een kortsluiting NIET op. Wissel vanaf een gesplitste tak richting de samenvoeging bereden: omzetten kan de kortsluiting juist opheffen. Bij een kortsluiting daarom eerst die wissels omzetten (BUG #169).
- Rijrichting (vooruit/achteruit) per overgang A -> B staat in `BlokRelatie.RijrichtingVooruit` (BUG #49).
- Hardware: Dinamo op COM10, 19200, odd parity. Sessielogs staan automatisch in
  `%AppData%\Modeltreinbesturing\Logs`.
- Levering: commit + push, sync project2, zip (bin/obj/.git uitgesloten) naar Marco sturen.
- Commitberichten eindigen met de Co-Authored-By/Claude-Session regels.
- Compile-controle zonder NuGet: `dotnet run --project Tools/CompileCheck` (dotnet-sdk-8.0 via apt) draait de
  regressietests op Kern/Hardware/importers met stubs. Het WPF-project zelf kan hier niet gecompileerd worden.
- Wisselstanden gelden in beide richtingen (Marco 17-10): staat 5→12 vast met wissel 2 (en 1) afbuigend, dan moet 12→5 dezelfde stand krijgen, ook in blokkenschema en wisselstraten. Staat als open punt in BUGS.md; in de volgende versie meenemen.
- Fysiek ontdekt/niet ontdekt (Marco 18-10): wissels 3, 4, 7, 8, 11 en 12 bestaan op de baan niet (niet ontdekt). Dit is ALLEEN kennis om logs en rapporten beter te analyseren: nooit hard coderen of uit de kaart/instellingen verwijderen, het programma moet zelflerend blijven. Een vondst voor zo'n adres is dus een aanwijzing voor een valse vondst (zoals #210), geen reden om het adres uit te sluiten.
- Instellingen die uit de logs/waarnemingen volgen (Marco 18-10 15:01): niet aan Marco vragen om ze in te vullen, maar zelf als eenmalige aanpassing toevoegen in `Baanverkenner.Kern/EenmaligeAanpassingen.cs` (vaste sleutel, één keer per instellingenbestand, Marco kan het daarna in het scherm terugdraaien). Blijkt er later een fout, dan op dezelfde manier een nieuwe eenmalige aanpassing. Bekend: wissel 6 is omgekeerd aangesloten (Dinamo 'afbuigend' = baan rechtdoor).
- Overloopset (Marco 20-10 18:32): wissel 5 en 6 staan op de baan altijd dezelfde stand (eenmalige aanpassing "259-overloop-5-6", instelling Gekoppelde wissels 5+6). Wissel 6 is omgekeerd aangesloten: zelfde stand op de baan = tegenovergesteld Dinamo-commando. Altijd fysiek (baan) en softwarematig (Dinamo) uit elkaar houden.
- Testloc (Marco 21-10 17:17): de loc is NIET langer dan een melder. Twee melders bezet = loc staat op de grens van twee secties (vaak met een draaistel bij een wissel).
