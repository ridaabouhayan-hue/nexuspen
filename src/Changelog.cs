namespace NexusPen
{
    // ------------------------------------------------------------------ Wat is er nieuw (te zien bij Instellingen > Bijwerken)
    // Per versie een paar korte regels, elk met een icoontje. Zit in het programma zelf: ook zonder internet te lezen.
    // Bij een nieuwe versie bovenaan een blok toevoegen (de test controleert dat het bovenste blok Program.Version is).
    static class Changelog
    {
        public class Version { public string Number, Date; public string[][] Lines; }

        static Version V(string n, string d, params string[][] lines) { Version v = new Version(); v.Number = n; v.Date = d; v.Lines = lines; return v; }
        static string[] I(string icon, string text) { return new string[] { icon, text }; }

        public static readonly Version[] All = {
            V("1.3.4", "7 november 2026",
                I("tekst", "Tekst ingedrukt verplaatsen, zonder streepje en zonder kader"),
                I("zoom", "Vergrootglas: ronde hoeken, je muis één keer, staat boven de plek die het vergroot"),
                I("vink", "\"Opgeslagen\" rechtsonder als je iets verandert in Instellingen"),
                I("info", "Rustigere meldingen met een passend icoontje"),
                I("download", "Hier: wat er in elke versie nieuw is"),
                I("tekst", "Kortere teksten in Instellingen")),
            V("1.3.3", "7 november 2026",
                I("verplaats", "Instellingen scrollen: muiswiel, slepen, schuifbalkje of toetsen"),
                I("download", "Nieuwe pagina Bijwerken"),
                I("terugzetten", "Terugzetten staat bij Hulp en foutlog")),
            V("1.3.2", "7 november 2026",
                I("download", "Bijwerken met een klik, met controle van het bestand"),
                I("vink", "Je tekening blijft staan bij het bijwerken")),
            V("1.3.1", "7 november 2026",
                I("kleuren", "Kleuren slepen, weghalen en toevoegen"),
                I("vormen", "Venstertjes met alleen icoontjes"),
                I("stempel", "Nieuw stempel: zo-zo"),
                I("zoom", "Zoomen op het bord met Ctrl+plus en Ctrl+min"),
                I("map", "Zelf een map kiezen voor schermafbeeldingen"),
                I("aftekenen", "Instellingen naar een andere computer")),
            V("1.3.0", "7 november 2026",
                I("timer", "Timer, ook te zien bij schermdelen"),
                I("zoom", "Vergrootglas"),
                I("kleuren", "16 kleuren, in je eigen volgorde"),
                I("tekst", "Woorden slepen zoals in Word"),
                I("zandloper", "Vervagen als aan/uit-knop"),
                I("laser", "Laser: snel weg, langzaam of blijft staan")),
            V("1.2.9", "6 november 2026",
                I("gum", "Het rondje van de gum groeit mee"),
                I("selecteer", "Duidelijkere gekozen knop")),
            V("1.2.8", "6 november 2026",
                I("rechthoek", "Smallere werkbalk"),
                I("kleuren", "Rustige thema's: Rustig wit en Rustig zwart"),
                I("tekst", "Meteen typen na een klik met Tekst")),
            V("1.2.7", "6 november 2026",
                I("toetsen", "Elke sneltoets los uit te zetten"),
                I("rechthoek", "Balk inkorten knop voor knop"),
                I("opslaan", "Bewaren is robuuster")),
            V("1.2.6", "6 november 2026",
                I("muis", "Balk slepen aan het logo"),
                I("prullenbak", "Alles wissen als eigen knop"),
                I("bord", "Zelf in- en uitzoomen op het bord")),
            V("1.2.5", "6 november 2026",
                I("tekst", "Tekst bewerken als in een tekstverwerker"),
                I("krijt", "Krijt als eigen knop"),
                I("gum", "Vier maten voor de gum")),
            V("1.2.4", "6 november 2026",
                I("verplaats", "Balk vrij slepen over het scherm"),
                I("laser", "Laser als neonlicht"),
                I("info", "Pagina Wat doet elke knop")),
            V("1.2.3", "6 november 2026",
                I("tekst", "Tekst en stempels groter of kleiner met het muiswiel"),
                I("focus", "Spotlicht rond of rechthoekig"),
                I("bord", "Zwart bord")),
            V("1.2.2", "6 november 2026",
                I("tekst", "Klik met Tekst en typ meteen"),
                I("rechthoek", "Werkbalk groeit mee op een groot scherm")),
            V("1.2.1", "6 november 2026",
                I("pen", "Inkt zit vast aan je muis"),
                I("vink", "Scherpere werkbalk")),
            V("1.2.0", "6 november 2026",
                I("rechthoek", "Nieuwe werkbalk, liggend of staand"),
                I("taal", "Nederlands, Engels, Frans en Spaans"),
                I("stempel", "Stempels, whiteboard en krijtbord")),
        };
    }
}
