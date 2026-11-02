# Story order of places from a WikiDex walkthrough: subpages in index order, then the place headings inside each one.
import re, wiki

def ordered_subpages(guide):
    pages = wiki.subpages(guide)
    index = wiki.wikitext(guide)['text']
    pos = {}
    for p in pages:
        sub = p.split('/', 1)[1]
        m = re.search(r'\[\[\s*(?:' + re.escape(guide) + r')?/' + re.escape(sub) + r'/?\s*[|\]#]', index)
        pos[p] = m.start() if m else None
    def num(p):
        m = re.search(r'(\d+)$', p)
        return int(m.group(1)) if m else 10**6
    linked = [p for p in pages if pos[p] is not None]
    if len(linked) >= len(pages) * 0.6:
        return sorted(linked, key=lambda p: pos[p]) + sorted([p for p in pages if pos[p] is None], key=num)
    return sorted(pages, key=num)

HEAD = re.compile(r'título\s*=\s*([^\n|}]+)|^(={2,4})\s*([^=\n]+?)\s*\2\s*$', re.M)

def clean(name):
    name = re.sub(r'\[\[(?:[^|\]]*\|)?([^\]]*)\]\]', r'\1', name)
    name = re.sub(r"'''?|<[^>]+>|\{\{[^}]*\}\}", '', name)
    return name.strip()

def places(guide):
    """[(subpage, heading, section text)] in story order."""
    out = []
    for page in ordered_subpages(guide):
        text = wiki.wikitext(page)['text']
        heads = [(m.start(), clean(m.group(1) or m.group(3) or '')) for m in HEAD.finditer(text)]
        heads = [h for h in heads if h[1]]
        if not heads:
            out.append((page.split('/', 1)[1], page.split('/', 1)[1], text))
        for k, (pos, h) in enumerate(heads):
            end = heads[k + 1][0] if k + 1 < len(heads) else len(text)
            out.append((page.split('/', 1)[1], h, text[pos:end]))
    return out
