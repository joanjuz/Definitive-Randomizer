# Historial: disponibilidad del botón Deshacer tras comprobar archivos

Base: `main` actualizado desde `origin/main`, `38e2cc122eac5ba0325ce3e6056343bd7087b0d6`. Rama `fix/history-undo-button`. Cambio exclusivamente de presentación y pruebas WPF; sin cambio de versión, publicación ni modificación de RestorePoints, FileTransaction, RecoveryJournal, UiProgress o los contratos de historial/Global Template.

## Problema reproducido

Al cargar Historial, `VerifyAsync()` toma `UiProgress.BlockInteraction()`. Sus llamadas a `Fill()` reconstruyen el botón mientras `IsInteractionBlocked` todavía es true. Su valor inicial IsEnabled queda false y no se recalcula cuando se libera el bloqueo. El punto de restauración es válido y la acción de Ctrl+K usa otra ruta de interfaz.

La regresión crea un binario temporal `[0]`, guarda `[1]` mediante el historial y carga HistoryView en un HwndSource WPF fuera de pantalla. Se bombea el Dispatcher hasta terminar la comprobación automática. Sobre el código anterior pasan `HistoryInspectionUi_LoadedChecksActualFiles` y `HistoryUndoUi_ControlHasValidRestorePoint`, pero falla **`HistoryUndoUi_AutomaticVerificationEnablesValidUndo`**, con salida `-532462766` por la excepción del runner. No hace falta ROM, proyecto real ni ninguna pausa temporal para reproducirlo.

## Corrección

HistoryView escucha `InteractionLockChanged` mientras está cargada. Durante una operación deshabilita el botón sin releer archivos que podrían estar retenidos por esa operación. Al liberarse el último bloqueo, vuelve a consultar la elegibilidad del punto seleccionado y actualiza IsEnabled y su explicación. Los cambios notificados desde otro hilo se aplican por Dispatcher y consultan el estado actual del bloqueo.

Se conservan el botón y los demás controles al actualizar esta disponibilidad: no se reconstruye el panel de detalles, no se colapsan sus detalles técnicos y no se sustituye el control que mantiene el foco. `Fill()` sigue usando su comportamiento existente cuando se cambia la selección o se verifica el historial.

Los errores de lectura o permisos se convierten en una razón para deshabilitar el botón en la vista; no se cambian las validaciones transaccionales. El handler de clic también rechaza actuar durante un bloqueo. Loaded evita suscripciones duplicadas y Unloaded retira la suscripción estática y mantiene la cancelación existente de VerifyAsync.

## Casos de regresión

Se añaden 19 comprobaciones a `HistoryInspectionChecks.RunUi`, ejecutadas por la opción existente `--rebuild-marts-ui-only`, que ya forma parte del CI. No se añaden flags, hooks de producción ni cambios en el runner.

| Comportamiento | Caso exacto |
|---|---|
| Punto válido y botón habilitado tras la comprobación automática | `HistoryUndoUi_ControlHasValidRestorePoint`, `HistoryUndoUi_AutomaticVerificationEnablesValidUndo` |
| Deshabilitado mientras VerifyAsync está activo | `HistoryUndoUi_DisabledWhileVerificationIsActive` |
| Un clic emitido bajo bloqueo no modifica el archivo | `HistoryUndoUi_BlockedClickDoesNotWrite` |
| El fin de la comprobación no libera una operación exterior | `HistoryUndoUi_InnerCompletionKeepsOuterOperationDisabled`, `HistoryUndoUi_LastUnlockEnablesValidUndo` |
| Cambio externo durante otra operación | `HistoryUndoUi_UnlockRechecksExternalChanges` |
| Comprobación manual con punto válido/inválido | `HistoryUndoUi_ManualVerificationEnablesValidUndo`, `HistoryUndoUi_ExternalChangeRemainsDisabledAfterVerification` |
| Archivo ilegible y posterior liberación | `HistoryUndoUi_UnreadableTargetStaysDisabledOnUnlock`, `HistoryUndoUi_ReadableTargetCanEnableAgain` |
| Conservación de controles y estado desplegado | `HistoryUndoUi_UnlockPreservesControlsAndExpandedDetails` |
| Bloqueo/desbloqueo desde otro hilo | `HistoryUndoUi_WorkerLockUpdatesOnDispatcher`, `HistoryUndoUi_WorkerUnlockEnablesOnDispatcher` |
| Ciclo Loaded/Unloaded sin suscripciones estáticas acumuladas | `HistoryUndoUi_LoadSubscribesOnce`, `HistoryUndoUi_UnloadRemovesStaticHandler`, `HistoryUndoUi_ReloadEnablesWithoutDuplicateSubscription`, `HistoryUndoUi_FinalUnloadReleasesStaticHandler` |
| Clic real: restaura `[0]` y registra UndoOf | `HistoryUndoUi_EnabledButtonRestoresAndRecordsUndo` |

El caso de verificación activa usa el gate existente del historial, retenido por un worker con eventos explícitos. Esto mantiene la comprobación pendiente mientras se examina el botón. Los límites de espera solo son watchdogs. Se conservan las comprobaciones anteriores de cancelación al descargar, errores de inspección y cambios externos; no se debilitan aserciones existentes.

## Comandos y resultados ejecutados

Windows, SDK .NET 10.0.301, Release. Todos los datos son sintéticos y temporales.

```powershell
dotnet build .\3DS.Progressive.Tests\3DS.Progressive.Tests.csproj -c Release -p:UseAppHost=true --nologo -v:q
dotnet .\3DS.Progressive.Tests\bin\Release\net10.0-windows\3DS.Progressive.Tests.dll --rebuild-marts-ui-only
dotnet .\3DS.Progressive.Tests\bin\Release\net10.0-windows\3DS.Progressive.Tests.dll --interaction-lock-only
dotnet .\3DS.Progressive.Tests\bin\Release\net10.0-windows\3DS.Progressive.Tests.dll --visual-redesign-only
dotnet .\3DS.Progressive.Tests\bin\Release\net10.0-windows\3DS.Progressive.Tests.dll --undo-toctou-only
dotnet .\3DS.Progressive.Tests\bin\Release\net10.0-windows\3DS.Progressive.Tests.dll --transaction-races-only
# Regresión general, incluidas las comprobaciones WPF del recorrido predeterminado:
dotnet .\3DS.Progressive.Tests\bin\Release\net10.0-windows\3DS.Progressive.Tests.dll
```

| Comprobación | Resultado |
|---|---|
| Build final incremental | 0 errores, 0 advertencias |
| `--rebuild-marts-ui-only` | 88 PASS, salida 0, incluidos los 19 casos nuevos |
| `--interaction-lock-only` | 31 PASS, salida 0 |
| `--visual-redesign-only` | 187 PASS, salida 0; sin errores de binding detectados |
| `--undo-toctou-only` | 6 PASS, salida 0 |
| `--transaction-races-only`, fuera del sandbox | 90 PASS, salida 0 |
| Sin argumentos, regresión general sintética y WPF | 4509 PASS, salida 0 |

Dos intentos adicionales de `--transaction-races-only` dentro del sandbox terminaron en `Harness/control failure: GARC child exit`, el watchdog del proceso hijo, con salida `-532462766`. No completaron la suite ni reportaron una invariante de datos fallida. La ejecución fuera del sandbox sí completó 90 comprobaciones; no se modificó ni se omitió ningún caso H2/H3. No se atribuye el mecanismo exacto del timeout sin más evidencia.

Los logs locales ignorados por Git están bajo `3DS.Progressive.Tests/bin/`: `history-undo-before.log`, `history-undo-build.log`, `history-undo-rebuild-marts-ui-only.log`, `history-undo-interaction-lock-only.log`, `history-undo-visual-redesign-only.log`, `history-undo-general.log`, `history-undo-undo-toctou-only.log`, `history-undo-transaction-races-only.log`, `history-undo-transaction-races-retry.log` y `history-undo-transaction-races-unsandboxed.log`.

## Límites

Se verifican controles WPF reales, cargados en una fuente de presentación fuera de pantalla, y el clic con confirmación automatizada. No equivale a una sesión interactiva con teclado/ratón, un proyecto real ni una validación manual de accesibilidad/DPI. La vista no vigila escrituras externas continuamente: se reevalúa al mostrar detalles, comprobar archivos o terminar una operación, y Undo conserva sus comprobaciones existentes al ejecutarse. No se cambia Ctrl+K ni la lógica transaccional H2/H3. La Preview 3 queda intacta.
