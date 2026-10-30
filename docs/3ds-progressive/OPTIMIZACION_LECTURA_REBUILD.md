# Optimización de lectura y reconstrucción

Fecha: 2026-10-19. Continuación de REBUILD_TIENDAS_RENDIMIENTO.md. Se preservan todos los cambios locales previos de Rebuild/tiendas. Sin commit, push, actualización de instalación ni tag/release.

## Implementación

1. Field Items Gen7 hace una única lectura inicial para fingerprint y análisis. Conserva únicamente sus IDs/valores y un catálogo inmutable de objetos para mostrar registros. Se reutiliza el desempaquetado EI/EB por zona al preparar múltiples cambios, conservando el orden de ediciones y los miembros ajenos.
2. Field Items Gen6 conserva valores pequeños para mostrar, decodifica cada script una vez durante esa indexación, comparte catálogo y evita la segunda lectura de code.bin. La búsqueda de firma ORAS usa Span.IndexOf y sigue rechazando duplicados.
3. Encuentros usa una instantánea privada de las tablas necesarias. Gen7 solo decodifica los miembros EA (índice 9 de cada 11); los demás permanecen opacos. Los metadatos Area7 reutilizan el archivo inicial y no vuelven a leer encdata. La referencia grande inicial se libera después de construir el índice. Gen6 conserva su orden físico/editor.
4. Las selecciones visuales consultan esas instantáneas y una marca rápida de tamaño/fecha. Los cambios detectados de metadatos obligan a reabrir. Esta marca NO sustituye SHA-256: Preview/Save/Apply mantienen comprobaciones de contenido, incluso si se cambia un archivo conservando tamaño y fecha. Una alteración externa que falsifique los metadatos puede no reflejarse inmediatamente en pantalla, pero se rechaza antes de escribir. Ninguna propuesta recibe los buffers de lectura privados para editarlos.
5. Después de guardar/aplicar se refrescan valores/instantáneas. El historial, templates, semillas y replay mantienen sus contratos.
6. Field Items pasa a Task.Run, como encuentros. Ambas cargas tienen CancellationToken, guardas de sesión/versión y el bloqueo de interacción existente. Cambiar proyecto o cerrar cancela la carga; no se publica una vista de una sesión antigua. Se conservan los constructores públicos originales de un argumento.
7. La caché ExeFS vive en GameProjectSession para que navegar y recrear RomRebuildView no provoque otra compresión de entradas idénticas. Se mantienen la firma de contenido y sus verificaciones, BLZ y ExHeader. La caché existente del proceso Batch también se conserva.
8. RomFS reutiliza un buffer de copia de 1 MiB, usa ReadExactly y calcula IVFC por lotes: una lectura/escritura posicionada por lote, hashes de cada bloque de 4 KiB y padding cero. Se conservan layout, alineaciones y límite de refrescos de progreso. No se cambió CTR/NCCH ni el cifrado. La firma pública previa de BuildRomFS permanece mediante overload.
9. Se registran tiempos separados de metadatos, copia, cada nivel IVFC, ExeFS, BLZ, ensamblado, SHA final y total mediante callback opcional y LastTimings del servicio. No se crean logs ni archivos adicionales dentro de la extracción original.

## Verificación de SHA final de Batch

Se evaluó la propuesta de trasladar el hash del proceso hijo al manifiesto del padre. Se mantienen ambas comprobaciones independientes. Un sidecar por sí solo no prueba que la salida no haya cambiado; confiar solo en él debilitaría la validación. El escritor CTR también usa Seek y vuelve sobre regiones, por lo que calcular un hash incremental de las llamadas Write no representa sin más el archivo final. No se amplió el cambio al escritor/cifrado para evitar una lectura de unos segundos. Es una decisión de integridad, no una optimización implementada ni un reporte tipado nuevo.

No se añadió paralelismo entre ROMs ni una caché RomFS por nombre: las semillas y cambios de layout invalidan esa reutilización. No se eliminó validación ni se modificaron motores de randomización.

## Medición de servicios reales

Tres ejecuciones por variante y proyecto; se muestra la mediana. Procesos nuevos, JIT incluido; caché del SO y carga del sistema no aisladas. No se forzó ni se afirma un escenario de caché fría. Son mediciones de servicios, NO de la ventana completa. Asignaciones acumuladas, NO pico ni memoria residente. Comparación con los binarios anteriores, sin aplicar propuestas sobre las entradas reales.

Entrada USUM: D:/Nintendo/3DS/Juegos/PokemonUltraSol (538 Field Items, 794 entradas de encuentros). Entrada ORAS: D:/Nintendo/3DS/Juegos/PokemonAS. Solo apertura y Read; no Save/Apply. Los valores de apertura dependen más de la caché del SO que los de selección.

| Fase | Antes (mediana) | Después (mediana) | Bytes asignados antes → después |
|---|---:|---:|---:|
| USUM · FieldItems.Constructor | 561.48 ms | 410.44 ms | 979,314,240 → 496,531,576 |
| USUM · FieldItems.ReadFirst | 386.27 ms | 1.64 ms | 485,667,928 → 496 |
| USUM · FieldItems.ReadSecond | 412.29 ms | 0.19 ms | 485,667,592 → 320 |
| USUM · Encounters.Constructor | 1054.41 ms | 406.08 ms | 1,468,474,032 → 502,817,760 |
| USUM · Encounters.ReadFirst | 404.34 ms | 3.00 ms | 488,324,144 → 98,320 |
| USUM · Encounters.ReadSecond | 427.07 ms | 0.28 ms | 488,278,008 → 52,480 |
| ORAS · FieldItems.Constructor | 168.84 ms | 20.16 ms | 48,845,064 → 7,642,560 |
| ORAS · FieldItems.ReadFirst | 6.60 ms | 2.88 ms | 6,677,584 → 688 |
| ORAS · FieldItems.ReadSecond | 6.70 ms | 0.35 ms | 6,677,248 → 512 |
| ORAS · Encounters.Constructor | 66.54 ms | 69.92 ms | 24,275,352 → 23,238,504 |
| ORAS · Encounters.ReadFirst | 50.64 ms | 3.14 ms | 22,927,360 → 93,528 |
| ORAS · Encounters.ReadSecond | 44.37 ms | 0.31 ms | 22,870,256 → 48,184 |

No se atribuye mejora a la apertura general del proyecto ni a la apertura de encuentros ORAS (66,54 → 69,92 ms); esa diferencia no justifica una conclusión de velocidad con tres muestras no aisladas.

## Equivalencia con la versión publicada

- Propuestas de Field Items y encuentros, semilla 123, sobre USUM y ORAS reales, sin aplicar: mismos recuentos de cambios, hashes fuente y hashes propuestos que los binarios publicados. En USUM son 489 cambios de Field Items y 51.536 valores de encuentros.
- Entrada RomFS sintética de 64 MiB más archivos de tamaños 0, 1, 4095, 4096, 4097 y 131073 bytes. Tres construcciones por variante. Mediana de 233,55 a 107,11 ms; asignaciones de 68.267.864 a 4.280.768 bytes. Se comprobó igualdad completa de los 67.796.992 bytes de salida, no solo igualdad de los datos extraídos. SHA-256 de ambas variantes: 7DD8B9A4D719617F9FE5F982606B5469333094D760AC0021949C36C3C87D54D3. No se extrapola este porcentaje al tiempo de una ROM completa.

## Reconstrucción real aislada

Salida: D:/Progra/Pokemon/Randomizer/DefinitiveRandomizer-validation/Performance20261019/USUM-rebuild-optimizado.3ds. Entrada original solo leída; salida nueva, fuera del repositorio y del proyecto original. No contiene una nueva randomización de la receta: valida la reconstrucción de la extracción actual.

Tamaño: 4.294.967.296 bytes. SHA-256: DFE3BEE755C222018FE977CE6AB2FF7137C8471E70BCE7B737D23CAF5121D6CB.

Verificación Python independiente: 747 archivos RomFS idénticos al proyecto, cero discrepancias, ejecutable BLZ descomprimido idéntico (5.914.624 bytes). Se verificaron todos los hashes IVFC: nivel 0, 55 bloques; nivel 1, 6.990; nivel 2, 894.593. Total 901.638 bloques sin errores.

Tiempos de esa ejecución (no comparación A/B completa y no garantía de tiempo futuro):

| Fase | Tiempo |
|---|---:|
| Metadatos RomFS | 0,09 s |
| Copia de datos | 74,80 s |
| IVFC nivel 2 | 3,23 s |
| IVFC niveles 1 y 0 | 0,02 s |
| BLZ | 51,13 s |
| Ensamblado | 26,59 s |
| SHA final | 5,79 s |
| Total | 161,80 s |

La copia y BLZ dominan esta ejecución. La caché ExeFS puede evitar BLZ solo si las entradas son idénticas; no se afirma ese ahorro cuando las MT u otras opciones cambien code.bin. El tiempo total incluye actividad real del equipo y del disco; los microdatos sintéticos no lo sustituyen.

## Pruebas ejecutadas

- Build Debug de 3DS.Progressive.Tests y dependencias WPF/Core/WinForms: 0 errores, 88 advertencias. No se clasificó exhaustivamente la antigüedad de todas las advertencias.
- --synthetic-only: 3.661 comprobaciones correctas, XY/ORAS/SM/USUM, en la compilación final.
- --performance-only: 98 comprobaciones correctas.
- --native-wild7-only: 18 comprobaciones correctas, incluyendo replay exacto y miembros comprimidos ajenos.
- --rebuild-marts-ui-only: 65 comprobaciones correctas, conserva el trabajo anterior.
- --visual-redesign-only: 95 comprobaciones correctas, sin errores de bindings detectados.
- --interaction-lock-only: 5 comprobaciones correctas.
- diff --check correcto. Las suites tienen cobertura solapada; no se suman como pruebas distintas.

La prueba nueva USUM_FieldItems_SameSizeTimestampChangeRejectsApply detectó que un Apply sin diferencias podía aceptar una fuente externa y renovar su fingerprint sin validarla. Ahora también se comprueba el origen en ese caso y la prueba pasa. Las primeras herramientas temporales tuvieron errores de referencias/reflectividad/imports; se corrigieron sin cambiar contratos de producción para satisfacerlas. No se declaran como pruebas exitosas esas ejecuciones fallidas.

| Requirement | Evidence |
|---|---|
| Selección sin copiar el archivo grande | USUM_FieldItems_SelectionDoesNotCopyLargeArchive; USUM_Encounters_SelectionDoesNotCopyLargeArchive; ORAS_FieldItems_SelectionUsesSmallSnapshot |
| Snapshot aislado y actualizado | USUM_Encounters_PreviewDoesNotMutateReadSnapshot; USUM_FieldItems_SaveRefreshesSnapshot |
| SHA fuerte antes de escribir | USUM_FieldItems_SameSizeTimestampChangeRejectsPreview; USUM_FieldItems_SameSizeTimestampChangeRejectsSave; USUM_FieldItems_SameSizeTimestampChangeRejectsApply |
| Cancelación | XY_FieldItems_CancellationStopsConstruction; USUM_Encounters_CancellationStopsConstruction |
| Caché ExeFS de sesión | Rebuild_SessionCacheSurvivesViewServiceRecreation; Rebuild_PhaseTimingsIncludeFinalVerification |
| IVFC/streaming y tiempos | RomFS_IndependentIVFC_Level0; RomFS_IndependentIVFC_Level1; RomFS_IndependentIVFC_Level2; RomFS_AllPhasesMeasured; RomFS_StreamingPreservesAllSourceFiles |

## Archivos afectados en esta fase

MainWindow.xaml.cs; Services/ArchiveProjectEditor.cs; Services/DisplayFileStamp.cs (nuevo); Services/GameEncounterService.cs; Services/GameFieldItemsService.cs; Services/GameFieldItems6Service.cs; Services/GameProjectSession.cs; Services/GameRomRebuildService.cs; pk3DS.Core/CTR/RomFS.cs; Tests/PerformanceChecks.cs (nuevo); Tests/FieldItems6Checks.cs; Tests/RebuildCacheChecks.cs; Tests/Program.cs; este informe y enlace desde el informe previo. Los archivos de cabecera/tiendas anteriores y compras únicas se preservan.

## Límites y comprobaciones manuales

No se ejecutó una partida en emulador/consola con esta nueva reconstrucción; hashes, roundtrip y pruebas sintéticas no prueban jugabilidad completa. La cancelación se prueba en servicios; queda pendiente comprobar interactivamente las cargas y el cambio/cierre de proyecto, las ventanas del historial, el teclado y la percepción real del progreso. Las pruebas visuales son fuera de pantalla. No se declara una prueba WPF interactiva ni una prueba de caché fría del SO.

No se reemplazó la instalación, no se publicó .exe/ZIP ni se modificó el tag/release. No se hicieron commits ni push. Las ROMs e informes privados de validación permanecen fuera del repositorio; no deben incluirse en una publicación.
