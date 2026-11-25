# Habilidades especiales · Multitipo en otras especies (Ultra Sol / Ultra Luna)

Opción experimental de «Mecánicas y CRO», desactivada por defecto: un Pokémon distinto de Arceus que tenga la habilidad Multitipo toma el tipo de la Tabla que lleva, o Normal sin Tabla. **No asigna Multitipo a nadie**; la habilidad se reparte en Personal o con el randomizador.

## Mecanismo

En `code.bin` hay dos funciones que devuelven el tipo 1 y el tipo 2 de un Pokémon (`CoreParam`). Si la especie es Arceus (493) y la habilidad es Multitipo (121), devuelven el tipo de la Tabla equipada. Silvally (773) con Sistema Alfa tiene su propia rama, y el resto de Pokémon lee los tipos de Personal.

El parche sustituye en las dos funciones la comprobación de especie `sub r2, r5, #0x100; subs r2, r2, #0xED` por `cmp r0, #0x79; nop`, donde `r0` contiene la habilidad. Resultado:

- Multitipo usa la rama de la Tabla en cualquier especie.
- Cualquier otra habilidad sigue el camino original, incluido Silvally con Sistema Alfa. El parche publicado por ABZB (`cmp r0, r0`) se saltaba esa rama.
- Arceus con Multitipo no cambia.
- No se leen ni se escriben formas, modelos ni estadísticas.

| Juego | Title ID | `code.bin` | Puntos (8 B cada uno) | Huella de las dos funciones |
|---|---|---|---|---|
| Ultra Sol | `00040000001B5000` | `b5388f75…` | `0x3AF2A0`, `0x3AF334` | ventana `0x3AF280` + `0x12C` |
| Ultra Luna | `00040000001B5100` | `0aa12056…` | `0x3AF2A8`, `0x3AF33C` | ventana `0x3AF288` + `0x12C` |

Cada juego tiene su propio perfil: no se usan las posiciones de un juego en el otro. Si la huella o el tamaño no coinciden, el juego no está acreditado y no se escribe nada. Los estados son: original, instalado, parcial, conflicto (otras instrucciones en el punto) y no acreditado.

## Evidencia

- **Ejecución de las dos funciones reales** de US y de UM, original y parcheada, con un emulador ARM (Unicorn) y los accesos a especie, habilidad, objeto y Personal simulados. Se probaron 24 casos por juego (Arceus, Pikachu y Silvally, con Multitipo, Sistema Alfa u otra habilidad; sin objeto, con Tabla Llama, con otra Tabla y con Memoria Fuego):
  - Con el parche, una especie con Multitipo devuelve exactamente lo mismo que Arceus con Multitipo y el mismo objeto.
  - Todo lo demás devuelve lo mismo que el original.

  El arnés está fuera de Git (scratchpad de la sesión).
- `--multitype-only` (CI, sintético): 26 comprobaciones.
  - Solo cambian 16 bytes; doble instalación, instalación parcial, conflicto, otra revisión, otro juego y tamaño se rechazan; la retirada es exacta.
  - El editor: vista previa sin escritura, historial, captura para Global Template, deshacer exacto, retirada con 0 y rechazo en Sol/Luna.
- `--multitype-real <ExeFS US> <ExeFS UM> <RomFS UM> <salida>` (copias reales, en memoria): 13 comprobaciones.
  - Perfiles reales, perfil del otro juego rechazado, retirada exacta.
  - Shiny Rate sin cambios y las mentas siguen identificando el `code.bin`.
  - Player Level Caps en los dos órdenes.
  - Genera los `code.ips` de la prueba jugable.

## Convivencia

- **Caramelos Raros USUM y mentas:** escriben `Bag.cro`; este parche solo escribe `code.bin`, en dos puntos ajenos a ellos. Las mentas siguen identificando el `code.bin`.
- **Mega Ability Sync y Persistent Items:** escriben `Battle.cro`.
- **Player Level Caps:** escribe `code.bin` en otros puntos; la convivencia está comprobada en los dos órdenes.
- **Shiny Rate:** sin cambios.

## Riesgos y pendientes

- Sin prueba jugable todavía.
- **El tipo se lee de estas funciones; no está comprobado si el combate lo recalcula al cambiar de objeto.** ABZB indica que, si se retira la Tabla en combate, el tipo no cambia hasta salir del combate. Juicio usa el tipo de la Tabla por otra vía.
- **Desarme y Truco** probablemente quitan la Tabla a especies que no son Arceus: la inmunidad parece ligada a la especie y no se ha tocado.
- **Formas:** Arceus sigue cambiando de forma con la Tabla como siempre (otra función); las demás especies no cambian de forma.
- Sol/Luna, X/Y y Rubí Omega/Zafiro Alfa tienen funciones equivalentes según el análisis estático, pero no están acreditados.
