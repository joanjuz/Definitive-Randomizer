# Caramelos Raros personalizados — Ultra Sol y Ultra Luna

Misma opción que en [Rubí Omega](CARAMELOS_RAROS_PERSONALIZADOS_OR.md), con un mecanismo propio de Gen7. Está desactivada por defecto y aparece en proyectos de Ultra Sol (`00040000001B5000`) y Ultra Luna (`00040000001B5100`).

## Comportamiento en el juego

- Bolsa → Caramelo Raro → Usar → Pokémon. Con más de un Caramelo Raro aparece la ventana de cantidad: es la misma que usa la tienda para vender. Con uno solo, el uso es el original.
- Cada caramelo sigue el proceso normal del juego: ventana de nivel, movimientos nuevos (se pueden rechazar), consumo de una unidad y comprobación de evolución.
- Se detiene al evolucionar, al llegar al nivel 100, si el nivel no sube (por ejemplo por un tope de nivel) o si no quedan caramelos. Los restantes no se gastan. Cancelar la ventana no gasta nada.

**Validación:** el propietario probó el 25-11-2026, en Ultra Sol y en Ultra Luna, un prototipo con exactamente estas instrucciones y confirmó que funciona. El instalador añade a ese prototipo solo un marcador de 16 bytes, que no es código, detrás de la rutina; la sonda real lo comprueba byte a byte.

## Cómo se instala

Editor «Caramelos Raros personalizados · Ultra Sol / Ultra Luna» y tarjeta en «Mecánicas y CRO», con el mismo flujo que en OR:

- `Enable = 1` instala y `Enable = 0` retira.
- La vista previa no escribe nada y se registra una sola entrada de historial.
- Deshacer restaura los bytes exactos, y queda una operación reproducible en Global Template.

Solo se escriben `Bag.cro` y `.crr/static.crr`. `FieldBag.cro` y la cabecera NCCH solo se leen. `code.bin` no se toca.

- `Bag.cro` es el mismo archivo en los dos juegos, y sus llamadas al juego van por nombre de símbolo (las resuelve `static.crs` al cargar). Por eso una única rutina sirve para Ultra Sol y Ultra Luna.
- La rutina se añade ampliando el segmento de código de `Bag.cro` con `CroPatchSession`, como las mentas.
- Al retirarla se restauran las tres instrucciones originales y el bloque queda desactivado. Reinstalar lo reutiliza, así que el archivo no crece más.

## Puntos y dependencias

| Punto de `Bag.cro` | Original | Función |
|---|---|---|
| `0x9C68` | `bl 0xC2F0` | Usar un objeto sobre un Pokémon: abre la ventana si es un Caramelo Raro y hay más de uno |
| `0x6144` | `ldr r0, [r4, #4]` | `switch` de estados de la Bolsa: estado propio `0x19` para la ventana y para encadenar el siguiente uso |
| `0xE9A0` | `str r7, [r4, #4]` | Fin de la subida de nivel: decide si sigue con otro caramelo |

El estado vive en un bloque de memoria propio, junto a la ventana de cantidad, que la Bolsa destruye al cerrarse en cualquier modo. No persiste entre sesiones.

El reconocimiento es por anclas, no por el hash del archivo:

- ventanas alrededor de los tres puntos;
- comienzo de cada función de `Bag.cro` que usa;
- símbolo de cada stub con nombre que llama;
- destino en `FieldBag.cro` de cada stub anónimo;
- huella de las funciones de la ventana de cantidad en `FieldBag.cro`.

Estados posibles: original, instalado, parcial, conflicto (otra modificación en un punto o bloque alterado) y no acreditado (otro juego, otra revisión o anclas cambiadas).

## Convivencia

- **Mentas Gen7:** conviven en cualquier orden ([convivencia en `Bag.cro`](MENTAS_GEN7_USUM_CONVIVENCIA_BAG.md)). Ninguna de las dos escribe en lo que necesita la otra (comprobación en `--rare-candy-only`). Retirar los caramelos mantiene las mentas reconocidas.
- **Player Level Caps:** actúa en `code.bin`/`Battle.cro`. Si el nivel no sube por el tope, la secuencia se detiene. Esta combinación no se ha probado en el juego.
- Mega Ability Sync y Persistent Items escriben `Battle.cro`; no hay solapamiento.

## Pruebas

- `--rare-candy-only` (CI): incluye 16 comprobaciones USUM:
  - la rutina conserva sus destinos en cualquier posición;
  - no se pisa con las mentas;
  - rechazos por título y por archivos desconocidos;
  - el editor no escribe nada al rechazar.
- `native/3ds/rare-candy-usum/generate.py --check` (CI): la plantilla C# coincide con `candy_usum.s`.
- `--rare-candy-usum-real <RomFS US> <RomFS UM> [<Bag.cro del prototipo>]` (copias reales, fuera del CI), 40 comprobaciones:
  - instalación, igualdad con el prototipo validado, doble instalación, retirada, reinstalación sin crecer;
  - instalación parcial, conflicto, ventana de cantidad cambiada, CRR sin entrada;
  - mentas en los dos órdenes;
  - editor con historial, deshacer exacto y reproducción desde Global Template.
- Sin pendientes de juego para el prototipo. La versión instalada desde el Randomizer solo añade el marcador; se puede confirmar con una ROM generada desde la aplicación.
