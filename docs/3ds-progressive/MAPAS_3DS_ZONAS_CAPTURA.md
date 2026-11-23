# Editor de mapas 3DS: zonas de captura (Gen6 y Ultra Sol / Ultra Luna)

**Estado:**

| Juego | Situación |
| --- | --- |
| **Pokémon X** | **Prueba en el juego aprobada por el propietario.** Pueblo Acuarela, 24 casillas nuevas (6 × 4), tabla copiada de la Ruta 2: producen encuentros salvajes. La aprobación cubre ese caso; no se extiende sola a otros terrenos. |
| **Pokémon Y** | El mismo formato que X, comprobado en los datos de Y (estructura de zonas, tablas, valor de hierba y mapas de zonas idénticos). Sin prueba en el juego. |
| **Rubí Omega** | **Prueba jugable aprobada** por el propietario en Villa Raíz (24 casillas, tabla de Ruta 101), ROM `OR-captura-villa-raiz.3ds`. |
| **Zafiro Alfa** | **Prueba jugable aprobada** por el propietario en Villa Raíz (24 casillas, tabla de Ruta 101), ROM `AS-captura-villa-raiz.3ds`; copia real 11/11, sincronización EN y restauración exacta. |
| **Ultra Luna** | Implementado (ver «Ultra Sol / Ultra Luna» más abajo). Escritura comprobada en una copia real limpia de Ultra Luna (Pueblo Lilii). **Sin prueba en el juego.** |
| **Ultra Sol** | Mismo código; sus regiones son idénticas a las de Ultra Luna y sus tablas distintas. Sin copia real ni prueba en el juego. |
| **Juegos DS** | Sin esta función de zonas de captura 3DS. |

**Hierba visible:** no implementada y **fuera del roadmap activo por decisión del propietario**. Una zona de captura es funcional cuando el juego produce encuentros en sus casillas, aunque el escenario siga mostrando suelo normal. El editor señala en el mapa las casillas con encuentros (verde) y las marcadas (verde claro).

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
  - Hay 231 regiones en 64 áreas, y ningún área tiene regiones sin tablas.
- **Tablas.** Están en el **paquete EA** del área (slot 9): entradas de 768 bytes, de día y de noche.
  - Todos los índices de las regiones están dentro de su paquete.
  - En los paquetes originales los datos empiezan en 0x80, y un área sin encuentros tiene una sola entrada vacía.
- **Máscara.** El bit `n` permite el atributo de suelo `n`.
  - Solo hay 1 máscara de 231 con un atributo ausente del suelo de su área.
  - Las cajas de hierba de la Ruta 1 (bit 31) cubren casi solo triángulos de atributo `0x1F`; las de agua (bit 8) cubren el atributo `0x08`.
  - Un atributo sin región no da encuentros: Pueblo Lilii tiene 294 casillas de hierba `0x1F` y ninguna región.
- **Ultra Sol y Ultra Luna:** las regiones (parte 6 de ED) son idénticas en las 336 áreas; las tablas difieren.
- **Indicios no imprescindibles:** el campo `u32 0x38` de los datos de zona tiene los bits 0 y 2 en el 98 % de las zonas con regiones, pero también en zonas sin ellas. Pueblo Lilii tiene el mismo valor que la Ruta 1, así que no bloquea la prueba.

### Qué hace el editor

1. **Casillas.** Se eligen casillas (de 105 unidades, como en el resto del editor) que tengan suelo de hierba `0x1F`, el de la Ruta 1. Cualquier otro suelo se rechaza.
2. **Región.** Se añade una caja alineada con los ejes que cubre las casillas, con la máscara de `0x1F` y altura 500, como las originales. Si la caja abarca casillas de hierba no marcadas, se avisa: también tendrán encuentros.
3. **Tabla.** Se elige entre las tablas de las regiones de hierba del juego.
   - Si es de otra área, se copia al área. En un paquete original se rellena su entrada vacía, conservando el inicio de datos en 0x80.
   - Si es del área, se usa sin copiarla.
4. **Escritura.** ED y EA se reconstruyen byte a byte; si los originales no se reconstruyen exactamente, no se escribe. Se escriben en una sola reconstrucción del archivo de campo y en una sola entrada del historial.
5. **Restauración.** Como el archivo de campo supera el tamaño de los puntos de restauración, los dos miembros originales se guardan juntos para restaurar el área. El GARC se reescribe con alineación de 4 bytes, igual que en cualquier otro guardado del proyecto; por eso el archivo puede no ser idéntico al volcado original aunque todos sus miembros lo sean.
6. **Compatibilidad.** El editor de encuentros muestra la tabla nueva (día y noche) y la randomización la incluye.

**Límites de esta primera versión:**

- solo suelo `0x1F`;
- solo áreas con un único archivo de regiones (309 de 336);
- solo tablas de regiones de hierba;
- regiones de caja.

### Pruebas

- **`--map3ds-gen7-catch-zones-only`** (13 comprobaciones sintéticas) y **`--map3ds-gen7-catch-zones-ui-only`** (3).
- **Copia real de Ultra Luna limpia, `--map3ds-gen7-catch-zone-probe`:** Pueblo Lilii, 61 casillas de hierba, tabla 0 de la Ruta 1. 8/8:
  - solo cambian los miembros de eventos y tablas del área 1;
  - el editor de encuentros pasa de 794 a 796 ubicaciones;
  - la restauración devuelve todos los miembros originales;
  - un editor con datos antiguos no puede escribir.
  - Verificado también con un lector independiente.
- **Imagen 3DS reconstruida:** contiene byte a byte el archivo de campo editado.

## Limitaciones

- Solo se acreditan en el juego las 24 casillas de Pueblo Acuarela en X. Y, Rubí Omega y Zafiro Alfa están pendientes de prueba jugable.
- Solo se escribe la hierba normal de cada familia. Otros tipos de encuentro (flores, hierba oscura, cuevas, agua) quedan fuera mientras no se acredite su valor.
- Quitar encuentros a casillas existentes no forma parte de esta versión; lo hecho se deshace desde el historial.
