# TE/Entrenador templates of a WikiDex place page: edition, class, name, team (species name, level).
import re

def templates(text, name):
    out, i = [], 0
    key = '{{' + name
    while True:
        i = text.find(key, i)
        if i < 0:
            return out
        depth, j = 0, i
        while j < len(text):
            if text.startswith('{{', j):
                depth += 1; j += 2; continue
            if text.startswith('}}', j):
                depth -= 1; j += 2
                if depth == 0:
                    break
                continue
            j += 1
        out.append((i, text[i:j]))
        i = j

def params(body):
    body = body[2:-2]
    # top-level split on |
    parts, depth, cur = [], 0, ''
    k = 0
    while k < len(body):
        if body.startswith('{{', k) or body.startswith('[[', k):
            depth += 1; cur += body[k:k+2]; k += 2; continue
        if body.startswith('}}', k) or body.startswith(']]', k):
            depth -= 1; cur += body[k:k+2]; k += 2; continue
        if body[k] == '|' and depth == 0:
            parts.append(cur); cur = ''; k += 1; continue
        cur += body[k]; k += 1
    parts.append(cur)
    res = {}
    for p in parts[1:]:
        if '=' in p:
            a, b = p.split('=', 1)
            res[a.strip()] = b.strip()
    return res

def strip(v):
    v = re.sub(r'\[\[(?:[^|\]]*\|)?([^\]]*)\]\]', r'\1', v)
    v = re.sub(r'<[^>]+>|\{\{[^}]*\}\}', '', v)
    return v.strip()

def trainers(text):
    out = []
    for pos, body in templates(text, 'TE/Entrenador'):
        p = params(body)
        team = []
        for s in range(1, 7):
            sp, lv = strip(p.get(f'P{s}', '')), strip(p.get(f'NvP{s}', ''))
            if sp:
                m = re.search(r'\d+', lv)
                team.append((sp, int(m.group()) if m else None))
        out.append({'pos': pos, 'ed': p.get('EdPok', '').strip(), 'cls': strip(p.get('Entrenador', '')), 'name': strip(p.get('Nombre', '')), 'team': team})
    return out
