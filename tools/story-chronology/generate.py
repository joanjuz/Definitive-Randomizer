# Story chronology profiles (pk3DS.Core/Story/<key>.json) from the WikiDex walkthroughs and the games' own trainers.
import sys, os, re, json, collections
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import wiki, guide, build, trainers as te

GAMES = {
    'DP':   dict(guide='Guía de Pokémon Diamante y Pokémon Perla', dump='D.json', leaders=r'^Leader$', e4=r'^Elite Four$', champ=r'^Champion$'),
    'Pt':   dict(guide='Guía de Pokémon Platino', dump='Pt.json', leaders=r'^Leader$', e4=r'^Elite Four$', champ=r'^Champion$'),
    'HGSS': dict(guide='Guía de Pokémon Oro HeartGold y Pokémon Plata SoulSilver', dump='HG.json', leaders=r'^Leader$', e4=r'^Elite Four$', champ=r'^Champion$'),
    'BW':   dict(guide='Guía de Pokémon Negro y Pokémon Blanco', dump='B.json', leaders=r'^Leader$', e4=r'^Elite Four$', final=['Ghetsis']),
    'B2W2': dict(guide='Guía de Pokémon Negro 2 y Pokémon Blanco 2', dump='W2.json', leaders=r'^Leader$', e4=r'^Elite Four$', champ=r'^Champion$'),
    'XY':   dict(guide='Guía de Pokémon X y Pokémon Y', dump='X.json', leaders=r'^Líder$', e4=r'^Alto Mando$', champ=r'^Campeona?$'),
    'ORAS': dict(guide='Guía de Pokémon Rubí Omega y Pokémon Zafiro Alfa', dump='OR.json', leaders=r'^Líder(es)?$', e4=r'^Alto Mando$', champ=r'^Campeón$'),
    'SM':   dict(guide='Guía de Pokémon Sol y Pokémon Luna', dump='Sol.json', leaders=r'^Kahuna$', e4=r'^Alto Mando$', final=['Kukui']),
    'USUM': dict(guide='Guía de Pokémon Ultrasol y Pokémon Ultraluna', dump='UltraSol.json', leaders=r'^Kahuna$', e4=r'^Alto Mando$', final_ids=[494, 495, 496]),
    'SWSH': dict(guide='Guía de Pokémon Espada y Pokémon Escudo', dump='Sw.json',
                 leader_names=[['Percy'], ['Cathy'], ['Naboru'], ['Judith', 'Alistair'], ['Sally'], ['Morris', 'Mel'], ['Nerio'], ['Roy']], final=['Lionel']),
}


def ace(t):
    return max(p['Level'] for p in t['Team'])


def team_key(t):
    return tuple(sorted((p['Species'], p['Level']) for p in t['Team']))


GYM = re.compile(r'(?i)gimnasio|estadio')
PAGES = {}


def first_instances(trainers, pred, below=101):
    """Per name, its milestone battle: the one on its gym/stadium/league page when the walkthrough places it, else its lowest
    level (the first battle); several IDs when there are version or starter variants of the same level."""
    by = collections.defaultdict(list)
    for t in trainers:
        if pred(t) and t['Name'] and ace(t) < below:
            by[t['Name']].append(t)
    out = []
    for ts in by.values():
        gym = [t for t in ts if GYM.search(PAGES.get(t['Id'], ''))]
        # Else the lowest ID: rematches and special battles come later in the table.
        first = min(gym or ts, key=lambda t: (ace(t), t['Id'])) if gym else min(ts, key=lambda t: t['Id'])
        levels = sorted(p['Level'] for p in first['Team'])
        out.append([t for t in ts if t is first or (ace(t) == ace(first) and sorted(p['Level'] for p in t['Team']) == levels and abs(t['Id'] - first['Id']) <= 6)])
    return out


def place_pages(cfg):
    places = guide.places(cfg['guide'])
    wiki.prefetch([c for _, h, b in places for c in build.candidates(h, b) if len(c) >= 3])
    pages, seen = [], set()
    for _, h, b in places:
        for c in build.candidates(h, b):
            if c in seen or len(c) < 3:
                continue
            seen.add(c)
            page = wiki.wikitext(c)
            if build.is_place(page) and page['resolved'] not in [p['resolved'] for p in pages]:
                pages.append(page)
    return pages


def detect_editions(pages, trainers):
    """WikiDex edition codes whose teams match this game's trainers."""
    sp = build.species_map()
    rom = {team_key(t) for t in trainers}
    scores = collections.Counter()
    for page in pages:
        for w in te.trainers(page['text']):
            ids = [(sp.get(build.norm(s)) or sp.get(build.norm(s.split(' ')[0])), l) for s, l in w['team']]
            if ids and all(i is not None and l is not None for i, l in ids) and tuple(sorted(ids)) in rom:
                scores[w['ed']] += 1
    best = max(scores.values()) if scores else 0
    # Team equality is what places a trainer; the edition only discards stray matches of other games' sections.
    return {c for c, s in scores.items() if s >= 10}, scores


def generate(key, cfg):
    dump = json.load(open(os.path.join(build.DUMPS, cfg['dump']), encoding='utf-8'))
    trainers = [t for t in dump['Trainers'] if t['Team']]
    # Unused placeholder trainers (the same team ten times or more, e.g. Rattata level 5 in Gen4).
    counts = collections.Counter(team_key(t) for t in trainers)
    trainers = [t for t in trainers if counts[team_key(t)] < 10]
    by_id = {t['Id']: t for t in trainers}

    pages = place_pages(cfg)
    editions, scores = detect_editions(pages, trainers)
    _, seq, points, total, unknown = build.build(cfg['guide'], cfg['dump'], editions)
    place = {i: p[0] * 1000 + p[1] for i, p in points.items() if i in by_id}
    PAGES.clear()
    PAGES.update({i: p[2] for i, p in points.items()})

    if 'final_ids' in cfg:
        final = [by_id[i] for i in cfg['final_ids']]
    else:
        if 'champ' in cfg:
            finals = first_instances(trainers, lambda t: re.search(cfg['champ'], t['Class']))
        else:
            finals = first_instances(trainers, lambda t: t['Name'] in cfg['final'])
        final = min(finals, key=lambda g: ace(g[0]))
    final_ace = ace(final[0])
    # Milestones: leaders (equal first-battle level = version/starter variants of one gym), Elite Four, Champion or final battle.
    groups = []
    if 'leader_names' in cfg:
        for names in cfg['leader_names']:
            groups.append(['Líder', [t for g in first_instances(trainers, lambda t: t['Name'] in names, final_ace) for t in g]])
    else:
        firsts = sorted(first_instances(trainers, lambda t: re.search(cfg['leaders'], t['Class']), final_ace),
                        key=lambda g: (ace(g[0]), min((place[t['Id']] for t in g if t['Id'] in place), default=10 ** 9), g[0]['Id']))
        def same_battle(a, b):
            # Version or starter variants of one gym: the same gym page, or (both unplaced) the same levels.
            pa, pb = {PAGES.get(x['Id']) for x in a} - {None}, {PAGES.get(x['Id']) for x in b} - {None}
            if pa and pb:
                return bool(pa & pb)
            return not pa and not pb and sorted(p['Level'] for p in a[0]['Team']) == sorted(p['Level'] for p in b[0]['Team'])
        for g in firsts:
            if groups and ace(groups[-1][1][0]) == ace(g[0]) and same_battle(groups[-1][1], g):
                groups[-1][1].extend(g)
            else:
                groups.append(['Líder', list(g)])
    groups = [g for g in groups if ace(g[1][0]) < final_ace]
    if 'e4' in cfg:
        e4 = [t for g in first_instances(trainers, lambda t: re.search(cfg['e4'], t['Class'])) for t in g if ace(t) <= final_ace]
        if e4:
            groups.append(['Alto Mando', e4])
    groups.append(['Final', final])

    def gplace(g):
        ps = [place[t['Id']] for t in g[1] if t['Id'] in place]
        return min(ps) if ps else None

    caps = [min(ace(t) for t in g[1]) for g in groups]
    milestone_stage = {t['Id']: s for s, g in enumerate(groups) for t in g[1]}
    # The bosses' places must move forward along the story; one that comes before the previous boss (an early mention of the
    # League in the introduction, for instance) is ignored and the levels decide around it.
    gplaces, last = [], -1
    for g in groups:
        # A leader bounds the story only on its own gym/stadium page: a kahuna's grand trial takes place in a town visited
        # long before (its first visit is what the walkthrough order gives).
        gp = gplace(g) if g[0] != 'Líder' or any(GYM.search(PAGES.get(x['Id'], '')) for x in g[1]) else None
        if gp is not None and gp > last:
            gplaces.append(gp)
            last = gp
        else:
            gplaces.append(None)
    final_place = gplaces[-1]

    def stage_by_level(a):
        for s, c in enumerate(caps):
            if c >= a:
                return s
        return len(groups)

    out = {}
    for t in trainers:
        i, a = t['Id'], ace(t)
        if i in milestone_stage:
            out[i] = [milestone_stage[i], place.get(i, 10 ** 7), 1 if i in place else 0]
            continue
        s = None
        if i in place:
            if final_place is not None and place[i] > final_place and a > caps[max(0, len(caps) - 2)]:
                s = len(groups)
            elif final_place is not None and place[i] > final_place:
                s = None  # after the League in the walkthrough but at story level: its level decides
            else:
                # The walkthrough bounds the window between the placed bosses around the trainer; its level picks inside.
                after = [k for k, gp in enumerate(gplaces) if gp is not None and gp >= place[i]]
                before = [k for k, gp in enumerate(gplaces) if gp is not None and gp < place[i]]
                low, high = (before[-1] + 1 if before else 0), (after[0] if after else len(groups) - 1)
                if low <= high:
                    s = next((k for k in range(low, high + 1) if caps[k] >= a - 2), high)
            if s is not None and s < len(groups) and caps[s] < a - 2:
                s = None  # reached later (Surf, Strength...): its level decides
        if s is None:
            # Nobody regular stands between the last milestone before the final battle and it: a higher level is postgame.
            out[i] = [stage_by_level(a) if a <= caps[max(0, len(caps) - 2)] else len(groups), None, 0]
        else:
            out[i] = [s, place[i], 1]
    # Level-placed trainers share the position of the walkthrough trainer of their window with the closest lower level.
    for i, v in out.items():
        if v[1] is None:
            same = sorted((ace(by_id[j]), w[1]) for j, w in out.items() if w[0] == v[0] and w[2] == 1 and j not in milestone_stage)
            lower = [o for a2, o in same if a2 <= ace(by_id[i])]
            v[1] = lower[-1] if lower else (same[0][1] if same else 0)
    stages = [{'name': kind + ' · ' + ' / '.join(sorted({t['Name'] for t in g})), 'milestones': sorted(t['Id'] for t in g), 'cap': caps[s]}
              for s, (kind, g) in enumerate(groups)]
    profile = {'game': key, 'source': 'WikiDex: ' + cfg['guide'] + ' y páginas de lugares (ediciones ' + ', '.join(sorted(editions)) + ')',
               'stages': stages, 'points': {str(i): v for i, v in sorted(out.items())}}
    report = dict(trainers=len(trainers), walkthrough=sum(1 for v in out.values() if v[2] == 1),
                  postgame=sum(1 for v in out.values() if v[0] == len(groups)), places=len(seq), editions=sorted(editions),
                  scores=dict(scores.most_common(6)), stages=[(s['name'], s['cap']) for s in stages])
    return profile, report


if __name__ == '__main__':
    sys.stdout.reconfigure(encoding='utf-8')
    outdir = sys.argv[1]
    for key in sys.argv[2:]:
        profile, report = generate(key, GAMES[key])
        json.dump(profile, open(os.path.join(outdir, key + '.json'), 'w', encoding='utf-8'), ensure_ascii=False, separators=(',', ':'))
        print(key, json.dumps(report, ensure_ascii=False))
