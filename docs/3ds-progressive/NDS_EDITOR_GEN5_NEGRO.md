# Editor DS Gen5: Pokémon Negro (IRBO/0)

El editor de mapas y eventos de Nintendo DS se abre con **Pokémon Negro USA/EUR, revisión 0 (IRBO/0)**. Es el primer juego de la quinta generación. La interfaz es la misma que en Platino, Diamante y HeartGold. Los datos de Gen5 se leen con un **lector propio**: no se reutiliza ningún desplazamiento ni formato binario de Gen4.

No se añaden Blanco, Negro 2 ni Blanco 2: tendrán su propio perfil e incremento. Un juego o una revisión sin perfil muestra el aviso habitual y no abre nada.

## Arquitectura: contratos compartidos y lectores por generación

El editor (sesión, checkpoints, proyecto, exportación, lienzo y vista) ya no depende de las clases de Gen4. Trabaja con tres contratos del núcleo:

- **`INdsEventProfile`.** Identifica el juego y sus límites: nombre, código y revisión; rutas de eventos y scripts; si admite NPC nuevos; dirección máxima de interacción y si se puede cambiar; alcance máximo de visión y por qué no se puede editar.
- **`INdsEventData`.** Mapas, eventos, matriz y las ediciones comunes: mover y girar NPC y editar objetos de fondo, warps y activadores. También destinos de warp, consulta de scripts, bytes y `WithEvents`. Cada implementación aplica sus propias reglas y huellas.
- **`INdsMapGraphics`.** Lo que necesita el render cenital: escena de cada bloque, modelo de terreno y elección de cabecera por texturas.

Gen4 (`Gen4EventProfile`, `PlatinumEvents`, `PlatinumMapAssets`) cumple estos contratos sin cambiar su comportamiento. Gen5 los implementa con `Gen5MapProfile`, `Gen5ZoneEvents` y `Gen5MapGraphics`.

Los registros de presentación (`PlatinumMap`, `PlatinumEvent`, `PlatinumMapLayout`, `PlatinumTerrain`) son comunes. Un lector de Gen5 los rellena con sus propios campos. Dos añadidos:

- `PlatinumEvent.EditLock` marca, con su motivo, un registro que se muestra pero no se escribe. En Gen4 no se usa nunca.
- `PlatinumTerrain.FromAttributes` construye el terreno de presentación a partir de otro formato. Los bloques sin datos quedan como desconocidos, nunca como libres.

Añadir, duplicar o eliminar NPC, la enfermera y los scripts añadidos siguen siendo exclusivos de Platino. La sesión los rechaza en cualquier otro juego, venga la petición de la interfaz, de un checkpoint o de un proyecto.

## Formatos medidos en Negro

No hay decompilación pública de la capa de mapas de Negro, Blanco, Negro 2 o Blanco 2. Todo lo siguiente se midió en el volcado del propietario (SHA-1 `26ad0b99…`) y se contrastó con Blanco (IRAS/0) y Blanco 2 (IRDO/0).

| Dato | Negro IRBO/0 |
|---|---|
| Cabeceras de mapa | `a/0/1/2`, un único miembro: 427 × 48 bytes. +2 conjunto de texturas (`a/0/1/4`), +4 matriz, +6 scripts, +22 eventos, +26 nombre de lugar (u8, texto 89) |
| Matrices | `a/0/0/9` (255): u32 con cabeceras (0/1), u16 ancho, u16 alto, celdas u32 (bloque; `0xFFFFFFFF` vacía) y, si las hay, cabeceras u32. Lado máximo: 29 (exterior de 29 × 27) |
| Bloques de mapa | `a/0/0/8` (649): contenedor con u16 tipo, u16 número de secciones, desplazamientos u32 y fin. WB (572: modelo, permisos, edificios), GC (47: segunda capa de permisos), NG (22: sin permisos), RD (8: permisos de raíl) |
| Eventos | `a/1/2/5` (428): u32 tamaño, cuatro contadores u8 y registros de 20 (fondo), 36 (NPC), 20 (warp) y 22 (activador) bytes, más una cola variable que se conserva byte a byte |
| Scripts | `a/0/5/7` (899): fijado por CRC32; no se interpreta ni se escribe |

**Por qué `+22` son los eventos.** Es el único campo con el que todos los warps llevan a un warp existente: 880 de 886. Los otros 6 tienen un destino que no es una cabecera y se marcan como especiales.

**Campos de los registros.**

| Registro | Campos |
|---|---|
| NPC (36 bytes) | +0 id, +2 sprite, +4 movimiento, +6 tipo, +8 flag, +10 script, +12 dirección 0–3, +14 visión, +20/+22 rango de paseo, +24 tipo de posición (0 casilla, 1 raíl), +28 X, +30 Y, +32 altura |
| Fondo (20 bytes) | +0 script, +2 tipo (2 = objeto oculto), +4 dirección de interacción, +6 tipo de posición, +8 X, +12 Y |
| Warp (20 bytes) | +0 cabecera de destino, +2 warp de destino, +6 tipo de posición, +8/+12 posición en dieciseisavos de casilla (`16·x + 8`, el centro: lo cumplen los 809 warps de casilla), +14/+16 extensión |
| Activador (22 bytes) | +0 script, +2 valor, +4 variable, +8 tipo de posición, +10 X, +12 Y, +14 ancho, +16 alto |

**Objetos.** Las 276 Poké Ball son NPC con un script de 7000 a 7999 (265 con el sprite 110). Los 184 objetos ocultos son eventos de fondo de tipo 2 con script 8000+.

**Entrenadores.** Usan los scripts 3000+ y 5000+, con tipos 1 y 2–20. De los NPC «mirar fijo», 258 de 274 tienen la dirección que corresponde a su movimiento (14 arriba, 15 abajo, 16 izquierda, 17 derecha). La regla se acredita por coherencia con los datos y queda pendiente de prueba jugable.

**Colisión (inferida).** Cada casilla tiene 8 bytes de permisos. El bit 0 del byte 6 está en el 78 % de todas las casillas, pero solo en el 5 % de aquellas donde hay un NPC de casilla. Las puertas, donde están los warps, lo tienen en el 66 %. Ningún otro dato del permiso se interpreta. Un bloque de Negro tiene permisos de 64 × 64 y queda como desconocido.

**Texturas del terreno.** Con el conjunto que nombra la cabecera (+2) enlazan 6.911 de los 6.933 materiales (99,7 %), y los 649 modelos se leen sin fallos.

**Blanco y Blanco 2.**
- Blanco (IRAS/0) tiene exactamente las mismas cabeceras, matrices, bloques, texturas y eventos que Negro (CRC32 idénticos); solo cambian los scripts.
- Blanco 2 (IRDO/0) tiene la misma estructura con rutas y tamaños propios: 615 cabeceras, eventos en `a/1/2/6` (616), scripts en `a/0/5/6`, nombres en el texto 109. En él, `+22` resuelve 1.084 warps con 5 especiales.
- Ninguno de los dos tiene perfil en esta entrega.

## Qué está disponible en Negro

| Función | Negro IRBO/0 |
|---|---|
| Lista de mapas con su nombre de lugar; matriz, terreno (colisión inferida) y modelo de terreno con texturas | sí |
| Estructuras (edificios, árboles y otros modelos de cada bloque) | se dibujan en la capa gráfica (solo lectura), con selector de estación; ver [NDS_GEN5_EDIFICIOS_ESTACIONES.md](NDS_GEN5_EDIFICIOS_ESTACIONES.md) |
| Mover y girar NPC de casilla | sí |
| Orientación de entrenadores (con «mirar fijo») | sí |
| Alcance de visión | se muestra el valor, pero ni se dibuja ni se edita |
| Mover objetos de fondo | sí; la dirección de interacción se muestra sin interpretar y no se cambia |
| Mover warps y cambiar su destino a otro warp existente | sí (no los especiales) |
| Mover activadores y cambiar su tamaño (1–32) | sí |
| Poké Ball del suelo | se listan con los objetos; no se mueven ni se giran |
| Registros de raíl (152 en Negro) | se muestran con su motivo; no se editan |
| NPC nuevos, enfermera, scripts | **no** |
| Checkpoints `.ndsevents.json` (con `IRBO`/`0`), proyecto persistente, exportación, restauración exacta | sí |

La huella de eventos (`EditableEventsSha256`) borra solo los campos escribibles de los registros de casilla. Una edición que cambie cualquier otro byte (scripts, flags, visión, dirección de interacción, alturas, extensión de los warps, raíles o la cola) se rechaza. Una ROM reconstruida con ediciones aceptadas se vuelve a abrir por esa huella.

## Comprobaciones

- `--nds-gen5-black-only`: 34 comprobaciones sintéticas. `--nds-gen5-black-ui-only`: 6 comprobaciones fuera de pantalla. Las dos están en el CI.
- `--nds-gen5-black-probe` sobre una copia real:
  - censo: 427 mapas, 2.257 NPC (276 Poké Ball), 886 warps (880 resuelven, 6 especiales) y 156 registros bloqueados;
  - terreno y gráficos se abren;
  - cuatro ediciones jugables y un activador ampliado y restaurado;
  - se rechaza mover una Poké Ball y crear NPC;
  - guardar, reabrir, exportar y reabrir la ROM exportada, con el ARM9 y el resto de archivos idénticos byte a byte;
  - la restauración tiene el mismo SHA-256 que la fuente.
- `--nds-gen5-black-view-probe`: el editor real con la copia, la capa gráfica y un entrenador girado en vista previa.
- `--nds-map-render-gallery-probe` con Negro: 10 mapas (Pueblo Arcilla y sus interiores, Ruta 1, Pueblo Terracota y su Centro Pokémon, Ciudad Gres, Ciudad Esmalte y Ciudad Porcelana), 0 triángulos sin textura. Los warps caen en las puertas y los NPC del Centro Pokémon, tras el mostrador.
- Regresión de Gen4: la lista completa del CI en local, más las sondas reales de Platino, Diamante y HeartGold.

**Validación jugable pendiente.** Ninguna de estas comprobaciones sustituye una prueba en el juego.

## Limitaciones

- **Estructuras.** Resuelto en [NDS_GEN5_EDIFICIOS_ESTACIONES.md](NDS_GEN5_EDIFICIOS_ESTACIONES.md): el id es big-endian, el área de la cabecera elige el paquete `AB` y la Z almacenada está invertida. Los edificios se dibujan en solo lectura; no se editan.
- **Estaciones.** La cabecera nombra el área de primavera; el juego suma la estación (ver [NDS_GEN5_EDIFICIOS_ESTACIONES.md](NDS_GEN5_EDIFICIOS_ESTACIONES.md)). El editor ofrece un selector de estación solo para la vista previa.
- **Visión de entrenadores.** El valor está en +14, pero su comportamiento en Gen5 no se comprobó en el juego. No se dibuja ni se edita.
- **Dirección de interacción de los objetos de fondo.** Los valores 0–6 no tienen un significado acreditado en Gen5. No se cambia.
- **Raíles.** Los registros con posición de raíl usan otro sistema de coordenadas y no se editan.
- **Altura.** Al mover un NPC o un objeto se conserva su altura; en mapas con desniveles, la casilla nueva debe estar a la misma altura.
- **Scripts.** Solo se muestra su número.
