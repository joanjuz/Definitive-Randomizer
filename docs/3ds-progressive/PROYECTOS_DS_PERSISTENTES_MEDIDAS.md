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
