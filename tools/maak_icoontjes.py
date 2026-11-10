# Maakt src/IconData.cs uit Lucide (npm-pakket lucide-static). Draai opnieuw als er icoontjes bijkomen.
# Gebruik: python3 tools/maak_icoontjes.py <map met lucide-static/icons>
import os, re, sys
D = sys.argv[1] if len(sys.argv) > 1 else "lucide/package/icons"
MAP = {
 "muis": "mouse-pointer-2", "selecteer": "square-dashed-mouse-pointer", "pen": "pencil", "markeer": "highlighter",
 "laser": "circle-dot", "vormen": "shapes", "tekst": "type", "gum": "eraser", "stempel": "stamp", "focus": "flashlight",
 "undo": "undo-2", "redo": "redo-2", "bord": "presentation", "foto": "camera", "opslaan": "save",
 "oog": "eye-off", "oogopen": "eye", "instellingen": "settings", "op": "chevron-up", "neer": "chevron-down",
 "links": "chevron-left", "rechts": "chevron-right", "plus": "plus", "min": "minus", "lijn": "slash", "pijl": "move-up-right",
 "rechthoek": "rectangle-horizontal", "cirkel": "circle", "prullenbak": "trash-2", "dupliceer": "copy", "voren": "bring-to-front", "achter": "send-to-back", "geluid": "volume-2", "geluiduit": "volume-x", "scherm": "monitor", "naarscherm": "screen-share", "vol": "maximize", "volterug": "minimize", "aanwijzer": "mouse-pointer-2", "aanwijzeruit": "mouse-pointer-2-off",
 "map": "folder", "kruis": "x", "vink": "check", "taal": "languages", "toetsen": "keyboard", "huis": "house",
 "grafiek": "chart-no-axes-column", "info": "info", "schermdelen": "monitor-off", "kleuren": "palette", "voorlezen": "volume-2",
 "ster": "star", "vraag": "circle-question-mark", "hekje": "hash", "lijnen": "align-justify", "ruitjes": "grid-3x3",
 "schrijflijnen": "text", "leeg": "square", "scherm": "monitor", "whiteboard": "square", "krijtbord": "square",
 "meer": "chevrons-right", "rand": "type-outline", "groter": "maximize-2", "aftekenen": "log-out", "vergrootglas": "search",
 "afsluiten": "power", "verplaats": "move", "krijt": "brush", "zoom": "zoom-in",
 "puntjes": "ellipsis", "puntjesstaand": "ellipsis-vertical", "zandloper": "hourglass", "timer": "timer",
 "download": "download", "pauze": "pause", "afspelen": "play", "terugzetten": "rotate-ccw", "vastzetten": "pin", "losmaken": "pin-off",
}
def elements(name):
    p = os.path.join(D, name + ".svg")
    if not os.path.exists(p): raise SystemExit("Icoon ontbreekt in Lucide: " + name)
    s = open(p).read()
    body = s[s.index(">", s.index("<svg")) + 1: s.rindex("</svg>")]
    out = []
    for tag, attrs in re.findall(r"<(\w+)([^>]*)/?>", body):
        a = dict(re.findall(r'([\w-]+)="([^"]*)"', attrs))
        if tag == "path": out.append("p|" + a["d"])
        elif tag == "circle": out.append("c|%s %s %s" % (a["cx"], a["cy"], a["r"]))
        elif tag == "ellipse": out.append("e|%s %s %s %s" % (a["cx"], a["cy"], a["rx"], a["ry"]))
        elif tag == "rect": out.append("r|%s %s %s %s %s" % (a.get("x", "0"), a.get("y", "0"), a["width"], a["height"], a.get("rx", "0")))
        elif tag == "line": out.append("l|%s %s %s %s" % (a["x1"], a["y1"], a["x2"], a["y2"]))
        elif tag in ("polyline", "polygon"): out.append(("y|" if tag == "polyline" else "g|") + a["points"])
    return out
lines = ["// Gemaakt door tools/maak_icoontjes.py uit Lucide (ISC-licentie, zie assets/LICENSE-Lucide.txt). Niet met de hand aanpassen.",
         "using System.Collections.Generic;", "", "namespace NexusPen", "{", "    static class IconData", "    {",
         "        public static readonly Dictionary<string, string[]> All = new Dictionary<string, string[]>", "        {"]
for k in sorted(MAP):
    els = elements(MAP[k])
    lines.append('            { "%s", new string[] { %s } },' % (k, ", ".join('"' + e.replace('"', '\\"') + '"' for e in els)))
lines += ["        };", "    }", "}", ""]
open(os.path.join(os.path.dirname(__file__), "..", "src", "IconData.cs"), "w").write("\n".join(lines))
print(len(MAP), "icoontjes")
