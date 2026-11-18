# Editor de mapas 3DS — Gen7 (Ultra Sol/Ultra Luna)

Ultra Sol/Ultra Luna se abren en el mismo editor de mapas que Gen6 (`Map3dsEditorView`), con un adaptador propio **de solo lectura**. Cada entrada de la lista es un **área** de campo con su nombre de ubicación; se dibuja con sus modelos y texturas reales vistos desde arriba (la capa «Modelos/texturas» se activa por defecto) y con sus **eventos**: NPC, entrenadores, warps, interacciones, objetos recogibles y activadores.

## Formatos verificados (Ultra Luna)

| Recurso | Ubicación | Contenido |
| --- | --- | --- |
| Datos de campo por área | `encdata` a/0/8/3 (Ultra Luna; a/0/8/2 en Ultra Sol) | 11 archivos por área (336 áreas). Slot 0 `ED` eventos; slot 1 `BG` (parte 0 texturas del área, 1 shaders, 2 vegetación, **4 paisaje del área: suelo exterior, mar y rocas**); slot 2 matriz `TR`; slot 4 `AS` objetos estáticos |
| Matriz `TR` | slot 2 | u32 ancho, u32 alto, u32 tamaño de bloque (4200), dos u32 y registros de 12 bytes; el primer u16 es el identificador del bloque (0xFFFF = vacío). El bloque (c, f) ocupa **[c·4200, (c+1)·4200)** en X y Z |
| Bloques | a/0/8/6 | paquete `BG` por identificador; las partes que empiezan por 0x15122117 son modelos GFL2 en coordenadas del mundo |
| Objetos estáticos | slot 4 `AS` | un paquete `SM` por objeto: parte 0 empieza por el u16 del identificador; parte 1 es un paquete de modelos GFL2 (magia 0x10000) |
| Nombres de área | a/0/7/7 y a/0/9/1 | zonas de 84 bytes (nombre de ubicación en +0x1C) → mundo (u16 por zona) → cabecera `WD` (pares zona/área entre los u32 de +8 y +12) |
| Eventos | slot 0 `ED` | cada parte es un paquete con un archivo por zona del área: u32 recuento, registros de tamaño fijo cuyo primer u32 es el tipo, posición float3 en +4 (X, altura, Z) e identificador de evento en +0x2C |

Tipos de evento (parte de `ED`, tamaño, tipo): activadores (0, 60, 0), NPC (1, 120, 1; modelo en +0x34, script en +0x38), warps (2, 88, 3), interacciones (3, 60, 2), objetos estáticos (4, 56, 4; rotación cuaternión en +0x10, modelo u16 en +0x30), entrenadores (7, 84, 7), objetos recogibles (10, 64, 10). La tabla se contrastó con USUM-Studio (Aqua-0, GPL-3.0) y se verificó con los datos: en las 336 áreas de Ultra Luna, **ningún registro tiene un tipo distinto del esperado** (5253 eventos).

Implementación propia (sin copiar código):

- `pk3DS.Core/Maps/Ctr/Gfl2File.cs`: modelos, texturas y paquetes de modelos GFL2 (formato documentado por SPICA, gdkchan, dominio público). La transformación del modelo de campo no se aplica: los vértices ya están en coordenadas del mundo.
- `pk3DS.Core/Maps/Gen7/Gen7MapBackend.cs` y `Gen7Events.cs`: áreas, matriz, modelos de bloque, objetos estáticos colocados (cuaternión + traslación), nombres y eventos. Un tipo de evento con registros inesperados no se muestra.
- El rasterizador cenital de Gen6 se reutiliza con un rectángulo del mundo arbitrario y mezcla el alfa parcial.

## Pruebas

| Nivel | Opción | Resultado |
| --- | --- | --- |
| Sintética | `--map3ds-gen7-only` | 16 comprobaciones (matriz, texturas, adaptador, registros de eventos, tipos inesperados, tablas truncadas) |
| Copia real, solo lectura | `--map3ds-gen7-render-probe` | 336 áreas; 2600 modelos (bloques, objetos estáticos y paisaje del área) y 12 926 texturas sin errores; 5253 eventos sin tipos inesperados; 29 fuera de la matriz (sobre todo áreas del Ultraespacio) |
| Sintética (colisión y capa de diagnóstico) | `--map3ds-gen7-only`, `--map3ds-render-ui-only` | lectura de colisión, rechazo de archivos ajenos y mallas truncadas; capa desactivada por defecto, dibujada y explicada |
| Editor real | `--map3ds-gen7-ui-probe` | Iki (área 1): 12 bloques y 62 eventos alineados con sus estructuras; con `diagnostico`, también la colisión del suelo |
| Juego | — | no aplica (solo lectura) |

## Limitaciones

- **Suelo exterior (resuelto)**: el suelo de las áreas exteriores grandes (Route 1, Iki, Hau'oli…) no está en los bloques de a/0/8/6, sino en la **parte 4 del `BG` del área** (un paquete con un modelo por entrada; mallas como `ground_01_01_*`, además del mar y las rocas lejanas). Se localizó comparando con USUM-Studio (binario oficial de su CI, ejecutado sobre una copia aislada de Ultra Luna), cuyo inspector la muestra como «Distant scenery». Con ella, Route 1 pasa de 5,6 % a 34,1 % de cobertura visual y lo que solo tenía colisión baja de 23,2 % a 1,9 % (Hau'oli: 0,2 %). Cada área trae el paisaje de su propia zona; lo que queda fuera pertenece a las áreas vecinas.
- **Mar**: sus materiales combinan texturas de brillo y oleaje que la vista aproximada no reproduce; se ve como franjas de color.
- **Capa «Colisión del suelo (diagnóstico)»**, desactivada por defecto: dibuja en colores planos (uno por atributo de superficie) la colisión transitable de la parte 6 de cada bloque (u32 0x14120500, triángulos de 72 bytes con vértices en +0/+16/+32 y atributo en +64). La leyenda indica que no son los gráficos del suelo. Comparación en Ultra Luna (`--map3ds-gen7-ground-probe`): Route 1 visual 5,6 % frente a colisión 27,8 % (23,2 % solo colisión); Iki 2,6 % frente a 11,5 %; Hau'oli 2,1 % frente a 6,7 %. La colisión cubre exactamente los caminos, plazas y zonas transitables que faltan; las matrices no tienen variantes (un archivo `TR`, u32 en +16 = 0).
- Sin edición: ningún campo de evento tiene escritura verificada todavía.
- Destinos de warps, scripts y formas de activación no se interpretan aún.
- Sol/Luna no está habilitado.
