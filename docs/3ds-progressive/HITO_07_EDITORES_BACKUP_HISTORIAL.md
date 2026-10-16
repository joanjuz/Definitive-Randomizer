# Hito 07 — Editores, backup e historial (v0.7.0)

## Template de movimientos

La captura del usuario identificaba un fallo en la verificación de segunda aplicación de los parches. Giro Vil V2 amplía el segmento de código; los segmentos posteriores y sus registros ya no conservan las direcciones del CRO original. Los adaptadores de layout remapean los registros de Meditación, Conjuro, Cerrojo Feérico y Deseo, y permiten el tamaño ampliado para Pesadilla/Conjuro solo cuando las firmas V2 y las relocaciones son válidas. Los hashes del CRO se recalculan tras completar los patchers.

No cambian las instrucciones, el payload ni el arreglo multigolpe de Giro Vil V2. La propuesta sigue exigiendo segunda aplicación byte a byte idempotente; se rechazan expansiones ajenas. Las regresiones combinan Pesadilla, Meditación ya instalada y Giro Vil V2 en un CRO sintético ampliado, y comprueban preservación de las instrucciones V2 y ausencia de cambios al reaplicar.

## Editores habilitados

| Sección | Edición disponible | Randomización disponible |
| --- | --- | --- |
| Entrenadores | Datos, objetos y seis slots de equipo; Gen 6 y Gen 7 según sus formatos | Especies, tolerancia BST y escala de niveles |
| Encuentros | Gen 6: hierba, agua, cañas, hordas; Gen 7: día/noche, SOS, clima, tasas | Especies, tolerancia BST, escala y opción SOS |
| Evoluciones | Ocho rutas, método, argumento y destino; forma/nivel Gen 7 | Destinos con tolerancia BST y conversión de intercambio a nivel |

Lista/búsqueda, inspector con acciones visibles, panel temporal en el centro, alcance todo/filtrado/selección, semillas nuevas automáticas y propuesta antes/después. Opciones y alcance sobreviven a cambiar de sección durante la sesión. Guardado individual y aplicación se confirman con diálogos oscuros. Los controles deshabilitados usan fondo #26374A, borde #3B4F65 y texto #8294AB.

Se conservan los campos desconocidos de cabeceras, padding y bits reservados de equipos. ORAS actualiza también el espejo EN de encuentros. Las tablas SOS y clima, prefijos EA y otra franja horaria permanecen iguales al editar solo una tabla Gen 7.

## Historial portable

`3ds-progressive-history.json` se escribe en la raíz del proyecto, fuera de RomFS/ExeFS. Abrir el proyecto o una vista previa no crea archivos. Cada guardado confirmado conserva un documento versión 1 con entradas ordenadas: identificador, fecha UTC, módulo/acción, valores anteriores/nuevos, parámetros, semilla/alcance y hashes SHA-256 antes/después. Los editores de registros incluyen clave de campo y grupo para identificar el slot de equipo, tabla o ruta de evolución. Los templates registran además la huella del CSV y las dependencias. El historial se consulta desde la navegación y se exporta como JSON.

El historial participa en el mismo `FileTransaction` que los archivos del juego. Los rechazos por datos inválidos, propuestas obsoletas o fallos de escritura no añaden acciones. Las operaciones sin cambios no añaden entradas. El modo de pruebas permite una carpeta de estado aislada mediante `PROGRESSIVE_STATE_DIR`.

Este registro sirve de base para mejorar el Global Template; todavía no importa ni reproduce el schema original de GlobalRandomizationTemplate, ni ofrece deshacer/reconstruir una ROM desde el historial.

## Backup manual

Botón **Generar backup** junto a **Abrir proyecto**, habilitado al cargar el juego. El selector permite elegir una carpeta de destino fuera del proyecto. El ZIP incluye todos los archivos de la extracción, manifiesto de tamaños/hashes y snapshot del historial. Se generan nombres únicos y se verifica cada fuente y entrada ZIP. Los fallos/cancelaciones eliminan el parcial. El servicio comunica progreso; la interfaz bloquea ediciones mientras trabaja.

El backup refleja los archivos guardados; las modificaciones aún pendientes en formularios no se incluyen. No es un servicio automático. El guardado de varios archivos conserva rollback verificado frente a fallos de IO, pero no garantiza atomicidad frente a apagados.

## Verificación y alcance

Datos sintéticos XY, ORAS, SM y USUM: formatos, lectura sin escritura, roundtrip, redimensionamiento de equipos, preservación binaria, rangos, scope, semillas distintas, cross-session/stale, historial portable/exportable, ZIP completo y cancelación, CRO V2 ampliado e idempotencia. WPF Windows: navegación a módulos reales, edición/descarte, propuesta en el centro, opciones recordadas y ocho renders de editores/paneles.

Prueba manual: extraer el ZIP completo; abrir una copia de Ultra Sol; generar backup; comprobar el template de movimientos y reaplicarlo; editar un entrenador y una tabla de encuentros; reabrir el proyecto y revisar Historial. Validar mecánicas y encuentros en juego.

Core, WinForms, templates incluidos y la rama estable permanecen sin cambios. Pendientes: Progressive Trainers/Wild completos, Smart Items, Better Movesets, templates específicos, MT/tutores, economía, textos, Global Template y Batch ROM Builder. Los editores nuevos no se presentan como paridad completa de esos sistemas.
