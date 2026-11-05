# Reproducción determinista H2/H3

> **Registro histórico.** Describe el código anterior a la corrección. Solución y estado vigente: [CORRECCION_H2_H3_20261104.md](CORRECCION_H2_H3_20261104.md).

Base: `main` en `9f7b5a2c289f73ca4fe5498fc12ff69259a9963b`, 04/11/2026. Investigación del [issue #43](https://github.com/joanjuz/Definitive-Randomizer/issues/43), siguiendo la auditoría del [PR #42](https://github.com/joanjuz/Definitive-Randomizer/pull/42). Rama independiente `test/h2-h3-deterministic-races`. No se cambia producción, la release `v0.14.0-preview.3` ni el PR #42.

## Resultado verificado

Windows, NTFS local, SDK .NET `10.0.301`, Release. Se demuestra pérdida de modificaciones externas en Commit, rollback y Recover. Son tres ventanas distintas; las ocho invariantes fallidas incluyen también éxito silencioso y eliminación del journal. No representan ocho defectos independientes.

| Caso | Before | Proposed | External, confirmado antes de liberar | Observed | Resultado productivo |
|---|---|---|---|---|---|
| H2 Commit, segundo destino | `05060708` | `0F101112` | `E1E2E3E4` | `0F101112` | Éxito, journal eliminado |
| H2 rollback, primer destino | `01020304` | `0B0C0D0E` | `E1E2E3E4` | `01020304` | Solo informa `Synthetic rollback trigger`; journal eliminado |
| H3 primer destino restaurable | `05060708` | `0F101112` | `E1E2E3E4` | `05060708` | Recover termina sin excepción, journal eliminado |
| H3 tras restaurar otro destino | `01020304` | `0B0C0D0E` | `E1E2E3E4` | `01020304` | Recover termina sin excepción, journal eliminado |
| H3 cambio en destino posterior aún no restaurado | `01020304` | `0B0C0D0E` | `E1E2E3E4` | `E1E2E3E4` | Rechazo, otro destino ya restaurado, journal conservado |

El último caso confirma una recuperación parcial diagnosticada: el reintento conserva la edición y el manifest. Tras reconciliar explícitamente los bytes sintéticos con Proposed, los respaldos conservados permiten terminar. No se considera por sí solo una pérdida de modificaciones externas.

La suite imprime bytes y SHA-256 de Before, Proposed, External y Observed, estado de cada destino, excepción, contenido residual y bytes/hash del manifest antes y después. External en el encabezado de una calibración es el patrón de prueba disponible; solo las líneas `external write flushed` y sus verificaciones indican una escritura efectuada. `history.json` es únicamente la ruta permitida al journal; estos casos no generan historial de proyecto.

## Sincronización comprobada

`TransactionOpenBarrier.cs` usa un oplock de nivel 1, handle asíncrono y evento de Windows. El handle se abre con acceso de datos **cero** y compartición Read/Write/Delete. Un handle de lectura como el de Undo no sirve aquí: interfiere con las aperturas exclusivas de los temporales y puede generar falsos rechazos por compartición. Los tres controles `Harness_*` usan la misma instrumentación sin escritor y exigen éxito/restauración y limpieza normales; si fallan, se aborta como fallo del mecanismo.

El actor externo abre el destino con FileShare Read/Write/Delete, escribe los cuatro bytes y hace Flush(true). Esto permite coexistir con el handle de destino que Windows ya puede haber abierto dentro de File.Replace. Lecturas de diagnóstico durante esa pausa usan la misma compartición. No se reutiliza un handle antiguo para fingir una edición del nombre sustituido: el escritor abre la ruta del destino mientras la operación está pausada.

1. **Commit:** el callback productivo existente `afterReplace(1)` arma el oplock sobre el temporal ya preparado del segundo destino. Después se ejecuta la última comprobación del fingerprint de ese destino. La apertura del temporal por File.Replace rompe el oplock; el evento llega con el worker bloqueado. Se verifican Before, escritura externa y relectura External antes de liberar la apertura. Al liberarla, File.Replace pierde la edición externa.
2. **Rollback:** el mismo callback crea un temporal marcador propio, arma el oplock y lanza una excepción. Rollback compara el destino con Proposed y luego abre ese temporal para WriteFlushed. El evento pausa esta apertura; se escribe External y se libera. La restauración sustituye External por Before. No se añade ningún hook productivo.
3. **Recover:** un hijo real ejecuta Begin/Commit sobre tres binarios y sale con código 91 desde `afterReplace(2)`, sin ejecutar Dispose, rollback ni finally. Quedan dos destinos Proposed, uno Before y un journal duradero. El padre coloca un marcador y oplock en el temporal de un destino ya sustituido. Recover valida todo el conjunto, vuelve a comparar ese destino y abre el temporal para restaurar. El evento delimita la ventana y permite introducir External. El caso múltiple pausa el segundo destino que se restaura, después de confirmar que el anterior ya volvió a Before.
4. **Recuperación parcial:** se pausa la primera restauración y se edita el destino que se comprobará después. Se confirma el rechazo posterior, restauración parcial, conservación del journal y reintento sin pérdida.

La secuencia se deduce de esos puntos de apertura únicos en el código productivo y se confirma con evento completado, worker no completado, bytes anteriores y escritura/relectura externas antes de liberar. No se basa en la velocidad de CPU, polling, Thread.Sleep ni Task.Delay. Watchdogs de 15/20 segundos solo detectan bloqueos. Si el worker no termina tras liberar la barrera, el proceso sale con código 3 y conserva los temporales para revisión, sin borrarlos mientras siguen en uso.

Los datos están en una raíz temporal única `transaction-races-<guid>` y son exclusivamente sintéticos. El modo hijo exige raíz bajo TEMP, nombre de padre propio, marcador de propiedad y los bytes iniciales esperados. Los metadatos JSON y respaldos los genera el journal real. La limpieza borra únicamente esa raíz; los logs redirigidos fuera de ella permanecen disponibles.

## Runner y comandos ejecutados

```powershell
dotnet build .\3DS.Progressive.Tests\3DS.Progressive.Tests.csproj -c Release -p:UseAppHost=true --nologo -v:q
dotnet .\3DS.Progressive.Tests\bin\Release\net10.0-windows\3DS.Progressive.Tests.dll --transaction-races-only
dotnet .\3DS.Progressive.Tests\bin\Release\net10.0-windows\3DS.Progressive.Tests.dll --editor-modules-only
dotnet .\3DS.Progressive.Tests\bin\Release\net10.0-windows\3DS.Progressive.Tests.dll --undo-toctou-only
```

- Build final: correcto, 0 errores y 0 advertencias emitidas en la compilación incremental. El primer intento quedó bloqueado por acceso del sandbox a NuGet.Config; se compiló con elevación. Se corrigieron dos errores de nulabilidad de las pruebas durante la implementación.
- `--transaction-races-only`: **Passed: 18, Failed: 8, Skipped: 0**, en tres ejecuciones consecutivas de la versión final. Cada ejecución termina con la excepción agregada del runner, código Windows `-532462766` (`0xE0434352`), porque las propiedades de conservación fallan. El bucle local de repetición registró el código individual de cada ejecución; su propio código 0 no significa que la suite pasara.
- `--editor-modules-only`: 408 comprobaciones correctas, salida 0, incluidas las verificaciones existentes de RecoveryChecks.
- `--undo-toctou-only`: 6 comprobaciones correctas, salida 0. Su ventana de preparación permanece protegida; no sustituye las pruebas nuevas.
- Logs locales, ignorados por Git: `3DS.Progressive.Tests/bin/h2h3-build.log`, `h2h3-verified-1.log`, `h2h3-verified-2.log`, `h2h3-verified-3.log`, `h2h3-editor-modules.log` y `h2h3-undo.log`.

El modo `--interrupt-transaction-races <raíz>` es interno, no una suite independiente. Se registra en catálogo/ayuda por coherencia con Program.cs. La suite de carreras queda fuera de los recorridos predeterminados y de CI; falla deliberadamente sobre las propiedades pendientes de corregir. No se afirma una corrección ni se convierten las pérdidas actuales en resultados esperados exitosos.

## Casos e invariantes

| Requisito | Casos exactos |
|---|---|
| Barrera no interfiere | `Harness_CommitBarrierAllowsSuccessWithoutWriter`, `Harness_RollbackBarrierAllowsRestoreWithoutWriter`, `Harness_RecoveryBarrierAllowsRestoreWithoutWriter` |
| Commit conserva externo y no comunica éxito tras pérdida | `H2_CommitPreservesExternalEditAfterFinalHash`, `H2_CommitDoesNotSilentlySucceedAfterExternalLoss` (fallan) |
| Rollback conserva externo/evidencia | `H2_RollbackPreservesExternalEditAfterComparison`, `H2_RollbackRetainsEvidenceIfExternalEditLost` (fallan) |
| Rollback informa fallo y respeta destinos no reemplazados | `H2_RollbackReportsOriginalFailure`, `H2_RollbackLeavesUnreplacedTargetsUntouched` |
| Recover conserva externo/evidencia | `H3_SinglePreservesExternalEditAfterFinalHash`, `H3_SingleRetainsJournalIfExternalEditLost`, `H3_MultiplePreservesExternalEditAfterFinalHash`, `H3_MultipleRetainsJournalIfExternalEditLost` (fallan) |
| Recover respeta destino nunca confirmado | `H3_SingleLeavesNeverCommittedTargetUntouched`, `H3_MultipleLeavesNeverCommittedTargetUntouched` |
| Recuperación parcial y reintentos | `H3_PartialRecoveryRejectsLaterConflictAndRetainsJournal`, `H3_PartialRetryPreservesConflictAndManifest`, `H3_ReconciledPartialRecoveryCanFinish` |
| Controles de Commit/rollback | `H2_ControlNormalCommit`, `H2_ControlExternalBeforePreflight`, `H2_ControlNormalRollback`, `H2_ControlExternalBeforeRollbackComparison` |
| Controles de Recover | `H3_ControlExternalBeforePreflight`, `H3_ControlCorruptBackupRejectedBeforeWrites`, `H3_ControlNormalInterruptedRecovery`, `H3_ControlRecoveryIdempotent` |

## Límites y siguiente fase

Evidencia local Windows/NTFS con escritor externo en otro hilo y procesos hijos para las interrupciones reales. No se ensayan ROMs, extracciones, partidas, almacenamiento remoto, FAT/exFAT/ReFS, múltiples procesos recuperadores, symlinks, cortes de alimentación ni todas las variantes de renombrado/borrado. Las pausas alargan ventanas reales para hacerlas repetibles; no miden su frecuencia durante el uso habitual.

No se ha aplicado ninguna corrección. Una futura corrección debe hacer pasar estas invariantes, conservar los controles y revisar el contrato de handles/reemplazo y la gestión de estados parciales. Añadir otra comparación de hash no demuestra por sí mismo el cierre de las ventanas. No se hace merge, publicación ni cambio de release en esta fase.
