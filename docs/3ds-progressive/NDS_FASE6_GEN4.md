# NDS fase 6: Gen4 — Diamante, Platino y HeartGold (USA) — 2026-10-30

Primera fase de Gen4: lectura de datos y randomizer de **entrenadores y Pokémon salvajes**, con los mismos modos, semilla y registro de cambios que Gen5, y la ventana DS. Iniciales, objetos y tiendas de Gen4 quedan para una fase posterior: la ventana los desactiva y el motor los rechaza.

## Juegos

| Juego | Código | Revisión | Volcado |
|---|---|---|---|
| Diamante (USA) | `ADAE` | 5 | SHA-1 del propietario |
| Platino (USA) | `CPUE` | 1 | SHA-1 del propietario |
| HeartGold (USA) | `IPKE` | 0 | SHA-1 del propietario |

Perla, SoulSilver y otras regiones no tienen perfil. Cada NARC se comprueba con el CRC32 de `gen4_offsets.ini` de UPR-ZX (coinciden en los tres volcados).

## Código

- `pk3DS.Core/NDS/Gen4Text.cs`: textos Gen4. La tabla de punteros y cada línea van cifradas con claves que dependen de la clave del archivo y del número de línea; algunas líneas usan compresión de 9 bits, y los caracteres pasan por la tabla `Generation4.tbl` (recurso incrustado, de UPR-ZX/thenewpoketext, GPLv3; licencia en `docs/third-party/UPR-ZX-LICENSE.txt`). Solo lectura.
- `pk3DS.Core/NDS/Gen4Data.cs`: `Gen4Profile` (rutas, CRC32 e índices de texto por juego) y `Gen4Data` (especies, estadísticas, entrenadores y huecos de encuentro).
- `pk3DS.Core/NDS/Gen4Randomizer.cs`: entrenadores y encuentros. Usa el mismo selector de especies que Gen5 (`Gen5Randomizer.SpeciesPicker`), limitado a las 493 especies de Gen4.
- `NdsGen5Session` abre Gen4 o Gen5 según la generación del perfil de identidad.

## Formatos

- **Entrenadores**: cabecera de 20 bytes (como Gen5, combate doble en el byte 16). Cada miembro ocupa 6 bytes, más 2 si lleva objeto y 8 si tiene movimientos propios, más **2 bytes de sello de Poké Ball en Platino y HeartGold**. La especie usa los 10 bits bajos y la forma los 6 altos. Al cambiar la especie se quitan los movimientos propios (el juego usa los de nivel), la forma pasa a 0 y se conservan dificultad, habilidad/género, nivel, objeto, sello y los bytes de relleno al final del equipo.
- **Encuentros Diamante/Platino** (424 bytes por zona): 12 huecos de hierba/cueva (nivel y especie de 4 bytes), 20 especies de sustitución (enjambre, día, noche, radar y ranura doble de GBA) con el nivel del hueco de hierba que sustituyen, y cuatro tablas de agua (surf y tres cañas) con nivel mínimo y máximo. El conjunto «relleno» de UPR-ZX no se toca.
- **Encuentros HeartGold** (196 bytes por zona): niveles de hierba compartidos y especies de 2 bytes para mañana, día y noche; radio Hoenn/Sinnoh; surf, Golpe Roca y tres cañas; enjambres y pesca nocturna. Radio y enjambres usan el nivel máximo de la hierba para el modo progresivo.
- En cada tabla, la misma especie original se cambia por la misma especie nueva.
- **No se modifican todavía**: Feebas, árboles de miel, Gran Pantano, Jardín Trofeo, árboles de Golpe Cabeza ni el Concurso de Bichos de HeartGold.

## Validación

- `--nds-only` (sintético, CI): 62. Texto Gen4 (normal, comprimido, variables, archivo vacío y tabla truncada); para Diamante y HeartGold, lectura de entrenadores con su tamaño de entrada, huecos de encuentro, «sin cambios», opciones no admitidas rechazadas, determinismo, exportación solo desde tablas verificadas, niveles/objetos conservados, sello y relleno intactos, misma especie → misma especie, y solo cambian los bytes de especie.
- `--nds-gen4-probe` sobre los tres volcados: 27/27 (8 líderes de gimnasio por juego, más de 6.000 huecos, más de 8.000 cambios, equipos y huecos conservan tamaño, nivel y objeto, resto de archivos idéntico, misma semilla mismo resultado, originales sin cambios).
- Ventana DS: `--nds-ui-only` 7; `--nds-ui-probe` 8/8 con HeartGold y con Diamante (iniciales, objetos y tiendas desactivados) y 9/9 con Blanco ES.
- ROM de prueba para el juego (semilla 20261030, entrenadores BST progresivo, encuentros BST similar), en `D:\Nintendo\DS\Juegos\Pruebas`: `Diamante-random-prueba.nds` (8242 cambios), `Platino-random-prueba.nds` (8573) y `HeartGold-random-prueba.nds` (8127), cada una con su `.cambios.txt`.

## Pendiente

- Prueba en juego de las tres ROM (partida nueva): combates contra entrenadores (incluido el primer líder) y Pokémon salvajes en hierba, de día y de noche, y con caña.
- Fases siguientes de Gen4: iniciales, objetos y tiendas.
