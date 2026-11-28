// SPDX-License-Identifier: GPL-3.0-only
// XY-only experimental Bag handler. File-relative CRO branches; fixed native
// VAs are independently fingerprinted for X/Y. No USUM hook/offset is reused.
// Only two hooks: import thunk 0x428 and action instruction 0x2B5C.
.org 0
recover:
    push {r0-r6, lr}
    mrs r6, cpsr
    mov r4, r0
    bl lookup
    cmp r0, #0
    beq original_recover
    mov r5, r0, lsr #24
    sub sp, sp, #0x130
    mov r0, r4
    mov r1, sp
    bl clone
    cmp r0, #0
    movne r1, r5
    blne eligible
    add sp, sp, #0x130
    add sp, sp, #4
    pop {r1-r6, pc}
original_recover:
    msr cpsr_f, r6
    pop {r0-r6, lr}
    ldr r12, can_recover
    bx r12

.org 0x100
dispatch:
    push {r0-r3, r10, lr}
    mrs r10, cpsr
    ldrh r1, [r4, #0x4c]
    bl lookup
    cmp r0, #0
    beq original_dispatch
    mov r1, r0, lsr #24
    mov r0, r6
    bl transaction
    cmp r0, #0
    beq dispatch_failed
    ldr r0, [r4]
    ldr r2, [r4, #0x58]
    bl message
    ldrh r1, [r4, #0x4c]
    mov r0, r6
    bl finalize_without_second_consume
    str r0, [sp]
    pop {r0-r3, r10, lr}
    b 0x3068
dispatch_failed:
    pop {r0-r3, r10, lr}
    add r0, sp, #0x10
    bl 0x468
    b 0x30d0
original_dispatch:
    msr cpsr_f, r10
    pop {r0-r3, r10, lr}
    ldr r2, [r4, #0x58]
    b 0x2b60

.org 0x200
message:
    push {r4-r6, lr}
    mov r4, r0
    ldr r0, [r4, #8]
    mov r1, #0
    bl 0x5b8
    ldr r0, [r4, #8]
    mov r1, #1
    bl 0x2e0
    mov r5, r0
    ldr r0, [r4, #8]
    mov r1, r5
    mov r2, #179
    bl 0x2e8
    ldr r0, [r4, #8]
    mov r1, r5
    bl 0x2f0
    mov r0, r4
    pop {r4-r6, lr}
    mov r1, #1
    b 0x9a88

.org 0x280
finalize_without_second_consume:
    push {r4-r6, lr}
    mov r4, r0
    ldr r5, [r4, #0x4c]
    bl 0x96cc
    mov r6, r0
    b 0x9814

.org 0x300
// Stage every PK mutation on our own stack. Until RemoveItem succeeds the
// original PK is only read. After success only bounded copies remain; the
// native finalizer is entered AFTER its decrement so consumption is not doubled.
transaction:
    push {r4-r10, lr}
    sub sp, sp, #0x130
    mov r4, r0
    mov r5, r1
    ldr r6, [r4, #0x4c]
    ldr r7, [r6, #0x58]
    mov r0, r7
    mov r1, sp
    bl clone
    movs r8, r0
    beq transaction_failed
    mov r1, r5
    bl eligible
    cmp r0, #0
    beq transaction_failed
    ldr r0, [r6]
    bl ui_ready
    cmp r0, #0
    beq transaction_failed
    ldr r0, personal_globals
    ldr r0, [r0, #0x14]
    cmp r0, #0
    ldrne r0, [r0, #8]
    cmpne r0, #0
    beq transaction_failed
    ldr r0, personal_table_globals
    ldr r0, [r0, #4]
    cmp r0, #0
    beq transaction_failed
    ldr r9, [r8, #0xc]
    mov r0, r9
    ldr r12, begin_access
    blx r12
    mov r0, r9
    mov r1, #0
    ldr r12, block_a
    blx r12
    strb r5, [r0, #0x14]
    mov r0, r9
    ldr r12, end_access
    blx r12
    mov r0, r8
    ldr r12, refresh_attack
    blx r12
    mov r0, r8
    ldr r12, refresh_defense
    blx r12
    mov r0, r8
    ldr r12, refresh_speed
    blx r12
    mov r0, r8
    ldr r12, refresh_spattack
    blx r12
    mov r0, r8
    ldr r12, refresh_spdefense
    blx r12
    ldr r10, [r7, #0xc]
    ldrb r0, [r10, #0xc]
    cmp r0, #1
    ldrbeq r0, [r10, #0xd]
    cmpeq r0, #0
    bne transaction_failed
    ldr r0, [r4, #8]
    ldrh r1, [r6, #0x4c]
    ldr r0, [r0, #0x30]
    mov r2, #1
    ldr r3, [r4, #0x54]
    bl 0x500
    cmp r0, #0
    beq transaction_failed
    ldr r0, [r10, #8]
    ldr r1, [r9, #8]
    mov r2, #58
    bl copy_words
    ldr r0, [r10, #4]
    ldr r1, [r9, #4]
    mov r2, #7
    bl copy_words
    mov r0, #1
    b transaction_return
transaction_failed:
    mov r0, #0
transaction_return:
    add sp, sp, #0x130
    pop {r4-r10, pc}

.org 0x500
clone:
    push {r4-r8, lr}
    movs r4, r0
    mov r5, r1
    beq clone_failed
    tst r4, #3
    bne clone_failed
    ldr r6, [r4, #0xc]
    cmp r6, #0
    beq clone_failed
    tst r6, #3
    bne clone_failed
    ldrb r0, [r6, #0xc]
    cmp r0, #1
    ldrbeq r0, [r6, #0xd]
    cmpeq r0, #0
    bne clone_failed
    ldr r7, [r6, #8]
    ldr r8, [r6, #4]
    cmp r7, #0
    cmpne r8, #0
    beq clone_failed
    orr r0, r7, r8
    tst r0, #3
    bne clone_failed
    mov r0, r5
    mov r1, r4
    mov r2, #4
    bl copy_words
    add r0, r5, #0x10
    mov r1, r6
    mov r2, #4
    bl copy_words
    add r0, r5, #0x20
    mov r1, r7
    mov r2, #58
    bl copy_words
    add r0, r5, #0x108
    mov r1, r8
    mov r2, #7
    bl copy_words
    add r0, r5, #0x10
    str r0, [r5, #0xc]
    add r1, r5, #0x20
    str r1, [r0, #8]
    add r1, r5, #0x108
    str r1, [r0, #4]
    mov r0, r5
    pop {r4-r8, pc}
clone_failed:
    mov r0, #0
    pop {r4-r8, pc}

.org 0x700
eligible:
    push {r4-r6, lr}
    mov r4, r0
    cmp r1, #25
    mov r5, r1
    bhs ineligible
    mov r1, #2
    bl 0x368
    cmp r0, #0
    bne ineligible
    mov r0, r4
    ldr r12, get_nature
    blx r12
    cmp r0, r5
    movne r0, #1
    moveq r0, #0
    pop {r4-r6, pc}
ineligible:
    mov r0, #0
    pop {r4-r6, pc}

.org 0x780
copy_words:
    ldr r3, [r1], #4
    str r3, [r0], #4
    subs r2, r2, #1
    bne copy_words
    bx lr

.org 0x7c0
lookup:
    adr r2, mint_table
lookup_next:
    ldr r0, [r2], #4
    cmp r0, #0
    bxeq lr
    mov r3, r0, lsl #16
    cmp r1, r3, lsr #16
    bne lookup_next
    bx lr

.org 0x800
ui_ready:
    cmp r0, #0
    ldrne r0, [r0, #8]
    cmpne r0, #0
    bxeq lr
    ldr r1, [r0, #4]
    cmp r1, #0
    moveq r0, #0
    bxeq lr
    ldr r2, [r1]
    ldr r3, [r1, #4]
    cmp r2, #0
    cmpne r3, #0
    ldrne r1, [r0, #0x28]
    cmpne r1, #0
    ldrne r1, [r0, #0x30]
    cmpne r1, #0
    ldrne r1, [r0, #0x34]
    cmpne r1, #0
    movne r0, #1
    moveq r0, #0
    bx lr

.org 0x880
can_recover: .word 0x43ea8c
get_nature: .word 0x4a0d40
begin_access: .word 0x168660
block_a: .word 0x16b358
end_access: .word 0x1685f0
refresh_attack: .word 0x14fb00
refresh_defense: .word 0x14fbd4
refresh_speed: .word 0x14fe78
refresh_spattack: .word 0x14fa2c
refresh_spdefense: .word 0x14fda4
personal_globals: .word 0x5d1790
personal_table_globals: .word 0x5d1778

.org 0x900
mint_table:
// 21 map words and zero terminator supplied by the typed XY catalog.
.org 0xa00
// Marker, title discriminator, source dependency digest and payload digest.
.org 0xb00
