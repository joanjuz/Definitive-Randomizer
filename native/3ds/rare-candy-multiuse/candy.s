// SPDX-License-Identifier: GPL-3.0-only
// Rare Candy multi-use for Pokémon Omega Ruby (000400000011C400), code.bin profile
// "or-d587c98a". Reconstructed from the reachable part of the reference patch by
// alvarocm21 (free use with credit); see docs/third-party/RARE_CANDY_MULTIUSE_OR.md.
//
// Addresses are virtual addresses of .code (file offset = VA - 0x100000).
// Every instruction lives in the zero padding after .text (text ends at 0x579604,
// the page ends at 0x57A000). r12 always carries the DllBag hook site + 8, set by
// the trampoline "mov r12, pc; ldr pc, [pc, #-4]; .word target"; hooks return
// with "add r12, r12, #4; bx r12". DllBag functions are reached relative to r12,
// so the CRO load address does not matter.
//
// State (12 bytes in the unused tail of the last .bss page, VA 0x6AEFF0):
//   +0 candies still to apply   +4 phase (0 idle, 1 selector open, 2 applying)
//   +8 level before the first candy (0 = not in a multi-use)
//
// Native calls (code.bin, accredited by fingerprint in RareCandyMultiUsePatch):
//   0x46DA9C  can the item be used on this Pokémon (stops at level 100)
//   0x4635D0  remove items from the bag
//   0x46CC70  apply the item effect
//   0x14FD1C  current level
//   DllBag+0xA608  number selector (open / close)

.org 0x579690
// DllBag+0x6C30: bag state dispatch. Replaces
//   cmp r0, #0xB ; ldrlo pc, [pc, r0, lsl #2] ; b default
dispatch: ldr r3, state_a
ldr r2, [r3, #4]
cmp r2, #1
bne dispatch_original
cmp r0, #1
cmpne r0, #2
beq selector_confirmed
cmp r0, #3
cmpne r0, #4
bne dispatch_original
mov r2, #0
str r2, [r3]
str r2, [r3, #4]
dispatch_original: cmp r0, #0xb
addlo r12, r12, #4
ldrlo pc, [r12, r0, lsl #2]
add r12, r12, #0x1f8
bx r12
// Selector confirmed: the first candy follows the original path, the rest wait.
selector_confirmed: ldr r1, [r4, #0x50]
sub r1, r1, #1
str r1, [r3]
mov r1, #2
str r1, [r3, #4]
ldr r0, [r4]
mov r1, #0
mov r2, #0
sub r12, r12, #0x6c00
sub r12, r12, #0x38
add r12, r12, #0xa600
add r12, r12, #8
blx r12
mov r0, #0x4b
pop {r3, r4, r5, pc}

// DllBag+0x3618: after one candy. Replaces
//   ldr r0, [r4, #0x130] ; ldr r1, [r7, #4] ; ldrsb r2, [r0, #4]
apply_next: ldr r3, state_a
ldr r0, [r3]
cmp r0, #0
beq apply_original
ldrh r1, [r4, #0x4c]
cmp r1, #0x32
bne apply_clear
str r12, [sp, #-4]!
sub sp, sp, #4
ldr r0, [r4, #0x58]
ldrh r2, [r4, #0xc4]
ldr r3, [r5, #0x60]
bl 0x46da9c
add sp, sp, #4
ldr r12, [sp], #4
cmp r0, #0
beq apply_clear
str r12, [sp, #-4]!
sub sp, sp, #4
ldr r0, [r5, #8]
ldr r0, [r0, #0x30]
mov r1, #0x32
mov r2, #1
ldr r3, [r5, #0x60]
bl 0x4635d0
add sp, sp, #4
ldr r12, [sp], #4
ldr r3, state_a
ldr r0, [r3]
sub r0, r0, #1
str r0, [r3]
mov r0, #2
str r0, [r3, #4]
mov r0, #0x4b
add sp, sp, #0xc
pop {r4, r5, r6, r7, r8, r9, pc}
apply_clear: ldr r3, state_a
mov r0, #0
str r0, [r3]
str r0, [r3, #4]
apply_original: ldr r3, state_a
mov r0, #0
str r0, [r3, #8]
ldr r0, [r4, #0x130]
ldr r1, [r7, #4]
ldrsb r2, [r0, #4]
add r12, r12, #4
bx r12
state_a: .word 0x006aeff0

.org 0x579878
// DllBag+0x2B48: start of «Use». Replaces
//   push {r4-r9, lr} ; mov r6, r0 ; add r7, r0, #8
use_entry: ldr r1, [r0, #0x58]
ldr r3, state_b
ldr r2, [r3, #4]
cmp r2, #2
beq use_reset_phase
ldrh r2, [r1, #0x4c]
cmp r2, #0x32
bne use_reset_all
ldrh r2, [r1, #0x4e]
cmp r2, #1
bls use_reset_all
push {r4, lr}
mov r4, r3
mov r2, #1
str r2, [r1, #0x50]
ldr r0, [r1]
mov r1, #1
sub r12, r12, #0x2b00
sub r12, r12, #0x50
add r12, r12, #0xa600
add r12, r12, #8
blx r12
mov r2, #1
str r2, [r4, #4]
mov r0, #0x15
pop {r4, pc}
use_reset_all: mov r2, #0
str r2, [r3]
use_reset_phase: mov r2, #0
str r2, [r3, #4]
mov r2, #0
str r2, [r3, #8]
push {r4, r5, r6, r7, r8, r9, lr}
mov r6, r0
add r7, r0, #8
add r12, r12, #4
bx r12
state_b: .word 0x006aeff0

// DllBag+0x2C88: apply the pending candies. Replaces
//   ldr r0, [r4, #0x58] ; bl <apply effect import> ; mov r5, #0
apply_loop: push {r4, r6, r7, r8, r9, r10, r11, r12}
ldr r10, [sp, #0x20]
ldr r11, [sp, #0x24]
mov r9, r4
mov r7, r6
ldr r6, [r9, #0x58]
mov r4, r1
mov r8, r3
ldr r0, state_c
ldr r0, [r0]
cmp r0, #0
beq loop_first
mov r0, r6
bl 0x14fd1c
ldr r1, state_c
str r0, [r1, #8]
loop_first: bl use_one
loop_check: ldr r0, state_c
ldr r1, [r0]
cmp r1, #0
beq loop_done
cmp r4, #0x32
bne loop_done
mov r0, r6
mov r1, r4
ldrh r2, [r9, #0xc4]
mov r3, r11
bl 0x46da9c
cmp r0, #0
beq loop_done
ldr r0, [r7, #8]
ldr r0, [r0, #0x30]
mov r1, r4
mov r2, #1
mov r3, r11
bl 0x4635d0
bl use_one
ldr r0, state_c
ldr r1, [r0]
sub r1, r1, #1
str r1, [r0]
b loop_check
loop_done: ldr r0, state_c
mov r1, #0
str r1, [r0]
pop {r4, r6, r7, r8, r9, r10, r11, r12}
mov r5, #0
add r12, r12, #4
bx r12
use_one: str lr, [sp, #-4]!
sub sp, sp, #0xc
str r10, [sp]
str r11, [sp, #4]
mov r0, r6
mov r1, r4
mov r2, #0
mov r3, r8
bl 0x46cc70
add sp, sp, #0xc
ldr pc, [sp], #4

// code.bin VA 0x3B5160 (level-up move loop) branches here instead of
// "bne skip": also learn moves with start level < move level <= level.
learn_detour: beq 0x3b5164
ldr r12, state_c
ldr r12, [r12, #8]
cmp r12, #0
beq 0x3b525c
cmp r8, r12
bls 0x3b525c
b 0x3b5164
state_c: .word 0x006aeff0
