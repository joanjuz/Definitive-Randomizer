# Iteraciones 62–63 — iniciales localizados y recetas nativas Gen7

Trabajo local en feature/3ds-progressive-ui. Sin commits, push o publicación. No se declara completa ni estable toda la migración.

## Implementación

- Iniciales SM/USUM: nombres y tipos de selección sincronizados en los bancos disponibles de los diez idiomas. USUM también actualiza las entradas independientes de nombres. Preserva líneas y bancos ajenos y verifica el roundtrip de caracteres.
- La tabla de encuentros y los textos se guardan mediante la misma transacción. Los catálogos de nombres se incluyen en la comprobación de fingerprints sin modificarse. Una modificación externa impide el commit entero. Los guardados consecutivos actualizan todas las referencias.
- Se evita confundir nombres que son prefijos de otros, como Mew y Mewtwo.
- Shiny Gen7: la receta nativa completa y preflight admiten sus parámetros explícitos; validan la firma ARM antes del replay. El ejecutor nativo existente se conserva.
- Wild Gen7: Global guarda y restaura el template v1 y el conjunto íntegro de opciones, incluidos rangos dinámicos, SOS/clima, modo de slots y niveles. El replay usa las APIs del servicio Wild existente con semilla. Alcances parciales quedan disponibles en la interfaz y se rechazan para Batch.
- Al cambiar el número de rangos se reemplaza el conjunto de opciones Wild para impedir keys sobrantes. El modo de acción se sincroniza con los ajustes; las deselecciones se conservan al exportar.
- El snapshot nativo conserva los valores avanzados de niveles (1–500) en sus opciones; el template v1 auxiliar mantiene sus límites originales (10–300). Las opciones completas del snapshot gobiernan el replay.
- Pickup Gen7: acción nativa con semilla; modifica objetos y conserva probabilidades, sufijo del miembro y miembros ajenos. No acepta parámetros o snapshots personalizados que el replay no representa. La edición manual de filas/probabilidades conserva su guardado directo.
- Las recetas nativas Gen7 nuevas incorporan Wild y Pickup, además de MT, estáticos, campo, economía, shiny y los módulos USUM correspondientes.

## Preservación

Los archivos de lógica Wild, reglas SOS y selección no se modificaron. Once de los doce archivos protegidos coinciden con el baseline SHA-256. RecordEditorView.xaml.cs recibió una única extensión a su guarda de captura: también tolera InvalidDataException al generar el template auxiliar, conservando las opciones numéricas. Se verificó que el archivo entero coincide con el respaldo más ese hunk; los cambios anteriores del usuario permanecen íntegros. El baseline original no se sustituyó.

## Pruebas

- Debug final: 0 errores y 79 advertencias heredadas.
- Iteración 62: 2940 regresiones XY/ORAS/SM/USUM superadas. Iniciales localizados, nombres con prefijos, tipos SM, bancos y líneas ajenos, guardados consecutivos y conflicto externo antes del commit.
- USUM real: propuesta de iniciales con diez bancos de texto sobre copia independiente, sin Apply. Dos hashes principales y el inventario completo conservados.
- Iteración 63: 2960 regresiones completas superadas. Dieciséis específicas del bridge Wild y cuatro adicionales de Pickup nativo. Template tipado original, semilla, copy SOS, rangos dinámicos, metadata desconocida, nivel avanzado y rechazo de configuración incompleta.
- Los primeros intentos detectaron fixtures antiguos de tres iniciales con storytext de dos bancos. Se actualizaron únicamente fixtures no protegidos para representar bancos completos; no se debilitaron los controles del motor.
- git diff --check pasa. Evidencias en ITERACION_62_PRUEBAS e ITERACION_63_PRUEBAS.

## Pendientes reales

Todavía falta cerrar perfiles independientes de estáticos Gen6 y sus combinaciones, otras diferencias de módulos/formatos legacy, los formatos gráficos de escritura pendientes, la matriz completa de juegos/contenedores y el empaquetado candidato. La prueba USUM de iniciales fue de lectura/propuesta; no hubo nuevo rebuild de este cambio ni validación en juego. Wild/Pickup nuevos se verificaron con fixtures sintéticos, no con un lote real nuevo. La migración continúa pendiente de cierre global.