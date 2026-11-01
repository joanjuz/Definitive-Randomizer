# Tablas: centrado, edición, secciones de estáticos y tablas verticales — 2026-10-31

Arreglos visuales pedidos por el propietario. Afectan a todas las tablas de la app: BST, level caps, Totem, movimientos, MT/tutores, tiendas, economía y las del editor común.

## Texto centrado en las celdas

La plantilla por defecto de la celda de WPF no centra el contenido, y con filas de 34 a 44 px el texto quedaba pegado arriba.
- `App.xaml` define un estilo implícito de `DataGridCell`. Su plantilla es un borde con un `ContentPresenter` centrado en vertical.
- `DarkGridTheme` y `ProgressiveBstTable` tienen su propio estilo de celda, que ahora hereda de ese, así que también lo aplican.

## Edición legible

`DataGridTextColumn` y `DataGridComboBoxColumn` crean el editor con su propio estilo por defecto. Por eso no se aplicaba el tema oscuro y el valor que se editaba se veía con los colores del sistema.
- `GridEditing` se instala al arrancar la app con manejadores de clase: `Loaded` para los `TextBox`, y `Loaded` y `GotKeyboardFocus` para los `ComboBox` (el `Loaded` de estos no llega a tiempo).
- Cuando un editor llega con el estilo por defecto, se le aplica `GridCellEditor` (cuadro de texto oscuro compacto, pensado para filas de 34 px) o `GridComboBox`. Si una vista fijó su propio estilo, se respeta.
- La primera versión enganchaba la tabla en su `Loaded` y no llegaba a ejecutarse; las pruebas solo llamaban a la función directamente. Ahora la auditoría visual pone celdas reales en edición y comprueba el estilo.

## Selección

El estilo implícito de celda también define la celda seleccionada con los colores del tema (`Selected`, `SelectionText`, borde `Accent` con foco). Antes, las tablas que no usan `DarkGridTheme` (Totem, precios de economía, Level Caps de entrenadores…) mostraban el azul del sistema.

## Líneas de tabla

WPF dibuja las líneas en negro por defecto. Un estilo implícito de `DataGrid` las pone en el color del borde del tema (`Stroke`). Las tablas que ya fijaban su color lo conservan.

## Secciones en los encuentros estáticos

Igual que las pestañas de pk3DS, la lista ya no mezcla todo:
- **Gen7:** Regalos, Estáticos e Intercambios.
- **Gen6:** Iniciales, Regalos y Estáticos.
- **Todos** muestra la lista completa.

Un selector «Sección» bajo la búsqueda indica cuántas entradas tiene cada sección (por ejemplo «Regalos (1)»). Se probó primero con botones, pero en la columna de la lista no cabían en una fila. La sección se combina con la búsqueda, y el alcance «entradas visibles» del randomizador respeta la sección elegida.

Es genérico: cualquier editor que implemente `IRecordSections` obtiene las secciones en la vista común.

## Una tabla vertical con las opciones a la derecha

- **Level Up:** una sola tabla de movimientos con desplazamiento propio. A la derecha, en 340 px, las tarjetas «Especie seleccionada» (Añadir, Quitar, Metrónomo, Randomizar, Exportar CSV, en dos columnas) y «Randomizar todos los Pokémon».
- **MT y tutores:** en cada pestaña, una sola tabla vertical. A la derecha, tarjetas «Asignación de MT» y «Compatibilidad de MT», o «Movimientos y precios» y «Compatibilidad de tutores».
- `OptionCards` construye esas tarjetas: título, descripción, botones a todo el ancho con el mismo espaciado, casillas alineadas y textos de ayuda en 12 px. Mueve los controles existentes, así que los manejadores no cambian. La acción que guarda es el botón principal.

## Botones e Inspector

- Los botones del Inspector parten el texto en dos líneas en todos los editores (antes solo en algunos), y la lista de registros también, en lugar de mostrar una barra horizontal.
- «Guardar cambios» y «Descartar cambios» ya no se tocan.

## Validación

Nueva auditoría `--visual-audit <carpeta>`: abre la ventana principal real fuera de pantalla con el despachador activo y recorre todas las secciones, sus pestañas y las tablas secundarias (Player Level Caps, Totem Level Caps, Totem BST, Level Caps y Trainer Move Rules de entrenadores, tabla de precios) a 1280×800 y 1600×900. En cada vista:
- detecta botones solapados, botones pegados en la misma fila, texto de botón cortado y controles fuera del borde derecho;
- pone en edición la primera celda de texto y de lista de cada tabla y comprueba que el editor no tenga el aspecto del sistema;
- guarda una captura PNG de cada estado e `issues.txt`.

Termina con 0 problemas. Las capturas se revisaron una a una.

Pruebas de interfaz renderizadas fuera de pantalla:
- `--all-ui-only`: 400 comprobaciones. Dos son nuevas:
  - las secciones de estáticos filtran correctamente (1 regalo, 200 estáticos y 202 en total en la fixture);
  - la celda centra el contenido y el editor recibe `GridCellEditor`.
- También pasan `--visual-redesign-only` (95), `--rebuild-marts-ui-only` (69), `--requested-ui-only` (38), las de templates, `--guidance-only`, `--swsh-ui-only`, `--nds-ui-only` y `--synthetic-only` (4059).

Al capturar fuera de pantalla, las columnas con ancho proporcional (`*`) aparecen en su ancho mínimo. El `DataGrid` reparte ese ancho con el despachador de WPF, y la captura no le deja procesarlo. En una ventana con el despachador activo se comprobó que la columna Movimiento de Level Up ocupa todo el espacio (432 px de 524).

**Pendiente:** revisión en la app real, sobre todo la edición de celdas y el reparto de anchos.
