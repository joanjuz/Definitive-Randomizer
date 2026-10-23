/* SPDX-License-Identifier: GPL-3.0-or-later */
.section .text.start,"ax"
.global __module_start
__module_start:
 b dr_start
 .word dr_mod0-__module_start
 .ascii "DR Sword Surprise"
 .balign 4
dr_start:
 ret
.section .rodata.mod0,"a"
.balign 4
dr_mod0:
 .ascii "MOD0"
 .word _DYNAMIC-dr_mod0
 .word __bss_start-dr_mod0
 .word __bss_end-dr_mod0
 .word 0
 .word 0
 .word dr_module_runtime-dr_mod0
.section .bss.runtime,"aw",%nobits
.balign 16
dr_module_runtime:
 .space 0x2000
.section .text,"ax"
.global dr_query_memory
.hidden dr_query_memory
dr_query_memory:
 str x1,[sp,#-16]!
 svc #6
 ldr x2,[sp],#16
 str w1,[x2]
 ret
.global dr_debug_string
.hidden dr_debug_string
dr_debug_string:
 svc #0x27
 ret
.macro SAVE_VOLATILE
 sub sp,sp,#0x140
 stp x0,x1,[sp,#0]
 stp x2,x3,[sp,#16]
 stp x4,x5,[sp,#32]
 stp x6,x7,[sp,#48]
 stp x8,x9,[sp,#64]
 stp x10,x11,[sp,#80]
 stp x12,x13,[sp,#96]
 stp x14,x15,[sp,#112]
 stp x16,x17,[sp,#128]
 str x18,[sp,#144]
 stp q0,q1,[sp,#160]
 stp q2,q3,[sp,#192]
 stp q4,q5,[sp,#224]
 stp q6,q7,[sp,#256]
 mrs x16,nzcv
 mrs x17,fpcr
 stp x16,x17,[sp,#288]
 mrs x16,fpsr
 str x16,[sp,#304]
.endm
.macro RESTORE_VOLATILE
 ldp x16,x17,[sp,#288]
 msr nzcv,x16
 msr fpcr,x17
 ldr x16,[sp,#304]
 msr fpsr,x16
 ldp q0,q1,[sp,#160]
 ldp q2,q3,[sp,#192]
 ldp q4,q5,[sp,#224]
 ldp q6,q7,[sp,#256]
 ldp x0,x1,[sp,#0]
 ldp x2,x3,[sp,#16]
 ldp x4,x5,[sp,#32]
 ldp x6,x7,[sp,#48]
 ldp x8,x9,[sp,#64]
 ldp x10,x11,[sp,#80]
 ldp x12,x13,[sp,#96]
 ldp x14,x15,[sp,#112]
 ldp x16,x17,[sp,#128]
 ldr x18,[sp,#144]
 add sp,sp,#0x140
.endm
.macro SPEC_WRAPPER name,original,helper
.global \name
.hidden \name
\name:
 stp x19,x30,[sp,#-16]!
 mov x19,x1
 adrp x16,\original
 ldr x16,[x16,:lo12:\original]
 blr x16
 SAVE_VOLATILE
 mov x0,x19
 bl \helper
 RESTORE_VOLATILE
 ldp x19,x30,[sp],#16
 ret
.endm
SPEC_WRAPPER dr_basic_entry,dr_original_basic,dr_note_basic
SPEC_WRAPPER dr_gimmick_entry,dr_original_gimmick,dr_note_gimmick
SPEC_WRAPPER dr_finalized_entry,dr_original_finalized,dr_note_finalized
.global dr_battle_entry
.hidden dr_battle_entry
dr_battle_entry:
 stp x19,x20,[sp,#-32]!
 str x30,[sp,#16]
 mov x19,x0
 mov x20,x8
 adrp x16,dr_original_battle
 ldr x16,[x16,:lo12:dr_original_battle]
 blr x16
 /* D609A8 dereferences the return handle before writing the battle spec. */
 ldr x20,[x20]
 SAVE_VOLATILE
 mov x0,x19
 mov x1,x20
 bl dr_after_battle_copy
 RESTORE_VOLATILE
 ldr x30,[sp,#16]
 ldp x19,x20,[sp],#32
 ret
