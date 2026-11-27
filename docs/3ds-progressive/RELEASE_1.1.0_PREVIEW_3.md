# Definitive Randomizer v1.1.0-preview.3 — acceso anticipado

@DISTRIBUTION_STATUS@

Versión del paquete: **1.1.0-preview.3**. Commit exacto compilado: `@BUILD_COMMIT@`. `VERSION.txt` y `BUILD_PROVENANCE.json` identifican la misma compilación.

@DISTRIBUTION_METADATA@

Versión de **acceso anticipado**. Contiene todo lo de la [1.1.0-preview.2](RELEASE_1.1.0_PREVIEW_2.md) (y, por tanto, de la estable 1.0.0), más lo integrado en `main` después de ella (PR #114–#142). Si buscas la versión más probada, usa la 1.0.0 ([LINEAS_DE_VERSION.md](LINEAS_DE_VERSION.md)).

## Novedades de la preview.3

### Editores de mapas y eventos

| Función | Juegos | Evidencia |
| --- | --- | --- |
| Orientación y visión de entrenadores, renderizado gráfico real, objetos, warps y activadores (PR #114–#117) | Nintendo DS (Platino) | Validación manual; la prueba jugable de objetos, warps y activadores (#117) está pendiente. |
| Editor de mapas Gen6 y Gen7 y movimiento de NPC (PR #118–#121) | X/Y, OR/AS, USUM | Pruebas automáticas y revisión manual. |
| Orientación de entrenadores (PR #122) | 3DS | Aprobado en juego en Ultra Luna. |
| Añadir enfermera (PR #123, #130) | X (Gen6), Ultra Luna | Aprobado en juego en X y en Ultra Luna; Ultra Sol pendiente. |
| Zonas de captura y hierba visible (PR #124–#129) | X, OR, AS, Ultra Sol/Ultra Luna | Aprobado en juego. |
| Editor de mapas Gen4 (PR #131, #132) | Diamante/Perla, HeartGold/SoulSilver | HeartGold aprobado en juego. |
| Editor de mapas Gen5, gráficos de edificios y estaciones (PR #133–#136) | Negro, Blanco, Blanco 2 | Negro aprobado en juego; Blanco y Blanco 2 sin prueba jugable completa. |

### Mecánicas y CRO

| Función | Juegos | Evidencia |
| --- | --- | --- |
| **Caramelos Raros de uso múltiple** (PR #137, #139) | Rubí Omega / Zafiro Alfa, Ultra Sol / Ultra Luna | Confirmado en juego en Rubí Omega y en USUM. |
| **Convivencia de mentas con otros parches de `Bag.cro`** (PR #138): reconocimiento por anclas en lugar de huella del archivo completo | Ultra Sol / Ultra Luna | Confirmado en juego junto con Caramelos Raros. |
| **Habilidades especiales · Multitipo en otras especies** (PR #140), experimental | Ultra Sol / Ultra Luna | Confirmado en juego. |
| **Habilidades especiales · Disfraz en otras especies** (PR #141), experimental | Ultra Sol / Ultra Luna | Confirmado en juego con el instalador definitivo. |
| **Habilidades especiales · Sistema Alfa en otras especies** (PR #142), experimental; se activa por separado o junto con Multitipo, en cualquier orden | Ultra Sol / Ultra Luna | Confirmado en juego, solo y combinado con Multitipo. |

Las tres habilidades especiales están **desactivadas por defecto** y no asignan la habilidad a ningún Pokémon: solo cambian su comportamiento si la especie ya la tiene. Todas se registran en el historial, se deshacen con bytes exactos y se reproducen desde Global Template.

## Riesgos y pruebas pendientes

- Sistema Alfa: Multiataque, cambio de Memoria en combate y Truco/Desarme en otras especies no están detallados por separado.
- Disfraz y Multitipo no cambian formas ni modelos de especies distintas de Mimikyu y Arceus.
- Las limitaciones de la preview.2 siguen vigentes (Mega Ability Sync, perfiles, ROM reconstruidas); ver sus notas y [LIMITACIONES_ESTABLE.md](LIMITACIONES_ESTABLE.md).
- Trabaja siempre con copias y respalda tus partidas.

@DISTRIBUTION_WORKFLOW@
