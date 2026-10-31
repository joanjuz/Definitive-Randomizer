# NDS fase 13: reglas de entrenador, combates dobles y tabla BST — 2026-10-31

Herramientas del randomizer de 3DS y de UPR-ZX que faltaban en DS, en las mismas pestañas de opciones que usan 3DS y Switch.

## Entrenadores

Las opciones se reparten en las cinco pestañas compartidas del editor:

- **Pokémon y equipos**:
  - especies: aleatorias, BST similar o sin cambios;
  - un tipo por entrenador;
  - sin legendarios;
  - Pokémon mínimos por equipo: los nuevos tienen el nivel de un compañero y una especie de fuerza parecida;
  - IV al máximo.
- **Niveles de entrenadores**: niveles en % y evolución final forzada desde un nivel.
- **Movimientos y reglas**:
  - sin cambios;
  - los de su nivel (Level Up, como `getMovesAtLevel` de UPR-ZX);
  - mejores movesets: los ataques más fuertes que puede aprender por nivel o MT/MO, con STAB, como mucho dos del mismo tipo y un movimiento de estado;
  - aleatorios;
  - solo Metrónomo.
- **IA, objetos y combates**:
  - objetos equipados: sin cambios, aleatorios, inteligentes (potenciador de su tipo, Lodo Negro, objetos elegidos según su mejor estadística de ataque o un objeto general) o ninguno;
  - IA al máximo;
  - combates dobles (%).
- **BST progresivo**: la tabla editable de rangos por nivel, con «Full Random» por rango, igual que en 3DS y Switch.

Los formatos de equipo con movimientos u objetos se añaden cuando hacen falta, como en `setTrainers` de UPR-ZX: el tipo de equipo de la cabecera y el tamaño de cada Pokémon se reescriben.

Las especies y las reglas son una sola operación. Primero se randomizan las especies y después se aplican las reglas sobre el resultado. Esa operación se guarda en el historial y en el Global Template (receta `Trainers`).

### Combates dobles

Usan el modo de combates dobles de UPR-ZX:
- Solo se convierten combates individuales. Se excluyen los entrenadores cuyo Pokémon más fuerte no llega al nivel 8 (combates guionizados del principio, como los primeros del rival) y, en Diamante y Platino, los compañeros 608-612.
- Un equipo de un solo Pokémon recibe una copia.

| Juego | Datos | Código |
|---|---|---|
| Diamante, Platino, HeartGold | tipo de combate 3 | ARM9: intro especial, bandera, caminata y caja de texto; secuencia de fin de combate (`sub_seq`) |
| Negro, Blanco, Blanco 2 | tipo de combate 1 y bandera 0x80 | overlay de campo (21 o 36): cajas de texto, límite de dos Pokémon y puntero de pareja; tabla de cajas de texto |

- El código se parchea una sola vez y solo si es el original. Los prefijos se buscan en el ARM9 u overlay de la ROM: deben aparecer exactamente una vez, y en los cinco volcados es así.
- Una segunda operación de dobles detecta que el código ya está parcheado y solo cambia datos.
- En un romhack sin ese código original la opción aparece como no disponible.
- **Gen5 es experimental**: UPR-ZX también parchea el ARM9 para la caja de texto al ser visto por un entrenador doble. Ese ARM9 es DSi y no se modifica, así que al ser visto puede haber una pausa breve o una caja de texto vacía. Hay que confirmarlo en el juego.

## Encuentros

- Especies: aleatorias, BST similar o sin cambios.
- BST progresivo por nivel con la misma tabla editable.
- Sin legendarios.
- Mapeo global y niveles en %.

## Pokémon

- Crecimiento medio rápido para todos y lento para los legendarios (UPR-ZX «standardize EXP curves»).
- Objetos salvajes aleatorios: la mitad de los huecos quedan vacíos y los demás reciben objetos equipables.

## Archivos nuevos en los perfiles

Todos tienen su CRC32 verificado en los volcados:

| Juego | Tabla | Ruta y CRC32 |
|---|---|---|
| Negro/Blanco | cajas de texto de entrenador | `a/0/9/0` (DB72FD1A) |
| Blanco 2 | cajas de texto de entrenador | `a/0/8/9` (C73441C5) |
| Negro | overlay de campo 21 | F2B9DC42 |
| Blanco ES | overlay de campo 21 | 3882585C |
| Blanco 2 | overlay de campo 36 | E3CF3D36 |
| Diamante | secuencia de combate | `battle/skill/sub_seq.narc` (E03D048D) |
| Platino | secuencia de combate | `battle/skill/sub_seq.narc` (B02FF213) |
| HeartGold | secuencia de combate | `a/0/0/1` (6644F919) |

En modo romhack son tablas opcionales: si faltan o no se pueden leer, solo los combates dobles dejan de estar disponibles.

## Validación

- `--nds-only` (sintético): 157. Incluye:
  - equipos de tres con movimientos de su nivel, objetos, IV y IA;
  - quitar objetos conservando movimientos;
  - mejores movesets sin repetidos;
  - crecimiento y objetos salvajes;
  - especies sin legendarios.
- `--nds-data-probe` sobre los cinco volcados, con dobles al 100 %:
  - entre 560 y 736 entrenadores convertidos según el juego;
  - el código se parchea una vez y no se repite;
  - la ROM exportada se relee con equipos de dos o más Pokémon, movimientos, objetos e IV al máximo.
- `--nds-ui-probe`: las opciones de entrenador usan las cinco pestañas y la tabla BST, los encuentros muestran su tabla BST y la receta de entrenadores se repite igual desde el Global Template.
- Pendiente: la prueba en juego, sobre todo de los combates dobles.
