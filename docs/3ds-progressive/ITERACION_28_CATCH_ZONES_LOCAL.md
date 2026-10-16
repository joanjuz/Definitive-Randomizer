# Iteración 28 · Enable Catch Zones XY/ORAS integrado

Se incorpora la acción Progressive al apartado **Mecánicas y CRO**, con una pantalla oscura, vista previa obligatoria y guardado conjunto con historial. Se utilizan los manifiestos originales distribuidos en la iteración 27. Abrir el proyecto o esta pantalla no modifica ROMs. No se modifica el editor Wild protegido.

`GameCatchZoneService` resuelve los miembros de encdata/mapGR por sus nombres originales `000.bin` / `dec_000.bin`. Los miembros `dec_` se descomprimen como LZ11, reciben el parche sobre los bytes descomprimidos y se recomprimen con relectura exacta. Los otros miembros conservan sus bytes. Reconstruye cada GARC con sus referencias Core y admite los cambios de tamaño de los deltas. Los archivos raw de RomFS participan en la misma propuesta y transacción que ambos GARC.

La vista previa verifica SHA-256 de origen y destino según el manifiesto; el guardado vuelve a comprobar los archivos y el propio manifiesto. Se rechazan enlaces de filesystem, miembros inexistentes, dos nombres dirigidos al mismo miembro, colisión raw/GARC y GARC con múltiples subarchivos. No existe fallback que fuerce un parche sobre archivos incompatibles. Repetir la operación con todo aplicado conserva los contenedores completos y no añade entradas al historial.

La pantalla utiliza el tema oscuro para tabla/cabeceras/celdas y bloquea la navegación mientras prepara o guarda. Los cambios se muestran por miembro/archivo; su número puede ser mayor que el número de contenedores escritos.

## Validación local

Fixtures XY y ORAS con un miembro comprimido redimensionado, un mapa sin comprimir y un archivo raw verifican que la vista previa no escribe, la lectura posterior coincide exactamente, los miembros opacos ajenos sobreviven y los tres archivos se registran en una sola operación. También se comprueban idempotencia, modificaciones externas, cambio del manifiesto, nombre de compresión incorrecto, índices inexistentes, operaciones de eliminación rechazadas y propuesta de otro proyecto. Gen7 se rechaza.

Las pruebas WPF comprueban navegación, conservación de la instancia para el bloqueo de operación, botón de aplicar deshabilitado antes de la vista previa y tabla oscura. La captura y registros locales se guardan en `ITERACION_28_PRUEBAS/`.

## Límites y pendientes

Esta integración cubre el botón **Enable Catch Zones basado en manifiestos**. Las plantillas externas `CatchZoneTemplateSync` son una ruta distinta y siguen pendientes. Los manifiestos actuales no contienen eliminaciones; el servicio las rechaza antes de preparar cualquier cambio porque el historial actual solo sustituye archivos existentes. No se modifica esa limitación mediante escrituras separadas.

La validación del comportamiento en juego sigue pendiente. Los manifiestos son específicos de los hashes de las compilaciones usadas originalmente; un dump con cambios previos incompatibles se rechazará. No se ha aplicado esta acción a ROMs del usuario.

La migración completa continúa: opciones Static/entrenadores, intercambios Gen6, tutores/scripts/herramientas restantes, paridad exhaustiva de plantillas/Batch, rendimiento y preparación de entrega. No se solicita todavía validación real.

Se mantiene la rama `feature/3ds-progressive-ui`; sin commits, push ni publicación, y sin cambios en Eris-Ultranova ni archivos personales.

Resultado final: **1.481 comprobaciones aprobadas; Debug 0 errores / 22 advertencias existentes; 12/12 hashes protegidos conservados.**
