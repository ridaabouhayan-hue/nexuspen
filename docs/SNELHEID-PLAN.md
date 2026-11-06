# NexusPen snappy maken: plan voor schrijven en klikken

Doel: NexusPen moet voelen als pen op papier. Je klikt en er gebeurt meteen iets. Je schrijft en de inkt zit vast aan je muis of pen. Dit plan zegt wat we daarvoor doen, in welke volgorde, en hoe we meten of het gelukt is.

Status: onderzoek en plan. Nog niet gebouwd. Bouwen pas na "begin met bouwen" van Rida.

## 1. Wat "snappy" betekent (de doelen)

| Wat | Nu (geschat) | Doel |
|---|---|---|
| Klik op een knop tot je iets ziet | 16 tot 30 ms | binnen 1 beeldje (16 ms) |
| Muis of pen bewegen tot de inkt er staat | 16 tot 50 ms, meer bij lange lijnen | binnen 1 beeldje, ook bij lange lijnen |
| Gemiste punten bij snel schrijven | soms hoekjes | geen |
| Haperen met een volle tekening (300+ lijnen) | gum en lange lijnen haperen | nooit merkbaar |
| Processor als je niets doet | bijna niets (sinds 1.2) | bijna niets |

Waarom deze getallen: onder ongeveer 100 ms voelt een klik als "ik deed dat zelf". Voor schrijven is dat veel te traag: onderzoek van Ink & Switch zegt dat het pas rond 20 ms goed voelt, en dat 80 ms al heel traag aanvoelt. Voor slepen met de muis merken mensen vertraging vanaf ongeveer 34 tot 65 ms.

## 2. Wat er nu in NexusPen gebeurt (de oorzaken)

1. **Tekenen wacht op een timer.** Een muisbeweging zet alleen een vlaggetje (`dirty`). Het echte tekenen gebeurt in de timer van 15 ms. Windows geeft die timer de laagste voorrang en rondt af op ongeveer 15,6 ms. Elke beweging wacht dus 0 tot 16 ms extra, en meer als het druk is.
2. **De hele lijn wordt elke keer opnieuw getekend.** Tijdens het schrijven tekent NexusPen de hele huidige lijn opnieuw (DrawCurve over alle punten) en ververst een vlak zo groot als de hele lijn. Een korte lijn is snel, een lange zin wordt steeds trager.
3. **Punten gaan verloren.** Als het programma even bezig is, voegt Windows muisbewegingen samen. Snelle letters krijgen dan hoekjes in plaats van rondingen.
4. **Alles gaat via de processor (GDI+).** Dat is prima voor ons, maar dan moeten we zuinig zijn met hoeveel we per keer tekenen.
5. **Geen echte pendruk.** Met een pen of touchscreen weten we nu niet hoe hard er gedrukt wordt.

## 3. Het plan, stap voor stap

### Stap 1. Meten (eerst, anders weten we niet of het beter wordt)
- Een ingebouwde meter: tijd van muisbericht tot `UpdateLayeredWindow` klaar is, per beeldje. Bewaar p50 en p95 (de gewone en de slechtste tijden) in het foutlog bij afsluiten, en laat ze zien in Hulp en foutlog.
- In de zelftest: een nep-lijn van 2000 punten tekenen, een tekening met 500 lijnen gummen, en de tijden vastleggen. Deze tijden mogen in GitHub Actions niet slechter worden dan de vorige keer.

### Stap 2. Direct tekenen bij elke beweging (grootste winst)
- In `OnMouseMove` niet meer wachten op de timer. Teken meteen het nieuwe stukje, maar hooguit eens per ongeveer 4 ms (komen er sneller berichten, dan het laatste stukje meenemen in de volgende keer).
- **Natte en droge inkt.** Tijdens het schrijven tekenen we alleen het nieuwste stukje van de lijn (de laatste 2 of 3 punten) in de buffer, en verversen alleen dat kleine vlak. Als je loslaat, wordt de lijn een keer helemaal netjes getekend ("droog"). Zo blijft een lange zin net zo snel als een korte streep.
- De timer blijft alleen voor dingen die vanzelf bewegen (laser, vervagen, opslaan).

### Stap 3. Geen gemiste punten
- Overstappen op de pointerberichten van Windows (`EnableMouseInPointer`, `WM_POINTERDOWN`, `WM_POINTERUPDATE`, `WM_POINTERUP`), werkt vanaf Windows 8. Met `GetPointerInfoHistory` krijgen we alle tussenpunten die Windows heeft samengevoegd.
- Hetzelfde bericht geeft voor een pen ook de druk (0 tot 1024 via `GetPointerPenInfo`), de gum aan de achterkant van de pen en de knop op de pen. Gum-kant van de pen = gum, zonder knop te kiezen.
- Terugval: lukt dit niet op een pc, dan gewoon de muisberichten zoals nu. `GetMouseMovePointsEx` gebruiken we niet, die is sinds Windows 8 onbetrouwbaar volgens de makers van Paint.NET.

### Stap 4. Gladde lijnen zonder vertraging
- **One Euro filter** op de punten: bij langzaam schrijven veel gladmaken (geen trillende letters), bij snel schrijven bijna niet (geen achterlopende inkt). Het is goedkoop en gemaakt voor precies dit.
- Tussen de punten een zachte bocht (Catmull-Rom), per stukje uitgerekend in plaats van over de hele lijn.
- **Pen-gevoel (instelling, standaard aan voor de pen, uit voor markeerstift):** dikte volgt de snelheid (snel = iets dunner) of de echte pendruk, met een zacht begin en einde. Dit is hoe "perfect-freehand" (gebruikt in tldraw) zijn mooie lijnen maakt. Wij nemen het idee over, de code schrijven we zelf in C#.
- Een minimale afstand tussen punten (bijvoorbeeld 0,75 pixel) zodat we niet duizenden dubbele punten bewaren.

### Stap 5. Klein stukje vooruit voorspellen (optioneel, na stap 2 tot 4)
- Teken 8 tot 15 ms vooruit waar de muis waarschijnlijk heen gaat, als dun voorlopig stukje. Komt het echte punt binnen, dan vervangt dat het voorspelde stukje. Windows Ink doet dit standaard 15 ms vooruit, en zet het soms terug naar 8 ms als het te vaak misgaat.
- Alleen bij pen en snel bewegen, en uit te zetten in de instellingen.

### Stap 6. Klikken die meteen iets doen
- Knoppen reageren al bij indrukken (niet bij loslaten). Goed zo houden.
- Na een klik de knop **direct** opnieuw tekenen (`Update()` meteen na `Invalidate()`), zodat de oranje "aan"-kleur er binnen hetzelfde beeldje staat.
- De cursor wisselt meteen bij het kiezen van een gereedschap, nog voor de rest bijgewerkt is.
- Pop-ups (kleur, stempel, vormen, bord) bij het opstarten al een keer onzichtbaar maken en daarna alleen tonen of verbergen. Openen is dan direct.
- **Opstarten en eerste klik sneller:** .NET vertaalt code pas bij het eerste gebruik. Met `ProfileOptimization` (zit in .NET 4.5 en hoger) onthoudt NexusPen welke code nodig is en vertaalt die bij de volgende start alvast op de achtergrond. Daarnaast bij het opstarten een keer onzichtbaar een lijn, een tekst en een stempel tekenen, zodat de eerste echte keer niet hapert.
- Het verwijderen met de gum per beeldje bundelen: alles wat de gum in een beeldje raakt in een keer weghalen en een keer verversen.

### Stap 7. Zuinig blijven
- Geen `timeBeginPeriod(1)` voor het hele programma, dat maakt de hele pc onzuiniger. Hooguit tijdens het schrijven, en weer uit bij loslaten.
- Rustige timer als er niets gebeurt (zit al in 1.2).
- Grenzen (bounds) per lijn onthouden, zodat gummen en selecteren niet steeds alles opnieuw uitrekenen.

### Later, alleen als het daarna nog niet goed genoeg is
- **Tekenen via de videokaart** (Direct2D met DirectComposition) of **Windows Ink voor desktop** (`InkDesktopHost`). Dat is de snelste route, Windows tekent de natte inkt dan zelf op een eigen draad. Nadeel: een grote verbouwing en lastig vanuit C# 5 zonder extra pakketten. Pas overwegen als stap 1 tot 6 niet genoeg zijn.

## 4. Volgorde en wat Rida merkt

| Versie | Wat | Wat je merkt |
|---|---|---|
| 1.2.1 | Stap 1, 2 en 6 | Inkt zit vast aan de muis, ook bij lange zinnen. Knoppen reageren meteen. |
| 1.2.2 | Stap 3 en 4 | Mooie ronde letters, ook bij snel schrijven. Pen met druk op een touchscreen of tekentablet. |
| 1.2.3 | Stap 5 en 7 | Nog net iets directer met een pen. Laptop blijft koel. |

Elke stap: eerst meten, dan bouwen, dan weer meten. Wordt het niet sneller, dan niet doorvoeren.

## 5. Wat Rida kan testen
- Snel "Ik ga met de trein" schrijven met de muis en met het touchpad. Zijn de bochten rond? Loopt de inkt achter?
- Een lange zin over het hele scherm schrijven. Wordt het aan het eind trager?
- 5 minuten lesgeven met veel tekeningen, daarna gummen. Hapert het?
- Op een knop klikken en kijken of hij meteen oranje is.
- Als je een pen of touchscreen hebt: hard en zacht drukken.

## Bronnen
- Ink & Switch, Slow Software (hoe snel schrijven moet voelen): https://www.inkandswitch.com/slow-software
- Jakob Nielsen, 0,1 seconde voelt direct: https://jakobnielsenphd.substack.com/p/time-scale-ux
- Microsoft, PointerPredictor (vooruit voorspellen, standaard 15 ms): https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.input.pointerpredictor
- Microsoft, InkModelerAttributes (gladmaken, voorspellen, druk): https://learn.microsoft.com/en-us/uwp/api/windows.ui.input.inking.inkdrawingattributes.modelerattributes
- Microsoft, POINTER_PEN_INFO (pendruk 0 tot 1024): https://learn.microsoft.com/windows/win32/api/winuser/ns-winuser-pointer_pen_info
- Microsoft, InkDesktopHost (Windows Ink in desktopprogramma's): https://learn.microsoft.com/en-us/windows/win32/input_ink/inkdesktophost
- Raymond Chen, gemiste muisbewegingen terughalen: https://devblogs.microsoft.com/oldnewthing/?p=8103
- Paint.NET over GetMouseMovePointsEx en WM_POINTER: https://blog.paint.net/2019/11/14/paint-net-4-2-6-alpha-build-7258/
- One Euro filter (gladmaken met weinig vertraging): https://engine.needle.tools/docs/api/OneEuroFilter
- perfect-freehand (lijnen met druk en zachte uiteinden): https://github.com/steveruizok/perfect-freehand
- Microsoft Edge, inkt met voorspelling en de compositor: https://blogs.windows.com/msedgedev/?p=24844
