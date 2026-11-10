# Nintendo DS — mover NPC con el ratón, fase visual 4

Edición visual de la **posición (X, Y) de NPC existentes** en **Platino USA CPUE/1**, sobre el lienzo de las fases 2 y 3 ([NDS_MAP_CANVAS_PHASE2.md](NDS_MAP_CANVAS_PHASE2.md), [NDS_MAP_TERRAIN_PHASE3.md](NDS_MAP_TERRAIN_PHASE3.md)). Reutiliza por completo el flujo de la fase 1 (previsualizar → checkpoint inmutable → exportar ROM nueva → restaurar). No cambia `NdsEventSession`, el formato de los checkpoints, el motor de aleatorización ni el terreno.

**No hay validación dentro del juego de esta función.** La edición manual de X/Y y el intercambio de NPC ya fueron comprobados por el propietario en emulador; el arrastre solo es una forma de rellenar esos mismos campos.

## Cómo funciona

Barra de herramientas: **[Seleccionar] [Mover NPC]**.

- **Seleccionar** (por defecto): igual que antes. Clic selecciona; arrastrar el fondo desplaza el mapa.
- **Mover NPC**: se puede arrastrar **solo un NPC existente** (no objetos, warps, activadores ni NPC objeto de suelo). Arrastrar el fondo o los demás eventos sigue desplazando/seleccionando; el **botón central** desplaza el mapa con cualquier herramienta.
- Durante el arrastre se muestran el NPC original (anillo punteado), una línea, el **NPC fantasma** y la casilla de destino con su estado, más las coordenadas `X,Y → X,Y`. Cada estado tiene forma además de color: ✓ (sin conflictos estáticos conocidos), ? con borde discontinuo (indeterminado) y ✕ (rechazado). Un clic sin mover selecciona el NPC.
- **Esc**, cambiar de herramienta, cambiar de mapa o de modo, o perder la captura del ratón cancelan el arrastre sin tocar nada.
- Soltar en otra casilla **no guarda nada y no modifica la sesión**: abre un panel de **propuesta** con el antes/después (X/Y, bloque y posición local), el estado, los motivos exactos, lo no evaluado y, si procede, el aviso de archivo de eventos compartido. Botones: **[Aceptar propuesta]** / **[Cancelar]** (o **[Cerrar]** si está rechazada).
- **Aceptar** selecciona el NPC en las tablas e inspector, copia X/Y a los campos existentes y ejecuta la **previsualización existente** (`NdsEventSession.Preview`). Después hay que **Guardar cambios…** (checkpoint nuevo), **Exportar ROM** y, si se quiere, **Restaurar último cambio…**. El lienzo muestra un fantasma «propuesta pendiente» hasta guardar o descartar, y se refresca desde la sesión real al guardar.

Si hay campos o una propuesta sin guardar, empezar un arrastre pide la misma confirmación que cambiar de selección (`ConfirmLeave`).

## Política de validación (`NdsNpcMovePlanner`, sobre `NdsSpatialQuery.Assess` con el NPC como evento ignorado)

| Resultado | Cuándo | Qué se permite |
|---|---|---|
| **Rechazado** | colisión de terreno confirmada; fuera de la matriz; fuera de los límites del editor (`PlatinumEvents.Bounds`, los más conservadores si varias cabeceras comparten archivo); coordenadas negativas | nada: no se puede aceptar y ninguna confirmación lo evita |
| **Indeterminado (con advertencia)** | sin datos de terreno; celda de otra cabecera; casilla con otro NPC, objeto o warp; rango declarado de un activador | propuesta solo con «Aceptar propuesta con advertencia» tras ver los motivos |
| **Sin conflictos estáticos conocidos** | sin colisión de terreno, celda de esta cabecera, dentro de los límites, sin otros eventos ni rangos | propuesta normal; **nunca** se llama «posición segura» |
| Sin cambios | misma casilla | no hay propuesta |

Siempre se indica que no se evalúan alturas y BDHC, comportamiento del tile, puertas, scripts, movimiento del NPC ni estado dinámico. Las validaciones estructurales de `ReplaceNpc` (matriz, dirección 0–3) siguen siendo la autoridad final al previsualizar. La dirección no se modifica.

## Archivos de eventos compartidos

Si varias cabeceras comparten el archivo de eventos del mapa, la propuesta lo advierte («compartido por N cabeceras (…): el cambio afecta a todas y se aplican los límites más conservadores») y los límites son los de `Bounds`. No se duplican archivos ni se cambian asociaciones.

## Estructura

| Pieza | Responsabilidad |
|---|---|
| `Services/Nds/NdsNpcMove.cs` | `NdsNpcMovePlanner` (política), `NdsNpcDragSession` (píxel → tile entero con `NdsMapCanvasModel`), `NdsMapTool`; sin WPF ni escritura |
| `NdsMapCanvasControl.cs` | herramienta, arrastre (`BeginDragAt/DragTo/DropAt/CancelDrag`), fantasma, botón central, Esc; un único `DrawingContext` |
| `NdsEventEditorView.cs` | barra de herramientas, panel de propuesta, aceptación mediante los campos y `PreviewSelection` existentes |

## Pruebas

- `--nds-npc-drag-only` (29): política, conversión píxel→tile exacta con varios zoom y desplazamientos (también tiles negativos), umbral de arrastre, sin mutación de eventos, rendimiento.
- `--nds-npc-drag-ui-only` (49): herramientas, inicio de arrastre, diferencia con el desplazamiento, fantasma y estados, DPI alto, cancelación (Esc/API, herramienta, mapa, modo), propuesta, cancelar sin cambios, rechazos, advertencias, aceptar → previsualización, checkpoint con la posición arrastrada, restauración idéntica de los bytes, archivos compartidos, ausencia de escrituras durante el arrastre y diseño a 1100 y 760.
- Regresión: `--nds-map-canvas-*`, `--nds-map-terrain-*`, `--nds-map-layout-only`, `--nds-events-*`, `--nds-ui-only`.

Son pruebas sintéticas y fuera de pantalla; no se simula el ratón real ni la tecla Esc a través de WPF (se prueban las rutas que invocan).

## Prueba manual para el propietario

Usa una copia de la ROM CPUE/1 y una partida de respaldo; salida en otra carpeta.

1. Abre Nintendo DS → Eventos, modo **Mapa**, busca **411 · T01** y selecciona el **NPC 0** (X=119, Y=878).
2. Activa **Mover NPC**.
3. Arrastra el NPC a una casilla contigua sin colisión. Comprueba el fantasma y `119,878 → …`.
4. Arrástralo sobre una casilla roja (×): debe indicar «Rechazado» y no ofrecer aceptar.
5. Arrástralo sobre un warp, otro NPC o fuera de la máscara: debe pedir advertencia explícita.
6. Con un destino válido, **Aceptar propuesta**: los campos X/Y cambian y aparecen las diferencias; aún no hay checkpoint.
7. **Guardar cambios…** en un checkpoint nuevo.
8. **Exportar ROM…** a una copia nueva.
9. Abre la ROM en el emulador y comprueba el NPC en Pueblo Hojaverde (un guion puede reposicionarlo; usa un NPC que no se mueva).
10. En el editor, **Restaurar último cambio…**, guarda otro checkpoint, exporta y verifica el regreso a la posición original.

Los pasos 9 y 10 (en el juego) son del propietario; hasta que los confirme no se declara validada la función en juego.

## Pendiente

Edición de dirección con el ratón, mover varios NPC a la vez, objetos/warps/activadores, deshacer dentro del propio lienzo, alturas y comportamiento del tile, y validación en juego.
