/* SPDX-License-Identifier: GPL-3.0-or-later */
#include "policy.h"
#include <stdio.h>
#include <stdlib.h>
static unsigned checks;
static void check(int value,const char *name){if(!value){fprintf(stderr,"FAIL %s\n",name);exit(1);}printf("PASS %s\n",name);checks++;}
int main(void) {
    const uint16_t pool[]={1,2,3,281,810,898};dr_surprise_state s;dr_surprise_reset(&s,20261021);
    check(dr_surprise_validate_pool(pool,6),"Surprise_PoolAcceptsSortedBaseIds");
    const uint16_t duplicate[]={1,1},unsorted[]={3,1},zero[]={0},high[]={899};
    check(!dr_surprise_validate_pool(duplicate,2)&&!dr_surprise_validate_pool(unsorted,2)&&!dr_surprise_validate_pool(zero,1)&&!dr_surprise_validate_pool(high,1)&&!dr_surprise_validate_pool(NULL,0),"Surprise_PoolRejectsInvalidIdsAndDuplicates");
    dr_surprise_result form=dr_surprise_begin(&s,7,DR_ENCOUNTER_TRAINER,281,3,25,pool,6);
    check(form.form==3&&form.species==281&&!s.active,"Surprise_BypassPreservesOriginalForm");
    uint64_t start=s.rng;dr_surprise_result r=dr_surprise_begin(&s,1,DR_ENCOUNTER_TRAINER,281,0,25,pool,6);
    check(r.status==DR_SURPRISE_BYPASS&&r.species==281&&r.form==0&&!s.active&&s.rng==start,"Surprise_BypassesTrainersWithoutConsumingRng");
    check(dr_surprise_begin(&s,1,DR_ENCOUNTER_HIDDEN_WILD,281,0,25,pool,6).status==DR_SURPRISE_BYPASS&&!s.active,"Surprise_BypassesHiddenWild");
    check(dr_surprise_begin(&s,1,DR_ENCOUNTER_SCRIPTED,281,0,25,pool,6).status==DR_SURPRISE_BYPASS&&!s.active,"Surprise_BypassesScriptedEncounters");
    r=dr_surprise_begin(&s,1,DR_ENCOUNTER_VISIBLE_WILD,281,0,25,pool,6);
    check(r.status==DR_SURPRISE_OK&&r.species!=281&&r.form==0&&r.level==25,"Surprise_KirliaBecomesDifferentBaseSpeciesAtSameLevel");
    uint16_t selected=r.species;uint64_t rolled=s.rng;
    dr_surprise_result trainer=dr_surprise_begin(&s,55,DR_ENCOUNTER_TRAINER,810,2,40,pool,6);
    check(trainer.status==DR_SURPRISE_BYPASS&&trainer.species==810&&trainer.form==2&&s.token==1&&s.species==selected&&s.rng==rolled,"Surprise_TrainerCannotOverwriteActiveWildCache");
    int stable=1;for(int i=0;i<10000;i++){r=dr_surprise_begin(&s,1,DR_ENCOUNTER_VISIBLE_WILD,281,0,25,pool,6);if(r.species!=selected||s.rng!=rolled)stable=0;}
    check(stable&&dr_surprise_current(&s,1).species==selected,"Surprise_RepeatedQueriesNeverReroll");
    check(dr_surprise_begin(&s,2,DR_ENCOUNTER_VISIBLE_WILD,281,0,25,pool,6).status==DR_SURPRISE_CONFLICT&&s.token==1,"Surprise_RejectsOverlappingBattle");
    dr_surprise_end(&s,2);check(s.active,"Surprise_ForeignEndCannotClearActiveBattle");
    dr_surprise_end(&s,1);check(!s.active&&dr_surprise_current(&s,1).status==DR_SURPRISE_INVALID,"Surprise_EndInvalidatesBattleCache");
    const uint16_t only[]={281};start=s.rng;
    check(dr_surprise_begin(&s,2,DR_ENCOUNTER_VISIBLE_WILD,281,0,25,only,1).status==DR_SURPRISE_NO_ALTERNATIVE&&!s.active&&s.rng==start,"Surprise_OnlyVisibleCandidateDoesNotLoop");
    check(dr_surprise_begin(&s,0,DR_ENCOUNTER_VISIBLE_WILD,281,0,25,pool,6).status==DR_SURPRISE_INVALID&&dr_surprise_begin(&s,2,DR_ENCOUNTER_VISIBLE_WILD,281,0,0,pool,6).status==DR_SURPRISE_INVALID,"Surprise_InvalidTokenAndLevelRejected");
    check(dr_surprise_begin(&s,2,(dr_encounter_kind)99,281,0,25,pool,6).status==DR_SURPRISE_INVALID,"Surprise_RejectsUnknownBattleContext");
    const uint16_t alternative[]={1};dr_surprise_result lone=dr_surprise_begin(&s,2,DR_ENCOUNTER_VISIBLE_WILD,281,0,25,alternative,1);
    check(lone.status==DR_SURPRISE_OK&&lone.species==1,"Surprise_SingleAlternativeIsValid");dr_surprise_end(&s,2);
    dr_surprise_state a,b;dr_surprise_reset(&a,123);dr_surprise_reset(&b,123);int equal=1,different=0,excluded=1;uint16_t first=0;
    const uint16_t golden[]={810,898,3,1,810,2,3,898};int golden_ok=1;
    for(uint64_t token=1;token<=10000;token++){
        dr_surprise_result x=dr_surprise_begin(&a,token,DR_ENCOUNTER_VISIBLE_WILD,281,0,50,pool,6),y=dr_surprise_begin(&b,token,DR_ENCOUNTER_VISIBLE_WILD,281,0,50,pool,6);
        if(token<=8&&x.species!=golden[token-1])golden_ok=0;
        if(x.species!=y.species)equal=0;if(token==1)first=x.species;else if(x.species!=first)different=1;if(x.species==281||x.status!=DR_SURPRISE_OK)excluded=0;
        dr_surprise_end(&a,token);dr_surprise_end(&b,token);
    }
    check(golden_ok,"Surprise_Seed123GoldenSequence");
    check(equal,"Surprise_SeedReproducesEncounterSequence");check(different,"Surprise_SameVisibleModelCanYieldDifferentBattles");check(excluded,"Surprise_AlwaysExcludesVisibleSpecies");
    printf("Completed %u native policy checks; NOT a game integration test.\n",checks);return 0;
}
