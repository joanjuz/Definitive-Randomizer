# Tablas: centrado, edición, secciones de estáticos y tablas verticales — 2026-10-31

Arreglos visuales pedidos por el propietario. Afectan a todas las tablas de la app: BST, level caps, Totem, movimientos, MT/tutores, tiendas, economía y las del editor común.

## Texto centrado en las celdas

La plantilla por defecto de la celda de WPF no centra el contenido, y con filas de 34 a 44 px el texto quedaba pegado arriba.
- `App.xaml` define un estilo implícito de `DataGridCell`. Su plantilla es un borde con un `ContentPresenter` centrado en vertical.
- `DarkGridTheme` y `ProgressiveBstTable` tienen su propio estilo de celda, que ahora hereda de ese, así que también lo aplican.

## Edición legible

`DataGridTextColumn` y `DataGridComboBoxColumn` crean el editor con su propio estilo por defecto. Por eso no se aplicaba el tema oscuro y el valor que se editaba se veía con los colores del sistema.
- `GridEditing` se instala al arrancar la app y engancha cada tabla cuando se carga, con un manejador de clase sobre `Loaded`.
- Cuando un editor llega con el estilo por defecto, se le aplica `GridTextBox` o `GridComboBox` y se selecciona el texto. Si una vista fijó su propio estilo, se respeta.

## Líneas de tabla

WPF dibuja las líneas en negro por defecto. Un estilo implícito de `DataGrid` las pone en el color del borde del tema (`Stroke`). Las tablas que ya fijaban su color lo conservan.

## Secciones en los encuentros estáticos

Igual que las pestañas de pk3DS, la lista ya no mezcla todo:
- **Gen7:** Regalos, Estáticos e Intercambios.
- **Gen6:** Iniciales, Regalos y Estáticos.
- **Todos** muestra la lista completa.

Cada botón indica cuántas entradas tiene su sección. La sección se combina con la búsqueda, y el alcance «entradas visibles» del randomizador respeta la sección elegida.

Es genérico: cualquier editor que implemente `IRecordSections` obtiene las secciones en la vista común.

## Una tabla vertical con las opciones a la derecha

- **Level Up:** una sola tabla de movimientos con desplazamiento propio. Encima, las acciones de la especie; a la derecha, en 340 px, las opciones del randomizador global en una columna.
- **MT y tutores:** en cada pestaña, una sola tabla vertical. Los botones y las opciones (Sanity, Follow Evolutions, precios…) pasan a una columna derecha con desplazamiento.

## Validación

Pruebas de interfaz renderizadas fuera de pantalla:
- `--all-ui-only`: 400 comprobaciones. Dos son nuevas:
  - las secciones de estáticos filtran correctamente (1 regalo, 200 estáticos y 202 en total en la fixture);
  - la celda centra el contenido y el editor recibe `GridTextBox`.
- También pasan `--visual-redesign-only` (95), `--rebuild-marts-ui-only` (69), `--requested-ui-only` (38), las de templates, `--guidance-only`, `--swsh-ui-only`, `--nds-ui-only` y `--synthetic-only` (4059).

Al capturar fuera de pantalla, las columnas con ancho proporcional (`*`) aparecen en su ancho mínimo. El `DataGrid` reparte ese ancho con el despachador de WPF, y la captura no le deja procesarlo. En una ventana con el despachador activo se comprobó que la columna Movimiento de Level Up ocupa todo el espacio (432 px de 524).

**Pendiente:** revisión en la app real, sobre todo la edición de celdas y el reparto de anchos.
