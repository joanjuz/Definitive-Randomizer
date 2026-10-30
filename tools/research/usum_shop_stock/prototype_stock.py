"""USUM ARM proof of concept. Emulation only: never installs into a ROM or save.

The flag IDs below belong ONLY to synthetic test memory. They are NOT an approved
reservation in a real save. No CLI argument enables writing source binaries.
"""
from pathlib import Path
import argparse,hashlib,json,struct,sys
parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--shop',required=True,type=Path)
parser.add_argument('--code',required=True,type=Path)
parser.add_argument('--crs',required=True,type=Path)
parser.add_argument('--output-directory',required=True,type=Path)
parser.add_argument('--allocation-plan',type=Path,help='CRO allocator plan from the isolated playable research helper')
parser.add_argument('--export-tested-image',action='store_true',help='Export ONLY into output directory, after all checks; requires allocated experimental mode')
parser.add_argument('--allow-known-expanded',action='store_true',help='Also emulate the exact fingerprint of the locally researched expanded Shop.cro; not a generic patch installer')
parser.add_argument('--experimental-storage',action='store_true',help='Use a tagged candidate save-block area ONLY in synthetic memory; not approved for real saves')
parser.add_argument('--dependencies',type=Path,help='Optional isolated Python dependency directory')
args=parser.parse_args()
if not __debug__:raise RuntimeError('Do not run this validation tool with Python optimization enabled')
if args.dependencies:sys.path.insert(0,str(args.dependencies))
from keystone import Ks,KS_ARCH_ARM,KS_MODE_ARM
from unicorn import Uc,UC_ARCH_ARM,UC_MODE_ARM,UC_HOOK_CODE
from unicorn.arm_const import *

ROOT=args.output_directory
ROOT.mkdir(parents=True,exist_ok=True)
SHOP,CODE,CRS=args.shop,args.code,args.crs
source=SHOP.read_bytes();code=CODE.read_bytes();crs=CRS.read_bytes()
EXPECTED='E7E8E9066B117485441949B9B15652623A1576C018C36E8BBB3BE361F52BDE38'
SOURCE_HASH=hashlib.sha256(source).hexdigest().upper()
EXPANDED='8BB08FBCD26B694CADC6ED6858616E57BE7AD51AB61A0181D30B570730A28D7A'
assert SOURCE_HASH==EXPECTED or (args.allow_known_expanded and SOURCE_HASH==EXPANDED),'Unresearched Shop.cro'
expanded=SOURCE_HASH==EXPANDED
CODE_HASH=hashlib.sha256(code).hexdigest().upper()
assert CODE_HASH=='B5388F7500D91BE01499A99CA007C98212068608ED7C83C43952E1D5148E9E09' or (args.allow_known_expanded and CODE_HASH=='D2E9B4D91D7B14BCEF0C34D869C1DA60EF85D98B1DF3C34CBC42E42F54CFBA27'),'Unresearched code.bin'
assert hashlib.sha256(crs).hexdigest().upper()=='B14DC9BC0FDB46ED85D422350048B76D7197FF90E14AA11AE9DC71483C494C91','Unresearched static.crs'
INPUT_HASH=SOURCE_HASH
plan=None
if args.allocation_plan:
    assert args.experimental_storage and args.allow_known_expanded,'Allocated mode requires experimental storage and exact known input fingerprints'
    plan=json.loads(args.allocation_plan.read_text(encoding='utf-8-sig'))
    assert plan['SourceSha256']==INPUT_HASH,'Plan source fingerprint mismatch'
    prepared=Path(plan['PreparedFile']).read_bytes()
    assert hashlib.sha256(prepared).hexdigest().upper()==plan['PreparedSha256'],'Prepared allocation image fingerprint mismatch'
    assert plan['GrantSize']>=0x700,'Insufficient allocation'
    source=prepared;SOURCE_HASH=plan['PreparedSha256']
assert not args.export_tested_image or plan is not None,'Export requires an allocator plan'
SHOP_A,SHOP_B=plan['FixtureShops'] if plan else (8,9)
BASE=0x1000000;MAIN=0x100000;CTX=0x2000000;PARAM=CTX+0x1000;ROWS=CTX+0x2000;FIELD=CTX+0x3000;STACK=0x3000000;SENTINEL=0x4000000
SHIFT=plan['AllocationBase']-0x4800 if plan else (0xf00 if expanded else 0)
STORE_CHECK=0x4d00+SHIFT;STORE_MARK=0x4e00+SHIFT
STORAGE_OFFSET=0xd04;STORAGE_MAGIC=0x44525331;RECIPE_ID=0x53544f43
PREFLIGHT=0x4800+SHIFT;GUARD=0x4900+SHIFT;MARK=0x4a00+SHIFT;LOOKUP=0x4b00+SHIFT;TRAMP=0x4c00+SHIFT
ks=Ks(KS_ARCH_ARM,KS_MODE_ARM)
def asm(text,address):return bytes(ks.asm(text,addr=address)[0])
def u32(data,at):return struct.unpack_from('<I',data,at)[0]
def exports():
    off,count=struct.unpack_from('<II',crs,0xd0);segments=u32(crs,0xc8)
    result={}
    for i in range(count):
        name,encoded=struct.unpack_from('<II',crs,off+i*8)
        segment=encoded&15;start=u32(crs,segments+segment*12)
        result[crs[name:crs.index(0,name)].decode()]=start+(encoded>>4)
    return result
symbols=exports()
SETFLAG=symbols['_ZN5Field9EventWork12SetEventFlagEt']
CHECKFLAG=symbols['_ZNK5Field9EventWork14CheckEventFlagEt']
assert SETFLAG==0x3a8a7c and CHECKFLAG==0x4bb1f0
# Signature checks supplement the exact CRO fingerprint.
assert u32(code,SETFLAG-MAIN)==0xe3510000
assert u32(code,CHECKFLAG-MAIN)==0xe3510000
assert source[0x276c:0x2770]==asm('push {r4-r8,lr}',0x276c)
assert source[0x15e4:0x15e8]==asm('ldr r0,[r4,#0x20]',0x15e4)
assert source[0x164c:0x1650]==asm('ldrsh r0,[r4,#0x82]',0x164c)
assert not any(source[PREFLIGHT:STORE_MARK+0x100]),'Prototype padding occupied'

payloads={
PREFLIGHT:f'''
    push {{r4-r8,lr}}
    mov r4,r0
    bl {LOOKUP}
    mov r5,r0
    cmp r5,#0
    beq original
    mov r1,r5
    ldr r0,[r4,#0x38]
    bl 0x378
    cmp r0,#0
    bne sold
    mov r1,#1
    strh r1,[r4,#0x84]
original:
    mov r0,r4
    bl {TRAMP}
    cmp r0,#0
    cmpeq r5,#0
    beq done
    cmp r0,#0
    moveq r1,#1
    strheq r1,[r4,#0x86]
done:
    pop {{r4-r8,pc}}
sold:
    mov r0,#3
    pop {{r4-r8,pc}}
''',
GUARD:f'''
    push {{r0-r3,ip,lr}}
    mov r0,r4
    bl {LOOKUP}
    cmp r0,#0
    beq resume
    mov r0,r4
    bl {PREFLIGHT}
    cmp r0,#0
    bne abort
resume:
    pop {{r0-r3,ip,lr}}
    ldr r0,[r4,#0x20]
    b 0x15e8
abort:
    pop {{r0-r3,ip,lr}}
    b 0x158c
''',
MARK:f'''
    push {{r0-r3,ip,lr}}
    mov r0,r4
    bl {LOOKUP}
    cmp r0,#0
    beq resume
    ldr r2,[sp]
    cmp r2,#0
    beq abort
    mov r1,r0
    ldr r0,[r4,#0x38]
    movw ip,#{SETFLAG&65535}
    movt ip,#{SETFLAG>>16}
    blx ip
resume:
    pop {{r0-r3,ip,lr}}
    ldrsh r0,[r4,#0x82]
    b 0x1650
abort:
    pop {{r0-r3,ip,lr}}
    b 0x158c
''',
LOOKUP:'''
    push {r4-r7,lr}
    sub sp,sp,#4
    mov r4,r0
    ldr r1,[r4,#0x20]
    ldrb r2,[r1]
    cmp r2,#0
    bne none
    ldr r5,[r1,#4]
    ldrsh r2,[r4,#0x82]
    cmp r2,#0
    blt none
    ldr r3,[r4,#0x70]
    cmp r2,r3
    bhs none
    ldr r3,[r4,#0x6c]
    add r2,r2,r2,lsl #1
    add r3,r3,r2,lsl #2
    ldrh r6,[r3]
    adr r7,table
    mov r2,#3
next:
    ldrh r0,[r7]
    cmp r0,r5
    ldrheq r0,[r7,#2]
    cmpeq r0,r6
    ldrheq r0,[r7,#4]
    beq found
    add r7,r7,#8
    subs r2,r2,#1
    bne next
none:
    mov r0,#0
found:
    add sp,sp,#4
    pop {r4-r7,pc}
    .align 2
table:
    .short 8,101,100,0
    .short 9,101,101,0
    .short 8,102,102,0
''',
TRAMP:'''
    push {r4-r8,lr}
    b 0x2770
'''
}
payloads[LOOKUP]=payloads[LOOKUP].replace('.short 8,101,100,0',f'.short {SHOP_A},101,100,0').replace('.short 9,101,101,0',f'.short {SHOP_B},101,101,0').replace('.short 8,102,102,0',f'.short {SHOP_A},102,102,0')
if args.experimental_storage:
    payloads[PREFLIGHT]=payloads[PREFLIGHT].replace('bl 0x378',f'bl {STORE_CHECK}')
    # Candidate only: offset 0xD00 in the serialized 0xE00 EventWork block.
    # The object has a four-byte vtable prefix. No native event flags are reused.
    payloads[STORE_CHECK]=f'''
        sub r1,r1,#100
        cmp r1,#3
        bhs unavailable
        ldr r2,[r0,#{STORAGE_OFFSET}]
        ldr r3,[r0,#{STORAGE_OFFSET+4}]
        ldr ip,[r0,#{STORAGE_OFFSET+8}]
        orr r2,r2,r3
        orrs r2,r2,ip
        beq available
        ldr r2,[r0,#{STORAGE_OFFSET}]
        movw r3,#{STORAGE_MAGIC&65535}
        movt r3,#{STORAGE_MAGIC>>16}
        cmp r2,r3
        bne unavailable
        ldr r2,[r0,#{STORAGE_OFFSET+4}]
        movw r3,#{RECIPE_ID&65535}
        movt r3,#{RECIPE_ID>>16}
        cmp r2,r3
        bne unavailable
        cmp ip,#7
        bhi unavailable
        mov r2,#1
        tst ip,r2,lsl r1
        bne unavailable
    available:
        mov r0,#0
        bx lr
    unavailable:
        mov r0,#1
        bx lr
    '''
    payloads[STORE_MARK]=f'''
        sub r1,r1,#100
        movw r2,#{STORAGE_MAGIC&65535}
        movt r2,#{STORAGE_MAGIC>>16}
        str r2,[r0,#{STORAGE_OFFSET}]
        movw r2,#{RECIPE_ID&65535}
        movt r2,#{RECIPE_ID>>16}
        str r2,[r0,#{STORAGE_OFFSET+4}]
        ldr r2,[r0,#{STORAGE_OFFSET+8}]
        mov r3,#1
        orr r2,r2,r3,lsl r1
        str r2,[r0,#{STORAGE_OFFSET+8}]
        bx lr
    '''
    payloads[MARK]=payloads[MARK].replace(
        f'movw ip,#{SETFLAG&65535}\n    movt ip,#{SETFLAG>>16}\n    blx ip',f'bl {STORE_MARK}')
image=bytearray(source);compiled={}
for address,text in payloads.items():
    blob=asm(text,address);assert len(blob)<=0x100,(hex(address),len(blob))
    image[address:address+len(blob)]=blob;compiled[address]=blob
for address,target in [(0x276c,PREFLIGHT),(0x15e4,GUARD),(0x164c,MARK)]:image[address:address+4]=asm(f'b {target}',address)

class Machine:
    def __init__(self,save=None):
        self.uc=Uc(UC_ARCH_ARM,UC_MODE_ARM)
        for address,size in [(BASE,0x10000),(MAIN,0x600000),(CTX,0x10000),(STACK,0x10000),(SENTINEL,0x1000)]:self.uc.mem_map(address,size)
        self.uc.mem_write(BASE,bytes(image));self.uc.mem_write(MAIN,code)
        self.uc.mem_write(FIELD,save if save is not None else bytes(0x1000))
        # Resolve internal jump-table/pointer relocations, as the loader would.
        segtable=u32(source,0xc8);segs=[u32(source,segtable+i*12) for i in range(5)]
        off,count=struct.unpack_from('<II',source,0x128)
        for i in range(count):
            encoded,kind,addend=struct.unpack_from('<III',source,off+i*12)
            write=BASE+segs[encoded&15]+(encoded>>4);segment=(kind>>8)&255
            if kind&255==2:self.put(write,BASE+segs[segment]+addend)
        # Resolve native flag thunk to real, unmodified code.bin implementation.
        self.put(BASE+0x37c,CHECKFLAG)
        self.bag={};self.fail_add=False;self.aborted=False;self.stubs={}
        off,count=struct.unpack_from('<II',source,0x100)
        for i in range(count):
            name,rel=struct.unpack_from('<II',source,off+i*8)
            encoded=u32(source,rel);thunk=BASE+0x180+(encoded>>4)-4
            self.stubs[thunk]=source[name:source.index(0,name)].decode()
        self.uc.hook_add(UC_HOOK_CODE,self.hook)
        self.context(SHOP_A,101)
    def put(self,at,value,fmt='<I'):self.uc.mem_write(at,struct.pack(fmt,value))
    def get(self,at,fmt='<I'):return struct.unpack(fmt,self.uc.mem_read(at,struct.calcsize(fmt)))[0]
    def context(self,shop,item,quantity=1,money=1000,mode=0,duplicate=False):
        self.put(CTX+0x20,PARAM);self.put(PARAM,mode);self.put(PARAM+4,shop)
        self.put(CTX+0x6c,ROWS);self.put(ROWS,item);self.put(ROWS+4,200)
        self.put(ROWS+12,item);self.put(ROWS+16,200)
        self.put(CTX+0x70,2 if duplicate else 1);self.put(CTX+0x82,0,'<h')
        self.put(CTX+0x84,quantity,'<h');self.put(CTX+0x86,0,'<h');self.put(CTX+0x74,money)
        self.put(CTX+0x38,FIELD);self.uc.reg_write(UC_ARM_REG_R4,CTX)
    def hook(self,uc,address,size,user):
        if address==SENTINEL:uc.emu_stop();return
        if address not in self.stubs or address==BASE+0x378:return
        name=self.stubs[address];r1=uc.reg_read(UC_ARM_REG_R1);r2=uc.reg_read(UC_ARM_REG_R2)
        if 'MyItem10GetItemNum' in name:uc.reg_write(UC_ARM_REG_R0,self.bag.get(r1,0))
        elif 'ITEM_CheckWazaMachine' in name:uc.reg_write(UC_ARM_REG_R0,0)
        elif 'MyItem3Add' in name:
            if self.fail_add:uc.reg_write(UC_ARM_REG_R0,0)
            else:self.bag[r1]=self.bag.get(r1,0)+r2;uc.reg_write(UC_ARM_REG_R0,1)
        elif name=='__aeabi_uidivmod':
            a=uc.reg_read(UC_ARM_REG_R0);b=r1
            uc.reg_write(UC_ARM_REG_R0,a//b if b else 0);uc.reg_write(UC_ARM_REG_R1,a%b if b else 0)
        # Other touched UI/diary helpers are no-op stubs, not tested game behavior.
        uc.reg_write(UC_ARM_REG_PC,uc.reg_read(UC_ARM_REG_LR))
    def run(self,at,stop=SENTINEL):
        self.uc.reg_write(UC_ARM_REG_SP,STACK+0xff00);self.uc.reg_write(UC_ARM_REG_LR,SENTINEL)
        self.uc.emu_start(BASE+at,stop,count=15000)
        return self.uc.reg_read(UC_ARM_REG_R0)
    def preflight(self):self.uc.reg_write(UC_ARM_REG_R0,CTX);return self.run(0x276c)
    def flags(self):return bytes(self.uc.mem_read(FIELD,0x1000))
    def confirm(self):
        # Execute the real confirmation path through guarded item delivery.
        self.uc.reg_write(UC_ARM_REG_R4,CTX);self.uc.reg_write(UC_ARM_REG_R6,0)
        stopped=[]
        def stop(uc,address,size,user):
            if address in (BASE+0x158c,BASE+0x1650):stopped.append(address);uc.emu_stop()
        handle=self.uc.hook_add(UC_HOOK_CODE,stop)
        try:self.run(0x15e4)
        finally:self.uc.hook_del(handle)
        assert stopped,'Confirmation escaped expected path'
        if stopped[-1]==BASE+0x158c:return False
        # Execute the unmodified cost calculation, omitting intervening UI updates.
        self.run(0x1694,BASE+0x16c0)
        return True

passed=[]
def check(ok,name):
    if not ok:raise AssertionError(name)
    passed.append(name);print('PASS',name)

m=Machine();before=m.flags();check(m.preflight()==0,'limited_offer_available')
check(m.get(CTX+0x86,'<h')==1,'quantity_selector_maximum_one')
check(m.flags()==before,'preview_does_not_consume_offer')
check(m.confirm(),'first_purchase_succeeds')
check(m.bag[101]==1 and m.get(CTX+0x74)==800,'one_item_and_exact_price')
check(m.preflight()==3,'second_purchase_reports_sold_out')
money=m.get(CTX+0x74);bag=dict(m.bag);check(not m.confirm(),'confirmation_rechecks_sold_out')
check(m.bag==bag and m.get(CTX+0x74)==money,'blocked_purchase_changes_neither_items_nor_money')
saved=m.flags();m.bag.clear();check(m.preflight()==3,'using_or_selling_item_does_not_restore_stock')
loaded=Machine(saved);check(loaded.preflight()==3,'flag_state_survives_synthetic_save_reload')
loaded.context(SHOP_B,101);check(loaded.preflight()==0 and loaded.confirm(),'same_item_other_shop_has_independent_stock')
check(loaded.preflight()==3,'other_shop_also_exhausts_after_one_purchase')
loaded.context(SHOP_A,102);check(loaded.preflight()==0 and loaded.confirm(),'different_item_same_shop_has_independent_stock')
check(loaded.preflight()==3,'different_item_same_shop_exhausts_independently')
loaded.context(SHOP_A,101,duplicate=True);loaded.put(CTX+0x82,1,'<h');check(loaded.preflight()==3,'duplicate_row_in_same_shop_shares_limit')

for name,money,bag_count,failed_add in [('insufficient_money',199,0,False),('full_bag',1000,999,False),('delivery_failure',1000,0,True)]:
    state=Machine();state.context(SHOP_A,101,money=money);state.bag[101]=bag_count;state.fail_add=failed_add
    flags=state.flags();old=dict(state.bag)
    check(not state.confirm(),name+'_blocks_delivery')
    check(state.flags()==flags and state.get(CTX+0x74)==money and state.bag==old,name+'_preserves_stock_items_and_money')

for quantity in [-32768,-1,0,2,99,32767]:
    state=Machine();state.context(SHOP_A,101,quantity=quantity)
    check(state.confirm() and state.bag[101]==1 and state.get(CTX+0x74)==800,'quantity_clamped_'+('minus_'+str(-quantity) if quantity<0 else str(quantity)))
state=Machine();state.context(SHOP_A,103,quantity=2);flags=state.flags()
check(state.preflight()==0 and state.get(CTX+0x86,'<h')>1,'unlisted_item_keeps_original_quantity_rules')
check(state.confirm() and state.bag[103]==2 and state.get(CTX+0x74)==600 and state.flags()==flags,'unlisted_item_keeps_original_transaction')
state=Machine();state.context(7,101,quantity=2);check(state.preflight()==0 and state.get(CTX+0x86,'<h')>1,'normal_progression_shop_unaffected')
state=Machine();state.context(SHOP_A,101,quantity=2,mode=1);flags=state.flags()
check(state.preflight()==0 and state.get(CTX+0x86,'<h')>1 and state.flags()==flags,'bp_mode_not_subject_to_regular_shop_stock')
# The actual cancel handler exits into the existing UI path before delivery.
state=Machine();flags=state.flags();state.uc.reg_write(UC_ARM_REG_R0,CTX);state.uc.reg_write(UC_ARM_REG_R1,1)
state.run(0x1560,BASE+0x158c)
check(state.flags()==flags and not state.bag and state.get(CTX+0x74)==1000,'cancel_does_not_consume_stock')
changed=[i for i,(a,b) in enumerate(zip(before,saved)) if a!=b]
check(all(STORAGE_OFFSET<=i<STORAGE_OFFSET+12 for i in changed) and bool(changed) if args.experimental_storage else changed==[0x7d4+(100>>3)],'only_candidate_storage_bytes_change' if args.experimental_storage else 'only_assigned_synthetic_flag_byte_changes')
if args.experimental_storage:
    # Execute native virtual accessors. This validates the serialization boundary,
    # NOT the real save pipeline, checksum, disk persistence or ownership of padding.
    state=Machine()
    state.uc.reg_write(UC_ARM_REG_R0,FIELD)
    state.uc.reg_write(UC_ARM_REG_SP,STACK+0xff00)
    state.uc.reg_write(UC_ARM_REG_LR,SENTINEL)
    state.uc.emu_start(0x3a8a38,SENTINEL,count=100)
    block_size=state.uc.reg_read(UC_ARM_REG_R0)
    check(block_size==0xe00,'native_eventwork_serialized_size_is_e00')
    state.uc.reg_write(UC_ARM_REG_R0,FIELD)
    state.uc.reg_write(UC_ARM_REG_LR,SENTINEL)
    state.uc.emu_start(0x3a91cc,SENTINEL,count=100)
    block_pointer=state.uc.reg_read(UC_ARM_REG_R0)
    check(block_pointer==FIELD+4,'native_eventwork_serialized_pointer_skips_vtable')
    # Unrelated bytes are nonzero to detect accidental clearing or copying.
    old=bytearray((i*17+3)&255 for i in range(0x1000))
    old[STORAGE_OFFSET:STORAGE_OFFSET+12]=bytes(12)
    state=Machine(bytes(old));check(state.confirm(),'tagged_candidate_first_purchase')
    after=state.flags()
    check(after[:STORAGE_OFFSET]==old[:STORAGE_OFFSET] and after[STORAGE_OFFSET+12:]==old[STORAGE_OFFSET+12:],'candidate_preserves_all_unowned_memory')
    block=bytes(state.uc.mem_read(block_pointer,block_size))
    restored=bytearray(old);restored[4:4+block_size]=block
    state=Machine(bytes(restored))
    check(state.preflight()==3,'native_block_boundary_roundtrip_keeps_sold_out')
    state.context(SHOP_B,101)
    check(state.preflight()==0,'native_block_roundtrip_other_shop_available')
    for name,header in [
        ('foreign_magic',struct.pack('<III',0x12345678,RECIPE_ID,0)),
        ('different_recipe',struct.pack('<III',STORAGE_MAGIC,RECIPE_ID+1,0)),
        ('invalid_bitmap',struct.pack('<III',STORAGE_MAGIC,RECIPE_ID,8)),
        ('partial_header',struct.pack('<III',0,0,1)),
    ]:
        memory=bytearray(0x1000);memory[STORAGE_OFFSET:STORAGE_OFFSET+12]=header
        state=Machine(bytes(memory));oldflags=state.flags()
        check(state.preflight()==3 and not state.confirm() and state.flags()==oldflags and not state.bag and state.get(CTX+0x74)==1000,name+'_fails_closed_without_writes_or_charge')
changed_offsets={i for i,(a,b) in enumerate(zip(source,image)) if a!=b}
allowed_offsets=set(range(PREFLIGHT,STORE_MARK+0x100))|{i for a in (0x276c,0x15e4,0x164c) for i in range(a,a+4)}
check(len(source)==len(image) and changed_offsets<=allowed_offsets,'in_memory_patch_preserves_all_unowned_cro_bytes')
check(hashlib.sha256(SHOP.read_bytes()).hexdigest().upper()==INPUT_HASH,'original_shop_file_unchanged')
report={'Status':'EMULATION_ONLY_NOT_INSTALLABLE','StorageMode':'TAGGED_CANDIDATE_NOT_RESERVED' if args.experimental_storage else 'SYNTHETIC_NATIVE_FLAGS','CandidateSerializedOffset':0xd00 if args.experimental_storage else None,'InputShopSha256':INPUT_HASH,'ShopSha256':SOURCE_HASH,'ShopVariant':'KNOWN_EXPANDED' if expanded else 'ORIGINAL','CodeSha256':hashlib.sha256(code).hexdigest().upper(),'CRSSha256':hashlib.sha256(crs).hexdigest().upper(),'Checks':passed,'NativeCodeExecuted':['Shop.cro preflight','Shop.cro confirmation through item-delivery hook','Shop.cro native cost calculation',*(['native EventWork virtual serialized-size and data-pointer accessors','experimental tagged ARM storage accessors'] if args.experimental_storage else ['code.bin Field::EventWork::CheckEventFlag','code.bin Field::EventWork::SetEventFlag'])],'Stubbed':['inventory functions','diary and UI helpers','integer division'],'SaveReload':'Synthetic EventWork memory serialization, NOT a real game save or emulator session','Unresolved':['No approved real-save storage reservation','No in-game UI/save/CRC test','No updater or Global/Batch integration'],'PayloadSizes':{hex(k):len(v) for k,v in compiled.items()}}
(ROOT/'prototype-results.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
(ROOT/'prototype-hooks.asm').write_text('\n\n'.join(f'; FILE OFFSET {a:#x}\n{s}' for a,s in payloads.items()),encoding='utf-8')
if args.export_tested_image:
    with (ROOT/'tested-shop-image.bin').open('xb') as exported:exported.write(image)
print(f'Completed {len(passed)} ARM-emulated checks. Not installable; source ROM/save untouched.')
