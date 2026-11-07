using System; using System.Collections.Generic; using System.Drawing; using System.Drawing.Imaging;
namespace NexusPen {
static class Tests {
  static int fails = 0;
  static void Check(bool ok, string name) { Console.WriteLine((ok ? "OK    " : "FOUT  ") + name); if (!ok) fails++; }
  static int Main() {
    // 1. Sneltoetsen inlezen (dit ging mis in versie 1.0)
    int mods, vk;
    Check(Settings.ParseHotkey("Ctrl+Shift+D", out mods, out vk) && mods == 6 && vk == 68, "Sneltoets Ctrl+Shift+D");
    Check(Settings.ParseHotkey("ctrl + alt + F9", out mods, out vk) && mods == 3 && vk == 0x78, "Sneltoets Ctrl+Alt+F9");
    Check(!Settings.ParseHotkey("Ctrl+Bestaatniet", out mods, out vk), "Ongeldige sneltoets wordt geweigerd");
    Check(!Settings.ParseHotkey("", out mods, out vk), "Lege sneltoets wordt geweigerd");

    // 2. Opslaan en terugladen
    var list = new List<Stroke>();
    foreach (Tool t in new[]{Tool.Pen,Tool.Marker,Tool.Line,Tool.Arrow,Tool.Rect,Tool.Ellipse,Tool.Text}) {
      Stroke s = new Stroke(); s.Tool=t; s.Color=Color.FromArgb(255,232,17,35); s.Width=4.5f; s.FontSize=22;
      s.Pts.Add(new PointF(10.25f,20)); s.Pts.Add(new PointF(300,400.5f)); s.Pts.Add(new PointF(150,60));
      if (t==Tool.Text) s.Text = "Héél goed! | ✓ één, twee\nregel 2";
      list.Add(s);
    }
    string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "np_test.dat");
    StrokeStore.Save(list, path); StrokeStore.Save(list, path); // twee keer: test ook vervangen
    var back = StrokeStore.Load(path);
    Check(back.Count == list.Count, "Aantal lijnen terug na opslaan: " + back.Count);
    Check(back[6].Text == list[6].Text, "Tekst met accenten, | en nieuwe regel blijft gelijk");
    Check(back[0].Color.ToArgb() == list[0].Color.ToArgb() && Math.Abs(back[0].Width-4.5f) < 0.01, "Kleur en dikte blijven gelijk");
    Check(Math.Abs(back[3].Pts[1].Y-400.5f) < 0.06 && back[3].Tool == Tool.Arrow, "Punten en gereedschap blijven gelijk");
    System.IO.File.AppendAllText(path, "rommel|zonder|zin\nPen|x|y|z|w|1,2\n");
    Check(StrokeStore.Load(path).Count == list.Count, "Kapotte regels worden overgeslagen zonder crash");
    Check(StrokeStore.Load(path + ".bestaatniet").Count == 0, "Ontbrekend bestand geeft lege tekening");

    // 3. Gum raakt de goede lijnen
    var line = new Stroke{Tool=Tool.Line, Width=4}; line.Pts.Add(new PointF(0,0)); line.Pts.Add(new PointF(100,0));
    Check(Render.Hit(line, new PointF(50,5), 8), "Gum raakt lijn dichtbij");
    Check(!Render.Hit(line, new PointF(50,40), 8), "Gum raakt lijn ver weg niet");
    var dot = new Stroke{Tool=Tool.Pen, Width=6}; dot.Pts.Add(new PointF(10,10));
    Check(Render.Hit(dot, new PointF(14,10), 8) && !Render.Hit(dot, new PointF(60,60), 8), "Gum en losse stip");

    // 4. Alles tekenen op een doorzichtig vlak (zoals de echte tekenlaag), ook rare gevallen
    using (var bmp = new Bitmap(800, 600, PixelFormat.Format32bppPArgb))
    using (var g = Graphics.FromImage(bmp)) {
      Render.Setup(g);
      bool ok = true;
      try {
        foreach (var s in back) Render.Draw(g, s, 1f);
        var laser = new Stroke{Tool=Tool.Laser, Color=Color.Red, Width=4}; laser.Pts.Add(new PointF(5,5)); laser.Pts.Add(new PointF(90,90)); laser.Pts.Add(new PointF(200,30));
        Render.Draw(g, laser, 0.37f); Render.Draw(g, laser, 0f);
        var zero = new Stroke{Tool=Tool.Rect, Color=Color.Blue, Width=2}; zero.Pts.Add(new PointF(50,50)); zero.Pts.Add(new PointF(50,50)); Render.Draw(g, zero, 1f);
        var arrow0 = new Stroke{Tool=Tool.Arrow, Color=Color.Blue, Width=2}; arrow0.Pts.Add(new PointF(50,50)); arrow0.Pts.Add(new PointF(51,50)); Render.Draw(g, arrow0, 1f);
        var ell0 = new Stroke{Tool=Tool.Ellipse, Color=Color.Blue, Width=60}; ell0.Pts.Add(new PointF(5,5)); ell0.Pts.Add(new PointF(5,5)); Render.Draw(g, ell0, 1f);
      } catch (Exception ex) { ok = false; Console.WriteLine(ex); }
      Check(ok, "Alle gereedschappen tekenen zonder fout, ook vormen van 0 pixels");
      Color px = bmp.GetPixel(300, 400);
      Check(bmp.GetPixel(799, 599).A == 0, "Lege plek blijft doorzichtig");
      bmp.Save(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "np_render_test.png"), ImageFormat.Png);
    }

    // 5. Nieuw in 1.2: oud bestand (1.1) blijft leesbaar
    string oldPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "np_oud.dat");
    System.IO.File.WriteAllText(oldPath, "NEXUSPEN1\nPen|-1497051|4|0||10,10 20,20 30,25\nText|-16777216|4|22|" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("Hallo")) + "|5,5\n");
    var old = StrokeStore.Load(oldPath);
    Check(old.Count == 2 && old[1].Text == "Hallo" && !old[1].Outline, "Tekening uit versie 1.1 laadt nog");

    // 6. Lettertype, zwarte rand en stempel blijven bewaard
    var t2 = new Stroke{Tool=Tool.Text, Color=Color.Yellow, Width=4, FontSize=30, Text="Rand", Outline=true, FontName="Andika"}; t2.Pts.Add(new PointF(40,40));
    var st = new Stroke{Tool=Tool.Stamp, Color=Color.Lime, Width=6, FontSize=Render.StampSize(6)}; st.Pts.Add(new PointF(400,300));
    StrokeStore.Save(new List<Stroke>{t2, st}, path);
    var b2 = StrokeStore.Load(path);
    Check(b2.Count == 2 && b2[0].Outline && b2[0].FontName == "Andika" && b2[1].Tool == Tool.Stamp, "Rand, lettertype en stempel blijven bewaard");

    // 7. Kopie bij verplaatsen: ongedaan maken houdt de oude plek
    var orig = new Stroke{Tool=Tool.Pen, Width=4}; orig.Pts.Add(new PointF(1,1)); orig.Pts.Add(new PointF(5,5));
    var copy = orig.Clone(); Render.Move(copy, 100, 0);
    Check(orig.Pts[0].X == 1 && copy.Pts[0].X == 101, "Verplaatsen raakt het origineel niet (ongedaan maken klopt)");
    Check(Render.Hit(st, new PointF(405,305), 4) && !Render.Hit(st, new PointF(600,300), 4), "Stempel is te selecteren");

    // 8. Instellingen met thema bewaren en terugladen
    string ini = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "np_test.ini");
    var cfg = new Settings(); cfg.ThemeIndex = Theme.Custom; cfg.CustomAccent = Color.FromArgb(255, 0x12, 0x34, 0x56); cfg.Vertical = true; cfg.SizeIndex = 2; cfg.TextOutline = true; cfg.FontName = "Nunito"; cfg.PaletteIndex = 1; cfg.ColorIndex = 9;
    cfg.Save(ini);
    var c2 = Settings.Load(ini);
    Check(c2.ThemeIndex == Theme.Custom && c2.CustomAccent.ToArgb() == cfg.CustomAccent.ToArgb() && c2.Vertical && c2.SizeIndex == 2 && c2.TextOutline && c2.FontName == "Nunito" && c2.PaletteIndex == 1 && c2.ColorIndex == 9, "Thema en werkbalk-instellingen blijven bewaard");
    Check(Theme.Get(c2).Accent.ToArgb() == cfg.CustomAccent.ToArgb(), "Eigen accentkleur wordt gebruikt");
    System.IO.File.WriteAllText(ini, "thema=99\ngrootte=-4\nkleur=55\n");
    var c3 = Settings.Load(ini);
    Check(c3.ThemeIndex == Theme.All.Length - 1 && c3.SizeIndex == 0 && c3.ColorIndex == Palette.MaxColors - 1, "Rare waarden in instellingen geven geen crash");
    int[] ord = (int[])Theme.Order.Clone(); Array.Sort(ord); bool ordOk = ord.Length == Theme.All.Length && Theme.Order[Theme.Order.Length - 1] == Theme.Custom; for (int oi = 0; oi < ord.Length; oi++) if (ord[oi] != oi) ordOk = false;
    Check(ordOk, "Elk thema staat precies een keer op de pagina Uiterlijk, Eigen als laatste");
    Check(Theme.All.Length == 12 && Theme.All[9].Name == "Eigen" && Theme.All[0].Name == "Nexus" && Theme.All[0].Accent.ToArgb() == Color.FromArgb(255, 0xFF, 0x80, 0x00).ToArgb(), "Nexus is het eerste thema met NOL-oranje");

    // 9. Icoontjes uit het ontwerp worden goed gelezen (ook bogen als 'a2 2 0 01-2 2')
    bool icOk = true;
    foreach (string ic in new[]{"muis","pen","markeer","laser","vormen","tekst","stempel","focus","gum","undo","bord","foto","oog","oogopen","instellingen","selecteer","opslaan","prullenbak","map","vraag","dupliceer"}) {
      using (var bmp = new Bitmap(48, 48, PixelFormat.Format32bppPArgb)) using (var g = Graphics.FromImage(bmp)) {
        try { Icons.Draw(g, ic, new RectangleF(0,0,48,48), 48, Color.White, 1.8f); } catch (Exception ex) { icOk = false; Console.WriteLine(ic + ": " + ex.Message); }
      }
    }
    Check(icOk, "Alle icoontjes tekenen zonder fout");
    using (var gp = Svg.Path("M4 9h10a6 6 0 010 12h-3")) { RectangleF bb = gp.GetBounds(); Check(Math.Abs(bb.Right - 20) < 0.3 && Math.Abs(bb.Bottom - 21) < 0.3, "Boog in het ongedaan-icoon heeft de goede maat (" + bb + ")"); }
    using (var gp = Svg.Path("M19.4 15a1.65 1.65 0 00.33 1.82l.06.06")) { RectangleF bb = gp.GetBounds(); Check(bb.Right < 19.9 && bb.Right > 19.6, "Aan elkaar geschreven boogvlaggen ('00.33') kloppen"); }

    // 10. Het logo: tegel in de accentkleur, N wit (of donker op een heel lichte kleur)
    using (var lb = Logo.Bitmap(256, Color.FromArgb(255, 0x38, 0xBD, 0xF8), Color.White)) {
      Color corner = lb.GetPixel(30, 128), stem = lb.GetPixel(176, 100), wood = lb.GetPixel(70, 170), tipC = lb.GetPixel(78, 196);
      Check(corner.B > 240 && corner.R < 70, "Logotegel krijgt de accentkleur van het thema");
      Check(stem.R > 240 && stem.G > 240 && stem.B > 240, "N is wit");
      Check(wood.R > 240 && wood.G > 240 && wood.B > 240, "Potlood is wit (alleen de grafietpunt heeft kleur)");
      Check(tipC.R < 90 && tipC.G < 70, "Grafietpunt is donker");
      lb.Save(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "np_logo_test.png"), ImageFormat.Png);
    }
    Check(Logo.LetterColor(Color.FromArgb(255, 0xFF, 0xE6, 0x00), Color.Black).ToArgb() == Color.Black.ToArgb(), "Op geel wordt de N donker");
    Check(Logo.LetterColor(Color.FromArgb(255, 0xFF, 0x80, 0x00), Color.Black).ToArgb() == Color.White.ToArgb(), "Op oranje blijft de N wit");
    using (var sm = Logo.Bitmap(16, Color.FromArgb(255, 0xFF, 0x80, 0x00), Color.White)) Check(sm.GetPixel(8, 1).R > 200, "Klein logo (16 px) tekent");

    // 7. Vertalingen
    bool rowsOk = true; foreach (string[] r in LangData.Rows) if (r.Length != 4 || string.Join("", r).IndexOf('—') >= 0) rowsOk = false;
    Check(rowsOk && LangData.Rows.Length > 250, "Elke vertaling heeft NL, EN, FR en ES, zonder lang streepje (" + LangData.Rows.Length + ")");
    L.Set("en"); string en = L.T("Opslaan"); L.Set("fr"); string fr = L.T("Opslaan"); L.Set("es"); string es = L.T("Opslaan");
    string unk = L.T("Dit bestaat niet"); L.Set("nl");
    Check(en == "Save" && fr == "Enregistrer" && es == "Guardar" && L.T("Opslaan") == "Opslaan", "Opslaan in vier talen");
    Check(unk == "Dit bestaat niet", "Onbekende tekst blijft Nederlands, geen crash");
    L.Set("en"); Check(L.F("Pagina {0} van {1}", 2, 3) == "Page 2 of 3", "Tekst met getallen vertaalt goed"); L.Set("nl");
    L.Set("xx"); Check(L.Code == "nl", "Onbekende taal valt terug op Nederlands"); L.Set("nl");

    // 8. Sneltoetsen: standaard Ctrl+Shift, eigen keuze en botsingen
    var sc = new Settings(); Shortcuts.Cfg = sc;
    Check(Shortcuts.Combo("pen") == "Ctrl+Shift+P" && Shortcuts.Combo("redo") == "Ctrl+Y" && Shortcuts.Combo("undo") == "Ctrl+Z", "Pen is Ctrl+Shift+P, opnieuw is Ctrl+Y");
    sc.ShortcutMode = 1; Check(Shortcuts.Combo("pen") == "P", "Stand alleen een letter");
    sc.ShortcutMode = 2; Check(Shortcuts.Combo("pen") == "" && Shortcuts.Combo("undo") == "Ctrl+Z", "Stand uit laat Ctrl+Z werken");
    sc.ShortcutMode = 0;
    Check(Shortcuts.Conflict("pen", "Ctrl+Shift+M") != null, "Botsing met markeerstift wordt gemeld");
    Check(Shortcuts.Conflict("pen", "Ctrl+Shift+D") != null, "Botsing met tekenen aan of uit wordt gemeld");
    Shortcuts.SetOverride("pen", "Ctrl+Alt+P"); Check(Shortcuts.Combo("pen") == "Ctrl+Alt+P", "Eigen sneltoets wordt gebruikt");
    Shortcuts.SetOverride("pen", ""); Check(Shortcuts.Combo("pen") == "", "Sneltoets kan leeg");
    var ke = new System.Windows.Forms.KeyEventArgs(System.Windows.Forms.Keys.M | System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.Shift);
    Check(Shortcuts.ActionFor(ke) == "markeer", "Ctrl+Shift+M kiest de markeerstift");
    var plain = new System.Windows.Forms.KeyEventArgs(System.Windows.Forms.Keys.M);
    Check(Shortcuts.ActionFor(plain) == null, "Gewoon M typen doet niets in de standaardstand");
    Check(Shortcuts.FromKeys(System.Windows.Forms.Keys.Y | System.Windows.Forms.Keys.Control) == "Ctrl+Y", "Toetsen worden goed opgeschreven");

    // 9. Werkbalk: volgorde, verbergen en terugzetten
    var bc = new Settings();
    var lay = Toolbar.Layout(bc);
    Check(lay.Count > 15 && lay[0] == "muis" && lay.Contains("|") && lay[lay.Count - 1] == "weg", "Standaardbalk begint met muis en eindigt met wegschuiven");
    lay.Remove("laser"); lay.Insert(0, "redo"); bc.BarLayout = Toolbar.LayoutString(lay);
    var lay2 = Toolbar.Layout(bc);
    Check(lay2[0] == "redo" && !lay2.Contains("laser"), "Eigen volgorde en verborgen knop blijven bewaard");
    bc.BarLayout = "pen,bestaatniet,,|,|,gum"; var lay3 = Toolbar.Layout(bc);
    Check(!lay3.Contains("bestaatniet") && lay3.Contains("pen") && lay3.Contains("gum"), "Rare werkbalk-instelling geeft geen crash");
    Check(Toolbar.NameOf("foto") == "Schermafbeelding" && Toolbar.IconOf("foto") != null, "Elke knop heeft een naam en icoontje");
    bool allIcons = true; foreach (string k in Toolbar.Catalog) if (k != "|" && k != "kleur" && !Icons.Has(Toolbar.IconOf(k))) allIcons = false;
    Check(allIcons, "Alle knoppen in de catalogus hebben een Lucide-icoontje");

    // 10. Nieuwe instellingen blijven bewaard
    var ns = new Settings(); ns.Language = "fr"; ns.KeepDrawing = 2; ns.ShortcutMode = 1; ns.ShortcutOverrides = "pen=Ctrl+Alt+P"; ns.StampKind = 4; ns.StampNext = 7;
    ns.BoardBg = 2; ns.OutlineWidth = 2; ns.OutlineColor = 1; ns.TextSize = 48; ns.Chalk = true; ns.Fade = true; ns.BarLayout = "pen,gum"; ns.UsageCats = 5; ns.ShowInTaskbar = false;
    string nsPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "np_test2.ini"); ns.Save(nsPath); var nb = Settings.Load(nsPath);
    Check(nb.Language == "fr" && nb.KeepDrawing == 2 && nb.ShortcutMode == 1 && nb.ShortcutOverrides == "pen=Ctrl+Alt+P" && nb.StampKind == 4 && nb.StampNext == 7 && nb.BoardBg == 2
      && nb.OutlineWidth == 2 && nb.OutlineColor == 1 && Math.Abs(nb.TextSize - 48) < 0.01 && nb.Chalk && nb.Fade && nb.BarLayout == "pen,gum" && nb.UsageCats == 5 && !nb.ShowInTaskbar, "Taal, sneltoetsen, stempels, bord en tekst blijven bewaard");
    var dflt = new Settings(); Check(dflt.FontName == "Andika" && dflt.TextOutline && Math.Abs(dflt.TextSize - 36) < 0.01 && dflt.KeepDrawing == 1 && dflt.ShortcutMode == 0, "Standaard: Andika, rand aan, grootte 36, bewaren na Opslaan, Ctrl+Shift");

    // 11. Stempels
    bool stOk = true;
    using (var sb = new Bitmap(200, 200, PixelFormat.Format32bppArgb)) using (var sg = Graphics.FromImage(sb)) {
      foreach (string k in Render.StampKinds) { var s = new Stroke{Tool=Tool.Stamp, FontSize=40, Text = k == "nummer" ? "#12" : k, Color = Render.StampColor(k)}; s.Pts.Add(new PointF(100,100));
        try { Render.Draw(sg, s, 1); } catch { stOk = false; } }
    }
    Check(stOk && Render.StampKinds.Length == 6 && Render.StampOrder.Length == 6, "Alle zes stempels tekenen (ook zo-zo en nummer 12)");
    // 12. Versies vergelijken
    Check(Updates.IsNewer("1.2.1", "1.2.0") && Updates.IsNewer("v1.3", "1.2.0") && !Updates.IsNewer("1.2.0", "1.2.0") && !Updates.IsNewer("1.1.9", "1.2.0") && !Updates.IsNewer("rommel", "1.2.0"), "Nieuwe versie wordt goed herkend");
    // 13. Punten uit de code-review
    Check(Settings.ParseHotkey("Ctrl+Shift+5", out mods, out vk) && vk == 0x35, "Ctrl+Shift+5 is echt de toets 5");
    var hs = new Settings(); hs.HotkeyDraw = "Ctrl+Shift+P"; Shortcuts.Cfg = hs;
    Check(Shortcuts.Conflict("lijn", "ctrl + shift + p") != null, "Botsing met je eigen wereldwijde sneltoets wordt gemeld");
    Check(Shortcuts.ConflictGlobal("Ctrl+Shift+M") != null && Shortcuts.ConflictGlobal("Ctrl+Alt+F9") == null, "Wereldwijde sneltoets botst met markeerstift");
    var longPen = new Stroke{Tool=Tool.Pen, Width=4}; for (int i = 0; i < 2000; i++) longPen.Pts.Add(new PointF(i, (i % 50)));
    var sw = System.Diagnostics.Stopwatch.StartNew(); int hits = 0; for (int i = 0; i < 2000; i++) if (Render.Hit(longPen, new PointF(5000, 5000 + i), 8)) hits++;
    Check(hits == 0 && sw.ElapsedMilliseconds < 200, "Gum is snel bij lange lijnen ver weg (" + sw.ElapsedMilliseconds + " ms)");
    Check(Render.Hit(longPen, new PointF(1000, 0), 8) && !Render.Hit(longPen, new PointF(1000, 80), 8), "Gum raakt lange penlijn nog wel goed");
    var bad = new Settings(); bad.Width = float.NaN; bad.TextSize = float.PositiveInfinity; bad.Tool = (Tool)99; bad.Clamp();
    Check(!float.IsNaN(bad.Width) && Math.Abs(bad.TextSize - 36) < 0.01 && bad.Tool == Tool.Pen, "Kapotte getallen in instellingen worden hersteld");
    Shortcuts.Cfg = new Settings();

    // 14. Nieuw in 1.2.1: natte inkt (alleen het nieuwste stukje tekenen) geeft precies dezelfde lijn als alles in een keer
    foreach (Tool wt in new[]{Tool.Pen, Tool.Marker, Tool.Laser}) {
      var rnd = new Random(7);
      var ws = new Stroke{Tool=wt, Color=Color.FromArgb(255, 41, 98, 255), Width=wt == Tool.Marker ? 6 : 5};
      PointF cur = new PointF(200, 200); ws.Pts.Add(cur);
      using (var wetBmp = new Bitmap(500, 420, PixelFormat.Format32bppPArgb))
      using (var dryBmp = new Bitmap(500, 420, PixelFormat.Format32bppPArgb)) {
        using (var g = Graphics.FromImage(wetBmp)) {
          Render.Setup(g);
          for (int i = 0; i < 400; i++) {
            int n = ws.Pts.Count;
            RectangleF before = n >= 2 ? Render.SegmentBox(ws.Pts, n - 2) : new RectangleF(ws.Pts[0], SizeF.Empty);
            cur = new PointF(Math.Max(20, Math.Min(480, cur.X + rnd.Next(-14, 15))), Math.Max(20, Math.Min(400, cur.Y + rnd.Next(-14, 15))));
            ws.Pts.Add(cur);
            RectangleF a = RectangleF.Union(before, Render.SegmentBox(ws.Pts, n - 1));
            if (n >= 2) a = RectangleF.Union(a, Render.SegmentBox(ws.Pts, n - 2));
            float pad = (wt == Tool.Marker ? Render.MarkerWidth(ws.Width) : wt == Tool.Laser ? Render.LaserWidest(ws.Width) : ws.Width) / 2 + 3;
            a.Inflate(pad, pad);
            Rectangle ai = Rectangle.FromLTRB((int)Math.Floor(a.Left), (int)Math.Floor(a.Top), (int)Math.Ceiling(a.Right), (int)Math.Ceiling(a.Bottom));
            ai.Intersect(new Rectangle(0, 0, 500, 420));
            g.SetClip(ai);
            g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
            g.FillRectangle(Brushes.Transparent, ai);
            g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceOver;
            Render.DrawWet(g, ws, ai);
            g.ResetClip();
          }
        }
        using (var g = Graphics.FromImage(dryBmp)) { Render.Setup(g); Render.Draw(g, ws, 1f); }
        int diff = 0, big = 0, inked = 0;
        for (int y = 0; y < 420; y += 1) for (int x = 0; x < 500; x += 1) {
          Color w1 = wetBmp.GetPixel(x, y), w2 = dryBmp.GetPixel(x, y);
          int d = Math.Max(Math.Abs(w1.A - w2.A), Math.Max(Math.Abs(w1.R - w2.R), Math.Max(Math.Abs(w1.G - w2.G), Math.Abs(w1.B - w2.B))));
          if (w2.A > 0) inked++; if (d > 2) diff++; if (d > 140) big++;
        }
        // Tijdens het schrijven mag een randpixel een fractie anders zijn (mono tekent iets anders dan Windows).
        // Bij loslaten wordt de lijn een keer helemaal getekend ('droog'), dan is hij precies gelijk.
        Check(big == 0 && diff * 200 < inked, "Natte inkt is gelijk aan de hele lijn (" + wt + ", " + diff + " van " + inked + " pixels een fractie anders)");
      }
    }
    var bs = new Stroke{Tool=Tool.Text, Text="Hallo", FontSize=30}; bs.Pts.Add(new PointF(10, 10));
    RectangleF bb1 = Render.Bounds(bs); Render.Move(bs, 50, 0); RectangleF bb2 = Render.Bounds(bs);
    Check(Math.Abs(bb2.X - bb1.X - 50) < 0.01, "Onthouden omtrek gaat mee als je iets verplaatst");
    bs.Text = "Hallo allemaal, dit is langer"; Check(Render.Bounds(bs).Width > bb2.Width + 20, "Onthouden omtrek gaat mee als de tekst verandert");
    using (var p0 = new System.Drawing.Drawing2D.GraphicsPath()) {
      p0.AddLine(3.1f, 5.2f, 20.7f, 5.2f);
      using (var sp = Icons.Snapped(p0, 1f, 0, 0, 2)) { PointF[] q = sp.PathPoints; Check(q[0].X == 3 && q[0].Y == 5 && q[1].X == 21, "Icoontjes staan op hele pixels (lijn van 2 pixels)"); }
      using (var sp = Icons.Snapped(p0, 1f, 0, 0, 1)) { PointF[] q = sp.PathPoints; Check(q[0].X == 3.5f && q[0].Y == 5.5f, "Icoontjes staan op hele pixels (lijn van 1 pixel)"); }
    }
    Check(Icons.DeviceStroke(2, 20 / 24f) == 2 && Icons.DeviceStroke(2, 30 / 24f) == 3 && Icons.DeviceStroke(2, 16 / 24f) == 1, "Lijndikte van icoontjes in hele pixels");

    // 15d. Bijwerken: het antwoord van GitHub goed lezen (installer en controlecode), en nooit een vreemde link
    {
      string gh = "{\"html_url\":\"https://github.com/ridaabouhayan-hue/nexuspen/releases/tag/v1.3.1\",\"tag_name\":\"v1.3.1\",\"name\":\"NexusPen 1.3.1\",\"assets\":[{\"url\":\"https://api.github.com/repos/ridaabouhayan-hue/nexuspen/releases/assets/719390116\",\"id\":719390116,\"name\":\"NexusPen-Setup.exe\",\"label\":\"\",\"uploader\":{\"login\":\"github-actions[bot]\",\"following_url\":\"https://api.github.com/users/github-actions%5Bbot%5D/following{/other_user}\"},\"content_type\":\"application/x-msdos-program\",\"state\":\"uploaded\",\"size\":2522825,\"digest\":\"sha256:3d6c2d48a90f964b7efce6742415f081c3038b93cf701687780db16003ef81f2\",\"browser_download_url\":\"https://github.com/ridaabouhayan-hue/nexuspen/releases/download/v1.3.1/NexusPen-Setup.exe\"},{\"url\":\"https://api.github.com/repos/ridaabouhayan-hue/nexuspen/releases/assets/719390115\",\"id\":719390115,\"name\":\"NexusPen.exe\",\"label\":\"\",\"uploader\":{\"login\":\"github-actions[bot]\",\"following_url\":\"https://api.github.com/users/github-actions%5Bbot%5D/following{/other_user}\"},\"content_type\":\"application/x-msdos-program\",\"state\":\"uploaded\",\"size\":1242624,\"digest\":\"sha256:08c4ab86c90a0b06e135cc14feac1ff20659983c054da8d7d4f1aa4ca7f0b124\",\"browser_download_url\":\"https://github.com/ridaabouhayan-hue/nexuspen/releases/download/v1.3.1/NexusPen.exe\"}]}";
      var ui = Updates.Parse(gh);
      Check(ui != null && ui.Version == "1.3.1" && ui.Sha256 != null && ui.Sha256.Length == 64 && ui.SetupUrl != null && ui.SetupUrl.EndsWith("/v1.3.1/NexusPen-Setup.exe"), "Bijwerken: installer en controlecode uit het GitHub-antwoord");
      var ubad = Updates.Parse(gh.Replace("https://github.com/ridaabouhayan-hue/nexuspen/releases/download/", "https://example.com/x/"));
      Check(ubad != null && ubad.SetupUrl == null, "Bijwerken: een download van een andere plek wordt nooit gebruikt");
      Check(Updates.IsNewer("1.3.2", "1.3.1") && !Updates.IsNewer("1.3.1", "1.3.1") && !Updates.IsNewer("1.2.9", "1.3.0"), "Versienummers vergelijken");
    }

    // 15c. Instellingen exporteren en importeren: alleen echte NexusPen-bestanden
    {
      string ex = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "np-export-test.nexuspen");
      string junk = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "np-export-junk.ini");
      System.IO.File.WriteAllText(junk, "[.ShellClassInfo]\nIconResource=x\n");
      bool saved = new Settings().TrySave(ex);
      Check(saved && Settings.LooksLikeSettings(ex) && !Settings.LooksLikeSettings(junk), "Importeren accepteert alleen echte NexusPen-instellingen");
      try { System.IO.File.Delete(ex); System.IO.File.Delete(junk); } catch { }
    }

    // 15b. Woorden slepen in een tekst (zoals in Word)
    {
      var ms = new Stroke{Tool=Tool.Text, Text="ik ga snel naar huis", FontSize=30}; ms.Pts.Add(new PointF(0, 0));
      var me = new TextEditor(ms, 0);
      me.SelectWordAt(7);                                    // "snel"
      bool moved = me.MoveSelectionTo(ms.Text.Length, false);
      Check(moved && ms.Text == "ik ga  naar huissnel" && me.Selected == "snel", "Geselecteerde woorden naar het einde slepen (" + ms.Text + ")");
      me.Undo();
      Check(ms.Text == "ik ga snel naar huis", "Woorden slepen is ongedaan te maken");
      me.SelectWordAt(7);
      Check(!me.MoveSelectionTo(8, false) && ms.Text == "ik ga snel naar huis", "Loslaten in de selectie zelf doet niets");
      me.MoveSelectionTo(0, true);
      Check(ms.Text == "snelik ga snel naar huis", "Met Ctrl slepen maakt een kopie");
    }

    // 15. Teksteditor: pijltjes, selectie, woorden, ongedaan maken
    {
      var ts = new Stroke{Tool=Tool.Text, Text="", FontSize=30, FontName="Andika"}; ts.Pts.Add(new PointF(100, 100));
      var ed = new TextEditor(ts, 0);
      foreach (char ch in "Ik ga met de trein") ed.Insert(ch.ToString());
      ed.Left(true, false); ed.Left(true, false);            // voor "de"
      ed.Insert("niet ");
      Check(ts.Text == "Ik ga met niet de trein", "Tekst invoegen midden in de zin met Ctrl+pijltje (" + ts.Text + ")");
      ed.End(false, false); ed.Left(true, true);              // "trein" selecteren
      Check(ed.Selected == "trein", "Shift+Ctrl+pijltje selecteert een woord");
      ed.Insert("bus");
      Check(ts.Text == "Ik ga met niet de bus", "Typen vervangt de selectie");
      ed.Backspace(true);
      Check(ts.Text == "Ik ga met niet de ", "Ctrl+Backspace wist een woord");
      ed.Undo();
      Check(ts.Text == "Ik ga met niet de bus", "Ongedaan maken binnen de tekst");
      ed.Home(false, false); ed.Insert("\n"); ed.Home(true, false); ed.Insert("Regel 1");
      using (var tlay = new TextLayout(ts)) {
        Check(tlay.Lines.Length == 2, "Twee regels na Enter");
        ed.End(true, false); ed.Vertical(-1, false, tlay);
        int tline, tcol; tlay.ToLineCol(ed.Caret, out tline, out tcol);
        Check(tline == 0, "Pijltje omhoog gaat naar de regel erboven");
        int tidx = tlay.ToIndex(1, 3); RectangleF tcr = tlay.CaretRect(tidx, 2);
        Check(tlay.Hit(new PointF(tcr.X + 1, tcr.Y + tcr.Height / 2)) == tidx, "Klikken met de muis zet het streepje op de goede letter");
      }
      ed.SelectWordAt(2);
      Check(ed.Selected == "Regel", "Dubbelklik selecteert het woord");
    }

    Console.WriteLine(fails == 0 ? "\nALLE TESTS GESLAAGD" : "\n" + fails + " TEST(S) MISLUKT");
    return fails;
  }
}}
