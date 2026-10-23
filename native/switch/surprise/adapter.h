/* SPDX-License-Identifier: GPL-3.0-or-later */
#ifndef DR_SURPRISE_ADAPTER_H
#define DR_SURPRISE_ADAPTER_H
#include "policy.h"
#define DR_SPEC_SIZE 0x58u
#define DR_OBJECT_SPEC 0x8A8u
#define DR_OBJECT_ROUTE 0x8A0u /* Observed marker 1 on approved visible spawns; not a species ID. */
#define DR_OUTPUT_MIN 0xD8u
int dr_adapter_initialize(uint32_t seed,const uint16_t *pool,size_t count);
void dr_adapter_note_basic(const void *spec,size_t length);
void dr_adapter_note_finalized(const void *spec,size_t length);
void dr_adapter_note_gimmick(const void *spec,size_t length);
int dr_adapter_copy(const void *object,size_t object_length,void *output,size_t output_length);
int dr_adapter_copy_diagnostic(const void *object,size_t object_length,void *output,size_t output_length,uint32_t *reason);
#endif
