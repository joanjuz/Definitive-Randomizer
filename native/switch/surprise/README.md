# Sword surprise-battle native prototype

Implemented for Sword 1.3.2, pending actual gameplay validation. Shield is excluded. This directory builds our own loadable NSO; it contains no Nintendo executable or third-party precompiled module.

- `policy.c`: freestanding deterministic species selection, excludes the visible species and uses present base species.
- `adapter.c`: tracks approved basic spawns, revokes gimmick spawns, changes only the battle-copy identity and builder inputs. The map object is read-only. Atomic try-lock bypasses contention. Selection remains stable for the same generated visible object, including re-entry while it persists.
- `module.c`, `module.S`, `module.ld`: MOD0/DT_INIT module, QueryMemory main discovery, preserved ARM64 registers/flags, four wrappers and original-function trampolines. Callback pointers occupy 32 added BSS bytes in main; zero callbacks fall back to original functions. There are no runtime RX writes or NPDM replacements.
- `build_runtime.py`: Zig 0.13.0 compilation/linking, ELF checks and hashed NSO conversion. Reproducible output is embedded as `3DS.Progressive.App/Assets/Switch/sword-surprise.nso`. Configuration is inserted into our module and its segment hash/Build ID updated during preview.
- `adapter_tests.c`: 13 host checks; `policy_tests.c`: 20 host checks. `Test-NativePolicy.ps1` runs policy tests and checks the ARM64 object. Adapter tests are compiled separately with policy.c and adapter.c using C11, -Wall -Wextra -Werror.
- `test_runtime.py <sword-surprise.elf>`: nine Unicorn ARM64 checks, including initialization, wrappers and preservation of original return state. It simulates relocations/SVC responses and original game function bodies: this is NOT gameplay or actual RTLD validation.

The managed writer pins the exact original text hash and instructions, adds 96 bytes of stubs and patches only four function-entry instructions. The changed main text is stored uncompressed: Ryujinx/LibHac rejects literal-only LZ4 when its stored size exceeds the segment size. Original rodata/data are preserved; the source extraction is never written. Existing subsdk9 is rejected. Export currently permits Ryujinx only.

The native builder gender/ability sentinels and cleared move inputs are based on disassembly. Actual stats, moves, ability, capture identity, hidden/script exclusions and loader compatibility remain manual validation requirements. Do not infer these from component or emulation tests.

See `docs/3ds-progressive/SWITCH_ESPADA_COMBATE_SORPRESA_INTEGRACION.md` for current results and test protocol. The earlier investigation report is historical.

The fourth hook refreshes an already approved basic source after D311C0 completes its mutable properties. It requires matching source pointer and initial identity; it never creates approvals for unknown or revoked sources. Initial/final spec hashes differ in actual gameplay logs, so hashing only before finalization was insufficient. Current native tests: 31 host adapter, 16 ARM64 emulated.
