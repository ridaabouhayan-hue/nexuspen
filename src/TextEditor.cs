using System;
using System.Collections.Generic;

namespace NexusPen
{
    // ------------------------------------------------------------------ Tekst bewerken zoals in een gewone tekstverwerker
    // Een knipperend streepje (Caret) dat je met de pijltjes en de muis verplaatst, en een selectie van Anchor tot Caret.
    // Typen vervangt de selectie. Eigen Ongedaan maken binnen de tekst. Alleen de logica: tekenen gebeurt in Overlay.
    class TextEditor
    {
        public readonly Stroke S;
        public int Caret, Anchor;
        float wantX = -1;   // bij omhoog en omlaag: onthoud waar je in de regel zat

        readonly List<string> undoText = new List<string>(), redoText = new List<string>();
        readonly List<int> undoCaret = new List<int>(), redoCaret = new List<int>();
        string lastKind;
        DateTime lastAt = DateTime.MinValue;

        public TextEditor(Stroke s, int caret)
        {
            S = s;
            if (S.Text == null) S.Text = "";
            Caret = Anchor = Math.Max(0, Math.Min(Text.Length, caret));
        }

        public string Text { get { return S.Text ?? ""; } }
        public bool HasSelection { get { return Caret != Anchor; } }
        public int SelStart { get { return Math.Min(Caret, Anchor); } }
        public int SelEnd { get { return Math.Max(Caret, Anchor); } }
        public string Selected { get { return HasSelection ? Text.Substring(SelStart, SelEnd - SelStart) : ""; } }

        // ---- ongedaan maken binnen de tekst (typen achter elkaar telt als een stap)
        void Snapshot(string kind)
        {
            DateTime now = DateTime.Now;
            bool merge = kind == lastKind && (kind == "typen" || kind == "wissen") && (now - lastAt).TotalSeconds < 1.5;
            lastKind = kind; lastAt = now;
            if (merge) return;
            undoText.Add(Text); undoCaret.Add(Caret);
            if (undoText.Count > 200) { undoText.RemoveAt(0); undoCaret.RemoveAt(0); }
            redoText.Clear(); redoCaret.Clear();
        }

        public bool Undo()
        {
            if (undoText.Count == 0) return false;
            redoText.Add(Text); redoCaret.Add(Caret);
            S.Text = undoText[undoText.Count - 1]; Caret = Anchor = Math.Min(S.Text.Length, undoCaret[undoCaret.Count - 1]);
            undoText.RemoveAt(undoText.Count - 1); undoCaret.RemoveAt(undoCaret.Count - 1);
            lastKind = null; wantX = -1;
            return true;
        }

        public bool Redo()
        {
            if (redoText.Count == 0) return false;
            undoText.Add(Text); undoCaret.Add(Caret);
            S.Text = redoText[redoText.Count - 1]; Caret = Anchor = Math.Min(S.Text.Length, redoCaret[redoCaret.Count - 1]);
            redoText.RemoveAt(redoText.Count - 1); redoCaret.RemoveAt(redoCaret.Count - 1);
            lastKind = null; wantX = -1;
            return true;
        }

        // ---- veranderen
        public void Insert(string t)
        {
            if (t == null) return;
            if (t == " ") lastKind = null;   // na een spatie een nieuwe stap: Ctrl+Z haalt dan een woord weg, niet de hele zin
            Snapshot(t.Length == 1 && t != "\n" ? "typen" : "plakken");
            string s = Text;
            int a = SelStart, b = SelEnd;
            S.Text = s.Substring(0, a) + t + s.Substring(b);
            Caret = Anchor = a + t.Length;
            wantX = -1;
        }

        public void Backspace(bool word)
        {
            if (HasSelection) { DeleteSelection(); return; }
            if (Caret == 0) return;
            int from = word ? WordLeft(Caret) : Caret - 1;
            Snapshot("wissen");
            S.Text = Text.Substring(0, from) + Text.Substring(Caret);
            Caret = Anchor = from; wantX = -1;
        }

        public void Delete(bool word)
        {
            if (HasSelection) { DeleteSelection(); return; }
            if (Caret >= Text.Length) return;
            int to = word ? WordRight(Caret) : Caret + 1;
            Snapshot("wissen");
            S.Text = Text.Substring(0, Caret) + Text.Substring(to);
            Anchor = Caret; wantX = -1;
        }

        public void DeleteSelection()
        {
            if (!HasSelection) return;
            Snapshot("knippen");
            int a = SelStart;
            S.Text = Text.Substring(0, a) + Text.Substring(SelEnd);
            Caret = Anchor = a; wantX = -1;
        }

        // ---- bewegen (met Shift: selecteren)
        void Set(int pos, bool shift)
        {
            Caret = Math.Max(0, Math.Min(Text.Length, pos));
            if (!shift) Anchor = Caret;
        }

        public void Left(bool word, bool shift)
        {
            if (!shift && HasSelection) { Set(SelStart, false); wantX = -1; return; }
            Set(word ? WordLeft(Caret) : Caret - 1, shift); wantX = -1;
        }

        public void Right(bool word, bool shift)
        {
            if (!shift && HasSelection) { Set(SelEnd, false); wantX = -1; return; }
            Set(word ? WordRight(Caret) : Caret + 1, shift); wantX = -1;
        }

        public void Home(bool all, bool shift)
        {
            if (all) { Set(0, shift); wantX = -1; return; }
            int i = Caret;
            while (i > 0 && Text[i - 1] != '\n') i--;
            Set(i, shift); wantX = -1;
        }

        public void End(bool all, bool shift)
        {
            if (all) { Set(Text.Length, shift); wantX = -1; return; }
            int i = Caret;
            while (i < Text.Length && Text[i] != '\n') i++;
            Set(i, shift); wantX = -1;
        }

        // Omhoog of omlaag: naar de regel erboven of eronder, op dezelfde hoogte in de zin
        public void Vertical(int dir, bool shift, TextLayout lay)
        {
            int line, col; lay.ToLineCol(Caret, out line, out col);
            if (wantX < 0) wantX = lay.X(line, col);
            int nl = line + dir;
            if (nl < 0) { Set(0, shift); return; }
            if (nl >= lay.Lines.Length) { Set(Text.Length, shift); return; }
            float keep = wantX;
            Set(lay.ToIndex(nl, lay.ColAt(nl, keep)), shift);
            wantX = keep;
        }

        // De selectie naar een andere plek in de tekst slepen (met copy: een kopie daar neerzetten)
        public bool MoveSelectionTo(int target, bool copy)
        {
            if (!HasSelection) return false;
            int a = SelStart, b = SelEnd;
            target = Math.Max(0, Math.Min(Text.Length, target));
            if (!copy && target >= a && target <= b) return false;   // in de selectie zelf losgelaten: niets doen
            string sel = Text.Substring(a, b - a);
            Snapshot("slepen");
            string s = Text;
            if (!copy)
            {
                s = s.Substring(0, a) + s.Substring(b);
                if (target > b) target -= b - a;
            }
            S.Text = s.Substring(0, target) + sel + s.Substring(target);
            Anchor = target; Caret = target + sel.Length; wantX = -1;
            return true;
        }

        public void SelectAll() { Anchor = 0; Caret = Text.Length; wantX = -1; }

        public void SelectWordAt(int pos)
        {
            pos = Math.Max(0, Math.Min(Text.Length, pos));
            int a = pos, b = pos;
            while (a > 0 && IsWord(Text[a - 1])) a--;
            while (b < Text.Length && IsWord(Text[b])) b++;
            Anchor = a; Caret = b; wantX = -1;
        }

        public void PlaceAt(int pos, bool shift) { Set(pos, shift); wantX = -1; }

        static bool IsWord(char c) { return char.IsLetterOrDigit(c) || c == '\'' || c == '-'; }

        int WordLeft(int i)
        {
            while (i > 0 && !IsWord(Text[i - 1])) i--;
            while (i > 0 && IsWord(Text[i - 1])) i--;
            return i;
        }

        int WordRight(int i)
        {
            while (i < Text.Length && !IsWord(Text[i])) i++;
            while (i < Text.Length && IsWord(Text[i])) i++;
            return i;
        }
    }
}
