# Tiendas 3DS: disposición en tres columnas

Cambio exclusivamente de presentación en los editores de tiendas de X/Y, ORAS, Sol/Luna y Ultra Sol/Ultra Luna (`Gen6MartView.cs` y `MartEditorView.cs`). No cambia `GameMartService`, `GameMart6Service`, `NativeGen7MartBridge`, la receta de Global Template/Batch, el historial ni ningún dato de la ROM.

## Disposición

Las dos vistas comparten `MartWorkspace.cs` (solo presentación):

| Columna | Contenido |
| --- | --- |
| Izquierda | Lista vertical de tiendas con el número de objetos de cada una. En Gen7, «Tiendas normales» y «Tiendas BP» en la misma columna, cada grupo con su cabecera y su recuento. |
| Centro | Nombre y detalle de la tienda seleccionada (ranuras, máximo y, en Gen6 ampliado, ranuras usadas del total), su tabla, «Cambios propuestos» y el estado. |
| Derecha | Acciones en vertical: «Inventario de la tienda» (añadir/eliminar), las opciones de la vista previa y de Global/Batch agrupadas en «Randomización», «Objetos especiales» y «Restricciones», y, fijos al pie, «Generar vista previa» y «Aplicar y verificar». |

La columna de tiendas y la de opciones se desplazan solo en vertical. Los botones de opción ocupan el ancho de su columna y su texto se ajusta en varias líneas en lugar de recortarse.

En Gen7 las opciones de la receta siguen siendo botones acumulativos (clic para incluir, botón derecho para quitar). Una opción incluida muestra además una marca ✓, de modo que su estado no depende solo del color.

### Adaptación al ancho y al alto

`MartWorkspace` elige la disposición según el ancho útil del editor:

| Ancho útil (DIP) | Disposición | Tiendas · acciones |
| --- | --- | --- |
| ≥ 1180 | Ancha | 260 · 300 |
| 900–1180 | Media | 210 · 250 |
| 720–900 | Estrecha | 180 · 220 |
| < 720 | Compacta | lista y acciones apiladas en la columna lateral (240); la tabla conserva el resto |

Con poca altura, la descripción de la página pasa a una línea (texto completo como información sobre herramientas) y por debajo de 450 DIP se pliega; las cifras que da también figuran en la lista y en la cabecera de la tabla. La lista de cambios propuestos reduce su altura. El editor tiene un alto mínimo de 420 DIP: si la ventana es más baja, la página se desplaza en vertical, como el resto de editores con alto mínimo, en lugar de aplastar la tabla.

En Gen6 la columna «Objeto» pasa de un ancho mínimo de 400 a 220 para no forzar desplazamiento horizontal en ventanas estrechas. En Gen7 las columnas fijas «#» y «Acción» pasan de 60 y 90 a 48 y 84.

## Contratos conservados

- Campos que las pruebas y la ventana principal usan: `_regularPicker`, `_bpPicker`, `_tabs`, `_regularGrid`, `_bpGrid`, `_recipeFlags`, `_apply`, `_pending`, `_previewSeed`, `_specialOnly`, `_protectX` (Gen7) y `_picker`, `_grid`, `_checks`, `_pending` (Gen6). `RememberRecipe`/`RestoreRecipe`, `RememberOptions`, `Preview`, `ConfirmLeave` y `Editor` no cambian.
- Los selectores de tienda pasan de `ComboBox` a `ListBox` (ambos `Selector`); mantienen `SelectedItem`/`SelectedIndex` y el `CommitGrid` antes de cambiar de tienda. La lista admite búsqueda por teclado por nombre (`TextSearch`).
- Gen7 conserva el `TabControl` `_tabs` como fuente de verdad de la tabla visible (normal o BP): ya no muestra pestañas, y elegir o enfocar una lista selecciona su tabla. Al no generar contenedores de pestaña, la primera se selecciona de forma explícita.
- Las ocho opciones de receta, «Lista blanca de compra única…» y «Restablecer opciones» siguen en un único panel, en el mismo orden de índices.
- Textos de botones, casillas, cabeceras de tabla y mensajes: sin cambios. Se añaden cabeceras de grupo, el detalle de la tienda seleccionada, la indicación «Pulsa para incluir; botón derecho para quitar.» y, en añadir/eliminar ranura (Gen7), una información sobre herramientas que explica por qué están desactivados en tiendas BP.
- Funciones por juego: «Añadir/Eliminar ranura» solo si Shop.cro admite ampliación (USUM); compra única, lista blanca y 21 mentas solo en USUM; Gen6 activa añadir/eliminar solo con tabla ampliada.

## Comprobaciones

`--mart-layout-ui-only [carpeta de capturas] [extracción USUM]` (en el CI sin argumentos; ver [RUNNER_PRUEBAS.md](RUNNER_PRUEBAS.md)) abre Tiendas en la ventana principal real fuera de pantalla para Sol/Luna, X/Y y ORAS, y para Ultra Sol/Ultra Luna con una extracción, a 1920×1080 (100, 125 y 150 %), 1366×768 (100 y 125 %), 1024×640 y la ventana mínima. Comprueba la disposición, que nada salga por la derecha, que la tabla conserve alto y no necesite desplazamiento horizontal, que vista previa y aplicar estén siempre visibles, el cambio entre tiendas normales y BP, añadir/eliminar ranura con el recuento de la lista, la marca de opción incluida, la restauración de la receta y la presencia de cada acción con su texto. Con carpeta, guarda una captura por juego y tamaño a la escala correspondiente.

Siguen aplicando `--rebuild-marts-ui-only` (selector, búsqueda de objetos, vista previa acumulativa, aplicación, historial y reapertura), `--requested-ui-only` (opciones Gen6) y `--visual-audit`.

Estas pruebas se ejecutan fuera de pantalla. No sustituyen la comprobación interactiva de foco, contraste y escalado de Windows.
