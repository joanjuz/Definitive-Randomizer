# NDS fase 14: movimientos huevo, tutores, lista de MT y más ajustes — 2026-10-31

Herramientas del randomizer de 3DS y de UPR-ZX que faltaban en DS. Se editan en el editor compartido y se randomizan con vista previa, como el resto de secciones.

## Secciones nuevas

| Sección | Juegos | Dónde están los datos |
|---|---|---|
| Movimientos huevo | los seis | Gen5: NARC `a/1/2/3` (BW) o `a/1/2/4` (B2W2), un archivo por especie. HeartGold: NARC `a/2/2/9`, una lista. Diamante y Platino: overlay de campo 5, tras el prefijo de UPR-ZX `40016601` |
| Listas de MT y tutores | Diamante, Platino, HeartGold (MT) · Platino, HeartGold, Blanco 2 (tutores) | MT: tabla del ARM9, la misma de la que ya se leían los nombres de las MT. Tutores: overlay de campo (5 en Platino, 1 en HeartGold, 36 en Blanco 2) |
| Tutores (compatibilidad) | Platino, HeartGold, Blanco 2 | Platino: overlay 5. HeartGold: `fielddata/wazaoshie/waza_oshie.bin`. Blanco 2: datos de especie (0x3C), en cuatro bloques con el orden de UPR-ZX |

- Las MO no se pueden cambiar porque la historia necesita sus movimientos de campo.
- En Negro y Blanco la lista de MT está en el ARM9 DSi, que no se modifica, y no hay tutores. Esas secciones muestran el motivo.
- Diamante no tiene tutores.

### Lo que acompaña a una MT o a un tutor nuevos (Gen4)

Al cambiar el movimiento de una MT, como hace `setTMMoves` de UPR-ZX:
- la descripción del objeto pasa a ser la del movimiento nuevo, reajustada a líneas de 38 caracteres (36 en HeartGold);
- el color del disco pasa a ser el del tipo nuevo (tabla de colores del ARM9);
- en los diálogos que nombran la MT (lista `TMText` de UPR-ZX) el nombre viejo se sustituye por el nuevo.

El tutor de Golpe Cabeza del Bosque Encina (HeartGold) enseña su movimiento desde un script. Si ese tutor cambia, se actualizan el script y su texto. Por defecto no cambia, porque Golpe Cabeza es un movimiento de campo.

Los precios de la Zona de Juegos y del Frente Batalla conservan su texto.

## Randomización

- **Movimientos huevo**: aleatorios (con el % del mismo tipo que el Pokémon) o solo Metrónomo. Cada especie conserva el número de movimientos.
- **Listas**:
  - MT y tutores con movimientos aleatorios, sin repetir ninguno entre MT, MO y tutores;
  - «Conservar movimientos de campo» (por defecto) deja las MT y tutores de Destello, Golpe Cabeza, Excavar, Golpe Roca…
- **Compatibilidad de tutores**: aleatoria (%), por tipo o completa, como la de MT.

## Iniciales

Diamante y Platino dan el inicial con un objeto (ninguno en los originales), en el comando del script que entrega el Pokémon (UPR-ZX `StarterPokemonHeldItemOffset`).
- La sección Iniciales muestra «Objeto del inicial».
- La randomización ofrece «Objeto aleatorio para el inicial».
- Antes de escribir se comprueba la forma del comando.

## Entrenadores

En la pestaña «Pokémon y equipos»:
- barajar nombres de entrenador;
- barajar clases de entrenador.

Se barajan los nombres y clases del propio juego, en vez de la lista en inglés de UPR-ZX. Las líneas con variables (el nombre del rival) no se tocan. Se permutan las líneas para que el archivo de texto conserve su tamaño: Negro, Blanco y Blanco 2 no tienen espacio libre para que crezca.

Para eso el escritor de texto de Gen5 vuelve a comprimir (9 bits) las líneas que el juego guardaba comprimidas, en vez de guardarlas sin comprimir. Una línea comprimida que termina en una palabra de unos no lleva otro terminador, como en los archivos del juego.

## Ajustes del juego

| Ajuste | Gen4 | Gen5 | Dónde |
|---|---|---|---|
| Tabla de tipos de Gen6 (Acero ya no resiste Fantasma ni Siniestro) | sí | sí | overlay de combate (11, 16, 12 / 93, 167) |
| Captura garantizada | sí | sí | overlay de combate |
| Objetos de Recogida aleatorios | sí (18 comunes y 11 raros) | sí (29) | overlay de combate / overlay 92 o 166 |
| Correr sin Deportivas | sí (ARM9) | sí (overlay de campo) | |
| Sin música de PS bajos | — | sí | overlay 94 o 168 |
| Texto instantáneo, Pokédex Nacional, Mundo Distorsión | sí | — | ARM9 y scripts |

- Cada cambio de código se busca en el overlay original. Esos bytes deben seguir siendo los originales, así que un ajuste no se aplica dos veces.
- Todos los overlays nuevos están en el perfil con su CRC32, verificado en los volcados.
- En un romhack, un overlay que no sea el original deja sus opciones sin efecto (error al aplicar) o como no disponibles (huevo y tutores).
- La Recogida aleatoria guarda su semilla en el Global Template, y Batch usa una semilla nueva.

## CRC32 nuevos

| Juego | Overlay de campo | Overlay de combate | Recogida | PS bajos | Otros |
|---|---|---|---|---|---|
| Diamante | 5: 52005967 | 11: 3DCCA476 | | | |
| Platino | 5: 3E286491 | 16: 25EBE8C1 | | | |
| HeartGold | 1: 21F7A855 | 12: 90D2AF3E | | | `a/2/2/9` 67123F30, `waza_oshie.bin` 1DFC77A1 |
| Negro | 21 (ya en el perfil) | 93: A36EECA9 | 92: D4CF0B58 | 94: 58394A4F | `a/1/2/3` 784B2D4F |
| Blanco ES | 21 (ya en el perfil) | 93: 73422427 | 92: 8043EB67 | 94: 325AA919 | `a/1/2/3` 784B2D4F |
| Blanco 2 | 36 (ya en el perfil) | 167: 3E3CA5FA | 166: 759F8A03 | 168: EDE00D9D | `a/1/2/4` 784B2D4F |

Todos los prefijos de UPR-ZX aparecen exactamente una vez en los cinco volcados.

## Validación

- `--nds-only` (sintético): 161, con los movimientos huevo de Gen5 y HeartGold (lectura, aleatorios, Metrónomo y edición).
- `--nds-data-probe` sobre los cinco volcados: 119 en total. En cada juego:
  - lee los movimientos huevo (1711 a 2773) y los randomiza conservando cuántos tiene cada especie;
  - lee las listas (MT: 92; tutores: 38, 52 y 60) y las randomiza sin repetir movimientos, sin tocar las MO y conservando Destello y Golpe Cabeza;
  - en Gen4, la descripción y el color de la MT01 siguen a su movimiento nuevo, y un segundo cambio de las MT vuelve a encontrar todas las tablas;
  - compatibilidad de tutores completa;
  - objeto del inicial en Diamante y Platino;
  - nombres y clases barajados;
  - ajustes de overlay y Recogida, que no se aplican dos veces;
  - exportación y relectura de todo.
- `--nds-ui-probe` (cinco juegos, 25 comprobaciones cada uno):
  - las secciones nuevas abren el editor compartido, o un aviso con el motivo si el juego no las tiene;
  - el Global Template repite las operaciones nuevas y los ajustes de ambas generaciones.
- Regresión completa del runner: todas las suites pasan. En una de las cinco pruebas de interfaz (Platino) hubo un fallo intermitente que no se reprodujo en 16 ejecuciones seguidas.
- Pendiente: la prueba en juego. Sobre todo:
  - un tutor y una MT nuevos se aprenden;
  - la tabla de tipos y la captura garantizada funcionan en combate;
  - la Recogida da los objetos nuevos;
  - los nombres barajados se ven bien en Negro, Blanco y Blanco 2.
