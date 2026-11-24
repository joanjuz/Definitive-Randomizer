# Editor de mapas 3DS: zonas de captura (Gen6 y Ultra Sol / Ultra Luna)

**Estado:**

| Juego | Situación |
| --- | --- |
| **Pokémon X** | **Prueba en el juego aprobada por el propietario.** Pueblo Acuarela, 24 casillas nuevas (6 × 4), tabla copiada de la Ruta 2: producen encuentros salvajes. La aprobación cubre ese caso; no se extiende sola a otros terrenos. |
| **Pokémon Y** | El mismo formato que X, comprobado en los datos de Y (estructura de zonas, tablas, valor de hierba y mapas de zonas idénticos). Sin prueba en el juego. |
| **Rubí Omega** | **Prueba jugable aprobada** por el propietario en Villa Raíz (24 casillas, tabla de Ruta 101), ROM `OR-captura-villa-raiz.3ds`. |
| **Zafiro Alfa** | **Prueba jugable aprobada** por el propietario en Villa Raíz (24 casillas, tabla de Ruta 101), ROM `AS-captura-villa-raiz.3ds`; copia real 11/11, sincronización EN y restauración exacta. |
| **Ultra Luna** | **Prueba jugable aprobada** por el propietario en la Ruta 13: 72 casillas de arena `0x19` junto a la entrada al Desierto de Haina, con la tabla 0 del desierto (ROM `UL-captura-ruta13-arena.3ds`). La primera prueba, en Pueblo Lilii, se descartó: aquella hierba es terreno de la Ruta 1 y la caja se escribió con el doble de tamaño. |
| **Ultra Sol** | El mismo caso que Ultra Luna (Ruta 13 y tabla 0 del Desierto de Haina de Ultra Sol): copia real limpia 8/8 y ROM `US-captura-ruta13-arena.3ds`. **Pendiente de la prueba del propietario.** |
| **Juegos DS** | Sin esta función de zonas de captura 3DS. |

**Hierba visible:**

- **Gen6 (X/Y, OR/AS):** no implementada y **fuera del roadmap por decisión del propietario**. Una zona de captura es funcional cuando el juego produce encuentros en sus casillas, aunque el escenario siga mostrando suelo normal.
- **Ultra Sol / Ultra Luna:** **prioridad del propietario**, porque sin ella la zona no se distingue en el juego. Hay una primera versión con prueba jugable pendiente; ver «Hierba visible en Ultra Sol / Ultra Luna».

El editor señala en el mapa las casillas con encuentros (verde) y las marcadas (verde claro).

## Qué hace

1. El botón «Zona de captura» abre un panel.
2. Se eligen casillas con clic (otro clic las quita) o con un rectángulo de dos esquinas.
3. **Si la zona no tiene tabla de encuentros,** se elige la zona de la que copiarla.
4. **«Previsualizar» es obligatorio antes de «Guardar».** Cualquier cambio de casillas u origen obliga a previsualizar otra vez.
5. «Guardar» escribe todos los archivos afectados en una sola entrada del historial del proyecto, que se deshace y se reconstruye como las demás.

## Cuándo produce encuentros una casilla

Hacen falta dos cosas, comprobadas en los datos de cada familia:

1. **El permiso de la casilla es el valor de hierba de esa familia.**
2. **La zona dueña de la casilla tiene tabla de encuentros:** la parte 3 de su paquete `ZO`, vacía en las zonas sin encuentros.
   - El valor de hierba aparece también en zonas sin tabla, que no tienen encuentros.

**Qué zona es la dueña:**
- **Matrices exteriores:** el bloque 0 de la matriz lleva, tras los bloques, un mapa de zonas (un u16 por cada 10 × 10 casillas) y un u16 más por bloque. Su tamaño es exactamente 8 + 36 bytes por bloque. Hay 20 en X/Y y 22 en OR/AS, y sus zonas son siempre zonas que usan la matriz.
- **Matrices interiores:** no tienen mapa; solo se admiten si una única zona usa la matriz.

| | X / Y | Rubí Omega / Zafiro Alfa |
| --- | --- | --- |
| Suelo normal | `0x20` | `0x20` |
| Hierba con encuentros | **`0x20002004`** (las 115 casillas de hierba de la Ruta 2 de X; Y igual) | **`0x20004004`** (8693 casillas en 45 zonas con tabla, igual en OR y AS). `0x20002004` **no existe** en OR/AS |
| Partes del paquete `ZO` | 4; la tabla es la última | 5; la tabla va seguida de una parte 4 de 12 bytes |
| Tabla | 0x188 bytes, en 53 de 360 zonas | 0x104 bytes, en 150 de 536 zonas |
| Copia compartida | — | El último miembro del archivo de zonas es un paquete `EN` con una copia de cada tabla, idéntica byte a byte a la de su zona en las 536 zonas de OR y de AS. El editor de encuentros escribe ambas |

**Diferencias entre Rubí Omega y Zafiro Alfa:** las mismas 150 zonas tienen tabla y el formato es idéntico. Solo difieren los Pokémon de 28 tablas (exclusivos de cada versión).

## Qué escribe y qué comprueba

- **Casillas.**
  - Solo cambian las casillas elegidas, de `0x20` al valor de hierba de la familia. Se comprueba byte a byte.
  - Se rechazan:
    - muros, agua, hierba existente y cualquier otro valor;
    - casillas de otra zona según el mapa de zonas;
    - casillas ocupadas por un NPC, un objeto, un warp o un activador;
    - bloques que aparezcan en más de una posición de las matrices (no hay ninguno en X, Y, OR ni AS, pero se comprueba);
    - zonas cuyas posiciones no son casillas;
    - más de 1600 casillas.
  - Valores distintos de esos dos, como `0x20084004` de OR/AS, no se escriben: su efecto no está acreditado.
- **Tabla.**
  - Si la zona ya la tiene, no se toca.
  - Si no la tiene, se **inserta** una copia exacta de la de la zona de origen tal como esté en el proyecto (también si está randomizada). Los desplazamientos de las partes siguientes se mueven y el resto de partes se comprueba byte a byte.
  - En OR/AS se inserta además la misma tabla en la copia `EN`, en la posición de la zona. Las demás copias solo se desplazan.
  - En OR/AS se rechaza la escritura si la copia `EN` de la zona o del origen no coincide con su tabla.
- **Reconstrucción.** Se reconstruyen y releen los GARC de bloques y de zonas, comprobando que los demás miembros no cambian. Si los archivos no coinciden con lo que leyó el editor, no se escribe nada.
- **Compatibilidad.** El editor «Encuentros» y la randomización encuentran las tablas por su desplazamiento. La tabla creada aparece como una ubicación más, se edita y se randomiza como las demás. En OR/AS esto mantiene sincronizada la copia `EN`.

### «Enable Catch Zones» de OR/AS

Su manifiesto sirvió de referencia, pero no se copia:

- en tres bloques escribe `0x20002004`, el valor de X/Y, que OR/AS no usa en ninguna casilla;
- en dos zonas (por ejemplo, la 447) deja la parte 4 con longitud negativa.

El editor usa solo valores y estructuras leídos de los juegos.

## Pruebas

- **`--map3ds-catch-zones-only`** (21 comprobaciones sintéticas):
  - valores por familia y mapa de zonas;
  - dueño de cada casilla y usos de los bloques;
  - casillas y tabla en X/Y y OR/AS: inserción antes de la parte 4 y copia `EN`;
  - rechazos, incluidas la copia `EN` ausente o distinta;
  - relectura.
- **`--map3ds-catch-zones-ui-only`** (10 fuera de pantalla):
  - botón, origen de la tabla, clic y rectángulo;
  - vista previa obligatoria y guardado;
  - panel en OR/AS.
- **`--map3ds-catch-zone-probe`**, copias reales:

| Juego | Caso | Resultado |
| --- | --- | --- |
| X | zona 8, Pueblo Acuarela, sin tabla: 24 casillas y tabla de la Ruta 2 | 8/8 (la copia de la prueba aprobada) |
| X | zona 259, Ruta 2, con tabla: 10 casillas | 9/9; el editor de encuentros sigue con 54 ubicaciones; sin dejar cambios |
| X | zona 4, interior de Pueblo Boceto (una sola zona), sin tabla: 4 casillas | 9/9; el editor de encuentros pasa de 54 a 55; sin dejar cambios |
| X | casillas de la Ruta 2 pedidas para Pueblo Acuarela | rechazado: «pertenece a la zona 259» |
| Rubí Omega | zona 6, Villa Raíz, sin tabla: 24 casillas (92–97, 172–175) y tabla de la Ruta 101 | 11/11. Cambian solo los miembros 6 y 537 (`EN`) y 24 casillas del bloque 6. El editor de encuentros pasa de 150 a 151 ubicaciones y las 151 copias `EN` coinciden con sus zonas. Deshacer devuelve los dos archivos originales. Verificado por separado con un lector propio |

- **`--map3ds-catch-zone-rebuild`:** las imágenes 3DS contienen byte a byte los archivos editados.

## Ultra Sol / Ultra Luna

El mecanismo es distinto del de Gen6. No hay rejilla de casillas.

### Qué produce un encuentro

Un Pokémon salvaje aparece al pisar un triángulo del suelo cuando se cumplen dos condiciones:

- el **atributo** del triángulo (malla de colisión, parte 6 de cada bloque) está permitido por la **máscara** de una **región de encuentros**;
- la forma de esa región lo cubre.

La región indica qué **tabla** del área usa. Se comprobó en las 336 áreas de Ultra Luna:

- **Regiones.** Están en la **parte 6 del paquete de eventos (ED)** del área, en un archivo por zona.
  - Cada registro mide 28 bytes: `{u32 6, u32 tabla, u32 máscara, 3 × u32, u32 desplazamiento de la forma}`.
  - La forma puede ser una caja de 48 bytes (posición, cuaternión y tamaño) o un cilindro de 28 bytes (posición, radio y altura).
  - **El tamaño de la caja es un semieje** (la mitad del ancho). Leídas así, las 12 cajas de hierba de Ultra Luna cubren el 99,8 % de su mancha de hierba y ninguna otra; leídas como ancho completo, el 44,6 %.
  - Hay 231 regiones en 64 áreas, y ningún área tiene regiones sin tablas.
- **Tablas.** Están en el **paquete EA** del área (slot 9): entradas de 768 bytes, de día y de noche.
  - Todos los índices de las regiones están dentro de su paquete.
  - En los paquetes originales los datos empiezan en 0x80, y un área sin encuentros tiene una sola entrada vacía.
- **Máscara.** El bit `n` permite el atributo de suelo `n`.
  - Solo hay 1 máscara de 231 con un atributo ausente del suelo de su área.
  - Las cajas de hierba de la Ruta 1 (bit 31) cubren casi solo triángulos de atributo `0x1F`; las de agua (bit 8) cubren el atributo `0x08`.
  - Atributos que usan las máscaras originales (regiones por bit): `0x00` 124, `0x16` 44, `0x08` 15, `0x12` 14, `0x1F` 12, `0x19` 8, `0x0A` 7, `0x14` 5, `0x15` 5 y otros con 1 o 2.
  - La hierba `0x1F` solo existe en el terreno de la Ruta 1 y sus copias. El suelo `0x00` es el de la hierba de casi todas las demás rutas; `0x16`, el de las de Ula-Ula y Poni; `0x19`, la arena del Desierto de Haina; `0x15`, el de algunas cuevas y colinas.
- **Terreno compartido.** Un área también contiene el terreno de sus vecinas, para dibujarlo; a veces con los mismos bloques en la misma posición y a veces como copia con otros números de bloque. Pueblo Lilii comparte 8 bloques con la Ruta 1: sus 294 casillas de hierba son hierba de la Ruta 1, donde el jugador está en el área de la Ruta 1. El terreno propio de Pueblo Lilii tiene suelos `0x00`–`0x08`, sin hierba de encuentros.
- **Ultra Sol y Ultra Luna:** las regiones (parte 6 de ED) son idénticas en las 336 áreas; las tablas difieren.
- **Indicios no imprescindibles:** el campo `u32 0x38` de los datos de zona tiene los bits 0 y 2 en el 98 % de las zonas con regiones, pero también en zonas sin ellas. Pueblo Lilii tiene el mismo valor que la Ruta 1, así que no bloquea la prueba.

### Qué hace el editor

1. **Casillas.** Se eligen casillas (de 105 unidades, como en el resto del editor) con uno de los suelos que alguna región original usa como único bit de su máscara: `0x00`, `0x15`, `0x16`, `0x19` y `0x1F`. Todas las casillas de una zona deben tener el mismo suelo. Los suelos que solo aparecen combinados (suelos de cueva) o que ninguna máscara usa se rechazan. También se rechaza el terreno de otra área:
   - un bloque que carga otra área en la misma posición, salvo que el área ya tenga encuentros en él;
   - la misma hierba (mismas coordenadas y altura) que tiene encuentros en otra área.

   El panel marca con borde punteado las casillas disponibles y la capa «Encuentros» muestra en verde las que ya los tienen.
2. **Región.** Se añade una caja alineada con los ejes que cubre las casillas (tamaño escrito como semieje), con la máscara de ese suelo y altura 500. Si la caja abarca casillas de hierba no marcadas, se avisa: también tendrán encuentros.
3. **Tabla.** Se elige entre las tablas de las regiones originales con uno de esos suelos (190 en Ultra Luna; el nombre indica el suelo).
   - Si es de otra área, se copia al área. En un paquete original se rellena su entrada vacía, conservando el inicio de datos en 0x80.
   - Si es del área, se usa sin copiarla.
4. **Escritura.** ED y EA se reconstruyen byte a byte; si los originales no se reconstruyen exactamente, no se escribe. Se escriben en una sola reconstrucción del archivo de campo y en una sola entrada del historial.
5. **Restauración.** Como el archivo de campo supera el tamaño de los puntos de restauración, los dos miembros originales se guardan juntos para restaurar el área. El GARC se reescribe con alineación de 4 bytes, igual que en cualquier otro guardado del proyecto; por eso el archivo puede no ser idéntico al volcado original aunque todos sus miembros lo sean.
6. **Compatibilidad.** El editor de encuentros muestra la tabla nueva (día y noche) y la randomización la incluye.

**Límites de esta primera versión:**

- solo los suelos `0x00`, `0x15`, `0x16`, `0x19` y `0x1F`, uno por zona;
- solo áreas con un único archivo de regiones (309 de 336);
- solo tablas de regiones originales con esos suelos;
- regiones de caja;
- el editor no sabe dónde están los límites entre áreas que marca un warp de conexión: si una mancha cruza uno, hay que marcar solo el lado del área.

### Pruebas

- **`--map3ds-gen7-catch-zones-only`** (16 comprobaciones sintéticas) y **`--map3ds-gen7-catch-zones-ui-only`** (5).
- **Segunda prueba, copia real limpia de Ultra Luna (aprobada en el juego por el propietario):** Ruta 13 (área 138), 72 casillas de arena `0x19` (X 183–193, Y 303–312, del lado de la ruta del warp al desierto), tabla 0 del Desierto de Haina. 8/8; verificado también con un lector independiente (miembros 1518 y 1527, solo la parte 6 de ED, tablas de pesca intactas, tabla nueva idéntica a la de Haina, semiejes 630 × 472,5). Imagen `UL-captura-ruta13-arena.3ds`. **Aprobada en el juego.** Ultra Sol: el mismo caso, copia real 8/8 e imagen `US-captura-ruta13-arena.3ds`, pendiente.
- **Búsqueda de sitios** (solo lectura): `--map3ds-gen7-encounter-attribute-candidates`, `--map3ds-gen7-encounter-area-info` y `--map3ds-gen7-encounter-reach`. La mancha libre de Colina Dequilate está desconectada del suelo del área (decorado); la de la Ruta 13 está conectada con las entradas.
- **Copia real de Ultra Luna limpia, `--map3ds-gen7-catch-zone-probe`:** Pueblo Lilii, 61 casillas de hierba, tabla 0 de la Ruta 1. 8/8:
  - solo cambian los miembros de eventos y tablas del área 1;
  - el editor de encuentros pasa de 794 a 796 ubicaciones;
  - la restauración devuelve todos los miembros originales;
  - un editor con datos antiguos no puede escribir.
  - Verificado también con un lector independiente.
- **Imagen 3DS reconstruida:** contiene byte a byte el archivo de campo editado. **Descartada para la prueba jugable** (hierba de la Ruta 1 y caja escrita con el tamaño como ancho completo).
- **`--map3ds-gen7-encounter-candidates`** (solo lectura): con las reglas actuales, en Ultra Luna solo queda una mancha `0x1F` sin encuentros, en el área 010 (una copia de la Ruta 1 cuyo uso en el juego no se conoce).

## Hierba visible en Ultra Sol / Ultra Luna

### Qué es la hierba que se ve

La hierba alta **no está ligada a las regiones de encuentros**: es un modelo aparte.

| Recurso | Dónde | Datos de Ultra Luna |
| --- | --- | --- |
| Modelo de hierba | **Parte 1 del bloque** del archivo de bloques (`a/0/8/6`, paquete «BG» de 8 partes, miembros LZ11) | 143 de 852 bloques. Siempre un modelo GFL2, `Map07_encgrass01`, con un material de sombreador `Kusa` por mancha y una malla por mancha (`blc00`, `blc01`…) |
| Texturas | Parte 0 del BG del área (slot 1 del archivo de campo) | 55 áreas tienen texturas `encgrass` |
| Posiciones | Vértices `float32` ya en coordenadas del mundo | La transformación del modelo solo es un punto de referencia |

- Las mallas coinciden con las manchas de suelo de encuentros. Por ejemplo, las de la Ruta 1 cubren las mismas casillas que su suelo `0x1F`.
- La parte 6 del bloque es la colisión (el suelo y sus atributos).
- La parte 7 existe en todos los bloques y no es específica de la hierba.
- Un bloque puede estar en las matrices de varias áreas: su hierba se ve desde todas ellas.

### Qué hace esta primera versión

`Map3dsGen7VisibleGrassService` copia una **mancha original de una sola malla** (23 plantillas en Ultra Luna) en la parte 1 de un **bloque que no tiene hierba**:

- desplaza todos los vértices, la caja envolvente y el punto de referencia;
- el resto de bytes es el original;
- antes de escribir comprueba que el bloque se reconstruye byte a byte;
- solo cambia ese miembro del archivo de bloques, recomprimido y comprobado;
- el archivo (unos 77 MB) cabe en los puntos de restauración, así que **el historial lo deshace byte a byte**.

**Comprobaciones previas:**

- el área tiene las texturas de la plantilla;
- la mancha cabe entera en el bloque;
- el desnivel del suelo debajo es como máximo de 30 unidades; la base de la hierba se coloca a la altura media del suelo.

La zona de captura de esas casillas se añade aparte, con el servicio de zonas de captura. **La hierba sola no produce encuentros.**

**Verificación con datos reales** (`--map3ds-gen7-grass-verify`, Ultra Luna y Ultra Sol limpias):

- los 852 bloques se reconstruyen byte a byte;
- los 143 modelos de hierba se desplazan y se releen;
- con desplazamiento cero quedan idénticos.

### Prueba preparada (pendiente del propietario)

- **Pueblo Lilii, bloque 23** (su césped del oeste): plantilla del bloque 19, unas 6 × 6 casillas, y debajo una zona con la tabla 0 del Cementerio de Hauoli.
- **El suelo de ese césped es `0x01`, que ninguna región original usa solo.** Por eso esa zona es **experimental**: la prueba dirá si el juego acepta una máscara de ese suelo. Está marcada así en el historial, y el editor no ofrece ese suelo.
- Copia real 8/8, verificación independiente (solo cambia la parte 1 del bloque 23, idéntica a la plantilla salvo 512 `float` por eje) e imagen con los dos archivos editados.

### Limitaciones

- Solo plantillas de una malla, y solo en bloques sin hierba; añadir una malla a un modelo de hierba existente queda fuera.
- No añade texturas. Áreas sin texturas `encgrass` (por ejemplo, la Ruta 13) no admiten hierba visible en esta versión, y la arena del desierto tampoco tiene hierba en el juego original.
- La hierba es un modelo plano: no se adapta a pendientes.
- Todavía no está en el panel del editor; se aplica con el servicio y el runner.
- **Sin prueba en el juego**: no se sabe aún si se dibuja y se mueve como la original al caminar.

## Limitaciones

- Aprobadas en el juego: Pueblo Acuarela en X, Villa Raíz en Rubí Omega y Zafiro Alfa, y la arena de la Ruta 13 en Ultra Luna. Y y Ultra Sol, pendientes.
- Solo se escribe la hierba normal de cada familia. Otros tipos de encuentro (flores, hierba oscura, cuevas, agua) quedan fuera mientras no se acredite su valor.
- Quitar encuentros a casillas existentes no forma parte de esta versión; lo hecho se deshace desde el historial.
