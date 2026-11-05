# H2/H3: recuperación de guardados sin journal explícito

Seguimiento del [issue #43](https://github.com/joanjuz/Definitive-Randomizer/issues/43) y del [comentario de cierre del PR #45](https://github.com/joanjuz/Definitive-Randomizer/pull/45#issuecomment-6649345678). Base local `a8d00de`, rama `fix/h2-h3-transaction-races`. No modifica la preview `v0.14.0-preview.3`, los formatos de historial/Global Template ni el esquema del journal. No autoriza merge.

## Fallo reproducido y causa

Los tres llamadores productivos de `FileTransaction.Commit` son `ProjectHistory.Commit`, `ProjectHistory.CommitGlobalTemplate` y `GarcTransaction.Commit`. Los dos primeros aportaban `RecoveryJournal`; el último no. Tampoco se supondrá que futuros llamadores opcionales lo aporten.

Sobre el código previo del PR #45 se añadió una prueba que llama Commit **sin** journal. Tras el primer reemplazo arma un oplock RH sobre el temporal del segundo archivo. El evento confirma la pausa entre los dos renombrados, con el destino ausente y el original retirado. El actor externo escribe y confirma `E1E2E3E4` en ese nombre antes de liberar el rename. El segundo rename y el intento de rollback fallan sin sobrescribir al tercero.

| Before | Proposed | External | Observed | Original retirado |
|---|---|---|---|---|
| `05060708` | `0F101112` | `E1E2E3E4` | `E1E2E3E4` | `05060708`, conservado en `.old.tmp` |

La transacción informa IOException y restaura los demás destinos, pero **no existe manifest persistente** para reconciliar ese estado. Resultado anterior: `Passed: 57, Failed: 1, Skipped: 0`, salida Windows `-532462766`. La invariante fallida es `NoJournal_ConflictHasDurableRecoveryRecord`. El log conserva bytes, SHA-256, rutas y excepción. Es una ausencia de recuperación fiable demostrada, no una pérdida nueva de los bytes originales.

## Solución implementada

- `FileTransaction.Commit` crea automáticamente un journal duradero cuando el llamador no aporta uno. Conserva el mismo motor de handles/renames; no se cambia la garantía de exclusión ya probada. Duplicados se rechazan antes de comenzar y un conjunto vacío no genera metadatos.
- `StandaloneTransactions` determina la raíz común de los destinos y guarda el journal en `%LOCALAPPDATA%/DefinitiveRandomizer/file-transactions/<SHA256-raíz>.pending`. Si existe `PROGRESSIVE_STATE_DIR`, usa `<estado>/file-transactions`. Las pruebas usan estado temporal propio, heredado por sus hijos. El manifest y sus respaldos quedan fuera de RomFS/ExeFS; el lock residual tampoco entra en el rebuild.
- Se reutiliza `RecoveryJournal` versión 1 con sus originales, hashes, token y lease. `Targets` permite descubrir registros que afectan a una raíz, sin convertirlos en historial ni en recetas. La recuperación valida el conjunto y no actúa fuera de la raíz elegida; un journal que también afecta a destinos fuera de ella se rechaza para abrir su raíz común.
- `ProjectHistory.RecoverPending`, usado antes de cargar un proyecto 3DS y al leer/guardar su historial, recupera también los guardados individuales. `GarcTransaction.Commit` realiza esa recuperación antes de leer su origen. Un guardado directo posterior también la intenta antes de comenzar otra transacción.
- Los errores conservan causa y rutas, y añaden la ubicación del registro y el procedimiento de recuperación. Si un nombre está ocupado por bytes ajenos, se rechaza la recuperación sin borrar el tercero, `.old.tmp` ni el journal necesario.
- Diagnóstico de solo lectura, backup y rebuild rechazan un guardado pendiente. No intentan reparar silenciosamente ni empaquetar ese estado. Las nuevas guardas comprueban el estado antes de crear la salida.

No se altera la limpieza productiva de `.old.tmp`: un original retirado por rollback fallido se conserva; la recuperación mantiene un respaldo duradero verificado antes de restaurar y solo limpia el conjunto tras recuperarlo. Las pruebas verifican específicamente la retención del original retirado cuando el destino está vacante/ocupado y tras un segundo cierre forzado. En la recuperación de un destino ya instalado, el motor anterior puede sustituir la copia retirada por los bytes After; el respaldo Before verificado permanece en el journal. No se afirma que cada `.old.tmp` retenga siempre Before durante todos los pasos internos.

## Procedimiento seguro para el usuario

1. No guardar, reconstruir ni borrar temporales tras el error. Conservar también la carpeta del registro indicada en el mensaje.
2. Cerrar los otros editores/procesos que escriban en el proyecto. Sin conflicto, volver a abrir la misma raíz recupera automáticamente.
3. Si sigue habiendo bytes externos en un destino, conservarlos **fuera del proyecto**, verificar su SHA-256 y liberar el nombre solo después de asegurar esa copia. En las pruebas se mueve sin sobrescritura a una ruta sintética nueva y se comprueban sus bytes.
4. Reabrir el proyecto en la aplicación. El journal restaura todo el conjunto a Before y se limpia después de completar la recuperación. Verificar también la copia externa antes de cualquier reconciliación manual posterior.
5. No copiar `.old.tmp` a ciegas ni regenerar el manifest. Si el respaldo está alterado o no se conoce la procedencia de un archivo, conservar todo para revisión.

Los nuevos registros están en el perfil que efectuó el guardado y contienen rutas absolutas. No mover/renombrar la raíz ni eliminar el directorio de estado mientras haya un guardado pendiente. La recuperación de huérfanos anteriores a esta corrección, sin manifest, sigue requiriendo revisión manual; no se inventa su pertenencia a una transacción.

## Pruebas automatizadas

Todas usan binarios generados, eventos y oplocks de Windows, sin ROMs ni proyectos reales. Los límites temporales son watchdogs; no ordenan las carreras. El proceso hijo se termina mediante Kill mientras el oplock mantiene bloqueado el rename, y se comprueba el estado después de su salida antes de recuperar.

| Requisito | Evidencia exacta |
|---|---|
| Conflicto sin journal explícito | `NoJournal_ConflictPreservesExternalOriginalAndOtherTargets`, `NoJournal_ConflictHasDurableRecoveryRecord` |
| Rollback falla ante nombre ocupado | `NoJournal_RollbackConflictPreservesExternalOriginalAndOtherTargets`, `NoJournal_RollbackConflictHasDurableRecoveryRecord` |
| Rechazo/reintento conserva evidencia y explica recuperación | `NoJournal_ConflictRetryKeepsEvidenceAndExplainsRecovery`, `NoJournal_RollbackConflictRetryKeepsEvidence` |
| Reconciliar conserva tercero y restaura originales | `NoJournal_ConflictRecoversAfterExternalIsSaved`, `NoJournal_RollbackConflictRecoversAfterExternalIsSaved` |
| Cuatro destinos, proceso matado entre renames | `NoJournal_KilledBetweenRenamesKeepsOriginalAndPartialSet`, `NoJournal_KilledBetweenRenamesRecoversWholeSet` |
| Conflicto después de Kill | `NoJournal_KilledConflictRetryPreservesExternalAndOld`, `NoJournal_KilledConflictRecoversWholeSetAfterReconciliation` |
| Backup/diagnóstico no limpian el estado pendiente | `NoJournal_KilledBlocksBackupAndDiagnosticWithoutCleanup`, `NoJournal_KilledConflictBlocksBackupAndDiagnosticWithoutCleanup` |
| Otro Kill dentro de Recover | `NoJournal_KilledRecoveryRetainsOldAndJournal`, `NoJournal_KilledRecoveryRetriesWholeSet` |
| Ruta real de código GARC, datos generados | `Garc_ConflictRetainsOriginalAndJournal`, `Garc_ConflictRetryDoesNotDeleteOldOrExternal`, `Garc_ConflictRecoversAfterExternalIsSaved`, `Garc_SaveAfterConflictRecoveryIsVerified` |
| GARC y proceso terminado entre renames | `Garc_KilledBetweenRenamesRetainsOriginalAndJournal`, `Garc_KilledBetweenRenamesRecoversOnOpen`, `Garc_SaveAfterKilledRecoveryIsVerified` |
| Raíz común y separación de proyectos | `NoJournal_SubRootCannotRecoverOtherDirectoryTargets`, `NoJournal_UnrelatedRootDoesNotTouchPendingJournal`, `NoJournal_CommonRootRecoversMultipleDirectories` |
| Compartición con otros procesos | `Garc_SharingReaderRejectsWithoutPartialWrite`, `NoJournal_OtherProcessWriterRejectsWholeSetBeforeChanges`, `NoJournal_SaveSucceedsAfterOtherProcessReleasesHandle` |
| Rebuild y apertura | `NoJournal_RebuildRejectsPendingWithoutDeletingOld`, `NoJournal_DiagnosticOpenLeavesPendingUntouched`, `NoJournal_ProjectOpenRecoversBeforeLoadingGameData` |

El caso de apertura usa una estructura 3DS sintética mínima y verifica que se recupera antes de cargar/validar datos; después la estructura incompleta puede rechazarse. No equivale a abrir satisfactoriamente una extracción real. Las invariantes anteriores del PR se mantienen; no se rebajan para aceptar el estado defectuoso.

## Comandos y resultados

```powershell
dotnet build .\3DS.Progressive.Tests\3DS.Progressive.Tests.csproj -c Release -p:UseAppHost=true --nologo -v:q
dotnet .\3DS.Progressive.Tests\bin\Release\net10.0-windows\3DS.Progressive.Tests.dll --transaction-races-only
dotnet .\3DS.Progressive.Tests\bin\Release\net10.0-windows\3DS.Progressive.Tests.dll --undo-toctou-only
dotnet .\3DS.Progressive.Tests\bin\Release\net10.0-windows\3DS.Progressive.Tests.dll --editor-modules-only
dotnet .\3DS.Progressive.Tests\bin\Release\net10.0-windows\3DS.Progressive.Tests.dll --synthetic-only
```

Windows/NTFS local, .NET 10.0.301, Release. Build incremental: 0 errores, 0 advertencias. Se corrigió durante la implementación un error de compilación de la prueba que intentaba acceder a un campo interno de Core; ahora genera el GARC mediante su API pública de guardado.

| Suite | Resultado posterior |
|---|---|
| `--transaction-races-only` | 90 comprobaciones PASS; resumen interno: 89 casos más la comprobación agregada, 0 fallos |
| `--undo-toctou-only` | 6 PASS, salida 0 |
| `--editor-modules-only` | 408 PASS, salida 0 |
| `--synthetic-only` | 4101 PASS, salida 0 |

Logs locales ignorados bajo `3DS.Progressive.Tests/bin/`: `h2h3-nojournal-before.log`, `h2h3-followup-build.log`, `h2h3-followup-<suite>.log`. El último resultado de cada suite y la repetición de carreras se registran también en el cuerpo del PR #45.

## CI y límites restantes

Al comenzar esta tarea se comprobó que el run anterior `41964456812`, head `a8d00de`, **había concluido SUCCESS**; el comentario que lo mostraba en progreso había quedado desfasado. Ese resultado no valida estos cambios nuevos. El resultado de GitHub Actions del último SHA subido se comprueba después del push y se registra con enlace y SHA en la entrega y en el cuerpo del PR #45.

No declarar H2/H3 completamente cerrados. Sigue pendiente validar una copia aislada de proyecto 3DS real (guardar, deshacer, reabrir y rebuild). No se ensayan cortes de alimentación, eliminación del estado del usuario, movimiento de proyectos pendientes entre perfiles/rutas, FAT/exFAT/ReFS/UNC, antivirus reales, enlaces duros ni escrituras por debajo del sistema de archivos. Se mantienen los límites de ADS/compresión/cifrado y de lectores sin FileShare.Delete del informe anterior. El conjunto no es atómico para observadores y no se impide un guardado ajeno posterior a liberar los handles. Un backup/rebuild ya en curso no adquiere un bloqueo global frente a nuevos guardados iniciados después de su preflight.
