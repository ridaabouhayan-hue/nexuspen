using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace NexusPen
{
    // ------------------------------------------------------------------ Sneltoetsen tijdens het tekenen
    // Standaard Ctrl+Shift + letter, zodat je nooit per ongeluk een gereedschap kiest als je typt.
    // Ze werken alleen als de tekenlaag actief is (dus niet in een ander programma of in een tekstvak).
    class Shortcut
    {
        public string Id, Label, Icon;
        public Keys Letter;     // de letter, gebruikt met Ctrl+Shift of los
        public string Fixed;    // vaste combinatie, onafhankelijk van de stand (zoals Ctrl+Z)
        public Shortcut(string id, string label, string icon, Keys letter, string fix) { Id = id; Label = label; Icon = icon; Letter = letter; Fixed = fix; }
    }

    static class Shortcuts
    {
        public static readonly Shortcut[] All = {
            new Shortcut("pen", "Pen", "pen", Keys.P, null),
            new Shortcut("markeer", "Markeerstift", "markeer", Keys.M, null),
            new Shortcut("laser", "Laserpen", "laser", Keys.L, null),
            new Shortcut("tekst", "Tekst", "tekst", Keys.T, null),
            new Shortcut("gum", "Gum", "gum", Keys.E, null),
            new Shortcut("selecteer", "Selecteren", "selecteer", Keys.V, null),
            new Shortcut("lijn", "Lijn", "lijn", Keys.N, null),
            new Shortcut("pijl", "Pijl", "pijl", Keys.A, null),
            new Shortcut("rechthoek", "Rechthoek", "rechthoek", Keys.R, null),
            new Shortcut("cirkel", "Cirkel", "cirkel", Keys.O, null),
            new Shortcut("stempel", "Stempel", "stempel", Keys.S, null),
            new Shortcut("focus", "Spotlicht", "focus", Keys.F, null),
            new Shortcut("loep", "Vergrootglas", "zoom", Keys.G, null),
            new Shortcut("bord", "Bord", "bord", Keys.B, null),
            new Shortcut("foto", "Schermafbeelding", "foto", Keys.C, null),
            new Shortcut("oog", "Tekeningen verbergen", "oog", Keys.H, null),
            new Shortcut("undo", "Ongedaan maken", "undo", Keys.None, "Ctrl+Z"),
            new Shortcut("redo", "Opnieuw", "redo", Keys.None, "Ctrl+Y"),
            new Shortcut("opslaan", "Opslaan", "opslaan", Keys.None, "Ctrl+S"),
            new Shortcut("kopie", "Kopie van selectie", "dupliceer", Keys.None, "Ctrl+D"),
        };

        public static Settings Cfg;

        public static Shortcut Find(string id)
        {
            foreach (Shortcut s in All) if (s.Id == id) return s;
            return null;
        }

        static Dictionary<string, string> Overrides()
        {
            Dictionary<string, string> d = new Dictionary<string, string>();
            if (Cfg == null || string.IsNullOrEmpty(Cfg.ShortcutOverrides)) return d;
            foreach (string part in Cfg.ShortcutOverrides.Split(';'))
            {
                int i = part.IndexOf('=');
                if (i > 0) d[part.Substring(0, i)] = part.Substring(i + 1);
            }
            return d;
        }

        // combo null = terug naar standaard, "" = geen sneltoets
        public static void SetOverride(string id, string combo)
        {
            Dictionary<string, string> d = Overrides();
            if (combo == null) d.Remove(id); else d[id] = combo;
            List<string> parts = new List<string>();
            foreach (KeyValuePair<string, string> kv in d) parts.Add(kv.Key + "=" + kv.Value);
            Cfg.ShortcutOverrides = string.Join(";", parts.ToArray());
        }

        // De combinatie als tekst, zoals "Ctrl+Shift+P". Leeg = geen sneltoets.
        public static string Combo(string id)
        {
            Shortcut s = Find(id);
            if (s == null) return "";
            string o;
            if (Overrides().TryGetValue(id, out o)) return o;
            if (s.Fixed != null) return s.Fixed;
            int mode = Cfg == null ? 0 : Cfg.ShortcutMode;
            if (mode == 2 || s.Letter == Keys.None) return "";
            if (id == "oog" && mode == 0) return "";   // Ctrl+Shift+H is al "alles in- of uitklappen"
            return (mode == 0 ? "Ctrl+Shift+" : "") + s.Letter;
        }

        public static string ColorCombo(int index)
        {
            int mode = Cfg == null ? 0 : Cfg.ShortcutMode;
            if (mode == 2 || index < 0 || index > 9) return "";   // alleen de eerste tien kleuren hebben een toets
            string d = ((index + 1) % 10).ToString();
            return mode == 0 ? "Ctrl+Shift+" + d : d;
        }

        // Voor tooltips: "Pen (Ctrl+Shift+P)"
        public static string Tip(string text, string id)
        {
            string c = Combo(id);
            return c.Length == 0 ? text : text + "  (" + c + ")";
        }

        public static string FromKeys(Keys k)
        {
            Keys code = k & Keys.KeyCode;
            if (code == Keys.ControlKey || code == Keys.ShiftKey || code == Keys.Menu || code == Keys.None) return "";
            string s = "";
            if ((k & Keys.Control) != 0) s += "Ctrl+";
            if ((k & Keys.Shift) != 0) s += "Shift+";
            if ((k & Keys.Alt) != 0) s += "Alt+";
            string name = code.ToString();
            if (code >= Keys.D0 && code <= Keys.D9) name = ((int)(code - Keys.D0)).ToString();
            return s + name;
        }

        public static bool Matches(string combo, KeyEventArgs e)
        {
            if (string.IsNullOrEmpty(combo)) return false;
            return FromKeys(e.KeyData).Equals(combo, StringComparison.OrdinalIgnoreCase);
        }

        // Welke actie hoort bij deze toets? null = geen.
        public static string ActionFor(KeyEventArgs e)
        {
            foreach (Shortcut s in All) if (Matches(Combo(s.Id), e)) return s.Id;
            return null;
        }

        public static int ColorFor(KeyEventArgs e)
        {
            Keys code = e.KeyCode;
            int n = -1;
            if (code >= Keys.D1 && code <= Keys.D9) n = code - Keys.D1;
            else if (code >= Keys.NumPad1 && code <= Keys.NumPad9) n = code - Keys.NumPad1;
            else if (code == Keys.D0 || code == Keys.NumPad0) n = 9;
            if (n < 0) return -1;
            int mode = Cfg == null ? 0 : Cfg.ShortcutMode;
            if (mode == 2) return -1;
            bool cs = e.Control && e.Shift && !e.Alt;
            bool plain = !e.Control && !e.Shift && !e.Alt;
            return (mode == 0 && cs) || (mode == 1 && plain) ? n : -1;
        }

        // Combinaties die Windows zelf gebruikt of die we al hebben
        static readonly string[] Reserved = { "Ctrl+Shift+Escape", "Ctrl+Alt+Delete", "Alt+F4", "Alt+Tab", "Ctrl+Escape", "Escape", "Delete" };

        // "ctrl + shift+d" en "Ctrl+Shift+D" zijn hetzelfde
        static bool Same(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            int ma, va, mb, vb;
            if (Settings.ParseHotkey(a, out ma, out va) && Settings.ParseHotkey(b, out mb, out vb)) return ma == mb && va == vb;
            return a.Replace(" ", "").Equals(b.Replace(" ", ""), StringComparison.OrdinalIgnoreCase);
        }

        // Botst een wereldwijde sneltoets met een sneltoets tijdens het tekenen?
        public static string ConflictGlobal(string combo)
        {
            foreach (Shortcut s in All)
                if (Same(Combo(s.Id), combo)) return L.F("Deze combinatie hoort al bij {0}.", L.T(s.Label));
            return null;
        }

        public static string Conflict(string id, string combo)
        {
            if (string.IsNullOrEmpty(combo)) return null;
            foreach (string r in Reserved)
                if (r.Equals(combo, StringComparison.OrdinalIgnoreCase)) return L.T("Deze combinatie gebruikt Windows of NexusPen al.");
            string draw = Cfg != null ? Cfg.HotkeyDraw : "Ctrl+Shift+D", hide = Cfg != null ? Cfg.HotkeyHide : "Ctrl+Shift+H";
            if (Same(draw, combo) || Same(hide, combo)) return L.T("Deze combinatie gebruikt Windows of NexusPen al.");
            foreach (Shortcut s in All)
                if (s.Id != id && Combo(s.Id).Equals(combo, StringComparison.OrdinalIgnoreCase))
                    return L.F("Deze combinatie hoort al bij {0}.", L.T(s.Label));
            // de kleurtoetsen (1 tot 0) en Ctrl+Shift+Z (ook Opnieuw) tellen ook mee
            for (int i = 0; i < 10; i++)
                if (Same(ColorCombo(i), combo)) return L.T("Deze combinatie kiest al een kleur.");
            if (id != "redo" && Same("Ctrl+Shift+Z", combo) && Combo("redo").Length > 0) return L.F("Deze combinatie hoort al bij {0}.", L.T("Opnieuw"));
            return null;
        }
    }
}
