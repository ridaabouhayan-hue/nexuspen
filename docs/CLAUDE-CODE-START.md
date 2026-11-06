# NexusPen: verder met Claude Code op je eigen pc

## Wat jij doet (eenmalig)
1. Open Claude Code op je pc in een lege map, bijvoorbeeld `Documenten\Code`.
2. Plak de opdracht hieronder en druk op Enter.
3. Claude Code vraagt je om toestemming voor dingen als klonen, bouwen en pushen. Lees kort wat er staat en klik op Toestaan.

## Opdracht om te plakken

```
Je gaat verder met NexusPen, mijn eigen tekenprogramma over het scherm (vervanger van Epic Pen).

1. Klonen en lezen
- Kloon https://github.com/ridaabouhayan-hue/nexuspen (privé, ik ben ingelogd op GitHub). Werk in die map.
- Lees eerst CLAUDE.md en BOUWPLAN.md helemaal. Daar staan alle afspraken en keuzes. Kijk ook in docs/ontwerp/ (borden 10 t/m 18 gaan voor op oudere) en docs/SNELHEID-PLAN.md als die er staat.
- Code staat in src/ (C# 5, WinForms, .NET Framework 4.8, geen NuGet). Tests in tests/Tests.cs. Bouwen: powershell -ExecutionPolicy Bypass -File build\build.ps1. Opstarttest: out\NexusPen.exe --zelftest (maakt schermafbeeldingen in out\schermen). Vertalingen: tools\vertalingen.txt en daarna python tools\check_vertalingen.py (maakt src\LangData.cs).

2. Stand van zaken
- Versie 1.2.0 staat op main. GitHub Actions bouwt, test, draait de zelftest en maakt NexusPen-Setup.exe. Alles is groen.
- De versietag kon nog niet gemaakt worden. Vraag mij of je hem nu mag maken: git tag v1.2.0 en git push origin v1.2.0. Daarna maakt GitHub vanzelf een Release met NexusPen-Setup.exe en NexusPen.exe. Controleer dat de Release er staat.

3. Testen op mijn echte pc (dit kon in de cloud niet)
Bouw lokaal, start out\NexusPen.exe en loop samen met mij langs. Vraag mij wat ik zie als je het zelf niet kunt zien.
- Twee schermen en verschillende schaal (100 en 150 procent): staat alles scherp en op de goede plek?
- Tekenen met muis, touchpad (knijpen voor dikte) en eventueel pen of touchscreen.
- Zoom of Teams scherm delen: zien cursisten de werkbalk niet, maar de tekeningen wel?
- Starten met Windows, tonen in de taakbalk, inklappen via het logo, wegschuiven, inkorten.
- Instellingen: slepen tussen balk en bak, sneltoets opnemen (Ctrl+Shift+letter), taal wisselen.
- Opslaan, afsluiten, weer openen: staat de tekening er weer? Ook bord met pagina's.
- Installer: installeren zonder beheerdersrechten, starten via het startmenu, verwijderen.
Schrijf wat misgaat in docs\TESTLIJST.md (wat, waar, hoe vaak) en laat het mij zien.

4. Open punten uit de code-review (nog niet gedaan)
- Autosave op het bord tekent nu het hele scherm opnieuw. Alleen de regel "Automatisch bewaard" bijwerken.
- Grenzen (bounds) per lijn bewaren in plaats van steeds opnieuw uitrekenen, vooral voor tekst en stempels.
- Vervagende lijnen niet elke frame klonen, de doorzichtigheid meegeven aan Render.Draw.
- Bordpagina's ook bewaren als vorige-bord-N.dat, zodat Vorige tekening terughalen ook het bord terugzet.
- Bij verwijderen: vragen of mijn tekeningen en instellingen in %APPDATA%\NexusPen weg mogen.
- GitHub Actions: acties vastzetten op een vaste versie en schrijfrechten alleen voor het maken van de Release.

5. Werkafspraken (belangrijk)
- Schrijf in het Nederlands. Gebruik nooit het lange gedachtestreepje, ook niet in code, commentaar of commits.
- Ik ben geen programmeur. Leg kort en in gewone woorden uit wat je doet en wat ik moet doen.
- Neem zelf de technische beslissingen. Vraag mij alleen wat alleen ik kan weten.
- Vraag altijd eerst voordat je iets groots bouwt of verandert. Begin pas als ik "begin met bouwen" zeg. Testen, uitzoeken en kleine fouten herstellen mag direct.
- Na elke wijziging: bouwen, tests en zelftest moeten slagen. Daarna committen en pushen naar main en kijken of GitHub Actions groen is.
- Gebruiksgegevens blijven lokaal. Nooit inhoud van het scherm of wat ik schrijf opslaan of versturen.
- Na elke nieuwe versie: een aparte code-review (veiligheid, stabiliteit, snelheid) voordat ik hem krijg.

Begin met stap 1 en 2 en vertel mij daarna kort wat je gevonden hebt.
```
