# Habilidades especiales · Disfraz en otras especies (Ultra Sol / Ultra Luna)

Opción experimental de «Mecánicas y CRO», desactivada por defecto: un Pokémon distinto de Mimikyu con la habilidad Disfraz anula el primer golpe que recibe en cada combate, **sin cambiar de forma ni de aspecto**. Mimikyu funciona como siempre. **No asigna Disfraz a nadie.**

## Mecanismo

En Gen7, «Disfraz intacto» es que Mimikyu esté en la forma 0 o 2, y romperlo es un cambio a la forma 1 o 3. Por eso quitar la comprobación de especie, como en las referencias antiguas, cambia a otras especies a formas reales o inexistentes (por ejemplo, Pikachu → Pikachu con gorra).

La opción engancha los cuatro manejadores de Disfraz de `Battle.cro` (`0xD8DA8` rotura; `0xDE078`, `0xDE118`, `0xDE530` comprobaciones previas):

- **Mimikyu (778):** vuelve al código original de cada manejador.
- **Otras especies:** «roto» = bit 15 del campo de 16 bits del Pokémon de combate en `+0x1C`. En la rotura se activa el bit, el daño queda en 0 y aparece el mensaje del señuelo (`0x168`). **No hay evento de cambio de forma**, y tampoco el segundo mensaje de Mimikyu.

### Por qué el bit 15 es seguro (análisis de `Battle.cro`)

- **Qué guarda el campo:** los bits 0–4 son la forma original; los bits 5–8 son banderas; los bits 9–15 son un campo declarado de 7 bits que **ningún código lee**.
- **Ciclo de vida:**
  - el constructor del Pokémon de combate (`0x7E784`) lo pone a 0;
  - la preparación desde el equipo al empezar el combate conserva los bits 8–15;
  - el cambio de forma solo escribe el bit 8 (0/1).

  Resultado: persiste al retirar y volver a sacar y al debilitarse y reanimarse; es de cada Pokémon (también en dobles), y desaparece con el combate.
- **Transformación:** copia el campo del objetivo sobre el Pokémon que se transforma. Mientras dura, Disfraz no actúa (los manejadores exigen el bit 5 apagado).

### Reserva de espacio

El bloque (240 B de rutina + marcador de 16 B + 8 B de guarda) se reserva con `CroPatchSession.TryAllocateCode`, como Mega Ability Sync. Toma un tramo libre del relleno ejecutable tras el código y solo amplía el segmento si no hay sitio.

En un `Battle.cro` original queda en `0xFC980`, sin cambiar el tamaño del archivo. Con Player Level Caps instalado antes, se coloca sin pisar ni mover la rutina que Level Caps guarda en ese relleno. Esto se corrigió durante el desarrollo: la reserva «relocatable» la desplazaba.

Retirar restaura las cuatro instrucciones originales y deja el bloque desactivado, que se reutiliza al reinstalar. Deshacer desde el historial restaura los bytes exactos.

| Perfil | Title ID | Archivo |
|---|---|---|
| Ultra Sol | `00040000001B5000` | `Battle.cro` `334AB920…` (el mismo en los dos juegos), por anclas: los 4 manejadores y las 5 funciones del juego que llama la rutina |
| Ultra Luna | `00040000001B5100` | ídem |

## Pruebas

- **Juego (propietario, 25-11-2026):** prototipo con exactamente esta rutina, «se comporta perfectamente» en US y UM. La pantalla negra al salir del combate la causaba un truco anti-ghosting de otra versión del juego, ajeno al parche.
- `--disguise-only` (CI): 9. Destinos de la rutina en cualquier posición, rechazos, editor sin escritura y Sol/Luna no ofrecido.
- `--disguise-real <RomFS US> <RomFS UM> <ExeFS UM>` (copias reales, en memoria): 23.
  - Instalación, doble instalación, retirada, reinstalación con el mismo bloque y punto ajeno.
  - **Player Level Caps y Mega Ability Sync en los dos órdenes**, y Persistent Items instalable.
  - Editor: historial, deshacer exacto y Global Template.
- `native/3ds/disguise-usum/generate.py --check` (CI): la plantilla coincide con `disguise.s` y con el prototipo validado.

## Pendiente

- Confirmar en el juego el mod generado por el instalador definitivo, con la rutina en `0xFC980` en lugar de `0xFC978`.
- Casos que el propietario no ha detallado: golpes múltiples, Rompemoldes, dobles con dos usuarios y Transformación.
