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
- Rijrichting (vooruit/achteruit) per overgang A -> B staat in `BlokRelatie.RijrichtingVooruit` (BUG #49).
- Hardware: Dinamo op COM10, 19200, odd parity. Sessielogs staan automatisch in
  `%AppData%\Modeltreinbesturing\Logs`.
- Levering: commit + push, sync project2, zip (bin/obj/.git uitgesloten) naar Marco sturen.
- Commitberichten eindigen met de Co-Authored-By/Claude-Session regels.
- Compile-controle zonder NuGet: `dotnet run --project Tools/CompileCheck` (dotnet-sdk-8.0 via apt) draait de
  regressietests op Kern/Hardware/importers met stubs. Het WPF-project zelf kan hier niet gecompileerd worden.
