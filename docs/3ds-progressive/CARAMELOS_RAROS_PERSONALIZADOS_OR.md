# Caramelos Raros personalizados — Rubí Omega

Opción de «Mecánicas y CRO»: al usar un Caramelo Raro desde la Bolsa, el juego pregunta cuántos quieres usar y los aplica seguidos. Está desactivada por defecto y solo aparece en proyectos de **Rubí Omega**.

## Comportamiento en el juego

- Bolsa → Caramelo Raro → Usar → Pokémon. Con más de un Caramelo Raro aparece el selector de cantidad del propio juego; con uno solo, el uso es el original.
- El máximo seleccionable es la cantidad que tienes. La cantidad se elige en el juego al usar el objeto; el Randomizer no la fija ni entrega caramelos.
- Se aplican uno a uno con la comprobación de uso del juego. Al llegar al nivel 100, o cuando el juego no deja usar más, se detiene y no gasta los restantes.
- Aprende también los movimientos de los niveles que se salta (nivel inicial < nivel del movimiento ≤ nivel final).

Para conseguir caramelos se mantienen las vías existentes (objetos del mapa, tiendas con «Añadir caramelos raros a las tiendas», etc.).

## Cómo se instala

Editor «Caramelos Raros personalizados · Rubí Omega»: `Enable = 1` instala y `Enable = 0` retira. La vista previa no escribe nada. Aplicar confirma `code.bin`, `DllBag.cro` y `.crr/static.crr` en una sola entrada del historial; Deshacer y «Restaurar archivos» devuelven los bytes exactos. Guardar registra una operación manual reproducible en Global Template. Después hay que reconstruir la ROM.

La tarjeta de «Mecánicas y CRO» muestra el estado (sin instalar, instalado o no disponible con el motivo) sin escribir.

## Perfil acreditado

Solo uno: título `000400000011C400` (NCCH), ExHeader con `.text` de `0x479604` bytes en `0x47A` páginas, `.data` `0x5EC000`/`0x439F4`, `.bss` `0x7EC2C`, `code.bin` descomprimido de `0x530000` bytes y `DllBag.cro` de 106 496 bytes. No se busca nada por firma; cada punto se comprueba en su posición exacta:

| Recurso | Comprobación | Escritura |
|---|---|---|
| `code.bin` `0x479604–0x47A000` | relleno vacío tras `.text` | rutina: `0x479690` (328 B) y `0x479878` (428 B) |
| `code.bin` `0x2B5160` | instrucción original y huella de `0x2B5100–0x2B5270` | desvío de 4 B a `0x579A00` |
| `code.bin`, 4 funciones nativas | huella de sus primeros 32 B (`0x46DA9C`, `0x4635D0`, `0x46CC70`, `0x14FD1C`) | ninguna |
| `DllBag.cro` | huella de todo el archivo salvo cabecera de hashes y enlaces; hashes internos válidos | enlaces de 12 B en `0x2B48`, `0x2C88`, `0x3618`, `0x6C30`; hash del segmento de código |
| `static.crr` | exactamente una entrada para `DllBag.cro` | sustituye esa entrada y mantiene la lista ordenada |
| memoria | `.bss` termina en `0x6AE620` | estado de 12 B en `0x6AEFF0` (misma página, sin uso) |

Otras ediciones de `code.bin` no invalidan el perfil: solo cuentan los bytes de los que depende la rutina.

Estados: **original** (se puede instalar), **instalado** (se puede retirar), **parcial** (no se completa ni se retira a ciegas: restaurar desde el historial), **conflicto** (otra modificación ocupa un punto; también se identifica el parche de referencia instalado fuera del Randomizer) y **no acreditado** (otro título, revisión, ExHeader, funciones nativas, `DllBag.cro` modificado, hashes desfasados o CRR sin entrada). Solo «original» e «instalado» permiten escribir.

## Compatibilidad por juego

| Juego | Estado |
|---|---|
| Rubí Omega (`000400000011C400`, perfil anterior) | Implementado; probado sobre copia real; **pendiente de prueba jugable** |
| Zafiro Alfa | Sin investigar: se rechaza con ese motivo |
| X, Y, Sol, Luna, Ultra Sol, Ultra Luna | Sin investigar: la opción no aparece |
| Rubí Omega con la actualización 1.4 instalada en el emulador o la consola | No acreditado: la actualización sustituye `code.bin` |

## Convivencia

- Ningún otro parche del Randomizer escribe `DllBag.cro`. Player Level Caps, Mega Ability Sync, Persistent Items y las mentas son exclusivos de Ultra Sol/Ultra Luna.
- Con una copia real se leyeron, sin aplicarlas, las propuestas de Tiendas Gen6, MT/MO, tutores ORAS, intercambios, encuentros estáticos, objetos de campo, tabla de tipos y Pickup, antes y después de instalar: ninguna toca la rutina, el bucle de movimientos ni las funciones con huella. Shiny Rate se lee igual antes y después. O-Powers no tiene randomizador.
- Riesgo propio por probar en el juego: si el flujo terminara de forma anómala con el nivel inicial guardado, una subida de nivel posterior podría ofrecer movimientos de más (caso 10 del manual).

## Pruebas

- `--rare-candy-only` (CI, sintético, 53): ubicación de la rutina, llamadas solo a funciones con huella, rechazos de identidad y evidencia, instalación sin modificar las entradas, rangos escritos, hashes, CRR ordenada, doble aplicación, parcial, conflictos, parche de referencia externo, restauración exacta, editor con historial, reapertura, vista previa caducada, captura para Global Template y reproducción rechazada sobre archivos no acreditados.
- `--rare-candy-real <extracción OR> <salida> [<referencia>]` (copia real, solo lectura, 9): instalación y retirada exacta; con la referencia privada, `DllBag.cro` y `static.crr` resultan idénticos byte a byte a los del parche original y `code.bin` solo difiere en sus restos no alcanzables.
- `--rare-candy-project <copia del proyecto> <imagen> <reextracción>` (copia real, 22): convivencia anterior, instalación desde el editor, deshacer exacto, reproducción desde Global Template con los mismos bytes, rebuild completo y reextracción idéntica (658 archivos) con el parche reconocido.
- Ninguna ejecuta el juego. La prueba jugable está pendiente del propietario.

Procedencia y licencia: [RARE_CANDY_MULTIUSE_OR.md](../third-party/RARE_CANDY_MULTIUSE_OR.md). Fuente ARM: [`native/3ds/rare-candy-multiuse`](../../native/3ds/rare-candy-multiuse/README.md).
