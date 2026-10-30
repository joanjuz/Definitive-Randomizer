# NDS fase 1: lectura de datos Gen5 (Negro y Blanco) — 2026-10-30

Lectura de los datos de Pokémon Negro (USA/EUR, `IRBO`) y Blanco (España, `IRAS`). **No escribe nada**: es la base que usarán los randomizers de la fase 2. Código en `pk3DS.Core/NDS/Gen5Data.cs` y `Gen5Text.cs`.

## Alcance

- `Gen5Profile`: para cada juego, la ruta de cada NARC y el CRC32 que debe tener (tomados de `gen5_offsets.ini` de UPR-ZX), los índices de texto y las posiciones de los iniciales en los scripts. Blanco ES usa sus propios textos y scripts y los encuentros de Blanco; el resto de tablas coincide con Negro (USA).
- `Gen5Data.Open`: identifica el juego y **comprueba el CRC32 de cada tabla antes de leerla**. Si una no coincide, rechaza el juego en lugar de interpretar datos desconocidos.
- `Gen5Text`: textos cifrados de Gen5 (XOR con clave rotatoria) y comprimidos (`0xF100`, 9 bits). Los códigos de control se conservan como `\xNNNN`.
- Lectores: nombres de especies, estadísticas base, entrenadores (cabecera de 20 bytes, equipo de 8/10/18 bytes por Pokémon según las banderas de objeto y movimientos, nombre y clase), encuentros por estación (bloques de 232 bytes: 7 tasas y 56 huecos) e iniciales (todas las posiciones de cada inicial deben coincidir).
- Pendiente para la fase 2: overlays con compresión BLZ (necesarios para tiendas y otros datos del ejecutable), movimientos, aprendizajes y evoluciones.

## Hallazgos durante la validación

- Los nombres de entrenador están comprimidos (`0xF100`), así que la prueba real ejercita el descompresor.
- La línea 0 de nombres de entrenador está vacía: el entrenador N usa la línea N. Una primera versión usaba N−1; la comprobación de líderes la detectó porque aparecían nombres vecinos (Tia, Adelaide, Roberto) en lugar de Cress, Skyla y Drayden. Ahora exige la lista exacta.
- En Gen5 la clase de los líderes se llama "Leader" (inglés) y "Líder" (español), no "Gym Leader".

## Validación

- `--nds-only` (sintético, en el CI): 19 comprobaciones, incluidas el CRC32 con su valor estándar (`CBF43926`) y el descifrado de un texto Gen5 construido como el del juego.
- `--nds-gen5-probe <rom...>` sobre los volcados originales de Negro y Blanco ES: 16/16, sin escribir archivos.

| Comprobación | Negro (USA/EUR) | Blanco (España) |
|---|---|---|
| CRC32 de las seis tablas | coinciden | coinciden |
| Especies | 652 nombres; Bulbasaur, Snivy, Genesect | igual |
| Estadísticas base | 669 entradas; Bulbasaur 45/49/49/45/65/65, Planta/Veneno | igual |
| Iniciales en todas las posiciones | Snivy, Tepig, Oshawott | igual |
| Entrenadores | 615 (1.304 Pokémon, 26 dobles) | igual |
| Líderes | Chili, Cilan, Cress, Lenora, Burgh, Elesa, Clay, Skyla, Brycen, Iris, Drayden | Zeo, Millo, Maíz, Aloe, Camus, Camila, Yakón, Gerania, Junco, Iris, Lirio |
| Encuentros | 444 áreas, 4.012 huecos, con Patrat | igual |
| Volcado original | sin cambios | sin cambios |

## Siguiente fase

Fase 2: overlays (BLZ), randomizers de entrenadores, encuentros e iniciales con funciones Progressive cuando apliquen a Gen5, exportación de un `.nds` nuevo y sección DS en la app.
