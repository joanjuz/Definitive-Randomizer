# Rediseño de navegación y jerarquía visual — 2026-11-02

Objetivo: que quien abre el randomizer encuentre cada herramienta sin perderse. Parte del análisis de esta fecha, hecho con capturas reales de `--visual-audit`.

Es un cambio de presentación: no cambia motores, randomización, servicios de ROM, formatos ni contratos de Global Template/Batch. Se conservan los nombres de los controles, los manejadores, los `Tag` de ruta y los bindings. Solo afecta a la app WPF de 3DS. Los espacios de Nintendo DS y Espada/Escudo comparten la cabecera y los estilos globales, pero su diseño no se revisó.

## Qué cambia

### Barra lateral

- **Grupos por objetivo, plegables:** *Pokémon y movimientos*, *Mundo y combates*, *Textos*, *Parches y ROM* y *Recetas y lotes*.
  - Cada cabecera pliega su grupo.
  - Una cabecera se oculta si el modo actual no muestra ninguna de sus entradas, por ejemplo *Textos* en modo simplificado.
  - Al abrir una sección, su grupo se despliega.
- **Opciones e idioma** queda fija abajo, fuera del desplazamiento.
- **Automatización** deja de verse en la barra.
  - Sus accesos (los randomizadores, Global Template, Batch e Historial) ya estaban en la barra.
  - El botón y su ruta se conservan: se abre desde el buscador, la vista guiada y la auditoría.
- **Marca de cambios:** cada entrada muestra cuántas operaciones guardadas tiene en el historial.
  - Se calcula leyendo el historial existente con `SectionProgress`.
  - Se actualiza con el evento `ProjectHistory.Committed`, que solo se lanza después de una confirmación verificada y no puede fallarla.
  - El lector de pantalla anuncia la misma información.
- **Resaltado único:** `HighlightNavigation` marca la sección abierta. Si es una herramienta de un hub, marca el hub.
  - Antes, al volver a Pokémon o Movimientos solo se limpiaban cuatro botones.
  - Las herramientas de los hubs no marcaban ninguno.
- La barra pasa de 218 a 236 px para que «Herramientas Pokémon» quepa junto a la barra de desplazamiento.

### Buscador (Ctrl+K)

- Se abre con el botón «Buscar…» de la barra o con **Ctrl+K**.
- Lista todos los destinos disponibles para el juego abierto: secciones, herramientas de los hubs y acciones generales (backup, Rebuild, randomizado guiado, cambio de modo…).
- Cada resultado dice dónde está, por ejemplo «Herramientas Pokémon», para que el usuario aprenda la estructura.
- La búsqueda ignora tildes y mayúsculas, y admite sinónimos («shiny», «caps», «tiendas», «variocolor»…).
- Se maneja con teclado: ↑/↓, Intro y Esc. Un clic fuera lo cierra sin hacer nada.
- Solo navega con los manejadores originales; no aplica cambios.
- Usa los temas de `GuidanceCatalog` (los mismos que la vista guiada) y respeta `ProjectFeatureAvailability`.
- A diferencia de la vista guiada, no cambia a modo completo.

### Miga de pan en herramientas de hub

Las herramientas que se abren desde un hub muestran arriba «← Herramientas Pokémon › Egg Moves». El botón vuelve al hub.

### Cabecera

- **Sin proyecto:** «Abrir proyecto…» es la acción principal.
- **Con proyecto:** la acción principal pasa a ser «Rebuild», o «Exportar ROM…/mod…» en DS y Switch.
  - «Extraer ROM…» se oculta; sigue en Herramientas ROM y en el buscador.
  - «Restaurar backup…» pasa a estilo terciario.
- El orden visual va de lo secundario a lo principal, y el orden de tabulación lo sigue.

### Inicio: estado del proyecto

Con un proyecto abierto, Inicio muestra:

- cuántos cambios hay guardados y en qué secciones (cada sección es un botón que lleva a ella);
- si falta un **backup** o un **Rebuild** desde los últimos cambios. Esto se recuerda **solo durante la sesión** (`SessionMilestones`, en memoria): no se escribe en el proyecto, el historial ni las preferencias.

### Editores (`RecordEditorView`)

- **Inspector por grupos:**
  - arriba, la acción principal destacada y, justo debajo, su **alcance**;
  - después, *Tablas y reglas*, *Templates* y *Copiar y pegar tablas*;
  - las herramientas de diagnóstico y rangos BST quedan en *Más herramientas*, plegado;
  - los grupos sin acciones visibles se ocultan;
  - se quita el título «Inspector» repetido (ya lo muestra el encabezado plegable).
- **Indicador de pasos:** «1 Configura las opciones › 2 Genera la vista previa › 3 Aplica los cambios».
  - Avanza con la vista previa.
  - Vuelve al paso 1 si cambian las opciones.
  - Marca todo como hecho al aplicar.
- **Barra «Guardar / Descartar»:** se oculta mientras está abierto el panel de randomización, que tiene su propio Vista previa/Aplicar. Así no conviven dos formas de confirmar.
- **Opciones sin huecos:** `FormGrid` sustituye al `UniformGrid` de los formularios. Cada fila mide lo que su contenido, en lugar de igualarse al campo más alto del formulario.
  - Hereda de `UniformGrid`, así que el paso a una columna por debajo de 460 px sigue funcionando.
- **Textos de ejemplo** («Nombre o número») en las búsquedas, mediante `InputHint`, que también los expone como texto de ayuda accesible.

### Pokémon

- Las acciones del Inspector se agrupan como «Todas las especies»; «Randomizar Pokémon» es la principal.
- La tarjeta del BST repetido pasa a ser «Especie seleccionada».
- «Sesión actual» pasa a ser «Proyecto», con «Sprites: …».

### Pestañas

Las pestañas que ocupan dos filas usan un `WrapPanel` en lugar de `TabPanel`. Al seleccionar una ya no cambian de fila, como pasaba en las opciones de Entrenadores.

### Hubs (Mecánicas, Herramientas Pokémon, Herramientas ROM, Automatización, Opciones)

- Tarjetas en cuadrícula adaptable (`TileGrid`, mínimo 340 px por tarjeta), en lugar de una tarjeta por fila a todo el ancho.
- Cada tarjeta tiene:
  - una descripción breve en lenguaje sencillo;
  - una etiqueta «Avanzado», «Parche de código», «Experimental» o «Paso final» cuando corresponde;
  - el texto técnico original dentro de «Detalles técnicos», plegado y siempre al final de la tarjeta.
- Los títulos y las etiquetas de los botones no cambian. Las pruebas y la auditoría los usan.
- Automatización ya no dice que no existe un «Randomize All»: remite al Randomizado guiado.

### Textos: tuteo

Toda la interfaz pasa a tuteo. Antes se mezclaban «Seleccioná/Elegí/Revisá…» con «Selecciona/Elige/Revisa…».

- Se hizo con una tabla explícita de unas 90 formas verbales, por palabra completa.
- Afectó a 264 textos de la app (vistas, mensajes y excepciones mostradas) y a una comprobación de `VisualRedesignChecks`.
- No se cambiaron términos (Level Up Moves, Field Items, Game Text…), como pide la regla de terminología.

## Archivos

- **Nuevos:** `NavigationAids.cs` (`InputHint`, `Breadcrumbs`, `NavBadge`, `FormGrid`, `TileGrid`, `StepIndicator`, `SectionProgress`, `SessionMilestones`), `CommandPalette.cs`, `MainWindow.Navigation.cs`, `3DS.Progressive.Tests/NavigationRedesignChecks.cs`.
- **Modificados:**
  - `MainWindow.xaml` y su code-behind, `MainWindow.Guidance.cs`, `App.xaml`;
  - `RecordEditorView.xaml` y su code-behind, `SystemsView.cs`, `StartView.cs`, `RomRebuildView.cs` (solo marca el Rebuild de la sesión);
  - `Services/ProjectHistory.cs`: evento `Committed`, sin cambio de formato;
  - los textos en tuteo de las vistas y los servicios;
  - `3DS.Progressive.Tests/VisualAudit.cs`, que también recorre las entradas ocultas que siguen siendo una ruta (`Tag`), para no perder la auditoría de Automatización.

## Pruebas

- `--visual-redesign-only` incluye las comprobaciones nuevas de `NavigationRedesignChecks`:
  - grupos y modos;
  - cabecera;
  - resaltado y miga de pan;
  - buscador (resultados, tildes, vacío, Intro);
  - marca de cambios y estado de Inicio a partir de una confirmación real del historial de la fixture;
  - `FormGrid`, pasos, barra de guardado, grupos del Inspector, texto de ejemplo, pestañas y cuadrícula de hubs.
- Capturas en `bin/<config>/net10.0-windows/visual-redesign/navigation-*.png`.
- `--visual-audit`: 588 vistas, 4 juegos, 4 tamaños.

## Segunda iteración (2026-11-02): qué cambió, exactitud, Entrenadores y Encuentros

### Marcas de cambios exactas y con detalle

- **Fallo corregido:** en un proyecto randomizado con un Global Template, casi ninguna sección se marcaba.
  - Esas entradas del historial tienen módulo «Global Template» y la acción como identificador (`trainers.randomize`, `marts.add-rare-candies`…).
  - Ahora la sección se decide por el identificador: el parámetro `ActionId` o la acción del Global Template. Hay 66 identificadores con nombre legible.
  - Si no hay identificador, se usa el módulo, como antes.
- **Codificación dañada:** algunos historiales antiguos guardan módulos con una tilde rota («Encuentros est�ticos») y se clasificaban como Encuentros salvajes. La comparación ignora ahora los caracteres no ASCII.
- **Comprobado con los historiales reales de Ultra Luna y Ultra Sol, solo en lectura:** con el identificador o el módulo, todas las entradas tienen sección.
- **Qué se cambió:**
  - El tooltip de cada sección de la barra lista los últimos cinco cambios.
  - Inicio tiene «Ver qué se cambió», con la lista por sección.
  - Cada sección abierta muestra arriba «N cambios guardados ▾», con título, detalle y fecha de cada operación, y «Ver el historial completo».
  - El detalle incluye si vino del Global Template, cuántos valores cambió y a quién afectó (por ejemplo, «Tapu Koko, Lillie y 3 más»).

### Entrenadores

- **Cabecera del entrenador:** nombre, clase y etiquetas con el tipo de combate, el número de Pokémon, el rango de niveles, los objetos de combate y si cura tras el combate.
- **Tarjetas del equipo:** sprite, especie, nivel, objeto equipado y movimientos. Muestran los valores actuales, también los no guardados (se actualizan a los 250 ms de editar).
- **Un Pokémon a la vez:** se edita solo el Pokémon elegido en las tarjetas, en lugar de seis bloques apilados que incluían las ranuras vacías.
  - Su título sigue la tarjeta: «Pokémon 2 · Pikachu · Nv. 11».
  - Los datos del entrenador pasan al final.
- **«+ Añadir Pokémon»:** sube «Pokémon en el equipo» y abre la ranura nueva; se guarda con «Guardar cambios».
- Se mantienen los seis editores (están ocultos, no eliminados), copiar y pegar Pokémon y los nombres de todos los campos.

### Encuentros

- **Tarjetas:** cada ranura muestra su probabilidad (tabla principal de Gen7) o sus niveles (Gen6). Las vacías aparecen atenuadas como «Vacío». Todo se actualiza mientras se edita.
- **Tabla de ranuras:** cada tabla de encuentros es una tabla compacta, con una fila por ranura: número, especie, forma y probabilidad (Gen7) o niveles mínimo y máximo (Gen6).
  - Antes eran dos campos sueltos por ranura en un formulario de dos columnas.
  - Las probabilidades salen de «Niveles y probabilidades» y van junto a su especie.
  - Las claves de los campos no cambian.

### Pruebas de esta iteración

`NavigationRedesignChecks` suma 15 comprobaciones, con lo que `--visual-redesign-only` queda en 150:

- asignación con identificadores, Global Template, tildes dañadas y obediencia;
- panel de cambios de la sección y tooltip de la barra;
- cabecera del entrenador y un solo editor visible;
- selección de ranura, tarjetas en vivo, «Añadir Pokémon» y título de la ranura;
- probabilidad junto a la especie en la tabla, y probabilidad en las tarjetas.

El resto de suites sigue igual (`--all-ui-only` 408, `--synthetic-only` 4101, `--guidance-only` 233, Switch 316/117/239 y DS 161/15). `--visual-audit` da 0 problemas y 77 avisos.

La auditoría cuenta 568 vistas en lugar de 588, con las mismas 648 capturas. Inspecciona cada grupo de pestañas visible, y ahora cada entrenador muestra un solo editor de Pokémon con sus pestañas, no seis.

Capturas: `trainers-redesign*.png` y `wild-redesign*.png`. En `UiSmoke`, el panel de tarjetas del equipo se lee ahora como `Panel` porque pasó de `StackPanel` a `WrapPanel`.

## Tercera iteración (2026-11-02): opciones aplicadas, Entrenadores, Field Items y texto de movimientos

### Opciones aplicadas visibles al randomizar

- **Fallo:** al abrir un proyecto ya randomizado, «Randomizar» mostraba las opciones por defecto, sin las que se aplicaron. Pasaba también con los rangos del BST progresivo de Entrenadores.
- **Corrección (`MainWindow.AppliedOptions.cs`):** al abrir el proyecto se leen, solo en lectura, dos fuentes:
  - El último Global Template aplicado: su `GlobalTemplateSnapshot`, con `GlobalTemplateDocument.FromHistory`. Se pasa a la configuración de cada sección con el mismo `PrepareGlobalOptions` que usa «Cargar template global». Así se recuperan entrenadores con sus rangos BST, estáticos, objetos, evoluciones, movimientos huevo, Field Items, tiendas, MT y tutores, y Pokémon.
  - Si una sección se randomizó después desde su propio editor, las opciones de esa entrada: los valores planos del editor, `Controls` y `TrainerTemplate` de Entrenadores (con sus rangos BST). Se toma la entrada más reciente que tenga opciones legibles.
- **Aviso en el panel:** «Estas son las opciones de la última randomización aplicada (Global Template «…» · fecha)», para saber de dónde salen.
- **Qué no se toca:** no se escribe nada en el proyecto, el historial ni las recetas. Si la receta no es válida para el juego, se ignora sin bloquear la apertura.
- **Sin carreras:** abrir una sección espera a que termine la lectura, así nunca muestra las opciones por defecto por llegar antes.

### Entrenadores

- **Lista:** cada entrenador muestra un subtítulo con clase, Pokémon, niveles y si es combate doble. La búsqueda también encuentra por clase.
- **Gen7:** género (Aleatorio, Macho, Hembra) y habilidad (Cualquiera, Habilidad 1, Habilidad 2, Habilidad oculta) pasan a desplegables. Los valores guardados (0–3) son los mismos.
- **IV y EV:** tabla con una fila por estadística (nombres tomados de las etiquetas del servicio) y el total de EV sobre 510.

### Field Items (Sol/Luna y Ultra Sol/Ultra Luna)

- **Interfaz nueva:** `IFieldItemPresentation` (solo lectura), implementada en `GameFieldItemsService` con las mismas reglas de MT y objetos clave que los filtros.
- **Field Items del juego:** etiquetas con ubicaciones, visibles, ocultos, MT, objetos clave y objetos distintos. Los «más repetidos» filtran la lista al pulsarlos.
- **Área del recogible:** los demás recogibles de esa área y las otras ubicaciones del mismo objeto, todo navegable.
- **Aviso:** si el objeto queda excluido por «Excluir MT» o «Excluir objetos clave».
- La edición del objeto y el guardado no cambian.
- Gen6 guarda los objetos por scripts y todavía no tiene este resumen.

### Movimientos: nombre y descripción

- **Tarjeta «Nombre y descripción en el juego»:** muestra los textos `MoveNames` y `MoveFlavor` del movimiento elegido.
- **Edición:** la descripción se edita con los saltos de línea reales del juego.
- **Vista previa:** con la fuente y el recuadro del juego (`TemplateDescriptionWindow`).
- **Guardado:** mediante `GameTextService.Save`, el mismo camino verificado y la misma entrada de historial que el editor Game Text, en el idioma activo.
- **Cambios pendientes:** el texto sin guardar cuenta como cambio del movimiento, así que se pregunta antes de salir.
- Los demás editores muestran el nombre nuevo al volver a abrir el proyecto.

### Pruebas de esta iteración

`NavigationRedesignChecks` suma 11 comprobaciones, con lo que `--visual-redesign-only` queda en 161:

- género y habilidad por nombre, tabla IV/EV y subtítulo de la lista;
- opciones guardadas al randomizar, recuperadas al reabrir el proyecto y con aviso de su origen;
- tarjetas de Field Items;
- tarjeta de texto de Movimientos con sus saltos de línea, cambio pendiente y guardado en Game Text.

La prueba de Movimientos crea un banco de textos sintético con los archivos de nombres y descripciones de USUM. El resto de suites sigue igual. `--visual-audit`: 568 vistas, 0 problemas, 77 avisos (los mismos de antes).

## Cuarta iteración (2026-11-02): menú contextual del equipo y pestañas del Pokémon

### Menú contextual del equipo

- Se quitan los botones «Copiar Pokémon» y «Pegar Pokémon» del editor de cada ranura.
- Al hacer clic derecho sobre la tarjeta de un Pokémon del equipo se abre un menú con:
  - **Copiar**;
  - **Cortar** (copia y elimina);
  - **Pegar** (solo se activa si hay un Pokémon copiado del mismo editor, de este u otro entrenador);
  - **Eliminar del equipo** (los Pokémon siguientes suben una posición y baja «Pokémon en el equipo»).
- Un entrenador conserva al menos un Pokémon: con uno solo, Cortar y Eliminar están desactivados.
- Con la tarjeta enfocada funcionan los mismos atajos: Ctrl+C, Ctrl+X, Ctrl+V y Supr.
- El clic derecho sobre «+ Añadir Pokémon» ofrece «Pegar como nuevo Pokémon».
- Todo actúa sobre los mismos campos y se guarda con «Guardar cambios», como cualquier edición (se puede descartar).

### Pestañas del Pokémon

«Principal», «IV y EV» y «Movimientos» pasan de botones con recuadro a pestañas subrayadas (`UnderlineTabControl`): texto con una línea de acento bajo la seleccionada. Las pestañas siguen siendo las mismas.

### Menús con el tema

`ContextMenu` y `MenuItem` tienen ahora estilo oscuro global, con la combinación de teclas a la derecha y los elementos desactivados atenuados. Así se ve también el menú de las tiendas, que antes salía con el aspecto claro de Windows.

### Pruebas

- `EditorConvenienceChecks` comprueba ahora el menú de cada tarjeta y que Pegar espere a que haya algo copiado, en lugar de los botones retirados. Copiar y pegar entre entrenadores y guardar siguen comprobándose igual.
- `NavigationRedesignChecks` suma comprobaciones de que Eliminar sube el equipo, de que Cortar deja el Pokémon listo para pegar, de una eliminación fuera del equipo y de las entradas del menú, con captura `trainer-card-menu.png`.
- Resultados: `--visual-redesign-only` 165, `--requested-ui-only` 55, `--all-ui-only` 408, `--synthetic-only` 4101, `--guidance-only` 233 y `--visual-audit` con 0 problemas.

## Resultados

Compilación Debug y Release de `3DS.Progressive.Tests` (incluye la app): 0 errores, 0 advertencias.

| Suite | Resultado |
|---|---|
| `--visual-redesign-only` | 135 correctas (antes 95; 40 nuevas) |
| `--guidance-only` | 233 correctas |
| `--interaction-lock-only` | 31 correctas |
| `--all-ui-only` | 408 correctas |
| `--synthetic-only` | 4101 correctas |
| `--swsh-only` / `--swsh-ui-only` / `--swsh-parity-only` | 316 / 117 / 239 correctas |
| `--nds-only` / `--nds-ui-only` | 161 / 15 correctas |
| `--visual-audit` | 588 vistas, 0 problemas, 77 avisos (antes 81) |

Los avisos que quedan son los de la auditoría anterior: herramientas que necesitan firmas de `code.bin` o archivos que la fixture no tiene. Durante el trabajo, la auditoría detectó tres problemas del rediseño, ya corregidos:

- «Herramientas Pokémon» no cabía junto a la barra de desplazamiento;
- «Randomizar Pokémon» no cabía en el Inspector estrecho a 1280×720;
- el texto de ejemplo se cortaba en las listas estrechas.

## Límites

- Todo se comprobó fuera de pantalla, con datos sintéticos. Faltan a mano:
  - el uso real con ratón, teclado y lector de pantalla del buscador y de las cabeceras plegables;
  - el escalado físico al 125-200 %;
  - la navegación con un proyecto real.
- El estado de backup y Rebuild de Inicio es solo de la sesión. Tras reiniciar la app indica «ninguno en esta sesión», aunque exista un backup anterior.
- La correspondencia entre historial y sección es aproximada en dos casos:
  - Game Text y Story Text comparten el módulo «Historia y textos», y se marca Game Text;
  - Shiny Rate y la tabla de tipos se registran como «Mecánicas y CRO», pero se reconocen por la acción y se marcan en Herramientas Pokémon.
- No se revisaron visualmente los espacios de Nintendo DS ni de Espada/Escudo.
