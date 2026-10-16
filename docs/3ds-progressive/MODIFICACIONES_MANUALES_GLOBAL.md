# Reproducción general de modificaciones manuales

Código fuente actualizado el 15/10/2026. La distribución permanece sin actualizar.

## Funcionamiento

Guardar una modificación real en un editor conserva su contenido reproducible en el mismo historial transaccional que protege el archivo del juego. Global Template incorpora ese contenido al crear desde historial o al guardar una receta cargada. Se mantiene una acción seleccionable: «Modificaciones manuales · aplicar entradas y recursos al final».

En cada copia de Batch se ejecutan primero las acciones habituales y luego las modificaciones manuales, antes del rebuild. Un Pokémon, movimiento o campo de entrenador modificado recibe el valor explícito, independientemente de la semilla. Los campos no modificados conservan el resultado aleatorio. Varias ediciones de una entrada acumulan sus campos; si un campo se edita varias veces, prevalece su último valor manual.

En equipos se preservan las dependencias estructurales: tamaño del equipo, datos necesarios para Pokémon añadidos y flags que habilitan movimientos/objetos personalizados en Gen6. No se sobrescriben otros entrenadores. Las listas que se editan como una unidad —aprendizajes, movimientos huevo y listas ampliadas— conservan su configuración válida y su orden.

Los botones y opciones que generan datos aleatorios se mantienen separados de las celdas editadas a mano en tiendas, MT, tutores y Pickup. La ejecución de Batch marca su contexto para que sus resultados aleatorios no se registren de nuevo como modificaciones fijas.

## Cobertura

| Sección | Contenido reproducible |
| --- | --- |
| Pokémon | Propiedades editadas y compatibilidades guardadas |
| Entrenadores | Clase, equipo, especies, formas, movimientos, niveles, objetos, IA, IV/EV y demás campos disponibles |
| Encuentros | Campos de las tablas editadas en Gen6/Gen7 |
| Estáticos, regalos, iniciales e intercambios | Entradas de los editores correspondientes, respetando la generación |
| Movimientos | Propiedades modificadas; las demás siguen pudiendo randomizarse |
| Aprendizajes y movimientos huevo | Listas y datos guardados por especie/forma |
| Evoluciones y megaevoluciones | Métodos, condiciones y destinos editados |
| Objetos y field items | Propiedades y referencias editadas |
| MT/MO y tutores | Movimientos, precios y flags concretos añadidos por Sanity/Follow |
| Tiendas | Ediciones manuales en inventarios y BP; admite ampliaciones verificadas |
| O-Powers y Persistent Items | Valores/mecanismos expuestos por sus editores compatibles |
| Maison, Super Maison, Battle Tree y Battle Royal | Entrenadores y Pokémon de las instalaciones |
| Tabla de tipos y Shiny | Configuración guardada, incluido restaurar Shiny |
| Player Level Caps | Tabla y condiciones guardadas en USUM |
| Títulos, vídeos y SMDH | Reemplazos de recursos y metadatos |
| Recetas CRO | Cambios de recursos verificados; las incompatibilidades se notifican |
| Integridad CRO/CRR | Recalcular sobre el resultado final, no copiar hashes obsoletos |

Los 17 tipos concretos que implementan IProjectEditor están registrados. Los editores de tablas/listas y recursos emplean adaptadores adicionales. Las preferencias de la aplicación, rutas de salida, exportaciones, backups y extracción no se convierten en modificaciones de la ROM. Un GARC Shuffler sigue siendo una operación aleatoria, no una edición manual.

## Uso y dependencias

1. Guardar los cambios en el proyecto.
2. Crear la receta desde historial o guardar una receta previamente cargada en un archivo nuevo.
3. Mantener seleccionadas las modificaciones manuales deseadas y elegir esa receta en Batch.
4. Si aparece una carpeta `nombre-del-template.assets`, conservarla junto al JSON al trasladarlo.

Los recursos grandes se conservan como dependencias inmutables verificadas por SHA-256. No se incrustan ROMs completas en JSON ni se copian archivos enteros para fijar un simple campo de entrenador. En GARC se reemplazan miembros concretos y se preservan los demás. Para cambios binarios sin adaptador de campos se verifican tamaño y bytes esperados; un conflicto detiene el lote en lugar de escribir sobre una versión desconocida. La normalización de `code.bin` a `.code.bin` de Batch no crea archivos duplicados.

El almacén `manual-assets` del historial contiene dependencias persistentes; no es una carpeta temporal desechable. El historial de proyectos generados puede reexportar recursos mientras sus dependencias sigan disponibles. Guardar una receta copia sus recursos necesarios a su propia carpeta `.assets`.

## Historial antiguo

Las ediciones anteriores de Pokémon y textos que incluyen datos suficientes siguen siendo compatibles. En otros editores, algunos historiales antiguos guardaban solo nombres, resúmenes o hashes. Un hash no permite reconstruir el contenido reemplazado. Esos casos se señalan y bloquean una receta que solicite reproducirlos; no se omiten silenciosamente. Requieren volver a editar y guardar las entradas o recursos afectados en esta implementación. Un guardado sin cambios conserva el comportamiento previo: no crea una edición ficticia.

## Formato y seguridad

Se conserva Version=1 / ActionCoverageVersion=8 en el documento global. La extensión ProgressiveApp.ManualEdits usa Version=2 y conserva lectura de la versión 1 anterior. Los motores antiguos deben rechazar la nueva extensión, evitando aplicar una receta a medias.

El replay solo usa editores registrados, IDs y campos válidos y rutas dentro del proyecto. Rechaza recorridos `..`, enlaces de destino, dependencias alteradas y clases arbitrarias. Las modificaciones están vinculadas a la familia y generación; una receta ANY queda vinculada al proyecto al añadirlas. Los idiomas registrados se restauran temporalmente y no cambian la preferencia del usuario.

## Validación y límites de esta entrega

Compilación Debug con UseAppHost=false: sin ejecutable actualizado ni modificación de la distribución. Pruebas en fixtures aislados de XY, ORAS, SM y USUM: entrenadores con equipos diferentes, acumulación de campos, conservación de clase/tipos no editados, evoluciones, encuentros, huevos, instalaciones, estáticos, field items, flags de compatibilidad, aprendizajes, recursos, dependencias, reexportación, rutas y conflictos. Se comprueba también la recuperación transaccional y los editores existentes.

La batería general antigua se detiene en TemplateChecks por una expectativa de activar parches de combate por defecto que no corresponde a la configuración actual. La comprobación de presencia del ejecutable de GlobalEditorBridgeChecks tampoco pasa en el build sin apphost solicitado. No se reactivaron parches de combate ni se generó un EXE para eludir esas comprobaciones. La validación in-game y una nueva prueba de empaquetado/rebuild completo siguen pendientes para cuando se autorice construir la distribución.

Distribución conservada: SHA-256 de 3DS.Progressive.dll = 644DD5B2A4B374D8BCED12FBBD06D2400F7B9F794F39EF1ECF1180BA48572D3B. No se controló el escritorio, ni se tocaron ROMs originales, ni se hicieron commits o push.

Resultados finales aprobados: 113 comprobaciones generales de replay manual; 100 de textos/estadísticas exactas; 408 de editores y recuperación; 32 de compatibilidad MT/MO Gen6; 56 de MT y descripciones Gen7; 45 de lotes de estadísticas/movimientos; 54 del motor Batch Gen6; 48 de tutores ORAS; 48 de tiendas SM. Total: 904 comprobaciones dirigidas. Las dos limitaciones de la batería general y del empaquetado descritas arriba siguen pendientes.

## MT predeterminadas en recetas Gen7 — 16/10/2026

La casilla «Randomizar MT en Global/Batch» se elimina. Al capturar una receta desde la aplicación, la randomización de MT se incluye por defecto también si no se abrió el editor. Las opciones de compatibilidad y tutores se conservan. Los documentos antiguos mantienen sus acciones al cargarse o ejecutarse; guardar de nuevo desde la aplicación incorpora el nuevo valor predeterminado. Sigue siendo posible excluir una acción en la selección de Global Template.

Para fijar una MT en todas las ROMs, editar su movimiento y guardar MT. El historial conserva únicamente las asignaciones manuales; su reproducción final prevalece sobre la randomización sin fijar las demás MT.

Validación: build Release de App y Tests sin errores (90 advertencias existentes); --tm7-text-only: 76 comprobaciones; --tm-default-ui-only: 32 comprobaciones de componentes WPF fuera de pantalla. Incluye SM/USUM, captura sin abrir editor, recuperación de opciones antiguas, ausencia de escrituras al capturar, y prioridad de MT manual con dos semillas. No se hizo una prueba interactiva ni se publicó un ejecutable actualizado.
