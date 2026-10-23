/* SPDX-License-Identifier: GPL-3.0-or-later */
#include "policy.h"
_Static_assert(sizeof(dr_surprise_state)==32,"Unexpected state ABI");
_Static_assert(sizeof(dr_surprise_result)==12,"Unexpected result ABI");
static dr_surprise_result result(uint16_t species,uint16_t form,uint8_t level,dr_surprise_status status) {
    dr_surprise_result value={species,form,level,status};return value;
}
static uint32_t next_u32(dr_surprise_state *state) {
    uint64_t z=(state->rng+=UINT64_C(0x9E3779B97F4A7C15));
    z=(z^(z>>30))*UINT64_C(0xBF58476D1CE4E5B9);
    z=(z^(z>>27))*UINT64_C(0x94D049BB133111EB);
    return (uint32_t)((z^(z>>31))>>32);
}
static uint32_t draw(dr_surprise_state *state,uint32_t count) {
    uint32_t threshold=(0u-count)%count,value;
    do {value=next_u32(state);} while(value<threshold);
    return value%count;
}
void dr_surprise_reset(dr_surprise_state *state,uint32_t seed) {
    if(!state)return;
    state->rng=((uint64_t)seed<<32)^UINT64_C(0x5355525052495345);
    state->token=0;state->visible=0;state->visible_form=0;state->species=0;state->form=0;state->level=0;state->active=0;
}
int dr_surprise_validate_pool(const uint16_t *pool,size_t count) {
    if(!pool||count<1||count>DR_SURPRISE_MAX_POOL)return 0;
    /* Unique sorted IDs keep portable draws stable and make duplicate weighting impossible. */
    for(size_t i=0;i<count;i++)if(pool[i]<1||pool[i]>DR_SURPRISE_MAX_SPECIES||(i&&pool[i]<=pool[i-1]))return 0;
    return 1;
}
dr_surprise_result dr_surprise_current(const dr_surprise_state *state,uint64_t token) {
    if(!state||!state->active||state->token!=token)return result(0,0,0,DR_SURPRISE_INVALID);
    return result(state->species,state->form,state->level,DR_SURPRISE_OK);
}
dr_surprise_result dr_surprise_begin(dr_surprise_state *state,uint64_t token,dr_encounter_kind kind,uint16_t visible,uint16_t visible_form,uint8_t level,const uint16_t *pool,size_t count) {
    if(!state||token==0||visible<1||visible>DR_SURPRISE_MAX_SPECIES||visible_form>255||level<1||level>100)return result(0,0,0,DR_SURPRISE_INVALID);
    if(kind<DR_ENCOUNTER_VISIBLE_WILD||kind>DR_ENCOUNTER_SCRIPTED)return result(0,0,0,DR_SURPRISE_INVALID);
    if(kind!=DR_ENCOUNTER_VISIBLE_WILD)return result(visible,visible_form,level,DR_SURPRISE_BYPASS);
    if(state->active) {
        if(state->token!=token||state->visible!=visible||state->visible_form!=visible_form||state->level!=level)return result(0,0,0,DR_SURPRISE_CONFLICT);
        return dr_surprise_current(state,token);
    }
    if(!dr_surprise_validate_pool(pool,count))return result(0,0,0,DR_SURPRISE_INVALID);
    size_t eligible=0;for(size_t i=0;i<count;i++)if(pool[i]!=visible)eligible++;
    if(eligible==0)return result(visible,visible_form,level,DR_SURPRISE_NO_ALTERNATIVE);
    uint32_t pick=draw(state,(uint32_t)eligible);uint16_t species=0;
    for(size_t i=0;i<count;i++)if(pool[i]!=visible&&pick--==0){species=pool[i];break;}
    state->token=token;state->visible=visible;state->visible_form=visible_form;state->species=species;state->form=0;state->level=level;state->active=1;
    return dr_surprise_current(state,token);
}
void dr_surprise_end(dr_surprise_state *state,uint64_t token) {
    if(!state||!state->active||state->token!=token)return;
    state->active=0;state->token=0;state->visible=0;state->visible_form=0;state->species=0;state->form=0;state->level=0;
}
