// SPDX-License-Identifier: GPL-3.0-only
// Adapted for Definitive Randomizer, 2026-11-05, from adhfult's
// AbilityPatchAndNatureMints.json at 274d836e88f840c9c326a569198ebff8031969d6.
// Nature-only implementation: no Ability Patch, Shifter or other item handlers.
// ARM little endian, linked at 0x18010. External B/BL instructions are rebased
// by UsumNatureMintPatch; all local addresses use PC-relative instructions.
.org 0
recover:
    push {r0-r6, lr}
    mov r4, r0
    bl lookup
    cmp r0, #0
    beq native_recover
    mov r5, r0, lsr #24
    mov r0, r4
    mov r1, #2
    bl 0x700
    tst r0, #1
    bne unavailable
    mov r0, r4
    ldr r12, getter
    blx r12
    cmp r0, r5
    moveq r0, #0
    movne r0, #1
    b recover_return
unavailable:
    mov r0, #0
recover_return:
    add sp, sp, #4
    pop {r1-r6, pc}
native_recover:
    pop {r0-r6, lr}
    b 0x798
.org 0x80
dispatch:
    mov r1, r5
    bl lookup
    cmp r0, #0
    beq 0xc85c
    mov r1, r0, lsr #24
    ldr r0, [sp, #0x44]
    bl apply
    cmp r0, #0
    beq 0xc8e4
    mov r3, #0
    ldr r0, sound
    mvn r2, #0
    mov r1, r3
    bl 0x210
    ldr r0, [r4, #0x70]
    mov r1, #0x100
    add r1, r1, #0x9f
    ldr r2, [sp, #0x44]
    mov r3, #0
    bl 0x80a4
    b 0xc85c
.org 0x100
message:
    cmp r5, #0x100
    blo 0x8450
    mov r2, r7
    mov r1, #0
    mov r0, r9
    bl 0x5f8
    sub r2, r5, #0x100
    mov r1, r8
    mov r0, r9
    bl 0x470
    b 0x8460
.org 0x140
lookup:
    adr r2, mint_table
    mov r0, #0
lookup_next:
    ldr r0, [r2], #4
    cmp r0, #0
    bxeq lr
    mov r3, r0, lsl #16
    cmp r1, r3, lsr #16
    bne lookup_next
    bx lr
.org 0x180
apply:
    push {r4-r8, lr}
    mov r4, r0
    mov r5, r1
    cmp r5, #25
    bhs no_effect
    mov r1, #2
    bl 0x700
    tst r0, #1
    bne no_effect
    mov r0, r4
    ldr r12, getter
    blx r12
    cmp r0, r5
    beq no_effect
    ldr r6, [r4, #0xc]
    mov r0, r6
    ldr r12, begin_access
    blx r12
    mov r0, r6
    mov r1, #0
    ldr r12, block_a
    blx r12
    strb r5, [r0, #0x14]
    mov r0, r6
    ldr r12, end_access
    blx r12
    mov r5, #1
refresh_stats:
    mov r0, r4
    mov r1, r5
    ldr r12, derive_stat
    blx r12
    mov r2, r0
    mov r0, r4
    mov r1, r5
    ldr r12, store_stat
    blx r12
    add r5, r5, #1
    cmp r5, #6
    blo refresh_stats
    mov r0, #1
    pop {r4-r8, pc}
no_effect:
    mov r0, #0
    pop {r4-r8, pc}
.org 0x300
getter: .word 0x4ae0e0
begin_access: .word 0x32258c
block_a: .word 0x4ad588
end_access: .word 0x322514
derive_stat: .word 0x4aea1c
store_stat: .word 0x324d84
sound: .word 0x50021
.org 0x340
mint_table:
// Filled from the stable typed catalog, with a zero terminator.
.org 0x3c0
// Identification, profile number, normalized whole-Bag digest follow here.
.org 0x400
