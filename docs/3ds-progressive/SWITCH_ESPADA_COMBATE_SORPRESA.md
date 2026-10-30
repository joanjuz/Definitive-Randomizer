> **Actualización 22/10/2026:** este documento conserva la investigación de la fase anterior. El adaptador y el módulo NSO propio ya están implementados; la prueba jugable sigue pendiente. Estado actual: [integración del prototipo](SWITCH_ESPADA_COMBATE_SORPRESA_INTEGRACION.md). Las afirmaciones siguientes de «no conectado» corresponden al estado histórico.

# Espada: implementación del combate sorpresa — núcleo nativo

21 de octubre de 2026 (America/Guatemala) · rama `feature/switch-swsh-prototype`.

**Trabajo parcial: todavía no hay un módulo conectado al juego ni un mod nuevo jugable.** Se implementaron el núcleo nativo de selección y el preflight del ejecutable. Las pruebas de selección, compilación ARM64 y firmas no acreditan integración, generación correcta del Pokémon, captura ni estabilidad dentro del juego. El mod anterior de objetos/entrenadores no se ha reemplazado.

## Núcleo implementado

`native/switch/surprise/policy.c` y `policy.h`: C11 freestanding, sin heap, libc ni llamadas al juego. ABI comprobada con assertions; cabecera compatible con adaptadores C++. Compilado en host Windows para ejecutar las pruebas reales del código C y como ELF64 AArch64 para el futuro módulo. La selección:

- Interviene solo en encuentros salvajes visibles. Entrenadores, ocultos y guionizados devuelven estado de bypass y conservan especie/forma/nivel de entrada.
- Recibe un pool ordenado y único de especies base presentes. Valida límites 1–898 y evita pesos por duplicados. La pertenencia al juego la tendrá que comprobar el generador del pool, no se inventa desde el núcleo C.
- Excluye siempre la especie visible si existe alternativa y conserva nivel. La forma seleccionada es 0.
- Sortea una sola vez por token de encuentro. Las consultas repetidas conservan especie/forma/nivel y no consumen RNG adicional. Un token o contexto incompatible con una batalla activa se rechaza.
- Mantiene una secuencia SplitMix64 con elección acotada sin sesgo, versión de algoritmo 1. La semilla reproduce la secuencia para idéntico orden de encuentros/pool; no se promete reproducibilidad después de reiniciar o cambiar el orden sin persistencia adicional.
- Terminar una batalla limpia solo el token correspondiente y conserva la secuencia RNG. No puede terminar la batalla otro token. Sin alternativa devuelve estado explícito, sin bucle ni consumo de RNG.

No se modifican modelos del mapa ni memoria del juego desde esta capa. No es thread-safe; el adaptador debe comprobar propiedad de hilo y sincronizar si hace falta. No debe tomar el bypass como una instrucción para reescribir datos.

## Preflight implementado

`pk3DS.Core/NX/SwordSurprisePreflight.cs` valida exclusivamente Build ID de **Espada 1.3.2**, firma NSO0, límites del segmento .text, longitud del perfil, LZ4 y SHA-256 del segmento si lo declara NSO. Después coteja las 12 preimágenes conocidas. Una coincidencia parcial es un fallo del conjunto. `AuditText` examina solo bytes y no demuestra identidad de un archivo NSO: para un ejecutable se debe usar `Audit`.

Runner en lectura: `--sword-surprise-audit <extracción>` abre el proyecto, comprueba que sea Espada, ejecuta el preflight y verifica que el origen no cambió. No emite parches.

Extracción real facilitada: **12/12 firmas coinciden**, incluido hash NSO válido. SHA-256 de .text: `82A40B8D3C334DBA1B5D9D1F2F99D5BA6A04D0B1953A069C39C3E9097264A37E`.

## Investigación del adaptador

La referencia original [Lincoln-LM/swsh-mods-exl](https://github.com/Lincoln-LM/swsh-mods-exl/blob/master/source/program/patches/randomizer.hpp) modifica los slots y los spawns al generarlos: no implementa por sí sola separación visual/batalla. El contrato de [PokeFeurRandomSWSH](https://github.com/JonatanUHC/PokeFeurRandomSWSH/blob/main/contract/hook-baseline-epee-1.3.2.json) contiene 12 entradas del módulo randomizer más reciente, pero su árbol público examinado no incluye el `research/.../randomizer.hpp` que cita. No se importaron ni ejecutaron módulos precompilados de ese proyecto.

Se desensamblaron en lectura los candidatos del ejecutable suministrado. Observaciones, **no contrato completo validado**:

- En `0xD609C4` se almacenan w8/w9 con escrituras de 16 bits en un destino x20+0xA0/+0xA2, seguido de más campos. Cambiar solo esas escrituras no demuestra coherencia del Pokémon generado.
- Antes del candidato `0xEA2868` se observan copias de bloques a x24+0xA80 y x24+0x900. El candidato lee x21+0x4C y almacena un valor en x24+0x8A0. Esto obliga a distinguir las copias y su uso antes de elegir cuál modificar; la apariencia y la generación de combate no deben confundirse.
- La referencia antigua documenta OverworldSpec con especie, forma, nivel, naturaleza, habilidad, movimientos, IV y semilla. No se reutilizó ese layout para escribir memoria: quedan por validar su vigencia y el momento adecuado de regeneración.

El código propio de selección puede enlazarse con un adaptador, pero **ese adaptador todavía no está escrito ni conectado**. Lo pendiente es identificar la ruta que crea el Pokémon de batalla, regenerar sus campos dependientes, conservar el resultado hasta la captura/fin y comprobar que no se toca entrenadores. Después habrá que enlazar/empaquetar el módulo NSO, resolver la carga/NPDM sin reemplazos a ciegas, exportar con hash y preparar la prueba jugable. No hace falta que el usuario aporte Escudo para continuar con Espada.

## Pruebas ejecutadas

- `native/switch/surprise/Test-NativePolicy.ps1 -ZigPath <zig-local> -OutputDirectory <temporal-nuevo>`: **20 comprobaciones nativas aprobadas**, compilación C11 de host y AArch64 con `-Wall -Wextra -Werror`. La secuencia de 10000 encuentros excluye el modelo visible; las primeras ocho selecciones con semilla 123 tienen vector golden.
- Verificación ELF64, máquina AArch64 y objeto sin símbolos externos pendientes. Un objeto ELF no es un módulo NSO cargable.
- `dotnet build 3DS.Progressive.Tests/3DS.Progressive.Tests.csproj -c Release --no-incremental --nologo -v:q -bl:<temporal>/build-{}.binlog`: **0 errores, 88 advertencias en archivos anteriores**; ninguna advertencia en los archivos nuevos.
- `--swsh-only`: **198 aprobadas** (incluidas nueve comprobaciones nuevas de preflight).
- `--swsh-ui-only`: **26 aprobadas**, componentes fuera de pantalla; no se cambió UI.
- `--synthetic-only`: **3960 regresiones aprobadas**, ejecutadas después de terminar las compilaciones .NET.
- `--sword-surprise-audit`: **12/12 firmas reales** y origen intacto.
- `git diff --check`: aprobado.

| Requirement | Evidence |
|---|---|
| Seleccionar otra especie al entrar | `Surprise_KirliaBecomesDifferentBaseSpeciesAtSameLevel`, `Surprise_AlwaysExcludesVisibleSpecies` (solo política nativa) |
| No cambiar de especie dentro de una batalla | `Surprise_RepeatedQueriesNeverReroll`, `Surprise_RejectsOverlappingBattle` |
| Preservar entrenadores y otros contextos en la política | `Surprise_BypassesTrainersWithoutConsumingRng`, `Surprise_BypassPreservesOriginalForm`, `Surprise_TrainerCannotOverwriteActiveWildCache` |
| Semilla estable y sorteos independientes sucesivos | `Surprise_Seed123GoldenSequence`, `Surprise_SeedReproducesEncounterSequence`, `Surprise_SameVisibleModelCanYieldDifferentBattles` |
| Rechazar ejecutables o firmas incompatibles | `Surprise_PreflightRejectsOtherBuildsIncludingShield`, `Surprise_PreflightDoesNotAcceptPartialSignatureSet`, `Surprise_PreflightRejectsTextHashMismatch` |
| Mantener el ejecutable en lectura | `Surprise_PreflightNeverWritesExecutable` y auditoría real |

Archivos nuevos: `native/switch/surprise/{policy.h,policy.c,policy_tests.c,Test-NativePolicy.ps1,README.md}`, `pk3DS.Core/NX/SwordSurprisePreflight.cs`, `3DS.Progressive.Tests/SwordSurpriseChecks.cs` y este documento. Modificados: `3DS.Progressive.Tests/Program.cs` y la hoja de ruta. No se modificaron motores 3DS, ROMs originales, guardados, mods instalados ni distribuciones existentes. No se hicieron commits ni push.

Herramientas de validación (Zig 0.13.0 y Capstone) instaladas solo en la carpeta temporal de esta tarea; no se cambió ningún SDK global. Estado interno de pruebas y binlogs fuera del repositorio. Skills aplicadas: pk3ds-progressive-safety, dotnet-test/code-testing, dotnet-test/run-tests, dotnet-msbuild/binlog-generation; no existe agente test-engineer invocable, se trabajó sin delegación. Escudo e interacción jugable pendientes; no se afirmaron como validados.
