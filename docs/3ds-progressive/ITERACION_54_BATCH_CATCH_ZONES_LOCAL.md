# Iteración 54: Catch Zones en Batch Gen6

Batch Gen6 admite 32 acciones. Se incorpora catchzones.enable al preflight WPF, al ejecutor original y al puente nativo. Se utiliza el manifiesto incluido del juego, con las variantes y hashes ya verificados. No se aceptan snapshots CatchZones personalizados. La ruta del manifiesto se resuelve junto a la DLL de la aplicación, incluso si el puente se carga desde LegacyBatch.

El puente aplica Catch Zones antes de los demás módulos nativos, aunque la receta lo enumere después de Wild. Valida la compatibilidad de todos los módulos seleccionados antes de aplicar cambios y vuelve a crear los editores sobre los archivos efectivos. Wild obtiene así las tablas añadidas por Catch Zones y mantiene el espejo EN de ORAS. Los demás módulos conservan su orden relativo; agregar Catch Zones cambia la subsecuencia de semillas del lote de manera determinista.

La combinación real en Y detectó un rechazo al conservar una especie fuera del catálogo. ArchiveProjectEditor ahora conserva el slot Gen6 desconocido completo durante la vista previa: especie, forma y niveles permanecen iguales. Los slots reconocidos siguen pasando por la validación normal y la edición manual sigue rechazando especies nuevas fuera del catálogo. Este ajuste está limitado a encuentros Gen6; los doce archivos protegidos, incluido GameEncounterService, no se modifican.

18 checks dirigidos: preflight sin escritura, rechazo de origen desconocido antes de economía, Catch Zones primero aunque figure al final, aplicación de resultados exactos, idempotencia de preset y rechazo de snapshot personalizado; preservación de slot desconocido y cambios en slots reconocidos; nuevas especies inválidas rechazadas. Batería completa: 2300 regresiones XY/ORAS/SM/USUM. Debug: 0 errores y 79 advertencias.

La prueba opcional --check-catch-batch-real-copies crea carpetas nuevas separadas de las ROMs originales. Comprueba targets y miembros excluidos del parche; luego restaura solamente esa copia privada y ejecuta Catch + Wild niveles, deliberadamente con Wild primero en la receta. Compara los bytes finales con la propuesta manual, tablas añadidas, espejo EN, inventario y hashes originales. La primera ejecución encontró el rechazo de Y y se conservó su log de diagnóstico; la ejecución final usa copias nuevas.

## Límites

La comprobación combinada usa cambios de niveles con especies conservadas; no demuestra todas las combinaciones de randomización Wild. Catch Zones exige una fuente o target de hash reconocido: una extracción modificada después de aplicar el parche, incluyendo cambios Wild, puede requerir una fuente limpia compatible para crear un lote nuevo. No se eluden hashes ni se distribuyen binarios ROM.

Quedan pendientes otras acciones Gen6, tutores efectivos ORAS, Global completo y recetas antiguas, catálogos/plantillas personalizados, herramientas vanilla, UX/rendimiento, carga desde proceso original independiente, lotes reales completos hasta .3ds, extracción/rebuild, empaquetado y pruebas en juego. No se declara estabilidad completa.

Sin commits, push, publicación, cambios a Eris-Ultranova ni modificaciones a ROMs originales o archivos personales.
Resultado real final: X, Y, OR y AS pasaron Catch Zones + Wild niveles en copias privadas. Resultado byte a byte igual al flujo manual; tablas añadidas legibles y espejo EN ORAS sincronizado. En las cuatro referencias, hashes de los once archivos fuente e inventario completo originales intactos. Ver iteration54-real-final.log. Doce archivos protegidos intactos y git diff --check sin errores.
