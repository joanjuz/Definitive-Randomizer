# Iteración 52: Sanity y Follow Evolutions de MT/MO Gen6

GameTmCompatibilityService admite XY/ORAS además de SM/USUM. Gen6 utiliza las asignaciones actuales del editor MT/MO y su orden de compatibilidad: 100 MT más 5 MO en XY o 7 en ORAS. Decodifica Personal y evoluciones con la estructura de cada generación. Sanity añade compatibilidad para movimientos aprendidos por nivel; Follow Evolutions propaga compatibilidad por cadenas, ramas y formas hasta alcanzar un punto fijo.

Solo se modifica Personal 0x28..0x37 y la tabla maestra correspondiente. Se conservan bits fuera de las máquinas activas, tutores, atributos y demás campos. La vista previa no escribe; Apply verifica las huellas de Personal, code.bin, learnsets y evoluciones, reconstruye/relee GARC y refresca la sesión. Las propuestas obsoletas y los conflictos externos se rechazan.

El panel nativo MT/MO Gen6 incorpora ambas opciones y botones de vista previa/aplicar. Conserva el editor de movimientos dentro del panel, sus opciones Global, su estado recordado y su guardia de navegación con cambios pendientes. La prueba WPF existente se adapta al contenedor y sigue comprobando la referencia efectiva del editor usado por esa guardia.

Batch Gen6 admite 31 acciones: se añaden tms.sanity y tms.follow-evolutions al puente nativo y al ejecutor original. Ambas pueden combinarse en el orden de la receta; cada acción se aplica a toda la tabla actual. Una receta con Sanity seguido de Follow produce el mismo resultado que la operación manual combinada. Operaciones idempotentes no generan escrituras nuevas. No se admiten snapshots personalizados de MachineCompatibility; estas acciones no representan alcance parcial. El guardado de opciones del panel de compatibilidad a Global desde la UI/historial sigue pendiente; una receta explícita ya puede ejecutarlas.

## Evidencia

22 comprobaciones dirigidas en fixtures XY/ORAS: vista previa sin escritura, MO heredada a través de dos evoluciones, bits de relleno y tutores conservados, tabla maestra, guardado exacto, idempotencia, propuesta obsoleta y dependencia modificada rechazadas, replay Batch equivalente e idempotente. Cuatro comprobaciones WPF adicionales verifican el panel y ambas opciones.

Lectura y vista previa de PokemonX, PokemonY, PokemonOR y PokemonAS: sin Apply. Inventario de archivos y bytes originales de Personal intactos. En cada referencia: Sanity +0, herencia +1, un registro afectado. Estas vistas previas no demuestran comportamiento in-game.

La tabla localizada por TutorEditor6 empieza en code.bin 0x4960F8 en las referencias OR y AS y coincide con los cuatro bancos vanilla. El editor antiguo advierte que sus cambios no se reflejan en juego. Localizar esa tabla no demuestra que editarla cambie los tutores efectivos: edición/randomización de movimientos de tutores ORAS y Sanity/Follow de tutores Gen6 siguen pendientes.

## Trabajo restante

Continúa abierto el inventario de la iteración 44: tutores efectivos, otras acciones Gen6, proyecciones completas Global/recetas antiguas, herramientas vanilla, plantillas personalizadas, UX/rendimiento, proceso Batch independiente y lotes reales completos, extracción/rebuild, empaquetado y pruebas in-game. La aplicación todavía no se declara estable completa.

Se preservan Wild protegido, archivos personales, Eris-Ultranova y las ROMs originales. Sin commits, push ni publicación.
Resultado final: Debug 0 errores y 79 advertencias; 2268 regresiones completadas; 12 archivos protegidos intactos. Captura mt-mo-gen6.png revisada: editor y controles WPF conservan el tema oscuro. git diff --check sin errores.
