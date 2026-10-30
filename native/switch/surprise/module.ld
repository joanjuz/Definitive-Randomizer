OUTPUT_FORMAT(elf64-littleaarch64)
OUTPUT_ARCH(aarch64)
ENTRY(__module_start)
PHDRS { text PT_LOAD FLAGS(5); rodata PT_LOAD FLAGS(4); data PT_LOAD FLAGS(6); dynamic PT_DYNAMIC FLAGS(6); }
SECTIONS {
 . = 0;
 .text : { KEEP(*(.text.start)) *(.text .text.*) } :text
 . = ALIGN(0x1000);
 .rodata : { KEEP(*(.rodata.mod0)) *(.rodata .rodata.*) } :rodata
 .hash : { *(.hash) } :rodata
 .gnu.hash : { *(.gnu.hash) } :rodata
 .dynsym : { *(.dynsym) } :rodata
 .dynstr : { *(.dynstr) } :rodata
 .rela.dyn : { *(.rela.dyn) } :rodata
 .rela.plt : { *(.rela.plt) } :rodata
 . = ALIGN(0x1000);
 .data : { KEEP(*(.data.drconfig)) *(.data .data.*) } :data
 .got : { *(.got .got.*) } :data
 .dynamic : { *(.dynamic) } :data :dynamic
 __bss_start = .;
 .bss (NOLOAD) : { *(.bss .bss.*) *(COMMON) . = ALIGN(16); } :data
 __bss_end = .;
 /DISCARD/ : { *(.comment) *(.note*) *(.eh_frame*) }
}
