/* SPDX-License-Identifier: GPL-3.0-or-later */
#include "adapter.h"
/* Offsets verified against Sword1.3.2 instructions; gameplay validation remains required. */
typedef struct {uint64_t hash,identity;uintptr_t source,object;uint16_t chosen;uint8_t valid;} approval;
static approval records[512];
static uint16_t candidates[DR_SURPRISE_MAX_POOL];
static size_t candidate_count;
static uint32_t next_record;
static uint64_t next_token;
static dr_surprise_state selection;
static uint32_t busy;
static uint32_t u32(const uint8_t *p){return (uint32_t)p[0]|(uint32_t)p[1]<<8|(uint32_t)p[2]<<16|(uint32_t)p[3]<<24;}
static uint16_t u16(const uint8_t *p){return (uint16_t)(p[0]|p[1]<<8);}
static void put16(uint8_t *p,uint16_t v){p[0]=(uint8_t)v;p[1]=(uint8_t)(v>>8);}
static uint64_t identity(const uint8_t *p){return (uint64_t)u32(p)|((uint64_t)u32(p+4)<<32);}
static uint64_t spec_hash(const uint8_t *p){uint64_t h=UINT64_C(0xCBF29CE484222325);for(size_t i=0;i<DR_SPEC_SIZE;i++)h=(h^p[i])*UINT64_C(0x100000001B3);return h;}
static int enter(void){return __atomic_exchange_n(&busy,1,__ATOMIC_ACQUIRE)==0;}
static void leave(void){__atomic_store_n(&busy,0,__ATOMIC_RELEASE);}
int dr_adapter_initialize(uint32_t seed,const uint16_t *pool,size_t count){
    if(!dr_surprise_validate_pool(pool,count)||count<2)return 0;
    if(!enter())return 0;
    for(size_t i=0;i<512;i++)records[i].valid=0;
    for(size_t i=0;i<count;i++)candidates[i]=pool[i];candidate_count=count;
    next_record=0;next_token=0;dr_surprise_reset(&selection,seed);leave();return 1;
}
void dr_adapter_note_basic(const void *spec,size_t length){
    if(!spec||length<DR_SPEC_SIZE||!candidate_count)return;
    const uint8_t *p=spec;uint32_t id=u32(p);if(id<1||id>898||p[6]<1||p[6]>100)return;
    uint64_t hash=spec_hash(p);if(!enter())return;
    approval *r=&records[next_record++%512];r->hash=hash;r->identity=identity(p);r->source=(uintptr_t)spec;r->object=0;r->chosen=0;r->valid=1;leave();
}
void dr_adapter_note_finalized(const void *spec,size_t length){
    if(!spec||length<DR_SPEC_SIZE||!enter())return;
    uint64_t key=identity(spec);
    for(uint32_t n=0;n<512&&n<next_record;n++){
        approval *r=&records[(next_record-1-n)%512];
        if(r->valid&&r->object==0&&r->source==(uintptr_t)spec&&r->identity==key){r->hash=spec_hash(spec);break;}
    }
    leave();
}
void dr_adapter_note_gimmick(const void *spec,size_t length){
    if(!spec||length<DR_SPEC_SIZE||!enter())return;
    uint64_t hash=spec_hash(spec);for(size_t i=0;i<512;i++)if(records[i].valid&&(records[i].hash==hash||(records[i].object==0&&records[i].source==(uintptr_t)spec)))records[i].valid=0;leave();
}
int dr_adapter_copy(const void *object,size_t object_length,void *output,size_t output_length){return dr_adapter_copy_diagnostic(object,object_length,output,output_length,0);}
int dr_adapter_copy_diagnostic(const void *object,size_t object_length,void *output,size_t output_length,uint32_t *reason){
    if(reason)*reason=1;
    if(!object||!output||object_length<DR_OBJECT_SPEC+DR_SPEC_SIZE||output_length<DR_OUTPUT_MIN||!candidate_count)return 0;
    const uint8_t *obj=object,*spec=obj+DR_OBJECT_SPEC;uint8_t *out=output;
    uint32_t route=u32(obj+DR_OBJECT_ROUTE),species=u32(spec);uint16_t form=u16(spec+4);uint8_t level=spec[6];
    /* Only the observed route marker plus a matching approved basic spec is eligible.
       +8A0/+8A4 are not species/form fields; identity comes from +8A8/+8AC. */
    if(reason)*reason=2;if(route!=1)return 0;
    if(reason)*reason=3;if(species<1||species>898)return 0;
    if(reason)*reason=4;if(level<1||level>100)return 0;
    if(reason)*reason=5;if(u16(out+0xA0)!=species)return 0;
    if(reason)*reason=6;if(u16(out+0xA2)!=form)return 0;
    if(reason)*reason=7;if(u16(out+0xA4)!=level)return 0;
    if(reason)*reason=8;
    uint64_t hash=spec_hash(spec);if(!enter())return 0;approval *r=0;
    for(size_t i=0;i<512;i++)if(records[i].valid&&records[i].hash==hash&&(records[i].object==0||records[i].object==(uintptr_t)object)){r=&records[i];if(r->object)break;}
    if(!r){if(reason)*reason=9;leave();return 0;}
    if(!r->chosen){
        dr_surprise_result result=dr_surprise_begin(&selection,++next_token,DR_ENCOUNTER_VISIBLE_WILD,(uint16_t)species,form,level,candidates,candidate_count);
        if(result.status!=DR_SURPRISE_OK){if(reason)*reason=10;leave();return 0;}r->chosen=result.species;r->object=(uintptr_t)object;dr_surprise_end(&selection,next_token);
    }
    put16(out+0xA0,r->chosen);put16(out+0xA2,0);
    /* Sentinels observed in the native builder: random valid gender/normal ability; zero moves use level learnset. */
    put16(out+0xAA,0xFF);put16(out+0xAC,0xFE);for(size_t i=0;i<4;i++)put16(out+0xD0+2*i,0);
    if(reason)*reason=0;leave();return 1;
}
