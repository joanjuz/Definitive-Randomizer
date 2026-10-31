# NDS fase 7b: iniciales de Gen4 — 2026-10-30

Iniciales aleatorios o personalizados en Diamante, Platino y HeartGold, como en Gen5. Sigue lo que hace UPR-ZX (`Gen4RomHandler.setStarters`, GPLv3), con comprobaciones más estrictas: cada patrón tiene que encontrarse como se espera y con los iniciales originales; si no, no se devuelve nada.

## Qué cambia

| | Diamante / Platino | HeartGold |
|---|---|---|
| Tabla de iniciales | overlay 64 / 78 (0x1B88 / 0x1BC0), sin comprimir | ARM9, 13 bytes antes de un código único |
| Pantalla de elección | parche Thumb de la rutina gráfica (prefijo y prefijo interno únicos) para mostrar cualquier especie | — |
| Gritos | — | overlay 61 (tras un prefijo único), recomprimido en su hueco |
| Combates del rival | 7 / 9 ramas en los scripts que comparan el inicial | 7 scripts |
| Combates dobles con acompañante | 5 / 6 ramas | — |
| Texto de elección | categoría del Pokédex y nombre (líneas 1–3) | nombre y tipo (líneas 1–6) |

- **Texto Gen4**: `Gen4Text.Replace` reescribe solo las líneas cambiadas, sin comprimir y con la clave del archivo, y conserva tal cual los caracteres del resto (también las líneas comprimidas). Los nombres, categorías y tipos se sustituyen dentro de cada línea, así que se mantienen colores y saltos.
- Los textos del rival tras recibir el inicial usan variables y no hace falta tocarlos.
- Iniciales y objetos del mapa modifican la misma tabla de scripts: se usa una sola copia de trabajo. En HeartGold, iniciales, objetos ocultos y tiendas comparten el mismo ARM9.
- La ventana DS habilita los iniciales también en Gen4: aleatorios o personalizados, entre las 493 especies.

## Validación

- `--nds-only` (sintético, CI): 75. Incluye la reescritura de texto Gen4 (una línea, el resto intacto, rechazo de caracteres fuera de la tabla) y el rechazo de iniciales repetidos o fuera de Gen4.
- `--nds-gen4-probe` sobre los tres volcados: 60/60. Para los iniciales:
  - se leen los originales y la tabla queda con los nuevos;
  - en el overlay solo cambian los datos de los iniciales: 44 bytes en Diamante/Platino, 3 en HeartGold, cuyo overlay sigue comprimido en su hueco;
  - solo cambian los scripts del rival y de combates dobles;
  - el texto nombra a los nuevos iniciales y el resto de líneas queda igual;
  - los iniciales aleatorios son distintos entre sí y reproducibles.
- `--nds-ui-probe`: la misma secuencia (iniciales personalizados, objetos y tiendas) en Gen4 y Gen5.
- ROM de prueba para el juego (semilla 20261031, todas las opciones; iniciales Pikachu, Charmander y Squirtle), en `D:\Nintendo\DS\Juegos\Pruebas`:
  - `Diamante-completa-prueba.nds`;
  - `Platino-completa-prueba.nds`;
  - `HeartGold-completa-prueba.nds`.

  Sustituyen a las `*-todo-prueba.nds` de la fase 7a.

## Prueba en juego

**Confirmada por el propietario el 2026-10-31**: las ROM de prueba de las fases 4 a 8 funcionan en DeSmuME y están bien randomizadas. Eran Blanco ES (objetos y tiendas), Blanco 2, Diamante, Platino y HeartGold con todas las opciones, y los romhacks Renegade Platinum y Volt White 2.
