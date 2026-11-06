"""Execute the actual Core-generated, relocated ARM payload with controlled native calls.

Inputs are synthetic CROs emitted by --usum-mints-arm-fixtures. No ROM required.
This proves the handler/eligibility/dispatch contract, not a real save or emulator session.
"""
import argparse
import struct
from pathlib import Path
from unicorn import Uc, UC_ARCH_ARM, UC_MODE_ARM, UC_HOOK_CODE
from unicorn.arm_const import UC_ARM_REG_R0, UC_ARM_REG_R1, UC_ARM_REG_R2, UC_ARM_REG_R3, UC_ARM_REG_R4, UC_ARM_REG_R5, UC_ARM_REG_R6, UC_ARM_REG_R7, UC_ARM_REG_R8, UC_ARM_REG_R9, UC_ARM_REG_R12, UC_ARM_REG_LR, UC_ARM_REG_PC, UC_ARM_REG_SP

BAG, MEMORY, STACK = 0x1000000, 0x2000000, 0x201f000
POKEMON, CONTEXT, BLOCK = MEMORY+0x100, MEMORY+0x1000, MEMORY+0x2000
STOP = BAG+0x11000
MINTS = list(zip([113,114,115,123,124,125,126,127,128,129,130,131,132,133,426,427,622,837,838,839,840], [1,3,4,2,5,8,9,7,15,16,19,17,20,21,23,22,10,11,13,14,12]))

def run(binary, moon, mode, item=113, nature=3, egg=False):
    marker=binary.index(b'DGMINT1\0'); offset=marker-0x3c0
    machine=Uc(UC_ARCH_ARM, UC_MODE_ARM)
    machine.mem_map(BAG, 0x20000); machine.mem_write(BAG, binary)
    machine.mem_map(MEMORY, 0x20000)
    machine.mem_map(0x300000, 0x200000)
    machine.mem_write(BLOCK, bytes((i*13+3)%256 for i in range(0x100)))
    machine.mem_write(BLOCK+0x14, bytes([nature]))
    machine.mem_write(POKEMON+0xc, struct.pack('<I',BLOCK))
    machine.reg_write(UC_ARM_REG_SP,STACK);machine.reg_write(UC_ARM_REG_LR,STOP)
    counters={'begin':0,'end':0,'derive':[],'store':[],'sound':0,'message':[],'route':None}
    shift=8 if moon else 0
    getter, begin, block, end, derive, store = 0x4ae0e0+shift,0x32258c,0x4ad588+shift,0x322514,0x4aea1c+shift,0x324d84
    before=bytes(machine.mem_read(BLOCK,0x100))
    def r(reg):return machine.reg_read(reg)
    def ret(value=0):machine.reg_write(UC_ARM_REG_R0,value);machine.reg_write(UC_ARM_REG_PC,r(UC_ARM_REG_LR))
    def hook(uc,address,size,_):
        if address==STOP:uc.emu_stop()
        elif address==BAG+0x700:
            assert r(UC_ARM_REG_R0)==POKEMON and r(UC_ARM_REG_R1)==2
            ret(int(egg))
        elif address==getter:
            assert r(UC_ARM_REG_R0)==POKEMON
            ret(uc.mem_read(BLOCK+0x14,1)[0])
        elif address==begin:
            assert r(UC_ARM_REG_R0)==BLOCK
            counters['begin']+=1;ret()
        elif address==block:
            assert r(UC_ARM_REG_R0)==BLOCK and r(UC_ARM_REG_R1)==0
            ret(BLOCK)
        elif address==end:
            assert r(UC_ARM_REG_R0)==BLOCK
            counters['end']+=1;ret()
        elif address==derive:
            assert r(UC_ARM_REG_R0)==POKEMON
            counters['derive'].append(r(UC_ARM_REG_R1));ret(100+r(UC_ARM_REG_R1))
        elif address==store:
            assert r(UC_ARM_REG_R0)==POKEMON
            counters['store'].append((r(UC_ARM_REG_R1),r(UC_ARM_REG_R2)));ret()
        elif address==BAG+0x798:
            counters['route']='native-recover';ret(0x55)
        elif address==BAG+0x210:
            assert r(UC_ARM_REG_R0)==0x50021
            counters['sound']+=1;ret()
        elif address==BAG+0x80a4:
            counters['message'].append(r(UC_ARM_REG_R1));ret()
        elif address in [BAG+0xc85c,BAG+0xc8e4]:
            counters['route']='stock-success' if address==BAG+0xc85c else 'stock-no-effect';uc.emu_stop()
    machine.hook_add(UC_HOOK_CODE,hook)
    if mode=='apply':
        machine.reg_write(UC_ARM_REG_R0,POKEMON);machine.reg_write(UC_ARM_REG_R1,item)
        start=BAG+offset+0x180
    elif mode=='recover':
        machine.reg_write(UC_ARM_REG_R0,POKEMON);machine.reg_write(UC_ARM_REG_R1,item)
        start=BAG+offset
    else:
        machine.reg_write(UC_ARM_REG_R4,CONTEXT);machine.reg_write(UC_ARM_REG_R5,item)
        machine.reg_write(UC_ARM_REG_R7,0)
        machine.mem_write(STACK+0x44,struct.pack('<I',POKEMON))
        start=BAG+offset+0x80
    machine.emu_start(start,STOP+4,count=3000)
    after=bytes(machine.mem_read(BLOCK,0x100))
    assert before[:0x14]==after[:0x14] and before[0x15:]==after[0x15:], 'Other PK fields changed'
    assert machine.reg_read(UC_ARM_REG_SP)==STACK, 'Stack imbalance'
    return machine.reg_read(UC_ARM_REG_R0),after[0x14],counters

def main():
    parser=argparse.ArgumentParser();parser.add_argument('fixtures',type=Path);args=parser.parse_args()
    checks=0
    for moon in [False,True]:
        binary=(args.fixtures/('moon.bin' if moon else 'sun.bin')).read_bytes()
        for item,nature in MINTS:
            _,actual,c=run(binary,moon,'apply',nature,nature=(nature+1)%25)
            assert actual==nature and c['begin']==c['end']==1 and c['derive']==[1,2,3,4,5] and c['store']==[(i,100+i) for i in range(1,6)]
            checks+=1
            for original,egg in [(nature,False),((nature+1)%25,True)]:
                result,actual,c=run(binary,moon,'apply',nature,nature=original,egg=egg)
                assert result==0 and actual==original and c['begin']==c['end']==0 and not c['store'];checks+=1
            result,actual,c=run(binary,moon,'recover',item,nature=(nature+1)%25)
            assert result==1 and actual==(nature+1)%25 and not c['begin'];checks+=1
            for original,egg in [(nature,False),((nature+1)%25,True)]:
                result,actual,c=run(binary,moon,'recover',item,nature=original,egg=egg)
                assert result==0 and actual==original and not c['begin'];checks+=1
            _,actual,c=run(binary,moon,'dispatch',item,nature=(nature+1)%25)
            assert actual==nature and c['route']=='stock-success' and c['sound']==1 and c['message']==[0x19f];checks+=1
            _,actual,c=run(binary,moon,'dispatch',item,nature=nature)
            assert actual==nature and c['route']=='stock-no-effect' and c['sound']==0 and not c['message'];checks+=1
        result,_,c=run(binary,moon,'recover',1)
        assert result==0x55 and c['route']=='native-recover';checks+=1
        result,actual,c=run(binary,moon,'apply',25)
        assert result==0 and actual==3 and not c['begin'];checks+=1
        print('PASS MintRuntimeSemantics', 'UM' if moon else 'US')
    print(f'Passed: {checks}, Failed: 0, Skipped: 0 (controlled ARM dependencies; gameplay/save persistence remains manual)')

if __name__=='__main__':main()
