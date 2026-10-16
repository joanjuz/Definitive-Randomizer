"""Build metadata-only quote matches. Input text comes from the user's local ROMs.
No dialogue or downloaded articles are written to the generated catalogue.
"""
import argparse, collections, hashlib, html, json, re, time, unicodedata, urllib.parse, urllib.request
from pathlib import Path

PEOPLE = {
    'Lillie': 'Lylia', 'Hau': 'Tilo', 'Professor Kukui': 'Profesor Kukui',
    'Gladion': 'Gladio', 'Lusamine': 'Samina', 'Guzma': 'Guzmán',
    'Plumeria': 'Francine', 'Hala': 'Kaudan', 'Olivia': 'Mayla',
    'Nanu': 'Denio', 'Hapu': 'Hela', 'Ilima': 'Liam', 'Lana': 'Nereida',
    'Kiawe': 'Kiawe', 'Mallow': 'Lulú', 'Sophocles': 'Chris', 'Acerola': 'Zarala',
    'Mina': 'Rika', 'Molayne': 'Lario', 'Professor Burnet': 'Profesora Pimpinela',
    'Wicke': 'Zyra', 'Faba': 'Fabio', 'Looker': 'Handsome', 'Anabel': 'Destra',
    'Dulse': 'Darius', 'Zossie': 'Zoe', 'Soliera': 'Miria', 'Phyco': 'Siro',
    'Giovanni': 'Giovanni', 'Colress': 'Acromo', 'Grimsley': 'Aza',
    'Professor Sycamore': 'Profesor Ciprés', 'Lysandre': 'Lysson', 'AZ': 'A. Z.',
    'Shauna': 'Xana', 'Tierno': 'Benigno / Tierno', 'Trevor': 'Trovato', 'Serena (game)': 'Serena', 'Calem (game)': 'Kalm',
    'Diantha': 'Dianta', 'Korrina': 'Corelia', 'Clemont': 'Lem', 'Valerie': 'Valeria',
    'Professor Birch': 'Profesor Abedul', 'Steven Stone': 'Máximo', 'Wally': 'Blasco',
    'Zinnia': 'Tristana', 'Archie': 'Aquiles', 'Maxie': 'Magno', 'May (game)': 'Aura', 'Brendan (game)': 'Bruno',
    'Norman': 'Norman', 'Wallace': 'Plubio', 'Courtney': 'Carola', 'Matt': 'Matías', 'Shelly': 'Silvina', 'Tabitha': 'Tatiano',
}

def normalize(text):
    text=re.sub(r'\[VAR[^\]]*\]', ' ', text, flags=re.I).replace('\\n',' ').replace('\\r',' ')
    return ' '.join(''.join(c.lower() if c.isalnum() else ' ' for c in unicodedata.normalize('NFD',text) if unicodedata.category(c)!='Mn').split())

def clean(text):
    text=html.unescape(text)
    for _ in range(5):
        text=re.sub(r'\[\[([^\[\]]+)\]\]',lambda m:m[1].split('|')[-1],text)
        text=re.sub(r'\{\{([^{}]+)\}\}',lambda m:m[1].split('|')[-1] if m[1].split('|')[0] in ('p','m','a','i','t','rt','color2') else ' ',text)
    return re.sub(r'<[^>]+>', ' ', text).replace("'''",'').replace("''",'').strip()

def game_for(heading):
    h=heading.lower()
    if 'demo' in h: return None
    if 'ultra sun' in h:
        return 'SM+USUM' if re.search(r'(?<!ultra )sun and moon',h) else 'USUM'
    if 'sun and' in h and 'moon' in h:return 'SM'
    if 'omega ruby' in h or 'alpha sapphire' in h:return 'ORAS'
    if re.search(r'x and (pok[eé]mon )?y',h):return 'XY'
    return None

def build(args):
    banks=json.loads(Path(args.input).read_text(encoding='utf-8-sig'))
    index={}
    exact={}
    for game,files in banks.items():
        table=collections.defaultdict(set)
        full=collections.defaultdict(set)
        for fid,lines in enumerate(files):
            for lid,line in enumerate(lines):
                readable=re.sub(r'\[~[^\]]*\]|\\[nrc]', ' ',line)
                words=normalize(readable).split()
                if len(words)>=6:full[' '.join(words)].add((fid,lid))
                for start in range(len(words)-8): table[' '.join(words[start:start+9])].add((fid,lid))
        index[game]=table
        exact[game]=full
    cache=Path(args.cache);cache.mkdir(parents=True,exist_ok=True)
    hits={}; failures=[]; retrieved=0
    for page,speaker in PEOPLE.items():
        title=page+'/Quotes';cached=cache/(page.replace(' ','_')+'.json')
        try:
            if cached.exists():payload=json.loads(cached.read_text(encoding='utf-8'))
            else:
                uri='https://bulbapedia.bulbagarden.net/w/api.php?'+urllib.parse.urlencode(dict(action='parse',page=title,prop='wikitext',format='json'))
                request=urllib.request.Request(uri,headers={'User-Agent':'DefinitiveRandomizer-TextResearch/1.0 (local metadata research)'})
                with urllib.request.urlopen(request,timeout=20) as response:payload=json.load(response)
                cached.write_text(json.dumps(payload),encoding='utf-8');time.sleep(.35)
            source=payload['parse']['wikitext']['*'];retrieved+=1
        except Exception as ex:
            failures.append(dict(page=title,error=str(ex)));print('UNAVAILABLE',title,flush=True);continue
        game=None;context='';url='https://bulbapedia.bulbagarden.net/wiki/'+urllib.parse.quote(title.replace(' ','_'),safe='/')
        for raw in source.splitlines():
            heading=re.match(r'^(={2,6})([^=].*?)\1\s*$',raw)
            if heading:
                found=game_for(heading[2])
                if found:game=found;context=''
                elif len(heading[1])<=3:game=None;context=''
                else:context=clean(heading[2])[:160]
                continue
            if not game or any(g not in index for g in game.split('+')):continue
            if raw.startswith(';'):context=clean(raw[1:])[:160]
            quotes=re.findall(r'"([^"\n]{35,})"',raw)
            for quote in quotes:
                for active in game.split('+'):
                    words=normalize(clean(quote)).split();locations=collections.Counter()
                    for start in range(len(words)-8):
                        matches=index[active].get(' '.join(words[start:start+9]),set())
                        if len(matches)==1:locations[next(iter(matches))]+=1
                    complete=exact[active].get(' '.join(words),set())
                    if len(complete)==1:locations[next(iter(complete))]=max(2,locations[next(iter(complete))])
                    # Two unique 9-word anchor matches, or a unique complete 6+ word message.
                    for (fid,lid),count in locations.items():
                        if count<2:continue
                        line=banks[active][fid][lid]
                        key=(active,fid,lid,speaker,context,url)
                        hits[key]=dict(game=active,file=fid,line=lid,lines=len(banks[active][fid]),bankCount=len(banks[active]),englishSha256=hashlib.sha256(normalize(line).encode()).hexdigest().upper(),speaker=speaker,context=context,source=url)
        print('REVIEWED',title,'matches',len(hits),flush=True)
    result=dict(format=1,method='Two matches of unique exact 9-word English anchors, or complete unique 6+ word message; local line SHA-256; no dialogue included',pagesRetrieved=retrieved,failures=failures,matches=list(hits.values()))
    Path(args.output).write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
    for game in banks:print(game,'files',len(set(x['file'] for x in hits.values() if x['game']==game)))

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--input',required=True);parser.add_argument('--output',required=True);parser.add_argument('--cache',required=True);build(parser.parse_args())
