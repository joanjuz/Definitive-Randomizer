# Iteración 46: Batch Gen6 y Global Template

Integración local en feature/3ds-progressive-ui. Sin commits, push ni publicación.

El panel Batch y el proceso original aislado admiten Generation 6 y 7 y verifican que la generación/juego de la plantilla coincida con el proyecto. Gen6 reutiliza cinco formularios originales, guarda sus GARC y recarga los datos correspondientes.

Acciones Gen6 integradas: rng.seed; moves.balance/randomize/metronome; personal.randomize/modify-all; pokemon-stats.apply; evolutions.randomize/every-level/remove-trade/normalize; level-up-moves.randomize/metronome; egg-moves.randomize. Se mantienen copias independientes, semillas y reconstrucción del flujo Batch existente.

Global Template permite reproducir instantáneas completas de movimientos huevo y evoluciones Gen6 con una acción compatible. Las instantáneas parciales o combinaciones sin representación fiel siguen rechazándose. El panel explica que reproduce acciones compatibles, sin prometer todos los módulos.

Validación: build Debug 0 errores y 79 advertencias; 2151 comprobaciones sintéticas XY/ORAS/SM/USUM. Las pruebas nuevas verifican igualdad de los conjuntos admitidos entre WPF y ejecutor empaquetado, existencia de métodos originales, aceptación de recetas XY/ORAS sin escritura y rechazo previo de acciones exclusivas Gen7. Los 12 archivos protegidos conservan su SHA-256.

Límites: estas pruebas no ejecutan una generación completa de ROM ni prueban en juego el resultado del nuevo ejecutor Gen6. No declaran paridad total. Todavía faltan en Batch Gen6 las familias de estáticos, entrenadores, Wild, máquinas/MO, tutores y economía, entre otras; esas acciones se rechazan antes del staging. Las funciones manuales existentes no se retiran. También permanecen pendientes los puntos enumerados en ITERACION_44_INSTALACIONES_Y_PENDIENTES_LOCAL.md, salvo la restauración guiada y el subconjunto Batch integrado después.

No se modificaron ROMs de referencia ni Eris-Ultranova. Evidencia en ITERACION_46_PRUEBAS.
