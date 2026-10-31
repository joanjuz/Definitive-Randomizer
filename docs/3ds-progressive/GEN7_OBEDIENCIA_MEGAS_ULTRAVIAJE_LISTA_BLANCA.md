# Gen7: obediencia, formas Mega, pista del Ultraviaje y lista blanca de compra única — 2026-10-31

Cuatro opciones nuevas para Sol, Luna, Ultra Sol y Ultra Luna. Las cuatro se guardan en el historial, pasan a Global Template y funcionan en Batch.

## Los Pokémon intercambiados siempre obedecen (SM y USUM)

**Dónde:** Mecánicas y CRO → «Obediencia y formas Mega · Gen7».

Un Pokémon con otro Entrenador Original desobedece si su nivel supera un límite que depende de las grandes pruebas superadas: 20, 35, 50, 65 y 80, o 100 tras la Liga. Con los level caps altos, los Pokémon de los intercambios del juego desobedecían.

- El límite está en una tabla de seis bytes de `code.bin`, que la preparación del combate indexa por progreso. Su posición es `0x4C4644` en USUM y `0x49E390` en SM.
- El ajuste pone los seis valores a 100, la regla que el juego ya aplica tras la Liga. Cambian 5 bytes.
- Antes de escribir se comprueba que la tabla tenga los valores originales y que el literal de la función que la usa apunte a ella (`0x21BD64` en USUM, `0x213C0C` en SM). Una tabla desconocida se rechaza, y el ajuste no se aplica dos veces.
- Acción de Global/Batch: `player.traded-obedience`.

## Quitar formas Mega obtenibles (SM y USUM)

**Dónde:** Mecánicas y CRO → «Obediencia y formas Mega · Gen7».

Las formas Mega y Primigenias y Ultra-Necrozma pasan a su forma base en:
- salvajes, incluidas las tablas SOS y las del clima;
- encuentros estáticos;
- regalos, incluidos los iniciales;
- intercambios;
- evoluciones.

Lo que no cambia:
- **Combates Totem** de USUM, incluido Ultra-Necrozma: no se pueden capturar.
- **Entrenadores:** pueden seguir megaevolucionando.
- **Otras formas de combate:** Zen, Banco, Filo, Mimikyu destapado, Greninja Ash y Necrozma Melena Crepuscular o Alas del Alba se mantienen, por decisión del propietario.

Cada editor genera una propuesta con sus diferencias, y al aplicar se guarda en el historial. En Batch se ejecuta al final, después de las modificaciones manuales, para que ninguna vuelva a introducir una Mega.

Acción de Global/Batch: `pokemon.no-obtainable-megas`.

## Pista de GAME FREAK en el Ultraviaje (USUM)

**Dónde:** Story Text → «Pista de GAME FREAK en el Ultraviaje».

Tras Necrozma, la Unidad Ultra ofrece explorar el Ultraespacio a lomos de Lunala o Solgaleo: archivo 366, líneas 6 y 16. El botón añade una página a ese diálogo, antes del comando que cierra la caja, en español y en inglés:

> Por cierto, en las oficinas de GAME FREAK de Ciudad Kantai te enseñarán otra forma de guiar a Lunala (Solgaleo) por el Ultraespacio.

> By the way, the GAME FREAK office in Heahea City can show you another way to steer Lunala (Solgaleo) through Ultra Space.

- Antes de editar se comprueba que la línea sea el texto esperado.
- Una línea que ya menciona GAME FREAK no se toca, así que el botón no duplica la página.
- El resto de idiomas no cambia.
- El archivo 730, el del «documento confidencial» que explica el giroscopio y el botón deslizante, no se modifica.

Acción de Global/Batch: `usum.ultra-warp-hint`.

## Lista blanca de la compra única (USUM)

**Dónde:** Tiendas y BP → «Lista blanca de compra única…».

Los objetos marcados se venden sin límite en las tiendas especiales aunque la compra única esté activa.

**Cómo funciona.** El código ARM de la tienda busca la pareja tienda + objeto en el registro y compara el campo de tienda con la tienda actual (8–27).
- Las ofertas de la lista blanca se registran como las demás, pero con el bit 15 activo en el campo de tienda. Ese campo nunca coincide, así que la tienda no las encuentra y vende sin límite. **No se modifica el código ARM.**
- Cada fila conserva su posición y su bit de la partida. Quitar un objeto de la lista vuelve a aplicar el límite con el mismo bit.
- La ROM recuerda la lista porque las filas marcadas permanecen en el registro. El paso final de Batch (`FinalizeSinglePurchase`) la conserva y aplica la de la receta.
- Las ofertas del prototipo (tienda 10/objeto 101, 11/101 y 10/102) se registran siempre. Si están en la lista blanca, el bit antiguo de la partida deja de aplicarse.

**Dónde se guarda.**
- Global Template: en `ProgressiveApp.Gen7Marts.SinglePurchaseExempt`, una lista de IDs de objeto. Las recetas anteriores siguen siendo válidas.
- Historial: en la operación manual de tiendas (`SinglePurchaseExempt`).

**Limitación.** Solo afecta a objetos que estén en alguna tienda especial cuando se aplica. Un objeto que se añada después a una tienda especial con otra herramienta entra con límite hasta que se vuelva a aplicar la lista.

## Global Template y Batch

- Las tres acciones nuevas no tienen parámetros. Una receta creada desde el historial queda completa con ellas: hubo que añadirlas a las listas de `NativeRecipeCompletion`, `GlobalEditorTemplateBridge` y `BatchRomExecution`.
- El backend `LegacyBatch` las ejecuta con `Gen7Adjustments.Execute`, la misma lógica de la app, después de las modificaciones manuales y antes del cierre de la compra única.

## Validación

- `--gen7-adjust-only`, sintético (22 comprobaciones):
  - tabla de obediencia de ambas familias: se aplica una vez y rechaza valores desconocidos u offsets de otra familia;
  - detección de Megas;
  - quitar Megas en salvajes, estáticos, regalos, intercambios y evoluciones, conservando los Totem y las demás formas de combate, con historial y Global Template;
  - lista blanca en la receta;
  - las acciones nuevas están en la lista de Batch.
- `--gen7-adjust-real <USUM> <SM>`, copias de Ultra Luna y Sol (42 comprobaciones):
  - obediencia sobre los `code.bin` reales;
  - pista del Ultraviaje sobre los textos reales: solo cambian las líneas 6 y 16 de cada idioma, y no se duplica;
  - lista blanca sobre el `Shop.cro` real;
  - Global Template con las tres acciones y la lista blanca pasando por el backend original;
  - los archivos originales no cambian.
- `verify_integrated_arm.py`, con Unicorn: el verificador ahora lee las ofertas exentas del registro.
  - Con nueve ofertas en la lista blanca (un objeto en siete tiendas y una oferta del prototipo): 516 comprobaciones; cada oferta exenta se compra dos veces sin escribir en la memoria de la partida.
  - Sin exenciones: 534.
- Interfaz fuera de pantalla: los botones nuevos solo aparecen en los juegos compatibles.
- `--single-purchase-copies` (173) y `--single-purchase-backend` (4): sin regresiones.
- **Pendiente: la prueba en el juego.**
  - Un Pokémon de intercambio de nivel alto obedece antes de la Liga.
  - No aparecen Megas en salvajes, regalos ni intercambios.
  - La página nueva se ve bien en el diálogo de la Unidad Ultra.
  - Un objeto de la lista blanca se puede comprar varias veces.
