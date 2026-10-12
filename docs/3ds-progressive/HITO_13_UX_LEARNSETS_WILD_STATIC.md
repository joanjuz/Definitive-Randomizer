# Hito 13 — Densidad visual, dependencias y randomización avanzada

**Rama:** `feature/3ds-progressive-ui` (no merge). Comparado contra los formularios y los motores vanilla/Progressive existentes en el repositorio.

## Cambios implementados

- **Level Up Moves:** aprende/editá por nivel en tarjetas compactas de 1–4 columnas según espacio. Conserva movimientos individuales y permite añadir/quitar. Panel desplegable de `LearnsetRandomizer` original: Allow HMs (filtro Gen6), Bias by Type / STAB %, Expand Pool / cantidad, Start with 4, Spread Evenly / stop level, No Fixed Damage. `Randomizar TODOS` y `Metrónomo · Todos` generan resultados congelados sin modificar la ROM hasta confirmar y aplicar. Se valida SHA del archivo y usa historial + GARC de tamaño variable.
- **MT:** los 100 movimientos de MT se distribuyen en **tres tablas verticales editables** compartiendo los mismos `MachineMoveRow`; botón de randomización y guardado siguen usando code.bin verificado. Tutores BP sin cambio funcional.
- **Entrenadores:** ordenación y asociación de casilla/valor, con deshabilitación del dato cuando la casilla se apaga. Priorizar mejor estadística, Better Movesets y Smart Items presentan subopciones condicionales. Casilla de evolución forzada junto a su nivel; doble batalla junto al porcentaje, movimientos ofensivos/STAB con sus cantidades. Evita activar simultáneamente los modos de BST similares y progresivos.
- **Trainer Level Caps:** la etiqueta pasa a `Niveles de entrenadores`, aclarando explícitamente que los **Player Level Caps** son otro motor/parche y no están representados por esa tabla.
- **Wild:** `BST progresivo por nivel` se encuentra inmediatamente encima de su tabla y la esconde cuando está desmarcada. `SimilarBST` deshabilita la tolerancia si está desactivado. Nuevo `All Can Call Allies`: rellena únicamente slots SOS normales vacíos para especies presentes en encuentros normales; respeta los SOS ya definidos y las filas meteorológicas.
- **Static Encounters Gen7:** nuevas opciones con lógica real: generaciones 1–7, legendarios/eventos, reemplazo legendario por legendario, excluir legendarios de regalos/intercambios, restricción BST similar con su toggle, escala de niveles y ajuste plano opt-in, randomización de objetos (pool conservador), slots de habilidades en gifts/statics, movimiento especial en gifts, shiny locks y quitar locks, variar auras Totem existentes y forzar evoluciones finales por nivel. No presentar estas opciones como equivalentes al sistema de Totem BST y Totem Level Caps por entrada del original.
- **Indicador inferior:** barra de actividad común para abrir proyectos, navegar a secciones, backups, vistas previas y aplicación de propuestas de randomización. La generación global de learnsets utiliza preparación en segundo plano e impide navegar hasta finalizar.

## Pendientes explícitos de paridad

1. Player Level Caps para USUM: controles de 21 checkpoints, flags Event Work, templates, lectura/actualización real de Battle.cro y code.bin, `Sync Trainer Caps`. No confundir con límites de entrenadores.
2. Totem BST y Totem Level Caps por ID, Ultra Necrozma, Gift BST Baseline y los parches CRO / code específicos, sin tocar bloques ajenos.
3. Static Randomizer: auras Totem respetando clases, Mega Forms, starter de dos evoluciones, aceptación de cualquier Pokémon en intercambios y cambios de nombres de intercambios con sus parches verdaderos.
4. Wild: todos los sprites y paneles día/noche/SOS, plantillas avanzadas, Mega Forms / All Can Call Allies a nivel de todas las áreas por defecto.
5. Level Up Moves: paridad completa de la selección de movimientos de Gen6/7 bajo custom ROMs y sus casos de HM/movimientos prohibidos. Revisar output con originales en juego.
6. Probar en juego con ROMs extraídas reales de SM/USUM y probar modificaciones incompatibles antes de merge.

## Seguridad y pruebas

CI Windows compila el editor y corre regresiones sintéticas XY, ORAS, SM y USUM, incluyendo Metronome All Preview/Apply, guardado de texto/ítems y la interfaz WPF. Todas las acciones de escritura exigen preview y/o confirmación más huella de origen. El estado final de la rama es el de GitHub Actions y no equivale a validación runtime.
