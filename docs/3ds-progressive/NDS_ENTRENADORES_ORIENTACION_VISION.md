# Editor DS · Orientación y visión de entrenadores existentes (Platino CPUE/1)

Amplía el editor de eventos de **Pokémon Platino USA, CPUE/1** ([fase 1](NDS_EVENT_EDITOR_PHASE1.md), [lienzo](NDS_MAP_CANVAS_PHASE2.md), [arrastre](NDS_NPC_DRAG_PHASE4.md)). Permite girar un NPC existente y cambiar el alcance de visión de un entrenador existente desde el mapa, con previsualización, guardado, exportación y restauración por el flujo que ya existía. No crea entrenadores, no cambia equipos ni scripts y no se aplica a otros juegos.

## Uso

1. **Nintendo DS → Eventos**, elige el mapa y haz clic en el entrenador.
2. El panel **Orientación y visión** muestra hacia dónde mira y su alcance, guardado y propuesto. En el mapa aparecen una flecha y las casillas que alcanza.
3. Para girarlo: botones **↑ Arriba / ↓ Abajo / ← Izquierda / → Derecha**, las flechas pequeñas junto al NPC (con zoom suficiente) o **Mayús+flecha** con el foco en el mapa.
4. Para el alcance: **−** y **+** (o **Mayús+−** / **Mayús++** en el mapa), o escribe el valor.
5. Cada cambio prepara la propuesta al momento: las diferencias aparecen abajo y el mapa muestra la visión nueva (relleno) junto a la guardada (punteada). **Restaurar valores guardados** vuelve a los valores guardados sin guardar nada.
6. **Guardar cambios…** guarda en el proyecto o en un checkpoint nuevo. El entrenador sigue seleccionado y el mapa conserva zoom y posición.
7. **Exportar ROM** genera la copia jugable. **Restaurar último cambio…** deshace el cambio con el mismo flujo.

Las capas **Visión** (seleccionado) y **Visión de todos** (todos los entrenadores visibles, tenue) se activan en la barra de capas.

## Datos del juego (acreditados)

Fuente: decompilación pública `pret/pokeplatinum` (`constants/map_object.h`, `struct_defs/events.h`, `trainer_encounter.c`, `map_object.c`, `res/field/scripts/battles.s`, `vs_seeker.c`), contrastada con el volcado CPUE/1 del propietario. No se incluye código del proyecto citado.

| Campo del NPC (32 bytes) | Significado |
| --- | --- |
| `+12` dirección | 0 = arriba (norte), 1 = abajo (sur), 2 = izquierda (oeste), 3 = derecha (este) |
| `+6` tipo de entrenador | 0 no es entrenador; 1 detecta hacia donde mira; 2 detecta en las cuatro direcciones; 4–8 giran solos y se comprueban como 1 con la dirección de cada momento; 3 objeto de suelo; 9–11 sin identificar |
| `+14` (`data[0]`) | alcance de visión **solo** en los tipos 1, 2 y 4–8. En otros NPC contiene otros datos (hasta 117 en el original): no se muestra ni se edita como visión |
| `+4` movimiento | 14–17 = «mirar» fijo arriba/abajo/izquierda/derecha |

Censo del original (todas las cabeceras, archivo de eventos único): 3555 NPC, 439 entrenadores con visión, alcance máximo **6**; 266 entrenadores «mirar fijo», 265 con la dirección acorde a su movimiento.

**Giro duradero.** El propio juego gira a un entrenador de forma permanente cambiando su movimiento a «mirar» en la dirección nueva. Por eso, al girar un NPC con movimiento 14–17 se escriben la dirección y el movimiento acorde (14 + dirección). Con cualquier otro movimiento solo se escribe la dirección, y la interfaz avisa de que es la dirección inicial: si el NPC pasea o mira alrededor, puede cambiar durante el juego.

**Alcance admitido: 0–6.** El motor no tiene un tope explícito. 6 es el máximo que usa el juego original. Los valores mayores no están acreditados y se rechazan. 0 = no detecta.

## Lo que reproduce la superposición y lo que no

Regla del motor: el entrenador ve al jugador si está en su misma fila o columna, en la dirección comprobada, a una distancia entre 1 y el alcance, y ninguna casilla intermedia colisiona (terreno, comportamiento de la casilla, otro objeto del mapa o cambio de altura). Un entrenador derrotado no detecta.

La capa es un **alcance geométrico estimado**:

- **Exacto:** la dirección, el alcance, la línea recta, las cuatro direcciones del tipo 2 y el corte en el borde de la matriz.
- **Aproximado:** se marcan las colisiones de `land_data` (bloqueado; lo de detrás queda en sombra) y los NPC en su posición guardada (ocupado, en sombra detrás). No se evalúan el comportamiento de cada casilla, las alturas/BDHC, los NPC que se mueven, los scripts ni el estado de derrota. Sin datos de terreno, las casillas aparecen como «sin datos», no como libres.

No es una reproducción jugable acreditada. La fidelidad se comprueba en el emulador.

## Formatos e integridad

- `PlatinumNpcValues` gana `Movement` y `Sight` opcionales. Valen null cuando no forman parte del cambio, y los campos que no cambian se descartan al previsualizar.
- **Checkpoint v4**, solo si algún cambio incluye movimiento o visión. Las versiones anteriores no admiten esos campos, y una v4 sin ellos se rechaza. Un giro simple sigue siendo v1. Los checkpoints v1–v3 existentes se leen igual.
- **Proyecto `.drproj`:** el lector pasa a la versión 2. Un proyecto cuyo historial de eventos incluya movimiento o visión exige el lector 2 (`MinReaderVersion = 2`); una compilación anterior lo rechaza con su mensaje de «versión más reciente» en lugar de reproducir otra cosa. Sin esos cambios, el proyecto sigue requiriendo el lector 1.
- `ReplaceNpc` solo escribe `+4` (si el movimiento era 14–17 y coincide con la dirección), `+12`, `+14` (si es un tipo de entrenador con visión y el valor está entre 0 y 6) y `+24/+26`. Los demás bytes no cambian.
- Al reproducir, la huella `EditableHash` vacía únicamente esos campos, y solo en los registros donde son editables. Si se cambia un movimiento que no es 14–17, una visión en un NPC normal o un byte desconocido, la operación se rechaza.
- Una ROM exportada con estos cambios se reabre en el editor mediante la segunda huella acreditada (`EditableEventsSha256`) y la comprobación de que todo alcance de entrenador está entre 0 y 6. La huella de fase 1 sigue intacta.

## Pruebas

| Nivel | Opción | Resultado |
| --- | --- | --- |
| Sintética | `--nds-trainer-vision-only` | 74 PASS |
| WPF fuera de pantalla (1100×760 y 760×560, 125 % y 150 %) | `--nds-trainer-vision-ui-only` | 34 PASS |
| Copia real CPUE/1 | `--nds-trainer-vision-probe` | PASS: huellas, censo, guardado v4, reapertura, exportación (3 bytes del archivo de eventos), reapertura de la ROM exportada, restauración con SHA-256 idéntico y fuente intacta |
| Juego en emulador | Ruta 202, NPC 1 (`Platino-vision.nds` y `Platino-restaurado.nds`) | **confirmado por el propietario** (2026-11-17): cambió de dirección, detectó al jugador con alcance 6 y la restauración se comportó como el original |

Las comprobaciones de interfaz invocan los mismos métodos que los manejadores de ratón y teclado (`TurnAt`, `FacingKey`); no son entrada física real.

## Prueba jugable (confirmada el 2026-11-17)

El probe deja `MANUAL.txt` con el entrenador elegido: **Ruta 202, NPC índice 1, script 3002**. En el original mira abajo con visión 4; tras el cambio mira a la derecha con visión 6.

1. Usa una partida de prueba guardada **fuera** de la Ruta 202. El juego restaura los NPC del mapa en el que se guardó la partida, así que el cambio no se vería hasta salir del mapa y volver a entrar.
2. Con la ROM `Platino-vision.nds`, entra en la Ruta 202 y acércate por la derecha del entrenador, en su misma fila. Debe detectarte a 6 casillas o menos si no hay nada en medio, y no a 7.
3. Pasa por debajo, en su columna: ya no debe detectarte.
4. Con `Platino-restaurado.nds`, el entrenador vuelve a mirar abajo con visión 4.

Con el editor: abre la ROM, mapa 343 · Ruta 202, NPC 1, pulsa «→ Derecha» y «+» dos veces, guarda y exporta.
