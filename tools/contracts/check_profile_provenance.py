"""Contract checks for optional historical profile provenance; no game inputs."""
import argparse
import copy
import json
from pathlib import Path
import subprocess
import sys
import tempfile
from jsonschema import Draft202012Validator

ROOT = Path(__file__).resolve().parents[2]
EXAMPLE = ROOT/'docs/contracts/examples/definitive-game-data-v1.profile-provenance.synthetic.json'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--tracker', type=Path)
    parser.add_argument('--consumer', type=Path)
    args = parser.parse_args()
    schema = json.loads((ROOT/'docs/contracts/definitive-game-data-v1.schema.json').read_text())
    Draft202012Validator.check_schema(schema)
    validator = Draft202012Validator(schema)
    example = json.loads(EXAMPLE.read_text(encoding='utf-8'))
    original = json.loads((ROOT/'docs/contracts/examples/definitive-game-data-v1.player-level-caps.synthetic.json').read_text())
    passed = 0

    def check(ok, name):
        nonlocal passed
        if not ok:
            raise AssertionError(name)
        passed += 1
        print('PASS: ' + name)

    check({k:v for k,v in example.items() if k!='profileProvenance'} == original, 'ProfileExample_OnlyOptionalBlockAdded')
    check(not list(validator.iter_errors(example)), 'ProfileSchema_ExampleValid')
    check('profileProvenance' not in schema['required'], 'ProfileSchema_Optional')
    check(example['source']==original['source'], 'ProfileSchema_RootSourceUnchanged')
    variants = []

    def variant(name, change, valid=False):
        doc = copy.deepcopy(example)
        change(doc['profileProvenance'])
        variants.append((name, doc, valid))

    for field in example['profileProvenance']:
        variant('Missing_'+field, lambda b,f=field:b.pop(f))
    for field in example['profileProvenance']['profile']:
        variant('MissingProfile_'+field,lambda b,f=field:b['profile'].pop(f),field=='author')
    variant('Future',lambda b:(b.clear(),b.update(version=2,future={'x':[1]})),True)
    for version in (0,True,'1',1.5):
        variant('InvalidVersion_'+str(version),lambda b,v=version:b.update(version=v))
    for field,value in [('status','verified'),('currentMatch','exact'),('subsequentChanges','unknown'),('gameId','sun'),('sections',[]),('extra',True)]:
        variant('Invalid_'+field,lambda b,f=field,v=value:b.update({f:v}))
    for field,value in [('name',''),('name','x'*121),('name','C:\\private'),('name','/home/private'),('name','a\n'),('author','x'*81),('recipeSha256','a'*64),('recipeSha256','A'*63),('version',True),('format','other'),('extra','private')]:
        variant('InvalidProfile_'+field+'_'+str(value)[:8],lambda b,f=field,v=value:b['profile'].update({f:v}))
    for value in [['actions/a']*2,['resources/private'],['manual/a/b'],['manual/'+'a'*81],['actions/a']*65]:
        variant('InvalidSections_'+str(len(value)),lambda b,v=value:b.update(sections=v))
    variant('MaximumNames',lambda b:b['profile'].update(name='x'*120,author='y'*80),True)
    variant('MaximumSections',lambda b:b.update(sections=[f'actions/a{i:02}' for i in range(64)]),True)
    variant('ModifiedHistory',lambda b:b.update(subsequentChanges='recorded'),True)
    for name,doc,valid in variants:
        check((not list(validator.iter_errors(doc)))==valid,'ProfileSchema_'+name)
    check(len(json.dumps(example['profileProvenance']).encode())<16*1024,'ProfileExample_Bounded')
    # Every pinned historical consumer imports the real extended example and persists it unchanged.
    for repo in dict.fromkeys(p for p in (args.tracker,args.consumer) if p):
        code = '''import json,sys,tempfile
from pathlib import Path
sys.path.insert(0,sys.argv[1])
from tracker.game_data import GameDataManager,validate_document
raw=Path(sys.argv[2]).read_text(encoding='utf-8'); doc=validate_document(raw)
with tempfile.TemporaryDirectory() as folder:
 p=Path(folder)/'definitive-game-data.json'; m=GameDataManager(p)
 v=m.preview(raw,'Ultra Sun 1.0',lambda _:[])
 assert not p.exists()
 m.apply(v['previewToken'],'Ultra Sun 1.0',True)
 r=GameDataManager(p)
 assert r.document==doc and r.status('Ultra Sun 1.0')['loaded']
 assert r.document['profileProvenance']==doc['profileProvenance']
 assert r.document['playerLevelCaps']==doc['playerLevelCaps']
 m.remove('Ultra Sun 1.0'); assert not p.exists()
'''
        child=subprocess.run([sys.executable,'-I','-c',code,str(repo.resolve()),str(EXAMPLE)])
        check(child.returncode==0,'ProfileLegacyConsumer_'+repo.name)
    print(f'Completed {passed} profile provenance contract checks; 0 failed.')


if __name__=='__main__':
    main()
