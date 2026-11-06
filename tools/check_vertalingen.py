#!/usr/bin/env python3
"""Zoekt alle teksten die vertaald moeten worden en maakt src/LangData.cs uit tools/vertalingen.txt.
Gebruik: python3 tools/check_vertalingen.py        (maakt LangData.cs, meldt wat ontbreekt)
         python3 tools/check_vertalingen.py --lijst (toont alle teksten)"""
import re, glob, os, sys
root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
lit = r'"((?:[^"\\]|\\.)*)"'

def teksten():
    found = []
    def add(s):
        if s and s not in found and re.search('[A-Za-z]', s): found.append(s)
    for f in sorted(glob.glob(os.path.join(root, 'src', '*.cs'))):
        if f.endswith('LangData.cs'): continue
        t = open(f, encoding='utf-8').read()
        for m in re.finditer(r'\bL\.[TF]\(\s*' + lit, t): add(m.group(1))
        for m in re.finditer(r'\bT\(' + lit + r',\s*' + lit + r',\s*"#', t): add(m.group(1)); add(m.group(2))
        for m in re.finditer(r'new Shortcut\(' + lit + r',\s*' + lit, t): add(m.group(2))
        for line in t.split('\n'):
            if '// vertaal' in line:
                for m in re.finditer(lit, line.split('// vertaal')[0]): add(m.group(1))
    return found

def tabel():
    rows = {}
    p = os.path.join(root, 'tools', 'vertalingen.txt')
    for line in open(p, encoding='utf-8'):
        line = line.rstrip('\n')
        if not line or line.startswith('#'): continue
        parts = [x.strip() for x in line.split(' | ')]
        if len(parts) != 4: print('Fout in regel:', line); sys.exit(1)
        rows[parts[0]] = parts
    return rows

def cs(s): return '"' + s + '"'  # staat al als C#-tekst in de tabel

if __name__ == '__main__':
    all_ = teksten()
    if '--lijst' in sys.argv:
        for s in all_: print(s)
        sys.exit(0)
    rows = tabel()
    missing = [s for s in all_ if s not in rows]
    extra = [k for k in rows if k not in all_]
    for s in missing: print('ONTBREEKT:', s)
    for s in extra: print('NIET GEBRUIKT:', s)
    bad = [k for k, r in rows.items() if '—' in ''.join(r)]
    for k in bad: print('LANG STREEPJE:', k)
    out = ['// Gemaakt door tools/check_vertalingen.py uit tools/vertalingen.txt. Niet met de hand aanpassen.',
           'namespace NexusPen', '{', '    static class LangData', '    {',
           '        // nl, en, fr, es', '        public static readonly string[][] Rows = new string[][]', '        {']
    for s in all_:
        if s in rows:
            out.append('            new string[] { ' + ', '.join(cs(x) for x in rows[s]) + ' },')
    out += ['        };', '    }', '}', '']
    open(os.path.join(root, 'src', 'LangData.cs'), 'w', encoding='utf-8').write('\n'.join(out))
    print(len(all_), 'teksten,', len(missing), 'ontbreken')
    sys.exit(1 if missing or bad else 0)
