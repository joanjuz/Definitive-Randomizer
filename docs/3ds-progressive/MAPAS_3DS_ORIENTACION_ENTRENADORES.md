# Editor de mapas 3DS: orientación y visión de entrenadores existentes

Estado: implementado en `feature/3ds-map-editor-trainer-facing`. Escritura verificada en copias reales; **la prueba dentro del juego está pendiente**.

## Alcance

| Juego | Orientación | Alcance de visión | Dibujo en el mapa |
| --- | --- | --- | --- |
| X / Y, Rubí Omega / Zafiro Alfa | editable: arriba, abajo, izquierda y derecha | editable de 1 a 8 casillas, solo en NPC que ya tienen visión | flecha y casillas de la línea de visión |
| Ultra Sol / Ultra Luna | editable: ángulo libre, solo en entrenadores quietos | **no localizado**: no se dibuja ni se edita | flecha |
| Sol / Luna | sin adaptador de mapas | — | — |

No se crean entrenadores ni se añade visión a NPC que no la tienen. En USUM los NPC normales no se pueden girar, y los entrenadores siguen sin poder moverse.

## Gen6: campos del NPC (registro de 0x30 bytes en la parte 1 de la zona `ZO`)

- `0x0C` (u16): orientación. **0 arriba, 1 abajo, 2 izquierda, 3 derecha.**
- `0x0E` (u16): alcance de visión en casillas, en línea recta hacia donde mira.

**Evidencia.** `--map3ds-gen6-facing-probe` recorre todos los NPC con visión y mide, para cada valor de `0x0C`, qué parte de la línea de visión pasa por casillas con el mismo permiso que la del entrenador en cada dirección:

| Valor | Zafiro Alfa (mejor dirección) | X (mejor dirección) |
| --- | --- | --- |
| 0 | arriba 98 % | arriba 82 % |
| 1 | abajo 95 % | derecha 82 %, abajo 80 % (se deduce por exclusión) |
| 2 | izquierda 97 % | izquierda 88 % |
| 3 | derecha 97 % | derecha 90 % |

Los valores 4–7 solo aparecen en NPC sin visión y no tienen dirección verificada: quedan en solo lectura. Tampoco se escribe la orientación de un NPC con un ángulo distinto de 0 en `0x2C`, porque no se ha comprobado cómo se combinan los dos campos.

**Escritura.** `Gen6ZoneEdit.SetNpcView` cambia solo `0x0C`–`0x0F` y comprueba que no varía ningún otro byte. El servicio `Map3dsNpcMoveService.PreviewView` sigue el mismo camino que mover un NPC: lectura por huella, recompresión reversible, `GarcTransaction.Build`, demás zonas idénticas y entrada del historial (`map3ds.npc.view`).

## Ultra Sol / Ultra Luna: registro de entrenador (parte 7 del `ED`, 84 bytes)

| Desplazamiento | Contenido |
| --- | --- |
| `+0x00` | tipo 7 |
| `+0x04` | posición (float ×3) |
| `+0x10` | rotación: cuaternión x, y, z, w |
| `+0x24` | condición (u32) |
| `+0x2C` | evento |
| `+0x30` / `+0x34` | modelo / script |
| `+0x3C` | bloque de movimiento: u32 tipo (4 quieto, 8 con ruta), u32 animación, f32 fotograma |
| `+0x40` / `+0x44` | formas de conversación y de cuerpo (cilindros centrados en el entrenador) |
| `+0x48` | señales de animación (entradas de 4 u32; sin interpretar) |
| `+0x4C` / `+0x50` | ruta de patrulla y acciones de la ruta |

Los desplazamientos de `+0x3C` a `+0x50` son relativos al archivo de zona. La disposición se contrastó con USUM-Studio (Aqua-0, GPL-3.0), que se usó solo como referencia; la implementación es propia.

**Orientación.** Los 239 entrenadores de Ultra Luna tienen `x = z = 0`, así que la rotación es un giro sobre el eje vertical. Sin giro, el entrenador mira hacia +Z, que es hacia abajo en el mapa. En dirección de brújula: `brújula = 180 − giro`. Entre los entrenadores que empiezan en el primer punto de su ruta, 8 tienen el primer tramo alineado con su giro a ±5° (por ejemplo, −20°, −90° y −123°), lo que fija el signo y el origen de la convención.

**Escritura.** `Gen7ZoneEdit.TurnTrainer` reescribe solo `y` y `w` del cuaternión (8 bytes). Se rechaza cuando:

- el entrenador no está quieto (movimiento distinto de 4) o tiene ruta o acciones;
- una de sus formas no es un cilindro o no está centrada en él;
- la rotación no es un giro vertical.

El servicio `Map3dsGen7NpcMoveService.PreviewView` guarda el miembro original en `map3ds-originales` para restaurarlo exactamente, igual que al mover un NPC.

**Visión: bloqueo documentado.** En el registro de entrenador no hay ningún campo de alcance identificado. Los valores del bloque de movimiento y de las señales describen animaciones. Hasta localizar el alcance con evidencia, el editor no dibuja la visión de Gen7 ni la ofrece para editar.

## Interfaz

Al seleccionar un entrenador aparece el panel «Orientación y visión» (Gen6) u «Orientación del entrenador» (USUM), con **Previsualizar** (flecha y casillas discontinuas, sin escribir), **Guardar** (entrada del historial) y **Cancelar**. En USUM, el botón «Restaurar estado anterior del área» devuelve el área byte a byte a su estado antes del último cambio.

## Pruebas

- `--map3ds-trainer-facing-only` (19, sintética) y `--map3ds-trainer-facing-ui-only` (11, fuera de pantalla), las dos en el CI.
- `--map3ds-trainer-facing-probe-gen6` / `-gen7`, en copias reales: vista previa sin escritura, reapertura, bytes cambiados, demás zonas o miembros idénticos, historial, deshacer o restaurar, y rechazo de los cambios externos.
- `--map3ds-trainer-facing-rebuild`: imagen 3DS y verificación.
- Evidencia: `--map3ds-gen6-facing-probe` y `--map3ds-gen7-event-dump`.
