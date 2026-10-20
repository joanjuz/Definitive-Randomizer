# Rebuild, acciones de tiendas y valoración de rendimiento

> Este documento conserva la valoración inicial. La implementación posterior, mediciones comparativas y validaciones están en [OPTIMIZACION_LECTURA_REBUILD.md](OPTIMIZACION_LECTURA_REBUILD.md).

Fecha: 2026-10-19. Aplicación WPF/.NET 10. Rama de trabajo existente; sin commit, push ni publicación automática.

## Cambios de interfaz

- Rebuild junto a Generar backup, deshabilitado sin proyecto. Usa la navegación, comprobaciones de cambios pendientes y bloqueo de interacción existentes. Las acciones de cabecera pasan a una segunda fila al reducir el ancho.
- Al abrir Rebuild se muestra el historial guardado en un diálogo desplazable; la confirmación de reconstrucción vuelve a mostrarlo. Incluye secuencia, valores antes/después, parámetros y archivos. Explica que se empaqueta el estado actual guardado, sin volver a aplicar acciones antiguas, sin ejecutar opciones de Global/Batch pendientes y sin afirmar que el historial detecte modificaciones externas.
- Las seis acciones de tiendas/BP son botones acumulables. Una semilla estable durante la vida de la vista evita que añadir otra acción vuelva a sortear la anterior. Repetir un botón no lo desactiva. Botón derecho permite quitar una acción; Restablecer opciones quita las seis. Se conservan filtros, generación explícita de vista previa, edición de filas, confirmación, guardado y captura/restauración de opciones de Global/Batch.
- La propuesta se guarda con la misma instancia del servicio que la generó. Antes, Preview generaba un draft y Apply utilizaba otro servicio, cuya comprobación rechazaba propuestas de presets. Se conserva la validación de bytes, archivos modificados externamente, escritura verificada e historial.
- No se modificaron motores, formatos, reglas, tablas de ROM ni templates. No se aplicaron las optimizaciones sugeridas a continuación.

## Medición real de lectura en UltraSol

Herramienta temporal fuera del repositorio. Solo Project.Open(recoverInterruptedSave:false), constructores y Read; sin Preview, Apply ni Save. Entrada identificada: D:/Nintendo/3DS/Juegos/PokemonUltraSol; encdata de 482.842.644 bytes; 538 Field Items, 794 entradas de encuentros.

Una ejecución por fase en un proceso, secuencial; caché del SO no controlada, JIT y GC incluidos. No es benchmark estadístico ni medición del tiempo completo de la interfaz. Asignaciones acumuladas, NO memoria residente ni pico de memoria.

| Fase | Tiempo observado | Bytes asignados |
|---|---:|---:|
| Apertura del proyecto | 565 ms | 92.398.672 |
| Constructor Field Items | 3.855 ms | 979.315.920 |
| Seleccionar primer Field Item | 510 ms | 485.667.928 |
| Seleccionar segundo Field Item | 545 ms | 485.667.816 |
| Constructor encuentros | 1.261 ms | 1.468.478.808 |
| Seleccionar primer encuentro | 394 ms | 488.324.480 |
| Seleccionar segundo encuentro | 413 ms | 488.278.232 |

La repetición por selección es un problema comprobado, no solo una conjetura sobre disco lento. No se prometen porcentajes de mejora a partir de esta muestra.

## Hallazgos y propuestas priorizadas

### Prioridad 1: selección de Field Items y encuentros

GameFieldItemsService.Read crea LazyGARC sobre Original(), que lee y calcula SHA-256 del encdata completo. ArchiveProjectEditor.Read llama Records() y recorre las tablas necesarias de nuevo por cada selección. RecordEditorView.Show lo ejecuta sincrónicamente en el hilo WPF.

Propuesta: snapshot inmutable por sesión del índice/miembros necesarios, lecturas por miembro bajo demanda y caché de registros pequeños. Compartir catálogos de selección de objetos, actualmente recreados por lectura. Actualizar/invalidar después de cada guardado o cambio de proyecto; rechazar discrepancias del archivo antes de Preview/Save/Apply. Una caché solo por fecha/tamaño NO debe sustituir la verificación de integridad. Separar la inspección visual rápida de la comprobación completa antes de escribir. Nunca compartir buffers editables entre propuestas.

Impacto previsto: alto al recorrer registros (se elimina trabajo proporcional a todo encdata en cada selección). Medir memoria retenida, no mantener copias de 483 MB por editor indefinidamente. Riesgo medio por invalidación y concurrencia; pruebas de modificación externa y miembros opacos obligatorias.

### Prioridad 2: apertura de Field Items

Constructor Gen7 tiene exactamente dos File.ReadAllBytes(_path) consecutivos: uno para SHA-256 y otro para LazyGARC. En la muestra supone aproximadamente 966 MB de lectura/asignación base para un archivo de 483 MB. Escanea miembros ED para EI/EB. MainWindow crea este servicio en la rama FieldItems sin Task.Run, a diferencia de Encounters.

Propuesta de bajo riesgo: una lectura fuente compartida para hash y LazyGARC durante la construcción, sin cambiar bytes ni compresión. Pasar la construcción a segundo plano con el mismo control de sesión/versión y bloqueo transitorio ya existente para Encounters. Esto último mejora respuesta/progreso, no reduce por sí solo el tiempo total. Añadir cancelación segura para cargas obsoletas. Mayor trabajo: indexar EI/EB por zona en lugar de redecodificar repetidamente durante previews.

### Prioridad 3: apertura de encuentros

ArchiveProjectEditor constructor lee el archivo, construye un MemGARC descartado para validar y calcula hash. Records() hace otra lectura verificada; GameEncounterService vuelve a pedir encdata mediante GetlzGARCData para Area7 y nombres/zonas. La selección Gen7 ya conserva opacos los miembros ajenos a EA; no debe revertirse ese aislamiento para ganar velocidad.

Propuesta: reutilizar una instantánea verificada/índice entre validación inicial, EA y metadatos de Area7; cargar nombres de zonas separadamente y conservar el mismo mapeo. Revisar que una validación de cabeceras no necesite materializar todos los miembros. El constructor ya se ejecuta en Task.Run desde MainWindow, pero la selección no. Riesgo medio por correspondencia área/zona/tabla y compresión.

### Prioridad 4: reconstrucción de RomFS

RomFS.BuildRomFS copia archivos creando un buffer nuevo de hasta 1 MiB por iteración. Después recorre bloques IVFC, alternando Seek/Read en los datos y Seek/Write de hashes sobre el mismo FileStream. Son muchas operaciones pequeñas y asignaciones para varios GB. Las actualizaciones de progreso ya están limitadas (aproximadamente 200 por nivel); no conviene atribuir de nuevo todo el coste al refresco de progreso.

Propuesta: buffer reutilizable, ReadExactly para no asumir lecturas completas; procesamiento de hashes por lotes con lecturas/escrituras secuenciales y separación de posiciones de streams. Conservar alineación, padding, orden de archivos, IVFC y formatos finales. Primero medir los tiempos de copia, metadatos, hash de cada nivel, ensamblado, BLZ y SHA final, con caché fría/caliente y disco identificado. Luego comparar archivos extraídos y hashes IVFC completos. No se ejecutó una nueva construcción de ROM para esta valoración; no se puede cuantificar todavía qué etapa domina en este equipo.

### Prioridad 5: ExeFS, Batch y validación

Ya existe ExeFsBuildCache por contenido y se comparte en NativeBatchRomBridge dentro del proceso Batch. Rebuild manual conserva caché dentro de su servicio, pero recrear RomRebuildView al navegar pierde esa instancia. No proponer quitar BLZ ni el bit de compresión del ExHeader: forman parte del formato correcto.

Propuesta: caché de ExeFS al nivel de sesión con la firma de contenido actual y las verificaciones antes/después. Reutilizar artefactos solo cuando coincidan todas las entradas relevantes. Para RomFS de ROMs distintas no basta reutilizar por nombre de proyecto: cada semilla puede cambiar archivos.

GameRomRebuildService calcula SHA-256 final; BatchRomExecution también recorre cada ROM generada para su manifiesto. Valorar un informe de integridad tipado producido por el proceso hijo y contrastado por el padre para evitar cálculos idénticos, manteniendo una verificación final de la salida real. No eliminar comprobaciones ni confiar sin más en un hash entregado por otro proceso. El hash final no verifica por sí solo que todos los formatos internos sean válidos; conservar las validaciones actuales.

No aumentar por defecto el paralelismo de ROMs: puede aumentar memoria, contención del disco y accesos a motores heredados con estado estático. Priorizar primero las lecturas redundantes y el patrón de E/S.

### Gen6 y otros módulos

GameFieldItems6Service.ReadSource vuelve a leer/verificar objetos, scripts y code.bin cuando corresponde; se beneficia del mismo diseño de snapshot/índice. No trasladar offsets ni decodificadores Gen7 a Gen6. No se midió Gen6 en esta valoración.

Catálogos, sprites en línea, tablas y carga de proyectos tienen costes distintos: conservar virtualización y no mezclar tiempos de descarga con lectura de encdata. Esta revisión se centra en las rutas solicitadas, no certifica rendimiento de cada módulo.

## Plan de implementación recomendado

1. Instrumentación por fase, baseline de apertura/selección, tamaño de archivos y asignaciones (mínimo varias repeticiones).
2. Eliminar doble lectura de Field Items y mover su construcción fuera del hilo UI, con guardas de sesión y pruebas.
3. Snapshot/indexado de lectura por selección para Field Items y encuentros; validación fuerte antes de escribir y pruebas de cambios externos.
4. Reutilizar caché ExeFS por sesión.
5. Optimizar streaming/IVFC solo con equivalencia y roundtrip independientes, y después valorar el hash duplicado de Batch.

Sin garantizar tiempos ni un número fijo de ROMs por minuto antes de medir cada paso. No se recomienda recortar validación a costa de integridad.

## Validación de esta implementación

Resultados finales registrados al terminar. Las comprobaciones de controles y renderizado fuera de pantalla no equivalen a una sesión interactiva con teclado, diálogos modales ni reconstrucción jugable. Quedan pendientes la revisión manual de las ventanas emergentes con historiales extensos, DPI y la interacción real del menú contextual.

## Archivos de esta entrega

- 3DS.Progressive.App/MainWindow.xaml y MainWindow.xaml.cs: botón y navegación.
- 3DS.Progressive.App/AdaptiveLayout.cs: ancho reservado para las acciones de cabecera.
- 3DS.Progressive.App/RomRebuildView.cs: resumen y confirmación con historial.
- 3DS.Progressive.App/MartEditorView.cs: acciones acumulables, retirada/restablecimiento, servicio de propuesta y guardado. Generar vista previa conserva la posibilidad de una nueva tirada explícita; añadir otra acción conserva la semilla de la propuesta existente.
- 3DS.Progressive.Tests/RebuildMartUiChecks.cs, MartSmChecks.cs y Program.cs: comprobaciones nuevas y flag --rebuild-marts-ui-only.
- Este informe, docs/3ds-progressive/REBUILD_TIENDAS_RENDIMIENTO.md.

Compilación final Debug de WPF, Core, backend WinForms y runner: 0 errores y 90 advertencias. La compilación inicial detectó una ambigüedad WPF/WinForms de MenuItem/ContextMenu introducida en la implementación; se corrigió con aliases explícitos antes de las ejecuciones correctas. No se reclasifica la antigüedad de todas las advertencias.

Suites ejecutadas: --rebuild-marts-ui-only (65), --visual-redesign-only (95, sin errores de bindings), --interaction-lock-only (5), --rebuild-cache-only (11). Total: 176 comprobaciones distintas de estas cuatro suites. La suite de tiendas usa un fixture SM y datos aislados; sus escrituras de comprobación nunca se aplican al proyecto real usado para medir. La lógica compartida de opciones conserva el contrato SM/USUM, sin afirmar una prueba interactiva USUM de esta interfaz.

| Requisito | Evidencia exacta |
|---|---|
| Rebuild en cabecera y navegación | Rebuild_HeaderAdjacentToBackup; Rebuild_DisabledWithoutProject; Rebuild_EnabledWithProject; Rebuild_HeaderUsesExistingNavigation |
| Resumen fiel del historial | Rebuild_EmptyHistoryExplainsCurrentFiles; Rebuild_HistoryPreservesOrderDetailsAndDistinctFiles; Rebuild_HistoryDistinguishesSavedStateFromOptions |
| Acciones acumulables y compatibles | Marts_ButtonsAccumulateRegularAndBp; Marts_AddingBpPreservesPreviousRegularPreview; Marts_CumulativePreviewMatchesNativeBridge; Marts_ApplyWritesExactCumulativePreview; Marts_ApplyRecordsBothActionsForGlobalBatch; Marts_RestoreRecipePreservesGlobalBatchOptions |
| Retirada, repetición y conservación de datos | Marts_RepeatedActionDoesNotToggleOrReroll; Marts_RemoveActionPreservesOtherSelections; Marts_ResetOptionsClearsPreviewWithoutWriting; Marts_ButtonPreviewsNeverWriteRomOrHistory |

Sin commit, push, actualización del tag/release ni sustitución de la aplicación instalada en esta tarea. El trabajo previo pendiente de compras únicas permanece intacto. No se aplicó ninguna optimización de motores ni se construyó una nueva ROM para esta valoración.
