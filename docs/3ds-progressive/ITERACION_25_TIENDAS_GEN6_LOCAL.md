# Iteración 25 — Tiendas XY/ORAS

Trabajo local en `feature/3ds-progressive-ui`, sin commits, push ni release. Las pruebas con ROMs reales XY, ORAS y USUM quedan para cuando termine toda la migración, según la indicación del usuario.

## Implementación

El botón Tiendas abre ahora el editor integrado Gen6: 26 tiendas XY y 24 tiendas ORAS, con nombres y tamaños del editor vanilla. Se conserva la vista estructural USUM. Los inventarios Gen6 tienen ranuras fijas; no se reubican tablas ni se permite cambiar su longitud.

Se reconoce una firma única `rom:/DllStartMenu.cro` o `rom2:/DllStartMenu.cro` desde 0x400000 y se valida la alineación descomprimida de code.bin, el tamaño total y todos los objetos contra el catálogo. La edición manual cambia solamente los ushort seleccionados y queda en el historial.

La randomización respeta el alcance elegido, permite solo tiendas especiales y conserva objetos X mediante una opción. Las ranuras que contienen MT/MO permanecen protegidas como en vanilla. El pool procede del randomizador original XY/ORAS; se baraja y reinicia al agotarse, evitando el acceso fuera de rango del contador original. Solo especiales conserva las primeras diez tiendas, siguiendo el corte de índice 10 de vanilla.

La opción Caramelos Raros sustituye únicamente la última ranura de las tiendas regulares elegidas (9 XY y 10 ORAS, incluida la tienda previa a la Pokédex en ORAS). Actualiza el precio del objeto a 10 usando las unidades de diez del formato Item. Conserva el resto de bytes del objeto y todos los otros registros. Inventarios y precio se preparan sin escribir, se confirman, verifican y guardan en una sola operación del historial. Se comprueban también archivos dependientes sin cambios; modificar externamente el GARC de objetos invalida la propuesta aunque el precio ya sea 10.

La pantalla reutiliza el editor WPF oscuro y sus protecciones de cambios pendientes. Al cambiar de proyecto o abrir tiendas Gen6 se descarta la referencia a la vista USUM anterior, evitando un guardado/protección de navegación asociado al proyecto equivocado. Los parámetros de historial conservan los IDs de acciones vanilla y sus opciones `price`, `specialOnly` y `keepXItems`; esto no completa la integración bidireccional Global Template.

## Pruebas

Compilación Debug y suite completa local, sin GitHub Actions. Fixtures XY/ORAS con ambos marcadores de code.bin, edición de la última tienda, bytes ajenos, scope, pool, exclusiones, historial, propuestas ajenas/obsoletas, precio exacto y bytes opacos, repetición idempotente, conflictos externos en código/objetos y entradas truncadas/duplicadas. La navegación Gen6 y el tema se revisan mediante captura WPF.

Los logs finales, captura y SHA-256 de los 12 archivos protegidos se conservan en `ITERACION_25_PRUEBAS/`. Wild y archivos personales se preservan; Eris-Ultranova no se modifica.

## Pendientes

No se declara terminada la migración. Siguen pendientes los tutores Gen6 y su efecto real, Sanity/Follow Gen6, Field Items/scripts, opciones exhaustivas Static/entrenadores, herramientas vanilla restantes, Global Template bidireccional/Batch integral, recuperación durable, rendimiento y empaquetado final. Las tiendas SM tampoco tienen aún editor equivalente a la vista USUM. La validación real de juegos y extracción/rebuild se realizará al terminar la migración; no se solicita ahora.

Resultado final: **1.290 comprobaciones aprobadas; Debug 0 errores / 22 advertencias existentes. Los 12 archivos protegidos conservan su SHA-256 original.**
