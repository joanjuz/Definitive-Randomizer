# Cuelgue en las pruebas de los pétalos (USUM) — análisis del 2026-10-31

## El informe

- Lo reporta un tester que juega en Citra con `Crash.3ds`, una ROM de Ultra Sol generada por una preview anterior de la app con el template DEFINITIVO.
- En la prueba de Mina, después del combate del pétalo, el juego se cuelga al volver a hablar con el kahuna. Pasa con dos kahunas.
- El recordador de las cafeterías funciona.
- No hay partida guardada del momento del cuelgue.
- Aclaración del propietario:
  - El cuelgue ocurre al final de la escena, cuando se entrega el pétalo. Una de las dos veces fue con Kiawe (Pétalo Rojo; texto 487, entrenador #504).
  - En la historia principal, los kahunas funcionan bien.
  - No se puede reproducir, porque no hay partida previa.

## Lo que se comprobó

Todo se hizo sobre copias, con extracciones en `D:\Progra\Pokemon\Randomizer\AnalisisCrashPetalos`. Las ROM y las partidas no se modificaron.

**El contenedor está íntegro.**
- Cifrado de clave fija, el mismo que usan las ROM generadas por pk3DS.
- Hashes del ExHeader, superbloque de ExeFS y archivos de ExeFS: correctos.
- Superbloque del RomFS y árbol IVFC completo (L1 55, L2 6978 y L3 893 075 bloques): correctos.
- El `.code` comprimido con BLZ da el mismo `code.bin` con un descompresor independiente, portado del cargador de la 3DS (`lzss_decompress` de Luma3DS).
- **El rebuild no es la causa.**

**El contenido coincide con el original salvo lo esperado.**
- Comparado con el Ultra Sol original (`Rom.3ds`, verificado), solo cambian Battle.cro, Shop.cro, `code.bin` y los GARC de movimientos, huevos, aprendizaje, evoluciones, personal, objetos, textos, encuentros, entrenadores, estáticos y Recogida.
- **Ningún script de campo (`a/0/9/2`) ni de mapa está modificado.**

**Los textos están bien.**
- En historia solo cambian los archivos 14 (intercambios), 39 (iniciales) y 77 (cafetería), sin variar el número de líneas.
- El texto general se reescribe con el relleno de longitud de pk3DS y sin espacios finales. Es lo mismo que hace pk3DS.

**Entrenadores, objetos y regalos.**
- Kahuna Denio (#508) tiene la misma estructura que los capitanes, que no se cuelgan.
- Los pétalos (objetos 934–941) son idénticos al original.

**Los parches de `code.bin` y Battle.cro ya existían en pk3DS Eris-Ultranova 4.17.1.**
- Son: level caps, aceptar cualquier Pokémon en intercambios, recordador de cafeterías, consumibles persistentes y Mega desde el inicio.
- Las ROM `TierraPlanta.3ds` y `Zidra.3ds` llevan exactamente los mismos parches. No sirven para comparar con pk3DS.

**Causa del cuelgue: sin determinar.** Hace falta reproducirlo, con una partida previa a hablar con el kahuna o cerca de la prueba de Mina.

### Descartado: el gancho de la cafetería

`CAFE.asm` engancha `0x39A044`, el lanzador genérico de scripts de campo, y desvía al recordador (`0x157C`) los IDs de script `0x14AA`–`0x14B7`. Se analizaron los 759 scripts AMX de `a/0/9/2`, descomprimidos y con sus tablas `switch`:
- Los IDs de script son globales, y cada uno lo atiende un único script.
- `0x14AA`–`0x14B8` (15 IDs consecutivos) son casos del script 16, y `0x157C` del script 23.
- Como las cafeterías abren el recordador, el script 16 es el de las cafeterías.
- Por tanto el gancho no puede interceptar otros diálogos, en particular la entrega de los pétalos.

Aparecen valores iguales en otros scripts, pero son constantes que se pasan a funciones nativas, no lanzamientos de esos scripts.

Nota: el parche original deja fuera `0x14B8`, el decimoquinto caso. Probablemente sea una cafetería que no abre el recordador. No se cambió sin confirmarlo en el juego.

### Otras comprobaciones descartadas

- **Encuentros salvajes:** las 794 tablas son válidas (especies, formas existentes, niveles, probabilidades y SOS). El viaje con el capitán al siguiente lugar no carga datos inválidos.
- **Gancho de intercambios (`0x3843F4`):** sustituye una sola llamada dentro de la preparación del intercambio. No es una función común.
- **Líneas de texto general vaciadas o recortadas:** son del buscador de cajas, del Pokémon Resort y de los créditos.

### Estado

La causa sigue sin determinar. Ningún cambio de la ROM actúa de forma demostrable en la entrega de los pétalos. Para cerrarlo hace falta una partida anterior a la escena.

## Fallos encontrados y corregidos

### Learn.asm escribía en memoria de código

Esto afecta solo a la consola.
- El payload guardaba el puntero del Pokémon en `0x5B9D00`, dentro de las páginas de `.text` (de `0x100000` a `0x5BA000`).
- En una 3DS real esa zona es de solo lectura y ejecución, así que la escritura es un fallo de acceso. Los emuladores no aplican los permisos.

**Corrección:**
- El puntero ahora se guarda en `0x6D3FFC`, la cola de la última página del `.bss`.
- El `.bss` termina en `0x6D3F04` en Ultra Sol y en Ultra Luna, y la página llega hasta `0x6D4000`.
- Esa zona está mapeada como lectura/escritura, el arranque solo limpia hasta `0x6D3F04` y nada referencia esos bytes. `0x6D4000` es donde empiezan los CRO.

**ROM ya parcheadas:** se reconoce el payload antiguo y se reescribe solo la dirección (3 bytes en `Crash.3ds`).

### La vista USUM Relearner no funcionaba con `code.bin` sin punto

La copia de trabajo renombra el ejecutable a `.code.bin`, y el servicio lo buscaba con el nombre original. Ahora, igual que en el Global Template, si no encuentra `code.bin` busca `.code.bin`.

## Validación

`--relearner-real <Ultra Sol> <Ultra Luna> <ROM con el parche antiguo>`: 11 comprobaciones con copias reales, pasando por el parcheador original de LegacyBatch.
- La instalación limpia guarda el puntero fuera de `.text` y no escribe en la antigua dirección.
- Volver a aplicarlo no cambia nada.
- La ROM antigua se actualiza cambiando solo la dirección.
- Los originales no cambian.

Además pasan `--synthetic-only` (4059), `--legacy-parity-only` (13) y `--global-apply-only` (46).

**Pendiente:** la prueba en el juego, sobre todo en una consola real, y reproducir el cuelgue del kahuna.
