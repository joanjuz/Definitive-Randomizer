"""Execute the XY mint Bag handler with original native PK/Bag code in Unicorn.

PK crypto, egg/nature access, stats, inventory remove/find/quantity are real
instructions from the pinned executable. Filesystem-backed item lookup and UI
are explicit contracts. Synthetic RAM/personal records, NOT gameplay evidence.
No game file or save is modified. The optional candidate is a detached CRO.
"""
import argparse
import importlib.util
import json
import struct
from pathlib import Path

from inspect_mints import analyze, read_project, verify_unchanged, require, Cro, garc_members, u32, region

ROOT = Path(__file__).resolve().parents[3]
ITEMS = [113,114,115,*range(120,134),426,427,622,623]
NATURES = [1,3,4,2,5,8,9,7,15,16,19,17,20,21,23,22,10,11,13,14,12]


def template():
    spec=importlib.util.spec_from_file_location('xy_mint_generator',ROOT/'native/3ds/xy-nature-mints/generate.py')
    module=importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module.generate()[0]


def branch(at,target):
    delta=target-at-8
    require(delta%4==0 and -0x2000000<=delta<0x2000000,'Invalid replay branch')
    return 0xea000000|((delta>>2)&0xffffff)


def run(blobs,item,old_nature=0,quantity=2,egg=False,fainted=False,missing=False,
        bad_ui=False,bad_manager=False,unclosed=False,load=0xa00000,candidate=None):
    from unicorn import Uc, UC_ARCH_ARM, UC_MODE_ARM, UC_HOOK_CODE, UC_HOOK_MEM_WRITE
    from unicorn.arm_const import (UC_ARM_REG_R0,UC_ARM_REG_R1,UC_ARM_REG_R2,UC_ARM_REG_R4,
        UC_ARM_REG_SP,UC_ARM_REG_LR,UC_ARM_REG_PC,UC_ARM_REG_C1_C0_2,UC_ARM_REG_FPEXC)
    require(item in ITEMS,'Only an allocated XY candidate item can enter this replay')
    code=blobs['ExeFS/.code.bin']; base=0x100000
    bag=bytearray(candidate if candidate is not None else blobs['RomFS/DllBag.cro'])
    if candidate is None:
        start=0x20000; payload=bytearray(template())
        for i,(obj,nature) in enumerate(zip(ITEMS,NATURES)):
            struct.pack_into('<I',payload,0x900+i*4,obj|(nature<<24))
        bag.extend(bytes(start+len(payload)-len(bag)));bag[start:start+len(payload)]=payload
        for at,target in ((0x428,start),(0x2b5c,start+0x100)):
            struct.pack_into('<I',bag,at,branch(at,target))
        original=blobs['RomFS/DllBag.cro']
    else:
        marker=bag.find(b'XYMINT-E1\0');require(marker>=0,'Missing candidate marker');start=marker-0xa00
        original=bytes(bag)
    # Relocate the original static import literals, independently per title.
    static=Cro(blobs['RomFS/static.crs'],static=True)
    slots=set(range(0x184,0xc80,8))
    for link in Cro(original).static_links(static,slots):
        struct.pack_into('<I',bag,int(link['slot'],16),int(link['target_va'],16))
    uc=Uc(UC_ARCH_ARM,UC_MODE_ARM)
    uc.mem_map(base,(len(code)+4095)&~4095);uc.mem_write(base,code)
    uc.mem_map(load,(len(bag)+4095)&~4095);uc.mem_write(load,bytes(bag))
    ram,stop=0x800000,0xc00000
    uc.mem_map(ram,0x60000);uc.mem_map(stop,0x1000)
    core,accessor,boxed,stats=ram,ram+0x100,ram+0x200,ram+0x300
    main,context,window,ui,array=ram+0x2000,ram+0x2100,ram+0x2300,ram+0x2400,ram+0x2500
    work,inventory,meds=ram+0x2b00,ram+0x3000,ram+0x5000
    manager,cache,table,pockets,stack=ram+0x10000,ram+0x10100,ram+0x20000,ram+0x30000,ram+0x5f000
    def word(at,value):uc.mem_write(at,struct.pack('<I',value))
    def reg(r):return uc.reg_read(r)
    def ret(value=0):uc.reg_write(UC_ARM_REG_R0,value);uc.reg_write(UC_ARM_REG_PC,reg(UC_ARM_REG_LR))
    def count(obj):
        data=bytes(uc.mem_read(meds,256))
        return sum(n for i,n in struct.iter_unpack('<HH',data) if i==obj)
    word(core+12,accessor);uc.mem_write(accessor,struct.pack('<III',0,stats,boxed)+bytes(4))
    ec=0x12345678
    positions=region(code,u32(code,0x16b3d0-base)-base,128);row=((ec>>13)&31)*4
    body=bytearray(224);a,b=positions[row]*56,positions[row+1]*56
    struct.pack_into('<HHII',body,a,282,158,1234,5678);struct.pack_into('<I',body,a+16,0xcafef00d)
    body[a+20]=old_nature
    if egg:struct.pack_into('<I',body,b+0x34,1<<30)
    raw=struct.pack('<IHH',ec,0,sum(struct.unpack('<112H',body))&65535)+body
    party=struct.pack('<IBBHHH5H6x',0,50,0,0,0 if fainted else 120,150,100,101,102,103,104)
    uc.mem_write(boxed,raw);uc.mem_write(stats,party)
    word(u32(code,0x167e40-base)+0x14,0 if bad_manager else manager)
    word(u32(code,0x174ca4-base)+4,table);word(manager+8,cache)
    uc.mem_write(table+282*64,bytes([100]*6)+bytes(58))
    records=garc_members(blobs['RomFS/a/2/2/0']);packed=bytearray(359)
    for i,record in enumerate(records):
        pocket=1 if i in ITEMS else (struct.unpack_from('<H',record,8)[0]>>7)&15
        packed[i//2]|=pocket<<(4 if i%2==0 else 0)
    uc.mem_write(pockets,bytes(packed));word(u32(code,0x39fb40-base),pockets)
    word(main+8,work);word(main+0x4c,context);word(main+0x54,1);word(main+0x30,ram+0x2e00)
    word(work+0x30,inventory);word(work+0x34,ram+0x2f00)
    word(context,window);word(context+0x58,core);word(context+0x10,ram+0x2d00)
    uc.mem_write(context+0x4c,struct.pack('<H',item));word(window+8,ui)
    word(ui+4,array);word(array,ram+0x2600);word(array+4,ram+0x2700)
    for offset in (0x28,0x30,0x34):word(ui+offset,ram+0x2800+offset*16)
    if bad_ui:word(ui+0x28,0)
    word(inventory+0xb98,meds)
    uc.mem_write(meds,struct.pack('<HHHH',0 if missing else item,quantity,17,7))
    uc.reg_write(UC_ARM_REG_C1_C0_2,0xf<<20);uc.reg_write(UC_ARM_REG_FPEXC,0x40000000)
    saved={UC_ARM_REG_R4+i:0x11110000+i for i in range(8)}
    def invoke(entry,arg0,arg1=0):
        uc.reg_write(UC_ARM_REG_R0,arg0);uc.reg_write(UC_ARM_REG_R1,arg1)
        uc.reg_write(UC_ARM_REG_SP,stack);uc.reg_write(UC_ARM_REG_LR,stop)
        for r,v in saved.items():uc.reg_write(r,v)
        uc.emu_start(entry,stop,count=2000000)
        require(reg(UC_ARM_REG_PC)==stop,'Handler exceeded instruction budget')
        require(reg(UC_ARM_REG_SP)==stack and all(reg(r)==v for r,v in saved.items()),'Handler corrupted ABI')
        return reg(UC_ARM_REG_R0)
    invoke(0x1685f0,accessor)
    if unclosed:uc.mem_write(accessor+12,b'\0')
    before_box=bytes(uc.mem_read(boxed,232));before_stats=bytes(uc.mem_read(stats,28));before_count=count(item)
    events=[];pk_writes=[]
    stubs={0x430,0x438,0x440,0x468,0x5b8,0x2e0,0x2e8,0x2f0,0x9a88,
           0xbfb8,0xc1f0,0xbf54,0x1c8,0x6d0,0x660,0x280,0xa5e4,0xa4e0,0xa6bc}
    def hook(machine,pc,size,_):
        offset=pc-load
        if pc==0x434944:events.append(('native_remove',reg(UC_ARM_REG_R1),reg(UC_ARM_REG_R2)))
        if offset==0x976c:raise ValueError('Mint reached the native pre-action callback')
        if offset in stubs:
            if offset==0x2e8:require(reg(UC_ARM_REG_R2)==179,'Wrong appended message index')
            if offset==0x5b8:require(reg(UC_ARM_REG_R2)==core,'Message nicknames a different Pokemon')
            events.append(('contract',hex(offset)))
            ret(ram+0x2700 if offset==0x2e0 else 1 if offset==0x280 else 0)
    def write_hook(machine,access,address,size,value,_):
        if boxed<=address<boxed+232 or stats<=address<stats+28:
            require(count(item)==before_count-1,'Original PK changed before successful native consumption')
            pk_writes.append((address,size))
    uc.hook_add(UC_HOOK_CODE,hook);uc.hook_add(UC_HOOK_MEM_WRITE,write_hook)
    # The actual original medicine action enters through its four-call-site
    # eligibility check and our one action hook; native no-effect/cleanup runs.
    result=invoke(load+0x2b00,main)
    should_apply=not(egg or missing or bad_ui or bad_manager or unclosed or quantity==0 or old_nature==NATURES[ITEMS.index(item)])
    require(result==4,'Medicine action returned a different state')
    if should_apply:
        require(count(item)==before_count-1 and count(17)==7,'Consumption changed the wrong quantity/item')
        require(len([e for e in events if e[0]=='native_remove'])==1,'Consumption was repeated')
        require(('contract','0x2e8') in events and ('contract','0x9a88') in events,'Success message not emitted')
        require(len(pk_writes)==65,'Commit did not copy exactly boxed+party data')
        nature=NATURES[ITEMS.index(item)]
        require(invoke(0x4a0d40,core)==nature,'Native getter sees the wrong resulting nature')
        invoke(0x168660,accessor)
        expected=bytearray(raw);expected[8+a+20]=nature
        struct.pack_into('<H',expected,6,sum(struct.unpack('<112H',expected[8:]))&65535)
        require(bytes(uc.mem_read(boxed,232))==bytes(expected),'Nature commit changed other boxed data or checksum')
        expected_party=bytearray(party)
        for stat in range(5):
            value=105
            if nature//5!=nature%5:
                if stat==nature//5:value=value*110//100
                elif stat==nature%5:value=value*90//100
            struct.pack_into('<H',expected_party,12+stat*2,value)
        require(bytes(uc.mem_read(stats,28))==bytes(expected_party),'Native resulting stats or unchanged HP/status/level differ')
        invoke(0x1685f0,accessor)
    else:
        require(bytes(uc.mem_read(boxed,232))==before_box and bytes(uc.mem_read(stats,28))==before_stats,'Rejected use mutated original PK')
        require(count(item)==before_count and count(17)==7,'Rejected use consumed inventory')
        require(not pk_writes and ('contract','0x2e8') not in events,'Rejected use committed or emitted success')
    return {'item':item,'nature':NATURES[ITEMS.index(item)],'applied':should_apply,'quantity_before':before_count,
            'quantity_after':count(item),'events':events,'pk_preserved_on_rejection':not should_apply,
            'abi_preserved':True,'gameplay_verified':False}


def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('project',type=Path)
    parser.add_argument('--output',type=Path,required=True);parser.add_argument('--candidate-cro',type=Path)
    args=parser.parse_args();profiles=json.loads(Path(__file__).with_name('observed_profiles.json').read_text())
    blobs=read_project(args.project,profiles);report=analyze(blobs,profiles)
    candidate=args.candidate_cro.read_bytes() if args.candidate_cro else None
    cases=[]
    for item,nature in zip(ITEMS,NATURES):
        for options in ({},{'quantity':1},{'quantity':999},{'old_nature':nature},{'egg':True},
                        {'missing':True},{'quantity':0},{'bad_ui':True},{'bad_manager':True},{'unclosed':True},{'fainted':True}):
            cases.append(run(blobs,item,candidate=candidate,**options))
    cases.append(run(blobs,623,load=0xb00000,candidate=candidate))
    verify_unchanged(args.project,blobs)
    with args.output.open('x',encoding='utf-8') as output:
        json.dump({'title_id':report['title_id'],'cases':cases,'limits':__doc__},output,indent=2)
    print(report['title_id'],len(cases),'native Bag/PK/quantity cases; UI/item lookup contracts, no gameplay claim')


if __name__=='__main__':main()
