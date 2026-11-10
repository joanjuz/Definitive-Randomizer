# Nintendo DS — terreno y obstáculos, fase visual 3

Capa de **solo lectura** con la colisión de terreno real de **Platino USA CPUE/1**, sobre el lienzo de la fase 2 ([NDS_MAP_CANVAS_PHASE2.md](NDS_MAP_CANVAS_PHASE2.md)). No hay arrastre ni escritura de NPC, terreno, colisiones, edificios, scripts ni warps, ni soporte de otros juegos. Una capa sin datos nunca bloquea la apertura de un mapa.

## Qué se averiguó del formato

Fuentes técnicas primarias: el código de [pret/pokeplatinum](https://github.com/pret/pokeplatinum) (`include/constants/field/map.h`, `src/overlay005/land_data.c`, `src/terrain_collision_manager.c`), contrastado con una copia privada CPUE/1. No se copió código de ese proyecto ni de DSPRE; se implementó de forma independiente a partir de hechos de formato.

### A. Información estática verificada

| Hecho | Evidencia |
|---|---|
| Archivo `fielddata/land_data/land_data.narc`, 666 miembros, 16 125 840 bytes, CRC32 `E1E68D47` | lectura de la ROM privada |
| Cada miembro empieza por cuatro u32: bytes de atributos de terreno, de *map props*, de modelo de mapa y de BDHC; el total (`0x10` + suma) coincide con la longitud en los 666 miembros | `land_data.c`; comprobado en los 666 |
| Atributos de terreno: `0x800` bytes = 32×32 u16, **fila-mayor**: índice = `z×32 + x` | `map.h` (`TERRAIN_ATTRIBUTES_SIZE`, `MAP_TILES_COUNT_X/Z`), `terrain_collision_manager.c` |
| **Bit 15 = colisión** (`0x8000`); byte bajo = comportamiento del tile | `map.h` (`TERRAIN_ATTRIBUTES_COLLISION_MASK`, `TILE_BEHAVIOR_MASK`); el gestor de colisión bloquea cuando el bit vale 1 |
| El ID de `land_data` de cada celda es el u16 de la sección de terreno de la matriz (fase visual 1); `0xFFFF` = celda vacía | 1604 celdas de las 289 matrices: 432 vacías, el resto con ID dentro de la tabla |
| El ID de `land_data` va por celda de la matriz y es independiente de la cabecera propietaria | mapa 411: la celda (3,27) usa `land_data` 0; otras celdas de la misma matriz usan otros miembros |

Comprobaciones empíricas con la copia privada (`--nds-map-terrain-probe`):

- **Alineación de ejes.** De 3498 NPC en celdas de su cabecera, 173 (5 %) están sobre un tile con colisión con el orden `z×32+x`; con los ejes transpuestos serían 1196 (34 %). El orden fila-mayor queda respaldado por los datos, no solo por la fuente.
- **Objetos de fondo.** 479 de 671 (carteles, etc.) están sobre tiles con colisión, como cabe esperar.
- **Caso 411 / T01.** NPC 0 en X=119, Y=878 → bloque (3,27), local (23,14), `land_data` 0, atributo `0x0000` (sin colisión). En ese bloque hay 548 de 1024 tiles con colisión, con huellas rectangulares y huecos que coinciden con la disposición de un pueblo; **esto es una observación, no una correspondencia verificada con casas o árboles concretos**.

### B. Información que exigiría interpretar modelos (no implementada)

Los *map props* (edificios: 48 bytes por entrada en la mayoría de miembros, con una excepción), el modelo del mapa (NSBMD con texturas NSBTX) y el BDHC (alturas) están en el mismo miembro pero **no se leen ni se interpretan**: no hay contornos reales de edificios ni terreno 3D. Lo dibujado son **bloqueos abstractos de colisión**, no los gráficos del juego.

### C. Comportamiento dinámico (fuera de alcance)

Puertas, escaleras, objetos y NPC que se mueven, scripts, activadores, saltos de altura y reglas por mapa dependen de la ejecución del juego. El comportamiento del tile (byte bajo) se muestra en bruto y sin interpretar.

## Qué se muestra

Capa **Terreno** (activada por defecto cuando hay datos), con leyenda y aviso permanente:

| Estado | Significado | Dibujo |
|---|---|---|
| Terreno bloqueado | el bit de colisión del dato del juego es 1 | relleno rojo y aspa × al acercar |
| Sin colisión | el bit es 0; **no** confirma que se pueda pasar | relleno turquesa tenue |
| Sin datos | celda vacía, ID fuera de tabla, fuera de la matriz o proyecto sin datos | damero gris; nunca se trata como libre |

Se mantienen los eventos, los límites de bloques y la máscara de cabeceras. La capa se dibuja con una imagen de un píxel por tile y un único pincel sobre el rectángulo visible: no hay un elemento por baldosa. Si `land_data` no está acreditado (otra ROM, otro CRC32), la capa aparece desactivada con el motivo y el editor de eventos sigue igual.

Al pasar el cursor se indica X, Y, bloque, posición local, cabecera y estado de terreno. El inspector de un evento añade la **consulta espacial**.

## Consulta espacial (para un futuro arrastre)

`NdsSpatialQuery.Assess(x, y, ignorar)` devuelve **Permitido / Bloqueado / Indeterminado según datos conocidos**, con motivos, procedencia, lo no evaluado y los ocupantes:

- *Bloqueado*: colisión de terreno (también en celdas de otra cabecera).
- *Indeterminado*: sin datos; celda de otra cabecera; casilla ocupada por otro NPC, objeto o warp; fuera de la matriz.
- *Permitido*: sin colisión de terreno, celda de esta cabecera (o contextual, con nota) y sin otro evento. Siempre incluye «No evaluado: alturas y BDHC; comportamiento del tile; puertas y escaleras; scripts y movimiento del NPC; estado dinámico del juego». Los rangos de activadores y el byte de comportamiento se informan como avisos.
- Informa además si el editor de eventos rechazaría la posición (límites de `Bounds`).

Es solo lectura. Aún no hay arrastre y no debe presentarse «Permitido» como garantía de seguridad.

## Estructura

| Pieza | Responsabilidad |
|---|---|
| `pk3DS.Core/NDS/PlatinumTerrain.cs` | lector estricto de `land_data` (perfil CPUE/1 + CRC32); muestra por tile con estado y procedencia |
| `Services/Nds/NdsSpatialQuery.cs` | consulta espacial sin escritura |
| `Services/Nds/NdsMapCanvasModel.cs`, `NdsMapCanvasControl.cs` | píxeles y capa de terreno, descripción del cursor |
| `NdsEventEditorView.cs`, `NdsProject.Events.cs`, `NdsWorkspaceView.cs` | capa, leyenda, inspector y carga perezosa; `NdsEventSession` y los checkpoints no cambian |

## Pruebas

- `--nds-map-terrain-only` (46): lector, ejes, solo bit 15, validación estricta de archivos, perfil, matriz compartida, consulta, superposición y rendimiento 960×960.
- `--nds-map-terrain-ui-only` (16): vista con y sin datos, capa, leyenda, inspector, dibujo con capa activada/desactivada, rendimiento y que guardar sigue funcionando.
- `--nds-map-terrain-probe <informe nuevo> <ROM privada>`: lee la ROM real en solo lectura y comprueba el SHA-256 de la fuente antes y después. Resultado en la copia del propietario: todas las comprobaciones OK; SHA-256 sin cambios.
- Regresión: `--nds-map-canvas-only`, `--nds-map-canvas-ui-only`, `--nds-map-layout-only`, `--nds-events-only`, `--nds-events-ui-only`, `--nds-ui-only`.

No hay validación dentro del juego ni revisión visual del propietario aún.

## Pendiente

Alturas y BDHC; interpretar el comportamiento del tile (hierba, agua, saltos…); contornos reales a partir de *map props* y modelos; objetos y puertas; y, después, el arrastre de NPC consultando `NdsSpatialQuery`.
