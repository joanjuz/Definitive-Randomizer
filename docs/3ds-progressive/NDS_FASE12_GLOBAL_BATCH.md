# NDS fase 12: Global Template y Batch para DS — 2026-10-31

El espacio de trabajo DS tiene ahora, como Espada/Escudo, una página «Global Template y Batch» en SISTEMAS.

## Global Template

- Cada operación aplicada guarda su receta junto al historial: la sección y las ediciones manuales (claves `Entrada:Campo`), las opciones completas de cada randomización (incluida la semilla y el alcance) o las opciones de los ajustes del juego.
- «Crear desde historial» copia las recetas en orden. El template se guarda en JSON con el formato `DefinitiveRandomizer.NdsGlobal`, versión 1, y el código del juego.
- «Aplicar a esta ROM» repite las operaciones en orden, cada una con su vista previa, como si se hicieran a mano, y con sus semillas: sobre otra copia del mismo juego da exactamente los mismos cambios.
- Un template de otro juego se rechaza: las entradas y claves no coinciden entre juegos, igual que no se trasladan offsets entre ellos. El formato es propio de DS y no se mezcla con las recetas de 3DS ni de Switch.

## Batch ROM Builder

- Genera de 1 a 20 ROM nuevas desde la ROM abierta, en una carpeta elegida. Cada una abre una sesión nueva sobre el original, repite el template con semillas nuevas, se exporta y se verifica, con su registro `.cambios.txt`.
- No sobrescribe nada: si ya existe algún archivo de destino, no genera ninguno.
- La ROM de origen nunca se modifica.

## Validación

- `--nds-ui-probe` sobre los cinco juegos, además de lo anterior:
  - el template guardado y releído, repetido sobre una sesión nueva, produce las mismas operaciones y cambios;
  - un template de otro juego se rechaza sin tocar el historial;
  - un lote de dos ROM da ROM distintas, con su registro, y un segundo lote con el mismo nombre se rechaza;
  - captura de la página.

## Prueba en juego

**Confirmada por el propietario el 2026-10-31**: las ROM de prueba de la fase 14, que acumulan las fases 9 a 14, funcionan correctamente en el juego.
