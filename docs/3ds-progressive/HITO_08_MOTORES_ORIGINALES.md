# Hito 08 — Flujos originales, formatos reales y acciones reutilizables (v0.8.0)

Las capturas del usuario evidenciaron límites en las pruebas anteriores: el CRO sintético no representaba la tabla maestra relocalizada ni el aumento independiente del headroom; los encuentros estaban sin comprimir y el randomizador de entrenadores ofrecía solo tres opciones. Esta revisión usa las implementaciones Progressive existentes.

## Movimientos

El flujo de `MoveEditor7.ApplyBalancedMovePatches` aplica los instaladores una vez. Studio añadía una segunda instalación como validación, que no era válida tras mover la tabla. Se retiró esa operación; continúan las verificaciones de firmas de los instaladores y del guardado congelado. Los registros originales de movimientos siguen la relocación #755. Otros descriptores siguen el segmento `.rodata`; el tamaño final del archivo no indica ese desplazamiento porque el headroom de relocaciones crece por separado.

Giro Vil V2 conserva el payload ARM y su arreglo multigolpe previamente validado por el usuario en Ultra Sun y Ultra Moon. No se modifica el motor Core ni su instalador. Nuevas regresiones combinan los seis patchers con tabla maestra movida a código, registros originales liberados, tabla de relocaciones completa y un archivo cuyo crecimiento no es múltiplo de una página.

## Entrenadores

Se copiaron los flujos completos SMTE, RSTE y TrainerRand, sus schemas de templates, reglas, clasificación de entrenadores, progresión de historia USUM, picker Smart Items y picker Better Movesets. Los hosts originales trabajan sobre arrays clonados y nunca se muestran. Un adaptador ofrece sus controles en el inspector WPF y carga/exporta el mismo template JSON.

El adaptador redirige ejemplos/settings a una carpeta temporal; no escribe preferencias WinForms ni archivos del juego. Se usa un hilo STA cuando el llamador no lo es. El RNG se inicializa antes de construir/ejecutar el original. La propuesta filtra los resultados al alcance elegido y verifica las dependencias antes de ofrecer/aplicar. Los caches de movimientos se limpian entre ROMs. Los originales `trainer_held_items.txt` y `trainer_better_movesets.txt` se incluyen y se copian al espacio temporal.

Opciones: generaciones, legendarios/míticos, BST/smart/progressive, rangos manuales, formas y evolución final, tamaño y categorías de equipo, clases, IA, niveles y límites, combates dobles, movimientos/origen/potencia/STAB/daño fijo, Better Movesets y MT, Strong Stat, Smart Items por categoría/calidad, exclusión de objetos e Item Clause. Las reglas de entrenadores elegibles incluyen nivel/Ace dinámico, Max 3, Mega/Z, potencia, atacante mixto, estado y EV. Las opciones/reglas sobreviven a navegar mediante su captura JSON.

## Encuentros y evoluciones

El lector de `encdata` es `GARC.LazyGARC`, igual que el original. Los registros LZ11 se descomprimen para editar y se recomprimen solo si cambiaron. Se comparan los registros descomprimidos al verificar y se conservan byte a byte los miembros no editados. Esto cubre XY, ORAS, SM y USUM, manteniendo el espejo EN de ORAS y las franjas día/noche, SOS/clima.

El inspector de Evoluciones ofrece template incluido o CSV propio. Se reutilizan schema, aliases y semántica add/update del normalizador: conserva rutas originales, separa día/noche y objetos distintos, y limpia solo las dos equivocaciones históricas conocidas. La propuesta detecta cambios externos del CSV. Un record lleno no se sobrescribe silenciosamente. El CSV adjunto del usuario usa este mismo formato; puede elegirse desde **Elegir CSV**.

## Historial v2

Ediciones individuales: valores antes/después, clave y slot/ruta. Operaciones masivas: acción, opciones, alcance y reglas/template, sin resultados por Pokémon ni semilla fija. La política `FreshPerRun` indica que cada futura ROM debe randomizar de nuevo; `NotRandom` identifica acciones deterministas. Los hashes sirven de auditoría, no como datos que deban copiarse a otra ROM. Los historiales v1 se convierten al leer/exportar y se persisten al próximo guardado. Esto prepara Global Template; su replay/importación completa aún no está migrada.

## Validación

Regresiones sintéticas de las cuatro versiones, compresión y miembros intactos; CSV de evolución alternativo/idempotente/stale; seis mecánicas de combate y V2 intacto; historial de acciones sin resultados/semillas. Windows valida los hosts originales, propuestas y la navegación WPF. La validación en las ROMs del usuario sigue siendo necesaria para los nuevos ports; no se presenta la aplicación como paridad total.

Pendientes: Progressive Wild completo y templates específicos, asignaciones MT/tutores, tiendas/economía, otros encuentros, textos y automatización global. La rama estable y los proyectos Core/WinForms permanecen sin modificaciones.
