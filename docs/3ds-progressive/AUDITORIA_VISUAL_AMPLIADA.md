# Auditoría visual ampliada — 2026-11-01

Ampliación de `--visual-audit` para revisar toda la app, no solo la vista principal de Ultra Sol/Ultra Luna a dos tamaños, y arreglos de lo que encontró.

## Qué recorre ahora

- **Juegos:** X/Y, ORAS, Sol/Luna y Ultra Sol/Ultra Luna.
- **Tamaños lógicos de pantallas habituales:**
  - 1280×800 al 100 %;
  - 1920×1080 al 125 % (1536×864) y al 150 % (1280×720);
  - 1366×768 al 125 % (1093×614).

  En WPF el escalado de Windows equivale a una ventana más pequeña en unidades lógicas.
- **Vistas:**
  - Inicio y todas las secciones con sus pestañas, incluida Tiendas y BP, que antes faltaba.
  - Todas las herramientas que se abren desde los hubs («Abrir …»).
  - Las tablas que se abren desde una sección: Totem, Level Caps y Trainer Move Rules, y la tabla de precios.
  - La ventana de la lista blanca de compra única.
- **Ventanas y diálogos:** un temporizador captura y cierra cada uno, también los modales.
- **Errores de carga:** se registran sin cortar la auditoría.
  - Los que la app ya gestiona por falta de datos en la fixture son avisos.
  - Cualquier otra excepción es un problema.
- **Datos sintéticos:** la fixture añade Egg Moves, Mega Evolutions, Battle Tree/Royal/Maison, SMDH y un `Shop.cro` sintético de Sol/Luna. Tiendas y BP de USUM usa el `Shop.cro` de una extracción real, pasada como segundo argumento, que solo se lee.

Total: 466 vistas.

## Qué comprueba

- controles solapados y botones pegados;
- texto que no cabe en su línea o que se corta por abajo;
- controles recortados por su contenedor, descontando el margen;
- controles que se salen por la derecha;
- contraste de texto WCAG AA (4,5:1, o 3:1 en texto grande), teniendo en cuenta la opacidad;
- fondos claros del sistema (Border o Panel claros de más de 4.000 px²);
- pulgares de barra de desplazamiento de menos de 20 px;
- editores de celda con el aspecto del sistema.

El texto acortado con «…» es un aviso.

## Arreglos

| Problema | Dónde | Arreglo |
|---|---|---|
| El pulgar de la barra medía unos 4 px en listas largas (839 especies) y casi no se veía ni se podía agarrar | Todas las barras | Longitud mínima de 28 px: recurso del `Track` en el estilo de `ScrollBar` |
| En 1366×768 al 125 %, la cabecera ocupaba toda la altura y las listas y tablas quedaban aplastadas a 0 px | Game Text, Story Text, Template Studio, Player Level Caps | `SectionHost` desplaza la página cuando la vista declara una altura mínima (560, 540 y 540 px) y la ventana es más baja. Sin altura mínima no cambia nada. |
| La columna de opciones de 340 px no cabía; los botones se salían por la derecha | Level Up, MT/tutores | Columnas proporcionales con límites: lista de especies 170–242 px, opciones 250–340 px |
| Botones de la tarjeta en dos columnas sin sitio | Level Up («Especie seleccionada») | La tarjeta usa dos columnas si cada botón tiene al menos 130 px; si no, una. Las etiquetas largas pasan a dos líneas. |
| Texto secundario de 3,8:1 sobre el elemento seleccionado | Lista de archivos de Game Text y Story Text | La línea de detalle hereda el color del elemento al 78 % |
| «Seguridad de guardado» se cortaba en la columna del Inspector a 1280×720 | Pokémon | Se ajusta en varias líneas |
| Los dos botones de página no indicaban cuál estaba activa | Pantallas de título | La página actual usa el estilo principal y tiene estado accesible |

## Resultado

Las 466 vistas pasan sin problemas. Quedan 76 avisos, todos de herramientas que necesitan datos que la fixture no tiene:
- Shiny Rate, Type Chart y Pickup necesitan firmas de `code.bin`;
- las pantallas de título necesitan sus archivos;
- OWSE, Field Items y Encuentros estáticos de Gen6;
- O-Powers, y tutores, tiendas e intercambios de ORAS.

Las capturas de todas las vistas se revisaron a mano. Las de Gen6 tienen el mismo aspecto que las de Gen7.

## Límites

- Es renderizado fuera de pantalla a 96 PPP. El escalado real se representa con el tamaño lógico; no se prueba el redondeo de píxeles a 125 % o 150 %.
- Las vistas que dependen de firmas de `code.bin` o de archivos que la fixture no tiene solo se ven con datos reales.
- La interacción (ratón, teclado, foco) sigue cubierta por las suites de interfaz, no por esta auditoría.
