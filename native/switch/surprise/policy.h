/* SPDX-License-Identifier: GPL-3.0-or-later
 * Freestanding selection core for Sword surprise battles. No game-memory access here.
 * The adapter must supply a unique encounter token and an already validated base-species pool.
 */
#ifndef DR_SWORD_SURPRISE_POLICY_H
#define DR_SWORD_SURPRISE_POLICY_H
#include <stdint.h>
#include <stddef.h>
#define DR_SURPRISE_MAX_SPECIES 898u
#define DR_SURPRISE_MAX_POOL 898u
#define DR_SURPRISE_ALGORITHM 1u

typedef enum { DR_ENCOUNTER_VISIBLE_WILD=1, DR_ENCOUNTER_HIDDEN_WILD=2, DR_ENCOUNTER_TRAINER=3, DR_ENCOUNTER_SCRIPTED=4 } dr_encounter_kind;
typedef enum { DR_SURPRISE_OK=0, DR_SURPRISE_BYPASS=1, DR_SURPRISE_NO_ALTERNATIVE=2, DR_SURPRISE_INVALID=3, DR_SURPRISE_CONFLICT=4 } dr_surprise_status;
typedef struct { uint64_t rng, token; uint16_t visible, visible_form, species, form; uint8_t level, active; } dr_surprise_state;
typedef struct { uint16_t species, form; uint8_t level; dr_surprise_status status; } dr_surprise_result;
#ifdef __cplusplus
extern "C" {
#endif
void dr_surprise_reset(dr_surprise_state *state,uint32_t seed);
int dr_surprise_validate_pool(const uint16_t *pool,size_t count);
dr_surprise_result dr_surprise_begin(dr_surprise_state *state,uint64_t token,dr_encounter_kind kind,uint16_t visible,uint16_t visible_form,uint8_t level,const uint16_t *pool,size_t count);
dr_surprise_result dr_surprise_current(const dr_surprise_state *state,uint64_t token);
void dr_surprise_end(dr_surprise_state *state,uint64_t token);
#ifdef __cplusplus
}
#endif
#endif
