# Iteración 24 — MT/MO Gen6 y MO olvidables

Implementación local sobre `feature/3ds-progressive-ui`, sin commits, push ni release.

## Implementación

El botón MT y tutores abre ahora un editor MT/MO para XY/ORAS. La vista Gen7 y su servicio se conservan. El editor Gen6 utiliza la infraestructura WPF existente, con cambios pendientes, confirmación, vista previa de randomización e historial.

Se leen/escriben las 100 MT y 5 MO XY / 7 MO ORAS según el orden intercalado original: MT01–92, primeras MO, MT93–100 y MO finales ORAS. Se exige code.bin descomprimido y una firma de tabla única. Solo cambian los ushort de asignaciones seleccionadas.

El mismo guardado sincroniza las descripciones de objetos MT/MO con las descripciones de sus movimientos en el banco de textos del idioma seleccionado. Se verifica la codificación reversible y el GARC reconstruido; el resto de registros del banco se conserva. Código y texto participan en una sola operación del historial. Ambos archivos se comprueban antes de escribir, incluso cuando uno no tiene diferencias. Tras guardar se actualizan la caché de descripciones y las asignaciones MT que muestra el inspector.

La randomización utiliza los filtros vanilla: excluye Forcejeo y Cerco Dimensión, evita repetir los movimientos elegidos, conserva las MT de campo por defecto y mantiene las MO intactas por defecto. Se permite incluir movimientos de campo y randomizar MO mediante opciones separadas; esta última opción puede afectar a la progresión de la historia. La compatibilidad Pokémon original no se randomiza con esta acción.

MO olvidables se edita en el grupo Restricción MO. Reconoce la función ARM completa XY u ORAS, tanto original como parcheada, y cambia únicamente los primeros ocho bytes a `mov r0, #0; bx lr`. Desactivarlo restaura exactamente los ocho bytes originales. Se rechazan funciones duplicadas y se deshabilita la opción si no hay firma compatible. No se utiliza el fallback vanilla que acepta únicamente ocho bytes en un offset conocido.

## Validación

Compilación Debug y suite local completa, sin GitHub Actions. Se conservan logs finales, captura WPF y SHA-256 de los 12 archivos protegidos en `ITERACION_24_PRUEBAS/`.

Los fixtures XY/ORAS cubren orden intercalado, MO finales, bytes ajenos, descripciones con códigos de salto de línea, otro idioma (ORAS español), conservación de otros registros y líneas, caché/inspector, historial conjunto, idempotencia, pool y filtros, propuestas ajenas/obsoletas, cambios externos de código/texto, firmas ausentes/duplicadas, parche MO y restauración exacta, función desconocida y navegación WPF. Las regresiones anteriores de Gen7, Wild, Pickup, CRO y extracción siguen incluidas.

## Pendientes

La funcionalidad aún requiere validación en ROMs reales X/Y/OR/AS y en juego. No se ha terminado la paridad del conjunto MT/MO/tutores/tiendas Gen6: los tutores y tiendas necesitan su propia implementación, y la tabla TutorEditor6 vanilla por sí sola no acredita efecto en juego. No se migra todavía Sanity/Follow Evolutions Gen6 en este editor.

Siguen pendientes opciones exhaustivas de Static/entrenadores, otras herramientas vanilla, Global Template bidireccional, Batch integral, recuperación durable, rendimiento, extracción/rebuild en las ocho ROMs reales y empaquetado. No se solicita aún la validación final de la aplicación. Eris-Ultranova y cambios personales quedan fuera de esta iteración.

Resultado final: **1.176 comprobaciones aprobadas; Debug 0 errores / 22 advertencias existentes. Los 12 archivos protegidos conservan su SHA-256 original.**
