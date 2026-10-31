# NDS fases B y C: datos de Pokémon, movimientos, Level Up, evoluciones y MT/MO — 2026-10-31

Cinco secciones nuevas en el espacio de trabajo DS, para Gen4 (Diamante, Platino, HeartGold) y Gen5 (Negro, Blanco ES, Blanco 2). Usan la misma vista compartida que el resto: edición manual por entrada, con nombres de especies, movimientos, tipos, habilidades, objetos y métodos en el idioma del juego, y randomización con vista previa y alcance. Los cambios se acumulan con los de las demás secciones y se exportan juntos.

| Sección | Edición manual | Randomización (al estilo de Universal Pokémon Randomizer ZX) |
|---|---|---|
| Pokémon | estadísticas base, tipos, habilidades (2 en Gen4, 3 en Gen5), ratio de captura, crecimiento, ratio de género, objetos en estado salvaje | barajar o aleatorizar estadísticas **conservando el total**; tipos y habilidades aleatorios, con la opción de que las evoluciones hereden los de su preevolución; ratio de captura mínimo |
| Movimientos | tipo, categoría, poder, precisión, PP, prioridad | poder (20–150), precisión (50–100), PP (5–40) y tipo aleatorios; Combate se queda igual |
| Level Up (tabla) | movimiento y nivel de cada entrada | movimientos aleatorios con un % del mismo tipo que el Pokémon; el primero siempre hace daño y no se repiten; o **solo Metrónomo** |
| Evoluciones (tabla) | método, parámetro y especie de las 7 evoluciones | evoluciones por intercambio (con o sin objeto, y Karrablast/Shelmet) → nivel 37; evoluciones aleatorias de fuerza similar (BST) |
| MT/MO (tabla) | compatibilidad de cada MT y MO, con el nombre del movimiento | aleatoria (%), por tipo (mismo tipo siempre, Normal al 75 %) o completa; por defecto se conservan las MO para no bloquear la historia |

Las habilidades aleatorias excluyen Superguarda, Predicción, Multitipo, Don Floral, Ilusión y Modo Daruma.

## Núcleo

- **Tablas nuevas en los perfiles**, cada una con su CRC32 comprobada en los volcados del propietario: Level Up (`a/0/1/8`, `wotbl.narc`), evoluciones (`a/0/1/9`, `evo.narc`) y movimientos (`a/0/2/1`, `waza_tbl.narc`).
  - Blanco ES comparte las de Negro.
  - En modo romhack, si alguna no se puede leer, su sección queda «no disponible» con el motivo y el resto sigue disponible. Ocurre en Volt White 2: su NARC de Level Up tiene una entrada fuera del bloque de datos.
- `NdsDataSource` reúne nombres, tamaños de registro de cada generación y la **lista de MT del ARM9**.
  - En Gen5 la lista guarda MT01–92, MO01–06 y MT93–95, y los bits siguen el orden MT01–95 y luego MO.
  - El ARM9 de Negro/Blanco/Blanco 2 (DSi) va comprimido sin pie de módulo: ahora se descomprime para leerlo, pero sigue sin reescribirse.
- `NdsCells` lee y escribe las cinco secciones. Los valores empaquetados se editan como campos de bits sin tocar los vecinos: el Level Up de Gen4 guarda movimiento y nivel en 16 bits, y cada MT/MO es un bit.
- `NdsDataRandomizer` produce las randomizaciones como ediciones de celdas. Por eso el registro de cambios y las comprobaciones de rango son los de la edición manual, y cada resultado depende solo de la semilla y de la entrada.

## Validación

- `--nds-only` (sintético, CI): 124, con 33 nuevas (11 por Gen5, Diamante y HeartGold):
  - lectura de las cinco tablas;
  - edición manual releída, incluido un nivel empaquetado y un bit de MT;
  - rechazo de valores fuera de rango;
  - el total de estadísticas se conserva al barajar y al aleatorizar;
  - herencia de tipos y habilidades por la línea evolutiva, sin habilidades prohibidas;
  - ratio de captura mínimo;
  - rangos de poder y PP;
  - Level Up aleatorio que empieza con un movimiento de daño, y modo solo Metrónomo;
  - intercambios a nivel 37;
  - MT/MO completas o aleatorias conservando las MO;
  - determinismo y alcance.
- `--nds-data-probe` sobre los cinco juegos: 30/30.
  - La lista de MT se lee del ARM9, en español en Blanco ES: «MT01 · Afilagarras», «MO01 · Corte».
  - Bulbasaur se lee correctamente.
  - Se acumulan cinco randomizaciones y una edición: entre 33.845 y 47.987 cambios.
  - La ROM exportada se relee igual en las cinco secciones, el resto de archivos queda idéntico y el original no cambia.
- `--nds-ui-probe`: las diez secciones abren en la vista compartida, 15/15 en los cinco juegos.
- Regresión completa en verde (compilación limpia sin advertencias); romhacks 24/24.

## Prueba en juego

**Confirmada por el propietario el 2026-10-31**: las ROM de prueba de la fase 14, que acumulan las fases 9 a 14, funcionan correctamente en el juego.

## Pendiente

- Fase C, parte 2: más herramientas de UPR-ZX en entrenadores (por tipo, el rival con su inicial, forzar evolucionados, niveles en %), salvajes (mapeo por área o global), estáticos e intercambios.
