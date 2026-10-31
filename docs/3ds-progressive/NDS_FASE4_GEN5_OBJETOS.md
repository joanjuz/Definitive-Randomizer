# NDS fase 4: objetos del mapa y tiendas Gen5 (Negro y Blanco ES) — 2026-10-30

Barajado de los objetos del mapa y de las tiendas especiales. Código en `pk3DS.Core/NDS/Gen5Data.cs` y `Gen5Randomizer.cs`.

## Qué se baraja

- **Objetos del mapa**: los 306 objetos visibles (script 864, instrucción `0x28 0x800C <objeto>`) y los 177 ocultos (script 865, `0x2A 0x8000 <objeto>`), leídos como UPR-ZX.
- **Tiendas**: las 26 listas de la tabla del overlay 21 (posiciones propias de Negro y de Blanco ES). No se tocan las tiendas de MT (1, 2, 4, 7) ni las de progreso del Centro Pokémon (0, 16, 20, 23, 24, 25), igual que UPR-ZX.
- **Solo se baraja**: el conjunto de objetos del juego no cambia, así que no aparecen objetos inválidos. **Las MT, MO y objetos clave no se mueven**, ni los identificadores desconocidos. Sus números se obtuvieron de los nombres de objeto del propio Negro (USA): MT 328–419 y 618–620, MO 420–425, objetos clave 428–503, 505–536, 574, 578, 579, 616, 617 y desde 621, y los no usados 120–133.

## Decisiones técnicas

- **Una sola copia de trabajo por tabla** en `Generate`: iniciales y objetos modifican el mismo archivo de scripts, y antes cada paso partía de su propia copia (el segundo habría borrado al primero). La prueba real combina ambos.
- **Overlay 21 recomprimido**: descomprimido ocupa 354 KB y no cabe sin comprimir en el espacio libre (≈296 KB en Negro, ≈140 KB en Blanco ES). Se añadió `BLZCoder.Compress`, un envoltorio público del codificador existente (sin cambiar el algoritmo de 3DS). Al guardar se descomprime de nuevo y se exige el mismo contenido; la tabla conserva la bandera de compresión. Recomprimir el original da exactamente su tamaño (260.628 y 260.636 bytes) y tras barajar sigue cabiendo en su hueco. El overlay 223 de los iniciales sigue guardándose sin comprimir, como se probó en juego.

## Validación

- `--nds-only` (sintético, CI): 37, incluida la clasificación de objetos que no se mueven.
- `--nds-gen5-items-probe` sobre Negro y Blanco ES (objetos, tiendas e iniciales a la vez): 20/20. 306 + 177 objetos y 26 tiendas; mismas posiciones y el mismo conjunto de objetos; MT en su sitio y 439 objetos cambiados; tiendas fijas idénticas; overlay 21 comprimido en su hueco; iniciales y objetos conviven en los scripts; resto de archivos idénticos; misma semilla, mismo resultado; originales sin cambios.
- Ventana DS: «Objetos del mapa» y «Tiendas» (sin cambios o barajar); `--nds-ui-only` 7 y `--nds-ui-probe` 9 con Blanco ES.
- ROM de prueba para el juego: `D:\Nintendo\DS\Juegos\Pruebas\Blanco-ES-objetos-tiendas-prueba.nds` (solo objetos y tiendas, 575 cambios).

## Pendiente

Prueba en juego: recoger objetos visibles y ocultos y comprar en una tienda especial (por ejemplo, la de hierbas o la de vitaminas).
