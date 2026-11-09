# Nintendo DS — base espacial, fase visual 1

Implementación exclusivamente de lectura para **Platino CPUE/1**. Base: HEAD del PR #55 `43bd194268eff242ef7a2fa41a743a5cd6339f91`, sin modificar su rama. No hay lienzo WPF, edición de eventos, cambios en sesiones/guardado ni soporte adicional de juegos.

## Estructura e identidad

`PlatinumMapLayout.Open(rom, mapId)` reutiliza **todas** las comprobaciones de `PlatinumEvents.Open`: código/revisión, ARM9, cabeceras, nombres, matrices, scripts y bytes normalizados de eventos. `ParseMatrix` es una entrada estructural para fixtures; no acredita una ROM.

Cada miembro de `fielddata/mapmatrix/map_matrix.narc` contiene:

| Sección | Lectura |
|---|---|
| Cabecera de 5 bytes | ancho, alto, presencia de cabeceras, presencia de altitudes, longitud del prefijo |
| Prefijo | ASCII imprimible, hasta 16 bytes |
| Cabeceras opcionales | `ancho × alto` valores u16 little-endian |
| Altitudes opcionales | `ancho × alto` bytes sin convertir a geometría |
| Terreno | `ancho × alto` identificadores u16 little-endian |

Dimensiones admitidas: 1–30 por eje. Flags: 0/1. Se exige longitud exacta; se rechazan truncamientos y bytes finales. Orden por filas: `índice = bloqueX + bloqueY × ancho`.

Si no hay cabeceras almacenadas, el juego asigna la cabecera seleccionada a las celdas de esa carga. El lector expone `StoredHeaderId=null` y `HeaderId=MapId` para distinguir esta interpretación contextual de una asociación explícita. `OwnedCells` selecciona las celdas cuyo `HeaderId` coincide con el mapa; no presupone rectángulos ni continuidad. Los IDs desconocidos se conservan sin asignarles significado. Una matriz compartida no pertenece íntegramente a cada cabecera que la referencia.

`RawAltitude=null` indica ausencia de la sección; no se inventa una altura del terreno. El ID de terreno **0 es válido**, no un marcador de celda vacía. No se leen modelos ni colisiones. Las listas y sus registros son inmutables; no se retiene una referencia mutable al buffer de entrada. No hay serializador ni operaciones de escritura.

## Coordenadas

X/Y del lector de eventos son los dos ejes horizontales en tiles de la matriz seleccionada. Para NPC, corresponden a X/Z del juego (u16 en offsets 24/26; el lector de eventos existente los interpreta como i16). El campo denominado Z por `PlatinumEvent` es el valor vertical crudo de 32 bits en offset 28; no se usa como fila ni se cambia su interpretación en esta fase.

Para coordenadas no negativas dentro de la matriz:

```text
bloqueX = X / 32        localX = X % 32
bloqueY = Y / 32        localY = Y % 32
X = bloqueX*32 + localX
Y = bloqueY*32 + localY
```

`Locate` rechaza coordenadas negativas o fuera de la matriz antes de dividir. `ToTiles` valida bloque y coordenadas locales 0–31. Se permite consultar una posición de otra cabecera sin corregirla ni trasladarla automáticamente. Estas son coordenadas globales **dentro de una matriz**, no un espacio universal entre matrices.

## Caso real 411 — T01 / Twinleaf

Sonda de solo lectura sobre la copia privada CPUE/1 acreditada:

| Dato | Resultado |
|---|---|
| Cabecera / archivo de eventos / matriz | 411 / 390 / 0 |
| Dimensiones de matriz | 30×30 bloques = 960×960 tiles |
| Prefijo / secciones opcionales | `map` / cabeceras y altitudes presentes |
| NPC índice 0 | X=119, Y=878; vertical crudo=65536 |
| Bloque / posición local | **(3,27) / (23,14)** |
| Índice de celda | 813 |
| Cabecera almacenada en esa celda | **411** |
| Celdas explícitamente de la cabecera 411 | **solo (3,27)** |
| ID de terreno / altitud cruda | 0 / 0 |

**Hipótesis correcta para esta posición y asociación estática.** `119=3×32+23` y `878=27×32+14`; el origen del bloque es (96,864). Interpretar las 900 celdas como área propia de T01 sería falso. El `Width/Height` de `PlatinumMap` ya existente describe límites de matriz, no propiedad de cabecera; no se cambia el guardado ni sus límites en esta entrega.

Se leyeron **289 miembros** y **593 contextos de cabecera**, con dimensiones coincidentes con el lector de eventos. Dos matrices contienen ambas secciones opcionales; las otras 287 no contienen ninguna. ROM y NARC de eventos coinciden byte a byte/hash antes y después. La sonda escribe únicamente un informe JSON nuevo; no abre sesiones ni crea checkpoints o exportaciones. La evidencia privada adicional verifica los cuatro checkpoints preexistentes y los archivos locales ajenos a esta tarea.

## Validación focalizada

Build Release del proyecto de pruebas, incluyendo Core, WPF y LegacyBatch: **0 errores, 0 advertencias**. Runner propio del repositorio; no se utiliza `dotnet test`.

| Requisito | Evidencia ejecutada |
|---|---|
| Secciones y orden por filas | `MapLayout_Sections_HeadersTrue_AltitudesTrue`, `MapLayout_RowMajorLittleEndianAndRawLandIds_HeadersTrue_AltitudesTrue` y variantes de los cuatro flags |
| Propiedad y ausencia de cabeceras | `MapLayout_DisconnectedOwnership_HeadersTrue_AltitudesTrue`, `MapLayout_MissingHeadersFollowSelectedMapContextNotMatrixId`, `MapLayout_UnknownRawHeaderIsPreservedWithoutInventingOwnership` |
| Escala y límites | `MapLayout_AllNonSquareTilesRoundtrip`, `MapLayout_TileBoundaryRoundtrip_32_32`, `MapLayout_RejectTile_-1_0`, `MapLayout_RejectLocal_32_0` |
| Integridad e identidad | `MapLayout_ParseLeavesInputBytesUnchanged`, `MapLayout_InputMutationCannotChangeLayout`, `MapLayout_CellsAreReadOnly`, `MapLayout_ProfileRejectsUnaccreditedTables` y rechazos de juego/revisión/DSi |
| Caso real | `MapLayoutReal_Npc0ExplicitUniqueHeader411Cell`, `MapLayoutReal_Npc0TileRoundtrip` |
| Lectura real inmutable | `MapLayoutReal_EventArchiveByteExactAfterReads`, `MapLayoutReal_SourceSha256Unchanged` |

- `--nds-map-layout-only`: **74 comprobaciones sintéticas aprobadas**, incluyendo matrices no cuadradas, los cuatro formatos de secciones, malformed/truncados, bordes y rechazo de perfiles.
- `--nds-events-only`: **42 comprobaciones previas aprobadas**; no se cambia el editor ni sus checkpoints.
- `--nds-map-layout-probe <informe JSON nuevo> <ROM privada CPUE/1>`: **9 comprobaciones reales aprobadas**. El directorio padre debe existir y el informe no puede existir previamente. Sin rutas privadas ni datos de ROM en Git.
- `git diff --check`: sin incidencias. No se ejecutan suites generales ni emulador.

## Datos para un futuro lienzo y límites

Disponibles: dimensiones, origen de cada bloque, máscara de cabecera, distinción explícita/contextual, IDs de terreno y prefijo, altitudes crudas opcionales y conversión reversible de posiciones. Esto permite preparar una cuadrícula y situar marcadores, sin afirmar que existe un fondo visual o una superficie transitable.

Pendiente: geometría, colisiones, alturas efectivas, conexiones entre matrices, estados dinámicos de partida, sprites, modelos y texturas. El juego puede modificar datos de matriz al revelar rutas; el lector representa únicamente el archivo estático. No se infiere que todo evento fuera de la máscara sea inválido. Solo se acredita en ROM el caso espacial del NPC solicitado; no se verifica en emulador el posicionamiento visual de todos los NPC/warps/objetos/activadores ni sus reglas particulares. No se alteran formatos, coordenadas o restricciones del editor actual. No hay confirmación jugable nueva.

## Fuentes primarias

Hechos del formato contrastados con `pret/pokeplatinum`, revisión `62bdaacdca6c56a6a396f3e9911e2f922b79bba8`:

- [Carga de matrices, índices y cambios de rutas en ejecución](https://github.com/pret/pokeplatinum/blob/62bdaacdca6c56a6a396f3e9911e2f922b79bba8/src/map_matrix.c).
- [Límites de matriz y prefijo](https://github.com/pret/pokeplatinum/blob/62bdaacdca6c56a6a396f3e9911e2f922b79bba8/include/constants/field/map_matrix.h).
- [Escala horizontal de 32×32 tiles](https://github.com/pret/pokeplatinum/blob/62bdaacdca6c56a6a396f3e9911e2f922b79bba8/include/constants/field/map.h).
- [Estructuras de eventos](https://github.com/pret/pokeplatinum/blob/62bdaacdca6c56a6a396f3e9911e2f922b79bba8/include/struct_defs/events.h) y [uso de X/Z horizontales del NPC](https://github.com/pret/pokeplatinum/blob/62bdaacdca6c56a6a396f3e9911e2f922b79bba8/src/map_object.c).

Implementación independiente a partir de esos hechos y del lector existente, contrastada con la copia privada. No se incorpora código ni assets de DSPRE ni de la ROM. La validación espacial no amplía el perfil más allá de CPUE/1.
