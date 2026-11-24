# Editor DS: perfil Gen4 de HeartGold (IPKE/0)

El editor de mapas y eventos de Nintendo DS se abre también con **Pokémon HeartGold USA, revisión 0 (IPKE/0)**. Es el mismo editor visual de Platino y Diamante. HeartGold tiene su propio perfil `Gen4EventProfile`, medido sobre su volcado, y un **adaptador de cabeceras**, porque sus cabeceras de mapa colocan los campos en otras posiciones.

No se añaden SoulSilver, otras revisiones de HeartGold ni Gen5. Un juego o una revisión sin perfil muestra el aviso habitual y no abre nada.

## Qué está disponible en HeartGold

| Función | HeartGold IPKE/0 | Diamante ADAE/5 | Platino CPUE/1 |
|---|---|---|---|
| Mapa con matriz, terreno (colisión), estructuras y modelos con texturas | sí, también interiores | sí | sí |
| Mover y girar NPC existentes | sí | sí | sí |
| Orientación «mirar fijo» y alcance de visión de entrenadores existentes | sí, alcance 0–7 | sí, 0–6 | sí, 0–6 |
| Mover objetos de fondo y cambiar su dirección de interacción | sí (0–4) | sí (0–5) | sí (0–6) |
| Mover warps y cambiar su destino a otro warp existente | sí | sí | sí |
| Mover activadores y cambiar su tamaño | sí | sí | sí |
| Poké Ball del suelo (objetos visibles) | se listan con los objetos; no se mueven ni giran | ídem (tipo 3) | ídem (tipo 3) |
| Añadir, duplicar, pegar o eliminar NPC; añadir enfermera | **no** | **no** | sí |
| Checkpoints `.ndsevents.json`, proyecto persistente, exportar ROM, restaurar | sí | sí | sí |

En HeartGold, como en Diamante, la interfaz no muestra los controles de NPC nuevos ni registra sus atajos. El núcleo también rechaza crear NPC y añadir scripts.

## Diferencias medidas respecto a Platino y Diamante

| Dato | HeartGold IPKE/0 | Platino CPUE/1 / Diamante ADAE/5 |
|---|---|---|
| ARM9 | comprimido con BLZ; las tablas se leen del ARM9 descomprimido | sin comprimir |
| Cabeceras de mapa (ARM9) | `0xF6BE0`, 540 × 24 bytes | `0xE601C`, 593 × 24 / `0xEEDBC`, 559 × 24 |
| Campos de la cabecera | área u8 +1, matriz +4, scripts +6, eventos +16, sección +18 | área u8 +0, matriz +2, scripts +4, eventos +16, sección +18 |
| Eventos / scripts / terreno | `a/0/3/2` (491) / `a/0/1/2` (965) / `a/0/6/5` (676) | `fielddata/…` con rutas propias |
| Matrices y nombres internos | `a/0/4/1` (288) y `fielddata/maptable/mapname.bin` | `fielddata/mapmatrix/map_matrix.narc` y `mapname.bin` |
| Lado máximo de una matriz | 47 bloques (la matriz exterior mide 47 × 17) | 30 |
| `land_data` | tras los cuatro tamaños: u16 `0x1234`, u16 *n* y *n* bytes de sonido de fondo; luego los atributos | los atributos empiezan en `0x10` |
| Áreas | `a/0/4/2`: +0 modelos, +2 texturas, +6 u8 exterior (0 = interior), +7 u8 luz | +6 u16 luz |
| Modelos de estructuras | exteriores `a/0/4/0`; **interiores `a/1/4/8`** | un solo archivo |
| Listas de modelos / texturas de estructuras / texturas del mapa | `a/0/4/3` / `a/0/7/0` / `a/0/4/4` | `fielddata/areadata/…` |
| Nombres de lugar (banco de texto) | 279 | 433 / 382 |
| Scripts de objetos visibles / ocultos | 141 / 145 | 404 / 408 y 370 / 374 |
| Poké Ball del suelo | NPC de tipo 0 con script 7000–7999 (257 en el juego) | NPC de tipo 3 |
| Alcance de visión máximo en los datos originales | 7 | 6 |
| Dirección de interacción máxima de objetos de fondo | 4 | 6 / 5 |

Cada tabla está fijada por CRC32 en `Gen4EventProfile.HeartGold`, incluido el ARM9 descomprimido. El archivo de eventos se fija con las tres huellas SHA-256 del editor, como en los otros juegos. Una ROM reconstruida con ediciones aceptadas se vuelve a abrir por esas huellas.

### Procedencia

- **Tabla de cabeceras.** Se buscó en el ARM9 descomprimido. `0xF6BE0` es el único desplazamiento donde las 540 cabeceras tienen el área, la matriz, los scripts y los eventos dentro de sus tablas. En él, los 1.308 warps llevan a un warp existente, más 9 dinámicos (ancla 256). La disposición coincide con `include/map_header.h` de la decompilación pública pret/pokeheartgold. El descompresor BLZ del proyecto y uno independiente dan el mismo ARM9 (CRC32 `0x99A30D93`).
- **Registros de eventos.** Son los de Diamante y Platino: cuatro bloques de 20, 32, 12 y 16 bytes (`include/map_events_internal.h`). Los 491 miembros se leen sin bytes sobrantes. Las coordenadas son de la matriz, como en Platino, y caen dentro de ella.
- **Modelos interiores.** `AreaDataManager_Alloc` (`asm/overlay_01_021FB878.s`) abre `a/0/4/0` si el byte +6 del área no es 0 y `a/1/4/8` si lo es. En los datos, las listas de modelos de las áreas interiores no pasan de 221 (`a/1/4/8` tiene 222 modelos), y las exteriores no pasan de 339 (`a/0/4/0` tiene 340). De las 4.767 estructuras de los mapas, 4.760 están en la lista de su área. El resto se omite, igual que en Platino.
- **Scripts de objetos.** `src/script_manager.c` asigna 7000+ a `scr_seq` 141 (Poké Ball) y 8000+ a 145 (objetos ocultos). Las 257 Poké Ball del juego son NPC de tipo 0, con el gráfico 87 y un script de 7000 a 7999.
- **Entrenadores.** Los tipos 1, 4 y 7 aparecen en 407 NPC, con alcance de 0 a 7. Se aplican las reglas de visión de Platino: dirección 0–3 y «mirar fijo» con los movimientos 14–17. La función de visión de HeartGold sigue en ensamblador en la decompilación. Por eso estas reglas se acreditan por coherencia con los datos: 147 de los 148 entrenadores «mirar fijo» tienen la dirección de su movimiento. La excepción es un entrenador del gimnasio T30GYM0101, con movimiento 17 y dirección 1. **Queda pendiente de prueba jugable.**
- **Terreno.** El bit 15 sigue siendo la colisión: 2.578 de los 2.667 NPC están en casillas sin ella.

## Contratos de archivo

- **Checkpoints.** Mismo formato y mismas versiones (1–5). El campo `Game`/`Revision` lleva `IPKE`/`0`. Un checkpoint de otro juego se rechaza.
- **Proyecto persistente (`.drproj`).** Sin cambios de formato.
- **Exportación.** Solo cambia el archivo de eventos `a/0/3/2`. El ARM9 y el resto de archivos quedan idénticos byte a byte.
- **Detector de proyectos.** Acepta un checkpoint de HeartGold solo si su ROM de origen es HeartGold IPKE/0. Rechaza SoulSilver (IPGE) y las combinaciones cruzadas.
- **Global Template y Batch.** Sin cambios: siguen rechazando las operaciones de eventos.
- **Platino y Diamante.** No cambia ningún valor de sus perfiles. Las propiedades nuevas de `Gen4EventProfile` toman por defecto lo que el editor ya usaba con ellos. Las ROM de prueba de Platino (#117) y de Diamante (#131), regeneradas con este código, son idénticas byte a byte a las ya aprobadas en juego.

## Comprobaciones automatizadas

- `--nds-gen4-hgss-only`: 36 comprobaciones sintéticas. `--nds-gen4-hgss-ui-only`: 6 comprobaciones fuera de pantalla. Las dos están en el CI.
- `--nds-gen4-hgss-measure <ROM>` compara la copia con el perfil: CRC32, huellas y censo (540 mapas, 2.667 NPC, 257 Poké Ball, 1.317 warps, 407 entrenadores).
- `--nds-gen4-hgss-probe <carpeta nueva> <ROM>`, sobre la copia real, hace estas ediciones:
  - en Pueblo Primavera, la puerta de la casa del suroeste lleva a la casa del rival y el activador de la salida oeste pasa de 1 × 7 a 2 × 7;
  - en la Ruta 29, un NPC «mirar fijo» mira hacia arriba;
  - en la Ruta 30, un objeto oculto se mueve una casilla y un entrenador mira hacia arriba.

  Después comprueba que una Poké Ball no se mueve y que no se crean NPC. Guarda cinco checkpoints, reabre el último, exporta y vuelve a abrir la ROM exportada. Compara el ARM9 y el resto de archivos byte a byte. Por último, restaura todo y compara el SHA-256 con la fuente: coincide. Escribe `MANUAL.txt`.
- `--nds-map-render-gallery-probe` con HeartGold: 16 mapas con 0 triángulos sin textura. Incluye Pueblo Primavera, Ciudad Cerezo, Ciudad Malva, Ciudad Trigal, Pueblo Azalea, las Rutas 1, 29, 30 y 32, interiores (laboratorio, casa, Centro Pokémon), las Ruinas Alfa, la Senda Campana y la Meseta Añil.
- Regresión: la lista completa del CI en local, más las sondas reales de Platino (eventos, visión, objetos/warps/activadores, NPC nuevos, scripts, render, matriz, terreno y estructuras) y de Diamante.

**Limitación visual conocida.** En la Meseta Añil (T10), el edificio principal se dibuja como una zona negra. No afecta a la edición. Puede estar relacionado con las texturas dinámicas del área (byte +4), que el render cenital no carga.

Ninguna de estas comprobaciones sustituye una prueba en el juego.

## Validación jugable

El propietario probó en el juego la ROM `HeartGold-eventos.nds` generada por la sonda y **aprobó las cinco comprobaciones** en HeartGold IPKE/0:

1. Puerta de la casa del suroeste de Pueblo Primavera, que lleva a la casa del rival.
2. Activador de la salida oeste ampliado a 2 × 7.
3. NPC 1 de la Ruta 29 mirando arriba.
4. Objeto oculto de la Ruta 30 movido una casilla.
5. Entrenador 47 de la Ruta 30 mirando arriba.

La aprobación cubre solo estas cinco operaciones en IPKE/0. No se extiende a SoulSilver, otras revisiones ni funciones que no se ensayaron.

## Fuera de alcance

- **SoulSilver.** Comparte el formato, pero no hay volcado medido.
- **Crear NPC o enfermeras en HeartGold.** El script común de la enfermera solo se acreditó en Platino.
- **Editar los gráficos, el tipo, el script o las flags de un evento.**
- **Gen5.**
