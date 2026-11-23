# Editor de mapas 3DS: «Añadir enfermera» (Gen6)

**Estado:**
- Implementado para Pokémon X / Y y Rubí Omega / Zafiro Alfa.
- Escritura comprobada en una copia real de X.
- **Prueba en el juego aprobada por el propietario en Pokémon X** (Ruta 2, imagen v2): la enfermera aparece, se puede hablar con ella, cura al equipo y el juego no se congela. La primera versión, sin la entrada del modelo en la lista del área, se congelaba al entrar en la ruta.
- Sin prueba en el juego: Y, Rubí Omega y Zafiro Alfa.
- Ultra Sol / Ultra Luna, Sol / Luna y los juegos de DS no tienen esta función.

## Qué hace

El botón «Añadir enfermera» de la barra del mapa coloca en la zona abierta una enfermera de Centro Pokémon. Al hablar con ella, ejecuta el mismo script que las enfermeras de los Centros: saluda, pregunta, cura al equipo y se despide.

No hay que conectar scripts ni buscar identificadores.

1. Pulsa «Añadir enfermera».
2. Haz clic en una casilla del mapa o escribe sus coordenadas.
3. Elige hacia dónde mira.
4. «Previsualizar» dibuja la enfermera (círculo rosa con cruz y flecha de dirección) sin escribir nada.
5. «Guardar» la añade a través del historial del proyecto. Se deshace desde el historial y se reconstruye como cualquier otro cambio.

## Cómo funciona en el juego

En Gen6, el campo `0x0A` de cada NPC es su script de conversación:

- **2000 significa «ninguno».** El motor lo comprueba antes de lanzar un script; el código del juego compara con `0x7D0`.
- **Los ID altos van a scripts comunes.** Una tabla de rangos de `code.bin` envía cada rango de ID a un script común:
  - X: 66 entradas en `0x5457A4`; los scripts comunes están en `a/0/3/1`.
  - Zafiro Alfa: la misma estructura; los scripts comunes están en `a/0/2/9`.
- **El rango 2100–2199 es el del Centro Pokémon.**
  - En X va al script común 43 (textos de la historia, archivo 97).
  - En Zafiro Alfa va al script común 63.
- **El caso 2100 de ese script es la secuencia completa de la enfermera.**
  - Saludo según la hora del día («Buenos días / Hola / Buenas noches… ¿Quieres que cure a tus Pokémon?»).
  - Pregunta sí/no, curación y despedida («Hemos restaurado tus Pokémon…»).
- **Todas las enfermeras de Centro usan el script 2100.**
  - X: 17, modelo 59. Zafiro Alfa: 16, modelo 298.
  - Ningún otro NPC lo usa.
- **Las tablas de casos de los scripts son las de AMX.** Su formato es: recuento, salto por defecto y pares (valor, salto). Con esa lectura, en cada script común el caso `-1` no hace nada, el primer ID del rango es la conversación y el caso por defecto informa de un error.

La función no crea un script: reutiliza el del juego.

## Qué escribe

En la tabla de entidades de la zona (parte 1 del paquete `ZO`) se añade un registro de NPC de 0x30 bytes después del último NPC:

- **Origen del registro.** Es una copia exacta de la primera enfermera del propio juego. Si las enfermeras no coinciden en modelo, o alguna se mueve o tiene bandera de aparición, la función no se ofrece.
- **Campos que cambian.** Solo el ID (igual al índice, como en todas las zonas de X y Zafiro Alfa), la orientación (`0x0C`) y la casilla (`0x28`/`0x2A`).
- **Ajustes de la tabla.**
  - El recuento de NPC sube en 1.
  - La longitud declarada de la tabla sube 0x30.
  - Los desplazamientos de las partes siguientes del paquete suben 0x30.
- **Lo que se conserva.**
  - Los demás registros.
  - El script de entidades: va detrás de los registros y solo se desplaza.
  - El script de zona y el resto de partes.
- **Verificación.** Se relee la zona y se comprueba byte a byte antes de guardar. El resto de zonas del GARC debe quedar idéntico.

### Lista de modelos del área

Cada área tiene la lista de modelos de personaje que carga el juego:

- **Dónde está.** X / Y: `a/1/4/9`; Rubí Omega / Zafiro Alfa: `a/1/3/7`. Hay un miembro por área; el área es el u16 `0x02` de la cabecera de la zona.
- **Formato.** Entradas de 24 bytes, con el modelo en el primer u16.
- **Comprobado en los datos.** En todas las zonas con NPC de X (339) y de Zafiro Alfa (453), los modelos de sus NPC están en la lista de su área.
- **Consecuencia.** Si el área no carga el modelo de la enfermera, el juego se congela al entrar en la zona. Ocurrió en la primera prueba jugable: una enfermera en la Ruta 2 de X sin esta entrada.
- **Qué hace ahora la función.**
  - Si falta, añade al final de la lista del área la entrada del modelo de la enfermera.
  - La entrada se copia de las listas del propio juego, que tienen la misma en todos los Centros. Si no fuera única, la función no se ofrece.
  - La lista es del área: el modelo se carga en todas las zonas que la comparten, y el aviso dice cuántas son.
  - No se supera la lista más larga del juego (X: 28; Zafiro Alfa: 31).
  - La zona y la lista se guardan en la misma entrada del historial y se deshacen juntas.
- **Precedente.** «Enable Catch Zones» hace lo mismo al añadir su enfermera en la zona 338 de X: inserta el NPC y la entrada del modelo 59 en la lista del área 46.
- **CRO.** No se usa CRO Expander ni se modifica ningún CRO: la función solo cambia datos.

## Casillas válidas

Se rechaza la casilla:

- si está fuera de la matriz o en una celda sin bloque;
- si la ocupa otro NPC, un objeto, un warp o un activador;
- en zonas cuyas posiciones no son casillas (algunas de Ciudad Luminalia);
- si la zona superaría el mayor número de NPC que ya tiene alguna zona del juego (X: 29; Zafiro Alfa: 27);
- en orientaciones distintas de las cuatro verificadas.

El valor de permiso de la casilla se muestra junto con cuántos NPC de la zona están sobre casillas con el mismo valor. Es solo una referencia: el significado de esos valores no está verificado y no se usa para bloquear.

## Pruebas

- **`--map3ds-nurse-only`** (19 comprobaciones sintéticas):
  - plantilla tomada del juego, y nada si no hay enfermera o no coinciden;
  - inserción byte a byte y rechazos;
  - validación de casillas y relectura;
  - lista de modelos: añadir sin tocar las entradas existentes, entrada única, área que ya carga el modelo, lista ilegible o llena.
- **`--map3ds-nurse-ui-only`** (7 comprobaciones fuera de pantalla):
  - botón solo con proyecto, panel y elección de casilla con clic;
  - casilla ocupada, vista previa sin escritura y dirección;
  - guardado por el proyecto y selección de la enfermera nueva.
- **`--map3ds-add-nurse-probe`** en una copia real de X, zona 259 (Ruta 2), casilla (462, 458), mirando abajo. 8/8:
  - la vista previa no escribe;
  - al reabrir aparece la enfermera (script 2100, modelo 59);
  - los demás registros, scripts y partes no cambian, ni las demás zonas;
  - la lista del área 2 pasa de 13 a 14 modelos y las demás listas quedan idénticas;
  - queda entrada de historial;
  - deshacer devuelve el SHA-256 original de los dos archivos;
  - los cambios externos exigen recargar.
- **`--map3ds-add-nurse-rebuild`:** la imagen 3DS reconstruida contiene byte a byte el archivo de zonas y la lista de modelos editados.
- **Zafiro Alfa:** comprobación de solo lectura en una copia real. Encuentra la enfermera (zona 228, modelo 298, 16 enfermeras) sin escribir nada.

## Limitaciones

- **Aprobada en el juego solo en X**, dentro de la Ruta 2 (fuera de un Centro Pokémon). La primera versión, sin la lista de modelos, se congelaba al entrar en la ruta.
  - En X la secuencia del Centro funcionó fuera de un Centro.
  - Su script coloca la cámara con valores pensados para los Centros; en otros juegos y lugares no se ha comprobado.
- **Rubí Omega / Zafiro Alfa:** el mecanismo está acreditado de forma estática, pero no se ha escrito ninguna copia real.
- **Gen7 (Ultra Sol / Ultra Luna):** usa otro formato de eventos y de scripts. Necesita su propia investigación.
- **Gráficos:** la vista previa es un marcador esquemático, no el modelo 3D de la enfermera.
