# Editor de mapas 3DS: «Añadir enfermera» (Gen6 y Ultra Sol / Ultra Luna)

**Estado:**
- Implementado para Pokémon X / Y y Rubí Omega / Zafiro Alfa.
- Escritura comprobada en una copia real de X.
- **Prueba en el juego aprobada por el propietario en Pokémon X** (Ruta 2, imagen v2): la enfermera aparece, se puede hablar con ella, cura al equipo y el juego no se congela. La primera versión, sin la entrada del modelo en la lista del área, se congelaba al entrar en la ruta.
- Sin prueba en el juego: Y, Rubí Omega y Zafiro Alfa.
- **Ultra Sol / Ultra Luna:** implementado, con su propio mecanismo (ver «Ultra Sol / Ultra Luna» más abajo). Escritura comprobada en copias reales de los dos juegos.
  - **Primera prueba jugable** en Ultra Luna (Pueblo Lilii): la enfermera aparecía y hablaba, pero el juego se congelaba al aceptar curar. Faltaba la máquina de curación.
  - **Segunda versión**, con la máquina: pendiente de prueba.
- Sol / Luna y los juegos de DS no tienen esta función.

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

## Ultra Sol / Ultra Luna

Investigado con los datos de los dos juegos, sin trasladar nada de Gen6.

### Cómo funciona en el juego

- **Las enfermeras.** Hay 15, una por Centro Pokémon, y son el mismo NPC: registro de 120 bytes de la parte 1 del paquete de eventos (ED), con **modelo 66** (`+0x34`) y **script 5030** (`+0x38`).
  - Solo cambian el número de evento (`+0x2C`) y la posición de sus formas. Todos los Centros tienen la misma planta.
  - En 3 Centros, la zona de conversación está desplazada 2,3 unidades; se tolera.
- **Formas.** Dos listas de formas:
  - la zona de conversación, un cilindro de radio 80, 130 unidades delante;
  - el cuerpo, un cilindro de radio 50 en su posición.
- **El script de curación es común.**
  - El GARC `a/0/9/2` guarda scripts comunes ordenados por rangos, cada uno con su tabla de casos. El miembro 3 atiende los casos −1 y 5030–5035, y su caso 5030 es la rutina de la enfermera.
  - El juego despacha estos números desde cualquier área: hay NPC con números de rangos comunes también fuera de los Centros, incluso en exteriores.
  - El script de zona («ZS», slot 7) no la incluye, así que no se toca.
- **Máquina de curación.** La rama de curación de la rutina busca en el área el **objeto estático 2**, la máquina del mostrador (llamada al motor con la constante 2), y anima las Poké Balls sobre ella.
  - Las 21 áreas donde el juego cura con esa escena la tienen: los 15 Centros, el Árbol de Combate, el Paraíso Æther (2), el Estadio Royale, Ciudad Hauoli y Ciudad Konikoni.
  - Su modelo «SM» del paquete de objetos («AS», slot 4) es idéntico en todas.
  - Se identifica por el objeto y no por el id de evento (701 o 702, según el área).
  - Faltaba en la primera prueba jugable (v1).
- **Módulo de campo del Centro.** El byte `+0x0E` del registro de 84 bytes de cada zona (`zonedata`, miembro 0) elige el módulo de campo («gimmick») que el juego carga en la zona:
  - 0: ninguno (305 zonas de Ultra Luna);
  - **3: Centro Pokémon**, solo en los 15 Centros;
  - 5: ascensores (Monte Lanakila, Paraíso Æther);
  - 6: Árbol de Combate;
  - 9: Festival Plaza, y otros en zonas sueltas.
  - El módulo 3 es `FieldGimmickPokecen.cro`. Contiene `monsterball_loc_01…06`, las texturas `com_bm_pcounter_poke01…06` de la máquina y la lectura del equipo: es quien coloca las Poké Balls y pone los iconos en la máquina.
  - La rama de curación de 5030 llama a funciones del motor que no usa ningún otro script común; son las de ese módulo.
  - **Sin él, el juego se congela nada más decir «Sí»**, aunque la máquina esté en el área (v2).
  - El Árbol de Combate cura con otra rutina (caso 5660), sin esa animación.
- **Carga del modelo.** El slot 3 de cada área es un paquete «AC» con **un paquete «CP» por modelo de personaje**, nombrado con el número del modelo.
  - En las 300 áreas con NPC o entrenadores de cada juego, el «AC» contiene los modelos de todos ellos.
  - El CP «66» es idéntico en los 15 Centros y en los dos juegos.
  - Es el equivalente de la lista de modelos de Gen6: una enfermera en un área que no carga su modelo necesita su CP.
- **Archivo de NPC.** Formato: número de NPC, registros de 120 bytes y las formas de cada NPC, en orden y contiguas (307/307 archivos).
- **Ids de evento.** Únicos en la zona y sin coincidir con otros tipos de evento (307/307).
- **Paquete «AC».** Datos desde la cabecera redondeada a 0x80 (0x80 hasta 30 modelos y 0x100 a partir de 31; 311/311 áreas) y partes de tamaño múltiplo de 0x80.

### Qué escribe

Hasta tres miembros del archivo de campo (`a/0/8/3` en Ultra Luna, `a/0/8/2` en Ultra Sol): eventos, personajes y objetos del área. Además, un byte de la tabla de zonas (`a/0/7/7`). Todo va en una sola entrada del historial.

1. **Paquete de eventos del área:** se añade la copia de la enfermera del juego al final del archivo de NPC, con:
   - la posición de la casilla, a la altura de su suelo;
   - el giro de la orientación elegida;
   - el siguiente número de evento libre;
   - sus dos formas al final, con la de conversación girada con ella.

   Los demás NPC solo desplazan 120 bytes la posición de sus formas. Antes de escribir se comprueba la estructura y después se relee todo.
2. **Máquina de curación**, si el área no tiene una: copia del estático de la máquina del Centro, como allí respecto a la enfermera (girada con ella) pero apoyada en el suelo, con el id 701 si está libre. Su «SM» se añade al paquete de objetos si falta, con datos desde la cabecera redondeada a 0x80 (336/336 áreas), sin pasar de 25 objetos ni de 1 070 592 bytes. Los estáticos existentes solo desplazan su forma 56 bytes.
3. **Modelos de personaje del área:** si el área no carga el modelo 66, se añade al final la copia exacta de su CP. El inicio de datos sigue la regla del juego. No se supera el área más cargada del juego (37 modelos, 7 344 640 bytes).

4. **Tabla de zonas:** si la zona del área no tiene módulo de campo (0), su byte `+0x0E` pasa a 3. No cambia ningún otro byte. Se rechaza si la zona ya usa otro módulo, porque una zona solo carga uno, y si el área tiene más de una zona.

Los miembros originales (incluido el de la tabla de zonas) se guardan juntos para restaurar el área exactamente, porque el archivo supera los puntos de restauración del historial, como en las zonas de captura.

**Casillas válidas:**
- suelo de un solo nivel en la casilla;
- suelo a la misma altura en la casilla desde la que se habla;
- sin NPC ni entrenadores en ninguna de las dos;
- sin warps (puertas) a menos de 1,5 casillas;
- áreas con una sola zona de NPC (309 de 336);
- la casilla de la máquina, si hay que añadirla: con suelo a la misma altura, libre y fuera del sitio desde el que se habla;
- **sin paredes ni vallas** entre la enfermera y la casilla desde la que se habla;
- **la casilla desde la que se habla, alcanzable a pie** desde la llegada de algún warp del área, pasando entre casillas vecinas con suelo sin cruzar paredes.
  - Las paredes son los triángulos verticales de la parte 7 de cada bloque: un paquete «BG» de mallas con el formato de colisión de la parte 6, vistas desde arriba como segmentos.
  - En la v2, la casilla 148, 118 de Pueblo Lilii quedaba detrás de una valla. Ahora se rechaza; la 148, 115, la que eligió el propietario, se acepta.
  - Auditoría con `--map3ds-gen7-reach-audit` en Ultra Luna: las 15 enfermeras de los Centros son alcanzables.
    - De todos los NPC con zona de conversación, lo son 1574 de 1866.
    - Los demás están detrás de mostradores, en escenas o en zonas a las que se llega por el borde.
    - La regla puede rechazar casillas válidas, pero no acepta las inaccesibles.

Si las enfermeras del juego no coincidieran, la función no se ofrecería.

### En el editor

Es el mismo panel «Añadir enfermera» de Gen6: casilla con un clic o escrita, dirección, vista previa, guardado por el historial y restauración desde el NPC. El texto de la vista previa indica:
- el número de evento;
- la casilla desde la que se le habla;
- si el área ya carga el modelo o si se añadirá, con el total de modelos y de bytes frente al máximo del juego.

La vista previa es un marcador con la dirección, no el modelo 3D.

### Pruebas

- **`--map3ds-gen7-nurse-only`** (16 sintéticas):
  - plantilla;
  - regla del inicio de datos;
  - plan;
  - registro movido y girado;
  - NPC y partes intactos;
  - modelo añadido o ya cargado;
  - rechazos: casilla ocupada, NPC delante, warp, sin suelo, sin suelo donde se habla, varias zonas, área llena;
  - enfermeras distintas no se copian;
  - una enfermera añadida se reconoce y se puede añadir otra;
  - la máquina, con su modelo;
  - el módulo del Centro en la zona (solo cambia ese byte), y el rechazo si la zona usa otro módulo;
  - el cruce de un paso con una valla.
- **`--map3ds-gen7-nurse-ui-only`** (5 de interfaz).
- **`--map3ds-gen7-nurse-probe`** en copias reales de **Ultra Luna limpia y Ultra Sol** (Pueblo Lilii, casilla 148, 115, mirando a la derecha), 10/10:
  - la vista previa no escribe;
  - hay exactamente un NPC más, la enfermera;
  - los demás NPC no cambian;
  - el área carga su modelo (30 → 31);
  - la máquina, estático 701, junto a ella, con su modelo en el «AS» (14 → 15);
  - solo cambian los miembros de eventos, personajes y objetos del área;
  - la zona 16 carga el módulo del Centro (gimmick 0 → 3, un solo byte; comprobado también con el lector independiente);
  - se registra en el historial;
  - la restauración es exacta;
  - un editor con datos antiguos no puede escribir.
  - Verificado también con un lector independiente: la enfermera nueva solo difiere de la del Centro en posición, giro e id, y el CP añadido es el del Centro byte a byte.
- **Imagen 3DS de Ultra Luna:**
  - v1, sin máquina: se congelaba al curar.
  - v2, con máquina y sin módulo: se congelaba nada más decir «Sí». Además, la enfermera quedaba detrás de una valla.
  - **v3**, con módulo del Centro y en la casilla 148, 115: juego pendiente.

## Limitaciones

- **Aprobada en el juego solo en X**, dentro de la Ruta 2 (fuera de un Centro Pokémon). La primera versión, sin la lista de modelos, se congelaba al entrar en la ruta.
  - En X la secuencia del Centro funcionó fuera de un Centro.
  - Su script coloca la cámara con valores pensados para los Centros; en otros juegos y lugares no se ha comprobado.
- **Rubí Omega / Zafiro Alfa:** el mecanismo está acreditado de forma estática, pero no se ha escrito ninguna copia real.
- **Gen7 (Ultra Sol / Ultra Luna):** sin prueba en el juego.
  - La rutina de curación lleva la cámara a una posición fija del Centro, (2449,7; 388,4; 2098,4) mirando hacia (1920,4; 152,7; 2100,9), y espera a que llegue. Fuera del Centro la cámara apuntará a otro sitio durante la animación.
  - La máquina va en el suelo, porque fuera no hay mostrador.
  - El módulo del Centro se carga en toda la zona y nunca se ha usado fuera de un Centro. Podría afectar a la memoria o a la carga de la zona.
  - No se añade en zonas que ya usan otro módulo (ascensores, Árbol de Combate, Festival Plaza…).
  - Solo áreas de una zona de NPC.
  - No se encontró en el código la tabla que asocia los números de script a los scripts comunes; el despacho global se apoya en los datos.
- **Gráficos:** la vista previa es un marcador esquemático, no el modelo 3D de la enfermera.
