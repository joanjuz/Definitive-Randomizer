# Historial contrastado con los archivos y confirmación de Rebuild

Fecha: 19/10/2026. Se conservan las correcciones de estabilidad locales anteriores y el prototipo de compras únicas pendiente. No hay publicación, despliegue, commit ni push de esta revisión.

## Comportamiento

- Se retira el aviso del historial al entrar en Rebuild. Después de elegir el destino se comprueban los archivos y aparece una sola confirmación con el historial contrastado. Se mantiene el aviso final de reconstrucción completada, que informa del resultado y no repite el historial.
- La sección Historial comprueba automáticamente al cargarse y permite repetir con «Comprobar archivos actuales». La lectura se ejecuta fuera del hilo de UI, con bloqueo de interacción y cancelación al descargar la vista.
- El contraste usa SHA-256 del contenido, no nombre/tamaño/fecha. Lee cada archivo registrado una sola vez, con un búfer de 128 KiB, sin cargar la ROM completa en memoria. Se compara la huella final de cada archivo: las ediciones sucesivas legítimas no se rechazan por sus hashes intermedios.
- Si un archivo fue restablecido externamente y después se vuelve a editar, la discontinuidad entre hashes Before/After separa los registros anteriores del segmento actual. Una nueva edición no rehabilita el historial de la ROM anterior.
- Los registros se muestran como coherentes, desfasados o sin comprobación binaria. Archivos ausentes, bloqueados, referencias inseguras, hashes inválidos o un juego distinto no se anuncian como comprobados. Una acción con varios archivos requiere que todos coincidan.
- Rebuild identifica los registros desfasados y no los lista como cambios confirmados en el estado actual. Puede reconstruir los archivos actuales aunque haya historial desfasado: el historial no se vuelve a aplicar.
- Una comprobación fallida elimina las etiquetas de verificación anteriores. Se conserva el registro histórico; no se borra, no se recalculan hashes para forzar coincidencias ni se restauran archivos del juego.

## Alcance y compatibilidad

La serialización del historial, exportación JSON, captura de acciones y reproducción de Global Template/Batch no cambian. El JSON exportado sigue siendo un registro histórico completo, no una certificación del estado actual. Tampoco se filtran automáticamente recetas históricas: conservarlas permite su reutilización intencional. Esta comprobación concierne a la presentación y confirmación del estado del proyecto.

La comprobación solo cubre archivos registrados, no modificaciones externas nunca registradas. Una huella coincidente acredita contenido compatible con el último registro y su cadena; no es una firma criptográfica de la autoría del historial. El mecanismo de recuperación de guardados interrumpidos de `Read()` se conserva.

## Archivos de esta mejora

- `3DS.Progressive.App/Services/ProjectHistory.cs`: declaración parcial, sin cambiar el contrato existente.
- `3DS.Progressive.App/Services/ProjectHistoryInspection.cs`: contraste, segmentos y estados.
- `3DS.Progressive.App/HistoryView.cs`: comprobación asíncrona, estados y cancelación.
- `3DS.Progressive.App/MainWindow.xaml.cs`: retirada del aviso duplicado, preservando las correcciones de cierre pendientes.
- `3DS.Progressive.App/RomRebuildView.cs`: una confirmación después del contraste.
- `3DS.Progressive.Tests/HistoryInspectionChecks.cs`: escenarios binarios y UI fuera de pantalla.
- `3DS.Progressive.Tests/RebuildMartUiChecks.cs`, `Program.cs`: integración de las nuevas comprobaciones.
- `3DS.Progressive.Tests/WpfTestHost.cs`: contexto Dispatcher de WPF para eventos asíncronos del runner.

## Evidencia

| Requirement | Evidence |
|---|---|
| ROM limpiada en la misma carpeta | `HistoryInspection_ReopenedCleanRomWithSameSizeAndDateIsOutOfDate` |
| Cadena de ediciones y restablecimiento | `HistoryInspection_ChecksTerminalHashNotIntermediateEdits`, `HistoryInspection_NewEditAfterResetDoesNotValidateOldSegment` |
| Restablecimiento parcial | `HistoryInspection_PartialResetInvalidatesWholeAction`, `HistoryInspection_PartialResetCheckNeverRestoresFiles` |
| No escribir ni borrar registros | `HistoryInspection_NeverClearsHistoryOrChangesRom`, `HistoryInspection_EmptyDoesNotCreateMetadata` |
| Archivos no comprobables | `HistoryInspection_MissingFileIsNotApplied`, `HistoryInspection_LockedFileIsNotReportedVerified`, `HistoryInspection_InvalidHashIsNotVerified`, `HistoryInspection_NoFileRecordDoesNotProveRomModification` |
| Compatibilidad y referencias | `HistoryInspection_LegacyJournalRemainsReadable`, `HistoryInspection_WrongGameIsNotVerified`, `HistoryInspection_UnsafePathRejected_Absolute`, `HistoryInspection_UnsafePathRejected_Parent` |
| Cancelación | `HistoryInspection_CancellationIsObserved`, `HistoryInspectionUi_UnloadCancelsAndUnlocks` |
| Vista y error | `HistoryInspectionUi_LoadedChecksActualFiles`, `HistoryInspectionUi_RecheckShowsResetWithoutDeletingLog`, `HistoryInspectionUi_FailureClearsPreviousVerifiedLabels` |
| Resumen de Rebuild | `HistoryInspection_RebuildDoesNotListStaleActionsAsApplied`, `HistoryInspection_ExplainsContentCheckAndPreservedJournal` |
| Navegación original | `Rebuild_HeaderUsesExistingNavigation` |

Compilación Release desde cero de `3DS.Progressive.Tests.csproj` con dependencias WPF/Core/WinForms: 0 errores, 78 advertencias heredadas. Binlog fuera del repositorio.

Resultados finales Release:

- `--history-inspection-only`: 18 comprobaciones, éxito.
- `--rebuild-marts-ui-only`: 69, éxito; incluye cuatro casos nuevos de vista del historial.
- `--visual-redesign-only`: 95, éxito, sin errores de bindings detectados.
- `--interaction-lock-only`: 19, éxito.
- `--synthetic-only`: 3.687, éxito, fixtures XY/ORAS/SM/USUM.

Las suites se solapan; no sumar cifras como casos únicos. Durante el desarrollo, el caso de actualización manual expuso que el host fuera de pantalla no tenía contexto de sincronización de WPF; se corrigió el host y la prueba final pasa. La aplicación normal conserva su Dispatcher. No se presentan estos casos como pruebas físicas de ratón o teclado.

Queda comprobación manual del recorrido del selector nativo de destino y del único diálogo de confirmación con un proyecto grande. La ausencia de la llamada duplicada se revisó en código; no se automatizó el selector nativo ni se ejecutó una partida. El contraste refleja el momento de lectura; cambios externos posteriores requieren repetirlo.
