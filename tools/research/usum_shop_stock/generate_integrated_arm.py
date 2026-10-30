from pathlib import Path
import sys,struct,json
import argparse
parser=argparse.ArgumentParser();parser.add_argument('--dependencies',type=Path);args=parser.parse_args()
if args.dependencies:sys.path.insert(0,str(args.dependencies))
from keystone import Ks,KS_ARCH_ARM,KS_MODE_ARM
ks=Ks(KS_ARCH_ARM,KS_MODE_ARM)
functions={
0:'''push {r4-r8,lr}
mov r4,r0
bl 0x300
mov r5,r0
cmp r5,#0
beq original
mov r1,r5
ldr r0,[r4,#0x38]
bl 0x500
cmp r0,#0
bne sold
mov r1,#1
strh r1,[r4,#0x84]
original:
mov r0,r4
bl 0x400
cmp r0,#0
cmpeq r5,#0
beq done
cmp r0,#0
moveq r1,#1
strheq r1,[r4,#0x86]
done:
pop {r4-r8,pc}
sold:
mov r0,#4
pop {r4-r8,pc}''',
0x100:'''push {r0-r3,ip,lr}
mov r0,r4
bl 0x300
cmp r0,#0
beq resume
mov r0,r4
bl 0
cmp r0,#0
bne abort
resume:
pop {r0-r3,ip,lr}
ldr r0,[r4,#0x20]
b 0x15e8
abort:
pop {r0-r3,ip,lr}
b 0x158c''',
0x200:'''push {r0-r3,ip,lr}
mov r0,r4
bl 0x300
cmp r0,#0
beq resume
ldr r2,[sp]
cmp r2,#0
beq abort
mov r1,r0
ldr r0,[r4,#0x38]
bl 0x600
resume:
pop {r0-r3,ip,lr}
ldrsh r0,[r4,#0x82]
b 0x1650
abort:
pop {r0-r3,ip,lr}
b 0x158c''',
0x180:'''push {r4,lr}
mov r4,r0
sub r1,r1,#1
adr r3,legacy_registry
ldr r2,[r3]
add r3,r3,r2
ldr r2,[r3,r1,lsl #3]
ldr r3,key_a
cmp r2,r3
moveq r1,#1
beq bit
add r3,r3,#1
cmp r2,r3
moveq r1,#2
beq bit
ldr r3,key_b
cmp r2,r3
movne r0,#0
popne {r4,pc}
mov r1,#4
bit:
tst r4,r1
mov r0,#0
movne r0,#1
pop {r4,pc}
.align 2
key_a:
.word 0x0065000a
key_b:
.word 0x0066000a
legacy_registry:
.word 0x98765433''',
0x300:'''push {r4-r7,lr}
sub sp,sp,#4
mov r4,r0
ldr r1,[r4,#0x20]
ldrb r2,[r1]
cmp r2,#0
bne none
ldr r5,[r1,#4]
cmp r5,#8
blo none
cmp r5,#28
bhs none
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
adr r7,anchor
ldr r2,[r7]
add r7,r7,r2
ldr r2,[r7],#4
cmp r2,#0
beq none
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
anchor:
.word 0x98765432''',
0x400:'''push {r4-r8,lr}
b 0x2770''',
0x420:'''push {r4-r8,lr}
mov r4,r0
add r5,r0,#0xd00
add r5,r5,#12
ldr r6,[r5]
mov r0,#0
str r0,[r5]
adr r3,migrate_registry
ldr r2,[r3]
add r3,r3,r2
ldr r8,[r3]
mov r7,#1
loop:
cmp r7,r8
bhi finish
mov r0,r6
mov r1,r7
bl 0x180
cmp r0,#0
beq next
sub r1,r7,#1
ldrb r2,[r5,r1,lsr #3]
and r1,r1,#7
mov r3,#1
orr r2,r2,r3,lsl r1
sub r1,r7,#1
strb r2,[r5,r1,lsr #3]
next:
add r7,r7,#1
b loop
finish:
mov r0,r4
pop {r4-r8,pc}
.align 2
migrate_registry:
.word 0x98765434''',
0x500:'''push {r4-r6,lr}
subs r4,r1,#1
blo unavailable
cmp r4,#1984
bhs unavailable
add r5,r0,#0xd00
add r5,r5,#4
ldr r2,[r5]
cmp r2,#0
bne header
mov r6,#64
mov r2,r5
blank:
ldr r3,[r2],#4
cmp r3,#0
bne unavailable
subs r6,r6,#1
bne blank
b available
header:
ldr r3,magic
cmp r2,r3
beq integrated
sub r3,r3,#1
cmp r2,r3
bne unavailable
ldr r2,[r5,#4]
ldr r3,old_identity
cmp r2,r3
bne unavailable
ldr r0,[r5,#8]
bic r2,r0,#7
cmp r2,#0
bne unavailable
add r2,r5,#12
mov r6,#61
old_tail:
ldr r3,[r2],#4
cmp r3,#0
bne unavailable
subs r6,r6,#1
bne old_tail
add r1,r4,#1
pop {r4-r6,lr}
b 0x180
integrated:
ldr r2,[r5,#4]
ldr r3,identity
cmp r2,r3
bne unavailable
add r5,r5,#8
ldrb r2,[r5,r4,lsr #3]
and r1,r4,#7
mov r3,#1
tst r2,r3,lsl r1
bne unavailable
available:
mov r0,#0
pop {r4-r6,pc}
unavailable:
mov r0,#1
pop {r4-r6,pc}
.align 2
magic:
.word 0x44525332
identity:
.word 0x13579bdf
old_identity:
.word 0x53544f43''',
0x600:'''push {r4-r6,lr}
sub r4,r1,#1
mov r6,r0
add r2,r0,#0xd00
ldr r2,[r2,#4]
ldr r3,old_magic
cmp r2,r3
bleq 0x420
mov r0,r6
add r5,r0,#0xd00
add r5,r5,#4
ldr r2,magic
str r2,[r5]
ldr r2,identity
str r2,[r5,#4]
add r5,r5,#8
add r5,r5,r4,lsr #3
ldrb r2,[r5]
and r1,r4,#7
mov r3,#1
orr r2,r2,r3,lsl r1
strb r2,[r5]
pop {r4-r6,pc}
.align 2
magic:
.word 0x44525332
identity:
.word 0x13579bdf
old_magic:
.word 0x44525331''',
0x700:'''cmp r0,#4
bne original
mov r0,r4
movw r1,#0xbeef
mov r2,#0
mov r3,#1
bl 0x1f44
ldr r1,[r4,#0x20]
ldr r1,[r1,#0x18]
str r1,[r4,#0x88]
b 0x3d18
original:
cmp r0,#1
b 0x3cb0'''
}
nop=bytes.fromhex('00f020e3');blob=bytearray(nop*0x200);fix=[]
ordered=sorted(functions)
for addr,src in functions.items():
 limit=next((x for x in ordered if x>addr),0x800)
 data=bytes(ks.asm(src,addr=addr)[0]);assert addr+len(data)<=limit,(hex(addr),len(data),hex(limit));blob[addr:addr+len(data)]=data
 for i in range(0,len(data),4):
  word=struct.unpack_from('<I',data,i)[0]
  if word>>24 in (0xea,0xeb):
   delta=(word&0xffffff);delta=delta-(1<<24) if delta&(1<<23) else delta;target=addr+i+8+4*delta
   if target>=0x1000:fix.append((addr+i,target))
anchor=blob.index(struct.pack('<I',0x98765432));struct.pack_into('<I',blob,anchor,0x900-anchor)
for marker,target in [(0x98765433,0x904),(0x98765434,0x900)]:
 pos=blob.index(struct.pack('<I',marker));struct.pack_into('<I',blob,pos,target-pos)
tag_positions=[i for i in range(len(blob)-3) if blob[i:i+4]==struct.pack('<I',0x13579bdf)]
message=0x70c
assert struct.unpack_from('<I',blob,message)[0]==0xe30b1eef
root=Path(__file__).resolve().parents[3]
cs='''// Generated from tools/research/usum_shop_stock/generate_integrated_arm.py. No game assets.
using System;
using System.Buffers.Binary;
namespace pk3DS.Core.Modding.Research;
public static partial class Gen7SinglePurchaseStock
{
    private static byte[] ArmPayload(uint location,uint identity,ushort message)
    {
        byte[] code=Convert.FromHexString("HEX");
        foreach(var (offset,target) in new (int,uint)[]{FIXUPS})
        {
            uint before=BinaryPrimitives.ReadUInt32LittleEndian(code.AsSpan(offset));
            Write(code,offset,Branch(location+(uint)offset,target,(before&0x01000000)!=0));
        }
        foreach(int offset in new[]{TAGS})Write(code,offset,identity);
        Write(code,0x70c,0xe3001000u|((uint)(message&0xf000)<<4)|(uint)(message&0x0fff));
        return code;
    }
}
'''.replace('HEX',blob.hex().upper()).replace('FIXUPS',','.join(f'({o:#x},{t:#x}u)' for o,t in fix)).replace('TAGS',','.join(hex(i) for i in tag_positions))
(root/'pk3DS.Core/Modding/Research/Gen7SinglePurchaseStock.Arm.g.cs').write_text(cs,encoding='utf-8')
print('Fixups',fix,'tags',tag_positions)
