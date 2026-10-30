# Inicio, About GARCs y aplicación de Global Template

Fecha: 20 de octubre de 2026. Sin commit, push, release ni modificación de la distribución instalada. Se conservan las modificaciones pendientes de guía, historial y estabilidad.

## Resultado

Al abrir un proyecto, Inicio permanece visible y el editor queda fuera de interacción hasta elegir un modo o iniciar el asistente. About GARCs abre un diálogo compacto con el listado real de nombres y rutas del juego, desplazamiento y Cerrar; deja de limitarse a copiar al portapapeles.

Global Template añade Previsualizar cambios y Aplicar cambios. La selección de acciones corresponde a la receta cargada; previsualizar no incorpora silenciosamente otros cambios del historial ni las opciones de editores ajenos. Guardar y Crear desde historial mantienen sus funciones.

La vista previa ejecuta los motores Gen6/Gen7 originales en una copia temporal, sin restaurar backups ni reconstruir .3ds. Muestra las acciones elegidas, archivos realmente distintos, tamaños y SHA-256 del resultado. Aplicar confirma y traslada exactamente esos bytes: no vuelve a randomizar. Después se recarga el proyecto para descartar caches antiguas; Rebuild sigue siendo la acción independiente de la cabecera.

## Historial e integridad

Los archivos y el historial se guardan con FileTransaction y RecoveryJournal, en una sola transacción recuperable. Se registra una entrada conjunta con las diferencias de archivos y una entrada por cada acción, sus parámetros y la referencia SHA al resultado conjunto. El ejecutor agrupa operaciones; no se inventan diferencias binarias independientes por acción. La entrada conjunta conserva la receta completa y su origen para Crear desde historial, incluidas las opciones que antes no podían reconstruirse solo desde IDs.

Antes de aplicar se comprueban los hashes de todos los recursos de entrada y del historial. Se rechaza una propuesta repetida, un proyecto o historial cambiado, una copia temporal alterada, dependencias ausentes o con hash incorrecto y recetas incompatibles. Los nombres originales de archivos, incluido code.bin/.code.bin y las mayúsculas de CRO, se conservan.

Las dependencias declaradas se copian y verifican en un directorio privado. Solo el subproceso de vista previa recibe DEFINITIVE_GLOBAL_PREVIEW_ROOT; los flujos Batch normales mantienen sus rutas. Se aíslan también los CSV, Templates, estados temporales y el directorio de trabajo; no se sustituyen los templates instalados. Las adiciones/eliminaciones de recursos de juego se rechazan explícitamente en esta ruta de reemplazo, en lugar de instalarlas parcialmente.

## Archivos de esta tarea

- 3DS.Progressive.App/MainWindow.xaml.cs: Inicio tras abrir, bloqueo de salida y recarga posterior a la aplicación.
- 3DS.Progressive.App/SystemsView.cs y StudioDialog.xaml.cs: ventana About GARCs y fábrica reutilizada de diálogos.
- 3DS.Progressive.App/GlobalTemplateView.cs: previsualización, confirmación, invalidación, resultado y receta del historial.
- 3DS.Progressive.App/Services/GlobalTemplateApplication.cs: preparación aislada, hashes, diferencias congeladas y aplicación.
- 3DS.Progressive.App/Services/ProjectHistoryGlobalTemplate.cs: transacción conjunta y entradas por acción.
- 3DS.Progressive.App/Services/GlobalTemplateDocument.cs: conservar parámetros de acciones registrados en historial.
- pk3DS.WinForms/Main.BatchBridge.cs: opción PreviewOnly, falsa por defecto; reutiliza los ejecutores existentes y termina antes de reconstruir.
- pk3DS.WinForms/Misc/CustomBalanceTemplates.cs, TrainerHeldItemTemplate.cs y TrainerBetterMovesetTemplate.cs: raíz privada de dependencias únicamente cuando el proceso de vista previa la define.
- 3DS.Progressive.Tests/GlobalApplicationChecks.cs y Program.cs: suites --global-apply-only y --global-apply-ui-only.

No se modificaron algoritmos de randomización, offsets, datos del juego, templates personales, compras únicas ni workflows de publicación.

## Evidencia ejecutada

| Requirement | Evidence |
|---|---|
| Inicio antes de elegir modo | GlobalApply_OpeningProjectRequiresChoiceAtHome |
| About GARCs muestra el listado real | GlobalApply_AboutGarcsDialogContainsActualScrollableListing; GlobalApply_AboutGarcsOpensListingInsteadOfCopyOnly |
| Vista previa real sin escribir origen ni rebuild | GlobalApply_PreviewRunsOriginalEngineWithoutSourceWritesOrRebuild_XY/ORAS/SM/USUM |
| Aplicar el resultado congelado y conservar rutas | GlobalApply_AppliesFrozenBytesAndPreservesCodeFilename_XY/ORAS/SM/USUM |
| Cada acción en historial | GlobalApply_EachActionRecordedOnceWithSharedTransaction_XY/ORAS/SM/USUM; GlobalApply_HistoryReferencesRealFinalHashes_XY/ORAS/SM/USUM |
| Conservar receta y opciones | GlobalApply_HistoryRetainsCompleteRecipeOptions_XY/ORAS/SM/USUM; GlobalApply_FromHistoryRestoresAppliedRecipeWithoutInventingSettings |
| Rechazar cambios y doble aplicación | GlobalApply_RejectsSecondApply_XY/ORAS/SM/USUM; GlobalApply_RejectsChangedDependency_XY/ORAS/SM/USUM; GlobalApply_RejectsTamperedPreview_XY/ORAS/SM/USUM; GlobalApply_RejectsChangedHistory |
| Rollback sin aplicación parcial | GlobalApply_ReadOnlyCommitRejected; GlobalApply_FailedCommitRollsBackFilesAndHistory |
| Dependencias locales verificadas | GlobalApply_LocalVerifiedDependenciesOverrideBundledCopies |

Resultados:

- Compilación Release de Tests y referencias: 0 errores; 88 advertencias de código existente. Sin advertencias nuevas en los componentes añadidos.
- --global-apply-only: 46 comprobaciones correctas. Incluye subprocesos reales del ejecutor sobre proyectos sintéticos XY, ORAS, SM y USUM; sus partes WPF se comprueban fuera de pantalla.
- --global-options-only: 132 correctas.
- --guidance-only: 233 correctas.
- --visual-redesign-only: 95 correctas, sin errores de binding en los renders.
- --interaction-lock-only: 19 correctas.
- --rebuild-marts-ui-only: 69 correctas.
- --synthetic-only: 3.750 comprobaciones correctas con la versión final del código.
- git diff --check: sin incidencias.

Los fallos durante el desarrollo se corrigieron antes de esta ejecución: fixtures incompletos de textos/MT y un helper de rechazo que no contemplaba UnauthorizedAccessException. La prueba de archivo de solo lectura confirma ahora el rollback real de archivos e historial.

## Verificación manual pendiente

Probar el recorrido completo en una ventana real, con la receta completa del usuario y su ROM extraída, revisar visualmente la propuesta, aplicar, consultar el historial y reconstruir desde la cabecera. No se modificó ninguna ROM personal para estas pruebas. La integración usa datos sintéticos y demuestra replay, aislamiento y transacción, no jugabilidad ni todos los parches concretos de una receta real. La vista previa necesita espacio para la copia del proyecto y tiempo para ejecutar las acciones elegidas; Aplicar reutiliza el resultado en lugar de ejecutarlas de nuevo.

Skills: pk3ds-progressive-safety, wpf, dotnet-winforms/winforms-expert, dotnet-test/code-testing y run-tests, dotnet-msbuild/binlog-generation. El especialista test-engineer no está disponible; se hizo la investigación, matriz, implementación y revisión de pruebas directamente.
