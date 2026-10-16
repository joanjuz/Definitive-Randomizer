# Iteración 38 — Definitive Randomizer y perfiles Progressive

Fecha: 2026-10-13. Rama local: `feature/3ds-progressive-ui`.

## Nombre elegido

El usuario eligió **Definitive Randomizer**, sin «3DS» en la marca. Se actualizan ventana principal, cabecera, monograma DR, pie de versión, editor de movimientos, diálogo base, acerca de, reportes, nombre predeterminado de Global Template y metadatos Product/Title. Se mantienen ensamblados internos, espacios de nombres, rutas de preferencias, cachés, historial y esquemas de plantillas para conservar compatibilidad. Las menciones a pk3DS Progressive como procedencia del motor o de sus parches permanecen.

El cambio de marca no añade todavía compatibilidad con otras plataformas.

## Funciones incorporadas

- Iniciales Gen7: comprobar el movimiento especial y los cuatro movimientos naturales según especie, forma y nivel resultantes. Un movimiento especial de estado puede desplazar el único ataque; se sustituye por un movimiento natural dañino. Si no existe, se usa Placaje solo cuando su potencia activa es positiva; en otro caso se busca otro ataque. Rechazo si la tabla activa no contiene ninguno. Regla basada en `Power > 0`, como la implementación Progressive original.
- Objetos: usar el catálogo completo del randomizador Progressive en regalos, estáticos e intercambios, en lugar del antiguo subconjunto de trece objetos.
- Habilidades: estáticos utilizan slots 1–3; regalos e intercambios 0–2. Los intercambios ahora participan en la opción.
- Movimientos especiales: catálogo completo de movimientos activo; excluir Z-Moves y los IDs 165, 464 y 621. Conservar 166 y 226, antes excluidos por error.
- Aura Totem: cambiar la estadística conservando la intensidad +1/+2/+3; conservar aumentos de todas las estadísticas y valores desconocidos. USUM restringe la operación a las entradas conocidas de jefes, independientemente de su especie randomizada; SM conserva el criterio original de aura presente.
- Estáticos Gen7: exponer IV, EV, mapa y dos aliados; guardar por los offsets originales. Intercambios: exponer IV. Al randomizar especies se restauran naturaleza, género e IV/EV como en Progressive, conservando el marcador de tres IV perfectos; regenerar movimientos recordables desde el learnset final. Metrónomo conserva su tratamiento específico. Los bytes ajenos a los campos editados permanecen.
- Intercambios Gen7: randomizar también la especie solicitada utilizando el filtro de Pokémon recibidos. Conservar solicitud cero cuando Accept Any ya está aplicado.
- Force Final: evaluar el nivel resultante y utilizar el catálogo de evoluciones finales con legendarios/míticos permitidos, conservando la restricción para Pokémon recibidos. Los primeros tres regalos siguen el perfil de iniciales.
- Iniciales Gen6: perfiles de iniciales básicos y generación del grupo original. XY: generaciones 6 y 1; ORAS: 3, 2, 4 y 5. Pools explícitos independientes de las casillas normales de generaciones; excluir Shedinja. Se mantienen la sincronización atómica DllField/DllPoke3Select y el historial conjunto. Snapshot nativo Global conserva estas opciones.

## Verificación local

- Debug: 0 errores; 79 advertencias preexistentes. Sin GitHub Actions.
- 1.754 comprobaciones sintéticas XY/ORAS/SM/USUM, incluida navegación WPF, Global Template y regresiones de Wild/Catch Zones.
- Lectura y preview reales, **sin Apply**: X/Y 32 entradas por juego; OR/AS 96; Ultra Sol/Ultra Luna 294. Hashes de tablas monitorizadas y el inventario completo de cada carpeta permanecen intactos. Esto no prueba la aplicación del parche ni el comportamiento en combate.
- La ruta antigua `D:\Nintendo\3DS\Juegos\Pokemon` ya no existe. El primer diagnóstico finalizó con ese error después de comprobar los cuatro Gen6; se conserva el log completo y se comprobó después la ubicación actual de ambas referencias USUM.
- Los mensajes «Wrote encounterstatic» del empaquetador durante el preview USUM se refieren a la reconstrucción en memoria: hashes e inventario de disco antes/después son idénticos.
- 12 archivos protegidos de modificaciones previas Wild y documentación personal conservan SHA-256. Sin cambios en Eris-Ultranova, archivos personales ni ROM originales. Sin commits, push ni publicación.
- Captura WPF revisada: marca DR y Definitive Randomizer encajan en la cabecera oscura; no se cambia el estilo general.

## Pendientes para declarar paridad completa y versión estable

Esta iteración no cierra toda la migración. La matriz contiene filas históricas parcialmente duplicadas; se corrigen estados obsoletos de extracción, CRO y Type Chart/Shiny Gen6, sin presentar las pruebas locales como validación en juego.

1. Global Template bidireccional para todos los módulos. Actualmente sincroniza entrenadores y opciones nativas de estáticos; falta reproducir los controles separados originales de Static/Gift/Starter Gen6 y las opciones WPF de los otros módulos.
2. Batch ROM de extremo a extremo: la extensión nativa parcial sigue rechazada para evitar omitir opciones. Falta integración completa y pruebas de un lote real con reconstrucción.
3. Estáticos: espejo localizado de textos de iniciales; comparación completa de los tres perfiles Gen6, regalos con megapiedras y gestos separados de starters/randomize-all. La escala de nivel y el filtro BST nativos conservan sus opciones existentes y no se declara equivalencia exacta de cada semilla con WinForms.
4. Field Items Gen6: codec disponible; faltan descubrimiento de slots, editor, pools y operación conjunta de scripts/código ORAS. Tutores y Sanity/Follow Gen6 pendientes.
5. Entrenadores: concluir auditoría de Level Caps/Move Rules en todos los perfiles y Battle Maison/Royal/Tree.
6. Herramientas pendientes de la matriz: Restore Original Files guiado, icono/SMDH/BCLIM, GARC Shuffler, Title Screen Gen6 y randomización protegida de Story Text. Plantillas externas Catch Zones deben seguir el formato sin binarios de ROM.
7. UX/rendimiento de tareas prolongadas y recuperación durable ante cierre inesperado. Extracción/rebuild requieren muestras reales de formatos y revisión completa.
8. Validación final de las ocho versiones y pruebas en juego de capturas, colisiones, eventos, combates y progreso. Preparar paquete local y documentación de release después; publicar requiere autorización.

No se solicita validación en juego todavía: falta trabajo de implementación.
