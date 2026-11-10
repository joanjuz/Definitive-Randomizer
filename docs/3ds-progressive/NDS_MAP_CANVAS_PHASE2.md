# Nintendo DS — lienzo esquemático de eventos, fase visual 2

Vista **de solo lectura** para **Platino USA CPUE/1**, dentro de **Nintendo DS → Eventos**. Base: `main` `2238113` (PR #55 editor de eventos y PR #62 lector `PlatinumMapLayout`). No añade soporte de otros juegos, revisiones ni idiomas.

## Qué es y qué no es

Es una **vista esquemática de eventos**: una rejilla con el tamaño de la matriz del mapa y marcadores en las coordenadas que el juego guarda para cada evento. La vista lo indica siempre: «Vista esquemática de eventos. No representa los gráficos ni las colisiones del juego».

No es un render auténtico. No lee ni dibuja terreno, modelos, texturas, colisiones, alturas ni permisos de paso (`land_data`, NSBMD/NSBTX, BDHC), y no los infiere. No hay edición: no arrastra, no mueve ni escribe nada desde el mapa. Las operaciones de previsualizar, guardar checkpoint y restaurar son las de la fase 1, sin cambios, y la vista las alimenta solo a través de la selección ya existente.

## Datos y semántica de coordenadas

- Tamaño: `ancho × alto` de la matriz (en bloques) × 32 tiles. Un mapa 411 usa una matriz de 30×30 bloques (960×960 tiles).
- X/Y de evento: tiles dentro de **esa matriz**, no un espacio global entre matrices (ver [NDS_MAP_LAYOUT_PHASE1.md](NDS_MAP_LAYOUT_PHASE1.md)). Caso acreditado: mapa 411 / T01, NPC 0, X=119, Y=878 → bloque (3,27), posición local (23,14).
- Una matriz puede ser compartida. Con cabeceras almacenadas, **solo las celdas cuyo ID es el del mapa seleccionado** se muestran como «asociadas a esta cabecera» (en 411, solo la celda (3,27)); el resto aparece como «celda de otra cabecera» y no se reinterpreta como parte del mapa. Sin cabeceras almacenadas, todas las celdas se marcan «cabecera contextual», que no es lo mismo que asociación explícita.
- Los eventos fuera de la máscara de la cabecera (en una celda ajena) o fuera de la matriz **se muestran en su posición real**, sin recortarlos ni corregirlos: anillo discontinuo en el primer caso, aspa en el segundo; el área visible se amplía para incluirlos y el inspector lo explica.
- Activadores: el rectángulo usa los campos de rango declarados (+6/+8) del registro; se rotula «rango declarado», no como geometría validada.
- El juego no se consulta para decidir si una baldosa es transitable: no se valida ni se advierte sobre ello.

## Interfaz

- Selector `[Mapa | Tablas]`: las tablas, el selector de 593 mapas, el inspector, la navegación de warps y los botones de guardado existentes no cambian. «Mapa» es el modo inicial.
- Lienzo: cuadrícula de tiles (a partir de 8 px/tile), líneas y rótulos de bloques de 32×32, borde de matriz, escala y dimensiones, leyenda, posición bajo el cursor (X, Y, bloque, local, cabecera).
- Formas distintas por tipo: círculo NPC, triángulo NPC objeto de suelo, cuadrado objeto de fondo, rombo warp, rectángulo discontinuo activador; las celdas se distinguen además por trama (sólida, diagonal ascendente, diagonal descendente), no solo por color.
- Interacción: rueda y botones −/+ (zoom 0,1–64 px/tile, anclado en el cursor), arrastre para desplazar, clic para seleccionar, «Ajustar a eventos», «Ver matriz completa». Teclado: flechas = evento vecino, Ctrl+flechas = desplazar, Re Pág/Av Pág = anterior/siguiente, +/−, F, Inicio.
- Capas: NPC, objetos, warps, activadores, bloques, tiles y máscara de cabecera, con nombres accesibles.
- Sincronización: seleccionar en la tabla resalta el marcador y viceversa; el inspector añade la posición (bloque, local, relación con la cabecera). Si hay campos o una propuesta sin guardar, cambiar la selección pide confirmar igual que desde las tablas.

## Estructura

| Pieza | Responsabilidad |
|---|---|
| `PlatinumEvents.Layout(mapId)` (Core, mínimo) | entrega `PlatinumMapLayout` de la matriz del mapa; no altera eventos ni bytes |
| `Services/Nds/NdsMapCanvasModel.cs` | modelo puro sin WPF: tile↔píxel, zoom y desplazamiento acotados, ajuste, recorte al viewport, hit-test, selección, vecinos, clasificación de celdas y eventos |
| `NdsMapCanvasControl.cs` | un único `FrameworkElement` con `DrawingContext`; sin elementos visuales por baldosa; no escribe datos |
| `NdsEventEditorView.cs` | modos, capas, leyenda y sincronización; la fuente de verdad sigue siendo `NdsEventSession` |

`PlatinumMapLayout`, `NdsEventSession` y los formatos de checkpoint no se modifican.

## Pruebas

- `--nds-map-canvas-only`: 32 comprobaciones del modelo (conversión exacta, zoom, límites, ajuste, recorte, hit-test, selección, matrices no cuadradas y 30×30, celdas explícitas/contextuales/ajenas, eventos fuera de máscara y de matriz, rendimiento con 4000 eventos).
- `--nds-map-canvas-ui-only`: 33 comprobaciones fuera de pantalla con una ROM sintética tipo 411: sincronización tabla↔lienzo, cambio de mapa, modos, capas, accesibilidad, 1100 y 760 unidades, tiempo de dibujo de 960×960, ausencia de cambios en sesión/fuente/disco, y que previsualizar y guardar siguen funcionando y refrescan el lienzo. Las capturas se guardan en `nds-map-canvas/` junto al ejecutable (no se versionan).
- Regresión focalizada: `--nds-map-layout-only`, `--nds-events-only`, `--nds-events-ui-only`, `--nds-ui-only`.

Todas son sintéticas o fuera de pantalla. **No hay validación dentro del juego** de esta vista ni se probó con una ROM privada en esta fase.

## Pendiente

Edición visual de posiciones de NPC (fase siguiente, por el flujo Previsualizar → checkpoint → exportación ya comprobado), permisos y colisiones reales (`land_data`), render con tiles/texturas, y revisión manual de DPI y alto contraste en el equipo del propietario.
