"""Memory-permission audit of the ARM code that the randomizer injects into a Gen7 game.

Compares an original extraction with a patched one (copies; nothing is written), finds the code that changed in
code.bin and in every CRO, and checks it against the permissions the 3DS gives each region at runtime:
  code.bin  .text RX, .rodata R, .data/.bss RW (sizes from the exheader, rounded to pages);
  CRO       .text RX, .rodata and header/tables R, .data/.bss RW (in the separate buffer ldr:ro gives the module).
Emulators such as Citra do not enforce these permissions, so a store into .text works there and faults on a console.

Two passes:
  static   follows each payload path with constant propagation (literals, MOVW/MOVT, ADR, relocations) and reports
           every store whose address is known, and every branch whose target is known;
  dynamic  runs each payload in Unicorn with the real permissions; calls outside the payload return a pointer into a
           synthetic RW heap, so the payload's own memory accesses are what gets exercised.
Exit code 1 when a violation is found.
"""
from pathlib import Path
import argparse,json,struct,sys

PAGE=0x1000
def up(v,a=PAGE):return (v+a-1)&~(a-1)
u32=lambda b,o:struct.unpack_from('<I',b,o)[0]

class Region:
    def __init__(s,module,name,start,end,perm):s.module,s.name,s.start,s.end,s.perm=module,name,start,end,perm
    def __contains__(s,a):return s.start<=a<s.end
    def label(s):return f'{s.module}:{s.name}'

class Module:
    def __init__(s,name):
        s.name=name;s.maps=[]      # (va, bytes, perm) to map in Unicorn
        s.regions=[];s.changed=[]  # changed: (start,end,region-name)
        s.relocs={}                # location va -> target va (internal relocations)
        s.imports=set()            # location va of import relocations (unknown at load time)

# ---------------------------------------------------------------- loading
def find(root,*names):
    for n in names:
        p=root/n
        if p.exists():return p
    return None

def exheader_layout(path,size):
    if path is None:
        return None
    b=path.read_bytes()
    text=struct.unpack_from('<III',b,0x10);ro=struct.unpack_from('<III',b,0x20);data=struct.unpack_from('<III',b,0x30)
    return dict(text=(text[0],text[1]*PAGE,text[2]),ro=(ro[0],ro[1]*PAGE,ro[2]),data=(data[0],data[1]*PAGE,data[2]),bss=u32(b,0x3C))

def load_code(original_root,patched_root):
    paths=['ExeFS/.code.bin','ExeFS/code.bin','ExtractedExeFS/code.bin','ExtractedExeFS/.code.bin']
    po,pp=find(original_root,*paths),find(patched_root,*paths)
    if po is None or pp is None:raise SystemExit('code.bin not found')
    o,p=po.read_bytes(),pp.read_bytes()
    eh=['exheader.bin','DecryptedExHeader.bin','ExtractedExHeader.bin']
    eo,ep=find(original_root,*eh),find(patched_root,*eh)
    if eo and ep:
        ido,idp=u32(eo.read_bytes(),0x200)|u32(eo.read_bytes(),0x204)<<32,u32(ep.read_bytes(),0x200)|u32(ep.read_bytes(),0x204)<<32
        if ido!=idp:raise SystemExit(f'different games: original {ido:016X}, patched {idp:016X}')
    lay=exheader_layout(ep or eo,len(p))
    if lay is None:raise SystemExit('exheader not found')
    t,r,d=lay['text'],lay['ro'],lay['data']
    if len(p)!=t[1]+r[1]+d[1] or len(o)!=len(p):raise SystemExit(f'code.bin looks compressed or has a different layout ({len(o):#x}/{len(p):#x})')
    m=Module('code.bin')
    rw_end=up(d[0]+d[2]+lay['bss'])
    m.regions=[Region('code.bin','.text',t[0],t[0]+t[1],'rx'),Region('code.bin','.rodata',r[0],r[0]+r[1],'r'),Region('code.bin','.data/.bss',d[0],rw_end,'rw')]
    m.maps=[(t[0],p[:t[1]],'rx'),(r[0],p[t[1]:t[1]+r[1]],'r'),(d[0],p[t[1]+r[1]:]+bytes(rw_end-d[0]-d[1]),'rw')]
    file_base=[(t[0],0,t[1],'.text'),(r[0],t[1],r[1],'.rodata'),(d[0],t[1]+r[1],d[1],'.data')]
    for va,off,size,name in file_base:
        m.changed+=[(va+a-off,va+b-off,name) for a,b in runs(o,p,off,off+size)]
    m.layout=lay
    return m

def runs(a,b,start,end):
    out=[];i=start
    while i<end:
        if i<len(a) and i<len(b) and a[i]==b[i]:i+=1;continue
        j=i
        while j<end and not(j<len(a) and j<len(b) and a[j]==b[j]):j+=1
        out.append((i,j));i=j
    return out

SEG_NAMES={0:'.text',1:'.rodata',2:'.data',3:'.bss'}

def changed_words(o,p):
    """Changed byte ranges of p against o. When the segment grew in the middle (an inserted instruction moves the
    rest of the code), the comparison aligns the 32-bit words of both versions, so moved code is not reported."""
    straight=runs(o,p,0,len(p))
    if len(o)==len(p) or sum(b-a for a,b in straight)<len(p)//4:return straight
    import difflib
    ow=[o[i:i+4] for i in range(0,len(o)-len(o)%4,4)];pw=[p[i:i+4] for i in range(0,len(p)-len(p)%4,4)]
    out=[]
    for tag,i1,i2,j1,j2 in difflib.SequenceMatcher(None,ow,pw,autojunk=False).get_opcodes():
        if tag in('replace','insert') and j2>j1:out.append((j1*4,j2*4))
    if len(p)%4:out.append((len(p)-len(p)%4,len(p)))
    return out
def load_cro(name,o,p,base):
    """Maps the patched CRO at base (file image) and base+0x800000 (data/bss buffer), applies internal relocations."""
    if p[0x80:0x84]!=b'CRO0':return None
    m=Module(name)
    def segments(b):
        off,n=u32(b,0xC8),u32(b,0xCC)
        return [(u32(b,off+i*12),u32(b,off+i*12+4),u32(b,off+i*12+8)) for i in range(n)]
    ps,os_=segments(p),segments(o)
    data_buf=base+0x800000
    seg_va=[]
    data_size=0
    for off,size,kind in ps:
        if kind==2:data_size=size
    for off,size,kind in ps:
        if kind==0 or kind==1:seg_va.append(base+off)
        elif kind==2:seg_va.append(data_buf)
        elif kind==3:seg_va.append(data_buf+up(data_size,8))
        else:seg_va.append(None)
    image=bytearray(p)+bytearray(up(len(p))-len(p))
    rw=bytearray(up(max(1,up(data_size,8)+u32(p,0x94))))
    for (off,size,kind),va in zip(ps,seg_va):
        if kind==2:rw[:size]=p[off:off+size]
    # internal relocations: location (segment index | offset<<4), type | target segment<<8, addend
    roff,rn=u32(p,0x128),u32(p,0x12C)
    def write(va,value):
        if base<=va<base+len(image):struct.pack_into('<I',image,va-base,value&0xffffffff)
        elif data_buf<=va<data_buf+len(rw):struct.pack_into('<I',rw,va-data_buf,value&0xffffffff)
    for i in range(rn):
        loc,kind,addend=struct.unpack_from('<III',p,roff+i*12)
        si,lo=loc&15,loc>>4;ti=(kind>>8)&255;typ=kind&255
        if si>=len(seg_va) or ti>=len(seg_va) or seg_va[si] is None or seg_va[ti] is None:continue
        at=seg_va[si]+lo;target=seg_va[ti]+addend
        if typ==2:write(at,target);m.relocs[at]=target
        elif typ==3:write(at,target-at);m.relocs[at]=target
    m.maps=[(base,bytes(image),'r'),(data_buf,bytes(rw),'rw')]
    # ldr:ro maps the header's code region (.text and .rodata, page-rounded) RX; the rest of the image stays read-only
    # and .data/.bss live in the separate RW buffer.
    code_start,code_end=base+(u32(p,0xB0)&~(PAGE-1)),base+up(u32(p,0xB0)+u32(p,0xB4))
    m.regions=[Region(name,'code region (.text/.rodata)',code_start,code_end,'rx')]
    m.regions+=[Region(name,'.text (declared)',seg_va[i],seg_va[i]+size,'rx') for i,(off,size,kind) in enumerate(ps) if kind==0 and size]
    m.regions+=[Region(name,'header and tables',base,base+len(image),'r'),Region(name,'.data/.bss',data_buf,data_buf+len(rw),'rw')]
    m.code_region=(code_start,code_end)
    # .text is compared up to the next segment: the patchers also place code in the gap after the declared size.
    def window(segs,blob,i):
        off,size,kind=segs[i]
        if kind!=0:return off,size
        later=[s[0] for s in segs if s[2] in(1,2) and s[0]>off and s[1]]
        return off,(min(later) if later else u32(blob,0xB0)+u32(blob,0xB4))-off
    for i,((_,psize,kind),va) in enumerate(zip(ps,seg_va)):
        if kind==3 or va is None or psize==0 or i>=len(os_):continue
        poff,pwin=window(ps,p,i);ooff,owin=window(os_,o,i)
        for a,b in changed_words(o[ooff:ooff+owin],p[poff:poff+pwin]):
            m.changed.append((va+a,va+b,SEG_NAMES.get(kind,'?')))
    return m

# ---------------------------------------------------------------- analysis helpers
class World:
    def __init__(s,modules):
        s.modules=modules;s.regions=[r for m in modules for r in m.regions]
    def region(s,a):
        # the smallest region wins: a CRO .text lies inside its read-only file image
        inner=[r for r in s.regions if a in r]
        return min(inner,key=lambda r:r.end-r.start) if inner else None
    def read32(s,a):
        for m in s.modules:
            for va,data,perm in m.maps:
                if va<=a<va+len(data)-3:return u32(data,a-va)
        return None

def payload_ranges(m):
    """Changed .text runs, word-aligned; runs a few bytes apart (an instruction and its literal) are merged."""
    out=[]
    for a,b in sorted((a&~3,up(b,4)) for a,b,name in m.changed if name=='.text'):
        if out and a-out[-1][1]<=8:out[-1]=(out[-1][0],max(out[-1][1],b))
        else:out.append((a,b))
    return out

import bisect
def inside(ranges,a):
    """ranges: sorted, non-overlapping (start, end) pairs."""
    i=bisect.bisect_right(ranges,(a,0xffffffffff))-1
    return i>=0 and ranges[i][0]<=a<ranges[i][1]

def entries(m,world,cs_arm):
    """Start of each changed .text run plus every branch target that lands in changed .text."""
    pr=payload_ranges(m);found={}
    for a,b in pr:
        found.setdefault(a,'arm')
        for ins in cs_arm.disasm(bytes(world_bytes(world,a,b-a)),a):
            if ins.mnemonic in('b','bl','blx') or ins.mnemonic.startswith(('b','bl')) and len(ins.operands)==1:
                if len(ins.operands)==1 and ins.operands[0].type==2:
                    t=ins.operands[0].imm
                    if inside(pr,t) or inside(pr,t&~1):found.setdefault(t,'thumb' if ins.mnemonic=='blx' else 'arm')
    return found,pr

def world_bytes(world,a,n):
    for m in world.modules:
        for va,data,perm in m.maps:
            if va<=a and a+n<=va+len(data):return data[a-va:a-va+n]
    return b''

# ---------------------------------------------------------------- static pass
def static_pass(m,world,cs,findings,notes):
    from capstone import CS_OP_IMM,CS_OP_MEM,CS_OP_REG
    from capstone.arm import ARM_REG_PC,ARM_REG_SP,ARM_REG_LR,ARM_REG_R0,ARM_REG_R1,ARM_REG_R2,ARM_REG_R3,ARM_REG_R12
    found,pr=entries(m,world,cs['arm'])
    stores=[];visits={};count=0
    def val(state,reg):return state.get(reg)
    # Worklist of entries. After each round, every changed word no path reached starts a new entry (linear sweep), so
    # code that is only reached through a function pointer or a relocation is analysed too.
    pending=sorted(found.items());used=dict(found);swept=set()
    words={w for x,y in pr for w in range(x,y,4)}
    while pending:
      for entry,mode in pending:
        work=[(entry&~1 if mode=='thumb' else entry,mode,{})]
        while work:
            pc,mode,state=work.pop()
            while inside(pr,pc):
                key=(pc,mode);visits[key]=visits.get(key,0)+1
                if visits[key]>4:break
                raw=world_bytes(world,pc,4)
                ins=next(cs[mode].disasm(raw,pc),None)
                if ins is None:break
                count+=1;mn=ins.mnemonic.split('.')[0];ops=ins.operands;nxt=pc+ins.size
                cond=ins.cc not in(0,15) if hasattr(ins,'cc') else False
                pcv=pc+(8 if mode=='arm' else 4)
                def opval(op):
                    if op.type==CS_OP_IMM:return op.imm
                    if op.type==CS_OP_REG:return pcv if op.reg==ARM_REG_PC else val(state,op.reg)
                    return None
                written=[]
                # stores
                if mn.startswith(('str','stm','push','vstr','vstm','stc','swp')) and not mn.startswith('strex') or mn.startswith('strex'):
                    targets=[]
                    if mn.startswith(('stm','vstm','push')):
                        base=ops[0].reg if ops and ops[0].type==CS_OP_REG else ARM_REG_SP
                        if mn.startswith('push'):base=ARM_REG_SP
                        b=val(state,base)
                        if b is not None:targets.append(b)
                        elif base!=ARM_REG_SP:stores.append(('unknown',pc,ins))
                        if getattr(ins,'writeback',False) and base!=ARM_REG_SP:state.pop(base,None)
                    else:
                        mem=[op for op in ops if op.type==CS_OP_MEM]
                        if mem:
                            op=mem[0];b=pcv if op.mem.base==ARM_REG_PC else val(state,op.mem.base)
                            if op.mem.base==ARM_REG_SP:b=None;base_sp=True
                            else:base_sp=False
                            if b is not None and op.mem.index==0:
                                post=len([o for o in ops if o.type==CS_OP_IMM])>0 and getattr(ins,'post_index',False)
                                targets.append(b if post else b+op.mem.disp)
                            elif not base_sp:stores.append(('unknown',pc,ins))
                            if getattr(ins,'writeback',False):state.pop(op.mem.base,None)
                    for t in targets:
                        r=world.region(t)
                        stores.append(('known',pc,ins,t))
                        if r is not None and r.perm!='rw':
                            findings.append(dict(kind='store-into-read-only',module=m.name,at=hex(pc),instruction=f'{ins.mnemonic} {ins.op_str}',target=hex(t),region=r.label(),perm=r.perm))
                # control flow
                if mn in('b','bl','blx','bx') or (mn.startswith('b') and len(mn)<=4 and mn not in('bic','bics','bfi','bfc','bkpt')):
                    tgt=opval(ops[0]) if ops else None
                    link=mn.startswith('bl')
                    tmode=mode
                    if mn.startswith('blx') and ops and ops[0].type==CS_OP_IMM:tmode='thumb' if mode=='arm' else 'arm'
                    if mn.startswith('bx') or (mn.startswith('blx') and ops and ops[0].type==CS_OP_REG):
                        if tgt is not None:tmode='thumb' if tgt&1 else 'arm';tgt&=~1
                        elif ops and ops[0].reg==ARM_REG_LR:break
                    if tgt is not None:
                        r=world.region(tgt)
                        if r is None or r.perm!='rx':
                            findings.append(dict(kind='branch-to-non-executable',module=m.name,at=hex(pc),instruction=f'{ins.mnemonic} {ins.op_str}',target=hex(tgt),region=r.label() if r else 'unmapped'))
                        elif r.name.startswith('code region'):
                            notes.add(f'{m.name}: code at {tgt:#x} runs from the executable code region but after the declared .text size')
                    if link:
                        for reg in(ARM_REG_R0,ARM_REG_R1,ARM_REG_R2,ARM_REG_R3,ARM_REG_R12,ARM_REG_LR):state.pop(reg,None)
                        pc=nxt;continue
                    if tgt is not None and inside(pr,tgt):
                        if cond:work.append((tgt,tmode,dict(state)));pc=nxt;continue
                        pc,mode=tgt,tmode;continue
                    if cond:pc=nxt;continue
                    break
                # returns
                if (mn.startswith(('pop','ldm')) and any(op.type==CS_OP_REG and op.reg==ARM_REG_PC for op in ops)) or (mn.startswith(('mov','ldr')) and ops and ops[0].type==CS_OP_REG and ops[0].reg==ARM_REG_PC):
                    if cond:pc=nxt;continue
                    break
                # constant propagation
                dst=ops[0].reg if ops and ops[0].type==CS_OP_REG else None
                if dst is not None and mn not in('cmp','cmn','tst','teq') and not mn.startswith(('str','stm','push','vstr','vstm')):
                    v=None
                    try:
                        if mn.startswith('ldr') and not mn.startswith(('ldrd','ldrex')) and len(ops)>=2 and ops[1].type==CS_OP_MEM:
                            op=ops[1]
                            if op.mem.base==ARM_REG_PC and op.mem.index==0 and mn in('ldr',):
                                addr=(pcv&~3)+op.mem.disp;v=world.read32(addr)
                                if addr in m.imports:v=None
                            if getattr(ins,'writeback',False):state.pop(op.mem.base,None)
                        elif mn in('mov','movs','movw','mvn','mvns') and len(ops)==2:
                            x=opval(ops[1])
                            if x is not None:v=(~x)&0xffffffff if mn.startswith('mvn') else x&0xffffffff
                        elif mn=='movt' and len(ops)==2:
                            lo=val(state,dst);v=None if lo is None else (lo&0xffff)|((ops[1].imm&0xffff)<<16)
                        elif mn in('add','adds','sub','subs','orr','orrs','and','ands','bic','bics','eor','eors','lsl','lsls','lsr','lsrs','adr') and len(ops)>=2:
                            if mn=='adr':v=(pcv&~3)+ops[1].imm
                            else:
                                a=opval(ops[1]) if len(ops)==3 else val(state,dst)
                                b=opval(ops[2]) if len(ops)==3 else opval(ops[1])
                                if a is not None and b is not None:
                                    f={'add':lambda:a+b,'sub':lambda:a-b,'orr':lambda:a|b,'and':lambda:a&b,'bic':lambda:a&~b,'eor':lambda:a^b,'lsl':lambda:a<<b,'lsr':lambda:a>>b}[mn.rstrip('s') if mn not in('lsls','lsrs') else mn[:3]]
                                    v=f()&0xffffffff
                    except Exception:v=None
                    if v is None:state.pop(dst,None)
                    else:state[dst]=v
                    if mn.startswith('ldm') or mn.startswith('pop'):
                        for op in ops:
                            if op.type==CS_OP_REG:state.pop(op.reg,None)
                pc=nxt
      visited={pc for pc,_ in visits}
      pending=[]
      for x,y in pr:
          w=x
          while w<y:
              if w not in visited and w not in used:
                  # strings and tables inside .text decode as conditional junk: start only where the word is an
                  # always-executed instruction
                  word=world.read32(w)
                  if word is not None and (word>>28)==0xE:
                      pending.append((w,'arm'));used[w]='arm';swept.add(w)
                      while w<y and w not in visited:w+=4
              w+=4
    visited={pc for pc,_ in visits}
    m.static_entries={e:md for e,md in used.items() if e not in swept}
    return dict(entries=len(used),instructions=count,coverage=f'{len(words&visited)}/{len(words)} words',known_stores=sum(1 for s in stores if s[0]=='known'),unknown_stores=sum(1 for s in stores if s[0]=='unknown'),
                store_targets=sorted({(world.region(s[3]).label() if world.region(s[3]) else 'unmapped/heap') for s in stores if s[0]=='known'}))

# ---------------------------------------------------------------- dynamic pass
ACCESS={22:'write into protected memory',24:'execute non-executable memory',23:'read protected memory'}
HEAP=0x08000000;HEAP_SIZE=0x100000;STACK=0x0F000000;STACK_SIZE=0x40000;END=0x0FF00000

def dynamic_pass(world,targets,cs,findings,limit=4000):
    from unicorn import Uc,UcError,UC_ARCH_ARM,UC_MODE_ARM,UC_PROT_READ,UC_PROT_WRITE,UC_PROT_EXEC,UC_HOOK_CODE,UC_HOOK_MEM_WRITE_PROT,UC_HOOK_MEM_FETCH_PROT,UC_HOOK_MEM_UNMAPPED,UC_HOOK_MEM_WRITE
    from unicorn.arm_const import UC_ARM_REG_R0,UC_ARM_REG_SP,UC_ARM_REG_LR,UC_ARM_REG_PC,UC_ARM_REG_CPSR
    perms={'rx':UC_PROT_READ|UC_PROT_EXEC,'r':UC_PROT_READ,'rw':UC_PROT_READ|UC_PROT_WRITE}
    uc=Uc(UC_ARCH_ARM,UC_MODE_ARM)
    for m in world.modules:
        for va,data,perm in m.maps:
            uc.mem_map(va,up(len(data)),perms[perm]);uc.mem_write(va,bytes(data))
        # the CRO file image is mapped read-only; give its .text pages RX
        if m.name!='code.bin':
            for r in m.regions:
                if r.perm=='rx':
                    a=r.start&~(PAGE-1);uc.mem_protect(a,up(r.end)-a,perms['rx'])
    uc.mem_map(HEAP,HEAP_SIZE,perms['rw']);uc.mem_map(STACK,STACK_SIZE,perms['rw']);uc.mem_map(END,PAGE,perms['rx'])
    heap=bytearray(HEAP_SIZE)
    for i in range(0,HEAP_SIZE,4):struct.pack_into('<I',heap,i,HEAP+((i*0x9E3779B1)&(HEAP_SIZE-0x100)&~3))
    runs_done=0;external=0
    state={}
    def on_prot(uc,access,address,size,value,user):
        state['violation']=(access,address)
        return False
    def on_unmapped(uc,access,address,size,value,user):
        state['unmapped']=(access,address);return False
    def on_code(uc,address,size,user):
        state['count']+=1
        if address==END or state['count']>limit:uc.emu_stop();return
        pr=state['ranges']
        if not inside(pr,address&~1):
            # left the payload: a call into game code returns a heap pointer; a jump back into game code ends the run
            if state['last_call']:
                uc.reg_write(UC_ARM_REG_R0,HEAP+0x400);lr=uc.reg_read(UC_ARM_REG_LR)
                cpsr=uc.reg_read(UC_ARM_REG_CPSR)
                if lr&1:uc.reg_write(UC_ARM_REG_CPSR,cpsr|0x20)
                else:uc.reg_write(UC_ARM_REG_CPSR,cpsr&~0x20)
                uc.reg_write(UC_ARM_REG_PC,lr&~1);state['last_call']=False;return
            state['left']=address;uc.emu_stop();return
        state['last_call']=address in calls
        state['pcs'].add(address)
    uc.hook_add(UC_HOOK_CODE,on_code)
    uc.hook_add(UC_HOOK_MEM_WRITE_PROT|UC_HOOK_MEM_FETCH_PROT,on_prot)
    uc.hook_add(UC_HOOK_MEM_UNMAPPED,on_unmapped)
    from unicorn.arm_const import UC_ARM_REG_R1,UC_ARM_REG_R2,UC_ARM_REG_R3,UC_ARM_REG_R4,UC_ARM_REG_R5,UC_ARM_REG_R6,UC_ARM_REG_R7,UC_ARM_REG_R8,UC_ARM_REG_R9,UC_ARM_REG_R10,UC_ARM_REG_R11,UC_ARM_REG_R12
    regs=[UC_ARM_REG_R0,UC_ARM_REG_R1,UC_ARM_REG_R2,UC_ARM_REG_R3,UC_ARM_REG_R4,UC_ARM_REG_R5,UC_ARM_REG_R6,UC_ARM_REG_R7,UC_ARM_REG_R8,UC_ARM_REG_R9,UC_ARM_REG_R10,UC_ARM_REG_R11,UC_ARM_REG_R12]
    seeds=[lambda i:HEAP+0x1000*(i+1),lambda i:0,lambda i:HEAP+0x40*i+4]
    # calls inside the payload, decoded once: when execution leaves the payload after one of them, it is a call
    calls=set()
    for ranges in {id(r):r for _,_,_,r in targets}.values():
        for a,b in ranges:
            for ins in cs['arm'].disasm(bytes(world_bytes(world,a,b-a)),a):
                if ins.mnemonic.split('.')[0] in('bl','blx') or ins.mnemonic.startswith(('bl','blx')) and not ins.mnemonic.startswith(('bic',)):calls.add(ins.address)
    coverage={}
    for module,entry,mode,ranges in targets:
        for seed in seeds:
            uc.mem_write(HEAP,bytes(heap));uc.mem_write(STACK,bytes(STACK_SIZE))
            for m in world.modules:
                for va,data,perm in m.maps:
                    if perm=='rw':uc.mem_write(va,bytes(data))
            for i,r in enumerate(regs):uc.reg_write(r,seed(i)&0xffffffff)
            uc.reg_write(UC_ARM_REG_SP,STACK+STACK_SIZE-0x100);uc.reg_write(UC_ARM_REG_LR,END)
            cpsr=uc.reg_read(UC_ARM_REG_CPSR)
            uc.reg_write(UC_ARM_REG_CPSR,(cpsr|0x20) if mode=='thumb' else (cpsr&~0x20))
            state.clear();state.update(count=0,ranges=ranges,last_call=False,pcs=set())
            try:uc.emu_start(entry|(1 if mode=='thumb' else 0),END,count=limit+10)
            except UcError as e:state.setdefault('error',str(e))
            runs_done+=1
            coverage.setdefault((module,entry),set()).update(state['pcs'])
            if 'violation' in state:
                access,address=state['violation'];r=world.region(address)
                # a jump through a pointer taken from the synthetic heap is not a finding; a write into, or a jump to,
                # a module region that does not allow it is
                if r is not None:
                    findings.append(dict(kind='runtime-fault',module=module,entry=hex(entry),access=ACCESS.get(access,str(access)),
                                         address=hex(address),region=r.label(),pc=hex(uc.reg_read(UC_ARM_REG_PC))))
                else:external+=1
    total=sum(len(v) for v in coverage.values())
    return dict(runs=runs_done,covered_instructions=total,stopped_at_external_pointer=external)

# ---------------------------------------------------------------- self-test
def self_test(dependencies):
    """Synthetic code.bin (one page each of .text/.rodata/.data, 0x100 bytes of .bss): a hook calls a payload that
    stores through a literal. Stores into .text or .rodata and a call into .data must be reported; a store into .bss not."""
    import tempfile,subprocess
    T,R,D=0x100000,0x101000,0x102000
    def build(folder,payload):
        (folder/'ExeFS').mkdir(parents=True)
        code=bytearray(3*PAGE)
        if payload is not None:
            struct.pack_into('<I',code,0,0xEB000000|(((0x100-0-8)>>2)&0xffffff))   # bl payload (at .text+0x100)
            for i,w in enumerate(payload):struct.pack_into('<I',code,0x100+i*4,w)
        (folder/'ExeFS'/'.code.bin').write_bytes(bytes(code))
        h=bytearray(0x400);struct.pack_into('<III',h,0x10,T,1,0x200);struct.pack_into('<III',h,0x20,R,1,0x10)
        struct.pack_into('<III',h,0x30,D,1,0x10);struct.pack_into('<I',h,0x3C,0x100);struct.pack_into('<Q',h,0x200,0x0004000000123400)
        (folder/'exheader.bin').write_bytes(bytes(h))
    store=lambda target:[0xE59F4004,0xE5841000,0xE12FFF1E,target]   # ldr r4,[pc,#4]; str r1,[r4]; bx lr; literal
    cases=[('store into .text',store(T+0x40),True),('store into .rodata',store(R+0x10),True),('store into .bss',store(D+0x20),False),
           ('call into .data',[0xEB000000|(((D-(T+0x100)-8)>>2)&0xffffff),0xE12FFF1E],True)]
    ok=True
    with tempfile.TemporaryDirectory() as tmp:
        tmp=Path(tmp);build(tmp/'original',None)
        for i,(name,payload,expected) in enumerate(cases):
            build(tmp/f'p{i}',payload)
            args=[sys.executable,'-I',__file__,'--original',str(tmp/'original'),'--patched',str(tmp/f'p{i}'),'--output',str(tmp/f'r{i}.json')]
            if dependencies:args+=['--dependencies',str(dependencies)]
            found=subprocess.run(args,capture_output=True,text=True).returncode==1
            print(('PASS' if found==expected else 'FAIL')+f': {name} -> {"reported" if found else "not reported"}');ok&=found==expected
    return 0 if ok else 1

# ---------------------------------------------------------------- main
def main():
    p=argparse.ArgumentParser(description=__doc__,formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument('--original',type=Path,help='extraction of the unmodified game (read only)')
    p.add_argument('--patched',type=Path,help='extraction of the randomized game (read only)')
    p.add_argument('--output',type=Path,help='JSON report to create')
    p.add_argument('--dependencies',type=Path,help='folder with capstone and unicorn (pip --target)')
    p.add_argument('--self-test',action='store_true',help='check the detector on synthetic payloads')
    a=p.parse_args()
    if a.self_test:return self_test(a.dependencies)
    if not(a.original and a.patched and a.output):p.error('--original, --patched and --output are required')
    if a.dependencies:sys.path.insert(0,str(a.dependencies))
    from capstone import Cs,CS_ARCH_ARM,CS_MODE_ARM,CS_MODE_THUMB
    cs={'arm':Cs(CS_ARCH_ARM,CS_MODE_ARM),'thumb':Cs(CS_ARCH_ARM,CS_MODE_THUMB)}
    for c in cs.values():c.detail=True
    modules=[load_code(a.original,a.patched)]
    rom=lambda r:find(r,'RomFS','ExtractedRomFS')
    ro,rp=rom(a.original),rom(a.patched)
    base=0x20000000
    if ro and rp:
        for f in sorted(rp.glob('*.cro')):
            of=ro/f.name
            if not of.exists():continue
            o,pb=of.read_bytes(),f.read_bytes()
            if o==pb:continue
            m=load_cro(f.name,o,pb,base);base+=0x1000000
            if m:modules.append(m)
    world=World(modules);findings=[];summary=[];notes=set()
    targets=[]
    import time
    for m in modules:
        started=time.time()
        st=static_pass(m,world,cs,findings,notes)
        print(f'  static {m.name}: {time.time()-started:.1f} s',file=sys.stderr)
        pr=payload_ranges(m)
        targets+=[(m.name,e&~1 if md=='thumb' else e,md,pr) for e,md in m.static_entries.items()]
        changed={}
        for x,y,name in m.changed:changed[name]=changed.get(name,0)+(y-x)
        summary.append(dict(module=m.name,changed_bytes=changed,**st))
    started=time.time()
    dyn=dynamic_pass(world,targets,cs,findings)
    print(f'  dynamic: {time.time()-started:.1f} s',file=sys.stderr)
    seen=set();unique=[]
    for f in findings:
        k=(f['kind'],f['module'],f.get('at') or f.get('entry'),f.get('target') or f.get('address'))
        if k not in seen:seen.add(k);unique.append(f)
    report=dict(original=str(a.original),patched=str(a.patched),code_layout={k:[hex(x) for x in v] if isinstance(v,tuple) else hex(v) for k,v in modules[0].layout.items()},
                modules=summary,dynamic=dyn,notes=sorted(notes),violations=unique)
    a.output.parent.mkdir(parents=True,exist_ok=True)
    a.output.write_text(json.dumps(report,indent=1,ensure_ascii=False),encoding='utf-8')
    for s in summary:
        print(f"{s['module']}: {sum(s['changed_bytes'].values())} bytes changed {s['changed_bytes']}, {s['entries']} entries, {s['instructions']} instructions (coverage {s['coverage']}), stores known {s['known_stores']} / unknown {s['unknown_stores']} -> {', '.join(s['store_targets']) or '-'}")
    print(f"dynamic: {dyn['runs']} runs, {dyn['covered_instructions']} instructions covered")
    for n in sorted(notes):print('NOTE',n)
    for f in unique:print('VIOLATION',json.dumps(f,ensure_ascii=False))
    print('OK: no write into read-only memory and no jump into non-executable memory' if not unique else f'{len(unique)} violation(s)')
    return 1 if unique else 0

if __name__=='__main__':sys.exit(main())
