// SPDX-License-Identifier: GPL-3.0-only
// Disguise for species other than Mimikyu, Ultra Sun / Ultra Moon. Battle.cro (334AB920…) is
// the same file in both games. Validated in game by the owner as a disposable prototype
// (2026-11-25) with exactly these instructions.
// Template base 0x18010; branches outside the template are rebased to the real block by
// DisguiseUnlockPatch. All other targets are Battle.cro offsets.
// State: bit 15 (0x8000) of [bpp+0x1C] = «Disguise busted» for species other than Mimikyu.
// That bit belongs to a declared 7-bit field no code reads; the constructor zeroes it, the
// party setup at battle start keeps it, ChangeForm only writes bit 8 (0/1). Mimikyu (778)
// always returns to the original form-based path. No form change for other species.
.org 0
// Hook 0xD8DA8 (bust handler 0xD8D8C). r0 = species, r5 = bpp, r4 = pending damage, r6 = pokeID, r7 = ctx.
bust:
sub r1, r0, #0x300
subs r1, r1, #0xa
beq 0xd8db4
ldrh r0, [r5, #0x1c]
tst r0, #0x8000
bne 0xd8e98
mov r0, r5
bl 0x7f7a8
cmp r0, #0
ldreq r0, [r4]
cmpeq r0, #1
bne 0xd8e98
ldrh r0, [r5, #0x1c]
orr r0, r0, #0x8000
strh r0, [r5, #0x1c]
mov r0, #0
str r0, [r4]
mov r2, r6
mov r1, #1
mov r0, r7
bl 0x89e28
mov r2, r6
mov r1, #3
mov r0, r7
bl 0x876f8
mov r4, r0
mov r2, #0x168
mov r1, #1
add r0, r0, #4
bl 0x89c6c
mov r1, r4
mov r0, r7
bl 0x7ccf4
b 0xd8e84

.org 0x90
// Hook 0xDE078 (handler 0xDE034). r4 = bpp.
pre_a:
sub r1, r0, #0x300
subs r1, r1, #0xa
beq 0xde084
ldrh r0, [r4, #0x1c]
tst r0, #0x8000
bne 0xde0d0
b 0xde098

.org 0xb0
// Hook 0xDE118 (handler 0xDE0D4). r4 = bpp.
pre_b:
sub r1, r0, #0x300
subs r1, r1, #0xa
beq 0xde124
ldrh r0, [r4, #0x1c]
tst r0, #0x8000
bne 0xde164
b 0xde138

.org 0xd0
// Hook 0xDE530 (handler 0xDE4DC). r4 = bpp.
pre_c:
sub r1, r0, #0x300
subs r1, r1, #0xa
beq 0xde53c
ldrh r0, [r4, #0x1c]
tst r0, #0x8000
bne 0xde588
b 0xde550
