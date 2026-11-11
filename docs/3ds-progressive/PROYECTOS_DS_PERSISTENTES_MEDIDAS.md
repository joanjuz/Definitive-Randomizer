# Mediciones para el formato de proyecto DS persistente

Informe de `--nds-project-measure` (sonda real de solo lectura) sobre copias privadas de Platino (USA rev. 1), Diamante (USA rev. 5), HeartGold (USA), Negro (USA/EUR) y Blanco 2 (USA/EUR). Blanco (España) no estaba disponible y comparte estructura Gen5 con Negro y Blanco 2. Cada ROM se abre en memoria, se aplican seis operaciones representativas y se mide el tamaño de las tablas que cambian y el coste de hacerlas duraderas en el disco del propietario (escritura, `Flush(true)`, relectura con SHA-256 y reemplazo). Ver la decisión en [PROYECTOS_DS_PERSISTENTES.md](PROYECTOS_DS_PERSISTENTES.md).

«Miembros» es el tamaño si solo se guardaran los miembros NARC que cambian (índice, longitud y bytes de cada miembro distinto). En las ediciones manuales cambia un único miembro de 42 a 76 bytes, así que el total es de 50 a 84 bytes: la primera versión de este informe lo dividía entre 1024 con división entera y mostraba «0 KiB». Se revisó el 10/11/2026 repitiendo la sonda: el cálculo era correcto y solo fallaba la presentación; ahora los tamaños inferiores a 1 KiB se dan en bytes. La variante por miembros sigue descartada (ver la decisión). Tiempos de «Comprimir»: Brotli rápido, una sola pasada. Salvo donde se indica «B», los tamaños están en KiB.

| ROM | Operación | Archivos | Bruto (KiB) | Miembros (KiB) | Brotli (KiB) | Brotli miembros (KiB) | Deflate (KiB) | Comprimir (ms) |
|---|---|---|---|---|---|---|---|---|
| Pokémon Platino (USA, rev. 1) | Edición manual · un entrenador | 2 | 53 | 52 B | 14 | 54 B | 23 | 4 |
| Pokémon Platino (USA, rev. 1) | Edición manual · una especie | 1 | 25 | 52 B | 13 | 56 B | 17 | 15 |
| Pokémon Platino (USA, rev. 1) | Randomizar entrenadores y salvajes | 3 | 125 | 106 | 26 | 24 | 43 | 3 |
| Pokémon Platino (USA, rev. 1) | Entrenadores (equipos y reglas) | 2 | 56 | 48 | 15 | 15 | 25 | 2 |
| Pokémon Platino (USA, rev. 1) | Datos · estadísticas, tipos y habilidades | 1 | 25 | 25 | 14 | 14 | 17 | 1 |
| Pokémon Platino (USA, rev. 1) | Ajustes del juego | 1 | 214 | 214 | 147 | 147 | 155 | 5 |
| Pokémon Diamante (USA, rev. 5) | Edición manual · un entrenador | 2 | 44 | 50 B | 11 | 52 B | 19 | 1 |
| Pokémon Diamante (USA, rev. 5) | Edición manual · una especie | 1 | 25 | 52 B | 13 | 56 B | 16 | 0 |
| Pokémon Diamante (USA, rev. 5) | Randomizar entrenadores y salvajes | 3 | 117 | 100 | 24 | 22 | 40 | 2 |
| Pokémon Diamante (USA, rev. 5) | Entrenadores (equipos y reglas) | 2 | 47 | 41 | 14 | 13 | 22 | 1 |
| Pokémon Diamante (USA, rev. 5) | Datos · estadísticas, tipos y habilidades | 1 | 25 | 25 | 14 | 14 | 17 | 1 |
| Pokémon Diamante (USA, rev. 5) | Ajustes del juego | 1 | 202 | 202 | 138 | 138 | 147 | 5 |
| Pokémon Oro HeartGold (USA) | Edición manual · un entrenador | 2 | 46 | 68 B | 14 | 76 B | 22 | 1 |
| Pokémon Oro HeartGold (USA) | Edición manual · una especie | 1 | 25 | 52 B | 14 | 56 B | 17 | 1 |
| Pokémon Oro HeartGold (USA) | Randomizar entrenadores y salvajes | 3 | 68 | 53 | 21 | 20 | 31 | 1 |
| Pokémon Oro HeartGold (USA) | Entrenadores (equipos y reglas) | 2 | 45 | 37 | 13 | 13 | 21 | 1 |
| Pokémon Oro HeartGold (USA) | Datos · estadísticas, tipos y habilidades | 1 | 25 | 25 | 14 | 14 | 17 | 1 |
| Pokémon Oro HeartGold (USA) | Ajustes del juego | 1 | 220 | 220 | 151 | 151 | 160 | 5 |
| Pokémon Negro (USA/EUR) | Edición manual · un entrenador | 2 | 33 | 52 B | 10 | 59 B | 15 | 1 |
| Pokémon Negro (USA/EUR) | Edición manual · una especie | 1 | 45 | 68 B | 21 | 72 B | 27 | 1 |
| Pokémon Negro (USA/EUR) | Randomizar entrenadores y salvajes | 3 | 66 | 50 | 19 | 17 | 27 | 1 |
| Pokémon Negro (USA/EUR) | Entrenadores (equipos y reglas) | 2 | 37 | 32 | 11 | 10 | 17 | 1 |
| Pokémon Negro (USA/EUR) | Datos · estadísticas, tipos y habilidades | 1 | 45 | 43 | 22 | 21 | 27 | 1 |
| Pokémon Negro (USA/EUR) | Ajustes del juego | 1 | 246 | 246 | 166 | 166 | 179 | 6 |
| Pokémon Blanco 2 (USA/EUR) | Edición manual · un entrenador | 2 | 48 | 52 B | 16 | 60 B | 24 | 1 |
| Pokémon Blanco 2 (USA/EUR) | Edición manual · una especie | 1 | 59 | 84 B | 26 | 88 B | 34 | 1 |
| Pokémon Blanco 2 (USA/EUR) | Randomizar entrenadores y salvajes | 3 | 84 | 66 | 26 | 23 | 37 | 1 |
| Pokémon Blanco 2 (USA/EUR) | Entrenadores (equipos y reglas) | 2 | 50 | 42 | 15 | 14 | 24 | 1 |
| Pokémon Blanco 2 (USA/EUR) | Datos · estadísticas, tipos y habilidades | 1 | 59 | 53 | 27 | 25 | 34 | 1 |
| Pokémon Blanco 2 (USA/EUR) | Ajustes del juego | 1 | 261 | 261 | 177 | 177 | 191 | 6 |

| ROM | Archivos | Bruto (KiB) | Brotli (KiB) | Instantánea completa (ms) | Registro incremental (ms) | Lectura+verificación (ms) |
|---|---|---|---|---|---|---|
| Pokémon Platino (USA, rev. 1) | 5 | 373 | 191 | 46 | 44,3 | 2 |
| Pokémon Diamante (USA, rev. 5) | 5 | 352 | 181 | 54 | 39,7 | 2 |
| Pokémon Oro HeartGold (USA) | 5 | 320 | 189 | 43 | 40,4 | 2 |
| Pokémon Negro (USA/EUR) | 5 | 363 | 210 | 53 | 45,6 | 2 |
| Pokémon Blanco 2 (USA/EUR) | 5 | 411 | 233 | 46 | 43,5 | 2 |

**El coste duradero depende del disco.** Los tiempos de la tabla anterior se midieron en el disco mecánico donde están las ROM (unidad D:, SATA). Repetida la sonda el 10/11/2026 con la misma entrada: en ese disco, instantánea completa 28–50 ms y registro incremental 21–45 ms; en un SSD NVMe (unidad C:), instantánea completa 5–9 ms y registro incremental 1,3–2,5 ms. En el SSD el registro incremental es unos 4 ms más rápido en términos absolutos, una diferencia que no percibe quien confirma una operación; la decisión (instantánea completa, sin diario) se mantiene en ambos.

## Contenedor real (10/11/2026)

Las tablas anteriores miden solo las tablas del juego. La sonda mide ahora también el archivo que escribe `NdsProjectCodec`, con el historial de operaciones y sus listas de cambios, tras las seis operaciones y tras 30 randomizaciones más de entrenadores y salvajes.

| ROM | Operaciones | Cambios | Manifiesto | Archivo | Serializar (ms) | Leer y verificar (ms) |
|---|---|---|---|---|---|---|
| Pokémon Platino (USA, rev. 1) | 6 | 16 748 | 1 KiB | 203 KiB | 77 | 83 |
| Pokémon Platino (USA, rev. 1) | 36 | 297 418 | 1 KiB | 2858 KiB | 447 | 569 |
| Pokémon Diamante (USA, rev. 5) | 6 | 16 085 | 1 KiB | 193 KiB | 13 | 17 |
| Pokémon Diamante (USA, rev. 5) | 36 | 286 965 | 1 KiB | 2720 KiB | 221 | 403 |
| Pokémon Oro HeartGold (USA) | 6 | 15 581 | 1 KiB | 184 KiB | 11 | 23 |
| Pokémon Oro HeartGold (USA) | 36 | 270 627 | 1 KiB | 2430 KiB | 185 | 395 |
| Pokémon Blanco 2 (USA/EUR) | 6 | 17 027 | 1 KiB | 219 KiB | 15 | 18 |
| Pokémon Blanco 2 (USA/EUR) | 36 | 251 639 | 1 KiB | 2308 KiB | 182 | 350 |
| Pokémon Negro (USA/EUR) | 6 | 14 376 | 1 KiB | 180 KiB | 10 | 20 |
| Pokémon Negro (USA/EUR) | 36 | 192 098 | 1 KiB | 1710 KiB | 136 | 238 |

Antes de mover el historial a una tabla comprimida, el mismo proyecto de seis operaciones de Platino tenía un manifiesto de 1847 KiB (archivo de 1891 KiB) y el de 36 operaciones no se podía escribir. «Serializar» es en frío (todo el historial de una vez, y la primera fila incluye el arranque del serializador); durante una sesión solo se serializa la operación nueva.

## Coste de guardar con el historial creciente (11/11/2026)

`--nds-project-save-bench` sobre los cinco volcados (Blanco no disponible), con los proyectos en el disco mecánico D: (donde están las ROM). Se aplican 40 randomizaciones completas seguidas, alternando especies (entrenadores y salvajes) y datos (estadísticas, tipos y habilidades); en cada punto se confirman cinco ediciones manuales de una especie y se da la mediana. «Serializar», «Verificar» y «Leer al abrir» son las funciones de `NdsProjectCodec` sobre el proyecto de ese punto, con la caché de la sesión caliente (mediana de tres). «Antes» es el diseño de los PR #74–#81 (una sola tabla `history` y relectura completa del principal en cada `Commit`); «después», bloques de historial y principal conocido.

| ROM | Randomizaciones | Cambios | Archivo (KiB) | Bloques | Guardar edición manual (ms) antes → después | Serializar (ms) | Verificar (ms) | Leer al abrir (ms) | Primer guardado tras reabrir (ms) |
|---|---|---|---|---|---|---|---|---|---|
| Pokémon Platino (USA, rev. 1) | 1 | 8 683 | 119 | 2 | 84 → 42 | 4 → 1 | 3 → 1 | 24 → 29 |  |
| Pokémon Platino (USA, rev. 1) | 10 | 65 418 | 650 | 6 | 185 → 71 | 16 → 2 | 15 → 1 | 82 → 79 |  |
| Pokémon Platino (USA, rev. 1) | 20 | 130 488 | 1259 | 11 | 295 → 61 | 32 → 2 | 29 → 2 | 165 → 155 | 501 → 57 |
| Pokémon Platino (USA, rev. 1) | 40 | 260 627 | 2474 | 21 | 510 → 121 | 62 → 3 | 53 → 4 | 324 → 314 | 896 → 125 |
| Pokémon Diamante (USA, rev. 5) | 1 | 8 343 | 112 | 2 | 75 → 68 | 3 → 1 | 3 → 1 | 10 → 9 |  |
| Pokémon Diamante (USA, rev. 5) | 10 | 63 822 | 627 | 6 | 164 → 50 | 16 → 2 | 14 → 2 | 80 → 108 |  |
| Pokémon Diamante (USA, rev. 5) | 20 | 127 323 | 1215 | 11 | 282 → 67 | 30 → 2 | 26 → 2 | 146 → 151 | 501 → 68 |
| Pokémon Diamante (USA, rev. 5) | 40 | 254 321 | 2389 | 21 | 507 → 115 | 64 → 2 | 54 → 3 | 306 → 301 | 895 → 104 |
| Pokémon Oro HeartGold (USA) | 1 | 8 222 | 106 | 2 | 80 → 56 | 3 → 1 | 2 → 1 | 9 → 8 |  |
| Pokémon Oro HeartGold (USA) | 10 | 63 186 | 598 | 6 | 145 → 47 | 15 → 1 | 14 → 1 | 72 → 75 |  |
| Pokémon Oro HeartGold (USA) | 20 | 126 025 | 1159 | 11 | 257 → 64 | 30 → 1 | 26 → 2 | 155 → 156 | 558 → 57 |
| Pokémon Oro HeartGold (USA) | 40 | 251 751 | 2278 | 21 | 471 → 76 | 58 → 2 | 51 → 3 | 302 → 323 | 817 → 78 |
| Pokémon Blanco 2 (USA/EUR) | 1 | 7 268 | 117 | 1 | 67 → 54 | 3 → 1 | 2 → 1 | 8 → 10 |  |
| Pokémon Blanco 2 (USA/EUR) | 10 | 68 908 | 680 | 6 | 166 → 45 | 21 → 1 | 18 → 1 | 83 → 117 |  |
| Pokémon Blanco 2 (USA/EUR) | 20 | 137 507 | 1304 | 11 | 289 → 55 | 32 → 2 | 31 → 2 | 158 → 175 | 445 → 60 |
| Pokémon Blanco 2 (USA/EUR) | 40 | 274 766 | 2549 | 21 | 529 → 138 | 65 → 3 | 57 → 3 | 340 → 337 | 966 → 104 |
| Pokémon Negro (USA/EUR) | 1 | 5 367 | 88 | 1 | 50 → 63 | 2 → 1 | 2 → 1 | 6 → 6 |  |
| Pokémon Negro (USA/EUR) | 10 | 59 915 | 582 | 6 | 148 → 42 | 14 → 1 | 16 → 1 | 79 → 74 |  |
| Pokémon Negro (USA/EUR) | 20 | 119 709 | 1121 | 11 | 255 → 51 | 28 → 1 | 25 → 2 | 144 → 155 | 400 → 59 |
| Pokémon Negro (USA/EUR) | 40 | 239 291 | 2196 | 21 | 453 → 71 | 55 → 2 | 50 → 3 | 315 → 305 | 788 → 82 |

Con 0 randomizaciones, guardar una edición manual cuesta 38–60 ms en ambos diseños: es la escritura duradera en este disco (temporal con escritura directa, relectura, copia de seguridad y sustitución). Lo que queda después de la mejora crece con el tamaño del archivo a velocidad de disco, no con el trabajo de CPU sobre el historial; «Leer al abrir» se paga una vez por apertura. La variación entre filas de 40 randomizaciones (71–138 ms) es del disco: las mismas ROM dieron 47–76 ms entre 1 y 30 randomizaciones.

Causa medida: antes de sustituir el archivo, `Commit` lo leía entero y reconstruía su historial para comprobar generación, proyecto y ROM (la columna «Leer al abrir» entra en cada guardado antes de la mejora). Comprimir y verificar el historial completo sumaban unos 60 ms con 20 randomizaciones.
