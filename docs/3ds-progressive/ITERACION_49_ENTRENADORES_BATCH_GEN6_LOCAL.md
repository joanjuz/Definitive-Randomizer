# Iteración 49: entrenadores Gen6 en Batch

Batch Gen6 admite trainers.randomize además de sus 16 acciones anteriores. El ejecutor utiliza RSTE/TrainerRand originales, con archivos trdata/trpoke y ejecución al final para ver movimientos, aprendizaje y estadísticas efectivos. El flujo original continúa abriendo la configuración en edición manual; durante Batch consume RandSettings y el Trainer template activo sin mostrar el diálogo.

Se conserva la entrada original de randomización, que reinicia el estado de la operación y recoge controles y reglas Progressive. No se sustituye por un randomizador reducido. Global Template ya transporta controles TrainerRand y el template estructurado para XY/ORAS, y exige alcance Todos para Batch.

Validación específica: 6 comprobaciones ejecutan el motor empaquetado real sobre fixtures XY/ORAS. Verifican multiplicación de niveles por 2, especies conservadas al desactivar randomización y archivo personal intacto. --batch-gen6-only permite repetir esas pruebas sin repetir toda la batería. La batería completa mantiene las pruebas existentes de reglas avanzadas del host nativo; la prueba nueva de Batch cubre explícitamente configuración y ejecución de niveles/especies, no todas las combinaciones de reglas.

Pendientes: encuentros estáticos/regalos/iniciales Gen6 en Batch requieren proyectar fielmente las opciones del editor combinado y sincronizar selector de iniciales; no se habilitan acciones que puedan omitir tablas. También continúan pendientes las familias Wild, máquinas/MO, tutores y economía en Batch Gen6, y la generación completa de lotes y validación in-game. El editor manual existente conserva sus capacidades.

No hubo commits, push ni publicación. No se modificaron ROMs de referencia, archivos personales ni Eris-Ultranova.

Resultado final: compilación Debug con 0 errores/79 advertencias; 2188 regresiones completadas; 12 archivos protegidos preservados.
