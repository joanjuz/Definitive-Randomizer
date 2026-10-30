"""Execute the actual owned ARM64 module with Unicorn.
Game function bodies and SVC memory-query responses are controlled fixtures, NOT gameplay.
"""
import argparse,struct
from pathlib import Path
from unicorn import Uc,UC_ARCH_ARM64,UC_MODE_ARM,UC_HOOK_CODE,UC_HOOK_INTR
from unicorn.arm64_const import *

def u32(b,p):return struct.unpack_from("<I",b,p)[0]
def symbols(elf):
    off=struct.unpack_from("<Q",elf,40)[0];size,count=struct.unpack_from("<HH",elf,58);result={}
    for i in range(count):
        sh=struct.unpack_from("<II4QII2Q",elf,off+i*size)
        if sh[1]!=2:continue
        st=struct.unpack_from("<II4QII2Q",elf,off+sh[6]*size);strings=elf[st[4]:st[4]+st[5]]
        for j in range(sh[5]//sh[9]):
            name,info,other,index,value,n=struct.unpack_from("<IBBHQQ",elf,sh[4]+j*sh[9])
            if name:result[strings[name:strings.find(b"\0",name)].decode()]=value
    return result

def run(path):
    elf=path.read_bytes();sym=symbols(elf);module=0x20000000;main=0x10000000;obj=0x30000000;out=0x30010000;spec=0x30020000;stack=0x40000000;stop=0x41000000
    uc=Uc(UC_ARCH_ARM64,UC_MODE_ARM);uc.mem_map(main,0x2A00000);uc.mem_map(module,0x20000);uc.mem_map(obj,0x30000);uc.mem_map(stack,0x100000);uc.mem_map(stop,0x1000)
    ph=struct.unpack_from("<Q",elf,32)[0];ps,pc=struct.unpack_from("<HH",elf,54)
    for i in range(pc):
        typ,flags,offset,va,pa,fs,ms,align=struct.unpack_from("<II6Q",elf,ph+i*ps)
        if typ==1:uc.mem_write(module+va,elf[offset:offset+fs])
        if typ==2:
            tags=[struct.unpack_from("<QQ",elf,p) for p in range(offset,offset+fs,16)]
            assert (12,sym['dr_module_init']) in tags,"DT_INIT must call our initializer"
    # Simulate RTLD R_AARCH64_RELATIVE relocations for the module.
    sho=struct.unpack_from('<Q',elf,40)[0];shs,shc=struct.unpack_from('<HH',elf,58)
    for i in range(shc):
        sec=struct.unpack_from('<II4QII2Q',elf,sho+i*shs)
        if sec[1]!=4:continue
        for j in range(sec[5]//sec[9]):
            where,info,addend=struct.unpack_from('<QQq',elf,sec[4]+j*sec[9]);assert info&0xffffffff==1027,'Unexpected runtime relocation'
            uc.mem_write(module+where,struct.pack('<Q',module+addend))
    cfg=module+sym['config'];uc.mem_write(cfg+20,struct.pack('<III',123,6,0));uc.mem_write(cfg+32,struct.pack('<6H',1,2,3,281,810,898))
    for at,word in [(0x766D64,0xB9402A60),(0x766EBC,0xB9402A60),(0xEA2868,0xB9404EA8),(0xD609C4,0x79014288)]:uc.mem_write(main+at,struct.pack('<I',word))
    entries=[0xD329D0,0xD30D30,0xD60920,0xD311C0];first=[0xA9BC5FF8,0xA9BD57F6,0xD10183FF,0xD10603FF]
    def branch(a,b):return 0x14000000|(((b-a)//4)&0x3ffffff)
    for i,entry in enumerate(entries):
        stub=0x1900F30+24*i;slot=0x2985000+8*i;pages=(slot>>12)-(stub>>12);adrp=0x90000010|((pages&3)<<29)|(((pages>>2)&0x7ffff)<<5)
        data=struct.pack('<6I',adrp,0xF9400210|((slot&0xfff)//8<<10),0xB4000050,0xD61F0200,first[i],branch(stub+20,entry+4))
        uc.mem_write(main+stub,data);uc.mem_write(main+entry,struct.pack('<I',branch(entry,stub)))
    diagnostics=[]
    def svc(uc,number,data):
        assert number==2,"Unexpected ARM64 exception"
        instruction=u32(uc.mem_read(uc.reg_read(UC_ARM64_REG_PC)-4,4),0)
        if instruction==0xD40004E1:
            diagnostics.append(bytes(uc.mem_read(uc.reg_read(UC_ARM64_REG_X0),uc.reg_read(UC_ARM64_REG_X1))).decode());uc.reg_write(UC_ARM64_REG_X0,0);return
        assert instruction==0xD40000C1,"Only SVC6 and diagnostic SVC27 are allowed"
        address=uc.reg_read(UC_ARM64_REG_X2);pointer=uc.reg_read(UC_ARM64_REG_X0)
        if address==0:info=(0,main,0,0,0,0,0,0)
        elif address==main:info=(main,0x1901000,3,0,5,0,0,0)
        else:raise AssertionError("Unexpected query")
        uc.mem_write(pointer,struct.pack('<QQ6I',*info));uc.reg_write(UC_ARM64_REG_X0,0);uc.reg_write(UC_ARM64_REG_X1,0)
    uc.hook_add(UC_HOOK_INTR,svc)
    return_gprs={};return_q={};original_calls=[0,0,0,0]
    def original(uc,address,size,data):
        for i,entry in enumerate(entries):
            if address!=main+entry+4:continue
            original_calls[i]+=1
            if i==2:
                target=struct.unpack('<Q',uc.mem_read(uc.reg_read(UC_ARM64_REG_X8),8))[0];source=uc.mem_read(uc.reg_read(UC_ARM64_REG_X0)+0x8A8,88)
                value=bytearray([0xA5]*0x120);struct.pack_into('<HHH',value,0xA0,u32(source,0),struct.unpack_from('<H',source,4)[0],source[6]);uc.mem_write(target,bytes(value))
            uc.reg_write(UC_ARM64_REG_SP,uc.reg_read(UC_ARM64_REG_SP)+[64,48,96,384][i])
            for r in range(19):v=0xCAFE0000+r;uc.reg_write(UC_ARM64_REG_X0+r,v);return_gprs[r]=v
            for r in range(8):v=int.from_bytes(bytes([r+1])*16,'little');uc.reg_write(UC_ARM64_REG_Q0+r,v);return_q[r]=v
            uc.reg_write(UC_ARM64_REG_NZCV,0x60000000);uc.reg_write(UC_ARM64_REG_PC,uc.reg_read(UC_ARM64_REG_LR));return
    uc.hook_add(UC_HOOK_CODE,original)
    def call(address,x0=0,x1=0,x8=0):
        uc.reg_write(UC_ARM64_REG_SP,stack+0xFF000);uc.reg_write(UC_ARM64_REG_LR,stop);uc.reg_write(UC_ARM64_REG_X0,x0);uc.reg_write(UC_ARM64_REG_X1,x1);uc.reg_write(UC_ARM64_REG_X8,x8)
        uc.emu_start(address,stop,count=2000000)
        assert uc.reg_read(UC_ARM64_REG_PC)==stop,"Function failed to return"
        assert uc.reg_read(UC_ARM64_REG_SP)==stack+0xFF000,"Stack pointer must be restored"
    checks=[]
    def check(ok,name):
        assert ok,name;checks.append(name);print('PASS '+name)
    handle=obj+0x21000
    handle_bytes=struct.pack('<Q',out)+bytes([0x5A])*0x118
    uc.mem_write(handle,handle_bytes)
    raw=bytearray(88);struct.pack_into('<IHB',raw,0,281,0,25);raw[80]=17;uc.mem_write(spec,bytes(raw));uc.mem_write(obj+0x8A8,bytes(raw));uc.mem_write(obj+0x8A0,struct.pack('<II',1,0))
    call(main+entries[2],obj,0,handle)
    check(struct.unpack('<H',uc.mem_read(out+0xA0,2))[0]==281,'Arm64_ZeroCallbackSlotsFallBackToOriginalGameFunction')
    call(module+sym['dr_module_init'])
    check([struct.unpack('<Q',uc.mem_read(main+0x2985000+i*8,8))[0] for i in range(4)]==[module+sym[n] for n in ['dr_basic_entry','dr_gimmick_entry','dr_battle_entry','dr_finalized_entry']],'Arm64_InitializerFindsMainAndInstallsOnlyOwnedPointerSlots')
    check('DRSurprise v3: initializer entered' in diagnostics and 'DRSurprise v3: main matched, installing callbacks' in diagnostics,'Arm64_DiagnosticsDistinguishInitializationAndInstallation')
    check('DRSurprise v3: RX type=00000003 size=0000000001901000' in diagnostics,'Arm64_DiagnosticsReportActualMemoryTypeAndSize')
    original_object=bytes(uc.mem_read(obj,0x900));call(main+entries[0],0,spec)
    check(original_calls[0]==1,'Arm64_BasicWrapperCallsOriginalThroughReplayedPrologue')
    call(main+entries[2],obj,0,handle);value=bytes(uc.mem_read(out,0x120));chosen=struct.unpack_from('<H',value,0xA0)[0]
    check(chosen in [1,2,3,810,898],'Arm64_ActualNativeAdapterChangesOnlyApprovedVisibleBattle')
    check(bytes(uc.mem_read(handle,len(handle_bytes)))==handle_bytes,'Arm64_BattleDestinationHandleRemainsUnchanged')
    check(bytes(uc.mem_read(obj,0x900))==original_object,'Arm64_VisibleMapObjectRemainsByteIdentical')
    check(all(uc.reg_read(UC_ARM64_REG_X0+r)==v for r,v in return_gprs.items()) and all(uc.reg_read(UC_ARM64_REG_Q0+r)==v for r,v in return_q.items()) and uc.reg_read(UC_ARM64_REG_NZCV)==0x60000000,'Arm64_WrapperPreservesOriginalReturnRegistersSimdAndFlags')
    check(struct.unpack_from('<HH',value,0xAA)==(255,254) and value[0xD0:0xD8]==bytes(8),'Arm64_BuilderInputsUseNewGenderAbilityAndLevelLearnset')
    call(main+entries[2],obj,0,handle)
    check(struct.unpack('<H',uc.mem_read(out+0xA0,2))[0]==chosen,'Arm64_RepeatedBattleCopyKeepsSelectedSpecies')
    call(main+entries[1],0,spec);call(main+entries[2],obj,0,handle)
    check(struct.unpack('<H',uc.mem_read(out+0xA0,2))[0]==281,'Arm64_GimmickWrapperRevokesEligibilityAndPreservesOriginal')
    check('DRSurprise v3: basic spawn reached' in diagnostics and 'DRSurprise v3: battle species changed' in diagnostics and 'DRSurprise v3: battle reached but eligibility rejected' in diagnostics,'Arm64_DiagnosticsDistinguishSpawnBattleAndBypass')
    call(main+entries[0],0,spec)
    finalized=bytearray(uc.mem_read(spec,88));struct.pack_into('<IIIII',finalized,8,2,3,1,1,2);finalized[0x4B]=1;struct.pack_into('<I',finalized,0x50,0x05C2D106)
    uc.mem_write(spec,bytes(finalized));uc.mem_write(obj+0x8A8,bytes(finalized))
    call(main+entries[2],obj,0,handle)
    check(struct.unpack('<H',uc.mem_read(out+0xA0,2))[0]==281,'Arm64_UnfinalizedApprovalRejectsChangedSpec')
    call(main+entries[3],0,spec);call(main+entries[2],obj,0,handle)
    check(struct.unpack('<H',uc.mem_read(out+0xA0,2))[0]!=281 and original_calls[3]==1,'Arm64_FinalizerRefreshesOnlySameApprovedSource')
    call(main+entries[1],0,spec);call(main+entries[3],0,spec);call(main+entries[2],obj,0,handle)
    check(struct.unpack('<H',uc.mem_read(out+0xA0,2))[0]==281,'Arm64_FinalizerCannotReviveRevokedGimmick')
    print(f'Completed {len(checks)} ARM64 runtime checks; original game bodies are fixtures, NOT gameplay.')

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('elf',type=Path);run(p.parse_args().elf)
