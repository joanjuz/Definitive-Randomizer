# Editor de mapas 3DS: zonas de captura (X / Y)

**Estado:**

| Capacidad | Situación |
| --- | --- |
| **A. Zona de encuentros funcional** | Implementada para X / Y. Escritura comprobada en una copia real de X. **Sin prueba en el juego.** |
| **B. Hierba visible** | **No implementada.** Las casillas convertidas no cambian de aspecto. |
| Rubí Omega / Zafiro Alfa | Rechazado: su tabla de encuentros tiene otro formato. |
| Ultra Sol / Ultra Luna, DS | Sin esta función. |

## Qué hace

1. El botón «Zona de captura» abre un panel.
2. Se eligen casillas con clic (otro clic las quita) o con un rectángulo de dos esquinas.
3. Las casillas que ya tienen encuentros se ven en verde; las marcadas, en verde claro con borde discontinuo.
4. **Si la zona no tiene tabla de encuentros,** se elige la zona de la que copiarla.
5. **«Previsualizar» es obligatorio antes de «Guardar».** Comprueba todo sin escribir; cualquier cambio de casillas u origen obliga a previsualizar otra vez.
6. «Guardar» escribe a través del historial del proyecto: un solo cambio que se deshace y se reconstruye como los demás.

## Cómo funcionan los encuentros en X / Y

Lo comprobado en los datos de X:

### 1. Permiso de la casilla

- Los bloques de mapa (`mapGR`, `a/0/4/1`) tienen una rejilla de 40 × 40 permisos de 32 bits.
- La hierba alta de la Ruta 2 vale **`0x20002004`** en sus 115 casillas.
- El suelo normal sobre el que están los NPC vale **`0x20`**.
- «Enable Catch Zones» convierte exactamente `0x20` en `0x20002004` en tres bloques.
- **El significado de cada bit no está acreditado.** Por eso solo se escriben esos dos valores y solo en ese sentido.

### 2. Tabla de encuentros de la zona

- Es la parte 3 del paquete `ZO` de la zona: 0x188 bytes, que son 0x10 de cabecera y 0x178 de 94 ranuras.
- Está vacía en 307 de las 360 zonas de X y tiene 0x188 bytes en las otras 53.
- **El valor de hierba solo no basta:** `0x20002004` aparece en 11 zonas sin tabla, que no tienen encuentros. Por eso «Enable Catch Zones» crea la tabla en las zonas a las que añade hierba.
- La parte 3 es siempre la última del paquete. Crearla solo añade 0x188 bytes al final y mueve el desplazamiento final.

### 3. Qué zona manda en cada casilla

- **Matrices exteriores** (20 en X, 22 en Zafiro Alfa), que comparten varias zonas:
  - el bloque 0 de la matriz tiene, tras los bloques, un **mapa de zonas** con un u16 por cada 10 × 10 casillas (4 × 4 por bloque);
  - después lleva un u16 más por bloque;
  - el tamaño es exactamente 8 + 36 bytes por bloque, y todas las zonas del mapa son zonas que usan esa matriz;
  - por ejemplo, la Ruta 2 ocupa las filas 400–479 y Pueblo Acuarela las 480–559 de la misma matriz.
- **Matrices interiores:** no tienen mapa de zonas. Solo se admiten si una única zona usa la matriz.

## Qué escribe y qué comprueba

- **Bloques.** Solo cambian las casillas elegidas, de `0x20` a `0x20002004`.
  - Se comprueba byte a byte que nada más cambia.
  - Se rechaza cualquier casilla que no sea suelo normal: muros, agua, hierba ya existente y otros valores.
  - Se rechaza un bloque que aparezca en más de una posición de las matrices del juego. En X no hay ninguno, pero se comprueba igualmente.
- **Casillas rechazadas:**
  - las que, según el mapa de zonas, pertenecen a otra zona;
  - las que ocupa un NPC, un objeto, un warp o un activador;
  - las de zonas cuyas posiciones no son casillas;
  - más de 1600 casillas por operación.
- **Tabla.**
  - Si la zona ya tiene tabla, no se toca.
  - Si no la tiene, se copia entera la tabla de la zona de origen tal como está en el proyecto. Si se randomizó antes, se copian los Pokémon randomizados.
  - La tabla nueva aparece en el editor «Encuentros» y la incluyen randomizaciones posteriores, porque ambos encuentran las tablas por su desplazamiento.
- **Reconstrucción.** Los dos GARC se reconstruyen y se releen, y se comprueba que el resto de miembros es idéntico.
  - Si los archivos no coinciden con lo que leyó el editor, no se escribe nada.

## Pruebas

- **`--map3ds-catch-zones-only`** (13 comprobaciones sintéticas):
  - mapa de zonas, dueño de cada casilla y usos de los bloques;
  - escritura de casillas byte a byte y tabla añadida al final;
  - todos los rechazos y la relectura.
- **`--map3ds-catch-zones-ui-only`** (9 fuera de pantalla):
  - botón, origen de la tabla, clics y rectángulo;
  - vista previa obligatoria y guardado por el proyecto.
- **Copia real de X, `--map3ds-catch-zone-probe`:** zona 8 (Pueblo Acuarela, sin encuentros), casillas (444–449, 518–521) = 24, tabla copiada de la zona 259 (Ruta 2). 8/8:
  - la vista previa no escribe;
  - al reabrir aparecen las 24 casillas y la tabla igual a la de la Ruta 2;
  - solo cambian esas casillas de los bloques 7 y 10;
  - las demás zonas quedan idénticas;
  - queda entrada de historial;
  - deshacer devuelve el SHA-256 original de los dos archivos;
  - un editor con datos antiguos no puede escribir.
  - Verificado también por separado con un lector propio.
- **`--map3ds-catch-zone-rebuild`:** la imagen 3DS contiene byte a byte los dos archivos editados.

## Limitaciones

- **Sin prueba en el juego.**
- **Hierba no visible.** Representarla exige modificar el modelo 3D del bloque (BCH), que es otro formato con otros riesgos; queda para un incremento propio.
- **Solo X / Y.**
  - La tabla de OR/AS es distinta (0xF6 bytes, según el editor de encuentros).
  - Sus valores de hierba no se han estudiado.
- **No se distinguen otros tipos de encuentro** (flores, hierba oscura, cuevas, agua). Solo se escribe el valor de la hierba de la Ruta 2, que es la primera fila de la tabla.
- **Quitar encuentros a casillas existentes** no forma parte de esta versión. Lo hecho aquí se deshace desde el historial.
