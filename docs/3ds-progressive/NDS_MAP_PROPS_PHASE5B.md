# Nintendo DS — estructuras (*map props*), fase visual 5B

Capa **de solo lectura** con la posición real y el ID de modelo de las estructuras (edificios y otros objetos del entorno) de **Platino USA CPUE/1**, sobre el lienzo de las fases 2–4. **No dibuja la forma ni los gráficos de las estructuras**: no se leen los modelos NSBMD, las texturas NSBTX ni las alturas BDHC. No hay escritura, ni arrastre de estructuras, ni soporte de otros juegos.

## Qué se averiguó

Fuentes técnicas primarias: [pret/pokeplatinum](https://github.com/pret/pokeplatinum) (`include/constants/field/map.h`, `src/overlay005/land_data.c`, `src/overlay005/map_prop.c`, `include/overlay005/map_prop.h`) y la copia privada CPUE/1, solo lectura. No se copió código ni recursos de editores externos.

| Hecho | Evidencia |
|---|---|
| Cada miembro de `land_data.narc` guarda, tras los 0x800 bytes de terreno, la sección de **map props**: entradas de **48 bytes** (`MapPropFile`) | `map_prop.c` (`mapPropFilesCount = size / sizeof(MapPropFile)`); los 666 miembros cuadran con su cabecera |
| Entrada: `i32` ID de modelo, posición `3×i32`, rotación `3×i32`, escala `3×i32`, 8 bytes sin uso | `map_prop.c` |
| Valores en coma fija 20.12 (0x1000 = 1,0); 1 tile = 16 unidades; el bloque de 32×32 tiles tiene su **origen en el centro** | `map.h` (`MAP_OBJECT_TILE_SIZE = 16·FX32_ONE`, 32×32 tiles) y los datos |
| El motor carga como máximo 32 props por mapa (`MAX_LOADED_MAP_PROPS`) | `map_prop.h`; el máximo observado es 32 |
| 3476 props en 387 miembros, 360 IDs de modelo distintos; escala siempre 1,0; **rotación siempre 0** (el motor tampoco la aplica en esa ruta) | sonda |
| Un miembro (506) tiene 146 bytes de props (3 entradas + 2 bytes sobrantes): el motor ignora el resto, igual que el lector | sonda |
| 16 entradas tienen los 8 bytes «sin uso» distintos de 0: se conservan sin interpretar | sonda |

### Interpretación de la posición (contrastada con datos, no con el motor completo)

Posición en tiles dentro de la matriz: `bloque·32 + 16 + x/16` (horizontal X) y `bloque·32 + 16 + z/16` (horizontal Z); Y es la altura. Se probaron seis combinaciones de origen y ejes frente a la **colisión de terreno** de la fase 3 (¿hay un tile con colisión a ≤ 2 tiles de la prop?):

| Hipótesis | Props cerca de colisión |
|---|---|
| **Centro del bloque (la usada)** | **88,4 %** |
| ejes intercambiados | 76,8 % |
| X invertido | 52,4 % |
| Z invertido | 34,2 % |
| ambos invertidos | 31,0 % |
| esquina del bloque | 10,0 % |

Además, el 99,3 % de las props caen dentro de su bloque. **Caso 411 / T01** (bloque (3,27), `land_data` 0): 8 props, modelos 22, 22, 23, 236 y 67×4. Los cuatro modelos «de casa» (22, 22, 23, 236) están, cada uno, a ≤ 1,5 tiles del centro de una de las **cuatro huellas de colisión** rectangulares del bloque, con un warp (puerta) en su borde. Es una evidencia fuerte de que la posición es correcta; **no demuestra qué aspecto tiene cada modelo** ni su tamaño.

## Qué se muestra

Capa **Estructuras** (activada si hay datos): un marcador con forma de casa en la posición real de cada prop, con su ID de modelo (`m23`) al acercar. Al hacer clic en una estructura el inspector muestra su **procedencia**: `land_data` y entrada, bloque, relación con la cabecera (explícita / contextual / otra), posición en la matriz y local, altura, valores crudos de posición, rotación y escala, y la nota de límites. Los eventos tienen prioridad sobre las estructuras bajo el cursor. Al pasar el cursor se indica «estructura modelo N (sin geometría)».

Seleccionar una estructura **deselecciona el evento** (con la misma confirmación que cualquier cambio de selección si hay campos o una propuesta sin guardar). Así el inspector tiene un único sujeto y Copiar, Cortar, Duplicar, Eliminar (también Ctrl+C/X/D y Supr) y los campos de posición no pueden actuar sobre un NPC que ya no se muestra. Las estructuras son de solo lectura: no se mueven, copian ni eliminan, y no se guardan en el proyecto `.drproj` ni en los checkpoints.

- Es **independiente de la capa de colisión**: la huella roja no se usa como sustituto de la casa y la casa no se dibuja como si fuera su huella.
- **Modelos/texturas**: casilla visible pero **desactivada** con la explicación («exige leer NSBMD y NSBTX, que no están implementados»).
- Los props pertenecen a la **celda** de la matriz (su `land_data`), no a una cabecera; en matrices compartidas se muestran también los de celdas de otras cabeceras, marcados como tales al seleccionarlos. Las celdas vacías no aportan nada.
- Sin datos (otra ROM, otro CRC32) la capa aparece desactivada y el editor de eventos sigue igual.

## Qué no se sabe todavía

- La forma, el tamaño, la orientación y las texturas de cada modelo (NSBMD/NSBTX de los edificios).
- A qué archivo de modelo corresponde cada ID: el motor lo resuelve con los datos de **área** (`AreaDataManager`), que no se leen aquí.
- La altura efectiva del terreno (BDHC) y cómo se asienta cada prop sobre ella.
- Para qué sirven los 8 bytes sin uso no nulos de 16 entradas.
- Si dos props del mismo bloque se superponen en el juego o están ocultos por reglas dinámicas.

## Estructura

| Pieza | Responsabilidad |
|---|---|
| `pk3DS.Core/NDS/PlatinumLandProps.cs` | lector estricto de la sección de props (perfil CPUE/1 + CRC32), conversión a tiles y colocación por matriz; `PlatinumTerrain` no cambia |
| `Services/Nds/NdsMapCanvasModel.cs` | lista de estructuras, capa, acierto por cursor y descripción con procedencia |
| `NdsMapCanvasControl.cs`, `NdsEventEditorView.cs`, `NdsProject.Events.cs`, `NdsWorkspaceView.cs` | dibujo, selección, leyenda y carga perezosa; `NdsEventSession` y los checkpoints no cambian |

## Pruebas

- `--nds-map-props-only` (31): conversión exacta de coma fija, origen en el centro, entrada parcial, validación estricta de archivos, perfil, colocación (celdas vacías, bloques compartidos, cabeceras), caso 411, modelo y rendimiento (≈ 28 800 estructuras).
- `--nds-map-props-ui-only` (16): capa y casilla de modelos/texturas, leyenda, selección con procedencia y límites, deselección del evento con las operaciones de NPC desactivadas y Supr sin efecto, prioridad de los eventos, sin datos, dibujo con/sin capa, rendimiento y diseño a 1100/760; la sesión, la ROM fuente y el disco quedan intactos.
- `--nds-map-props-probe <informe nuevo> <ROM privada>`: lee los 666 miembros reales, contrasta origen y ejes con la colisión, comprueba el caso 411 y el SHA-256 de la fuente antes y después.
- Regresión: `--nds-map-*`, `--nds-events-*`, `--nds-npc-drag-*`, `--nds-ui-only`.

Son pruebas sintéticas, fuera de pantalla y de lectura sobre la copia privada. **No hay validación visual del propietario ni comparación con una captura del juego todavía.**

## Prueba manual para el propietario

1. Abre Nintendo DS → Eventos, modo Mapa, busca **411 · T01** y acerca el bloque (3,27).
2. Comprueba que las estructuras (casa marrón con `m22`, `m23`, `m236`, `m67`) están donde esperas respecto a las casas de Pueblo Hojaverde en una captura del juego, y que cada casa coincide con una huella de colisión.
3. Haz clic en una estructura: el inspector debe mostrar procedencia y límites, y la fila del NPC que estaba seleccionado debe deseleccionarse (Copiar, Cortar, Duplicar y Eliminar desactivados; Supr no hace nada). Desactiva «Estructuras»: desaparecen sin afectar a eventos ni colisión.
4. Con un NPC con la posición editada sin guardar, haz clic en una estructura: debe pedirse confirmación; al cancelar, el NPC y sus campos siguen igual.
5. Informa de las discrepancias (posición desplazada o invertida) con el mapa y el ID de modelo, sin adjuntar ROMs.

## Pendiente

Resolver el ID de modelo → archivo con los datos de área, leer NSBMD/NSBTX (caja delimitadora y, después, geometría y texturas), alturas BDHC, contrastar con capturas del propietario y, solo entonces, dibujar contornos o geometría de los edificios.
