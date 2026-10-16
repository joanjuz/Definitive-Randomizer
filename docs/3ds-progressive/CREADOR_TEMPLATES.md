# Creador de templates

Implementación en código fuente, 15/10/2026. Sección **Templates** del editor.
La distribución portable y su ejecutable no se han actualizado.

## Uso

1. Abrir un proyecto y elegir Templates.
2. Elegir Movimientos, Evoluciones, Estadísticas Pokémon, Objetos de campo o Tiendas.
3. Añadir filas, importar un archivo o cargar los valores Progressive. Cada tipo conserva su borrador al cambiar de tipo durante la sesión.
4. Comprobar y previsualizar. La pestaña Cambios previstos muestra entrada, campo, valor anterior y nuevo; no escribe la ROM.
5. Usar template aplica los cambios verificados y registra el archivo completo en el historial, incluso si el juego ya tiene esos valores.
6. Crear o guardar Global Template incorpora las acciones y copia sus dependencias a `<nombre>.assets`. Conservar esa carpeta junto al JSON al compartirlo o trasladarlo.

Exportar guarda un archivo independiente y no aplica el borrador a la ROM.
Se retiró el botón Nuevo para evitar confundirlo con aplicar. Usar template es la acción que guarda los cambios en el juego; importar sustituye el borrador del tipo seleccionado. Eliminar fila actúa únicamente sobre la fila seleccionada.

## Tipos y formatos

| Tipo | Edición | Importación / exportación |
| --- | --- | --- |
| Movimientos | Selector de movimiento; tipo y categoría automáticos editables; clasificaciones, objetivos, estados y estadísticas mediante selectores; números escritos; flags; descripción multilínea | CSV nativo o JSON del creador |
| Evoluciones | Origen, destino y método mediante selectores; nivel, argumento y forma cuando corresponde | CSV nativo o JSON del creador |
| Estadísticas | Selector de especie/forma; HP, ATK, DEF, SPA, SPD y SPE opcionales, entre 1 y 255 | CSV nativo o JSON del creador |
| Objetos de campo | Opciones del juego y catálogo editable: predeterminado, permitido o prohibido; filtro por nombre/ID | `DefinitiveRandomizer.FieldItems.v1` JSON nativo, o JSON del creador al importar |
| Tiendas | Disponible desde (pruebas / medallas) y objeto; tabla inicialmente vacía; incorporación acumulativa | JSON del creador con `ShopMode=AppendProgression` |

Las celdas opcionales vacías conservan el valor que tenga el juego al aplicar.
Los nombres del catálogo proceden del proyecto abierto. Los IDs no cambian al exportar.
Las descripciones de movimientos se escriben en el banco de español, con comprobación de relectura; se conservan los demás textos.
Los CSV escapan comillas y comas, y representan saltos de línea como `\n` para mantener compatibilidad con el lector existente.

### Progressive

Movimientos de Gen6 carga los datos internos si el CSV distribuido solo contiene su cabecera. USUM carga el CSV Progressive existente. Sol/Luna permite crear templates propios de datos de movimientos, pero no importa el balance exclusivo de USUM.

Objetos de campo carga el catálogo común Progressive para personalizarlo. Importar Progressive solo aparece donde existe una importación válida: movimientos de Gen6/USUM, objetos de campo y archivos de estadísticas/evoluciones con filas válidas. Se oculta en Sol/Luna para movimientos y en el nuevo creador de incorporaciones de tiendas.

En objetos de campo, Permitidos forma el catálogo de selección y Prohibidos excluye objetos de los reemplazos. Los filtros de seguridad siguen protegiendo las ubicaciones excluidas. El nuevo creador de tiendas no contiene catálogo, BP ni controles de randomización.

## BattlePatch

Cada nueva fila empieza en **None**. El desplegable muestra solo las implementaciones disponibles para ese movimiento y familia. El control y cada opción tienen una explicación al pasar el cursor.

None evita instalar mecánicas especiales, incluso al editar parámetros de datos que el antiguo motor podía interpretar como solicitudes implícitas. No desinstala parches que ya existan en el proyecto.

- Gen6: Escudo Real con reducción de Ataque de un nivel, sujeto a sus firmas compatibles.
- USUM: Escudo Real, Meditación, Pantalla de Humo, Pesadilla, Deseo, Capa Mágica, Chapoteolodo, Hidrochorro, Conjuro, Cerrojo Feérico, Neblina y Giro Vil.
- Sol/Luna: ninguno de los parches exclusivos de USUM.

Las implementaciones reutilizan el motor existente. No se han creado nuevos parches ni se aplican por defecto al añadir movimientos. Las firmas de combate se validan antes de guardar; que una implementación aparezca no certifica una revisión de CRO desconocida.

## Historial, Global y Batch

El archivo utilizado se conserva como JSON inmutable identificado por SHA-256 en `authored-templates`, junto al almacenamiento persistente del historial, fuera de RomFS y ExeFS. Cambiar o borrar el archivo importado/exportado original no altera la receta guardada.

El historial registra una operación `authored-template` dentro de la fase final de modificaciones, después de las randomizaciones normales. Estadísticas, movimientos y evoluciones aplican los valores pedidos; objetos de campo y presets aleatorios de tiendas usan la semilla de cada ROM de Batch. El nuevo template de tiendas añade sus objetos después de las randomizaciones. Un objeto desbloqueado en 0 pruebas se incorpora a las ocho etapas normales; uno desbloqueado en 2 pruebas se incorpora desde esa etapa hasta la última. Gen6 utiliza las nueve etapas de 0 a 8 medallas. Se añade una ranura sin reemplazar objetos originales, se conserva su orden y no se duplican objetos ya presentes. Tiendas especiales y BP quedan intactas.

Las modificaciones posteriores de estadísticas o textos quedan como `exact-overrides` ordenadas después del template anterior: gana la última intención guardada. Los comandos finales se consolidan por campo al volver a exportar. Se comprueban índices, tipos, generación y codificación antes de escribir. Los textos finales posteriores a un template se aplican en sus coordenadas del mismo juego, sin exigir que su texto intermedio ya exista en la ROM limpia durante la prevalidación.

Global exporta los JSON de templates como dependencias SHA-256 en su carpeta `.assets`; no incluye archivos completos de datos del juego. Batch copia también el template a su almacenamiento local de historial al reproducirlo, para que un proyecto editable pueda generar otra receta sin depender de la ubicación anterior.

## Límites

- El JSON del creador se vincula a la familia del proyecto. Los CSV de los formatos nativos siguen sujetos a los límites del juego al importarse o aplicarse.
- Las rutas de evolución se añaden/actualizan con la semántica existente; no se borran rutas ajenas para hacer hueco. Se rechaza superar las ocho rutas.
- Gen6 amplía inventarios solamente cuando reconoce la estructura de code.bin; en otro caso exige las longitudes originales.
- Sol/Luna conserva las longitudes originales de tiendas. USUM permite ampliar las tiendas normales cuando su CRO es compatible. Las tiendas BP conservan un número fijo de ranuras.
- El precio por ranura corresponde a BP de Gen7; Gen6 conserva sus precios habituales salvo los efectos de precio de presets ya implementados.
- Un fallo detiene la operación. Cada módulo conserva su guardado verificado; varios templates no forman una única transacción conjunta sobre todo el proyecto.
- No se reconstruyeron ROMs completas ni se realizaron pruebas dentro del juego de esta integración. No se controló el escritorio ni el emulador.
- La batería general y el empaquetado conservan las dos limitaciones ya comunicadas: prueba antigua de parches de combate y comprobación del ejecutable auxiliar sin apphost. No se han activado parches ni generado ejecutables para satisfacer esas pruebas.

## Revisión de usabilidad

Los selectores de datos del creador permiten escribir parte del nombre o el ID para filtrar, sin distinguir mayúsculas ni acentos. Cada selector mantiene su filtro independiente; escribir una búsqueda sin confirmar un resultado no modifica el ID guardado. Los controles se crean con un constructor público compatible con las plantillas WPF.

Se corrigió la lectura de `CurrentKind` en el trabajo de importación: el tipo y la ruta del diálogo se capturan en el hilo de interfaz antes de preparar datos en segundo plano. Se verificó la importación Progressive real con el contexto de sincronización WPF, sin mostrar ventanas ni controlar el escritorio.

Los JSON antiguos sin `ShopMode` mantienen su semántica de reemplazo únicamente en la reproducción de recetas existentes. El nuevo creador rechaza abrirlos como si fueran incorporaciones: no convierte silenciosamente un reemplazo en una operación distinta.

La ampliación de tiendas se verificó con copias aisladas de los recursos originales de X, Rubí Omega y Ultra Sol. Sol/Luna todavía tiene cantidades fijas: si necesita una ranura nueva, se rechaza antes de guardar; no se reutiliza una ranura existente para simular una ampliación. Esta limitación sigue pendiente.

## Verificación dirigida

Compilación Debug de aplicación y pruebas con `UseAppHost=false`, sin actualizar la distribución.

- Pruebas del creador: generación, formatos, opcionales, descripciones multilínea y comillas, selección de BattlePatch, ausencia de solicitudes implícitas con None, historial de usos sin cambios, precedencia de cambios posteriores, dependencias portables, reexportación y rechazo de hashes distintos.
- Pruebas WPF sin mostrar ventanas: tabla vacía, selección de movimiento, tipo/categoría automáticos, None, descripción y campos Z por generación.
- Copias aisladas de Shop.cro de Sol y Ultra Sol: inventario fijo tras randomizar, catálogo personalizado, prohibidos en tiendas/BP, conservación de precios, reproducción con la misma semilla e integridad del archivo original.
- Regresiones dirigidas de editores/historial/recuperación, compatibilidad MT de ambas generaciones, Batch nativo Gen6 y master, tutores ORAS y tiendas SM.

Resultado final: compilación con 0 errores y 79 advertencias existentes; 140 comprobaciones funcionales del creador, 20 de interfaz WPF y 18 sobre copias aisladas de tiendas (178 nuevas), más 904 regresiones dirigidas: 1.082 comprobaciones aprobadas en la primera integración. La revisión de usabilidad añade 118 comprobaciones dirigidas: 63 de búsqueda/interfaz/importación con WPF, 32 de propagación por etapas y formatos, y 23 sobre copias de recursos reales (incluida la negativa segura de Sol/Luna). Se repitieron las 1.082 anteriores: total de 1.200 comprobaciones aprobadas. Estos resultados no equivalen a una validación in-game ni a una verificación del empaquetado final. La DLL distribuida conserva su SHA-256 anterior: 644DD5B2A4B374D8BCED12FBBD06D2400F7B9F794F39EF1ECF1180BA48572D3B.

## Revisión del 16/10/2026: tema, idiomas y Field Items

- El selector del tipo de template vuelve a ser fijo, sin edición de texto. Los selectores de datos mantienen su búsqueda.
- SearchableTemplateComboBox reutiliza explícitamente el tema oscuro. La plantilla ComboBox incluye PART_EditableTextBox con fondo, texto y cursor del tema; sus elementos conservan también el estilo oscuro.
- Evoluciones no depende de que el preset disponible tenga nombres resueltos para poder abrir una tabla nueva. Los errores de disponibilidad del preset se capturan. Si un CSV incluye un argumento numérico, ese ID tiene prioridad sobre ItemName/AltItemName, aunque un nombre coincida con otro ID. Si no hay ID ni un nombre resoluble, se rechaza sin adivinar.
- Los templates creados, importados y guardados normalizan las identidades de movimientos, especies, objetos, tipos, categorías, métodos y estadísticas a IDs numéricos. Las descripciones, notas y los identificadores estables de BattlePatch conservan su naturaleza de texto. Al exportar no se aceptan nombres traducidos en campos de identidad.
- Field Items tiene filtros Todos / Permitidos / Prohibidos combinables con búsqueda. Estado actual considera la lista blanca: cuando existe, las entradas Predeterminado que estén fuera de ella son efectivamente prohibidas. Las reglas y los filtros se actualizan al editar. La pestaña Tabla del template se oculta y se abre directamente el catálogo.
- Compilación Debug con 0 errores y 79 advertencias existentes. 28 pruebas nuevas de identidad numérica, idiomas y prioridad de IDs, más 52 de interfaz, colores, apertura de evoluciones y filtros. Se repitieron las 1.200 comprobaciones dirigidas anteriores: total de 1.280 aprobadas. La distribución y su ejecutable permanecen sin actualizar; no se realizaron pruebas in-game ni control del escritorio.


## Argumentos y descripciones ampliadas (16/10/2026)

- Argumento usa un selector con búsqueda para métodos que reciben objetos (6, 8, 17–20 y 42), movimientos (21), especies (22) o tipos (29). Los demás conservan la entrada numérica. Cambiar el método no borra el argumento existente; un ID importado fuera del catálogo permanece disponible. Las selecciones siguen guardándose como IDs, independientemente del idioma.
- InGameDescription muestra una vista breve o «Añadir descripción…». Al pulsarla se abre un editor amplio con saltos de línea, Guardar descripción y Cancelar. Cancelar conserva el borrador previo; guardar modifica el borrador, que se aplica e incorpora al historial al usar el template.
- Las descripciones emergentes usan los colores oscuros de la aplicación y ajustan las líneas a un ancho máximo. Las opciones sin descripción no generan un tooltip vacío.
- Verificación sin mostrar ventanas ni controlar el escritorio: compilación Debug sin apphost; pruebas de argumentos por método, conservación de IDs, cambio de control, edición multilínea, supresión de tooltips vacíos y paleta oscura, junto con regresiones de búsqueda, importación y reproducción. La distribución permanece sin actualizar.

## Vista previa de InGameDescription (16/10/2026)

El editor ampliado incorpora el botón Previsualizar. Carga de forma asíncrona la primera fuente latina CFNT/FFNT del proyecto: X/Y `a/1/8/5`, ORAS `a/1/6/7`, SM/USUM `a/0/2/5`. Se leen las tablas de caracteres y anchos y las texturas A4/A8, sin exportar ni incluir assets del juego en la aplicación. Los recursos se mantienen en memoria dentro del editor.

La vista usa glifos y avances reales, conserva los saltos de línea y se actualiza al editar. Es una referencia de 320×112 píxeles, mostrada al doble de tamaño; el fondo, escala y área útil son aproximados y no representan un menú concreto. No interpreta comandos ni sustituye variables. Advierte de desbordamientos y caracteres sin representación; no modifica ni recorta el texto guardado. El dibujo se limita a 10.000 caracteres para mantener la respuesta del editor. Si falta la fuente o su formato no es compatible, se explica el motivo y se permite seguir editando y guardar.

Verificación: 42 comprobaciones sobre fuentes reales de X, Rubí Omega, Sol y Ultra Sol, glifos españoles, avances proporcionales, saltos, desbordamientos, recursos truncados, integridad SHA-256 de los originales, botón asíncrono y actualización en vivo. Imágenes resultantes revisadas visualmente sin controlar el escritorio. Compilación Debug sin apphost; distribución sin actualizar. No se afirma equivalencia exacta con el renderizado in-game.


### Ficha real de movimientos en Gen7

La vista de movimientos SM/USUM usa ahora el paquete ALYT de la ficha: SM `a/1/3/1`, USUM `a/1/3/2`, registro 0, diseño `CommonStatus_DSC_LOW_000.bflyt`. Se decodifican sus texturas `Waza_BG_00.bflim` y `Battle_Infomation_Basic_01.bflim`, sus colores por vértice y la paleta de tipo de las animaciones `color_00` a `color_17`. Las posiciones y el área de descripción salen del diseño; se usan letras blancas de la ROM. Es una composición estática: los símbolos de tipo/categoría se expresan como texto y no se ejecutan scripts, transiciones o variables.

El ejemplo se toma de la fila editada: nombre del movimiento y tipo, categoría, PP, potencia y precisión del borrador, con respaldo en los valores del proyecto para campos vacíos. Si la fila aún no tiene movimiento se usa el primero del catálogo. La vista no aplica cambios a la ROM ni al historial. No se distribuyen estos assets. El renderizado de Gen6 mantiene su vista de referencia anterior; esta ficha específica corresponde a Gen7.

Pruebas con recursos de Sol y Ultra Sol: los 18 tipos, diversidad de paletas, anclaje del cuadro real, desbordamiento y botón de previsualización completo desde un proyecto aislado. Las imágenes compuestas se revisaron sin abrir ventanas ni controlar el escritorio.


### Ficha de movimientos Gen6 (X/Y y ORAS)

El botón Previsualizar usa también la ficha nativa de detalle de movimientos en combate de Gen6: GARC `a/0/0/5`, registro 11, paquete DARC con `waza_info.bclyt`. Se leen sus texturas BCLIM, materiales, ventanas y separadores; X/Y y ORAS tienen diferentes identificadores y opacidades, conservados por el lector. La descripción utiliza el panel `14`, posición (3, 120), área 318×60 dentro de una pantalla 320×240. Las dimensiones nominales de la fuente CFNT y el tamaño del texto CLYT determinan escalas horizontal y vertical, en lugar de reutilizar la escala de Gen7.

Se muestran el movimiento y los valores del borrador, se mantienen los saltos de línea y se avisa de desbordamientos sin modificar lo guardado. Los tipos y categorías dinámicos se expresan como etiquetas. El fondo es el recurso estático del juego: no se captura ni emula un combate. Si faltan recursos, el editor informa del error y permite seguir editando. Los assets se leen de la ROM del usuario y no se copian a la distribución.

Verificación con X, Y, Rubí Omega y Zafiro Alfa: lectura y dibujo de la ficha, tipos válidos, tres líneas a escala del juego, desbordamientos horizontales/verticales, datos inválidos y SHA-256 de recursos originales. Pruebas WPF del botón y actualización en vivo sin mostrar ventanas; imágenes de X y ORAS revisadas visualmente. Se repiten además las pruebas de fuentes/fichas Gen7 y de interfaz de templates. La distribución y su ejecutable siguen sin actualizarse.


### Ajuste visual Gen6

La ficha Gen6 sustituye el fondo de captura estático incluido en el recurso (imagen borrosa y ajena al movimiento editado) por paneles limpios del tema oscuro. Mantiene las posiciones, dimensiones del texto, escalas CFNT y límites de descripción del diseño de la ROM. El texto usa un color claro uniforme para conservar contraste en X/Y y ORAS. La interfaz identifica explícitamente que el fondo está adaptado al tema y ya no lo presenta como una reproducción del fondo original.


### Fondo propio del juego para Gen6

La vista Gen6 vuelve a utilizar exclusivamente recursos gráficos del proyecto: el fondo `back.bclim` y los colores del panel `22` de `waza.bclyt` (resumen de movimientos: GARC `a/0/0/0`, registro 2 en XY y 3 en ORAS), junto con los paneles, separadores, fuente y medidas del detalle `waza_info.bclyt`. Se excluye la captura borrosa `bg_cap.bclim`. No se dibuja el fondo adaptado al tema oscuro. La interfaz describe esta combinación como composición estática de recursos originales, no como reproducción exacta de una pantalla ejecutada.

La composición estática limita las capas a `back.bclim`, los separadores `line.bclim` y los paneles `black_win.bclim` del proyecto. Omite las demás ventanas del detalle para evitar incorporar elementos de captura o contexto ajenos al movimiento. No utiliza colores ni rectángulos del tema de la aplicación como fondo.
