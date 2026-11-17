# Editor DS · Objetos de fondo, warps y activadores existentes (Platino CPUE/1)

Amplía el editor de eventos de **Pokémon Platino USA, CPUE/1** a los eventos que hasta ahora eran solo de consulta. Usa el mismo inspector, lienzo, vista previa, checkpoints, proyectos `.drproj`, exportación y restauración. No crea ni elimina eventos, no escribe scripts y no cambia referencias que no puedan verificarse.

## Qué se puede editar

| Evento | Campos editables | Protegido |
| --- | --- | --- |
| Objeto de fondo (cartel, evento al mirar, objeto oculto) | posición; dirección desde la que se usa (7 valores con nombre) | script, tipo, altura y el resto de bytes |
| Warp | posición; mapa de destino y warp de llegada, **solo hacia un warp existente de un mapa existente** | destino de los warps dinámicos (warp 256: lo decide la partida), bytes sin uso |
| Activador | posición; ancho y alto (de 1 a 32 casillas, dentro de los límites) | script, variable, valor y altura |

Los NPC que representan objetos de suelo siguen sin poder moverse.

## Uso

1. **Nintendo DS → Eventos**, elige el mapa y haz clic en el objeto, warp o activador (o selecciónalo en las tablas).
2. El panel contextual muestra qué es, lo guardado y lo propuesto:
   - **Objeto:** desplegable «Se usa…» con la dirección desde la que se interactúa.
   - **Warp:** desplegables «Lleva al mapa» y «Llega al warp», que solo listan warps existentes. Una nota indica si la vuelta regresa a este warp. En el mapa, una etiqueta junto al warp seleccionado dice adónde lleva.
   - **Activador:** ancho y alto con − y +.
3. Para moverlo, arrastra el evento en el mapa con la herramienta «Mover NPC». La propuesta siempre lleva una advertencia, porque no se evalúan ni el terreno ni las reglas de juego de estos eventos. Aceptar solo rellena X/Y y genera la vista previa.
4. Cada cambio prepara la propuesta al momento. **Restaurar valores guardados** la descarta y **Guardar cambios…** la guarda en el proyecto o en un checkpoint nuevo.

## Datos acreditados

Fuente: `pret/pokeplatinum` (`struct_defs/events.h`, `location.h`, `overlay005/field_control.c`, `generated/bg_event_dirs.txt`, `generated/bg_event_types.txt`), contrastada con el volcado del propietario:

- 1213 warps (contados por cabecera): 1207 apuntan a un warp existente y 6 son dinámicos.
- Activadores de hasta 8×6.
- Direcciones de interacción 0–6.

| Registro | Desplazamientos |
| --- | --- |
| Objeto de fondo (20 B) | +0 script, +2 tipo, +4 X (32 bits), +8 Z (32 bits), +12 altura, +16 dirección de interacción |
| Warp (12 B) | +0 X, +2 Z, +4 cabecera de destino, +6 warp de llegada (256 = dinámico), +8 sin uso |
| Activador (16 B) | +0 script, +2 X, +4 Z, +6 ancho, +8 alto, +10 altura, +12 valor, +14 variable |

## Integridad y formatos

- `PlatinumEvents.ReplaceEvent` solo escribe esos campos y valida los límites (el área completa de un activador) y los destinos.
- La reproducción compara la huella `EventsHash`, que vacía únicamente los campos editables de cada tipo. Los destinos de los warps dinámicos quedan dentro de la huella, así que nadie puede cambiarlos.
- Una ROM exportada se reabre en el editor mediante la constante acreditada `EventsEditableSha256` y la comprobación de que todos los warps no dinámicos apuntan a un warp existente.
- **Checkpoint v5** y **lector de proyecto 3**, solo cuando el historial contiene estas ediciones. Las versiones anteriores las rechazan, y las de lector 1 y 2 se siguen escribiendo y leyendo como antes. Los checkpoints v1–v4 no cambian.

## Pruebas

| Nivel | Opción | Resultado |
| --- | --- | --- |
| Sintética | `--nds-event-editing-only` (en el CI) | 41 PASS |
| WPF fuera de pantalla | `--nds-event-editing-ui-only` (en el CI) | 12 PASS |
| Copia real CPUE/1 | `--nds-event-editing-probe <carpeta nueva> <ROM>` | PASS: huella del original, tres ediciones en Hojaverde, checkpoint v5, reapertura, exportación (solo cambia el archivo de eventos), reapertura de la ROM exportada en el editor, restauración con SHA-256 idéntico |
| Juego en emulador | — | **pendiente del propietario** (instrucciones en el `MANUAL.txt` del probe) |

Se adaptaron las comprobaciones que fijaban la regla anterior («warps, objetos y activadores no se arrastran ni se editan») al contrato nuevo. Siguen comprobando que los NPC de objetos de suelo no se mueven y que la navegación a un destino existente se valida.
