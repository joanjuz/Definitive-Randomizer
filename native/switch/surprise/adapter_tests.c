/* SPDX-License-Identifier: GPL-3.0-or-later */
#include "adapter.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
static unsigned checks;
static void check(int ok,const char *name){if(!ok){fprintf(stderr,"FAIL %s\n",name);exit(1);}printf("PASS %s\n",name);checks++;}
static void put16(unsigned char*p,unsigned v){p[0]=(unsigned char)v;p[1]=(unsigned char)(v>>8);}
static unsigned get16(const unsigned char*p){return p[0]|p[1]<<8;}
int main(void){
 const uint16_t pool[]={1,2,3,281,810,898};unsigned char object[0x900]={0},out[0x120],original[0x120],saved[0x900];
 unsigned char *spec=object+DR_OBJECT_SPEC;spec[0]=25;spec[1]=1;spec[6]=25;spec[0x50]=1;object[DR_OBJECT_ROUTE]=1;object[DR_OBJECT_ROUTE+1]=0;
 memset(out,0xA5,sizeof(out));put16(out+0xA0,281);put16(out+0xA2,0);put16(out+0xA4,25);memcpy(original,out,sizeof(out));memcpy(saved,object,sizeof(object));
 check(dr_adapter_initialize(123,pool,6),"Adapter_ValidPresentPoolInitializes");
 check(!dr_adapter_copy(object,sizeof(object),out,sizeof(out))&&!memcmp(out,original,sizeof(out)),"Adapter_UnapprovedObjectsRemainUntouched");
 dr_adapter_note_basic(spec,DR_SPEC_SIZE);check(dr_adapter_copy(object,sizeof(object),out,sizeof(out))&&get16(out+0xA0)!=281,"Adapter_BasicSpawnReceivesDifferentBattleSpecies");
 unsigned choice=get16(out+0xA0);check(!memcmp(object,saved,sizeof(object)),"Adapter_MapObjectAndVisibleModelAreNeverWritten");
 check(get16(out+0xA2)==0&&get16(out+0xA4)==25&&get16(out+0xAA)==255&&get16(out+0xAC)==254,"Adapter_BaseFormLevelAndBuilderSentinels");
 int scoped=1;for(unsigned i=0;i<sizeof(out);i++)if(out[i]!=original[i]&&!(i>=0xA0&&i<0xA4)&&!(i>=0xAA&&i<0xAE)&&!(i>=0xD0&&i<0xD8))scoped=0;
 check(scoped,"Adapter_OnlyIdentityGenderAbilityAndMoveInputsChange");
 check(get16(out+0xD0)==0&&get16(out+0xD2)==0&&get16(out+0xD4)==0&&get16(out+0xD6)==0,"Adapter_ClearsOldMovesForNativeLevelLearnset");
 memcpy(out,original,sizeof(out));check(dr_adapter_copy(object,sizeof(object),out,sizeof(out))&&get16(out+0xA0)==choice,"Adapter_RepeatedCopiesKeepSpeciesForSameSpawn");
 dr_adapter_note_gimmick(spec,DR_SPEC_SIZE);memcpy(out,original,sizeof(out));check(!dr_adapter_copy(object,sizeof(object),out,sizeof(out))&&!memcmp(out,original,sizeof(out)),"Adapter_GimmickRevocationProtectsScriptedSpawns");
 dr_adapter_note_basic(spec,DR_SPEC_SIZE);object[DR_OBJECT_ROUTE]=0;object[DR_OBJECT_ROUTE+1]=0;
 check(!dr_adapter_copy(object,sizeof(object),out,sizeof(out))&&!memcmp(out,original,sizeof(out)),"Adapter_UnobservedRouteBypasses");
 memcpy(object,saved,sizeof(object));check(!dr_adapter_copy(object,1,out,sizeof(out))&&!dr_adapter_copy(object,sizeof(object),out,1),"Adapter_TruncatedBuffersBypassWithoutWrites");
 put16(out+0xA0,1);check(!dr_adapter_copy(object,sizeof(object),out,sizeof(out)),"Adapter_UnexpectedOutputIdentityBypasses");
 check(!dr_adapter_initialize(1,pool,1),"Adapter_InsufficientPoolRejected");
 uint32_t reason=99;
 check(dr_adapter_initialize(123,pool,6),"Adapter_DiagnosticReset");
 memcpy(object,saved,sizeof(object));memcpy(out,original,sizeof(out));
 check(!dr_adapter_copy_diagnostic(object,sizeof(object),out,sizeof(out),&reason)&&reason==9&&!memcmp(out,original,sizeof(out)),"Adapter_DiagnosticReportsMissingApprovalWithoutWrites");
 dr_adapter_note_basic(spec,DR_SPEC_SIZE);
 for(unsigned gate=2;gate<=7;gate++){
  memcpy(object,saved,sizeof(object));memcpy(out,original,sizeof(out));
  if(gate==2){object[DR_OBJECT_ROUTE]=0;object[DR_OBJECT_ROUTE+1]=0;}
  if(gate==3){spec[0]=0;spec[1]=0;}
  if(gate==4)spec[6]=0;
  if(gate==5)put16(out+0xA0,1);
  if(gate==6)put16(out+0xA2,1);
  if(gate==7)put16(out+0xA4,1);
  unsigned char unchanged[sizeof(out)];memcpy(unchanged,out,sizeof(out));
  char name[100];snprintf(name,sizeof(name),"Adapter_DiagnosticRejectGate%uPreservesOutput",gate);
  check(!dr_adapter_copy_diagnostic(object,sizeof(object),out,sizeof(out),&reason)&&reason==gate&&!memcmp(out,unchanged,sizeof(out)),name);
 }
 memcpy(object,saved,sizeof(object));memcpy(out,original,sizeof(out));
 check(!dr_adapter_copy_diagnostic(object,1,out,sizeof(out),&reason)&&reason==1,"Adapter_DiagnosticReportsTruncatedInput");
 check(dr_adapter_copy_diagnostic(object,sizeof(object),out,sizeof(out),&reason)&&reason==0,"Adapter_DiagnosticReportsAppliedChange");
 /* Captured v5 Yamper spec and route marker, not an invented species/model equality. */
 const uint32_t captured[22]={0x343,0x000E0000,0,0xFFFF,0,0,0,0,0,0,0,0,0,0xFFFF0000,0xFFFFFFFF,0xFFFFFFFF,0xFFFF,0,0xFF,0,0,0};
 memset(object,0,sizeof(object));object[DR_OBJECT_ROUTE]=1;memcpy(spec,captured,sizeof(captured));
 memset(out,0,sizeof(out));put16(out+0xA0,835);put16(out+0xA4,14);
 check(dr_adapter_initialize(123,pool,6),"Adapter_RecordedEncounterInitializes");dr_adapter_note_basic(spec,DR_SPEC_SIZE);
 check(dr_adapter_copy_diagnostic(object,sizeof(object),out,sizeof(out),&reason)&&reason==0&&get16(out+0xA0)!=835&&get16(out+0xA4)==14,"Adapter_RecordedYamperMarkerIsNotSpeciesId");
 memcpy(spec,captured,sizeof(captured));put16(spec,710);put16(spec+4,1);spec[6]=15;
 put16(out+0xA0,710);put16(out+0xA2,1);put16(out+0xA4,15);dr_adapter_note_basic(spec,DR_SPEC_SIZE);
 check(dr_adapter_copy_diagnostic(object,sizeof(object),out,sizeof(out),&reason)&&reason==0&&get16(out+0xA2)==0,"Adapter_RecordedAlternateFormDoesNotUseAdjacentMarkerAsForm");
 put16(out+0xA0,710);put16(out+0xA2,1);put16(out+0xA4,15);object[DR_OBJECT_ROUTE]=2;
 unsigned char untouched[sizeof(out)];memcpy(untouched,out,sizeof(out));
 check(!dr_adapter_copy_diagnostic(object,sizeof(object),out,sizeof(out),&reason)&&reason==2&&!memcmp(untouched,out,sizeof(out)),"Adapter_UnknownRouteMarkerRemainsUntouched");
 /* Match the exact mutable fields seen between v6 basic and battle logs. */
 memset(object,0,sizeof(object));object[DR_OBJECT_ROUTE]=1;memcpy(spec,captured,sizeof(captured));
 memset(out,0,sizeof(out));put16(out+0xA0,835);put16(out+0xA4,14);
 dr_adapter_initialize(123,pool,6);dr_adapter_note_basic(spec,DR_SPEC_SIZE);
 spec[8]=2;put16(spec+0xC,3);spec[0x10]=1;spec[0x14]=1;spec[0x18]=2;spec[0x4B]=1;spec[0x50]=0x11;
 check(!dr_adapter_copy_diagnostic(object,sizeof(object),out,sizeof(out),&reason)&&reason==9,"Adapter_FinalizedMutationRequiresMatchingRefresh");
 unsigned char copy[88];memcpy(copy,spec,sizeof(copy));dr_adapter_note_finalized(copy,sizeof(copy));
 check(!dr_adapter_copy_diagnostic(object,sizeof(object),out,sizeof(out),&reason)&&reason==9,"Adapter_FinalizerRejectsUnrelatedSourcePointer");
 dr_adapter_note_finalized(spec,DR_SPEC_SIZE);
 check(dr_adapter_copy_diagnostic(object,sizeof(object),out,sizeof(out),&reason)&&reason==0&&get16(out+0xA0)!=835,"Adapter_FinalizerAcceptsMutatedFieldsOfApprovedSource");
 dr_adapter_note_gimmick(spec,DR_SPEC_SIZE);dr_adapter_note_finalized(spec,DR_SPEC_SIZE);put16(out+0xA0,835);
 check(!dr_adapter_copy_diagnostic(object,sizeof(object),out,sizeof(out),&reason)&&reason==9,"Adapter_FinalizerCannotReviveRevokedSpawn");
 printf("Completed %u native adapter checks; game execution NOT tested.\n",checks);return 0;
}
