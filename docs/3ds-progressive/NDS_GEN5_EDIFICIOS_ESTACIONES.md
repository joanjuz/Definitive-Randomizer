# Editor DS Gen5: edificios, árboles y estaciones en la vista gráfica

La capa gráfica del editor de Negro (IRBO/0), Blanco (IRAS/0) y Blanco 2 (IRDO/0) dibuja ahora los **edificios y objetos tridimensionales** de cada bloque con sus modelos y texturas originales. Se dibujan sobre el terreno que ya existía, con su posición y su giro. También se puede elegir la **estación** de la vista previa.

- Es de **solo lectura**: no se mueven ni se editan estructuras.
- No cambia ningún byte de la ROM.
- Los eventos, la edición y la exportación siguen como estaban.
- Negro 2 sigue sin perfil.

## Qué archivos intervienen

| Recurso | Negro y Blanco | Blanco 2 |
|---|---|---|
| Bloques de mapa (terreno, permisos, lista de edificios) | `a/0/0/8` | `a/0/0/8` |
| Tabla de áreas (no es un NARC, 10 bytes por área) | `a/0/1/3` (282 áreas) | `a/0/1/3` (409 áreas) |
| Texturas del terreno, una por área | `a/0/1/4` (282) | `a/0/1/4` (409) |
| Paquetes de edificios exteriores (`AB`) | `a/2/2/9` (52) | `a/2/2/5` (70) |
| Paquetes de edificios interiores (`AB`) | `a/2/3/0` (64) | `a/2/2/6` (93) |
| Texturas de edificios exteriores | `a/1/7/6` (52) | `a/1/7/4` (70) |
| Texturas de edificios interiores | `a/1/7/7` (64) | `a/1/7/5` (93) |

- Blanco comparte con Negro la tabla de áreas y los interiores.
- Sus paquetes y texturas exteriores son propios (Ciudad Negra y Bosque Blanco).
- Cada archivo queda fijado por su CRC32 en `Gen5BuildingProfile`.

`a/2/2/5` y `a/2/2/6` de Negro y Blanco **no** son modelos: son 151 y 174 imágenes de 256 × 256 en BGR555 comprimidas con LZ11. Los paquetes `AB` de `a/2/2/9` sí contienen los modelos (en la segunda mitad de cada paquete).

## Cómo se relacionan con cada mapa

1. **Cabecera → área.** El campo +2 de la cabecera es un **área**, no directamente un conjunto de texturas. En la tabla de áreas, el campo +2 vale lo mismo que el índice en las 282 áreas de Negro y las 409 de Blanco 2. Por eso la lectura anterior funcionaba.
2. **Área → paquete de edificios.**
   - **Blanco 2:** el paquete es el campo +0 del registro de área. El campo +6 indica si es interior (0) o exterior (1) y, con ello, qué archivos se usan. Está documentado por DS Pokémon Hacking («B2W2 Area Data», licencia MIT; se consultó el formato, no se copió código) y se comprobó en los datos: en 487 de los 491 mapas con edificios, el paquete contiene todos los ids de sus bloques.
   - **Negro y Blanco:** el campo +0 de su tabla **no** es el paquete. El paquete sigue el grupo de estaciones del área:
     - exterior: `(área − 2) / 4`;
     - interior: `área − 210` (210 = 2 + 4 × 52 paquetes exteriores).

     No hay ninguna tabla que lo guarde en los datos ni en ARM9/overlays (búsqueda completa). La regla está **deducida por comparación**:
     - el paquete deducido contiene todos los ids de 334 de los 335 mapas con edificios;
     - en 52 áreas es el único paquete posible;
     - Blanco 2 guarda exactamente esa relación en su tabla en sus 280 áreas exteriores (280/280).
3. **Paquete → modelo.**
   - Un paquete `AB` es un contenedor: u16 «AB», u16 número de entradas, desplazamientos u32 y el final.
   - La primera mitad son descriptores, con el id u16 en +0 seguido de datos de animación BTA/BTP/BCA.
   - La segunda mitad son los modelos `BMD0`, en el mismo orden.
   - Se cumple en los 116 paquetes de Negro y los 163 de Blanco 2.
   - Hay ids repetidos (4 en Negro, 8 en Blanco 2), siempre idénticos byte a byte. Si alguna vez no lo fueran, el id se trataría como ambiguo y no se dibujaría.
4. **Paquete → texturas.**
   - Los modelos no traen texturas (`TEX0`).
   - El paquete *k* se texturiza con el miembro *k* del archivo de texturas correspondiente.
   - El lector existente lee los 10 695 modelos de Negro y Blanco 2 sin fallos, y todos sus materiales texturizados (24 384) enlazan con ese conjunto. Los conjuntos vecinos (*k* ± 1) enlazan muchos menos, así que la relación no es casual.

## Posición, orientación y escala

La lista de edificios es la última sección del bloque: un u32 con el número de edificios y 16 bytes por cada uno.

| Campo | Formato | Estado |
|---|---|---|
| +0, +4, +8 | X, Y, Z en `fx32` (1.19.12) respecto al **centro** del bloque; 16 unidades = 1 casilla | confirmado por el formato |
| +12 | giro (yaw) u16, 65 536 = una vuelta | confirmado por el formato |
| +14 | id del edificio, **big-endian** | confirmado por el formato y medido |

- **El id es big-endian.** Leído en little-endian daba valores como 65 280 (`00 FF`), que no son índices: solo 2 de los 373 bloques con edificios de Negro tenían un paquete que los contuviera. En big-endian (`0x00FF` = 255) los 373 lo tienen.
- **La Z almacenada crece hacia el norte**, al revés que el terreno, los permisos y los eventos. El editor la invierte. Medido en todos los bloques con edificios:
  - Con −Z, el 91,1 % (Negro) y el 92,7 % (Blanco 2) de las casillas que cubre un edificio están bloqueadas en los permisos. Sobre el total de casillas, la proporción es del 75,7 % y el 79,7 %.
  - Con −Z, cuatro veces más warps tocan un edificio (sus puertas).
  - Con +Z, la proporción baja al 72,2 % y al 76,5 %, por debajo del azar.
  - En Pueblo Arcilla, el laboratorio aparecía en el mar y las casas lejos de sus puertas. Con −Z, cada warp cae en la puerta de su edificio.
- **Giro.** Es antihorario visto desde arriba, como indica la documentación. Las estadísticas no distinguen los dos sentidos porque casi todos los edificios girados son simétricos. El centro comercial de la Ruta 9 sí lo distingue (id 146, giro 0x4000, origen descentrado):
  - en sentido antihorario queda sobre su huella con la puerta en su warp;
  - en sentido horario tapa el final de la carretera.
- **Escala.** No hay campo de escala; los modelos se dibujan a su tamaño.

## Árboles y vegetación

Hay dos fuentes distintas:

- **Árboles del terreno.** La mayoría de los bosques y bordes de ruta forman parte del **propio modelo del bloque**. Usan materiales `ki02*` en capas:
  - sombra translúcida A3I5 (`ki02c`) a ras de suelo;
  - copas `ki02bx` y `ki02dx` a 1,5 y 3 casillas de altura;
  - frentes verticales `ki02ax`.

  Ya se dibujaban, pero el renderizador pintaba opacas las sombras translúcidas y aparecían **manchas gris oscuro** donde debían verse árboles. Ahora, solo en Gen5, los texeles con alfa parcial se mezclan después de lo opaco, en dos pasadas, sin ocultar lo que hay debajo. En Gen4 el render no cambia: la opción está desactivada y `DrawScene` equivale al `Draw` anterior.
- **Árboles sueltos, farolas, buzones, vallas, muelles…** Son edificios de la lista del bloque y se dibujan con el resto.

La vista sigue siendo **cenital**. Los frentes verticales de los árboles y las fachadas no se ven desde arriba; para verlos haría falta una cámara inclinada. Se probó una proyección oblicua, pero mostraba franjas de caras verticales ocultas que no se pudieron explicar, así que no se incorporó.

## Estaciones

- **Selección.** El juego suma la estación al área de la cabecera: `área = área de la cabecera + estación` (0 primavera, 1 verano, 2 otoño, 3 invierno; `mes % 4`). Solo se aplica en las áreas de estaciones:
  - Blanco 2: 2…0x119, documentado; 0x11A = 2 + 4 × 70 paquetes exteriores;
  - Negro y Blanco: 2…209, por la misma relación con sus 52 paquetes.
- **Qué guardan las cabeceras.** Siempre la primavera: un área 4*p* + 2.
- **Texturas.** En 49 de los 52 grupos de cada juego, los cuatro conjuntos tienen los mismos nombres de textura con píxeles distintos. Las capturas lo muestran: verde, verde azulado, ocre y nieve, en ese orden.
- **Edificios.**
  - Negro y Blanco: el paquete es el mismo en las cuatro estaciones y tiene un único conjunto de texturas, así que los edificios no cambian.
  - Blanco 2: su tabla también repite el paquete en el grupo, pero sus capturas de otoño e invierno sí muestran vegetación otoñal y nevada.
- **Iluminación.** El campo +7 del área cambia con la estación (iluminación), pero el render no aplica iluminación.

**Selector en el editor.** Aparece «Estación» junto a las capas, solo en Gen5 cuando los archivos de edificios están acreditados.

- Cambia el conjunto de texturas y el paquete que carga la vista gráfica.
- Los interiores y las áreas sin estaciones no cambian.
- No modifica la ROM, la sesión ni el proyecto: al cambiar de estación, se crea una caché de imágenes nueva.

## Diferencias entre Negro/Blanco y Blanco 2

| | Negro y Blanco | Blanco 2 |
|---|---|---|
| Archivos de paquetes y texturas | `a/2/2/9`, `a/2/3/0`, `a/1/7/6`, `a/1/7/7` | `a/2/2/5`, `a/2/2/6`, `a/1/7/4`, `a/1/7/5` |
| Paquete de un área | regla del grupo de estaciones (deducida) | campo +0 de la tabla (documentado y medido) |
| Interiores | `área − 210`, sin reutilización | la tabla reutiliza paquetes (21 de 93 áreas no siguen la aritmética) |
| Áreas de estaciones | 2…209 | 2…281 |
| Ids ausentes de su paquete | 4, en la cabecera 239: el id 69 no está en el paquete 63 | 10 (cabeceras 239, 296, 437 y 598) |

Los ids ausentes no se dibujan y se informan por bloque. Son casos de los propios datos, por ejemplo bloques compartidos de la matriz exterior.

## Qué está acreditado y qué no

| Afirmación | Estado |
|---|---|
| Contenedor de bloque, lista de edificios de 16 bytes, id big-endian, posición respecto al centro | confirmado por el formato documentado y observado en las ROM |
| Tabla de áreas de 10 bytes; campo +0 = paquete en Blanco 2; +6 interior/exterior; estaciones = área + mes % 4 | documentado para Blanco 2 y observado |
| Paquetes `AB` divididos en descriptores y modelos; texturas por índice de paquete | observado en las ROM (Negro y Blanco 2: 279 paquetes, 10 695 modelos) |
| Z almacenada invertida | deducido por comparación con permisos, warps y Pueblo Arcilla |
| Paquete de Negro y Blanco = grupo de estaciones | deducido por comparación (334/335 mapas; reproduce la tabla de Blanco 2) |
| Sentido del giro | documentado; confirmado en un caso asimétrico |
| Qué dibuja el juego cuando un id no está en el paquete | **sin comprobar**: el editor no lo dibuja |
| Ciudad Negra y Bosque Blanco (bloques RD y datos de `a/1/5/6`) | **no interpretado**: se dibujan su terreno y los edificios de la lista |
| Animaciones de los descriptores (BTA/BTP/BCA) | no se aplican |

## Rendimiento y memoria

- Los paquetes y sus texturas se cargan bajo demanda en una caché LRU de 12 entradas; los modelos decodificados, en otra de 384.
- Recorrer todos los bloques de un juego deja las dos cachés en su límite. La memoria gestionada vuelve a unos 18–30 MB al terminar.
- El censo completo tarda entre 1,0 y 1,5 s por juego.
- Un bloque nuevo tarda entre 10 y 370 ms en renderizarse, según su tamaño.
- No se añadieron dependencias: se reutilizan los lectores NSBMD/NSBTX existentes.

## Comprobaciones

- **Sintéticas** (`--nds-gen5-buildings-only`, 27, en el CI):
  - perfiles por juego;
  - regla área → paquete de los dos juegos, con rechazos;
  - estaciones;
  - id big-endian;
  - paquetes (mitades, duplicados idénticos y ambiguos, formatos rechazados);
  - colocación (Z invertida y giro antihorario);
  - avisos;
  - terreno solo, sin archivos;
  - pasada translúcida, desactivada fuera de Gen5.
- **Fuera de pantalla** (`--nds-gen5-buildings-ui-only`, 3, en el CI): el selector de estación aparece solo con edificios Gen5 y cambia la vista previa sin escribir.
- **Sonda real** (`--nds-gen5-buildings-probe`): abre la copia sin duplicarla ni escribirla y compara su SHA-256 antes y después.

  | | Negro | Blanco | Blanco 2 |
  |---|---|---|---|
  | Edificios dibujados | 4 090 de 4 094 | 4 090 de 4 094 | 6 348 de 6 358 |
  | Triángulos de edificio sin textura | 0 | 0 | 0 |
  | Casillas bloqueadas bajo edificios | 91,1 % | 91,1 % | 92,7 % |

  En los tres juegos las cachés quedan acotadas. La sonda guarda capturas PNG (privadas) sin y con edificios, con los eventos encima y con las cuatro estaciones.

**Validación jugable.** Ninguna de estas comprobaciones sustituye una revisión visual del propietario: no se compararon capturas del juego en ejecución.

## Limitaciones y siguiente paso

- **Vista cenital.** Las fachadas y los frentes de los árboles no se ven desde arriba. Una cámara inclinada sería el siguiente paso, pero primero hay que explicar las caras verticales ocultas.
- **Edificios en el borde.** Un edificio que sobresale de su bloque (por ejemplo, las casetas de entrada de ruta) se corta en el borde: cada bloque se renderiza por separado. Platino tiene la misma limitación.
- **Ciudad Negra y Bosque Blanco.** Su contenido variable (`a/1/5/6`) no se interpreta.
- **Animaciones e iluminación.** No se aplican las animaciones de agua ni de edificios, ni la iluminación estacional.
- **Edición.** Los edificios no se seleccionan ni se editan. La capa «Estructuras» sigue siendo de Gen4 y en Gen5 explica dónde se dibujan.
