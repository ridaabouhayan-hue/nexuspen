# NexusPen 1.2: bouwplan

Lichtgewicht tekenprogramma over je scherm, als vervanger van Epic Pen. Gemaakt voor Nexus of Language (Rida Abouhayan).
Dit document legt alle keuzes uit het design canvas vast. Het canvas is leidend voor het uiterlijk:
https://claude.ai/artifact/H9z7dfEfDjwDuioZ9niRt3

## Spelregels
- Pas bouwen als Rida "begin met bouwen" zegt. Grote keuzes eerst voorleggen.
- Alle teksten in de app in het Nederlands (plus vertalingen, zie Taal). Nooit het lange gedachtestreepje gebruiken.
- Na elke versie: automatisch bouwen en testen op Windows (GitHub Actions), daarna een aparte code-review.

## Techniek
- C# met WinForms op .NET Framework 4.8 (zit al in Windows 10 en 11, niets extra installeren). Doel: ongeveer 1 MB, weinig geheugen, vrijwel niets doen als je niet tekent.
- Tekenlaag: layered window (UpdateLayeredWindow, alleen het gewijzigde stuk verversen), doorklikken als je niet tekent.
- Per scherm de juiste schaal (PerMonitorV2), meerdere schermen.
- Windows Ink-gebaren uit (geen rechtsklik door lang drukken).
- Robuust: foutlog, automatisch bewaren, herstel na crash, waakhond die het scherm vrijgeeft als het programma vastloopt, Esc werkt altijd.
- Geen keyboard hooks. Sneltoetsen via RegisterHotKey (overal) en via de tekenlaag zelf (tijdens tekenen).
- Gaat zelf niet het internet op, behalve de optionele check op een nieuwe versie (GitHub Releases).
- Code opgesplitst in onderdelen (tekenlaag, werkbalk, instellingen, opslag, iconen, thema's, taal, gebruik), met tests.
- Icoontjes: Lucide (ISC-licentie), 24-raster, lijndikte 2, ronde uiteinden. Paden letterlijk uit Lucide overnemen.
- Lettertypes ingebouwd: Andika en Nunito (SIL OFL), plus de lettertypes van Windows.
- Licenties van Lucide, Andika en Nunito meeleveren.

## Logo
- Inkt-N met wit potlood: de N en het potlood zijn één witte vorm, alleen de grafietpunt is donker.
- Raster van 64, veilige zone 8, letter van 15,5 tot 48,5 en van 13 tot 51. Kleine versie onder 24 px.
- In de app volgt de tegel de accentkleur van het thema. De N wordt donker op een heel lichte accentkleur.
- Logopakket: NexusPen-logo.zip (SVG, PNG, ICO).

## Werkbalk (ontwerp D, Studio-balk)
- Liggend of staand. Groottes Klein, Normaal en Groot. Rond of strak. Dekkend, 90% of 80%.
- Volgorde: logo | muis, selecteren | pen, markeerstift, laser | vormen, tekst, gum | kleur, stempel, spotlicht, vergrootglas | ongedaan, bord, timer, schermafbeelding | opslaan, verbergen, alles wissen, instellingen, afsluiten, wegschuiven.
- Het logo en Instellingen staan altijd in de balk (ook ingekort); Instellingen kan niet naar de bak.
- Geen sleeppuntjes: het logo is de greep. Klik op het logo = alles inklappen, slepen = de balk verplaatsen. Tegen de zijkant wordt de balk vanzelf staand, tegen boven of onder liggend.
- In de bak (zelf toe te voegen): krijt, lijn, pijl, rechthoek, cirkel, opnieuw.
- Gekozen knop: Rustig (standaard, een ingedrukt grijs vakje) of in de accentkleur. Het vakje is iets kleiner dan de knop.
- Rondom het logo is de afstand tot de rand overal gelijk.
- Popups (vormen, kleur, stempel, bord) gaan open als je met de muis over de knop gaat. Geen tooltips standaard (aan te zetten).
- Vormen: één knop met een rij lijn, pijl, rechthoek en cirkel. Tekst is een losse knop.
- Kleur: Fel en Zacht, elk 16 kleuren (van meest naar minst gebruikt) in rijen van 8, plus een schuifje voor de dikte. In Instellingen > Pen en kleuren: kleuren verplaatsen, weghalen, toevoegen (tot 24) of de standaard terugzetten. De toetsen 1 tot 0 kiezen de eerste tien.
- Vervagen is een aan/uit-knop met een zandloper; de tijd kies je in Instellingen. Laser: snel weg, langzaam, of blijft staan tot een nieuwe laserlijn, Esc of een ander gereedschap.
- Rechtsklikmenu's zijn zelf getekend (zelfde stijl als de venstertjes): overal dezelfde ruimte rondom, een rustig vinkje.
- Timer: tijd kiezen (min, plus en vaste tijden), starten en pauzeren, groot of klein, verslepen, zacht geluidje aan het eind. Zichtbaar bij schermdelen.
- Vergrootglas (Windows Magnification): gaat met de muis mee, klik = vastzetten (hoogstens drie), muiswiel = zoom, rechtsklik = rechthoek of rond en 2, 3 of 4 keer. Vastgezet glas heeft een balkje om te verslepen, te zoomen en te sluiten.
- Inklappen met het logo en weer uitklappen: je gaat precies verder waar je was (tekenen, gereedschap, bord, pagina).
- Muiswiel en knijpen op het touchpad veranderen de dikte, met een grens bij minimum en maximum.
- Selecteren (bord 2): aanklikken, verplaatsen, hoekjes slepen = groter of kleiner, dubbelklik op tekst = tekst wijzigen. Balkje erbij met kleuren, grootte min en plus, Rand aan of uit (bij tekst), kopie, naar voren, verwijderen.
- Pen-paneel (bord 4): Fel en Zacht, dikte-schuifje (1 tot 68), opties Krijtstreep (krijteffect) en Vervaagt na 5 s. Bij scrollen of knijpen verschijnt naast de cursor een tipje met de dikte.
- Stempel (bord 1): vinkje, kruis, ster, vraagteken en Nummer (telt vanzelf op: 1, 2, 3), in de maten S, M en L.
- Bord (bord 5): Scherm, Whiteboard of Krijtbord. Achtergrond Leeg, Schrijflijnen (voor Alfa), Ruitjes of Lijntjes. Meerdere pagina's met vorige, volgende en nieuwe pagina.
- Tooltip bij elke knop (als tips aan staan), met je eigen sneltoets erbij.
- Gum: groter of kleiner met muiswiel of knijpen. Bord: zelf in- en uitzoomen met min en plus (0,5 tot 6).
- Schermafbeelding naar Afbeeldingen\NexusPen en naar het klembord, met een melding onderin en de knop Map openen.
- Rechtsonder klein: "Automatisch bewaard" met tijd, en bij een bord ook de pagina.
- Bij twijfel gaat dit bouwplan voor op de borden, en gaan de nieuwere borden (10 t/m 18) voor op de oudere.

### Inklappen en wegschuiven (bord 10)
- Inklappen met het logo: ALLES verdwijnt (balk, tekeningen, tekst, whiteboard, schrijflijnen). Alleen het logo blijft staan, zonder kleurbolletje. Weer klikken: alles komt precies terug.
- Wegschuiven naar de rand: alleen de balk verdwijnt, de tekeningen blijven staan. Aan de rand komt een klein logo met een oranje lipje aan de randkant. Klikken: de balk komt terug.
- Rechtsklik op het logo: Uitklappen, Instellingen, NexusPen afsluiten.
- Logo ingedrukt houden en slepen = verplaatsen. De plek wordt onthouden.

### Inkorten en volgorde (borden 11 en 15)
- Sleeprandje aan het eind van de balk: het randje volgt de muis en de balk wordt knop voor knop korter of langer. Aan de rand van het scherm schuift de balk op, en blijf je tegen de rand, dan komt er steeds een knop bij. De knoppen erachter komen in een knopje met drie puntjes. Daar klik je alles terug.
- Rechtsklik op een knop: Verbergen uit de balk.
- Instellingen > Werkbalk: knoppen slepen tussen je balk (boven) en de bak (onder). Binnen de balk slepen = volgorde. Strepen sleep je net zo. Standaardbalk terugzetten.

## Sneltoetsen (bord 17)
- Standaard: Ctrl+Shift + letter voor gereedschap, zodat je nooit per ongeluk iets kiest als je typt. Instelbaar: Alleen een letter, of Uit.
- Overal: Ctrl+Shift+D tekenen aan of uit, Ctrl+Shift+H alles in- of uitklappen.
- Tijdens tekenen: Ctrl+Shift+P pen, M markeerstift, L laser, T tekst, E gum, V selecteren, A pijl, S stempel, F spotlicht, G vergrootglas, 1 t/m 0 kleur. Ctrl+Z ongedaan, Ctrl+Y opnieuw, Ctrl+S opslaan.
- Elke sneltoets zelf te wijzigen of los uit te zetten ("Uit"), met een melding als hij al in gebruik is (ook door een kleurtoets). In een tekstvak doen sneltoetsen niets. Esc sluit eerst een venstertje van de balk, en sluit ook Instellingen.

## Instellingen
Menu links: Algemeen, Taal, Uiterlijk, Werkbalk, Pen en kleuren, Tekst, Stempels, Bord en achtergrond, Voorlezen (vanaf 1.4), Sneltoetsen, Opslaan, Schermdelen, Gebruik, Hulp en foutlog.
- Algemeen: starten met Windows, tonen in de taakbalk, muiswiel verandert de dikte, elk gereedschap onthoudt zijn eigen dikte, tekening bewaren na afsluiten (Nooit, Na Opslaan, Altijd), melding bij een nieuwe versie, alles terugzetten.
- Taal: Nederlands, English, Français, Español. Direct toepassen zonder opnieuw te starten. Alle teksten in taalbestanden.
- Uiterlijk: 12 thema's (Nexus is standaard: #1C1C1C met NOL-oranje #FF8000), waaronder Rustig wit en Rustig zwart zonder kleur, plus een eigen accentkleur. Elke themakaart toont een stukje echte werkbalk. Gekozen knop: Rustig of Accentkleur.
- Tekst typen zoals in een tekstverwerker: woorden selecteren en naar een andere plek slepen (Ctrl = kopie). Met Tekst op een tekst drukken en bewegen = de hele tekst verplaatsen.
- Tekst: lettertype (Segoe UI, Andika aanbevolen, Atkinson Hyperlegible, Lexend, Nunito, Patrick Hand, of elk lettertype op de pc). Rand om tekst met dikte (dun, middel, dik) en kleur (zwart, wit, automatisch). Het voorbeeld toont het gekozen lettertype.
- Schermdelen: werkbalk onzichtbaar in Zoom en Teams (de tekeningen blijven zichtbaar).
- Opslaan: knop Opslaan (Ctrl+S). Na Opslaan staat de tekening er de volgende keer weer. Vorige tekening terughalen. Mappen openen.

## Gebruik bijhouden (bord 16)
- Alleen op deze pc, ongeveer 3 MB per jaar, per onderdeel aan of uit.
- Meten: klikken op knoppen (en of dat via de balk, een sneltoets of het rechtsklikmenu ging), gereedschap en tijd, kleuren en dikte, menu's en instellingen, twijfel (direct ongedaan maken, gummen na tekenen), lessen (wanneer en hoe lang, inklappen en wegschuiven), scherm en snelheid.
- Nooit: wat je schrijft of tekent, wat er op je scherm staat, namen of bestanden.
- Knop "Exporteren voor Claude", zodat Claude de gegevens kan analyseren en NexusPen kan verbeteren.

## Installeren (bord 13)
- NexusPen-Setup.exe, zonder beheerdersrechten, in je eigen map.
- Snelkoppeling in Start (en desgewenst op het bureaublad), starten met Windows, zichtbaar in de taakbalk zolang hij draait, icoon bij de klok.
- Verwijderen via Windows Instellingen > Apps. Bijwerken met dezelfde setup, instellingen en tekeningen blijven staan.
- Zonder betaalde handtekening geeft Windows de eerste keer een waarschuwing: Meer info > Toch uitvoeren.

## Later (versie 1.4 en verder)
- Voorlezen en tekst van het scherm lezen.
- Slimme vormen (rommelige vorm wordt netjes), klikken laten zien bij schermdelen, het hele scherm inzoomen.
- Bord als PDF opslaan, plaatjes plakken op het bord, sjablonen (e-mail, WhatsApp, formulier, woordweb).
- Rondleiding bij de eerste start, ondertekenen van het programma, bijwerken met een klik.
