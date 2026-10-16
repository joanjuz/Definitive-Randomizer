# Iteración 29 · Intercambios Progressive XY/ORAS

Se incorpora **Intercambios Progressive · Gen6** en Mecánicas y CRO mediante el editor WPF integrado. El guardado de la tabla habilita aceptar cualquier Pokémon y permite editar las ofertas. La vista previa permite habilitar sin randomizar o habilitar con ofertas randomizadas. El grupo de ofertas avisa explícitamente que guardar habilita el parche; las solicitudes y el estado del selector se muestran como valores de solo lectura.

## Perfil original y preservación

Se migran los offsets y bytes de `pk3DS.WinForms/Misc/Gen6TradePatcher.cs`: tres solicitudes XY/ORAS, tres ofertas XY y cuatro ORAS. En ORAS también se instalan los dos parches del selector PC en `0x3196C8` y `0x319F38`. Solicitudes y selector se preparan y guardan juntos en code.bin con historial y relectura verificada.

Se admiten solicitudes originales del perfil o ya puestas a cero y selectores originales o ya parcheados. Se rechazan solicitudes y selectores incompatibles antes de escribir. El binario debe estar descomprimido, tener longitud múltiplo de 512 y contener los offsets requeridos. Estas comprobaciones son las firmas locales del perfil conocido, no una identificación exhaustiva de todas las revisiones del juego; sigue siendo necesaria la validación real posterior.

Las ofertas usan el mismo pool Core y las mismas exclusiones del patcher Progressive: generaciones 1–6, sin legendarios, eventos ni Shedinja; sin restricciones BST/EXP/tipo. El barajado cíclico utiliza un Random local y consume candidatos iguales a la especie anterior hasta obtener una distinta. La secuencia aleatoria no se declara idéntica a la del RNG global de WinForms. Las ofertas originales cero o fuera del catálogo se conservan al randomizar; una oferta nueva desconocida se rechaza. Los bytes restantes del código no cambian.

La operación repetida sin randomización ni edición es idempotente y no añade historial. Los cambios externos invalidan las propuestas. No se añade una restauración genérica: habilitar y editar son las operaciones migradas del patcher original.

## Pruebas y pendientes

Fixtures XY y ORAS verifican vista previa sin escrituras, bytes exactos de solicitudes y selector, ofertas manuales, pool legal, exclusión de la especie previa, conservación del resto del código, entradas únicas de historial, idempotencia, ofertas vacías/desconocidas y rechazo de propuestas ajenas, modificaciones externas, alcances/opciones inválidos, firmas incompatibles y binarios truncados. Se verifica navegación WPF y conservación de la instancia del editor para su protección de cambios pendientes. Se conserva una captura revisada de la pantalla.

Las evidencias de Debug, regresión y SHA-256 de los 12 archivos protegidos se guardan en `ITERACION_29_PRUEBAS/`. La validación con ROMs XY/ORAS sigue pendiente y se reserva para el cierre general acordado.

Continúan pendientes otras opciones exhaustivas de Static/entrenadores, tutores/scripts/herramientas Gen6, plantillas externas Catch Zones, Global Template/Batch integral, rendimiento y preparación de entrega. Esta iteración no declara terminada la migración completa.

Rama `feature/3ds-progressive-ui`. Sin commits, push ni publicación; sin cambios en Eris-Ultranova, archivos personales ni los archivos protegidos de Wild.

Resultado final: **1.522 comprobaciones aprobadas; Debug 0 errores / 22 advertencias existentes; 12/12 hashes protegidos conservados.**
