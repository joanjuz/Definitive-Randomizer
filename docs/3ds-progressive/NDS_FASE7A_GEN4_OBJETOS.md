# NDS fase 7a: ARM9, objetos del mapa y tiendas de Gen4 — 2026-10-30

Barajado de objetos del mapa y tiendas en Diamante, Platino y HeartGold. En Gen4 los objetos ocultos y las tiendas están en el **ARM9** (el ejecutable principal), así que esta fase añade su lectura y escritura al contenedor NDS.

## ARM9 (`pk3DS.Core/NDS/NdsRom.cs`)

- `ReadArm9()` devuelve el ARM9 tal como se ejecuta. Si los parámetros del módulo (señalados por el pie `0xDEC00621`) tienen el campo «final comprimido», está comprimido con BLZ y se descomprime: es el caso de HeartGold; Diamante y Platino no lo están.
- `Save(..., arm9)` escribe un ARM9 nuevo **en su sitio**: mismo tamaño descomprimido, guardado igual que el original (comprimido o no), con el pie justo después y el resto del hueco anterior a cero. Si va comprimido:
  - se recomprime con el modo ARM9 del codificador (los primeros 0x4000 bytes sin comprimir) y la búsqueda óptima;
  - se exige que se descomprima exactamente igual;
  - se actualiza el «final comprimido» de los parámetros del módulo.

  Con la búsqueda normal, HeartGold ocupaba 120 bytes más que el original; con la óptima cabe.
- Se rechaza en juegos DSi-enhanced (Negro, Blanco, Blanco 2): su ARM9 está protegido por las firmas DSi. Gen5 no lo necesita.
- `BLZCoder.CompressArm9`: nuevo envoltorio del codificador existente, sin cambiar el algoritmo.

## Objetos y tiendas (`Gen4Data.cs`, `Gen4Randomizer.cs`)

- **Objetos visibles**: lista de scripts de objetos (370 en Diamante, 404 en Platino, 141 en HeartGold) con `setvar 0x8008, objeto`, saltando las entradas que UPR-ZX marca como no válidas. 253, 322 y 254 objetos.
- **Objetos ocultos**: tabla del ARM9 de entradas de 8 bytes. 229, 257 y 231 objetos.
- **Tiendas**: listas del ARM9 que empiezan tras un prefijo único y acaban en `0xFFFF`. 28, 29 y 40 tiendas. Las tiendas de MT, de premios y especiales que UPR-ZX salta no cambian.
- **Solo se baraja** y solo se mueven objetos normales: ids 1–111 y 135–327. Las MT, MO, objetos clave, exclusivos de versión y los ids sin uso se quedan en su sitio (lista de objetos permitidos de UPR-ZX para Gen4).
- Cada tabla se comprueba antes de usarla: el ARM9 descomprimido con su CRC32, los scripts con el de UPR-ZX.
- Ventana DS: en Gen4 se pueden barajar objetos del mapa y tiendas; los iniciales siguen desactivados (fase 7b).

## Validación

- `--nds-only` (sintético, CI): 71. Incluye el ARM9 sin comprimir y comprimido (lectura, guardado en su sitio con pie y parámetros del módulo, rechazo de un cambio de tamaño), el rechazo en juegos DSi y la clasificación de objetos de Gen4.
- `--nds-gen4-probe` sobre los tres volcados con todas las opciones: 39/39. Objetos en las mismas posiciones y con el mismo conjunto, los fijos sin cambios; tiendas fijas idénticas y mismo conjunto; ARM9 escrito (recomprimido en HeartGold, sin otro cambio que el «final comprimido») y overlays intactos; resto de archivos idéntico; originales sin cambios.
- Ventana DS: `--nds-ui-probe` 8/8 con HeartGold y con Diamante, ahora con objetos y tiendas.
- ROM de prueba para el juego (semilla 20261031, todas las opciones de Gen4), en `D:\Nintendo\DS\Juegos\Pruebas`:
  - `Diamante-todo-prueba.nds` (8753 cambios);
  - `Platino-todo-prueba.nds` (9179);
  - `HeartGold-todo-prueba.nds` (8698).

  Sustituyen a las `*-random-prueba.nds` de la fase 6.

## Pendiente

- Prueba en juego, sobre todo HeartGold: arranca con el ARM9 recomprimido, recoger objetos visibles y ocultos (con el Zahorí) y comprar en una tienda especial.
- Fase 7b: iniciales de Gen4.
