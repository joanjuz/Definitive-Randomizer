"""Real C# DGD fixture -> real Tracker import service -> persistence and browser presentation."""
import argparse
import copy
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import threading
from jsonschema import Draft202012Validator

ROOT = Path(__file__).resolve().parents[2]


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--producer',type=Path,required=True)
    parser.add_argument('--consumer',type=Path,required=True)
    parser.add_argument('--legacy',type=Path,action='append',default=[])
    parser.add_argument('--browser',action='store_true')
    parser.add_argument('--artifacts',type=Path)
    args=parser.parse_args()
    folder=args.producer.resolve(); consumer=args.consumer.resolve()
    schema=json.loads((ROOT/'docs/contracts/definitive-game-data-v1.schema.json').read_text())
    validator=Draft202012Validator(schema)
    names=['absent','preview-only','applied','modified','undone']
    docs={n:json.loads((folder/(n+'.json')).read_text(encoding='utf-8')) for n in names}
    passed=0

    def check(condition,name):
        nonlocal passed
        if not condition:raise AssertionError(name)
        passed+=1;print('PASS: '+name,flush=True)

    for name,doc in docs.items():
        check(not list(validator.iter_errors(doc)),'ProfileInterop_Schema_'+name)
        check(doc['source']['scope']=='personal,moves,evolutions,gametext','ProfileInterop_RootScope_'+name)
        check(('profileProvenance' in doc)==(name in ('applied','modified')),'ProfileInterop_EvidenceOnly_'+name)
    check(docs['applied']['profileProvenance']['profile']['name']=='Perfil aplicado','ProfileInterop_RealProducerName')
    check(docs['applied']['profileProvenance']['sections']==['actions/personal'],'ProfileInterop_RealExecutedSelection')
    check(docs['modified']['profileProvenance']['subsequentChanges']=='recorded','ProfileInterop_RealLaterEdit')
    check(docs['absent']==docs['preview-only'],'ProfileInterop_PreviewDoesNotChangeSavedExport')

    # Import through the actual consumer service, not a parallel model of its behavior.
    sys.path.insert(0,str(consumer))
    from tracker.service import TrackerService
    from tracker.game_data import GAMES
    for name,doc in docs.items():
        with tempfile.TemporaryDirectory(prefix='dgd-profile-interop-') as temp:
            path=Path(temp)/'run-a'/'state.json'
            service=TrackerService(path); game=GAMES[doc['game']['id']][0]
            service.update(game=game,party=[None]*6,stale=True)
            service.handle({'action':'level_caps_set','stages':[{'id':'one','name':'Propios','cap':35}]})
            own_caps=copy.deepcopy(service.snapshot()['level_caps'])
            preview=service.preview_game_data((folder/(name+'.json')).read_text(encoding='utf-8'),game)
            check(not path.with_name('definitive-game-data.json').exists(),'ProfileInterop_PreviewNoPersistence_'+name)
            service.apply_game_data(preview['previewToken'],game,True)
            view=service.snapshot()['game_data']['profileProvenance']
            check(view==preview['profileProvenance'],'ProfileInterop_PreviewAndAppliedAgree_'+name)
            if name in ('applied','modified'):
                b=doc['profileProvenance']
                check((view['state'],view['name'],view['author'],view['gameId'],view['sections'],view['recipeSha256'],view['subsequentChanges'],view['currentMatch'])==
                      ('historical',b['profile']['name'],b['profile']['author'],b['gameId'],b['sections'],b['profile']['recipeSha256'],b['subsequentChanges'],'not-verified'),
                      'ProfileInterop_ExactContractInterpretation_'+name)
            else:check(view['state']=='absent','ProfileInterop_NoFalseAttribution_'+name)
            reopened=TrackerService(path);reopened.update(game=game,party=[None]*6,stale=True)
            check(reopened.snapshot()['game_data']['profileProvenance']==view,'ProfileInterop_ReopenedSummary_'+name)
            check(service.snapshot()['level_caps']==own_caps,'ProfileInterop_AcceptedCapsUnchanged_'+name)
            check(TrackerService(Path(temp)/'run-b'/'state.json').game_data.document is None,'ProfileInterop_RunIsolation_'+name)
            service.remove_game_data()
            check(not service.snapshot()['game_data']['loaded'],'ProfileInterop_Withdraw_'+name)

    for legacy in args.legacy:
        code='''import json,sys,tempfile
from pathlib import Path
sys.path.insert(0,sys.argv[1])
from tracker.game_data import GameDataManager
for p in Path(sys.argv[2]).glob('*.json'):
 raw=p.read_text(encoding='utf-8');doc=json.loads(raw)
 game='Ultra Sun 1.0' if doc['game']['id']=='ultra-sun' else 'Ultra Moon 1.0'
 with tempfile.TemporaryDirectory() as folder:
  path=Path(folder)/'definitive-game-data.json';m=GameDataManager(path)
  v=m.preview(raw,game,lambda _:[]);m.apply(v['previewToken'],game,True)
  assert GameDataManager(path).document==doc
'''
        result=subprocess.run([sys.executable,'-I','-c',code,str(legacy.resolve()),str(folder)])
        check(result.returncode==0,'ProfileInterop_LegacyConsumer_'+legacy.name)

    if args.browser:
        from playwright.sync_api import sync_playwright,expect
        from server import TrackerHTTPServer,make_handler
        artifacts=(args.artifacts or ROOT/'validation-logs/profile-browser').resolve();artifacts.mkdir(parents=True,exist_ok=True)
        with tempfile.TemporaryDirectory(prefix='dgd-profile-browser-') as temp:
            service=TrackerService(Path(temp)/'state.json');service.update(game='Ultra Sun 1.0',party=[None]*6,stale=True)
            server=TrackerHTTPServer(('127.0.0.1',0),make_handler(service,'profile-interop-test'))
            thread=threading.Thread(target=server.serve_forever,daemon=True);thread.start()
            try:
                with sync_playwright() as playwright:
                    browser=playwright.chromium.launch();page=browser.new_page(viewport={'width':1366,'height':900})
                    errors=[];page.on('pageerror',lambda err:errors.append(str(err)))
                    page.goto(f'http://127.0.0.1:{server.server_port}/',wait_until='domcontentloaded')
                    page.locator('#section-nav button[data-tab="connection"]').click()
                    page.evaluate("document.getElementById('game-data-settings').open=true")
                    page.locator('#game').select_option('Ultra Sun 1.0')
                    expect(page.locator('#game')).to_have_value('Ultra Sun 1.0')
                    for name in ('applied','modified','absent'):
                        page.locator('#game-data-file').set_input_files(str(folder/(name+'.json')))
                        page.locator('#preview-game-data').click()
                        expect(page.locator('#game-data-preview')).to_be_visible()
                        if name!='absent':expect(page.locator('#game-data-profile-text')).to_contain_text('Perfil aplicado')
                        page.locator('#game-data-ack').check();page.locator('#apply-game-data').click()
                        expect(page.locator('#game-data-profile-title')).to_have_text('Perfil de origen')
                        text=page.locator('#game-data-profile-text')
                        if name=='absent':expect(text).to_contain_text('no incluye procedencia')
                        else:
                            expect(text).to_contain_text('Autor declarado: Autora declarada')
                            expect(text).to_contain_text('No acredita coincidencia exacta')
                            if name=='modified':expect(text).to_contain_text('Hay cambios posteriores registrados')
                        page.screenshot(path=str(artifacts/(name+'.png')),full_page=True)
                        if name=='modified':
                            page.set_viewport_size({'width':390,'height':844})
                            check(page.locator('#game-data-profile').evaluate('(el)=>el.scrollWidth<=el.clientWidth'),'ProfileInterop_BrowserNarrowNoOverflow')
                            page.screenshot(path=str(artifacts/'modified-narrow.png'),full_page=True)
                            page.set_viewport_size({'width':1366,'height':900})
                        check(page.locator('#game-data-profile').is_visible() and
                              service.game_data.status('Ultra Sun 1.0')['profileProvenance']['state']==('absent' if name=='absent' else 'historical'),
                              'ProfileInterop_ActualBrowserPresentation_'+name)
                    check(not errors,'ProfileInterop_BrowserNoScriptErrors')
                    browser.close()
            finally:
                server.shutdown();server.server_close();thread.join();service.stop.set()
    print(f'Completed {passed} real profile interoperability checks; 0 failed.')


if __name__=='__main__':main()
