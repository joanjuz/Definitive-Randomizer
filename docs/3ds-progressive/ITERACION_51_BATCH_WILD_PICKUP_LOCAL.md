# Iteración 51: Batch Pickup y Wild Gen6

Batch Gen6 admite 29 acciones: se suman pickup.randomize, wild-encounters.randomize, wild-encounters.modify-levels y wild-encounters.scale-levels. El motor nativo opera sobre el staging del ejecutor original, conserva ProgressiveApp durante la serialización y recarga la configuración antes de entrenadores.

Pickup usa el pool legal original y una semilla explícita; conserva todos los bytes fuera de sus 29 identificadores. No representa tablas personalizadas y rechaza snapshots Pickup. Wild usa el servicio existente y el orden físico/lógico ya verificado de GARC; mantiene la sincronización EN de ORAS y la escritura transaccional. El Global Template permite guardar/cargar opciones Wild Gen6 con alcance Todos para replay. Se requiere una sola operación por módulo. Las recetas antiguas Wild sin snapshot nativo completo requieren actualizar su configuración desde el editor.

Las acciones de niveles conservan especies/formas y exigen ModifyLevel activo. El modo BST progresivo de esta vista sigue siendo Gen7: se rechaza explícitamente en este puente Gen6. La sobrecarga interna de ArchiveProjectEditor permite semilla explícita; el flujo manual sigue generando semillas aleatorias. Los doce archivos protegidos no se modificaron.

Validación: 20 comprobaciones nuevas XY/ORAS, sin contar duplicados: reproducción por semilla tras serializar por el tipo original, objetos legales, bytes ajenos, niveles con multiplicador y suma, especies/formas conservadas y rechazos sin escrituras. Debug: 0 errores y 79 advertencias. Batería completa: 2242 comprobaciones sintéticas XY/ORAS/SM/USUM. No constituye validación in-game ni ejecución completa de lote real.

## Pendientes para estabilidad completa

- Tutores Gen6: TutorEditor6.cs:24 advierte literalmente que sus cambios no se reflejan en juego. Auditar tablas efectivas de ORAS y compatibilidad antes de migrar la edición de movimientos; la compatibilidad de tutores en Personal es un módulo distinto.
- Batch: demás acciones Gen6, recetas antiguas, varias operaciones del mismo módulo, carga del puente desde proceso original independiente y lotes completos hasta .3ds con referencias reales.
- Herramientas y cierre de paridad: consultar el inventario de iteración 44 para restauración selectiva, herramientas vanilla, plantillas personalizadas, texto de estáticos, UX/rendimiento y distribución.
- Validación con ROMs reales de variantes compatibles, rebuild/extracción, juego y empaquetado de release. No se declara versión estable completa.

Sin commits, push, publicación ni modificaciones a Eris-Ultranova, archivos personales o ROMs de referencia.