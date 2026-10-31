# NDS fase 5: Blanco 2 (USA/EUR) — 2026-10-30

Blanco 2 usa el mismo randomizer Gen5 que Negro y Blanco: entrenadores, encuentros, iniciales, objetos del mapa y tiendas. El código de cada juego vive en su perfil (`Gen5Profile` en `pk3DS.Core/NDS/Gen5Data.cs`); `Gen5Randomizer.cs` ya no tiene números propios de Negro/Blanco.

## Alcance

- Solo **Blanco 2 (USA/EUR, `IRDO`)**: es el único volcado de la secuela disponible. Negro 2 no tiene perfil y se rechaza; no se le aplican los números de Blanco 2 sin comprobarlos con un volcado real.
- Las CRC32 de las tablas coinciden con las que UPR-ZX registra para Negro 2 (U), salvo los encuentros, que son propios de Blanco 2. Todas se comprobaron con el volcado del propietario.

## Diferencias con Negro y Blanco

| Dato | Negro/Blanco | Blanco 2 |
|---|---|---|
| Scripts, entrenadores, encuentros | `a/0/5/7`, `a/0/9/2-3`, `a/1/2/6` | `a/0/5/6`, `a/0/9/1-2`, `a/1/2/7` |
| Textos: especies, entrenadores, clases, tipos, objetos | 70, 190, 191, 199, 54 | 90, 382, 383, 398, 64 |
| Iniciales | scripts 782 y 304 | script 854 (Snivy, Tepig y Oshawott) |
| Arreglo del Pokédex | llamada (`0x04`) en el script 792 | salto (`0x1E`) en el script 854, bloque de UPR-ZX `bw2NewStarterScript` |
| Gritos de los iniciales | overlay 223, se guarda sin comprimir | overlay 316, se **recomprime** (BLZ) en su hueco |
| Texto del tipo de cada inicial | texto 430, líneas 18–16 | texto 169, líneas 37–35 |
| Objetos del mapa | scripts 864/865 | scripts 1240/1241 (400 visibles y 213 ocultos) |
| Tiendas | 26 listas en el overlay 21 | 32 archivos del NARC `a/2/8/2` (los 48 restantes están vacíos y no se tocan) |
| Tiendas fijas | MT 1, 2, 4, 7; progreso 0, 16, 20, 23–25 | Poké Mart 0–5; MT 7, 10, 12, 17, 24 (como UPR-ZX) |

Los objetos que no se mueven se comprobaron con los nombres de Blanco 2: los mismos rangos de MT, MO y objetos clave que en Negro; todos los objetos nuevos de la secuela (621–638) son objetos clave.

## Validación

- `--nds-only` (sintético, CI): 38; comprueba que el perfil de Blanco 2 usa el NARC de tiendas y sus propios datos de iniciales, y que Negro/Blanco conservan los suyos.
- `--nds-gen5-w2-probe` sobre el volcado real, con todas las opciones a la vez: 21/21. Lectura (813 entrenadores y los 8 líderes en orden, 610 áreas, iniciales, objetos y 32 tiendas); entrenadores con mismos tamaños, niveles y objetos; encuentros con mismas tasas y niveles; iniciales en scripts, gritos (overlay 316 comprimido en su hueco), imágenes y texto del tipo; salto del Pokédex; objetos y tiendas con el mismo conjunto y las listas fijas intactas; resto de archivos idénticos; misma semilla, mismo resultado; original sin cambios.
- Ventana DS: `--nds-ui-probe` 9/9 con Blanco 2, Negro y Blanco ES. Pruebas reales anteriores de Negro y Blanco ES sin cambios: 16, 14, 20 y 20.
- ROM de prueba para el juego: `D:\Nintendo\DS\Juegos\Pruebas\Blanco2-todo-prueba.nds` (todas las opciones, semilla 20261030, 7957 cambios; iniciales Pikachu, Charmander y Squirtle).

## Pendiente

Prueba en juego (partida nueva): elegir inicial (imagen, grito, tipo en el texto), comprobar que el Pokédex se recibe, combates contra entrenadores y salvajes, recoger objetos y comprar en una tienda especial.
