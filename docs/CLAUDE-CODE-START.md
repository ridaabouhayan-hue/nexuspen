# NexusPen: verder met Claude Code op de server

## Wat jij doet
1. Open Claude Code op de website (claude.ai/code) en kies de repository `ridaabouhayan-hue/nexuspen`.
2. Plak de opdracht hieronder en druk op Enter.
3. Claude Code werkt in de cloud. Hij kan NexusPen daar niet zelf starten, want dat is geen Windows. Hij bouwt en test via GitHub Actions op een echte Windows-machine en bekijkt de schermafbeeldingen die daar gemaakt worden. Jij test daarna op je eigen pc.

## Opdracht om te plakken

```
Je gaat verder met NexusPen, mijn eigen tekenprogramma over het scherm (vervanger van Epic Pen). Je werkt in de cloud in deze repository, niet op mijn pc.

1. Lezen
- Lees eerst CLAUDE.md en BOUWPLAN.md helemaal. Daar staan alle afspraken en keuzes. Kijk ook in docs/ontwerp/ (borden 10 t/m 18 gaan voor op oudere) en lees docs/SNELHEID-PLAN.md.
- Code staat in src/ (C# 5, WinForms, .NET Framework 4.8, geen NuGet). Tests in tests/Tests.cs. Vertalingen: tools/vertalingen.txt, daarna python3 tools/check_vertalingen.py (maakt src/LangData.cs en meldt wat ontbreekt).

2. Zo bouw en test je (je zit op Linux, NexusPen is voor Windows)
- Snel controleren in de cloud: installeer mono (apt-get install -y mono-devel als dat kan) en compileer met mcs -langversion:5 -r:System.Drawing.dll -r:System.Windows.Forms.dll, net als build/build.ps1. Draai de tests met mono. Let op: mono kan de ingebouwde lettertypes niet tekenen, dat is normaal.
- De echte controle is GitHub Actions (.github/workflows/build.yml, windows-latest): bouwen, tests, de zelftest (NexusPen.exe --zelftest) en de installer. Na elke push: kijk met gh api repos/ridaabouhayan-hue/nexuspen/actions/runs of de run groen is.
- De zelftest maakt schermafbeeldingen van de echte Windows-versie en zet ze op de tak zelftest-schermen. Haal ze op met git clone --depth 1 -b zelftest-schermen in een aparte map en bekijk ze. Zo zie je hoe het er op Windows uitziet. Bijlagen (artifacts) downloaden lukt vanuit de cloud meestal niet, gebruik daarom die tak.
- Wil je iets nieuws zien op Windows, voeg dan een stap toe aan RunSelfTest in src/Overlay.cs die een schermafbeelding maakt.

3. Stand van zaken
- Versie 1.2.0 staat op main en alles is groen.
- De versietag v1.2.0 is nog niet gemaakt. Probeer git tag v1.2.0 en git push origin v1.2.0. Lukt pushen van een tag niet vanuit de cloud, zeg dat dan tegen mij. Dan maak ik hem zelf op GitHub via Releases, Draft a new release, tag v1.2.0, Publish. Daarna zet GitHub Actions er vanzelf NexusPen-Setup.exe en NexusPen.exe bij. Controleer dat ze er staan.

4. Volgende werk (pas bouwen als ik "begin met bouwen" zeg)
- Versie 1.2.1 volgens docs/SNELHEID-PLAN.md, stap 1, 2 en 6: eerst een meter voor de snelheid, dan direct tekenen bij elke muisbeweging (natte en droge inkt), dan klikken die meteen iets doen. Leg mij eerst in gewone woorden uit wat je gaat doen.
- Open punten uit de code-review van 1.2.0:
  - autosave op het bord ververst nu het hele scherm, alleen de regel "Automatisch bewaard" bijwerken;
  - grenzen (bounds) per lijn bewaren in plaats van steeds uitrekenen;
  - vervagende lijnen niet elke frame klonen;
  - bordpagina's ook bewaren als vorige-bord-N.dat;
  - bij verwijderen vragen of %APPDATA%\NexusPen weg mag;
  - GitHub Actions: acties op een vaste versie zetten en schrijfrechten alleen voor de Release.

5. Werkafspraken (belangrijk)
- Schrijf in het Nederlands. Gebruik nooit het lange gedachtestreepje, ook niet in code, commentaar of commits.
- Ik ben geen programmeur. Leg kort en in gewone woorden uit wat je doet en wat ik moet doen.
- Neem zelf de technische beslissingen. Vraag mij alleen wat alleen ik kan weten.
- Vraag altijd eerst voordat je iets groots bouwt of verandert. Begin pas als ik "begin met bouwen" zeg. Uitzoeken, lezen en kleine fouten herstellen mag direct.
- Na elke wijziging: lokaal compileren en tests met mono, daarna pushen naar main en wachten tot GitHub Actions groen is. Bekijk de nieuwe schermafbeeldingen voordat je zegt dat iets klaar is.
- Gebruiksgegevens blijven lokaal. Nooit inhoud van het scherm of wat ik schrijf opslaan of versturen.
- Na elke nieuwe versie: een aparte code-review (veiligheid, stabiliteit, snelheid) voordat ik hem krijg.
- Als je klaar bent met een versie: zeg mij precies wat ik op mijn pc moet testen.

Begin met stap 1, 2 en 3 en vertel mij daarna kort wat je gevonden hebt.
```
