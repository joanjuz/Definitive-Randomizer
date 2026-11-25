// SPDX-License-Identifier: GPL-3.0-only
// Rare Candy multi-use for Pokémon Ultra Sun / Ultra Moon. Bag.cro (BD97393E…) is the
// same file in both games; its imports are resolved by name at load time, so this one
// routine serves both. Validated in game by the owner as a disposable prototype
// (2026-11-25) with exactly these instructions.
// Assembled at template base 0x18010; branches outside the template are rebased to the
// real allocation by RareCandyUsumPatch. All other targets are Bag.cro offsets.
//
// State block S lives in our own heap block, after the FieldBag sale window object
// (0xAC bytes) at +0xB0, so nothing here writes the CRO segments:
//   +0x00 sub   (0 idle, 1 open, 4 wait open, 5 wait answer, 6 closing-confirm,
//                7 closing-cancel, 2 applying, 3 auto-use next frame)
//   +0x04 pending   +0x08 item   +0x0C PokemonParam*   +0x10 slot
//   +0x14 arg4      +0x18 arg5   +0x1C level before
// Bag.cro stubs: 0x968 MyItem::GetItemNum, 0x6d8 MyItem::Check, 0x350 CoreParam::GetLevel,
// 0x240 operator new(size, heap), 0x3a8 UIView::GetG2DUtil, 0x4c0 UIView::AddSubView,
// FieldBag.cro (anonymous imports): 0xd70 window ctor, 0x9e8 open, 0x9f0 ready,
// 0x9f8 answer (1 confirm, 2 cancel), 0xa00 close, 0xa08 close finished, 0xa18 count.
.org 0
// Hook 0x9C68 (bl 0xc2f0): start of «use item on a Pokémon».
entry:
push {r0, r1, r2, r3, r4, r5, r6, r7, r8, lr}
cmp r1, #0x32
bne passthru
ldr r4, [r0, #0x28]
ldrb r4, [r4]
cmp r4, #4
beq passthru
mov r4, r0
ldr r0, [r4, #0x3c]
mov r1, #0x32
bl 0x968
cmp r0, #1
bls passthru_clear
ldr r6, [r4, #0x94]
cmp r6, #0
bne have
ldr r0, [r4, #0x2c]
ldr r1, [r0, #8]
mov r0, #0xd0
bl 0x240
movs r6, r0
beq passthru
ldr r0, [r4, #0x70]
add r0, r0, #4
bl 0x3a8
mov r2, r0
ldr r1, [r4, #0x2c]
mov r0, r6
bl 0xd70
mov r6, r0
str r6, [r4, #0x94]
ldr r1, [r4, #0x70]
add r0, r1, #4
mov r1, r6
bl 0x4c0
add r5, r6, #0xb0
mov r0, #0
str r0, [r5]
str r0, [r5, #4]
have:
add r5, r6, #0xb0
ldr r0, [sp, #4]
str r0, [r5, #8]
ldr r0, [sp, #8]
str r0, [r5, #0xc]
ldr r0, [sp, #0xc]
str r0, [r5, #0x10]
ldr r0, [sp, #0x28]
str r0, [r5, #0x14]
ldr r0, [sp, #0x2c]
str r0, [r5, #0x18]
mov r0, #0
str r0, [r5, #4]
mov r0, #1
str r0, [r5]
ldr r0, [r4, #0x70]
mov r1, #0
bl 0xa5e4
ldr r0, [r4, #0x7c]
mov r1, #0
bl 0xa944
ldr r0, [r4, #0x70]
mov r1, #0
bl 0xa92c
ldr r0, [r4, #0x70]
mov r1, #0
bl 0xa860
mov r0, #0x19
str r0, [r4, #4]
pop {r0, r1, r2, r3, r4, r5, r6, r7, r8, pc}
passthru_clear:
ldr r6, [r4, #0x94]
cmp r6, #0
beq passthru
add r5, r6, #0xb0
mov r0, #0
str r0, [r5]
str r0, [r5, #4]
passthru:
pop {r0, r1, r2, r3, r4, r5, r6, r7, r8, lr}
b 0xc2f0

.org 0x180
// Hook 0x6144 (ldr r0, [r4, #4]): BagFrame state switch. State 0x19 = our selector.
select:
ldr r0, [r4, #4]
cmp r0, #0x19
bne 0x6148
ldr r6, [r4, #0x94]
add r5, r6, #0xb0
ldr r0, [r5]
cmp r0, #1
beq s_open
cmp r0, #4
beq s_ready
cmp r0, #5
beq s_answer
cmp r0, #6
beq s_confirmed
cmp r0, #7
beq s_cancelled
cmp r0, #3
beq s_auto
b 0x66b4
s_open:
mov r0, r4
bl 0x9fa8
mov r1, r0
mov r2, #0
mov r0, r6
bl 0x9e8
mov r0, #4
str r0, [r5]
b 0x66b4
s_ready:
mov r0, r6
bl 0x9f0
cmp r0, #0
beq 0x66b4
mov r0, #5
str r0, [r5]
b 0x66b4
s_answer:
mov r0, r6
bl 0x9f8
cmp r0, #1
beq s_yes
cmp r0, #2
bne 0x66b4
ldr r0, [r4, #0x70]
mov r1, #1
bl 0xa5e4
mov r0, r6
bl 0xa00
mov r0, #7
str r0, [r5]
b 0x66b4
s_yes:
mov r0, r6
bl 0xa00
mov r0, #6
str r0, [r5]
b 0x66b4
s_confirmed:
mov r0, r6
bl 0xa08
cmp r0, #0
beq 0x66b4
mov r0, r6
bl 0xa18
sub r0, r0, #1
str r0, [r5, #4]
ldr r0, [r5, #0xc]
bl 0x350
str r0, [r5, #0x1c]
s_auto:
mov r0, #2
str r0, [r5]
sub sp, sp, #8
ldr r0, [r5, #0x14]
str r0, [sp]
ldr r0, [r5, #0x18]
str r0, [sp, #4]
mov r0, r4
ldr r1, [r5, #8]
ldr r2, [r5, #0xc]
ldr r3, [r5, #0x10]
bl 0xc2f0
add sp, sp, #8
b 0x66b4
s_cancelled:
mov r0, r6
bl 0xa08
cmp r0, #0
beq 0x66b4
mov r0, #0
str r0, [r5]
str r0, [r5, #4]
mov r0, r4
bl 0x4d60
ldr r0, [r4, #0x28]
mov r1, #0
str r1, [r0, #0x84]
strh r1, [r4, #0x76]
mov r1, #1
str r1, [r4, #4]
b 0x66b4

.org 0x380
// Hook 0xE9A0 (str r7, [r4, #4]): end of the level-up continuation.
repeat:
str r7, [r4, #4]
ldr r0, [r4, #0x28]
ldrb r0, [r0]
cmp r0, #4
beq 0xe9a4
ldr r5, [r4, #0x94]
cmp r5, #0
beq 0xe9a4
add r5, r5, #0xb0
ldr r0, [r5]
cmp r0, #2
bne r_clear
ldr r0, [r5, #4]
cmp r0, #0
beq r_clear
ldr r0, [r4, #0x3c]
mov r1, #0x32
mov r2, #1
bl 0x6d8
cmp r0, #0
beq r_clear
ldr r0, [r5, #0xc]
bl 0x350
ldr r1, [r5, #0x1c]
cmp r0, r1
bls r_clear
str r0, [r5, #0x1c]
ldr r0, [r5, #4]
sub r0, r0, #1
str r0, [r5, #4]
mov r0, #3
str r0, [r5]
mov r0, #0x19
str r0, [r4, #4]
b 0xe9a4
r_clear:
mov r0, #0
str r0, [r5]
str r0, [r5, #4]
b 0xe9a4
