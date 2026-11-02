# Story chronology of one game: WikiDex walkthrough order of places x trainers of each place, matched with the ROM by team.
import sys, os, re, json, unicodedata, collections
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import wiki, guide, trainers as te

HERE = os.path.dirname(os.path.abspath(__file__))
DUMPS = os.environ.get('STORY_DUMPS', os.path.join(HERE, 'dumps'))

def norm(s):
    s = s.replace('♂', ' macho').replace('♀', ' hembra')
    s = unicodedata.normalize('NFKD', s).encode('ascii', 'ignore').decode().lower()
    return re.sub(r'[^a-z0-9]', '', s)

def species_map():
    m = {}
    for f in ['UltraSol.json', 'Sw.json', 'Pt.json', 'W2.json']:
        d = json.load(open(os.path.join(DUMPS, f), encoding='utf-8'))
        for i, n in enumerate(d['Species']):
            if n and norm(n) not in m:
                m[norm(n)] = i
    m.setdefault('nidoranmacho', 32); m.setdefault('nidoranhembra', 29)
    return m

LINK = re.compile(r'\[\[([^|\]#]+)')

def candidates(heading, body):
    segs = re.split(r'\s+-\s+|,|\s+y\s+(?=[A-ZÁÉÍÓÚ])', heading)
    out = [s.strip() for s in segs if s.strip()]
    out += [m.group(1).strip() for m in LINK.finditer(body)]
    return out

def is_place(page):
    t = page['text']
    return page['resolved'] and '{{TE/Entrenador' in t and '{{Personaje' not in t and not page['resolved'].startswith(('Archivo:', 'Categoría:'))

def build(guide_title, dump_file, editions, verbose=False):
    dump = json.load(open(os.path.join(DUMPS, dump_file), encoding='utf-8'))
    sp = species_map()
    seq, seen = [], set()
    places = guide.places(guide_title)
    wiki.prefetch([c for _, h, b in places for c in candidates(h, b) if len(c) >= 3])
    for sub, heading, body in places:
        # The section's own heading names its place; the links of its text are only used when the heading is not a place
        # (they also point ahead: postgame caves, later routes).
        heads = [s.strip() for s in re.split(r'\s+-\s+|,|\s+y\s+(?=[A-ZÁÉÍÓÚ])', heading) if s.strip()]
        found = [wiki.wikitext(c) for c in heads if len(c) >= 3]
        found = [p for p in found if is_place(p)]
        if not found:
            links = [p for p in (wiki.wikitext(m.group(1).strip()) for m in LINK.finditer(body)) if is_place(p)]
            # A whole guide page without headings: every place it links, in order; a section: its first places.
            found = links if heading == sub else links[:3]
        for page in found:
            if page['resolved'] not in [s[0] for s in seq]:
                seq.append((page['resolved'], sub, heading))
    rom = collections.defaultdict(list)
    for t in dump['Trainers']:
        if t['Team']:
            rom[tuple(sorted((p['Species'], p['Level']) for p in t['Team']))].append(t['Id'])
    rom_species = collections.defaultdict(list)
    for t in dump['Trainers']:
        if t['Team']:
            rom_species[tuple(sorted(p['Species'] for p in t['Team']))].append(t)
    points, unknown = {}, collections.Counter()
    for place_index, (title, sub, heading) in enumerate(seq):
        for k, w in enumerate(te.trainers(wiki.wikitext(title)['text'])):
            if w['ed'] not in editions:
                continue
            ids = []
            for name, lv in w['team']:
                i = sp.get(norm(name))
                if i is None and ' ' in name:
                    i = sp.get(norm(name.split(' ')[0]))  # forms: "Shellos este", "Burmy planta"
                if i is None:
                    unknown[name] += 1
                ids.append((i, lv))
            if any(i is None for i, _ in ids):
                continue
            key = tuple(sorted(ids))
            hits = rom.get(key, []) if all(lv is not None for _, lv in ids) else [t['Id'] for t in rom_species.get(tuple(sorted(i for i, _ in ids)), [])]
            for h in hits:
                if h not in points:
                    points[h] = (place_index, k, title, w['cls'], w['name'])
    total = sum(1 for t in dump['Trainers'] if t['Team'])
    return dump, seq, points, total, unknown

if __name__ == '__main__':
    sys.stdout.reconfigure(encoding='utf-8')
    g, d, eds = sys.argv[1], sys.argv[2], set(sys.argv[3].split(','))
    dump, seq, points, total, unknown = build(g, d, eds)
    print('places', len(seq), 'matched', len(points), 'of', total)
    print('unknown species', unknown.most_common(10))
