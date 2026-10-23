/* SPDX-License-Identifier: GPL-3.0-or-later */
#include "adapter.h"
typedef struct {uint64_t address,size;uint32_t type,attribute,permission,ipc,device,pad;} memory_info;
extern uint32_t dr_query_memory(memory_info *info,uint32_t *page,uint64_t address);
extern void dr_basic_entry(void),dr_gimmick_entry(void),dr_battle_entry(void),dr_finalized_entry(void);
uintptr_t dr_original_basic,dr_original_gimmick,dr_original_battle,dr_original_finalized;
typedef struct {uint8_t magic[16];uint32_t version,seed,count,reserved;uint16_t pool[898];} configuration;
__attribute__((used,section(".data.drconfig"))) static volatile const configuration config={
    {'D','R','S','U','R','P','R','I','S','E','C','F','G','1','!',0},1,0,0,0,{0}
};
static volatile uint32_t initialized;
extern void dr_debug_string(const char*,uint64_t);
static void log_line(const char *s){uint64_t n=0;while(s[n])n++;dr_debug_string(s,n);}
static void log_memory(uint32_t type,uint64_t size){
 char line[]="DRSurprise v3: RX type=00000000 size=0000000000000000";
 const char *hex="0123456789ABCDEF";
 for(unsigned i=0;i<8;i++)line[23+i]=hex[(type>>(28-i*4))&15];
 for(unsigned i=0;i<16;i++)line[37+i]=hex[(size>>(60-i*4))&15];
 log_line(line);
}
static uint32_t basic_seen,gimmick_seen,battle_seen;

static uint16_t pool_copy[898];
static void log_spec(const char *label,const void *spec);
void dr_note_basic(const void *spec){if(basic_seen==0)log_line("DRSurprise v3: basic spawn reached");if(basic_seen++<8)log_spec("DRSurprise v5: basic spec",spec);dr_adapter_note_basic(spec,DR_SPEC_SIZE);}
void dr_note_finalized(const void *spec){static uint32_t seen;if(seen++==0)log_line("DRSurprise v7: finalization reached");dr_adapter_note_finalized(spec,DR_SPEC_SIZE);}
void dr_note_gimmick(const void *spec){if(gimmick_seen++==0)log_line("DRSurprise v3: gimmick spawn reached");dr_adapter_note_gimmick(spec,DR_SPEC_SIZE);}
static uint32_t read32(const uint8_t *p){return (uint32_t)p[0]|(uint32_t)p[1]<<8|(uint32_t)p[2]<<16|(uint32_t)p[3]<<24;}
static uint16_t read16(const uint8_t *p){return (uint16_t)(p[0]|p[1]<<8);}
static void log_values(const char *label,const uint32_t *values,unsigned count){
 char line[240];unsigned at=0;while(label[at]){line[at]=label[at];at++;}
 const char *hex="0123456789ABCDEF";
 for(unsigned i=0;i<count;i++){line[at++]=' ';for(unsigned j=0;j<8;j++)line[at++]=hex[(values[i]>>(28-j*4))&15];}line[at]=0;log_line(line);
}
static void log_spec(const char *label,const void *spec){uint32_t values[22];for(unsigned i=0;i<22;i++)values[i]=read32((const uint8_t*)spec+i*4);log_values(label,values,22);}
void dr_after_battle_copy(const void *object,void *output){
 uint32_t reason=0;int changed=dr_adapter_copy_diagnostic(object,DR_OBJECT_SPEC+DR_SPEC_SIZE,output,DR_OUTPUT_MIN,&reason);
 if(battle_seen++<12){
  log_line(changed?"DRSurprise v3: battle species changed":"DRSurprise v3: battle reached but eligibility rejected");
  const uint8_t *o=object,*s=o+DR_OBJECT_SPEC,*d=output;
  uint32_t values[]={reason,read32(o+DR_OBJECT_ROUTE),read32(o+DR_OBJECT_ROUTE+4),read32(s),read16(s+4),s[6],read16(d+0xA0),read16(d+0xA2),read16(d+0xA4)};
  log_values("DRSurprise v5: reason/route/adjacent/spec/form/level/out/form/level",values,9);
  if(reason==9)log_spec("DRSurprise v5: battle spec",s);
 }
}
static uint32_t word(uintptr_t p){return *(const volatile uint32_t*)p;}
void dr_module_init(void){
    if(initialized)return;
    log_line("DRSurprise v3: initializer entered");
    if(config.version!=1||config.count<2||config.count>898){log_line("DRSurprise v3: invalid configuration");return;}
    for(uint32_t i=0;i<config.count;i++)pool_copy[i]=config.pool[i];
    if(!dr_adapter_initialize(config.seed,pool_copy,config.count))return;
    memory_info info;uint32_t page;uint64_t at=0;
    for(unsigned i=0;i<4096;i++){
        if(dr_query_memory(&info,&page,at)!=0||info.size==0){log_line("DRSurprise v3: memory query stopped before matching main");return;}
        if(info.permission==5)log_memory(info.type,info.size);
        if((info.type&255)==3&&info.permission==5&&info.size==0x1901000){
            uintptr_t base=(uintptr_t)info.address;
            if(word(base+0x766D64)==0xB9402A60&&word(base+0x766EBC)==0xB9402A60&&word(base+0xEA2868)==0xB9404EA8&&word(base+0xD609C4)==0x79014288){
                /* Managed builder checks the whole original text and writes these exact stubs. */
                if(word(base+0x1900F38)!=0xB4000050||word(base+0x1900F50)!=0xB4000050||word(base+0x1900F68)!=0xB4000050||word(base+0x1900F80)!=0xB4000050)return;
                dr_original_basic=base+0x1900F40;dr_original_gimmick=base+0x1900F58;dr_original_battle=base+0x1900F70;dr_original_finalized=base+0x1900F88;
                initialized=1;log_line("DRSurprise v3: main matched, installing callbacks");
                *(volatile uintptr_t*)(base+0x2985000)=(uintptr_t)&dr_basic_entry;
                *(volatile uintptr_t*)(base+0x2985008)=(uintptr_t)&dr_gimmick_entry;
                *(volatile uintptr_t*)(base+0x2985010)=(uintptr_t)&dr_battle_entry;
                *(volatile uintptr_t*)(base+0x2985018)=(uintptr_t)&dr_finalized_entry;
                return;
            }
        }
        uint64_t next=info.address+info.size;if(next<=at){log_line("DRSurprise v3: main not found in memory map");return;}at=next;
    }
}
