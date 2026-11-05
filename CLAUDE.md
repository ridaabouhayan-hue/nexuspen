# NexusPen: werkafspraken voor Claude Code

NexusPen is een lichtgewicht tekenprogramma over je scherm (vervanger van Epic Pen) voor Rida Abouhayan, docent NT2 bij Nexus of Language.

## Altijd eerst lezen
1. `BOUWPLAN.md`: alle afgesproken keuzes. Dit gaat voor bij twijfel.
2. `ontwerp/`: afbeeldingen van de borden uit het design canvas. In `ontwerp/html/` staat de bron van elk bord, ook van de interactieve borden (6 Tekst, 7 Timer, 8 Thema's). Nieuwere borden (10 t/m 18) gaan voor op oudere.

## Werkafspraken met Rida
- Schrijf in het Nederlands. Gebruik nooit het lange gedachtestreepje, ook niet in code-commentaar, teksten of commits.
- Vraag altijd eerst voordat je iets groots bouwt of verandert. Begin pas met bouwen als Rida "begin met bouwen" zegt.
- Rida is geen programmeur. Leg kort en in gewone woorden uit wat je doet en wat hij moet doen.
- Neem zelf de technische beslissingen en vraag alleen wat alleen Rida kan weten.

## Techniek
- C# 5 met WinForms op .NET Framework 4.8. Dat zit al in Windows, er hoeft niets extra geïnstalleerd te worden.
- `start/NexusPen.cs` is een eerste opzet van 1.2. Die compileert en de tests slagen, maar hij volgt het nieuwste ontwerp nog niet helemaal (sneltoetsen Ctrl+Shift, inklappen, inkorten, slepen tussen balk en bak, taal en gebruik ontbreken nog). Gebruik hem als startpunt en splits hem op in nette bestanden.
- `start/NexusPen-1.1.cs` is de huidige werkende versie, ter vergelijking.
- Icoontjes: Lucide (ISC). Neem de paden letterlijk over uit lucide-static (zie `BOUWPLAN.md`). De SVG-lezer moet path, circle, rect, line, polyline en ellipse kunnen lezen.
- Lettertypes: `fonts/` (Andika en Nunito, SIL OFL), ingebouwd als resource.
- Logo: `logo/` (SVG, PNG en nexuspen.ico). Het witte potlood is de goedgekeurde versie.
- Tests: `tests/Tests.cs`.

## GitHub
- Privé repository `nexuspen`.
- GitHub Actions op `windows-latest`: bouwen, tests draaien en een installer maken (NexusPen-Setup.exe, bijvoorbeeld met Inno Setup, zonder beheerdersrechten, in %LOCALAPPDATA%\Programs\NexusPen).
- Bij een versietag (v1.2.0) een GitHub Release maken met NexusPen-Setup.exe en een losse NexusPen.exe.
- Na elke versie: een aparte code-review (veiligheid, stabiliteit, snelheid) voordat Rida hem krijgt.

## Gebruik
- Gebruiksgegevens alleen lokaal (`%APPDATA%\NexusPen\gebruik`). Nooit inhoud van het scherm of wat er geschreven wordt. Er gaat niets naar internet, behalve de optionele check op een nieuwe versie.
