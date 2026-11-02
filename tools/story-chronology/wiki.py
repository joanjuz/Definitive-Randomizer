# WikiDex API access with an on-disk cache (wikitext of each page, redirects resolved).
import json, os, time, urllib.request, urllib.parse, hashlib
CACHE = os.environ.get('STORY_CACHE', os.path.join(os.path.dirname(os.path.abspath(__file__)), 'cache'))
os.makedirs(CACHE, exist_ok=True)
API = 'https://www.wikidex.net/api.php'

def _get(params):
    url = API + '?' + urllib.parse.urlencode(params)
    for attempt in range(4):
        try:
            req = urllib.request.Request(url, headers={'User-Agent': 'DefinitiveRandomizer-story/1.0 (chronology research)'})
            with urllib.request.urlopen(req, timeout=60) as r:
                return json.loads(r.read().decode('utf-8'))
        except Exception as e:
            time.sleep(2 + attempt * 3)
    raise RuntimeError('fetch failed ' + url)

def wikitext(title):
    key = hashlib.sha1(title.encode('utf-8')).hexdigest()
    path = os.path.join(CACHE, key + '.json')
    if os.path.exists(path):
        return json.load(open(path, encoding='utf-8'))
    time.sleep(0.4)
    d = _get({'action': 'parse', 'page': title, 'prop': 'wikitext', 'redirects': 1, 'format': 'json', 'formatversion': 2})
    if 'error' in d:
        res = {'title': title, 'resolved': None, 'text': ''}
    else:
        res = {'title': title, 'resolved': d['parse']['title'], 'text': d['parse']['wikitext']}
    json.dump(res, open(path, 'w', encoding='utf-8'), ensure_ascii=False)
    return res

def subpages(prefix):
    d = _get({'action': 'query', 'list': 'allpages', 'apprefix': prefix + '/', 'aplimit': 500, 'format': 'json'})
    return [p['title'] for p in d['query']['allpages']]

def prefetch(titles):
    """Fill the cache 50 pages per request (redirects resolved)."""
    todo = []
    for t in dict.fromkeys(titles):
        key = hashlib.sha1(t.encode('utf-8')).hexdigest()
        if not os.path.exists(os.path.join(CACHE, key + '.json')):
            todo.append(t)
    for i in range(0, len(todo), 50):
        batch = todo[i:i + 50]
        time.sleep(0.5)
        d = _get({'action': 'query', 'prop': 'revisions', 'rvprop': 'content', 'rvslots': 'main', 'redirects': 1,
                  'titles': '|'.join(batch), 'format': 'json', 'formatversion': 2})
        q = d.get('query', {})
        norm = {n['from']: n['to'] for n in q.get('normalized', [])}
        redir = {r['from']: r['to'] for r in q.get('redirects', [])}
        pages = {p['title']: p for p in q.get('pages', [])}
        for t in batch:
            r = norm.get(t, t)
            r = redir.get(r, r)
            p = pages.get(r)
            text = ''
            if p and not p.get('missing') and not p.get('invalid') and p.get('revisions'):
                text = p['revisions'][0]['slots']['main']['content']
            res = {'title': t, 'resolved': r if text else None, 'text': text}
            key = hashlib.sha1(t.encode('utf-8')).hexdigest()
            json.dump(res, open(os.path.join(CACHE, key + '.json'), 'w', encoding='utf-8'), ensure_ascii=False)
