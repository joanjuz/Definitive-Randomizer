# Iteración 60 — integridad de recetas nativas y catálogo de movimientos

Trabajo local en feature/3ds-progressive-ui. Sin commit, push ni publicación. Se respaldaron los siete archivos intervenidos y se verificaron sus SHA-256 antes de aplicar los cambios.

## Cambios

- La finalización de recetas Gen7 exige snapshots de todas las acciones nativas seleccionadas: estáticos/iniciales, objetos del mapa, economía y Pokémon/movimientos. Una acción sin snapshot mantiene PartialSnapshot=true. Una receta marcada NativeOnly manualmente sin esos datos se rechaza durante la validación Batch.
- Se conserva la vía de recetas originales legacy; la nueva restricción corresponde a recetas declaradas nativas completas.
- MT y tutores Gen7 cuentan con overloads de semilla reproducible. La interfaz conserva su aleatorización habitual; los overloads permiten reproducción exacta y futuras integraciones Batch.
- Catálogo de selección y pool de MT/tutores limitados al mínimo entre nombres y registros reales de movimientos. La validación de tutores rechaza IDs presentes solo en el catálogo de nombres.

## Validación

- Debug local: 0 errores, 79 advertencias heredadas.
- Suite completa: 2779 regresiones sintéticas XY, ORAS, SM y USUM superadas. Nuevas pruebas cubren snapshots ausentes, marcadores de completitud inválidos, reproducción de semilla y límites del catálogo de MT, incluidos idiomas 0 y 6.
- git diff --check sin errores de whitespace; Git informa conversión LF/CRLF en archivos existentes.
- Los 12 archivos protegidos de Wild, pruebas y notas personales conservaron sus hashes.
- No se ejecutaron cambios sobre ROM reales ni pruebas en juego en esta iteración. Evidencias en ITERACION_60_PRUEBAS.

## Pendientes de cierre global

Esta corrección no termina la migración. La integración bidireccional y el replay avanzado de máquinas/tutores Gen7 y tiendas siguen pendientes; los overloads de semilla no equivalen a esa integración. También quedan perfiles separados de estáticos Gen6 y textos localizados, opciones especiales/catálogos restantes, compatibilidad con formatos antiguos, encoders gráficos adicionales y la matriz completa de contenedores/versiones y empaquetado. La validación en juego continúa pendiente y se realizará después del cierre de implementación.